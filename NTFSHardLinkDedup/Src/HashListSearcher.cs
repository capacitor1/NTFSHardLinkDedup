using System;
using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NTFSHardLinkDedup.Src
{
    public sealed class HashListSearcher(
        string sourcePath,
        HashListSearcher.HashListInfo info,
        HashListSearcher.EntryRecord[] entries,
        string[] paths,
        int[] pathEntryIds,
        FrozenDictionary<Hash256, int> hashToEntryId) : IDisposable
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        private readonly string _sourcePath = sourcePath;
        private readonly HashListInfo _info = info;
        private readonly EntryRecord[] _entries = entries;
        private readonly string[] _paths = paths;
        private readonly int[] _pathEntryIds = pathEntryIds;
        private readonly FrozenDictionary<Hash256, int> _hashToEntryId = hashToEntryId;

        public int MaxResult { get; set; } = 20_000;

        private static class ChunkIds
        {
            public static ReadOnlySpan<byte> HVER => "HVER"u8;
            public static ReadOnlySpan<byte> HCNT => "HCNT"u8;
            public static ReadOnlySpan<byte> HREC => "HREC"u8;
            public static ReadOnlySpan<byte> HASH => "HASH"u8;
        }

        private static class FormatConstants
        {
            public static ReadOnlySpan<byte> Magic => "HashList"u8;
            public const int Version = 1;
        }

        public readonly struct HashPathPair(Hash256 hash, string path, ulong size)
        {
            public readonly Hash256 Hash = hash;
            public readonly string Path = path;
            public readonly ulong Size = size;
        }

        public readonly struct SearchResult(List<HashPathPair> items, bool exceededMaxResults)
        {
            public readonly bool ExceededMaxResults = exceededMaxResults;
            public readonly List<HashPathPair> Items = items;
        }

        public readonly struct HashListInfo(int hashCount, int fileCount)
        {
            public readonly int HashCount = hashCount;
            public readonly int FileCount = fileCount;
        }

        public readonly struct EntryRecord(Hash256 hash, ulong size, int pathStartIndex, int pathCount)
        {
            public readonly Hash256 Hash = hash;
            public readonly ulong Size = size;
            public readonly int PathStartIndex = pathStartIndex;
            public readonly int PathCount = pathCount;
        }

        public int EntryCount => _entries.Length;

        public string SourcePath => _sourcePath;

        public static async Task<HashListSearcher> OpenAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Path is null or empty.", nameof(filePath));

            byte[] fileBytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
            return Parse(filePath, fileBytes);
        }

        public static bool IsValidFile(string hashListPath)
        {
            if (string.IsNullOrWhiteSpace(hashListPath))
                return false;

            try
            {
                if (!File.Exists(hashListPath))
                    return false;

                using var fs = new FileStream(hashListPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (fs.Length < 16)
                    return false;

                Span<byte> magic = stackalloc byte[8];
                Span<byte> i64 = stackalloc byte[8];

                ReadExact(fs, magic);
                if (!magic.SequenceEqual(FormatConstants.Magic))
                    return false;

                ReadExact(fs, i64);
                long dataLength = BinaryPrimitives.ReadInt64LittleEndian(i64);
                if (dataLength < 0 || fs.Length < 16 + dataLength)
                    return false;

                byte[] payload = new byte[dataLength];
                ReadExact(fs, payload);

                return ValidateChunks(payload);
            }
            catch
            {
                return false;
            }
        }

        public HashListInfo GetCounts() => _info;

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

        public SearchResult FindByKeyword(ReadOnlySpan<char> keyword)
        {
            string pattern = keyword.ToString().Trim();

            if (pattern.Length == 1 && pattern[0] == '*')
                return FindAll();

            var groups = ParseKeywordPattern(pattern);

            var items = new List<HashPathPair>(Math.Min(MaxResult, 256));
            if (groups.Count == 0)
                return new SearchResult(items, false);

            bool exceeded = false;

            for (int pathId = 0; pathId < _paths.Length; pathId++)
            {
                string path = _paths[pathId];

                if (!MatchesKeywordPattern(path, groups))
                    continue;

                if (items.Count >= MaxResult)
                {
                    exceeded = true;
                    break;
                }

                ref readonly EntryRecord entry = ref _entries[_pathEntryIds[pathId]];
                items.Add(new HashPathPair(entry.Hash, path, entry.Size));
            }

            return new SearchResult(items, exceeded);
        }

        public bool TryGetEntryById(int entryId, out Hash256 hash, out ulong size, out string[]? paths)
        {
            if ((uint)entryId >= (uint)_entries.Length)
            {
                hash = default;
                size = 0;
                paths = null;
                return false;
            }

            ref readonly EntryRecord rec = ref _entries[entryId];
            hash = rec.Hash;
            size = rec.Size;

            var arr = new string[rec.PathCount];
            Array.Copy(_paths, rec.PathStartIndex, arr, 0, rec.PathCount);
            paths = arr;
            return true;
        }

        public HashStorageBuilder RestoreToBuilder(int initialCapacity = 0)
        {
            var builder = new HashStorageBuilder(initialCapacity > 0 ? initialCapacity : _entries.Length);

            Span<byte> hashBytes = stackalloc byte[Hash256.Size];

            for (int entryId = 0; entryId < _entries.Length; entryId++)
            {
                ref readonly EntryRecord entry = ref _entries[entryId];
                entry.Hash.CopyTo(hashBytes);

                if (entry.Hash == Hash256.DirectoryMarker)
                {
                    for (int i = 0; i < entry.PathCount; i++)
                    {
                        builder.AddDir(_paths[entry.PathStartIndex + i].AsSpan());
                    }
                }
                else
                {
                    for (int i = 0; i < entry.PathCount; i++)
                    {
                        builder.AddFile(hashBytes, _paths[entry.PathStartIndex + i].AsSpan(), entry.Size);
                    }
                }
            }

            return builder;
        }

        private SearchResult FindAll()
        {
            var items = new List<HashPathPair>(Math.Min(MaxResult, 256));
            bool exceeded = false;

            for (int pathId = 0; pathId < _paths.Length; pathId++)
            {
                if (items.Count >= MaxResult)
                {
                    exceeded = true;
                    break;
                }

                ref readonly EntryRecord entry = ref _entries[_pathEntryIds[pathId]];
                items.Add(new HashPathPair(entry.Hash, _paths[pathId], entry.Size));
            }

            return new SearchResult(items, exceeded);
        }

        private List<HashPathPair> ExpandEntryToPairs(int entryId)
        {
            ref readonly EntryRecord entry = ref _entries[entryId];
            var list = new List<HashPathPair>(entry.PathCount);

            for (int i = 0; i < entry.PathCount; i++)
            {
                string path = _paths[entry.PathStartIndex + i];
                list.Add(new HashPathPair(entry.Hash, path, entry.Size));
            }

            return list;
        }

        private static HashListSearcher Parse(string sourcePath, byte[] fileBytes)
        {
            ReadOnlySpan<byte> span = fileBytes;

            if (span.Length < 16)
                throw new InvalidDataException("File too small.");

            if (!span.Slice(0, 8).SequenceEqual(FormatConstants.Magic))
                throw new InvalidDataException("Invalid magic.");

            long dataLength = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(8, 8));
            if (dataLength < 0 || fileBytes.Length < 16 + dataLength)
                throw new InvalidDataException("Invalid dataLength.");

            ReadOnlySpan<byte> payload = span.Slice(16, (int)dataLength);

            int? version = null;
            int hashCount = 0;
            int fileCount = 0;

            var entries = new List<EntryRecord>(1024);
            var paths = new List<string>(4096);
            var pathEntryIds = new List<int>(4096);

            int pos = 0;
            while (pos < payload.Length)
            {
                if (payload.Length - pos < 12)
                    throw new InvalidDataException("Broken top-level chunk.");

                ReadOnlySpan<byte> chunkId = payload.Slice(pos, 4);
                long chunkLen = BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(pos + 4, 8));
                pos += 12;

                if (chunkLen < 0 || payload.Length - pos < chunkLen)
                    throw new InvalidDataException("Broken chunk length.");

                ReadOnlySpan<byte> chunkPayload = payload.Slice(pos, (int)chunkLen);

                if (chunkId.SequenceEqual(ChunkIds.HVER))
                {
                    if (chunkPayload.Length != 4)
                        throw new InvalidDataException("Invalid HVER chunk.");

                    version = BinaryPrimitives.ReadInt32LittleEndian(chunkPayload);
                }
                else if (chunkId.SequenceEqual(ChunkIds.HCNT))
                {
                    if (chunkPayload.Length != 8)
                        throw new InvalidDataException("Invalid HCNT chunk.");

                    hashCount = BinaryPrimitives.ReadInt32LittleEndian(chunkPayload.Slice(0, 4));
                    fileCount = BinaryPrimitives.ReadInt32LittleEndian(chunkPayload.Slice(4, 4));
                }
                else if (chunkId.SequenceEqual(ChunkIds.HREC))
                {
                    ParseHrec(chunkPayload, entries, paths, pathEntryIds);
                }

                pos += (int)chunkLen;
            }

            if (version != FormatConstants.Version)
                throw new InvalidDataException($"Unsupported HLF version: {version}");

            var hashDict = new Dictionary<Hash256, int>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
                hashDict[entries[i].Hash] = i;

            return new HashListSearcher(
                sourcePath,
                new HashListInfo(hashCount, fileCount),
                [.. entries],
                [.. paths],
                [.. pathEntryIds],
                hashDict.ToFrozenDictionary());
        }

        private static void ParseHrec(
            ReadOnlySpan<byte> hrecPayload,
            List<EntryRecord> entries,
            List<string> paths,
            List<int> pathEntryIds)
        {
            int pos = 0;

            while (pos < hrecPayload.Length)
            {
                if (hrecPayload.Length - pos < 12)
                    throw new InvalidDataException("Broken HASH chunk.");

                ReadOnlySpan<byte> chunkId = hrecPayload.Slice(pos, 4);
                long chunkLen = BinaryPrimitives.ReadInt64LittleEndian(hrecPayload.Slice(pos + 4, 8));
                pos += 12;

                if (!chunkId.SequenceEqual(ChunkIds.HASH))
                    throw new InvalidDataException("Non-HASH chunk found inside HREC.");

                if (chunkLen < Hash256.Size + 8 || hrecPayload.Length - pos < chunkLen)
                    throw new InvalidDataException("Broken HASH payload.");

                ReadOnlySpan<byte> payload = hrecPayload.Slice(pos, (int)chunkLen);

                var hash = new Hash256(payload.Slice(0, Hash256.Size));
                ulong size = BinaryPrimitives.ReadUInt64LittleEndian(payload.Slice(Hash256.Size, 8));

                int pathStartIndex = paths.Count;
                int pathCount = 0;

                int inner = Hash256.Size + 8;
                while (inner < payload.Length)
                {
                    if (payload.Length - inner < 2)
                        throw new InvalidDataException("Broken path LV.");

                    ushort pathLen = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(inner, 2));
                    inner += 2;

                    if (payload.Length - inner < pathLen)
                        throw new InvalidDataException("Broken path payload.");

                    string path = Utf8.GetString(payload.Slice(inner, pathLen));
                    paths.Add(path);
                    pathEntryIds.Add(entries.Count);

                    inner += pathLen;
                    pathCount++;
                }

                entries.Add(new EntryRecord(hash, size, pathStartIndex, pathCount));
                pos += (int)chunkLen;
            }
        }

        private static bool ValidateChunks(byte[] payload)
        {
            ReadOnlySpan<byte> span = payload;

            int? version = null;
            bool hasHrec = false;

            int pos = 0;
            while (pos < span.Length)
            {
                if (span.Length - pos < 12)
                    return false;

                ReadOnlySpan<byte> chunkId = span.Slice(pos, 4);
                long chunkLen = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(pos + 4, 8));
                pos += 12;

                if (chunkLen < 0 || span.Length - pos < chunkLen)
                    return false;

                ReadOnlySpan<byte> chunkPayload = span.Slice(pos, (int)chunkLen);

                if (chunkId.SequenceEqual(ChunkIds.HVER))
                {
                    if (chunkPayload.Length != 4)
                        return false;

                    version = BinaryPrimitives.ReadInt32LittleEndian(chunkPayload);
                }
                else if (chunkId.SequenceEqual(ChunkIds.HCNT))
                {
                    if (chunkPayload.Length != 8)
                        return false;
                }
                else if (chunkId.SequenceEqual(ChunkIds.HREC))
                {
                    hasHrec = true;

                    int inner = 0;
                    while (inner < chunkPayload.Length)
                    {
                        if (chunkPayload.Length - inner < 12)
                            return false;

                        ReadOnlySpan<byte> hashId = chunkPayload.Slice(inner, 4);
                        long hashLen = BinaryPrimitives.ReadInt64LittleEndian(chunkPayload.Slice(inner + 4, 8));
                        inner += 12;

                        if (!hashId.SequenceEqual(ChunkIds.HASH))
                            return false;

                        if (hashLen < Hash256.Size + 8 || chunkPayload.Length - inner < hashLen)
                            return false;

                        ReadOnlySpan<byte> hashPayload = chunkPayload.Slice(inner, (int)hashLen);
                        int p = Hash256.Size + 8;
                        while (p < hashPayload.Length)
                        {
                            if (hashPayload.Length - p < 2)
                                return false;

                            ushort pathLen = BinaryPrimitives.ReadUInt16LittleEndian(hashPayload.Slice(p, 2));
                            p += 2;

                            if (hashPayload.Length - p < pathLen)
                                return false;

                            p += pathLen;
                        }

                        inner += (int)hashLen;
                    }
                }

                pos += (int)chunkLen;
            }

            return version == FormatConstants.Version && hasHrec;
        }

        private static List<List<string>> ParseKeywordPattern(string pattern)
        {
            var result = new List<List<string>>();
            if (string.IsNullOrWhiteSpace(pattern))
                return result;

            var currentGroup = new List<string>();
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
                        currentGroup = new List<string>();
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
                    currentGroup.Add(token);
            }

            if (currentGroup.Count > 0)
                result.Add(currentGroup);

            for (int g = 0; g < result.Count; g++)
                result[g].Sort((a, b) => b.Length.CompareTo(a.Length));

            return result;
        }

        private static bool MatchesKeywordPattern(string path, List<List<string>> groups)
        {
            for (int g = 0; g < groups.Count; g++)
            {
                List<string> andTerms = groups[g];
                bool andMatched = true;

                for (int t = 0; t < andTerms.Count; t++)
                {
                    if (path.IndexOf(andTerms[t], StringComparison.Ordinal) < 0)
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

        private static void ReadExact(Stream stream, byte[] buffer)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int n = stream.Read(buffer, total, buffer.Length - total);
                if (n <= 0)
                    throw new EndOfStreamException();
                total += n;
            }
        }

        public void Dispose()
        {
            // 仅托管内存，无需释放外部资源
        }
    }
}