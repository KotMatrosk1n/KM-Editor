// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Pokemon;
using KM.Core.Projects;
using KM.SV.Data;
using KM.SV.Workflows;

namespace KM.SV.Pokemon;

internal sealed class SvPokemonWorkflowService
{
    private const string WorkflowLabel = "Pokemon Data";
    private const string WorkflowDescription =
        "Edit Scarlet/Violet personal data, evolutions, learnsets, and move compatibility.";
    private const string EvolutionArgumentKindNone = "none";
    private const string EvolutionArgumentKindLevel = "level";
    private const string EvolutionArgumentKindItem = "item";
    private const string EvolutionArgumentKindMove = "move";
    private const string EvolutionArgumentKindSpecies = "species";
    private const string EvolutionArgumentKindValue = "value";
    private const string EvolutionArgumentKindType = "type";
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
    public const string RegionalDexIndexField = "regionalDexIndex";
    public const string FormField = "form";
    public const string ArmorDexIndexField = "armorDexIndex";
    public const string CrownDexIndexField = "crownDexIndex";
    public const string CompatibilityFieldPrefix = "compatibility";
    public const string TechnicalMachineCompatibilityGroupId = "tm";
    public const string TechnicalRecordCompatibilityGroupId = "tr";
    public const string TypeTutorCompatibilityGroupId = "typeTutor";
    public const string ArmorTutorCompatibilityGroupId = "armorTutor";

    private static readonly IReadOnlyList<SvPokemonEditableFieldOption> BooleanOptions =
    [
        new(0, "No"),
        new(1, "Yes"),
    ];

    private static readonly IReadOnlyList<SvPokemonEditableField> BaseEditableFields =
    [
        CreateField(SvPokemonWorkflowService.HPField, "HP", "Base Stats", 0, byte.MaxValue),
        CreateField(SvPokemonWorkflowService.AttackField, "Attack", "Base Stats", 0, byte.MaxValue),
        CreateField(SvPokemonWorkflowService.DefenseField, "Defense", "Base Stats", 0, byte.MaxValue),
        CreateField(SvPokemonWorkflowService.SpecialAttackField, "Sp. Atk", "Base Stats", 0, byte.MaxValue),
        CreateField(SvPokemonWorkflowService.SpecialDefenseField, "Sp. Def", "Base Stats", 0, byte.MaxValue),
        CreateField(SvPokemonWorkflowService.SpeedField, "Speed", "Base Stats", 0, byte.MaxValue),
        CreateField(SvPokemonWorkflowService.Type1Field, "Type 1", "Traits", 0, 17),
        CreateField(SvPokemonWorkflowService.Type2Field, "Type 2", "Traits", 0, 17),
        CreateField(SvPokemonWorkflowService.Ability1Field, "Ability 1", "Abilities", 0, ushort.MaxValue),
        CreateField(SvPokemonWorkflowService.Ability2Field, "Ability 2", "Abilities", 0, ushort.MaxValue),
        CreateField(SvPokemonWorkflowService.HiddenAbilityField, "Hidden Ability", "Abilities", 0, ushort.MaxValue),
        CreateField(SvPokemonWorkflowService.CatchRateField, "Catch Rate", "Identity", 0, byte.MaxValue),
        CreateField(SvPokemonWorkflowService.EvolutionStageField, "Evolution Stage", "Identity", 0, 3),
        CreateField(SvPokemonWorkflowService.EVYieldHPField, "HP EV Yield", "EV Yield", 0, byte.MaxValue),
        CreateField(SvPokemonWorkflowService.EVYieldAttackField, "Attack EV Yield", "EV Yield", 0, byte.MaxValue),
        CreateField(SvPokemonWorkflowService.EVYieldDefenseField, "Defense EV Yield", "EV Yield", 0, byte.MaxValue),
        CreateField(SvPokemonWorkflowService.EVYieldSpecialAttackField, "Sp. Atk EV Yield", "EV Yield", 0, byte.MaxValue),
        CreateField(SvPokemonWorkflowService.EVYieldSpecialDefenseField, "Sp. Def EV Yield", "EV Yield", 0, byte.MaxValue),
        CreateField(SvPokemonWorkflowService.EVYieldSpeedField, "Speed EV Yield", "EV Yield", 0, byte.MaxValue),
        CreateField(SvPokemonWorkflowService.GenderRatioField, "Gender Ratio", "Identity", 0, byte.MaxValue),
        CreateField(SvPokemonWorkflowService.HatchCyclesField, "Hatch Cycles", "Identity", 0, byte.MaxValue),
        CreateField(SvPokemonWorkflowService.BaseFriendshipField, "Base Friendship", "Identity", 0, byte.MaxValue),
        CreateField(SvPokemonWorkflowService.ExpGrowthField, "EXP Growth", "Identity", 0, byte.MaxValue),
        CreateField(SvPokemonWorkflowService.EggGroup1Field, "Egg Group 1", "Breeding", 0, byte.MaxValue),
        CreateField(SvPokemonWorkflowService.EggGroup2Field, "Egg Group 2", "Breeding", 0, byte.MaxValue),
        CreateField(SvPokemonWorkflowService.BaseExperienceField, "Base EXP", "Identity", 0, ushort.MaxValue),
        CreateField(SvPokemonWorkflowService.FormField, "Form", "Forms/Dex", 0, ushort.MaxValue),
        CreateField(SvPokemonWorkflowService.ModelIdField, "Model ID", "Forms/Dex", 0, ushort.MaxValue),
        CreateField(SvPokemonWorkflowService.ColorField, "Color", "Forms/Dex", 0, byte.MaxValue),
        CreateField(SvPokemonWorkflowService.HeightField, "Height", "Forms/Dex", 0, ushort.MaxValue),
        CreateField(SvPokemonWorkflowService.WeightField, "Weight", "Forms/Dex", 0, ushort.MaxValue),
        CreateField(SvPokemonWorkflowService.IsPresentInGameField, "Present In Game", "Flags", 0, 1, BooleanOptions),
        CreateField(SvPokemonWorkflowService.RegionalDexIndexField, "Paldea Dex", "Forms/Dex", 0, ushort.MaxValue),
        CreateField(SvPokemonWorkflowService.ArmorDexIndexField, "Kitakami Dex", "Forms/Dex", 0, ushort.MaxValue),
        CreateField(SvPokemonWorkflowService.CrownDexIndexField, "Blueberry Dex", "Forms/Dex", 0, ushort.MaxValue),
    ];

