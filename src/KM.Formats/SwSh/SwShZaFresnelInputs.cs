// SPDX-License-Identifier: GPL-3.0-only
namespace KM.Formats.SwSh;

/// <summary>Reads the native varyings used by the ZA gem material components.</summary>
internal static class SwShZaFresnelInputs
{
    // Requires the paired authored-tangent vertex stage and native perspective
    // reciprocal74. UV1 is read only when a source selector actually uses it.
    // No absent attribute, normal, UV or camera values are supplied here.
    internal static (ulong Instruction, ulong Control)[] Geometry(bool secondaryUv)
    {
        var words = new List<ulong>();
        // The paired stage stores the skinned world N/T/W in these varyings.
        int[] attributes = [0x9c, 0xac, 0xbc, 0xf8, 0x10c, 0x118, 0x11c];
        for (var c = 0; c < attributes.Length; c++) words.Add(Interpolate(64 + c, attributes[c]));
        // Native vertex input map1 passes through to these outputs unchanged.
        words.Add(Interpolate(72, 0x110)); words.Add(Interpolate(73, 0x114));
        if (secondaryUv)
        {
            words.Add(Interpolate(76, 0x120)); words.Add(Interpolate(77, 0x124));
        }
        // The source uses the raw signed face value, not a synthesized boolean.
        words.Add(0xe083ffbfcff7ff47UL);
        words.AddRange(Position().Select(p => p.Instruction));
        return words.Select(w => (w, SwShShaderScheduling.Serial(w))).ToArray();
    }

    // Normal reconstruction expires80..82. Reload the native world position
    // before the view component; camera C3[76..78] shares its native units.
    internal static (ulong Instruction, ulong Control)[] Position()
        => Enumerable.Range(0, 3).Select(c =>
        {
            var word = Interpolate(80 + c, 0xe0 + c * 4);
            return (word, SwShShaderScheduling.Serial(word));
        }).ToArray();

    internal static byte[] Vertex(byte[] template, bool secondaryUv)
        => secondaryUv ? SwShTangentNormalShader.PoltchageistVertex(template)
            : SwShTangentNormalShader.Vertex(template);

    private static ulong Interpolate(int destination, int attribute)
        => 0xe043ff800007ff00UL & ~(255UL << 30)
            | (ulong)(attribute / 4) << 30 | 74UL << 20 | (uint)destination;
}
