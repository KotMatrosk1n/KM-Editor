// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;

namespace KM.Formats.ZA.Generated.FashionCatalog;

public struct ZaDressUpCatalogEntry : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;
    public uint ItemId => ReadUInt(4);
    public bool HasItemId => Has(4);
    public string? ModelPart => ReadString(6);
    public bool HasModelPart => Has(6);
    public uint CatalogGroupCode => ReadUInt(8);
    public bool HasCatalogGroupCode => Has(8);
    public string? ModelVariant => ReadString(10);
    public bool HasModelVariant => Has(10);
    public uint CategoryCode => ReadUInt(12);
    public bool HasCategoryCode => Has(12);
    public uint ColorVariantCode => ReadUInt(14);
    public bool HasColorVariantCode => Has(14);
    public string? PrimaryColorLabel => ReadString(16);
    public bool HasPrimaryColorLabel => Has(16);
    public string? SecondaryColorLabel => ReadString(18);
    public bool HasSecondaryColorLabel => Has(18);
    public bool ReservedFlagA => ReadBool(20);
    public bool HasReservedFlagA => Has(20);
    public uint Price => ReadUInt(22);
    public bool HasPrice => Has(22);
    public uint UiIndex => ReadUInt(24);
    public bool HasUiIndex => Has(24);
    public string? FootwearSubtype => ReadString(26);
    public bool HasFootwearSubtype => Has(26);
    public bool ReservedFlagB => ReadBool(28);
    public bool HasReservedFlagB => Has(28);
    public int TablePosition => table.bb_pos;

    public void __init(int position, ByteBuffer buffer) => table = new Table(position, buffer);

    public ZaDressUpCatalogEntry __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    private bool Has(int slot) => table.__offset(slot) != 0;

    private uint ReadUInt(int slot)
    {
        var offset = table.__offset(slot);
        return offset == 0 ? 0 : table.bb.GetUint(offset + table.bb_pos);
    }

    private bool ReadBool(int slot)
    {
        var offset = table.__offset(slot);
        return offset != 0 && table.bb.Get(offset + table.bb_pos) != 0;
    }

    private string? ReadString(int slot)
    {
        var offset = table.__offset(slot);
        return offset == 0 ? null : table.__string(offset + table.bb_pos);
    }
}

public struct ZaDressUpCatalogArray : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;
    public int EntriesLength => VectorLength(4);
    public bool HasEntries => table.__offset(4) != 0;
    public int TablePosition => table.bb_pos;

    public static ZaDressUpCatalogArray GetRootAsZaDressUpCatalogArray(ByteBuffer buffer) =>
        new ZaDressUpCatalogArray().__assign(buffer.GetInt(buffer.Position) + buffer.Position, buffer);

    public ZaDressUpCatalogEntry? Entries(int index)
    {
        var offset = table.__offset(4);
        return offset == 0
            ? null
            : new ZaDressUpCatalogEntry().__assign(
                table.__indirect(table.__vector(offset) + index * sizeof(int)),
                table.bb);
    }

    public void __init(int position, ByteBuffer buffer) => table = new Table(position, buffer);

    public ZaDressUpCatalogArray __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    public static void FinishBuffer(FlatBufferBuilder builder, Offset<ZaDressUpCatalogArray> offset) =>
        builder.Finish(offset.Value);

    private int VectorLength(int slot)
    {
        var offset = table.__offset(slot);
        return offset == 0 ? 0 : table.__vector_len(offset);
    }
}

public struct ZaDressUpGroupCatalogEntry : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;
    public string? ModelPart => ReadString(4);
    public bool HasModelPart => Has(4);
    public uint DisplayOrder => ReadUInt(6);
    public bool HasDisplayOrder => Has(6);
    public string? DisplayLabel => ReadString(8);
    public bool HasDisplayLabel => Has(8);
    public int TablePosition => table.bb_pos;

    public void __init(int position, ByteBuffer buffer) => table = new Table(position, buffer);

    public ZaDressUpGroupCatalogEntry __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    private bool Has(int slot) => table.__offset(slot) != 0;

    private uint ReadUInt(int slot)
    {
        var offset = table.__offset(slot);
        return offset == 0 ? 0 : table.bb.GetUint(offset + table.bb_pos);
    }

    private string? ReadString(int slot)
    {
        var offset = table.__offset(slot);
        return offset == 0 ? null : table.__string(offset + table.bb_pos);
    }
}

