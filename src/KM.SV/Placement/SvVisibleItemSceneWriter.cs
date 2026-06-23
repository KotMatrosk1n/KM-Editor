// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Formats.SV.Generated.TrinityScene;
using System.Globalization;

namespace KM.SV.Placement;

internal static class SvVisibleItemSceneWriter
{
    private const string ObjectTemplateType = "trinity_ObjectTemplate";
    private const string PropertySheetType = "trinity_PropertySheet";
    private const string SceneObjectType = "trinity_SceneObject";
    private const string ScenePointType = "trinity_ScenePoint";

    private static readonly string[] ItemFieldCandidates =
    [
        "itemid",
        "itemno",
        "itemnum",
        "itemindex",
        "item",
        "dropitem",
        "getitem",
    ];

    private static readonly string[] QuantityFieldCandidates =
    [
        "quantity",
        "count",
        "num",
        "itemcount",
        "itemnum",
        "dropcount",
        "amount",
    ];

    public static SvVisibleItemSceneWriteResult Write(
        byte[] bytes,
        IReadOnlyList<SvVisibleItemSceneEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(edits);

        var editsByIndex = edits.ToDictionary(edit => edit.Index);
        var state = new WriteState(editsByIndex);
        var template = TrinitySceneObjectTemplate
            .GetRootAsTrinitySceneObjectTemplate(new ByteBuffer(bytes))
            .UnPack();

        // Keep writer indexing aligned with the reader traversal so staged edits can
        // target the displayed row without requiring stable scene-local identifiers.
        foreach (var entry in template.Objects ?? [])
        {
            WalkEntry(entry, inheritedPoint: false, state);
        }

        foreach (var edit in edits)
        {
            if (!state.SeenIndexes.Contains(edit.Index))
            {
                state.Failures.Add(new SvVisibleItemSceneWriteFailure(
                    edit.Index,
                    string.Empty,
                    "Visible item scene point was not found."));
            }
        }

        return new SvVisibleItemSceneWriteResult(template.SerializeToBinary(), state.Failures);
    }

    private static void WalkEntry(
        TrinitySceneObjectTemplateEntryT entry,
        bool inheritedPoint,
        WriteState state)
    {
        var currentPoint = IsScenePointEntry(entry) || inheritedPoint;

        if (currentPoint)
        {
            // Visible items are stored as property sheets under scene point entries.
            // Some points are nested through object templates, so inheritedPoint keeps
            // those nested sheets eligible without treating unrelated branches as items.
            foreach (var subObject in entry.SubObjects ?? [])
            {
                if (!string.Equals(subObject.Type, PropertySheetType, StringComparison.Ordinal))
                {
                    continue;
                }

                var propertySheet = TrinityPropertySheet
                    .GetRootAsTrinityPropertySheet(new ByteBuffer(subObject.Data.ToArray()))
                    .UnPack();
                if (!IsVisibleItemPropertySheet(propertySheet))
                {
                    continue;
                }

                var index = state.NextIndex++;
                state.SeenIndexes.Add(index);
                if (!state.EditsByIndex.TryGetValue(index, out var edit))
                {
                    continue;
                }

                ApplyEdit(propertySheet, edit, state);
                subObject.Data = [.. propertySheet.SerializeToBinary()];
            }
        }

        foreach (var subObject in entry.SubObjects ?? [])
        {
            if (string.Equals(subObject.Type, ScenePointType, StringComparison.Ordinal)
                || string.Equals(subObject.Type, ObjectTemplateType, StringComparison.Ordinal))
            {
                WalkEntry(subObject, currentPoint, state);
            }
        }
    }

