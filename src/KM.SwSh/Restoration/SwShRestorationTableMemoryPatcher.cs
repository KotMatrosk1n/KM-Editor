// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Security.Cryptography;
using KM.Core.Projects;
using KM.Formats.Executable;
using KM.SwSh.ExeFs;

namespace KM.SwSh.Restoration;

/// <summary>Reserves temporary file and permanent parsed Pokemon table memory together.</summary>
public static class SwShRestorationTableMemoryPatcher
{
    internal const string Owner = "Restoration Table Memory";
    internal const string MainPath = "exefs/main";
    // The child is carved out of the parent. A 2 MiB parent increase gives
    // temporary file loading and permanent parsed tables another 1 MiB each.
    private static readonly (int Offset, uint Original, uint Expanded)[] Words =
    [
        (0x7D1658, 0x72A002C1, 0x72A006C1), // parent: 0x164000 -> 0x364000
        (0x7D1698, 0x72A001E8, 0x72A005E8), // matching usable parent size
        (0x7D16B0, 0x72A000D4, 0x72A002D4), // child: 0x64000 -> 0x164000
    ];

    internal static (NsoFile Main, bool Installed) Read(byte[] bytes, ProjectGame game)
    {
        var main = NsoFile.Parse(bytes);
        if (game is not (ProjectGame.Sword or ProjectGame.Shield)
            || SwShExecutableMergeSupport.GameForBuild(Convert.ToHexString(main.BuildId)) != game
            || main.Text.Header.MemoryOffset != 0 || main.Text.DecompressedData.Length < 0x7D1730)
            throw new InvalidDataException("Restored Pokemon tables require the selected game's supported 1.3.2 executable.");
        foreach (var (segment, flag) in new[] { (main.Text, NsoFlags.CheckHashText), (main.Ro, NsoFlags.CheckHashRo), (main.Data, NsoFlags.CheckHashData) })
            if (main.Flags.HasFlag(flag) && !NsoFile.ComputeHash(segment.DecompressedData).SequenceEqual(segment.Hash))
                throw new InvalidDataException("The table memory executable has an invalid segment checksum.");
        var text = main.Text.DecompressedData;
        var original = Words.All(word => BinaryPrimitives.ReadUInt32LittleEndian(text.AsSpan(word.Offset, 4)) == word.Original);
        var installed = Words.All(word => BinaryPrimitives.ReadUInt32LittleEndian(text.AsSpan(word.Offset, 4)) == word.Expanded);
        if (!original && !installed)
            throw new InvalidDataException("The Pokemon table memory budgets contain incompatible changes.");
        var constructor = text.AsSpan(0x7D1620, 0x110).ToArray();
        foreach (var word in Words)
            BinaryPrimitives.WriteUInt32LittleEndian(constructor.AsSpan(word.Offset - 0x7D1620), word.Original);
        if (Convert.ToHexString(SHA256.HashData(constructor)) != "D7476A03A9B9FDC275608AD24D242D24972DA91B2FC0CDC7C04C0CEF51DDA022")
            throw new InvalidDataException("The Pokemon table heap initialization contains incompatible changes.");
        return (main, installed);
    }

    public static byte[] Apply(byte[] baseline, byte[] source, ProjectGame game) => Rewrite(baseline, source, game, true);
    internal static byte[] RestoreFromBase(byte[] baseline, byte[] source, ProjectGame game) => Rewrite(baseline, source, game, false);

    private static byte[] Rewrite(byte[] baseline, byte[] source, ProjectGame game, bool expanded)
    {
        var original = Read(baseline, game); var current = Read(source, game);
        if (original.Installed) throw new InvalidDataException("Base ExeFS must contain the original Pokemon table memory budgets.");
        SwShExeFsMainComparison.EnsureCompatibleBaseLayout(original.Main, current.Main, Owner);
        if (Words.Any(word => SwShExeFsReservedRegionLedger.MainTextReservationsForOtherOwners(game, Owner)
            .Any(other => SwShExeFsReservedRegionLedger.Overlaps(other, word.Offset, 4))))
            throw new InvalidDataException("Pokemon table memory support overlaps another executable feature.");
        if (current.Installed == expanded) return source;
        var text = current.Main.Text.DecompressedData.ToArray();
        foreach (var word in Words)
            BinaryPrimitives.WriteUInt32LittleEndian(text.AsSpan(word.Offset), expanded ? word.Expanded : word.Original);
        var output = current.Main.Write(textDecompressedData: text); var after = Read(output, game);
        if (after.Installed != expanded || !after.Main.Text.DecompressedData.SequenceEqual(text)
            || !after.Main.Ro.DecompressedData.SequenceEqual(current.Main.Ro.DecompressedData)
            || !after.Main.Data.DecompressedData.SequenceEqual(current.Main.Data.DecompressedData)
            || !SwShExeFsMainComparison.StableHeaderBytesMatch(current.Main.RawHeader, after.Main.RawHeader))
            throw new InvalidDataException("Pokemon table memory output failed preservation verification.");
        return output;
    }

    public static bool HasInstalledPatch(byte[] bytes)
    {
        try
        {
            var game = SwShExecutableMergeSupport.GameForBuild(Convert.ToHexString(NsoFile.Parse(bytes).BuildId));
            return game is not null && Read(bytes, game.Value).Installed;
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or OverflowException) { return false; }
    }

    internal static IReadOnlyList<SwShExeFsReservedRegion> Reservations() =>
    [
        new(Owner, "restoration-table-parent", MainPath, "main.text", 0x7D1658, 4, "Pokemon table parent heap size", "payload-only"),
        new(Owner, "restoration-table-parent-usable", MainPath, "main.text", 0x7D1698, 4, "Pokemon table parent usable size", "payload-only"),
        new(Owner, "restoration-table-temporary", MainPath, "main.text", 0x7D16B0, 4, "Pokemon table temporary file heap size", "payload-only"),
    ];
}
