// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Items;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.Pokemon;

public sealed class SwShPokemonWorkflowService
{
    public const string PersonalDataPath = SwShPersonalTable.PersonalDataRelativePath;
    public const string LearnsetDataPath = SwShPokemonLearnsetTable.LearnsetDataRelativePath;
    public const string EvolutionDataDirectory = SwShEvolutionSet.EvolutionDataRelativeDirectory;
    public const string EnglishPokemonNamePath = "romfs/bin/message/English/common/pokelist.dat";
    public const string EnglishSpeciesNamePath = "romfs/bin/message/English/common/monsname.dat";
    public const string EnglishItemNamePath = "romfs/bin/message/English/common/itemname.dat";
    public const string EnglishAbilityNamePath = "romfs/bin/message/English/common/tokusei.dat";
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
    public const string CompatibilityFieldPrefix = "compatibility";
    public const string TechnicalMachineCompatibilityGroupId = "tm";
    public const string TechnicalRecordCompatibilityGroupId = "tr";
    public const string TypeTutorCompatibilityGroupId = "typeTutor";
    public const string ArmorTutorCompatibilityGroupId = "armorTutor";

    private const string EvolutionArgumentKindNone = "none";
    private const string EvolutionArgumentKindLevel = "level";
    private const string EvolutionArgumentKindItem = "item";
    private const string EvolutionArgumentKindMove = "move";
    private const string EvolutionArgumentKindSpecies = "species";
    private const string EvolutionArgumentKindValue = "value";
    private const string EvolutionArgumentKindType = "type";
    private const string EvolutionArgumentKindVersion = "version";

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

    private static readonly IReadOnlyList<SwShPokemonEditableFieldOption> GenderRatioOptions =
        CreateGenderRatioOptions();

    private static readonly IReadOnlyList<int> TechnicalMachineMoveIds =
    [
        5, 25, 6, 7, 8, 9, 19, 42, 63, 416,
        345, 76, 669, 83, 86, 91, 103, 113, 115, 219,
        120, 156, 157, 168, 173, 182, 184, 196, 202, 204,
        211, 213, 201, 240, 241, 258, 250, 251, 261, 263,
        129, 270, 279, 280, 286, 291, 311, 313, 317, 328,
        331, 333, 340, 341, 350, 362, 369, 371, 372, 374,
        384, 385, 683, 409, 419, 421, 422, 423, 424, 427,
        433, 472, 478, 440, 474, 490, 496, 506, 512, 514,
        521, 523, 527, 534, 541, 555, 566, 577, 580, 581,
        604, 678, 595, 598, 206, 403, 684, 693, 707, 784,
    ];

    private static readonly IReadOnlyList<int> TechnicalRecordMoveIds =
    [
        14, 34, 53, 56, 57, 58, 59, 67, 85, 87,
        89, 94, 97, 116, 118, 126, 127, 133, 141, 161,
        164, 179, 188, 191, 200, 473, 203, 214, 224, 226,
        227, 231, 242, 247, 248, 253, 257, 269, 271, 276,
        285, 299, 304, 315, 322, 330, 334, 337, 339, 347,
        348, 349, 360, 370, 390, 394, 396, 398, 399, 402,
        404, 405, 406, 408, 411, 412, 413, 414, 417, 428,
        430, 437, 438, 441, 442, 444, 446, 447, 482, 484,
        486, 492, 500, 502, 503, 526, 528, 529, 535, 542,
        583, 599, 605, 663, 667, 675, 676, 706, 710, 776,
    ];

    private static readonly IReadOnlyList<int> TypeTutorMoveIds =
    [
        520, 519, 518, 338, 307, 308, 434, 796,
    ];

