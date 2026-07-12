// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Items;
using KM.SwSh.Moves;
using KM.SwSh.Pokemon;
using KM.SwSh.Workflows;
using System.Globalization;
using System.Text.RegularExpressions;

namespace KM.SwSh.Trainers;

public sealed class SwShTrainersWorkflowService
{
    public const int MaximumPokemonEvValue = 252;

    public const string TrainerClassIdField = "trainerClassId";
    public const string ClassBallIdField = "classBallId";
    public const string BattleTypeField = "battleType";
    public const string TrainerItem1IdField = "trainerItem1Id";
    public const string TrainerItem2IdField = "trainerItem2Id";
    public const string TrainerItem3IdField = "trainerItem3Id";
    public const string TrainerItem4IdField = "trainerItem4Id";
    public const string AiFlagsField = "aiFlags";
    public const string HealField = "heal";
    public const string MoneyField = "money";
    public const string GiftField = "gift";
    internal const string PokemonCountField = "pokemonCount";
    public const string SpeciesIdField = "speciesId";
    public const string FormField = "form";
    public const string LevelField = "level";
    public const string HeldItemIdField = "heldItemId";
    public const string Move1IdField = "move1Id";
    public const string Move2IdField = "move2Id";
    public const string Move3IdField = "move3Id";
    public const string Move4IdField = "move4Id";
    public const string GenderField = "gender";
    public const string AbilityField = "ability";
    public const string NatureField = "nature";
    public const string EvHpField = "evHp";
    public const string EvAttackField = "evAttack";
    public const string EvDefenseField = "evDefense";
    public const string EvSpecialAttackField = "evSpecialAttack";
    public const string EvSpecialDefenseField = "evSpecialDefense";
    public const string EvSpeedField = "evSpeed";
    public const string DynamaxLevelField = "dynamaxLevel";
    public const string CanGigantamaxField = "canGigantamax";
    public const string IvHpField = "ivHp";
    public const string IvAttackField = "ivAttack";
    public const string IvDefenseField = "ivDefense";
    public const string IvSpecialAttackField = "ivSpecialAttack";
    public const string IvSpecialDefenseField = "ivSpecialDefense";
    public const string IvSpeedField = "ivSpeed";
    public const string ShinyField = "shiny";
    public const string CanDynamaxField = "canDynamax";
    public const string TrainerDataRootPath = SwShTrainerDataFile.TrainerDataRootRelativePath;
    public const string TrainerPokeRootPath = SwShTrainerTeamFile.TrainerPokeRootRelativePath;
    public const string TrainerClassRootPath = SwShTrainerClassFile.TrainerClassRootRelativePath;
    public const string PreferredLanguage = "English";

    private const string MessageRootPath = "romfs/bin/message";
    private const int MaximumEditableBattleMode = 1;

    private static readonly Regex DigitsRegex = new("(\\d+)(?!.*\\d)", RegexOptions.Compiled);

    private static readonly IReadOnlyList<SwShTrainerEditableFieldOption> BattleTypeOptions =
    [
        new SwShTrainerEditableFieldOption(0, "0 Singles"),
        new SwShTrainerEditableFieldOption(1, "1 Doubles"),
    ];

    private static readonly IReadOnlyList<SwShTrainerEditableFieldOption> BooleanOptions =
    [
        new SwShTrainerEditableFieldOption(0, "No"),
        new SwShTrainerEditableFieldOption(1, "Yes"),
    ];

    private static readonly IReadOnlyList<SwShTrainerEditableFieldOption> DynamaxLevelOptions =
    [
        ..Enumerable.Range(0, SwShTrainerTeamFile.MaximumDynamaxLevel + 1)
            .Select(value => new SwShTrainerEditableFieldOption(value, value.ToString(CultureInfo.InvariantCulture))),
    ];

    private static readonly IReadOnlyList<SwShTrainerEditableFieldOption> AbilityOptions =
    [
        new SwShTrainerEditableFieldOption(0, "Default"),
        new SwShTrainerEditableFieldOption(1, "Ability 1"),
        new SwShTrainerEditableFieldOption(2, "Ability 2"),
        new SwShTrainerEditableFieldOption(3, "Hidden Ability"),
    ];

    private static readonly IReadOnlyList<SwShTrainerEditableFieldOption> GenderOptions =
    [
        new SwShTrainerEditableFieldOption(0, "Random"),
        new SwShTrainerEditableFieldOption(1, "Male"),
        new SwShTrainerEditableFieldOption(2, "Female"),
        new SwShTrainerEditableFieldOption(3, "Genderless"),
    ];

