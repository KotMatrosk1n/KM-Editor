// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Diagnostics;
using KM.Formats.SwSh;
using Xunit;

namespace KM.Formats.Tests;

public sealed class SwShDynamaxAdventureArchiveTests
{
    [Fact]
    public void ParsePreservesDynamaxAdventureArchiveAndRefusesFullRebuild()
    {
        var archive = CreateArchive();
        var source = archive.Write();

        var parsed = SwShDynamaxAdventureArchive.Parse(source);

        Assert.Equal(2, parsed.Entries.Count);
        Assert.Equal(0, parsed.Entries[0].EntryIndex);
        Assert.True(parsed.Entries[0].IsSingleCapture);
        Assert.Equal(0x1122334455667788UL, parsed.Entries[0].SingleCaptureFlagBlock);
        Assert.Equal(133, parsed.Entries[0].Species);
        Assert.Equal(1, parsed.Entries[0].Form);
        Assert.Equal(65, parsed.Entries[0].Level);
        Assert.Equal(4, parsed.Entries[0].GuaranteedPerfectIvs());
        Assert.Equal(-1, parsed.Entries[0].Ivs.Attack);
        Assert.Equal(10, parsed.Entries[0].Moves[2]);
        Assert.True(parsed.Entries[0].IsStoryProgressGated);
        Assert.Equal(0x8877665544332211UL, parsed.Entries[0].UiMessageId);
        Assert.Equal(31, parsed.Entries[1].Ivs.SpecialDefense);

        var exception = Assert.Throws<InvalidOperationException>(() => parsed.Write());
        Assert.Contains("cannot be fully rebuilt", exception.Message, StringComparison.Ordinal);

        var preserved = parsed.WriteEdits([]);
        Assert.Equal(source, preserved);

        var reparsed = SwShDynamaxAdventureArchive.Parse(preserved);
        Assert.Equal(parsed.Entries.Count, reparsed.Entries.Count);
        Assert.Equal(parsed.Entries[0].Species, reparsed.Entries[0].Species);
        Assert.Equal(parsed.Entries[0].Ivs, reparsed.Entries[0].Ivs);
        Assert.Equal(parsed.Entries[0].Moves, reparsed.Entries[0].Moves);
        Assert.Equal(parsed.Entries[1].UiMessageId, reparsed.Entries[1].UiMessageId);
    }

    [Fact]
    public void GenericArchiveAllowsMoreThanCanonicalProductRowCount()
    {
        const int rowCount = 274;
        var template = CreateArchive().Entries[0];
        var archive = new SwShDynamaxAdventureArchive(
            Enumerable.Range(0, rowCount)
                .Select(index => template with
                {
                    EntryIndex = index,
                    Ivs = template.Ivs with { },
                    Moves = template.Moves.ToArray(),
                })
                .ToArray());

        var parsed = SwShDynamaxAdventureArchive.Parse(archive.Write());

        Assert.Equal(rowCount, parsed.Entries.Count);
    }

