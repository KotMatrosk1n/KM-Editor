// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;

namespace KM.Formats.ZA.Generated.GameData;

public struct ZaSpeciesInfo : IFlatbufferObject
{
    private Struct p;

    public ByteBuffer ByteBuffer => p.bb;

    public ushort Species => p.bb.GetUshort(p.bb_pos);
    public ushort Form => p.bb.GetUshort(p.bb_pos + 2);
    public ushort Model => p.bb.GetUshort(p.bb_pos + 4);
    public byte Color => p.bb.Get(p.bb_pos + 6);
    public byte BodyType => p.bb.Get(p.bb_pos + 7);
    public ushort Height => p.bb.GetUshort(p.bb_pos + 8);
    public ushort Weight => p.bb.GetUshort(p.bb_pos + 10);
    public byte Reserved => p.bb.Get(p.bb_pos + 12);
    public byte Reserved1 => p.bb.Get(p.bb_pos + 13);
    public byte Reserved2 => p.bb.Get(p.bb_pos + 14);

    public void __init(int i, ByteBuffer bb) => p = new Struct(i, bb);

    public ZaSpeciesInfo __assign(int i, ByteBuffer bb)
    {
        __init(i, bb);
        return this;
    }

    public static Offset<ZaSpeciesInfo> Create(
        FlatBufferBuilder builder,
        ushort species,
        ushort form,
        ushort model,
        byte color,
        byte bodyType,
        ushort height,
        ushort weight,
        byte reserved,
        byte reserved1,
        byte reserved2)
    {
        builder.Prep(2, 16);
        builder.Pad(1);
        builder.PutByte(reserved2);
        builder.PutByte(reserved1);
        builder.PutByte(reserved);
        builder.PutUshort(weight);
        builder.PutUshort(height);
        builder.PutByte(bodyType);
        builder.PutByte(color);
        builder.PutUshort(model);
        builder.PutUshort(form);
        builder.PutUshort(species);
        return new Offset<ZaSpeciesInfo>(builder.Offset);
    }
}

public struct ZaGenderInfo : IFlatbufferObject
{
    private Struct p;

    public ByteBuffer ByteBuffer => p.bb;

    public byte Group => p.bb.Get(p.bb_pos);
    public byte Ratio => p.bb.Get(p.bb_pos + 1);

    public void __init(int i, ByteBuffer bb) => p = new Struct(i, bb);

    public ZaGenderInfo __assign(int i, ByteBuffer bb)
    {
        __init(i, bb);
        return this;
    }

    public static Offset<ZaGenderInfo> Create(FlatBufferBuilder builder, byte group, byte ratio)
    {
        builder.Prep(1, 2);
        builder.PutByte(ratio);
        builder.PutByte(group);
        return new Offset<ZaGenderInfo>(builder.Offset);
    }
}

public struct ZaEggHatchInfo : IFlatbufferObject
{
    private Struct p;

    public ByteBuffer ByteBuffer => p.bb;

    public ushort Species => p.bb.GetUshort(p.bb_pos);
    public ushort Form => p.bb.GetUshort(p.bb_pos + 2);
    public ushort FormFlags => p.bb.GetUshort(p.bb_pos + 4);
    public ushort FormEverstone => p.bb.GetUshort(p.bb_pos + 6);

    public void __init(int i, ByteBuffer bb) => p = new Struct(i, bb);

    public ZaEggHatchInfo __assign(int i, ByteBuffer bb)
    {
        __init(i, bb);
        return this;
    }

    public static Offset<ZaEggHatchInfo> Create(
        FlatBufferBuilder builder,
        ushort species,
        ushort form,
        ushort formFlags,
        ushort formEverstone)
    {
        builder.Prep(2, 8);
        builder.PutUshort(formEverstone);
        builder.PutUshort(formFlags);
        builder.PutUshort(form);
        builder.PutUshort(species);
        return new Offset<ZaEggHatchInfo>(builder.Offset);
    }
}

public struct ZaStatInfo : IFlatbufferObject
{
    private Struct p;

    public ByteBuffer ByteBuffer => p.bb;

