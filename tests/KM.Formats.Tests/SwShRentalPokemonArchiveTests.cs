// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Xunit;

namespace KM.Formats.Tests;

public sealed class SwShRentalPokemonArchiveTests
{
    public static TheoryData<SwShRentalPokemonField, int, long> PhysicalScalarEdits => new()
    {
        { SwShRentalPokemonField.EvSpeed, 0, 61 },
        { SwShRentalPokemonField.EvAttack, 1, 21 },
        { SwShRentalPokemonField.EvDefense, 2, 31 },
        { SwShRentalPokemonField.EvHp, 3, 11 },
        { SwShRentalPokemonField.EvSpecialAttack, 4, 41 },
        { SwShRentalPokemonField.EvSpecialDefense, 5, 51 },
        { SwShRentalPokemonField.Form, 6, 2 },
        { SwShRentalPokemonField.BallItemId, 7, 5 },
        { SwShRentalPokemonField.HeldItem, 9, 26 },
        { SwShRentalPokemonField.Level, 10, 51 },
        { SwShRentalPokemonField.Species, 11, 134 },
        { SwShRentalPokemonField.TrainerId, 13, uint.MaxValue },
        { SwShRentalPokemonField.Nature, 14, 14 },
        { SwShRentalPokemonField.Gender, 15, 1 },
        { SwShRentalPokemonField.IvSpeed, 16, 7 },
        { SwShRentalPokemonField.IvAttack, 17, 3 },
        { SwShRentalPokemonField.IvDefense, 18, 4 },
        { SwShRentalPokemonField.IvHp, 19, 2 },
        { SwShRentalPokemonField.IvSpecialAttack, 20, 5 },
        { SwShRentalPokemonField.IvSpecialDefense, 21, 6 },
        { SwShRentalPokemonField.Ability, 22, 2 },
        { SwShRentalPokemonField.Move0, 23, 34 },
        { SwShRentalPokemonField.Move1, 24, 46 },
        { SwShRentalPokemonField.Move2, 25, 99 },
        { SwShRentalPokemonField.Move3, 26, 130 },
    };

    public static TheoryData<SwShRentalPokemonField, long> InvalidSemanticEdits => new()
    {
        { SwShRentalPokemonField.EvHp, -1 },
        { SwShRentalPokemonField.EvHp, 253 },
        { SwShRentalPokemonField.Form, 256 },
        { SwShRentalPokemonField.BallItemId, 17 },
        { SwShRentalPokemonField.BallItemId, 491 },
        { SwShRentalPokemonField.BallItemId, 500 },
        { SwShRentalPokemonField.BallItemId, 575 },
        { SwShRentalPokemonField.BallItemId, 577 },
        { SwShRentalPokemonField.BallItemId, 850 },
        { SwShRentalPokemonField.BallItemId, 852 },
        { SwShRentalPokemonField.HeldItem, -1 },
        { SwShRentalPokemonField.HeldItem, (long)int.MaxValue + 1 },
        { SwShRentalPokemonField.Level, 0 },
        { SwShRentalPokemonField.Level, 101 },
        { SwShRentalPokemonField.Species, 0 },
        { SwShRentalPokemonField.Species, (long)int.MaxValue + 1 },
        { SwShRentalPokemonField.TrainerId, -1 },
        { SwShRentalPokemonField.TrainerId, (long)uint.MaxValue + 1 },
        { SwShRentalPokemonField.Nature, 25 },
        { SwShRentalPokemonField.Gender, 3 },
        { SwShRentalPokemonField.IvHp, -1 },
        { SwShRentalPokemonField.IvHp, 32 },
        { SwShRentalPokemonField.Ability, 3 },
        { SwShRentalPokemonField.Move0, -1 },
        { SwShRentalPokemonField.Move0, (long)int.MaxValue + 1 },
        { SwShRentalPokemonField.FixedIvPreset, -1 },
        { SwShRentalPokemonField.FixedIvPreset, 32 },
    };

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

