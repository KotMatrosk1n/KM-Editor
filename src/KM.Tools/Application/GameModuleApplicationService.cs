// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using KM.Api.Encounters;
using KM.Api.GameModules;
using KM.Api.Moves;
using KM.Api.Pokemon;
using KM.Api.Projects;
using KM.Api.Raids;
using KM.Api.Semantics;
using KM.Api.Trainers;
using KM.Api.TrainerPools;
using KM.Core.Indexing;
using KM.Core.Projects;
using KM.Core.Semantics;
using KM.SV.GameModules;
using KM.ZA.GameModules;

namespace KM.Tools.Application;

public sealed class GameModuleApplicationService
{
    private const string CacheCallerKeyPrefix = "game-modules-v1";

    private readonly SemanticExploreApplicationService semanticExploreService;
    private readonly Func<ProjectPathsDto, TeraRaidsWorkflowDto> loadTeraRaidsFresh;
    private readonly Func<ProjectPathsDto, SvPackedLooseSourceComparison>
        loadPackedLooseSourceComparisonFresh;
    private readonly Func<ProjectPathsDto, SvEventDataComparison>
        loadEventDataComparisonFresh;
    private readonly Func<ProjectPathsDto, SvScenePlacementProjection>
        loadScenePlacementProjectionFresh;
    private readonly Func<ProjectPathsDto, SvTypeEffectivenessStateProjection>
        loadScarletVioletTypeEffectivenessStateFresh;
    private readonly Func<ProjectPathsDto, (EncountersWorkflowDto Encounters, MovesWorkflowDto Moves)>
        loadScriptedBossTimelineFresh;
    private readonly Func<ProjectPathsDto, SwordShieldGameModuleSourceBatchDto>
        loadSwordShieldCapabilityBatchFresh;
    private readonly Func<
        ProjectPathsDto,
        (
            EncountersWorkflowDto ScriptedBossEncounters,
            EncountersWorkflowDto WildEncounters,
            MovesWorkflowDto Moves,
            TrainersWorkflowDto Trainers,
            EncounterCompatibilityWorkflowDto EncounterCompatibility,
            PokemonWorkflowDto Pokemon,
            TrainerPoolsWorkflowDto TrainerPools,
            LegendsZaTypeEffectivenessStateDto TypeEffectivenessState,
            ZaStaticMapMarkerCatalog StaticMapMarkers,
            ZaNamedFlagCatalog NamedFlagCatalog,
            ZaPokemonResourceCatalog PokemonResourceCatalog)>
        loadZaCapabilityBatchFresh;
    private readonly Func<ProjectPathsDto, TrainersWorkflowDto> loadTrainersFresh;
    private readonly Func<ProjectPathsDto, EncountersWorkflowDto> loadEncountersFresh;
    private readonly Func<ProjectPathsDto, MovesWorkflowDto> loadMovesFresh;
    private readonly Func<ProjectPathsDto, EncounterCompatibilityWorkflowDto>
        loadEncounterCompatibilityFresh;
    private readonly Func<ProjectPathsDto, PokemonWorkflowDto> loadPokemonFresh;
    private readonly Func<ProjectPathsDto, TrainerPoolsWorkflowDto> loadTrainerPoolsFresh;
    private readonly Func<ProjectPathsDto, LegendsZaTypeEffectivenessStateDto>
        loadTypeEffectivenessStateFresh;
    private readonly Func<ProjectPathsDto, ZaStaticMapMarkerCatalog>
        loadStaticMapMarkersFresh;
    private readonly Func<ProjectPathsDto, ZaNamedFlagCatalog>
        loadNamedFlagCatalogFresh;
    private readonly Func<ProjectPathsDto, ZaPokemonResourceCatalog>
        loadPokemonResourceCatalogFresh;
    private readonly BoundedDerivedIndexCache<GameModuleData> cache = new(
        new BoundedDerivedIndexCacheOptions
        {
            MaximumEntryCount = 12,
            MaximumSizeBytes = GameModuleSizingLimits.ModuleCacheCeilingBytes,
        });

