// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Security;
using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Core.Workflows;
using KM.Formats.ZA;
using KM.Formats.ZA.Generated.GameData;
using KM.ZA.Data;
using KM.ZA.EvolutionItems;
using KM.ZA.ExeFs;
using KM.ZA.Moves;
using KM.ZA.Workflows;

namespace KM.ZA.Pokemon;

internal sealed class ZaPokemonWorkflowService
{
    private const string WorkflowLabel = "Pokemon Data";
    private const string WorkflowDescription =
        "Edit Pokemon Legends Z-A personal data, evolutions, learnsets, and move compatibility.";
    private const string EvolutionArgumentKindNone = "none";
    private const string EvolutionArgumentKindLevel = "level";
    private const string EvolutionArgumentKindItem = "item";
    private const string EvolutionArgumentKindMove = "move";
    private const string EvolutionArgumentKindSpecies = "species";
    private const string EvolutionArgumentKindValue = "value";
    private const string EvolutionArgumentKindType = "type";
    private const int LearnsetDisplayLevelMask = 0x00FF;
    private const int LearnsetMasteryLevelShift = 8;
    private const int LearnsetMasteryLevelMask = 0xFF00;

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
    public const string GenderRatioField = "genderRatio";
    public const string HatchCyclesField = "hatchCycles";
    public const string BaseFriendshipField = "baseFriendship";
    public const string BaseExperienceField = "baseExperience";
    public const string ExpGrowthField = "expGrowth";
    public const string EggGroup1Field = "eggGroup1";
    public const string EggGroup2Field = "eggGroup2";
    public const string Ability1Field = "ability1";
    public const string Ability2Field = "ability2";
    public const string HiddenAbilityField = "hiddenAbility";
    public const string ColorField = "color";
    public const string IsPresentInGameField = "isPresentInGame";
    public const string HeightField = "height";
    public const string WeightField = "weight";
    public const string ModelIdField = "modelId";
    public const string HatchedSpeciesField = "hatchedSpecies";
    public const string RegionalDexIndexField = "regionalDexIndex";
    public const string DexPlacementField = "dexPlacement";
    public const string RegularDexKind = "regular";
    public const string HyperspaceDexKind = "hyperspace";
    public const string FormField = "form";
    public const string CompatibilityFieldPrefix = "compatibility";
    public const string TechnicalMachineCompatibilityGroupId = "tm";
    public const string EggMoveCompatibilityGroupId = "egg";
    public const string ReminderMoveCompatibilityGroupId = "reminder";
    public const string AlphaMoveField = "alphaMove";

    private static readonly IReadOnlyList<ZaPokemonEditableFieldOption> BooleanOptions =
    [
        new(0, "No"),
        new(1, "Yes"),
    ];

    private static readonly IReadOnlyList<ZaPokemonEditableField> BaseEditableFields =
    [
        CreateField(HPField, "HP", "Base Stats", 0, byte.MaxValue),
        CreateField(AttackField, "Attack", "Base Stats", 0, byte.MaxValue),
        CreateField(DefenseField, "Defense", "Base Stats", 0, byte.MaxValue),
        CreateField(SpecialAttackField, "Sp. Atk", "Base Stats", 0, byte.MaxValue),
        CreateField(SpecialDefenseField, "Sp. Def", "Base Stats", 0, byte.MaxValue),
        CreateField(SpeedField, "Speed", "Base Stats", 0, byte.MaxValue),
        CreateField(Type1Field, "Type 1", "Battle Basics", 0, 17),
        CreateField(Type2Field, "Type 2", "Battle Basics", 0, 17),
        CreateField(Ability1Field, "Ability 1", "Abilities", 0, ushort.MaxValue),
        CreateField(Ability2Field, "Ability 2", "Abilities", 0, ushort.MaxValue),
        CreateField(HiddenAbilityField, "Hidden Ability", "Abilities", 0, ushort.MaxValue),
        CreateField(CatchRateField, "Catch Rate", "Battle Basics", 0, byte.MaxValue),
        CreateField(EvolutionStageField, "Evolution Stage", "Identity", 0, 3),
        CreateField(EVYieldHPField, "HP EV Yield", "EV Yield", 0, byte.MaxValue),
        CreateField(EVYieldAttackField, "Attack EV Yield", "EV Yield", 0, byte.MaxValue),
        CreateField(EVYieldDefenseField, "Defense EV Yield", "EV Yield", 0, byte.MaxValue),
        CreateField(EVYieldSpecialAttackField, "Sp. Atk EV Yield", "EV Yield", 0, byte.MaxValue),
        CreateField(EVYieldSpecialDefenseField, "Sp. Def EV Yield", "EV Yield", 0, byte.MaxValue),
        CreateField(EVYieldSpeedField, "Speed EV Yield", "EV Yield", 0, byte.MaxValue),
        CreateField(GenderRatioField, "Gender Ratio", "Breeding", 0, byte.MaxValue),
        CreateField(HatchCyclesField, "Hatch Cycles", "Breeding", 0, byte.MaxValue),
        CreateField(BaseFriendshipField, "Base Friendship", "Battle Basics", 0, byte.MaxValue),
        CreateField(BaseExperienceField, "Base EXP", "Battle Basics", 0, null),
        CreateField(ExpGrowthField, "EXP Growth", "Battle Basics", 0, byte.MaxValue),
        CreateField(EggGroup1Field, "Egg Group 1", "Breeding", 0, byte.MaxValue),
        CreateField(EggGroup2Field, "Egg Group 2", "Breeding", 0, byte.MaxValue),
        CreateField(FormField, "Form", "Identity", 0, ushort.MaxValue),
        CreateField(ModelIdField, "Model ID", "Identity", 0, ushort.MaxValue),
        CreateField(ColorField, "Color", "Identity", 0, byte.MaxValue),
        CreateField(HeightField, "Height", "Identity", 0, ushort.MaxValue),
        CreateField(WeightField, "Weight", "Identity", 0, ushort.MaxValue),
        CreateField(HatchedSpeciesField, "Hatched Species", "Breeding", 0, ushort.MaxValue),
        CreateField(IsPresentInGameField, "Present In Game", "Flags", 0, 1, BooleanOptions),
    ];

    private static readonly IReadOnlyList<ZaPokemonEditableFieldOption> TypeOptions =
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

    private static readonly IReadOnlyList<ZaPokemonEditableFieldOption> EggGroupOptions =
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

    private static readonly IReadOnlyList<ZaPokemonEditableFieldOption> ExpGrowthOptions =
        CreateOptionList(
            (0, "Medium Fast"),
            (1, "Erratic"),
            (2, "Fluctuating"),
            (3, "Medium Slow"),
            (4, "Fast"),
            (5, "Slow"));

    private static readonly IReadOnlyList<ZaPokemonEditableFieldOption> EvolutionStageOptions =
        CreateOptionList(
            (0, "Single-stage or unevolved"),
            (1, "First evolution"),
            (2, "Second evolution"),
            (3, "Final or special stage"));

    private static readonly IReadOnlyList<ZaPokemonEditableFieldOption> ColorOptions =
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

    private static readonly IReadOnlyList<ZaPokemonEditableFieldOption> GenderRatioOptions = CreateGenderRatioOptions();

    private static readonly IReadOnlyList<ZaPokemonEditableFieldOption> ByteArgumentOptions = Enumerable
        .Range(0, 256)
        .Select(value => new ZaPokemonEditableFieldOption(value, value.ToString(CultureInfo.InvariantCulture)))
        .ToArray();

    private static readonly IReadOnlyDictionary<int, int> VanillaEvolutionItemParameterIds = new Dictionary<int, int>
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

    private static readonly IReadOnlyList<int> DefaultEvolutionItemIds =
    [
        80, 81, 82, 83, 84, 85, 107, 108, 109, 110, 326, 327, 849, 1116, 1117,
        1253, 1254, 1582, 1592, 1691, 1857, 1858, 1861, 2344, 2402, 2403, 2404, 2482,
    ];

