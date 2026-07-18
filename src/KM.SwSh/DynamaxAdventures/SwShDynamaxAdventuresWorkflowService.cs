// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Items;
using KM.SwSh.ExeFs;
using KM.SwSh.Moves;
using KM.SwSh.Pokemon;
using KM.SwSh.Workflows;
using System.Globalization;
using System.Security.Cryptography;

namespace KM.SwSh.DynamaxAdventures;

public sealed class SwShDynamaxAdventuresWorkflowService
{
    public const string SpeciesField = "species";
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
    private const string KnownUnsafeRebuiltVanillaTableSha256 = "5DD898C9DB0BE5CB28119779D886496C7467108AA3C4FC810CE5EA3EC5358E73";
    internal const int CanonicalBaseTableLength = 23_296;
    internal const int CanonicalBaseTableRowCount = 273;
    public const int MaximumSwordShieldMoveId = 826;
    private const string CanonicalBaseTableSha256 = "18D7D6546EB2C6A5CA9531EB13A0A3BFFEAAB186EE2F6510EA4B6275B5EA4328";

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

    // Fields here are covered by exact in-place table edits plus the ExeFS summary and command mirror reconciler.
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
        CreateField(SpeciesField, "Species", "integer", 1, SwShDynamaxAdventureSafetyRules.MaximumVerifiedNormalReplacementSpecies),
        CreateField(FormField, "Form", "integer", 0, SwShDynamaxAdventureArchive.MaximumByteValue, FormOptions),
        CreateField(LevelField, "Level", "integer", 1, 100),
        CreateField(BallItemIdField, "Ball item", "integer", 0, SwShDynamaxAdventureArchive.MaximumIdValue),
        CreateField(AbilityField, "Ability roll", "integer", 0, SwShDynamaxAdventureArchive.MaximumAbilityRoll, AbilityOptions),
        CreateField(GigantamaxStateField, "Gigantamax state", "integer", 0, SwShDynamaxAdventureArchive.MaximumGigantamaxState, GigantamaxOptions),
        CreateField(VersionField, "Game version", "integer", 0, SwShDynamaxAdventureArchive.MaximumVersion, VersionOptions),
        CreateField(ShinyRollField, "Shiny roll", "integer", 0, SwShDynamaxAdventureArchive.MaximumShinyRoll, ShinyRollOptions),
        CreateField(Move0Field, "Move 1", "integer", 0, MaximumSwordShieldMoveId),
        CreateField(Move1Field, "Move 2", "integer", 0, MaximumSwordShieldMoveId),
        CreateField(Move2Field, "Move 3", "integer", 0, MaximumSwordShieldMoveId),
        CreateField(Move3Field, "Move 4", "integer", 0, MaximumSwordShieldMoveId),
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

    public SwShDynamaxAdventuresWorkflowService()
    {
    }

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