    [Fact]
    public void ParseRejectsEntryCountAboveGenericAllocationBoundBeforeVectorAllocation()
    {
        var data = CreateRootWithVectorLength((uint)SwShDynamaxAdventureArchive.MaximumEntryCount + 1);

        var exception = Assert.Throws<InvalidDataException>(() => SwShDynamaxAdventureArchive.Parse(data));

        Assert.Contains("exceeds the supported", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsVectorWhoseDeclaredElementsExceedSourceCapacity()
    {
        var data = CreateRootWithVectorLength(1);

        var exception = Assert.Throws<InvalidDataException>(() => SwShDynamaxAdventureArchive.Parse(data));

        Assert.Contains("invalid offset", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsAliasedEntryTables()
    {
        var data = CreateArchive().Write();
        var vectorOffset = ReadEntryVectorOffset(data);
        var firstElementOffset = vectorOffset + sizeof(uint);
        var secondElementOffset = firstElementOffset + sizeof(uint);
        var firstTableOffset = ReadUOffset(data, firstElementOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(
            data.AsSpan(secondElementOffset, sizeof(uint)),
            checked((uint)(firstTableOffset - secondElementOffset)));

        var exception = Assert.Throws<InvalidDataException>(() => SwShDynamaxAdventureArchive.Parse(data));

        Assert.Contains("aliases the same table", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsOverlappingEntryTableObjects()
    {
        var data = CreateOverlappingEntryTablesArchive();

        var exception = Assert.Throws<InvalidDataException>(() => SwShDynamaxAdventureArchive.Parse(data));

        Assert.Contains("overlaps another table", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsRootTableOverlappingItsOwnVtable()
    {
        var data = CreateRootWithVectorLength(0);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4, sizeof(ushort)), 10);

        var exception = Assert.Throws<InvalidDataException>(() => SwShDynamaxAdventureArchive.Parse(data));

        Assert.Contains("root table overlaps its own vtable", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsRootVectorStorageOverlappingRootTable()
    {
        var data = CreateRootWithVectorLength(0);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6, sizeof(ushort)), 12);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16, sizeof(uint)), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20, sizeof(uint)), 0);

        var exception = Assert.Throws<InvalidDataException>(() => SwShDynamaxAdventureArchive.Parse(data));

        Assert.Contains("vector storage overlaps root", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsEntryTableOverlappingItsOwnVtable()
    {
        var data = CreateEntryTablesArchive(
            (VtableOffset: 40, VtableLength: 70, ObjectLength: 8, TableOffset: 100));

        var exception = Assert.Throws<InvalidDataException>(() => SwShDynamaxAdventureArchive.Parse(data));

        Assert.Contains("entry table overlaps its own vtable", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseAllowsExactSharedEntryVtable()
    {
        var data = CreateEntryTablesArchive(
            (VtableOffset: 40, VtableLength: 54, ObjectLength: 8, TableOffset: 100),
            (VtableOffset: 40, VtableLength: 54, ObjectLength: 8, TableOffset: 120));

        var parsed = SwShDynamaxAdventureArchive.Parse(data);

        Assert.Equal(2, parsed.Entries.Count);
    }

    [Fact]
    public void ParseAllowsExactForwardSharedEntryVtable()
    {
        var data = CreateEntryTablesArchive(
            (VtableOffset: 160, VtableLength: 54, ObjectLength: 8, TableOffset: 100),
            (VtableOffset: 160, VtableLength: 54, ObjectLength: 8, TableOffset: 120));

        var parsed = SwShDynamaxAdventureArchive.Parse(data);

        Assert.Equal(2, parsed.Entries.Count);
    }

    [Fact]
    public void ParseAllowsEntryToShareExactRootVtable()
    {
        var data = CreateEntryTablesArchive(
            (VtableOffset: 4, VtableLength: 6, ObjectLength: 8, TableOffset: 40));

        var parsed = SwShDynamaxAdventureArchive.Parse(data);

        Assert.Single(parsed.Entries);
    }

    [Fact]
    public void ParseAllowsRootToShareExactForwardEntryVtable()
    {
        var data = CreateForwardRootSharedVtableArchive();

        var parsed = SwShDynamaxAdventureArchive.Parse(data);

        Assert.Single(parsed.Entries);
    }

    [Fact]
    public void ParseHandlesMaximumBoundedEntriesWithExactSharedVtablePromptly()
    {
        var rowCount = SwShDynamaxAdventureArchive.MaximumEntryCount;
        const int vectorOffset = 24;
        var vectorEnd = vectorOffset + sizeof(uint) + (rowCount * sizeof(uint));
        var sharedVtableOffset = vectorEnd;
        var firstTableOffset = sharedVtableOffset + (sizeof(ushort) * 2);
        var entries = Enumerable.Range(0, rowCount)
            .Select(index => (
                VtableOffset: sharedVtableOffset,
                VtableLength: sizeof(ushort) * 2,
                ObjectLength: sizeof(int),
                TableOffset: firstTableOffset + (index * sizeof(int))))
            .ToArray();
        var data = CreateEntryTablesArchive(entries);
        var stopwatch = Stopwatch.StartNew();

        var parsed = SwShDynamaxAdventureArchive.Parse(data);

        stopwatch.Stop();
        Assert.Equal(rowCount, parsed.Entries.Count);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"Bounded shared-vtable parse took {stopwatch.Elapsed}.");
    }

    [Fact]
    public void ParseRejectsPartiallyOverlappingEntryVtables()
    {
        var data = CreateEntryTablesArchive(
            (VtableOffset: 40, VtableLength: 54, ObjectLength: 100, TableOffset: 120),
            (VtableOffset: 60, VtableLength: 54, ObjectLength: 64, TableOffset: 240));

        var exception = Assert.Throws<InvalidDataException>(() => SwShDynamaxAdventureArchive.Parse(data));

        Assert.Contains("vtables partially overlap", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteEditsAppliesStableDynamaxAdventureFields()
    {
        var archive = CreateArchive();

        var edited = SwShDynamaxAdventureArchive.Parse(archive.WriteEdits(
        [
            new(0, SwShDynamaxAdventureField.Species, 25),
            new(0, SwShDynamaxAdventureField.Form, 2),
            new(0, SwShDynamaxAdventureField.Ability, 2),
            new(0, SwShDynamaxAdventureField.GigantamaxState, 2),
            new(0, SwShDynamaxAdventureField.Version, 2),
            new(0, SwShDynamaxAdventureField.ShinyRoll, 2),
            new(0, SwShDynamaxAdventureField.Move3, 85),
            new(0, SwShDynamaxAdventureField.GuaranteedPerfectIvs, 6),
            new(0, SwShDynamaxAdventureField.IvAttack, 31),
            new(0, SwShDynamaxAdventureField.IsSingleCapture, 0),
            new(0, SwShDynamaxAdventureField.IsStoryProgressGated, 0),
            new(0, SwShDynamaxAdventureField.OtGender, 0),
        ]));

        var entry = edited.Entries[0];
        Assert.Equal(25, entry.Species);
        Assert.Equal(2, entry.Form);
        Assert.Equal(2, entry.Ability);
        Assert.Equal(2, entry.GigantamaxState);
        Assert.Equal(2, entry.Version);
        Assert.Equal(2, entry.ShinyRoll);
        Assert.Equal(85, entry.Moves[3]);
        Assert.Equal(6, entry.GuaranteedPerfectIvs());
        Assert.Equal(31, entry.Ivs.Attack);
        Assert.False(entry.IsSingleCapture);
        Assert.False(entry.IsStoryProgressGated);
        Assert.Equal(0, entry.OtGender);

        Assert.Equal(0x1122334455667788UL, entry.SingleCaptureFlagBlock);
        Assert.Equal(0x8877665544332211UL, entry.UiMessageId);
    }

    [Fact]
    public void ParsedWriteEditsPreservesExistingFlatBufferLayout()
    {
        var original = CreateArchive().Write();
        ClearTableField(original, entryIndex: 1, fieldIndex: 3);
        ClearTableField(original, entryIndex: 1, fieldIndex: 11);

        var edited = SwShDynamaxAdventureArchive.Parse(original).WriteEdits(
        [
            new(1, SwShDynamaxAdventureField.Species, 467),
        ]);

        Assert.Equal(original.Length, edited.Length);
        Assert.Equal(0, ReadEntryFieldOffset(edited, entryIndex: 1, fieldIndex: 3));
        Assert.Equal(0, ReadEntryFieldOffset(edited, entryIndex: 1, fieldIndex: 11));

        var reparsed = SwShDynamaxAdventureArchive.Parse(edited);
        Assert.Equal(467, reparsed.Entries[1].Species);
        Assert.Equal(0, reparsed.Entries[1].Form);
        Assert.Equal(0, reparsed.Entries[1].Version);
    }

    [Fact]
    public void RebuildingParsedRecordsReintroducesOmittedFlatBufferDefaults()
    {
        var original = CreateArchive().Write();
        ClearTableField(original, entryIndex: 1, fieldIndex: 3);
        ClearTableField(original, entryIndex: 1, fieldIndex: 11);
        var parsed = SwShDynamaxAdventureArchive.Parse(original);

        var rebuilt = new SwShDynamaxAdventureArchive(parsed.Entries).Write();

        Assert.Equal(0, ReadEntryFieldOffset(original, entryIndex: 1, fieldIndex: 3));
        Assert.Equal(0, ReadEntryFieldOffset(original, entryIndex: 1, fieldIndex: 11));
        Assert.NotEqual(0, ReadEntryFieldOffset(rebuilt, entryIndex: 1, fieldIndex: 3));
        Assert.NotEqual(0, ReadEntryFieldOffset(rebuilt, entryIndex: 1, fieldIndex: 11));
    }

    [Fact]
    public void ParsedWriteEditsRejectsNonDefaultChangesToOmittedFields()
    {
        var original = CreateArchive().Write();
        ClearTableField(original, entryIndex: 1, fieldIndex: 3);

        var parsed = SwShDynamaxAdventureArchive.Parse(original);

        var exception = Assert.Throws<InvalidDataException>(() => parsed.WriteEdits(
        [
            new(1, SwShDynamaxAdventureField.Form, 2),
        ]));
        Assert.Contains("omitted FlatBuffer default", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParsedWriteRowCopiesCopiesMetadataAndPreservesTargetAdventureIndex()
    {
        var original = CreateArchive().Write();
        var parsed = SwShDynamaxAdventureArchive.Parse(original);

        var output = parsed.WriteRowCopies(
        [
            new(TargetEntryIndex: 0, SourceEntryIndex: 1, PreserveTargetAdventureIndex: true),
            new(TargetEntryIndex: 1, SourceEntryIndex: 0, PreserveTargetAdventureIndex: true),
        ]);
        var edited = SwShDynamaxAdventureArchive.Parse(output);

        Assert.Equal(original.Length, output.Length);

        Assert.Equal(25, edited.Entries[0].Species);
        Assert.Equal(0x0102030405060708UL, edited.Entries[0].SingleCaptureFlagBlock);
        Assert.Equal(0x0807060504030201UL, edited.Entries[0].UiMessageId);
        Assert.Equal(100, edited.Entries[0].AdventureIndex);
        Assert.Equal([3, 4, 5, 6], edited.Entries[0].Moves);

        Assert.Equal(133, edited.Entries[1].Species);
        Assert.Equal(0x1122334455667788UL, edited.Entries[1].SingleCaptureFlagBlock);
        Assert.Equal(0x8877665544332211UL, edited.Entries[1].UiMessageId);
        Assert.Equal(101, edited.Entries[1].AdventureIndex);
        Assert.Equal([1, 2, 10, 20], edited.Entries[1].Moves);
    }

    [Fact]
    public void ParsedWriteRowCopiesRejectsNonDefaultCopiesToOmittedFields()
    {
        var original = CreateArchive().Write();
        ClearTableField(original, entryIndex: 1, fieldIndex: 3);
        var parsed = SwShDynamaxAdventureArchive.Parse(original);

        var exception = Assert.Throws<InvalidDataException>(() => parsed.WriteRowCopies(
        [
            new(TargetEntryIndex: 1, SourceEntryIndex: 0, PreserveTargetAdventureIndex: true),
        ]));

        Assert.Contains("omitted FlatBuffer default", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteEditsRejectsInvalidGuaranteedPerfectIvCount()
    {
        var archive = CreateArchive();

        Assert.Throws<ArgumentOutOfRangeException>(() => archive.WriteEdits(
        [
            new(0, SwShDynamaxAdventureField.GuaranteedPerfectIvs, 7),
        ]));
    }

    [Fact]
    public void WriteEditsRejectsAmbiguousGuaranteedPerfectIvCount()
    {
        var archive = CreateArchive();

        Assert.Throws<ArgumentOutOfRangeException>(() => archive.WriteEdits(
        [
            new(0, SwShDynamaxAdventureField.GuaranteedPerfectIvs, 1),
        ]));
    }

    [Fact]
    public void WriteEditsRejectsUnsupportedIvOverrideSentinel()
    {
        var archive = CreateArchive();

        Assert.Throws<ArgumentOutOfRangeException>(() => archive.WriteEdits(
        [
            new(0, SwShDynamaxAdventureField.IvDefense, -2),
        ]));
    }

    private static SwShDynamaxAdventureArchive CreateArchive()
    {
        return new SwShDynamaxAdventureArchive(
        [
            new SwShDynamaxAdventureRecord(
                0,
                IsSingleCapture: true,
                SingleCaptureFlagBlock: 0x1122334455667788UL,
                Field02: 0,
                Form: 1,
                GigantamaxState: 1,
                BallItemId: 4,
                AdventureIndex: 100,
                Level: 65,
                Species: 133,
                UiMessageId: 0x8877665544332211UL,
                OtGender: 1,
                Version: 1,
                ShinyRoll: 1,
                new SwShDynamaxAdventureIvs(-4, -1, -1, -1, -1, -1),
                Ability: 1,
                IsStoryProgressGated: true,
                Moves: [1, 2, 10, 20]),
            new SwShDynamaxAdventureRecord(
                1,
                IsSingleCapture: false,
                SingleCaptureFlagBlock: 0x0102030405060708UL,
                Field02: 0,
                Form: 0,
                GigantamaxState: 0,
                BallItemId: 4,
                AdventureIndex: 101,
                Level: 60,
                Species: 25,
                UiMessageId: 0x0807060504030201UL,
                OtGender: 1,
                Version: 0,
                ShinyRoll: 1,
                new SwShDynamaxAdventureIvs(-1, 0, 1, 2, 3, 31),
                Ability: 0,
                IsStoryProgressGated: false,
                Moves: [3, 4, 5, 6]),
        ]);
    }

    private static byte[] CreateRootWithVectorLength(uint count)
    {
        const int rootVtableOffset = 4;
        const int rootTableOffset = 12;
        const int vectorOffset = 24;
        var data = new byte[vectorOffset + sizeof(uint)];

        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, sizeof(uint)), rootTableOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(rootVtableOffset, sizeof(ushort)), 6);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(rootVtableOffset + 2, sizeof(ushort)), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(rootVtableOffset + 4, sizeof(ushort)), 4);
        BinaryPrimitives.WriteInt32LittleEndian(
            data.AsSpan(rootTableOffset, sizeof(int)),
            rootTableOffset - rootVtableOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(
            data.AsSpan(rootTableOffset + 4, sizeof(uint)),
            vectorOffset - (rootTableOffset + 4));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(vectorOffset, sizeof(uint)), count);

        return data;
    }

    private static byte[] CreateOverlappingEntryTablesArchive()
    {
        return CreateEntryTablesArchive(
            (VtableOffset: 40, VtableLength: 54, ObjectLength: 100, TableOffset: 100),
            (VtableOffset: 40, VtableLength: 54, ObjectLength: 100, TableOffset: 180));
    }

    private static byte[] CreateForwardRootSharedVtableArchive()
    {
        const int rootTableOffset = 12;
        const int vectorOffset = 24;
        const int sharedVtableOffset = 40;
        const int entryTableOffset = 48;
        var data = new byte[56];

        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, sizeof(uint)), rootTableOffset);
        BinaryPrimitives.WriteInt32LittleEndian(
            data.AsSpan(rootTableOffset, sizeof(int)),
            rootTableOffset - sharedVtableOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(
            data.AsSpan(rootTableOffset + sizeof(uint), sizeof(uint)),
            vectorOffset - (rootTableOffset + sizeof(uint)));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(vectorOffset, sizeof(uint)), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            data.AsSpan(vectorOffset + sizeof(uint), sizeof(uint)),
            entryTableOffset - (vectorOffset + sizeof(uint)));

        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(sharedVtableOffset, sizeof(ushort)), 6);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(sharedVtableOffset + 2, sizeof(ushort)), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(sharedVtableOffset + 4, sizeof(ushort)), 4);
        BinaryPrimitives.WriteInt32LittleEndian(
            data.AsSpan(entryTableOffset, sizeof(int)),
            entryTableOffset - sharedVtableOffset);

        return data;
    }

    private static byte[] CreateEntryTablesArchive(
        params (int VtableOffset, int VtableLength, int ObjectLength, int TableOffset)[] entries)
    {
        const int rootVtableOffset = 4;
        const int rootTableOffset = 12;
        const int vectorOffset = 24;
        var vectorEnd = vectorOffset + sizeof(uint) + (entries.Length * sizeof(uint));
        var entryEnd = entries.Length == 0
            ? vectorEnd
            : entries.Max(entry => Math.Max(
                entry.VtableOffset + entry.VtableLength,
                entry.TableOffset + entry.ObjectLength));
        var data = new byte[Math.Max(vectorEnd, entryEnd)];

        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, sizeof(uint)), rootTableOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(rootVtableOffset, sizeof(ushort)), 6);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(rootVtableOffset + 2, sizeof(ushort)), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(rootVtableOffset + 4, sizeof(ushort)), 4);
        BinaryPrimitives.WriteInt32LittleEndian(
            data.AsSpan(rootTableOffset, sizeof(int)),
            rootTableOffset - rootVtableOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(
            data.AsSpan(rootTableOffset + 4, sizeof(uint)),
            vectorOffset - (rootTableOffset + 4));

        BinaryPrimitives.WriteUInt32LittleEndian(
            data.AsSpan(vectorOffset, sizeof(uint)),
            checked((uint)entries.Length));
        for (var index = 0; index < entries.Length; index++)
        {
            var elementOffset = vectorOffset + sizeof(uint) + (index * sizeof(uint));
            BinaryPrimitives.WriteUInt32LittleEndian(
                data.AsSpan(elementOffset, sizeof(uint)),
                checked((uint)(entries[index].TableOffset - elementOffset)));
        }

        foreach (var entry in entries)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                data.AsSpan(entry.VtableOffset, sizeof(ushort)),
                checked((ushort)entry.VtableLength));
            BinaryPrimitives.WriteUInt16LittleEndian(
                data.AsSpan(entry.VtableOffset + sizeof(ushort), sizeof(ushort)),
                checked((ushort)entry.ObjectLength));
        }

        foreach (var entry in entries)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                data.AsSpan(entry.TableOffset, sizeof(int)),
                entry.TableOffset - entry.VtableOffset);
        }

        return data;
    }

