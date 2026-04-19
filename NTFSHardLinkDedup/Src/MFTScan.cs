using System;
using System.Buffers;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace NTFSHardLinkDedup.Src;

public sealed class MFTScan : IDisposable
{
    private const ulong RootFrn = 5;
    private const ulong FrnMask = 0x0000FFFFFFFFFFFFUL;

    private readonly char _driveLetter;
    private readonly string _driveRoot;

    private readonly StatsState _stats = new();

    private int _scanStarted;
    private int _scanFinished;
    private int _disposed;

    public MFTScan(char drvletter)
    {
        drvletter = char.ToUpperInvariant(drvletter);

        if (drvletter is < 'A' or > 'Z')
            throw new ArgumentOutOfRangeException(nameof(drvletter), "Drive letter must be A-Z.");

        string root = drvletter + @":\";
        if (!Directory.Exists(root))
            throw new DriveNotFoundException($"Drive '{drvletter}:' does not exist or is not ready.");

        _driveLetter = drvletter;
        _driveRoot = root;
    }

    /// <summary>
    /// 每秒推送一次统计快照。外部不再需要时请 -= 解绑。
    /// </summary>
    public event Action<ScanStats>? StatsUpdated;

    public async Task<ScanResult> Scan(Func<bool>? isCancel = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (Interlocked.Exchange(ref _scanStarted, 1) != 0)
            throw new InvalidOperationException("This MFTScan instance can only scan once.");

        using SafeFileHandle volumeHandle = OpenVolume(_driveLetter);
        VolumeInfo volumeInfo = GetVolumeInfo(volumeHandle, _driveRoot);

        List<Extent> mftExtents = GetMftExtentsFromVolume(volumeHandle, volumeInfo, isCancel, cancellationToken);

        using var statsPumpCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stopwatch = Stopwatch.StartNew();

        Task statsPumpTask = RunStatsPump(
            elapsedProvider: () => stopwatch.Elapsed,
            isCancel: isCancel,
            cancellationToken: statsPumpCts.Token);

        try
        {
            Dictionary<ulong, FileNode> nodes = await Task.Run(
                () => ReadAndParseMft(volumeHandle, volumeInfo, mftExtents, isCancel, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            List<Entry> entries = await Task.Run(
                () => BuildEntries(nodes, isCancel, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();
            Interlocked.Exchange(ref _scanFinished, 1);

            var finalStats = SnapshotStats(
                elapsed: stopwatch.Elapsed,
                completed: true,
                canceled: false);

            StatsUpdated?.Invoke(finalStats);

            return new ScanResult(entries, finalStats);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            Interlocked.Exchange(ref _scanFinished, 1);

            var finalStats = SnapshotStats(
                elapsed: stopwatch.Elapsed,
                completed: false,
                canceled: true);

            StatsUpdated?.Invoke(finalStats);
            throw;
        }
        finally
        {
            statsPumpCts.Cancel();

            try
            {
                await statsPumpTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            Interlocked.Exchange(ref _scanFinished, 1);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        StatsUpdated = null;
    }

    private Task RunStatsPump(
        Func<TimeSpan> elapsedProvider,
        Func<bool>? isCancel,
        CancellationToken cancellationToken)
    {
        return Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                StatsUpdated?.Invoke(SnapshotStats(
                    elapsed: elapsedProvider(),
                    completed: Volatile.Read(ref _scanFinished) != 0,
                    canceled: IsCanceled(isCancel)));

                try
                {
                    await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (Volatile.Read(ref _scanFinished) != 0)
                    break;
            }
        }, CancellationToken.None);
    }

    private Dictionary<ulong, FileNode> ReadAndParseMft(
        SafeFileHandle volumeHandle,
        VolumeInfo volumeInfo,
        List<Extent> mftExtents,
        Func<bool>? isCancel,
        CancellationToken cancellationToken)
    {
        ThrowIfCanceled(isCancel, cancellationToken);

        int recordSize = checked((int)volumeInfo.BytesPerFileRecordSegment);
        int bytesPerCluster = checked((int)volumeInfo.BytesPerCluster);
        int bytesPerSector = checked((int)volumeInfo.BytesPerSector);

        if (recordSize <= 0 || bytesPerCluster <= 0 || bytesPerSector <= 0)
            throw new InvalidOperationException("Invalid NTFS geometry.");

        long mftBytesRemaining = volumeInfo.MftValidDataLength;
        long mftStreamOffset = 0;

        int chunkSize = Math.Max(4 * 1024 * 1024, recordSize * 1024);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(chunkSize + recordSize);
        int carryBytes = 0;

        try
        {
            var nodes = new Dictionary<ulong, FileNode>(1_000_000);

            foreach (Extent extent in mftExtents)
            {
                ThrowIfCanceled(isCancel, cancellationToken);

                if (mftBytesRemaining <= 0)
                    break;

                long clusters = extent.NextVcn - extent.StartVcn;
                if (clusters <= 0)
                    continue;

                long extentBytes = clusters * volumeInfo.BytesPerCluster;
                if (extentBytes > mftBytesRemaining)
                    extentBytes = mftBytesRemaining;

                if (extent.Lcn < 0)
                {
                    // 理论上 $MFT 不应出现稀疏区，这里防御性跳过。
                    mftStreamOffset += extentBytes;
                    mftBytesRemaining -= extentBytes;
                    carryBytes = 0;
                    continue;
                }

                long extentVolumeOffset = extent.Lcn * volumeInfo.BytesPerCluster;
                long extentRead = 0;

                while (extentRead < extentBytes)
                {
                    ThrowIfCanceled(isCancel, cancellationToken);

                    int desired = (int)Math.Min(chunkSize, extentBytes - extentRead);
                    int read = RandomAccess.Read(
                        volumeHandle,
                        buffer.AsSpan(carryBytes, desired),
                        extentVolumeOffset + extentRead);

                    if (read <= 0)
                        throw new IOException("Unexpected EOF while reading raw $MFT extents.");

                    int totalBytes = carryBytes + read;
                    int parseableBytes = (totalBytes / recordSize) * recordSize;

                    for (int recordOffset = 0; recordOffset < parseableBytes; recordOffset += recordSize)
                    {
                        ulong frn = (ulong)((mftStreamOffset + recordOffset) / recordSize);

                        if (TryParseFileRecord(
                            buffer.AsSpan(recordOffset, recordSize),
                            bytesPerSector,
                            frn,
                            out ParsedRecord parsed))
                        {
                            if (!nodes.TryGetValue(parsed.BaseFrn, out FileNode? fileNode))
                            {
                                fileNode = new FileNode(parsed.BaseFrn, parsed.IsDirectory);
                                nodes.Add(parsed.BaseFrn, fileNode);

                                Interlocked.Increment(ref _stats.RawNodeCount);
                                if (parsed.IsDirectory)
                                    Interlocked.Increment(ref _stats.DirectoryCount);
                                else
                                    Interlocked.Increment(ref _stats.FileCount);
                            }
                            else if (parsed.IsDirectory)
                            {
                                fileNode.IsDirectory = true;
                            }

                            for (int i = 0; i < parsed.Links.Count; i++)
                            {
                                fileNode.AddLinkIfNotExists(parsed.Links[i].ParentFrn, parsed.Links[i].Name);
                            }
                        }
                    }

                    carryBytes = totalBytes - parseableBytes;
                    if (carryBytes > 0)
                    {
                        Buffer.BlockCopy(buffer, parseableBytes, buffer, 0, carryBytes);
                    }

                    mftStreamOffset += parseableBytes;
                    extentRead += read;
                }

                mftBytesRemaining -= extentBytes;
            }

            long parentMissing = 0;
            foreach (FileNode n in nodes.Values)
            {
                bool hasAnyReachableParent = false;

                for (int i = 0; i < n.LinkCount; i++)
                {
                    ref readonly LinkName link = ref n.GetLink(i);

                    if (link.ParentFrn == 0 || link.ParentFrn == RootFrn || nodes.ContainsKey(link.ParentFrn))
                    {
                        hasAnyReachableParent = true;
                        break;
                    }
                }

                if (!hasAnyReachableParent && n.LinkCount > 0 && n.Frn != RootFrn)
                    parentMissing++;
            }

            Interlocked.Exchange(ref _stats.ParentMissingCount, parentMissing);
            return nodes;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static bool TryParseFileRecord(
        Span<byte> record,
        int bytesPerSector,
        ulong frn,
        out ParsedRecord parsed)
    {
        parsed = default;

        if (record.Length < 64)
            return false;

        if (!ApplyFixup(record, bytesPerSector))
            return false;

        if (ReadUInt32(record, 0) != 0x454C4946) // "FILE"
            return false;

        ushort flags = ReadUInt16(record, 22);
        if ((flags & 0x0001) == 0) // in-use
            return false;

        ushort firstAttrOffset = ReadUInt16(record, 20);
        uint bytesInUse = ReadUInt32(record, 24);
        ulong baseFileRef = NormalizeFrn(ReadUInt64(record, 32));

        if (bytesInUse == 0 || bytesInUse > record.Length)
            bytesInUse = (uint)record.Length;

        if (firstAttrOffset >= bytesInUse || firstAttrOffset < 24)
            return false;

        bool isDirectory = (flags & 0x0002) != 0;
        ulong baseFrn = baseFileRef == 0 ? frn : baseFileRef;

        List<LinkName>? links = null;

        int offset = firstAttrOffset;
        int limit = (int)bytesInUse;

        while (offset + 8 <= limit)
        {
            uint attrType = ReadUInt32(record, offset);
            if (attrType == 0xFFFFFFFF)
                break;

            int attrLength = checked((int)ReadUInt32(record, offset + 4));
            if (attrLength <= 0 || offset + attrLength > limit)
                break;

            byte nonResident = record[offset + 8];

            if (attrType == 0x30 && nonResident == 0) // FILE_NAME, resident
            {
                int valueLength = checked((int)ReadUInt32(record, offset + 16));
                int valueOffset = ReadUInt16(record, offset + 20);
                int valuePos = offset + valueOffset;

                if (valueLength >= 66 && valuePos >= offset && valuePos + valueLength <= offset + attrLength)
                {
                    ulong parentRef = NormalizeFrn(ReadUInt64(record, valuePos));
                    byte nameLengthChars = record[valuePos + 64];
                    byte nameNamespace = record[valuePos + 65];

                    // 跳过 DOS-only 名称，避免 8.3 重复。
                    if (nameNamespace != 2)
                    {
                        int nameBytes = nameLengthChars * 2;
                        int namePos = valuePos + 66;

                        if (namePos >= valuePos && namePos + nameBytes <= valuePos + valueLength)
                        {
                            string name = Encoding.Unicode.GetString(record.Slice(namePos, nameBytes));

                            if (!string.IsNullOrEmpty(name))
                            {
                                links ??= new List<LinkName>(2);
                                AddLinkIfNotExists(links, parentRef, name);
                            }
                        }
                    }
                }
            }

            offset += attrLength;
        }

        if (links is null || links.Count == 0)
            return false;

        parsed = new ParsedRecord(baseFrn, isDirectory, links);
        return true;
    }

    private static bool ApplyFixup(Span<byte> record, int bytesPerSector)
    {
        if (record.Length < 8)
            return false;

        ushort usaOffset = ReadUInt16(record, 4);
        ushort usaCount = ReadUInt16(record, 6);

        if (usaOffset < 8 || usaOffset + usaCount * 2 > record.Length || usaCount == 0)
            return false;

        ushort sequenceNumber = ReadUInt16(record, usaOffset);

        for (int i = 1; i < usaCount; i++)
        {
            int sectorEnd = i * bytesPerSector - 2;
            if (sectorEnd + 2 > record.Length)
                return false;

            ushort current = ReadUInt16(record, sectorEnd);
            if (current != sequenceNumber)
                return false;

            ushort replacement = ReadUInt16(record, usaOffset + i * 2);
            WriteUInt16(record, sectorEnd, replacement);
        }

        return true;
    }

    private static void AddLinkIfNotExists(List<LinkName> links, ulong parentFrn, string name)
    {
        for (int i = 0; i < links.Count; i++)
        {
            if (links[i].ParentFrn == parentFrn &&
                string.Equals(links[i].Name, name, StringComparison.Ordinal))
            {
                return;
            }
        }

        links.Add(new LinkName(parentFrn, name));
    }

    private List<Entry> BuildEntries(
        Dictionary<ulong, FileNode> nodes,
        Func<bool>? isCancel,
        CancellationToken cancellationToken)
    {
        ThrowIfCanceled(isCancel, cancellationToken);

        var pathBuilder = new PathBuilder(nodes);

        var results = new List<Entry>(nodes.Count);

        foreach (FileNode node in nodes.Values)
        {
            ThrowIfCanceled(isCancel, cancellationToken);

            for (int i = 0; i < node.LinkCount; i++)
            {
                ThrowIfCanceled(isCancel, cancellationToken);

                string path = pathBuilder.GetRelativePath(node.Frn, i);
                if (path.Length == 0)
                {
                    Interlocked.Increment(ref _stats.NoPathCount);
                    continue;
                }
                if (ShouldFilterFinalPath(path))
                {
                    Interlocked.Increment(ref _stats.FilteredCount);
                    continue;
                }
                results.Add(new Entry(path, node.IsDirectory));
                Interlocked.Increment(ref _stats.ReturnedCount);

                if (node.IsDirectory)
                    Interlocked.Increment(ref _stats.ReturnedDirectoryCount);
                else
                    Interlocked.Increment(ref _stats.ReturnedFileCount);
            }
        }

        return results;
    }
    private static bool ShouldFilterFinalPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        if (path.StartsWith("System Volume Information", StringComparison.OrdinalIgnoreCase))
            return true;

        if (path.StartsWith("$RECYCLE.BIN", StringComparison.OrdinalIgnoreCase))
            return true;

        if (path.StartsWith("$Extend", StringComparison.OrdinalIgnoreCase))
            return true;

        if (path.Equals("$RmMetadata", StringComparison.OrdinalIgnoreCase))
            return true;

        if (path.Equals("$ObjId", StringComparison.OrdinalIgnoreCase))
            return true;

        if (path.Equals("$Quota", StringComparison.OrdinalIgnoreCase))
            return true;

        if (path.Equals("$Reparse", StringComparison.OrdinalIgnoreCase))
            return true;

        if (path.Equals("$UsnJrnl", StringComparison.OrdinalIgnoreCase))
            return true;

        if (path.StartsWith("$Txf", StringComparison.OrdinalIgnoreCase))
            return true;

        if (path.Equals("$Tops", StringComparison.OrdinalIgnoreCase))
            return true;

        if (path.Equals("$Secure", StringComparison.OrdinalIgnoreCase))
            return true;

        if (path.Equals("$UpCase", StringComparison.OrdinalIgnoreCase))
            return true;

        if (path.Equals("$BadClus", StringComparison.OrdinalIgnoreCase))
            return true;

        if (path.Equals("$Bitmap", StringComparison.OrdinalIgnoreCase))
            return true;

        if (path.Equals("$Boot", StringComparison.OrdinalIgnoreCase))
            return true;

        if (path.Equals("$LogFile", StringComparison.OrdinalIgnoreCase))
            return true;

        if (path.Equals("$MFT", StringComparison.OrdinalIgnoreCase))
            return true;

        if (path.Equals("$MFTMirr", StringComparison.OrdinalIgnoreCase))
            return true;

        if (path.Equals("$Volume", StringComparison.OrdinalIgnoreCase))
            return true;

        if (path.Equals("$AttrDef", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
    private ScanStats SnapshotStats(TimeSpan elapsed, bool completed, bool canceled)
    {
        return new ScanStats(
            RawNodeCount: Volatile.Read(ref _stats.RawNodeCount),
            FileCount: Volatile.Read(ref _stats.FileCount),
            DirectoryCount: Volatile.Read(ref _stats.DirectoryCount),
            ParentMissingCount: Volatile.Read(ref _stats.ParentMissingCount),
            ReturnedCount: Volatile.Read(ref _stats.ReturnedCount),
            ReturnedFileCount: Volatile.Read(ref _stats.ReturnedFileCount),
            ReturnedDirectoryCount: Volatile.Read(ref _stats.ReturnedDirectoryCount),
            FilteredCount: Volatile.Read(ref _stats.FilteredCount),
            NoPathCount: Volatile.Read(ref _stats.NoPathCount),
            Canceled: canceled,
            Completed: completed,
            Elapsed: elapsed
        );
    }

    private static VolumeInfo GetVolumeInfo(SafeFileHandle volumeHandle, string driveRoot)
    {
        int outSize = Marshal.SizeOf<NTFS_VOLUME_DATA_BUFFER>();
        IntPtr outPtr = Marshal.AllocHGlobal(outSize);

        try
        {
            bool ok = NativeMethods.DeviceIoControl(
                volumeHandle,
                NativeMethods.FSCTL_GET_NTFS_VOLUME_DATA,
                IntPtr.Zero,
                0,
                outPtr,
                outSize,
                out _,
                IntPtr.Zero);

            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException(
                    $"Drive '{driveRoot}' is not accessible as NTFS volume, or FSCTL_GET_NTFS_VOLUME_DATA failed. Win32={err}");
            }

            NTFS_VOLUME_DATA_BUFFER d = Marshal.PtrToStructure<NTFS_VOLUME_DATA_BUFFER>(outPtr);
            return new VolumeInfo(
    BytesPerSector: d.BytesPerSector,
    BytesPerCluster: d.BytesPerCluster,
    BytesPerFileRecordSegment: d.BytesPerFileRecordSegment,
    MftValidDataLength: d.MftValidDataLength,
    MftStartLcn: d.MftStartLcn);
        }
        finally
        {
            Marshal.FreeHGlobal(outPtr);
        }
    }
    private static List<Extent> ParseRunlist(ReadOnlySpan<byte> runlist)
    {
        var extents = new List<Extent>(128);

        long currentLcn = 0;
        long currentVcn = 0;
        int pos = 0;

        while (pos < runlist.Length)
        {
            byte header = runlist[pos++];
            if (header == 0)
                break;

            int lenSize = header & 0x0F;
            int offSize = (header >> 4) & 0x0F;

            if (lenSize == 0 || pos + lenSize + offSize > runlist.Length)
                throw new InvalidOperationException("Invalid runlist.");

            long runLength = 0;
            for (int i = 0; i < lenSize; i++)
                runLength |= (long)runlist[pos + i] << (8 * i);
            pos += lenSize;

            long runOffset = 0;
            if (offSize > 0)
            {
                long temp = 0;
                for (int i = 0; i < offSize; i++)
                    temp |= (long)runlist[pos + i] << (8 * i);

                // sign extend
                long signBit = 1L << (offSize * 8 - 1);
                if ((temp & signBit) != 0)
                    temp |= -1L << (offSize * 8);

                runOffset = temp;
            }
            pos += offSize;

            currentLcn += runOffset;
            long nextVcn = currentVcn + runLength;

            extents.Add(new Extent(currentVcn, nextVcn, currentLcn));
            currentVcn = nextVcn;
        }

        return extents;
    }
    private static SafeFileHandle OpenVolume(char driveLetter)
    {
        string volumePath = @"\\.\" + driveLetter + ":";

        SafeFileHandle handle = NativeMethods.CreateFile(
            volumePath,
            NativeMethods.GENERIC_READ,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE | NativeMethods.FILE_SHARE_DELETE,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to open volume {volumePath}");

        return handle;
    }
    private static List<Extent> GetMftExtentsFromVolume(
    SafeFileHandle volumeHandle,
    VolumeInfo volumeInfo,
    Func<bool>? isCancel,
    CancellationToken cancellationToken)
    {
        ThrowIfCanceled(isCancel, cancellationToken);

        int bytesPerSector = checked((int)volumeInfo.BytesPerSector);
        int bytesPerCluster = checked((int)volumeInfo.BytesPerCluster);
        int recordSize = checked((int)volumeInfo.BytesPerFileRecordSegment);

        long firstRecordOffset = volumeInfo.MftStartLcn * volumeInfo.BytesPerCluster;

        byte[] record = new byte[recordSize];
        int read = RandomAccess.Read(volumeHandle, record, firstRecordOffset);
        if (read != recordSize)
            throw new IOException("Failed to read first $MFT record from volume.");

        if (!ApplyFixup(record, bytesPerSector))
            throw new InvalidOperationException("Failed to apply fixup to $MFT record.");

        if (ReadUInt32(record, 0) != 0x454C4946) // FILE
            throw new InvalidOperationException("Invalid $MFT file record signature.");

        ushort firstAttrOffset = ReadUInt16(record, 20);
        uint bytesInUse = ReadUInt32(record, 24);
        if (bytesInUse == 0 || bytesInUse > record.Length)
            bytesInUse = (uint)record.Length;

        int offset = firstAttrOffset;
        int limit = (int)bytesInUse;

        while (offset + 8 <= limit)
        {
            uint attrType = ReadUInt32(record, offset);
            if (attrType == 0xFFFFFFFF)
                break;

            int attrLength = checked((int)ReadUInt32(record, offset + 4));
            if (attrLength <= 0 || offset + attrLength > limit)
                break;

            byte nonResident = record[offset + 8];
            byte nameLength = record[offset + 9];
            ushort nameOffset = ReadUInt16(record, offset + 10);

            // 找 unnamed $DATA，且必须是 non-resident
            if (attrType == 0x80 && nonResident != 0 && nameLength == 0)
            {
                ushort runOffset = ReadUInt16(record, offset + 32);
                int runPos = offset + runOffset;
                int runEnd = offset + attrLength;

                if (runPos < offset || runPos >= runEnd)
                    break;

                return ParseRunlist(record.AsSpan(runPos, runEnd - runPos));
            }

            offset += attrLength;
        }

        throw new InvalidOperationException("Failed to find unnamed non-resident DATA attribute in $MFT record.");
    }

    private sealed class PathBuilder
    {
        private readonly Dictionary<ulong, FileNode> _nodes;
        private readonly Dictionary<ulong, string> _dirPathCache = new();

        public PathBuilder(Dictionary<ulong, FileNode> nodes)
        {
            _nodes = nodes;
            _dirPathCache[RootFrn] = string.Empty;
        }

        public string GetRelativePath(ulong frn, int linkIndex)
        {
            frn = NormalizeFrn(frn);

            if (frn == RootFrn)
                return string.Empty;

            if (!_nodes.TryGetValue(frn, out FileNode? node))
                return string.Empty;

            ref readonly LinkName link = ref node.GetLink(linkIndex);

            string parentPath = GetDirectoryRelativePath(link.ParentFrn);
            if (parentPath.Length == 0)
                return link.ParentFrn == RootFrn ? link.Name : string.Empty;

            return string.Concat(parentPath, "\\", link.Name);
        }

        private string GetDirectoryRelativePath(ulong dirFrn)
        {
            dirFrn = NormalizeFrn(dirFrn);

            if (dirFrn == 0 || dirFrn == RootFrn)
                return string.Empty;

            if (_dirPathCache.TryGetValue(dirFrn, out string? cached))
                return cached;

            if (!_nodes.TryGetValue(dirFrn, out FileNode? dirNode))
                return string.Empty;

            if (!dirNode.IsDirectory)
                return string.Empty;

            for (int i = 0; i < dirNode.LinkCount; i++)
            {

                ref readonly LinkName dirLink = ref dirNode.GetLink(i);

                if (dirLink.ParentFrn == dirFrn)
                    continue;

                string result;
                if (dirLink.ParentFrn == RootFrn)
                {
                    result = dirLink.Name;
                }
                else
                {
                    string parent = GetDirectoryRelativePath(dirLink.ParentFrn);
                    if (parent.Length == 0)
                        continue;

                    result = string.Concat(parent, "\\", dirLink.Name);
                }

                _dirPathCache[dirFrn] = result;
                return result;
            }

            return string.Empty;
        }

    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(MFTScan));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong NormalizeFrn(ulong frn) => frn & FrnMask;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsCanceled(Func<bool>? isCancel)
    {
        return isCancel != null && isCancel();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ThrowIfCanceled(Func<bool>? isCancel, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (isCancel != null && isCancel())
            throw new OperationCanceledException("Scan canceled by external bool callback.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort ReadUInt16(ReadOnlySpan<byte> span, int offset)
        => (ushort)(span[offset] | (span[offset + 1] << 8));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ReadUInt32(ReadOnlySpan<byte> span, int offset)
        => (uint)(span[offset]
            | (span[offset + 1] << 8)
            | (span[offset + 2] << 16)
            | (span[offset + 3] << 24));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong ReadUInt64(ReadOnlySpan<byte> span, int offset)
        => (ulong)ReadUInt32(span, offset) | ((ulong)ReadUInt32(span, offset + 4) << 32);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteUInt16(Span<byte> span, int offset, ushort value)
    {
        span[offset] = (byte)value;
        span[offset + 1] = (byte)(value >> 8);
    }

    private sealed class StatsState
    {
        public long RawNodeCount;
        public long FileCount;
        public long DirectoryCount;
        public long ParentMissingCount;
        public long ReturnedCount;
        public long ReturnedFileCount;
        public long ReturnedDirectoryCount;
        public long FilteredCount;
        public long NoPathCount;
    }

    private readonly record struct VolumeInfo(
        uint BytesPerSector,
        uint BytesPerCluster,
        uint BytesPerFileRecordSegment,
        long MftValidDataLength,
        long MftStartLcn);

    private readonly record struct Extent(long StartVcn, long NextVcn, long Lcn);

    private readonly record struct ParsedRecord(ulong BaseFrn, bool IsDirectory, List<LinkName> Links);

    private readonly record struct LinkName(ulong ParentFrn, string Name);

    private sealed class FileNode
    {
        public ulong Frn { get; }
        public bool IsDirectory { get; set; }

        private bool _hasFirst;
        private LinkName _first;
        private List<LinkName>? _more;

        public FileNode(ulong frn, bool isDirectory)
        {
            Frn = frn;
            IsDirectory = isDirectory;
        }

        public int LinkCount => !_hasFirst ? 0 : (_more is null ? 1 : 1 + _more.Count);

        public ref readonly LinkName GetLink(int index)
        {
            if (!_hasFirst)
                throw new ArgumentOutOfRangeException(nameof(index));

            if (index == 0)
                return ref _first;

            if (_more is null)
                throw new ArgumentOutOfRangeException(nameof(index));

            return ref CollectionsMarshal.AsSpan(_more)[index - 1];
        }

        public void AddLinkIfNotExists(ulong parentFrn, string name)
        {
            if (!_hasFirst)
            {
                _first = new LinkName(parentFrn, name);
                _hasFirst = true;
                return;
            }

            if (_first.ParentFrn == parentFrn &&
                string.Equals(_first.Name, name, StringComparison.Ordinal))
            {
                return;
            }

            if (_more is null)
            {
                _more = new List<LinkName>(1)
                {
                    new LinkName(parentFrn, name)
                };
                return;
            }

            for (int i = 0; i < _more.Count; i++)
            {
                LinkName existing = _more[i];
                if (existing.ParentFrn == parentFrn &&
                    string.Equals(existing.Name, name, StringComparison.Ordinal))
                {
                    return;
                }
            }

            _more.Add(new LinkName(parentFrn, name));
        }
    }

    private static class NativeMethods
    {
        public const uint GENERIC_READ = 0x80000000;
        public const uint FILE_SHARE_READ = 0x00000001;
        public const uint FILE_SHARE_WRITE = 0x00000002;
        public const uint FILE_SHARE_DELETE = 0x00000004;
        public const uint OPEN_EXISTING = 3;

        public const uint FSCTL_GET_NTFS_VOLUME_DATA = 0x00090064;
        public const uint FSCTL_GET_RETRIEVAL_POINTERS = 0x00090073;

        public const int ERROR_HANDLE_EOF = 38;
        public const int ERROR_MORE_DATA = 234;
        public const int ERROR_ACCESS_DENIED = 5;
        public const int ERROR_JOURNAL_ALREADY_ACTIVE = 1179;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            int nInBufferSize,
            IntPtr lpOutBuffer,
            int nOutBufferSize,
            out int lpBytesReturned,
            IntPtr lpOverlapped);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NTFS_VOLUME_DATA_BUFFER
    {
        public long VolumeSerialNumber;
        public long NumberSectors;
        public long TotalClusters;
        public long FreeClusters;
        public long TotalReserved;
        public uint BytesPerSector;
        public uint BytesPerCluster;
        public uint BytesPerFileRecordSegment;
        public uint ClustersPerFileRecordSegment;
        public long MftValidDataLength;
        public long MftStartLcn;
        public long Mft2StartLcn;
        public long MftZoneStart;
        public long MftZoneEnd;
    }
}

public readonly record struct Entry(string Path, bool IsDir);

public readonly record struct ScanStats(
    long RawNodeCount,
    long FileCount,
    long DirectoryCount,
    long ParentMissingCount,
    long ReturnedCount,
    long ReturnedFileCount,
    long ReturnedDirectoryCount,
    long FilteredCount,
    long NoPathCount,
    bool Canceled,
    bool Completed,
    TimeSpan Elapsed
);

public sealed record ScanResult(
    List<Entry> Entries,
    ScanStats Stats
);