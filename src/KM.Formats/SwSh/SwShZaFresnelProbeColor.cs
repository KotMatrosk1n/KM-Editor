// SPDX-License-Identifier: GPL-3.0-only
namespace KM.Formats.SwSh;

/// <summary>Original ZA authored local probe reflectance, without scene lighting.</summary>
internal static class SwShZaFresnelProbeColor
{
    // REQUIRED source diffuse56..58, filtered HDR probe64..66, raw worldN99..101,
    // world position80..82 and camera C3[76..78] in matching units.
    // REQUIRED owned scalars56=probe intensity,72=metallic,73=roughness.
    // Output64..66; scratch83..98/102..111. Allocate112 registers.
    // Source RRO/EX2 and all arithmetic flags retained; no donor scene globals.
    // Emission68..70, diffuse56..58 and worldN99..101 remain intact.
    internal static (ulong Instruction, ulong Control)[] Create()
    {
        ulong[] words =
        [
            0x4C59100C04C75053UL, // Source fragment0 instruction 20.
            0x4C59100C04D75154UL, // Source fragment0 instruction 21.
            0x5C68100005375355UL, // Source fragment0 instruction 24.
            0x4C59100C04E75256UL, // Source fragment0 instruction 25.
            0x59A02A8005475457UL, // Source fragment0 instruction 31.
            0x59A02B8005675655UL, // Source fragment0 instruction 35.
            0x5080000000575557UL, // Source fragment0 instruction 37.
            0x5C68100005775455UL, // Source fragment0 instruction 46.
            0x5C68100005775354UL, // Source fragment0 instruction 48.
            0x5C68100005775653UL, // Source fragment0 instruction 51.
            0x5C68100006375456UL, // Source fragment0 instruction 168.
            0x59A02B0006475554UL, // Source fragment0 instruction 172.
            0x59A02A0006575355UL, // Source fragment0 instruction 174.
            0x386013BF80075553UL, // Source fragment0 instruction 177.
            0x0103727C5AC7F055UL, // Explicit source constant or stable coefficient.
            0x5C60178005575354UL, // Source fragment0 instruction 179.
            0x088BD23D70A73953UL, // Source fragment0 instruction 634.
            0x088BD23D70A73A55UL, // Source fragment0 instruction 635.
            0x1E2C1147AE175456UL, // Source fragment0 instruction 652.
            0x088BD23D70A73854UL, // Source fragment0 instruction 655.
            0x5C90008005670057UL, // Source fragment0 instruction 656.
            0x5080000000275756UL, // Source fragment0 instruction 657.
            0x0103D23D70A7F057UL, // Explicit source constant or stable coefficient.
            0x49A02B9404875558UL, // Source fragment0 instruction 666.
            0x49A02B9404875455UL, // Source fragment0 instruction 672.
            0x4C98079404970054UL, // Explicit source constant or stable coefficient.
            0x3859103F80075459UL, // Source fragment0 instruction 673.
            0x49A02B940487535AUL, // Source fragment0 instruction 676.
            0x5C68100005975953UL, // Source fragment0 instruction 678.
            0x5C6013800567535BUL, // Source fragment0 instruction 685.
            0x0103D2E147B7F053UL, // Explicit source constant or stable coefficient.
            0x010BCE147AE7F05CUL, // Explicit source constant or stable coefficient.
            0x59A0298005C75456UL, // Source fragment0 instruction 695.
            0x59A02B0005B75953UL, // Source fragment0 instruction 699.
            0x0103F851EB87F056UL, // Explicit source constant or stable coefficient.
            0x010BF126E987F05BUL, // Explicit source constant or stable coefficient.
            0x59A02B0005B75459UL, // Source fragment0 instruction 700.
            0x0103CB439587F05BUL, // Explicit source constant or stable coefficient.
            0x59A22B8005B75456UL, // Source fragment0 instruction 701.
            0x010BF851EB87F057UL, // Explicit source constant or stable coefficient.
            0x59A02C8005775354UL, // Source fragment0 instruction 705.
            0x0103F851EB87F059UL, // Explicit source constant or stable coefficient.
            0x59A02B0005975357UL, // Source fragment0 instruction 706.
            0x59A02B8005475553UL, // Source fragment0 instruction 712.
            0x59A02B8005475A55UL, // Source fragment0 instruction 713.
            0x59A02B8005475856UL, // Source fragment0 instruction 714.
            0x5C98078005370060UL, // Explicit source constant or stable coefficient.
            0x5C98078005570061UL, // Explicit source constant or stable coefficient.
            0x5C98078005670062UL, // Explicit source constant or stable coefficient.
            0x5C68100004076040UL, // Source fragment0 instruction 722.
            0x4C68101403874040UL, // Source fragment0 instruction 728.
            0x5C68100004176141UL, // Source fragment0 instruction 723.
            0x4C68101403874141UL, // Source fragment0 instruction 729.
            0x5C68100004276242UL, // Source fragment0 instruction 724.
            0x4C68101403874242UL, // Source fragment0 instruction 730.
        ];
        return words.Select(w => (w, SwShShaderScheduling.Serial(w))).ToArray();
    }
}