    public byte Hp => p.bb.Get(p.bb_pos);
    public byte Atk => p.bb.Get(p.bb_pos + 1);
    public byte Def => p.bb.Get(p.bb_pos + 2);
    public byte Spa => p.bb.Get(p.bb_pos + 3);
    public byte Spd => p.bb.Get(p.bb_pos + 4);
    public byte Spe => p.bb.Get(p.bb_pos + 5);

    public void __init(int i, ByteBuffer bb) => p = new Struct(i, bb);

    public ZaStatInfo __assign(int i, ByteBuffer bb)
    {
        __init(i, bb);
        return this;
    }

    public static Offset<ZaStatInfo> Create(
        FlatBufferBuilder builder,
        byte hp,
        byte atk,
        byte def,
        byte spa,
        byte spd,
        byte spe)
    {
        builder.Prep(1, 6);
        builder.PutByte(spe);
        builder.PutByte(spd);
        builder.PutByte(spa);
        builder.PutByte(def);
        builder.PutByte(atk);
        builder.PutByte(hp);
        return new Offset<ZaStatInfo>(builder.Offset);
    }
}

public struct ZaEvolutionData : IFlatbufferObject
{
    private Struct p;

    public ByteBuffer ByteBuffer => p.bb;

    public ushort Level => p.bb.GetUshort(p.bb_pos);
    public ushort Condition => p.bb.GetUshort(p.bb_pos + 2);
    public ushort Parameter => p.bb.GetUshort(p.bb_pos + 4);
    public ushort Reserved3 => p.bb.GetUshort(p.bb_pos + 6);
    public ushort Reserved4 => p.bb.GetUshort(p.bb_pos + 8);
    public ushort Reserved5 => p.bb.GetUshort(p.bb_pos + 10);
    public ushort Species => p.bb.GetUshort(p.bb_pos + 12);
    public ushort Form => p.bb.GetUshort(p.bb_pos + 14);

    public void __init(int i, ByteBuffer bb) => p = new Struct(i, bb);

    public ZaEvolutionData __assign(int i, ByteBuffer bb)
    {
        __init(i, bb);
        return this;
    }

    public static Offset<ZaEvolutionData> Create(
        FlatBufferBuilder builder,
        ushort level,
        ushort condition,
        ushort parameter,
        ushort reserved3,
        ushort reserved4,
        ushort reserved5,
        ushort species,
        ushort form)
    {
        builder.Prep(2, 16);
        builder.PutUshort(form);
        builder.PutUshort(species);
        builder.PutUshort(reserved5);
        builder.PutUshort(reserved4);
        builder.PutUshort(reserved3);
        builder.PutUshort(parameter);
        builder.PutUshort(condition);
        builder.PutUshort(level);
        return new Offset<ZaEvolutionData>(builder.Offset);
    }
}

public struct ZaLevelUpMoveData : IFlatbufferObject
{
    private Struct p;

    public ByteBuffer ByteBuffer => p.bb;

    public ushort Move => p.bb.GetUshort(p.bb_pos);
    public ushort Level => p.bb.GetUshort(p.bb_pos + 2);

    public void __init(int i, ByteBuffer bb) => p = new Struct(i, bb);

    public ZaLevelUpMoveData __assign(int i, ByteBuffer bb)
    {
        __init(i, bb);
        return this;
    }

    public static Offset<ZaLevelUpMoveData> Create(FlatBufferBuilder builder, ushort move, ushort level)
    {
        builder.Prep(2, 4);
        builder.PutUshort(level);
        builder.PutUshort(move);
        return new Offset<ZaLevelUpMoveData>(builder.Offset);
    }
}

public struct ZaPersonal : IFlatbufferObject
{
    private Table p;

    public ByteBuffer ByteBuffer => p.bb;

    public static ZaPersonal GetRootAsZaPersonal(ByteBuffer bb) => GetRootAsZaPersonal(bb, new ZaPersonal());

    public static ZaPersonal GetRootAsZaPersonal(ByteBuffer bb, ZaPersonal obj) =>
        obj.__assign(bb.GetInt(bb.Position) + bb.Position, bb);

    public void __init(int i, ByteBuffer bb) => p = new Table(i, bb);