    public GameModuleApplicationService(
        SemanticExploreApplicationService semanticExploreService,
        Func<ProjectPathsDto, TeraRaidsWorkflowDto> loadTeraRaidsFresh,
        Func<ProjectPathsDto, SvPackedLooseSourceComparison>
            loadPackedLooseSourceComparisonFresh,
        Func<ProjectPathsDto, SvEventDataComparison>
            loadEventDataComparisonFresh,
        Func<ProjectPathsDto, SvScenePlacementProjection>
            loadScenePlacementProjectionFresh,
        Func<ProjectPathsDto, SvTypeEffectivenessStateProjection>
            loadScarletVioletTypeEffectivenessStateFresh,
        Func<ProjectPathsDto, (EncountersWorkflowDto Encounters, MovesWorkflowDto Moves)>
            loadScriptedBossTimelineFresh,
        Func<ProjectPathsDto, SwordShieldGameModuleSourceBatchDto>
            loadSwordShieldCapabilityBatchFresh,
        Func<
            ProjectPathsDto,
            (
                EncountersWorkflowDto ScriptedBossEncounters,
                EncountersWorkflowDto WildEncounters,
                MovesWorkflowDto Moves,
                TrainersWorkflowDto Trainers,
                EncounterCompatibilityWorkflowDto EncounterCompatibility,
                PokemonWorkflowDto Pokemon,
                TrainerPoolsWorkflowDto TrainerPools,
                LegendsZaTypeEffectivenessStateDto TypeEffectivenessState,
                ZaStaticMapMarkerCatalog StaticMapMarkers,
                ZaNamedFlagCatalog NamedFlagCatalog,
                ZaPokemonResourceCatalog PokemonResourceCatalog)>
            loadZaCapabilityBatchFresh,
        Func<ProjectPathsDto, TrainersWorkflowDto> loadTrainersFresh,
        Func<ProjectPathsDto, EncountersWorkflowDto> loadEncountersFresh,
        Func<ProjectPathsDto, MovesWorkflowDto> loadMovesFresh,
        Func<ProjectPathsDto, EncounterCompatibilityWorkflowDto> loadEncounterCompatibilityFresh,
        Func<ProjectPathsDto, PokemonWorkflowDto> loadPokemonFresh,
        Func<ProjectPathsDto, TrainerPoolsWorkflowDto> loadTrainerPoolsFresh,
        Func<ProjectPathsDto, LegendsZaTypeEffectivenessStateDto>
            loadTypeEffectivenessStateFresh,
        Func<ProjectPathsDto, ZaStaticMapMarkerCatalog> loadStaticMapMarkersFresh,
        Func<ProjectPathsDto, ZaNamedFlagCatalog> loadNamedFlagCatalogFresh,
        Func<ProjectPathsDto, ZaPokemonResourceCatalog> loadPokemonResourceCatalogFresh)
    {
        this.semanticExploreService = semanticExploreService
            ?? throw new ArgumentNullException(nameof(semanticExploreService));
        this.loadTeraRaidsFresh = loadTeraRaidsFresh
            ?? throw new ArgumentNullException(nameof(loadTeraRaidsFresh));
        this.loadPackedLooseSourceComparisonFresh = loadPackedLooseSourceComparisonFresh
            ?? throw new ArgumentNullException(nameof(loadPackedLooseSourceComparisonFresh));
        this.loadEventDataComparisonFresh = loadEventDataComparisonFresh
            ?? throw new ArgumentNullException(nameof(loadEventDataComparisonFresh));
        this.loadScenePlacementProjectionFresh = loadScenePlacementProjectionFresh
            ?? throw new ArgumentNullException(nameof(loadScenePlacementProjectionFresh));
        this.loadScarletVioletTypeEffectivenessStateFresh =
            loadScarletVioletTypeEffectivenessStateFresh
            ?? throw new ArgumentNullException(
                nameof(loadScarletVioletTypeEffectivenessStateFresh));
        this.loadScriptedBossTimelineFresh = loadScriptedBossTimelineFresh
            ?? throw new ArgumentNullException(nameof(loadScriptedBossTimelineFresh));
        this.loadSwordShieldCapabilityBatchFresh = loadSwordShieldCapabilityBatchFresh
            ?? throw new ArgumentNullException(nameof(loadSwordShieldCapabilityBatchFresh));
        this.loadZaCapabilityBatchFresh = loadZaCapabilityBatchFresh
            ?? throw new ArgumentNullException(nameof(loadZaCapabilityBatchFresh));
        this.loadTrainersFresh = loadTrainersFresh
            ?? throw new ArgumentNullException(nameof(loadTrainersFresh));
        this.loadEncountersFresh = loadEncountersFresh
            ?? throw new ArgumentNullException(nameof(loadEncountersFresh));
        this.loadMovesFresh = loadMovesFresh ?? throw new ArgumentNullException(nameof(loadMovesFresh));
        this.loadEncounterCompatibilityFresh = loadEncounterCompatibilityFresh
            ?? throw new ArgumentNullException(nameof(loadEncounterCompatibilityFresh));
        this.loadPokemonFresh = loadPokemonFresh
            ?? throw new ArgumentNullException(nameof(loadPokemonFresh));
        this.loadTrainerPoolsFresh = loadTrainerPoolsFresh
            ?? throw new ArgumentNullException(nameof(loadTrainerPoolsFresh));
        this.loadTypeEffectivenessStateFresh = loadTypeEffectivenessStateFresh
            ?? throw new ArgumentNullException(nameof(loadTypeEffectivenessStateFresh));
        this.loadStaticMapMarkersFresh = loadStaticMapMarkersFresh
            ?? throw new ArgumentNullException(nameof(loadStaticMapMarkersFresh));
        this.loadNamedFlagCatalogFresh = loadNamedFlagCatalogFresh
            ?? throw new ArgumentNullException(nameof(loadNamedFlagCatalogFresh));
        this.loadPokemonResourceCatalogFresh = loadPokemonResourceCatalogFresh
            ?? throw new ArgumentNullException(nameof(loadPokemonResourceCatalogFresh));
    }

