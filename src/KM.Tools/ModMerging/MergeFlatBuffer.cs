// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Concurrent;
using System.Reflection;
using Google.FlatBuffers;
using KM.Core.ModMerging;

namespace KM.Tools.ModMerging;

/// <summary>Field composition through explicitly registered, preserving game schemas.</summary>
internal static class MergeFlatBuffer
{
    private sealed record Field(string Name, Type ValueType, PropertyInfo? Property, MethodInfo? Vector, int? FixedIndex = null);
    private sealed record Schema(Field[] Fields);
    private static readonly ConcurrentDictionary<Type, Schema> Schemas = new();

    private static readonly ConcurrentDictionary<Type, MergeTableSchema> PhysicalSchemas = new();

    public static MergeDocument Read<T>(byte[] bytes, string kind) where T : struct, IFlatbufferObject =>
        PhysicalSchema(typeof(T)).Read(bytes, kind);

    private static MergeTableSchema PhysicalSchema(Type type) => PhysicalSchemas.GetOrAdd(type, static type =>
    {
        if (!type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Any(field => field.FieldType == typeof(Table)))
            throw new InvalidDataException("Inline structures require a dedicated schema writer.");
        var fields = GetSchema(type).Fields.Select(field =>
        {
            var valueType = Nullable.GetUnderlyingType(field.ValueType) ?? field.ValueType;
            if (valueType.IsEnum) valueType = Enum.GetUnderlyingType(valueType);
            var nested = typeof(IFlatbufferObject).IsAssignableFrom(valueType) ? PhysicalSchema(valueType) : null;
            var storage = nested is not null ? "table" : Type.GetTypeCode(valueType) switch
            {
                TypeCode.Boolean => "bool", TypeCode.Byte => "ubyte", TypeCode.SByte => "byte",
                TypeCode.Int16 => "short", TypeCode.UInt16 => "ushort", TypeCode.Int32 => "int", TypeCode.UInt32 => "uint",
                TypeCode.Int64 => "long", TypeCode.UInt64 => "ulong", TypeCode.Single => "float", TypeCode.Double => "double",
                TypeCode.String => "string", _ => throw new InvalidDataException("Unsupported schema scalar."),
            };
            return new MergeTableSchema.Field(field.Name, storage, nested, field.Vector is not null && field.FixedIndex is null);
        }).ToArray();
        return new(type.FullName ?? type.Name, fields);
    });

    private static Schema GetSchema(Type type) => Schemas.GetOrAdd(type, static type =>
    {
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static);
        if (type == typeof(KM.Formats.SV.Placement.HiddenItemDataTable))
            return new([MakeField("TableId"), .. Enumerable.Range(0, 10).Select(index => new Field("Item" + index,
                typeof(KM.Formats.SV.Placement.HiddenItemDataTableInfo?), null, type.GetMethod("Item"), index))]);
        var adds = methods.Where(method => method.Name.StartsWith("Add", StringComparison.Ordinal)
            && method.GetParameters() is [{ ParameterType: var first }, _] && first == typeof(FlatBufferBuilder)).OrderBy(method => method.MetadataToken).ToArray();
        var create = adds.Length == 0 ? methods.SingleOrDefault(method => (method.Name == "Create" || method.Name == "Create" + type.Name)
            && method.GetParameters().FirstOrDefault()?.ParameterType == typeof(FlatBufferBuilder)) : null;
        var fields = adds.Length > 0
            ? adds.Select(add => MakeField(add.Name[3..])).ToArray()
            : create?.GetParameters().Skip(1).Select(parameter => MakeField(parameter.Name!.EndsWith("Offset", StringComparison.Ordinal)
                ? parameter.Name[..^6] : parameter.Name)).ToArray()
                ?? throw new InvalidDataException("The registered schema has no preserving builder.");
        return new(fields);

        Field MakeField(string name)
        {
            var property = type.GetProperties().SingleOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            var vector = type.GetMethods().SingleOrDefault(method => method.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                && method.GetParameters() is [{ ParameterType: var parameter }] && parameter == typeof(int));
            if (property is null && vector is null) throw new InvalidDataException("Schema builder and reader fields differ.");
            return new(property?.Name ?? vector!.Name, property?.PropertyType ?? vector!.ReturnType, property, vector);
        }
    });

    internal static void AddScalar(FlatBufferBuilder builder, Type type, object value, int? slot)
    {
        var converted = Convert.ChangeType(value, type, System.Globalization.CultureInfo.InvariantCulture);
        switch (converted)
        {
            case bool valueBool: if (slot is { } sb) builder.AddBool(sb, valueBool, false); else builder.AddBool(valueBool); break;
            case byte valueByte: if (slot is { } sby) builder.AddByte(sby, valueByte, 0); else builder.AddByte(valueByte); break;
            case sbyte valueSbyte: if (slot is { } ssb) builder.AddSbyte(ssb, valueSbyte, 0); else builder.AddSbyte(valueSbyte); break;
            case short valueShort: if (slot is { } ss) builder.AddShort(ss, valueShort, 0); else builder.AddShort(valueShort); break;
            case ushort valueUshort: if (slot is { } sus) builder.AddUshort(sus, valueUshort, 0); else builder.AddUshort(valueUshort); break;
            case int valueInt: if (slot is { } si) builder.AddInt(si, valueInt, 0); else builder.AddInt(valueInt); break;
            case uint valueUint: if (slot is { } sui) builder.AddUint(sui, valueUint, 0); else builder.AddUint(valueUint); break;
            case long valueLong: if (slot is { } sl) builder.AddLong(sl, valueLong, 0); else builder.AddLong(valueLong); break;
            case ulong valueUlong: if (slot is { } sul) builder.AddUlong(sul, valueUlong, 0); else builder.AddUlong(valueUlong); break;
            case float valueFloat: if (slot is { } sf) builder.AddFloat(sf, valueFloat, 0); else builder.AddFloat(valueFloat); break;
            case double valueDouble: if (slot is { } sd) builder.AddDouble(sd, valueDouble, 0); else builder.AddDouble(valueDouble); break;
            default: throw new InvalidDataException("Unsupported schema scalar.");
        }
    }

}
