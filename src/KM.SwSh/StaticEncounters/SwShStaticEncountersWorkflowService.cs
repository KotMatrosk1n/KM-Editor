// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Pokemon;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.StaticEncounters;

public sealed class SwShStaticEncountersWorkflowService
{
    public const string SpeciesField = "species";
    public const string FormField = "form";
    public const string LevelField = "level";
    public const string HeldItemIdField = "heldItemId";
    public const string AbilityField = "ability";
    public const string NatureField = "nature";
    public const string GenderField = "gender";
    public const string ShinyLockField = "shinyLock";
    public const string EncounterScenarioField = "encounterScenario";
    public const string DynamaxLevelField = "dynamaxLevel";
    public const string CanGigantamaxField = "canGigantamax";
    public const string Move0Field = "move0Id";
    public const string Move1Field = "move1Id";
    public const string Move2Field = "move2Id";
    public const string Move3Field = "move3Id";
    public const string EvHpField = "evHp";
    public const string EvAttackField = "evAttack";
    public const string EvDefenseField = "evDefense";
    public const string EvSpeedField = "evSpeed";
    public const string EvSpecialAttackField = "evSpecialAttack";
    public const string EvSpecialDefenseField = "evSpecialDefense";
    public const string IvHpField = "ivHp";
    public const string IvAttackField = "ivAttack";
    public const string IvDefenseField = "ivDefense";
    public const string IvSpeedField = "ivSpeed";
    public const string IvSpecialAttackField = "ivSpecialAttack";
    public const string IvSpecialDefenseField = "ivSpecialDefense";
    public const string FlawlessIvCountField = "flawlessIvCount";
    public const string StaticEncounterDataPath = "romfs/bin/script_event_data/event_encount_data.bin";
    public const string LegacyStaticEncounterDataPath = "romfs/bin/script_event_data/event_encount.bin";

    internal const string StaticEncountersEditDomain = "workflow.staticEncounters";

    private const string MessageRootPath = "romfs/bin/message";
    private const string PreferredLanguage = "English";

    private static readonly IReadOnlyList<SwShStaticEncounterEditableFieldOption> BooleanOptions =
    [
        new(0, "No"),
        new(1, "Yes"),
    ];

    private static readonly IReadOnlyList<SwShStaticEncounterEditableFieldOption> AbilityOptions =
    [
        new(0, "Default"),
        new(1, "Ability 1"),
        new(2, "Ability 2"),
        new(3, "Hidden Ability"),
    ];

    private static readonly IReadOnlyList<SwShStaticEncounterEditableFieldOption> GenderOptions =
    [
        new(0, "Random"),
        new(1, "Male"),
        new(2, "Female"),
    ];

    private static readonly IReadOnlyList<SwShStaticEncounterEditableFieldOption> ShinyLockOptions =
    [
        new(0, "Random"),
        new(1, "Always Shiny"),
        new(2, "Never Shiny"),
    ];

    private static readonly IReadOnlyList<SwShStaticEncounterEditableFieldOption> FlawlessIvCountOptions =
    [
        new(0, "Random IVs"),
        new(3, "3 Guaranteed Perfect IVs"),
        new(6, "6 Perfect IVs"),
    ];

    private static readonly IReadOnlyList<SwShStaticEncounterEditableFieldOption> FormOptions =
    [
        new(0, "Base"),
        ..Enumerable.Range(1, 31).Select(value => new SwShStaticEncounterEditableFieldOption(value, $"Form {value}")),
    ];

    private static readonly IReadOnlyList<SwShStaticEncounterEditableFieldOption> DynamaxLevelOptions =
    [
        ..Enumerable.Range(0, 11).Select(value => new SwShStaticEncounterEditableFieldOption(
            value,
            value == 0 ? "0 Off" : $"Level {value}")),
    ];

    private static readonly IReadOnlyList<SwShStaticEncounterEditableFieldOption> NatureOptions =
    [
        new(0, "Hardy"),
        new(1, "Lonely"),
        new(2, "Brave"),
        new(3, "Adamant"),
        new(4, "Naughty"),
        new(5, "Bold"),
        new(6, "Docile"),
        new(7, "Relaxed"),
        new(8, "Impish"),
        new(9, "Lax"),
        new(10, "Timid"),
        new(11, "Hasty"),
        new(12, "Serious"),
        new(13, "Jolly"),
        new(14, "Naive"),
        new(15, "Modest"),
        new(16, "Mild"),
        new(17, "Quiet"),
        new(18, "Bashful"),
        new(19, "Rash"),
        new(20, "Calm"),
        new(21, "Gentle"),
        new(22, "Sassy"),
        new(23, "Careful"),
        new(24, "Quirky"),
        new(25, "Random"),
    ];

