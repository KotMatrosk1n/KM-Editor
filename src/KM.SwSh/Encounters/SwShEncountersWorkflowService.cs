// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Workflows;
using System.Text.Json;

namespace KM.SwSh.Encounters;

public sealed class SwShEncountersWorkflowService
{
    public const string EncountersReadModelPath = "romfs/kmeditor/encounters.wild.readmodel.json";

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
                    "Encounters and Wild Data requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShEncountersWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);

        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(summary, Array.Empty<SwShEncounterTableRecord>(), diagnostics);
        }

        var graphEntry = project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, EncountersReadModelPath, StringComparison.OrdinalIgnoreCase));

        if (graphEntry is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Encounters and Wild Data is not available for this project yet.",
                expected: EncountersReadModelPath));
            return CreateWorkflow(summary, Array.Empty<SwShEncounterTableRecord>(), diagnostics);
        }

        var sourcePath = ResolveSourcePath(project.Paths, graphEntry);
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Encounters and Wild Data source could not be resolved from the project graph.",
                file: graphEntry.RelativePath,
                expected: "Readable Encounters and Wild Data read model"));
            return CreateWorkflow(summary, Array.Empty<SwShEncounterTableRecord>(), diagnostics);
        }

        try
        {
            using var stream = File.OpenRead(sourcePath);
            var readModel = JsonSerializer.Deserialize<EncountersReadModel>(stream, ReadModelJsonOptions);
            var provenance = CreateProvenance(graphEntry);
            var tables = readModel?.Tables is null
                ? Array.Empty<SwShEncounterTableRecord>()
                : readModel.Tables
                    .OrderBy(table => table.TableId, StringComparer.Ordinal)
                    .Select(table => ToEncounterTableRecord(table, provenance))
                    .ToArray();

            foreach (var duplicateGroup in tables.GroupBy(table => table.TableId).Where(group => group.Count() > 1))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Encounter table id '{duplicateGroup.Key}' appears more than once in the Encounters and Wild Data read model.",
                    file: graphEntry.RelativePath,
                    expected: "Unique encounter table ids"));
            }

            return CreateWorkflow(summary, tables, diagnostics);
        }
        catch (JsonException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Encounters and Wild Data source is not valid JSON: {exception.Message}",
                file: graphEntry.RelativePath,
                expected: "Sanitized Encounters and Wild Data read model JSON"));
            return CreateWorkflow(summary, Array.Empty<SwShEncounterTableRecord>(), diagnostics);
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Encounters and Wild Data source could not be read: {exception.Message}",
                file: graphEntry.RelativePath,
                expected: "Readable Encounters and Wild Data read model"));
            return CreateWorkflow(summary, Array.Empty<SwShEncounterTableRecord>(), diagnostics);
        }
    }

    private static SwShEncountersWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShEncounterTableRecord> tables,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SwShEncountersWorkflow(
            summary,
            tables,
            new SwShEncountersWorkflowStats(
                tables.Count,
                tables.Sum(table => table.Slots.Count),
                tables.Count > 0 ? 1 : 0),
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

    private static SwShEncounterProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShEncounterProvenance(entry.RelativePath, sourceLayer, entry.State);
    }

    private static SwShEncounterTableRecord ToEncounterTableRecord(
        EncounterTableReadModelRecord table,
        SwShEncounterProvenance provenance)
    {
        return new SwShEncounterTableRecord(
            table.TableId,
            table.Location,
            table.Area,
            table.EncounterType,
            table.GameVersion,
            (table.Slots ?? Array.Empty<EncounterSlotReadModelRecord>())
                .OrderBy(slot => slot.Slot)
                .Select(ToEncounterSlotRecord)
                .ToArray(),
            provenance);
    }

    private static SwShEncounterSlotRecord ToEncounterSlotRecord(EncounterSlotReadModelRecord slot)
    {
        return new SwShEncounterSlotRecord(
            slot.Slot,
            slot.Species,
            slot.LevelMin,
            slot.LevelMax,
            slot.Weight,
            slot.TimeOfDay,
            slot.Weather);
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.Encounters,
            "Encounters and Wild Data",
            "Encounter tables, wild slots, levels, weather, and source provenance.",
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
            Domain: "workflow.encounters",
            Expected: expected);
    }

    private sealed record EncountersReadModel(
        int SchemaVersion,
        IReadOnlyList<EncounterTableReadModelRecord>? Tables);

    private sealed record EncounterTableReadModelRecord(
        string TableId,
        string Location,
        string Area,
        string EncounterType,
        string GameVersion,
        IReadOnlyList<EncounterSlotReadModelRecord>? Slots);

    private sealed record EncounterSlotReadModelRecord(
        int Slot,
        string Species,
        int LevelMin,
        int LevelMax,
        int Weight,
        string? TimeOfDay,
        string Weather);
}
