// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.Formats.ZA.Generated.GameData;
using KM.ZA.Data;
using KM.ZA.Workflows;
using System.Globalization;

namespace KM.ZA.Trainers;

internal sealed class ZaTrainersWorkflowService
{
    private const string WorkflowLabel = "Trainers";
    private const string WorkflowDescription = "Edit Pokemon Legends Z-A trainer data and trainer Pokemon.";
    public const string RankField = "rank";
    public const string MoneyField = "money";
    public const string MegaEvolutionField = "megaEvolution";
    public const string LastHandField = "lastHand";
    public const string AiFlagsField = "aiFlags";
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

    private static readonly IReadOnlyList<ZaTrainerEditableFieldOption> BooleanOptions =
    [
        new(0, "No"),
        new(1, "Yes"),
    ];

    private static readonly IReadOnlyList<ZaTrainerEditableFieldOption> GenderOptions =
    [
        new(-1, "Game default / random"),
        new(0, "Random"),
        new(1, "Male"),
        new(2, "Female"),
    ];

    private static readonly IReadOnlyList<ZaTrainerEditableFieldOption> NatureOptions =
    [
        new(-1, "Random / game default"),
        new(0, "Default (game behavior)"),
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

    private static readonly IReadOnlyList<ZaTrainerEditableFieldOption> RankOptions =
    [
        new(0, "None"),
        new(1, "Z"),
        new(2, "Y"),
        new(3, "X"),
        new(4, "W"),
        new(5, "V"),
        new(6, "U"),
        new(7, "T"),
        new(8, "S"),
        new(9, "R"),
        new(10, "Q"),
        new(11, "P"),
        new(12, "O"),
        new(13, "N"),
        new(14, "M"),
        new(15, "L"),
        new(16, "K"),
        new(17, "J"),
        new(18, "I"),
        new(19, "H"),
        new(20, "G"),
        new(21, "F"),
        new(22, "E"),
        new(23, "D"),
        new(24, "C"),
        new(25, "B"),
        new(26, "A"),
        new(27, "Infinite"),
    ];

    private static readonly IReadOnlyList<ZaTrainerEditableFieldOption> ShinyModeOptions =
    [
        new(0, "Default / not forced"),
        new(1, "Forced shiny"),
    ];

    private static readonly IReadOnlyList<ZaTrainerEditableField> BaseEditableFields =
    [
        CreateField(RankField, "Z-A rank", 0, 27, RankOptions),
        CreateField(MoneyField, "Money rate", 0, byte.MaxValue),
        CreateField(AiFlagsField, "AI flags", 0, byte.MaxValue),
        CreateField(MegaEvolutionField, "Mega Evolution", 0, 1, BooleanOptions, "boolean"),
        CreateField(LastHandField, "Last hand", 0, 1, BooleanOptions, "boolean"),
        CreateField(FormField, "Form", short.MinValue, short.MaxValue),
        CreateField(LevelField, "Level", 0, 100),
        CreateField(GenderField, "Gender", -1, 2, GenderOptions),
        CreateField(AbilityField, "Ability mode", 0, 255, CreateAbilityModeOptions(ZaTrainerAbilitySet.Empty)),
        CreateField(NatureField, "Nature", -1, 25, NatureOptions),
        CreateField(EvHpField, "HP EV", 0, int.MaxValue),
        CreateField(EvAttackField, "Attack EV", 0, int.MaxValue),
        CreateField(EvDefenseField, "Defense EV", 0, int.MaxValue),
        CreateField(EvSpecialAttackField, "Sp. Atk EV", 0, int.MaxValue),
        CreateField(EvSpecialDefenseField, "Sp. Def EV", 0, int.MaxValue),
        CreateField(EvSpeedField, "Speed EV", 0, int.MaxValue),
        CreateField(IvHpField, "HP IV", -1, int.MaxValue),
        CreateField(IvAttackField, "Attack IV", -1, int.MaxValue),
        CreateField(IvDefenseField, "Defense IV", -1, int.MaxValue),
        CreateField(IvSpecialAttackField, "Sp. Atk IV", -1, int.MaxValue),
        CreateField(IvSpecialDefenseField, "Sp. Def IV", -1, int.MaxValue),
        CreateField(IvSpeedField, "Speed IV", -1, int.MaxValue),
        CreateField(ShinyField, "Shiny mode", 0, 1, ShinyModeOptions),
    ];

    private readonly ZaWorkflowFileSource fileSource;

    public ZaTrainersWorkflowService(ZaWorkflowFileSource? fileSource = null)
    {
        this.fileSource = fileSource ?? new ZaWorkflowFileSource();
    }

    public ZaWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return ZaWorkflowSupport.CreateSummary(
            project,
            ZaWorkflowIds.Trainers,
            WorkflowLabel,
            WorkflowDescription);
    }