    [Theory]
    [MemberData(nameof(PhysicalScalarEdits))]
    public void ParsedEditsPatchTheExpectedRawFieldOnly(
        SwShRentalPokemonField field,
        int expectedFieldIndex,
        long value)
    {
        var source = CreateArchive().Write();
        var tableOffset = GetFirstRentalTableOffset(source);
        var fieldOffset = GetRentalFieldOffset(source, tableOffset, expectedFieldIndex);
        var fieldSize = GetRentalFieldSize(expectedFieldIndex);

        var output = SwShRentalPokemonArchive.Parse(source).WriteEdits(
        [
            new SwShRentalPokemonEdit(0, field, value),
        ]);

        Assert.Equal(source.Length, output.Length);
        Assert.Equal(
            GetExpectedScalarBytes(expectedFieldIndex, value),
            output.AsSpan(tableOffset + fieldOffset, fieldSize).ToArray());
        for (var index = 0; index < source.Length; index++)
        {
            if (index >= tableOffset + fieldOffset && index < tableOffset + fieldOffset + fieldSize)
            {
                continue;
            }

            Assert.Equal(source[index], output[index]);
        }
    }

    [Fact]
    public void ParsedNoOpAndSameValueEditsPreserveExactSourceBytes()
    {
        var source = CreateArchive().Write().Concat(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }).ToArray();
        var archive = SwShRentalPokemonArchive.Parse(source);

