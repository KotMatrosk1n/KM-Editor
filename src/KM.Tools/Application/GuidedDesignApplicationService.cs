// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using KM.Api.Bridge;
using KM.Api.ChangeSets;
using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Encounters;
using KM.Api.GuidedDesign;
using KM.Api.Items;
using KM.Api.Pokemon;
using KM.Api.Projects;
using KM.Api.Semantics;
using KM.Api.Trainers;
using KM.Core.Concurrency;
using KM.Api.Workflows;
using KM.Core.Editing;
using KM.Core.Projects;
using KM.Tools.Bridge;

namespace KM.Tools.Application;

public enum GuidedDesignFailureKind
{
    StaleProposal,
}

public sealed class GuidedDesignValidationException : Exception
{
    public GuidedDesignValidationException(
        string message,
        GuidedDesignFailureKind failureKind,
        Exception? innerException = null)
        : base(message, innerException)
    {
        FailureKind = failureKind;
    }

    public GuidedDesignFailureKind FailureKind { get; }
}

internal sealed record GuidedDesignWorkflowDtoLoaders(
    Func<TrainersWorkflowDto> Trainers,
    Func<EncountersWorkflowDto> Encounters,
    Func<ItemsWorkflowDto> Items,
    Func<PokemonWorkflowDto> Pokemon);

public sealed class GuidedDesignApplicationService
{
    private const int MaximumDiagnostics = 512;
    private const int MaximumCapabilityCacheEntries = 8;
    private const int CapabilityBuildLockCount = 8;
    private const int MaximumConcurrentSourceLoads = 4;
    private const long EstimatedSourceLoadWorkerBytes = 256L * 1024L * 1024L;
    private static readonly BoundedConcurrencyPolicy SourceLoadPolicy = new(
        "guided-design-source-load",
        BoundedWorkloadKind.Decode,
        EstimatedSourceLoadWorkerBytes,
        maximumDegreeOfParallelism: MaximumConcurrentSourceLoads,
        memoryBudgetDivisor: 8,
        degreeOfParallelismWhenMemoryUnknown: 1);
    private readonly object capabilityCacheSync = new();
    private readonly Dictionary<string, CapabilityCacheEntry> capabilityCache = new(
        StringComparer.Ordinal);
    private readonly LinkedList<string> capabilityCacheUsage = new();
    private readonly object[] capabilityBuildLocks =
    [
        new(), new(), new(), new(), new(), new(), new(), new(),
    ];
    private readonly SemanticExploreApplicationService semanticExploreService;
    private readonly ChangeSetApplicationService changeSetService;
    private readonly Func<ProjectPathsDto, TrainersWorkflowDto> loadTrainersFresh;
    private readonly Func<ProjectPathsDto, EncountersWorkflowDto> loadEncountersFresh;
    private readonly Func<ProjectPathsDto, ItemsWorkflowDto> loadItemsFresh;
    private readonly Func<ProjectPathsDto, PokemonWorkflowDto> loadPokemonFresh;
    private readonly Func<ProjectPathsDto, bool> canLoadSourcesConcurrently;
    private readonly Func<ProjectPathsDto, int, GuidedDesignWorkflowDtoLoaders>?
        prepareSourcesFresh;
    private readonly Func<
        ProjectPathsDto,
        IReadOnlyList<GuidedDesignStagingEdit>,
        GuidedDesignStagingResult> stageEdits;
    private readonly Func<
        ProjectPathsDto,
        EditSession,
        ChangePlanOutputModeDto?,
        ChangePlan> createChangePlan;

    public GuidedDesignApplicationService(
        SemanticExploreApplicationService semanticExploreService,
        ChangeSetApplicationService changeSetService,
        Func<ProjectPathsDto, TrainersWorkflowDto> loadTrainersFresh,
        Func<ProjectPathsDto, EncountersWorkflowDto> loadEncountersFresh,
        Func<ProjectPathsDto, ItemsWorkflowDto> loadItemsFresh,
        Func<ProjectPathsDto, PokemonWorkflowDto> loadPokemonFresh,
        Func<
            ProjectPathsDto,
            IReadOnlyList<GuidedDesignStagingEdit>,
            GuidedDesignStagingResult> stageEdits,
        Func<
            ProjectPathsDto,
            EditSession,
            ChangePlanOutputModeDto?,
            ChangePlan> createChangePlan,
        Func<ProjectPathsDto, bool>? canLoadSourcesConcurrently = null)
        : this(
            semanticExploreService,
            changeSetService,
            loadTrainersFresh,
            loadEncountersFresh,
            loadItemsFresh,
            loadPokemonFresh,
            stageEdits,
            createChangePlan,
            canLoadSourcesConcurrently,
            prepareSourcesFresh: null)
    {
    }

    internal GuidedDesignApplicationService(
        SemanticExploreApplicationService semanticExploreService,
        ChangeSetApplicationService changeSetService,
        Func<ProjectPathsDto, TrainersWorkflowDto> loadTrainersFresh,
        Func<ProjectPathsDto, EncountersWorkflowDto> loadEncountersFresh,
        Func<ProjectPathsDto, ItemsWorkflowDto> loadItemsFresh,
        Func<ProjectPathsDto, PokemonWorkflowDto> loadPokemonFresh,
        Func<
            ProjectPathsDto,
            IReadOnlyList<GuidedDesignStagingEdit>,
            GuidedDesignStagingResult> stageEdits,
        Func<
            ProjectPathsDto,
            EditSession,
            ChangePlanOutputModeDto?,
            ChangePlan> createChangePlan,
        Func<ProjectPathsDto, bool>? canLoadSourcesConcurrently,
        Func<ProjectPathsDto, int, GuidedDesignWorkflowDtoLoaders>? prepareSourcesFresh)
    {
        this.semanticExploreService = semanticExploreService
            ?? throw new ArgumentNullException(nameof(semanticExploreService));
        this.changeSetService = changeSetService
            ?? throw new ArgumentNullException(nameof(changeSetService));
        this.loadTrainersFresh = loadTrainersFresh
            ?? throw new ArgumentNullException(nameof(loadTrainersFresh));
        this.loadEncountersFresh = loadEncountersFresh
            ?? throw new ArgumentNullException(nameof(loadEncountersFresh));
        this.loadItemsFresh = loadItemsFresh
            ?? throw new ArgumentNullException(nameof(loadItemsFresh));
        this.loadPokemonFresh = loadPokemonFresh
            ?? throw new ArgumentNullException(nameof(loadPokemonFresh));
        this.canLoadSourcesConcurrently = canLoadSourcesConcurrently ?? (_ => false);
        this.prepareSourcesFresh = prepareSourcesFresh;
        this.stageEdits = stageEdits ?? throw new ArgumentNullException(nameof(stageEdits));
        this.createChangePlan = createChangePlan
            ?? throw new ArgumentNullException(nameof(createChangePlan));
    }