    private static readonly IReadOnlyList<SvPokemonEditableFieldOption> TypeOptions =
        CreateOptionList(
            (0, "Normal"),
            (1, "Fighting"),
            (2, "Flying"),
            (3, "Poison"),
            (4, "Ground"),
            (5, "Rock"),
            (6, "Bug"),
            (7, "Ghost"),
            (8, "Steel"),
            (9, "Fire"),
            (10, "Water"),
            (11, "Grass"),
            (12, "Electric"),
            (13, "Psychic"),
            (14, "Ice"),
            (15, "Dragon"),
            (16, "Dark"),
            (17, "Fairy"));

    private static readonly IReadOnlyList<SvPokemonEditableFieldOption> EggGroupOptions =
        CreateOptionList(
            (0, "None"),
            (1, "Monster"),
            (2, "Water 1"),
            (3, "Bug"),
            (4, "Flying"),
            (5, "Field"),
            (6, "Fairy"),
            (7, "Grass"),
            (8, "Human-Like"),
            (9, "Water 3"),
            (10, "Mineral"),
            (11, "Amorphous"),
            (12, "Water 2"),
            (13, "Ditto"),
            (14, "Dragon"),
            (15, "Undiscovered"));

    private static readonly IReadOnlyList<SvPokemonEditableFieldOption> ExpGrowthOptions =
        CreateOptionList(
            (0, "Medium Fast"),
            (1, "Erratic"),
            (2, "Fluctuating"),
            (3, "Medium Slow"),
            (4, "Fast"),
            (5, "Slow"));

    private static readonly IReadOnlyList<SvPokemonEditableFieldOption> EvolutionStageOptions =
        CreateOptionList(
            (0, "Single-stage or unevolved"),
            (1, "First evolution"),
            (2, "Second evolution"),
            (3, "Final or special stage"));

    private static readonly IReadOnlyList<SvPokemonEditableFieldOption> ColorOptions =
        CreateOptionList(
            (0, "Red"),
            (1, "Blue"),
            (2, "Yellow"),
            (3, "Green"),
            (4, "Black"),
            (5, "Brown"),
            (6, "Purple"),
            (7, "Gray"),
            (8, "White"),
            (9, "Pink"));

    private static readonly IReadOnlyList<SvPokemonEditableFieldOption> GenderRatioOptions =
        CreateGenderRatioOptions();

    private static readonly IReadOnlyList<SvPokemonEditableFieldOption> ByteArgumentOptions =
        Enumerable
            .Range(0, 256)
            .Select(value => new SvPokemonEditableFieldOption(
                value,
                value.ToString(CultureInfo.InvariantCulture)))
            .ToArray();

    private static readonly IReadOnlyDictionary<int, int> EvolutionItemParameterItemIds = new Dictionary<int, int>
    {
        [1] = 80,
        [2] = 81,
        [3] = 82,
        [4] = 83,
        [5] = 84,
        [6] = 85,
        [7] = 107,
        [8] = 108,
        [9] = 110,
        [49] = 326,
        [50] = 327,
        [52] = 849,
        [79] = 1116,
        [80] = 1117,
        [81] = 1253,
        [82] = 1254,
        [83] = 1582,
        [84] = 1592,
        [85] = 2344,
        [86] = 1861,
        [88] = 1857,
        [89] = 1858,
        [93] = 109,
        [94] = 2403,
        [95] = 2404,
        [96] = 2402,
        [119] = 2482,
        [1691] = 1691,
    };

