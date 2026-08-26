// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;

namespace KM.Formats.ZA.Generated.TrainerPools;

public struct ZaTrainerPoolActivationParameter : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;
    public string? Condition => ReadString(4);
    public bool HasCondition => Has(4);
    public int Op => Has(6) ? table.bb.GetInt(table.bb_pos + table.__offset(6)) : 0;
    public bool HasOp => Has(6);
    public int ParametersLength => VectorLength(8);
    public bool HasParameters => Has(8);
    public int TablePosition => table.bb_pos;

    public string? Parameters(int index)
    {
        var offset = table.__offset(8);
        return offset == 0 ? null : table.__string(table.__vector(offset) + index * 4);
    }

    public void __init(int position, ByteBuffer buffer) => table = new Table(position, buffer);
    public ZaTrainerPoolActivationParameter __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    private bool Has(int slot) => table.__offset(slot) != 0;
    private string? ReadString(int slot)
    {
        var offset = table.__offset(slot);
        return offset == 0 ? null : table.__string(offset + table.bb_pos);
    }

    private int VectorLength(int slot)
    {
        var offset = table.__offset(slot);
        return offset == 0 ? 0 : table.__vector_len(offset);
    }
}

public struct ZaTrainerPoolActivationElement : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;
    public int ParametersLength => VectorLength(4);
    public bool HasParameters => table.__offset(4) != 0;
    public int TablePosition => table.bb_pos;

    public ZaTrainerPoolActivationParameter? Parameters(int index)
    {
        var offset = table.__offset(4);
        return offset == 0
            ? null
            : new ZaTrainerPoolActivationParameter().__assign(
                table.__indirect(table.__vector(offset) + index * 4),
                table.bb);
    }

    public void __init(int position, ByteBuffer buffer) => table = new Table(position, buffer);
    public ZaTrainerPoolActivationElement __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    private int VectorLength(int slot)
    {
        var offset = table.__offset(slot);
        return offset == 0 ? 0 : table.__vector_len(offset);
    }
}

public struct ZaTrainerPoolActivationCondition : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;
    public int ElementsLength => VectorLength(4);
    public bool HasElements => table.__offset(4) != 0;
    public int TablePosition => table.bb_pos;

    public ZaTrainerPoolActivationElement? Elements(int index)
    {
        var offset = table.__offset(4);
        return offset == 0
            ? null
            : new ZaTrainerPoolActivationElement().__assign(
                table.__indirect(table.__vector(offset) + index * 4),
                table.bb);
    }

    public void __init(int position, ByteBuffer buffer) => table = new Table(position, buffer);
    public ZaTrainerPoolActivationCondition __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    private int VectorLength(int slot)
    {
        var offset = table.__offset(slot);
        return offset == 0 ? 0 : table.__vector_len(offset);
    }
}

public struct ZaTrainerPoolAiInfo : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;
    public int ActionId => ReadInt(4);
    public bool HasActionId => Has(4);
    public string? PointName => ReadString(6);
    public bool HasPointName => Has(6);
    public string? ActorName => ReadString(8);
    public bool HasActorName => Has(8);
    public int CreateIgnoreFlagsLength => VectorLength(10);
    public bool HasCreateIgnoreFlags => Has(10);
    public float HomeRange => ReadFloat(12);
    public bool HasHomeRange => Has(12);
    public int PopActionId => ReadInt(14);
    public bool HasPopActionId => Has(14);
    public int TablePosition => table.bb_pos;

    public int CreateIgnoreFlags(int index)
    {
        var offset = table.__offset(10);
        return offset == 0 ? 0 : table.bb.GetInt(table.__vector(offset) + index * 4);
    }

    public void __init(int position, ByteBuffer buffer) => table = new Table(position, buffer);
    public ZaTrainerPoolAiInfo __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    private bool Has(int slot) => table.__offset(slot) != 0;
    private int ReadInt(int slot)
    {
        var offset = table.__offset(slot);
        return offset == 0 ? 0 : table.bb.GetInt(offset + table.bb_pos);
    }

    private float ReadFloat(int slot)
    {
        var offset = table.__offset(slot);
        return offset == 0 ? 0 : table.bb.GetFloat(offset + table.bb_pos);
    }

    private string? ReadString(int slot)
    {
        var offset = table.__offset(slot);
        return offset == 0 ? null : table.__string(offset + table.bb_pos);
    }

    private int VectorLength(int slot)
    {
        var offset = table.__offset(slot);
        return offset == 0 ? 0 : table.__vector_len(offset);
    }
}

