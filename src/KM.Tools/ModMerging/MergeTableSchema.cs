// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Google.FlatBuffers;
using KM.Core.ModMerging;

namespace KM.Tools.ModMerging;

/// <summary>Explicit physical schemas for tables whose existing readers expose only selected fields.</summary>
internal sealed class MergeTableSchema(string name, params MergeTableSchema.Field[] fields)
{
    internal sealed record Field(string Name, string Storage, MergeTableSchema? Table = null, bool Vector = false);
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal MergeDocument Read(byte[] bytes, string kind)
    {
        var remaining = 2_000_000;
        var shape = new StringBuilder(name);
        var content = ReadTable(this, Pointer(0), "", 0);
        var identity = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(shape.ToString())));
        return new(content, Write, kind, identity);

        JsonObject ReadTable(MergeTableSchema schema, int position, string path, int depth)
        {
            if (--remaining < 0 || depth > 48) throw new InvalidDataException("Table traversal exceeds the merge limit.");
            Range(position, 4);
            var vtable = checked(position - I32(position)); Range(vtable, 4);
            var extent = U16(vtable); var objectSize = U16(vtable + 2);
            if (extent < 4 || extent % 2 != 0 || objectSize < 4) throw new InvalidDataException("Invalid table extent.");
            Range(vtable, extent); Range(position, objectSize);
            for (var slot = 4 + schema.fields.Length * 2; slot < extent; slot += 2)
                if (U16(vtable + slot) != 0) throw new InvalidDataException("The table extends beyond its registered fields.");
            var result = new JsonObject();
            for (var slot = 0; slot < schema.fields.Length; slot++)
            {
                var field = schema.fields[slot]; var location = 4 + slot * 2; var offset = location < extent ? U16(vtable + location) : 0;
                if (offset == 0) { result[field.Name] = null; continue; }
                var size = field.Vector || field.Table is not null || field.Storage == "string" ? 4 : Width(field.Storage);
                if (offset < 4 || offset > objectSize - size) throw new InvalidDataException("Field falls outside its table.");
                var fieldPath = path + "/" + field.Name;
                if (field.Vector)
                {
                    var vector = Pointer(position + offset); var count = I32(vector);
                    if (count is < 0 or > 100_000) throw new InvalidDataException("Vector exceeds the merge limit.");
                    var stride = field.Table is not null || field.Storage == "string" ? 4 : Width(field.Storage);
                    Range(vector + 4, checked(count * stride)); shape.Append(fieldPath).Append(':').Append(count).Append(';');
                    var values = new JsonObject();
                    for (var index = 0; index < count; index++)
                    {
                        var key = index.ToString("D5");
                        values[key] = ReadValue(field, vector + 4 + index * stride, fieldPath + "/" + key, depth + 1);
                    }
                    result[field.Name] = values;
                }
                else result[field.Name] = ReadValue(field, position + offset, fieldPath, depth + 1);
                if (field.Name is "Id" or "TableId" or "TableKey" or "DevId" or "Label" or "EncounterId" or "Hash" or "No" or "RomVer")
                    shape.Append(fieldPath).Append(':').Append(result[field.Name]?.ToJsonString()).Append(';');
            }
            return result;
        }

        JsonNode? ReadValue(Field field, int position, string path, int depth)
        {
            if (--remaining < 0) throw new InvalidDataException("Table traversal exceeds the merge limit.");
            if (field.Table is not null) return I32(position) == 0 ? null : ReadTable(field.Table, Pointer(position), path, depth);
            if (field.Storage == "string")
            {
                if (I32(position) == 0) return null;
                var text = Pointer(position); var length = I32(text); Range(text + 4, checked(length + 1));
                if (bytes[text + 4 + length] != 0) throw new InvalidDataException("Unterminated table string.");
                return JsonValue.Create(Utf8.GetString(bytes.AsSpan(text + 4, length)));
            }
            Range(position, Width(field.Storage));
            if (field.Storage == "bool" && bytes[position] > 1) throw new InvalidDataException("Boolean field has an unknown encoding.");
            return JsonSerializer.SerializeToNode(Scalar(field.Storage, bytes, position));
        }

        void Range(int offset, int length) { if (offset < 0 || length < 0 || offset > bytes.Length - length) throw new InvalidDataException("Truncated table data."); }
        int I32(int offset) { Range(offset, 4); return BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset)); }
        int U16(int offset) { Range(offset, 2); return BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset)); }
        int Pointer(int offset) { var displacement = I32(offset); if (displacement <= 0) throw new InvalidDataException("Invalid forward table reference."); var result = checked(offset + displacement); Range(result, 4); return result; }
    }

    private readonly Field[] fields = fields;

    private byte[] Write(JsonNode content)
    {
        var builder = new FlatBufferBuilder(1024) { ForceDefaults = true }; var budget = 2_000_000;
        builder.Finish(WriteTable(this, content, 0));
        if (builder.Offset > 64 * 1024 * 1024) throw new InvalidDataException("Merged table exceeds the file limit.");
        return builder.SizedByteArray();

        int WriteTable(MergeTableSchema schema, JsonNode node, int depth)
        {
            if (--budget < 0 || depth > 48 || builder.Offset > 64 * 1024 * 1024) throw new InvalidDataException("Table composition exceeds the merge limit.");
            var obj = node.AsObject();
            if (obj.Count != schema.fields.Length || schema.fields.Any(field => !obj.ContainsKey(field.Name))) throw new InvalidDataException("Merged table shape changed.");
            var offsets = new int[schema.fields.Length];
            for (var slot = 0; slot < schema.fields.Length; slot++)
            {
                var field = schema.fields[slot]; var value = obj[field.Name]; if (value is null) continue;
                if (field.Vector)
                {
                    var values = value.AsObject().OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
                    if (values.Length > 100_000 || values.Where((pair, index) => pair.Key != index.ToString("D5")).Any()) throw new InvalidDataException("Invalid merged vector slots.");
                    var references = field.Table is not null || field.Storage == "string";
                    var targets = references ? values.Select(pair => Reference(field, pair.Value, depth + 1)).ToArray() : [];
                    var width = references ? 4 : Width(field.Storage); builder.StartVector(width, values.Length, width);
                    for (var index = values.Length - 1; index >= 0; index--)
                        if (references) { if (targets[index] == 0) builder.AddInt(0); else builder.AddOffset(targets[index]); }
                        else MergeFlatBuffer.AddScalar(builder, StorageType(field.Storage), values[index].Value!.Deserialize(StorageType(field.Storage))!, null);
                    offsets[slot] = builder.EndVector().Value;
                }
                else if (field.Table is not null || field.Storage == "string") offsets[slot] = Reference(field, value, depth + 1);
            }
            builder.StartTable(schema.fields.Length);
            for (var slot = schema.fields.Length - 1; slot >= 0; slot--)
            {
                var field = schema.fields[slot]; var value = obj[field.Name]; if (value is null) continue;
                if (field.Vector || field.Table is not null || field.Storage == "string") builder.AddOffset(slot, offsets[slot], 0);
                else MergeFlatBuffer.AddScalar(builder, StorageType(field.Storage), value.Deserialize(StorageType(field.Storage))!, slot);
            }
            return builder.EndTable();
        }
        int Reference(Field field, JsonNode? value, int depth) => value is null ? 0 : field.Table is not null ? WriteTable(field.Table, value, depth) : builder.CreateString(value.GetValue<string>()).Value;
    }

    private static int Width(string type) => type switch { "bool" or "ubyte" or "byte" => 1, "short" or "ushort" => 2, "long" or "ulong" or "double" => 8, "int" or "uint" or "float" => 4, _ => throw new InvalidDataException("Unknown field storage.") };
    private static Type StorageType(string type) => type switch { "bool" => typeof(bool), "ubyte" => typeof(byte), "byte" => typeof(sbyte), "short" => typeof(short), "ushort" => typeof(ushort), "int" => typeof(int), "uint" => typeof(uint), "long" => typeof(long), "ulong" => typeof(ulong), "float" => typeof(float), "double" => typeof(double), _ => throw new InvalidDataException("Unknown field storage.") };
    private static object Scalar(string type, byte[] data, int offset) => type switch
    {
        "bool" => data[offset] != 0, "ubyte" => data[offset], "byte" => (sbyte)data[offset],
        "short" => BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(offset)), "ushort" => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset)),
        "int" => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset)), "uint" => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset)),
        "long" => BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(offset)), "ulong" => BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(offset)),
        "float" => BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset)), "double" => BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(offset)),
        _ => throw new InvalidDataException("Unknown field storage."),
    };
}
