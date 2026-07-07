// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.ZA.Generated.GameData;
using KM.ZA.Data;
using KM.ZA.Items;
using KM.ZA.Workflows;
using System.Globalization;

namespace KM.ZA.Shops;

internal sealed class ZaShopsWorkflowService
{
    public const string ItemIdField = "itemId";
    public const string AddItemField = "addItem";
    public const string RemoveItemField = "removeItem";
    public const string SetInventoryField = "setInventory";
    public const string DisplayIndexField = "displayIndex";
    public const string ConditionKindField = "zaConditionKind";
    public const string ConditionComparisonField = "zaConditionComparison";
    public const string ConditionArgumentsField = "zaConditionArguments";
    public const int MinimumItemId = 0;
    public const int MaximumItemId = 65_535;

    private const string WorkflowLabel = "Shops";
    private const string WorkflowDescription = "Edit Pokemon Legends Z-A shop inventories, display order, currencies, and unlock conditions.";

    private static readonly IReadOnlyList<string> RowSupportedFields =
    [
        DisplayIndexField,
        ConditionKindField,
        ConditionComparisonField,
        ConditionArgumentsField,
    ];

    private static readonly IReadOnlyList<ConditionKindDefinition> ConditionKinds =
    [
        new(0, "force_condition", "Force condition"),
        new(1, "phase_condition", "Scenario phase"),
        new(2, "flag_condition", "Flag condition"),
        new(3, "work_condition", "Work value condition"),
        new(4, "have_item_whole_condition", "Have item condition"),
    ];

    private static readonly IReadOnlyList<ConditionComparisonDefinition> ConditionComparisons =
    [
        new(0, "Always / present"),
        new(1, "Equal to"),
        new(2, "Not equal to"),
        new(3, "Less than"),
        new(4, "Less than or equal"),
        new(5, "Greater than or equal"),
        new(6, "Greater than"),
    ];

    private static readonly IReadOnlyList<ZaShopEditableFieldOption> ConditionKindOptions =
        ConditionKinds
            .Select(kind => new ZaShopEditableFieldOption(kind.Value, $"{kind.Value.ToString(CultureInfo.InvariantCulture)} {kind.Label}", kind.Label, 0))
            .ToArray();

    private static readonly IReadOnlyList<ZaShopEditableFieldOption> ConditionComparisonOptions =
        ConditionComparisons
            .Select(comparison => new ZaShopEditableFieldOption(
                comparison.Value,
                $"{comparison.Value.ToString(CultureInfo.InvariantCulture)} {comparison.Label}",
                comparison.Label,
                0))
            .ToArray();

    private readonly ZaWorkflowFileSource fileSource;
    private readonly ZaItemsWorkflowService itemsWorkflowService;

    public ZaShopsWorkflowService(
        ZaWorkflowFileSource? fileSource = null,
        ZaItemsWorkflowService? itemsWorkflowService = null)
    {
        this.fileSource = fileSource ?? new ZaWorkflowFileSource();
        this.itemsWorkflowService = itemsWorkflowService ?? new ZaItemsWorkflowService(this.fileSource);
    }

