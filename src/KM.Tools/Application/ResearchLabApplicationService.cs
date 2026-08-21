// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using KM.Api.Projects;
using KM.Api.Research;
using KM.Api.Semantics;
using KM.Core.Research;
using Microsoft.Win32.SafeHandles;

namespace KM.Tools.Application;

public enum ResearchLabFailureKind
{
    InvalidData,
    LimitExceeded,
    StaleRevision,
    InvalidCursor,
    SourceRejected,
    SourceExpired,
    ComparisonStale,
}

public sealed class ResearchLabValidationException : Exception
{
    public ResearchLabValidationException(
        string message,
        ResearchLabFailureKind researchFailureKind,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ResearchFailureKind = researchFailureKind;
    }

    public ResearchLabFailureKind ResearchFailureKind { get; }
}

public sealed class ResearchLabApplicationService : IDisposable
{
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint VolumeNameNt = 0x00000002;
    private const int MacOsGetPath = 50;
    private const int MaximumPhysicalPathBytes = 32_768;
    private const int MaximumCacheEntries = 8;
    private const string ProjectIdPrefix = "km1_";
    private readonly object syncRoot = new();
    private readonly SemanticExploreApplicationService semanticExplore;
    private readonly ResearchAnnotationApplicationService annotations;
    private readonly ReadOnlyResearchExtensionRegistry extensionRegistry;
    private readonly Dictionary<string, SourceRegistration> sources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ComparisonRegistration> comparisons = new(StringComparer.Ordinal);
    private readonly LinkedList<string> comparisonUsage = new();
    private readonly Timer cleanupTimer;
    private long comparisonCacheBytes;
    private bool disposed;

