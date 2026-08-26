// SPDX-License-Identifier: GPL-3.0-only

using KM.ZA.Data;

namespace KM.ZA.Encounters;

internal static class ZaSpecialSpawnNormalizer
{
    private const string TreeBranchTag = "野生ポケ_木の枝";
    private const string TreeTrunkTag = "野生ポケ_木の幹";
    private const string LampTag = "野生ポケ_街灯";
    private const string WallTag = "野生ポケ_壁";
    private const string CeilingTag = "野生ポケ_天井";
    private const string DirtMoundTag = "野生ポケ_土モコ";
    private const string BerryTag = "野生ポケ_通常特殊_きのみ";
    private const string RestTag = "野生ポケ_通常特殊_休憩";
    private const string SleepTag = "野生ポケ_通常特殊_睡眠";
    private const string TemperamentChangeTag = "野生ポケ_温厚６に変更";
    private const string ImmobileTag = "野生ポケ_温厚６_不動";
    private const string ObjectBoundTag = "野生ポケ_オブジェクト";
    private const int SquawkabillySpeciesId = 960;

    // These pairs are verified to support a compatibility group even though they do not
    // occur in that group's clean base placements. Keep them separate from the base pairs:
    // a current output may already have been normalized before the pair was verified, and
    // must pass through reconciliation once so its base action and tags can be restored.
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<SpeciesFormPair>>
        VerifiedCompatiblePairs = new Dictionary<string, IReadOnlyList<SpeciesFormPair>>(
            StringComparer.Ordinal)
        {
            ["perched-bird"] =
            [
                new SpeciesFormPair(SquawkabillySpeciesId, 0),
                new SpeciesFormPair(SquawkabillySpeciesId, 1),
                new SpeciesFormPair(SquawkabillySpeciesId, 2),
                new SpeciesFormPair(SquawkabillySpeciesId, 3),
            ],
        };

    // Compatibility filtering is intentionally limited to geometry-bound attachments.
    // Floating offsets, berry eating, rest, sleep, and object-bound poses work across
    // replacements and are always restored to their verified base actions and tags.
    private static readonly SpecialSpawnDefinition[] Definitions =
    [
        new(
            "tree-branch",
            "tree branch",
            [11552],
            null,
            SpecialSpawnPolicy.FilterByNativePair,
            TagNormalizationMode.TruncateFromFirstMarker,
            [TreeBranchTag]),
        new(
            "tree-trunk",
            "tree trunk",
            [11558],
            null,
            SpecialSpawnPolicy.FilterByNativePair,
            TagNormalizationMode.TruncateFromFirstMarker,
            [TreeTrunkTag]),
        new(
            "spider-web",
            "spider web",
            [11563],
            null,
            SpecialSpawnPolicy.FilterByNativePair,
            TagNormalizationMode.TruncateFromFirstMarker,
            [TreeBranchTag]),
        new(
            "perched-bird",
            "lamppost or fixed perch",
            [11567],
            null,
            SpecialSpawnPolicy.FilterByNativePair,
            TagNormalizationMode.TruncateFromFirstMarker,
            [LampTag, TemperamentChangeTag, ImmobileTag]),
        new(
            "wall",
            "wall",
            [11573],
            null,
            SpecialSpawnPolicy.FilterByNativePair,
            TagNormalizationMode.TruncateFromFirstMarker,
            [WallTag]),
        new(
            "ceiling",
            "ceiling",
            [11577],
            null,
            SpecialSpawnPolicy.FilterByNativePair,
            TagNormalizationMode.TruncateFromFirstMarker,
            [CeilingTag]),
        new(
            "dirt-mound",
            "burrow or dirt mound",
            [11594],
            null,
            SpecialSpawnPolicy.FilterByNativePair,
            TagNormalizationMode.TruncateFromFirstMarker,
            [DirtMoundTag]),
        new(
            "dirt-mound-11595",
            "burrow or dirt mound",
            [11595],
            null,
            SpecialSpawnPolicy.FilterByNativePair,
            TagNormalizationMode.TruncateFromFirstMarker,
            [DirtMoundTag]),
        new(
            "floating-offset",
            "floating offset",
            [11606],
            null,
            SpecialSpawnPolicy.AlwaysPreserve,
            TagNormalizationMode.KeepAll,
            []),
        new(
            "berry",
            "berry-eating pose",
            [11618],
            null,
            SpecialSpawnPolicy.AlwaysPreserve,
            TagNormalizationMode.RemoveTrailingMarkers,
            [BerryTag]),
        new(
            "rest",
            "resting pose",
            [11619],
            null,
            SpecialSpawnPolicy.AlwaysPreserve,
            TagNormalizationMode.RemoveTrailingMarkers,
            [RestTag]),
        new(
            "sleep",
            "sleeping pose",
            [11620],
            null,
            SpecialSpawnPolicy.AlwaysPreserve,
            TagNormalizationMode.RemoveTrailingMarkers,
            [SleepTag]),
        new(
            "perched-bird",
            "fixed perch",
            [],
            ImmobileTag,
            SpecialSpawnPolicy.FilterByNativePair,
            TagNormalizationMode.TruncateFromFirstMarker,
            [TemperamentChangeTag, ImmobileTag]),
        new(
            "object-bound",
            "object-bound placement",
            [],
            ObjectBoundTag,
            SpecialSpawnPolicy.AlwaysPreserve,
            TagNormalizationMode.TruncateFromFirstMarker,
            [ObjectBoundTag]),
        new(
            "rest",
            "resting pose",
            [],
            RestTag,
            SpecialSpawnPolicy.AlwaysPreserve,
            TagNormalizationMode.RemoveTrailingMarkers,
            [RestTag]),
    ];

