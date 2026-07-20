// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Formats.ZA.Generated.GameData;

namespace KM.ZA.Data;

internal sealed record ZaTechnicalMachineLegacyRecovery(
    bool RemoveSyntheticRow,
    int? RepairItemId,
    int? RepairTechnicalMachineNumber,
    int PhysicalTechnicalMachineCount,
    int? BaseSlot101OwnerItemId,
    string? BlockingReason)
{
    public static ZaTechnicalMachineLegacyRecovery None { get; } =
        new(false, null, null, 0, null, null);

    public IReadOnlyList<ZaTechnicalMachineIconRepair> IconRepairs { get; init; } = [];

    public string? IconRepairWarning { get; init; }

    public bool HasChanges =>
        RemoveSyntheticRow
        || RepairItemId is not null
        || IconRepairs.Count > 0;

    public bool IsBlocked => !string.IsNullOrWhiteSpace(BlockingReason);
}

internal readonly record struct ZaTechnicalMachineIconRepair(
    int ItemId,
    string PreviousIconName,
    string RepairedIconName);

internal static class ZaTechnicalMachineLegacyRecoveryDetector
{
    private const int TechnicalMachineTypeCount = 18;

    public static ZaTechnicalMachineLegacyRecovery Analyze(
        byte[] activeBytes,
        byte[] baseBytes)
    {
        ArgumentNullException.ThrowIfNull(activeBytes);
        ArgumentNullException.ThrowIfNull(baseBytes);

        var baseRows = ReadRows(baseBytes);
        var activeRows = ReadRows(activeBytes);
        if (!HasUniqueItemIds(baseRows) || !HasUniqueItemIds(activeRows))
        {
            return ZaTechnicalMachineLegacyRecovery.None with
            {
                BlockingReason = "Legacy TM recovery requires unique item IDs.",
            };
        }

        if (baseRows.Any(row => row.Id == ZaTechnicalMachineCatalog.LegacySyntheticTechnicalMachineItemId))
        {
            // A future base revision owning this ID is not the legacy KM Editor case.
            return ZaTechnicalMachineLegacyRecovery.None;
        }

        var baseMachines = baseRows
            .Where(ZaTechnicalMachineCatalog.IsTechnicalMachine)
            .ToArray();
        var baseAssignments = CreateAssignments(baseMachines);
        if (!ZaTechnicalMachineCatalog.HasCompleteNumbering(baseAssignments))
        {
            return ZaTechnicalMachineLegacyRecovery.None;
        }

        var syntheticRows = activeRows
            .Where(row => row.Id == ZaTechnicalMachineCatalog.LegacySyntheticTechnicalMachineItemId)
            .ToArray();
        var removeSyntheticRow = syntheticRows.Length == 1
            && ZaTechnicalMachineCatalog.IsLegacySyntheticTechnicalMachineTemplate(
                syntheticRows[0],
                baseMachines.Length);
        if (syntheticRows.Length > 0 && !removeSyntheticRow)
        {
            return new ZaTechnicalMachineLegacyRecovery(
                false,
                null,
                null,
                baseMachines.Length,
                TryResolvePhysicalSlotOwner(baseBytes, ZaTechnicalMachineCatalog.LegacyMissingTechnicalMachineSlot, out var owner)
                    ? owner
                    : null,
                "Item 2161 is customized or does not match KM Editor's exact legacy synthetic TM template. "
                + "It cannot be removed automatically without risking user data.");
        }

        var activeMachines = activeRows
            .Where(row =>
                row.Id != ZaTechnicalMachineCatalog.LegacySyntheticTechnicalMachineItemId
                && ZaTechnicalMachineCatalog.IsTechnicalMachine(row))
            .ToArray();
        var baseMachineIds = baseMachines.Select(row => row.Id).Order().ToArray();
        var activeMachineIds = activeMachines.Select(row => row.Id).Order().ToArray();
        var baseSlotOwner = TryResolvePhysicalSlotOwner(
            baseBytes,
            ZaTechnicalMachineCatalog.LegacyMissingTechnicalMachineSlot,
            out var baseSlot101Owner)
                ? baseSlot101Owner
                : (int?)null;
        if (!activeMachineIds.SequenceEqual(baseMachineIds))
        {
            return new ZaTechnicalMachineLegacyRecovery(
                removeSyntheticRow,
                null,
                null,
                activeMachines.Length,
                baseSlotOwner,
                null);
        }

        var assignments = CreateAssignments(activeMachines);
        ZaTechnicalMachineNumberRepair? repair = null;
        if (!ZaTechnicalMachineCatalog.HasCompleteNumbering(assignments))
        {
            var missingNumber = removeSyntheticRow
                ? syntheticRows[0].SortNum
                : ZaTechnicalMachineCatalog.LegacyMissingTechnicalMachineSlot;
            if (ZaTechnicalMachineCatalog.TryFindLegacyNumberRepair(
                    assignments,
                    missingNumber,
                    out var detectedRepair))
            {
                repair = detectedRepair;
            }
        }

        return new ZaTechnicalMachineLegacyRecovery(
            removeSyntheticRow,
            repair?.ItemId,
            repair?.SortNum,
            activeMachines.Length,
            baseSlotOwner,
            null);
    }

