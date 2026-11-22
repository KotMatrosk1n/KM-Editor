// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;
using KM.Core.Projects;
using KM.ZA.Data;
using KM.ZA.FashionCatalog;
using KM.ZA.Workflows;

namespace KM.ZA.FashionUnlock;

internal sealed class ZaFashionUnlockSalon
{
    internal const string VirtualPath = ZaDataPaths.ShopHairMakeLineupArray;

    private static readonly string[] TargetLineups = ["hairsalon_normal_lineup", "hairsalon_special_lineup"];
    private static readonly (uint Item, string Quest)[] Styles =
    [
        (15, "SYS_WORK_QUEST_161"),
        (16, "SYS_WORK_QUEST_164"),
        (17, "SYS_WORK_QUEST_162"),
        (18, "SYS_WORK_QUEST_163"),
        (19, "SYS_WORK_QUEST_165"),
    ];

    private static readonly ZaFashionLineupConditionData Unlocked =
        new(0, true, "force_condition", false, 0, false, []);

    private readonly ZaFashionLineupCatalogDocument document;
    private readonly string baseRevision;
    private readonly byte[] baseBytes;
    private readonly Dictionary<(int Lineup, int Entry), ZaFashionLineupConditionData> vanilla = [];

    private ZaFashionUnlockSalon(ZaWorkflowFile baseline, ZaWorkflowFile effective)
    {
        Effective = effective;
        var baseDocument = Parse(baseline.Bytes);
        baseRevision = baseDocument.CreateStructureRevision();
        baseBytes = baseline.Bytes;
        document = ReferenceEquals(effective, baseline) ? baseDocument : Parse(effective.Bytes);
        foreach (var lineup in TargetLineups)
        {
            foreach (var (item, quest) in Styles)
            {
                var baseEntry = FindEntry(baseDocument, lineup, item);
                var baseCondition = new ZaFashionLineupConditionData(
                    0, true, "work_condition", true, 1, true, [quest, "255"]);
                if (!Matches(baseEntry.Entry, baseCondition))
                    throw new InvalidDataException($"The base Hair Salon condition for item {item} in {lineup} is unsupported.");

                var entry = FindEntry(document, lineup, item);
                if (!Matches(entry.Entry, Unlocked) && !Matches(entry.Entry, baseCondition))
                    throw new InvalidDataException($"The Hair Salon condition for item {item} in {lineup} has conflicting edits.");

                vanilla.Add((entry.Lineup, entry.Entry.PhysicalIndex), baseCondition);
            }
        }

        var unlockedCount = vanilla.Keys.Count(key => Matches(document.Lineups[key.Lineup].Entries[key.Entry], Unlocked));
        IsUnlocked = unlockedCount == vanilla.Count;
        HasUnlockedStyles = unlockedCount != 0;
    }

    internal ZaWorkflowFile Effective { get; }
    internal bool IsUnlocked { get; }
    internal bool HasUnlockedStyles { get; }

    internal static ZaFashionUnlockSalon Read(OpenedProject project)
    {
        var files = new ZaWorkflowFileSource();
        using var reads = ZaWorkflowFileSource.BeginFreshReadScope(project.Paths);
        var effective = files.Read(project, VirtualPath);
        var baseline = effective.SourceLayer == ProjectFileLayer.Base
            ? effective : files.ReadBase(project, VirtualPath);
        return new ZaFashionUnlockSalon(baseline, effective);
    }

    internal byte[]? CreateOutput(bool uninstall)
    {
        var replacements = vanilla
            .Where(pair => !Matches(document.Lineups[pair.Key.Lineup].Entries[pair.Key.Entry],
                uninstall ? pair.Value : Unlocked))
            .ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<ZaFashionLineupConditionGroupData>)
                    [new(0, [new(0, [uninstall ? pair.Value : Unlocked])])]);
        if (replacements.Count == 0)
            return null;

        var bytes = document.Write(replacements);
        var verified = Parse(bytes);
        if (verified.CreateStructureRevision() != document.CreateStructureRevision(replacements))
            throw new InvalidDataException("The updated Hair Salon lineup could not be verified.");

        return bytes;
    }

    internal bool CanDeleteOutput(ProjectPaths paths, byte[] restored) =>
        Effective.Origin == ZaWorkflowFileOrigin.StandaloneLooseOutput
        && Parse(restored).CreateStructureRevision() == baseRevision
        && ZaWorkflowFileSource.CanDeleteStandaloneOutput(paths, Effective, baseBytes);

    private static ZaFashionLineupCatalogDocument Parse(byte[] bytes) =>
        ZaFashionLineupCatalogDocument.Parse(bytes,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal), "Hair Salon lineup");

    private static (int Lineup, ZaFashionLineupEntryData Entry) FindEntry(
        ZaFashionLineupCatalogDocument document, string lineupId, uint item)
    {
        var entries = document.Lineups
            .Where(lineup => lineup.LineupId == lineupId)
            .SelectMany(lineup => lineup.Entries
                .Where(entry => entry.ItemId == item)
                .Select(entry => (lineup.PhysicalIndex, entry)))
            .ToArray();
        return entries.Length == 1 ? entries[0]
            : throw new InvalidDataException($"Hair Salon item {item} must appear once in {lineupId}.");
    }

    private static bool Matches(ZaFashionLineupEntryData entry, ZaFashionLineupConditionData expected)
    {
        return entry.ConditionGroups is [{ Holders: [{ Conditions: [var condition] }] }]
            && condition.HasCondition == expected.HasCondition
            && condition.Condition == expected.Condition
            && condition.HasComparison == expected.HasComparison
            && condition.Comparison == expected.Comparison
            && condition.HasArguments == expected.HasArguments
            && condition.Arguments.SequenceEqual(expected.Arguments, StringComparer.Ordinal);
    }
}
