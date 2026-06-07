// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Workflows;
using System.Text.Json;

namespace KM.SwSh.Flagwork;

public sealed class SwShFlagworkSaveWorkflowService
{
    public const string FlagworkSaveReadModelPath = "romfs/kmeditor/flagwork.save.readmodel.json";

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
                    "Flagwork and Save Inspectors requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShFlagworkSaveWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);

        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(summary, Array.Empty<SwShFlagRecord>(), Array.Empty<SwShSaveBlockRecord>(), diagnostics);
        }

        var graphEntry = project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, FlagworkSaveReadModelPath, StringComparison.OrdinalIgnoreCase));

        if (graphEntry is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Flagwork and Save Inspectors data is not available for this project yet.",
                expected: FlagworkSaveReadModelPath));
            return CreateWorkflow(summary, Array.Empty<SwShFlagRecord>(), Array.Empty<SwShSaveBlockRecord>(), diagnostics);
        }

        var sourcePath = ResolveSourcePath(project.Paths, graphEntry);
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Flagwork and Save Inspectors data source could not be resolved from the project graph.",
                file: graphEntry.RelativePath,
                expected: "Readable Flagwork and Save Inspectors read model"));
            return CreateWorkflow(summary, Array.Empty<SwShFlagRecord>(), Array.Empty<SwShSaveBlockRecord>(), diagnostics);
        }

        try
        {
            using var stream = File.OpenRead(sourcePath);
            var readModel = JsonSerializer.Deserialize<FlagworkSaveReadModel>(stream, ReadModelJsonOptions);
            var provenance = CreateProvenance(graphEntry);
            var flags = readModel?.Flags is null
                ? Array.Empty<SwShFlagRecord>()
                : readModel.Flags
                    .OrderBy(flag => flag.FlagId, StringComparer.Ordinal)
                    .Select(flag => ToFlagRecord(flag, provenance))
                    .ToArray();
            var saveBlocks = readModel?.SaveBlocks is null
                ? Array.Empty<SwShSaveBlockRecord>()
                : readModel.SaveBlocks
                    .OrderBy(saveBlock => saveBlock.BlockId, StringComparer.Ordinal)
                    .Select(saveBlock => ToSaveBlockRecord(saveBlock, provenance))
                    .ToArray();

            foreach (var duplicateGroup in flags.GroupBy(flag => flag.FlagId).Where(group => group.Count() > 1))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Flag id '{duplicateGroup.Key}' appears more than once in the Flagwork and Save Inspectors read model.",
                    file: graphEntry.RelativePath,
                    expected: "Unique flag ids"));
            }

            foreach (var duplicateGroup in saveBlocks.GroupBy(saveBlock => saveBlock.BlockId).Where(group => group.Count() > 1))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Save block id '{duplicateGroup.Key}' appears more than once in the Flagwork and Save Inspectors read model.",
                    file: graphEntry.RelativePath,
                    expected: "Unique save block ids"));
            }

            return CreateWorkflow(summary, flags, saveBlocks, diagnostics);
        }
        catch (JsonException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Flagwork and Save Inspectors data source is not valid JSON: {exception.Message}",
                file: graphEntry.RelativePath,
                expected: "Sanitized Flagwork and Save Inspectors read model JSON"));
            return CreateWorkflow(summary, Array.Empty<SwShFlagRecord>(), Array.Empty<SwShSaveBlockRecord>(), diagnostics);
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Flagwork and Save Inspectors data source could not be read: {exception.Message}",
                file: graphEntry.RelativePath,
                expected: "Readable Flagwork and Save Inspectors read model"));
            return CreateWorkflow(summary, Array.Empty<SwShFlagRecord>(), Array.Empty<SwShSaveBlockRecord>(), diagnostics);
        }
    }

    private static SwShFlagworkSaveWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShFlagRecord> flags,
        IReadOnlyList<SwShSaveBlockRecord> saveBlocks,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SwShFlagworkSaveWorkflow(
            summary,
            flags,
            saveBlocks,
            new SwShFlagworkSaveWorkflowStats(
                flags.Count,
                saveBlocks.Count,
                flags.Count > 0 || saveBlocks.Count > 0 ? 1 : 0),
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

    private static SwShFlagworkSaveProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShFlagworkSaveProvenance(entry.RelativePath, sourceLayer, entry.State);
    }

    private static SwShFlagRecord ToFlagRecord(
        FlagReadModelRecord flag,
        SwShFlagworkSaveProvenance provenance)
    {
        return new SwShFlagRecord(
            flag.FlagId,
            flag.Name,
            flag.Category,
            flag.ValueKind,
            flag.DefaultValue,
            flag.Description,
            provenance);
    }

    private static SwShSaveBlockRecord ToSaveBlockRecord(
        SaveBlockReadModelRecord saveBlock,
        SwShFlagworkSaveProvenance provenance)
    {
        return new SwShSaveBlockRecord(
            saveBlock.BlockId,
            saveBlock.Name,
            saveBlock.Offset,
            saveBlock.Length,
            saveBlock.Description,
            provenance);
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.FlagworkSave,
            "Flagwork and Save Inspectors",
            "Game flags, save blocks, inspector metadata, and source provenance.",
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
            Domain: "workflow.flagworkSave",
            Expected: expected);
    }

    private sealed record FlagworkSaveReadModel(
        int SchemaVersion,
        IReadOnlyList<FlagReadModelRecord>? Flags,
        IReadOnlyList<SaveBlockReadModelRecord>? SaveBlocks);

    private sealed record FlagReadModelRecord(
        string FlagId,
        string Name,
        string Category,
        string ValueKind,
        string DefaultValue,
        string Description);

    private sealed record SaveBlockReadModelRecord(
        string BlockId,
        string Name,
        int Offset,
        int Length,
        string Description);
}