    public ReadGuidedDesignCapabilitiesResponse ReadCapabilities(
        ReadGuidedDesignCapabilitiesRequest request)
    {
        if (request?.Scope?.Paths is null)
        {
            throw new SemanticExploreValidationException(
                "The Guided Design capability request is malformed.",
                SemanticExploreFailureKind.InvalidData);
        }

        var initial = semanticExploreService.ReadCapabilities(
            new ReadSemanticCapabilitiesRequest(request.Scope));
        var capabilityCacheKey = CapabilityCacheKey(
            initial.Revision,
            semanticExploreService.ReadSourceCacheIdentity(request.Scope));
        ProjectCapabilityRead capabilityRead;
        ReadSemanticCapabilitiesResponse completed;
        lock (CapabilityBuildLock(capabilityCacheKey))
        {
            // Capability readiness is decoded once per exact semantic revision. Keep the
            // source recheck and cache publication inside the same gate so launch preload
            // and a foreground open cannot duplicate or publish a stale readiness result.
            capabilityRead = ReadProjectCapabilitiesCached(
                request.Scope.Paths,
                initial.Revision.GameFamily,
                capabilityCacheKey);
            completed = semanticExploreService.ReadCapabilities(
                new ReadSemanticCapabilitiesRequest(request.Scope));
            EnsureSameSemanticObservation(initial, completed);
            if (capabilityRead.Cacheable)
            {
                CacheProjectCapabilities(capabilityCacheKey, capabilityRead.Capabilities);
            }
        }

        return new ReadGuidedDesignCapabilitiesResponse(
            completed.Revision,
            completed.Snapshots,
            capabilityRead.Capabilities,
            GuidedDesignProviders.FieldCatalogs(
                completed.Revision.GameFamily,
                capabilityRead.Capabilities
                    .Where(capability => capability.State != SemanticCoverageStateDto.Unavailable)
                    .SelectMany(capability => capability.ProposalKinds)));
    }

