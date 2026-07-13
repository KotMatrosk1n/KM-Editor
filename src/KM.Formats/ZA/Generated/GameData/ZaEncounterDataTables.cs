// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;

namespace KM.Formats.ZA.Generated.GameData;

public struct ZaEncounterItemDropInfo : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;

    public string? ItemTableId
    {
        get
        {
            var offset = table.__offset(4);
            return offset == 0 ? null : table.__string(offset + table.bb_pos);
        }
    }

    public int DropConditionListLength
    {
        get
        {
            var offset = table.__offset(6);
            return offset == 0 ? 0 : table.__vector_len(offset);
        }
    }

    public uint DropProbability => ReadUint(8);
    public uint MinCount => ReadUint(10);
    public uint MaxCount => ReadUint(12);

    public void __init(int position, ByteBuffer buffer)
    {
        table = new Table(position, buffer);
    }

    public ZaEncounterItemDropInfo __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    public int DropConditionList(int index)
    {
        var offset = table.__offset(6);
        return offset == 0 ? 0 : table.bb.GetInt(table.__vector(offset) + index * 4);
    }

    public static Offset<ZaEncounterItemDropInfo> Create(
        FlatBufferBuilder builder,
        StringOffset itemTableIdOffset = default,
        VectorOffset dropConditionListOffset = default,
        uint dropProbability = 0,
        uint minCount = 0,
        uint maxCount = 0)
    {
        builder.StartTable(5);
        builder.AddUint(4, maxCount, 0);
        builder.AddUint(3, minCount, 0);
        builder.AddUint(2, dropProbability, 0);
        builder.AddOffset(1, dropConditionListOffset.Value, 0);
        builder.AddOffset(0, itemTableIdOffset.Value, 0);
        return new Offset<ZaEncounterItemDropInfo>(builder.EndTable());
    }

    public static VectorOffset CreateDropConditionListVector(FlatBufferBuilder builder, int[] data)
    {
        builder.StartVector(4, data.Length, 4);
        for (var index = data.Length - 1; index >= 0; index--)
        {
            builder.AddInt(data[index]);
        }

        return builder.EndVector();
    }

    private uint ReadUint(int vtableOffset)
    {
        var offset = table.__offset(vtableOffset);
        return offset == 0 ? 0 : table.bb.GetUint(offset + table.bb_pos);
    }
}

public struct ZaEncounterDataRow : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;

    public string? Id => ReadString(4);
    public int DevNo => ReadInt(6);
    public int MinLevel => ReadInt(8);
    public int MaxLevel => ReadInt(10);
    public int Sex => ReadInt(12);
    public int FormNo => ReadInt(14);
    public int Rare => ReadInt(16);
    public int Tokusei => ReadInt(18);
    public int Seikaku => ReadInt(20);
    public int TalentScale => ReadInt(22);
    public int TalentVNum => ReadInt(24);
    public float OyabunProbability => ReadFloat(26);
    public int OyabunAdditionalLevel => ReadInt(28);

    public int ActivationConditionLength
    {
        get
        {
            var offset = table.__offset(30);
            return offset == 0 ? 0 : table.__vector_len(offset);
        }
    }

    public ZaPokemonDataTalentValue? TalentValue => ReadStats(32);
    public ZaPokemonDataTalentValue? StrengthenValue => ReadStats(34);

    public ZaPokemonDataWazaList? WazaList
    {
        get
        {
            var offset = table.__offset(36);
            return offset == 0
                ? null
                : new ZaPokemonDataWazaList().__assign(table.__indirect(offset + table.bb_pos), table.bb);
        }
    }

    public ZaPokemonDataHoldItem? HoldItem
    {
        get
        {
            var offset = table.__offset(38);
            return offset == 0
                ? null
                : new ZaPokemonDataHoldItem().__assign(table.__indirect(offset + table.bb_pos), table.bb);
        }
    }

    public int ItemDropInfoListLength
    {
        get
        {
            var offset = table.__offset(40);
            return offset == 0 ? 0 : table.__vector_len(offset);
        }
    }

    public void __init(int position, ByteBuffer buffer)
    {
        table = new Table(position, buffer);
    }

    public ZaEncounterDataRow __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    public ZaPokemonDataActivationCondition? ActivationCondition(int index)
    {
        var offset = table.__offset(30);
        return offset == 0
            ? null
            : new ZaPokemonDataActivationCondition().__assign(
                table.__indirect(table.__vector(offset) + index * 4),
                table.bb);
    }

    public ZaEncounterItemDropInfo? ItemDropInfoList(int index)
    {
        var offset = table.__offset(40);
        return offset == 0
            ? null
            : new ZaEncounterItemDropInfo().__assign(
                table.__indirect(table.__vector(offset) + index * 4),
                table.bb);
    }

    public static Offset<ZaEncounterDataRow> Create(
        FlatBufferBuilder builder,
        StringOffset idOffset = default,
        int devNo = 0,
        int minLevel = 0,
        int maxLevel = 0,
        int sex = 0,
        int formNo = 0,
        int rare = 0,
        int tokusei = 0,
        int seikaku = 0,
        int talentScale = 0,
        int talentVNum = 0,
        float oyabunProbability = 0,
        int oyabunAdditionalLevel = 0,
        VectorOffset activationConditionOffset = default,
        Offset<ZaPokemonDataTalentValue> talentValueOffset = default,
        Offset<ZaPokemonDataTalentValue> strengthenValueOffset = default,
        Offset<ZaPokemonDataWazaList> wazaListOffset = default,
        Offset<ZaPokemonDataHoldItem> holdItemOffset = default,
        VectorOffset itemDropInfoListOffset = default)
    {
        builder.StartTable(19);
        builder.AddOffset(18, itemDropInfoListOffset.Value, 0);
        builder.AddOffset(17, holdItemOffset.Value, 0);
        builder.AddOffset(16, wazaListOffset.Value, 0);
        builder.AddOffset(15, strengthenValueOffset.Value, 0);
        builder.AddOffset(14, talentValueOffset.Value, 0);
        builder.AddOffset(13, activationConditionOffset.Value, 0);
        builder.AddInt(12, oyabunAdditionalLevel, 0);
        builder.AddFloat(11, oyabunProbability, 0);
        builder.AddInt(10, talentVNum, 0);
        builder.AddInt(9, talentScale, 0);
        builder.AddInt(8, seikaku, 0);
        builder.AddInt(7, tokusei, 0);
        builder.AddInt(6, rare, 0);
        builder.AddInt(5, formNo, 0);
        builder.AddInt(4, sex, 0);
        builder.AddInt(3, maxLevel, 0);
        builder.AddInt(2, minLevel, 0);
        builder.AddInt(1, devNo, 0);
        builder.AddOffset(0, idOffset.Value, 0);
        return new Offset<ZaEncounterDataRow>(builder.EndTable());
    }

    public static VectorOffset CreateActivationConditionVector(
        FlatBufferBuilder builder,
        Offset<ZaPokemonDataActivationCondition>[] data)
    {
        return CreateOffsetVector(builder, data.Select(offset => offset.Value).ToArray());
    }

    public static VectorOffset CreateItemDropInfoListVector(
        FlatBufferBuilder builder,
        Offset<ZaEncounterItemDropInfo>[] data)
    {
        return CreateOffsetVector(builder, data.Select(offset => offset.Value).ToArray());
    }

    private static VectorOffset CreateOffsetVector(FlatBufferBuilder builder, int[] data)
    {
        builder.StartVector(4, data.Length, 4);
        for (var index = data.Length - 1; index >= 0; index--)
        {
            builder.AddOffset(data[index]);
        }

        return builder.EndVector();
    }

    private string? ReadString(int vtableOffset)
    {
        var offset = table.__offset(vtableOffset);
        return offset == 0 ? null : table.__string(offset + table.bb_pos);
    }

    private int ReadInt(int vtableOffset)
    {
        var offset = table.__offset(vtableOffset);
        return offset == 0 ? 0 : table.bb.GetInt(offset + table.bb_pos);
    }

    private float ReadFloat(int vtableOffset)
    {
        var offset = table.__offset(vtableOffset);
        return offset == 0 ? 0 : table.bb.GetFloat(offset + table.bb_pos);
    }

    private ZaPokemonDataTalentValue? ReadStats(int vtableOffset)
    {
        var offset = table.__offset(vtableOffset);
        return offset == 0
            ? null
            : new ZaPokemonDataTalentValue().__assign(table.__indirect(offset + table.bb_pos), table.bb);
    }
}

