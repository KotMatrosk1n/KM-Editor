// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;

namespace KM.Formats.ZA.Generated.GameData;

public struct ZaPokemonDataActivationConditionParam : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;

    public string? Condition
    {
        get
        {
            var offset = table.__offset(4);
            return offset == 0 ? null : table.__string(offset + table.bb_pos);
        }
    }

    public int Op
    {
        get
        {
            var offset = table.__offset(6);
            return offset == 0 ? 0 : table.bb.GetInt(offset + table.bb_pos);
        }
    }

    public int ParamLength
    {
        get
        {
            var offset = table.__offset(8);
            return offset == 0 ? 0 : table.__vector_len(offset);
        }
    }

    public static ZaPokemonDataActivationConditionParam GetRootAsZaPokemonDataActivationConditionParam(ByteBuffer buffer)
    {
        return GetRootAsZaPokemonDataActivationConditionParam(buffer, new ZaPokemonDataActivationConditionParam());
    }

    public static ZaPokemonDataActivationConditionParam GetRootAsZaPokemonDataActivationConditionParam(
        ByteBuffer buffer,
        ZaPokemonDataActivationConditionParam row)
    {
        return row.__assign(buffer.GetInt(buffer.Position) + buffer.Position, buffer);
    }

    public void __init(int position, ByteBuffer buffer)
    {
        table = new Table(position, buffer);
    }

    public ZaPokemonDataActivationConditionParam __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    public string? Param(int index)
    {
        var offset = table.__offset(8);
        return offset == 0 ? null : table.__string(table.__vector(offset) + index * 4);
    }

    public static Offset<ZaPokemonDataActivationConditionParam> Create(
        FlatBufferBuilder builder,
        StringOffset conditionOffset = default,
        int op = 0,
        VectorOffset paramOffset = default)
    {
        builder.StartTable(3);
        builder.AddOffset(2, paramOffset.Value, 0);
        builder.AddInt(1, op, 0);
        builder.AddOffset(0, conditionOffset.Value, 0);
        return new Offset<ZaPokemonDataActivationConditionParam>(builder.EndTable());
    }

    public static VectorOffset CreateParamVector(FlatBufferBuilder builder, StringOffset[] data)
    {
        builder.StartVector(4, data.Length, 4);
        for (var index = data.Length - 1; index >= 0; index--)
        {
            builder.AddOffset(data[index].Value);
        }

        return builder.EndVector();
    }
}

public struct ZaPokemonDataActivationConditionElement : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;

    public int ParamLength
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

    public ZaPokemonDataActivationConditionElement __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    public ZaPokemonDataActivationConditionParam? Param(int index)
    {
        var offset = table.__offset(4);
        return offset == 0
            ? null
            : new ZaPokemonDataActivationConditionParam().__assign(
                table.__indirect(table.__vector(offset) + index * 4),
                table.bb);
    }

    public static Offset<ZaPokemonDataActivationConditionElement> Create(
        FlatBufferBuilder builder,
        VectorOffset paramOffset = default)
    {
        builder.StartTable(1);
        builder.AddOffset(0, paramOffset.Value, 0);
        return new Offset<ZaPokemonDataActivationConditionElement>(builder.EndTable());
    }

    public static VectorOffset CreateParamVector(
        FlatBufferBuilder builder,
        Offset<ZaPokemonDataActivationConditionParam>[] data)
    {
        builder.StartVector(4, data.Length, 4);
        for (var index = data.Length - 1; index >= 0; index--)
        {
            builder.AddOffset(data[index].Value);
        }

        return builder.EndVector();
    }
}

public struct ZaPokemonDataActivationCondition : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;

    public int ElementLength
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

    public ZaPokemonDataActivationCondition __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    public ZaPokemonDataActivationConditionElement? Element(int index)
    {
        var offset = table.__offset(4);
        return offset == 0
            ? null
            : new ZaPokemonDataActivationConditionElement().__assign(
                table.__indirect(table.__vector(offset) + index * 4),
                table.bb);
    }

    public static Offset<ZaPokemonDataActivationCondition> Create(
        FlatBufferBuilder builder,
        VectorOffset elementOffset = default)
    {
        builder.StartTable(1);
        builder.AddOffset(0, elementOffset.Value, 0);
        return new Offset<ZaPokemonDataActivationCondition>(builder.EndTable());
    }

    public static VectorOffset CreateElementVector(
        FlatBufferBuilder builder,
        Offset<ZaPokemonDataActivationConditionElement>[] data)
    {
        builder.StartVector(4, data.Length, 4);
        for (var index = data.Length - 1; index >= 0; index--)
        {
            builder.AddOffset(data[index].Value);
        }

        return builder.EndVector();
    }
}

