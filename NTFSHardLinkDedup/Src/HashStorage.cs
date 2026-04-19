using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NTFSHardLinkDedup.Src
{
    public sealed class HashStorageBuilder(int capacity = 0)
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        private readonly Dictionary<Hash256, EntryBucket> _map = capacity > 0
                ? new Dictionary<Hash256, EntryBucket>(capacity)
                : new Dictionary<Hash256, EntryBucket>();
        private readonly HashSet<string> _pathSet = capacity > 0
                ? new HashSet<string>(capacity, StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);

        private readonly struct PathItem(string path)
        {
            public readonly string Path = path;
        }

        private sealed class EntryBucket(ulong size, int capacity)
        {
            public ulong Size = size;
            public readonly List<PathItem> Paths = new List<PathItem>(capacity);
        }

        private static class FormatConstants
        {
            public static ReadOnlySpan<byte> Magic => "HashList"u8;

            public const int Version = 3;

            // HeaderFixed:
            // int Version
            // int Flags
            // int EntryCount
            // int PathCount
            // long EntryIndexOffset
            // long EntryIndexLength
            // long PathIndexOffset
            // long PathIndexLength
            // long PathBlobOffset
            // long PathBlobLength
            public const int HeaderFixedSize =
                4 + 4 + 4 + 4 +
                8 + 8 +
                8 + 8 +
                8 + 8;

            // EntryIndexRecord:
            // 32 bytes Hash
            // 8 bytes Size
            // 4 bytes PathStartIndex
            // 4 bytes PathCount
            public const int EntryIndexRecordSize = 32 + 8 + 4 + 4;

            // PathIndexRecord:
            // 8 bytes PathBlobOffset
            // 4 bytes PathByteLength
            // 4 bytes EntryId
            public const int PathIndexRecordSize = 8 + 4 + 4;
        }

        private readonly struct HashListHeader(
            int version,
            int flags,
            int entryCount,
            int pathCount,
            long entryIndexOffset,
            long entryIndexLength,
            long pathIndexOffset,
            long pathIndexLength,
            long pathBlobOffset,
            long pathBlobLength)
        {
            public readonly int Version = version;
            public readonly int Flags = flags;
            public readonly int EntryCount = entryCount;
            public readonly int PathCount = pathCount;
            public readonly long EntryIndexOffset = entryIndexOffset;
            public readonly long EntryIndexLength = entryIndexLength;
            public readonly long PathIndexOffset = pathIndexOffset;
            public readonly long PathIndexLength = pathIndexLength;
            public readonly long PathBlobOffset = pathBlobOffset;
            public readonly long PathBlobLength = pathBlobLength;
        }

        public int EntryCount => _map.Count;

        public bool ContainsPath(ReadOnlySpan<char> path)
        {
            if (path.IsEmpty)
                return false;

            return _pathSet.Contains(path.ToString());
        }

        public bool TryAddFile(ReadOnlyMemory<byte> hash, ReadOnlySpan<char> path, ulong size)
            => TryAddFile(hash.Span, path, size);

        public bool TryAddFile(ReadOnlySpan<byte> hash, ReadOnlySpan<char> path, ulong size)
        {
            if (hash.Length != Hash256.Size)
                throw new ArgumentException("SHA256 hash must be exactly 32 bytes.", nameof(hash));
            if (path.IsEmpty)
                throw new ArgumentException("Path cannot be empty.", nameof(path));

            string copiedPath = new string(path);

            if (!_pathSet.Add(copiedPath))
                return false;

            var key = new Hash256(hash);

            if (!_map.TryGetValue(key, out var bucket))
            {
                bucket = new EntryBucket(size, 4);
                _map.Add(key, bucket);
            }
            else
            {
                // 同 hash 文件大小应相同；内部库下直接强约束
                if (bucket.Size != size)
                    throw new InvalidDataException("Files with the same hash must have the same size.");
            }

            bucket.Paths.Add(new PathItem(copiedPath));
            return true;
        }

        public void AddFile(ReadOnlyMemory<byte> hash, ReadOnlySpan<char> path, ulong size)
            => TryAddFile(hash.Span, path, size);

        public void AddFile(ReadOnlySpan<byte> hash, ReadOnlySpan<char> path, ulong size)
            => TryAddFile(hash, path, size);

        public bool TryAddDir(ReadOnlySpan<char> path)
        {
            if (path.IsEmpty)
                throw new ArgumentException("Path cannot be empty.", nameof(path));

            string copiedPath = new string(path);

            if (!_pathSet.Add(copiedPath))
                return false;

            if (!_map.TryGetValue(Hash256.DirectoryMarker, out var bucket))
            {
                bucket = new EntryBucket(0UL, 16);
                _map.Add(Hash256.DirectoryMarker, bucket);
            }

            bucket.Paths.Add(new PathItem(copiedPath));
            return true;
        }

        public void AddDir(ReadOnlySpan<char> path)
            => TryAddDir(path);

        internal IEnumerable<KeyValuePair<Hash256, (ulong Size, IReadOnlyList<string> Paths)>> EnumerateEntries()
        {
            foreach (var kv in _map)
            {
                var list = new List<string>(kv.Value.Paths.Count);
                for (int i = 0; i < kv.Value.Paths.Count; i++)
                    list.Add(kv.Value.Paths[i].Path);

                yield return new KeyValuePair<Hash256, (ulong Size, IReadOnlyList<string> Paths)>(
                    kv.Key, (kv.Value.Size, list));
            }
        }

        public void WriteToFile(string filePath, int bufferSize = 1 << 20)
        {
            using var fs = new FileStream(
                filePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize,
                FileOptions.SequentialScan);

            WriteTo(fs);
        }

        public void WriteTo(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);

            int entryCount = _map.Count;
            int pathCount = 0;
            long pathBlobRawLength = 0;

            foreach (var kv in _map)
            {
                var paths = kv.Value.Paths;
                pathCount += paths.Count;

                for (int i = 0; i < paths.Count; i++)
                {
                    int byteCount = Utf8.GetByteCount(paths[i].Path);
                    if ((uint)byteCount > ushort.MaxValue)
                        throw new InvalidDataException("Path UTF-8 byte length exceeds ushort.MaxValue.");

                    pathBlobRawLength += byteCount;
                }
            }

            // 每条 path 后额外写一个 0 分隔字节，避免跨 path 误命中
            long pathBlobLength = pathBlobRawLength + pathCount;

            long entryIndexLength = (long)entryCount * FormatConstants.EntryIndexRecordSize;
            long pathIndexLength = (long)pathCount * FormatConstants.PathIndexRecordSize;

            long entryIndexOffset = FormatConstants.HeaderFixedSize;
            long pathIndexOffset = entryIndexOffset + entryIndexLength;
            long pathBlobOffset = pathIndexOffset + pathIndexLength;

            long dataLength =
                FormatConstants.HeaderFixedSize +
                entryIndexLength +
                pathIndexLength +
                pathBlobLength;

            var header = new HashListHeader(
                version: FormatConstants.Version,
                flags: 0,
                entryCount: entryCount,
                pathCount: pathCount,
                entryIndexOffset: entryIndexOffset,
                entryIndexLength: entryIndexLength,
                pathIndexOffset: pathIndexOffset,
                pathIndexLength: pathIndexLength,
                pathBlobOffset: pathBlobOffset,
                pathBlobLength: pathBlobLength);

            Span<byte> i32 = stackalloc byte[4];
            Span<byte> i64 = stackalloc byte[8];
            Span<byte> hashBuf = stackalloc byte[Hash256.Size];

            stream.Write(FormatConstants.Magic);
            BinaryPrimitives.WriteInt64LittleEndian(i64, dataLength);
            stream.Write(i64);

            WriteHeader(stream, header);

            byte[] rented = ArrayPool<byte>.Shared.Rent(4096);

            try
            {
                // EntryIndex
                int entryId = 0;
                int globalPathIndex = 0;

                foreach (var kv in _map)
                {
                    Hash256 hash = kv.Key;
                    EntryBucket bucket = kv.Value;

                    hash.CopyTo(hashBuf);
                    stream.Write(hashBuf);

                    BinaryPrimitives.WriteUInt64LittleEndian(i64, bucket.Size);
                    stream.Write(i64);

                    BinaryPrimitives.WriteInt32LittleEndian(i32, globalPathIndex);
                    stream.Write(i32);

                    BinaryPrimitives.WriteInt32LittleEndian(i32, bucket.Paths.Count);
                    stream.Write(i32);

                    globalPathIndex += bucket.Paths.Count;
                    entryId++;
                }

                // PathIndex
                entryId = 0;
                long currentBlobOffset = 0;

                foreach (var kv in _map)
                {
                    EntryBucket bucket = kv.Value;

                    for (int i = 0; i < bucket.Paths.Count; i++)
                    {
                        string path = bucket.Paths[i].Path;
                        int byteCount = Utf8.GetByteCount(path);

                        BinaryPrimitives.WriteInt64LittleEndian(i64, currentBlobOffset);
                        stream.Write(i64);

                        BinaryPrimitives.WriteInt32LittleEndian(i32, byteCount);
                        stream.Write(i32);

                        BinaryPrimitives.WriteInt32LittleEndian(i32, entryId);
                        stream.Write(i32);

                        currentBlobOffset += byteCount + 1;
                    }

                    entryId++;
                }

                // PathBlob
                foreach (var kv in _map)
                {
                    EntryBucket bucket = kv.Value;

                    for (int i = 0; i < bucket.Paths.Count; i++)
                    {
                        string path = bucket.Paths[i].Path;
                        int byteCount = Utf8.GetByteCount(path);

                        if (rented.Length < byteCount)
                        {
                            ArrayPool<byte>.Shared.Return(rented);
                            rented = ArrayPool<byte>.Shared.Rent(byteCount);
                        }

                        int written = Utf8.GetBytes(path.AsSpan(), rented);
                        stream.Write(rented.AsSpan(0, written));
                        stream.WriteByte(0);
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        public static HashStorageBuilder RestoreFromFile(string filePath, int fileBufferSize = 1 << 20, int initialCapacity = 0)
        {
            if (!HashListSearcher.IsValidFile(filePath))
                throw new InvalidDataException("Invalid hashlist file.");

            using var searcher = new HashListSearcher(filePath, fileBufferSize);
            return searcher.RestoreToBuilder(initialCapacity);
        }

        private static void WriteHeader(Stream stream, HashListHeader h)
        {
            Span<byte> i32 = stackalloc byte[4];
            Span<byte> i64 = stackalloc byte[8];

            BinaryPrimitives.WriteInt32LittleEndian(i32, h.Version);
            stream.Write(i32);

            BinaryPrimitives.WriteInt32LittleEndian(i32, h.Flags);
            stream.Write(i32);

            BinaryPrimitives.WriteInt32LittleEndian(i32, h.EntryCount);
            stream.Write(i32);

            BinaryPrimitives.WriteInt32LittleEndian(i32, h.PathCount);
            stream.Write(i32);

            BinaryPrimitives.WriteInt64LittleEndian(i64, h.EntryIndexOffset);
            stream.Write(i64);

            BinaryPrimitives.WriteInt64LittleEndian(i64, h.EntryIndexLength);
            stream.Write(i64);

            BinaryPrimitives.WriteInt64LittleEndian(i64, h.PathIndexOffset);
            stream.Write(i64);

            BinaryPrimitives.WriteInt64LittleEndian(i64, h.PathIndexLength);
            stream.Write(i64);

            BinaryPrimitives.WriteInt64LittleEndian(i64, h.PathBlobOffset);
            stream.Write(i64);

            BinaryPrimitives.WriteInt64LittleEndian(i64, h.PathBlobLength);
            stream.Write(i64);
        }
        public void Clear()
        {
            _map.Clear();
            _pathSet.Clear();
        }
    }
}