    public ZaPersonal __assign(int i, ByteBuffer bb)
    {
        __init(i, bb);
        return this;
    }

    public ZaSpeciesInfo? Species
    {
        get
        {
            var offset = p.__offset(4);
            return offset != 0 ? new ZaSpeciesInfo().__assign(offset + p.bb_pos, p.bb) : null;
        }
    }

    public bool IsPresent => ReadBool(6);
    public byte ZADexOrder => ReadByte(8);
    public byte Type1 => ReadByte(10);
    public byte Type2 => ReadByte(12);
    public ushort Ability1 => ReadUshort(14);
    public ushort Ability2 => ReadUshort(16);
    public ushort AbilityHidden => ReadUshort(18);
    public byte XpGrowth => ReadByte(20);
    public byte CatchRate => ReadByte(22);

    public ZaGenderInfo? Gender
    {
        get
        {
            var offset = p.__offset(24);
            return offset != 0 ? new ZaGenderInfo().__assign(offset + p.bb_pos, p.bb) : null;
        }
    }

    public byte EggGroup1 => ReadByte(26);
    public byte EggGroup2 => ReadByte(28);

    public ZaEggHatchInfo? EggHatch
    {
        get
        {
            var offset = p.__offset(30);
            return offset != 0 ? new ZaEggHatchInfo().__assign(offset + p.bb_pos, p.bb) : null;
        }
    }

    public byte EggHatchCycles => ReadByte(32);
    public byte BaseFriendship => ReadByte(34);
    public ushort Unknown16 => ReadUshort(36);
    public bool HasUnknown16 => p.__offset(36) != 0;
    public byte EvoStage => ReadByte(38);
    public ushort Unknown18 => ReadUshort(40);
    public bool HasUnknown18 => p.__offset(40) != 0;

    public ZaStatInfo? EvYield
    {
        get
        {
            var offset = p.__offset(42);
            return offset != 0 ? new ZaStatInfo().__assign(offset + p.bb_pos, p.bb) : null;
        }
    }

    public ZaStatInfo? BaseStats
    {
        get
        {
            var offset = p.__offset(44);
            return offset != 0 ? new ZaStatInfo().__assign(offset + p.bb_pos, p.bb) : null;
        }
    }

    public ZaEvolutionData? Evolutions(int index)
    {
        var offset = p.__offset(46);
        return offset != 0
            ? new ZaEvolutionData().__assign(p.__vector(offset) + index * 16, p.bb)
            : null;
    }

    public int EvolutionsLength
    {
        get
        {
            var offset = p.__offset(46);
            return offset != 0 ? p.__vector_len(offset) : 0;
        }
    }

    public ushort TmMoves(int index) => ReadUshortVector(48, index);

    public int TmMovesLength => ReadVectorLength(48);

    public ushort[] GetTmMovesArray() => ReadUshortArray(48);

    public ushort EggMoves(int index) => ReadUshortVector(50, index);

    public int EggMovesLength => ReadVectorLength(50);

    public ushort[] GetEggMovesArray() => ReadUshortArray(50);

    public ushort ReminderMoves(int index) => ReadUshortVector(52, index);

    public int ReminderMovesLength => ReadVectorLength(52);

    public ushort[] GetReminderMovesArray() => ReadUshortArray(52);

    public ZaLevelUpMoveData? LevelupMoves(int index)
    {
        var offset = p.__offset(54);
        return offset != 0
            ? new ZaLevelUpMoveData().__assign(p.__vector(offset) + index * 4, p.bb)
            : null;
    }

    public int LevelupMovesLength
    {
        get
        {
            var offset = p.__offset(54);
            return offset != 0 ? p.__vector_len(offset) : 0;
        }
    }

