// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;

namespace KM.SwSh.Tests.FpsPatch;

internal static class SwShFpsMainTestAnchors
{
    public const int RequiredTextLength = 0x018A4000;

    public static void WriteVanilla(byte[] text, ProjectGame game)
    {
        var nvnOffset = game == ProjectGame.Shield ? 0x018A2D18 : 0x018A2C88;
        var schedulerAdrpOffset = game == ProjectGame.Shield ? 0x013167AC : 0x0131677C;
        var schedulerLdrOffset = game == ProjectGame.Shield ? 0x013167B0 : 0x01316780;

        WriteBytes(text, nvnOffset, "E103152A");
        WriteBytes(text, 0x000061F0, "E2030032");
        WriteBytes(text, 0x0000620C, "E2030032");
        WriteBytes(text, 0x005DE834, "C90A9452");
        WriteBytes(text, 0x005DE838, "893FA072");
        WriteBytes(text, schedulerAdrpOffset, "A94900B0");
        WriteBytes(text, schedulerLdrOffset, "20C94FBD");
        WriteBytes(text, 0x009D17B0, "08F044B9");
        WriteBytes(text, 0x009D17B4, "1FE90D71");
        WriteBytes(text, 0x009D17B8, "21010054");
        WriteBytes(text, 0x009D17BC, "080445B9");
        WriteBytes(text, 0x009D05C8, "E81B0932");
        WriteBytes(text, 0x009D0834, "00102C1E");
        WriteBytes(text, 0x009D0838, "01102E1E");
        WriteBytes(text, 0x009D0848, "00102C1E");
    }

    private static void WriteBytes(byte[] data, int offset, string hex)
    {
        Convert.FromHexString(hex).CopyTo(data.AsSpan(offset));
    }
}
