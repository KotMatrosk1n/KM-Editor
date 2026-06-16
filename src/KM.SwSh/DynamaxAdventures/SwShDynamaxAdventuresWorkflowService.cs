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

namespace KM.SwSh.DynamaxAdventures;

public sealed class SwShDynamaxAdventuresWorkflowService
{
    public const string SpeciesField = "species";
    public const string BossTargetSpeciesField = "bossTargetSpecies";
    public const string FormField = "form";
    public const string LevelField = "level";
    public const string BallItemIdField = "ballItemId";
    public const string AbilityField = "ability";
    public const string GigantamaxStateField = "gigantamaxState";
    public const string VersionField = "version";
    public const string ShinyRollField = "shinyRoll";
    public const string Move0Field = "move0Id";
    public const string Move1Field = "move1Id";
    public const string Move2Field = "move2Id";
    public const string Move3Field = "move3Id";
    public const string GuaranteedPerfectIvsField = "guaranteedPerfectIvs";
    public const string IvAttackField = "ivAttack";
    public const string IvDefenseField = "ivDefense";
    public const string IvSpeedField = "ivSpeed";
    public const string IvSpecialAttackField = "ivSpecialAttack";
    public const string IvSpecialDefenseField = "ivSpecialDefense";
    public const string IsSingleCaptureField = "isSingleCapture";
    public const string IsStoryProgressGatedField = "isStoryProgressGated";
    public const string OtGenderField = "otGender";
    public const string DynamaxAdventureDataPath = "romfs/bin/appli/chika/data_table/underground_exploration_poke.bin";

    internal const string DynamaxAdventuresEditDomain = "workflow.dynamaxAdventures";

    private const string MessageRootPath = "romfs/bin/message";
    private const string PreferredLanguage = "English";

    private static readonly IReadOnlyList<SwShDynamaxAdventureEditableFieldOption> AbilityOptions =
    [
        new(0, "Ability 1"),
        new(1, "Ability 2"),
        new(2, "Hidden Ability"),
    ];

    private static readonly IReadOnlyList<SwShDynamaxAdventureEditableFieldOption> GigantamaxOptions =
    [
        new(0, "Unknown"),
        new(1, "Normal"),
        new(2, "Gigantamax"),
    ];

    private static readonly IReadOnlyList<SwShDynamaxAdventureEditableFieldOption> VersionOptions =
    [
        new(0, "Both"),
        new(1, "Sword"),
        new(2, "Shield"),
    ];

    private static readonly IReadOnlyList<SwShDynamaxAdventureEditableFieldOption> ShinyRollOptions =
    [
        new(0, "Unknown"),
        new(1, "Enabled"),
        new(2, "Disabled"),
    ];

    private static readonly IReadOnlyList<SwShDynamaxAdventureEditableFieldOption> BooleanOptions =
    [
        new(0, "No"),
        new(1, "Yes"),
    ];

    private static readonly IReadOnlyList<SwShDynamaxAdventureEditableFieldOption> OtGenderOptions =
    [
        new(0, "Male"),
        new(1, "Female"),
    ];

    private static readonly IReadOnlyList<SwShDynamaxAdventureEditableFieldOption> FormOptions =
    [
        new(0, "Base"),
        ..Enumerable.Range(1, 31).Select(value => new SwShDynamaxAdventureEditableFieldOption(value, $"Form {value}")),
    ];

    private static readonly IReadOnlyList<SwShDynamaxAdventureEditableFieldOption> GuaranteedPerfectIvOptions =
    [
        new(0, "Random IVs"),
        new(2, "2 Guaranteed Perfect IVs"),
        new(3, "3 Guaranteed Perfect IVs"),
        new(4, "4 Guaranteed Perfect IVs"),
        new(5, "5 Guaranteed Perfect IVs"),
        new(6, "6 Guaranteed Perfect IVs"),
    ];

    private static readonly IReadOnlyList<SwShDynamaxAdventureEditableFieldOption> IvOverrideOptions =
    [
        new(SwShDynamaxAdventureArchive.RandomIvValue, "Random"),
        ..Enumerable.Range(0, 32).Select(value => new SwShDynamaxAdventureEditableFieldOption(value, $"{value} IV")),
    ];

    // Fields here are covered by the loose DA table builder plus the ExeFS summary and command mirror patcher.
    private static readonly HashSet<string> SafeEditableFieldNames =
    [
        SpeciesField,
        FormField,
        LevelField,
        AbilityField,
        GigantamaxStateField,
        Move0Field,
        Move1Field,
        Move2Field,
        Move3Field,
        GuaranteedPerfectIvsField,
        IvAttackField,
        IvDefenseField,
        IvSpecialAttackField,
        IvSpecialDefenseField,
        IvSpeedField,
    ];

