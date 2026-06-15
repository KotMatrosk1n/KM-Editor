// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Pokemon;
using KM.SwSh.Workflows;

namespace KM.SV;

internal sealed class SvPokemonWorkflowService
{
    private static readonly IReadOnlyList<SwShPokemonEditableField> EditableFields =
    [
        CreateField(SwShPokemonWorkflowService.HPField, "HP", "Base Stats", 0, byte.MaxValue),
        CreateField(SwShPokemonWorkflowService.AttackField, "Attack", "Base Stats", 0, byte.MaxValue),
        CreateField(SwShPokemonWorkflowService.DefenseField, "Defense", "Base Stats", 0, byte.MaxValue),
        CreateField(SwShPokemonWorkflowService.SpecialAttackField, "Sp. Atk", "Base Stats", 0, byte.MaxValue),
        CreateField(SwShPokemonWorkflowService.SpecialDefenseField, "Sp. Def", "Base Stats", 0, byte.MaxValue),
        CreateField(SwShPokemonWorkflowService.SpeedField, "Speed", "Base Stats", 0, byte.MaxValue),
        CreateField(SwShPokemonWorkflowService.Type1Field, "Type 1", "Traits", 0, 17),
        CreateField(SwShPokemonWorkflowService.Type2Field, "Type 2", "Traits", 0, 17),
        CreateField(SwShPokemonWorkflowService.Ability1Field, "Ability 1", "Abilities", 0, ushort.MaxValue),
        CreateField(SwShPokemonWorkflowService.Ability2Field, "Ability 2", "Abilities", 0, ushort.MaxValue),
        CreateField(SwShPokemonWorkflowService.HiddenAbilityField, "Hidden Ability", "Abilities", 0, ushort.MaxValue),
        CreateField(SwShPokemonWorkflowService.CatchRateField, "Catch Rate", "Identity", 0, byte.MaxValue),
        CreateField(SwShPokemonWorkflowService.EvolutionStageField, "Evolution Stage", "Identity", 0, byte.MaxValue),
        CreateField(SwShPokemonWorkflowService.EVYieldHPField, "HP EV Yield", "EV Yield", 0, byte.MaxValue),
        CreateField(SwShPokemonWorkflowService.EVYieldAttackField, "Attack EV Yield", "EV Yield", 0, byte.MaxValue),
        CreateField(SwShPokemonWorkflowService.EVYieldDefenseField, "Defense EV Yield", "EV Yield", 0, byte.MaxValue),
        CreateField(SwShPokemonWorkflowService.EVYieldSpecialAttackField, "Sp. Atk EV Yield", "EV Yield", 0, byte.MaxValue),
        CreateField(SwShPokemonWorkflowService.EVYieldSpecialDefenseField, "Sp. Def EV Yield", "EV Yield", 0, byte.MaxValue),
        CreateField(SwShPokemonWorkflowService.EVYieldSpeedField, "Speed EV Yield", "EV Yield", 0, byte.MaxValue),
        CreateField(SwShPokemonWorkflowService.GenderRatioField, "Gender Ratio", "Identity", 0, byte.MaxValue),
        CreateField(SwShPokemonWorkflowService.HatchCyclesField, "Hatch Cycles", "Identity", 0, byte.MaxValue),
        CreateField(SwShPokemonWorkflowService.BaseFriendshipField, "Base Friendship", "Identity", 0, byte.MaxValue),
        CreateField(SwShPokemonWorkflowService.ExpGrowthField, "EXP Growth", "Identity", 0, byte.MaxValue),
        CreateField(SwShPokemonWorkflowService.EggGroup1Field, "Egg Group 1", "Breeding", 0, byte.MaxValue),
        CreateField(SwShPokemonWorkflowService.EggGroup2Field, "Egg Group 2", "Breeding", 0, byte.MaxValue),
        CreateField(SwShPokemonWorkflowService.BaseExperienceField, "Base EXP Addend", "Identity", short.MinValue, short.MaxValue),
        CreateField(SwShPokemonWorkflowService.FormField, "Form", "Forms/Dex", 0, ushort.MaxValue),
        CreateField(SwShPokemonWorkflowService.ModelIdField, "Model ID", "Forms/Dex", 0, ushort.MaxValue),
        CreateField(SwShPokemonWorkflowService.ColorField, "Color", "Forms/Dex", 0, byte.MaxValue),
        CreateField(SwShPokemonWorkflowService.HeightField, "Height", "Forms/Dex", 0, ushort.MaxValue),
        CreateField(SwShPokemonWorkflowService.WeightField, "Weight", "Forms/Dex", 0, ushort.MaxValue),
        CreateField(SwShPokemonWorkflowService.IsPresentInGameField, "Present In Game", "Flags", 0, 1, BooleanOptions),
        CreateField(SwShPokemonWorkflowService.RegionalDexIndexField, "Paldea Dex", "Forms/Dex", 0, ushort.MaxValue),
        CreateField(SwShPokemonWorkflowService.ArmorDexIndexField, "Kitakami Dex", "Forms/Dex", 0, ushort.MaxValue),
        CreateField(SwShPokemonWorkflowService.CrownDexIndexField, "Blueberry Dex", "Forms/Dex", 0, ushort.MaxValue),
    ];

