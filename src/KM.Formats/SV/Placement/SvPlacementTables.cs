// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;

namespace KM.Formats.SV.Placement;

public enum BehaviorFrequency : int
{
    None = -1,
    Every1Frame = 0,
    Every2Frames = 1,
    Every3Frames = 2,
    Every4Frames = 3,
    Every5Frames = 4,
    Every6Frames = 5,
    Every10Frames = 6,
    Every12Frames = 7,
    Every15Frames = 8,
    Every30Frames = 9,
    Every60Frames = 10,
}

public enum GenerationPattern : int
{
    Encount = 0,
    Watch = 1,
}

public enum RummagingCategory : int
{
    None = 0,
    Bush = 1,
    Rock = 2,
    UnderWater = 3,
    InTheGround = 4,
    HighAltitude = 5,
}

public enum RummagingPattern : int
{
    None = 0,
    Normal = 1,
    Nut = 2,
    Rare = 3,
}

public struct FixedSymbolAI : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;

    public static FixedSymbolAI GetRootAsFixedSymbolAI(ByteBuffer buffer)
    {
        return GetRootAsFixedSymbolAI(buffer, new FixedSymbolAI());
    }

    public static FixedSymbolAI GetRootAsFixedSymbolAI(ByteBuffer buffer, FixedSymbolAI obj)
    {
        return obj.__assign(buffer.GetInt(buffer.Position) + buffer.Position, buffer);
    }

    public void __init(int position, ByteBuffer buffer)
    {
        table = new Table(position, buffer);
    }

    public FixedSymbolAI __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    public int ActionId => GetInt(4);
    public float Hunger => GetFloat(6);
    public float Fatigue => GetFloat(8);
    public float Sleepiness => GetFloat(10);
    public int Priority => GetInt(12);
    public int TriggerActionId => GetInt(14);
    public BehaviorFrequency OverrideFrequency
    {
        get
        {
            var offset = table.__offset(16);
            return offset != 0
                ? (BehaviorFrequency)table.bb.GetInt(offset + table.bb_pos)
                : BehaviorFrequency.Every1Frame;
        }
    }

    public static Offset<FixedSymbolAI> CreateFixedSymbolAI(
        FlatBufferBuilder builder,
        int actionId = 0,
        float hunger = 0.0f,
        float fatigue = 0.0f,
        float sleepiness = 0.0f,
        int priority = 0,
        int triggerActionId = 0,
        BehaviorFrequency overrideFrequency = BehaviorFrequency.Every1Frame)
    {
        builder.StartTable(7);
        AddOverrideFrequency(builder, overrideFrequency);
        AddTriggerActionId(builder, triggerActionId);
        AddPriority(builder, priority);
        AddSleepiness(builder, sleepiness);
        AddFatigue(builder, fatigue);
        AddHunger(builder, hunger);
        AddActionId(builder, actionId);
        return EndFixedSymbolAI(builder);
    }

    public static void AddActionId(FlatBufferBuilder builder, int actionId) => builder.AddInt(0, actionId, 0);
    public static void AddHunger(FlatBufferBuilder builder, float hunger) => builder.AddFloat(1, hunger, 0.0f);
    public static void AddFatigue(FlatBufferBuilder builder, float fatigue) => builder.AddFloat(2, fatigue, 0.0f);
    public static void AddSleepiness(FlatBufferBuilder builder, float sleepiness) => builder.AddFloat(3, sleepiness, 0.0f);
    public static void AddPriority(FlatBufferBuilder builder, int priority) => builder.AddInt(4, priority, 0);
    public static void AddTriggerActionId(FlatBufferBuilder builder, int triggerActionId) => builder.AddInt(5, triggerActionId, 0);
    public static void AddOverrideFrequency(FlatBufferBuilder builder, BehaviorFrequency overrideFrequency) => builder.AddInt(6, (int)overrideFrequency, 0);

    public static Offset<FixedSymbolAI> EndFixedSymbolAI(FlatBufferBuilder builder)
    {
        return new Offset<FixedSymbolAI>(builder.EndTable());
    }

    private int GetInt(int vtableOffset)
    {
        var offset = table.__offset(vtableOffset);
        return offset != 0 ? table.bb.GetInt(offset + table.bb_pos) : 0;
    }

    private float GetFloat(int vtableOffset)
    {
        var offset = table.__offset(vtableOffset);
        return offset != 0 ? table.bb.GetFloat(offset + table.bb_pos) : 0.0f;
    }
}