        Assert.Equal(source, archive.WriteEdits([]));
        Assert.Equal(source, archive.WriteEdits(
        [
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.Species, 133),
        ]));
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
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.TrainerId, uint.MaxValue),
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.Gender, 2),
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.IvHp, 31),
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.Ability, 2),
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.Move0, 85),
        ]);
        var reparsed = SwShRentalPokemonArchive.Parse(output).Rentals[0];

        Assert.Equal(252, reparsed.Evs.HP);
        Assert.Equal(1, reparsed.Form);
        Assert.Equal(uint.MaxValue, reparsed.TrainerId);
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
    public void ParsedEditsRejectChangingRowsWithMaterializedFutureFields()
    {
        var source = CreateArchive().Write().ToList();
        var tableOffset = GetFirstRentalTableOffset(CollectionsMarshal.AsSpan(source));
        var vtableOffset = GetRentalVtableOffset(CollectionsMarshal.AsSpan(source), tableOffset);
        var originalVtableLength = BinaryPrimitives.ReadUInt16LittleEndian(
            CollectionsMarshal.AsSpan(source)[vtableOffset..]);
        var extendedVtable = source.GetRange(vtableOffset, originalVtableLength).ToList();
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
        var sourceBytes = source.ToArray();
        var parsed = SwShRentalPokemonArchive.Parse(sourceBytes);

        Assert.Equal(sourceBytes, parsed.WriteEdits([]));

        var error = Assert.Throws<InvalidDataException>(() => parsed.WriteEdits(
        [
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.Form, 1),
        ]));

        Assert.Contains("materialized unknown fields", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsMalformedKnownFieldOffsets()
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
        var error = Assert.Throws<InvalidDataException>(() => SwShRentalPokemonArchive.Parse(source));

        Assert.Contains("outside its table object", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParsedEditsIsolateExactTableAliases()
    {
        var source = CreateArchiveWithTwoRentals().Write();
        var elementOffsets = GetRentalVectorElementOffsets(source);
        var originalTableOffset = GetRentalTableOffsets(source)[0];
        PatchUOffset(source, elementOffsets[1], originalTableOffset);
        var originalTableBytes = source.AsSpan(originalTableOffset, 82).ToArray();

        var output = SwShRentalPokemonArchive.Parse(source).WriteEdits(
        [
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.Species, 25),
        ]);
        var reparsed = SwShRentalPokemonArchive.Parse(output);
        var outputTableOffsets = GetRentalTableOffsets(output);

        Assert.Equal(25, reparsed.Rentals[0].Species);
        Assert.Equal(133, reparsed.Rentals[1].Species);
        Assert.NotEqual(outputTableOffsets[0], outputTableOffsets[1]);
        Assert.Equal(originalTableOffset, outputTableOffsets[1]);
        Assert.True(outputTableOffsets[0] >= source.Length);
        Assert.Equal(originalTableBytes, output.AsSpan(originalTableOffset, 82).ToArray());
    }

    [Fact]
    public void ParsedEditsMaterializeLateFieldsFromAShortenedVtable()
    {
        var source = CreateArchive().Write();
        var tableOffset = GetFirstRentalTableOffset(source);
        var vtableOffset = GetRentalVtableOffset(source, tableOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(
            source.AsSpan(vtableOffset),
            checked((ushort)((sizeof(ushort) * 2) + (23 * sizeof(ushort)))));
        var vectorElementOffset = GetRentalVectorElementOffsets(source)[0];

        var output = SwShRentalPokemonArchive.Parse(source).WriteEdits(
        [
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.Move3, 85),
        ]);
        var reparsed = SwShRentalPokemonArchive.Parse(output).Rentals[0];

        Assert.Equal([0, 0, 0, 85], reparsed.Moves);
        Assert.True(output.Length > source.Length);
        for (var index = 0; index < source.Length; index++)
        {
            if (index >= vectorElementOffset && index < vectorElementOffset + sizeof(uint))
            {
                continue;
            }

            Assert.Equal(source[index], output[index]);
        }
    }

    [Fact]
    public void ParseRejectsVectorLengthsBeyondTheAvailablePayload()
    {
        var source = CreateArchive().Write();
        var vectorOffset = GetRentalVectorOffset(source);
        BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(vectorOffset), uint.MaxValue);

        var error = Assert.Throws<InvalidDataException>(() => SwShRentalPokemonArchive.Parse(source));

        Assert.Contains("vector length", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsZeroUnsignedOffsets()
    {
        var source = CreateArchive().Write();
        var vectorElementOffset = GetRentalVectorElementOffsets(source)[0];
        BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(vectorElementOffset), 0);

        var error = Assert.Throws<InvalidDataException>(() => SwShRentalPokemonArchive.Parse(source));

        Assert.Contains("invalid unsigned offset", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsZeroVtableOffsets()
    {
        var source = CreateArchive().Write();
        var tableOffset = GetFirstRentalTableOffset(source);
        BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(tableOffset), 0);

        var error = Assert.Throws<InvalidDataException>(() => SwShRentalPokemonArchive.Parse(source));

        Assert.Contains("zero vtable offset", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseStrictlyValidatesTheRootTableLayout()
    {
        var source = CreateArchive().Write();
        var rootTableOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(source));
        BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(rootTableOffset), 0);

        var error = Assert.Throws<InvalidDataException>(() => SwShRentalPokemonArchive.Parse(source));

        Assert.Contains("zero vtable offset", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsMisalignedScalarFields()
    {
        var source = CreateArchive().Write();
        var tableOffset = GetFirstRentalTableOffset(source);
        SetRentalFieldOffset(source, tableOffset, fieldIndex: 7, fieldOffset: 25);

        var error = Assert.Throws<InvalidDataException>(() => SwShRentalPokemonArchive.Parse(source));

        Assert.Contains("not naturally aligned", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsOverlappingScalarFields()
    {
        var source = CreateArchive().Write();
        var tableOffset = GetFirstRentalTableOffset(source);
        var specialDefenseOffset = GetRentalFieldOffset(source, tableOffset, fieldIndex: 5);
        SetRentalFieldOffset(source, tableOffset, fieldIndex: 6, specialDefenseOffset);

        var error = Assert.Throws<InvalidDataException>(() => SwShRentalPokemonArchive.Parse(source));

        Assert.Contains("overlaps another scalar field", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsPartiallyOverlappingTableObjects()
    {
        var source = CreateArchiveWithTwoRentals().Write().Concat(new byte[8]).ToArray();
        var tableOffset = GetRentalTableOffsets(source)[0];
        var vtableOffset = GetRentalVtableOffset(source, tableOffset);
        var overlappingTableOffset = tableOffset + sizeof(ulong);
        BinaryPrimitives.WriteInt32LittleEndian(
            source.AsSpan(overlappingTableOffset),
            checked(overlappingTableOffset - vtableOffset));
        PatchUOffset(source, GetRentalVectorElementOffsets(source)[1], overlappingTableOffset);

        var error = Assert.Throws<InvalidDataException>(() => SwShRentalPokemonArchive.Parse(source));

        Assert.Contains("partially overlap", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsRentalTableObjectsOverlappingTheRentalVector()
    {
        var source = CreateArchiveWithRentals(3).Write().ToList();
        var originalTableOffset = GetRentalTableOffsets(CollectionsMarshal.AsSpan(source))[0];
        var originalVtableOffset = GetRentalVtableOffset(
            CollectionsMarshal.AsSpan(source),
            originalTableOffset);
        var vtableLength = BinaryPrimitives.ReadUInt16LittleEndian(
            CollectionsMarshal.AsSpan(source)[originalVtableOffset..]);
        var vtableBytes = source.GetRange(originalVtableOffset, vtableLength).ToArray();
        var elementOffsets = GetRentalVectorElementOffsets(CollectionsMarshal.AsSpan(source));
        while (source.Count % sizeof(ushort) != 0)
        {
            source.Add(0);
        }

        var appendedVtableOffset = source.Count;
        source.AddRange(vtableBytes);
        var overlappingTableOffset = elementOffsets[2];
        PatchUOffset(
            CollectionsMarshal.AsSpan(source),
            elementOffsets[0],
            overlappingTableOffset);
        BinaryPrimitives.WriteInt32LittleEndian(
            CollectionsMarshal.AsSpan(source)[overlappingTableOffset..],
            checked(overlappingTableOffset - appendedVtableOffset));

        var error = Assert.Throws<InvalidDataException>(() =>
            SwShRentalPokemonArchive.Parse(CollectionsMarshal.AsSpan(source)));

        Assert.Contains("table object overlaps the rental vector", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsRentalVtablesOverlappingTheRentalVector()
    {
        var source = CreateArchiveWithRentals(16).Write();
        var tableOffset = GetRentalTableOffsets(source)[0];
        var originalVtableOffset = GetRentalVtableOffset(source, tableOffset);
        var vtableLength = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(originalVtableOffset));
        var vtableBytes = source.AsSpan(originalVtableOffset, vtableLength).ToArray();
        var overlappingVtableOffset = GetRentalVectorOffset(source) + (sizeof(uint) * 2);
        vtableBytes.CopyTo(source.AsSpan(overlappingVtableOffset));
        BinaryPrimitives.WriteInt32LittleEndian(
            source.AsSpan(tableOffset),
            checked(tableOffset - overlappingVtableOffset));

        var error = Assert.Throws<InvalidDataException>(() => SwShRentalPokemonArchive.Parse(source));

        Assert.Contains("table vtable overlaps the rental vector", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsRentalVtablesOverlappingRootStructures()
    {
        var source = CreateArchive().Write();
        var rootTableOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(source));
        var rootVtableOffset = rootTableOffset
            - BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(rootTableOffset));
        var rentalTableOffset = GetFirstRentalTableOffset(source);
        BinaryPrimitives.WriteInt32LittleEndian(
            source.AsSpan(rentalTableOffset),
            checked(rentalTableOffset - rootVtableOffset));

        var error = Assert.Throws<InvalidDataException>(() => SwShRentalPokemonArchive.Parse(source));

        Assert.Contains("table vtable overlaps the root vtable", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsRentalVtablesOverlappingAnotherRentalObject()
    {
        var source = CreateArchiveWithTwoRentals().Write();
        var tableOffsets = GetRentalTableOffsets(source);
        var originalVtableOffset = GetRentalVtableOffset(source, tableOffsets[0]);
        var vtableLength = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(originalVtableOffset));
        var vtableBytes = source.AsSpan(originalVtableOffset, vtableLength).ToArray();
        var overlappingVtableOffset = tableOffsets[0] + sizeof(ulong);
        vtableBytes.CopyTo(source.AsSpan(overlappingVtableOffset));
        BinaryPrimitives.WriteInt32LittleEndian(
            source.AsSpan(tableOffsets[1]),
            checked(tableOffsets[1] - overlappingVtableOffset));

        var error = Assert.Throws<InvalidDataException>(() => SwShRentalPokemonArchive.Parse(source));

        Assert.Contains("table object overlaps another rental table vtable", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsPartiallyOverlappingRentalVtables()
    {
        var source = CreateArchiveWithTwoRentals().Write().ToList();
        var tableOffsets = GetRentalTableOffsets(CollectionsMarshal.AsSpan(source));
        var originalVtableOffset = GetRentalVtableOffset(
            CollectionsMarshal.AsSpan(source),
            tableOffsets[0]);
        var vtableLength = BinaryPrimitives.ReadUInt16LittleEndian(
            CollectionsMarshal.AsSpan(source)[originalVtableOffset..]);
        var originalVtable = source.GetRange(originalVtableOffset, vtableLength).ToArray();
        while (source.Count % sizeof(ushort) != 0)
        {
            source.Add(0);
        }

        var firstVtableOffset = source.Count;
        source.AddRange(new byte[sizeof(ushort)]);
        source.AddRange(originalVtable);
        BinaryPrimitives.WriteUInt16LittleEndian(
            CollectionsMarshal.AsSpan(source)[firstVtableOffset..],
            sizeof(ushort) * 2);
        BinaryPrimitives.WriteInt32LittleEndian(
            CollectionsMarshal.AsSpan(source)[tableOffsets[0]..],
            checked(tableOffsets[0] - firstVtableOffset));
        BinaryPrimitives.WriteInt32LittleEndian(
            CollectionsMarshal.AsSpan(source)[tableOffsets[1]..],
            checked(tableOffsets[1] - (firstVtableOffset + sizeof(ushort))));

        var error = Assert.Throws<InvalidDataException>(() =>
            SwShRentalPokemonArchive.Parse(CollectionsMarshal.AsSpan(source)));

        Assert.Contains("table vtables partially overlap", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(InvalidSemanticEdits))]
    public void WriteEditsRejectsUnsupportedSemanticValues(SwShRentalPokemonField field, long value)
    {
        var archive = CreateArchive();

        Assert.Throws<ArgumentOutOfRangeException>(() => archive.WriteEdits(
        [
            new SwShRentalPokemonEdit(0, field, value),
        ]));
    }

    [Fact]
    public void WriteEditsRejectsEvTotalsAboveThePokemonLimit()
    {
        var archive = CreateArchive();

        Assert.Throws<ArgumentOutOfRangeException>(() => archive.WriteEdits(
        [
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.EvHp, 252),
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.EvAttack, 252),
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.EvDefense, 6),
        ]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(492)]
    [InlineData(499)]
    [InlineData(576)]
    [InlineData(851)]
    public void WriteEditsAcceptsEverySwordShieldBallIdRangeBoundary(int ballItemId)
    {
        var output = CreateArchive().WriteEdits(
        [
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.BallItemId, ballItemId),
        ]);

        Assert.True(SwShRentalPokemonArchive.IsValidBallItemId(ballItemId));
        Assert.Equal(ballItemId, SwShRentalPokemonArchive.Parse(output).Rentals[0].BallItemId);
    }

    [Fact]
    public void ParsedEditsPreserveUntouchedLegacyScalarValues()
    {
        var source = CreateArchive().Write();
        var tableOffset = GetFirstRentalTableOffset(source);
        var abilityOffset = GetRentalFieldOffset(source, tableOffset, fieldIndex: 22);
        var ivHpOffset = GetRentalFieldOffset(source, tableOffset, fieldIndex: 19);
        BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(tableOffset + abilityOffset), 99);
        source[tableOffset + ivHpOffset] = 0xFF;

        var output = SwShRentalPokemonArchive.Parse(source).WriteEdits(
        [
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.Move0, 85),
        ]);
        var reparsed = SwShRentalPokemonArchive.Parse(output).Rentals[0];

        Assert.Equal(99, reparsed.Ability);
        Assert.Equal(-1, reparsed.Ivs.HP);
        Assert.Equal(85, reparsed.Moves[0]);
        Assert.Equal(source.AsSpan(tableOffset + abilityOffset, sizeof(int)).ToArray(),
            output.AsSpan(tableOffset + abilityOffset, sizeof(int)).ToArray());
        Assert.Equal(source[tableOffset + ivHpOffset], output[tableOffset + ivHpOffset]);
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
        return CreateArchiveWithRentals(2);
    }

    private static SwShRentalPokemonArchive CreateArchiveWithRentals(int count)
    {
        return new SwShRentalPokemonArchive(
            Enumerable.Range(0, count).Select(CreateRental).ToArray());
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

    private static int GetRentalVectorOffset(ReadOnlySpan<byte> data)
    {
        var rootTableOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data));
        var rootVtableOffset = rootTableOffset
            - BinaryPrimitives.ReadInt32LittleEndian(data[rootTableOffset..]);
        var vectorFieldOffset = BinaryPrimitives.ReadUInt16LittleEndian(
            data[(rootVtableOffset + (sizeof(ushort) * 2))..]);
        var vectorFieldLocation = rootTableOffset + vectorFieldOffset;
        return vectorFieldLocation
            + checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data[vectorFieldLocation..]));
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
        var vectorOffset = GetRentalVectorOffset(data);
        var count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data[vectorOffset..]));
        return Enumerable.Range(0, count)
            .Select(index => vectorOffset + sizeof(uint) + (index * sizeof(uint)))
            .ToArray();
    }

    private static int GetRentalVtableOffset(ReadOnlySpan<byte> data, int tableOffset)
    {
        return tableOffset - BinaryPrimitives.ReadInt32LittleEndian(data[tableOffset..]);
    }

    private static int GetRentalFieldOffset(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex)
    {
        var vtableOffset = GetRentalVtableOffset(data, tableOffset);
        return BinaryPrimitives.ReadUInt16LittleEndian(
            data[(vtableOffset + (sizeof(ushort) * 2) + (fieldIndex * sizeof(ushort)))..]);
    }

    private static void SetRentalFieldOffset(
        Span<byte> data,
        int tableOffset,
        int fieldIndex,
        int fieldOffset)
    {
        var vtableOffset = GetRentalVtableOffset(data, tableOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(
            data[(vtableOffset + (sizeof(ushort) * 2) + (fieldIndex * sizeof(ushort)))..],
            checked((ushort)fieldOffset));
    }

    private static void PatchUOffset(Span<byte> data, int sourceOffset, int targetOffset)
    {
        Assert.True(targetOffset > sourceOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(
            data[sourceOffset..],
            checked((uint)(targetOffset - sourceOffset)));
    }

    private static int GetRentalFieldSize(int fieldIndex)
    {
        return fieldIndex switch
        {
            >= 0 and <= 6 => sizeof(byte),
            7 or 9 or 11 or 13 or 14 or 15 or >= 22 and <= 26 => sizeof(int),
            8 or 12 => sizeof(ulong),
            10 or >= 16 and <= 21 => sizeof(byte),
            _ => throw new ArgumentOutOfRangeException(nameof(fieldIndex)),
        };
    }

    private static byte[] GetExpectedScalarBytes(int fieldIndex, long value)
    {
        var output = new byte[GetRentalFieldSize(fieldIndex)];
        switch (fieldIndex)
        {
            case >= 0 and <= 6:
            case 10:
            case >= 16 and <= 21:
                output[0] = checked((byte)value);
                break;
            case 13:
                BinaryPrimitives.WriteUInt32LittleEndian(output, checked((uint)value));
                break;
            default:
                BinaryPrimitives.WriteInt32LittleEndian(output, checked((int)value));
                break;
        }

        return output;
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