    public static ZaSpecialSpawnNormalizationResult Reconcile(
        ZaEncountersWorkflow currentWorkflow,
        ZaEncountersWorkflow effectiveWorkflow,
        ZaPokemonSpawnerDataDocument spawnerDocument,
        ZaPokemonSpawnerDataDocument baseSpawnerDocument,
        ZaEncounterDataDocument baseEncounterDocument)
    {
        ArgumentNullException.ThrowIfNull(currentWorkflow);
        ArgumentNullException.ThrowIfNull(effectiveWorkflow);
        ArgumentNullException.ThrowIfNull(spawnerDocument);
        ArgumentNullException.ThrowIfNull(baseSpawnerDocument);
        ArgumentNullException.ThrowIfNull(baseEncounterDocument);

        var currentPairs = CreatePairMap(currentWorkflow);
        var effectivePairs = CreatePairMap(effectiveWorkflow);
        var baseEncounterRows = CreateEncounterRowMap(baseEncounterDocument);
        var baseSpecialSlots = FindBaseSpecialSlots(baseSpawnerDocument, baseEncounterRows);
        var compatibility = CreateCompatibilityCatalog(baseSpecialSlots);
        var nativeBasePairs = compatibility.NativePairs;
        var compatiblePairs = compatibility.CompatiblePairs;
        var currentSpawners = spawnerDocument.Entries.ToDictionary(
            entry => (entry.GroupIndex, entry.SpawnerIndex));
        var errors = new List<string>();
        var normalizedCount = 0;
        var restoredCount = 0;

        foreach (var candidate in baseSpecialSlots)
        {
            var key = (
                candidate.Spawner.GroupIndex,
                candidate.Spawner.SpawnerIndex,
                candidate.Slot.SlotIndex);
            if (!currentSpawners.TryGetValue(
                    (candidate.Spawner.GroupIndex, candidate.Spawner.SpawnerIndex),
                    out var currentSpawner)
                || !string.Equals(currentSpawner.Id, candidate.Spawner.Id, StringComparison.Ordinal)
                || (uint)candidate.Slot.SlotIndex >= (uint)currentSpawner.EncountDataInfoList.Count
                || currentSpawner.EncountDataInfoList[candidate.Slot.SlotIndex] is not { } currentSlot
                || !string.Equals(
                    currentSlot.EncountDataId,
                    candidate.Slot.EncountDataId,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var alwaysPreserve =
                candidate.Definition.Policy == SpecialSpawnPolicy.AlwaysPreserve;
            var finalUsesSpecialBehavior = true;
            if (!alwaysPreserve)
            {
                if (!currentPairs.TryGetValue(key, out var currentPair)
                    || !effectivePairs.TryGetValue(key, out var effectivePair))
                {
                    continue;
                }

                var profilePairs = compatiblePairs[candidate.Definition.CompatibilityGroup];
                var nativeProfilePairs = nativeBasePairs[candidate.Definition.CompatibilityGroup];
                finalUsesSpecialBehavior = profilePairs.Contains(effectivePair);
                if (finalUsesSpecialBehavior && nativeProfilePairs.Contains(currentPair))
                {
                    continue;
                }
            }

            if (!spawnerDocument.TrySetSlotSpecialSpawnEnabled(
                    candidate.Spawner.GroupIndex,
                    candidate.Spawner.SpawnerIndex,
                    candidate.Slot.SlotIndex,
                    candidate.Slot,
                    candidate.NormalizedTagCount,
                    finalUsesSpecialBehavior,
                    out var changed,
                    out var error))
            {
                errors.Add(
                    $"Spawner '{candidate.Spawner.Id}' slot "
                    + $"{candidate.Slot.SlotIndex + 1} ({candidate.Definition.DisplayName}) "
                    + $"could not be reconciled: {error}");
                continue;
            }

            if (!changed)
            {
                continue;
            }

            if (finalUsesSpecialBehavior)
            {
                restoredCount++;
            }
            else
            {
                normalizedCount++;
            }
        }

        return new ZaSpecialSpawnNormalizationResult(
            normalizedCount,
            restoredCount,
            errors);
    }

    public static IReadOnlyList<ZaEncounterCompatibilityRule> ProjectCompatibilityRules(
        ZaPokemonSpawnerDataDocument baseSpawnerDocument,
        ZaEncounterDataDocument baseEncounterDocument)
    {
        ArgumentNullException.ThrowIfNull(baseSpawnerDocument);
        ArgumentNullException.ThrowIfNull(baseEncounterDocument);

        var baseEncounterRows = CreateEncounterRowMap(baseEncounterDocument);
        var baseSpecialSlots = FindBaseSpecialSlots(baseSpawnerDocument, baseEncounterRows);
        var compatibility = CreateCompatibilityCatalog(baseSpecialSlots);
        var rules = new List<ZaEncounterCompatibilityRule>();

        foreach (var definitionGroup in Definitions
                     .GroupBy(definition =>
                         (definition.CompatibilityGroup, definition.Policy))
                     .OrderBy(group => group.Key.CompatibilityGroup, StringComparer.Ordinal)
                     .ThenBy(group => group.Key.Policy))
        {
            var groupId = definitionGroup.Key.CompatibilityGroup;
            var policy = definitionGroup.Key.Policy;
            var displayName = definitionGroup
                .Select(definition => definition.DisplayName)
                .OrderBy(value => value, StringComparer.Ordinal)
                .First();
            var actionIds = definitionGroup
                .SelectMany(definition => definition.PopActionIds)
                .Distinct()
                .Order()
                .ToArray();
            var pairs = policy == SpecialSpawnPolicy.AlwaysPreserve
                ? []
                : compatibility.CompatiblePairs.GetValueOrDefault(groupId, [])
                    .OrderBy(pair => pair.SpeciesId)
                    .ThenBy(pair => pair.Form)
                    .Select(pair => new ZaEncounterCompatibilityPair(
                        pair.SpeciesId,
                        pair.Form,
                        compatibility.NativePairs.GetValueOrDefault(groupId, []).Contains(pair),
                        VerifiedCompatiblePairs.GetValueOrDefault(groupId, []).Contains(pair)))
                    .ToArray();

            rules.Add(new ZaEncounterCompatibilityRule(
                groupId,
                displayName,
                policy == SpecialSpawnPolicy.AlwaysPreserve
                    ? ZaEncounterCompatibilityPolicy.PreserveForEveryReplacement
                    : ZaEncounterCompatibilityPolicy.FilterByVerifiedPair,
                actionIds,
                definitionGroup.Any(definition => definition.TagOnlyMarker is not null),
                pairs));
        }

        return rules;
    }

    private static SpecialSpawnCompatibilityCatalog CreateCompatibilityCatalog(
        IReadOnlyList<BaseSpecialSlot> baseSpecialSlots)
    {
        var nativeBasePairs = baseSpecialSlots
            .Where(candidate =>
                candidate.Definition.Policy == SpecialSpawnPolicy.FilterByNativePair)
            .GroupBy(candidate => candidate.Definition.CompatibilityGroup, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(candidate => candidate.NativePair).ToHashSet(),
                StringComparer.Ordinal);
        var compatiblePairs = nativeBasePairs.ToDictionary(
            group => group.Key,
            group => group.Value.ToHashSet(),
            StringComparer.Ordinal);
        foreach (var (compatibilityGroup, verifiedPairs) in VerifiedCompatiblePairs)
        {
            if (!compatiblePairs.TryGetValue(compatibilityGroup, out var groupPairs))
            {
                groupPairs = [];
                compatiblePairs.Add(compatibilityGroup, groupPairs);
            }

            groupPairs.UnionWith(verifiedPairs);
        }

        return new SpecialSpawnCompatibilityCatalog(nativeBasePairs, compatiblePairs);
    }

    private static Dictionary<(int GroupIndex, int SpawnerIndex, int SlotIndex), SpeciesFormPair>
        CreatePairMap(ZaEncountersWorkflow workflow)
    {
        var pairs = new Dictionary<(int, int, int), SpeciesFormPair>();
        foreach (var table in workflow.Tables)
        {
            if (!ZaEncountersWorkflowService.TryParseTableId(
                    table.TableId,
                    out var groupIndex,
                    out var spawnerIndex))
            {
                continue;
            }

            foreach (var slot in table.Slots)
            {
                pairs[(groupIndex, spawnerIndex, slot.Slot)] =
                    new SpeciesFormPair(slot.SpeciesId, slot.Form);
            }
        }

        return pairs;
    }

    private static IReadOnlyDictionary<string, ZaPokemonDataEntry> CreateEncounterRowMap(
        ZaEncounterDataDocument document)
    {
        return document.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Id))
            .GroupBy(entry => entry.Id!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.Ordinal);
    }