    public ReadGameModuleCapabilitiesResponse ReadCapabilities(
        ReadGameModuleCapabilitiesRequest request)
    {
        if (request?.Scope is null)
        {
            throw new SemanticExploreValidationException(
                "The game-specific module capability request is malformed.",
                SemanticExploreFailureKind.InvalidData);
        }

        var semantic = semanticExploreService.ReadCapabilities(
            new ReadSemanticCapabilitiesRequest(request.Scope));
        var sourceCacheIdentity = semanticExploreService.ReadSourceCacheIdentity(request.Scope);
        var declaredCapabilities = GameModuleProviders.Capabilities(semantic.Revision.GameFamily);
        var resolvedByModule = new Dictionary<GameModuleDto, GameModuleCapabilityDto>();
        var pendingCacheItems = new List<(DerivedIndexCacheKey Key, DerivedIndexCacheItem<GameModuleData> Item)>();
        long pendingCacheBytes = 0;
        var missing = new List<(GameModuleCapabilityDto Capability, DerivedIndexCacheKey Key)>();
        foreach (var declared in declaredCapabilities.Where(capability => capability.CanQuery))
        {
            var key = CacheKey(
                semantic.Revision,
                SemanticSourceLayerKindDto.Layered,
                declared.Module,
                sourceCacheIdentity);
            if (cache.TryGet(key, out var cached))
            {
                resolvedByModule.Add(declared.Module, cached.Capability);
            }
            else
            {
                missing.Add((declared, key));
            }
        }

        if (missing.Count > 0)
        {
            if (semantic.Revision.GameFamily == SemanticGameFamilyDto.SwordShield)
            {
                try
                {
                    var sources = loadSwordShieldCapabilityBatchFresh(request.Scope.Paths);
                    foreach (var candidate in missing)
                    {
                        ResolveCapabilityCandidate(
                            candidate,
                            () => BuildSwordShieldCapabilityModule(
                                candidate.Capability.Module,
                                sources),
                            resolvedByModule,
                            pendingCacheItems,
                            ref pendingCacheBytes);
                    }
                }
                catch (Exception exception) when (IsReadinessFailure(exception))
                {
                    foreach (var candidate in missing)
                    {
                        ResolveCapabilityCandidate(
                            candidate,
                            () => BuildModule(
                                request.Scope,
                                candidate.Capability.Module,
                                CancellationToken.None),
                            resolvedByModule,
                            pendingCacheItems,
                            ref pendingCacheBytes);
                    }
                }
            }
            else if (semantic.Revision.GameFamily == SemanticGameFamilyDto.LegendsZA)
            {
                try
                {
                    var sources = loadZaCapabilityBatchFresh(request.Scope.Paths);
                    foreach (var candidate in missing)
                    {
                        ResolveCapabilityCandidate(
                            candidate,
                            () => BuildZaCapabilityModule(candidate.Capability.Module, sources),
                            resolvedByModule,
                            pendingCacheItems,
                            ref pendingCacheBytes);
                    }
                }
                catch (Exception exception) when (IsReadinessFailure(exception))
                {
                    foreach (var candidate in missing)
                    {
                        ResolveCapabilityCandidate(
                            candidate,
                            () => BuildModule(
                                request.Scope,
                                candidate.Capability.Module,
                                CancellationToken.None),
                            resolvedByModule,
                            pendingCacheItems,
                            ref pendingCacheBytes);
                    }
                }
            }
            else
            {
                foreach (var candidate in missing)
                {
                    ResolveCapabilityCandidate(
                        candidate,
                        () => BuildModule(request.Scope, candidate.Capability.Module, CancellationToken.None),
                        resolvedByModule,
                        pendingCacheItems,
                        ref pendingCacheBytes);
                }
            }
        }

        var completed = semanticExploreService.ReadCapabilities(
            new ReadSemanticCapabilitiesRequest(request.Scope));
        EnsureSameObservation(semantic, completed);
        foreach (var pending in pendingCacheItems)
        {
            _ = cache.Set(pending.Key, pending.Item);
        }

        return new ReadGameModuleCapabilitiesResponse(
            completed.Revision,
            completed.Snapshots,
            declaredCapabilities
                .Select(capability => capability.CanQuery
                    ? resolvedByModule[capability.Module]
                    : capability)
                .ToArray());
    }

