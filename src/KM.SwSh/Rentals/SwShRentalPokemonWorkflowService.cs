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
using System.Security.Cryptography;
using System.Text;

namespace KM.SwSh.Rentals;

public sealed class SwShRentalPokemonWorkflowService
{
    public const int MaximumPokemonEvValue = 252;

    public const string SpeciesField = "species";
    public const string FormField = "form";
    public const string LevelField = "level";
    public const string HeldItemIdField = "heldItemId";
    public const string BallItemIdField = "ballItemId";
    public const string AbilityField = "ability";
    public const string NatureField = "nature";
    public const string GenderField = "gender";
    public const string TrainerIdField = "trainerId";
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
    public const string FixedIvPresetField = "fixedIvPreset";
    public const string RentalPokemonDataPath = "romfs/bin/script_event_data/rental.bin";

    internal const string RentalPokemonEditDomain = "workflow.rentalPokemon";

    private const string MessageRootPath = "romfs/bin/message";
    private const string PreferredLanguage = "English";

    private static readonly IReadOnlyList<SwShRentalPokemonEditableFieldOption> AbilityOptions =
    [
        new(0, "Ability 1"),
        new(1, "Ability 2"),
        new(2, "Hidden Ability"),
    ];

    private static readonly IReadOnlyList<SwShRentalPokemonEditableFieldOption> GenderOptions =
    [
        new(0, "Random"),
        new(1, "Male"),
        new(2, "Female"),
    ];

    private static readonly IReadOnlyList<SwShRentalPokemonEditableFieldOption> FixedIvPresetOptions =
    [
        new(0, "0 IVs"),
        new(31, "6 Guaranteed Perfect IVs"),
    ];

    private static readonly IReadOnlyList<SwShRentalPokemonEditableFieldOption> FormOptions =
    [
    ];

    private static readonly IReadOnlyList<SwShRentalPokemonEditableFieldOption> NatureOptions =
    [
        ..SwShNatureLabels.Fixed.Select(nature => new SwShRentalPokemonEditableFieldOption(
            nature.Value,
            nature.Label)),
    ];

    private static readonly IReadOnlyList<SwShRentalPokemonEditableField> BaseEditableFields =
    [
        CreateField(SpeciesField, "Species", "integer", 1, SwShRentalPokemonArchive.MaximumIdValue),
        CreateField(FormField, "Form", "integer", 0, SwShRentalPokemonArchive.MaximumByteValue, FormOptions),
        CreateField(
            LevelField,
            "Level",
            "integer",
            SwShRentalPokemonArchive.MinimumPokemonLevel,
            SwShRentalPokemonArchive.MaximumPokemonLevel),
        CreateField(HeldItemIdField, "Held item", "integer", 0, SwShRentalPokemonArchive.MaximumIdValue),
        CreateField(BallItemIdField, "Ball item", "integer", 0, SwShRentalPokemonArchive.MaximumIdValue),
        CreateField(AbilityField, "Ability slot", "integer", 0, 2, AbilityOptions),
        CreateField(NatureField, "Nature", "integer", 0, 24, NatureOptions),
        CreateField(GenderField, "Gender", "integer", 0, 2, GenderOptions),
        CreateField(TrainerIdField, "Trainer ID", "integer", 0, uint.MaxValue),
        CreateField(Move0Field, "Move 1", "integer", 0, SwShRentalPokemonArchive.MaximumIdValue),
        CreateField(Move1Field, "Move 2", "integer", 0, SwShRentalPokemonArchive.MaximumIdValue),
        CreateField(Move2Field, "Move 3", "integer", 0, SwShRentalPokemonArchive.MaximumIdValue),
        CreateField(Move3Field, "Move 4", "integer", 0, SwShRentalPokemonArchive.MaximumIdValue),
        CreateField(EvHpField, "HP EV", "integer", 0, MaximumPokemonEvValue),
        CreateField(EvAttackField, "Attack EV", "integer", 0, MaximumPokemonEvValue),
        CreateField(EvDefenseField, "Defense EV", "integer", 0, MaximumPokemonEvValue),
        CreateField(EvSpecialAttackField, "Sp. Atk EV", "integer", 0, MaximumPokemonEvValue),
        CreateField(EvSpecialDefenseField, "Sp. Def EV", "integer", 0, MaximumPokemonEvValue),
        CreateField(EvSpeedField, "Speed EV", "integer", 0, MaximumPokemonEvValue),
        CreateField(IvHpField, "HP IV", "integer", SwShRentalPokemonArchive.MinimumFixedIvValue, SwShRentalPokemonArchive.MaximumFixedIvValue),
        CreateField(IvAttackField, "Attack IV", "integer", SwShRentalPokemonArchive.MinimumFixedIvValue, SwShRentalPokemonArchive.MaximumFixedIvValue),
        CreateField(IvDefenseField, "Defense IV", "integer", SwShRentalPokemonArchive.MinimumFixedIvValue, SwShRentalPokemonArchive.MaximumFixedIvValue),
        CreateField(IvSpeedField, "Speed IV", "integer", SwShRentalPokemonArchive.MinimumFixedIvValue, SwShRentalPokemonArchive.MaximumFixedIvValue),
        CreateField(IvSpecialAttackField, "Sp. Atk IV", "integer", SwShRentalPokemonArchive.MinimumFixedIvValue, SwShRentalPokemonArchive.MaximumFixedIvValue),
        CreateField(IvSpecialDefenseField, "Sp. Def IV", "integer", SwShRentalPokemonArchive.MinimumFixedIvValue, SwShRentalPokemonArchive.MaximumFixedIvValue),
        CreateField(FixedIvPresetField, "IV preset", "integer", SwShRentalPokemonArchive.MinimumFixedIvValue, SwShRentalPokemonArchive.MaximumFixedIvValue, FixedIvPresetOptions),
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
                    "Rental Pokemon requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShRentalPokemonWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);
        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(summary, [], sourceFileCount: 0, CreateEmptyLookupTables(), diagnostics);
        }