        if (!IsSupportedGame(project.Paths.SelectedGame))
        {
            return CreateSummary(
                SwShWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Dynamax Adventures requires Pokemon Sword or Pokemon Shield to be selected before it can load.",
                    expected: "Selected Pokemon Sword or Pokemon Shield project"));
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
                DiagnosticSeverity.Error,
                "Dynamax Adventures data is not available for this project.",
                expected: DynamaxAdventureDataPath));
            return CreateWorkflow(summary, [], sourceFileCount: 0, CreateEmptyLookupTables(), diagnostics);
        }

        var lookupTables = LoadLookupTables(project, diagnostics);
        var parsedSourcePaths = new HashSet<string>(
            lookupTables.ParsedSourcePaths,
            StringComparer.OrdinalIgnoreCase);

        try
        {
            var sourceBytes = ReadBoundedDynamaxAdventureTable(source.AbsolutePath);
            var archive = SwShDynamaxAdventureArchive.Parse(sourceBytes);
            parsedSourcePaths.Add(Path.GetFullPath(source.AbsolutePath));
            var vanillaArchive = LoadVanillaArchive(project, parsedSourcePaths, diagnostics);
            var hasVerifiedLayeredBase = vanillaArchive is not null
                && source.GraphEntry.State == ProjectFileGraphEntryState.LayeredOverride;
            var hasLayeredLayoutMismatch = hasVerifiedLayeredBase
                && !IsDynamaxAdventureTableLayoutCompatible(
                    vanillaArchive!.Archive,
                    vanillaArchive.Data,
                    archive,
                    sourceBytes);
            var hasSupportedEffectiveContract = TryValidateRecordContractDomain(
                archive.Entries,
                out var invalidEntry,
                out var invalidField,
                out var expected);
            if (!hasSupportedEffectiveContract)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Dynamax Adventures row {invalidEntry.ToString(CultureInfo.InvariantCulture)} contains {invalidField} outside the supported API domain.",
                    file: source.GraphEntry.RelativePath,
                    field: invalidField,
                    expected: expected));
                if (!hasVerifiedLayeredBase)
                {
                    return CreateWorkflow(summary, [], parsedSourcePaths.Count, lookupTables, diagnostics);
                }
            }

            var hasPersonalData = lookupTables.PersonalRecords.Count > 0;
            var hasResolvableEffectiveForms = !hasPersonalData
                || TryValidatePersonalRecordResolution(
                    archive.Entries,
                    lookupTables.PersonalRecords,
                    out invalidEntry,
                    out expected);
            if (hasPersonalData && !hasResolvableEffectiveForms)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Dynamax Adventures row {invalidEntry.ToString(CultureInfo.InvariantCulture)} contains a form that does not exist for its species in Sword/Shield personal data.",
                    file: source.GraphEntry.RelativePath,
                    field: FormField,
                    expected: expected));
                if (!hasVerifiedLayeredBase)
                {
                    return CreateWorkflow(summary, [], parsedSourcePaths.Count, lookupTables, diagnostics);
                }
            }

            if (hasLayeredLayoutMismatch)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Dynamax Adventures source table byte layout differs from the vanilla table. Restore the Adventure table from a clean dump before making new Pokemon edits.",
                    file: source.GraphEntry.RelativePath,
                    expected: "Vanilla byte layout or prior KM in-place Dynamax Adventures edits"));
            }

            if (vanillaArchive is not null
                && !TryValidateRecordContractDomain(
                    vanillaArchive.Archive.Entries,
                    out invalidEntry,
                    out invalidField,
                    out expected))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Verified base Dynamax Adventures row {invalidEntry.ToString(CultureInfo.InvariantCulture)} contains {invalidField} outside the supported API domain.",
                    file: DynamaxAdventureDataPath,
                    field: invalidField,
                    expected: expected));
                return CreateWorkflow(summary, [], parsedSourcePaths.Count, lookupTables, diagnostics);
            }

            if (vanillaArchive is not null
                && hasPersonalData
                && !TryValidatePersonalRecordResolution(
                    vanillaArchive.Archive.Entries,
                    lookupTables.PersonalRecords,
                    out invalidEntry,
                    out expected))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Verified base Dynamax Adventures row {invalidEntry.ToString(CultureInfo.InvariantCulture)} contains a form that does not exist for its species in Sword/Shield personal data.",
                    file: DynamaxAdventureDataPath,
                    field: FormField,
                    expected: expected));
                return CreateWorkflow(summary, [], parsedSourcePaths.Count, lookupTables, diagnostics);
            }

            var effectiveBossSpeciesForms = archive.Entries
                .Where(entry => SwShDynamaxAdventureSafetyRules.IsBossEntryIndex(entry.EntryIndex))
                .Select(entry => (entry.Species, entry.Form))
                .ToHashSet();
            var vanillaEntriesByIndex = vanillaArchive?.Archive.Entries
                .ToDictionary(entry => entry.EntryIndex);
            var hasHiddenRowChanges = vanillaEntriesByIndex is not null
                && archive.Entries.Any(entry =>
                    !IsSafeEditableEncounter(entry, effectiveBossSpeciesForms, lookupTables.PersonalRecords)
                    && (!vanillaEntriesByIndex.TryGetValue(entry.EntryIndex, out var vanillaEntry)
                        || !RecordsEqual(entry, vanillaEntry)));
            var usesVanillaRecoveryProjection = hasLayeredLayoutMismatch
                || !hasSupportedEffectiveContract
                || !hasResolvableEffectiveForms
                || hasHiddenRowChanges;
            var projectionArchive = usesVanillaRecoveryProjection
                ? vanillaArchive!.Archive
                : archive;
            var provenance = CreateProvenance(source.GraphEntry);
            var bossSpeciesForms = projectionArchive.Entries
                .Where(entry => SwShDynamaxAdventureSafetyRules.IsBossEntryIndex(entry.EntryIndex))
                .Select(entry => (entry.Species, entry.Form))
                .ToHashSet();
            var encounters = projectionArchive.Entries
                .Select(entry => ToEncounterEntry(
                    entry,
                    projectionArchive,
                    lookupTables,
                    provenance,
                    !usesVanillaRecoveryProjection
                        && IsSafeEditableEncounter(entry, bossSpeciesForms, lookupTables.PersonalRecords),
                    vanillaArchive?.Archive.Entries.FirstOrDefault(vanillaEntry => vanillaEntry.EntryIndex == entry.EntryIndex)))
                .ToArray();
            if (hasHiddenRowChanges)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Dynamax Adventures contains changes on a hidden normal or boss row. Unsupported row classes must remain base-identical before editing can continue.",
                    file: source.GraphEntry.RelativePath,
                    expected: "Base-identical hidden normal and boss rows"));
            }
            if (encounters.Any(encounter => encounter.IsEditable
                && (encounter.VanillaPokemon is null
                    || encounter.AbilityOptions.Count == 0
                    || encounter.GigantamaxOptions.Count == 0
                    || encounter.MoveOptions.Count == 0)))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Dynamax Adventures could not map complete base, ability, Gigantamax, and move options for every editable row.",
                    file: source.GraphEntry.RelativePath,
                    expected: "Verified vanilla row plus nonempty safe option sets for every editable Pokemon"));
            }
            var mainState = AnalyzeMainState(
                project,
                projectionArchive,
                vanillaArchive?.Archive,
                usesVanillaRecoveryProjection
                    ? CanRecognizeSourceMainProjection(archive, vanillaArchive!.Archive)
                        ? archive
                        : null
                    : archive,
                parsedSourcePaths,
                diagnostics);
            var sourceFileCount = parsedSourcePaths.Count;
            var loadedWorkflow = CreateWorkflow(
                summary,
                encounters,
                sourceFileCount,
                lookupTables,
                diagnostics);
            var canRestoreVanillaTable = vanillaArchive is not null
                && source.GraphEntry.State == ProjectFileGraphEntryState.LayeredOverride
                && HasRecoverableTableErrors(loadedWorkflow.Diagnostics);

            return loadedWorkflow with
            {
                InstallStatus = mainState.InstallStatus,
                InstallMessage = mainState.Analysis.HasLegacyBossTargetPatch
                    ? "An exact legacy Dynamax Adventures final-boss target remap is installed. Ordinary Adventure edits are blocked until Stage Repair or a full vanilla table restore removes this unsupported executable code."
                    : mainState.Analysis.Message,
                BuildId = mainState.Analysis.BuildId,
                DetectedGame = mainState.Analysis.DetectedGame,
                HasLegacyBossTargetPatch = mainState.Analysis.HasLegacyBossTargetPatch,
                CanRestoreVanillaTable = canRestoreVanillaTable,
                UsesVanillaRecoveryProjection = usesVanillaRecoveryProjection,
                RestoreVanillaTableMessage = canRestoreVanillaTable
                    ? "Restore is available. Applying it removes all layered Adventure-table changes and restores the verified vanilla table."
                    : "Vanilla Dynamax Adventures table restore is not available for this source state.",
                ReservedRegions = mainState.ReservedRegions,
            };
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventures data source is not a supported Sword/Shield table: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Sword/Shield Dynamax Adventures table"));
            return CreateWorkflow(summary, [], parsedSourcePaths.Count, lookupTables, diagnostics);
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventures data source could not be read: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield Dynamax Adventures table"));
            return CreateWorkflow(summary, [], parsedSourcePaths.Count, lookupTables, diagnostics);
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventures data source could not be read: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield Dynamax Adventures table"));
            return CreateWorkflow(summary, [], parsedSourcePaths.Count, lookupTables, diagnostics);
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

    internal static IReadOnlyList<SwShDynamaxAdventureEditableFieldOption> CreateGigantamaxOptions(
        int speciesId,
        int currentState)
    {
        var options = new List<SwShDynamaxAdventureEditableFieldOption>();
        if (currentState == 0)
        {
            options.Add(new SwShDynamaxAdventureEditableFieldOption(0, "Unknown"));
        }

        options.Add(new SwShDynamaxAdventureEditableFieldOption(1, "Normal"));
        if (IsGigantamaxCapableSpecies(speciesId) || currentState == SwShDynamaxAdventureArchive.MaximumGigantamaxState)
        {
            options.Add(new SwShDynamaxAdventureEditableFieldOption(2, "Gigantamax"));
        }

        return options;
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

        return PathContainment.IsWithinRoot(pathFromOutputRoot)
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

    internal static bool TryValidateRecordContractDomain(
        IReadOnlyList<SwShDynamaxAdventureRecord> entries,
        out int entryIndex,
        out string field,
        out string expected)
    {
        foreach (var entry in entries)
        {
            if (entry.Species is < 1 or > SwShDynamaxAdventureSafetyRules.MaximumVerifiedNormalReplacementSpecies)
            {
                return Invalid(entry, SpeciesField, "1-898", out entryIndex, out field, out expected);
            }
            if (entry.Level is < 1 or > 100)
            {
                return Invalid(entry, LevelField, "1-100", out entryIndex, out field, out expected);
            }
            if (entry.BallItemId is < 0 or > ushort.MaxValue)
            {
                return Invalid(entry, BallItemIdField, "0-65535", out entryIndex, out field, out expected);
            }
            if (entry.Ability is < 0 or > SwShDynamaxAdventureArchive.MaximumAbilityRoll)
            {
                return Invalid(entry, AbilityField, "0-2", out entryIndex, out field, out expected);
            }
            if (entry.GigantamaxState is < 0 or > SwShDynamaxAdventureArchive.MaximumGigantamaxState)
            {
                return Invalid(entry, GigantamaxStateField, "0-2", out entryIndex, out field, out expected);
            }
            if (entry.Version is < 0 or > SwShDynamaxAdventureArchive.MaximumVersion)
            {
                return Invalid(entry, VersionField, "0-2", out entryIndex, out field, out expected);
            }
            if (entry.ShinyRoll is < 0 or > SwShDynamaxAdventureArchive.MaximumShinyRoll)
            {
                return Invalid(entry, ShinyRollField, "0-2", out entryIndex, out field, out expected);
            }
            if (entry.OtGender is < 0 or > 1)
            {
                return Invalid(entry, OtGenderField, "0-1", out entryIndex, out field, out expected);
            }
            if (entry.AdventureIndex < 0)
            {
                return Invalid(entry, "adventureIndex", "Non-negative integer", out entryIndex, out field, out expected);
            }
            if (entry.Ivs.Hp is < -SwShDynamaxAdventureArchive.MaximumGuaranteedPerfectIvs or > SwShDynamaxAdventureArchive.MaximumFixedIvValue)
            {
                return Invalid(entry, GuaranteedPerfectIvsField, "HP IV encoding -6 through 31", out entryIndex, out field, out expected);
            }
            if (!IsContractIv(entry.Ivs.Attack)
                || !IsContractIv(entry.Ivs.Defense)
                || !IsContractIv(entry.Ivs.Speed)
                || !IsContractIv(entry.Ivs.SpecialAttack)
                || !IsContractIv(entry.Ivs.SpecialDefense))
            {
                return Invalid(entry, "ivs", "Each IV override -1 through 31", out entryIndex, out field, out expected);
            }
            if (entry.Moves.Count != 4
                || entry.Moves.Any(move => move is < 0 or > MaximumSwordShieldMoveId))
            {
                return Invalid(entry, Move0Field, "Four move IDs from 0 through 826", out entryIndex, out field, out expected);
            }
        }

        entryIndex = 0;
        field = string.Empty;
        expected = string.Empty;
        return true;
    }

    internal static bool TryValidatePersonalRecordResolution(
        IReadOnlyList<SwShDynamaxAdventureRecord> entries,
        IReadOnlyList<SwShPersonalRecord> personalRecords,
        out int entryIndex,
        out string expected)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(personalRecords);

        foreach (var entry in entries)
        {
            if (SwShDynamaxAdventureSafetyRules.ResolvePersonalRecord(
                entry.Species,
                entry.Form,
                personalRecords) is not null)
            {
                continue;
            }

            entryIndex = entry.EntryIndex;
            expected = GetExpectedPersonalFormRange(entry.Species, personalRecords);
            return false;
        }

        entryIndex = 0;
        expected = string.Empty;
        return true;
    }

    private static string GetExpectedPersonalFormRange(
        int species,
        IReadOnlyList<SwShPersonalRecord> personalRecords)
    {
        if ((uint)species >= (uint)personalRecords.Count)
        {
            return "Species with a resolvable Sword/Shield personal record";
        }

        var record = personalRecords[species];
        var maximumForm = record.FormStatsIndex > 0
            ? Math.Max(0, record.FormCount - 1)
            : 0;
        return maximumForm == 0
            ? "Form 0 for this species"
            : $"Form 0 through {maximumForm.ToString(CultureInfo.InvariantCulture)} for this species";
    }

    private static bool IsContractIv(int value)
    {
        return value is >= SwShDynamaxAdventureArchive.RandomIvValue
            and <= SwShDynamaxAdventureArchive.MaximumFixedIvValue;
    }

    private static bool HasRecoverableTableErrors(IEnumerable<ValidationDiagnostic> diagnostics)
    {
        var errors = diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        return errors.Any(diagnostic =>
                diagnostic.Message.Contains(
                    "source table byte layout differs from the vanilla table",
                    StringComparison.Ordinal)
                || diagnostic.Message.StartsWith("Dynamax Adventures row ", StringComparison.Ordinal)
                || diagnostic.Message.Contains("contains changes on a hidden normal or boss row", StringComparison.Ordinal))
            && errors.All(diagnostic =>
                diagnostic.Message.Contains("source table byte layout differs from the vanilla table", StringComparison.Ordinal)
                || diagnostic.Message.StartsWith("Dynamax Adventures row ", StringComparison.Ordinal)
                || diagnostic.Message.Contains("contains changes on a hidden normal or boss row", StringComparison.Ordinal)
                || diagnostic.Message.Contains("could not map complete base, ability, Gigantamax, and move options", StringComparison.Ordinal)
                || diagnostic.Message.Contains("could not map any verified safe normal-route species options", StringComparison.Ordinal)
                || diagnostic.Message.Contains("personal data is missing or unreadable", StringComparison.Ordinal)
                || diagnostic.Message.Contains("move data is missing or unreadable", StringComparison.Ordinal)
                || diagnostic.Message.Contains("learnset data is missing or unreadable", StringComparison.Ordinal));
    }

    private static bool Invalid(
        SwShDynamaxAdventureRecord entry,
        string invalidField,
        string expectedValue,
        out int entryIndex,
        out string field,
        out string expected)
    {
        entryIndex = entry.EntryIndex;
        field = invalidField;
        expected = expectedValue;
        return false;
    }

    private static SwShDynamaxAdventuresWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShDynamaxAdventureEntry> encounters,
        int sourceFileCount,
        DynamaxAdventureLookupTables lookupTables,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        var workflowDiagnostics = diagnostics.ToList();
        var safeNormalSpeciesOptions = CreateSafeNormalSpeciesOptions(encounters, lookupTables);
        if (encounters.Any(encounter => encounter.IsEditable)
            && safeNormalSpeciesOptions.Count == 0)
        {
            workflowDiagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures could not map any verified safe normal-route species options.",
                field: SpeciesField,
                expected: "Readable species names and personal data with at least one safe replacement"));
        }

        if (summary.Availability == SwShWorkflowAvailability.Available
            && workflowDiagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            summary = summary with { Availability = SwShWorkflowAvailability.ReadOnly };
        }

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
            workflowDiagnostics);
    }

    private static DynamaxAdventureLookupTables CreateEmptyLookupTables()
    {
        return new DynamaxAdventureLookupTables(
            [],
            new HashSet<int>(),
            [],
            [],
            new HashSet<int>(),
            [],
            [],
            SwShPokemonAbilityOptionResolver.Empty,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<SwShDynamaxAdventureEditableField> CreateEditableFields(
        DynamaxAdventureLookupTables lookupTables,
        IReadOnlyList<SwShDynamaxAdventureEditableFieldOption> safeNormalSpeciesOptions)
    {
        var itemOptions = CreateIndexedOptions(lookupTables.ItemNames, "Item");
        var moveOptions = SwShMoveAvailability.CreateMoveOptions(
                lookupTables.MoveNames,
                lookupTables.UsableMoveIds,
                (value, label) => new SwShDynamaxAdventureEditableFieldOption(value, label))
            .Where(option => option.Value <= MaximumSwordShieldMoveId)
            .ToArray();

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
        if (personal?.IsPresentInGame != true)
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
        SwShDynamaxAdventureArchive archive,
        DynamaxAdventureLookupTables lookupTables,
        SwShDynamaxAdventureProvenance provenance,
        bool isEditable,
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
            AbilityOptions = CreateLayoutSafeAbilityOptions(lookupTables, archive, entry),
            GigantamaxOptions = CreateLayoutSafeGigantamaxOptions(archive, entry),
            MoveOptions = CreateMoveOptions(lookupTables, entry, vanillaEntry),
            LayoutWritableFields = isEditable ? CreateLayoutWritableFields(archive, entry) : [],
            BossTargetOptions = [],
            BossTargetSpeciesId = entry.Species,
            BossTargetSpecies = GetIndexedName(entry.Species, lookupTables.SpeciesNames, "Species"),
            VanillaPokemon = vanillaEntry is null ? null : ToPokemonSnapshot(vanillaEntry, lookupTables),
        };
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

    private static DynamaxAdventureMainState AnalyzeMainState(
        OpenedProject project,
        SwShDynamaxAdventureArchive effectiveArchive,
        SwShDynamaxAdventureArchive? baseArchive,
        SwShDynamaxAdventureArchive? recognizedSourceArchive,
        ISet<string> parsedSourcePaths,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var unavailable = new SwShDynamaxAdventuresMainAnalysis(
            SwShDynamaxAdventuresMainKind.Conflict,
            "Dynamax Adventures executable state is unavailable until base and effective exefs/main can be verified.",
            "unknown",
            DetectedGame: null,
            RequiresSummaryMirror: false,
            RequiresCommandValidatorPatch: false,
            SummaryMatchesEffectiveArchive: false,
            CommandValidatorsMatchEffectiveArchive: false);
        if (baseArchive is null)
        {
            return new DynamaxAdventureMainState("blocked", unavailable, []);
        }

        var mainSource = ResolveWorkflowFile(project, SwShExeFsPatchWorkflowService.ExeFsMainPath);
        var baseMainPath = CombineGraphPath(project.Paths.BaseExeFsPath, "main");
        if (mainSource is null || baseMainPath is null || !File.Exists(baseMainPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures requires readable base and effective exefs/main sources for mirror inspection.",
                file: SwShExeFsPatchWorkflowService.ExeFsMainPath,
                expected: "Selected-game Sword/Shield base and effective exefs/main"));
            return new DynamaxAdventureMainState("blocked", unavailable, []);
        }

        try
        {
            var effectiveMainBytes = File.ReadAllBytes(mainSource.AbsolutePath);
            parsedSourcePaths.Add(Path.GetFullPath(mainSource.AbsolutePath));
            var baseMainBytes = File.ReadAllBytes(baseMainPath);
            parsedSourcePaths.Add(Path.GetFullPath(baseMainPath));
            var analysis = SwShDynamaxAdventuresMainPatcher.Analyze(
                effectiveMainBytes,
                baseMainBytes,
                effectiveArchive,
                baseArchive,
                project.Paths.SelectedGame,
                recognizedSourceArchive);
            var blocked = analysis.Kind is SwShDynamaxAdventuresMainKind.UnsupportedBuild
                or SwShDynamaxAdventuresMainKind.GameMismatch
                or SwShDynamaxAdventuresMainKind.Conflict;
            if (blocked)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    analysis.Message,
                    file: SwShExeFsPatchWorkflowService.ExeFsMainPath,
                    expected: "Supported selected-game DA executable projection"));
            }
            else if (analysis.HasLegacyBossTargetPatch)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    "An exact legacy Dynamax Adventures final-boss target remap is installed. This unsupported executable code must be removed explicitly with Stage Repair or a full vanilla table restore before ordinary Adventure edits can continue.",
                    file: SwShExeFsPatchWorkflowService.ExeFsMainPath,
                    expected: "Vanilla final-boss target call sites"));
            }

            var reservedRegions = analysis.DetectedGame is ProjectGame.Sword or ProjectGame.Shield
                ? SwShExeFsReservedRegionLedger
                    .MainTextRegionsForOwner(
                        SwShExeFsReservedRegionLedger.OwnerDynamaxAdventures,
                        analysis.DetectedGame)
                    .Concat(SwShExeFsReservedRegionLedger.MainRoRegionsForOwner(
                        SwShExeFsReservedRegionLedger.OwnerDynamaxAdventures))
                    .Select(region => new SwShDynamaxAdventureReservedRegion(
                        region.Area,
                        region.OffsetLabel,
                        region.Label,
                        region.Rule))
                    .ToArray()
                : [];
            return new DynamaxAdventureMainState(
                blocked
                    ? "blocked"
                    : analysis.Kind == SwShDynamaxAdventuresMainKind.Vanilla
                        ? "available"
                        : analysis.Kind == SwShDynamaxAdventuresMainKind.Stale
                            ? "repairable"
                            : "modified",
                analysis,
                reservedRegions);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventures could not inspect exefs/main: {exception.Message}",
                file: SwShExeFsPatchWorkflowService.ExeFsMainPath,
                expected: "Readable supported selected-game executable"));
            return new DynamaxAdventureMainState("blocked", unavailable, []);
        }
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
                    : ivs.Hp == SwShDynamaxAdventureArchive.RandomIvValue
                        ? "random HP"
                        : $"HP {ivs.Hp.ToString(CultureInfo.InvariantCulture)}",
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
        var parsedSourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var messageRoot = ResolveLanguageMessageRoot(project, diagnostics);
        var speciesNames = LoadMessageTable(
            project,
            messageRoot,
            "monsname.dat",
            parsedSourcePaths,
            diagnostics);
        var itemNames = LoadMessageTable(
            project,
            messageRoot,
            "itemname.dat",
            parsedSourcePaths,
            diagnostics);
        var moveNames = LoadMessageTable(
            project,
            messageRoot,
            "wazaname.dat",
            parsedSourcePaths,
            diagnostics);
        var itemDisplayNames = SwShItemsWorkflowService.CreateItemDisplayNames(project, itemNames, moveNames);
        var moveAvailability = SwShMoveAvailability.Load(project);
        parsedSourcePaths.UnionWith(moveAvailability.ParsedSourcePaths);
        var usableMoveIds = moveAvailability.UsableMoveIds;
        var personalRecords = LoadPersonalRecords(project, parsedSourcePaths);
        var presentSpeciesIds = SwShSpeciesAvailability.CreatePresentSpeciesIds(personalRecords);
        var learnsetRecords = LoadLearnsetRecords(project, parsedSourcePaths);
        var abilityResolver = SwShPokemonAbilityOptionResolver.Load(project);
        parsedSourcePaths.UnionWith(abilityResolver.ParsedSourcePaths);

        if (personalRecords.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures personal data is missing or unreadable. Pokemon rows remain read-only because species and form safety cannot be verified.",
                file: SwShPersonalTable.PersonalDataRelativePath,
                expected: "Readable Sword/Shield personal_total.bin"));
        }

        if (!moveAvailability.HasSemanticData || usableMoveIds.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures move data is missing or unreadable. Pokemon rows remain read-only because usable moves cannot be verified.",
                file: SwShMoveDataFile.MoveDataRelativeDirectory,
                expected: "Readable Sword/Shield move data with at least one usable move"));
        }

        if (learnsetRecords.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures learnset data is missing or unreadable. Pokemon rows remain read-only because move compatibility cannot be verified.",
                file: SwShPokemonLearnsetTable.LearnsetDataRelativePath,
                expected: "Readable Sword/Shield wazaoboe_total.bin"));
        }

        return new DynamaxAdventureLookupTables(
            speciesNames,
            presentSpeciesIds,
            itemDisplayNames,
            moveNames,
            usableMoveIds,
            personalRecords,
            learnsetRecords,
            abilityResolver,
            parsedSourcePaths);
    }

    private static IReadOnlyList<SwShPersonalRecord> LoadPersonalRecords(
        OpenedProject project,
        ISet<string> parsedSourcePaths)
    {
        var source = ResolveWorkflowFile(project, SwShPersonalTable.PersonalDataRelativePath);
        if (source is null)
        {
            return [];
        }

        try
        {
            var records = SwShPersonalTable.Parse(File.ReadAllBytes(source.AbsolutePath)).Records;
            parsedSourcePaths.Add(Path.GetFullPath(source.AbsolutePath));
            return records;
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

    private static IReadOnlyList<SwShPokemonLearnsetRecord> LoadLearnsetRecords(
        OpenedProject project,
        ISet<string> parsedSourcePaths)
    {
        var source = ResolveWorkflowFile(project, SwShPokemonLearnsetTable.LearnsetDataRelativePath);
        if (source is null)
        {
            return [];
        }

        try
        {
            var records = SwShPokemonLearnsetTable.Parse(File.ReadAllBytes(source.AbsolutePath)).Records;
            parsedSourcePaths.Add(Path.GetFullPath(source.AbsolutePath));
            return records;
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
            if (moveId <= MaximumSwordShieldMoveId
                && SwShDynamaxAdventureSafetyRules.CanLearnMove(personal, learnset, moveId, entry.Level))
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

    internal static IReadOnlyList<SwShDynamaxAdventureEditableFieldOption> FilterAbilityOptionsForLayout(
        IReadOnlyList<SwShDynamaxAdventureEditableFieldOption> options,
        IReadOnlyList<SwShDynamaxAdventureEditableFieldOption> layoutSafeOptions)
    {
        if (options.Count == 0 || layoutSafeOptions.Count == 0)
        {
            return [];
        }

        var safeValues = layoutSafeOptions
            .Select(option => option.Value)
            .ToHashSet();
        return options
            .Where(option => safeValues.Contains(option.Value))
            .ToArray();
    }

    private static IReadOnlyList<SwShDynamaxAdventureEditableFieldOption> CreateLayoutSafeAbilityOptions(
        DynamaxAdventureLookupTables lookupTables,
        SwShDynamaxAdventureArchive archive,
        SwShDynamaxAdventureRecord entry)
    {
        return CreateAbilityOptions(lookupTables, entry.Species, entry.Form)
            .Where(option => archive.CanWriteEditPreservingLayout(new SwShDynamaxAdventureEdit(
                entry.EntryIndex,
                SwShDynamaxAdventureField.Ability,
                option.Value)))
            .ToArray();
    }

    private static IReadOnlyList<SwShDynamaxAdventureEditableFieldOption> CreateLayoutSafeGigantamaxOptions(
        SwShDynamaxAdventureArchive archive,
        SwShDynamaxAdventureRecord entry)
    {
        return CreateGigantamaxOptions(entry.Species, entry.GigantamaxState)
            .Where(option => archive.CanWriteEditPreservingLayout(new SwShDynamaxAdventureEdit(
                entry.EntryIndex,
                SwShDynamaxAdventureField.GigantamaxState,
                option.Value)))
            .ToArray();
    }

    private static IReadOnlyList<string> CreateLayoutWritableFields(
        SwShDynamaxAdventureArchive archive,
        SwShDynamaxAdventureRecord entry)
    {
        var probes = new (string Field, SwShDynamaxAdventureField ArchiveField, int Value)[]
        {
            (SpeciesField, SwShDynamaxAdventureField.Species, entry.Species == 1 ? 2 : 1),
            (FormField, SwShDynamaxAdventureField.Form, entry.Form == 1 ? 2 : 1),
            (LevelField, SwShDynamaxAdventureField.Level, entry.Level == 1 ? 2 : 1),
            (AbilityField, SwShDynamaxAdventureField.Ability, entry.Ability == 1 ? 2 : 1),
            (GigantamaxStateField, SwShDynamaxAdventureField.GigantamaxState, entry.GigantamaxState == 1 ? 2 : 1),
            (Move0Field, SwShDynamaxAdventureField.Move0, entry.Moves[0] == 1 ? 2 : 1),
            (Move1Field, SwShDynamaxAdventureField.Move1, entry.Moves[1] == 1 ? 2 : 1),
            (Move2Field, SwShDynamaxAdventureField.Move2, entry.Moves[2] == 1 ? 2 : 1),
            (Move3Field, SwShDynamaxAdventureField.Move3, entry.Moves[3] == 1 ? 2 : 1),
            (GuaranteedPerfectIvsField, SwShDynamaxAdventureField.GuaranteedPerfectIvs,
                SwShDynamaxAdventureArchive.GetGuaranteedPerfectIvCount(entry.Ivs) == 2 ? 3 : 2),
            (IvAttackField, SwShDynamaxAdventureField.IvAttack,
                entry.Ivs.Attack == SwShDynamaxAdventureArchive.RandomIvValue ? 1 : SwShDynamaxAdventureArchive.RandomIvValue),
            (IvDefenseField, SwShDynamaxAdventureField.IvDefense,
                entry.Ivs.Defense == SwShDynamaxAdventureArchive.RandomIvValue ? 1 : SwShDynamaxAdventureArchive.RandomIvValue),
            (IvSpeedField, SwShDynamaxAdventureField.IvSpeed,
                entry.Ivs.Speed == SwShDynamaxAdventureArchive.RandomIvValue ? 1 : SwShDynamaxAdventureArchive.RandomIvValue),
            (IvSpecialAttackField, SwShDynamaxAdventureField.IvSpecialAttack,
                entry.Ivs.SpecialAttack == SwShDynamaxAdventureArchive.RandomIvValue ? 1 : SwShDynamaxAdventureArchive.RandomIvValue),
            (IvSpecialDefenseField, SwShDynamaxAdventureField.IvSpecialDefense,
                entry.Ivs.SpecialDefense == SwShDynamaxAdventureArchive.RandomIvValue ? 1 : SwShDynamaxAdventureArchive.RandomIvValue),
        };

        return probes
            .Where(probe =>
                (probe.Field != GuaranteedPerfectIvsField || entry.Ivs.Hp < 0)
                && archive.CanWriteEditPreservingLayout(new SwShDynamaxAdventureEdit(
                    entry.EntryIndex,
                    probe.ArchiveField,
                    probe.Value)))
            .Select(probe => probe.Field)
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

    private VanillaDynamaxAdventureTable? LoadVanillaArchive(
        OpenedProject project,
        ISet<string> parsedSourcePaths,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var graphEntry = project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, DynamaxAdventureDataPath, StringComparison.OrdinalIgnoreCase));
        if (graphEntry?.BaseFile is null
            || !graphEntry.RelativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "The base Dynamax Adventures table is missing. Pokemon rows remain read-only because vanilla metadata and restore values cannot be verified.",
                file: DynamaxAdventureDataPath,
                expected: "Verified base Sword/Shield Dynamax Adventures table"));
            return null;
        }

        var sourcePath = CombineGraphPath(
            project.Paths.BaseRomFsPath,
            graphEntry.RelativePath["romfs/".Length..]);
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "The base Dynamax Adventures table could not be resolved. Pokemon rows remain read-only because vanilla metadata and restore values cannot be verified.",
                file: DynamaxAdventureDataPath,
                expected: "Verified base Sword/Shield Dynamax Adventures table"));
            return null;
        }

        try
        {
            var data = ReadBoundedDynamaxAdventureTable(sourcePath);
            var archive = SwShDynamaxAdventureArchive.Parse(data);
            parsedSourcePaths.Add(Path.GetFullPath(sourcePath));
            if (!IsCanonicalBaseDynamaxAdventureTable(data, archive))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "The base Dynamax Adventures table does not match the supported canonical Sword/Shield table identity.",
                    file: DynamaxAdventureDataPath,
                    expected: $"Exactly {CanonicalBaseTableLength.ToString(CultureInfo.InvariantCulture)} bytes and {CanonicalBaseTableRowCount.ToString(CultureInfo.InvariantCulture)} rows with the supported canonical content hash"));
                return null;
            }

            return new VanillaDynamaxAdventureTable(archive, data);
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Vanilla Dynamax Adventures data could not be decoded for restore values: {exception.Message}",
                file: DynamaxAdventureDataPath,
                expected: "Base Sword/Shield Dynamax Adventures table"));
            return null;
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Vanilla Dynamax Adventures data could not be read for restore values: {exception.Message}",
                file: DynamaxAdventureDataPath,
                expected: "Readable base Sword/Shield Dynamax Adventures table"));
            return null;
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
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
                $"English Dynamax Adventures lookup text was not found; using '{language}' lookup tables instead.",
                expected: $"{MessageRootPath}/{PreferredLanguage}/common/monsname.dat"));
        }

        return $"{MessageRootPath}/{language}/common";
    }

    private static string[] LoadMessageTable(
        OpenedProject project,
        string? messageRoot,
        string fileName,
        ISet<string> parsedSourcePaths,
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
            var names = SwShGameTextFile.Parse(File.ReadAllBytes(source.AbsolutePath))
                .Lines
                .Select(line => line.Text)
                .ToArray();
            parsedSourcePaths.Add(Path.GetFullPath(source.AbsolutePath));
            return names;
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

    internal static bool IsDynamaxAdventureTableLayoutCompatible(
        SwShDynamaxAdventureArchive baseArchive,
        byte[] baseData,
        SwShDynamaxAdventureArchive sourceArchive,
        byte[] sourceData)
    {
        ArgumentNullException.ThrowIfNull(baseArchive);
        ArgumentNullException.ThrowIfNull(baseData);
        ArgumentNullException.ThrowIfNull(sourceArchive);
        ArgumentNullException.ThrowIfNull(sourceData);

        if (HasKnownUnsafeDynamaxAdventureTableLayout(baseData)
            || HasKnownUnsafeDynamaxAdventureTableLayout(sourceData))
        {
            return false;
        }

        if (sourceData.SequenceEqual(baseData))
        {
            return true;
        }

        if (sourceData.Length != baseData.Length)
        {
            return false;
        }

        try
        {
            if (!TryCreateLayoutPreservingEdits(baseArchive, sourceArchive, out var edits))
            {
                return false;
            }

            return baseArchive
                .WriteEditsPreservingLayout(edits)
                .SequenceEqual(sourceData);
        }
        catch (Exception exception) when (exception is
            InvalidDataException
            or ArgumentOutOfRangeException
            or OverflowException)
        {
            return false;
        }
    }

    internal static bool IsCanonicalBaseDynamaxAdventureTable(
        byte[] data,
        SwShDynamaxAdventureArchive archive)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(archive);

        return data.Length == CanonicalBaseTableLength
            && archive.Entries.Count == CanonicalBaseTableRowCount
            && string.Equals(
                Convert.ToHexString(SHA256.HashData(data)),
                CanonicalBaseTableSha256,
                StringComparison.Ordinal);
    }

    internal bool AcceptsBaseTable(byte[] data, SwShDynamaxAdventureArchive archive)
    {
        return IsCanonicalBaseDynamaxAdventureTable(data, archive);
    }

    internal static byte[] ReadBoundedDynamaxAdventureTable(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        if (stream.Length < sizeof(uint)
            || stream.Length > SwShDynamaxAdventureArchive.MaximumArchiveByteLength)
        {
            throw new InvalidDataException(
                "Dynamax Adventure table length is outside the supported bounded file size.");
        }

        var data = new byte[checked((int)stream.Length)];
        stream.ReadExactly(data);
        return data;
    }

    internal static bool IsSupportedGame(ProjectGame? game)
    {
        return game is ProjectGame.Sword or ProjectGame.Shield;
    }

    internal bool CanRecognizeSourceMainProjection(
        SwShDynamaxAdventureArchive sourceArchive,
        SwShDynamaxAdventureArchive baseArchive)
    {
        ArgumentNullException.ThrowIfNull(sourceArchive);
        ArgumentNullException.ThrowIfNull(baseArchive);

        return baseArchive.Entries.Count == CanonicalBaseTableRowCount
            && sourceArchive.Entries.Count == CanonicalBaseTableRowCount
            && sourceArchive.Entries.Select((entry, index) => (entry, index)).All(pair =>
                pair.entry.EntryIndex == pair.index
                && pair.entry.Species is >= short.MinValue and <= short.MaxValue
                && pair.entry.Form is >= sbyte.MinValue and <= sbyte.MaxValue
                && pair.entry.ShinyRoll is >= sbyte.MinValue and <= sbyte.MaxValue);
    }

    private static bool HasKnownUnsafeDynamaxAdventureTableLayout(byte[] data)
    {
        return string.Equals(
            Convert.ToHexString(SHA256.HashData(data)),
            KnownUnsafeRebuiltVanillaTableSha256,
            StringComparison.Ordinal);
    }

    private static bool TryCreateLayoutPreservingEdits(
        SwShDynamaxAdventureArchive baseArchive,
        SwShDynamaxAdventureArchive sourceArchive,
        out IReadOnlyList<SwShDynamaxAdventureEdit> edits)
    {
        var pending = new List<SwShDynamaxAdventureEdit>();
        edits = pending;

        if (baseArchive.Entries.Count != sourceArchive.Entries.Count)
        {
            return false;
        }

        for (var index = 0; index < baseArchive.Entries.Count; index++)
        {
            var left = baseArchive.Entries[index];
            var right = sourceArchive.Entries[index];
            if (left.EntryIndex != right.EntryIndex
                || left.SingleCaptureFlagBlock != right.SingleCaptureFlagBlock
                || left.Field02 != right.Field02
                || left.AdventureIndex != right.AdventureIndex
                || left.UiMessageId != right.UiMessageId
                || left.IsSingleCapture != right.IsSingleCapture
                || left.BallItemId != right.BallItemId
                || left.OtGender != right.OtGender
                || left.Version != right.Version
                || left.ShinyRoll != right.ShinyRoll
                || left.IsStoryProgressGated != right.IsStoryProgressGated)
            {
                return false;
            }

            if (SwShDynamaxAdventureSafetyRules.IsBossEntryIndex(index)
                && !RecordsEqual(left, right))
            {
                return false;
            }

            AddEditIfChanged(pending, right, SwShDynamaxAdventureField.Form, left.Form, right.Form);
            AddEditIfChanged(pending, right, SwShDynamaxAdventureField.GigantamaxState, left.GigantamaxState, right.GigantamaxState);
            AddEditIfChanged(pending, right, SwShDynamaxAdventureField.Level, left.Level, right.Level);
            AddEditIfChanged(pending, right, SwShDynamaxAdventureField.Species, left.Species, right.Species);
            AddEditIfChanged(pending, right, SwShDynamaxAdventureField.IvSpeed, left.Ivs.Speed, right.Ivs.Speed);
            AddEditIfChanged(pending, right, SwShDynamaxAdventureField.IvAttack, left.Ivs.Attack, right.Ivs.Attack);
            AddEditIfChanged(pending, right, SwShDynamaxAdventureField.IvDefense, left.Ivs.Defense, right.Ivs.Defense);
            AddEditIfChanged(
                pending,
                right,
                SwShDynamaxAdventureField.GuaranteedPerfectIvs,
                SwShDynamaxAdventureArchive.GetGuaranteedPerfectIvCount(left.Ivs),
                SwShDynamaxAdventureArchive.GetGuaranteedPerfectIvCount(right.Ivs));
            AddEditIfChanged(pending, right, SwShDynamaxAdventureField.IvSpecialAttack, left.Ivs.SpecialAttack, right.Ivs.SpecialAttack);
            AddEditIfChanged(pending, right, SwShDynamaxAdventureField.IvSpecialDefense, left.Ivs.SpecialDefense, right.Ivs.SpecialDefense);
            AddEditIfChanged(pending, right, SwShDynamaxAdventureField.Ability, left.Ability, right.Ability);
            AddEditIfChanged(pending, right, SwShDynamaxAdventureField.Move0, left.Moves[0], right.Moves[0]);
            AddEditIfChanged(pending, right, SwShDynamaxAdventureField.Move1, left.Moves[1], right.Moves[1]);
            AddEditIfChanged(pending, right, SwShDynamaxAdventureField.Move2, left.Moves[2], right.Moves[2]);
            AddEditIfChanged(pending, right, SwShDynamaxAdventureField.Move3, left.Moves[3], right.Moves[3]);
        }

        return true;
    }

    private static bool RecordsEqual(
        SwShDynamaxAdventureRecord left,
        SwShDynamaxAdventureRecord right)
    {
        return left.EntryIndex == right.EntryIndex
            && left.IsSingleCapture == right.IsSingleCapture
            && left.SingleCaptureFlagBlock == right.SingleCaptureFlagBlock
            && left.Field02 == right.Field02
            && left.Form == right.Form
            && left.GigantamaxState == right.GigantamaxState
            && left.BallItemId == right.BallItemId
            && left.AdventureIndex == right.AdventureIndex
            && left.Level == right.Level
            && left.Species == right.Species
            && left.UiMessageId == right.UiMessageId
            && left.OtGender == right.OtGender
            && left.Version == right.Version
            && left.ShinyRoll == right.ShinyRoll
            && left.Ivs == right.Ivs
            && left.Ability == right.Ability
            && left.IsStoryProgressGated == right.IsStoryProgressGated
            && left.Moves.SequenceEqual(right.Moves);
    }

    private static void AddEditIfChanged(
        ICollection<SwShDynamaxAdventureEdit> edits,
        SwShDynamaxAdventureRecord sourceRecord,
        SwShDynamaxAdventureField field,
        int currentValue,
        int sourceValue)
    {
        if (currentValue == sourceValue)
        {
            return;
        }

        edits.Add(new SwShDynamaxAdventureEdit(sourceRecord.EntryIndex, field, sourceValue));
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
        IReadOnlySet<string> ParsedSourcePaths);

    private sealed record VanillaDynamaxAdventureTable(
        SwShDynamaxAdventureArchive Archive,
        byte[] Data);

    private sealed record DynamaxAdventureMainState(
        string InstallStatus,
        SwShDynamaxAdventuresMainAnalysis Analysis,
        IReadOnlyList<SwShDynamaxAdventureReservedRegion> ReservedRegions);

    internal sealed record WorkflowFileSource(
        ProjectFileGraphEntry GraphEntry,
        string AbsolutePath);
}
