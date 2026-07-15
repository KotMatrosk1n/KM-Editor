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

namespace KM.SwSh.Gifts;

public sealed class SwShGiftPokemonWorkflowService
{
    public const string SpeciesField = "species";
    public const string FormField = "form";
    public const string LevelField = "level";
    public const string HeldItemIdField = "heldItemId";
    public const string BallItemIdField = "ballItemId";
    public const string AbilityField = "ability";
    public const string NatureField = "nature";
    public const string GenderField = "gender";
    public const string ShinyLockField = "shinyLock";
    public const string DynamaxLevelField = "dynamaxLevel";
    public const string CanGigantamaxField = "canGigantamax";
    public const string SpecialMoveIdField = "specialMoveId";
    public const string IvHpField = "ivHp";
    public const string IvAttackField = "ivAttack";
    public const string IvDefenseField = "ivDefense";
    public const string IvSpeedField = "ivSpeed";
    public const string IvSpecialAttackField = "ivSpecialAttack";
    public const string IvSpecialDefenseField = "ivSpecialDefense";
    public const string FlawlessIvCountField = "flawlessIvCount";
    public const string GiftPokemonDataPath = "romfs/bin/script_event_data/add_poke.bin";

    internal const string GiftPokemonEditDomain = "workflow.giftPokemon";

    private const string MessageRootPath = "romfs/bin/message";
    private const string PreferredLanguage = "English";

    private static readonly IReadOnlyList<SwShGiftPokemonEditableFieldOption> BooleanOptions =
    [
        new(0, "No"),
        new(1, "Yes"),
    ];

    private static readonly IReadOnlyList<SwShGiftPokemonEditableFieldOption> AbilityOptions =
    [
        new(0, "Default"),
        new(1, "Ability 1"),
        new(2, "Ability 2"),
        new(3, "Hidden Ability"),
    ];

    private static readonly IReadOnlyList<SwShGiftPokemonEditableFieldOption> GenderOptions =
    [
        new(0, "Random"),
        new(1, "Male"),
        new(2, "Female"),
    ];

    private static readonly IReadOnlyList<SwShGiftPokemonEditableFieldOption> ShinyLockOptions =
    [
        new(0, "Random"),
        new(1, "Always Shiny"),
        new(2, "Never Shiny"),
    ];

    private static readonly IReadOnlyList<SwShGiftPokemonEditableFieldOption> FlawlessIvCountOptions =
    [
        new(0, "Random IVs"),
        new(3, "3 Guaranteed Perfect IVs"),
        new(6, "6 Guaranteed Perfect IVs"),
    ];

    private static readonly IReadOnlyList<SwShGiftPokemonEditableFieldOption> FormOptions =
    [
    ];

    private static readonly IReadOnlyList<SwShGiftPokemonEditableFieldOption> DynamaxLevelOptions =
    [
        ..Enumerable.Range(0, 11).Select(value => new SwShGiftPokemonEditableFieldOption(
            value,
            value.ToString(CultureInfo.InvariantCulture))),
    ];

    private static readonly IReadOnlyList<SwShGiftPokemonEditableFieldOption> NatureOptions =
    [
        ..SwShNatureLabels.WithRandom.Select(nature => new SwShGiftPokemonEditableFieldOption(
            nature.Value,
            nature.Label)),
    ];