    public static ZaTechnicalMachineLegacyRecovery AnalyzeWithMoveData(
        byte[] activeItemBytes,
        byte[] baseItemBytes,
        byte[] activeMoveBytes,
        byte[] baseMoveBytes)
    {
        ArgumentNullException.ThrowIfNull(activeMoveBytes);
        ArgumentNullException.ThrowIfNull(baseMoveBytes);

        var recovery = Analyze(activeItemBytes, baseItemBytes);
        if (recovery.IsBlocked
            || !recovery.HasChanges
            || recovery.BaseSlot101OwnerItemId is not { } iconCandidateItemId)
        {
            return recovery;
        }

        try
        {
            var activeItems = ReadRows(activeItemBytes)
                .Where(row => row.Id == iconCandidateItemId)
                .ToArray();
            var baseItems = ReadRows(baseItemBytes)
                .Where(row => row.Id == iconCandidateItemId)
                .ToArray();
            if (activeItems.Length != 1 || baseItems.Length != 1)
            {
                return WithIconRepairWarning(
                    recovery,
                    "the affected physical item is not unique in both the active and clean item tables");
            }

            var activeItem = activeItems[0];
            var baseItem = baseItems[0];
            if (activeItem.MachineWaza == baseItem.MachineWaza
                || !string.Equals(
                    activeItem.IconName,
                    baseItem.IconName,
                    StringComparison.Ordinal))
            {
                // An unchanged move needs no icon migration. An icon that already differs
                // from clean base data is treated as an intentional external customization.
                return recovery;
            }

            var baseMoveTypes = ReadMoveTypes(baseMoveBytes);
            var activeMoveTypes = ReadMoveTypes(activeMoveBytes);
            if (!TryResolveUniqueMoveType(
                    baseMoveTypes,
                    baseItem.MachineWaza,
                    out var baseMoveType)
                || !TryResolveUniqueMoveType(
                    activeMoveTypes,
                    activeItem.MachineWaza,
                    out var activeMoveType))
            {
                return WithIconRepairWarning(
                    recovery,
                    "the original or current TM move does not resolve uniquely in the move table");
            }

            if (activeMoveType == baseMoveType)
            {
                // Different moves of the same type intentionally share the same disc icon.
                return recovery;
            }

            var baseMachines = ReadRows(baseItemBytes)
                .Where(ZaTechnicalMachineCatalog.IsTechnicalMachine)
                .ToArray();
            if (!TryCreateCanonicalTypeIconMap(
                    baseMachines,
                    baseMoveTypes,
                    out var canonicalIcons)
                || !canonicalIcons.TryGetValue(activeMoveType, out var repairedIconName))
            {
                return WithIconRepairWarning(
                    recovery,
                    "the clean physical TM data does not provide an unambiguous icon for the current move type");
            }

            var previousIconName = activeItem.IconName ?? string.Empty;
            if (string.Equals(previousIconName, repairedIconName, StringComparison.Ordinal))
            {
                return recovery;
            }

            return recovery with
            {
                IconRepairs =
                [
                    new ZaTechnicalMachineIconRepair(
                        iconCandidateItemId,
                        previousIconName,
                        repairedIconName),
                ],
            };
        }
        catch (Exception exception) when (
            exception is InvalidDataException
                or InvalidOperationException
                or ArgumentException
                or IndexOutOfRangeException)
        {
            return WithIconRepairWarning(
                recovery,
                "the active or clean move data could not be parsed safely");
        }
    }

