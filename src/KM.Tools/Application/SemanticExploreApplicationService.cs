// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using KM.Api.ChangeSets;
using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Items;
using KM.Api.Moves;
using KM.Api.Pokemon;
using KM.Api.Projects;
using KM.Api.Semantics;
using KM.Api.SemanticMerging;
using KM.Api.Workflows;
using KM.Core.Concurrency;
using KM.Core.Files;
using KM.Core.Indexing;
using KM.Core.Projects;
using KM.Core.Semantics;

namespace KM.Tools.Application;

public enum SemanticExploreFailureKind
{
    InvalidData,
    StaleRevision,
    Unsupported,
    InvalidCursor,
    ExternalRejected,
    ExternalSnapshotUnavailable,
    LimitExceeded,
}

public sealed class SemanticExploreValidationException : Exception
{
    public SemanticExploreValidationException(
        string message,
        SemanticExploreFailureKind failureKind,
        Exception? innerException = null)
        : base(message, innerException)
    {
        FailureKind = failureKind;
    }

    public SemanticExploreFailureKind FailureKind { get; }
}

internal sealed record SemanticMergeIndexedLayers(
    SemanticProjectRevisionDto Revision,
    SemanticLayerData Base,
    SemanticSourceSnapshotDto BaseSnapshot,
    SemanticLayerData Layered,
    SemanticSourceSnapshotDto LayeredSnapshot,
    SemanticLayerData Pending,
    SemanticSourceSnapshotDto PendingSnapshot,
    SemanticLayerData SourceA,
    SemanticSourceSnapshotDto SourceASnapshot,
    SemanticLayerData SourceB,
    SemanticSourceSnapshotDto SourceBSnapshot);

internal sealed record SemanticRecipeIndexedLayers(
    SemanticProjectRevisionDto Revision,
    SemanticLayerData Base,
    SemanticSourceSnapshotDto BaseSnapshot,
    SemanticLayerData Layered,
    SemanticSourceSnapshotDto LayeredSnapshot,
    SemanticLayerData Pending,
    SemanticSourceSnapshotDto PendingSnapshot);

internal static class SemanticIndexSizingLimits
{
    private const int ProvisionMultiplier = 4;
    private const int HardCeilingMultiplier = 2;

    internal const int ExpectedEntityCount = 50_000;
    internal const int ProvisionedEntityCount = checked(
        ExpectedEntityCount * ProvisionMultiplier);
    internal const int MaximumEntityCount = checked(
        ProvisionedEntityCount * HardCeilingMultiplier);
    internal const int MaximumFieldCountPerEntity = 512;
    internal const int MaximumComparisonEntityKeyCount = checked(MaximumEntityCount * 2);
    internal const int ExpectedReferenceCount = 2_000_000;
    internal const int ProvisionedReferenceCount = checked(
        ExpectedReferenceCount * ProvisionMultiplier);
    internal const int MaximumReferenceCount = checked(
        ProvisionedReferenceCount * HardCeilingMultiplier);
    internal const int ExpectedOwnershipRowCount = 100_000;
    internal const int ProvisionedOwnershipRowCount = checked(
        ExpectedOwnershipRowCount * ProvisionMultiplier);
    internal const int MaximumOwnershipRowCount = checked(
        ProvisionedOwnershipRowCount * HardCeilingMultiplier);
    internal const long ExpectedIndexSizeBytes = 128L * 1024L * 1024L;
    internal const long ProvisionedIndexSizeBytes = checked(
        ExpectedIndexSizeBytes * ProvisionMultiplier);
    internal const long MaximumIndexSizeBytes = checked(
        ProvisionedIndexSizeBytes * HardCeilingMultiplier);
}

internal sealed record SemanticWorkflowDtoLoaders(
    Func<ItemsWorkflowDto> Items,
    Func<PokemonWorkflowDto> Pokemon,
    Func<MovesWorkflowDto> Moves);

public sealed class SemanticExploreApplicationService
{
    private const int MaximumDomainFilters = 16;
    private const int MaximumPendingSourcesPerEdit = 64;
    private const int MaximumPendingDomainLength = 128;
    private const int MaximumPendingSummaryLength = 8_192;
    private const int MaximumPendingStableIdLength = 1_024;
    private const int MaximumPendingValueLength = 32_768;
    private const int MaximumConflictNodeIds = 32;
    private const int MaximumCanonicalQueryCharacters = 32_768;
    private const int MaximumExternalFileSystemEntries = 200_000;
    private const int MaximumExternalDirectories = 50_000;
    private const int MaximumExternalTraversalDepth = 128;
    private const long MaximumExternalFileBytes = 64L * 1024L * 1024L;
    private const long MaximumExternalAggregateBytes = 512L * 1024L * 1024L;
    private const int MaximumVerifiedSourceObservations = 16;
    private const int SourceMaterializationLockCount = 8;
    private const int MaximumConcurrentCorpusLoads = 3;
    private const long EstimatedCorpusLoadWorkerBytes = 256L * 1024L * 1024L;
    private static readonly BoundedConcurrencyPolicy CorpusLoadPolicy = new(
        "semantic-explore-corpus-load",
        BoundedWorkloadKind.Decode,
        EstimatedCorpusLoadWorkerBytes,
        maximumDegreeOfParallelism: MaximumConcurrentCorpusLoads,
        memoryBudgetDivisor: 8,
        degreeOfParallelismWhenMemoryUnknown: 1);
    private const int SourceObservationTokenLength = 69;
    private const string SourceObservationTokenPrefix = "sob1_";
    private const string QueryMaterializationLimitMessage =
        "The semantic query exceeds its bounded materialization limits.";
    private const string SourceCacheCallerKey = "semantic-explore-source-v1";
    private const string PendingCacheCallerKey = "semantic-explore-pending-v1";
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint VolumeNameNt = 0x00000002;

    private static readonly IReadOnlyList<SemanticFeatureDto> AllFeatures =
    [
        SemanticFeatureDto.Search,
        SemanticFeatureDto.Entity,
        SemanticFeatureDto.Compare,
        SemanticFeatureDto.References,
        SemanticFeatureDto.Impact,
        SemanticFeatureDto.Ownership,
        SemanticFeatureDto.ExternalCompare,
        SemanticFeatureDto.Changes,
    ];

    private readonly Func<ProjectPathsDto, ItemsWorkflowDto> loadItemsFresh;
    private readonly Func<ProjectPathsDto, PokemonWorkflowDto> loadPokemonFresh;
    private readonly Func<ProjectPathsDto, MovesWorkflowDto> loadMovesFresh;
    private readonly Func<ProjectPathsDto, string> captureExactSourceFingerprint;
    private readonly Func<ProjectPathsDto, bool> canLoadCorpusConcurrently;
    private readonly Func<ProjectPathsDto, int, SemanticWorkflowDtoLoaders>?
        prepareCorpusFresh;
    private readonly object cachePublicationSync = new();
    private long cacheInvalidationEpoch;
    private readonly object sourceObservationSync = new();
    private readonly Dictionary<string, VerifiedSourceObservation> sourceObservations = new(
        StringComparer.Ordinal);
    private readonly object[] sourceMaterializationLocks =
    [
        new(), new(), new(), new(), new(), new(), new(), new(),
    ];
    private long sourceObservationAccessSequence;
    private readonly BoundedDerivedIndexCache<SemanticSourceIndex> sourceCache = new(
        new BoundedDerivedIndexCacheOptions
        {
            MaximumEntryCount = 8,
            MaximumSizeBytes = SemanticIndexSizingLimits.MaximumIndexSizeBytes,
        });
    private readonly BoundedDerivedIndexCache<SemanticPendingOverlay> pendingOverlayCache = new(
        new BoundedDerivedIndexCacheOptions
        {
            MaximumEntryCount = 4,
            MaximumSizeBytes = SemanticIndexSizingLimits.MaximumIndexSizeBytes,
        });
    private readonly BoundedDerivedIndexCache<SemanticIndexedLayer> externalCache = new(
        new BoundedDerivedIndexCacheOptions
        {
            MaximumEntryCount = 4,
            MaximumSizeBytes = SemanticIndexSizingLimits.MaximumIndexSizeBytes,
        });
    private readonly BoundedDerivedIndexCache<SemanticExternalRegistration> semanticMergeExternalCache = new(
        new BoundedDerivedIndexCacheOptions
        {
            MaximumEntryCount = 4,
            MaximumSizeBytes = SemanticIndexSizingLimits.MaximumIndexSizeBytes,
        });

    public SemanticExploreApplicationService(
        Func<ProjectPathsDto, ItemsWorkflowDto> loadItemsFresh,
        Func<ProjectPathsDto, PokemonWorkflowDto> loadPokemonFresh,
        Func<ProjectPathsDto, MovesWorkflowDto> loadMovesFresh,
        Func<ProjectPathsDto, string> captureExactSourceFingerprint,
        Func<ProjectPathsDto, bool>? canLoadCorpusConcurrently = null)
        : this(
            loadItemsFresh,
            loadPokemonFresh,
            loadMovesFresh,
            captureExactSourceFingerprint,
            canLoadCorpusConcurrently,
            prepareCorpusFresh: null)
    {
    }

    internal SemanticExploreApplicationService(
        Func<ProjectPathsDto, ItemsWorkflowDto> loadItemsFresh,
        Func<ProjectPathsDto, PokemonWorkflowDto> loadPokemonFresh,
        Func<ProjectPathsDto, MovesWorkflowDto> loadMovesFresh,
        Func<ProjectPathsDto, string> captureExactSourceFingerprint,
        Func<ProjectPathsDto, bool>? canLoadCorpusConcurrently,
        Func<ProjectPathsDto, int, SemanticWorkflowDtoLoaders>? prepareCorpusFresh)
    {
        this.loadItemsFresh = loadItemsFresh ?? throw new ArgumentNullException(nameof(loadItemsFresh));
        this.loadPokemonFresh = loadPokemonFresh
            ?? throw new ArgumentNullException(nameof(loadPokemonFresh));
        this.loadMovesFresh = loadMovesFresh ?? throw new ArgumentNullException(nameof(loadMovesFresh));
        this.captureExactSourceFingerprint = captureExactSourceFingerprint
            ?? throw new ArgumentNullException(nameof(captureExactSourceFingerprint));
        this.canLoadCorpusConcurrently = canLoadCorpusConcurrently ?? (_ => false);
        this.prepareCorpusFresh = prepareCorpusFresh;
    }

    internal string RegisterVerifiedSourceObservation(
        string projectId,
        ProjectPathsDto paths,
        string sourceFingerprint)
    {
        var scope = new SemanticExploreScopeDto(projectId, paths);
        ValidateScope(scope);
        if (!IsSha256Fingerprint(sourceFingerprint))
        {
            throw new SemanticExploreValidationException(
                "The verified project source observation is invalid.",
                SemanticExploreFailureKind.InvalidData);
        }

        var scopeIdentity = SourceObservationScopeIdentity(scope);
        var normalizedFingerprint = sourceFingerprint.ToLowerInvariant();
        string verifiedToken;
        bool sourceChanged;
        lock (sourceObservationSync)
        {
            sourceChanged = sourceObservations.Values.Any(observation =>
                string.Equals(
                    observation.ScopeIdentity,
                    scopeIdentity,
                    StringComparison.Ordinal)
                && !string.Equals(
                    observation.SourceFingerprint,
                    normalizedFingerprint,
                    StringComparison.Ordinal));
            var matching = sourceObservations.Values.FirstOrDefault(observation =>
                string.Equals(observation.ScopeIdentity, scopeIdentity, StringComparison.Ordinal)
                && string.Equals(
                    observation.SourceFingerprint,
                    normalizedFingerprint,
                    StringComparison.Ordinal));
            if (matching is not null)
            {
                RemoveSourceObservationsForScope(
                    scopeIdentity,
                    exceptToken: matching.Token);
                TouchSourceObservation(matching);
                verifiedToken = matching.Token;
            }
            else
            {
                RemoveSourceObservationsForScope(scopeIdentity, exceptToken: null);
                do
                {
                    verifiedToken = SourceObservationTokenPrefix
                        + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
                }
                while (sourceObservations.ContainsKey(verifiedToken));

                AddSourceObservation(
                    verifiedToken,
                    scopeIdentity,
                    normalizedFingerprint);
            }
        }

        if (sourceChanged)
        {
            ClearDerivedCaches();
        }

        return verifiedToken;
    }

    public void ClearMemoryCaches()
    {
        lock (sourceObservationSync)
        {
            sourceObservations.Clear();
            sourceObservationAccessSequence = 0;
        }

        ClearDerivedCaches();
    }

    internal string ReadSourceCacheIdentity(SemanticExploreScopeDto scope)
    {
        ValidateScope(scope);
        var family = ToFamily(scope.Paths.SelectedGame!.Value);
        return CaptureSourceObservation(scope, family).Fingerprint;
    }

    public ReadSemanticCapabilitiesResponse ReadCapabilities(ReadSemanticCapabilitiesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var index = BuildCurrentIndex(request.Scope);
        return new ReadSemanticCapabilitiesResponse(
            index.Revision,
            OrderedSnapshots(index),
            index.Pending.Data.DomainStatuses
                .OrderBy(status => status.ProviderId, StringComparer.Ordinal)
                .Select(status => new SemanticProviderDescriptorDto(
                    status.ProviderId,
                    [status.Domain],
                    status.Available ? AllFeatures : [],
                    DescriptorCoverage(status)))
                .ToArray());
    }

    public SearchSemanticResponse Search(SearchSemanticRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var index = BuildAndValidate(request.Scope, request.ExpectedRevision);
        ValidatePage(request.Limit, request.Cursor);
        var searchText = NormalizeSearchText(request.SearchText);
        var domains = NormalizeDomains(request.Domains);
        var layer = GetCurrentLayer(index, request.Layer);
        var queryFingerprint = QueryFingerprint(
            "search",
            index.Revision.Fingerprint,
            request.Layer.ToString(),
            searchText,
            string.Join(',', domains));
        var offset = DecodeCursor(request.Cursor, queryFingerprint);

        var baseEntities = index.Base.Data.Entities;
        var queryBudget = new SemanticMaterializationBudget();
        var matches = new List<SemanticIndexedEntity>();
        foreach (var entity in layer.Data.Entities.Values)
        {
            if ((domains.Count > 0 && !domains.Contains(entity.Record.Domain))
                || !SearchMatches(entity, searchText))
            {
                continue;
            }

            AdmitQueryRow(
                queryBudget,
                matches.Count,
                SemanticIndexSizingLimits.MaximumEntityCount,
                SemanticExploreSizeEstimator.EstimateQueryEntitySelection(entity));
            matches.Add(entity);
        }

        matches.Sort(static (left, right) =>
        {
            var comparison = StringComparer.Ordinal.Compare(
                left.Record.Domain,
                right.Record.Domain);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = StringComparer.OrdinalIgnoreCase.Compare(left.Title, right.Title);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = SemanticNumericStringComparer.Instance.Compare(
                left.Record.RecordId,
                right.Record.RecordId);
            return comparison != 0
                ? comparison
                : StringComparer.Ordinal.Compare(RecordKey(left.Record), RecordKey(right.Record));
        });
        var page = Page(matches, offset, request.Limit, queryFingerprint);
        var items = page.Items.Select(entity =>
        {
            var key = RecordKey(entity.Record);
            return new SemanticSearchResultDto(
                entity.Record,
                entity.Title,
                entity.Summary,
                entity.DomainLabel,
                layer.Snapshot,
                request.Layer == SemanticSourceLayerKindDto.Base
                    ? null
                    : ClassifyEntityChange(baseEntities.GetValueOrDefault(key), entity));
        }).ToArray();

        return new SearchSemanticResponse(
            index.Revision,
            queryFingerprint,
            items,
            Coverage(layer, SemanticFeatureDto.Search),
            page.NextCursor);
    }

    public ReadSemanticEntityResponse ReadEntity(ReadSemanticEntityRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var index = BuildAndValidate(request.Scope, request.ExpectedRevision);
        var layer = GetCurrentLayer(index, request.Layer);
        var record = ValidateRecord(request.Record, index.Revision.GameFamily);
        var key = RecordKey(record);
        if (!layer.Data.Entities.TryGetValue(key, out var entity))
        {
            throw new SemanticExploreValidationException(
                "The requested semantic entity is unavailable in this source layer.",
                SemanticExploreFailureKind.Unsupported);
        }

        var queryFingerprint = QueryFingerprint(
            "entity",
            index.Revision.Fingerprint,
            request.Layer.ToString(),
            key);
        return new ReadSemanticEntityResponse(
            index.Revision,
            queryFingerprint,
            new SemanticEntityDto(
                entity.Record,
                entity.Title,
                entity.Summary,
                layer.Snapshot,
                entity.Fields.Values
                    .OrderBy(field => field.Group, StringComparer.Ordinal)
                    .ThenBy(field => field.Key, StringComparer.Ordinal)
                    .Select(field => new SemanticEntityFieldDto(
                        field.Key,
                        field.Label,
                        field.Group,
                        field.Value,
                        field.OwnerId))
                    .ToArray(),
                new SemanticEntityFeaturesDto(
                    Compare: true,
                    References: true,
                    Impact: true,
                    Ownership: true)),
            Coverage(layer, SemanticFeatureDto.Entity));
    }

