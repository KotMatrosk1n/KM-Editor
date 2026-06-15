// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.SwSh.Items;
using KM.SwSh.Workflows;

namespace KM.SV;

internal sealed class SvItemsWorkflowService
{
    private static readonly IReadOnlyList<SwShItemEditableField> EditableFields =
    [
        CreateField(SwShItemsWorkflowService.BuyPriceField, "Buy price", 0, 999_999),
        CreateField(SwShItemsWorkflowService.WattsPriceField, "BP price", 0, 999_999),
        CreateField(SwShItemsWorkflowService.PouchField, "Field pocket", 0, int.MaxValue),
        CreateField(SwShItemsWorkflowService.FlingPowerField, "Throw power", 0, int.MaxValue),
        CreateField(SwShItemsWorkflowService.FieldUseTypeField, "Field function", 0, int.MaxValue),
        CreateField(SwShItemsWorkflowService.CanUseOnPokemonField, "Can use on Pokemon", 0, 1, BooleanOptions, "boolean"),
        CreateField(SwShItemsWorkflowService.ItemTypeField, "Item type", 0, int.MaxValue),
        CreateField(SwShItemsWorkflowService.SortIndexField, "Sort index", 0, int.MaxValue),
        CreateField(SwShItemsWorkflowService.GroupTypeField, "Item group", 0, int.MaxValue),
        CreateField(SwShItemsWorkflowService.GroupIndexField, "Group ID", 0, int.MaxValue),
        CreateField(SwShItemsWorkflowService.AttackBoostField, "Attack boost", int.MinValue, int.MaxValue),
        CreateField(SwShItemsWorkflowService.DefenseBoostField, "Defense boost", int.MinValue, int.MaxValue),
        CreateField(SwShItemsWorkflowService.SpecialAttackBoostField, "Sp. Atk boost", int.MinValue, int.MaxValue),
        CreateField(SwShItemsWorkflowService.SpecialDefenseBoostField, "Sp. Def boost", int.MinValue, int.MaxValue),
        CreateField(SwShItemsWorkflowService.SpeedBoostField, "Speed boost", int.MinValue, int.MaxValue),
        CreateField(SwShItemsWorkflowService.AccuracyBoostField, "Accuracy boost", int.MinValue, int.MaxValue),
        CreateField(SwShItemsWorkflowService.CriticalHitBoostField, "Critical boost", int.MinValue, int.MaxValue),
        CreateField(SwShItemsWorkflowService.EvHpField, "HP EV", int.MinValue, int.MaxValue),
        CreateField(SwShItemsWorkflowService.EvAttackField, "Attack EV", int.MinValue, int.MaxValue),
        CreateField(SwShItemsWorkflowService.EvDefenseField, "Defense EV", int.MinValue, int.MaxValue),
        CreateField(SwShItemsWorkflowService.EvSpeedField, "Speed EV", int.MinValue, int.MaxValue),
        CreateField(SwShItemsWorkflowService.EvSpecialAttackField, "Sp. Atk EV", int.MinValue, int.MaxValue),
        CreateField(SwShItemsWorkflowService.EvSpecialDefenseField, "Sp. Def EV", int.MinValue, int.MaxValue),
        CreateField(SwShItemsWorkflowService.HealAmountField, "Heal amount", int.MinValue, int.MaxValue),
        CreateField(SwShItemsWorkflowService.PpGainField, "PP gain", int.MinValue, int.MaxValue),
        CreateField(SwShItemsWorkflowService.FriendshipGain1Field, "Friendship 1", int.MinValue, int.MaxValue),
        CreateField(SwShItemsWorkflowService.FriendshipGain2Field, "Friendship 2", int.MinValue, int.MaxValue),
        CreateField(SwShItemsWorkflowService.FriendshipGain3Field, "Friendship 3", int.MinValue, int.MaxValue),
        CreateField(SwShItemsWorkflowService.MachineMoveIdField, "TM move", 0, ushort.MaxValue),
    ];

    private static readonly IReadOnlyList<SwShItemEditableFieldOption> BooleanOptions =
    [
        new(0, "No"),
        new(1, "Yes"),
    ];

    private readonly SvWorkflowFileSource fileSource;

    public SvItemsWorkflowService(SvWorkflowFileSource? fileSource = null)
    {
        this.fileSource = fileSource ?? new SvWorkflowFileSource();
    }