public struct FixedSymbolGeneration : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;

    public static FixedSymbolGeneration GetRootAsFixedSymbolGeneration(ByteBuffer buffer)
    {
        return GetRootAsFixedSymbolGeneration(buffer, new FixedSymbolGeneration());
    }

    public static FixedSymbolGeneration GetRootAsFixedSymbolGeneration(ByteBuffer buffer, FixedSymbolGeneration obj)
    {
        return obj.__assign(buffer.GetInt(buffer.Position) + buffer.Position, buffer);
    }

    public void __init(int position, ByteBuffer buffer)
    {
        table = new Table(position, buffer);
    }

    public FixedSymbolGeneration __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    public float MinCreateDistance => GetFloat(4);
    public float MaxCreateDistance => GetFloat(6);
    public float MinDestroyDistance => GetFloat(8);
    public float MaxDestroyDistance => GetFloat(10);
    public GenerationPattern GenerationPattern
    {
        get
        {
            var offset = table.__offset(12);
            return offset != 0
                ? (GenerationPattern)table.bb.GetInt(offset + table.bb_pos)
                : GenerationPattern.Encount;
        }
    }
    public bool FirstGenerate
    {
        get
        {
            var offset = table.__offset(14);
            return offset != 0 && table.bb.Get(offset + table.bb_pos) != 0;
        }
    }
    public int RepopProbability => GetInt(16);
    public string? RequireScenarioId
    {
        get
        {
            var offset = table.__offset(18);
            return offset != 0 ? table.__string(offset + table.bb_pos) : null;
        }
    }

    public static Offset<FixedSymbolGeneration> CreateFixedSymbolGeneration(
        FlatBufferBuilder builder,
        float minCreateDistance = 0.0f,
        float maxCreateDistance = 0.0f,
        float minDestroyDistance = 0.0f,
        float maxDestroyDistance = 0.0f,
        GenerationPattern generationPattern = GenerationPattern.Encount,
        bool firstGenerate = false,
        int repopProbability = 0,
        StringOffset requireScenarioIdOffset = default)
    {
        builder.StartTable(8);
        AddRequireScenarioId(builder, requireScenarioIdOffset);
        AddRepopProbability(builder, repopProbability);
        AddGenerationPattern(builder, generationPattern);
        AddMaxDestroyDistance(builder, maxDestroyDistance);
        AddMinDestroyDistance(builder, minDestroyDistance);
        AddMaxCreateDistance(builder, maxCreateDistance);
        AddMinCreateDistance(builder, minCreateDistance);
        AddFirstGenerate(builder, firstGenerate);
        return EndFixedSymbolGeneration(builder);
    }

    public static void AddMinCreateDistance(FlatBufferBuilder builder, float value) => builder.AddFloat(0, value, 0.0f);
    public static void AddMaxCreateDistance(FlatBufferBuilder builder, float value) => builder.AddFloat(1, value, 0.0f);
    public static void AddMinDestroyDistance(FlatBufferBuilder builder, float value) => builder.AddFloat(2, value, 0.0f);
    public static void AddMaxDestroyDistance(FlatBufferBuilder builder, float value) => builder.AddFloat(3, value, 0.0f);
    public static void AddGenerationPattern(FlatBufferBuilder builder, GenerationPattern value) => builder.AddInt(4, (int)value, 0);
    public static void AddFirstGenerate(FlatBufferBuilder builder, bool value) => builder.AddBool(5, value, false);
    public static void AddRepopProbability(FlatBufferBuilder builder, int value) => builder.AddInt(6, value, 0);
    public static void AddRequireScenarioId(FlatBufferBuilder builder, StringOffset value) => builder.AddOffset(7, value.Value, 0);

    public static Offset<FixedSymbolGeneration> EndFixedSymbolGeneration(FlatBufferBuilder builder)
    {
        return new Offset<FixedSymbolGeneration>(builder.EndTable());
    }

    private int GetInt(int vtableOffset)
    {
        var offset = table.__offset(vtableOffset);
        return offset != 0 ? table.bb.GetInt(offset + table.bb_pos) : 0;
    }

    private float GetFloat(int vtableOffset)
    {
        var offset = table.__offset(vtableOffset);
        return offset != 0 ? table.bb.GetFloat(offset + table.bb_pos) : 0.0f;
    }
}

