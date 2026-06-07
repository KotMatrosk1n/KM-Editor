// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Workflows;
using System.Text.Json;

namespace KM.SwSh.Items;

public sealed class SwShItemsWorkflowService
{
    public const string BuyPriceField = "buyPrice";
    public const string SellPriceField = "sellPrice";
    public const int MaximumBuyPrice = 999_999;
    public const int MaximumSellPrice = 999_999;
    public const string ItemsReadModelPath = "romfs/kmeditor/items.readmodel.json";

    private static readonly JsonSerializerOptions ReadModelJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyList<SwShItemEditableField> EditableFields =
    [
        new SwShItemEditableField(
            BuyPriceField,
            "Buy price",
            "integer",
            MinimumValue: 0,
            MaximumBuyPrice),
        new SwShItemEditableField(
            SellPriceField,
            "Sell price",
            "integer",
            MinimumValue: 0,
            MaximumSellPrice),
    ];

    public SwShWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!project.Health.CanOpenReadOnlyWorkflows)
        {
            return CreateSummary(
                SwShWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Items requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShItemsWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);

        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(summary, Array.Empty<SwShItemRecord>(), diagnostics);
        }

        var graphEntry = project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, ItemsReadModelPath, StringComparison.OrdinalIgnoreCase));

        if (graphEntry is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Items data is not available for this project yet.",
                expected: ItemsReadModelPath));
            return CreateWorkflow(summary, Array.Empty<SwShItemRecord>(), diagnostics);
        }

        var sourcePath = ResolveSourcePath(project.Paths, graphEntry);
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Items data source could not be resolved from the project graph.",
                file: graphEntry.RelativePath,
                expected: "Readable Items read model"));
            return CreateWorkflow(summary, Array.Empty<SwShItemRecord>(), diagnostics);
        }

        try
        {
            using var stream = File.OpenRead(sourcePath);
            var readModel = JsonSerializer.Deserialize<ItemsReadModel>(stream, ReadModelJsonOptions);
            var provenance = CreateProvenance(graphEntry);
            var items = readModel?.Items is null
                ? Array.Empty<SwShItemRecord>()
                : readModel.Items
                    .OrderBy(item => item.ItemId)
                    .Select(item => ToItemRecord(item, provenance))
                    .ToArray();

            return CreateWorkflow(summary, items, diagnostics);
        }
        catch (JsonException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Items data source is not valid JSON: {exception.Message}",
                file: graphEntry.RelativePath,
                expected: "Sanitized Items read model JSON"));
            return CreateWorkflow(summary, Array.Empty<SwShItemRecord>(), diagnostics);
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Items data source could not be read: {exception.Message}",
                file: graphEntry.RelativePath,
                expected: "Readable Items read model"));
            return CreateWorkflow(summary, Array.Empty<SwShItemRecord>(), diagnostics);
        }
    }

    private static SwShItemsWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShItemRecord> items,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SwShItemsWorkflow(
            summary,
            items,
            EditableFields,
            new SwShItemsWorkflowStats(items.Count, items.Count > 0 ? 1 : 0),
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

    private static SwShItemProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShItemProvenance(entry.RelativePath, sourceLayer, entry.State);
    }

    private static SwShItemRecord ToItemRecord(ItemReadModelRecord item, SwShItemProvenance provenance)
    {
        return new SwShItemRecord(
            item.ItemId,
            item.Name,
            item.Category,
            item.BuyPrice,
            item.SellPrice,
            provenance);
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.Items,
            "Items",
            "Item records, names, and source provenance.",
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
            Domain: "workflow.items",
            Expected: expected);
    }

    private sealed record ItemsReadModel(
        int SchemaVersion,
        IReadOnlyList<ItemReadModelRecord>? Items);

    private sealed record ItemReadModelRecord(
        int ItemId,
        string Name,
        string Category,
        int BuyPrice,
        int SellPrice);
}
