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

        private sealed class EntryBucket
        {
            public ulong Size;
            public readonly List<PathItem> Paths;

            public EntryBucket(ulong size, int capacity)
            {
                Size = size;
                Paths = new List<PathItem>(capacity);
            }
        }

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

        public int EntryCount => _map.Count;

        public bool ContainsPath(ReadOnlySpan<char> path)
        {
            if (path.IsEmpty)
                return false;

            return _pathSet.Contains(path.ToString());
        }
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
            if (stream is null) throw new ArgumentNullException(nameof(stream));

            int hashCount = 0;
            int fileCount = 0;

            long hrecPayloadLength = 0;

            foreach (var kv in _map)
            {
                var bucket = kv.Value;
                hashCount++;

                fileCount += bucket.Paths.Count;

                long hashPayloadLen = Hash256.Size + 8; // hash + size
                for (int i = 0; i < bucket.Paths.Count; i++)
                {
                    int byteCount = Utf8.GetByteCount(bucket.Paths[i].Path);
                    if ((uint)byteCount > ushort.MaxValue)
                        throw new InvalidDataException("Path UTF-8 byte length exceeds ushort.MaxValue.");

                    hashPayloadLen += 2 + byteCount;
                }

                hrecPayloadLength += 4 + 8 + hashPayloadLen; // HASH + payloadLen + payload
            }

            long hverChunkLen = 4 + 8 + 4;       // id + len + version(int32)
            long hcntChunkLen = 4 + 8 + 8;       // id + len + hashCount(int32)+fileCount(int32)
            long hrecChunkLen = 4 + 8 + hrecPayloadLength;

            long dataLength = hverChunkLen + hcntChunkLen + hrecChunkLen;

            Span<byte> i32 = stackalloc byte[4];
            Span<byte> i64 = stackalloc byte[8];
            Span<byte> hashBuf = stackalloc byte[Hash256.Size];

            stream.Write(FormatConstants.Magic);
            BinaryPrimitives.WriteInt64LittleEndian(i64, dataLength);
            stream.Write(i64);

            // HVER
            WriteChunkHeader(stream, ChunkIds.HVER, 4);
            BinaryPrimitives.WriteInt32LittleEndian(i32, FormatConstants.Version);
            stream.Write(i32);

            // HCNT
            WriteChunkHeader(stream, ChunkIds.HCNT, 8);
            BinaryPrimitives.WriteInt32LittleEndian(i32, hashCount);
            stream.Write(i32);
            BinaryPrimitives.WriteInt32LittleEndian(i32, fileCount);
            stream.Write(i32);

            // HREC
            WriteChunkHeader(stream, ChunkIds.HREC, hrecPayloadLength);

            byte[] rented = ArrayPool<byte>.Shared.Rent(4096);

            try
            {
                foreach (var kv in _map)
                {
                    Hash256 hash = kv.Key;
                    EntryBucket bucket = kv.Value;

                    long hashPayloadLen = Hash256.Size + 8;
                    for (int i = 0; i < bucket.Paths.Count; i++)
                    {
                        int byteCount = Utf8.GetByteCount(bucket.Paths[i].Path);
                        hashPayloadLen += 2 + byteCount;
                    }

                    WriteChunkHeader(stream, ChunkIds.HASH, hashPayloadLen);

                    hash.CopyTo(hashBuf);
                    stream.Write(hashBuf);

                    BinaryPrimitives.WriteUInt64LittleEndian(i64, bucket.Size);
                    stream.Write(i64);

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

                        BinaryPrimitives.WriteUInt16LittleEndian(i32[..2], (ushort)written);
                        stream.Write(i32[..2]);
                        stream.Write(rented.AsSpan(0, written));
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        public static HashStorageBuilder RestoreFromFile(string filePath, int initialCapacity = 0)
        {
            if (!HashListSearcher.IsValidFile(filePath))
                throw new InvalidDataException("Invalid hashlist file.");

            var searcher = HashListSearcher.OpenAsync(filePath).GetAwaiter().GetResult();
            using (searcher)
            {
                return searcher.RestoreToBuilder(initialCapacity);
            }
        }

        private static void WriteChunkHeader(Stream stream, ReadOnlySpan<byte> chunkId, long payloadLength)
        {
            if (chunkId.Length != 4)
                throw new ArgumentException("Chunk id must be 4 bytes.", nameof(chunkId));

            Span<byte> i64 = stackalloc byte[8];
            stream.Write(chunkId);
            BinaryPrimitives.WriteInt64LittleEndian(i64, payloadLength);
            stream.Write(i64);
        }
    }
}