    public QueryGameModuleResponse Query(
        QueryGameModuleRequest request,
        CancellationToken cancellationToken = default)
    {
        return QueryAsync(request, cancellationToken).AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask<QueryGameModuleResponse> QueryAsync(
        QueryGameModuleRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new SemanticExploreValidationException(
                "The game-specific module query is malformed.",
                SemanticExploreFailureKind.InvalidData);
        }

        ValidateRequest(request);
        cancellationToken.ThrowIfCancellationRequested();
        var semantic = semanticExploreService.ReadCapabilities(
            new ReadSemanticCapabilitiesRequest(request.Scope));
        EnsureExpectedRevision(semantic.Revision, request.ExpectedRevision);
        if (GameModuleProviders.ModuleFamily(request.Module) != semantic.Revision.GameFamily)
        {
            throw new SemanticExploreValidationException(
                "The requested module does not belong to the selected game family.",
                SemanticExploreFailureKind.Unsupported);
        }

        var declaredCapability = GameModuleProviders.Capabilities(semantic.Revision.GameFamily)
            .Single(candidate => candidate.Module == request.Module);
        if (!declaredCapability.CanQuery || !declaredCapability.SupportedLayers.Contains(request.Layer))
        {
            throw new SemanticExploreValidationException(
                "The requested game-specific module does not have a bounded read-only provider.",
                SemanticExploreFailureKind.Unsupported);
        }

        var snapshot = semantic.Snapshots.SingleOrDefault(candidate => candidate.Layer.Kind == request.Layer)
            ?? throw new SemanticExploreValidationException(
                "The requested game-specific module source layer is unavailable.",
                SemanticExploreFailureKind.Unsupported);
        var queryFingerprint = QueryFingerprint(request, snapshot);
        var offset = DecodeCursor(request.Cursor, queryFingerprint);
        var sourceCacheIdentity = semanticExploreService.ReadSourceCacheIdentity(request.Scope);
        var key = CacheKey(
            semantic.Revision,
            request.Layer,
            request.Module,
            sourceCacheIdentity);

        GameModuleData data;
        DerivedIndexCacheItem<GameModuleData>? pendingCacheItem = null;
        if (!cache.TryGet(key, out data))
        {
            var built = await ValueTask.FromResult(
                BuildModule(request.Scope, request.Module, cancellationToken)).ConfigureAwait(false);
            data = built.Value;
            pendingCacheItem = data.Cacheable
                && built.SizeBytes <= GameModuleSizingLimits.ModuleCacheCeilingBytes
                ? built
                : null;
        }

        if (!data.Capability.CanQuery || !data.Capability.SupportedLayers.Contains(request.Layer))
        {
            throw new SemanticExploreValidationException(
                "The requested game-specific module source is not ready for bounded analysis.",
                SemanticExploreFailureKind.Unsupported);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var (completedRevision, completedSnapshot) = ReadCompletedSnapshot(request, snapshot);
        if (pendingCacheItem is not null && !cache.Set(key, pendingCacheItem))
        {
            throw new SemanticExploreValidationException(
                "The game-specific module exceeds its bounded cache budget.",
                SemanticExploreFailureKind.LimitExceeded);
        }

        var terms = AnalysisCatalog.SearchTerms(request.SearchText);
        var matches = data.Records.Where(record =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return (request.RecordId is null || record.RecordId == request.RecordId)
                && (terms.Length == 0 || AnalysisCatalog.Matches(terms, new[] { record.Title, record.RecordId, record.RecordKind,
                    record.GroupId, record.ParentRecordId, record.Target?.RecordId, record.Summary }
                .Concat(record.Facts.SelectMany(fact => new[] { fact.Label, fact.Value.DisplayValue, fact.Value.CanonicalValue }))));
        }).ToArray();
        if (offset > matches.Length
            || request.Cursor is not null && (offset == 0 || offset >= matches.Length))
        {
            throw new SemanticExploreValidationException(
                "The game-specific module continuation cursor is outside the current result set.",
                SemanticExploreFailureKind.InvalidCursor);
        }

        var records = matches.Skip(offset).Take(request.Limit)
            .Select(record => request.CatalogOnly ? record with { Facts = [], Summary = string.Empty } : record).ToArray();
        var nextOffset = checked(offset + records.Length);
        var nextCursor = nextOffset < matches.Length
            ? EncodeCursor(queryFingerprint, nextOffset)
            : null;
        return new QueryGameModuleResponse(
            completedRevision,
            queryFingerprint,
            completedSnapshot,
            data.Capability,
            matches.Length,
            records,
            data.Diagnostics,
            nextCursor);
    }