    private static readonly HashSet<int> GigantamaxCapableSpecies =
    [
        3,   // Venusaur
        6,   // Charizard
        9,   // Blastoise
        12,  // Butterfree
        25,  // Pikachu
        52,  // Meowth
        68,  // Machamp
        94,  // Gengar
        99,  // Kingler
        131, // Lapras
        133, // Eevee
        143, // Snorlax
        569, // Garbodor
        809, // Melmetal
        812, // Rillaboom
        815, // Cinderace
        818, // Inteleon
        823, // Corviknight
        826, // Orbeetle
        834, // Drednaw
        839, // Coalossal
        841, // Flapple
        842, // Appletun
        844, // Sandaconda
        849, // Toxtricity
        851, // Centiskorch
        858, // Hatterene
        861, // Grimmsnarl
        869, // Alcremie
        879, // Copperajah
        884, // Duraludon
        892, // Urshifu
    ];

    private static readonly IReadOnlyList<SwShDynamaxAdventureEditableField> BaseEditableFields =
    [
        CreateField(SpeciesField, "Species", "integer", 0, SwShDynamaxAdventureArchive.MaximumIdValue),
        CreateField(BossTargetSpeciesField, "Boss target species", "integer", 0, SwShDynamaxAdventureArchive.MaximumIdValue),
        CreateField(FormField, "Form", "integer", 0, SwShDynamaxAdventureArchive.MaximumByteValue, FormOptions),
        CreateField(LevelField, "Level", "integer", 1, 100),
        CreateField(BallItemIdField, "Ball item", "integer", 0, SwShDynamaxAdventureArchive.MaximumIdValue),
        CreateField(AbilityField, "Ability roll", "integer", 0, SwShDynamaxAdventureArchive.MaximumAbilityRoll, AbilityOptions),
        CreateField(GigantamaxStateField, "Gigantamax state", "integer", 0, SwShDynamaxAdventureArchive.MaximumGigantamaxState, GigantamaxOptions),
        CreateField(VersionField, "Game version", "integer", 0, SwShDynamaxAdventureArchive.MaximumVersion, VersionOptions),
        CreateField(ShinyRollField, "Shiny roll", "integer", 0, SwShDynamaxAdventureArchive.MaximumShinyRoll, ShinyRollOptions),
        CreateField(Move0Field, "Move 1", "integer", 0, SwShDynamaxAdventureArchive.MaximumIdValue),
        CreateField(Move1Field, "Move 2", "integer", 0, SwShDynamaxAdventureArchive.MaximumIdValue),
        CreateField(Move2Field, "Move 3", "integer", 0, SwShDynamaxAdventureArchive.MaximumIdValue),
        CreateField(Move3Field, "Move 4", "integer", 0, SwShDynamaxAdventureArchive.MaximumIdValue),
        CreateField(GuaranteedPerfectIvsField, "Guaranteed perfect IVs", "integer", 0, SwShDynamaxAdventureArchive.MaximumGuaranteedPerfectIvs, GuaranteedPerfectIvOptions),
        CreateField(IvAttackField, "Attack IV override", "integer", SwShDynamaxAdventureArchive.RandomIvValue, SwShDynamaxAdventureArchive.MaximumFixedIvValue, IvOverrideOptions),
        CreateField(IvDefenseField, "Defense IV override", "integer", SwShDynamaxAdventureArchive.RandomIvValue, SwShDynamaxAdventureArchive.MaximumFixedIvValue, IvOverrideOptions),
        CreateField(IvSpecialAttackField, "Sp. Atk IV override", "integer", SwShDynamaxAdventureArchive.RandomIvValue, SwShDynamaxAdventureArchive.MaximumFixedIvValue, IvOverrideOptions),
        CreateField(IvSpecialDefenseField, "Sp. Def IV override", "integer", SwShDynamaxAdventureArchive.RandomIvValue, SwShDynamaxAdventureArchive.MaximumFixedIvValue, IvOverrideOptions),
        CreateField(IvSpeedField, "Speed IV override", "integer", SwShDynamaxAdventureArchive.RandomIvValue, SwShDynamaxAdventureArchive.MaximumFixedIvValue, IvOverrideOptions),
        CreateField(IsSingleCaptureField, "Single-capture Pokemon", "integer", 0, 1, BooleanOptions),
        CreateField(IsStoryProgressGatedField, "Requires story progress", "integer", 0, 1, BooleanOptions),
        CreateField(OtGenderField, "OT gender", "integer", 0, 1, OtGenderOptions),
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
                    "Dynamax Adventures requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShDynamaxAdventuresWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);
        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(summary, [], sourceFileCount: 0, CreateEmptyLookupTables(), diagnostics);
        }

