// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace KM.Formats.SwSh;

public sealed record SwShPlacementTransform(
    float X,
    float Y,
    float Z,
    float RotationY);

public enum SwShPlacementObjectKind
{
    FieldItem,
    HiddenItem,
}

public enum SwShPlacementEditableField
{
    LocationX,
    LocationY,
    LocationZ,
    RotationY,
    ItemId,
    Quantity,
    Chance,
}

public sealed record SwShPlacementObjectEdit(
    int ZoneIndex,
    SwShPlacementObjectKind ObjectKind,
    int ObjectIndex,
    int? ChanceIndex,
    SwShPlacementEditableField Field,
    double Value,
    ulong? HashValue = null);

public sealed record SwShPlacementRawFieldEdit(
    int ZoneIndex,
    string ObjectType,
    int ObjectIndex,
    string Field,
    string Value);

public sealed record SwShPlacementHiddenItemChance(
    int ChanceIndex,
    ulong ItemHash,
    int? ItemId,
    int Chance,
    int Quantity,
    int ItemHashOffset,
    int ChanceOffset,
    int QuantityOffset);

public sealed record SwShPlacementFieldItem(
    int ObjectIndex,
    string Model,
    SwShPlacementTransform Transform,
    IReadOnlyList<ulong> ItemHashes,
    IReadOnlyList<int> ItemHashOffsets,
    IReadOnlyList<uint> ItemIds,
    IReadOnlyList<int> ItemIdOffsets,
    byte Quantity,
    int QuantityOffset,
    PlacementTransformOffsets TransformOffsets);

public sealed record SwShPlacementHiddenItem(
    int ObjectIndex,
    SwShPlacementTransform Transform,
    IReadOnlyList<SwShPlacementHiddenItemChance> Chances,
    PlacementTransformOffsets TransformOffsets);

public sealed record SwShPlacementRawField(
    string Field,
    string Label,
    string Group,
    string Value,
    string DisplayValue,
    bool IsReadOnly,
    string ValueKind,
    double MinimumValue,
    double MaximumValue,
    string Description,
    string StorageKind = "",
    int ValueOffset = 0,
    int StringByteCapacity = 0,
    int StringLengthOffset = 0,
    string TableName = "",
    int TableOffset = 0,
    int TableReferenceOffset = 0,
    int FieldIndex = -1,
    bool CanRewriteTable = false);

public sealed record SwShPlacementRawObject(
    string ObjectType,
    int ObjectIndex,
    SwShPlacementTransform Transform,
    ulong ObjectHash,
    string PrimaryLabel,
    string LinkValue,
    IReadOnlyList<SwShPlacementRawField> Fields);

public sealed record SwShPlacementZone(
    int ZoneIndex,
    ulong ZoneId,
    ulong ObjectHash,
    SwShPlacementTransform Transform,
    IReadOnlyList<SwShPlacementFieldItem> FieldItems,
    IReadOnlyList<SwShPlacementHiddenItem> HiddenItems)
{
    public IReadOnlyList<SwShPlacementRawObject> RawObjects { get; init; } = Array.Empty<SwShPlacementRawObject>();
}