    public bool HasSpecies => HasField(4);
    public bool HasIsPresent => HasField(6);
    public bool HasZADexOrder => HasField(8);
    public bool HasType1 => HasField(10);
    public bool HasType2 => HasField(12);
    public bool HasAbility1 => HasField(14);
    public bool HasAbility2 => HasField(16);
    public bool HasAbilityHidden => HasField(18);
    public bool HasXpGrowth => HasField(20);
    public bool HasCatchRate => HasField(22);
    public bool HasGender => HasField(24);
    public bool HasEggGroup1 => HasField(26);
    public bool HasEggGroup2 => HasField(28);
    public bool HasEggHatch => HasField(30);
    public bool HasEggHatchCycles => HasField(32);
    public bool HasBaseFriendship => HasField(34);
    public bool HasEvoStage => HasField(38);
    public bool HasEvYield => HasField(42);
    public bool HasBaseStats => HasField(44);
    public bool HasEvolutions => HasField(46);
    public bool HasTmMoves => HasField(48);
    public bool HasEggMoves => HasField(50);
    public bool HasReminderMoves => HasField(52);
    public bool HasLevelupMoves => HasField(54);

    public static void Start(FlatBufferBuilder builder) => builder.StartTable(26);
    public static void AddSpecies(FlatBufferBuilder builder, Offset<ZaSpeciesInfo> offset) => builder.AddStruct(0, offset.Value, 0);
    public static void AddIsPresent(FlatBufferBuilder builder, bool value) => builder.AddBool(1, value, false);
    public static void AddZADexOrder(FlatBufferBuilder builder, byte value) => builder.AddByte(2, value, 0);
    public static void AddType1(FlatBufferBuilder builder, byte value) => builder.AddByte(3, value, 0);
    public static void AddType2(FlatBufferBuilder builder, byte value) => builder.AddByte(4, value, 0);
    public static void AddAbility1(FlatBufferBuilder builder, ushort value) => builder.AddUshort(5, value, 0);
    public static void AddAbility2(FlatBufferBuilder builder, ushort value) => builder.AddUshort(6, value, 0);
    public static void AddAbilityHidden(FlatBufferBuilder builder, ushort value) => builder.AddUshort(7, value, 0);
    public static void AddXpGrowth(FlatBufferBuilder builder, byte value) => builder.AddByte(8, value, 0);
    public static void AddCatchRate(FlatBufferBuilder builder, byte value) => builder.AddByte(9, value, 0);
    public static void AddGender(FlatBufferBuilder builder, Offset<ZaGenderInfo> offset) => builder.AddStruct(10, offset.Value, 0);
    public static void AddEggGroup1(FlatBufferBuilder builder, byte value) => builder.AddByte(11, value, 0);
    public static void AddEggGroup2(FlatBufferBuilder builder, byte value) => builder.AddByte(12, value, 0);
    public static void AddEggHatch(FlatBufferBuilder builder, Offset<ZaEggHatchInfo> offset) => builder.AddStruct(13, offset.Value, 0);
    public static void AddEggHatchCycles(FlatBufferBuilder builder, byte value) => builder.AddByte(14, value, 0);
    public static void AddBaseFriendship(FlatBufferBuilder builder, byte value) => builder.AddByte(15, value, 0);
    public static void AddUnknown16(FlatBufferBuilder builder, ushort value) => builder.AddUshort(16, value, 0);
    public static void AddEvoStage(FlatBufferBuilder builder, byte value) => builder.AddByte(17, value, 0);
    public static void AddUnknown18(FlatBufferBuilder builder, ushort value) => builder.AddUshort(18, value, 0);
    public static void AddEvYield(FlatBufferBuilder builder, Offset<ZaStatInfo> offset) => builder.AddStruct(19, offset.Value, 0);
    public static void AddBaseStats(FlatBufferBuilder builder, Offset<ZaStatInfo> offset) => builder.AddStruct(20, offset.Value, 0);
    public static void AddEvolutions(FlatBufferBuilder builder, VectorOffset offset) => builder.AddOffset(21, offset.Value, 0);
    public static void StartEvolutionsVector(FlatBufferBuilder builder, int count) => builder.StartVector(16, count, 2);
    public static void AddTmMoves(FlatBufferBuilder builder, VectorOffset offset) => builder.AddOffset(22, offset.Value, 0);
    public static void AddEggMoves(FlatBufferBuilder builder, VectorOffset offset) => builder.AddOffset(23, offset.Value, 0);
    public static void AddReminderMoves(FlatBufferBuilder builder, VectorOffset offset) => builder.AddOffset(24, offset.Value, 0);
    public static void AddLevelupMoves(FlatBufferBuilder builder, VectorOffset offset) => builder.AddOffset(25, offset.Value, 0);
    public static void StartLevelupMovesVector(FlatBufferBuilder builder, int count) => builder.StartVector(4, count, 2);

