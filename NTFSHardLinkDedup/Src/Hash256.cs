using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace NTFSHardLinkDedup.Src
{
    public readonly struct Hash256 : IEquatable<Hash256>
    {
        public const int Size = 32;

        private readonly ulong _a;
        private readonly ulong _b;
        private readonly ulong _c;
        private readonly ulong _d;

        public static Hash256 DirectoryMarker { get; } =
    new Hash256(ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Hash256(ReadOnlySpan<byte> src)
        {
            if (src.Length != Size)
                throw new ArgumentException("SHA256 hash must be exactly 32 bytes.", nameof(src));

            _a = BinaryPrimitives.ReadUInt64LittleEndian(src.Slice(0, 8));
            _b = BinaryPrimitives.ReadUInt64LittleEndian(src.Slice(8, 8));
            _c = BinaryPrimitives.ReadUInt64LittleEndian(src.Slice(16, 8));
            _d = BinaryPrimitives.ReadUInt64LittleEndian(src.Slice(24, 8));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Hash256(ulong a, ulong b, ulong c, ulong d)
        {
            _a = a;
            _b = b;
            _c = c;
            _d = d;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyTo(Span<byte> dst)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(dst.Slice(0, 8), _a);
            BinaryPrimitives.WriteUInt64LittleEndian(dst.Slice(8, 8), _b);
            BinaryPrimitives.WriteUInt64LittleEndian(dst.Slice(16, 8), _c);
            BinaryPrimitives.WriteUInt64LittleEndian(dst.Slice(24, 8), _d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Hash256 other)
            => _a == other._a && _b == other._b && _c == other._c && _d == other._d;

        public override bool Equals(object? obj) => obj is Hash256 other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(_a, _b, _c, _d);

        public static bool operator ==(Hash256 left, Hash256 right) => left.Equals(right);
        public static bool operator !=(Hash256 left, Hash256 right) => !left.Equals(right);
    }

}