    private static readonly IReadOnlyList<EvolutionMethodDefinition> EvolutionMethods =
    [
        new(0, "None", EvolutionArgumentKindNone, "None", UsesLevel: false),
        new(1, "Level Up Friendship", EvolutionArgumentKindNone, "None"),
        new(2, "Level Up Friendship Morning", EvolutionArgumentKindNone, "None"),
        new(3, "Level Up Friendship Night", EvolutionArgumentKindNone, "None"),
        new(4, "Level Up", EvolutionArgumentKindLevel, "Level"),
        new(5, "Trade", EvolutionArgumentKindNone, "None", UsesLevel: false),
        new(6, "Trade Held Item", EvolutionArgumentKindItem, "Item", UsesLevel: false),
        new(7, "Trade Shelmet/Karrablast", EvolutionArgumentKindNone, "None", UsesLevel: false),
        new(8, "Use Item", EvolutionArgumentKindItem, "Item", UsesLevel: false),
        new(9, "Level Up Attack > Defense", EvolutionArgumentKindLevel, "Level"),
        new(10, "Level Up Attack = Defense", EvolutionArgumentKindLevel, "Level"),
        new(11, "Level Up Defense > Attack", EvolutionArgumentKindLevel, "Level"),
        new(12, "Level Up EC < 5", EvolutionArgumentKindLevel, "Level"),
        new(13, "Level Up EC >= 5", EvolutionArgumentKindLevel, "Level"),
        new(14, "Level Up Ninjask", EvolutionArgumentKindLevel, "Level"),
        new(15, "Level Up Shedinja", EvolutionArgumentKindLevel, "Level"),
        new(16, "Level Up Beauty", EvolutionArgumentKindValue, "Beauty"),
        new(17, "Use Item Male", EvolutionArgumentKindItem, "Item", UsesLevel: false),
        new(18, "Use Item Female", EvolutionArgumentKindItem, "Item", UsesLevel: false),
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
        new(35, "Unused", EvolutionArgumentKindNone, "None", UsesLevel: false),
        new(36, "Level Up Version", EvolutionArgumentKindValue, "Version branch"),
        new(37, "Level Up Version Day", EvolutionArgumentKindValue, "Version"),
        new(38, "Level Up Version Night", EvolutionArgumentKindValue, "Version"),
        new(39, "Level Up Summit", EvolutionArgumentKindLevel, "Level"),
        new(40, "Level Up Dusk", EvolutionArgumentKindLevel, "Level"),
        new(41, "Level Up Wormhole", EvolutionArgumentKindLevel, "Level"),
        new(42, "Use Item Wormhole", EvolutionArgumentKindItem, "Item", UsesLevel: false),
        new(43, "Critical Hits In Battle", EvolutionArgumentKindValue, "Critical hits", UsesLevel: false),
        new(44, "HP Lost In Battle", EvolutionArgumentKindValue, "HP lost", UsesLevel: false),
        new(45, "Spin", EvolutionArgumentKindNone, "None", UsesLevel: false),
        new(46, "Level Up Nature Amped", EvolutionArgumentKindNone, "None"),
        new(47, "Level Up Nature Low Key", EvolutionArgumentKindNone, "None"),
        new(48, "Tower Of Darkness", EvolutionArgumentKindNone, "None", UsesLevel: false),
        new(49, "Tower Of Waters", EvolutionArgumentKindNone, "None", UsesLevel: false),
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
        new(61, "Species-Specific Regional Evolution", EvolutionArgumentKindValue, "Rule", UsesLevel: false),
        new(62, "Use Barb Barrage 20 Times", EvolutionArgumentKindValue, "Uses", UsesLevel: false),
    ];

    private readonly ZaWorkflowFileSource fileSource;
    private readonly Func<string, byte[]> readExecutableBytes;
    private readonly ProjectWorkflowMemoryCache<ZaPokemonWorkflow> memoryCache = new();

    public ZaPokemonWorkflowService(
        ZaWorkflowFileSource? fileSource = null,
        Func<string, byte[]>? readExecutableBytes = null)
    {
        this.fileSource = fileSource ?? new ZaWorkflowFileSource();
        this.readExecutableBytes = readExecutableBytes ?? File.ReadAllBytes;
    }

