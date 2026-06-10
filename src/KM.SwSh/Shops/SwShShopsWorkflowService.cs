// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Items;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.Shops;

public sealed class SwShShopsWorkflowService
{
    public const string ItemIdField = "itemId";
    public const string AddItemField = "addItem";
    public const string RemoveItemField = "removeItem";
    public const string SetInventoryField = "setInventory";
    public const int MinimumItemId = 0;
    public const int MaximumItemId = 65_535;
    public const string ShopDataPath = "romfs/bin/appli/shop/bin/shop_data.bin";
    public const string LegacyShopDataPath = "romfs/bin/app/shop/shop_data.bin";

    private const string ShopsEditDomain = "workflow.shops";

    private static readonly IReadOnlyDictionary<ulong, string> KnownSingleShopNames = new Dictionary<ulong, string>
    {
        [0x1F3FF031A3A24490] = "Poke Mart [0 Badges, Before Catching Tutorial]",
        [0x8E308F85B43038B4] = "Motostoke [Upper Tier, TMs]",
        [0x8E309085B4303A67] = "Hammerlocke [West, TMs]",
        [0x8E309185B4303C1A] = "Hammerlocke [East, TMs]",
        [0x8E309285B4303DCD] = "Wyndon [North, TMs]",
        [0x8E308B85B43031E8] = "Battle Tower [TMs]",
        [0xCBD67969D873539B] = "Motostoke [Lower Tier, Miscellaneous]",
        [0xCBD67869D87351E8] = "Hammerlocke [South, Miscellaneous]",
        [0xCBD67B69D8735701] = "Wyndon [South, Miscellaneous]",
        [0x04D7046DA09D3C78] = "Hulbury [Herb Shop]",
        [0x4B2F9E98DDCB0707] = "Hulbury [Incense Shop]",
        [0xE379CDF67A297070] = "Wedgehurst [Berry Shop]",
        [0x3FD7A44219BF30BB] = "Hammerlocke [South, BP Shop]",
        [0x3FD7A34219BF2F08] = "Battle Tower [Battle Items]",
        [0x3FD7A64219BF3421] = "Battle Tower [Nature Mints]",
        [0xD1BEAA2EAAE52D0D] = "Watt Trader 1 [Net Ball]",
        [0xD1BEA72EAAE527F4] = "Watt Trader 1 [Dive Ball]",
        [0xD1BEA82EAAE529A7] = "Watt Trader 1 [Nest Ball]",
        [0xD1BEA52EAAE5248E] = "Watt Trader 1 [Repeat Ball]",
        [0xD1BEA62EAAE52641] = "Watt Trader 1 [Timer Ball]",
        [0xD1BEA32EAAE52128] = "Watt Trader 1 [Luxury Ball]",
        [0xD1BEA42EAAE522DB] = "Watt Trader 1 [Dusk Ball]",
        [0xD1BEA12EAAE51DC2] = "Watt Trader 1 [Heal Ball]",
        [0xD1BEA22EAAE51F75] = "Watt Trader 1 [Quick Ball]",
        [0xD1C20F2EAAE80E83] = "Watt Trader 2 [Net Ball]",
        [0xD1C20E2EAAE80CD0] = "Watt Trader 2 [Dive Ball]",
        [0xD1C2112EAAE811E9] = "Watt Trader 2 [Nest Ball]",
        [0xD1C2102EAAE81036] = "Watt Trader 2 [Repeat Ball]",
        [0xD1C2132EAAE8154F] = "Watt Trader 2 [Timer Ball]",
        [0xD1C2122EAAE8139C] = "Watt Trader 2 [Luxury Ball]",
        [0xD1C2152EAAE818B5] = "Watt Trader 2 [Dusk Ball]",
        [0xD1C2142EAAE81702] = "Watt Trader 2 [Heal Ball]",
        [0xD1C2172EAAE81C1B] = "Watt Trader 2 [Quick Ball]",
        [0xD1C2162EAAE81A68] = "Watt Trader 3 [Net Ball]",
        [0xD1B79D2EAADEF848] = "Watt Trader 3 [Dive Ball]",
        [0xD1B79E2EAADEF9FB] = "Watt Trader 3 [Nest Ball]",
        [0xD1B79F2EAADEFBAE] = "Watt Trader 3 [Repeat Ball]",
        [0xD1B7A02EAADEFD61] = "Watt Trader 3 [Timer Ball]",
        [0xD1B7A12EAADEFF14] = "Watt Trader 3 [Luxury Ball]",
        [0xD1B7A22EAADF00C7] = "Watt Trader 3 [Dusk Ball]",
        [0xD1B7A32EAADF027A] = "Watt Trader 3 [Heal Ball]",
        [0xD1B7A42EAADF042D] = "Watt Trader 3 [Quick Ball]",
        [0xD1B7952EAADEEAB0] = "Watt Trader 4 [Net Ball]",
        [0xD1B7962EAADEEC63] = "Watt Trader 4 [Dive Ball]",
        [0xD1BB232EAAE211D1] = "Watt Trader 4 [Nest Ball]",
        [0xD1BB222EAAE2101E] = "Watt Trader 4 [Repeat Ball]",
        [0xD1BB212EAAE20E6B] = "Watt Trader 4 [Timer Ball]",
        [0xD1BB202EAAE20CB8] = "Watt Trader 4 [Luxury Ball]",
        [0xD1BB272EAAE2189D] = "Watt Trader 4 [Dusk Ball]",
        [0xD1BB262EAAE216EA] = "Watt Trader 4 [Heal Ball]",
        [0xD1BB252EAAE21537] = "Watt Trader 4 [Quick Ball]",
        [0xD1BB242EAAE21384] = "Watt Trader 5 [Net Ball]",
        [0xD1BB1B2EAAE20439] = "Watt Trader 5 [Dive Ball]",
        [0xD1BB1A2EAAE20286] = "Watt Trader 5 [Nest Ball]",
        [0xD1CC212EAAF0819E] = "Watt Trader 5 [Repeat Ball]",
        [0xD1CC222EAAF08351] = "Watt Trader 5 [Timer Ball]",
        [0xD1CC1F2EAAF07E38] = "Watt Trader 5 [Luxury Ball]",
        [0xD1CC202EAAF07FEB] = "Watt Trader 5 [Dusk Ball]",
        [0xD1CC252EAAF0886A] = "Watt Trader 5 [Heal Ball]",
        [0xD1CC262EAAF08A1D] = "Watt Trader 5 [Quick Ball]",
        [0xD1CC232EAAF08504] = "Watt Trader 6 [Net Ball]",
        [0xD1CC242EAAF086B7] = "Watt Trader 6 [Dive Ball]",
        [0xD1CC192EAAF07406] = "Watt Trader 6 [Repeat Ball]",
        [0xD1CC1A2EAAF075B9] = "Watt Trader 6 [Quick Ball]",
        [0xD1CFA72EAAF39B27] = "Watt Trader 6 [Heal Ball]",
        [0x5870C0165650F6A5] = "Fields of Honor [Berry Shop]",
        [0x81DA6390A03C7E3F] = "Freezington [Peddler]",
        [0x813C350B0B777943] = "Snowslide Slope [Today's Highlight, TR00-TR09]",
        [0x813C360B0B777AF6] = "Snowslide Slope [Today's Highlight, TR10-TR19]",
        [0x813C370B0B777CA9] = "Snowslide Slope [Today's Highlight, TR20-TR29]",
        [0x813C380B0B777E5C] = "Snowslide Slope [Today's Highlight, TR30-TR39]",
        [0x813C390B0B77800F] = "Snowslide Slope [Today's Highlight, TR40-TR49]",
        [0x813C3A0B0B7781C2] = "Snowslide Slope [Today's Highlight, TR50-TR59]",
        [0x813C3B0B0B778375] = "Snowslide Slope [Today's Highlight, TR60-TR69]",
        [0x813C3C0B0B778528] = "Snowslide Slope [Today's Highlight, TR70-TR79]",
        [0x813C3D0B0B7786DB] = "Snowslide Slope [Today's Highlight, TR80-TR89]",
        [0x813F3A0B0B79B799] = "Snowslide Slope [Today's Highlight, TR90-TR99]",
        [0xF49C86F8683842BF] = "Max Lair [Dynite Ore Trader]",
    };

