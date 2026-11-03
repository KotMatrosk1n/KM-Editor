// SPDX-License-Identifier: GPL-3.0-only
namespace KM.Formats.SwSh;

/// <summary>Preserves the compiled ZA two-normal surface calculation.</summary>
internal static class SwShZaFresnelNormal
{
    // Input R80/81: source NormalMap RG; R82/83: NormalMap1 RG.
    // C5[60/61] must contain the authored NormalHeight/NormalHeight1.
    // Output R96..98: source tangent-space normal, before TBN/face/view.
    // R80..83 and R86..92 are scratch; other registers remain intact.
    // Pixel inputs, both samplers/UV sets, uniforms and the tangent parent
    // still require binding; this component is not a complete renderer.
    internal static (ulong Instruction, ulong Control)[] Create()
    {
        ulong[] words =
        [
            0x010400000007f05aUL, // Source fragment0 instruction 98.
            0x4c68101403d75256UL, // Source fragment0 instruction 101.
            0x4c68101403d75357UL, // Source fragment0 instruction 102.
            0x51a22d1403d75658UL, // Source fragment0 instruction 103.
            0x51a22d1403d75757UL, // Source fragment0 instruction 104.
            0x4c68101403c75056UL, // Source fragment0 instruction 105.
            0x4c68101403c75151UL, // Source fragment0 instruction 106.
            0x5c68100005875859UL, // Source fragment0 instruction 107.
            0x51a22d1403c75656UL, // Source fragment0 instruction 108.
            0x51a22d1403c75151UL, // Source fragment0 instruction 109.
            0x59a42c8005775759UL, // Source fragment0 instruction 111.
            0x5c6810000567565bUL, // Source fragment0 instruction 112.
            0x5c69100005875650UL, // Source fragment0 instruction 113.
            0x3859103f80075959UL, // Source fragment0 instruction 117.
            0x5080000000875959UL, // Source fragment0 instruction 119.
            0x59a42d800517515bUL, // Source fragment0 instruction 120.
            0x59a1280005775150UL, // Source fragment0 instruction 121.
            0x3859103f80075b5bUL, // Source fragment0 instruction 122.
            0x5080000000875b5bUL, // Source fragment0 instruction 123.
            0x3858103f80075952UL, // Source fragment0 instruction 124.
            0x508000000047525cUL, // Source fragment0 instruction 125.
            0x59a0280005275b50UL, // Source fragment0 instruction 126.
            0x5c68100005c75757UL, // Source fragment0 instruction 127.
            0x5c68100005c75853UL, // Source fragment0 instruction 128.
            0x5c68100005c75259UL, // Source fragment0 instruction 129.
            0x59a0288005775057UL, // Source fragment0 instruction 130.
            0x59a02b0005375053UL, // Source fragment0 instruction 131.
            0x59a22d8005975059UL, // Source fragment0 instruction 132.
            0x5c59100005775157UL, // Source fragment0 instruction 134.
            0x5c59100005375653UL, // Source fragment0 instruction 135.
            0x5c59100005975b59UL, // Source fragment0 instruction 136.
            0x49a0289403c75751UL, // Source fragment0 instruction 137.
            0x49a02b1403c75356UL, // Source fragment0 instruction 138.
            0x49a02d9403c7595bUL, // Source fragment0 instruction 139.
            0x5c98078005670060UL, // Stable XYZ output.
            0x5c98078005170061UL, // Stable XYZ output.
            0x5c98078005b70062UL, // Stable XYZ output.
        ];
        return words.Select(w => (w, SwShShaderScheduling.Serial(w))).ToArray();
    }
}
