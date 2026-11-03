// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;

namespace KM.Formats.ZA;

public static partial class ZaSwShFresnelMaterial
{
    // Exact original pm0478_51_00 normal/rare containers, sole FresnelEffect
    // body_a_01. The distinct body_b_01 FresnelBlend surface is not qualified here.
    private static bool QualifiesFroslassEmissionMask(byte[] bytes, string materialName)
        => materialName == "body_a_01" && Convert.ToHexString(SHA256.HashData(bytes)) is
            "B104CEA6F15DF28020C3865343B72053DC7E730A43DF752553FB1E4CFC49AF67"
            or "AEE073348CA358E97791712932CB9F47FDAFB631E5D172559BFE004BEB6077A6";
}
