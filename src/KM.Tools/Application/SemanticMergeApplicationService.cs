// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KM.Api.Bridge;
using KM.Api.ChangeSets;
using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Items;
using KM.Api.Moves;
using KM.Api.Pokemon;
using KM.Api.Projects;
using KM.Api.SemanticMerging;
using KM.Api.Semantics;
using KM.Api.Workflows;
using KM.Core.Editing;
using KM.Core.Projects;
using KM.Core.Semantics;
using KM.Core.Workspace;
using KM.Tools.Bridge;

namespace KM.Tools.Application;

public enum SemanticMergeFailureKind
{
    StaleProposal,
    StaleRecipeProposal,
}

public sealed class SemanticMergeValidationException : Exception
{
    public SemanticMergeValidationException(string message, SemanticMergeFailureKind failureKind)
        : base(message)
    {
        FailureKind = failureKind;
    }

    public SemanticMergeFailureKind FailureKind { get; }
}

public sealed class SemanticMergeApplicationService : IDisposable
{
    private const string ProviderSchema = "km.semantic-scalar.swsh.v1";
    private const string ProviderId = "swsh.semantic-merge.scalar.v1";
    private const string RecipeProviderId = "swsh.recipe.scalar.v1";
    private const string UnavailableProviderId = "semantic-merge.unavailable.v1";
    private const string LegacyHardeningReason = "legacy-review-transaction-hardening-required";
    private const string CollectionUnavailableReason =
        "stable-collection-operation-provider-unavailable";
    private const string CliUnavailableReason = "shared-cli-facade-unavailable";
    private const string SeedUnavailableReason = "seeded-recipe-provider-unavailable";
    private const string LegacyFallbackUnavailableReason =
        "legacy-reviewed-transaction-boundary-unavailable";
    private const string SelectionRequiredCode = "KM-SEMANTIC-MERGE-TARGET-SELECTION-REQUIRED";
    private const string ConflictCode = "KM-SEMANTIC-MERGE-CONFLICT";
    private const string ProposalBlockedCode = "KM-SEMANTIC-MERGE-PROPOSAL-BLOCKED";
    private const string RecipeBlockedCode = "KM-RECIPE-COMPATIBILITY-BLOCKED";
    private const string MergeProviderValidationCode =
        "KM-SEMANTIC-MERGE-PROVIDER-VALIDATION-FAILED";
    private const string RecipeProviderValidationCode = "KM-RECIPE-PROVIDER-VALIDATION-FAILED";
    private const int MaximumSourceHandles = 4;
    private const int MaximumRecipeHandles = 8;
    private const int RecipeCacheProvisionMultiplier = 4;
    private const int RecipeCacheHardCeilingMultiplier = 2;
    private const long ExpectedRecipeCacheBytes = 32L * 1024L * 1024L;
    private const long ProvisionedRecipeCacheBytes = checked(
        ExpectedRecipeCacheBytes * RecipeCacheProvisionMultiplier);
    private const long MaximumRecipeCacheBytes = checked(
        ProvisionedRecipeCacheBytes * RecipeCacheHardCeilingMultiplier);
    private static readonly TimeSpan HandleTimeToLive = TimeSpan.FromMinutes(30);
    private static readonly string[] PokemonProviderFields =
    [
        "hp",
        "attack",
        "defense",
        "specialAttack",
        "specialDefense",
        "speed",
        "type1",
        "type2",
        "catchRate",
        "baseExperience",
        "ability1",
        "ability2",
        "hiddenAbility",
        "heldItem1",
        "heldItem2",
        "heldItem3",
    ];
    private static readonly string[] MoveProviderFields =
    [
        "type",
        "category",
        "power",
        "accuracy",
        "pp",
        "priority",
        "critStage",
        "maxMovePower",
        "target",
        "hitMin",
        "hitMax",
        "inflict",
        "inflictPercent",
        "flinch",
        "recoil",
        "rawHealing",
    ];
    private static readonly JsonSerializerOptions RecipeJsonOptions = CreateRecipeJsonOptions();

    private readonly SemanticExploreApplicationService semanticExploreService;
    private readonly ChangeSetApplicationService changeSetService;
    private readonly Func<ProjectPathsDto, ItemsWorkflowDto> loadItemsFresh;
    private readonly Func<ProjectPathsDto, PokemonWorkflowDto> loadPokemonFresh;
    private readonly Func<ProjectPathsDto, MovesWorkflowDto> loadMovesFresh;
    private readonly Func<
        ProjectPathsDto,
        IReadOnlyList<GuidedDesignStagingEdit>,
        GuidedDesignStagingResult> stageEdits;
    private readonly Func<ProjectPathsDto, EditSession, ChangePlanOutputModeDto?, ChangePlan>
        createChangePlan;
    private readonly Func<ProjectPathsDto, EditSession, ChangePlanOutputModeDto?, ChangePlan>
        createBoundedScalarChangePlan;
    private readonly object sourceHandleSync = new();
    private readonly Dictionary<string, SourceHandleEntry> sourceHandles = new(StringComparer.Ordinal);
    private readonly object recipeHandleSync = new();
    private readonly Dictionary<string, RecipeHandleEntry> recipeHandles = new(StringComparer.Ordinal);
    private readonly Timer handleExpiryTimer;
    private long recipeCacheBytes;
    private int disposed;

    public SemanticMergeApplicationService(
        SemanticExploreApplicationService semanticExploreService,
        ChangeSetApplicationService changeSetService,
        Func<ProjectPathsDto, ItemsWorkflowDto> loadItemsFresh,
        Func<ProjectPathsDto, PokemonWorkflowDto> loadPokemonFresh,
        Func<ProjectPathsDto, MovesWorkflowDto> loadMovesFresh,
        Func<
            ProjectPathsDto,
            IReadOnlyList<GuidedDesignStagingEdit>,
            GuidedDesignStagingResult> stageEdits,
        Func<ProjectPathsDto, EditSession, ChangePlanOutputModeDto?, ChangePlan> createChangePlan,
        Func<ProjectPathsDto, EditSession, ChangePlanOutputModeDto?, ChangePlan>
            createBoundedScalarChangePlan)
    {
        this.semanticExploreService = semanticExploreService
            ?? throw new ArgumentNullException(nameof(semanticExploreService));
        this.changeSetService = changeSetService
            ?? throw new ArgumentNullException(nameof(changeSetService));
        this.loadItemsFresh = loadItemsFresh ?? throw new ArgumentNullException(nameof(loadItemsFresh));
        this.loadPokemonFresh = loadPokemonFresh
            ?? throw new ArgumentNullException(nameof(loadPokemonFresh));
        this.loadMovesFresh = loadMovesFresh ?? throw new ArgumentNullException(nameof(loadMovesFresh));
        this.stageEdits = stageEdits ?? throw new ArgumentNullException(nameof(stageEdits));
        this.createChangePlan = createChangePlan
            ?? throw new ArgumentNullException(nameof(createChangePlan));
        this.createBoundedScalarChangePlan = createBoundedScalarChangePlan
            ?? throw new ArgumentNullException(nameof(createBoundedScalarChangePlan));
        handleExpiryTimer = new Timer(
            _ => ExpireHandles(),
            state: null,
            dueTime: TimeSpan.FromMinutes(1),
            period: TimeSpan.FromMinutes(1));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        handleExpiryTimer.Dispose();
        List<SourceHandleEntry> sources;
        lock (sourceHandleSync)
        {
            sources = sourceHandles.Values.ToList();
            sourceHandles.Clear();
        }

        ReleaseSourceEntries(sources);
        lock (recipeHandleSync)
        {
            recipeHandles.Clear();
            recipeCacheBytes = 0;
        }
    }

    private void ExpireHandles()
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            List<SourceHandleEntry> sources;
            lock (sourceHandleSync)
            {
                sources = RemoveExpiredSourceHandlesCore(now);
            }

