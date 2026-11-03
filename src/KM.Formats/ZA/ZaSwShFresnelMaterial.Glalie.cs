// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;

namespace KM.Formats.ZA;

public static partial class ZaSwShFresnelMaterial
{
    // Exact original pm0362_51_00 normal/rare containers, three authored
    // opaque FresnelEffect surfaces; selected source forward program 6.
    // Other Glalie materials and edited containers are not qualified here.
    private static bool QualifiesGlalieEmissionMask(byte[] bytes, string materialName)
        => materialName is ("body_b_02" or "body_a_00" or "body_a_02")
            && Convert.ToHexString(SHA256.HashData(bytes)) is
                "1F8BAD8FA650AFEA95F5BC8955C0D39316C4195973C1C22490EAC53799358D54"
                or "14679C9EEBACA146550BAD1138CB30BB01F1197FD9015E072EE2719951C538E1";
}
