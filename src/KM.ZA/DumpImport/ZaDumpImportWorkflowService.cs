// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.ZA.Data;
using KM.ZA.Items;
using KM.ZA.Workflows;

namespace KM.ZA.DumpImport;

internal sealed class ZaDumpImportWorkflowService
{
    public const string ItemsPriceProfileId = "za-items-price-csv";
    public const string WorkflowId = ZaWorkflowIds.SpreadsheetImport;

    private readonly ZaItemsWorkflowService itemsWorkflowService;

    public ZaDumpImportWorkflowService(ZaItemsWorkflowService? itemsWorkflowService = null)
    {
        this.itemsWorkflowService = itemsWorkflowService ?? new ZaItemsWorkflowService();
    }

    public ZaWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!project.Health.CanOpenReadOnlyWorkflows)
        {
            return CreateSummary(
                ZaWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Dump Importer requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        return ZaWorkflowSupport.CreateSummary(
            project,
            WorkflowId,
            "Dump Importer",
            "CSV, TSV, and JSON import profiles that execute through backend edit sessions.");
    }

    public ZaDumpImportWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);

        if (summary.Availability == ZaWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(summary, [], sourceFileCount: 0, diagnostics);
        }

        var itemsWorkflow = itemsWorkflowService.Load(project);
        var itemErrors = itemsWorkflow.Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        var itemDataAvailable = itemsWorkflow.Items.Count > 0 && itemErrors.Length == 0;

        if (!itemDataAvailable)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Items price dump import is not executable because item data is not available.",
                expected: ZaDataPaths.ItemDataArray));
        }

        foreach (var diagnostic in itemErrors)
        {
            diagnostics.Add(diagnostic);
        }

        var profile = CreateItemsPriceProfile(
            summary,
            itemDataAvailable,
            itemsWorkflow.Items.FirstOrDefault()?.Provenance);

        return CreateWorkflow(summary, [profile], itemsWorkflow.Stats.SourceFileCount, diagnostics);
    }

    internal static ZaDumpImportProfileRecord CreateItemsPriceProfile(
        ZaWorkflowSummary summary,
        bool itemDataAvailable,
        ZaItemProvenance? itemDataProvenance)
    {
        var status = itemDataAvailable
            ? summary.Availability switch
            {
                ZaWorkflowAvailability.Available => "available",
                ZaWorkflowAvailability.ReadOnly => "readOnly",
                _ => "blocked",
            }
            : "blocked";

        return new ZaDumpImportProfileRecord(
            ItemsPriceProfileId,
            "Z-A Items Price Dump",
            "csv/tsv/json",
            ZaWorkflowIds.Items,
            status,
            "Imports supported Z-A item price dump files into the Items workflow for change-plan review.",
            [
                new ZaDumpImportColumnRecord(1, "ItemId", "integer", IsRequired: true, "Existing item ID."),
                new ZaDumpImportColumnRecord(2, "Price", "integer", IsRequired: false, "New base shop price."),
                new ZaDumpImportColumnRecord(3, "SellPrice", "integer", IsRequired: false, "New sell price. This writes the underlying base price row value."),
                new ZaDumpImportColumnRecord(4, "MegaShardPrice", "integer", IsRequired: false, "New Mega Shard price."),
                new ZaDumpImportColumnRecord(5, "ColorfulScrewPrice", "integer", IsRequired: false, "New Colorful Screw price."),
            ],
            CreateProvenance(itemDataProvenance));
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

    private static ZaDumpImportWorkflow CreateWorkflow(
        ZaWorkflowSummary summary,
        IReadOnlyList<ZaDumpImportProfileRecord> profiles,
        int sourceFileCount,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new ZaDumpImportWorkflow(
            summary,
            profiles,
            new ZaDumpImportWorkflowStats(
                profiles.Count,
                profiles.Sum(profile => profile.Columns.Count),
                sourceFileCount),
            diagnostics);
    }

    private static ZaDumpImportProvenance CreateProvenance(ZaItemProvenance? provenance)
    {
        if (provenance is null)
        {
            return new ZaDumpImportProvenance(
                "backend:za-dump-import-profiles",
                ProjectFileLayer.Generated,
                ProjectFileGraphEntryState.BaseOnly);
        }

        return new ZaDumpImportProvenance(
            provenance.SourceFile,
            provenance.SourceLayer,
            provenance.FileState);
    }

    private static ZaWorkflowSummary CreateSummary(
        ZaWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new ZaWorkflowSummary(
            WorkflowId,
            "Dump Importer",
            "CSV, TSV, and JSON import profiles that execute through backend edit sessions.",
            availability,
            diagnostics);
    }
}
