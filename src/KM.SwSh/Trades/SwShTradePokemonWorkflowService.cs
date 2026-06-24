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

namespace KM.SwSh.Trades;

public sealed class SwShTradePokemonWorkflowService
{
    public const string SpeciesField = "species";
    public const string FormField = "form";
    public const string LevelField = "level";
    public const string HeldItemIdField = "heldItemId";
    public const string BallItemIdField = "ballItemId";
    public const string Field03Field = "field03";
    public const string AbilityField = "ability";
    public const string NatureField = "nature";
    public const string GenderField = "gender";
    public const string ShinyLockField = "shinyLock";
    public const string DynamaxLevelField = "dynamaxLevel";
    public const string CanGigantamaxField = "canGigantamax";
    public const string RequiredSpeciesField = "requiredSpecies";
    public const string RequiredFormField = "requiredForm";
    public const string RequiredNatureField = "requiredNature";
    public const string UnknownRequirementField = "unknownRequirement";
    public const string TrainerIdField = "trainerId";
    public const string OtGenderField = "otGender";
    public const string MemoryCodeField = "memoryCode";
    public const string MemoryTextVariableField = "memoryTextVariable";
    public const string MemoryFeelField = "memoryFeel";
    public const string MemoryIntensityField = "memoryIntensity";
    public const string RelearnMove0Field = "relearnMove0";
    public const string RelearnMove1Field = "relearnMove1";
    public const string RelearnMove2Field = "relearnMove2";
    public const string RelearnMove3Field = "relearnMove3";
    public const string IvHpField = "ivHp";
    public const string IvAttackField = "ivAttack";
    public const string IvDefenseField = "ivDefense";
    public const string IvSpeedField = "ivSpeed";
    public const string IvSpecialAttackField = "ivSpecialAttack";
    public const string IvSpecialDefenseField = "ivSpecialDefense";
    public const string FlawlessIvCountField = "flawlessIvCount";
    public const string TradePokemonDataPath = "romfs/bin/script_event_data/field_trade.bin";
    public const string LegacyTradePokemonDataPath = "romfs/bin/script_event_data/field_trade_data.bin";

    internal const string TradePokemonEditDomain = "workflow.tradePokemon";

    private const string MessageRootPath = "romfs/bin/message";
    private const string PreferredLanguage = "English";

    private static readonly IReadOnlyList<SwShTradePokemonEditableFieldOption> BooleanOptions =
    [
        new(0, "No"),
        new(1, "Yes"),
    ];

    private static readonly IReadOnlyList<SwShTradePokemonEditableFieldOption> AbilityOptions =
    [
        new(0, "Default"),
        new(1, "Ability 1"),
        new(2, "Ability 2"),
        new(3, "Hidden Ability"),
    ];

    private static readonly IReadOnlyList<SwShTradePokemonEditableFieldOption> GenderOptions =
    [
        new(0, "Random"),
        new(1, "Male"),
        new(2, "Female"),
    ];

    private static readonly IReadOnlyList<SwShTradePokemonEditableFieldOption> ShinyLockOptions =
    [
        new(0, "Random"),
        new(1, "Always Shiny"),
        new(2, "Never Shiny"),
    ];

    private static readonly IReadOnlyList<SwShTradePokemonEditableFieldOption> OtGenderOptions =
    [
        new(0, "Male"),
        new(1, "Female"),
    ];

    private static readonly IReadOnlyList<SwShTradePokemonEditableFieldOption> FlawlessIvCountOptions =
    [
        new(0, "Random IVs"),
        new(3, "3 Guaranteed Perfect IVs"),
        new(6, "6 Guaranteed Perfect IVs"),
    ];

    private static readonly IReadOnlyList<SwShTradePokemonEditableFieldOption> FormOptions =
    [
        new(0, "Base"),
        ..Enumerable.Range(1, 31).Select(value => new SwShTradePokemonEditableFieldOption(value, $"Form {value}")),
    ];

    private static readonly IReadOnlyList<SwShTradePokemonEditableFieldOption> DynamaxLevelOptions =
    [
        ..Enumerable.Range(0, 11).Select(value => new SwShTradePokemonEditableFieldOption(
            value,
            value.ToString(CultureInfo.InvariantCulture))),
    ];

    private static readonly IReadOnlyList<SwShTradePokemonEditableFieldOption> NatureOptions =
    [
        ..SwShNatureLabels.WithRandom.Select(nature => new SwShTradePokemonEditableFieldOption(
            nature.Value,
            nature.Label)),
    ];

