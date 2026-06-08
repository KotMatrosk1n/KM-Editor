// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Workflows;

namespace KM.SwSh.Pokemon;

public sealed class SwShPokemonWorkflowService
{
    public const string PersonalDataPath = SwShPersonalTable.PersonalDataRelativePath;
    public const string LearnsetDataPath = SwShPokemonLearnsetTable.LearnsetDataRelativePath;
    public const string EvolutionDataDirectory = SwShEvolutionSet.EvolutionDataRelativeDirectory;
    public const string EnglishPokemonNamePath = "romfs/bin/message/English/common/pokelist.dat";
    public const string EnglishMoveNamePath = "romfs/bin/message/English/common/wazaname.dat";

    private static readonly IReadOnlyList<string> TypeNames =
    [
        "Normal",
        "Fighting",
        "Flying",
        "Poison",
        "Ground",
        "Rock",
        "Bug",
        "Ghost",
        "Steel",
        "Fire",
        "Water",
        "Grass",
        "Electric",
        "Psychic",
        "Ice",
        "Dragon",
        "Dark",
        "Fairy",
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
                    "Pokemon Data requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        return CreateSummary(SwShWorkflowAvailability.ReadOnly);
    }

    public SwShPokemonWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);

        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(summary, [], sourceFileCount: 0, diagnostics);
        }

        var personalSource = ResolveWorkflowFile(project, PersonalDataPath);
        if (personalSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Pokemon personal data is not available for this project.",
                expected: PersonalDataPath));
            return CreateWorkflow(summary, [], sourceFileCount: 0, diagnostics);
        }

        var pokemonNames = LoadOptionalTextTable(
            project,
            EnglishPokemonNamePath,
            "Pokemon names",
            diagnostics);
        var moveNames = LoadOptionalTextTable(
            project,
            EnglishMoveNamePath,
            "Move names",
            diagnostics);
        var learnsets = LoadLearnsets(project, diagnostics);
        var evolutions = LoadEvolutions(project, diagnostics);

        try
        {
            var personalTable = SwShPersonalTable.Parse(File.ReadAllBytes(personalSource.AbsolutePath));
            var provenance = CreateProvenance(personalSource.GraphEntry);
            var pokemon = personalTable.Records
                .Select(record => ToPokemonRecord(record, pokemonNames, moveNames, learnsets, evolutions, provenance))
                .ToArray();
            var sourceFileCount =
                1
                + (pokemonNames.Count > 0 ? 1 : 0)
                + (moveNames.Count > 0 ? 1 : 0)
                + (learnsets.Count > 0 ? 1 : 0)
                + (evolutions.Count > 0 ? evolutions.Count : 0);

            return CreateWorkflow(summary, pokemon, sourceFileCount, diagnostics);
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon personal data source is not supported: {exception.Message}",
                file: personalSource.GraphEntry.RelativePath,
                expected: "Sword/Shield personal_total.bin"));
            return CreateWorkflow(summary, [], sourceFileCount: 1, diagnostics);
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon personal data source could not be read: {exception.Message}",
                file: personalSource.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield personal_total.bin"));
            return CreateWorkflow(summary, [], sourceFileCount: 1, diagnostics);
        }
    }

    private static IReadOnlyDictionary<int, SwShPokemonLearnsetRecord> LoadLearnsets(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var source = ResolveWorkflowFile(project, LearnsetDataPath);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Pokemon learnset data is not available; learnset counts will be empty.",
                expected: LearnsetDataPath));
            return new Dictionary<int, SwShPokemonLearnsetRecord>();
        }

        try
        {
            return SwShPokemonLearnsetTable.Parse(File.ReadAllBytes(source.AbsolutePath))
                .Records
                .ToDictionary(record => record.PersonalId);
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Pokemon learnset data source is not supported: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Sword/Shield wazaoboe_total.bin"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Pokemon learnset data source could not be read: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield wazaoboe_total.bin"));
        }

        return new Dictionary<int, SwShPokemonLearnsetRecord>();
    }

    private static IReadOnlyDictionary<int, IReadOnlyList<SwShEvolutionRecord>> LoadEvolutions(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var sources = ResolveWorkflowFiles(project, EvolutionDataDirectory)
            .Where(source => source.GraphEntry.RelativePath.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
            .OrderBy(source => source.GraphEntry.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (sources.Length == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Pokemon evolution data is not available; evolution counts will be empty.",
                expected: EvolutionDataDirectory));
            return new Dictionary<int, IReadOnlyList<SwShEvolutionRecord>>();
        }

        var evolutions = new Dictionary<int, IReadOnlyList<SwShEvolutionRecord>>();
        foreach (var source in sources)
        {
            var speciesId = TryParseEvolutionFileSpeciesId(source.GraphEntry.RelativePath);
            if (speciesId is null)
            {
                continue;
            }

            try
            {
                evolutions[speciesId.Value] = SwShEvolutionSet.Parse(File.ReadAllBytes(source.AbsolutePath)).Evolutions;
            }
            catch (InvalidDataException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Pokemon evolution data source is not supported: {exception.Message}",
                    file: source.GraphEntry.RelativePath,
                    expected: "Sword/Shield evo_###.bin"));
            }
            catch (IOException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Pokemon evolution data source could not be read: {exception.Message}",
                    file: source.GraphEntry.RelativePath,
                    expected: "Readable Sword/Shield evo_###.bin"));
            }
        }

        return evolutions;
    }

    private static int? TryParseEvolutionFileSpeciesId(string relativePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(relativePath);
        return fileName.StartsWith("evo_", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(fileName["evo_".Length..], out var speciesId)
            ? speciesId
            : null;
    }

    private static IReadOnlyList<string> LoadOptionalTextTable(
        OpenedProject project,
        string relativePath,
        string label,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var source = ResolveWorkflowFile(project, relativePath);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"{label} are not available; numeric fallback labels will be shown.",
                expected: relativePath));
            return [];
        }

        try
        {
            return SwShGameTextFile.Parse(File.ReadAllBytes(source.AbsolutePath))
                .Lines
                .Select(line => line.Text)
                .ToArray();
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"{label} table could not be decoded: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Sword/Shield message .dat"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"{label} table could not be read: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield message .dat"));
        }

        return [];
    }

    private static SwShPokemonWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShPokemonRecord> pokemon,
        int sourceFileCount,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SwShPokemonWorkflow(
            summary,
            pokemon,
            new SwShPokemonWorkflowStats(
                pokemon.Count,
                pokemon.Count(record => record.DexPresence.IsPresentInGame),
                pokemon.Sum(record => record.Evolutions.Count),
                pokemon.Sum(record => record.Learnset.Count),
                sourceFileCount),
            diagnostics);
    }

    private static SwShPokemonRecord ToPokemonRecord(
        SwShPersonalRecord personal,
        IReadOnlyList<string> pokemonNames,
        IReadOnlyList<string> moveNames,
        IReadOnlyDictionary<int, SwShPokemonLearnsetRecord> learnsets,
        IReadOnlyDictionary<int, IReadOnlyList<SwShEvolutionRecord>> evolutions,
        SwShPokemonProvenance provenance)
    {
        var speciesId = ResolveSpeciesId(personal);
        var learnset = learnsets.TryGetValue(personal.PersonalId, out var learnsetRecord)
            ? learnsetRecord.Moves.Select(move => new SwShPokemonLearnsetMove(
                    move.MoveId,
                    GetIndexedName(move.MoveId, moveNames, "Move"),
                    move.Level))
                .ToArray()
            : [];
        var evolutionRecords = evolutions.TryGetValue(personal.PersonalId, out var evolutionRecord)
            ? evolutionRecord
                .Select(evolution => new SwShPokemonEvolutionRecord(
                    evolution.Method,
                    evolution.Argument,
                    evolution.Species,
                    evolution.Form,
                    evolution.Level))
                .ToArray()
            : [];

        return new SwShPokemonRecord(
            personal.PersonalId,
            speciesId,
            personal.Form,
            GetIndexedName(speciesId, pokemonNames, "Pokemon"),
            personal.Form == 0 ? "Base" : $"Form {personal.Form}",
            FormatType(personal.Type1),
            FormatType(personal.Type2),
            new SwShPokemonBaseStats(
                personal.HP,
                personal.Attack,
                personal.Defense,
                personal.SpecialAttack,
                personal.SpecialDefense,
                personal.Speed,
                personal.BaseStatTotal),
            new SwShPokemonAbilitySet(personal.Ability1, personal.Ability2, personal.HiddenAbility),
            new SwShPokemonDexPresence(
                personal.IsPresentInGame,
                personal.RegionalDexIndex != 0 || personal.ArmorDexIndex != 0 || personal.CrownDexIndex != 0,
                personal.RegionalDexIndex,
                personal.ArmorDexIndex,
                personal.CrownDexIndex),
            personal.CatchRate,
            personal.EvolutionStage,
            personal.GenderRatio,
            personal.BaseExperience,
            personal.Height,
            personal.Weight,
            evolutionRecords,
            learnset,
            provenance);
    }

    private static int ResolveSpeciesId(SwShPersonalRecord personal)
    {
        return personal.HatchedSpecies > 0
            ? personal.HatchedSpecies
            : personal.PersonalId;
    }

    private static string GetIndexedName(int id, IReadOnlyList<string> names, string fallbackPrefix)
    {
        if ((uint)id < (uint)names.Count && !string.IsNullOrWhiteSpace(names[id]))
        {
            return names[id];
        }

        return $"{fallbackPrefix} {id}";
    }

    private static string FormatType(int typeId)
    {
        return (uint)typeId < (uint)TypeNames.Count
            ? TypeNames[typeId]
            : $"Type {typeId}";
    }

    private static WorkflowFileSource? ResolveWorkflowFile(OpenedProject project, string relativePath)
    {
        var graphEntry = project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));

        if (graphEntry is null)
        {
            return null;
        }

        var sourcePath = ResolveSourcePath(project.Paths, graphEntry);

        return sourcePath is not null && File.Exists(sourcePath)
            ? new WorkflowFileSource(graphEntry, sourcePath)
            : null;
    }

    private static IEnumerable<WorkflowFileSource> ResolveWorkflowFiles(
        OpenedProject project,
        string relativeDirectory)
    {
        var prefix = relativeDirectory.TrimEnd('/') + "/";

        return project.FileGraph.Entries
            .Where(entry => entry.RelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(entry => new
            {
                Entry = entry,
                SourcePath = ResolveSourcePath(project.Paths, entry),
            })
            .Where(source => source.SourcePath is not null && File.Exists(source.SourcePath))
            .Select(source => new WorkflowFileSource(source.Entry, source.SourcePath!));
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

    private static SwShPokemonProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShPokemonProvenance(entry.RelativePath, sourceLayer, entry.State);
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.Pokemon,
            "Pokemon Data",
            "Pokemon personal stats, forms, evolutions, learnsets, and source provenance.",
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
            Domain: "workflow.pokemon",
            Expected: expected);
    }

    internal sealed record WorkflowFileSource(
        ProjectFileGraphEntry GraphEntry,
        string AbsolutePath);
}