    private static IReadOnlyList<BaseSpecialSlot> FindBaseSpecialSlots(
        ZaPokemonSpawnerDataDocument baseSpawnerDocument,
        IReadOnlyDictionary<string, ZaPokemonDataEntry> baseEncounterRows)
    {
        var slots = new List<BaseSpecialSlot>();
        foreach (var spawner in baseSpawnerDocument.Entries)
        {
            foreach (var slot in spawner.EncountDataInfoList
                         .OfType<ZaPokemonSpawnerEncountDataInfo>())
            {
                var definition = Definitions.FirstOrDefault(candidate => candidate.Matches(slot));
                if (definition is null
                    || !definition.TryGetNormalizedTagCount(slot, out var normalizedTagCount)
                    || !TryResolveEncounterRow(
                        slot.EncountDataId,
                        baseEncounterRows,
                        out var encounterRow))
                {
                    continue;
                }

                slots.Add(new BaseSpecialSlot(
                    spawner,
                    slot,
                    definition,
                    normalizedTagCount,
                    new SpeciesFormPair(encounterRow.DevNo, encounterRow.FormNo)));
            }
        }

        return slots;
    }

    private static bool TryResolveEncounterRow(
        string? encounterDataId,
        IReadOnlyDictionary<string, ZaPokemonDataEntry> rows,
        out ZaPokemonDataEntry row)
    {
        row = null!;
        if (string.IsNullOrWhiteSpace(encounterDataId))
        {
            return false;
        }

        if (rows.TryGetValue(encounterDataId, out var exactRow) && exactRow is not null)
        {
            row = exactRow;
            return true;
        }

        var normalizedId = ZaEncounterDataIds.NormalizeSpawnerEncounterDataId(encounterDataId);
        if (!string.Equals(normalizedId, encounterDataId, StringComparison.Ordinal)
            && rows.TryGetValue(normalizedId, out var normalizedRow)
            && normalizedRow is not null)
        {
            row = normalizedRow;
            return true;
        }

        return false;
    }