    public CompareSemanticResponse Compare(CompareSemanticRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var index = BuildAndValidate(request.Scope, request.ExpectedRevision);
        ValidatePage(request.Limit, request.Cursor);
        var left = GetCurrentLayer(index, request.Left);
        var right = GetCurrentLayer(index, request.Right);
        var record = request.Record is null
            ? null
            : ValidateRecord(request.Record, index.Revision.GameFamily);
        return CompareLayers(
            index,
            left,
            right,
            record,
            request.Limit,
            request.Cursor,
            "compare");
    }

    public QuerySemanticReferencesResponse QueryReferences(QuerySemanticReferencesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Direction))
        {
            throw new SemanticExploreValidationException(
                "The semantic reference direction is invalid.",
                SemanticExploreFailureKind.InvalidData);
        }

        var index = BuildAndValidate(request.Scope, request.ExpectedRevision);
        ValidatePage(request.Limit, request.Cursor);
        var layer = GetCurrentLayer(index, request.Layer);
        var record = ValidateRecord(request.Record, index.Revision.GameFamily);
        var recordKey = RecordKey(record);
        RequireEntity(layer, recordKey);
        var queryFingerprint = QueryFingerprint(
            "references",
            index.Revision.Fingerprint,
            request.Layer.ToString(),
            request.Direction.ToString(),
            recordKey);
        var offset = DecodeCursor(request.Cursor, queryFingerprint);
        var queryBudget = new SemanticMaterializationBudget();
        var referenceKeys = new HashSet<(string SourceKey, string TargetKey, string RelationshipKey)>();
        var references = new List<SemanticIndexedReference>();
        foreach (var reference in layer.Data.References)
        {
            var matchesDirection = request.Direction == SemanticReferenceDirectionDto.Incoming
                ? reference.TargetKey == recordKey
                : reference.SourceKey == recordKey;
            var identity = (
                reference.SourceKey,
                reference.TargetKey,
                reference.RelationshipKey);
            if (!matchesDirection || referenceKeys.Contains(identity))
            {
                continue;
            }

            AdmitQueryRow(
                queryBudget,
                references.Count,
                SemanticIndexSizingLimits.MaximumReferenceCount,
                SemanticExploreSizeEstimator.EstimateQueryReferenceSelection(reference));
            referenceKeys.Add(identity);
            references.Add(reference);
        }

        references.Sort(static (left, right) =>
        {
            var comparison = StringComparer.Ordinal.Compare(
                left.RelationshipKey,
                right.RelationshipKey);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = StringComparer.Ordinal.Compare(left.SourceKey, right.SourceKey);
            return comparison != 0
                ? comparison
                : StringComparer.Ordinal.Compare(left.TargetKey, right.TargetKey);
        });
        var page = Page(references, offset, request.Limit, queryFingerprint);
        var items = page.Items.Select(reference =>
        {
            var source = layer.Data.Entities[reference.SourceKey];
            var target = layer.Data.Entities[reference.TargetKey];
            return new SemanticReferenceDto(
                source.Record,
                target.Record,
                reference.RelationshipKey,
                reference.RelationshipLabel,
                SemanticConfidenceDto.Verified,
                reference.ProviderId,
                source.Title,
                target.Title,
                layer.Snapshot);
        }).ToArray();

        return new QuerySemanticReferencesResponse(
            index.Revision,
            queryFingerprint,
            items,
            Coverage(layer, SemanticFeatureDto.References),
            page.NextCursor);
    }

    public QuerySemanticImpactResponse QueryImpact(QuerySemanticImpactRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var index = BuildAndValidate(request.Scope, request.ExpectedRevision);
        ValidatePage(request.Limit, request.Cursor);
        var layer = GetCurrentLayer(index, request.Layer);
        var record = ValidateRecord(request.Record, index.Revision.GameFamily);
        var recordKey = RecordKey(record);
        RequireEntity(layer, recordKey);
        var queryFingerprint = QueryFingerprint(
            "impact",
            index.Revision.Fingerprint,
            request.Layer.ToString(),
            recordKey);
        var offset = DecodeCursor(request.Cursor, queryFingerprint);
        var queryBudget = new SemanticMaterializationBudget();
        var referenceKeys = new HashSet<(string SourceKey, string TargetKey, string RelationshipKey)>();
        var aggregateCounts = new Dictionary<(string RelationshipKey, string SourceDomain), int>();
        foreach (var reference in layer.Data.References)
        {
            var identity = (
                reference.SourceKey,
                reference.TargetKey,
                reference.RelationshipKey);
            if (reference.TargetKey != recordKey || referenceKeys.Contains(identity))
            {
                continue;
            }

            AdmitQueryRow(
                queryBudget,
                referenceKeys.Count,
                SemanticIndexSizingLimits.MaximumReferenceCount,
                SemanticExploreSizeEstimator.EstimateQueryReferenceSelection(reference));
            referenceKeys.Add(identity);

            var aggregateKey = (
                RelationshipKey: reference.RelationshipKey,
                SourceDomain: layer.Data.Entities[reference.SourceKey].Record.Domain);
            if (aggregateCounts.TryGetValue(aggregateKey, out var count))
            {
                aggregateCounts[aggregateKey] = checked(count + 1);
            }
            else
            {
                AdmitQueryRow(
                    queryBudget,
                    aggregateCounts.Count,
                    SemanticIndexSizingLimits.MaximumReferenceCount,
                    checked(
                        SemanticExploreSizeEstimator.EstimateQueryKey(aggregateKey.RelationshipKey)
                        + SemanticExploreSizeEstimator.EstimateQueryKey(aggregateKey.SourceDomain)));
                aggregateCounts.Add(aggregateKey, 1);
            }
        }

        var impacts = new List<SemanticImpactDto>(aggregateCounts.Count);
        foreach (var (aggregateKey, count) in aggregateCounts)
        {
            var impact = new SemanticImpactDto(
                aggregateKey.RelationshipKey,
                aggregateKey.SourceDomain,
                count,
                SemanticImpactSeverityDto.Info,
                SemanticImpactActionabilityDto.ReadOnly,
                $"{count.ToString(CultureInfo.InvariantCulture)} verified consumer(s)");
            AdmitQueryRow(
                queryBudget,
                impacts.Count,
                SemanticIndexSizingLimits.MaximumReferenceCount,
                SemanticExploreSizeEstimator.EstimateImpact(impact));
            impacts.Add(impact);
        }

        impacts.Sort(static (left, right) =>
        {
            var comparison = right.Count.CompareTo(left.Count);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = StringComparer.Ordinal.Compare(
                left.RelationshipKey,
                right.RelationshipKey);
            return comparison != 0
                ? comparison
                : StringComparer.Ordinal.Compare(left.SourceDomain, right.SourceDomain);
        });
        var page = Page(impacts, offset, request.Limit, queryFingerprint);
        return new QuerySemanticImpactResponse(
            index.Revision,
            queryFingerprint,
            page.Items,
            Coverage(layer, SemanticFeatureDto.Impact),
            page.NextCursor);
    }

    public QuerySemanticOwnershipResponse QueryOwnership(QuerySemanticOwnershipRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var index = BuildAndValidate(request.Scope, request.ExpectedRevision);
        ValidatePage(request.Limit, request.Cursor);
        var record = request.Record is null
            ? null
            : ValidateRecord(request.Record, index.Revision.GameFamily);
        var recordKey = record is null ? null : RecordKey(record);
        if (recordKey is not null)
        {
            RequireEntity(index.Pending, recordKey);
        }

        var queryFingerprint = QueryFingerprint(
            "ownership",
            index.Revision.Fingerprint,
            recordKey ?? "all");
        var offset = DecodeCursor(request.Cursor, queryFingerprint);
        var rows = BuildOwnershipRows(index, request.Scope.PendingSession, recordKey);
        var page = Page(
            rows,
            offset,
            Math.Min(request.Limit, SemanticExploreContract.MaximumPageSize / 2),
            queryFingerprint);
        var nodes = page.Items
            .SelectMany(row => row.Nodes)
            .DistinctBy(node => node.NodeId)
            .OrderBy(node => node.NodeId, StringComparer.Ordinal)
            .ToArray();
        var edges = page.Items
            .Select(row => row.Edge)
            .Distinct()
            .OrderBy(edge => edge.SourceNodeId, StringComparer.Ordinal)
            .ThenBy(edge => edge.TargetNodeId, StringComparer.Ordinal)
            .ThenBy(edge => edge.Kind)
            .ToArray();
        var conflicts = page.Items
            .SelectMany(row => row.Conflicts)
            .DistinctBy(conflict => conflict.ConflictId)
            .OrderBy(conflict => conflict.ConflictId, StringComparer.Ordinal)
            .ToArray();
        return new QuerySemanticOwnershipResponse(
            index.Revision,
            queryFingerprint,
            nodes,
            edges,
            conflicts,
            Coverage(index.Pending, SemanticFeatureDto.Ownership),
            page.NextCursor);
    }

    public CompareSemanticResponse CompareExternal(CompareExternalSemanticRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var index = BuildAndValidate(request.Scope, request.ExpectedRevision);
        ValidatePage(request.Limit, request.Cursor);
        var external = ResolveExternalLayer(index, request);
        var left = GetCurrentLayer(index, request.Left);
        var record = request.Record is null
            ? null
            : ValidateRecord(request.Record, index.Revision.GameFamily);
        return CompareLayers(
            index,
            left,
            external,
            record,
            request.Limit,
            request.Cursor,
            "external-compare");
    }

    internal SemanticMergeSourceDto OpenSemanticMergeSource(
        SemanticExploreScopeDto scope,
        SemanticProjectRevisionDto expectedRevision,
        string externalRootPath)
    {
        if (scope?.Paths is null || expectedRevision is null || externalRootPath is null)
        {
            throw new SemanticExploreValidationException(
                "The semantic merge source request is malformed.",
                SemanticExploreFailureKind.InvalidData);
        }

        var index = BuildAndValidate(scope, expectedRevision);
        var registration = BuildSemanticMergeExternalRegistration(
            index,
            scope.Paths,
            externalRootPath);
        var layer = registration.Layer;
        var cacheKey = new DerivedIndexCacheKey(
            ToCoreRevision(index.Revision),
            $"semantic-merge-external-v1.{layer.Snapshot.Layer.InstanceId}");
        var estimatedSize = checked(
            EstimateLayerSize(layer) + EstimateExternalRootSize(registration.Root));
        if (estimatedSize > SemanticIndexSizingLimits.MaximumIndexSizeBytes)
        {
            throw new SemanticExploreValidationException(
                "The semantic merge source snapshot exceeds its bounded cache budget.",
                SemanticExploreFailureKind.LimitExceeded);
        }

        if (!semanticMergeExternalCache.Set(cacheKey, registration, estimatedSize))
        {
            throw new SemanticExploreValidationException(
                "The semantic merge source snapshot exceeds its bounded cache budget.",
                SemanticExploreFailureKind.LimitExceeded);
        }
        return new SemanticMergeSourceDto(
            layer.Snapshot.Layer.InstanceId
                ?? throw new SemanticExploreValidationException(
                    "The semantic merge source identity is unavailable.",
                    SemanticExploreFailureKind.ExternalSnapshotUnavailable),
            layer.Snapshot,
            Coverage(layer, SemanticFeatureDto.ExternalCompare));
    }

    internal SemanticMergeIndexedLayers ReadSemanticMergeLayers(
        SemanticExploreScopeDto scope,
        SemanticProjectRevisionDto expectedRevision,
        string sourceAInstanceId,
        string sourceBInstanceId)
    {
        if (scope?.Paths is null
            || expectedRevision is null
            || sourceAInstanceId is null
            || sourceBInstanceId is null)
        {
            throw new SemanticExploreValidationException(
                "The semantic merge snapshot request is malformed.",
                SemanticExploreFailureKind.InvalidData);
        }

        var index = BuildAndValidate(scope, expectedRevision);
        var sourceARegistration = GetSemanticMergeExternalRegistration(
            index,
            sourceAInstanceId);
        var sourceBRegistration = GetSemanticMergeExternalRegistration(
            index,
            sourceBInstanceId);
        if (string.Equals(
                sourceARegistration.Root.Identity,
                sourceBRegistration.Root.Identity,
                PhysicalPathComparison))
        {
            throw new SemanticExploreValidationException(
                "Semantic merge requires two physically distinct source roots.",
                SemanticExploreFailureKind.ExternalRejected);
        }

        var sourceA = ReobserveExternalLayer(index, scope.Paths, sourceAInstanceId);
        var sourceB = ReobserveExternalLayer(index, scope.Paths, sourceBInstanceId);
        EnsureExternalRootIdentity(sourceARegistration.Root);
        EnsureExternalRootIdentity(sourceBRegistration.Root);
        var completedSourceAFingerprint = CaptureExternalSourceFingerprint(
            scope.Paths with { OutputRootPath = sourceARegistration.Root.Path });
        var completedSourceBFingerprint = CaptureExternalSourceFingerprint(
            scope.Paths with { OutputRootPath = sourceBRegistration.Root.Path });
        EnsureExternalRootIdentity(sourceARegistration.Root);
        var finalSourceAFingerprint = CaptureExternalSourceFingerprint(
            scope.Paths with { OutputRootPath = sourceARegistration.Root.Path });
        if (!string.Equals(
                completedSourceAFingerprint,
                sourceARegistration.SourceFingerprint,
                StringComparison.Ordinal)
            || !string.Equals(
                finalSourceAFingerprint,
                sourceARegistration.SourceFingerprint,
                StringComparison.Ordinal)
            || !string.Equals(
                completedSourceBFingerprint,
                sourceBRegistration.SourceFingerprint,
                StringComparison.Ordinal))
        {
            throw new SemanticExploreValidationException(
                "A selected semantic merge source changed while the pair was observed. Select it again.",
                SemanticExploreFailureKind.ExternalSnapshotUnavailable);
        }

        var completedIndex = BuildAndValidate(scope, expectedRevision);
        if (!EqualsRevision(index.Revision, completedIndex.Revision)
            || !EqualsSnapshot(index.Base.Snapshot, completedIndex.Base.Snapshot)
            || !EqualsSnapshot(index.Layered.Snapshot, completedIndex.Layered.Snapshot)
            || !EqualsSnapshot(index.Pending.Snapshot, completedIndex.Pending.Snapshot))
        {
            throw new SemanticExploreValidationException(
                "The semantic project sources changed while the merge pair was observed. Retry the query.",
                SemanticExploreFailureKind.StaleRevision);
        }

        return new SemanticMergeIndexedLayers(
            completedIndex.Revision,
            completedIndex.Base.Data,
            completedIndex.Base.Snapshot,
            completedIndex.Layered.Data,
            completedIndex.Layered.Snapshot,
            completedIndex.Pending.Data,
            completedIndex.Pending.Snapshot,
            sourceA.Data,
            sourceA.Snapshot,
            sourceB.Data,
            sourceB.Snapshot);
    }

    internal void ReleaseSemanticMergeSource(
        SemanticProjectRevisionDto revision,
        string instanceId)
    {
        if (revision is null || !IsComparedModInstanceId(instanceId))
        {
            return;
        }

        semanticMergeExternalCache.Remove(new DerivedIndexCacheKey(
            ToCoreRevision(revision),
            $"semantic-merge-external-v1.{instanceId}"));
    }

    internal SemanticRecipeIndexedLayers ReadSemanticRecipeLayers(
        SemanticExploreScopeDto scope,
        SemanticProjectRevisionDto expectedRevision)
    {
        if (scope?.Paths is null || expectedRevision is null)
        {
            throw new SemanticExploreValidationException(
                "The recipe semantic snapshot request is malformed.",
                SemanticExploreFailureKind.InvalidData);
        }

        var index = BuildAndValidate(scope, expectedRevision);
        return new SemanticRecipeIndexedLayers(
            index.Revision,
            index.Base.Data,
            index.Base.Snapshot,
            index.Layered.Data,
            index.Layered.Snapshot,
            index.Pending.Data,
            index.Pending.Snapshot);
    }

    public QuerySemanticChangesResponse QueryChanges(QuerySemanticChangesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Format))
        {
            throw new SemanticExploreValidationException(
                "The semantic change format is invalid.",
                SemanticExploreFailureKind.InvalidData);
        }

        var index = BuildAndValidate(request.Scope, request.ExpectedRevision);
        ValidatePage(request.Limit, request.Cursor);
        if (request.From is not (SemanticSourceLayerKindDto.Base or SemanticSourceLayerKindDto.Layered)
            || request.To is not (SemanticSourceLayerKindDto.Layered or SemanticSourceLayerKindDto.Pending))
        {
            throw new SemanticExploreValidationException(
                "Semantic change views support Base or Layered sources and Layered or Pending targets.",
                SemanticExploreFailureKind.Unsupported);
        }

        var from = GetCurrentLayer(index, request.From);
        var to = GetCurrentLayer(index, request.To);
        var queryFingerprint = QueryFingerprint(
            "changes",
            index.Revision.Fingerprint,
            request.From.ToString(),
            request.To.ToString(),
            request.Format.ToString());
        var offset = DecodeCursor(request.Cursor, queryFingerprint);
        var queryBudget = new SemanticMaterializationBudget();
        var changes = new List<SemanticChangeDto>();
        foreach (var difference in BuildDifferences(
                     from.Data,
                     to.Data,
                     record: null,
                     queryBudget))
        {
            var path = CanonicalChangePath(difference.Record, difference.FieldKey);
            var before = CanonicalValue(difference.Left);
            var after = CanonicalValue(difference.Right);
            var marker = difference.Kind switch
            {
                SemanticDifferenceKindDto.Added => "+",
                SemanticDifferenceKindDto.Removed => "-",
                _ => "~",
            };
            var line = $"{marker} {path}: {before} -> {after}";
            var change = new SemanticChangeDto(
                path,
                difference.Record,
                difference.FieldKey,
                difference.Kind,
                difference.Left,
                difference.Right,
                line);
            AdmitQueryRow(
                queryBudget,
                changes.Count,
                SemanticIndexSizingLimits.MaximumReferenceCount,
                SemanticExploreSizeEstimator.EstimateChange(change));
            changes.Add(change);
        }

        var page = Page(changes, offset, request.Limit, queryFingerprint);
        return new QuerySemanticChangesResponse(
            index.Revision,
            queryFingerprint,
            page.Items,
            Coverage(to, SemanticFeatureDto.Changes),
            page.NextCursor);
    }

    private SemanticProjectIndex BuildAndValidate(
        SemanticExploreScopeDto scope,
        SemanticProjectRevisionDto expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(expectedRevision);
        if (!Enum.IsDefined(expectedRevision.GameFamily)
            || !long.TryParse(
                expectedRevision.Generation,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var generation)
            || generation < 0
            || !IsSha256Fingerprint(expectedRevision.Fingerprint))
        {
            throw new SemanticExploreValidationException(
                "The expected semantic revision is invalid.",
                SemanticExploreFailureKind.InvalidData);
        }

        var index = BuildCurrentIndex(scope);
        if (!EqualsRevision(index.Revision, expectedRevision))
        {
            throw new SemanticExploreValidationException(
                "The semantic source revision changed. Refresh the semantic workspace and retry.",
                SemanticExploreFailureKind.StaleRevision);
        }

        return index;
    }

    private SemanticProjectIndex BuildCurrentIndex(SemanticExploreScopeDto scope)
    {
        ValidateScope(scope);
        var gameFamily = ToFamily(scope.Paths.SelectedGame!.Value);
        var observation = CaptureSourceObservation(scope, gameFamily);
        var buildEpoch = CaptureCacheEpoch();
        var revisionFingerprint = Hash(
            "semantic-project-revision-v2",
            scope.ProjectId,
            gameFamily.ToString(),
            observation.Fingerprint,
            FingerprintPendingSession(scope.PendingSession));
        var generation = Convert
            .ToUInt64(revisionFingerprint[..15], 16)
            .ToString(CultureInfo.InvariantCulture);
        var revision = new SemanticProjectRevisionDto(
            scope.ProjectId,
            gameFamily,
            generation,
            revisionFingerprint);

        var sourceRevisionFingerprint = Hash(
            "semantic-project-source-revision-v1",
            scope.ProjectId,
            gameFamily.ToString(),
            observation.Fingerprint);
        var sourceGeneration = Convert
            .ToUInt64(sourceRevisionFingerprint[..15], 16)
            .ToString(CultureInfo.InvariantCulture);
        var sourceRevision = new SemanticProjectRevisionDto(
            scope.ProjectId,
            gameFamily,
            sourceGeneration,
            sourceRevisionFingerprint);
        var sourceKey = new DerivedIndexCacheKey(
            ToCoreRevision(sourceRevision),
            SourceCacheCallerKey);

        SemanticSourceIndex sourceIndex;
        bool sourceCacheable;
        SemanticMaterializationBudget materializationBudget;
        if (sourceCache.TryGet(sourceKey, out var cachedSource))
        {
            sourceIndex = cachedSource;
            sourceCacheable = true;
            materializationBudget = new SemanticMaterializationBudget(
                EstimateSourceSize(sourceIndex));
        }
        else
        {
            lock (SourceMaterializationLock(sourceRevisionFingerprint))
            {
                if (sourceCache.TryGet(sourceKey, out cachedSource))
                {
                    sourceIndex = cachedSource;
                    sourceCacheable = true;
                    materializationBudget = new SemanticMaterializationBudget(
                        EstimateSourceSize(sourceIndex));
                }
                else
                {
                    var provider = GetProvider(gameFamily);
                    materializationBudget = new SemanticMaterializationBudget(
                        SemanticExploreSizeEstimator.ProjectEnvelopeSizeBytes);
                    var basePaths = scope.Paths with { OutputRootPath = null };
                    var baseData = BuildSourceLayer(
                        provider,
                        basePaths,
                        materializationBudget);
                    var layeredData = Equals(basePaths, scope.Paths)
                        ? baseData
                        : BuildSourceLayer(
                            provider,
                            scope.Paths,
                            materializationBudget);
                    ValidateLayerBounds(baseData);
                    ValidateLayerBounds(layeredData);

                    sourceIndex = new SemanticSourceIndex(
                        baseData,
                        FingerprintLayer(baseData, pendingSession: null),
                        layeredData,
                        FingerprintLayer(layeredData, pendingSession: null));

                    var completedObservation = CaptureSourceObservation(
                        scope,
                        gameFamily,
                        forceRefresh: true);
                    if (!string.Equals(
                            observation.Fingerprint,
                            completedObservation.Fingerprint,
                            StringComparison.Ordinal))
                    {
                        InvalidateSourceObservation(scope.SourceObservationToken);
                        throw new SemanticExploreValidationException(
                            "The semantic project sources changed while the index was being built. Retry the query.",
                            SemanticExploreFailureKind.StaleRevision);
                    }

                    var sourceSize = EstimateSourceSize(sourceIndex);
                    if (sourceSize > SemanticIndexSizingLimits.MaximumIndexSizeBytes)
                    {
                        throw new SemanticExploreValidationException(
                            "The semantic index exceeds its bounded cache budget.",
                            SemanticExploreFailureKind.LimitExceeded);
                    }

                    sourceCacheable = new[] { baseData, layeredData }
                        .All(layer => layer.DomainStatuses.All(status => status.Available));
                    if (sourceCacheable
                        && !PublishSourceIndex(
                            buildEpoch,
                            sourceKey,
                            sourceIndex,
                            sourceSize))
                    {
                        throw new SemanticExploreValidationException(
                            "The semantic index exceeds its bounded cache budget.",
                            SemanticExploreFailureKind.LimitExceeded);
                    }
                }
            }
        }

        SemanticLayerData pendingData;
        string pendingFingerprint;
        if (scope.PendingSession is not { PendingEdits.Count: > 0 })
        {
            pendingData = ApplyPendingOverlay(
                sourceIndex.LayeredData,
                scope.PendingSession,
                gameFamily,
                materializationBudget);
            pendingFingerprint = FingerprintLayer(pendingData, scope.PendingSession);
        }
        else
        {
            var pendingKey = new DerivedIndexCacheKey(
                ToCoreRevision(revision),
                PendingCacheCallerKey);
            if (pendingOverlayCache.TryGet(pendingKey, out var cachedPending))
            {
                pendingData = cachedPending.Data;
                pendingFingerprint = cachedPending.Fingerprint;
            }
            else
            {
                lock (SourceMaterializationLock(revisionFingerprint))
                {
                    if (pendingOverlayCache.TryGet(pendingKey, out cachedPending))
                    {
                        pendingData = cachedPending.Data;
                        pendingFingerprint = cachedPending.Fingerprint;
                    }
                    else
                    {
                        pendingData = ApplyPendingOverlay(
                            sourceIndex.LayeredData,
                            scope.PendingSession,
                            gameFamily,
                            materializationBudget);
                        ValidateLayerBounds(pendingData);
                        pendingFingerprint = FingerprintLayer(pendingData, scope.PendingSession);
                        var pendingOverlay = new SemanticPendingOverlay(
                            pendingData,
                            pendingFingerprint);
                        var pendingSize = EstimatePendingOverlaySize(pendingOverlay);
                        if (sourceCacheable
                            && !PublishPendingOverlay(
                                buildEpoch,
                                pendingKey,
                                pendingOverlay,
                                pendingSize))
                        {
                            throw new SemanticExploreValidationException(
                                "The semantic index exceeds its bounded cache budget.",
                                SemanticExploreFailureKind.LimitExceeded);
                        }
                    }
                }
            }
        }

        ValidateLayerBounds(pendingData);
        var baseLayer = Layer(
            sourceIndex.BaseData,
            SemanticSourceLayerKindDto.Base,
            revision,
            sourceIndex.BaseFingerprint);
        var layeredLayer = Layer(
            sourceIndex.LayeredData,
            SemanticSourceLayerKindDto.Layered,
            revision,
            sourceIndex.LayeredFingerprint);
        var pendingLayer = Layer(
            pendingData,
            SemanticSourceLayerKindDto.Pending,
            revision,
            pendingFingerprint);
        var built = new SemanticProjectIndex(revision, baseLayer, layeredLayer, pendingLayer);

        var estimatedSize = EstimateSize(built);
        if (estimatedSize > SemanticIndexSizingLimits.MaximumIndexSizeBytes)
        {
            throw new SemanticExploreValidationException(
                "The semantic index exceeds its bounded cache budget.",
                SemanticExploreFailureKind.LimitExceeded);
        }

        EnsureCacheEpoch(buildEpoch);

        return built;
    }

    private SemanticWorkflowCorpus LoadCorpus(ProjectPathsDto paths)
    {
        return WrapCorpus(new SemanticWorkflowDtoLoaders(
            () => loadItemsFresh(paths),
            () => loadPokemonFresh(paths),
            () => loadMovesFresh(paths)));
    }

    private static SemanticWorkflowCorpus WrapCorpus(SemanticWorkflowDtoLoaders loaders)
    {
        ArgumentNullException.ThrowIfNull(loaders);
        return new SemanticWorkflowCorpus(
            () => TryLoad(
                loaders.Items,
                workflow => workflow.Summary,
                workflow => workflow.Items.Count,
                workflow => workflow.Diagnostics),
            () => TryLoad(
                loaders.Pokemon,
                workflow => workflow.Summary,
                workflow => workflow.Pokemon.Count,
                workflow => workflow.Diagnostics),
            () => TryLoad(
                loaders.Moves,
                workflow => workflow.Summary,
                workflow => workflow.Moves.Count,
                workflow => workflow.Diagnostics));
    }

    private SemanticLayerData BuildSourceLayer(
        ISemanticExploreFamilyProvider provider,
        ProjectPathsDto paths,
        SemanticMaterializationBudget materializationBudget)
    {
        var parallelism = canLoadCorpusConcurrently(paths)
            ? BoundedParallel.Plan(
                MaximumConcurrentCorpusLoads,
                CorpusLoadPolicy).DegreeOfParallelism
            : 1;
        var corpus = prepareCorpusFresh is null
            ? LoadCorpus(paths)
            : PrepareCorpus(paths, parallelism);
        return provider.Build(
            parallelism > 1 && prepareCorpusFresh is null
                ? MaterializeCorpusConcurrently(corpus, parallelism)
                : corpus,
            materializationBudget);
    }

    private SemanticWorkflowCorpus PrepareCorpus(ProjectPathsDto paths, int parallelism)
    {
        try
        {
            return WrapCorpus(prepareCorpusFresh!(paths, parallelism));
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            var failure = ExceptionDispatchInfo.Capture(exception);
            return WrapCorpus(new SemanticWorkflowDtoLoaders(
                () => RethrowPreparedCorpusFailure<ItemsWorkflowDto>(failure),
                () => RethrowPreparedCorpusFailure<PokemonWorkflowDto>(failure),
                () => RethrowPreparedCorpusFailure<MovesWorkflowDto>(failure)));
        }
    }

    private static T RethrowPreparedCorpusFailure<T>(ExceptionDispatchInfo failure)
        where T : class
    {
        failure.Throw();
        throw new InvalidOperationException("The semantic corpus preparation failure was not rethrown.");
    }

    private static SemanticWorkflowCorpus MaterializeCorpusConcurrently(
        SemanticWorkflowCorpus corpus,
        int parallelism)
    {
        SemanticWorkflowLoad<ItemsWorkflowDto>? items = null;
        SemanticWorkflowLoad<PokemonWorkflowDto>? pokemon = null;
        SemanticWorkflowLoad<MovesWorkflowDto>? moves = null;
        var failures = new ExceptionDispatchInfo?[MaximumConcurrentCorpusLoads];

        var executionPolicy = parallelism > 1
            ? CorpusLoadPolicy
            : new BoundedConcurrencyPolicy(
                "semantic-explore-corpus-load-serial",
                BoundedWorkloadKind.Decode,
                EstimatedCorpusLoadWorkerBytes,
                maximumDegreeOfParallelism: 1);
        _ = BoundedParallel.For(
            MaximumConcurrentCorpusLoads,
            executionPolicy,
            index =>
            {
                try
                {
                    switch (index)
                    {
                        case 0:
                            items = corpus.LoadItems();
                            break;
                        case 1:
                            pokemon = corpus.LoadPokemon();
                            break;
                        case 2:
                            moves = corpus.LoadMoves();
                            break;
                    }
                }
                catch (Exception exception)
                {
                    failures[index] = ExceptionDispatchInfo.Capture(exception);
                }
            });

        foreach (var failure in failures)
        {
            failure?.Throw();
        }

        return new SemanticWorkflowCorpus(
            () => items ?? throw new InvalidOperationException(
                "The semantic items corpus was not materialized."),
            () => pokemon ?? throw new InvalidOperationException(
                "The semantic Pokemon corpus was not materialized."),
            () => moves ?? throw new InvalidOperationException(
                "The semantic moves corpus was not materialized."));
    }

    private SemanticSourceObservation CaptureSourceObservation(
        SemanticExploreScopeDto scope,
        SemanticGameFamilyDto family,
        bool forceRefresh = false)
    {
        string sourceFingerprint;
        if (!forceRefresh
            && TryResolveSourceObservation(scope, out sourceFingerprint))
        {
            return CreateSourceObservation(scope, family, sourceFingerprint);
        }

        try
        {
            sourceFingerprint = captureExactSourceFingerprint(scope.Paths);
        }
        catch (ProjectFileGraphDiscoveryException exception)
        {
            throw new SemanticExploreValidationException(
                "The semantic source graph exceeds its bounded observation limits.",
                SemanticExploreFailureKind.LimitExceeded,
                exception);
        }
        catch (Exception exception) when (ContainsInvalidDataException(exception))
        {
            throw new SemanticExploreValidationException(
                "The semantic source observation could not be completed within its safe bounds.",
                SemanticExploreFailureKind.LimitExceeded,
                exception);
        }
        catch (SemanticExploreValidationException)
        {
            throw;
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            throw new SemanticExploreValidationException(
                "The semantic source payloads could not be observed safely.",
                SemanticExploreFailureKind.InvalidData,
                exception);
        }

        if (!IsSha256Fingerprint(sourceFingerprint))
        {
            throw new SemanticExploreValidationException(
                "The semantic provider returned an invalid source observation.",
                SemanticExploreFailureKind.InvalidData);
        }

        sourceFingerprint = sourceFingerprint.ToLowerInvariant();
        if (!forceRefresh)
        {
            RegisterRequestedSourceObservation(scope, sourceFingerprint);
        }

        return CreateSourceObservation(scope, family, sourceFingerprint);
    }

    private static SemanticSourceObservation CreateSourceObservation(
        SemanticExploreScopeDto scope,
        SemanticGameFamilyDto family,
        string sourceFingerprint)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHash(hash, "semantic-source-observation-v3");
        AppendHash(hash, SourceObservationScopeIdentity(scope));
        AppendHash(hash, scope.ProjectId);
        AppendHash(hash, family.ToString());
        AppendHash(hash, scope.Paths.SelectedGame!.Value.ToString());
        AppendHash(hash, scope.Paths.GameTextLanguage ?? string.Empty);
        AppendHash(hash, sourceFingerprint.ToLowerInvariant());

        return new SemanticSourceObservation(Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    private object SourceMaterializationLock(string sourceRevisionFingerprint)
    {
        var stripe = (int)(Convert.ToUInt32(sourceRevisionFingerprint[..8], 16)
            % SourceMaterializationLockCount);
        return sourceMaterializationLocks[stripe];
    }

    private long CaptureCacheEpoch()
    {
        lock (cachePublicationSync)
        {
            return cacheInvalidationEpoch;
        }
    }

    private void ClearDerivedCaches()
    {
        lock (cachePublicationSync)
        {
            cacheInvalidationEpoch = checked(cacheInvalidationEpoch + 1);
            sourceCache.Clear();
            pendingOverlayCache.Clear();
            externalCache.Clear();
            semanticMergeExternalCache.Clear();
        }
    }

    private void EnsureCacheEpoch(long expectedEpoch)
    {
        lock (cachePublicationSync)
        {
            EnsureCacheEpochCore(expectedEpoch);
        }
    }

    private bool PublishSourceIndex(
        long expectedEpoch,
        DerivedIndexCacheKey key,
        SemanticSourceIndex index,
        long size)
    {
        lock (cachePublicationSync)
        {
            EnsureCacheEpochCore(expectedEpoch);
            return sourceCache.Set(key, index, size);
        }
    }

    private bool PublishPendingOverlay(
        long expectedEpoch,
        DerivedIndexCacheKey key,
        SemanticPendingOverlay overlay,
        long size)
    {
        lock (cachePublicationSync)
        {
            EnsureCacheEpochCore(expectedEpoch);
            return pendingOverlayCache.Set(key, overlay, size);
        }
    }

    private void EnsureCacheEpochCore(long expectedEpoch)
    {
        if (expectedEpoch != cacheInvalidationEpoch)
        {
            throw new SemanticExploreValidationException(
                "The semantic project cache changed while the index was being built. Retry the query.",
                SemanticExploreFailureKind.StaleRevision);
        }
    }

    private bool TryResolveSourceObservation(
        SemanticExploreScopeDto scope,
        out string sourceFingerprint)
    {
        var token = scope.SourceObservationToken;
        if (token is null)
        {
            sourceFingerprint = string.Empty;
            return false;
        }

        var scopeIdentity = SourceObservationScopeIdentity(scope);
        lock (sourceObservationSync)
        {
            if (!sourceObservations.TryGetValue(token, out var observation))
            {
                sourceFingerprint = string.Empty;
                return false;
            }

            if (!string.Equals(
                    observation.ScopeIdentity,
                    scopeIdentity,
                    StringComparison.Ordinal))
            {
                throw new SemanticExploreValidationException(
                    "The semantic source observation does not belong to this project scope.",
                    SemanticExploreFailureKind.InvalidData);
            }

            TouchSourceObservation(observation);
            sourceFingerprint = observation.SourceFingerprint;
            return true;
        }
    }

    private void RegisterRequestedSourceObservation(
        SemanticExploreScopeDto scope,
        string sourceFingerprint)
    {
        var token = scope.SourceObservationToken;
        if (token is null)
        {
            return;
        }

        var scopeIdentity = SourceObservationScopeIdentity(scope);
        bool sourceChanged;
        lock (sourceObservationSync)
        {
            if (sourceObservations.TryGetValue(token, out var existing))
            {
                if (!string.Equals(existing.ScopeIdentity, scopeIdentity, StringComparison.Ordinal)
                    || !string.Equals(
                        existing.SourceFingerprint,
                        sourceFingerprint,
                        StringComparison.Ordinal))
                {
                    throw new SemanticExploreValidationException(
                        "The semantic source observation is no longer valid for this project scope.",
                        SemanticExploreFailureKind.StaleRevision);
                }

                TouchSourceObservation(existing);
                return;
            }

            sourceChanged = sourceObservations.Values.Any(observation =>
                string.Equals(
                    observation.ScopeIdentity,
                    scopeIdentity,
                    StringComparison.Ordinal)
                && !string.Equals(
                    observation.SourceFingerprint,
                    sourceFingerprint,
                    StringComparison.Ordinal));
            RemoveSourceObservationsForScope(
                scopeIdentity,
                exceptToken: null,
                exceptFingerprint: sourceFingerprint);
            AddSourceObservation(token, scopeIdentity, sourceFingerprint);
        }

        if (sourceChanged)
        {
            ClearDerivedCaches();
        }
    }

    private static string SourceObservationScopeIdentity(SemanticExploreScopeDto scope)
    {
        return Hash(
            "semantic-source-observation-scope-v1",
            scope.ProjectId,
            scope.Paths.BaseRomFsPath ?? string.Empty,
            scope.Paths.BaseExeFsPath ?? string.Empty,
            scope.Paths.OutputRootPath ?? string.Empty,
            scope.Paths.SaveFilePath ?? string.Empty,
            scope.Paths.ScarletVioletSupportFolderPath ?? string.Empty,
            scope.Paths.PokemonLegendsZASupportFolderPath ?? string.Empty,
            scope.Paths.SelectedGame?.ToString() ?? string.Empty,
            scope.Paths.GameTextLanguage ?? string.Empty);
    }

    private void AddSourceObservation(
        string token,
        string scopeIdentity,
        string sourceFingerprint)
    {
        if (sourceObservations.Count >= MaximumVerifiedSourceObservations)
        {
            var oldest = sourceObservations.Values.MinBy(observation =>
                observation.AccessSequence);
            if (oldest is not null)
            {
                sourceObservations.Remove(oldest.Token);
            }
        }

        sourceObservations[token] = new VerifiedSourceObservation(
            token,
            scopeIdentity,
            sourceFingerprint,
            checked(++sourceObservationAccessSequence));
    }

    private void TouchSourceObservation(VerifiedSourceObservation observation)
    {
        sourceObservations[observation.Token] = observation with
        {
            AccessSequence = checked(++sourceObservationAccessSequence),
        };
    }

    private void RemoveSourceObservationsForScope(
        string scopeIdentity,
        string? exceptToken,
        string? exceptFingerprint = null)
    {
        var tokens = sourceObservations.Values
            .Where(observation =>
                string.Equals(observation.ScopeIdentity, scopeIdentity, StringComparison.Ordinal)
                && !string.Equals(observation.Token, exceptToken, StringComparison.Ordinal)
                && (exceptFingerprint is null
                    || !string.Equals(
                        observation.SourceFingerprint,
                        exceptFingerprint,
                        StringComparison.Ordinal)))
            .Select(observation => observation.Token)
            .ToArray();
        foreach (var token in tokens)
        {
            sourceObservations.Remove(token);
        }
    }

    private void InvalidateSourceObservation(string? token)
    {
        if (token is null)
        {
            return;
        }

        lock (sourceObservationSync)
        {
            sourceObservations.Remove(token);
        }
    }

    private static bool IsSourceObservationToken(string? token)
    {
        return token is { Length: SourceObservationTokenLength }
            && token.StartsWith(SourceObservationTokenPrefix, StringComparison.Ordinal)
            && token.AsSpan(SourceObservationTokenPrefix.Length).ToArray().All(character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    private static bool IsSha256Fingerprint(string? fingerprint)
    {
        return fingerprint is { Length: 64 }
            && fingerprint.All(character => character is
                >= '0' and <= '9'
                or >= 'a' and <= 'f'
                or >= 'A' and <= 'F');
    }

    private static string FingerprintPendingSession(EditSessionDto? session)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHash(hash, "semantic-pending-session-v1");
        if (session is null)
        {
            AppendHash(hash, "none");
            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }

        AppendHash(hash, session.SessionId);
        AppendHash(hash, session.HasPendingChanges ? "pending" : "clean");
        if (session.PendingEdits.Count > ChangeSetContract.MaximumOperationCount)
        {
            throw new SemanticExploreValidationException(
                "The pending semantic session exceeds its bounded edit limit.",
                SemanticExploreFailureKind.LimitExceeded);
        }

        foreach (var edit in session.PendingEdits)
        {
            AppendHash(hash, edit.Domain);
            AppendHash(hash, edit.RecordId ?? string.Empty);
            AppendHash(hash, edit.Field ?? string.Empty);
            AppendHash(hash, edit.NewValue ?? string.Empty);
            AppendHash(hash, edit.Owner ?? string.Empty);
            AppendHash(hash, edit.Association?.ChangeSetId ?? string.Empty);
            AppendHash(hash, edit.Association?.OperationId ?? string.Empty);
            if (edit.Sources.Count > MaximumPendingSourcesPerEdit)
            {
                throw new SemanticExploreValidationException(
                    "A pending semantic edit exceeds its bounded source limit.",
                    SemanticExploreFailureKind.LimitExceeded);
            }

            foreach (var source in edit.Sources
                         .OrderBy(source => source.Layer)
                         .ThenBy(source => source.RelativePath, StringComparer.Ordinal))
            {
                AppendHash(hash, source.Layer.ToString());
                AppendHash(hash, source.RelativePath);
            }
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static SemanticWorkflowLoad<T> TryLoad<T>(
        Func<T> loader,
        Func<T, WorkflowSummaryDto> summary,
        Func<T, int> semanticRecordCount,
        Func<T, IReadOnlyList<ApiDiagnostic>> diagnostics)
        where T : class
    {
        try
        {
            var value = loader();
            var workflowSummary = summary(value);
            if (workflowSummary.Availability == WorkflowAvailabilityDto.Disabled)
            {
                return new SemanticWorkflowLoad<T>(null, "workflow-disabled");
            }

            var providerDiagnostics = diagnostics(value);
            if (workflowSummary.Diagnostics.Any(diagnostic =>
                    diagnostic.Severity == ApiDiagnosticSeverity.Error)
                || providerDiagnostics.Any(diagnostic =>
                    diagnostic.Severity == ApiDiagnosticSeverity.Error))
            {
                return new SemanticWorkflowLoad<T>(null, "provider-diagnostics-error");
            }

            if (semanticRecordCount(value) <= 0)
            {
                return new SemanticWorkflowLoad<T>(null, "provider-records-unavailable");
            }

            var partial = workflowSummary.Diagnostics.Any(diagnostic =>
                    diagnostic.Severity == ApiDiagnosticSeverity.Warning
                    && !IsSemanticIrrelevantProjectWarning(diagnostic))
                || providerDiagnostics.Any(diagnostic =>
                    diagnostic.Severity == ApiDiagnosticSeverity.Warning
                    && !IsSemanticIrrelevantProjectWarning(diagnostic));
            return new SemanticWorkflowLoad<T>(
                value,
                partial ? "provider-diagnostics-warning" : null,
                partial);
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            var reasonCode = exception switch
            {
                UnauthorizedAccessException => "provider-access-denied",
                IOException => "provider-io-unavailable",
                InvalidDataException => "provider-data-invalid",
                _ => "provider-unavailable",
            };
            return new SemanticWorkflowLoad<T>(null, reasonCode);
        }
    }

    private static bool IsSemanticIrrelevantProjectWarning(ApiDiagnostic diagnostic)
    {
        if (!string.Equals(diagnostic.Domain, "project", StringComparison.Ordinal))
        {
            return false;
        }

        return string.Equals(diagnostic.Field, "outputRootPath", StringComparison.Ordinal)
                && diagnostic.Code is ProjectValidator.OutputRootNotConfiguredDiagnosticCode
                    or ProjectValidator.OutputRootMissingDiagnosticCode
            || string.Equals(diagnostic.Field, "saveFilePath", StringComparison.Ordinal)
                && diagnostic.Code is ProjectValidator.SaveFileWrongKindDiagnosticCode
                    or ProjectValidator.SaveFileMissingDiagnosticCode;
    }

    private static SemanticLayerData ApplyPendingOverlay(
        SemanticLayerData layered,
        EditSessionDto? session,
        SemanticGameFamilyDto family,
        SemanticMaterializationBudget materializationBudget)
    {
        ArgumentNullException.ThrowIfNull(materializationBudget);
        materializationBudget.Admit(
            SemanticExploreSizeEstimator.MaximumLayerEnvelopeSizeBytes,
            "The semantic index exceeds its bounded cache budget.");
        if (session is null || session.PendingEdits.Count == 0)
        {
            return layered;
        }

        materializationBudget.Admit(
            SemanticExploreSizeEstimator.EstimateLayerData(layered),
            "The semantic index exceeds its bounded cache budget.");

        var entities = new SortedDictionary<string, SemanticIndexedEntity>(StringComparer.Ordinal);
        foreach (var pair in layered.Entities)
        {
            entities.Add(
                pair.Key,
                pair.Value with
                {
                    Fields = pair.Value.Fields.ToDictionary(
                        field => field.Key,
                        field => field.Value,
                        StringComparer.Ordinal),
                });
        }
        var partiallyAppliedDomains = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edit in session.PendingEdits)
        {
            if (edit.Domain is not ("workflow.items" or "workflow.pokemon" or "workflow.moves"))
            {
                continue;
            }

            if (edit.Sources.Count > MaximumPendingSourcesPerEdit)
            {
                throw new SemanticExploreValidationException(
                    "A pending semantic edit exceeds its bounded source limit.",
                    SemanticExploreFailureKind.LimitExceeded);
            }

            foreach (var source in edit.Sources)
            {
                _ = NormalizePendingSourceFile(source.RelativePath);
            }

            if (edit.Owner is not null
                || edit.Sources.Count == 0
                || edit.Sources.Any(source =>
                    source.Layer is not (FileLayerDto.Base or FileLayerDto.Layered))
                || !int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                || id < 0
                || string.IsNullOrWhiteSpace(edit.Field)
                || edit.NewValue is null)
            {
                partiallyAppliedDomains.Add(edit.Domain);
                continue;
            }

            var kind = edit.Domain switch
            {
                "workflow.items" => "item",
                "workflow.pokemon" => "pokemon-personal",
                "workflow.moves" => "move",
                _ => throw new InvalidOperationException(),
            };
            var record = new SemanticRecordRefDto(
                family,
                edit.Domain,
                new SemanticRecordKindDto(kind, 1),
                id.ToString(CultureInfo.InvariantCulture),
                SubrecordId: null);
            var recordKey = RecordKey(record);
            if (!entities.TryGetValue(recordKey, out var entity)
                || !entity.Fields.TryGetValue(edit.Field, out var field))
            {
                partiallyAppliedDomains.Add(edit.Domain);
                continue;
            }

            var expectedLayer = ToPendingLayer(entity.SourceLayer);
            if (!edit.Sources.Any(source =>
                    source.Layer == expectedLayer
                    && string.Equals(
                        NormalizePendingSourceFile(source.RelativePath),
                        entity.SourceFile,
                        StringComparison.Ordinal)))
            {
                partiallyAppliedDomains.Add(edit.Domain);
                continue;
            }

            var fields = (Dictionary<string, SemanticIndexedField>)entity.Fields;
            var updatedField = field with
            {
                Value = ParsePendingValue(field.Value.Kind, edit.NewValue),
            };
            var previousEstimatedSize = SemanticExploreSizeEstimator.EstimateField(field);
            var updatedEstimatedSize = SemanticExploreSizeEstimator.EstimateField(updatedField);
            if (updatedEstimatedSize > previousEstimatedSize)
            {
                materializationBudget.Admit(
                    updatedEstimatedSize - previousEstimatedSize,
                    "The semantic index exceeds its bounded cache budget.");
            }

            fields[edit.Field] = updatedField;
        }

        var statuses = layered.DomainStatuses
            .Select(status => partiallyAppliedDomains.Contains(status.Domain)
                ? status with
                {
                    Partial = true,
                    ReasonCode = "pending-field-coverage",
                }
                : status)
            .ToArray();
        return new SemanticLayerData(entities, layered.References, statuses);
    }

    private static string NormalizePendingSourceFile(string sourceFile)
    {
        if (string.IsNullOrWhiteSpace(sourceFile)
            || sourceFile != sourceFile.Trim()
            || Path.IsPathRooted(sourceFile)
            || sourceFile.Any(IsUnsafeUnicode))
        {
            throw new SemanticExploreValidationException(
                "A pending semantic edit has invalid source provenance.",
                SemanticExploreFailureKind.InvalidData);
        }

        try
        {
            return new RelativeOutputPath(sourceFile).Value;
        }
        catch (ArgumentException exception)
        {
            throw new SemanticExploreValidationException(
                "A pending semantic edit has invalid source provenance.",
                SemanticExploreFailureKind.InvalidData,
                exception);
        }
    }

    private static FileLayerDto ToPendingLayer(ProjectFileLayerDto layer)
    {
        return layer switch
        {
            ProjectFileLayerDto.Base => FileLayerDto.Base,
            ProjectFileLayerDto.Layered => FileLayerDto.Layered,
            _ => throw new SemanticExploreValidationException(
                "The provider returned an unsupported source layer for a pending semantic edit.",
                SemanticExploreFailureKind.Unsupported),
        };
    }

    private static SemanticScalarValueDto ParsePendingValue(
        SemanticValueKindDto kind,
        string value)
    {
        if (value.Length > 1_024 || value.Any(IsUnsafeUnicode))
        {
            throw new SemanticExploreValidationException(
                "A pending semantic value is invalid or too large.",
                SemanticExploreFailureKind.InvalidData);
        }

        return kind switch
        {
            SemanticValueKindDto.Boolean when bool.TryParse(value, out var booleanValue) =>
                new SemanticScalarValueDto(
                    kind,
                    booleanValue ? "true" : "false",
                    booleanValue ? "true" : "false"),
            SemanticValueKindDto.Boolean when value is "0" or "1" =>
                new SemanticScalarValueDto(
                    kind,
                    value == "1" ? "true" : "false",
                    value == "1" ? "true" : "false"),
            SemanticValueKindDto.SignedInteger
                when long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var signed) =>
                new SemanticScalarValueDto(
                    kind,
                    signed.ToString(CultureInfo.InvariantCulture),
                    signed.ToString(CultureInfo.InvariantCulture)),
            SemanticValueKindDto.UnsignedInteger
                when ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var unsigned) =>
                new SemanticScalarValueDto(
                    kind,
                    unsigned.ToString(CultureInfo.InvariantCulture),
                    unsigned.ToString(CultureInfo.InvariantCulture)),
            SemanticValueKindDto.Decimal
                when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                     && double.IsFinite(number) =>
                new SemanticScalarValueDto(
                    kind,
                    number.ToString("R", CultureInfo.InvariantCulture),
                    number.ToString("R", CultureInfo.InvariantCulture)),
            SemanticValueKindDto.Enum
                when long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var enumValue) =>
                new SemanticScalarValueDto(
                    kind,
                    enumValue.ToString(CultureInfo.InvariantCulture),
                    enumValue.ToString(CultureInfo.InvariantCulture)),
            _ => throw new SemanticExploreValidationException(
                "A pending semantic value is not supported by the exact field provider.",
                SemanticExploreFailureKind.Unsupported),
        };
    }

    private static CompareSemanticResponse CompareLayers(
        SemanticProjectIndex index,
        SemanticIndexedLayer left,
        SemanticIndexedLayer right,
        SemanticRecordRefDto? record,
        int limit,
        string? cursor,
        string operation)
    {
        var recordKey = record is null ? "all" : RecordKey(record);
        var queryFingerprint = QueryFingerprint(
            operation,
            index.Revision.Fingerprint,
            left.Snapshot.Layer.Kind.ToString(),
            left.Snapshot.Fingerprint,
            right.Snapshot.Layer.Kind.ToString(),
            right.Snapshot.Fingerprint,
            right.Snapshot.Layer.InstanceId ?? "none",
            recordKey);
        var offset = DecodeCursor(cursor, queryFingerprint);
        var queryBudget = new SemanticMaterializationBudget();
        var differences = new List<SemanticDifferenceDto>();
        foreach (var difference in BuildDifferences(left.Data, right.Data, record, queryBudget))
        {
            AdmitQueryRow(
                queryBudget,
                differences.Count,
                SemanticIndexSizingLimits.MaximumReferenceCount,
                SemanticExploreSizeEstimator.EstimateDifference(difference));
            differences.Add(difference);
        }

        var page = Page(differences, offset, limit, queryFingerprint);
        return new CompareSemanticResponse(
            index.Revision,
            queryFingerprint,
            left.Snapshot,
            right.Snapshot,
            page.Items,
            MergeCoverage(
                left,
                right,
                operation == "external-compare"
                    ? SemanticFeatureDto.ExternalCompare
                    : SemanticFeatureDto.Compare),
            page.NextCursor);
    }

    private static IEnumerable<SemanticDifferenceDto> BuildDifferences(
        SemanticLayerData left,
        SemanticLayerData right,
        SemanticRecordRefDto? record,
        SemanticMaterializationBudget queryBudget)
    {
        ArgumentNullException.ThrowIfNull(queryBudget);
        var recordFilter = record is null ? null : RecordKey(record);
        var observedKeys = new HashSet<string>(StringComparer.Ordinal);
        var keys = new List<string>();
        AddComparisonKeys(left.Entities.Keys);
        AddComparisonKeys(right.Entities.Keys);
        keys.Sort(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            left.Entities.TryGetValue(key, out var leftEntity);
            right.Entities.TryGetValue(key, out var rightEntity);
            var identity = leftEntity?.Record ?? rightEntity!.Record;
            var ownerId = rightEntity?.OwnerId ?? leftEntity!.OwnerId;
            if (leftEntity is null || rightEntity is null)
            {
                yield return new SemanticDifferenceDto(
                    identity,
                    "record",
                    "Record",
                    leftEntity is null
                        ? SemanticDifferenceKindDto.Added
                        : SemanticDifferenceKindDto.Removed,
                    Left: null,
                    Right: null,
                    ownerId);
                continue;
            }

            var fieldKeys = leftEntity.Fields.Keys
                .Concat(rightEntity.Fields.Keys)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(field => field, StringComparer.Ordinal);
            foreach (var fieldKey in fieldKeys)
            {
                leftEntity.Fields.TryGetValue(fieldKey, out var leftField);
                rightEntity.Fields.TryGetValue(fieldKey, out var rightField);
                if (leftField is not null
                    && rightField is not null
                    && Equals(leftField.Value, rightField.Value))
                {
                    continue;
                }

                yield return new SemanticDifferenceDto(
                    identity,
                    fieldKey,
                    rightField?.Label ?? leftField!.Label,
                    leftField is null
                        ? SemanticDifferenceKindDto.Added
                        : rightField is null
                            ? SemanticDifferenceKindDto.Removed
                            : SemanticDifferenceKindDto.Changed,
                    leftField?.Value,
                    rightField?.Value,
                    rightField?.OwnerId ?? leftField!.OwnerId);
            }
        }

        void AddComparisonKeys(IEnumerable<string> candidates)
        {
            foreach (var key in candidates)
            {
                if ((recordFilter is not null && key != recordFilter)
                    || observedKeys.Contains(key))
                {
                    continue;
                }

                AdmitQueryRow(
                    queryBudget,
                    keys.Count,
                    SemanticIndexSizingLimits.MaximumComparisonEntityKeyCount,
                    SemanticExploreSizeEstimator.EstimateQueryKey(key));
                observedKeys.Add(key);
                keys.Add(key);
            }
        }
    }

    private static IReadOnlyList<SemanticOwnershipRow> BuildOwnershipRows(
        SemanticProjectIndex index,
        EditSessionDto? pendingSession,
        string? recordFilter)
    {
        var queryBudget = new SemanticMaterializationBudget();
        var rows = new List<SemanticOwnershipRow>();
        var entities = new List<SemanticIndexedEntity>();
        foreach (var entity in index.Pending.Data.Entities.Values)
        {
            if (recordFilter is not null && RecordKey(entity.Record) != recordFilter)
            {
                continue;
            }

            AdmitQueryRow(
                queryBudget,
                entities.Count,
                SemanticIndexSizingLimits.MaximumEntityCount,
                SemanticExploreSizeEstimator.EstimateQueryEntitySelection(entity));
            entities.Add(entity);
        }

        entities.Sort(static (left, right) => StringComparer.Ordinal.Compare(
            RecordKey(left.Record),
            RecordKey(right.Record)));
        foreach (var entity in entities)
        {
            var providerNode = new SemanticOwnershipNodeDto(
                NodeId("provider", entity.OwnerId),
                SemanticOwnershipNodeKindDto.Provider,
                entity.OwnerId,
                Record: null,
                entity.OwnerId);
            var fileNode = new SemanticOwnershipNodeDto(
                NodeId("file", entity.SourceFile),
                SemanticOwnershipNodeKindDto.File,
                entity.SourceFile,
                Record: null,
                entity.OwnerId);
            var entityNode = new SemanticOwnershipNodeDto(
                NodeId("entity", RecordKey(entity.Record)),
                SemanticOwnershipNodeKindDto.Entity,
                entity.Title,
                entity.Record,
                entity.OwnerId);
            AddOwnershipRow(rows, queryBudget, new SemanticOwnershipRow(
                [providerNode, fileNode],
                new SemanticOwnershipEdgeDto(
                    providerNode.NodeId,
                    fileNode.NodeId,
                    SemanticOwnershipEdgeKindDto.Owns),
                []));
            AddOwnershipRow(rows, queryBudget, new SemanticOwnershipRow(
                [fileNode, entityNode],
                new SemanticOwnershipEdgeDto(
                    fileNode.NodeId,
                    entityNode.NodeId,
                    SemanticOwnershipEdgeKindDto.Owns),
                []));
        }

        var referenceKeys = new HashSet<(string SourceKey, string TargetKey, string RelationshipKey)>();
        foreach (var reference in index.Pending.Data.References)
        {
            var identity = (
                reference.SourceKey,
                reference.TargetKey,
                reference.RelationshipKey);
            if (referenceKeys.Contains(identity)
                || recordFilter is not null
                && reference.SourceKey != recordFilter
                && reference.TargetKey != recordFilter)
            {
                continue;
            }

            AdmitQueryRow(
                queryBudget,
                referenceKeys.Count,
                SemanticIndexSizingLimits.MaximumReferenceCount,
                SemanticExploreSizeEstimator.EstimateQueryReferenceSelection(reference));
            referenceKeys.Add(identity);
            var source = index.Pending.Data.Entities[reference.SourceKey];
            var target = index.Pending.Data.Entities[reference.TargetKey];
            var sourceNode = new SemanticOwnershipNodeDto(
                NodeId("entity", reference.SourceKey),
                SemanticOwnershipNodeKindDto.Entity,
                source.Title,
                source.Record,
                source.OwnerId);
            var targetNode = new SemanticOwnershipNodeDto(
                NodeId("entity", reference.TargetKey),
                SemanticOwnershipNodeKindDto.Entity,
                target.Title,
                target.Record,
                target.OwnerId);
            AddOwnershipRow(rows, queryBudget, new SemanticOwnershipRow(
                [sourceNode, targetNode],
                new SemanticOwnershipEdgeDto(
                    sourceNode.NodeId,
                    targetNode.NodeId,
                    SemanticOwnershipEdgeKindDto.References),
                []));
        }

        if (pendingSession is not null)
        {
            var candidates = pendingSession.PendingEdits
                .Select((edit, index) => new { Edit = edit, Index = index })
                .Where(item => item.Edit.Domain is
                    "workflow.items" or "workflow.pokemon" or "workflow.moves")
                .Where(item => item.Edit.RecordId is not null && item.Edit.Field is not null)
                .Where(item =>
                {
                    var key = PendingRecordKey(index.Revision.GameFamily, item.Edit);
                    return index.Pending.Data.Entities.TryGetValue(key, out var entity)
                        && entity.Fields.ContainsKey(item.Edit.Field!);
                })
                .Where(item => recordFilter is null
                    || PendingRecordKey(index.Revision.GameFamily, item.Edit) == recordFilter)
                .ToArray();

            foreach (var item in candidates)
            {
                var recordKey = PendingRecordKey(index.Revision.GameFamily, item.Edit);
                var entity = index.Pending.Data.Entities[recordKey];
                var operationNode = PendingOperationNode(
                    pendingSession,
                    item.Edit,
                    item.Index,
                    entity.OwnerId);
                var entityNode = new SemanticOwnershipNodeDto(
                    NodeId("entity", recordKey),
                    SemanticOwnershipNodeKindDto.Entity,
                    entity.Title,
                    entity.Record,
                    entity.OwnerId);
                var fileNode = new SemanticOwnershipNodeDto(
                    NodeId("file", entity.SourceFile),
                    SemanticOwnershipNodeKindDto.File,
                    entity.SourceFile,
                    Record: null,
                    entity.OwnerId);
                AddOwnershipRow(rows, queryBudget, new SemanticOwnershipRow(
                    [operationNode, entityNode],
                    new SemanticOwnershipEdgeDto(
                        operationNode.NodeId,
                        entityNode.NodeId,
                        SemanticOwnershipEdgeKindDto.Targets),
                    []));
                AddOwnershipRow(rows, queryBudget, new SemanticOwnershipRow(
                    [operationNode, fileNode],
                    new SemanticOwnershipEdgeDto(
                        operationNode.NodeId,
                        fileNode.NodeId,
                        SemanticOwnershipEdgeKindDto.Targets),
                    []));
            }

            foreach (var group in candidates.GroupBy(
                         item => (item.Edit.Domain, item.Edit.RecordId, item.Edit.Field)))
            {
                var operationNodes = new List<SemanticOwnershipNodeDto>();
                foreach (var item in group)
                {
                    if (operationNodes.Count >= MaximumConflictNodeIds)
                    {
                        throw new SemanticExploreValidationException(
                            "A semantic ownership conflict exceeds its bounded operation limit.",
                            SemanticExploreFailureKind.LimitExceeded);
                    }

                    operationNodes.Add(PendingOperationNode(
                        pendingSession,
                        item.Edit,
                        item.Index,
                        index.Pending.Data.Entities[
                            PendingRecordKey(index.Revision.GameFamily, item.Edit)].OwnerId));
                }

                if (operationNodes.Count < 2)
                {
                    continue;
                }

                var conflictId = "conflict-" + Hash(
                    "semantic-pending-conflict-v1",
                    group.Key.Domain,
                    group.Key.RecordId!,
                    group.Key.Field!)[..24];
                var conflict = new SemanticOwnershipConflictDto(
                    conflictId,
                    "Multiple pending operations target the same semantic field.",
                    SemanticImpactSeverityDto.Warning,
                    operationNodes.Select(node => node.NodeId).ToArray());
                for (var position = 1; position < operationNodes.Count; position++)
                {
                    AddOwnershipRow(rows, queryBudget, new SemanticOwnershipRow(
                        [operationNodes[0], operationNodes[position]],
                        new SemanticOwnershipEdgeDto(
                            operationNodes[0].NodeId,
                            operationNodes[position].NodeId,
                            SemanticOwnershipEdgeKindDto.Conflicts),
                        [conflict]));
                }
            }
        }

        return rows;
    }

    private static void AddOwnershipRow(
        ICollection<SemanticOwnershipRow> rows,
        SemanticMaterializationBudget queryBudget,
        SemanticOwnershipRow row)
    {
        AdmitQueryRow(
            queryBudget,
            rows.Count,
            SemanticIndexSizingLimits.MaximumOwnershipRowCount,
            SemanticExploreSizeEstimator.EstimateOwnershipRow(
                row.Nodes,
                row.Edge,
                row.Conflicts));
        rows.Add(row);
    }

    private static SemanticOwnershipNodeDto PendingOperationNode(
        EditSessionDto session,
        PendingEditDto edit,
        int index,
        string ownerId)
    {
        return new SemanticOwnershipNodeDto(
            NodeId(
                "pending",
                edit.Association?.OperationId
                    ?? $"{session.SessionId}:{index.ToString(CultureInfo.InvariantCulture)}"),
            SemanticOwnershipNodeKindDto.PendingOperation,
            "Pending field change",
            Record: null,
            ownerId);
    }

    private static string PendingRecordKey(
        SemanticGameFamilyDto family,
        PendingEditDto edit)
    {
        var kind = edit.Domain switch
        {
            "workflow.items" => "item",
            "workflow.pokemon" => "pokemon-personal",
            "workflow.moves" => "move",
            _ => "unsupported",
        };
        return RecordKey(new SemanticRecordRefDto(
            family,
            edit.Domain,
            new SemanticRecordKindDto(kind, 1),
            edit.RecordId!,
            SubrecordId: null));
    }

    private static SemanticDifferenceKindDto? ClassifyEntityChange(
        SemanticIndexedEntity? baseline,
        SemanticIndexedEntity current)
    {
        if (baseline is null)
        {
            return SemanticDifferenceKindDto.Added;
        }

        return baseline.Fields.Count != current.Fields.Count
               || baseline.Fields.Any(pair =>
                   !current.Fields.TryGetValue(pair.Key, out var currentField)
                   || !Equals(pair.Value.Value, currentField.Value))
            ? SemanticDifferenceKindDto.Changed
            : null;
    }

    private static bool SearchMatches(SemanticIndexedEntity entity, string searchText)
    {
        return searchText.Length == 0
            || entity.Title.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || entity.Record.RecordId.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || (entity.Summary?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static SemanticIndexedLayer GetCurrentLayer(
        SemanticProjectIndex index,
        SemanticSourceLayerKindDto kind)
    {
        return kind switch
        {
            SemanticSourceLayerKindDto.Base => index.Base,
            SemanticSourceLayerKindDto.Layered => index.Layered,
            SemanticSourceLayerKindDto.Pending => index.Pending,
            _ => throw new SemanticExploreValidationException(
                "The requested semantic source layer is unsupported by this command.",
                SemanticExploreFailureKind.Unsupported),
        };
    }

    private static void RequireEntity(SemanticIndexedLayer layer, string key)
    {
        if (!layer.Data.Entities.ContainsKey(key))
        {
            throw new SemanticExploreValidationException(
                "The requested semantic entity is unavailable in this source layer.",
                SemanticExploreFailureKind.Unsupported);
        }
    }

    private static SemanticRecordRefDto ValidateRecord(
        SemanticRecordRefDto record,
        SemanticGameFamilyDto expectedFamily)
    {
        ArgumentNullException.ThrowIfNull(record);
        var expectedKind = record.Domain switch
        {
            "workflow.items" => "item",
            "workflow.pokemon" => "pokemon-personal",
            "workflow.moves" => "move",
            _ => null,
        };
        if (record.GameFamily != expectedFamily
            || expectedKind is null
            || record.RecordKind is not { SchemaVersion: 1 }
            || record.RecordKind.Key != expectedKind
            || record.SubrecordId is not null
            || !int.TryParse(record.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            || id < 0
            || record.RecordId != id.ToString(CultureInfo.InvariantCulture))
        {
            throw new SemanticExploreValidationException(
                "The semantic record reference is unsupported or invalid.",
                SemanticExploreFailureKind.Unsupported);
        }

        return record;
    }

    private static void ValidateScope(SemanticExploreScopeDto scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(scope.Paths);
        if (scope.Paths.SelectedGame is null)
        {
            throw new SemanticExploreValidationException(
                "Semantic exploration requires an exact selected game.",
                SemanticExploreFailureKind.Unsupported);
        }

        var corePaths = ToCorePaths(scope.Paths);
        var computedProjectId = ProjectIdentity.FromPaths(corePaths).Value;
        if (!string.Equals(scope.ProjectId, computedProjectId, StringComparison.Ordinal))
        {
            throw new SemanticExploreValidationException(
                "The semantic project scope does not match the configured project.",
                SemanticExploreFailureKind.InvalidData);
        }

        if (scope.SourceObservationToken is not null
            && !IsSourceObservationToken(scope.SourceObservationToken))
        {
            throw new SemanticExploreValidationException(
                "The semantic source observation token is malformed.",
                SemanticExploreFailureKind.InvalidData);
        }

        ValidatePendingSession(scope.PendingSession);

        if (scope.PendingSession?.AuthoringBinding is { } binding
            && !string.Equals(binding.ProjectId, scope.ProjectId, StringComparison.Ordinal))
        {
            throw new SemanticExploreValidationException(
                "The pending semantic session belongs to another project.",
                SemanticExploreFailureKind.StaleRevision);
        }

    }

    private static void ValidatePendingSession(EditSessionDto? session)
    {
        if (session is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(session.SessionId)
            || session.SessionId != session.SessionId.Trim()
            || session.SessionId.Length > 1_024
            || session.SessionId.Any(IsUnsafeUnicode)
            || session.PendingEdits is null)
        {
            throw new SemanticExploreValidationException(
                "The pending semantic session is malformed.",
                SemanticExploreFailureKind.InvalidData);
        }

        if (session.PendingEdits.Count > ChangeSetContract.MaximumOperationCount)
        {
            throw new SemanticExploreValidationException(
                "The pending semantic session exceeds its bounded edit limit.",
                SemanticExploreFailureKind.LimitExceeded);
        }

        if (session.HasPendingChanges != (session.PendingEdits.Count > 0))
        {
            throw new SemanticExploreValidationException(
                "The pending semantic session state does not match its edits.",
                SemanticExploreFailureKind.InvalidData);
        }

        ValidatePendingAuthoringBinding(session.AuthoringBinding);

        foreach (var edit in session.PendingEdits)
        {
            if (edit is null
                || string.IsNullOrWhiteSpace(edit.Domain)
                || edit.Domain != edit.Domain.Trim()
                || edit.Domain.Length > MaximumPendingDomainLength
                || edit.Domain.Any(IsUnsafeUnicode)
                || string.IsNullOrWhiteSpace(edit.Summary)
                || edit.Summary.Length > MaximumPendingSummaryLength
                || edit.Summary.Any(IsUnsafeUnicode)
                || !IsBoundedPendingOptional(edit.RecordId, MaximumPendingStableIdLength, trim: true)
                || !IsBoundedPendingOptional(edit.Field, MaximumPendingDomainLength, trim: true)
                || !IsBoundedPendingOptional(
                    edit.NewValue,
                    MaximumPendingValueLength,
                    trim: false,
                    allowEmpty: true)
                || !IsBoundedPendingOptional(edit.Owner, MaximumPendingStableIdLength, trim: true)
                || !IsValidPendingAssociation(edit.Association)
                || edit.Sources is null
                || edit.Sources.Any(source => source is null
                    || !Enum.IsDefined(source.Layer)
                    || source.RelativePath is null))
            {
                throw new SemanticExploreValidationException(
                    "The pending semantic session contains a malformed edit.",
                    SemanticExploreFailureKind.InvalidData);
            }

            if (edit.Sources.Count > MaximumPendingSourcesPerEdit)
            {
                throw new SemanticExploreValidationException(
                    "A pending semantic edit exceeds its bounded source limit.",
                    SemanticExploreFailureKind.LimitExceeded);
            }

            foreach (var source in edit.Sources)
            {
                _ = NormalizePendingSourceFile(source.RelativePath);
            }
        }
    }

    private static void ValidatePendingAuthoringBinding(EditSessionAuthoringBindingDto? binding)
    {
        if (binding is null)
        {
            return;
        }

        if (binding.Version != 1
            || !IsBoundedPendingOptional(binding.ProjectId, 128, trim: true, requireValue: true)
            || !IsSha256Fingerprint(binding.WorkspaceETag)
            || !IsSha256Fingerprint(binding.WorkspaceFingerprint)
            || !IsSha256Fingerprint(binding.OutputRootFingerprint)
            || binding.SelectedChangeSetIds is null
            || binding.SelectedChangeSetIds.Count > 64
            || binding.SelectedChangeSetIds.Distinct(StringComparer.Ordinal).Count()
                != binding.SelectedChangeSetIds.Count
            || binding.SelectedChangeSetIds.Any(id => !IsPendingAssociationId(id))
            || (binding.OutputProfileId is not null
                && !IsPendingAssociationId(binding.OutputProfileId))
            || (binding.WorkspacePersonalStateETag is not null
                && !IsSha256Fingerprint(binding.WorkspacePersonalStateETag))
            || (binding.OutputProfileId is null)
                != (binding.WorkspacePersonalStateETag is null)
            || (binding.OutputMode is not null && !Enum.IsDefined(binding.OutputMode.Value)))
        {
            throw new SemanticExploreValidationException(
                "The pending semantic session authoring binding is malformed.",
                SemanticExploreFailureKind.InvalidData);
        }
    }

    private static bool IsBoundedPendingOptional(
        string? value,
        int maximumLength,
        bool trim,
        bool requireValue = false,
        bool allowEmpty = false)
    {
        if (value is null)
        {
            return !requireValue;
        }

        return (allowEmpty || value.Length > 0)
            && value.Length <= maximumLength
            && (!trim || value == value.Trim())
            && !value.Any(IsUnsafeUnicode);
    }

    private static bool IsValidPendingAssociation(PendingEditAssociationDto? association)
    {
        return association is null
            || (association.Version == 1
                && IsPendingAssociationId(association.ChangeSetId)
                && IsPendingAssociationId(association.OperationId));
    }

    private static bool IsPendingAssociationId(string? value)
    {
        return value is { Length: > 0 and <= 128 }
            && char.IsAsciiLetterOrDigit(value[0])
            && value.All(character =>
                char.IsAsciiLetterOrDigit(character)
                || character is '.' or '-' or '_');
    }

    private static ProjectPaths ToCorePaths(ProjectPathsDto paths)
    {
        return new ProjectPaths(
            paths.BaseRomFsPath,
            paths.BaseExeFsPath,
            paths.OutputRootPath,
            paths.SaveFilePath,
            paths.ScarletVioletSupportFolderPath,
            paths.SelectedGame switch
            {
                ProjectGameDto.Sword => ProjectGame.Sword,
                ProjectGameDto.Shield => ProjectGame.Shield,
                ProjectGameDto.Scarlet => ProjectGame.Scarlet,
                ProjectGameDto.Violet => ProjectGame.Violet,
                ProjectGameDto.ZA => ProjectGame.ZA,
                null => null,
                _ => throw new SemanticExploreValidationException(
                    "The selected semantic game is invalid.",
                    SemanticExploreFailureKind.InvalidData),
            })
        {
            GameTextLanguage = paths.GameTextLanguage,
            PokemonLegendsZASupportFolderPath = paths.PokemonLegendsZASupportFolderPath,
        };
    }

    private SemanticIndexedLayer ResolveExternalLayer(
        SemanticProjectIndex index,
        CompareExternalSemanticRequest request)
    {
        var hasPath = request.ExternalRootPath is not null;
        var hasInstanceId = request.ComparedModInstanceId is not null;
        if (hasPath == hasInstanceId
            || (hasPath && request.Cursor is not null)
            || (hasInstanceId && request.Cursor is null))
        {
            throw new SemanticExploreValidationException(
                "An external semantic comparison must either select a new overlay or continue an existing page.",
                SemanticExploreFailureKind.ExternalRejected);
        }

        var coreRevision = ToCoreRevision(index.Revision);
        if (request.ComparedModInstanceId is { } instanceId)
        {
            if (!IsComparedModInstanceId(instanceId))
            {
                throw new SemanticExploreValidationException(
                    "The compared-mod snapshot identity is invalid.",
                    SemanticExploreFailureKind.ExternalSnapshotUnavailable);
            }

            var key = new DerivedIndexCacheKey(
                coreRevision,
                $"semantic-external-v1.{instanceId}");
            if (!externalCache.TryGet(key, out var cached))
            {
                throw new SemanticExploreValidationException(
                    "The external semantic snapshot is no longer available. Select the overlay again.",
                    SemanticExploreFailureKind.ExternalSnapshotUnavailable);
            }

            return cached;
        }

        var externalRoot = ValidateExternalRoot(request.Scope.Paths, request.ExternalRootPath!);
        var externalPaths = request.Scope.Paths with { OutputRootPath = externalRoot.Path };
        EnsureExternalRootIdentity(externalRoot);
        var initialExternalSourceFingerprint = CaptureExternalSourceFingerprint(externalPaths);
        var provider = GetProvider(index.Revision.GameFamily);
        var externalData = BuildSourceLayer(
            provider,
            externalPaths,
            new SemanticMaterializationBudget());
        EnsureExternalRootIdentity(externalRoot);
        var completedExternalSourceFingerprint = CaptureExternalSourceFingerprint(externalPaths);
        if (!string.Equals(
                initialExternalSourceFingerprint,
                completedExternalSourceFingerprint,
                StringComparison.Ordinal))
        {
            throw new SemanticExploreValidationException(
                "The selected external overlay changed while it was indexed. Select it again.",
                SemanticExploreFailureKind.ExternalRejected);
        }

        ValidateLayerBounds(externalData);
        if (externalData.DomainStatuses.Any(status => !status.Available))
        {
            throw new SemanticExploreValidationException(
                "The selected external overlay could not be indexed by every supported semantic provider.",
                SemanticExploreFailureKind.ExternalRejected);
        }

        var externalFingerprint = Hash(
            "semantic-external-layer-v2",
            completedExternalSourceFingerprint,
            FingerprintLayer(externalData, pendingSession: null));
        var createdInstanceId = "mod-" + Hash(
            "semantic-compared-mod-v1",
            index.Revision.ProjectId,
            externalFingerprint)[..24];
        var external = new SemanticIndexedLayer(
            externalData,
            new SemanticSourceSnapshotDto(
                new SemanticSourceLayerDto(
                    SemanticSourceLayerKindDto.ComparedMod,
                    createdInstanceId),
                index.Revision,
                externalFingerprint));
        var cacheKey = new DerivedIndexCacheKey(
            coreRevision,
            $"semantic-external-v1.{createdInstanceId}");
        var estimatedSize = EstimateLayerSize(external);
        if (estimatedSize > SemanticIndexSizingLimits.MaximumIndexSizeBytes)
        {
            throw new SemanticExploreValidationException(
                "The external semantic snapshot exceeds the comparison cache limit.",
                SemanticExploreFailureKind.LimitExceeded);
        }

        if (!externalCache.Set(cacheKey, external, estimatedSize))
        {
            throw new SemanticExploreValidationException(
                "The external semantic snapshot exceeds the comparison cache limit.",
                SemanticExploreFailureKind.LimitExceeded);
        }

        return external;
    }

    private SemanticExternalRegistration BuildSemanticMergeExternalRegistration(
        SemanticProjectIndex index,
        ProjectPathsDto projectPaths,
        string externalRootPath)
    {
        var externalRoot = ValidateExternalRoot(projectPaths, externalRootPath);
        var externalPaths = projectPaths with { OutputRootPath = externalRoot.Path };
        EnsureExternalRootIdentity(externalRoot);
        var initialSourceFingerprint = CaptureExternalSourceFingerprint(externalPaths);
        var materializationBudget = new SemanticMaterializationBudget();
        materializationBudget.Admit(
            EstimateExternalRootSize(externalRoot),
            "The semantic merge source snapshot exceeds its bounded cache budget.");
        var data = GetProvider(index.Revision.GameFamily).Build(
            LoadCorpus(externalPaths),
            materializationBudget);
        EnsureExternalRootIdentity(externalRoot);
        var completedSourceFingerprint = CaptureExternalSourceFingerprint(externalPaths);
        if (!string.Equals(
                initialSourceFingerprint,
                completedSourceFingerprint,
                StringComparison.Ordinal))
        {
            throw new SemanticExploreValidationException(
                "The selected semantic merge source changed while it was indexed. Select it again.",
                SemanticExploreFailureKind.ExternalRejected);
        }

        ValidateLayerBounds(data);
        if (data.DomainStatuses.Any(status => !status.Available))
        {
            throw new SemanticExploreValidationException(
                "The selected semantic merge source could not be indexed by every required provider.",
                SemanticExploreFailureKind.ExternalRejected);
        }

        var layerFingerprint = Hash(
            "semantic-external-layer-v2",
            completedSourceFingerprint,
            FingerprintLayer(data, pendingSession: null));
        var instanceId = "mod-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12));
        return new SemanticExternalRegistration(
            new SemanticIndexedLayer(
                data,
                new SemanticSourceSnapshotDto(
                    new SemanticSourceLayerDto(
                        SemanticSourceLayerKindDto.ComparedMod,
                        instanceId),
                    index.Revision,
                    layerFingerprint)),
            externalRoot,
            completedSourceFingerprint);
    }

    private SemanticIndexedLayer ReobserveExternalLayer(
        SemanticProjectIndex index,
        ProjectPathsDto projectPaths,
        string instanceId)
    {
        if (!IsComparedModInstanceId(instanceId))
        {
            throw new SemanticExploreValidationException(
                "The semantic merge source identity is invalid.",
                SemanticExploreFailureKind.ExternalSnapshotUnavailable);
        }

        var registration = GetSemanticMergeExternalRegistration(index, instanceId);

        EnsureExternalRootIdentity(registration.Root);
        var externalPaths = projectPaths with { OutputRootPath = registration.Root.Path };
        var initialFingerprint = CaptureExternalSourceFingerprint(externalPaths);
        if (!string.Equals(
                initialFingerprint,
                registration.SourceFingerprint,
                StringComparison.Ordinal))
        {
            throw new SemanticExploreValidationException(
                "The selected semantic merge source changed. Select it again.",
                SemanticExploreFailureKind.ExternalSnapshotUnavailable);
        }

        var data = GetProvider(index.Revision.GameFamily).Build(
            LoadCorpus(externalPaths),
            new SemanticMaterializationBudget());
        EnsureExternalRootIdentity(registration.Root);
        var completedFingerprint = CaptureExternalSourceFingerprint(externalPaths);
        if (!string.Equals(
                completedFingerprint,
                registration.SourceFingerprint,
                StringComparison.Ordinal))
        {
            throw new SemanticExploreValidationException(
                "The selected semantic merge source changed. Select it again.",
                SemanticExploreFailureKind.ExternalSnapshotUnavailable);
        }

        ValidateLayerBounds(data);
        if (data.DomainStatuses.Any(status => !status.Available))
        {
            throw new SemanticExploreValidationException(
                "The selected semantic merge source is no longer supported by every required provider.",
                SemanticExploreFailureKind.ExternalSnapshotUnavailable);
        }

        var layerFingerprint = Hash(
            "semantic-external-layer-v2",
            completedFingerprint,
            FingerprintLayer(data, pendingSession: null));
        if (!string.Equals(
                layerFingerprint,
                registration.Layer.Snapshot.Fingerprint,
                StringComparison.Ordinal))
        {
            throw new SemanticExploreValidationException(
                "The selected semantic merge source changed. Select it again.",
                SemanticExploreFailureKind.ExternalSnapshotUnavailable);
        }

        return new SemanticIndexedLayer(data, registration.Layer.Snapshot);
    }

    private SemanticExternalRegistration GetSemanticMergeExternalRegistration(
        SemanticProjectIndex index,
        string instanceId)
    {
        if (!IsComparedModInstanceId(instanceId))
        {
            throw new SemanticExploreValidationException(
                "The semantic merge source identity is invalid.",
                SemanticExploreFailureKind.ExternalSnapshotUnavailable);
        }

        var key = new DerivedIndexCacheKey(
            ToCoreRevision(index.Revision),
            $"semantic-merge-external-v1.{instanceId}");
        if (!semanticMergeExternalCache.TryGet(key, out var registration))
        {
            throw new SemanticExploreValidationException(
                "The semantic merge source snapshot is no longer available. Select it again.",
                SemanticExploreFailureKind.ExternalSnapshotUnavailable);
        }

        return registration;
    }


    private string CaptureExternalSourceFingerprint(ProjectPathsDto externalPaths)
    {
        try
        {
            var fingerprint = captureExactSourceFingerprint(externalPaths);
            if (!IsSha256Fingerprint(fingerprint))
            {
                throw new SemanticExploreValidationException(
                    "The external semantic provider returned an invalid source observation.",
                    SemanticExploreFailureKind.ExternalRejected);
            }

            return fingerprint.ToLowerInvariant();
        }
        catch (SemanticExploreValidationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ProjectFileGraphDiscoveryException
            || ContainsInvalidDataException(exception))
        {
            throw new SemanticExploreValidationException(
                "The selected external overlay exceeds the safe semantic observation bounds.",
                SemanticExploreFailureKind.LimitExceeded,
                exception);
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            throw new SemanticExploreValidationException(
                "The selected external overlay could not be observed safely.",
                SemanticExploreFailureKind.ExternalRejected,
                exception);
        }
    }

    private static bool IsComparedModInstanceId(string value)
    {
        return value.Length == 28
            && value.StartsWith("mod-", StringComparison.Ordinal)
            && value.AsSpan(4).ContainsAnyExcept("0123456789abcdef") is false;
    }

    private static bool ContainsInvalidDataException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is InvalidDataException)
            {
                return true;
            }
        }

        return false;
    }

    private static ValidatedExternalRoot ValidateExternalRoot(ProjectPathsDto projectPaths, string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path != path.Trim()
            || path.Length > SemanticExploreContract.MaximumExternalRootLength
            || path.Any(IsUnsafeUnicode))
        {
            throw new SemanticExploreValidationException(
                "The selected external overlay root is invalid.",
                SemanticExploreFailureKind.ExternalRejected);
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is
            ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new SemanticExploreValidationException(
                "The selected external overlay root is invalid.",
                SemanticExploreFailureKind.ExternalRejected,
                exception);
        }

        if (!Path.IsPathFullyQualified(fullPath) || !Directory.Exists(fullPath))
        {
            throw new SemanticExploreValidationException(
                "The selected external overlay root is unavailable.",
                SemanticExploreFailureKind.ExternalRejected);
        }

        ValidatedExternalRoot validatedRoot;
        try
        {
            var externalIdentity = ResolvePhysicalExistingPath(fullPath);
            var privatePaths = EnumeratePrivateProjectPaths(projectPaths)
                .Select(candidate => new PhysicalPathIdentity(
                    candidate,
                    ResolvePhysicalExistingPath(candidate)))
                .ToArray();
            if (IsUnverifiableWindowsNetworkPath(fullPath, externalIdentity)
                || privatePaths.Any(candidate =>
                    IsUnverifiableWindowsNetworkPath(candidate.Path, candidate.Identity)))
            {
                throw new SemanticExploreValidationException(
                    "External semantic comparison requires local paths with provable physical identity.",
                    SemanticExploreFailureKind.ExternalRejected);
            }

            if (ContainsReservedMetadataSegment(fullPath)
                || ContainsReservedMetadataSegment(externalIdentity)
                || privatePaths.Any(candidate =>
                    PathsOverlap(externalIdentity, candidate.Identity)))
            {
                throw new SemanticExploreValidationException(
                    "The selected external overlay root overlaps a private project source or metadata root.",
                    SemanticExploreFailureKind.ExternalRejected);
            }

            validatedRoot = new ValidatedExternalRoot(fullPath, externalIdentity, privatePaths);
        }
        catch (SemanticExploreValidationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            UnauthorizedAccessException or IOException or SecurityException or
            PathTooLongException or ArgumentException or NotSupportedException)
        {
            throw new SemanticExploreValidationException(
                "The selected external overlay root identity could not be verified safely.",
                SemanticExploreFailureKind.ExternalRejected,
                exception);
        }

        var externalPaths = ToCorePaths(projectPaths) with { OutputRootPath = fullPath };
        ProjectHealth health;
        try
        {
            health = new ProjectValidator().Validate(externalPaths);
        }
        catch (ProjectFileGraphDiscoveryException exception)
        {
            throw new SemanticExploreValidationException(
                "The selected external overlay exceeds the safe traversal bounds.",
                SemanticExploreFailureKind.LimitExceeded,
                exception);
        }

        var outputValidation = health.Paths.First(pathResult => pathResult.Role == ProjectPathRole.OutputRoot);
        if (outputValidation.Status != ProjectPathStatus.Valid || outputValidation.HasBlockingError)
        {
            throw new SemanticExploreValidationException(
                "The selected external overlay failed project path validation.",
                SemanticExploreFailureKind.ExternalRejected);
        }

        ValidateExternalOverlayTree(fullPath);
        EnsureExternalRootIdentity(validatedRoot);

        return validatedRoot;
    }

    private static IEnumerable<string> EnumeratePrivateProjectPaths(ProjectPathsDto paths)
    {
        if (!string.IsNullOrWhiteSpace(paths.BaseRomFsPath))
        {
            yield return paths.BaseRomFsPath;
        }

        if (!string.IsNullOrWhiteSpace(paths.BaseExeFsPath))
        {
            yield return paths.BaseExeFsPath;
        }

        if (!string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            yield return paths.OutputRootPath;
        }

        if (!string.IsNullOrWhiteSpace(paths.SaveFilePath))
        {
            yield return paths.SaveFilePath;
        }

        if (!string.IsNullOrWhiteSpace(paths.ScarletVioletSupportFolderPath))
        {
            yield return paths.ScarletVioletSupportFolderPath;
        }

        if (!string.IsNullOrWhiteSpace(paths.PokemonLegendsZASupportFolderPath))
        {
            yield return paths.PokemonLegendsZASupportFolderPath;
        }
    }

    private static bool PathsOverlap(string path, string candidate)
    {
        return IsSameOrDescendant(path, candidate)
            || IsSameOrDescendant(candidate, path);
    }

    private static bool IsSameOrDescendant(string path, string ancestor)
    {
        var comparison = PhysicalPathComparison;
        if (string.Equals(path, ancestor, comparison))
        {
            return true;
        }

        var prefix = ancestor + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, comparison);
    }

    private static void EnsureExternalRootIdentity(ValidatedExternalRoot root)
    {
        try
        {
            var currentExternalIdentity = ResolvePhysicalExistingPath(root.Path);
            if (!string.Equals(
                    currentExternalIdentity,
                    root.Identity,
                    PhysicalPathComparison)
                || ContainsReservedMetadataSegment(currentExternalIdentity))
            {
                throw new SemanticExploreValidationException(
                    "The selected external overlay root changed while it was inspected.",
                    SemanticExploreFailureKind.ExternalRejected);
            }

            foreach (var privatePath in root.PrivatePaths)
            {
                var currentPrivateIdentity = ResolvePhysicalExistingPath(privatePath.Path);
                if (!string.Equals(
                        currentPrivateIdentity,
                        privatePath.Identity,
                        PhysicalPathComparison)
                    || PathsOverlap(currentExternalIdentity, currentPrivateIdentity))
                {
                    throw new SemanticExploreValidationException(
                        "The selected external overlay root overlaps a private project source or metadata root.",
                        SemanticExploreFailureKind.ExternalRejected);
                }
            }
        }
        catch (SemanticExploreValidationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            UnauthorizedAccessException or IOException or SecurityException or
            PathTooLongException or ArgumentException or NotSupportedException)
        {
            throw new SemanticExploreValidationException(
                "The selected external overlay root identity could not be reverified safely.",
                SemanticExploreFailureKind.ExternalRejected,
                exception);
        }
    }

    private static StringComparison PhysicalPathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static string ResolvePhysicalExistingPath(string path)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
        if (!Path.IsPathFullyQualified(fullPath)
            || (!Directory.Exists(fullPath) && !File.Exists(fullPath)))
        {
            throw new IOException("A configured project path is unavailable.");
        }

        return OperatingSystem.IsWindows()
            ? ResolveWindowsFinalPath(fullPath)
            : ResolveExistingPathLinks(fullPath);
    }

    private static bool IsUnverifiableWindowsNetworkPath(string path, string identity)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        return path.StartsWith("\\\\", StringComparison.Ordinal)
            || identity.StartsWith("\\Device\\Mup\\", StringComparison.OrdinalIgnoreCase)
            || identity.StartsWith(
                "\\Device\\LanmanRedirector\\",
                StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveExistingPathLinks(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new IOException("A configured project path has no physical root.");
        }

        var current = Path.TrimEndingDirectorySeparator(root);
        if (current.Length == 0)
        {
            current = root;
        }

        var relative = Path.GetRelativePath(root, fullPath);
        if (relative == ".")
        {
            return current;
        }

        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(current, segment);
            FileSystemInfo info = Directory.Exists(candidate)
                ? new DirectoryInfo(candidate)
                : new FileInfo(candidate);
            info.Refresh();
            if (!info.Exists)
            {
                throw new IOException("A configured project path is unavailable.");
            }

            current = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName
                ?? info.FullName;
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(current));
    }

    private static string ResolveWindowsFinalPath(string fullPath)
    {
        using var handle = CreateFileForFinalPath(
            fullPath,
            desiredAccess: 0,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw new IOException("A configured project path could not be opened for identity verification.");
        }

        var capacity = 512;
        while (capacity <= 32_768)
        {
            var buffer = new StringBuilder(capacity);
            var length = GetFinalPathNameByHandle(
                handle,
                buffer,
                checked((uint)buffer.Capacity),
                VolumeNameNt);
            if (length == 0)
            {
                throw new IOException("A configured project path identity could not be resolved.");
            }

            if (length < buffer.Capacity)
            {
                return Path.TrimEndingDirectorySeparator(buffer.ToString());
            }

            capacity = checked((int)length + 1);
        }

        throw new PathTooLongException("A configured project path identity exceeds its safe bound.");
    }

    private static bool ContainsReservedMetadataSegment(string fullPath)
    {
        return fullPath
            .Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => string.Equals(segment, ".km", StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidateExternalOverlayTree(string rootPath)
    {
        var pending = new Stack<(DirectoryInfo Directory, int Depth)>();
        pending.Push((new DirectoryInfo(rootPath), 0));
        var directoryCount = 0;
        var entryCount = 0;
        long aggregateBytes = 0;
        while (pending.Count > 0)
        {
            var (directory, depth) = pending.Pop();
            if (depth > MaximumExternalTraversalDepth
                || ++directoryCount > MaximumExternalDirectories)
            {
                throw new SemanticExploreValidationException(
                    "The selected external overlay exceeds the safe traversal bounds.",
                    SemanticExploreFailureKind.LimitExceeded);
            }

            IEnumerable<FileSystemInfo> entries;
            try
            {
                directory.Refresh();
                if (!directory.Exists
                    || (directory.Attributes & FileAttributes.ReparsePoint) != 0
                    || !string.IsNullOrEmpty(directory.LinkTarget))
                {
                    throw new SemanticExploreValidationException(
                        "The selected external overlay contains an unsafe linked directory.",
                        SemanticExploreFailureKind.ExternalRejected);
                }

                entries = directory.EnumerateFileSystemInfos();
            }
            catch (SemanticExploreValidationException)
            {
                throw;
            }
            catch (Exception exception) when (exception is
                UnauthorizedAccessException or IOException or SecurityException or
                PathTooLongException or ArgumentException or NotSupportedException)
            {
                throw new SemanticExploreValidationException(
                    "The selected external overlay could not be inspected safely.",
                    SemanticExploreFailureKind.ExternalRejected,
                    exception);
            }

            try
            {
                foreach (var entry in entries)
                {
                    if (++entryCount > MaximumExternalFileSystemEntries)
                    {
                        throw new SemanticExploreValidationException(
                            "The selected external overlay exceeds the safe traversal bounds.",
                            SemanticExploreFailureKind.LimitExceeded);
                    }

                    if (string.Equals(entry.Name, ".km", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new SemanticExploreValidationException(
                            "The selected external overlay contains a reserved metadata namespace.",
                            SemanticExploreFailureKind.ExternalRejected);
                    }

                    entry.Refresh();
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0
                        || !string.IsNullOrEmpty(entry.LinkTarget))
                    {
                        throw new SemanticExploreValidationException(
                            "The selected external overlay contains an unsafe linked entry.",
                            SemanticExploreFailureKind.ExternalRejected);
                    }

                    if ((entry.Attributes & FileAttributes.Directory) != 0)
                    {
                        pending.Push(((DirectoryInfo)entry, checked(depth + 1)));
                    }
                    else
                    {
                        var fileLength = ((FileInfo)entry).Length;
                        if (fileLength < 0
                            || fileLength > MaximumExternalFileBytes
                            || fileLength > MaximumExternalAggregateBytes - aggregateBytes)
                        {
                            throw new SemanticExploreValidationException(
                                "The selected external overlay exceeds the safe source-byte bounds.",
                                SemanticExploreFailureKind.LimitExceeded);
                        }

                        aggregateBytes = checked(aggregateBytes + fileLength);
                    }
                }
            }
            catch (SemanticExploreValidationException)
            {
                throw;
            }
            catch (Exception exception) when (exception is
                UnauthorizedAccessException or IOException or SecurityException or
                PathTooLongException or ArgumentException or NotSupportedException)
            {
                throw new SemanticExploreValidationException(
                    "The selected external overlay could not be inspected safely.",
                    SemanticExploreFailureKind.ExternalRejected,
                    exception);
            }
        }
    }

    private static void ValidatePage(int limit, string? cursor)
    {
        if (limit is <= 0 or > SemanticExploreContract.MaximumPageSize)
        {
            throw new SemanticExploreValidationException(
                $"A semantic page must request between 1 and {SemanticExploreContract.MaximumPageSize.ToString(CultureInfo.InvariantCulture)} rows.",
                SemanticExploreFailureKind.LimitExceeded);
        }

        if (cursor is { Length: > SemanticExploreContract.MaximumCursorLength }
            || cursor?.Any(IsUnsafeUnicode) is true)
        {
            throw new SemanticExploreValidationException(
                "The semantic continuation cursor is invalid or too large.",
                SemanticExploreFailureKind.InvalidCursor);
        }
    }

    private static string NormalizeSearchText(string value)
    {
        if (value is null
            || value.Length > SemanticExploreContract.MaximumSearchTextLength
            || value.Any(IsUnsafeUnicode))
        {
            throw new SemanticExploreValidationException(
                "The semantic search text is invalid or too large.",
                SemanticExploreFailureKind.InvalidData);
        }

        var normalized = value.Trim().Normalize(NormalizationForm.FormC);
        if (normalized.Length == 0)
        {
            throw new SemanticExploreValidationException(
                "The semantic search text must not be empty.",
                SemanticExploreFailureKind.InvalidData);
        }

        return normalized;
    }

    private static HashSet<string> NormalizeDomains(IReadOnlyList<string>? domains)
    {
        if (domains is null)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        if (domains.Count > MaximumDomainFilters
            || domains.Any(domain => domain is not
                ("workflow.items" or "workflow.pokemon" or "workflow.moves")))
        {
            throw new SemanticExploreValidationException(
                "A semantic search domain filter is unsupported or too large.",
                SemanticExploreFailureKind.Unsupported);
        }

        return domains.ToHashSet(StringComparer.Ordinal);
    }

    private static int DecodeCursor(string? cursor, string queryFingerprint)
    {
        if (cursor is null)
        {
            return 0;
        }

        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded += (padded.Length % 4) switch
            {
                0 => string.Empty,
                2 => "==",
                3 => "=",
                _ => throw new FormatException(),
            };
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            var fields = decoded.Split('\n');
            if (fields is not ["1", var fingerprint, var offsetValue]
                || !string.Equals(fingerprint, queryFingerprint, StringComparison.Ordinal)
                || !int.TryParse(offsetValue, NumberStyles.None, CultureInfo.InvariantCulture, out var offset)
                || offset < 0)
            {
                throw new FormatException();
            }

            return offset;
        }
        catch (Exception exception) when (exception is FormatException or DecoderFallbackException)
        {
            throw new SemanticExploreValidationException(
                "The semantic continuation cursor is invalid for this query.",
                SemanticExploreFailureKind.InvalidCursor,
                exception);
        }
    }

    private static string EncodeCursor(string queryFingerprint, int offset)
    {
        var bytes = Encoding.UTF8.GetBytes(
            $"1\n{queryFingerprint}\n{offset.ToString(CultureInfo.InvariantCulture)}");
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static SemanticPage<T> Page<T>(
        IReadOnlyList<T> items,
        int offset,
        int limit,
        string queryFingerprint)
    {
        if (offset > items.Count)
        {
            throw new SemanticExploreValidationException(
                "The semantic continuation cursor is outside the current result set.",
                SemanticExploreFailureKind.InvalidCursor);
        }

        var pageItems = items.Skip(offset).Take(limit).ToArray();
        var nextOffset = checked(offset + pageItems.Length);
        return new SemanticPage<T>(
            pageItems,
            nextOffset < items.Count ? EncodeCursor(queryFingerprint, nextOffset) : null);
    }

    private static void AdmitQueryRow(
        SemanticMaterializationBudget queryBudget,
        int currentCount,
        int maximumCount,
        long estimatedSizeBytes)
    {
        ArgumentNullException.ThrowIfNull(queryBudget);
        if (currentCount >= maximumCount)
        {
            throw new SemanticExploreValidationException(
                QueryMaterializationLimitMessage,
                SemanticExploreFailureKind.LimitExceeded);
        }

        queryBudget.Admit(estimatedSizeBytes, QueryMaterializationLimitMessage);
    }

    private static string QueryFingerprint(string operation, params string[] fields)
    {
        return Hash("semantic-query-v1", operation, fields);
    }

    private static string Hash(string prefix, params object[] fields)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHash(hash, prefix);
        foreach (var field in fields)
        {
            if (field is string[] array)
            {
                foreach (var item in array)
                {
                    AppendHash(hash, item);
                }
            }
            else
            {
                AppendHash(hash, Convert.ToString(field, CultureInfo.InvariantCulture) ?? string.Empty);
            }
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void AppendHash(IncrementalHash hash, string value)
    {
        if (value.Length > MaximumCanonicalQueryCharacters)
        {
            throw new SemanticExploreValidationException(
                "A semantic fingerprint input exceeds its bounded size.",
                SemanticExploreFailureKind.LimitExceeded);
        }

        var length = Encoding.UTF8.GetByteCount(value).ToString(CultureInfo.InvariantCulture);
        hash.AppendData(Encoding.UTF8.GetBytes(length));
        hash.AppendData("\n"u8);
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData("\n"u8);
    }

    private static string FingerprintLayer(SemanticLayerData layer, EditSessionDto? pendingSession)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHash(hash, "semantic-layer-v1");
        foreach (var status in layer.DomainStatuses.OrderBy(status => status.ProviderId, StringComparer.Ordinal))
        {
            AppendHash(hash, status.ProviderId);
            AppendHash(hash, status.Domain);
            AppendHash(hash, status.Available ? "available" : "unavailable");
            AppendHash(hash, status.ReasonCode ?? string.Empty);
        }

        foreach (var (key, entity) in layer.Entities)
        {
            AppendHash(hash, key);
            AppendHash(hash, entity.Title);
            AppendHash(hash, entity.Summary ?? string.Empty);
            AppendHash(hash, entity.OwnerId);
            AppendHash(hash, entity.SourceFile);
            foreach (var field in entity.Fields.Values.OrderBy(field => field.Key, StringComparer.Ordinal))
            {
                AppendHash(hash, field.Key);
                AppendHash(hash, field.Value.Kind.ToString());
                AppendHash(hash, field.Value.CanonicalValue ?? "null");
            }
        }

        foreach (var reference in layer.References)
        {
            AppendHash(hash, reference.SourceKey);
            AppendHash(hash, reference.TargetKey);
            AppendHash(hash, reference.RelationshipKey);
            AppendHash(hash, reference.ProviderId);
        }

        if (pendingSession is not null)
        {
            AppendHash(hash, pendingSession.SessionId);
            foreach (var edit in pendingSession.PendingEdits)
            {
                AppendHash(hash, edit.Domain);
                AppendHash(hash, edit.RecordId ?? string.Empty);
                AppendHash(hash, edit.Field ?? string.Empty);
                AppendHash(hash, edit.NewValue ?? string.Empty);
                AppendHash(hash, edit.Owner ?? string.Empty);
                AppendHash(hash, edit.Association?.ChangeSetId ?? string.Empty);
                AppendHash(hash, edit.Association?.OperationId ?? string.Empty);
                foreach (var source in edit.Sources)
                {
                    AppendHash(hash, source.Layer.ToString());
                    AppendHash(hash, source.RelativePath);
                }
            }
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void ValidateLayerBounds(SemanticLayerData layer)
    {
        if (layer.Entities.Count > SemanticIndexSizingLimits.MaximumEntityCount
            || layer.References.Count > SemanticIndexSizingLimits.MaximumReferenceCount
            || layer.Entities.Values.Any(entity =>
                entity.Fields.Count > SemanticIndexSizingLimits.MaximumFieldCountPerEntity))
        {
            throw new SemanticExploreValidationException(
                "The semantic index exceeds its bounded provider limits.",
                SemanticExploreFailureKind.LimitExceeded);
        }
    }

    private static long EstimateSize(SemanticProjectIndex index)
    {
        long size = SemanticExploreSizeEstimator.ProjectEnvelopeSizeBytes;
        var observedData = new HashSet<SemanticLayerData>(ReferenceEqualityComparer.Instance);
        foreach (var layer in new[] { index.Base, index.Layered, index.Pending })
        {
            size = checked(size + EstimateLayerSize(layer, observedData.Add(layer.Data)));
        }

        return size;
    }

    private static long EstimateSourceSize(SemanticSourceIndex index)
    {
        long size = SemanticExploreSizeEstimator.ProjectEnvelopeSizeBytes;
        var observedData = new HashSet<SemanticLayerData>(ReferenceEqualityComparer.Instance);
        size = checked(
            size
            + EstimateSourceLayerSize(
                index.BaseData,
                index.BaseFingerprint,
                observedData.Add(index.BaseData)));
        size = checked(
            size
            + EstimateSourceLayerSize(
                index.LayeredData,
                index.LayeredFingerprint,
                observedData.Add(index.LayeredData)));
        return size;
    }

    private static long EstimatePendingOverlaySize(SemanticPendingOverlay overlay)
    {
        return EstimateSourceLayerSize(
            overlay.Data,
            overlay.Fingerprint,
            includeData: true);
    }

    private static long EstimateSourceLayerSize(
        SemanticLayerData data,
        string fingerprint,
        bool includeData)
    {
        long size = SemanticExploreSizeEstimator.LayerEnvelopeSizeBytes;
        size = checked(size + SemanticExploreSizeEstimator.EstimateString(fingerprint));
        if (!includeData)
        {
            return size;
        }

        return checked(size + SemanticExploreSizeEstimator.EstimateLayerData(data));
    }

    private static long EstimateLayerSize(SemanticIndexedLayer layer, bool includeData = true)
    {
        long size = SemanticExploreSizeEstimator.LayerEnvelopeSizeBytes;
        size = checked(
            size + SemanticExploreSizeEstimator.EstimateString(layer.Snapshot.Fingerprint));
        size = checked(
            size + SemanticExploreSizeEstimator.EstimateString(layer.Snapshot.Layer.InstanceId));
        if (!includeData)
        {
            return size;
        }

        return checked(size + SemanticExploreSizeEstimator.EstimateLayerData(layer.Data));
    }

    private static long EstimateExternalRootSize(ValidatedExternalRoot root)
    {
        long size = checked(
            512L
            + SemanticExploreSizeEstimator.EstimateString(root.Path)
            + SemanticExploreSizeEstimator.EstimateString(root.Identity));
        foreach (var privatePath in root.PrivatePaths)
        {
            size = checked(
                size
                + 128L
                + SemanticExploreSizeEstimator.EstimateString(privatePath.Path)
                + SemanticExploreSizeEstimator.EstimateString(privatePath.Identity));
        }

        return size;
    }

    private static IReadOnlyList<SemanticProviderCoverageDto> Coverage(
        SemanticIndexedLayer layer,
        SemanticFeatureDto feature)
    {
        return layer.Data.DomainStatuses
            .OrderBy(status => status.ProviderId, StringComparer.Ordinal)
            .Select(status => ToCoverage(status, feature))
            .ToArray();
    }

    private static IReadOnlyList<SemanticProviderCoverageDto> MergeCoverage(
        SemanticIndexedLayer left,
        SemanticIndexedLayer right,
        SemanticFeatureDto feature)
    {
        return left.Data.DomainStatuses
            .Concat(right.Data.DomainStatuses)
            .GroupBy(status => (status.ProviderId, status.Domain))
            .OrderBy(group => group.Key.ProviderId, StringComparer.Ordinal)
            .Select(group =>
            {
                var statuses = group.ToArray();
                var unavailable = statuses.FirstOrDefault(status => !status.Available);
                if (unavailable is not null)
                {
                    return ToCoverage(unavailable, feature);
                }

                var partial = statuses.FirstOrDefault(status => status.Partial);
                return ToCoverage(partial ?? statuses[0], feature);
            })
            .ToArray();
    }

    private static SemanticProviderCoverageDto ToCoverage(
        SemanticDomainStatus status,
        SemanticFeatureDto feature)
    {
        if (!status.Available)
        {
            return new SemanticProviderCoverageDto(
                status.ProviderId,
                [status.Domain],
                SemanticCoverageStateDto.Unavailable,
                SemanticConfidenceDto.Unknown,
                status.ReasonCode ?? "provider-unavailable");
        }

        var partial = status.Partial || feature is
            SemanticFeatureDto.References
            or SemanticFeatureDto.Impact
            or SemanticFeatureDto.Ownership
            or SemanticFeatureDto.ExternalCompare;
        return new SemanticProviderCoverageDto(
            status.ProviderId,
            [status.Domain],
            partial ? SemanticCoverageStateDto.Partial : SemanticCoverageStateDto.Complete,
            SemanticConfidenceDto.Verified,
            partial
                ? status.ReasonCode ?? "vertical-slice-coverage"
                : null);
    }

    private static SemanticProviderCoverageDto DescriptorCoverage(SemanticDomainStatus status)
    {
        if (!status.Available)
        {
            return ToCoverage(status, SemanticFeatureDto.Search);
        }

        return new SemanticProviderCoverageDto(
            status.ProviderId,
            [status.Domain],
            SemanticCoverageStateDto.Partial,
            SemanticConfidenceDto.Verified,
            "vertical-slice-coverage");
    }

    private static SemanticIndexedLayer Layer(
        SemanticLayerData data,
        SemanticSourceLayerKindDto kind,
        SemanticProjectRevisionDto revision,
        string fingerprint)
    {
        return new SemanticIndexedLayer(
            data,
            new SemanticSourceSnapshotDto(
                new SemanticSourceLayerDto(kind, InstanceId: null),
                revision,
                fingerprint));
    }

    private static IReadOnlyList<SemanticSourceSnapshotDto> OrderedSnapshots(SemanticProjectIndex index)
    {
        return [index.Base.Snapshot, index.Layered.Snapshot, index.Pending.Snapshot];
    }

    private static bool EqualsRevision(
        SemanticProjectRevisionDto current,
        SemanticProjectRevisionDto expected)
    {
        return current.ProjectId == expected.ProjectId
            && current.GameFamily == expected.GameFamily
            && current.Generation == expected.Generation
            && current.Fingerprint == expected.Fingerprint;
    }

    private static bool EqualsSnapshot(
        SemanticSourceSnapshotDto current,
        SemanticSourceSnapshotDto expected)
    {
        return current.Layer.Kind == expected.Layer.Kind
            && string.Equals(current.Layer.InstanceId, expected.Layer.InstanceId, StringComparison.Ordinal)
            && EqualsRevision(current.Revision, expected.Revision)
            && string.Equals(current.Fingerprint, expected.Fingerprint, StringComparison.Ordinal);
    }

    private static string RecordKey(SemanticRecordRefDto record)
    {
        return string.Join(
            ':',
            record.GameFamily,
            record.Domain,
            record.RecordKind.Key,
            record.RecordKind.SchemaVersion.ToString(CultureInfo.InvariantCulture),
            record.RecordId,
            record.SubrecordId ?? string.Empty);
    }

    private static string CanonicalChangePath(SemanticRecordRefDto record, string fieldKey)
    {
        return string.Join(
            '/',
            record.Domain,
            record.RecordKind.Key,
            record.RecordId,
            fieldKey);
    }

    private static string CanonicalValue(SemanticScalarValueDto? value)
    {
        return value?.CanonicalValue ?? "null";
    }

    private static string NodeId(string kind, string identity)
    {
        return $"{kind}-{Hash("semantic-ownership-node-v1", kind, identity)[..24]}";
    }

    private static SemanticGameFamilyDto ToFamily(ProjectGameDto game)
    {
        return game switch
        {
            ProjectGameDto.Sword or ProjectGameDto.Shield => SemanticGameFamilyDto.SwordShield,
            ProjectGameDto.Scarlet or ProjectGameDto.Violet => SemanticGameFamilyDto.ScarletViolet,
            ProjectGameDto.ZA => SemanticGameFamilyDto.LegendsZA,
            _ => throw new SemanticExploreValidationException(
                "The selected game family is unsupported.",
                SemanticExploreFailureKind.Unsupported),
        };
    }

    private static GameFamily ToCoreFamily(SemanticGameFamilyDto family)
    {
        return family switch
        {
            SemanticGameFamilyDto.SwordShield => GameFamily.SwordShield,
            SemanticGameFamilyDto.ScarletViolet => GameFamily.ScarletViolet,
            SemanticGameFamilyDto.LegendsZA => GameFamily.LegendsZA,
            _ => throw new ArgumentOutOfRangeException(nameof(family), family, null),
        };
    }

    private static ProjectSourceRevision ToCoreRevision(SemanticProjectRevisionDto revision)
    {
        return new ProjectSourceRevision(
            new ProjectId(revision.ProjectId),
            ToCoreFamily(revision.GameFamily),
            long.Parse(revision.Generation, NumberStyles.None, CultureInfo.InvariantCulture),
            revision.Fingerprint);
    }

    private static ISemanticExploreFamilyProvider GetProvider(SemanticGameFamilyDto family)
    {
        return family switch
        {
            SemanticGameFamilyDto.SwordShield => new SwShSemanticExploreProvider(),
            SemanticGameFamilyDto.ScarletViolet => new SvSemanticExploreProvider(),
            SemanticGameFamilyDto.LegendsZA => new ZaSemanticExploreProvider(),
            _ => throw new SemanticExploreValidationException(
                "The selected semantic provider is unsupported.",
                SemanticExploreFailureKind.Unsupported),
        };
    }

    private static bool IsUnsafeUnicode(char character)
    {
        return char.IsControl(character)
            || character is '\u061c'
                or '\u200b'
                or '\u200c'
                or '\u200d'
                or '\u200e'
                or '\u200f'
                or '\u202a'
                or '\u202b'
                or '\u202c'
                or '\u202d'
                or '\u202e'
                or '\u2060'
                or '\u2061'
                or '\u2062'
                or '\u2063'
                or '\u2064'
                or '\u2066'
                or '\u2067'
                or '\u2068'
                or '\u2069'
                or '\ufeff';
    }

    private static bool IsFatal(Exception exception)
    {
        return exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or AppDomainUnloadedException;
    }

    [DllImport(
        "kernel32.dll",
        EntryPoint = "CreateFileW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern SafeFileHandle CreateFileForFinalPath(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport(
        "kernel32.dll",
        EntryPoint = "GetFinalPathNameByHandleW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(
        SafeFileHandle file,
        StringBuilder filePath,
        uint filePathLength,
        uint flags);

    private sealed record PhysicalPathIdentity(string Path, string Identity);

    private sealed record ValidatedExternalRoot(
        string Path,
        string Identity,
        IReadOnlyList<PhysicalPathIdentity> PrivatePaths);

    private sealed record SemanticProjectIndex(
        SemanticProjectRevisionDto Revision,
        SemanticIndexedLayer Base,
        SemanticIndexedLayer Layered,
        SemanticIndexedLayer Pending);

    private sealed record SemanticSourceIndex(
        SemanticLayerData BaseData,
        string BaseFingerprint,
        SemanticLayerData LayeredData,
        string LayeredFingerprint);

    private sealed record SemanticPendingOverlay(
        SemanticLayerData Data,
        string Fingerprint);

    private sealed record SemanticSourceObservation(string Fingerprint);

    private sealed record VerifiedSourceObservation(
        string Token,
        string ScopeIdentity,
        string SourceFingerprint,
        long AccessSequence);

    private sealed record SemanticIndexedLayer(
        SemanticLayerData Data,
        SemanticSourceSnapshotDto Snapshot);

    private sealed record SemanticExternalRegistration(
        SemanticIndexedLayer Layer,
        ValidatedExternalRoot Root,
        string SourceFingerprint);

    private sealed record SemanticPage<T>(IReadOnlyList<T> Items, string? NextCursor);

    private sealed record SemanticOwnershipRow(
        IReadOnlyList<SemanticOwnershipNodeDto> Nodes,
        SemanticOwnershipEdgeDto Edge,
        IReadOnlyList<SemanticOwnershipConflictDto> Conflicts);

    private sealed class SemanticNumericStringComparer : IComparer<string>
    {
        public static SemanticNumericStringComparer Instance { get; } = new();

        public int Compare(string? left, string? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return -1;
            }

            if (right is null)
            {
                return 1;
            }

            if (long.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out var leftNumber)
                && long.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out var rightNumber))
            {
                var numeric = leftNumber.CompareTo(rightNumber);
                if (numeric != 0)
                {
                    return numeric;
                }
            }

            return StringComparer.Ordinal.Compare(left, right);
        }
    }
}
