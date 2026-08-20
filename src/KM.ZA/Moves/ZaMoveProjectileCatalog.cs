// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using KM.ZA.AngeFight;

namespace KM.ZA.Moves;

internal static class ZaMoveProjectileCatalog
{
    public const string ChildInvocationKind = "child";

    public const string LandingInvocationKind = "landing";

    public const string CoreLandingInvocationKind = "coreLanding";

    public static IReadOnlyList<ZaMoveEditableFieldOption> ReadOptions(
        byte[] activeBytes,
        int? maximumVectorEntries = null,
        int? maximumAggregateVectorEntries = null)
    {
        ArgumentNullException.ThrowIfNull(activeBytes);

        var resourcesById = ReadEntries(
                activeBytes,
                maximumVectorEntries,
                maximumAggregateVectorEntries)
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

    public static IReadOnlyDictionary<int, IReadOnlyList<ZaMovePlayerDamageInvocationRecord>>
        ReadPlayerDamageInvocations(
            byte[] activeBytes,
            bool includeVerifiedVanillaTimelineLaunches,
            int? maximumVectorEntries = null,
            int? maximumAggregateVectorEntries = null)
    {
        ArgumentNullException.ThrowIfNull(activeBytes);

        var entries = ReadEntries(
                activeBytes,
                maximumVectorEntries,
                maximumAggregateVectorEntries)
            .ToArray();
        var sourcesByBulletId = entries
            .SelectMany(parent => CreateInvocationSources(parent))
            .GroupBy(source => source.BulletId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ZaMovePlayerDamageInvocationSourceRecord>)group
                    .Select(source => source.Source)
                    .Distinct()
                    .OrderBy(source => source.ParentBulletId)
                    .ThenBy(source => source.Kind, StringComparer.Ordinal)
                    .ToArray());

        return entries
            .Where(entry => entry.AttackId > 0)
            .GroupBy(entry => entry.AttackId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ZaMovePlayerDamageInvocationRecord>)group
                    .OrderBy(entry => entry.Id)
                    .ThenBy(entry => entry.Resource, StringComparer.Ordinal)
                    .Select(entry =>
                    {
                        var sources = sourcesByBulletId.GetValueOrDefault(entry.Id) ?? [];
                        var resourceName = FormatResourceName(entry.Resource);
                        return new ZaMovePlayerDamageInvocationRecord(
                            entry.Id,
                            resourceName,
                            entry.Resource,
                            FormatDamageRole(sources),
                            entry.LifetimeSeconds,
                            entry.IsSelf,
                            sources,
                            includeVerifiedVanillaTimelineLaunches
                                ? ZaMovePlayerDamageTimelineCatalog.GetLaunches(
                                    entry.AttackId,
                                    entry.Id)
                                : [])
                        {
                            IncomingAncestryShape = CreateIncomingAncestryShape(
                                entry.Id,
                                sourcesByBulletId),
                        };
                    })
                    .ToArray());
    }

    public static bool HaveSamePlayerDamageInvocationShape(
        IReadOnlyList<ZaMovePlayerDamageInvocationRecord> active,
        IReadOnlyList<ZaMovePlayerDamageInvocationRecord> verifiedBase)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(verifiedBase);

        return active
            .Select(CreateInvocationShapeKey)
            .SequenceEqual(verifiedBase.Select(CreateInvocationShapeKey), StringComparer.Ordinal);
    }

    private static IEnumerable<ProjectileEntry> ReadEntries(
        byte[] bytes,
        int? maximumVectorEntries,
        int? maximumAggregateVectorEntries)
    {
        var reader = new ZaAngeFlatBufferReader(
            bytes,
            maximumVectorEntries,
            maximumAggregateVectorEntries);
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

                var attackIdOffset = reader.GetFieldOffset(
                    row,
                    fieldIndex: 1,
                    $"BulletId {id} AttackId");
                var attackId = attackIdOffset is null
                    ? 0
                    : reader.ReadInt32(attackIdOffset.Value, $"BulletId {id} AttackId");
                var resourceOffset = reader.GetFieldOffset(row, fieldIndex: 4, $"BulletId {id} resource")
                    ?? throw new InvalidDataException($"BulletId {id} is missing its resource path.");
                var lifetimeOffset = reader.GetFieldOffset(
                    row,
                    fieldIndex: 5,
                    $"BulletId {id} lifetime");
                var lifetime = lifetimeOffset is null
                    ? 0.0f
                    : reader.ReadSingle(lifetimeOffset.Value, $"BulletId {id} lifetime");
                if (!float.IsFinite(lifetime) || lifetime < 0.0f)
                {
                    throw new InvalidDataException(
                        $"BulletId {id} has invalid lifetime {lifetime.ToString(CultureInfo.InvariantCulture)}.");
                }

                var isSelfOffset = reader.GetFieldOffset(
                    row,
                    fieldIndex: 9,
                    $"BulletId {id} IsSelf");
                var childBulletId = ReadOptionalId(reader, row, 22, id, "ChildBulletId");
                var landingBulletId = ReadOptionalId(reader, row, 11, id, "LandingBulletId");
                var coreLandingBulletId = ReadOptionalId(
                    reader,
                    row,
                    13,
                    id,
                    "LandingCoreBulletId");
                var resource = reader.ReadStringReference(
                    resourceOffset,
                    $"BulletId {id} resource");
                if (string.IsNullOrWhiteSpace(resource))
                {
                    throw new InvalidDataException(
                        $"BulletId {id} has an empty object-template resource path.");
                }

                yield return new ProjectileEntry(
                    id,
                    attackId,
                    resource,
                    lifetime,
                    isSelfOffset is not null
                        && reader.ReadBoolean(isSelfOffset.Value, $"BulletId {id} IsSelf"),
                    childBulletId,
                    landingBulletId,
                    coreLandingBulletId);
            }
        }
    }

    private static int ReadOptionalId(
        ZaAngeFlatBufferReader reader,
        int row,
        int fieldIndex,
        int bulletId,
        string fieldName)
    {
        var offset = reader.GetFieldOffset(
            row,
            fieldIndex,
            $"BulletId {bulletId} {fieldName}");
        var value = offset is null
            ? 0
            : reader.ReadInt32(offset.Value, $"BulletId {bulletId} {fieldName}");
        if (value < 0)
        {
            throw new InvalidDataException(
                $"BulletId {bulletId} has invalid {fieldName} {value}.");
        }

        return value;
    }

    private static IEnumerable<InvocationSource> CreateInvocationSources(ProjectileEntry parent)
    {
        if (parent.ChildBulletId > 0)
        {
            yield return new InvocationSource(
                parent.ChildBulletId,
                new ZaMovePlayerDamageInvocationSourceRecord(parent.Id, ChildInvocationKind));
        }

        if (parent.LandingBulletId > 0)
        {
            yield return new InvocationSource(
                parent.LandingBulletId,
                new ZaMovePlayerDamageInvocationSourceRecord(parent.Id, LandingInvocationKind));
        }

        if (parent.CoreLandingBulletId > 0)
        {
            yield return new InvocationSource(
                parent.CoreLandingBulletId,
                new ZaMovePlayerDamageInvocationSourceRecord(parent.Id, CoreLandingInvocationKind));
        }
    }

    private static string FormatDamageRole(
        IReadOnlyList<ZaMovePlayerDamageInvocationSourceRecord> sources)
    {
        var kinds = sources.Select(source => source.Kind).ToHashSet(StringComparer.Ordinal);
        if (kinds.Count > 1)
        {
            return "Attack-bearing bullet with multiple incoming BulletParam links";
        }

        if (kinds.Contains(CoreLandingInvocationKind))
        {
            return "Attack-bearing core landing bullet";
        }

        if (kinds.Contains(LandingInvocationKind))
        {
            return "Attack-bearing landing bullet";
        }

        if (kinds.Contains(ChildInvocationKind))
        {
            return "Attack-bearing child bullet";
        }

        return "Attack-bearing bullet with no incoming BulletParam link";
    }

    private static string CreateInvocationShapeKey(ZaMovePlayerDamageInvocationRecord invocation)
    {
        var sources = string.Join(
            ",",
            invocation.Sources.Select(source =>
                $"{source.ParentBulletId.ToString(CultureInfo.InvariantCulture)}:{source.Kind}"));
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{invocation.BulletId}:{invocation.ResourcePath}:"
            + $"{BitConverter.DoubleToInt64Bits(invocation.LifetimeSeconds):X16}:"
            + $"{invocation.IsSelf}:{sources}:{invocation.IncomingAncestryShape}");
    }

    private static string CreateIncomingAncestryShape(
        int damageBulletId,
        IReadOnlyDictionary<int, IReadOnlyList<ZaMovePlayerDamageInvocationSourceRecord>>
            sourcesByBulletId)
    {
        var pending = new Stack<int>();
        var visited = new HashSet<int>();
        var edges = new HashSet<string>(StringComparer.Ordinal);
        pending.Push(damageBulletId);
        visited.Add(damageBulletId);
        while (pending.Count > 0)
        {
            var childBulletId = pending.Pop();
            foreach (var source in sourcesByBulletId.GetValueOrDefault(childBulletId) ?? [])
            {
                edges.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{source.ParentBulletId}:{source.Kind}>{childBulletId}"));
                if (visited.Add(source.ParentBulletId))
                {
                    pending.Push(source.ParentBulletId);
                }
            }
        }

        return string.Join(",", edges.Order(StringComparer.Ordinal));
    }

    private static string FormatResourceName(string resource)
    {
        var normalized = resource.Replace('/', Path.DirectorySeparatorChar);
        var fileName = Path.GetFileNameWithoutExtension(normalized);
        return string.IsNullOrWhiteSpace(fileName) ? resource : fileName;
    }

    private sealed record ProjectileEntry(
        int Id,
        int AttackId,
        string Resource,
        double LifetimeSeconds,
        bool IsSelf,
        int ChildBulletId,
        int LandingBulletId,
        int CoreLandingBulletId);

    private sealed record InvocationSource(
        int BulletId,
        ZaMovePlayerDamageInvocationSourceRecord Source);
}
