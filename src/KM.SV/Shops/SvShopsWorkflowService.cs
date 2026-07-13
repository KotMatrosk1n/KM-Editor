// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SV.Data;
using KM.SV.Items;
using KM.SV.Workflows;
using System.Globalization;

namespace KM.SV.Shops;

internal sealed class SvShopsWorkflowService
{
    public const string ItemIdField = "itemId";
    public const string AddItemField = "addItem";
    public const string RemoveItemField = "removeItem";
    public const string SetInventoryField = "setInventory";
    public const string SortOrderField = "sortOrder";
    public const string ConditionKindField = "conditionKind";
    public const string ConditionValueField = "conditionValue";
    public const string GymBadgeCountField = "gymBadgeCount";
    public const string MoveIdField = "moveId";
    public const string LpCostField = "lpCost";
    public const string Material1ItemIdField = "material1ItemId";
    public const string Material1CountField = "material1Count";
    public const string Material1DevNoField = "material1DevNo";
    public const string Material2ItemIdField = "material2ItemId";
    public const string Material2CountField = "material2Count";
    public const string Material2DevNoField = "material2DevNo";
    public const string Material3ItemIdField = "material3ItemId";
    public const string Material3CountField = "material3Count";
    public const string Material3DevNoField = "material3DevNo";
    public const string RegionField = "region";
    public const int MinimumItemId = 0;
    public const int MaximumItemId = 65_535;
    public const string SourceRowIdPrefix = "source:";
    public const string NewRowIdPrefix = "new:";

    private const string WorkflowLabel = "Shops";
    private const string WorkflowDescription = "Edit Scarlet/Violet shop inventories, TM Machine recipes, unlocks, and source provenance.";
    private const string RecordRowIdentityPrefix = "row:";

    private static readonly IReadOnlyDictionary<string, ShopLineupDefinition> KnownLineups =
        new Dictionary<string, ShopLineupDefinition>(StringComparer.Ordinal)
        {
            ["shop_00_lineup"] = new("Poke Mart", "Poke Mart", "Badge inventory", "Paldea", "Money"),
            ["shop_delibird_00_lineup1"] = new("Delibird Presents [Branch 1, Battle Items]", "Delibird Presents", "Battle items", "Paldea", "Money"),
            ["shop_delibird_00_lineup2"] = new("Delibird Presents [Branch 1, General Goods]", "Delibird Presents", "General goods", "Paldea", "Money"),
            ["shop_delibird_00_lineup3"] = new("Delibird Presents [Branch 1, Poke Balls]", "Delibird Presents", "Poke Balls", "Paldea", "Money"),
            ["shop_delibird_01_lineup1"] = new("Delibird Presents [Branch 2, Battle Items]", "Delibird Presents", "Battle items", "Paldea", "Money"),
            ["shop_delibird_01_lineup2"] = new("Delibird Presents [Branch 2, General Goods]", "Delibird Presents", "General goods", "Paldea", "Money"),
            ["shop_delibird_01_lineup3"] = new("Delibird Presents [Branch 2, Poke Balls]", "Delibird Presents", "Poke Balls", "Paldea", "Money"),
            ["shop_delibird_02_lineup1"] = new("Delibird Presents [Branch 3, Battle Items]", "Delibird Presents", "Battle items", "Paldea", "Money"),
            ["shop_delibird_02_lineup2"] = new("Delibird Presents [Branch 3, General Goods]", "Delibird Presents", "General goods", "Paldea", "Money"),
            ["shop_delibird_02_lineup3"] = new("Delibird Presents [Branch 3, Poke Balls]", "Delibird Presents", "Poke Balls", "Paldea", "Money"),
            ["shop_kusuriya_00_lineup"] = new("Chansey Supply", "Chansey Supply", "Medicine and training items", "Paldea", "Money"),
            ["shop_souzai_00_lineup"] = new("Deli Cioso", "Ingredient Shop", "Ingredients", "Paldea", "Money"),
            ["shop_kanzume_00_lineup"] = new("Sure Cans", "Ingredient Shop", "Ingredients", "Paldea", "Money"),
            ["shop_pan_00_lineup"] = new("Artisan Bakery", "Ingredient Shop", "Ingredients", "Paldea", "Money"),
            ["shop_suupaa_00_lineup"] = new("Aquiesta Supermarket", "Ingredient Shop", "Ingredients", "Paldea", "Money"),
            ["shop_koubai_00_lineup"] = new("School Store", "School Store", "Academy inventory", "Paldea", "Money"),
            ["shop_picnic_00_lineup"] = new("Picnic-Knacks [Branch 1]", "Picnic-Knacks", "Picnic goods", "Paldea", "Money"),
            ["shop_picnic_01_lineup"] = new("Picnic-Knacks [Branch 2]", "Picnic-Knacks", "Picnic goods", "Paldea", "Money"),
            ["shop_picnic_02_lineup"] = new("Picnic-Knacks [Branch 3]", "Picnic-Knacks", "Picnic goods", "Paldea", "Money"),
            ["shop_syouten_lineup"] = new("Kitakami General Store", "General Store", "Kitakami inventory", "Kitakami", "Money"),
            ["shop_bbkoubai_lineup"] = new("Blueberry Academy School Store", "School Store", "Blueberry Academy inventory", "Blueberry", "Money"),
            ["shop_bbzihanki_lineup"] = new("Blueberry Academy Vending Machine", "Vending Machine", "Blueberry Academy inventory", "Blueberry", "Money"),
        };

