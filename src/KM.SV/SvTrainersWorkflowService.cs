// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.SwSh.Trainers;
using KM.SwSh.Workflows;

namespace KM.SV;

internal sealed class SvTrainersWorkflowService
{
    private static readonly IReadOnlyList<SwShTrainerEditableFieldOption> BooleanOptions =
    [
        new(0, "No"),
        new(1, "Yes"),
    ];

    private static readonly IReadOnlyList<SwShTrainerEditableField> EditableFields =
    [
        CreateField(SwShTrainersWorkflowService.BattleTypeField, "Battle type", 0, int.MaxValue),
        CreateField(SwShTrainersWorkflowService.MoneyField, "Money rate", sbyte.MinValue, sbyte.MaxValue),
        CreateField(SwShTrainersWorkflowService.AiFlagsField, "AI flags", 0, byte.MaxValue),
        CreateField("isStrong", "Strong trainer", 0, 1, BooleanOptions, "boolean"),
        CreateField("changeGem", "Change Tera type", 0, 1, BooleanOptions, "boolean"),
        CreateField(SwShTrainersWorkflowService.SpeciesIdField, "Species", 0, ushort.MaxValue),
        CreateField(SwShTrainersWorkflowService.FormField, "Form", short.MinValue, short.MaxValue),
        CreateField(SwShTrainersWorkflowService.LevelField, "Level", 0, 100),
        CreateField(SwShTrainersWorkflowService.HeldItemIdField, "Held item", 0, int.MaxValue),
        CreateField(SwShTrainersWorkflowService.Move1IdField, "Move 1", 0, ushort.MaxValue),
        CreateField(SwShTrainersWorkflowService.Move2IdField, "Move 2", 0, ushort.MaxValue),
        CreateField(SwShTrainersWorkflowService.Move3IdField, "Move 3", 0, ushort.MaxValue),
        CreateField(SwShTrainersWorkflowService.Move4IdField, "Move 4", 0, ushort.MaxValue),
        CreateField(SwShTrainersWorkflowService.GenderField, "Gender", 0, int.MaxValue),
        CreateField(SwShTrainersWorkflowService.AbilityField, "Ability mode", 0, int.MaxValue),
        CreateField(SwShTrainersWorkflowService.NatureField, "Nature", 0, int.MaxValue),
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
        CreateField(SwShTrainersWorkflowService.ShinyField, "Shiny mode", 0, int.MaxValue),
        CreateField("teraType", "Tera type", 0, int.MaxValue),
    ];

    private readonly SvWorkflowFileSource fileSource;

    public SvTrainersWorkflowService(SvWorkflowFileSource? fileSource = null)
    {
        this.fileSource = fileSource ?? new SvWorkflowFileSource();
    }

    public SwShTrainersWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var diagnostics = new List<ValidationDiagnostic>();
        SvWorkflowFile? source = null;
        var trainers = Array.Empty<SwShTrainerRecord>();

        try
        {
            source = fileSource.Read(project, SvDataPaths.TrainerDataArray);
            trainers = LoadRecords(source).ToArray();
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
            "Trainers",
            "Edit Scarlet/Violet trainer data and trainer Pokemon.",
            diagnostics.Count == 0 ? null : diagnostics);

        return new SwShTrainersWorkflow(
            summary,
            trainers,
            EditableFields,
            new SwShTrainersWorkflowStats(
                trainers.Length,
                trainers.Sum(trainer => trainer.Team.Count),
                source is null ? 0 : 1),
            diagnostics);
    }

    private static IEnumerable<SwShTrainerRecord> LoadRecords(SvWorkflowFile source)
    {
        var table = global::trainer.TrdataMainArray.GetRootAsTrdataMainArray(new ByteBuffer(source.Bytes));
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var trainer = table.Values(index);
            if (trainer is null)
            {
                continue;
            }

            yield return ToRecord(index, trainer.Value, source);
        }
    }

    private static SwShTrainerRecord ToRecord(int trainerId, global::trainer.TrdataMain trainer, SvWorkflowFile source)
    {
        var aiFlags = PackAiFlags(trainer);
        var team = ReadTeam(trainer).ToArray();
        var aiStates = CreateAiStates(trainer);
        var className = string.IsNullOrWhiteSpace(trainer.TrainerType) ? "Trainer" : trainer.TrainerType;
        var name = string.IsNullOrWhiteSpace(trainer.TrNameLabel) ? trainer.Trid ?? $"Trainer {trainerId}" : trainer.TrNameLabel;

        return new SwShTrainerRecord(
            trainerId,
            name,
            TrainerClassId: 0,
            className,
            Location: trainer.Trid ?? string.Empty,
            (int)trainer.BattleType,
            SvLabels.EnumName(trainer.BattleType),
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

    private static IEnumerable<SwShTrainerPokemonRecord> ReadTeam(global::trainer.TrdataMain trainer)
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

            yield return ToPokemon(index, pokemon.Value);
        }
    }

    private static SwShTrainerPokemonRecord ToPokemon(int slot, global::PokeDataBattle pokemon)
    {
        var speciesId = (int)pokemon.DevId;
        var itemId = (int)pokemon.Item;
        var moveIds = ReadMoves(pokemon);
        var evs = pokemon.EffortValue;
        var ivs = pokemon.TalentValue;
        var record = new SwShTrainerPokemonRecord(
            slot,
            speciesId,
            SvLabels.Pokemon(speciesId),
            pokemon.FormId,
            pokemon.Level,
            itemId,
            itemId > 0 ? SvLabels.Item(itemId) : null,
            moveIds,
            moveIds.Select(SvLabels.Move).ToArray(),
            (int)pokemon.Sex,
            SvLabels.EnumName(pokemon.Sex),
            (int)pokemon.Tokusei,
            SvLabels.EnumName(pokemon.Tokusei),
            (int)pokemon.Seikaku,
            SvLabels.EnumName(pokemon.Seikaku),
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
            CanDynamax: true)
        {
            AbilityOptions =
            [
                new(0, "Random 1/2"),
                new(1, "Random 1/2/Hidden"),
                new(2, "Ability 1"),
                new(3, "Ability 2"),
                new(4, "Hidden Ability"),
            ],
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
}