        var source = ResolveDynamaxAdventureDataSource(project);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Dynamax Adventures data is not available for this project.",
                expected: DynamaxAdventureDataPath));
            return CreateWorkflow(summary, [], sourceFileCount: 0, CreateEmptyLookupTables(), diagnostics);
        }

        var lookupTables = LoadLookupTables(project, diagnostics);

        try
        {
            var sourceBytes = File.ReadAllBytes(source.AbsolutePath);
            var archive = SwShDynamaxAdventureArchive.Parse(sourceBytes);
            var vanillaArchive = LoadVanillaArchive(project, diagnostics);
            if (vanillaArchive is not null && sourceBytes.Length != vanillaArchive.Length)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    "Dynamax Adventures source table layout differs from the vanilla table. Restore the generated Adventure table before making new Pokemon edits.",
                    file: source.GraphEntry.RelativePath,
                    expected: "Vanilla-length Dynamax Adventures table"));
            }

            var provenance = CreateProvenance(source.GraphEntry);
            var bossSpeciesForms = archive.Entries
                .Where(entry => SwShDynamaxAdventureSafetyRules.IsBossEntryIndex(entry.EntryIndex))
                .Select(entry => (entry.Species, entry.Form))
                .ToHashSet();
            var encounters = archive.Entries
                .Select(entry => ToEncounterEntry(
                    entry,
                    archive.Entries,
                    lookupTables,
                    provenance,
                    IsSafeEditableEncounter(entry, bossSpeciesForms, lookupTables.PersonalRecords),
                    null,
                    vanillaArchive?.Archive.Entries.FirstOrDefault(vanillaEntry => vanillaEntry.EntryIndex == entry.EntryIndex)))
                .ToArray();
            var sourceFileCount = 1 + lookupTables.SourceFileCount;

            return CreateWorkflow(summary, encounters, sourceFileCount, lookupTables, diagnostics);
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventures data source is not a supported Sword/Shield table: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Sword/Shield Dynamax Adventures table"));
            return CreateWorkflow(summary, [], sourceFileCount: 1, lookupTables, diagnostics);
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventures data source could not be read: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield Dynamax Adventures table"));
            return CreateWorkflow(summary, [], sourceFileCount: 1, lookupTables, diagnostics);
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventures data source could not be read: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield Dynamax Adventures table"));
            return CreateWorkflow(summary, [], sourceFileCount: 1, lookupTables, diagnostics);
        }
    }

    internal static SwShDynamaxAdventureEditableField? GetEditableField(string? field)
    {
        return BaseEditableFields.FirstOrDefault(candidate =>
            SafeEditableFieldNames.Contains(candidate.Field)
            && string.Equals(candidate.Field, field, StringComparison.Ordinal));
    }

    internal static bool IsEditableField(string? field)
    {
        return GetEditableField(field) is not null;
    }

    internal static bool IsGigantamaxCapableSpecies(int speciesId)
    {
        return GigantamaxCapableSpecies.Contains(speciesId);
    }

    internal static string CreateEncounterRecordId(int entryIndex)
    {
        return $"dynamaxAdventure:{entryIndex.ToString(CultureInfo.InvariantCulture)}";
    }

    internal static bool TryParseEncounterRecordId(string? recordId, out int entryIndex)
    {
        entryIndex = 0;

        const string prefix = "dynamaxAdventure:";
        return recordId is not null
            && recordId.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(recordId[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out entryIndex)
            && entryIndex >= 0;
    }

    internal static WorkflowFileSource? ResolveDynamaxAdventureDataSource(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return ResolveWorkflowFile(project, DynamaxAdventureDataPath);
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

    internal static string GetOptionLabel(
        IReadOnlyList<SwShDynamaxAdventureEditableFieldOption> options,
        int value,
        string fallbackPrefix)
    {
        return options.FirstOrDefault(option => option.Value == value)?.Label
            ?? $"{fallbackPrefix} {value.ToString(CultureInfo.InvariantCulture)}";
    }

    private static SwShDynamaxAdventuresWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShDynamaxAdventureEntry> encounters,
        int sourceFileCount,
        DynamaxAdventureLookupTables lookupTables,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        var safeNormalSpeciesOptions = CreateSafeNormalSpeciesOptions(encounters, lookupTables);
        return new SwShDynamaxAdventuresWorkflow(
            summary,
            encounters,
            CreateEditableFields(lookupTables, safeNormalSpeciesOptions),
            safeNormalSpeciesOptions,
            new SwShDynamaxAdventuresWorkflowStats(
                encounters.Count,
                encounters.Count(encounter => encounter.IsSingleCapture),
                encounters.Count(encounter => encounter.IsStoryProgressGated),
                encounters.Count(encounter => encounter.GuaranteedPerfectIvs > 0),
                sourceFileCount),
            diagnostics);
    }

    private static DynamaxAdventureLookupTables CreateEmptyLookupTables()
    {
        return new DynamaxAdventureLookupTables([], new HashSet<int>(), [], [], new HashSet<int>(), [], [], SwShPokemonAbilityOptionResolver.Empty, SourceFileCount: 0);
    }

    private static IReadOnlyList<SwShDynamaxAdventureEditableField> CreateEditableFields(
        DynamaxAdventureLookupTables lookupTables,
        IReadOnlyList<SwShDynamaxAdventureEditableFieldOption> safeNormalSpeciesOptions)
    {
        var itemOptions = CreateIndexedOptions(lookupTables.ItemNames, "Item");
        var moveOptions = SwShMoveAvailability.CreateMoveOptions(
            lookupTables.MoveNames,
            lookupTables.UsableMoveIds,
            (value, label) => new SwShDynamaxAdventureEditableFieldOption(value, label));

        return BaseEditableFields
            .Where(field => SafeEditableFieldNames.Contains(field.Field))
            .Select(field => field.Field switch
            {
                SpeciesField => field with { Options = safeNormalSpeciesOptions },
                BallItemIdField => field with { Options = itemOptions },
                Move0Field or Move1Field or Move2Field or Move3Field => field with { Options = moveOptions },
                _ => field,
            })
            .ToArray();
    }

    private static IReadOnlyList<SwShDynamaxAdventureEditableFieldOption> CreateSafeNormalSpeciesOptions(
        IReadOnlyList<SwShDynamaxAdventureEntry> encounters,
        DynamaxAdventureLookupTables lookupTables)
    {
        if (lookupTables.SpeciesNames.Count == 0)
        {
            return [];
        }

        var usedNormalSpeciesForms = encounters
            .Where(encounter => SwShDynamaxAdventureSafetyRules.IsNormalEntryIndex(encounter.EntryIndex))
            .Select(encounter => (encounter.SpeciesId, encounter.Form))
            .ToHashSet();
        var bossSpeciesForms = encounters
            .Where(encounter => SwShDynamaxAdventureSafetyRules.IsBossEntryIndex(encounter.EntryIndex))
            .Select(encounter => (encounter.SpeciesId, encounter.Form))
            .ToHashSet();

        return lookupTables.SpeciesNames
            .Select((name, species) => (name, species))
            .Where(entry => SwShDynamaxAdventureSafetyRules.CanUseAsNormalReplacement(
                entry.species,
                form: 0,
                usedNormalSpeciesForms,
                bossSpeciesForms,
                lookupTables.PersonalRecords))
            .Select(entry => new SwShDynamaxAdventureEditableFieldOption(
                entry.species,
                FormatSpeciesOptionLabel(entry.species, entry.name)))
            .ToArray();
    }

    private static string FormatSpeciesOptionLabel(int speciesId, string name)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{speciesId:000} {(string.IsNullOrWhiteSpace(name) ? $"Species {speciesId.ToString(CultureInfo.InvariantCulture)}" : name)}");
    }

    private static bool IsSafeEditableEncounter(
        SwShDynamaxAdventureRecord entry,
        IReadOnlySet<(int Species, int Form)> bossSpeciesForms,
        IReadOnlyList<SwShPersonalRecord> personalRecords)
    {
        if (!SwShDynamaxAdventureSafetyRules.IsNormalEntryIndex(entry.EntryIndex)
            || entry.Species <= 0
            || entry.Species > SwShDynamaxAdventureSafetyRules.MaximumVerifiedNormalReplacementSpecies
            || bossSpeciesForms.Contains((entry.Species, entry.Form))
            || SwShDynamaxAdventureSafetyRules.IsSpecialNormalRouteSpecies(entry.Species)
            || SwShDynamaxAdventureSafetyRules.IsBattleFusionSpecies(entry.Species))
        {
            return false;
        }

        var personal = SwShDynamaxAdventureSafetyRules.ResolvePersonalRecord(
            entry.Species,
            entry.Form,
            personalRecords);
        if (personalRecords.Count > 0 && personal?.IsPresentInGame != true)
        {
            return false;
        }

        return personal?.CanNotDynamax != true;
    }

    private static IReadOnlyList<SwShDynamaxAdventureEditableFieldOption> CreateIndexedOptions(
        IReadOnlyList<string> names,
        string fallbackPrefix)
    {
        return names
            .Select((name, index) => new SwShDynamaxAdventureEditableFieldOption(
                index,
                string.IsNullOrWhiteSpace(name)
                    ? $"{index.ToString("000", CultureInfo.InvariantCulture)} {fallbackPrefix} {index}"
                    : $"{index.ToString("000", CultureInfo.InvariantCulture)} {name}"))
            .ToArray();
    }

    private static SwShDynamaxAdventureEntry ToEncounterEntry(
        SwShDynamaxAdventureRecord entry,
        IReadOnlyList<SwShDynamaxAdventureRecord> entries,
        DynamaxAdventureLookupTables lookupTables,
        SwShDynamaxAdventureProvenance provenance,
        bool isEditable,
        int? activeBossTargetSpeciesId,
        SwShDynamaxAdventureRecord? vanillaEntry = null)
    {
        var species = GetIndexedName(entry.Species, lookupTables.SpeciesNames, "Species");
        var versionLabel = GetOptionLabel(VersionOptions, entry.Version, "Version");
        var moves = entry.Moves
            .Select((moveId, index) => new SwShDynamaxAdventureMoveRecord(
                index + 1,
                moveId,
                GetIndexedName(moveId, lookupTables.MoveNames, "Move")))
            .ToArray();
        var guaranteedPerfectIvs = SwShDynamaxAdventureArchive.GetGuaranteedPerfectIvCount(entry.Ivs);

        return new SwShDynamaxAdventureEntry(
            entry.EntryIndex,
            isEditable,
            CreateLabel(entry.EntryIndex, entry.AdventureIndex, species, entry.Species, entry.Form, versionLabel),
            entry.AdventureIndex,
            entry.Species,
            species,
            entry.Form,
            entry.Level,
            entry.BallItemId,
            GetIndexedName(entry.BallItemId, lookupTables.ItemNames, "Item"),
            entry.Ability,
            GetAbilityOptionLabel(lookupTables, entry.Species, entry.Form, entry.Ability),
            entry.GigantamaxState,
            GetOptionLabel(GigantamaxOptions, entry.GigantamaxState, "Gigantamax"),
            entry.Version,
            versionLabel,
            entry.ShinyRoll,
            GetOptionLabel(ShinyRollOptions, entry.ShinyRoll, "Shiny roll"),
            entry.IsSingleCapture,
            FormatHash(entry.SingleCaptureFlagBlock),
            entry.IsStoryProgressGated,
            FormatHash(entry.UiMessageId),
            entry.OtGender,
            GetOptionLabel(OtGenderOptions, entry.OtGender, "OT gender"),
            moves,
            new SwShDynamaxAdventureIvsRecord(
                entry.Ivs.Hp,
                entry.Ivs.Attack,
                entry.Ivs.Defense,
                entry.Ivs.Speed,
                entry.Ivs.SpecialAttack,
                entry.Ivs.SpecialDefense),
            guaranteedPerfectIvs,
            FormatIvSummary(entry.Ivs, guaranteedPerfectIvs),
            provenance)
        {
            AbilityOptions = CreateAbilityOptions(lookupTables, entry.Species, entry.Form),
            MoveOptions = CreateMoveOptions(lookupTables, entry, vanillaEntry),
            BossTargetOptions = CreateBossTargetOptions(entry, entries, lookupTables),
            BossTargetSpeciesId = activeBossTargetSpeciesId ?? entry.Species,
            BossTargetSpecies = GetIndexedName(activeBossTargetSpeciesId ?? entry.Species, lookupTables.SpeciesNames, "Species"),
            VanillaPokemon = vanillaEntry is null ? null : ToPokemonSnapshot(vanillaEntry, lookupTables),
        };
    }

    private static IReadOnlyDictionary<int, int> LoadActiveBossTargetRemaps(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var mainPath = ResolveOutputPath(project.Paths, "exefs/main");
        if (mainPath is null || !File.Exists(mainPath))
        {
            return new Dictionary<int, int>();
        }

        try
        {
            if (!SwShDynamaxAdventuresBossTargetPatcher.TryReadConditionalTargetSpeciesRemap(
                File.ReadAllBytes(mainPath),
                out var remap))
            {
                return new Dictionary<int, int>();
            }

            return new Dictionary<int, int>
            {
                [remap.FromSpecies] = remap.ToSpecies,
            };
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Generated exefs/main contains an unrecognized Dynamax Adventures boss target remap: {exception.Message}",
                file: "exefs/main",
                expected: "Owned Dynamax Adventures boss target patch or vanilla call sites"));
            return new Dictionary<int, int>();
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Generated exefs/main could not be read for Dynamax Adventures boss target detection: {exception.Message}",
                file: "exefs/main",
                expected: "Readable generated ExeFS main"));
            return new Dictionary<int, int>();
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Generated exefs/main could not be read for Dynamax Adventures boss target detection: {exception.Message}",
                file: "exefs/main",
                expected: "Readable generated ExeFS main"));
            return new Dictionary<int, int>();
        }
    }

    private static IReadOnlyList<SwShDynamaxAdventureBossTargetOption> CreateBossTargetOptions(
        SwShDynamaxAdventureRecord entry,
        IReadOnlyList<SwShDynamaxAdventureRecord> entries,
        DynamaxAdventureLookupTables lookupTables)
    {
        if (!SwShDynamaxAdventureSafetyRules.IsBossEntryIndex(entry.EntryIndex))
        {
            return [];
        }

        var bossEntries = entries
            .Where(candidate => SwShDynamaxAdventureSafetyRules.IsBossEntryIndex(candidate.EntryIndex))
            .ToArray();
        var bossSpeciesCounts = bossEntries
            .GroupBy(candidate => candidate.Species)
            .ToDictionary(group => group.Key, group => group.Count());
        if (bossSpeciesCounts.GetValueOrDefault(entry.Species) != 1)
        {
            return [];
        }

        return bossEntries
            .Where(candidate => candidate.EntryIndex != entry.EntryIndex
                && candidate.Version == entry.Version
                && candidate.IsStoryProgressGated == entry.IsStoryProgressGated
                && bossSpeciesCounts.GetValueOrDefault(candidate.Species) == 1)
            .OrderBy(candidate => candidate.EntryIndex)
            .Select(candidate =>
            {
                var species = GetIndexedName(candidate.Species, lookupTables.SpeciesNames, "Species");
                var versionLabel = GetOptionLabel(VersionOptions, candidate.Version, "Version");
                return new SwShDynamaxAdventureBossTargetOption(
                    candidate.EntryIndex,
                    candidate.AdventureIndex,
                    candidate.Species,
                    species,
                    candidate.Form,
                    candidate.Version,
                    versionLabel,
                    candidate.IsStoryProgressGated,
                    CreateLabel(
                        candidate.EntryIndex,
                        candidate.AdventureIndex,
                        species,
                        candidate.Species,
                        candidate.Form,
                        versionLabel));
            })
            .ToArray();
    }

    private static SwShDynamaxAdventurePokemonSnapshot ToPokemonSnapshot(
        SwShDynamaxAdventureRecord entry,
        DynamaxAdventureLookupTables lookupTables)
    {
        var species = GetIndexedName(entry.Species, lookupTables.SpeciesNames, "Species");
        var moves = entry.Moves
            .Select((moveId, index) => new SwShDynamaxAdventureMoveRecord(
                index + 1,
                moveId,
                GetIndexedName(moveId, lookupTables.MoveNames, "Move")))
            .ToArray();
        var guaranteedPerfectIvs = SwShDynamaxAdventureArchive.GetGuaranteedPerfectIvCount(entry.Ivs);

        return new SwShDynamaxAdventurePokemonSnapshot(
            entry.Species,
            species,
            entry.Form,
            entry.Level,
            entry.Ability,
            GetAbilityOptionLabel(lookupTables, entry.Species, entry.Form, entry.Ability),
            entry.GigantamaxState,
            GetOptionLabel(GigantamaxOptions, entry.GigantamaxState, "Gigantamax"),
            moves,
            new SwShDynamaxAdventureIvsRecord(
                entry.Ivs.Hp,
                entry.Ivs.Attack,
                entry.Ivs.Defense,
                entry.Ivs.Speed,
                entry.Ivs.SpecialAttack,
                entry.Ivs.SpecialDefense),
            guaranteedPerfectIvs,
            FormatIvSummary(entry.Ivs, guaranteedPerfectIvs));
    }

    private static string CreateLabel(int entryIndex, int adventureIndex, string species, int speciesId, int form, string versionLabel)
    {
        var speciesLabel = SwShSpeciesFormLabels.FormatSpeciesFormLabel(species, speciesId, form);
        var versionSuffix = string.Equals(versionLabel, "Both", StringComparison.Ordinal)
            ? string.Empty
            : $" [{versionLabel}]";
        return $"{entryIndex.ToString("000", CultureInfo.InvariantCulture)} / {adventureIndex.ToString("000", CultureInfo.InvariantCulture)} - {speciesLabel}{versionSuffix}";
    }

    private static string FormatIvSummary(SwShDynamaxAdventureIvs ivs, int guaranteedPerfectIvs)
    {
        return string.Join(
            ", ",
            [
                guaranteedPerfectIvs > 0
                    ? $"{guaranteedPerfectIvs.ToString(CultureInfo.InvariantCulture)} guaranteed perfect"
                    : "random HP",
                $"Atk {FormatIvValue(ivs.Attack)}",
                $"Def {FormatIvValue(ivs.Defense)}",
                $"SpA {FormatIvValue(ivs.SpecialAttack)}",
                $"SpD {FormatIvValue(ivs.SpecialDefense)}",
                $"Spe {FormatIvValue(ivs.Speed)}",
            ]);
    }

    private static string FormatIvValue(int value)
    {
        return value == SwShDynamaxAdventureArchive.RandomIvValue
            ? "random"
            : value.ToString(CultureInfo.InvariantCulture);
    }

    private static string GetIndexedName(int id, IReadOnlyList<string> names, string fallbackPrefix)
    {
        return (uint)id < (uint)names.Count && !string.IsNullOrWhiteSpace(names[id])
            ? names[id]
            : $"{fallbackPrefix} {id.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string FormatHash(ulong value)
    {
        return $"0x{value:X16}";
    }

    private static DynamaxAdventureLookupTables LoadLookupTables(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var messageRoot = ResolveLanguageMessageRoot(project, diagnostics);
        var speciesNames = LoadMessageTable(project, messageRoot, "monsname.dat", diagnostics);
        var itemNames = LoadMessageTable(project, messageRoot, "itemname.dat", diagnostics);
        var moveNames = LoadMessageTable(project, messageRoot, "wazaname.dat", diagnostics);
        var itemDisplayNames = SwShItemsWorkflowService.CreateItemDisplayNames(project, itemNames, moveNames);
        var presentSpeciesIds = SwShSpeciesAvailability.LoadPresentSpeciesIds(project);
        var usableMoveIds = SwShMoveAvailability.LoadUsableMoveIds(project);
        var personalRecords = LoadPersonalRecords(project);
        var learnsetRecords = LoadLearnsetRecords(project);
        var abilityResolver = SwShPokemonAbilityOptionResolver.Load(project);

        return new DynamaxAdventureLookupTables(
            speciesNames,
            presentSpeciesIds,
            itemDisplayNames,
            moveNames,
            usableMoveIds,
            personalRecords,
            learnsetRecords,
            abilityResolver,
            SourceFileCount:
                (speciesNames.Length > 0 ? 1 : 0)
                + (itemNames.Length > 0 ? 1 : 0)
                + (moveNames.Length > 0 ? 1 : 0)
                + (presentSpeciesIds.Count > 0 ? 1 : 0)
                + (usableMoveIds.Count > 0 ? 1 : 0)
                + (learnsetRecords.Count > 0 ? 1 : 0));
    }

    private static IReadOnlyList<SwShPersonalRecord> LoadPersonalRecords(OpenedProject project)
    {
        var source = ResolveWorkflowFile(project, SwShPersonalTable.PersonalDataRelativePath);
        if (source is null)
        {
            return [];
        }

        try
        {
            return SwShPersonalTable.Parse(File.ReadAllBytes(source.AbsolutePath)).Records;
        }
        catch (InvalidDataException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IReadOnlyList<SwShPokemonLearnsetRecord> LoadLearnsetRecords(OpenedProject project)
    {
        var source = ResolveWorkflowFile(project, SwShPokemonLearnsetTable.LearnsetDataRelativePath);
        if (source is null)
        {
            return [];
        }

        try
        {
            return SwShPokemonLearnsetTable.Parse(File.ReadAllBytes(source.AbsolutePath)).Records;
        }
        catch (InvalidDataException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IReadOnlyList<SwShDynamaxAdventureEditableFieldOption> CreateMoveOptions(
        DynamaxAdventureLookupTables lookupTables,
        SwShDynamaxAdventureRecord entry,
        SwShDynamaxAdventureRecord? vanillaEntry)
    {
        if (lookupTables.MoveNames.Count == 0
            || lookupTables.UsableMoveIds.Count == 0
            || lookupTables.PersonalRecords.Count == 0
            || lookupTables.LearnsetRecords.Count == 0)
        {
            return [];
        }

        var personal = SwShDynamaxAdventureSafetyRules.ResolvePersonalRecord(
            entry.Species,
            entry.Form,
            lookupTables.PersonalRecords);
        if (personal is null)
        {
            return [];
        }

        var learnset = personal.PersonalId < lookupTables.LearnsetRecords.Count
            ? lookupTables.LearnsetRecords[personal.PersonalId]
            : null;
        var moveIds = new SortedSet<int>();
        foreach (var moveId in lookupTables.UsableMoveIds)
        {
            if (SwShDynamaxAdventureSafetyRules.CanLearnMove(personal, learnset, moveId, entry.Level))
            {
                moveIds.Add(moveId);
            }
        }

        foreach (var moveId in entry.Moves)
        {
            moveIds.Add(moveId);
        }

        if (vanillaEntry is not null
            && vanillaEntry.Species == entry.Species
            && vanillaEntry.Form == entry.Form
            && entry.Level >= vanillaEntry.Level)
        {
            foreach (var moveId in vanillaEntry.Moves)
            {
                moveIds.Add(moveId);
            }
        }

        return moveIds
            .Select(moveId => new SwShDynamaxAdventureEditableFieldOption(
                moveId,
                FormatMoveOptionLabel(moveId, lookupTables.MoveNames)))
            .ToArray();
    }

    private static IReadOnlyList<SwShDynamaxAdventureEditableFieldOption> CreateAbilityOptions(
        DynamaxAdventureLookupTables lookupTables,
        int speciesId,
        int form)
    {
        return lookupTables.AbilityResolver
            .CreateOptions(speciesId, form, SwShAbilityOptionMode.ZeroBasedSlots)
            .Select(option => new SwShDynamaxAdventureEditableFieldOption(option.Value, option.Label))
            .ToArray();
    }

    private static string GetAbilityOptionLabel(
        DynamaxAdventureLookupTables lookupTables,
        int speciesId,
        int form,
        int value)
    {
        return CreateAbilityOptions(lookupTables, speciesId, form)
            .FirstOrDefault(option => option.Value == value)?.Label
            ?? GetOptionLabel(AbilityOptions, value, "Ability roll");
    }

    private static string FormatMoveOptionLabel(int moveId, IReadOnlyList<string> moveNames)
    {
        var label = (uint)moveId < (uint)moveNames.Count && !string.IsNullOrWhiteSpace(moveNames[moveId])
            ? moveNames[moveId]
            : moveId == 0 ? "None" : $"Move {moveId.ToString(CultureInfo.InvariantCulture)}";
        return string.Create(CultureInfo.InvariantCulture, $"{moveId:000} {label}");
    }

    private static VanillaDynamaxAdventureTable? LoadVanillaArchive(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var graphEntry = project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, DynamaxAdventureDataPath, StringComparison.OrdinalIgnoreCase));
        if (graphEntry?.BaseFile is null
            || !graphEntry.RelativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var sourcePath = CombineGraphPath(
            project.Paths.BaseRomFsPath,
            graphEntry.RelativePath["romfs/".Length..]);
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            return null;
        }

        try
        {
            var data = File.ReadAllBytes(sourcePath);
            return new VanillaDynamaxAdventureTable(
                SwShDynamaxAdventureArchive.Parse(data),
                data.Length);
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Vanilla Dynamax Adventures data could not be decoded for restore values: {exception.Message}",
                file: DynamaxAdventureDataPath,
                expected: "Base Sword/Shield Dynamax Adventures table"));
            return null;
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Vanilla Dynamax Adventures data could not be read for restore values: {exception.Message}",
                file: DynamaxAdventureDataPath,
                expected: "Readable base Sword/Shield Dynamax Adventures table"));
            return null;
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Vanilla Dynamax Adventures data could not be read for restore values: {exception.Message}",
                file: DynamaxAdventureDataPath,
                expected: "Readable base Sword/Shield Dynamax Adventures table"));
            return null;
        }
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
                "Dynamax Adventures lookup text is not available; numeric fallback labels will be shown.",
                expected: $"{MessageRootPath}/{PreferredLanguage}/common/monsname.dat"));
            return null;
        }

        var language = languages.Contains(PreferredLanguage, StringComparer.OrdinalIgnoreCase)
            ? PreferredLanguage
            : languages[0];

        if (!string.Equals(language, PreferredLanguage, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"English Dynamax Adventures lookup text was not found; using '{language}' lookup tables instead.",
                expected: $"{MessageRootPath}/{PreferredLanguage}/common/monsname.dat"));
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
                $"Dynamax Adventures lookup table '{relativePath}' could not be decoded: {exception.Message}",
                file: relativePath,
                expected: "Sword/Shield message table"));
            return [];
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Dynamax Adventures lookup table '{relativePath}' could not be read: {exception.Message}",
                file: relativePath,
                expected: "Readable message table"));
            return [];
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Dynamax Adventures lookup table '{relativePath}' could not be read: {exception.Message}",
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

    private static SwShDynamaxAdventureProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShDynamaxAdventureProvenance(entry.RelativePath, sourceLayer, entry.State);
    }

    private static SwShDynamaxAdventureEditableField CreateField(
        string field,
        string label,
        string valueKind,
        int? minimumValue,
        int? maximumValue,
        IReadOnlyList<SwShDynamaxAdventureEditableFieldOption>? options = null)
    {
        return new SwShDynamaxAdventureEditableField(
            field,
            label,
            valueKind,
            minimumValue,
            maximumValue,
            options ?? Array.Empty<SwShDynamaxAdventureEditableFieldOption>());
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.DynamaxAdventures,
            "Dynamax Adventures",
            "Adventure Pokemon, moves, encounter rules, guaranteed perfect IVs, and source provenance.",
            availability,
            diagnostics);
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? file = null,
        string? field = null,
        string? expected = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Field: field,
            Domain: DynamaxAdventuresEditDomain,
            Expected: expected);
    }

    private sealed record DynamaxAdventureLookupTables(
        IReadOnlyList<string> SpeciesNames,
        IReadOnlySet<int> PresentSpeciesIds,
        IReadOnlyList<string> ItemNames,
        IReadOnlyList<string> MoveNames,
        IReadOnlySet<int> UsableMoveIds,
        IReadOnlyList<SwShPersonalRecord> PersonalRecords,
        IReadOnlyList<SwShPokemonLearnsetRecord> LearnsetRecords,
        SwShPokemonAbilityOptionResolver AbilityResolver,
        int SourceFileCount);

    private sealed record VanillaDynamaxAdventureTable(
        SwShDynamaxAdventureArchive Archive,
        int Length);

    internal sealed record WorkflowFileSource(
        ProjectFileGraphEntry GraphEntry,
        string AbsolutePath);
}
