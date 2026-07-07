// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.SV.Workflows;

namespace KM.SV.Data;

internal sealed record SvTechnicalMachineMove(
    int Slot,
    int ItemId,
    int GroupId,
    int MoveId,
    string MoveName,
    string Label);

internal static class SvTechnicalMachineCatalog
{
    public static IReadOnlyList<SvTechnicalMachineMove> Load(
        OpenedProject project,
        SvWorkflowFileSource fileSource,
        SvTextLabelLookup labels,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        try
        {
            var source = fileSource.Read(project, SvDataPaths.ItemDataArray);
            return Read(source.Bytes, labels);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(SvWorkflowSupport.Warning(
                $"S/V TM catalog could not be resolved from Items: {exception.Message}",
                $"romfs/{SvDataPaths.ItemDataArray}"));
            return [];
        }
    }

    public static IReadOnlyList<SvTechnicalMachineMove> Read(
        byte[] itemData,
        SvTextLabelLookup labels)
    {
        var table = global::ItemDataArray.GetRootAsItemDataArray(new ByteBuffer(itemData));
        var machineItems = new List<(int Slot, int ItemId, int GroupId, int MoveId, int SortNum)>();
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var item = table.Values(index);
            if (item is null)
            {
                continue;
            }

            var itemName = labels.Item(item.Value.Id);
            if (!IsTechnicalMachine(item.Value, itemName)
                || !TryParseMachineSlot(itemName, out var slot))
            {
                continue;
            }

            var moveId = (int)item.Value.MachineWaza;
            if (moveId <= 0)
            {
                continue;
            }

            machineItems.Add((slot, item.Value.Id, item.Value.GroupID, moveId, item.Value.SortNum));
        }

        var records = new List<SvTechnicalMachineMove>();
        foreach (var item in machineItems
            .OrderBy(item => item.Slot)
            .ThenBy(item => item.SortNum)
            .ThenBy(item => item.ItemId))
        {
            var moveName = labels.Move(item.MoveId);
            records.Add(new SvTechnicalMachineMove(
                item.Slot,
                item.ItemId,
                item.GroupId,
                item.MoveId,
                moveName,
                $"{FormatMachineLabel(item.Slot)} {moveName}"));
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

    public static bool IsTechnicalMachine(global::ItemData item)
    {
        return HasTechnicalMachineShape(item);
    }

    public static bool IsTechnicalMachine(global::ItemData item, string itemName)
    {
        return HasTechnicalMachineShape(item) && TryParseMachineSlot(itemName, out _);
    }

    private static bool HasTechnicalMachineShape(global::ItemData item)
    {
        return item.FieldPocket == global::FieldPocket.FPOCKET_WAZA
            && item.FieldFunctionType == global::FieldFunctionType.FIELDFUNC_WAZA
            && item.ItemType == global::ItemType.ITEMTYPE_WAZA
            && (int)item.MachineWaza > 0;
    }

    private static bool TryParseMachineSlot(string itemName, out int slot)
    {
        slot = 0;
        var trimmedName = itemName.Trim();
        return trimmedName.Length == 5
            && trimmedName.StartsWith("TM", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(trimmedName[2..], NumberStyles.None, CultureInfo.InvariantCulture, out slot)
            && slot > 0;
    }

    public static string FormatMachineLabel(int slot)
    {
        return string.Create(CultureInfo.InvariantCulture, $"TM{slot:000}");
    }
}
