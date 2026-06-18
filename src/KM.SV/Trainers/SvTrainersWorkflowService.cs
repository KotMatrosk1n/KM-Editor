// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.SV.Data;
using KM.SV.Workflows;
using System.Globalization;

namespace KM.SV.Trainers;

internal sealed class SvTrainersWorkflowService
{
    private const string WorkflowLabel = "Trainers";
    private const string WorkflowDescription = "Edit Scarlet/Violet trainer data and trainer Pokemon.";
    public const string TrainerClassIdField = "trainerClassId";
    public const string ClassBallIdField = "classBallId";
    public const string BattleTypeField = "battleType";
    public const string TrainerItem1IdField = "trainerItem1Id";
    public const string TrainerItem2IdField = "trainerItem2Id";
    public const string TrainerItem3IdField = "trainerItem3Id";
    public const string TrainerItem4IdField = "trainerItem4Id";
    public const string AiFlagsField = "aiFlags";
    public const string HealField = "heal";
    public const string MoneyField = "money";
    public const string GiftField = "gift";
    public const string SpeciesIdField = "speciesId";
    public const string FormField = "form";
    public const string LevelField = "level";
    public const string HeldItemIdField = "heldItemId";
    public const string Move1IdField = "move1Id";
    public const string Move2IdField = "move2Id";
    public const string Move3IdField = "move3Id";
    public const string Move4IdField = "move4Id";
    public const string GenderField = "gender";
    public const string AbilityField = "ability";
    public const string NatureField = "nature";
    public const string EvHpField = "evHp";
    public const string EvAttackField = "evAttack";
    public const string EvDefenseField = "evDefense";
    public const string EvSpecialAttackField = "evSpecialAttack";
    public const string EvSpecialDefenseField = "evSpecialDefense";
    public const string EvSpeedField = "evSpeed";
    public const string IvHpField = "ivHp";
    public const string IvAttackField = "ivAttack";
    public const string IvDefenseField = "ivDefense";
    public const string IvSpecialAttackField = "ivSpecialAttack";
    public const string IvSpecialDefenseField = "ivSpecialDefense";
    public const string IvSpeedField = "ivSpeed";
    public const string ShinyField = "shiny";
    internal const string TeraTypeField = "teraType";

    private static readonly IReadOnlyList<SvTrainerEditableFieldOption> BooleanOptions =
    [
        new(0, "No"),
        new(1, "Yes"),
    ];

    private static readonly IReadOnlyList<SvTrainerEditableFieldOption> BattleTypeOptions =
    [
        new(0, "1v1"),
        new(1, "2v2"),
    ];

    private static readonly IReadOnlyList<SvTrainerEditableFieldOption> GenderOptions =
    [
        new(0, "Random"),
        new(1, "Male"),
        new(2, "Female"),
    ];

    private static readonly IReadOnlyList<SvTrainerEditableFieldOption> AbilityModeOptions =
    [
        new(0, "Random 1/2"),
        new(1, "Random 1/2/Hidden"),
        new(2, "Ability 1"),
        new(3, "Ability 2"),
        new(4, "Hidden Ability"),
    ];

    private static readonly IReadOnlyList<SvTrainerEditableFieldOption> NatureOptions =
    [
        new(0, "Default"),
        new(1, "Hardy (neutral)"),
        new(2, "Lonely (+Atk, -Def)"),
        new(3, "Brave (+Atk, -Spe)"),
        new(4, "Adamant (+Atk, -Sp. Atk)"),
        new(5, "Naughty (+Atk, -Sp. Def)"),
        new(6, "Bold (+Def, -Atk)"),
        new(7, "Docile (neutral)"),
        new(8, "Relaxed (+Def, -Spe)"),
        new(9, "Impish (+Def, -Sp. Atk)"),
        new(10, "Lax (+Def, -Sp. Def)"),
        new(11, "Timid (+Spe, -Atk)"),
        new(12, "Hasty (+Spe, -Def)"),
        new(13, "Serious (neutral)"),
        new(14, "Jolly (+Spe, -Sp. Atk)"),
        new(15, "Naive (+Spe, -Sp. Def)"),
        new(16, "Modest (+Sp. Atk, -Atk)"),
        new(17, "Mild (+Sp. Atk, -Def)"),
        new(18, "Quiet (+Sp. Atk, -Spe)"),
        new(19, "Bashful (neutral)"),
        new(20, "Rash (+Sp. Atk, -Sp. Def)"),
        new(21, "Calm (+Sp. Def, -Atk)"),
        new(22, "Gentle (+Sp. Def, -Def)"),
        new(23, "Sassy (+Sp. Def, -Spe)"),
        new(24, "Careful (+Sp. Def, -Sp. Atk)"),
        new(25, "Quirky (neutral)"),
    ];

