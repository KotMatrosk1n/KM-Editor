// SPDX-License-Identifier: GPL-3.0-only

using KM.ZA.Data;

namespace KM.ZA.Encounters;

internal static class ZaWebSpawnNormalizer
{
    private const int SpiderWebPopActionId = 11563;
    private static readonly string[] SpiderWebTags =
    [
        "野生ポケ_ゾーン外",
        "野生ポケ_木の枝",
    ];

    public static ZaWebSpawnNormalizationResult Reconcile(
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
        var baseWebSlots = FindBaseWebSlots(baseSpawnerDocument, baseEncounterRows);
        var compatiblePairs = baseWebSlots
            .Select(candidate => candidate.NativePair)
            .ToHashSet();
        var currentSpawners = spawnerDocument.Entries.ToDictionary(
            entry => (entry.GroupIndex, entry.SpawnerIndex));
        var errors = new List<string>();
        var normalizedCount = 0;
        var restoredCount = 0;

        foreach (var candidate in baseWebSlots)
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
                    StringComparison.Ordinal)
                || !currentPairs.TryGetValue(key, out var currentPair)
                || !effectivePairs.TryGetValue(key, out var effectivePair))
            {
                continue;
            }

            var finalUsesWebSpecies = compatiblePairs.Contains(effectivePair);
            var currentUsesWebSpecies = compatiblePairs.Contains(currentPair);
            if (finalUsesWebSpecies && currentUsesWebSpecies)
            {
                continue;
            }

            var enableWebSpawn = finalUsesWebSpecies;
            if (!spawnerDocument.TrySetSlotWebSpawnEnabled(
                    candidate.Spawner.GroupIndex,
                    candidate.Spawner.SpawnerIndex,
                    candidate.Slot.SlotIndex,
                    candidate.Slot,
                    enableWebSpawn,
                    out var changed,
                    out var error))
            {
                errors.Add(
                    $"Spawner '{candidate.Spawner.Id}' slot "
                    + $"{candidate.Slot.SlotIndex + 1} could not be reconciled: {error}");
                continue;
            }

            if (!changed)
            {
                continue;
            }

            if (enableWebSpawn)
            {
                restoredCount++;
            }
            else
            {
                normalizedCount++;
            }
        }

        return new ZaWebSpawnNormalizationResult(
            normalizedCount,
            restoredCount,
            errors);
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

    private static IReadOnlyList<BaseWebSlot> FindBaseWebSlots(
        ZaPokemonSpawnerDataDocument baseSpawnerDocument,
        IReadOnlyDictionary<string, ZaPokemonDataEntry> baseEncounterRows)
    {
        var slots = new List<BaseWebSlot>();
        foreach (var spawner in baseSpawnerDocument.Entries)
        {
            foreach (var slot in spawner.EncountDataInfoList
                         .OfType<ZaPokemonSpawnerEncountDataInfo>())
            {
                if (slot.PopActionId != SpiderWebPopActionId
                    || !slot.Tags.SequenceEqual(SpiderWebTags, StringComparer.Ordinal)
                    || !TryResolveEncounterRow(
                        slot.EncountDataId,
                        baseEncounterRows,
                        out var encounterRow))
                {
                    continue;
                }

                slots.Add(new BaseWebSlot(
                    spawner,
                    slot,
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

    private readonly record struct SpeciesFormPair(int SpeciesId, int Form);

    private sealed record BaseWebSlot(
        ZaPokemonSpawnerDataEntry Spawner,
        ZaPokemonSpawnerEncountDataInfo Slot,
        SpeciesFormPair NativePair);
}

internal sealed record ZaWebSpawnNormalizationResult(
    int NormalizedCount,
    int RestoredCount,
    IReadOnlyList<string> Errors)
{
    public bool HasChanges => NormalizedCount > 0 || RestoredCount > 0;
}