public struct ZaTrainerPoolAppearance : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;
    public string? Id => ReadString(4);
    public bool HasId => Has(4);
    public string? TrainerId => ReadString(6);
    public bool HasTrainerId => Has(6);
    public int TagsLength => VectorLength(8);
    public bool HasTags => Has(8);
    public int Weight => ReadInt(10);
    public bool HasWeight => Has(10);
    public int ActivationConditionsLength => VectorLength(12);
    public bool HasActivationConditions => Has(12);
    public bool HasAiInfo => Has(14);
    public int TablePosition => table.bb_pos;

    public string? Tags(int index)
    {
        var offset = table.__offset(8);
        return offset == 0 ? null : table.__string(table.__vector(offset) + index * 4);
    }

    public ZaTrainerPoolActivationCondition? ActivationConditions(int index)
    {
        var offset = table.__offset(12);
        return offset == 0
            ? null
            : new ZaTrainerPoolActivationCondition().__assign(
                table.__indirect(table.__vector(offset) + index * 4),
                table.bb);
    }

    public ZaTrainerPoolAiInfo? AiInfo
    {
        get
        {
            var offset = table.__offset(14);
            return offset == 0
                ? null
                : new ZaTrainerPoolAiInfo().__assign(table.__indirect(offset + table.bb_pos), table.bb);
        }
    }

    public void __init(int position, ByteBuffer buffer) => table = new Table(position, buffer);
    public ZaTrainerPoolAppearance __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    private bool Has(int slot) => table.__offset(slot) != 0;
    private string? ReadString(int slot)
    {
        var offset = table.__offset(slot);
        return offset == 0 ? null : table.__string(offset + table.bb_pos);
    }

    private int ReadInt(int slot)
    {
        var offset = table.__offset(slot);
        return offset == 0 ? 0 : table.bb.GetInt(offset + table.bb_pos);
    }

    private int VectorLength(int slot)
    {
        var offset = table.__offset(slot);
        return offset == 0 ? 0 : table.__vector_len(offset);
    }
}

public struct ZaTrainerPoolTable : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;
    public string? Id => ReadString(4);
    public bool HasId => table.__offset(4) != 0;
    public int AppearancesLength => VectorLength(6);
    public bool HasAppearances => table.__offset(6) != 0;
    public int TablePosition => table.bb_pos;

    public ZaTrainerPoolAppearance? Appearances(int index)
    {
        var offset = table.__offset(6);
        return offset == 0
            ? null
            : new ZaTrainerPoolAppearance().__assign(
                table.__indirect(table.__vector(offset) + index * 4),
                table.bb);
    }

    public void __init(int position, ByteBuffer buffer) => table = new Table(position, buffer);
    public ZaTrainerPoolTable __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    private string? ReadString(int slot)
    {
        var offset = table.__offset(slot);
        return offset == 0 ? null : table.__string(offset + table.bb_pos);
    }

    private int VectorLength(int slot)
    {
        var offset = table.__offset(slot);
        return offset == 0 ? 0 : table.__vector_len(offset);
    }
}

public struct ZaTrainerPoolDatabaseGroup : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;
    public int TablesLength => VectorLength(4);
    public bool HasTables => table.__offset(4) != 0;
    public int TablePosition => table.bb_pos;

    public ZaTrainerPoolTable? Tables(int index)
    {
        var offset = table.__offset(4);
        return offset == 0
            ? null
            : new ZaTrainerPoolTable().__assign(
                table.__indirect(table.__vector(offset) + index * 4),
                table.bb);
    }

    public void __init(int position, ByteBuffer buffer) => table = new Table(position, buffer);
    public ZaTrainerPoolDatabaseGroup __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    private int VectorLength(int slot)
    {
        var offset = table.__offset(slot);
        return offset == 0 ? 0 : table.__vector_len(offset);
    }
}

public struct ZaTrainerPoolDatabaseArray : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;
    public int GroupsLength => VectorLength(4);
    public bool HasGroups => table.__offset(4) != 0;
    public int TablePosition => table.bb_pos;

    public static ZaTrainerPoolDatabaseArray GetRootAsZaTrainerPoolDatabaseArray(ByteBuffer buffer)
    {
        return new ZaTrainerPoolDatabaseArray().__assign(
            buffer.GetInt(buffer.Position) + buffer.Position,
            buffer);
    }

    public ZaTrainerPoolDatabaseGroup? Groups(int index)
    {
        var offset = table.__offset(4);
        return offset == 0
            ? null
            : new ZaTrainerPoolDatabaseGroup().__assign(
                table.__indirect(table.__vector(offset) + index * 4),
                table.bb);
    }

    public void __init(int position, ByteBuffer buffer) => table = new Table(position, buffer);
    public ZaTrainerPoolDatabaseArray __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    public static void FinishBuffer(FlatBufferBuilder builder, Offset<ZaTrainerPoolDatabaseArray> root)
    {
        builder.Finish(root.Value);
    }

    private int VectorLength(int slot)
    {
        var offset = table.__offset(slot);
        return offset == 0 ? 0 : table.__vector_len(offset);
    }
}

public struct ZaTrainerPoolIdentity : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;
    public string? TrainerId => ReadString(4);
    public string? AssetId => ReadString(6);
    public string? RosterId => ReadString(8);
    public int TablePosition => table.bb_pos;

    public void __init(int position, ByteBuffer buffer) => table = new Table(position, buffer);
    public ZaTrainerPoolIdentity __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    private string? ReadString(int slot)
    {
        var offset = table.__offset(slot);
        return offset == 0 ? null : table.__string(offset + table.bb_pos);
    }
}

