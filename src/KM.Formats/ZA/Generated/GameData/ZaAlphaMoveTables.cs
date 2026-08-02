// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;

namespace KM.Formats.ZA.Generated.GameData;

public struct ZaAlphaMoveForm : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;

    public ushort FormNo => ReadUshort(4);
    public ushort WazaNo => ReadUshort(6);
    public bool HasWazaNo => table.__offset(6) != 0;

    public int? WazaNoPosition
    {
        get
        {
            var offset = table.__offset(6);
            return offset == 0 ? null : offset + table.bb_pos;
        }
    }

    public void __init(int position, ByteBuffer buffer)
    {
        table = new Table(position, buffer);
    }

    public ZaAlphaMoveForm __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    public bool MutateWazaNo(ushort wazaNo)
    {
        var offset = table.__offset(6);
        if (offset == 0)
        {
            return false;
        }

        table.bb.PutUshort(offset + table.bb_pos, wazaNo);
        return true;
    }

    public static Offset<ZaAlphaMoveForm> Create(
        FlatBufferBuilder builder,
        ushort formNo = 0,
        ushort wazaNo = 0)
    {
        builder.StartTable(2);
        builder.AddUshort(1, wazaNo, 0);
        builder.AddUshort(0, formNo, 0);
        return new Offset<ZaAlphaMoveForm>(builder.EndTable());
    }

    private ushort ReadUshort(int vtableOffset)
    {
        var offset = table.__offset(vtableOffset);
        return offset == 0 ? (ushort)0 : table.bb.GetUshort(offset + table.bb_pos);
    }
}

public struct ZaAlphaMoveSpecies : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;

    public ushort DevNo
    {
        get
        {
            var offset = table.__offset(4);
            return offset == 0 ? (ushort)0 : table.bb.GetUshort(offset + table.bb_pos);
        }
    }

    public int FormTableListLength
    {
        get
        {
            var offset = table.__offset(6);
            return offset == 0 ? 0 : table.__vector_len(offset);
        }
    }

    public void __init(int position, ByteBuffer buffer)
    {
        table = new Table(position, buffer);
    }

    public ZaAlphaMoveSpecies __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    public ZaAlphaMoveForm? FormTableList(int index)
    {
        var offset = table.__offset(6);
        return offset == 0
            ? null
            : new ZaAlphaMoveForm().__assign(
                table.__indirect(table.__vector(offset) + index * sizeof(int)),
                table.bb);
    }

    public static Offset<ZaAlphaMoveSpecies> Create(
        FlatBufferBuilder builder,
        ushort devNo = 0,
        VectorOffset formTableListOffset = default)
    {
        builder.StartTable(2);
        builder.AddOffset(1, formTableListOffset.Value, 0);
        builder.AddUshort(0, devNo, 0);
        var tableOffset = builder.EndTable();
        builder.Required(tableOffset, 6);
        return new Offset<ZaAlphaMoveSpecies>(tableOffset);
    }

    public static VectorOffset CreateFormTableListVector(
        FlatBufferBuilder builder,
        Offset<ZaAlphaMoveForm>[] data)
    {
        builder.StartVector(sizeof(int), data.Length, sizeof(int));
        for (var index = data.Length - 1; index >= 0; index--)
        {
            builder.AddOffset(data[index].Value);
        }

        return builder.EndVector();
    }
}

public struct ZaAlphaMoveTable : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;

    public static void ValidateVersion()
    {
        FlatBufferConstants.FLATBUFFERS_25_2_10();
    }

    public static ZaAlphaMoveTable GetRootAsZaAlphaMoveTable(ByteBuffer buffer)
    {
        return GetRootAsZaAlphaMoveTable(buffer, new ZaAlphaMoveTable());
    }

    public static ZaAlphaMoveTable GetRootAsZaAlphaMoveTable(ByteBuffer buffer, ZaAlphaMoveTable table)
    {
        return table.__assign(buffer.GetInt(buffer.Position) + buffer.Position, buffer);
    }

    public int RootLength
    {
        get
        {
            var offset = table.__offset(4);
            return offset == 0 ? 0 : table.__vector_len(offset);
        }
    }

    public void __init(int position, ByteBuffer buffer)
    {
        table = new Table(position, buffer);
    }

    public ZaAlphaMoveTable __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    public ZaAlphaMoveSpecies? Root(int index)
    {
        var offset = table.__offset(4);
        return offset == 0
            ? null
            : new ZaAlphaMoveSpecies().__assign(
                table.__indirect(table.__vector(offset) + index * sizeof(int)),
                table.bb);
    }

    public static Offset<ZaAlphaMoveTable> Create(
        FlatBufferBuilder builder,
        VectorOffset rootOffset = default)
    {
        builder.StartTable(1);
        builder.AddOffset(0, rootOffset.Value, 0);
        var tableOffset = builder.EndTable();
        builder.Required(tableOffset, 4);
        return new Offset<ZaAlphaMoveTable>(tableOffset);
    }

    public static VectorOffset CreateRootVector(
        FlatBufferBuilder builder,
        Offset<ZaAlphaMoveSpecies>[] data)
    {
        builder.StartVector(sizeof(int), data.Length, sizeof(int));
        for (var index = data.Length - 1; index >= 0; index--)
        {
            builder.AddOffset(data[index].Value);
        }

        return builder.EndVector();
    }

    public static void FinishBuffer(FlatBufferBuilder builder, Offset<ZaAlphaMoveTable> offset)
    {
        builder.Finish(offset.Value);
    }
}
