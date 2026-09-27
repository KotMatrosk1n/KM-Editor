// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Projects;
using KM.Formats.Executable;
using KM.SwSh.ExeFs;
using System.Security.Cryptography;

namespace KM.SwSh.HeldItemChance;

internal static class SwShHeldItemChancePatcher
{
    public const int RateOffset = 0x0075FB5C;
    public static readonly int[] Defaults = [50, 5, 0, 60, 20, 0];

    public static bool AreValid(IReadOnlyList<int>? rates) => rates is { Count: 6 }
        && rates.All(rate => rate is >= 0 and <= 100)
        && rates.Take(3).Sum() <= 100 && rates.Skip(3).Sum() <= 100;

    public static NsoFile Read(byte[] bytes, ProjectGame? game)
    {
        var nso = NsoFile.Parse(bytes);
        var (build, picker, hash) = game switch
        {
            ProjectGame.Sword => ("A3B75BCD3311385AEED67FBEEB79CBB7BF02F471000000000000000000000000", 0x00D317F0,
                "2104389400D458326034755E2C25866CABBC459885987565FA84C89B87930DC1"),
            ProjectGame.Shield => ("A16802625E7826BF83B6F9708E475B912A9AB7DF000000000000000000000000", 0x00D31820,
                "F0EEA5416428022F5E5F9AD8FFC604A04186078238CBC51E3BFFF4011BCE330A"),
            _ => throw new InvalidDataException("Select Sword or Shield.")
        };
        if (Convert.ToHexString(nso.BuildId) != build || nso.Text.Header.MemoryOffset != 0
            || nso.Ro.Header.MemoryOffset != 0x01901000 || nso.Ro.DecompressedData.Length < RateOffset + 6
            || nso.Text.DecompressedData.Length < picker + 0x114
            || Convert.ToHexString(SHA256.HashData(nso.Text.DecompressedData.AsSpan(picker, 0x114))) != hash)
            throw new InvalidDataException("Held Item Chance requires the selected game's 1.3.2 executable and an unchanged held item picker.");
        foreach (var (segment, flag) in new[] { (nso.Text, NsoFlags.CheckHashText), (nso.Ro, NsoFlags.CheckHashRo), (nso.Data, NsoFlags.CheckHashData) })
            if (nso.Flags.HasFlag(flag) && !NsoFile.ComputeHash(segment.DecompressedData).SequenceEqual(segment.Hash))
                throw new InvalidDataException("The executable segment checksum is invalid.");
        if (!AreValid(Rates(nso))) throw new InvalidDataException("The executable held item percentages are invalid.");
        return nso;
    }

    public static int[] Rates(NsoFile nso) => nso.Ro.DecompressedData.AsSpan(RateOffset, 6).ToArray().Select(value => (int)value).ToArray();

    public static byte[] Apply(byte[] vanilla, byte[] source, ProjectGame? game, IReadOnlyList<int> rates)
    {
        if (!AreValid(rates)) throw new InvalidDataException("Each set requires three integer percentages totaling at most 100%.");
        var baseline = Read(vanilla, game);
        var current = Read(source, game);
        if (!Rates(baseline).SequenceEqual(Defaults)) throw new InvalidDataException("The base executable must contain the original held item chances.");
        SwShExeFsMainComparison.EnsureCompatibleBaseLayout(baseline, current, "Held Item Chance");
        var ro = current.Ro.DecompressedData.ToArray();
        for (var i = 0; i < 6; i++) ro[RateOffset + i] = checked((byte)rates[i]);
        var output = current.Write(roDecompressedData: ro);
        var after = Read(output, game);
        if (!after.Ro.DecompressedData.SequenceEqual(ro)
            || !after.Text.DecompressedData.SequenceEqual(current.Text.DecompressedData)
            || !after.Data.DecompressedData.SequenceEqual(current.Data.DecompressedData)
            || !after.Text.CompressedData.SequenceEqual(current.Text.CompressedData)
            || !after.Data.CompressedData.SequenceEqual(current.Data.CompressedData)
            || !SwShExeFsMainComparison.StableHeaderBytesMatch(current.RawHeader, after.RawHeader))
            throw new InvalidDataException("Held Item Chance output did not preserve the other executable data.");
        return output;
    }
}