    private static readonly IReadOnlyList<SwShTradePokemonEditableField> BaseEditableFields =
    [
        CreateField(SpeciesField, "Species", "integer", 0, SwShTradePokemonArchive.MaximumIdValue),
        CreateField(FormField, "Form", "integer", 0, SwShTradePokemonArchive.MaximumByteValue, FormOptions),
        CreateField(LevelField, "Level", "integer", 0, SwShTradePokemonArchive.MaximumByteValue),
        CreateField(HeldItemIdField, "Held item", "integer", 0, SwShTradePokemonArchive.MaximumIdValue),
        CreateField(BallItemIdField, "Ball item", "integer", 0, SwShTradePokemonArchive.MaximumIdValue),
        CreateField(Field03Field, "Unknown field 03", "integer", 0, SwShTradePokemonArchive.MaximumIdValue),
        CreateField(AbilityField, "Ability slot", "integer", 0, 3, AbilityOptions),
        CreateField(NatureField, "Nature", "integer", 0, 25, NatureOptions),
        CreateField(GenderField, "Gender", "integer", 0, 2, GenderOptions),
        CreateField(ShinyLockField, "Shiny lock", "integer", 0, SwShTradePokemonArchive.MaximumIdValue, ShinyLockOptions),
        CreateField(DynamaxLevelField, "Dynamax level", "integer", 0, 10, DynamaxLevelOptions),
        CreateField(CanGigantamaxField, "Can Gigantamax", "boolean", 0, 1, BooleanOptions),
        CreateField(RequiredSpeciesField, "Requested species", "integer", 0, SwShTradePokemonArchive.MaximumIdValue),
        CreateField(RequiredFormField, "Requested form", "integer", 0, SwShTradePokemonArchive.MaximumByteValue, FormOptions),
        CreateField(RequiredNatureField, "Requested nature", "integer", 0, 25, NatureOptions),
        CreateField(UnknownRequirementField, "Unknown requirement", "integer", 0, SwShTradePokemonArchive.MaximumByteValue),
        CreateField(TrainerIdField, "Trainer ID", "integer", 0, SwShTradePokemonArchive.MaximumIdValue),
        CreateField(OtGenderField, "OT gender", "integer", 0, SwShTradePokemonArchive.MaximumByteValue, OtGenderOptions),
        CreateField(MemoryCodeField, "Memory code", "integer", 0, SwShTradePokemonArchive.MaximumByteValue),
        CreateField(MemoryTextVariableField, "Memory text variable", "integer", 0, ushort.MaxValue),
        CreateField(MemoryFeelField, "Memory feeling", "integer", 0, SwShTradePokemonArchive.MaximumByteValue),
        CreateField(MemoryIntensityField, "Memory intensity", "integer", 0, SwShTradePokemonArchive.MaximumByteValue),
        CreateField(RelearnMove0Field, "Relearn move 1", "integer", 0, ushort.MaxValue),
        CreateField(RelearnMove1Field, "Relearn move 2", "integer", 0, ushort.MaxValue),
        CreateField(RelearnMove2Field, "Relearn move 3", "integer", 0, ushort.MaxValue),
        CreateField(RelearnMove3Field, "Relearn move 4", "integer", 0, ushort.MaxValue),
        CreateField(IvHpField, "HP IV", "integer", SwShTradePokemonArchive.ThreePerfectIvSentinel, SwShTradePokemonArchive.MaximumFixedIvValue),
        CreateField(IvAttackField, "Attack IV", "integer", SwShTradePokemonArchive.RandomIvValue, SwShTradePokemonArchive.MaximumFixedIvValue),
        CreateField(IvDefenseField, "Defense IV", "integer", SwShTradePokemonArchive.RandomIvValue, SwShTradePokemonArchive.MaximumFixedIvValue),
        CreateField(IvSpeedField, "Speed IV", "integer", SwShTradePokemonArchive.RandomIvValue, SwShTradePokemonArchive.MaximumFixedIvValue),
        CreateField(IvSpecialAttackField, "Sp. Atk IV", "integer", SwShTradePokemonArchive.RandomIvValue, SwShTradePokemonArchive.MaximumFixedIvValue),
        CreateField(IvSpecialDefenseField, "Sp. Def IV", "integer", SwShTradePokemonArchive.RandomIvValue, SwShTradePokemonArchive.MaximumFixedIvValue),
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
                    "Trade Pokemon requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShTradePokemonWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);
        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(summary, [], sourceFileCount: 0, CreateEmptyLookupTables(), diagnostics);
        }