    public ZaWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return ZaWorkflowSupport.CreateSummary(
            project,
            ZaWorkflowIds.Pokemon,
            WorkflowLabel,
            WorkflowDescription);
    }

    public ZaPokemonWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (memoryCache.TryGet(project.Paths, out var cachedWorkflow))
        {
            return cachedWorkflow!;
        }

        var workflow = LoadUncached(project, includeDexEditor: true);
        memoryCache.Set(project.Paths, workflow);
        return workflow;
    }

    internal ZaPokemonWorkflow LoadGameModuleReadOnly(
        OpenedProject project,
        bool includeDexEditor = false)
    {
        ArgumentNullException.ThrowIfNull(project);
        return LoadUncached(project, includeDexEditor);
    }

    public void ClearMemoryCache()
    {
        memoryCache.Clear();
    }

    private ZaPokemonWorkflow LoadUncached(OpenedProject project, bool includeDexEditor)
    {

        var diagnostics = new List<ValidationDiagnostic>();
        ZaWorkflowFile? source = null;
        ZaWorkflowFile? pokedexSource = null;
        ZaWorkflowFile? pokedexMegaSource = null;
        ZaWorkflowFile? alphaMoveSource = null;
        var labels = ZaTextLabelLookup.None();
        var pokemon = Array.Empty<ZaPokemonRecord>();
        ZaPokemonDexEditor? dexEditor = null;
        IReadOnlyList<ZaTechnicalMachineMove> tmCatalog = [];
        IReadOnlyDictionary<int, string> evolutionItemArgumentLabels = CreateDefaultEvolutionItemArgumentLabels(labels);

        try
        {
            labels = ZaTextLabelLookup.Load(project, fileSource, diagnostics, project.Paths);
            var spriteLabels = ZaTextLabelLookup.Load(project, fileSource, new List<ValidationDiagnostic>());
            evolutionItemArgumentLabels = LoadEvolutionItemArgumentLabels(project, labels, diagnostics);
            tmCatalog = ZaTechnicalMachineCatalog.Load(project, fileSource, labels, diagnostics);

            source = fileSource.Read(project, ZaDataPaths.PersonalArray);
            var requiresLegacyPersonalRecovery = ZaPersonalTable
                .GetRootAsZaPersonalTable(new ByteBuffer(source.Bytes))
                .HasLegacyByteZADexOrderLayout;
            ZaEvolutionItemConversionState? conversionState = null;
            try
            {
                conversionState = ZaEvolutionItemConversionState.Load(project, fileSource);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
            {
                // Output-only fixtures and incomplete projects retain the guarded vanilla fallback below.
            }
            ZaWorkflowFile? baseSource = null;
            try
            {
                if (conversionState is null || requiresLegacyPersonalRecovery)
                {
                    baseSource = fileSource.ReadBase(project, ZaDataPaths.PersonalArray);
                    if (requiresLegacyPersonalRecovery
                        && ZaPersonalTable
                            .GetRootAsZaPersonalTable(new ByteBuffer(baseSource.Bytes))
                            .HasLegacyByteZADexOrderLayout)
                    {
                        throw new InvalidDataException(
                            "The configured base Pokemon personal table also uses the malformed byte layout.");
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
            {
                if (requiresLegacyPersonalRecovery)
                {
                    throw new InvalidDataException(
                        $"Legacy Pokemon personal output needs readable clean base data for complete recovery: {exception.Message}",
                    exception);
                }
            }
            if (requiresLegacyPersonalRecovery)
            {
                diagnostics.Add(ZaWorkflowSupport.Warning(
                    "This output uses an older malformed Pokemon layout. KM recovered it from clean base data for editing. Apply a Pokemon Data change once to rewrite the output safely.",
                    $"romfs/{ZaDataPaths.PersonalArray}"));
            }

            try
            {
                pokemon = LoadRecords(
                    source,
                    baseSource,
                    conversionState,
                    labels,
                    spriteLabels,
                    tmCatalog,
                    evolutionItemArgumentLabels).ToArray();
            }
            catch (Exception exception) when (
                exception is IndexOutOfRangeException
                    or ArgumentOutOfRangeException
                    or OverflowException
                    or InvalidDataException
                    or FormatException
                    or InvalidOperationException
                    or NullReferenceException)
            {
                throw new ProjectFileOperationException(
                    ProjectFileOperation.Decode,
                    source.RelativePath,
                    source.SourceLayer,
                    state: null,
                    innerException: exception);
            }
            pokemon = AttachVanillaYieldDefaults(project, pokemon);

            try
            {
                alphaMoveSource = fileSource.Read(project, ZaDataPaths.AlphaMoveTable);
                var baseAlphaMoveSource = fileSource.ReadBase(project, ZaDataPaths.AlphaMoveTable);
                var battleSource = fileSource.Read(project, ZaDataPaths.BattleMoveParameterArray);
                var baseBattleSource = fileSource.ReadBase(project, ZaDataPaths.BattleMoveParameterArray);
                var itemSource = fileSource.Read(project, ZaDataPaths.ItemDataArray);
                var baseItemSource = fileSource.ReadBase(project, ZaDataPaths.ItemDataArray);
                pokemon = ProjectAlphaMoves(
                    pokemon,
                    labels,
                    tmCatalog,
                    source,
                    alphaMoveSource,
                    baseAlphaMoveSource,
                    battleSource,
                    baseBattleSource,
                    itemSource,
                    baseItemSource);
            }
            catch (Exception exception) when (
                exception is IOException
                    or InvalidDataException
                    or InvalidOperationException
                    or ArgumentException
                    or OverflowException
                    or IndexOutOfRangeException)
            {
                var blockedReason =
                    $"Alpha-exclusive move editing is unavailable because its sources could not be verified: {exception.Message}";
                pokemon = pokemon
                    .Select(record => record with
                    {
                        AlphaMove = CreateUnavailableAlphaMove(blockedReason),
                    })
                    .ToArray();
                diagnostics.Add(ZaWorkflowSupport.Warning(
                    blockedReason,
                    $"romfs/{ZaDataPaths.AlphaMoveTable}") with
                {
                    Code = ZaPokemonDiagnosticCodes.AlphaSourceUnavailable,
                });
            }

            if (includeDexEditor)
            {
                try
                {
                    pokedexSource = fileSource.Read(project, ZaDataPaths.PokedexContentsData);
                    pokedexMegaSource = fileSource.Read(project, ZaDataPaths.PokedexMegaContentsData);
                    dexEditor = AddVanillaLayoutStatus(
                        project,
                        CreateDexEditor(
                            project,
                            pokemon,
                            source,
                            pokedexSource,
                            pokedexMegaSource));
                }
                catch (Exception exception) when (
                    exception is IOException or InvalidDataException or ArgumentException or OverflowException)
                {
                    var blockedReason =
                        $"Pokédex placement is unavailable because its active slot mapping could not be verified: {exception.Message}";
                    dexEditor = CreateBlockedDexEditor(
                        project,
                        blockedReason,
                        source,
                        pokedexSource,
                        pokedexMegaSource);
                    diagnostics.Add(ZaWorkflowSupport.Warning(
                        blockedReason,
                        $"romfs/{ZaDataPaths.PokedexContentsData}"));
                }
            }
        }
        catch (ProjectFileOperationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(ZaWorkflowSupport.Error(
                $"Pokemon Data could not be loaded: {exception.Message}",
                $"romfs/{ZaDataPaths.PersonalArray}"));
        }

        var summary = ZaWorkflowSupport.CreateSummary(
            project,
            ZaWorkflowIds.Pokemon,
            WorkflowLabel,
            WorkflowDescription,
            diagnostics.Count == 0 ? null : diagnostics);

        var stats = new ZaPokemonWorkflowStats(
            pokemon.Length,
            pokemon.Count(record => record.DexPresence.IsPresentInGame),
            pokemon.Sum(record => record.Evolutions.Count),
            pokemon.Sum(record => record.Learnset.Count),
            (source is null ? 0 : 1)
                + (pokedexSource is null ? 0 : 1)
                + (pokedexMegaSource is null ? 0 : 1)
                + (alphaMoveSource is null ? 0 : 1));

        return new ZaPokemonWorkflow(
            summary,
            pokemon,
            stats,
            CreateEvolutionOptions(pokemon, labels, evolutionItemArgumentLabels),
            CreateMoveOptions(pokemon, labels),
            CreateEditableFields(labels),
            diagnostics,
            dexEditor);
    }

    private ZaPokemonRecord[] ProjectAlphaMoves(
        IReadOnlyList<ZaPokemonRecord> pokemon,
        ZaTextLabelLookup labels,
        IReadOnlyList<ZaTechnicalMachineMove> tmCatalog,
        ZaWorkflowFile personalSource,
        ZaWorkflowFile alphaMoveSource,
        ZaWorkflowFile baseAlphaMoveSource,
        ZaWorkflowFile battleSource,
        ZaWorkflowFile baseBattleSource,
        ZaWorkflowFile itemSource,
        ZaWorkflowFile baseItemSource)
    {
        var activeTable = ZaAlphaMoveTableDocument.Parse(
            alphaMoveSource.Bytes,
            fileSource.BoundedTableRecordLimit,
            fileSource.BoundedNestedRecordLimit);
        var vanillaTable = ZaAlphaMoveTableDocument.Parse(
            baseAlphaMoveSource.Bytes,
            fileSource.BoundedTableRecordLimit,
            fileSource.BoundedNestedRecordLimit);
        var activePlusMoves = CreateValidPlusMoveIds(battleSource.Bytes);
        var vanillaPlusMoves = CreateValidPlusMoveIds(baseBattleSource.Bytes);
        var physicalTechnicalMachines = tmCatalog
            .GroupBy(move => move.MoveId)
            .ToDictionary(group => group.Key, group => group.First());
        var editSources = new[]
            {
                personalSource,
                alphaMoveSource,
                baseAlphaMoveSource,
                battleSource,
                baseBattleSource,
                itemSource,
                baseItemSource,
            }
            .Select(ZaWorkflowFileSource.CreateReference)
            .Distinct()
            .OrderBy(reference => reference.Layer)
            .ThenBy(reference => reference.RelativePath, StringComparer.Ordinal)
            .ToArray();
        var provenance = new ZaPokemonProvenance(
            alphaMoveSource.RelativePath,
            alphaMoveSource.SourceLayer,
            alphaMoveSource.FileState);
        var sharedOptions = pokemon
            .GroupBy(record => (record.SpeciesId, record.Form))
            .ToDictionary(
                group => group.Key,
                group => CreateSharedAlphaMoveOptions(
                    group,
                    physicalTechnicalMachines,
                    activePlusMoves,
                    vanillaPlusMoves));

        return pokemon
            .Select(record => record with
            {
                AlphaMove = CreateAlphaMove(
                    record,
                    labels,
                    sharedOptions[(record.SpeciesId, record.Form)],
                    activeTable,
                    vanillaTable,
                    provenance,
                    editSources),
            })
            .ToArray();
    }

    private IReadOnlySet<int> CreateValidPlusMoveIds(byte[] battleBytes)
    {
        return ZaRuntimeMoveData
            .BattleRows(ZaRuntimeMoveData.ReadBattle(
                battleBytes,
                fileSource.BoundedTableRecordLimit,
                fileSource.BoundedNestedRecordLimit))
            .Where(row => row.MoveId > 0 && row.MoveId <= int.MaxValue)
            .GroupBy(row => row.MoveId)
            .Where(group => group.Count(row => row.VariantType == 1) == 1)
            .Select(group => checked((int)group.Key))
            .ToHashSet();
    }

    private static IReadOnlyList<ZaPokemonEditableFieldOption> CreateSharedAlphaMoveOptions(
        IEnumerable<ZaPokemonRecord> pokemon,
        IReadOnlyDictionary<int, ZaTechnicalMachineMove> physicalTechnicalMachines,
        IReadOnlySet<int> activePlusMoves,
        IReadOnlySet<int> vanillaPlusMoves)
    {
        HashSet<int>? sharedCompatibility = null;
        foreach (var record in pokemon)
        {
            var compatibleMoves = record.Compatibility
                .FirstOrDefault(group => string.Equals(
                    group.GroupId,
                    TechnicalMachineCompatibilityGroupId,
                    StringComparison.Ordinal))
                ?.Entries
                .Where(entry => entry.CanLearn)
                .Select(entry => entry.MoveId)
                .ToHashSet()
                ?? [];
            if (sharedCompatibility is null)
            {
                sharedCompatibility = compatibleMoves;
            }
            else
            {
                sharedCompatibility.IntersectWith(compatibleMoves);
            }
        }

        if (sharedCompatibility is null || sharedCompatibility.Count == 0)
        {
            return [];
        }

        return physicalTechnicalMachines.Values
            .Where(move => sharedCompatibility.Contains(move.MoveId))
            .Where(move => activePlusMoves.Contains(move.MoveId))
            .Where(move => vanillaPlusMoves.Contains(move.MoveId))
            .OrderBy(move => move.Slot)
            .ThenBy(move => move.MoveId)
            .Select(move => new ZaPokemonEditableFieldOption(move.MoveId, move.MoveName))
            .ToArray();
    }

    private static ZaPokemonAlphaMove CreateAlphaMove(
        ZaPokemonRecord pokemon,
        ZaTextLabelLookup labels,
        IReadOnlyList<ZaPokemonEditableFieldOption> options,
        ZaAlphaMoveTableDocument activeTable,
        ZaAlphaMoveTableDocument vanillaTable,
        ZaPokemonProvenance provenance,
        IReadOnlyList<ProjectFileReference> editSources)
    {
        if (pokemon.SpeciesId is < 0 or > ushort.MaxValue
            || pokemon.Form is < 0 or > ushort.MaxValue)
        {
            return CreateUnavailableAlphaMove(
                "This species or form is outside the supported alpha-exclusive move key range.");
        }

        var speciesId = checked((ushort)pokemon.SpeciesId);
        var formId = checked((ushort)pokemon.Form);
        var active = activeTable.FindFirstExact(speciesId, formId);
        var vanilla = vanillaTable.FindFirstExact(speciesId, formId);
        if (active is null)
        {
            return new ZaPokemonAlphaMove(
                HasMapping: false,
                MoveId: null,
                MoveName: null,
                VanillaMoveId: vanilla?.MoveId,
                VanillaMoveName: vanilla is null ? null : labels.Move(vanilla.MoveId),
                CanEdit: false,
                BlockedReason:
                    "No alpha-exclusive move mapping exists for this species and form. Adding mappings is not supported.",
                DiffersFromVanilla: false,
                CanRevertToVanilla: false,
                RestoreBlockedReason: "There is no existing mapping to restore.",
                Options: [],
                Provenance: provenance)
            {
                EditSources = editSources,
            };
        }

        var activeMoveId = (int)active.MoveId;
        var vanillaMoveId = vanilla is null ? null : (int?)vanilla.MoveId;

        string? blockedReason = null;
        if (!active.HasMoveId)
        {
            blockedReason = "The existing alpha-exclusive move field is not materialized and cannot be patched safely.";
        }
        else if (vanilla is null || !vanilla.HasMoveId)
        {
            blockedReason = "The clean alpha-exclusive move mapping could not be matched exactly.";
        }
        else if (options.Count == 0)
        {
            blockedReason = "No verified TM-compatible move with active and clean Plus data is available for this species and form.";
        }

        var canEdit = blockedReason is null;
        var differsFromVanilla = vanillaMoveId is not null && activeMoveId != vanillaMoveId.Value;
        var vanillaIsSafe = vanillaMoveId is not null
            && options.Any(option => option.Value == vanillaMoveId.Value);
        var canRevert = canEdit && differsFromVanilla && vanillaIsSafe;
        var restoreBlockedReason = canRevert
            ? null
            : !differsFromVanilla
                ? "This mapping already matches vanilla."
                : !vanillaIsSafe
                    ? "The vanilla move is not currently a verified TM-compatible move with Plus data."
                    : blockedReason;

        return new ZaPokemonAlphaMove(
            HasMapping: true,
            activeMoveId,
            labels.Move(activeMoveId),
            vanillaMoveId,
            vanillaMoveId is null ? null : labels.Move(vanillaMoveId.Value),
            canEdit,
            blockedReason,
            differsFromVanilla,
            canRevert,
            restoreBlockedReason,
            options,
            provenance)
        {
            EditSources = editSources,
        };
    }

    private static ZaPokemonAlphaMove CreateUnavailableAlphaMove(string blockedReason)
    {
        return new ZaPokemonAlphaMove(
            HasMapping: false,
            MoveId: null,
            MoveName: null,
            VanillaMoveId: null,
            VanillaMoveName: null,
            CanEdit: false,
            blockedReason,
            DiffersFromVanilla: false,
            CanRevertToVanilla: false,
            RestoreBlockedReason: blockedReason,
            Options: [],
            Provenance: null);
    }

    private ZaPokemonDexEditor CreateDexEditor(
        OpenedProject project,
        IReadOnlyList<ZaPokemonRecord> pokemon,
        ZaWorkflowFile personalSource,
        ZaWorkflowFile contentsSource,
        ZaWorkflowFile megaContentsSource)
    {
        var contents = ZaPokedexContentsTable.Read(
            contentsSource.Bytes,
            fileSource.BoundedTableRecordLimit);
        var megaContents = ZaPokedexMegaContentsTable.Read(
            megaContentsSource.Bytes,
            fileSource.BoundedTableRecordLimit);
        var activeSpecies = pokemon
            .Where(record => record.DexPresence.IsPresentInGame)
            .GroupBy(record => record.SpeciesId)
            .ToArray();
        if (activeSpecies.Length == 0)
        {
            throw new InvalidDataException("No active Pokédex species were found.");
        }

        var indexBySpecies = new Dictionary<int, int>(activeSpecies.Length);
        var representativeBySpecies = new Dictionary<int, ZaPokemonRecord>(activeSpecies.Length);
        foreach (var speciesGroup in activeSpecies)
        {
            var indices = speciesGroup
                .Select(record => record.DexPresence.RegionalDexIndex)
                .Distinct()
                .ToArray();
            if (indices.Length != 1 || indices[0] <= 0)
            {
                throw new InvalidDataException(
                    $"Species {speciesGroup.Key} does not have one shared positive Pokédex slot across its active forms.");
            }

            indexBySpecies.Add(speciesGroup.Key, indices[0]);
            representativeBySpecies.Add(
                speciesGroup.Key,
                speciesGroup
                    .OrderBy(record => record.Form == 0 ? 0 : 1)
                    .ThenBy(record => record.Form)
                    .ThenBy(record => record.PersonalId)
                    .First());
        }

        var contentRows = contents.Rows.ToArray();
        if (contentRows.Any(row => !row.HasKnownGroup))
        {
            throw new InvalidDataException("The contents table contains an unsupported Pokédex group.");
        }

        var activeSpeciesIds = indexBySpecies.Keys.Order().ToArray();
        var contentSpeciesIds = contentRows.Select(row => row.Species).Order().ToArray();
        if (!activeSpeciesIds.SequenceEqual(contentSpeciesIds))
        {
            throw new InvalidDataException(
                "The contents table does not exactly cover the active personal-data species.");
        }

        var unsupportedMegaSpecies = megaContents.Rows
            .Select(row => row.Species)
            .Distinct()
            .Except(activeSpeciesIds)
            .Order()
            .ToArray();
        if (unsupportedMegaSpecies.Length > 0)
        {
            throw new InvalidDataException(
                "The Mega Pokédex contents table references species outside the active Pokédex mapping.");
        }

        var orderedIndices = indexBySpecies.Values.Order().ToArray();
        if (!orderedIndices.SequenceEqual(Enumerable.Range(1, indexBySpecies.Count)))
        {
            throw new InvalidDataException(
                "Active Pokédex slots must be unique and contiguous.");
        }

        var groupBySpecies = contentRows.ToDictionary(row => row.Species, row => row.Group);
        var canSyncMegasToRegular = megaContents.Rows.Any(row =>
            row.Group != groupBySpecies[row.Species]);
        var regularIndices = indexBySpecies
            .Where(pair => groupBySpecies[pair.Key] == (int)ZaPokedexContentsGroup.Regular)
            .Select(pair => pair.Value)
            .Order()
            .ToArray();
        var hyperspaceIndices = indexBySpecies
            .Where(pair => groupBySpecies[pair.Key] == (int)ZaPokedexContentsGroup.Hyperspace)
            .Select(pair => pair.Value)
            .Order()
            .ToArray();
        if (regularIndices.Length == 0
            || hyperspaceIndices.Length == 0
            || !regularIndices.SequenceEqual(Enumerable.Range(1, regularIndices.Length))
            || !hyperspaceIndices.SequenceEqual(
                Enumerable.Range(regularIndices.Length + 1, hyperspaceIndices.Length)))
        {
            throw new InvalidDataException(
                "Regular and Hyperspace species do not occupy one verified contiguous slot range each.");
        }

        var placements = indexBySpecies
            .Select(pair =>
            {
                var group = groupBySpecies[pair.Key];
                var isRegular = group == (int)ZaPokedexContentsGroup.Regular;
                var displayedNumber = isRegular
                    ? pair.Value
                    : pair.Value - regularIndices.Length;
                var dexKind = isRegular ? RegularDexKind : HyperspaceDexKind;
                var representative = representativeBySpecies[pair.Key];
                return new ZaPokemonDexPlacement(
                    pair.Key,
                    pair.Value,
                    dexKind,
                    displayedNumber,
                    representative.Name);
            })
            .OrderBy(placement => placement.InternalIndex)
            .ToArray();

        var executable = AnalyzeDexExecutable(
            project,
            regularIndices.Length,
            activeSpecies.Length);
        return new ZaPokemonDexEditor(
            CanEdit: true,
            BlockedReason: null,
            CanEditAdvanced: executable.CanEditAdvanced,
            AdvancedBlockedReason: executable.AdvancedBlockedReason,
            IsVanillaLayout: false,
            CanReturnToVanilla: false,
            ReturnToVanillaBlockedReason: null,
            CanSyncMegasToRegular: canSyncMegasToRegular,
            ExecutableBuildId: executable.BuildId,
            ExecutableRegularCount: executable.RegularCount,
            RegularCount: regularIndices.Length,
            HyperspaceCount: hyperspaceIndices.Length,
            Placements: placements,
            PersonalProvenance: ToProvenance(personalSource),
            ContentsProvenance: ToProvenance(contentsSource),
            MegaContentsProvenance: ToProvenance(megaContentsSource),
            ExecutableProvenance: executable.Provenance,
            VanillaLayoutFingerprint: null);
    }

    private ZaPokemonDexEditor CreateBlockedDexEditor(
        OpenedProject project,
        string blockedReason,
        ZaWorkflowFile? personalSource,
        ZaWorkflowFile? contentsSource,
        ZaWorkflowFile? megaContentsSource)
    {
        var executable = AnalyzeDexExecutable(
            project,
            expectedRegularCount: null,
            activeSpeciesCount: null);
        return new ZaPokemonDexEditor(
            CanEdit: false,
            BlockedReason: blockedReason,
            CanEditAdvanced: false,
            AdvancedBlockedReason: executable.AdvancedBlockedReason,
            IsVanillaLayout: false,
            CanReturnToVanilla: false,
            ReturnToVanillaBlockedReason: blockedReason,
            CanSyncMegasToRegular: false,
            ExecutableBuildId: executable.BuildId,
            ExecutableRegularCount: executable.RegularCount,
            RegularCount: 0,
            HyperspaceCount: 0,
            Placements: Array.Empty<ZaPokemonDexPlacement>(),
            PersonalProvenance: personalSource is null ? null : ToProvenance(personalSource),
            ContentsProvenance: contentsSource is null ? null : ToProvenance(contentsSource),
            MegaContentsProvenance: megaContentsSource is null ? null : ToProvenance(megaContentsSource),
            ExecutableProvenance: executable.Provenance,
            VanillaLayoutFingerprint: null);
    }

    private ZaPokemonDexEditor AddVanillaLayoutStatus(
        OpenedProject project,
        ZaPokemonDexEditor editor)
    {
        try
        {
            var vanilla = ZaDexLayoutStateReader.ReadBase(project, fileSource);
            if (vanilla.RegularCount != ZaDexLayoutMainPatcher.VanillaRegularCount)
            {
                throw new InvalidDataException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The verified base Pokédex uses Regular boundary {vanilla.RegularCount}, "
                        + $"not the expected vanilla boundary {ZaDexLayoutMainPatcher.VanillaRegularCount}."));
            }

            var currentAssignments = editor.Placements.ToDictionary(
                placement => placement.SpeciesId,
                placement => placement.InternalIndex);
            if (!currentAssignments.Keys.Order().SequenceEqual(
                    vanilla.Assignments.Keys.Order()))
            {
                throw new InvalidDataException(
                    "The effective and base Pokédexes do not contain the same active species.");
            }

            var vanillaFingerprint = vanilla.Fingerprint;
            var assignmentsMatchVanilla = string.Equals(
                ZaDexLayoutStateReader.CreateFingerprint(
                    editor.RegularCount,
                    currentAssignments),
                vanillaFingerprint,
                StringComparison.Ordinal);
            var currentMegaContents = fileSource.Read(
                project,
                ZaDataPaths.PokedexMegaContentsData);
            var isVanilla = assignmentsMatchVanilla
                && currentMegaContents.Bytes.AsSpan()
                    .SequenceEqual(vanilla.MegaContentsSource.Bytes);
            string? blockedReason = null;
            if (!isVanilla
                && editor.RegularCount != vanilla.RegularCount
                && (!editor.CanEditAdvanced || editor.ExecutableProvenance is null))
            {
                blockedReason = editor.AdvancedBlockedReason
                    ?? "Returning Dex Layout to vanilla requires a verified matching Pokemon Legends Z-A exefs/main.";
            }

            return editor with
            {
                IsVanillaLayout = isVanilla,
                CanReturnToVanilla = !isVanilla && blockedReason is null,
                ReturnToVanillaBlockedReason = blockedReason,
                CanSyncMegasToRegular = !isVanilla && editor.CanSyncMegasToRegular,
                VanillaLayoutFingerprint = vanillaFingerprint,
            };
        }
        catch (Exception exception) when (
            exception is IOException
                or InvalidDataException
                or InvalidOperationException
                or SecurityException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException
                or OverflowException)
        {
            return editor with
            {
                IsVanillaLayout = false,
                CanReturnToVanilla = false,
                ReturnToVanillaBlockedReason =
                    $"Returning Dex Layout to vanilla is unavailable because the base layout could not be verified: {exception.Message}",
                VanillaLayoutFingerprint = null,
            };
        }
    }

    private DexExecutableStatus AnalyzeDexExecutable(
        OpenedProject project,
        int? expectedRegularCount,
        int? activeSpeciesCount)
    {
        ZaExeFsMainFile? effectiveMain;
        try
        {
            effectiveMain = ZaExeFsMainFileResolver.ResolveEffective(project);
        }
        catch (Exception exception) when (
            exception is IOException
                or InvalidDataException
                or InvalidOperationException
                or SecurityException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
        {
            return new DexExecutableStatus(
                CanEditAdvanced: false,
                AdvancedBlockedReason: $"Changing Pokédex sizes is unavailable because exefs/main could not be resolved: {exception.Message}",
                BuildId: null,
                RegularCount: null,
                Provenance: null);
        }

        if (effectiveMain is null)
        {
            return new DexExecutableStatus(
                CanEditAdvanced: false,
                AdvancedBlockedReason: "Changing the Regular and Hyperspace Pokédex sizes requires a readable Pokemon Legends Z-A exefs/main.",
                BuildId: null,
                RegularCount: null,
                Provenance: null);
        }

        var provenance = new ZaPokemonProvenance(
            effectiveMain.Reference.RelativePath,
            effectiveMain.Reference.Layer,
            effectiveMain.FileState);
        try
        {
            var analysis = ZaDexLayoutMainPatcher.Analyze(
                readExecutableBytes(effectiveMain.AbsolutePath),
                project.Paths.SelectedGame);
            if (analysis.Kind is ZaDexLayoutMainKind.UnsupportedBuild
                or ZaDexLayoutMainKind.GameMismatch
                or ZaDexLayoutMainKind.Conflict
                || analysis.RegularCount is null)
            {
                return new DexExecutableStatus(
                    CanEditAdvanced: false,
                    AdvancedBlockedReason: analysis.Message,
                    BuildId: analysis.BuildId,
                    RegularCount: analysis.RegularCount,
                    Provenance: provenance);
            }

            if (activeSpeciesCount != ZaDexLayoutMainPatcher.TotalDexSpeciesCount)
            {
                return new DexExecutableStatus(
                    CanEditAdvanced: false,
                    AdvancedBlockedReason: string.Create(
                        CultureInfo.InvariantCulture,
                        $"Changing Pokédex sizes requires exactly {ZaDexLayoutMainPatcher.TotalDexSpeciesCount} verified active species."),
                    BuildId: analysis.BuildId,
                    RegularCount: analysis.RegularCount,
                    Provenance: provenance);
            }

            if (expectedRegularCount is null
                || analysis.RegularCount.Value != expectedRegularCount.Value)
            {
                return new DexExecutableStatus(
                    CanEditAdvanced: false,
                    AdvancedBlockedReason: expectedRegularCount is null
                        ? "Changing Pokédex sizes is unavailable until the active placement data can be verified."
                        : string.Create(
                            CultureInfo.InvariantCulture,
                            $"Pokédex contents use a Regular Dex boundary of {expectedRegularCount.Value}, "
                            + $"but exefs/main uses {analysis.RegularCount.Value}. Repair the mismatch before changing Pokédex sizes."),
                    BuildId: analysis.BuildId,
                    RegularCount: analysis.RegularCount,
                    Provenance: provenance);
            }

            var baseMain = ZaExeFsMainFileResolver.ResolveBase(project);
            if (baseMain is null)
            {
                return new DexExecutableStatus(
                    CanEditAdvanced: false,
                    AdvancedBlockedReason: "Changing Pokédex sizes requires a readable clean base exefs/main for guarded patch verification.",
                    BuildId: analysis.BuildId,
                    RegularCount: analysis.RegularCount,
                    Provenance: provenance);
            }

            var baseAnalysis = ZaDexLayoutMainPatcher.Analyze(
                readExecutableBytes(baseMain.AbsolutePath),
                project.Paths.SelectedGame);
            if (baseAnalysis.Kind != ZaDexLayoutMainKind.Vanilla
                || baseAnalysis.RegularCount != ZaDexLayoutMainPatcher.VanillaRegularCount
                || !string.Equals(
                    baseAnalysis.BuildId,
                    analysis.BuildId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new DexExecutableStatus(
                    CanEditAdvanced: false,
                    AdvancedBlockedReason: "Changing Pokédex sizes requires the verified clean base exefs/main with the vanilla Regular Dex boundary.",
                    BuildId: analysis.BuildId,
                    RegularCount: analysis.RegularCount,
                    Provenance: provenance);
            }

            return new DexExecutableStatus(
                CanEditAdvanced: true,
                AdvancedBlockedReason: null,
                BuildId: analysis.BuildId,
                RegularCount: analysis.RegularCount,
                Provenance: provenance);
        }
        catch (Exception exception) when (
            exception is IOException
                or InvalidDataException
                or InvalidOperationException
                or SecurityException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
        {
            return new DexExecutableStatus(
                CanEditAdvanced: false,
                AdvancedBlockedReason: $"Changing Pokédex sizes is unavailable because exefs/main could not be verified: {exception.Message}",
                BuildId: null,
                RegularCount: null,
                Provenance: provenance);
        }
    }

    private static ZaPokemonProvenance ToProvenance(ZaWorkflowFile source)
    {
        return new ZaPokemonProvenance(
            source.RelativePath,
            source.SourceLayer,
            source.FileState);
    }

    private sealed record DexExecutableStatus(
        bool CanEditAdvanced,
        string? AdvancedBlockedReason,
        string? BuildId,
        int? RegularCount,
        ZaPokemonProvenance? Provenance);

    private IReadOnlyDictionary<int, string> LoadEvolutionItemArgumentLabels(
        OpenedProject project,
        ZaTextLabelLookup labels,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        try
        {
            var argumentLabels = new Dictionary<int, string>();
            var source = fileSource.Read(project, ZaDataPaths.ItemDataArray);
            var table = ZaItemDataArray.GetRootAsZaItemDataArray(new ByteBuffer(source.Bytes));
            fileSource.EnsureBoundedTableCount(table.ValuesLength, "The Z-A evolution-item table");
            for (var index = 0; index < table.ValuesLength; index++)
            {
                if (table.Values(index) is not { } item)
                {
                    continue;
                }

                if (item.WorkEvolutional && item.Id > 0)
                {
                    argumentLabels[item.Id] = labels.Item(item.Id);
                }
            }

            return argumentLabels;
        }
        catch (FileNotFoundException)
        {
            return CreateDefaultEvolutionItemArgumentLabels(labels);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(ZaWorkflowSupport.Warning(
                $"Item metadata could not be decoded; edited evolution items may be missing from Use Item selectors: {exception.Message}",
                $"romfs/{ZaDataPaths.ItemDataArray}"));
            return CreateDefaultEvolutionItemArgumentLabels(labels);
        }
    }

    private static Dictionary<int, string> CreateDefaultEvolutionItemArgumentLabels(ZaTextLabelLookup labels)
    {
        return DefaultEvolutionItemIds.ToDictionary(
            itemId => itemId,
            labels.Item);
    }

    private IEnumerable<ZaPokemonRecord> LoadRecords(
        ZaWorkflowFile source,
        ZaWorkflowFile? baseSource,
        ZaEvolutionItemConversionState? conversionState,
        ZaTextLabelLookup labels,
        ZaTextLabelLookup spriteLabels,
        IReadOnlyList<ZaTechnicalMachineMove> tmCatalog,
        IReadOnlyDictionary<int, string> evolutionItemArgumentLabels)
    {
        var table = ZaPersonalTable.GetRootAsZaPersonalTable(new ByteBuffer(source.Bytes));
        var hasLegacyByteDexOrderLayout = table.HasLegacyByteZADexOrderLayout;
        ZaPersonalTable? baseTable = baseSource is null
            ? null
            : ZaPersonalTable.GetRootAsZaPersonalTable(new ByteBuffer(baseSource.Bytes));
        fileSource.EnsureBoundedTableCount(table.EntryLength, "The Z-A personal table");
        if (baseTable is { } boundedBaseTable)
        {
            fileSource.EnsureBoundedTableCount(boundedBaseTable.EntryLength, "The base Z-A personal table");
        }

        var baseRowsBySpecies = baseTable is { } vanilla
            ? ZaPersonalLegacyRecovery.CreateUniqueBaseRowsBySpecies(vanilla)
            : null;
        var nestedCount = 0;
        for (var index = 0; index < table.EntryLength; index++)
        {
            var entry = table.Entry(index);
            if (entry is null || (entry.Value.Species?.Species ?? 0) <= 0)
            {
                continue;
            }

            var indexedBaseEntry = baseTable is { } vanillaTable && index < vanillaTable.EntryLength
                ? vanillaTable.Entry(index)
                : null;
            var baseEntry = ZaPersonalLegacyRecovery.FindBaseRow(
                entry,
                indexedBaseEntry,
                baseRowsBySpecies);
            nestedCount = checked(nestedCount + EnsureBoundedPersonalVectors(entry.Value, "The Z-A personal row"));
            if (baseEntry is { } boundedBaseEntry)
            {
                nestedCount = checked(
                    nestedCount + EnsureBoundedPersonalVectors(boundedBaseEntry, "The base Z-A personal row"));
            }

            fileSource.EnsureBoundedNestedCount(nestedCount, "The Z-A personal nested vectors");
            if (hasLegacyByteDexOrderLayout && entry.Value.Species is not null && baseEntry is null)
            {
                throw new InvalidDataException(
                    $"Legacy Pokemon personal row {index} cannot recover its missing data from the configured base table.");
            }

            yield return ToRecord(
                index,
                entry.Value,
                baseEntry,
                conversionState,
                source,
                labels,
                spriteLabels,
                tmCatalog,
                evolutionItemArgumentLabels,
                hasLegacyByteDexOrderLayout);
        }
    }

    private int EnsureBoundedPersonalVectors(ZaPersonal entry, string label)
    {
        fileSource.EnsureBoundedTableCount(entry.EvolutionsLength, $"{label} evolution vector");
        fileSource.EnsureBoundedTableCount(entry.LevelupMovesLength, $"{label} learnset vector");
        fileSource.EnsureBoundedTableCount(entry.TmMovesLength, $"{label} TM vector");
        fileSource.EnsureBoundedTableCount(entry.EggMovesLength, $"{label} egg-move vector");
        fileSource.EnsureBoundedTableCount(entry.ReminderMovesLength, $"{label} reminder-move vector");
        var count = checked(entry.EvolutionsLength
            + entry.LevelupMovesLength
            + entry.TmMovesLength
            + entry.EggMovesLength
            + entry.ReminderMovesLength);
        fileSource.EnsureBoundedNestedCount(count, $"{label} nested vectors");
        return count;
    }

    private static ZaPokemonRecord ToRecord(
        int personalId,
        ZaPersonal entry,
        ZaPersonal? baseEntry,
        ZaEvolutionItemConversionState? conversionState,
        ZaWorkflowFile source,
        ZaTextLabelLookup labels,
        ZaTextLabelLookup spriteLabels,
        IReadOnlyList<ZaTechnicalMachineMove> tmCatalog,
        IReadOnlyDictionary<int, string> evolutionItemArgumentLabels,
        bool hasLegacyByteDexOrderLayout)
    {
        var species = entry.Species;
        var baseStatsData = entry.BaseStats;
        var evYieldData = entry.EvYield;
        var genderData = entry.Gender;
        var eggHatchData = entry.EggHatch;

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
        var zaDexOrder = ZaPersonalLegacyRecovery.ResolveZADexOrder(
            entry,
            baseEntry,
            hasLegacyByteDexOrderLayout);
        var total = hp + attack + defense + specialAttack + specialDefense + speed;
        var baseExperience = ZaPokemonExperience.CalculateBaseExperience(
            total,
            entry.EvoStage,
            entry.ExpAddend);
        var stats = new ZaPokemonBaseStats(
            hp,
            attack,
            defense,
            specialAttack,
            specialDefense,
            speed,
            total);
        var abilities = new ZaPokemonAbilitySet(
            entry.Ability1,
            labels.Ability(entry.Ability1),
            entry.Ability2,
            labels.Ability(entry.Ability2),
            entry.AbilityHidden,
            labels.Ability(entry.AbilityHidden));
        var dexPresence = new ZaPokemonDexPresence(
            entry.IsPresent,
            zaDexOrder > 0,
            zaDexOrder,
            0,
            0);
        var details = new ZaPokemonPersonalDetails(
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
            entry.EggHatchCycles,
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
            zaDexOrder,
            form,
            0,
            0);

        return new ZaPokemonRecord(
            personalId,
            speciesId,
            form,
            labels.Pokemon(speciesId),
            ZaLabels.PokemonFormLabel(speciesId, form, labels.Pokemon(speciesId)),
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
            ReadEvolutions(entry, baseEntry, conversionState, labels, evolutionItemArgumentLabels),
            ReadLearnset(entry, labels),
            ReadCompatibility(entry, labels, tmCatalog),
            new ZaPokemonProvenance(source.RelativePath, source.SourceLayer, source.FileState),
            spriteLabels.Pokemon(speciesId));
    }

    private ZaPokemonRecord[] AttachVanillaYieldDefaults(
        OpenedProject project,
        IReadOnlyList<ZaPokemonRecord> pokemon)
    {
        try
        {
            var baseSource = fileSource.ReadBase(project, ZaDataPaths.PersonalArray);
            var baseTable = ZaPersonalTable.GetRootAsZaPersonalTable(new ByteBuffer(baseSource.Bytes));
            fileSource.EnsureBoundedTableCount(baseTable.EntryLength, "The base Z-A yield table");
            return pokemon.Select(record =>
            {
                if (record.PersonalId < 0
                    || record.PersonalId >= baseTable.EntryLength
                    || baseTable.Entry(record.PersonalId) is not { } baseEntry
                    || baseEntry.Species is not { } baseSpecies
                    || baseSpecies.Species != record.SpeciesId
                    || baseSpecies.Form != record.Form)
                {
                    return record;
                }

                var baseStats = baseEntry.BaseStats;
                var baseTotal = baseStats is null
                    ? 0
                    : baseStats.Value.Hp
                        + baseStats.Value.Atk
                        + baseStats.Value.Def
                        + baseStats.Value.Spa
                        + baseStats.Value.Spd
                        + baseStats.Value.Spe;
                var baseEvYield = baseEntry.EvYield;
                return record with
                {
                    VanillaYieldDefaults = new ZaPokemonVanillaYieldDefaults(
                        ZaPokemonExperience.CalculateBaseExperience(
                            baseTotal,
                            baseEntry.EvoStage,
                            baseEntry.ExpAddend),
                        baseEvYield?.Hp ?? 0,
                        baseEvYield?.Atk ?? 0,
                        baseEvYield?.Def ?? 0,
                        baseEvYield?.Spa ?? 0,
                        baseEvYield?.Spd ?? 0,
                        baseEvYield?.Spe ?? 0),
                };
            }).ToArray();
        }
        catch (Exception exception) when (
            exception is IOException
                or InvalidDataException
                or InvalidOperationException
                or ArgumentException
                or UnauthorizedAccessException)
        {
            return pokemon.ToArray();
        }
    }

    private static IReadOnlyList<ZaPokemonEvolutionRecord> ReadEvolutions(
        ZaPersonal entry,
        ZaPersonal? baseEntry,
        ZaEvolutionItemConversionState? conversionState,
        ZaTextLabelLookup labels,
        IReadOnlyDictionary<int, string> evolutionItemArgumentLabels)
    {
        var evolutions = new List<ZaPokemonEvolutionRecord>();
        for (var index = 0; index < entry.EvolutionsLength; index++)
        {
            var evolution = entry.Evolutions(index);
            if (evolution is null || evolution.Value.Species == 0)
            {
                continue;
            }

            var method = GetEvolutionMethodDefinition(evolution.Value.Condition);
            var argument = NormalizeEvolutionItemArgument(
                evolution.Value.Condition,
                evolution.Value.Parameter,
                evolution.Value.Species,
                evolution.Value.Form,
                evolution.Value.Level,
                baseEntry,
                conversionState);
            evolutions.Add(new ZaPokemonEvolutionRecord(
                index,
                evolution.Value.Condition,
                argument,
                evolution.Value.Species,
                evolution.Value.Form,
                evolution.Value.Level,
                method.Name,
                method.ArgumentKind,
                method.ArgumentLabel,
                FormatEvolutionArgument(
                    argument,
                    method,
                    labels,
                    evolutionItemArgumentLabels)));
        }

        return evolutions;
    }

    private static int NormalizeEvolutionItemArgument(
        int method,
        int storedArgument,
        int species,
        int form,
        int level,
        ZaPersonal? baseEntry,
        ZaEvolutionItemConversionState? conversionState)
    {
        if (UsesEvolutionItemConversion(method)
            && conversionState is not null
            && conversionState.TryDecode(storedArgument, out var convertedItemId))
        {
            return convertedItemId;
        }

        if (UsesEvolutionItemConversion(method)
            && storedArgument == 50
            && VanillaEvolutionItemParameterIds.TryGetValue(storedArgument, out var reservedItemId))
        {
            return reservedItemId;
        }

        if (!UsesEvolutionItemConversion(method)
            || !VanillaEvolutionItemParameterIds.TryGetValue(storedArgument, out var itemId)
            || baseEntry is not { } vanilla
            || !ContainsMatchingVanillaEvolution(vanilla, method, storedArgument, species, form, level))
        {
            return storedArgument;
        }

        return itemId;
    }

    private static bool ContainsMatchingVanillaEvolution(
        ZaPersonal vanilla,
        int method,
        int storedArgument,
        int species,
        int form,
        int level)
    {
        for (var index = 0; index < vanilla.EvolutionsLength; index++)
        {
            if (vanilla.Evolutions(index) is { } baseEvolution
                && baseEvolution.Condition == method
                && baseEvolution.Parameter == storedArgument
                && baseEvolution.Species == species
                && baseEvolution.Form == form
                && baseEvolution.Level == level)
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<ZaPokemonLearnsetMove> ReadLearnset(
        ZaPersonal entry,
        ZaTextLabelLookup labels)
    {
        var moves = new List<ZaPokemonLearnsetMove>();
        for (var index = 0; index < entry.LevelupMovesLength; index++)
        {
            var learnedMove = entry.LevelupMoves(index);
            if (learnedMove is null || learnedMove.Value.Move == 0)
            {
                continue;
            }

            var rawLevel = learnedMove.Value.Level;
            var level = DecodeLearnsetDisplayLevel(rawLevel);
            moves.Add(new ZaPokemonLearnsetMove(
                index,
                learnedMove.Value.Move,
                labels.Move(learnedMove.Value.Move),
                level,
                rawLevel,
                FormatLearnsetLevelLabel(rawLevel)));
        }

        return moves;
    }

    internal static int DecodeLearnsetDisplayLevel(int rawLevel)
    {
        return rawLevel & LearnsetDisplayLevelMask;
    }

    internal static int EncodeLearnsetRawLevel(int displayLevel, int? existingRawLevel)
    {
        return (existingRawLevel.GetValueOrDefault() & LearnsetMasteryLevelMask)
            | (displayLevel & LearnsetDisplayLevelMask);
    }

    internal static string? FormatLearnsetLevelLabel(int rawLevel)
    {
        if (rawLevel == 0)
        {
            return "Evolution or default";
        }

        var masteryLevel = (rawLevel & LearnsetMasteryLevelMask) >> LearnsetMasteryLevelShift;
        return masteryLevel > 0
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Lv. {DecodeLearnsetDisplayLevel(rawLevel)} / Mastery Lv. {masteryLevel}")
            : null;
    }

    private static IReadOnlyList<ZaPokemonCompatibilityGroup> ReadCompatibility(
        ZaPersonal entry,
        ZaTextLabelLookup labels,
        IReadOnlyList<ZaTechnicalMachineMove> tmCatalog)
    {
        var groups = new List<ZaPokemonCompatibilityGroup>
        {
            CreateTechnicalMachineCompatibilityGroup(entry.GetTmMovesArray(), labels, tmCatalog),
        };

        var eggMoves = entry.GetEggMovesArray();
        if (eggMoves.Length > 0)
        {
            groups.Add(CreateMoveVectorCompatibilityGroup(EggMoveCompatibilityGroupId, "Egg Moves", eggMoves, labels));
        }

        var reminderMoves = entry.GetReminderMovesArray();
        if (reminderMoves.Length > 0)
        {
            groups.Add(CreateMoveVectorCompatibilityGroup(ReminderMoveCompatibilityGroupId, "Reminder Moves", reminderMoves, labels));
        }

        return groups;
    }

    private static ZaPokemonCompatibilityGroup CreateTechnicalMachineCompatibilityGroup(
        IReadOnlyList<ushort> learnableMoves,
        ZaTextLabelLookup labels,
        IReadOnlyList<ZaTechnicalMachineMove> tmCatalog)
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
            .Select(tm => new ZaPokemonCompatibilityEntry(
                tm.MoveId,
                tm.MoveId,
                tm.MoveName,
                tm.Label,
                learnableMoveSet.Contains(tm.MoveId)))
            .ToArray();

        return new ZaPokemonCompatibilityGroup(
            TechnicalMachineCompatibilityGroupId,
            "TM Moves",
            entries.Count(entry => entry.CanLearn),
            entries);
    }

    private static ZaPokemonCompatibilityGroup CreateMoveVectorCompatibilityGroup(
        string id,
        string label,
        IReadOnlyList<ushort> moves,
        ZaTextLabelLookup labels,
        bool useMoveIdAsSlot = false)
    {
        var entries = moves
            .Where(move => move != 0)
            .Select((move, index) => new ZaPokemonCompatibilityEntry(
                useMoveIdAsSlot ? move : index,
                move,
                labels.Move(move),
                $"{index + 1}. {labels.Move(move)}",
                CanLearn: true))
            .ToArray();

        return new ZaPokemonCompatibilityGroup(id, label, entries.Length, entries);
    }

    private static IReadOnlyList<ZaPokemonEvolutionMethodOption> CreateEvolutionOptions(
        IReadOnlyList<ZaPokemonRecord> pokemon,
        ZaTextLabelLookup labels,
        IReadOnlyDictionary<int, string> evolutionItemArgumentLabels)
    {
        var itemOptions = CreateIndexedOptions(labels.ItemNameCount, labels.Item, includeNone: true);
        var evolutionItemOptions = CreateEvolutionItemArgumentOptions(labels, evolutionItemArgumentLabels);
        var moveOptions = CreateIndexedOptions(labels.MoveNameCount, labels.Move, includeNone: false);
        var speciesOptions = CreateIndexedOptions(labels.PokemonNameCount, labels.Pokemon, includeNone: true);

        return EvolutionMethods
            .Select(method => method.Value)
            .Concat(pokemon.SelectMany(record => record.Evolutions.Select(evolution => evolution.Method)).Append(0))
            .Distinct()
            .OrderBy(value => value)
            .Select(value =>
            {
                var definition = GetEvolutionMethodDefinition(value);
                return new ZaPokemonEvolutionMethodOption(
                    value,
                    $"{value.ToString("000", CultureInfo.InvariantCulture)} {definition.Name}",
                    definition.ArgumentKind,
                    definition.ArgumentLabel,
                    CreateEvolutionArgumentOptions(
                        definition,
                        itemOptions,
                        evolutionItemOptions,
                        moveOptions,
                        speciesOptions),
                    definition.UsesLevel);
            })
            .ToArray();
    }

    private static IReadOnlyList<ZaPokemonEditableFieldOption> CreateMoveOptions(
        IReadOnlyList<ZaPokemonRecord> pokemon,
        ZaTextLabelLookup labels)
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
            .Select(move => new ZaPokemonEditableFieldOption(move, labels.Move(move)))
            .ToArray();
    }

    private static IReadOnlyList<ZaPokemonEditableField> CreateEditableFields(ZaTextLabelLookup labels)
    {
        var abilityOptions = CreateIndexedOptions(labels.AbilityNameCount, labels.Ability, includeNone: true);
        var speciesOptions = CreateIndexedOptions(labels.PokemonNameCount, labels.Pokemon, includeNone: true);

        return BaseEditableFields
            .Select(field =>
            {
                var options = field.Field switch
                {
                    Type1Field or Type2Field => TypeOptions,
                    Ability1Field or Ability2Field or HiddenAbilityField => abilityOptions,
                    GenderRatioField => GenderRatioOptions,
                    ExpGrowthField => ExpGrowthOptions,
                    EvolutionStageField => EvolutionStageOptions,
                    EggGroup1Field or EggGroup2Field => EggGroupOptions,
                    ColorField => ColorOptions,
                    HatchedSpeciesField => speciesOptions,
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

    private static IReadOnlyList<ZaPokemonEditableFieldOption> CreateEvolutionArgumentOptions(
        EvolutionMethodDefinition method,
        IReadOnlyList<ZaPokemonEditableFieldOption> itemOptions,
        IReadOnlyList<ZaPokemonEditableFieldOption> evolutionItemOptions,
        IReadOnlyList<ZaPokemonEditableFieldOption> moveOptions,
        IReadOnlyList<ZaPokemonEditableFieldOption> speciesOptions)
    {
        return method.ArgumentKind switch
        {
            EvolutionArgumentKindItem => IsUseItemEvolutionMethod(method.Value) ? evolutionItemOptions : itemOptions,
            EvolutionArgumentKindMove => moveOptions,
            EvolutionArgumentKindSpecies => speciesOptions,
            EvolutionArgumentKindType => TypeOptions,
            EvolutionArgumentKindValue => CreateEvolutionValueArgumentOptions(method),
            _ => [],
        };
    }

    private static IReadOnlyList<ZaPokemonEditableFieldOption> CreateEvolutionItemArgumentOptions(
        ZaTextLabelLookup labels,
        IReadOnlyDictionary<int, string> evolutionItemArgumentLabels)
    {
        return evolutionItemArgumentLabels.Keys
            .OrderBy(value => value)
            .Prepend(0)
            .Select(value => new ZaPokemonEditableFieldOption(
                value,
                $"{value.ToString(CultureInfo.InvariantCulture)} {FormatEvolutionItemArgument(value, labels, evolutionItemArgumentLabels)}"))
            .ToArray();
    }

    private static IReadOnlyList<ZaPokemonEditableFieldOption> CreateEvolutionValueArgumentOptions(EvolutionMethodDefinition method)
    {
        return method.Value switch
        {
            16 => CreateOptionList((170, "170 beauty")),
            31 => CreateOptionList(
                (0, "Standard rain rule"),
                (1, "Hisuian rain rule")),
            36 => CreateOptionList(
                (44, "Solgaleo branch"),
                (45, "Lunala branch")),
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
            62 => CreateOptionList((20, "20 Barb Barrage uses")),
            _ => ByteArgumentOptions,
        };
    }

    private static string FormatEvolutionArgument(
        int argument,
        EvolutionMethodDefinition method,
        ZaTextLabelLookup labels,
        IReadOnlyDictionary<int, string> evolutionItemArgumentLabels)
    {
        return method.ArgumentKind switch
        {
            EvolutionArgumentKindItem => IsUseItemEvolutionMethod(method.Value)
                ? FormatEvolutionItemArgument(argument, labels, evolutionItemArgumentLabels)
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

    private static string FormatEvolutionItemArgument(
        int argument,
        ZaTextLabelLookup labels,
        IReadOnlyDictionary<int, string> evolutionItemArgumentLabels)
    {
        if (argument == 0)
        {
            return "None";
        }

        if (evolutionItemArgumentLabels.TryGetValue(argument, out var itemLabel))
        {
            return itemLabel;
        }

        return labels.Item(argument);
    }

    private static bool IsUseItemEvolutionMethod(int method)
    {
        return method is 8 or 17 or 18 or 42;
    }

    internal static bool UsesEvolutionItemConversion(int method)
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
            36 when argument == 44 => "Solgaleo branch",
            36 when argument == 45 => "Lunala branch",
            43 when argument == 3 => "3 critical hits",
            44 when argument == 49 => "49 HP lost",
            50 when argument == 1000 => "1000 steps",
            54 when argument == 999 => "999 coins",
            55 when argument == 3 => "3 leader wins",
            56 when argument == 20 => "20 Rage Fist uses",
            59 or 60 when argument == 294 => "294 recoil damage",
            61 when argument == 0 => "Kleavor/Ursaluna/Wyrdeer rule",
            61 when argument == 1 => "Hisuian Sliggoo rain rule",
            62 when argument == 20 => "20 Barb Barrage uses",
            _ => argument.ToString(CultureInfo.InvariantCulture),
        };
    }

    public static EvolutionMethodDefinition GetEvolutionMethodDefinition(int value)
    {
        return EvolutionMethods.FirstOrDefault(method => method.Value == value)
            ?? new EvolutionMethodDefinition(value, $"Condition {value}", EvolutionArgumentKindValue, "Parameter");
    }

    public static ZaPokemonEditableField? GetEditableField(ZaPokemonWorkflow workflow, string field)
    {
        return workflow.EditableFields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, field, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<ZaPokemonEditableFieldOption> CreateIndexedOptions(
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
                return new ZaPokemonEditableFieldOption(
                    value,
                    $"{value.ToString(CultureInfo.InvariantCulture)} {label}");
            })
            .ToArray();
    }

    private static IReadOnlyList<ZaPokemonEditableFieldOption> CreateOptionList(params (int Value, string Label)[] options)
    {
        return options
            .Select(option => new ZaPokemonEditableFieldOption(option.Value, option.Label))
            .ToArray();
    }

    private static IReadOnlyList<ZaPokemonEditableFieldOption> CreateGenderRatioOptions()
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

    private static ZaPokemonEditableField CreateField(
        string field,
        string label,
        string group,
        int? minimumValue,
        int? maximumValue,
        IReadOnlyList<ZaPokemonEditableFieldOption>? options = null)
    {
        return new ZaPokemonEditableField(
            field,
            label,
            group,
            "integer",
            minimumValue,
            maximumValue,
            options ?? Array.Empty<ZaPokemonEditableFieldOption>());
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

    public sealed record EvolutionMethodDefinition(
        int Value,
        string Name,
        string ArgumentKind,
        string ArgumentLabel,
        bool UsesLevel = true);
}