    private static readonly IReadOnlyList<SvTrainerEditableFieldOption> ShinyModeOptions =
    [
        new(0, "Default"),
        new(1, "Forced shiny"),
    ];

    private static readonly IReadOnlyList<SvTrainerEditableFieldOption> TeraTypeOptions =
    [
        new((int)global::GemType.DEFAULT, "Default"),
        new((int)global::GemType.RANDOM, "Random"),
        new((int)global::GemType.NORMAL, "Normal"),
        new((int)global::GemType.KAKUTOU, "Fighting"),
        new((int)global::GemType.HIKOU, "Flying"),
        new((int)global::GemType.DOKU, "Poison"),
        new((int)global::GemType.JIMEN, "Ground"),
        new((int)global::GemType.IWA, "Rock"),
        new((int)global::GemType.MUSHI, "Bug"),
        new((int)global::GemType.GHOST, "Ghost"),
        new((int)global::GemType.HAGANE, "Steel"),
        new((int)global::GemType.HONOO, "Fire"),
        new((int)global::GemType.MIZU, "Water"),
        new((int)global::GemType.KUSA, "Grass"),
        new((int)global::GemType.DENKI, "Electric"),
        new((int)global::GemType.ESPER, "Psychic"),
        new((int)global::GemType.KOORI, "Ice"),
        new((int)global::GemType.DRAGON, "Dragon"),
        new((int)global::GemType.AKU, "Dark"),
        new((int)global::GemType.FAIRY, "Fairy"),
        new((int)global::GemType.NIJI, "Stellar"),
    ];

    private static readonly IReadOnlyList<SvTrainerEditableField> BaseEditableFields =
    [
        CreateField(SvTrainersWorkflowService.BattleTypeField, "Battle type", 0, 1, BattleTypeOptions),
        CreateField(SvTrainersWorkflowService.MoneyField, "Money rate", sbyte.MinValue, sbyte.MaxValue),
        CreateField(SvTrainersWorkflowService.AiFlagsField, "AI flags", 0, byte.MaxValue),
        CreateField("isStrong", "Strong trainer", 0, 1, BooleanOptions, "boolean"),
        CreateField("changeGem", "Change Tera type", 0, 1, BooleanOptions, "boolean"),
        CreateField(SvTrainersWorkflowService.FormField, "Form", short.MinValue, short.MaxValue),
        CreateField(SvTrainersWorkflowService.LevelField, "Level", 0, 100),
        CreateField(SvTrainersWorkflowService.GenderField, "Gender", 0, 2, GenderOptions),
        CreateField(SvTrainersWorkflowService.AbilityField, "Ability mode", 0, 4, AbilityModeOptions),
        CreateField(SvTrainersWorkflowService.NatureField, "Nature", 0, 25, NatureOptions),
        CreateField(TeraTypeField, "Tera type", 0, 101, TeraTypeOptions),
        CreateField(SvTrainersWorkflowService.EvHpField, "HP EV", 0, int.MaxValue),
        CreateField(SvTrainersWorkflowService.EvAttackField, "Attack EV", 0, int.MaxValue),
        CreateField(SvTrainersWorkflowService.EvDefenseField, "Defense EV", 0, int.MaxValue),
        CreateField(SvTrainersWorkflowService.EvSpecialAttackField, "Sp. Atk EV", 0, int.MaxValue),
        CreateField(SvTrainersWorkflowService.EvSpecialDefenseField, "Sp. Def EV", 0, int.MaxValue),
        CreateField(SvTrainersWorkflowService.EvSpeedField, "Speed EV", 0, int.MaxValue),
        CreateField(SvTrainersWorkflowService.IvHpField, "HP IV", 0, int.MaxValue),
        CreateField(SvTrainersWorkflowService.IvAttackField, "Attack IV", 0, int.MaxValue),
        CreateField(SvTrainersWorkflowService.IvDefenseField, "Defense IV", 0, int.MaxValue),
        CreateField(SvTrainersWorkflowService.IvSpecialAttackField, "Sp. Atk IV", 0, int.MaxValue),
        CreateField(SvTrainersWorkflowService.IvSpecialDefenseField, "Sp. Def IV", 0, int.MaxValue),
        CreateField(SvTrainersWorkflowService.IvSpeedField, "Speed IV", 0, int.MaxValue),
        CreateField(SvTrainersWorkflowService.ShinyField, "Shiny mode", 0, 1, ShinyModeOptions),
    ];

