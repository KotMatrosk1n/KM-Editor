// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Workflows;
using System.Text.Json;

namespace KM.SwSh.Placement;

public sealed class SwShPlacementWorkflowService
{
    public const string PlacementReadModelPath = "romfs/kmeditor/placement.readmodel.json";

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
                    "Placement requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShPlacementWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);

        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(summary, Array.Empty<SwShPlacedObjectRecord>(), diagnostics);
        }

        var graphEntry = project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, PlacementReadModelPath, StringComparison.OrdinalIgnoreCase));

        if (graphEntry is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Placement data is not available for this project yet.",
                expected: PlacementReadModelPath));
            return CreateWorkflow(summary, Array.Empty<SwShPlacedObjectRecord>(), diagnostics);
        }

        var sourcePath = ResolveSourcePath(project.Paths, graphEntry);
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Placement data source could not be resolved from the project graph.",
                file: graphEntry.RelativePath,
                expected: "Readable Placement read model"));
            return CreateWorkflow(summary, Array.Empty<SwShPlacedObjectRecord>(), diagnostics);
        }

        try
        {
            using var stream = File.OpenRead(sourcePath);
            var readModel = JsonSerializer.Deserialize<PlacementReadModel>(stream, ReadModelJsonOptions);
            var provenance = CreateProvenance(graphEntry);
            var objects = readModel?.Objects is null
                ? Array.Empty<SwShPlacedObjectRecord>()
                : readModel.Objects
                    .OrderBy(placedObject => placedObject.ObjectId, StringComparer.Ordinal)
                    .Select(placedObject => ToPlacedObjectRecord(placedObject, provenance))
                    .ToArray();

            foreach (var duplicateGroup in objects.GroupBy(placedObject => placedObject.ObjectId).Where(group => group.Count() > 1))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Placed object id '{duplicateGroup.Key}' appears more than once in the Placement read model.",
                    file: graphEntry.RelativePath,
                    expected: "Unique placed object ids"));
            }

            return CreateWorkflow(summary, objects, diagnostics);
        }
        catch (JsonException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Placement data source is not valid JSON: {exception.Message}",
                file: graphEntry.RelativePath,
                expected: "Sanitized Placement read model JSON"));
            return CreateWorkflow(summary, Array.Empty<SwShPlacedObjectRecord>(), diagnostics);
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Placement data source could not be read: {exception.Message}",
                file: graphEntry.RelativePath,
                expected: "Readable Placement read model"));
            return CreateWorkflow(summary, Array.Empty<SwShPlacedObjectRecord>(), diagnostics);
        }
    }

    private static SwShPlacementWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShPlacedObjectRecord> objects,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SwShPlacementWorkflow(
            summary,
            objects,
            new SwShPlacementWorkflowStats(
                objects.Count,
                objects.Count > 0 ? 1 : 0),
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

    private static SwShPlacementProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShPlacementProvenance(entry.RelativePath, sourceLayer, entry.State);
    }

    private static SwShPlacedObjectRecord ToPlacedObjectRecord(
        PlacedObjectReadModelRecord placedObject,
        SwShPlacementProvenance provenance)
    {
        return new SwShPlacedObjectRecord(
            placedObject.ObjectId,
            placedObject.ObjectType,
            placedObject.Label,
            placedObject.Map,
            placedObject.X,
            placedObject.Y,
            placedObject.Z,
            placedObject.RotationY,
            placedObject.ScriptId,
            provenance);
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.Placement,
            "Placement",
            "Placed objects, map coordinates, script links, and source provenance.",
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
            Domain: "workflow.placement",
            Expected: expected);
    }

    private sealed record PlacementReadModel(
        int SchemaVersion,
        IReadOnlyList<PlacedObjectReadModelRecord>? Objects);

    private sealed record PlacedObjectReadModelRecord(
        string ObjectId,
        string ObjectType,
        string Label,
        string Map,
        double X,
        double Y,
        double Z,
        double RotationY,
        string? ScriptId);
}
