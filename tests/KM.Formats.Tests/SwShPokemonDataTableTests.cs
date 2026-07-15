// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using System.Buffers.Binary;
using Xunit;

namespace KM.Formats.Tests;

public sealed class SwShPokemonDataTableTests
{
    [Fact]
    public void PersonalTableParsesRecordFields()
    {
        var record = new byte[SwShPersonalTable.RecordSize];
        record[0x00] = 45;
        record[0x01] = 49;
        record[0x02] = 50;
        record[0x03] = 65;
        record[0x04] = 66;
        record[0x05] = 67;
        record[0x06] = 11;
        record[0x07] = 3;
        record[0x08] = 45;
        record[0x09] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(
            record.AsSpan(0x0A),
            unchecked((ushort)(1 | (2 << 2) | (3 << 4) | (1 << 8) | (2 << 10))));
        BinaryPrimitives.WriteInt16LittleEndian(record.AsSpan(0x0C), 10);
        BinaryPrimitives.WriteInt16LittleEndian(record.AsSpan(0x0E), 20);
        BinaryPrimitives.WriteInt16LittleEndian(record.AsSpan(0x10), 30);
        record[0x12] = 31;
        record[0x13] = 20;
        record[0x14] = 70;
        record[0x15] = 4;
        record[0x16] = 7;
        record[0x17] = 8;
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x18), 65);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x1A), 66);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x1C), 34);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x1E), 100);
        record[0x20] = 2;
        record[0x21] = 12 | (1 << 6) | (1 << 7);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x22), 64);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x24), 7);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x26), 69);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(0x4C), 0x11223344);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x56), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x58), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x5A), 5);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x5C), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x5E), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0xAC), 200);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0xAE), 300);

        var parsed = SwShPersonalTable.Parse(record);

        var personal = Assert.Single(parsed.Records);
        Assert.Equal(0, personal.PersonalId);
        Assert.Equal(45, personal.HP);
        Assert.Equal(49, personal.Attack);
        Assert.Equal(50, personal.Defense);
        Assert.Equal(65, personal.Speed);
        Assert.Equal(66, personal.SpecialAttack);
        Assert.Equal(67, personal.SpecialDefense);
        Assert.Equal(342, personal.BaseStatTotal);
        Assert.Equal(11, personal.Type1);
        Assert.Equal(3, personal.Type2);
        Assert.Equal(1, personal.EVYieldHP);
        Assert.Equal(2, personal.EVYieldAttack);
        Assert.Equal(3, personal.EVYieldDefense);
        Assert.Equal(0, personal.EVYieldSpeed);
        Assert.Equal(1, personal.EVYieldSpecialAttack);
        Assert.Equal(2, personal.EVYieldSpecialDefense);
        Assert.True(personal.IsPresentInGame);
        Assert.True(personal.HasSpriteForm);
        Assert.Equal(12, personal.Color);
        Assert.Equal(65, personal.Ability1);
        Assert.Equal(66, personal.Ability2);
        Assert.Equal(34, personal.HiddenAbility);
        Assert.Equal(1, personal.HatchedSpecies);
        Assert.True(personal.IsRegionalForm);
        Assert.True(personal.CanNotDynamax);
        Assert.Equal(200, personal.ArmorDexIndex);
        Assert.Equal(300, personal.CrownDexIndex);
    }

    [Fact]
    public void PersonalTableWritesEditedFieldsAndPreservesUnknownBytes()
    {
        var record = Enumerable.Repeat((byte)0xCC, SwShPersonalTable.RecordSize).ToArray();
        record[0x00] = 45;
        record[0x01] = 49;
        record[0x02] = 50;
        record[0x03] = 65;
        record[0x04] = 66;
        record[0x05] = 67;
        record[0x06] = 11;
        record[0x07] = 3;
        record[0x08] = 45;
        record[0x09] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x0A), 0xD000);
        BinaryPrimitives.WriteInt16LittleEndian(record.AsSpan(0x0C), 10);
        BinaryPrimitives.WriteInt16LittleEndian(record.AsSpan(0x0E), 20);
        BinaryPrimitives.WriteInt16LittleEndian(record.AsSpan(0x10), 30);
        record[0x12] = 31;
        record[0x13] = 20;
        record[0x14] = 70;
        record[0x15] = 4;
        record[0x16] = 7;
        record[0x17] = 8;
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x18), 65);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x1A), 66);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x1C), 34);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x1E), 100);
        record[0x20] = 2;
        record[0x21] = 12 | (1 << 6);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x22), 64);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x24), 7);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x26), 69);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(0x4C), 0x11223344);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x56), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x58), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x5A), 0x00F0);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x5C), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x5E), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0xAC), 200);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0xAE), 300);
        var parsed = SwShPersonalTable.Parse(record);
        var edited = parsed.Records[0] with
        {
            HP = 99,
            EVYieldAttack = 3,
            IsPresentInGame = false,
            HasSpriteForm = true,
            IsRegionalForm = true,
            CanNotDynamax = true,
            CrownDexIndex = 401,
        };

        var written = SwShPersonalTable.Write([edited], record);
        var reparsed = SwShPersonalTable.Parse(written).Records[0];

        Assert.Equal(99, reparsed.HP);
        Assert.Equal(3, reparsed.EVYieldAttack);
        Assert.False(reparsed.IsPresentInGame);
        Assert.True(reparsed.HasSpriteForm);
        Assert.True(reparsed.IsRegionalForm);
        Assert.True(reparsed.CanNotDynamax);
        Assert.Equal(401, reparsed.CrownDexIndex);
        Assert.Equal(0xCC, written[0x60]);
        Assert.Equal(0xD00C, BinaryPrimitives.ReadUInt16LittleEndian(written.AsSpan(0x0A)));
        Assert.Equal(0xF0 | 0x1 | 0x4, BinaryPrimitives.ReadUInt16LittleEndian(written.AsSpan(0x5A)));
    }

    [Fact]
    public void PersonalTableWholeTableWriteLeavesUntouchedRecordsByteExact()
    {
        var source = Enumerable.Range(0, SwShPersonalTable.RecordSize * 2)
            .Select(index => unchecked((byte)((index * 37) + 11)))
            .ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(0x0A), 0xB321);
        BinaryPrimitives.WriteUInt16LittleEndian(
            source.AsSpan(SwShPersonalTable.RecordSize + 0x0A),
            0xE654);
        var parsed = SwShPersonalTable.Parse(source);
        var records = parsed.Records.ToArray();
        records[0] = records[0] with { HP = records[0].HP == 255 ? 254 : records[0].HP + 1 };

        var written = SwShPersonalTable.Write(records, source);

        Assert.Equal(0xB000, BinaryPrimitives.ReadUInt16LittleEndian(written.AsSpan(0x0A)) & 0xF000);
        Assert.Equal(
            source.AsSpan(SwShPersonalTable.RecordSize, SwShPersonalTable.RecordSize).ToArray(),
            written.AsSpan(SwShPersonalTable.RecordSize, SwShPersonalTable.RecordSize).ToArray());
        var changedOffsets = source
            .Select((value, index) => (value, index))
            .Where(entry => entry.value != written[entry.index])
            .Select(entry => entry.index)
            .ToArray();
        Assert.Equal([0], changedOffsets);
    }

    [Fact]
    public void PersonalTableWriteRequiresPhysicalPersonalIdOrder()
    {
        var source = new byte[SwShPersonalTable.RecordSize * 2];
        var records = SwShPersonalTable.Parse(source).Records.ToArray();
        records[0] = records[0] with { PersonalId = 1 };

        var exception = Assert.Throws<InvalidDataException>(
            () => SwShPersonalTable.Write(records, source));

        Assert.Contains("physical index 0", exception.Message, StringComparison.Ordinal);
        Assert.Contains("PersonalId 1", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void PersonalRecordWriteRequiresExactRecordLength(int sizeDelta)
    {
        var record = SwShPersonalTable.Parse(new byte[SwShPersonalTable.RecordSize]).Records[0];
        var destination = new byte[SwShPersonalTable.RecordSize + sizeDelta];

        Assert.Throws<InvalidDataException>(
            () => SwShPersonalTable.WriteRecord(record, destination));
    }

    [Fact]
    public void PersonalRecordWriteRejectsOutOfRangeEVYields()
    {
        var record = SwShPersonalTable.Parse(new byte[SwShPersonalTable.RecordSize]).Records[0];
        SwShPersonalRecord[] invalidRecords =
        [
            record with { EVYieldHP = -1 },
            record with { EVYieldAttack = 4 },
            record with { EVYieldDefense = -1 },
            record with { EVYieldSpeed = 4 },
            record with { EVYieldSpecialAttack = -1 },
            record with { EVYieldSpecialDefense = 4 },
        ];

        foreach (var invalidRecord in invalidRecords)
        {
            Assert.Throws<InvalidDataException>(
                () => SwShPersonalTable.WriteRecord(
                    invalidRecord,
                    new byte[SwShPersonalTable.RecordSize]));
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(64)]
    public void PersonalRecordWriteRejectsOutOfRangeStorageColor(int color)
    {
        var record = SwShPersonalTable.Parse(new byte[SwShPersonalTable.RecordSize]).Records[0]
            with { Color = color };

        Assert.Throws<InvalidDataException>(
            () => SwShPersonalTable.WriteRecord(
                record,
                new byte[SwShPersonalTable.RecordSize]));
    }

    [Fact]
    public void LearnsetTableParsesMovesUntilSentinel()
    {
        var data = Enumerable.Repeat((byte)0xFF, SwShPokemonLearnsetTable.RecordSize).ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x00), 33);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x02), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x04), 45);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x06), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x08), ushort.MaxValue);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x0A), ushort.MaxValue);

        var parsed = SwShPokemonLearnsetTable.Parse(data);

        var learnset = Assert.Single(parsed.Records);
        Assert.Equal(0, learnset.PersonalId);
        Assert.Collection(
            learnset.Moves,
            move =>
            {
                Assert.Equal(0, move.Slot);
                Assert.Equal(33, move.MoveId);
                Assert.Equal(1, move.Level);
            },
            move =>
            {
                Assert.Equal(1, move.Slot);
                Assert.Equal(45, move.MoveId);
                Assert.Equal(3, move.Level);
            });
    }

    [Fact]
    public void LearnsetTableWritesRowsAndClearsStaleEntries()
    {
        var original = Enumerable.Repeat((byte)0xFF, SwShPokemonLearnsetTable.RecordSize).ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(original.AsSpan(0x00), 33);
        BinaryPrimitives.WriteUInt16LittleEndian(original.AsSpan(0x02), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(original.AsSpan(0x04), 45);
        BinaryPrimitives.WriteUInt16LittleEndian(original.AsSpan(0x06), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(original.AsSpan(0x08), 345);
        BinaryPrimitives.WriteUInt16LittleEndian(original.AsSpan(0x0A), 7);
        BinaryPrimitives.WriteUInt16LittleEndian(original.AsSpan(0x0C), ushort.MaxValue);
        BinaryPrimitives.WriteUInt16LittleEndian(original.AsSpan(0x0E), ushort.MaxValue);
        var edited = new SwShPokemonLearnsetRecord(
            0,
            [
                new SwShPokemonLearnsetMoveRecord(0, 33, 1),
                new SwShPokemonLearnsetMoveRecord(1, 345, 9),
            ]);

        var written = SwShPokemonLearnsetTable.Write([edited], original);
        var reparsed = SwShPokemonLearnsetTable.Parse(written).Records[0];

        Assert.Collection(
            reparsed.Moves,
            move =>
            {
                Assert.Equal(0, move.Slot);
                Assert.Equal(33, move.MoveId);
                Assert.Equal(1, move.Level);
            },
            move =>
            {
                Assert.Equal(1, move.Slot);
                Assert.Equal(345, move.MoveId);
                Assert.Equal(9, move.Level);
            });
        Assert.Equal(ushort.MaxValue, BinaryPrimitives.ReadUInt16LittleEndian(written.AsSpan(0x08)));
        Assert.Equal(ushort.MaxValue, BinaryPrimitives.ReadUInt16LittleEndian(written.AsSpan(0x0A)));
        Assert.Equal(ushort.MaxValue, BinaryPrimitives.ReadUInt16LittleEndian(written.AsSpan(0x0C)));
        Assert.Equal(ushort.MaxValue, BinaryPrimitives.ReadUInt16LittleEndian(written.AsSpan(0x0E)));
    }

    [Fact]
    public void LearnsetTableSupportsFull65MoveRecordWithoutTerminator()
    {
        var original = Enumerable.Repeat((byte)0xFF, SwShPokemonLearnsetTable.RecordSize).ToArray();
        var moves = Enumerable.Range(0, SwShPokemonLearnsetTable.MaxMovesPerRecord)
            .Select(index => new SwShPokemonLearnsetMoveRecord(index, index + 1, index))
            .ToArray();
        var edited = new SwShPokemonLearnsetRecord(0, moves);

        var written = SwShPokemonLearnsetTable.Write([edited], original);
        var reparsed = Assert.Single(SwShPokemonLearnsetTable.Parse(written).Records);

        Assert.Equal(65, SwShPokemonLearnsetTable.MaxMovesPerRecord);
        Assert.Equal(65, reparsed.Moves.Count);
        Assert.Equal(moves, reparsed.Moves);
        Assert.NotEqual(
            uint.MaxValue,
            BinaryPrimitives.ReadUInt32LittleEndian(written.AsSpan(written.Length - 4)));
    }

    [Fact]
    public void LearnsetTableWriteRequiresPhysicalPersonalIdOrder()
    {
        var original = Enumerable.Repeat((byte)0xFF, SwShPokemonLearnsetTable.RecordSize).ToArray();
        var record = new SwShPokemonLearnsetRecord(1, []);

        var exception = Assert.Throws<InvalidDataException>(
            () => SwShPokemonLearnsetTable.Write([record], original));

        Assert.Contains("physical index 0", exception.Message, StringComparison.Ordinal);
        Assert.Contains("PersonalId 1", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(0, 2)]
    [InlineData(1, 0)]
    [InlineData(0, 0)]
    public void LearnsetRecordWriteRejectsNoncontiguousSlots(int firstSlot, int secondSlot)
    {
        var record = new SwShPokemonLearnsetRecord(
            0,
            [
                new SwShPokemonLearnsetMoveRecord(firstSlot, 33, 1),
                new SwShPokemonLearnsetMoveRecord(secondSlot, 45, 3),
            ]);

        Assert.Throws<InvalidDataException>(
            () => SwShPokemonLearnsetTable.WriteRecord(
                record,
                new byte[SwShPokemonLearnsetTable.RecordSize]));
    }

    [Theory]
    [InlineData(ushort.MaxValue, 1)]
    [InlineData(1, 0xFF00)]
    [InlineData(1, ushort.MaxValue)]
    public void LearnsetTableRejectsPartialTerminatorTuples(int moveId, int level)
    {
        var data = Enumerable.Repeat((byte)0xFF, SwShPokemonLearnsetTable.RecordSize).ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x00), checked((ushort)moveId));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x02), checked((ushort)level));

        Assert.Throws<InvalidDataException>(() => SwShPokemonLearnsetTable.Parse(data));

        var record = new SwShPokemonLearnsetRecord(
            0,
            [new SwShPokemonLearnsetMoveRecord(0, moveId, level)]);
        Assert.Throws<InvalidDataException>(
            () => SwShPokemonLearnsetTable.WriteRecord(
                record,
                new byte[SwShPokemonLearnsetTable.RecordSize]));
    }

    [Fact]
    public void LearnsetRecordWriteRejectsTerminatorAsMoveData()
    {
        var record = new SwShPokemonLearnsetRecord(
            0,
            [new SwShPokemonLearnsetMoveRecord(0, ushort.MaxValue, ushort.MaxValue)]);

        Assert.Throws<InvalidDataException>(
            () => SwShPokemonLearnsetTable.WriteRecord(
                record,
                new byte[SwShPokemonLearnsetTable.RecordSize]));
    }

    [Fact]
    public void LearnsetTableRejectsDataAfterTerminator()
    {
        var data = Enumerable.Repeat((byte)0xFF, SwShPokemonLearnsetTable.RecordSize).ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x04), 33);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x06), 1);

        var exception = Assert.Throws<InvalidDataException>(
            () => SwShPokemonLearnsetTable.Parse(data));

        Assert.Contains("data after its terminator", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LearnsetTableWholeTableWriteLeavesUntouchedRecordsByteExact()
    {
        var original = Enumerable.Repeat(
                (byte)0xFF,
                SwShPokemonLearnsetTable.RecordSize * 2)
            .ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(original.AsSpan(0x00), 33);
        BinaryPrimitives.WriteUInt16LittleEndian(original.AsSpan(0x02), 1);
        var secondOffset = SwShPokemonLearnsetTable.RecordSize;
        for (var index = 0; index < SwShPokemonLearnsetTable.MaxMovesPerRecord; index++)
        {
            var offset = secondOffset + (index * 4);
            BinaryPrimitives.WriteUInt16LittleEndian(original.AsSpan(offset), checked((ushort)(index + 1)));
            BinaryPrimitives.WriteUInt16LittleEndian(original.AsSpan(offset + 2), checked((ushort)index));
        }

        var records = SwShPokemonLearnsetTable.Parse(original).Records.ToArray();
        records[0] = records[0] with
        {
            Moves =
            [
                records[0].Moves[0],
                new SwShPokemonLearnsetMoveRecord(1, 45, 3),
            ],
        };

        var written = SwShPokemonLearnsetTable.Write(records, original);

        Assert.Equal(
            original.AsSpan(secondOffset, SwShPokemonLearnsetTable.RecordSize).ToArray(),
            written.AsSpan(secondOffset, SwShPokemonLearnsetTable.RecordSize).ToArray());
    }

    [Fact]
    public void EvolutionSetSkipsEmptyEntries()
    {
        var data = new byte[SwShEvolutionSet.FileSize];
        WriteEvolution(data, 0, method: 4, argument: 0, species: 2, form: 0, level: 16);
        WriteEvolution(data, 2, method: 7, argument: 25, species: 3, form: 1, level: 32);

        var parsed = SwShEvolutionSet.Parse(data);

        Assert.Collection(
            parsed.Evolutions,
            evolution =>
            {
                Assert.Equal(0, evolution.Slot);
                Assert.Equal(4, evolution.Method);
                Assert.Equal(2, evolution.Species);
                Assert.Equal(16, evolution.Level);
            },
            evolution =>
            {
                Assert.Equal(2, evolution.Slot);
                Assert.Equal(7, evolution.Method);
                Assert.Equal(25, evolution.Argument);
                Assert.Equal(3, evolution.Species);
                Assert.Equal(1, evolution.Form);
                Assert.Equal(32, evolution.Level);
            });
    }

    [Fact]
    public void EvolutionSetWritesRowsToTheirPhysicalSlots()
    {
        var written = SwShEvolutionSet.Write(
        [
            new SwShEvolutionRecord(2, 4, 0, 2, 0, 16),
            new SwShEvolutionRecord(7, 7, 25, 3, 1, 32),
        ]);

        Assert.Equal(SwShEvolutionSet.FileSize, written.Length);
        var parsed = SwShEvolutionSet.Parse(written);
        Assert.Collection(
            parsed.Evolutions,
            evolution =>
            {
                Assert.Equal(2, evolution.Slot);
                Assert.Equal(4, evolution.Method);
                Assert.Equal(2, evolution.Species);
                Assert.Equal(16, evolution.Level);
            },
            evolution =>
            {
                Assert.Equal(7, evolution.Slot);
                Assert.Equal(7, evolution.Method);
                Assert.Equal(25, evolution.Argument);
                Assert.Equal(3, evolution.Species);
                Assert.Equal(1, evolution.Form);
                Assert.Equal(32, evolution.Level);
            });
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(written));
        Assert.Equal(4, BinaryPrimitives.ReadUInt16LittleEndian(written.AsSpan(SwShEvolutionSet.RecordSize * 2)));
        Assert.Equal(7, BinaryPrimitives.ReadUInt16LittleEndian(written.AsSpan(SwShEvolutionSet.RecordSize * 7)));
    }

    private static void WriteEvolution(
        byte[] data,
        int slot,
        ushort method,
        ushort argument,
        ushort species,
        byte form,
        byte level)
    {
        var offset = slot * SwShEvolutionSet.RecordSize;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), method);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset + 2), argument);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset + 4), species);
        data[offset + 6] = form;
        data[offset + 7] = level;
    }
}