public struct ZaPokemonDataTalentValue : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;
    public int Hp => ReadInt(4);
    public int Atk => ReadInt(6);
    public int Def => ReadInt(8);
    public int SpAtk => ReadInt(10);
    public int SpDef => ReadInt(12);
    public int Agi => ReadInt(14);

    public void __init(int position, ByteBuffer buffer)
    {
        table = new Table(position, buffer);
    }

    public ZaPokemonDataTalentValue __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    public static Offset<ZaPokemonDataTalentValue> Create(
        FlatBufferBuilder builder,
        int hp = 0,
        int atk = 0,
        int def = 0,
        int spAtk = 0,
        int spDef = 0,
        int agi = 0)
    {
        builder.StartTable(6);
        builder.AddInt(5, agi, 0);
        builder.AddInt(4, spDef, 0);
        builder.AddInt(3, spAtk, 0);
        builder.AddInt(2, def, 0);
        builder.AddInt(1, atk, 0);
        builder.AddInt(0, hp, 0);
        return new Offset<ZaPokemonDataTalentValue>(builder.EndTable());
    }

    private int ReadInt(int vtableOffset)
    {
        var offset = table.__offset(vtableOffset);
        return offset == 0 ? 0 : table.bb.GetInt(offset + table.bb_pos);
    }
}

public struct ZaPokemonDataWazaList : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;
    public int Waza1 => ReadInt(4);
    public int Waza2 => ReadInt(6);
    public int Waza3 => ReadInt(8);
    public int Waza4 => ReadInt(10);

    public void __init(int position, ByteBuffer buffer)
    {
        table = new Table(position, buffer);
    }

    public ZaPokemonDataWazaList __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    public static Offset<ZaPokemonDataWazaList> Create(
        FlatBufferBuilder builder,
        int waza1 = 0,
        int waza2 = 0,
        int waza3 = 0,
        int waza4 = 0)
    {
        builder.StartTable(4);
        builder.AddInt(3, waza4, 0);
        builder.AddInt(2, waza3, 0);
        builder.AddInt(1, waza2, 0);
        builder.AddInt(0, waza1, 0);
        return new Offset<ZaPokemonDataWazaList>(builder.EndTable());
    }

    private int ReadInt(int vtableOffset)
    {
        var offset = table.__offset(vtableOffset);
        return offset == 0 ? 0 : table.bb.GetInt(offset + table.bb_pos);
    }
}

public struct ZaPokemonDataHoldItem : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;

    public int HoldItem
    {
        get
        {
            var offset = table.__offset(4);
            return offset == 0 ? 0 : table.bb.GetInt(offset + table.bb_pos);
        }
    }

    public void __init(int position, ByteBuffer buffer)
    {
        table = new Table(position, buffer);
    }

    public ZaPokemonDataHoldItem __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    public static Offset<ZaPokemonDataHoldItem> Create(FlatBufferBuilder builder, int holdItem = 0)
    {
        builder.StartTable(1);
        builder.AddInt(0, holdItem, 0);
        return new Offset<ZaPokemonDataHoldItem>(builder.EndTable());
    }
}

