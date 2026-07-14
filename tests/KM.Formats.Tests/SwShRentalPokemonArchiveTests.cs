// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Xunit;

namespace KM.Formats.Tests;

public sealed class SwShRentalPokemonArchiveTests
{
    [Fact]
    public void WriteRoundTripsRentalPokemonRecords()
    {
        var archive = CreateArchive();

        var reparsed = SwShRentalPokemonArchive.Parse(archive.Write());

        var rental = Assert.Single(reparsed.Rentals);
        Assert.Equal(0, rental.Index);
        Assert.Equal(3, rental.Form);
        Assert.Equal(4, rental.BallItemId);
        Assert.Equal(0x1122334455667788UL, rental.Hash1);
        Assert.Equal(25, rental.HeldItem);
        Assert.Equal(50, rental.Level);
        Assert.Equal(133, rental.Species);
        Assert.Equal(0x8877665544332211UL, rental.Hash2);
        Assert.Equal(12345u, rental.TrainerId);
        Assert.Equal(13, rental.Nature);
        Assert.Equal(2, rental.Gender);
        Assert.Equal(1, rental.Ability);
        Assert.Equal([33, 45, 98, 129], rental.Moves);
        Assert.Equal(new SwShRentalPokemonStats(10, 20, 30, 40, 50, 60), rental.Evs);
        Assert.Equal(new SwShRentalPokemonStats(1, 2, 3, 4, 5, 6), rental.Ivs);
    }

