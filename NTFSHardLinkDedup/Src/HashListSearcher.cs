using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace NTFSHardLinkDedup.Src
{
    public sealed class HashListSearcher : IDisposable
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        private readonly FileStream _stream;
        private readonly MemoryMappedFile _mmf;
        private readonly MemoryMappedViewAccessor _pathBlobAccessor;

        private readonly long _dataAreaStart;
        private readonly HashListHeader _header;
        private readonly EntryIndexRecord[] _entryIndex;
        private readonly PathIndexRecord[] _pathIndex;
        private readonly Dictionary<Hash256, int> _hashToEntryId;

        private byte[] _buffer;
        private byte[] _keywordBuffer;

        public int MaxResult { get; set; } = 20_000;

        private static class FormatConstants
        {
            public static ReadOnlySpan<byte> Magic => "HashList"u8;

            public const int Version = 3;

            public const int HeaderFixedSize =
                4 + 4 + 4 + 4 +
                8 + 8 +
                8 + 8 +
                8 + 8;

            public const int EntryIndexRecordSize = 32 + 8 + 4 + 4;
            public const int PathIndexRecordSize = 8 + 4 + 4;
        }

        public readonly struct HashListHeader(
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

        public readonly struct EntryIndexRecord(Hash256 hash, ulong size, int pathStartIndex, int pathCount)
        {
            public readonly Hash256 Hash = hash;
            public readonly ulong Size = size;
            public readonly int PathStartIndex = pathStartIndex;
            public readonly int PathCount = pathCount;
        }

        public readonly struct PathIndexRecord(long pathBlobOffset, int pathByteLength, int entryId)
        {
            public readonly long PathBlobOffset = pathBlobOffset;
            public readonly int PathByteLength = pathByteLength;
            public readonly int EntryId = entryId;
        }

        public readonly struct HashPathPair(Hash256 hash, string path, ulong size)
        {
            public readonly Hash256 Hash = hash;
            public readonly string Path = path;
            public readonly ulong Size = size;
        }

        public readonly struct SearchResult(List<HashListSearcher.HashPathPair> items, bool exceededMaxResults, int scannedHitCount)
        {
            public readonly bool ExceededMaxResults = exceededMaxResults;
            public readonly int ScannedHitCount = scannedHitCount;
            public readonly List<HashPathPair> Items = items;
        }

        public int EntryCount => _entryIndex.Length;

        public HashListSearcher(string filePath, int fileBufferSize = 1 << 20, int initialBufferSize = 4096)
        {
            _stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                fileBufferSize,
                FileOptions.SequentialScan | FileOptions.RandomAccess);

            _mmf = MemoryMappedFile.CreateFromFile(
                _stream,
                mapName: null,
                capacity: 0,
                MemoryMappedFileAccess.Read,
                HandleInheritability.None,
                leaveOpen: true);

            Span<byte> magic = stackalloc byte[8];
            Span<byte> i64 = stackalloc byte[8];

            ReadExact(_stream, magic);
            if (!magic.SequenceEqual(FormatConstants.Magic))
                throw new InvalidDataException("Invalid magic.");

            ReadExact(_stream, i64);
            long dataLength = BinaryPrimitives.ReadInt64LittleEndian(i64);

            _dataAreaStart = 16;
            if (_stream.Length < _dataAreaStart + dataLength)
                throw new InvalidDataException("Invalid dataLength.");

            _header = ReadHeader(_stream);
            _entryIndex = ReadEntryIndex(_stream, _dataAreaStart, _header);
            _pathIndex = ReadPathIndex(_stream, _dataAreaStart, _header);

            _hashToEntryId = new Dictionary<Hash256, int>(_entryIndex.Length);
            for (int i = 0; i < _entryIndex.Length; i++)
                _hashToEntryId[_entryIndex[i].Hash] = i;

            _pathBlobAccessor = _mmf.CreateViewAccessor(
                _dataAreaStart + _header.PathBlobOffset,
                _header.PathBlobLength,
                MemoryMappedFileAccess.Read);

            _buffer = ArrayPool<byte>.Shared.Rent(initialBufferSize);
            _keywordBuffer = ArrayPool<byte>.Shared.Rent(initialBufferSize);
        }
        public (int HashCount, int FileCount) GetCounts()
        {
            int hashCount = _entryIndex.Length;
            int fileCount = 0;

            for (int i = 0; i < _entryIndex.Length; i++)
            {
                ref readonly EntryIndexRecord rec = ref _entryIndex[i];
                fileCount += rec.PathCount;
            }

            return (hashCount, fileCount);
        }

        public static bool IsValidFile(string hashListPath)
        {
            if (string.IsNullOrWhiteSpace(hashListPath))
                return false;

            try
            {
                var fi = new FileInfo(hashListPath);
                if (!fi.Exists)
                    return false;

                long minLen = 8 + 8 + FormatConstants.HeaderFixedSize;
                if (fi.Length < minLen)
                    return false;

                using var fs = new FileStream(
                    hashListPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    4096,
                    FileOptions.SequentialScan);

                Span<byte> magic = stackalloc byte[8];
                Span<byte> i32 = stackalloc byte[4];
                Span<byte> i64 = stackalloc byte[8];

                ReadExact(fs, magic);
                if (!magic.SequenceEqual(FormatConstants.Magic))
                    return false;

                ReadExact(fs, i64);
                long dataLength = BinaryPrimitives.ReadInt64LittleEndian(i64);
                if (dataLength < FormatConstants.HeaderFixedSize)
                    return false;

                if (fs.Length < 16 + dataLength)
                    return false;

                ReadExact(fs, i32);
                int version = BinaryPrimitives.ReadInt32LittleEndian(i32);

                ReadExact(fs, i32);
                int flags = BinaryPrimitives.ReadInt32LittleEndian(i32);

                ReadExact(fs, i32);
                int entryCount = BinaryPrimitives.ReadInt32LittleEndian(i32);

                ReadExact(fs, i32);
                int pathCount = BinaryPrimitives.ReadInt32LittleEndian(i32);

                ReadExact(fs, i64);
                long entryIndexOffset = BinaryPrimitives.ReadInt64LittleEndian(i64);

                ReadExact(fs, i64);
                long entryIndexLength = BinaryPrimitives.ReadInt64LittleEndian(i64);

                ReadExact(fs, i64);
                long pathIndexOffset = BinaryPrimitives.ReadInt64LittleEndian(i64);

                ReadExact(fs, i64);
                long pathIndexLength = BinaryPrimitives.ReadInt64LittleEndian(i64);

                ReadExact(fs, i64);
                long pathBlobOffset = BinaryPrimitives.ReadInt64LittleEndian(i64);

                ReadExact(fs, i64);
                long pathBlobLength = BinaryPrimitives.ReadInt64LittleEndian(i64);

                if (version != FormatConstants.Version)
                    return false;

                if (flags < 0 || entryCount < 0 || pathCount < 0)
                    return false;

                if (entryIndexOffset < 0 || entryIndexLength < 0 ||
                    pathIndexOffset < 0 || pathIndexLength < 0 ||
                    pathBlobOffset < 0 || pathBlobLength < 0)
                    return false;

                if (entryIndexLength != (long)entryCount * FormatConstants.EntryIndexRecordSize)
                    return false;

                if (pathIndexLength != (long)pathCount * FormatConstants.PathIndexRecordSize)
                    return false;

                if (entryIndexOffset != FormatConstants.HeaderFixedSize)
                    return false;

                if (pathIndexOffset != entryIndexOffset + entryIndexLength)
                    return false;

                if (pathBlobOffset != pathIndexOffset + pathIndexLength)
                    return false;

                if (entryIndexOffset + entryIndexLength > dataLength)
                    return false;

                if (pathIndexOffset + pathIndexLength > dataLength)
                    return false;

                if (pathBlobOffset + pathBlobLength > dataLength)
                    return false;

                if (dataLength != pathBlobOffset + pathBlobLength)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool TryFindByHash(ReadOnlySpan<byte> hash, out List<HashPathPair>? items)
        {
            if (hash.Length != Hash256.Size)
                throw new ArgumentException("SHA256 hash must be exactly 32 bytes.", nameof(hash));

            var key = new Hash256(hash);
            if (!_hashToEntryId.TryGetValue(key, out int entryId))
            {
                items = null;
                return false;
            }

            items = ExpandEntryToPairs(entryId);
            return true;
        }

        public bool TryFindByPath(ReadOnlySpan<char> path, out List<HashPathPair>? items)
        {
            int byteCount = Utf8.GetByteCount(path);
            EnsureKeywordCapacity(byteCount);
            int encoded = Utf8.GetBytes(path, _keywordBuffer);

            int pathId = FindExactPathId(_keywordBuffer.AsSpan(0, encoded));
            if (pathId < 0)
            {
                items = null;
                return false;
            }

            int entryId = _pathIndex[pathId].EntryId;
            items = ExpandEntryToPairs(entryId);
            return true;
        }
        private SearchResult FindAll()
        {
            var items = new List<HashPathPair>(Math.Min(MaxResult, 256));
            bool exceeded = false;
            int matchedCount = 0;

            for (int pathId = 0; pathId < _pathIndex.Length; pathId++)
            {
                ref readonly PathIndexRecord rec = ref _pathIndex[pathId];
                matchedCount++;

                if (items.Count >= MaxResult)
                {
                    exceeded = true;
                    break;
                }

                string path = ReadPathString(pathId);
                ref readonly EntryIndexRecord entry = ref _entryIndex[rec.EntryId];
                items.Add(new HashPathPair(entry.Hash, path, entry.Size));
            }

            return new SearchResult(items, exceededMaxResults: exceeded, scannedHitCount: matchedCount);
        }
        public SearchResult FindByKeyword(ReadOnlySpan<char> keyword)
        {
            string pattern = keyword.ToString().Trim();
            // 特殊规则：单独一个 * 表示显示全部
            if ((pattern.Length == 1 && pattern[0] == '*') || pattern == string.Empty)
            {
                return FindAll();
            }
            var groups = ParseKeywordPattern(pattern);

            var items = new List<HashPathPair>(Math.Min(MaxResult, 256));
            if (groups.Count == 0)
                return new SearchResult(items, exceededMaxResults: false, scannedHitCount: 0);

            int matchedCount = 0;
            bool exceeded = false;

            for (int pathId = 0; pathId < _pathIndex.Length; pathId++)
            {
                ref readonly PathIndexRecord rec = ref _pathIndex[pathId];

                EnsureBufferCapacity(rec.PathByteLength);
                _pathBlobAccessor.ReadArray(rec.PathBlobOffset, _buffer, 0, rec.PathByteLength);

                ReadOnlySpan<byte> pathBytes = _buffer.AsSpan(0, rec.PathByteLength);

                if (!MatchesKeywordPattern(pathBytes, groups))
                    continue;

                matchedCount++;

                if (items.Count >= MaxResult)
                {
                    exceeded = true;
                    break;
                }

                string path = Utf8.GetString(_buffer, 0, rec.PathByteLength);
                ref readonly EntryIndexRecord entry = ref _entryIndex[rec.EntryId];
                items.Add(new HashPathPair(entry.Hash, path, entry.Size));
            }

            return new SearchResult(items, exceededMaxResults: exceeded, scannedHitCount: matchedCount);
        }

        public bool TryGetEntryById(int entryId, out Hash256 hash, out ulong size, out string[]? paths)
        {
            if ((uint)entryId >= (uint)_entryIndex.Length)
            {
                hash = default;
                size = 0;
                paths = null;
                return false;
            }

            ref readonly EntryIndexRecord rec = ref _entryIndex[entryId];
            hash = rec.Hash;
            size = rec.Size;

            var arr = new string[rec.PathCount];
            for (int i = 0; i < rec.PathCount; i++)
                arr[i] = ReadPathString(rec.PathStartIndex + i);

            paths = arr;
            return true;
        }

        public HashStorageBuilder RestoreToBuilder(int initialCapacity = 0)
        {
            var builder = new HashStorageBuilder(initialCapacity > 0 ? initialCapacity : _header.EntryCount);

            for (int entryId = 0; entryId < _entryIndex.Length; entryId++)
            {
                ref readonly EntryIndexRecord rec = ref _entryIndex[entryId];
                int pathStart = rec.PathStartIndex;
                int pathCount = rec.PathCount;

                Span<byte> hashBytes = new byte[Hash256.Size];
                rec.Hash.CopyTo(hashBytes);

                if (rec.Hash == Hash256.DirectoryMarker)
                {
                    for (int i = 0; i < pathCount; i++)
                    {
                        string path = ReadPathString(pathStart + i);
                        builder.AddDir(path.AsSpan());
                    }
                }
                else
                {
                    for (int i = 0; i < pathCount; i++)
                    {
                        string path = ReadPathString(pathStart + i);
                        builder.AddFile(hashBytes, path.AsSpan(), rec.Size);
                    }
                }
            }

            return builder;
        }

        private List<HashPathPair> ExpandEntryToPairs(int entryId)
        {
            ref readonly EntryIndexRecord rec = ref _entryIndex[entryId];
            var list = new List<HashPathPair>(rec.PathCount);

            for (int i = 0; i < rec.PathCount; i++)
            {
                string path = ReadPathString(rec.PathStartIndex + i);
                list.Add(new HashPathPair(rec.Hash, path, rec.Size));
            }

            return list;
        }

        private string ReadPathString(int pathId)
        {
            ref readonly PathIndexRecord rec = ref _pathIndex[pathId];
            EnsureBufferCapacity(rec.PathByteLength);
            _pathBlobAccessor.ReadArray(rec.PathBlobOffset, _buffer, 0, rec.PathByteLength);
            return Utf8.GetString(_buffer, 0, rec.PathByteLength);
        }

        private int FindExactPathId(ReadOnlySpan<byte> target)
        {
            for (int i = 0; i < _pathIndex.Length; i++)
            {
                ref readonly PathIndexRecord rec = ref _pathIndex[i];
                if (rec.PathByteLength != target.Length)
                    continue;

                EnsureBufferCapacity(rec.PathByteLength);
                _pathBlobAccessor.ReadArray(rec.PathBlobOffset, _buffer, 0, rec.PathByteLength);

                if (_buffer.AsSpan(0, rec.PathByteLength).SequenceEqual(target))
                    return i;
            }

            return -1;
        }

        private static List<List<byte[]>> ParseKeywordPattern(string pattern)
        {
            var result = new List<List<byte[]>>();
            if (string.IsNullOrWhiteSpace(pattern))
                return result;

            var currentGroup = new List<byte[]>();
            int i = 0;
            int len = pattern.Length;

            while (i < len)
            {
                while (i < len && char.IsWhiteSpace(pattern[i]))
                    i++;

                if (i >= len)
                    break;

                if (pattern[i] == '|')
                {
                    if (currentGroup.Count > 0)
                    {
                        result.Add(currentGroup);
                        currentGroup = new List<byte[]>();
                    }

                    i++;
                    continue;
                }

                string token;

                if (pattern[i] == '"')
                {
                    i++;
                    int start = i;

                    while (i < len && pattern[i] != '"')
                        i++;

                    token = pattern.Substring(start, i - start);

                    if (i < len && pattern[i] == '"')
                        i++;
                }
                else
                {
                    int start = i;

                    while (i < len && !char.IsWhiteSpace(pattern[i]) && pattern[i] != '|')
                        i++;

                    token = pattern.Substring(start, i - start);
                }

                if (!string.IsNullOrEmpty(token))
                {
                    byte[] encoded = Utf8.GetBytes(token);
                    if (encoded.Length > 0)
                        currentGroup.Add(encoded);
                }
            }

            if (currentGroup.Count > 0)
                result.Add(currentGroup);

            for (int g = 0; g < result.Count; g++)
                result[g].Sort((a, b) => b.Length.CompareTo(a.Length));

            return result;
        }

        private static bool MatchesKeywordPattern(ReadOnlySpan<byte> pathBytes, List<List<byte[]>> groups)
        {
            for (int g = 0; g < groups.Count; g++)
            {
                List<byte[]> andTerms = groups[g];
                bool andMatched = true;

                for (int t = 0; t < andTerms.Count; t++)
                {
                    if (pathBytes.IndexOf(andTerms[t]) < 0)
                    {
                        andMatched = false;
                        break;
                    }
                }

                if (andMatched)
                    return true;
            }

            return false;
        }

        private static HashListHeader ReadHeader(Stream stream)
        {
            Span<byte> i32 = stackalloc byte[4];
            Span<byte> i64 = stackalloc byte[8];

            ReadExact(stream, i32);
            int version = BinaryPrimitives.ReadInt32LittleEndian(i32);

            ReadExact(stream, i32);
            int flags = BinaryPrimitives.ReadInt32LittleEndian(i32);

            ReadExact(stream, i32);
            int entryCount = BinaryPrimitives.ReadInt32LittleEndian(i32);

            ReadExact(stream, i32);
            int pathCount = BinaryPrimitives.ReadInt32LittleEndian(i32);

            ReadExact(stream, i64);
            long entryIndexOffset = BinaryPrimitives.ReadInt64LittleEndian(i64);

            ReadExact(stream, i64);
            long entryIndexLength = BinaryPrimitives.ReadInt64LittleEndian(i64);

            ReadExact(stream, i64);
            long pathIndexOffset = BinaryPrimitives.ReadInt64LittleEndian(i64);

            ReadExact(stream, i64);
            long pathIndexLength = BinaryPrimitives.ReadInt64LittleEndian(i64);

            ReadExact(stream, i64);
            long pathBlobOffset = BinaryPrimitives.ReadInt64LittleEndian(i64);

            ReadExact(stream, i64);
            long pathBlobLength = BinaryPrimitives.ReadInt64LittleEndian(i64);

            return new HashListHeader(
                version,
                flags,
                entryCount,
                pathCount,
                entryIndexOffset,
                entryIndexLength,
                pathIndexOffset,
                pathIndexLength,
                pathBlobOffset,
                pathBlobLength);
        }

        private static EntryIndexRecord[] ReadEntryIndex(Stream stream, long dataAreaStart, HashListHeader header)
        {
            var arr = new EntryIndexRecord[header.EntryCount];
            stream.Position = dataAreaStart + header.EntryIndexOffset;

            Span<byte> hashBuf = stackalloc byte[Hash256.Size];
            Span<byte> i32 = stackalloc byte[4];
            Span<byte> i64 = stackalloc byte[8];

            for (int i = 0; i < arr.Length; i++)
            {
                ReadExact(stream, hashBuf);
                ReadExact(stream, i64);
                ulong size = BinaryPrimitives.ReadUInt64LittleEndian(i64);

                ReadExact(stream, i32);
                int pathStartIndex = BinaryPrimitives.ReadInt32LittleEndian(i32);

                ReadExact(stream, i32);
                int pathCount = BinaryPrimitives.ReadInt32LittleEndian(i32);

                arr[i] = new EntryIndexRecord(new Hash256(hashBuf), size, pathStartIndex, pathCount);
            }

            return arr;
        }

        private static PathIndexRecord[] ReadPathIndex(Stream stream, long dataAreaStart, HashListHeader header)
        {
            var arr = new PathIndexRecord[header.PathCount];
            stream.Position = dataAreaStart + header.PathIndexOffset;

            Span<byte> i32 = stackalloc byte[4];
            Span<byte> i64 = stackalloc byte[8];

            for (int i = 0; i < arr.Length; i++)
            {
                ReadExact(stream, i64);
                long pathBlobOffset = BinaryPrimitives.ReadInt64LittleEndian(i64);

                ReadExact(stream, i32);
                int pathByteLength = BinaryPrimitives.ReadInt32LittleEndian(i32);

                ReadExact(stream, i32);
                int entryId = BinaryPrimitives.ReadInt32LittleEndian(i32);

                arr[i] = new PathIndexRecord(pathBlobOffset, pathByteLength, entryId);
            }

            return arr;
        }

        private void EnsureBufferCapacity(int len)
        {
            if (_buffer.Length >= len) return;
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = ArrayPool<byte>.Shared.Rent(len);
        }

        private void EnsureKeywordCapacity(int len)
        {
            if (_keywordBuffer.Length >= len) return;
            ArrayPool<byte>.Shared.Return(_keywordBuffer);
            _keywordBuffer = ArrayPool<byte>.Shared.Rent(len);
        }

        private static void ReadExact(Stream stream, Span<byte> buffer)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int n = stream.Read(buffer.Slice(total));
                if (n <= 0)
                    throw new EndOfStreamException();
                total += n;
            }
        }

        public void Dispose()
        {
            _pathBlobAccessor.Dispose();
            _mmf.Dispose();
            _stream.Dispose();
            ArrayPool<byte>.Shared.Return(_buffer);
            ArrayPool<byte>.Shared.Return(_keywordBuffer);
        }
    }
}