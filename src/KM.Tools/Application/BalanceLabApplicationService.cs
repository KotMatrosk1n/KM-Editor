// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using KM.Api.Diagnostics;
using KM.Api.Encounters;
using KM.Api.Items;
using KM.Api.Moves;
using KM.Api.Pokemon;
using KM.Api.Projects;
using KM.Api.Semantics;
using KM.Api.Trainers;
using KM.Api.Workflows;
using KM.Core.Indexing;
using KM.Core.Projects;
using KM.Core.Semantics;

namespace KM.Tools.Application;

public sealed class BalanceLabApplicationService
{
    private const int MaximumFindingsPerResponse = 100;
    private const long MaximumCachedStudyBytes = 32L * 1024L * 1024L;
    private const string CacheCallerKeyPrefix = "balance-lab-v1";

    private readonly SemanticExploreApplicationService semanticExploreService;
    private readonly Func<ProjectPathsDto, TrainersWorkflowDto> loadTrainersFresh;
    private readonly Func<ProjectPathsDto, EncountersWorkflowDto> loadEncountersFresh;
    private readonly Func<ProjectPathsDto, MovesWorkflowDto> loadMovesFresh;
    private readonly Func<ProjectPathsDto, ItemsWorkflowDto> loadItemsFresh;
    private readonly Func<ProjectPathsDto, PokemonWorkflowDto> loadPokemonFresh;
    private readonly BoundedDerivedIndexCache<BalanceLabStudyData> cache = new(
        new BoundedDerivedIndexCacheOptions
        {
            MaximumEntryCount = 12,
            MaximumSizeBytes = MaximumCachedStudyBytes,
        });

    public BalanceLabApplicationService(
        SemanticExploreApplicationService semanticExploreService,
        Func<ProjectPathsDto, TrainersWorkflowDto> loadTrainersFresh,
        Func<ProjectPathsDto, EncountersWorkflowDto> loadEncountersFresh,
        Func<ProjectPathsDto, MovesWorkflowDto> loadMovesFresh,
        Func<ProjectPathsDto, ItemsWorkflowDto> loadItemsFresh,
        Func<ProjectPathsDto, PokemonWorkflowDto> loadPokemonFresh)
    {
        this.semanticExploreService = semanticExploreService
            ?? throw new ArgumentNullException(nameof(semanticExploreService));
        this.loadTrainersFresh = loadTrainersFresh
            ?? throw new ArgumentNullException(nameof(loadTrainersFresh));
        this.loadEncountersFresh = loadEncountersFresh
            ?? throw new ArgumentNullException(nameof(loadEncountersFresh));
        this.loadMovesFresh = loadMovesFresh ?? throw new ArgumentNullException(nameof(loadMovesFresh));
        this.loadItemsFresh = loadItemsFresh ?? throw new ArgumentNullException(nameof(loadItemsFresh));
        this.loadPokemonFresh = loadPokemonFresh ?? throw new ArgumentNullException(nameof(loadPokemonFresh));
    }