    private sealed record SpecialSpawnDefinition(
        string CompatibilityGroup,
        string DisplayName,
        IReadOnlyList<int> PopActionIds,
        string? TagOnlyMarker,
        SpecialSpawnPolicy Policy,
        TagNormalizationMode TagMode,
        IReadOnlyList<string> BehaviorTags)
    {
        public bool Matches(ZaPokemonSpawnerEncountDataInfo slot)
        {
            if (PopActionIds.Contains(slot.PopActionId))
            {
                return true;
            }

            return slot.PopActionId == 0
                && TagOnlyMarker is not null
                && slot.Tags.Contains(TagOnlyMarker, StringComparer.Ordinal);
        }

        public bool TryGetNormalizedTagCount(
            ZaPokemonSpawnerEncountDataInfo slot,
            out int normalizedTagCount)
        {
            normalizedTagCount = slot.Tags.Count;
            switch (TagMode)
            {
                case TagNormalizationMode.KeepAll:
                    return true;
                case TagNormalizationMode.RemoveTrailingMarkers:
                    while (normalizedTagCount > 0
                           && BehaviorTags.Contains(
                               slot.Tags[normalizedTagCount - 1],
                               StringComparer.Ordinal))
                    {
                        normalizedTagCount--;
                    }

                    return true;
                case TagNormalizationMode.TruncateFromFirstMarker:
                    for (var index = 0; index < slot.Tags.Count; index++)
                    {
                        if (BehaviorTags.Contains(slot.Tags[index], StringComparer.Ordinal))
                        {
                            normalizedTagCount = index;
                            return true;
                        }
                    }

                    return false;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported special-spawn tag mode {TagMode}.");
            }
        }
    }

    private enum SpecialSpawnPolicy
    {
        FilterByNativePair,
        AlwaysPreserve,
    }

    private enum TagNormalizationMode
    {
        KeepAll,
        RemoveTrailingMarkers,
        TruncateFromFirstMarker,
    }

    private readonly record struct SpeciesFormPair(int SpeciesId, int Form);

    private sealed record BaseSpecialSlot(
        ZaPokemonSpawnerDataEntry Spawner,
        ZaPokemonSpawnerEncountDataInfo Slot,
        SpecialSpawnDefinition Definition,
        int NormalizedTagCount,
        SpeciesFormPair NativePair);

    private sealed record SpecialSpawnCompatibilityCatalog(
        IReadOnlyDictionary<string, HashSet<SpeciesFormPair>> NativePairs,
        IReadOnlyDictionary<string, HashSet<SpeciesFormPair>> CompatiblePairs);
}

internal sealed record ZaSpecialSpawnNormalizationResult(
    int NormalizedCount,
    int RestoredCount,
    IReadOnlyList<string> Errors)
{
    public bool HasChanges => NormalizedCount > 0 || RestoredCount > 0;
}
