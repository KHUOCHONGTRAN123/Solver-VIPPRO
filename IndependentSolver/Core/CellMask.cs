using System;
using System.Numerics;

namespace CatDom.CoreSolver
{
    // Two machine words cover the sample boards. Signed extension preserves
    // BigInteger's complement semantics; larger boards use the exact fallback.
    internal readonly struct CellMask : IEquatable<CellMask>
    {
        private readonly ulong low, high;
        private readonly bool negative, wide;
        private readonly BigInteger large;
        private CellMask(ulong low, ulong high, bool negative = false)
        { this.low = low; this.high = high; this.negative = negative; wide = false; large = default; }
        internal CellMask(BigInteger value)
        {
            var limit = BigInteger.One << 128;
            negative = value.Sign < 0;
            var bits = negative ? value + limit : value;
            wide = bits.Sign < 0 || bits >= limit;
            large = wide ? value : default;
            low = wide ? 0 : (ulong)(bits & ulong.MaxValue);
            high = wide ? 0 : (ulong)(bits >> 64);
        }
        internal BigInteger ToBigInteger()
        {
            if (wide) return large;
            var value = ((BigInteger)high << 64) | low;
            return negative ? value - (BigInteger.One << 128) : value;
        }
        internal static CellMask Zero => default;
        internal static CellMask One => new CellMask(1, 0);
        internal bool IsZero => wide ? large.IsZero : !negative && (low | high) == 0;
        public static CellMask operator &(CellMask a, CellMask b) => a.wide || b.wide
            ? new CellMask(a.ToBigInteger() & b.ToBigInteger()) : new CellMask(a.low & b.low, a.high & b.high, a.negative && b.negative);
        public static CellMask operator |(CellMask a, CellMask b) => a.wide || b.wide
            ? new CellMask(a.ToBigInteger() | b.ToBigInteger()) : new CellMask(a.low | b.low, a.high | b.high, a.negative || b.negative);
        public static CellMask operator ^(CellMask a, CellMask b) => a.wide || b.wide
            ? new CellMask(a.ToBigInteger() ^ b.ToBigInteger()) : new CellMask(a.low ^ b.low, a.high ^ b.high, a.negative != b.negative);
        public static CellMask operator ~(CellMask a) => a.wide ? new CellMask(~a.large) : new CellMask(~a.low, ~a.high, !a.negative);
        public static CellMask operator <<(CellMask a, int shift)
        {
            if (shift < 0 || shift >= 128 || a.wide || a.negative) return new CellMask(a.ToBigInteger() << shift);
            if (shift == 0) return a;
            if (shift >= 64)
            {
                if (a.high != 0 || (shift > 64 && (a.low >> (128 - shift)) != 0)) return new CellMask(a.ToBigInteger() << shift);
                return new CellMask(0, a.low << (shift - 64));
            }
            if ((a.high >> (64 - shift)) != 0) return new CellMask(a.ToBigInteger() << shift);
            return new CellMask(a.low << shift, (a.high << shift) | (a.low >> (64 - shift)));
        }
        public static CellMask operator -(CellMask a, CellMask b)
        {
            if (a.wide || b.wide || a.negative || b.negative || a.high < b.high || (a.high == b.high && a.low < b.low))
                return new CellMask(a.ToBigInteger() - b.ToBigInteger());
            return new CellMask(unchecked(a.low - b.low), unchecked(a.high - b.high - (a.low < b.low ? 1UL : 0UL)));
        }
        public bool Equals(CellMask other) => !wide && !other.wide
            ? low == other.low && high == other.high && negative == other.negative : ToBigInteger() == other.ToBigInteger();
        public override bool Equals(object obj) => obj is CellMask mask && Equals(mask);
        public override int GetHashCode() => wide ? large.GetHashCode() : unchecked((int)low ^ (int)(low >> 32) ^ (int)high * 397 ^ (int)(high >> 32) ^ (negative ? -1 : 0));
        public static bool operator ==(CellMask a, CellMask b) => a.Equals(b);
        public static bool operator !=(CellMask a, CellMask b) => !a.Equals(b);
        internal int PopCount()
        {
            if (negative) throw new InvalidOperationException("Negative masks do not have a finite population count.");
            if (!wide) return Count(low) + Count(high);
            int count = 0; var value = large;
            while (!value.IsZero) { value &= value - BigInteger.One; count++; }
            return count;
        }
        private static int Count(ulong value)
        {
            value -= (value >> 1) & 0x5555555555555555UL;
            value = (value & 0x3333333333333333UL) + ((value >> 2) & 0x3333333333333333UL);
            value = (value + (value >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
            return (int)(unchecked(value * 0x0101010101010101UL) >> 56);
        }
    }
}