    public QueryBalanceLabResponse Query(
        QueryBalanceLabRequest request,
        CancellationToken cancellationToken = default)
    {
        return QueryAsync(request, cancellationToken).AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask<QueryBalanceLabResponse> QueryAsync(
        QueryBalanceLabRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new SemanticExploreValidationException(
                "The Balance Lab request is malformed.",
                SemanticExploreFailureKind.InvalidData);
        }

        ValidateRequest(request);
        cancellationToken.ThrowIfCancellationRequested();

        var semanticCapabilities = semanticExploreService.ReadCapabilities(
            new ReadSemanticCapabilitiesRequest(request.Scope));
        EnsureExpectedRevision(semanticCapabilities.Revision, request.ExpectedRevision);
        var snapshot = semanticCapabilities.Snapshots.SingleOrDefault(candidate =>
            candidate.Layer.Kind == request.Layer)
            ?? throw new SemanticExploreValidationException(
                "The requested Balance Lab source layer is unavailable.",
                SemanticExploreFailureKind.Unsupported);
        var provider = GetProvider(semanticCapabilities.Revision.GameFamily);
        var queryFingerprint = QueryFingerprint(request, snapshot);
        var offset = DecodeCursor(request.Cursor, queryFingerprint);

        if (HasRelevantPendingEdits(request))
        {
            var unavailable = UnavailablePendingStudy(provider, request.Study);
            var (pendingRevision, pendingSnapshot) = ReadCompletedSnapshot(request, snapshot);
            return new QueryBalanceLabResponse(
                pendingRevision,
                queryFingerprint,
                pendingSnapshot,
                ReplaceCapability(provider.Capabilities, unavailable.Capability),
                [],
                [],
                unavailable.Diagnostics,
                NextCursor: null);
        }

        var coreRevision = ToCoreRevision(semanticCapabilities.Revision);
        var key = new DerivedIndexCacheKey(
            coreRevision,
            $"{CacheCallerKeyPrefix}:{request.Layer}:{request.Study}");
        BalanceLabStudyData study;
        DerivedIndexCacheItem<BalanceLabStudyData>? pendingCacheItem = null;
        if (!cache.TryGet(key, out study))
        {
            var built = await BuildStudyAsync(request, provider, cancellationToken).ConfigureAwait(false);
            study = built.Value;
            pendingCacheItem = study.Cacheable ? built : null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var (completedRevision, completedSnapshot) = ReadCompletedSnapshot(request, snapshot);

        if (pendingCacheItem is not null && !cache.Set(key, pendingCacheItem))
        {
            throw new SemanticExploreValidationException(
                "The Balance Lab study exceeds its bounded cache budget.",
                SemanticExploreFailureKind.LimitExceeded);
        }

        if (offset > study.Points.Count)
        {
            throw new SemanticExploreValidationException(
                "The Balance Lab continuation cursor is outside the current result set.",
                SemanticExploreFailureKind.InvalidCursor);
        }

        var exactPointRecords = study.Points.Select(point => point.Record).ToHashSet();
        var aggregatePointRecords = study.Points
            .Where(point => point.Record.SubrecordId is null)
            .Select(point => point.Record)
            .ToHashSet();
        var findingsByRecord = study.Findings
            .GroupBy(finding => ResolveFindingOwner(
                finding.Record,
                exactPointRecords,
                aggregatePointRecords))
            .ToDictionary(group => group.Key, group => group.ToArray());
        var pagePoints = new List<BalanceLabChartPointDto>(request.Limit);
        var pageFindings = new List<BalanceLabFindingDto>();
        var pageFindingIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = offset; index < study.Points.Count && pagePoints.Count < request.Limit; index++)
        {
            var candidate = study.Points[index];
            var candidateFindings = findingsByRecord.GetValueOrDefault(candidate.Record) ?? [];
            var additionalCount = candidateFindings.Count(finding => !pageFindingIds.Contains(finding.FindingId));
            if (pagePoints.Count > 0 && pageFindings.Count + additionalCount > MaximumFindingsPerResponse)
            {
                break;
            }

            if (additionalCount > MaximumFindingsPerResponse)
            {
                throw new SemanticExploreValidationException(
                    "One Balance Lab point exceeds the bounded finding response limit.",
                    SemanticExploreFailureKind.LimitExceeded);
            }

            pagePoints.Add(candidate);
            foreach (var finding in candidateFindings)
            {
                if (pageFindingIds.Add(finding.FindingId))
                {
                    pageFindings.Add(finding);
                }
            }
        }

        var points = pagePoints.ToArray();
        var findings = pageFindings.ToArray();
        var nextOffset = checked(offset + points.Length);
        var nextCursor = nextOffset < study.Points.Count
            ? EncodeCursor(queryFingerprint, nextOffset)
            : null;
        return new QueryBalanceLabResponse(
            completedRevision,
            queryFingerprint,
            completedSnapshot,
            ReplaceCapability(provider.Capabilities, study.Capability),
            points,
            findings,
            study.Diagnostics,
            nextCursor);
    }

    private (SemanticProjectRevisionDto Revision, SemanticSourceSnapshotDto Snapshot) ReadCompletedSnapshot(
        QueryBalanceLabRequest request,
        SemanticSourceSnapshotDto initialSnapshot)
    {
        var completedCapabilities = semanticExploreService.ReadCapabilities(
            new ReadSemanticCapabilitiesRequest(request.Scope));
        EnsureExpectedRevision(completedCapabilities.Revision, request.ExpectedRevision);
        var completedSnapshot = completedCapabilities.Snapshots.SingleOrDefault(candidate =>
            candidate.Layer.Kind == request.Layer);
        if (completedSnapshot is null
            || !string.Equals(initialSnapshot.Fingerprint, completedSnapshot.Fingerprint, StringComparison.Ordinal))
        {
            throw new SemanticExploreValidationException(
                "The Balance Lab source layer changed while analysis was running. Refresh and retry.",
                SemanticExploreFailureKind.StaleRevision);
        }

        return (completedCapabilities.Revision, completedSnapshot);
    }

