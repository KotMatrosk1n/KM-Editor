// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Tests.Items;
using KM.SwSh.Tests.Pokemon;
using KM.SwSh.Trainers;
using KM.SwSh.Workflows;
using System.Buffers.Binary;
using Xunit;

namespace KM.SwSh.Tests.Trainers;

public sealed class SwShTrainersWorkflowServiceTests
{
    [Fact]
    public void LoadReadsTrainersFromRealSwordShieldFiles()
    {
        using var temp = TemporarySwShProject.Create();
        WriteTrainerFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShTrainersWorkflowService().Load(project);

        Assert.Equal(SwShWorkflowAvailability.ReadOnly, workflow.Summary.Availability);
        var trainer = Assert.Single(workflow.Trainers);
        Assert.Equal(10, trainer.TrainerId);
        Assert.Equal("Avery", trainer.Name);
        Assert.Equal(5, trainer.TrainerClassId);
        Assert.Equal("Pokemon Trainer", trainer.TrainerClass);
        Assert.Equal(1, trainer.BattleTypeValue);
        Assert.Equal("Doubles", trainer.BattleType);
        Assert.Equal([1, 2, 0, 0], trainer.ItemIds);
        Assert.Equal(["Potion", "Antidote", "None", "None"], trainer.Items);
        Assert.Equal(0x4D, trainer.AiFlags);
        Assert.Equal(13, trainer.AiFlagStates.Count);
        Assert.Contains(trainer.AiFlagStates, flag => flag.Label == "Basic" && flag.Enabled);
        Assert.Contains(trainer.AiFlagStates, flag => flag.Label == "Expert" && flag.Enabled);
        Assert.Contains(trainer.AiFlagStates, flag => flag.Label == "Double" && flag.Enabled);
        Assert.Contains(trainer.AiFlagStates, flag => flag.Label == "Fire Gym Rival" && flag.Enabled);
        Assert.Contains(trainer.AiFlagStates, flag => flag.Label == "PokeChange" && !flag.Enabled);
        Assert.True(trainer.Heal);
        Assert.Equal(24, trainer.Money);
        Assert.Equal(7, trainer.Gift);
        Assert.Equal(4, trainer.ClassBallId);
        Assert.Equal("4 Poke Ball", trainer.ClassBall);
        Assert.True(trainer.CanEditClassBall);
        Assert.Equal("Unique trainer class: Avery", trainer.ClassBallScope);
        Assert.Equal(6, trainer.Team.Count);
        Assert.Equal(810, trainer.Team[0].SpeciesId);
        Assert.Equal("Grookey", trainer.Team[0].Species);
        Assert.Equal(12, trainer.Team[0].Level);
        Assert.Equal(0, trainer.Team[0].Form);
        Assert.Equal(1, trainer.Team[0].HeldItemId);
        Assert.Equal("Potion", trainer.Team[0].HeldItem);
        Assert.Equal([1, 2, 0, 0], trainer.Team[0].MoveIds);
        Assert.Equal(["Scratch", "Growl", "None", "None"], trainer.Team[0].Moves);
        Assert.Equal(1, trainer.Team[0].Gender);
        Assert.Equal("Male", trainer.Team[0].GenderLabel);
        Assert.Equal(2, trainer.Team[0].Ability);
        Assert.Equal("Ability 2 - 066 Blaze", trainer.Team[0].AbilityLabel);
        Assert.Equal(13, trainer.Team[0].Nature);
        Assert.Equal("Jolly (+Spe/-Sp.Atk)", trainer.Team[0].NatureLabel);
        Assert.Equal(new SwShTrainerPokemonStatsRecord(10, 20, 30, 40, 50, 60), trainer.Team[0].Evs);
        Assert.Equal(7, trainer.Team[0].DynamaxLevel);
        Assert.False(trainer.Team[0].CanGigantamax);
        Assert.Equal(new SwShTrainerPokemonStatsRecord(1, 2, 3, 5, 6, 4), trainer.Team[0].Ivs);
        Assert.True(trainer.Team[0].Shiny);
        Assert.False(trainer.Team[0].CanDynamax);
        Assert.All(trainer.Team.Skip(2), pokemon => Assert.Equal(0, pokemon.SpeciesId));
        Assert.All(trainer.Team.Skip(2), pokemon => Assert.Equal("None", pokemon.Species));
        Assert.Equal(ProjectFileLayer.Base, trainer.Provenance.SourceLayer);
        Assert.Equal(ProjectFileLayer.Base, trainer.Provenance.TeamSourceLayer);
        Assert.Equal(ProjectFileLayer.Base, trainer.Provenance.ClassSourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, trainer.Provenance.FileState);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, trainer.Provenance.TeamFileState);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, trainer.Provenance.ClassFileState);
        Assert.Equal(1, workflow.Stats.TotalTrainerCount);
        Assert.Equal(2, workflow.Stats.TotalPokemonCount);
        Assert.Equal(3, workflow.Stats.SourceFileCount);
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShTrainersWorkflowService.TrainerClassIdField).Options,
            option => option.Value == 5 && option.Label == "005 Pokemon Trainer");
        var battleTypeField = workflow.EditableFields.Single(field => field.Field == SwShTrainersWorkflowService.BattleTypeField);
        Assert.Equal(1, battleTypeField.MaximumValue);
        Assert.Equal(
            [(0, "0 Singles"), (1, "1 Doubles")],
            battleTypeField.Options.Select(option => (option.Value, option.Label)));
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShTrainersWorkflowService.ClassBallIdField).Options,
            option => option.Value == 4 && option.Label == "4 Poke Ball");
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShTrainersWorkflowService.SpeciesIdField).Options,
            option => option.Value == 810 && option.Label == "810 Grookey");
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShTrainersWorkflowService.HeldItemIdField).Options,
            option => option.Value == 1 && option.Label == "001 Potion");
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShTrainersWorkflowService.TrainerItem1IdField).Options,
            option => option.Value == 2 && option.Label == "002 Antidote");
        Assert.Equal(
            "Prize money",
            workflow.EditableFields.Single(field => field.Field == SwShTrainersWorkflowService.MoneyField).Label);
        Assert.Empty(
            workflow.EditableFields.Single(field => field.Field == SwShTrainersWorkflowService.GiftField).Options);
        Assert.Equal(
            "Unknown header value",
            workflow.EditableFields.Single(field => field.Field == SwShTrainersWorkflowService.GiftField).Label);
        Assert.Equal(
            "Unknown header flag",
            workflow.EditableFields.Single(field => field.Field == SwShTrainersWorkflowService.HealField).Label);
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShTrainersWorkflowService.Move1IdField).Options,
            option => option.Value == 1 && option.Label == "001 Scratch");
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShTrainersWorkflowService.AbilityField).Options,
            option => option.Value == 2 && option.Label == "Ability 2");
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShTrainersWorkflowService.GenderField).Options,
            option => option.Value == 1 && option.Label == "Male");
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShTrainersWorkflowService.NatureField).Options,
            option => option.Value == 13 && option.Label == "Jolly (+Spe/-Sp.Atk)");
        Assert.Equal(
            [
                "Lonely (+Atk/-Def)",
                "Adamant (+Atk/-Sp.Atk)",
                "Naughty (+Atk/-Sp.Def)",
                "Brave (+Atk/-Spe)",
                "Bold (+Def/-Atk)",
            ],
            workflow.EditableFields
                .Single(field => field.Field == SwShTrainersWorkflowService.NatureField)
                .Options
                .Take(5)
                .Select(option => option.Label));
        Assert.Equal(
            "HP",
            workflow.EditableFields.Single(field => field.Field == SwShTrainersWorkflowService.EvHpField).Label);
        Assert.Equal(
            "Attack",
            workflow.EditableFields.Single(field => field.Field == SwShTrainersWorkflowService.EvAttackField).Label);
        Assert.Equal(
            "HP",
            workflow.EditableFields.Single(field => field.Field == SwShTrainersWorkflowService.IvHpField).Label);
        Assert.Equal(
            "Attack",
            workflow.EditableFields.Single(field => field.Field == SwShTrainersWorkflowService.IvAttackField).Label);
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShTrainersWorkflowService.DynamaxLevelField).Options,
            option => option.Value == 10 && option.Label == "10");
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShTrainersWorkflowService.CanDynamaxField).Options,
            option => option.Value == 0 && option.Label == "No");
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShTrainersWorkflowService.CanDynamaxField).Options,
            option => option.Value == 1 && option.Label == "Yes");
        Assert.Equal(
            SwShTrainerDataFile.KnownAiFlagsMask,
            workflow.EditableFields.Single(field => field.Field == SwShTrainersWorkflowService.AiFlagsField).MaximumValue);
        Assert.Empty(workflow.Diagnostics);
    }

    [Fact]
    public void CreateAiFlagStatesUsesVerifiedScriptSlotOrder()
    {
        var states = SwShTrainersWorkflowService.CreateAiFlagStates(SwShTrainerDataFile.KnownAiFlagsMask);

        Assert.Equal(0x1FFF, SwShTrainerDataFile.KnownAiFlagsMask);
        Assert.Equal(
            [
                (0, 1 << 0, "Basic", "Enables the Basic trainer AI script slot."),
                (1, 1 << 1, "Strong", "Enables the Strong trainer AI script slot."),
                (2, 1 << 2, "Expert", "Enables the Expert trainer AI script slot."),
                (3, 1 << 3, "Double", "Enables the Double trainer AI script slot."),
                (4, 1 << 4, "Raid", "Enables the Raid trainer AI script slot."),
                (5, 1 << 5, "Allowance", "Enables the Allowance trainer AI script slot."),
                (6, 1 << 6, "Fire Gym Rival", "Enables the Fire Gym Rival trainer AI script slot."),
                (7, 1 << 7, "Fire Gym Staff", "Enables the Fire Gym Staff trainer AI script slot."),
                (8, 1 << 8, "Fire Gym Team Yell", "Enables the Fire Gym Team Yell trainer AI script slot."),
                (9, 1 << 9, "JK3 Ookami", "Enables the JK3 Ookami trainer AI script slot."),
                (10, 1 << 10, "Item", "Enables the Item trainer AI script slot."),
                (11, 1 << 11, "Fire Gym Item", "Enables the Fire Gym Item trainer AI script slot."),
                (12, 1 << 12, "PokeChange", "Enables the PokeChange trainer AI script slot."),
            ],
            states.Select(state => (state.Bit, state.Mask, state.Label, state.Description)));
        Assert.All(states, state => Assert.True(state.Enabled));
        Assert.Equal(
            SwShTrainerDataFile.KnownAiFlagsMask,
            states.Aggregate(0, (mask, state) => mask | state.Mask));
    }

    [Fact]
    public void LoadTreatsIdenticallyNamedDistinctTrainerIdsAsSharedClassOwners()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/trainer/trainer_data/trainer_010.bin",
            CreateTrainerData(classId: 5, battleMode: 0, pokemonCount: 1));
        temp.WriteBaseRomFsFile(
            "bin/trainer/trainer_data/trainer_011.bin",
            CreateTrainerData(classId: 5, battleMode: 0, pokemonCount: 1));
        temp.WriteBaseRomFsFile(
            "bin/trainer/trainer_poke/trainer_010.bin",
            CreateTrainerTeam((speciesId: 810, level: 12, heldItemId: 0, moves: [1, 0, 0, 0])));
        temp.WriteBaseRomFsFile(
            "bin/trainer/trainer_poke/trainer_011.bin",
            CreateTrainerTeam((speciesId: 821, level: 12, heldItemId: 0, moves: [1, 0, 0, 0])));
        temp.WriteBaseRomFsFile("bin/trainer/trainer_type/trainer_type_005.bin", CreateTrainerClass(ballId: 4));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/trname.dat",
            CreateTextTable(11, (10, "Gym Trainer"), (11, "Gym Trainer")));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/trtype.dat",
            CreateTextTable(5, (5, "Pokemon Trainer")));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShTrainersWorkflowService().Load(project);

        Assert.Equal([10, 11], workflow.Trainers.Select(trainer => trainer.TrainerId));
        Assert.All(workflow.Trainers, trainer => Assert.Equal("Gym Trainer", trainer.Name));
        Assert.All(workflow.Trainers, trainer => Assert.False(trainer.CanEditClassBall));
        Assert.All(workflow.Trainers, trainer => Assert.Equal("Shared trainer class", trainer.ClassBallScope));
        Assert.Equal(5, workflow.Stats.SourceFileCount);
    }

    [Fact]
    public void LoadReturnsDiagnosticWhenTrainerFilesAreMissing()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/trainers.bin", "placeholder");
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShTrainersWorkflowService().Load(project);

        Assert.Empty(workflow.Trainers);
        Assert.Contains(workflow.Diagnostics, diagnostic => diagnostic.Domain == "workflow.trainers");
    }

    [Fact]
    public void LoadOmitsTrainerFilesWithoutTrailingNumericIds()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/trainer/trainer_data/trainer_data_copy.bin",
            CreateTrainerData(classId: 5, battleMode: 0, pokemonCount: 1));
        temp.WriteBaseRomFsFile(
            "bin/trainer/trainer_poke/trainer_poke_copy.bin",
            CreateTrainerTeam((speciesId: 810, level: 12, heldItemId: 0, moves: [1, 0, 0, 0])));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShTrainersWorkflowService().Load(project);

        Assert.Empty(workflow.Trainers);
        Assert.Equal(
            2,
            workflow.Diagnostics.Count(diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Warning
                && diagnostic.Message.Contains("filename does not end with a numeric ID", StringComparison.Ordinal)));
    }

    [Fact]
    public void LoadWarnsWhenTrainerPokemonCountDoesNotMatchPartyRows()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("bin/trainer/trainer_data/trainer_010.bin", CreateTrainerData(classId: 5, battleMode: 0, pokemonCount: 2));
        temp.WriteBaseRomFsFile(
            "bin/trainer/trainer_poke/trainer_010.bin",
            CreateTrainerTeam((speciesId: 810, level: 12, heldItemId: 0, moves: new[] { 1, 0, 0, 0 })));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShTrainersWorkflowService().Load(project);

        Assert.Single(workflow.Trainers);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning
                && diagnostic.Domain == "workflow.trainers"
                && diagnostic.Message.Contains("declares 2 Pokemon", StringComparison.Ordinal));
    }

    [Fact]
    public void LoadKeepsSpeciesOptionsForTrainerPokemonWhoseHatchedSpeciesPointsAtBase()
    {
        using var temp = TemporarySwShProject.Create();
        WriteTrainerFixture(temp);
        temp.WriteBaseRomFsFile(
            "bin/trainer/trainer_poke/trainer_010.bin",
            CreateTrainerTeam((speciesId: 745, level: 75, heldItemId: 0, moves: new[] { 709, 446, 444, 583 })));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/monsname.dat",
            CreateTextTable(745, (744, "Rockruff"), (745, "Lycanroc")));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/wazaname.dat",
            CreateTextTable(709, (444, "Stone Edge"), (446, "Stealth Rock"), (583, "Play Rough"), (709, "Accelerock")));
        temp.WriteBaseRomFsFile(
            "bin/pml/personal/personal_total.bin",
            CreatePersonalTable(
                746,
                (744, hatchedSpecies: 744),
                (745, hatchedSpecies: 744)));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShTrainersWorkflowService().Load(project);

        var speciesField = workflow.EditableFields.Single(field =>
            field.Field == SwShTrainersWorkflowService.SpeciesIdField);
        Assert.Contains(speciesField.Options, option => option.Value == 745 && option.Label == "745 Lycanroc");
        var pokemon = Assert.Single(workflow.Trainers).Team.Single(pokemon => pokemon.SpeciesId > 0);
        Assert.Equal("Lycanroc", pokemon.Species);
        Assert.Equal(new SwShTrainerPokemonStatsRecord(45, 49, 49, 65, 65, 45), pokemon.BaseStats);
    }

    internal static void WriteTrainerFixture(TemporarySwShProject temp)
    {
        temp.WriteBaseRomFsFile(
            "bin/trainer/trainer_data/trainer_010.bin",
            CreateTrainerData(
                classId: 5,
                battleMode: 1,
                pokemonCount: 2,
                items: [1, 2, 0, 0],
                aiFlags: 0x4D,
                heal: true,
                money: 24,
                gift: 7));
        temp.WriteBaseRomFsFile("bin/trainer/trainer_type/trainer_type_005.bin", CreateTrainerClass(ballId: 4));
        temp.WriteBaseRomFsFile(
            "bin/trainer/trainer_poke/trainer_010.bin",
            CreateTrainerTeam(
                (speciesId: 810, level: 12, heldItemId: 1, moves: new[] { 1, 2, 0, 0 }),
                (speciesId: 821, level: 11, heldItemId: 0, moves: new[] { 3, 0, 0, 0 })));
        temp.WriteBaseRomFsFile("bin/message/English/common/trname.dat", CreateTextTable(10, (10, "Avery")));
        temp.WriteBaseRomFsFile("bin/message/English/common/trtype.dat", CreateTextTable(5, (5, "Pokemon Trainer")));
        temp.WriteBaseRomFsFile("bin/message/English/common/monsname.dat", CreateTextTable(821, (810, "Grookey"), (821, "Rookidee")));
        temp.WriteBaseRomFsFile("bin/message/English/common/itemname.dat", CreateTextTable(2, (1, "Potion"), (2, "Antidote")));
        temp.WriteBaseRomFsFile("bin/message/English/common/wazaname.dat", CreateTextTable(3, (1, "Scratch"), (2, "Growl"), (3, "Peck")));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/tokusei.dat",
            CreateTextTable(66, (34, "Chlorophyll"), (65, "Overgrow"), (66, "Blaze")));
        temp.WriteBaseRomFsFile(
            "bin/pml/personal/personal_total.bin",
            CreatePersonalTable(
                822,
                (810, hatchedSpecies: 810),
                (821, hatchedSpecies: 821)));
    }

    internal static byte[] CreatePersonalTable(
        int recordCount,
        params (int personalId, int hatchedSpecies)[] presentRecords)
    {
        var records = Enumerable.Range(0, recordCount)
            .Select(_ => SwShPokemonWorkflowServiceTests.CreateEmptyPersonalRecord())
            .ToArray();

        foreach (var record in presentRecords)
        {
            records[record.personalId] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(
                hatchedSpecies: record.hatchedSpecies);
            BinaryPrimitives.WriteUInt16LittleEndian(records[record.personalId].AsSpan(0x1A), 66);
        }

        return SwShPokemonWorkflowServiceTests.CreatePersonalTable(records);
    }

    internal static byte[] CreateTrainerData(
        int classId,
        int battleMode,
        int pokemonCount,
        int[]? items = null,
        int aiFlags = 0,
        bool heal = false,
        int money = 0,
        int gift = 0)
    {
        var data = new byte[SwShTrainerDataFile.Size];
        var itemIds = items ?? [0, 0, 0, 0];
        WriteUInt16(data, 0x00, classId);
        data[0x02] = checked((byte)battleMode);
        data[0x03] = checked((byte)pokemonCount);
        WriteUInt16(data, 0x04, itemIds[0]);
        WriteUInt16(data, 0x06, itemIds[1]);
        WriteUInt16(data, 0x08, itemIds[2]);
        WriteUInt16(data, 0x0A, itemIds[3]);
        WriteUInt32(data, 0x0C, checked((uint)aiFlags));
        data[0x10] = heal ? (byte)1 : (byte)0;
        data[0x11] = checked((byte)money);
        WriteUInt16(data, 0x12, gift);

        return data;
    }

    internal static byte[] CreateTrainerTeam(params (int speciesId, int level, int heldItemId, int[] moves)[] pokemon)
    {
        var data = new byte[pokemon.Length * SwShTrainerTeamFile.RowSize];

        for (var index = 0; index < pokemon.Length; index++)
        {
            var rowOffset = index * SwShTrainerTeamFile.RowSize;
            var record = pokemon[index];
            if (index == 0)
            {
                data[rowOffset] = 0x21;
                data[rowOffset + 0x01] = 13;
                data[rowOffset + 0x02] = 10;
                data[rowOffset + 0x03] = 20;
                data[rowOffset + 0x04] = 30;
                data[rowOffset + 0x05] = 40;
                data[rowOffset + 0x06] = 50;
                data[rowOffset + 0x07] = 60;
                data[rowOffset + 0x08] = 7;
                data[rowOffset + 0x09] = 0;
                WriteUInt32(data, rowOffset + 0x1C, PackIvs(1, 2, 3, 4, 5, 6, shiny: true, canDynamax: false));
            }

            WriteUInt16(data, rowOffset + 0x0A, record.level);
            WriteUInt16(data, rowOffset + 0x0C, record.speciesId);
            WriteUInt16(data, rowOffset + 0x10, record.heldItemId);
            WriteUInt16(data, rowOffset + 0x12, record.moves[0]);
            WriteUInt16(data, rowOffset + 0x14, record.moves[1]);
            WriteUInt16(data, rowOffset + 0x16, record.moves[2]);
            WriteUInt16(data, rowOffset + 0x18, record.moves[3]);
        }

        return data;
    }

    internal static byte[] CreateTrainerClass(int ballId)
    {
        var data = new byte[SwShTrainerClassFile.Size];
        data[0x01] = 8;
        data[0x02] = checked((byte)ballId);

        return data;
    }

    internal static byte[] CreateTextTable(int highestIndex, params (int index, string value)[] entries)
    {
        var lines = Enumerable.Range(0, highestIndex + 1)
            .Select(index => new SwShGameTextLine($"Value {index}", Flags: 0))
            .ToArray();

        foreach (var entry in entries)
        {
            lines[entry.index] = new SwShGameTextLine(entry.value, Flags: 0);
        }

        return SwShGameTextFile.Write(lines);
    }

    private static void WriteUInt16(byte[] data, int offset, int value)
    {
        data[offset] = checked((byte)(value & 0xFF));
        data[offset + 1] = checked((byte)(value >> 8));
    }

    private static void WriteUInt32(byte[] data, int offset, uint value)
    {
        data[offset] = checked((byte)(value & 0xFF));
        data[offset + 1] = checked((byte)((value >> 8) & 0xFF));
        data[offset + 2] = checked((byte)((value >> 16) & 0xFF));
        data[offset + 3] = checked((byte)(value >> 24));
    }

    private static uint PackIvs(
        int hp,
        int attack,
        int defense,
        int speed,
        int specialAttack,
        int specialDefense,
        bool shiny,
        bool canDynamax)
    {
        return (uint)hp
            | ((uint)attack << 5)
            | ((uint)defense << 10)
            | ((uint)speed << 15)
            | ((uint)specialAttack << 20)
            | ((uint)specialDefense << 25)
            | (shiny ? 1u << 30 : 0)
            | (canDynamax ? 1u << 31 : 0);
    }
}