    private static readonly IReadOnlyList<EvolutionMethodDefinition> EvolutionMethods =
    [
        new(0, "None", EvolutionArgumentKindNone, "None"),
        new(1, "Level Up Friendship", EvolutionArgumentKindNone, "None"),
        new(2, "Level Up Friendship Morning", EvolutionArgumentKindNone, "None"),
        new(3, "Level Up Friendship Night", EvolutionArgumentKindNone, "None"),
        new(4, "Level Up", EvolutionArgumentKindLevel, "Level"),
        new(5, "Trade", EvolutionArgumentKindNone, "None"),
        new(6, "Trade Held Item", EvolutionArgumentKindItem, "Item"),
        new(7, "Trade Shelmet/Karrablast", EvolutionArgumentKindNone, "None"),
        new(8, "Use Item", EvolutionArgumentKindItem, "Item"),
        new(9, "Level Up Attack > Defense", EvolutionArgumentKindLevel, "Level"),
        new(10, "Level Up Attack = Defense", EvolutionArgumentKindLevel, "Level"),
        new(11, "Level Up Defense > Attack", EvolutionArgumentKindLevel, "Level"),
        new(12, "Level Up EC < 5", EvolutionArgumentKindLevel, "Level"),
        new(13, "Level Up EC >= 5", EvolutionArgumentKindLevel, "Level"),
        new(14, "Level Up Ninjask", EvolutionArgumentKindLevel, "Level"),
        new(15, "Level Up Shedinja", EvolutionArgumentKindLevel, "Level"),
        new(16, "Level Up Beauty", EvolutionArgumentKindValue, "Beauty"),
        new(17, "Use Item Male", EvolutionArgumentKindItem, "Item"),
        new(18, "Use Item Female", EvolutionArgumentKindItem, "Item"),
        new(19, "Level Up Held Item Day", EvolutionArgumentKindItem, "Item"),
        new(20, "Level Up Held Item Night", EvolutionArgumentKindItem, "Item"),
        new(21, "Level Up Know Move", EvolutionArgumentKindMove, "Move"),
        new(22, "Level Up With Teammate", EvolutionArgumentKindSpecies, "Species"),
        new(23, "Level Up Male", EvolutionArgumentKindLevel, "Level"),
        new(24, "Level Up Female", EvolutionArgumentKindLevel, "Level"),
        new(25, "Level Up Electric Area", EvolutionArgumentKindNone, "None"),
        new(26, "Level Up Forest Area", EvolutionArgumentKindNone, "None"),
        new(27, "Level Up Cold Area", EvolutionArgumentKindNone, "None"),
        new(28, "Level Up Inverted", EvolutionArgumentKindNone, "None"),
        new(29, "Level Up Affection 50 Move Type", EvolutionArgumentKindType, "Type"),
        new(30, "Level Up With Dark-Type Teammate", EvolutionArgumentKindNone, "None"),
        new(31, "Level Up In Rain", EvolutionArgumentKindValue, "Rain rule"),
        new(32, "Level Up Morning", EvolutionArgumentKindLevel, "Level"),
        new(33, "Level Up Night", EvolutionArgumentKindLevel, "Level"),
        new(34, "Level Up Female Alternate Form", EvolutionArgumentKindLevel, "Level"),
        new(35, "Unused", EvolutionArgumentKindNone, "None"),
        new(36, "Level Up Version", EvolutionArgumentKindValue, "Version branch"),
        new(37, "Level Up Version Day", EvolutionArgumentKindValue, "Version"),
        new(38, "Level Up Version Night", EvolutionArgumentKindValue, "Version"),
        new(39, "Level Up Summit", EvolutionArgumentKindLevel, "Level"),
        new(40, "Level Up Dusk", EvolutionArgumentKindLevel, "Level"),
        new(41, "Level Up Wormhole", EvolutionArgumentKindLevel, "Level"),
        new(42, "Use Item Wormhole", EvolutionArgumentKindItem, "Item"),
        new(43, "Critical Hits In Battle", EvolutionArgumentKindValue, "Critical hits"),
        new(44, "HP Lost In Battle", EvolutionArgumentKindValue, "HP lost"),
        new(45, "Spin", EvolutionArgumentKindNone, "None"),
        new(46, "Level Up Nature Amped", EvolutionArgumentKindNone, "None"),
        new(47, "Level Up Nature Low Key", EvolutionArgumentKindNone, "None"),
        new(48, "Tower Of Darkness", EvolutionArgumentKindNone, "None"),
        new(49, "Tower Of Waters", EvolutionArgumentKindNone, "None"),
        new(50, "Walk 1000 Steps", EvolutionArgumentKindValue, "Steps"),
        new(51, "Level Up In Union Circle", EvolutionArgumentKindLevel, "Level"),
        new(52, "Level Up Maushold Family Of Four", EvolutionArgumentKindLevel, "Level"),
        new(53, "Level Up Maushold Family Of Three", EvolutionArgumentKindLevel, "Level"),
        new(54, "Collect Gimmighoul Coins", EvolutionArgumentKindValue, "Coins"),
        new(55, "Defeat Three Leader Bisharp", EvolutionArgumentKindValue, "Wins"),
        new(56, "Use Rage Fist 20 Times", EvolutionArgumentKindValue, "Uses"),
        new(57, "Level Up Know Hyper Drill Two-Segment", EvolutionArgumentKindMove, "Move"),
        new(58, "Level Up Know Hyper Drill Three-Segment", EvolutionArgumentKindMove, "Move"),
        new(59, "Take Recoil Damage Male", EvolutionArgumentKindValue, "Damage"),
        new(60, "Take Recoil Damage Female", EvolutionArgumentKindValue, "Damage"),
        new(61, "Species-Specific Regional Evolution", EvolutionArgumentKindValue, "Rule"),
    ];