public struct FixedSymbolTable : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;

    public static FixedSymbolTable GetRootAsFixedSymbolTable(ByteBuffer buffer)
    {
        return GetRootAsFixedSymbolTable(buffer, new FixedSymbolTable());
    }

    public static FixedSymbolTable GetRootAsFixedSymbolTable(ByteBuffer buffer, FixedSymbolTable obj)
    {
        return obj.__assign(buffer.GetInt(buffer.Position) + buffer.Position, buffer);
    }

    public void __init(int position, ByteBuffer buffer)
    {
        table = new Table(position, buffer);
    }

    public FixedSymbolTable __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    public string? TableKey
    {
        get
        {
            var offset = table.__offset(4);
            return offset != 0 ? table.__string(offset + table.bb_pos) : null;
        }
    }

    public global::PokeDataSymbol? PokeDataSymbol
    {
        get
        {
            var offset = table.__offset(6);
            return offset != 0
                ? new global::PokeDataSymbol().__assign(table.__indirect(offset + table.bb_pos), table.bb)
                : null;
        }
    }

    public FixedSymbolAI? PokeAI
    {
        get
        {
            var offset = table.__offset(8);
            return offset != 0
                ? new FixedSymbolAI().__assign(table.__indirect(offset + table.bb_pos), table.bb)
                : null;
        }
    }

    public FixedSymbolGeneration? PokeGeneration
    {
        get
        {
            var offset = table.__offset(10);
            return offset != 0
                ? new FixedSymbolGeneration().__assign(table.__indirect(offset + table.bb_pos), table.bb)
                : null;
        }
    }

    public static Offset<FixedSymbolTable> CreateFixedSymbolTable(
        FlatBufferBuilder builder,
        StringOffset tableKeyOffset = default,
        Offset<global::PokeDataSymbol> pokeDataSymbolOffset = default,
        Offset<FixedSymbolAI> pokeAIOffset = default,
        Offset<FixedSymbolGeneration> pokeGenerationOffset = default)
    {
        builder.StartTable(4);
        AddPokeGeneration(builder, pokeGenerationOffset);
        AddPokeAI(builder, pokeAIOffset);
        AddPokeDataSymbol(builder, pokeDataSymbolOffset);
        AddTableKey(builder, tableKeyOffset);
        return EndFixedSymbolTable(builder);
    }

    public static void AddTableKey(FlatBufferBuilder builder, StringOffset value) => builder.AddOffset(0, value.Value, 0);
    public static void AddPokeDataSymbol(FlatBufferBuilder builder, Offset<global::PokeDataSymbol> value) => builder.AddOffset(1, value.Value, 0);
    public static void AddPokeAI(FlatBufferBuilder builder, Offset<FixedSymbolAI> value) => builder.AddOffset(2, value.Value, 0);
    public static void AddPokeGeneration(FlatBufferBuilder builder, Offset<FixedSymbolGeneration> value) => builder.AddOffset(3, value.Value, 0);

    public static Offset<FixedSymbolTable> EndFixedSymbolTable(FlatBufferBuilder builder)
    {
        return new Offset<FixedSymbolTable>(builder.EndTable());
    }
}

