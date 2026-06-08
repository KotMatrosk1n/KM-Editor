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

    public const string HPField = "hp";
    public const string AttackField = "attack";
    public const string DefenseField = "defense";
    public const string SpecialAttackField = "specialAttack";
    public const string SpecialDefenseField = "specialDefense";
    public const string SpeedField = "speed";
    public const string Type1Field = "type1";
    public const string Type2Field = "type2";
    public const string CatchRateField = "catchRate";
    public const string EvolutionStageField = "evolutionStage";
    public const string EVYieldHPField = "evYieldHP";
    public const string EVYieldAttackField = "evYieldAttack";
    public const string EVYieldDefenseField = "evYieldDefense";
    public const string EVYieldSpecialAttackField = "evYieldSpecialAttack";
    public const string EVYieldSpecialDefenseField = "evYieldSpecialDefense";
    public const string EVYieldSpeedField = "evYieldSpeed";
    public const string HeldItem1Field = "heldItem1";
    public const string HeldItem2Field = "heldItem2";
    public const string HeldItem3Field = "heldItem3";
    public const string GenderRatioField = "genderRatio";
    public const string HatchCyclesField = "hatchCycles";
    public const string BaseFriendshipField = "baseFriendship";
    public const string ExpGrowthField = "expGrowth";
    public const string EggGroup1Field = "eggGroup1";
    public const string EggGroup2Field = "eggGroup2";
    public const string Ability1Field = "ability1";
    public const string Ability2Field = "ability2";
    public const string HiddenAbilityField = "hiddenAbility";
    public const string FormStatsIndexField = "formStatsIndex";
    public const string FormCountField = "formCount";
    public const string ColorField = "color";
    public const string IsPresentInGameField = "isPresentInGame";
    public const string HasSpriteFormField = "hasSpriteForm";
    public const string BaseExperienceField = "baseExperience";
    public const string HeightField = "height";
    public const string WeightField = "weight";
    public const string ModelIdField = "modelId";
    public const string HatchedSpeciesField = "hatchedSpecies";
    public const string LocalFormIndexField = "localFormIndex";
    public const string IsRegionalFormField = "isRegionalForm";
    public const string CanNotDynamaxField = "canNotDynamax";
    public const string RegionalDexIndexField = "regionalDexIndex";
    public const string FormField = "form";
    public const string ArmorDexIndexField = "armorDexIndex";
    public const string CrownDexIndexField = "crownDexIndex";

    private static readonly IReadOnlyList<SwShPokemonEditableFieldOption> TypeOptions =
    [
        CreateOption(0, "Normal"),
        CreateOption(1, "Fighting"),
        CreateOption(2, "Flying"),
        CreateOption(3, "Poison"),
        CreateOption(4, "Ground"),
        CreateOption(5, "Rock"),
        CreateOption(6, "Bug"),
        CreateOption(7, "Ghost"),
        CreateOption(8, "Steel"),
        CreateOption(9, "Fire"),
        CreateOption(10, "Water"),
        CreateOption(11, "Grass"),
        CreateOption(12, "Electric"),
        CreateOption(13, "Psychic"),
        CreateOption(14, "Ice"),
        CreateOption(15, "Dragon"),
        CreateOption(16, "Dark"),
        CreateOption(17, "Fairy"),
    ];

    private static readonly IReadOnlyList<SwShPokemonEditableFieldOption> EggGroupOptions =
    [
        CreateOption(0, "None"),
        CreateOption(1, "Monster"),
        CreateOption(2, "Water 1"),
        CreateOption(3, "Bug"),
        CreateOption(4, "Flying"),
        CreateOption(5, "Field"),
        CreateOption(6, "Fairy"),
        CreateOption(7, "Grass"),
        CreateOption(8, "Human-Like"),
        CreateOption(9, "Water 3"),
        CreateOption(10, "Mineral"),
        CreateOption(11, "Amorphous"),
        CreateOption(12, "Water 2"),
        CreateOption(13, "Ditto"),
        CreateOption(14, "Dragon"),
        CreateOption(15, "Undiscovered"),
    ];

    private static readonly IReadOnlyList<SwShPokemonEditableFieldOption> ExpGrowthOptions =
    [
        CreateOption(0, "Medium Fast"),
        CreateOption(1, "Erratic"),
        CreateOption(2, "Fluctuating"),
        CreateOption(3, "Medium Slow"),
        CreateOption(4, "Fast"),
        CreateOption(5, "Slow"),
    ];

    private static readonly IReadOnlyList<SwShPokemonEditableFieldOption> ColorOptions =
    [
        CreateOption(0, "Red"),
        CreateOption(1, "Blue"),
        CreateOption(2, "Yellow"),
        CreateOption(3, "Green"),
        CreateOption(4, "Black"),
        CreateOption(5, "Brown"),
        CreateOption(6, "Purple"),
        CreateOption(7, "Gray"),
        CreateOption(8, "White"),
        CreateOption(9, "Pink"),
    ];

    public static readonly IReadOnlyList<SwShPokemonEditableField> EditableFields =
    [
        CreateField(HPField, "HP", "Base Stats", 0, byte.MaxValue),
        CreateField(AttackField, "Attack", "Base Stats", 0, byte.MaxValue),
        CreateField(DefenseField, "Defense", "Base Stats", 0, byte.MaxValue),
        CreateField(SpecialAttackField, "Sp. Atk", "Base Stats", 0, byte.MaxValue),
        CreateField(SpecialDefenseField, "Sp. Def", "Base Stats", 0, byte.MaxValue),
        CreateField(SpeedField, "Speed", "Base Stats", 0, byte.MaxValue),
        CreateField(EVYieldHPField, "HP EV Yield", "EV Yield", 0, 3),
        CreateField(EVYieldAttackField, "Attack EV Yield", "EV Yield", 0, 3),
        CreateField(EVYieldDefenseField, "Defense EV Yield", "EV Yield", 0, 3),
        CreateField(EVYieldSpecialAttackField, "Sp. Atk EV Yield", "EV Yield", 0, 3),
        CreateField(EVYieldSpecialDefenseField, "Sp. Def EV Yield", "EV Yield", 0, 3),
        CreateField(EVYieldSpeedField, "Speed EV Yield", "EV Yield", 0, 3),
        CreateField(Type1Field, "Type 1", "Traits", 0, 17, TypeOptions),
        CreateField(Type2Field, "Type 2", "Traits", 0, 17, TypeOptions),
        CreateField(EggGroup1Field, "Egg Group 1", "Traits", 0, 15, EggGroupOptions),
        CreateField(EggGroup2Field, "Egg Group 2", "Traits", 0, 15, EggGroupOptions),
        CreateField(ExpGrowthField, "EXP Growth", "Traits", 0, 5, ExpGrowthOptions),
        CreateField(ColorField, "Color", "Traits", 0, 63, ColorOptions),
        CreateField(HeldItem1Field, "Held Item 50%", "Held Items", 0, short.MaxValue),
        CreateField(HeldItem2Field, "Held Item 5%", "Held Items", 0, short.MaxValue),
        CreateField(HeldItem3Field, "Held Item 1%", "Held Items", 0, short.MaxValue),
        CreateField(Ability1Field, "Ability 1", "Abilities", 0, ushort.MaxValue),
        CreateField(Ability2Field, "Ability 2", "Abilities", 0, ushort.MaxValue),
        CreateField(HiddenAbilityField, "Hidden Ability", "Abilities", 0, ushort.MaxValue),
        CreateField(GenderRatioField, "Gender Ratio", "Identity", 0, byte.MaxValue),
        CreateField(BaseFriendshipField, "Base Friendship", "Identity", 0, byte.MaxValue),
        CreateField(BaseExperienceField, "Base EXP", "Identity", 0, ushort.MaxValue),
        CreateField(HatchCyclesField, "Hatch Cycles", "Identity", 0, byte.MaxValue),
        CreateField(CatchRateField, "Catch Rate", "Identity", 0, byte.MaxValue),
        CreateField(EvolutionStageField, "Evolution Stage", "Forms/Dex", 0, byte.MaxValue),
        CreateField(FormStatsIndexField, "Form Sprite/Stats Index", "Forms/Dex", 0, ushort.MaxValue),
        CreateField(FormCountField, "Forms Count", "Forms/Dex", 0, byte.MaxValue),
        CreateField(FormField, "Form", "Forms/Dex", 0, ushort.MaxValue),
        CreateField(LocalFormIndexField, "Local Form Index", "Forms/Dex", 0, ushort.MaxValue),
        CreateField(HatchedSpeciesField, "Hatched Species", "Forms/Dex", 0, ushort.MaxValue),
        CreateField(HeightField, "Height", "Forms/Dex", 0, ushort.MaxValue),
        CreateField(WeightField, "Weight", "Forms/Dex", 0, ushort.MaxValue),
        CreateField(ModelIdField, "Model ID", "Forms/Dex", 0, ushort.MaxValue),
        CreateField(RegionalDexIndexField, "Regional Dex", "Forms/Dex", 0, ushort.MaxValue),
        CreateField(ArmorDexIndexField, "Armor Dex", "Forms/Dex", 0, ushort.MaxValue),
        CreateField(CrownDexIndexField, "Crown Dex", "Forms/Dex", 0, ushort.MaxValue),
        CreateBooleanField(IsRegionalFormField, "Regional Variant", "Flags"),
        CreateBooleanField(CanNotDynamaxField, "Cannot Dynamax", "Flags"),
        CreateBooleanField(IsPresentInGameField, "Present In Game", "Flags"),
        CreateBooleanField(HasSpriteFormField, "Has Sprite Form", "Flags"),
    ];

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

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
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
            EditableFields,
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
            new SwShPokemonPersonalDetails(
                personal.Type1,
                personal.Type2,
                personal.CatchRate,
                personal.EvolutionStage,
                personal.EVYieldHP,
                personal.EVYieldAttack,
                personal.EVYieldDefense,
                personal.EVYieldSpecialAttack,
                personal.EVYieldSpecialDefense,
                personal.EVYieldSpeed,
                personal.HeldItem1,
                personal.HeldItem2,
                personal.HeldItem3,
                personal.GenderRatio,
                personal.HatchCycles,
                personal.BaseFriendship,
                personal.ExpGrowth,
                personal.EggGroup1,
                personal.EggGroup2,
                personal.FormStatsIndex,
                personal.FormCount,
                personal.Color,
                personal.IsPresentInGame,
                personal.HasSpriteForm,
                personal.BaseExperience,
                personal.Height,
                personal.Weight,
                personal.ModelId,
                personal.HatchedSpecies,
                personal.LocalFormIndex,
                personal.IsRegionalForm,
                personal.CanNotDynamax,
                personal.RegionalDexIndex,
                personal.Form,
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

    public static SwShPokemonEditableField? GetEditableField(string? field)
    {
        return EditableFields.FirstOrDefault(editableField =>
            string.Equals(editableField.Field, field, StringComparison.Ordinal));
    }

    internal static WorkflowFileSource? ResolvePersonalDataSource(OpenedProject project)
    {
        return ResolveWorkflowFile(project, PersonalDataPath);
    }

    internal static string? ResolveOutputPath(ProjectPaths paths, string targetRelativePath)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath) || Path.IsPathRooted(targetRelativePath))
        {
            return null;
        }

        var outputRoot = Path.GetFullPath(paths.OutputRootPath);
        var targetPath = Path.GetFullPath(Path.Combine(
            outputRoot,
            targetRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(outputRoot, targetPath);

        return relative.StartsWith("..", StringComparison.Ordinal)
            || Path.IsPathRooted(relative)
            ? null
            : targetPath;
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

    private static SwShPokemonEditableField CreateField(
        string field,
        string label,
        string group,
        int minimumValue,
        int maximumValue,
        IReadOnlyList<SwShPokemonEditableFieldOption>? options = null)
    {
        return new SwShPokemonEditableField(
            field,
            label,
            group,
            "integer",
            minimumValue,
            maximumValue,
            options ?? []);
    }

    private static SwShPokemonEditableField CreateBooleanField(
        string field,
        string label,
        string group)
    {
        return new SwShPokemonEditableField(
            field,
            label,
            group,
            "boolean",
            0,
            1,
            []);
    }

    private static SwShPokemonEditableFieldOption CreateOption(int value, string label)
    {
        return new SwShPokemonEditableFieldOption(value, label);
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
