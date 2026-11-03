// SPDX-License-Identifier: GPL-3.0-only
namespace KM.Formats.SwSh;

/// <summary>Compiled ZA gem diffuse and emission with explicit source samples.</summary>
internal static class SwShZaFresnelSurface
{
    // Diffuse input/output56..58; secondary RGB60..62, AO63, weight80.
    // Masked emission additionally requires MaskedEmissionMap red79 (TEX36).
    // No fallback image or sample is supplied.
    // Emission output68..70; scratch83..95/102..111. Allocate112 registers.
    // REQUIRED scalar55=saturation,57=EI0,58=EI1,59=layer mask scale.
    // REQUIRED vec3 words76..78=base tint,80..82=layer tint,84..86=emission.
    // These are owned binding slots; metadata/default loops/UVs/samplers,
    // material selection and native lighting/probe integration remain pending.
    internal static (ulong Instruction, ulong Control)[] Diffuse()
    {
        ulong[] words =
        [
            0x4c68101804c73853UL, // Source instruction 115.
            0x4c68101804d73954UL, // Source instruction 116.
            0x4c68101804e73a55UL, // Source instruction 118.
            0x1e23e99168775356UL, // Source instruction 189.
            0x0103f1645a27f058UL, // Explicit source constant/uniform/output.
            0x59a02b0005875457UL, // Source instruction 194.
            0x0103de978d57f058UL, // Explicit source constant/uniform/output.
            0x59a02b8005875556UL, // Source instruction 198.
            0x5c58300005675357UL, // Source instruction 203.
            0x5c58300005675453UL, // Source instruction 204.
            0x5c58300005675554UL, // Source instruction 205.
            0x49a02b1403775755UL, // Source instruction 209.
            0x49a02b1403775357UL, // Source instruction 210.
            0x49a02b1403775453UL, // Source instruction 211.
            0x5c98078005570038UL, // Explicit source constant/uniform/output.
            0x5c98078005770039UL, // Explicit source constant/uniform/output.
            0x5c9807800537003aUL, // Explicit source constant/uniform/output.
        ];
        return words.Select(w => (w, SwShShaderScheduling.Serial(w))).ToArray();
    }
    internal static (ulong Instruction, ulong Control)[] Emission0()
    {
        ulong[] words =
        [
            0x4c68101805173f53UL, // Source instruction 142.
            0x4c68101403a75354UL, // Source instruction 145.
            0x4c98079403970053UL, // Explicit source constant/uniform/output.
            0x4c68101805475355UL, // Source instruction 155.
            0x4c68101805273f56UL, // Source instruction 156.
            0x5c6017800557ff57UL, // Source instruction 160.
            0x4c68101403a75655UL, // Source instruction 161.
            0x4c68101805575356UL, // Source instruction 173.
            0x5c6017800567ff58UL, // Source instruction 175.
            0x4c68101805675356UL, // Source instruction 187.
            0x4c68101805073f53UL, // Source instruction 188.
            0x59a22c0003d75459UL, // Source instruction 190.
            0x5c6017800567ff54UL, // Source instruction 192.
            0x4c68101403a75356UL, // Source instruction 193.
            0x4c68101403b75953UL, // Source instruction 195.
            0x59a22a0003e75559UL, // Source instruction 196.
            0x59a22b8003c75655UL, // Source instruction 197.
            0x4c68101403b75956UL, // Source instruction 201.
            0x4c68101403b75559UL, // Source instruction 202.
            0x5c58100005375855UL, // Source instruction 206.
            0x5c58100005675458UL, // Source instruction 207.
            0x5c58100005975754UL, // Source instruction 208.
            0x59a12a8005375057UL, // Source instruction 212.
            0x59a12c0005675053UL, // Source instruction 213.
            0x59a12a0005975055UL, // Source instruction 214.
            0x5c98078005570044UL, // Explicit source constant/uniform/output.
            0x5c98078005770045UL, // Explicit source constant/uniform/output.
            0x5c98078005370046UL, // Explicit source constant/uniform/output.
        ];
        return words.Select(w => (w, SwShShaderScheduling.Serial(w))).ToArray();
    }
    internal static (ulong Instruction, ulong Control)[] Emission2()
    {
        ulong[] words =
        [
            0x4c68101805073f53UL, // Source instruction 186.
            0x4c68101805273f54UL, // Source instruction 189.
            0x4c68101403a75355UL, // Source instruction 191.
            0x4c98079403970053UL, // Explicit source constant/uniform/output.
            0x4c68101805475356UL, // Source instruction 198.
            0x5c6017800567ff57UL, // Source instruction 202.
            0x59a22b8005573c56UL, // Source instruction 206.
            0x4c68101805575355UL, // Source instruction 207.
            0x4c68101805173f58UL, // Source instruction 209.
            0x4c68101403b75659UL, // Source instruction 210.
            0x5c6017800557ff56UL, // Source instruction 211.
            0x4c68101403a75855UL, // Source instruction 213.
            0x5c58100005975758UL, // Source instruction 214.
            0x59a22b0005573d57UL, // Source instruction 217.
            0x4c68101805675355UL, // Source instruction 238.
            0x5c6017800557ff53UL, // Source instruction 240.
            0x4c68101403a75455UL, // Source instruction 241.
            0x4c68101403b75754UL, // Source instruction 242.
            0x59a2298005573e57UL, // Source instruction 246.
            0x59a12c0005975055UL, // Source instruction 247.
            0x5c58100005475658UL, // Source instruction 249.
            0x4c68101403b75756UL, // Source instruction 252.
            0x59a12c0005475057UL, // Source instruction 254.
            0x5c58100005675354UL, // Source instruction 256.
            0x59a12a0005675053UL, // Source instruction 260.
            0x5c98078005570044UL, // Explicit source constant/uniform/output.
            0x5c98078005770045UL, // Explicit source constant/uniform/output.
            0x5c98078005370046UL, // Explicit source constant/uniform/output.
        ];
        return words.Select(w => (w, SwShShaderScheduling.Serial(w))).ToArray();
    }
    internal static (ulong Instruction, ulong Control)[] Emission6()
    {
        ulong[] words =
        [
            0x4c68101805173f53UL, // Source instruction 160.
            0x4c68101403a75354UL, // Source instruction 166.
            0x4c68101805273f53UL, // Source instruction 175.
            0x4c68101403a75355UL, // Source instruction 177.
            0x4c98079403970053UL, // Explicit source constant/uniform/output.
            0x4c68101805475356UL, // Source instruction 189.
            0x4c68101805575357UL, // Source instruction 191.
            0x4c68101805675358UL, // Source instruction 192.
            0x5c68100004f75653UL, // Source instruction 193.
            0x4c68101805073f56UL, // Source instruction 195.
            0x5c68100004f75859UL, // Source instruction 196.
            0x5c68100004f75758UL, // Source instruction 197.
            0x5c6017800537ff57UL, // Source instruction 199.
            0x4c68101403a75653UL, // Source instruction 201.
            0x5c6017800597ff56UL, // Source instruction 203.
            0x5c6017800587ff59UL, // Source instruction 205.
            0x59a22b8003c75358UL, // Source instruction 206.
            0x59a22b0003e75553UL, // Source instruction 207.
            0x59a22c8003d75455UL, // Source instruction 209.
            0x4c68101403b75854UL, // Source instruction 211.
            0x4c68101403b75358UL, // Source instruction 213.
            0x4c68101403b75553UL, // Source instruction 215.
            0x5c58100005475755UL, // Source instruction 218.
            0x5c58100005875657UL, // Source instruction 219.
            0x5c58100005375956UL, // Source instruction 221.
            0x59a12a8005075459UL, // Source instruction 224.
            0x59a12b8005075854UL, // Source instruction 225.
            0x59a12b0005075355UL, // Source instruction 227.
            0x5c98078005970044UL, // Explicit source constant/uniform/output.
            0x5c98078005570045UL, // Explicit source constant/uniform/output.
            0x5c98078005470046UL, // Explicit source constant/uniform/output.
        ];
        return words.Select(w => (w, SwShShaderScheduling.Serial(w))).ToArray();
    }
    internal static (ulong Instruction, ulong Control)[] Emission8()
    {
        ulong[] words =
        [
            0x4c68101805073f53UL, // Source instruction 199.
            0x4c68101403a75354UL, // Source instruction 204.
            0x4c68101805273f53UL, // Source instruction 205.
            0x4c68101403a75355UL, // Source instruction 209.
            0x4c98079403970053UL, // Explicit source constant/uniform/output.
            0x4c68101805475356UL, // Source instruction 217.
            0x5c68100004f75657UL, // Source instruction 222.
            0x4c68101805575356UL, // Source instruction 223.
            0x4c68101805675358UL, // Source instruction 224.
            0x5c6017800577ff53UL, // Source instruction 227.
            0x5c68100004f75657UL, // Source instruction 228.
            0x5c68100004f75856UL, // Source instruction 229.
            0x59a2298005473c58UL, // Source instruction 233.
            0x5c6017800577ff54UL, // Source instruction 234.
            0x5c6017800567ff57UL, // Source instruction 236.
            0x4c68101403b75856UL, // Source instruction 237.
            0x59a22b8005573e58UL, // Source instruction 241.
            0x5c58100005675355UL, // Source instruction 243.
            0x4c68101805173f53UL, // Source instruction 252.
            0x4c68101403a75359UL, // Source instruction 254.
            0x59a22a0005973d53UL, // Source instruction 258.
            0x59a12a8005675059UL, // Source instruction 259.
            0x4c68101403b75855UL, // Source instruction 260.
            0x4c68101403b75356UL, // Source instruction 265.
            0x5c58100005575753UL, // Source instruction 267.
            0x5c58100005675457UL, // Source instruction 269.
            0x59a1298005575054UL, // Source instruction 270.
            0x59a12b8005675053UL, // Source instruction 274.
            0x5c98078005970044UL, // Explicit source constant/uniform/output.
            0x5c98078005370045UL, // Explicit source constant/uniform/output.
            0x5c98078005470046UL, // Explicit source constant/uniform/output.
        ];
        return words.Select(w => (w, SwShShaderScheduling.Serial(w))).ToArray();
    }
}