    private readonly SvWorkflowFileSource fileSource;

    public SvPokemonWorkflowService(SvWorkflowFileSource? fileSource = null)
    {
        this.fileSource = fileSource ?? new SvWorkflowFileSource();
    }

    public SvWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return SvWorkflowSupport.CreateSummary(
            project,
            SvWorkflowIds.Pokemon,
            WorkflowLabel,
            WorkflowDescription);
    }

    public SvPokemonWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var diagnostics = new List<ValidationDiagnostic>();
        SvWorkflowFile? source = null;
        var labels = SvTextLabelLookup.None();
        var pokemon = Array.Empty<SvPokemonRecord>();

        try
        {
            labels = SvTextLabelLookup.Load(project, fileSource, diagnostics, project.Paths);
            var tmCatalog = SvTechnicalMachineCatalog.Load(project, fileSource, labels, diagnostics);
            source = fileSource.Read(project, SvDataPaths.PersonalArray);
            pokemon = LoadRecords(source, labels, tmCatalog).ToArray();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(SvWorkflowSupport.Error(
                $"Pokemon Data could not be loaded: {exception.Message}",
                $"romfs/{SvDataPaths.PersonalArray}"));
        }

        var summary = SvWorkflowSupport.CreateSummary(
            project,
            SvWorkflowIds.Pokemon,
            WorkflowLabel,
            WorkflowDescription,
            diagnostics.Count == 0 ? null : diagnostics);

        var stats = new SvPokemonWorkflowStats(
            pokemon.Length,
            pokemon.Count(record => record.DexPresence.IsPresentInGame),
            pokemon.Sum(record => record.Evolutions.Count),
            pokemon.Sum(record => record.Learnset.Count),
            source is null ? 0 : 1);

        return new SvPokemonWorkflow(
            summary,
            pokemon,
            stats,
            CreateEvolutionOptions(pokemon, labels),
            CreateMoveOptions(pokemon, labels),
            CreateEditableFields(labels),
            diagnostics);
    }

    private static IEnumerable<SvPokemonRecord> LoadRecords(
        SvWorkflowFile source,
        SvTextLabelLookup labels,
        IReadOnlyList<SvTechnicalMachineMove> tmCatalog)
    {
        var table = global::personal_table.GetRootAspersonal_table(new ByteBuffer(source.Bytes));
        for (var index = 0; index < table.EntryLength; index++)
        {
            var entry = table.Entry(index);
            if (entry is null || !entry.Value.IsPresent)
            {
                continue;
            }

            yield return ToRecord(index, entry.Value, source, labels, tmCatalog);
        }
    }

    private static SvPokemonRecord ToRecord(
        int personalId,
        global::personal entry,
        SvWorkflowFile source,
        SvTextLabelLookup labels,
        IReadOnlyList<SvTechnicalMachineMove> tmCatalog)
    {
        var species = entry.Species;
        var baseStatsData = entry.BaseStats;
        var evYieldData = entry.EvYield;
        var genderData = entry.Gender;
        var eggHatchData = entry.EggHatch;
        var paldeaDexData = entry.PaldeaDex;
        var kitakamiDexData = entry.KitakamiDex;
        var blueberryDexData = entry.BlueberryDex;

        var speciesId = species?.Species ?? 0;
        var form = species?.Form ?? 0;
        var model = species?.Model ?? 0;
        var color = species?.Color ?? 0;
        var height = species?.Height ?? 0;
        var weight = species?.Weight ?? 0;
        var hp = baseStatsData?.Hp ?? 0;
        var attack = baseStatsData?.Atk ?? 0;
        var defense = baseStatsData?.Def ?? 0;
        var specialAttack = baseStatsData?.Spa ?? 0;
        var specialDefense = baseStatsData?.Spd ?? 0;
        var speed = baseStatsData?.Spe ?? 0;
        var evHp = evYieldData?.Hp ?? 0;
        var evAttack = evYieldData?.Atk ?? 0;
        var evDefense = evYieldData?.Def ?? 0;
        var evSpecialAttack = evYieldData?.Spa ?? 0;
        var evSpecialDefense = evYieldData?.Spd ?? 0;
        var evSpeed = evYieldData?.Spe ?? 0;
        var genderRatio = genderData?.Ratio ?? 0;
        var eggSpecies = eggHatchData?.Species ?? 0;
        var eggForm = eggHatchData?.Form ?? 0;
        var paldeaDexIndex = paldeaDexData?.Index ?? 0;
        var kitakamiDexIndex = kitakamiDexData?.Index ?? 0;
        var blueberryDexIndex = blueberryDexData?.Index ?? 0;

        var total = hp + attack + defense + specialAttack + specialDefense + speed;
        var baseExperience = SvPokemonExperience.CalculateBaseExperience(total, entry.EvoStage, entry.ExpAddend);
        var stats = new SvPokemonBaseStats(
            hp,
            attack,
            defense,
            specialAttack,
            specialDefense,
            speed,
            total);
        var abilities = new SvPokemonAbilitySet(
            entry.Ability1,
            labels.Ability(entry.Ability1),
            entry.Ability2,
            labels.Ability(entry.Ability2),
            entry.AbilityHidden,
            labels.Ability(entry.AbilityHidden));
        var dexPresence = new SvPokemonDexPresence(
            entry.IsPresent,
            paldeaDexIndex > 0 || kitakamiDexIndex > 0 || blueberryDexIndex > 0,
            paldeaDexIndex,
            kitakamiDexIndex,
            blueberryDexIndex);
        var details = new SvPokemonPersonalDetails(
            entry.Type1,
            entry.Type2,
            entry.CatchRate,
            entry.EvoStage,
            evHp,
            evAttack,
            evDefense,
            evSpecialAttack,
            evSpecialDefense,
            evSpeed,
            0,
            0,
            0,
            genderRatio,
            entry.EggHatchSteps,
            entry.BaseFriendship,
            entry.XpGrowth,
            entry.EggGroup1,
            entry.EggGroup2,
            form,
            0,
            color,
            entry.IsPresent,
            HasSpriteForm: false,
            baseExperience,
            height,
            weight,
            model,
            eggSpecies,
            eggForm,
            IsRegionalForm: false,
            paldeaDexIndex,
            form,
            kitakamiDexIndex,
            blueberryDexIndex);

        return new SvPokemonRecord(
            personalId,
            speciesId,
            form,
            labels.Pokemon(speciesId),
            form == 0
                ? "Base"
                : PokemonFormLabels.ResolveFormLabel(
                    speciesId,
                    labels.Pokemon(speciesId),
                    form,
                    PokemonFormLabelFamily.ScarletViolet)
                    ?? $"Form {form.ToString(CultureInfo.InvariantCulture)}",
            FormatType(entry.Type1),
            FormatType(entry.Type2),
            stats,
            abilities,
            dexPresence,
            details,
            entry.CatchRate,
            entry.EvoStage,
            genderRatio,
            FormatGender(genderRatio),
            baseExperience,
            height,
            weight,
            ReadEvolutions(entry, labels),
            ReadLearnset(entry, labels),
            ReadCompatibility(entry, labels, tmCatalog),
            new SvPokemonProvenance(source.RelativePath, source.SourceLayer, source.FileState));
    }

    private static IReadOnlyList<SvPokemonEvolutionRecord> ReadEvolutions(
        global::personal entry,
        SvTextLabelLookup labels)
    {
        var evolutions = new List<SvPokemonEvolutionRecord>();
        for (var index = 0; index < entry.EvolutionsLength; index++)
        {
            var evolution = entry.Evolutions(index);
            if (evolution is null || evolution.Value.Species == 0)
            {
                continue;
            }

            var method = GetEvolutionMethodDefinition(evolution.Value.Condition);
            evolutions.Add(new SvPokemonEvolutionRecord(
                index,
                evolution.Value.Condition,
                evolution.Value.Parameter,
                evolution.Value.Species,
                evolution.Value.Form,
                evolution.Value.Level,
                method.Name,
                method.ArgumentKind,
                method.ArgumentLabel,
                FormatEvolutionArgument(
                    evolution.Value.Parameter,
                    method,
                    labels)));
        }

        return evolutions;
    }

    private static IReadOnlyList<SvPokemonLearnsetMove> ReadLearnset(
        global::personal entry,
        SvTextLabelLookup labels)
    {
        var moves = new List<SvPokemonLearnsetMove>();
        for (var index = 0; index < entry.LevelupMovesLength; index++)
        {
            var learnedMove = entry.LevelupMoves(index);
            if (learnedMove is null || learnedMove.Value.Move == 0)
            {
                continue;
            }

            var rawLevel = learnedMove.Value.Level;
            moves.Add(new SvPokemonLearnsetMove(
                index,
                learnedMove.Value.Move,
                labels.Move(learnedMove.Value.Move),
                SvLearnsetLevel.ToDisplayLevel(rawLevel),
                rawLevel,
                SvLearnsetLevel.ToLevelLabel(rawLevel)));
        }

        return moves;
    }

    private static IReadOnlyList<SvPokemonCompatibilityGroup> ReadCompatibility(
        global::personal entry,
        SvTextLabelLookup labels,
        IReadOnlyList<SvTechnicalMachineMove> tmCatalog)
    {
        var groups = new List<SvPokemonCompatibilityGroup>
        {
            CreateTechnicalMachineCompatibilityGroup(entry.GetTmMovesArray(), labels, tmCatalog),
        };

        var eggMoves = entry.GetEggMovesArray();
        if (eggMoves.Length > 0)
        {
            groups.Add(CreateMoveVectorCompatibilityGroup("egg", "Egg Moves", eggMoves, labels));
        }

        var reminderMoves = entry.GetReminderMovesArray();
        if (reminderMoves.Length > 0)
        {
            groups.Add(CreateMoveVectorCompatibilityGroup("reminder", "Reminder Moves", reminderMoves, labels));
        }

        return groups;
    }

    private static SvPokemonCompatibilityGroup CreateTechnicalMachineCompatibilityGroup(
        IReadOnlyList<ushort> learnableMoves,
        SvTextLabelLookup labels,
        IReadOnlyList<SvTechnicalMachineMove> tmCatalog)
    {
        if (tmCatalog.Count == 0)
        {
            return CreateMoveVectorCompatibilityGroup(
                TechnicalMachineCompatibilityGroupId,
                "TM Moves",
                learnableMoves,
                labels,
                useMoveIdAsSlot: true);
        }

        var learnableMoveSet = learnableMoves
            .Select(move => (int)move)
            .ToHashSet();
        var entries = tmCatalog
            .Select(tm => new SvPokemonCompatibilityEntry(
                tm.MoveId,
                tm.MoveId,
                tm.MoveName,
                tm.Label,
                learnableMoveSet.Contains(tm.MoveId)))
            .ToArray();

        return new SvPokemonCompatibilityGroup(
            TechnicalMachineCompatibilityGroupId,
            "TM Moves",
            entries.Count(entry => entry.CanLearn),
            entries);
    }

    private static SvPokemonCompatibilityGroup CreateMoveVectorCompatibilityGroup(
        string id,
        string label,
        IReadOnlyList<ushort> moves,
        SvTextLabelLookup labels,
        bool useMoveIdAsSlot = false)
    {
        var entries = moves
            .Select((move, index) => new SvPokemonCompatibilityEntry(
                useMoveIdAsSlot ? move : index,
                move,
                labels.Move(move),
                $"{index + 1}. {labels.Move(move)}",
                CanLearn: true))
            .ToArray();

        return new SvPokemonCompatibilityGroup(id, label, entries.Length, entries);
    }

    private static IReadOnlyList<SvPokemonEvolutionMethodOption> CreateEvolutionOptions(
        IReadOnlyList<SvPokemonRecord> pokemon,
        SvTextLabelLookup labels)
    {
        var itemOptions = CreateIndexedOptions(labels.ItemNameCount, labels.Item, includeNone: true);
        var evolutionItemOptions = CreateEvolutionItemArgumentOptions(pokemon, labels);
        var moveOptions = CreateIndexedOptions(labels.MoveNameCount, labels.Move, includeNone: false);
        var speciesOptions = CreateIndexedOptions(labels.PokemonNameCount, labels.Pokemon, includeNone: true);

        return EvolutionMethods
            .Select(method => method.Value)
            .Concat(pokemon
            .SelectMany(record => record.Evolutions.Select(evolution => evolution.Method))
            .Append(0))
            .Distinct()
            .OrderBy(value => value)
            .Select(value =>
            {
                var definition = GetEvolutionMethodDefinition(value);
                return new SvPokemonEvolutionMethodOption(
                    value,
                    $"{value.ToString("000", CultureInfo.InvariantCulture)} {definition.Name}",
                    definition.ArgumentKind,
                    definition.ArgumentLabel,
                    CreateEvolutionArgumentOptions(
                        definition,
                        itemOptions,
                        evolutionItemOptions,
                        moveOptions,
                        speciesOptions));
            })
            .ToArray();
    }

    private static IReadOnlyList<SvPokemonEditableFieldOption> CreateMoveOptions(
        IReadOnlyList<SvPokemonRecord> pokemon,
        SvTextLabelLookup labels)
    {
        if (labels.MoveNameCount > 1)
        {
            return CreateIndexedOptions(labels.MoveNameCount, labels.Move, includeNone: false);
        }

        return pokemon
            .SelectMany(record => record.Learnset.Select(move => move.MoveId))
            .Concat(pokemon.SelectMany(record => record.Compatibility.SelectMany(group => group.Entries.Select(entry => entry.MoveId))))
            .Where(move => move > 0)
            .Distinct()
            .OrderBy(move => move)
            .Select(move => new SvPokemonEditableFieldOption(move, labels.Move(move)))
            .ToArray();
    }

    private static IReadOnlyList<SvPokemonEditableField> CreateEditableFields(SvTextLabelLookup labels)
    {
        var abilityOptions = CreateIndexedOptions(labels.AbilityNameCount, labels.Ability, includeNone: true);

        return BaseEditableFields
            .Select(field =>
            {
                var options = field.Field switch
                {
                    SvPokemonWorkflowService.Type1Field or SvPokemonWorkflowService.Type2Field => TypeOptions,
                    SvPokemonWorkflowService.Ability1Field
                        or SvPokemonWorkflowService.Ability2Field
                        or SvPokemonWorkflowService.HiddenAbilityField => abilityOptions,
                    SvPokemonWorkflowService.GenderRatioField => GenderRatioOptions,
                    SvPokemonWorkflowService.ExpGrowthField => ExpGrowthOptions,
                    SvPokemonWorkflowService.EvolutionStageField => EvolutionStageOptions,
                    SvPokemonWorkflowService.EggGroup1Field or SvPokemonWorkflowService.EggGroup2Field => EggGroupOptions,
                    SvPokemonWorkflowService.ColorField => ColorOptions,
                    _ => field.Options,
                };

                return ReferenceEquals(options, field.Options)
                    ? field
                    : field with
                    {
                        MaximumValue = options.Count > 0 ? options.Max(option => option.Value) : field.MaximumValue,
                        Options = options,
                    };
            })
            .ToArray();
    }

    private static IReadOnlyList<SvPokemonEditableFieldOption> CreateEvolutionArgumentOptions(
        EvolutionMethodDefinition method,
        IReadOnlyList<SvPokemonEditableFieldOption> itemOptions,
        IReadOnlyList<SvPokemonEditableFieldOption> evolutionItemOptions,
        IReadOnlyList<SvPokemonEditableFieldOption> moveOptions,
        IReadOnlyList<SvPokemonEditableFieldOption> speciesOptions)
    {
        return method.ArgumentKind switch
        {
            EvolutionArgumentKindItem => IsEvolutionItemParameterMethod(method.Value) ? evolutionItemOptions : itemOptions,
            EvolutionArgumentKindMove => moveOptions,
            EvolutionArgumentKindSpecies => speciesOptions,
            EvolutionArgumentKindType => TypeOptions,
            EvolutionArgumentKindValue => CreateEvolutionValueArgumentOptions(method),
            _ => [],
        };
    }

    private static IReadOnlyList<SvPokemonEditableFieldOption> CreateEvolutionItemArgumentOptions(
        IReadOnlyList<SvPokemonRecord> pokemon,
        SvTextLabelLookup labels)
    {
        return EvolutionItemParameterItemIds.Keys
            .Concat(pokemon
                .SelectMany(record => record.Evolutions)
                .Where(evolution => IsEvolutionItemParameterMethod(evolution.Method) && evolution.Argument > 0)
                .Select(evolution => evolution.Argument))
            .Distinct()
            .OrderBy(value => value)
            .Prepend(0)
            .Select(value => new SvPokemonEditableFieldOption(
                value,
                $"{value.ToString(CultureInfo.InvariantCulture)} {FormatEvolutionItemArgument(value, labels)}"))
            .ToArray();
    }

    private static IReadOnlyList<SvPokemonEditableFieldOption> CreateEvolutionValueArgumentOptions(
        EvolutionMethodDefinition method)
    {
        return method.Value switch
        {
            16 => CreateOptionList((170, "170 beauty")),
            31 => CreateOptionList(
                (0, "Standard rain rule"),
                (1, "Hisuian rain rule")),
            36 => CreateOptionList(
                (50, "Solgaleo branch"),
                (51, "Lunala branch")),
            43 => CreateOptionList((3, "3 critical hits")),
            44 => CreateOptionList((49, "49 HP lost")),
            50 => CreateOptionList((1000, "1000 steps")),
            54 => CreateOptionList((999, "999 coins")),
            55 => CreateOptionList((3, "3 leader wins")),
            56 => CreateOptionList((20, "20 Rage Fist uses")),
            59 or 60 => CreateOptionList((294, "294 recoil damage")),
            61 => CreateOptionList(
                (0, "Kleavor/Ursaluna/Wyrdeer rule"),
                (1, "Hisuian Sliggoo rain rule")),
            _ => ByteArgumentOptions,
        };
    }

    private static string FormatEvolutionArgument(
        int argument,
        EvolutionMethodDefinition method,
        SvTextLabelLookup labels)
    {
        return method.ArgumentKind switch
        {
            EvolutionArgumentKindItem => IsEvolutionItemParameterMethod(method.Value)
                ? FormatEvolutionItemArgument(argument, labels)
                : argument == 0
                    ? "None"
                    : labels.Item(argument),
            EvolutionArgumentKindMove => argument == 0 ? "None" : labels.Move(argument),
            EvolutionArgumentKindSpecies => argument == 0 ? "None" : labels.Pokemon(argument),
            EvolutionArgumentKindType => FormatType(argument),
            EvolutionArgumentKindValue => FormatEvolutionValueArgument(argument, method),
            _ => "None",
        };
    }

    private static string FormatEvolutionItemArgument(int argument, SvTextLabelLookup labels)
    {
        if (argument == 0)
        {
            return "None";
        }

        return EvolutionItemParameterItemIds.TryGetValue(argument, out var itemId)
            ? labels.Item(itemId)
            : labels.Item(argument);
    }

    private static bool IsEvolutionItemParameterMethod(int method)
    {
        return method is 8 or 17 or 18 or 19 or 20 or 42;
    }

    private static string FormatEvolutionValueArgument(int argument, EvolutionMethodDefinition method)
    {
        return method.Value switch
        {
            16 when argument == 170 => "170 beauty",
            31 when argument == 0 => "Standard rain rule",
            31 when argument == 1 => "Hisuian rain rule",
            36 when argument == 50 => "Solgaleo branch",
            36 when argument == 51 => "Lunala branch",
            43 when argument == 3 => "3 critical hits",
            44 when argument == 49 => "49 HP lost",
            50 when argument == 1000 => "1000 steps",
            54 when argument == 999 => "999 coins",
            55 when argument == 3 => "3 leader wins",
            56 when argument == 20 => "20 Rage Fist uses",
            59 or 60 when argument == 294 => "294 recoil damage",
            61 when argument == 0 => "Kleavor/Ursaluna/Wyrdeer rule",
            61 when argument == 1 => "Hisuian Sliggoo rain rule",
            _ => argument.ToString(CultureInfo.InvariantCulture),
        };
    }

    private static EvolutionMethodDefinition GetEvolutionMethodDefinition(int value)
    {
        return EvolutionMethods.FirstOrDefault(method => method.Value == value)
            ?? new EvolutionMethodDefinition(value, $"Condition {value}", EvolutionArgumentKindValue, "Parameter");
    }

    private static IReadOnlyList<SvPokemonEditableFieldOption> CreateIndexedOptions(
        int count,
        Func<int, string> resolveName,
        bool includeNone)
    {
        var firstValue = includeNone ? 0 : 1;
        if (count <= firstValue)
        {
            return includeNone ? [new(0, "0 None")] : [];
        }

        return Enumerable
            .Range(firstValue, count - firstValue)
            .Select(value =>
            {
                var label = value == 0 ? "None" : resolveName(value);
                return new SvPokemonEditableFieldOption(
                    value,
                    $"{value.ToString(CultureInfo.InvariantCulture)} {label}");
            })
            .ToArray();
    }

    private static IReadOnlyList<SvPokemonEditableFieldOption> CreateOptionList(
        params (int Value, string Label)[] options)
    {
        return options
            .Select(option => new SvPokemonEditableFieldOption(option.Value, option.Label))
            .ToArray();
    }

    private static IReadOnlyList<SvPokemonEditableFieldOption> CreateGenderRatioOptions()
    {
        return
        [
            new(0, "Always male or genderless"),
            new(31, "87.5% male / 12.5% female"),
            new(63, "75% male / 25% female"),
            new(127, "50% male / 50% female"),
            new(191, "25% male / 75% female"),
            new(225, "12.5% male / 87.5% female"),
            new(254, "Always female"),
            new(255, "Genderless"),
        ];
    }

    private static SvPokemonEditableField CreateField(
        string field,
        string label,
        string group,
        int minimumValue,
        int maximumValue,
        IReadOnlyList<SvPokemonEditableFieldOption>? options = null)
    {
        return new SvPokemonEditableField(
            field,
            label,
            group,
            "integer",
            minimumValue,
            maximumValue,
            options ?? Array.Empty<SvPokemonEditableFieldOption>());
    }

    private static string FormatType(int type)
    {
        return type switch
        {
            0 => "Normal",
            1 => "Fighting",
            2 => "Flying",
            3 => "Poison",
            4 => "Ground",
            5 => "Rock",
            6 => "Bug",
            7 => "Ghost",
            8 => "Steel",
            9 => "Fire",
            10 => "Water",
            11 => "Grass",
            12 => "Electric",
            13 => "Psychic",
            14 => "Ice",
            15 => "Dragon",
            16 => "Dark",
            17 => "Fairy",
            _ => $"Type {type}",
        };
    }

    private static string FormatGender(int ratio)
    {
        return ratio switch
        {
            0 => "Always male or genderless",
            254 => "Always female",
            255 => "Genderless",
            _ => $"{ratio}/254 female",
        };
    }

    private sealed record EvolutionMethodDefinition(
        int Value,
        string Name,
        string ArgumentKind,
        string ArgumentLabel);
}