    private static readonly IReadOnlyDictionary<ulong, string> KnownMultiShopNames = new Dictionary<ulong, string>
    {
        [0x66CA73B2966BB871] = "Poke Mart Inventories [0-8 Badges]",
        [0x5870BD165650F18C] = "Fields of Honor [Watt Trader, 0-8 Badges]",
    };

    private readonly SwShItemsWorkflowService itemsWorkflowService;

    public SwShShopsWorkflowService(SwShItemsWorkflowService? itemsWorkflowService = null)
    {
        this.itemsWorkflowService = itemsWorkflowService ?? new SwShItemsWorkflowService();
    }

    public SwShWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!project.Health.CanOpenReadOnlyWorkflows)
        {
            return CreateSummary(
                SwShWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Shops requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShShopsWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);

        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(
                summary,
                Array.Empty<SwShShopRecord>(),
                CreateEditableFields(Array.Empty<SwShItemRecord>()),
                sourceFileCount: 0,
                diagnostics);
        }

        var shopDataSource = ResolveShopDataSource(project);
        if (shopDataSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Shops data is not available for this project.",
                expected: $"{ShopDataPath} or {LegacyShopDataPath}"));
            return CreateWorkflow(
                summary,
                Array.Empty<SwShShopRecord>(),
                CreateEditableFields(Array.Empty<SwShItemRecord>()),
                sourceFileCount: 0,
                diagnostics);
        }

        var itemLookup = CreateItemLookup(project, diagnostics);
        var editableFields = CreateEditableFields(itemLookup.Values);

        try
        {
            var shopData = SwShShopDataFile.Parse(File.ReadAllBytes(shopDataSource.AbsolutePath));
            var provenance = CreateProvenance(shopDataSource.GraphEntry);
            var shops = FlattenShops(shopData, itemLookup, provenance, diagnostics);

            return CreateWorkflow(summary, shops, editableFields, sourceFileCount: 1, diagnostics);
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shops data source is not a supported Sword/Shield shop table: {exception.Message}",
                file: shopDataSource.GraphEntry.RelativePath,
                expected: "Sword/Shield shop_data.bin"));
            return CreateWorkflow(
                summary,
                Array.Empty<SwShShopRecord>(),
                editableFields,
                sourceFileCount: 1,
                diagnostics);
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shops data source could not be read: {exception.Message}",
                file: shopDataSource.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield shop_data.bin"));
            return CreateWorkflow(
                summary,
                Array.Empty<SwShShopRecord>(),
                editableFields,
                sourceFileCount: 1,
                diagnostics);
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shops data source could not be read: {exception.Message}",
                file: shopDataSource.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield shop_data.bin"));
            return CreateWorkflow(
                summary,
                Array.Empty<SwShShopRecord>(),
                editableFields,
                sourceFileCount: 1,
                diagnostics);
        }
    }

    internal static bool TryParseShopId(
        string? shopId,
        out SwShShopKind kind,
        out ulong hash,
        out int inventoryIndex)
    {
        kind = SwShShopKind.Single;
        hash = 0;
        inventoryIndex = 0;

        var parts = shopId?.Split(':') ?? [];
        if (parts.Length == 2
            && string.Equals(parts[0], "single", StringComparison.Ordinal)
            && ulong.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out hash))
        {
            return true;
        }

        if (parts.Length == 3
            && string.Equals(parts[0], "multi", StringComparison.Ordinal)
            && ulong.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out hash)
            && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out inventoryIndex)
            && inventoryIndex >= 0)
        {
            kind = SwShShopKind.Multi;
            return true;
        }

        return false;
    }

    internal static bool IsEditableField(string? field)
    {
        return string.Equals(field, ItemIdField, StringComparison.Ordinal)
            || string.Equals(field, AddItemField, StringComparison.Ordinal)
            || string.Equals(field, RemoveItemField, StringComparison.Ordinal)
            || string.Equals(field, SetInventoryField, StringComparison.Ordinal);
    }

    internal static WorkflowFileSource? ResolveShopDataSource(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return ResolveWorkflowFile(project, ShopDataPath)
            ?? ResolveWorkflowFile(project, LegacyShopDataPath);
    }

    internal static WorkflowFileSource? ResolveWorkflowFile(OpenedProject project, string relativePath)
    {
        var graphEntry = project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));

        if (graphEntry is null)
        {
            return null;
        }

        var sourcePath = ResolveSourcePath(project.Paths, graphEntry);

        return sourcePath is not null && File.Exists(sourcePath)
            ? new WorkflowFileSource(graphEntry, sourcePath)
            : null;
    }

    internal static string? ResolveOutputPath(ProjectPaths paths, string targetRelativePath)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(targetRelativePath);

        if (string.IsNullOrWhiteSpace(paths.OutputRootPath) || Path.IsPathRooted(targetRelativePath))
        {
            return null;
        }

        var outputRoot = Path.GetFullPath(paths.OutputRootPath);
        var targetPath = Path.GetFullPath(Path.Combine(
            outputRoot,
            targetRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        var outputRootWithSeparator = outputRoot.EndsWith(Path.DirectorySeparatorChar)
            ? outputRoot
            : outputRoot + Path.DirectorySeparatorChar;

        return targetPath.StartsWith(outputRootWithSeparator, StringComparison.OrdinalIgnoreCase)
            ? targetPath
            : null;
    }

    internal static string CreateShopId(SwShShopKind kind, ulong hash, int inventoryIndex)
    {
        return kind == SwShShopKind.Single
            ? $"single:{hash:X16}"
            : $"multi:{hash:X16}:{inventoryIndex.ToString(CultureInfo.InvariantCulture)}";
    }

    internal static string CreateInventoryRecordId(string shopId, int slot)
    {
        return $"{shopId}#{slot.ToString(CultureInfo.InvariantCulture)}";
    }

    internal static bool TryParseInventoryRecordId(string? recordId, out string shopId, out int slot)
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

    private SwShShopsWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShShopRecord> shops,
        IReadOnlyList<SwShShopEditableField> editableFields,
        int sourceFileCount,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SwShShopsWorkflow(
            summary,
            shops,
            editableFields,
            new SwShShopsWorkflowStats(
                shops.Count,
                shops.Sum(shop => shop.Inventory.Count),
                sourceFileCount),
            diagnostics);
    }

    private static IReadOnlyList<SwShShopEditableField> CreateEditableFields(IEnumerable<SwShItemRecord> items)
    {
        var options = items
            .OrderBy(item => item.ItemId)
            .Select(item => new SwShShopEditableFieldOption(
                item.ItemId,
                FormatItemOptionLabel(item),
                item.Name,
                item.BuyPrice))
            .ToArray();

        return
        [
            new SwShShopEditableField(
                ItemIdField,
                "Item",
                "integer",
                MinimumItemId,
                MaximumItemId,
                options),
        ];
    }

    internal static string FormatItemOptionLabel(SwShItemRecord item)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{item.ItemId:0000} {item.Name} ({item.Category})");
    }

    private IReadOnlyDictionary<int, SwShItemRecord> CreateItemLookup(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var itemsWorkflow = itemsWorkflowService.Load(project);
        if (itemsWorkflow.Items.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Item metadata is not available; shop inventory rows will use item ID fallback labels and zero prices.",
                expected: SwShItemsWorkflowService.ItemDataPath));
        }

        return itemsWorkflow.Items
            .GroupBy(item => item.ItemId)
            .ToDictionary(group => group.Key, group => group.First());
    }

    private static SwShShopRecord[] FlattenShops(
        SwShShopDataFile shopData,
        IReadOnlyDictionary<int, SwShItemRecord> itemLookup,
        SwShShopProvenance provenance,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var shops = new List<SwShShopRecord>();
        var unresolvedItemIds = new HashSet<int>();

        foreach (var (shop, index) in shopData.SingleShops.OrderBy(shop => shop.Hash).Select((shop, index) => (shop, index)))
        {
            var inventoryRows = CreateInventoryRecords(shop.Inventory, itemLookup, unresolvedItemIds);
            var name = FormatSingleShopName(shop.Hash, index, [inventoryRows]);
            shops.Add(ToShopRecord(
                CreateShopId(SwShShopKind.Single, shop.Hash, inventoryIndex: 0),
                name,
                "Single",
                "Inventory",
                1,
                1,
                shop.Hash,
                inventoryRows,
                provenance));
        }

        foreach (var (shop, shopIndex) in shopData.MultiShops.OrderBy(shop => shop.Hash).Select((shop, index) => (shop, index)))
        {
            var inventoryRowsByIndex = shop.Inventories
                .Select(inventory => CreateInventoryRecords(inventory, itemLookup, unresolvedItemIds))
                .ToArray();
            var name = FormatMultiShopName(shop.Hash, shopIndex, inventoryRowsByIndex);
            for (var inventoryIndex = 0; inventoryIndex < shop.Inventories.Count; inventoryIndex++)
            {
                var inventoryLabel = IsBadgeIndexedMultiShop(name, shop.Inventories.Count)
                    ? FormatBadgeInventoryLabel(inventoryIndex)
                    : $"Inventory {inventoryIndex + 1} of {shop.Inventories.Count}";
                var recordName = IsBadgeIndexedMultiShop(name, shop.Inventories.Count)
                    ? $"{name} [{inventoryLabel}]"
                    : $"{name} #{inventoryIndex + 1}";
                shops.Add(ToShopRecord(
                    CreateShopId(SwShShopKind.Multi, shop.Hash, inventoryIndex),
                    recordName,
                    "Multi",
                    inventoryLabel,
                    inventoryIndex + 1,
                    shop.Inventories.Count,
                    shop.Hash,
                    inventoryRowsByIndex[inventoryIndex],
                    provenance));
            }
        }

        if (itemLookup.Count > 0)
        {
            foreach (var itemId in unresolvedItemIds.Order())
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Shop inventory references item ID {itemId}, but the Items workflow did not resolve that item.",
                    file: provenance.SourceFile,
                    expected: $"Loaded item metadata for item {itemId}"));
            }
        }

        return shops.ToArray();
    }

    internal static bool IsBadgeIndexedMultiShop(string name, int inventoryCount)
    {
        return inventoryCount == 9
            && name.Contains("0-8 Badges", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatBadgeInventoryLabel(int inventoryIndex)
    {
        return inventoryIndex == 1
            ? "1 Badge"
            : string.Create(CultureInfo.InvariantCulture, $"{inventoryIndex} Badges");
    }

    private static SwShShopRecord ToShopRecord(
        string shopId,
        string name,
        string kind,
        string inventoryLabel,
        int inventoryIndex,
        int inventoryCount,
        ulong hash,
        IReadOnlyList<SwShShopInventoryRecord> inventoryRows,
        SwShShopProvenance provenance)
    {
        return new SwShShopRecord(
            shopId,
            name,
            kind,
            inventoryLabel,
            inventoryIndex,
            inventoryCount,
            $"0x{hash:X16}",
            FormatInventorySummary(inventoryRows),
            FormatLocation(name),
            FormatCurrency(name),
            inventoryRows,
            provenance);
    }

    private static SwShShopInventoryRecord[] CreateInventoryRecords(
        SwShShopInventory inventory,
        IReadOnlyDictionary<int, SwShItemRecord> itemLookup,
        ISet<int> unresolvedItemIds)
    {
        return inventory.Items
            .Select((itemId, index) => ToInventoryRecord(index, itemId, itemLookup, unresolvedItemIds))
            .ToArray();
    }

    private static SwShShopInventoryRecord ToInventoryRecord(
        int index,
        int itemId,
        IReadOnlyDictionary<int, SwShItemRecord> itemLookup,
        ISet<int> unresolvedItemIds)
    {
        if (itemLookup.TryGetValue(itemId, out var item))
        {
            return new SwShShopInventoryRecord(
                index + 1,
                itemId,
                item.Name,
                item.BuyPrice,
                IsKnownItem: true,
                StockLimit: null);
        }

        unresolvedItemIds.Add(itemId);
        return new SwShShopInventoryRecord(
            index + 1,
            itemId,
            $"Item {itemId}",
            Price: 0,
            IsKnownItem: false,
            StockLimit: null);
    }

    internal static string FormatInventorySummary(IReadOnlyList<SwShShopInventoryRecord> inventory)
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

    private static string FormatSingleShopName(
        ulong hash,
        int index,
        IEnumerable<IReadOnlyList<SwShShopInventoryRecord>> inventories)
    {
        return KnownSingleShopNames.TryGetValue(hash, out var name)
            ? name
            : FormatFallbackShopName("Single Shop", hash, index, inventories);
    }

    private static string FormatMultiShopName(
        ulong hash,
        int index,
        IEnumerable<IReadOnlyList<SwShShopInventoryRecord>> inventories)
    {
        return KnownMultiShopNames.TryGetValue(hash, out var name)
            ? name
            : FormatFallbackShopName("Multi Shop", hash, index, inventories);
    }

    private static string FormatFallbackShopName(
        string label,
        ulong hash,
        int index,
        IEnumerable<IReadOnlyList<SwShShopInventoryRecord>> inventories)
    {
        var summary = string.Join(
            " / ",
            inventories
                .Select(FormatInventoryNameSummary)
                .Where(value => value.Length > 0)
                .Take(2));

        return summary.Length == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{label} {index + 1} [0x{hash:X16}]")
            : string.Create(CultureInfo.InvariantCulture, $"{label} {index + 1} [{summary}]");
    }

    private static string FormatInventoryNameSummary(IReadOnlyList<SwShShopInventoryRecord> inventory)
    {
        const int MaxItems = 4;

        var summary = string.Join(", ", inventory.Take(MaxItems).Select(item => item.ItemName));
        return inventory.Count > MaxItems
            ? string.Create(CultureInfo.InvariantCulture, $"{summary}, ...")
            : summary;
    }

    private static string FormatLocation(string name)
    {
        var bracketIndex = name.IndexOf('[', StringComparison.Ordinal);
        return bracketIndex > 0
            ? name[..bracketIndex].Trim()
            : name;
    }

    private static string FormatCurrency(string name)
    {
        if (name.Contains("Watt", StringComparison.OrdinalIgnoreCase))
        {
            return "Watts";
        }

        if (name.Contains("BP Shop", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Battle Tower", StringComparison.OrdinalIgnoreCase))
        {
            return "BP";
        }

        if (name.Contains("Dynite", StringComparison.OrdinalIgnoreCase))
        {
            return "Dynite Ore";
        }

        return "Money";
    }

    private static string? ResolveSourcePath(ProjectPaths paths, ProjectFileGraphEntry entry)
    {
        if (entry.LayeredFile is not null && !string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            return CombineGraphPath(paths.OutputRootPath, entry.RelativePath);
        }

        if (entry.BaseFile is not null && entry.RelativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineGraphPath(paths.BaseRomFsPath, entry.RelativePath["romfs/".Length..]);
        }

        return null;
    }

    private static string? CombineGraphPath(string? rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return null;
        }

        return Path.Combine(
            rootPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static SwShShopProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShShopProvenance(entry.RelativePath, sourceLayer, entry.State);
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.Shops,
            "Shops",
            "Shop inventories, item metadata, and source provenance.",
            availability,
            diagnostics);
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? file = null,
        string? expected = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Domain: ShopsEditDomain,
            Expected: expected);
    }

    internal sealed record WorkflowFileSource(
        ProjectFileGraphEntry GraphEntry,
        string AbsolutePath);
}
