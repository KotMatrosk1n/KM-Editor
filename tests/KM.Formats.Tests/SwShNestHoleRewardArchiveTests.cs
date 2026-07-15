// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using KM.Formats.SwSh;
using Xunit;

namespace KM.Formats.Tests;

public sealed class SwShNestHoleRewardArchiveTests
{
    [Fact]
    public void WriteRoundTripsRewardTables()
    {
        var archive = CreateArchive();

        var parsed = SwShNestHoleRewardArchive.Parse(archive.Write());

        var table = Assert.Single(parsed.Tables);
        Assert.Equal(0xAABBCCDD00112233UL, table.TableId);
        Assert.Collection(
            table.Rewards,
            reward =>
            {
                Assert.Equal(10u, reward.EntryId);
                Assert.Equal(3u, reward.ItemId);
                Assert.Equal([40u, 30u, 20u, 10u, 5u], reward.Values);
            },
            reward =>
            {
                Assert.Equal(11u, reward.EntryId);
                Assert.Equal(2u, reward.ItemId);
                Assert.Equal([5u, 10u, 15u, 20u, 25u], reward.Values);
            });
    }

    [Fact]
    public void ParseAcceptsOfficialFourByteAlignedRewardTableLayout()
    {
        var source = Convert.FromHexString(
            "0C00000000000600080004000600000004000000010000000C0000000800100004000C0008000000887766554433221104000000010000001000000000000A001000040008000C000A000000010000000200000004000000050000000100000002000000030000000400000005000000");

        var parsed = SwShNestHoleRewardArchive.Parse(source);

        var table = Assert.Single(parsed.Tables);
        Assert.Equal(0x1122334455667788UL, table.TableId);
        var reward = Assert.Single(table.Rewards);
        Assert.Equal(1u, reward.EntryId);
        Assert.Equal(2u, reward.ItemId);
        Assert.Equal([1u, 2u, 3u, 4u, 5u], reward.Values);
        Assert.Equal(source, parsed.Write());

        var edited = parsed.WriteEdits(
        [
            new SwShNestHoleRewardEdit(0, 0, SwShNestHoleRewardField.Star5Value, 9),
        ]);
        Assert.Equal(9u, SwShNestHoleRewardArchive.Parse(edited).Tables[0].Rewards[0].Values[4]);
    }

    [Fact]
    public void WriteAndWriteEditsPreserveFullWidthItemIds()
    {
        var archive = new SwShNestHoleRewardArchive(
        [
            new SwShNestHoleRewardTable(
                1,
                [new SwShNestHoleReward(1, uint.MaxValue, [1, 2, 3, 4, 5])]),
        ]);

        var source = archive.Write();
        var parsed = SwShNestHoleRewardArchive.Parse(source);
        var edited = parsed.WriteEdits(
        [
            new SwShNestHoleRewardEdit(0, 0, SwShNestHoleRewardField.ItemId, uint.MaxValue - 1),
        ]);

        Assert.Equal(uint.MaxValue, parsed.Tables[0].Rewards[0].ItemId);
        Assert.Equal(uint.MaxValue - 1, SwShNestHoleRewardArchive.Parse(edited).Tables[0].Rewards[0].ItemId);
    }

    [Fact]
    public void OmittedZeroItemFieldCanBeMaterializedWithoutRewritingSourceBytes()
    {
        var source = ReplaceFirstRewardWithOmittedItemTable(
            CreateArchive().Write(),
            includeUnknownField: false,
            out _);
        var firstReward = ReadRewardLayout(source, tableIndex: 0, rewardIndex: 0);
        var secondReward = ReadRewardLayout(source, tableIndex: 0, rewardIndex: 1);
        WriteUOffset(source, secondReward.RewardVectorElementOffset, firstReward.RewardOffset);
        var parsed = SwShNestHoleRewardArchive.Parse(source);

        Assert.Equal(0u, parsed.Tables[0].Rewards[0].ItemId);
        Assert.Equal(
            source,
            parsed.WriteEdits(
            [
                new SwShNestHoleRewardEdit(0, 0, SwShNestHoleRewardField.ItemId, 0),
            ]));

        var output = parsed.WriteEdits(
        [
            new SwShNestHoleRewardEdit(0, 0, SwShNestHoleRewardField.ItemId, 44),
            new SwShNestHoleRewardEdit(0, 0, SwShNestHoleRewardField.Star1Value, 77),
        ]);

        Assert.True(output.Length > source.Length);
        for (var index = 0; index < source.Length; index++)
        {
            if (index >= firstReward.RewardVectorElementOffset
                && index < firstReward.RewardVectorElementOffset + sizeof(uint))
            {
                continue;
            }

            Assert.Equal(source[index], output[index]);
        }

        var reparsed = SwShNestHoleRewardArchive.Parse(output);
        Assert.Equal(44u, reparsed.Tables[0].Rewards[0].ItemId);
        Assert.Equal(77u, reparsed.Tables[0].Rewards[0].Values[0]);
        Assert.Equal(0u, reparsed.Tables[0].Rewards[1].ItemId);
        Assert.Equal(40u, reparsed.Tables[0].Rewards[1].Values[0]);
    }