    private static readonly IReadOnlyList<SwShPokemonEditableFieldOption> BooleanOptions =
    [
        new(0, "No"),
        new(1, "Yes"),
    ];

    private readonly SvWorkflowFileSource fileSource;

    public SvPokemonWorkflowService(SvWorkflowFileSource? fileSource = null)
    {
        this.fileSource = fileSource ?? new SvWorkflowFileSource();
    }

    public SwShPokemonWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var diagnostics = new List<ValidationDiagnostic>();
        SvWorkflowFile? source = null;
        var pokemon = Array.Empty<SwShPokemonRecord>();

        try
        {
            source = fileSource.Read(project, SvDataPaths.PersonalArray);
            pokemon = LoadRecords(source).ToArray();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(SvWorkflowSupport.Error(
                $"Pokemon Data could not be loaded: {exception.Message}",
                $"romfs/{SvDataPaths.PersonalArray}"));
        }

        var summary = SvWorkflowSupport.CreateSummary(
            project,
            SwShWorkflowIds.Pokemon,
            "Pokemon Data",
            "Edit Scarlet/Violet personal data, evolutions, learnsets, and move compatibility.",
            diagnostics.Count == 0 ? null : diagnostics);

        var stats = new SwShPokemonWorkflowStats(
            pokemon.Length,
            pokemon.Count(record => record.DexPresence.IsPresentInGame),
            pokemon.Sum(record => record.Evolutions.Count),
            pokemon.Sum(record => record.Learnset.Count),
            source is null ? 0 : 1);

