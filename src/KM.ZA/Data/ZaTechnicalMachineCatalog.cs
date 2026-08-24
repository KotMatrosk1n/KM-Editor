// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.ZA.Generated.GameData;
using KM.ZA.Workflows;

namespace KM.ZA.Data;

internal sealed record ZaTechnicalMachineMove(
    int Slot,
    int ItemId,
    int MachineIndex,
    int MoveId,
    string MoveName,
    string Label,
    bool IsOwnedTestTechnicalMachine);

internal readonly record struct ZaTechnicalMachineNumberAssignment(
    int ItemId,
    int SortNum,
    int MachineIndex);

internal readonly record struct ZaTechnicalMachineNumberRepair(
    int ItemId,
    int SortNum,
    int MachineIndex);

internal static class ZaTechnicalMachineCatalog
{
    public const int LegacySyntheticTechnicalMachineItemId = 2161;
    public const int LegacyMissingTechnicalMachineSlot = 101;
    public const int TestTechnicalMachineTemplateItemId = 2221;
    public const int TestTechnicalMachineItemId = 2222;
    public const int TestTechnicalMachineSlot = 162;
    public const int TestTechnicalMachineIndex = TestTechnicalMachineSlot - 1;
    public const int TestTechnicalMachineMoveId = 405;
    public const string TestTechnicalMachineInternalName = "WAZAMASIN162";
    public const string TestTechnicalMachineIconName = "item_0403";
    public const string TestTechnicalMachineMoveName = "Bug Buzz";

    public const int BaseTechnicalMachineCount = 160;
    public const int FirstOwnedExtensionSlot = 162;
    public const int LastOwnedExtensionSlot = 201;
    public const int FirstOwnedExtensionItemId = 2222;
    public const int LastOwnedExtensionItemId = 2261;

    private const string TestTechnicalMachineTemplateInternalName = "WAZAMASIN161";

    public static IReadOnlyList<ZaTechnicalMachineMove> Load(
        OpenedProject project,
        ZaWorkflowFileSource fileSource,
        ZaTextLabelLookup labels,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        try
        {
            var source = fileSource.Read(project, ZaDataPaths.ItemDataArray);
            var recovery = source.SourceLayer == ProjectFileLayer.Layered
                ? ZaTechnicalMachineLegacyRecoveryDetector.Analyze(
                    source.Bytes,
                    fileSource.ReadBase(project, ZaDataPaths.ItemDataArray).Bytes)
                : ZaTechnicalMachineLegacyRecovery.None;
            if (recovery.IsBlocked)
            {
                diagnostics.Add(ZaWorkflowSupport.Error(
                    recovery.BlockingReason!,
                    $"romfs/{ZaDataPaths.ItemDataArray}",
                    field: "tmNumber",
                    expected: "An exact KM-generated legacy row or the clean physical item table"));
                return [];
            }

            return Read(source.Bytes, labels, recovery, fileSource, diagnostics);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(ZaWorkflowSupport.Warning(
                $"Z-A TM catalog could not be resolved from Items: {exception.Message}",
                $"romfs/{ZaDataPaths.ItemDataArray}"));
            return [];
        }
    }

    public static IReadOnlyList<ZaTechnicalMachineMove> Read(
        byte[] itemData,
        ZaTextLabelLookup labels)
    {
        return Read(
            itemData,
            labels,
            ZaTechnicalMachineLegacyRecovery.None,
            boundedFileSource: null,
            diagnostics: null);
    }

