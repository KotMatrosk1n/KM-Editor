// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;

namespace KM.Formats.ZA.Generated.GameData;

public struct ZaTrainerStats : IFlatbufferObject
{
    private Table p;

    public ByteBuffer ByteBuffer => p.bb;

    public int Hp => ReadInt(4);
    public int Atk => ReadInt(6);
    public int Def => ReadInt(8);
    public int SpAtk => ReadInt(10);
    public int SpDef => ReadInt(12);
    public int Agi => ReadInt(14);

    public void __init(int i, ByteBuffer bb) => p = new Table(i, bb);

    public ZaTrainerStats __assign(int i, ByteBuffer bb)
    {
        __init(i, bb);
        return this;
    }

    public static Offset<ZaTrainerStats> Create(
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
        return new Offset<ZaTrainerStats>(builder.EndTable());
    }

    private int ReadInt(int offsetSlot)
    {
        var offset = p.__offset(offsetSlot);
        return offset == 0 ? 0 : p.bb.GetInt(offset + p.bb_pos);
    }
}

public struct ZaTrainerMove : IFlatbufferObject
{
    private Table p;

    public ByteBuffer ByteBuffer => p.bb;

    public ushort MoveId
    {
        get
        {
            var offset = p.__offset(4);
            return offset == 0 ? (ushort)0 : p.bb.GetUshort(offset + p.bb_pos);
        }
    }

    public bool IsPlusMove
    {
        get
        {
            var offset = p.__offset(6);
            return offset != 0 && p.bb.Get(offset + p.bb_pos) != 0;
        }
    }

    public void __init(int i, ByteBuffer bb) => p = new Table(i, bb);

    public ZaTrainerMove __assign(int i, ByteBuffer bb)
    {
        __init(i, bb);
        return this;
    }

    public static Offset<ZaTrainerMove> Create(
        FlatBufferBuilder builder,
        ushort moveId = 0,
        bool isPlusMove = false)
    {
        builder.StartTable(2);
        builder.AddBool(1, isPlusMove, false);
        builder.AddUshort(0, moveId, 0);
        return new Offset<ZaTrainerMove>(builder.EndTable());
    }
}

public struct ZaTrainerPokemon : IFlatbufferObject
{
    private Table p;

    public ByteBuffer ByteBuffer => p.bb;

    public ushort SpeciesId
    {
        get
        {
            var offset = p.__offset(4);
            return offset == 0 ? (ushort)0 : p.bb.GetUshort(offset + p.bb_pos);
        }
    }

    public short FormId => ReadShort(6);
    public int Sex => ReadInt(8);
    public int Item => ReadInt(10);
    public int Level => ReadInt(12);
    public byte BallId
    {
        get
        {
            var offset = p.__offset(14);
            return offset == 0 ? (byte)0 : p.bb.Get(offset + p.bb_pos);
        }
    }

    public ZaTrainerMove? Move1 => ReadMove(16);
    public ZaTrainerMove? Move2 => ReadMove(18);
    public ZaTrainerMove? Move3 => ReadMove(20);
    public ZaTrainerMove? Move4 => ReadMove(22);
    public int Nature => ReadInt(24);
    public int Ability => ReadInt(26);
    public ZaTrainerStats? Ivs => ReadStats(28);
    public ZaTrainerStats? Evs => ReadStats(30);
    public int RareType => ReadInt(32);
    public short ScaleValue => ReadShort(34);
    public bool IsOriginalTrainerByName
    {
        get
        {
            var offset = p.__offset(36);
            return offset != 0 && p.bb.Get(offset + p.bb_pos) != 0;
        }
    }

    public void __init(int i, ByteBuffer bb) => p = new Table(i, bb);

    public ZaTrainerPokemon __assign(int i, ByteBuffer bb)
    {
        __init(i, bb);
        return this;
    }

    public static Offset<ZaTrainerPokemon> Create(
        FlatBufferBuilder builder,
        ushort speciesId = 0,
        short formId = 0,
        int sex = 0,
        int item = 0,
        int level = 0,
        byte ballId = 0,
        Offset<ZaTrainerMove> move1Offset = default,
        Offset<ZaTrainerMove> move2Offset = default,
        Offset<ZaTrainerMove> move3Offset = default,
        Offset<ZaTrainerMove> move4Offset = default,
        int nature = 0,
        int ability = 0,
        Offset<ZaTrainerStats> ivsOffset = default,
        Offset<ZaTrainerStats> evsOffset = default,
        int rareType = 0,
        short scaleValue = 0,
        bool isOriginalTrainerByName = false)
    {
        builder.StartTable(17);
        builder.AddBool(16, isOriginalTrainerByName, false);
        builder.AddShort(15, scaleValue, 0);
        builder.AddInt(14, rareType, 0);
        builder.AddOffset(13, evsOffset.Value, 0);
        builder.AddOffset(12, ivsOffset.Value, 0);
        builder.AddInt(11, ability, 0);
        builder.AddInt(10, nature, 0);
        builder.AddOffset(9, move4Offset.Value, 0);
        builder.AddOffset(8, move3Offset.Value, 0);
        builder.AddOffset(7, move2Offset.Value, 0);
        builder.AddOffset(6, move1Offset.Value, 0);
        builder.AddByte(5, ballId, 0);
        builder.AddInt(4, level, 0);
        builder.AddInt(3, item, 0);
        builder.AddInt(2, sex, 0);
        builder.AddShort(1, formId, 0);
        builder.AddUshort(0, speciesId, 0);
        return new Offset<ZaTrainerPokemon>(builder.EndTable());
    }

