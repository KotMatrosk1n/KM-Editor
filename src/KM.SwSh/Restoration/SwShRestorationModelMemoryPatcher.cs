// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Security.Cryptography;
using KM.Core.Projects;
using KM.Formats.Executable;
using KM.SwSh.ExeFs;

namespace KM.SwSh.Restoration;

/// <summary>Reserves model heaps and their enclosing budgets without increasing startup memory.</summary>
public static partial class SwShRestorationModelMemoryPatcher
{
    internal const string Owner = "Restoration Model Memory";
    internal const string MainPath = "exefs/main";
    internal const int SceneBudgetOffset = 0x30;
    internal const uint OriginalSceneBudget = 713 * 1024 * 1024;
    internal const uint ExpandedSceneBudget = 771 * 1024 * 1024 + 512 * 1024;
    private sealed record Budget(string Area, int SwordOffset, int ShieldOffset, uint Original, uint Expanded,
        string SwordContext, string ShieldContext, string Label, bool PokedexParent = false)
    {
        public int Offset(ProjectGame game) => game == ProjectGame.Sword ? SwordOffset : ShieldOffset;
        public string Context(ProjectGame game) => game == ProjectGame.Sword ? SwordContext : ShieldContext;
    }

    internal static (NsoFile Main, bool Installed, bool NeedsPokedexRepair) Read(byte[] bytes, ProjectGame game)
    {
        var main = NsoFile.Parse(bytes);
        if (game is not (ProjectGame.Sword or ProjectGame.Shield)
            || SwShExecutableMergeSupport.GameForBuild(Convert.ToHexString(main.BuildId)) != game
            || main.Text.Header.MemoryOffset != 0
            || main.Ro.Header.MemoryOffset != 0x1901000 || main.Data.Header.MemoryOffset != 0x24DB000)
            throw new InvalidDataException("Restored models require the selected game's supported 1.3.2 executable.");
        foreach (var (segment, flag) in new[] { (main.Text, NsoFlags.CheckHashText), (main.Ro, NsoFlags.CheckHashRo), (main.Data, NsoFlags.CheckHashData) })
            if (main.Flags.HasFlag(flag) && !NsoFile.ComputeHash(segment.DecompressedData).SequenceEqual(segment.Hash))
                throw new InvalidDataException("The model memory executable has an invalid segment checksum.");
        foreach (var word in Words)
            if (Segment(main, word.Area).Length < word.Offset(game) + 36)
                throw new InvalidDataException("The model memory executable has a truncated initialization.");
        bool Matches(Budget word, uint expected) => Word(Segment(main, word.Area), word.Offset(game)) == expected;
        var original = Words.All(word => Matches(word, word.Original));
        // Existing installations expanded the shared preview child but left both Pokédex parents at retail sizes.
        var installed = Words.All(word => Matches(word, word.Expanded));
        var needsPokedexRepair = !original && !installed
            && Words.All(word => Matches(word, word.PokedexParent ? word.Original : word.Expanded));
        if (!original && !installed && !needsPokedexRepair)
            throw new InvalidDataException("The model memory budgets contain incompatible or incomplete changes.");

        // Hash only small constructor windows. Normalize all owned neighbors so
        // adjacent budget instructions remain one atomic, reversible change.
        foreach (var word in Words)
        {
            var offset = word.Offset(game); var start = Math.Max(0, offset - 32);
            var context = Segment(main, word.Area).AsSpan(start, offset + 36 - start).ToArray();
            foreach (var neighbor in Words.Where(other => other.Area == word.Area
                && other.Offset(game) >= start && other.Offset(game) + 4 <= offset + 36))
                Put(context, neighbor.Offset(game) - start, neighbor.Original);
            if (word.Area == "main.data" && start <= 0x28 && offset + 36 >= 0x2C)
            {
                // Sound memory owns a separate adjacent parent. Validate its
                // complete known tuple locally to avoid reciprocal patch reads.
                ValidateSoundTuple(main);
                Put(context, 0x28 - start, 0x5454CCCC);
            }
            if (Convert.ToHexString(SHA256.HashData(context)) != word.Context(game))
                throw new InvalidDataException("The model heap initialization contains incompatible changes.");
        }
        return (main, installed || needsPokedexRepair, needsPokedexRepair);
    }

