// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;

namespace KM.Formats.ZA;

public static partial class ZaSwShFresnelMaterial
{
    // Exact ZA catalog models for the Baxcalibur family, including Mega
    // Baxcalibur. Both palettes use masked forward program 6 with UV0.
    private static bool QualifiesBaxcaliburFamilyEmissionMask(byte[] bytes, string materialName)
        => (Convert.ToHexString(SHA256.HashData(bytes)), materialName) switch
        {
            ("06476C0F63D7113285474EC47AF4AE816741E096F3EF9F31B0EA3A8A5BADAAFE"
                or "42E29B5942EDC6228452C711C3606962F0D4D31CDB3D0BD71A54369E072C3642", "body_02") => true,
            ("01090C2F57061A8ED26150BBF38F5FEC1D11D3B86C8178FA1743867764E88012"
                or "66A8E1C44888F1E959CA9F82FF9BC6FE57698F987117F6CE99169DE63773D2B7", "body_a_01" or "body_a_02") => true,
            ("362AEF6A8EDF4A0DB4C5A5051FF611855CE2546743B9B3A1B4A144AF39BD6B59"
                or "8BC8F085DA8617DB30A18D0255DA898EDCAA6EC69D60F35AE5D7D49CD14A1E34", "body_b_01") => true,
            ("D6C717BF07A67F39C05A7B39DE5D3F9853E0F180EFBEE36297D31A22BC57F463"
                or "02A133424B986BF51601F4F079E710D396E44CA806EEB9D0A2FDF07536E534EA", "body_b_00" or "body_b_01" or "body_b_02") => true,
            _ => false,
        };
}
