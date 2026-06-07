// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Items;
using KM.SwSh.Workflows;

namespace KM.SwSh.SpreadsheetImport;

public sealed class SwShSpreadsheetImportWorkflowService
{
    public const string ItemsPriceProfileId = "items-price-csv";

    private readonly SwShItemsWorkflowService itemsWorkflowService;

    public SwShSpreadsheetImportWorkflowService(SwShItemsWorkflowService? itemsWorkflowService = null)
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
                    "Spreadsheet Import requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShSpreadsheetImportWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);

        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(summary, Array.Empty<SwShSpreadsheetImportProfileRecord>(), sourceFileCount: 0, diagnostics);
        }

        var itemsWorkflow = itemsWorkflowService.Load(project);
        var itemDataSource = SwShItemsWorkflowService.ResolveItemDataSource(project);
        var itemErrors = itemsWorkflow.Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        var itemDataAvailable = itemDataSource is not null && itemsWorkflow.Items.Count > 0 && itemErrors.Length == 0;

        if (!itemDataAvailable)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Items price import is not executable because item data is not available.",
                expected: SwShItemsWorkflowService.ItemDataPath));
        }

        foreach (var diagnostic in itemErrors)
        {
            diagnostics.Add(diagnostic);
        }

        var profile = CreateItemsPriceProfile(
            summary,
            itemDataAvailable,
            itemDataSource?.GraphEntry);
        var sourceFileCount = itemDataSource is null ? 0 : 1;

        return CreateWorkflow(summary, [profile], sourceFileCount, diagnostics);
    }

    internal static SwShSpreadsheetImportProfileRecord CreateItemsPriceProfile(
        SwShWorkflowSummary summary,
        bool itemDataAvailable,
        ProjectFileGraphEntry? itemDataEntry)
    {
        var status = itemDataAvailable
            ? summary.Availability switch
            {
                SwShWorkflowAvailability.Available => "available",
                SwShWorkflowAvailability.ReadOnly => "readOnly",
                _ => "blocked",
            }
            : "blocked";

        return new SwShSpreadsheetImportProfileRecord(
            ItemsPriceProfileId,
            "Items Price CSV/TSV",
            "csv/tsv",
            SwShWorkflowIds.Items,
            status,
            "Imports item price columns into the Items workflow for change-plan review.",
            [
                new SwShSpreadsheetImportColumnRecord(
                    1,
                    "ItemId",
                    "integer",
                    IsRequired: true,
                    "Existing item ID."),
                new SwShSpreadsheetImportColumnRecord(
                    2,
                    "BuyPrice",
                    "integer",
                    IsRequired: false,
                    "New buy price. Sell price is derived from this stored value."),
                new SwShSpreadsheetImportColumnRecord(
                    3,
                    "SellPrice",
                    "integer",
                    IsRequired: false,
                    "New sell price. This writes the underlying buy-price row value."),
                new SwShSpreadsheetImportColumnRecord(
                    4,
                    "WattsPrice",
                    "integer",
                    IsRequired: false,
                    "New Watts price."),
                new SwShSpreadsheetImportColumnRecord(
                    5,
                    "AlternatePrice",
                    "integer",
                    IsRequired: false,
                    "New alternate price."),
            ],
            CreateProvenance(itemDataEntry));
    }

    private static SwShSpreadsheetImportWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShSpreadsheetImportProfileRecord> profiles,
        int sourceFileCount,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SwShSpreadsheetImportWorkflow(
            summary,
            profiles,
            new SwShSpreadsheetImportWorkflowStats(
                profiles.Count,
                profiles.Sum(profile => profile.Columns.Count),
                sourceFileCount),
            diagnostics);
    }

    private static SwShSpreadsheetImportProvenance CreateProvenance(ProjectFileGraphEntry? entry)
    {
        if (entry is null)
        {
            return new SwShSpreadsheetImportProvenance(
                "backend:spreadsheet-import-profiles",
                ProjectFileLayer.Generated,
                ProjectFileGraphEntryState.BaseOnly);
        }

        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShSpreadsheetImportProvenance(entry.RelativePath, sourceLayer, entry.State);
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.SpreadsheetImport,
            "Spreadsheet Import",
            "CSV and TSV import profiles that execute through backend edit sessions.",
            availability,
            diagnostics);
    }

    internal static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? file = null,
        string? field = null,
        string? expected = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Domain: "workflow.spreadsheetImport",
            Field: field,
            Expected: expected);
    }
}