    private int ReadInt(int offsetSlot)
    {
        var offset = p.__offset(offsetSlot);
        return offset == 0 ? 0 : p.bb.GetInt(offset + p.bb_pos);
    }

    private short ReadShort(int offsetSlot)
    {
        var offset = p.__offset(offsetSlot);
        return offset == 0 ? (short)0 : p.bb.GetShort(offset + p.bb_pos);
    }

    private ZaTrainerMove? ReadMove(int offsetSlot)
    {
        var offset = p.__offset(offsetSlot);
        return offset == 0
            ? null
            : new ZaTrainerMove().__assign(p.__indirect(offset + p.bb_pos), p.bb);
    }

    private ZaTrainerStats? ReadStats(int offsetSlot)
    {
        var offset = p.__offset(offsetSlot);
        return offset == 0
            ? null
            : new ZaTrainerStats().__assign(p.__indirect(offset + p.bb_pos), p.bb);
    }
}

public struct ZaTrainerRow : IFlatbufferObject
{
    private Table p;

    public ByteBuffer ByteBuffer => p.bb;

    public string? TrainerId
    {
        get
        {
            var offset = p.__offset(4);
            return offset == 0 ? null : p.__string(offset + p.bb_pos);
        }
    }

    public ulong TrainerType => ReadUlong(6);
    public ulong TrainerType2 => ReadUlong(8);
    public sbyte Rank
    {
        get
        {
            var offset = p.__offset(10);
            return offset == 0 ? (sbyte)0 : p.bb.GetSbyte(offset + p.bb_pos);
        }
    }

    public byte MoneyRate
    {
        get
        {
            var offset = p.__offset(12);
            return offset == 0 ? (byte)0 : p.bb.Get(offset + p.bb_pos);
        }
    }

    public bool MegaEvolution => ReadBool(14);
    public bool LastHand => ReadBool(16);
    public ZaTrainerPokemon? Pokemon1 => ReadPokemon(18);
    public ZaTrainerPokemon? Pokemon2 => ReadPokemon(20);
    public ZaTrainerPokemon? Pokemon3 => ReadPokemon(22);
    public ZaTrainerPokemon? Pokemon4 => ReadPokemon(24);
    public ZaTrainerPokemon? Pokemon5 => ReadPokemon(26);
    public ZaTrainerPokemon? Pokemon6 => ReadPokemon(28);
    public bool AiBasic => ReadBool(30);
    public bool AiHigh => ReadBool(32);
    public bool AiExpert => ReadBool(34);
    public bool AiDouble => ReadBool(36);
    public bool AiRaid => ReadBool(38);
    public bool AiWeak => ReadBool(40);
    public bool AiItem => ReadBool(42);
    public bool AiChange => ReadBool(44);
    public float ViewHorizontalAngle => ReadFloat(46);
    public float ViewVerticalAngle => ReadFloat(48);
    public float ViewRange => ReadFloat(50);
    public float HearingRange => ReadFloat(52);

    public void __init(int i, ByteBuffer bb) => p = new Table(i, bb);

    public ZaTrainerRow __assign(int i, ByteBuffer bb)
    {
        __init(i, bb);
        return this;
    }