public struct ZaEncounterDataDb : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;

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

    public ZaEncounterDataDb __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    public ZaEncounterDataRow? Root(int index)
    {
        var offset = table.__offset(4);
        return offset == 0
            ? null
            : new ZaEncounterDataRow().__assign(table.__indirect(table.__vector(offset) + index * 4), table.bb);
    }

    public static Offset<ZaEncounterDataDb> Create(FlatBufferBuilder builder, VectorOffset rootOffset = default)
    {
        builder.StartTable(1);
        builder.AddOffset(0, rootOffset.Value, 0);
        return new Offset<ZaEncounterDataDb>(builder.EndTable());
    }

    public static VectorOffset CreateRootVector(FlatBufferBuilder builder, Offset<ZaEncounterDataRow>[] data)
    {
        builder.StartVector(4, data.Length, 4);
        for (var index = data.Length - 1; index >= 0; index--)
        {
            builder.AddOffset(data[index].Value);
        }

        return builder.EndVector();
    }
}

public struct ZaEncounterDataDbArray : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;

    public int ValuesLength
    {
        get
        {
            var offset = table.__offset(4);
            return offset == 0 ? 0 : table.__vector_len(offset);
        }
    }

    public static ZaEncounterDataDbArray GetRootAsZaEncounterDataDbArray(ByteBuffer buffer)
    {
        return GetRootAsZaEncounterDataDbArray(buffer, new ZaEncounterDataDbArray());
    }

    public static ZaEncounterDataDbArray GetRootAsZaEncounterDataDbArray(
        ByteBuffer buffer,
        ZaEncounterDataDbArray row)
    {
        return row.__assign(buffer.GetInt(buffer.Position) + buffer.Position, buffer);
    }

    public void __init(int position, ByteBuffer buffer)
    {
        table = new Table(position, buffer);
    }

    public ZaEncounterDataDbArray __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    public ZaEncounterDataDb? Values(int index)
    {
        var offset = table.__offset(4);
        return offset == 0
            ? null
            : new ZaEncounterDataDb().__assign(table.__indirect(table.__vector(offset) + index * 4), table.bb);
    }

    public static Offset<ZaEncounterDataDbArray> Create(
        FlatBufferBuilder builder,
        VectorOffset valuesOffset = default)
    {
        builder.StartTable(1);
        builder.AddOffset(0, valuesOffset.Value, 0);
        return new Offset<ZaEncounterDataDbArray>(builder.EndTable());
    }

    public static VectorOffset CreateValuesVector(FlatBufferBuilder builder, Offset<ZaEncounterDataDb>[] data)
    {
        builder.StartVector(4, data.Length, 4);
        for (var index = data.Length - 1; index >= 0; index--)
        {
            builder.AddOffset(data[index].Value);
        }

        return builder.EndVector();
    }

    public static void FinishBuffer(FlatBufferBuilder builder, Offset<ZaEncounterDataDbArray> offset)
    {
        builder.Finish(offset.Value);
    }
}