    public static VectorOffset CreateUshortVector(FlatBufferBuilder builder, IReadOnlyList<ushort> data)
    {
        builder.StartVector(2, data.Count, 2);
        for (var index = data.Count - 1; index >= 0; index--)
        {
            builder.AddUshort(data[index]);
        }

        return builder.EndVector();
    }

    public static Offset<ZaPersonal> End(FlatBufferBuilder builder) => new(builder.EndTable());

    private bool ReadBool(int vtableOffset)
    {
        var offset = p.__offset(vtableOffset);
        return offset != 0 && p.bb.Get(offset + p.bb_pos) != 0;
    }

    private byte ReadByte(int vtableOffset)
    {
        var offset = p.__offset(vtableOffset);
        return offset != 0 ? p.bb.Get(offset + p.bb_pos) : (byte)0;
    }

    private ushort ReadUshort(int vtableOffset)
    {
        var offset = p.__offset(vtableOffset);
        return offset != 0 ? p.bb.GetUshort(offset + p.bb_pos) : (ushort)0;
    }

    private ushort ReadUshortVector(int vtableOffset, int index)
    {
        var offset = p.__offset(vtableOffset);
        return offset != 0 ? p.bb.GetUshort(p.__vector(offset) + index * 2) : (ushort)0;
    }

    private int ReadVectorLength(int vtableOffset)
    {
        var offset = p.__offset(vtableOffset);
        return offset != 0 ? p.__vector_len(offset) : 0;
    }

    private ushort[] ReadUshortArray(int vtableOffset)
    {
        var length = ReadVectorLength(vtableOffset);
        if (length == 0)
        {
            return [];
        }

        var values = new ushort[length];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = ReadUshortVector(vtableOffset, index);
        }

        return values;
    }

    private bool HasField(int vtableOffset) => p.__offset(vtableOffset) != 0;
}

public struct ZaPersonalTable : IFlatbufferObject
{
    private Table p;

    public ByteBuffer ByteBuffer => p.bb;

    public static ZaPersonalTable GetRootAsZaPersonalTable(ByteBuffer bb) =>
        GetRootAsZaPersonalTable(bb, new ZaPersonalTable());

    public static ZaPersonalTable GetRootAsZaPersonalTable(ByteBuffer bb, ZaPersonalTable obj) =>
        obj.__assign(bb.GetInt(bb.Position) + bb.Position, bb);

    public void __init(int i, ByteBuffer bb) => p = new Table(i, bb);

    public ZaPersonalTable __assign(int i, ByteBuffer bb)
    {
        __init(i, bb);
        return this;
    }

    public ZaPersonal? Entry(int index)
    {
        var offset = p.__offset(4);
        return offset != 0
            ? new ZaPersonal().__assign(p.__indirect(p.__vector(offset) + index * 4), p.bb)
            : null;
    }

    public int EntryLength
    {
        get
        {
            var offset = p.__offset(4);
            return offset != 0 ? p.__vector_len(offset) : 0;
        }
    }

    public static void Start(FlatBufferBuilder builder) => builder.StartTable(1);
    public static void AddEntry(FlatBufferBuilder builder, VectorOffset offset) => builder.AddOffset(0, offset.Value, 0);
    public static VectorOffset CreateEntryVector(FlatBufferBuilder builder, Offset<ZaPersonal>[] data)
    {
        builder.StartVector(4, data.Length, 4);
        for (var index = data.Length - 1; index >= 0; index--)
        {
            builder.AddOffset(data[index].Value);
        }

        return builder.EndVector();
    }

    public static Offset<ZaPersonalTable> End(FlatBufferBuilder builder) => new(builder.EndTable());
    public static void FinishBuffer(FlatBufferBuilder builder, Offset<ZaPersonalTable> offset) => builder.Finish(offset.Value);
}