    private static void ClearTableField(byte[] data, int entryIndex, int fieldIndex)
    {
        var tableOffset = ReadEntryTableOffset(data, entryIndex);
        var vtableOffset = tableOffset - BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(tableOffset, sizeof(int)));
        var fieldEntryOffset = sizeof(ushort) * 2 + (fieldIndex * sizeof(ushort));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(vtableOffset + fieldEntryOffset, sizeof(ushort)), 0);
    }

    private static int ReadEntryFieldOffset(ReadOnlySpan<byte> data, int entryIndex, int fieldIndex)
    {
        var tableOffset = ReadEntryTableOffset(data, entryIndex);
        return ReadTableFieldOffset(data, tableOffset, fieldIndex);
    }

    private static int ReadEntryTableOffset(ReadOnlySpan<byte> data, int entryIndex)
    {
        var vectorOffset = ReadEntryVectorOffset(data);
        var elementOffset = vectorOffset + sizeof(uint) + (entryIndex * sizeof(uint));

        return ReadUOffset(data, elementOffset);
    }

    private static int ReadEntryVectorOffset(ReadOnlySpan<byte> data)
    {
        var rootTableOffset = ReadUOffset(data, offset: 0);
        var vectorFieldOffset = ReadTableFieldOffset(data, rootTableOffset, fieldIndex: 0);
        return ReadUOffset(data, rootTableOffset + vectorFieldOffset);
    }

    private static int ReadTableFieldOffset(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex)
    {
        var vtableOffset = tableOffset - BinaryPrimitives.ReadInt32LittleEndian(data.Slice(tableOffset, sizeof(int)));
        var fieldEntryOffset = sizeof(ushort) * 2 + (fieldIndex * sizeof(ushort));
        var vtableLength = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(vtableOffset, sizeof(ushort)));

        return fieldEntryOffset + sizeof(ushort) <= vtableLength
            ? BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(vtableOffset + fieldEntryOffset, sizeof(ushort)))
            : 0;
    }

    private static int ReadUOffset(ReadOnlySpan<byte> data, int offset)
    {
        return checked(offset + (int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint))));
    }
}

internal static class SwShDynamaxAdventureArchiveTestExtensions
{
    public static int GuaranteedPerfectIvs(this SwShDynamaxAdventureRecord entry)
    {
        return SwShDynamaxAdventureArchive.GetGuaranteedPerfectIvCount(entry.Ivs);
    }
}
