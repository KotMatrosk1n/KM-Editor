// SPDX-License-Identifier: GPL-3.0-only
namespace KM.Formats.SwSh;

/// <summary>Compiled ZA FresnelEffect weight with an explicit pixel normal/view dot.</summary>
internal static class SwShZaFresnelWeight
{
    // The caller supplies the source pixel-normal/view dot in R80 and the
    // authored minimum, maximum and angle bias in R83..85. R80 receives the
    // result; R83..85 remain intact. R86..92 are scratch. This component does
    // not provide a normal, camera, material binding or renderer by itself.
    internal static (ulong Instruction, ulong Control)[] Create()
    {
        var words = new List<ulong>();
        void Constant(int d, float v) => words.Add(0x010000000007f000UL
            | (ulong)BitConverter.SingleToUInt32Bits(v) << 20 | (uint)d);
        void Add(int d, int a, int b) => words.Add(0x5c58300000070000UL
            | (ulong)b << 20 | (uint)a << 8 | (uint)d);
        void Multiply(int d, int a, int b) => words.Add(0x5c68100000070000UL
            | (ulong)b << 20 | (uint)a << 8 | (uint)d);
        void Limit(int d, int a, int b, bool maximum) => words.Add(0x5c60138000070000UL
            | (maximum ? 1UL << 42 : 0) | (ulong)b << 20 | (uint)a << 8 | (uint)d);

        Constant(86, 1);
        Limit(88, 80, 86, maximum: false);
        // The original fragment constant C1[2], rather than an invented zero.
        Constant(87, BitConverter.Int32BitsToSingle(0x3727c5ac));
        Limit(88, 88, 87, maximum: true);
        Add(88, 88, 85);
        Constant(87, 0);
        Limit(88, 88, 87, maximum: true);
        Add(90, 86, 88);
        Add(89, 84, 83);
        Multiply(91, 90, 90);
        Multiply(92, 91, 89);
        Multiply(92, 91, 92);
        words.Add(0x59a0000000070000UL | 83UL << 39 | 92UL << 20 | 90UL << 8 | 80);
        return words.Select(w => (w, SwShShaderScheduling.Serial(w))).ToArray();
    }
}
