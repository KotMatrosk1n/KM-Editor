// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using KM.Formats.SwSh;
using Xunit;

namespace KM.Formats.Tests;

public sealed class SwShEncounterNestArchiveTests
{
    [Fact]
    public void WriteRoundTripsEncounterNestTables()
    {
        var archive = CreateArchive();

        var parsed = SwShEncounterNestArchive.Parse(archive.Write());

        var table = Assert.Single(parsed.Tables);
        Assert.Equal(0xAABBCCDD00112233UL, table.TableId);
        Assert.Equal(1, table.GameVersion);
        Assert.Collection(
            table.Entries,
            entry =>
            {
                Assert.Equal(0, entry.EntryIndex);
                Assert.Equal(1, entry.Species);
                Assert.Equal(2, entry.Form);
                Assert.Equal(0x1122334455667788UL, entry.LevelTableId);
                Assert.Equal(3, entry.Ability);
                Assert.True(entry.IsGigantamax);
                Assert.Equal(0x8877665544332211UL, entry.DropTableId);
                Assert.Equal(0x0102030405060708UL, entry.BonusTableId);
                Assert.Equal([100u, 20u, 30u, 40u, 50u], entry.Probabilities);
                Assert.Equal(2, entry.Gender);
                Assert.Equal(4, entry.FlawlessIvs);
            },
            entry =>
            {
                Assert.Equal(1, entry.EntryIndex);
                Assert.Equal(25, entry.Species);
                Assert.Equal(0, entry.Form);
                Assert.Equal(0x2233445566778899UL, entry.LevelTableId);
                Assert.Equal(4, entry.Ability);
                Assert.False(entry.IsGigantamax);
                Assert.Equal(0x9988776655443322UL, entry.DropTableId);
                Assert.Equal(0x0807060504030201UL, entry.BonusTableId);
                Assert.Equal([5u, 10u, 15u, 20u, 25u], entry.Probabilities);
                Assert.Equal(1, entry.Gender);
                Assert.Equal(2, entry.FlawlessIvs);
            });
    }

    [Fact]
    public void ParseAcceptsFourByteAlignedTablesWithAlignedUInt64Fields()
    {
        var source = CreateFourByteAlignedArchive();

        var parsed = SwShEncounterNestArchive.Parse(source);

        var table = Assert.Single(parsed.Tables);
        Assert.Equal(0x1122334455667788UL, table.TableId);
        Assert.Equal(1, table.GameVersion);
        var entry = Assert.Single(table.Entries);
        Assert.Equal(25, entry.Species);
        Assert.Equal(0x2233445566778899UL, entry.LevelTableId);
        Assert.Equal(0xAABBCCDD00112233UL, entry.DropTableId);
        Assert.Equal(0x0102030405060708UL, entry.BonusTableId);
        Assert.Equal([10u, 20u, 30u, 40u, 50u], entry.Probabilities);
        Assert.Equal(source, parsed.Write());

        var edited = parsed.WriteEdits(
        [
            new SwShEncounterNestEdit(0, 0, SwShEncounterNestField.Species, 133),
        ]);

        Assert.Equal(source.Length, edited.Length);
        Assert.Equal(133, SwShEncounterNestArchive.Parse(edited).Tables[0].Entries[0].Species);
    }