    private static void ValidateSoundTuple(NsoFile main)
    {
        var values = (Word(main.Text.DecompressedData, 0xDA8), Word(main.Data.DecompressedData, 0x28),
            Word(main.Data.DecompressedData, 0x58));
        if (values is not ((0x52B85209, 0x5454CCCC, 65 * 1024 * 1024)
            or (0x52B85209, 0x5454CCCC, 81 * 1024 * 1024)
            or (0x52B85209, 0x5454CCCC, 256 * 1024 * 1024)
            or (0x52B95209, 0x6044CCCC, 256 * 1024 * 1024)
            or (0x52B99209, 0x6844CCCC, 384 * 1024 * 1024)))
            throw new InvalidDataException("The neighboring sound memory budgets contain incompatible changes.");
    }

    public static byte[] Apply(byte[] baseline, byte[] source, ProjectGame game) => Rewrite(baseline, source, game, true);
    internal static byte[] RestoreFromBase(byte[] baseline, byte[] source, ProjectGame game) => Rewrite(baseline, source, game, false);

    private static byte[] Rewrite(byte[] baseline, byte[] source, ProjectGame game, bool expanded)
    {
        var original = Read(baseline, game); var current = Read(source, game);
        if (original.Installed) throw new InvalidDataException("Base ExeFS must contain the original model memory budgets.");
        SwShExeFsMainComparison.EnsureCompatibleBaseLayout(original.Main, current.Main, Owner);
        foreach (var region in Reservations(game))
            if (SwShExeFsReservedRegionLedger.Regions.Any(other => other.Owner != Owner
                && other.Area == region.Area && !other.FeatureId.Contains(game == ProjectGame.Sword ? "-shield-" : "-sword-", StringComparison.Ordinal)
                && SwShExeFsReservedRegionLedger.Overlaps(other, region.StartOffset!.Value, 4)))
                throw new InvalidDataException("Model memory support overlaps another executable feature.");
        if (current.Installed == expanded && !current.NeedsPokedexRepair) return source;
        var text = current.Main.Text.DecompressedData.ToArray();
        var ro = current.Main.Ro.DecompressedData.ToArray();
        var data = current.Main.Data.DecompressedData.ToArray();
        foreach (var word in Words)
            Put(word.Area == "main.text" ? text : word.Area == "main.ro" ? ro : data,
                word.Offset(game), expanded ? word.Expanded : word.Original);
        var output = current.Main.Write(textDecompressedData: text, roDecompressedData: ro, dataDecompressedData: data);
        var after = Read(output, game);
        if (after.Installed != expanded || after.NeedsPokedexRepair || !after.Main.Text.DecompressedData.SequenceEqual(text)
            || !after.Main.Ro.DecompressedData.SequenceEqual(ro) || !after.Main.Data.DecompressedData.SequenceEqual(data)
            || !SwShExeFsMainComparison.StableHeaderBytesMatch(current.Main.RawHeader, after.Main.RawHeader))
            throw new InvalidDataException("Model memory output failed preservation verification.");
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

    internal static IReadOnlyList<SwShExeFsReservedRegion> Reservations(ProjectGame game) => Words.Select((word, index) =>
        new SwShExeFsReservedRegion(Owner, $"restoration-model-{game.ToString().ToLowerInvariant()}-{index:D2}",
            MainPath, word.Area, word.Offset(game), 4, word.Label, "payload-only")).ToArray();
    internal static IReadOnlyList<SwShExeFsReservedRegion> Reservations() =>
        [.. Reservations(ProjectGame.Sword), .. Reservations(ProjectGame.Shield)];
    private static byte[] Segment(NsoFile main, string area) =>
        (area == "main.text" ? main.Text : area == "main.ro" ? main.Ro : main.Data).DecompressedData;
    private static uint Word(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    private static void Put(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);
}
