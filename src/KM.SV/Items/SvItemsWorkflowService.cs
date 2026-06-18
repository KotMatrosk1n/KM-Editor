// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.SV.Data;
using KM.SV.Workflows;
using System.Globalization;

namespace KM.SV.Items;

internal sealed class SvItemsWorkflowService
{
    private const string WorkflowLabel = "Items";
    private const string WorkflowDescription = "Edit Scarlet/Violet item data and TM move assignments.";
    public const string BuyPriceField = "buyPrice";
    public const string SellPriceField = "sellPrice";
    public const string WattsPriceField = "wattsPrice";
    public const string AlternatePriceField = "alternatePrice";
    public const string PouchField = "pouch";
    public const string PouchFlagsField = "pouchFlags";
    public const string FlingPowerField = "flingPower";
    public const string FieldUseTypeField = "fieldUseType";
    public const string FieldFlagsField = "fieldFlags";
    public const string CanUseOnPokemonField = "canUseOnPokemon";
    public const string ItemTypeField = "itemType";
    public const string SortIndexField = "sortIndex";
    public const string ItemSpriteField = "itemSprite";
    public const string GroupTypeField = "groupType";
    public const string GroupIndexField = "groupIndex";
    public const string CureStatusFlagsField = "cureStatusFlags";
    public const string AttackBoostField = "attackBoost";
    public const string DefenseBoostField = "defenseBoost";
    public const string SpecialAttackBoostField = "specialAttackBoost";
    public const string SpecialDefenseBoostField = "specialDefenseBoost";
    public const string SpeedBoostField = "speedBoost";
    public const string AccuracyBoostField = "accuracyBoost";
    public const string CriticalHitBoostField = "criticalHitBoost";
    public const string UseFlags1Field = "useFlags1";
    public const string UseFlags2Field = "useFlags2";
    public const string EvHpField = "evHp";
    public const string EvAttackField = "evAttack";
    public const string EvDefenseField = "evDefense";
    public const string EvSpeedField = "evSpeed";
    public const string EvSpecialAttackField = "evSpecialAttack";
    public const string EvSpecialDefenseField = "evSpecialDefense";
    public const string HealAmountField = "healAmount";
    public const string PpGainField = "ppGain";
    public const string FriendshipGain1Field = "friendshipGain1";
    public const string FriendshipGain2Field = "friendshipGain2";
    public const string FriendshipGain3Field = "friendshipGain3";
    public const string MachineMoveIdField = "machineMoveId";

    private static readonly IReadOnlyList<SvItemEditableFieldOption> BooleanOptions =
    [
        new(0, "No"),
        new(1, "Yes"),
    ];

    private static readonly IReadOnlyList<SvItemEditableFieldOption> FieldPocketOptions =
        CreateEnumOptions<global::FieldPocket>("FPOCKET_");

    private static readonly IReadOnlyList<SvItemEditableFieldOption> FieldFunctionOptions =
        CreateEnumOptions<global::FieldFunctionType>("FIELDFUNC_");

    private static readonly IReadOnlyList<SvItemEditableFieldOption> ItemTypeOptions =
        CreateEnumOptions<global::ItemType>("ITEMTYPE_");

    private static readonly IReadOnlyList<SvItemEditableFieldOption> ItemGroupOptions =
        CreateEnumOptions<global::ItemGroup>("ITEMGROUP_");

    private static readonly IReadOnlyList<SvItemEditableField> EditableFields =
    [
        CreateField(SvItemsWorkflowService.BuyPriceField, "Buy price", 0, 999_999),
        CreateField(SvItemsWorkflowService.WattsPriceField, "BP price", 0, 999_999),
        CreateField(SvItemsWorkflowService.PouchField, "Field pocket", 0, int.MaxValue, FieldPocketOptions),
        CreateField(SvItemsWorkflowService.FlingPowerField, "Throw power", 0, int.MaxValue),
        CreateField(SvItemsWorkflowService.FieldUseTypeField, "Field function", 0, int.MaxValue, FieldFunctionOptions),
        CreateField(SvItemsWorkflowService.CanUseOnPokemonField, "Can use on Pokemon", 0, 1, BooleanOptions, "boolean"),
        CreateField(SvItemsWorkflowService.ItemTypeField, "Item type", 0, int.MaxValue, ItemTypeOptions),
        CreateField(SvItemsWorkflowService.SortIndexField, "Sort index", 0, int.MaxValue),
        CreateField(SvItemsWorkflowService.GroupTypeField, "Item group", 0, int.MaxValue, ItemGroupOptions),
        CreateField(SvItemsWorkflowService.GroupIndexField, "Group ID", 0, int.MaxValue),
        // The shared Items panel treats legacy boost fields as packed nibbles, while S/V stores them separately.
        // Keep those fields locked until S/V gets dedicated controls for the raw work values.
        CreateField(SvItemsWorkflowService.EvHpField, "HP EV", int.MinValue, int.MaxValue),
        CreateField(SvItemsWorkflowService.EvAttackField, "Attack EV", int.MinValue, int.MaxValue),
        CreateField(SvItemsWorkflowService.EvDefenseField, "Defense EV", int.MinValue, int.MaxValue),
        CreateField(SvItemsWorkflowService.EvSpeedField, "Speed EV", int.MinValue, int.MaxValue),
        CreateField(SvItemsWorkflowService.EvSpecialAttackField, "Sp. Atk EV", int.MinValue, int.MaxValue),
        CreateField(SvItemsWorkflowService.EvSpecialDefenseField, "Sp. Def EV", int.MinValue, int.MaxValue),
        CreateField(SvItemsWorkflowService.HealAmountField, "Heal amount", int.MinValue, int.MaxValue),
        CreateField(SvItemsWorkflowService.PpGainField, "PP gain", int.MinValue, int.MaxValue),
        CreateField(SvItemsWorkflowService.FriendshipGain1Field, "Friendship 1", int.MinValue, int.MaxValue),
        CreateField(SvItemsWorkflowService.FriendshipGain2Field, "Friendship 2", int.MinValue, int.MaxValue),
        CreateField(SvItemsWorkflowService.FriendshipGain3Field, "Friendship 3", int.MinValue, int.MaxValue),
        CreateField(SvItemsWorkflowService.MachineMoveIdField, "TM move", 0, ushort.MaxValue),
    ];

