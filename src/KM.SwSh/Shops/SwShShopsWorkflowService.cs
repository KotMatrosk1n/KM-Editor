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
    public const int MinimumItemId = 0;
    public const int MaximumItemId = 65_535;
    public const string ShopDataPath = "romfs/bin/app/shop/shop_data.bin";

    private const string ShopsEditDomain = "workflow.shops";

    private static readonly IReadOnlyDictionary<ulong, string> KnownSingleShopNames = new Dictionary<ulong, string>
    {
        [0x1F3FF031A3A24490] = "Poke Mart [0 Badges, Before Catching Tutorial]",
        [0x8E308F85B43038B4] = "Motostoke [Upper Tier, TMs]",
        [0x8E309085B4303A67] = "Hammerlocke [West, TMs]",
        [0x8E309185B4303C1A] = "Hammerlocke [East, TMs]",
        [0x8E309285B4303DCD] = "Wyndon [North, TMs]",
        [0x3FD7A44219BF30BB] = "Hammerlocke [South, BP Shop]",
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
                expected: ShopDataPath));
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
            var shops = FlattenShops(shopData, itemLookup, provenance);

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
        return string.Equals(field, ItemIdField, StringComparison.Ordinal);
    }

    internal static WorkflowFileSource? ResolveShopDataSource(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return ResolveWorkflowFile(project, ShopDataPath);
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
        SwShShopProvenance provenance)
    {
        var shops = new List<SwShShopRecord>();

        foreach (var shop in shopData.SingleShops.OrderBy(shop => shop.Hash))
        {
            var name = FormatSingleShopName(shop.Hash);
            shops.Add(ToShopRecord(
                CreateShopId(SwShShopKind.Single, shop.Hash, inventoryIndex: 0),
                name,
                shop.Inventory,
                itemLookup,
                provenance));
        }

        foreach (var shop in shopData.MultiShops.OrderBy(shop => shop.Hash))
        {
            var name = FormatMultiShopName(shop.Hash);
            for (var inventoryIndex = 0; inventoryIndex < shop.Inventories.Count; inventoryIndex++)
            {
                shops.Add(ToShopRecord(
                    CreateShopId(SwShShopKind.Multi, shop.Hash, inventoryIndex),
                    $"{name} #{inventoryIndex + 1}",
                    shop.Inventories[inventoryIndex],
                    itemLookup,
                    provenance));
            }
        }

        return shops.ToArray();
    }

    private static SwShShopRecord ToShopRecord(
        string shopId,
        string name,
        SwShShopInventory inventory,
        IReadOnlyDictionary<int, SwShItemRecord> itemLookup,
        SwShShopProvenance provenance)
    {
        return new SwShShopRecord(
            shopId,
            name,
            FormatLocation(name),
            FormatCurrency(name),
            inventory.Items
                .Select((itemId, index) => ToInventoryRecord(index, itemId, itemLookup))
                .ToArray(),
            provenance);
    }

    private static SwShShopInventoryRecord ToInventoryRecord(
        int index,
        int itemId,
        IReadOnlyDictionary<int, SwShItemRecord> itemLookup)
    {
        return itemLookup.TryGetValue(itemId, out var item)
            ? new SwShShopInventoryRecord(index + 1, itemId, item.Name, item.BuyPrice, StockLimit: null)
            : new SwShShopInventoryRecord(index + 1, itemId, $"Item {itemId}", Price: 0, StockLimit: null);
    }

    private static string FormatSingleShopName(ulong hash)
    {
        return KnownSingleShopNames.TryGetValue(hash, out var name)
            ? name
            : $"Single Shop 0x{hash:X16}";
    }

    private static string FormatMultiShopName(ulong hash)
    {
        return KnownMultiShopNames.TryGetValue(hash, out var name)
            ? name
            : $"Multi Shop 0x{hash:X16}";
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