    private static readonly IReadOnlyList<int> ArmorTutorMoveIds =
    [
        805, 807, 812, 804, 803, 813, 811, 810, 815,
        814, 797, 806, 800, 809, 799, 808, 798, 802,
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
        CreateField(GenderRatioField, "Gender Ratio", "Identity", 0, byte.MaxValue, GenderRatioOptions),
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

    private static readonly IReadOnlyList<EvolutionMethodDefinition> EvolutionMethods =
    [
        CreateEvolutionMethod(0, "None", EvolutionArgumentKindNone, "None"),
        CreateEvolutionMethod(1, "Level Up Friendship", EvolutionArgumentKindNone, "None"),
        CreateEvolutionMethod(2, "Level Up Friendship Morning", EvolutionArgumentKindNone, "None"),
        CreateEvolutionMethod(3, "Level Up Friendship Night", EvolutionArgumentKindNone, "None"),
        CreateEvolutionMethod(4, "Level Up", EvolutionArgumentKindLevel, "Level"),
        CreateEvolutionMethod(5, "Trade", EvolutionArgumentKindNone, "None"),
        CreateEvolutionMethod(6, "Trade Held Item", EvolutionArgumentKindItem, "Item"),
        CreateEvolutionMethod(7, "Trade Shelmet/Karrablast", EvolutionArgumentKindNone, "None"),
        CreateEvolutionMethod(8, "Use Item", EvolutionArgumentKindItem, "Item"),
        CreateEvolutionMethod(9, "Level Up Attack > Defense", EvolutionArgumentKindLevel, "Level"),
        CreateEvolutionMethod(10, "Level Up Attack = Defense", EvolutionArgumentKindLevel, "Level"),
        CreateEvolutionMethod(11, "Level Up Defense > Attack", EvolutionArgumentKindLevel, "Level"),
        CreateEvolutionMethod(12, "Level Up EC < 5", EvolutionArgumentKindLevel, "Level"),
        CreateEvolutionMethod(13, "Level Up EC >= 5", EvolutionArgumentKindLevel, "Level"),
        CreateEvolutionMethod(14, "Level Up Ninjask", EvolutionArgumentKindLevel, "Level"),
        CreateEvolutionMethod(15, "Level Up Shedinja", EvolutionArgumentKindLevel, "Level"),
        CreateEvolutionMethod(16, "Level Up Beauty", EvolutionArgumentKindValue, "Beauty"),
        CreateEvolutionMethod(17, "Use Item Male", EvolutionArgumentKindItem, "Item"),
        CreateEvolutionMethod(18, "Use Item Female", EvolutionArgumentKindItem, "Item"),
        CreateEvolutionMethod(19, "Level Up Held Item Day", EvolutionArgumentKindItem, "Item"),
        CreateEvolutionMethod(20, "Level Up Held Item Night", EvolutionArgumentKindItem, "Item"),
        CreateEvolutionMethod(21, "Level Up Know Move", EvolutionArgumentKindMove, "Move"),
        CreateEvolutionMethod(22, "Level Up With Teammate", EvolutionArgumentKindSpecies, "Species"),
        CreateEvolutionMethod(23, "Level Up Male", EvolutionArgumentKindLevel, "Level"),
        CreateEvolutionMethod(24, "Level Up Female", EvolutionArgumentKindLevel, "Level"),
        CreateEvolutionMethod(25, "Level Up Electric Area", EvolutionArgumentKindNone, "None"),
        CreateEvolutionMethod(26, "Level Up Forest Area", EvolutionArgumentKindNone, "None"),
        CreateEvolutionMethod(27, "Level Up Cold Area", EvolutionArgumentKindNone, "None"),
        CreateEvolutionMethod(28, "Level Up Inverted", EvolutionArgumentKindNone, "None"),
        CreateEvolutionMethod(29, "Level Up Affection 50 Move Type", EvolutionArgumentKindType, "Type"),
        CreateEvolutionMethod(30, "Level Up Move Type", EvolutionArgumentKindType, "Type"),
        CreateEvolutionMethod(31, "Level Up Weather", EvolutionArgumentKindLevel, "Level"),
        CreateEvolutionMethod(32, "Level Up Morning", EvolutionArgumentKindLevel, "Level"),
        CreateEvolutionMethod(33, "Level Up Night", EvolutionArgumentKindLevel, "Level"),
        CreateEvolutionMethod(34, "Level Up Female Form 1", EvolutionArgumentKindLevel, "Level"),
        CreateEvolutionMethod(35, "Unused", EvolutionArgumentKindNone, "None"),
        CreateEvolutionMethod(36, "Level Up Version", EvolutionArgumentKindVersion, "Version"),
        CreateEvolutionMethod(37, "Level Up Version Day", EvolutionArgumentKindVersion, "Version"),
        CreateEvolutionMethod(38, "Level Up Version Night", EvolutionArgumentKindVersion, "Version"),
        CreateEvolutionMethod(39, "Level Up Summit", EvolutionArgumentKindLevel, "Level"),
        CreateEvolutionMethod(40, "Level Up Dusk", EvolutionArgumentKindLevel, "Level"),
        CreateEvolutionMethod(41, "Level Up Wormhole", EvolutionArgumentKindLevel, "Level"),
        CreateEvolutionMethod(42, "Use Item Wormhole", EvolutionArgumentKindItem, "Item"),
        CreateEvolutionMethod(43, "Critical Hits In Battle", EvolutionArgumentKindVersion, "Version"),
        CreateEvolutionMethod(44, "HP Lost In Battle", EvolutionArgumentKindVersion, "Version"),
        CreateEvolutionMethod(45, "Spin", EvolutionArgumentKindNone, "None"),
        CreateEvolutionMethod(46, "Level Up Nature Amped", EvolutionArgumentKindNone, "None"),
        CreateEvolutionMethod(47, "Level Up Nature Low Key", EvolutionArgumentKindNone, "None"),
        CreateEvolutionMethod(48, "Tower Of Darkness", EvolutionArgumentKindNone, "None"),
        CreateEvolutionMethod(49, "Tower Of Waters", EvolutionArgumentKindNone, "None"),
    ];

    private static readonly IReadOnlyList<SwShPokemonEditableFieldOption> ByteArgumentOptions =
        CreateNumericOptions(0, byte.MaxValue);

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
        var speciesNames = LoadOptionalTextTable(
            project,
            EnglishSpeciesNamePath,
            "Pokemon species names",
            diagnostics);
        var itemNames = LoadOptionalTextTable(
            project,
            EnglishItemNamePath,
            "Item names",
            diagnostics);
        var abilityNames = LoadOptionalTextTable(
            project,
            EnglishAbilityNamePath,
            "Ability names",
            diagnostics);
        var moveNames = LoadOptionalTextTable(
            project,
            EnglishMoveNamePath,
            "Move names",
            diagnostics);
        var itemRecords = LoadOptionalItemRecords(project, diagnostics);
        var itemDisplayNames = CreateItemDisplayNames(itemNames, moveNames, itemRecords);
        var itemOptions = CreateIndexedOptions(itemDisplayNames, "Item");
        var evolutionItemOptions = CreateEvolutionItemOptions(itemRecords, itemDisplayNames);
        var learnsets = LoadLearnsets(project, diagnostics);
        var evolutions = LoadEvolutions(project, diagnostics);
        var displaySpeciesNames = speciesNames.Count > 0 ? speciesNames : Array.Empty<string>();
        var evolutionMethodOptions = CreateEvolutionMethodOptions(
            itemOptions,
            evolutionItemOptions,
            itemRecords.Count > 0,
            moveNames,
            displaySpeciesNames);
        var learnsetMoveOptions = CreateIndexedOptions(moveNames, "Move");

        try
        {
            var personalTable = SwShPersonalTable.Parse(File.ReadAllBytes(personalSource.AbsolutePath));
            var provenance = CreateProvenance(personalSource.GraphEntry);
            var formOwners = CreateFormOwnerLookup(personalTable.Records);
            var pokemon = personalTable.Records
                .Select(record => ToPokemonRecord(
                    record,
                    displaySpeciesNames,
                    abilityNames,
                    itemDisplayNames,
                    moveNames,
                    learnsets,
                    evolutions,
                    formOwners,
                    provenance))
                .ToArray();
            var sourceFileCount =
                1
                + (pokemonNames.Count > 0 ? 1 : 0)
                + (speciesNames.Count > 0 ? 1 : 0)
                + (itemNames.Count > 0 ? 1 : 0)
                + (itemRecords.Count > 0 ? 1 : 0)
                + (abilityNames.Count > 0 ? 1 : 0)
                + (moveNames.Count > 0 ? 1 : 0)
                + (learnsets.Count > 0 ? 1 : 0)
                + (evolutions.Count > 0 ? evolutions.Count : 0);

            return CreateWorkflow(
                summary,
                pokemon,
                sourceFileCount,
                evolutionMethodOptions,
                learnsetMoveOptions,
                CreateEditableFields(itemDisplayNames, abilityNames, displaySpeciesNames),
                diagnostics);
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

    private static IReadOnlyList<SwShItemTableRecord> LoadOptionalItemRecords(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var source = SwShItemsWorkflowService.ResolveItemDataSource(project);
        if (source is null)
        {
            return [];
        }

        try
        {
            return SwShItemTable.Parse(File.ReadAllBytes(source.AbsolutePath))
                .Records
                .OrderBy(item => item.ItemId)
                .ToArray();
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Item metadata could not be decoded; evolution item selectors will be limited: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Sword/Shield item.dat"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Item metadata could not be read; evolution item selectors will be limited: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield item.dat"));
        }

        return [];
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
        return CreateWorkflow(
            summary,
            pokemon,
            sourceFileCount,
            CreateEvolutionMethodOptions([], [], false, [], []),
            [],
            EditableFields,
            diagnostics);
    }

