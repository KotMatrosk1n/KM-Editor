// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Tests.Items;
using KM.SwSh.Trainers;
using KM.SwSh.Workflows;
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
        Assert.Equal(2, trainer.Team.Count);
        Assert.Equal(810, trainer.Team[0].SpeciesId);
        Assert.Equal("Grookey", trainer.Team[0].Species);
        Assert.Equal(12, trainer.Team[0].Level);
        Assert.Equal(1, trainer.Team[0].HeldItemId);
        Assert.Equal("Potion", trainer.Team[0].HeldItem);
        Assert.Equal([1, 2, 0, 0], trainer.Team[0].MoveIds);
        Assert.Equal(["Scratch", "Growl", "None", "None"], trainer.Team[0].Moves);
        Assert.Equal(ProjectFileLayer.Base, trainer.Provenance.SourceLayer);
        Assert.Equal(ProjectFileLayer.Base, trainer.Provenance.TeamSourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, trainer.Provenance.FileState);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, trainer.Provenance.TeamFileState);
        Assert.Equal(1, workflow.Stats.TotalTrainerCount);
        Assert.Equal(2, workflow.Stats.TotalPokemonCount);
        Assert.Equal(2, workflow.Stats.SourceFileCount);
        Assert.Empty(workflow.Diagnostics);
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

    internal static void WriteTrainerFixture(TemporarySwShProject temp)
    {
        temp.WriteBaseRomFsFile("bin/trainer/trainer_data/trainer_010.bin", CreateTrainerData(classId: 5, battleMode: 1, pokemonCount: 2));
        temp.WriteBaseRomFsFile(
            "bin/trainer/trainer_poke/trainer_010.bin",
            CreateTrainerTeam(
                (speciesId: 810, level: 12, heldItemId: 1, moves: new[] { 1, 2, 0, 0 }),
                (speciesId: 821, level: 11, heldItemId: 0, moves: new[] { 3, 0, 0, 0 })));
        temp.WriteBaseRomFsFile("bin/message/English/common/trname.dat", CreateTextTable(10, (10, "Avery")));
        temp.WriteBaseRomFsFile("bin/message/English/common/trtype.dat", CreateTextTable(5, (5, "Pokemon Trainer")));
        temp.WriteBaseRomFsFile("bin/message/English/common/monsname.dat", CreateTextTable(821, (810, "Grookey"), (821, "Rookidee")));
        temp.WriteBaseRomFsFile("bin/message/English/common/itemname.dat", CreateTextTable(1, (1, "Potion")));
        temp.WriteBaseRomFsFile("bin/message/English/common/wazaname.dat", CreateTextTable(3, (1, "Scratch"), (2, "Growl"), (3, "Peck")));
    }

    internal static byte[] CreateTrainerData(int classId, int battleMode, int pokemonCount)
    {
        var data = new byte[SwShTrainerDataFile.Size];
        WriteUInt16(data, 0x00, classId);
        data[0x02] = checked((byte)battleMode);
        data[0x03] = checked((byte)pokemonCount);

        return data;
    }

    internal static byte[] CreateTrainerTeam(params (int speciesId, int level, int heldItemId, int[] moves)[] pokemon)
    {
        var data = new byte[pokemon.Length * SwShTrainerTeamFile.RowSize];

        for (var index = 0; index < pokemon.Length; index++)
        {
            var rowOffset = index * SwShTrainerTeamFile.RowSize;
            var record = pokemon[index];
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
}