public struct ZaDressUpGroupCatalogArray : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;
    public int EntriesLength => VectorLength(4);
    public bool HasEntries => table.__offset(4) != 0;
    public int TablePosition => table.bb_pos;

    public static ZaDressUpGroupCatalogArray GetRootAsZaDressUpGroupCatalogArray(ByteBuffer buffer) =>
        new ZaDressUpGroupCatalogArray().__assign(buffer.GetInt(buffer.Position) + buffer.Position, buffer);

    public ZaDressUpGroupCatalogEntry? Entries(int index)
    {
        var offset = table.__offset(4);
        return offset == 0
            ? null
            : new ZaDressUpGroupCatalogEntry().__assign(
                table.__indirect(table.__vector(offset) + index * sizeof(int)),
                table.bb);
    }

    public void __init(int position, ByteBuffer buffer) => table = new Table(position, buffer);

    public ZaDressUpGroupCatalogArray __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    public static void FinishBuffer(FlatBufferBuilder builder, Offset<ZaDressUpGroupCatalogArray> offset) =>
        builder.Finish(offset.Value);

    private int VectorLength(int slot)
    {
        var offset = table.__offset(slot);
        return offset == 0 ? 0 : table.__vector_len(offset);
    }
}

public struct ZaHairAndMakeupCatalogEntry : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;
    public uint ItemId => ReadUInt(4);
    public bool HasItemId => Has(4);
    public string? ModelKey => ReadString(6);
    public bool HasModelKey => Has(6);
    public uint CatalogTypeCode => ReadUInt(8);
    public bool HasCatalogTypeCode => Has(8);
    public bool ReservedFlag => ReadBool(10);
    public bool HasReservedFlag => Has(10);
    public string? ColorValue => ReadString(12);
    public bool HasColorValue => Has(12);
    public string? LabelKey => ReadString(14);
    public bool HasLabelKey => Has(14);
    public uint DisplayOrder => ReadUInt(16);
    public bool HasDisplayOrder => Has(16);
    public int GroupCode => ReadInt(18);
    public bool HasGroupCode => Has(18);
    public int VariantCode => ReadInt(20);
    public bool HasVariantCode => Has(20);
    public int TablePosition => table.bb_pos;

    public void __init(int position, ByteBuffer buffer) => table = new Table(position, buffer);

    public ZaHairAndMakeupCatalogEntry __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    private bool Has(int slot) => table.__offset(slot) != 0;

    private uint ReadUInt(int slot)
    {
        var offset = table.__offset(slot);
        return offset == 0 ? 0 : table.bb.GetUint(offset + table.bb_pos);
    }

    private int ReadInt(int slot)
    {
        var offset = table.__offset(slot);
        return offset == 0 ? 0 : table.bb.GetInt(offset + table.bb_pos);
    }

    private bool ReadBool(int slot)
    {
        var offset = table.__offset(slot);
        return offset != 0 && table.bb.Get(offset + table.bb_pos) != 0;
    }

    private string? ReadString(int slot)
    {
        var offset = table.__offset(slot);
        return offset == 0 ? null : table.__string(offset + table.bb_pos);
    }
}

public struct ZaHairAndMakeupCatalogArray : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;
    public int EntriesLength => VectorLength(4);
    public bool HasEntries => table.__offset(4) != 0;
    public int TablePosition => table.bb_pos;

    public static ZaHairAndMakeupCatalogArray GetRootAsZaHairAndMakeupCatalogArray(ByteBuffer buffer) =>
        new ZaHairAndMakeupCatalogArray().__assign(buffer.GetInt(buffer.Position) + buffer.Position, buffer);

    public ZaHairAndMakeupCatalogEntry? Entries(int index)
    {
        var offset = table.__offset(4);
        return offset == 0
            ? null
            : new ZaHairAndMakeupCatalogEntry().__assign(
                table.__indirect(table.__vector(offset) + index * sizeof(int)),
                table.bb);
    }

    public void __init(int position, ByteBuffer buffer) => table = new Table(position, buffer);

    public ZaHairAndMakeupCatalogArray __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    public static void FinishBuffer(FlatBufferBuilder builder, Offset<ZaHairAndMakeupCatalogArray> offset) =>
        builder.Finish(offset.Value);

    private int VectorLength(int slot)
    {
        var offset = table.__offset(slot);
        return offset == 0 ? 0 : table.__vector_len(offset);
    }
}
