// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;

namespace KM.ZA.Placement;

internal sealed class ZaSpawnerTransformDocument
{
    private ZaSpawnerTransformDocument(IReadOnlyList<ZaSpawnerTransformGroup> groups)
    {
        Groups = groups;
    }

    public IReadOnlyList<ZaSpawnerTransformGroup> Groups { get; }

    internal static ZaSpawnerTransformDocument Create(IReadOnlyList<ZaSpawnerTransformGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        return new ZaSpawnerTransformDocument(groups);
    }

    public static ZaSpawnerTransformDocument Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var root = SpawnerTransformDataDBArray.GetRootAsSpawnerTransformDataDBArray(new ByteBuffer(bytes));
        var groups = new List<ZaSpawnerTransformGroup>();
        for (var groupIndex = 0; groupIndex < root.ValuesLength; groupIndex++)
        {
            var db = root.Values(groupIndex);
            if (db is null)
            {
                groups.Add(new ZaSpawnerTransformGroup(groupIndex, []));
                continue;
            }

            var rows = new List<ZaSpawnerTransformRow>();
            for (var rowIndex = 0; rowIndex < db.Value.RootLength; rowIndex++)
            {
                var row = db.Value.Root(rowIndex);
                if (row is null)
                {
                    continue;
                }

                rows.Add(new ZaSpawnerTransformRow(
                    groupIndex,
                    rowIndex,
                    row.Value.Name ?? string.Empty,
                    ToVector(row.Value.Position),
                    ToVector(row.Value.Rotation),
                    row.Value.AttachTransformEnable));
            }

            groups.Add(new ZaSpawnerTransformGroup(groupIndex, rows));
        }

