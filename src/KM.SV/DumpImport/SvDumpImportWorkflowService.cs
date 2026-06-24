// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SV.Data;
using KM.SV.Items;
using KM.SV.Workflows;

namespace KM.SV.DumpImport;

internal sealed class SvDumpImportWorkflowService
{
    public const string ItemsPriceProfileId = "items-price-csv";
    public const string WorkflowId = SvWorkflowIds.SpreadsheetImport;

    private readonly SvItemsWorkflowService itemsWorkflowService;

    public SvDumpImportWorkflowService(SvItemsWorkflowService? itemsWorkflowService = null)
    {
        this.itemsWorkflowService = itemsWorkflowService ?? new SvItemsWorkflowService();
    }

    public SvWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!project.Health.CanOpenReadOnlyWorkflows)
        {
            return CreateSummary(
                SvWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Dump Importer requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        return SvWorkflowSupport.CreateSummary(
            project,
            WorkflowId,
            "Dump Importer",
            "CSV, TSV, and JSON import profiles that execute through backend edit sessions.");
    }

    public SvDumpImportWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);

        if (summary.Availability == SvWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(summary, Array.Empty<SvDumpImportProfileRecord>(), sourceFileCount: 0, diagnostics);
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
                expected: SvDataPaths.ItemDataArray));
        }

        foreach (var diagnostic in itemErrors)
        {
            diagnostics.Add(diagnostic);
        }

        var profile = CreateItemsPriceProfile(
            summary,
            itemDataAvailable,
            itemsWorkflow.Items.FirstOrDefault()?.Provenance);
        var sourceFileCount = itemsWorkflow.Stats.SourceFileCount;

        return CreateWorkflow(summary, [profile], sourceFileCount, diagnostics);
    }

    internal static SvDumpImportProfileRecord CreateItemsPriceProfile(
        SvWorkflowSummary summary,
        bool itemDataAvailable,
        SvItemProvenance? itemDataProvenance)
    {
        var status = itemDataAvailable
            ? summary.Availability switch
            {
                SvWorkflowAvailability.Available => "available",
                SvWorkflowAvailability.ReadOnly => "readOnly",
                _ => "blocked",
            }
            : "blocked";

        return new SvDumpImportProfileRecord(
            ItemsPriceProfileId,
            "Items Price Dump",
            "csv/tsv/json",
            SvWorkflowIds.Items,
            status,
            "Imports supported item price dump files into the Items workflow for change-plan review.",
            [
                new SvDumpImportColumnRecord(
                    1,
                    "ItemId",
                    "integer",
                    IsRequired: true,
                    "Existing item ID."),
                new SvDumpImportColumnRecord(
                    2,
                    "BuyPrice",
                    "integer",
                    IsRequired: false,
                    "New buy price."),
                new SvDumpImportColumnRecord(
                    3,
                    "SellPrice",
                    "integer",
                    IsRequired: false,
                    "New sell price. This writes the underlying buy-price row value."),
                new SvDumpImportColumnRecord(
                    4,
                    "WattsPrice",
                    "integer",
                    IsRequired: false,
                    "New Watts price."),
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

    private static SvDumpImportWorkflow CreateWorkflow(
        SvWorkflowSummary summary,
        IReadOnlyList<SvDumpImportProfileRecord> profiles,
        int sourceFileCount,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SvDumpImportWorkflow(
            summary,
            profiles,
            new SvDumpImportWorkflowStats(
                profiles.Count,
                profiles.Sum(profile => profile.Columns.Count),
                sourceFileCount),
            diagnostics);
    }

    private static SvDumpImportProvenance CreateProvenance(SvItemProvenance? provenance)
    {
        if (provenance is null)
        {
            return new SvDumpImportProvenance(
                "backend:dump-import-profiles",
                ProjectFileLayer.Generated,
                ProjectFileGraphEntryState.BaseOnly);
        }

        return new SvDumpImportProvenance(
            provenance.SourceFile,
            provenance.SourceLayer,
            provenance.FileState);
    }

    private static SvWorkflowSummary CreateSummary(
        SvWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SvWorkflowSummary(
            WorkflowId,
            "Dump Importer",
            "CSV, TSV, and JSON import profiles that execute through backend edit sessions.",
            availability,
            diagnostics);
    }
}