    private static readonly IReadOnlyList<SwShTrainerEditableFieldOption> NatureOptions =
    [
        ..SwShNatureLabels.Fixed.Select(nature => new SwShTrainerEditableFieldOption(
            nature.Value,
            nature.Label)),
    ];

    private static readonly IReadOnlyList<TrainerAiFlagDefinition> AiFlagDefinitions =
    [
        new(0, "Basic", "Enables basic battle decision logic."),
        new(1, "Strong", "Enables stronger move and target choices."),
        new(2, "Expert", "Enables expert battle decision logic."),
        new(3, "Double", "Enables double-battle-aware decision logic."),
        new(4, "Raid", "Enables raid-battle-specific decision logic."),
        new(5, "Allowance", "Allows additional AI-controlled action checks."),
        new(6, "PokeChange", "Allows AI-driven Pokemon switching."),
        new(7, "Fire Gym (1)", "Enables the first Fire Gym behavior bit."),
        new(8, "Fire Gym (2)", "Enables the second Fire Gym behavior bit."),
        new(11, "Fire Gym (3)", "Enables the third Fire Gym behavior bit."),
        new(9, "Unused 1", "Reserved trainer AI bit."),
        new(10, "Item", "Allows AI-driven trainer item usage."),
        new(12, "Unused 2", "Reserved trainer AI bit."),
    ];

    private static readonly IReadOnlyList<SwShTrainerEditableFieldOption> BallOptions =
    [
        new SwShTrainerEditableFieldOption(0, "0 None"),
        new SwShTrainerEditableFieldOption(1, "1 Master Ball"),
        new SwShTrainerEditableFieldOption(2, "2 Ultra Ball"),
        new SwShTrainerEditableFieldOption(3, "3 Great Ball"),
        new SwShTrainerEditableFieldOption(4, "4 Poke Ball"),
        new SwShTrainerEditableFieldOption(5, "5 Safari Ball"),
        new SwShTrainerEditableFieldOption(6, "6 Net Ball"),
        new SwShTrainerEditableFieldOption(7, "7 Dive Ball"),
        new SwShTrainerEditableFieldOption(8, "8 Nest Ball"),
        new SwShTrainerEditableFieldOption(9, "9 Repeat Ball"),
        new SwShTrainerEditableFieldOption(10, "10 Timer Ball"),
        new SwShTrainerEditableFieldOption(11, "11 Luxury Ball"),
        new SwShTrainerEditableFieldOption(12, "12 Premier Ball"),
        new SwShTrainerEditableFieldOption(13, "13 Dusk Ball"),
        new SwShTrainerEditableFieldOption(14, "14 Heal Ball"),
        new SwShTrainerEditableFieldOption(15, "15 Quick Ball"),
        new SwShTrainerEditableFieldOption(16, "16 Cherish Ball"),
        new SwShTrainerEditableFieldOption(17, "17 Fast Ball"),
        new SwShTrainerEditableFieldOption(18, "18 Level Ball"),
        new SwShTrainerEditableFieldOption(19, "19 Lure Ball"),
        new SwShTrainerEditableFieldOption(20, "20 Heavy Ball"),
        new SwShTrainerEditableFieldOption(21, "21 Love Ball"),
        new SwShTrainerEditableFieldOption(22, "22 Friend Ball"),
        new SwShTrainerEditableFieldOption(23, "23 Moon Ball"),
        new SwShTrainerEditableFieldOption(24, "24 Sport Ball"),
        new SwShTrainerEditableFieldOption(25, "25 Dream Ball"),
        new SwShTrainerEditableFieldOption(26, "26 Beast Ball"),
    ];

