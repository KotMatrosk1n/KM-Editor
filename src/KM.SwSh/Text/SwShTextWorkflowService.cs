// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Workflows;
using System.Text.Json;

namespace KM.SwSh.Text;

public sealed class SwShTextWorkflowService
{
    public const string TextReadModelPath = "romfs/kmeditor/text.dialogue.readmodel.json";

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
                    "Text and Dialogue Map requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShTextWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);

        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(summary, Array.Empty<SwShTextEntryRecord>(), Array.Empty<SwShDialogueReferenceRecord>(), diagnostics);
        }

        var graphEntry = project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, TextReadModelPath, StringComparison.OrdinalIgnoreCase));

        if (graphEntry is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Text and Dialogue Map data is not available for this project yet.",
                expected: TextReadModelPath));
            return CreateWorkflow(summary, Array.Empty<SwShTextEntryRecord>(), Array.Empty<SwShDialogueReferenceRecord>(), diagnostics);
        }

        var sourcePath = ResolveSourcePath(project.Paths, graphEntry);
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Text and Dialogue Map data source could not be resolved from the project graph.",
                file: graphEntry.RelativePath,
                expected: "Readable Text and Dialogue Map read model"));
            return CreateWorkflow(summary, Array.Empty<SwShTextEntryRecord>(), Array.Empty<SwShDialogueReferenceRecord>(), diagnostics);
        }

        try
        {
            using var stream = File.OpenRead(sourcePath);
            var readModel = JsonSerializer.Deserialize<TextReadModel>(stream, ReadModelJsonOptions);
            var provenance = CreateProvenance(graphEntry);
            var language = string.IsNullOrWhiteSpace(readModel?.Language) ? "unknown" : readModel.Language;
            var entries = readModel?.Entries is null
                ? Array.Empty<SwShTextEntryRecord>()
                : readModel.Entries
                    .OrderBy(entry => entry.TextId)
                    .Select(entry => ToTextEntryRecord(entry, language, provenance))
                    .ToArray();
            var entryIds = entries.Select(entry => entry.TextId).ToHashSet();
            var dialogueReferences = readModel?.DialogueReferences is null
                ? Array.Empty<SwShDialogueReferenceRecord>()
                : readModel.DialogueReferences
                    .OrderBy(reference => reference.DialogueId, StringComparer.Ordinal)
                    .Select(reference => ToDialogueReferenceRecord(reference, provenance))
                    .ToArray();

            foreach (var dialogueReference in dialogueReferences.Where(reference => !entryIds.Contains(reference.TextId)))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Dialogue reference '{dialogueReference.DialogueId}' points to missing text entry {dialogueReference.TextId}.",
                    file: graphEntry.RelativePath,
                    expected: "Dialogue reference text IDs should exist in the text entry table"));
            }

            return CreateWorkflow(summary, entries, dialogueReferences, diagnostics);
        }
        catch (JsonException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Text and Dialogue Map data source is not valid JSON: {exception.Message}",
                file: graphEntry.RelativePath,
                expected: "Sanitized Text and Dialogue Map read model JSON"));
            return CreateWorkflow(summary, Array.Empty<SwShTextEntryRecord>(), Array.Empty<SwShDialogueReferenceRecord>(), diagnostics);
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Text and Dialogue Map data source could not be read: {exception.Message}",
                file: graphEntry.RelativePath,
                expected: "Readable Text and Dialogue Map read model"));
            return CreateWorkflow(summary, Array.Empty<SwShTextEntryRecord>(), Array.Empty<SwShDialogueReferenceRecord>(), diagnostics);
        }
    }

    private static SwShTextWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShTextEntryRecord> entries,
        IReadOnlyList<SwShDialogueReferenceRecord> dialogueReferences,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SwShTextWorkflow(
            summary,
            entries,
            dialogueReferences,
            new SwShTextWorkflowStats(
                entries.Count,
                dialogueReferences.Count,
                entries.Count > 0 || dialogueReferences.Count > 0 ? 1 : 0),
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

    private static SwShTextProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShTextProvenance(entry.RelativePath, sourceLayer, entry.State);
    }

    private static SwShTextEntryRecord ToTextEntryRecord(
        TextReadModelEntry entry,
        string language,
        SwShTextProvenance provenance)
    {
        return new SwShTextEntryRecord(
            entry.TextId,
            entry.Label,
            language,
            entry.Value,
            provenance);
    }

    private static SwShDialogueReferenceRecord ToDialogueReferenceRecord(
        DialogueReferenceReadModelRecord reference,
        SwShTextProvenance provenance)
    {
        return new SwShDialogueReferenceRecord(
            reference.DialogueId,
            reference.Label,
            reference.TextId,
            reference.Context,
            reference.Preview,
            provenance);
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.Text,
            "Text and Dialogue Map",
            "Text entries, dialogue references, and source provenance.",
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
            Domain: "workflow.text",
            Expected: expected);
    }

    private sealed record TextReadModel(
        int SchemaVersion,
        string Language,
        IReadOnlyList<TextReadModelEntry>? Entries,
        IReadOnlyList<DialogueReferenceReadModelRecord>? DialogueReferences);

    private sealed record TextReadModelEntry(
        int TextId,
        string Label,
        string Value);

    private sealed record DialogueReferenceReadModelRecord(
        string DialogueId,
        string Label,
        int TextId,
        string Context,
        string Preview);
}