        return new SwShPokemonWorkflow(
            summary,
            pokemon,
            stats,
            CreateEvolutionOptions(pokemon),
            CreateMoveOptions(pokemon),
            EditableFields,
            diagnostics);
    }

    private static IEnumerable<SwShPokemonRecord> LoadRecords(SvWorkflowFile source)
    {
        var table = global::personal_table.GetRootAspersonal_table(new ByteBuffer(source.Bytes));
        for (var index = 0; index < table.EntryLength; index++)
        {
            var entry = table.Entry(index);
            if (entry is null)
            {
                continue;
            }

            yield return ToRecord(index, entry.Value, source);
        }
    }

    private static SwShPokemonRecord ToRecord(int personalId, global::personal entry, SvWorkflowFile source)
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
        var stats = new SwShPokemonBaseStats(
            hp,
            attack,
            defense,
            specialAttack,
            specialDefense,
            speed,
            total);
        var abilities = new SwShPokemonAbilitySet(
            entry.Ability1,
            SvLabels.Ability(entry.Ability1),
            entry.Ability2,
            SvLabels.Ability(entry.Ability2),
            entry.AbilityHidden,
            SvLabels.Ability(entry.AbilityHidden));
        var dexPresence = new SwShPokemonDexPresence(
            entry.IsPresent,
            paldeaDexIndex > 0 || kitakamiDexIndex > 0 || blueberryDexIndex > 0,
            paldeaDexIndex,
            kitakamiDexIndex,
            blueberryDexIndex);
        var details = new SwShPokemonPersonalDetails(
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
            entry.ExpAddend,
            height,
            weight,
            model,
            eggSpecies,
            eggForm,
            IsRegionalForm: false,
            entry.TypeChangeDisallowed,
            paldeaDexIndex,
            form,
            kitakamiDexIndex,
            blueberryDexIndex);

        return new SwShPokemonRecord(
            personalId,
            speciesId,
            form,
            SvLabels.Pokemon(speciesId),
            form == 0 ? "Base" : $"Form {form}",
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
            entry.ExpAddend,
            height,
            weight,
            ReadEvolutions(entry),
            ReadLearnset(entry),
            ReadCompatibility(entry),
            new SwShPokemonProvenance(source.RelativePath, source.SourceLayer, source.FileState));
    }

    private static IReadOnlyList<SwShPokemonEvolutionRecord> ReadEvolutions(global::personal entry)
    {
        var evolutions = new List<SwShPokemonEvolutionRecord>();
        for (var index = 0; index < entry.EvolutionsLength; index++)
        {
            var evolution = entry.Evolutions(index);
            if (evolution is null || evolution.Value.Species == 0)
            {
                continue;
            }

            evolutions.Add(new SwShPokemonEvolutionRecord(
                index,
                evolution.Value.Condition,
                evolution.Value.Parameter,
                evolution.Value.Species,
                evolution.Value.Form,
                evolution.Value.Level,
                $"Method {evolution.Value.Condition}",
                "value",
                "Parameter",
                evolution.Value.Parameter.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        return evolutions;
    }

    private static IReadOnlyList<SwShPokemonLearnsetMove> ReadLearnset(global::personal entry)
    {
        var moves = new List<SwShPokemonLearnsetMove>();
        for (var index = 0; index < entry.LevelupMovesLength; index++)
        {
            var learnedMove = entry.LevelupMoves(index);
            if (learnedMove is null || learnedMove.Value.Move == 0)
            {
                continue;
            }

            moves.Add(new SwShPokemonLearnsetMove(
                index,
                learnedMove.Value.Move,
                SvLabels.Move(learnedMove.Value.Move),
                learnedMove.Value.Level));
        }

        return moves;
    }

    private static IReadOnlyList<SwShPokemonCompatibilityGroup> ReadCompatibility(global::personal entry)
    {
        return
        [
            CreateCompatibilityGroup("tm", "TM Moves", entry.GetTmMovesArray()),
            CreateCompatibilityGroup("egg", "Egg Moves", entry.GetEggMovesArray()),
            CreateCompatibilityGroup("reminder", "Reminder Moves", entry.GetReminderMovesArray()),
        ];
    }

    private static SwShPokemonCompatibilityGroup CreateCompatibilityGroup(string id, string label, IReadOnlyList<ushort> moves)
    {
        var entries = moves
            .Select((move, index) => new SwShPokemonCompatibilityEntry(
                index,
                move,
                SvLabels.Move(move),
                $"{index + 1}. {SvLabels.Move(move)}",
                CanLearn: true))
            .ToArray();

        return new SwShPokemonCompatibilityGroup(id, label, entries.Length, entries);
    }

    private static IReadOnlyList<SwShPokemonEvolutionMethodOption> CreateEvolutionOptions(IReadOnlyList<SwShPokemonRecord> pokemon)
    {
        return pokemon
            .SelectMany(record => record.Evolutions.Select(evolution => evolution.Method))
            .Append(0)
            .Distinct()
            .OrderBy(value => value)
            .Select(value => new SwShPokemonEvolutionMethodOption(
                value,
                value == 0 ? "None" : $"Method {value}",
                "value",
                "Parameter",
                Array.Empty<SwShPokemonEditableFieldOption>()))
            .ToArray();
    }

    private static IReadOnlyList<SwShPokemonEditableFieldOption> CreateMoveOptions(IReadOnlyList<SwShPokemonRecord> pokemon)
    {
        return pokemon
            .SelectMany(record => record.Learnset.Select(move => move.MoveId))
            .Concat(pokemon.SelectMany(record => record.Compatibility.SelectMany(group => group.Entries.Select(entry => entry.MoveId))))
            .Where(move => move > 0)
            .Distinct()
            .OrderBy(move => move)
            .Select(move => new SwShPokemonEditableFieldOption(move, SvLabels.Move(move)))
            .ToArray();
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
            options ?? Array.Empty<SwShPokemonEditableFieldOption>());
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
}
