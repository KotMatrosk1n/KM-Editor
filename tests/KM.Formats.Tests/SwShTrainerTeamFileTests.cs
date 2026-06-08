// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using System.Buffers.Binary;
using Xunit;

namespace KM.Formats.Tests;

public sealed class SwShTrainerTeamFileTests
{
    [Fact]
    public void ParseReadsStatsAndPackedFlags()
    {
        var data = CreateTrainerTeamRow();

        var file = SwShTrainerTeamFile.Parse(data);

        var pokemon = Assert.Single(file.Records);
        Assert.Equal(1, pokemon.Gender);
        Assert.Equal(2, pokemon.Ability);
        Assert.Equal(13, pokemon.Nature);
        Assert.Equal(2, pokemon.Form);
        Assert.Equal(new SwShTrainerPokemonStats(10, 20, 30, 40, 50, 60), pokemon.Evs);
        Assert.Equal(7, pokemon.DynamaxLevel);
        Assert.True(pokemon.CanGigantamax);
        Assert.Equal(new SwShTrainerPokemonStats(1, 2, 3, 5, 6, 4), pokemon.Ivs);
        Assert.True(pokemon.Shiny);
        Assert.False(pokemon.CanDynamax);
    }

    [Fact]
    public void WriteEditsPatchesStatsAndPackedFlags()
    {
        var file = SwShTrainerTeamFile.Parse(CreateTrainerTeamRow());

        var output = file.WriteEdits(
        [
            new SwShTrainerPokemonEdit(1, SwShTrainerPokemonField.Gender, 3),
            new SwShTrainerPokemonEdit(1, SwShTrainerPokemonField.Ability, 1),
            new SwShTrainerPokemonEdit(1, SwShTrainerPokemonField.Nature, 24),
            new SwShTrainerPokemonEdit(1, SwShTrainerPokemonField.EvSpecialDefense, 200),
            new SwShTrainerPokemonEdit(1, SwShTrainerPokemonField.DynamaxLevel, 10),
            new SwShTrainerPokemonEdit(1, SwShTrainerPokemonField.CanGigantamax, 0),
            new SwShTrainerPokemonEdit(1, SwShTrainerPokemonField.IvAttack, 31),
            new SwShTrainerPokemonEdit(1, SwShTrainerPokemonField.IvSpecialAttack, 30),
            new SwShTrainerPokemonEdit(1, SwShTrainerPokemonField.IvSpeed, 29),
            new SwShTrainerPokemonEdit(1, SwShTrainerPokemonField.Shiny, 0),
            new SwShTrainerPokemonEdit(1, SwShTrainerPokemonField.CanDynamax, 1),
        ]);

        var pokemon = Assert.Single(SwShTrainerTeamFile.Parse(output).Records);
        Assert.Equal(3, pokemon.Gender);
        Assert.Equal(1, pokemon.Ability);
        Assert.Equal(24, pokemon.Nature);
        Assert.Equal(new SwShTrainerPokemonStats(10, 20, 30, 40, 200, 60), pokemon.Evs);
        Assert.Equal(10, pokemon.DynamaxLevel);
        Assert.False(pokemon.CanGigantamax);
        Assert.Equal(new SwShTrainerPokemonStats(1, 31, 3, 30, 6, 29), pokemon.Ivs);
        Assert.False(pokemon.Shiny);
        Assert.True(pokemon.CanDynamax);
    }

    private static byte[] CreateTrainerTeamRow()
    {
        var data = new byte[SwShTrainerTeamFile.RowSize];
        data[0x00] = 0x21;
        data[0x01] = 13;
        data[0x02] = 10;
        data[0x03] = 20;
        data[0x04] = 30;
        data[0x05] = 40;
        data[0x06] = 50;
        data[0x07] = 60;
        data[0x08] = 7;
        data[0x09] = 1;
        WriteUInt16(data, 0x0A, 12);
        WriteUInt16(data, 0x0C, 810);
        WriteUInt16(data, 0x0E, 2);
        WriteUInt16(data, 0x10, 1);
        WriteUInt16(data, 0x12, 1);
        WriteUInt16(data, 0x14, 2);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x1C), PackIvs(1, 2, 3, 4, 5, 6, shiny: true, canDynamax: false));

        return data;
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

    private static void WriteUInt16(byte[] data, int offset, int value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), checked((ushort)value));
    }
}