    private ProjectCapabilityRead ReadProjectCapabilities(
        ProjectPathsDto paths,
        SemanticGameFamilyDto family)
    {
        var readiness = Enum.GetValues<GuidedDesignProposalKindDto>()
            .ToDictionary(kind => kind, _ => false);
        var sources = LoadProjectSources(paths, family);
        var trainersCacheable = ProjectTrainerReadiness(sources.Trainers, family, readiness);
        var encountersCacheable = ProjectEncounterReadiness(sources.Encounters, family, readiness);
        var itemsCacheable = ProjectItemReadiness(sources.Items, family, readiness);
        var pokemonCacheable = ProjectPokemonReadiness(sources.Pokemon, family, readiness);

        var capabilities = GuidedDesignProviders.Capabilities(family).Select(capability =>
        {
            if (capability.State == SemanticCoverageStateDto.Unavailable)
            {
                return capability;
            }

            var availableKinds = capability.ProposalKinds
                .Where(kind => readiness.GetValueOrDefault(kind))
                .ToArray();
            if (availableKinds.Length == 0)
            {
                return capability with
                {
                    State = SemanticCoverageStateDto.Unavailable,
                    Confidence = SemanticConfidenceDto.Unknown,
                    ReasonCode = "workflow-source-unavailable",
                    ProposalKinds = Array.Empty<GuidedDesignProposalKindDto>(),
                    SourceLayers = Array.Empty<SemanticSourceLayerKindDto>(),
                };
            }

            return availableKinds.Length == capability.ProposalKinds.Count
                ? capability
                : capability with
                {
                    State = SemanticCoverageStateDto.Partial,
                    Confidence = SemanticConfidenceDto.Derived,
                    ReasonCode = "workflow-source-partial",
                    ProposalKinds = availableKinds,
                };
        }).ToArray();
        var cacheable = trainersCacheable
            && encountersCacheable
            && itemsCacheable
            && pokemonCacheable;
        return new ProjectCapabilityRead(capabilities, cacheable);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool ProjectTrainerReadiness(
        TrainersWorkflowDto? trainers,
        SemanticGameFamilyDto family,
        IDictionary<GuidedDesignProposalKindDto, bool> readiness)
    {
        if (family == SemanticGameFamilyDto.SwordShield)
        {
            return true;
        }

        if (trainers is null)
        {
            return false;
        }

        if (WorkflowReady(trainers.Summary, trainers.Diagnostics))
        {
            var hasEligibleMember = trainers.Trainers.Any(trainer =>
                trainer.Team.Any(member => member.SpeciesId > 0));
            readiness[GuidedDesignProposalKindDto.TrainerLevelAdjustment] =
                hasEligibleMember && HasTrainerField(trainers, "level");
            readiness[GuidedDesignProposalKindDto.TrainerEvArchetype] =
                hasEligibleMember && new[]
                {
                    "evHp",
                    "evAttack",
                    "evDefense",
                    "evSpecialAttack",
                    "evSpecialDefense",
                    "evSpeed",
                }.All(field => HasTrainerField(trainers, field));
        }

        return WorkflowCacheable(trainers.Summary, trainers.Diagnostics);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool ProjectEncounterReadiness(
        EncountersWorkflowDto? encounters,
        SemanticGameFamilyDto family,
        IDictionary<GuidedDesignProposalKindDto, bool> readiness)
    {
        if (encounters is null)
        {
            return false;
        }

        if (WorkflowReady(encounters.Summary, encounters.Diagnostics))
        {
            var eligibleSlots = encounters.Tables
                .SelectMany(table => table.Slots)
                .Where(slot => slot.SpeciesId > 0)
                .ToArray();
            readiness[GuidedDesignProposalKindDto.EncounterLevelAdjustment] =
                eligibleSlots.Length > 0
                && HasEncounterField(encounters, "levelMin")
                && HasEncounterField(encounters, "levelMax");
            readiness[GuidedDesignProposalKindDto.EncounterWeightScale] = family switch
            {
                SemanticGameFamilyDto.ScarletViolet => eligibleSlots.Length > 0
                    && HasEncounterField(encounters, "probability"),
                SemanticGameFamilyDto.LegendsZA => HasEncounterField(encounters, "weight")
                    && eligibleSlots.Any(slot => slot.CanEditWeight == true),
                _ => false,
            };
        }

        return WorkflowCacheable(encounters.Summary, encounters.Diagnostics);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool ProjectItemReadiness(
        ItemsWorkflowDto? items,
        SemanticGameFamilyDto family,
        IDictionary<GuidedDesignProposalKindDto, bool> readiness)
    {
        if (items is null)
        {
            return false;
        }

        if (WorkflowReady(items.Summary, items.Diagnostics))
        {
            var field = family == SemanticGameFamilyDto.LegendsZA ? "price" : "buyPrice";
            readiness[GuidedDesignProposalKindDto.EconomyPrimaryPriceScale] =
                items.Items.Any(item => item.ItemId > 0)
                && items.EditableFields.Count(candidate =>
                    string.Equals(candidate.Field, field, StringComparison.Ordinal)
                    && !candidate.IsReadOnly
                    && candidate.MinimumValue is not null
                    && candidate.MaximumValue is not null) == 1;
        }

        return WorkflowCacheable(items.Summary, items.Diagnostics);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool ProjectPokemonReadiness(
        PokemonWorkflowDto? pokemon,
        SemanticGameFamilyDto family,
        IDictionary<GuidedDesignProposalKindDto, bool> readiness)
    {
        if (pokemon is null)
        {
            return false;
        }

        if (WorkflowReady(pokemon.Summary, pokemon.Diagnostics))
        {
            readiness[GuidedDesignProposalKindDto.PokemonBaseStatShuffle] =
                new[] { "hp", "attack", "defense", "specialAttack", "specialDefense", "speed" }
                    .All(field => HasPokemonField(pokemon, field))
                && pokemon.Pokemon.Any(IsEligiblePokemon);
            var methods = pokemon.EvolutionMethodOptions
                .GroupBy(option => option.Value)
                .ToDictionary(group => group.Key, group => group.ToArray());
            readiness[GuidedDesignProposalKindDto.EvolutionLevelClamp] =
                family == SemanticGameFamilyDto.LegendsZA
                && pokemon.Pokemon.Any(candidate =>
                    IsEligiblePokemon(candidate)
                    && candidate.Evolutions.Any(evolution =>
                        methods.TryGetValue(evolution.Method, out var options)
                        && options.Length == 1
                        && options[0].UsesLevel));
        }

        return WorkflowCacheable(pokemon.Summary, pokemon.Diagnostics);
    }

    private ProjectWorkflowSources LoadProjectSources(
        ProjectPathsDto paths,
        SemanticGameFamilyDto family)
    {
        var sourceCount = family == SemanticGameFamilyDto.SwordShield
            ? MaximumConcurrentSourceLoads - 1
            : MaximumConcurrentSourceLoads;
        var parallelism = canLoadSourcesConcurrently(paths)
            ? BoundedParallel.Plan(sourceCount, SourceLoadPolicy).DegreeOfParallelism
            : 1;
        var loaders = PrepareProjectSourceLoaders(paths, parallelism);
        TrainersWorkflowDto? trainers = null;
        EncountersWorkflowDto? encounters = null;
        ItemsWorkflowDto? items = null;
        PokemonWorkflowDto? pokemon = null;
        var failures = new ExceptionDispatchInfo?[MaximumConcurrentSourceLoads];

        void LoadAt(int index)
        {
            try
            {
                switch (index)
                {
                    case 0 when family != SemanticGameFamilyDto.SwordShield:
                        trainers = TryLoad(loaders.Trainers);
                        break;
                    case 1:
                        encounters = TryLoad(loaders.Encounters);
                        break;
                    case 2:
                        items = TryLoad(loaders.Items);
                        break;
                    case 3:
                        pokemon = TryLoad(loaders.Pokemon);
                        break;
                }
            }
            catch (Exception exception)
            {
                failures[index] = ExceptionDispatchInfo.Capture(exception);
            }
        }

        var executionPolicy = parallelism > 1
            ? SourceLoadPolicy
            : new BoundedConcurrencyPolicy(
                "guided-design-source-load-serial",
                BoundedWorkloadKind.Decode,
                EstimatedSourceLoadWorkerBytes,
                maximumDegreeOfParallelism: 1);
        _ = BoundedParallel.For(MaximumConcurrentSourceLoads, executionPolicy, LoadAt);

        // Preserve the historical trainer, encounter, item, Pokemon failure precedence.
        foreach (var failure in failures)
        {
            failure?.Throw();
        }

        return new ProjectWorkflowSources(trainers, encounters, items, pokemon);
    }

    private GuidedDesignWorkflowDtoLoaders PrepareProjectSourceLoaders(
        ProjectPathsDto paths,
        int parallelism)
    {
        if (prepareSourcesFresh is null)
        {
            return new GuidedDesignWorkflowDtoLoaders(
                () => loadTrainersFresh(paths),
                () => loadEncountersFresh(paths),
                () => loadItemsFresh(paths),
                () => loadPokemonFresh(paths));
        }

        try
        {
            return prepareSourcesFresh(paths, parallelism);
        }
        catch (Exception exception) when (IsUnavailableSourceException(exception))
        {
            var failure = ExceptionDispatchInfo.Capture(exception);
            return new GuidedDesignWorkflowDtoLoaders(
                () => RethrowPreparedSourceFailure<TrainersWorkflowDto>(failure),
                () => RethrowPreparedSourceFailure<EncountersWorkflowDto>(failure),
                () => RethrowPreparedSourceFailure<ItemsWorkflowDto>(failure),
                () => RethrowPreparedSourceFailure<PokemonWorkflowDto>(failure));
        }
    }

    private static T RethrowPreparedSourceFailure<T>(ExceptionDispatchInfo failure)
        where T : class
    {
        failure.Throw();
        throw new InvalidOperationException("The Guided Design source failure was not rethrown.");
    }

    private ProjectCapabilityRead ReadProjectCapabilitiesCached(
        ProjectPathsDto paths,
        SemanticGameFamilyDto family,
        string cacheKey)
    {
        lock (capabilityCacheSync)
        {
            if (capabilityCache.TryGetValue(cacheKey, out var cached))
            {
                capabilityCacheUsage.Remove(cached.UsageNode);
                capabilityCacheUsage.AddFirst(cached.UsageNode);
                return new ProjectCapabilityRead(cached.Capabilities, Cacheable: true);
            }
        }

        return ReadProjectCapabilities(paths, family);
    }

    private void CacheProjectCapabilities(
        string cacheKey,
        IReadOnlyList<GuidedDesignCapabilityDto> capabilities)
    {
        lock (capabilityCacheSync)
        {
            if (capabilityCache.Remove(cacheKey, out var existing))
            {
                capabilityCacheUsage.Remove(existing.UsageNode);
            }

            var usageNode = capabilityCacheUsage.AddFirst(cacheKey);
            capabilityCache.Add(
                cacheKey,
                new CapabilityCacheEntry(capabilities.ToArray(), usageNode));
            while (capabilityCache.Count > MaximumCapabilityCacheEntries)
            {
                var oldest = capabilityCacheUsage.Last
                    ?? throw new InvalidOperationException(
                        "The Guided Design capability cache is inconsistent.");
                capabilityCacheUsage.RemoveLast();
                if (!capabilityCache.Remove(oldest.Value))
                {
                    throw new InvalidOperationException(
                        "The Guided Design capability cache is inconsistent.");
                }
            }
        }
    }

    private object CapabilityBuildLock(string cacheKey)
    {
        var hash = 2166136261u;
        foreach (var character in cacheKey)
        {
            hash = unchecked((hash ^ character) * 16777619u);
        }

        return capabilityBuildLocks[hash % CapabilityBuildLockCount];
    }

    private static string CapabilityCacheKey(
        SemanticProjectRevisionDto revision,
        string sourceCacheIdentity)
    {
        return string.Join(
            ':',
            "guided-design-capabilities-v1",
            revision.ProjectId,
            revision.GameFamily.ToString(),
            sourceCacheIdentity);
    }

    private static T? TryLoad<T>(Func<T> load)
        where T : class
    {
        try
        {
            return load();
        }
        catch (Exception exception) when (IsUnavailableSourceException(exception))
        {
            return null;
        }
    }

    private static bool IsUnavailableSourceException(Exception exception) => exception is
        IOException or
        UnauthorizedAccessException or
        InvalidDataException or
        InvalidOperationException or
        ArgumentException or
        NotSupportedException or
        OverflowException;

    private static bool WorkflowReady(
        WorkflowSummaryDto summary,
        IReadOnlyList<ApiDiagnostic> diagnostics) =>
        summary.Availability == WorkflowAvailabilityDto.Available
        && !summary.Diagnostics.Concat(diagnostics)
            .Any(diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

    private static bool WorkflowCacheable(
        WorkflowSummaryDto summary,
        IReadOnlyList<ApiDiagnostic> diagnostics) =>
        summary.Availability == WorkflowAvailabilityDto.Available
        && !summary.Diagnostics.Concat(diagnostics)
            .Any(diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

    private static bool HasTrainerField(TrainersWorkflowDto workflow, string field) =>
        workflow.EditableFields.Count(candidate =>
            string.Equals(candidate.Field, field, StringComparison.Ordinal)
            && candidate.MinimumValue is not null
            && candidate.MaximumValue is not null) == 1;

    private static bool HasEncounterField(EncountersWorkflowDto workflow, string field) =>
        workflow.EditableFields.Count(candidate =>
            string.Equals(candidate.Field, field, StringComparison.Ordinal)
            && candidate.MinimumValue is not null
            && candidate.MaximumValue is not null) == 1;

    private static bool HasPokemonField(PokemonWorkflowDto workflow, string field) =>
        workflow.EditableFields.Count(candidate =>
            string.Equals(candidate.Field, field, StringComparison.Ordinal)
            && candidate.MinimumValue is not null
            && candidate.MaximumValue is not null) == 1;

    private static bool IsEligiblePokemon(PokemonRecordDto candidate) =>
        candidate.PersonalId > 0
        && candidate.SpeciesId > 0
        && candidate.DexPresence.IsPresentInGame
        && candidate.Personal.IsPresentInGame
        && !string.Equals(candidate.Name, "Egg", StringComparison.OrdinalIgnoreCase);

    public PreviewGuidedDesignResponse Preview(
        PreviewGuidedDesignRequest request,
        CancellationToken cancellationToken = default)
    {
        return PreviewAsync(request, cancellationToken).GetAwaiter().GetResult();
    }

    public async Task<PreviewGuidedDesignResponse> PreviewAsync(
        PreviewGuidedDesignRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidatePreviewRequest(request);
        var workspaceScope = ToChangeSetScope(request.Scope);
        var planner = Planner(request.Scope.Paths);
        var initialContext = await changeSetService.ObserveGeneratedProposalContextAsync(
                workspaceScope,
                request.ExpectedChangeSetETag,
                request.Scope.PendingSession,
                planner,
                cancellationToken)
            .ConfigureAwait(false);
        var normalizedSeedInput = NormalizePreviewSeed(request.Input, request.Cursor is null);
        var normalizedTargetSearchText = NormalizeTargetSearch(request.TargetSearchText);
        var generated = Generate(
            request.Scope,
            request.ExpectedRevision,
            normalizedSeedInput,
            normalizedTargetSearchText,
            initialContext,
            cancellationToken);
        var completedContext = await changeSetService.ObserveGeneratedProposalContextAsync(
                workspaceScope,
                request.ExpectedChangeSetETag,
                request.Scope.PendingSession,
                planner,
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(
                initialContext.Fingerprint,
                completedContext.Fingerprint,
                StringComparison.Ordinal))
        {
            throw StaleProposal(
                "The Guided Design authoring context changed while the proposal was generated. Refresh and retry.");
        }

        if (request.Cursor is not null
            && (!GuidedDesignInputsMatchExactly(request.Input, generated.NormalizedInput)
                || !string.Equals(request.ProposalId, generated.ProposalId, StringComparison.Ordinal)
                || !string.Equals(
                    request.ProposalFingerprint,
                    generated.ProposalFingerprint,
                    StringComparison.Ordinal)))
        {
            throw StaleProposal(
                "The Guided Design proposal changed before its next page was read. Preview it again.");
        }

        var queryFingerprint = Hash(
            "guided-design-query-v1",
            generated.ProposalId,
            generated.ProposalFingerprint);
        var offset = DecodeCursor(request.Cursor, queryFingerprint);
        var resultCount = Math.Max(
            generated.EligibleTargets.Count,
            Math.Max(generated.Mutations.Count, generated.Findings.Count));
        if (offset > resultCount)
        {
            throw new SemanticExploreValidationException(
                "The Guided Design continuation cursor is outside the current result set.",
                SemanticExploreFailureKind.InvalidCursor);
        }

        var mutations = generated.Mutations.Skip(offset).Take(request.Limit).ToArray();
        var findings = generated.Findings.Skip(offset).Take(request.Limit).ToArray();
        var eligibleTargets = generated.EligibleTargets.Skip(offset).Take(request.Limit).ToArray();
        var nextOffset = checked(offset + request.Limit);
        var nextCursor = nextOffset < resultCount
            ? EncodeCursor(queryFingerprint, nextOffset)
            : null;
        return new PreviewGuidedDesignResponse(
            generated.Revision,
            queryFingerprint,
            generated.Snapshot,
            generated.Capabilities,
            generated.NormalizedInput,
            generated.Seed,
            completedContext.Fingerprint,
            generated.ProposalId,
            generated.ProposalFingerprint,
            generated.CanImport,
            generated.SelectionRequired,
            generated.NormalizedTargetSearchText,
            generated.EligibleTargetWindowCapped,
            generated.TotalEligibleTargetCount,
            eligibleTargets,
            generated.Mutations.Count,
            generated.Findings.Count,
            generated.AffectedRecords,
            mutations,
            findings,
            generated.Exports,
            generated.Diagnostics,
            nextCursor);
    }

    public ImportGuidedDesignProposalResponse Import(
        ImportGuidedDesignProposalRequest request,
        CancellationToken cancellationToken = default)
    {
        return ImportAsync(request, cancellationToken).GetAwaiter().GetResult();
    }

    public async Task<ImportGuidedDesignProposalResponse> ImportAsync(
        ImportGuidedDesignProposalRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateImportRequest(request);
        GuidedDesignGeneratedProposal? regenerated = null;
        var planner = Planner(request.Scope.Paths);
        var result = await changeSetService.ImportGeneratedProposalAsync(
                new GeneratedChangeSetImportRequest(
                    ToChangeSetScope(request.Scope),
                    request.ChangeSetName,
                    GuidedDesignProviders.GeneratedEditOwner,
                    "guided-design",
                    "Import guided design proposal",
                    request.ExpectedChangeSetETag,
                    request.Scope.PendingSession),
                (context, token) =>
                {
                    var candidate = Generate(
                        request.Scope,
                        request.ExpectedRevision,
                        request.Input,
                        targetSearchText: null,
                        context,
                        token);
                    if (!string.Equals(candidate.ProposalId, request.ProposalId, StringComparison.Ordinal)
                        || !string.Equals(
                            candidate.ProposalFingerprint,
                            request.ProposalFingerprint,
                            StringComparison.Ordinal)
                        || !GuidedDesignInputsMatchExactly(request.Input, candidate.NormalizedInput)
                        || !candidate.CanImport)
                    {
                        throw StaleProposal(
                            "The Guided Design proposal is stale or is no longer safe to import. Preview it again.");
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
            ?? throw StaleProposal("The Guided Design proposal could not be regenerated for import.");
        return new ImportGuidedDesignProposalResponse(
            proposal.Revision,
            proposal.ProposalId,
            proposal.ProposalFingerprint,
            result.ImportedChangeSetId,
            result.Snapshot,
            proposal.Diagnostics);
    }

    private GuidedDesignGeneratedProposal Generate(
        SemanticExploreScopeDto scope,
        SemanticProjectRevisionDto expectedRevision,
        GuidedDesignInputDto input,
        string? targetSearchText,
        GeneratedChangeSetAuthoringContext authoringContext,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var initial = semanticExploreService.ReadCapabilities(
            new ReadSemanticCapabilitiesRequest(scope));
        EnsureExpectedRevision(initial.Revision, expectedRevision);
        var initialSnapshot = initial.Snapshots.SingleOrDefault(snapshot =>
            snapshot.Layer.Kind == SemanticSourceLayerKindDto.Layered)
            ?? throw new SemanticExploreValidationException(
                "The exact layered source is unavailable for Guided Design.",
                SemanticExploreFailureKind.Unsupported);
        var capabilityResponse = ReadCapabilities(
            new ReadGuidedDesignCapabilitiesRequest(scope));
        EnsureExpectedRevision(capabilityResponse.Revision, expectedRevision);
        var capabilities = capabilityResponse.Capabilities;
        var provider = GuidedDesignProviders.Build(
            initial.Revision.GameFamily,
            input,
            () => loadTrainersFresh(scope.Paths),
            () => loadEncountersFresh(scope.Paths),
            () => loadItemsFresh(scope.Paths),
            () => loadPokemonFresh(scope.Paths),
            cancellationToken);
        var targetWindow = ApplyTargetWindow(provider, targetSearchText);
        provider = targetWindow.Provider;
        var pendingOverlay = HasRelevantPendingEdits(scope, input.Kind);
        var staged = provider.StagingEdits.Count == 0
            ? new GuidedDesignStagingResult(EditSession.Start(), IsValid: true)
            : stageEdits(scope.Paths, provider.StagingEdits);
        ArgumentNullException.ThrowIfNull(staged);
        ArgumentNullException.ThrowIfNull(staged.Session);
        var proposalValidation = staged.IsValid
            ? changeSetService.ValidateGeneratedProposal(
                staged.Session.PendingEdits,
                Planner(scope.Paths),
                authoringContext.OutputMode)
            : new GeneratedChangeSetProposalValidation(
                CanImport: false,
                "The owning workflow rejected one or more generated edits.",
                Array.Empty<GeneratedChangeSetOperationBinding>());
        var diagnostics = provider.Diagnostics.ToList();
        if (pendingOverlay)
        {
            diagnostics.Add(new ApiDiagnostic(
                ApiDiagnosticSeverity.Warning,
                "This proposal cannot be imported while its owning workflow has local pending edits that the layered generator cannot overlay safely.",
                Domain: GuidedDesignProviders.DomainFor(input.Kind))
            {
                Code = GuidedDesignProviders.PendingOverlayDiagnosticCode,
            });
        }

        if (!staged.IsValid || !proposalValidation.CanImport && provider.Mutations.Count > 0)
        {
            diagnostics.Add(new ApiDiagnostic(
                ApiDiagnosticSeverity.Error,
                "The generated proposal cannot be rebuilt independently by its owning workflow and was left read-only.",
                Domain: GuidedDesignProviders.DomainFor(input.Kind))
            {
                Code = GuidedDesignProviders.ProposalBlockedDiagnosticCode,
            });
        }

        diagnostics = diagnostics.Distinct().Take(MaximumDiagnostics).ToList();
        var completed = semanticExploreService.ReadCapabilities(
            new ReadSemanticCapabilitiesRequest(scope));
        EnsureExpectedRevision(completed.Revision, expectedRevision);
        var completedSnapshot = completed.Snapshots.SingleOrDefault(snapshot =>
            snapshot.Layer.Kind == SemanticSourceLayerKindDto.Layered);
        if (completedSnapshot is null
            || !string.Equals(
                initialSnapshot.Fingerprint,
                completedSnapshot.Fingerprint,
                StringComparison.Ordinal))
        {
            throw new SemanticExploreValidationException(
                "The Guided Design source changed while the proposal was generated. Refresh and retry.",
                SemanticExploreFailureKind.StaleRevision);
        }

        var canImport = provider.Mutations.Count > 0
            && provider.StagingEdits.Count > 0
            && staged.IsValid
            && proposalValidation.CanImport
            && !pendingOverlay;
        var proposalId = CreateProposalId(
            completed.Revision,
            completedSnapshot,
            authoringContext,
            provider.NormalizedInput,
            targetWindow.NormalizedSearchText);
        var proposalFingerprint = CreateProposalFingerprint(
            proposalId,
            provider,
            staged.Session.PendingEdits,
            canImport,
            diagnostics,
            targetWindow.WindowCapped,
            targetWindow.TotalCount);
        var exports = canImport
            ? CreateExports(provider, proposalFingerprint)
            : new GuidedDesignCanonicalExportsDto(null, null);
        return new GuidedDesignGeneratedProposal(
            completed.Revision,
            completedSnapshot,
            capabilities,
            provider.NormalizedInput,
            provider.Seed,
            proposalId,
            proposalFingerprint,
            canImport,
            provider.SelectionRequired,
            targetWindow.NormalizedSearchText,
            targetWindow.WindowCapped,
            targetWindow.TotalCount,
            provider.EligibleTargets,
            provider.AffectedRecords,
            provider.Mutations,
            provider.Findings,
            exports,
            diagnostics,
            staged.Session.PendingEdits);
    }

    private static string CreateProposalId(
        SemanticProjectRevisionDto revision,
        SemanticSourceSnapshotDto snapshot,
        GeneratedChangeSetAuthoringContext context,
        GuidedDesignInputDto input,
        string? targetSearchText)
    {
        return Hash(
            "guided-design-proposal-id-v1",
            revision.ProjectId,
            revision.GameFamily.ToString(),
            revision.Generation,
            revision.Fingerprint,
            snapshot.Fingerprint,
            context.Fingerprint,
            targetSearchText,
            JsonSerializer.Serialize(input, BridgeJson.SerializerOptions));
    }

    private static string CreateProposalFingerprint(
        string proposalId,
        GuidedDesignProviderBuild provider,
        IReadOnlyList<PendingEdit> pendingEdits,
        bool canImport,
        IReadOnlyList<ApiDiagnostic> diagnostics,
        bool eligibleTargetWindowCapped,
        int totalEligibleTargetCount)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        GuidedDesignProviders.AppendHash(hash, "guided-design-proposal-fingerprint-v1");
        GuidedDesignProviders.AppendHash(hash, proposalId);
        GuidedDesignProviders.AppendHash(hash, provider.ProviderId);
        GuidedDesignProviders.AppendHash(hash, canImport ? "importable" : "read-only");
        GuidedDesignProviders.AppendHash(
            hash,
            eligibleTargetWindowCapped ? "target-window-capped" : "target-window-complete");
        GuidedDesignProviders.AppendHash(
            hash,
            totalEligibleTargetCount.ToString(CultureInfo.InvariantCulture));
        foreach (var mutation in provider.Mutations)
        {
            GuidedDesignProviders.AppendHash(
                hash,
                JsonSerializer.Serialize(mutation, BridgeJson.SerializerOptions));
        }

        GuidedDesignProviders.AppendHash(hash, null);
        foreach (var finding in provider.Findings)
        {
            GuidedDesignProviders.AppendHash(
                hash,
                JsonSerializer.Serialize(finding, BridgeJson.SerializerOptions));
        }

        GuidedDesignProviders.AppendHash(hash, null);
        foreach (var option in provider.EligibleTargets)
        {
            GuidedDesignProviders.AppendHash(
                hash,
                JsonSerializer.Serialize(option, BridgeJson.SerializerOptions));
        }

        GuidedDesignProviders.AppendHash(hash, null);
        foreach (var edit in pendingEdits
                     .Select(EditSessionBridgeMapper.ToPendingEditDto)
                     .OrderBy(edit => edit.Domain, StringComparer.Ordinal)
                     .ThenBy(edit => edit.RecordId, StringComparer.Ordinal)
                     .ThenBy(edit => edit.Field, StringComparer.Ordinal))
        {
            GuidedDesignProviders.AppendHash(
                hash,
                JsonSerializer.Serialize(edit with { Association = null }, BridgeJson.SerializerOptions));
        }

        GuidedDesignProviders.AppendHash(hash, null);
        foreach (var code in diagnostics.Select(diagnostic => diagnostic.Code ?? string.Empty)
                     .Order(StringComparer.Ordinal))
        {
            GuidedDesignProviders.AppendHash(hash, code);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static GuidedDesignCanonicalExportsDto CreateExports(
        GuidedDesignProviderBuild provider,
        string localProposalFingerprint)
    {
        // localProposalFingerprint deliberately does not cross into the
        // canonical spoiler. It binds private local authoring state only.
        _ = localProposalFingerprint;
        var mutationCommitments = provider.Mutations.Select(MutationCommitment).ToArray();
        var proposalCommitment = CreatePublicProposalCommitment(provider, mutationCommitments);
        var spoiler = CreateCanonicalExport(
            GuidedDesignCanonicalExportKindDto.Spoiler,
            "guided-design-spoiler.json",
            WriteCanonicalManifest(
                provider,
                proposalCommitment,
                includeOutcomes: true));
        // A useful spoiler can disclose exact path-safe semantic identities and
        // outcomes. A race artifact is intentionally withheld until there is a
        // reviewed nonce-based hiding and deterministic replay contract.
        return new GuidedDesignCanonicalExportsDto(spoiler, Race: null);
    }

    private static GuidedDesignCanonicalExportDto CreateCanonicalExport(
        GuidedDesignCanonicalExportKindDto kind,
        string fileName,
        string content)
    {
        if (Encoding.UTF8.GetByteCount(content) > GuidedDesignContract.MaximumCanonicalExportBytes)
        {
            throw new SemanticExploreValidationException(
                "The Guided Design canonical export exceeds its bounded size.",
                SemanticExploreFailureKind.LimitExceeded);
        }

        return new GuidedDesignCanonicalExportDto(
            kind,
            GuidedDesignContract.SchemaVersion,
            "application/json",
            fileName,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content))),
            content);
    }

    private static string WriteCanonicalManifest(
        GuidedDesignProviderBuild provider,
        string proposalCommitment,
        bool includeOutcomes)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.Default,
            Indented = false,
        }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", GuidedDesignContract.SchemaVersion);
            writer.WriteString("kind", includeOutcomes ? "spoiler" : "race");
            writer.WriteString(
                "gameFamily",
                JsonNamingPolicy.CamelCase.ConvertName(
                    provider.NormalizedInput.Targets[0].GameFamily.ToString()));
            writer.WriteString("providerId", provider.ProviderId);
            writer.WriteString("generatorSchema", "guided-design-v1");
            writer.WriteString(
                "rngAlgorithm",
                provider.NormalizedInput.Kind == GuidedDesignProposalKindDto.PokemonBaseStatShuffle
                    ? "sha256-counter-fisher-yates-v1"
                    : "none");
            writer.WriteString("proposalCommitment", proposalCommitment);
            WriteCanonicalConfig(writer, provider.NormalizedInput, includeOutcomes);
            writer.WritePropertyName(
                includeOutcomes ? "affectedRecords" : "affectedRecordCommitments");
            writer.WriteStartArray();
            foreach (var record in provider.AffectedRecords)
            {
                if (includeOutcomes)
                {
                    WriteCanonicalRecord(writer, record);
                }
                else
                {
                    writer.WriteStringValue(RecordCommitment(record));
                }
            }

            writer.WriteEndArray();
            if (includeOutcomes)
            {
                writer.WritePropertyName("mutations");
                writer.WriteStartArray();
                foreach (var mutation in provider.Mutations)
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName("record");
                    WriteCanonicalRecord(writer, mutation.Record);
                    writer.WriteString("fieldKey", mutation.FieldKey);
                    WriteCanonicalScalar(writer, "before", mutation.Before);
                    WriteCanonicalScalar(writer, "after", mutation.After);
                    writer.WriteBoolean("pinned", mutation.Pinned);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonicalConfig(
        Utf8JsonWriter writer,
        GuidedDesignInputDto input,
        bool includePinValues)
    {
        writer.WritePropertyName("config");
        writer.WriteStartObject();
        writer.WriteString("proposalKind", ProposalKindKey(input.Kind));
        WriteNullableNumber(writer, "delta", input.Delta);
        WriteNullableNumber(writer, "multiplierBasisPoints", input.MultiplierBasisPoints);
        WriteNullableNumber(writer, "minimumValue", input.MinimumValue);
        WriteNullableNumber(writer, "maximumValue", input.MaximumValue);
        if (input.Rounding is null)
        {
            writer.WriteNull("rounding");
        }
        else
        {
            writer.WriteString("rounding", RoundingKey(input.Rounding.Value));
        }

        if (input.Archetype is null)
        {
            writer.WriteNull("archetype");
        }
        else
        {
            writer.WriteString("archetype", ArchetypeKey(input.Archetype.Value));
        }

        if (input.Seed is null)
        {
            writer.WriteNull("seed");
        }
        else if (!includePinValues)
        {
            writer.WriteNull("seed");
            writer.WriteString(
                "seedCommitment",
                Hash("guided-design-public-seed-v1", input.Seed));
        }
        else
        {
            writer.WriteString("seed", input.Seed);
        }

        writer.WritePropertyName("fieldKeys");
        writer.WriteStartArray();
        foreach (var field in input.FieldKeys)
        {
            writer.WriteStringValue(field);
        }

        writer.WriteEndArray();
        writer.WritePropertyName(includePinValues ? "targets" : "targetCommitments");
        writer.WriteStartArray();
        foreach (var target in input.Targets)
        {
            if (includePinValues)
            {
                WriteCanonicalRecord(writer, target);
            }
            else
            {
                writer.WriteStringValue(RecordCommitment(target));
            }
        }

        writer.WriteEndArray();
        if (includePinValues)
        {
            writer.WritePropertyName("pins");
            writer.WriteStartArray();
            foreach (var pin in input.Pins)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("record");
                WriteCanonicalRecord(writer, pin.Record);
                writer.WriteString("fieldKey", pin.FieldKey);
                writer.WriteString("canonicalValue", pin.CanonicalValue);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }
        else
        {
            writer.WriteNumber("pinCount", input.Pins.Count);
        }

        writer.WriteEndObject();
    }

    private static void WriteCanonicalRecord(
        Utf8JsonWriter writer,
        SemanticRecordRefDto record)
    {
        _ = GuidedDesignProviders.RecordKey(record);
        writer.WriteStartObject();
        writer.WriteString(
            "gameFamily",
            JsonNamingPolicy.CamelCase.ConvertName(record.GameFamily.ToString()));
        writer.WriteString("domain", record.Domain);
        writer.WritePropertyName("recordKind");
        writer.WriteStartObject();
        writer.WriteString("key", record.RecordKind.Key);
        writer.WriteNumber("schemaVersion", record.RecordKind.SchemaVersion);
        writer.WriteEndObject();
        writer.WriteString("recordId", record.RecordId);
        if (record.SubrecordId is null)
        {
            writer.WriteNull("subrecordId");
        }
        else
        {
            writer.WriteString("subrecordId", record.SubrecordId);
        }

        writer.WriteEndObject();
    }

    private static void WriteCanonicalScalar(
        Utf8JsonWriter writer,
        string property,
        SemanticScalarValueDto value)
    {
        writer.WritePropertyName(property);
        writer.WriteStartObject();
        writer.WriteString("kind", ScalarKindKey(value.Kind));
        if (value.CanonicalValue is null)
        {
            writer.WriteNull("canonicalValue");
        }
        else
        {
            writer.WriteString("canonicalValue", value.CanonicalValue);
        }

        writer.WriteEndObject();
    }

    private static void WriteNullableNumber(Utf8JsonWriter writer, string property, int? value)
    {
        if (value is null)
        {
            writer.WriteNull(property);
        }
        else
        {
            writer.WriteNumber(property, value.Value);
        }
    }

    private static string CreatePublicProposalCommitment(
        GuidedDesignProviderBuild provider,
        IReadOnlyList<string> mutationCommitments)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        GuidedDesignProviders.AppendHash(hash, "guided-design-public-proposal-v1");
        GuidedDesignProviders.AppendHash(hash, ProposalKindKey(provider.NormalizedInput.Kind));
        GuidedDesignProviders.AppendHash(hash, provider.Seed);
        foreach (var target in provider.NormalizedInput.Targets)
        {
            GuidedDesignProviders.AppendHash(hash, RecordCommitment(target));
        }

        GuidedDesignProviders.AppendHash(hash, null);
        foreach (var pin in provider.NormalizedInput.Pins)
        {
            GuidedDesignProviders.AppendHash(hash, PinCommitment(pin));
        }

        GuidedDesignProviders.AppendHash(hash, null);
        foreach (var commitment in mutationCommitments)
        {
            GuidedDesignProviders.AppendHash(hash, commitment);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string MutationCommitment(GuidedDesignMutationDto mutation) => Hash(
        "guided-design-public-mutation-v1",
        RecordCommitment(mutation.Record),
        mutation.FieldKey,
        mutation.Before.Kind.ToString(),
        mutation.Before.CanonicalValue,
        mutation.After.Kind.ToString(),
        mutation.After.CanonicalValue,
        mutation.PinRecord is null ? null : RecordCommitment(mutation.PinRecord),
        mutation.PinFieldKey,
        mutation.Pinned ? "pinned" : "generated");

    private static string PinCommitment(GuidedDesignPinDto pin) => Hash(
        "guided-design-public-pin-v1",
        RecordCommitment(pin.Record),
        pin.FieldKey,
        pin.CanonicalValue);

    private static string RecordCommitment(SemanticRecordRefDto record) => Hash(
        "guided-design-public-record-v1",
        GuidedDesignProviders.RecordKey(record));

    private Func<EditSession, ChangePlanOutputModeDto?, ChangePlan> Planner(ProjectPathsDto paths) =>
        (session, outputMode) => createChangePlan(paths, session, outputMode);

    private static bool GuidedDesignInputsMatchExactly(
        GuidedDesignInputDto actual,
        GuidedDesignInputDto normalized) =>
        JsonSerializer.SerializeToUtf8Bytes(actual, BridgeJson.SerializerOptions)
            .AsSpan()
            .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(
                normalized,
                BridgeJson.SerializerOptions));

    private static bool HasRelevantPendingEdits(
        SemanticExploreScopeDto scope,
        GuidedDesignProposalKindDto kind)
    {
        return scope.PendingSession is { HasPendingChanges: true } session
            && session.PendingEdits.Any(edit =>
                edit.Association is null
                && string.Equals(
                    edit.Domain,
                    GuidedDesignProviders.DomainFor(kind),
                    StringComparison.Ordinal));
    }

    private static ChangeSetWorkspaceScopeDto ToChangeSetScope(SemanticExploreScopeDto scope) =>
        new(scope.ProjectId, scope.Paths);

    private static GuidedDesignInputDto NormalizePreviewSeed(
        GuidedDesignInputDto input,
        bool allowGeneration)
    {
        if (input.Kind != GuidedDesignProposalKindDto.PokemonBaseStatShuffle
            || input.Seed is not null)
        {
            return input;
        }

        if (!allowGeneration)
        {
            throw new SemanticExploreValidationException(
                "A Guided Design continuation must echo its generated canonical seed.",
                SemanticExploreFailureKind.InvalidCursor);
        }

        return input with
        {
            Seed = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)),
        };
    }

    private static string? NormalizeTargetSearch(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var normalized = value.Trim().Normalize(NormalizationForm.FormC);
        if (value.Length > GuidedDesignContract.MaximumTargetSearchTextLength
            || value.Any(IsUnsafeUnicode)
            || normalized.Length == 0
            || !string.Equals(value, normalized, StringComparison.Ordinal))
        {
            throw new SemanticExploreValidationException(
                "The Guided Design target search is invalid or too large.",
                SemanticExploreFailureKind.InvalidData);
        }

        return normalized;
    }

    private static TargetWindow ApplyTargetWindow(
        GuidedDesignProviderBuild provider,
        string? normalizedSearchText)
    {
        if (!provider.SelectionRequired)
        {
            if (normalizedSearchText is not null)
            {
                throw new SemanticExploreValidationException(
                    "Guided Design target search is available only before exact targets are selected.",
                    SemanticExploreFailureKind.InvalidData);
            }

            return new TargetWindow(provider, null, WindowCapped: false, TotalCount: 0);
        }

        var matching = normalizedSearchText is null
            ? provider.EligibleTargets
            : provider.EligibleTargets.Where(option =>
                option.RecordLabel.Contains(normalizedSearchText, StringComparison.OrdinalIgnoreCase)
                || GuidedDesignProviders.RecordKey(option.Record)
                    .Contains(normalizedSearchText, StringComparison.OrdinalIgnoreCase)).ToArray();
        var totalCount = matching.Count;
        var window = matching.Take(GuidedDesignContract.MaximumTargetSelectionWindow).ToArray();
        return new TargetWindow(
            provider with { EligibleTargets = window },
            normalizedSearchText,
            totalCount > window.Length,
            totalCount);
    }

    private static void ValidatePreviewRequest(PreviewGuidedDesignRequest request)
    {
        if (request is null
            || request.Scope is null
            || request.Scope.Paths is null
            || request.ExpectedRevision is null
            || request.Input is null
            || request.Input.Targets is null
            || request.Input.Pins is null
            || request.Input.FieldKeys is null
            || request.Input.Targets.Any(target => target is null)
            || request.Input.Pins.Any(pin => pin is null || pin.Record is null)
            || request.Input.FieldKeys.Any(field => field is null))
        {
            throw new SemanticExploreValidationException(
                "The Guided Design preview request is malformed.",
                SemanticExploreFailureKind.InvalidData);
        }

        ValidateRevision(request.ExpectedRevision);
        if (request.Layer != SemanticSourceLayerKindDto.Layered)
        {
            throw new SemanticExploreValidationException(
                "Guided Design currently supports only the exact layered source.",
                SemanticExploreFailureKind.Unsupported);
        }

        if (request.Limit is <= 0 or > GuidedDesignContract.MaximumPageSize)
        {
            throw new SemanticExploreValidationException(
                "A Guided Design page must request between 1 and 100 rows.",
                SemanticExploreFailureKind.LimitExceeded);
        }

        if (request.Cursor is { Length: > GuidedDesignContract.MaximumCursorLength }
            || request.Cursor?.Any(char.IsControl) == true)
        {
            throw new SemanticExploreValidationException(
                "The Guided Design continuation cursor is invalid.",
                SemanticExploreFailureKind.InvalidCursor);
        }

        var continuation = request.Cursor is not null;
        if (continuation != request.ProposalId is not null
            || continuation != request.ProposalFingerprint is not null)
        {
            throw new SemanticExploreValidationException(
                "A Guided Design continuation requires its exact proposal identity.",
                SemanticExploreFailureKind.InvalidCursor);
        }

        if (request.Input.Targets.Count > 0 && request.TargetSearchText is not null)
        {
            throw new SemanticExploreValidationException(
                "Guided Design target search cannot accompany exact selected targets.",
                SemanticExploreFailureKind.InvalidData);
        }

        ValidateOptionalSha256(request.ExpectedChangeSetETag, "change-set ETag");
        ValidateOptionalSha256(request.ProposalId, "proposal ID");
        ValidateOptionalSha256(request.ProposalFingerprint, "proposal fingerprint");
    }

    private static void ValidateImportRequest(ImportGuidedDesignProposalRequest request)
    {
        if (request is null
            || request.Scope is null
            || request.Scope.Paths is null
            || request.ExpectedRevision is null
            || request.Input is null
            || request.Input.Targets is null
            || request.Input.Pins is null
            || request.Input.FieldKeys is null
            || request.Input.Targets.Any(target => target is null)
            || request.Input.Pins.Any(pin => pin is null || pin.Record is null)
            || request.Input.FieldKeys.Any(field => field is null))
        {
            throw new SemanticExploreValidationException(
                "The Guided Design import request is malformed.",
                SemanticExploreFailureKind.InvalidData);
        }

        ValidateRevision(request.ExpectedRevision);
        if (request.Input.Kind == GuidedDesignProposalKindDto.PokemonBaseStatShuffle
            && request.Input.Seed is null)
        {
            throw new SemanticExploreValidationException(
                "A Guided Design import must echo the canonical seed returned by preview.",
                SemanticExploreFailureKind.InvalidData);
        }

        ValidateSha256(request.ProposalId, "proposal ID");
        ValidateSha256(request.ProposalFingerprint, "proposal fingerprint");
        ValidateOptionalSha256(request.ExpectedChangeSetETag, "change-set ETag");
        if (string.IsNullOrWhiteSpace(request.ChangeSetName)
            || request.ChangeSetName != request.ChangeSetName.Trim()
            || request.ChangeSetName.Length > GuidedDesignContract.MaximumChangeSetNameLength
            || request.ChangeSetName.Any(IsUnsafeUnicode))
        {
            throw new SemanticExploreValidationException(
                "The Guided Design change-set name is invalid.",
                SemanticExploreFailureKind.InvalidData);
        }
    }

    private static void ValidateRevision(SemanticProjectRevisionDto revision)
    {
        if (string.IsNullOrWhiteSpace(revision.ProjectId)
            || revision.ProjectId != revision.ProjectId.Trim()
            || !Enum.IsDefined(revision.GameFamily)
            || !long.TryParse(
                revision.Generation,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var generation)
            || generation < 0)
        {
            throw new SemanticExploreValidationException(
                "The Guided Design expected source revision is invalid.",
                SemanticExploreFailureKind.InvalidData);
        }

        ValidateSha256(revision.Fingerprint, "source revision fingerprint");
    }

    private static void ValidateOptionalSha256(string? value, string label)
    {
        if (value is not null)
        {
            ValidateSha256(value, label);
        }
    }

    private static void ValidateSha256(string value, string label)
    {
        if (value is not { Length: 64 }
            || value.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw new SemanticExploreValidationException(
                $"The Guided Design {label} is invalid.",
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
                "The Guided Design source revision changed. Refresh the workspace and retry.",
                SemanticExploreFailureKind.StaleRevision);
        }
    }

    private static void EnsureSameSemanticObservation(
        ReadSemanticCapabilitiesResponse initial,
        ReadSemanticCapabilitiesResponse completed)
    {
        if (!Equals(initial.Revision, completed.Revision)
            || initial.Snapshots.Count != completed.Snapshots.Count
            || initial.Snapshots.Zip(completed.Snapshots).Any(pair => !Equals(pair.First, pair.Second)))
        {
            throw new SemanticExploreValidationException(
                "The Guided Design source changed while capabilities were read. Refresh and retry.",
                SemanticExploreFailureKind.StaleRevision);
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
                || !int.TryParse(
                    offsetValue,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var offset)
                || offset < 0)
            {
                throw new FormatException();
            }

            return offset;
        }
        catch (Exception exception) when (exception is FormatException or DecoderFallbackException)
        {
            throw new SemanticExploreValidationException(
                "The Guided Design continuation cursor is invalid for this proposal.",
                SemanticExploreFailureKind.InvalidCursor,
                exception);
        }
    }

    private static string Hash(string prefix, params string?[] values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        GuidedDesignProviders.AppendHash(hash, prefix);
        foreach (var value in values)
        {
            GuidedDesignProviders.AppendHash(hash, value);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string ProposalKindKey(GuidedDesignProposalKindDto kind) =>
        JsonNamingPolicy.CamelCase.ConvertName(kind.ToString());

    private static string RoundingKey(GuidedDesignRoundingDto rounding) =>
        JsonNamingPolicy.CamelCase.ConvertName(rounding.ToString());

    private static string ArchetypeKey(GuidedDesignTrainerArchetypeDto archetype) =>
        JsonNamingPolicy.CamelCase.ConvertName(archetype.ToString());

    private static string ScalarKindKey(SemanticValueKindDto kind) =>
        JsonNamingPolicy.CamelCase.ConvertName(kind.ToString());

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

    private static GuidedDesignValidationException StaleProposal(string message) =>
        new(message, GuidedDesignFailureKind.StaleProposal);

    private sealed record GuidedDesignGeneratedProposal(
        SemanticProjectRevisionDto Revision,
        SemanticSourceSnapshotDto Snapshot,
        IReadOnlyList<GuidedDesignCapabilityDto> Capabilities,
        GuidedDesignInputDto NormalizedInput,
        string? Seed,
        string ProposalId,
        string ProposalFingerprint,
        bool CanImport,
        bool SelectionRequired,
        string? NormalizedTargetSearchText,
        bool EligibleTargetWindowCapped,
        int TotalEligibleTargetCount,
        IReadOnlyList<GuidedDesignTargetOptionDto> EligibleTargets,
        IReadOnlyList<SemanticRecordRefDto> AffectedRecords,
        IReadOnlyList<GuidedDesignMutationDto> Mutations,
        IReadOnlyList<GuidedDesignFindingDto> Findings,
        GuidedDesignCanonicalExportsDto Exports,
        IReadOnlyList<ApiDiagnostic> Diagnostics,
        IReadOnlyList<PendingEdit> PendingEdits);

    private sealed record TargetWindow(
        GuidedDesignProviderBuild Provider,
        string? NormalizedSearchText,
        bool WindowCapped,
        int TotalCount);

    private sealed record CapabilityCacheEntry(
        IReadOnlyList<GuidedDesignCapabilityDto> Capabilities,
        LinkedListNode<string> UsageNode);

    private sealed record ProjectWorkflowSources(
        TrainersWorkflowDto? Trainers,
        EncountersWorkflowDto? Encounters,
        ItemsWorkflowDto? Items,
        PokemonWorkflowDto? Pokemon);

    private sealed record ProjectCapabilityRead(
        IReadOnlyList<GuidedDesignCapabilityDto> Capabilities,
        bool Cacheable);
}