    [Fact]
    public void ParseRejectsTargetsThatAreNotFourByteAligned()
    {
        var source = CreateFourByteAlignedArchive();
        var tableVectorOffset = GetRootTableVectorOffset(source);
        var tableVectorElementOffset = tableVectorOffset + sizeof(uint);
        var tableOffset = ReadUOffset(source, tableVectorElementOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(
            source.AsSpan(tableVectorElementOffset, sizeof(uint)),
            checked((uint)((tableOffset + 1) - tableVectorElementOffset)));

        var exception = Assert.Throws<InvalidDataException>(() => SwShEncounterNestArchive.Parse(source));

        Assert.Contains("target is not aligned to 4 bytes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsEntryTargetsThatAreNotFourByteAligned()
    {
        var source = CreateFourByteAlignedArchive();
        var tableOffset = GetTableOffset(source, tableIndex: 0);
        var entriesVectorOffset = ReadUOffset(source, tableOffset + 16);
        var entryVectorElementOffset = entriesVectorOffset + sizeof(uint);
        var entryOffset = ReadUOffset(source, entryVectorElementOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(
            source.AsSpan(entryVectorElementOffset, sizeof(uint)),
            checked((uint)((entryOffset + 1) - entryVectorElementOffset)));

        var exception = Assert.Throws<InvalidDataException>(() => SwShEncounterNestArchive.Parse(source));

        Assert.Contains("target is not aligned to 4 bytes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsMisalignedUInt64FieldsInFourByteAlignedTables()
    {
        var source = CreateFourByteAlignedArchive();
        var tableOffset = GetTableOffset(source, tableIndex: 0);
        var vtableOffset = GetVtableOffset(source, tableOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(
            source.AsSpan(vtableOffset + (sizeof(ushort) * 2), sizeof(ushort)),
            8);

        var exception = Assert.Throws<InvalidDataException>(() => SwShEncounterNestArchive.Parse(source));

        Assert.Contains("field 0 is not aligned to 8 bytes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsMisalignedEntryUInt64FieldsInFourByteAlignedTables()
    {
        var source = CreateFourByteAlignedArchive();
        var tableOffset = GetTableOffset(source, tableIndex: 0);
        var entriesVectorOffset = ReadUOffset(source, tableOffset + 16);
        var entryOffset = ReadUOffset(source, entriesVectorOffset + sizeof(uint));
        var vtableOffset = GetVtableOffset(source, entryOffset);
        const int levelTableFieldIndex = 3;
        BinaryPrimitives.WriteUInt16LittleEndian(
            source.AsSpan(
                vtableOffset + (sizeof(ushort) * 2) + (levelTableFieldIndex * sizeof(ushort)),
                sizeof(ushort)),
            8);

        var exception = Assert.Throws<InvalidDataException>(() => SwShEncounterNestArchive.Parse(source));

        Assert.Contains("field 3 is not aligned to 8 bytes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteEditsUpdatesStableFieldsAndProbabilities()
    {
        var archive = CreateArchive();

        var output = archive.WriteEdits(
        [
            new SwShEncounterNestEdit(0, 1, SwShEncounterNestField.Species, 133),
            new SwShEncounterNestEdit(0, 1, SwShEncounterNestField.Form, 7),
            new SwShEncounterNestEdit(0, 1, SwShEncounterNestField.Ability, 2),
            new SwShEncounterNestEdit(0, 1, SwShEncounterNestField.IsGigantamax, 1),
            new SwShEncounterNestEdit(0, 1, SwShEncounterNestField.Gender, 3),
            new SwShEncounterNestEdit(0, 1, SwShEncounterNestField.FlawlessIvs, 6),
            new SwShEncounterNestEdit(0, 1, SwShEncounterNestField.Star5Probability, 80),
        ]);

        var entry = SwShEncounterNestArchive.Parse(output).Tables[0].Entries[1];
        Assert.Equal(133, entry.Species);
        Assert.Equal(7, entry.Form);
        Assert.Equal(2, entry.Ability);
        Assert.True(entry.IsGigantamax);
        Assert.Equal(3, entry.Gender);
        Assert.Equal(6, entry.FlawlessIvs);
        Assert.Equal(80u, entry.Probabilities[4]);
    }

    [Fact]
    public void WriteEditsRejectsInvalidProbability()
    {
        var archive = CreateArchive();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => archive.WriteEdits(
            [
                new SwShEncounterNestEdit(0, 0, SwShEncounterNestField.Star1Probability, 101),
            ]));
    }

    [Fact]
    public void WriteEditsRejectsInvalidFlawlessIvCount()
    {
        var archive = CreateArchive();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => archive.WriteEdits(
            [
                new SwShEncounterNestEdit(0, 0, SwShEncounterNestField.FlawlessIvs, 7),
            ]));
    }

    [Fact]
    public void ParsedArchivePreservesExactSourceForWriteEmptyEditsAndNoOpEdits()
    {
        var canonical = CreateArchive().Write();
        var source = canonical.Concat(new byte[] { 0xA5, 0x5A, 0xC3 }).ToArray();
        var archive = SwShEncounterNestArchive.Parse(source);

        var write = archive.Write();
        var emptyEdits = archive.WriteEdits([]);
        var noOpEdit = archive.WriteEdits(
        [
            new SwShEncounterNestEdit(0, 0, SwShEncounterNestField.Species, 1),
        ]);

        Assert.Equal(source, write);
        Assert.Equal(source, emptyEdits);
        Assert.Equal(source, noOpEdit);
    }

    [Fact]
    public void WriteEditsUsesLastValueAndReturnsExactSourceWhenLastValueRestoresSource()
    {
        var source = CreateArchive().Write();
        var archive = SwShEncounterNestArchive.Parse(source);

        var updated = archive.WriteEdits(
        [
            new SwShEncounterNestEdit(0, 0, SwShEncounterNestField.Species, 25),
            new SwShEncounterNestEdit(0, 0, SwShEncounterNestField.Species, 133),
        ]);
        var restored = archive.WriteEdits(
        [
            new SwShEncounterNestEdit(0, 0, SwShEncounterNestField.Species, 133),
            new SwShEncounterNestEdit(0, 0, SwShEncounterNestField.Species, 1),
        ]);

        Assert.Equal(133, SwShEncounterNestArchive.Parse(updated).Tables[0].Entries[0].Species);
        Assert.Equal(source, restored);
    }

    [Fact]
    public void WriteEditsPreservesProbabilityValuesAfterEditableStarColumns()
    {
        var table = CreateArchive().Tables[0];
        var entries = table.Entries.ToArray();
        entries[0] = entries[0] with { Probabilities = [100, 20, 30, 40, 50, 60, 70] };
        var source = new SwShEncounterNestArchive([table with { Entries = entries }]).Write();
        var archive = SwShEncounterNestArchive.Parse(source);

        var output = archive.WriteEdits(
        [
            new SwShEncounterNestEdit(0, 0, SwShEncounterNestField.Star3Probability, 44),
        ]);

        Assert.Equal(source.Length, output.Length);
        Assert.Equal(
            [100u, 20u, 44u, 40u, 50u, 60u, 70u],
            SwShEncounterNestArchive.Parse(output).Tables[0].Entries[0].Probabilities);
    }

    [Fact]
    public void WriteEditsIsolatesAliasedEncounterTables()
    {
        var firstTable = CreateArchive().Tables[0];
        var source = new SwShEncounterNestArchive(
        [
            firstTable,
            firstTable with { TableId = firstTable.TableId + 1 },
        ]).Write();
        var tableVectorOffset = GetRootTableVectorOffset(source);
        var firstTableOffset = ReadUOffset(source, tableVectorOffset + sizeof(uint));
        PatchUOffset(source, tableVectorOffset + (sizeof(uint) * 2), firstTableOffset);
        var archive = SwShEncounterNestArchive.Parse(source);

        var output = archive.WriteEdits(
        [
            new SwShEncounterNestEdit(0, 0, SwShEncounterNestField.Species, 133),
        ]);
        var parsed = SwShEncounterNestArchive.Parse(output);

        Assert.Equal(133, parsed.Tables[0].Entries[0].Species);
        Assert.Equal(1, parsed.Tables[1].Entries[0].Species);
    }

    [Fact]
    public void WriteEditsIsolatesAliasedEncounterEntries()
    {
        var source = CreateArchive().Write();
        var entriesVectorOffset = GetEntriesVectorOffset(source, tableIndex: 0);
        var firstEntryOffset = ReadUOffset(source, entriesVectorOffset + sizeof(uint));
        PatchUOffset(source, entriesVectorOffset + (sizeof(uint) * 2), firstEntryOffset);
        var archive = SwShEncounterNestArchive.Parse(source);

        var output = archive.WriteEdits(
        [
            new SwShEncounterNestEdit(0, 0, SwShEncounterNestField.Species, 133),
        ]);
        var parsed = SwShEncounterNestArchive.Parse(output);

        Assert.Equal(133, parsed.Tables[0].Entries[0].Species);
        Assert.Equal(1, parsed.Tables[0].Entries[1].Species);
    }

    [Fact]
    public void WriteEditsIsolatesAliasedProbabilityVectors()
    {
        var source = CreateArchive().Write();
        var firstEntryOffset = GetEntryOffset(source, tableIndex: 0, entryIndex: 0);
        var firstProbabilityFieldOffset = GetFieldValueOffset(source, firstEntryOffset, fieldIndex: 8);
        var secondProbabilityVectorOffset = GetProbabilityVectorOffset(source, tableIndex: 0, entryIndex: 1);
        PatchUOffset(source, firstProbabilityFieldOffset, secondProbabilityVectorOffset);
        var archive = SwShEncounterNestArchive.Parse(source);

        var output = archive.WriteEdits(
        [
            new SwShEncounterNestEdit(0, 0, SwShEncounterNestField.Star1Probability, 77),
        ]);
        var parsed = SwShEncounterNestArchive.Parse(output);

        Assert.Equal(77u, parsed.Tables[0].Entries[0].Probabilities[0]);
        Assert.Equal(5u, parsed.Tables[0].Entries[1].Probabilities[0]);
    }

    [Fact]
    public void WriteEditsMaterializesOmittedKnownScalarWhenNoUnknownFieldsArePresent()
    {
        var source = CreateArchive().Write();
        var entryOffset = GetEntryOffset(source, tableIndex: 0, entryIndex: 0);
        var vtableOffset = GetVtableOffset(source, entryOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(
            source.AsSpan(vtableOffset + (sizeof(ushort) * 2) + (4 * sizeof(ushort)), sizeof(ushort)),
            0);
        var archive = SwShEncounterNestArchive.Parse(source);
        Assert.Equal(0, archive.Tables[0].Entries[0].Ability);

        var output = archive.WriteEdits(
        [
            new SwShEncounterNestEdit(0, 0, SwShEncounterNestField.Ability, 2),
        ]);

        Assert.True(output.Length > source.Length);
        Assert.Equal(2, SwShEncounterNestArchive.Parse(output).Tables[0].Entries[0].Ability);
    }

    [Fact]
    public void WriteEditsPreservesUnknownMaterializedFieldsWhenEditedScalarIsPresent()
    {
        var (source, extendedEntryOffset) = CreateSourceWithExtendedEntry(omitAbility: false);
        var archive = SwShEncounterNestArchive.Parse(source);

        var output = archive.WriteEdits(
        [
            new SwShEncounterNestEdit(0, 0, SwShEncounterNestField.Species, 133),
        ]);

        Assert.Equal(source.Length, output.Length);
        Assert.Equal(0xA5, output[extendedEntryOffset + 48]);
        Assert.Equal(133, SwShEncounterNestArchive.Parse(output).Tables[0].Entries[0].Species);
    }

    [Fact]
    public void WriteEditsRejectsMaterializingOmittedScalarWhenUnknownFieldsArePresent()
    {
        var (source, _) = CreateSourceWithExtendedEntry(omitAbility: true);
        var archive = SwShEncounterNestArchive.Parse(source);
        Assert.Equal(0, archive.Tables[0].Entries[0].Ability);

        var exception = Assert.Throws<InvalidDataException>(
            () => archive.WriteEdits(
            [
                new SwShEncounterNestEdit(0, 0, SwShEncounterNestField.Ability, 2),
            ]));

        Assert.Contains("unknown materialized fields", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsProbabilityVectorsWithFewerThanFiveValues()
    {
        var source = CreateArchive().Write();
        var probabilitiesVectorOffset = GetProbabilityVectorOffset(source, tableIndex: 0, entryIndex: 0);
        BinaryPrimitives.WriteUInt32LittleEndian(
            source.AsSpan(probabilitiesVectorOffset, sizeof(uint)),
            4);

        var exception = Assert.Throws<InvalidDataException>(() => SwShEncounterNestArchive.Parse(source));

        Assert.Contains("contains 4 star probabilities; at least 5 are required", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseNormalizesOversizedVectorFaultToInvalidDataException()
    {
        var source = CreateArchive().Write();
        BinaryPrimitives.WriteUInt32LittleEndian(
            source.AsSpan(GetRootTableVectorOffset(source), sizeof(uint)),
            uint.MaxValue);

        Assert.Throws<InvalidDataException>(() => SwShEncounterNestArchive.Parse(source));
    }

    [Fact]
    public void WriteRejectsProbabilityVectorsWithFewerThanFiveValues()
    {
        var table = CreateArchive().Tables[0];
        var entries = table.Entries.ToArray();
        entries[0] = entries[0] with { Probabilities = [1, 2, 3, 4] };
        var archive = new SwShEncounterNestArchive([table with { Entries = entries }]);

        var exception = Assert.Throws<InvalidDataException>(() => archive.Write());

        Assert.Contains("contains 4 star probabilities; at least 5 are required", exception.Message, StringComparison.Ordinal);
    }

    private static SwShEncounterNestArchive CreateArchive()
    {
        return new SwShEncounterNestArchive(
        [
            new SwShEncounterNestTable(
                0xAABBCCDD00112233,
                1,
                [
                    new SwShEncounterNest(
                        0,
                        1,
                        2,
                        0x1122334455667788,
                        3,
                        true,
                        0x8877665544332211,
                        0x0102030405060708,
                        [100, 20, 30, 40, 50],
                        2,
                        4),
                    new SwShEncounterNest(
                        1,
                        25,
                        0,
                        0x2233445566778899,
                        4,
                        false,
                        0x9988776655443322,
                        0x0807060504030201,
                        [5, 10, 15, 20, 25],
                        1,
                        2),
                ]),
        ]);
    }

    private static byte[] CreateFourByteAlignedArchive()
    {
        const int rootTableOffset = 12;
        const int tableVectorOffset = 20;
        const int tableOffset = 44;
        const int entriesVectorOffset = 64;
        const int entryOffset = 100;
        const int probabilitiesVectorOffset = 148;
        var data = new byte[172];

        BinaryPrimitives.WriteUInt32LittleEndian(data, rootTableOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), 6);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(10), 4);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(rootTableOffset), 6);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(rootTableOffset + 4), 4);

        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(tableVectorOffset), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            data.AsSpan(tableVectorOffset + sizeof(uint)),
            tableOffset - (tableVectorOffset + sizeof(uint)));

        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(32), 10);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(34), 20);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(36), 4);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(38), 12);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(40), 16);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(tableOffset), 12);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(tableOffset + 4), 0x1122334455667788);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(tableOffset + 12), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(tableOffset + 16), 4);

        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(entriesVectorOffset), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            data.AsSpan(entriesVectorOffset + sizeof(uint)),
            entryOffset - (entriesVectorOffset + sizeof(uint)));

        ushort[] entryFieldOffsets = [12, 16, 20, 4, 24, 25, 28, 36, 44, 26, 27];
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(72), 26);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(74), 48);
        for (var fieldIndex = 0; fieldIndex < entryFieldOffsets.Length; fieldIndex++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                data.AsSpan(76 + (fieldIndex * sizeof(ushort))),
                entryFieldOffsets[fieldIndex]);
        }

        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(entryOffset), 28);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(entryOffset + 4), 0x2233445566778899);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(entryOffset + 12), 0);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(entryOffset + 16), 25);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(entryOffset + 20), 0);
        data[entryOffset + 24] = 2;
        data[entryOffset + 25] = 1;
        data[entryOffset + 26] = 1;
        data[entryOffset + 27] = 4;
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(entryOffset + 28), 0xAABBCCDD00112233);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(entryOffset + 36), 0x0102030405060708);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(entryOffset + 44), 4);

        uint[] probabilities = [10, 20, 30, 40, 50];
        BinaryPrimitives.WriteUInt32LittleEndian(
            data.AsSpan(probabilitiesVectorOffset),
            checked((uint)probabilities.Length));
        for (var index = 0; index < probabilities.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                data.AsSpan(probabilitiesVectorOffset + sizeof(uint) + (index * sizeof(uint))),
                probabilities[index]);
        }

        return data;
    }

    private static (byte[] Source, int EntryOffset) CreateSourceWithExtendedEntry(bool omitAbility)
    {
        var source = CreateArchive().Write();
        var logicalEntry = SwShEncounterNestArchive.Parse(source).Tables[0].Entries[0];
        var entryVectorElementOffset = GetEntryVectorElementOffset(source, tableIndex: 0, entryIndex: 0);
        var output = new List<byte>(source);
        const int vtableLength = 28;
        const int objectLength = 56;
        while (((output.Count + vtableLength) % sizeof(ulong)) != 0)
        {
            output.Add(0);
        }

        var vtableOffset = output.Count;
        var vtable = new byte[vtableLength];
        BinaryPrimitives.WriteUInt16LittleEndian(vtable.AsSpan(0, sizeof(ushort)), vtableLength);
        BinaryPrimitives.WriteUInt16LittleEndian(vtable.AsSpan(2, sizeof(ushort)), objectLength);
        var fieldOffsets = new ushort[] { 8, 12, 16, 24, 20, 21, 32, 40, 4, 22, 23, 48 };
        if (omitAbility)
        {
            fieldOffsets[4] = 0;
        }

        for (var fieldIndex = 0; fieldIndex < fieldOffsets.Length; fieldIndex++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                vtable.AsSpan((sizeof(ushort) * 2) + (fieldIndex * sizeof(ushort)), sizeof(ushort)),
                fieldOffsets[fieldIndex]);
        }

        output.AddRange(vtable);
        var entryOffset = output.Count;
        var table = new byte[objectLength];
        BinaryPrimitives.WriteInt32LittleEndian(table.AsSpan(0, sizeof(int)), entryOffset - vtableOffset);
        BinaryPrimitives.WriteInt32LittleEndian(table.AsSpan(8, sizeof(int)), logicalEntry.EntryIndex);
        BinaryPrimitives.WriteInt32LittleEndian(table.AsSpan(12, sizeof(int)), logicalEntry.Species);
        BinaryPrimitives.WriteInt32LittleEndian(table.AsSpan(16, sizeof(int)), logicalEntry.Form);
        table[20] = checked((byte)logicalEntry.Ability);
        table[21] = logicalEntry.IsGigantamax ? (byte)1 : (byte)0;
        table[22] = unchecked((byte)(sbyte)logicalEntry.Gender);
        table[23] = unchecked((byte)(sbyte)logicalEntry.FlawlessIvs);
        BinaryPrimitives.WriteUInt64LittleEndian(table.AsSpan(24, sizeof(ulong)), logicalEntry.LevelTableId);
        BinaryPrimitives.WriteUInt64LittleEndian(table.AsSpan(32, sizeof(ulong)), logicalEntry.DropTableId);
        BinaryPrimitives.WriteUInt64LittleEndian(table.AsSpan(40, sizeof(ulong)), logicalEntry.BonusTableId);
        table[48] = 0xA5;
        output.AddRange(table);

        var probabilitiesVectorOffset = output.Count;
        var probabilities = new byte[sizeof(uint) + (logicalEntry.Probabilities.Count * sizeof(uint))];
        BinaryPrimitives.WriteUInt32LittleEndian(
            probabilities.AsSpan(0, sizeof(uint)),
            checked((uint)logicalEntry.Probabilities.Count));
        for (var probabilityIndex = 0; probabilityIndex < logicalEntry.Probabilities.Count; probabilityIndex++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                probabilities.AsSpan(sizeof(uint) + (probabilityIndex * sizeof(uint)), sizeof(uint)),
                logicalEntry.Probabilities[probabilityIndex]);
        }

        output.AddRange(probabilities);
        var result = output.ToArray();
        PatchUOffset(result, entryOffset + sizeof(int), probabilitiesVectorOffset);
        PatchUOffset(result, entryVectorElementOffset, entryOffset);
        return (result, entryOffset);
    }

    private static int GetRootTableVectorOffset(byte[] data)
    {
        var rootTableOffset = ReadUOffset(data, offset: 0);
        return ReadUOffset(data, GetFieldValueOffset(data, rootTableOffset, fieldIndex: 0));
    }

    private static int GetTableOffset(byte[] data, int tableIndex)
    {
        var tableVectorOffset = GetRootTableVectorOffset(data);
        return ReadUOffset(
            data,
            tableVectorOffset + sizeof(uint) + (tableIndex * sizeof(uint)));
    }

    private static int GetEntriesVectorOffset(byte[] data, int tableIndex)
    {
        var tableOffset = GetTableOffset(data, tableIndex);
        return ReadUOffset(data, GetFieldValueOffset(data, tableOffset, fieldIndex: 2));
    }

    private static int GetEntryVectorElementOffset(byte[] data, int tableIndex, int entryIndex)
    {
        return GetEntriesVectorOffset(data, tableIndex)
            + sizeof(uint)
            + (entryIndex * sizeof(uint));
    }

    private static int GetEntryOffset(byte[] data, int tableIndex, int entryIndex)
    {
        return ReadUOffset(data, GetEntryVectorElementOffset(data, tableIndex, entryIndex));
    }

    private static int GetProbabilityVectorOffset(byte[] data, int tableIndex, int entryIndex)
    {
        var entryOffset = GetEntryOffset(data, tableIndex, entryIndex);
        return ReadUOffset(data, GetFieldValueOffset(data, entryOffset, fieldIndex: 8));
    }

    private static int GetFieldValueOffset(byte[] data, int tableOffset, int fieldIndex)
    {
        var vtableOffset = GetVtableOffset(data, tableOffset);
        var vtableLength = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(vtableOffset, sizeof(ushort)));
        var fieldEntryOffset = (sizeof(ushort) * 2) + (fieldIndex * sizeof(ushort));
        if (fieldEntryOffset + sizeof(ushort) > vtableLength)
        {
            throw new InvalidDataException($"FlatBuffer field {fieldIndex} is not present.");
        }

        var fieldOffset = BinaryPrimitives.ReadUInt16LittleEndian(
            data.AsSpan(vtableOffset + fieldEntryOffset, sizeof(ushort)));
        if (fieldOffset == 0)
        {
            throw new InvalidDataException($"FlatBuffer field {fieldIndex} is omitted.");
        }

        return tableOffset + fieldOffset;
    }

    private static int GetVtableOffset(byte[] data, int tableOffset)
    {
        return tableOffset - BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(tableOffset, sizeof(int)));
    }

    private static int ReadUOffset(byte[] data, int offset)
    {
        return checked(offset + (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, sizeof(uint))));
    }

    private static void PatchUOffset(byte[] data, int sourceOffset, int targetOffset)
    {
        Assert.True(targetOffset > sourceOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(
            data.AsSpan(sourceOffset, sizeof(uint)),
            checked((uint)(targetOffset - sourceOffset)));
    }
}
