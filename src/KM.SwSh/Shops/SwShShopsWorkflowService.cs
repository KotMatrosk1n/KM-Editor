// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Workflows;
using System.Text.Json;

namespace KM.SwSh.Shops;

public sealed class SwShShopsWorkflowService
{
    public const string ShopsReadModelPath = "romfs/kmeditor/shops.readmodel.json";

    private static readonly JsonSerializerOptions ReadModelJsonOptions = new(JsonSerializerDefaults.Web);

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
            return CreateWorkflow(summary, Array.Empty<SwShShopRecord>(), diagnostics);
        }

        var graphEntry = project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, ShopsReadModelPath, StringComparison.OrdinalIgnoreCase));

        if (graphEntry is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Shops data is not available for this project yet.",
                expected: ShopsReadModelPath));
            return CreateWorkflow(summary, Array.Empty<SwShShopRecord>(), diagnostics);
        }

        var sourcePath = ResolveSourcePath(project.Paths, graphEntry);
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Shops data source could not be resolved from the project graph.",
                file: graphEntry.RelativePath,
                expected: "Readable Shops read model"));
            return CreateWorkflow(summary, Array.Empty<SwShShopRecord>(), diagnostics);
        }

        try
        {
            using var stream = File.OpenRead(sourcePath);
            var readModel = JsonSerializer.Deserialize<ShopsReadModel>(stream, ReadModelJsonOptions);
            var provenance = CreateProvenance(graphEntry);
            var shops = readModel?.Shops is null
                ? Array.Empty<SwShShopRecord>()
                : readModel.Shops
                    .OrderBy(shop => shop.ShopId, StringComparer.Ordinal)
                    .Select(shop => ToShopRecord(shop, provenance))
                    .ToArray();

            foreach (var duplicateGroup in shops.GroupBy(shop => shop.ShopId).Where(group => group.Count() > 1))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Shop id '{duplicateGroup.Key}' appears more than once in the Shops read model.",
                    file: graphEntry.RelativePath,
                    expected: "Unique shop ids"));
            }

            return CreateWorkflow(summary, shops, diagnostics);
        }
        catch (JsonException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shops data source is not valid JSON: {exception.Message}",
                file: graphEntry.RelativePath,
                expected: "Sanitized Shops read model JSON"));
            return CreateWorkflow(summary, Array.Empty<SwShShopRecord>(), diagnostics);
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shops data source could not be read: {exception.Message}",
                file: graphEntry.RelativePath,
                expected: "Readable Shops read model"));
            return CreateWorkflow(summary, Array.Empty<SwShShopRecord>(), diagnostics);
        }
    }

    private static SwShShopsWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShShopRecord> shops,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SwShShopsWorkflow(
            summary,
            shops,
            new SwShShopsWorkflowStats(
                shops.Count,
                shops.Sum(shop => shop.Inventory.Count),
                shops.Count > 0 ? 1 : 0),
            diagnostics);
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

    private static SwShShopRecord ToShopRecord(
        ShopReadModelRecord shop,
        SwShShopProvenance provenance)
    {
        return new SwShShopRecord(
            shop.ShopId,
            shop.Name,
            shop.Location,
            shop.Currency,
            (shop.Inventory ?? Array.Empty<ShopInventoryReadModelRecord>())
                .OrderBy(item => item.Slot)
                .Select(ToShopInventoryRecord)
                .ToArray(),
            provenance);
    }

    private static SwShShopInventoryRecord ToShopInventoryRecord(ShopInventoryReadModelRecord item)
    {
        return new SwShShopInventoryRecord(
            item.Slot,
            item.ItemId,
            item.ItemName,
            item.Price,
            item.StockLimit);
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.Shops,
            "Shops",
            "Shop inventories, prices, stock limits, and source provenance.",
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
            Domain: "workflow.shops",
            Expected: expected);
    }

    private sealed record ShopsReadModel(
        int SchemaVersion,
        IReadOnlyList<ShopReadModelRecord>? Shops);

    private sealed record ShopReadModelRecord(
        string ShopId,
        string Name,
        string Location,
        string Currency,
        IReadOnlyList<ShopInventoryReadModelRecord>? Inventory);

    private sealed record ShopInventoryReadModelRecord(
        int Slot,
        int ItemId,
        string ItemName,
        int Price,
        int? StockLimit);
}