public struct FixedSymbolTableArray : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;

    public static FixedSymbolTableArray GetRootAsFixedSymbolTableArray(ByteBuffer buffer)
    {
        return GetRootAsFixedSymbolTableArray(buffer, new FixedSymbolTableArray());
    }

    public static FixedSymbolTableArray GetRootAsFixedSymbolTableArray(ByteBuffer buffer, FixedSymbolTableArray obj)
    {
        return obj.__assign(buffer.GetInt(buffer.Position) + buffer.Position, buffer);
    }

    public void __init(int position, ByteBuffer buffer)
    {
        table = new Table(position, buffer);
    }

    public FixedSymbolTableArray __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    public FixedSymbolTable? Values(int index)
    {
        var offset = table.__offset(4);
        return offset != 0
            ? new FixedSymbolTable().__assign(table.__indirect(table.__vector(offset) + index * 4), table.bb)
            : null;
    }

    public int ValuesLength
    {
        get
        {
            var offset = table.__offset(4);
            return offset != 0 ? table.__vector_len(offset) : 0;
        }
    }

    public static Offset<FixedSymbolTableArray> CreateFixedSymbolTableArray(
        FlatBufferBuilder builder,
        VectorOffset valuesOffset = default)
    {
        builder.StartTable(1);
        AddValues(builder, valuesOffset);
        return EndFixedSymbolTableArray(builder);
    }

    public static void AddValues(FlatBufferBuilder builder, VectorOffset value) => builder.AddOffset(0, value.Value, 0);
    public static VectorOffset CreateValuesVector(FlatBufferBuilder builder, Offset<FixedSymbolTable>[] data)
    {
        builder.StartVector(4, data.Length, 4);
        for (var index = data.Length - 1; index >= 0; index--)
        {
            builder.AddOffset(data[index].Value);
        }

        return builder.EndVector();
    }

    public static Offset<FixedSymbolTableArray> EndFixedSymbolTableArray(FlatBufferBuilder builder)
    {
        return new Offset<FixedSymbolTableArray>(builder.EndTable());
    }

    public static void FinishFixedSymbolTableArrayBuffer(
        FlatBufferBuilder builder,
        Offset<FixedSymbolTableArray> offset)
    {
        builder.Finish(offset.Value);
    }
}

public struct EventBattlePokemon : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;

    public static EventBattlePokemon GetRootAsEventBattlePokemon(ByteBuffer buffer)
    {
        return GetRootAsEventBattlePokemon(buffer, new EventBattlePokemon());
    }

    public static EventBattlePokemon GetRootAsEventBattlePokemon(ByteBuffer buffer, EventBattlePokemon obj)
    {
        return obj.__assign(buffer.GetInt(buffer.Position) + buffer.Position, buffer);
    }

    public void __init(int position, ByteBuffer buffer)
    {
        table = new Table(position, buffer);
    }

    public EventBattlePokemon __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    public string? Label
    {
        get
        {
            var offset = table.__offset(4);
            return offset != 0 ? table.__string(offset + table.bb_pos) : null;
        }
    }

    public global::PokeDataEventBattle? PokeData
    {
        get
        {
            var offset = table.__offset(6);
            return offset != 0
                ? new global::PokeDataEventBattle().__assign(table.__indirect(offset + table.bb_pos), table.bb)
                : null;
        }
    }

    public bool DisableBattleOut => GetBool(8);
    public bool EventEncount => GetBool(10);

    public static Offset<EventBattlePokemon> CreateEventBattlePokemon(
        FlatBufferBuilder builder,
        StringOffset labelOffset = default,
        Offset<global::PokeDataEventBattle> pokeDataOffset = default,
        bool disableBattleOut = false,
        bool eventEncount = false)
    {
        builder.StartTable(4);
        AddPokeData(builder, pokeDataOffset);
        AddLabel(builder, labelOffset);
        AddEventEncount(builder, eventEncount);
        AddDisableBattleOut(builder, disableBattleOut);
        return EndEventBattlePokemon(builder);
    }

    public static void AddLabel(FlatBufferBuilder builder, StringOffset value) => builder.AddOffset(0, value.Value, 0);
    public static void AddPokeData(FlatBufferBuilder builder, Offset<global::PokeDataEventBattle> value) => builder.AddOffset(1, value.Value, 0);
    public static void AddDisableBattleOut(FlatBufferBuilder builder, bool value) => builder.AddBool(2, value, false);
    public static void AddEventEncount(FlatBufferBuilder builder, bool value) => builder.AddBool(3, value, false);

    public static Offset<EventBattlePokemon> EndEventBattlePokemon(FlatBufferBuilder builder)
    {
        return new Offset<EventBattlePokemon>(builder.EndTable());
    }

    private bool GetBool(int vtableOffset)
    {
        var offset = table.__offset(vtableOffset);
        return offset != 0 && table.bb.Get(offset + table.bb_pos) != 0;
    }
}