    public SwShItemsWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var diagnostics = new List<ValidationDiagnostic>();
        SvWorkflowFile? source = null;
        var items = Array.Empty<SwShItemRecord>();

        try
        {
            source = fileSource.Read(project, SvDataPaths.ItemDataArray);
            items = LoadRecords(source).ToArray();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(SvWorkflowSupport.Error(
                $"Items could not be loaded: {exception.Message}",
                $"romfs/{SvDataPaths.ItemDataArray}"));
        }

        var summary = SvWorkflowSupport.CreateSummary(
            project,
            SwShWorkflowIds.Items,
            "Items",
            "Edit Scarlet/Violet item data and TM move assignments.",
            diagnostics.Count == 0 ? null : diagnostics);

        return new SwShItemsWorkflow(
            summary,
            items,
            EditableFields,
            new SwShItemsWorkflowStats(items.Length, source is null ? 0 : 1),
            diagnostics);
    }

    private static IEnumerable<SwShItemRecord> LoadRecords(SvWorkflowFile source)
    {
        var table = global::ItemDataArray.GetRootAsItemDataArray(new ByteBuffer(source.Bytes));
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var item = table.Values(index);
            if (item is null)
            {
                continue;
            }

            yield return ToRecord(item.Value, source);
        }
    }

    private static SwShItemRecord ToRecord(global::ItemData item, SvWorkflowFile source)
    {
        var machineMoveId = (int)item.MachineWaza;
        var metadata = new SwShItemMetadata(
            (int)item.FieldPocket,
            PouchFlags: 0,
            item.ThrowPower,
            (int)item.FieldFunctionType,
            FieldFlags: 0,
            item.SetToPoke,
            (int)item.ItemType,
            item.SortNum,
            item.Id,
            (int)item.ItemGroup,
            item.GroupID,
            CureStatusFlags: 0,
            item.WorkAttack,
            item.WorkDefense,
            item.WorkSpAttack,
            item.WorkSpDefense,
            item.WorkCommon,
            item.WorkEffectGuard,
            item.WorkStatusHp,
            item.WorkStatusAtk,
            item.WorkStatusDef,
            item.WorkStatusSpd,
            item.WorkStatusSAtk,
            item.WorkStatusSDef,
            item.WorkStatusHp,
            item.WorkPpRcv,
            item.WorkFriendly1,
            item.WorkFriendly2,
            item.WorkFriendly3,
            MachineSlot: machineMoveId > 0 ? item.GroupID : null,
            MachineMoveId: machineMoveId > 0 ? machineMoveId : null,
            MachineMoveName: machineMoveId > 0 ? SvLabels.Move(machineMoveId) : null);

        var detailGroups = new[]
        {
            new SwShItemDetailGroup(
                "Scarlet/Violet",
                [
                    new SwShItemDetail("Icon", item.IconName ?? string.Empty),
                    new SwShItemDetail("Item type", SvLabels.EnumName(item.ItemType, "ITEMTYPE_")),
                    new SwShItemDetail("Field pocket", SvLabels.EnumName(item.FieldPocket, "FPOCKET_")),
                    new SwShItemDetail("Field function", SvLabels.EnumName(item.FieldFunctionType, "FIELDFUNC_")),
                    new SwShItemDetail("Battle function", SvLabels.EnumName(item.BattleFunctionType, "BTLFUNC_")),
                    new SwShItemDetail("Group", SvLabels.EnumName(item.ItemGroup, "ITEMGROUP_")),
                ]),
        };

        return new SwShItemRecord(
            item.Id,
            SvLabels.Item(item.Id),
            SvLabels.EnumName(item.FieldPocket, "FPOCKET_"),
            item.Price,
            item.Price / 2,
            item.BP,
            AlternatePrice: 0,
            metadata,
            SharedItemIds: [],
            detailGroups,
            new SwShItemProvenance(source.RelativePath, source.SourceLayer, source.FileState));
    }

    private static SwShItemEditableField CreateField(
        string field,
        string label,
        int? minimumValue,
        int? maximumValue,
        IReadOnlyList<SwShItemEditableFieldOption>? options = null,
        string valueKind = "integer")
    {
        return new SwShItemEditableField(
            field,
            label,
            valueKind,
            minimumValue,
            maximumValue,
            options ?? Array.Empty<SwShItemEditableFieldOption>());
    }
}