    public ResearchLabApplicationService(
        SemanticExploreApplicationService semanticExplore,
        ResearchAnnotationApplicationService annotations,
        ReadOnlyResearchExtensionRegistry? extensionRegistry = null)
    {
        this.semanticExplore = semanticExplore ?? throw new ArgumentNullException(nameof(semanticExplore));
        this.annotations = annotations ?? throw new ArgumentNullException(nameof(annotations));
        this.extensionRegistry = extensionRegistry ?? new ReadOnlyResearchExtensionRegistry();
        cleanupTimer = new Timer(
            _ => CleanupExpired(),
            null,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1));
    }

    public ReadResearchLabCapabilitiesResponse ReadCapabilities(
        ReadResearchLabCapabilitiesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();
        var semantic = Observe(request.Scope);
        return new ReadResearchLabCapabilitiesResponse(
            semantic.Revision,
            semantic.Snapshots,
            Capabilities(),
            extensionRegistry.Descriptors.Select(ToDto).ToArray(),
            Limits());
    }

    public OpenResearchSourceResponse OpenSource(OpenResearchSourceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();
        if (request.ReplaceSourceId is not null
            && !IsOpaqueId(request.ReplaceSourceId, "source-"))
        {
            throw Invalid("The research source replacement handle is invalid.");
        }

        var semantic = ObserveAndValidate(request.Scope, request.ExpectedRevision);
        var root = ValidateRoot(request.Scope, request.RootPath);
        var after = ObserveAndValidate(request.Scope, request.ExpectedRevision);
        EnsureSemanticObservationUnchanged(semantic, after);
        EnsureRootIdentityForRegistration(root);

        var now = DateTimeOffset.UtcNow;
        string sourceId;
        lock (syncRoot)
        {
            CleanupExpiredCore(now);
            var replacedSourceId = request.ReplaceSourceId;
            if (replacedSourceId is not null)
            {
                if (!sources.TryGetValue(replacedSourceId, out var replaced)
                    || replaced.Revision != semantic.Revision)
                {
                    throw SourceExpired(
                        "The research source replacement handle expired or belongs to another revision.");
                }
            }
            else if (sources.Count >= ResearchLabContract.MaximumRegistrations)
            {
                throw Limit("The research source registration limit has been reached.");
            }

            do
            {
                sourceId = "source-"
                    + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12));
            }
            while (sources.ContainsKey(sourceId));

            if (replacedSourceId is not null) RemoveSourceCore(replacedSourceId);

            sources.Add(sourceId, new SourceRegistration(
                sourceId,
                semantic.Revision,
                root,
                now.AddMinutes(ResearchLabContract.RegistrationLifetimeMinutes)));
        }

        return new OpenResearchSourceResponse(
            semantic.Revision,
            sourceId,
            now.AddMinutes(ResearchLabContract.RegistrationLifetimeMinutes));
    }

    public CloseResearchSourceResponse CloseSource(CloseResearchSourceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();
        if (!IsOpaqueId(request.SourceId, "source-"))
        {
            throw Invalid("The research source handle is invalid.");
        }

        ValidateCloseIdentity(request.Scope, request.ExpectedRevision);
        var closed = false;
        lock (syncRoot)
        {
            CleanupExpiredCore(DateTimeOffset.UtcNow);
            if (sources.TryGetValue(request.SourceId, out var source))
            {
                if (source.Revision != request.ExpectedRevision)
                {
                    throw SourceExpired(
                        "The research source handle belongs to another project revision.");
                }

                RemoveSourceCore(request.SourceId);
                closed = true;
            }
        }

        return new CloseResearchSourceResponse(request.ExpectedRevision, request.SourceId, closed);
    }

    public CompareResearchSourcesResponse Compare(CompareResearchSourcesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();
        ValidateCompareRequest(request);
        var before = ObserveAndValidate(request.Scope, request.ExpectedRevision);
        var normalizedSelected = NormalizeSelectedPaths(request.SelectedRelativePaths);

        ComparisonRegistration comparison;
        var offset = 0;
        if (request.Cursor is null)
        {
            var pair = ResolvePair(request.SourceIds, before.Revision);
            var budget = new ResearchReadBudget();
            var firstA = Capture(pair.A.Root, budget, retainFiles: false);
            var firstB = Capture(pair.B.Root, budget, retainFiles: true);
            var middleA = Capture(pair.A.Root, budget, retainFiles: true);
            EnsureSameSnapshot(firstA, middleA);
            EnsureDistinctRoots(pair.A.Root, pair.B.Root);

            var findings = BuildFindings(
                pair.A.Root,
                pair.B.Root,
                middleA,
                firstB,
                normalizedSelected,
                budget);
            var finalB = Capture(pair.B.Root, budget, retainFiles: false);
            EnsureSameSnapshot(firstB, finalB);
            var finalA = Capture(pair.A.Root, budget, retainFiles: false);
            EnsureSameSnapshot(middleA, finalA);
            var comparisonFingerprint = Hash(
                "research-comparison-v2",
                before.Revision.ProjectId,
                before.Revision.GameFamily.ToString(),
                before.Revision.Generation,
                before.Revision.Fingerprint,
                pair.A.SourceId,
                pair.B.SourceId,
                middleA.Fingerprint,
                middleA.FileCount.ToString(CultureInfo.InvariantCulture),
                middleA.DirectoryCount.ToString(CultureInfo.InvariantCulture),
                middleA.TotalBytes.ToString(CultureInfo.InvariantCulture),
                firstB.Fingerprint,
                firstB.FileCount.ToString(CultureInfo.InvariantCulture),
                firstB.DirectoryCount.ToString(CultureInfo.InvariantCulture),
                firstB.TotalBytes.ToString(CultureInfo.InvariantCulture));
            var comparisonId = "comparison-"
                + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12));
            var queryFingerprint = QueryFingerprint(
                comparisonId,
                comparisonFingerprint,
                before.Revision,
                request.SourceIds,
                normalizedSelected,
                request.Limit);
            var sizeBytes = EstimateComparisonSize(middleA, firstB, findings, normalizedSelected);
            if (sizeBytes > ResearchLabContract.MaximumResultCeilingBytes)
            {
                throw Limit("The research comparison exceeds the result size bound.");
            }

            var after = ObserveAndValidate(request.Scope, request.ExpectedRevision);
            EnsureSemanticObservationUnchanged(before, after);
            comparison = new ComparisonRegistration(
                comparisonId,
                comparisonFingerprint,
                queryFingerprint,
                before.Revision,
                request.SourceIds.ToArray(),
                normalizedSelected,
                middleA,
                firstB,
                findings,
                sizeBytes,
                DateTimeOffset.UtcNow);
            PublishComparison(comparison);
        }
        else
        {
            var decoded = DecodeCursor(request.Cursor);
            comparison = ResolveComparison(decoded.ComparisonId);
            ValidateContinuation(request, normalizedSelected, comparison, decoded);
            offset = decoded.Offset;
            ReobserveComparison(comparison, before.Revision, new ResearchReadBudget());
            var after = ObserveAndValidate(request.Scope, request.ExpectedRevision);
            EnsureSemanticObservationUnchanged(before, after);
        }

        if (offset < 0 || offset >= comparison.Findings.Count && offset != 0)
        {
            throw InvalidCursor("The research continuation cursor is outside the result set.");
        }

        var page = comparison.Findings.Skip(offset).Take(request.Limit).ToArray();
        var nextOffset = checked(offset + page.Length);
        var nextCursor = nextOffset < comparison.Findings.Count
            ? EncodeCursor(comparison, nextOffset)
            : null;
        return new CompareResearchSourcesResponse(
            comparison.Revision,
            comparison.QueryFingerprint,
            comparison.ComparisonId,
            comparison.ComparisonFingerprint,
            [ToDto(comparison.SourceA, comparison.SourceIds[0]),
                ToDto(comparison.SourceB, comparison.SourceIds[1])],
            page,
            SemanticProjectionCapability(),
            nextCursor);
    }

    public ReadResearchByteWindowResponse ReadByteWindow(ReadResearchByteWindowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();
        if (request.Length is <= 0 or > ResearchLabContract.MaximumByteWindowLength
            || request.Offset < 0 || request.Offset > ResearchLabContract.MaximumFileBytes)
        {
            throw Limit("A research byte window is outside the supported bounds.");
        }

        ValidateRelativePath(request.RelativePath);
        if (!IsSha256(request.ExpectedComparisonFingerprint))
        {
            throw Invalid("The expected research comparison fingerprint is invalid.");
        }

        var before = ObserveAndValidate(request.Scope, request.ExpectedRevision);
        var comparison = ResolveComparison(request.ComparisonId);
        if (comparison.Revision != before.Revision
            || !string.Equals(
                comparison.ComparisonFingerprint,
                request.ExpectedComparisonFingerprint,
                StringComparison.Ordinal))
        {
            throw SnapshotUnavailable("The reviewed research comparison is no longer current.");
        }

        var hasA = comparison.SourceA.Files.TryGetValue(request.RelativePath, out var fileA);
        var hasB = comparison.SourceB.Files.TryGetValue(request.RelativePath, out var fileB);
        if (!hasA && !hasB
            || !comparison.Findings.Any(finding => string.Equals(
                finding.RelativePath,
                request.RelativePath,
                StringComparison.Ordinal)))
        {
            throw Invalid("The selected research file does not belong to this comparison.");
        }

        var budget = new ResearchReadBudget();
        var pair = ResolvePair(comparison.SourceIds, before.Revision);
        var firstA = Capture(pair.A.Root, budget, retainFiles: false);
        var firstB = Capture(pair.B.Root, budget, retainFiles: false);
        var middleA = Capture(pair.A.Root, budget, retainFiles: false);
        EnsureSameSnapshot(firstA, middleA);
        EnsureSameSnapshot(comparison.SourceA, middleA);
        var windowA = ReadWindow(pair.A.Root, fileA, request.RelativePath, request.Offset, request.Length, budget);
        var windowB = ReadWindow(pair.B.Root, fileB, request.RelativePath, request.Offset, request.Length, budget);
        var finalB = Capture(pair.B.Root, budget, retainFiles: false);
        EnsureSameSnapshot(firstB, finalB);
        EnsureSameSnapshot(comparison.SourceB, finalB);
        var finalA = Capture(pair.A.Root, budget, retainFiles: false);
        EnsureSameSnapshot(middleA, finalA);
        var after = ObserveAndValidate(request.Scope, request.ExpectedRevision);
        EnsureSemanticObservationUnchanged(before, after);
        return new ReadResearchByteWindowResponse(
            before.Revision,
            comparison.ComparisonFingerprint,
            request.RelativePath,
            request.Offset,
            request.Length,
            windowA,
            windowB);
    }

    public ReadResearchAnnotationsResponse ReadAnnotations(ReadResearchAnnotationsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();
        var observation = ObserveAndValidate(request.Scope, request.ExpectedRevision);
        var response = annotations.ReadAsync(observation.Revision).GetAwaiter().GetResult();
        var after = ObserveAndValidate(request.Scope, request.ExpectedRevision);
        EnsureSemanticObservationUnchanged(observation, after);
        return response;
    }

    public MutateResearchAnnotationsResponse MutateAnnotations(
        MutateResearchAnnotationsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();
        var observation = ObserveAndValidate(request.Scope, request.ExpectedRevision);
        ValidateAnnotationMutationTarget(request.Mutation, request.Scope, observation);
        var finalObservation = ObserveAndValidate(request.Scope, request.ExpectedRevision);
        EnsureSemanticObservationUnchanged(observation, finalObservation);
        return annotations.MutateAsync(
                observation.Revision,
                request.ExpectedETag,
                request.Mutation)
            .GetAwaiter()
            .GetResult();
    }

    public void Dispose()
    {
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            cleanupTimer.Dispose();
            sources.Clear();
            comparisons.Clear();
            comparisonUsage.Clear();
            comparisonCacheBytes = 0;
        }
    }

    public static void ValidateRelativePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > ResearchLabContract.MaximumRelativePathLength
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || !value.IsNormalized(NormalizationForm.FormC)
            || value[0] is '/' or '\\'
            || value.Contains('\\', StringComparison.Ordinal)
            || value.Any(IsUnsafeUnicode))
        {
            throw Invalid("A research relative path is invalid.");
        }

        var segments = value.Split('/');
        if (segments.Any(segment => string.IsNullOrWhiteSpace(segment)
            || segment.Length > 255
            || segment is "." or ".."
            || segment.EndsWith('.')
            || segment.EndsWith(' ')
            || segment.Any(character => character is '"' or ':' or '<' or '>' or '|' or '?' or '*'
                || char.IsControl(character))
            || IsWindowsReservedDeviceAlias(segment)
            || string.Equals(segment, ".km", StringComparison.OrdinalIgnoreCase)))
        {
            throw Invalid("A research relative path is invalid.");
        }
    }

    private void ValidateAnnotationMutationTarget(
        ResearchAnnotationMutationDto mutation,
        SemanticExploreScopeDto scope,
        ReadSemanticCapabilitiesResponse observation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        if (mutation.Kind != ResearchAnnotationMutationKindDto.Upsert
            || mutation.Upsert is null
            || mutation.Upsert.AnnotationId is not null)
        {
            return;
        }

        var target = mutation.Upsert.Target
            ?? throw Invalid("A research annotation target is required.");
        if (target.Revision != observation.Revision)
        {
            throw Stale("The research annotation target is not bound to the current revision.");
        }

        if (target.Kind == ResearchAnnotationTargetKindDto.SemanticRecord)
        {
            if (target.SemanticSnapshot is null || target.SemanticRecord is null
                || !observation.Snapshots.Contains(target.SemanticSnapshot))
            {
                throw Stale("The semantic annotation target is no longer current.");
            }

            var entity = semanticExplore.ReadEntity(new ReadSemanticEntityRequest(
                scope,
                observation.Revision,
                target.SemanticRecord,
                target.SemanticSnapshot.Layer.Kind));
            if (entity.Entity.Snapshot != target.SemanticSnapshot)
            {
                throw Stale("The semantic annotation target changed after it was selected.");
            }

            return;
        }

        var comparisonFingerprint = target.Kind switch
        {
            ResearchAnnotationTargetKindDto.RelativeRange => target.RelativeRange?.ComparisonFingerprint,
            ResearchAnnotationTargetKindDto.Finding => target.Finding?.ComparisonFingerprint,
            _ => null,
        };
        if (comparisonFingerprint is null)
        {
            throw Invalid("The research annotation target is invalid.");
        }

        var comparison = ResolveComparisonByFingerprint(comparisonFingerprint, observation.Revision);
        ReobserveComparison(comparison, observation.Revision, new ResearchReadBudget());
        if (target.Kind == ResearchAnnotationTargetKindDto.Finding)
        {
            if (!comparison.Findings.Any(finding => string.Equals(
                    finding.FindingId,
                    target.Finding!.FindingId,
                    StringComparison.Ordinal)
                && string.Equals(
                    finding.RelativePath,
                    target.Finding.RelativePath,
                    StringComparison.Ordinal)))
            {
                throw Stale("The research finding annotation target is unavailable.");
            }

            return;
        }

        var range = target.RelativeRange!;
        ValidateRelativePath(range.RelativePath);
        var maximumLength = Math.Max(
            comparison.SourceA.Files.TryGetValue(range.RelativePath, out var a) ? a.Length : 0,
            comparison.SourceB.Files.TryGetValue(range.RelativePath, out var b) ? b.Length : 0);
        if (range.Offset < 0 || range.Length <= 0
            || range.Offset > maximumLength
            || range.Length > maximumLength - range.Offset)
        {
            throw Invalid("The research range annotation target is outside its reviewed file.");
        }
    }

    private static IReadOnlyList<ResearchCapabilityDto> Capabilities() =>
    [
        new(ResearchFeatureDto.SourceComparison, true, SemanticCoverageStateDto.Complete,
            SemanticConfidenceDto.Verified, null),
        new(ResearchFeatureDto.ByteWindows, true, SemanticCoverageStateDto.Complete,
            SemanticConfidenceDto.Verified, null),
        SemanticProjectionCapability(),
        new(ResearchFeatureDto.Annotations, true, SemanticCoverageStateDto.Partial,
            SemanticConfidenceDto.Verified, "comparison-target-creation-only"),
        new(ResearchFeatureDto.OwnershipEvidence, false, SemanticCoverageStateDto.Unavailable,
            SemanticConfidenceDto.Unknown, "opaque-file-ownership-provider-unavailable"),
        new(ResearchFeatureDto.ReadOnlyExtensions, true, SemanticCoverageStateDto.Partial,
            SemanticConfidenceDto.Verified, "host-registered-descriptors-only"),
        new(ResearchFeatureDto.WritableExtensions, false, SemanticCoverageStateDto.Unavailable,
            SemanticConfidenceDto.Unknown, "writable-extensions-not-supported"),
    ];

    private static ResearchCapabilityDto SemanticProjectionCapability() => new(
        ResearchFeatureDto.SemanticProjection,
        false,
        SemanticCoverageStateDto.Unavailable,
        SemanticConfidenceDto.Unknown,
        "selected-dump-semantic-provider-unavailable");

    private static ResearchLimitsDto Limits() => new(
        ResearchLabContract.MaximumRegistrations,
        ResearchLabContract.RequiredComparisonSources,
        ResearchLabContract.RegistrationLifetimeMinutes,
        ResearchLabContract.MaximumFileBytes,
        ResearchLabContract.MaximumAggregateBytes,
        ResearchLabContract.MaximumEntries,
        ResearchLabContract.MaximumDirectories,
        ResearchLabContract.MaximumTraversalDepth,
        ResearchLabContract.MaximumSelectedFiles,
        ResearchLabContract.MaximumRangesPerFile,
        ResearchLabContract.MaximumAggregateRanges,
        ResearchLabContract.MaximumPageSize,
        ResearchLabContract.MaximumCursorLength,
        ResearchLabContract.MaximumByteWindowLength,
        ResearchLabContract.MaximumResultCacheBytes);

    private static ResearchExtensionDescriptorDto ToDto(
        ReadOnlyResearchExtensionDescriptor descriptor) => new(
        descriptor.ExtensionId,
        descriptor.Kind == ReadOnlyResearchExtensionKind.HostRegistered
            ? ResearchExtensionKindDto.HostRegistered
            : ResearchExtensionKindDto.DeclarativeData,
        descriptor.SchemaVersion,
        descriptor.Features.Select(ParseFeature).ToArray(),
        descriptor.GameFamilies.Select(ParseGameFamily).ToArray(),
        descriptor.Coverage switch
        {
            ReadOnlyResearchExtensionCoverage.Complete => SemanticCoverageStateDto.Complete,
            ReadOnlyResearchExtensionCoverage.Partial => SemanticCoverageStateDto.Partial,
            _ => SemanticCoverageStateDto.Unavailable,
        },
        descriptor.Coverage == ReadOnlyResearchExtensionCoverage.Unavailable
            ? SemanticConfidenceDto.Unknown
            : SemanticConfidenceDto.Verified,
        descriptor.ReasonCode);

    private static ResearchFeatureDto ParseFeature(string feature) => feature switch
    {
        "sourceComparison" => ResearchFeatureDto.SourceComparison,
        "byteWindows" => ResearchFeatureDto.ByteWindows,
        "semanticProjection" => ResearchFeatureDto.SemanticProjection,
        "ownershipEvidence" => ResearchFeatureDto.OwnershipEvidence,
        _ => throw new InvalidOperationException("A registered research feature is unsupported."),
    };

    private static SemanticGameFamilyDto ParseGameFamily(string family) => family switch
    {
        "swordShield" => SemanticGameFamilyDto.SwordShield,
        "scarletViolet" => SemanticGameFamilyDto.ScarletViolet,
        "legendsZA" => SemanticGameFamilyDto.LegendsZA,
        _ => throw new InvalidOperationException("A registered research game family is unsupported."),
    };

    private ReadSemanticCapabilitiesResponse Observe(SemanticExploreScopeDto scope)
    {
        try
        {
            return semanticExplore.ReadCapabilities(new ReadSemanticCapabilitiesRequest(scope));
        }
        catch (SemanticExploreValidationException)
        {
            throw;
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            throw new SemanticExploreValidationException(
                "The research project revision could not be observed safely.",
                SemanticExploreFailureKind.InvalidData,
                exception);
        }
    }

    private ReadSemanticCapabilitiesResponse ObserveAndValidate(
        SemanticExploreScopeDto scope,
        SemanticProjectRevisionDto expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var observation = Observe(scope);
        if (observation.Revision != expected)
        {
            throw Stale("The research project revision changed after review.");
        }

        return observation;
    }

    private static void EnsureSemanticObservationUnchanged(
        ReadSemanticCapabilitiesResponse before,
        ReadSemanticCapabilitiesResponse after)
    {
        if (before.Revision != after.Revision
            || !before.Snapshots.SequenceEqual(after.Snapshots))
        {
            throw Stale("The research project sources changed while they were observed.");
        }
    }

    private static void ValidateCompareRequest(CompareResearchSourcesRequest request)
    {
        if (request.SourceIds is null
            || request.SourceIds.Count != ResearchLabContract.RequiredComparisonSources
            || request.SourceIds.Any(id => !IsOpaqueId(id, "source-"))
            || request.SourceIds.Distinct(StringComparer.Ordinal).Count()
                != ResearchLabContract.RequiredComparisonSources)
        {
            throw Invalid("A research comparison requires exactly two distinct source handles.");
        }

        if (request.SelectedRelativePaths is null
            || request.SelectedRelativePaths.Count > ResearchLabContract.MaximumSelectedFiles)
        {
            throw Limit("Too many research files were selected for detailed comparison.");
        }

        if (request.Limit is <= 0 or > ResearchLabContract.MaximumPageSize)
        {
            throw Limit("A research result page exceeds the supported bound.");
        }

        if (request.Cursor is { Length: > ResearchLabContract.MaximumCursorLength }
            || request.Cursor?.Any(IsUnsafeUnicode) is true)
        {
            throw InvalidCursor("The research continuation cursor is invalid.");
        }
    }

    private static void ValidateCloseIdentity(
        SemanticExploreScopeDto scope,
        SemanticProjectRevisionDto revision)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(revision);
        var scopeGameFamily = scope.Paths?.SelectedGame switch
        {
            ProjectGameDto.Sword or ProjectGameDto.Shield => SemanticGameFamilyDto.SwordShield,
            ProjectGameDto.Scarlet or ProjectGameDto.Violet => SemanticGameFamilyDto.ScarletViolet,
            ProjectGameDto.ZA => SemanticGameFamilyDto.LegendsZA,
            _ => (SemanticGameFamilyDto?)null,
        };
        if (scope.Paths is null
            || scope.ProjectId is null
            || scope.ProjectId.Length != ProjectIdPrefix.Length + 64
            || !scope.ProjectId.StartsWith(ProjectIdPrefix, StringComparison.Ordinal)
            || scope.ProjectId.AsSpan(ProjectIdPrefix.Length).ContainsAnyExcept("0123456789abcdef")
            || !string.Equals(scope.ProjectId, revision.ProjectId, StringComparison.Ordinal)
            || !Enum.IsDefined(revision.GameFamily)
            || scopeGameFamily != revision.GameFamily
            || !long.TryParse(
                revision.Generation,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var generation)
            || generation < 0
            || !string.Equals(
                revision.Generation,
                generation.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal)
            || !IsSha256(revision.Fingerprint))
        {
            throw Invalid("The research source close identity is invalid.");
        }
    }

    private static string[] NormalizeSelectedPaths(IReadOnlyList<string> values)
    {
        foreach (var value in values)
        {
            ValidateRelativePath(value);
        }

        var normalized = values.Order(StringComparer.Ordinal).ToArray();
        if (normalized.Select(PortableCaseIdentity)
            .Distinct(StringComparer.Ordinal)
            .Count() != values.Count)
        {
            throw Invalid("The detailed research file selection contains duplicates.");
        }

        return normalized;
    }

    private SourcePair ResolvePair(
        IReadOnlyList<string> sourceIds,
        SemanticProjectRevisionDto revision)
    {
        lock (syncRoot)
        {
            ThrowIfDisposedCore();
            var now = DateTimeOffset.UtcNow;
            CleanupExpiredCore(now);
            if (!sources.TryGetValue(sourceIds[0], out var sourceA)
                || !sources.TryGetValue(sourceIds[1], out var sourceB)
                || sourceA.Revision != revision
                || sourceB.Revision != revision)
            {
                throw SourceExpired("A research source handle expired or is not bound to this revision.");
            }

            EnsureDistinctRoots(sourceA.Root, sourceB.Root);
            return new SourcePair(sourceA, sourceB);
        }
    }

    private SourcePair ReobserveComparison(
        ComparisonRegistration comparison,
        SemanticProjectRevisionDto revision,
        ResearchReadBudget budget)
    {
        var pair = ResolvePair(comparison.SourceIds, revision);
        var firstA = Capture(pair.A.Root, budget, retainFiles: false);
        var firstB = Capture(pair.B.Root, budget, retainFiles: false);
        var middleA = Capture(pair.A.Root, budget, retainFiles: false);
        EnsureSameSnapshot(firstA, middleA);
        var finalB = Capture(pair.B.Root, budget, retainFiles: false);
        EnsureSameSnapshot(firstB, finalB);
        var finalA = Capture(pair.A.Root, budget, retainFiles: false);
        EnsureSameSnapshot(middleA, finalA);
        EnsureSameSnapshot(comparison.SourceA, finalA);
        EnsureSameSnapshot(comparison.SourceB, finalB);

        return pair;
    }

    private void PublishComparison(ComparisonRegistration comparison)
    {
        lock (syncRoot)
        {
            ThrowIfDisposedCore();
            CleanupExpiredCore(DateTimeOffset.UtcNow);
            if (!sources.ContainsKey(comparison.SourceIds[0])
                || !sources.ContainsKey(comparison.SourceIds[1]))
            {
                throw SourceExpired("A research source expired before the result was published.");
            }

            while (comparisons.Count >= MaximumCacheEntries
                || comparisonCacheBytes > ResearchLabContract.MaximumResultCacheBytes - comparison.SizeBytes)
            {
                EvictComparisonCore();
            }

            comparisons.Add(comparison.ComparisonId, comparison);
            comparisonUsage.AddFirst(comparison.ComparisonId);
            comparisonCacheBytes = checked(comparisonCacheBytes + comparison.SizeBytes);
        }
    }

    private ComparisonRegistration ResolveComparison(string comparisonId)
    {
        if (!IsOpaqueId(comparisonId, "comparison-"))
        {
            throw SnapshotUnavailable("The research comparison handle is invalid.");
        }

        lock (syncRoot)
        {
            ThrowIfDisposedCore();
            CleanupExpiredCore(DateTimeOffset.UtcNow);
            if (!comparisons.TryGetValue(comparisonId, out var comparison))
            {
                throw SnapshotUnavailable("The research comparison is no longer available.");
            }

            TouchComparisonCore(comparisonId);
            return comparison;
        }
    }

    private ComparisonRegistration ResolveComparisonByFingerprint(
        string fingerprint,
        SemanticProjectRevisionDto revision)
    {
        if (!IsSha256(fingerprint))
        {
            throw Invalid("The research comparison fingerprint is invalid.");
        }

        lock (syncRoot)
        {
            ThrowIfDisposedCore();
            CleanupExpiredCore(DateTimeOffset.UtcNow);
            var matches = comparisons.Values.Where(comparison =>
                    comparison.Revision == revision
                    && string.Equals(
                        comparison.ComparisonFingerprint,
                        fingerprint,
                        StringComparison.Ordinal))
                .OrderByDescending(comparison => comparison.CreatedAtUtc)
                .ToArray();
            if (matches.Length == 0)
            {
                throw SnapshotUnavailable("The reviewed research comparison is no longer available.");
            }

            TouchComparisonCore(matches[0].ComparisonId);
            return matches[0];
        }
    }

    private static void ValidateContinuation(
        CompareResearchSourcesRequest request,
        IReadOnlyList<string> selected,
        ComparisonRegistration comparison,
        CursorState cursor)
    {
        if (comparison.Revision != request.ExpectedRevision
            || !comparison.SourceIds.SequenceEqual(request.SourceIds)
            || !comparison.SelectedPaths.SequenceEqual(selected)
            || !string.Equals(cursor.QueryFingerprint, comparison.QueryFingerprint, StringComparison.Ordinal)
            || !string.Equals(cursor.ComparisonId, comparison.ComparisonId, StringComparison.Ordinal)
            || !string.Equals(
                comparison.QueryFingerprint,
                QueryFingerprint(
                    comparison.ComparisonId,
                    comparison.ComparisonFingerprint,
                    comparison.Revision,
                    request.SourceIds,
                    selected,
                    request.Limit),
                StringComparison.Ordinal))
        {
            throw InvalidCursor("The research continuation cursor does not match this query.");
        }
    }

    private static ResearchRootSnapshot Capture(
        ValidatedRoot root,
        ResearchReadBudget budget,
        bool retainFiles)
    {
        EnsureRootIdentity(root);
        var discovered = DiscoverFiles(root, budget);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHash(hash, "research-source-v1");
        long totalBytes = 0;
        var retained = retainFiles
            ? new Dictionary<string, ResearchFileManifest>(StringComparer.Ordinal)
            : null;
        foreach (var file in discovered.Files.OrderBy(file => file.RelativePath, StringComparer.Ordinal))
        {
            var contentHash = HashFile(root, file, budget);
            totalBytes = checked(totalBytes + file.Length);
            AppendHash(hash, file.RelativePath);
            AppendHash(hash, file.Length.ToString(CultureInfo.InvariantCulture));
            AppendHash(hash, contentHash);
            retained?.Add(file.RelativePath, new ResearchFileManifest(
                file.RelativePath,
                file.Length,
                contentHash));
        }

        EnsureRootIdentity(root);
        var fingerprint = Convert.ToHexStringLower(hash.GetHashAndReset());
        return new ResearchRootSnapshot(
            fingerprint,
            discovered.Files.Count,
            discovered.DirectoryCount,
            totalBytes,
            retained ?? new Dictionary<string, ResearchFileManifest>(StringComparer.Ordinal));
    }

    private static DiscoveredFiles DiscoverFiles(ValidatedRoot root, ResearchReadBudget budget)
    {
        var pending = new Stack<(DirectoryInfo Directory, int Depth)>();
        pending.Push((new DirectoryInfo(root.Path), 0));
        var files = new List<DiscoveredFile>();
        var relativeIds = new HashSet<string>(StringComparer.Ordinal);
        var platformIds = new HashSet<string>(StringComparer.Ordinal);
        var entryCount = 0;
        var directoryCount = 0;
        long projectedBytes = 0;
        while (pending.Count > 0)
        {
            var (directory, depth) = pending.Pop();
            if (depth > ResearchLabContract.MaximumTraversalDepth)
            {
                throw Limit("The research source exceeds the directory traversal bound.");
            }

            directoryCount++;
            budget.ChargeDirectory();

            IEnumerable<FileSystemInfo> entries;
            try
            {
                directory.Refresh();
                EnsureUnlinked(directory);
                entries = directory.EnumerateFileSystemInfos();
            }
            catch (SemanticExploreValidationException)
            {
                throw;
            }
            catch (Exception exception) when (IsFileSystemFailure(exception))
            {
                throw ExternalRejected("A research source could not be enumerated safely.", exception);
            }

            try
            {
                foreach (var entry in entries)
                {
                    entryCount++;
                    budget.ChargeEntry();

                    entry.Refresh();
                    EnsureUnlinked(entry);
                    if (string.Equals(entry.Name, ".km", StringComparison.OrdinalIgnoreCase))
                    {
                        throw ExternalRejected("A research source contains a reserved metadata namespace.");
                    }

                    if ((entry.Attributes & FileAttributes.Directory) != 0)
                    {
                        pending.Push(((DirectoryInfo)entry, checked(depth + 1)));
                        continue;
                    }

                    var file = (FileInfo)entry;
                    if (file.Length < 0 || file.Length > ResearchLabContract.MaximumFileBytes)
                    {
                        throw Limit("A research source file exceeds the per-file byte bound.");
                    }

                    var relative = NormalizeDiscoveredPath(
                        Path.GetRelativePath(root.Path, file.FullName));
                    if (!relativeIds.Add(relative)
                        || !platformIds.Add(PortableCaseIdentity(relative)))
                    {
                        throw ExternalRejected("A research source contains ambiguous relative file identities.");
                    }

                    projectedBytes = checked(projectedBytes
                        + StringStorageBytes(relative) + 256L);
                    if (projectedBytes > ResearchLabContract.MaximumResultCeilingBytes)
                    {
                        throw Limit("The research source catalog exceeds the bounded projection size.");
                    }

                    files.Add(new DiscoveredFile(
                        relative,
                        file.Length,
                        file.LastWriteTimeUtc));
                }
            }
            catch (SemanticExploreValidationException)
            {
                throw;
            }
            catch (Exception exception) when (IsFileSystemFailure(exception))
            {
                throw ExternalRejected("A research source entry could not be inspected safely.", exception);
            }
        }

        return new DiscoveredFiles(files, directoryCount);
    }

    private static string HashFile(
        ValidatedRoot root,
        DiscoveredFile file,
        ResearchReadBudget budget)
    {
        budget.ChargeBytes(file.Length);
        var fullPath = ResolveRelativeFile(root, file.RelativePath);
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.SequentialScan);
            EnsureOpenedFileContained(root, stream);
            if (stream.Length != file.Length)
            {
                throw SnapshotUnavailable("A research source file changed while it was opened.");
            }

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long readTotal = 0;
            while (readTotal < file.Length)
            {
                var read = stream.Read(
                    buffer,
                    0,
                    checked((int)Math.Min(buffer.Length, file.Length - readTotal)));
                if (read == 0)
                {
                    break;
                }

                readTotal = checked(readTotal + read);
                hash.AppendData(buffer, 0, read);
            }

            var trailingByte = stream.ReadByte();
            if (trailingByte != -1) budget.ChargeBytes(1);
            if (readTotal != file.Length || trailingByte != -1)
            {
                throw SnapshotUnavailable("A research source file changed while it was read.");
            }

            var refreshed = new FileInfo(fullPath);
            refreshed.Refresh();
            EnsureUnlinked(refreshed);
            if (!refreshed.Exists || refreshed.Length != file.Length
                || refreshed.LastWriteTimeUtc != file.LastWriteTimeUtc)
            {
                throw SnapshotUnavailable("A research source file changed while it was read.");
            }

            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }
        catch (SemanticExploreValidationException)
        {
            throw;
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            throw ExternalRejected("A research source file could not be read safely.", exception);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static IReadOnlyList<ResearchFileFindingDto> BuildFindings(
        ValidatedRoot rootA,
        ValidatedRoot rootB,
        ResearchRootSnapshot sourceA,
        ResearchRootSnapshot sourceB,
        IReadOnlyCollection<string> selected,
        ResearchReadBudget budget)
    {
        var selectedSet = selected.ToHashSet(StringComparer.Ordinal);
        if (sourceA.Files.Keys.Concat(sourceB.Files.Keys)
            .GroupBy(PortableCaseIdentity, StringComparer.Ordinal)
            .Any(group => group.Distinct(StringComparer.Ordinal).Count() > 1))
        {
            throw ExternalRejected(
                "The research sources contain ambiguous portable relative file identities.");
        }

        var keys = sourceA.Files.Keys.Concat(sourceB.Files.Keys)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var results = new List<ResearchFileFindingDto>();
        var aggregateRanges = 0;
        long projectedBytes = 0;
        foreach (var key in keys)
        {
            var hasA = sourceA.Files.TryGetValue(key, out var a);
            var hasB = sourceB.Files.TryGetValue(key, out var b);
            if (hasA && hasB && string.Equals(a!.ContentSha256, b!.ContentSha256, StringComparison.Ordinal)
                && a.Length == b.Length)
            {
                continue;
            }

            var kind = !hasA
                ? ResearchFileDifferenceKindDto.Added
                : !hasB
                    ? ResearchFileDifferenceKindDto.Removed
                    : ResearchFileDifferenceKindDto.Changed;
            IReadOnlyList<ResearchByteRangeDto> ranges = [];
            var rangeCoverage = ResearchRangeCoverageDto.NotRequested;
            if (selectedSet.Contains(key))
            {
                var calculated = CalculateRanges(rootA, rootB, key, a, b, budget);
                var remaining = ResearchLabContract.MaximumAggregateRanges - aggregateRanges;
                if (remaining < calculated.Ranges.Count)
                {
                    ranges = calculated.Ranges.Take(Math.Max(remaining, 0)).ToArray();
                    rangeCoverage = ResearchRangeCoverageDto.Truncated;
                }
                else
                {
                    ranges = calculated.Ranges;
                    rangeCoverage = calculated.Truncated
                        ? ResearchRangeCoverageDto.Truncated
                        : ResearchRangeCoverageDto.Complete;
                }

                aggregateRanges = checked(aggregateRanges + ranges.Count);
            }

            projectedBytes = checked(projectedBytes + StringStorageBytes(key) + 768L
                + ranges.Count * 32L);
            if (projectedBytes > ResearchLabContract.MaximumResultCeilingBytes)
            {
                throw Limit("The research comparison result exceeds the bounded projection size.");
            }

            var findingId = "finding-" + Hash(
                "research-finding-v1",
                key,
                kind.ToString(),
                a?.ContentSha256 ?? "missing",
                b?.ContentSha256 ?? "missing")[..24];
            results.Add(new ResearchFileFindingDto(
                findingId,
                key,
                kind,
                ToSide(a),
                ToSide(b),
                ranges,
                rangeCoverage,
                new ResearchOwnershipEvidenceDto(
                    SemanticCoverageStateDto.Unavailable,
                    SemanticConfidenceDto.Unknown,
                    null,
                    "opaque-file-ownership-provider-unavailable")));
        }

        var resultPaths = results.Select(result => result.RelativePath)
            .ToHashSet(StringComparer.Ordinal);
        if (selected.Any(path => !resultPaths.Contains(path)))
        {
            throw Invalid("A selected research file is not a differing file in this comparison.");
        }

        return results;
    }

    private static RangeResult CalculateRanges(
        ValidatedRoot rootA,
        ValidatedRoot rootB,
        string relativePath,
        ResearchFileManifest? sourceA,
        ResearchFileManifest? sourceB,
        ResearchReadBudget budget)
    {
        if (sourceA is null || sourceB is null)
        {
            var length = checked((int)(sourceA?.Length ?? sourceB?.Length ?? 0));
            return new RangeResult(length == 0 ? [] : [new ResearchByteRangeDto(0, length)], false);
        }

        var bytesA = ReadAndVerify(
            rootA,
            sourceA,
            ResolveRelativeFile(rootA, relativePath),
            budget);
        var bytesB = ReadAndVerify(
            rootB,
            sourceB,
            ResolveRelativeFile(rootB, relativePath),
            budget);
        var ranges = new List<ResearchByteRangeDto>();
        var maximum = Math.Max(bytesA.Length, bytesB.Length);
        var start = -1;
        var truncated = false;
        for (var index = 0; index < maximum; index++)
        {
            var different = index >= bytesA.Length || index >= bytesB.Length
                || bytesA[index] != bytesB[index];
            if (different && start < 0)
            {
                start = index;
            }
            else if (!different && start >= 0)
            {
                if (ranges.Count >= ResearchLabContract.MaximumRangesPerFile)
                {
                    truncated = true;
                    break;
                }

                ranges.Add(new ResearchByteRangeDto(start, index - start));
                start = -1;
            }
        }

        if (!truncated && start >= 0)
        {
            if (ranges.Count >= ResearchLabContract.MaximumRangesPerFile)
            {
                truncated = true;
            }
            else
            {
                ranges.Add(new ResearchByteRangeDto(start, maximum - start));
            }
        }

        return new RangeResult(ranges, truncated);
    }

    private static byte[] ReadAndVerify(
        ValidatedRoot root,
        ResearchFileManifest file,
        string fullPath,
        ResearchReadBudget budget)
    {
        budget.ChargeBytes(file.Length);
        if (file.Length > int.MaxValue)
        {
            throw Limit("A research source file is too large to inspect.");
        }

        try
        {
            var bytes = new byte[checked((int)file.Length)];
            using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.SequentialScan);
            EnsureOpenedFileContained(root, stream);
            stream.ReadExactly(bytes);
            var trailingByte = stream.ReadByte();
            if (trailingByte != -1) budget.ChargeBytes(1);
            if (trailingByte != -1 || !string.Equals(
                    Convert.ToHexStringLower(SHA256.HashData(bytes)),
                    file.ContentSha256,
                    StringComparison.Ordinal))
            {
                throw SnapshotUnavailable("A research source file changed during range analysis.");
            }

            return bytes;
        }
        catch (ResearchLabValidationException)
        {
            throw;
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            throw SnapshotUnavailable("A research source file could not be re-read safely.", exception);
        }
    }

    private static ResearchByteWindowSideDto ReadWindow(
        ValidatedRoot root,
        ResearchFileManifest? manifest,
        string relativePath,
        long offset,
        int length,
        ResearchReadBudget budget)
    {
        if (manifest is null)
        {
            return new ResearchByteWindowSideDto(false, null, null, null);
        }

        var fullPath = ResolveRelativeFile(root, relativePath);
        budget.ChargeBytes(manifest.Length);
        var available = offset >= manifest.Length ? 0L : manifest.Length - offset;
        var count = checked((int)Math.Min(length, available));
        var window = new byte[count];
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.SequentialScan);
            EnsureOpenedFileContained(root, stream);
            if (stream.Length != manifest.Length)
            {
                throw SnapshotUnavailable("A research source file changed before its byte window was read.");
            }

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long position = 0;
            var copied = 0;
            while (position < manifest.Length)
            {
                var read = stream.Read(
                    buffer,
                    0,
                    checked((int)Math.Min(buffer.Length, manifest.Length - position)));
                if (read == 0) break;
                hash.AppendData(buffer, 0, read);
                var chunkStart = position;
                var chunkEnd = checked(position + read);
                var copyStart = Math.Max(offset, chunkStart);
                var copyEnd = Math.Min(checked(offset + count), chunkEnd);
                if (copyEnd > copyStart)
                {
                    var sourceOffset = checked((int)(copyStart - chunkStart));
                    var copyLength = checked((int)(copyEnd - copyStart));
                    buffer.AsSpan(sourceOffset, copyLength).CopyTo(window.AsSpan(copied));
                    copied += copyLength;
                }

                position = chunkEnd;
            }

            var trailingByte = stream.ReadByte();
            if (trailingByte != -1) budget.ChargeBytes(1);
            if (position != manifest.Length || trailingByte != -1 || copied != count
                || !string.Equals(
                    Convert.ToHexStringLower(hash.GetHashAndReset()),
                    manifest.ContentSha256,
                    StringComparison.Ordinal))
            {
                throw SnapshotUnavailable("A research source file changed while its byte window was read.");
            }
        }
        catch (SemanticExploreValidationException)
        {
            throw;
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            throw SnapshotUnavailable("A research byte window could not be read safely.", exception);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return new ResearchByteWindowSideDto(
            true,
            manifest.Length,
            Convert.ToBase64String(window),
            Convert.ToHexStringLower(SHA256.HashData(window)));
    }

    private static string ResolveRelativeFile(ValidatedRoot root, string relativePath)
    {
        ValidateRelativePath(relativePath);
        try
        {
            var candidate = Path.GetFullPath(Path.Combine(
                root.Path,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsSameOrDescendant(candidate, root.Path) || !File.Exists(candidate))
            {
                throw SnapshotUnavailable("The selected research file is unavailable.");
            }

            var info = new FileInfo(candidate);
            info.Refresh();
            EnsureUnlinked(info);
            return candidate;
        }
        catch (ResearchLabValidationException)
        {
            throw;
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            throw SnapshotUnavailable("The selected research file could not be resolved safely.", exception);
        }
    }

    private static string NormalizeDiscoveredPath(string path)
    {
        var normalized = path.Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/')
            .Normalize(NormalizationForm.FormC);
        ValidateRelativePath(normalized);
        return normalized;
    }

    private static void EnsureSameSnapshot(ResearchRootSnapshot expected, ResearchRootSnapshot actual)
    {
        if (!string.Equals(expected.Fingerprint, actual.Fingerprint, StringComparison.Ordinal)
            || expected.FileCount != actual.FileCount
            || expected.DirectoryCount != actual.DirectoryCount
            || expected.TotalBytes != actual.TotalBytes)
        {
            throw SnapshotUnavailable("A research source changed while it was observed.");
        }
    }

    private static ResearchSourceSnapshotDto ToDto(ResearchRootSnapshot snapshot, string sourceId) =>
        new(sourceId, snapshot.Fingerprint, snapshot.FileCount, snapshot.DirectoryCount, snapshot.TotalBytes);

    private static ResearchFileSideDto ToSide(ResearchFileManifest? file) => file is null
        ? new ResearchFileSideDto(false, null, null)
        : new ResearchFileSideDto(true, file.Length, file.ContentSha256);

    private static long EstimateComparisonSize(
        ResearchRootSnapshot sourceA,
        ResearchRootSnapshot sourceB,
        IReadOnlyList<ResearchFileFindingDto> findings,
        IReadOnlyList<string> selected)
    {
        long size = 2_048;
        foreach (var file in sourceA.Files.Values.Concat(sourceB.Files.Values))
        {
            size = checked(size + StringStorageBytes(file.RelativePath) + 384L);
        }

        foreach (var finding in findings)
        {
            size = checked(size + StringStorageBytes(finding.RelativePath) + 896L
                + finding.Ranges.Count * 32L);
        }

        foreach (var path in selected)
        {
            size = checked(size + StringStorageBytes(path) + 96L);
        }

        return size;
    }

    private static long StringStorageBytes(string value) => Math.Max(
        Encoding.UTF8.GetByteCount(value),
        checked(value.Length * 2L));

    private static string PortableCaseIdentity(string value) => string.Create(
        value.Length,
        value,
        static (destination, source) =>
        {
            for (var index = 0; index < source.Length; index++)
            {
                var character = source[index];
                destination[index] = character is >= 'A' and <= 'Z'
                    ? (char)(character + ('a' - 'A'))
                    : character;
            }
        });

    private static string QueryFingerprint(
        string comparisonId,
        string comparisonFingerprint,
        SemanticProjectRevisionDto revision,
        IReadOnlyList<string> sourceIds,
        IReadOnlyList<string> selectedPaths,
        int limit) => Hash(
            "research-query-v1",
            comparisonId,
            comparisonFingerprint,
            revision.ProjectId,
            revision.GameFamily.ToString(),
            revision.Generation,
            revision.Fingerprint,
            sourceIds[0],
            sourceIds[1],
            limit.ToString(CultureInfo.InvariantCulture),
            string.Join('\n', selectedPaths));

    private static CursorState DecodeCursor(string cursor)
    {
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
            var fields = Encoding.UTF8.GetString(Convert.FromBase64String(padded)).Split('\n');
            if (fields is not ["1", var comparisonId, var fingerprint, var offsetText]
                || !IsOpaqueId(comparisonId, "comparison-")
                || !IsSha256(fingerprint)
                || !int.TryParse(offsetText, NumberStyles.None, CultureInfo.InvariantCulture, out var offset)
                || offset <= 0)
            {
                throw new FormatException();
            }

            return new CursorState(comparisonId, fingerprint, offset);
        }
        catch (Exception exception) when (exception is FormatException or DecoderFallbackException)
        {
            throw InvalidCursor("The research continuation cursor is invalid.", exception);
        }
    }

    private static string EncodeCursor(ComparisonRegistration comparison, int offset)
    {
        var bytes = Encoding.UTF8.GetBytes(
            $"1\n{comparison.ComparisonId}\n{comparison.QueryFingerprint}\n{offset.ToString(CultureInfo.InvariantCulture)}");
        var encoded = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        if (encoded.Length > ResearchLabContract.MaximumCursorLength)
        {
            throw Limit("The research continuation cursor exceeds its safe bound.");
        }

        return encoded;
    }

    private static ValidatedRoot ValidateRoot(SemanticExploreScopeDto scope, string path)
    {
        if (scope is null || scope.Paths is null
            || string.IsNullOrWhiteSpace(path)
            || path.Length > SemanticExploreContract.MaximumExternalRootLength
            || !string.Equals(path, path.Trim(), StringComparison.Ordinal)
            || !Path.IsPathFullyQualified(path)
            || path.Any(IsUnsafeUnicode))
        {
            throw ExternalRejected("The selected research source root is invalid.");
        }

        try
        {
            var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            if (!Path.IsPathFullyQualified(fullPath) || !Directory.Exists(fullPath))
            {
                throw ExternalRejected("The selected research source root is unavailable.");
            }

            var rootInfo = new DirectoryInfo(fullPath);
            rootInfo.Refresh();
            EnsureUnlinked(rootInfo);
            var identity = ResolvePhysicalExistingPath(fullPath);
            if (IsUnverifiableWindowsNetworkPath(fullPath, identity)
                || ContainsReservedMetadataSegment(fullPath)
                || ContainsReservedMetadataSegment(identity))
            {
                throw ExternalRejected("The selected research source root cannot be verified safely.");
            }

            var privatePaths = EnumeratePrivatePaths(scope)
                .Select(candidate => Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate)))
                .Distinct(OperatingSystem.IsWindows()
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal)
                .Select(candidate => new PhysicalPathIdentity(
                    candidate,
                    Directory.Exists(candidate) || File.Exists(candidate)
                        ? ResolvePhysicalExistingPath(candidate)
                        : null))
                .ToArray();
            if (privatePaths.Any(privatePath => IsUnverifiableWindowsNetworkPath(
                    privatePath.Path,
                    privatePath.Identity ?? privatePath.Path)
                || PathsOverlap(fullPath, privatePath.Path)
                || privatePath.Identity is not null && PathsOverlap(identity, privatePath.Identity)))
            {
                throw ExternalRejected("The selected research source overlaps a private project root.");
            }

            return new ValidatedRoot(fullPath, identity, privatePaths);
        }
        catch (SemanticExploreValidationException)
        {
            throw;
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            throw ExternalRejected("The selected research source identity could not be verified.", exception);
        }
    }

    private static IEnumerable<string> EnumeratePrivatePaths(SemanticExploreScopeDto scope)
    {
        var paths = scope.Paths;
        if (!string.IsNullOrWhiteSpace(paths.BaseRomFsPath)) yield return paths.BaseRomFsPath;
        if (!string.IsNullOrWhiteSpace(paths.BaseExeFsPath)) yield return paths.BaseExeFsPath;
        if (!string.IsNullOrWhiteSpace(paths.OutputRootPath)) yield return paths.OutputRootPath;
        if (!string.IsNullOrWhiteSpace(paths.SaveFilePath)) yield return paths.SaveFilePath;
        if (!string.IsNullOrWhiteSpace(paths.ScarletVioletSupportFolderPath))
            yield return paths.ScarletVioletSupportFolderPath;
        if (!string.IsNullOrWhiteSpace(paths.PokemonLegendsZASupportFolderPath))
            yield return paths.PokemonLegendsZASupportFolderPath;
        yield return GetPrivateAppDataRoot();
    }

    private static string GetPrivateAppDataRoot()
    {
        var root = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
        {
            throw ExternalRejected("The private application-data root could not be verified.");
        }

        return Path.Combine(root, "KM Editor");
    }

    private static void EnsureRootIdentity(ValidatedRoot root)
    {
        try
        {
            var info = new DirectoryInfo(root.Path);
            info.Refresh();
            EnsureUnlinked(info);
            var identity = ResolvePhysicalExistingPath(root.Path);
            if (!string.Equals(identity, root.Identity, PhysicalPathComparison)
                || root.PrivatePaths.Any(privatePath =>
                    PathsOverlap(root.Path, privatePath.Path)
                    || PrivatePathChangedOrOverlaps(privatePath, identity)))
            {
                throw SnapshotUnavailable("A research source physical identity changed.");
            }
        }
        catch (SemanticExploreValidationException)
        {
            throw;
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            throw SnapshotUnavailable("A research source identity could not be reverified.", exception);
        }
    }

    private static void EnsureRootIdentityForRegistration(ValidatedRoot root)
    {
        try
        {
            EnsureRootIdentity(root);
        }
        catch (ResearchLabValidationException exception) when (
            exception.ResearchFailureKind == ResearchLabFailureKind.ComparisonStale)
        {
            throw ExternalRejected(
                "The selected research source changed while it was registered.",
                exception);
        }
    }

    private static void EnsureDistinctRoots(ValidatedRoot a, ValidatedRoot b)
    {
        if (PathsOverlap(a.Identity, b.Identity))
        {
            throw ExternalRejected("Research comparison sources must be physically distinct roots.");
        }
    }

    private static bool PrivatePathChangedOrOverlaps(
        PhysicalPathIdentity privatePath,
        string rootIdentity)
    {
        if (!Directory.Exists(privatePath.Path) && !File.Exists(privatePath.Path))
        {
            return privatePath.Identity is not null;
        }

        var currentIdentity = ResolvePhysicalExistingPath(privatePath.Path);
        return privatePath.Identity is not null
            && !string.Equals(currentIdentity, privatePath.Identity, PhysicalPathComparison)
            || PathsOverlap(rootIdentity, currentIdentity);
    }

    private static void EnsureUnlinked(FileSystemInfo info)
    {
        if (!info.Exists || !string.IsNullOrEmpty(info.LinkTarget))
        {
            throw ExternalRejected("A research source contains an unsafe linked entry.");
        }
    }

    private static void EnsureOpenedFileContained(ValidatedRoot root, FileStream stream)
    {
        try
        {
            var identity = ResolveOpenedFileIdentity(stream.SafeFileHandle);
            if (!IsSameOrDescendant(identity, root.Identity)
                || ContainsReservedMetadataSegment(identity))
            {
                throw SnapshotUnavailable(
                    "A research source file resolved outside its approved physical root.");
            }
        }
        catch (ResearchLabValidationException)
        {
            throw;
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            throw SnapshotUnavailable(
                "A research source file identity could not be verified safely.",
                exception);
        }
    }

    private static string ResolveOpenedFileIdentity(SafeFileHandle handle)
    {
        if (handle.IsInvalid || handle.IsClosed)
        {
            throw new IOException("A research source file handle is unavailable.");
        }

        if (OperatingSystem.IsWindows())
        {
            return ResolveWindowsHandlePath(handle);
        }

        var descriptorValue = handle.DangerousGetHandle().ToInt64();
        if (descriptorValue is < 0 or > int.MaxValue)
        {
            throw new IOException("A research source file descriptor is invalid.");
        }

        var descriptor = checked((int)descriptorValue);
        if (OperatingSystem.IsLinux())
        {
            return ResolveLinuxHandlePath(descriptor);
        }

        if (OperatingSystem.IsMacOS())
        {
            return ResolveMacOsHandlePath(descriptor);
        }

        throw new PlatformNotSupportedException(
            "Opened research source identities are unsupported on this platform.");
    }

    private static string ResolveLinuxHandlePath(int descriptor)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(MaximumPhysicalPathBytes);
        try
        {
            var length = ReadLink(
                "/proc/self/fd/" + descriptor.ToString(CultureInfo.InvariantCulture),
                buffer,
                (nuint)MaximumPhysicalPathBytes);
            if (length <= 0 || length >= MaximumPhysicalPathBytes)
            {
                throw new IOException("A research source file descriptor could not be resolved.");
            }

            var path = Encoding.UTF8.GetString(buffer, 0, checked((int)length));
            return NormalizeOpenedPhysicalPath(path);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static string ResolveMacOsHandlePath(int descriptor)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(MaximumPhysicalPathBytes);
        try
        {
            Array.Clear(buffer, 0, buffer.Length);
            if (FcntlGetPath(descriptor, MacOsGetPath, buffer) != 0)
            {
                throw new IOException("A research source file descriptor could not be resolved.");
            }

            var length = Array.IndexOf(buffer, (byte)0);
            if (length <= 0)
            {
                throw new IOException("A research source file descriptor returned an invalid path.");
            }

            return NormalizeOpenedPhysicalPath(Encoding.UTF8.GetString(buffer, 0, length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static string NormalizeOpenedPhysicalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path.EndsWith(" (deleted)", StringComparison.Ordinal)
            || !Path.IsPathFullyQualified(path))
        {
            throw new IOException("A research source file descriptor returned an unsafe path.");
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static string ResolvePhysicalExistingPath(string path)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
        if (!Path.IsPathFullyQualified(fullPath)
            || !Directory.Exists(fullPath) && !File.Exists(fullPath))
        {
            throw new IOException("A research source path is unavailable.");
        }

        return OperatingSystem.IsWindows()
            ? ResolveWindowsFinalPath(fullPath)
            : ResolveExistingPathLinks(fullPath);
    }

    private static string ResolveExistingPathLinks(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new IOException("A research source path has no physical root.");
        }

        var current = Path.TrimEndingDirectorySeparator(root);
        var relative = Path.GetRelativePath(root, fullPath);
        if (relative == ".") return current.Length == 0 ? root : current;
        foreach (var segment in relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(current, segment);
            FileSystemInfo info = Directory.Exists(candidate)
                ? new DirectoryInfo(candidate)
                : new FileInfo(candidate);
            info.Refresh();
            if (!info.Exists) throw new IOException("A research source path is unavailable.");
            current = info.ResolveLinkTarget(true)?.FullName ?? info.FullName;
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(current));
    }

    private static string ResolveWindowsFinalPath(string fullPath)
    {
        using var handle = CreateFileForFinalPath(
            fullPath,
            0,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw new IOException("A research source path identity could not be opened.");
        }

        return ResolveWindowsHandlePath(handle);
    }

    private static string ResolveWindowsHandlePath(SafeFileHandle handle)
    {
        var capacity = 512;
        while (capacity <= MaximumPhysicalPathBytes)
        {
            var buffer = new StringBuilder(capacity);
            var length = GetFinalPathNameByHandle(
                handle,
                buffer,
                checked((uint)buffer.Capacity),
                VolumeNameNt);
            if (length == 0) throw new IOException("A research source path identity could not be resolved.");
            if (length < buffer.Capacity) return Path.TrimEndingDirectorySeparator(buffer.ToString());
            capacity = checked((int)length + 1);
        }

        throw new PathTooLongException("A research source path identity exceeds the safe bound.");
    }

    private void CleanupExpired()
    {
        try
        {
            lock (syncRoot)
            {
                if (!disposed) CleanupExpiredCore(DateTimeOffset.UtcNow);
            }
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            // Lazy cleanup on the next public operation remains authoritative.
        }
    }

    private void CleanupExpiredCore(DateTimeOffset now)
    {
        foreach (var sourceId in sources.Values
            .Where(source => source.ExpiresAtUtc <= now)
            .Select(source => source.SourceId)
            .ToArray())
        {
            RemoveSourceCore(sourceId);
        }
    }

    private void RemoveSourceCore(string sourceId)
    {
        sources.Remove(sourceId);
        foreach (var comparisonId in comparisons.Values
            .Where(comparison => comparison.SourceIds.Contains(sourceId, StringComparer.Ordinal))
            .Select(comparison => comparison.ComparisonId)
            .ToArray())
        {
            RemoveComparisonCore(comparisonId);
        }
    }

    private void EvictComparisonCore()
    {
        if (comparisonUsage.Last is null)
        {
            throw new InvalidOperationException("Research comparison cache accounting is inconsistent.");
        }

        RemoveComparisonCore(comparisonUsage.Last.Value);
    }

    private void RemoveComparisonCore(string comparisonId)
    {
        if (!comparisons.Remove(comparisonId, out var comparison)) return;
        comparisonUsage.Remove(comparisonId);
        comparisonCacheBytes -= comparison.SizeBytes;
    }

    private void TouchComparisonCore(string comparisonId)
    {
        comparisonUsage.Remove(comparisonId);
        comparisonUsage.AddFirst(comparisonId);
    }

    private void ThrowIfDisposed()
    {
        lock (syncRoot) ThrowIfDisposedCore();
    }

    private void ThrowIfDisposedCore()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    private static string Hash(string prefix, params string[] fields)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHash(hash, prefix);
        foreach (var field in fields) AppendHash(hash, field);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void AppendHash(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static bool IsOpaqueId(string value, string prefix) => value is not null
        && value.Length == prefix.Length + 24
        && value.StartsWith(prefix, StringComparison.Ordinal)
        && !value.AsSpan(prefix.Length).ContainsAnyExcept("0123456789abcdef");

    private static bool IsSha256(string value) => value is { Length: 64 }
        && !value.AsSpan().ContainsAnyExcept("0123456789abcdef");

    private static bool ContainsReservedMetadataSegment(string path) => path.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries)
        .Any(segment => string.Equals(segment, ".km", StringComparison.OrdinalIgnoreCase));

    private static bool PathsOverlap(string left, string right) =>
        IsSameOrDescendant(left, right) || IsSameOrDescendant(right, left);

    private static bool IsSameOrDescendant(string path, string ancestor) =>
        string.Equals(path, ancestor, PhysicalPathComparison)
        || path.StartsWith(ancestor + Path.DirectorySeparatorChar, PhysicalPathComparison);

    private static StringComparison PhysicalPathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static bool IsUnverifiableWindowsNetworkPath(string path, string identity) =>
        OperatingSystem.IsWindows()
        && (path.StartsWith("\\\\", StringComparison.Ordinal)
            || identity.StartsWith("\\Device\\Mup\\", StringComparison.OrdinalIgnoreCase)
            || identity.StartsWith("\\Device\\LanmanRedirector\\", StringComparison.OrdinalIgnoreCase));

    private static bool IsWindowsReservedDeviceAlias(string segment)
    {
        var stem = segment.Split('.', 2)[0];
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("CLOCK$", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("CONIN$", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return stem.Length == 4
            && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
            && stem[3] is >= '1' and <= '9' or '¹' or '²' or '³';
    }

    private static bool IsUnsafeUnicode(char character) => char.IsControl(character)
        || char.IsSurrogate(character)
        || character is '\u061c' or '\u200b' or '\u200c' or '\u200d' or '\u200e' or '\u200f'
            or '\u202a' or '\u202b' or '\u202c' or '\u202d' or '\u202e'
            or '\u2060' or '\u2061' or '\u2062' or '\u2063' or '\u2064'
            or '\u2066' or '\u2067' or '\u2068' or '\u2069' or '\ufeff';

    private static bool IsFileSystemFailure(Exception exception) => exception is
        UnauthorizedAccessException or IOException or SecurityException or PathTooLongException
        or ArgumentException or NotSupportedException;

    private static bool IsFatal(Exception exception) => exception is OutOfMemoryException
        or StackOverflowException or AccessViolationException or AppDomainUnloadedException;

    private static ResearchLabValidationException Invalid(string message) =>
        new(message, ResearchLabFailureKind.InvalidData);

    private static ResearchLabValidationException Limit(string message) =>
        new(message, ResearchLabFailureKind.LimitExceeded);

    private static ResearchLabValidationException Stale(string message) =>
        new(message, ResearchLabFailureKind.StaleRevision);

    private static ResearchLabValidationException InvalidCursor(
        string message,
        Exception? exception = null) =>
        new(message, ResearchLabFailureKind.InvalidCursor, exception);

    private static ResearchLabValidationException ExternalRejected(
        string message,
        Exception? exception = null) =>
        new(message, ResearchLabFailureKind.SourceRejected, exception);

    private static ResearchLabValidationException SourceExpired(
        string message,
        Exception? exception = null) =>
        new(message, ResearchLabFailureKind.SourceExpired, exception);

    private static ResearchLabValidationException SnapshotUnavailable(
        string message,
        Exception? exception = null) =>
        new(message, ResearchLabFailureKind.ComparisonStale, exception);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileForFinalPath(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(
        SafeFileHandle file,
        StringBuilder filePath,
        uint filePathLength,
        uint flags);

    [DllImport("libc", EntryPoint = "readlink", SetLastError = true)]
    private static extern nint ReadLink(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        byte[] buffer,
        nuint bufferLength);

    [DllImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static extern int FcntlGetPath(int fileDescriptor, int command, byte[] buffer);

    private sealed class ResearchReadBudget
    {
        private long bytes;
        private int entries;
        private int directories;

        public void ChargeBytes(long value)
        {
            if (value < 0 || value > ResearchLabContract.MaximumAggregateBytes - bytes)
            {
                throw Limit("The research operation exceeds the aggregate source-byte bound.");
            }

            bytes = checked(bytes + value);
        }

        public void ChargeEntry()
        {
            if (++entries > ResearchLabContract.MaximumEntries)
            {
                throw Limit("The research operation exceeds the filesystem entry bound.");
            }
        }

        public void ChargeDirectory()
        {
            if (++directories > ResearchLabContract.MaximumDirectories)
            {
                throw Limit("The research operation exceeds the directory bound.");
            }
        }
    }

    private sealed record PhysicalPathIdentity(string Path, string? Identity);
    private sealed record ValidatedRoot(
        string Path,
        string Identity,
        IReadOnlyList<PhysicalPathIdentity> PrivatePaths);
    private sealed record SourceRegistration(
        string SourceId,
        SemanticProjectRevisionDto Revision,
        ValidatedRoot Root,
        DateTimeOffset ExpiresAtUtc);
    private sealed record SourcePair(SourceRegistration A, SourceRegistration B);
    private sealed record DiscoveredFile(
        string RelativePath,
        long Length,
        DateTime LastWriteTimeUtc);
    private sealed record DiscoveredFiles(IReadOnlyList<DiscoveredFile> Files, int DirectoryCount);
    private sealed record ResearchFileManifest(
        string RelativePath,
        long Length,
        string ContentSha256);
    private sealed record ResearchRootSnapshot(
        string Fingerprint,
        int FileCount,
        int DirectoryCount,
        long TotalBytes,
        IReadOnlyDictionary<string, ResearchFileManifest> Files);
    private sealed record RangeResult(IReadOnlyList<ResearchByteRangeDto> Ranges, bool Truncated);
    private sealed record ComparisonRegistration(
        string ComparisonId,
        string ComparisonFingerprint,
        string QueryFingerprint,
        SemanticProjectRevisionDto Revision,
        IReadOnlyList<string> SourceIds,
        IReadOnlyList<string> SelectedPaths,
        ResearchRootSnapshot SourceA,
        ResearchRootSnapshot SourceB,
        IReadOnlyList<ResearchFileFindingDto> Findings,
        long SizeBytes,
        DateTimeOffset CreatedAtUtc);
    private sealed record CursorState(string ComparisonId, string QueryFingerprint, int Offset);
}