public struct ZaPokemonDataRow : IFlatbufferObject
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

    public ZaPokemonDataTalentValue? TalentValue
    {
        get
        {
            var offset = table.__offset(32);
            return offset == 0
                ? null
                : new ZaPokemonDataTalentValue().__assign(table.__indirect(offset + table.bb_pos), table.bb);
        }
    }

    public ZaPokemonDataWazaList? WazaList
    {
        get
        {
            var offset = table.__offset(34);
            return offset == 0
                ? null
                : new ZaPokemonDataWazaList().__assign(table.__indirect(offset + table.bb_pos), table.bb);
        }
    }

    public ZaPokemonDataHoldItem? HoldItem
    {
        get
        {
            var offset = table.__offset(36);
            return offset == 0
                ? null
                : new ZaPokemonDataHoldItem().__assign(table.__indirect(offset + table.bb_pos), table.bb);
        }
    }

    public void __init(int position, ByteBuffer buffer)
    {
        table = new Table(position, buffer);
    }

    public ZaPokemonDataRow __assign(int position, ByteBuffer buffer)
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

    public static Offset<ZaPokemonDataRow> Create(
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
        Offset<ZaPokemonDataWazaList> wazaListOffset = default,
        Offset<ZaPokemonDataHoldItem> holdItemOffset = default)
    {
        builder.StartTable(17);
        builder.AddOffset(16, holdItemOffset.Value, 0);
        builder.AddOffset(15, wazaListOffset.Value, 0);
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
        return new Offset<ZaPokemonDataRow>(builder.EndTable());
    }

    public static VectorOffset CreateActivationConditionVector(
        FlatBufferBuilder builder,
        Offset<ZaPokemonDataActivationCondition>[] data)
    {
        builder.StartVector(4, data.Length, 4);
        for (var index = data.Length - 1; index >= 0; index--)
        {
            builder.AddOffset(data[index].Value);
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
}

public struct ZaPokemonDataDb : IFlatbufferObject
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

    public ZaPokemonDataDb __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    public ZaPokemonDataRow? Root(int index)
    {
        var offset = table.__offset(4);
        return offset == 0
            ? null
            : new ZaPokemonDataRow().__assign(table.__indirect(table.__vector(offset) + index * 4), table.bb);
    }

    public static Offset<ZaPokemonDataDb> Create(FlatBufferBuilder builder, VectorOffset rootOffset = default)
    {
        builder.StartTable(1);
        builder.AddOffset(0, rootOffset.Value, 0);
        return new Offset<ZaPokemonDataDb>(builder.EndTable());
    }

    public static VectorOffset CreateRootVector(FlatBufferBuilder builder, Offset<ZaPokemonDataRow>[] data)
    {
        builder.StartVector(4, data.Length, 4);
        for (var index = data.Length - 1; index >= 0; index--)
        {
            builder.AddOffset(data[index].Value);
        }

        return builder.EndVector();
    }
}

public struct ZaPokemonDataDbArray : IFlatbufferObject
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

    public static ZaPokemonDataDbArray GetRootAsZaPokemonDataDbArray(ByteBuffer buffer)
    {
        return GetRootAsZaPokemonDataDbArray(buffer, new ZaPokemonDataDbArray());
    }

    public static ZaPokemonDataDbArray GetRootAsZaPokemonDataDbArray(
        ByteBuffer buffer,
        ZaPokemonDataDbArray row)
    {
        return row.__assign(buffer.GetInt(buffer.Position) + buffer.Position, buffer);
    }

    public void __init(int position, ByteBuffer buffer)
    {
        table = new Table(position, buffer);
    }

    public ZaPokemonDataDbArray __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    public ZaPokemonDataDb? Values(int index)
    {
        var offset = table.__offset(4);
        return offset == 0
            ? null
            : new ZaPokemonDataDb().__assign(table.__indirect(table.__vector(offset) + index * 4), table.bb);
    }

    public static Offset<ZaPokemonDataDbArray> Create(
        FlatBufferBuilder builder,
        VectorOffset valuesOffset = default)
    {
        builder.StartTable(1);
        builder.AddOffset(0, valuesOffset.Value, 0);
        return new Offset<ZaPokemonDataDbArray>(builder.EndTable());
    }

    public static VectorOffset CreateValuesVector(FlatBufferBuilder builder, Offset<ZaPokemonDataDb>[] data)
    {
        builder.StartVector(4, data.Length, 4);
        for (var index = data.Length - 1; index >= 0; index--)
        {
            builder.AddOffset(data[index].Value);
        }

        return builder.EndVector();
    }

    public static void FinishBuffer(FlatBufferBuilder builder, Offset<ZaPokemonDataDbArray> offset)
    {
        builder.Finish(offset.Value);
    }
}
