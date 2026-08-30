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
    string Label);

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
    public const int OwnedExtensionTemplateItemId = 2221;
    public const string OwnedExtensionIconName = "item_0403";

    public const int BaseTechnicalMachineCount = 160;
    public const int FirstOwnedExtensionSlot = 162;
    public const int LastOwnedExtensionSlot = 201;
    public const int FirstOwnedExtensionItemId = 2222;
    public const int LastOwnedExtensionItemId = 2261;

    public const string OwnedExtensionTemplateInternalName = "WAZAMASIN161";

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

            return Read(source.Bytes, labels, recovery, fileSource);
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
            boundedFileSource: null);
    }

    private static IReadOnlyList<ZaTechnicalMachineMove> Read(
        byte[] itemData,
        ZaTextLabelLookup labels,
        ZaTechnicalMachineLegacyRecovery recovery,
        ZaWorkflowFileSource? boundedFileSource)
    {
        var table = ZaItemDataArray.GetRootAsZaItemDataArray(new ByteBuffer(itemData));
        boundedFileSource?.EnsureBoundedTableCount(table.ValuesLength, "The Z-A TM item table");
        var records = new List<ZaTechnicalMachineMove>();
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var item = table.Values(index);
            if (item is null)
            {
                continue;
            }

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
            var moveName = labels.Move(moveId);
            records.Add(new ZaTechnicalMachineMove(
                resolvedNumber,
                item.Value.Id,
                hasRecoveredNumber ? resolvedNumber - 1 : item.Value.MachineIndex,
                moveId,
                moveName,
                $"{FormatMachineLabel(resolvedNumber)} {moveName}"));
        }

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

}