    public static Offset<ZaTrainerRow> Create(
        FlatBufferBuilder builder,
        StringOffset trainerIdOffset = default,
        ulong trainerType = 0,
        ulong trainerType2 = 0,
        sbyte rank = 0,
        byte moneyRate = 0,
        bool megaEvolution = false,
        bool lastHand = false,
        Offset<ZaTrainerPokemon> pokemon1Offset = default,
        Offset<ZaTrainerPokemon> pokemon2Offset = default,
        Offset<ZaTrainerPokemon> pokemon3Offset = default,
        Offset<ZaTrainerPokemon> pokemon4Offset = default,
        Offset<ZaTrainerPokemon> pokemon5Offset = default,
        Offset<ZaTrainerPokemon> pokemon6Offset = default,
        bool aiBasic = false,
        bool aiHigh = false,
        bool aiExpert = false,
        bool aiDouble = false,
        bool aiRaid = false,
        bool aiWeak = false,
        bool aiItem = false,
        bool aiChange = false,
        float viewHorizontalAngle = 0,
        float viewVerticalAngle = 0,
        float viewRange = 0,
        float hearingRange = 0)
    {
        builder.StartTable(25);
        builder.AddUlong(2, trainerType2, 0);
        builder.AddUlong(1, trainerType, 0);
        builder.AddFloat(24, hearingRange, 0);
        builder.AddFloat(23, viewRange, 0);
        builder.AddFloat(22, viewVerticalAngle, 0);
        builder.AddFloat(21, viewHorizontalAngle, 0);
        builder.AddOffset(12, pokemon6Offset.Value, 0);
        builder.AddOffset(11, pokemon5Offset.Value, 0);
        builder.AddOffset(10, pokemon4Offset.Value, 0);
        builder.AddOffset(9, pokemon3Offset.Value, 0);
        builder.AddOffset(8, pokemon2Offset.Value, 0);
        builder.AddOffset(7, pokemon1Offset.Value, 0);
        builder.AddOffset(0, trainerIdOffset.Value, 0);
        builder.AddBool(20, aiChange, false);
        builder.AddBool(19, aiItem, false);
        builder.AddBool(18, aiWeak, false);
        builder.AddBool(17, aiRaid, false);
        builder.AddBool(16, aiDouble, false);
        builder.AddBool(15, aiExpert, false);
        builder.AddBool(14, aiHigh, false);
        builder.AddBool(13, aiBasic, false);
        builder.AddBool(6, lastHand, false);
        builder.AddBool(5, megaEvolution, false);
        builder.AddByte(4, moneyRate, 0);
        builder.AddSbyte(3, rank, 0);
        return new Offset<ZaTrainerRow>(builder.EndTable());
    }

    private ulong ReadUlong(int offsetSlot)
    {
        var offset = p.__offset(offsetSlot);
        return offset == 0 ? 0UL : p.bb.GetUlong(offset + p.bb_pos);
    }

    private bool ReadBool(int offsetSlot)
    {
        var offset = p.__offset(offsetSlot);
        return offset != 0 && p.bb.Get(offset + p.bb_pos) != 0;
    }

    private float ReadFloat(int offsetSlot)
    {
        var offset = p.__offset(offsetSlot);
        return offset == 0 ? 0 : p.bb.GetFloat(offset + p.bb_pos);
    }

    private ZaTrainerPokemon? ReadPokemon(int offsetSlot)
    {
        var offset = p.__offset(offsetSlot);
        return offset == 0
            ? null
            : new ZaTrainerPokemon().__assign(p.__indirect(offset + p.bb_pos), p.bb);
    }
}

public struct ZaTrainerTable : IFlatbufferObject
{
    private Table p;

    public ByteBuffer ByteBuffer => p.bb;

    public static ZaTrainerTable GetRootAsZaTrainerTable(ByteBuffer bb) =>
        GetRootAsZaTrainerTable(bb, new ZaTrainerTable());

    public static ZaTrainerTable GetRootAsZaTrainerTable(ByteBuffer bb, ZaTrainerTable obj) =>
        obj.__assign(bb.GetInt(bb.Position) + bb.Position, bb);

    public ZaTrainerRow? Value(int index)
    {
        var offset = p.__offset(4);
        return offset == 0
            ? null
            : new ZaTrainerRow().__assign(p.__indirect(p.__vector(offset) + index * 4), p.bb);
    }

    public int ValueLength
    {
        get
        {
            var offset = p.__offset(4);
            return offset == 0 ? 0 : p.__vector_len(offset);
        }
    }

    public void __init(int i, ByteBuffer bb) => p = new Table(i, bb);

    public ZaTrainerTable __assign(int i, ByteBuffer bb)
    {
        __init(i, bb);
        return this;
    }

    public static VectorOffset CreateValueVector(FlatBufferBuilder builder, Offset<ZaTrainerRow>[] data)
    {
        builder.StartVector(4, data.Length, 4);
        for (var index = data.Length - 1; index >= 0; index--)
        {
            builder.AddOffset(data[index].Value);
        }

        return builder.EndVector();
    }

    public static Offset<ZaTrainerTable> Create(FlatBufferBuilder builder, VectorOffset valueOffset = default)
    {
        builder.StartTable(1);
        builder.AddOffset(0, valueOffset.Value, 0);
        return new Offset<ZaTrainerTable>(builder.EndTable());
    }

    public static void FinishBuffer(FlatBufferBuilder builder, Offset<ZaTrainerTable> offset)
    {
        builder.Finish(offset.Value);
    }
}