        var source = ResolveRentalPokemonDataSource(project);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Rental Pokemon data is not available for this project.",
                expected: RentalPokemonDataPath));
            return CreateWorkflow(summary, [], sourceFileCount: 0, CreateEmptyLookupTables(), diagnostics);
        }

        var lookupTables = LoadLookupTables(project, diagnostics);

        try
        {
            var archive = SwShRentalPokemonArchive.Parse(File.ReadAllBytes(source.AbsolutePath));
            var provenance = CreateProvenance(source.GraphEntry);
            var rentals = archive.Rentals
                .Select(rental => ToRentalEntry(rental, lookupTables, provenance))
                .ToArray();
            var sourceFileCount = 1 + lookupTables.SourceFileCount;

            return CreateWorkflow(summary, rentals, sourceFileCount, lookupTables, diagnostics);
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Rental Pokemon data source is not a supported Sword/Shield rental table: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Sword/Shield Rental Pokemon table"));
            return CreateWorkflow(summary, [], sourceFileCount: 1, lookupTables, diagnostics);
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Rental Pokemon data source could not be read: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield Rental Pokemon table"));
            return CreateWorkflow(summary, [], sourceFileCount: 1, lookupTables, diagnostics);
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Rental Pokemon data source could not be read: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield Rental Pokemon table"));
            return CreateWorkflow(summary, [], sourceFileCount: 1, lookupTables, diagnostics);
        }
    }

    internal static SwShRentalPokemonEditableField? GetEditableField(string? field)
    {
        return BaseEditableFields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, field, StringComparison.Ordinal));
    }

    internal static bool IsEditableField(string? field)
    {
        return GetEditableField(field) is not null;
    }

    internal static string CreateRentalRecordId(int rentalIndex)
    {
        return $"rental:{rentalIndex.ToString(CultureInfo.InvariantCulture)}";
    }

    internal static string CreateRentalRecordId(int rentalIndex, string sourceIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceIdentity);

        return $"{CreateRentalRecordId(rentalIndex)}:{sourceIdentity}";
    }

    internal static bool TryParseRentalRecordId(string? recordId, out int rentalIndex)
    {
        return TryParseRentalRecordId(recordId, out rentalIndex, out _);
    }

    internal static bool TryParseRentalRecordId(
        string? recordId,
        out int rentalIndex,
        out string? sourceIdentity)
    {
        rentalIndex = 0;
        sourceIdentity = null;

        const string prefix = "rental:";
        if (recordId is null || !recordId.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var remainder = recordId[prefix.Length..];
        var separator = remainder.IndexOf(':');
        var indexText = separator < 0 ? remainder : remainder[..separator];
        if (!int.TryParse(indexText, NumberStyles.None, CultureInfo.InvariantCulture, out rentalIndex)
            || rentalIndex < 0
            || !string.Equals(
                indexText,
                rentalIndex.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal))
        {
            return false;
        }

        if (separator < 0)
        {
            return true;
        }

        sourceIdentity = remainder[(separator + 1)..];
        return sourceIdentity.Length == 64 && sourceIdentity.All(Uri.IsHexDigit);
    }

    internal static string CreateSourceIdentity(SwShRentalPokemonRecord rental)
    {
        ArgumentNullException.ThrowIfNull(rental);

        var canonical = new StringBuilder();
        AppendIdentityValue(canonical, rental.Index);
        AppendIdentityValue(canonical, rental.Evs.HP);
        AppendIdentityValue(canonical, rental.Evs.Attack);
        AppendIdentityValue(canonical, rental.Evs.Defense);
        AppendIdentityValue(canonical, rental.Evs.Speed);
        AppendIdentityValue(canonical, rental.Evs.SpecialAttack);
        AppendIdentityValue(canonical, rental.Evs.SpecialDefense);
        AppendIdentityValue(canonical, rental.Form);
        AppendIdentityValue(canonical, rental.BallItemId);
        AppendIdentityValue(canonical, rental.Hash1);
        AppendIdentityValue(canonical, rental.HeldItem);
        AppendIdentityValue(canonical, rental.Level);
        AppendIdentityValue(canonical, rental.Species);
        AppendIdentityValue(canonical, rental.Hash2);
        AppendIdentityValue(canonical, rental.TrainerId);
        AppendIdentityValue(canonical, rental.Nature);
        AppendIdentityValue(canonical, rental.Gender);
        AppendIdentityValue(canonical, rental.Ivs.HP);
        AppendIdentityValue(canonical, rental.Ivs.Attack);
        AppendIdentityValue(canonical, rental.Ivs.Defense);
        AppendIdentityValue(canonical, rental.Ivs.Speed);
        AppendIdentityValue(canonical, rental.Ivs.SpecialAttack);
        AppendIdentityValue(canonical, rental.Ivs.SpecialDefense);
        AppendIdentityValue(canonical, rental.Ability);
        foreach (var move in rental.Moves)
        {
            AppendIdentityValue(canonical, move);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static void AppendIdentityValue<T>(StringBuilder destination, T value)
        where T : IFormattable
    {
        var text = value.ToString(null, CultureInfo.InvariantCulture);
        destination.Append(text.Length);
        destination.Append(':');
        destination.Append(text);
        destination.Append('|');
    }

    internal static WorkflowFileSource? ResolveRentalPokemonDataSource(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return ResolveWorkflowFile(project, RentalPokemonDataPath);
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

    private static SwShRentalPokemonWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShRentalPokemonEntry> rentals,
        int sourceFileCount,
        RentalLookupTables lookupTables,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SwShRentalPokemonWorkflow(
            summary,
            rentals,
            CreateEditableFields(lookupTables),
            new SwShRentalPokemonWorkflowStats(
                rentals.Count,
                rentals.Count(rental => rental.HasPerfectIvs),
                sourceFileCount),
            diagnostics)
        {
            AbilityResolver = lookupTables.AbilityResolver,
            HasItemSemanticData = lookupTables.ItemSemanticData.HasSemanticData,
            ValidHeldItemIds = lookupTables.ItemSemanticData.ValidItemIds,
            ItemSemanticSource = lookupTables.ItemSemanticData.Source,
            HasMoveSemanticData = lookupTables.MoveAvailability.HasSemanticData,
            UsableMoveSources = lookupTables.MoveAvailability.UsableMoveSources,
        };
    }

    private static RentalLookupTables CreateEmptyLookupTables()
    {
        return new RentalLookupTables(
            [],
            new HashSet<int>(),
            [],
            [],
            ItemSemanticData.Empty,
            new SwShMoveAvailabilityResult(
                new HashSet<int>(),
                new Dictionary<int, ProjectFileReference>(),
                HasSemanticData: false),
            SwShPokemonAbilityOptionResolver.Empty,
            SourceFileCount: 0);
    }

    private static IReadOnlyList<SwShRentalPokemonEditableField> CreateEditableFields(RentalLookupTables lookupTables)
    {
        var speciesOptions = SwShSpeciesAvailability.CreateSpeciesOptions(
            lookupTables.SpeciesNames,
            lookupTables.PresentSpeciesIds,
            (value, label) => new SwShRentalPokemonEditableFieldOption(value, label));
        var heldItemOptions = CreateIndexedOptions(
            lookupTables.ItemSemanticData.ValidItemIds.Append(0),
            lookupTables.ItemNames,
            "Item");
        var ballOptions = CreateIndexedOptions(
            SwShRentalPokemonArchive.ValidBallItemIds,
            lookupTables.ItemNames,
            "Item");
        IReadOnlyList<SwShRentalPokemonEditableFieldOption> moveOptions =
            lookupTables.MoveAvailability.UsableMoveIds.Count > 0
                ? SwShMoveAvailability.CreateMoveOptions(
                    lookupTables.MoveNames,
                    lookupTables.MoveAvailability.UsableMoveIds,
                    (value, label) => new SwShRentalPokemonEditableFieldOption(value, label),
                    includeNone: true)
                : [new SwShRentalPokemonEditableFieldOption(0, "000 None")];

        return BaseEditableFields
            .Select(field => field.Field switch
            {
                SpeciesField => field with { Options = speciesOptions },
                HeldItemIdField => field with { Options = heldItemOptions },
                BallItemIdField => field with { Options = ballOptions },
                Move0Field or Move1Field or Move2Field or Move3Field => field with { Options = moveOptions },
                _ => field,
            })
            .ToArray();
    }

    private static IReadOnlyList<SwShRentalPokemonEditableFieldOption> CreateIndexedOptions(
        IEnumerable<int> ids,
        IReadOnlyList<string> names,
        string fallbackPrefix)
    {
        return ids
            .Where(id => id >= 0)
            .Distinct()
            .Order()
            .Select(id => new SwShRentalPokemonEditableFieldOption(
                id,
                $"{id.ToString("000", CultureInfo.InvariantCulture)} {GetIndexedName(id, names, fallbackPrefix)}"))
            .ToArray();
    }

    private static SwShRentalPokemonEntry ToRentalEntry(
        SwShRentalPokemonRecord rental,
        RentalLookupTables lookupTables,
        SwShRentalPokemonProvenance provenance)
    {
        var evs = new SwShRentalPokemonStatsRecord(
            rental.Evs.HP,
            rental.Evs.Attack,
            rental.Evs.Defense,
            rental.Evs.SpecialAttack,
            rental.Evs.SpecialDefense,
            rental.Evs.Speed);
        var ivs = new SwShRentalPokemonStatsRecord(
            rental.Ivs.HP,
            rental.Ivs.Attack,
            rental.Ivs.Defense,
            rental.Ivs.SpecialAttack,
            rental.Ivs.SpecialDefense,
            rental.Ivs.Speed);
        var species = GetIndexedName(rental.Species, lookupTables.SpeciesNames, "Species");
        var heldItem = rental.HeldItem == 0
            ? null
            : GetIndexedName(rental.HeldItem, lookupTables.ItemNames, "Item");
        var moves = rental.Moves
            .Select((moveId, slot) => new SwShRentalPokemonMoveRecord(
                slot,
                moveId,
                moveId == 0 ? null : GetIndexedName(moveId, lookupTables.MoveNames, "Move")))
            .ToArray();
        var hasPerfectIvs = SwShRentalPokemonArchive.HasPerfectIvs(rental.Ivs);

        return new SwShRentalPokemonEntry(
            rental.Index,
            FormatRentalLabel(rental.Index, species, rental.Species, rental.Form, rental.Level, moves),
            rental.Species,
            species,
            rental.Form,
            rental.Level,
            rental.HeldItem,
            heldItem,
            rental.BallItemId,
            GetIndexedName(rental.BallItemId, lookupTables.ItemNames, "Item"),
            rental.Ability,
            GetAbilityOptionLabel(lookupTables, rental.Species, rental.Form, rental.Ability),
            rental.Nature,
            GetOptionLabel(NatureOptions, rental.Nature, "Nature"),
            rental.Gender,
            GetOptionLabel(
                CreateGenderOptions(lookupTables.AbilityResolver, rental.Species, rental.Form),
                rental.Gender,
                "Gender"),
            rental.TrainerId,
            FormatHash(rental.Hash1),
            FormatHash(rental.Hash2),
            moves,
            evs,
            ivs,
            hasPerfectIvs,
            FormatIvSummary(ivs),
            provenance)
        {
            AbilityOptions = CreateAbilityOptions(lookupTables, rental.Species, rental.Form),
            GenderOptions = CreateGenderOptions(lookupTables.AbilityResolver, rental.Species, rental.Form),
            SourceIdentity = CreateSourceIdentity(rental),
        };
    }

    internal static string FormatRentalLabel(
        int rentalIndex,
        string species,
        int speciesId,
        int form,
        int level,
        IReadOnlyList<SwShRentalPokemonMoveRecord> moves)
    {
        var speciesLabel = SwShSpeciesFormLabels.FormatSpeciesFormLabel(species, speciesId, form);
        var moveSummary = string.Join(", ", moves
            .Where(move => !string.IsNullOrWhiteSpace(move.Move))
            .Take(2)
            .Select(move => move.Move));
        var suffix = string.IsNullOrWhiteSpace(moveSummary) ? string.Empty : $" | {moveSummary}";

        return $"Rental {(rentalIndex + 1).ToString("000", CultureInfo.InvariantCulture)}: {speciesLabel} Lv. {level}{suffix}";
    }

    internal static string FormatIvSummary(SwShRentalPokemonStatsRecord ivs)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"HP {ivs.HP} / Atk {ivs.Attack} / Def {ivs.Defense} / SpA {ivs.SpecialAttack} / SpD {ivs.SpecialDefense} / Spe {ivs.Speed}");
    }

    internal static string GetOptionLabel(
        IReadOnlyList<SwShRentalPokemonEditableFieldOption> options,
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

    private static RentalLookupTables LoadLookupTables(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var messageRoot = ResolveLanguageMessageRoot(project, diagnostics);
        var speciesNames = LoadMessageTable(project, messageRoot, "monsname.dat", diagnostics);
        var itemNames = LoadMessageTable(project, messageRoot, "itemname.dat", diagnostics);
        var moveNames = LoadMessageTable(project, messageRoot, "wazaname.dat", diagnostics);
        var itemDisplayNames = SwShItemsWorkflowService.CreateItemDisplayNames(project, itemNames, moveNames);
        var presentSpeciesIds = SwShSpeciesAvailability.LoadPresentSpeciesIds(project);
        var itemSemanticData = LoadItemSemanticData(project);
        var moveAvailability = SwShMoveAvailability.Load(project);
        var abilityResolver = SwShPokemonAbilityOptionResolver.Load(project);

        return new RentalLookupTables(
            speciesNames,
            presentSpeciesIds,
            itemDisplayNames,
            moveNames,
            itemSemanticData,
            moveAvailability,
            abilityResolver,
            CountSource(speciesNames)
                + CountSource(itemNames)
                + CountSource(moveNames)
                + (presentSpeciesIds.Count > 0 ? 1 : 0)
                + (itemSemanticData.HasSemanticData ? 1 : 0)
                + (moveAvailability.HasSemanticData ? 1 : 0));
    }

    private static ItemSemanticData LoadItemSemanticData(OpenedProject project)
    {
        var source = SwShItemsWorkflowService.ResolveItemDataSource(project);
        if (source is null)
        {
            return ItemSemanticData.Empty;
        }

        try
        {
            var itemIds = SwShItemTable.Parse(File.ReadAllBytes(source.AbsolutePath))
                .Records
                .Select(record => record.ItemId)
                .Where(itemId => itemId >= 0)
                .ToHashSet();
            if (itemIds.Count == 0)
            {
                return ItemSemanticData.Empty;
            }

            return new ItemSemanticData(
                itemIds,
                new ProjectFileReference(
                    source.GraphEntry.LayeredFile is not null
                        ? ProjectFileLayer.Layered
                        : ProjectFileLayer.Base,
                    source.GraphEntry.RelativePath),
                HasSemanticData: true);
        }
        catch (Exception exception) when (exception is InvalidDataException
            or IOException
            or UnauthorizedAccessException
            or OverflowException)
        {
            return ItemSemanticData.Empty;
        }
    }

    private static IReadOnlyList<SwShRentalPokemonEditableFieldOption> CreateAbilityOptions(
        RentalLookupTables lookupTables,
        int speciesId,
        int form)
    {
        return CreateAbilityOptions(lookupTables.AbilityResolver, speciesId, form);
    }

    internal static IReadOnlyList<SwShRentalPokemonEditableFieldOption> CreateAbilityOptions(
        SwShPokemonAbilityOptionResolver abilityResolver,
        int speciesId,
        int form)
    {
        var personal = abilityResolver.ResolvePersonalRecord(speciesId, form);
        if (personal is null)
        {
            return AbilityOptions;
        }

        return abilityResolver
            .CreateOptions(speciesId, form, SwShAbilityOptionMode.ZeroBasedSlots)
            .Where(option => option.Value switch
            {
                0 => personal.Ability1 != 0,
                1 => personal.Ability2 != 0,
                2 => personal.HiddenAbility != 0,
                _ => false,
            })
            .Select(option => new SwShRentalPokemonEditableFieldOption(option.Value, option.Label))
            .ToArray();
    }

    internal static IReadOnlyList<SwShRentalPokemonEditableFieldOption> CreateGenderOptions(
        SwShPokemonAbilityOptionResolver abilityResolver,
        int speciesId,
        int form)
    {
        var personal = abilityResolver.ResolvePersonalRecord(speciesId, form);
        if (personal is null)
        {
            return GenderOptions;
        }

        return personal.GenderRatio switch
        {
            255 =>
            [
                new(0, "Random"),
                new(2, "Genderless"),
            ],
            0 =>
            [
                new(0, "Random"),
                new(1, "Male"),
            ],
            254 =>
            [
                new(0, "Random"),
                new(2, "Female"),
            ],
            _ => GenderOptions,
        };
    }

    private static string GetAbilityOptionLabel(
        RentalLookupTables lookupTables,
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
                "Rental Pokemon lookup text is not available; numeric fallback labels will be shown.",
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
                $"English Rental Pokemon lookup text was not found; using '{language}' lookup tables instead.",
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
                $"Rental Pokemon lookup table '{relativePath}' could not be decoded: {exception.Message}",
                file: relativePath,
                expected: "Sword/Shield message table"));
            return [];
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Rental Pokemon lookup table '{relativePath}' could not be read: {exception.Message}",
                file: relativePath,
                expected: "Readable message table"));
            return [];
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Rental Pokemon lookup table '{relativePath}' could not be read: {exception.Message}",
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

    private static SwShRentalPokemonProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShRentalPokemonProvenance(entry.RelativePath, sourceLayer, entry.State);
    }

    private static string FormatHash(ulong value)
    {
        return string.Create(CultureInfo.InvariantCulture, $"0x{value:X16}");
    }

    private static SwShRentalPokemonEditableField CreateField(
        string field,
        string label,
        string valueKind,
        long? minimumValue,
        long? maximumValue,
        IReadOnlyList<SwShRentalPokemonEditableFieldOption>? options = null)
    {
        return new SwShRentalPokemonEditableField(
            field,
            label,
            valueKind,
            minimumValue,
            maximumValue,
            options ?? Array.Empty<SwShRentalPokemonEditableFieldOption>());
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.RentalPokemon,
            "Rental Pokemon",
            "Rental Pokemon records, fixed IVs, EVs, items, moves, and source provenance.",
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
            Domain: RentalPokemonEditDomain,
            Field: field,
            Expected: expected);
    }

    internal sealed record WorkflowFileSource(
        ProjectFileGraphEntry GraphEntry,
        string AbsolutePath);

    private sealed record RentalLookupTables(
        IReadOnlyList<string> SpeciesNames,
        IReadOnlySet<int> PresentSpeciesIds,
        IReadOnlyList<string> ItemNames,
        IReadOnlyList<string> MoveNames,
        ItemSemanticData ItemSemanticData,
        SwShMoveAvailabilityResult MoveAvailability,
        SwShPokemonAbilityOptionResolver AbilityResolver,
        int SourceFileCount);

    private sealed record ItemSemanticData(
        IReadOnlySet<int> ValidItemIds,
        ProjectFileReference? Source,
        bool HasSemanticData)
    {
        public static ItemSemanticData Empty { get; } =
            new(new HashSet<int>(), Source: null, HasSemanticData: false);
    }
}
