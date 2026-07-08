// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using Google.FlatBuffers;
using KM.Core.Diagnostics;
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

internal static class ZaTechnicalMachineCatalog
{
    public const int KnownMissingTechnicalMachineItemId = 2161;
    public const int KnownMissingTechnicalMachineSlot = 101;
    public const int KnownMissingTechnicalMachineIndex = KnownMissingTechnicalMachineSlot - 1;
    public const int KnownMissingTechnicalMachineMoveId = 527;

    public static IReadOnlyList<ZaTechnicalMachineMove> Load(
        OpenedProject project,
        ZaWorkflowFileSource fileSource,
        ZaTextLabelLookup labels,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        try
        {
            var source = fileSource.Read(project, ZaDataPaths.ItemDataArray);
            return Read(source.Bytes, labels);
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
        var table = ZaItemDataArray.GetRootAsZaItemDataArray(new ByteBuffer(itemData));
        var records = new List<ZaTechnicalMachineMove>();
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var item = table.Values(index);
            if (item is null || !IsTechnicalMachine(item.Value))
            {
                continue;
            }

            if (!TryResolveMachineSlot(item.Value, labels.Item(item.Value.Id), out var slot))
            {
                continue;
            }

            var moveId = item.Value.MachineWaza;
            var moveName = labels.Move(moveId);
            records.Add(new ZaTechnicalMachineMove(
                slot,
                item.Value.Id,
                item.Value.MachineIndex,
                moveId,
                moveName,
                $"{FormatMachineLabel(slot)} {moveName}"));
        }

        var hasNearbyHighNumberMachine = records.Any(record =>
            record.ItemId is KnownMissingTechnicalMachineItemId - 1 or KnownMissingTechnicalMachineItemId + 1
            || record.Slot is KnownMissingTechnicalMachineSlot - 1 or KnownMissingTechnicalMachineSlot + 1);
        if (hasNearbyHighNumberMachine
            && !records.Any(record => record.ItemId == KnownMissingTechnicalMachineItemId)
            && !records.Any(record => record.Slot == KnownMissingTechnicalMachineSlot))
        {
            records.Add(CreateKnownMissingTechnicalMachine(labels));
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

    public static bool IsKnownMissingTechnicalMachineItemId(int itemId) =>
        itemId == KnownMissingTechnicalMachineItemId;

    public static ZaTechnicalMachineMove CreateKnownMissingTechnicalMachine(ZaTextLabelLookup labels)
    {
        var moveName = labels.Move(KnownMissingTechnicalMachineMoveId);
        return new ZaTechnicalMachineMove(
            KnownMissingTechnicalMachineSlot,
            KnownMissingTechnicalMachineItemId,
            KnownMissingTechnicalMachineIndex,
            KnownMissingTechnicalMachineMoveId,
            moveName,
            $"{FormatMachineLabel(KnownMissingTechnicalMachineSlot)} {moveName}");
    }

    public static bool TryResolveMachineSlot(ZaItemData item, string itemName, out int slot)
    {
        if (item.SortNum > 0)
        {
            slot = item.SortNum;
            return true;
        }

        if (TryParseMachineSlot(itemName, out slot))
        {
            return true;
        }

        if (item.MachineIndex >= 0)
        {
            slot = item.MachineIndex + 1;
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