    private static readonly IReadOnlyList<SvShopEditableFieldOption> ConditionKindOptions =
    [
        new((int)CondEnum.NONE, "None", string.Empty, 0),
        new((int)CondEnum.SYSTEM_FLAG, "System flag", string.Empty, 0),
        new((int)CondEnum.SCENARIO, "Scenario", string.Empty, 0),
        new((int)CondEnum.GYMBADGENUM, "Gym badge count", string.Empty, 0),
    ];

    private static readonly IReadOnlyList<SvShopEditableFieldOption> RegionOptions =
    [
        new((int)AddRegion.NONE, "None", string.Empty, 0),
        new((int)AddRegion.TITAN, "Paldea", string.Empty, 0),
        new((int)AddRegion.SUDACHI1, "Kitakami", string.Empty, 0),
        new((int)AddRegion.SUDACHI2, "Blueberry", string.Empty, 0),
    ];

    private readonly SvWorkflowFileSource fileSource;
    private readonly SvItemsWorkflowService itemsWorkflowService;

    public SvShopsWorkflowService(
        SvWorkflowFileSource? fileSource = null,
        SvItemsWorkflowService? itemsWorkflowService = null)
    {
        this.fileSource = fileSource ?? new SvWorkflowFileSource();
        this.itemsWorkflowService = itemsWorkflowService ?? new SvItemsWorkflowService(this.fileSource);
    }