        var tradeSource = ResolveTradePokemonDataSource(project);
        if (tradeSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Trade Pokemon data is not available for this project.",
                expected: TradePokemonDataPath));
            return CreateWorkflow(summary, [], sourceFileCount: 0, CreateEmptyLookupTables(), diagnostics);
        }

        var lookupTables = LoadLookupTables(project, diagnostics);

        try
        {
            var archive = SwShTradePokemonArchive.Parse(File.ReadAllBytes(tradeSource.AbsolutePath));
            var provenance = CreateProvenance(tradeSource.GraphEntry);
            var trades = archive.Trades
                .Select(trade => ToTradeEntry(trade, lookupTables, provenance))
                .ToArray();
            var sourceFileCount = 1 + lookupTables.SourceFileCount;

            return CreateWorkflow(summary, trades, sourceFileCount, lookupTables, diagnostics);
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Trade Pokemon data source is not a supported Sword/Shield trade table: {exception.Message}",
                file: tradeSource.GraphEntry.RelativePath,
                expected: "Sword/Shield Trade Pokemon table"));
            return CreateWorkflow(summary, [], sourceFileCount: 1, lookupTables, diagnostics);
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Trade Pokemon data source could not be read: {exception.Message}",
                file: tradeSource.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield Trade Pokemon table"));
            return CreateWorkflow(summary, [], sourceFileCount: 1, lookupTables, diagnostics);
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Trade Pokemon data source could not be read: {exception.Message}",
                file: tradeSource.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield Trade Pokemon table"));
            return CreateWorkflow(summary, [], sourceFileCount: 1, lookupTables, diagnostics);
        }
    }

    internal static SwShTradePokemonEditableField? GetEditableField(string? field)
    {
        return BaseEditableFields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, field, StringComparison.Ordinal));
    }

    internal static bool IsEditableField(string? field)
    {
        return GetEditableField(field) is not null;
    }

    internal static string CreateTradeRecordId(int tradeIndex)
    {
        return $"trade:{tradeIndex.ToString(CultureInfo.InvariantCulture)}";
    }

    internal static bool TryParseTradeRecordId(string? recordId, out int tradeIndex)
    {
        tradeIndex = 0;

        const string prefix = "trade:";
        return recordId is not null
            && recordId.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(recordId[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out tradeIndex)
            && tradeIndex >= 0;
    }

    internal static WorkflowFileSource? ResolveTradePokemonDataSource(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return ResolveWorkflowFile(project, TradePokemonDataPath)
            ?? ResolveWorkflowFile(project, LegacyTradePokemonDataPath);
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

    private static SwShTradePokemonWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShTradePokemonEntry> trades,
        int sourceFileCount,
        TradeLookupTables lookupTables,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SwShTradePokemonWorkflow(
            summary,
            trades,
            CreateEditableFields(lookupTables),
            new SwShTradePokemonWorkflowStats(
                trades.Count,
                trades.Count(trade => trade.FlawlessIvCount is null),
                sourceFileCount),
            diagnostics);
    }

    private static TradeLookupTables CreateEmptyLookupTables()
    {
        return new TradeLookupTables([], new HashSet<int>(), [], [], new HashSet<int>(), SwShPokemonAbilityOptionResolver.Empty, SourceFileCount: 0);
    }

    private static IReadOnlyList<SwShTradePokemonEditableField> CreateEditableFields(TradeLookupTables lookupTables)
    {
        var speciesOptions = SwShSpeciesAvailability.CreateSpeciesOptions(
            lookupTables.SpeciesNames,
            lookupTables.PresentSpeciesIds,
            (value, label) => new SwShTradePokemonEditableFieldOption(value, label));
        var itemOptions = CreateIndexedOptions(lookupTables.ItemNames, "Item");
        var moveOptions = SwShMoveAvailability.CreateMoveOptions(
            lookupTables.MoveNames,
            lookupTables.UsableMoveIds,
            (value, label) => new SwShTradePokemonEditableFieldOption(value, label),
            includeNone: true);

        return BaseEditableFields
            .Select(field => field.Field switch
            {
                SpeciesField => field with { Options = speciesOptions },
                RequiredSpeciesField => field with { Options = speciesOptions },
                HeldItemIdField or BallItemIdField => field with { Options = itemOptions },
                RelearnMove0Field or RelearnMove1Field or RelearnMove2Field or RelearnMove3Field => field with { Options = moveOptions },
                _ => field,
            })
            .ToArray();
    }

    private static IReadOnlyList<SwShTradePokemonEditableFieldOption> CreateIndexedOptions(
        IReadOnlyList<string> names,
        string fallbackPrefix)
    {
        return names
            .Select((name, index) => new SwShTradePokemonEditableFieldOption(
                index,
                string.IsNullOrWhiteSpace(name)
                    ? $"{index.ToString("000", CultureInfo.InvariantCulture)} {fallbackPrefix} {index}"
                    : $"{index.ToString("000", CultureInfo.InvariantCulture)} {name}"))
            .ToArray();
    }

    private static SwShTradePokemonEntry ToTradeEntry(
        KM.Formats.SwSh.SwShTradePokemonRecord trade,
        TradeLookupTables lookupTables,
        SwShTradePokemonProvenance provenance)
    {
        var ivs = new SwShTradePokemonIvsRecord(
            trade.Ivs.Hp,
            trade.Ivs.Attack,
            trade.Ivs.Defense,
            trade.Ivs.SpecialAttack,
            trade.Ivs.SpecialDefense,
            trade.Ivs.Speed);
        var flawlessIvCount = SwShTradePokemonArchive.GetFlawlessIvCount(trade.Ivs);
        var species = GetIndexedName(trade.Species, lookupTables.SpeciesNames, "Species");
        var requiredSpecies = GetIndexedName(trade.RequiredSpecies, lookupTables.SpeciesNames, "Species");
        var heldItem = trade.HeldItem == 0
            ? null
            : GetIndexedName(trade.HeldItem, lookupTables.ItemNames, "Item");
        var relearnMoves = trade.RelearnMoves
            .Select((moveId, slot) => new SwShTradePokemonMoveRecord(
                slot,
                moveId,
                moveId == 0 ? null : GetIndexedName(moveId, lookupTables.MoveNames, "Move")))
            .ToArray();

        return new SwShTradePokemonEntry(
            trade.Index,
            FormatTradeLabel(
                trade.Index,
                requiredSpecies,
                trade.RequiredSpecies,
                trade.RequiredForm,
                species,
                trade.Species,
                trade.Form,
                trade.Level),
            trade.Species,
            species,
            trade.Form,
            trade.Level,
            trade.HeldItem,
            heldItem,
            trade.BallItemId,
            GetIndexedName(trade.BallItemId, lookupTables.ItemNames, "Item"),
            trade.Ability,
            GetAbilityOptionLabel(lookupTables, trade.Species, trade.Form, trade.Ability),
            trade.Nature,
            GetOptionLabel(NatureOptions, trade.Nature, "Nature"),
            trade.Gender,
            GetOptionLabel(GenderOptions, trade.Gender, "Gender"),
            trade.ShinyLock,
            GetOptionLabel(ShinyLockOptions, trade.ShinyLock, "Shiny lock"),
            trade.DynamaxLevel,
            trade.CanGigantamax,
            trade.RequiredSpecies,
            requiredSpecies,
            trade.RequiredForm,
            trade.RequiredNature,
            GetOptionLabel(NatureOptions, trade.RequiredNature, "Nature"),
            trade.UnknownRequirement,
            trade.TrainerId,
            trade.OtGender,
            GetOptionLabel(OtGenderOptions, trade.OtGender, "OT gender"),
            trade.MemoryCode,
            trade.MemoryTextVariable,
            trade.MemoryFeel,
            trade.MemoryIntensity,
            trade.Field03,
            trade.Hash0,
            trade.Hash1,
            trade.Hash2,
            relearnMoves,
            ivs,
            flawlessIvCount,
            FormatIvSummary(ivs, flawlessIvCount),
            provenance)
        {
            AbilityOptions = CreateAbilityOptions(lookupTables, trade.Species, trade.Form),
        };
    }

    private static string FormatTradeLabel(
        int tradeIndex,
        string requiredSpecies,
        int requiredSpeciesId,
        int requiredForm,
        string species,
        int speciesId,
        int form,
        int level)
    {
        var requested = SwShSpeciesFormLabels.FormatSpeciesFormLabel(requiredSpecies, requiredSpeciesId, requiredForm);
        var received = SwShSpeciesFormLabels.FormatSpeciesFormLabel(species, speciesId, form);

        return $"Trade {(tradeIndex + 1).ToString("000", CultureInfo.InvariantCulture)}: {requested} -> {received} Lv. {level}";
    }

    internal static string FormatIvSummary(SwShTradePokemonIvsRecord ivs, int? flawlessIvCount)
    {
        return flawlessIvCount switch
        {
            0 => "Random IVs",
            3 => "3 guaranteed perfect IVs",
            6 => "6 guaranteed perfect IVs",
            _ => string.Create(
                CultureInfo.InvariantCulture,
                $"HP {ivs.HP} / Atk {ivs.Attack} / Def {ivs.Defense} / SpA {ivs.SpecialAttack} / SpD {ivs.SpecialDefense} / Spe {ivs.Speed}"),
        };
    }

    internal static string GetOptionLabel(
        IReadOnlyList<SwShTradePokemonEditableFieldOption> options,
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

    private static TradeLookupTables LoadLookupTables(
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
        var abilityResolver = SwShPokemonAbilityOptionResolver.Load(project);

        return new TradeLookupTables(
            speciesNames,
            presentSpeciesIds,
            itemDisplayNames,
            moveNames,
            usableMoveIds,
            abilityResolver,
            CountSource(speciesNames)
                + CountSource(itemNames)
                + CountSource(moveNames)
                + (presentSpeciesIds.Count > 0 ? 1 : 0)
                + (usableMoveIds.Count > 0 ? 1 : 0));
    }

    private static IReadOnlyList<SwShTradePokemonEditableFieldOption> CreateAbilityOptions(
        TradeLookupTables lookupTables,
        int speciesId,
        int form)
    {
        return lookupTables.AbilityResolver
            .CreateOptions(speciesId, form, SwShAbilityOptionMode.DefaultPlusSlots)
            .Select(option => new SwShTradePokemonEditableFieldOption(option.Value, option.Label))
            .ToArray();
    }

    private static string GetAbilityOptionLabel(
        TradeLookupTables lookupTables,
        int speciesId,
        int form,
        int value)
    {
        return CreateAbilityOptions(lookupTables, speciesId, form)
            .FirstOrDefault(option => option.Value == value)?.Label
            ?? GetOptionLabel(AbilityOptions, value, "Ability slot");
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
                "Trade Pokemon lookup text is not available; numeric fallback labels will be shown.",
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
                $"English Trade Pokemon lookup text was not found; using '{language}' lookup tables instead.",
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
                $"Trade Pokemon lookup table '{relativePath}' could not be decoded: {exception.Message}",
                file: relativePath,
                expected: "Sword/Shield message table"));
            return [];
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Trade Pokemon lookup table '{relativePath}' could not be read: {exception.Message}",
                file: relativePath,
                expected: "Readable message table"));
            return [];
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Trade Pokemon lookup table '{relativePath}' could not be read: {exception.Message}",
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

    private static SwShTradePokemonProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShTradePokemonProvenance(entry.RelativePath, sourceLayer, entry.State);
    }

    private static SwShTradePokemonEditableField CreateField(
        string field,
        string label,
        string valueKind,
        int? minimumValue,
        int? maximumValue,
        IReadOnlyList<SwShTradePokemonEditableFieldOption>? options = null)
    {
        return new SwShTradePokemonEditableField(
            field,
            label,
            valueKind,
            minimumValue,
            maximumValue,
            options ?? Array.Empty<SwShTradePokemonEditableFieldOption>());
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.TradePokemon,
            "Trade Pokemon",
            "Scripted Trade Pokemon records, IV modes, items, moves, and source provenance.",
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
            Domain: TradePokemonEditDomain,
            Field: field,
            Expected: expected);
    }

    internal sealed record WorkflowFileSource(
        ProjectFileGraphEntry GraphEntry,
        string AbsolutePath);

    private sealed record TradeLookupTables(
        IReadOnlyList<string> SpeciesNames,
        IReadOnlySet<int> PresentSpeciesIds,
        IReadOnlyList<string> ItemNames,
        IReadOnlyList<string> MoveNames,
        IReadOnlySet<int> UsableMoveIds,
        SwShPokemonAbilityOptionResolver AbilityResolver,
        int SourceFileCount);
}
