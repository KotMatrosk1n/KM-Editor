// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using System.Text;

namespace KM.SwSh.Randomizer;

internal sealed class DeterministicRandom
{
    private ulong s0;
    private ulong s1;
    private ulong s2;
    private ulong s3;

    private DeterministicRandom(ReadOnlySpan<byte> seed)
    {
        s0 = BitConverter.ToUInt64(seed[0..8]);
        s1 = BitConverter.ToUInt64(seed[8..16]);
        s2 = BitConverter.ToUInt64(seed[16..24]);
        s3 = BitConverter.ToUInt64(seed[24..32]);

        if ((s0 | s1 | s2 | s3) == 0)
        {
            s0 = 0x9E3779B97F4A7C15UL;
        }
    }

    public static DeterministicRandom Create(string seed, string module)
    {
        var material = Encoding.UTF8.GetBytes($"{seed}\n{module}");
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(material, hash);
        return new DeterministicRandom(hash);
    }

    public ulong NextUInt64()
    {
        var result = RotateLeft(s1 * 5, 7) * 9;
        var t = s1 << 17;

        s2 ^= s0;
        s3 ^= s1;
        s1 ^= s2;
        s0 ^= s3;

        s2 ^= t;
        s3 = RotateLeft(s3, 45);

        return result;
    }

    public int NextInt(int maxExclusive)
    {
        if (maxExclusive <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Upper bound must be greater than zero.");
        }

        var bound = (ulong)maxExclusive;
        var threshold = (0UL - bound) % bound;
        while (true)
        {
            var value = NextUInt64();
            if (value >= threshold)
            {
                return (int)(value % bound);
            }
        }
    }

    public bool NextBool()
    {
        return (NextUInt64() & 1UL) == 1UL;
    }

    public T Pick<T>(IReadOnlyList<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            throw new ArgumentException("Cannot pick from an empty list.", nameof(values));
        }

        return values[NextInt(values.Count)];
    }

    public void Shuffle<T>(IList<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        for (var i = values.Count - 1; i > 0; i--)
        {
            var j = NextInt(i + 1);
            (values[i], values[j]) = (values[j], values[i]);
        }
    }

    private static ulong RotateLeft(ulong value, int offset)
    {
        return (value << offset) | (value >> (64 - offset));
    }
}
