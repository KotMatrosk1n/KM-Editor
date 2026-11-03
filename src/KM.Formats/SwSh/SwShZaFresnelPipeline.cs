// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;

namespace KM.Formats.SwSh;

/// <summary>Composes the authored ZA gem surface before native scene lighting.</summary>
internal static class SwShZaFresnelPipeline
{
    // REQUIRED paired tangent vertex stage, native perspective reciprocal74,
    // native environment texture LOD bias78, camera C3[76..78], owned material
    // uniforms and declared source samplers8..14. No resource/input defaults.
    // Outputs source diffuse56..58, local probe64..66, emission68..70 and
    // raw face-corrected source worldN99..101. Native lighting must consume the
    // normal with the face sign applied exactly once. This is not a parent FS.
    internal static (ulong Instruction, ulong Control)[] Create(
        int layerUv, int aoUv, int sourceVariant, int maskUv, int probeWidth)
    {
        if (sourceVariant is not (0 or 2 or 6 or 8) || sourceVariant < 6 && maskUv != 0
            || probeWidth is < 1 or > 512 || !BitOperations.IsPow2((uint)probeWidth))
            throw new InvalidDataException("ZA gem pipeline requires qualified selectors and the authored probe width.");
        bool masked = sourceVariant >= 6;
        var code = new List<(ulong Instruction, ulong Control)>();
        code.AddRange(SwShZaFresnelInputs.Geometry(layerUv == 1 || aoUv == 1 || masked && maskUv == 1));
        code.AddRange(SwShZaFresnelUv.Create(layerUv, aoUv));
        if (masked) code.AddRange(SwShZaFresnelUv.EmissionMask(maskUv));
        Emit(0x5c98078004e7005fUL); // Explicit caller LOD bias78 ->95, after UV scratch expires.
        code.AddRange(SwShZaFresnelSamples.Surface(masked));
        code.AddRange(SwShZaFresnelNormal.Create());
        code.AddRange(SwShZaFresnelInputs.Position());
        code.AddRange(SwShZaFresnelView.Create());
        for (var c = 0; c < 3; c++)
            Emit(0x4c98078000070000UL | 5UL << 34 | (ulong)(52 + c) << 20 | (uint)(83 + c));
        code.AddRange(SwShZaFresnelWeight.Create());
        // Emission still needs raw base56..58; diffuse overwrites those slots.
        code.AddRange(sourceVariant switch
        {
            0 => SwShZaFresnelSurface.Emission0(),
            2 => SwShZaFresnelSurface.Emission2(),
            6 => SwShZaFresnelSurface.Emission6(),
            8 => SwShZaFresnelSurface.Emission8(),
            _ => throw new InvalidDataException("ZA gem source variant is unqualified.")
        });
        code.AddRange(SwShZaFresnelSurface.Diffuse());
        code.AddRange(SwShZaFresnelInputs.Position());
        Emit(0x010000000007f000UL | (ulong)BitConverter.SingleToUInt32Bits(BitOperations.Log2((uint)probeWidth)) << 20 | 87);
        code.AddRange(SwShZaFresnelProbe.Create());
        code.AddRange(SwShZaFresnelSamples.Probe());
        code.AddRange(SwShZaFresnelProbeColor.Create());
        return code.ToArray();

        void Emit(ulong word) => code.Add((word, SwShShaderScheduling.Serial(word)));
    }
}
