// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.ZA;

namespace KM.Formats.SwSh;

/// <summary>Source-ordered FresnelBlend layer5→6→7 arithmetic; not a complete lighting shader.</summary>
public static class ZaSwShEelektrossShader
{
    public sealed record ColorCut(float[] EmissionWeighted, float[] ComplementWeighted);
    public static ColorCut Evaluate(ReadOnlySpan<float> values, ReadOnlySpan<float> mask)
    {
        Require(values, mask);
        var owned = values.ToArray();
        float V(int buffer, int word) => owned[ZaSwShEelektrossMaterial.Index(buffer, word)];
        var e0 = V(7, 33); var e1 = V(7, 34); var e2 = V(7, 35);
        var sum01 = mask[0] + mask[1]; var sum012 = mask[2] + sum01; var sum = mask[3] + sum012;
        var residual = Math.Clamp(1f - sum, 0f, 1f);
        var q0 = MathF.Max(0f, 1f - e0); var q1 = MathF.Max(0f, 1f - e1); var q2 = MathF.Max(0f, 1f - e2);
        var emission = new float[3]; var complement = new float[3];
        for (var c = 0; c < 3; c++)
        {
            var a = e1 * V(8, 56 + c);
            var d = residual * V(8, 56 + c); d = q0 * d;
            a = MathF.FusedMultiplyAdd(mask[0], MathF.FusedMultiplyAdd(e1, V(8, 60 + c), -a), a);
            d = MathF.FusedMultiplyAdd(mask[0], MathF.FusedMultiplyAdd(q1, V(8, 60 + c), -d), d);
            emission[c] = MathF.FusedMultiplyAdd(mask[1], MathF.FusedMultiplyAdd(e2, V(8, 64 + c), -a), a);
            complement[c] = MathF.FusedMultiplyAdd(mask[1], MathF.FusedMultiplyAdd(q2, V(8, 64 + c), -d), d);
        }
        return new(emission, complement);
    }

    public static (float U, float V) LayerMaskUv(ReadOnlySpan<float> values, float u, float v)
    {
        Require(values, [0, 0, 0, 0]);
        if (!float.IsFinite(u) || !float.IsFinite(v)) throw new InvalidDataException("Nonfinite source UV.");
        var x = values[ZaSwShEelektrossMaterial.Index(8, 4)]; var y = values[ZaSwShEelektrossMaterial.Index(8, 5)];
        var z = values[ZaSwShEelektrossMaterial.Index(8, 6)]; var w = values[ZaSwShEelektrossMaterial.Index(8, 7)];
        return ((u - z) * x, MathF.FusedMultiplyAdd(-(v - w), y, 1f));
    }

    // Fixed private register ABI: mask sample RGBA148..151 (read-only), color-cut
    // outputs140..142 emission and143..145 complement. Scratch60..69 only.
    // Native C5 words52..70 correspond one-to-one with the owned material rows.
    // Caller owns scheduling, register allocation, texture sample, cbuf size,
    // lighting/output composition and the complete container admission guard.
    public static ulong[] CreateLayerColorKernel()
    {
        var code = new List<ulong>();
        void Emit(ulong word) => code.Add(word);
        void Load(int d, int buffer, int word) => Emit(0x4c98078000070000UL | 5UL << 34
            | (ulong)ZaSwShEelektrossMaterial.ScalarWord(buffer, word) << 20 | (uint)d);
        void Const(int d, float v) => Emit(0x010000000007f000UL | (ulong)BitConverter.SingleToUInt32Bits(v) << 20 | (uint)d);
        void Mul(int d, int a, int b) => Emit(0x5c68100000070000UL | (ulong)b << 20 | (uint)a << 8 | (uint)d);
        void Add(int d, int a, int b) => Emit(0x5c58100000070000UL | (ulong)b << 20 | (uint)a << 8 | (uint)d);
        void Sub(int d, int a, int b) => Emit(0x5c58300000070000UL | (ulong)b << 20 | (uint)a << 8 | (uint)d);
        void Max(int d, int a, int b) => Emit(0x5c60178000070000UL | (ulong)b << 20 | (uint)a << 8 | (uint)d);
        void Fma(int d, int a, int b, int c, bool negativeC = false) => Emit((negativeC ? 0x59a2000000070000UL : 0x59a0000000070000UL)
            | (ulong)c << 39 | (ulong)b << 20 | (uint)a << 8 | (uint)d);
        void Blend(int target, int factor, int color, int mask)
        { Fma(69, factor, color, target, true); Fma(target, mask, 69, target); }
        Const(60, 1); Const(61, 0);
        Add(62, 148, 149); Add(62, 150, 62); Add(62, 151, 62); Sub(62, 60, 62);
        Emit(0x5c5c100000070000UL | 255UL << 20 | 62UL << 8 | 62);
        Load(63, 7, 33); Sub(63, 60, 63); Max(63, 63, 61);
        Load(64, 7, 34); Sub(65, 60, 64); Max(65, 65, 61);
        Load(66, 7, 35); Sub(67, 60, 66); Max(67, 67, 61);
        for (var c = 0; c < 3; c++)
        {
            Load(68, 8, 56 + c); Mul(140 + c, 64, 68); Mul(143 + c, 62, 68); Mul(143 + c, 63, 143 + c);
            Load(68, 8, 60 + c); Blend(140 + c, 64, 68, 148); Blend(143 + c, 65, 68, 148);
            Load(68, 8, 64 + c); Blend(140 + c, 66, 68, 149); Blend(143 + c, 67, 68, 149);
        }
        return code.ToArray();
    }

    public static bool TryCreateCompleteFragment(out byte[] bytes, out string unresolvedProducer)
    {
        bytes = [];
        unresolvedProducer = "FresnelBlend runtime selector defaults, interpolated UV/texture sample and complete downstream Fresnel/normal/view/lighting/alpha/native-parent composition are not qualified.";
        return false;
    }
    private static void Require(ReadOnlySpan<float> values, ReadOnlySpan<float> mask)
    {
        if (values.Length != ZaSwShEelektrossMaterial.Inputs.Count || mask.Length != 4)
            throw new InvalidDataException("FresnelBlend color-cut interface length changed.");
        foreach (var value in values) if (!float.IsFinite(value)) throw new InvalidDataException("Nonfinite material cut input.");
        foreach (var value in mask) if (!float.IsFinite(value)) throw new InvalidDataException("Nonfinite layer-mask cut input.");
    }
}