    public ZaTrainersWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var diagnostics = new List<ValidationDiagnostic>();
        ZaWorkflowFile? source = null;
        var labels = ZaTextLabelLookup.None();
        var pokemonAvailability = ZaPokemonAvailability.Unfiltered;
        var trainers = Array.Empty<ZaTrainerRecord>();

        try
        {
            labels = ZaTextLabelLookup.Load(project, fileSource, diagnostics, project.Paths);
            pokemonAvailability = ZaPokemonAvailability.Load(project, fileSource, diagnostics, WorkflowLabel);
            var abilityResolver = ZaTrainerAbilityResolver.Load(project, fileSource, labels, diagnostics);
            source = fileSource.Read(project, ZaDataPaths.TrainerDataArray);
            trainers = LoadRecords(source, labels, abilityResolver).ToArray();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(ZaWorkflowSupport.Error(
                $"Trainers could not be loaded: {exception.Message}",
                $"romfs/{ZaDataPaths.TrainerDataArray}"));
        }

        var summary = ZaWorkflowSupport.CreateSummary(
            project,
            ZaWorkflowIds.Trainers,
            WorkflowLabel,
            WorkflowDescription,
            diagnostics.Count == 0 ? null : diagnostics);

        return new ZaTrainersWorkflow(
            summary,
            trainers,
            CreateEditableFields(labels, pokemonAvailability),
            new ZaTrainersWorkflowStats(
                trainers.Length,
                trainers.Sum(trainer => trainer.Team.Count),
                source is null ? 0 : 1),
            diagnostics);
    }

    internal static IEnumerable<ZaTrainerRecord> LoadRecords(
        ZaWorkflowFile source,
        ZaTextLabelLookup labels,
        ZaTrainerAbilityResolver abilityResolver)
    {
        var table = ZaTrainerTable.GetRootAsZaTrainerTable(new ByteBuffer(source.Bytes));
        for (var index = 0; index < table.ValueLength; index++)
        {
            var trainer = table.Value(index);
            if (trainer is null)
            {
                continue;
            }

            yield return ToRecord(index, trainer.Value, source, labels, abilityResolver);
        }
    }

    private static ZaTrainerRecord ToRecord(
        int trainerId,
        ZaTrainerRow trainer,
        ZaWorkflowFile source,
        ZaTextLabelLookup labels,
        ZaTrainerAbilityResolver abilityResolver)
    {
        var aiFlags = PackAiFlags(trainer);
        var team = ReadTeam(trainer, labels, abilityResolver).ToArray();
        var classId = ToSmallClassId(trainer.TrainerType);
        var className = labels.TrainerTypeByIndex(classId) ?? "Trainer";
        var location = string.IsNullOrWhiteSpace(trainer.TrainerId)
            ? $"Trainer {trainerId.ToString(CultureInfo.InvariantCulture)}"
            : trainer.TrainerId!;

        return new ZaTrainerRecord(
            trainerId,
            labels.TrainerName(trainer.TrainerId, trainerId),
            classId,
            className,
            location,
            0,
            trainer.MegaEvolution ? "Mega Evolution" : "Trainer Battle",
            [],
            [],
            aiFlags,
            CreateAiStates(trainer),
            false,
            "Not used",
            false,
            trainer.MoneyRate,
            0,
            null,
            null,
            false,
            "none",
            team,
            new ZaTrainerProvenance(
                source.RelativePath,
                source.RelativePath,
                ClassSourceFile: null,
                source.SourceLayer,
                source.SourceLayer,
                ClassSourceLayer: null,
                source.FileState,
                source.FileState,
                ClassFileState: null),
            trainer.Rank,
            trainer.MegaEvolution,
            trainer.LastHand);
    }

    private static IEnumerable<ZaTrainerPokemonRecord> ReadTeam(
        ZaTrainerRow trainer,
        ZaTextLabelLookup labels,
        ZaTrainerAbilityResolver abilityResolver)
    {
        var slots = new[]
        {
            trainer.Pokemon1,
            trainer.Pokemon2,
            trainer.Pokemon3,
            trainer.Pokemon4,
            trainer.Pokemon5,
            trainer.Pokemon6,
        };

        for (var index = 0; index < slots.Length; index++)
        {
            var pokemon = slots[index];
            if (pokemon is null || pokemon.Value.SpeciesId == 0)
            {
                continue;
            }

            yield return ToPokemon(index, pokemon.Value, labels, abilityResolver);
        }
    }

    private static ZaTrainerPokemonRecord ToPokemon(
        int slot,
        ZaTrainerPokemon pokemon,
        ZaTextLabelLookup labels,
        ZaTrainerAbilityResolver abilityResolver)
    {
        var speciesId = pokemon.SpeciesId;
        var itemId = pokemon.Item;
        var moveIds = ReadMoves(pokemon);
        var abilities = abilityResolver.Resolve(speciesId, pokemon.FormId);
        var abilityOptions = CreateAbilityModeOptions(abilities);
        var evs = pokemon.Evs;
        var ivs = pokemon.Ivs;
        return new ZaTrainerPokemonRecord(
            slot,
            speciesId,
            labels.Pokemon(speciesId),
            pokemon.FormId,
            pokemon.Level,
            itemId,
            itemId > 0 ? labels.Item(itemId) : null,
            moveIds,
            moveIds.Select(move => move <= 0 ? "None" : labels.Move(move)).ToArray(),
            pokemon.Sex,
            FormatGender(pokemon.Sex),
            pokemon.Ability,
            abilityOptions.FirstOrDefault(option => option.Value == pokemon.Ability)?.Label
                ?? abilityResolver.FormatAbilityMode(pokemon.Ability, abilities),
            pokemon.Nature,
            FormatNature(pokemon.Nature),
            new ZaTrainerPokemonStatsRecord(
                evs?.Hp ?? 0,
                evs?.Atk ?? 0,
                evs?.Def ?? 0,
                evs?.SpAtk ?? 0,
                evs?.SpDef ?? 0,
                evs?.Agi ?? 0),
            0,
            false,
            new ZaTrainerPokemonStatsRecord(
                ivs?.Hp ?? 0,
                ivs?.Atk ?? 0,
                ivs?.Def ?? 0,
                ivs?.SpAtk ?? 0,
                ivs?.SpDef ?? 0,
                ivs?.Agi ?? 0),
            pokemon.RareType == 2,
            false)
        {
            AbilityOptions = abilityOptions,
        };
    }

    private static IReadOnlyList<int> ReadMoves(ZaTrainerPokemon pokemon)
    {
        return
        [
            ReadMoveId(pokemon.Move1),
            ReadMoveId(pokemon.Move2),
            ReadMoveId(pokemon.Move3),
            ReadMoveId(pokemon.Move4),
        ];
    }

    private static int ReadMoveId(ZaTrainerMove? move)
    {
        return move?.MoveId ?? 0;
    }

    private static int PackAiFlags(ZaTrainerRow trainer)
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

    private static IReadOnlyList<ZaTrainerAiFlagState> CreateAiStates(ZaTrainerRow trainer)
    {
        return CreateAiStates(PackAiFlags(trainer));
    }

    internal static IReadOnlyList<ZaTrainerAiFlagState> CreateAiStates(int flags)
    {
        var definitions = new[]
        {
            (0, "Basic", "Enables baseline move selection and battle decisions."),
            (1, "High", "Uses stronger scoring for move choice, targets, and matchup checks."),
            (2, "Expert", "Enables the highest trainer AI tier for advanced battle decisions."),
            (3, "Double", "Uses double-battle-aware partner, target, and spread move logic."),
            (4, "Raid", "Uses raid-style AI checks for encounters that share raid battle behavior."),
            (5, "Weak", "Allows weakness-aware choices against the opponent's active Pokemon."),
            (6, "Item", "Allows the trainer AI to consider configured battle item usage."),
            (7, "Change", "Allows the trainer AI to consider switching Pokemon during battle."),
        };

        return definitions
            .Select(definition =>
            {
                var mask = 1 << definition.Item1;
                return new ZaTrainerAiFlagState(
                    definition.Item1,
                    mask,
                    definition.Item2,
                    definition.Item3,
                    (flags & mask) != 0);
            })
            .ToArray();
    }

    private static IReadOnlyList<ZaTrainerEditableField> CreateEditableFields(
        ZaTextLabelLookup labels,
        ZaPokemonAvailability pokemonAvailability)
    {
        var speciesOptions = CreateSpeciesOptions(labels, pokemonAvailability);
        var speciesMaximumValue = Math.Max(labels.PokemonNameCount - 1, MaximumOptionValue(speciesOptions, 0));
        var itemOptions = CreateIndexedOptions(labels.ItemNameCount, labels.Item, includeNone: true);
        var moveOptions = CreateIndexedOptions(labels.MoveNameCount, labels.Move, includeNone: true);
        var fields = new List<ZaTrainerEditableField>();

        foreach (var field in BaseEditableFields)
        {
            if (field.Field == FormField)
            {
                fields.Add(CreateField(SpeciesIdField, "Species", 0, speciesMaximumValue, speciesOptions));
            }

            fields.Add(field);

            if (field.Field == LevelField)
            {
                fields.Add(CreateField(HeldItemIdField, "Held item", 0, MaximumOptionValue(itemOptions, int.MaxValue), itemOptions));
                fields.Add(CreateField(Move1IdField, "Move 1", 0, MaximumOptionValue(moveOptions, ushort.MaxValue), moveOptions));
                fields.Add(CreateField(Move2IdField, "Move 2", 0, MaximumOptionValue(moveOptions, ushort.MaxValue), moveOptions));
                fields.Add(CreateField(Move3IdField, "Move 3", 0, MaximumOptionValue(moveOptions, ushort.MaxValue), moveOptions));
                fields.Add(CreateField(Move4IdField, "Move 4", 0, MaximumOptionValue(moveOptions, ushort.MaxValue), moveOptions));
            }
        }

        return fields;
    }

    private static IReadOnlyList<ZaTrainerEditableFieldOption> CreateSpeciesOptions(
        ZaTextLabelLookup labels,
        ZaPokemonAvailability pokemonAvailability)
    {
        return pokemonAvailability
            .CreateSpeciesOptions(labels.PokemonNameCount, labels.Pokemon, includeNone: true)
            .Select(option => new ZaTrainerEditableFieldOption(option.Value, option.Label))
            .ToArray();
    }

    private static IReadOnlyList<ZaTrainerEditableFieldOption> CreateIndexedOptions(
        int count,
        Func<int, string> resolveName,
        bool includeNone)
    {
        var firstValue = includeNone ? 0 : 1;
        if (count <= firstValue)
        {
            return includeNone ? [new(0, "0 None")] : Array.Empty<ZaTrainerEditableFieldOption>();
        }

        return Enumerable
            .Range(firstValue, count - firstValue)
            .Select(value =>
            {
                var label = value == 0 ? "None" : resolveName(value);
                return new ZaTrainerEditableFieldOption(
                    value,
                    $"{value.ToString(CultureInfo.InvariantCulture)} {label}");
            })
            .ToArray();
    }

    private static int MaximumOptionValue(IReadOnlyList<ZaTrainerEditableFieldOption> options, int fallback)
    {
        return options.Count == 0 ? fallback : options.Max(option => option.Value);
    }

    private static ZaTrainerEditableField CreateField(
        string field,
        string label,
        int minimumValue,
        int maximumValue,
        IReadOnlyList<ZaTrainerEditableFieldOption>? options = null,
        string valueKind = "integer")
    {
        return new ZaTrainerEditableField(
            field,
            label,
            valueKind,
            minimumValue,
            maximumValue,
            options ?? Array.Empty<ZaTrainerEditableFieldOption>());
    }

    internal static IReadOnlyList<ZaTrainerEditableFieldOption> CreateAbilityModeOptions(ZaTrainerAbilitySet abilities)
    {
        return
        [
            new(0, "Random 1/2"),
            new(1, "Random 1/2/Hidden"),
            new(2, FormatAbilitySlot(abilities.Ability1, "Ability 1")),
            new(3, FormatAbilitySlot(abilities.Ability2, "Ability 2")),
            new(4, FormatAbilitySlot(abilities.HiddenAbility, "Hidden Ability")),
            new(255, "Game default / random"),
        ];
    }

    private static string FormatAbilitySlot(string ability, string slot)
    {
        return string.Equals(ability, slot, StringComparison.Ordinal) ? slot : $"{ability} ({slot})";
    }

    internal static string FormatGender(int value)
    {
        return value switch
        {
            -1 => "Game default / random",
            0 => "Random",
            1 => "Male",
            2 => "Female",
            _ => $"Gender {value.ToString(CultureInfo.InvariantCulture)}",
        };
    }

    internal static string FormatNature(int value)
    {
        return NatureOptions.FirstOrDefault(option => option.Value == value)?.Label
            ?? $"Nature {value.ToString(CultureInfo.InvariantCulture)}";
    }

    private static int ToSmallClassId(ulong value)
    {
        return value <= int.MaxValue ? (int)value : 0;
    }
}