    private static readonly IReadOnlyList<SwShGiftPokemonEditableField> BaseEditableFields =
    [
        CreateField(SpeciesField, "Species", "integer", 1, SwShGiftPokemonArchive.MaximumIdValue),
        CreateField(FormField, "Form", "integer", 0, SwShGiftPokemonArchive.MaximumByteValue, FormOptions),
        CreateField(LevelField, "Level", "integer", SwShGiftPokemonArchive.MinimumLevel, SwShGiftPokemonArchive.MaximumLevel),
        CreateField(HeldItemIdField, "Held item", "integer", 0, SwShGiftPokemonArchive.MaximumIdValue),
        CreateField(BallItemIdField, "Ball item", "integer", 0, SwShGiftPokemonArchive.MaximumIdValue),
        CreateField(AbilityField, "Ability slot", "integer", 0, 3, AbilityOptions),
        CreateField(NatureField, "Nature", "integer", 0, 25, NatureOptions),
        CreateField(GenderField, "Gender", "integer", 0, 2, GenderOptions),
        CreateField(ShinyLockField, "Shiny lock", "integer", 0, 2, ShinyLockOptions),
        CreateField(DynamaxLevelField, "Dynamax level", "integer", 0, 10, DynamaxLevelOptions),
        CreateField(CanGigantamaxField, "Can Gigantamax", "boolean", 0, 1, BooleanOptions),
        CreateField(SpecialMoveIdField, "Special Move", "integer", 0, SwShGiftPokemonArchive.MaximumIdValue),
        CreateField(IvHpField, "HP IV", "integer", SwShGiftPokemonArchive.ThreePerfectIvSentinel, SwShGiftPokemonArchive.MaximumFixedIvValue),
        CreateField(IvAttackField, "Attack IV", "integer", SwShGiftPokemonArchive.RandomIvValue, SwShGiftPokemonArchive.MaximumFixedIvValue),
        CreateField(IvDefenseField, "Defense IV", "integer", SwShGiftPokemonArchive.RandomIvValue, SwShGiftPokemonArchive.MaximumFixedIvValue),
        CreateField(IvSpeedField, "Speed IV", "integer", SwShGiftPokemonArchive.RandomIvValue, SwShGiftPokemonArchive.MaximumFixedIvValue),
        CreateField(IvSpecialAttackField, "Sp. Atk IV", "integer", SwShGiftPokemonArchive.RandomIvValue, SwShGiftPokemonArchive.MaximumFixedIvValue),
        CreateField(IvSpecialDefenseField, "Sp. Def IV", "integer", SwShGiftPokemonArchive.RandomIvValue, SwShGiftPokemonArchive.MaximumFixedIvValue),
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
                    "Gift Pokemon requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShGiftPokemonWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);
        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(summary, [], sourceFileCount: 0, CreateEmptyLookupTables(), diagnostics);
        }