    [Fact]
    public void OmittedItemFieldWithUnknownMaterializedFieldsIsRejectedSafely()
    {
        var source = ReplaceFirstRewardWithOmittedItemTable(
            CreateArchive().Write(),
            includeUnknownField: true,
            out var unknownValueOffset);
        var parsed = SwShNestHoleRewardArchive.Parse(source);

        var error = Assert.Throws<InvalidDataException>(() => parsed.WriteEdits(
        [
            new SwShNestHoleRewardEdit(0, 0, SwShNestHoleRewardField.ItemId, 44),
        ]));

        Assert.Contains("unknown materialized fields", error.Message, StringComparison.Ordinal);
        Assert.Equal(0xDEADBEEFu, BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(unknownValueOffset, 4)));
        Assert.Equal(source, parsed.Write());
    }

    [Fact]
    public void ParsedNoOpAndSameValueEditsPreserveEverySourceByte()
    {
        var source = CreateArchive().Write().Concat(new byte[] { 0xA5, 0x5A, 0xC3 }).ToArray();
        var parsed = SwShNestHoleRewardArchive.Parse(source);

        Assert.Equal(source, parsed.Write());
        Assert.Equal(source, parsed.WriteEdits([]));
        Assert.Equal(
            source,
            parsed.WriteEdits(
            [
                new SwShNestHoleRewardEdit(0, 0, SwShNestHoleRewardField.ItemId, 3),
                new SwShNestHoleRewardEdit(0, 0, SwShNestHoleRewardField.Star3Value, 20),
            ]));
    }

    [Fact]
    public void ParsedModelChangesStillSerializeAndCombineWithExplicitEdits()
    {
        var source = CreateArchive().Write();
        var parsed = SwShNestHoleRewardArchive.Parse(source);
        var rewards = parsed.Tables[0].Rewards.ToArray();
        rewards[0] = rewards[0] with { ItemId = 44 };
        var tables = parsed.Tables.ToArray();
        tables[0] = tables[0] with { Rewards = rewards };
        var changed = parsed with { Tables = tables };

        var changedOutput = changed.Write();
        var editedOutput = changed.WriteEdits(
        [
            new SwShNestHoleRewardEdit(0, 0, SwShNestHoleRewardField.Star1Value, 77),
        ]);

        Assert.Equal(44u, SwShNestHoleRewardArchive.Parse(changedOutput).Tables[0].Rewards[0].ItemId);
        var edited = SwShNestHoleRewardArchive.Parse(editedOutput).Tables[0].Rewards[0];
        Assert.Equal(44u, edited.ItemId);
        Assert.Equal(77u, edited.Values[0]);
    }

    [Fact]
    public void WriteEditsUpdatesItemAndStarValues()
    {
        var archive = CreateArchive();

        var output = archive.WriteEdits(
        [
            new SwShNestHoleRewardEdit(0, 1, SwShNestHoleRewardField.ItemId, 4),
            new SwShNestHoleRewardEdit(0, 1, SwShNestHoleRewardField.Star5Value, 80),
        ]);

        var parsed = SwShNestHoleRewardArchive.Parse(output);
        var reward = parsed.Tables[0].Rewards[1];
        Assert.Equal(4u, reward.ItemId);
        Assert.Equal(80u, reward.Values[4]);
    }

    [Fact]
    public void ParsedEditPreservesUnknownRewardFieldAndTrailingBytes()
    {
        var source = AddUnknownRewardScalarField(CreateArchive().Write(), out var unknownValueOffset);
        var parsed = SwShNestHoleRewardArchive.Parse(source);
        var sourceLayout = ReadRewardLayout(source, tableIndex: 0, rewardIndex: 0);

        var output = parsed.WriteEdits(
        [
            new SwShNestHoleRewardEdit(0, 0, SwShNestHoleRewardField.ItemId, 44),
        ]);

        Assert.Equal(source.Length, output.Length);
        Assert.Equal(0xDEADBEEFu, BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(unknownValueOffset, 4)));
        Assert.Equal(source[^3..], output[^3..]);
        for (var index = 0; index < source.Length; index++)
        {
            if (index >= sourceLayout.ItemValueOffset && index < sourceLayout.ItemValueOffset + sizeof(uint))
            {
                continue;
            }

            Assert.Equal(source[index], output[index]);
        }
    }

    [Fact]
    public void ParsedStarEditPatchesOnlyItsFourByteSourceScalar()
    {
        var source = CreateArchive().Write().Concat(new byte[] { 0xA5, 0x5A }).ToArray();
        var reward = ReadRewardLayout(source, tableIndex: 0, rewardIndex: 0);
        var valueOffset = reward.ValuesVectorOffset + sizeof(uint) + (3 * sizeof(uint));
        var parsed = SwShNestHoleRewardArchive.Parse(source);

        var output = parsed.WriteEdits(
        [
            new SwShNestHoleRewardEdit(0, 0, SwShNestHoleRewardField.Star4Value, 77),
        ]);

        Assert.Equal(source.Length, output.Length);
        for (var index = 0; index < source.Length; index++)
        {
            if (index >= valueOffset && index < valueOffset + sizeof(uint))
            {
                continue;
            }

            Assert.Equal(source[index], output[index]);
        }

        Assert.Equal(77u, BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(valueOffset, sizeof(uint))));
    }

    [Fact]
    public void EditIsolatesAliasedRewardTableIndex()
    {
        var archive = new SwShNestHoleRewardArchive(
        [
            CreateTable(0x1111),
            CreateTable(0x2222),
        ]);
        var source = archive.Write();
        var firstTable = ReadTableLayout(source, tableIndex: 0);
        var secondTable = ReadTableLayout(source, tableIndex: 1);
        WriteUOffset(source, secondTable.TableVectorElementOffset, firstTable.TableOffset);
        var parsed = SwShNestHoleRewardArchive.Parse(source);

        var output = parsed.WriteEdits(
        [
            new SwShNestHoleRewardEdit(0, 0, SwShNestHoleRewardField.ItemId, 55),
        ]);

        var reparsed = SwShNestHoleRewardArchive.Parse(output);
        Assert.Equal(55u, reparsed.Tables[0].Rewards[0].ItemId);
        Assert.Equal(3u, reparsed.Tables[1].Rewards[0].ItemId);
        Assert.Equal(0x1111UL, reparsed.Tables[1].TableId);
    }

    [Fact]
    public void EditIsolatesAliasedRewardRow()
    {
        var source = CreateArchive().Write();
        var firstReward = ReadRewardLayout(source, tableIndex: 0, rewardIndex: 0);
        var secondReward = ReadRewardLayout(source, tableIndex: 0, rewardIndex: 1);
        WriteUOffset(source, secondReward.RewardVectorElementOffset, firstReward.RewardOffset);
        var parsed = SwShNestHoleRewardArchive.Parse(source);

        var output = parsed.WriteEdits(
        [
            new SwShNestHoleRewardEdit(0, 0, SwShNestHoleRewardField.Star1Value, 88),
        ]);

        var reparsed = SwShNestHoleRewardArchive.Parse(output);
        Assert.Equal(88u, reparsed.Tables[0].Rewards[0].Values[0]);
        Assert.Equal(40u, reparsed.Tables[0].Rewards[1].Values[0]);
    }

    [Fact]
    public void EditIsolatesAliasedValuesVector()
    {
        var source = CreateArchive().Write();
        var sourceCopyOffset = AppendAlignedSourceCopy(source, out source);
        var firstReward = ReadRewardLayout(source, tableIndex: 0, rewardIndex: 0);
        var secondReward = ReadRewardLayout(source, tableIndex: 0, rewardIndex: 1);
        var sharedValuesOffset = checked(firstReward.ValuesVectorOffset + sourceCopyOffset);
        WriteUOffset(source, firstReward.ValuesFieldOffset, sharedValuesOffset);
        WriteUOffset(source, secondReward.ValuesFieldOffset, sharedValuesOffset);
        var parsed = SwShNestHoleRewardArchive.Parse(source);

        var output = parsed.WriteEdits(
        [
            new SwShNestHoleRewardEdit(0, 0, SwShNestHoleRewardField.Star2Value, 77),
        ]);

        var reparsed = SwShNestHoleRewardArchive.Parse(output);
        Assert.Equal(77u, reparsed.Tables[0].Rewards[0].Values[1]);
        Assert.Equal(30u, reparsed.Tables[0].Rewards[1].Values[1]);
    }

    [Fact]
    public void EditPreservesRewardValuesBeyondFiveStars()
    {
        var archive = new SwShNestHoleRewardArchive(
        [
            new SwShNestHoleRewardTable(
                1,
                [new SwShNestHoleReward(1, 2, [1, 2, 3, 4, 5, 600, 700])]),
        ]);
        var parsed = SwShNestHoleRewardArchive.Parse(archive.Write());

        var output = parsed.WriteEdits(
        [
            new SwShNestHoleRewardEdit(0, 0, SwShNestHoleRewardField.Star5Value, 50),
        ]);

        Assert.Equal(
            [1u, 2u, 3u, 4u, 50u, 600u, 700u],
            SwShNestHoleRewardArchive.Parse(output).Tables[0].Rewards[0].Values);
    }

    [Fact]
    public void ParseRejectsRewardWithFewerThanFiveValues()
    {
        var source = CreateArchive().Write();
        var reward = ReadRewardLayout(source, tableIndex: 0, rewardIndex: 0);
        BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(reward.ValuesVectorOffset, 4), 4);

        var error = Assert.Throws<InvalidDataException>(() => SwShNestHoleRewardArchive.Parse(source));

        Assert.Contains("at least 5", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteRejectsRewardWithFewerThanFiveValues()
    {
        var archive = new SwShNestHoleRewardArchive(
        [
            new SwShNestHoleRewardTable(
                1,
                [new SwShNestHoleReward(1, 2, [1, 2, 3, 4])]),
        ]);

        var error = Assert.Throws<InvalidDataException>(() => archive.Write());

        Assert.Contains("at least 5", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsPartiallyOverlappingValuesVectors()
    {
        var archive = new SwShNestHoleRewardArchive(
        [
            new SwShNestHoleRewardTable(
                1,
                [
                    new SwShNestHoleReward(1, 2, [5, 30, 20, 10, 5, 99]),
                    new SwShNestHoleReward(2, 3, [1, 2, 3, 4, 5]),
                ]),
        ]);
        var source = archive.Write();
        var sourceCopyOffset = AppendAlignedSourceCopy(source, out source);
        var firstReward = ReadRewardLayout(source, tableIndex: 0, rewardIndex: 0);
        var secondReward = ReadRewardLayout(source, tableIndex: 0, rewardIndex: 1);
        var copiedValuesOffset = checked(firstReward.ValuesVectorOffset + sourceCopyOffset);
        WriteUOffset(source, firstReward.ValuesFieldOffset, copiedValuesOffset);
        WriteUOffset(source, secondReward.ValuesFieldOffset, copiedValuesOffset + sizeof(uint));

        var error = Assert.Throws<InvalidDataException>(() => SwShNestHoleRewardArchive.Parse(source));

        Assert.Contains("overlap unsafely", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsMalformedOddLengthVtable()
    {
        var source = CreateArchive().Write();
        var reward = ReadRewardLayout(source, tableIndex: 0, rewardIndex: 0);
        var vtableOffset = GetVtableOffset(source, reward.RewardOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(vtableOffset, 2), 9);

        Assert.Throws<InvalidDataException>(() => SwShNestHoleRewardArchive.Parse(source));
    }

    [Fact]
    public void ParseRejectsOverlappingKnownRewardFields()
    {
        var source = CreateArchive().Write();
        var reward = ReadRewardLayout(source, tableIndex: 0, rewardIndex: 0);
        var vtableOffset = GetVtableOffset(source, reward.RewardOffset);
        var entryFieldOffset = ReadTableFieldOffset(source, reward.RewardOffset, fieldIndex: 0);
        BinaryPrimitives.WriteUInt16LittleEndian(
            source.AsSpan(vtableOffset + 4 + sizeof(ushort), sizeof(ushort)),
            checked((ushort)entryFieldOffset));

        var error = Assert.Throws<InvalidDataException>(() => SwShNestHoleRewardArchive.Parse(source));

        Assert.Contains("overlap within the table object", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsUnknownRewardFieldStartingInsideKnownScalar()
    {
        var source = AddUnknownRewardScalarField(CreateArchive().Write(), out _);
        var reward = ReadRewardLayout(source, tableIndex: 0, rewardIndex: 0);
        var vtableOffset = GetVtableOffset(source, reward.RewardOffset);
        var itemFieldOffset = ReadTableFieldOffset(source, reward.RewardOffset, fieldIndex: 1);
        BinaryPrimitives.WriteUInt16LittleEndian(
            source.AsSpan(vtableOffset + 4 + (3 * sizeof(ushort)), sizeof(ushort)),
            checked((ushort)(itemFieldOffset + 2)));

        var error = Assert.Throws<InvalidDataException>(() => SwShNestHoleRewardArchive.Parse(source));

        Assert.Contains("unknown field 3 aliases known field 1", error.Message, StringComparison.Ordinal);
    }

    private static SwShNestHoleRewardArchive CreateArchive()
    {
        return new SwShNestHoleRewardArchive([CreateTable(0xAABBCCDD00112233)]);
    }

    private static SwShNestHoleRewardTable CreateTable(ulong tableId)
    {
        return new SwShNestHoleRewardTable(
            tableId,
            [
                new SwShNestHoleReward(10, 3, [40, 30, 20, 10, 5]),
                new SwShNestHoleReward(11, 2, [5, 10, 15, 20, 25]),
            ]);
    }

    private static byte[] AddUnknownRewardScalarField(byte[] source, out int unknownValueOffset)
    {
        var original = ReadRewardLayout(source, tableIndex: 0, rewardIndex: 0);
        var output = source.ToList();
        while (((output.Count + 12) % sizeof(uint)) != 0)
        {
            output.Add(0);
        }

        AddUInt16(output, 12);
        AddUInt16(output, 20);
        AddUInt16(output, 8);
        AddUInt16(output, 12);
        AddUInt16(output, 4);
        AddUInt16(output, 16);
        var tableOffset = output.Count;
        AddInt32(output, 12);
        var valuesFieldOffset = output.Count;
        AddUInt32(output, 0);
        AddUInt32(output, 10);
        AddUInt32(output, 3);
        unknownValueOffset = output.Count;
        AddUInt32(output, 0xDEADBEEF);
        while ((output.Count % sizeof(uint)) != 0)
        {
            output.Add(0);
        }

        var valuesVectorOffset = output.Count;
        var valuesLength = sizeof(uint) + (original.ValueCount * sizeof(uint));
        output.AddRange(source.AsSpan(original.ValuesVectorOffset, valuesLength).ToArray());
        output.AddRange(new byte[] { 0xC1, 0xC2, 0xC3 });
        var bytes = output.ToArray();
        WriteUOffset(bytes, valuesFieldOffset, valuesVectorOffset);
        WriteUOffset(bytes, original.RewardVectorElementOffset, tableOffset);
        return bytes;
    }

    private static byte[] ReplaceFirstRewardWithOmittedItemTable(
        byte[] source,
        bool includeUnknownField,
        out int unknownValueOffset)
    {
        var original = ReadRewardLayout(source, tableIndex: 0, rewardIndex: 0);
        var output = source.ToList();
        var vtableLength = includeUnknownField ? 12 : 10;
        var objectLength = includeUnknownField ? 16 : 12;
        while (((output.Count + vtableLength) % sizeof(uint)) != 0)
        {
            output.Add(0);
        }

        // Matches the standard FlatBuffers layout when the middle uint ItemID field is omitted:
        // EntryID remains at +4, the values uoffset is at +8, and the compact object ends at +12.
        AddUInt16(output, checked((ushort)vtableLength));
        AddUInt16(output, checked((ushort)objectLength));
        AddUInt16(output, 4);
        AddUInt16(output, 0);
        AddUInt16(output, 8);
        if (includeUnknownField)
        {
            AddUInt16(output, 12);
        }

        var tableOffset = output.Count;
        AddInt32(output, vtableLength);
        AddUInt32(output, 10);
        var valuesFieldOffset = output.Count;
        AddUInt32(output, 0);
        unknownValueOffset = -1;
        if (includeUnknownField)
        {
            unknownValueOffset = output.Count;
            AddUInt32(output, 0xDEADBEEF);
        }

        var valuesVectorOffset = output.Count;
        var valuesLength = sizeof(uint) + (original.ValueCount * sizeof(uint));
        output.AddRange(source.AsSpan(original.ValuesVectorOffset, valuesLength).ToArray());
        output.AddRange(new byte[] { 0xC1, 0xC2, 0xC3 });
        var bytes = output.ToArray();
        WriteUOffset(bytes, valuesFieldOffset, valuesVectorOffset);
        WriteUOffset(bytes, original.RewardVectorElementOffset, tableOffset);
        return bytes;
    }

    private static int AppendAlignedSourceCopy(byte[] original, out byte[] output)
    {
        var bytes = original.ToList();
        while ((bytes.Count % sizeof(ulong)) != 0)
        {
            bytes.Add(0);
        }

        var offset = bytes.Count;
        bytes.AddRange(original);
        output = bytes.ToArray();
        return offset;
    }

    private static TableSourceLayout ReadTableLayout(byte[] data, int tableIndex)
    {
        var rootTableOffset = ReadUOffset(data, 0);
        var tableVectorOffset = ReadTableUOffset(data, rootTableOffset, fieldIndex: 0);
        var tableVectorElementOffset = tableVectorOffset + sizeof(uint) + (tableIndex * sizeof(uint));
        return new TableSourceLayout(
            tableVectorElementOffset,
            ReadUOffset(data, tableVectorElementOffset));
    }

    private static RewardSourceLayout ReadRewardLayout(byte[] data, int tableIndex, int rewardIndex)
    {
        var table = ReadTableLayout(data, tableIndex);
        var rewardsVectorOffset = ReadTableUOffset(data, table.TableOffset, fieldIndex: 1);
        var rewardVectorElementOffset = rewardsVectorOffset + sizeof(uint) + (rewardIndex * sizeof(uint));
        var rewardOffset = ReadUOffset(data, rewardVectorElementOffset);
        var itemFieldOffset = ReadTableFieldOffset(data, rewardOffset, fieldIndex: 1);
        var valuesFieldOffset = rewardOffset + ReadTableFieldOffset(data, rewardOffset, fieldIndex: 2);
        var valuesVectorOffset = ReadUOffset(data, valuesFieldOffset);
        var valueCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(valuesVectorOffset, 4)));
        return new RewardSourceLayout(
            rewardVectorElementOffset,
            rewardOffset,
            rewardOffset + itemFieldOffset,
            valuesFieldOffset,
            valuesVectorOffset,
            valueCount);
    }

    private static int ReadTableUOffset(byte[] data, int tableOffset, int fieldIndex)
    {
        return ReadUOffset(data, tableOffset + ReadTableFieldOffset(data, tableOffset, fieldIndex));
    }

    private static int ReadTableFieldOffset(byte[] data, int tableOffset, int fieldIndex)
    {
        var vtableOffset = GetVtableOffset(data, tableOffset);
        return BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(
            vtableOffset + 4 + (fieldIndex * sizeof(ushort)),
            sizeof(ushort)));
    }

    private static int GetVtableOffset(byte[] data, int tableOffset)
    {
        return tableOffset - BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(tableOffset, sizeof(int)));
    }

    private static int ReadUOffset(byte[] data, int sourceOffset)
    {
        return checked(sourceOffset + (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(sourceOffset, 4)));
    }

    private static void WriteUOffset(byte[] data, int sourceOffset, int targetOffset)
    {
        Assert.True(targetOffset > sourceOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(
            data.AsSpan(sourceOffset, sizeof(uint)),
            checked((uint)(targetOffset - sourceOffset)));
    }

    private static void AddUInt16(List<byte> output, ushort value)
    {
        var bytes = new byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        output.AddRange(bytes);
    }

    private static void AddInt32(List<byte> output, int value)
    {
        var bytes = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        output.AddRange(bytes);
    }

    private static void AddUInt32(List<byte> output, uint value)
    {
        var bytes = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        output.AddRange(bytes);
    }

    private sealed record TableSourceLayout(
        int TableVectorElementOffset,
        int TableOffset);

    private sealed record RewardSourceLayout(
        int RewardVectorElementOffset,
        int RewardOffset,
        int ItemValueOffset,
        int ValuesFieldOffset,
        int ValuesVectorOffset,
        int ValueCount);
}