    private static IReadOnlyList<ZaTechnicalMachineMove> Read(
        byte[] itemData,
        ZaTextLabelLookup labels,
        ZaTechnicalMachineLegacyRecovery recovery,
        ZaWorkflowFileSource? boundedFileSource,
        ICollection<ValidationDiagnostic>? diagnostics)
    {
        var table = ZaItemDataArray.GetRootAsZaItemDataArray(new ByteBuffer(itemData));
        boundedFileSource?.EnsureBoundedTableCount(table.ValuesLength, "The Z-A TM item table");
        var items = new List<ZaItemData>(table.ValuesLength);
        var records = new List<ZaTechnicalMachineMove>();
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var item = table.Values(index);
            if (item is null)
            {
                continue;
            }

            items.Add(item.Value);
            if (!IsTechnicalMachine(item.Value))
            {
                continue;
            }

            if (recovery.RemoveSyntheticRow
                && item.Value.Id == LegacySyntheticTechnicalMachineItemId)
            {
                continue;
            }

            var hasRecoveredNumber = recovery.RepairItemId == item.Value.Id
                && recovery.RepairTechnicalMachineNumber is not null;
            int resolvedNumber;
            if (hasRecoveredNumber)
            {
                resolvedNumber = recovery.RepairTechnicalMachineNumber!.Value;
            }
            else if (!TryResolveMachineSlot(
                         item.Value,
                         labels.Item(item.Value.Id),
                         out resolvedNumber))
            {
                continue;
            }

            var moveId = item.Value.MachineWaza;
            var moveName = IsOwnedTestTechnicalMachineRow(item.Value)
                ? ResolveTestTechnicalMachineMoveName(labels, moveId)
                : labels.Move(moveId);
            records.Add(new ZaTechnicalMachineMove(
                resolvedNumber,
                item.Value.Id,
                hasRecoveredNumber ? resolvedNumber - 1 : item.Value.MachineIndex,
                moveId,
                moveName,
                $"{FormatMachineLabel(resolvedNumber)} {moveName}",
                IsOwnedTestTechnicalMachineRow(item.Value)));
        }

        ApplyTestTechnicalMachineProjection(items, records, labels, diagnostics);

