// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Workflows;
using System.Text.Json;

namespace KM.SwSh.SpreadsheetImport;

public sealed class SwShSpreadsheetImportWorkflowService
{
    public const string SpreadsheetImportReadModelPath = "romfs/kmeditor/spreadsheet-import.profiles.readmodel.json";

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
                    "Spreadsheet Import Tooling requires valid base RomFS and base ExeFS paths before it can load.",
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
            return CreateWorkflow(summary, Array.Empty<SwShSpreadsheetImportProfileRecord>(), diagnostics);
        }

        var graphEntry = project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, SpreadsheetImportReadModelPath, StringComparison.OrdinalIgnoreCase));

        if (graphEntry is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Spreadsheet Import Tooling data is not available for this project yet.",
                expected: SpreadsheetImportReadModelPath));
            return CreateWorkflow(summary, Array.Empty<SwShSpreadsheetImportProfileRecord>(), diagnostics);
        }

        var sourcePath = ResolveSourcePath(project.Paths, graphEntry);
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Spreadsheet Import Tooling data source could not be resolved from the project graph.",
                file: graphEntry.RelativePath,
                expected: "Readable Spreadsheet Import Tooling read model"));
            return CreateWorkflow(summary, Array.Empty<SwShSpreadsheetImportProfileRecord>(), diagnostics);
        }

        try
        {
            using var stream = File.OpenRead(sourcePath);
            var readModel = JsonSerializer.Deserialize<SpreadsheetImportReadModel>(stream, ReadModelJsonOptions);
            var provenance = CreateProvenance(graphEntry);
            var profiles = readModel?.Profiles is null
                ? Array.Empty<SwShSpreadsheetImportProfileRecord>()
                : readModel.Profiles
                    .OrderBy(profile => profile.ProfileId, StringComparer.Ordinal)
                    .Select(profile => ToProfileRecord(profile, provenance))
                    .ToArray();

            foreach (var duplicateGroup in profiles.GroupBy(profile => profile.ProfileId).Where(group => group.Count() > 1))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Spreadsheet import profile id '{duplicateGroup.Key}' appears more than once in the read model.",
                    file: graphEntry.RelativePath,
                    expected: "Unique Spreadsheet Import Tooling profile ids"));
            }

            return CreateWorkflow(summary, profiles, diagnostics);
        }
        catch (JsonException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Spreadsheet Import Tooling data source is not valid JSON: {exception.Message}",
                file: graphEntry.RelativePath,
                expected: "Sanitized Spreadsheet Import Tooling read model JSON"));
            return CreateWorkflow(summary, Array.Empty<SwShSpreadsheetImportProfileRecord>(), diagnostics);
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Spreadsheet Import Tooling data source could not be read: {exception.Message}",
                file: graphEntry.RelativePath,
                expected: "Readable Spreadsheet Import Tooling read model"));
            return CreateWorkflow(summary, Array.Empty<SwShSpreadsheetImportProfileRecord>(), diagnostics);
        }
    }

    private static SwShSpreadsheetImportWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShSpreadsheetImportProfileRecord> profiles,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SwShSpreadsheetImportWorkflow(
            summary,
            profiles,
            new SwShSpreadsheetImportWorkflowStats(
                profiles.Count,
                profiles.Sum(profile => profile.Columns.Count),
                profiles.Count > 0 ? 1 : 0),
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

    private static SwShSpreadsheetImportProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShSpreadsheetImportProvenance(entry.RelativePath, sourceLayer, entry.State);
    }

    private static SwShSpreadsheetImportProfileRecord ToProfileRecord(
        SpreadsheetImportProfileReadModelRecord profile,
        SwShSpreadsheetImportProvenance provenance)
    {
        return new SwShSpreadsheetImportProfileRecord(
            profile.ProfileId,
            profile.Name,
            profile.SourceKind,
            profile.TargetWorkflow,
            profile.Status,
            profile.Description,
            (profile.Columns ?? Array.Empty<SpreadsheetImportColumnReadModelRecord>())
                .OrderBy(column => column.Column)
                .Select(ToColumnRecord)
                .ToArray(),
            provenance);
    }

    private static SwShSpreadsheetImportColumnRecord ToColumnRecord(
        SpreadsheetImportColumnReadModelRecord column)
    {
        return new SwShSpreadsheetImportColumnRecord(
            column.Column,
            column.Header,
            column.ValueKind,
            column.IsRequired,
            column.Description);
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.SpreadsheetImport,
            "Spreadsheet Import Tooling",
            "Spreadsheet import profiles, target workflows, columns, and source provenance.",
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
            Domain: "workflow.spreadsheetImport",
            Expected: expected);
    }

    private sealed record SpreadsheetImportReadModel(
        int SchemaVersion,
        IReadOnlyList<SpreadsheetImportProfileReadModelRecord>? Profiles);

    private sealed record SpreadsheetImportProfileReadModelRecord(
        string ProfileId,
        string Name,
        string SourceKind,
        string TargetWorkflow,
        string Status,
        string Description,
        IReadOnlyList<SpreadsheetImportColumnReadModelRecord>? Columns);

    private sealed record SpreadsheetImportColumnReadModelRecord(
        int Column,
        string Header,
        string ValueKind,
        bool IsRequired,
        string Description);
}