public struct ZaTrainerPoolIdentityGroup : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;
    public int IdentitiesLength => VectorLength(4);
    public int TablePosition => table.bb_pos;

    public ZaTrainerPoolIdentity? Identities(int index)
    {
        var offset = table.__offset(4);
        return offset == 0
            ? null
            : new ZaTrainerPoolIdentity().__assign(
                table.__indirect(table.__vector(offset) + index * 4),
                table.bb);
    }

    public void __init(int position, ByteBuffer buffer) => table = new Table(position, buffer);
    public ZaTrainerPoolIdentityGroup __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    private int VectorLength(int slot)
    {
        var offset = table.__offset(slot);
        return offset == 0 ? 0 : table.__vector_len(offset);
    }
}

public struct ZaTrainerPoolIdentityDatabaseArray : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;
    public int GroupsLength => VectorLength(4);
    public int TablePosition => table.bb_pos;

    public static ZaTrainerPoolIdentityDatabaseArray GetRootAsZaTrainerPoolIdentityDatabaseArray(ByteBuffer buffer)
    {
        return new ZaTrainerPoolIdentityDatabaseArray().__assign(
            buffer.GetInt(buffer.Position) + buffer.Position,
            buffer);
    }

    public ZaTrainerPoolIdentityGroup? Groups(int index)
    {
        var offset = table.__offset(4);
        return offset == 0
            ? null
            : new ZaTrainerPoolIdentityGroup().__assign(
                table.__indirect(table.__vector(offset) + index * 4),
                table.bb);
    }

    public void __init(int position, ByteBuffer buffer) => table = new Table(position, buffer);
    public ZaTrainerPoolIdentityDatabaseArray __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    private int VectorLength(int slot)
    {
        var offset = table.__offset(slot);
        return offset == 0 ? 0 : table.__vector_len(offset);
    }
}

public struct ZaTrainerPoolSpawnerTableReference : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;
    public string? TableId => ReadString(4);
    public int TablePosition => table.bb_pos;

    public void __init(int position, ByteBuffer buffer) => table = new Table(position, buffer);
    public ZaTrainerPoolSpawnerTableReference __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    private string? ReadString(int slot)
    {
        var offset = table.__offset(slot);
        return offset == 0 ? null : table.__string(offset + table.bb_pos);
    }
}

public struct ZaTrainerPoolSpawner : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;
    public int TableReferencesLength => VectorLength(10);
    public int TablePosition => table.bb_pos;

    public ZaTrainerPoolSpawnerTableReference? TableReferences(int index)
    {
        var offset = table.__offset(10);
        return offset == 0
            ? null
            : new ZaTrainerPoolSpawnerTableReference().__assign(
                table.__indirect(table.__vector(offset) + index * 4),
                table.bb);
    }

    public void __init(int position, ByteBuffer buffer) => table = new Table(position, buffer);
    public ZaTrainerPoolSpawner __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    private int VectorLength(int slot)
    {
        var offset = table.__offset(slot);
        return offset == 0 ? 0 : table.__vector_len(offset);
    }
}

public struct ZaTrainerPoolSpawnerGroup : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;
    public int SpawnersLength => VectorLength(4);
    public int TablePosition => table.bb_pos;

    public ZaTrainerPoolSpawner? Spawners(int index)
    {
        var offset = table.__offset(4);
        return offset == 0
            ? null
            : new ZaTrainerPoolSpawner().__assign(
                table.__indirect(table.__vector(offset) + index * 4),
                table.bb);
    }

    public void __init(int position, ByteBuffer buffer) => table = new Table(position, buffer);
    public ZaTrainerPoolSpawnerGroup __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    private int VectorLength(int slot)
    {
        var offset = table.__offset(slot);
        return offset == 0 ? 0 : table.__vector_len(offset);
    }
}

public struct ZaTrainerPoolSpawnerDatabaseArray : IFlatbufferObject
{
    private Table table;

    public ByteBuffer ByteBuffer => table.bb;
    public int GroupsLength => VectorLength(4);
    internal int TablePosition => table.bb_pos;

    public static ZaTrainerPoolSpawnerDatabaseArray GetRootAsZaTrainerPoolSpawnerDatabaseArray(ByteBuffer buffer)
    {
        return new ZaTrainerPoolSpawnerDatabaseArray().__assign(
            buffer.GetInt(buffer.Position) + buffer.Position,
            buffer);
    }

    public ZaTrainerPoolSpawnerGroup? Groups(int index)
    {
        var offset = table.__offset(4);
        return offset == 0
            ? null
            : new ZaTrainerPoolSpawnerGroup().__assign(
                table.__indirect(table.__vector(offset) + index * 4),
                table.bb);
    }

    public void __init(int position, ByteBuffer buffer) => table = new Table(position, buffer);
    public ZaTrainerPoolSpawnerDatabaseArray __assign(int position, ByteBuffer buffer)
    {
        __init(position, buffer);
        return this;
    }

    private int VectorLength(int slot)
    {
        var offset = table.__offset(slot);
        return offset == 0 ? 0 : table.__vector_len(offset);
    }
}
