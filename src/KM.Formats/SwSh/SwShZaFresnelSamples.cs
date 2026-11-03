// SPDX-License-Identifier: GPL-3.0-only
namespace KM.Formats.SwSh;

/// <summary>Original ZA gem sample operations with owned native sampler slots.</summary>
internal static class SwShZaFresnelSamples
{
    internal static (string Name, int Slot)[] Samplers(bool masked)
        => masked ? [.. Samplers(false), ("SourceGemMaskTex", 14)]
            : [("SourceGemBaseTex", 8), ("SourceGemLayerTex", 9), ("SourceGemNormalTex", 10),
                ("SourceGemLayerNormalTex", 11), ("SourceGemAoTex", 12), ("SourceGemProbeTex", 13)];

    // Requires source UV outputs83..92 and optional mask UV93/94.
    // Caller supplies the environment texture LOD bias in95 AFTER UV work.
    // This component supplies no bias, texture, sampler or missing-UV defaults.
    // Outputs raw base56..58, layer60..62, AO63, optional mask79,
    // and normal pairs80..83. Order protects UV83/84 until base is sampled.
    // Sampler slots8..14 are declared separately; native0..7 stay available.
    internal static (ulong Instruction, ulong Control)[] Surface(bool masked)
    {
        var words = new List<ulong>
        {
            0xC13A0183A5F75338UL, // SourceGemBaseTex, source fragment6 instruction105.
            0xC13A01A3A5F75B3CUL, // SourceGemLayerTex, source fragment6 instruction107.
            0xC13A0200A5F7593FUL, // SourceGemAoTex, source fragment6 instruction103.
        };
        if (masked) words.Add(0xC13A0240A5F75D4FUL); // Authored MaskedEmissionMap14/TEX36.
        words.Add(0xC13801C1A5F75550UL); // SourceGemNormalTex, source instruction102.
        words.Add(0xC13801E1A5F75752UL); // SourceGemLayerNormalTex, source instruction101.
        return words.Select(w => (w, SwShShaderScheduling.Serial(w))).ToArray();
    }

    // Call only after the probe coordinate component: direction83..85/LOD86.
    // Writes raw linear HDR probe64..66. No color conversion or alpha overwrite.
    internal static (ulong Instruction, ulong Control)[] Probe()
    {
        const ulong word = 0xD9A2022425575340UL;
        return [(word, SwShShaderScheduling.Serial(word))];
    }
}
