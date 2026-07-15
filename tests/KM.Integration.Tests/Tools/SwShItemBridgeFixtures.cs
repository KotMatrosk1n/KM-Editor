// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using KM.Formats.SwSh;

namespace KM.Integration.Tests.Tools;

internal static class SwShItemBridgeFixtures
{
    private const int HeaderSize = 0x44;
    private const int RowSize = 0x30;
    private const int EntryTableOffset = 0x44;
    private const int RowsStartOffset = 0x40;

    public static byte[] CreateItemTable(params ItemBridgeFixtureRecord[] records)
    {
        return CreateItemTable(machineMovesBySlot: null, records);
    }

    public static byte[] CreateItemTableWithMachineMoves(
        IReadOnlyDictionary<int, int> machineMovesBySlot,
        params ItemBridgeFixtureRecord[] records)
    {
        return CreateItemTable(machineMovesBySlot, records);
    }

    private static byte[] CreateItemTable(
        IReadOnlyDictionary<int, int>? machineMovesBySlot,
        params ItemBridgeFixtureRecord[] records)
    {
        var itemCount = records.Length;
        var maxRowIndex = records.Length == 0
            ? 0
            : records.Max(record => record.RawRowIndex) + 1;
        var rowsStart = HeaderSize + (itemCount * sizeof(ushort));
        var machineTableOffset = rowsStart + (maxRowIndex * RowSize);
        var dataLength = machineMovesBySlot is null
            ? machineTableOffset
            : machineTableOffset + (200 * sizeof(uint));
        var data = new byte[dataLength];

        BinaryPrimitives.WriteUInt16LittleEndian(data, checked((ushort)itemCount));
        BinaryPrimitives.WriteUInt16LittleEndian(
            data.AsSpan(0x02),
            machineMovesBySlot is null
                ? (ushort)0
                : checked((ushort)((machineTableOffset - HeaderSize) / 2)));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x04), checked((ushort)maxRowIndex));
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(RowsStartOffset), rowsStart);

        foreach (var record in records)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                data.AsSpan(EntryTableOffset + (record.ItemId * sizeof(ushort))),
                checked((ushort)record.RawRowIndex));
            var rowOffset = rowsStart + (record.RawRowIndex * RowSize);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(rowOffset), checked((uint)record.BuyPrice));
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(rowOffset + 0x04), checked((uint)record.WattsPrice));
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(rowOffset + 0x08), checked((uint)record.AlternatePrice));
            data[rowOffset + 0x11] = (byte)(((record.PouchFlags & 0x0F) << 4) | ((byte)record.Pouch & 0x0F));
            data[rowOffset + 0x12] = record.FlingPower;
            data[rowOffset + 0x13] = record.FieldUseType;
            data[rowOffset + 0x14] = record.BattlePouch;
            data[rowOffset + 0x15] = (byte)(record.CanUseOnPokemon ? 1 : 0);
            data[rowOffset + 0x16] = record.ItemType;
            data[rowOffset + 0x18] = record.SortIndex;
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(rowOffset + 0x1A), record.ItemSprite);
            data[rowOffset + 0x1C] = record.GroupType;
            data[rowOffset + 0x1D] = record.GroupIndex;
            data[rowOffset + 0x1E] = record.CureStatusFlags;
            data[rowOffset + 0x1F] = record.Boost0;
            data[rowOffset + 0x20] = record.Boost1;
            data[rowOffset + 0x21] = record.Boost2;
            data[rowOffset + 0x22] = record.Boost3;
            data[rowOffset + 0x23] = record.UseFlags1;
            data[rowOffset + 0x24] = record.UseFlags2;
            data[rowOffset + 0x25] = unchecked((byte)record.EvHp);
            data[rowOffset + 0x26] = unchecked((byte)record.EvAttack);
            data[rowOffset + 0x27] = unchecked((byte)record.EvDefense);
            data[rowOffset + 0x28] = unchecked((byte)record.EvSpeed);
            data[rowOffset + 0x29] = unchecked((byte)record.EvSpecialAttack);
            data[rowOffset + 0x2A] = unchecked((byte)record.EvSpecialDefense);
            data[rowOffset + 0x2B] = record.HealAmount;
            data[rowOffset + 0x2C] = record.PpGain;
            data[rowOffset + 0x2D] = unchecked((byte)record.FriendshipGain1);
            data[rowOffset + 0x2E] = unchecked((byte)record.FriendshipGain2);
            data[rowOffset + 0x2F] = unchecked((byte)record.FriendshipGain3);
        }

        if (machineMovesBySlot is not null)
        {
            foreach (var (slot, moveId) in machineMovesBySlot)
            {
                var ownerItemId = records
                    .FirstOrDefault(record =>
                        record.GroupType == 4
                        && record.FieldUseType == 2
                        && record.GroupIndex == slot)
                    ?.ItemId ?? 0;
                BinaryPrimitives.WriteUInt16LittleEndian(
                    data.AsSpan(machineTableOffset + (slot * sizeof(uint))),
                    checked((ushort)ownerItemId));
                BinaryPrimitives.WriteUInt16LittleEndian(
                    data.AsSpan(machineTableOffset + (slot * sizeof(uint)) + 2),
                    checked((ushort)moveId));
            }
        }

        return data;
    }

    public static byte[] CreateItemNames(params string[] names)
    {
        return SwShGameTextFile.Write(names.Select(name => new SwShGameTextLine(name, Flags: 0)).ToArray());
    }

    public static void WriteBaseItems(TemporaryBridgeProject temp)
    {
        temp.WriteBaseRomFsFile(
            "bin/pml/item/item.dat",
            CreateItemTable(
                new ItemBridgeFixtureRecord(0, 0, 0, 0, 0, SwShItemPouch.Items),
                new ItemBridgeFixtureRecord(
                    1,
                    1,
                    300,
                    15,
                    3,
                    SwShItemPouch.Medicine,
                    FlingPower: 30,
                    FieldUseType: 1,
                    BattlePouch: 2,
                    CanUseOnPokemon: true,
                    ItemType: 9,
                    SortIndex: 5,
                    ItemSprite: 12,
                    UseFlags1: 4,
                    HealAmount: 20,
                    FriendshipGain1: 1,
                    FriendshipGain2: 1),
                new ItemBridgeFixtureRecord(2, 2, 200, 10, 5, SwShItemPouch.Medicine)));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname.dat",
            CreateItemNames("None", "Potion", "Antidote"));
    }

    public static void WriteBaseMachineItems(TemporaryBridgeProject temp)
    {
        temp.WriteBaseRomFsFile(
            "bin/pml/item/item.dat",
            CreateItemTableWithMachineMoves(
                new Dictionary<int, int> { [10] = 345 },
                new ItemBridgeFixtureRecord(0, 0, 0, 0, 0, SwShItemPouch.Items),
                new ItemBridgeFixtureRecord(
                    1,
                    1,
                    0,
                    0,
                    0,
                    SwShItemPouch.TMs,
                    FieldUseType: 2,
                    GroupType: 4,
                    GroupIndex: 10)));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname.dat",
            CreateItemNames("None", "TM10 Magical Leaf"));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/wazaname.dat",
            CreateIndexedText(346, (85, "Thunderbolt"), (345, "Magical Leaf")));
    }

    private static byte[] CreateIndexedText(int count, params (int Index, string Text)[] entries)
    {
        var values = Enumerable.Repeat(string.Empty, count).ToArray();
        foreach (var (index, text) in entries)
        {
            values[index] = text;
        }

        return CreateItemNames(values);
    }
}

internal sealed record ItemBridgeFixtureRecord(
    int ItemId,
    int RawRowIndex,
    int BuyPrice,
    int WattsPrice,
    int AlternatePrice,
    SwShItemPouch Pouch,
    byte PouchFlags = 0,
    byte FlingPower = 0,
    byte FieldUseType = 0,
    byte BattlePouch = 0,
    bool CanUseOnPokemon = false,
    byte ItemType = 0,
    byte SortIndex = 0,
    short ItemSprite = 0,
    byte GroupType = 0,
    byte GroupIndex = 0,
    byte CureStatusFlags = 0,
    byte Boost0 = 0,
    byte Boost1 = 0,
    byte Boost2 = 0,
    byte Boost3 = 0,
    byte UseFlags1 = 0,
    byte UseFlags2 = 0,
    sbyte EvHp = 0,
    sbyte EvAttack = 0,
    sbyte EvDefense = 0,
    sbyte EvSpeed = 0,
    sbyte EvSpecialAttack = 0,
    sbyte EvSpecialDefense = 0,
    byte HealAmount = 0,
    byte PpGain = 0,
    sbyte FriendshipGain1 = 0,
    sbyte FriendshipGain2 = 0,
    sbyte FriendshipGain3 = 0);
