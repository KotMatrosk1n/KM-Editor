// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Semantics;

namespace KM.Tools.Application;

/// <summary>
/// Conservatively estimates retained semantic objects. These values are admission estimates,
/// not measurements of physical process memory.
/// </summary>
internal static class SemanticExploreSizeEstimator
{
    private const int SnapshotFingerprintLength = 64;
    private const int MaximumSnapshotInstanceIdLength = 1_024;

    internal const long ProjectEnvelopeSizeBytes = 4_096L;
    internal const long LayerEnvelopeSizeBytes = 2_048L;
    internal const long TemporaryIndexEntrySizeBytes = 128L;
    internal const long MaximumLayerEnvelopeSizeBytes = checked(
        LayerEnvelopeSizeBytes
        + 32L + (SnapshotFingerprintLength * 2L)
        + 32L + (MaximumSnapshotInstanceIdLength * 2L));

    internal static long EstimateLayerData(SemanticLayerData layer)
    {
        ArgumentNullException.ThrowIfNull(layer);

        long size = 0;
        foreach (var entity in layer.Entities.Values)
        {
            size = checked(size + EstimateEntity(entity));
        }

        foreach (var reference in layer.References)
        {
            size = checked(size + EstimateReference(reference));
        }

        foreach (var status in layer.DomainStatuses)
        {
            size = checked(size + EstimateStatus(status));
        }

        return size;
    }

    internal static long EstimateEntity(SemanticIndexedEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        long size = 640L;
        size = checked(size + EstimateRecord(entity.Record));
        size = checked(size + EstimateString(entity.Title));
        size = checked(size + EstimateString(entity.Summary));
        size = checked(size + EstimateString(entity.DomainLabel));
        size = checked(size + EstimateString(entity.OwnerId));
        size = checked(size + EstimateString(entity.SourceFile));
        foreach (var field in entity.Fields.Values)
        {
            size = checked(size + EstimateField(field));
        }

        return size;
    }

    internal static long EstimateField(SemanticIndexedField field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return checked(
            256L
            + EstimateString(field.Key)
            + EstimateString(field.Label)
            + EstimateString(field.Group)
            + EstimateString(field.OwnerId)
            + EstimateString(field.Value.CanonicalValue)
            + EstimateString(field.Value.DisplayValue));
    }

    internal static long EstimateReference(SemanticIndexedReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        return checked(
            320L
            + EstimateString(reference.SourceKey)
            + EstimateString(reference.TargetKey)
            + EstimateString(reference.RelationshipKey)
            + EstimateString(reference.RelationshipLabel)
            + EstimateString(reference.ProviderId));
    }

    internal static long EstimateStatus(SemanticDomainStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        return checked(
            192L
            + EstimateString(status.ProviderId)
            + EstimateString(status.Domain)
            + EstimateString(status.ReasonCode));
    }

    internal static long EstimateQueryEntitySelection(SemanticIndexedEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return checked(64L + EstimateString(entity.Record.Domain) + EstimateString(entity.Title));
    }

    internal static long EstimateQueryReferenceSelection(SemanticIndexedReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return checked(
            128L
            + EstimateString(reference.SourceKey)
            + EstimateString(reference.TargetKey)
            + EstimateString(reference.RelationshipKey));
    }