    private static SwShPokemonWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShPokemonRecord> pokemon,
        int sourceFileCount,
        IReadOnlyList<SwShPokemonEvolutionMethodOption> evolutionMethodOptions,
        IReadOnlyList<SwShPokemonEditableFieldOption> learnsetMoveOptions,
        IReadOnlyList<SwShPokemonEditableField> editableFields,
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
            evolutionMethodOptions,
            learnsetMoveOptions,
            editableFields,
            diagnostics);
    }

    private static SwShPokemonRecord ToPokemonRecord(
        SwShPersonalRecord personal,
        IReadOnlyList<string> speciesNames,
        IReadOnlyList<string> abilityNames,
        IReadOnlyList<string> itemNames,
        IReadOnlyList<string> moveNames,
        IReadOnlyDictionary<int, SwShPokemonLearnsetRecord> learnsets,
        IReadOnlyDictionary<int, IReadOnlyList<SwShEvolutionRecord>> evolutions,
        IReadOnlyDictionary<int, PokemonFormOwner> formOwners,
        SwShPokemonProvenance provenance)
    {
        var displayIdentity = ResolveDisplayIdentity(personal, speciesNames, formOwners);
        var learnset = learnsets.TryGetValue(personal.PersonalId, out var learnsetRecord)
            ? learnsetRecord.Moves.Select(move => new SwShPokemonLearnsetMove(
                    move.Slot,
                    move.MoveId,
                    GetIndexedName(move.MoveId, moveNames, "Move"),
                    move.Level))
                .ToArray()
            : [];
        var evolutionRecords = evolutions.TryGetValue(personal.PersonalId, out var evolutionRecord)
            ? evolutionRecord
                .Select(evolution => ToPokemonEvolutionRecord(evolution, itemNames, moveNames, speciesNames))
                .ToArray()
            : [];
        var compatibility = CreateCompatibilityGroups(personal, moveNames);

        return new SwShPokemonRecord(
            personal.PersonalId,
            displayIdentity.SpeciesId,
            personal.Form,
            displayIdentity.Name,
            displayIdentity.FormLabel,
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
            new SwShPokemonAbilitySet(
                personal.Ability1,
                FormatIndexedOption(personal.Ability1, abilityNames, "Ability"),
                personal.Ability2,
                FormatIndexedOption(personal.Ability2, abilityNames, "Ability"),
                personal.HiddenAbility,
                FormatIndexedOption(personal.HiddenAbility, abilityNames, "Ability")),
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
            FormatGenderRatio(personal.GenderRatio, includeValue: true),
            personal.BaseExperience,
            personal.Height,
            personal.Weight,
            evolutionRecords,
            learnset,
            compatibility,
            provenance);
    }

    private static SwShPokemonEvolutionRecord ToPokemonEvolutionRecord(
        SwShEvolutionRecord evolution,
        IReadOnlyList<string> itemNames,
        IReadOnlyList<string> moveNames,
        IReadOnlyList<string> speciesNames)
    {
        var definition = GetEvolutionMethodDefinition(evolution.Method);
        return new SwShPokemonEvolutionRecord(
            evolution.Slot,
            evolution.Method,
            evolution.Argument,
            evolution.Species,
            evolution.Form,
            evolution.Level,
            definition.Name,
            definition.ArgumentKind,
            definition.ArgumentLabel,
            FormatEvolutionArgument(evolution.Argument, definition.ArgumentKind, itemNames, moveNames, speciesNames));
    }

    private static IReadOnlyList<SwShPokemonEvolutionMethodOption> CreateEvolutionMethodOptions(
        IReadOnlyList<SwShPokemonEditableFieldOption> itemOptions,
        IReadOnlyList<SwShPokemonEditableFieldOption> evolutionItemOptions,
        bool hasEvolutionItemMetadata,
        IReadOnlyList<string> moveNames,
        IReadOnlyList<string> speciesNames)
    {
        return EvolutionMethods
            .Select(method => new SwShPokemonEvolutionMethodOption(
                method.Value,
                string.Create(CultureInfo.InvariantCulture, $"{method.Value:000} {method.Name}"),
                method.ArgumentKind,
                method.ArgumentLabel,
                CreateEvolutionArgumentOptions(
                    method,
                    itemOptions,
                    evolutionItemOptions,
                    hasEvolutionItemMetadata,
                    moveNames,
                    speciesNames)))
            .ToArray();
    }

    private static IReadOnlyList<SwShPokemonEditableFieldOption> CreateEvolutionArgumentOptions(
        EvolutionMethodDefinition method,
        IReadOnlyList<SwShPokemonEditableFieldOption> itemOptions,
        IReadOnlyList<SwShPokemonEditableFieldOption> evolutionItemOptions,
        bool hasEvolutionItemMetadata,
        IReadOnlyList<string> moveNames,
        IReadOnlyList<string> speciesNames)
    {
        return method.ArgumentKind switch
        {
            EvolutionArgumentKindItem => IsUseItemEvolutionMethod(method.Value) && hasEvolutionItemMetadata
                ? evolutionItemOptions
                : itemOptions,
            EvolutionArgumentKindMove => CreateIndexedOptions(moveNames, "Move"),
            EvolutionArgumentKindSpecies => CreateIndexedOptions(speciesNames, "Species"),
            EvolutionArgumentKindType => TypeOptions,
            EvolutionArgumentKindValue or EvolutionArgumentKindVersion => ByteArgumentOptions,
            _ => [],
        };
    }

    private static string FormatEvolutionArgument(
        int argument,
        string argumentKind,
        IReadOnlyList<string> itemNames,
        IReadOnlyList<string> moveNames,
        IReadOnlyList<string> speciesNames)
    {
        return argumentKind switch
        {
            EvolutionArgumentKindItem => GetIndexedName(argument, itemNames, "Item"),
            EvolutionArgumentKindMove => GetIndexedName(argument, moveNames, "Move"),
            EvolutionArgumentKindSpecies => GetIndexedName(argument, speciesNames, "Species"),
            EvolutionArgumentKindType => FormatType(argument),
            EvolutionArgumentKindValue => argument.ToString(CultureInfo.InvariantCulture),
            EvolutionArgumentKindVersion => argument.ToString(CultureInfo.InvariantCulture),
            _ => "None",
        };
    }

    private static EvolutionMethodDefinition GetEvolutionMethodDefinition(int method)
    {
        return EvolutionMethods.FirstOrDefault(definition => definition.Value == method)
            ?? CreateEvolutionMethod(
                method,
                string.Create(CultureInfo.InvariantCulture, $"Method {method}"),
                EvolutionArgumentKindValue,
                "Argument");
    }

    private static IReadOnlyList<SwShPokemonCompatibilityGroup> CreateCompatibilityGroups(
        SwShPersonalRecord personal,
        IReadOnlyList<string> moveNames)
    {
        return
        [
            CreateCompatibilityGroup(
                TechnicalMachineCompatibilityGroupId,
                "TMs",
                TechnicalMachineMoveIds,
                personal.TechnicalMachines,
                moveNames,
                slot => $"TM{slot:00}"),
            CreateCompatibilityGroup(
                TechnicalRecordCompatibilityGroupId,
                "TRs",
                TechnicalRecordMoveIds,
                personal.TechnicalRecords,
                moveNames,
                slot => $"TR{slot:00}"),
            CreateCompatibilityGroup(
                TypeTutorCompatibilityGroupId,
                "Type Tutors",
                TypeTutorMoveIds,
                personal.TypeTutors,
                moveNames),
            CreateCompatibilityGroup(
                ArmorTutorCompatibilityGroupId,
                "Armor Tutors",
                ArmorTutorMoveIds,
                personal.ArmorTutors,
                moveNames),
        ];
    }

    private static SwShPokemonCompatibilityGroup CreateCompatibilityGroup(
        string groupId,
        string label,
        IReadOnlyList<int> moveIds,
        IReadOnlyList<bool> flags,
        IReadOnlyList<string> moveNames,
        Func<int, string>? slotLabelFactory = null)
    {
        var entries = moveIds
            .Select((moveId, slot) =>
            {
                var moveName = GetIndexedName(moveId, moveNames, "Move");
                var slotLabel = slotLabelFactory?.Invoke(slot);
                var entryLabel = slotLabel is null ? moveName : $"{slotLabel} ({moveName})";

                return new SwShPokemonCompatibilityEntry(
                    slot,
                    moveId,
                    moveName,
                    entryLabel,
                    flags[slot]);
            })
            .ToArray();

        return new SwShPokemonCompatibilityGroup(
            groupId,
            label,
            entries.Count(entry => entry.CanLearn),
            entries);
    }

    private static IReadOnlyList<string> CreateItemDisplayNames(
        IReadOnlyList<string> itemNames,
        IReadOnlyList<string> moveNames,
        IReadOnlyList<SwShItemTableRecord> itemRecords)
    {
        if (itemRecords.Count == 0)
        {
            return itemNames;
        }

        var itemCount = Math.Max(itemNames.Count, itemRecords.Max(item => item.ItemId) + 1);
        var displayNames = new string[itemCount];
        for (var itemId = 0; itemId < itemNames.Count && itemId < displayNames.Length; itemId++)
        {
            displayNames[itemId] = itemNames[itemId];
        }

        foreach (var item in itemRecords)
        {
            displayNames[item.ItemId] = SwShItemsWorkflowService.FormatItemDisplayName(
                item,
                itemNames,
                moveNames);
        }

        return displayNames;
    }

    private static IReadOnlyList<SwShPokemonEditableFieldOption> CreateEvolutionItemOptions(
        IReadOnlyList<SwShItemTableRecord> itemRecords,
        IReadOnlyList<string> itemDisplayNames)
    {
        return itemRecords
            .Where(IsUsableEvolutionItem)
            .Select(item => CreateOption(
                item.ItemId,
                FormatIndexedOption(item.ItemId, itemDisplayNames, "Item")))
            .ToArray();
    }

    private static bool IsUsableEvolutionItem(SwShItemTableRecord item)
    {
        return item.CanUseOnPokemon && (item.Boost0 & 0x08) != 0;
    }

    private static bool IsUseItemEvolutionMethod(int method)
    {
        return method is 8 or 17 or 18 or 42;
    }

    private static IReadOnlyDictionary<int, PokemonFormOwner> CreateFormOwnerLookup(
        IReadOnlyList<SwShPersonalRecord> records)
    {
        var owners = new Dictionary<int, PokemonFormOwner>();

        foreach (var record in records)
        {
            if (record.FormStatsIndex == 0 || record.FormCount <= 1)
            {
                continue;
            }

            for (var localFormIndex = 1; localFormIndex < record.FormCount; localFormIndex++)
            {
                var personalId = record.FormStatsIndex + localFormIndex - 1;
                if ((uint)personalId >= (uint)records.Count || personalId == record.PersonalId)
                {
                    continue;
                }

                owners.TryAdd(personalId, new PokemonFormOwner(record.PersonalId, localFormIndex));
            }
        }

        return owners;
    }

    private static PokemonDisplayIdentity ResolveDisplayIdentity(
        SwShPersonalRecord personal,
        IReadOnlyList<string> speciesNames,
        IReadOnlyDictionary<int, PokemonFormOwner> formOwners)
    {
        if (formOwners.TryGetValue(personal.PersonalId, out var owner))
        {
            var formLabel = ResolveFormLabel(personal, owner);
            var speciesName = GetIndexedName(owner.SpeciesId, speciesNames, "Pokemon");

            return new PokemonDisplayIdentity(
                owner.SpeciesId,
                FormatPokemonDisplayName(speciesName, formLabel),
                formLabel);
        }

        if (personal.PersonalId >= speciesNames.Count && IsEmptyPersonalRecord(personal))
        {
            return new PokemonDisplayIdentity(
                personal.PersonalId,
                string.Create(CultureInfo.InvariantCulture, $"Unused {personal.PersonalId}"),
                "Unused");
        }

        var baseFormLabel = ResolveFormLabel(personal, owner: null);
        var baseSpeciesName = GetIndexedName(personal.PersonalId, speciesNames, "Pokemon");

        return new PokemonDisplayIdentity(
            personal.PersonalId,
            FormatPokemonDisplayName(baseSpeciesName, baseFormLabel),
            baseFormLabel);
    }

    private static string ResolveFormLabel(SwShPersonalRecord personal, PokemonFormOwner? owner)
    {
        var localFormIndex = personal.LocalFormIndex != 0
            ? personal.LocalFormIndex
            : personal.Form != 0
                ? personal.Form
                : owner?.LocalFormIndex ?? 0;

        if (personal.IsRegionalForm)
        {
            return SwShSpeciesFormLabels.ResolveRegionalFormLabel(owner?.SpeciesId ?? personal.PersonalId, localFormIndex);
        }

        if (localFormIndex != 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"Form {localFormIndex}");
        }

        return owner is null ? "Base" : string.Create(CultureInfo.InvariantCulture, $"Form {owner.LocalFormIndex}");
    }

    private static string FormatPokemonDisplayName(string speciesName, string formLabel)
    {
        return string.Equals(formLabel, "Base", StringComparison.Ordinal)
            || string.Equals(formLabel, "Unused", StringComparison.Ordinal)
            ? speciesName
            : string.Create(CultureInfo.InvariantCulture, $"{speciesName} ({formLabel})");
    }

    private static bool IsEmptyPersonalRecord(SwShPersonalRecord personal)
    {
        return personal.BaseStatTotal == 0
            && personal.CatchRate == 0
            && personal.EvolutionStage == 0
            && personal.FormStatsIndex == 0
            && personal.FormCount <= 1
            && personal.ModelId == 0
            && personal.HatchedSpecies == 0
            && personal.LocalFormIndex == 0
            && personal.Form == 0
            && !personal.IsPresentInGame;
    }

    private static IReadOnlyList<SwShPokemonEditableField> CreateEditableFields(
        IReadOnlyList<string> itemNames,
        IReadOnlyList<string> abilityNames,
        IReadOnlyList<string> speciesNames)
    {
        var itemOptions = CreateIndexedOptions(itemNames, "Item");
        var abilityOptions = CreateIndexedOptions(abilityNames, "Ability");
        var speciesOptions = CreateIndexedOptions(speciesNames, "Species");

        return EditableFields
            .Select(field =>
            {
                var options = field.Field switch
                {
                    HeldItem1Field or HeldItem2Field or HeldItem3Field => itemOptions,
                    Ability1Field or Ability2Field or HiddenAbilityField => abilityOptions,
                    HatchedSpeciesField => speciesOptions,
                    _ => field.Options,
                };

                return options.Count == 0 || ReferenceEquals(options, field.Options)
                    ? field
                    : field with { Options = options };
            })
            .ToArray();
    }

    private static IReadOnlyList<SwShPokemonEditableFieldOption> CreateIndexedOptions(
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
                    return CreateOption(index, string.Create(CultureInfo.InvariantCulture, $"{index:000} {label}"));
                })
                .ToArray();
    }

    private static string FormatIndexedOption(int id, IReadOnlyList<string> names, string fallbackPrefix)
    {
        var label = (uint)id < (uint)names.Count && !string.IsNullOrWhiteSpace(names[id])
            ? names[id]
            : id == 0 ? "None" : $"{fallbackPrefix} {id}";

        return string.Create(CultureInfo.InvariantCulture, $"{id:000} {label}");
    }

    private static string GetIndexedName(int id, IReadOnlyList<string> names, string fallbackPrefix)
    {
        if ((uint)id < (uint)names.Count && !string.IsNullOrWhiteSpace(names[id]))
        {
            return names[id];
        }

        return $"{fallbackPrefix} {id}";
    }

    private static IReadOnlyList<SwShPokemonEditableFieldOption> CreateGenderRatioOptions()
    {
        return Enumerable
            .Range(0, byte.MaxValue + 1)
            .Select(value => CreateOption(value, FormatGenderRatio(value, includeValue: true)))
            .ToArray();
    }

    private static string FormatGenderRatio(int genderRatio, bool includeValue)
    {
        var label = genderRatio switch
        {
            0 => "Male only",
            254 => "Female only",
            255 => "Genderless",
            _ => FormatMixedGenderRatio(genderRatio),
        };

        return includeValue
            ? string.Create(CultureInfo.InvariantCulture, $"{genderRatio:000} {label}")
            : label;
    }

    private static string FormatMixedGenderRatio(int genderRatio)
    {
        var femalePercent = (genderRatio + 1) * 100.0 / 256.0;
        var malePercent = 100.0 - femalePercent;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"Male {malePercent:0.#}% / Female {femalePercent:0.#}%");
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

    public static string CreateCompatibilityFieldId(string groupId, int slot)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{CompatibilityFieldPrefix}:{groupId}:{slot}");
    }

    public static bool TryParseCompatibilityField(
        string? field,
        out string groupId,
        out int slot)
    {
        groupId = string.Empty;
        slot = -1;

        if (string.IsNullOrWhiteSpace(field))
        {
            return false;
        }

        var parts = field.Split(':');
        if (parts.Length != 3
            || !string.Equals(parts[0], CompatibilityFieldPrefix, StringComparison.Ordinal)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out slot))
        {
            return false;
        }

        groupId = parts[1];
        return IsValidCompatibilitySlot(groupId, slot);
    }

    public static bool IsValidCompatibilitySlot(string groupId, int slot)
    {
        return groupId switch
        {
            TechnicalMachineCompatibilityGroupId => (uint)slot < (uint)TechnicalMachineMoveIds.Count,
            TechnicalRecordCompatibilityGroupId => (uint)slot < (uint)TechnicalRecordMoveIds.Count,
            TypeTutorCompatibilityGroupId => (uint)slot < (uint)TypeTutorMoveIds.Count,
            ArmorTutorCompatibilityGroupId => (uint)slot < (uint)ArmorTutorMoveIds.Count,
            _ => false,
        };
    }

    internal static WorkflowFileSource? ResolvePersonalDataSource(OpenedProject project)
    {
        return ResolveWorkflowFile(project, PersonalDataPath);
    }

    internal static WorkflowFileSource? ResolveBasePersonalDataSource(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var graphEntry = project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, PersonalDataPath, StringComparison.OrdinalIgnoreCase)
            && entry.BaseFile is not null);
        if (graphEntry is null)
        {
            return null;
        }

        var sourcePath = PersonalDataPath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase)
            ? CombineGraphPath(project.Paths.BaseRomFsPath, PersonalDataPath["romfs/".Length..])
            : null;

        return sourcePath is not null && File.Exists(sourcePath)
            ? new WorkflowFileSource(graphEntry, sourcePath)
            : null;
    }

    internal static WorkflowFileSource? ResolveLearnsetDataSource(OpenedProject project)
    {
        return ResolveWorkflowFile(project, LearnsetDataPath);
    }

    internal static WorkflowFileSource? ResolveEvolutionDataSource(OpenedProject project, int personalId)
    {
        return ResolveWorkflowFile(project, CreateEvolutionDataPath(personalId));
    }

    public static string CreateEvolutionDataPath(int personalId)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{EvolutionDataDirectory}/evo_{personalId:000}.bin");
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

    private static IReadOnlyList<SwShPokemonEditableFieldOption> CreateNumericOptions(int minimum, int maximum)
    {
        return Enumerable
            .Range(minimum, maximum - minimum + 1)
            .Select(value => CreateOption(
                value,
                value.ToString(CultureInfo.InvariantCulture)))
            .ToArray();
    }

    private static EvolutionMethodDefinition CreateEvolutionMethod(
        int value,
        string name,
        string argumentKind,
        string argumentLabel)
    {
        return new EvolutionMethodDefinition(value, name, argumentKind, argumentLabel);
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

    private sealed record EvolutionMethodDefinition(
        int Value,
        string Name,
        string ArgumentKind,
        string ArgumentLabel);

    private sealed record PokemonFormOwner(
        int SpeciesId,
        int LocalFormIndex);

    private sealed record PokemonDisplayIdentity(
        int SpeciesId,
        string Name,
        string FormLabel);
}
