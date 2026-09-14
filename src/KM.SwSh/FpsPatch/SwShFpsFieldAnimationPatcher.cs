// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;

namespace KM.SwSh.FpsPatch;

internal static class SwShFpsFieldAnimationPatcher
{
    public const string RelativePath = "romfs/bin/field/param/symbol_encount/ai.blua";
    private const string SourceHash = "53656257257569D72BB59A1ECD9108AC7D17148D9DBB9875B3C3E93421553520";

    // Only the supported timer operands, continuous movement and branch offsets change.
    private static readonly (int Offset, string Before, string After)[] Edits =
    [
        (0x953E, "4D80C000", "4DC0C800"),
        (0x9712, "23000000", "24000000"),
        (0x97F8, "", "03000000000000E03F"),
        (0x1026F, "4E00C200", "4EC0C500"),
        (0x1027F, "4D00C200", "4D00C600"),
        (0x1031B, "17000000", "19000000"),
        (0x103FA, "", "03000000000000E03F03000000000000E03F"),
        (0x190E1, "0D444808", "0DC44B08"),
        (0x1912D, "0D444808", "0D044C08"),
        (0x19145, "2F000000", "31000000"),
        (0x192DF, "", "03000000000000E03F03000000000000E03F"),
        (0x19650, "CD43C807", "CD03CC07"),
        (0x19734, "30000000", "31000000"),
        (0x198BB, "", "03000000000000E03F"),
        (0x1CDBF, "4DC4D508", "4D44E908"),
        (0x1D753, "A5000000", "A6000000"),
        (0x1DDCB, "", "03000000000000E03F"),
        (0x1FC0D, "8D404901", "8D805001"),
        (0x1FECD, "42000000", "43000000"),
        (0x20149, "", "03000000000000E03F"),
        (0x20688, "CE40C001", "CE80CB01"),
        (0x207D8, "2E000000", "2F000000"),
        (0x20968, "", "03000000000000E03F"),
        (0x214AA, "BB000000", "BC000000"),
        (0x2150A, "4D80C200", "4D00CD00"),
        (0x215F2, "1E801980", "1EC01980"),
        (0x216FA, "92C34A07", "92C34D07"),
        (0x2172E, "", "0F044E08"),
        (0x2177A, "0AC0CC98", "0A80CD98"),
        (0x21786, "8E834207", "8E434D07"),
        (0x2179A, "34000000", "39000000"),
        (0x2191C, "", "03000000000000E03F03000000000000E03F030000000000001A4003CD3B7F669EA0F63F03000000000000E03F"),
        (0x22A86, "4D40C000", "4D80C200"),
        (0x22ACA, "0A000000", "0B000000"),
        (0x22B20, "", "03000000000000E03F"),
        (0x22CEF, "4E80C300", "4E00C600"),
        (0x22CFB, "4E80C300", "4E40C600"),
        (0x22D7F, "0AC0458A", "0AC0468A"),
        (0x22D8B, "4E80C300", "4E80C600"),
        (0x22D97, "18000000", "1C000000"),
        (0x22E82, "", "03000000000000E03F03000000000000E03F03000000000000E03F030000000000002940"),
        (0x23657, "0D014302", "0D814602"),
        (0x23717, "1A000000", "1B000000"),
        (0x237C0, "", "03000000000000E03F"),
        (0x25226, "8E404001", "8EC04A01"),
        (0x253DA, "2B000000", "2C000000"),
        (0x25555, "", "03000000000000E03F"),
        (0x27A3E, "4DC0C000", "4D40C800"),
        (0x27AC6, "0DC14002", "0D814802"),
        (0x27BA2, "21000000", "23000000"),
        (0x27C9B, "", "03000000000000E03F03000000000000E03F"),
        (0x29276, "0D414002", "0DC14902"),
        (0x29342, "27000000", "28000000"),
        (0x2944A, "", "03000000000000E03F"),
        (0x2A147, "34000000", "35000000"),
        (0x2A153, "1E800780", "1EC00780"),
        (0x2A18B, "4D40C200", "4D80C500"),
        (0x2A1A7, "", "8FC04501"),
        (0x2A21B, "16000000", "18000000"),
        (0x2A2DE, "", "03000000000000E03F03000000000000E03F"),
        (0x2B326, "CD03C907", "CD03CB07"),
        (0x2B3DA, "2C000000", "2D000000"),
        (0x2B532, "", "03000000000000E03F"),
        (0x2DECC, "4D40C300", "4D80D100"),
        (0x2DF4C, "0D414302", "0DC15102"),
        (0x2E034, "0E414302", "0E015202"),
        (0x2E294, "CD41C303", "CD41D203"),
        (0x2E2C8, "46000000", "4A000000"),
        (0x2E517, "", "03000000000000E03F03000000000000E03F03000000000000E03F03000000000000E03F"),
        (0x3042A, "4D40C000", "4D80CB00"),
        (0x307CA, "2E000000", "2F000000"),
        (0x30951, "", "03000000000000E03F"),
        (0x312D6, "29000000", "2A000000"),
        (0x31366, "", "8F814503"),
        (0x3137E, "16000000", "17000000"),
        (0x3142C, "", "03000000000000E03F"),
        (0x31480, "CD80C101", "CD00C701"),
        (0x31520, "1C000000", "1D000000"),
        (0x315D9, "", "03000000000000E03F"),
    ];

    public static byte[] ConvertScript(byte[] source)
    {
        if (System.Convert.ToHexString(SHA256.HashData(source)) != SourceHash)
            throw new InvalidDataException("60FPS field animation timing requires the supported original script.");
        using var output = new MemoryStream();
        var position = 0;
        foreach (var edit in Edits)
        {
            var before = System.Convert.FromHexString(edit.Before);
            if (edit.Offset < position || !source.AsSpan(edit.Offset, before.Length).SequenceEqual(before))
                throw new InvalidDataException("60FPS field animation timing found an incompatible instruction.");
            output.Write(source.AsSpan(position, edit.Offset - position));
            output.Write(System.Convert.FromHexString(edit.After));
            position = edit.Offset + before.Length;
        }
        output.Write(source.AsSpan(position));
        return output.ToArray();
    }
}