    private static readonly IReadOnlyList<SwShStaticEncounterEditableFieldOption> EncounterScenarioOptions =
    [
        new(0, "None"),
        new(1, "Legendary Pokemon"),
        new(2, "Scenario 2"),
        new(3, "Scenario 3"),
        new(4, "Eternatus"),
        new(5, "Eternamax Eternatus 1"),
        new(6, "Eternamax Eternatus 2"),
        new(7, "Zacian / Zamazenta Fog"),
        new(8, "Motostoke Gym Challenge"),
        new(9, "Max Raid Battle 1"),
        new(10, "Max Raid Battle 2"),
        new(11, "Max Raid Battle 3"),
        new(12, "Max Raid Battle 4"),
        new(13, "Zacian / Zamazenta Boss"),
        new(14, "Fast Slowpoke"),
        new(15, "Regigigas Raid Battle"),
        new(16, "Special Raid Battle"),
        new(17, "Calyrex"),
        new(18, "Glastrier / Spectrier"),
        new(19, "Calyrex Fusion"),
    ];

    private static readonly IReadOnlyList<SwShStaticEncounterEditableField> BaseEditableFields =
    [
        CreateField(SpeciesField, "Species", "integer", 0, SwShStaticEncounterArchive.MaximumIdValue),
        CreateField(FormField, "Form", "integer", 0, 31, FormOptions),
        CreateField(LevelField, "Level", "integer", 0, SwShStaticEncounterArchive.MaximumByteValue),
        CreateField(HeldItemIdField, "Held item", "integer", 0, SwShStaticEncounterArchive.MaximumIdValue),
        CreateField(AbilityField, "Ability slot", "integer", 0, 3, AbilityOptions),
        CreateField(NatureField, "Nature", "integer", 0, 25, NatureOptions),
        CreateField(GenderField, "Gender", "integer", 0, 2, GenderOptions),
        CreateField(ShinyLockField, "Shiny lock", "integer", 0, 2, ShinyLockOptions),
        CreateField(EncounterScenarioField, "Scenario", "integer", 0, SwShStaticEncounterArchive.MaximumIdValue, EncounterScenarioOptions),
        CreateField(DynamaxLevelField, "Dynamax level", "integer", 0, 10, DynamaxLevelOptions),
        CreateField(CanGigantamaxField, "Can Gigantamax", "boolean", 0, 1, BooleanOptions),
        CreateField(Move0Field, "Move 1", "integer", 0, SwShStaticEncounterArchive.MaximumIdValue),
        CreateField(Move1Field, "Move 2", "integer", 0, SwShStaticEncounterArchive.MaximumIdValue),
        CreateField(Move2Field, "Move 3", "integer", 0, SwShStaticEncounterArchive.MaximumIdValue),
        CreateField(Move3Field, "Move 4", "integer", 0, SwShStaticEncounterArchive.MaximumIdValue),
        CreateField(EvHpField, "HP EV", "integer", 0, SwShStaticEncounterArchive.MaximumByteValue),
        CreateField(EvAttackField, "Attack EV", "integer", 0, SwShStaticEncounterArchive.MaximumByteValue),
        CreateField(EvDefenseField, "Defense EV", "integer", 0, SwShStaticEncounterArchive.MaximumByteValue),
        CreateField(EvSpecialAttackField, "Sp. Atk EV", "integer", 0, SwShStaticEncounterArchive.MaximumByteValue),
        CreateField(EvSpecialDefenseField, "Sp. Def EV", "integer", 0, SwShStaticEncounterArchive.MaximumByteValue),
        CreateField(EvSpeedField, "Speed EV", "integer", 0, SwShStaticEncounterArchive.MaximumByteValue),
        CreateField(IvHpField, "HP IV", "integer", SwShStaticEncounterArchive.ThreePerfectIvSentinel, SwShStaticEncounterArchive.MaximumFixedIvValue),
        CreateField(IvAttackField, "Attack IV", "integer", SwShStaticEncounterArchive.RandomIvValue, SwShStaticEncounterArchive.MaximumFixedIvValue),
        CreateField(IvDefenseField, "Defense IV", "integer", SwShStaticEncounterArchive.RandomIvValue, SwShStaticEncounterArchive.MaximumFixedIvValue),
        CreateField(IvSpeedField, "Speed IV", "integer", SwShStaticEncounterArchive.RandomIvValue, SwShStaticEncounterArchive.MaximumFixedIvValue),
        CreateField(IvSpecialAttackField, "Sp. Atk IV", "integer", SwShStaticEncounterArchive.RandomIvValue, SwShStaticEncounterArchive.MaximumFixedIvValue),
        CreateField(IvSpecialDefenseField, "Sp. Def IV", "integer", SwShStaticEncounterArchive.RandomIvValue, SwShStaticEncounterArchive.MaximumFixedIvValue),
        CreateField(FlawlessIvCountField, "IV preset", "integer", 0, 6, FlawlessIvCountOptions),
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
                    "Static Encounters requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShStaticEncountersWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);
        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(summary, [], sourceFileCount: 0, CreateEmptyLookupTables(), diagnostics);
        }