    public ZaWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return ZaWorkflowSupport.CreateSummary(
            project,
            ZaWorkflowIds.Shops,
            WorkflowLabel,
            WorkflowDescription);
    }

    public ZaShopsWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var diagnostics = new List<ValidationDiagnostic>();
        var shops = Array.Empty<ZaShopRecord>();
        var itemRecords = Array.Empty<ZaItemRecord>();
        var sourceCount = 0;

        try
        {
            var itemWorkflow = itemsWorkflowService.Load(project);
            itemRecords = itemWorkflow.Items.ToArray();
            var itemLookup = itemRecords
                .GroupBy(item => item.ItemId)
                .ToDictionary(group => group.Key, group => group.First());

            var shopSource = fileSource.Read(project, ZaDataPaths.ShopItemArray);
            var lineupSource = fileSource.Read(project, ZaDataPaths.ShopItemLineupArray);
            sourceCount = 2;
            shops = LoadShopRecords(
                    ReadShopRows(shopSource.Bytes),
                    ReadLineupRows(lineupSource.Bytes),
                    lineupSource,
                    itemLookup,
                    diagnostics)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(ZaWorkflowSupport.Error(
                $"Shops could not be loaded: {exception.Message}",
                $"romfs/{ZaDataPaths.ShopItemLineupArray}"));
        }

        var summary = ZaWorkflowSupport.CreateSummary(
            project,
            ZaWorkflowIds.Shops,
            WorkflowLabel,
            WorkflowDescription,
            diagnostics.Count == 0 ? null : diagnostics);

        return new ZaShopsWorkflow(
            summary,
            shops,
            CreateEditableFields(itemRecords, shops),
            new ZaShopsWorkflowStats(shops.Length, shops.Sum(shop => shop.Inventory.Count), sourceCount),
            diagnostics)
        {
            KnownItemIds = itemRecords.Select(item => item.ItemId).ToHashSet(),
        };
    }

    public static string CreateInventoryRecordId(string shopId, int slot) =>
        string.Create(CultureInfo.InvariantCulture, $"{shopId}#{slot}");

    public static bool TryParseInventoryRecordId(string? recordId, out string shopId, out int slot)
    {
        shopId = string.Empty;
        slot = 0;

        var separatorIndex = recordId?.LastIndexOf('#') ?? -1;
        if (separatorIndex <= 0 || separatorIndex >= recordId!.Length - 1)
        {
            return false;
        }

        shopId = recordId[..separatorIndex];
        return int.TryParse(recordId[(separatorIndex + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out slot)
            && slot >= 1;
    }

    public static bool TryGetMasterShopId(string shopId, out string masterShopId)
    {
        const string prefix = "shop:";
        if (shopId.StartsWith(prefix, StringComparison.Ordinal) && shopId.Length > prefix.Length)
        {
            masterShopId = shopId[prefix.Length..];
            return true;
        }

        masterShopId = string.Empty;
        return false;
    }

    public static bool TryGetLineupShopId(string shopId, out string lineupId)
    {
        const string prefix = "lineup:";
        if (shopId.StartsWith(prefix, StringComparison.Ordinal) && shopId.Length > prefix.Length)
        {
            lineupId = shopId[prefix.Length..];
            return true;
        }

        lineupId = string.Empty;
        return false;
    }

    public static bool IsInventoryActionField(string field) =>
        field is ItemIdField or AddItemField or RemoveItemField or SetInventoryField;

    public static bool IsEditableRowField(string field) =>
        field is ItemIdField or DisplayIndexField or ConditionKindField or ConditionComparisonField or ConditionArgumentsField;

    public static bool IsTextField(string field) => field == ConditionArgumentsField;

    public static string FormatConditionKind(string condition) =>
        ConditionKinds.FirstOrDefault(kind => string.Equals(kind.Token, condition, StringComparison.Ordinal))?.Label
            ?? FormatIdentifier(condition);

    public static string FormatInventorySummary(IReadOnlyList<ZaShopInventoryRecord> inventory)
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

    public static IReadOnlyList<ShopMasterRow> ReadShopRows(byte[] bytes)
    {
        var table = ZaShopDataArray.GetRootAsZaShopDataArray(new ByteBuffer(bytes));
        var rows = new List<ShopMasterRow>();
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var row = table.Values(index);
            if (row is null)
            {
                continue;
            }

            rows.Add(ShopMasterRow.From(index, row.Value));
        }

        return rows;
    }

    public static IReadOnlyList<ShopLineupRow> ReadLineupRows(byte[] bytes)
    {
        var table = ZaShopLineupArray.GetRootAsZaShopLineupArray(new ByteBuffer(bytes));
        var rows = new List<ShopLineupRow>();
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var row = table.Values(index);
            if (row is null)
            {
                continue;
            }

            rows.Add(ShopLineupRow.From(index, row.Value));
        }

        return rows;
    }

    public static byte[] WriteLineupRows(IReadOnlyList<ShopLineupRow> rows)
    {
        var builder = new FlatBufferBuilder(1024);
        var offsets = rows.Select(row => row.Write(builder)).ToArray();
        var vector = ZaShopLineupArray.CreateValuesVector(builder, offsets);
        var root = ZaShopLineupArray.CreateZaShopLineupArray(builder, vector);
        ZaShopLineupArray.FinishZaShopLineupArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    internal static int ConditionTokenToValue(string? condition)
    {
        return ConditionKinds.FirstOrDefault(kind => string.Equals(kind.Token, condition, StringComparison.Ordinal))?.Value ?? 0;
    }

    internal static string ConditionValueToToken(int value)
    {
        return ConditionKinds.FirstOrDefault(kind => kind.Value == value)?.Token ?? ConditionKinds[0].Token;
    }

    private static IEnumerable<ZaShopRecord> LoadShopRecords(
        IReadOnlyList<ShopMasterRow> masterRows,
        IReadOnlyList<ShopLineupRow> lineupRows,
        ZaWorkflowFile lineupSource,
        IReadOnlyDictionary<int, ZaItemRecord> itemLookup,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var lineupLookup = lineupRows
            .GroupBy(lineup => lineup.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var unresolvedItemIds = new HashSet<int>();
        var usedLineups = new HashSet<string>(StringComparer.Ordinal);
        var totalCount = Math.Max(masterRows.Count, lineupRows.Count);
        var inventoryIndex = 1;

        foreach (var master in masterRows.OrderBy(row => row.SourceIndex))
        {
            if (!lineupLookup.TryGetValue(master.LineupId, out var lineup))
            {
                diagnostics.Add(ZaWorkflowSupport.Warning(
                    $"Shop '{master.ShopId}' references lineup '{master.LineupId}', but the lineup table does not contain it.",
                    lineupSource.RelativePath,
                    field: "lineupId",
                    expected: "Existing shop lineup"));
                continue;
            }

            usedLineups.Add(lineup.Name);
            yield return ToShopRecord(
                $"shop:{master.ShopId}",
                master,
                lineup,
                lineupSource,
                itemLookup,
                unresolvedItemIds,
                inventoryIndex++,
                totalCount);
        }

        foreach (var lineup in lineupRows
            .Where(row => !usedLineups.Contains(row.Name))
            .OrderBy(row => row.SourceIndex))
        {
            yield return ToShopRecord(
                $"lineup:{lineup.Name}",
                master: null,
                lineup,
                lineupSource,
                itemLookup,
                unresolvedItemIds,
                inventoryIndex++,
                totalCount);
        }

        if (itemLookup.Count > 0)
        {
            foreach (var itemId in unresolvedItemIds.Order())
            {
                diagnostics.Add(ZaWorkflowSupport.Warning(
                    $"Shop inventory references item ID {itemId}, but Items did not resolve that item.",
                    lineupSource.RelativePath));
            }
        }
    }

    private static ZaShopRecord ToShopRecord(
        string shopId,
        ShopMasterRow? master,
        ShopLineupRow lineup,
        ZaWorkflowFile source,
        IReadOnlyDictionary<int, ZaItemRecord> itemLookup,
        ISet<int> unresolvedItemIds,
        int inventoryIndex,
        int inventoryCount)
    {
        var shopKind = master?.ShopKind ?? 0;
        var currency = ResolveCurrency(shopKind);
        var inventory = lineup.Inventory
            .OrderBy(row => row.DisplayIndex)
            .ThenBy(row => row.SourceIndex)
            .Select((row, index) => ToInventoryRecord(index + 1, row, shopKind, itemLookup, unresolvedItemIds))
            .ToArray();

        return new ZaShopRecord(
            shopId,
            FormatShopName(master?.ShopId, lineup.Name),
            FormatShopKind(shopKind),
            FormatInventoryLabel(lineup.Name),
            inventoryIndex,
            Math.Max(1, inventoryCount),
            lineup.Name,
            FormatInventorySummary(inventory),
            FormatLocation(master),
            currency,
            inventory,
            new ZaShopProvenance(source.RelativePath, source.SourceLayer, source.FileState),
            CanEditInventoryOrder: true);
    }

    private static ZaShopInventoryRecord ToInventoryRecord(
        int slot,
        ShopInventoryRow row,
        int shopKind,
        IReadOnlyDictionary<int, ZaItemRecord> itemLookup,
        ISet<int> unresolvedItemIds)
    {
        var itemId = checked((int)row.ItemId);
        var item = ResolveItem(itemId, shopKind, itemLookup, unresolvedItemIds);
        var firstCondition = row.FirstCondition;
        var conditionKindValue = ConditionTokenToValue(firstCondition?.Condition);
        var conditionArguments = firstCondition is null ? string.Empty : string.Join(", ", firstCondition.Arguments);
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ItemIdField] = itemId.ToString(CultureInfo.InvariantCulture),
            [DisplayIndexField] = row.DisplayIndex.ToString(CultureInfo.InvariantCulture),
            [ConditionKindField] = conditionKindValue.ToString(CultureInfo.InvariantCulture),
            [ConditionComparisonField] = (firstCondition?.Comparison ?? 0).ToString(CultureInfo.InvariantCulture),
            [ConditionArgumentsField] = conditionArguments,
        };
        var displays = new Dictionary<string, string>(values, StringComparer.Ordinal)
        {
            [ItemIdField] = item.Name,
            [ConditionKindField] = firstCondition is null
                ? FormatConditionKind(ConditionKinds[0].Token)
                : FormatConditionKind(firstCondition.Condition),
            [ConditionComparisonField] = FormatConditionComparison(firstCondition?.Comparison ?? 0),
            [ConditionArgumentsField] = FormatConditionArguments(firstCondition, itemLookup),
        };

        return new ZaShopInventoryRecord(
            slot,
            itemId,
            item.Name,
            item.Price,
            item.IsKnown,
            StockLimit: null,
            values,
            displays,
            RowSupportedFields,
            PriceField: null,
            CanEditPrice: false);
    }

    private static ResolvedItem ResolveItem(
        int itemId,
        int shopKind,
        IReadOnlyDictionary<int, ZaItemRecord> itemLookup,
        ISet<int> unresolvedItemIds)
    {
        if (itemLookup.TryGetValue(itemId, out var item))
        {
            return new ResolvedItem(item.Name, ResolvePrice(item, shopKind), IsKnown: true);
        }

        if (itemId > 0)
        {
            unresolvedItemIds.Add(itemId);
        }

        return new ResolvedItem(itemId == 0 ? "None" : $"Item {itemId.ToString(CultureInfo.InvariantCulture)}", Price: 0, IsKnown: false);
    }

    private static int ResolvePrice(ZaItemRecord item, int shopKind)
    {
        return shopKind switch
        {
            7 => item.WattsPrice,
            8 => item.AlternatePrice,
            _ => item.BuyPrice,
        };
    }

    private static string ResolveCurrency(int shopKind)
    {
        return shopKind switch
        {
            7 => "Mega Shards",
            8 => "Colorful Screws",
            _ => "Money",
        };
    }

    private static string FormatShopName(string? shopId, string lineupId)
    {
        return shopId switch
        {
            "a01_friendlyshop_01" => "Friendly Shop",
            "a02_stoneshop_01" => "Stone Shop",
            "a04_ballshop_01" => "Ball Shop",
            "megashop_01" => "Mega Shard Shop 1",
            "megashop_02" => "Mega Shard Shop 2",
            "screwshop_01" => "Colorful Screw Shop",
            _ => FormatIdentifier(shopId ?? lineupId),
        };
    }

    private static string FormatShopKind(int shopKind)
    {
        return shopKind switch
        {
            2 => "Friendly Shop",
            3 => "Stone Shop",
            4 => "Ball Shop",
            7 => "Mega Shard Shop",
            8 => "Colorful Screw Shop",
            _ => $"Shop Type {shopKind.ToString(CultureInfo.InvariantCulture)}",
        };
    }

    private static string FormatInventoryLabel(string lineupId)
    {
        var suffixIndex = lineupId.IndexOf("_lineup", StringComparison.Ordinal);
        var baseName = suffixIndex > 0 ? lineupId[..suffixIndex] : lineupId;
        return $"{FormatIdentifier(baseName)} Lineup";
    }

    private static string FormatLocation(ShopMasterRow? master)
    {
        if (master is null)
        {
            return "Pokemon Legends Z-A";
        }

        if (!string.IsNullOrWhiteSpace(master.ResourceLabel))
        {
            return FormatIdentifier(master.ResourceLabel);
        }

        return FormatIdentifier(master.MessageLabel);
    }

    private static string FormatConditionComparison(uint comparison)
    {
        return ConditionComparisons.FirstOrDefault(definition => definition.Value == (int)comparison)?.Label
            ?? $"Comparison {comparison.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string FormatConditionArguments(
        ShopAppearCondition? condition,
        IReadOnlyDictionary<int, ZaItemRecord> itemLookup)
    {
        if (condition is null)
        {
            return FormatConditionKind(ConditionKinds[0].Token);
        }

        var label = FormatConditionKind(condition.Condition);
        var arguments = condition.Arguments.Count == 0
            ? string.Empty
            : string.Join(", ", condition.Arguments);

        return ConditionTokenToValue(condition.Condition) switch
        {
            0 => "No arguments",
            1 => string.IsNullOrEmpty(arguments) ? "Scenario phase value" : $"Scenario phase {arguments}",
            2 => string.IsNullOrEmpty(arguments) ? "Flag value" : $"Flag {arguments}",
            3 => string.IsNullOrEmpty(arguments) ? "Work value" : $"Work value {arguments}",
            4 => FormatHaveItemArguments(condition.Arguments, itemLookup),
            _ => string.IsNullOrEmpty(arguments) ? label : $"{label}: {arguments}",
        };
    }

    private static string FormatHaveItemArguments(
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<int, ZaItemRecord> itemLookup)
    {
        if (arguments.Count == 0)
        {
            return "Item requirement";
        }

        if (int.TryParse(arguments[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId)
            && itemLookup.TryGetValue(itemId, out var item))
        {
            var quantitySuffix = arguments.Count > 1 ? $" x {string.Join(", ", arguments.Skip(1))}" : string.Empty;
            return $"{item.Name} ({itemId.ToString(CultureInfo.InvariantCulture)}){quantitySuffix}";
        }

        return $"Item requirement {string.Join(", ", arguments)}";
    }

    private static string FormatIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unknown";
        }

        var parts = value
            .Replace('-', '_')
            .Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0
            ? value
            : string.Join(
                " ",
                parts.Select(part => part.Length <= 1
                    ? part.ToUpperInvariant()
                    : string.Concat(part[..1].ToUpperInvariant(), part[1..])));
    }

    private static IReadOnlyList<ZaShopEditableField> CreateEditableFields(
        IReadOnlyList<ZaItemRecord> items,
        IReadOnlyList<ZaShopRecord> shops)
    {
        var itemOptions = CreateItemOptions(items, shops);
        return
        [
            CreateField(ItemIdField, "Item", "integer", MinimumItemId, MaximumItemId, itemOptions),
            CreateField(DisplayIndexField, "Display order", "integer", 0, int.MaxValue),
            CreateField(ConditionKindField, "First condition", "integer", 0, 4, ConditionKindOptions),
            CreateField(ConditionComparisonField, "Condition comparison", "integer", 0, int.MaxValue, ConditionComparisonOptions),
            CreateField(ConditionArgumentsField, "Condition arguments", "text", null, null),
        ];
    }

    private static IReadOnlyList<ZaShopEditableFieldOption> CreateItemOptions(
        IReadOnlyList<ZaItemRecord> items,
        IReadOnlyList<ZaShopRecord> shops)
    {
        var options = new Dictionary<int, ZaShopEditableFieldOption>
        {
            [0] = new(0, "0 None", "None", 0),
        };

        foreach (var item in items)
        {
            options.TryAdd(
                item.ItemId,
                new ZaShopEditableFieldOption(
                    item.ItemId,
                    $"{item.ItemId.ToString(CultureInfo.InvariantCulture)} {item.Name}",
                    item.Name,
                    item.BuyPrice));
        }

        foreach (var item in shops.SelectMany(shop => shop.Inventory).Where(item => item.IsKnownItem))
        {
            options.TryAdd(
                item.ItemId,
                new ZaShopEditableFieldOption(
                    item.ItemId,
                    $"{item.ItemId.ToString(CultureInfo.InvariantCulture)} {item.ItemName}",
                    item.ItemName,
                    item.Price));
        }

        return options.Values.OrderBy(option => option.Value).ToArray();
    }

    private static ZaShopEditableField CreateField(
        string field,
        string label,
        string valueKind,
        int? minimumValue,
        int? maximumValue,
        IReadOnlyList<ZaShopEditableFieldOption>? options = null)
    {
        return new ZaShopEditableField(
            field,
            label,
            valueKind,
            minimumValue,
            maximumValue,
            options ?? Array.Empty<ZaShopEditableFieldOption>());
    }

    private sealed record ConditionKindDefinition(
        int Value,
        string Token,
        string Label);

    private sealed record ConditionComparisonDefinition(
        int Value,
        string Label);

    private sealed record ResolvedItem(
        string Name,
        int Price,
        bool IsKnown);

    public sealed record ShopMasterRow(
        int SourceIndex,
        string ShopId,
        string LineupId,
        string ResourceLabel,
        string MessageLabel,
        int ShopKind,
        int Condition)
    {
        public static ShopMasterRow From(int sourceIndex, ZaShopData row) =>
            new(
                sourceIndex,
                row.ShopId ?? string.Empty,
                row.LineupId ?? string.Empty,
                row.ResourceLabel ?? string.Empty,
                row.MessageLabel ?? string.Empty,
                row.ShopKind,
                row.Condition);
    }

    public sealed class ShopLineupRow
    {
        public ShopLineupRow(
            int sourceIndex,
            string name,
            IReadOnlyList<ShopInventoryRow> inventory)
        {
            SourceIndex = sourceIndex;
            Name = name;
            Inventory = inventory.ToList();
        }

        public int SourceIndex { get; }

        public string Name { get; }

        public List<ShopInventoryRow> Inventory { get; }

        public static ShopLineupRow From(int sourceIndex, ZaShopLineup row)
        {
            var inventory = new List<ShopInventoryRow>();
            for (var index = 0; index < row.InventoryLength; index++)
            {
                var item = row.Inventory(index);
                if (item is not null)
                {
                    inventory.Add(ShopInventoryRow.From(index, item.Value));
                }
            }

            return new ShopLineupRow(sourceIndex, row.Name ?? string.Empty, inventory);
        }

        public Offset<ZaShopLineup> Write(FlatBufferBuilder builder)
        {
            var nameOffset = builder.CreateString(Name);
            var inventoryOffsets = Inventory.Select(row => row.Write(builder)).ToArray();
            var inventoryVector = ZaShopLineup.CreateInventoryVector(builder, inventoryOffsets);

            ZaShopLineup.StartZaShopLineup(builder);
            ZaShopLineup.AddName(builder, nameOffset);
            ZaShopLineup.AddInventory(builder, inventoryVector);
            return ZaShopLineup.EndZaShopLineup(builder);
        }
    }

    public sealed class ShopInventoryRow
    {
        public ShopInventoryRow(
            int sourceIndex,
            uint itemId,
            uint displayIndex,
            IReadOnlyList<ShopConditionGroup> conditions)
        {
            SourceIndex = sourceIndex;
            ItemId = itemId;
            DisplayIndex = displayIndex;
            Conditions = conditions.ToList();
        }

        public int SourceIndex { get; }

        public uint ItemId { get; set; }

        public uint DisplayIndex { get; set; }

        public List<ShopConditionGroup> Conditions { get; }

        public ShopAppearCondition? FirstCondition =>
            Conditions.FirstOrDefault()?.Values.FirstOrDefault()?.Values.FirstOrDefault();

        public ShopAppearCondition EnsureFirstCondition()
        {
            if (Conditions.Count == 0)
            {
                Conditions.Add(new ShopConditionGroup([new ShopConditionHolder([new ShopAppearCondition(ConditionKinds[0].Token, 0, [])])]));
            }

            var conditionGroup = Conditions[0];
            if (conditionGroup.Values.Count == 0)
            {
                conditionGroup.Values.Add(new ShopConditionHolder([new ShopAppearCondition(ConditionKinds[0].Token, 0, [])]));
            }

            var holder = conditionGroup.Values[0];
            if (holder.Values.Count == 0)
            {
                holder.Values.Add(new ShopAppearCondition(ConditionKinds[0].Token, 0, []));
            }

            return holder.Values[0];
        }

        public static ShopInventoryRow From(int sourceIndex, ZaShopInventory row)
        {
            var conditions = new List<ShopConditionGroup>();
            for (var index = 0; index < row.ConditionsLength; index++)
            {
                var condition = row.Conditions(index);
                if (condition is not null)
                {
                    conditions.Add(ShopConditionGroup.From(condition.Value));
                }
            }

            return new ShopInventoryRow(sourceIndex, row.Item, row.DisplayIndex, conditions);
        }

        public Offset<ZaShopInventory> Write(FlatBufferBuilder builder)
        {
            var conditionOffsets = Conditions.Select(condition => condition.Write(builder)).ToArray();
            var conditionVector = ZaShopInventory.CreateConditionsVector(builder, conditionOffsets);

            ZaShopInventory.StartZaShopInventory(builder);
            ZaShopInventory.AddItem(builder, ItemId);
            ZaShopInventory.AddDisplayIndex(builder, DisplayIndex);
            ZaShopInventory.AddConditions(builder, conditionVector);
            return ZaShopInventory.EndZaShopInventory(builder);
        }
    }

    public sealed class ShopConditionGroup
    {
        public ShopConditionGroup(IReadOnlyList<ShopConditionHolder> values)
        {
            Values = values.ToList();
        }

        public List<ShopConditionHolder> Values { get; }

        public static ShopConditionGroup From(ZaShopInventoryCondition row)
        {
            var values = new List<ShopConditionHolder>();
            for (var index = 0; index < row.ValuesLength; index++)
            {
                var value = row.Values(index);
                if (value is not null)
                {
                    values.Add(ShopConditionHolder.From(value.Value));
                }
            }

            return new ShopConditionGroup(values);
        }

        public Offset<ZaShopInventoryCondition> Write(FlatBufferBuilder builder)
        {
            var offsets = Values.Select(value => value.Write(builder)).ToArray();
            var vector = ZaShopInventoryCondition.CreateValuesVector(builder, offsets);

            ZaShopInventoryCondition.StartZaShopInventoryCondition(builder);
            ZaShopInventoryCondition.AddValues(builder, vector);
            return ZaShopInventoryCondition.EndZaShopInventoryCondition(builder);
        }
    }

    public sealed class ShopConditionHolder
    {
        public ShopConditionHolder(IReadOnlyList<ShopAppearCondition> values)
        {
            Values = values.ToList();
        }

        public List<ShopAppearCondition> Values { get; }

        public static ShopConditionHolder From(ZaShopInventoryConditionHolder row)
        {
            var values = new List<ShopAppearCondition>();
            for (var index = 0; index < row.ValuesLength; index++)
            {
                var value = row.Values(index);
                if (value is not null)
                {
                    values.Add(ShopAppearCondition.From(value.Value));
                }
            }

            return new ShopConditionHolder(values);
        }

        public Offset<ZaShopInventoryConditionHolder> Write(FlatBufferBuilder builder)
        {
            var offsets = Values.Select(value => value.Write(builder)).ToArray();
            var vector = ZaShopInventoryConditionHolder.CreateValuesVector(builder, offsets);

            ZaShopInventoryConditionHolder.StartZaShopInventoryConditionHolder(builder);
            ZaShopInventoryConditionHolder.AddValues(builder, vector);
            return ZaShopInventoryConditionHolder.EndZaShopInventoryConditionHolder(builder);
        }
    }

    public sealed class ShopAppearCondition
    {
        public ShopAppearCondition(
            string condition,
            uint comparison,
            IReadOnlyList<string> arguments)
        {
            Condition = condition;
            Comparison = comparison;
            Arguments = arguments.ToList();
        }

        public string Condition { get; set; }

        public uint Comparison { get; set; }

        public List<string> Arguments { get; }

        public static ShopAppearCondition From(ZaShopInventoryAppearCondition row)
        {
            var arguments = new List<string>();
            for (var index = 0; index < row.ArgumentsLength; index++)
            {
                arguments.Add(row.Arguments(index) ?? string.Empty);
            }

            return new ShopAppearCondition(
                row.Condition ?? string.Empty,
                row.Comparison,
                arguments);
        }

        public Offset<ZaShopInventoryAppearCondition> Write(FlatBufferBuilder builder)
        {
            var conditionOffset = string.IsNullOrEmpty(Condition)
                ? default
                : builder.CreateString(Condition);
            var argumentOffsets = Arguments.Select(builder.CreateString).ToArray();
            var argumentsVector = ZaShopInventoryAppearCondition.CreateArgumentsVector(builder, argumentOffsets);

            ZaShopInventoryAppearCondition.StartZaShopInventoryAppearCondition(builder);
            ZaShopInventoryAppearCondition.AddCondition(builder, conditionOffset);
            ZaShopInventoryAppearCondition.AddComparison(builder, Comparison);
            ZaShopInventoryAppearCondition.AddArguments(builder, argumentsVector);
            return ZaShopInventoryAppearCondition.EndZaShopInventoryAppearCondition(builder);
        }
    }
}