    private readonly SvWorkflowFileSource fileSource;

    public SvItemsWorkflowService(SvWorkflowFileSource? fileSource = null)
    {
        this.fileSource = fileSource ?? new SvWorkflowFileSource();
    }

    public SvWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return SvWorkflowSupport.CreateSummary(
            project,
            SvWorkflowIds.Items,
            WorkflowLabel,
            WorkflowDescription);
    }

    public SvItemsWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var diagnostics = new List<ValidationDiagnostic>();
        SvWorkflowFile? source = null;
        var items = Array.Empty<SvItemRecord>();
        var labels = SvTextLabelLookup.None();
        var tmCatalog = Array.Empty<SvTechnicalMachineMove>();

        try
        {
            labels = SvTextLabelLookup.Load(project, fileSource, diagnostics);
            source = fileSource.Read(project, SvDataPaths.ItemDataArray);
            tmCatalog = SvTechnicalMachineCatalog.Read(source.Bytes, labels).ToArray();
            items = LoadRecords(source, labels).ToArray();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(SvWorkflowSupport.Error(
                $"Items could not be loaded: {exception.Message}",
                $"romfs/{SvDataPaths.ItemDataArray}"));
        }

        var summary = SvWorkflowSupport.CreateSummary(
            project,
            SvWorkflowIds.Items,
            WorkflowLabel,
            WorkflowDescription,
            diagnostics.Count == 0 ? null : diagnostics);

        return new SvItemsWorkflow(
            summary,
            items,
            CreateEditableFields(labels, items, tmCatalog),
            new SvItemsWorkflowStats(items.Length, source is null ? 0 : 1),
            diagnostics);
    }

    private static IEnumerable<SvItemRecord> LoadRecords(SvWorkflowFile source, SvTextLabelLookup labels)
    {
        var table = global::ItemDataArray.GetRootAsItemDataArray(new ByteBuffer(source.Bytes));
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var item = table.Values(index);
            if (item is null)
            {
                continue;
            }

            yield return ToRecord(item.Value, source, labels);
        }
    }

    private static SvItemRecord ToRecord(global::ItemData item, SvWorkflowFile source, SvTextLabelLookup labels)
    {
        var machineMoveId = (int)item.MachineWaza;
        var metadata = new SvItemMetadata(
            (int)item.FieldPocket,
            PouchFlags: 0,
            item.ThrowPower,
            (int)item.FieldFunctionType,
            FieldFlags: 0,
            CanUseOnPokemon(item.FieldFunctionType),
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
            MachineSlot: SvTechnicalMachineCatalog.IsTechnicalMachine(item) && machineMoveId > 0 ? item.GroupID : null,
            MachineMoveId: machineMoveId > 0 ? machineMoveId : null,
            MachineMoveName: machineMoveId > 0 ? labels.Move(machineMoveId) : null);

        var detailGroups = new[]
        {
            new SvItemDetailGroup(
                "Scarlet/Violet",
                [
                    new SvItemDetail("Icon", item.IconName ?? string.Empty),
                    new SvItemDetail("Item type", SvLabels.EnumName(item.ItemType, "ITEMTYPE_")),
                    new SvItemDetail("Field pocket", SvLabels.EnumName(item.FieldPocket, "FPOCKET_")),
                    new SvItemDetail("Field function", SvLabels.EnumName(item.FieldFunctionType, "FIELDFUNC_")),
                    new SvItemDetail("Battle function", SvLabels.EnumName(item.BattleFunctionType, "BTLFUNC_")),
                    new SvItemDetail("Group", SvLabels.EnumName(item.ItemGroup, "ITEMGROUP_")),
                ]),
        };

        return new SvItemRecord(
            item.Id,
            labels.Item(item.Id),
            SvLabels.EnumName(item.FieldPocket, "FPOCKET_"),
            item.Price,
            item.Price / 2,
            item.BP,
            AlternatePrice: 0,
            metadata,
            SharedItemIds: [],
            detailGroups,
            new SvItemProvenance(source.RelativePath, source.SourceLayer, source.FileState));
    }

    private static SvItemEditableField CreateField(
        string field,
        string label,
        int? minimumValue,
        int? maximumValue,
        IReadOnlyList<SvItemEditableFieldOption>? options = null,
        string valueKind = "integer")
    {
        return new SvItemEditableField(
            field,
            label,
            valueKind,
            minimumValue,
            maximumValue,
            options ?? Array.Empty<SvItemEditableFieldOption>());
    }

    private static IReadOnlyList<SvItemEditableField> CreateEditableFields(
        SvTextLabelLookup labels,
        IReadOnlyList<SvItemRecord> items,
        IReadOnlyList<SvTechnicalMachineMove> tmCatalog)
    {
        var tmMoveOptions = CreateIndexedOptions(labels.MoveNameCount, labels.Move, includeNone: true);
        var groupIndexOptions = CreateGroupIndexOptions(items, tmCatalog);
        return EditableFields
            .Select(field => field.Field switch
            {
                SvItemsWorkflowService.MachineMoveIdField => field with
                {
                    MaximumValue = tmMoveOptions.Count > 0 ? tmMoveOptions.Max(option => option.Value) : field.MaximumValue,
                    Options = tmMoveOptions,
                },
                SvItemsWorkflowService.GroupIndexField => field with
                {
                    MaximumValue = groupIndexOptions.Count > 0 ? groupIndexOptions.Max(option => option.Value) : field.MaximumValue,
                    Options = groupIndexOptions,
                },
                _ => field,
            })
            .ToArray();
    }

    private static IReadOnlyList<SvItemEditableFieldOption> CreateGroupIndexOptions(
        IReadOnlyList<SvItemRecord> items,
        IReadOnlyList<SvTechnicalMachineMove> tmCatalog)
    {
        var options = new Dictionary<int, string>
        {
            [0] = "0 None",
        };

        foreach (var tm in tmCatalog)
        {
            options.TryAdd(tm.Slot, $"{tm.Slot.ToString(CultureInfo.InvariantCulture)} {tm.Label}");
        }

        foreach (var group in items
            .Where(item => item.Metadata.GroupIndex > 0)
            .GroupBy(item => item.Metadata.GroupIndex)
            .OrderBy(group => group.Key))
        {
            if (options.ContainsKey(group.Key))
            {
                continue;
            }

            var categories = group
                .Select(item => item.Category)
                .Where(category => !string.IsNullOrWhiteSpace(category))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .ToArray();
            var labelSuffix = categories.Length switch
            {
                0 => "Group",
                1 => $"{categories[0]} group",
                _ => "Mixed group",
            };
            options[group.Key] = $"{group.Key.ToString(CultureInfo.InvariantCulture)} {labelSuffix}";
        }

        return options
            .OrderBy(option => option.Key)
            .Select(option => new SvItemEditableFieldOption(option.Key, option.Value))
            .ToArray();
    }

    private static IReadOnlyList<SvItemEditableFieldOption> CreateEnumOptions<TEnum>(string prefix)
        where TEnum : struct, Enum
    {
        return Enum
            .GetValues<TEnum>()
            .Select(value => new SvItemEditableFieldOption(
                Convert.ToInt32(value),
                SvLabels.EnumName(value, prefix)))
            .OrderBy(option => option.Value)
            .ToArray();
    }

    internal static bool CanUseOnPokemon(global::FieldFunctionType functionType)
    {
        return functionType is
            global::FieldFunctionType.FIELDFUNC_RECOVER or
            global::FieldFunctionType.FIELDFUNC_WAZA or
            global::FieldFunctionType.FIELDFUNC_EVOLUTION or
            global::FieldFunctionType.FIELDFUNC_VIDRO or
            global::FieldFunctionType.FIELDFUNC_KINOMI or
            global::FieldFunctionType.FIELDFUNC_FORM_CHANGE;
    }

    private static IReadOnlyList<SvItemEditableFieldOption> CreateIndexedOptions(
        int count,
        Func<int, string> resolveName,
        bool includeNone)
    {
        var firstValue = includeNone ? 0 : 1;
        if (count <= firstValue)
        {
            return includeNone ? [new(0, "0 None")] : [];
        }

        return Enumerable
            .Range(firstValue, count - firstValue)
            .Select(value =>
            {
                var label = value == 0 ? "None" : resolveName(value);
                return new SvItemEditableFieldOption(
                    value,
                    $"{value.ToString(System.Globalization.CultureInfo.InvariantCulture)} {label}");
            })
            .ToArray();
    }
}
