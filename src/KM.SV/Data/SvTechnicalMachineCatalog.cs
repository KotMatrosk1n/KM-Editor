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
        var records = new List<SvTechnicalMachineMove>();
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var item = table.Values(index);
            if (item is null || !IsTechnicalMachine(item.Value))
            {
                continue;
            }

            var moveId = (int)item.Value.MachineWaza;
            if (moveId <= 0)
            {
                continue;
            }

            var slot = item.Value.GroupID > 0 ? item.Value.GroupID : item.Value.Id;
            var moveName = labels.Move(moveId);
            records.Add(new SvTechnicalMachineMove(
                slot,
                item.Value.Id,
                moveId,
                moveName,
                $"{FormatMachineLabel(slot)} {moveName}"));
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
        return item.ItemGroup == global::ItemGroup.ITEMGROUP_WAZA_MACHINE
            || item.FieldFunctionType == global::FieldFunctionType.FIELDFUNC_WAZA
            || ((int)item.MachineWaza > 0 && item.GroupID > 0);
    }

    public static string FormatMachineLabel(int slot)
    {
        return string.Create(CultureInfo.InvariantCulture, $"TM{slot:000}");
    }
}