    private static readonly IReadOnlyList<SwShTrainerEditableField> EditableFields =
    [
        new SwShTrainerEditableField(
            TrainerClassIdField,
            "Trainer class ID",
            "integer",
            0,
            SwShTrainerDataFile.MaximumClassId),
        new SwShTrainerEditableField(
            ClassBallIdField,
            "Class ball",
            "integer",
            0,
            SwShTrainerClassFile.MaximumBallId,
            BallOptions),
        new SwShTrainerEditableField(
            BattleTypeField,
            "Battle type",
            "integer",
            0,
            MaximumEditableBattleMode,
            BattleTypeOptions),
        new SwShTrainerEditableField(
            TrainerItem1IdField,
            "Trainer item 1 ID",
            "integer",
            0,
            SwShTrainerDataFile.MaximumItemId),
        new SwShTrainerEditableField(
            TrainerItem2IdField,
            "Trainer item 2 ID",
            "integer",
            0,
            SwShTrainerDataFile.MaximumItemId),
        new SwShTrainerEditableField(
            TrainerItem3IdField,
            "Trainer item 3 ID",
            "integer",
            0,
            SwShTrainerDataFile.MaximumItemId),
        new SwShTrainerEditableField(
            TrainerItem4IdField,
            "Trainer item 4 ID",
            "integer",
            0,
            SwShTrainerDataFile.MaximumItemId),
        new SwShTrainerEditableField(
            AiFlagsField,
            "AI flags",
            "integer",
            0,
            SwShTrainerDataFile.MaximumAiFlags),
        new SwShTrainerEditableField(
            HealField,
            "Heal flag",
            "integer",
            0,
            1,
            BooleanOptions),
        new SwShTrainerEditableField(
            MoneyField,
            "Prize money",
            "integer",
            0,
            SwShTrainerDataFile.MaximumMoney),
        new SwShTrainerEditableField(
            GiftField,
            "Gift ID",
            "integer",
            0,
            SwShTrainerDataFile.MaximumGiftId),
        new SwShTrainerEditableField(
            SpeciesIdField,
            "Species ID",
            "integer",
            0,
            SwShTrainerTeamFile.MaximumPokemonId),
        new SwShTrainerEditableField(
            FormField,
            "Form",
            "integer",
            0,
            SwShTrainerTeamFile.MaximumFormId),
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
        new SwShTrainerEditableField(
            GenderField,
            "Gender",
            "integer",
            0,
            SwShTrainerTeamFile.MaximumGenderValue,
            GenderOptions),
        new SwShTrainerEditableField(
            AbilityField,
            "Ability",
            "integer",
            0,
            SwShTrainerTeamFile.MaximumAbilityValue,
            AbilityOptions),
        new SwShTrainerEditableField(
            NatureField,
            "Nature",
            "integer",
            0,
            SwShTrainerTeamFile.MaximumNatureId,
            NatureOptions),
        new SwShTrainerEditableField(
            EvHpField,
            "HP",
            "integer",
            0,
            MaximumPokemonEvValue),
        new SwShTrainerEditableField(
            EvAttackField,
            "Attack",
            "integer",
            0,
            MaximumPokemonEvValue),
        new SwShTrainerEditableField(
            EvDefenseField,
            "Defense",
            "integer",
            0,
            MaximumPokemonEvValue),
        new SwShTrainerEditableField(
            EvSpecialAttackField,
            "Sp. Atk",
            "integer",
            0,
            MaximumPokemonEvValue),
        new SwShTrainerEditableField(
            EvSpecialDefenseField,
            "Sp. Def",
            "integer",
            0,
            MaximumPokemonEvValue),
        new SwShTrainerEditableField(
            EvSpeedField,
            "Speed",
            "integer",
            0,
            MaximumPokemonEvValue),
        new SwShTrainerEditableField(
            DynamaxLevelField,
            "Dynamax level",
            "integer",
            0,
            SwShTrainerTeamFile.MaximumDynamaxLevel,
            DynamaxLevelOptions),
        new SwShTrainerEditableField(
            CanGigantamaxField,
            "Can Gigantamax",
            "boolean",
            0,
            1,
            BooleanOptions),
        new SwShTrainerEditableField(
            IvHpField,
            "HP",
            "integer",
            0,
            SwShTrainerTeamFile.MaximumIvValue),
        new SwShTrainerEditableField(
            IvAttackField,
            "Attack",
            "integer",
            0,
            SwShTrainerTeamFile.MaximumIvValue),
        new SwShTrainerEditableField(
            IvDefenseField,
            "Defense",
            "integer",
            0,
            SwShTrainerTeamFile.MaximumIvValue),
        new SwShTrainerEditableField(
            IvSpecialAttackField,
            "Sp. Atk",
            "integer",
            0,
            SwShTrainerTeamFile.MaximumIvValue),
        new SwShTrainerEditableField(
            IvSpecialDefenseField,
            "Sp. Def",
            "integer",
            0,
            SwShTrainerTeamFile.MaximumIvValue),
        new SwShTrainerEditableField(
            IvSpeedField,
            "Speed",
            "integer",
            0,
            SwShTrainerTeamFile.MaximumIvValue),
        new SwShTrainerEditableField(
            ShinyField,
            "Shiny",
            "boolean",
            0,
            1,
            BooleanOptions),
        new SwShTrainerEditableField(
            CanDynamaxField,
            "Can Dynamax",
            "boolean",
            0,
            1,
            BooleanOptions),
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
        var trainerClassSources = ResolveTrainerFolder(project, TrainerClassRootPath);
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

        if (trainerClassSources.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Trainers did not find Sword/Shield trainer class files; class ball editing is disabled.",
                expected: $"{TrainerClassRootPath}/**/*"));
        }

        if (trainerDataSources.Count == 0)
        {
            return CreateWorkflow(summary, Array.Empty<SwShTrainerRecord>(), diagnostics, sourceFileCount: 0);
        }

        var names = LoadLookupTables(project, diagnostics);
        var classRecordsByClassId = LoadTrainerClassRecords(trainerClassSources, diagnostics);
        var classOwnershipByClassId = CreateTrainerClassOwnership(trainerDataSources, names);
        var pokeSourcesByTrainerId = trainerPokeSources
            .GroupBy(source => source.TrainerId)
            .ToDictionary(group => group.Key, group => group.First());
        var classSourcesByClassId = trainerClassSources
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
                parsedSourceFileCount += classRecordsByClassId.ContainsKey(dataFile.Record.ClassId) ? 3 : 2;

                if (dataFile.Record.PokemonCount != teamFile.Records.Count)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Warning,
                        $"Trainer {dataSource.TrainerId} data declares {dataFile.Record.PokemonCount} Pokemon but the party file contains {teamFile.Records.Count}.",
                        file: dataSource.Entry.RelativePath,
                        expected: "Trainer data Pokemon count matching party rows"));
                }

                classSourcesByClassId.TryGetValue(dataFile.Record.ClassId, out var classSource);
                classRecordsByClassId.TryGetValue(dataFile.Record.ClassId, out var classRecord);
                classOwnershipByClassId.TryGetValue(dataFile.Record.ClassId, out var classOwnership);

                trainers.Add(ToTrainerRecord(
                    dataSource,
                    pokeSource,
                    classSource,
                    dataFile.Record,
                    teamFile.Records,
                    classRecord,
                    classOwnership,
                    names));
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

        return CreateWorkflow(
            summary,
            trainers
                .Where(trainer => trainer.TrainerId != 0)
                .OrderBy(trainer => trainer.TrainerId)
                .ToArray(),
            diagnostics,
            parsedSourceFileCount,
            names);
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

        return PathContainment.IsWithinRoot(pathFromOutputRoot)
            ? targetPath
            : null;
    }

    internal static bool IsTrainerDataField(string? field)
    {
        return string.Equals(field, TrainerClassIdField, StringComparison.Ordinal)
            || string.Equals(field, BattleTypeField, StringComparison.Ordinal)
            || string.Equals(field, TrainerItem1IdField, StringComparison.Ordinal)
            || string.Equals(field, TrainerItem2IdField, StringComparison.Ordinal)
            || string.Equals(field, TrainerItem3IdField, StringComparison.Ordinal)
            || string.Equals(field, TrainerItem4IdField, StringComparison.Ordinal)
            || string.Equals(field, AiFlagsField, StringComparison.Ordinal)
            || string.Equals(field, HealField, StringComparison.Ordinal)
            || string.Equals(field, MoneyField, StringComparison.Ordinal)
            || string.Equals(field, GiftField, StringComparison.Ordinal)
            || string.Equals(field, PokemonCountField, StringComparison.Ordinal);
    }

    internal static bool IsTrainerClassField(string? field)
    {
        return string.Equals(field, ClassBallIdField, StringComparison.Ordinal);
    }

    internal static bool IsTrainerPokemonField(string? field)
    {
        return string.Equals(field, SpeciesIdField, StringComparison.Ordinal)
            || string.Equals(field, FormField, StringComparison.Ordinal)
            || string.Equals(field, LevelField, StringComparison.Ordinal)
            || string.Equals(field, HeldItemIdField, StringComparison.Ordinal)
            || string.Equals(field, Move1IdField, StringComparison.Ordinal)
            || string.Equals(field, Move2IdField, StringComparison.Ordinal)
            || string.Equals(field, Move3IdField, StringComparison.Ordinal)
            || string.Equals(field, Move4IdField, StringComparison.Ordinal)
            || string.Equals(field, GenderField, StringComparison.Ordinal)
            || string.Equals(field, AbilityField, StringComparison.Ordinal)
            || string.Equals(field, NatureField, StringComparison.Ordinal)
            || string.Equals(field, EvHpField, StringComparison.Ordinal)
            || string.Equals(field, EvAttackField, StringComparison.Ordinal)
            || string.Equals(field, EvDefenseField, StringComparison.Ordinal)
            || string.Equals(field, EvSpecialAttackField, StringComparison.Ordinal)
            || string.Equals(field, EvSpecialDefenseField, StringComparison.Ordinal)
            || string.Equals(field, EvSpeedField, StringComparison.Ordinal)
            || string.Equals(field, DynamaxLevelField, StringComparison.Ordinal)
            || string.Equals(field, CanGigantamaxField, StringComparison.Ordinal)
            || string.Equals(field, IvHpField, StringComparison.Ordinal)
            || string.Equals(field, IvAttackField, StringComparison.Ordinal)
            || string.Equals(field, IvDefenseField, StringComparison.Ordinal)
            || string.Equals(field, IvSpecialAttackField, StringComparison.Ordinal)
            || string.Equals(field, IvSpecialDefenseField, StringComparison.Ordinal)
            || string.Equals(field, IvSpeedField, StringComparison.Ordinal)
            || string.Equals(field, ShinyField, StringComparison.Ordinal)
            || string.Equals(field, CanDynamaxField, StringComparison.Ordinal);
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
        int sourceFileCount,
        TrainerLookupTables? names = null)
    {
        return new SwShTrainersWorkflow(
            summary,
            trainers,
            CreateEditableFields(names),
            new SwShTrainersWorkflowStats(
                trainers.Count,
                trainers.Sum(GetOccupiedPokemonCount),
                sourceFileCount),
            diagnostics);
    }

    private static IReadOnlyList<SwShTrainerEditableField> CreateEditableFields(TrainerLookupTables? names)
    {
        if (names is null)
        {
            return EditableFields;
        }

        var trainerClassOptions = CreateIndexedOptions(names.TrainerClasses, "Class");
        var speciesOptions = new[] { new SwShTrainerEditableFieldOption(0, "0 None") }
            .Concat(SwShSpeciesAvailability.CreateSpeciesOptions(
                names.SpeciesNames,
                names.PresentSpeciesIds,
                (value, label) => new SwShTrainerEditableFieldOption(value, label)))
            .ToArray();
        var itemOptions = CreateIndexedOptions(names.ItemNames, "Item");
        var moveOptions = SwShMoveAvailability.CreateMoveOptions(
            names.MoveNames,
            names.UsableMoveIds,
            (value, label) => new SwShTrainerEditableFieldOption(value, label),
            includeNone: true);

        return EditableFields
            .Select(field =>
            {
                var options = field.Field switch
                {
                    TrainerClassIdField => trainerClassOptions,
                    TrainerItem1IdField or TrainerItem2IdField or TrainerItem3IdField or TrainerItem4IdField => itemOptions,
                    GiftField => itemOptions,
                    SpeciesIdField => speciesOptions,
                    HeldItemIdField => itemOptions,
                    Move1IdField or Move2IdField or Move3IdField or Move4IdField => moveOptions,
                    _ => field.Options,
                };

                return options.Count == 0 || ReferenceEquals(options, field.Options)
                    ? field
                    : field with { Options = options };
            })
            .ToArray();
    }

    private static IReadOnlyList<SwShTrainerEditableFieldOption> CreateIndexedOptions(
        IReadOnlyList<string> names,
        string fallbackPrefix)
    {
        return names.Count == 0
            ? []
            : names
                .Select((name, index) =>
                {
                    var label = string.IsNullOrWhiteSpace(name)
                        ? index == 0 ? "None" : $"{fallbackPrefix} {index}"
                        : name;

                    return new SwShTrainerEditableFieldOption(
                        index,
                        string.Create(CultureInfo.InvariantCulture, $"{index:000} {label}"));
                })
                .ToArray();
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

    private static IReadOnlyDictionary<int, SwShTrainerClassRecord> LoadTrainerClassRecords(
        IReadOnlyList<WorkflowFileSource> classSources,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var records = new Dictionary<int, SwShTrainerClassRecord>();

        foreach (var source in classSources)
        {
            try
            {
                records[source.TrainerId] = SwShTrainerClassFile.Parse(File.ReadAllBytes(source.AbsolutePath)).Record;
            }
            catch (InvalidDataException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Trainer class {source.TrainerId} could not be decoded: {exception.Message}",
                    file: source.Entry.RelativePath,
                    expected: "Sword/Shield trainer class file"));
            }
            catch (IOException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Trainer class {source.TrainerId} could not be read: {exception.Message}",
                    file: source.Entry.RelativePath,
                    expected: "Readable trainer class file"));
            }
            catch (UnauthorizedAccessException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Trainer class {source.TrainerId} could not be read: {exception.Message}",
                    file: source.Entry.RelativePath,
                    expected: "Readable trainer class file"));
            }
        }

        return records;
    }

    private static IReadOnlyDictionary<int, TrainerClassOwnership> CreateTrainerClassOwnership(
        IReadOnlyList<WorkflowFileSource> trainerDataSources,
        TrainerLookupTables names)
    {
        var firstOwnerNames = new Dictionary<int, string>();
        var sharedClasses = new HashSet<int>();

        foreach (var source in trainerDataSources)
        {
            SwShTrainerDataRecord trainer;
            try
            {
                trainer = SwShTrainerDataFile.Parse(File.ReadAllBytes(source.AbsolutePath)).Record;
            }
            catch (InvalidDataException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            var ownerName = GetLookupValue(names.TrainerNames, source.TrainerId, $"Trainer {source.TrainerId}");
            if (!firstOwnerNames.TryGetValue(trainer.ClassId, out var firstOwnerName))
            {
                firstOwnerNames[trainer.ClassId] = ownerName;
                continue;
            }

            if (!string.Equals(firstOwnerName, ownerName, StringComparison.Ordinal))
            {
                sharedClasses.Add(trainer.ClassId);
            }
        }

        return firstOwnerNames
            .ToDictionary(
                entry => entry.Key,
                entry => new TrainerClassOwnership(entry.Value, sharedClasses.Contains(entry.Key)));
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
        var abilityResolver = SwShPokemonAbilityOptionResolver.Load(project);
        var itemNames = LoadMessageTable(project, messageRoot, "itemname.dat", diagnostics);
        var moveNames = LoadMessageTable(project, messageRoot, "wazaname.dat", diagnostics);
        var spriteSpeciesNames = LoadMessageTable(
            project,
            $"{MessageRootPath}/{PreferredLanguage}/common",
            "monsname.dat",
            diagnostics);
        var itemDisplayNames = SwShItemsWorkflowService.CreateItemDisplayNames(project, itemNames, moveNames);
        var presentSpeciesIds = SwShSpeciesAvailability.LoadPresentSpeciesIds(project);
        var usableMoveIds = SwShMoveAvailability.LoadUsableMoveIds(project);

        return new TrainerLookupTables(
            LoadMessageTable(project, messageRoot, "trname.dat", diagnostics),
            LoadMessageTable(project, messageRoot, "trtype.dat", diagnostics),
            LoadMessageTable(project, messageRoot, "monsname.dat", diagnostics),
            spriteSpeciesNames,
            presentSpeciesIds,
            itemDisplayNames,
            moveNames,
            usableMoveIds,
            abilityResolver);
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

        var preferredLanguage = SwShGameTextLanguage.Resolve(project.Paths);
        var language = languages.Contains(preferredLanguage, StringComparer.OrdinalIgnoreCase)
            ? preferredLanguage
            : languages.Contains(PreferredLanguage, StringComparer.OrdinalIgnoreCase)
                ? PreferredLanguage
                : languages[0];

        if (!string.Equals(language, PreferredLanguage, StringComparison.OrdinalIgnoreCase)
            && string.Equals(preferredLanguage, PreferredLanguage, StringComparison.OrdinalIgnoreCase))
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
        WorkflowFileSource? classSource,
        SwShTrainerDataRecord trainer,
        IReadOnlyList<SwShTrainerPokemonTableRecord> team,
        SwShTrainerClassRecord? classRecord,
        TrainerClassOwnership? classOwnership,
        TrainerLookupTables names)
    {
        var canEditClassBall = classSource is not null
            && classRecord is not null
            && classOwnership is { HasMultipleOwners: false };

        return new SwShTrainerRecord(
            dataSource.TrainerId,
            GetLookupValue(names.TrainerNames, dataSource.TrainerId, $"Trainer {dataSource.TrainerId}"),
            trainer.ClassId,
            GetLookupValue(names.TrainerClasses, trainer.ClassId, $"Class {trainer.ClassId}"),
            $"Trainer {dataSource.TrainerId}",
            trainer.BattleMode,
            FormatBattleMode(trainer.BattleMode),
            trainer.Items,
            trainer.Items.Select(itemId => itemId == 0 ? "None" : GetLookupValue(names.ItemNames, itemId, $"Item {itemId}")).ToArray(),
            (int)(trainer.AiFlags & SwShTrainerDataFile.KnownAiFlagsMask),
            CreateAiFlagStates(trainer.AiFlags),
            trainer.Heal,
            trainer.Money,
            trainer.Gift,
            classRecord?.BallId,
            classRecord is null ? null : FormatBall(classRecord.BallId),
            canEditClassBall,
            FormatClassBallScope(classSource, classRecord, classOwnership),
            CreateTrainerPokemonRecords(team, names),
            CreateProvenance(dataSource.Entry, pokeSource.Entry, classSource?.Entry));
    }

    private static IReadOnlyList<SwShTrainerPokemonRecord> CreateTrainerPokemonRecords(
        IReadOnlyList<SwShTrainerPokemonTableRecord> team,
        TrainerLookupTables names)
    {
        return Enumerable
            .Range(1, SwShTrainerTeamFile.MaximumPartySize)
            .Select(slot =>
            {
                var pokemon = team.FirstOrDefault(candidate => candidate.Slot == slot);
                return pokemon is null
                    ? CreateEmptyTrainerPokemonRecord(slot, names)
                    : ToTrainerPokemonRecord(pokemon, names);
            })
            .ToArray();
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
            pokemon.Form,
            pokemon.Level,
            pokemon.HeldItemId,
            pokemon.HeldItemId == 0
                ? null
                : GetLookupValue(names.ItemNames, pokemon.HeldItemId, $"Item {pokemon.HeldItemId}"),
            pokemon.MoveIds,
            moves,
            pokemon.Gender,
            FormatTrainerPokemonGender(pokemon.Gender),
            pokemon.Ability,
            GetTrainerPokemonAbilityLabel(names, pokemon.SpeciesId, pokemon.Form, pokemon.Ability),
            pokemon.Nature,
            FormatTrainerPokemonNature(pokemon.Nature),
            ToStatsRecord(pokemon.Evs),
            pokemon.DynamaxLevel,
            pokemon.CanGigantamax,
            ToStatsRecord(pokemon.Ivs),
            pokemon.Shiny,
            pokemon.CanDynamax)
        {
            AbilityOptions = CreateTrainerPokemonAbilityOptions(names, pokemon.SpeciesId, pokemon.Form),
            BaseStats = ResolveTrainerPokemonBaseStats(names, pokemon.SpeciesId, pokemon.Form),
            SpriteName = GetOptionalLookupValue(names.SpriteSpeciesNames, pokemon.SpeciesId),
        };
    }

    private static SwShTrainerPokemonStatsRecord? ResolveTrainerPokemonBaseStats(
        TrainerLookupTables names,
        int speciesId,
        int form)
    {
        var record = names.AbilityResolver.ResolvePersonalRecord(speciesId, form);
        return record is null
            ? null
            : new SwShTrainerPokemonStatsRecord(
                record.HP,
                record.Attack,
                record.Defense,
                record.SpecialAttack,
                record.SpecialDefense,
                record.Speed);
    }

    private static SwShTrainerPokemonRecord CreateEmptyTrainerPokemonRecord(
        int slot,
        TrainerLookupTables names)
    {
        return new SwShTrainerPokemonRecord(
            slot,
            0,
            "None",
            0,
            SwShTrainerTeamFile.MinimumLevel,
            0,
            null,
            [0, 0, 0, 0],
            ["None", "None", "None", "None"],
            0,
            FormatTrainerPokemonGender(0),
            0,
            GetTrainerPokemonAbilityLabel(names, 0, 0, 0),
            0,
            FormatTrainerPokemonNature(0),
            new SwShTrainerPokemonStatsRecord(0, 0, 0, 0, 0, 0),
            0,
            false,
            new SwShTrainerPokemonStatsRecord(0, 0, 0, 0, 0, 0),
            false,
            true)
        {
            AbilityOptions = CreateTrainerPokemonAbilityOptions(names, 0, 0),
        };
    }

    private static int GetOccupiedPokemonCount(SwShTrainerRecord trainer)
    {
        return trainer.Team.Count(pokemon => pokemon.SpeciesId > 0);
    }

    private static SwShTrainerPokemonStatsRecord ToStatsRecord(SwShTrainerPokemonStats stats)
    {
        return new SwShTrainerPokemonStatsRecord(
            stats.HP,
            stats.Attack,
            stats.Defense,
            stats.SpecialAttack,
            stats.SpecialDefense,
            stats.Speed);
    }

    private static string GetLookupValue(IReadOnlyList<string> values, int index, string fallback)
    {
        return (uint)index < (uint)values.Count && !string.IsNullOrWhiteSpace(values[index])
            ? values[index]
            : fallback;
    }

    private static string? GetOptionalLookupValue(IReadOnlyList<string> values, int index)
    {
        return (uint)index < (uint)values.Count && !string.IsNullOrWhiteSpace(values[index])
            ? values[index]
            : null;
    }

    private static string FormatBattleMode(int mode)
    {
        return mode switch
        {
            0 => "Singles",
            1 => "Doubles",
            _ => $"Mode {mode}",
        };
    }

    private static string FormatClassBallScope(
        WorkflowFileSource? classSource,
        SwShTrainerClassRecord? classRecord,
        TrainerClassOwnership? classOwnership)
    {
        if (classSource is null)
        {
            return "Class file missing";
        }

        if (classRecord is null)
        {
            return "Class file unsupported";
        }

        if (classOwnership is null)
        {
            return "No loaded trainer owner";
        }

        return classOwnership.HasMultipleOwners
            ? "Shared trainer class"
            : $"Unique trainer class: {classOwnership.OwnerName}";
    }

    private static string FormatBall(int ballId)
    {
        return BallOptions.FirstOrDefault(option => option.Value == ballId)?.Label ?? $"Ball {ballId}";
    }

    internal static IReadOnlyList<SwShTrainerAiFlagState> CreateAiFlagStates(uint aiFlags)
    {
        return AiFlagDefinitions
            .Select(definition =>
            {
                var mask = 1 << definition.Bit;
                return new SwShTrainerAiFlagState(
                    definition.Bit,
                    mask,
                    definition.Label,
                    definition.Description,
                    (aiFlags & (uint)mask) != 0);
            })
            .ToArray();
    }

    internal static string FormatTrainerPokemonGender(int value)
    {
        return GetOptionLabel(GenderOptions, value, "Gender");
    }

    internal static string FormatTrainerPokemonAbility(int value)
    {
        return GetOptionLabel(AbilityOptions, value, "Ability");
    }

    private static IReadOnlyList<SwShTrainerEditableFieldOption> CreateTrainerPokemonAbilityOptions(
        TrainerLookupTables names,
        int speciesId,
        int form)
    {
        return names.AbilityResolver.CreateOptions(speciesId, form, SwShAbilityOptionMode.DefaultPlusSlots)
            .Select(option => new SwShTrainerEditableFieldOption(option.Value, option.Label))
            .ToArray();
    }

    private static string GetTrainerPokemonAbilityLabel(
        TrainerLookupTables names,
        int speciesId,
        int form,
        int value)
    {
        return CreateTrainerPokemonAbilityOptions(names, speciesId, form)
            .FirstOrDefault(option => option.Value == value)?.Label
            ?? FormatTrainerPokemonAbility(value);
    }

    internal static string FormatTrainerPokemonNature(int value)
    {
        return GetOptionLabel(NatureOptions, value, "Nature");
    }

    internal static string GetOptionLabel(
        IReadOnlyList<SwShTrainerEditableFieldOption> options,
        int value,
        string fallbackPrefix)
    {
        return options.FirstOrDefault(option => option.Value == value)?.Label
            ?? $"{fallbackPrefix} {value.ToString(CultureInfo.InvariantCulture)}";
    }

    private static SwShTrainerProvenance CreateProvenance(
        ProjectFileGraphEntry dataEntry,
        ProjectFileGraphEntry teamEntry,
        ProjectFileGraphEntry? classEntry)
    {
        var dataSourceLayer = dataEntry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;
        var teamSourceLayer = teamEntry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;
        var classSourceLayer = classEntry?.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : classEntry is not null
                ? ProjectFileLayer.Base
                : (ProjectFileLayer?)null;

        return new SwShTrainerProvenance(
            dataEntry.RelativePath,
            teamEntry.RelativePath,
            classEntry?.RelativePath,
            dataSourceLayer,
            teamSourceLayer,
            classSourceLayer,
            dataEntry.State,
            teamEntry.State,
            classEntry?.State);
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
        IReadOnlyList<string> SpriteSpeciesNames,
        IReadOnlySet<int> PresentSpeciesIds,
        IReadOnlyList<string> ItemNames,
        IReadOnlyList<string> MoveNames,
        IReadOnlySet<int> UsableMoveIds,
        SwShPokemonAbilityOptionResolver AbilityResolver);

    private sealed record TrainerClassOwnership(
        string OwnerName,
        bool HasMultipleOwners);

    private sealed record TrainerAiFlagDefinition(
        int Bit,
        string Label,
        string Description);
}