        var giftSource = ResolveGiftPokemonDataSource(project);
        if (giftSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Gift Pokemon data is not available for this project.",
                expected: GiftPokemonDataPath));
            return CreateWorkflow(summary, [], sourceFileCount: 0, CreateEmptyLookupTables(), diagnostics);
        }

        var lookupTables = LoadLookupTables(project, diagnostics);

        try
        {
            var archive = SwShGiftPokemonArchive.Parse(File.ReadAllBytes(giftSource.AbsolutePath));
            var provenance = CreateProvenance(giftSource.GraphEntry);
            var gifts = archive.Gifts
                .Select(gift => ToGiftEntry(gift, lookupTables, provenance))
                .ToArray();
            var sourceFileCount = 1 + lookupTables.SourceFileCount;

            return CreateWorkflow(summary, gifts, sourceFileCount, lookupTables, diagnostics);
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Gift Pokemon data source is not a supported Sword/Shield gift table: {exception.Message}",
                file: giftSource.GraphEntry.RelativePath,
                expected: "Sword/Shield gift Pokemon table"));
            return CreateWorkflow(summary, [], sourceFileCount: 1, lookupTables, diagnostics);
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Gift Pokemon data source could not be read: {exception.Message}",
                file: giftSource.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield gift Pokemon table"));
            return CreateWorkflow(summary, [], sourceFileCount: 1, lookupTables, diagnostics);
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Gift Pokemon data source could not be read: {exception.Message}",
                file: giftSource.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield gift Pokemon table"));
            return CreateWorkflow(summary, [], sourceFileCount: 1, lookupTables, diagnostics);
        }
    }

    internal static SwShGiftPokemonEditableField? GetEditableField(string? field)
    {
        return BaseEditableFields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, field, StringComparison.Ordinal));
    }

    internal static bool IsEditableField(string? field)
    {
        return GetEditableField(field) is not null;
    }

    internal static string CreateGiftRecordId(int giftIndex)
    {
        return $"gift:{giftIndex.ToString(CultureInfo.InvariantCulture)}";
    }

    internal static string CreateGiftRecordId(int giftIndex, string sourceIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceIdentity);

        return $"{CreateGiftRecordId(giftIndex)}:{sourceIdentity}";
    }

    internal static bool TryParseGiftRecordId(string? recordId, out int giftIndex)
    {
        return TryParseGiftRecordId(recordId, out giftIndex, out _);
    }

    internal static bool TryParseGiftRecordId(
        string? recordId,
        out int giftIndex,
        out string? sourceIdentity)
    {
        giftIndex = 0;
        sourceIdentity = null;

        const string prefix = "gift:";
        if (recordId is null || !recordId.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var remainder = recordId[prefix.Length..];
        var separator = remainder.IndexOf(':');
        var indexText = separator < 0 ? remainder : remainder[..separator];
        if (!int.TryParse(indexText, NumberStyles.None, CultureInfo.InvariantCulture, out giftIndex)
            || giftIndex < 0
            || !string.Equals(
                indexText,
                giftIndex.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal))
        {
            return false;
        }

        if (separator < 0)
        {
            return true;
        }

        sourceIdentity = remainder[(separator + 1)..];
        return sourceIdentity.Length == 64
            && sourceIdentity.All(Uri.IsHexDigit);
    }

    internal static string CreateSourceIdentity(SwShGiftPokemonRecord gift)
    {
        ArgumentNullException.ThrowIfNull(gift);

        var canonical = new StringBuilder();
        AppendIdentityValue(canonical, gift.Index);
        AppendIdentityValue(canonical, gift.IsEgg);
        AppendIdentityValue(canonical, gift.Form);
        AppendIdentityValue(canonical, gift.DynamaxLevel);
        AppendIdentityValue(canonical, gift.BallItemId);
        AppendIdentityValue(canonical, gift.Field04);
        AppendIdentityValue(canonical, gift.Hash1);
        AppendIdentityValue(canonical, gift.CanGigantamax ? 1 : 0);
        AppendIdentityValue(canonical, gift.HeldItem);
        AppendIdentityValue(canonical, gift.Level);
        AppendIdentityValue(canonical, gift.Species);
        AppendIdentityValue(canonical, gift.Field0A);
        AppendIdentityValue(canonical, gift.MemoryCode);
        AppendIdentityValue(canonical, gift.MemoryData);
        AppendIdentityValue(canonical, gift.MemoryFeel);
        AppendIdentityValue(canonical, gift.MemoryLevel);
        AppendIdentityValue(canonical, gift.OtNameId);
        AppendIdentityValue(canonical, gift.OtGender);
        AppendIdentityValue(canonical, gift.ShinyLock);
        AppendIdentityValue(canonical, gift.Nature);
        AppendIdentityValue(canonical, gift.Gender);
        AppendIdentityValue(canonical, gift.Ivs.Hp);
        AppendIdentityValue(canonical, gift.Ivs.Attack);
        AppendIdentityValue(canonical, gift.Ivs.Defense);
        AppendIdentityValue(canonical, gift.Ivs.Speed);
        AppendIdentityValue(canonical, gift.Ivs.SpecialAttack);
        AppendIdentityValue(canonical, gift.Ivs.SpecialDefense);
        AppendIdentityValue(canonical, gift.Ability);
        AppendIdentityValue(canonical, gift.SpecialMove);

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

    internal static WorkflowFileSource? ResolveGiftPokemonDataSource(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return ResolveWorkflowFile(project, GiftPokemonDataPath);
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

    private static SwShGiftPokemonWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShGiftPokemonEntry> gifts,
        int sourceFileCount,
        GiftLookupTables lookupTables,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SwShGiftPokemonWorkflow(
            summary,
            gifts,
            CreateEditableFields(lookupTables),
            new SwShGiftPokemonWorkflowStats(
                gifts.Count,
                gifts.Count(gift => gift.IsEgg),
                gifts.Count(gift => gift.FlawlessIvCount != 0),
                sourceFileCount),
            diagnostics)
        {
            AbilityResolver = lookupTables.AbilityResolver,
        };
    }

    private static GiftLookupTables CreateEmptyLookupTables()
    {
        return new GiftLookupTables([], new HashSet<int>(), [], [], new HashSet<int>(), SwShPokemonAbilityOptionResolver.Empty, SourceFileCount: 0);
    }

    private static IReadOnlyList<SwShGiftPokemonEditableField> CreateEditableFields(GiftLookupTables lookupTables)
    {
        var speciesOptions = SwShSpeciesAvailability.CreateSpeciesOptions(
            lookupTables.SpeciesNames,
            lookupTables.PresentSpeciesIds,
            (value, label) => new SwShGiftPokemonEditableFieldOption(value, label));
        var itemOptions = CreateIndexedOptions(lookupTables.ItemNames, "Item");
        var ballOptions = itemOptions
            .Where(option => SwShGiftPokemonArchive.IsValidBallItemId(option.Value))
            .ToArray();
        var moveOptions = SwShMoveAvailability.CreateMoveOptions(
            lookupTables.MoveNames,
            lookupTables.UsableMoveIds,
            (value, label) => new SwShGiftPokemonEditableFieldOption(value, label),
            includeNone: true);

        return BaseEditableFields
            .Select(field => field.Field switch
            {
                SpeciesField => field with { Options = speciesOptions },
                HeldItemIdField => field with { Options = itemOptions },
                BallItemIdField => field with { Options = ballOptions },
                SpecialMoveIdField => field with { Options = moveOptions },
                _ => field,
            })
            .ToArray();
    }

    private static IReadOnlyList<SwShGiftPokemonEditableFieldOption> CreateIndexedOptions(
        IReadOnlyList<string> names,
        string fallbackPrefix)
    {
        return names
            .Select((name, index) => new SwShGiftPokemonEditableFieldOption(
                index,
                string.IsNullOrWhiteSpace(name)
                    ? $"{index.ToString("000", CultureInfo.InvariantCulture)} {fallbackPrefix} {index}"
                    : $"{index.ToString("000", CultureInfo.InvariantCulture)} {name}"))
            .ToArray();
    }

    private static SwShGiftPokemonEntry ToGiftEntry(
        KM.Formats.SwSh.SwShGiftPokemonRecord gift,
        GiftLookupTables lookupTables,
        SwShGiftPokemonProvenance provenance)
    {
        var ivs = new SwShGiftPokemonIvsRecord(
            gift.Ivs.Hp,
            gift.Ivs.Attack,
            gift.Ivs.Defense,
            gift.Ivs.SpecialAttack,
            gift.Ivs.SpecialDefense,
            gift.Ivs.Speed);
        var flawlessIvCount = SwShGiftPokemonArchive.GetFlawlessIvCount(gift.Ivs);
        var species = GetIndexedName(gift.Species, lookupTables.SpeciesNames, "Species");
        var heldItem = gift.HeldItem == 0
            ? null
            : GetIndexedName(gift.HeldItem, lookupTables.ItemNames, "Item");
        var specialMove = gift.SpecialMove == 0
            ? null
            : GetIndexedName(gift.SpecialMove, lookupTables.MoveNames, "Move");

        return new SwShGiftPokemonEntry(
            gift.Index,
            FormatGiftLabel(gift.Index, species, gift.Species, gift.Form, gift.Level, gift.IsEgg != 0),
            gift.Species,
            species,
            gift.Form,
            gift.Level,
            gift.IsEgg != 0,
            gift.HeldItem,
            heldItem,
            gift.BallItemId,
            GetIndexedName(gift.BallItemId, lookupTables.ItemNames, "Item"),
            gift.Ability,
            GetAbilityOptionLabel(lookupTables, gift.Species, gift.Form, gift.Ability),
            gift.Nature,
            GetOptionLabel(NatureOptions, gift.Nature, "Nature"),
            gift.Gender,
            GetGenderOptionLabel(lookupTables.AbilityResolver, gift.Species, gift.Form, gift.Gender),
            gift.ShinyLock,
            GetOptionLabel(ShinyLockOptions, gift.ShinyLock, "Shiny lock"),
            gift.DynamaxLevel,
            gift.CanGigantamax,
            gift.SpecialMove,
            specialMove,
            ivs,
            flawlessIvCount,
            FormatIvSummary(ivs, flawlessIvCount),
            provenance)
        {
            AbilityOptions = CreateAbilityOptions(lookupTables, gift.Species, gift.Form),
            GenderOptions = CreateGenderOptions(lookupTables.AbilityResolver, gift.Species, gift.Form),
            SourceIdentity = CreateSourceIdentity(gift),
        };
    }

    internal static string FormatGiftLabel(int giftIndex, string species, int speciesId, int form, int level, bool isEgg)
    {
        var speciesLabel = SwShSpeciesFormLabels.FormatSpeciesFormLabel(species, speciesId, form);
        var eggSuffix = isEgg ? " Egg" : string.Empty;
        return $"Gift {(giftIndex + 1).ToString("000", CultureInfo.InvariantCulture)}: {speciesLabel}{eggSuffix} Lv. {level}";
    }

    internal static string FormatIvSummary(SwShGiftPokemonIvsRecord ivs, int? flawlessIvCount)
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
        IReadOnlyList<SwShGiftPokemonEditableFieldOption> options,
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

    private static GiftLookupTables LoadLookupTables(
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

        return new GiftLookupTables(
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

    private static IReadOnlyList<SwShGiftPokemonEditableFieldOption> CreateAbilityOptions(
        GiftLookupTables lookupTables,
        int speciesId,
        int form)
    {
        return CreateAbilityOptions(lookupTables.AbilityResolver, speciesId, form);
    }

    internal static IReadOnlyList<SwShGiftPokemonEditableFieldOption> CreateAbilityOptions(
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
            .CreateOptions(speciesId, form, SwShAbilityOptionMode.DefaultPlusSlots)
            .Where(option => option.Value switch
            {
                0 or 1 => personal.Ability1 != 0,
                2 => personal.Ability2 != 0,
                3 => personal.HiddenAbility != 0,
                _ => false,
            })
            .Select(option => new SwShGiftPokemonEditableFieldOption(option.Value, option.Label))
            .ToArray();
    }

    internal static IReadOnlyList<SwShGiftPokemonEditableFieldOption> CreateGenderOptions(
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

    internal static string GetGenderOptionLabel(
        SwShPokemonAbilityOptionResolver abilityResolver,
        int speciesId,
        int form,
        int value)
    {
        return GetOptionLabel(
            CreateGenderOptions(abilityResolver, speciesId, form),
            value,
            "Gender");
    }

    private static string GetAbilityOptionLabel(
        GiftLookupTables lookupTables,
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
                "Gift Pokemon lookup text is not available; numeric fallback labels will be shown.",
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
                $"English Gift Pokemon lookup text was not found; using '{language}' lookup tables instead.",
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
                $"Gift Pokemon lookup table '{relativePath}' could not be decoded: {exception.Message}",
                file: relativePath,
                expected: "Sword/Shield message table"));
            return [];
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Gift Pokemon lookup table '{relativePath}' could not be read: {exception.Message}",
                file: relativePath,
                expected: "Readable message table"));
            return [];
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Gift Pokemon lookup table '{relativePath}' could not be read: {exception.Message}",
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

    private static SwShGiftPokemonProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShGiftPokemonProvenance(entry.RelativePath, sourceLayer, entry.State);
    }

    private static SwShGiftPokemonEditableField CreateField(
        string field,
        string label,
        string valueKind,
        int? minimumValue,
        int? maximumValue,
        IReadOnlyList<SwShGiftPokemonEditableFieldOption>? options = null)
    {
        return new SwShGiftPokemonEditableField(
            field,
            label,
            valueKind,
            minimumValue,
            maximumValue,
            options ?? Array.Empty<SwShGiftPokemonEditableFieldOption>());
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.GiftPokemon,
            "Gift Pokemon",
            "Scripted gift Pokemon records, IV modes, items, moves, and source provenance.",
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
            Domain: GiftPokemonEditDomain,
            Field: field,
            Expected: expected);
    }

    internal sealed record WorkflowFileSource(
        ProjectFileGraphEntry GraphEntry,
        string AbsolutePath);

    private sealed record GiftLookupTables(
        IReadOnlyList<string> SpeciesNames,
        IReadOnlySet<int> PresentSpeciesIds,
        IReadOnlyList<string> ItemNames,
        IReadOnlyList<string> MoveNames,
        IReadOnlySet<int> UsableMoveIds,
        SwShPokemonAbilityOptionResolver AbilityResolver,
        int SourceFileCount);
}
