// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Pokemon;
using KM.SwSh.Workflows;
using System.Buffers.Binary;
using Xunit;

namespace KM.SwSh.Tests.Pokemon;

public sealed class SwShPokemonWorkflowServiceTests
{
    [Fact]
    public void LoadReadsPokemonDataFromRealTables()
    {
        using var temp = TemporaryPokemonProject.Create();
        WriteBasePokemonData(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShPokemonWorkflowService().Load(project);

        Assert.Equal(SwShWorkflowAvailability.ReadOnly, workflow.Summary.Availability);
        Assert.Equal(2, workflow.Pokemon.Count);
        Assert.Equal(1, workflow.Stats.PresentPokemonCount);
        Assert.Equal(1, workflow.Stats.TotalEvolutionCount);
        Assert.Equal(2, workflow.Stats.TotalLearnsetMoveCount);
        Assert.Equal(8, workflow.Stats.SourceFileCount);
        Assert.Contains(
            workflow.EvolutionMethodOptions,
            option => option.Value == 4
                && option.Label == "004 Level Up"
                && option.ArgumentKind == "level");
        Assert.Contains(
            workflow.EvolutionMethodOptions.Single(option => option.Value == 8).ArgumentOptions,
            option => option.Value == 1 && option.Label == "001 Potion");
        Assert.Contains(
            workflow.EvolutionMethodOptions.Single(option => option.Value == 21).ArgumentOptions,
            option => option.Value == 45 && option.Label == "045 Growl");
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShPokemonWorkflowService.HeldItem1Field).Options,
            option => option.Value == 1 && option.Label == "001 Potion");
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShPokemonWorkflowService.Ability1Field).Options,
            option => option.Value == 65 && option.Label == "065 Overgrow");
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShPokemonWorkflowService.HatchedSpeciesField).Options,
            option => option.Value == 2 && option.Label == "002 Ivysaur");
        var pokemon = workflow.Pokemon[1];
        Assert.Equal(1, pokemon.PersonalId);
        Assert.Equal(1, pokemon.SpeciesId);
        Assert.Equal("Bulbasaur", pokemon.Name);
        Assert.Equal("Base", pokemon.FormLabel);
        Assert.Equal("Grass", pokemon.Type1);
        Assert.Equal("Poison", pokemon.Type2);
        Assert.Equal(318, pokemon.BaseStats.Total);
        Assert.Equal(65, pokemon.Abilities.Ability1);
        Assert.Equal("065 Overgrow", pokemon.Abilities.Ability1Label);
        Assert.Equal(0, pokemon.Abilities.Ability2);
        Assert.Equal("000 None", pokemon.Abilities.Ability2Label);
        Assert.Equal(34, pokemon.Abilities.HiddenAbility);
        Assert.Equal("034 Chlorophyll", pokemon.Abilities.HiddenAbilityLabel);
        Assert.Equal(31, pokemon.GenderRatio);
        Assert.Equal("031 Male 87.5% / Female 12.5%", pokemon.GenderRatioLabel);
        Assert.True(pokemon.DexPresence.IsPresentInGame);
        Assert.Equal(1, pokemon.DexPresence.RegionalDexIndex);
        Assert.Equal(ProjectFileLayer.Base, pokemon.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, pokemon.Provenance.FileState);
        Assert.Equal(SwShPokemonWorkflowService.PersonalDataPath, pokemon.Provenance.SourceFile);
        var tmGroup = pokemon.Compatibility.Single(group => group.GroupId == SwShPokemonWorkflowService.TechnicalMachineCompatibilityGroupId);
        Assert.Equal(1, tmGroup.EnabledCount);
        var tm10 = tmGroup.Entries.Single(entry => entry.Slot == 10);
        Assert.Equal("TM10 Magical Leaf", tm10.Label);
        Assert.True(tm10.CanLearn);
        var typeTutorGroup = pokemon.Compatibility.Single(group => group.GroupId == SwShPokemonWorkflowService.TypeTutorCompatibilityGroupId);
        Assert.True(typeTutorGroup.Entries[0].CanLearn);
        var evolution = Assert.Single(pokemon.Evolutions);
        Assert.Equal(0, evolution.Slot);
        Assert.Equal(4, evolution.Method);
        Assert.Equal(2, evolution.Species);
        Assert.Equal(16, evolution.Level);
        Assert.Equal("Level Up", evolution.MethodName);
        Assert.Equal("level", evolution.ArgumentKind);
        Assert.Equal("Level", evolution.ArgumentLabel);
        Assert.Equal("None", evolution.ArgumentValue);
        Assert.Collection(
            pokemon.Learnset,
            move =>
            {
                Assert.Equal(0, move.Slot);
                Assert.Equal(33, move.MoveId);
                Assert.Equal("Tackle", move.MoveName);
                Assert.Equal(1, move.Level);
            },
            move =>
            {
                Assert.Equal(1, move.Slot);
                Assert.Equal(45, move.MoveId);
                Assert.Equal("Growl", move.MoveName);
                Assert.Equal(3, move.Level);
            });
        Assert.Empty(workflow.Diagnostics);
    }

    [Fact]
    public void LoadPrefersLayeredPersonalDataWhenOutputOverridesBase()
    {
        using var temp = TemporaryPokemonProject.Create();
        WriteBasePokemonData(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        temp.WriteOutputFile(
            SwShPokemonWorkflowService.PersonalDataPath,
            CreatePersonalTable(
                CreateEmptyPersonalRecord(),
                CreateBulbasaurPersonalRecord(hp: 99)));
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = new SwShPokemonWorkflowService().Load(project);

        var pokemon = workflow.Pokemon[1];
        Assert.Equal(99, pokemon.BaseStats.HP);
        Assert.Equal(ProjectFileLayer.Layered, pokemon.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.LayeredOverride, pokemon.Provenance.FileState);
        Assert.Empty(workflow.Diagnostics);
    }

    [Fact]
    public void LoadUsesPersonalRowForDisplaySpeciesWhenHatchedSpeciesPointsAtBase()
    {
        using var temp = TemporaryPokemonProject.Create();
        WriteBasePokemonData(temp);
        temp.WriteBaseRomFsFile(
            "bin/pml/personal/personal_total.bin",
            CreatePersonalTable(
                CreateEmptyPersonalRecord(),
                CreateBulbasaurPersonalRecord(),
                CreateBulbasaurPersonalRecord(hp: 60, hatchedSpecies: 1)));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShPokemonWorkflowService().Load(project);

        var evolvedRow = workflow.Pokemon[2];
        Assert.Equal(2, evolvedRow.PersonalId);
        Assert.Equal(2, evolvedRow.SpeciesId);
        Assert.Equal("Ivysaur", evolvedRow.Name);
        Assert.Equal(1, evolvedRow.Personal.HatchedSpecies);
    }

    [Fact]
    public void LoadReportsDiagnosticWhenPersonalDataIsMissing()
    {
        using var temp = TemporaryPokemonProject.Create();
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShPokemonWorkflowService().Load(project);

        Assert.Empty(workflow.Pokemon);
        Assert.Contains(workflow.Diagnostics, diagnostic => diagnostic.Domain == "workflow.pokemon");
    }

    internal static void WriteBasePokemonData(TemporaryPokemonProject temp)
    {
        temp.WriteBaseRomFsFile(
            "bin/pml/personal/personal_total.bin",
            CreatePersonalTable(CreateEmptyPersonalRecord(), CreateBulbasaurPersonalRecord()));
        temp.WriteBaseRomFsFile(
            "bin/pml/waza_oboe/wazaoboe_total.bin",
            CreateLearnsetTable([], [(33, 1), (45, 3)]));
        temp.WriteBaseRomFsFile(
            "bin/pml/evolution/evo_001.bin",
            CreateEvolutionFile((4, 0, 2, 0, 16)));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/pokelist.dat",
            CreateTextTable("None", "Which Pokemon do you want to swap with?"));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/monsname.dat",
            CreateIndexedPokemonNames());
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname.dat",
            CreateIndexedItemNames());
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/tokusei.dat",
            CreateIndexedAbilityNames());
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/wazaname.dat",
            CreateIndexedMoveNames());
    }

    internal static byte[] CreatePersonalTable(params byte[][] records)
    {
        var data = new byte[records.Length * SwShPersonalTable.RecordSize];
        for (var index = 0; index < records.Length; index++)
        {
            records[index].CopyTo(data.AsSpan(index * SwShPersonalTable.RecordSize));
        }

        return data;
    }

    internal static byte[] CreateEmptyPersonalRecord()
    {
        return new byte[SwShPersonalTable.RecordSize];
    }

    internal static byte[] CreateBulbasaurPersonalRecord(int hp = 45, int hatchedSpecies = 1)
    {
        var record = new byte[SwShPersonalTable.RecordSize];
        record[0x00] = checked((byte)hp);
        record[0x01] = 49;
        record[0x02] = 49;
        record[0x03] = 45;
        record[0x04] = 65;
        record[0x05] = 65;
        record[0x06] = 11;
        record[0x07] = 3;
        record[0x08] = 45;
        record[0x09] = 1;
        record[0x12] = 31;
        record[0x13] = 20;
        record[0x14] = 70;
        record[0x15] = 4;
        record[0x16] = 7;
        record[0x17] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x18), 65);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x1A), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x1C), 34);
        record[0x20] = 1;
        record[0x21] = 12 | (1 << 6);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x22), 64);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x24), 7);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x26), 69);
        SetFlag(record, 0x28, 10);
        SetFlag(record, 0x38, 0);
        SetFlag(record, 0xA8, 1);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x56), checked((ushort)hatchedSpecies));
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x5C), 1);

        return record;
    }

    internal static void SetFlag(byte[] data, int offset, int bitIndex)
    {
        data[offset + (bitIndex / 8)] |= (byte)(1 << (bitIndex % 8));
    }

    internal static byte[] CreateLearnsetTable(params (ushort MoveId, ushort Level)[][] learnsets)
    {
        var data = new byte[learnsets.Length * SwShPokemonLearnsetTable.RecordSize];
        for (var recordIndex = 0; recordIndex < learnsets.Length; recordIndex++)
        {
            var recordOffset = recordIndex * SwShPokemonLearnsetTable.RecordSize;
            var moves = learnsets[recordIndex];
            for (var moveIndex = 0; moveIndex < moves.Length; moveIndex++)
            {
                var moveOffset = recordOffset + (moveIndex * 4);
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(moveOffset), moves[moveIndex].MoveId);
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(moveOffset + 2), moves[moveIndex].Level);
            }

            var sentinelOffset = recordOffset + (moves.Length * 4);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(sentinelOffset), ushort.MaxValue);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(sentinelOffset + 2), ushort.MaxValue);
        }

        return data;
    }

    internal static byte[] CreateEvolutionFile(params (ushort Method, ushort Argument, ushort Species, byte Form, byte Level)[] evolutions)
    {
        var data = new byte[SwShEvolutionSet.FileSize];
        for (var index = 0; index < evolutions.Length; index++)
        {
            var evolution = evolutions[index];
            var offset = index * SwShEvolutionSet.RecordSize;
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), evolution.Method);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset + 2), evolution.Argument);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset + 4), evolution.Species);
            data[offset + 6] = evolution.Form;
            data[offset + 7] = evolution.Level;
        }

        return data;
    }

    private static byte[] CreateIndexedMoveNames()
    {
        var names = Enumerable.Range(0, 521)
            .Select(index => $"Move {index}")
            .ToArray();
        names[5] = "Mega Punch";
        names[14] = "Swords Dance";
        names[33] = "Tackle";
        names[45] = "Growl";
        names[345] = "Magical Leaf";
        names[520] = "Grass Pledge";

        return CreateTextTable(names);
    }

    private static byte[] CreateIndexedPokemonNames()
    {
        var names = Enumerable.Range(0, 4)
            .Select(index => $"Pokemon {index}")
            .ToArray();
        names[0] = "None";
        names[1] = "Bulbasaur";
        names[2] = "Ivysaur";
        names[3] = "Venusaur";

        return CreateTextTable(names);
    }

    private static byte[] CreateIndexedItemNames()
    {
        var names = Enumerable.Range(0, 2)
            .Select(index => $"Item {index}")
            .ToArray();
        names[0] = "None";
        names[1] = "Potion";

        return CreateTextTable(names);
    }

    private static byte[] CreateIndexedAbilityNames()
    {
        var names = Enumerable.Range(0, 66)
            .Select(index => $"Ability {index}")
            .ToArray();
        names[0] = "None";
        names[34] = "Chlorophyll";
        names[65] = "Overgrow";

        return CreateTextTable(names);
    }

    private static byte[] CreateTextTable(params string[] lines)
    {
        return SwShGameTextFile.Write(
            lines.Select(line => new SwShGameTextLine(line, Flags: 0)).ToArray());
    }
}
