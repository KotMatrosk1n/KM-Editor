// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Workflows;
using System.Text.Json;

namespace KM.SwSh.RoyalCandy;

public sealed class SwShRoyalCandyWorkflowService
{
    public const string RoyalCandyReadModelPath = "romfs/kmeditor/royal-candy.workflows.readmodel.json";

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
                    "Royal Candy Workflows requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShRoyalCandyWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);

        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(summary, Array.Empty<SwShRoyalCandyWorkflowRecord>(), diagnostics);
        }

        var graphEntry = project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, RoyalCandyReadModelPath, StringComparison.OrdinalIgnoreCase));

        if (graphEntry is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Royal Candy Workflows data is not available for this project yet.",
                expected: RoyalCandyReadModelPath));
            return CreateWorkflow(summary, Array.Empty<SwShRoyalCandyWorkflowRecord>(), diagnostics);
        }

        var sourcePath = ResolveSourcePath(project.Paths, graphEntry);
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Royal Candy Workflows data source could not be resolved from the project graph.",
                file: graphEntry.RelativePath,
                expected: "Readable Royal Candy Workflows read model"));
            return CreateWorkflow(summary, Array.Empty<SwShRoyalCandyWorkflowRecord>(), diagnostics);
        }

        try
        {
            using var stream = File.OpenRead(sourcePath);
            var readModel = JsonSerializer.Deserialize<RoyalCandyReadModel>(stream, ReadModelJsonOptions);
            var provenance = CreateProvenance(graphEntry);
            var workflows = readModel?.Workflows is null
                ? Array.Empty<SwShRoyalCandyWorkflowRecord>()
                : readModel.Workflows
                    .OrderBy(workflow => workflow.WorkflowId, StringComparer.Ordinal)
                    .Select(workflow => ToWorkflowRecord(workflow, provenance))
                    .ToArray();

            foreach (var duplicateGroup in workflows.GroupBy(workflow => workflow.WorkflowId).Where(group => group.Count() > 1))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Royal Candy workflow id '{duplicateGroup.Key}' appears more than once in the read model.",
                    file: graphEntry.RelativePath,
                    expected: "Unique Royal Candy workflow ids"));
            }

            return CreateWorkflow(summary, workflows, diagnostics);
        }
        catch (JsonException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Royal Candy Workflows data source is not valid JSON: {exception.Message}",
                file: graphEntry.RelativePath,
                expected: "Sanitized Royal Candy Workflows read model JSON"));
            return CreateWorkflow(summary, Array.Empty<SwShRoyalCandyWorkflowRecord>(), diagnostics);
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Royal Candy Workflows data source could not be read: {exception.Message}",
                file: graphEntry.RelativePath,
                expected: "Readable Royal Candy Workflows read model"));
            return CreateWorkflow(summary, Array.Empty<SwShRoyalCandyWorkflowRecord>(), diagnostics);
        }
    }

    private static SwShRoyalCandyWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShRoyalCandyWorkflowRecord> workflows,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SwShRoyalCandyWorkflow(
            summary,
            workflows,
            new SwShRoyalCandyWorkflowStats(
                workflows.Count,
                workflows.Sum(workflow => workflow.Steps.Count),
                workflows.Count > 0 ? 1 : 0),
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

    private static SwShRoyalCandyProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShRoyalCandyProvenance(entry.RelativePath, sourceLayer, entry.State);
    }

    private static SwShRoyalCandyWorkflowRecord ToWorkflowRecord(
        RoyalCandyWorkflowReadModelRecord workflow,
        SwShRoyalCandyProvenance provenance)
    {
        return new SwShRoyalCandyWorkflowRecord(
            workflow.WorkflowId,
            workflow.Name,
            workflow.Category,
            workflow.Target,
            workflow.Status,
            workflow.Description,
            (workflow.Steps ?? Array.Empty<RoyalCandyWorkflowStepReadModelRecord>())
                .OrderBy(step => step.Step)
                .Select(ToWorkflowStepRecord)
                .ToArray(),
            provenance);
    }

    private static SwShRoyalCandyWorkflowStepRecord ToWorkflowStepRecord(RoyalCandyWorkflowStepReadModelRecord step)
    {
        return new SwShRoyalCandyWorkflowStepRecord(
            step.Step,
            step.Label,
            step.Description);
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.RoyalCandy,
            "Royal Candy Workflows",
            "Curated batch workflow recipes, targets, steps, and source provenance.",
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
            Domain: "workflow.royalCandy",
            Expected: expected);
    }

    private sealed record RoyalCandyReadModel(
        int SchemaVersion,
        IReadOnlyList<RoyalCandyWorkflowReadModelRecord>? Workflows);

    private sealed record RoyalCandyWorkflowReadModelRecord(
        string WorkflowId,
        string Name,
        string Category,
        string Target,
        string Status,
        string Description,
        IReadOnlyList<RoyalCandyWorkflowStepReadModelRecord>? Steps);

    private sealed record RoyalCandyWorkflowStepReadModelRecord(
        int Step,
        string Label,
        string Description);
}
