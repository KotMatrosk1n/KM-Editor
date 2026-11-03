// SPDX-License-Identifier: GPL-3.0-only
namespace KM.Formats.SwSh;

/// <summary>Original ZA material probe direction and roughness LOD.</summary>
internal static class SwShZaFresnelProbe
{
    // REQUIRED raw face-corrected world normal99..101, world position80..82,
    // camera C3[76..78] in matching units, source roughness C5[73],
    // log2 authored probe width87 (source TXQ/I2F/MUFU result). No defaults.
    // Outputs83..85 direction with original source Z convention,86 LOD.
    // Scratch88..95/102..111; allocate112. No texture fetch or scene lights.
    // Caller must retain the view dot before reloading world position80..82.
    internal static (ulong Instruction, ulong Control)[] Create()
    {
        ulong[] words =
        [
            0x4C59100C04C75058UL, // Source fragment0 instruction 20.
            0x4C59100C04D75159UL, // Source fragment0 instruction 21.
            0x5C6810000587585AUL, // Source fragment0 instruction 24.
            0x4C59100C04E7525BUL, // Source fragment0 instruction 25.
            0x59A02D000597595CUL, // Source fragment0 instruction 31.
            0x59A02E0005B75B59UL, // Source fragment0 instruction 35.
            0x508000000057595AUL, // Source fragment0 instruction 37.
            0x5C68100005A7585CUL, // Source fragment0 instruction 48.
            0x5C68100005A75B58UL, // Source fragment0 instruction 51.
            0x508000000057595AUL, // Source fragment0 instruction 602.
            0x4C59100C04D75159UL, // Source fragment0 instruction 603.
            0x5C69100006375C5BUL, // Source fragment0 instruction 604.
            0x5C68100005A7595DUL, // Source fragment0 instruction 605.
            0x59A12D8006475D59UL, // Source fragment0 instruction 606.
            0x59A12C800657585AUL, // Source fragment0 instruction 607.
            0x5C68100005A76359UL, // Source fragment0 instruction 608.
            0x5C68100005A7645BUL, // Source fragment0 instruction 609.
            0x5C68100005A7655EUL, // Source fragment0 instruction 610.
            0x33A22E400007595AUL, // Source fragment0 instruction 611.
            0x33A22EC000075B59UL, // Source fragment0 instruction 612.
            0x33A22C4000075E5BUL, // Source fragment0 instruction 613.
            0x5C62578005975A58UL, // Source fragment0 instruction 614.
            0x5C60578005875B5CUL, // Source fragment0 instruction 615.
            0x5080000000475C58UL, // Source fragment0 instruction 616.
            0x5C69100005875B5CUL, // Source fragment0 instruction 617.
            0x5C68100005875A5BUL, // Source fragment0 instruction 619.
            0x5C6810000587595AUL, // Source fragment0 instruction 620.
            0x4C68101404975758UL, // Source fragment0 instruction 628.
            0x5C98078005B70053UL, // Explicit source constant or stable output.
            0x5C98078005A70054UL, // Explicit source constant or stable output.
            0x5C98078005C70055UL, // Explicit source constant or stable output.
            0x5C98078005870056UL, // Explicit source constant or stable output.
        ];
        return words.Select(w => (w, SwShShaderScheduling.Serial(w))).ToArray();
    }
}
