// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Workflows;
using System.Globalization;
using System.Text.RegularExpressions;

namespace KM.SwSh.Trainers;

public sealed class SwShTrainersWorkflowService
{
    public const string TrainerClassIdField = "trainerClassId";
    public const string BattleTypeField = "battleType";
    public const string SpeciesIdField = "speciesId";
    public const string LevelField = "level";
    public const string HeldItemIdField = "heldItemId";
    public const string Move1IdField = "move1Id";
    public const string Move2IdField = "move2Id";
    public const string Move3IdField = "move3Id";
    public const string Move4IdField = "move4Id";
    public const string TrainerDataRootPath = SwShTrainerDataFile.TrainerDataRootRelativePath;
    public const string TrainerPokeRootPath = SwShTrainerTeamFile.TrainerPokeRootRelativePath;
    public const string PreferredLanguage = "English";

    private const string MessageRootPath = "romfs/bin/message";

    private static readonly Regex DigitsRegex = new("(\\d+)(?!.*\\d)", RegexOptions.Compiled);

    private static readonly IReadOnlyList<SwShTrainerEditableField> EditableFields =
    [
        new SwShTrainerEditableField(
            TrainerClassIdField,
            "Trainer class ID",
            "integer",
            0,
            SwShTrainerDataFile.MaximumClassId),
        new SwShTrainerEditableField(
            BattleTypeField,
            "Battle type",
            "integer",
            0,
            SwShTrainerDataFile.MaximumBattleMode),
        new SwShTrainerEditableField(
            SpeciesIdField,
            "Species ID",
            "integer",
            0,
            SwShTrainerTeamFile.MaximumPokemonId),
        new SwShTrainerEditableField(
            LevelField,
            "Level",
            "integer",
            SwShTrainerTeamFile.MinimumLevel,
            SwShTrainerTeamFile.MaximumLevel),
        new SwShTrainerEditableField(
            HeldItemIdField,
            "Held item ID",
            "integer",
            0,
            SwShTrainerTeamFile.MaximumItemId),
        new SwShTrainerEditableField(
            Move1IdField,
            "Move 1 ID",
            "integer",
            0,
            SwShTrainerTeamFile.MaximumMoveId),
        new SwShTrainerEditableField(
            Move2IdField,
            "Move 2 ID",
            "integer",
            0,
            SwShTrainerTeamFile.MaximumMoveId),
        new SwShTrainerEditableField(
            Move3IdField,
            "Move 3 ID",
            "integer",
            0,
            SwShTrainerTeamFile.MaximumMoveId),
        new SwShTrainerEditableField(
            Move4IdField,
            "Move 4 ID",
            "integer",
            0,
            SwShTrainerTeamFile.MaximumMoveId),
    ];

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
            return CreateWorkflow(summary, Array.Empty<SwShTrainerRecord>(), diagnostics, sourceFileCount: 0);
        }

        var trainerDataSources = ResolveTrainerFolder(project, TrainerDataRootPath);
        var trainerPokeSources = ResolveTrainerFolder(project, TrainerPokeRootPath);
        if (trainerDataSources.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Trainers did not find Sword/Shield trainer data files.",
                expected: $"{TrainerDataRootPath}/**/*"));
        }

        if (trainerPokeSources.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Trainers did not find Sword/Shield trainer party files.",
                expected: $"{TrainerPokeRootPath}/**/*"));
        }

        if (trainerDataSources.Count == 0)
        {
            return CreateWorkflow(summary, Array.Empty<SwShTrainerRecord>(), diagnostics, sourceFileCount: 0);
        }

        var names = LoadLookupTables(project, diagnostics);
        var pokeSourcesByTrainerId = trainerPokeSources
            .GroupBy(source => source.TrainerId)
            .ToDictionary(group => group.Key, group => group.First());
        var trainers = new List<SwShTrainerRecord>();
        var parsedSourceFileCount = 0;

        foreach (var dataSource in trainerDataSources)
        {
            if (!pokeSourcesByTrainerId.TryGetValue(dataSource.TrainerId, out var pokeSource))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Trainer {dataSource.TrainerId} does not have a matching party file.",
                    file: dataSource.Entry.RelativePath,
                    expected: "Matching trainer_poke file"));
                continue;
            }

            try
            {
                var dataFile = SwShTrainerDataFile.Parse(File.ReadAllBytes(dataSource.AbsolutePath));
                var teamFile = SwShTrainerTeamFile.Parse(File.ReadAllBytes(pokeSource.AbsolutePath));
                parsedSourceFileCount += 2;

                if (dataFile.Record.PokemonCount != teamFile.Records.Count)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Warning,
                        $"Trainer {dataSource.TrainerId} data declares {dataFile.Record.PokemonCount} Pokemon but the party file contains {teamFile.Records.Count}.",
                        file: dataSource.Entry.RelativePath,
                        expected: "Trainer data Pokemon count matching party rows"));
                }

                trainers.Add(ToTrainerRecord(dataSource, pokeSource, dataFile.Record, teamFile.Records, names));
            }
            catch (InvalidDataException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Trainer {dataSource.TrainerId} could not be decoded: {exception.Message}",
                    file: dataSource.Entry.RelativePath,
                    expected: "Sword/Shield trainer data and party files"));
            }
            catch (IOException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Trainer {dataSource.TrainerId} could not be read: {exception.Message}",
                    file: dataSource.Entry.RelativePath,
                    expected: "Readable trainer data and party files"));
            }
            catch (UnauthorizedAccessException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Trainer {dataSource.TrainerId} could not be read: {exception.Message}",
                    file: dataSource.Entry.RelativePath,
                    expected: "Readable trainer data and party files"));
            }
        }

        return CreateWorkflow(summary, trainers.OrderBy(trainer => trainer.TrainerId).ToArray(), diagnostics, parsedSourceFileCount);
    }

    internal static WorkflowFileSource? ResolveWorkflowFile(OpenedProject project, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var entry = project.FileGraph.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return null;
        }

        var sourcePath = ResolveSourcePath(project.Paths, entry);
        return sourcePath is null || !File.Exists(sourcePath)
            ? null
            : new WorkflowFileSource(GetTrainerId(entry.RelativePath, 0), entry, sourcePath);
    }

    internal static string? ResolveOutputPath(ProjectPaths paths, string targetRelativePath)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRelativePath);

        if (string.IsNullOrWhiteSpace(paths.OutputRootPath) || Path.IsPathRooted(targetRelativePath))
        {
            return null;
        }

        var normalizedRelativePath = targetRelativePath.Replace('/', Path.DirectorySeparatorChar);
        var outputRoot = Path.GetFullPath(paths.OutputRootPath);
        var targetPath = Path.GetFullPath(Path.Combine(outputRoot, normalizedRelativePath));
        var pathFromOutputRoot = Path.GetRelativePath(outputRoot, targetPath);

        return !pathFromOutputRoot.StartsWith("..", StringComparison.Ordinal)
            && !Path.IsPathRooted(pathFromOutputRoot)
            ? targetPath
            : null;
    }

    internal static bool IsTrainerDataField(string? field)
    {
        return string.Equals(field, TrainerClassIdField, StringComparison.Ordinal)
            || string.Equals(field, BattleTypeField, StringComparison.Ordinal);
    }

    internal static bool IsTrainerPokemonField(string? field)
    {
        return string.Equals(field, SpeciesIdField, StringComparison.Ordinal)
            || string.Equals(field, LevelField, StringComparison.Ordinal)
            || string.Equals(field, HeldItemIdField, StringComparison.Ordinal)
            || string.Equals(field, Move1IdField, StringComparison.Ordinal)
            || string.Equals(field, Move2IdField, StringComparison.Ordinal)
            || string.Equals(field, Move3IdField, StringComparison.Ordinal)
            || string.Equals(field, Move4IdField, StringComparison.Ordinal);
    }

    internal static SwShTrainerEditableField? GetEditableField(string? field)
    {
        return EditableFields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, field, StringComparison.Ordinal));
    }

    internal static string CreateTeamRecordId(int trainerId, int slot)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{trainerId}:{slot}");
    }

    internal static bool TryParseTeamRecordId(string? recordId, out int trainerId, out int slot)
    {
        trainerId = -1;
        slot = -1;

        if (string.IsNullOrWhiteSpace(recordId))
        {
            return false;
        }

        var separatorIndex = recordId.IndexOf(':', StringComparison.Ordinal);
        if (separatorIndex <= 0 || separatorIndex == recordId.Length - 1)
        {
            return false;
        }

        return int.TryParse(recordId[..separatorIndex], NumberStyles.None, CultureInfo.InvariantCulture, out trainerId)
            && int.TryParse(recordId[(separatorIndex + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out slot)
            && trainerId >= 0
            && slot >= 1;
    }

    private static SwShTrainersWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShTrainerRecord> trainers,
        IReadOnlyList<ValidationDiagnostic> diagnostics,
        int sourceFileCount)
    {
        return new SwShTrainersWorkflow(
            summary,
            trainers,
            EditableFields,
            new SwShTrainersWorkflowStats(
                trainers.Count,
                trainers.Sum(trainer => trainer.Team.Count),
                sourceFileCount),
            diagnostics);
    }

    private static IReadOnlyList<WorkflowFileSource> ResolveTrainerFolder(OpenedProject project, string rootPath)
    {
        return project.FileGraph.Entries
            .Where(entry =>
                entry.RelativePath.StartsWith($"{rootPath}/", StringComparison.OrdinalIgnoreCase)
                && !entry.RelativePath.EndsWith("/", StringComparison.Ordinal))
            .Select((entry, index) =>
            {
                var sourcePath = ResolveSourcePath(project.Paths, entry);
                return sourcePath is null || !File.Exists(sourcePath)
                    ? null
                    : new WorkflowFileSource(GetTrainerId(entry.RelativePath, index), entry, sourcePath);
            })
            .Where(source => source is not null)
            .Select(source => source!)
            .OrderBy(source => source.SortKey)
            .ThenBy(source => source.Entry.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
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

    private static int GetTrainerId(string relativePath, int fallback)
    {
        var fileName = Path.GetFileNameWithoutExtension(relativePath);
        var match = DigitsRegex.Match(fileName);

        return match.Success
            && int.TryParse(match.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var trainerId)
            ? trainerId
            : fallback;
    }

    private static TrainerLookupTables LoadLookupTables(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var messageRoot = ResolveLanguageMessageRoot(project, diagnostics);

        return new TrainerLookupTables(
            LoadMessageTable(project, messageRoot, "trname.dat", diagnostics),
            LoadMessageTable(project, messageRoot, "trtype.dat", diagnostics),
            LoadMessageTable(project, messageRoot, "monsname.dat", diagnostics),
            LoadMessageTable(project, messageRoot, "itemname.dat", diagnostics),
            LoadMessageTable(project, messageRoot, "wazaname.dat", diagnostics));
    }

    private static string? ResolveLanguageMessageRoot(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var languages = project.FileGraph.Entries
            .Where(entry => entry.RelativePath.StartsWith($"{MessageRootPath}/", StringComparison.OrdinalIgnoreCase))
            .Select(entry => GetLanguage(entry.RelativePath))
            .Where(language => !string.IsNullOrWhiteSpace(language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (languages.Length == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Trainer lookup text is not available; numeric fallback labels will be shown.",
                expected: $"{MessageRootPath}/{PreferredLanguage}/common/*.dat"));
            return null;
        }

        var language = languages.Contains(PreferredLanguage, StringComparer.OrdinalIgnoreCase)
            ? PreferredLanguage
            : languages[0];

        if (!string.Equals(language, PreferredLanguage, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"English trainer lookup text was not found; using '{language}' lookup tables instead.",
                expected: $"{MessageRootPath}/{PreferredLanguage}/common/*.dat"));
        }

        return $"{MessageRootPath}/{language}/common";
    }

    private static string[] LoadMessageTable(
        OpenedProject project,
        string? messageRoot,
        string fileName,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (messageRoot is null)
        {
            return Array.Empty<string>();
        }

        var relativePath = $"{messageRoot}/{fileName}";
        var entry = project.FileGraph.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return Array.Empty<string>();
        }

        var sourcePath = ResolveSourcePath(project.Paths, entry);
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            return Array.Empty<string>();
        }

        try
        {
            return SwShGameTextFile.Parse(File.ReadAllBytes(sourcePath))
                .Lines
                .Select(line => line.Text)
                .ToArray();
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Trainer lookup table '{relativePath}' could not be decoded: {exception.Message}",
                file: relativePath,
                expected: "Sword/Shield message table"));
            return Array.Empty<string>();
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Trainer lookup table '{relativePath}' could not be read: {exception.Message}",
                file: relativePath,
                expected: "Readable message table"));
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Trainer lookup table '{relativePath}' could not be read: {exception.Message}",
                file: relativePath,
                expected: "Readable message table"));
            return Array.Empty<string>();
        }
    }

    private static string? GetLanguage(string relativePath)
    {
        if (!relativePath.StartsWith($"{MessageRootPath}/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var languageStart = MessageRootPath.Length + 1;
        var nextSeparator = relativePath.IndexOf('/', languageStart);

        return nextSeparator < 0
            ? null
            : relativePath[languageStart..nextSeparator];
    }

    private static SwShTrainerRecord ToTrainerRecord(
        WorkflowFileSource dataSource,
        WorkflowFileSource pokeSource,
        SwShTrainerDataRecord trainer,
        IReadOnlyList<SwShTrainerPokemonTableRecord> team,
        TrainerLookupTables names)
    {
        return new SwShTrainerRecord(
            dataSource.TrainerId,
            GetLookupValue(names.TrainerNames, dataSource.TrainerId, $"Trainer {dataSource.TrainerId}"),
            trainer.ClassId,
            GetLookupValue(names.TrainerClasses, trainer.ClassId, $"Class {trainer.ClassId}"),
            $"Trainer {dataSource.TrainerId}",
            trainer.BattleMode,
            FormatBattleMode(trainer.BattleMode),
            team.Select(pokemon => ToTrainerPokemonRecord(pokemon, names)).ToArray(),
            CreateProvenance(dataSource.Entry, pokeSource.Entry));
    }

    private static SwShTrainerPokemonRecord ToTrainerPokemonRecord(
        SwShTrainerPokemonTableRecord pokemon,
        TrainerLookupTables names)
    {
        var moves = pokemon.MoveIds
            .Select(moveId => moveId == 0 ? "None" : GetLookupValue(names.MoveNames, moveId, $"Move {moveId}"))
            .ToArray();

        return new SwShTrainerPokemonRecord(
            pokemon.Slot,
            pokemon.SpeciesId,
            GetLookupValue(names.SpeciesNames, pokemon.SpeciesId, $"Species {pokemon.SpeciesId}"),
            pokemon.Level,
            pokemon.HeldItemId,
            pokemon.HeldItemId == 0
                ? null
                : GetLookupValue(names.ItemNames, pokemon.HeldItemId, $"Item {pokemon.HeldItemId}"),
            pokemon.MoveIds,
            moves);
    }

    private static string GetLookupValue(IReadOnlyList<string> values, int index, string fallback)
    {
        return (uint)index < (uint)values.Count && !string.IsNullOrWhiteSpace(values[index])
            ? values[index]
            : fallback;
    }

    private static string FormatBattleMode(int mode)
    {
        return mode switch
        {
            0 => "Singles",
            1 => "Doubles",
            2 => "Multi",
            _ => $"Mode {mode}",
        };
    }

    private static SwShTrainerProvenance CreateProvenance(
        ProjectFileGraphEntry dataEntry,
        ProjectFileGraphEntry teamEntry)
    {
        var dataSourceLayer = dataEntry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;
        var teamSourceLayer = teamEntry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShTrainerProvenance(
            dataEntry.RelativePath,
            teamEntry.RelativePath,
            dataSourceLayer,
            teamSourceLayer,
            dataEntry.State,
            teamEntry.State);
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

    internal sealed record WorkflowFileSource(
        int TrainerId,
        ProjectFileGraphEntry Entry,
        string AbsolutePath)
    {
        public int SortKey { get; } = TrainerId;
    }

    private sealed record TrainerLookupTables(
        IReadOnlyList<string> TrainerNames,
        IReadOnlyList<string> TrainerClasses,
        IReadOnlyList<string> SpeciesNames,
        IReadOnlyList<string> ItemNames,
        IReadOnlyList<string> MoveNames);
}
