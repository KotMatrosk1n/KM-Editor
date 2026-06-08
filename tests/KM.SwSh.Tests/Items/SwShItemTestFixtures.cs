// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using KM.Formats.SwSh;

namespace KM.SwSh.Tests.Items;

internal static class SwShItemTestFixtures
{
    private const int HeaderSize = 0x44;
    private const int RowSize = 0x30;
    private const int EntryTableOffset = 0x44;
    private const int RowsStartOffset = 0x40;

    public static byte[] CreateItemTable(params ItemFixtureRecord[] records)
    {
        var itemCount = records.Length;
        var maxRowIndex = records.Length == 0
            ? 0
            : records.Max(record => record.RawRowIndex) + 1;
        var rowsStart = HeaderSize + (itemCount * sizeof(ushort));
        var data = new byte[rowsStart + (maxRowIndex * RowSize)];

        BinaryPrimitives.WriteUInt16LittleEndian(data, checked((ushort)itemCount));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x02), 0);
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
            data[rowOffset + 0x14] = record.FieldFlags;
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

        return data;
    }

    public static byte[] CreateItemNames(params string[] names)
    {
        return SwShGameTextFile.Write(names.Select(name => new SwShGameTextLine(name, Flags: 0)).ToArray());
    }
}

internal sealed record ItemFixtureRecord(
    int ItemId,
    int RawRowIndex,
    int BuyPrice,
    int WattsPrice,
    int AlternatePrice,
    SwShItemPouch Pouch,
    byte PouchFlags = 0,
    byte FlingPower = 0,
    byte FieldUseType = 0,
    byte FieldFlags = 0,
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
