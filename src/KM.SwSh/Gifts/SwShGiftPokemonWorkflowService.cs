// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Pokemon;
using KM.SwSh.Workflows;
using System.Globalization;

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
        new(3, "Genderless"),
    ];

    private static readonly IReadOnlyList<SwShGiftPokemonEditableFieldOption> ShinyLockOptions =
    [
        new(0, "Random"),
        new(1, "Never Shiny"),
        new(2, "Always Shiny"),
        new(3, "Star Shiny"),
        new(4, "Square Shiny"),
    ];

    private static readonly IReadOnlyList<SwShGiftPokemonEditableFieldOption> FlawlessIvCountOptions =
    [
        new(0, "Random IVs"),
        new(3, "3 Guaranteed Perfect IVs"),
        new(6, "6 Perfect IVs"),
    ];

    private static readonly IReadOnlyList<SwShGiftPokemonEditableFieldOption> NatureOptions =
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

    private static readonly IReadOnlyList<SwShGiftPokemonEditableField> BaseEditableFields =
    [
        CreateField(SpeciesField, "Species", "integer", 0, SwShGiftPokemonArchive.MaximumIdValue),
        CreateField(FormField, "Form", "integer", 0, SwShGiftPokemonArchive.MaximumByteValue),
        CreateField(LevelField, "Level", "integer", 0, SwShGiftPokemonArchive.MaximumByteValue),
        CreateField(HeldItemIdField, "Held item", "integer", 0, SwShGiftPokemonArchive.MaximumIdValue),
        CreateField(BallItemIdField, "Ball item", "integer", 0, SwShGiftPokemonArchive.MaximumIdValue),
        CreateField(AbilityField, "Ability slot", "integer", 0, 3, AbilityOptions),
        CreateField(NatureField, "Nature", "integer", 0, 25, NatureOptions),
        CreateField(GenderField, "Gender", "integer", 0, SwShGiftPokemonArchive.MaximumByteValue, GenderOptions),
        CreateField(ShinyLockField, "Shiny lock", "integer", 0, SwShGiftPokemonArchive.MaximumIdValue, ShinyLockOptions),
        CreateField(DynamaxLevelField, "Dynamax level", "integer", 0, SwShGiftPokemonArchive.MaximumByteValue),
        CreateField(CanGigantamaxField, "Can Gigantamax", "boolean", 0, 1, BooleanOptions),
        CreateField(SpecialMoveIdField, "Special move", "integer", 0, SwShGiftPokemonArchive.MaximumIdValue),
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

    internal static bool TryParseGiftRecordId(string? recordId, out int giftIndex)
    {
        giftIndex = 0;

        const string prefix = "gift:";
        return recordId is not null
            && recordId.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(recordId[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out giftIndex)
            && giftIndex >= 0;
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

        return !pathFromOutputRoot.StartsWith("..", StringComparison.Ordinal)
            && !Path.IsPathRooted(pathFromOutputRoot)
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
                gifts.Count(gift => gift.FlawlessIvCount is null),
                sourceFileCount),
            diagnostics);
    }

    private static GiftLookupTables CreateEmptyLookupTables()
    {
        return new GiftLookupTables([], [], [], SourceFileCount: 0);
    }

    private static IReadOnlyList<SwShGiftPokemonEditableField> CreateEditableFields(GiftLookupTables lookupTables)
    {
        var speciesOptions = CreateIndexedOptions(lookupTables.SpeciesNames, "Species");
        var itemOptions = CreateIndexedOptions(lookupTables.ItemNames, "Item");
        var moveOptions = CreateIndexedOptions(lookupTables.MoveNames, "Move");

        return BaseEditableFields
            .Select(field => field.Field switch
            {
                SpeciesField => field with { Options = speciesOptions },
                HeldItemIdField or BallItemIdField => field with { Options = itemOptions },
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
            GetOptionLabel(AbilityOptions, gift.Ability, "Ability slot"),
            gift.Nature,
            GetOptionLabel(NatureOptions, gift.Nature, "Nature"),
            gift.Gender,
            GetOptionLabel(GenderOptions, gift.Gender, "Gender"),
            gift.ShinyLock,
            GetOptionLabel(ShinyLockOptions, gift.ShinyLock, "Shiny lock"),
            gift.DynamaxLevel,
            gift.CanGigantamax,
            gift.SpecialMove,
            specialMove,
            ivs,
            flawlessIvCount,
            FormatIvSummary(ivs, flawlessIvCount),
            provenance);
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
            6 => "6 perfect IVs",
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

        return new GiftLookupTables(
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
                "Gift Pokemon lookup text is not available; numeric fallback labels will be shown.",
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
        IReadOnlyList<string> ItemNames,
        IReadOnlyList<string> MoveNames,
        int SourceFileCount);
}
