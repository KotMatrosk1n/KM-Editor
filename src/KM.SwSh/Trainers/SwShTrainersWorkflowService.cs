// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Workflows;
using System.Text.Json;

namespace KM.SwSh.Trainers;

public sealed class SwShTrainersWorkflowService
{
    public const string TrainersReadModelPath = "romfs/kmeditor/trainers.readmodel.json";

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
                    "Trainers requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShTrainersWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);

        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(summary, Array.Empty<SwShTrainerRecord>(), diagnostics);
        }

        var graphEntry = project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, TrainersReadModelPath, StringComparison.OrdinalIgnoreCase));

        if (graphEntry is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Trainers data is not available for this project yet.",
                expected: TrainersReadModelPath));
            return CreateWorkflow(summary, Array.Empty<SwShTrainerRecord>(), diagnostics);
        }

        var sourcePath = ResolveSourcePath(project.Paths, graphEntry);
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Trainers data source could not be resolved from the project graph.",
                file: graphEntry.RelativePath,
                expected: "Readable Trainers read model"));
            return CreateWorkflow(summary, Array.Empty<SwShTrainerRecord>(), diagnostics);
        }

        try
        {
            using var stream = File.OpenRead(sourcePath);
            var readModel = JsonSerializer.Deserialize<TrainersReadModel>(stream, ReadModelJsonOptions);
            var provenance = CreateProvenance(graphEntry);
            var trainers = readModel?.Trainers is null
                ? Array.Empty<SwShTrainerRecord>()
                : readModel.Trainers
                    .OrderBy(trainer => trainer.TrainerId)
                    .Select(trainer => ToTrainerRecord(trainer, provenance))
                    .ToArray();

            foreach (var duplicateGroup in trainers.GroupBy(trainer => trainer.TrainerId).Where(group => group.Count() > 1))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Trainer id {duplicateGroup.Key} appears more than once in the Trainers read model.",
                    file: graphEntry.RelativePath,
                    expected: "Unique trainer ids"));
            }

            return CreateWorkflow(summary, trainers, diagnostics);
        }
        catch (JsonException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Trainers data source is not valid JSON: {exception.Message}",
                file: graphEntry.RelativePath,
                expected: "Sanitized Trainers read model JSON"));
            return CreateWorkflow(summary, Array.Empty<SwShTrainerRecord>(), diagnostics);
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Trainers data source could not be read: {exception.Message}",
                file: graphEntry.RelativePath,
                expected: "Readable Trainers read model"));
            return CreateWorkflow(summary, Array.Empty<SwShTrainerRecord>(), diagnostics);
        }
    }

    private static SwShTrainersWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShTrainerRecord> trainers,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SwShTrainersWorkflow(
            summary,
            trainers,
            new SwShTrainersWorkflowStats(
                trainers.Count,
                trainers.Sum(trainer => trainer.Team.Count),
                trainers.Count > 0 ? 1 : 0),
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

    private static SwShTrainerProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShTrainerProvenance(entry.RelativePath, sourceLayer, entry.State);
    }

    private static SwShTrainerRecord ToTrainerRecord(
        TrainerReadModelRecord trainer,
        SwShTrainerProvenance provenance)
    {
        return new SwShTrainerRecord(
            trainer.TrainerId,
            trainer.Name,
            trainer.TrainerClass,
            trainer.Location,
            trainer.BattleType,
            (trainer.Team ?? Array.Empty<TrainerPokemonReadModelRecord>())
                .OrderBy(pokemon => pokemon.Slot)
                .Select(ToTrainerPokemonRecord)
                .ToArray(),
            provenance);
    }

    private static SwShTrainerPokemonRecord ToTrainerPokemonRecord(TrainerPokemonReadModelRecord pokemon)
    {
        return new SwShTrainerPokemonRecord(
            pokemon.Slot,
            pokemon.Species,
            pokemon.Level,
            pokemon.HeldItem,
            pokemon.Moves ?? Array.Empty<string>());
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.Trainers,
            "Trainers",
            "Trainer parties, classes, battle types, and source provenance.",
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
            Domain: "workflow.trainers",
            Expected: expected);
    }

    private sealed record TrainersReadModel(
        int SchemaVersion,
        IReadOnlyList<TrainerReadModelRecord>? Trainers);

    private sealed record TrainerReadModelRecord(
        int TrainerId,
        string Name,
        string TrainerClass,
        string Location,
        string BattleType,
        IReadOnlyList<TrainerPokemonReadModelRecord>? Team);

    private sealed record TrainerPokemonReadModelRecord(
        int Slot,
        string Species,
        int Level,
        string? HeldItem,
        IReadOnlyList<string>? Moves);
}