public struct EventBattlePokemonArray : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;

    public static EventBattlePokemonArray GetRootAsEventBattlePokemonArray(ByteBuffer buffer)
    {
        return GetRootAsEventBattlePokemonArray(buffer, new EventBattlePokemonArray());
    }

    public static EventBattlePokemonArray GetRootAsEventBattlePokemonArray(ByteBuffer buffer, EventBattlePokemonArray obj)
    {
        return obj.__assign(buffer.GetInt(buffer.Position) + buffer.Position, buffer);
    }

    public void __init(int position, ByteBuffer buffer)
    {
        table = new Table(position, buffer);
    }

    public EventBattlePokemonArray __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    public EventBattlePokemon? Values(int index)
    {
        var offset = table.__offset(4);
        return offset != 0
            ? new EventBattlePokemon().__assign(table.__indirect(table.__vector(offset) + index * 4), table.bb)
            : null;
    }

    public int ValuesLength
    {
        get
        {
            var offset = table.__offset(4);
            return offset != 0 ? table.__vector_len(offset) : 0;
        }
    }

    public static Offset<EventBattlePokemonArray> CreateEventBattlePokemonArray(
        FlatBufferBuilder builder,
        VectorOffset valuesOffset = default)
    {
        builder.StartTable(1);
        AddValues(builder, valuesOffset);
        return EndEventBattlePokemonArray(builder);
    }

    public static void AddValues(FlatBufferBuilder builder, VectorOffset value) => builder.AddOffset(0, value.Value, 0);
    public static VectorOffset CreateValuesVector(FlatBufferBuilder builder, Offset<EventBattlePokemon>[] data)
    {
        builder.StartVector(4, data.Length, 4);
        for (var index = data.Length - 1; index >= 0; index--)
        {
            builder.AddOffset(data[index].Value);
        }

        return builder.EndVector();
    }

    public static Offset<EventBattlePokemonArray> EndEventBattlePokemonArray(FlatBufferBuilder builder)
    {
        return new Offset<EventBattlePokemonArray>(builder.EndTable());
    }

    public static void FinishEventBattlePokemonArrayBuffer(
        FlatBufferBuilder builder,
        Offset<EventBattlePokemonArray> offset)
    {
        builder.Finish(offset.Value);
    }
}

public struct HiddenItemDataTableInfo : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;

    public void __init(int position, ByteBuffer buffer)
    {
        table = new Table(position, buffer);
    }

    public HiddenItemDataTableInfo __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    public int ItemId => GetInt(4);
    public int EmergePercent => GetInt(6);
    public int DropCount => GetInt(8);

    public static Offset<HiddenItemDataTableInfo> CreateHiddenItemDataTableInfo(
        FlatBufferBuilder builder,
        int itemId = 0,
        int emergePercent = 0,
        int dropCount = 0)
    {
        builder.StartTable(3);
        AddDropCount(builder, dropCount);
        AddEmergePercent(builder, emergePercent);
        AddItemId(builder, itemId);
        return EndHiddenItemDataTableInfo(builder);
    }

    public static void AddItemId(FlatBufferBuilder builder, int value) => builder.AddInt(0, value, 0);
    public static void AddEmergePercent(FlatBufferBuilder builder, int value) => builder.AddInt(1, value, 0);
    public static void AddDropCount(FlatBufferBuilder builder, int value) => builder.AddInt(2, value, 0);

    public static Offset<HiddenItemDataTableInfo> EndHiddenItemDataTableInfo(FlatBufferBuilder builder)
    {
        return new Offset<HiddenItemDataTableInfo>(builder.EndTable());
    }

    private int GetInt(int vtableOffset)
    {
        var offset = table.__offset(vtableOffset);
        return offset != 0 ? table.bb.GetInt(offset + table.bb_pos) : 0;
    }
}