    internal static long EstimateQueryKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return checked(96L + EstimateString(key));
    }

    internal static long EstimateDifference(SemanticDifferenceDto difference)
    {
        ArgumentNullException.ThrowIfNull(difference);
        return checked(
            320L
            + EstimateRecord(difference.Record)
            + EstimateString(difference.FieldKey)
            + EstimateString(difference.Label)
            + EstimateString(difference.OwnerId)
            + EstimateScalar(difference.Left)
            + EstimateScalar(difference.Right));
    }

    internal static long EstimateImpact(SemanticImpactDto impact)
    {
        ArgumentNullException.ThrowIfNull(impact);
        return checked(
            256L
            + EstimateString(impact.RelationshipKey)
            + EstimateString(impact.SourceDomain)
            + EstimateString(impact.Summary));
    }

    internal static long EstimateChange(SemanticChangeDto change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return checked(
            320L
            + EstimateRecord(change.Record)
            + EstimateString(change.Path)
            + EstimateString(change.FieldKey)
            + EstimateScalar(change.Before)
            + EstimateScalar(change.After)
            + EstimateString(change.Line));
    }

    internal static long EstimateOwnershipRow(
        IReadOnlyList<SemanticOwnershipNodeDto> nodes,
        SemanticOwnershipEdgeDto edge,
        IReadOnlyList<SemanticOwnershipConflictDto> conflicts)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edge);
        ArgumentNullException.ThrowIfNull(conflicts);

        long size = 256L;
        foreach (var node in nodes)
        {
            size = checked(
                size
                + 256L
                + EstimateString(node.NodeId)
                + EstimateString(node.Label)
                + EstimateString(node.OwnerId)
                + (node.Record is null ? 0L : EstimateRecord(node.Record)));
        }

        size = checked(
            size
            + 192L
            + EstimateString(edge.SourceNodeId)
            + EstimateString(edge.TargetNodeId));
        foreach (var conflict in conflicts)
        {
            size = checked(
                size
                + 256L
                + EstimateString(conflict.ConflictId)
                + EstimateString(conflict.Label));
            foreach (var nodeId in conflict.NodeIds)
            {
                size = checked(size + 32L + EstimateString(nodeId));
            }
        }

        return size;
    }

    internal static long EstimateRecord(SemanticRecordRefDto record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return checked(
            192L
            + EstimateString(record.Domain)
            + EstimateString(record.RecordKind.Key)
            + EstimateString(record.RecordId)
            + EstimateString(record.SubrecordId));
    }

    private static long EstimateScalar(SemanticScalarValueDto? value)
    {
        return value is null
            ? 0L
            : checked(
                128L
                + EstimateString(value.CanonicalValue)
                + EstimateString(value.DisplayValue));
    }

    internal static long EstimateString(string? value)
    {
        return value is null ? 0L : checked(32L + (value.Length * 2L));
    }
}

internal sealed class SemanticMaterializationBudget
{
    private long estimatedSizeBytes;

    internal SemanticMaterializationBudget(long initialEstimatedSizeBytes = 0)
    {
        if (initialEstimatedSizeBytes < 0
            || initialEstimatedSizeBytes > SemanticIndexSizingLimits.MaximumIndexSizeBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialEstimatedSizeBytes),
                initialEstimatedSizeBytes,
                null);
        }

        estimatedSizeBytes = initialEstimatedSizeBytes;
    }

    internal void Admit(long additionalEstimatedSizeBytes, string failureMessage)
    {
        if (additionalEstimatedSizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(additionalEstimatedSizeBytes),
                additionalEstimatedSizeBytes,
                null);
        }

        if (additionalEstimatedSizeBytes > SemanticIndexSizingLimits.MaximumIndexSizeBytes
            || estimatedSizeBytes
                > SemanticIndexSizingLimits.MaximumIndexSizeBytes - additionalEstimatedSizeBytes)
        {
            throw new SemanticExploreValidationException(
                failureMessage,
                SemanticExploreFailureKind.LimitExceeded);
        }

        estimatedSizeBytes += additionalEstimatedSizeBytes;
    }

    internal IDisposable ReserveTemporary(long estimatedSizeBytes, string failureMessage)
    {
        Admit(estimatedSizeBytes, failureMessage);
        return new TemporaryReservation(this, estimatedSizeBytes);
    }

    private void Release(long estimatedSizeBytes)
    {
        if (estimatedSizeBytes < 0 || estimatedSizeBytes > this.estimatedSizeBytes)
        {
            throw new InvalidOperationException("A semantic materialization reservation is unbalanced.");
        }

        this.estimatedSizeBytes -= estimatedSizeBytes;
    }

    private sealed class TemporaryReservation(
        SemanticMaterializationBudget owner,
        long estimatedSizeBytes) : IDisposable
    {
        private SemanticMaterializationBudget? owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref owner, null)?.Release(estimatedSizeBytes);
        }
    }
}
