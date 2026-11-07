// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;

namespace KM.Formats.ZA;

public static partial class ZaSwShFresnelMaterial
{
    // The original Mega Abomasnow palettes use UV1 for the base/ambient
    // inputs and UV0 for the emission mask in source forward program 6.
    private static bool QualifiesAbomasnowEmissionMask(byte[] bytes, string materialName)
        => materialName == "body_b_01" && Convert.ToHexString(SHA256.HashData(bytes)) is
            "4B41E72A780E3F7CD239B0FA97A4A00437C2F507B7BB9756DE1754B74014D0AE"
            or "EA7F98B6338FA9DDFC8C4886922CC3CDCCE078362327122011C29E43D4352A9C";
}
