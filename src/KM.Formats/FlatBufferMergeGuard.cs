// SPDX-License-Identifier: GPL-3.0-only
using System.Reflection;
using Google.FlatBuffers;

namespace KM.Formats;

/// <summary>Rejects extended tables before a known-schema writer can discard unknown fields.</summary>
public static class FlatBufferMergeGuard
{
    public static void ValidateRoundTrip<T>(T original, byte[] rewritten) where T : struct, IFlatbufferObject
    {
        var reader = typeof(T).GetMethods(BindingFlags.Public | BindingFlags.Static).Single(method =>
            method.Name.StartsWith("GetRootAs", StringComparison.Ordinal) && method.GetParameters() is [{ ParameterType: var parameter }] && parameter == typeof(ByteBuffer));
        var rebuilt = reader.Invoke(null, [new ByteBuffer(rewritten)])!;
        var remaining = 2_000_000;
        Compare(original, rebuilt, 0);

        void Compare(object? first, object? second, int depth)
        {
            if (--remaining < 0 || depth > 48) throw new InvalidDataException("FlatBuffer preservation check exceeds its supported size.");
            if (first is null || second is null)
            {
                if (first is not null || second is not null) throw new InvalidDataException("The writer changes a table's presence.");
                return;
            }
            if (first is not IFlatbufferObject)
            {
                if (!first.Equals(second)) throw new InvalidDataException("The writer changes an unedited field.");
                return;
            }
            var type = first.GetType();
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (property.PropertyType != typeof(ByteBuffer) && !property.PropertyType.IsByRefLike && property.GetIndexParameters().Length == 0)
                    Compare(property.GetValue(first), property.GetValue(second), depth + 1);
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                if (method.GetParameters() is [{ ParameterType: var parameter }] && parameter == typeof(int)
                    && type.GetProperty(method.Name + "Length")?.GetValue(first) is int length)
                {
                    if (length is < 0 or > 100_000) throw new InvalidDataException("FlatBuffer vector exceeds the preservation limit.");
                    for (var index = 0; index < length; index++) Compare(method.Invoke(first, [index]), method.Invoke(second, [index]), depth + 1);
                }
        }
    }

    public static void Validate<T>(T root, int maximumTableEntries = 100_000, int maximumNodes = 500_000) where T : struct, IFlatbufferObject
    {
        var visited = new HashSet<(Type, int)>();
        var nodes = 0;
        Visit(root, 0);

        void Visit(object value, int depth)
        {
            if (depth > 48 || ++nodes > maximumNodes) throw new InvalidDataException("FlatBuffer traversal exceeds its supported limit.");
            var type = value.GetType();
            var backing = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic).FirstOrDefault(field => field.FieldType == typeof(Table))?.GetValue(value);
            if (backing is not Table table) return; // Inline structs have fixed schemas and no extension slots.
            if (!visited.Add((type, table.bb_pos))) return;
            var vtable = checked(table.bb_pos - table.bb.GetInt(table.bb_pos));
            var length = table.bb.GetUshort(vtable);
            if (length < 4 || length % 2 != 0) throw new InvalidDataException("FlatBuffer vtable is invalid.");
            var fields = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Count(method => method.Name.StartsWith("Add", StringComparison.Ordinal)
                    && method.GetParameters().FirstOrDefault()?.ParameterType == typeof(FlatBufferBuilder));
            if (fields == 0)
                fields = type.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(method => method.Name == "Create"
                    && method.GetParameters().FirstOrDefault()?.ParameterType == typeof(FlatBufferBuilder))
                    .Select(method => method.GetParameters().Length - 1).DefaultIfEmpty(0).Max();
            // Keep the move schema extent explicit, including its preserved unnamed flag.
            if (type == typeof(global::SvMoveData)) fields = 73;
            else if (type == typeof(KM.Formats.ZA.Generated.GameData.ZaMoveData)) fields = 73;
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                if (property.Name.EndsWith("Length", StringComparison.Ordinal) && property.PropertyType == typeof(int)
                    && property.GetValue(value) is int count && (count < 0 || count > Math.Max(500_000, maximumTableEntries)))
                    throw new InvalidDataException("FlatBuffer vector exceeds the supported limit.");
            for (var slot = 4 + fields * 2; slot < length; slot += 2)
                if (table.bb.GetUshort(vtable + slot) != 0)
                    throw new InvalidDataException($"FlatBuffer {type.Name} contains populated slot {(slot - 4) / 2} beyond its {fields}-field schema.");
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                if (IsTable(property.PropertyType) && property.GetIndexParameters().Length == 0 && property.GetValue(value) is { } child)
                    Visit(child, depth + 1);
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!IsTable(method.ReturnType) || method.GetParameters() is not [{ ParameterType: var parameter }] || parameter != typeof(int)) continue;
                if (type.GetProperty(method.Name + "Length")?.GetValue(value) is not int count || count < 0 || count > maximumTableEntries)
                    throw new InvalidDataException("FlatBuffer table vector is invalid.");
                for (var index = 0; index < count; index++)
                    if (method.Invoke(value, [index]) is { } child) Visit(child, depth + 1);
            }
        }
    }

    private static bool IsTable(Type type) => typeof(IFlatbufferObject).IsAssignableFrom(Nullable.GetUnderlyingType(type) ?? type);
}