    public SvWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return SvWorkflowSupport.CreateSummary(
            project,
            SvWorkflowIds.Shops,
            WorkflowLabel,
            WorkflowDescription);
    }

    public SvShopsWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var diagnostics = new List<ValidationDiagnostic>();
        var shops = Array.Empty<SvShopRecord>();
        var sourceCount = 0;
        var labels = SvTextLabelLookup.None();
        var itemRecords = Array.Empty<SvItemRecord>();

        try
        {
            labels = SvTextLabelLookup.Load(project, fileSource, diagnostics, project.Paths);
            var itemWorkflow = itemsWorkflowService.Load(project);
            itemRecords = itemWorkflow.Items.ToArray();
            var itemLookup = itemRecords
                .GroupBy(item => item.ItemId)
                .ToDictionary(group => group.Key, group => group.First());
            var friendlySource = fileSource.Read(project, SvDataPaths.FriendlyShopLineupDataArray);
            var tmSource = fileSource.Read(project, SvDataPaths.ShopWazaMachineDataArray);
            sourceCount = 2;
            shops = LoadFriendlyShopRecords(friendlySource, itemLookup, diagnostics)
                .Concat(LoadTechnicalMachineRecords(tmSource, labels, itemLookup, diagnostics))
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(SvWorkflowSupport.Error(
                $"Shops could not be loaded: {exception.Message}",
                $"romfs/{SvDataPaths.FriendlyShopLineupDataArray}"));
        }

        var summary = SvWorkflowSupport.CreateSummary(
            project,
            SvWorkflowIds.Shops,
            WorkflowLabel,
            WorkflowDescription,
            diagnostics.Count == 0 ? null : diagnostics);

        return new SvShopsWorkflow(
            summary,
            shops,
            CreateEditableFields(labels, itemRecords, shops),
            new SvShopsWorkflowStats(shops.Length, shops.Sum(shop => shop.Inventory.Count), sourceCount),
            diagnostics);
    }

    public static string CreateInventoryRecordId(string shopId, int slot, int? sourceIndex = null)
    {
        return sourceIndex is >= 0
            ? CreateInventoryRecordId(shopId, slot, CreateSourceRowId(sourceIndex.Value))
            : string.Create(CultureInfo.InvariantCulture, $"{shopId}#{slot}");
    }

    public static string CreateInventoryRecordId(string shopId, int slot, string rowId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rowId);
        if (!IsValidRowId(rowId))
        {
            throw new ArgumentException("Shop row identity is not valid.", nameof(rowId));
        }

        var positionalId = string.Create(CultureInfo.InvariantCulture, $"{shopId}#{slot}");
        return string.Create(CultureInfo.InvariantCulture, $"{positionalId}#{RecordRowIdentityPrefix}{rowId}");
    }

    public static string CreateSourceRowId(int sourceIndex) =>
        string.Create(CultureInfo.InvariantCulture, $"{SourceRowIdPrefix}{sourceIndex}");

    public static bool IsValidRowId(string? rowId) =>
        TryParseRowNumber(rowId, SourceRowIdPrefix, allowZero: true, out _)
        || TryParseRowNumber(rowId, NewRowIdPrefix, allowZero: false, out _);

    public static bool TryParseSourceRowId(string? rowId, out int sourceIndex) =>
        TryParseRowNumber(rowId, SourceRowIdPrefix, allowZero: true, out sourceIndex);

    public static bool TryParseInventoryRecordId(string? recordId, out string shopId, out int slot) =>
        TryParseInventoryRecordId(recordId, out shopId, out slot, out _);

    public static bool TryParseInventoryRecordId(
        string? recordId,
        out string shopId,
        out int slot,
        out int? sourceIndex)
    {
        var parsed = TryParseInventoryRecordRowId(recordId, out shopId, out slot, out var rowId);
        sourceIndex = TryParseSourceRowId(rowId, out var parsedSourceIndex)
            ? parsedSourceIndex
            : null;
        return parsed;
    }

    public static bool TryParseInventoryRecordRowId(
        string? recordId,
        out string shopId,
        out int slot,
        out string? rowId)
    {
        shopId = string.Empty;
        slot = 0;
        rowId = null;

        if (string.IsNullOrEmpty(recordId))
        {
            return false;
        }

        var positionalId = recordId;
        var identitySeparatorIndex = recordId.LastIndexOf('#');
        if (identitySeparatorIndex > 0)
        {
            var identity = recordId[(identitySeparatorIndex + 1)..];
            if (identity.StartsWith(RecordRowIdentityPrefix, StringComparison.Ordinal))
            {
                var parsedRowId = identity[RecordRowIdentityPrefix.Length..];
                if (!IsValidRowId(parsedRowId))
                {
                    return false;
                }

                rowId = parsedRowId;
                positionalId = recordId[..identitySeparatorIndex];
            }
        }

        var separatorIndex = positionalId.LastIndexOf('#');
        if (separatorIndex <= 0 || separatorIndex >= positionalId.Length - 1)
        {
            return false;
        }

        shopId = positionalId[..separatorIndex];
        return int.TryParse(positionalId[(separatorIndex + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out slot)
            && slot >= 1;
    }

    private static bool TryParseRowNumber(
        string? rowId,
        string prefix,
        bool allowZero,
        out int value)
    {
        value = 0;
        return rowId?.StartsWith(prefix, StringComparison.Ordinal) == true
            && int.TryParse(rowId[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out value)
            && (allowZero ? value >= 0 : value >= 1);
    }

    public static bool IsFriendlyShopId(string shopId, out string lineupId)
    {
        const string prefix = "lineup:";
        if (shopId.StartsWith(prefix, StringComparison.Ordinal))
        {
            lineupId = shopId[prefix.Length..];
            return !string.IsNullOrEmpty(lineupId);
        }

        lineupId = string.Empty;
        return false;
    }

    public static bool IsTechnicalMachineShopId(string shopId, out AddRegion region)
    {
        const string prefix = "tm:";
        region = AddRegion.NONE;
        if (!shopId.StartsWith(prefix, StringComparison.Ordinal)
            || !int.TryParse(shopId[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            || !Enum.IsDefined((AddRegion)value))
        {
            return false;
        }

        region = (AddRegion)value;
        return true;
    }

    public static bool IsInventoryActionField(string field) =>
        field is ItemIdField or AddItemField or RemoveItemField or SetInventoryField;

    public static bool IsFriendlyRowField(string field) =>
        field is ItemIdField or SortOrderField or ConditionKindField or ConditionValueField or GymBadgeCountField;

    public static bool IsTechnicalMachineRowField(string field) =>
        field is
            ItemIdField or
            MoveIdField or
            LpCostField or
            ConditionKindField or
            ConditionValueField or
            Material1ItemIdField or
            Material1CountField or
            Material1DevNoField or
            Material2ItemIdField or
            Material2CountField or
            Material2DevNoField or
            Material3ItemIdField or
            Material3CountField or
            Material3DevNoField or
            RegionField;

    public static bool IsTextField(string field) => field == ConditionValueField;

    public static string FormatConditionKind(CondEnum condition) =>
        ConditionKindOptions.FirstOrDefault(option => option.Value == (int)condition)?.Label
            ?? SvLabels.EnumName(condition);

    public static string FormatRegion(AddRegion region) =>
        RegionOptions.FirstOrDefault(option => option.Value == (int)region)?.Label
            ?? SvLabels.EnumName(region);

    private static IEnumerable<SvShopRecord> LoadFriendlyShopRecords(
        SvWorkflowFile source,
        IReadOnlyDictionary<int, SvItemRecord> itemLookup,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var rows = ReadFriendlyRows(source.Bytes);
        var unresolvedItemIds = new HashSet<int>();
        foreach (var group in rows.GroupBy(row => row.LineupId, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var definition = KnownLineups.GetValueOrDefault(group.Key)
                ?? new ShopLineupDefinition(
                    FormatFallbackLineupName(group.Key),
                    "Shop",
                    group.Key,
                    "Scarlet/Violet",
                    "Money");
            var inventory = group
                .OrderBy(row => row.SortNum)
                .ThenBy(row => row.SourceIndex)
                .Select((row, index) => ToFriendlyInventoryRecord(index + 1, row, itemLookup, unresolvedItemIds))
                .ToArray();
            var shopId = $"lineup:{group.Key}";

            yield return new SvShopRecord(
                shopId,
                definition.Name,
                definition.Kind,
                definition.InventoryLabel,
                1,
                1,
                group.Key,
                FormatInventorySummary(inventory),
                definition.Location,
                definition.Currency,
                inventory,
                new SvShopProvenance(source.RelativePath, source.SourceLayer, source.FileState),
                CanEditInventoryOrder: true);
        }

        if (itemLookup.Count > 0)
        {
            foreach (var itemId in unresolvedItemIds.Order())
            {
                diagnostics.Add(SvWorkflowSupport.Warning(
                    $"Shop inventory references item ID {itemId}, but Items did not resolve that item.",
                    $"romfs/{source.RelativePath}"));
            }
        }
    }

    private static IEnumerable<SvShopRecord> LoadTechnicalMachineRecords(
        SvWorkflowFile source,
        SvTextLabelLookup labels,
        IReadOnlyDictionary<int, SvItemRecord> itemLookup,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var rows = ReadTechnicalMachineRows(source.Bytes);
        var unresolvedItemIds = new HashSet<int>();
        foreach (var group in rows.GroupBy(row => row.Region).OrderBy(group => group.Key))
        {
            var regionLabel = FormatRegion(group.Key);
            var inventory = group
                .OrderBy(row => row.WazaItemId)
                .ThenBy(row => row.SourceIndex)
                .Select((row, index) => ToTechnicalMachineInventoryRecord(index + 1, row, labels, itemLookup, unresolvedItemIds))
                .ToArray();

            yield return new SvShopRecord(
                $"tm:{((int)group.Key).ToString(CultureInfo.InvariantCulture)}",
                $"TM Machine [{regionLabel}]",
                "TM Machine",
                regionLabel,
                (int)group.Key + 1,
                4,
                $"region:{((int)group.Key).ToString(CultureInfo.InvariantCulture)}",
                FormatInventorySummary(inventory),
                regionLabel,
                "LP",
                inventory,
                new SvShopProvenance(source.RelativePath, source.SourceLayer, source.FileState),
                CanEditInventoryOrder: false);
        }

        if (itemLookup.Count > 0)
        {
            foreach (var itemId in unresolvedItemIds.Order())
            {
                diagnostics.Add(SvWorkflowSupport.Warning(
                    $"TM Machine references item ID {itemId}, but Items did not resolve that item.",
                    $"romfs/{source.RelativePath}"));
            }
        }
    }

    private static SvShopInventoryRecord ToFriendlyInventoryRecord(
        int slot,
        FriendlyShopRow row,
        IReadOnlyDictionary<int, SvItemRecord> itemLookup,
        ISet<int> unresolvedItemIds)
    {
        var item = ResolveItem(row.ItemId, itemLookup, unresolvedItemIds);
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ItemIdField] = row.ItemId.ToString(CultureInfo.InvariantCulture),
            [SortOrderField] = row.SortNum.ToString(CultureInfo.InvariantCulture),
            [ConditionKindField] = ((int)row.ConditionKind).ToString(CultureInfo.InvariantCulture),
            [ConditionValueField] = row.ConditionValue,
            [GymBadgeCountField] = row.GymBadgeNum.ToString(CultureInfo.InvariantCulture),
        };
        var displays = new Dictionary<string, string>(values, StringComparer.Ordinal)
        {
            [ConditionKindField] = FormatConditionKind(row.ConditionKind),
        };

        return new SvShopInventoryRecord(
            slot,
            row.ItemId,
            item.Name,
            item.Price,
            item.IsKnown,
            StockLimit: null,
            values,
            displays,
            [SortOrderField, ConditionKindField, ConditionValueField, GymBadgeCountField],
            PriceField: null,
            CanEditPrice: true,
            SourceIndex: row.SourceIndex,
            RowId: CreateSourceRowId(row.SourceIndex));
    }

    private static SvShopInventoryRecord ToTechnicalMachineInventoryRecord(
        int slot,
        TechnicalMachineRow row,
        SvTextLabelLookup labels,
        IReadOnlyDictionary<int, SvItemRecord> itemLookup,
        ISet<int> unresolvedItemIds)
    {
        var item = ResolveItem(row.WazaItemId, itemLookup, unresolvedItemIds);
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ItemIdField] = row.WazaItemId.ToString(CultureInfo.InvariantCulture),
            [MoveIdField] = row.MoveId.ToString(CultureInfo.InvariantCulture),
            [LpCostField] = row.LpCost.ToString(CultureInfo.InvariantCulture),
            [ConditionKindField] = ((int)row.ConditionKind).ToString(CultureInfo.InvariantCulture),
            [ConditionValueField] = row.ConditionValue,
            [Material1ItemIdField] = row.Material1ItemId.ToString(CultureInfo.InvariantCulture),
            [Material1CountField] = row.Material1Count.ToString(CultureInfo.InvariantCulture),
            [Material1DevNoField] = row.Material1DevNo.ToString(CultureInfo.InvariantCulture),
            [Material2ItemIdField] = row.Material2ItemId.ToString(CultureInfo.InvariantCulture),
            [Material2CountField] = row.Material2Count.ToString(CultureInfo.InvariantCulture),
            [Material2DevNoField] = row.Material2DevNo.ToString(CultureInfo.InvariantCulture),
            [Material3ItemIdField] = row.Material3ItemId.ToString(CultureInfo.InvariantCulture),
            [Material3CountField] = row.Material3Count.ToString(CultureInfo.InvariantCulture),
            [Material3DevNoField] = row.Material3DevNo.ToString(CultureInfo.InvariantCulture),
            [RegionField] = ((int)row.Region).ToString(CultureInfo.InvariantCulture),
        };
        var displays = new Dictionary<string, string>(values, StringComparer.Ordinal)
        {
            [MoveIdField] = row.MoveId == 0 ? "None" : labels.Move(row.MoveId),
            [ConditionKindField] = FormatConditionKind(row.ConditionKind),
            [Material1ItemIdField] = FormatItemName(row.Material1ItemId, itemLookup),
            [Material2ItemIdField] = FormatItemName(row.Material2ItemId, itemLookup),
            [Material3ItemIdField] = FormatItemName(row.Material3ItemId, itemLookup),
            [RegionField] = FormatRegion(row.Region),
        };

        return new SvShopInventoryRecord(
            slot,
            row.WazaItemId,
            item.Name,
            row.LpCost,
            item.IsKnown,
            StockLimit: null,
            values,
            displays,
            [
                MoveIdField,
                LpCostField,
                ConditionKindField,
                ConditionValueField,
                Material1ItemIdField,
                Material1CountField,
                Material1DevNoField,
                Material2ItemIdField,
                Material2CountField,
                Material2DevNoField,
                Material3ItemIdField,
                Material3CountField,
                Material3DevNoField,
                RegionField,
            ],
            PriceField: LpCostField,
            CanEditPrice: true,
            SourceIndex: row.SourceIndex,
            RowId: CreateSourceRowId(row.SourceIndex));
    }

    private static ResolvedItem ResolveItem(
        int itemId,
        IReadOnlyDictionary<int, SvItemRecord> itemLookup,
        ISet<int> unresolvedItemIds)
    {
        if (itemLookup.TryGetValue(itemId, out var item))
        {
            return new ResolvedItem(item.Name, item.BuyPrice, IsKnown: true);
        }

        if (itemId > 0)
        {
            unresolvedItemIds.Add(itemId);
        }

        return new ResolvedItem(itemId == 0 ? "None" : $"Item {itemId}", Price: 0, IsKnown: false);
    }

    private static string FormatItemName(
        int itemId,
        IReadOnlyDictionary<int, SvItemRecord> itemLookup)
    {
        return itemId == 0
            ? "None"
            : itemLookup.TryGetValue(itemId, out var item)
                ? item.Name
                : $"Item {itemId.ToString(CultureInfo.InvariantCulture)}";
    }

    public static string FormatInventorySummary(IReadOnlyList<SvShopInventoryRecord> inventory)
    {
        if (inventory.Count == 0)
        {
            return "Empty";
        }

        var preview = string.Join(", ", inventory.Take(3).Select(item => item.ItemName));
        return inventory.Count > 3
            ? string.Create(CultureInfo.InvariantCulture, $"{preview}, +{inventory.Count - 3} more")
            : preview;
    }

    private static IReadOnlyList<SvShopEditableField> CreateEditableFields(
        SvTextLabelLookup labels,
        IReadOnlyList<SvItemRecord> items,
        IReadOnlyList<SvShopRecord> shops)
    {
        var itemOptions = CreateItemOptions(items, shops);
        var moveOptions = CreateIndexedOptions(labels.MoveNameCount, labels.Move, includeNone: true);
        return
        [
            CreateField(ItemIdField, "Item", MinimumItemId, MaximumItemId, itemOptions),
            CreateField(SortOrderField, "Sort order", 0, int.MaxValue),
            CreateField(ConditionKindField, "Unlock condition", 0, 3, ConditionKindOptions),
            CreateField(ConditionValueField, "Condition value", null, null, valueKind: "text"),
            CreateField(GymBadgeCountField, "Gym badges", 0, 8),
            CreateField(MoveIdField, "Move", 0, moveOptions.Count > 0 ? moveOptions.Max(option => option.Value) : ushort.MaxValue, moveOptions),
            CreateField(LpCostField, "LP cost", 0, int.MaxValue),
            CreateField(Material1ItemIdField, "Material 1 item", MinimumItemId, MaximumItemId, itemOptions),
            CreateField(Material1CountField, "Material 1 count", 0, int.MaxValue),
            CreateField(Material1DevNoField, "Material 1 DevNo", 0, int.MaxValue),
            CreateField(Material2ItemIdField, "Material 2 item", MinimumItemId, MaximumItemId, itemOptions),
            CreateField(Material2CountField, "Material 2 count", 0, int.MaxValue),
            CreateField(Material2DevNoField, "Material 2 DevNo", 0, int.MaxValue),
            CreateField(Material3ItemIdField, "Material 3 item", MinimumItemId, MaximumItemId, itemOptions),
            CreateField(Material3CountField, "Material 3 count", 0, int.MaxValue),
            CreateField(Material3DevNoField, "Material 3 DevNo", 0, int.MaxValue),
            CreateField(RegionField, "Region", 0, 3, RegionOptions),
        ];
    }

    private static IReadOnlyList<SvShopEditableFieldOption> CreateItemOptions(
        IReadOnlyList<SvItemRecord> items,
        IReadOnlyList<SvShopRecord> shops)
    {
        var options = new Dictionary<int, SvShopEditableFieldOption>
        {
            [0] = new(0, "0 None", "None", 0),
        };

        foreach (var item in items)
        {
            options.TryAdd(
                item.ItemId,
                new SvShopEditableFieldOption(
                    item.ItemId,
                    $"{item.ItemId.ToString(CultureInfo.InvariantCulture)} {item.Name}",
                    item.Name,
                    item.BuyPrice));
        }

        foreach (var item in shops.SelectMany(shop => shop.Inventory))
        {
            options.TryAdd(
                item.ItemId,
                new SvShopEditableFieldOption(
                    item.ItemId,
                    $"{item.ItemId.ToString(CultureInfo.InvariantCulture)} {item.ItemName}",
                    item.ItemName,
                    item.Price));
        }

        return options.Values.OrderBy(option => option.Value).ToArray();
    }

    private static IReadOnlyList<SvShopEditableFieldOption> CreateIndexedOptions(
        int count,
        Func<int, string> resolveName,
        bool includeNone)
    {
        var firstValue = includeNone ? 0 : 1;
        if (count <= firstValue)
        {
            return includeNone ? [new(0, "0 None", "None", 0)] : [];
        }

        return Enumerable
            .Range(firstValue, count - firstValue)
            .Select(value =>
            {
                var label = value == 0 ? "None" : resolveName(value);
                return new SvShopEditableFieldOption(
                    value,
                    $"{value.ToString(CultureInfo.InvariantCulture)} {label}",
                    label,
                    Price: 0);
            })
            .ToArray();
    }

    private static SvShopEditableField CreateField(
        string field,
        string label,
        int? minimumValue,
        int? maximumValue,
        IReadOnlyList<SvShopEditableFieldOption>? options = null,
        string valueKind = "integer")
    {
        return new SvShopEditableField(
            field,
            label,
            valueKind,
            minimumValue,
            maximumValue,
            options ?? Array.Empty<SvShopEditableFieldOption>());
    }

    public static IReadOnlyList<FriendlyShopRow> ReadFriendlyRows(byte[] bytes)
    {
        var table = global::LineupDataArray.GetRootAsLineupDataArray(new ByteBuffer(bytes));
        var rows = new List<FriendlyShopRow>();
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var row = table.Values(index);
            if (row is null)
            {
                continue;
            }

            rows.Add(FriendlyShopRow.From(index, row.Value));
        }

        return rows;
    }

    public static byte[] WriteFriendlyRows(IReadOnlyList<FriendlyShopRow> rows)
    {
        var builder = new FlatBufferBuilder(1024);
        var offsets = rows
            .Select(row => row.Write(builder))
            .ToArray();
        var vector = global::LineupDataArray.CreateValuesVector(builder, offsets);
        var root = global::LineupDataArray.CreateLineupDataArray(builder, vector);
        global::LineupDataArray.FinishLineupDataArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    public static IReadOnlyList<TechnicalMachineRow> ReadTechnicalMachineRows(byte[] bytes)
    {
        var table = global::ShopWazamachineDataArray.GetRootAsShopWazamachineDataArray(new ByteBuffer(bytes));
        var rows = new List<TechnicalMachineRow>();
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var row = table.Values(index);
            if (row is null)
            {
                continue;
            }

            rows.Add(TechnicalMachineRow.From(index, row.Value));
        }

        return rows;
    }

    public static byte[] WriteTechnicalMachineRows(IReadOnlyList<TechnicalMachineRow> rows)
    {
        var builder = new FlatBufferBuilder(1024);
        var offsets = rows
            .Select(row => row.Write(builder))
            .ToArray();
        var vector = global::ShopWazamachineDataArray.CreateValuesVector(builder, offsets);
        var root = global::ShopWazamachineDataArray.CreateShopWazamachineDataArray(builder, vector);
        global::ShopWazamachineDataArray.FinishShopWazamachineDataArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static string FormatFallbackLineupName(string lineupId) =>
        string.Create(CultureInfo.InvariantCulture, $"Shop Lineup [{lineupId}]");

    private sealed record ShopLineupDefinition(
        string Name,
        string Kind,
        string InventoryLabel,
        string Location,
        string Currency);

    private sealed record ResolvedItem(
        string Name,
        int Price,
        bool IsKnown);

    public sealed record FriendlyShopRow(
        int SourceIndex,
        string LineupId,
        int SortNum,
        int ItemId,
        CondEnum ConditionKind,
        string ConditionValue,
        int GymBadgeNum)
    {
        public string RowId { get; init; } = CreateSourceRowId(SourceIndex);

        public static FriendlyShopRow From(int sourceIndex, global::LineupData row) =>
            new(
                sourceIndex,
                row.Lineupid ?? string.Empty,
                row.Sortnum,
                (int)row.Item,
                row.ItemCondkind,
                row.ItemCondvalue ?? string.Empty,
                row.GymBadgeNum);

        public Offset<global::LineupData> Write(FlatBufferBuilder builder)
        {
            var lineupOffset = builder.CreateString(LineupId);
            var conditionValueOffset = string.IsNullOrEmpty(ConditionValue)
                ? default
                : builder.CreateString(ConditionValue);
            return global::LineupData.CreateLineupData(
                builder,
                lineupOffset,
                SortNum,
                (ItemID)ItemId,
                ConditionKind,
                conditionValueOffset,
                GymBadgeNum);
        }
    }

    public sealed record TechnicalMachineRow(
        int SourceIndex,
        int MoveId,
        int WazaItemId,
        int LpCost,
        CondEnum ConditionKind,
        string ConditionValue,
        int Material1ItemId,
        int Material1Count,
        int Material1DevNo,
        int Material2ItemId,
        int Material2Count,
        int Material2DevNo,
        int Material3ItemId,
        int Material3Count,
        int Material3DevNo,
        AddRegion Region)
    {
        public static TechnicalMachineRow From(int sourceIndex, global::ShopWazamachineData row) =>
            new(
                sourceIndex,
                row.WazaNo,
                (int)row.WazaItemID,
                row.LP,
                row.Cond,
                row.CondValue ?? string.Empty,
                (int)row.Item01,
                row.ItemNum01,
                row.DevNo01,
                (int)row.Item02,
                row.ItemNum02,
                row.DevNo02,
                (int)row.Item03,
                row.ItemNum03,
                row.DevNo03,
                row.AddRegion);

        public Offset<global::ShopWazamachineData> Write(FlatBufferBuilder builder)
        {
            var conditionValueOffset = string.IsNullOrEmpty(ConditionValue)
                ? default
                : builder.CreateString(ConditionValue);
            return global::ShopWazamachineData.CreateShopWazamachineData(
                builder,
                MoveId,
                (ItemID)WazaItemId,
                LpCost,
                ConditionKind,
                conditionValueOffset,
                (ItemID)Material1ItemId,
                Material1Count,
                Material1DevNo,
                (ItemID)Material2ItemId,
                Material2Count,
                Material2DevNo,
                (ItemID)Material3ItemId,
                Material3Count,
                Material3DevNo,
                Region);
        }
    }
}
