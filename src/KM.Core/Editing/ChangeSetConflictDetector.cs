// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Semantics;

namespace KM.Core.Editing;

public enum ChangeSetCompositionConflictKind
{
    SemanticTarget,
    OwnedOutput,
}

public sealed record ChangeSetCompositionTarget(
    string ChangeSetId,
    string OperationId,
    string Domain,
    string? RecordId,
    string? Field,
    IReadOnlyList<string> OwnedTargets,
    bool IsSessionLocal = false);

public sealed record ChangeSetCompositionConflict(
    ChangeSetCompositionConflictKind Kind,
    ChangeSetCompositionTarget First,
    ChangeSetCompositionTarget Second,
    string Target);

/// <summary>
/// Conservative common conflict detection. Field-addressable edits may share a
/// writer-owned output file, but two operations may never own the same semantic
/// target. Opaque operations conflict when their reviewed plans own the same output.
/// </summary>
public static class ChangeSetConflictDetector
{
    private const int MaximumConflictCount = 256;

    public static IReadOnlyList<ChangeSetCompositionConflict> Detect(
        IEnumerable<ChangeSetCompositionTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var materialized = targets.ToArray();
        var conflicts = new List<ChangeSetCompositionConflict>();
        var seen = new HashSet<ConflictKey>();

        for (var leftIndex = 0; leftIndex < materialized.Length; leftIndex++)
        {
            var left = materialized[leftIndex];
            for (var rightIndex = leftIndex + 1; rightIndex < materialized.Length; rightIndex++)
            {
                var right = materialized[rightIndex];
                if (HasSameSemanticTarget(left, right, out var semanticTarget))
                {
                    Add(
                        conflicts,
                        seen,
                        ChangeSetCompositionConflictKind.SemanticTarget,
                        left,
                        right,
                        semanticTarget);
                    if (conflicts.Count == MaximumConflictCount)
                    {
                        return conflicts;
                    }

                    continue;
                }

                if ((IsOpaque(left) || IsOpaque(right))
                    && TryFindSharedOwnedTarget(left, right, out var ownedTarget))
                {
                    Add(
                        conflicts,
                        seen,
                        ChangeSetCompositionConflictKind.OwnedOutput,
                        left,
                        right,
                        ownedTarget);
                    if (conflicts.Count == MaximumConflictCount)
                    {
                        return conflicts;
                    }
                }
            }
        }

        return conflicts;
    }

    private static bool HasSameSemanticTarget(
        ChangeSetCompositionTarget left,
        ChangeSetCompositionTarget right,
        out string target)
    {
        target = string.Empty;
        if (left.RecordId is null
            || right.RecordId is null
            || !string.Equals(left.Domain, right.Domain, StringComparison.Ordinal)
            || !string.Equals(left.RecordId, right.RecordId, StringComparison.Ordinal))
        {
            return false;
        }

        if (left.Field is not null && right.Field is not null)
        {
            if (!string.Equals(left.Field, right.Field, StringComparison.Ordinal))
            {
                return false;
            }
        }

        target = left.Field is null
            ? $"{left.Domain}/{left.RecordId}"
            : $"{left.Domain}/{left.RecordId}/{left.Field}";
        return true;
    }

    private static bool IsOpaque(ChangeSetCompositionTarget target)
    {
        return target.RecordId is null || target.Field is null;
    }

    private static bool TryFindSharedOwnedTarget(
        ChangeSetCompositionTarget left,
        ChangeSetCompositionTarget right,
        out string target)
    {
        var leftTargets = left.OwnedTargets
            .Select(value => new RelativeOutputPath(value))
            .ToDictionary(path => path.CanonicalKey, path => path.Value, StringComparer.Ordinal);
        foreach (var value in right.OwnedTargets)
        {
            var path = new RelativeOutputPath(value);
            if (leftTargets.TryGetValue(path.CanonicalKey, out var matched))
            {
                target = string.CompareOrdinal(matched, path.Value) <= 0
                    ? matched
                    : path.Value;
                return true;
            }
        }

        target = string.Empty;
        return false;
    }

    private static void Add(
        ICollection<ChangeSetCompositionConflict> conflicts,
        ISet<ConflictKey> seen,
        ChangeSetCompositionConflictKind kind,
        ChangeSetCompositionTarget left,
        ChangeSetCompositionTarget right,
        string target)
    {
        var keyTarget = kind == ChangeSetCompositionConflictKind.OwnedOutput
            ? new RelativeOutputPath(target).CanonicalKey
            : target;
        var key = new ConflictKey(
            kind,
            string.CompareOrdinal(left.OperationId, right.OperationId) <= 0
                ? left.OperationId
                : right.OperationId,
            string.CompareOrdinal(left.OperationId, right.OperationId) <= 0
                ? right.OperationId
                : left.OperationId,
            keyTarget);
        if (seen.Add(key))
        {
            conflicts.Add(new ChangeSetCompositionConflict(kind, left, right, target));
        }
    }

    private readonly record struct ConflictKey(
        ChangeSetCompositionConflictKind Kind,
        string FirstOperationId,
        string SecondOperationId,
        string Target);
}
