// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Workflows;
using System.Text.Json;

namespace KM.SwSh.ExeFs;

public sealed class SwShExeFsPatchWorkflowService
{
    public const string ExeFsPatchReadModelPath = "exefs/kmeditor/exefs.patches.readmodel.json";

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
                    "ExeFS Patch Manager requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShExeFsPatchWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);

        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(summary, Array.Empty<SwShExeFsPatchRecord>(), diagnostics);
        }

        var graphEntry = project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, ExeFsPatchReadModelPath, StringComparison.OrdinalIgnoreCase));

        if (graphEntry is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "ExeFS Patch Manager data is not available for this project yet.",
                expected: ExeFsPatchReadModelPath));
            return CreateWorkflow(summary, Array.Empty<SwShExeFsPatchRecord>(), diagnostics);
        }

        var sourcePath = ResolveSourcePath(project.Paths, graphEntry);
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "ExeFS Patch Manager data source could not be resolved from the project graph.",
                file: graphEntry.RelativePath,
                expected: "Readable ExeFS Patch Manager read model"));
            return CreateWorkflow(summary, Array.Empty<SwShExeFsPatchRecord>(), diagnostics);
        }

        try
        {
            using var stream = File.OpenRead(sourcePath);
            var readModel = JsonSerializer.Deserialize<ExeFsPatchReadModel>(stream, ReadModelJsonOptions);
            var provenance = CreateProvenance(graphEntry);
            var patches = readModel?.Patches is null
                ? Array.Empty<SwShExeFsPatchRecord>()
                : readModel.Patches
                    .OrderBy(patch => patch.PatchId, StringComparer.Ordinal)
                    .Select(patch => ToPatchRecord(patch, provenance))
                    .ToArray();

            foreach (var duplicateGroup in patches.GroupBy(patch => patch.PatchId).Where(group => group.Count() > 1))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"ExeFS patch id '{duplicateGroup.Key}' appears more than once in the ExeFS Patch Manager read model.",
                    file: graphEntry.RelativePath,
                    expected: "Unique ExeFS patch ids"));
            }

            return CreateWorkflow(summary, patches, diagnostics);
        }
        catch (JsonException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"ExeFS Patch Manager data source is not valid JSON: {exception.Message}",
                file: graphEntry.RelativePath,
                expected: "Sanitized ExeFS Patch Manager read model JSON"));
            return CreateWorkflow(summary, Array.Empty<SwShExeFsPatchRecord>(), diagnostics);
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"ExeFS Patch Manager data source could not be read: {exception.Message}",
                file: graphEntry.RelativePath,
                expected: "Readable ExeFS Patch Manager read model"));
            return CreateWorkflow(summary, Array.Empty<SwShExeFsPatchRecord>(), diagnostics);
        }
    }

    private static SwShExeFsPatchWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShExeFsPatchRecord> patches,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SwShExeFsPatchWorkflow(
            summary,
            patches,
            new SwShExeFsPatchWorkflowStats(
                patches.Count,
                patches.Count > 0 ? 1 : 0),
            diagnostics);
    }

    private static string? ResolveSourcePath(ProjectPaths paths, ProjectFileGraphEntry entry)
    {
        if (entry.LayeredFile is not null && !string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            return CombineGraphPath(paths.OutputRootPath, entry.RelativePath);
        }

        if (entry.BaseFile is not null && entry.RelativePath.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineGraphPath(paths.BaseExeFsPath, entry.RelativePath["exefs/".Length..]);
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

    private static SwShExeFsPatchProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShExeFsPatchProvenance(entry.RelativePath, sourceLayer, entry.State);
    }

    private static SwShExeFsPatchRecord ToPatchRecord(
        ExeFsPatchReadModelRecord patch,
        SwShExeFsPatchProvenance provenance)
    {
        return new SwShExeFsPatchRecord(
            patch.PatchId,
            patch.Name,
            patch.TargetFile,
            patch.PatchKind,
            patch.Status,
            patch.Description,
            provenance);
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.ExeFsPatches,
            "ExeFS Patch Manager",
            "ExeFS patch definitions, target files, statuses, and source provenance.",
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
            Domain: "workflow.exefsPatches",
            Expected: expected);
    }

    private sealed record ExeFsPatchReadModel(
        int SchemaVersion,
        IReadOnlyList<ExeFsPatchReadModelRecord>? Patches);

    private sealed record ExeFsPatchReadModelRecord(
        string PatchId,
        string Name,
        string TargetFile,
        string PatchKind,
        string Status,
        string Description);
}