public struct HiddenItemDataTable : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;

    public static HiddenItemDataTable GetRootAsHiddenItemDataTable(ByteBuffer buffer)
    {
        return GetRootAsHiddenItemDataTable(buffer, new HiddenItemDataTable());
    }

    public static HiddenItemDataTable GetRootAsHiddenItemDataTable(ByteBuffer buffer, HiddenItemDataTable obj)
    {
        return obj.__assign(buffer.GetInt(buffer.Position) + buffer.Position, buffer);
    }

    public void __init(int position, ByteBuffer buffer)
    {
        table = new Table(position, buffer);
    }

    public HiddenItemDataTable __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    public string? TableId
    {
        get
        {
            var offset = table.__offset(4);
            return offset != 0 ? table.__string(offset + table.bb_pos) : null;
        }
    }

    public HiddenItemDataTableInfo? Item(int slot)
    {
        if (slot is < 0 or > 9)
        {
            return null;
        }

        var offset = table.__offset(6 + slot * 2);
        return offset != 0
            ? new HiddenItemDataTableInfo().__assign(table.__indirect(offset + table.bb_pos), table.bb)
            : null;
    }

    public static Offset<HiddenItemDataTable> CreateHiddenItemDataTable(
        FlatBufferBuilder builder,
        StringOffset tableIdOffset,
        IReadOnlyList<Offset<HiddenItemDataTableInfo>> itemOffsets)
    {
        builder.StartTable(11);
        for (var index = Math.Min(itemOffsets.Count, 10) - 1; index >= 0; index--)
        {
            builder.AddOffset(index + 1, itemOffsets[index].Value, 0);
        }

        builder.AddOffset(0, tableIdOffset.Value, 0);
        return new Offset<HiddenItemDataTable>(builder.EndTable());
    }
}

public struct HiddenItemDataTableArray : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;

    public static HiddenItemDataTableArray GetRootAsHiddenItemDataTableArray(ByteBuffer buffer)
    {
        return GetRootAsHiddenItemDataTableArray(buffer, new HiddenItemDataTableArray());
    }

    public static HiddenItemDataTableArray GetRootAsHiddenItemDataTableArray(ByteBuffer buffer, HiddenItemDataTableArray obj)
    {
        return obj.__assign(buffer.GetInt(buffer.Position) + buffer.Position, buffer);
    }

    public void __init(int position, ByteBuffer buffer)
    {
        table = new Table(position, buffer);
    }

    public HiddenItemDataTableArray __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    public HiddenItemDataTable? Values(int index)
    {
        var offset = table.__offset(4);
        return offset != 0
            ? new HiddenItemDataTable().__assign(table.__indirect(table.__vector(offset) + index * 4), table.bb)
            : null;
    }

    public int ValuesLength
    {
        get
        {
            var offset = table.__offset(4);
            return offset != 0 ? table.__vector_len(offset) : 0;
        }
    }

    public static Offset<HiddenItemDataTableArray> CreateHiddenItemDataTableArray(
        FlatBufferBuilder builder,
        VectorOffset valuesOffset = default)
    {
        builder.StartTable(1);
        builder.AddOffset(0, valuesOffset.Value, 0);
        return new Offset<HiddenItemDataTableArray>(builder.EndTable());
    }

    public static VectorOffset CreateValuesVector(FlatBufferBuilder builder, Offset<HiddenItemDataTable>[] data)
    {
        builder.StartVector(4, data.Length, 4);
        for (var index = data.Length - 1; index >= 0; index--)
        {
            builder.AddOffset(data[index].Value);
        }

        return builder.EndVector();
    }

    public static void FinishHiddenItemDataTableArrayBuffer(
        FlatBufferBuilder builder,
        Offset<HiddenItemDataTableArray> offset)
    {
        builder.Finish(offset.Value);
    }
}

