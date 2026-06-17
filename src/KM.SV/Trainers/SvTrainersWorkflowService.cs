// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.SwSh.Trainers;
using KM.SwSh.Workflows;
using KM.SV.Data;
using KM.SV.Workflows;
using System.Globalization;

namespace KM.SV.Trainers;

internal sealed class SvTrainersWorkflowService
{
    private const string WorkflowLabel = "Trainers";
    private const string WorkflowDescription = "Edit Scarlet/Violet trainer data and trainer Pokemon.";
    internal const string TeraTypeField = "teraType";

    private static readonly IReadOnlyList<SwShTrainerEditableFieldOption> BooleanOptions =
    [
        new(0, "No"),
        new(1, "Yes"),
    ];

    private static readonly IReadOnlyList<SwShTrainerEditableFieldOption> BattleTypeOptions =
    [
        new(0, "1v1"),
        new(1, "2v2"),
    ];

    private static readonly IReadOnlyList<SwShTrainerEditableFieldOption> GenderOptions =
    [
        new(0, "Random"),
        new(1, "Male"),
        new(2, "Female"),
    ];

    private static readonly IReadOnlyList<SwShTrainerEditableFieldOption> AbilityModeOptions =
    [
        new(0, "Random 1/2"),
        new(1, "Random 1/2/Hidden"),
        new(2, "Ability 1"),
        new(3, "Ability 2"),
        new(4, "Hidden Ability"),
    ];

    private static readonly IReadOnlyList<SwShTrainerEditableFieldOption> NatureOptions =
    [
        new(0, "Default"),
        new(1, "Hardy"),
        new(2, "Lonely"),
        new(3, "Brave"),
        new(4, "Adamant"),
        new(5, "Naughty"),
        new(6, "Bold"),
        new(7, "Docile"),
        new(8, "Relaxed"),
        new(9, "Impish"),
        new(10, "Lax"),
        new(11, "Timid"),
        new(12, "Hasty"),
        new(13, "Serious"),
        new(14, "Jolly"),
        new(15, "Naive"),
        new(16, "Modest"),
        new(17, "Mild"),
        new(18, "Quiet"),
        new(19, "Bashful"),
        new(20, "Rash"),
        new(21, "Calm"),
        new(22, "Gentle"),
        new(23, "Sassy"),
        new(24, "Careful"),
        new(25, "Quirky"),
    ];

    private static readonly IReadOnlyList<SwShTrainerEditableFieldOption> ShinyModeOptions =
    [
        new(0, "Default"),
        new(1, "Forced shiny"),
    ];

    private static readonly IReadOnlyList<SwShTrainerEditableFieldOption> TeraTypeOptions =
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

    private static readonly IReadOnlyList<SwShTrainerEditableField> BaseEditableFields =
    [
        CreateField(SwShTrainersWorkflowService.BattleTypeField, "Battle type", 0, 1, BattleTypeOptions),
        CreateField(SwShTrainersWorkflowService.MoneyField, "Money rate", sbyte.MinValue, sbyte.MaxValue),
        CreateField(SwShTrainersWorkflowService.AiFlagsField, "AI flags", 0, byte.MaxValue),
        CreateField("isStrong", "Strong trainer", 0, 1, BooleanOptions, "boolean"),
        CreateField("changeGem", "Change Tera type", 0, 1, BooleanOptions, "boolean"),
        CreateField(SwShTrainersWorkflowService.FormField, "Form", short.MinValue, short.MaxValue),
        CreateField(SwShTrainersWorkflowService.LevelField, "Level", 0, 100),
        CreateField(SwShTrainersWorkflowService.GenderField, "Gender", 0, 2, GenderOptions),
        CreateField(SwShTrainersWorkflowService.AbilityField, "Ability mode", 0, 4, AbilityModeOptions),
        CreateField(SwShTrainersWorkflowService.NatureField, "Nature", 0, 25, NatureOptions),
        CreateField(TeraTypeField, "Tera type", 0, 101, TeraTypeOptions),
        CreateField(SwShTrainersWorkflowService.EvHpField, "HP EV", 0, int.MaxValue),
        CreateField(SwShTrainersWorkflowService.EvAttackField, "Attack EV", 0, int.MaxValue),
        CreateField(SwShTrainersWorkflowService.EvDefenseField, "Defense EV", 0, int.MaxValue),
        CreateField(SwShTrainersWorkflowService.EvSpecialAttackField, "Sp. Atk EV", 0, int.MaxValue),
        CreateField(SwShTrainersWorkflowService.EvSpecialDefenseField, "Sp. Def EV", 0, int.MaxValue),
        CreateField(SwShTrainersWorkflowService.EvSpeedField, "Speed EV", 0, int.MaxValue),
        CreateField(SwShTrainersWorkflowService.IvHpField, "HP IV", 0, int.MaxValue),
        CreateField(SwShTrainersWorkflowService.IvAttackField, "Attack IV", 0, int.MaxValue),
        CreateField(SwShTrainersWorkflowService.IvDefenseField, "Defense IV", 0, int.MaxValue),
        CreateField(SwShTrainersWorkflowService.IvSpecialAttackField, "Sp. Atk IV", 0, int.MaxValue),
        CreateField(SwShTrainersWorkflowService.IvSpecialDefenseField, "Sp. Def IV", 0, int.MaxValue),
        CreateField(SwShTrainersWorkflowService.IvSpeedField, "Speed IV", 0, int.MaxValue),
        CreateField(SwShTrainersWorkflowService.ShinyField, "Shiny mode", 0, 1, ShinyModeOptions),
    ];