    public static bool TryResolvePhysicalSlotOwner(
        byte[] itemData,
        int slot,
        out int itemId)
    {
        ArgumentNullException.ThrowIfNull(itemData);

        itemId = 0;
        var owners = ReadRows(itemData)
            .Where(row =>
                row.Id != ZaTechnicalMachineCatalog.LegacySyntheticTechnicalMachineItemId
                && ZaTechnicalMachineCatalog.IsTechnicalMachine(row)
                && row.SortNum == slot
                && row.MachineIndex == slot - 1)
            .Select(row => row.Id)
            .Distinct()
            .ToArray();
        if (owners.Length != 1)
        {
            return false;
        }

        itemId = owners[0];
        return true;
    }

    private static ZaTechnicalMachineNumberAssignment[] CreateAssignments(
        IEnumerable<ZaItemData> rows)
    {
        return rows
            .Select(row => new ZaTechnicalMachineNumberAssignment(
                row.Id,
                row.SortNum,
                row.MachineIndex))
            .ToArray();
    }

    private static bool HasUniqueItemIds(IReadOnlyList<ZaItemData> rows)
    {
        return rows.Select(row => row.Id).Distinct().Count() == rows.Count;
    }

    private static ZaTechnicalMachineLegacyRecovery WithIconRepairWarning(
        ZaTechnicalMachineLegacyRecovery recovery,
        string reason)
    {
        return recovery with
        {
            IconRepairWarning =
                $"Legacy TM numbering can still be repaired, but KM will leave the affected disc icon unchanged because {reason}.",
        };
    }

    private static bool TryCreateCanonicalTypeIconMap(
        IReadOnlyList<ZaItemData> baseMachines,
        IReadOnlyList<ZaMoveType> baseMoveTypes,
        out IReadOnlyDictionary<byte, string> canonicalIcons)
    {
        canonicalIcons = new Dictionary<byte, string>();
        var assignments = new List<(byte Type, string IconName)>(baseMachines.Count);
        foreach (var machine in baseMachines)
        {
            var iconName = machine.IconName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(iconName)
                || !TryResolveUniqueMoveType(
                    baseMoveTypes,
                    machine.MachineWaza,
                    out var moveType))
            {
                return false;
            }

            assignments.Add((moveType, iconName));
        }

        var typeGroups = assignments
            .GroupBy(assignment => assignment.Type)
            .ToArray();
        var iconGroups = assignments
            .GroupBy(assignment => assignment.IconName, StringComparer.Ordinal)
            .ToArray();
        if (typeGroups.Length != TechnicalMachineTypeCount
            || iconGroups.Length != TechnicalMachineTypeCount
            || !typeGroups
                .Select(group => (int)group.Key)
                .Order()
                .SequenceEqual(Enumerable.Range(0, TechnicalMachineTypeCount))
            || typeGroups.Any(group =>
                group.Select(assignment => assignment.IconName)
                    .Distinct(StringComparer.Ordinal)
                    .Count() != 1)
            || iconGroups.Any(group =>
                group.Select(assignment => assignment.Type).Distinct().Count() != 1))
        {
            return false;
        }

        canonicalIcons = typeGroups.ToDictionary(
            group => group.Key,
            group => group.First().IconName);
        return true;
    }

    private static bool TryResolveUniqueMoveType(
        IReadOnlyList<ZaMoveType> moves,
        ushort moveId,
        out byte moveType)
    {
        moveType = 0;
        var matches = moves
            .Where(move => move.MoveId == moveId)
            .Select(move => move.Type)
            .ToArray();
        if (matches.Length != 1)
        {
            return false;
        }

        moveType = matches[0];
        return true;
    }

    private static List<ZaMoveType> ReadMoveTypes(byte[] bytes)
    {
        var table = ZaMoveDataArray.GetRootAsZaMoveDataArray(new ByteBuffer(bytes));
        var rows = new List<ZaMoveType>(table.ValuesLength);
        for (var index = 0; index < table.ValuesLength; index++)
        {
            if (table.Values(index) is { } row)
            {
                rows.Add(new ZaMoveType(row.MoveId, row.Type));
            }
        }

        return rows;
    }

    private static List<ZaItemData> ReadRows(byte[] bytes)
    {
        var table = ZaItemDataArray.GetRootAsZaItemDataArray(new ByteBuffer(bytes));
        var rows = new List<ZaItemData>(table.ValuesLength);
        for (var index = 0; index < table.ValuesLength; index++)
        {
            if (table.Values(index) is { } row)
            {
                rows.Add(row);
            }
        }

        return rows;
    }

    private readonly record struct ZaMoveType(ushort MoveId, byte Type);
}
