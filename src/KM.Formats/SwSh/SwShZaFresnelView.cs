// SPDX-License-Identifier: GPL-3.0-only
namespace KM.Formats.SwSh;

/// <summary>Compiled ZA pixel-normal/view math with explicit interpolated inputs.</summary>
internal static class SwShZaFresnelView
{
    // Inputs: N64..66, T67..69, handedness70, raw signed face flag71,
    // world position80..82, source tangent normal96..98; camera C3[76..78].
    // All interpolated values and camera coordinates must share their native
    // frame and units. Sampling, skinning and interpolation are not supplied.
    // Outputs: raw face-corrected world normal99..101 and view dot80.
    // Scratch83..95/102..103; preserve other input registers. Allocate112.
    internal static (ulong Instruction, ulong Control)[] Create()
    {
        ulong[] words =
        [
            0x5c68100004374353UL, // Source fragment0 instruction 18.
            0x5c68100004074054UL, // Source fragment0 instruction 19.
            0x4c59100c04c75055UL, // Source fragment0 instruction 20.
            0x4c59100c04d75156UL, // Source fragment0 instruction 21.
            0x59a0298004474457UL, // Source fragment0 instruction 22.
            0x59a02a0004174153UL, // Source fragment0 instruction 23.
            0x5c68100005575554UL, // Source fragment0 instruction 24.
            0x4c59100c04e75258UL, // Source fragment0 instruction 25.
            0x59a02b8004574559UL, // Source fragment0 instruction 28.
            0x59a0298004274257UL, // Source fragment0 instruction 29.
            0x5080000000575953UL, // Source fragment0 instruction 30.
            0x59a02a0005675659UL, // Source fragment0 instruction 31.
            0x5080000000575754UL, // Source fragment0 instruction 32.
            0x59a02c8005875857UL, // Source fragment0 instruction 35.
            0x5080000000575759UL, // Source fragment0 instruction 37.
            0x5c68100005374557UL, // Source fragment0 instruction 39.
            0x5c6810000537435aUL, // Source fragment0 instruction 40.
            0x5c6810000547405bUL, // Source fragment0 instruction 41.
            0x5c6810000537445cUL, // Source fragment0 instruction 42.
            0x5c68100005474253UL, // Source fragment0 instruction 43.
            0x5c6810000547415dUL, // Source fragment0 instruction 44.
            0x5c68100005b75754UL, // Source fragment0 instruction 45.
            0x5c6810000597565eUL, // Source fragment0 instruction 46.
            0x5c68100005375c56UL, // Source fragment0 instruction 47.
            0x5c6810000597555fUL, // Source fragment0 instruction 48.
            0x5c68100005d75a55UL, // Source fragment0 instruction 49.
            0x59a22a0005375a66UL, // Source fragment0 instruction 50.
            0x5c68100005975854UL, // Source fragment0 instruction 51.
            0x59a22b0005d75758UL, // Source fragment0 instruction 53.
            0x59a22a8005b75c56UL, // Source fragment0 instruction 54.
            0x5c68100004676655UL, // Source fragment0 instruction 55.
            0x5c68100004675859UL, // Source fragment0 instruction 58.
            0x5c68100004675658UL, // Source fragment0 instruction 59.
            0x5cb8000004772a56UL, // Source fragment0 instruction 133.
            0x0103f8000007f066UL, // Explicit constant or stable output.
            0x33a2334000075667UL, // Source fragment0 instruction 140.
            0x5c68100006175956UL, // Source fragment0 instruction 143.
            0x5c68100006175559UL, // Source fragment0 instruction 144.
            0x59a02b0006075a55UL, // Source fragment0 instruction 146.
            0x5c68100006175856UL, // Source fragment0 instruction 147.
            0x59a02c8006075c58UL, // Source fragment0 instruction 149.
            0x59a02a8006275b59UL, // Source fragment0 instruction 151.
            0x5b5913800ff76755UL, // Source fragment0 instruction 152.
            0x59a02b000607575aUL, // Source fragment0 instruction 153.
            0x59a02c0006275d56UL, // Source fragment0 instruction 154.
            0x5c68100005575957UL, // Source fragment0 instruction 157.
            0x59a02d0006275358UL, // Source fragment0 instruction 158.
            0x5c68100005575653UL, // Source fragment0 instruction 159.
            0x32a22cc00007575aUL, // Source fragment0 instruction 162.
            0x5c68100005575857UL, // Source fragment0 instruction 163.
            0x32a22b4000075355UL, // Source fragment0 instruction 164.
            0x5c68100005a75f53UL, // Source fragment0 instruction 168.
            0x32a22c4000075756UL, // Source fragment0 instruction 169.
            0x59a0298005575e57UL, // Source fragment0 instruction 172.
            0x59a02b8005675453UL, // Source fragment0 instruction 174.
            0x5c98078005a70063UL, // Explicit constant or stable output.
            0x5c98078005570064UL, // Explicit constant or stable output.
            0x5c98078005670065UL, // Explicit constant or stable output.
            0x5c98078005370050UL, // Explicit constant or stable output.
        ];
        return words.Select(w => (w, SwShShaderScheduling.Serial(w))).ToArray();
    }
}