    private readonly SvWorkflowFileSource fileSource;

    public SvTrainersWorkflowService(SvWorkflowFileSource? fileSource = null)
    {
        this.fileSource = fileSource ?? new SvWorkflowFileSource();
    }

    public SvWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return SvWorkflowSupport.CreateSummary(
            project,
            SvWorkflowIds.Trainers,
            WorkflowLabel,
            WorkflowDescription);
    }

    public SvTrainersWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var diagnostics = new List<ValidationDiagnostic>();
        SvWorkflowFile? source = null;
        var labels = SvTextLabelLookup.None();
        var trainers = Array.Empty<SvTrainerRecord>();

        try
        {
            labels = SvTextLabelLookup.Load(project, fileSource, diagnostics);
            var abilityResolver = SvTrainerAbilityResolver.Load(project, fileSource, labels, diagnostics);
            var moveResolver = SvTrainerMoveResolver.Load(project, fileSource, diagnostics);
            source = fileSource.Read(project, SvDataPaths.TrainerDataArray);
            trainers = LoadRecords(source, labels, abilityResolver, moveResolver).ToArray();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(SvWorkflowSupport.Error(
                $"Trainers could not be loaded: {exception.Message}",
                $"romfs/{SvDataPaths.TrainerDataArray}"));
        }

        var summary = SvWorkflowSupport.CreateSummary(
            project,
            SvWorkflowIds.Trainers,
            WorkflowLabel,
            WorkflowDescription,
            diagnostics.Count == 0 ? null : diagnostics);

        return new SvTrainersWorkflow(
            summary,
            trainers,
            CreateEditableFields(labels),
            new SvTrainersWorkflowStats(
                trainers.Length,
                trainers.Sum(trainer => trainer.Team.Count),
                source is null ? 0 : 1),
            diagnostics);
    }

    private static IEnumerable<SvTrainerRecord> LoadRecords(
        SvWorkflowFile source,
        SvTextLabelLookup labels,
        SvTrainerAbilityResolver abilityResolver,
        SvTrainerMoveResolver moveResolver)
    {
        var table = global::trainer.TrdataMainArray.GetRootAsTrdataMainArray(new ByteBuffer(source.Bytes));
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var trainer = table.Values(index);
            if (trainer is null)
            {
                continue;
            }

            yield return ToRecord(index, trainer.Value, source, labels, abilityResolver, moveResolver);
        }
    }

    private static SvTrainerRecord ToRecord(
        int trainerId,
        global::trainer.TrdataMain trainer,
        SvWorkflowFile source,
        SvTextLabelLookup labels,
        SvTrainerAbilityResolver abilityResolver,
        SvTrainerMoveResolver moveResolver)
    {
        var aiFlags = PackAiFlags(trainer);
        var team = ReadTeam(trainer, labels, abilityResolver, moveResolver).ToArray();
        var aiStates = CreateAiStates(trainer);
        var className = labels.TrainerType(trainer.TrainerType);
        var name = labels.TrainerName(trainer.TrNameLabel, trainerId);

        return new SvTrainerRecord(
            trainerId,
            name,
            TrainerClassId: 0,
            className,
            Location: trainer.Trid ?? string.Empty,
            (int)trainer.BattleType,
            FormatBattleType(trainer.BattleType),
            ItemIds: [],
            Items: [],
            aiFlags,
            aiStates,
            Heal: false,
            trainer.MoneyRate,
            Gift: 0,
            ClassBallId: null,
            ClassBall: null,
            CanEditClassBall: false,
            ClassBallScope: "none",
            team,
            new SvTrainerProvenance(
                source.RelativePath,
                source.RelativePath,
                ClassSourceFile: null,
                source.SourceLayer,
                source.SourceLayer,
                ClassSourceLayer: null,
                source.FileState,
                source.FileState,
                ClassFileState: null));
    }

    private static IEnumerable<SvTrainerPokemonRecord> ReadTeam(
        global::trainer.TrdataMain trainer,
        SvTextLabelLookup labels,
        SvTrainerAbilityResolver abilityResolver,
        SvTrainerMoveResolver moveResolver)
    {
        var slots = new[]
        {
            trainer.Poke1,
            trainer.Poke2,
            trainer.Poke3,
            trainer.Poke4,
            trainer.Poke5,
            trainer.Poke6,
        };

        for (var index = 0; index < slots.Length; index++)
        {
            var pokemon = slots[index];
            if (pokemon is null || (int)pokemon.Value.DevId == 0)
            {
                continue;
            }

            yield return ToPokemon(index, pokemon.Value, labels, abilityResolver, moveResolver);
        }
    }

    private static SvTrainerPokemonRecord ToPokemon(
        int slot,
        global::PokeDataBattle pokemon,
        SvTextLabelLookup labels,
        SvTrainerAbilityResolver abilityResolver,
        SvTrainerMoveResolver moveResolver)
    {
        var speciesId = (int)pokemon.DevId;
        var itemId = (int)pokemon.Item;
        var moveIds = ReadMoves(pokemon, moveResolver);
        var abilities = abilityResolver.Resolve(speciesId, pokemon.FormId);
        var abilityOptions = CreateAbilityModeOptions(abilities);
        var evs = pokemon.EffortValue;
        var ivs = pokemon.TalentValue;
        var record = new SvTrainerPokemonRecord(
            slot,
            speciesId,
            labels.Pokemon(speciesId),
            pokemon.FormId,
            pokemon.Level,
            itemId,
            itemId > 0 ? labels.Item(itemId) : null,
            moveIds,
            moveIds.Select(move => move == 0 ? "None" : labels.Move(move)).ToArray(),
            (int)pokemon.Sex,
            FormatGender(pokemon.Sex),
            (int)pokemon.Tokusei,
            FormatAbilityMode(pokemon.Tokusei, abilities),
            (int)pokemon.Seikaku,
            FormatNature(pokemon.Seikaku),
            new SvTrainerPokemonStatsRecord(
                evs?.Hp ?? 0,
                evs?.Atk ?? 0,
                evs?.Def ?? 0,
                evs?.SpAtk ?? 0,
                evs?.SpDef ?? 0,
                evs?.Agi ?? 0),
            DynamaxLevel: 0,
            CanGigantamax: false,
            new SvTrainerPokemonStatsRecord(
                ivs?.Hp ?? 0,
                ivs?.Atk ?? 0,
                ivs?.Def ?? 0,
                ivs?.SpAtk ?? 0,
                ivs?.SpDef ?? 0,
                ivs?.Agi ?? 0),
            pokemon.RareType != global::RareType.DEFAULT,
            CanDynamax: true,
            TeraType: (int)pokemon.GemType,
            TeraTypeLabel: FormatTeraType(pokemon.GemType))
        {
            AbilityOptions = abilityOptions,
        };

        return record;
    }

    private static IReadOnlyList<int> ReadMoves(
        global::PokeDataBattle pokemon,
        SvTrainerMoveResolver moveResolver)
    {
        var explicitMoves = new[]
        {
            ReadMoveId(pokemon.Waza1),
            ReadMoveId(pokemon.Waza2),
            ReadMoveId(pokemon.Waza3),
            ReadMoveId(pokemon.Waza4),
        };

        if (pokemon.WazaType == global::WazaType.DEFAULT && explicitMoves.All(move => move == 0))
        {
            return moveResolver.Resolve((int)pokemon.DevId, pokemon.FormId, pokemon.Level);
        }

        return explicitMoves;
    }

    private static int ReadMoveId(global::WazaSet? move)
    {
        return move is null ? 0 : (int)move.Value.WazaId;
    }

    private static int PackAiFlags(global::trainer.TrdataMain trainer)
    {
        return (trainer.AiBasic ? 1 << 0 : 0)
            | (trainer.AiHigh ? 1 << 1 : 0)
            | (trainer.AiExpert ? 1 << 2 : 0)
            | (trainer.AiDouble ? 1 << 3 : 0)
            | (trainer.AiRaid ? 1 << 4 : 0)
            | (trainer.AiWeak ? 1 << 5 : 0)
            | (trainer.AiItem ? 1 << 6 : 0)
            | (trainer.AiChange ? 1 << 7 : 0);
    }

    private static IReadOnlyList<SvTrainerAiFlagState> CreateAiStates(global::trainer.TrdataMain trainer)
    {
        var flags = new[]
        {
            (0, "Basic", "Basic AI", trainer.AiBasic),
            (1, "High", "High AI", trainer.AiHigh),
            (2, "Expert", "Expert AI", trainer.AiExpert),
            (3, "Double", "Double-battle AI", trainer.AiDouble),
            (4, "Raid", "Raid AI", trainer.AiRaid),
            (5, "Weak", "Weak AI", trainer.AiWeak),
            (6, "Item", "Item AI", trainer.AiItem),
            (7, "Change", "Switching AI", trainer.AiChange),
        };

        return flags
            .Select(flag => new SvTrainerAiFlagState(flag.Item1, 1 << flag.Item1, flag.Item2, flag.Item3, flag.Item4))
            .ToArray();
    }

    private static IReadOnlyList<SvTrainerEditableField> CreateEditableFields(SvTextLabelLookup labels)
    {
        var speciesOptions = CreateIndexedOptions(labels.PokemonNameCount, labels.Pokemon, includeNone: true);
        var itemOptions = CreateIndexedOptions(labels.ItemNameCount, labels.Item, includeNone: true);
        var moveOptions = CreateIndexedOptions(labels.MoveNameCount, labels.Move, includeNone: true);
        var fields = new List<SvTrainerEditableField>();

        foreach (var field in BaseEditableFields)
        {
            if (field.Field == SvTrainersWorkflowService.FormField)
            {
                fields.Add(CreateField(SvTrainersWorkflowService.SpeciesIdField, "Species", 0, MaximumOptionValue(speciesOptions, ushort.MaxValue), speciesOptions));
            }

            fields.Add(field);

            if (field.Field == SvTrainersWorkflowService.LevelField)
            {
                fields.Add(CreateField(SvTrainersWorkflowService.HeldItemIdField, "Held item", 0, MaximumOptionValue(itemOptions, int.MaxValue), itemOptions));
                fields.Add(CreateField(SvTrainersWorkflowService.Move1IdField, "Move 1", 0, MaximumOptionValue(moveOptions, ushort.MaxValue), moveOptions));
                fields.Add(CreateField(SvTrainersWorkflowService.Move2IdField, "Move 2", 0, MaximumOptionValue(moveOptions, ushort.MaxValue), moveOptions));
                fields.Add(CreateField(SvTrainersWorkflowService.Move3IdField, "Move 3", 0, MaximumOptionValue(moveOptions, ushort.MaxValue), moveOptions));
                fields.Add(CreateField(SvTrainersWorkflowService.Move4IdField, "Move 4", 0, MaximumOptionValue(moveOptions, ushort.MaxValue), moveOptions));
            }
        }

        return fields;
    }

    private static IReadOnlyList<SvTrainerEditableFieldOption> CreateIndexedOptions(
        int count,
        Func<int, string> resolveName,
        bool includeNone)
    {
        var firstValue = includeNone ? 0 : 1;
        if (count <= firstValue)
        {
            return includeNone ? [new(0, "0 None")] : Array.Empty<SvTrainerEditableFieldOption>();
        }

        return Enumerable
            .Range(firstValue, count - firstValue)
            .Select(value =>
            {
                var label = value == 0 ? "None" : resolveName(value);
                return new SvTrainerEditableFieldOption(
                    value,
                    $"{value.ToString(CultureInfo.InvariantCulture)} {label}");
            })
            .ToArray();
    }

    private static int MaximumOptionValue(
        IReadOnlyList<SvTrainerEditableFieldOption> options,
        int fallback)
    {
        return options.Count == 0 ? fallback : options.Max(option => option.Value);
    }

    private static SvTrainerEditableField CreateField(
        string field,
        string label,
        int minimumValue,
        int maximumValue,
        IReadOnlyList<SvTrainerEditableFieldOption>? options = null,
        string valueKind = "integer")
    {
        return new SvTrainerEditableField(
            field,
            label,
            valueKind,
            minimumValue,
            maximumValue,
            options ?? Array.Empty<SvTrainerEditableFieldOption>());
    }

    internal static string FormatBattleType(global::trainer.BattleType value)
    {
        return value switch
        {
            global::trainer.BattleType._1vs1 => "1v1",
            global::trainer.BattleType._2vs2 => "2v2",
            _ => SvLabels.EnumName(value),
        };
    }

    internal static string FormatGender(global::SexType value)
    {
        return value switch
        {
            global::SexType.DEFAULT => "Random",
            global::SexType.MALE => "Male",
            global::SexType.FEMALE => "Female",
            _ => SvLabels.EnumName(value),
        };
    }

    internal static string FormatAbilityMode(global::TokuseiType value)
    {
        return FormatAbilityMode(value, SvTrainerAbilitySet.Empty);
    }

    private static string FormatAbilityMode(global::TokuseiType value, SvTrainerAbilitySet abilities)
    {
        return CreateAbilityModeOptions(abilities).FirstOrDefault(option => option.Value == (int)value)?.Label
            ?? SvLabels.EnumName(value);
    }

    private static IReadOnlyList<SvTrainerEditableFieldOption> CreateAbilityModeOptions(SvTrainerAbilitySet abilities)
    {
        return
        [
            new((int)global::TokuseiType.RANDOM_12, "Random 1/2"),
            new((int)global::TokuseiType.RANDOM_123, "Random 1/2/Hidden"),
            new((int)global::TokuseiType.SET_1, FormatAbilitySlot(abilities.Ability1, "Ability 1")),
            new((int)global::TokuseiType.SET_2, FormatAbilitySlot(abilities.Ability2, "Ability 2")),
            new((int)global::TokuseiType.SET_3, FormatAbilitySlot(abilities.HiddenAbility, "Hidden Ability")),
        ];
    }

    private static string FormatAbilitySlot(string ability, string slot)
    {
        return string.Equals(ability, slot, StringComparison.Ordinal) ? slot : $"{ability} ({slot})";
    }

    internal static string FormatNature(global::SeikakuType value)
    {
        return NatureOptions.FirstOrDefault(option => option.Value == (int)value)?.Label
            ?? SvLabels.EnumName(value);
    }

    internal static string FormatTeraType(global::GemType value)
    {
        return TeraTypeOptions.FirstOrDefault(option => option.Value == (int)value)?.Label
            ?? SvLabels.EnumName(value);
    }

    private sealed class SvTrainerAbilityResolver
    {
        private readonly IReadOnlyDictionary<string, SvTrainerAbilitySet> abilitiesBySpeciesForm;

        private SvTrainerAbilityResolver(IReadOnlyDictionary<string, SvTrainerAbilitySet> abilitiesBySpeciesForm)
        {
            this.abilitiesBySpeciesForm = abilitiesBySpeciesForm;
        }

        public static SvTrainerAbilityResolver Empty { get; } = new(
            new Dictionary<string, SvTrainerAbilitySet>(StringComparer.Ordinal));

        public static SvTrainerAbilityResolver Load(
            OpenedProject project,
            SvWorkflowFileSource fileSource,
            SvTextLabelLookup labels,
            ICollection<ValidationDiagnostic> diagnostics)
        {
            try
            {
                var source = fileSource.Read(project, SvDataPaths.PersonalArray);
                var table = global::personal_table.GetRootAspersonal_table(new ByteBuffer(source.Bytes));
                var lookup = new Dictionary<string, SvTrainerAbilitySet>(StringComparer.Ordinal);
                for (var index = 0; index < table.EntryLength; index++)
                {
                    var row = table.Entry(index);
                    if (row?.Species is not { } species || !row.Value.IsPresent)
                    {
                        continue;
                    }

                    var key = CreateKey(species.Species, species.Form);
                    lookup.TryAdd(
                        key,
                        new SvTrainerAbilitySet(
                            labels.Ability(row.Value.Ability1),
                            labels.Ability(row.Value.Ability2),
                            labels.Ability(row.Value.AbilityHidden)));
                }

                return new SvTrainerAbilityResolver(lookup);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
            {
                diagnostics.Add(SvWorkflowSupport.Warning(
                    $"Trainer ability names could not be resolved from Pokemon Data: {exception.Message}",
                    $"romfs/{SvDataPaths.PersonalArray}"));
                return Empty;
            }
        }

        public SvTrainerAbilitySet Resolve(int species, int form)
        {
            return abilitiesBySpeciesForm.TryGetValue(CreateKey(species, form), out var exact)
                ? exact
                : abilitiesBySpeciesForm.TryGetValue(CreateKey(species, 0), out var baseForm)
                    ? baseForm
                    : SvTrainerAbilitySet.Empty;
        }

        private static string CreateKey(int species, int form)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{species}:{form}");
        }
    }

    private sealed record SvTrainerAbilitySet(
        string Ability1,
        string Ability2,
        string HiddenAbility)
    {
        public static SvTrainerAbilitySet Empty { get; } = new("Ability 1", "Ability 2", "Hidden Ability");
    }
}