public sealed record SwShPlacementZoneArchive(
    IReadOnlyList<SwShPlacementZone> Zones,
    ulong Hash,
    string Description,
    byte[] SourceData)
{
    public const ulong EmptyFnvHash = 0xCBF29CE484222645;

    private const string RawStorageBool = "bool";
    private const string RawStorageFloat = "float";
    private const string RawStorageInt = "int";
    private const string RawStorageString = "string";
    private const string RawStorageUByte = "ubyte";
    private const string RawStorageUInt = "uint";
    private const string RawStorageULong = "ulong";
    private const double MinimumRawFloatValue = -1_000_000;
    private const double MaximumRawFloatValue = 1_000_000;

    public static SwShPlacementZoneArchive Parse(ReadOnlySpan<byte> data, IReadOnlyDictionary<ulong, int>? itemIdsByHash = null)
    {
        if (data.Length < sizeof(uint))
        {
            throw new InvalidDataException("Placement archive is too small to contain a FlatBuffer root.");
        }

        var rootTableOffset = ReadUOffset(data, offset: 0);
        var zoneVectorOffset = ReadTableUOffset(data, rootTableOffset, fieldIndex: 0, required: true);
        var zones = ReadZoneVector(data, zoneVectorOffset, itemIdsByHash);
        var hash = ReadTableUInt64(data, rootTableOffset, fieldIndex: 1, required: false);
        var descriptionOffset = ReadTableUOffset(data, rootTableOffset, fieldIndex: 2, required: false);
        var description = descriptionOffset == 0 ? string.Empty : ReadString(data, descriptionOffset);

        return new SwShPlacementZoneArchive(zones, hash, description, data.ToArray());
    }

    public byte[] Write()
    {
        var writer = new PlacementFlatBufferWriter();
        return writer.Write(this);
    }

    public byte[] WriteEdits(
        IEnumerable<SwShPlacementObjectEdit> edits,
        IEnumerable<SwShPlacementRawFieldEdit>? rawFieldEdits = null)
    {
        ArgumentNullException.ThrowIfNull(edits);

        var output = SourceData.ToArray();
        foreach (var edit in edits)
        {
            ApplyEdit(output, edit);
        }

        if (rawFieldEdits is not null)
        {
            foreach (var edit in rawFieldEdits)
            {
                output = ApplyRawFieldEdit(output, edit);
            }
        }

        return output;
    }

    private void ApplyEdit(byte[] output, SwShPlacementObjectEdit edit)
    {
        if ((uint)edit.ZoneIndex >= (uint)Zones.Count)
        {
            throw new InvalidDataException($"Placement zone index {edit.ZoneIndex} is not present.");
        }

        var zone = Zones[edit.ZoneIndex];
        switch (edit.ObjectKind)
        {
            case SwShPlacementObjectKind.FieldItem:
                ApplyFieldItemEdit(output, zone, edit);
                break;
            case SwShPlacementObjectKind.HiddenItem:
                ApplyHiddenItemEdit(output, zone, edit);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(edit), $"Placement object kind '{edit.ObjectKind}' is not supported.");
        }
    }

    private byte[] ApplyRawFieldEdit(byte[] output, SwShPlacementRawFieldEdit edit)
    {
        if ((uint)edit.ZoneIndex >= (uint)Zones.Count)
        {
            throw new InvalidDataException($"Placement zone index {edit.ZoneIndex} is not present.");
        }

        var rawObject = Zones[edit.ZoneIndex].RawObjects.FirstOrDefault(candidate =>
            candidate.ObjectIndex == edit.ObjectIndex
            && string.Equals(candidate.ObjectType, edit.ObjectType, StringComparison.Ordinal));
        if (rawObject is null)
        {
            throw new InvalidDataException($"Placement raw object '{edit.ObjectType}' index {edit.ObjectIndex} is not present.");
        }

        var field = rawObject.Fields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, edit.Field, StringComparison.Ordinal));
        if (field is null)
        {
            throw new InvalidDataException($"Placement raw field '{edit.Field}' is not present.");
        }

        if (field.IsReadOnly)
        {
            throw new InvalidDataException($"Placement raw field '{edit.Field}' is structural or absent and cannot be patched in place.");
        }

        field = ResolveCurrentRawFieldTarget(output, field);
        if (field.ValueOffset <= 0 && field.CanRewriteTable)
        {
            return RewriteRawScalarTable(output, field, edit.Value);
        }

        switch (field.StorageKind)
        {
            case RawStorageFloat:
                WriteSingle(output, field.ValueOffset, ParseDouble(edit.Value, edit.Field));
                break;
            case RawStorageInt:
                WriteInt32(output, field.ValueOffset, ParseDouble(edit.Value, edit.Field), int.MinValue, int.MaxValue);
                break;
            case RawStorageUInt:
                WriteUInt32(output, field.ValueOffset, ParseUInt32(edit.Value, edit.Field));
                break;
            case RawStorageUByte:
                WriteByte(output, field.ValueOffset, ParseDouble(edit.Value, edit.Field), byte.MinValue, byte.MaxValue);
                break;
            case RawStorageBool:
                WriteByte(output, field.ValueOffset, ParseBool(edit.Value, edit.Field) ? 1 : 0, byte.MinValue, byte.MaxValue);
                break;
            case RawStorageULong:
                WriteUInt64(output, field.ValueOffset, ParseUInt64(edit.Value, edit.Field));
                break;
            case RawStorageString:
                WriteStringInPlace(output, field, edit.Value);
                break;
            default:
                throw new InvalidDataException($"Placement raw field '{edit.Field}' does not have a supported storage kind.");
        }

        return output;
    }

    private static SwShPlacementRawField ResolveCurrentRawFieldTarget(byte[] output, SwShPlacementRawField field)
    {
        if (field.TableReferenceOffset <= 0 || field.TableOffset <= 0 || field.FieldIndex < 0)
        {
            return field;
        }

        var currentTableOffset = ReadUOffset(output, field.TableReferenceOffset);
        if (currentTableOffset == field.TableOffset)
        {
            return field;
        }

        return field with
        {
            TableOffset = currentTableOffset,
            ValueOffset = ReadTableFieldAbsoluteOffset(output, currentTableOffset, field.FieldIndex),
        };
    }

    private static byte[] RewriteRawScalarTable(byte[] output, SwShPlacementRawField field, string value)
    {
        if (field.TableReferenceOffset <= 0 || field.TableOffset <= 0 || field.FieldIndex < 0)
        {
            throw new InvalidDataException($"Placement raw field '{field.Field}' does not have a writable parent table reference.");
        }

        if (!PlacementRawCatalog.Tables.TryGetValue(field.TableName, out var table)
            || table.Fields.Any(candidate => !IsRawScalarFieldKind(candidate.Kind)))
        {
            throw new InvalidDataException($"Placement raw field '{field.Field}' cannot rewrite table '{field.TableName}'.");
        }

        var values = new List<RawScalarTableValue>();
        foreach (var spec in table.Fields.OrderByDescending(candidate => candidate.FieldIndex))
        {
            var offset = ReadTableFieldAbsoluteOffset(output, field.TableOffset, spec.FieldIndex);
            if (spec.FieldIndex == field.FieldIndex)
            {
                values.Add(CreateRawScalarTableValue(spec, value));
                continue;
            }

            if (offset != 0)
            {
                values.Add(ReadRawScalarTableValue(output, spec, offset));
            }
        }

        var newTableOffset = AppendRawScalarTable(ref output, table, values);
        WriteUInt32(output, field.TableReferenceOffset, checked((uint)(newTableOffset - field.TableReferenceOffset)));
        return output;
    }

    private static RawScalarTableValue CreateRawScalarTableValue(PlacementRawFieldSpec spec, string value)
    {
        return spec.Kind switch
        {
            PlacementRawFieldKind.Bool => new RawScalarTableValue(spec.FieldIndex, sizeof(byte), [ParseBool(value, spec.Name) ? (byte)1 : (byte)0]),
            PlacementRawFieldKind.Float => new RawScalarTableValue(spec.FieldIndex, sizeof(float), BitConverter.GetBytes((float)ParseDouble(value, spec.Name))),
            PlacementRawFieldKind.Int => CreateRawInt32TableValue(spec, value),
            PlacementRawFieldKind.UByte => CreateRawByteTableValue(spec, value),
            PlacementRawFieldKind.UInt => new RawScalarTableValue(spec.FieldIndex, sizeof(uint), BitConverter.GetBytes(ParseUInt32(value, spec.Name))),
            PlacementRawFieldKind.ULong => new RawScalarTableValue(spec.FieldIndex, sizeof(ulong), BitConverter.GetBytes(ParseUInt64(value, spec.Name))),
            _ => throw new InvalidDataException($"Placement raw field '{spec.Name}' is not a scalar field."),
        };
    }

    private static RawScalarTableValue CreateRawInt32TableValue(PlacementRawFieldSpec spec, string value)
    {
        var parsed = ParseDouble(value, spec.Name);
        EnsureInteger(parsed, int.MinValue, int.MaxValue);
        return new RawScalarTableValue(spec.FieldIndex, sizeof(int), BitConverter.GetBytes((int)parsed));
    }

    private static RawScalarTableValue CreateRawByteTableValue(PlacementRawFieldSpec spec, string value)
    {
        var parsed = ParseDouble(value, spec.Name);
        EnsureInteger(parsed, byte.MinValue, byte.MaxValue);
        return new RawScalarTableValue(spec.FieldIndex, sizeof(byte), [(byte)parsed]);
    }

    private static RawScalarTableValue ReadRawScalarTableValue(ReadOnlySpan<byte> data, PlacementRawFieldSpec spec, int offset)
    {
        var size = GetRawScalarSize(spec.Kind);
        EnsureRange(data, offset, size);
        return new RawScalarTableValue(spec.FieldIndex, size, data.Slice(offset, size).ToArray());
    }

    private static int AppendRawScalarTable(
        ref byte[] output,
        PlacementRawTableSpec table,
        IReadOnlyList<RawScalarTableValue> values)
    {
        var fieldCount = table.Fields.Max(field => field.FieldIndex) + 1;
        var fieldOffsets = new ushort[fieldCount];
        var objectBytes = new List<byte> { 0, 0, 0, 0 };
        var maxAlignment = 1;

        foreach (var value in values)
        {
            maxAlignment = Math.Max(maxAlignment, value.Alignment);
            Align(objectBytes, value.Alignment);
            fieldOffsets[value.FieldIndex] = checked((ushort)objectBytes.Count);
            objectBytes.AddRange(value.Bytes);
        }

        Align(objectBytes, maxAlignment);
        var vtableSize = checked(sizeof(ushort) * (2 + fieldCount));
        var paddingLength = GetPaddingLength(output.Length + vtableSize, maxAlignment);
        var vtableOffset = output.Length + paddingLength;
        var tableOffset = vtableOffset + vtableSize;
        var nextLength = tableOffset + objectBytes.Count;
        Array.Resize(ref output, nextLength);
        output.AsSpan(vtableOffset - paddingLength, paddingLength).Clear();

        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(vtableOffset, sizeof(ushort)), checked((ushort)vtableSize));
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(vtableOffset + sizeof(ushort), sizeof(ushort)), checked((ushort)objectBytes.Count));
        for (var index = 0; index < fieldOffsets.Length; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(vtableOffset + (sizeof(ushort) * (2 + index)), sizeof(ushort)), fieldOffsets[index]);
        }

        objectBytes.ToArray().CopyTo(output.AsSpan(tableOffset));
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(tableOffset, sizeof(int)), tableOffset - vtableOffset);
        return tableOffset;
    }

    private static int GetRawScalarSize(PlacementRawFieldKind kind)
    {
        return kind switch
        {
            PlacementRawFieldKind.Bool => sizeof(byte),
            PlacementRawFieldKind.Float => sizeof(float),
            PlacementRawFieldKind.Int => sizeof(int),
            PlacementRawFieldKind.UByte => sizeof(byte),
            PlacementRawFieldKind.UInt => sizeof(uint),
            PlacementRawFieldKind.ULong => sizeof(ulong),
            _ => throw new InvalidDataException($"Placement raw field kind '{kind}' is not scalar."),
        };
    }

    private static void Align(List<byte> bytes, int alignment)
    {
        var padding = GetPaddingLength(bytes.Count, alignment);
        for (var index = 0; index < padding; index++)
        {
            bytes.Add(0);
        }
    }

    private static int GetPaddingLength(int value, int alignment)
    {
        var remainder = value % alignment;
        return remainder == 0 ? 0 : alignment - remainder;
    }

    private static void ApplyFieldItemEdit(byte[] output, SwShPlacementZone zone, SwShPlacementObjectEdit edit)
    {
        if ((uint)edit.ObjectIndex >= (uint)zone.FieldItems.Count)
        {
            throw new InvalidDataException($"Placement field item index {edit.ObjectIndex} is not present.");
        }

        var item = zone.FieldItems[edit.ObjectIndex];
        switch (edit.Field)
        {
            case SwShPlacementEditableField.LocationX:
                WriteSingle(output, item.TransformOffsets.X, edit.Value);
                break;
            case SwShPlacementEditableField.LocationY:
                WriteSingle(output, item.TransformOffsets.Y, edit.Value);
                break;
            case SwShPlacementEditableField.LocationZ:
                WriteSingle(output, item.TransformOffsets.Z, edit.Value);
                break;
            case SwShPlacementEditableField.RotationY:
                WriteSingle(output, item.TransformOffsets.RotationY, edit.Value);
                break;
            case SwShPlacementEditableField.Quantity:
                WriteByte(output, item.QuantityOffset, edit.Value, minimum: 0, maximum: byte.MaxValue);
                break;
            case SwShPlacementEditableField.ItemId:
                if (item.ItemHashOffsets.Count > 0)
                {
                    if (edit.HashValue is null)
                    {
                        throw new InvalidDataException("Placement field item hash edits require an item hash lookup.");
                    }

                    WriteUInt64(output, item.ItemHashOffsets[0], edit.HashValue.Value);
                }
                else if (item.ItemIdOffsets.Count > 0)
                {
                    WriteUInt32(output, item.ItemIdOffsets[0], edit.Value);
                }
                else
                {
                    throw new InvalidDataException("Placement field item does not have an editable item vector entry.");
                }

                break;
            default:
                throw new InvalidDataException($"Placement field '{edit.Field}' is not supported for field items.");
        }
    }

    private static void ApplyHiddenItemEdit(byte[] output, SwShPlacementZone zone, SwShPlacementObjectEdit edit)
    {
        if ((uint)edit.ObjectIndex >= (uint)zone.HiddenItems.Count)
        {
            throw new InvalidDataException($"Placement hidden item index {edit.ObjectIndex} is not present.");
        }

        var item = zone.HiddenItems[edit.ObjectIndex];
        switch (edit.Field)
        {
            case SwShPlacementEditableField.LocationX:
                WriteSingle(output, item.TransformOffsets.X, edit.Value);
                break;
            case SwShPlacementEditableField.LocationY:
                WriteSingle(output, item.TransformOffsets.Y, edit.Value);
                break;
            case SwShPlacementEditableField.LocationZ:
                WriteSingle(output, item.TransformOffsets.Z, edit.Value);
                break;
            case SwShPlacementEditableField.RotationY:
                WriteSingle(output, item.TransformOffsets.RotationY, edit.Value);
                break;
            case SwShPlacementEditableField.ItemId:
            case SwShPlacementEditableField.Quantity:
            case SwShPlacementEditableField.Chance:
                var chanceIndex = edit.ChanceIndex ?? 0;
                if ((uint)chanceIndex >= (uint)item.Chances.Count)
                {
                    throw new InvalidDataException($"Placement hidden item chance index {chanceIndex} is not present.");
                }

                var chance = item.Chances[chanceIndex];
                if (edit.Field == SwShPlacementEditableField.ItemId)
                {
                    if (edit.HashValue is null)
                    {
                        throw new InvalidDataException("Placement hidden item hash edits require an item hash lookup.");
                    }

                    WriteUInt64(output, chance.ItemHashOffset, edit.HashValue.Value);
                }
                else if (edit.Field == SwShPlacementEditableField.Quantity)
                {
                    WriteInt32(output, chance.QuantityOffset, edit.Value, minimum: 0, maximum: 999);
                }
                else
                {
                    WriteInt32(output, chance.ChanceOffset, edit.Value, minimum: 0, maximum: 100);
                }

                break;
            default:
                throw new InvalidDataException($"Placement field '{edit.Field}' is not supported for hidden items.");
        }
    }

    private static SwShPlacementZone ReadZone(
        ReadOnlySpan<byte> data,
        int zoneOffset,
        int zoneIndex,
        IReadOnlyDictionary<ulong, int>? itemIdsByHash)
    {
        var metaOffset = ReadTableUOffset(data, zoneOffset, fieldIndex: 0, required: true);
        var zoneMetaTransformOffset = ReadTableUOffset(data, metaOffset, fieldIndex: 0, required: true);
        var zoneId = ReadTableUInt64(data, metaOffset, fieldIndex: 1, required: false);
        var transform = ReadTransform(data, zoneMetaTransformOffset);
        var objectHash = ReadTableUInt64(data, zoneMetaTransformOffset, fieldIndex: 9, required: false);

        var fieldItemsOffset = ReadTableUOffset(data, zoneOffset, fieldIndex: 6, required: false);
        var hiddenItemsOffset = ReadTableUOffset(data, zoneOffset, fieldIndex: 19, required: false);

        return new SwShPlacementZone(
            zoneIndex,
            zoneId,
            objectHash,
            transform.Transform,
            fieldItemsOffset == 0
                ? Array.Empty<SwShPlacementFieldItem>()
                : ReadFieldItemVector(data, fieldItemsOffset),
            hiddenItemsOffset == 0
                ? Array.Empty<SwShPlacementHiddenItem>()
                : ReadHiddenItemVector(data, hiddenItemsOffset, itemIdsByHash))
        {
            RawObjects = ReadRawObjects(data, zoneOffset),
        };
    }

    private static IReadOnlyList<SwShPlacementRawObject> ReadRawObjects(ReadOnlySpan<byte> data, int zoneOffset)
    {
        var objects = new List<SwShPlacementRawObject>();
        foreach (var vector in PlacementRawCatalog.Vectors)
        {
            var vectorOffset = ReadTableUOffset(data, zoneOffset, vector.ZoneFieldIndex, required: false);
            if (vectorOffset == 0)
            {
                continue;
            }

            var length = ReadVectorLength(data, vectorOffset);
            for (var index = 0; index < length; index++)
            {
                var elementOffset = checked(vectorOffset + sizeof(int) + (index * sizeof(uint)));
                var holderOffset = ReadUOffset(data, elementOffset);
                var context = new RawObjectReadContext();
                var fields = new List<SwShPlacementRawField>();
                ReadRawTable(data, holderOffset, vector.HolderTable, vector.ObjectType, string.Empty, fields, context, tableReferenceOffset: 0, depth: 0);

                objects.Add(new SwShPlacementRawObject(
                    vector.ObjectType,
                    index,
                    context.Transform ?? new SwShPlacementTransform(0, 0, 0, 0),
                    context.ObjectHash,
                    ResolveRawPrimaryLabel(vector.ObjectType, fields),
                    ResolveRawLinkValue(vector.ObjectType, fields),
                    fields));
            }
        }

        return objects;
    }

    private static void ReadRawTable(
        ReadOnlySpan<byte> data,
        int tableOffset,
        string tableName,
        string objectType,
        string pathPrefix,
        ICollection<SwShPlacementRawField> fields,
        RawObjectReadContext context,
        int tableReferenceOffset,
        int depth)
    {
        if (depth > 12 || !PlacementRawCatalog.Tables.TryGetValue(tableName, out var table))
        {
            return;
        }

        if (tableName == PlacementRawCatalog.TransformTable)
        {
            var transform = ReadTransform(data, tableOffset);
            context.Transform ??= transform.Transform;
            context.ObjectHash = ReadTableUInt64(data, tableOffset, fieldIndex: 9, required: false);
        }

        foreach (var field in table.Fields)
        {
            var fieldPath = string.IsNullOrEmpty(pathPrefix) ? field.Name : $"{pathPrefix}.{field.Name}";
            switch (field.Kind)
            {
                case PlacementRawFieldKind.Table:
                    var childReferenceOffset = ReadTableFieldAbsoluteOffset(data, tableOffset, field.FieldIndex);
                    var childOffset = childReferenceOffset == 0 ? 0 : ReadUOffset(data, childReferenceOffset);
                    if (childOffset != 0 && field.TargetTable.Length > 0)
                    {
                        ReadRawTable(data, childOffset, field.TargetTable, objectType, fieldPath, fields, context, childReferenceOffset, depth + 1);
                    }
                    else
                    {
                        AddRawField(fields, objectType, fieldPath, field, string.Empty, "Not present");
                    }

                    break;
                case PlacementRawFieldKind.TableVector:
                    ReadRawTableVector(data, tableOffset, objectType, fieldPath, field, fields, context, depth + 1);
                    break;
                case PlacementRawFieldKind.UIntVector:
                    ReadRawUIntVector(data, tableOffset, objectType, fieldPath, field, fields);
                    break;
                case PlacementRawFieldKind.ULongVector:
                    ReadRawULongVector(data, tableOffset, objectType, fieldPath, field, fields);
                    break;
                case PlacementRawFieldKind.String:
                    var stringReferenceOffset = ReadTableFieldAbsoluteOffset(data, tableOffset, field.FieldIndex);
                    var stringOffset = stringReferenceOffset == 0 ? 0 : ReadUOffset(data, stringReferenceOffset);
                    var stringLength = stringOffset == 0 ? 0 : ReadVectorLength(data, stringOffset);
                    var stringValue = stringOffset == 0 ? string.Empty : ReadString(data, stringOffset);
                    AddRawField(
                        fields,
                        objectType,
                        fieldPath,
                        field,
                        stringValue,
                        string.IsNullOrWhiteSpace(stringValue) ? "None" : stringValue,
                        RawStorageString,
                        stringOffset == 0 ? 0 : stringOffset + sizeof(int),
                        stringByteCapacity: stringLength,
                        stringLengthOffset: stringOffset);
                    break;
                case PlacementRawFieldKind.Float:
                    var floatOffset = ReadTableFieldAbsoluteOffset(data, tableOffset, field.FieldIndex);
                    var floatValue = floatOffset == 0 ? GetRawDefaultSingle(field) : ReadSingleAtOffset(data, floatOffset);
                    AddRawField(
                        fields,
                        objectType,
                        fieldPath,
                        field,
                        FormatRawNumber(floatValue),
                        FormatRawNumber(floatValue),
                        RawStorageFloat,
                        floatOffset,
                        tableName: tableName,
                        tableOffset: tableOffset,
                        tableReferenceOffset: tableReferenceOffset);
                    break;
                case PlacementRawFieldKind.Int:
                    var intOffset = ReadTableFieldAbsoluteOffset(data, tableOffset, field.FieldIndex);
                    var intValue = intOffset == 0 ? GetRawDefaultInt32(field) : ReadInt32AtOffset(data, intOffset);
                    AddRawField(
                        fields,
                        objectType,
                        fieldPath,
                        field,
                        intValue.ToString(CultureInfo.InvariantCulture),
                        intValue.ToString(CultureInfo.InvariantCulture),
                        RawStorageInt,
                        intOffset,
                        tableName: tableName,
                        tableOffset: tableOffset,
                        tableReferenceOffset: tableReferenceOffset);
                    break;
                case PlacementRawFieldKind.UInt:
                    var uintOffset = ReadTableFieldAbsoluteOffset(data, tableOffset, field.FieldIndex);
                    var uintValue = uintOffset == 0 ? GetRawDefaultUInt32(field) : ReadUInt32AtOffset(data, uintOffset);
                    AddRawField(
                        fields,
                        objectType,
                        fieldPath,
                        field,
                        uintValue.ToString(CultureInfo.InvariantCulture),
                        FormatRawUInt(field.Name, uintValue),
                        RawStorageUInt,
                        uintOffset,
                        tableName: tableName,
                        tableOffset: tableOffset,
                        tableReferenceOffset: tableReferenceOffset);
                    break;
                case PlacementRawFieldKind.UByte:
                    var byteOffset = ReadTableFieldAbsoluteOffset(data, tableOffset, field.FieldIndex);
                    var byteValue = byteOffset == 0 ? GetRawDefaultByte(field) : data[byteOffset];
                    AddRawField(
                        fields,
                        objectType,
                        fieldPath,
                        field,
                        byteValue.ToString(CultureInfo.InvariantCulture),
                        byteValue.ToString(CultureInfo.InvariantCulture),
                        RawStorageUByte,
                        byteOffset,
                        tableName: tableName,
                        tableOffset: tableOffset,
                        tableReferenceOffset: tableReferenceOffset);
                    break;
                case PlacementRawFieldKind.Bool:
                    var boolOffset = ReadTableFieldAbsoluteOffset(data, tableOffset, field.FieldIndex);
                    var boolValue = boolOffset == 0 ? GetRawDefaultBool(field) : data[boolOffset] != 0;
                    AddRawField(
                        fields,
                        objectType,
                        fieldPath,
                        field,
                        boolValue ? "true" : "false",
                        boolValue ? "True" : "False",
                        RawStorageBool,
                        boolOffset,
                        tableName: tableName,
                        tableOffset: tableOffset,
                        tableReferenceOffset: tableReferenceOffset);
                    break;
                case PlacementRawFieldKind.ULong:
                    var hashOffset = ReadTableFieldAbsoluteOffset(data, tableOffset, field.FieldIndex);
                    var hashValue = hashOffset == 0 ? GetRawDefaultUInt64(field) : ReadUInt64AtOffset(data, hashOffset);
                    AddRawField(
                        fields,
                        objectType,
                        fieldPath,
                        field,
                        FormatRawHash(hashValue),
                        FormatRawHash(hashValue),
                        RawStorageULong,
                        hashOffset,
                        tableName: tableName,
                        tableOffset: tableOffset,
                        tableReferenceOffset: tableReferenceOffset);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(field), $"Placement raw field kind '{field.Kind}' is not supported.");
            }
        }
    }

    private static void ReadRawTableVector(
        ReadOnlySpan<byte> data,
        int tableOffset,
        string objectType,
        string fieldPath,
        PlacementRawFieldSpec field,
        ICollection<SwShPlacementRawField> fields,
        RawObjectReadContext context,
        int depth)
    {
        var vectorOffset = ReadTableUOffset(data, tableOffset, field.FieldIndex, required: false);
        if (vectorOffset == 0)
        {
            AddRawField(fields, objectType, $"{fieldPath}.Count", field with { Name = "Count" }, "0", "0");
            return;
        }

        var length = ReadVectorLength(data, vectorOffset);
        AddRawField(fields, objectType, $"{fieldPath}.Count", field with { Name = "Count" }, length.ToString(CultureInfo.InvariantCulture), length.ToString(CultureInfo.InvariantCulture));
        for (var index = 0; index < length; index++)
        {
            var elementOffset = checked(vectorOffset + sizeof(int) + (index * sizeof(uint)));
            ReadRawTable(data, ReadUOffset(data, elementOffset), field.TargetTable, objectType, $"{fieldPath}[{index.ToString(CultureInfo.InvariantCulture)}]", fields, context, elementOffset, depth);
        }
    }

    private static void ReadRawUIntVector(
        ReadOnlySpan<byte> data,
        int tableOffset,
        string objectType,
        string fieldPath,
        PlacementRawFieldSpec field,
        ICollection<SwShPlacementRawField> fields)
    {
        var vectorOffset = ReadTableUOffset(data, tableOffset, field.FieldIndex, required: false);
        if (vectorOffset == 0)
        {
            AddRawField(fields, objectType, $"{fieldPath}.Count", field with { Name = "Count" }, "0", "0");
            return;
        }

        var length = ReadVectorLength(data, vectorOffset);
        AddRawField(fields, objectType, $"{fieldPath}.Count", field with { Name = "Count" }, length.ToString(CultureInfo.InvariantCulture), length.ToString(CultureInfo.InvariantCulture));
        for (var index = 0; index < length; index++)
        {
            var elementOffset = checked(vectorOffset + sizeof(int) + (index * sizeof(uint)));
            EnsureRange(data, elementOffset, sizeof(uint));
            var value = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(elementOffset, sizeof(uint)));
            AddRawField(
                fields,
                objectType,
                $"{fieldPath}[{index.ToString(CultureInfo.InvariantCulture)}]",
                field,
                value.ToString(CultureInfo.InvariantCulture),
                value.ToString(CultureInfo.InvariantCulture),
                RawStorageUInt,
                elementOffset);
        }
    }

    private static void ReadRawULongVector(
        ReadOnlySpan<byte> data,
        int tableOffset,
        string objectType,
        string fieldPath,
        PlacementRawFieldSpec field,
        ICollection<SwShPlacementRawField> fields)
    {
        var vectorOffset = ReadTableUOffset(data, tableOffset, field.FieldIndex, required: false);
        if (vectorOffset == 0)
        {
            AddRawField(fields, objectType, $"{fieldPath}.Count", field with { Name = "Count" }, "0", "0");
            return;
        }

        var length = ReadVectorLength(data, vectorOffset);
        AddRawField(fields, objectType, $"{fieldPath}.Count", field with { Name = "Count" }, length.ToString(CultureInfo.InvariantCulture), length.ToString(CultureInfo.InvariantCulture));
        for (var index = 0; index < length; index++)
        {
            var elementOffset = checked(vectorOffset + sizeof(int) + (index * sizeof(ulong)));
            EnsureRange(data, elementOffset, sizeof(ulong));
            var value = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(elementOffset, sizeof(ulong)));
            AddRawField(
                fields,
                objectType,
                $"{fieldPath}[{index.ToString(CultureInfo.InvariantCulture)}]",
                field,
                FormatRawHash(value),
                FormatRawHash(value),
                RawStorageULong,
                elementOffset);
        }
    }

    private static void AddRawField(
        ICollection<SwShPlacementRawField> fields,
        string objectType,
        string fieldPath,
        PlacementRawFieldSpec field,
        string value,
        string displayValue,
        string storageKind = "",
        int valueOffset = 0,
        int stringByteCapacity = 0,
        int stringLengthOffset = 0,
        string tableName = "",
        int tableOffset = 0,
        int tableReferenceOffset = 0)
    {
        var canRewriteTable = valueOffset <= 0 && CanRewriteRawScalarTable(field, storageKind, tableName, tableOffset, tableReferenceOffset);
        var isPatchable = IsRawStoragePatchable(storageKind, valueOffset, stringByteCapacity, canRewriteTable);
        var label = ResolveRawLabel(objectType, fieldPath, field);
        fields.Add(new SwShPlacementRawField(
            $"raw.{objectType}.{fieldPath}",
            label,
            field.Group.Length == 0 ? InferRawGroup(fieldPath, field.Name) : field.Group,
            value,
            displayValue,
            IsReadOnly: !isPatchable,
            GetRawValueKind(storageKind),
            GetRawMinimumValue(storageKind),
            GetRawMaximumValue(storageKind, stringByteCapacity),
            GetRawDescription(objectType, fieldPath, field.Name, label, storageKind, isPatchable, canRewriteTable, field.DefaultValue, stringByteCapacity),
            storageKind,
            valueOffset,
            stringByteCapacity,
            stringLengthOffset,
            tableName,
            tableOffset,
            tableReferenceOffset,
            field.FieldIndex,
            canRewriteTable));
    }

    private static string ResolveRawPrimaryLabel(
        string objectType,
        IReadOnlyList<SwShPlacementRawField> fields)
    {
        var preferred = objectType switch
        {
            "FieldItem" => FindRawValue(fields, "Field_00.Field_02"),
            "Trainer" => FindRawValue(fields, "TrainerID"),
            "NPCType2" => FindRawValue(fields, "HashModel"),
            "NPCType1" => FindMeaningfulRawValue(fields, "HashObjectName", "HashModel", "Message"),
            "StaticObject" => FindRawValue(fields, "Spawns[0].SpawnID"),
            "Symbol" => FindRawValue(fields, "SymbolHash"),
            "Nest" => FindRawValue(fields, "Common"),
            "Warp" => FindRawValue(fields, "NameAreaOther"),
            "Particle" => FindRawValue(fields, "ParticleFile"),
            "Trigger" => FindRawValue(fields, "TriggerName"),
            "Path" => FindRawValue(fields, "PathName"),
            "AdvancedTip" => FindRawValue(fields, "SignHash"),
            _ => string.Empty,
        };

        return string.IsNullOrWhiteSpace(preferred) ? objectType : preferred;
    }

    private static string ResolveRawLinkValue(
        string objectType,
        IReadOnlyList<SwShPlacementRawField> fields)
    {
        return objectType switch
        {
            "Trainer" => FindRawValue(fields, "TrainerID"),
            "StaticObject" => FindRawValue(fields, "Spawns[0].SpawnID"),
            "Symbol" => FindRawValue(fields, "SymbolHash"),
            "Nest" => FindRawValue(fields, "Common"),
            "FlyTo" => FindRawValue(fields, "UnlockFlagHash"),
            "Trigger" => FindRawValue(fields, "TriggerName"),
            "Path" => FindRawValue(fields, "PathName"),
            _ => string.Empty,
        };
    }

    private static string FindRawValue(IReadOnlyList<SwShPlacementRawField> fields, string suffix)
    {
        return fields.FirstOrDefault(field => field.Field.EndsWith(suffix, StringComparison.Ordinal))?.DisplayValue ?? string.Empty;
    }

    private static string FindMeaningfulRawValue(IReadOnlyList<SwShPlacementRawField> fields, params string[] suffixes)
    {
        foreach (var suffix in suffixes)
        {
            var value = FindRawValue(fields, suffix);
            if (!IsEmptyRawHash(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static bool IsEmptyRawHash(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            || string.Equals(value, FormatRawHash(EmptyFnvHash), StringComparison.OrdinalIgnoreCase);
    }

    private static SwShPlacementFieldItem ReadFieldItem(ReadOnlySpan<byte> data, int holderOffset, int objectIndex)
    {
        var itemOffset = ReadTableUOffset(data, holderOffset, fieldIndex: 0, required: true);
        var transformOffset = ReadTableUOffset(data, itemOffset, fieldIndex: 0, required: true);
        var transform = ReadTransform(data, transformOffset);
        var modelOffset = ReadTableUOffset(data, itemOffset, fieldIndex: 2, required: false);
        var flagsOffset = ReadTableUOffset(data, itemOffset, fieldIndex: 6, required: false);
        var itemIdsOffset = ReadTableUOffset(data, itemOffset, fieldIndex: 7, required: false);
        var quantityOffset = ReadTableFieldAbsoluteOffset(data, itemOffset, fieldIndex: 8);

        IReadOnlyList<ulong> flags = Array.Empty<ulong>();
        IReadOnlyList<int> flagOffsets = Array.Empty<int>();
        if (flagsOffset != 0)
        {
            flags = ReadUInt64Vector(data, flagsOffset, out flagOffsets);
        }

        IReadOnlyList<uint> itemIds = Array.Empty<uint>();
        IReadOnlyList<int> itemIdOffsets = Array.Empty<int>();
        if (itemIdsOffset != 0)
        {
            itemIds = ReadUInt32Vector(data, itemIdsOffset, out itemIdOffsets);
        }

        return new SwShPlacementFieldItem(
            objectIndex,
            modelOffset == 0 ? string.Empty : ReadString(data, modelOffset),
            transform.Transform,
            flags,
            flagOffsets,
            itemIds,
            itemIdOffsets,
            quantityOffset == 0 ? (byte)0 : data[quantityOffset],
            quantityOffset,
            transform.Offsets);
    }

    private static SwShPlacementHiddenItem ReadHiddenItem(
        ReadOnlySpan<byte> data,
        int holderOffset,
        int objectIndex,
        IReadOnlyDictionary<ulong, int>? itemIdsByHash)
    {
        var itemOffset = ReadTableUOffset(data, holderOffset, fieldIndex: 0, required: true);
        var transformOffset = ReadTableUOffset(data, itemOffset, fieldIndex: 0, required: true);
        var transform = ReadTransform(data, transformOffset);
        var chancesOffset = ReadTableUOffset(data, itemOffset, fieldIndex: 2, required: false);

        return new SwShPlacementHiddenItem(
            objectIndex,
            transform.Transform,
            chancesOffset == 0
                ? Array.Empty<SwShPlacementHiddenItemChance>()
                : ReadHiddenItemChanceVector(data, chancesOffset, itemIdsByHash),
            transform.Offsets);
    }

    private static SwShPlacementHiddenItemChance ReadHiddenItemChance(
        ReadOnlySpan<byte> data,
        int chanceOffset,
        int chanceIndex,
        IReadOnlyDictionary<ulong, int>? itemIdsByHash)
    {
        var itemHashOffset = ReadTableFieldAbsoluteOffset(data, chanceOffset, fieldIndex: 0);
        var chanceValueOffset = ReadTableFieldAbsoluteOffset(data, chanceOffset, fieldIndex: 1);
        var quantityOffset = ReadTableFieldAbsoluteOffset(data, chanceOffset, fieldIndex: 2);
        var hash = itemHashOffset == 0
            ? 0
            : BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(itemHashOffset, sizeof(ulong)));

        return new SwShPlacementHiddenItemChance(
            chanceIndex,
            hash,
            itemIdsByHash is not null && itemIdsByHash.TryGetValue(hash, out var itemId) ? itemId : null,
            chanceValueOffset == 0 ? 0 : BinaryPrimitives.ReadInt32LittleEndian(data.Slice(chanceValueOffset, sizeof(int))),
            quantityOffset == 0 ? 0 : BinaryPrimitives.ReadInt32LittleEndian(data.Slice(quantityOffset, sizeof(int))),
            itemHashOffset,
            chanceValueOffset,
            quantityOffset);
    }

    private static TransformWithOffsets ReadTransform(ReadOnlySpan<byte> data, int tableOffset)
    {
        return new TransformWithOffsets(
            new SwShPlacementTransform(
                ReadTableSingle(data, tableOffset, fieldIndex: 0, required: false),
                ReadTableSingle(data, tableOffset, fieldIndex: 1, required: false),
                ReadTableSingle(data, tableOffset, fieldIndex: 2, required: false),
                ReadTableSingle(data, tableOffset, fieldIndex: 4, required: false)),
            new PlacementTransformOffsets(
                ReadTableFieldAbsoluteOffset(data, tableOffset, fieldIndex: 0),
                ReadTableFieldAbsoluteOffset(data, tableOffset, fieldIndex: 1),
                ReadTableFieldAbsoluteOffset(data, tableOffset, fieldIndex: 2),
                ReadTableFieldAbsoluteOffset(data, tableOffset, fieldIndex: 4)));
    }

    private static int ReadUOffset(ReadOnlySpan<byte> data, int offset)
    {
        EnsureRange(data, offset, sizeof(uint));
        var relativeOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));
        var targetOffset = checked(offset + (int)relativeOffset);
        EnsureRange(data, targetOffset, sizeof(int));

        return targetOffset;
    }

    private static int ReadTableUOffset(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex, bool required)
    {
        var fieldOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex);
        if (fieldOffset == 0)
        {
            if (required)
            {
                throw new InvalidDataException($"Required FlatBuffer field {fieldIndex} is missing.");
            }

            return 0;
        }

        return ReadUOffset(data, tableOffset + fieldOffset);
    }

    private static int ReadTableFieldAbsoluteOffset(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex)
    {
        var fieldOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex);
        return fieldOffset == 0 ? 0 : tableOffset + fieldOffset;
    }

    private static float ReadTableSingle(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex, bool required)
    {
        var offset = ReadTableFieldAbsoluteOffset(data, tableOffset, fieldIndex);
        if (offset == 0)
        {
            if (required)
            {
                throw new InvalidDataException($"Required FlatBuffer field {fieldIndex} is missing.");
            }

            return 0;
        }

        EnsureRange(data, offset, sizeof(float));
        return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, sizeof(int))));
    }

    private static ulong ReadTableUInt64(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex, bool required)
    {
        var offset = ReadTableFieldAbsoluteOffset(data, tableOffset, fieldIndex);
        if (offset == 0)
        {
            if (required)
            {
                throw new InvalidDataException($"Required FlatBuffer field {fieldIndex} is missing.");
            }

            return 0;
        }

        EnsureRange(data, offset, sizeof(ulong));
        return BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, sizeof(ulong)));
    }

    private static uint ReadTableUInt32(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex, bool required)
    {
        var offset = ReadTableFieldAbsoluteOffset(data, tableOffset, fieldIndex);
        if (offset == 0)
        {
            if (required)
            {
                throw new InvalidDataException($"Required FlatBuffer field {fieldIndex} is missing.");
            }

            return 0;
        }

        EnsureRange(data, offset, sizeof(uint));
        return BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));
    }

    private static int ReadTableInt32(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex, bool required)
    {
        var offset = ReadTableFieldAbsoluteOffset(data, tableOffset, fieldIndex);
        if (offset == 0)
        {
            if (required)
            {
                throw new InvalidDataException($"Required FlatBuffer field {fieldIndex} is missing.");
            }

            return 0;
        }

        EnsureRange(data, offset, sizeof(int));
        return BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, sizeof(int)));
    }

    private static byte ReadTableByte(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex)
    {
        var offset = ReadTableFieldAbsoluteOffset(data, tableOffset, fieldIndex);
        if (offset == 0)
        {
            return 0;
        }

        EnsureRange(data, offset, sizeof(byte));
        return data[offset];
    }

    private static bool ReadTableBool(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex)
    {
        return ReadTableByte(data, tableOffset, fieldIndex) != 0;
    }

    private static float ReadSingleAtOffset(ReadOnlySpan<byte> data, int offset)
    {
        EnsureRange(data, offset, sizeof(float));
        return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, sizeof(int))));
    }

    private static int ReadInt32AtOffset(ReadOnlySpan<byte> data, int offset)
    {
        EnsureRange(data, offset, sizeof(int));
        return BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, sizeof(int)));
    }

    private static uint ReadUInt32AtOffset(ReadOnlySpan<byte> data, int offset)
    {
        EnsureRange(data, offset, sizeof(uint));
        return BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));
    }

    private static ulong ReadUInt64AtOffset(ReadOnlySpan<byte> data, int offset)
    {
        EnsureRange(data, offset, sizeof(ulong));
        return BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, sizeof(ulong)));
    }

    private static string FormatRawHash(ulong value)
    {
        return value == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"0x{value:X16}");
    }

    private static string FormatRawNumber(float value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string FormatRawUInt(string fieldName, uint value)
    {
        if (fieldName == "State")
        {
            return value switch
            {
                0 => "Standing (0)",
                2 => "Sitting (2)",
                _ => value.ToString(CultureInfo.InvariantCulture),
            };
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static bool IsRawStoragePatchable(string storageKind, int valueOffset, int stringByteCapacity, bool canRewriteTable)
    {
        if (storageKind.Length == 0 || valueOffset <= 0)
        {
            return canRewriteTable;
        }

        return storageKind != RawStorageString || stringByteCapacity >= 0;
    }

    private static bool CanRewriteRawScalarTable(
        PlacementRawFieldSpec field,
        string storageKind,
        string tableName,
        int tableOffset,
        int tableReferenceOffset)
    {
        return field.CanRewriteTable
            && storageKind is RawStorageBool or RawStorageFloat or RawStorageInt or RawStorageUByte or RawStorageUInt or RawStorageULong
            && tableName.Length > 0
            && tableOffset > 0
            && tableReferenceOffset > 0
            && PlacementRawCatalog.Tables.TryGetValue(tableName, out var table)
            && table.Fields.All(candidate => IsRawScalarFieldKind(candidate.Kind));
    }

    private static bool IsRawScalarFieldKind(PlacementRawFieldKind kind)
    {
        return kind is PlacementRawFieldKind.Bool
            or PlacementRawFieldKind.Float
            or PlacementRawFieldKind.Int
            or PlacementRawFieldKind.UByte
            or PlacementRawFieldKind.UInt
            or PlacementRawFieldKind.ULong;
    }

    private static string GetRawValueKind(string storageKind)
    {
        return storageKind switch
        {
            RawStorageBool => "boolean",
            RawStorageFloat => "number",
            RawStorageInt => "integer",
            RawStorageString => "text",
            RawStorageUByte => "integer",
            RawStorageUInt => "integer",
            RawStorageULong => "hash",
            _ => "text",
        };
    }

    private static double GetRawMinimumValue(string storageKind)
    {
        return storageKind switch
        {
            RawStorageFloat => MinimumRawFloatValue,
            RawStorageInt => int.MinValue,
            RawStorageUByte => byte.MinValue,
            RawStorageUInt => uint.MinValue,
            RawStorageULong => 0,
            _ => 0,
        };
    }

    private static double GetRawMaximumValue(string storageKind, int stringByteCapacity)
    {
        return storageKind switch
        {
            RawStorageFloat => MaximumRawFloatValue,
            RawStorageInt => int.MaxValue,
            RawStorageString => stringByteCapacity,
            RawStorageUByte => byte.MaxValue,
            RawStorageUInt => uint.MaxValue,
            RawStorageULong => ulong.MaxValue,
            _ => 0,
        };
    }

    private static string GetRawDescription(
        string objectType,
        string fieldPath,
        string fieldName,
        string label,
        string storageKind,
        bool isPatchable,
        bool canRewriteTable,
        string defaultValue,
        int stringByteCapacity)
    {
        var storageDescription = storageKind switch
        {
            RawStorageBool => "Stored as a one byte boolean in this placement FlatBuffer.",
            RawStorageFloat => "Stored as a 32-bit float in this placement FlatBuffer.",
            RawStorageInt => "Stored as a signed 32-bit integer in this placement FlatBuffer.",
            RawStorageString => string.Create(
                CultureInfo.InvariantCulture,
                $"Stored as an in-place FlatBuffer string. New text can use up to {stringByteCapacity} UTF-8 bytes."),
            RawStorageUByte => "Stored as an unsigned byte in this placement FlatBuffer.",
            RawStorageUInt => "Stored as an unsigned 32-bit integer in this placement FlatBuffer.",
            RawStorageULong => "Stored as a 64-bit FNV hash. Enter a decimal value or 0x-prefixed hex hash.",
            _ => "This row is structural placement metadata rather than a directly stored scalar value.",
        };

        var knownness = fieldName.StartsWith("Field_", StringComparison.Ordinal)
            || fieldName.StartsWith("Hash_", StringComparison.Ordinal)
            || fieldName.StartsWith("Byte_", StringComparison.Ordinal)
            ? $"Reference schema keeps this as {fieldName}; KM exposes it as '{label}' because the exact gameplay meaning is not confirmed."
            : $"Placement field path: {objectType}.{fieldPath}.";

        var defaultDescription = defaultValue.Length == 0
            ? string.Empty
            : $" FlatBuffer default is {defaultValue} when this scalar is omitted.";

        var editability = isPatchable
            ? canRewriteTable
                ? "Editable by replacing this small placement subtable and redirecting its parent reference."
                : "Editable in place without rebuilding the placement FlatBuffer."
            : "Not editable because the value is absent from this object or represents a vector/table count.";

        return $"{knownness} {storageDescription}{defaultDescription} {editability}";
    }

    private static string ResolveRawLabel(
        string objectType,
        string fieldPath,
        PlacementRawFieldSpec field)
    {
        if (objectType == "AdvancedTip")
        {
            if (fieldPath.EndsWith("Field_11.Field_00", StringComparison.Ordinal))
            {
                return "Bounds A Type";
            }

            if (fieldPath.EndsWith("Field_13.Field_00", StringComparison.Ordinal))
            {
                return "Bounds B Type";
            }
        }

        return field.Label.Length == 0 ? FormatRawLabel(field.Name) : field.Label;
    }

    private static bool GetRawDefaultBool(PlacementRawFieldSpec field)
    {
        return field.DefaultValue.Equals("true", StringComparison.OrdinalIgnoreCase)
            || field.DefaultValue == "1";
    }

    private static byte GetRawDefaultByte(PlacementRawFieldSpec field)
    {
        return byte.TryParse(field.DefaultValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : (byte)0;
    }

    private static int GetRawDefaultInt32(PlacementRawFieldSpec field)
    {
        return int.TryParse(field.DefaultValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private static uint GetRawDefaultUInt32(PlacementRawFieldSpec field)
    {
        return uint.TryParse(field.DefaultValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private static ulong GetRawDefaultUInt64(PlacementRawFieldSpec field)
    {
        if (field.DefaultValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && ulong.TryParse(field.DefaultValue[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var hexValue))
        {
            return hexValue;
        }

        return ulong.TryParse(field.DefaultValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private static float GetRawDefaultSingle(PlacementRawFieldSpec field)
    {
        return float.TryParse(field.DefaultValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private static string FormatRawLabel(string fieldName)
    {
        return fieldName switch
        {
            "LocationX" => "X",
            "LocationY" => "Y",
            "LocationZ" => "Z",
            "RotationX" => "Rotation X",
            "RotationY" => "Rotation Y",
            "RotationZ" => "Rotation Z",
            "ScaleX" => "Scale X",
            "ScaleY" => "Scale Y",
            "ScaleZ" => "Scale Z",
            "HashObjectName" => "Object Hash",
            "NameModel" => "Model",
            "NameAnimation" => "Animation",
            "HashModel" => "Model Hash",
            "HashMessage" => "Message Hash",
            "Message" => "Message Hash",
            "WorkValue" => "Work / Flag Hash",
            "TrainerID" => "Trainer Battle",
            "MovementPath" => "Movement Path",
            "SpawnID" => "Static Encounter",
            "SymbolHash" => "Symbol Encounter Table",
            "Common" => "Common Raid Table",
            "Rare" => "Rare Raid Table",
            "EnableSpawns" => "Enable Spawn Flag",
            "UnlockFlagHash" => "Unlock Flag",
            "TriggerName" => "Trigger",
            "PathName" => "Path",
            "SignHash" => "Sign / Message Hash",
            "ParticleFile" => "Particle File",
            "PlayName" => "Play Event",
            "StopName" => "Stop Event",
            "Species" => "Species",
            "Form" => "Form",
            "Gender" => "Gender",
            "Shiny" => "Shiny",
            "ModelVariant" => "Model Variant",
            "AnimationIndexPrimary" => "Primary Animation",
            "AnimationIndexSecondary" => "Secondary Animation",
            "Quantity" => "Quantity",
            "Chance" => "Chance",
            "Rate" => "Rate",
            "Behavior" => "Behavior",
            "Hash" => "Hash",
            "Count" => "Count",
            _ when fieldName.StartsWith("Hash_", StringComparison.Ordinal) => fieldName.Replace('_', ' '),
            _ when fieldName.StartsWith("Field_", StringComparison.Ordinal) => fieldName.Replace('_', ' '),
            _ => fieldName,
        };
    }

    private static string InferRawGroup(string fieldPath, string fieldName)
    {
        if (fieldPath.Contains("Location", StringComparison.Ordinal)
            || fieldPath.Contains("Rotation", StringComparison.Ordinal)
            || fieldPath.Contains("Scale", StringComparison.Ordinal)
            || fieldName == "HashObjectName")
        {
            return "Transform";
        }

        if (fieldName.Contains("Model", StringComparison.Ordinal)
            || fieldName.Contains("Animation", StringComparison.Ordinal)
            || fieldName.Contains("Behavior", StringComparison.Ordinal)
            || fieldName == "State"
            || fieldName.StartsWith("Flag_", StringComparison.Ordinal))
        {
            return "Object / Behavior";
        }

        if (fieldName.Contains("Hash", StringComparison.Ordinal)
            || fieldName is "TrainerID" or "MovementPath" or "Common" or "Rare" or "EnableSpawns" or "UnlockFlagHash"
                or "TriggerName" or "PathName" or "SignHash" or "Message" or "WorkValue" or "SpawnID" or "SymbolHash")
        {
            return "References";
        }

        if (fieldName is "Species" or "Form" or "Gender" or "Shiny")
        {
            return "Pokemon";
        }

        if (fieldName is "Quantity" or "Chance" or "Rate" || fieldPath.Contains("Items", StringComparison.Ordinal)
            || fieldPath.Contains("Flags", StringComparison.Ordinal) || fieldPath.Contains("Spawns", StringComparison.Ordinal)
            || fieldPath.Contains("Field_01[", StringComparison.Ordinal) || fieldPath.Contains("Field_02[", StringComparison.Ordinal))
        {
            return "Entries";
        }

        if (fieldPath.Contains("Field_04", StringComparison.Ordinal)
            || fieldPath.Contains("Field_06", StringComparison.Ordinal)
            || fieldPath.Contains("Field_07", StringComparison.Ordinal)
            || fieldPath.Contains("SubMeta", StringComparison.Ordinal)
            || fieldPath.Contains("Unknown", StringComparison.Ordinal)
            || fieldPath.Contains("Details", StringComparison.Ordinal))
        {
            return "Bounds / Ranges";
        }

        return "Advanced / Unknown";
    }

    private static IReadOnlyList<SwShPlacementZone> ReadZoneVector(
        ReadOnlySpan<byte> data,
        int vectorOffset,
        IReadOnlyDictionary<ulong, int>? itemIdsByHash)
    {
        var length = ReadVectorLength(data, vectorOffset);
        var values = new SwShPlacementZone[length];
        for (var index = 0; index < values.Length; index++)
        {
            var elementOffset = checked(vectorOffset + sizeof(int) + (index * sizeof(uint)));
            values[index] = ReadZone(data, ReadUOffset(data, elementOffset), index, itemIdsByHash);
        }

        return values;
    }

    private static IReadOnlyList<SwShPlacementFieldItem> ReadFieldItemVector(ReadOnlySpan<byte> data, int vectorOffset)
    {
        var length = ReadVectorLength(data, vectorOffset);
        var values = new SwShPlacementFieldItem[length];
        for (var index = 0; index < values.Length; index++)
        {
            var elementOffset = checked(vectorOffset + sizeof(int) + (index * sizeof(uint)));
            values[index] = ReadFieldItem(data, ReadUOffset(data, elementOffset), index);
        }

        return values;
    }

    private static IReadOnlyList<SwShPlacementHiddenItem> ReadHiddenItemVector(
        ReadOnlySpan<byte> data,
        int vectorOffset,
        IReadOnlyDictionary<ulong, int>? itemIdsByHash)
    {
        var length = ReadVectorLength(data, vectorOffset);
        var values = new SwShPlacementHiddenItem[length];
        for (var index = 0; index < values.Length; index++)
        {
            var elementOffset = checked(vectorOffset + sizeof(int) + (index * sizeof(uint)));
            values[index] = ReadHiddenItem(data, ReadUOffset(data, elementOffset), index, itemIdsByHash);
        }

        return values;
    }

    private static IReadOnlyList<SwShPlacementHiddenItemChance> ReadHiddenItemChanceVector(
        ReadOnlySpan<byte> data,
        int vectorOffset,
        IReadOnlyDictionary<ulong, int>? itemIdsByHash)
    {
        var length = ReadVectorLength(data, vectorOffset);
        var values = new SwShPlacementHiddenItemChance[length];
        for (var index = 0; index < values.Length; index++)
        {
            var elementOffset = checked(vectorOffset + sizeof(int) + (index * sizeof(uint)));
            values[index] = ReadHiddenItemChance(data, ReadUOffset(data, elementOffset), index, itemIdsByHash);
        }

        return values;
    }

    private static IReadOnlyList<uint> ReadUInt32Vector(ReadOnlySpan<byte> data, int vectorOffset, out IReadOnlyList<int> offsets)
    {
        var length = ReadVectorLength(data, vectorOffset);
        var values = new uint[length];
        var valueOffsets = new int[length];
        for (var index = 0; index < values.Length; index++)
        {
            var elementOffset = checked(vectorOffset + sizeof(int) + (index * sizeof(uint)));
            EnsureRange(data, elementOffset, sizeof(uint));
            values[index] = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(elementOffset, sizeof(uint)));
            valueOffsets[index] = elementOffset;
        }

        offsets = valueOffsets;
        return values;
    }

    private static IReadOnlyList<ulong> ReadUInt64Vector(ReadOnlySpan<byte> data, int vectorOffset, out IReadOnlyList<int> offsets)
    {
        var length = ReadVectorLength(data, vectorOffset);
        var values = new ulong[length];
        var valueOffsets = new int[length];
        for (var index = 0; index < values.Length; index++)
        {
            var elementOffset = checked(vectorOffset + sizeof(int) + (index * sizeof(ulong)));
            EnsureRange(data, elementOffset, sizeof(ulong));
            values[index] = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(elementOffset, sizeof(ulong)));
            valueOffsets[index] = elementOffset;
        }

        offsets = valueOffsets;
        return values;
    }

    private static int ReadVectorLength(ReadOnlySpan<byte> data, int vectorOffset)
    {
        EnsureRange(data, vectorOffset, sizeof(int));
        var length = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(vectorOffset, sizeof(int)));
        if (length < 0)
        {
            throw new InvalidDataException("FlatBuffer vector length must not be negative.");
        }

        return length;
    }

    private static string ReadString(ReadOnlySpan<byte> data, int stringOffset)
    {
        var length = ReadVectorLength(data, stringOffset);
        EnsureRange(data, stringOffset + sizeof(int), length);
        return Encoding.UTF8.GetString(data.Slice(stringOffset + sizeof(int), length));
    }

    private static int ReadTableFieldOffset(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex)
    {
        EnsureRange(data, tableOffset, sizeof(int));
        var vTableOffset = tableOffset - BinaryPrimitives.ReadInt32LittleEndian(data.Slice(tableOffset, sizeof(int)));
        EnsureRange(data, vTableOffset, sizeof(ushort) * 2);
        var vTableSize = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(vTableOffset, sizeof(ushort)));
        var fieldOffset = sizeof(ushort) * (2 + fieldIndex);
        if (fieldOffset + sizeof(ushort) > vTableSize)
        {
            return 0;
        }

        return BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(vTableOffset + fieldOffset, sizeof(ushort)));
    }

    private static void WriteSingle(byte[] data, int offset, double value)
    {
        EnsurePatchOffset(data, offset, sizeof(float));
        if (double.IsNaN(value) || double.IsInfinity(value) || value < -1_000_000 || value > 1_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Placement coordinate value is outside the supported range.");
        }

        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, sizeof(int)), BitConverter.SingleToInt32Bits((float)value));
    }

    private static void WriteByte(byte[] data, int offset, double value, byte minimum, byte maximum)
    {
        EnsureInteger(value, minimum, maximum);
        EnsurePatchOffset(data, offset, sizeof(byte));
        data[offset] = (byte)value;
    }

    private static void WriteInt32(byte[] data, int offset, double value, int minimum, int maximum)
    {
        EnsureInteger(value, minimum, maximum);
        EnsurePatchOffset(data, offset, sizeof(int));
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, sizeof(int)), (int)value);
    }

    private static void WriteUInt32(byte[] data, int offset, double value)
    {
        EnsureInteger(value, 0, ushort.MaxValue);
        EnsurePatchOffset(data, offset, sizeof(uint));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)), (uint)value);
    }

    private static void WriteUInt32(byte[] data, int offset, uint value)
    {
        EnsurePatchOffset(data, offset, sizeof(uint));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)), value);
    }

    private static void WriteUInt64(byte[] data, int offset, ulong value)
    {
        EnsurePatchOffset(data, offset, sizeof(ulong));
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset, sizeof(ulong)), value);
    }

    private static void WriteStringInPlace(byte[] data, SwShPlacementRawField field, string value)
    {
        if (field.StringLengthOffset <= 0 || field.ValueOffset <= 0)
        {
            throw new InvalidDataException($"Placement raw string field '{field.Field}' is not present.");
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > field.StringByteCapacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Placement string field '{field.Label}' can use at most {field.StringByteCapacity} UTF-8 bytes in the current FlatBuffer layout."));
        }

        EnsurePatchOffset(data, field.StringLengthOffset, sizeof(int));
        EnsurePatchOffset(data, field.ValueOffset, field.StringByteCapacity);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(field.StringLengthOffset, sizeof(int)), bytes.Length);
        data.AsSpan(field.ValueOffset, field.StringByteCapacity).Clear();
        bytes.CopyTo(data.AsSpan(field.ValueOffset, bytes.Length));

        var terminatorOffset = field.ValueOffset + bytes.Length;
        if (terminatorOffset < data.Length)
        {
            data[terminatorOffset] = 0;
        }
    }

    private static double ParseDouble(string value, string field)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            || double.IsNaN(parsed)
            || double.IsInfinity(parsed))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                $"Placement raw field '{field}' must be a finite numeric value.");
        }

        return parsed;
    }

    private static uint ParseUInt32(string value, string field)
    {
        if (!uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                $"Placement raw field '{field}' must be an unsigned 32-bit integer.");
        }

        return parsed;
    }

    private static bool ParseBool(string value, string field)
    {
        var trimmed = value.Trim();
        if (bool.TryParse(trimmed, out var parsed))
        {
            return parsed;
        }

        if (trimmed == "1")
        {
            return true;
        }

        if (trimmed == "0")
        {
            return false;
        }

        if (trimmed.Equals("yes", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (trimmed.Equals("no", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        throw new ArgumentOutOfRangeException(
            nameof(value),
            $"Placement raw field '{field}' must be true/false or 1/0.");
    }

    private static ulong ParseUInt64(string value, string field)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0
            || trimmed.Equals("none", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("empty", StringComparison.OrdinalIgnoreCase))
        {
            return EmptyFnvHash;
        }

        var hexIndex = trimmed.IndexOf("0x", StringComparison.OrdinalIgnoreCase);
        if (hexIndex >= 0)
        {
            var hex = trimmed[(hexIndex + 2)..];
            var separatorIndex = hex.IndexOfAny(new[] { ' ', ')', ']' });
            if (separatorIndex >= 0)
            {
                hex = hex[..separatorIndex];
            }

            if (ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hexValue))
            {
                return hexValue;
            }
        }

        if (ulong.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        throw new ArgumentOutOfRangeException(
            nameof(value),
            $"Placement raw field '{field}' must be a decimal value or 0x-prefixed 64-bit hash.");
    }

    private static void EnsureInteger(double value, int minimum, int maximum)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || Math.Truncate(value) != value || value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Placement integer value must be in the supported range {minimum}-{maximum}."));
        }
    }

    private static void EnsurePatchOffset(byte[] data, int offset, int length)
    {
        if (offset <= 0 || length < 0 || offset > data.Length || length > data.Length - offset)
        {
            throw new InvalidDataException("Placement field is not present in the FlatBuffer layout and cannot be patched in place.");
        }
    }

    private static void EnsureRange(ReadOnlySpan<byte> data, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > data.Length || length > data.Length - offset)
        {
            throw new InvalidDataException("FlatBuffer offset points outside the placement archive.");
        }
    }

    private sealed record TransformWithOffsets(
        SwShPlacementTransform Transform,
        PlacementTransformOffsets Offsets);

    private enum PlacementRawFieldKind
    {
        Bool,
        Float,
        Int,
        String,
        Table,
        TableVector,
        UByte,
        UInt,
        UIntVector,
        ULong,
        ULongVector,
    }

    private sealed record PlacementRawVectorSpec(
        int ZoneFieldIndex,
        string ObjectType,
        string HolderTable);

    private sealed record PlacementRawTableSpec(
        string Name,
        IReadOnlyList<PlacementRawFieldSpec> Fields);

    private sealed record PlacementRawFieldSpec(
        string Name,
        PlacementRawFieldKind Kind,
        int FieldIndex,
        string TargetTable = "",
        string Label = "",
        string Group = "",
        string DefaultValue = "",
        bool CanRewriteTable = false);

    private sealed class RawObjectReadContext
    {
        public SwShPlacementTransform? Transform { get; set; }

        public ulong ObjectHash { get; set; }
    }

    private sealed record RawScalarTableValue(
        int FieldIndex,
        int Alignment,
        byte[] Bytes);

    private static class PlacementRawCatalog
    {
        public const string TransformTable = "PlacementZoneMetaTripleXYZ";

        public static readonly IReadOnlyList<PlacementRawVectorSpec> Vectors =
        [
            new(1, "UnitObject", "PlacementZoneUnitObjectHolder"),
            new(2, "Critter", "PlacementZoneSpeciesHolder"),
            new(3, "Warp", "PlacementZoneWarpHolder"),
            new(4, "StepJump", "PlacementZoneStepJumpHolder"),
            new(5, "Particle", "PlacementZoneParticleHolder"),
            new(6, "FieldItem", "PlacementZoneFieldItemHolder"),
            new(7, "Trigger", "PlacementZoneTriggerHolder"),
            new(8, "Trainer", "PlacementZoneTrainerHolder"),
            new(9, "TrainerTip", "PlacementZoneTrainerTipHolder"),
            new(10, "Environment", "PlacementZoneEnvironmentHolder"),
            new(11, "FlyTo", "PlacementZoneFlightAnchorHolder"),
            new(12, "PokeCenterAnchor", "PlacementZonePokeCenterSpawnAnchorHolder"),
            new(13, "NPCType1", "PlacementZoneNPCHolder"),
            new(14, "AdvancedTip", "PlacementZoneAdvancedTipHolder"),
            new(15, "Path", "PlacementZoneMovementPathHolder"),
            new(16, "NPCType2", "PlacementZoneOtherNPCHolder"),
            new(17, "Quadrant", "PlacementZoneQuadrantHolder"),
            new(18, "FishingPoint", "PlacementZoneFishingPointHolder"),
            new(19, "HiddenItem", "PlacementZoneHiddenItemHolder"),
            new(20, "Symbol", "PlacementZoneSymbolSpawnHolder"),
            new(21, "Nest", "PlacementZoneNestHoleHolder"),
            new(22, "BerryTree", "PlacementZoneBerryTreeHolder"),
            new(23, "Ladder", "PlacementZoneLadderHolder"),
            new(24, "Popup", "PlacementZonePopupHolder"),
            new(25, "IKStep", "PlacementZoneIKStepHolder"),
            new(26, "StaticObject", "PlacementZoneStaticObjectsHolder"),
            new(27, "RotomRally", "PlacementZoneRotomRallyEntry"),
        ];

        public static readonly IReadOnlyDictionary<string, PlacementRawTableSpec> Tables = CreateTables();

        private static IReadOnlyDictionary<string, PlacementRawTableSpec> CreateTables()
        {
            var tables = new Dictionary<string, PlacementRawTableSpec>(StringComparer.Ordinal)
            {
                [TransformTable] = Table(TransformTable,
                    F("LocationX", PlacementRawFieldKind.Float, 0),
                    F("LocationY", PlacementRawFieldKind.Float, 1),
                    F("LocationZ", PlacementRawFieldKind.Float, 2),
                    F("RotationX", PlacementRawFieldKind.Float, 3),
                    F("RotationY", PlacementRawFieldKind.Float, 4),
                    F("RotationZ", PlacementRawFieldKind.Float, 5),
                    F("ScaleX", PlacementRawFieldKind.Float, 6),
                    F("ScaleY", PlacementRawFieldKind.Float, 7),
                    F("ScaleZ", PlacementRawFieldKind.Float, 8),
                    F("HashObjectName", PlacementRawFieldKind.ULong, 9),
                    F("Hash_10", PlacementRawFieldKind.ULong, 10),
                    F("Hash_11", PlacementRawFieldKind.ULong, 11)),
                ["PlacementZone_V3f"] = Table("PlacementZone_V3f",
                    F("LocationX", PlacementRawFieldKind.Float, 0),
                    F("LocationY", PlacementRawFieldKind.Float, 1),
                    F("LocationZ", PlacementRawFieldKind.Float, 2)),
                ["PlacementZoneDeepX"] = NumericDeepX("PlacementZoneDeepX"),
                ["PlacementZoneDeepY"] = NumericDeepY("PlacementZoneDeepY"),
                ["FlatDummyObject"] = Table("FlatDummyObject", F("Field_00", PlacementRawFieldKind.UByte, 0)),
                ["FlatDummyEntry"] = Table("FlatDummyEntry"),
                ["PlacementZone_F02_Nine"] = Table("PlacementZone_F02_Nine",
                    F("Field_00", PlacementRawFieldKind.UByte, 0),
                    F("Field_01", PlacementRawFieldKind.UByte, 1),
                    F("Field_02", PlacementRawFieldKind.UByte, 2),
                    F("Field_03", PlacementRawFieldKind.UInt, 3),
                    F("Hash_04", PlacementRawFieldKind.ULong, 4),
                    F("Field_05", PlacementRawFieldKind.UByte, 5),
                    F("Field_06", PlacementRawFieldKind.UInt, 6),
                    F("Hash_07", PlacementRawFieldKind.ULong, 7),
                    F("AnimationIndexSecondary", PlacementRawFieldKind.UInt, 8),
                    F("Field_09", PlacementRawFieldKind.UInt, 9)),
                ["PlacementZoneUnitObjectHolder"] = Table("PlacementZoneUnitObjectHolder", T("Object", 0, "PlacementZoneUnitObject")),
                ["PlacementZoneUnitObject"] = Table("PlacementZoneUnitObject",
                    T("Field_00", 0, TransformTable),
                    F("NameModel", PlacementRawFieldKind.String, 1),
                    F("NameAnimation", PlacementRawFieldKind.String, 2),
                    F("Field_03", PlacementRawFieldKind.Float, 3),
                    F("Field_04", PlacementRawFieldKind.Float, 4),
                    F("Field_05", PlacementRawFieldKind.String, 5),
                    F("Field_06", PlacementRawFieldKind.String, 6),
                    F("Field_07", PlacementRawFieldKind.Float, 7),
                    F("Field_08", PlacementRawFieldKind.Float, 8),
                    F("Field_09", PlacementRawFieldKind.Float, 9),
                    F("Field_10", PlacementRawFieldKind.Float, 10),
                    T("Unknown", 11, "PlacementZoneDeepY"),
                    F("Number", PlacementRawFieldKind.UByte, 12),
                    T("Details", 13, "PlacementZoneUnitObjectDetails"),
                    T("Dummy", 14, "PlacementZoneUnitObjectToggle")),
                ["PlacementZoneUnitObjectDetails"] = NumericIntFloat10("PlacementZoneUnitObjectDetails"),
                ["PlacementZoneUnitObjectToggle"] = Table("PlacementZoneUnitObjectToggle",
                    F("Field_00", PlacementRawFieldKind.Bool, 0),
                    T("Field_01", 1, "PlacementZoneUnitObjectInner")),
                ["PlacementZoneUnitObjectInner"] = Table("PlacementZoneUnitObjectInner",
                    F("Field_00", PlacementRawFieldKind.Float, 0),
                    F("Field_01", PlacementRawFieldKind.Float, 1)),
                ["PlacementZoneSpeciesHolder"] = Table("PlacementZoneSpeciesHolder",
                    T("Field_00", 0, "PlacementZone_F02"),
                    T("Field_01", 1, "PlacementZone_F02_Field1"),
                    F("Species", PlacementRawFieldKind.UInt, 2),
                    F("Form", PlacementRawFieldKind.UInt, 3),
                    F("Gender", PlacementRawFieldKind.UInt, 4),
                    F("Shiny", PlacementRawFieldKind.UInt, 5),
                    F("Unused2", PlacementRawFieldKind.UInt, 6),
                    F("Hash_07", PlacementRawFieldKind.ULong, 7),
                    F("Hash_08", PlacementRawFieldKind.ULong, 8),
                    F("Hash_09", PlacementRawFieldKind.ULong, 9),
                    VT("Field_10", 10, "FlatDummyEntry"),
                    F("Field_11", PlacementRawFieldKind.Float, 11),
                    T("Field_12", 12, "PlacementZone_F02_Nine"),
                    F("Field_13", PlacementRawFieldKind.Int, 13),
                    F("Field_14", PlacementRawFieldKind.Int, 14),
                    F("Num_15", PlacementRawFieldKind.UByte, 15)),
                ["PlacementZone_F02"] = Table("PlacementZone_F02",
                    T("Field_00", 0, TransformTable),
                    F("Hash_01", PlacementRawFieldKind.ULong, 1),
                    F("Hash_02", PlacementRawFieldKind.ULong, 2),
                    F("Hash_03", PlacementRawFieldKind.ULong, 3),
                    F("Hash_04", PlacementRawFieldKind.ULong, 4),
                    F("Field_05", PlacementRawFieldKind.UInt, 5),
                    F("Field_06", PlacementRawFieldKind.UInt, 6),
                    F("Field_07", PlacementRawFieldKind.UInt, 7),
                    F("Field_08", PlacementRawFieldKind.UInt, 8),
                    T("Field_09", 9, "FlatDummyObject"),
                    F("Field_10", PlacementRawFieldKind.UInt, 10),
                    T("Field_11", 11, "FlatDummyObject"),
                    F("Hash_12", PlacementRawFieldKind.ULong, 12)),
                ["PlacementZone_F02_Field1"] = Table("PlacementZone_F02_Field1", T("Field_00", 0, "PlacementZone_F02_Inner")),
                ["PlacementZone_F02_Inner"] = Table("PlacementZone_F02_Inner",
                    T("Field_00", 0, TransformTable),
                    F("Hash_01", PlacementRawFieldKind.ULong, 1),
                    F("Hash_02", PlacementRawFieldKind.ULong, 2),
                    F("Hash_03", PlacementRawFieldKind.ULong, 3),
                    T("Field_04", 4, "PlacementZone_F02_IntFloat"),
                    F("Num_05", PlacementRawFieldKind.UByte, 5),
                    F("Hash_06", PlacementRawFieldKind.ULong, 6),
                    T("Field_07", 7, "PlacementZone_F02_IntFloat")),
                ["PlacementZone_F02_IntFloat"] = NumericIntFloat4("PlacementZone_F02_IntFloat"),
                ["PlacementZoneWarpHolder"] = Table("PlacementZoneWarpHolder", T("Field_00", 0, "PlacementZoneWarp")),
                ["PlacementZoneWarp"] = Table("PlacementZoneWarp",
                    T("Field_00", 0, TransformTable),
                    F("Hash_01", PlacementRawFieldKind.ULong, 1),
                    F("NameAreaOther", PlacementRawFieldKind.String, 2),
                    F("NameModel", PlacementRawFieldKind.String, 3),
                    F("NameAnimation", PlacementRawFieldKind.String, 4),
                    F("Field_05", PlacementRawFieldKind.Int, 5),
                    F("Field_06", PlacementRawFieldKind.Float, 6),
                    F("Field_07", PlacementRawFieldKind.Bool, 7),
                    F("Hash_08", PlacementRawFieldKind.ULong, 8),
                    T("SubMeta", 9, "PlacementZoneWarpDetails8"),
                    F("NameSoundEffect1", PlacementRawFieldKind.String, 10),
                    F("NameSoundEffect2", PlacementRawFieldKind.String, 11)),
                ["PlacementZoneWarpDetails8"] = NumericIntFloat10("PlacementZoneWarpDetails8"),
                ["PlacementZoneStepJumpHolder"] = Table("PlacementZoneStepJumpHolder", T("Field_00", 0, "PlacementZoneStepJump")),
                ["PlacementZoneStepJump"] = Table("PlacementZoneStepJump",
                    T("Field_00", 0, TransformTable),
                    F("Field_01", PlacementRawFieldKind.Float, 1),
                    F("Field_02", PlacementRawFieldKind.Float, 2),
                    F("Field_03", PlacementRawFieldKind.Float, 3),
                    F("Field_04", PlacementRawFieldKind.Float, 4)),
                ["PlacementZoneParticleHolder"] = Table("PlacementZoneParticleHolder", T("Field_00", 0, "PlacementZoneParticle")),
                ["PlacementZoneParticle"] = Table("PlacementZoneParticle",
                    T("Field_00", 0, TransformTable),
                    F("ParticleFile", PlacementRawFieldKind.String, 1),
                    F("Number", PlacementRawFieldKind.UInt, 2)),
                ["PlacementZoneFieldItemHolder"] = Table("PlacementZoneFieldItemHolder", T("Field_00", 0, "PlacementZoneFieldItem")),
                ["PlacementZoneFieldItem"] = Table("PlacementZoneFieldItem",
                    T("Field_00", 0, TransformTable),
                    F("Hash_01", PlacementRawFieldKind.ULong, 1),
                    F("Field_02", PlacementRawFieldKind.String, 2, Label: "Model"),
                    F("Field_03", PlacementRawFieldKind.Float, 3),
                    F("Field_04", PlacementRawFieldKind.Float, 4),
                    F("Hash_05", PlacementRawFieldKind.ULong, 5),
                    VS("Flags", PlacementRawFieldKind.ULongVector, 6),
                    VS("Items", PlacementRawFieldKind.UIntVector, 7),
                    F("Quantity", PlacementRawFieldKind.UByte, 8),
                    T("Field_09", 9, "PlacementZoneFieldItem_A")),
                ["PlacementZoneFieldItem_A"] = Table("PlacementZoneFieldItem_A",
                    F("Field_00", PlacementRawFieldKind.Bool, 0, Label: "Enabled"),
                    T("Field_01", 1, "FlatDummyObject")),
                ["PlacementZoneTriggerHolder"] = Table("PlacementZoneTriggerHolder", T("Object", 0, "PlacementZoneTrigger")),
                ["PlacementZoneTrigger"] = Table("PlacementZoneTrigger",
                    T("Field_00", 0, "PlacementZoneDeepX"),
                    F("TriggerName", PlacementRawFieldKind.ULong, 1),
                    F("Field_02", PlacementRawFieldKind.UInt, 2),
                    T("Field_03", 3, "PlacementZoneDeepY"),
                    F("Field_04", PlacementRawFieldKind.UInt, 4)),
                ["PlacementZoneTrainerHolder"] = Table("PlacementZoneTrainerHolder",
                    T("Field_00", 0, "PlacementZone_F08"),
                    F("Field_01", PlacementRawFieldKind.Float, 1, Label: "Range"),
                    F("TrainerID", PlacementRawFieldKind.ULong, 2),
                    F("Hash_03", PlacementRawFieldKind.ULong, 3),
                    F("MovementPath", PlacementRawFieldKind.ULong, 4),
                    VT("Unknown", 5, "PlacementZone_F08_ArrayEntry"),
                    F("Field_06", PlacementRawFieldKind.UInt, 6, Label: "Behavior"),
                    T("Field_07", 7, "PlacementZone_F08_Nine"),
                    F("Field_08", PlacementRawFieldKind.UInt, 8),
                    F("Field_09", PlacementRawFieldKind.UInt, 9),
                    F("Field_10", PlacementRawFieldKind.UInt, 10),
                    F("Field_11", PlacementRawFieldKind.UInt, 11),
                    F("Field_12", PlacementRawFieldKind.UInt, 12)),
                ["PlacementZone_F08_ArrayEntry"] = Table("PlacementZone_F08_ArrayEntry",
                    F("Field_00", PlacementRawFieldKind.UInt, 0),
                    F("Field_01", PlacementRawFieldKind.UInt, 1),
                    F("Field_02", PlacementRawFieldKind.UInt, 2),
                    F("Field_03", PlacementRawFieldKind.Float, 3),
                    F("Field_04", PlacementRawFieldKind.UByte, 4),
                    F("Field_05", PlacementRawFieldKind.ULong, 5)),
                ["PlacementZone_F08_Nine"] = Table("PlacementZone_F08_Nine",
                    F("Field_00", PlacementRawFieldKind.UByte, 0),
                    F("Field_01", PlacementRawFieldKind.UByte, 1),
                    F("Field_02", PlacementRawFieldKind.UByte, 2),
                    F("Field_03", PlacementRawFieldKind.UInt, 3),
                    F("Hash_04", PlacementRawFieldKind.ULong, 4),
                    F("Field_05", PlacementRawFieldKind.UByte, 5),
                    F("Field_06", PlacementRawFieldKind.UInt, 6),
                    F("Hash_07", PlacementRawFieldKind.ULong, 7),
                    F("Field_08", PlacementRawFieldKind.UInt, 8)),
                ["PlacementZone_F08"] = Table("PlacementZone_F08", T("Field_00", 0, "PlacementZone_F08_A")),
                ["PlacementZone_F08_A"] = Table("PlacementZone_F08_A",
                    T("Field_00", 0, TransformTable),
                    F("Hash_01", PlacementRawFieldKind.ULong, 1),
                    F("HashModel", PlacementRawFieldKind.ULong, 2),
                    F("Hash_03", PlacementRawFieldKind.ULong, 3),
                    T("Field_04", 4, "PlacementZone_F08_IntFloat"),
                    F("Field_06", PlacementRawFieldKind.UInt, 5),
                    F("Hash_06", PlacementRawFieldKind.ULong, 6),
                    T("Field_07", 7, "PlacementZone_F08_IntFloat")),
                ["PlacementZone_F08_IntFloat"] = NumericIntFloat4("PlacementZone_F08_IntFloat"),
                ["PlacementZoneTrainerTipHolder"] = Table("PlacementZoneTrainerTipHolder", T("Field_00", 0, "PlacementZoneTrainerTip")),
                ["PlacementZoneTrainerTip"] = Table("PlacementZoneTrainerTip",
                    T("Field_00", 0, TransformTable),
                    F("Field_01", PlacementRawFieldKind.Float, 1),
                    F("Field_02", PlacementRawFieldKind.Float, 2),
                    F("Field_03", PlacementRawFieldKind.Float, 3),
                    F("Field_04", PlacementRawFieldKind.Float, 4),
                    F("Field_05", PlacementRawFieldKind.ULong, 5, Label: "Sign / Message Hash"),
                    T("Field_06", 6, "PlacementZone_F09"),
                    T("Field_07", 7, "PlacementZone_F09_Union")),
                ["PlacementZone_F09"] = NumericUIntFloat10("PlacementZone_F09"),
                ["PlacementZone_F09_Union"] = Table("PlacementZone_F09_Union",
                    F("Field_00", PlacementRawFieldKind.UByte, 0),
                    T("Field_01", 1, "PlacementZone_F09_Sub")),
                ["PlacementZone_F09_Sub"] = Table("PlacementZone_F09_Sub",
                    F("Field_00", PlacementRawFieldKind.Float, 0),
                    F("Field_01", PlacementRawFieldKind.Float, 1)),
                ["PlacementZoneEnvironmentHolder"] = Table("PlacementZoneEnvironmentHolder", T("Field_00", 0, "PlacementZone_F10")),
                ["PlacementZone_F10"] = Table("PlacementZone_F10",
                    T("Field_00", 0, TransformTable),
                    VT("Field_01", 1, "PlacementZone_V3f"),
                    F("PlayName", PlacementRawFieldKind.String, 2),
                    F("StopName", PlacementRawFieldKind.String, 3),
                    F("Field_04", PlacementRawFieldKind.Float, 4),
                    F("Field_05", PlacementRawFieldKind.Int, 5),
                    F("Field_06", PlacementRawFieldKind.Int, 6)),
                ["PlacementZoneFlightAnchorHolder"] = Table("PlacementZoneFlightAnchorHolder", T("FlightAnchor", 0, "PlacementZoneFlightAnchor")),
                ["PlacementZoneFlightAnchor"] = Table("PlacementZoneFlightAnchor",
                    T("Placement", 0, TransformTable),
                    F("UnlockFlagHash", PlacementRawFieldKind.ULong, 1)),
                ["PlacementZonePokeCenterSpawnAnchorHolder"] = Table("PlacementZonePokeCenterSpawnAnchorHolder", T("Field_00", 0, "PlacementZone_F12")),
                ["PlacementZone_F12"] = Table("PlacementZone_F12", T("Field_00", 0, TransformTable)),
                ["PlacementZoneNPCHolder"] = Table("PlacementZoneNPCHolder", T("Field_00", 0, "PlacementZoneNPC")),
                ["PlacementZoneNPC"] = Table("PlacementZoneNPC",
                    T("Field_00", 0, TransformTable),
                    F("Hash_01", PlacementRawFieldKind.ULong, 1),
                    F("Message", PlacementRawFieldKind.ULong, 2),
                    F("Field_03", PlacementRawFieldKind.UInt, 3),
                    F("WorkValue", PlacementRawFieldKind.ULong, 4),
                    F("Field_05", PlacementRawFieldKind.UInt, 5),
                    F("Field_06", PlacementRawFieldKind.UInt, 6),
                    F("Field_07", PlacementRawFieldKind.UByte, 7),
                    F("Byte_08", PlacementRawFieldKind.UByte, 8),
                    F("Hash_09", PlacementRawFieldKind.ULong, 9)),
                ["PlacementZoneAdvancedTipHolder"] = Table("PlacementZoneAdvancedTipHolder",
                    T("Field_00", 0, "PlacementZoneAdvancedTip"),
                    F("Field_01", PlacementRawFieldKind.UInt, 1),
                    F("Field_02", PlacementRawFieldKind.UInt, 2),
                    F("SignHash", PlacementRawFieldKind.ULong, 3)),
                ["PlacementZoneAdvancedTip"] = Table("PlacementZoneAdvancedTip", T("Field_00", 0, "PlacementZone_F14")),
                ["PlacementZone_F14"] = SharedObjectWithBounds("PlacementZone_F14", "PlacementZone_F14_B", "PlacementZone_F14_Union"),
                ["PlacementZone_F14_B"] = AdvancedTipBounds("PlacementZone_F14_B"),
                ["PlacementZone_F14_Union"] = Table("PlacementZone_F14_Union",
                    F("Field_00", PlacementRawFieldKind.Bool, 0),
                    T("Field_01", 1, "PlacementZone_F14_Sub")),
                ["PlacementZone_F14_Sub"] = Table("PlacementZone_F14_Sub",
                    F("Field_00", PlacementRawFieldKind.Float, 0),
                    F("Field_01", PlacementRawFieldKind.Float, 1)),
                ["PlacementZoneMovementPathHolder"] = Table("PlacementZoneMovementPathHolder",
                    T("Field_00", 0, TransformTable),
                    F("PathName", PlacementRawFieldKind.ULong, 1),
                    F("Field_02", PlacementRawFieldKind.UInt, 2),
                    F("Field_03", PlacementRawFieldKind.UInt, 3),
                    F("Field_04", PlacementRawFieldKind.Bool, 4),
                    VT("Field_05", 5, "PlacementZone_V3f")),
                ["PlacementZoneOtherNPCHolder"] = Table("PlacementZoneOtherNPCHolder",
                    T("Field_00", 0, "PlacementZone_F16"),
                    F("ModelVariant", PlacementRawFieldKind.UInt, 1),
                    F("Hash_02", PlacementRawFieldKind.ULong, 2),
                    F("Hash_03", PlacementRawFieldKind.ULong, 3),
                    VT("Field_04", 4, "PlacementZone_F16_ArrayEntry"),
                    F("Hash_05", PlacementRawFieldKind.ULong, 5),
                    F("Flag_06", PlacementRawFieldKind.Bool, 6),
                    F("Flag_07", PlacementRawFieldKind.Bool, 7),
                    F("Field_08", PlacementRawFieldKind.UInt, 8),
                    F("State", PlacementRawFieldKind.UInt, 9),
                    F("Field_10", PlacementRawFieldKind.Float, 10),
                    T("Field_11", 11, "PlacementZone_F02_Nine"),
                    F("Field_12", PlacementRawFieldKind.UInt, 12),
                    F("AnimationIndexPrimary", PlacementRawFieldKind.UInt, 13),
                    F("Field_14", PlacementRawFieldKind.UInt, 14),
                    F("Field_15", PlacementRawFieldKind.UInt, 15),
                    F("Field_16", PlacementRawFieldKind.UInt, 16)),
                ["PlacementZone_F16_ArrayEntry"] = Table("PlacementZone_F16_ArrayEntry",
                    F("Field_00", PlacementRawFieldKind.UInt, 0),
                    F("Field_01", PlacementRawFieldKind.UInt, 1),
                    F("Field_02", PlacementRawFieldKind.UInt, 2),
                    F("Field_03", PlacementRawFieldKind.Float, 3),
                    F("Field_04", PlacementRawFieldKind.UByte, 4),
                    F("Field_05", PlacementRawFieldKind.Float, 5)),
                ["PlacementZone_F16"] = Table("PlacementZone_F16", T("Field_00", 0, "PlacementZone_F16_A")),
                ["PlacementZone_F16_A"] = Table("PlacementZone_F16_A",
                    T("Identifier", 0, TransformTable),
                    F("Hash_01", PlacementRawFieldKind.ULong, 1),
                    F("HashModel", PlacementRawFieldKind.ULong, 2),
                    F("Hash_03", PlacementRawFieldKind.ULong, 3),
                    T("Field_04", 4, "PlacementZone_F16_IntFloat"),
                    F("Flag_05", PlacementRawFieldKind.Bool, 5),
                    F("HashMessage", PlacementRawFieldKind.ULong, 6),
                    T("Field_07", 7, "PlacementZone_F16_IntFloat")),
                ["PlacementZone_F16_IntFloat"] = NumericIntFloat4("PlacementZone_F16_IntFloat"),
                ["PlacementZoneQuadrantHolder"] = Table("PlacementZoneQuadrantHolder", T("Field_00", 0, "PlacementZone_F17")),
                ["PlacementZone_F17"] = Table("PlacementZone_F17",
                    T("Field_00", 0, TransformTable),
                    F("Hash_01", PlacementRawFieldKind.ULong, 1),
                    T("Field_02", 2, "PlacementZone_F17_Sub")),
                ["PlacementZone_F17_Sub"] = NumericUIntFloat10("PlacementZone_F17_Sub"),
                ["PlacementZoneFishingPointHolder"] = Table("PlacementZoneFishingPointHolder", T("Object", 0, "PlacementZoneFishingPoint")),
                ["PlacementZoneFishingPoint"] = Table("PlacementZoneFishingPoint",
                    T("Identifier", 0, TransformTable),
                    F("Field_01", PlacementRawFieldKind.Float, 1),
                    F("Field_02", PlacementRawFieldKind.Float, 2),
                    F("Field_03", PlacementRawFieldKind.Float, 3),
                    F("Field_04", PlacementRawFieldKind.Float, 4),
                    F("Field_05", PlacementRawFieldKind.Float, 5),
                    F("Field_06", PlacementRawFieldKind.Float, 6),
                    F("Field_07", PlacementRawFieldKind.Float, 7),
                    F("IterateForSlotsExceptLastN", PlacementRawFieldKind.UInt, 8)),
                ["PlacementZoneHiddenItemHolder"] = Table("PlacementZoneHiddenItemHolder", T("Field_00", 0, "PlacementZoneHiddenItem")),
                ["PlacementZoneHiddenItem"] = Table("PlacementZoneHiddenItem",
                    T("Field_00", 0, TransformTable),
                    T("Field_01", 1, "PlacementZoneHiddenItemValue"),
                    VT("Field_02", 2, "PlacementZoneHiddenItemChance"),
                    F("Field_03", PlacementRawFieldKind.Int, 3),
                    F("Field_04", PlacementRawFieldKind.UInt, 4),
                    F("Field_05", PlacementRawFieldKind.Float, 5)),
                ["PlacementZoneHiddenItemValue"] = NumericIntFloat4("PlacementZoneHiddenItemValue"),
                ["PlacementZoneHiddenItemChance"] = Table("PlacementZoneHiddenItemChance",
                    F("Hash", PlacementRawFieldKind.ULong, 0),
                    F("Chance", PlacementRawFieldKind.Int, 1),
                    F("Quantity", PlacementRawFieldKind.Int, 2)),
                ["PlacementZoneSymbolSpawnHolder"] = Table("PlacementZoneSymbolSpawnHolder", T("Object", 0, "PlacementZoneSymbolSpawn")),
                ["PlacementZoneSymbolSpawn"] = Table("PlacementZoneSymbolSpawn",
                    T("Identifier", 0, TransformTable),
                    F("Field_01", PlacementRawFieldKind.Int, 1),
                    T("Field_02", 2, "PlacementZone_F20_Sub"),
                    T("Field_03", 3, "PlacementZone_F20_Sub"),
                    T("Field_04", 4, "PlacementZone_F20_Sub"),
                    T("Field_05", 5, "PlacementZone_F20_Sub"),
                    F("Field_06", PlacementRawFieldKind.Int, 6),
                    F("SymbolHash", PlacementRawFieldKind.ULong, 7)),
                ["PlacementZone_F20_Sub"] = NumericIntFloat10("PlacementZone_F20_Sub"),
                ["PlacementZoneNestHoleHolder"] = Table("PlacementZoneNestHoleHolder",
                    T("Field_00", 0, "PlacementZone_F21_A"),
                    F("Field_01", PlacementRawFieldKind.Bool, 1),
                    F("Field_02", PlacementRawFieldKind.Int, 2),
                    F("Common", PlacementRawFieldKind.ULong, 3),
                    F("Rare", PlacementRawFieldKind.ULong, 4),
                    F("Field_05", PlacementRawFieldKind.Bool, 5),
                    F("EnableSpawns", PlacementRawFieldKind.ULong, 6)),
                ["PlacementZone_F21_A"] = Table("PlacementZone_F21_A", T("Field_00", 0, "PlacementZone_F21_B")),
                ["PlacementZone_F21_B"] = SharedObjectWithBounds("PlacementZone_F21_B", "PlacementZone_F21_IntFloat", "PlacementZone_F21_BoolObject14"),
                ["PlacementZone_F21_IntFloat"] = NumericIntFloat4("PlacementZone_F21_IntFloat"),
                ["PlacementZone_F21_BoolObject14"] = Table("PlacementZone_F21_BoolObject14",
                    F("Type", PlacementRawFieldKind.UByte, 0),
                    T("Object", 1, "PlacementZone_F21_Inner")),
                ["PlacementZone_F21_Inner"] = Table("PlacementZone_F21_Inner",
                    F("Field_00", PlacementRawFieldKind.Float, 0),
                    F("Field_01", PlacementRawFieldKind.Float, 1)),
                ["PlacementZoneBerryTreeHolder"] = Table("PlacementZoneBerryTreeHolder",
                    T("Field_00", 0, "PlacementZone_F22_0"),
                    VT("Field_01", 1, "PlacementZoneBerryTreeRandom")),
                ["PlacementZone_F22_0"] = Table("PlacementZone_F22_0", T("Field_00", 0, "PlacementZone_F22_0_0")),
                ["PlacementZone_F22_0_0"] = SharedObjectWithBounds("PlacementZone_F22_0_0", "PlacementZone_F22_Sub", "PlacementZone_F22_BoolObject14"),
                ["PlacementZone_F22_Sub"] = NumericUIntFloat4("PlacementZone_F22_Sub"),
                ["PlacementZone_F22_BoolObject14"] = Table("PlacementZone_F22_BoolObject14",
                    F("Type", PlacementRawFieldKind.UByte, 0),
                    T("Object", 1, "PlacementZone_F22_Inner")),
                ["PlacementZone_F22_Inner"] = Table("PlacementZone_F22_Inner",
                    F("Field_00", PlacementRawFieldKind.Float, 0),
                    F("Field_01", PlacementRawFieldKind.Float, 1)),
                ["PlacementZoneBerryTreeRandom"] = Table("PlacementZoneBerryTreeRandom",
                    F("Hash", PlacementRawFieldKind.ULong, 0),
                    F("Rate", PlacementRawFieldKind.UInt, 1),
                    F("Quantity", PlacementRawFieldKind.UInt, 2)),
                ["PlacementZoneLadderHolder"] = Table("PlacementZoneLadderHolder", T("Field_00", 0, "PlacementZoneLadder")),
                ["PlacementZoneLadder"] = Table("PlacementZoneLadder",
                    T("Field_00", 0, TransformTable),
                    T("Field_01", 1, "PlacementZone_F23_Sub"),
                    F("Field_02", PlacementRawFieldKind.Int, 2),
                    F("Field_03", PlacementRawFieldKind.Int, 3)),
                ["PlacementZone_F23_Sub"] = NumericIntFloat4("PlacementZone_F23_Sub"),
                ["PlacementZonePopupHolder"] = Table("PlacementZonePopupHolder", T("Field_00", 0, "PlacementZone_F24")),
                ["PlacementZone_F24"] = Table("PlacementZone_F24",
                    T("Field_00", 0, TransformTable),
                    T("Field_01", 1, "PlacementZone_F24_IntFloat"),
                    F("Field_02", PlacementRawFieldKind.Float, 2),
                    F("Field_03", PlacementRawFieldKind.Float, 3),
                    F("Field_04", PlacementRawFieldKind.Float, 4),
                    F("Field_05", PlacementRawFieldKind.Float, 5),
                    F("Hash_06", PlacementRawFieldKind.ULong, 6),
                    F("Field_07", PlacementRawFieldKind.String, 7),
                    VT("Hash_08", 8, "PlacementZone_F24_Table"),
                    F("Field_09", PlacementRawFieldKind.Float, 9),
                    F("Field_10", PlacementRawFieldKind.Float, 10),
                    F("Field_11", PlacementRawFieldKind.Float, 11),
                    F("Hash_12", PlacementRawFieldKind.ULong, 12)),
                ["PlacementZone_F24_Table"] = Table("PlacementZone_F24_Table",
                    F("Hash_00", PlacementRawFieldKind.ULong, 0),
                    F("Hash_01", PlacementRawFieldKind.ULong, 1),
                    F("Field_02", PlacementRawFieldKind.UInt, 2)),
                ["PlacementZone_F24_IntFloat"] = NumericIntFloat4("PlacementZone_F24_IntFloat"),
                ["PlacementZoneIKStepHolder"] = Table("PlacementZoneIKStepHolder",
                    T("Field_00", 0, "PlacementZone_F25"),
                    F("Field_01", PlacementRawFieldKind.UByte, 1),
                    F("Field_02", PlacementRawFieldKind.UByte, 2),
                    F("Field_03", PlacementRawFieldKind.UByte, 3)),
                ["PlacementZone_F25"] = Table("PlacementZone_F25",
                    T("Field_00", 0, TransformTable),
                    F("Field_01", PlacementRawFieldKind.ULong, 1),
                    T("Field_02", 2, "PlacementZone_F25_X")),
                ["PlacementZone_F25_X"] = NumericUIntFloat10("PlacementZone_F25_X"),
                ["PlacementZoneStaticObjectsHolder"] = Table("PlacementZoneStaticObjectsHolder", T("Object", 0, "PlacementZoneStaticObject")),
                ["PlacementZoneStaticObject"] = Table("PlacementZoneStaticObject",
                    T("Identifier", 0, TransformTable),
                    F("Field_01", PlacementRawFieldKind.UInt, 1),
                    F("Rate", PlacementRawFieldKind.UInt, 2),
                    F("Field_03", PlacementRawFieldKind.UInt, 3),
                    F("Field_04", PlacementRawFieldKind.UByte, 4),
                    VT("Spawns", 5, "PlacementZoneStaticObjectSpawn"),
                    T("Field_06", 6, "PlacementZoneStaticObjectUnknown"),
                    T("Field_07", 7, "PlacementZoneStaticObjectUnknown")),
                ["PlacementZoneStaticObjectSpawn"] = Table("PlacementZoneStaticObjectSpawn",
                    F("SpawnID", PlacementRawFieldKind.ULong, 0),
                    F("Behavior", PlacementRawFieldKind.String, 1),
                    F("Field_02", PlacementRawFieldKind.ULong, 2),
                    F("Field_03", PlacementRawFieldKind.UInt, 3),
                    T("Field_04", 4, "PlacementZoneStaticObjectUnknown")),
                ["PlacementZoneStaticObjectUnknown"] = NumericUIntFloat4("PlacementZoneStaticObjectUnknown"),
                ["PlacementZoneRotomRallyEntry"] = Table("PlacementZoneRotomRallyEntry",
                    T("Field_00", 0, TransformTable),
                    F("Field_01", PlacementRawFieldKind.UInt, 1)),
            };

            return tables;
        }

        private static PlacementRawTableSpec SharedObjectWithBounds(
            string name,
            string boundsTable,
            string toggleTable)
        {
            return Table(name,
                T("Field_00", 0, TransformTable),
                F("NameModel", PlacementRawFieldKind.String, 1),
                F("NameAnimation", PlacementRawFieldKind.String, 2),
                F("Field_03", PlacementRawFieldKind.Float, 3),
                F("Field_04", PlacementRawFieldKind.Float, 4),
                F("Field_05", PlacementRawFieldKind.String, 5),
                F("Field_06", PlacementRawFieldKind.String, 6),
                F("Field_07", PlacementRawFieldKind.Float, 7),
                F("Field_08", PlacementRawFieldKind.Float, 8),
                F("Field_09", PlacementRawFieldKind.Float, 9),
                F("Field_10", PlacementRawFieldKind.Float, 10),
                T("Field_11", 11, boundsTable),
                F("Field_12", PlacementRawFieldKind.UInt, 12),
                T("Field_13", 13, boundsTable),
                T("Field_14", 14, toggleTable));
        }

        private static PlacementRawTableSpec NumericDeepX(string name)
        {
            return Table(name,
                F("Field_00", PlacementRawFieldKind.Float, 0),
                F("Field_01", PlacementRawFieldKind.Float, 1),
                F("Field_02", PlacementRawFieldKind.Float, 2),
                F("Field_03", PlacementRawFieldKind.Float, 3),
                F("Field_04", PlacementRawFieldKind.Float, 4),
                F("Field_05", PlacementRawFieldKind.Float, 5),
                F("Field_06", PlacementRawFieldKind.Float, 6),
                F("Field_07", PlacementRawFieldKind.Float, 7),
                F("Field_08", PlacementRawFieldKind.Float, 8),
                F("Field_09", PlacementRawFieldKind.ULong, 9),
                F("Field_10", PlacementRawFieldKind.ULong, 10),
                F("Field_11", PlacementRawFieldKind.ULong, 11));
        }

        private static PlacementRawTableSpec NumericDeepY(string name)
        {
            return Table(name,
                F("Field_00", PlacementRawFieldKind.UInt, 0),
                F("Field_01", PlacementRawFieldKind.Float, 1),
                F("Field_02", PlacementRawFieldKind.Float, 2),
                F("Field_03", PlacementRawFieldKind.Float, 3),
                F("Field_04", PlacementRawFieldKind.Float, 4),
                F("Field_05", PlacementRawFieldKind.Float, 5),
                F("Field_06", PlacementRawFieldKind.Float, 6),
                F("Field_07", PlacementRawFieldKind.Float, 7),
                F("Field_08", PlacementRawFieldKind.Float, 8),
                F("Field_09", PlacementRawFieldKind.Float, 9),
                F("Field_10", PlacementRawFieldKind.Float, 10));
        }

        private static PlacementRawTableSpec NumericIntFloat4(string name)
        {
            return Table(name,
                F("Field_00", PlacementRawFieldKind.Int, 0),
                F("Field_01", PlacementRawFieldKind.Float, 1),
                F("Field_02", PlacementRawFieldKind.Float, 2),
                F("Field_03", PlacementRawFieldKind.Float, 3),
                F("Field_04", PlacementRawFieldKind.Float, 4));
        }

        private static PlacementRawTableSpec NumericUIntFloat4(string name)
        {
            return Table(name,
                F("Field_00", PlacementRawFieldKind.UInt, 0),
                F("Field_01", PlacementRawFieldKind.Float, 1),
                F("Field_02", PlacementRawFieldKind.Float, 2),
                F("Field_03", PlacementRawFieldKind.Float, 3),
                F("Field_04", PlacementRawFieldKind.Float, 4));
        }

        private static PlacementRawTableSpec NumericIntFloat10(string name)
        {
            return Table(name,
                F("Field_00", PlacementRawFieldKind.Int, 0),
                F("Field_01", PlacementRawFieldKind.Float, 1),
                F("Field_02", PlacementRawFieldKind.Float, 2),
                F("Field_03", PlacementRawFieldKind.Float, 3),
                F("Field_04", PlacementRawFieldKind.Float, 4),
                F("Field_05", PlacementRawFieldKind.Float, 5),
                F("Field_06", PlacementRawFieldKind.Float, 6),
                F("Field_07", PlacementRawFieldKind.Float, 7),
                F("Field_08", PlacementRawFieldKind.Float, 8),
                F("Field_09", PlacementRawFieldKind.Float, 9),
                F("Field_10", PlacementRawFieldKind.Float, 10));
        }

        private static PlacementRawTableSpec NumericUIntFloat10(string name)
        {
            return Table(name,
                F("Field_00", PlacementRawFieldKind.UInt, 0),
                F("Field_01", PlacementRawFieldKind.Float, 1),
                F("Field_02", PlacementRawFieldKind.Float, 2),
                F("Field_03", PlacementRawFieldKind.Float, 3),
                F("Field_04", PlacementRawFieldKind.Float, 4),
                F("Field_05", PlacementRawFieldKind.Float, 5),
                F("Field_06", PlacementRawFieldKind.Float, 6),
                F("Field_07", PlacementRawFieldKind.Float, 7),
                F("Field_08", PlacementRawFieldKind.Float, 8),
                F("Field_09", PlacementRawFieldKind.Float, 9),
                F("Field_10", PlacementRawFieldKind.Float, 10));
        }

        private static PlacementRawTableSpec AdvancedTipBounds(string name)
        {
            const string group = "Bounds / Ranges";
            return Table(name,
                F("Field_00", PlacementRawFieldKind.UInt, 0, Label: "Bounds Type", Group: group, DefaultValue: "2", CanRewriteTable: true),
                F("Field_01", PlacementRawFieldKind.Float, 1, Group: group),
                F("Field_02", PlacementRawFieldKind.Float, 2, Group: group),
                F("Field_03", PlacementRawFieldKind.Float, 3, Group: group),
                F("Field_04", PlacementRawFieldKind.Float, 4, Group: group),
                F("Field_05", PlacementRawFieldKind.Float, 5, Group: group),
                F("Field_06", PlacementRawFieldKind.Float, 6, Group: group),
                F("Field_07", PlacementRawFieldKind.Float, 7, Group: group),
                F("Field_08", PlacementRawFieldKind.Float, 8, Group: group),
                F("Field_09", PlacementRawFieldKind.Float, 9, Group: group),
                F("Field_10", PlacementRawFieldKind.Float, 10, Group: group));
        }

        private static PlacementRawTableSpec Table(string name, params PlacementRawFieldSpec[] fields)
        {
            return new PlacementRawTableSpec(name, fields);
        }

        private static PlacementRawFieldSpec F(
            string name,
            PlacementRawFieldKind kind,
            int fieldIndex,
            string Label = "",
            string Group = "",
            string DefaultValue = "",
            bool CanRewriteTable = false)
        {
            return new PlacementRawFieldSpec(name, kind, fieldIndex, Label: Label, Group: Group, DefaultValue: DefaultValue, CanRewriteTable: CanRewriteTable);
        }

        private static PlacementRawFieldSpec T(string name, int fieldIndex, string target)
        {
            return new PlacementRawFieldSpec(name, PlacementRawFieldKind.Table, fieldIndex, target);
        }

        private static PlacementRawFieldSpec VT(string name, int fieldIndex, string target)
        {
            return new PlacementRawFieldSpec(name, PlacementRawFieldKind.TableVector, fieldIndex, target);
        }

        private static PlacementRawFieldSpec VS(string name, PlacementRawFieldKind kind, int fieldIndex)
        {
            return new PlacementRawFieldSpec(name, kind, fieldIndex);
        }
    }

    private sealed class PlacementFlatBufferWriter
    {
        private readonly MemoryStream stream = new();
        private readonly BinaryWriter writer;

        public PlacementFlatBufferWriter()
        {
            writer = new BinaryWriter(stream);
        }

        public byte[] Write(SwShPlacementZoneArchive archive)
        {
            writer.Write(0);
            var archiveTable = WriteTable(fieldCount: 3, objectSize: 20, [4, 8, 16]);
            PatchUOffset(0, archiveTable);
            WriteUInt64(archiveTable + 8, archive.Hash);

            var description = WriteString(archive.Description);
            PatchUOffset(archiveTable + 16, description);

            var zoneVector = WriteTableVector(archive.Zones.Count);
            PatchUOffset(archiveTable + 4, zoneVector);
            for (var index = 0; index < archive.Zones.Count; index++)
            {
                var zone = WriteZone(archive.Zones[index]);
                PatchUOffset(zoneVector + sizeof(int) + (index * sizeof(uint)), zone);
            }

            return stream.ToArray();
        }

        private int WriteZone(SwShPlacementZone zone)
        {
            var offsets = new ushort[20];
            offsets[0] = 4;
            offsets[6] = 8;
            offsets[19] = 12;
            var zoneTable = WriteTable(fieldCount: 20, objectSize: 16, offsets);

            var metaTable = WriteMeta(zone);
            PatchUOffset(zoneTable + 4, metaTable);

            var fieldItems = WriteTableVector(zone.FieldItems.Count);
            PatchUOffset(zoneTable + 8, fieldItems);
            for (var index = 0; index < zone.FieldItems.Count; index++)
            {
                var item = WriteFieldItemHolder(zone.FieldItems[index]);
                PatchUOffset(fieldItems + sizeof(int) + (index * sizeof(uint)), item);
            }

            var hiddenItems = WriteTableVector(zone.HiddenItems.Count);
            PatchUOffset(zoneTable + 12, hiddenItems);
            for (var index = 0; index < zone.HiddenItems.Count; index++)
            {
                var item = WriteHiddenItemHolder(zone.HiddenItems[index]);
                PatchUOffset(hiddenItems + sizeof(int) + (index * sizeof(uint)), item);
            }

            return zoneTable;
        }

        private int WriteMeta(SwShPlacementZone zone)
        {
            var meta = WriteTable(fieldCount: 2, objectSize: 16, [4, 8]);
            WriteUInt64(meta + 8, zone.ZoneId);
            var transform = WriteTransform(zone.Transform, zone.ObjectHash);
            PatchUOffset(meta + 4, transform);
            return meta;
        }

        private int WriteFieldItemHolder(SwShPlacementFieldItem item)
        {
            var holder = WriteTable(fieldCount: 1, objectSize: 8, [4]);
            var fieldItem = WriteTable(fieldCount: 10, objectSize: 40, [4, 24, 8, 0, 0, 32, 12, 16, 20, 0]);
            PatchUOffset(holder + 4, fieldItem);

            var transform = WriteTransform(item.Transform, hashObjectName: 0);
            PatchUOffset(fieldItem + 4, transform);
            var model = WriteString(item.Model);
            PatchUOffset(fieldItem + 8, model);
            var hashes = WriteUInt64Vector(item.ItemHashes);
            PatchUOffset(fieldItem + 12, hashes);
            var itemIds = WriteUInt32Vector(item.ItemIds);
            PatchUOffset(fieldItem + 16, itemIds);
            writer.BaseStream.Position = fieldItem + 20;
            writer.Write(item.Quantity);
            writer.BaseStream.Position = writer.BaseStream.Length;
            return holder;
        }

        private int WriteHiddenItemHolder(SwShPlacementHiddenItem item)
        {
            var holder = WriteTable(fieldCount: 1, objectSize: 8, [4]);
            var hiddenItem = WriteTable(fieldCount: 6, objectSize: 24, [4, 0, 8, 12, 16, 20]);
            PatchUOffset(holder + 4, hiddenItem);

            var transform = WriteTransform(item.Transform, hashObjectName: 0);
            PatchUOffset(hiddenItem + 4, transform);
            var chances = WriteTableVector(item.Chances.Count);
            PatchUOffset(hiddenItem + 8, chances);
            for (var index = 0; index < item.Chances.Count; index++)
            {
                var chance = WriteHiddenChance(item.Chances[index]);
                PatchUOffset(chances + sizeof(int) + (index * sizeof(uint)), chance);
            }

            return holder;
        }

        private int WriteHiddenChance(SwShPlacementHiddenItemChance chance)
        {
            var table = WriteTable(fieldCount: 3, objectSize: 24, [8, 16, 20]);
            WriteUInt64(table + 8, chance.ItemHash);
            WriteInt32(table + 16, chance.Chance);
            WriteInt32(table + 20, chance.Quantity);
            return table;
        }

        private int WriteTransform(SwShPlacementTransform transform, ulong hashObjectName)
        {
            var table = WriteTable(fieldCount: 12, objectSize: 64, [4, 8, 12, 0, 16, 0, 20, 24, 28, 32, 40, 48]);
            WriteSingleRaw(table + 4, transform.X);
            WriteSingleRaw(table + 8, transform.Y);
            WriteSingleRaw(table + 12, transform.Z);
            WriteSingleRaw(table + 16, transform.RotationY);
            WriteSingleRaw(table + 20, 1);
            WriteSingleRaw(table + 24, 1);
            WriteSingleRaw(table + 28, 1);
            WriteUInt64(table + 32, hashObjectName);
            WriteUInt64(table + 40, 0);
            WriteUInt64(table + 48, 0);
            return table;
        }

        private int WriteTableVector(int count)
        {
            var offset = checked((int)stream.Position);
            writer.Write(count);
            for (var index = 0; index < count; index++)
            {
                writer.Write(0);
            }

            return offset;
        }

        private int WriteUInt32Vector(IReadOnlyList<uint> values)
        {
            var offset = checked((int)stream.Position);
            writer.Write(values.Count);
            foreach (var value in values)
            {
                writer.Write(value);
            }

            return offset;
        }

        private int WriteUInt64Vector(IReadOnlyList<ulong> values)
        {
            var offset = checked((int)stream.Position);
            writer.Write(values.Count);
            foreach (var value in values)
            {
                writer.Write(value);
            }

            return offset;
        }

        private int WriteString(string value)
        {
            var data = Encoding.UTF8.GetBytes(value);
            var offset = checked((int)stream.Position);
            writer.Write(data.Length);
            writer.Write(data);
            writer.Write((byte)0);
            return offset;
        }

        private int WriteTable(int fieldCount, ushort objectSize, IReadOnlyList<ushort> fieldOffsets)
        {
            var vtableStart = checked((int)stream.Position);
            writer.Write((ushort)(sizeof(ushort) * (2 + fieldCount)));
            writer.Write(objectSize);
            for (var index = 0; index < fieldCount; index++)
            {
                writer.Write(index < fieldOffsets.Count ? fieldOffsets[index] : (ushort)0);
            }

            var tableStart = checked((int)stream.Position);
            writer.Write(tableStart - vtableStart);
            for (var index = sizeof(int); index < objectSize; index++)
            {
                writer.Write((byte)0);
            }

            return tableStart;
        }

        private void PatchUOffset(int sourceOffset, int targetOffset)
        {
            if (targetOffset < sourceOffset)
            {
                throw new InvalidOperationException("FlatBuffer target offsets must point forward.");
            }

            var position = writer.BaseStream.Position;
            writer.BaseStream.Position = sourceOffset;
            writer.Write((uint)(targetOffset - sourceOffset));
            writer.BaseStream.Position = position;
        }

        private void WriteUInt64(int offset, ulong value)
        {
            var position = writer.BaseStream.Position;
            writer.BaseStream.Position = offset;
            writer.Write(value);
            writer.BaseStream.Position = position;
        }

        private void WriteInt32(int offset, int value)
        {
            var position = writer.BaseStream.Position;
            writer.BaseStream.Position = offset;
            writer.Write(value);
            writer.BaseStream.Position = position;
        }

        private void WriteSingleRaw(int offset, float value)
        {
            var position = writer.BaseStream.Position;
            writer.BaseStream.Position = offset;
            writer.Write(value);
            writer.BaseStream.Position = position;
        }
    }
}

public sealed record PlacementTransformOffsets(
    int X,
    int Y,
    int Z,
    int RotationY);