        return records
            .GroupBy(record => record.MoveId)
            .Select(group => group
                .OrderBy(record => record.Slot)
                .ThenBy(record => record.ItemId)
                .First())
            .OrderBy(record => record.Slot)
            .ThenBy(record => record.MoveId)
            .ToArray();
    }

    public static bool IsTechnicalMachine(ZaItemData item)
    {
        return item.Pocket == 6
            && item.ItemType == 5
            && item.MachineWaza > 0;
    }

    public static bool TryFindLegacyNumberRepair(
        IReadOnlyList<ZaTechnicalMachineNumberAssignment> assignments,
        int missingNumber,
        out ZaTechnicalMachineNumberRepair repair)
    {
        repair = default;
        var machineCount = assignments.Count;
        if (machineCount < LegacyMissingTechnicalMachineSlot
            || missingNumber < 1
            || missingNumber > machineCount
            || assignments.Any(assignment =>
                assignment.SortNum <= 0
                || assignment.MachineIndex != assignment.SortNum - 1))
        {
            return false;
        }

        var expectedNumbers = Enumerable
            .Range(1, machineCount)
            .Where(number => number != missingNumber)
            .Append(machineCount + 1)
            .Order()
            .ToArray();
        var actualNumbers = assignments
            .Select(assignment => assignment.SortNum)
            .Order()
            .ToArray();
        if (!actualNumbers.SequenceEqual(expectedNumbers))
        {
            return false;
        }

        var outlier = assignments.Single(assignment => assignment.SortNum == machineCount + 1);
        repair = new ZaTechnicalMachineNumberRepair(
            outlier.ItemId,
            missingNumber,
            missingNumber - 1);
        return true;
    }

    public static bool HasCompleteNumbering(
        IReadOnlyList<ZaTechnicalMachineNumberAssignment> assignments)
    {
        return assignments.All(assignment =>
                assignment.SortNum > 0
                && assignment.MachineIndex == assignment.SortNum - 1)
            && assignments
                .Select(assignment => assignment.SortNum)
                .Order()
                .SequenceEqual(Enumerable.Range(1, assignments.Count));
    }

    public static bool HasCompleteNumberingWithTestTechnicalMachineExtension(
        IReadOnlyList<ZaTechnicalMachineNumberAssignment> assignments)
    {
        return HasCompleteNumberingWithOwnedExtensions(assignments);
    }

    public static bool HasCompleteNumberingWithOwnedExtensions(
        IReadOnlyList<ZaTechnicalMachineNumberAssignment> assignments)
    {
        if (assignments.Count < BaseTechnicalMachineCount
            || assignments.Count > BaseTechnicalMachineCount
                + LastOwnedExtensionSlot - FirstOwnedExtensionSlot + 1
            || assignments.Select(assignment => assignment.ItemId).Distinct().Count() != assignments.Count)
        {
            return false;
        }

        var extensionAssignments = assignments
            .Where(assignment => IsOwnedExtensionItemId(assignment.ItemId))
            .ToArray();
        if (extensionAssignments.Any(assignment =>
                !TryGetOwnedExtensionSlot(assignment.ItemId, out var slot)
                || assignment.SortNum != slot
                || assignment.MachineIndex != slot - 1))
        {
            return false;
        }

        var baseAssignments = assignments
            .Where(assignment => !IsOwnedExtensionItemId(assignment.ItemId))
            .ToArray();
        return baseAssignments.Length == BaseTechnicalMachineCount
            && HasCompleteNumbering(baseAssignments);
    }

    public static bool IsOwnedExtensionSlot(int slot) =>
        slot is >= FirstOwnedExtensionSlot and <= LastOwnedExtensionSlot;

    public static bool IsOwnedExtensionItemId(int itemId) =>
        itemId is >= FirstOwnedExtensionItemId and <= LastOwnedExtensionItemId;

    public static int GetOwnedExtensionItemId(int slot)
    {
        if (!IsOwnedExtensionSlot(slot))
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }

        return FirstOwnedExtensionItemId + slot - FirstOwnedExtensionSlot;
    }

    public static string GetOwnedExtensionInternalName(int slot)
    {
        if (!IsOwnedExtensionSlot(slot))
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }

        return $"WAZAMASIN{slot.ToString(CultureInfo.InvariantCulture)}";
    }

    public static bool TryGetOwnedExtensionSlot(int itemId, out int slot)
    {
        slot = FirstOwnedExtensionSlot + itemId - FirstOwnedExtensionItemId;
        return IsOwnedExtensionItemId(itemId) && IsOwnedExtensionSlot(slot);
    }

    public static bool HasOwnedExtensionIdentity(
        int itemId,
        int itemType,
        string? internalName,
        int pocket,
        int sortNum,
        int machineIndex)
    {
        return TryGetOwnedExtensionSlot(itemId, out var slot)
            && itemType == 5
            && string.Equals(internalName, GetOwnedExtensionInternalName(slot), StringComparison.Ordinal)
            && pocket == 6
            && sortNum == slot
            && machineIndex == slot - 1;
    }

    public static bool HasTestTechnicalMachineIdentity(
        int itemId,
        int itemType,
        string? internalName,
        string? iconName,
        int pocket,
        int sortNum,
        int machineIndex,
        int moveId)
    {
        _ = iconName;
        return itemId == TestTechnicalMachineItemId
            && HasOwnedExtensionIdentity(
                itemId,
                itemType,
                internalName,
                pocket,
                sortNum,
                machineIndex)
            && moveId > 0;
    }

    public static bool IsOwnedTestTechnicalMachineRow(ZaItemData item)
    {
        return HasTestTechnicalMachineIdentity(
            item.Id,
            item.ItemType,
            item.InternalName,
            item.IconName,
            item.Pocket,
            item.SortNum,
            item.MachineIndex,
            item.MachineWaza);
    }

    public static bool IsOwnedTechnicalMachineExtensionRow(ZaItemData item)
    {
        return HasOwnedExtensionIdentity(
                item.Id,
                item.ItemType,
                item.InternalName,
                item.Pocket,
                item.SortNum,
                item.MachineIndex)
            && item.MachineWaza > 0;
    }

    public static bool IsLegacySyntheticTechnicalMachineTemplate(
        ZaItemData item,
        int physicalTechnicalMachineCount)
    {
        return item.Id == LegacySyntheticTechnicalMachineItemId
            && item.ItemType == 5
            && string.Equals(item.InternalName, "WAZAMASIN101", StringComparison.Ordinal)
            && string.Equals(item.IconName, "item_2161", StringComparison.Ordinal)
            && item.Price == 0
            && item.Pocket == 6
            && item.SlotMaxNum == 1
            && item.SortNum >= 1
            && item.SortNum <= physicalTechnicalMachineCount + 1
            && item.PriceMegaShard == 0
            && item.PriceColorfulScrew == 0
            && !item.CanNotHold
            && item.MachineWaza == 527
            && item.MachineIndex == item.SortNum - 1
            && !item.WorkRecvSleep
            && !item.WorkRecvPoison
            && !item.WorkRecvBurn
            && !item.WorkRecvFreeze
            && !item.WorkRecvParalyze
            && !item.WorkRecvConfuse
            && !item.WorkRecvMero
            && item.WorkAttack == 0
            && item.WorkDefense == 0
            && item.WorkSpAttack == 0
            && item.WorkSpDefense == 0
            && item.WorkSpeed == 0
            && item.WorkAccuracy == 0
            && item.WorkCritical == 0
            && item.WorkEffectGuard == 0
            && item.MintNature is -1 or 0
            && item.WorkRecvPower == 0
            && item.HealPercentage == 0
            && item.WorkRevival == 0
            && item.RevivePercentage == 0
            && item.ExpPointGain == 0
            && item.MaxUseLevel == 0
            && item.WorkFriendly1 == 0
            && item.WorkFriendly2 == 0
            && item.WorkFriendly3 == 0
            && !item.WorkEvolutional
            && !item.WorkFormChange
            && item.WorkStatusHp == 0
            && item.WorkStatusAtk == 0
            && item.WorkStatusDef == 0
            && item.WorkStatusSpd == 0
            && item.WorkStatusSAtk == 0
            && item.WorkStatusSDef == 0
            && item.EquipPower == 0
            && item.AutoHealPriority == 0
            && !item.CanUseInBattle
            && item.SwapIntoId == 0;
    }

    public static bool TryResolveMachineSlot(ZaItemData item, string itemName, out int slot)
    {
        if (item.SortNum > 0)
        {
            slot = item.SortNum;
            return true;
        }

        if (item.MachineIndex >= 0)
        {
            slot = item.MachineIndex + 1;
            return true;
        }

        if (TryParseMachineSlot(itemName, out slot))
        {
            return true;
        }

        slot = 0;
        return false;
    }

    private static bool TryParseMachineSlot(string itemName, out int slot)
    {
        slot = 0;
        var trimmedName = itemName.Trim();
        if (!trimmedName.StartsWith("TM", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var digitCount = 0;
        while (2 + digitCount < trimmedName.Length && char.IsAsciiDigit(trimmedName[2 + digitCount]))
        {
            digitCount++;
        }

        return digitCount > 0
            && int.TryParse(trimmedName.AsSpan(2, digitCount), NumberStyles.None, CultureInfo.InvariantCulture, out slot)
            && slot > 0;
    }

    public static string FormatMachineLabel(int slot)
    {
        return string.Create(CultureInfo.InvariantCulture, $"TM{slot:000}");
    }

    private static void ApplyTestTechnicalMachineProjection(
        IReadOnlyList<ZaItemData> items,
        List<ZaTechnicalMachineMove> records,
        ZaTextLabelLookup labels,
        ICollection<ValidationDiagnostic>? diagnostics)
    {
        var existingTm162Rows = items
            .Where(item => item.Id == TestTechnicalMachineItemId)
            .ToArray();
        if (existingTm162Rows.Length > 1)
        {
            records.RemoveAll(record => record.ItemId == TestTechnicalMachineItemId);
            AddTestTechnicalMachineCollisionDiagnostic(
                diagnostics,
                $"Item {TestTechnicalMachineItemId.ToString(CultureInfo.InvariantCulture)} is duplicated, so KM Editor cannot determine a safe TM162 owner.");
            return;
        }
        if (existingTm162Rows.Length == 1
            && !IsOwnedTestTechnicalMachineRow(existingTm162Rows[0]))
        {
            AddTestTechnicalMachineCollisionDiagnostic(
                diagnostics,
                $"Item {TestTechnicalMachineItemId.ToString(CultureInfo.InvariantCulture)} already exists but does not match KM Editor's owned TM162 slot identity. The existing item remains available and is not modified.");
            return;
        }

        var assignments = records
            .Select(record => new ZaTechnicalMachineNumberAssignment(
                record.ItemId,
                record.Slot,
                record.MachineIndex))
            .ToArray();
        var templateRows = items
            .Where(item =>
                item.Id == TestTechnicalMachineTemplateItemId
                && item.ItemType == 5
                && item.Pocket == 6
                && item.MachineWaza > 0
                && string.Equals(
                    item.InternalName,
                    TestTechnicalMachineTemplateInternalName,
                    StringComparison.Ordinal))
            .ToArray();
        var ownedExtensionRowsAreValid = records
            .Where(record => IsOwnedExtensionItemId(record.ItemId))
            .All(record => items.Count(item =>
                item.Id == record.ItemId
                && IsOwnedTechnicalMachineExtensionRow(item)) == 1);
        var sourceIsEligible = items.Select(item => item.Id).Distinct().Count() == items.Count
            && templateRows.Length == 1
            && records.Select(record => record.ItemId).Distinct().Count() == records.Count
            && records.Select(record => record.MoveId).Distinct().Count() == records.Count
            && ownedExtensionRowsAreValid
            && (HasCompleteNumbering(assignments)
                || HasCompleteNumberingWithOwnedExtensions(assignments));
        if (!sourceIsEligible)
        {
            diagnostics?.Add(ZaWorkflowSupport.Warning(
                "Unused TM slots were not projected because the loaded item data is not the supported complete 160-TM source or a valid KM-owned extension.",
                $"romfs/{ZaDataPaths.ItemDataArray}",
                field: "tmNumber",
                expected: "Unique physical TM numbers 1 through 160 plus structurally owned slots from 162 through 201"));
            return;
        }

        var ownedItemRows = items
            .Where(item => item.Id == TestTechnicalMachineItemId)
            .ToArray();
        if (ownedItemRows.Length > 0)
        {
            if (ownedItemRows.Length != 1)
            {
                records.RemoveAll(record => record.ItemId == TestTechnicalMachineItemId);
                AddTestTechnicalMachineCollisionDiagnostic(
                    diagnostics,
                    $"Item {TestTechnicalMachineItemId.ToString(CultureInfo.InvariantCulture)} is duplicated, so KM Editor cannot determine a safe TM162 owner.");
                return;
            }

            if (!IsOwnedTestTechnicalMachineRow(ownedItemRows[0]))
            {
                AddTestTechnicalMachineCollisionDiagnostic(
                    diagnostics,
                    $"Item {TestTechnicalMachineItemId.ToString(CultureInfo.InvariantCulture)} already exists but does not match KM Editor's owned TM162 slot identity. The existing item remains available and is not modified.");
            }

            return;
        }

        var hasItemIdCollision = records.Any(record => record.ItemId == TestTechnicalMachineItemId);
        var hasSlotCollision = records.Any(record => record.Slot == TestTechnicalMachineSlot);
        var hasMoveCollisionWithoutExtension = records.Any(record => record.MoveId == TestTechnicalMachineMoveId);
        var hasInternalNameCollision = items.Any(item => string.Equals(
            item.InternalName,
            TestTechnicalMachineInternalName,
            StringComparison.Ordinal));
        if (hasItemIdCollision
            || hasSlotCollision
            || hasMoveCollisionWithoutExtension
            || hasInternalNameCollision)
        {
            AddTestTechnicalMachineCollisionDiagnostic(
                diagnostics,
                "TM162 Bug Buzz was not projected because its item ID, internal token, TM number, or move is already owned by the loaded item data.");
            return;
        }

        var moveName = ResolveTestTechnicalMachineMoveName(labels, TestTechnicalMachineMoveId);
        records.Add(new ZaTechnicalMachineMove(
            TestTechnicalMachineSlot,
            TestTechnicalMachineItemId,
            TestTechnicalMachineIndex,
            TestTechnicalMachineMoveId,
            moveName,
            $"{FormatMachineLabel(TestTechnicalMachineSlot)} {moveName}",
            IsOwnedTestTechnicalMachine: true));
    }

    internal static string ResolveTestTechnicalMachineMoveName(
        ZaTextLabelLookup labels,
        int moveId = TestTechnicalMachineMoveId)
    {
        var resolved = labels.Move(moveId);
        return string.Equals(
            resolved,
            $"Move {moveId.ToString(CultureInfo.InvariantCulture)}",
            StringComparison.Ordinal)
                ? moveId == TestTechnicalMachineMoveId
                    ? TestTechnicalMachineMoveName
                    : resolved
                : resolved;
    }

    private static void AddTestTechnicalMachineCollisionDiagnostic(
        ICollection<ValidationDiagnostic>? diagnostics,
        string message)
    {
        diagnostics?.Add(ZaWorkflowSupport.Warning(
            message,
            $"romfs/{ZaDataPaths.ItemDataArray}",
            field: "tmNumber",
            expected: "Unclaimed item 2222 and TM162, with an unclaimed default Bug Buzz assignment"));
    }
}