        var source = ResolveStaticEncounterDataSource(project);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Static encounter data is not available for this project.",
                expected: StaticEncounterDataPath));
            return CreateWorkflow(summary, [], sourceFileCount: 0, CreateEmptyLookupTables(), diagnostics);
        }

        var lookupTables = LoadLookupTables(project, diagnostics);

        try
        {
            var archive = SwShStaticEncounterArchive.Parse(File.ReadAllBytes(source.AbsolutePath));
            var provenance = CreateProvenance(source.GraphEntry);
            var encounters = archive.Encounters
                .Select(encounter => ToEncounterEntry(encounter, lookupTables, provenance))
                .ToArray();
            var sourceFileCount = 1 + lookupTables.SourceFileCount;

            return CreateWorkflow(summary, encounters, sourceFileCount, lookupTables, diagnostics);
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Static encounter data source is not a supported Sword/Shield static encounter table: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Sword/Shield static encounter table"));
            return CreateWorkflow(summary, [], sourceFileCount: 1, lookupTables, diagnostics);
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Static encounter data source could not be read: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield static encounter table"));
            return CreateWorkflow(summary, [], sourceFileCount: 1, lookupTables, diagnostics);
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Static encounter data source could not be read: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield static encounter table"));
            return CreateWorkflow(summary, [], sourceFileCount: 1, lookupTables, diagnostics);
        }
    }

    internal static SwShStaticEncounterEditableField? GetEditableField(string? field)
    {
        return BaseEditableFields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, field, StringComparison.Ordinal));
    }

    internal static bool IsEditableField(string? field)
    {
        return GetEditableField(field) is not null;
    }

    internal static string CreateEncounterRecordId(int encounterIndex)
    {
        return $"static:{encounterIndex.ToString(CultureInfo.InvariantCulture)}";
    }

    internal static bool TryParseEncounterRecordId(string? recordId, out int encounterIndex)
    {
        encounterIndex = 0;

        const string prefix = "static:";
        return recordId is not null
            && recordId.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(recordId[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out encounterIndex)
            && encounterIndex >= 0;
    }

    internal static WorkflowFileSource? ResolveStaticEncounterDataSource(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return ResolveWorkflowFile(project, StaticEncounterDataPath)
            ?? ResolveWorkflowFile(project, LegacyStaticEncounterDataPath);
    }

    internal static string? ResolveOutputPath(ProjectPaths paths, string targetRelativePath)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRelativePath);

        if (string.IsNullOrWhiteSpace(paths.OutputRootPath) || Path.IsPathRooted(targetRelativePath))
        {
            return null;
        }

        var outputRoot = Path.GetFullPath(paths.OutputRootPath);
        var targetPath = Path.GetFullPath(Path.Combine(
            outputRoot,
            targetRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        var pathFromOutputRoot = Path.GetRelativePath(outputRoot, targetPath);

        return !pathFromOutputRoot.StartsWith("..", StringComparison.Ordinal)
            && !Path.IsPathRooted(pathFromOutputRoot)
            ? targetPath
            : null;
    }

    private static SwShStaticEncountersWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShStaticEncounterEntry> encounters,
        int sourceFileCount,
        StaticEncounterLookupTables lookupTables,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SwShStaticEncountersWorkflow(
            summary,
            encounters,
            CreateEditableFields(lookupTables),
            new SwShStaticEncountersWorkflowStats(
                encounters.Count,
                encounters.Count(encounter => encounter.CanGigantamax),
                encounters.Count(encounter => encounter.FlawlessIvCount is null),
                sourceFileCount),
            diagnostics);
    }

    private static StaticEncounterLookupTables CreateEmptyLookupTables()
    {
        return new StaticEncounterLookupTables([], [], [], SourceFileCount: 0);
    }

    private static IReadOnlyList<SwShStaticEncounterEditableField> CreateEditableFields(StaticEncounterLookupTables lookupTables)
    {
        var speciesOptions = CreateIndexedOptions(lookupTables.SpeciesNames, "Species");
        var itemOptions = CreateIndexedOptions(lookupTables.ItemNames, "Item");
        var moveOptions = CreateIndexedOptions(lookupTables.MoveNames, "Move");

        return BaseEditableFields
            .Select(field => field.Field switch
            {
                SpeciesField => field with { Options = speciesOptions },
                HeldItemIdField => field with { Options = itemOptions },
                Move0Field or Move1Field or Move2Field or Move3Field => field with { Options = moveOptions },
                _ => field,
            })
            .ToArray();
    }

    private static IReadOnlyList<SwShStaticEncounterEditableFieldOption> CreateIndexedOptions(
        IReadOnlyList<string> names,
        string fallbackPrefix)
    {
        return names
            .Select((name, index) => new SwShStaticEncounterEditableFieldOption(
                index,
                string.IsNullOrWhiteSpace(name)
                    ? $"{index.ToString("000", CultureInfo.InvariantCulture)} {fallbackPrefix} {index}"
                    : $"{index.ToString("000", CultureInfo.InvariantCulture)} {name}"))
            .ToArray();
    }

    private static SwShStaticEncounterEntry ToEncounterEntry(
        SwShStaticEncounterRecord encounter,
        StaticEncounterLookupTables lookupTables,
        SwShStaticEncounterProvenance provenance)
    {
        var evs = ToStatsRecord(encounter.Evs);
        var ivs = ToStatsRecord(encounter.Ivs);
        var flawlessIvCount = SwShStaticEncounterArchive.GetFlawlessIvCount(encounter.Ivs);
        var species = GetIndexedName(encounter.Species, lookupTables.SpeciesNames, "Species");
        var heldItem = encounter.HeldItem == 0
            ? null
            : GetIndexedName(encounter.HeldItem, lookupTables.ItemNames, "Item");
        var moves = encounter.Moves
            .Select((moveId, slot) => new SwShStaticEncounterMoveRecord(
                slot,
                moveId,
                moveId == 0 ? null : GetIndexedName(moveId, lookupTables.MoveNames, "Move")))
            .ToArray();
        var scenarioLabel = GetOptionLabel(EncounterScenarioOptions, encounter.EncounterScenario, "Scenario");

        return new SwShStaticEncounterEntry(
            encounter.Index,
            FormatEncounterLabel(encounter.Index, species, encounter.Species, encounter.Form, encounter.Level, scenarioLabel, moves),
            $"0x{encounter.EncounterId:X16}",
            encounter.Species,
            species,
            encounter.Form,
            encounter.Level,
            encounter.HeldItem,
            heldItem,
            encounter.Ability,
            GetOptionLabel(AbilityOptions, encounter.Ability, "Ability slot"),
            encounter.Nature,
            GetOptionLabel(NatureOptions, encounter.Nature, "Nature"),
            encounter.Gender,
            GetOptionLabel(GenderOptions, encounter.Gender, "Gender"),
            encounter.ShinyLock,
            GetOptionLabel(ShinyLockOptions, encounter.ShinyLock, "Shiny lock"),
            encounter.EncounterScenario,
            scenarioLabel,
            encounter.DynamaxLevel,
            encounter.CanGigantamax,
            evs,
            ivs,
            flawlessIvCount,
            FormatIvSummary(ivs, flawlessIvCount),
            moves,
            provenance);
    }

    private static SwShStaticEncounterStatsRecord ToStatsRecord(SwShStaticEncounterStats stats)
    {
        return new SwShStaticEncounterStatsRecord(
            stats.HP,
            stats.Attack,
            stats.Defense,
            stats.SpecialAttack,
            stats.SpecialDefense,
            stats.Speed);
    }

    internal static string FormatEncounterLabel(
        int encounterIndex,
        string species,
        int speciesId,
        int form,
        int level,
        string scenarioLabel,
        IReadOnlyList<SwShStaticEncounterMoveRecord> moves)
    {
        var speciesLabel = SwShSpeciesFormLabels.FormatSpeciesFormLabel(species, speciesId, form);
        var scenarioText = string.Equals(scenarioLabel, "None", StringComparison.Ordinal)
            ? string.Empty
            : $" | {scenarioLabel}";
        var moveText = string.Join(", ", moves
            .Where(move => move.MoveId > 0)
            .Take(2)
            .Select(move => move.Move ?? $"Move {move.MoveId.ToString(CultureInfo.InvariantCulture)}"));

        return moveText.Length == 0
            ? $"Static {(encounterIndex + 1).ToString("000", CultureInfo.InvariantCulture)}: {speciesLabel} Lv. {level}{scenarioText}"
            : $"Static {(encounterIndex + 1).ToString("000", CultureInfo.InvariantCulture)}: {speciesLabel} Lv. {level}{scenarioText} | {moveText}";
    }

    internal static string FormatIvSummary(SwShStaticEncounterStatsRecord ivs, int? flawlessIvCount)
    {
        return flawlessIvCount switch
        {
            0 => "Random IVs",
            3 => "3 guaranteed perfect IVs",
            6 => "6 perfect IVs",
            _ => string.Create(
                CultureInfo.InvariantCulture,
                $"HP {ivs.HP} / Atk {ivs.Attack} / Def {ivs.Defense} / SpA {ivs.SpecialAttack} / SpD {ivs.SpecialDefense} / Spe {ivs.Speed}"),
        };
    }

    internal static string GetOptionLabel(
        IReadOnlyList<SwShStaticEncounterEditableFieldOption> options,
        int value,
        string fallbackPrefix)
    {
        return options.FirstOrDefault(option => option.Value == value)?.Label
            ?? $"{fallbackPrefix} {value.ToString(CultureInfo.InvariantCulture)}";
    }

    internal static string GetIndexedName(int id, IReadOnlyList<string> names, string fallbackPrefix)
    {
        return (uint)id < (uint)names.Count && !string.IsNullOrWhiteSpace(names[id])
            ? names[id]
            : $"{fallbackPrefix} {id.ToString(CultureInfo.InvariantCulture)}";
    }

    private static StaticEncounterLookupTables LoadLookupTables(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var messageRoot = ResolveLanguageMessageRoot(project, diagnostics);
        var speciesNames = LoadMessageTable(project, messageRoot, "monsname.dat", diagnostics);
        var itemNames = LoadMessageTable(project, messageRoot, "itemname.dat", diagnostics);
        var moveNames = LoadMessageTable(project, messageRoot, "wazaname.dat", diagnostics);

        return new StaticEncounterLookupTables(
            speciesNames,
            itemNames,
            moveNames,
            CountSource(speciesNames) + CountSource(itemNames) + CountSource(moveNames));
    }

    private static int CountSource(IReadOnlyList<string> values)
    {
        return values.Count > 0 ? 1 : 0;
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
                "Static encounter lookup text is not available; numeric fallback labels will be shown.",
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
                $"English static encounter lookup text was not found; using '{language}' lookup tables instead.",
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
            return [];
        }

        var relativePath = $"{messageRoot}/{fileName}";
        var source = ResolveWorkflowFile(project, relativePath);
        if (source is null)
        {
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
                $"Static encounter lookup table '{relativePath}' could not be decoded: {exception.Message}",
                file: relativePath,
                expected: "Sword/Shield message table"));
            return [];
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Static encounter lookup table '{relativePath}' could not be read: {exception.Message}",
                file: relativePath,
                expected: "Readable message table"));
            return [];
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Static encounter lookup table '{relativePath}' could not be read: {exception.Message}",
                file: relativePath,
                expected: "Readable message table"));
            return [];
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

    private static SwShStaticEncounterProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShStaticEncounterProvenance(entry.RelativePath, sourceLayer, entry.State);
    }

    private static SwShStaticEncounterEditableField CreateField(
        string field,
        string label,
        string valueKind,
        int? minimumValue,
        int? maximumValue,
        IReadOnlyList<SwShStaticEncounterEditableFieldOption>? options = null)
    {
        return new SwShStaticEncounterEditableField(
            field,
            label,
            valueKind,
            minimumValue,
            maximumValue,
            options ?? Array.Empty<SwShStaticEncounterEditableFieldOption>());
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.StaticEncounters,
            "Static Encounters",
            "Scripted overworld and story encounter records, IV modes, moves, rules, and source provenance.",
            availability,
            diagnostics);
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? file = null,
        string? expected = null,
        string? field = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Domain: StaticEncountersEditDomain,
            Field: field,
            Expected: expected);
    }

    internal sealed record WorkflowFileSource(
        ProjectFileGraphEntry GraphEntry,
        string AbsolutePath);

    private sealed record StaticEncounterLookupTables(
        IReadOnlyList<string> SpeciesNames,
        IReadOnlyList<string> ItemNames,
        IReadOnlyList<string> MoveNames,
        int SourceFileCount);
}