    private static bool IsScenePointEntry(TrinitySceneObjectTemplateEntryT entry)
    {
        if (string.Equals(entry.Type, ScenePointType, StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.Equals(entry.Type, ObjectTemplateType, StringComparison.Ordinal))
        {
            return false;
        }

        var template = TrinitySceneObjectTemplateData
            .GetRootAsTrinitySceneObjectTemplateData(new ByteBuffer(entry.Data.ToArray()))
            .UnPack();
        return string.Equals(template.Type, SceneObjectType, StringComparison.Ordinal)
            || string.Equals(template.Type, ScenePointType, StringComparison.Ordinal);
    }

    private static void ApplyEdit(
        TrinityPropertySheetT propertySheet,
        SvVisibleItemSceneEdit edit,
        WriteState state)
    {
        if (edit.ItemId is { } itemId
            && !TryUpdateIntegerField(propertySheet, ItemFieldCandidates, itemId, out var itemField))
        {
            state.Failures.Add(new SvVisibleItemSceneWriteFailure(
                edit.Index,
                SvPlacementWorkflowService.VisibleItemIdField,
                itemField is null
                    ? "Visible item id field was not found."
                    : $"Visible item id field '{itemField}' is not numeric."));
        }

        if (edit.Quantity is { } quantity
            && !TryUpdateIntegerField(propertySheet, QuantityFieldCandidates, quantity, out var quantityField))
        {
            state.Failures.Add(new SvVisibleItemSceneWriteFailure(
                edit.Index,
                SvPlacementWorkflowService.VisibleQuantityField,
                quantityField is null
                    ? "Visible item quantity field was not found."
                    : $"Visible item quantity field '{quantityField}' is not numeric."));
        }
    }

    private static bool TryUpdateIntegerField(
        TrinityPropertySheetT propertySheet,
        IReadOnlyList<string> candidates,
        int value,
        out string? matchedField)
    {
        matchedField = null;
        var fields = EnumerateFields(propertySheet).ToArray();
        // Field names vary across scene sheets. Match known item/count names only
        // when the backing value is numeric so unrelated properties are preserved.
        var match = fields.FirstOrDefault(field =>
            candidates.Any(candidate => string.Equals(NormalizeKey(field.Path), candidate, StringComparison.Ordinal))
            && TryReadInteger(field.Field.Data, out _))
            ?? fields.FirstOrDefault(field =>
                candidates.Any(candidate => NormalizeKey(field.Path).Contains(candidate, StringComparison.Ordinal))
                && TryReadInteger(field.Field.Data, out _));

        if (match is null)
        {
            return false;
        }

        matchedField = match.Path;
        return TryWriteInteger(match.Field.Data, value);
    }

    private static bool IsVisibleItemPropertySheet(TrinityPropertySheetT propertySheet)
    {
        if (NormalizeKey(propertySheet.Name).Contains("item", StringComparison.Ordinal))
        {
            return true;
        }

        return EnumerateFields(propertySheet)
            .Any(field => NormalizeKey(field.Path).Contains("item", StringComparison.Ordinal));
    }

    private static IEnumerable<PropertyFieldRef> EnumerateFields(TrinityPropertySheetT propertySheet)
    {
        foreach (var property in propertySheet.Properties ?? [])
        {
            foreach (var field in EnumerateObjectFields(property, string.Empty))
            {
                yield return field;
            }
        }
    }

    private static IEnumerable<PropertyFieldRef> EnumerateObjectFields(
        TrinityPropertySheetObjectT obj,
        string prefix)
    {
        foreach (var field in obj.Fields ?? [])
        {
            if (string.IsNullOrWhiteSpace(field.Name) || field.Data is null)
            {
                continue;
            }

            var path = string.IsNullOrWhiteSpace(prefix) ? field.Name : $"{prefix}.{field.Name}";
            yield return new PropertyFieldRef(path, field);

            foreach (var nested in EnumerateNestedFields(path, field.Data))
            {
                yield return nested;
            }
        }
    }

    private static IEnumerable<PropertyFieldRef> EnumerateNestedFields(
        string prefix,
        TrinityPropertySheetValueUnion value)
    {
        // Item values can sit inside nested objects and object arrays. Preserve the
        // dotted path so diagnostics can point at the exact field that failed.
        if (value.Type == TrinityPropertySheetValue.TrinityPropertySheetObject
            && value.AsTrinityPropertySheetObject() is { } nestedObject)
        {
            foreach (var nested in EnumerateObjectFields(nestedObject, prefix))
            {
                yield return nested;
            }
        }

        if (value.Type == TrinityPropertySheetValue.TrinityPropertySheetObjectArray
            && value.AsTrinityPropertySheetObjectArray() is { } objectArray)
        {
            var values = objectArray.Value ?? [];
            for (var index = 0; index < values.Count; index++)
            {
                var arrayPrefix = $"{prefix}.{index.ToString(CultureInfo.InvariantCulture)}";
                foreach (var nested in EnumerateNestedFields(arrayPrefix, values[index]))
                {
                    yield return nested;
                }
            }
        }
    }

    private static bool TryReadInteger(TrinityPropertySheetValueUnion? value, out int parsed)
    {
        parsed = 0;
        if (value is null)
        {
            return false;
        }

        return value.Type switch
        {
            TrinityPropertySheetValue.TrinityPropertySheetField1
                => TryFromUnsigned(value.AsTrinityPropertySheetField1()?.Value, out parsed),
            TrinityPropertySheetValue.TrinityPropertySheetField2
                => TryFromUnsigned(value.AsTrinityPropertySheetField2()?.Value, out parsed),
            TrinityPropertySheetValue.TrinityPropertySheetFieldEnumName
                => TryFromUnsigned(value.AsTrinityPropertySheetFieldEnumName()?.Value, out parsed),
            TrinityPropertySheetValue.TrinityPropertySheetFieldStringValue
                => int.TryParse(
                    value.AsTrinityPropertySheetFieldStringValue()?.Value,
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out parsed),
            _ => false,
        };
    }

    private static bool TryWriteInteger(TrinityPropertySheetValueUnion? value, int nextValue)
    {
        if (value is null || nextValue < 0)
        {
            return false;
        }

        switch (value.Type)
        {
            case TrinityPropertySheetValue.TrinityPropertySheetField1:
                if (value.AsTrinityPropertySheetField1() is not { } field1)
                {
                    return false;
                }

                field1.Value = (ulong)nextValue;
                return true;
            case TrinityPropertySheetValue.TrinityPropertySheetField2:
                if (value.AsTrinityPropertySheetField2() is not { } field2)
                {
                    return false;
                }

                field2.Value = (uint)nextValue;
                return true;
            case TrinityPropertySheetValue.TrinityPropertySheetFieldEnumName:
                if (value.AsTrinityPropertySheetFieldEnumName() is not { } enumField)
                {
                    return false;
                }

                enumField.Value = (uint)nextValue;
                return true;
            case TrinityPropertySheetValue.TrinityPropertySheetFieldStringValue:
                if (value.AsTrinityPropertySheetFieldStringValue() is not { } stringField)
                {
                    return false;
                }

                stringField.Value = nextValue.ToString(CultureInfo.InvariantCulture);
                return true;
            default:
                return false;
        }
    }

    private static bool TryFromUnsigned(ulong? value, out int parsed)
    {
        parsed = 0;
        if (value is null || value > int.MaxValue)
        {
            return false;
        }

        parsed = (int)value.Value;
        return true;
    }

    private static bool TryFromUnsigned(uint? value, out int parsed)
    {
        parsed = 0;
        if (value is null || value > int.MaxValue)
        {
            return false;
        }

        parsed = (int)value.Value;
        return true;
    }

    private static string NormalizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();
    }

    private sealed record PropertyFieldRef(
        string Path,
        TrinityPropertySheetFieldT Field);

    private sealed class WriteState(IReadOnlyDictionary<int, SvVisibleItemSceneEdit> editsByIndex)
    {
        public IReadOnlyDictionary<int, SvVisibleItemSceneEdit> EditsByIndex { get; } = editsByIndex;
        public int NextIndex { get; set; }
        public HashSet<int> SeenIndexes { get; } = [];
        public List<SvVisibleItemSceneWriteFailure> Failures { get; } = [];
    }
}

internal sealed record SvVisibleItemSceneEdit(
    int Index,
    int? ItemId,
    int? Quantity);

internal sealed record SvVisibleItemSceneWriteResult(
    byte[] Bytes,
    IReadOnlyList<SvVisibleItemSceneWriteFailure> Failures);

internal sealed record SvVisibleItemSceneWriteFailure(
    int Index,
    string Field,
    string Message);
