// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;

namespace KM.Formats.ZA;

public static partial class ZaSwShFresnelMaterial
{
    // Catalog species740/form1 joins pm0860_51_00. Only these two authored
    // material containers and their two Fresnel consumers qualify the masked
    // source family. Other source switches retain their existing guards.
    private static bool QualifiesCrabominableEmissionMask(byte[] bytes, string materialName)
        => materialName is "body_a_01" or "body_b_01"
            && Convert.ToHexString(SHA256.HashData(bytes)) is
                "2FDD263FD37F6D4AC2204BCF2D4EEBBD1B44F600A7D66DABE1A193C5D5701049"
                or "6C93EC8560BC21E126891628706EE644B92D7284584765A0EC743F8484308A72";
}