    private readonly SvWorkflowFileSource fileSource;

    public SvTrainersWorkflowService(SvWorkflowFileSource? fileSource = null)
    {
        this.fileSource = fileSource ?? new SvWorkflowFileSource();
    }

    public SwShWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return SvWorkflowSupport.CreateSummary(
            project,
            SwShWorkflowIds.Trainers,
            WorkflowLabel,
            WorkflowDescription);
    }

    public SwShTrainersWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var diagnostics = new List<ValidationDiagnostic>();
        SvWorkflowFile? source = null;
        var labels = SvTextLabelLookup.None();
        var trainers = Array.Empty<SwShTrainerRecord>();

        try
        {
            labels = SvTextLabelLookup.Load(project, fileSource, diagnostics);
            source = fileSource.Read(project, SvDataPaths.TrainerDataArray);
            trainers = LoadRecords(source, labels).ToArray();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(SvWorkflowSupport.Error(
                $"Trainers could not be loaded: {exception.Message}",
                $"romfs/{SvDataPaths.TrainerDataArray}"));
        }

        var summary = SvWorkflowSupport.CreateSummary(
            project,
            SwShWorkflowIds.Trainers,
            WorkflowLabel,
            WorkflowDescription,
            diagnostics.Count == 0 ? null : diagnostics);

        return new SwShTrainersWorkflow(
            summary,
            trainers,
            CreateEditableFields(labels),
            new SwShTrainersWorkflowStats(
                trainers.Length,
                trainers.Sum(trainer => trainer.Team.Count),
                source is null ? 0 : 1),
            diagnostics);
    }

    private static IEnumerable<SwShTrainerRecord> LoadRecords(SvWorkflowFile source, SvTextLabelLookup labels)
    {
        var table = global::trainer.TrdataMainArray.GetRootAsTrdataMainArray(new ByteBuffer(source.Bytes));
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var trainer = table.Values(index);
            if (trainer is null)
            {
                continue;
            }

            yield return ToRecord(index, trainer.Value, source, labels);
        }
    }

    private static SwShTrainerRecord ToRecord(
        int trainerId,
        global::trainer.TrdataMain trainer,
        SvWorkflowFile source,
        SvTextLabelLookup labels)
    {
        var aiFlags = PackAiFlags(trainer);
        var team = ReadTeam(trainer, labels).ToArray();
        var aiStates = CreateAiStates(trainer);
        var className = labels.TrainerType(trainer.TrainerType);
        var name = labels.TrainerName(trainer.TrNameLabel, trainerId);

        return new SwShTrainerRecord(
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
            new SwShTrainerProvenance(
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

    private static IEnumerable<SwShTrainerPokemonRecord> ReadTeam(
        global::trainer.TrdataMain trainer,
        SvTextLabelLookup labels)
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

            yield return ToPokemon(index, pokemon.Value, labels);
        }
    }

    private static SwShTrainerPokemonRecord ToPokemon(
        int slot,
        global::PokeDataBattle pokemon,
        SvTextLabelLookup labels)
    {
        var speciesId = (int)pokemon.DevId;
        var itemId = (int)pokemon.Item;
        var moveIds = ReadMoves(pokemon);
        var evs = pokemon.EffortValue;
        var ivs = pokemon.TalentValue;
        var record = new SwShTrainerPokemonRecord(
            slot,
            speciesId,
            labels.Pokemon(speciesId),
            pokemon.FormId,
            pokemon.Level,
            itemId,
            itemId > 0 ? labels.Item(itemId) : null,
            moveIds,
            moveIds.Select(labels.Move).ToArray(),
            (int)pokemon.Sex,
            FormatGender(pokemon.Sex),
            (int)pokemon.Tokusei,
            FormatAbilityMode(pokemon.Tokusei),
            (int)pokemon.Seikaku,
            FormatNature(pokemon.Seikaku),
            new SwShTrainerPokemonStatsRecord(
                evs?.Hp ?? 0,
                evs?.Atk ?? 0,
                evs?.Def ?? 0,
                evs?.SpAtk ?? 0,
                evs?.SpDef ?? 0,
                evs?.Agi ?? 0),
            DynamaxLevel: 0,
            CanGigantamax: false,
            new SwShTrainerPokemonStatsRecord(
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
            AbilityOptions = AbilityModeOptions,
        };

        return record;
    }

    private static IReadOnlyList<int> ReadMoves(global::PokeDataBattle pokemon)
    {
        var moves = new List<int>();
        AddMove(moves, pokemon.Waza1);
        AddMove(moves, pokemon.Waza2);
        AddMove(moves, pokemon.Waza3);
        AddMove(moves, pokemon.Waza4);
        return moves;
    }

    private static void AddMove(List<int> moves, global::WazaSet? move)
    {
        if (move is not null && (int)move.Value.WazaId > 0)
        {
            moves.Add((int)move.Value.WazaId);
        }
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

    private static IReadOnlyList<SwShTrainerAiFlagState> CreateAiStates(global::trainer.TrdataMain trainer)
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
            .Select(flag => new SwShTrainerAiFlagState(flag.Item1, 1 << flag.Item1, flag.Item2, flag.Item3, flag.Item4))
            .ToArray();
    }

    private static IReadOnlyList<SwShTrainerEditableField> CreateEditableFields(SvTextLabelLookup labels)
    {
        var speciesOptions = CreateIndexedOptions(labels.PokemonNameCount, labels.Pokemon, includeNone: true);
        var itemOptions = CreateIndexedOptions(labels.ItemNameCount, labels.Item, includeNone: true);
        var moveOptions = CreateIndexedOptions(labels.MoveNameCount, labels.Move, includeNone: true);
        var fields = new List<SwShTrainerEditableField>();

        foreach (var field in BaseEditableFields)
        {
            if (field.Field == SwShTrainersWorkflowService.FormField)
            {
                fields.Add(CreateField(SwShTrainersWorkflowService.SpeciesIdField, "Species", 0, MaximumOptionValue(speciesOptions, ushort.MaxValue), speciesOptions));
            }

            fields.Add(field);

            if (field.Field == SwShTrainersWorkflowService.LevelField)
            {
                fields.Add(CreateField(SwShTrainersWorkflowService.HeldItemIdField, "Held item", 0, MaximumOptionValue(itemOptions, int.MaxValue), itemOptions));
                fields.Add(CreateField(SwShTrainersWorkflowService.Move1IdField, "Move 1", 0, MaximumOptionValue(moveOptions, ushort.MaxValue), moveOptions));
                fields.Add(CreateField(SwShTrainersWorkflowService.Move2IdField, "Move 2", 0, MaximumOptionValue(moveOptions, ushort.MaxValue), moveOptions));
                fields.Add(CreateField(SwShTrainersWorkflowService.Move3IdField, "Move 3", 0, MaximumOptionValue(moveOptions, ushort.MaxValue), moveOptions));
                fields.Add(CreateField(SwShTrainersWorkflowService.Move4IdField, "Move 4", 0, MaximumOptionValue(moveOptions, ushort.MaxValue), moveOptions));
            }
        }

        return fields;
    }

    private static IReadOnlyList<SwShTrainerEditableFieldOption> CreateIndexedOptions(
        int count,
        Func<int, string> resolveName,
        bool includeNone)
    {
        var firstValue = includeNone ? 0 : 1;
        if (count <= firstValue)
        {
            return includeNone ? [new(0, "0 None")] : Array.Empty<SwShTrainerEditableFieldOption>();
        }

        return Enumerable
            .Range(firstValue, count - firstValue)
            .Select(value =>
            {
                var label = value == 0 ? "None" : resolveName(value);
                return new SwShTrainerEditableFieldOption(
                    value,
                    $"{value.ToString(CultureInfo.InvariantCulture)} {label}");
            })
            .ToArray();
    }

    private static int MaximumOptionValue(
        IReadOnlyList<SwShTrainerEditableFieldOption> options,
        int fallback)
    {
        return options.Count == 0 ? fallback : options.Max(option => option.Value);
    }

    private static SwShTrainerEditableField CreateField(
        string field,
        string label,
        int minimumValue,
        int maximumValue,
        IReadOnlyList<SwShTrainerEditableFieldOption>? options = null,
        string valueKind = "integer")
    {
        return new SwShTrainerEditableField(
            field,
            label,
            valueKind,
            minimumValue,
            maximumValue,
            options ?? Array.Empty<SwShTrainerEditableFieldOption>());
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
        return value switch
        {
            global::TokuseiType.RANDOM_12 => "Random 1/2",
            global::TokuseiType.RANDOM_123 => "Random 1/2/Hidden",
            global::TokuseiType.SET_1 => "Ability 1",
            global::TokuseiType.SET_2 => "Ability 2",
            global::TokuseiType.SET_3 => "Hidden Ability",
            _ => SvLabels.EnumName(value),
        };
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
}
