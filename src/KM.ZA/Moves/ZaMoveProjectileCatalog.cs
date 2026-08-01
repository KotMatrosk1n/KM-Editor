// SPDX-License-Identifier: GPL-3.0-only

using KM.ZA.AngeFight;

namespace KM.ZA.Moves;

internal static class ZaMoveProjectileCatalog
{
    public static IReadOnlyList<ZaMoveEditableFieldOption> ReadOptions(byte[] activeBytes)
    {
        ArgumentNullException.ThrowIfNull(activeBytes);

        var resourcesById = ReadEntries(activeBytes)
            .GroupBy(entry => entry.Id)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(entry => FormatResourceName(entry.Resource))
                    .Where(resource => resource.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray());

        return
        [
            new ZaMoveEditableFieldOption(0, "0 None"),
            .. resourcesById
                .Where(entry => entry.Key > 0)
                .OrderBy(entry => entry.Key)
                .Select(entry => new ZaMoveEditableFieldOption(
                    entry.Key,
                    entry.Value.Length switch
                    {
                        0 => entry.Key.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        1 => $"{entry.Key} {entry.Value[0]}",
                        _ => $"{entry.Key} multiple resources",
                    })),
        ];
    }

    private static IEnumerable<ProjectileEntry> ReadEntries(byte[] bytes)
    {
        var reader = new ZaAngeFlatBufferReader(bytes);
        var root = reader.ReadRootTable("bullet parameter array root", maximumFieldCount: 1);
        var groups = reader.ReadTableVector(root, fieldIndex: 0, "bullet parameter groups");
        foreach (var group in groups)
        {
            reader.ValidateTable(group, "bullet parameter group", maximumFieldCount: 1);
            var rows = reader.ReadTableVector(group, fieldIndex: 0, "bullet parameter rows");
            foreach (var row in rows)
            {
                reader.ValidateTable(row, "bullet parameter row", maximumFieldCount: 43);
                var idOffset = reader.GetFieldOffset(row, fieldIndex: 0, "BulletId");
                var id = idOffset is null ? 0 : reader.ReadInt32(idOffset.Value, "BulletId");
                if (id <= 0)
                {
                    continue;
                }

                var resourceOffset = reader.GetFieldOffset(row, fieldIndex: 4, $"BulletId {id} resource")
                    ?? throw new InvalidDataException($"BulletId {id} is missing its resource path.");
                yield return new ProjectileEntry(
                    id,
                    reader.ReadStringReference(resourceOffset, $"BulletId {id} resource"));
            }
        }
    }

    private static string FormatResourceName(string resource)
    {
        var normalized = resource.Replace('/', Path.DirectorySeparatorChar);
        var fileName = Path.GetFileNameWithoutExtension(normalized);
        return string.IsNullOrWhiteSpace(fileName) ? resource : fileName;
    }

    private sealed record ProjectileEntry(int Id, string Resource);
}