    private ValueTask<DerivedIndexCacheItem<BalanceLabStudyData>> BuildStudyAsync(
        QueryBalanceLabRequest request,
        IBalanceLabFamilyProvider provider,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var paths = request.Layer == SemanticSourceLayerKindDto.Base
            ? request.Scope.Paths with { OutputRootPath = null }
            : request.Scope.Paths;
        BalanceLabStudyData data;
        try
        {
            data = request.Study switch
            {
                BalanceLabStudyDto.TrainerProgression => provider.BuildTrainers(loadTrainersFresh(paths)),
                BalanceLabStudyDto.EncounterDistribution => provider.BuildEncounters(loadEncountersFresh(paths)),
                BalanceLabStudyDto.MoveBalance => provider.BuildMoves(loadMovesFresh(paths)),
                BalanceLabStudyDto.Economy => provider.BuildEconomy(loadItemsFresh(paths)),
                BalanceLabStudyDto.PokedexEvolution => provider.BuildPokedexEvolution(loadPokemonFresh(paths)),
                _ => throw new SemanticExploreValidationException(
                    "The requested Balance Lab study is unsupported.",
                    SemanticExploreFailureKind.Unsupported),
            };
        }
        catch (SemanticExploreValidationException)
        {
            throw;
        }
        catch (InvalidDataException exception) when (exception.Message.Contains("bounded", StringComparison.OrdinalIgnoreCase))
        {
            throw new SemanticExploreValidationException(
                "The Balance Lab source exceeds its bounded decode limits.",
                SemanticExploreFailureKind.LimitExceeded,
                exception);
        }
        catch (InvalidDataException exception)
        {
            throw new SemanticExploreValidationException(
                "The Balance Lab source data is invalid for this study.",
                SemanticExploreFailureKind.InvalidData,
                exception);
        }
        catch (ArgumentException exception)
        {
            throw new SemanticExploreValidationException(
                "The Balance Lab source data is invalid for this study.",
                SemanticExploreFailureKind.InvalidData,
                exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            data = UnavailableWorkflowStudy(provider, request.Study);
        }
        catch (OverflowException exception)
        {
            throw new SemanticExploreValidationException(
                "The Balance Lab source exceeds its bounded numeric limits.",
                SemanticExploreFailureKind.LimitExceeded,
                exception);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var estimatedSize = EstimateSize(data);
        if (estimatedSize > MaximumCachedStudyBytes)
        {
            throw new SemanticExploreValidationException(
                "The Balance Lab study exceeds its bounded cache budget.",
                SemanticExploreFailureKind.LimitExceeded);
        }

        return ValueTask.FromResult(new DerivedIndexCacheItem<BalanceLabStudyData>(data, estimatedSize));
    }

    private static BalanceLabStudyData UnavailablePendingStudy(
        IBalanceLabFamilyProvider provider,
        BalanceLabStudyDto study)
    {
        var declared = provider.Capabilities.Single(capability => capability.Study == study);
        return new BalanceLabStudyData(
            declared with
            {
                State = SemanticCoverageStateDto.Unavailable,
                Confidence = SemanticConfidenceDto.Unknown,
                ReasonCode = "pending-overlay-unavailable",
            },
            [],
            [],
            [
                new ApiDiagnostic(
                    ApiDiagnosticSeverity.Info,
                    "This Balance Lab study is unavailable because its owning workflow has pending edits that the read-only analyzer cannot overlay safely.",
                    Domain: StudyDomain(study))
                {
                    Code = "KM-BALANCE-LAB-PENDING-OVERLAY-UNAVAILABLE",
                },
            ],
            Cacheable: false);
    }

    private static BalanceLabStudyData UnavailableWorkflowStudy(
        IBalanceLabFamilyProvider provider,
        BalanceLabStudyDto study)
    {
        var declared = provider.Capabilities.Single(capability => capability.Study == study);
        return new BalanceLabStudyData(
            declared with
            {
                State = SemanticCoverageStateDto.Unavailable,
                Confidence = SemanticConfidenceDto.Unknown,
                ReasonCode = "workflow-source-unavailable",
            },
            [],
            [],
            [
                new ApiDiagnostic(
                    ApiDiagnosticSeverity.Warning,
                    "The owning workflow could not load a bounded source for this Balance Lab study.",
                    Domain: StudyDomain(study))
                {
                    Code = "KM-BALANCE-LAB-SOURCE-UNAVAILABLE",
                },
            ],
            Cacheable: false);
    }

    private static bool HasRelevantPendingEdits(QueryBalanceLabRequest request)
    {
        if (request.Layer != SemanticSourceLayerKindDto.Pending
            || request.Scope.PendingSession is not { HasPendingChanges: true } session)
        {
            return false;
        }

        var domain = StudyDomain(request.Study);
        return session.PendingEdits.Any(edit => string.Equals(edit.Domain, domain, StringComparison.Ordinal));
    }

    private static string StudyDomain(BalanceLabStudyDto study)
    {
        return study switch
        {
            BalanceLabStudyDto.TrainerProgression => "workflow.trainers",
            BalanceLabStudyDto.EncounterDistribution => "workflow.encounters",
            BalanceLabStudyDto.MoveBalance => "workflow.moves",
            BalanceLabStudyDto.Economy => "workflow.items",
            BalanceLabStudyDto.PokedexEvolution => "workflow.pokemon",
            _ => throw new ArgumentOutOfRangeException(nameof(study), study, null),
        };
    }

    private static IReadOnlyList<BalanceLabStudyCapabilityDto> ReplaceCapability(
        IReadOnlyList<BalanceLabStudyCapabilityDto> capabilities,
        BalanceLabStudyCapabilityDto selected)
    {
        return capabilities
            .Select(capability => capability.Study == selected.Study ? selected : capability)
            .OrderBy(capability => capability.Study)
            .ToArray();
    }

    private static SemanticRecordRefDto ResolveFindingOwner(
        SemanticRecordRefDto findingRecord,
        IReadOnlySet<SemanticRecordRefDto> exactPointRecords,
        IReadOnlySet<SemanticRecordRefDto> aggregatePointRecords)
    {
        if (exactPointRecords.Contains(findingRecord))
        {
            return findingRecord;
        }

        var aggregateRecord = findingRecord with { SubrecordId = null };
        return aggregatePointRecords.Contains(aggregateRecord)
            ? aggregateRecord
            : findingRecord;
    }

    private static long EstimateSize(BalanceLabStudyData data)
    {
        long size = 2_048;
        foreach (var point in data.Points)
        {
            size = checked(size + 512L + point.Label.Length * sizeof(char));
            size = checked(size + point.Facts.Count * 768L);
        }

        foreach (var finding in data.Findings)
        {
            size = checked(size + 1_024L + (finding.Title.Length + finding.Summary.Length) * sizeof(char));
            size = checked(size + finding.Facts.Count * 768L + finding.RelatedRecords.Count * 256L);
        }

        size = checked(size + data.Diagnostics.Count * 1_024L);
        return size;
    }

    private static string QueryFingerprint(
        QueryBalanceLabRequest request,
        SemanticSourceSnapshotDto snapshot)
    {
        var canonical = string.Join(
            '\n',
            "balance-lab-query-v1",
            request.ExpectedRevision.ProjectId,
            request.ExpectedRevision.GameFamily.ToString(),
            request.ExpectedRevision.Generation,
            request.ExpectedRevision.Fingerprint,
            snapshot.Fingerprint,
            request.Study.ToString(),
            request.Layer.ToString());
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string EncodeCursor(string queryFingerprint, int offset)
    {
        var text = $"{queryFingerprint}:{offset.ToString(CultureInfo.InvariantCulture)}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(text))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static int DecodeCursor(string? cursor, string queryFingerprint)
    {
        if (cursor is null)
        {
            return 0;
        }

        try
        {
            var normalized = cursor.Replace('-', '+').Replace('_', '/');
            normalized += new string('=', (4 - normalized.Length % 4) % 4);
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
            var separator = decoded.LastIndexOf(':');
            if (separator <= 0
                || !string.Equals(decoded[..separator], queryFingerprint, StringComparison.Ordinal)
                || !int.TryParse(decoded[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var offset)
                || offset < 0)
            {
                throw new FormatException();
            }

            return offset;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new SemanticExploreValidationException(
                "The Balance Lab continuation cursor is invalid or belongs to another query.",
                SemanticExploreFailureKind.InvalidCursor,
                exception);
        }
    }

    private static void ValidateRequest(QueryBalanceLabRequest request)
    {
        if (request.Scope is null
            || request.Scope.Paths is null
            || request.ExpectedRevision is null)
        {
            throw new SemanticExploreValidationException(
                "The Balance Lab request scope or expected revision is malformed.",
                SemanticExploreFailureKind.InvalidData);
        }

        if (!Enum.IsDefined(request.Study)
            || request.Layer is not (SemanticSourceLayerKindDto.Base
                or SemanticSourceLayerKindDto.Layered
                or SemanticSourceLayerKindDto.Pending))
        {
            throw new SemanticExploreValidationException(
                "The requested Balance Lab study or source layer is unsupported.",
                SemanticExploreFailureKind.Unsupported);
        }

        if (request.Limit is <= 0 or > SemanticExploreContract.MaximumPageSize)
        {
            throw new SemanticExploreValidationException(
                $"A Balance Lab page must request between 1 and {SemanticExploreContract.MaximumPageSize.ToString(CultureInfo.InvariantCulture)} points.",
                SemanticExploreFailureKind.LimitExceeded);
        }

        if (request.Cursor is { Length: > SemanticExploreContract.MaximumCursorLength }
            || request.Cursor?.Any(character => char.IsControl(character)) == true)
        {
            throw new SemanticExploreValidationException(
                "The Balance Lab continuation cursor is invalid.",
                SemanticExploreFailureKind.InvalidCursor);
        }

        ValidateRevision(request.ExpectedRevision);
    }

    private static void ValidateRevision(SemanticProjectRevisionDto revision)
    {
        if (string.IsNullOrWhiteSpace(revision.ProjectId)
            || revision.ProjectId != revision.ProjectId.Trim()
            || !Enum.IsDefined(revision.GameFamily)
            || !long.TryParse(revision.Generation, NumberStyles.None, CultureInfo.InvariantCulture, out var generation)
            || generation < 0
            || revision.Fingerprint is not { Length: 64 } fingerprint
            || !fingerprint.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'))
        {
            throw new SemanticExploreValidationException(
                "The expected Balance Lab source revision is invalid.",
                SemanticExploreFailureKind.InvalidData);
        }
    }

    private static void EnsureExpectedRevision(
        SemanticProjectRevisionDto current,
        SemanticProjectRevisionDto expected)
    {
        if (!Equals(current, expected))
        {
            throw new SemanticExploreValidationException(
                "The Balance Lab source revision changed. Refresh the workspace and retry.",
                SemanticExploreFailureKind.StaleRevision);
        }
    }

    private static ProjectSourceRevision ToCoreRevision(SemanticProjectRevisionDto revision)
    {
        var family = revision.GameFamily switch
        {
            SemanticGameFamilyDto.SwordShield => GameFamily.SwordShield,
            SemanticGameFamilyDto.ScarletViolet => GameFamily.ScarletViolet,
            SemanticGameFamilyDto.LegendsZA => GameFamily.LegendsZA,
            _ => throw new ArgumentOutOfRangeException(nameof(revision), revision.GameFamily, null),
        };
        return new ProjectSourceRevision(
            new ProjectId(revision.ProjectId),
            family,
            long.Parse(revision.Generation, NumberStyles.None, CultureInfo.InvariantCulture),
            revision.Fingerprint);
    }

    private static IBalanceLabFamilyProvider GetProvider(SemanticGameFamilyDto family)
    {
        return family switch
        {
            SemanticGameFamilyDto.SwordShield => new SwShBalanceLabProvider(),
            SemanticGameFamilyDto.ScarletViolet => new SvBalanceLabProvider(),
            SemanticGameFamilyDto.LegendsZA => new ZaBalanceLabProvider(),
            _ => throw new SemanticExploreValidationException(
                "The selected Balance Lab family provider is unsupported.",
                SemanticExploreFailureKind.Unsupported),
        };
    }

}