    [Fact]
    public void WriteEditsUpdatesStableFieldsAndPreservesHashes()
    {
        var archive = CreateArchive();

        var updated = SwShRentalPokemonArchive.Parse(archive.WriteEdits(
        [
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.Species, 25),
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.Move2, 85),
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.EvHp, 252),
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.IvSpecialAttack, 31),
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.FixedIvPreset, 31),
        ]));

        var rental = Assert.Single(updated.Rentals);
        Assert.Equal(25, rental.Species);
        Assert.Equal([33, 45, 85, 129], rental.Moves);
        Assert.Equal(252, rental.Evs.HP);
        Assert.Equal(new SwShRentalPokemonStats(31, 31, 31, 31, 31, 31), rental.Ivs);
        Assert.Equal(0x1122334455667788UL, rental.Hash1);
        Assert.Equal(0x8877665544332211UL, rental.Hash2);
    }

    [Fact]
    public void WriteEditsRejectsUnsupportedIvValue()
    {
        var archive = CreateArchive();

        Assert.Throws<ArgumentOutOfRangeException>(() => archive.WriteEdits(
        [
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.IvHp, -1),
        ]));
    }

    [Fact]
    public void ParsedEditsPreserveEveryUnownedByteAcrossRepeatedWrites()
    {
        var canonical = CreateArchive().Write();
        var rentalTableOffset = GetFirstRentalTableOffset(canonical);
        canonical[rentalTableOffset + 4] = 0xA5;
        var source = canonical.Concat(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }).ToArray();
        var parsed = SwShRentalPokemonArchive.Parse(source);

        var firstWrite = parsed.WriteEdits(
        [
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.EvHp, 11),
        ]);
        var changedOffsets = source
            .Select((value, index) => (value, index))
            .Where(entry => entry.value != firstWrite[entry.index])
            .Select(entry => entry.index)
            .ToArray();

        Assert.Single(changedOffsets);
        Assert.Equal(11, SwShRentalPokemonArchive.Parse(firstWrite).Rentals[0].Evs.HP);
        Assert.Equal(0xA5, firstWrite[rentalTableOffset + 4]);
        Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, firstWrite[^4..]);

        var secondWrite = SwShRentalPokemonArchive.Parse(firstWrite).WriteEdits(
        [
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.Move0, 85),
        ]);
        var reparsed = SwShRentalPokemonArchive.Parse(secondWrite).Rentals[0];

        Assert.Equal(11, reparsed.Evs.HP);
        Assert.Equal(85, reparsed.Moves[0]);
        Assert.Equal(0xA5, secondWrite[rentalTableOffset + 4]);
        Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, secondWrite[^4..]);
    }

    [Fact]
    public void ParsedEditsMaterializeOmittedKnownScalarsAndPreserveTheSourcePrefix()
    {
        var canonical = CreateArchive().Write();
        var rentalTableOffset = GetFirstRentalTableOffset(canonical);
        canonical[rentalTableOffset + sizeof(int)] = 0xA5;
        foreach (var fieldIndex in new[] { 3, 6, 13, 15, 19, 22 })
        {
            OmitRentalField(canonical, rentalTableOffset, fieldIndex);
        }

        var source = canonical.Concat(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }).ToArray();
        var vectorElementOffset = GetRentalVectorElementOffsets(source)[0];
        var parsed = SwShRentalPokemonArchive.Parse(source);

        Assert.Equal(0, parsed.Rentals[0].Evs.HP);
        Assert.Equal(0, parsed.Rentals[0].Form);
        Assert.Equal(0u, parsed.Rentals[0].TrainerId);
        Assert.Equal(0, parsed.Rentals[0].Gender);
        Assert.Equal(0, parsed.Rentals[0].Ivs.HP);
        Assert.Equal(0, parsed.Rentals[0].Ability);

        var output = parsed.WriteEdits(
        [
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.EvHp, 252),
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.Form, 1),
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.TrainerId, 54321),
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.Gender, 2),
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.IvHp, 31),
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.Ability, 2),
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.Move0, 85),
        ]);
        var reparsed = SwShRentalPokemonArchive.Parse(output).Rentals[0];

        Assert.Equal(252, reparsed.Evs.HP);
        Assert.Equal(1, reparsed.Form);
        Assert.Equal(54321u, reparsed.TrainerId);
        Assert.Equal(2, reparsed.Gender);
        Assert.Equal(31, reparsed.Ivs.HP);
        Assert.Equal(2, reparsed.Ability);
        Assert.Equal(85, reparsed.Moves[0]);
        Assert.Equal(0x1122334455667788UL, reparsed.Hash1);
        Assert.Equal(0x8877665544332211UL, reparsed.Hash2);
        Assert.True(output.Length > source.Length);

        for (var index = 0; index < source.Length; index++)
        {
            if (index >= vectorElementOffset && index < vectorElementOffset + sizeof(uint))
            {
                continue;
            }

            Assert.Equal(source[index], output[index]);
        }

        Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, output[canonical.Length..source.Length]);
        Assert.Equal(0xA5, output[rentalTableOffset + sizeof(int)]);
        var expandedTableOffset = GetFirstRentalTableOffset(output);
        Assert.True(expandedTableOffset >= source.Length);
        Assert.Equal(0xA5, output[expandedTableOffset + sizeof(int)]);
    }

    [Fact]
    public void ParsedEditsMaterializeSharedForwardVtablesForEachEditedRental()
    {
        var source = CreateArchiveWithTwoRentals().Write().ToList();
        var tableOffsets = GetRentalTableOffsets(CollectionsMarshal.AsSpan(source));
        var firstVtableOffset = GetRentalVtableOffset(CollectionsMarshal.AsSpan(source), tableOffsets[0]);
        var vtableLength = BinaryPrimitives.ReadUInt16LittleEndian(
            CollectionsMarshal.AsSpan(source)[firstVtableOffset..]);
        var sharedVtable = source.GetRange(firstVtableOffset, vtableLength).ToArray();
        OmitRentalField(sharedVtable, tableOffset: 0, fieldIndex: 6, vtableOffsetOverride: 0);
        while (source.Count % sizeof(ushort) != 0)
        {
            source.Add(0);
        }

        var sharedVtableOffset = source.Count;
        source.AddRange(sharedVtable);
        foreach (var tableOffset in tableOffsets)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                CollectionsMarshal.AsSpan(source)[tableOffset..],
                checked(tableOffset - sharedVtableOffset));
        }

        var sourceBytes = source.ToArray();
        var output = SwShRentalPokemonArchive.Parse(sourceBytes).WriteEdits(
        [
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.Form, 1),
            new SwShRentalPokemonEdit(1, SwShRentalPokemonField.Form, 2),
        ]);
        var outputTables = GetRentalTableOffsets(output);
        var rentals = SwShRentalPokemonArchive.Parse(output).Rentals;

        Assert.Equal(1, rentals[0].Form);
        Assert.Equal(2, rentals[1].Form);
        Assert.All(outputTables, tableOffset => Assert.True(tableOffset >= sourceBytes.Length));
        Assert.NotEqual(outputTables[0], outputTables[1]);
        Assert.Equal(
            sourceBytes.AsSpan(tableOffsets[0], 82).ToArray(),
            output.AsSpan(tableOffsets[0], 82).ToArray());
        Assert.Equal(
            sourceBytes.AsSpan(tableOffsets[1], 82).ToArray(),
            output.AsSpan(tableOffsets[1], 82).ToArray());
    }

    [Fact]
    public void ParsedEditsCanMaterializeAnotherOmittedFieldAfterReparse()
    {
        var source = CreateArchive().Write();
        var rentalTableOffset = GetFirstRentalTableOffset(source);
        OmitRentalField(source, rentalTableOffset, fieldIndex: 6);
        OmitRentalField(source, rentalTableOffset, fieldIndex: 22);

        var firstWrite = SwShRentalPokemonArchive.Parse(source).WriteEdits(
        [
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.Form, 1),
        ]);
        var secondWrite = SwShRentalPokemonArchive.Parse(firstWrite).WriteEdits(
        [
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.Ability, 2),
        ]);
        var reparsed = SwShRentalPokemonArchive.Parse(secondWrite).Rentals[0];

        Assert.Equal(1, reparsed.Form);
        Assert.Equal(2, reparsed.Ability);
        Assert.True(secondWrite.Length > firstWrite.Length);
    }

    [Fact]
    public void ParsedEditsRejectMaterializingWhenAnUnknownFieldIsReferenced()
    {
        var source = CreateArchive().Write().ToList();
        var tableOffset = GetFirstRentalTableOffset(CollectionsMarshal.AsSpan(source));
        var vtableOffset = GetRentalVtableOffset(CollectionsMarshal.AsSpan(source), tableOffset);
        var originalVtableLength = BinaryPrimitives.ReadUInt16LittleEndian(
            CollectionsMarshal.AsSpan(source)[vtableOffset..]);
        var extendedVtable = source.GetRange(vtableOffset, originalVtableLength).ToList();
        OmitRentalField(extendedVtable, tableOffset: 0, fieldIndex: 6, vtableOffsetOverride: 0);
        extendedVtable.AddRange(new byte[sizeof(ushort)]);
        BinaryPrimitives.WriteUInt16LittleEndian(
            CollectionsMarshal.AsSpan(extendedVtable),
            checked((ushort)extendedVtable.Count));
        BinaryPrimitives.WriteUInt16LittleEndian(
            CollectionsMarshal.AsSpan(extendedVtable)[originalVtableLength..],
            sizeof(int));
        while (source.Count % sizeof(ushort) != 0)
        {
            source.Add(0);
        }

        var extendedVtableOffset = source.Count;
        source.AddRange(extendedVtable);
        BinaryPrimitives.WriteInt32LittleEndian(
            CollectionsMarshal.AsSpan(source)[tableOffset..],
            checked(tableOffset - extendedVtableOffset));
        var parsed = SwShRentalPokemonArchive.Parse(CollectionsMarshal.AsSpan(source));

        var error = Assert.Throws<InvalidDataException>(() => parsed.WriteEdits(
        [
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.Form, 1),
        ]));

        Assert.Contains("unknown field 27", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParsedEditsRejectMaterializingAMalformedKnownFieldOffset()
    {
        var source = CreateArchive().Write().Concat(new byte[] { 0xCC }).ToArray();
        var tableOffset = GetFirstRentalTableOffset(source);
        var vtableOffset = GetRentalVtableOffset(source, tableOffset);
        var objectSize = BinaryPrimitives.ReadUInt16LittleEndian(
            source.AsSpan(vtableOffset + sizeof(ushort)));
        BinaryPrimitives.WriteUInt16LittleEndian(
            source.AsSpan(vtableOffset + (sizeof(ushort) * 2) + (3 * sizeof(ushort))),
            objectSize);
        OmitRentalField(source, tableOffset, fieldIndex: 6);
        var parsed = SwShRentalPokemonArchive.Parse(source);

        var error = Assert.Throws<InvalidDataException>(() => parsed.WriteEdits(
        [
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.Form, 1),
        ]));

        Assert.Contains("outside its table object", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void WriteEditsRejectsRentalLevelsOutsideTheGameRange(int level)
    {
        var archive = CreateArchive();

        Assert.Throws<ArgumentOutOfRangeException>(() => archive.WriteEdits(
        [
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.Level, level),
        ]));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void WriteEditsAcceptsRentalLevelBoundaries(int level)
    {
        var archive = CreateArchive();

        var output = SwShRentalPokemonArchive.Parse(archive.WriteEdits(
        [
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.Level, level),
        ]));

        Assert.Equal(level, output.Rentals[0].Level);
    }

    private static SwShRentalPokemonArchive CreateArchive()
    {
        return new SwShRentalPokemonArchive([CreateRental(0)]);
    }

    private static SwShRentalPokemonArchive CreateArchiveWithTwoRentals()
    {
        return new SwShRentalPokemonArchive([CreateRental(0), CreateRental(1)]);
    }

    private static SwShRentalPokemonRecord CreateRental(int index)
    {
        return new SwShRentalPokemonRecord(
            index,
            new SwShRentalPokemonStats(10, 20, 30, 40, 50, 60),
            3,
            4,
            0x1122334455667788UL,
            25,
            50,
            133,
            0x8877665544332211UL,
            12345,
            13,
            2,
            new SwShRentalPokemonStats(1, 2, 3, 4, 5, 6),
            1,
            [33, 45, 98, 129]);
    }

    private static int GetFirstRentalTableOffset(ReadOnlySpan<byte> data)
    {
        return GetRentalTableOffsets(data)[0];
    }

    private static int[] GetRentalTableOffsets(ReadOnlySpan<byte> data)
    {
        var elementOffsets = GetRentalVectorElementOffsets(data);
        var tableOffsets = new int[elementOffsets.Length];
        for (var index = 0; index < elementOffsets.Length; index++)
        {
            var elementOffset = elementOffsets[index];
            tableOffsets[index] = elementOffset
                + checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data[elementOffset..]));
        }

        return tableOffsets;
    }

    private static int[] GetRentalVectorElementOffsets(ReadOnlySpan<byte> data)
    {
        var rootTableOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data));
        var rootVtableOffset = rootTableOffset
            - BinaryPrimitives.ReadInt32LittleEndian(data[rootTableOffset..]);
        var vectorFieldOffset = BinaryPrimitives.ReadUInt16LittleEndian(
            data[(rootVtableOffset + (sizeof(ushort) * 2))..]);
        var vectorFieldLocation = rootTableOffset + vectorFieldOffset;
        var vectorOffset = vectorFieldLocation
            + checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data[vectorFieldLocation..]));
        var count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data[vectorOffset..]));
        return Enumerable.Range(0, count)
            .Select(index => vectorOffset + sizeof(uint) + (index * sizeof(uint)))
            .ToArray();
    }

    private static int GetRentalVtableOffset(ReadOnlySpan<byte> data, int tableOffset)
    {
        return tableOffset - BinaryPrimitives.ReadInt32LittleEndian(data[tableOffset..]);
    }

    private static void OmitRentalField(
        Span<byte> data,
        int tableOffset,
        int fieldIndex,
        int? vtableOffsetOverride = null)
    {
        var vtableOffset = vtableOffsetOverride ?? GetRentalVtableOffset(data, tableOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(
            data[(vtableOffset + (sizeof(ushort) * 2) + (fieldIndex * sizeof(ushort)))..],
            0);
    }

    private static void OmitRentalField(
        List<byte> data,
        int tableOffset,
        int fieldIndex,
        int? vtableOffsetOverride = null)
    {
        OmitRentalField(
            CollectionsMarshal.AsSpan(data),
            tableOffset,
            fieldIndex,
            vtableOffsetOverride);
    }
}