public struct RummagingItemDataTable : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;

    public static RummagingItemDataTable GetRootAsRummagingItemDataTable(ByteBuffer buffer)
    {
        return GetRootAsRummagingItemDataTable(buffer, new RummagingItemDataTable());
    }

    public static RummagingItemDataTable GetRootAsRummagingItemDataTable(ByteBuffer buffer, RummagingItemDataTable obj)
    {
        return obj.__assign(buffer.GetInt(buffer.Position) + buffer.Position, buffer);
    }

    public void __init(int position, ByteBuffer buffer)
    {
        table = new Table(position, buffer);
    }

    public RummagingItemDataTable __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    public RummagingCategory Category => (RummagingCategory)GetInt(4);
    public RummagingPattern Pattern => (RummagingPattern)GetInt(6);
    public int Item00 => GetInt(8);
    public int Item01 => GetInt(10);
    public int Item02 => GetInt(12);
    public int Item03 => GetInt(14);
    public int Item04 => GetInt(16);

    public int Item(int slot)
    {
        return slot switch
        {
            0 => Item00,
            1 => Item01,
            2 => Item02,
            3 => Item03,
            4 => Item04,
            _ => 0,
        };
    }

    public static Offset<RummagingItemDataTable> CreateRummagingItemDataTable(
        FlatBufferBuilder builder,
        RummagingCategory category = RummagingCategory.None,
        RummagingPattern pattern = RummagingPattern.None,
        int item00 = 0,
        int item01 = 0,
        int item02 = 0,
        int item03 = 0,
        int item04 = 0)
    {
        builder.StartTable(7);
        builder.AddInt(6, item04, 0);
        builder.AddInt(5, item03, 0);
        builder.AddInt(4, item02, 0);
        builder.AddInt(3, item01, 0);
        builder.AddInt(2, item00, 0);
        builder.AddInt(1, (int)pattern, 0);
        builder.AddInt(0, (int)category, 0);
        return new Offset<RummagingItemDataTable>(builder.EndTable());
    }

    private int GetInt(int vtableOffset)
    {
        var offset = table.__offset(vtableOffset);
        return offset != 0 ? table.bb.GetInt(offset + table.bb_pos) : 0;
    }
}

public struct RummagingItemDataTableArray : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;

    public static RummagingItemDataTableArray GetRootAsRummagingItemDataTableArray(ByteBuffer buffer)
    {
        return GetRootAsRummagingItemDataTableArray(buffer, new RummagingItemDataTableArray());
    }

    public static RummagingItemDataTableArray GetRootAsRummagingItemDataTableArray(ByteBuffer buffer, RummagingItemDataTableArray obj)
    {
        return obj.__assign(buffer.GetInt(buffer.Position) + buffer.Position, buffer);
    }

    public void __init(int position, ByteBuffer buffer)
    {
        table = new Table(position, buffer);
    }

    public RummagingItemDataTableArray __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    public RummagingItemDataTable? Values(int index)
    {
        var offset = table.__offset(4);
        return offset != 0
            ? new RummagingItemDataTable().__assign(table.__indirect(table.__vector(offset) + index * 4), table.bb)
            : null;
    }

    public int ValuesLength
    {
        get
        {
            var offset = table.__offset(4);
            return offset != 0 ? table.__vector_len(offset) : 0;
        }
    }

    public static Offset<RummagingItemDataTableArray> CreateRummagingItemDataTableArray(
        FlatBufferBuilder builder,
        VectorOffset valuesOffset = default)
    {
        builder.StartTable(1);
        builder.AddOffset(0, valuesOffset.Value, 0);
        return new Offset<RummagingItemDataTableArray>(builder.EndTable());
    }

    public static VectorOffset CreateValuesVector(FlatBufferBuilder builder, Offset<RummagingItemDataTable>[] data)
    {
        builder.StartVector(4, data.Length, 4);
        for (var index = data.Length - 1; index >= 0; index--)
        {
            builder.AddOffset(data[index].Value);
        }

        return builder.EndVector();
    }

    public static void FinishRummagingItemDataTableArrayBuffer(
        FlatBufferBuilder builder,
        Offset<RummagingItemDataTableArray> offset)
    {
        builder.Finish(offset.Value);
    }
}