        return new ZaSpawnerTransformDocument(groups);
    }

    public byte[] Write()
    {
        var builder = new FlatBufferBuilder(1024);
        var groupOffsets = Groups
            .Select(group =>
            {
                var rowOffsets = group.Rows.Select(row => WriteRow(builder, row)).ToArray();
                var rowsVector = SpawnerTransformDataDB.CreateRootVector(builder, rowOffsets);
                return SpawnerTransformDataDB.CreateSpawnerTransformDataDB(builder, rowsVector);
            })
            .ToArray();
        var groupsVector = SpawnerTransformDataDBArray.CreateValuesVector(builder, groupOffsets);
        var root = SpawnerTransformDataDBArray.CreateSpawnerTransformDataDBArray(builder, groupsVector);
        SpawnerTransformDataDBArray.FinishSpawnerTransformDataDBArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static Offset<SpawnerTransformData> WriteRow(
        FlatBufferBuilder builder,
        ZaSpawnerTransformRow row)
    {
        var name = builder.CreateString(row.Name);
        var position = WriteVector(builder, row.Position);
        var rotation = WriteVector(builder, row.Rotation);
        return SpawnerTransformData.CreateSpawnerTransformData(
            builder,
            name,
            position,
            rotation,
            row.AttachTransformEnable);
    }

    private static Offset<SpawnerTransformInfo> WriteVector(
        FlatBufferBuilder builder,
        ZaSpawnerTransformVector value)
    {
        return SpawnerTransformInfo.CreateSpawnerTransformInfo(builder, value.X, value.Y, value.Z);
    }

    private static ZaSpawnerTransformVector ToVector(SpawnerTransformInfo? value)
    {
        return value is null
            ? new ZaSpawnerTransformVector(0, 0, 0)
            : new ZaSpawnerTransformVector(value.Value.X, value.Value.Y, value.Value.Z);
    }

    private struct SpawnerTransformDataDBArray : IFlatbufferObject
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

        public static SpawnerTransformDataDBArray GetRootAsSpawnerTransformDataDBArray(ByteBuffer buffer)
        {
            return GetRootAsSpawnerTransformDataDBArray(buffer, new SpawnerTransformDataDBArray());
        }

        public static SpawnerTransformDataDBArray GetRootAsSpawnerTransformDataDBArray(
            ByteBuffer buffer,
            SpawnerTransformDataDBArray value)
        {
            return value.__assign(buffer.GetInt(buffer.Position) + buffer.Position, buffer);
        }

        public SpawnerTransformDataDBArray __assign(int offset, ByteBuffer buffer)
        {
            __init(offset, buffer);
            return this;
        }

        public void __init(int offset, ByteBuffer buffer)
        {
            table = new Table(offset, buffer);
        }

        public SpawnerTransformDataDB? Values(int index)
        {
            var offset = table.__offset(4);
            return offset == 0
                ? null
                : new SpawnerTransformDataDB().__assign(table.__indirect(table.__vector(offset) + (index * 4)), table.bb);
        }

        public static Offset<SpawnerTransformDataDBArray> CreateSpawnerTransformDataDBArray(
            FlatBufferBuilder builder,
            VectorOffset valuesOffset = default)
        {
            builder.StartTable(1);
            AddValues(builder, valuesOffset);
            return EndSpawnerTransformDataDBArray(builder);
        }

        public static void AddValues(FlatBufferBuilder builder, VectorOffset valuesOffset)
        {
            builder.AddOffset(0, valuesOffset.Value, 0);
        }

        public static VectorOffset CreateValuesVector(
            FlatBufferBuilder builder,
            Offset<SpawnerTransformDataDB>[] data)
        {
            builder.StartVector(4, data.Length, 4);
            for (var index = data.Length - 1; index >= 0; index--)
            {
                builder.AddOffset(data[index].Value);
            }

            return builder.EndVector();
        }

        public static Offset<SpawnerTransformDataDBArray> EndSpawnerTransformDataDBArray(FlatBufferBuilder builder)
        {
            return new Offset<SpawnerTransformDataDBArray>(builder.EndTable());
        }

        public static void FinishSpawnerTransformDataDBArrayBuffer(
            FlatBufferBuilder builder,
            Offset<SpawnerTransformDataDBArray> offset)
        {
            builder.Finish(offset.Value);
        }
    }

    private struct SpawnerTransformDataDB : IFlatbufferObject
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

        public SpawnerTransformDataDB __assign(int offset, ByteBuffer buffer)
        {
            __init(offset, buffer);
            return this;
        }

        public void __init(int offset, ByteBuffer buffer)
        {
            table = new Table(offset, buffer);
        }

        public SpawnerTransformData? Root(int index)
        {
            var offset = table.__offset(4);
            return offset == 0
                ? null
                : new SpawnerTransformData().__assign(table.__indirect(table.__vector(offset) + (index * 4)), table.bb);
        }

        public static Offset<SpawnerTransformDataDB> CreateSpawnerTransformDataDB(
            FlatBufferBuilder builder,
            VectorOffset rootOffset = default)
        {
            builder.StartTable(1);
            AddRoot(builder, rootOffset);
            return EndSpawnerTransformDataDB(builder);
        }

        public static void AddRoot(FlatBufferBuilder builder, VectorOffset rootOffset)
        {
            builder.AddOffset(0, rootOffset.Value, 0);
        }

        public static VectorOffset CreateRootVector(
            FlatBufferBuilder builder,
            Offset<SpawnerTransformData>[] data)
        {
            builder.StartVector(4, data.Length, 4);
            for (var index = data.Length - 1; index >= 0; index--)
            {
                builder.AddOffset(data[index].Value);
            }

            return builder.EndVector();
        }

        public static Offset<SpawnerTransformDataDB> EndSpawnerTransformDataDB(FlatBufferBuilder builder)
        {
            return new Offset<SpawnerTransformDataDB>(builder.EndTable());
        }
    }

    private struct SpawnerTransformData : IFlatbufferObject
    {
        private Table table;

        public ByteBuffer ByteBuffer => table.bb;

        public string? Name
        {
            get
            {
                var offset = table.__offset(4);
                return offset == 0 ? null : table.__string(offset + table.bb_pos);
            }
        }

        public SpawnerTransformInfo? Position
        {
            get
            {
                var offset = table.__offset(6);
                return offset == 0
                    ? null
                    : new SpawnerTransformInfo().__assign(table.__indirect(offset + table.bb_pos), table.bb);
            }
        }

        public SpawnerTransformInfo? Rotation
        {
            get
            {
                var offset = table.__offset(8);
                return offset == 0
                    ? null
                    : new SpawnerTransformInfo().__assign(table.__indirect(offset + table.bb_pos), table.bb);
            }
        }

        public bool AttachTransformEnable
        {
            get
            {
                var offset = table.__offset(10);
                return offset != 0 && table.bb.Get(offset + table.bb_pos) != 0;
            }
        }

        public SpawnerTransformData __assign(int offset, ByteBuffer buffer)
        {
            __init(offset, buffer);
            return this;
        }

        public void __init(int offset, ByteBuffer buffer)
        {
            table = new Table(offset, buffer);
        }

        public static Offset<SpawnerTransformData> CreateSpawnerTransformData(
            FlatBufferBuilder builder,
            StringOffset nameOffset = default,
            Offset<SpawnerTransformInfo> positionOffset = default,
            Offset<SpawnerTransformInfo> rotationOffset = default,
            bool attachTransformEnable = false)
        {
            builder.StartTable(4);
            AddRotation(builder, rotationOffset);
            AddPosition(builder, positionOffset);
            AddName(builder, nameOffset);
            AddAttachTransformEnable(builder, attachTransformEnable);
            return EndSpawnerTransformData(builder);
        }

        public static void AddName(FlatBufferBuilder builder, StringOffset nameOffset)
        {
            builder.AddOffset(0, nameOffset.Value, 0);
        }

        public static void AddPosition(FlatBufferBuilder builder, Offset<SpawnerTransformInfo> positionOffset)
        {
            builder.AddOffset(1, positionOffset.Value, 0);
        }

        public static void AddRotation(FlatBufferBuilder builder, Offset<SpawnerTransformInfo> rotationOffset)
        {
            builder.AddOffset(2, rotationOffset.Value, 0);
        }

        public static void AddAttachTransformEnable(FlatBufferBuilder builder, bool attachTransformEnable)
        {
            builder.AddBool(3, attachTransformEnable, false);
        }

        public static Offset<SpawnerTransformData> EndSpawnerTransformData(FlatBufferBuilder builder)
        {
            return new Offset<SpawnerTransformData>(builder.EndTable());
        }
    }

    private struct SpawnerTransformInfo : IFlatbufferObject
    {
        private Table table;

        public ByteBuffer ByteBuffer => table.bb;

        public float X
        {
            get
            {
                var offset = table.__offset(4);
                return offset == 0 ? 0 : table.bb.GetFloat(offset + table.bb_pos);
            }
        }

        public float Y
        {
            get
            {
                var offset = table.__offset(6);
                return offset == 0 ? 0 : table.bb.GetFloat(offset + table.bb_pos);
            }
        }

        public float Z
        {
            get
            {
                var offset = table.__offset(8);
                return offset == 0 ? 0 : table.bb.GetFloat(offset + table.bb_pos);
            }
        }

        public SpawnerTransformInfo __assign(int offset, ByteBuffer buffer)
        {
            __init(offset, buffer);
            return this;
        }

        public void __init(int offset, ByteBuffer buffer)
        {
            table = new Table(offset, buffer);
        }

        public static Offset<SpawnerTransformInfo> CreateSpawnerTransformInfo(
            FlatBufferBuilder builder,
            float x = 0,
            float y = 0,
            float z = 0)
        {
            builder.StartTable(3);
            AddZ(builder, z);
            AddY(builder, y);
            AddX(builder, x);
            return EndSpawnerTransformInfo(builder);
        }

        public static void AddX(FlatBufferBuilder builder, float x)
        {
            builder.AddFloat(0, x, 0);
        }

        public static void AddY(FlatBufferBuilder builder, float y)
        {
            builder.AddFloat(1, y, 0);
        }

        public static void AddZ(FlatBufferBuilder builder, float z)
        {
            builder.AddFloat(2, z, 0);
        }

        public static Offset<SpawnerTransformInfo> EndSpawnerTransformInfo(FlatBufferBuilder builder)
        {
            return new Offset<SpawnerTransformInfo>(builder.EndTable());
        }
    }
}

internal sealed record ZaSpawnerTransformGroup(
    int GroupIndex,
    IReadOnlyList<ZaSpawnerTransformRow> Rows);

internal sealed class ZaSpawnerTransformRow
{
    public ZaSpawnerTransformRow(
        int groupIndex,
        int rowIndex,
        string name,
        ZaSpawnerTransformVector position,
        ZaSpawnerTransformVector rotation,
        bool attachTransformEnable)
    {
        GroupIndex = groupIndex;
        RowIndex = rowIndex;
        Name = name;
        Position = position;
        Rotation = rotation;
        AttachTransformEnable = attachTransformEnable;
    }

    public int GroupIndex { get; }

    public int RowIndex { get; }

    public string Name { get; set; }

    public ZaSpawnerTransformVector Position { get; set; }

    public ZaSpawnerTransformVector Rotation { get; set; }

    public bool AttachTransformEnable { get; set; }
}

internal sealed record ZaSpawnerTransformVector(
    float X,
    float Y,
    float Z);