    private DerivedIndexCacheItem<GameModuleData> BuildModule(
        SemanticExploreScopeDto scope,
        GameModuleDto module,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GameModuleData data;
        try
        {
            data = module switch
            {
                GameModuleDto.ScarletVioletTeraRaidAnalysis =>
                    GameModuleProviders.BuildTeraRaidAnalysis(loadTeraRaidsFresh(scope.Paths)),
                GameModuleDto.ScarletVioletPackedLooseComparison =>
                    GameModuleProviders.BuildPackedLooseSourceComparison(
                        loadPackedLooseSourceComparisonFresh(scope.Paths)),
                GameModuleDto.ScarletVioletEventDataComparison =>
                    GameModuleProviders.BuildEventDataComparison(
                        loadEventDataComparisonFresh(scope.Paths)),
                GameModuleDto.ScarletVioletScenePlacementEditing =>
                    GameModuleProviders.BuildScenePlacementProjection(
                        loadScenePlacementProjectionFresh(scope.Paths)),
                GameModuleDto.ScarletVioletTypeEffectivenessState =>
                    GameModuleProviders.BuildScarletVioletTypeEffectivenessState(
                        loadScarletVioletTypeEffectivenessStateFresh(scope.Paths)),
                GameModuleDto.LegendsZaScriptedBossTimeline =>
                    BuildScriptedBossTimeline(scope.Paths),
                GameModuleDto.LegendsZaTrainerArchetypes =>
                    GameModuleProviders.BuildTrainerArchetypes(loadTrainersFresh(scope.Paths)),
                GameModuleDto.LegendsZaWildSpawnExplorer =>
                    GameModuleProviders.BuildWildSpawnExplorer(loadEncountersFresh(scope.Paths)),
                GameModuleDto.LegendsZaEncounterCompatibility =>
                    GameModuleProviders.BuildEncounterCompatibility(
                        loadPokemonFresh(scope.Paths),
                        loadEncounterCompatibilityFresh(scope.Paths)),
                GameModuleDto.LegendsZaAlphaMoveDistribution =>
                    GameModuleProviders.BuildAlphaMoveDistribution(loadPokemonFresh(scope.Paths)),
                GameModuleDto.LegendsZaDexLayoutPlanning =>
                    GameModuleProviders.BuildDexLayoutPlanning(loadPokemonFresh(scope.Paths)),
                GameModuleDto.LegendsZaMoveVariantComparison =>
                    GameModuleProviders.BuildMoveVariantComparison(loadMovesFresh(scope.Paths)),
                GameModuleDto.LegendsZaTrainerPoolSwitching =>
                    GameModuleProviders.BuildTrainerPoolSwitching(loadTrainerPoolsFresh(scope.Paths)),
                GameModuleDto.LegendsZaTypeEffectivenessState =>
                    GameModuleProviders.BuildTypeEffectivenessState(
                        loadTypeEffectivenessStateFresh(scope.Paths)),
                GameModuleDto.LegendsZaStaticMapMarkers =>
                    GameModuleProviders.BuildStaticMapMarkers(
                        loadStaticMapMarkersFresh(scope.Paths)),
                GameModuleDto.LegendsZaNamedFlagCatalog =>
                    GameModuleProviders.BuildNamedFlagCatalog(
                        loadNamedFlagCatalogFresh(scope.Paths)),
                GameModuleDto.LegendsZaPokemonResourceCatalog =>
                    GameModuleProviders.BuildPokemonResourceCatalog(
                        loadPokemonResourceCatalogFresh(scope.Paths)),
                GameModuleDto.SwordShieldRewardEcosystem =>
                    BuildSwordShieldModule(scope.Paths, module),
                GameModuleDto.SwordShieldExeFsCompatibility =>
                    BuildSwordShieldModule(scope.Paths, module),
                GameModuleDto.SwordShieldDynamaxAdventures =>
                    BuildSwordShieldModule(scope.Paths, module),
                GameModuleDto.SwordShieldRoyalCandyProgression =>
                    BuildSwordShieldModule(scope.Paths, module),
                GameModuleDto.SwordShieldBattleCafeRewards =>
                    BuildSwordShieldModule(scope.Paths, module),
                GameModuleDto.SwordShieldEventAssignments =>
                    BuildSwordShieldModule(scope.Paths, module),
                _ => throw new SemanticExploreValidationException(
                    "The requested game-specific module is unavailable.",
                    SemanticExploreFailureKind.Unsupported),
            };
        }
        catch (SemanticExploreValidationException)
        {
            throw;
        }
        catch (SvPackedLooseSourceObservationChangedException exception)
        {
            throw new SemanticExploreValidationException(
                "The Scarlet/Violet source candidates changed during comparison. Refresh and retry.",
                SemanticExploreFailureKind.StaleRevision,
                exception);
        }
        catch (SvEventDataObservationChangedException exception)
        {
            throw new SemanticExploreValidationException(
                "The Scarlet/Violet event sources changed during comparison. Refresh and retry.",
                SemanticExploreFailureKind.StaleRevision,
                exception);
        }
        catch (SvScenePlacementObservationChangedException exception)
        {
            throw new SemanticExploreValidationException(
                "The Scarlet/Violet placement sources changed during inspection. Refresh and retry.",
                SemanticExploreFailureKind.StaleRevision,
                exception);
        }
        catch (SvTypeEffectivenessObservationChangedException exception)
        {
            throw new SemanticExploreValidationException(
                "The Scarlet/Violet type-effectiveness source changed during inspection. Refresh and retry.",
                SemanticExploreFailureKind.StaleRevision,
                exception);
        }
        catch (SvTypeEffectivenessUnsupportedSourceException exception)
        {
            throw new SemanticExploreValidationException(
                "The Scarlet/Violet type-effectiveness source is not an exact supported build.",
                SemanticExploreFailureKind.Unsupported,
                exception);
        }
        catch (Exception exception) when (IsBoundedFailure(exception))
        {
            throw new SemanticExploreValidationException(
                "The game-specific module source exceeds its bounded decode limits.",
                SemanticExploreFailureKind.LimitExceeded,
                exception);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or ArgumentException or FormatException)
        {
            throw new SemanticExploreValidationException(
                "The game-specific module source is invalid.",
                SemanticExploreFailureKind.InvalidData,
                exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            data = new GameModuleData(
                GameModuleProviders.Capabilities(GameModuleProviders.ModuleFamily(module))
                    .Single(capability => capability.Module == module) with
                {
                    State = SemanticCoverageStateDto.Unavailable,
                    Confidence = SemanticConfidenceDto.Unknown,
                    CanQuery = false,
                    ReasonCode = "workflow-source-unavailable",
                    SupportedLayers = [],
                },
                [],
                [],
                Cacheable: false);
        }
        catch (OverflowException exception)
        {
            throw new SemanticExploreValidationException(
                "The game-specific module source exceeds its bounded numeric limits.",
                SemanticExploreFailureKind.LimitExceeded,
                exception);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var estimatedSize = GameModuleProviders.EstimateSizeBytes(data);
        return new DerivedIndexCacheItem<GameModuleData>(data, estimatedSize);
    }

    private GameModuleData BuildScriptedBossTimeline(ProjectPathsDto paths)
    {
        var sources = loadScriptedBossTimelineFresh(paths);
        return GameModuleProviders.BuildScriptedBossTimeline(sources.Encounters, sources.Moves);
    }

    private GameModuleData BuildSwordShieldModule(ProjectPathsDto paths, GameModuleDto module)
    {
        return BuildSwordShieldCapabilityModule(
            module,
            loadSwordShieldCapabilityBatchFresh(paths)).Value;
    }

    private static DerivedIndexCacheItem<GameModuleData> BuildSwordShieldCapabilityModule(
        GameModuleDto module,
        SwordShieldGameModuleSourceBatchDto sources)
    {
        var data = module switch
        {
            GameModuleDto.SwordShieldRewardEcosystem =>
                GameModuleProviders.BuildRewardEcosystem(
                    sources.RewardEcosystem.NpcItemGifts,
                    sources.RewardEcosystem.RaidRewards,
                    sources.RewardEcosystem.RaidBonusRewards,
                    sources.RewardEcosystem.Shops,
                    sources.RewardEcosystem.Placement),
            GameModuleDto.SwordShieldExeFsCompatibility =>
                GameModuleProviders.BuildExeFsCompatibility(sources.ExeFsCompatibility),
            GameModuleDto.SwordShieldDynamaxAdventures =>
                GameModuleProviders.BuildDynamaxAdventures(
                    sources.DynamaxAdventures,
                    sources.RentalPokemon,
                    sources.RewardEcosystem.RaidRewards),
            GameModuleDto.SwordShieldRoyalCandyProgression =>
                GameModuleProviders.BuildRoyalCandyProgression(sources.RoyalCandyProgression),
            GameModuleDto.SwordShieldBattleCafeRewards =>
                GameModuleProviders.BuildBattleCafeRewards(sources.BattleCafeRewards),
            GameModuleDto.SwordShieldEventAssignments =>
                GameModuleProviders.BuildEventAssignments(
                    sources.EventAssignments,
                    sources.ExeFsCompatibility),
            _ => throw new SemanticExploreValidationException(
                "The requested Sword and Shield game-specific module is unavailable.",
                SemanticExploreFailureKind.Unsupported),
        };
        return new DerivedIndexCacheItem<GameModuleData>(
            data,
            GameModuleProviders.EstimateSizeBytes(data));
    }

    private static DerivedIndexCacheItem<GameModuleData> BuildZaCapabilityModule(
        GameModuleDto module,
        (
            EncountersWorkflowDto ScriptedBossEncounters,
            EncountersWorkflowDto WildEncounters,
            MovesWorkflowDto Moves,
            TrainersWorkflowDto Trainers,
            EncounterCompatibilityWorkflowDto EncounterCompatibility,
            PokemonWorkflowDto Pokemon,
            TrainerPoolsWorkflowDto TrainerPools,
            LegendsZaTypeEffectivenessStateDto TypeEffectivenessState,
            ZaStaticMapMarkerCatalog StaticMapMarkers,
            ZaNamedFlagCatalog NamedFlagCatalog,
            ZaPokemonResourceCatalog PokemonResourceCatalog) sources)
    {
        var data = module switch
        {
            GameModuleDto.LegendsZaScriptedBossTimeline =>
                GameModuleProviders.BuildScriptedBossTimeline(
                    sources.ScriptedBossEncounters,
                    sources.Moves),
            GameModuleDto.LegendsZaTrainerArchetypes =>
                GameModuleProviders.BuildTrainerArchetypes(sources.Trainers),
            GameModuleDto.LegendsZaWildSpawnExplorer =>
                GameModuleProviders.BuildWildSpawnExplorer(sources.WildEncounters),
            GameModuleDto.LegendsZaEncounterCompatibility =>
                GameModuleProviders.BuildEncounterCompatibility(
                    sources.Pokemon,
                    sources.EncounterCompatibility),
            GameModuleDto.LegendsZaAlphaMoveDistribution =>
                GameModuleProviders.BuildAlphaMoveDistribution(sources.Pokemon),
            GameModuleDto.LegendsZaDexLayoutPlanning =>
                GameModuleProviders.BuildDexLayoutPlanning(sources.Pokemon),
            GameModuleDto.LegendsZaMoveVariantComparison =>
                GameModuleProviders.BuildMoveVariantComparison(sources.Moves),
            GameModuleDto.LegendsZaTrainerPoolSwitching =>
                GameModuleProviders.BuildTrainerPoolSwitching(sources.TrainerPools),
            GameModuleDto.LegendsZaTypeEffectivenessState =>
                GameModuleProviders.BuildTypeEffectivenessState(
                    sources.TypeEffectivenessState),
            GameModuleDto.LegendsZaStaticMapMarkers =>
                GameModuleProviders.BuildStaticMapMarkers(sources.StaticMapMarkers),
            GameModuleDto.LegendsZaNamedFlagCatalog =>
                GameModuleProviders.BuildNamedFlagCatalog(sources.NamedFlagCatalog),
            GameModuleDto.LegendsZaPokemonResourceCatalog =>
                GameModuleProviders.BuildPokemonResourceCatalog(
                    sources.PokemonResourceCatalog),
            _ => throw new SemanticExploreValidationException(
                "The requested Z-A game-specific module is unavailable.",
                SemanticExploreFailureKind.Unsupported),
        };
        return new DerivedIndexCacheItem<GameModuleData>(
            data,
            GameModuleProviders.EstimateSizeBytes(data));
    }

    private static void ResolveCapabilityCandidate(
        (GameModuleCapabilityDto Capability, DerivedIndexCacheKey Key) candidate,
        Func<DerivedIndexCacheItem<GameModuleData>> factory,
        IDictionary<GameModuleDto, GameModuleCapabilityDto> resolved,
        ICollection<(DerivedIndexCacheKey Key, DerivedIndexCacheItem<GameModuleData> Item)> pending,
        ref long pendingBytes)
    {
        try
        {
            var built = factory();
            resolved[candidate.Capability.Module] = built.Value.Capability;
            if (built.Value.Cacheable
                && built.SizeBytes <= GameModuleSizingLimits.ModuleCacheCeilingBytes - pendingBytes)
            {
                pending.Add((candidate.Key, built));
                pendingBytes = checked(pendingBytes + built.SizeBytes);
            }
        }
        catch (Exception exception) when (IsReadinessFailure(exception))
        {
            resolved[candidate.Capability.Module] =
                UnavailableCapability(candidate.Capability, exception);
        }
    }

    private (SemanticProjectRevisionDto Revision, SemanticSourceSnapshotDto Snapshot) ReadCompletedSnapshot(
        QueryGameModuleRequest request,
        SemanticSourceSnapshotDto initialSnapshot)
    {
        var completed = semanticExploreService.ReadCapabilities(
            new ReadSemanticCapabilitiesRequest(request.Scope));
        EnsureExpectedRevision(completed.Revision, request.ExpectedRevision);
        var snapshot = completed.Snapshots.SingleOrDefault(candidate => candidate.Layer.Kind == request.Layer);
        if (snapshot is null || !Equals(initialSnapshot, snapshot))
        {
            throw new SemanticExploreValidationException(
                "The game-specific module source changed while analysis was running. Refresh and retry.",
                SemanticExploreFailureKind.StaleRevision);
        }

        return (completed.Revision, snapshot);
    }

    private static DerivedIndexCacheKey CacheKey(
        SemanticProjectRevisionDto revision,
        SemanticSourceLayerKindDto layer,
        GameModuleDto module,
        string sourceCacheIdentity)
    {
        return new DerivedIndexCacheKey(
            ToCoreSourceRevision(revision, layer, sourceCacheIdentity),
            $"{CacheCallerKeyPrefix}:{layer}:{module}");
    }

    private static ProjectSourceRevision ToCoreSourceRevision(
        SemanticProjectRevisionDto revision,
        SemanticSourceLayerKindDto sourceLayer,
        string sourceCacheIdentity)
    {
        var family = revision.GameFamily switch
        {
            SemanticGameFamilyDto.SwordShield => GameFamily.SwordShield,
            SemanticGameFamilyDto.ScarletViolet => GameFamily.ScarletViolet,
            SemanticGameFamilyDto.LegendsZA => GameFamily.LegendsZA,
            _ => throw new ArgumentOutOfRangeException(nameof(revision), revision.GameFamily, null),
        };
        var canonical = string.Join(
            '\n',
            "game-module-source-revision-v1",
            revision.ProjectId,
            revision.GameFamily.ToString(),
            sourceLayer.ToString(),
            sourceCacheIdentity);
        var fingerprint = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        var generation = Convert.ToInt64(fingerprint[..15], 16);
        return new ProjectSourceRevision(
            new ProjectId(revision.ProjectId),
            family,
            generation,
            fingerprint);
    }

    private static bool IsReadinessFailure(Exception exception)
    {
        return exception is SemanticExploreValidationException
            or InvalidDataException
            or IOException
            or UnauthorizedAccessException
            or ArgumentException
            or FormatException
            or OverflowException;
    }

    private static bool IsBoundedFailure(Exception exception)
    {
        Exception? candidate = exception;
        for (var depth = 0; candidate is not null && depth < 8; depth++)
        {
            if (candidate is InvalidDataException
                && candidate.Message.Contains("bounded", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            candidate = candidate.InnerException;
        }

        return false;
    }

    private static GameModuleCapabilityDto UnavailableCapability(
        GameModuleCapabilityDto declared,
        Exception exception)
    {
        var reasonCode = IsBoundedFailure(exception)
            ? "bounded-provider-limit-exceeded"
            : exception switch
            {
                SemanticExploreValidationException
                {
                    FailureKind: SemanticExploreFailureKind.LimitExceeded,
                } => "bounded-provider-limit-exceeded",
                SemanticExploreValidationException
                {
                    FailureKind: SemanticExploreFailureKind.InvalidData,
                } => "workflow-source-invalid",
                InvalidDataException or FormatException or ArgumentException => "workflow-source-invalid",
                OverflowException => "bounded-provider-limit-exceeded",
                IOException or UnauthorizedAccessException => "workflow-source-unavailable",
                _ => "bounded-provider-unavailable",
            };
        return declared with
        {
            State = SemanticCoverageStateDto.Unavailable,
            Confidence = SemanticConfidenceDto.Unknown,
            CanQuery = false,
            ReasonCode = reasonCode,
            SupportedLayers = [],
        };
    }

    private static void EnsureSameObservation(
        ReadSemanticCapabilitiesResponse initial,
        ReadSemanticCapabilitiesResponse completed)
    {
        if (!Equals(initial.Revision, completed.Revision)
            || initial.Snapshots.Count != completed.Snapshots.Count
            || !initial.Snapshots.SequenceEqual(completed.Snapshots))
        {
            throw new SemanticExploreValidationException(
                "The game-specific module source changed while capabilities were loading. Refresh and retry.",
                SemanticExploreFailureKind.StaleRevision);
        }
    }

    private static string QueryFingerprint(
        QueryGameModuleRequest request,
        SemanticSourceSnapshotDto snapshot)
    {
        var canonical = string.Join(
            '\n',
            "game-module-query-v1",
            request.ExpectedRevision.ProjectId,
            request.ExpectedRevision.GameFamily.ToString(),
            request.ExpectedRevision.Generation,
            request.ExpectedRevision.Fingerprint,
            snapshot.Layer.Kind.ToString(),
            snapshot.Layer.InstanceId ?? "<none>",
            snapshot.Revision.Fingerprint,
            snapshot.Fingerprint,
            request.Module.ToString(),
            request.Layer.ToString(),
            request.Limit.ToString(CultureInfo.InvariantCulture),
            request.SearchText ?? string.Empty,
            request.CatalogOnly.ToString(),
            request.RecordId ?? string.Empty);
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
                || !int.TryParse(
                    decoded[(separator + 1)..],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var offset)
                || offset < 0)
            {
                throw new FormatException();
            }

            return offset;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new SemanticExploreValidationException(
                "The game-specific module continuation cursor is invalid or belongs to another query.",
                SemanticExploreFailureKind.InvalidCursor,
                exception);
        }
    }

    private static void ValidateRequest(QueryGameModuleRequest request)
    {
        AnalysisCatalog.Validate(request.SearchText);
        AnalysisCatalog.Validate(null, request.RecordId);
        if (request.Scope?.Paths is null || request.ExpectedRevision is null)
        {
            throw new SemanticExploreValidationException(
                "The game-specific module scope or expected revision is malformed.",
                SemanticExploreFailureKind.InvalidData);
        }

        if (!Enum.IsDefined(request.Module)
            || request.Layer != SemanticSourceLayerKindDto.Layered)
        {
            throw new SemanticExploreValidationException(
                "The requested game-specific module or source layer is unsupported.",
                SemanticExploreFailureKind.Unsupported);
        }

        if (request.Limit is <= 0 or > SemanticExploreContract.MaximumPageSize)
        {
            throw new SemanticExploreValidationException(
                $"A game-specific module page must request between 1 and {SemanticExploreContract.MaximumPageSize.ToString(CultureInfo.InvariantCulture)} records.",
                SemanticExploreFailureKind.LimitExceeded);
        }

        if (request.Cursor is { Length: > SemanticExploreContract.MaximumCursorLength }
            || request.Cursor?.Any(character => char.IsControl(character)) == true)
        {
            throw new SemanticExploreValidationException(
                "The game-specific module continuation cursor is invalid.",
                SemanticExploreFailureKind.InvalidCursor);
        }

        ValidateRevision(request.ExpectedRevision);
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
            || generation < 0
            || revision.Fingerprint is not { Length: 64 } fingerprint
            || !fingerprint.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'))
        {
            throw new SemanticExploreValidationException(
                "The expected game-specific module source revision is invalid.",
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
                "The game-specific module source revision changed. Refresh and retry.",
                SemanticExploreFailureKind.StaleRevision);
        }
    }

}
