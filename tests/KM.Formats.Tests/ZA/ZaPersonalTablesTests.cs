// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using Google.FlatBuffers;
using KM.Formats.ZA.Generated.GameData;
using Xunit;

namespace KM.Formats.Tests.ZA;

public sealed class ZaPersonalTablesTests
{
    [Fact]
    public void DexOrderAndSpeciesMetadataUseTheirFullGameWidths()
    {
        var builder = new FlatBufferBuilder(256);
        ZaPersonal.Start(builder);
        ZaPersonal.AddType1(builder, 11);
        ZaPersonal.AddZADexOrder(builder, 400);
        ZaPersonal.AddIsPresent(builder, true);
        ZaPersonal.AddSpecies(
            builder,
            ZaSpeciesInfo.Create(
                builder,
                species: 1,
                form: 0,
                model: 1,
                color: 3,
                bodyType: 1,
                height: 7,
                weight: 69,
                reserved: 0,
                reserved1: 0,
                reserved2: 0,
                reserved3: 8));
        var row = ZaPersonal.End(builder);
        var entries = ZaPersonalTable.CreateEntryVector(builder, [row]);
        ZaPersonalTable.Start(builder);
        ZaPersonalTable.AddEntry(builder, entries);
        var root = ZaPersonalTable.End(builder);
        ZaPersonalTable.FinishBuffer(builder, root);
        var bytes = builder.SizedByteArray();

        var table = ZaPersonalTable.GetRootAsZaPersonalTable(new ByteBuffer(bytes));
        var entry = table.Entry(0)!.Value;
        Assert.Equal(400, entry.ZADexOrder);
        Assert.Equal(8u, entry.Species!.Value.Reserved3);
        Assert.False(table.HasLegacyByteZADexOrderLayout);

        var tableOffset = ReadEntryTableOffset(bytes, entryIndex: 0);
        var speciesLocation = ReadTableFieldLocation(bytes, tableOffset, fieldIndex: 0);
        var presenceLocation = ReadTableFieldLocation(bytes, tableOffset, fieldIndex: 1);
        var dexLocation = ReadTableFieldLocation(bytes, tableOffset, fieldIndex: 2);
        var type1Location = ReadTableFieldLocation(bytes, tableOffset, fieldIndex: 3);
        Assert.True(presenceLocation - speciesLocation >= 20);
        Assert.Equal(8u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(speciesLocation + 16, sizeof(uint))));
        Assert.Equal(400, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(dexLocation, sizeof(ushort))));
        Assert.NotEqual(dexLocation + sizeof(byte), type1Location);
    }

    [Fact]
    public void LegacyByteDexOrderIsDetectedWhenType1IsAbsent()
    {
        var builder = new FlatBufferBuilder(128)
        {
            ForceDefaults = true,
        };
        ZaPersonal.Start(builder);
        ZaPersonal.AddAbility1(builder, 65);
        builder.AddByte(2, 173, 0);
        var row = ZaPersonal.End(builder);
        var entries = ZaPersonalTable.CreateEntryVector(builder, [row]);
        ZaPersonalTable.Start(builder);
        ZaPersonalTable.AddEntry(builder, entries);
        var root = ZaPersonalTable.End(builder);
        ZaPersonalTable.FinishBuffer(builder, root);

        var table = ZaPersonalTable.GetRootAsZaPersonalTable(new ByteBuffer(builder.SizedByteArray()));
        var entry = table.Entry(0)!.Value;
        Assert.False(entry.HasType1);
        Assert.False(entry.HasType2);
        Assert.True(entry.HasLegacyByteZADexOrderLayout);
        Assert.True(table.HasLegacyByteZADexOrderLayout);
        Assert.Equal(173, entry.ZADexOrderLowByte);
    }

    private static int ReadEntryTableOffset(byte[] data, int entryIndex)
    {
        var rootOffset = BinaryPrimitives.ReadInt32LittleEndian(data);
        var vectorField = ReadTableFieldLocation(data, rootOffset, fieldIndex: 0);
        var vectorOffset = vectorField
            + checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(vectorField, sizeof(uint))));
        var entryOffsetLocation = vectorOffset + sizeof(int) + entryIndex * sizeof(int);
        return entryOffsetLocation
            + checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                data.AsSpan(entryOffsetLocation, sizeof(uint))));
    }

    private static int ReadTableFieldLocation(byte[] data, int tableOffset, int fieldIndex)
    {
        var vtableOffset = tableOffset
            - BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(tableOffset, sizeof(int)));
        var fieldOffset = BinaryPrimitives.ReadUInt16LittleEndian(
            data.AsSpan(vtableOffset + sizeof(ushort) * (2 + fieldIndex), sizeof(ushort)));
        Assert.NotEqual(0, fieldOffset);
        return tableOffset + fieldOffset;
    }
}