            ReleaseSourceEntries(sources);
            lock (recipeHandleSync)
            {
                RemoveExpiredRecipeHandles(now);
            }
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            // Expiry is best-effort housekeeping. Lazy cleanup repeats the same
            // bounded removal on the next public operation.
        }
    }

    public ReadSemanticMergeCapabilitiesResponse ReadCapabilities(
        ReadSemanticMergeCapabilitiesRequest request)
    {
        if (request?.Scope?.Paths is null)
        {
            throw Invalid("The semantic merge capability request is malformed.");
        }

        var initial = semanticExploreService.ReadCapabilities(
            new ReadSemanticCapabilitiesRequest(request.Scope));
        var capabilities = BuildCapabilities(request.Scope.Paths, initial.Revision.GameFamily);
        var completed = semanticExploreService.ReadCapabilities(
            new ReadSemanticCapabilitiesRequest(request.Scope));
        EnsureSameRevision(initial.Revision, completed.Revision);
        return new ReadSemanticMergeCapabilitiesResponse(
            completed.Revision,
            completed.Snapshots,
            capabilities,
            CanOpenLegacyMerger: false);
    }

    public OpenSemanticMergeSourceResponse OpenSource(OpenSemanticMergeSourceRequest request)
    {
        if (request?.Scope?.Paths is null
            || request.ExpectedRevision is null
            || request.ExternalRootPath is null
            || request.ExternalRootPath.Length is 0
                or > SemanticMergeContract.MaximumExternalRootLength)
        {
            throw Invalid("The semantic merge source request is malformed.");
        }

        if (request.ExpectedRevision.GameFamily != SemanticGameFamilyDto.SwordShield)
        {
            throw Unsupported(
                "Semantic merge sources are unavailable for this family until its legacy review and transaction boundary is hardened.");
        }

        var now = DateTimeOffset.UtcNow;
        var preReleased = new List<SourceHandleEntry>();
        lock (sourceHandleSync)
        {
            preReleased.AddRange(RemoveExpiredSourceHandlesCore(now));
            if (sourceHandles.Count >= MaximumSourceHandles)
            {
                var oldest = sourceHandles.Values
                    .OrderBy(candidate => candidate.LastAccessUtc)
                    .ThenBy(candidate => candidate.PublicId, StringComparer.Ordinal)
                    .First();
                sourceHandles.Remove(oldest.PublicId);
                preReleased.Add(oldest);
            }
        }

        ReleaseSourceEntries(preReleased);
        var opened = semanticExploreService.OpenSemanticMergeSource(
            request.Scope,
            request.ExpectedRevision,
            request.ExternalRootPath);
        var publicId = "merge-src-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var publicSnapshot = opened.Snapshot with
        {
            Layer = opened.Snapshot.Layer with { InstanceId = publicId },
        };
        var entry = new SourceHandleEntry(
            publicId,
            request.ExpectedRevision,
            opened.InstanceId,
            opened.Snapshot.Fingerprint,
            publicSnapshot,
            opened.Coverage,
            now,
            now + HandleTimeToLive);
        var removed = new List<SourceHandleEntry>();
        try
        {
            lock (sourceHandleSync)
            {
                removed.AddRange(RemoveExpiredSourceHandlesCore(now));
                while (sourceHandles.Count >= MaximumSourceHandles)
                {
                    var oldest = sourceHandles.Values
                        .OrderBy(candidate => candidate.LastAccessUtc)
                        .ThenBy(candidate => candidate.PublicId, StringComparer.Ordinal)
                        .First();
                    sourceHandles.Remove(oldest.PublicId);
                    removed.Add(oldest);
                }

                sourceHandles.Add(publicId, entry);
            }
        }
        catch
        {
            semanticExploreService.ReleaseSemanticMergeSource(
                request.ExpectedRevision,
                opened.InstanceId);
            throw;
        }

        ReleaseSourceEntries(removed);

        return new OpenSemanticMergeSourceResponse(
            request.ExpectedRevision,
            new SemanticMergeSourceDto(publicId, publicSnapshot, opened.Coverage));
    }

    private IReadOnlyList<SemanticMergeCapabilityDto> BuildCapabilities(
        ProjectPathsDto paths,
        SemanticGameFamilyDto family)
    {
        if (family != SemanticGameFamilyDto.SwordShield)
        {
            return UnavailableCapabilities(LegacyHardeningReason);
        }

        var domains = ReadWritableDomains(paths);
        var scalarState = domains.Count == 0
            ? SemanticCoverageStateDto.Unavailable
            : SemanticCoverageStateDto.Partial;
        var scalarReason = domains.Count == 0
            ? "provider-fields-unavailable"
            : "scalar-fields-single-domain-proposals-only";
        return
        [
            Capability(
                SemanticMergeFeatureDto.ThreeWayScalarMerge,
                ProviderId,
                scalarState,
                SemanticConfidenceDto.Verified,
                scalarReason,
                domains),
            Capability(
                SemanticMergeFeatureDto.FocusedConflictResolution,
                ProviderId,
                scalarState,
                SemanticConfidenceDto.Verified,
                domains.Count == 0 ? "provider-fields-unavailable" : "focused-scalar-conflicts-only",
                domains),
            Capability(
                SemanticMergeFeatureDto.StableCollectionMerge,
                UnavailableProviderId,
                SemanticCoverageStateDto.Unavailable,
                SemanticConfidenceDto.Unknown,
                CollectionUnavailableReason),
            Capability(
                SemanticMergeFeatureDto.OpaqueFileFallback,
                UnavailableProviderId,
                SemanticCoverageStateDto.Unavailable,
                SemanticConfidenceDto.Unknown,
                LegacyFallbackUnavailableReason),
            Capability(
                SemanticMergeFeatureDto.RecipeImport,
                RecipeProviderId,
                scalarState,
                SemanticConfidenceDto.Verified,
                scalarReason,
                domains),
            Capability(
                SemanticMergeFeatureDto.RecipeExport,
                RecipeProviderId,
                scalarState,
                SemanticConfidenceDto.Verified,
                domains.Count == 0
                    ? "provider-fields-unavailable"
                    : "scalar-fields-single-domain-proposals-only",
                domains),
            Capability(
                SemanticMergeFeatureDto.CompatibilityReport,
                RecipeProviderId,
                scalarState,
                SemanticConfidenceDto.Verified,
                scalarReason,
                domains),
            Capability(
                SemanticMergeFeatureDto.SeededReproducibility,
                UnavailableProviderId,
                SemanticCoverageStateDto.Unavailable,
                SemanticConfidenceDto.Unknown,
                SeedUnavailableReason),
            Capability(
                SemanticMergeFeatureDto.HeadlessAutomation,
                UnavailableProviderId,
                SemanticCoverageStateDto.Unavailable,
                SemanticConfidenceDto.Unknown,
                CliUnavailableReason),
        ];
    }

    private IReadOnlyList<SemanticMergeDomainCapabilityDto> ReadWritableDomains(ProjectPathsDto paths)
    {
        var domains = new List<SemanticMergeDomainCapabilityDto>(3);
        var items = TryLoadWritableDomain(() => loadItemsFresh(paths));
        if (items is not null)
        {
            var itemFields = items.EditableFields
                .Where(field => !field.IsReadOnly && IsIntegerWritable(field.MinimumValue, field.MaximumValue, field.Options.Count))
                .Where(field => items.Items.Any(item => item.ItemId > 0 && item.FieldValues.GetValueOrDefault(field.Field) is not null))
                .Select(field => field.Field)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (WorkflowReady(items.Summary, items.Diagnostics) && itemFields.Length > 0)
            {
                domains.Add(new SemanticMergeDomainCapabilityDto("workflow.items", "item", itemFields));
            }
        }

        var pokemon = TryLoadWritableDomain(() => loadPokemonFresh(paths));
        if (pokemon is not null)
        {
            var pokemonProviderFields = PokemonProviderFields.ToHashSet(StringComparer.Ordinal);
            var pokemonFields = pokemon.EditableFields
                .Where(field => pokemonProviderFields.Contains(field.Field))
                .Where(field => IsIntegerWritable(field.MinimumValue, field.MaximumValue, field.Options.Count))
                .Select(field => field.Field)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (WorkflowReady(pokemon.Summary, pokemon.Diagnostics)
                && pokemon.Pokemon.Any(candidate => candidate.PersonalId > 0)
                && pokemonFields.Length > 0)
            {
                domains.Add(new SemanticMergeDomainCapabilityDto(
                    "workflow.pokemon",
                    "pokemon-personal",
                    pokemonFields));
            }
        }

        var moves = TryLoadWritableDomain(() => loadMovesFresh(paths));
        if (moves is not null)
        {
            var moveProviderFields = MoveProviderFields.ToHashSet(StringComparer.Ordinal);
            var moveFields = moves.EditableFields
                .Where(field => moveProviderFields.Contains(field.Field))
                .Where(field => IsIntegerWritable(field.MinimumValue, field.MaximumValue, field.Options.Count))
                .Select(field => field.Field)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (WorkflowReady(moves.Summary, moves.Diagnostics)
                && moves.Moves.Any(candidate => candidate.MoveId >= 0)
                && moveFields.Length > 0)
            {
                domains.Add(new SemanticMergeDomainCapabilityDto("workflow.moves", "move", moveFields));
            }
        }

        return domains;
    }

    private static T? TryLoadWritableDomain<T>(Func<T> load)
        where T : class
    {
        try
        {
            return load();
        }
        catch (SemanticExploreValidationException)
        {
            throw;
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            return null;
        }
    }

    private static bool IsIntegerWritable(int? minimum, int? maximum, int optionCount) =>
        minimum is not null && maximum is not null || optionCount > 0;

    private static bool IsIntegerWritable(double? minimum, double? maximum, int optionCount) =>
        minimum is not null
        && maximum is not null
        && minimum.Value >= int.MinValue
        && maximum.Value <= int.MaxValue
        || optionCount > 0;

    private static bool WorkflowReady(
        WorkflowSummaryDto summary,
        IReadOnlyList<ApiDiagnostic> diagnostics) =>
        summary.Availability == WorkflowAvailabilityDto.Available
        && !summary.Diagnostics.Concat(diagnostics)
            .Any(diagnostic =>
                diagnostic.Severity == ApiDiagnosticSeverity.Error
                || diagnostic.Severity == ApiDiagnosticSeverity.Warning
                    && !IsReadinessIrrelevantProjectWarning(diagnostic));

    private static bool IsReadinessIrrelevantProjectWarning(ApiDiagnostic diagnostic)
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

    private static IReadOnlyList<SemanticMergeCapabilityDto> UnavailableCapabilities(
        string reasonCode) =>
        Enum.GetValues<SemanticMergeFeatureDto>()
            .Select(feature => Capability(
                feature,
                UnavailableProviderId,
                SemanticCoverageStateDto.Unavailable,
                SemanticConfidenceDto.Unknown,
                feature switch
                {
                    SemanticMergeFeatureDto.HeadlessAutomation => CliUnavailableReason,
                    SemanticMergeFeatureDto.SeededReproducibility => SeedUnavailableReason,
                    SemanticMergeFeatureDto.StableCollectionMerge => CollectionUnavailableReason,
                    SemanticMergeFeatureDto.OpaqueFileFallback => LegacyFallbackUnavailableReason,
                    _ => reasonCode,
                }))
            .ToArray();

    private static SemanticMergeCapabilityDto Capability(
        SemanticMergeFeatureDto feature,
        string providerId,
        SemanticCoverageStateDto state,
        SemanticConfidenceDto confidence,
        string? reasonCode,
        IReadOnlyList<SemanticMergeDomainCapabilityDto>? domains = null) =>
        new(feature, providerId, state, confidence, reasonCode, domains ?? []);

    public PreviewSemanticMergeResponse Preview(
        PreviewSemanticMergeRequest request,
        CancellationToken cancellationToken = default) =>
        PreviewAsync(request, cancellationToken).GetAwaiter().GetResult();

    public async Task<PreviewSemanticMergeResponse> PreviewAsync(
        PreviewSemanticMergeRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidatePreviewRequest(request);
        var sourceA = ResolveSourceHandle(request.SourceAInstanceId, request.Scope, request.ExpectedRevision);
        var sourceB = ResolveSourceHandle(request.SourceBInstanceId, request.Scope, request.ExpectedRevision);
        if (string.Equals(sourceA.PublicId, sourceB.PublicId, StringComparison.Ordinal)
            || string.Equals(sourceA.InternalId, sourceB.InternalId, StringComparison.Ordinal))
        {
            throw Invalid("Semantic merge requires two distinct source registrations.");
        }

        var initialReview = await changeSetService.ObserveGeneratedReviewWorkspaceAsync(
                ToChangeSetScope(request.Scope),
                request.ExpectedChangeSetETag,
                request.Scope.PendingSession,
                cancellationToken)
            .ConfigureAwait(false);
        var initialContext = initialReview.AuthoringContext;
        var generated = GenerateMerge(
            request.Scope,
            request.ExpectedRevision,
            sourceA,
            sourceB,
            request.Targets,
            request.Resolutions,
            request.TargetSearchText,
            initialContext,
            cancellationToken);
        var completedReview = await changeSetService.ObserveGeneratedReviewWorkspaceAsync(
                ToChangeSetScope(request.Scope),
                request.ExpectedChangeSetETag,
                request.Scope.PendingSession,
                cancellationToken)
            .ConfigureAwait(false);
        var completedContext = completedReview.AuthoringContext;
        if (!string.Equals(
                initialContext.Fingerprint,
                completedContext.Fingerprint,
                StringComparison.Ordinal))
        {
            throw StaleMerge("The semantic merge authoring context changed. Preview it again.");
        }

        if (request.Cursor is not null
            && (!FieldRefsMatch(request.Targets, generated.NormalizedTargets)
                || !ResolutionsMatch(request.Resolutions, generated.NormalizedResolutions)
                || !string.Equals(
                    request.TargetSearchText,
                    generated.NormalizedTargetSearchText,
                    StringComparison.Ordinal)
                || !string.Equals(request.ProposalId, generated.ProposalId, StringComparison.Ordinal)
                || !string.Equals(
                    request.ProposalFingerprint,
                    generated.ProposalFingerprint,
                    StringComparison.Ordinal)))
        {
            throw StaleMerge("The semantic merge proposal changed before its next page was read.");
        }

        var queryFingerprint = Hash(
            "semantic-merge-query-v1",
            generated.ProposalId,
            generated.ProposalFingerprint,
            generated.NormalizedTargetSearchText);
        var offset = DecodeCursor(request.Cursor, queryFingerprint);
        if (offset > generated.Rows.Count)
        {
            throw InvalidCursor("The semantic merge cursor is outside the current result set.");
        }

        var page = generated.Rows.Skip(offset).Take(request.Limit).ToArray();
        var nextOffset = checked(offset + request.Limit);
        var nextCursor = nextOffset < generated.Rows.Count
            ? EncodeCursor(queryFingerprint, nextOffset)
            : null;
        return new PreviewSemanticMergeResponse(
            generated.Revision,
            queryFingerprint,
            generated.BaseSnapshot,
            generated.LayeredSnapshot,
            generated.PendingSnapshot,
            PublicSnapshot(generated.SourceASnapshot, sourceA),
            PublicSnapshot(generated.SourceBSnapshot, sourceB),
            generated.Capabilities,
            generated.NormalizedTargets,
            generated.NormalizedResolutions,
            completedContext.Fingerprint,
            generated.ProposalId,
            generated.ProposalFingerprint,
            generated.CanImport,
            generated.SelectionRequired,
            generated.NormalizedTargetSearchText,
            generated.TargetWindowCapped,
            generated.TotalMatchingTargetCount,
            generated.TotalRowCount,
            generated.TotalConflictCount,
            generated.PendingEdits.Count,
            page,
            generated.Diagnostics,
            nextCursor);
    }

    public ImportSemanticMergeResponse Import(
        ImportSemanticMergeRequest request,
        CancellationToken cancellationToken = default) =>
        ImportAsync(request, cancellationToken).GetAwaiter().GetResult();

    public async Task<ImportSemanticMergeResponse> ImportAsync(
        ImportSemanticMergeRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateImportRequest(request);
        var sourceA = ResolveSourceHandle(request.SourceAInstanceId, request.Scope, request.ExpectedRevision);
        var sourceB = ResolveSourceHandle(request.SourceBInstanceId, request.Scope, request.ExpectedRevision);
        if (string.Equals(sourceA.PublicId, sourceB.PublicId, StringComparison.Ordinal)
            || string.Equals(sourceA.InternalId, sourceB.InternalId, StringComparison.Ordinal))
        {
            throw Invalid("Semantic merge requires two distinct source registrations.");
        }

        MergeGeneratedProposal? regenerated = null;
        var planner = Planner(request.Scope.Paths);
        var result = await changeSetService.ImportGeneratedReviewProposalAsync(
                new GeneratedChangeSetImportRequest(
                    ToChangeSetScope(request.Scope),
                    request.ChangeSetName,
                    GeneratedChangeSetOwners.SemanticMerge,
                    "semantic-merge",
                    "Import semantic merge proposal",
                    request.ExpectedChangeSetETag,
                    request.Scope.PendingSession),
                (context, token) =>
                {
                    var candidate = GenerateMerge(
                        request.Scope,
                        request.ExpectedRevision,
                        sourceA,
                        sourceB,
                        request.Targets,
                        request.Resolutions,
                        targetSearchText: null,
                        context,
                        token);
                    if (!candidate.CanImport
                        || !string.Equals(candidate.ProposalId, request.ProposalId, StringComparison.Ordinal)
                        || !string.Equals(
                            candidate.ProposalFingerprint,
                            request.ProposalFingerprint,
                            StringComparison.Ordinal)
                        || !FieldRefsMatch(request.Targets, candidate.NormalizedTargets)
                        || !ResolutionsMatch(request.Resolutions, candidate.NormalizedResolutions))
                    {
                        throw StaleMerge(
                            "The semantic merge proposal is stale or no longer safe to import.");
                    }

                    regenerated = candidate;
                    return Task.FromResult(new GeneratedChangeSetProposal(
                        candidate.ProposalId,
                        candidate.ProposalFingerprint,
                        candidate.PendingEdits));
                },
                planner,
                cancellationToken)
            .ConfigureAwait(false);
        var proposal = regenerated
            ?? throw StaleMerge("The semantic merge proposal could not be regenerated.");
        ConsumeSourceHandles(sourceA, sourceB);
        return new ImportSemanticMergeResponse(
            proposal.Revision,
            proposal.ProposalId,
            proposal.ProposalFingerprint,
            result.ImportedChangeSetId,
            ToReceipt(result),
            proposal.Diagnostics);
    }

    private MergeGeneratedProposal GenerateMerge(
        SemanticExploreScopeDto scope,
        SemanticProjectRevisionDto expectedRevision,
        SourceHandleEntry sourceA,
        SourceHandleEntry sourceB,
        IReadOnlyList<SemanticMergeFieldRefDto> targets,
        IReadOnlyList<SemanticMergeConflictResolutionDto> resolutions,
        string? targetSearchText,
        GeneratedChangeSetAuthoringContext authoringContext,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedTargets = NormalizeFieldRefs(targets, expectedRevision.GameFamily);
        if (normalizedTargets.Select(target => target.Record.Domain)
                .Distinct(StringComparer.Ordinal).Count() > SemanticMergeContract.MaximumDomainsPerProposal)
        {
            throw Invalid("A semantic merge proposal supports exactly one workflow domain.");
        }

        var normalizedResolutions = NormalizeResolutions(resolutions);
        var normalizedSearch = NormalizeTargetSearch(targetSearchText);
        if (normalizedTargets.Count > 0 && normalizedSearch is not null)
        {
            throw Invalid("Target search is available only while discovering merge targets.");
        }

        SemanticMergeIndexedLayers layers;
        try
        {
            layers = semanticExploreService.ReadSemanticMergeLayers(
                scope,
                expectedRevision,
                sourceA.InternalId,
                sourceB.InternalId);
        }
        catch (SemanticExploreValidationException exception) when (exception.FailureKind is
            SemanticExploreFailureKind.ExternalRejected
            or SemanticExploreFailureKind.ExternalSnapshotUnavailable)
        {
            ConsumeSourceHandles(sourceA, sourceB);
            throw;
        }
        EnsureSourceSnapshot(layers.SourceASnapshot, sourceA);
        EnsureSourceSnapshot(layers.SourceBSnapshot, sourceB);
        var capabilities = BuildCapabilities(scope.Paths, layers.Revision.GameFamily);
        var supportedDomains = capabilities
            .Where(capability => capability.Feature == SemanticMergeFeatureDto.ThreeWayScalarMerge)
            .SelectMany(capability => capability.Domains)
            .ToDictionary(domain => domain.Domain, StringComparer.Ordinal);
        var allRows = BuildMergeRows(
            layers,
            supportedDomains,
            normalizedTargets,
            normalizedResolutions,
            cancellationToken);
        if (allRows.Count > SemanticMergeContract.MaximumIndexedRows)
        {
            throw LimitExceeded("The semantic merge result exceeds its bounded row limit.");
        }

        var matching = normalizedTargets.Count == 0
            ? ApplyTargetSearch(allRows, normalizedSearch)
            : allRows.Where(row => row.Selected).ToArray();
        var totalMatching = normalizedTargets.Count == 0 ? matching.Count : 0;
        var windowCapped = normalizedTargets.Count == 0
            && matching.Count > SemanticMergeContract.MaximumTargetSelectionWindow;
        var rows = normalizedTargets.Count == 0
            ? matching.Take(SemanticMergeContract.MaximumTargetSelectionWindow).ToArray()
            : matching.ToArray();
        if (normalizedTargets.Count > 0)
        {
            var found = rows.Select(row => FieldRefKey(row.Target)).ToHashSet(StringComparer.Ordinal);
            if (normalizedTargets.Any(target => !found.Contains(FieldRefKey(target))))
            {
                throw Invalid("A selected semantic merge target is unavailable or unchanged.");
            }
        }

        var knownConflictIds = rows.SelectMany(row => row.Conflicts)
            .Select(conflict => conflict.ConflictId)
            .ToHashSet(StringComparer.Ordinal);
        if (normalizedResolutions.Any(resolution => !knownConflictIds.Contains(resolution.ConflictId)))
        {
            throw Invalid("A semantic merge resolution does not belong to the selected proposal.");
        }

        var selectedRows = rows.Where(row => row.Selected).ToArray();
        var mutationRows = selectedRows
            .Where(row => row.State == SemanticMergeRowStateDto.AutoMerged)
            .Where(row => row.PendingValue is not null && row.ResultValue is not null)
            .Where(row => !ValuesEqual(row.PendingValue, row.ResultValue))
            .ToArray();
        var staging = mutationRows.Select(row =>
        {
            if (!TryCanonicalInt(row.ResultValue!, out var value))
            {
                throw Unsupported("A semantic merge result is outside the integer scalar adapter.");
            }

            return (GuidedDesignStagingEdit)new GuidedDesignScalarStagingEdit(
                row.Target.Record,
                row.Target.FieldKey,
                value);
        }).ToArray();
        var staged = staging.Length == 0
            ? new GuidedDesignStagingResult(EditSession.Start(), IsValid: true)
            : stageEdits(scope.Paths, staging);
        var candidatePendingEdits = staged.IsValid
            ? staged.Session.PendingEdits.Select(edit => edit with
            {
                Owner = GeneratedChangeSetOwners.SemanticMerge,
                Summary = SafeGeneratedSummary(edit, "Semantic merge"),
            }).ToArray()
            : Array.Empty<PendingEdit>();
        var stageAccepted = staged.IsValid
            && candidatePendingEdits.Length == mutationRows.Length;
        var pendingEdits = stageAccepted
            ? candidatePendingEdits
            : Array.Empty<PendingEdit>();
        var proposalValidation = stageAccepted && pendingEdits.Length > 0
            ? changeSetService.ValidateGeneratedProposal(
                pendingEdits,
                Planner(scope.Paths),
                authoringContext.OutputMode)
            : new GeneratedChangeSetProposalValidation(false, null, []);
        var unresolvedConflicts = selectedRows
            .SelectMany(row => row.Conflicts)
            .Any(conflict => conflict.SelectedChoice is null);
        var diagnostics = new List<ApiDiagnostic>();
        if (normalizedTargets.Count == 0)
        {
            diagnostics.Add(Diagnostic(
                ApiDiagnosticSeverity.Info,
                SelectionRequiredCode,
                "Select up to 128 exact scalar fields from one workflow domain to create a merge proposal."));
        }

        if (unresolvedConflicts)
        {
            diagnostics.Add(Diagnostic(
                ApiDiagnosticSeverity.Warning,
                ConflictCode,
                "One or more selected semantic fields has an unresolved focused conflict."));
        }

        if (staging.Length > 0 && !stageAccepted)
        {
            var affected = mutationRows.Select(row => FieldRefKey(row.Target))
                .ToHashSet(StringComparer.Ordinal);
            rows = rows.Select(row => affected.Contains(FieldRefKey(row.Target))
                    ? row with
                    {
                        State = SemanticMergeRowStateDto.Unsupported,
                        ResultValue = null,
                        Coverage = SemanticCoverageStateDto.Unavailable,
                        Confidence = SemanticConfidenceDto.Unknown,
                        Conflicts = [ProviderValidationConflict(row.RowId)],
                    }
                    : row)
                .ToArray();
            diagnostics.Add(Diagnostic(
                ApiDiagnosticSeverity.Error,
                MergeProviderValidationCode,
                "The owning workflow rejected one or more selected semantic scalar edits."));
        }

        if (pendingEdits.Length > 0 && !proposalValidation.CanImport)
        {
            diagnostics.Add(Diagnostic(
                ApiDiagnosticSeverity.Error,
                ProposalBlockedCode,
                proposalValidation.Reason
                    ?? "The owning workflow could not rebuild this proposal safely."));
        }

        var canImport = normalizedTargets.Count > 0
            && mutationRows.Length > 0
            && pendingEdits.Length == mutationRows.Length
            && staged.IsValid
            && proposalValidation.CanImport
            && !unresolvedConflicts
            && selectedRows.All(row => row.State is
                SemanticMergeRowStateDto.AutoMerged or SemanticMergeRowStateDto.AlreadyCurrent);
        var proposalId = Hash(
            "semantic-merge-proposal-id-v1",
            layers.Revision.Fingerprint,
            layers.BaseSnapshot.Fingerprint,
            layers.LayeredSnapshot.Fingerprint,
            layers.PendingSnapshot.Fingerprint,
            sourceA.PublicId,
            layers.SourceASnapshot.Fingerprint,
            sourceB.PublicId,
            layers.SourceBSnapshot.Fingerprint,
            authoringContext.Fingerprint,
            normalizedSearch,
            Serialize(normalizedTargets),
            Serialize(normalizedResolutions));
        var proposalFingerprint = Hash(
            "semantic-merge-proposal-fingerprint-v1",
            proposalId,
            Serialize(rows),
            Serialize(pendingEdits.Select(EditSessionBridgeMapper.ToPendingEditDto).ToArray()),
            canImport ? "importable" : "read-only");
        return new MergeGeneratedProposal(
            layers.Revision,
            layers.BaseSnapshot,
            layers.LayeredSnapshot,
            layers.PendingSnapshot,
            layers.SourceASnapshot,
            layers.SourceBSnapshot,
            capabilities,
            normalizedTargets,
            normalizedResolutions,
            normalizedSearch,
            windowCapped,
            totalMatching,
            rows.Length,
            rows.Sum(row => row.Conflicts.Count),
            proposalId,
            proposalFingerprint,
            canImport,
            normalizedTargets.Count == 0,
            rows,
            diagnostics.Take(SemanticMergeContract.MaximumDiagnostics).ToArray(),
            pendingEdits);
    }

    private static IReadOnlyList<SemanticMergeRowDto> BuildMergeRows(
        SemanticMergeIndexedLayers layers,
        IReadOnlyDictionary<string, SemanticMergeDomainCapabilityDto> supportedDomains,
        IReadOnlyList<SemanticMergeFieldRefDto> selectedTargets,
        IReadOnlyList<SemanticMergeConflictResolutionDto> resolutions,
        CancellationToken cancellationToken)
    {
        var selectedKeys = selectedTargets.Select(FieldRefKey).ToHashSet(StringComparer.Ordinal);
        var resolutionById = resolutions.ToDictionary(
            resolution => resolution.ConflictId,
            resolution => resolution.Choice,
            StringComparer.Ordinal);
        var entityKeys = layers.Base.Entities.Keys
            .Union(layers.SourceA.Entities.Keys, StringComparer.Ordinal)
            .Union(layers.SourceB.Entities.Keys, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var rows = new List<SemanticMergeRowDto>();
        foreach (var entityKey in entityKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var baseEntity = layers.Base.Entities.GetValueOrDefault(entityKey);
            var sourceAEntity = layers.SourceA.Entities.GetValueOrDefault(entityKey);
            var sourceBEntity = layers.SourceB.Entities.GetValueOrDefault(entityKey);
            var currentEntity = layers.Layered.Entities.GetValueOrDefault(entityKey);
            var pendingEntity = layers.Pending.Entities.GetValueOrDefault(entityKey);
            var identityEntity = baseEntity ?? sourceAEntity ?? sourceBEntity;
            if (identityEntity is null
                || !supportedDomains.TryGetValue(identityEntity.Record.Domain, out var domain)
                || !string.Equals(
                    identityEntity.Record.RecordKind.Key,
                    domain.RecordKind,
                    StringComparison.Ordinal)
                || !IsEligibleRecord(identityEntity.Record))
            {
                continue;
            }

            foreach (var fieldKey in domain.FieldKeys)
            {
                var baseField = baseEntity?.Fields.GetValueOrDefault(fieldKey);
                var sourceAField = sourceAEntity?.Fields.GetValueOrDefault(fieldKey);
                var sourceBField = sourceBEntity?.Fields.GetValueOrDefault(fieldKey);
                var currentField = currentEntity?.Fields.GetValueOrDefault(fieldKey);
                var pendingField = pendingEntity?.Fields.GetValueOrDefault(fieldKey);
                var changedA = !ValuesEqual(baseField?.Value, sourceAField?.Value);
                var changedB = !ValuesEqual(baseField?.Value, sourceBField?.Value);
                if (!changedA && !changedB)
                {
                    continue;
                }

                var target = new SemanticMergeFieldRefDto(identityEntity.Record, fieldKey);
                var selected = selectedKeys.Contains(FieldRefKey(target));
                rows.Add(BuildMergeRow(
                    target,
                    identityEntity,
                    baseField,
                    sourceAField,
                    sourceBField,
                    currentField,
                    pendingField,
                    selected,
                    resolutionById));
                if (rows.Count > SemanticMergeContract.MaximumIndexedRows)
                {
                    throw LimitExceeded("The semantic merge result exceeds its bounded row limit.");
                }
            }
        }

        return rows
            .OrderBy(row => row.Target.Record.Domain, StringComparer.Ordinal)
            .ThenBy(row => row.Target.Record.RecordKind.Key, StringComparer.Ordinal)
            .ThenBy(row => row.Target.Record.RecordId, SemanticRecordIdComparer.Instance)
            .ThenBy(row => row.Target.Record.SubrecordId, StringComparer.Ordinal)
            .ThenBy(row => row.Target.FieldKey, StringComparer.Ordinal)
            .ToArray();
    }

    private static SemanticMergeRowDto BuildMergeRow(
        SemanticMergeFieldRefDto target,
        SemanticIndexedEntity identityEntity,
        SemanticIndexedField? baseField,
        SemanticIndexedField? sourceAField,
        SemanticIndexedField? sourceBField,
        SemanticIndexedField? currentField,
        SemanticIndexedField? pendingField,
        bool selected,
        IReadOnlyDictionary<string, SemanticMergeConflictChoiceDto> resolutionById)
    {
        var rowId = "merge-row-" + Hash(
            "semantic-merge-row-v1",
            FieldRefKey(target))[..24];
        var providerId = baseField?.OwnerId
            ?? sourceAField?.OwnerId
            ?? sourceBField?.OwnerId
            ?? identityEntity.OwnerId;
        var fieldLabel = baseField?.Label
            ?? sourceAField?.Label
            ?? sourceBField?.Label
            ?? target.FieldKey;
        var fallback = new SemanticMergeFallbackActionDto(
            SemanticMergeFallbackKindDto.Unavailable,
            Target: null,
            Available: false,
            LegacyFallbackUnavailableReason);
        if (baseField is null
            || sourceAField is null
            || sourceBField is null
            || currentField is null
            || pendingField is null)
        {
            var conflict = Conflict(
                rowId,
                SemanticMergeConflictKindDto.IncompatibleLayout,
                [],
                resolutionById,
                "scalar-layout-mismatch");
            return Row(
                rowId,
                target,
                identityEntity,
                fieldLabel,
                SemanticMergeRowStateDto.Unsupported,
                baseField,
                sourceAField,
                sourceBField,
                currentField,
                pendingField,
                result: null,
                providerId,
                [conflict],
                fallback,
                selected);
        }

        var scalarValues = new[]
        {
            baseField.Value,
            sourceAField.Value,
            sourceBField.Value,
            currentField.Value,
            pendingField.Value,
        };
        if (scalarValues.Select(value => value.Kind).Distinct().Count() != 1
            || !IsRecipeScalarKind(scalarValues[0].Kind)
            || scalarValues.Any(value =>
                !TryCanonicalInt(value, out var parsed)
                || value.Kind == SemanticValueKindDto.UnsignedInteger && parsed < 0))
        {
            var conflict = Conflict(
                rowId,
                SemanticMergeConflictKindDto.IncompatibleLayout,
                [],
                resolutionById,
                "unsupported-scalar-kind-or-representation");
            return Row(
                rowId,
                target,
                identityEntity,
                fieldLabel,
                SemanticMergeRowStateDto.Unsupported,
                baseField,
                sourceAField,
                sourceBField,
                currentField,
                pendingField,
                result: null,
                providerId,
                [conflict],
                fallback,
                selected);
        }

        var owners = new[]
        {
            baseField.OwnerId,
            sourceAField.OwnerId,
            sourceBField.OwnerId,
            currentField.OwnerId,
            pendingField.OwnerId,
        };
        if (owners.Distinct(StringComparer.Ordinal).Count() != 1)
        {
            var conflict = Conflict(
                rowId,
                SemanticMergeConflictKindDto.Ownership,
                [],
                resolutionById,
                "semantic-owner-mismatch");
            return Row(
                rowId,
                target,
                identityEntity,
                fieldLabel,
                SemanticMergeRowStateDto.Unsupported,
                baseField,
                sourceAField,
                sourceBField,
                currentField,
                pendingField,
                result: null,
                providerId,
                [conflict],
                fallback,
                selected);
        }

        var conflicts = new List<SemanticMergeConflictDto>(2);
        SemanticScalarValueDto? result;
        var changedA = !ValuesEqual(baseField.Value, sourceAField.Value);
        var changedB = !ValuesEqual(baseField.Value, sourceBField.Value);
        if (changedA && changedB && !ValuesEqual(sourceAField.Value, sourceBField.Value))
        {
            var conflict = Conflict(
                rowId,
                SemanticMergeConflictKindDto.SameField,
                [
                    SemanticMergeConflictChoiceDto.SourceA,
                    SemanticMergeConflictChoiceDto.SourceB,
                    SemanticMergeConflictChoiceDto.Base,
                ],
                resolutionById,
                "divergent-source-values");
            conflicts.Add(conflict);
            result = conflict.SelectedChoice switch
            {
                SemanticMergeConflictChoiceDto.SourceA => sourceAField.Value,
                SemanticMergeConflictChoiceDto.SourceB => sourceBField.Value,
                SemanticMergeConflictChoiceDto.Base => baseField.Value,
                _ => null,
            };
        }
        else
        {
            result = changedA ? sourceAField.Value : sourceBField.Value;
        }

        if (result is null)
        {
            return Row(
                rowId,
                target,
                identityEntity,
                fieldLabel,
                SemanticMergeRowStateDto.Conflict,
                baseField,
                sourceAField,
                sourceBField,
                currentField,
                pendingField,
                result,
                providerId,
                conflicts,
                fallback,
                selected);
        }

        if (!ValuesEqual(currentField.Value, baseField.Value)
            && !ValuesEqual(currentField.Value, result))
        {
            var conflict = Conflict(
                rowId,
                SemanticMergeConflictKindDto.CurrentTarget,
                [SemanticMergeConflictChoiceDto.KeepCurrent],
                resolutionById,
                "current-layer-diverged-from-base");
            conflicts.Add(conflict);
            if (conflict.SelectedChoice == SemanticMergeConflictChoiceDto.KeepCurrent)
            {
                result = currentField.Value;
            }
            else
            {
                result = null;
            }
        }

        if (result is not null
            && !ValuesEqual(pendingField.Value, currentField.Value)
            && !ValuesEqual(pendingField.Value, result))
        {
            var conflict = Conflict(
                rowId,
                SemanticMergeConflictKindDto.PendingTarget,
                [SemanticMergeConflictChoiceDto.KeepCurrent],
                resolutionById,
                "pending-target-diverged-from-layered");
            conflicts.Add(conflict);
            if (conflict.SelectedChoice == SemanticMergeConflictChoiceDto.KeepCurrent)
            {
                result = pendingField.Value;
            }
            else
            {
                result = null;
            }
        }

        if (conflicts.Count > SemanticMergeContract.MaximumConflictsPerRow)
        {
            throw LimitExceeded("A semantic merge row exceeds the focused conflict limit.");
        }

        var state = result is null
            ? SemanticMergeRowStateDto.Conflict
            : ValuesEqual(currentField.Value, result)
                || ValuesEqual(pendingField.Value, result)
                ? SemanticMergeRowStateDto.AlreadyCurrent
                : SemanticMergeRowStateDto.AutoMerged;
        return Row(
            rowId,
            target,
            identityEntity,
            fieldLabel,
            state,
            baseField,
            sourceAField,
            sourceBField,
            currentField,
            pendingField,
            result,
            providerId,
            conflicts,
            fallback,
            selected);
    }

    private static SemanticMergeRowDto Row(
        string rowId,
        SemanticMergeFieldRefDto target,
        SemanticIndexedEntity identityEntity,
        string fieldLabel,
        SemanticMergeRowStateDto state,
        SemanticIndexedField? baseField,
        SemanticIndexedField? sourceAField,
        SemanticIndexedField? sourceBField,
        SemanticIndexedField? currentField,
        SemanticIndexedField? pendingField,
        SemanticScalarValueDto? result,
        string providerId,
        IReadOnlyList<SemanticMergeConflictDto> conflicts,
        SemanticMergeFallbackActionDto fallback,
        bool selected) =>
        new(
            rowId,
            target,
            identityEntity.Title,
            fieldLabel,
            state,
            baseField?.Value,
            sourceAField?.Value,
            sourceBField?.Value,
            currentField?.Value,
            pendingField?.Value,
            result,
            providerId,
            state == SemanticMergeRowStateDto.Unsupported
                ? SemanticCoverageStateDto.Unavailable
                : SemanticCoverageStateDto.Complete,
            state == SemanticMergeRowStateDto.Unsupported
                ? SemanticConfidenceDto.Unknown
                : SemanticConfidenceDto.Verified,
            conflicts,
            fallback,
            selected);

    private static SemanticMergeConflictDto Conflict(
        string rowId,
        SemanticMergeConflictKindDto kind,
        IReadOnlyList<SemanticMergeConflictChoiceDto> allowed,
        IReadOnlyDictionary<string, SemanticMergeConflictChoiceDto> resolutions,
        string reasonCode)
    {
        var id = "merge-conflict-" + Hash(
            "semantic-merge-conflict-v1",
            rowId,
            kind.ToString())[..24];
        SemanticMergeConflictChoiceDto? selected = null;
        if (resolutions.TryGetValue(id, out var choice))
        {
            if (!allowed.Contains(choice))
            {
                throw Invalid("A semantic merge conflict choice is not allowed for its conflict.");
            }

            selected = choice;
        }

        return new SemanticMergeConflictDto(id, kind, allowed, selected, reasonCode);
    }

    private static SemanticMergeConflictDto ProviderValidationConflict(string rowId) =>
        new(
            "merge-conflict-" + Hash(
                "semantic-merge-provider-validation-conflict-v1",
                rowId)[..24],
            SemanticMergeConflictKindDto.IncompatibleLayout,
            [],
            SelectedChoice: null,
            "provider-validation-failed");

    private static IReadOnlyList<SemanticMergeRowDto> ApplyTargetSearch(
        IReadOnlyList<SemanticMergeRowDto> rows,
        string? search)
    {
        if (search is null)
        {
            return rows;
        }

        return rows.Where(row =>
                row.RecordLabel.Contains(search, StringComparison.OrdinalIgnoreCase)
                || row.FieldLabel.Contains(search, StringComparison.OrdinalIgnoreCase)
                || row.Target.FieldKey.Contains(search, StringComparison.OrdinalIgnoreCase)
                || row.Target.Record.RecordId.Contains(search, StringComparison.OrdinalIgnoreCase)
                || row.Target.Record.Domain.Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static bool IsEligibleRecord(SemanticRecordRefDto record)
    {
        if (!int.TryParse(
                record.RecordId,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var id)
            || !string.Equals(
                id.ToString(CultureInfo.InvariantCulture),
                record.RecordId,
                StringComparison.Ordinal))
        {
            return false;
        }

        return record.Domain switch
        {
            "workflow.items" => id > 0,
            "workflow.pokemon" => id > 0,
            "workflow.moves" => id >= 0,
            _ => false,
        };
    }

    public ExportKmRecipeResponse ExportRecipe(
        ExportKmRecipeRequest request,
        CancellationToken cancellationToken = default) =>
        ExportRecipeAsync(request, cancellationToken).GetAwaiter().GetResult();

    public async Task<ExportKmRecipeResponse> ExportRecipeAsync(
        ExportKmRecipeRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRecipeExportRequest(request);
        var game = request.Scope.Paths.SelectedGame!.Value;
        if (ToFamily(game) != SemanticGameFamilyDto.SwordShield)
        {
            throw Unsupported(
                "Recipe export is unavailable for this family until its semantic writer boundary is hardened.");
        }

        var planner = Planner(request.Scope.Paths);
        var workspace = await changeSetService.ObserveGeneratedReviewWorkspaceAsync(
                ToChangeSetScope(request.Scope),
                request.ExpectedChangeSetETag,
                request.Scope.PendingSession,
                cancellationToken)
            .ConfigureAwait(false);

        var layers = semanticExploreService.ReadSemanticRecipeLayers(
            request.Scope,
            request.ExpectedRevision);
        var domains = ReadWritableDomains(request.Scope.Paths)
            .ToDictionary(domain => domain.Domain, StringComparer.Ordinal);
        var selected = ResolveRecipeExportClosure(
            workspace.Document.ChangeSets,
            request.SelectedChangeSetIds);
        var selectedStoredOperations = selected.SelectMany(set => set.Operations).ToArray();
        if (!changeSetService.ValidateStoredOperationBindingsBatch(
                selectedStoredOperations,
                BoundedScalarPlanner(request.Scope.Paths),
                workspace.AuthoringContext.OutputMode))
        {
            throw Invalid(
                "A selected recipe operation is stale, unsupported, or no longer matches its reviewed source binding.");
        }

        var stepIds = selected
            .Select((set, index) => (set.ChangeSetId, StepId: $"step-{index + 1:0000}"))
            .ToDictionary(pair => pair.ChangeSetId, pair => pair.StepId, StringComparer.Ordinal);
        var pendingOperations = new List<ExportOperation>();
        foreach (var (set, stepIndex) in selected.Select((set, index) => (set, index)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var operation in set.Operations)
            {
                pendingOperations.Add(CreateExportOperation(
                    stepIndex,
                    operation,
                    layers,
                    domains));
            }
        }

        if (pendingOperations.Count is 0 or > SemanticMergeContract.MaximumRecipeOperations
            || pendingOperations.Select(operation => operation.Record.Domain)
                .Distinct(StringComparer.Ordinal).Count() != 1
            || pendingOperations.Select(operation => FieldRefKey(
                    new SemanticMergeFieldRefDto(operation.Record, operation.FieldKey)))
                .Distinct(StringComparer.Ordinal).Count() != pendingOperations.Count)
        {
            throw Invalid(
                $"Recipe export requires one through {SemanticMergeContract.MaximumRecipeOperations:N0} unique scalar operations from exactly one workflow domain.");
        }


        var stagingEdits = pendingOperations.Select(operation =>
        {
            if (!TryCanonicalInt(operation.AfterValue.CanonicalValue, out var value))
            {
                throw Invalid("A recipe export value is outside the bounded integer scalar adapter.");
            }

            return (GuidedDesignStagingEdit)new GuidedDesignScalarStagingEdit(
                operation.Record,
                operation.FieldKey,
                value);
        }).ToArray();
        var staged = stageEdits(request.Scope.Paths, stagingEdits);
        var reboundEdits = staged.IsValid
            ? staged.Session.PendingEdits.Select(edit => edit with
            {
                Owner = GeneratedChangeSetOwners.Recipe,
                Summary = SafeGeneratedSummary(edit, "Recipe"),
            }).ToArray()
            : Array.Empty<PendingEdit>();
        var exportValidation = staged.IsValid && reboundEdits.Length == pendingOperations.Count
            ? changeSetService.ValidateGeneratedProposal(
                reboundEdits,
                planner,
                workspace.AuthoringContext.OutputMode)
            : new GeneratedChangeSetProposalValidation(false, null, []);
        if (!exportValidation.CanImport
            || exportValidation.Bindings.Count != pendingOperations.Count
            || pendingOperations.Zip(exportValidation.Bindings).Any(pair =>
                !OwnedTargetsEqual(pair.First.OwnedTargets, pair.Second.OwnedTargets)))
        {
            throw Invalid(
                "A selected recipe operation is stale, unsupported, or no longer owns the same output targets.");
        }

        var completedLayers = semanticExploreService.ReadSemanticRecipeLayers(
            request.Scope,
            request.ExpectedRevision);
        if (!SnapshotsEqual(layers.BaseSnapshot, completedLayers.BaseSnapshot)
            || !SnapshotsEqual(layers.LayeredSnapshot, completedLayers.LayeredSnapshot)
            || !SnapshotsEqual(layers.PendingSnapshot, completedLayers.PendingSnapshot))
        {
            throw new SemanticExploreValidationException(
                "The recipe source changed during export. Retry the export.",
                SemanticExploreFailureKind.StaleRevision);
        }

        var completedWorkspace = await changeSetService.ObserveGeneratedReviewWorkspaceAsync(
                ToChangeSetScope(request.Scope),
                request.ExpectedChangeSetETag,
                request.Scope.PendingSession,
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(
                completedWorkspace.AuthoringContext.Fingerprint,
                workspace.AuthoringContext.Fingerprint,
                StringComparison.Ordinal))
        {
            throw new WorkspaceDocumentConflictException(
                request.ExpectedChangeSetETag,
                completedWorkspace.AuthoringContext.ChangeSetETag);
        }

        var globalOperationIndex = 0;
        var steps = selected.Select((set, stepIndex) =>
        {
            var operations = pendingOperations
                .Where(operation => operation.StepIndex == stepIndex)
                .OrderBy(operation => RecordKey(operation.Record), StringComparer.Ordinal)
                .ThenBy(operation => operation.FieldKey, StringComparer.Ordinal)
                .Select(operation => new KmRecipeOperationDto(
                    $"op-{++globalOperationIndex:000000}",
                    operation.Record,
                    operation.FieldKey,
                    operation.ExpectedBaseValue,
                    operation.ExpectedCurrentValue,
                    operation.AfterValue,
                    operation.ProviderId))
                .ToArray();
            if (operations.Length == 0)
            {
                throw Invalid("Every exported recipe step must contain at least one operation.");
            }

            return new KmRecipeStepDto(
                stepIds[set.ChangeSetId],
                stepIndex,
                set.DependencyIds
                    .Where(stepIds.ContainsKey)
                    .Select(dependencyId => stepIds[dependencyId])
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
                operations);
        }).ToArray();
        var metadata = NormalizeRecipeMetadata(
            new KmRecipeMetadataDto(request.Name, request.Notes, request.Seed));
        var sourceCompatibility = CreateRecipeSourceCompatibility(game, steps);
        var package = new KmRecipePackageDto(
            SemanticMergeContract.SchemaVersion,
            game,
            ProviderSchema,
            sourceCompatibility,
            metadata,
            steps);
        var normalized = ValidateAndNormalizeRecipe(package);
        var content = JsonSerializer.Serialize(normalized, RecipeJsonOptions);
        var bytes = Encoding.UTF8.GetByteCount(content);
        if (bytes > SemanticMergeContract.MaximumRecipeBytes)
        {
            throw LimitExceeded("The canonical recipe exceeds its bounded size limit.");
        }

        var fingerprint = Sha256(content);
        var artifact = new KmRecipeArtifactDto(
            SemanticMergeContract.SchemaVersion,
            "application/vnd.km-editor.recipe+json",
            SuggestedRecipeFileName(metadata.Name),
            fingerprint,
            content);
        return new ExportKmRecipeResponse(
            layers.Revision,
            fingerprint,
            selected.Count,
            pendingOperations.Count,
            artifact,
            []);
    }

    public ValidateKmRecipeResponse ValidateRecipe(ValidateKmRecipeRequest request)
    {
        if (request?.Content is null
            || request.Content.Length == 0
            || Encoding.UTF8.GetByteCount(request.Content) > SemanticMergeContract.MaximumRecipeBytes)
        {
            throw Invalid("The recipe content is empty or exceeds its bounded size limit.");
        }

        KmRecipePackageDto package;
        try
        {
            ValidateNoDuplicateRecipeProperties(request.Content);
            package = JsonSerializer.Deserialize<KmRecipePackageDto>(
                    request.Content,
                    RecipeJsonOptions)
                ?? throw Invalid("The recipe content is empty.");
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new SemanticExploreValidationException(
                "The recipe content is invalid.",
                SemanticExploreFailureKind.InvalidData,
                exception);
        }

        var normalized = ValidateAndNormalizeRecipe(package);
        var canonical = JsonSerializer.Serialize(normalized, RecipeJsonOptions);
        var byteCount = Encoding.UTF8.GetByteCount(canonical);
        var fingerprint = Sha256(canonical);
        var now = DateTimeOffset.UtcNow;
        var instanceId = "recipe-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var entry = new RecipeHandleEntry(
            instanceId,
            fingerprint,
            normalized,
            byteCount,
            now,
            now + HandleTimeToLive);
        lock (recipeHandleSync)
        {
            RemoveExpiredRecipeHandles(now);
            while (recipeHandles.Count >= MaximumRecipeHandles
                   || recipeCacheBytes > MaximumRecipeCacheBytes - byteCount)
            {
                if (recipeHandles.Count == 0)
                {
                    throw LimitExceeded("The recipe exceeds the bounded validation cache budget.");
                }

                var oldest = recipeHandles.Values
                    .OrderBy(candidate => candidate.LastAccessUtc)
                    .ThenBy(candidate => candidate.InstanceId, StringComparer.Ordinal)
                    .First();
                RemoveRecipeHandle(oldest.InstanceId);
            }

            recipeHandles.Add(instanceId, entry);
            recipeCacheBytes = checked(recipeCacheBytes + byteCount);
        }

        return new ValidateKmRecipeResponse(
            instanceId,
            fingerprint,
            normalized.Game,
            normalized.Metadata,
            normalized.Steps.Count,
            normalized.Steps.Sum(step => step.Operations.Count),
            []);
    }

    private ExportOperation CreateExportOperation(
        int stepIndex,
        ChangeSetOperationDto operation,
        SemanticRecipeIndexedLayers layers,
        IReadOnlyDictionary<string, SemanticMergeDomainCapabilityDto> domains)
    {
        if (operation is null
            || operation.Kind != ChangeSetOperationStorageKindDto.LegacyPendingEdit
            || operation.SourceBindingKind != ChangeSetSourceBindingKindDto.ReviewedPlan
            || operation.PendingEdit is null
            || !domains.TryGetValue(operation.PendingEdit.Domain, out var domain)
            || operation.PendingEdit.RecordId is null
            || operation.PendingEdit.Field is null
            || operation.PendingEdit.NewValue is null
            || !domain.FieldKeys.Contains(operation.PendingEdit.Field, StringComparer.Ordinal))
        {
            throw Invalid("A selected change-set operation is outside the recipe scalar adapter.");
        }

        var record = CreateRecordRef(
            layers.Revision.GameFamily,
            domain,
            operation.PendingEdit.RecordId);
        var entityKey = RecordKey(record);
        var baseField = layers.Base.Entities.GetValueOrDefault(entityKey)?.Fields
            .GetValueOrDefault(operation.PendingEdit.Field);
        var currentField = layers.Layered.Entities.GetValueOrDefault(entityKey)?.Fields
            .GetValueOrDefault(operation.PendingEdit.Field);
        if (baseField is null
            || currentField is null
            || !TryCanonicalInt(operation.PendingEdit.NewValue, out var after)
            || !IsRecipeScalarKind(baseField.Value.Kind)
            || !IsRecipeScalarKind(currentField.Value.Kind))
        {
            throw Invalid("A selected change-set operation has no exact scalar recipe baseline.");
        }

        return new ExportOperation(
            stepIndex,
            record,
            operation.PendingEdit.Field,
            ToRecipeScalar(baseField.Value),
            ToRecipeScalar(currentField.Value),
            new KmRecipeScalarDto(
                currentField.Value.Kind,
                after.ToString(CultureInfo.InvariantCulture)),
            currentField.OwnerId,
            operation.OwnedTargets);
    }

    private static IReadOnlyList<NamedChangeSetDto> ResolveRecipeExportClosure(
        IReadOnlyList<NamedChangeSetDto> changeSets,
        IReadOnlyList<string> selectedIds)
    {
        var byId = changeSets.ToDictionary(set => set.ChangeSetId, StringComparer.Ordinal);
        var closure = selectedIds.ToHashSet(StringComparer.Ordinal);
        var pending = new Stack<string>(selectedIds.Reverse());
        while (pending.TryPop(out var id))
        {
            if (!byId.TryGetValue(id, out var set))
            {
                throw Invalid("A selected recipe change set or dependency does not exist.");
            }

            foreach (var dependencyId in set.DependencyIds)
            {
                if (closure.Add(dependencyId))
                {
                    pending.Push(dependencyId);
                }
            }
        }

        if (closure.Count != selectedIds.Count)
        {
            throw Invalid(
                "Recipe export requires the exact dependency-closed change-set selection.");
        }

        var ordered = changeSets.Where(set => closure.Contains(set.ChangeSetId)).ToArray();
        if (ordered.Length is 0 or > SemanticMergeContract.MaximumRecipeSteps)
        {
            throw Invalid("The selected recipe dependency closure is empty or too large.");
        }

        return ordered;
    }

    public PreviewKmRecipeResponse PreviewRecipe(
        PreviewKmRecipeRequest request,
        CancellationToken cancellationToken = default) =>
        PreviewRecipeAsync(request, cancellationToken).GetAwaiter().GetResult();

    public async Task<PreviewKmRecipeResponse> PreviewRecipeAsync(
        PreviewKmRecipeRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRecipePreviewRequest(request);
        var recipe = ResolveRecipeHandle(request.RecipeInstanceId, request.RecipeFingerprint);
        var initialReview = await changeSetService.ObserveGeneratedReviewWorkspaceAsync(
                ToChangeSetScope(request.Scope),
                request.ExpectedChangeSetETag,
                request.Scope.PendingSession,
                cancellationToken)
            .ConfigureAwait(false);
        var initialContext = initialReview.AuthoringContext;
        var generated = GenerateRecipe(
            request.Scope,
            request.ExpectedRevision,
            recipe,
            initialContext,
            cancellationToken);
        var completedReview = await changeSetService.ObserveGeneratedReviewWorkspaceAsync(
                ToChangeSetScope(request.Scope),
                request.ExpectedChangeSetETag,
                request.Scope.PendingSession,
                cancellationToken)
            .ConfigureAwait(false);
        var completedContext = completedReview.AuthoringContext;
        if (!string.Equals(
                initialContext.Fingerprint,
                completedContext.Fingerprint,
                StringComparison.Ordinal))
        {
            throw StaleRecipe("The recipe authoring context changed. Preview it again.");
        }

        if (request.Cursor is not null
            && (!string.Equals(request.ProposalId, generated.ProposalId, StringComparison.Ordinal)
                || !string.Equals(
                    request.ProposalFingerprint,
                    generated.ProposalFingerprint,
                    StringComparison.Ordinal)))
        {
            throw StaleRecipe("The recipe proposal changed before its next page was read.");
        }

        var queryFingerprint = Hash(
            "km-recipe-query-v1",
            generated.ProposalId,
            generated.ProposalFingerprint);
        var offset = DecodeCursor(request.Cursor, queryFingerprint);
        if (offset > generated.Compatibility.Count)
        {
            throw InvalidCursor("The recipe cursor is outside the current compatibility report.");
        }

        var page = generated.Compatibility.Skip(offset).Take(request.Limit).ToArray();
        var nextOffset = checked(offset + request.Limit);
        var nextCursor = nextOffset < generated.Compatibility.Count
            ? EncodeCursor(queryFingerprint, nextOffset)
            : null;
        return new PreviewKmRecipeResponse(
            generated.Revision,
            queryFingerprint,
            generated.BaseSnapshot,
            generated.LayeredSnapshot,
            generated.PendingSnapshot,
            recipe.Package.Metadata,
            recipe.InstanceId,
            recipe.Fingerprint,
            completedContext.Fingerprint,
            generated.ProposalId,
            generated.ProposalFingerprint,
            generated.CanImport,
            generated.Compatibility.Count,
            generated.PendingEdits.Count,
            page,
            generated.Diagnostics,
            nextCursor);
    }

    public ImportKmRecipeResponse ImportRecipe(
        ImportKmRecipeRequest request,
        CancellationToken cancellationToken = default) =>
        ImportRecipeAsync(request, cancellationToken).GetAwaiter().GetResult();

    public async Task<ImportKmRecipeResponse> ImportRecipeAsync(
        ImportKmRecipeRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRecipeImportRequest(request);
        var recipe = ResolveRecipeHandle(request.RecipeInstanceId, request.RecipeFingerprint);
        RecipeGeneratedProposal? regenerated = null;
        var planner = Planner(request.Scope.Paths);
        var result = await changeSetService.ImportGeneratedReviewProposalAsync(
                new GeneratedChangeSetImportRequest(
                    ToChangeSetScope(request.Scope),
                    request.ChangeSetName,
                    GeneratedChangeSetOwners.Recipe,
                    "recipe",
                    "Import semantic recipe",
                    request.ExpectedChangeSetETag,
                    request.Scope.PendingSession),
                (context, token) =>
                {
                    var candidate = GenerateRecipe(
                        request.Scope,
                        request.ExpectedRevision,
                        recipe,
                        context,
                        token);
                    if (!candidate.CanImport
                        || !string.Equals(candidate.ProposalId, request.ProposalId, StringComparison.Ordinal)
                        || !string.Equals(
                            candidate.ProposalFingerprint,
                            request.ProposalFingerprint,
                            StringComparison.Ordinal))
                    {
                        throw StaleRecipe(
                            "The recipe proposal is stale or no longer safe to import.");
                    }

                    regenerated = candidate;
                    return Task.FromResult(new GeneratedChangeSetProposal(
                        candidate.ProposalId,
                        candidate.ProposalFingerprint,
                        candidate.PendingEdits));
                },
                planner,
                cancellationToken)
            .ConfigureAwait(false);
        var proposal = regenerated
            ?? throw StaleRecipe("The recipe proposal could not be regenerated.");
        RemoveRecipeHandleThreadSafe(recipe.InstanceId);
        return new ImportKmRecipeResponse(
            proposal.Revision,
            recipe.InstanceId,
            recipe.Fingerprint,
            proposal.ProposalId,
            proposal.ProposalFingerprint,
            result.ImportedChangeSetId,
            ToReceipt(result),
            proposal.Diagnostics);
    }

    private RecipeGeneratedProposal GenerateRecipe(
        SemanticExploreScopeDto scope,
        SemanticProjectRevisionDto expectedRevision,
        RecipeHandleEntry recipe,
        GeneratedChangeSetAuthoringContext authoringContext,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (recipe.Package.Game != scope.Paths.SelectedGame
            || ToFamily(recipe.Package.Game) != SemanticGameFamilyDto.SwordShield)
        {
            throw Unsupported("The recipe game does not match this supported project family.");
        }

        var layers = semanticExploreService.ReadSemanticRecipeLayers(scope, expectedRevision);
        var domains = ReadWritableDomains(scope.Paths)
            .ToDictionary(domain => domain.Domain, StringComparer.Ordinal);
        var rows = new List<KmRecipeCompatibilityRowDto>();
        var staging = new List<GuidedDesignStagingEdit>();
        foreach (var operation in recipe.Package.Steps
                     .OrderBy(step => step.Order)
                     .SelectMany(step => step.Operations))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = new SemanticMergeFieldRefDto(operation.Record, operation.FieldKey);
            var entityKey = RecordKey(operation.Record);
            var baseField = layers.Base.Entities.GetValueOrDefault(entityKey)?.Fields
                .GetValueOrDefault(operation.FieldKey);
            var currentField = layers.Layered.Entities.GetValueOrDefault(entityKey)?.Fields
                .GetValueOrDefault(operation.FieldKey);
            var pendingField = layers.Pending.Entities.GetValueOrDefault(entityKey)?.Fields
                .GetValueOrDefault(operation.FieldKey);
            var state = KmRecipeCompatibilityStateDto.Compatible;
            string? reasonCode = null;
            if (!domains.TryGetValue(operation.Record.Domain, out var domain)
                || !domain.FieldKeys.Contains(operation.FieldKey, StringComparer.Ordinal)
                || baseField is null
                || currentField is null
                || pendingField is null
                || !string.Equals(currentField.OwnerId, operation.ProviderId, StringComparison.Ordinal))
            {
                state = KmRecipeCompatibilityStateDto.Unsupported;
                reasonCode = "recipe-provider-field-unavailable";
            }
            else if (!RecipeValueEquals(operation.ExpectedBaseValue, baseField.Value))
            {
                state = KmRecipeCompatibilityStateDto.Conflict;
                reasonCode = "recipe-base-preimage-mismatch";
            }
            else if (!ValuesEqual(pendingField.Value, currentField.Value)
                     && RecipeValueEquals(operation.AfterValue, pendingField.Value))
            {
                state = KmRecipeCompatibilityStateDto.AlreadyApplied;
            }
            else if (!ValuesEqual(pendingField.Value, currentField.Value))
            {
                state = KmRecipeCompatibilityStateDto.Conflict;
                reasonCode = "recipe-pending-target-diverged";
            }
            else if (RecipeValueEquals(operation.AfterValue, currentField.Value))
            {
                state = KmRecipeCompatibilityStateDto.AlreadyApplied;
            }
            else if (!RecipeValueEquals(operation.ExpectedCurrentValue, currentField.Value))
            {
                state = KmRecipeCompatibilityStateDto.Conflict;
                reasonCode = "recipe-current-preimage-mismatch";
            }
            else if (!TryCanonicalInt(operation.AfterValue.CanonicalValue, out var value))
            {
                state = KmRecipeCompatibilityStateDto.Unsupported;
                reasonCode = "recipe-scalar-value-unsupported";
            }
            else
            {
                staging.Add(new GuidedDesignScalarStagingEdit(
                    operation.Record,
                    operation.FieldKey,
                    value));
            }

            rows.Add(new KmRecipeCompatibilityRowDto(
                "recipe-row-" + operation.OperationId[3..],
                target,
                state,
                operation.ExpectedBaseValue,
                operation.ExpectedCurrentValue,
                baseField?.Value,
                currentField?.Value,
                pendingField?.Value,
                operation.AfterValue,
                operation.ProviderId,
                reasonCode));
        }

        var staged = staging.Count == 0
            ? new GuidedDesignStagingResult(EditSession.Start(), IsValid: true)
            : stageEdits(scope.Paths, staging);
        var candidatePendingEdits = staged.IsValid
            ? staged.Session.PendingEdits.Select(edit => edit with
            {
                Owner = GeneratedChangeSetOwners.Recipe,
                Summary = SafeGeneratedSummary(edit, "Recipe"),
            }).ToArray()
            : Array.Empty<PendingEdit>();
        var stageAccepted = staged.IsValid
            && candidatePendingEdits.Length == staging.Count;
        var pendingEdits = stageAccepted
            ? candidatePendingEdits
            : Array.Empty<PendingEdit>();
        var validation = stageAccepted && pendingEdits.Length > 0
            ? changeSetService.ValidateGeneratedProposal(
                pendingEdits,
                Planner(scope.Paths),
                authoringContext.OutputMode)
            : new GeneratedChangeSetProposalValidation(false, null, []);
        var diagnostics = new List<ApiDiagnostic>();
        if (staging.Count > 0 && !stageAccepted)
        {
            for (var index = 0; index < rows.Count; index++)
            {
                if (rows[index].State == KmRecipeCompatibilityStateDto.Compatible)
                {
                    rows[index] = rows[index] with
                    {
                        State = KmRecipeCompatibilityStateDto.Unsupported,
                        ReasonCode = "recipe-provider-validation-failed",
                    };
                }
            }

            diagnostics.Add(Diagnostic(
                ApiDiagnosticSeverity.Error,
                RecipeProviderValidationCode,
                "The owning workflow rejected one or more compatible recipe scalar edits."));
        }

        if (rows.Any(row => row.State is
                KmRecipeCompatibilityStateDto.Conflict or KmRecipeCompatibilityStateDto.Unsupported))
        {
            diagnostics.Add(Diagnostic(
                ApiDiagnosticSeverity.Warning,
                RecipeBlockedCode,
                "The recipe has incompatible scalar preimages or unavailable provider fields."));
        }

        if (pendingEdits.Length > 0 && !validation.CanImport)
        {
            diagnostics.Add(Diagnostic(
                ApiDiagnosticSeverity.Error,
                ProposalBlockedCode,
                validation.Reason ?? "The recipe could not be rebuilt by its owning workflow."));
        }

        var canImport = staging.Count > 0
            && staged.IsValid
            && pendingEdits.Length == staging.Count
            && validation.CanImport
            && rows.All(row => row.State is
                KmRecipeCompatibilityStateDto.Compatible
                or KmRecipeCompatibilityStateDto.AlreadyApplied);
        var proposalId = Hash(
            "km-recipe-proposal-id-v1",
            recipe.InstanceId,
            recipe.Fingerprint,
            layers.Revision.Fingerprint,
            layers.BaseSnapshot.Fingerprint,
            layers.LayeredSnapshot.Fingerprint,
            layers.PendingSnapshot.Fingerprint,
            authoringContext.Fingerprint);
        var proposalFingerprint = Hash(
            "km-recipe-proposal-fingerprint-v1",
            proposalId,
            Serialize(rows),
            Serialize(pendingEdits.Select(EditSessionBridgeMapper.ToPendingEditDto).ToArray()),
            canImport ? "importable" : "read-only");
        return new RecipeGeneratedProposal(
            layers.Revision,
            layers.BaseSnapshot,
            layers.LayeredSnapshot,
            layers.PendingSnapshot,
            proposalId,
            proposalFingerprint,
            canImport,
            rows,
            diagnostics.Take(SemanticMergeContract.MaximumDiagnostics).ToArray(),
            pendingEdits);
    }

    private static KmRecipePackageDto ValidateAndNormalizeRecipe(KmRecipePackageDto package)
    {
        if (package is null
            || package.Metadata is null
            || package.Steps is null
            || package.SchemaVersion != SemanticMergeContract.SchemaVersion
            || package.Game is not (ProjectGameDto.Sword or ProjectGameDto.Shield)
            || !string.Equals(package.ProviderSchema, ProviderSchema, StringComparison.Ordinal)
            || package.Steps.Count is 0 or > SemanticMergeContract.MaximumRecipeSteps
            || package.Steps.Any(step => step is null
                || step.DependencyStepIds is null
                || step.Operations is null
                || step.Operations.Count == 0)
            || package.Steps.Sum(step => step.Operations.Count) is 0
                or > SemanticMergeContract.MaximumRecipeOperations)
        {
            throw Invalid("The recipe schema, game, steps, or operation boundary is unsupported.");
        }

        var metadata = NormalizeRecipeMetadata(package.Metadata);
        var stepIds = new HashSet<string>(StringComparer.Ordinal);
        var operationIds = new HashSet<string>(StringComparer.Ordinal);
        var targetKeys = new HashSet<string>(StringComparer.Ordinal);
        string? recipeDomain = null;
        var normalizedSteps = new List<KmRecipeStepDto>(package.Steps.Count);
        var expectedOperationIndex = 0;
        for (var stepIndex = 0; stepIndex < package.Steps.Count; stepIndex++)
        {
            var step = package.Steps[stepIndex];
            var expectedStepId = $"step-{stepIndex + 1:0000}";
            if (step.Order != stepIndex
                || !string.Equals(step.StepId, expectedStepId, StringComparison.Ordinal)
                || !stepIds.Add(step.StepId)
                || step.DependencyStepIds.Count > SemanticMergeContract.MaximumRecipeDependencies
                || step.DependencyStepIds.Distinct(StringComparer.Ordinal).Count()
                    != step.DependencyStepIds.Count
                || !step.DependencyStepIds.SequenceEqual(
                    step.DependencyStepIds.Order(StringComparer.Ordinal),
                    StringComparer.Ordinal)
                || step.DependencyStepIds.Any(dependency => !stepIds.Contains(dependency)))
            {
                throw Invalid("The recipe step order, identity, or dependency order is invalid.");
            }

            var normalizedOperations = new List<KmRecipeOperationDto>(step.Operations.Count);
            string? previousTargetKey = null;
            foreach (var operation in step.Operations)
            {
                expectedOperationIndex++;
                var expectedOperationId = $"op-{expectedOperationIndex:000000}";
                if (operation is null
                    || operation.Record is null
                    || operation.Record.RecordKind is null
                    || operation.ExpectedBaseValue is null
                    || operation.ExpectedCurrentValue is null
                    || operation.AfterValue is null
                    || !string.Equals(operation.OperationId, expectedOperationId, StringComparison.Ordinal)
                    || !operationIds.Add(operation.OperationId))
                {
                    throw Invalid("A recipe operation identity or payload is invalid.");
                }

                ValidateRecipeRecord(operation.Record, package.Game);
                ValidateFieldKey(operation.FieldKey);
                var expectedProvider = operation.Record.Domain switch
                {
                    "workflow.items" => "swsh.items.semantic",
                    "workflow.pokemon" => "swsh.pokemon.semantic",
                    "workflow.moves" => "swsh.moves.semantic",
                    _ => null,
                };
                if (expectedProvider is null
                    || !string.Equals(operation.ProviderId, expectedProvider, StringComparison.Ordinal)
                    || operation.ExpectedBaseValue.Kind != operation.ExpectedCurrentValue.Kind
                    || operation.ExpectedBaseValue.Kind != operation.AfterValue.Kind)
                {
                    throw Invalid("A recipe operation provider or scalar kind is unsupported.");
                }

                ValidateRecipeScalar(operation.ExpectedBaseValue);
                ValidateRecipeScalar(operation.ExpectedCurrentValue);
                ValidateRecipeScalar(operation.AfterValue);
                recipeDomain ??= operation.Record.Domain;
                if (!string.Equals(recipeDomain, operation.Record.Domain, StringComparison.Ordinal))
                {
                    throw Invalid("A recipe supports exactly one workflow domain.");
                }

                var targetKey = FieldRefKey(new SemanticMergeFieldRefDto(
                    operation.Record,
                    operation.FieldKey));
                if (!targetKeys.Add(targetKey)
                    || previousTargetKey is not null
                    && StringComparer.Ordinal.Compare(previousTargetKey, targetKey) >= 0)
                {
                    throw Invalid("Recipe operations must be unique and canonically ordered within each step.");
                }

                previousTargetKey = targetKey;
                normalizedOperations.Add(operation);
            }

            normalizedSteps.Add(step with
            {
                DependencyStepIds = step.DependencyStepIds.ToArray(),
                Operations = normalizedOperations,
            });
        }

        var compatibility = CreateRecipeSourceCompatibility(package.Game, normalizedSteps);
        if (!IsSha256(package.SourceCompatibilityFingerprint)
            || !string.Equals(
                package.SourceCompatibilityFingerprint,
                compatibility,
                StringComparison.Ordinal))
        {
            throw Invalid("The recipe source compatibility commitment is invalid.");
        }

        return package with
        {
            SourceCompatibilityFingerprint = compatibility,
            Metadata = metadata,
            Steps = normalizedSteps,
        };
    }

    private static KmRecipeMetadataDto NormalizeRecipeMetadata(KmRecipeMetadataDto metadata)
    {
        var name = RequireCanonicalText(
            metadata.Name,
            "recipe name",
            SemanticMergeContract.MaximumRecipeNameLength,
            allowEmpty: false);
        var notes = metadata.Notes is null
            ? null
            : RequireCanonicalText(
                metadata.Notes,
                "recipe notes",
                SemanticMergeContract.MaximumRecipeNotesLength,
                allowEmpty: false);
        if (!GuidedDesignProviders.IsSafeGeneratedDisplayText(name)
            || notes is not null
            && !GuidedDesignProviders.IsSafeGeneratedDisplayText(notes))
        {
            throw Invalid("Recipe metadata must not contain path-shaped or unsafe text.");
        }

        if (metadata.Seed is not null)
        {
            throw Invalid(
                "Recipe seeds are unavailable until a provider-owned deterministic generation contract exists.");
        }

        return new KmRecipeMetadataDto(name, notes, Seed: null);
    }

    private static void ValidateRecipeRecord(SemanticRecordRefDto record, ProjectGameDto game)
    {
        var family = ToFamily(game);
        var expectedKind = record.Domain switch
        {
            "workflow.items" => "item",
            "workflow.pokemon" => "pokemon-personal",
            "workflow.moves" => "move",
            _ => null,
        };
        if (record.GameFamily != family
            || expectedKind is null
            || !string.Equals(record.RecordKind.Key, expectedKind, StringComparison.Ordinal)
            || record.RecordKind.SchemaVersion != 1
            || record.SubrecordId is not null
            || !IsEligibleRecord(record))
        {
            throw Invalid("A recipe semantic record identity is unsupported.");
        }
    }

    private static void ValidateRecipeScalar(KmRecipeScalarDto scalar)
    {
        if (!IsRecipeScalarKind(scalar.Kind)
            || !TryCanonicalInt(scalar.CanonicalValue, out var value)
            || scalar.Kind == SemanticValueKindDto.UnsignedInteger && value < 0)
        {
            throw Invalid("A recipe scalar must be a canonical bounded integer or enum value.");
        }
    }

    private static bool IsRecipeScalarKind(SemanticValueKindDto kind) => kind is
        SemanticValueKindDto.SignedInteger
        or SemanticValueKindDto.UnsignedInteger
        or SemanticValueKindDto.Enum;

    private static string CreateRecipeSourceCompatibility(
        ProjectGameDto game,
        IReadOnlyList<KmRecipeStepDto> steps)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHash(hash, "km-recipe-source-compatibility-v1");
        AppendHash(hash, SemanticMergeContract.SchemaVersion.ToString(CultureInfo.InvariantCulture));
        AppendHash(hash, game.ToString());
        AppendHash(hash, ProviderSchema);
        AppendHash(hash, RecipeProviderId);
        AppendHash(hash, steps
            .SelectMany(step => step.Operations)
            .Select(operation => operation.Record.Domain)
            .Distinct(StringComparer.Ordinal)
            .Single());
        foreach (var step in steps.OrderBy(step => step.Order))
        {
            AppendHash(hash, step.StepId);
            foreach (var dependency in step.DependencyStepIds)
            {
                AppendHash(hash, dependency);
            }

            AppendHash(hash, null);
            foreach (var operation in step.Operations)
            {
                AppendHash(hash, RecordKey(operation.Record));
                AppendHash(hash, operation.FieldKey);
                AppendHash(hash, operation.ProviderId);
                AppendHash(hash, operation.ExpectedBaseValue.Kind.ToString());
                AppendHash(hash, operation.ExpectedBaseValue.CanonicalValue);
                AppendHash(hash, operation.ExpectedCurrentValue.Kind.ToString());
                AppendHash(hash, operation.ExpectedCurrentValue.CanonicalValue);
            }

            AppendHash(hash, null);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void ValidatePreviewRequest(PreviewSemanticMergeRequest request)
    {
        if (request?.Scope?.Paths is null
            || request.ExpectedRevision is null
            || request.SourceAInstanceId is null
            || request.SourceBInstanceId is null
            || request.Targets is null
            || request.Resolutions is null
            || request.Targets.Count > SemanticMergeContract.MaximumTargets
            || request.Resolutions.Count > SemanticMergeContract.MaximumResolutions
            || request.Targets.Any(target => target is null || target.Record is null)
            || request.Resolutions.Any(resolution => resolution is null)
            || request.Limit is <= 0 or > SemanticMergeContract.MaximumPageSize
            || request.Cursor is { Length: > SemanticMergeContract.MaximumCursorLength }
            || request.Cursor is null && (request.ProposalId is not null || request.ProposalFingerprint is not null)
            || request.Cursor is not null
                && (!IsSha256(request.ProposalId) || !IsSha256(request.ProposalFingerprint)))
        {
            throw Invalid("The semantic merge preview request is malformed or exceeds its bounds.");
        }
    }

    private static void ValidateImportRequest(ImportSemanticMergeRequest request)
    {
        if (request?.Scope?.Paths is null
            || request.ExpectedRevision is null
            || request.SourceAInstanceId is null
            || request.SourceBInstanceId is null
            || request.Targets is null
            || request.Resolutions is null
            || request.Targets.Count is 0 or > SemanticMergeContract.MaximumTargets
            || request.Resolutions.Count > SemanticMergeContract.MaximumResolutions
            || request.Targets.Any(target => target is null || target.Record is null)
            || request.Resolutions.Any(resolution => resolution is null)
            || !IsSha256(request.ProposalId)
            || !IsSha256(request.ProposalFingerprint))
        {
            throw Invalid("The semantic merge import request is malformed or exceeds its bounds.");
        }

        RequireCanonicalText(
            request.ChangeSetName,
            "semantic merge change-set name",
            SemanticMergeContract.MaximumChangeSetNameLength,
            allowEmpty: false);
    }

    private static void ValidateRecipeExportRequest(ExportKmRecipeRequest request)
    {
        if (request?.Scope?.Paths is null
            || request.ExpectedRevision is null
            || request.Scope.Paths.SelectedGame is null
            || !IsSha256(request.ExpectedChangeSetETag)
            || request.SelectedChangeSetIds is null
            || request.SelectedChangeSetIds.Count is 0 or > SemanticMergeContract.MaximumRecipeSteps
            || request.SelectedChangeSetIds.Any(id => string.IsNullOrEmpty(id))
            || request.SelectedChangeSetIds.Distinct(StringComparer.Ordinal).Count()
                != request.SelectedChangeSetIds.Count)
        {
            throw Invalid("The recipe export request is malformed or exceeds its bounds.");
        }

        _ = NormalizeRecipeMetadata(new KmRecipeMetadataDto(
            request.Name,
            request.Notes,
            request.Seed));
    }

    private static void ValidateRecipePreviewRequest(PreviewKmRecipeRequest request)
    {
        if (request?.Scope?.Paths is null
            || request.ExpectedRevision is null
            || !IsRecipeInstanceId(request.RecipeInstanceId)
            || !IsSha256(request.RecipeFingerprint)
            || request.Limit is <= 0 or > SemanticMergeContract.MaximumPageSize
            || request.Cursor is { Length: > SemanticMergeContract.MaximumCursorLength }
            || request.Cursor is null && (request.ProposalId is not null || request.ProposalFingerprint is not null)
            || request.Cursor is not null
                && (!IsSha256(request.ProposalId) || !IsSha256(request.ProposalFingerprint)))
        {
            throw Invalid("The recipe preview request is malformed or exceeds its bounds.");
        }
    }

    private static void ValidateRecipeImportRequest(ImportKmRecipeRequest request)
    {
        if (request?.Scope?.Paths is null
            || request.ExpectedRevision is null
            || !IsRecipeInstanceId(request.RecipeInstanceId)
            || !IsSha256(request.RecipeFingerprint)
            || !IsSha256(request.ProposalId)
            || !IsSha256(request.ProposalFingerprint))
        {
            throw Invalid("The recipe import request is malformed.");
        }

        RequireCanonicalText(
            request.ChangeSetName,
            "recipe change-set name",
            SemanticMergeContract.MaximumChangeSetNameLength,
            allowEmpty: false);
    }

    private static IReadOnlyList<SemanticMergeFieldRefDto> NormalizeFieldRefs(
        IReadOnlyList<SemanticMergeFieldRefDto> targets,
        SemanticGameFamilyDto family)
    {
        var normalized = targets.Select(target =>
        {
            ValidateFieldRef(target, family);
            return target;
        }).OrderBy(FieldRefKey, StringComparer.Ordinal).ToArray();
        if (normalized.Select(FieldRefKey).Distinct(StringComparer.Ordinal).Count() != normalized.Length)
        {
            throw Invalid("A semantic merge target is duplicated.");
        }

        return normalized;
    }

    private static IReadOnlyList<SemanticMergeConflictResolutionDto> NormalizeResolutions(
        IReadOnlyList<SemanticMergeConflictResolutionDto> resolutions)
    {
        var normalized = resolutions.Select(resolution =>
        {
            if (!Enum.IsDefined(resolution.Choice)
                || !IsStableId(resolution.ConflictId, 64))
            {
                throw Invalid("A semantic merge conflict resolution is invalid.");
            }

            return resolution;
        }).OrderBy(resolution => resolution.ConflictId, StringComparer.Ordinal).ToArray();
        if (normalized.Select(resolution => resolution.ConflictId)
            .Distinct(StringComparer.Ordinal).Count() != normalized.Length)
        {
            throw Invalid("A semantic merge conflict is resolved more than once.");
        }

        return normalized;
    }

    private static void ValidateFieldRef(
        SemanticMergeFieldRefDto target,
        SemanticGameFamilyDto family)
    {
        var record = target.Record;
        var expectedKind = record.Domain switch
        {
            "workflow.items" => "item",
            "workflow.pokemon" => "pokemon-personal",
            "workflow.moves" => "move",
            _ => null,
        };
        if (record.RecordKind is null
            || record.GameFamily != family
            || expectedKind is null
            || !string.Equals(record.RecordKind.Key, expectedKind, StringComparison.Ordinal)
            || record.RecordKind.SchemaVersion != 1
            || record.SubrecordId is not null
            || !IsEligibleRecord(record))
        {
            throw Invalid("A semantic merge target record is invalid or unsupported.");
        }

        ValidateFieldKey(target.FieldKey);
    }

    private static void ValidateFieldKey(string fieldKey)
    {
        if (fieldKey is null
            || fieldKey.Length is < 1 or > 128
            || fieldKey[0] is < 'a' or > 'z'
            || fieldKey.Any(character => !char.IsAsciiLetterOrDigit(character)))
        {
            throw Invalid("A semantic scalar field key is invalid.");
        }
    }

    private static string? NormalizeTargetSearch(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value.Length == 0
            || value.Length > SemanticMergeContract.MaximumTargetSearchTextLength
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || !value.IsNormalized(NormalizationForm.FormC)
            || value.Any(IsUnsafeUnicode))
        {
            throw Invalid("The semantic merge target search is not canonical or exceeds its bounds.");
        }

        return value;
    }

    private SourceHandleEntry ResolveSourceHandle(
        string instanceId,
        SemanticExploreScopeDto scope,
        SemanticProjectRevisionDto revision)
    {
        if (!IsMergeSourceId(instanceId))
        {
            throw ExternalSnapshotUnavailable("The semantic merge source handle is invalid.");
        }

        var now = DateTimeOffset.UtcNow;
        SourceHandleEntry? expired = null;
        SourceHandleEntry result;
        lock (sourceHandleSync)
        {
            if (!sourceHandles.TryGetValue(instanceId, out var entry))
            {
                throw ExternalSnapshotUnavailable(
                    "The semantic merge source handle is unavailable. Select the source again.");
            }

            if (entry.ExpiresAtUtc <= now)
            {
                sourceHandles.Remove(instanceId);
                expired = entry;
                result = entry;
            }
            else
            {
                if (!RevisionsEqual(entry.Revision, revision)
                    || !string.Equals(entry.Revision.ProjectId, scope.ProjectId, StringComparison.Ordinal))
                {
                    throw ExternalSnapshotUnavailable(
                        "The semantic merge source handle belongs to a different project revision.");
                }

                result = entry with { LastAccessUtc = now };
                sourceHandles[instanceId] = result;
            }
        }

        if (expired is not null)
        {
            ReleaseSourceEntries([expired]);
            throw ExternalSnapshotUnavailable(
                "The semantic merge source handle expired. Select the source again.");
        }

        return result;
    }

    private RecipeHandleEntry ResolveRecipeHandle(string instanceId, string fingerprint)
    {
        if (!IsRecipeInstanceId(instanceId) || !IsSha256(fingerprint))
        {
            throw Invalid("The recipe handle or fingerprint is invalid.");
        }

        var now = DateTimeOffset.UtcNow;
        lock (recipeHandleSync)
        {
            RemoveExpiredRecipeHandles(now);
            if (!recipeHandles.TryGetValue(instanceId, out var entry)
                || !string.Equals(entry.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                throw new SemanticExploreValidationException(
                    "The validated recipe is unavailable. Validate its content again.",
                    SemanticExploreFailureKind.ExternalSnapshotUnavailable);
            }

            var updated = entry with { LastAccessUtc = now };
            recipeHandles[instanceId] = updated;
            return updated;
        }
    }

    private List<SourceHandleEntry> RemoveExpiredSourceHandlesCore(DateTimeOffset now)
    {
        var expired = sourceHandles.Values.Where(entry => entry.ExpiresAtUtc <= now).ToArray();
        foreach (var entry in expired)
        {
            sourceHandles.Remove(entry.PublicId);
        }

        return expired.ToList();
    }

    private void ConsumeSourceHandles(params SourceHandleEntry[] entries)
    {
        var removed = new List<SourceHandleEntry>();
        lock (sourceHandleSync)
        {
            foreach (var entry in entries.DistinctBy(entry => entry.PublicId))
            {
                if (sourceHandles.Remove(entry.PublicId, out var stored))
                {
                    removed.Add(stored);
                }
            }
        }

        ReleaseSourceEntries(removed);
    }

    private void ReleaseSourceEntries(IEnumerable<SourceHandleEntry> entries)
    {
        foreach (var entry in entries)
        {
            semanticExploreService.ReleaseSemanticMergeSource(entry.Revision, entry.InternalId);
        }
    }

    private void RemoveExpiredRecipeHandles(DateTimeOffset now)
    {
        foreach (var id in recipeHandles.Values
                     .Where(entry => entry.ExpiresAtUtc <= now)
                     .Select(entry => entry.InstanceId)
                     .ToArray())
        {
            RemoveRecipeHandle(id);
        }
    }

    private void RemoveRecipeHandleThreadSafe(string instanceId)
    {
        lock (recipeHandleSync)
        {
            RemoveRecipeHandle(instanceId);
        }
    }

    private void RemoveRecipeHandle(string instanceId)
    {
        if (recipeHandles.Remove(instanceId, out var removed))
        {
            recipeCacheBytes = checked(recipeCacheBytes - removed.SizeBytes);
        }
    }

    private static string SafeGeneratedSummary(PendingEdit edit, string prefix)
    {
        var recordId = edit.RecordId ?? "record";
        var field = edit.Field ?? "field";
        var summary = $"{prefix}: {edit.Domain} {recordId} {field}";
        return GuidedDesignProviders.IsSafeGeneratedDisplayText(summary)
            ? summary
            : $"{prefix}: semantic scalar update";
    }

    private static SemanticSourceSnapshotDto PublicSnapshot(
        SemanticSourceSnapshotDto snapshot,
        SourceHandleEntry handle) =>
        snapshot with { Layer = snapshot.Layer with { InstanceId = handle.PublicId } };

    private static void EnsureSourceSnapshot(
        SemanticSourceSnapshotDto snapshot,
        SourceHandleEntry handle)
    {
        if (!string.Equals(
                snapshot.Layer.InstanceId,
                handle.InternalId,
                StringComparison.Ordinal)
            || !string.Equals(
                snapshot.Fingerprint,
                handle.SnapshotFingerprint,
                StringComparison.Ordinal))
        {
            throw ExternalSnapshotUnavailable(
                "The semantic merge source snapshot changed. Select the source again.");
        }
    }

    private static IReadOnlyList<SemanticMergeFieldRefDto> EmptyFieldRefs =>
        Array.Empty<SemanticMergeFieldRefDto>();

    private static bool FieldRefsMatch(
        IReadOnlyList<SemanticMergeFieldRefDto> left,
        IReadOnlyList<SemanticMergeFieldRefDto> right) =>
        left.Count == right.Count
        && left.Select(FieldRefKey).SequenceEqual(right.Select(FieldRefKey), StringComparer.Ordinal);

    private static bool ResolutionsMatch(
        IReadOnlyList<SemanticMergeConflictResolutionDto> left,
        IReadOnlyList<SemanticMergeConflictResolutionDto> right) =>
        left.Count == right.Count
        && left.Zip(right).All(pair =>
            string.Equals(pair.First.ConflictId, pair.Second.ConflictId, StringComparison.Ordinal)
            && pair.First.Choice == pair.Second.Choice);

    private static bool ValuesEqual(SemanticScalarValueDto? left, SemanticScalarValueDto? right) =>
        left is null && right is null
        || left is not null
        && right is not null
        && left.Kind == right.Kind
        && string.Equals(left.CanonicalValue, right.CanonicalValue, StringComparison.Ordinal);

    private static bool RecipeValueEquals(KmRecipeScalarDto recipe, SemanticScalarValueDto value) =>
        recipe.Kind == value.Kind
        && string.Equals(recipe.CanonicalValue, value.CanonicalValue, StringComparison.Ordinal);

    private static KmRecipeScalarDto ToRecipeScalar(SemanticScalarValueDto value)
    {
        if (!IsRecipeScalarKind(value.Kind) || value.CanonicalValue is null)
        {
            throw Invalid("A semantic value is outside the recipe scalar adapter.");
        }

        return new KmRecipeScalarDto(value.Kind, value.CanonicalValue);
    }

    private static bool TryCanonicalInt(SemanticScalarValueDto value, out int parsed)
    {
        parsed = default;
        return value.CanonicalValue is not null
            && TryCanonicalInt(value.CanonicalValue, out parsed);
    }

    private static bool TryCanonicalInt(string value, out int parsed) =>
        int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out parsed)
        && string.Equals(parsed.ToString(CultureInfo.InvariantCulture), value, StringComparison.Ordinal);

    private static bool OwnedTargetsEqual(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right) =>
        left.Order(StringComparer.Ordinal).SequenceEqual(right.Order(StringComparer.Ordinal), StringComparer.Ordinal);

    private static bool SnapshotsEqual(
        SemanticSourceSnapshotDto left,
        SemanticSourceSnapshotDto right) =>
        string.Equals(left.Fingerprint, right.Fingerprint, StringComparison.Ordinal)
        && RevisionsEqual(left.Revision, right.Revision)
        && left.Layer.Kind == right.Layer.Kind
        && string.Equals(left.Layer.InstanceId, right.Layer.InstanceId, StringComparison.Ordinal);

    private static bool RevisionsEqual(
        SemanticProjectRevisionDto left,
        SemanticProjectRevisionDto right) =>
        string.Equals(left.ProjectId, right.ProjectId, StringComparison.Ordinal)
        && left.GameFamily == right.GameFamily
        && string.Equals(left.Generation, right.Generation, StringComparison.Ordinal)
        && string.Equals(left.Fingerprint, right.Fingerprint, StringComparison.Ordinal);

    private static void EnsureSameRevision(
        SemanticProjectRevisionDto initial,
        SemanticProjectRevisionDto completed)
    {
        if (!RevisionsEqual(initial, completed))
        {
            throw new SemanticExploreValidationException(
                "The semantic project changed during the request. Retry it.",
                SemanticExploreFailureKind.StaleRevision);
        }
    }

    private static SemanticRecordRefDto CreateRecordRef(
        SemanticGameFamilyDto family,
        SemanticMergeDomainCapabilityDto domain,
        string recordId)
    {
        var record = new SemanticRecordRefDto(
            family,
            domain.Domain,
            new SemanticRecordKindDto(domain.RecordKind, 1),
            recordId,
            SubrecordId: null);
        if (!IsEligibleRecord(record))
        {
            throw Invalid("A change-set operation record is outside the recipe identity adapter.");
        }

        return record;
    }

    private static string RecordKey(SemanticRecordRefDto record) => string.Join(
        ':',
        record.GameFamily,
        record.Domain,
        record.RecordKind.Key,
        record.RecordKind.SchemaVersion.ToString(CultureInfo.InvariantCulture),
        record.RecordId,
        record.SubrecordId ?? string.Empty);

    private static string FieldRefKey(SemanticMergeFieldRefDto target) =>
        $"{RecordKey(target.Record)}:{target.FieldKey}";

    private static string RequireCanonicalText(
        string? value,
        string label,
        int maximumLength,
        bool allowEmpty)
    {
        if (value is null
            || value.Length > maximumLength
            || !allowEmpty && value.Length == 0
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || !value.IsNormalized(NormalizationForm.FormC)
            || value.Any(IsUnsafeUnicode)
            || !GuidedDesignProviders.IsSafeGeneratedDisplayText(value))
        {
            throw Invalid($"The {label} is not canonical or exceeds its bounds.");
        }

        return value;
    }

    private static bool IsStableId(string? value, int maximumLength) =>
        value is { Length: > 0 }
        && value.Length <= maximumLength
        && char.IsAsciiLetterOrDigit(value[0])
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');

    private static bool IsMergeSourceId(string? value) =>
        value is { Length: 42 }
        && value.StartsWith("merge-src-", StringComparison.Ordinal)
        && value.AsSpan(10).ContainsAnyExcept("0123456789abcdef") is false;

    private static bool IsRecipeInstanceId(string? value) =>
        value is { Length: 39 }
        && value.StartsWith("recipe-", StringComparison.Ordinal)
        && value.AsSpan(7).ContainsAnyExcept("0123456789abcdef") is false;

    private static bool IsSha256(string? value) =>
        value is { Length: 64 }
        && value.AsSpan().ContainsAnyExcept("0123456789abcdef") is false;

    private static string SuggestedRecipeFileName(string name)
    {
        var slug = new string(name.ToLowerInvariant()
            .Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-')
            .ToArray()).Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return $"{(slug.Length == 0 ? "recipe" : slug[..Math.Min(slug.Length, 64)])}.kmrecipe";
    }

    private static string EncodeCursor(string queryFingerprint, int offset)
    {
        var payload = $"{offset.ToString(CultureInfo.InvariantCulture)}\n{queryFingerprint}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
    }

    private static int DecodeCursor(string? cursor, string queryFingerprint)
    {
        if (cursor is null)
        {
            return 0;
        }

        try
        {
            var value = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var separator = value.IndexOf('\n');
            if (separator <= 0
                || !int.TryParse(
                    value[..separator],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var offset)
                || offset < 0
                || !string.Equals(value[(separator + 1)..], queryFingerprint, StringComparison.Ordinal))
            {
                throw InvalidCursor("The continuation cursor is invalid for this query.");
            }

            return offset;
        }
        catch (FormatException exception)
        {
            throw new SemanticExploreValidationException(
                "The continuation cursor is invalid.",
                SemanticExploreFailureKind.InvalidCursor,
                exception);
        }
    }

    private static string Hash(string domain, params string?[] values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHash(hash, domain);
        foreach (var value in values)
        {
            AppendHash(hash, value);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void AppendHash(IncrementalHash hash, string? value)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        if (value is null)
        {
            BinaryPrimitives.WriteInt32LittleEndian(length, -1);
            hash.AppendData(length);
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, BridgeJson.SerializerOptions);

    private static string Sha256(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private Func<EditSession, ChangePlanOutputModeDto?, ChangePlan> Planner(ProjectPathsDto paths) =>
        (session, outputMode) => createChangePlan(paths, session, outputMode);

    private Func<EditSession, ChangePlanOutputModeDto?, ChangePlan> BoundedScalarPlanner(
        ProjectPathsDto paths) =>
        (session, outputMode) => createBoundedScalarChangePlan(paths, session, outputMode);

    private static ChangeSetWorkspaceScopeDto ToChangeSetScope(SemanticExploreScopeDto scope) =>
        new(scope.ProjectId, scope.Paths);

    private static SemanticMergeDisabledImportReceiptDto ToReceipt(
        GeneratedChangeSetReviewImportResult result) =>
        new(
            result.Document,
            result.ETag,
            result.CanUndo,
            result.CanRedo,
            result.UndoLabel,
            result.RedoLabel);

    private static SemanticGameFamilyDto ToFamily(ProjectGameDto game) => game switch
    {
        ProjectGameDto.Sword or ProjectGameDto.Shield => SemanticGameFamilyDto.SwordShield,
        ProjectGameDto.Scarlet or ProjectGameDto.Violet => SemanticGameFamilyDto.ScarletViolet,
        ProjectGameDto.ZA => SemanticGameFamilyDto.LegendsZA,
        _ => throw Invalid("The selected project game is invalid."),
    };

    private static ApiDiagnostic Diagnostic(
        ApiDiagnosticSeverity severity,
        string code,
        string message) => new(severity, message, Domain: "semanticMerge") { Code = code };

    private static SemanticExploreValidationException Invalid(string message) =>
        new(message, SemanticExploreFailureKind.InvalidData);

    private static SemanticExploreValidationException Unsupported(string message) =>
        new(message, SemanticExploreFailureKind.Unsupported);

    private static SemanticExploreValidationException InvalidCursor(string message) =>
        new(message, SemanticExploreFailureKind.InvalidCursor);

    private static SemanticExploreValidationException LimitExceeded(string message) =>
        new(message, SemanticExploreFailureKind.LimitExceeded);

    private static SemanticExploreValidationException ExternalSnapshotUnavailable(string message) =>
        new(message, SemanticExploreFailureKind.ExternalSnapshotUnavailable);

    private static SemanticMergeValidationException StaleMerge(string message) =>
        new(message, SemanticMergeFailureKind.StaleProposal);

    private static SemanticMergeValidationException StaleRecipe(string message) =>
        new(message, SemanticMergeFailureKind.StaleRecipeProposal);

    private static bool IsFatal(Exception exception) => exception is
        OutOfMemoryException or StackOverflowException or AccessViolationException;

    private static bool IsUnsafeUnicode(char character) =>
        char.IsControl(character)
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

    private static JsonSerializerOptions CreateRecipeJsonOptions()
    {
        var options = new JsonSerializerOptions(BridgeJson.SerializerOptions)
        {
            MaxDepth = 32,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            PropertyNameCaseInsensitive = false,
            WriteIndented = true,
        };
        options.Converters.Insert(0, new RecipeProjectGameJsonConverter());
        return options;
    }

    private static void ValidateNoDuplicateRecipeProperties(string content)
    {
        using var document = JsonDocument.Parse(
            content,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32,
            });
        ValidateNoDuplicateRecipeProperties(document.RootElement);
    }

    private static void ValidateNoDuplicateRecipeProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new JsonException("The recipe contains a duplicate object member.");
                }

                ValidateNoDuplicateRecipeProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                ValidateNoDuplicateRecipeProperties(item);
            }
        }
    }

    private sealed class RecipeProjectGameJsonConverter : JsonConverter<ProjectGameDto>
    {
        public override ProjectGameDto Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException("The recipe game must be a canonical string.");
            }

            return reader.GetString() switch
            {
                "sword" => ProjectGameDto.Sword,
                "shield" => ProjectGameDto.Shield,
                "scarlet" => ProjectGameDto.Scarlet,
                "violet" => ProjectGameDto.Violet,
                "za" => ProjectGameDto.ZA,
                _ => throw new JsonException("The recipe game is invalid."),
            };
        }

        public override void Write(
            Utf8JsonWriter writer,
            ProjectGameDto value,
            JsonSerializerOptions options)
        {
            writer.WriteStringValue(value switch
            {
                ProjectGameDto.Sword => "sword",
                ProjectGameDto.Shield => "shield",
                ProjectGameDto.Scarlet => "scarlet",
                ProjectGameDto.Violet => "violet",
                ProjectGameDto.ZA => "za",
                _ => throw new JsonException("The recipe game is invalid."),
            });
        }
    }

    private sealed record SourceHandleEntry(
        string PublicId,
        SemanticProjectRevisionDto Revision,
        string InternalId,
        string SnapshotFingerprint,
        SemanticSourceSnapshotDto PublicSnapshot,
        IReadOnlyList<SemanticProviderCoverageDto> Coverage,
        DateTimeOffset LastAccessUtc,
        DateTimeOffset ExpiresAtUtc);

    private sealed record RecipeHandleEntry(
        string InstanceId,
        string Fingerprint,
        KmRecipePackageDto Package,
        long SizeBytes,
        DateTimeOffset LastAccessUtc,
        DateTimeOffset ExpiresAtUtc);

    private sealed record ExportOperation(
        int StepIndex,
        SemanticRecordRefDto Record,
        string FieldKey,
        KmRecipeScalarDto ExpectedBaseValue,
        KmRecipeScalarDto ExpectedCurrentValue,
        KmRecipeScalarDto AfterValue,
        string ProviderId,
        IReadOnlyList<string> OwnedTargets);

    private sealed record MergeGeneratedProposal(
        SemanticProjectRevisionDto Revision,
        SemanticSourceSnapshotDto BaseSnapshot,
        SemanticSourceSnapshotDto LayeredSnapshot,
        SemanticSourceSnapshotDto PendingSnapshot,
        SemanticSourceSnapshotDto SourceASnapshot,
        SemanticSourceSnapshotDto SourceBSnapshot,
        IReadOnlyList<SemanticMergeCapabilityDto> Capabilities,
        IReadOnlyList<SemanticMergeFieldRefDto> NormalizedTargets,
        IReadOnlyList<SemanticMergeConflictResolutionDto> NormalizedResolutions,
        string? NormalizedTargetSearchText,
        bool TargetWindowCapped,
        int TotalMatchingTargetCount,
        int TotalRowCount,
        int TotalConflictCount,
        string ProposalId,
        string ProposalFingerprint,
        bool CanImport,
        bool SelectionRequired,
        IReadOnlyList<SemanticMergeRowDto> Rows,
        IReadOnlyList<ApiDiagnostic> Diagnostics,
        IReadOnlyList<PendingEdit> PendingEdits);

    private sealed record RecipeGeneratedProposal(
        SemanticProjectRevisionDto Revision,
        SemanticSourceSnapshotDto BaseSnapshot,
        SemanticSourceSnapshotDto LayeredSnapshot,
        SemanticSourceSnapshotDto PendingSnapshot,
        string ProposalId,
        string ProposalFingerprint,
        bool CanImport,
        IReadOnlyList<KmRecipeCompatibilityRowDto> Compatibility,
        IReadOnlyList<ApiDiagnostic> Diagnostics,
        IReadOnlyList<PendingEdit> PendingEdits);

    private sealed class SemanticRecordIdComparer : IComparer<string>
    {
        internal static SemanticRecordIdComparer Instance { get; } = new();

        public int Compare(string? left, string? right)
        {
            if (long.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out var leftValue)
                && long.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out var rightValue))
            {
                return leftValue.CompareTo(rightValue);
            }

            return StringComparer.Ordinal.Compare(left, right);
        }
    }
}
