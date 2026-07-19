// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;

namespace KM.Formats.ZA.Generated.GameData;

public struct ZaPokedexContentsData : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;

    public static void ValidateVersion()
    {
        FlatBufferConstants.FLATBUFFERS_25_2_10();
    }

    public int ContentId => ReadInt(4);
    public int Species => ReadInt(6);
    public int Group => ReadInt(8);

    public void __init(int position, ByteBuffer buffer)
    {
        table = new Table(position, buffer);
    }

    public ZaPokedexContentsData __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    public static Offset<ZaPokedexContentsData> Create(
        FlatBufferBuilder builder,
        int contentId,
        int species,
        int group = 0)
    {
        builder.StartTable(3);
        builder.AddInt(2, group, 0);
        builder.AddInt(1, species, 0);
        builder.AddInt(0, contentId, 0);
        return new Offset<ZaPokedexContentsData>(builder.EndTable());
    }

    private int ReadInt(int fieldOffset)
    {
        var offset = table.__offset(fieldOffset);
        return offset == 0 ? 0 : table.bb.GetInt(offset + table.bb_pos);
    }
}

public struct ZaPokedexContentsDataArray : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;

    public static void ValidateVersion()
    {
        FlatBufferConstants.FLATBUFFERS_25_2_10();
    }

    public static ZaPokedexContentsDataArray GetRootAsZaPokedexContentsDataArray(ByteBuffer buffer)
    {
        return GetRootAsZaPokedexContentsDataArray(buffer, new ZaPokedexContentsDataArray());
    }

    public static ZaPokedexContentsDataArray GetRootAsZaPokedexContentsDataArray(
        ByteBuffer buffer,
        ZaPokedexContentsDataArray array)
    {
        return array.__assign(buffer.GetInt(buffer.Position) + buffer.Position, buffer);
    }

    public int ValuesLength
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

    public ZaPokedexContentsDataArray __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    public ZaPokedexContentsData? Values(int index)
    {
        var offset = table.__offset(4);
        return offset == 0
            ? null
            : new ZaPokedexContentsData().__assign(
                table.__indirect(table.__vector(offset) + index * sizeof(int)),
                table.bb);
    }

    public static Offset<ZaPokedexContentsDataArray> Create(
        FlatBufferBuilder builder,
        VectorOffset valuesOffset = default)
    {
        builder.StartTable(1);
        builder.AddOffset(0, valuesOffset.Value, 0);
        var tableOffset = builder.EndTable();
        builder.Required(tableOffset, 4);
        return new Offset<ZaPokedexContentsDataArray>(tableOffset);
    }

    public static VectorOffset CreateValuesVector(
        FlatBufferBuilder builder,
        Offset<ZaPokedexContentsData>[] values)
    {
        builder.StartVector(sizeof(int), values.Length, sizeof(int));
        for (var index = values.Length - 1; index >= 0; index--)
        {
            builder.AddOffset(values[index].Value);
        }

        return builder.EndVector();
    }

    public static void FinishBuffer(
        FlatBufferBuilder builder,
        Offset<ZaPokedexContentsDataArray> offset)
    {
        builder.Finish(offset.Value);
    }
}
