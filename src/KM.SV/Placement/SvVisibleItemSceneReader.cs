// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using pkNX.Structures.FlatBuffers.SV.Trinity;
using System.Globalization;

namespace KM.SV.Placement;

internal static class SvVisibleItemSceneReader
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

    public static IReadOnlyList<SvVisibleItemScenePoint> Read(byte[] bytes, string virtualPath)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);

        var template = TrinitySceneObjectTemplate
            .GetRootAsTrinitySceneObjectTemplate(new ByteBuffer(bytes))
            .UnPack();
        var points = new List<SvVisibleItemScenePoint>();

        foreach (var entry in template.Objects ?? [])
        {
            WalkEntry(entry, virtualPath, inheritedPoint: null, points);
        }

        return points;
    }

    private static void WalkEntry(
        TrinitySceneObjectTemplateEntryT entry,
        string virtualPath,
        ScenePointInfo? inheritedPoint,
        ICollection<SvVisibleItemScenePoint> points)
    {
        var currentPoint = TryReadScenePoint(entry, out var point)
            ? point
            : inheritedPoint;

        if (currentPoint is not null)
        {
            foreach (var subObject in entry.SubObjects ?? [])
            {
                if (!string.Equals(subObject.Type, PropertySheetType, StringComparison.Ordinal))
                {
                    continue;
                }

                var propertySheet = TrinityPropertySheet
                    .GetRootAsTrinityPropertySheet(new ByteBuffer(subObject.Data.ToArray()))
                    .UnPack();
                var fields = ExtractFields(propertySheet);
                if (!IsVisibleItemPropertySheet(propertySheet, fields))
                {
                    continue;
                }

                var itemId = FindInt(fields, ItemFieldCandidates, out var itemFieldName);
                var quantity = FindInt(fields, QuantityFieldCandidates, out var quantityFieldName) ?? 1;
                points.Add(new SvVisibleItemScenePoint(
                    virtualPath,
                    currentPoint.PointName,
                    propertySheet.Name ?? string.Empty,
                    currentPoint.ObjectTemplateName,
                    currentPoint.ObjectTemplatePath,
                    itemId,
                    quantity,
                    itemFieldName,
                    quantityFieldName,
                    currentPoint.X,
                    currentPoint.Y,
                    currentPoint.Z,
                    currentPoint.RotationY,
                    fields));
            }
        }

        foreach (var subObject in entry.SubObjects ?? [])
        {
            if (string.Equals(subObject.Type, ScenePointType, StringComparison.Ordinal)
                || string.Equals(subObject.Type, ObjectTemplateType, StringComparison.Ordinal))
            {
                WalkEntry(subObject, virtualPath, currentPoint, points);
            }
        }
    }

    private static bool TryReadScenePoint(
        TrinitySceneObjectTemplateEntryT entry,
        out ScenePointInfo? point)
    {
        point = null;
        if (string.Equals(entry.Type, ScenePointType, StringComparison.Ordinal))
        {
            var scenePoint = TrinityScenePoint
                .GetRootAsTrinityScenePoint(new ByteBuffer(entry.Data.ToArray()))
                .UnPack();
            point = ToScenePointInfo(scenePoint, objectTemplateName: string.Empty, objectTemplatePath: string.Empty);
            return true;
        }

        if (!string.Equals(entry.Type, ObjectTemplateType, StringComparison.Ordinal))
        {
            return false;
        }

        var template = TrinitySceneObjectTemplateData
            .GetRootAsTrinitySceneObjectTemplateData(new ByteBuffer(entry.Data.ToArray()))
            .UnPack();
        if (string.Equals(template.Type, SceneObjectType, StringComparison.Ordinal))
        {
            var sceneObject = TrinitySceneObject
                .GetRootAsTrinitySceneObject(new ByteBuffer(template.Data.ToArray()))
                .UnPack();
            point = ToScenePointInfo(sceneObject, template.ObjectTemplateName ?? string.Empty, template.ObjectTemplatePath ?? string.Empty);
            return true;
        }

        if (!string.Equals(template.Type, ScenePointType, StringComparison.Ordinal))
        {
            return false;
        }

        var objectScenePoint = TrinityScenePoint
            .GetRootAsTrinityScenePoint(new ByteBuffer(template.Data.ToArray()))
            .UnPack();
        point = ToScenePointInfo(
            objectScenePoint,
            template.ObjectTemplateName ?? string.Empty,
            template.ObjectTemplatePath ?? string.Empty);
        return true;
    }

    private static ScenePointInfo ToScenePointInfo(
        TrinitySceneObjectT sceneObject,
        string objectTemplateName,
        string objectTemplatePath)
    {
        var position = sceneObject.ObjectPosition?.Field02 ?? new pkNX.Structures.FlatBuffers.PackedVec3fT();
        var rotation = sceneObject.ObjectPosition?.Field01 ?? new pkNX.Structures.FlatBuffers.PackedVec3fT();
        return new ScenePointInfo(
            sceneObject.ObjectName ?? objectTemplateName,
            objectTemplateName,
            objectTemplatePath,
            position.X,
            position.Y,
            position.Z,
            rotation.Y);
    }

    private static ScenePointInfo ToScenePointInfo(
        TrinityScenePointT scenePoint,
        string objectTemplateName,
        string objectTemplatePath)
    {
        var position = scenePoint.Position ?? new pkNX.Structures.FlatBuffers.PackedVec3fT();
        return new ScenePointInfo(
            scenePoint.Name ?? objectTemplateName,
            objectTemplateName,
            objectTemplatePath,
            position.X,
            position.Y,
            position.Z,
            0);
    }

    private static Dictionary<string, string> ExtractFields(TrinityPropertySheetT propertySheet)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in propertySheet.Properties ?? [])
        {
            foreach (var field in property.Fields ?? [])
            {
                if (string.IsNullOrWhiteSpace(field.Name) || field.Data is null)
                {
                    continue;
                }

                ExtractField(fields, field.Name, field.Data);
            }
        }

        return fields;
    }

    private static void ExtractField(
        IDictionary<string, string> fields,
        string fieldName,
        TrinityPropertySheetValueUnion value)
    {
        if (value.Type == TrinityPropertySheetValue.TrinityPropertySheetObject
            && value.AsTrinityPropertySheetObject() is { } nestedObject)
        {
            ExtractObjectFields(fields, fieldName, nestedObject);
            return;
        }

        if (value.Type == TrinityPropertySheetValue.TrinityPropertySheetObjectArray
            && value.AsTrinityPropertySheetObjectArray() is { } objectArray)
        {
            var values = objectArray.Value ?? [];
            for (var index = 0; index < values.Count; index++)
            {
                ExtractField(fields, $"{fieldName}.{index.ToString(CultureInfo.InvariantCulture)}", values[index]);
            }

            return;
        }

        var formatted = FormatFieldValue(value);
        if (formatted is not null)
        {
            AddUnique(fields, fieldName, formatted);
        }
    }

    private static void ExtractObjectFields(
        IDictionary<string, string> fields,
        string prefix,
        TrinityPropertySheetObjectT obj)
    {
        foreach (var field in obj.Fields ?? [])
        {
            if (string.IsNullOrWhiteSpace(field.Name) || field.Data is null)
            {
                continue;
            }

            ExtractField(fields, $"{prefix}.{field.Name}", field.Data);
        }
    }

    private static string? FormatFieldValue(TrinityPropertySheetValueUnion value)
    {
        return value.Type switch
        {
            TrinityPropertySheetValue.TrinityPropertySheetField1 => value.AsTrinityPropertySheetField1()?.Value.ToString(CultureInfo.InvariantCulture),
            TrinityPropertySheetValue.TrinityPropertySheetField2 => value.AsTrinityPropertySheetField2()?.Value.ToString(CultureInfo.InvariantCulture),
            TrinityPropertySheetValue.TrinityPropertySheetFieldStringValue => value.AsTrinityPropertySheetFieldStringValue()?.Value,
            TrinityPropertySheetValue.TrinityPropertySheetFieldEnumName => FormatEnumField(value.AsTrinityPropertySheetFieldEnumName()),
            _ => null,
        };
    }

    private static string? FormatEnumField(TrinityPropertySheetFieldEnumNameT? value)
    {
        if (value is null)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(value.Enum)
            ? value.Value.ToString(CultureInfo.InvariantCulture)
            : $"{value.Enum}:{value.Value.ToString(CultureInfo.InvariantCulture)}";
    }

    private static void AddUnique(IDictionary<string, string> fields, string name, string value)
    {
        if (!fields.ContainsKey(name))
        {
            fields.Add(name, value);
            return;
        }

        for (var suffix = 2; suffix < 1000; suffix++)
        {
            var candidate = $"{name}#{suffix.ToString(CultureInfo.InvariantCulture)}";
            if (!fields.ContainsKey(candidate))
            {
                fields.Add(candidate, value);
                return;
            }
        }
    }

    private static bool IsVisibleItemPropertySheet(
        TrinityPropertySheetT propertySheet,
        IReadOnlyDictionary<string, string> fields)
    {
        if (NormalizeKey(propertySheet.Name).Contains("item", StringComparison.Ordinal))
        {
            return true;
        }

        return fields.Keys.Any(key => NormalizeKey(key).Contains("item", StringComparison.Ordinal));
    }

    private static int? FindInt(
        IReadOnlyDictionary<string, string> fields,
        IReadOnlyList<string> candidates,
        out string? fieldName)
    {
        foreach (var candidate in candidates)
        {
            foreach (var field in fields)
            {
                if (!string.Equals(NormalizeKey(field.Key), candidate, StringComparison.Ordinal))
                {
                    continue;
                }

                if (TryParseInt(field.Value, out var value))
                {
                    fieldName = field.Key;
                    return value;
                }
            }
        }

        foreach (var field in fields)
        {
            var normalizedName = NormalizeKey(field.Key);
            if (!candidates.Any(candidate => normalizedName.Contains(candidate, StringComparison.Ordinal)))
            {
                continue;
            }

            if (TryParseInt(field.Value, out var value))
            {
                fieldName = field.Key;
                return value;
            }
        }

        fieldName = null;
        return null;
    }

    private static bool TryParseInt(string value, out int result)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
        {
            return true;
        }

        var separator = value.LastIndexOf(':');
        return separator >= 0
            && int.TryParse(value[(separator + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    private static string NormalizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();
    }

    private sealed record ScenePointInfo(
        string PointName,
        string ObjectTemplateName,
        string ObjectTemplatePath,
        float X,
        float Y,
        float Z,
        float RotationY);
}

internal sealed record SvVisibleItemScenePoint(
    string VirtualPath,
    string PointName,
    string PropertySheetName,
    string ObjectTemplateName,
    string ObjectTemplatePath,
    int? ItemId,
    int Quantity,
    string? ItemFieldName,
    string? QuantityFieldName,
    float X,
    float Y,
    float Z,
    float RotationY,
    IReadOnlyDictionary<string, string> Properties);
