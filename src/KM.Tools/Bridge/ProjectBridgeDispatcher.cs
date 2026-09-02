// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Bridge;
using KM.Api.Diagnostics;
using KM.Api.AngeFight;
using KM.Api.BagHook;
using KM.Api.BattleCafeRewards;
using KM.Api.Behavior;
using KM.Api.CatchCap;
using KM.Api.ChangeSets;
using KM.Api.DynamaxAdventures;
using KM.Api.Editing;
using KM.Api.Encounters;
using KM.Api.ExeFs;
using KM.Api.FairyGymBoosts;
using KM.Api.FashionUnlock;
using KM.Api.FashionCatalog;
using KM.Api.Flagwork;
using KM.Api.FpsPatch;
using KM.Api.GameDump;
using KM.Api.GameModules;
using KM.Api.Gifts;
using KM.Api.GymUniformRemoval;
using KM.Api.GuidedDesign;
using KM.Api.HabitatCoordinates;
using KM.Api.HyperspaceBypass;
using KM.Api.HyperTraining;
using KM.Api.Items;
using KM.Api.IvScreen;
using KM.Api.ModMerger;
using KM.Api.Moves;
using KM.Api.NpcItemGift;
using KM.Api.Output;
using KM.Api.Placement;
using KM.Api.Pokemon;
using KM.Api.ProfanityFilter;
using KM.Api.Projects;
using KM.Api.Research;
using KM.Api.Raids;
using KM.Api.Randomizer;
using KM.Api.Rentals;
using KM.Api.RuntimeSettings;
using KM.Api.RoyalCandy;
using KM.Api.Semantics;
using KM.Api.SemanticMerging;
using KM.Api.Shops;
using KM.Api.ShinyRate;
using KM.Api.SpreadsheetImport;
using KM.Api.StartingItems;
using KM.Api.StaticEncounters;
using KM.Api.SvCache;
using KM.Api.SwShCache;
using KM.Api.Text;
using KM.Api.TmMachine;
using KM.Api.Trainers;
using KM.Api.TrainerPools;
using KM.Api.Trades;
using KM.Api.TypeChart;
using KM.Api.Workflows;
using KM.Api.Workspace;
using KM.Api.ZaCache;
using KM.Core.Concurrency;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.GameDump;
using KM.Core.Output;
using KM.Core.Projects;
using KM.Core.Semantics;
using KM.Core.Workspace;
using KM.SwSh.Behavior;
using KM.SwSh.BagHook;
using KM.SwSh.BattleCafeRewards;
using KM.SwSh.CatchCap;
using KM.SwSh.DynamaxAdventures;
using KM.SwSh.Editing;
using KM.SwSh.Encounters;
using KM.SwSh.ExeFs;
using KM.SwSh.FairyGymBoosts;
using KM.SwSh.FashionUnlock;
using KM.SwSh.Gifts;
using KM.SwSh.FpsPatch;
using KM.SwSh.GameDump;
using KM.SwSh.GymUniformRemoval;
using KM.SwSh.HyperTraining;
using KM.SwSh.Items;
using KM.SwSh.IvScreen;
using KM.SwSh.ModMerger;
using KM.SwSh.Moves;
using KM.SwSh.NameFilter;
using KM.SwSh.NpcItemGift;
using KM.SwSh.Placement;
using KM.SwSh.Pokemon;
using KM.SwSh.Raids;
using KM.SwSh.Randomizer;
using KM.SwSh.Rentals;
using KM.SwSh.RoyalCandy;
using KM.SwSh.Shops;
using KM.SwSh.ShinyRate;
using KM.SwSh.SpreadsheetImport;
using KM.SwSh.StartingItems;
using KM.SwSh.StaticEncounters;
using KM.SwSh.Text;
using KM.SwSh.Trainers;
using KM.SwSh.Trades;
using KM.SwSh.TypeChart;
using KM.SwSh.Workflows;
using KM.SV.ModMerger;
using KM.SV.GameDump;
using KM.SV.GameModules;
using KM.SV.Shops;
using KM.SV.Text;
using KM.ZA.Encounters;
using KM.ZA.Gifts;
using KM.ZA.GameDump;
using KM.ZA.GameModules;
using KM.ZA.ModMerger;
using KM.ZA.Placement;
using KM.ZA.Shops;
using KM.ZA.StaticEncounters;
using KM.ZA.Text;
using KM.ZA.Trades;
using KM.SV.Workflows;
using KM.ZA.Workflows;
using KM.Tools.Application;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KM.Tools.Bridge;

public sealed class ProjectBridgeDispatcher : IDisposable
{
    private const int BridgeProvisionMultiplier = 4;
    private const int BridgeHardCeilingMultiplier = 2;
    private const long EstimatedBridgeWorkerBytes = 256L * 1024L * 1024L;
    internal const int ExpectedBridgeRequestCharacters = 16 * 1024 * 1024;
    internal const int ProvisionedBridgeRequestCharacters = checked(
        ExpectedBridgeRequestCharacters * BridgeProvisionMultiplier);
    internal const int MaximumBridgeRequestCharacters = checked(
        ProvisionedBridgeRequestCharacters * BridgeHardCeilingMultiplier);
    internal const int ExpectedBridgeRequestBytes = 16 * 1024 * 1024;
    internal const int ProvisionedBridgeRequestBytes = checked(
        ExpectedBridgeRequestBytes * BridgeProvisionMultiplier);
    internal const int MaximumBridgeRequestBytes = checked(
        ProvisionedBridgeRequestBytes * BridgeHardCeilingMultiplier);
    internal const int ExpectedBridgeResponseBytes = 30 * 1024 * 1024;
    internal const int ProvisionedBridgeResponseBytes = checked(
        ExpectedBridgeResponseBytes * BridgeProvisionMultiplier);
    internal const int MaximumBridgeResponseBytes = checked(
        ProvisionedBridgeResponseBytes * BridgeHardCeilingMultiplier);
    private static readonly object SwShApplySyncRoot = new();
    private static readonly JsonSerializerOptions RequestSerializerOptions =
        new(BridgeJson.SerializerOptions)
        {
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
        };

    internal Action<int, string>? NormalSwShApplyMutationHook { get; init; }

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShDynamaxAdventuresEditSessionService dynamaxAdventuresEditSessionService;
    private readonly SwShDynamaxAdventureSeedPlanningService dynamaxAdventureSeedPlanningService;
    private readonly SwShDynamaxAdventureSaveSeedService dynamaxAdventureSaveSeedService;
    private readonly SwShEncountersEditSessionService encountersEditSessionService;
    private readonly SwShExeFsPatchEditSessionService exeFsPatchEditSessionService;
    private readonly SwShBagHookEditSessionService bagHookEditSessionService;
    private readonly SwShCatchCapEditSessionService catchCapEditSessionService;
    private readonly SwShHyperTrainingEditSessionService hyperTrainingEditSessionService;
    private readonly SwShShinyRateEditSessionService shinyRateEditSessionService;
    private readonly SwShFashionUnlockEditSessionService fashionUnlockEditSessionService;
    private readonly SwShFairyGymBoostsEditSessionService fairyGymBoostsEditSessionService;
    private readonly SwShGymUniformRemovalEditSessionService gymUniformRemovalEditSessionService;
    private readonly SwShIvScreenEditSessionService ivScreenEditSessionService;
    private readonly SwShTypeChartEditSessionService typeChartEditSessionService;
    private readonly SwShGiftPokemonEditSessionService giftPokemonEditSessionService;
    private readonly SwShItemsEditSessionService itemsEditSessionService;
    private readonly SwShMovesEditSessionService movesEditSessionService;
    private readonly SwShPlacementEditSessionService placementEditSessionService;
    private readonly SwShBehaviorEditSessionService behaviorEditSessionService;
    private readonly SwShPokemonEditSessionService pokemonEditSessionService;
    private readonly SwShRaidBattlesEditSessionService raidBattlesEditSessionService;
    private readonly SwShRaidRewardsEditSessionService raidRewardsEditSessionService;
    private readonly SwShRentalPokemonEditSessionService rentalPokemonEditSessionService;
    private readonly SwShRoyalCandyEditSessionService royalCandyEditSessionService;
    private readonly SwShStartingItemsEditSessionService startingItemsEditSessionService;
    private readonly SwShNpcItemGiftEditSessionService npcItemGiftEditSessionService;
    private readonly SwShBattleCafeRewardsEditSessionService battleCafeRewardsEditSessionService;
    private readonly SwShShopsEditSessionService shopsEditSessionService;
    private readonly SwShSpreadsheetImportExecutionService spreadsheetImportExecutionService;
    private readonly SwShModMergerWorkflowService modMergerWorkflowService;
    private readonly SwShFpsPatchService fpsPatchService;
    private readonly SwShProfanityFilterService profanityFilterService;
    private readonly SwShRandomizerService randomizerService;
    private readonly SwShGameDumpService swShGameDumpService;
    private readonly SvGameDumpService svGameDumpService;
    private readonly ZaGameDumpService zaGameDumpService;
    private readonly SwShStaticEncountersEditSessionService staticEncountersEditSessionService;
    private readonly SwShTextEditSessionService textEditSessionService;
    private readonly SwShTrainersEditSessionService trainersEditSessionService;
    private readonly SwShTradePokemonEditSessionService tradePokemonEditSessionService;
    private readonly SwShWorkflowService swShWorkflowService;
    private readonly SvWorkflowService svWorkflowService;
    private readonly ZaWorkflowService zaWorkflowService;
    private readonly WorkspaceDraftApplicationService workspaceDraftApplicationService;
    private readonly WorkspacePersonalStateApplicationService workspacePersonalStateApplicationService;
    private readonly ChangeSetApplicationService changeSetApplicationService;
    private readonly OutputSafetyApplicationService outputSafetyApplicationService;
    private readonly ProjectRelocationApplicationService projectRelocationApplicationService;
    private readonly SemanticExploreApplicationService semanticExploreApplicationService;
    private readonly BalanceLabApplicationService balanceLabApplicationService;
    private readonly GameModuleApplicationService gameModuleApplicationService;
    private readonly GuidedDesignApplicationService guidedDesignApplicationService;
    private readonly SemanticMergeApplicationService semanticMergeApplicationService;
    private readonly ResearchAnnotationApplicationService researchAnnotationApplicationService;
    private readonly ResearchLabApplicationService researchLabApplicationService;
    private readonly RowClipboardApplicationService rowClipboardApplicationService;
    private readonly GameplaySettingsApplicationService gameplaySettingsApplicationService;
    private readonly InGameSettingsPackageApplicationService inGameSettingsPackageApplicationService;
    private readonly bool ownsSemanticMergeApplicationService;
    private readonly bool ownsResearchLabApplicationService;
    private readonly bool ownsInGameSettingsPackageApplicationService;

    public ProjectBridgeDispatcher(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShDynamaxAdventuresEditSessionService? dynamaxAdventuresEditSessionService = null,
        SwShDynamaxAdventureSeedPlanningService? dynamaxAdventureSeedPlanningService = null,
        SwShDynamaxAdventureSaveSeedService? dynamaxAdventureSaveSeedService = null,
        SwShEncountersEditSessionService? encountersEditSessionService = null,
        SwShExeFsPatchEditSessionService? exeFsPatchEditSessionService = null,
        SwShBagHookEditSessionService? bagHookEditSessionService = null,
        SwShCatchCapEditSessionService? catchCapEditSessionService = null,
        SwShHyperTrainingEditSessionService? hyperTrainingEditSessionService = null,
        SwShShinyRateEditSessionService? shinyRateEditSessionService = null,
        SwShFashionUnlockEditSessionService? fashionUnlockEditSessionService = null,
        SwShFairyGymBoostsEditSessionService? fairyGymBoostsEditSessionService = null,
        SwShGymUniformRemovalEditSessionService? gymUniformRemovalEditSessionService = null,
        SwShIvScreenEditSessionService? ivScreenEditSessionService = null,
        SwShTypeChartEditSessionService? typeChartEditSessionService = null,
        SwShGiftPokemonEditSessionService? giftPokemonEditSessionService = null,
        SwShItemsEditSessionService? itemsEditSessionService = null,
        SwShMovesEditSessionService? movesEditSessionService = null,
        SwShPlacementEditSessionService? placementEditSessionService = null,
        SwShBehaviorEditSessionService? behaviorEditSessionService = null,
        SwShPokemonEditSessionService? pokemonEditSessionService = null,
        SwShRaidBattlesEditSessionService? raidBattlesEditSessionService = null,
        SwShRaidRewardsEditSessionService? raidRewardsEditSessionService = null,
        SwShRentalPokemonEditSessionService? rentalPokemonEditSessionService = null,
        SwShRoyalCandyEditSessionService? royalCandyEditSessionService = null,
        SwShStartingItemsEditSessionService? startingItemsEditSessionService = null,
        SwShNpcItemGiftEditSessionService? npcItemGiftEditSessionService = null,
        SwShBattleCafeRewardsEditSessionService? battleCafeRewardsEditSessionService = null,
        SwShShopsEditSessionService? shopsEditSessionService = null,
        SwShSpreadsheetImportExecutionService? spreadsheetImportExecutionService = null,
        SwShModMergerWorkflowService? modMergerWorkflowService = null,
        SwShFpsPatchService? fpsPatchService = null,
        SwShProfanityFilterService? profanityFilterService = null,
        SwShRandomizerService? randomizerService = null,
        SwShGameDumpService? swShGameDumpService = null,
        SvGameDumpService? svGameDumpService = null,
        ZaGameDumpService? zaGameDumpService = null,
        SwShStaticEncountersEditSessionService? staticEncountersEditSessionService = null,
        SwShTextEditSessionService? textEditSessionService = null,
        SwShTrainersEditSessionService? trainersEditSessionService = null,
        SwShTradePokemonEditSessionService? tradePokemonEditSessionService = null,
        SwShWorkflowService? swShWorkflowService = null,
        SvWorkflowService? svWorkflowService = null,
        ZaWorkflowService? zaWorkflowService = null,
        SwShCacheManager? swShCacheManager = null,
        WorkspaceDraftApplicationService? workspaceDraftApplicationService = null,
        WorkspacePersonalStateApplicationService? workspacePersonalStateApplicationService = null,
        ChangeSetApplicationService? changeSetApplicationService = null,
        OutputSafetyApplicationService? outputSafetyApplicationService = null,
        ProjectRelocationApplicationService? projectRelocationApplicationService = null,
        SemanticExploreApplicationService? semanticExploreApplicationService = null,
        BalanceLabApplicationService? balanceLabApplicationService = null,
        GameModuleApplicationService? gameModuleApplicationService = null,
        GuidedDesignApplicationService? guidedDesignApplicationService = null,
        SemanticMergeApplicationService? semanticMergeApplicationService = null,
        ResearchAnnotationApplicationService? researchAnnotationApplicationService = null,
        ResearchLabApplicationService? researchLabApplicationService = null,
        RowClipboardApplicationService? rowClipboardApplicationService = null,
        GameplaySettingsApplicationService? gameplaySettingsApplicationService = null,
        InGameSettingsPackageApplicationService? inGameSettingsPackageApplicationService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.dynamaxAdventuresEditSessionService = dynamaxAdventuresEditSessionService ?? new SwShDynamaxAdventuresEditSessionService(this.projectWorkspaceService);
        this.dynamaxAdventureSeedPlanningService = dynamaxAdventureSeedPlanningService ?? new SwShDynamaxAdventureSeedPlanningService(this.projectWorkspaceService);
        this.dynamaxAdventureSaveSeedService = dynamaxAdventureSaveSeedService ?? new SwShDynamaxAdventureSaveSeedService();
        this.encountersEditSessionService = encountersEditSessionService ?? new SwShEncountersEditSessionService(this.projectWorkspaceService);
        this.exeFsPatchEditSessionService = exeFsPatchEditSessionService ?? new SwShExeFsPatchEditSessionService(this.projectWorkspaceService);
        this.bagHookEditSessionService = bagHookEditSessionService ?? new SwShBagHookEditSessionService(this.projectWorkspaceService);
        this.catchCapEditSessionService = catchCapEditSessionService ?? new SwShCatchCapEditSessionService(this.projectWorkspaceService);
        this.hyperTrainingEditSessionService = hyperTrainingEditSessionService ?? new SwShHyperTrainingEditSessionService(this.projectWorkspaceService);
        this.shinyRateEditSessionService = shinyRateEditSessionService ?? new SwShShinyRateEditSessionService(this.projectWorkspaceService);
        this.fashionUnlockEditSessionService = fashionUnlockEditSessionService ?? new SwShFashionUnlockEditSessionService(this.projectWorkspaceService);
        this.fairyGymBoostsEditSessionService = fairyGymBoostsEditSessionService ?? new SwShFairyGymBoostsEditSessionService(this.projectWorkspaceService);
        this.gymUniformRemovalEditSessionService = gymUniformRemovalEditSessionService ?? new SwShGymUniformRemovalEditSessionService(this.projectWorkspaceService);
        this.ivScreenEditSessionService = ivScreenEditSessionService ?? new SwShIvScreenEditSessionService(this.projectWorkspaceService);
        this.typeChartEditSessionService = typeChartEditSessionService ?? new SwShTypeChartEditSessionService(this.projectWorkspaceService);
        this.giftPokemonEditSessionService = giftPokemonEditSessionService ?? new SwShGiftPokemonEditSessionService(this.projectWorkspaceService);
        this.itemsEditSessionService = itemsEditSessionService ?? new SwShItemsEditSessionService(this.projectWorkspaceService);
        this.movesEditSessionService = movesEditSessionService ?? new SwShMovesEditSessionService(this.projectWorkspaceService);
        this.behaviorEditSessionService = behaviorEditSessionService ?? new SwShBehaviorEditSessionService(this.projectWorkspaceService);
        this.raidBattlesEditSessionService = raidBattlesEditSessionService ?? new SwShRaidBattlesEditSessionService(this.projectWorkspaceService);
        this.raidRewardsEditSessionService = raidRewardsEditSessionService ?? new SwShRaidRewardsEditSessionService(this.projectWorkspaceService);
        this.rentalPokemonEditSessionService = rentalPokemonEditSessionService ?? new SwShRentalPokemonEditSessionService(this.projectWorkspaceService);
        this.royalCandyEditSessionService = royalCandyEditSessionService ?? new SwShRoyalCandyEditSessionService(this.projectWorkspaceService);
        this.startingItemsEditSessionService = startingItemsEditSessionService ?? new SwShStartingItemsEditSessionService(this.projectWorkspaceService);
        this.npcItemGiftEditSessionService = npcItemGiftEditSessionService ?? new SwShNpcItemGiftEditSessionService(this.projectWorkspaceService);
        this.battleCafeRewardsEditSessionService = battleCafeRewardsEditSessionService ?? new SwShBattleCafeRewardsEditSessionService(this.projectWorkspaceService);
        this.shopsEditSessionService = shopsEditSessionService ?? new SwShShopsEditSessionService(this.projectWorkspaceService);
        this.spreadsheetImportExecutionService = spreadsheetImportExecutionService ?? new SwShSpreadsheetImportExecutionService(this.projectWorkspaceService);
        this.modMergerWorkflowService = modMergerWorkflowService ?? new SwShModMergerWorkflowService(this.projectWorkspaceService);
        this.fpsPatchService = fpsPatchService ?? new SwShFpsPatchService(this.projectWorkspaceService);
        this.profanityFilterService = profanityFilterService ?? new SwShProfanityFilterService(this.projectWorkspaceService);
        this.randomizerService = randomizerService ?? new SwShRandomizerService(this.projectWorkspaceService);
        this.staticEncountersEditSessionService = staticEncountersEditSessionService ?? new SwShStaticEncountersEditSessionService(this.projectWorkspaceService);
        this.trainersEditSessionService = trainersEditSessionService ?? new SwShTrainersEditSessionService(this.projectWorkspaceService);
        this.tradePokemonEditSessionService = tradePokemonEditSessionService ?? new SwShTradePokemonEditSessionService(this.projectWorkspaceService);
        var resolvedSwShCacheManager = swShWorkflowService?.SharedCacheManager
            ?? swShCacheManager
            ?? new SwShCacheManager();
        this.swShWorkflowService = swShWorkflowService ?? new SwShWorkflowService(
            this.projectWorkspaceService,
            modMergerWorkflowService: this.modMergerWorkflowService,
            cacheManager: resolvedSwShCacheManager);
        this.textEditSessionService = textEditSessionService ?? new SwShTextEditSessionService(
            this.projectWorkspaceService,
            this.swShWorkflowService.SharedTextWorkflowService);
        this.placementEditSessionService = placementEditSessionService ?? new SwShPlacementEditSessionService(
            this.projectWorkspaceService,
            this.swShWorkflowService.SharedPlacementWorkflowService);
        this.pokemonEditSessionService = pokemonEditSessionService ?? new SwShPokemonEditSessionService(
            this.projectWorkspaceService,
            this.swShWorkflowService.SharedPokemonWorkflowService);
        this.svWorkflowService = svWorkflowService ?? new SvWorkflowService(this.projectWorkspaceService);
        this.zaWorkflowService = zaWorkflowService ?? new ZaWorkflowService(this.projectWorkspaceService);
        var rowClipboardMutations = new RowClipboardWorkflowMutationProvider(
            this.swShWorkflowService,
            this.svWorkflowService,
            this.zaWorkflowService);
        this.rowClipboardApplicationService = rowClipboardApplicationService
            ?? new RowClipboardApplicationService(
                rowClipboardMutations.CaptureSourceFingerprint,
                rowClipboardMutations.Mutate);
        this.gameplaySettingsApplicationService = gameplaySettingsApplicationService
            ?? new GameplaySettingsApplicationService();
        ownsInGameSettingsPackageApplicationService =
            inGameSettingsPackageApplicationService is null;
        if (inGameSettingsPackageApplicationService is not null)
        {
            this.inGameSettingsPackageApplicationService = inGameSettingsPackageApplicationService;
        }
        else
        {
            this.inGameSettingsPackageApplicationService = new InGameSettingsPackageApplicationService(
                new NativeGameplayMenuBundleProvider());
        }
        this.swShGameDumpService = swShGameDumpService ?? new SwShGameDumpService(this.swShWorkflowService);
        this.svGameDumpService = svGameDumpService ?? new SvGameDumpService(this.svWorkflowService);
        this.zaGameDumpService = zaGameDumpService ?? new ZaGameDumpService(this.zaWorkflowService);
        this.workspaceDraftApplicationService = workspaceDraftApplicationService
            ?? new WorkspaceDraftApplicationService();
        this.workspacePersonalStateApplicationService = workspacePersonalStateApplicationService
            ?? new WorkspacePersonalStateApplicationService();
        this.changeSetApplicationService = changeSetApplicationService
            ?? new ChangeSetApplicationService(
                workspacePersonalStateService: this.workspacePersonalStateApplicationService);
        this.researchAnnotationApplicationService = researchAnnotationApplicationService
            ?? new ResearchAnnotationApplicationService();
        this.outputSafetyApplicationService = outputSafetyApplicationService
            ?? new OutputSafetyApplicationService();
        this.projectRelocationApplicationService = projectRelocationApplicationService
            ?? new ProjectRelocationApplicationService(
                workspaceDraftService: this.workspaceDraftApplicationService,
                workspacePersonalStateService: this.workspacePersonalStateApplicationService,
                changeSetService: this.changeSetApplicationService,
                researchAnnotationService: this.researchAnnotationApplicationService,
                outputSafetyService: this.outputSafetyApplicationService);
        this.semanticExploreApplicationService = semanticExploreApplicationService
            ?? new SemanticExploreApplicationService(
                LoadSemanticExploreItemsFresh,
                LoadSemanticExplorePokemonFresh,
                LoadSemanticExploreMovesFresh,
                CaptureSemanticExploreSourceFingerprint,
                CanLoadSemanticExploreCorporaConcurrently,
                PrepareSemanticExploreCorporaFresh);
        this.balanceLabApplicationService = balanceLabApplicationService
            ?? new BalanceLabApplicationService(
                this.semanticExploreApplicationService,
                LoadBalanceLabTrainersFresh,
                LoadBalanceLabEncountersFresh,
                LoadSemanticExploreMovesFresh,
                LoadSemanticExploreItemsFresh,
                LoadSemanticExplorePokemonFresh);
        this.gameModuleApplicationService = gameModuleApplicationService
            ?? new GameModuleApplicationService(
                this.semanticExploreApplicationService,
                LoadGameModuleTeraRaidsFresh,
                LoadGameModulePackedLooseSourceComparisonFresh,
                LoadGameModuleEventDataComparisonFresh,
                LoadGameModuleScenePlacementProjectionFresh,
                LoadGameModuleScarletVioletTypeEffectivenessStateFresh,
                LoadGameModuleScriptedBossTimelineFresh,
                LoadGameModuleSwordShieldCapabilityBatchFresh,
                LoadGameModuleZaCapabilityBatchFresh,
                LoadGameModuleTrainerArchetypesFresh,
                LoadGameModuleWildSpawnsFresh,
                LoadGameModuleMoveVariantsFresh,
                LoadGameModuleEncounterCompatibilityFresh,
                LoadGameModuleAlphaMovesFresh,
                LoadGameModuleTrainerPoolsFresh,
                LoadGameModuleTypeEffectivenessStateFresh,
                LoadGameModuleStaticMapMarkersFresh,
                LoadGameModuleNamedFlagCatalogFresh,
                LoadGameModulePokemonResourceCatalogFresh);
        this.guidedDesignApplicationService = guidedDesignApplicationService
            ?? new GuidedDesignApplicationService(
                this.semanticExploreApplicationService,
                this.changeSetApplicationService,
                LoadBalanceLabTrainersFresh,
                LoadBalanceLabEncountersFresh,
                LoadSemanticExploreItemsFresh,
                LoadSemanticExplorePokemonFresh,
                StageGuidedDesignEdits,
                (paths, session, outputMode) => CreateChangePlanForSession(
                    ProjectBridgeMapper.ToCore(paths),
                    session,
                    outputMode),
                CanLoadSemanticExploreCorporaConcurrently,
                PrepareGuidedDesignSourcesFresh);
        ownsSemanticMergeApplicationService = semanticMergeApplicationService is null;
        this.semanticMergeApplicationService = semanticMergeApplicationService
            ?? new SemanticMergeApplicationService(
                this.semanticExploreApplicationService,
                this.changeSetApplicationService,
                LoadSemanticExploreItemsFresh,
                LoadSemanticExplorePokemonFresh,
                LoadSemanticExploreMovesFresh,
                StageGuidedDesignEdits,
                (paths, session, outputMode) => CreateChangePlanForSession(
                    ProjectBridgeMapper.ToCore(paths),
                    session,
                    outputMode),
                (paths, session, outputMode) => CreateGuidedChangePlanForSession(
                    ProjectBridgeMapper.ToCore(paths),
                    session,
                    outputMode));
        ownsResearchLabApplicationService = researchLabApplicationService is null;
        this.researchLabApplicationService = researchLabApplicationService
            ?? new ResearchLabApplicationService(
                this.semanticExploreApplicationService,
                this.researchAnnotationApplicationService);
    }

    public void Dispose()
    {
        if (ownsSemanticMergeApplicationService)
        {
            semanticMergeApplicationService.Dispose();
        }

        if (ownsResearchLabApplicationService)
        {
            researchLabApplicationService.Dispose();
        }

        if (ownsInGameSettingsPackageApplicationService)
        {
            inGameSettingsPackageApplicationService.Dispose();
        }
    }

    public string Dispatch(string requestJson)
    {
        return DispatchForLongLivedRunner(requestJson).ResponseJson;
    }

    internal static string SerializeRequestTooLargeFailure()
    {
        return SerializeFailure(
            BridgeErrorCodes.RequestTooLarge,
            "Bridge request JSON exceeds the supported size limit.",
            requestId: null);
    }

    internal static string SerializeResponseTooLargeFailure(string? requestId)
    {
        return SerializeFailure(
            BridgeErrorCodes.ResponseTooLarge,
            "Bridge response JSON exceeds the supported size limit.",
            requestId);
    }

    internal (string ResponseJson, bool RequiresDispatcherReset) DispatchForLongLivedRunner(
        string requestJson)
    {
        if (string.IsNullOrWhiteSpace(requestJson))
        {
            return (
                SerializeFailure(
                    BridgeErrorCodes.EmptyRequest,
                    "Bridge request JSON cannot be empty.",
                    requestId: null),
                RequiresDispatcherReset: false);
        }

        if (requestJson.Length > MaximumBridgeRequestCharacters
            || Encoding.UTF8.GetByteCount(requestJson) > MaximumBridgeRequestBytes)
        {
            return (
                SerializeRequestTooLargeFailure(),
                RequiresDispatcherReset: false);
        }

        string? requestId = null;
        string? command = null;
        ProjectGame? selectedGame = null;
        try
        {
            // Read the minimal envelope first so the payload can be deserialized into the command-specific DTO.
            var envelope = DeserializeEnvelope(requestJson);
            requestId = envelope?.RequestId;
            command = envelope?.Command;
            if (command is not null
                && TryReadSelectedGame(requestJson, out var selectedGameDto))
            {
                selectedGame = ToCore(selectedGameDto);
            }

            var gameScopeFailure = ValidateCommandGameScope(envelope, selectedGame);
            if (gameScopeFailure is not null)
            {
                return (gameScopeFailure, RequiresDispatcherReset: false);
            }

            if (IsWorkflowCacheBoundary(command))
            {
                // Project and editor snapshots depend on every configured path, but reusable base indexes
                // fingerprint their own source files. Preserve those expensive indexes across ordinary
                // open, validation, and output-root refreshes; explicit cache controls still drop them.
                ClearWorkflowMemoryCaches(
                    clearReusableDataCaches: command is
                        KmCommandNames.UpdateSvCacheSettings or
                        KmCommandNames.ClearSvCache or
                        KmCommandNames.UpdateZaCacheSettings or
                        KmCommandNames.ClearZaCache or
                        KmCommandNames.UpdateSwShCacheSettings or
                        KmCommandNames.ClearSwShCache);
            }

            if (IsWorkflowCacheMutation(command))
            {
                // Invalidate verified semantic observations before any output mutation. A failed
                // mutation may still have changed files, so retaining a pre-mutation token is unsafe.
                semanticExploreApplicationService.ClearMemoryCaches();
            }

            var response = command switch
            {
                KmCommandNames.OpenProject => DispatchOpenProject(requestJson),
                KmCommandNames.ValidateProject => DispatchValidateProject(requestJson),
                KmCommandNames.RefreshFileGraph => DispatchRefreshFileGraph(requestJson),
                KmCommandNames.ListWorkflows => DispatchListWorkflows(requestJson),
                KmCommandNames.LoadItemsWorkflow => DispatchLoadItemsWorkflow(requestJson),
                KmCommandNames.UpdateItemField => DispatchUpdateItemField(requestJson),
                KmCommandNames.UpdateItemFields => DispatchUpdateItemFields(requestJson),
                KmCommandNames.StageItemVanilla => DispatchStageItemVanilla(requestJson),
                KmCommandNames.LoadPokemonWorkflow => DispatchLoadPokemonWorkflow(requestJson),
                KmCommandNames.UpdatePokemonField => DispatchUpdatePokemonField(requestJson),
                KmCommandNames.UpdatePokemonFields => DispatchUpdatePokemonFields(requestJson),
                KmCommandNames.UpdatePokemonComposite => DispatchUpdatePokemonComposite(requestJson),
                KmCommandNames.UpdatePokemonLearnset => DispatchUpdatePokemonLearnset(requestJson),
                KmCommandNames.UpdatePokemonEvolution => DispatchUpdatePokemonEvolution(requestJson),
                KmCommandNames.SwapPokemonDexPlacement => DispatchSwapPokemonDexPlacement(requestJson),
                KmCommandNames.MovePokemonDexPlacement => DispatchMovePokemonDexPlacement(requestJson),
                KmCommandNames.ResizePokemonDex => DispatchResizePokemonDex(requestJson),
                KmCommandNames.StagePokemonDexVanilla => DispatchStagePokemonDexVanilla(requestJson),
                KmCommandNames.StagePokemonDexMegaSync => DispatchStagePokemonDexMegaSync(requestJson),
                KmCommandNames.LoadMovesWorkflow => DispatchLoadMovesWorkflow(requestJson),
                KmCommandNames.UpdateMoveField => DispatchUpdateMoveField(requestJson),
                KmCommandNames.UpdateMoveFields => DispatchUpdateMoveFields(requestJson),
                KmCommandNames.StageMoveVanilla => DispatchStageMoveVanilla(requestJson),
                KmCommandNames.LoadTextWorkflow => DispatchLoadTextWorkflow(requestJson),
                KmCommandNames.UpdateTextEntry => DispatchUpdateTextEntry(requestJson),
                KmCommandNames.LoadTrainersWorkflow => DispatchLoadTrainersWorkflow(requestJson),
                KmCommandNames.UpdateTrainerField => DispatchUpdateTrainerField(requestJson),
                KmCommandNames.UpdateTrainerFields => DispatchUpdateTrainerFields(requestJson),
                KmCommandNames.LoadTrainerPoolsWorkflow => DispatchLoadTrainerPoolsWorkflow(requestJson),
                KmCommandNames.StageTrainerPoolFixedCountSwap => DispatchStageTrainerPoolFixedCountSwap(requestJson),
                KmCommandNames.LoadFashionCatalogWorkflow => DispatchLoadFashionCatalogWorkflow(requestJson),
                KmCommandNames.StageFashionCatalogFieldEdit => DispatchStageFashionCatalogFieldEdit(requestJson),
                KmCommandNames.LoadGiftPokemonWorkflow => DispatchLoadGiftPokemonWorkflow(requestJson),
                KmCommandNames.UpdateGiftPokemonField => DispatchUpdateGiftPokemonField(requestJson),
                KmCommandNames.UpdateGiftPokemonFields => DispatchUpdateGiftPokemonFields(requestJson),
                KmCommandNames.StageGiftPokemonVanilla => DispatchStageGiftPokemonVanilla(requestJson),
                KmCommandNames.LoadTradePokemonWorkflow => DispatchLoadTradePokemonWorkflow(requestJson),
                KmCommandNames.UpdateTradePokemonField => DispatchUpdateTradePokemonField(requestJson),
                KmCommandNames.UpdateTradePokemonFields => DispatchUpdateTradePokemonFields(requestJson),
                KmCommandNames.LoadStaticEncountersWorkflow => DispatchLoadStaticEncountersWorkflow(requestJson),
                KmCommandNames.UpdateStaticEncounterField => DispatchUpdateStaticEncounterField(requestJson),
                KmCommandNames.UpdateStaticEncounterFields => DispatchUpdateStaticEncounterFields(requestJson),
                KmCommandNames.LoadRentalPokemonWorkflow => DispatchLoadRentalPokemonWorkflow(requestJson),
                KmCommandNames.UpdateRentalPokemonField => DispatchUpdateRentalPokemonField(requestJson),
                KmCommandNames.UpdateRentalPokemonFields => DispatchUpdateRentalPokemonFields(requestJson),
                KmCommandNames.LoadDynamaxAdventuresWorkflow => DispatchLoadDynamaxAdventuresWorkflow(requestJson),
                KmCommandNames.UpdateDynamaxAdventureField => DispatchUpdateDynamaxAdventureField(requestJson),
                KmCommandNames.UpdateDynamaxAdventureFields => DispatchUpdateDynamaxAdventureFields(requestJson),
                KmCommandNames.StageDynamaxAdventureRepair => DispatchStageDynamaxAdventureRepair(requestJson),
                KmCommandNames.StageDynamaxAdventureRestore => DispatchStageDynamaxAdventureRestore(requestJson),
                KmCommandNames.PreviewDynamaxAdventureDefaults => DispatchPreviewDynamaxAdventureDefaults(requestJson),
                KmCommandNames.PlanDynamaxAdventureSeed => DispatchPlanDynamaxAdventureSeed(requestJson),
                KmCommandNames.SearchDynamaxAdventureSeed => DispatchSearchDynamaxAdventureSeed(requestJson),
                KmCommandNames.SetDynamaxAdventureSaveSeed => DispatchSetDynamaxAdventureSaveSeed(requestJson),
                KmCommandNames.LoadShopsWorkflow => DispatchLoadShopsWorkflow(requestJson),
                KmCommandNames.UpdateShopInventoryItem => DispatchUpdateShopInventoryItem(requestJson),
                KmCommandNames.UpdateShopInventoryItems => DispatchUpdateShopInventoryItems(requestJson),
                KmCommandNames.LoadTmMachineControls => DispatchLoadTmMachineControls(requestJson),
                KmCommandNames.StageTmRecipeAvailability => DispatchStageTmRecipeAvailability(requestJson),
                KmCommandNames.StageTmMaterialVisibility => DispatchStageTmMaterialVisibility(requestJson),
                KmCommandNames.LoadHabitatCoordinates => DispatchLoadHabitatCoordinates(requestJson),
                KmCommandNames.StageHabitatCoordinate => DispatchStageHabitatCoordinate(requestJson),
                KmCommandNames.LoadEncountersWorkflow => DispatchLoadEncountersWorkflow(requestJson),
                KmCommandNames.UpdateEncounterSlotField => DispatchUpdateEncounterSlotField(requestJson),
                KmCommandNames.UpdateEncounterSlotFields => DispatchUpdateEncounterSlotFields(requestJson),
                KmCommandNames.StageEncounterSlotVanilla => DispatchStageEncounterSlotVanilla(requestJson),
                KmCommandNames.PrepareRowClipboardCopy => DispatchPrepareRowClipboardCopy(requestJson),
                KmCommandNames.PreviewRowClipboardPaste => DispatchPreviewRowClipboardPaste(requestJson),
                KmCommandNames.StageRowClipboardPaste => DispatchStageRowClipboardPaste(requestJson),
                KmCommandNames.ClearRowClipboardAuthorizations => DispatchClearRowClipboardAuthorizations(requestJson),
                KmCommandNames.LoadRaidBattlesWorkflow => DispatchLoadRaidBattlesWorkflow(requestJson),
                KmCommandNames.UpdateRaidBattleSlotField => DispatchUpdateRaidBattleSlotField(requestJson),
                KmCommandNames.UpdateRaidBattleSlotFields => DispatchUpdateRaidBattleSlotFields(requestJson),
                KmCommandNames.LoadTeraRaidsWorkflow => DispatchLoadTeraRaidsWorkflow(requestJson),
                KmCommandNames.UpdateTeraRaidField => DispatchUpdateTeraRaidField(requestJson),
                KmCommandNames.UpdateTeraRaidFields => DispatchUpdateTeraRaidFields(requestJson),
                KmCommandNames.LoadRaidRewardsWorkflow => DispatchLoadRaidRewardsWorkflow(requestJson),
                KmCommandNames.UpdateRaidRewardField => DispatchUpdateRaidRewardField(requestJson),
                KmCommandNames.UpdateRaidRewardFields => DispatchUpdateRaidRewardFields(requestJson),
                KmCommandNames.LoadRaidBonusRewardsWorkflow => DispatchLoadRaidBonusRewardsWorkflow(requestJson),
                KmCommandNames.UpdateRaidBonusRewardField => DispatchUpdateRaidBonusRewardField(requestJson),
                KmCommandNames.UpdateRaidBonusRewardFields => DispatchUpdateRaidBonusRewardFields(requestJson),
                KmCommandNames.LoadPlacementWorkflow => DispatchLoadPlacementWorkflow(requestJson),
                KmCommandNames.OpenSwShPlacementCatalog => DispatchOpenSwShPlacementCatalog(requestJson),
                KmCommandNames.QuerySwShPlacementCatalog => DispatchQuerySwShPlacementCatalog(requestJson),
                KmCommandNames.LoadSwShPlacementObject => DispatchLoadSwShPlacementObject(requestJson),
                KmCommandNames.UpdatePlacementObjectField => DispatchUpdatePlacementObjectField(requestJson),
                KmCommandNames.UpdatePlacementObjectFields => DispatchUpdatePlacementObjectFields(requestJson),
                KmCommandNames.LoadBehaviorWorkflow => DispatchLoadBehaviorWorkflow(requestJson),
                KmCommandNames.UpdateBehaviorEntryField => DispatchUpdateBehaviorEntryField(requestJson),
                KmCommandNames.UpdateBehaviorEntryFields => DispatchUpdateBehaviorEntryFields(requestJson),
                KmCommandNames.LoadFlagworkSaveWorkflow => DispatchLoadFlagworkSaveWorkflow(requestJson),
                KmCommandNames.LoadBagHookWorkflow => DispatchLoadBagHookWorkflow(requestJson),
                KmCommandNames.StageBagHookInstall => DispatchStageBagHookInstall(requestJson),
                KmCommandNames.StageBagHookUninstall => DispatchStageBagHookUninstall(requestJson),
                KmCommandNames.LoadCatchCapWorkflow => DispatchLoadCatchCapWorkflow(requestJson),
                KmCommandNames.StageCatchCap => DispatchStageCatchCap(requestJson),
                KmCommandNames.StageCatchCapUninstall => DispatchStageCatchCapUninstall(requestJson),
                KmCommandNames.LoadHyperTrainingWorkflow => DispatchLoadHyperTrainingWorkflow(requestJson),
                KmCommandNames.StageHyperTraining => DispatchStageHyperTraining(requestJson),
                KmCommandNames.LoadShinyRateWorkflow => DispatchLoadShinyRateWorkflow(requestJson),
                KmCommandNames.StageShinyRate => DispatchStageShinyRate(requestJson),
                KmCommandNames.LoadTypeChartWorkflow => DispatchLoadTypeChartWorkflow(requestJson),
                KmCommandNames.StageTypeChart => DispatchStageTypeChart(requestJson),
                KmCommandNames.StageTypeChartUninstall => DispatchStageTypeChartUninstall(requestJson),
                KmCommandNames.LoadAngeFightWorkflow => DispatchLoadAngeFightWorkflow(requestJson),
                KmCommandNames.StageAngeFight => DispatchStageAngeFight(requestJson),
                KmCommandNames.StageAngeFightUninstall => DispatchStageAngeFightUninstall(requestJson),
                KmCommandNames.LoadFairyGymBoostsWorkflow => DispatchLoadFairyGymBoostsWorkflow(requestJson),
                KmCommandNames.StageFairyGymBoosts => DispatchStageFairyGymBoosts(requestJson),
                KmCommandNames.LoadFashionUnlockWorkflow => DispatchLoadFashionUnlockWorkflow(requestJson),
                KmCommandNames.StageFashionUnlockInstall => DispatchStageFashionUnlockInstall(requestJson),
                KmCommandNames.StageFashionUnlockUninstall => DispatchStageFashionUnlockUninstall(requestJson),
                KmCommandNames.LoadGymUniformRemovalWorkflow => DispatchLoadGymUniformRemovalWorkflow(requestJson),
                KmCommandNames.StageGymUniformRemovalInstall => DispatchStageGymUniformRemovalInstall(requestJson),
                KmCommandNames.StageGymUniformRemovalUninstall => DispatchStageGymUniformRemovalUninstall(requestJson),
                KmCommandNames.LoadHyperspaceBypassWorkflow => DispatchLoadHyperspaceBypassWorkflow(requestJson),
                KmCommandNames.StageHyperspaceBypassInstall => DispatchStageHyperspaceBypassInstall(requestJson),
                KmCommandNames.StageHyperspaceBypassUninstall => DispatchStageHyperspaceBypassUninstall(requestJson),
                KmCommandNames.LoadIvScreenWorkflow => DispatchLoadIvScreenWorkflow(requestJson),
                KmCommandNames.StageIvScreenInstall => DispatchStageIvScreenInstall(requestJson),
                KmCommandNames.StageIvScreenUninstall => DispatchStageIvScreenUninstall(requestJson),
                KmCommandNames.LoadExeFsPatchWorkflow => DispatchLoadExeFsPatchWorkflow(requestJson),
                KmCommandNames.StageExeFsPatch => DispatchStageExeFsPatch(requestJson),
                KmCommandNames.LoadRoyalCandyWorkflow => DispatchLoadRoyalCandyWorkflow(requestJson),
                KmCommandNames.StageRoyalCandyWorkflow => DispatchStageRoyalCandyWorkflow(requestJson),
                KmCommandNames.LoadStartingItemsWorkflow => DispatchLoadStartingItemsWorkflow(requestJson),
                KmCommandNames.StageStartingItems => DispatchStageStartingItems(requestJson),
                KmCommandNames.LoadNpcItemGiftWorkflow => DispatchLoadNpcItemGiftWorkflow(requestJson),
                KmCommandNames.StageNpcItemGift => DispatchStageNpcItemGift(requestJson),
                KmCommandNames.LoadBattleCafeRewardsWorkflow => DispatchLoadBattleCafeRewardsWorkflow(requestJson),
                KmCommandNames.StageBattleCafeRewardRows => DispatchStageBattleCafeRewardRows(requestJson),
                KmCommandNames.LoadSpreadsheetImportWorkflow => DispatchLoadSpreadsheetImportWorkflow(requestJson),
                KmCommandNames.PreviewSpreadsheetImport => DispatchPreviewSpreadsheetImport(requestJson),
                KmCommandNames.LoadModMergerWorkflow => DispatchLoadModMergerWorkflow(requestJson),
                KmCommandNames.StageModMerge => DispatchStageModMerge(requestJson),
                KmCommandNames.ApplyModMerge => DispatchApplyModMerge(requestJson),
                KmCommandNames.LoadSvModMergerWorkflow => DispatchLoadSvModMergerWorkflow(requestJson),
                KmCommandNames.StageSvModMerge => DispatchStageSvModMerge(requestJson),
                KmCommandNames.ApplySvModMerge => DispatchApplySvModMerge(requestJson),
                KmCommandNames.LoadZaModMergerWorkflow => DispatchLoadZaModMergerWorkflow(requestJson),
                KmCommandNames.StageZaModMerge => DispatchStageZaModMerge(requestJson),
                KmCommandNames.ApplyZaModMerge => DispatchApplyZaModMerge(requestJson),
                KmCommandNames.GetSvCacheStatus => DispatchGetSvCacheStatus(requestJson),
                KmCommandNames.UpdateSvCacheSettings => DispatchUpdateSvCacheSettings(requestJson),
                KmCommandNames.ClearSvCache => DispatchClearSvCache(requestJson),
                KmCommandNames.WarmupSvCacheStep => DispatchWarmupSvCacheStep(requestJson),
                KmCommandNames.GetZaCacheStatus => DispatchGetZaCacheStatus(requestJson),
                KmCommandNames.UpdateZaCacheSettings => DispatchUpdateZaCacheSettings(requestJson),
                KmCommandNames.ClearZaCache => DispatchClearZaCache(requestJson),
                KmCommandNames.WarmupZaCacheStep => DispatchWarmupZaCacheStep(requestJson),
                KmCommandNames.GetSwShCacheStatus => DispatchGetSwShCacheStatus(requestJson),
                KmCommandNames.UpdateSwShCacheSettings => DispatchUpdateSwShCacheSettings(requestJson),
                KmCommandNames.ClearSwShCache => DispatchClearSwShCache(requestJson),
                KmCommandNames.WarmupSwShCacheStep => DispatchWarmupSwShCacheStep(requestJson),
                KmCommandNames.LoadFpsPatch => DispatchLoadFpsPatch(requestJson),
                KmCommandNames.ApplyFpsPatch => DispatchApplyFpsPatch(requestJson),
                KmCommandNames.RestoreFpsPatch => DispatchRestoreFpsPatch(requestJson),
                KmCommandNames.LoadProfanityFilter => DispatchLoadProfanityFilter(requestJson),
                KmCommandNames.ApplyProfanityFilter => DispatchApplyProfanityFilter(requestJson),
                KmCommandNames.RestoreProfanityFilter => DispatchRestoreProfanityFilter(requestJson),
                KmCommandNames.ImportRandomizerSeed => DispatchImportRandomizerSeed(requestJson),
                KmCommandNames.ApplyRandomizer => DispatchApplyRandomizer(requestJson),
                KmCommandNames.RestoreRandomizer => DispatchRestoreRandomizer(requestJson),
                KmCommandNames.LoadGameDumpWorkflow => DispatchLoadGameDumpWorkflow(requestJson),
                KmCommandNames.RunGameDump => DispatchRunGameDump(requestJson),
                KmCommandNames.StartEditSession => DispatchStartEditSession(requestJson),
                KmCommandNames.ValidateEditSession => DispatchValidateEditSession(requestJson),
                KmCommandNames.CreateChangePlan => DispatchCreateChangePlan(requestJson),
                KmCommandNames.ApplyChangePlan => DispatchApplyChangePlan(requestJson),
                KmCommandNames.ReadChangeSets => DispatchReadChangeSets(requestJson),
                KmCommandNames.MutateChangeSets => DispatchMutateChangeSets(requestJson),
                KmCommandNames.CaptureChangeSetSession => DispatchCaptureChangeSetSession(requestJson),
                KmCommandNames.MaterializeChangeSets => DispatchMaterializeChangeSets(requestJson),
                KmCommandNames.ExportChangeSets => DispatchExportChangeSets(requestJson),
                KmCommandNames.ImportChangeSets => DispatchImportChangeSets(requestJson),
                KmCommandNames.ReadSemanticCapabilities => DispatchReadSemanticCapabilities(requestJson),
                KmCommandNames.SearchSemantic => DispatchSearchSemantic(requestJson),
                KmCommandNames.ReadSemanticEntity => DispatchReadSemanticEntity(requestJson),
                KmCommandNames.CompareSemantic => DispatchCompareSemantic(requestJson),
                KmCommandNames.QuerySemanticReferences => DispatchQuerySemanticReferences(requestJson),
                KmCommandNames.QuerySemanticImpact => DispatchQuerySemanticImpact(requestJson),
                KmCommandNames.QuerySemanticOwnership => DispatchQuerySemanticOwnership(requestJson),
                KmCommandNames.CompareExternalSemantic => DispatchCompareExternalSemantic(requestJson),
                KmCommandNames.QuerySemanticChanges => DispatchQuerySemanticChanges(requestJson),
                KmCommandNames.QueryBalanceLab => DispatchQueryBalanceLab(requestJson),
                KmCommandNames.ReadGameModuleCapabilities => DispatchReadGameModuleCapabilities(requestJson),
                KmCommandNames.QueryGameModule => DispatchQueryGameModule(requestJson),
                KmCommandNames.ReadGuidedDesignCapabilities => DispatchReadGuidedDesignCapabilities(requestJson),
                KmCommandNames.PreviewGuidedDesign => DispatchPreviewGuidedDesign(requestJson),
                KmCommandNames.ImportGuidedDesignProposal => DispatchImportGuidedDesignProposal(requestJson),
                KmCommandNames.ReadSemanticMergeCapabilities => DispatchReadSemanticMergeCapabilities(requestJson),
                KmCommandNames.OpenSemanticMergeSource => DispatchOpenSemanticMergeSource(requestJson),
                KmCommandNames.PreviewSemanticMerge => DispatchPreviewSemanticMerge(requestJson),
                KmCommandNames.ImportSemanticMerge => DispatchImportSemanticMerge(requestJson),
                KmCommandNames.ExportKmRecipe => DispatchExportKmRecipe(requestJson),
                KmCommandNames.ValidateKmRecipe => DispatchValidateKmRecipe(requestJson),
                KmCommandNames.PreviewKmRecipe => DispatchPreviewKmRecipe(requestJson),
                KmCommandNames.ImportKmRecipe => DispatchImportKmRecipe(requestJson),
                KmCommandNames.ReadResearchLabCapabilities => DispatchReadResearchLabCapabilities(requestJson),
                KmCommandNames.OpenResearchSource => DispatchOpenResearchSource(requestJson),
                KmCommandNames.CloseResearchSource => DispatchCloseResearchSource(requestJson),
                KmCommandNames.CompareResearchSources => DispatchCompareResearchSources(requestJson),
                KmCommandNames.ReadResearchByteWindow => DispatchReadResearchByteWindow(requestJson),
                KmCommandNames.ReadResearchAnnotations => DispatchReadResearchAnnotations(requestJson),
                KmCommandNames.MutateResearchAnnotations => DispatchMutateResearchAnnotations(requestJson),
                KmCommandNames.ReadWorkspaceDrafts => DispatchReadWorkspaceDrafts(requestJson),
                KmCommandNames.WriteWorkspaceDrafts => DispatchWriteWorkspaceDrafts(requestJson),
                KmCommandNames.DeleteWorkspaceDrafts => DispatchDeleteWorkspaceDrafts(requestJson),
                KmCommandNames.ReadProjectSourceRevision => DispatchReadProjectSourceRevision(requestJson),
                KmCommandNames.ReadWorkspaceApplicationState => DispatchReadWorkspaceApplicationState(requestJson),
                KmCommandNames.WriteWorkspaceApplicationState => DispatchWriteWorkspaceApplicationState(requestJson),
                KmCommandNames.ReadWorkspaceProjectState => DispatchReadWorkspaceProjectState(requestJson),
                KmCommandNames.WriteWorkspaceProjectState => DispatchWriteWorkspaceProjectState(requestJson),
                KmCommandNames.DeleteWorkspaceProjectState => DispatchDeleteWorkspaceProjectState(requestJson),
                KmCommandNames.GetOutputRecoveryStatus => DispatchGetOutputRecoveryStatus(requestJson),
                KmCommandNames.GetGameplaySettings => DispatchGetGameplaySettings(requestJson),
                KmCommandNames.PreviewGameplaySettingsUpdate => DispatchPreviewGameplaySettingsUpdate(requestJson),
                KmCommandNames.ApplyGameplaySettingsUpdate => DispatchApplyGameplaySettingsUpdate(requestJson),
                KmCommandNames.InspectInGameSettingsPackage => DispatchInspectInGameSettingsPackage(requestJson),
                KmCommandNames.PreviewInGameSettingsPackage => DispatchPreviewInGameSettingsPackage(requestJson),
                KmCommandNames.ApplyInGameSettingsPackage => DispatchApplyInGameSettingsPackage(requestJson),
                KmCommandNames.ReconcileOutputRecovery => DispatchReconcileOutputRecovery(requestJson),
                KmCommandNames.ScanOutputIntegrity => DispatchScanOutputIntegrity(requestJson),
                KmCommandNames.PreviewOutputCleanup => DispatchPreviewOutputCleanup(requestJson),
                KmCommandNames.ApplyOutputCleanup => DispatchApplyOutputCleanup(requestJson),
                KmCommandNames.ListOutputHistory => DispatchListOutputHistory(requestJson),
                KmCommandNames.ListOutputCheckpoints => DispatchListOutputCheckpoints(requestJson),
                KmCommandNames.CreateOutputCheckpoint => DispatchCreateOutputCheckpoint(requestJson),
                KmCommandNames.PreviewOutputCheckpointRestore => DispatchPreviewOutputCheckpointRestore(requestJson),
                KmCommandNames.RestoreOutputCheckpoint => DispatchRestoreOutputCheckpoint(requestJson),
                KmCommandNames.DeleteOutputCheckpoint => DispatchDeleteOutputCheckpoint(requestJson),
                KmCommandNames.PreviewProjectRelocation => DispatchPreviewProjectRelocation(requestJson),
                KmCommandNames.ApplyProjectRelocation => DispatchApplyProjectRelocation(requestJson),
                KmCommandNames.BuildSupportReport => DispatchBuildSupportReport(requestJson),
                null => SerializeFailure(BridgeErrorCodes.MissingCommand, "Bridge request is missing a command.", envelope?.RequestId),
                _ => SerializeFailure(
                    BridgeErrorCodes.UnsupportedCommand,
                    $"Bridge command '{command}' is not supported.",
                    envelope?.RequestId),
            };

            if (IsWorkflowCacheMutation(command))
            {
                ClearWorkflowMemoryCaches(clearReusableDataCaches: false);
            }

            return (
                BoundSerializedResponse(response, requestId),
                RequiresDispatcherReset: false);
        }
        catch (BridgeRequestException exception)
        {
            var code = exception.Code ?? BridgeErrorCodes.InvalidJson;
            return (
                SerializeFailure(
                    code,
                    exception.Message,
                    requestId),
                RequiresDispatcherReset: false);
        }

        catch (ResearchLabValidationException exception)
        {
            return (
                SerializeFailure(
                    GetResearchLabErrorCode(exception),
                    exception.Message,
                    requestId),
                RequiresDispatcherReset: false);
        }

        catch (SemanticExploreValidationException exception)
        {
            return (
                SerializeFailure(
                    GetSemanticExploreErrorCode(exception.FailureKind),
                    exception.Message,
                    requestId),
                RequiresDispatcherReset: false);
        }

        catch (GuidedDesignValidationException exception)
        {
            return (
                SerializeFailure(
                    BridgeErrorCodes.GuidedDesignStaleProposal,
                    exception.Message,
                    requestId),
                RequiresDispatcherReset: false);
        }

        catch (SemanticMergeValidationException exception)
        {
            return (
                SerializeFailure(
                    exception.FailureKind == SemanticMergeFailureKind.StaleRecipeProposal
                        ? BridgeErrorCodes.RecipeStaleProposal
                        : BridgeErrorCodes.SemanticMergeStaleProposal,
                    exception.Message,
                    requestId),
                RequiresDispatcherReset: false);
        }

        catch (GeneratedChangeSetContextChangedException exception)
        {
            var code = command switch
            {
                KmCommandNames.ImportSemanticMerge => BridgeErrorCodes.SemanticMergeStaleProposal,
                KmCommandNames.ImportKmRecipe => BridgeErrorCodes.RecipeStaleProposal,
                _ => BridgeErrorCodes.GuidedDesignStaleProposal,
            };
            return (
                SerializeFailure(
                    code,
                    exception.Message,
                    requestId),
                RequiresDispatcherReset: false);
        }

        catch (WorkspaceDraftValidationException exception)
        {
            return (
                SerializeFailure(
                    BridgeErrorCodes.DataInvalid,
                    exception.Message,
                    requestId),
                RequiresDispatcherReset: false);
        }
        catch (WorkspacePersonalStateValidationException exception)
        {
            return (
                SerializeFailure(
                    BridgeErrorCodes.DataInvalid,
                    exception.Message,
                    requestId),
                RequiresDispatcherReset: false);
        }
        catch (ChangeSetValidationException exception)
        {
            return (
                SerializeFailure(
                    BridgeErrorCodes.DataInvalid,
                    exception.Message,
                    requestId),
                RequiresDispatcherReset: false);
        }
        catch (EditSessionContractException exception)
        {
            return (
                SerializeFailure(
                    BridgeErrorCodes.DataInvalid,
                    exception.Message,
                    requestId),
                RequiresDispatcherReset: false);
        }
        catch (WorkspaceDocumentStoreException exception)
        {
            return (
                SerializeFailure(
                    GetWorkspaceErrorCode(exception),
                    exception.Message,
                    requestId),
                RequiresDispatcherReset: false);
        }
        catch (GameplaySettingsUnavailableException exception)
        {
            return (
                SerializeFailure(
                    BridgeErrorCodes.GameplaySettingsUnavailable,
                    exception.Message,
                    requestId),
                RequiresDispatcherReset: false);
        }
        catch (GameplaySettingsStateConflictException exception)
        {
            return (
                SerializeFailure(
                    BridgeErrorCodes.GameplaySettingsStateStale,
                    exception.Message,
                    requestId),
                RequiresDispatcherReset: false);
        }
        catch (GameplaySettingsReviewExpiredException exception)
        {
            return (
                SerializeFailure(
                    BridgeErrorCodes.GameplaySettingsReviewExpired,
                    exception.Message,
                    requestId),
                RequiresDispatcherReset: false);
        }
        catch (InGameSettingsPackageUnavailableException exception)
        {
            return (
                SerializeFailure(
                    BridgeErrorCodes.InGameSettingsPackageUnavailable,
                    exception.Message,
                    requestId),
                RequiresDispatcherReset: false);
        }
        catch (InGameSettingsPackageStateConflictException exception)
        {
            return (
                SerializeFailure(
                    BridgeErrorCodes.InGameSettingsPackageStateStale,
                    exception.Message,
                    requestId),
                RequiresDispatcherReset: false);
        }
        catch (InGameSettingsPackageReviewExpiredException exception)
        {
            return (
                SerializeFailure(
                    BridgeErrorCodes.InGameSettingsPackageReviewExpired,
                    exception.Message,
                    requestId),
                RequiresDispatcherReset: false);
        }
        catch (OutputScopeMismatchException exception)
        {
            return (
                SerializeFailure(
                    BridgeErrorCodes.OutputConcurrentModification,
                    exception.Message,
                    requestId),
                RequiresDispatcherReset: false);
        }
        catch (OutputReviewExpiredException exception)
        {
            return (
                SerializeFailure(
                    BridgeErrorCodes.OutputConcurrentModification,
                    exception.Message,
                    requestId),
                RequiresDispatcherReset: false);
        }
        catch (OutputOwnershipUnprovenException exception)
        {
            return (
                SerializeFailure(
                    BridgeErrorCodes.OutputOwnershipUnproven,
                    exception.Message,
                    requestId),
                RequiresDispatcherReset: false);
        }
        catch (OutputOwnershipConflictException exception)
        {
            return (
                SerializeFailure(
                    BridgeErrorCodes.OutputOwnershipUnproven,
                    exception.Message,
                    requestId),
                RequiresDispatcherReset: false);
        }
        catch (OutputCheckpointNotFoundException exception)
        {
            return (
                SerializeFailure(
                    BridgeErrorCodes.OutputCheckpointNotFound,
                    exception.Message,
                    requestId),
                RequiresDispatcherReset: false);
        }
        catch (OutputCheckpointConflictException exception)
        {
            return (
                SerializeFailure(
                    BridgeErrorCodes.OutputCheckpointConflict,
                    exception.Message,
                    requestId),
                RequiresDispatcherReset: false);
        }
        catch (OutputCheckpointAlreadyCurrentException exception)
        {
            return (
                SerializeFailure(
                    BridgeErrorCodes.OutputCheckpointConflict,
                    exception.Message,
                    requestId),
                RequiresDispatcherReset: false);
        }
        catch (ProjectRelocationReviewMismatchException exception)
        {
            return (
                SerializeFailure(
                    BridgeErrorCodes.ProjectRelocationMismatch,
                    exception.Message,
                    requestId),
                RequiresDispatcherReset: false);
        }
        catch (ProjectRelocationConflictException exception)
        {
            return (
                SerializeFailure(
                    BridgeErrorCodes.ProjectRelocationConflict,
                    exception.Message,
                    requestId),
                RequiresDispatcherReset: false);
        }
        catch (OutputPreimageConflictException exception)
        {
            return (
                SerializeFailure(
                    BridgeErrorCodes.OutputConcurrentModification,
                    exception.Message,
                    requestId),
                RequiresDispatcherReset: false);
        }
        catch (OutputReviewStateConflictException exception)
        {
            return (
                SerializeFailure(
                    BridgeErrorCodes.OutputConcurrentModification,
                    exception.Message,
                    requestId),
                RequiresDispatcherReset: false);
        }
        catch (OutputStateRevisionConflictException exception)
        {
            return (
                SerializeFailure(
                    BridgeErrorCodes.OutputConcurrentModification,
                    exception.Message,
                    requestId),
                RequiresDispatcherReset: false);
        }
        catch (OutputRecoveryRequiredException exception)
        {
            return (
                SerializeFailure(
                    BridgeErrorCodes.OutputRecoveryRequired,
                    exception.Message,
                    requestId),
                RequiresDispatcherReset: false);
        }
        catch (OutputRootLockTimeoutException exception)
        {
            return (
                SerializeFailure(
                    BridgeErrorCodes.OutputRootBusy,
                    exception.Message,
                    requestId),
                RequiresDispatcherReset: false);
        }
        catch (OutputPathSecurityException exception)
        {
            return (
                SerializeFailure(
                    BridgeErrorCodes.OutputUnsafePath,
                    exception.Message,
                    requestId),
                RequiresDispatcherReset: false);
        }
        catch (OutputLimitExceededException exception)
        {
            return (
                SerializeFailure(
                    BridgeErrorCodes.OutputLimitExceeded,
                    exception.Message,
                    requestId),
                RequiresDispatcherReset: false);
        }
        catch (ZaOutputApplyNotCommittedException exception)
        {
            var code = exception.Result.Outcome == OutputApplyOutcome.RecoveryRequired
                ? BridgeErrorCodes.OutputRecoveryRequired
                : BridgeErrorCodes.IoFailed;
            return (
                SerializeFailure(
                    code,
                    exception.Message,
                    requestId),
                RequiresDispatcherReset: false);
        }
        catch (ProjectFileGraphDiscoveryException exception)
        {
            return (
                SerializeFailure(
                    BridgeErrorCodes.OutputLimitExceeded,
                    exception.Message,
                    requestId),
                RequiresDispatcherReset: false);
        }
        catch (OutputCoordinatorException exception)
        {
            return (
                SerializeFailure(
                    BridgeErrorCodes.IoFailed,
                    exception.Message,
                    requestId),
                RequiresDispatcherReset: false);
        }
        catch (ArgumentException exception) when (IsOutputSafetyCommand(command))
        {
            return (
                SerializeFailure(
                    BridgeErrorCodes.DataInvalid,
                    exception.Message,
                    requestId),
                RequiresDispatcherReset: false);
        }
        catch (UnauthorizedAccessException) when (IsOutputSafetyCommand(command))
        {
            return (
                SerializeFailure(
                    BridgeErrorCodes.AccessDenied,
                    "The output operation could not access the selected output root.",
                    requestId),
                RequiresDispatcherReset: false);
        }
        catch (IOException) when (IsOutputSafetyCommand(command))
        {
            return (
                SerializeFailure(
                    BridgeErrorCodes.IoFailed,
                    "The output operation could not complete because an input or output operation failed.",
                    requestId),
                RequiresDispatcherReset: false);
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            var diagnostic = BridgeUnexpectedFailureClassifier.Classify(
                exception,
                command,
                selectedGame);
            return (
                SerializeFailure(
                    BridgeErrorCodes.Unexpected,
                    "The project bridge hit an unexpected internal error while processing the request.",
                    requestId,
                    [diagnostic]),
                RequiresDispatcherReset: true);
        }
    }

    private string DispatchOpenProject(string requestJson)
    {
        var request = DeserializeRequest<OpenProjectRequest>(requestJson);
        var openedProject = projectWorkspaceService.Open(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var health = ReconcileOutputOnProjectActivation(
            openedProject.Id.ToString(),
            request.Payload.Paths,
            ProjectBridgeMapper.ToDto(openedProject.Health));
        var response = new OpenProjectResponse(
            openedProject.Id.ToString(),
            health,
            ProjectBridgeMapper.ToDto(openedProject.FileGraph));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchReadSemanticCapabilities(string requestJson)
    {
        var request = DeserializeRequest<ReadSemanticCapabilitiesRequest>(requestJson);
        return SerializeSuccess(
            semanticExploreApplicationService.ReadCapabilities(request.Payload),
            request.RequestId);
    }

    private string DispatchSearchSemantic(string requestJson)
    {
        var request = DeserializeRequest<SearchSemanticRequest>(requestJson);
        return SerializeSuccess(
            semanticExploreApplicationService.Search(request.Payload),
            request.RequestId);
    }

    private string DispatchReadSemanticEntity(string requestJson)
    {
        var request = DeserializeRequest<ReadSemanticEntityRequest>(requestJson);
        return SerializeSuccess(
            semanticExploreApplicationService.ReadEntity(request.Payload),
            request.RequestId);
    }

    private string DispatchCompareSemantic(string requestJson)
    {
        var request = DeserializeRequest<CompareSemanticRequest>(requestJson);
        return SerializeSuccess(
            semanticExploreApplicationService.Compare(request.Payload),
            request.RequestId);
    }

    private string DispatchQuerySemanticReferences(string requestJson)
    {
        var request = DeserializeRequest<QuerySemanticReferencesRequest>(requestJson);
        return SerializeSuccess(
            semanticExploreApplicationService.QueryReferences(request.Payload),
            request.RequestId);
    }

    private string DispatchQuerySemanticImpact(string requestJson)
    {
        var request = DeserializeRequest<QuerySemanticImpactRequest>(requestJson);
        return SerializeSuccess(
            semanticExploreApplicationService.QueryImpact(request.Payload),
            request.RequestId);
    }

    private string DispatchQuerySemanticOwnership(string requestJson)
    {
        var request = DeserializeRequest<QuerySemanticOwnershipRequest>(requestJson);
        return SerializeSuccess(
            semanticExploreApplicationService.QueryOwnership(request.Payload),
            request.RequestId);
    }

    private string DispatchCompareExternalSemantic(string requestJson)
    {
        var request = DeserializeRequest<CompareExternalSemanticRequest>(requestJson);
        return SerializeSuccess(
            semanticExploreApplicationService.CompareExternal(request.Payload),
            request.RequestId);
    }

    private string DispatchQuerySemanticChanges(string requestJson)
    {
        var request = DeserializeRequest<QuerySemanticChangesRequest>(requestJson);
        return SerializeSuccess(
            semanticExploreApplicationService.QueryChanges(request.Payload),
            request.RequestId);
    }

    private string DispatchQueryBalanceLab(string requestJson)
    {
        var request = DeserializeRequest<QueryBalanceLabRequest>(requestJson);
        return SerializeSuccess(
            balanceLabApplicationService.Query(request.Payload),
            request.RequestId);
    }

    private string DispatchReadGameModuleCapabilities(string requestJson)
    {
        var request = DeserializeRequest<ReadGameModuleCapabilitiesRequest>(requestJson);
        return SerializeSuccess(
            gameModuleApplicationService.ReadCapabilities(request.Payload),
            request.RequestId);
    }

    private string DispatchQueryGameModule(string requestJson)
    {
        var request = DeserializeRequest<QueryGameModuleRequest>(requestJson);
        return SerializeSuccess(
            gameModuleApplicationService.Query(request.Payload),
            request.RequestId);
    }

    private string DispatchReadGuidedDesignCapabilities(string requestJson)
    {
        var request = DeserializeRequest<ReadGuidedDesignCapabilitiesRequest>(requestJson);
        return SerializeSuccess(
            guidedDesignApplicationService.ReadCapabilities(request.Payload),
            request.RequestId);
    }

    private string DispatchPreviewGuidedDesign(string requestJson)
    {
        var request = DeserializeRequest<PreviewGuidedDesignRequest>(requestJson);
        return SerializeSuccess(
            guidedDesignApplicationService.Preview(request.Payload),
            request.RequestId);
    }

    private string DispatchImportGuidedDesignProposal(string requestJson)
    {
        var request = DeserializeRequest<ImportGuidedDesignProposalRequest>(requestJson);
        return SerializeSuccess(
            guidedDesignApplicationService.Import(request.Payload),
            request.RequestId);
    }

    private string DispatchReadSemanticMergeCapabilities(string requestJson)
    {
        var request = DeserializeRequest<ReadSemanticMergeCapabilitiesRequest>(requestJson);
        return SerializeSuccess(
            semanticMergeApplicationService.ReadCapabilities(request.Payload),
            request.RequestId);
    }

    private string DispatchOpenSemanticMergeSource(string requestJson)
    {
        var request = DeserializeRequest<OpenSemanticMergeSourceRequest>(requestJson);
        return SerializeSuccess(
            semanticMergeApplicationService.OpenSource(request.Payload),
            request.RequestId);
    }

    private string DispatchPreviewSemanticMerge(string requestJson)
    {
        var request = DeserializeRequest<PreviewSemanticMergeRequest>(requestJson);
        return SerializeSuccess(
            semanticMergeApplicationService.Preview(request.Payload),
            request.RequestId);
    }

    private string DispatchImportSemanticMerge(string requestJson)
    {
        var request = DeserializeRequest<ImportSemanticMergeRequest>(requestJson);
        return SerializeSuccess(
            semanticMergeApplicationService.Import(request.Payload),
            request.RequestId);
    }

    private string DispatchExportKmRecipe(string requestJson)
    {
        var request = DeserializeRequest<ExportKmRecipeRequest>(requestJson);
        return SerializeSuccess(
            semanticMergeApplicationService.ExportRecipe(request.Payload),
            request.RequestId);
    }

    private string DispatchValidateKmRecipe(string requestJson)
    {
        var request = DeserializeRequest<ValidateKmRecipeRequest>(requestJson);
        return SerializeSuccess(
            semanticMergeApplicationService.ValidateRecipe(request.Payload),
            request.RequestId);
    }

    private string DispatchPreviewKmRecipe(string requestJson)
    {
        var request = DeserializeRequest<PreviewKmRecipeRequest>(requestJson);
        return SerializeSuccess(
            semanticMergeApplicationService.PreviewRecipe(request.Payload),
            request.RequestId);
    }

    private string DispatchImportKmRecipe(string requestJson)
    {
        var request = DeserializeRequest<ImportKmRecipeRequest>(requestJson);
        return SerializeSuccess(
            semanticMergeApplicationService.ImportRecipe(request.Payload),
            request.RequestId);
    }

    private string DispatchReadResearchLabCapabilities(string requestJson)
    {
        var request = DeserializeRequest<ReadResearchLabCapabilitiesRequest>(requestJson);
        return SerializeSuccess(
            researchLabApplicationService.ReadCapabilities(request.Payload),
            request.RequestId);
    }

    private string DispatchOpenResearchSource(string requestJson)
    {
        var request = DeserializeRequest<OpenResearchSourceRequest>(requestJson);
        return SerializeSuccess(
            researchLabApplicationService.OpenSource(request.Payload),
            request.RequestId);
    }

    private string DispatchCloseResearchSource(string requestJson)
    {
        var request = DeserializeRequest<CloseResearchSourceRequest>(requestJson);
        return SerializeSuccess(
            researchLabApplicationService.CloseSource(request.Payload),
            request.RequestId);
    }

    private string DispatchCompareResearchSources(string requestJson)
    {
        var request = DeserializeRequest<CompareResearchSourcesRequest>(requestJson);
        return SerializeSuccess(
            researchLabApplicationService.Compare(request.Payload),
            request.RequestId);
    }

    private string DispatchReadResearchByteWindow(string requestJson)
    {
        var request = DeserializeRequest<ReadResearchByteWindowRequest>(requestJson);
        return SerializeSuccess(
            researchLabApplicationService.ReadByteWindow(request.Payload),
            request.RequestId);
    }

    private string DispatchReadResearchAnnotations(string requestJson)
    {
        var request = DeserializeRequest<ReadResearchAnnotationsRequest>(requestJson);
        return SerializeSuccess(
            researchLabApplicationService.ReadAnnotations(request.Payload),
            request.RequestId);
    }

    private string DispatchMutateResearchAnnotations(string requestJson)
    {
        var request = DeserializeRequest<MutateResearchAnnotationsRequest>(requestJson);
        return SerializeSuccess(
            researchLabApplicationService.MutateAnnotations(request.Payload),
            request.RequestId);
    }

    private string DispatchReadChangeSets(string requestJson)
    {
        var request = DeserializeRequest<ReadChangeSetWorkspaceRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Scope.Paths);
        var response = changeSetApplicationService
            .ReadAsync(
                request.Payload,
                (session, outputMode) => CreateChangePlanForSession(
                    paths,
                    session,
                    outputMode))
            .GetAwaiter()
            .GetResult();
        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchMutateChangeSets(string requestJson)
    {
        var request = DeserializeRequest<MutateChangeSetWorkspaceRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Scope.Paths);
        var response = changeSetApplicationService
            .MutateAsync(
                request.Payload,
                (session, outputMode) => CreateChangePlanForSession(
                    paths,
                    session,
                    outputMode))
            .GetAwaiter()
            .GetResult();
        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchCaptureChangeSetSession(string requestJson)
    {
        var request = DeserializeRequest<CaptureChangeSetSessionRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Scope.Paths);
        var response = changeSetApplicationService
            .CaptureSessionAsync(
                request.Payload,
                (session, outputMode) => CreateChangePlanForSession(
                    paths,
                    session,
                    outputMode))
            .GetAwaiter()
            .GetResult();
        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchMaterializeChangeSets(string requestJson)
    {
        var request = DeserializeRequest<MaterializeChangeSetWorkspaceRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Scope.Paths);
        var response = changeSetApplicationService
            .MaterializeAsync(
                request.Payload,
                (session, outputMode) => CreateChangePlanForSession(
                    paths,
                    session,
                    outputMode))
            .GetAwaiter()
            .GetResult();
        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchExportChangeSets(string requestJson)
    {
        var request = DeserializeRequest<ExportChangeSetsRequest>(requestJson);
        var response = changeSetApplicationService
            .ExportAsync(request.Payload)
            .GetAwaiter()
            .GetResult();
        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchImportChangeSets(string requestJson)
    {
        var request = DeserializeRequest<ImportChangeSetsRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Scope.Paths);
        var response = changeSetApplicationService
            .ImportAsync(
                request.Payload,
                (session, outputMode) => CreateChangePlanForSession(
                    paths,
                    session,
                    outputMode))
            .GetAwaiter()
            .GetResult();
        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchReadWorkspaceDrafts(string requestJson)
    {
        var request = DeserializeRequest<ReadWorkspaceDraftsRequest>(requestJson);
        var response = workspaceDraftApplicationService
            .ReadAsync(request.Payload.ProjectId)
            .GetAwaiter()
            .GetResult();

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchWriteWorkspaceDrafts(string requestJson)
    {
        var request = DeserializeRequest<WriteWorkspaceDraftsRequest>(requestJson);
        var response = workspaceDraftApplicationService
            .WriteAsync(
                request.Payload.ProjectId,
                request.Payload.Document,
                request.Payload.ExpectedETag)
            .GetAwaiter()
            .GetResult();

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchDeleteWorkspaceDrafts(string requestJson)
    {
        var request = DeserializeRequest<DeleteWorkspaceDraftsRequest>(requestJson);
        var response = workspaceDraftApplicationService
            .DeleteAsync(request.Payload.ProjectId, request.Payload.ExpectedETag)
            .GetAwaiter()
            .GetResult();

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchReadWorkspaceApplicationState(string requestJson)
    {
        var request = DeserializeRequest<ReadWorkspaceApplicationStateRequest>(requestJson);
        var response = workspacePersonalStateApplicationService
            .ReadApplicationAsync()
            .GetAwaiter()
            .GetResult();
        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchWriteWorkspaceApplicationState(string requestJson)
    {
        var request = DeserializeRequest<WriteWorkspaceApplicationStateRequest>(requestJson);
        var response = workspacePersonalStateApplicationService
            .WriteApplicationAsync(request.Payload.Document, request.Payload.ExpectedETag)
            .GetAwaiter()
            .GetResult();
        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchReadWorkspaceProjectState(string requestJson)
    {
        var request = DeserializeRequest<ReadWorkspaceProjectStateRequest>(requestJson);
        var response = workspacePersonalStateApplicationService
            .ReadProjectAsync(request.Payload.ProjectId)
            .GetAwaiter()
            .GetResult();
        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchWriteWorkspaceProjectState(string requestJson)
    {
        var request = DeserializeRequest<WriteWorkspaceProjectStateRequest>(requestJson);
        var response = workspacePersonalStateApplicationService
            .WriteProjectAsync(
                request.Payload.ProjectId,
                request.Payload.Document,
                request.Payload.ExpectedETag)
            .GetAwaiter()
            .GetResult();
        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchDeleteWorkspaceProjectState(string requestJson)
    {
        var request = DeserializeRequest<DeleteWorkspaceProjectStateRequest>(requestJson);
        var response = workspacePersonalStateApplicationService
            .DeleteProjectAsync(request.Payload.ProjectId, request.Payload.ExpectedETag)
            .GetAwaiter()
            .GetResult();
        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchGetOutputRecoveryStatus(string requestJson)
    {
        var request = DeserializeRequest<GetOutputRecoveryStatusRequest>(requestJson);
        var response = outputSafetyApplicationService
            .GetRecoveryStatusAsync(request.Payload)
            .GetAwaiter()
            .GetResult();
        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchGetGameplaySettings(string requestJson)
    {
        var request = DeserializeRequest<GetGameplaySettingsRequest>(requestJson);
        var response = gameplaySettingsApplicationService
            .GetAsync(request.Payload)
            .GetAwaiter()
            .GetResult();
        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchPreviewGameplaySettingsUpdate(string requestJson)
    {
        var request = DeserializeRequest<PreviewGameplaySettingsUpdateRequest>(requestJson);
        var response = gameplaySettingsApplicationService
            .PreviewUpdateAsync(request.Payload)
            .GetAwaiter()
            .GetResult();
        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchApplyGameplaySettingsUpdate(string requestJson)
    {
        var request = DeserializeRequest<ApplyGameplaySettingsUpdateRequest>(requestJson);
        var response = gameplaySettingsApplicationService
            .ApplyUpdateAsync(request.Payload)
            .GetAwaiter()
            .GetResult();
        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchInspectInGameSettingsPackage(string requestJson)
    {
        var request = DeserializeRequest<InspectInGameSettingsPackageRequest>(requestJson);
        var response = inGameSettingsPackageApplicationService
            .InspectAsync(request.Payload)
            .GetAwaiter()
            .GetResult();
        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchPreviewInGameSettingsPackage(string requestJson)
    {
        var request = DeserializeRequest<PreviewInGameSettingsPackageRequest>(requestJson);
        var response = inGameSettingsPackageApplicationService
            .PreviewAsync(request.Payload)
            .GetAwaiter()
            .GetResult();
        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchApplyInGameSettingsPackage(string requestJson)
    {
        var request = DeserializeRequest<ApplyInGameSettingsPackageRequest>(requestJson);
        var response = inGameSettingsPackageApplicationService
            .ApplyAsync(request.Payload)
            .GetAwaiter()
            .GetResult();
        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchReconcileOutputRecovery(string requestJson)
    {
        var request = DeserializeRequest<ReconcileOutputRecoveryRequest>(requestJson);
        var response = outputSafetyApplicationService
            .ReconcileRecoveryAsync(request.Payload)
            .GetAwaiter()
            .GetResult();
        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchScanOutputIntegrity(string requestJson)
    {
        var request = DeserializeRequest<ScanOutputIntegrityRequest>(requestJson);
        var response = outputSafetyApplicationService
            .ScanIntegrityAsync(request.Payload)
            .GetAwaiter()
            .GetResult();
        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchPreviewOutputCleanup(string requestJson)
    {
        var request = DeserializeRequest<PreviewOutputCleanupRequest>(requestJson);
        var response = outputSafetyApplicationService
            .PreviewCleanupAsync(request.Payload)
            .GetAwaiter()
            .GetResult();
        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchApplyOutputCleanup(string requestJson)
    {
        var request = DeserializeRequest<ApplyOutputCleanupRequest>(requestJson);
        var response = outputSafetyApplicationService
            .ApplyCleanupAsync(request.Payload)
            .GetAwaiter()
            .GetResult();
        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchListOutputHistory(string requestJson)
    {
        var request = DeserializeRequest<ListOutputHistoryRequest>(requestJson);
        var response = outputSafetyApplicationService
            .ListHistoryAsync(request.Payload)
            .GetAwaiter()
            .GetResult();
        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchListOutputCheckpoints(string requestJson)
    {
        var request = DeserializeRequest<ListOutputCheckpointsRequest>(requestJson);
        var response = outputSafetyApplicationService
            .ListCheckpointsAsync(request.Payload)
            .GetAwaiter()
            .GetResult();
        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchCreateOutputCheckpoint(string requestJson)
    {
        var request = DeserializeRequest<CreateOutputCheckpointRequest>(requestJson);
        var response = outputSafetyApplicationService
            .CreateCheckpointAsync(request.Payload)
            .GetAwaiter()
            .GetResult();
        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchPreviewOutputCheckpointRestore(string requestJson)
    {
        var request = DeserializeRequest<PreviewOutputCheckpointRestoreRequest>(requestJson);
        var response = outputSafetyApplicationService
            .PreviewCheckpointRestoreAsync(request.Payload)
            .GetAwaiter()
            .GetResult();
        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchRestoreOutputCheckpoint(string requestJson)
    {
        var request = DeserializeRequest<RestoreOutputCheckpointRequest>(requestJson);
        var response = outputSafetyApplicationService
            .RestoreCheckpointAsync(request.Payload)
            .GetAwaiter()
            .GetResult();
        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchDeleteOutputCheckpoint(string requestJson)
    {
        var request = DeserializeRequest<DeleteOutputCheckpointRequest>(requestJson);
        var response = outputSafetyApplicationService
            .DeleteCheckpointAsync(request.Payload)
            .GetAwaiter()
            .GetResult();
        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchPreviewProjectRelocation(string requestJson)
    {
        var request = DeserializeRequest<PreviewProjectRelocationRequest>(requestJson);
        var response = projectRelocationApplicationService
            .PreviewAsync(request.Payload)
            .GetAwaiter()
            .GetResult();
        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchApplyProjectRelocation(string requestJson)
    {
        var request = DeserializeRequest<ApplyProjectRelocationRequest>(requestJson);
        var response = projectRelocationApplicationService
            .ApplyAsync(request.Payload)
            .GetAwaiter()
            .GetResult();
        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchBuildSupportReport(string requestJson)
    {
        var request = DeserializeRequest<BuildSupportReportRequest>(requestJson);
        var response = outputSafetyApplicationService
            .BuildSupportReportAsync(request.Payload)
            .GetAwaiter()
            .GetResult();
        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchValidateProject(string requestJson)
    {
        var request = DeserializeRequest<ValidateProjectRequest>(requestJson);
        var validatedProject = projectWorkspaceService.ValidateAndOpen(
            ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var health = ReconcileOutputOnProjectActivation(
            validatedProject.Id.ToString(),
            request.Payload.Paths,
            ProjectBridgeMapper.ToDto(validatedProject.Health));
        var response = new ValidateProjectResponse(
            validatedProject.Id.ToString(),
            health);

        return SerializeSuccess(response, request.RequestId);
    }

    private ProjectHealthDto ReconcileOutputOnProjectActivation(
        string projectId,
        ProjectPathsDto paths,
        ProjectHealthDto health)
    {
        if (!health.CanOpenEditableWorkflows)
        {
            return health;
        }

        try
        {
            var scope = new OutputScopeDto(projectId, paths);
            var recovery = outputSafetyApplicationService
                .GetRecoveryStatusAsync(new GetOutputRecoveryStatusRequest(scope))
                .GetAwaiter()
                .GetResult()
                .Status;
            if (recovery.RequiresRecovery)
            {
                return health with
                {
                    Diagnostics = health.Diagnostics.Concat(recovery.Diagnostics).ToArray(),
                };
            }

            if (recovery.Transactions.Any(
                    transaction => transaction.Phase == OutputTransactionPhaseDto.RecoveryRequired))
            {
                return health with
                {
                    Diagnostics = health.Diagnostics.Concat(recovery.Diagnostics).ToArray(),
                };
            }

            var dispositions = recovery.Transactions
                .Select(transaction => transaction.Disposition)
                .ToArray();
            if (!dispositions.Any(disposition => disposition is
                    OutputRecoveryDispositionDto.FinalizeCommit or
                    OutputRecoveryDispositionDto.RollBack))
            {
                return health;
            }

            var reconciled = outputSafetyApplicationService
                .ReconcileRecoveryAsync(new ReconcileOutputRecoveryRequest(scope, recovery.Revision))
                .GetAwaiter()
                .GetResult();
            if (reconciled.Status.RequiresRecovery
                || reconciled.Status.PendingReconciliationCount > 0)
            {
                return health with
                {
                    Diagnostics = health.Diagnostics
                        .Concat(reconciled.Status.Diagnostics)
                        .ToArray(),
                };
            }

            var rolledBack = dispositions.Contains(OutputRecoveryDispositionDto.RollBack);
            var diagnostic = new ApiDiagnostic(
                rolledBack ? ApiDiagnosticSeverity.Warning : ApiDiagnosticSeverity.Info,
                rolledBack
                    ? "An interrupted output transaction was safely rolled back during project activation."
                    : "An interrupted output transaction was safely finalized during project activation.",
                Domain: "output.recovery")
            {
                Code = rolledBack
                    ? "KM-OUTPUT-RECOVERY-ROLLED-BACK"
                    : "KM-OUTPUT-RECOVERY-FINALIZED",
            };
            return health with
            {
                Diagnostics = health.Diagnostics.Append(diagnostic).ToArray(),
            };
        }
        catch (Exception exception) when (exception is
            OutputCoordinatorException or
            OutputScopeMismatchException or
            IOException or
            UnauthorizedAccessException)
        {
            var diagnostic = new ApiDiagnostic(
                ApiDiagnosticSeverity.Error,
                "Output recovery status could not be verified. Applying changes remains blocked until it can be checked.",
                Domain: "output.recovery")
            {
                Code = "KM-OUTPUT-RECOVERY-STATUS-UNAVAILABLE",
            };
            return health with
            {
                Diagnostics = health.Diagnostics.Append(diagnostic).ToArray(),
            };
        }
    }

    private string DispatchRefreshFileGraph(string requestJson)
    {
        var request = DeserializeRequest<RefreshFileGraphRequest>(requestJson);
        var fileGraph = projectWorkspaceService.RefreshFileGraph(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = new RefreshFileGraphResponse(ProjectBridgeMapper.ToDto(fileGraph));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchListWorkflows(string requestJson)
    {
        var request = DeserializeRequest<ListWorkflowsRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = paths.SelectedGame switch
        {
            ProjectGame.Scarlet or ProjectGame.Violet => SvBridgeMapper.ToDto(svWorkflowService.List(paths)),
            ProjectGame.ZA => ZaBridgeMapper.ToDto(zaWorkflowService.List(paths)),
            _ => SwShBridgeMapper.ToDto(swShWorkflowService.List(paths)),
        };

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadGameDumpWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadGameDumpWorkflowRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var workflow = IsPokemonLegendsZA(paths)
            ? zaGameDumpService.Load(paths)
            : IsScarletViolet(paths)
            ? svGameDumpService.Load(paths)
            : swShGameDumpService.Load(paths);
        var response = new LoadGameDumpWorkflowResponse(ProjectBridgeMapper.ToDto(workflow));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchRunGameDump(string requestJson)
    {
        var request = DeserializeRequest<RunGameDumpRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var selections = ProjectBridgeMapper.ToCore(request.Payload.Selections);
        var result = IsPokemonLegendsZA(paths)
            ? zaGameDumpService.Run(paths, request.Payload.DestinationFolder, selections, request.Payload.ProducerVersion)
            : IsScarletViolet(paths)
            ? svGameDumpService.Run(paths, request.Payload.DestinationFolder, selections, request.Payload.ProducerVersion)
            : swShGameDumpService.Run(paths, request.Payload.DestinationFolder, selections, request.Payload.ProducerVersion);
        var response = new RunGameDumpResponse(ProjectBridgeMapper.ToDto(result));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadItemsWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadItemsWorkflowRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = IsPokemonLegendsZA(paths)
            ? ZaBridgeMapper.ToDto(zaWorkflowService.LoadItems(paths))
            : IsScarletViolet(paths)
            ? SvBridgeMapper.ToDto(svWorkflowService.LoadItems(paths))
            : SwShBridgeMapper.ToDto(swShWorkflowService.LoadItems(paths));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadPokemonWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadPokemonWorkflowRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = IsPokemonLegendsZA(paths)
            ? ZaBridgeMapper.ToDto(zaWorkflowService.LoadPokemon(paths))
            : IsScarletViolet(paths)
            ? SvBridgeMapper.ToDto(svWorkflowService.LoadPokemon(paths))
            : SwShBridgeMapper.ToDto(swShWorkflowService.LoadPokemon(paths));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdatePokemonField(string requestJson)
    {
        var request = DeserializeRequest<UpdatePokemonFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = IsPokemonLegendsZA(paths)
            ? ZaBridgeMapper.ToDto(zaWorkflowService.UpdatePokemonField(
                paths,
                session,
                request.Payload.PersonalId,
                request.Payload.Field,
                request.Payload.Value))
            : IsScarletViolet(paths)
            ? SvBridgeMapper.ToDto(svWorkflowService.UpdatePokemonField(
                paths,
                session,
                request.Payload.PersonalId,
                request.Payload.Field,
                request.Payload.Value))
            : SwShBridgeMapper.ToDto(pokemonEditSessionService.UpdateField(
                paths,
                session,
                request.Payload.PersonalId,
                request.Payload.Field,
                request.Payload.Value));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdatePokemonFields(string requestJson)
    {
        var request = DeserializeRequest<UpdatePokemonFieldsRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        if (IsPokemonLegendsZA(paths))
        {
            var zaResponse = ZaBridgeMapper.ToPokemonFieldsDto(zaWorkflowService.UpdatePokemonFields(
                paths,
                session,
                request.Payload.Updates
                    .Select(update => new KM.ZA.Pokemon.ZaPokemonFieldUpdate(update.PersonalId, update.Field, update.Value))
                    .ToArray()));

            return SerializeSuccess(zaResponse, request.RequestId);
        }

        if (IsScarletViolet(paths))
        {
            var svResponse = SvBridgeMapper.ToPokemonFieldsDto(
                svWorkflowService.UpdatePokemonFields(
                    paths,
                    session,
                    request.Payload.Updates
                        .Select(update => new SvPokemonFieldUpdate(update.PersonalId, update.Field, update.Value))
                        .ToArray()));

            return SerializeSuccess(svResponse, request.RequestId);
        }

        var response = SwShBridgeMapper.ToPokemonFieldsDto(
            pokemonEditSessionService.UpdateFields(
                paths,
                session,
                request.Payload.Updates
                    .Select(update => new SwShPokemonFieldUpdate(update.PersonalId, update.Field, update.Value))
                    .ToArray()));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdatePokemonComposite(string requestJson)
    {
        var request = DeserializeRequest<UpdatePokemonCompositeRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);

        if (IsPokemonLegendsZA(paths))
        {
            var response = ZaBridgeMapper.ToPokemonCompositeDto(
                zaWorkflowService.UpdatePokemonComposite(
                    paths,
                    session,
                    request.Payload.FieldUpdates
                        .Select(update => new KM.ZA.Pokemon.ZaPokemonFieldUpdate(
                            update.PersonalId,
                            update.Field,
                            update.Value))
                        .ToArray(),
                    request.Payload.EvolutionUpdates
                        .Select(update => new KM.ZA.Pokemon.ZaPokemonEvolutionOperation(
                            update.PersonalId,
                            update.Action,
                            update.Slot,
                            update.Method,
                            update.Argument,
                            update.Species,
                            update.Form,
                            update.Level))
                        .ToArray(),
                    request.Payload.LearnsetUpdates
                        .Select(update => new KM.ZA.Pokemon.ZaPokemonLearnsetUpdate(
                            update.PersonalId,
                            update.Action,
                            update.Slot,
                            update.MoveId,
                            update.Level))
                        .ToArray()));

            return SerializeSuccess(response, request.RequestId);
        }

        if (IsScarletViolet(paths))
        {
            var response = SvBridgeMapper.ToPokemonCompositeDto(
                svWorkflowService.UpdatePokemonComposite(
                    paths,
                    session,
                    request.Payload.FieldUpdates
                        .Select(update => new SvPokemonFieldUpdate(
                            update.PersonalId,
                            update.Field,
                            update.Value))
                        .ToArray(),
                    request.Payload.EvolutionUpdates
                        .Select(update => new KM.SV.Pokemon.SvPokemonEvolutionUpdate(
                            update.PersonalId,
                            update.Action,
                            update.Slot,
                            update.Method,
                            update.Argument,
                            update.Species,
                            update.Form,
                            update.Level))
                        .ToArray(),
                    request.Payload.LearnsetUpdates
                        .Select(update => new KM.SV.Pokemon.SvPokemonLearnsetUpdate(
                            update.PersonalId,
                            update.Action,
                            update.Slot,
                            update.MoveId,
                            update.Level))
                        .ToArray()));

            return SerializeSuccess(response, request.RequestId);
        }

        var swShResponse = SwShBridgeMapper.ToPokemonCompositeDto(
            pokemonEditSessionService.UpdateComposite(
                paths,
                session,
                request.Payload.FieldUpdates
                    .Select(update => new SwShPokemonFieldUpdate(
                        update.PersonalId,
                        update.Field,
                        update.Value))
                    .ToArray(),
                request.Payload.EvolutionUpdates
                    .Select(update => new SwShPokemonEvolutionUpdate(
                        update.PersonalId,
                        update.Action,
                        update.Slot,
                        update.Method,
                        update.Argument,
                        update.Species,
                        update.Form,
                        update.Level))
                    .ToArray(),
                request.Payload.LearnsetUpdates
                    .Select(update => new SwShPokemonLearnsetUpdate(
                        update.PersonalId,
                        update.Action,
                        update.Slot,
                        update.MoveId,
                        update.Level))
                    .ToArray()));

        return SerializeSuccess(swShResponse, request.RequestId);
    }

    private string DispatchUpdatePokemonLearnset(string requestJson)
    {
        var request = DeserializeRequest<UpdatePokemonLearnsetRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = IsPokemonLegendsZA(paths)
            ? ZaBridgeMapper.ToDtoLearnsetUpdate(zaWorkflowService.UpdatePokemonLearnset(
                paths,
                session,
                request.Payload.PersonalId,
                request.Payload.Action,
                request.Payload.Slot,
                request.Payload.MoveId,
                request.Payload.Level))
            : IsScarletViolet(paths)
            ? SvBridgeMapper.ToDtoLearnsetUpdate(svWorkflowService.UpdatePokemonLearnset(
                paths,
                session,
                request.Payload.PersonalId,
                request.Payload.Action,
                request.Payload.Slot,
                request.Payload.MoveId,
                request.Payload.Level))
            : SwShBridgeMapper.ToDtoLearnsetUpdate(pokemonEditSessionService.UpdateLearnset(
                paths,
                session,
                request.Payload.PersonalId,
                request.Payload.Action,
                request.Payload.Slot,
                request.Payload.MoveId,
                request.Payload.Level));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdatePokemonEvolution(string requestJson)
    {
        var request = DeserializeRequest<UpdatePokemonEvolutionRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = IsPokemonLegendsZA(paths)
            ? ZaBridgeMapper.ToDtoEvolutionUpdate(zaWorkflowService.UpdatePokemonEvolution(
                paths,
                session,
                request.Payload.PersonalId,
                request.Payload.Action,
                request.Payload.Slot,
                request.Payload.Method,
                request.Payload.Argument,
                request.Payload.Species,
                request.Payload.Form,
                request.Payload.Level))
            : IsScarletViolet(paths)
            ? SvBridgeMapper.ToDtoEvolutionUpdate(svWorkflowService.UpdatePokemonEvolution(
                paths,
                session,
                request.Payload.PersonalId,
                request.Payload.Action,
                request.Payload.Slot,
                request.Payload.Method,
                request.Payload.Argument,
                request.Payload.Species,
                request.Payload.Form,
                request.Payload.Level))
            : SwShBridgeMapper.ToDtoEvolutionUpdate(pokemonEditSessionService.UpdateEvolution(
                paths,
                session,
                request.Payload.PersonalId,
                request.Payload.Action,
                request.Payload.Slot,
                request.Payload.Method,
                request.Payload.Argument,
                request.Payload.Species,
                request.Payload.Form,
                request.Payload.Level));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchSwapPokemonDexPlacement(string requestJson)
    {
        var request = DeserializeRequest<SwapPokemonDexPlacementRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = ZaBridgeMapper.ToDtoDexPlacementSwap(
            zaWorkflowService.SwapPokemonDexPlacement(
                paths,
                session,
                request.Payload.SourceSpeciesId,
                request.Payload.TargetSpeciesId));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchMovePokemonDexPlacement(string requestJson)
    {
        var request = DeserializeRequest<MovePokemonDexPlacementRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = ZaBridgeMapper.ToDtoDexPlacementMove(
            zaWorkflowService.MovePokemonDexPlacement(
                paths,
                session,
                request.Payload.SourceSpeciesId,
                request.Payload.DestinationDexKind,
                request.Payload.DestinationDisplayedNumber));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchResizePokemonDex(string requestJson)
    {
        var request = DeserializeRequest<ResizePokemonDexRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = ZaBridgeMapper.ToDtoDexResize(
            zaWorkflowService.ResizePokemonDex(
                paths,
                session,
                request.Payload.RegularCount));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStagePokemonDexVanilla(string requestJson)
    {
        var request = DeserializeRequest<StagePokemonDexVanillaRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = ZaBridgeMapper.ToDtoDexVanilla(
            zaWorkflowService.StagePokemonDexVanilla(
                paths,
                session));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStagePokemonDexMegaSync(string requestJson)
    {
        var request = DeserializeRequest<StagePokemonDexMegaSyncRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = ZaBridgeMapper.ToDtoDexMegaSync(
            zaWorkflowService.StagePokemonDexMegaSync(
                paths,
                session));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadMovesWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadMovesWorkflowRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = IsPokemonLegendsZA(paths)
            ? ZaBridgeMapper.ToDto(zaWorkflowService.LoadMoves(paths))
            : IsScarletViolet(paths)
            ? SvBridgeMapper.ToDto(svWorkflowService.LoadMoves(paths))
            : SwShBridgeMapper.ToDto(swShWorkflowService.LoadMoves(paths));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateMoveField(string requestJson)
    {
        var request = DeserializeRequest<UpdateMoveFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = IsPokemonLegendsZA(paths)
            ? ZaBridgeMapper.ToDto(zaWorkflowService.UpdateMoveField(
                paths,
                session,
                request.Payload.MoveId,
                request.Payload.Field,
                request.Payload.Value))
            : IsScarletViolet(paths)
            ? SvBridgeMapper.ToDto(svWorkflowService.UpdateMoveField(
                paths,
                session,
                request.Payload.MoveId,
                request.Payload.Field,
                request.Payload.Value))
            : SwShBridgeMapper.ToDto(movesEditSessionService.UpdateField(
                paths,
                session,
                request.Payload.MoveId,
                request.Payload.Field,
                request.Payload.Value));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateMoveFields(string requestJson)
    {
        var request = DeserializeRequest<UpdateMoveFieldsRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        if (IsPokemonLegendsZA(paths))
        {
            var updates = request.Payload.Updates
                .Select(update => new ZaMoveFieldUpdate(update.MoveId, update.Field, update.Value))
                .ToArray();
            var zaResponse = ZaBridgeMapper.ToMoveFieldsDto(
                zaWorkflowService.UpdateMoveFields(paths, session, updates));

            return SerializeSuccess(zaResponse, request.RequestId);
        }

        if (IsScarletViolet(paths))
        {
            var svUpdates = request.Payload.Updates
                .Select(update => new SvMoveFieldUpdate(update.MoveId, update.Field, update.Value))
                .ToArray();
            var svResponse = SvBridgeMapper.ToMoveFieldsDto(
                svWorkflowService.UpdateMoveFields(paths, session, svUpdates));

            return SerializeSuccess(svResponse, request.RequestId);
        }

        var swShUpdates = request.Payload.Updates
            .Select(update => new SwShMoveFieldUpdate(update.MoveId, update.Field, update.Value))
            .ToArray();
        var response = SwShBridgeMapper.ToMoveFieldsDto(
            movesEditSessionService.UpdateFields(paths, session, swShUpdates));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageMoveVanilla(string requestJson)
    {
        var request = DeserializeRequest<StageMoveVanillaRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var response = ZaBridgeMapper.ToMoveVanillaDto(
            zaWorkflowService.StageMoveVanilla(
                ProjectBridgeMapper.ToCore(request.Payload.Paths),
                session,
                request.Payload.MoveId));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadTextWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadTextWorkflowRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = IsPokemonLegendsZA(paths)
            ? ZaBridgeMapper.ToDto(zaWorkflowService.LoadText(paths, ToZaTextWorkflowQuery(request.Payload.Query)))
            : IsScarletViolet(paths)
            ? SvBridgeMapper.ToDto(svWorkflowService.LoadText(paths, ToSvTextWorkflowQuery(request.Payload.Query)))
            : SwShBridgeMapper.ToDto(swShWorkflowService.LoadText(
                paths,
                ToSwShTextWorkflowQuery(request.Payload.Query)));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateTextEntry(string requestJson)
    {
        var request = DeserializeRequest<UpdateTextEntryRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = IsPokemonLegendsZA(paths)
            ? ZaBridgeMapper.ToDto(zaWorkflowService.UpdateTextEntry(
                paths,
                session,
                request.Payload.TextKey,
                request.Payload.Value,
                ToZaTextWorkflowQuery(request.Payload.Query)))
            : IsScarletViolet(paths)
            ? SvBridgeMapper.ToDto(svWorkflowService.UpdateTextEntry(
                paths,
                session,
                request.Payload.TextKey,
                request.Payload.Value,
                ToSvTextWorkflowQuery(request.Payload.Query)))
            : SwShBridgeMapper.ToDto(textEditSessionService.UpdateEntry(
                paths,
                session,
                request.Payload.TextKey,
                request.Payload.Value,
                ToSwShTextWorkflowQuery(request.Payload.Query)));

        return SerializeSuccess(response, request.RequestId);
    }

    private static SvTextWorkflowQuery? ToSvTextWorkflowQuery(TextWorkflowQueryDto? query)
    {
        return query is null
            ? null
            : new SvTextWorkflowQuery(
                query.SearchText,
                query.Offset ?? 0,
                query.Limit ?? SvTextWorkflowService.DefaultQueryLimit,
                query.CategoryId,
                query.Language);
    }

    private static ZaTextWorkflowQuery? ToZaTextWorkflowQuery(TextWorkflowQueryDto? query)
    {
        return query is null
            ? null
            : new ZaTextWorkflowQuery(
                query.SearchText,
                query.Offset ?? 0,
                query.Limit ?? ZaTextWorkflowService.DefaultQueryLimit,
                query.CategoryId,
                query.Language);
    }

    private static SwShTextWorkflowQuery? ToSwShTextWorkflowQuery(TextWorkflowQueryDto? query)
    {
        return query is null
            ? null
            : new SwShTextWorkflowQuery(
                query.SearchText,
                query.Offset ?? 0,
                query.Limit ?? SwShTextWorkflowService.DefaultQueryLimit,
                query.CategoryId,
                query.Language);
    }

    private string DispatchLoadTrainersWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadTrainersWorkflowRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = IsPokemonLegendsZA(paths)
            ? ZaBridgeMapper.ToDto(zaWorkflowService.LoadTrainers(paths))
            : IsScarletViolet(paths)
            ? SvBridgeMapper.ToDto(svWorkflowService.LoadTrainers(paths))
            : SwShBridgeMapper.ToDto(swShWorkflowService.LoadTrainers(paths));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateTrainerField(string requestJson)
    {
        var request = DeserializeRequest<UpdateTrainerFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = IsPokemonLegendsZA(paths)
            ? ZaBridgeMapper.ToDto(
                zaWorkflowService.UpdateTrainerField(
                    paths,
                    session,
                    request.Payload.TrainerId,
                    request.Payload.Slot,
                    request.Payload.Field,
                    request.Payload.Value),
                [request.Payload.TrainerId])
            : IsScarletViolet(paths)
            ? SvBridgeMapper.ToDto(
                svWorkflowService.UpdateTrainerField(
                    paths,
                    session,
                    request.Payload.TrainerId,
                    request.Payload.Slot,
                    request.Payload.Field,
                    request.Payload.Value),
                [request.Payload.TrainerId])
            : SwShBridgeMapper.ToDto(
                trainersEditSessionService.UpdateField(
                    paths,
                    session,
                    request.Payload.TrainerId,
                    request.Payload.Slot,
                    request.Payload.Field,
                    request.Payload.Value),
                request.Payload.TrainerId,
                request.Payload.Field);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateTrainerFields(string requestJson)
    {
        var request = DeserializeRequest<UpdateTrainerFieldsRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        if (IsPokemonLegendsZA(paths))
        {
            var zaUpdates = request.Payload.Updates
                .Select(update => new KM.ZA.Trainers.ZaTrainerFieldUpdate(update.TrainerId, update.Slot, update.Field, update.Value))
                .ToArray();
            var zaResponse = ZaBridgeMapper.ToTrainerFieldsDto(
                zaWorkflowService.UpdateTrainerFields(paths, session, zaUpdates),
                zaUpdates.Select(update => update.TrainerId));

            return SerializeSuccess(zaResponse, request.RequestId);
        }

        if (!IsScarletViolet(paths))
        {
            var swShUpdates = request.Payload.Updates
                .Select(update => new SwShTrainerFieldUpdate(
                    update.TrainerId,
                    update.Slot,
                    update.Field,
                    update.Value))
                .ToArray();
            var swShResponse = SwShBridgeMapper.ToTrainerFieldsDto(
                trainersEditSessionService.UpdateFields(paths, session, swShUpdates),
                swShUpdates);

            return SerializeSuccess(swShResponse, request.RequestId);
        }

        var updates = request.Payload.Updates
            .Select(update => new SvTrainerFieldUpdate(update.TrainerId, update.Slot, update.Field, update.Value))
            .ToArray();
        var response = SvBridgeMapper.ToTrainerFieldsDto(
            svWorkflowService.UpdateTrainerFields(paths, session, updates),
            updates.Select(update => update.TrainerId));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadShopsWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadShopsWorkflowRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        LoadShopsWorkflowResponse response;
        if (IsPokemonLegendsZA(paths))
        {
            response = ZaBridgeMapper.ToDto(zaWorkflowService.LoadShops(paths));
        }
        else if (IsScarletViolet(paths))
        {
            response = SvBridgeMapper.ToDto(svWorkflowService.LoadShops(paths));
        }
        else
        {
            response = SwShBridgeMapper.ToDto(swShWorkflowService.LoadShops(paths));
        }

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadGiftPokemonWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadGiftPokemonWorkflowRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        object response = IsPokemonLegendsZA(paths)
            ? ZaBridgeMapper.ToDto(zaWorkflowService.LoadGiftPokemon(paths))
            : IsScarletViolet(paths)
            ? SvBridgeMapper.ToDto(svWorkflowService.LoadGiftPokemon(paths))
            : SwShBridgeMapper.ToDto(swShWorkflowService.LoadGiftPokemon(paths));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateGiftPokemonField(string requestJson)
    {
        var request = DeserializeRequest<UpdateGiftPokemonFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        object response = IsPokemonLegendsZA(paths)
            ? ZaBridgeMapper.ToDto(zaWorkflowService.UpdateGiftPokemonField(
                paths,
                session,
                request.Payload.GiftIndex,
                request.Payload.Field,
                request.Payload.Value))
            : IsScarletViolet(paths)
            ? SvBridgeMapper.ToDto(svWorkflowService.UpdateGiftPokemonField(
                paths,
                session,
                request.Payload.GiftIndex,
                request.Payload.Field,
                request.Payload.Value))
            : SwShBridgeMapper.ToDto(giftPokemonEditSessionService.UpdateField(
                paths,
                session,
                request.Payload.GiftIndex,
                request.Payload.Field,
                request.Payload.Value));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateGiftPokemonFields(string requestJson)
    {
        var request = DeserializeRequest<UpdateGiftPokemonFieldsRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        object response;
        if (IsPokemonLegendsZA(paths))
        {
            var updates = request.Payload.Updates
                .Select(update => new ZaGiftPokemonFieldUpdate(update.GiftIndex, update.Field, update.Value))
                .ToArray();
            response = ZaBridgeMapper.ToGiftPokemonFieldsDto(
                zaWorkflowService.UpdateGiftPokemonFields(paths, session, updates));
        }
        else if (IsScarletViolet(paths))
        {
            var updates = request.Payload.Updates
                .Select(update => new SvGiftPokemonFieldUpdate(update.GiftIndex, update.Field, update.Value))
                .ToArray();
            response = SvBridgeMapper.ToGiftPokemonFieldsDto(
                svWorkflowService.UpdateGiftPokemonFields(paths, session, updates));
        }
        else
        {
            var updates = request.Payload.Updates
                .Select(update => new SwShGiftPokemonFieldUpdate(update.GiftIndex, update.Field, update.Value))
                .ToArray();
            response = SwShBridgeMapper.ToGiftPokemonFieldsDto(
                giftPokemonEditSessionService.UpdateFields(paths, session, updates));
        }

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageGiftPokemonVanilla(string requestJson)
    {
        var request = DeserializeRequest<StageGiftPokemonVanillaRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var response = ZaBridgeMapper.ToGiftPokemonVanillaDto(
            zaWorkflowService.StageGiftPokemonVanilla(
                ProjectBridgeMapper.ToCore(request.Payload.Paths),
                session,
                request.Payload.GiftIndex));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadTradePokemonWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadTradePokemonWorkflowRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        object response = IsPokemonLegendsZA(paths)
            ? ZaBridgeMapper.ToDto(zaWorkflowService.LoadTradePokemon(paths))
            : IsScarletViolet(paths)
            ? SvBridgeMapper.ToDto(svWorkflowService.LoadTradePokemon(paths))
            : SwShBridgeMapper.ToDto(swShWorkflowService.LoadTradePokemon(paths));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateTradePokemonField(string requestJson)
    {
        var request = DeserializeRequest<UpdateTradePokemonFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        object response = IsPokemonLegendsZA(paths)
            ? ZaBridgeMapper.ToDto(zaWorkflowService.UpdateTradePokemonField(
                paths,
                session,
                request.Payload.TradeIndex,
                request.Payload.Field,
                request.Payload.Value))
            : IsScarletViolet(paths)
            ? SvBridgeMapper.ToDto(svWorkflowService.UpdateTradePokemonField(
                paths,
                session,
                request.Payload.TradeIndex,
                request.Payload.Field,
                request.Payload.Value))
            : SwShBridgeMapper.ToDto(tradePokemonEditSessionService.UpdateField(
                paths,
                session,
                request.Payload.TradeIndex,
                request.Payload.Field,
                request.Payload.Value));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateTradePokemonFields(string requestJson)
    {
        var request = DeserializeRequest<UpdateTradePokemonFieldsRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        object response;
        if (IsPokemonLegendsZA(paths))
        {
            var updates = request.Payload.Updates
                .Select(update => new ZaTradePokemonFieldUpdate(update.TradeIndex, update.Field, update.Value))
                .ToArray();
            response = ZaBridgeMapper.ToTradePokemonFieldsDto(
                zaWorkflowService.UpdateTradePokemonFields(paths, session, updates));
        }
        else
        {
            if (IsScarletViolet(paths))
            {
                var updates = request.Payload.Updates
                    .Select(update => new SvTradePokemonFieldUpdate(update.TradeIndex, update.Field, update.Value))
                    .ToArray();
                response = SvBridgeMapper.ToTradePokemonFieldsDto(
                    svWorkflowService.UpdateTradePokemonFields(paths, session, updates));
            }
            else
            {
                var updates = request.Payload.Updates
                    .Select(update => new SwShTradePokemonFieldUpdate(update.TradeIndex, update.Field, update.Value))
                    .ToArray();
                response = SwShBridgeMapper.ToTradePokemonFieldsDto(
                    tradePokemonEditSessionService.UpdateFields(paths, session, updates));
            }
        }

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadStaticEncountersWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadStaticEncountersWorkflowRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        object response = IsPokemonLegendsZA(paths)
            ? ZaBridgeMapper.ToDto(zaWorkflowService.LoadStaticEncounters(paths))
            : IsScarletViolet(paths)
                ? SvBridgeMapper.ToDto(svWorkflowService.LoadStaticEncounters(paths))
                : SwShBridgeMapper.ToDto(swShWorkflowService.LoadStaticEncounters(paths));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateStaticEncounterField(string requestJson)
    {
        var request = DeserializeRequest<UpdateStaticEncounterFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        object response = IsPokemonLegendsZA(paths)
            ? ZaBridgeMapper.ToDto(zaWorkflowService.UpdateStaticEncounterField(
                paths,
                session,
                request.Payload.EncounterIndex,
                request.Payload.Field,
                request.Payload.Value))
            : IsScarletViolet(paths)
                ? SvBridgeMapper.ToDto(svWorkflowService.UpdateStaticEncounterField(
                paths,
                session,
                request.Payload.EncounterIndex,
                request.Payload.Field,
                request.Payload.Value,
                request.Payload.EncounterId))
                : SwShBridgeMapper.ToDto(staticEncountersEditSessionService.UpdateFields(
                    paths,
                    session,
                    [new SwShStaticEncounterFieldUpdate(
                        request.Payload.EncounterIndex,
                        request.Payload.Field,
                        request.Payload.Value,
                        request.Payload.EncounterId)]));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateStaticEncounterFields(string requestJson)
    {
        var request = DeserializeRequest<UpdateStaticEncounterFieldsRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        object response;
        if (IsPokemonLegendsZA(paths))
        {
            var updates = request.Payload.Updates
                .Select(update => new ZaStaticEncounterFieldUpdate(
                    update.EncounterIndex,
                    update.Field,
                    update.Value))
                .ToArray();
            response = ZaBridgeMapper.ToDto(
                zaWorkflowService.UpdateStaticEncounterFields(paths, session, updates));
        }
        else if (IsScarletViolet(paths))
        {
            var updates = request.Payload.Updates
                .Select(update => new KM.SV.StaticEncounters.SvStaticEncounterFieldUpdate(
                    update.EncounterIndex,
                    update.Field,
                    update.Value,
                    update.EncounterId))
                .ToArray();
            response = SvBridgeMapper.ToDto(
                svWorkflowService.UpdateStaticEncounterFields(paths, session, updates));
        }
        else
        {
            var updates = request.Payload.Updates
                .Select(update => new SwShStaticEncounterFieldUpdate(
                    update.EncounterIndex,
                    update.Field,
                    update.Value,
                    update.EncounterId))
                .ToArray();
            response = SwShBridgeMapper.ToDto(
                staticEncountersEditSessionService.UpdateFields(paths, session, updates));
        }

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadRentalPokemonWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadRentalPokemonWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadRentalPokemon(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateRentalPokemonField(string requestJson)
    {
        var request = DeserializeRequest<UpdateRentalPokemonFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = rentalPokemonEditSessionService.UpdateField(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session,
            request.Payload.RentalIndex,
            request.Payload.Field,
            request.Payload.Value);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateRentalPokemonFields(string requestJson)
    {
        var request = DeserializeRequest<UpdateRentalPokemonFieldsRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var updates = request.Payload.Updates?
            .Select(update => update is null
                ? null
                : new SwShRentalPokemonFieldUpdate(
                    update.RentalIndex,
                    update.Field,
                    update.Value))
            .ToArray();
        var result = rentalPokemonEditSessionService.UpdateFields(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session,
            updates);
        var response = SwShBridgeMapper.ToRentalPokemonFieldsDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadDynamaxAdventuresWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadDynamaxAdventuresWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadDynamaxAdventures(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateDynamaxAdventureField(string requestJson)
    {
        var request = DeserializeRequest<UpdateDynamaxAdventureFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCoreAllowingMalformedPendingEdits(request.Payload.Session);
        var result = dynamaxAdventuresEditSessionService.UpdateField(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session,
            request.Payload.EntryIndex,
            request.Payload.Field,
            request.Payload.Value);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateDynamaxAdventureFields(string requestJson)
    {
        var request = DeserializeRequest<UpdateDynamaxAdventureFieldsRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCoreAllowingMalformedPendingEdits(request.Payload.Session);
        var updates = request.Payload.Updates?
            .Select(update => update is null
                ? null!
                : new SwShDynamaxAdventureFieldUpdate(
                    update.EntryIndex,
                    update.Field,
                    update.Value))
            .ToArray() ?? [];
        var result = dynamaxAdventuresEditSessionService.UpdateFields(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session,
            updates);
        var response = SwShBridgeMapper.ToDynamaxAdventureFieldsDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageDynamaxAdventureRepair(string requestJson)
    {
        var request = DeserializeRequest<StageDynamaxAdventureRepairRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCoreAllowingMalformedPendingEdits(request.Payload.Session);
        var result = dynamaxAdventuresEditSessionService.StageRepair(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageDynamaxAdventureRestore(string requestJson)
    {
        var request = DeserializeRequest<StageDynamaxAdventureRestoreRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCoreAllowingMalformedPendingEdits(request.Payload.Session);
        var result = dynamaxAdventuresEditSessionService.StageVanillaTableRestore(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchPreviewDynamaxAdventureDefaults(string requestJson)
    {
        var request = DeserializeRequest<PreviewDynamaxAdventureDefaultsRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCoreAllowingMalformedPendingEdits(request.Payload.Session);
        var preview = dynamaxAdventuresEditSessionService.PreviewDefaults(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session,
            request.Payload.EntryIndex,
            request.Payload.Species,
            request.Payload.Form,
            request.Payload.Level);
        var response = SwShBridgeMapper.ToDto(preview);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchPlanDynamaxAdventureSeed(string requestJson)
    {
        var request = DeserializeRequest<PlanDynamaxAdventureSeedRequest>(requestJson);
        if (!TryParseSeed(request.Payload.Seed, out var seed))
        {
            return SerializeFailure(
                DynamaxAdventuresErrorCodes.SeedInvalid,
                $"Dynamax Adventures seed '{request.Payload.Seed}' is not a valid 64-bit seed.",
                request.RequestId);
        }

        var result = dynamaxAdventureSeedPlanningService.Predict(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            seed,
            request.Payload.NpcCount,
            request.Payload.RequiredRows);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchSearchDynamaxAdventureSeed(string requestJson)
    {
        var request = DeserializeRequest<SearchDynamaxAdventureSeedRequest>(requestJson);
        if (!TryParseSeed(request.Payload.StartSeed, out var startSeed))
        {
            return SerializeFailure(
                DynamaxAdventuresErrorCodes.StartSeedInvalid,
                $"Dynamax Adventures start seed '{request.Payload.StartSeed}' is not a valid 64-bit seed.",
                request.RequestId);
        }

        if (!TryParseSeed(request.Payload.Limit, out var limit))
        {
            return SerializeFailure(
                DynamaxAdventuresErrorCodes.SeedLimitInvalid,
                $"Dynamax Adventures seed search limit '{request.Payload.Limit}' is not a valid 64-bit value.",
                request.RequestId);
        }

        var result = dynamaxAdventureSeedPlanningService.SearchRows(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            request.Payload.RequiredRows,
            request.Payload.NpcCount,
            startSeed,
            limit,
            request.Payload.MaxResults);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchSetDynamaxAdventureSaveSeed(string requestJson)
    {
        var request = DeserializeRequest<SetDynamaxAdventureSaveSeedRequest>(requestJson);
        if (!TryParseSeed(request.Payload.Seed, out var seed))
        {
            return SerializeFailure(
                DynamaxAdventuresErrorCodes.SeedInvalid,
                $"Dynamax Adventures seed '{request.Payload.Seed}' is not a valid 64-bit seed.",
                request.RequestId);
        }

        var result = dynamaxAdventureSaveSeedService.SetSeed(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            seed);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateShopInventoryItem(string requestJson)
    {
        var request = DeserializeRequest<UpdateShopInventoryItemRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        UpdateShopInventoryItemResponse response;
        if (IsPokemonLegendsZA(paths))
        {
            response = ZaBridgeMapper.ToDto(zaWorkflowService.UpdateShopInventoryItem(
                paths,
                session,
                request.Payload.ShopId,
                request.Payload.Slot,
                request.Payload.Field,
                request.Payload.Value,
                request.Payload.RowId));
        }
        else if (IsScarletViolet(paths))
        {
            response = SvBridgeMapper.ToDto(svWorkflowService.UpdateShopInventoryItem(
                paths,
                session,
                request.Payload.ShopId,
                request.Payload.Slot,
                request.Payload.Field,
                request.Payload.Value,
                request.Payload.RowId));
        }
        else
        {
            response = SwShBridgeMapper.ToDto(shopsEditSessionService.UpdateInventoryItem(
                paths,
                session,
                request.Payload.ShopId,
                request.Payload.Slot,
                request.Payload.Field,
                request.Payload.Value));
        }

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateShopInventoryItems(string requestJson)
    {
        var request = DeserializeRequest<UpdateShopInventoryItemsRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        UpdateShopInventoryItemsResponse response;
        if (IsPokemonLegendsZA(paths))
        {
            var updates = request.Payload.Updates?
                .Select(update => update is null
                    ? null
                    : new ZaShopInventoryItemUpdate(
                        update.ShopId,
                        update.Slot,
                        update.Field,
                        update.Value,
                        update.RowId))
                .ToArray();
            response = ZaBridgeMapper.ToShopInventoryItemsDto(
                zaWorkflowService.UpdateShopInventoryItems(paths, session, updates));
        }
        else if (IsScarletViolet(paths))
        {
            var updates = request.Payload.Updates?
                .Select(update => update is null
                    ? null
                    : new SvShopInventoryItemUpdate(
                        update.ShopId,
                        update.Slot,
                        update.Field,
                        update.Value,
                        update.RowId))
                .ToArray();
            response = SvBridgeMapper.ToShopInventoryItemsDto(
                svWorkflowService.UpdateShopInventoryItems(paths, session, updates));
        }
        else
        {
            var updates = request.Payload.Updates?
                .Select(update => update is null
                    ? null
                    : new SwShShopInventoryItemUpdate(
                        update.ShopId,
                        update.Slot,
                        update.Field,
                        update.Value,
                        update.RowId))
                .ToArray();
            response = SwShBridgeMapper.ToShopInventoryItemsDto(
                shopsEditSessionService.UpdateInventoryItems(paths, session, updates));
        }

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchReadProjectSourceRevision(string requestJson)
    {
        var request = DeserializeRequest<ReadProjectSourceRevisionRequest>(requestJson);
        if (request.Payload.Paths is null
            || string.IsNullOrWhiteSpace(request.Payload.ProjectId)
            || request.Payload.Paths.SelectedGame is null)
        {
            throw new SemanticExploreValidationException(
                "The project source revision request is malformed.",
                SemanticExploreFailureKind.InvalidData);
        }

        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var actualProjectId = ProjectIdentity.FromPaths(paths).ToString();
        if (!string.Equals(actualProjectId, request.Payload.ProjectId, StringComparison.Ordinal))
        {
            throw new SemanticExploreValidationException(
                "The project source revision request does not match the selected project.",
                SemanticExploreFailureKind.StaleRevision);
        }

        (string Fingerprint, string Token) CaptureObservation()
        {
            var initialFingerprint = CaptureSemanticExploreSourceFingerprint(paths);
            var observedFingerprint = CaptureSemanticExploreSourceFingerprint(paths);
            if (!string.Equals(initialFingerprint, observedFingerprint, StringComparison.Ordinal))
            {
                throw new SemanticExploreValidationException(
                    "The project sources changed while their revision was being read. Retry the request.",
                    SemanticExploreFailureKind.StaleRevision);
            }

            var sourceObservationToken = semanticExploreApplicationService
                .RegisterVerifiedSourceObservation(
                    actualProjectId,
                    request.Payload.Paths,
                    observedFingerprint);
            return (Fingerprint: observedFingerprint, Token: sourceObservationToken);
        }

        var completedObservation = ShouldProtectSourceObservationWithOutputSafetyLock(
                actualProjectId,
                request.Payload.Paths)
            ? ExecuteExclusiveOutputOperation(request.Payload.Paths, CaptureObservation)
            : CaptureObservation();

        return SerializeSuccess(
            new ReadProjectSourceRevisionResponse(
                actualProjectId,
                request.Payload.Paths.SelectedGame.Value,
                completedObservation.Fingerprint,
                completedObservation.Token),
            request.RequestId);
    }

    private static bool ShouldProtectSourceObservationWithOutputSafetyLock(
        string projectId,
        ProjectPathsDto paths)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            return false;
        }

        if (!Path.IsPathFullyQualified(paths.OutputRootPath))
        {
            return true;
        }

        try
        {
            _ = Path.GetFullPath(paths.OutputRootPath);
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            NotSupportedException or
            PathTooLongException or
            System.Security.SecurityException)
        {
            // A malformed configured path is not an optional missing directory. Keep it on
            // the fail-closed boundary so its normal output-scope rejection remains visible.
            return true;
        }

        // Output Root is optional for read-only project analysis. The durable output
        // coordinator deliberately accepts only a currently safe output project scope.
        // Missing, unsafe, overlapping, and otherwise read-only output roots must not turn
        // an independent source read into a rejected output operation. A verified existing
        // scope still uses the exclusive coordinator and therefore remains fail closed.
        try
        {
            _ = OutputSafetyApplicationService.ResolveScope(
                new OutputScopeDto(projectId, paths));
            return true;
        }
        catch (OutputScopeMismatchException)
        {
            // The source fingerprint is still captured twice and each game workflow retains
            // its process-wide output mutex, so source changes remain observable without
            // claiming that this invalid output scope is safe for writes.
            return false;
        }
    }

    private string DispatchLoadTrainerPoolsWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadTrainerPoolsWorkflowRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        if (!IsPokemonLegendsZA(paths))
        {
            return SerializeFailure(
                BridgeErrorCodes.GameMismatch,
                "Trainer Pools are only available for Pokemon Legends Z-A projects.",
                request.RequestId);
        }

        return SerializeSuccess(
            ZaBridgeMapper.ToDto(zaWorkflowService.LoadTrainerPools(paths)),
            request.RequestId);
    }

    private string DispatchStageTrainerPoolFixedCountSwap(string requestJson)
    {
        var request = DeserializeRequest<StageTrainerPoolFixedCountSwapRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        if (!IsPokemonLegendsZA(paths))
        {
            return SerializeFailure(
                BridgeErrorCodes.GameMismatch,
                "Trainer Pools fixed-count swaps are only available for Pokemon Legends Z-A projects.",
                request.RequestId);
        }

        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var operation = new KM.ZA.TrainerPools.ZaTrainerPoolFixedCountSwap(
            request.Payload.SourceLogicalPoolId,
            request.Payload.SourceRawTrainerId,
            request.Payload.DestinationLogicalPoolId,
            request.Payload.DestinationRawTrainerId);
        var response = ZaBridgeMapper.ToDto(
            zaWorkflowService.StageTrainerPoolFixedCountSwap(paths, session, operation));
        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadFashionCatalogWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadFashionCatalogWorkflowRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        if (!IsPokemonLegendsZA(paths))
        {
            return SerializeFailure(
                BridgeErrorCodes.GameMismatch,
                "Fashion Catalog is only available for Pokemon Legends Z-A projects.",
                request.RequestId);
        }

        return SerializeSuccess(
            ZaFashionCatalogBridgeMapper.ToDto(zaWorkflowService.LoadFashionCatalog(paths)),
            request.RequestId);
    }

    private string DispatchStageFashionCatalogFieldEdit(string requestJson)
    {
        var request = DeserializeRequest<StageFashionCatalogFieldEditRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        if (!IsPokemonLegendsZA(paths))
        {
            return SerializeFailure(
                BridgeErrorCodes.GameMismatch,
                "Fashion Catalog edits are only available for Pokemon Legends Z-A projects.",
                request.RequestId);
        }

        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var operation = ZaFashionCatalogBridgeMapper.ToCore(
            request.Payload.CatalogFile,
            request.Payload.Binding,
            request.Payload.Field,
            request.Payload.Value,
            request.Payload.Clear);
        return SerializeSuccess(
            ZaFashionCatalogBridgeMapper.ToDto(
                zaWorkflowService.StageFashionCatalogFieldEdit(paths, session, operation)),
            request.RequestId);
    }

    private string DispatchLoadTmMachineControls(string requestJson)
    {
        var request = DeserializeRequest<LoadTmMachineControlsRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        if (!IsScarletViolet(paths))
        {
            return SerializeFailure(
                BridgeErrorCodes.GameMismatch,
                "TM Machine Controls are only available for Pokemon Scarlet and Pokemon Violet projects.",
                request.RequestId);
        }

        var response = SvBridgeMapper.ToDto(
            svWorkflowService.LoadTmMachineControls(paths));
        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageTmRecipeAvailability(string requestJson)
    {
        var request = DeserializeRequest<StageTmRecipeAvailabilityRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        if (!IsScarletViolet(paths))
        {
            return SerializeFailure(
                BridgeErrorCodes.GameMismatch,
                "TM recipe availability is only available for Pokemon Scarlet and Pokemon Violet projects.",
                request.RequestId);
        }

        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var response = SvBridgeMapper.ToTmRecipeAvailabilityDto(
            svWorkflowService.StageTmRecipeAvailability(
                paths,
                session,
                request.Payload.AllAvailable));
        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageTmMaterialVisibility(string requestJson)
    {
        var request = DeserializeRequest<StageTmMaterialVisibilityRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        if (!IsScarletViolet(paths))
        {
            return SerializeFailure(
                BridgeErrorCodes.GameMismatch,
                "TM material visibility is only available for Pokemon Scarlet and Pokemon Violet projects.",
                request.RequestId);
        }

        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var response = SvBridgeMapper.ToTmMaterialVisibilityDto(
            svWorkflowService.StageTmMaterialVisibility(
                paths,
                session,
                request.Payload.AlwaysVisible));
        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadHabitatCoordinates(string requestJson)
    {
        var request = DeserializeRequest<LoadHabitatCoordinatesRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        if (!IsScarletViolet(paths))
        {
            return SerializeFailure(
                BridgeErrorCodes.GameMismatch,
                "Habitat Coordinates are only available for Pokemon Scarlet and Pokemon Violet projects.",
                request.RequestId);
        }

        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var response = SvHabitatCoordinatesBridgeMapper.ToDto(
            svWorkflowService.LoadHabitatCoordinates(
                paths,
                SvHabitatCoordinatesBridgeMapper.ToCore(request.Payload.Query),
                session));
        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageHabitatCoordinate(string requestJson)
    {
        var request = DeserializeRequest<StageHabitatCoordinateRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        if (!IsScarletViolet(paths))
        {
            return SerializeFailure(
                BridgeErrorCodes.GameMismatch,
                "Habitat Coordinates are only available for Pokemon Scarlet and Pokemon Violet projects.",
                request.RequestId);
        }

        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var response = SvHabitatCoordinatesBridgeMapper.ToDto(
            svWorkflowService.StageHabitatCoordinate(
                paths,
                session,
                SvHabitatCoordinatesBridgeMapper.ToCore(request.Payload.Query),
                request.Payload.Region,
                SvHabitatCoordinatesBridgeMapper.ToCore(request.Payload.Binding),
                SvHabitatCoordinatesBridgeMapper.ToCore(request.Payload.Coordinate)));
        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchPrepareRowClipboardCopy(string requestJson)
    {
        var request = DeserializeRequest<PrepareRowClipboardCopyRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        return SerializeSuccess(
            RowClipboardBridgeMapper.ToDto(
                rowClipboardApplicationService.PrepareCopy(paths, session)),
            request.RequestId);
    }

    private string DispatchPreviewRowClipboardPaste(string requestJson)
    {
        var request = DeserializeRequest<PreviewRowClipboardPasteRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        try
        {
            var envelope = RowClipboardBridgeMapper.ToCore(request.Payload.Envelope);
            var mode = RowClipboardBridgeMapper.ToCorePasteMode(request.Payload.Mode);
            var target = RowClipboardBridgeMapper.ToCore(request.Payload.Target);
            return SerializeSuccess(
                RowClipboardBridgeMapper.ToDto(
                    rowClipboardApplicationService.Preview(paths, session, envelope, mode, target)),
                request.RequestId);
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidOperationException or
            OverflowException)
        {
            return SerializeSuccess(
                RowClipboardBridgeMapper.InvalidPreviewEnvelope(
                    "The pasted logical-row clipboard content is invalid or incompatible."),
                request.RequestId);
        }
    }

    private string DispatchStageRowClipboardPaste(string requestJson)
    {
        var request = DeserializeRequest<StageRowClipboardPasteRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        try
        {
            var envelope = RowClipboardBridgeMapper.ToCore(request.Payload.Envelope);
            var mode = RowClipboardBridgeMapper.ToCorePasteMode(request.Payload.Mode);
            var target = RowClipboardBridgeMapper.ToCore(request.Payload.Target);
            return SerializeSuccess(
                RowClipboardBridgeMapper.ToDto(
                    rowClipboardApplicationService.Stage(
                        paths,
                        session,
                        envelope,
                        mode,
                        target,
                        request.Payload.AuthorizationId,
                        request.Payload.ExpectedTargetRevision)),
                request.RequestId);
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidOperationException or
            OverflowException)
        {
            return SerializeSuccess(
                RowClipboardBridgeMapper.InvalidStageEnvelope(
                    request.Payload.Session,
                    "The pasted logical-row clipboard content is invalid or incompatible."),
                request.RequestId);
        }
    }

    private string DispatchClearRowClipboardAuthorizations(string requestJson)
    {
        var request = DeserializeRequest<ClearRowClipboardAuthorizationsRequest>(requestJson);
        var paths = request.Payload.Paths is null
            ? null
            : ProjectBridgeMapper.ToCore(request.Payload.Paths);
        return SerializeSuccess(
            new ClearRowClipboardAuthorizationsResponse(
                rowClipboardApplicationService.Clear(paths)),
            request.RequestId);
    }

    private string DispatchLoadEncountersWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadEncountersWorkflowRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        object response = IsPokemonLegendsZA(paths)
            ? ZaBridgeMapper.ToDto(zaWorkflowService.LoadEncounters(paths))
            : IsScarletViolet(paths)
                ? SvBridgeMapper.ToDto(svWorkflowService.LoadEncounters(paths))
                : SwShBridgeMapper.ToDto(swShWorkflowService.LoadEncounters(paths));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateEncounterSlotField(string requestJson)
    {
        var request = DeserializeRequest<UpdateEncounterSlotFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        object response = IsPokemonLegendsZA(paths)
            ? ZaBridgeMapper.ToDto(zaWorkflowService.UpdateEncounterSlotField(
                paths,
                session,
                request.Payload.TableId,
                request.Payload.Slot,
                request.Payload.Field,
                request.Payload.Value))
            : IsScarletViolet(paths)
            ? SvBridgeMapper.ToDto(svWorkflowService.UpdateEncounterSlotField(
                paths,
                session,
                request.Payload.TableId,
                request.Payload.Slot,
                request.Payload.Field,
                request.Payload.Value))
            : SwShBridgeMapper.ToDto(encountersEditSessionService.UpdateSlotField(
                paths,
                session,
                request.Payload.TableId,
                request.Payload.Slot,
                request.Payload.Field,
                request.Payload.Value));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateEncounterSlotFields(string requestJson)
    {
        var request = DeserializeRequest<UpdateEncounterSlotFieldsRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        if (IsPokemonLegendsZA(paths))
        {
            var zaUpdates = request.Payload.Updates
                .Select(update => new ZaEncounterSlotFieldUpdate(
                    update.TableId,
                    update.Slot,
                    update.Field,
                    update.Value))
                .ToArray();
            var zaResponse = ZaBridgeMapper.ToEncounterSlotFieldsDto(
                zaWorkflowService.UpdateEncounterSlotFields(paths, session, zaUpdates));

            return SerializeSuccess(zaResponse, request.RequestId);
        }

        object response;
        if (IsScarletViolet(paths))
        {
            var updates = request.Payload.Updates
                .Select(update => new SvEncounterSlotFieldUpdate(
                    update.TableId,
                    update.Slot,
                    update.Field,
                    update.Value))
                .ToArray();
            response = SvBridgeMapper.ToEncounterSlotFieldsDto(
                svWorkflowService.UpdateEncounterSlotFields(paths, session, updates));
        }
        else
        {
            var updates = request.Payload.Updates
                .Select(update => new SwShEncounterSlotFieldUpdate(
                    update.TableId,
                    update.Slot,
                    update.Field,
                    update.Value))
                .ToArray();
            response = SwShBridgeMapper.ToEncounterSlotFieldsDto(
                encountersEditSessionService.UpdateSlotFields(paths, session, updates));
        }

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageEncounterSlotVanilla(string requestJson)
    {
        var request = DeserializeRequest<StageEncounterSlotVanillaRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var response = ZaBridgeMapper.ToEncounterSlotVanillaDto(
            zaWorkflowService.StageEncounterSlotVanilla(
                ProjectBridgeMapper.ToCore(request.Payload.Paths),
                session,
                request.Payload.TableId,
                request.Payload.Slot));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadRaidBattlesWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadRaidBattlesWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadRaidBattles(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateRaidBattleSlotField(string requestJson)
    {
        var request = DeserializeRequest<UpdateRaidBattleSlotFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = raidBattlesEditSessionService.UpdateSlotField(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session,
            request.Payload.TableId,
            request.Payload.Slot,
            request.Payload.Field,
            request.Payload.Value);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateRaidBattleSlotFields(string requestJson)
    {
        var request = DeserializeRequest<UpdateRaidBattleSlotFieldsRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var updates = request.Payload.Updates?
            .Select(update => update is null
                ? null
                : new SwShRaidBattleFieldUpdate(
                    update.TableId,
                    update.Slot,
                    update.Field,
                    update.Value))
            .ToArray();
        var result = raidBattlesEditSessionService.UpdateSlotFields(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session,
            updates);
        var response = SwShBridgeMapper.ToFieldsDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadTeraRaidsWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadTeraRaidsWorkflowRequest>(requestJson);
        var workflow = svWorkflowService.LoadTeraRaids(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SvBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateTeraRaidField(string requestJson)
    {
        var request = DeserializeRequest<UpdateTeraRaidFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = svWorkflowService.UpdateTeraRaidField(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session,
            request.Payload.RecordId,
            request.Payload.Field,
            request.Payload.Value);
        var response = SvBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateTeraRaidFields(string requestJson)
    {
        var request = DeserializeRequest<UpdateTeraRaidFieldsRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var updates = request.Payload.Updates
            .Select(update => new SvTeraRaidFieldUpdate(update.RecordId, update.Field, update.Value))
            .ToArray();
        var response = SvBridgeMapper.ToTeraRaidFieldsDto(
            svWorkflowService.UpdateTeraRaidFields(
                ProjectBridgeMapper.ToCore(request.Payload.Paths),
                session,
                updates));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadRaidRewardsWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadRaidRewardsWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadRaidRewards(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateRaidRewardField(string requestJson)
    {
        var request = DeserializeRequest<UpdateRaidRewardFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = raidRewardsEditSessionService.UpdateRewardField(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session,
            request.Payload.TableId,
            request.Payload.Slot,
            request.Payload.Field,
            request.Payload.Value);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateRaidRewardFields(string requestJson)
    {
        var request = DeserializeRequest<UpdateRaidRewardFieldsRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var updates = request.Payload.Updates?
            .Select(update => update is null
                ? null
                : new SwShRaidRewardFieldUpdate(
                    update.TableId,
                    update.Slot,
                    update.Field,
                    update.Value))
            .ToArray();
        var result = raidRewardsEditSessionService.UpdateRewardFields(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session,
            updates);
        var response = SwShBridgeMapper.ToFieldsDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadRaidBonusRewardsWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadRaidBonusRewardsWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadRaidBonusRewards(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToBonusDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateRaidBonusRewardField(string requestJson)
    {
        var request = DeserializeRequest<UpdateRaidBonusRewardFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = raidRewardsEditSessionService.UpdateBonusRewardField(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session,
            request.Payload.TableId,
            request.Payload.Slot,
            request.Payload.Field,
            request.Payload.Value);
        var response = SwShBridgeMapper.ToBonusDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateRaidBonusRewardFields(string requestJson)
    {
        var request = DeserializeRequest<UpdateRaidBonusRewardFieldsRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var updates = request.Payload.Updates?
            .Select(update => update is null
                ? null
                : new SwShRaidRewardFieldUpdate(
                    update.TableId,
                    update.Slot,
                    update.Field,
                    update.Value))
            .ToArray();
        var result = raidRewardsEditSessionService.UpdateBonusRewardFields(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session,
            updates);
        var response = SwShBridgeMapper.ToBonusFieldsDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadPlacementWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadPlacementWorkflowRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        if (IsPokemonLegendsZA(paths))
        {
            var zaWorkflow = zaWorkflowService.LoadPlacement(paths);
            var zaResponse = ZaBridgeMapper.ToDto(zaWorkflow);

            return SerializeSuccess(zaResponse, request.RequestId);
        }

        if (IsScarletViolet(paths))
        {
            var svWorkflow = svWorkflowService.LoadPlacement(paths);
            var svResponse = SvBridgeMapper.ToDto(svWorkflow);

            return SerializeSuccess(svResponse, request.RequestId);
        }

        var workflow = swShWorkflowService.LoadPlacement(paths);
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchOpenSwShPlacementCatalog(string requestJson)
    {
        var request = DeserializeRequest<OpenSwShPlacementCatalogRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        EnsureSwordShieldPlacementCatalogPaths(paths);

        try
        {
            var project = projectWorkspaceService.Open(paths);
            var catalog = swShWorkflowService.OpenPlacementCatalog(project);
            return SerializeSuccess(SwShBridgeMapper.ToCatalogDto(catalog), request.RequestId);
        }
        catch (SwShPlacementCatalogException exception)
        {
            throw new BridgeRequestException(exception.Message, exception, exception.Code);
        }
    }

    private string DispatchQuerySwShPlacementCatalog(string requestJson)
    {
        var request = DeserializeRequest<QuerySwShPlacementCatalogRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        EnsureSwordShieldPlacementCatalogPaths(paths);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);

        try
        {
            var project = projectWorkspaceService.Open(paths);
            var result = swShWorkflowService.SharedPlacementWorkflowService.QueryCatalog(
                project,
                request.Payload.Revision,
                request.Payload.CategoryId,
                request.Payload.SearchText,
                request.Payload.Offset,
                request.Payload.Limit,
                session);
            return SerializeSuccess(SwShBridgeMapper.ToCatalogQueryDto(result), request.RequestId);
        }
        catch (SwShPlacementCatalogException exception)
        {
            throw new BridgeRequestException(exception.Message, exception, exception.Code);
        }
    }

    private string DispatchLoadSwShPlacementObject(string requestJson)
    {
        var request = DeserializeRequest<LoadSwShPlacementObjectRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        EnsureSwordShieldPlacementCatalogPaths(paths);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);

        try
        {
            var project = projectWorkspaceService.Open(paths);
            var result = swShWorkflowService.SharedPlacementWorkflowService.LoadCatalogObject(
                project,
                request.Payload.Revision,
                request.Payload.ObjectId,
                session);
            return SerializeSuccess(SwShBridgeMapper.ToPlacementObjectDetailDto(result), request.RequestId);
        }
        catch (SwShPlacementCatalogException exception)
        {
            throw new BridgeRequestException(exception.Message, exception, exception.Code);
        }
    }

    private static void EnsureSwordShieldPlacementCatalogPaths(ProjectPaths paths)
    {
        if (paths.SelectedGame is not (ProjectGame.Sword or ProjectGame.Shield))
        {
            throw new BridgeRequestException(
                "Sword/Shield Placement catalog commands require a Sword or Shield project.");
        }
    }

    private static T DispatchSwShPlacementOperation<T>(Func<T> operation)
    {
        try
        {
            return operation();
        }
        catch (SwShPlacementCatalogException exception)
        {
            throw new BridgeRequestException(exception.Message, exception, exception.Code);
        }
    }

    private string DispatchUpdatePlacementObjectField(string requestJson)
    {
        var request = DeserializeRequest<UpdatePlacementObjectFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        if (IsPokemonLegendsZA(paths))
        {
            var zaResult = zaWorkflowService.UpdatePlacementObjectField(
                paths,
                session,
                request.Payload.ObjectId,
                request.Payload.Field,
                request.Payload.Value);
            var zaResponse = ZaBridgeMapper.ToDto(zaResult);

            return SerializeSuccess(zaResponse, request.RequestId);
        }

        if (IsScarletViolet(paths))
        {
            var svResult = svWorkflowService.UpdatePlacementObjectField(
                paths,
                session,
                request.Payload.ObjectId,
                request.Payload.Field,
                request.Payload.Value);
            var svResponse = SvBridgeMapper.ToDto(svResult);

            return SerializeSuccess(svResponse, request.RequestId);
        }

        var result = DispatchSwShPlacementOperation(() =>
            placementEditSessionService.UpdateObjectField(
                paths,
                session,
                request.Payload.ObjectId,
                request.Payload.Field,
                request.Payload.Value));
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdatePlacementObjectFields(string requestJson)
    {
        var request = DeserializeRequest<UpdatePlacementObjectFieldsRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        if (IsPokemonLegendsZA(paths))
        {
            var zaUpdates = request.Payload.Updates
                .Select(update => new ZaPlacementObjectFieldUpdate(update.ObjectId, update.Field, update.Value))
                .ToArray();
            var zaResponse = ZaBridgeMapper.ToPlacementObjectFieldsDto(
                zaWorkflowService.UpdatePlacementObjectFields(paths, session, zaUpdates));

            return SerializeSuccess(zaResponse, request.RequestId);
        }

        if (IsScarletViolet(paths))
        {
            var svUpdates = request.Payload.Updates
                .Select(update => new SvPlacementObjectFieldUpdate(update.ObjectId, update.Field, update.Value))
                .ToArray();
            var svResponse = SvBridgeMapper.ToPlacementObjectFieldsDto(
                svWorkflowService.UpdatePlacementObjectFields(paths, session, svUpdates));

            return SerializeSuccess(svResponse, request.RequestId);
        }

        var swShUpdates = request.Payload.Updates
            .Select(update => new SwShPlacementObjectFieldUpdate(update.ObjectId, update.Field, update.Value))
            .ToArray();
        var response = SwShBridgeMapper.ToPlacementObjectFieldsDto(
            DispatchSwShPlacementOperation(() =>
                placementEditSessionService.UpdateObjectFields(paths, session, swShUpdates)));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadBehaviorWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadBehaviorWorkflowRequest>(requestJson);
        var paths = request.Payload.Paths
            ?? throw new BridgeRequestException("Behavior load paths are required.");
        var workflow = swShWorkflowService.LoadBehavior(ProjectBridgeMapper.ToCore(paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateBehaviorEntryField(string requestJson)
    {
        var request = DeserializeRequest<UpdateBehaviorEntryFieldRequest>(requestJson);
        var paths = request.Payload.Paths
            ?? throw new BridgeRequestException("Behavior update paths are required.");
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = behaviorEditSessionService.UpdateEntryField(
            ProjectBridgeMapper.ToCore(paths),
            session,
            request.Payload.EntryId,
            request.Payload.Field,
            request.Payload.Value);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateBehaviorEntryFields(string requestJson)
    {
        var request = DeserializeRequest<UpdateBehaviorEntryFieldsRequest>(requestJson);
        var paths = request.Payload.Paths
            ?? throw new BridgeRequestException("Behavior batch update paths are required.");
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var updates = request.Payload.Updates?.Select(update => update is null
            ? null
            : new SwShBehaviorFieldUpdate(update.EntryId, update.Field, update.Value)).ToArray();
        var result = behaviorEditSessionService.UpdateEntryFields(
            ProjectBridgeMapper.ToCore(paths),
            session,
            updates);
        var response = SwShBridgeMapper.ToBehaviorEntryFieldsDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadFlagworkSaveWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadFlagworkSaveWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadFlagworkSave(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadBagHookWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadBagHookWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadBagHook(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageBagHookInstall(string requestJson)
    {
        var request = DeserializeRequest<StageBagHookInstallRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = bagHookEditSessionService.StageInstall(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageBagHookUninstall(string requestJson)
    {
        var request = DeserializeRequest<StageBagHookUninstallRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = bagHookEditSessionService.StageUninstall(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session);
        var response = SwShBridgeMapper.ToBagHookUninstallDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadCatchCapWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadCatchCapWorkflowRequest>(requestJson);
        var paths = request.Payload.Paths
            ?? throw new BridgeRequestException("Catch Cap project paths are required.");
        var workflow = swShWorkflowService.LoadCatchCap(ProjectBridgeMapper.ToCore(paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageCatchCap(string requestJson)
    {
        var request = DeserializeRequest<StageCatchCapRequest>(requestJson);
        var paths = request.Payload.Paths
            ?? throw new BridgeRequestException("Catch Cap stage paths are required.");
        var caps = request.Payload.Caps
            ?? throw new BridgeRequestException("Catch Cap selections are required.");
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = catchCapEditSessionService.StageCaps(
            ProjectBridgeMapper.ToCore(paths),
            caps.Select(selection =>
            {
                var cap = selection
                    ?? throw new BridgeRequestException("Catch Cap selection entries are required.");
                return new SwShCatchCapSelection(cap.BadgeCount, cap.LevelCap);
            }).ToArray(),
            session);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageCatchCapUninstall(string requestJson)
    {
        var request = DeserializeRequest<StageCatchCapUninstallRequest>(requestJson);
        var paths = request.Payload.Paths
            ?? throw new BridgeRequestException("Catch Cap uninstall paths are required.");
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = catchCapEditSessionService.StageUninstall(
            ProjectBridgeMapper.ToCore(paths),
            session);
        var response = SwShBridgeMapper.ToCatchCapUninstallDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadHyperTrainingWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadHyperTrainingWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadHyperTraining(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageHyperTraining(string requestJson)
    {
        var request = DeserializeRequest<StageHyperTrainingRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = hyperTrainingEditSessionService.StageMinimumLevel(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            request.Payload.MinimumLevel,
            session);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadShinyRateWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadShinyRateWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadShinyRate(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageShinyRate(string requestJson)
    {
        var request = DeserializeRequest<StageShinyRateRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = shinyRateEditSessionService.StageRate(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            request.Payload.Mode,
            request.Payload.RollCount,
            session);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadTypeChartWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadTypeChartWorkflowRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        if (IsPokemonLegendsZA(paths))
        {
            var zaWorkflow = zaWorkflowService.LoadTypeChart(paths);
            var zaResponse = ZaBridgeMapper.ToDto(zaWorkflow);

            return SerializeSuccess(zaResponse, request.RequestId);
        }

        if (IsScarletViolet(paths))
        {
            var svWorkflow = svWorkflowService.LoadTypeChart(paths);
            var svResponse = SvBridgeMapper.ToDto(svWorkflow);

            return SerializeSuccess(svResponse, request.RequestId);
        }

        var workflow = swShWorkflowService.LoadTypeChart(paths);
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadFairyGymBoostsWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadFairyGymBoostsWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadFairyGymBoosts(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageFairyGymBoosts(string requestJson)
    {
        var request = DeserializeRequest<StageFairyGymBoostsRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var selections = request.Payload.Selections
            .Select(selection => new SwShFairyGymBoostSelection(
                selection.BoostId,
                selection.EffectId,
                selection.ResultKind))
            .ToArray();
        var result = fairyGymBoostsEditSessionService.StageBoosts(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            selections,
            session);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageTypeChart(string requestJson)
    {
        var request = DeserializeRequest<StageTypeChartRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        if (IsPokemonLegendsZA(paths))
        {
            var zaResult = zaWorkflowService.StageTypeChart(
                paths,
                request.Payload.Values,
                session);
            var zaResponse = ZaBridgeMapper.ToDto(zaResult);

            return SerializeSuccess(zaResponse, request.RequestId);
        }

        if (IsScarletViolet(paths))
        {
            var svResult = svWorkflowService.StageTypeChart(
                paths,
                request.Payload.Values,
                session);
            var svResponse = SvBridgeMapper.ToDto(svResult);

            return SerializeSuccess(svResponse, request.RequestId);
        }

        var result = typeChartEditSessionService.StageChart(
            paths,
            request.Payload.Values,
            session);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageTypeChartUninstall(string requestJson)
    {
        var request = DeserializeRequest<StageTypeChartUninstallRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        object response = IsPokemonLegendsZA(paths)
            ? ZaBridgeMapper.ToTypeChartUninstallDto(zaWorkflowService.StageTypeChartUninstall(paths, session))
            : SvBridgeMapper.ToTypeChartUninstallDto(svWorkflowService.StageTypeChartUninstall(paths, session));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadAngeFightWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadAngeFightWorkflowRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = ZaBridgeMapper.ToDto(zaWorkflowService.LoadAngeFight(paths));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageAngeFight(string requestJson)
    {
        var request = DeserializeRequest<StageAngeFightRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var settings = new KM.ZA.AngeFight.ZaAngeFightSettings(
            request.Payload.BlueFlowerHp,
            request.Payload.RedFlowerHp,
            request.Payload.Attacks
                .Select(attack => new KM.ZA.AngeFight.ZaAngeFightAttackSelection(
                    attack.AttackId,
                    attack.DamageToPokemon,
                    attack.DamageToPlayer))
                .ToArray());
        var response = ZaBridgeMapper.ToDto(
            zaWorkflowService.StageAngeFight(paths, settings, session));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageAngeFightUninstall(string requestJson)
    {
        var request = DeserializeRequest<StageAngeFightUninstallRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var response = ZaBridgeMapper.ToAngeFightUninstallDto(
            zaWorkflowService.StageAngeFightUninstall(paths, session));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadFashionUnlockWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadFashionUnlockWorkflowRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = IsScarletViolet(paths)
            ? SvBridgeMapper.ToDto(svWorkflowService.LoadFashionUnlock(paths))
            : SwShBridgeMapper.ToDto(swShWorkflowService.LoadFashionUnlock(paths));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageFashionUnlockInstall(string requestJson)
    {
        var request = DeserializeRequest<StageFashionUnlockInstallRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var response = IsScarletViolet(paths)
            ? SvBridgeMapper.ToFashionUnlockInstallDto(svWorkflowService.StageFashionUnlockInstall(paths, session))
            : SwShBridgeMapper.ToDto(fashionUnlockEditSessionService.StageInstall(paths, session));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageFashionUnlockUninstall(string requestJson)
    {
        var request = DeserializeRequest<StageFashionUnlockUninstallRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var response = IsScarletViolet(paths)
            ? SvBridgeMapper.ToFashionUnlockUninstallDto(svWorkflowService.StageFashionUnlockUninstall(paths, session))
            : SwShBridgeMapper.ToFashionUnlockUninstallDto(fashionUnlockEditSessionService.StageUninstall(paths, session));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadGymUniformRemovalWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadGymUniformRemovalWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadGymUniformRemoval(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageGymUniformRemovalInstall(string requestJson)
    {
        var request = DeserializeRequest<StageGymUniformRemovalInstallRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = gymUniformRemovalEditSessionService.StageInstall(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageGymUniformRemovalUninstall(string requestJson)
    {
        var request = DeserializeRequest<StageGymUniformRemovalUninstallRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = gymUniformRemovalEditSessionService.StageUninstall(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session);
        var response = SwShBridgeMapper.ToGymUniformRemovalUninstallDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadHyperspaceBypassWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadHyperspaceBypassWorkflowRequest>(requestJson);
        var workflow = svWorkflowService.LoadHyperspaceBypass(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SvBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageHyperspaceBypassInstall(string requestJson)
    {
        var request = DeserializeRequest<StageHyperspaceBypassInstallRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = svWorkflowService.StageHyperspaceBypassInstall(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session);
        var response = SvBridgeMapper.ToHyperspaceBypassInstallDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageHyperspaceBypassUninstall(string requestJson)
    {
        var request = DeserializeRequest<StageHyperspaceBypassUninstallRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = svWorkflowService.StageHyperspaceBypassUninstall(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session);
        var response = SvBridgeMapper.ToHyperspaceBypassUninstallDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadIvScreenWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadIvScreenWorkflowRequest>(requestJson);
        var paths = request.Payload.Paths
            ?? throw new BridgeRequestException("IV Screen project paths are required.");
        var workflow = swShWorkflowService.LoadIvScreen(ProjectBridgeMapper.ToCore(paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageIvScreenInstall(string requestJson)
    {
        var request = DeserializeRequest<StageIvScreenInstallRequest>(requestJson);
        var paths = request.Payload.Paths
            ?? throw new BridgeRequestException("IV Screen install paths are required.");
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = ivScreenEditSessionService.StageInstall(
            ProjectBridgeMapper.ToCore(paths),
            session);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageIvScreenUninstall(string requestJson)
    {
        var request = DeserializeRequest<StageIvScreenUninstallRequest>(requestJson);
        var paths = request.Payload.Paths
            ?? throw new BridgeRequestException("IV Screen uninstall paths are required.");
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = ivScreenEditSessionService.StageUninstall(
            ProjectBridgeMapper.ToCore(paths),
            session);
        var response = SwShBridgeMapper.ToIvScreenUninstallDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadExeFsPatchWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadExeFsPatchWorkflowRequest>(requestJson);
        var paths = request.Payload.Paths
            ?? throw new BridgeRequestException("ExeFS patch load paths are required.");
        var workflow = swShWorkflowService.LoadExeFsPatches(ProjectBridgeMapper.ToCore(paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageExeFsPatch(string requestJson)
    {
        var request = DeserializeRequest<StageExeFsPatchRequest>(requestJson);
        var paths = request.Payload.Paths
            ?? throw new BridgeRequestException("ExeFS patch stage paths are required.");
        var patchId = string.IsNullOrWhiteSpace(request.Payload.PatchId)
            ? throw new BridgeRequestException("ExeFS patch ID is required.")
            : request.Payload.PatchId.Trim();
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = exeFsPatchEditSessionService.StagePatch(
            ProjectBridgeMapper.ToCore(paths),
            patchId,
            session);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadRoyalCandyWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadRoyalCandyWorkflowRequest>(requestJson);
        var paths = request.Payload.Paths
            ?? throw new BridgeRequestException("Royal Candy load paths are required.");
        var workflow = swShWorkflowService.LoadRoyalCandy(ProjectBridgeMapper.ToCore(paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageRoyalCandyWorkflow(string requestJson)
    {
        var request = DeserializeRequest<StageRoyalCandyWorkflowRequest>(requestJson);
        var paths = request.Payload.Paths
            ?? throw new BridgeRequestException("Royal Candy stage paths are required.");
        var workflowId = string.IsNullOrWhiteSpace(request.Payload.WorkflowId)
            ? throw new BridgeRequestException("Royal Candy workflow ID is required.")
            : request.Payload.WorkflowId.Trim();
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = royalCandyEditSessionService.StageWorkflow(
            ProjectBridgeMapper.ToCore(paths),
            workflowId,
            request.Payload.LevelCaps?.Select(selection => new SwShRoyalCandyLevelCapSelection(
                selection.Slot,
                selection.LevelCap)).ToArray(),
            session);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadStartingItemsWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadStartingItemsWorkflowRequest>(requestJson);
        var paths = request.Payload.Paths
            ?? throw new BridgeRequestException("Starting Items load paths are required.");
        var workflow = swShWorkflowService.LoadStartingItems(ProjectBridgeMapper.ToCore(paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageStartingItems(string requestJson)
    {
        var request = DeserializeRequest<StageStartingItemsRequest>(requestJson);
        var paths = request.Payload.Paths
            ?? throw new BridgeRequestException("Starting Items stage paths are required.");
        var grants = request.Payload.Grants
            ?? throw new BridgeRequestException("Starting Items grants are required.");
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = startingItemsEditSessionService.StageGrants(
            ProjectBridgeMapper.ToCore(paths),
            grants.Select(selection => new SwShStartingItemGrantSelection(
                selection.Slot,
                selection.ItemId,
                selection.Quantity)).ToArray(),
            session);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadNpcItemGiftWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadNpcItemGiftWorkflowRequest>(requestJson);
        var paths = request.Payload.Paths
            ?? throw new BridgeRequestException("NPC Item Gift project paths are required.");
        var workflow = swShWorkflowService.LoadNpcItemGift(ProjectBridgeMapper.ToCore(paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageNpcItemGift(string requestJson)
    {
        var request = DeserializeRequest<StageNpcItemGiftRequest>(requestJson);
        var paths = request.Payload.Paths
            ?? throw new BridgeRequestException("NPC Item Gift project paths are required.");
        var gifts = request.Payload.Gifts
            ?? throw new BridgeRequestException("NPC Item Gift selections are required.");
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = npcItemGiftEditSessionService.StageGifts(
            ProjectBridgeMapper.ToCore(paths),
            gifts.Select(selection =>
            {
                var gift = selection
                    ?? throw new BridgeRequestException("NPC Item Gift selection entries are required.");
                var items = gift.Items
                    ?? throw new BridgeRequestException("NPC Item Gift item selections are required.");
                return new SwShNpcItemGiftSelection(
                    gift.GiftId,
                    gift.Quantity,
                    items.Select(item =>
                    {
                        var selectedItem = item
                            ?? throw new BridgeRequestException("NPC Item Gift item selection entries are required.");
                        return new SwShNpcItemGiftItemSelection(
                            selectedItem.SlotId,
                            selectedItem.ItemId);
                    }).ToArray());
            }).ToArray(),
            session);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadBattleCafeRewardsWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadBattleCafeRewardsWorkflowRequest>(requestJson);
        var paths = request.Payload.Paths
            ?? throw new BridgeRequestException("Battle Cafe Rewards project paths are required.");
        var workflow = swShWorkflowService.LoadBattleCafeRewards(ProjectBridgeMapper.ToCore(paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageBattleCafeRewardRows(string requestJson)
    {
        var request = DeserializeRequest<StageBattleCafeRewardRowsRequest>(requestJson);
        var paths = request.Payload.Paths
            ?? throw new BridgeRequestException("Battle Cafe Rewards project paths are required.");
        var rows = request.Payload.Rows
            ?? throw new BridgeRequestException("Battle Cafe reward rows are required.");
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = battleCafeRewardsEditSessionService.StageRows(
            ProjectBridgeMapper.ToCore(paths),
            rows.Select(row =>
            {
                var edit = row
                    ?? throw new BridgeRequestException("Battle Cafe reward row entries are required.");
                return new SwShBattleCafeRewardsRowEdit(
                    edit.RowIndex,
                    edit.ExpectedItemId,
                    edit.ExpectedDwightPercent,
                    edit.ExpectedBernardPercent,
                    edit.ExpectedRichardPercent,
                    edit.ItemId,
                    edit.DwightPercent,
                    edit.BernardPercent,
                    edit.RichardPercent);
            }).ToArray(),
            session);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadSpreadsheetImportWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadSpreadsheetImportWorkflowRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        object response = IsPokemonLegendsZA(paths)
            ? ZaBridgeMapper.ToDto(zaWorkflowService.LoadDumpImport(paths))
            : IsScarletViolet(paths)
                ? SvBridgeMapper.ToDto(svWorkflowService.LoadDumpImport(paths))
                : SwShBridgeMapper.ToDto(swShWorkflowService.LoadSpreadsheetImport(paths));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchPreviewSpreadsheetImport(string requestJson)
    {
        var request = DeserializeRequest<PreviewSpreadsheetImportRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        object response = IsPokemonLegendsZA(paths)
            ? ZaBridgeMapper.ToDto(zaWorkflowService.PreviewDumpImport(
                paths,
                request.Payload.ProfileId,
                request.Payload.SourcePath,
                session))
            : IsScarletViolet(paths)
                ? SvBridgeMapper.ToDto(svWorkflowService.PreviewDumpImport(
                    paths,
                    request.Payload.ProfileId,
                    request.Payload.SourcePath,
                    session))
                : SwShBridgeMapper.ToDto(spreadsheetImportExecutionService.Preview(
                    paths,
                    request.Payload.ProfileId,
                    request.Payload.SourcePath,
                    session));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadModMergerWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadModMergerWorkflowRequest>(requestJson);
        var workflow = modMergerWorkflowService.Load(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            request.Payload.ModDirectory1,
            request.Payload.ModDirectory2);
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageModMerge(string requestJson)
    {
        var request = DeserializeRequest<StageModMergeRequest>(requestJson);
        var result = modMergerWorkflowService.Stage(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            request.Payload.ModDirectory1,
            request.Payload.ModDirectory2,
            request.Payload.SelectedDirectory1Files,
            request.Payload.SelectedDirectory2Files,
            request.Payload.Resolutions.Select(resolution => new SwShModMergerConflictResolution(
                resolution.ConflictId,
                resolution.Source)).ToArray(),
            request.Payload.MergeMode);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchApplyModMerge(string requestJson)
    {
        var request = DeserializeRequest<ApplyModMergeRequest>(requestJson);
        var result = ExecuteSerializedSwShOutputOperation(() =>
            modMergerWorkflowService.ApplyReviewed(
                ProjectBridgeMapper.ToCore(request.Payload.Paths),
                request.Payload.ModDirectory1,
                request.Payload.ModDirectory2,
                request.Payload.SelectedDirectory1Files,
                request.Payload.SelectedDirectory2Files,
                request.Payload.Resolutions.Select(resolution => new SwShModMergerConflictResolution(
                    resolution.ConflictId,
                    resolution.Source)).ToArray(),
                request.Payload.MergeMode,
                request.Payload.ReviewToken));
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadSvModMergerWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadSvModMergerWorkflowRequest>(requestJson);
        var workflow = svWorkflowService.LoadModMerger(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            request.Payload.ModSources.Select(ToCore).ToArray());
        var response = SvBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageSvModMerge(string requestJson)
    {
        var request = DeserializeRequest<StageSvModMergeRequest>(requestJson);
        var result = svWorkflowService.StageModMerge(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            request.Payload.ModSources.Select(ToCore).ToArray());
        var response = SvBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchApplySvModMerge(string requestJson)
    {
        var request = DeserializeRequest<ApplySvModMergeRequest>(requestJson);
        var result = svWorkflowService.ApplyModMerge(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            request.Payload.ModSources.Select(ToCore).ToArray());
        var response = SvBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadZaModMergerWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadZaModMergerWorkflowRequest>(requestJson);
        var workflow = zaWorkflowService.LoadModMerger(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            request.Payload.ModSources.Select(ToCore).ToArray());
        var response = ZaBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageZaModMerge(string requestJson)
    {
        var request = DeserializeRequest<StageZaModMergeRequest>(requestJson);
        var result = zaWorkflowService.StageModMerge(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            request.Payload.ModSources.Select(ToCore).ToArray());
        var response = ZaBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchApplyZaModMerge(string requestJson)
    {
        var request = DeserializeRequest<ApplyZaModMergeRequest>(requestJson);
        var result = zaWorkflowService.ApplyModMerge(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            request.Payload.ModSources.Select(ToCore).ToArray());
        var response = ZaBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchGetSvCacheStatus(string requestJson)
    {
        var request = DeserializeRequest<GetSvCacheStatusRequest>(requestJson);
        var paths = request.Payload.Paths is null
            ? null
            : ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = SvBridgeMapper.ToDto(svWorkflowService.GetCacheStatus(paths));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateSvCacheSettings(string requestJson)
    {
        var request = DeserializeRequest<UpdateSvCacheSettingsRequest>(requestJson);
        var paths = request.Payload.Paths is null
            ? null
            : ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = SvBridgeMapper.ToDto(svWorkflowService.UpdateCacheSettings(
            SvBridgeMapper.ToCore(request.Payload.Mode),
            request.Payload.MaxCacheSizeBytes,
            paths));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchClearSvCache(string requestJson)
    {
        var request = DeserializeRequest<ClearSvCacheRequest>(requestJson);
        var paths = request.Payload.ActivePaths is null
            ? null
            : ProjectBridgeMapper.ToCore(request.Payload.ActivePaths);
        var response = SvBridgeMapper.ToDto(svWorkflowService.ClearCache(paths));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchWarmupSvCacheStep(string requestJson)
    {
        var request = DeserializeRequest<WarmupSvCacheStepRequest>(requestJson);
        var response = SvBridgeMapper.ToDto(svWorkflowService.WarmupCacheStep(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            request.Payload.StepIndex));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchGetZaCacheStatus(string requestJson)
    {
        var request = DeserializeRequest<GetZaCacheStatusRequest>(requestJson);
        var paths = request.Payload.Paths is null
            ? null
            : ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = ZaBridgeMapper.ToDto(zaWorkflowService.GetCacheStatus(paths));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateZaCacheSettings(string requestJson)
    {
        var request = DeserializeRequest<UpdateZaCacheSettingsRequest>(requestJson);
        var paths = request.Payload.Paths is null
            ? null
            : ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = ZaBridgeMapper.ToDto(zaWorkflowService.UpdateCacheSettings(
            ZaBridgeMapper.ToCore(request.Payload.Mode),
            request.Payload.MaxCacheSizeBytes,
            paths));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchClearZaCache(string requestJson)
    {
        var request = DeserializeRequest<ClearZaCacheRequest>(requestJson);
        var paths = request.Payload.ActivePaths is null
            ? null
            : ProjectBridgeMapper.ToCore(request.Payload.ActivePaths);
        var response = ZaBridgeMapper.ToDto(zaWorkflowService.ClearCache(paths));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchWarmupZaCacheStep(string requestJson)
    {
        var request = DeserializeRequest<WarmupZaCacheStepRequest>(requestJson);
        var response = ZaBridgeMapper.ToDto(zaWorkflowService.WarmupCacheStep(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            request.Payload.StepIndex));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchGetSwShCacheStatus(string requestJson)
    {
        var request = DeserializeRequest<GetSwShCacheStatusRequest>(requestJson);
        var paths = request.Payload.Paths is null
            ? null
            : ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = SwShCacheBridgeMapper.ToDto(swShWorkflowService.GetCacheStatus(paths));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateSwShCacheSettings(string requestJson)
    {
        var request = DeserializeRequest<UpdateSwShCacheSettingsRequest>(requestJson);
        var paths = request.Payload.Paths is null
            ? null
            : ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = SwShCacheBridgeMapper.ToDto(swShWorkflowService.UpdateCacheSettings(
            SwShCacheBridgeMapper.ToCore(request.Payload.Mode),
            request.Payload.MaxCacheSizeBytes,
            paths));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchClearSwShCache(string requestJson)
    {
        var request = DeserializeRequest<ClearSwShCacheRequest>(requestJson);
        var paths = request.Payload.ActivePaths is null
            ? null
            : ProjectBridgeMapper.ToCore(request.Payload.ActivePaths);
        var response = SwShCacheBridgeMapper.ToDto(swShWorkflowService.ClearCache(paths));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchWarmupSwShCacheStep(string requestJson)
    {
        var request = DeserializeRequest<WarmupSwShCacheStepRequest>(requestJson);
        var response = SwShCacheBridgeMapper.ToDto(swShWorkflowService.WarmupCacheStep(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            request.Payload.StepIndex));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadFpsPatch(string requestJson)
    {
        var request = DeserializeRequest<LoadFpsPatchRequest>(requestJson);
        var status = fpsPatchService.Load(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = new LoadFpsPatchResponse(ToDto(status));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchApplyFpsPatch(string requestJson)
    {
        var request = DeserializeRequest<ApplyFpsPatchRequest>(requestJson);
        var result = ExecuteSerializedSwShOutputOperation(() =>
            fpsPatchService.Apply(
                ProjectBridgeMapper.ToCore(request.Payload.Paths),
                request.Payload.EnabledAnimationTimingComponentIds));
        var response = new ApplyFpsPatchResponse(
            ToDto(result.Status),
            EditSessionBridgeMapper.ToDto(result.ApplyResult),
            result.RecoveryRequired);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchRestoreFpsPatch(string requestJson)
    {
        var request = DeserializeRequest<RestoreFpsPatchRequest>(requestJson);
        var result = ExecuteSerializedSwShOutputOperation(() =>
            fpsPatchService.Restore(
                ProjectBridgeMapper.ToCore(request.Payload.Paths),
                request.Payload.AnimationTimingComponentIds));
        var response = new RestoreFpsPatchResponse(
            ToDto(result.Status),
            EditSessionBridgeMapper.ToDto(result.ApplyResult),
            result.RecoveryRequired);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadProfanityFilter(string requestJson)
    {
        var request = DeserializeRequest<LoadProfanityFilterRequest>(requestJson);
        var status = profanityFilterService.Load(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = new LoadProfanityFilterResponse(ToDto(status));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchApplyProfanityFilter(string requestJson)
    {
        var request = DeserializeRequest<ApplyProfanityFilterRequest>(requestJson);
        var result = ExecuteSerializedSwShOutputOperation(() =>
            profanityFilterService.Apply(ProjectBridgeMapper.ToCore(request.Payload.Paths)));
        var response = new ApplyProfanityFilterResponse(
            ToDto(result.Status),
            EditSessionBridgeMapper.ToDto(result.ApplyResult));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchRestoreProfanityFilter(string requestJson)
    {
        var request = DeserializeRequest<RestoreProfanityFilterRequest>(requestJson);
        var result = ExecuteSerializedSwShOutputOperation(() =>
            profanityFilterService.Restore(ProjectBridgeMapper.ToCore(request.Payload.Paths)));
        var response = new RestoreProfanityFilterResponse(
            ToDto(result.Status),
            EditSessionBridgeMapper.ToDto(result.ApplyResult));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchImportRandomizerSeed(string requestJson)
    {
        var request = DeserializeRequest<ImportRandomizerSeedRequest>(requestJson);
        var result = randomizerService.ImportSeed(request.Payload.Seed);
        var response = new ImportRandomizerSeedResponse(
            result.Config is null ? null : ToDto(result.Config),
            result.Seed,
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchApplyRandomizer(string requestJson)
    {
        var request = DeserializeRequest<ApplyRandomizerRequest>(requestJson);
        var result = ExecuteSerializedSwShOutputOperation(() =>
            randomizerService.Apply(
                ProjectBridgeMapper.ToCore(request.Payload.Paths),
                ToCore(request.Payload.Config)));
        var response = new ApplyRandomizerResponse(
            result.Seed,
            EditSessionBridgeMapper.ToDto(result.ApplyResult));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchRestoreRandomizer(string requestJson)
    {
        var request = DeserializeRequest<RestoreRandomizerRequest>(requestJson);
        var result = ExecuteSerializedSwShOutputOperation(() =>
            randomizerService.Restore(ProjectBridgeMapper.ToCore(request.Payload.Paths)));
        var response = new RestoreRandomizerResponse(EditSessionBridgeMapper.ToDto(result));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateItemField(string requestJson)
    {
        var request = DeserializeRequest<UpdateItemFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = IsPokemonLegendsZA(paths)
            ? ZaBridgeMapper.ToDto(zaWorkflowService.UpdateItemField(
                paths,
                session,
                request.Payload.ItemId,
                request.Payload.Field,
                request.Payload.Value))
            : IsScarletViolet(paths)
            ? SvBridgeMapper.ToDto(svWorkflowService.UpdateItemField(
                paths,
                session,
                request.Payload.ItemId,
                request.Payload.Field,
                request.Payload.Value))
            : SwShBridgeMapper.ToDto(itemsEditSessionService.UpdateField(
                paths,
                session,
                request.Payload.ItemId,
                request.Payload.Field,
                request.Payload.Value));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateItemFields(string requestJson)
    {
        var request = DeserializeRequest<UpdateItemFieldsRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = IsPokemonLegendsZA(paths)
            ? ZaBridgeMapper.ToItemFieldsDto(
                zaWorkflowService.UpdateItemFields(
                    paths,
                    session,
                    request.Payload.Updates
                        .Select(update => new ZaItemFieldUpdate(update.ItemId, update.Field, update.Value))
                        .ToArray()))
            : IsScarletViolet(paths)
            ? SvBridgeMapper.ToItemFieldsDto(
                svWorkflowService.UpdateItemFields(
                    paths,
                    session,
                    request.Payload.Updates
                        .Select(update => new SvItemFieldUpdate(update.ItemId, update.Field, update.Value))
                        .ToArray()))
            : SwShBridgeMapper.ToItemFieldsDto(
                itemsEditSessionService.UpdateFields(
                    paths,
                    session,
                    request.Payload.Updates
                        .Select(update => new SwShItemFieldUpdate(update.ItemId, update.Field, update.Value))
                        .ToArray()));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageItemVanilla(string requestJson)
    {
        var request = DeserializeRequest<StageItemVanillaRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var response = ZaBridgeMapper.ToItemVanillaDto(
            zaWorkflowService.StageItemVanilla(
                ProjectBridgeMapper.ToCore(request.Payload.Paths),
                session,
                request.Payload.ItemId));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStartEditSession(string requestJson)
    {
        var request = DeserializeRequest<StartEditSessionRequest>(requestJson);
        _ = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = new StartEditSessionResponse(
            EditSessionBridgeMapper.ToDto(EditSession.Start()));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchValidateEditSession(string requestJson)
    {
        var request = DeserializeRequest<ValidateEditSessionRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var session = EditSessionBridgeMapper.ToCore(request.Payload.Session);
        return changeSetApplicationService
            .ExecuteWithAuthoringBindingAsync(
                session,
                paths,
                GetBoundOutputMode(session),
                (candidate, outputMode) => CreateChangePlanForSession(
                    paths,
                    candidate,
                    outputMode),
                () =>
                {
                    if (IsPokemonLegendsZA(paths))
                    {
                        var zaValidation = zaWorkflowService.ValidateEditSession(paths, session);
                        return SerializeSuccess(ZaBridgeMapper.ToDto(zaValidation), request.RequestId);
                    }

                    if (IsScarletViolet(paths))
                    {
                        var svValidation = svWorkflowService.ValidateEditSession(paths, session);
                        return SerializeSuccess(SvBridgeMapper.ToDto(svValidation), request.RequestId);
                    }

                    var validation = ValidateSwShEditSession(paths, session);
                    return SerializeSuccess(SwShBridgeMapper.ToDto(validation), request.RequestId);
                })
            .GetAwaiter()
            .GetResult();
    }

    private string DispatchCreateChangePlan(string requestJson)
    {
        var request = DeserializeRequest<CreateChangePlanRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var session = EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var changePlan = changeSetApplicationService
            .ExecuteWithAuthoringBindingAsync(
                session,
                paths,
                request.Payload.OutputMode,
                (candidate, outputMode) => CreateChangePlanForSession(
                    paths,
                    candidate,
                    outputMode),
                () => CreateChangePlanForSession(paths, session, request.Payload.OutputMode))
            .GetAwaiter()
            .GetResult();
        var response = new CreateChangePlanResponse(EditSessionBridgeMapper.ToDto(changePlan));

        return SerializeSuccess(response, request.RequestId);
    }

    private ChangePlan CreateChangePlanForSession(
        ProjectPaths paths,
        EditSession session,
        ChangePlanOutputModeDto? outputMode)
    {
        if (session.PendingEdits.Count > 0
            && session.PendingEdits.All(edit => GeneratedChangeSetOwners.IsSupported(edit.Owner))
            && session.PendingEdits.Select(edit => edit.Owner).Distinct(StringComparer.Ordinal).Count() == 1
            && session.PendingEdits
                .Select(edit => edit.Domain)
                .Distinct(StringComparer.Ordinal)
                .Count() == 1)
        {
            return CreateGuidedChangePlanForSession(paths, session, outputMode);
        }

        var isPokemonLegendsZA = IsPokemonLegendsZA(paths);
        var isScarletViolet = IsScarletViolet(paths);
        ChangePlan changePlan;
        if (isPokemonLegendsZA)
        {
            changePlan = zaWorkflowService.CreateChangePlan(
                paths,
                session,
                ZaBridgeMapper.ToCore(outputMode));
        }
        else if (isScarletViolet)
        {
            changePlan = svWorkflowService.CreateChangePlan(
                paths,
                session,
                SvBridgeMapper.ToCore(outputMode));
        }
        else
        {
            lock (SwShApplySyncRoot)
            {
                ClearCriticalSwShApplyCaches();
                changePlan = SwShChangePlanSourceGuard.Capture(
                    paths,
                    CreateSwShChangePlan(paths, session),
                    SwShGymUniformRemovalEditSessionService.IsCanonicalUninstallSession(session));
            }
        }

        return changePlan;
    }

    private ChangePlan CreateGuidedChangePlanForSession(
        ProjectPaths paths,
        EditSession session,
        ChangePlanOutputModeDto? outputMode)
    {
        if (session.PendingEdits
                .Select(edit => edit.Domain)
                .Distinct(StringComparer.Ordinal)
                .Count() != 1)
        {
            return InvalidGuidedChangePlan(
                session,
                "A generated change-set plan must contain exactly one owning workflow domain.");
        }

        var initialSourceFingerprint = CaptureSemanticExploreSourceFingerprint(paths);
        var initialOutputMembershipFingerprint =
            CaptureGuidedOutputMembershipFingerprint(paths, outputMode);
        ChangePlan plan;
        var outputModeKey = outputMode?.ToString() ?? "<null>";
        if (IsPokemonLegendsZA(paths))
        {
            var mode = ZaBridgeMapper.ToCore(outputMode);
            plan = zaWorkflowService.CreateGuidedChangePlanFreshBounded(paths, session, mode);
        }
        else if (IsScarletViolet(paths))
        {
            var mode = SvBridgeMapper.ToCore(outputMode);
            plan = svWorkflowService.CreateGuidedChangePlanFreshBounded(paths, session, mode);
        }
        else if (paths.SelectedGame is ProjectGame.Sword or ProjectGame.Shield)
        {
            plan = swShWorkflowService.CreateGuidedChangePlanFreshBounded(paths, session);
        }
        else
        {
            return InvalidGuidedChangePlan(
                session,
                "The selected game does not expose a generated change-set plan adapter.");
        }

        var completedSourceFingerprint = CaptureSemanticExploreSourceFingerprint(paths);
        var completedOutputMembershipFingerprint =
            CaptureGuidedOutputMembershipFingerprint(paths, outputMode);
        if (!string.Equals(
                initialSourceFingerprint,
                completedSourceFingerprint,
                StringComparison.Ordinal)
            || !string.Equals(
                initialOutputMembershipFingerprint,
                completedOutputMembershipFingerprint,
                StringComparison.Ordinal))
        {
            return InvalidGuidedChangePlan(
                session,
                "The generated change-set source changed while its bounded plan was created.",
                plan.Diagnostics);
        }

        if (!plan.CanApply)
        {
            return plan;
        }

        var domain = session.PendingEdits[0].Domain;
        var sourceBinding = CreateGuidedPlanSourceFingerprint(
            paths.SelectedGame!.Value,
            domain,
            outputModeKey,
            completedSourceFingerprint,
            completedOutputMembershipFingerprint);
        return plan with { GeneratedSourceBindingFingerprint = sourceBinding };
    }

    private static string CaptureGuidedOutputMembershipFingerprint(
        ProjectPaths paths,
        ChangePlanOutputModeDto? outputMode)
    {
        if (paths.SelectedGame is not (ProjectGame.Scarlet
                or ProjectGame.Violet
                or ProjectGame.ZA)
            || outputMode is ChangePlanOutputModeDto.TrinityModManager
                or ChangePlanOutputModeDto.TrinityBypass)
        {
            return "not-applicable";
        }

        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            throw new InvalidOperationException(
                "Generated change-set standalone planning requires an output root.");
        }

        var membership = ReadOnlyOutputDirectoryMembership.Capture(
            paths.OutputRootPath,
            new RelativeOutputPath("romfs"));
        return membership.Revision.Value;
    }

    private string CaptureSemanticExploreSourceFingerprint(ProjectPaths paths)
    {
        if (IsPokemonLegendsZA(paths))
        {
            return zaWorkflowService.CaptureSemanticExploreSourceFingerprint(paths);
        }

        if (IsScarletViolet(paths))
        {
            return svWorkflowService.CaptureSemanticExploreSourceFingerprint(paths);
        }

        if (paths.SelectedGame is ProjectGame.Sword or ProjectGame.Shield)
        {
            return swShWorkflowService.CaptureSemanticExploreSourceFingerprint(paths);
        }

        throw new SemanticExploreValidationException(
            "The selected semantic game is unsupported.",
            SemanticExploreFailureKind.Unsupported);
    }

    private static string CreateGuidedPlanSourceFingerprint(
        ProjectGame game,
        string domain,
        string outputMode,
        string sourceFingerprint,
        string outputMembershipFingerprint)
    {
        var payload = string.Join(
            '\n',
            "guided-design-plan-source-v1",
            game.ToString(),
            domain,
            outputMode,
            sourceFingerprint,
            outputMembershipFingerprint);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static ChangePlan InvalidGuidedChangePlan(
        EditSession session,
        string message,
        IReadOnlyList<ValidationDiagnostic>? existingDiagnostics = null) =>
        new(
            session.Id,
            Array.Empty<PlannedFileWrite>(),
            (existingDiagnostics ?? Array.Empty<ValidationDiagnostic>())
                .Append(new ValidationDiagnostic(
                    DiagnosticSeverity.Error,
                    message,
                    Domain: "guidedDesign"))
                .ToArray());

    private string DispatchApplyChangePlan(string requestJson)
    {
        var request = DeserializeRequest<ApplyChangePlanRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var session = EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var changePlan = EditSessionBridgeMapper.ToCore(request.Payload.ChangePlan);
        var isPokemonLegendsZA = IsPokemonLegendsZA(paths);
        var isScarletViolet = IsScarletViolet(paths);
        var applyResult = changeSetApplicationService
            .ExecuteBoundApplyAsync(
                session,
                paths,
                request.Payload.OutputMode,
                changePlan,
                (candidate, outputMode) => CreateChangePlanForSession(
                    paths,
                    candidate,
                    outputMode),
                () => isPokemonLegendsZA
                    ? zaWorkflowService.ApplyChangePlan(
                        paths,
                        session,
                        changePlan,
                        ZaBridgeMapper.ToCore(request.Payload.OutputMode))
                    : isScarletViolet
                        ? svWorkflowService.ApplyChangePlan(
                            paths,
                            session,
                            changePlan,
                            SvBridgeMapper.ToCore(request.Payload.OutputMode))
                        : ApplyVerifiedSwShChangePlan(paths, session, changePlan))
            .GetAwaiter()
            .GetResult();
        var response = new ApplyChangePlanResponse(EditSessionBridgeMapper.ToDto(applyResult));

        return SerializeSuccess(response, request.RequestId);
    }

    private static ChangePlanOutputModeDto? GetBoundOutputMode(EditSession session)
    {
        return session.AuthoringBinding?.OutputMode switch
        {
            "standalone" => ChangePlanOutputModeDto.Standalone,
            "trinityModManager" => ChangePlanOutputModeDto.TrinityModManager,
            "trinityBypass" => ChangePlanOutputModeDto.TrinityBypass,
            null => null,
            _ => throw new ChangeSetValidationException(
                "The edit-session authoring output mode is invalid."),
        };
    }

    private TResult ExecuteExclusiveOutputOperation<TResult>(
        ProjectPathsDto paths,
        Func<TResult> operation)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(operation);
        var projectId = ProjectIdentity.FromPaths(ProjectBridgeMapper.ToCore(paths));
        return outputSafetyApplicationService
            .ExecuteExclusiveOutputOperationAsync(
                new OutputScopeDto(projectId.Value, paths),
                operation)
            .GetAwaiter()
            .GetResult();
    }

    private static TResult ExecuteSerializedSwShOutputOperation<TResult>(Func<TResult> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        lock (SwShApplySyncRoot)
        {
            return operation();
        }
    }

    private ApplyResult ApplyVerifiedSwShChangePlan(
        ProjectPaths paths,
        EditSession session,
        ChangePlan reviewedPlan)
    {
        lock (SwShApplySyncRoot)
        {
            ClearCriticalSwShApplyCaches();
            var currentPlan = CreateSwShChangePlan(paths, session);
            var preserveExplicitSourceLayers =
                SwShGymUniformRemovalEditSessionService.IsCanonicalUninstallSession(session);
            if (!SwShChangePlanSourceGuard.TryAcquireApplyScope(
                paths,
                currentPlan,
                out var verifiedScope,
                out var sourceDiagnostics,
                preserveExplicitSourceLayers))
            {
                return CreateStaleSourceApplyResult(
                    reviewedPlan,
                    sourceDiagnostics
                        .Append(CreateStaleSwShPlanDiagnostic())
                        .ToArray());
            }

            using var applyScope = verifiedScope!;
            if (!ChangePlanReview.Matches(reviewedPlan, applyScope.CurrentPlan))
            {
                var staleDiagnostics = SwShChangePlanSourceGuard.Validate(paths, reviewedPlan).ToList();
                staleDiagnostics.Add(CreateStaleSwShPlanDiagnostic());
                return CreateStaleSourceApplyResult(
                    reviewedPlan,
                    staleDiagnostics);
            }

            var snapshotPlan = CreateSwShChangePlan(applyScope.ApplyPaths, session);
            if (!applyScope.TryPrepareSnapshotPlan(snapshotPlan, out var preparedSnapshotPlan))
            {
                return CreateStaleSourceApplyResult(
                    reviewedPlan,
                    preparedSnapshotPlan.Diagnostics
                        .Append(CreateStaleSwShPlanDiagnostic())
                        .ToArray());
            }

            var snapshotResult = ApplySwShChangePlan(
                applyScope.ApplyPaths,
                session,
                preparedSnapshotPlan);
            var result = applyScope.Commit(snapshotResult);
            if (result.WrittenFiles.Count > 0)
            {
                ClearCriticalSwShApplyCaches();
            }

            return result;
        }
    }

    private static ValidationDiagnostic CreateStaleSwShPlanDiagnostic()
    {
        return new ValidationDiagnostic(
            DiagnosticSeverity.Error,
            "Reviewed change plan is stale. Its files, sources, or planned changes no longer match.",
            Domain: "workflow.changePlan",
            Expected: "Review the current Sword/Shield change plan before applying");
    }

    private static ApplyResult CreateStaleSourceApplyResult(
        ChangePlan reviewedPlan,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        return new ApplyResult(
            applyId,
            appliedAt,
            Array.Empty<ProjectFileReference>(),
            new WriteManifest(applyId, appliedAt, reviewedPlan.Writes),
            diagnostics);
    }

    private SwShEditSessionValidation ValidateSwShEditSession(ProjectPaths paths, EditSession session)
    {
        var domain = GetEditSessionDomain(session);
        return domain == EditSessionDomain.Mixed && TryGetNormalSwShDomains(session, out var domains)
            ? ValidateNormalSwShDomains(paths, session, domains)
            : ValidateSingleSwShDomain(paths, session, domain);
    }

    private ChangePlan CreateSwShChangePlan(ProjectPaths paths, EditSession session)
    {
        var domain = GetEditSessionDomain(session);
        return domain == EditSessionDomain.Mixed && TryGetNormalSwShDomains(session, out var domains)
            ? CreateNormalSwShChangePlan(paths, session, domains)
            : CreateSingleSwShChangePlan(paths, session, domain);
    }

    private ApplyResult ApplySwShChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewedPlan)
    {
        var domain = GetEditSessionDomain(session);
        return domain == EditSessionDomain.Mixed && TryGetNormalSwShDomains(session, out var domains)
            ? ApplyNormalSwShChangePlan(paths, session, reviewedPlan, domains)
            : ApplySingleSwShChangePlan(paths, session, reviewedPlan, domain);
    }

    private SwShEditSessionValidation ValidateSingleSwShDomain(
        ProjectPaths paths,
        EditSession session,
        EditSessionDomain domain)
    {
        return domain switch
        {
            EditSessionDomain.DynamaxAdventures => dynamaxAdventuresEditSessionService.Validate(paths, session),
            EditSessionDomain.Encounters => encountersEditSessionService.Validate(paths, session),
            EditSessionDomain.ExeFsPatches => exeFsPatchEditSessionService.Validate(paths, session),
            EditSessionDomain.BagHook => bagHookEditSessionService.Validate(paths, session),
            EditSessionDomain.CatchCap => catchCapEditSessionService.Validate(paths, session),
            EditSessionDomain.HyperTraining => hyperTrainingEditSessionService.Validate(paths, session),
            EditSessionDomain.ShinyRate => shinyRateEditSessionService.Validate(paths, session),
            EditSessionDomain.TypeChart => typeChartEditSessionService.Validate(paths, session),
            EditSessionDomain.FairyGymBoosts => fairyGymBoostsEditSessionService.Validate(paths, session),
            EditSessionDomain.FashionUnlock => fashionUnlockEditSessionService.Validate(paths, session),
            EditSessionDomain.GymUniformRemoval => gymUniformRemovalEditSessionService.Validate(paths, session),
            EditSessionDomain.IvScreen => ivScreenEditSessionService.Validate(paths, session),
            EditSessionDomain.GiftPokemon => giftPokemonEditSessionService.Validate(paths, session),
            EditSessionDomain.TradePokemon => tradePokemonEditSessionService.Validate(paths, session),
            EditSessionDomain.RentalPokemon => rentalPokemonEditSessionService.Validate(paths, session),
            EditSessionDomain.Placement => DispatchSwShPlacementOperation(() =>
                placementEditSessionService.Validate(paths, session)),
            EditSessionDomain.Behavior => behaviorEditSessionService.Validate(paths, session),
            EditSessionDomain.RaidBattles => raidBattlesEditSessionService.Validate(paths, session),
            EditSessionDomain.RaidRewards => raidRewardsEditSessionService.Validate(paths, session),
            EditSessionDomain.RaidBonusRewards => raidRewardsEditSessionService.Validate(paths, session),
            EditSessionDomain.StaticEncounters => staticEncountersEditSessionService.Validate(paths, session),
            EditSessionDomain.Trainers => trainersEditSessionService.Validate(paths, session),
            EditSessionDomain.Shops => shopsEditSessionService.Validate(paths, session),
            EditSessionDomain.Text => textEditSessionService.Validate(paths, session),
            EditSessionDomain.Items => itemsEditSessionService.Validate(paths, session),
            EditSessionDomain.Pokemon => pokemonEditSessionService.Validate(paths, session),
            EditSessionDomain.Moves => movesEditSessionService.Validate(paths, session),
            EditSessionDomain.RoyalCandy => royalCandyEditSessionService.Validate(paths, session),
            EditSessionDomain.StartingItems => startingItemsEditSessionService.Validate(paths, session),
            EditSessionDomain.NpcItemGift => npcItemGiftEditSessionService.Validate(paths, session),
            EditSessionDomain.BattleCafeRewards => battleCafeRewardsEditSessionService.Validate(paths, session),
            EditSessionDomain.Mixed => CreateUnsupportedMixedValidation(session),
            _ => itemsEditSessionService.Validate(paths, session),
        };
    }

    private ChangePlan CreateSingleSwShChangePlan(
        ProjectPaths paths,
        EditSession session,
        EditSessionDomain domain)
    {
        return domain switch
        {
            EditSessionDomain.DynamaxAdventures => dynamaxAdventuresEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.Encounters => encountersEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.ExeFsPatches => exeFsPatchEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.BagHook => bagHookEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.CatchCap => catchCapEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.HyperTraining => hyperTrainingEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.ShinyRate => shinyRateEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.TypeChart => typeChartEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.FairyGymBoosts => fairyGymBoostsEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.FashionUnlock => fashionUnlockEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.GymUniformRemoval => gymUniformRemovalEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.IvScreen => ivScreenEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.GiftPokemon => giftPokemonEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.TradePokemon => tradePokemonEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.RentalPokemon => rentalPokemonEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.Placement => DispatchSwShPlacementOperation(() =>
                placementEditSessionService.CreateChangePlan(paths, session)),
            EditSessionDomain.Behavior => behaviorEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.RaidBattles => raidBattlesEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.RaidRewards => raidRewardsEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.RaidBonusRewards => raidRewardsEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.StaticEncounters => staticEncountersEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.Trainers => trainersEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.Shops => shopsEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.Text => textEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.Items => itemsEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.Pokemon => pokemonEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.Moves => movesEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.RoyalCandy => royalCandyEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.StartingItems => startingItemsEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.NpcItemGift => npcItemGiftEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.BattleCafeRewards => battleCafeRewardsEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.Mixed => CreateUnsupportedMixedChangePlan(session),
            _ => itemsEditSessionService.CreateChangePlan(paths, session),
        };
    }

    private ApplyResult ApplySingleSwShChangePlan(
        ProjectPaths paths,
        EditSession session,
        ChangePlan reviewedPlan,
        EditSessionDomain domain)
    {
        return domain switch
        {
            EditSessionDomain.DynamaxAdventures => dynamaxAdventuresEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.Encounters => encountersEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.ExeFsPatches => exeFsPatchEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.BagHook => bagHookEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.CatchCap => catchCapEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.HyperTraining => hyperTrainingEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.ShinyRate => shinyRateEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.TypeChart => typeChartEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.FairyGymBoosts => fairyGymBoostsEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.FashionUnlock => fashionUnlockEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.GymUniformRemoval => gymUniformRemovalEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.IvScreen => ivScreenEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.GiftPokemon => giftPokemonEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.TradePokemon => tradePokemonEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.RentalPokemon => rentalPokemonEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.Placement => DispatchSwShPlacementOperation(() =>
                placementEditSessionService.ApplyChangePlan(paths, session, reviewedPlan)),
            EditSessionDomain.Behavior => behaviorEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.RaidBattles => raidBattlesEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.RaidRewards => raidRewardsEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.RaidBonusRewards => raidRewardsEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.StaticEncounters => staticEncountersEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.Trainers => trainersEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.Shops => shopsEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.Text => textEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.Items => itemsEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.Pokemon => pokemonEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.Moves => movesEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.RoyalCandy => royalCandyEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.StartingItems => startingItemsEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.NpcItemGift => npcItemGiftEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.BattleCafeRewards => battleCafeRewardsEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.Mixed => CreateUnsupportedMixedApplyResult(session),
            _ => itemsEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
        };
    }

    private SwShEditSessionValidation ValidateNormalSwShDomains(
        ProjectPaths paths,
        EditSession session,
        IReadOnlyList<EditSessionDomain> domains)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        foreach (var domain in domains)
        {
            var validation = ValidateSingleSwShDomain(paths, SliceSession(session, domain), domain);
            diagnostics.AddRange(validation.Diagnostics);
        }

        return new SwShEditSessionValidation(
            session,
            diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error),
            diagnostics);
    }

    private ChangePlan CreateNormalSwShChangePlan(
        ProjectPaths paths,
        EditSession session,
        IReadOnlyList<EditSessionDomain> domains)
    {
        var validation = ValidateNormalSwShDomains(paths, session, domains);
        var diagnostics = validation.Diagnostics.ToList();
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var writes = new List<PlannedFileWrite>();
        foreach (var domain in domains)
        {
            var domainPlan = CreateSingleSwShChangePlan(paths, SliceSession(session, domain), domain);
            diagnostics.AddRange(domainPlan.Diagnostics);
            writes.AddRange(domainPlan.Writes);
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        return SwShChangePlanSourceGuard.Capture(
            paths,
            new ChangePlan(session.Id, CombinePlannedWrites(writes), diagnostics));
    }

    private ApplyResult ApplyNormalSwShChangePlan(
        ProjectPaths paths,
        EditSession session,
        ChangePlan reviewedPlan,
        IReadOnlyList<EditSessionDomain> domains)
    {
        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var currentPlan = CreateNormalSwShChangePlan(paths, session, domains);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();

        if (!ReviewedPlanMatchesCurrentPlan(reviewedPlan, currentPlan))
        {
            diagnostics.Add(new ValidationDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed change plan is stale. Review the change plan again before applying.",
                Domain: "workflow.editSession",
                Expected: "Current reviewed Sword/Shield change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateCombinedApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        SwShOutputRollbackScope? rollbackScope = null;
        if (domains.Count > 1
            && !SwShOutputRollbackScope.TryCapture(
                paths,
                currentPlan.Writes.Select(write => write.TargetRelativePath),
                out rollbackScope,
                out var captureFailure))
        {
            diagnostics.Add(new ValidationDiagnostic(
                DiagnosticSeverity.Error,
                $"Combined Sword/Shield apply could not snapshot output before apply: {captureFailure?.Message ?? "Unknown snapshot error."}",
                Domain: "workflow.editSession",
                File: string.IsNullOrWhiteSpace(captureFailure?.RelativePath) ? null : captureFailure.RelativePath,
                Expected: "Readable existing outputs and writable temporary storage"));
            return CreateCombinedApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        using (rollbackScope)
        {
            try
            {
                for (var index = 0; index < domains.Count; index++)
                {
                    var domain = domains[index];
                    NormalSwShApplyMutationHook?.Invoke(index, GetEditSessionDomainName(domain));
                    var domainSession = SliceSession(session, domain);
                    var domainPlan = CreateSingleSwShChangePlan(paths, domainSession, domain);
                    var result = ApplySingleSwShChangePlan(paths, domainSession, domainPlan, domain);
                    diagnostics.AddRange(result.Diagnostics);
                    writtenFiles.AddRange(result.WrittenFiles);

                    if (result.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                    {
                        break;
                    }

                    ClearCriticalSwShApplyCaches();
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(new ValidationDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Combined Sword/Shield apply failed: {exception.Message}",
                    Domain: "workflow.editSession",
                    Expected: "All selected editor changes applied together"));
            }

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                && rollbackScope is not null)
            {
                RollbackCombinedSwShApply(rollbackScope, writtenFiles, diagnostics);
                ClearCriticalSwShApplyCaches();
            }
            else
            {
                rollbackScope?.Commit();
            }
        }

        return CreateCombinedApplyResult(
            applyId,
            appliedAt,
            currentPlan,
            writtenFiles.Distinct().ToArray(),
            diagnostics);
    }

    private static void RollbackCombinedSwShApply(
        SwShOutputRollbackScope rollbackScope,
        ICollection<ProjectFileReference> writtenFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var rollbackFailures = rollbackScope.Rollback();
        writtenFiles.Clear();
        if (rollbackFailures.Count == 0)
        {
            diagnostics.Add(new ValidationDiagnostic(
                DiagnosticSeverity.Info,
                "Combined Sword/Shield apply failed and all output changes were rolled back.",
                Domain: "workflow.editSession"));
            return;
        }

        foreach (var failure in rollbackFailures)
        {
            diagnostics.Add(new ValidationDiagnostic(
                DiagnosticSeverity.Error,
                $"Combined Sword/Shield apply rollback failed: {failure.Message}",
                Domain: "workflow.editSession",
                File: string.IsNullOrWhiteSpace(failure.RelativePath) ? null : failure.RelativePath,
                Expected: "Output restored to its exact pre-apply state"));
            if (!string.IsNullOrWhiteSpace(failure.RelativePath))
            {
                writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, failure.RelativePath));
            }
        }
    }

    private void ClearCriticalSwShApplyCaches()
    {
        projectWorkspaceService.ClearMemoryCache();
        swShWorkflowService.ClearMemoryCaches(clearReusableDataCaches: true);
        exeFsPatchEditSessionService.ClearMemoryCache();
        royalCandyEditSessionService.ClearMemoryCache();
    }

    private static ApplyResult CreateCombinedApplyResult(
        string applyId,
        DateTimeOffset appliedAt,
        ChangePlan currentPlan,
        IReadOnlyList<ProjectFileReference> writtenFiles,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new ApplyResult(
            applyId,
            appliedAt,
            writtenFiles,
            new WriteManifest(applyId, appliedAt, currentPlan.Writes),
            diagnostics);
    }

    private static bool TryGetNormalSwShDomains(
        EditSession session,
        out IReadOnlyList<EditSessionDomain> domains)
    {
        var orderedDomains = session.PendingEdits
            .Select(edit => GetEditSessionDomain(edit.Domain))
            .Where(domain => domain != EditSessionDomain.None)
            .Distinct()
            .ToList();

        OrderDomainsBySourceDependencies(orderedDomains);

        domains = orderedDomains;
        return orderedDomains.Count > 1 && orderedDomains.All(IsNormalSwShDomain);
    }

    private static void OrderDomainsBySourceDependencies(List<EditSessionDomain> orderedDomains)
    {
        var remainingDomains = orderedDomains.ToList();
        orderedDomains.Clear();

        while (remainingDomains.Count > 0)
        {
            var nextIndex = remainingDomains.FindIndex(domain =>
                !HasPendingSourceDependency(domain, remainingDomains));
            if (nextIndex < 0)
            {
                orderedDomains.AddRange(remainingDomains);
                return;
            }

            orderedDomains.Add(remainingDomains[nextIndex]);
            remainingDomains.RemoveAt(nextIndex);
        }
    }

    private static bool HasPendingSourceDependency(
        EditSessionDomain domain,
        IReadOnlyCollection<EditSessionDomain> remainingDomains)
    {
        return domain switch
        {
            EditSessionDomain.Items => remainingDomains.Any(candidate => candidate is
                EditSessionDomain.Shops or
                EditSessionDomain.RaidRewards or
                EditSessionDomain.RaidBonusRewards),
            EditSessionDomain.Text => remainingDomains.Any(candidate => candidate is
                EditSessionDomain.RaidRewards or
                EditSessionDomain.RaidBonusRewards),
            _ => false,
        };
    }

    private static EditSessionDomain GetEditSessionDomain(string? domain)
    {
        return domain switch
        {
            "workflow.items" => EditSessionDomain.Items,
            "workflow.moves" => EditSessionDomain.Moves,
            "workflow.text" => EditSessionDomain.Text,
            "workflow.pokemon" => EditSessionDomain.Pokemon,
            "workflow.trainers" => EditSessionDomain.Trainers,
            "workflow.shops" => EditSessionDomain.Shops,
            "workflow.encounters" => EditSessionDomain.Encounters,
            "workflow.exefsPatches" => EditSessionDomain.ExeFsPatches,
            "workflow.bagHook" => EditSessionDomain.BagHook,
            "workflow.catchCap" => EditSessionDomain.CatchCap,
            "workflow.hyperTraining" => EditSessionDomain.HyperTraining,
            "workflow.shinyRate" => EditSessionDomain.ShinyRate,
            "workflow.typeChart" => EditSessionDomain.TypeChart,
            "workflow.fairyGymBoosts" => EditSessionDomain.FairyGymBoosts,
            "workflow.fashionUnlock" => EditSessionDomain.FashionUnlock,
            "workflow.gymUniformRemoval" => EditSessionDomain.GymUniformRemoval,
            "workflow.ivScreen" => EditSessionDomain.IvScreen,
            "workflow.giftPokemon" => EditSessionDomain.GiftPokemon,
            "workflow.tradePokemon" => EditSessionDomain.TradePokemon,
            "workflow.rentalPokemon" => EditSessionDomain.RentalPokemon,
            "workflow.dynamaxAdventures" => EditSessionDomain.DynamaxAdventures,
            "workflow.staticEncounters" => EditSessionDomain.StaticEncounters,
            "workflow.placement" => EditSessionDomain.Placement,
            "workflow.behavior" => EditSessionDomain.Behavior,
            "workflow.raidBattles" => EditSessionDomain.RaidBattles,
            "workflow.raidRewards" => EditSessionDomain.RaidRewards,
            "workflow.raidBonusRewards" => EditSessionDomain.RaidBonusRewards,
            "workflow.royalCandy" => EditSessionDomain.RoyalCandy,
            "workflow.startingItems" => EditSessionDomain.StartingItems,
            "workflow.npcItemGift" => EditSessionDomain.NpcItemGift,
            "workflow.battleCafeRewards" => EditSessionDomain.BattleCafeRewards,
            null or "" => EditSessionDomain.None,
            _ => EditSessionDomain.Mixed,
        };
    }

    private static bool IsNormalSwShDomain(EditSessionDomain domain)
    {
        return domain is
            EditSessionDomain.Items or
            EditSessionDomain.Moves or
            EditSessionDomain.Text or
            EditSessionDomain.Pokemon or
            EditSessionDomain.Trainers or
            EditSessionDomain.Shops or
            EditSessionDomain.Encounters or
            EditSessionDomain.GiftPokemon or
            EditSessionDomain.TradePokemon or
            EditSessionDomain.RentalPokemon or
            EditSessionDomain.StaticEncounters or
            EditSessionDomain.Placement or
            EditSessionDomain.Behavior or
            EditSessionDomain.RaidBattles or
            EditSessionDomain.RaidRewards or
            EditSessionDomain.RaidBonusRewards;
    }

    private static EditSession SliceSession(EditSession session, EditSessionDomain domain)
    {
        var domainName = GetEditSessionDomainName(domain);
        return session with
        {
            PendingEdits = session.PendingEdits
                .Where(edit => string.Equals(edit.Domain, domainName, StringComparison.Ordinal))
                .ToArray(),
        };
    }

    private static string GetEditSessionDomainName(EditSessionDomain domain)
    {
        return domain switch
        {
            EditSessionDomain.Items => "workflow.items",
            EditSessionDomain.Moves => "workflow.moves",
            EditSessionDomain.Text => "workflow.text",
            EditSessionDomain.Pokemon => "workflow.pokemon",
            EditSessionDomain.Trainers => "workflow.trainers",
            EditSessionDomain.Shops => "workflow.shops",
            EditSessionDomain.Encounters => "workflow.encounters",
            EditSessionDomain.ExeFsPatches => "workflow.exefsPatches",
            EditSessionDomain.BagHook => "workflow.bagHook",
            EditSessionDomain.CatchCap => "workflow.catchCap",
            EditSessionDomain.HyperTraining => "workflow.hyperTraining",
            EditSessionDomain.ShinyRate => "workflow.shinyRate",
            EditSessionDomain.TypeChart => "workflow.typeChart",
            EditSessionDomain.FairyGymBoosts => "workflow.fairyGymBoosts",
            EditSessionDomain.FashionUnlock => "workflow.fashionUnlock",
            EditSessionDomain.GymUniformRemoval => "workflow.gymUniformRemoval",
            EditSessionDomain.IvScreen => "workflow.ivScreen",
            EditSessionDomain.GiftPokemon => "workflow.giftPokemon",
            EditSessionDomain.TradePokemon => "workflow.tradePokemon",
            EditSessionDomain.RentalPokemon => "workflow.rentalPokemon",
            EditSessionDomain.DynamaxAdventures => "workflow.dynamaxAdventures",
            EditSessionDomain.StaticEncounters => "workflow.staticEncounters",
            EditSessionDomain.Placement => "workflow.placement",
            EditSessionDomain.Behavior => "workflow.behavior",
            EditSessionDomain.RaidBattles => "workflow.raidBattles",
            EditSessionDomain.RaidRewards => "workflow.raidRewards",
            EditSessionDomain.RaidBonusRewards => "workflow.raidBonusRewards",
            EditSessionDomain.RoyalCandy => "workflow.royalCandy",
            EditSessionDomain.StartingItems => "workflow.startingItems",
            EditSessionDomain.NpcItemGift => "workflow.npcItemGift",
            EditSessionDomain.BattleCafeRewards => "workflow.battleCafeRewards",
            _ => string.Empty,
        };
    }

    private static IReadOnlyList<PlannedFileWrite> CombinePlannedWrites(IEnumerable<PlannedFileWrite> writes)
    {
        return writes
            .GroupBy(write => write.TargetRelativePath, StringComparer.Ordinal)
            .Select(group =>
            {
                var groupedWrites = group.ToArray();
                if (groupedWrites.Length == 1)
                {
                    return groupedWrites[0];
                }

                return new PlannedFileWrite(
                    group.Key,
                    groupedWrites
                        .SelectMany(write => write.Sources)
                        .Distinct()
                        .ToArray(),
                    groupedWrites.Any(write => write.ReplacesExistingOutput),
                    string.Join(
                        " ",
                        groupedWrites
                            .Select(write => write.Reason)
                            .Where(reason => !string.IsNullOrWhiteSpace(reason))
                            .Distinct(StringComparer.Ordinal)));
            })
            .OrderBy(write => write.TargetRelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool ReviewedPlanMatchesCurrentPlan(ChangePlan reviewedPlan, ChangePlan currentPlan)
    {
        return ChangePlanReview.Matches(reviewedPlan, currentPlan);
    }

    private static EditSessionDomain GetEditSessionDomain(EditSession session)
    {
        var domains = session.PendingEdits
            .Select(edit => edit.Domain)
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return domains switch
        {
            [] => EditSessionDomain.None,
            ["workflow.items"] => EditSessionDomain.Items,
            ["workflow.moves"] => EditSessionDomain.Moves,
            ["workflow.text"] => EditSessionDomain.Text,
            ["workflow.pokemon"] => EditSessionDomain.Pokemon,
            ["workflow.trainers"] => EditSessionDomain.Trainers,
            ["workflow.shops"] => EditSessionDomain.Shops,
            ["workflow.encounters"] => EditSessionDomain.Encounters,
            ["workflow.exefsPatches"] => EditSessionDomain.ExeFsPatches,
            ["workflow.bagHook"] => EditSessionDomain.BagHook,
            ["workflow.catchCap"] => EditSessionDomain.CatchCap,
            ["workflow.hyperTraining"] => EditSessionDomain.HyperTraining,
            ["workflow.shinyRate"] => EditSessionDomain.ShinyRate,
            ["workflow.typeChart"] => EditSessionDomain.TypeChart,
            ["workflow.fairyGymBoosts"] => EditSessionDomain.FairyGymBoosts,
            ["workflow.fashionUnlock"] => EditSessionDomain.FashionUnlock,
            ["workflow.gymUniformRemoval"] => EditSessionDomain.GymUniformRemoval,
            ["workflow.ivScreen"] => EditSessionDomain.IvScreen,
            ["workflow.giftPokemon"] => EditSessionDomain.GiftPokemon,
            ["workflow.tradePokemon"] => EditSessionDomain.TradePokemon,
            ["workflow.rentalPokemon"] => EditSessionDomain.RentalPokemon,
            ["workflow.dynamaxAdventures"] => EditSessionDomain.DynamaxAdventures,
            ["workflow.staticEncounters"] => EditSessionDomain.StaticEncounters,
            ["workflow.placement"] => EditSessionDomain.Placement,
            ["workflow.behavior"] => EditSessionDomain.Behavior,
            ["workflow.raidBattles"] => EditSessionDomain.RaidBattles,
            ["workflow.raidRewards"] => EditSessionDomain.RaidRewards,
            ["workflow.raidBonusRewards"] => EditSessionDomain.RaidBonusRewards,
            ["workflow.royalCandy"] => EditSessionDomain.RoyalCandy,
            ["workflow.startingItems"] => EditSessionDomain.StartingItems,
            ["workflow.npcItemGift"] => EditSessionDomain.NpcItemGift,
            ["workflow.battleCafeRewards"] => EditSessionDomain.BattleCafeRewards,
            _ => EditSessionDomain.Mixed,
        };
    }

    private static bool IsScarletViolet(ProjectPaths paths)
    {
        return paths.SelectedGame is ProjectGame.Scarlet or ProjectGame.Violet;
    }

    private static bool IsPokemonLegendsZA(ProjectPaths paths)
    {
        return paths.SelectedGame is ProjectGame.ZA;
    }

    private static string? ValidateCommandGameScope(
        BridgeCommandEnvelope? envelope,
        ProjectGame? selectedGame)
    {
        if (envelope?.Command is not { } command || selectedGame is null)
        {
            return null;
        }

        if (IsSwordShieldOnlyCommand(command)
            && !IsSwordShield(selectedGame.Value))
        {
            return SerializeFailure(
                BridgeErrorCodes.GameMismatch,
                $"Bridge command '{command}' is only available for Sword/Shield projects.",
                envelope.RequestId);
        }

        if (IsScarletVioletOnlyCommand(command)
            && !IsScarletViolet(selectedGame.Value)
            && !((command is KmCommandNames.UpdateItemFields or KmCommandNames.UpdateTrainerFields)
                && IsPokemonLegendsZA(selectedGame.Value))
            && !(command is
                    KmCommandNames.UpdateGiftPokemonFields or
                    KmCommandNames.UpdateTradePokemonFields or
                    KmCommandNames.UpdateEncounterSlotFields
                && IsPokemonLegendsZA(selectedGame.Value))
            && !(command is KmCommandNames.UpdatePlacementObjectFields
                && IsPokemonLegendsZA(selectedGame.Value))
            && !(command is KmCommandNames.StageTypeChartUninstall && IsPokemonLegendsZA(selectedGame.Value)))
        {
            return SerializeFailure(
                BridgeErrorCodes.GameMismatch,
                $"Bridge command '{command}' is only available for Scarlet/Violet projects.",
                envelope.RequestId);
        }

        if (IsPokemonLegendsZAOnlyCommand(command) && !IsPokemonLegendsZA(selectedGame.Value))
        {
            return SerializeFailure(
                BridgeErrorCodes.GameMismatch,
                $"Bridge command '{command}' is only available for Pokemon Legends Z-A projects.",
                envelope.RequestId);
        }

        if (IsPokemonLegendsZA(selectedGame.Value)
            && !IsPokemonLegendsZAAllowedCommand(command)
            && !IsChangeSetCommand(command)
            && !IsOutputSafetyCommand(command))
        {
            return SerializeFailure(
                BridgeErrorCodes.GameMismatch,
                $"Bridge command '{command}' is not available for Pokemon Legends Z-A projects yet.",
                envelope.RequestId);
        }

        return null;
    }

    private static bool TryReadSelectedGame(string requestJson, out ProjectGameDto selectedGame)
    {
        selectedGame = default;

        var request = DeserializeRequest<JsonElement>(requestJson);
        if (request?.Payload.ValueKind is not JsonValueKind.Object)
        {
            return false;
        }

        JsonElement paths;
        if (!request.Payload.TryGetProperty("paths", out paths)
            && !request.Payload.TryGetProperty("activePaths", out paths))
        {
            if (!request.Payload.TryGetProperty("scope", out var scope)
                || scope.ValueKind is not JsonValueKind.Object
                || !scope.TryGetProperty("paths", out paths))
            {
                return false;
            }
        }

        if (paths.ValueKind is not JsonValueKind.Object
            || !paths.TryGetProperty("selectedGame", out var selectedGameJson)
            || selectedGameJson.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return false;
        }

        ProjectGameDto? parsedGame;
        try
        {
            parsedGame = selectedGameJson.Deserialize<ProjectGameDto?>(BridgeJson.SerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new BridgeRequestException(
                "Bridge request selectedGame is invalid.",
                exception,
                BridgeErrorCodes.InvalidJson);
        }

        if (parsedGame is null)
        {
            return false;
        }

        if (!Enum.IsDefined(parsedGame.Value))
        {
            throw new BridgeRequestException(
                "Bridge request selectedGame is invalid.",
                code: BridgeErrorCodes.InvalidJson);
        }

        selectedGame = parsedGame.Value;
        return true;
    }

    private static bool IsSwordShield(ProjectGame game)
    {
        return game is ProjectGame.Sword or ProjectGame.Shield;
    }

    private static bool IsScarletViolet(ProjectGame game)
    {
        return game is ProjectGame.Scarlet or ProjectGame.Violet;
    }

    private static bool IsPokemonLegendsZA(ProjectGame game)
    {
        return game is ProjectGame.ZA;
    }

    private static ProjectGame ToCore(ProjectGameDto game)
    {
        return game switch
        {
            ProjectGameDto.Sword => ProjectGame.Sword,
            ProjectGameDto.Shield => ProjectGame.Shield,
            ProjectGameDto.Scarlet => ProjectGame.Scarlet,
            ProjectGameDto.Violet => ProjectGame.Violet,
            ProjectGameDto.ZA => ProjectGame.ZA,
            _ => throw new ArgumentOutOfRangeException(nameof(game), game, null),
        };
    }

    private GuidedDesignStagingResult StageGuidedDesignEdits(
        ProjectPathsDto pathsDto,
        IReadOnlyList<GuidedDesignStagingEdit> edits)
    {
        if (pathsDto is null
            || edits is null
            || edits.Count is 0 or > ChangeSetContract.MaximumOperationsPerChangeSet
            || edits.Any(edit => edit is null || edit.Record is null))
        {
            return InvalidGuidedDesignStaging();
        }

        var paths = ProjectBridgeMapper.ToCore(pathsDto);
        if (paths.SelectedGame is null)
        {
            return InvalidGuidedDesignStaging();
        }

        var family = ToSemanticFamily(paths.SelectedGame.Value);
        if (edits.Any(edit => edit.Record.GameFamily != family)
            || edits.Select(edit => edit.Record.Domain).Distinct(StringComparer.Ordinal).Count() != 1)
        {
            return InvalidGuidedDesignStaging();
        }

        return edits[0].Record.Domain switch
        {
            "workflow.items" => StageGuidedItemEdits(paths, family, edits),
            "workflow.pokemon" => StageGuidedPokemonEdits(paths, family, edits),
            "workflow.moves" => StageGeneratedMoveEdits(paths, family, edits),
            "workflow.trainers" => StageGuidedTrainerEdits(paths, family, edits),
            "workflow.encounters" => StageGuidedEncounterEdits(paths, family, edits),
            _ => InvalidGuidedDesignStaging(),
        };
    }

    private GuidedDesignStagingResult StageGuidedItemEdits(
        ProjectPaths paths,
        SemanticGameFamilyDto family,
        IReadOnlyList<GuidedDesignStagingEdit> edits)
    {
        if (!TryScalarEdits(edits, "workflow.items", "item", family, requireSubrecord: false, out var scalar)
            || scalar.Any(edit => !TryParseCanonicalPositive(edit.Record.RecordId, out _)))
        {
            return InvalidGuidedDesignStaging();
        }

        var session = EditSession.Start();
        if (family == SemanticGameFamilyDto.LegendsZA)
        {
            var result = zaWorkflowService.UpdateItemFieldsFreshBounded(
                paths,
                session,
                scalar.Select(edit => new ZaItemFieldUpdate(
                    ParseCanonicalPositive(edit.Record.RecordId),
                    edit.Field,
                    edit.Value.ToString(CultureInfo.InvariantCulture))).ToArray());
            return CompleteGuidedDesignStaging(result.Session, result.Diagnostics);
        }

        if (family == SemanticGameFamilyDto.ScarletViolet)
        {
            var result = svWorkflowService.UpdateItemFieldsFreshBounded(
                paths,
                session,
                scalar.Select(edit => new SvItemFieldUpdate(
                    ParseCanonicalPositive(edit.Record.RecordId),
                    edit.Field,
                    edit.Value.ToString(CultureInfo.InvariantCulture))).ToArray());
            return CompleteGuidedDesignStaging(result.Session, result.Diagnostics);
        }

        var swsh = swShWorkflowService.UpdateItemFieldsFreshBounded(
            paths,
            session,
            scalar.Select(edit => new SwShItemFieldUpdate(
                ParseCanonicalPositive(edit.Record.RecordId),
                edit.Field,
                edit.Value.ToString(CultureInfo.InvariantCulture))).ToArray());
        return CompleteGuidedDesignStaging(swsh.Session, swsh.Diagnostics);
    }

    private GuidedDesignStagingResult StageGuidedPokemonEdits(
        ProjectPaths paths,
        SemanticGameFamilyDto family,
        IReadOnlyList<GuidedDesignStagingEdit> edits)
    {
        if (edits.All(edit => edit is GuidedDesignEvolutionStagingEdit))
        {
            return StageGuidedEvolutionEdits(paths, family, edits.Cast<GuidedDesignEvolutionStagingEdit>().ToArray());
        }

        if (!TryScalarEdits(edits, "workflow.pokemon", "pokemon-personal", family, requireSubrecord: false, out var scalar)
            || scalar.Any(edit => !TryParseCanonicalPositive(edit.Record.RecordId, out _)))
        {
            return InvalidGuidedDesignStaging();
        }

        var session = EditSession.Start();
        if (family == SemanticGameFamilyDto.LegendsZA)
        {
            var result = zaWorkflowService.UpdatePokemonFieldsFreshBounded(
                paths,
                session,
                scalar.Select(edit => new KM.ZA.Pokemon.ZaPokemonFieldUpdate(
                    ParseCanonicalPositive(edit.Record.RecordId),
                    edit.Field,
                    edit.Value.ToString(CultureInfo.InvariantCulture))).ToArray());
            return CompleteGuidedDesignStaging(result.Session, result.Diagnostics);
        }

        if (family == SemanticGameFamilyDto.ScarletViolet)
        {
            var result = svWorkflowService.UpdatePokemonFieldsFreshBounded(
                paths,
                session,
                scalar.Select(edit => new SvPokemonFieldUpdate(
                    ParseCanonicalPositive(edit.Record.RecordId),
                    edit.Field,
                    edit.Value.ToString(CultureInfo.InvariantCulture))).ToArray());
            return CompleteGuidedDesignStaging(result.Session, result.Diagnostics);
        }

        var swsh = swShWorkflowService.UpdatePokemonFieldsFreshBounded(
            paths,
            session,
            scalar.Select(edit => new SwShPokemonFieldUpdate(
                ParseCanonicalPositive(edit.Record.RecordId),
                edit.Field,
                edit.Value.ToString(CultureInfo.InvariantCulture))).ToArray());
        return CompleteGuidedDesignStaging(swsh.Session, swsh.Diagnostics);
    }

    private GuidedDesignStagingResult StageGeneratedMoveEdits(
        ProjectPaths paths,
        SemanticGameFamilyDto family,
        IReadOnlyList<GuidedDesignStagingEdit> edits)
    {
        if (family != SemanticGameFamilyDto.SwordShield
            || !TryScalarEdits(
                edits,
                "workflow.moves",
                "move",
                family,
                requireSubrecord: false,
                out var scalar)
            || scalar.Any(edit => !TryParseCanonicalNonNegative(edit.Record.RecordId, out _)))
        {
            return InvalidGuidedDesignStaging();
        }

        var result = swShWorkflowService.UpdateMoveFieldsFreshBounded(
            paths,
            EditSession.Start(),
            scalar.Select(edit => new SwShMoveFieldUpdate(
                ParseCanonicalNonNegative(edit.Record.RecordId),
                edit.Field,
                edit.Value.ToString(CultureInfo.InvariantCulture))).ToArray());
        return CompleteGuidedDesignStaging(result.Session, result.Diagnostics);
    }

    private GuidedDesignStagingResult StageGuidedEvolutionEdits(
        ProjectPaths paths,
        SemanticGameFamilyDto family,
        IReadOnlyList<GuidedDesignEvolutionStagingEdit> edits)
    {
        if (family != SemanticGameFamilyDto.LegendsZA
            || edits.Any(edit =>
                !MatchesGuidedRecord(edit.Record, family, "workflow.pokemon", "pokemon-personal", requireSubrecord: false)
                || !TryParseCanonicalPositive(edit.Record.RecordId, out _)
                || edit.Slot < 0))
        {
            return InvalidGuidedDesignStaging();
        }

        var result = zaWorkflowService.UpdatePokemonEvolutionsFreshBounded(
            paths,
            EditSession.Start(),
            edits
                .OrderBy(edit => ParseCanonicalPositive(edit.Record.RecordId))
                .ThenBy(edit => edit.Slot)
                .Select(edit => new KM.ZA.Pokemon.ZaPokemonEvolutionUpdate(
                    ParseCanonicalPositive(edit.Record.RecordId),
                    edit.Slot,
                    edit.Method,
                    edit.Argument,
                    edit.Species,
                    edit.Form,
                    edit.Level))
                .ToArray());
        return CompleteGuidedDesignStaging(result.Session, result.Diagnostics);
    }

    private GuidedDesignStagingResult StageGuidedTrainerEdits(
        ProjectPaths paths,
        SemanticGameFamilyDto family,
        IReadOnlyList<GuidedDesignStagingEdit> edits)
    {
        if (family == SemanticGameFamilyDto.SwordShield
            || !TryScalarEdits(edits, "workflow.trainers", "trainer", family, requireSubrecord: true, out var scalar)
            || scalar.Any(edit =>
                !TryParseCanonicalNonNegative(edit.Record.RecordId, out _)
                || !TryParseSubrecord(edit.Record.SubrecordId, "party-slot:", out _)))
        {
            return InvalidGuidedDesignStaging();
        }

        var session = EditSession.Start();
        if (family == SemanticGameFamilyDto.LegendsZA)
        {
            var result = zaWorkflowService.UpdateTrainerFieldsFreshBounded(
                paths,
                session,
                scalar.Select(edit => new KM.ZA.Trainers.ZaTrainerFieldUpdate(
                    ParseCanonicalNonNegative(edit.Record.RecordId),
                    ParseSubrecord(edit.Record.SubrecordId, "party-slot:"),
                    edit.Field,
                    edit.Value.ToString(CultureInfo.InvariantCulture))).ToArray());
            return CompleteGuidedDesignStaging(result.Session, result.Diagnostics);
        }

        var sv = svWorkflowService.UpdateTrainerFieldsFreshBounded(
            paths,
            session,
            scalar.Select(edit => new SvTrainerFieldUpdate(
                ParseCanonicalNonNegative(edit.Record.RecordId),
                ParseSubrecord(edit.Record.SubrecordId, "party-slot:"),
                edit.Field,
                edit.Value.ToString(CultureInfo.InvariantCulture))).ToArray());
        return CompleteGuidedDesignStaging(sv.Session, sv.Diagnostics);
    }

    private GuidedDesignStagingResult StageGuidedEncounterEdits(
        ProjectPaths paths,
        SemanticGameFamilyDto family,
        IReadOnlyList<GuidedDesignStagingEdit> edits)
    {
        if (!TryScalarEdits(edits, "workflow.encounters", "encounter-table", family, requireSubrecord: true, out var scalar)
            || scalar.Any(edit =>
                string.IsNullOrEmpty(edit.Record.RecordId)
                || !TryParseSubrecord(edit.Record.SubrecordId, "slot:", out var slot)
                || family == SemanticGameFamilyDto.SwordShield && slot == 0))
        {
            return InvalidGuidedDesignStaging();
        }

        var session = EditSession.Start();
        if (family == SemanticGameFamilyDto.LegendsZA)
        {
            var result = zaWorkflowService.UpdateEncounterSlotFieldsFreshBounded(
                paths,
                session,
                scalar.Select(edit => new ZaEncounterSlotFieldUpdate(
                    edit.Record.RecordId,
                    ParseSubrecord(edit.Record.SubrecordId, "slot:"),
                    edit.Field,
                    edit.Value.ToString(CultureInfo.InvariantCulture))).ToArray());
            return CompleteGuidedDesignStaging(result.Session, result.Diagnostics);
        }

        if (family == SemanticGameFamilyDto.ScarletViolet)
        {
            var result = svWorkflowService.UpdateEncounterSlotFieldsFreshBounded(
                paths,
                session,
                scalar.Select(edit => new SvEncounterSlotFieldUpdate(
                    edit.Record.RecordId,
                    ParseSubrecord(edit.Record.SubrecordId, "slot:"),
                    edit.Field,
                    edit.Value.ToString(CultureInfo.InvariantCulture))).ToArray());
            return CompleteGuidedDesignStaging(result.Session, result.Diagnostics);
        }

        var swsh = swShWorkflowService.UpdateEncounterSlotFieldsFreshBounded(
            paths,
            session,
            scalar.Select(edit => new SwShEncounterSlotFieldUpdate(
                edit.Record.RecordId,
                ParseSubrecord(edit.Record.SubrecordId, "slot:"),
                edit.Field,
                edit.Value.ToString(CultureInfo.InvariantCulture))).ToArray());
        return CompleteGuidedDesignStaging(swsh.Session, swsh.Diagnostics);
    }

    private static bool TryScalarEdits(
        IReadOnlyList<GuidedDesignStagingEdit> edits,
        string domain,
        string recordKind,
        SemanticGameFamilyDto family,
        bool requireSubrecord,
        out GuidedDesignScalarStagingEdit[] scalar)
    {
        scalar = edits.OfType<GuidedDesignScalarStagingEdit>().ToArray();
        return scalar.Length == edits.Count
            && scalar.All(edit => MatchesGuidedRecord(
                edit.Record,
                family,
                domain,
                recordKind,
                requireSubrecord));
    }

    private static bool MatchesGuidedRecord(
        SemanticRecordRefDto record,
        SemanticGameFamilyDto family,
        string domain,
        string recordKind,
        bool requireSubrecord) =>
        record is not null
        && record.RecordKind is not null
        && record.GameFamily == family
        && string.Equals(record.Domain, domain, StringComparison.Ordinal)
        && string.Equals(record.RecordKind.Key, recordKind, StringComparison.Ordinal)
        && record.RecordKind.SchemaVersion == 1
        && (requireSubrecord ? record.SubrecordId is not null : record.SubrecordId is null);

    private static SemanticGameFamilyDto ToSemanticFamily(ProjectGame game) => game switch
    {
        ProjectGame.Sword or ProjectGame.Shield => SemanticGameFamilyDto.SwordShield,
        ProjectGame.Scarlet or ProjectGame.Violet => SemanticGameFamilyDto.ScarletViolet,
        ProjectGame.ZA => SemanticGameFamilyDto.LegendsZA,
        _ => throw new ArgumentOutOfRangeException(nameof(game), game, null),
    };

    private static bool TryParseCanonicalNonNegative(string value, out int parsed) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed)
        && parsed >= 0
        && string.Equals(parsed.ToString(CultureInfo.InvariantCulture), value, StringComparison.Ordinal);

    private static bool TryParseCanonicalPositive(string value, out int parsed) =>
        TryParseCanonicalNonNegative(value, out parsed) && parsed > 0;

    private static int ParseCanonicalNonNegative(string value) =>
        TryParseCanonicalNonNegative(value, out var parsed) ? parsed : -1;

    private static int ParseCanonicalPositive(string value) =>
        TryParseCanonicalPositive(value, out var parsed) ? parsed : -1;

    private static bool TryParseSubrecord(string? value, string prefix, out int parsed)
    {
        parsed = -1;
        return value is not null
            && value.StartsWith(prefix, StringComparison.Ordinal)
            && TryParseCanonicalNonNegative(value[prefix.Length..], out parsed)
            && string.Equals(
                value,
                $"{prefix}{parsed.ToString(CultureInfo.InvariantCulture)}",
                StringComparison.Ordinal);
    }

    private static int ParseSubrecord(string? value, string prefix) =>
        TryParseSubrecord(value, prefix, out var parsed) ? parsed : -1;

    private static GuidedDesignStagingResult CompleteGuidedDesignStaging(
        EditSession session,
        IReadOnlyList<ValidationDiagnostic> diagnostics) =>
        HasErrors(diagnostics)
            ? InvalidGuidedDesignStaging()
            : new GuidedDesignStagingResult(
                session with
                {
                    PendingEdits = session.PendingEdits
                        .Select(edit => edit with
                        {
                            Owner = GuidedDesignProviders.GeneratedEditOwner,
                        })
                        .ToArray(),
                },
                IsValid: true);

    private static bool HasErrors(IReadOnlyList<ValidationDiagnostic> diagnostics) =>
        diagnostics is null
        || diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

    private static GuidedDesignStagingResult InvalidGuidedDesignStaging() =>
        new(EditSession.Start(), IsValid: false);

    private ItemsWorkflowDto LoadSemanticExploreItemsFresh(ProjectPathsDto pathsDto)
    {
        var paths = ProjectBridgeMapper.ToCore(pathsDto);
        if (IsPokemonLegendsZA(paths))
        {
            return ZaBridgeMapper.ToDto(zaWorkflowService.LoadSemanticExploreItems(paths)).Workflow;
        }

        if (IsScarletViolet(paths))
        {
            return SvBridgeMapper.ToDto(svWorkflowService.LoadSemanticExploreItems(paths)).Workflow;
        }

        if (paths.SelectedGame is ProjectGame.Sword or ProjectGame.Shield)
        {
            return SwShBridgeMapper.ToDto(swShWorkflowService.LoadSemanticExploreItems(paths)).Workflow;
        }

        throw new SemanticExploreValidationException(
            "The selected semantic game is unsupported.",
            SemanticExploreFailureKind.Unsupported);
    }

    private PokemonWorkflowDto LoadSemanticExplorePokemonFresh(ProjectPathsDto pathsDto)
    {
        var paths = ProjectBridgeMapper.ToCore(pathsDto);
        if (IsPokemonLegendsZA(paths))
        {
            return ZaBridgeMapper.ToDto(zaWorkflowService.LoadSemanticExplorePokemon(paths)).Workflow;
        }

        if (IsScarletViolet(paths))
        {
            return SvBridgeMapper.ToDto(svWorkflowService.LoadSemanticExplorePokemon(paths)).Workflow;
        }

        if (paths.SelectedGame is ProjectGame.Sword or ProjectGame.Shield)
        {
            return SwShBridgeMapper.ToDto(swShWorkflowService.LoadSemanticExplorePokemon(paths)).Workflow;
        }

        throw new SemanticExploreValidationException(
            "The selected semantic game is unsupported.",
            SemanticExploreFailureKind.Unsupported);
    }

    private MovesWorkflowDto LoadSemanticExploreMovesFresh(ProjectPathsDto pathsDto)
    {
        var paths = ProjectBridgeMapper.ToCore(pathsDto);
        if (IsPokemonLegendsZA(paths))
        {
            return ZaBridgeMapper.ToDto(zaWorkflowService.LoadSemanticExploreMoves(paths)).Workflow;
        }

        if (IsScarletViolet(paths))
        {
            return SvBridgeMapper.ToDto(svWorkflowService.LoadSemanticExploreMoves(paths)).Workflow;
        }

        if (paths.SelectedGame is ProjectGame.Sword or ProjectGame.Shield)
        {
            return SwShBridgeMapper.ToDto(swShWorkflowService.LoadSemanticExploreMoves(paths)).Workflow;
        }

        throw new SemanticExploreValidationException(
            "The selected semantic game is unsupported.",
            SemanticExploreFailureKind.Unsupported);
    }

    private string CaptureSemanticExploreSourceFingerprint(ProjectPathsDto pathsDto)
    {
        var paths = ProjectBridgeMapper.ToCore(pathsDto);
        if (IsPokemonLegendsZA(paths))
        {
            return zaWorkflowService.CaptureSemanticExploreSourceFingerprint(paths);
        }

        if (IsScarletViolet(paths))
        {
            return svWorkflowService.CaptureSemanticExploreSourceFingerprint(paths);
        }

        if (paths.SelectedGame is ProjectGame.Sword or ProjectGame.Shield)
        {
            return swShWorkflowService.CaptureSemanticExploreSourceFingerprint(paths);
        }

        throw new SemanticExploreValidationException(
            "The selected semantic game is unsupported.",
            SemanticExploreFailureKind.Unsupported);
    }

    private bool CanLoadSemanticExploreCorporaConcurrently(ProjectPathsDto pathsDto)
    {
        var paths = ProjectBridgeMapper.ToCore(pathsDto);
        if (IsPokemonLegendsZA(paths))
        {
            return zaWorkflowService.CanLoadSemanticExploreCorporaConcurrently;
        }

        if (IsScarletViolet(paths))
        {
            return svWorkflowService.CanLoadSemanticExploreCorporaConcurrently;
        }

        return paths.SelectedGame is ProjectGame.Sword or ProjectGame.Shield;
    }

    private SemanticWorkflowDtoLoaders PrepareSemanticExploreCorporaFresh(
        ProjectPathsDto pathsDto,
        int maximumParallelism)
    {
        var paths = ProjectBridgeMapper.ToCore(pathsDto);
        if (IsPokemonLegendsZA(paths))
        {
            var loaders = zaWorkflowService.PrepareSemanticExploreCorpora(
                paths,
                maximumParallelism);
            return PrepareSemanticWorkflowDtos(
                () => ZaBridgeMapper.ToDto(loaders.Items()).Workflow,
                () => ZaBridgeMapper.ToDto(loaders.Pokemon()).Workflow,
                () => ZaBridgeMapper.ToDto(loaders.Moves()).Workflow,
                maximumParallelism);
        }

        if (IsScarletViolet(paths))
        {
            var loaders = svWorkflowService.PrepareSemanticExploreCorpora(
                paths,
                maximumParallelism);
            return PrepareSemanticWorkflowDtos(
                () => SvBridgeMapper.ToDto(loaders.Items()).Workflow,
                () => SvBridgeMapper.ToDto(loaders.Pokemon()).Workflow,
                () => SvBridgeMapper.ToDto(loaders.Moves()).Workflow,
                maximumParallelism);
        }

        if (paths.SelectedGame is ProjectGame.Sword or ProjectGame.Shield)
        {
            return PrepareSemanticWorkflowDtos(
                () => SwShBridgeMapper.ToDto(
                    swShWorkflowService.LoadSemanticExploreItems(paths)).Workflow,
                () => SwShBridgeMapper.ToDto(
                    swShWorkflowService.LoadSemanticExplorePokemon(paths)).Workflow,
                () => SwShBridgeMapper.ToDto(
                    swShWorkflowService.LoadSemanticExploreMoves(paths)).Workflow,
                maximumParallelism);
        }

        throw new SemanticExploreValidationException(
            "The selected semantic game is unsupported.",
            SemanticExploreFailureKind.Unsupported);
    }

    private static SemanticWorkflowDtoLoaders PrepareSemanticWorkflowDtos(
        Func<ItemsWorkflowDto> loadItems,
        Func<PokemonWorkflowDto> loadPokemon,
        Func<MovesWorkflowDto> loadMoves,
        int maximumParallelism)
    {
        ArgumentNullException.ThrowIfNull(loadItems);
        ArgumentNullException.ThrowIfNull(loadPokemon);
        ArgumentNullException.ThrowIfNull(loadMoves);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumParallelism);

        PreparedSemanticWorkflowDto<ItemsWorkflowDto>? items = null;
        PreparedSemanticWorkflowDto<PokemonWorkflowDto>? pokemon = null;
        PreparedSemanticWorkflowDto<MovesWorkflowDto>? moves = null;

        void LoadAt(int index)
        {
            switch (index)
            {
                case 0:
                    items = CaptureSemanticWorkflowDto(loadItems);
                    break;
                case 1:
                    pokemon = CaptureSemanticWorkflowDto(loadPokemon);
                    break;
                case 2:
                    moves = CaptureSemanticWorkflowDto(loadMoves);
                    break;
            }
        }

        var effectiveParallelism = Math.Clamp(maximumParallelism, 1, 3);
        _ = BoundedParallel.For(
            3,
            CreateBridgePolicy(
                "bridge-semantic-workflow-dto-load",
                BoundedWorkloadKind.Decode,
                effectiveParallelism),
            LoadAt);

        return new SemanticWorkflowDtoLoaders(
            (items ?? throw new InvalidOperationException(
                "The semantic items workflow DTO was not prepared.")).Get,
            (pokemon ?? throw new InvalidOperationException(
                "The semantic Pokemon workflow DTO was not prepared.")).Get,
            (moves ?? throw new InvalidOperationException(
                "The semantic moves workflow DTO was not prepared.")).Get);
    }

    private GuidedDesignWorkflowDtoLoaders PrepareGuidedDesignSourcesFresh(
        ProjectPathsDto pathsDto,
        int maximumParallelism)
    {
        var paths = ProjectBridgeMapper.ToCore(pathsDto);
        if (IsPokemonLegendsZA(paths))
        {
            var loaders = zaWorkflowService.PrepareGuidedDesignSources(
                paths,
                maximumParallelism);
            return PrepareGuidedDesignWorkflowDtos(
                () => ZaBridgeMapper.ToDto(loaders.Trainers()).Workflow,
                () => ZaBridgeMapper.ToDto(loaders.Encounters()).Workflow,
                () => ZaBridgeMapper.ToDto(loaders.Items()).Workflow,
                () => ZaBridgeMapper.ToDto(loaders.Pokemon()).Workflow,
                maximumParallelism,
                includeTrainers: true);
        }

        if (IsScarletViolet(paths))
        {
            var loaders = svWorkflowService.PrepareGuidedDesignSources(
                paths,
                maximumParallelism);
            return PrepareGuidedDesignWorkflowDtos(
                () => SvBridgeMapper.ToDto(loaders.Trainers()).Workflow,
                () => SvBridgeMapper.ToDto(loaders.Encounters()).Workflow,
                () => SvBridgeMapper.ToDto(loaders.Items()).Workflow,
                () => SvBridgeMapper.ToDto(loaders.Pokemon()).Workflow,
                maximumParallelism,
                includeTrainers: true);
        }

        if (paths.SelectedGame is ProjectGame.Sword or ProjectGame.Shield)
        {
            return PrepareGuidedDesignWorkflowDtos(
                () => throw new NotSupportedException(
                    "Sword and Shield Guided Design does not load trainer sources."),
                () => SwShBridgeMapper.ToDto(
                    swShWorkflowService.LoadBalanceLabEncounters(paths)).Workflow,
                () => SwShBridgeMapper.ToDto(
                    swShWorkflowService.LoadSemanticExploreItems(paths)).Workflow,
                () => SwShBridgeMapper.ToDto(
                    swShWorkflowService.LoadSemanticExplorePokemon(paths)).Workflow,
                maximumParallelism,
                includeTrainers: false);
        }

        throw new SemanticExploreValidationException(
            "The selected Guided Design game is unsupported.",
            SemanticExploreFailureKind.Unsupported);
    }

    private static GuidedDesignWorkflowDtoLoaders PrepareGuidedDesignWorkflowDtos(
        Func<TrainersWorkflowDto> loadTrainers,
        Func<EncountersWorkflowDto> loadEncounters,
        Func<ItemsWorkflowDto> loadItems,
        Func<PokemonWorkflowDto> loadPokemon,
        int maximumParallelism,
        bool includeTrainers)
    {
        ArgumentNullException.ThrowIfNull(loadTrainers);
        ArgumentNullException.ThrowIfNull(loadEncounters);
        ArgumentNullException.ThrowIfNull(loadItems);
        ArgumentNullException.ThrowIfNull(loadPokemon);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumParallelism);

        const int sourceCount = 4;
        PreparedSemanticWorkflowDto<TrainersWorkflowDto>? trainers = null;
        PreparedSemanticWorkflowDto<EncountersWorkflowDto>? encounters = null;
        PreparedSemanticWorkflowDto<ItemsWorkflowDto>? items = null;
        PreparedSemanticWorkflowDto<PokemonWorkflowDto>? pokemon = null;

        void LoadAt(int index)
        {
            switch (index)
            {
                case 0 when includeTrainers:
                    trainers = CaptureSemanticWorkflowDto(loadTrainers);
                    break;
                case 1:
                    encounters = CaptureSemanticWorkflowDto(loadEncounters);
                    break;
                case 2:
                    items = CaptureSemanticWorkflowDto(loadItems);
                    break;
                case 3:
                    pokemon = CaptureSemanticWorkflowDto(loadPokemon);
                    break;
            }
        }

        var activeSourceCount = includeTrainers ? sourceCount : sourceCount - 1;
        var effectiveParallelism = Math.Clamp(maximumParallelism, 1, activeSourceCount);
        _ = BoundedParallel.For(
            sourceCount,
            CreateBridgePolicy(
                "bridge-guided-design-dto-load",
                BoundedWorkloadKind.Decode,
                effectiveParallelism),
            LoadAt);

        Func<TrainersWorkflowDto> preparedTrainers = trainers is null
            ? () => throw new NotSupportedException(
                "The Guided Design trainers workflow DTO was not prepared for this game.")
            : trainers.Get;
        return new GuidedDesignWorkflowDtoLoaders(
            preparedTrainers,
            (encounters ?? throw new InvalidOperationException(
                "The Guided Design encounters workflow DTO was not prepared.")).Get,
            (items ?? throw new InvalidOperationException(
                "The Guided Design items workflow DTO was not prepared.")).Get,
            (pokemon ?? throw new InvalidOperationException(
                "The Guided Design Pokemon workflow DTO was not prepared.")).Get);
    }

    private static PreparedSemanticWorkflowDto<T> CaptureSemanticWorkflowDto<T>(Func<T> load)
        where T : class
    {
        try
        {
            return new PreparedSemanticWorkflowDto<T>(load(), Failure: null);
        }
        catch (Exception exception)
        {
            return new PreparedSemanticWorkflowDto<T>(
                Value: null,
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception));
        }
    }

    private static BoundedConcurrencyPolicy CreateBridgePolicy(
        string name,
        BoundedWorkloadKind workloadKind,
        int maximumParallelism)
    {
        return new BoundedConcurrencyPolicy(
            name,
            workloadKind,
            EstimatedBridgeWorkerBytes,
            maximumDegreeOfParallelism: Math.Clamp(
                maximumParallelism,
                1,
                BoundedConcurrencyPolicy.MaximumSupportedParallelism),
            memoryBudgetDivisor: 8,
            degreeOfParallelismWhenMemoryUnknown: 1);
    }

    private sealed record PreparedSemanticWorkflowDto<T>(
        T? Value,
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? Failure)
        where T : class
    {
        public T Get()
        {
            Failure?.Throw();
            return Value ?? throw new InvalidOperationException(
                "The semantic workflow DTO was not prepared.");
        }
    }

    private TrainersWorkflowDto LoadBalanceLabTrainersFresh(ProjectPathsDto pathsDto)
    {
        var paths = ProjectBridgeMapper.ToCore(pathsDto);
        if (IsPokemonLegendsZA(paths))
        {
            return ZaBridgeMapper.ToDto(zaWorkflowService.LoadBalanceLabTrainers(paths)).Workflow;
        }

        if (IsScarletViolet(paths))
        {
            return SvBridgeMapper.ToDto(svWorkflowService.LoadBalanceLabTrainers(paths)).Workflow;
        }

        if (paths.SelectedGame is ProjectGame.Sword or ProjectGame.Shield)
        {
            return SwShBridgeMapper.ToDto(swShWorkflowService.LoadBalanceLabTrainers(paths)).Workflow;
        }

        throw new SemanticExploreValidationException(
            "The selected Balance Lab trainer provider is unsupported.",
            SemanticExploreFailureKind.Unsupported);
    }

    private EncountersWorkflowDto LoadBalanceLabEncountersFresh(ProjectPathsDto pathsDto)
    {
        var paths = ProjectBridgeMapper.ToCore(pathsDto);
        if (IsPokemonLegendsZA(paths))
        {
            return ZaBridgeMapper.ToDto(zaWorkflowService.LoadBalanceLabEncounters(paths)).Workflow;
        }

        if (IsScarletViolet(paths))
        {
            return SvBridgeMapper.ToDto(svWorkflowService.LoadBalanceLabEncounters(paths)).Workflow;
        }

        if (paths.SelectedGame is ProjectGame.Sword or ProjectGame.Shield)
        {
            return SwShBridgeMapper.ToDto(swShWorkflowService.LoadBalanceLabEncounters(paths)).Workflow;
        }

        throw new SemanticExploreValidationException(
            "The selected Balance Lab encounter provider is unsupported.",
            SemanticExploreFailureKind.Unsupported);
    }

    private TeraRaidsWorkflowDto LoadGameModuleTeraRaidsFresh(ProjectPathsDto pathsDto)
    {
        var paths = ProjectBridgeMapper.ToCore(pathsDto);
        if (IsScarletViolet(paths))
        {
            return SvBridgeMapper.ToDto(svWorkflowService.LoadGameModuleTeraRaids(paths)).Workflow;
        }

        throw new SemanticExploreValidationException(
            "The selected Tera Raid module provider is unsupported.",
            SemanticExploreFailureKind.Unsupported);
    }

    private SvPackedLooseSourceComparison LoadGameModulePackedLooseSourceComparisonFresh(
        ProjectPathsDto pathsDto)
    {
        var paths = ProjectBridgeMapper.ToCore(pathsDto);
        if (IsScarletViolet(paths))
        {
            return svWorkflowService.LoadPackedLooseSourceComparison(paths);
        }

        throw new SemanticExploreValidationException(
            "The selected packed and loose source module provider is unsupported.",
            SemanticExploreFailureKind.Unsupported);
    }

    private SvEventDataComparison LoadGameModuleEventDataComparisonFresh(
        ProjectPathsDto pathsDto)
    {
        var paths = ProjectBridgeMapper.ToCore(pathsDto);
        if (IsScarletViolet(paths))
        {
            return svWorkflowService.LoadEventDataComparison(paths);
        }

        throw new SemanticExploreValidationException(
            "The selected event data comparison module provider is unsupported.",
            SemanticExploreFailureKind.Unsupported);
    }

    private SvScenePlacementProjection LoadGameModuleScenePlacementProjectionFresh(
        ProjectPathsDto pathsDto)
    {
        var paths = ProjectBridgeMapper.ToCore(pathsDto);
        if (IsScarletViolet(paths))
        {
            return svWorkflowService.LoadScenePlacementProjection(paths);
        }

        throw new SemanticExploreValidationException(
            "The selected scene placement module provider is unsupported.",
            SemanticExploreFailureKind.Unsupported);
    }

    private SvTypeEffectivenessStateProjection
        LoadGameModuleScarletVioletTypeEffectivenessStateFresh(
            ProjectPathsDto pathsDto)
    {
        var paths = ProjectBridgeMapper.ToCore(pathsDto);
        if (IsScarletViolet(paths))
        {
            return svWorkflowService.LoadTypeEffectivenessStateProjection(paths);
        }

        throw new SemanticExploreValidationException(
            "The selected Scarlet/Violet type-effectiveness module provider is unsupported.",
            SemanticExploreFailureKind.Unsupported);
    }

    private (EncountersWorkflowDto Encounters, MovesWorkflowDto Moves)
        LoadGameModuleScriptedBossTimelineFresh(ProjectPathsDto pathsDto)
    {
        var paths = ProjectBridgeMapper.ToCore(pathsDto);
        if (IsPokemonLegendsZA(paths))
        {
            var sources = zaWorkflowService.LoadGameModuleScriptedBossTimeline(paths);
            return (
                ZaBridgeMapper.ToDto(sources.Encounters).Workflow,
                ZaBridgeMapper.ToDto(sources.Moves).Workflow);
        }

        throw new SemanticExploreValidationException(
            "The selected scripted boss module provider is unsupported.",
            SemanticExploreFailureKind.Unsupported);
    }

    private SwordShieldGameModuleSourceBatchDto LoadGameModuleSwordShieldCapabilityBatchFresh(
        ProjectPathsDto pathsDto)
    {
        var paths = ProjectBridgeMapper.ToCore(pathsDto);
        if (paths.SelectedGame is ProjectGame.Sword or ProjectGame.Shield)
        {
            return SwShBridgeMapper.ToDto(
                swShWorkflowService.LoadGameModuleSourcesFreshBounded(paths));
        }

        throw new SemanticExploreValidationException(
            "The selected Sword and Shield game module capability provider is unsupported.",
            SemanticExploreFailureKind.Unsupported);
    }

    private (
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
        ZaPokemonResourceCatalog PokemonResourceCatalog)
        LoadGameModuleZaCapabilityBatchFresh(ProjectPathsDto pathsDto)
    {
        var paths = ProjectBridgeMapper.ToCore(pathsDto);
        if (IsPokemonLegendsZA(paths))
        {
            const int outputCount = 11;
            var sources = zaWorkflowService.LoadGameModuleCapabilityBatch(paths);
            PreparedSemanticWorkflowDto<EncountersWorkflowDto>? scriptedBossEncounters = null;
            PreparedSemanticWorkflowDto<EncountersWorkflowDto>? wildEncounters = null;
            PreparedSemanticWorkflowDto<MovesWorkflowDto>? moves = null;
            PreparedSemanticWorkflowDto<TrainersWorkflowDto>? trainers = null;
            PreparedSemanticWorkflowDto<EncounterCompatibilityWorkflowDto>?
                encounterCompatibility = null;
            PreparedSemanticWorkflowDto<PokemonWorkflowDto>? pokemon = null;
            PreparedSemanticWorkflowDto<TrainerPoolsWorkflowDto>? trainerPools = null;
            PreparedSemanticWorkflowDto<LegendsZaTypeEffectivenessStateDto>?
                typeEffectivenessState = null;
            PreparedSemanticWorkflowDto<ZaStaticMapMarkerCatalog>? staticMapMarkers = null;
            PreparedSemanticWorkflowDto<ZaNamedFlagCatalog>? namedFlagCatalog = null;
            PreparedSemanticWorkflowDto<ZaPokemonResourceCatalog>? pokemonResourceCatalog = null;

            void MapAt(int index)
            {
                switch (index)
                {
                    case 0:
                        scriptedBossEncounters = CaptureSemanticWorkflowDto(
                            () => ZaBridgeMapper.ToDto(sources.ScriptedBossEncounters).Workflow);
                        break;
                    case 1:
                        wildEncounters = CaptureSemanticWorkflowDto(
                            () => ZaBridgeMapper.ToDto(sources.WildEncounters).Workflow);
                        break;
                    case 2:
                        moves = CaptureSemanticWorkflowDto(
                            () => ZaBridgeMapper.ToDto(sources.Moves).Workflow);
                        break;
                    case 3:
                        trainers = CaptureSemanticWorkflowDto(
                            () => ZaBridgeMapper.ToDto(sources.Trainers).Workflow);
                        break;
                    case 4:
                        encounterCompatibility = CaptureSemanticWorkflowDto(
                            () => ZaBridgeMapper.ToGameModuleDto(sources.EncounterCompatibility));
                        break;
                    case 5:
                        pokemon = CaptureSemanticWorkflowDto(
                            () => ZaBridgeMapper.ToGameModuleDto(sources.Pokemon));
                        break;
                    case 6:
                        trainerPools = CaptureSemanticWorkflowDto(
                            () => ZaBridgeMapper.ToGameModuleDto(sources.TrainerPools));
                        break;
                    case 7:
                        typeEffectivenessState = CaptureSemanticWorkflowDto(
                            () => ZaBridgeMapper.ToGameModuleDto(sources.TypeEffectivenessState));
                        break;
                    case 8:
                        staticMapMarkers = CaptureSemanticWorkflowDto(
                            () => sources.StaticMapMarkers);
                        break;
                    case 9:
                        namedFlagCatalog = CaptureSemanticWorkflowDto(
                            () => sources.NamedFlagCatalog);
                        break;
                    case 10:
                        pokemonResourceCatalog = CaptureSemanticWorkflowDto(
                            () => sources.PokemonResourceCatalog);
                        break;
                }
            }

            var effectiveParallelism = Math.Clamp(
                zaWorkflowService.GameModuleCapabilityBatchParallelism,
                1,
                outputCount);
            _ = BoundedParallel.For(
                outputCount,
                CreateBridgePolicy(
                    "bridge-za-game-module-dto-map",
                    BoundedWorkloadKind.Map,
                    effectiveParallelism),
                MapAt);

            // Resolve in the original bridge tuple order so mapping failures stay deterministic.
            return (
                (scriptedBossEncounters ?? throw new InvalidOperationException(
                    "The Z-A scripted boss encounter DTO was not prepared.")).Get(),
                (wildEncounters ?? throw new InvalidOperationException(
                    "The Z-A wild encounter DTO was not prepared.")).Get(),
                (moves ?? throw new InvalidOperationException(
                    "The Z-A move DTO was not prepared.")).Get(),
                (trainers ?? throw new InvalidOperationException(
                    "The Z-A trainer DTO was not prepared.")).Get(),
                (encounterCompatibility ?? throw new InvalidOperationException(
                    "The Z-A encounter compatibility DTO was not prepared.")).Get(),
                (pokemon ?? throw new InvalidOperationException(
                    "The Z-A Pokemon DTO was not prepared.")).Get(),
                (trainerPools ?? throw new InvalidOperationException(
                    "The Z-A trainer pool DTO was not prepared.")).Get(),
                (typeEffectivenessState ?? throw new InvalidOperationException(
                    "The Z-A type effectiveness DTO was not prepared.")).Get(),
                (staticMapMarkers ?? throw new InvalidOperationException(
                    "The Z-A static map marker projection was not prepared.")).Get(),
                (namedFlagCatalog ?? throw new InvalidOperationException(
                    "The Z-A named flag catalog projection was not prepared.")).Get(),
                (pokemonResourceCatalog ?? throw new InvalidOperationException(
                    "The Z-A Pokemon resource catalog projection was not prepared.")).Get());
        }

        throw new SemanticExploreValidationException(
            "The selected Z-A game module capability provider is unsupported.",
            SemanticExploreFailureKind.Unsupported);
    }

    private TrainersWorkflowDto LoadGameModuleTrainerArchetypesFresh(ProjectPathsDto pathsDto)
    {
        var paths = ProjectBridgeMapper.ToCore(pathsDto);
        if (IsPokemonLegendsZA(paths))
        {
            return ZaBridgeMapper.ToDto(zaWorkflowService.LoadGameModuleTrainerArchetypes(paths)).Workflow;
        }

        throw new SemanticExploreValidationException(
            "The selected trainer archetype module provider is unsupported.",
            SemanticExploreFailureKind.Unsupported);
    }

    private EncountersWorkflowDto LoadGameModuleWildSpawnsFresh(ProjectPathsDto pathsDto)
    {
        var paths = ProjectBridgeMapper.ToCore(pathsDto);
        if (IsPokemonLegendsZA(paths))
        {
            return ZaBridgeMapper.ToDto(zaWorkflowService.LoadGameModuleWildSpawns(paths)).Workflow;
        }

        throw new SemanticExploreValidationException(
            "The selected wild spawn module provider is unsupported.",
            SemanticExploreFailureKind.Unsupported);
    }

    private MovesWorkflowDto LoadGameModuleMoveVariantsFresh(ProjectPathsDto pathsDto)
    {
        var paths = ProjectBridgeMapper.ToCore(pathsDto);
        if (IsPokemonLegendsZA(paths))
        {
            return ZaBridgeMapper.ToDto(zaWorkflowService.LoadGameModuleMoveVariants(paths)).Workflow;
        }

        throw new SemanticExploreValidationException(
            "The selected move variant module provider is unsupported.",
            SemanticExploreFailureKind.Unsupported);
    }

    private EncounterCompatibilityWorkflowDto LoadGameModuleEncounterCompatibilityFresh(
        ProjectPathsDto pathsDto)
    {
        var paths = ProjectBridgeMapper.ToCore(pathsDto);
        if (IsPokemonLegendsZA(paths))
        {
            return ZaBridgeMapper.ToGameModuleDto(
                zaWorkflowService.LoadGameModuleEncounterCompatibility(paths));
        }

        throw new SemanticExploreValidationException(
            "The selected encounter compatibility module provider is unsupported.",
            SemanticExploreFailureKind.Unsupported);
    }

    private PokemonWorkflowDto LoadGameModuleAlphaMovesFresh(ProjectPathsDto pathsDto)
    {
        var paths = ProjectBridgeMapper.ToCore(pathsDto);
        if (IsPokemonLegendsZA(paths))
        {
            return ZaBridgeMapper.ToGameModuleDto(
                zaWorkflowService.LoadGameModuleAlphaMoves(paths));
        }

        throw new SemanticExploreValidationException(
            "The selected alpha move module provider is unsupported.",
            SemanticExploreFailureKind.Unsupported);
    }

    private TrainerPoolsWorkflowDto LoadGameModuleTrainerPoolsFresh(ProjectPathsDto pathsDto)
    {
        var paths = ProjectBridgeMapper.ToCore(pathsDto);
        if (IsPokemonLegendsZA(paths))
        {
            return ZaBridgeMapper.ToGameModuleDto(
                zaWorkflowService.LoadGameModuleTrainerPools(paths));
        }

        throw new SemanticExploreValidationException(
            "The selected Trainer Pools game module provider is unsupported.",
            SemanticExploreFailureKind.Unsupported);
    }

    private LegendsZaTypeEffectivenessStateDto LoadGameModuleTypeEffectivenessStateFresh(
        ProjectPathsDto pathsDto)
    {
        var paths = ProjectBridgeMapper.ToCore(pathsDto);
        if (IsPokemonLegendsZA(paths))
        {
            return ZaBridgeMapper.ToGameModuleDto(
                zaWorkflowService.LoadGameModuleTypeEffectivenessState(paths));
        }

        throw new SemanticExploreValidationException(
            "The selected Type Effectiveness State game module provider is unsupported.",
            SemanticExploreFailureKind.Unsupported);
    }

    private ZaStaticMapMarkerCatalog LoadGameModuleStaticMapMarkersFresh(ProjectPathsDto pathsDto)
    {
        var paths = ProjectBridgeMapper.ToCore(pathsDto);
        if (IsPokemonLegendsZA(paths))
        {
            return zaWorkflowService.LoadGameModuleStaticMapMarkers(paths);
        }

        throw new SemanticExploreValidationException(
            "The selected Static Map Markers game module provider is unsupported.",
            SemanticExploreFailureKind.Unsupported);
    }

    private ZaNamedFlagCatalog LoadGameModuleNamedFlagCatalogFresh(ProjectPathsDto pathsDto)
    {
        var paths = ProjectBridgeMapper.ToCore(pathsDto);
        if (IsPokemonLegendsZA(paths))
        {
            return zaWorkflowService.LoadGameModuleNamedFlagCatalog(paths);
        }

        throw new SemanticExploreValidationException(
            "The selected Named Flag Catalog game module provider is unsupported.",
            SemanticExploreFailureKind.Unsupported);
    }

    private ZaPokemonResourceCatalog LoadGameModulePokemonResourceCatalogFresh(
        ProjectPathsDto pathsDto)
    {
        var paths = ProjectBridgeMapper.ToCore(pathsDto);
        if (IsPokemonLegendsZA(paths))
        {
            return zaWorkflowService.LoadGameModulePokemonResourceCatalog(paths);
        }

        throw new SemanticExploreValidationException(
            "The selected Pokemon Resource Catalog game module provider is unsupported.",
            SemanticExploreFailureKind.Unsupported);
    }

    private static string GetSemanticExploreErrorCode(SemanticExploreFailureKind failureKind)
    {
        return failureKind switch
        {
            SemanticExploreFailureKind.InvalidData => BridgeErrorCodes.SemanticInvalidQuery,
            SemanticExploreFailureKind.StaleRevision => BridgeErrorCodes.SemanticStaleRevision,
            SemanticExploreFailureKind.Unsupported => BridgeErrorCodes.SemanticUnsupported,
            SemanticExploreFailureKind.InvalidCursor => BridgeErrorCodes.SemanticInvalidCursor,
            SemanticExploreFailureKind.ExternalRejected => BridgeErrorCodes.SemanticExternalRejected,
            SemanticExploreFailureKind.ExternalSnapshotUnavailable =>
                BridgeErrorCodes.SemanticExternalSnapshotUnavailable,
            SemanticExploreFailureKind.LimitExceeded => BridgeErrorCodes.SemanticLimitExceeded,
            _ => BridgeErrorCodes.SemanticInvalidQuery,
        };
    }

    private static string GetResearchLabErrorCode(ResearchLabValidationException exception)
    {
        return exception.ResearchFailureKind switch
        {
            ResearchLabFailureKind.InvalidData => BridgeErrorCodes.SemanticInvalidQuery,
            ResearchLabFailureKind.LimitExceeded => BridgeErrorCodes.SemanticLimitExceeded,
            ResearchLabFailureKind.StaleRevision => BridgeErrorCodes.SemanticStaleRevision,
            ResearchLabFailureKind.InvalidCursor => BridgeErrorCodes.SemanticInvalidCursor,
            ResearchLabFailureKind.SourceRejected => BridgeErrorCodes.ResearchSourceRejected,
            ResearchLabFailureKind.SourceExpired => BridgeErrorCodes.ResearchSourceExpired,
            ResearchLabFailureKind.ComparisonStale => BridgeErrorCodes.ResearchComparisonStale,
            _ => BridgeErrorCodes.SemanticInvalidQuery,
        };
    }

    private void ClearWorkflowMemoryCaches(bool clearReusableDataCaches = true)
    {
        semanticExploreApplicationService.ClearMemoryCaches();
        projectWorkspaceService.ClearMemoryCache();
        swShWorkflowService.ClearMemoryCaches(clearReusableDataCaches);
        svWorkflowService.ClearMemoryCaches(clearReusableDataCaches);
        zaWorkflowService.ClearMemoryCaches(clearReusableDataCaches);
    }

    private static bool IsWorkflowCacheBoundary(string? command)
    {
        return command is
            KmCommandNames.OpenProject or
            KmCommandNames.ValidateProject or
            KmCommandNames.RefreshFileGraph or
            KmCommandNames.UpdateSvCacheSettings or
            KmCommandNames.ClearSvCache or
            KmCommandNames.UpdateZaCacheSettings or
            KmCommandNames.ClearZaCache or
            KmCommandNames.UpdateSwShCacheSettings or
            KmCommandNames.ClearSwShCache;
    }

    private static bool IsWorkflowCacheMutation(string? command)
    {
        return command is
            KmCommandNames.ApplyChangePlan or
            KmCommandNames.ApplyModMerge or
            KmCommandNames.ApplySvModMerge or
            KmCommandNames.ApplyZaModMerge or
            KmCommandNames.ApplyFpsPatch or
            KmCommandNames.RestoreFpsPatch or
            KmCommandNames.ApplyProfanityFilter or
            KmCommandNames.RestoreProfanityFilter or
            KmCommandNames.ApplyRandomizer or
            KmCommandNames.RestoreRandomizer or
            KmCommandNames.ApplyGameplaySettingsUpdate or
            KmCommandNames.ApplyInGameSettingsPackage or
            KmCommandNames.ReconcileOutputRecovery or
            KmCommandNames.ApplyOutputCleanup or
            KmCommandNames.RestoreOutputCheckpoint or
            KmCommandNames.ApplyProjectRelocation;
    }

    private static bool IsOutputSafetyCommand(string? command)
    {
        return command is
            KmCommandNames.GetOutputRecoveryStatus or
            KmCommandNames.GetGameplaySettings or
            KmCommandNames.PreviewGameplaySettingsUpdate or
            KmCommandNames.ApplyGameplaySettingsUpdate or
            KmCommandNames.InspectInGameSettingsPackage or
            KmCommandNames.PreviewInGameSettingsPackage or
            KmCommandNames.ApplyInGameSettingsPackage or
            KmCommandNames.ReconcileOutputRecovery or
            KmCommandNames.ScanOutputIntegrity or
            KmCommandNames.PreviewOutputCleanup or
            KmCommandNames.ApplyOutputCleanup or
            KmCommandNames.ListOutputHistory or
            KmCommandNames.ListOutputCheckpoints or
            KmCommandNames.CreateOutputCheckpoint or
            KmCommandNames.PreviewOutputCheckpointRestore or
            KmCommandNames.RestoreOutputCheckpoint or
            KmCommandNames.DeleteOutputCheckpoint or
            KmCommandNames.PreviewProjectRelocation or
            KmCommandNames.ApplyProjectRelocation or
            KmCommandNames.BuildSupportReport;
    }

    private static bool IsChangeSetCommand(string? command)
    {
        return command is
            KmCommandNames.ReadChangeSets or
            KmCommandNames.MutateChangeSets or
            KmCommandNames.CaptureChangeSetSession or
            KmCommandNames.MaterializeChangeSets or
            KmCommandNames.ExportChangeSets or
            KmCommandNames.ImportChangeSets;
    }

    private static bool IsSwordShieldOnlyCommand(string command)
    {
        return command is
            KmCommandNames.OpenSwShPlacementCatalog or
            KmCommandNames.QuerySwShPlacementCatalog or
            KmCommandNames.LoadSwShPlacementObject or
            KmCommandNames.GetSwShCacheStatus or
            KmCommandNames.UpdateSwShCacheSettings or
            KmCommandNames.ClearSwShCache or
            KmCommandNames.WarmupSwShCacheStep or
            KmCommandNames.LoadRentalPokemonWorkflow or
            KmCommandNames.UpdateRentalPokemonField or
            KmCommandNames.UpdateRentalPokemonFields or
            KmCommandNames.LoadDynamaxAdventuresWorkflow or
            KmCommandNames.UpdateDynamaxAdventureField or
            KmCommandNames.UpdateDynamaxAdventureFields or
            KmCommandNames.StageDynamaxAdventureRepair or
            KmCommandNames.StageDynamaxAdventureRestore or
            KmCommandNames.PreviewDynamaxAdventureDefaults or
            KmCommandNames.PlanDynamaxAdventureSeed or
            KmCommandNames.SearchDynamaxAdventureSeed or
            KmCommandNames.SetDynamaxAdventureSaveSeed or
            KmCommandNames.LoadRaidBattlesWorkflow or
            KmCommandNames.UpdateRaidBattleSlotField or
            KmCommandNames.UpdateRaidBattleSlotFields or
            KmCommandNames.LoadRaidRewardsWorkflow or
            KmCommandNames.UpdateRaidRewardField or
            KmCommandNames.UpdateRaidRewardFields or
            KmCommandNames.LoadRaidBonusRewardsWorkflow or
            KmCommandNames.UpdateRaidBonusRewardField or
            KmCommandNames.UpdateRaidBonusRewardFields or
            KmCommandNames.LoadBehaviorWorkflow or
            KmCommandNames.UpdateBehaviorEntryField or
            KmCommandNames.UpdateBehaviorEntryFields or
            KmCommandNames.LoadFlagworkSaveWorkflow or
            KmCommandNames.LoadBagHookWorkflow or
            KmCommandNames.StageBagHookInstall or
            KmCommandNames.StageBagHookUninstall or
            KmCommandNames.LoadCatchCapWorkflow or
            KmCommandNames.StageCatchCap or
            KmCommandNames.StageCatchCapUninstall or
            KmCommandNames.LoadHyperTrainingWorkflow or
            KmCommandNames.StageHyperTraining or
            KmCommandNames.LoadShinyRateWorkflow or
            KmCommandNames.StageShinyRate or
            KmCommandNames.LoadFairyGymBoostsWorkflow or
            KmCommandNames.StageFairyGymBoosts or
            KmCommandNames.LoadGymUniformRemovalWorkflow or
            KmCommandNames.StageGymUniformRemovalInstall or
            KmCommandNames.StageGymUniformRemovalUninstall or
            KmCommandNames.LoadIvScreenWorkflow or
            KmCommandNames.StageIvScreenInstall or
            KmCommandNames.StageIvScreenUninstall or
            KmCommandNames.LoadExeFsPatchWorkflow or
            KmCommandNames.StageExeFsPatch or
            KmCommandNames.LoadRoyalCandyWorkflow or
            KmCommandNames.StageRoyalCandyWorkflow or
            KmCommandNames.LoadStartingItemsWorkflow or
            KmCommandNames.StageStartingItems or
            KmCommandNames.LoadNpcItemGiftWorkflow or
            KmCommandNames.StageNpcItemGift or
            KmCommandNames.LoadBattleCafeRewardsWorkflow or
            KmCommandNames.StageBattleCafeRewardRows or
            KmCommandNames.LoadModMergerWorkflow or
            KmCommandNames.StageModMerge or
            KmCommandNames.ApplyModMerge or
            KmCommandNames.LoadFpsPatch or
            KmCommandNames.ApplyFpsPatch or
            KmCommandNames.RestoreFpsPatch or
            KmCommandNames.LoadProfanityFilter or
            KmCommandNames.ApplyProfanityFilter or
            KmCommandNames.RestoreProfanityFilter or
            KmCommandNames.ImportRandomizerSeed or
            KmCommandNames.ApplyRandomizer or
            KmCommandNames.RestoreRandomizer;
    }

    private static bool IsScarletVioletOnlyCommand(string command)
    {
        return command is
            KmCommandNames.LoadTeraRaidsWorkflow or
            KmCommandNames.UpdateTeraRaidField or
            KmCommandNames.UpdateTeraRaidFields or
            KmCommandNames.StageTypeChartUninstall or
            KmCommandNames.LoadHyperspaceBypassWorkflow or
            KmCommandNames.StageHyperspaceBypassInstall or
            KmCommandNames.StageHyperspaceBypassUninstall or
            KmCommandNames.LoadSvModMergerWorkflow or
            KmCommandNames.StageSvModMerge or
            KmCommandNames.ApplySvModMerge or
            KmCommandNames.LoadTmMachineControls or
            KmCommandNames.StageTmRecipeAvailability or
            KmCommandNames.StageTmMaterialVisibility or
            KmCommandNames.LoadHabitatCoordinates or
            KmCommandNames.StageHabitatCoordinate or
            KmCommandNames.GetSvCacheStatus or
            KmCommandNames.UpdateSvCacheSettings or
            KmCommandNames.ClearSvCache or
            KmCommandNames.WarmupSvCacheStep;
    }

    private static bool IsPokemonLegendsZAOnlyCommand(string command)
    {
        return command is
            KmCommandNames.GetZaCacheStatus or
            KmCommandNames.UpdateZaCacheSettings or
            KmCommandNames.ClearZaCache or
            KmCommandNames.WarmupZaCacheStep or
            KmCommandNames.SwapPokemonDexPlacement or
            KmCommandNames.MovePokemonDexPlacement or
            KmCommandNames.ResizePokemonDex or
            KmCommandNames.StagePokemonDexVanilla or
            KmCommandNames.StagePokemonDexMegaSync or
            KmCommandNames.StageItemVanilla or
            KmCommandNames.StageMoveVanilla or
            KmCommandNames.StageGiftPokemonVanilla or
            KmCommandNames.StageEncounterSlotVanilla or
            KmCommandNames.LoadAngeFightWorkflow or
            KmCommandNames.StageAngeFight or
            KmCommandNames.StageAngeFightUninstall or
            KmCommandNames.LoadTrainerPoolsWorkflow or
            KmCommandNames.StageTrainerPoolFixedCountSwap or
            KmCommandNames.LoadFashionCatalogWorkflow or
            KmCommandNames.StageFashionCatalogFieldEdit or
            KmCommandNames.LoadZaModMergerWorkflow or
            KmCommandNames.StageZaModMerge or
            KmCommandNames.ApplyZaModMerge;
    }

    private static bool IsPokemonLegendsZAAllowedCommand(string command)
    {
        return command is
            KmCommandNames.OpenProject or
            KmCommandNames.ValidateProject or
            KmCommandNames.RefreshFileGraph or
            KmCommandNames.ReadProjectSourceRevision or
            KmCommandNames.ListWorkflows or
            KmCommandNames.LoadItemsWorkflow or
            KmCommandNames.UpdateItemField or
            KmCommandNames.UpdateItemFields or
            KmCommandNames.StageItemVanilla or
            KmCommandNames.LoadPokemonWorkflow or
            KmCommandNames.UpdatePokemonField or
            KmCommandNames.UpdatePokemonFields or
            KmCommandNames.UpdatePokemonComposite or
            KmCommandNames.UpdatePokemonLearnset or
            KmCommandNames.UpdatePokemonEvolution or
            KmCommandNames.SwapPokemonDexPlacement or
            KmCommandNames.MovePokemonDexPlacement or
            KmCommandNames.ResizePokemonDex or
            KmCommandNames.StagePokemonDexVanilla or
            KmCommandNames.StagePokemonDexMegaSync or
            KmCommandNames.LoadTrainersWorkflow or
            KmCommandNames.UpdateTrainerField or
            KmCommandNames.UpdateTrainerFields or
            KmCommandNames.LoadTrainerPoolsWorkflow or
            KmCommandNames.StageTrainerPoolFixedCountSwap or
            KmCommandNames.LoadFashionCatalogWorkflow or
            KmCommandNames.StageFashionCatalogFieldEdit or
            KmCommandNames.LoadPlacementWorkflow or
            KmCommandNames.UpdatePlacementObjectField or
            KmCommandNames.UpdatePlacementObjectFields or
            KmCommandNames.LoadGiftPokemonWorkflow or
            KmCommandNames.UpdateGiftPokemonField or
            KmCommandNames.UpdateGiftPokemonFields or
            KmCommandNames.StageGiftPokemonVanilla or
            KmCommandNames.LoadTradePokemonWorkflow or
            KmCommandNames.UpdateTradePokemonField or
            KmCommandNames.UpdateTradePokemonFields or
            KmCommandNames.LoadEncountersWorkflow or
            KmCommandNames.UpdateEncounterSlotField or
            KmCommandNames.UpdateEncounterSlotFields or
            KmCommandNames.StageEncounterSlotVanilla or
            KmCommandNames.PrepareRowClipboardCopy or
            KmCommandNames.PreviewRowClipboardPaste or
            KmCommandNames.StageRowClipboardPaste or
            KmCommandNames.ClearRowClipboardAuthorizations or
            KmCommandNames.LoadStaticEncountersWorkflow or
            KmCommandNames.UpdateStaticEncounterField or
            KmCommandNames.UpdateStaticEncounterFields or
            KmCommandNames.LoadMovesWorkflow or
            KmCommandNames.UpdateMoveField or
            KmCommandNames.UpdateMoveFields or
            KmCommandNames.StageMoveVanilla or
            KmCommandNames.LoadTextWorkflow or
            KmCommandNames.UpdateTextEntry or
            KmCommandNames.LoadShopsWorkflow or
            KmCommandNames.UpdateShopInventoryItem or
            KmCommandNames.UpdateShopInventoryItems or
            KmCommandNames.LoadTypeChartWorkflow or
            KmCommandNames.StageTypeChart or
            KmCommandNames.StageTypeChartUninstall or
            KmCommandNames.LoadAngeFightWorkflow or
            KmCommandNames.StageAngeFight or
            KmCommandNames.StageAngeFightUninstall or
            KmCommandNames.StartEditSession or
            KmCommandNames.ValidateEditSession or
            KmCommandNames.LoadSpreadsheetImportWorkflow or
            KmCommandNames.PreviewSpreadsheetImport or
            KmCommandNames.CreateChangePlan or
            KmCommandNames.ApplyChangePlan or
            KmCommandNames.GetZaCacheStatus or
            KmCommandNames.UpdateZaCacheSettings or
            KmCommandNames.ClearZaCache or
            KmCommandNames.WarmupZaCacheStep or
            KmCommandNames.ReadSemanticCapabilities or
            KmCommandNames.SearchSemantic or
            KmCommandNames.ReadSemanticEntity or
            KmCommandNames.CompareSemantic or
            KmCommandNames.QuerySemanticReferences or
            KmCommandNames.QuerySemanticImpact or
            KmCommandNames.QuerySemanticOwnership or
            KmCommandNames.CompareExternalSemantic or
            KmCommandNames.QuerySemanticChanges or
            KmCommandNames.QueryBalanceLab or
            KmCommandNames.ReadGameModuleCapabilities or
            KmCommandNames.QueryGameModule or
            KmCommandNames.ReadGuidedDesignCapabilities or
            KmCommandNames.PreviewGuidedDesign or
            KmCommandNames.ImportGuidedDesignProposal or
            KmCommandNames.ReadSemanticMergeCapabilities or
            KmCommandNames.OpenSemanticMergeSource or
            KmCommandNames.PreviewSemanticMerge or
            KmCommandNames.ImportSemanticMerge or
            KmCommandNames.ExportKmRecipe or
            KmCommandNames.ValidateKmRecipe or
            KmCommandNames.PreviewKmRecipe or
            KmCommandNames.ImportKmRecipe or
            KmCommandNames.ReadResearchLabCapabilities or
            KmCommandNames.OpenResearchSource or
            KmCommandNames.CloseResearchSource or
            KmCommandNames.CompareResearchSources or
            KmCommandNames.ReadResearchByteWindow or
            KmCommandNames.ReadResearchAnnotations or
            KmCommandNames.MutateResearchAnnotations or
            KmCommandNames.LoadZaModMergerWorkflow or
            KmCommandNames.StageZaModMerge or
            KmCommandNames.ApplyZaModMerge or
            KmCommandNames.LoadGameDumpWorkflow or
            KmCommandNames.RunGameDump;
    }

    private static SwShEditSessionValidation CreateUnsupportedMixedValidation(EditSession session)
    {
        var diagnostics = new[]
        {
            CreateMixedSessionDiagnostic(),
        };

        return new SwShEditSessionValidation(session, IsValid: false, diagnostics);
    }

    private static ChangePlan CreateUnsupportedMixedChangePlan(EditSession session)
    {
        return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), [CreateMixedSessionDiagnostic()]);
    }

    private static ApplyResult CreateUnsupportedMixedApplyResult(EditSession session)
    {
        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var emptyPlan = new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), [CreateMixedSessionDiagnostic()]);

        return new ApplyResult(
            applyId,
            appliedAt,
            Array.Empty<ProjectFileReference>(),
            new WriteManifest(applyId, appliedAt, emptyPlan.Writes),
            emptyPlan.Diagnostics);
    }

    private static FpsPatchStatusDto ToDto(SwShFpsPatchStatus status)
    {
        return new FpsPatchStatusDto(
            status.Status,
            status.Message,
            status.GlobalApplyBlocked,
            status.GlobalRestoreBlocked,
            status.HasRemovableKmState,
            status.RestoreDiagnostics.Select(ProjectBridgeMapper.ToDto).ToArray(),
            status.BuildId,
            status.DetectedGame is null ? null : ProjectBridgeMapper.ToDto(status.DetectedGame.Value),
            status.PatchedMainSiteCount,
            status.MainSiteCount,
            status.PatchedRomFsFileCount,
            status.ManagedRomFsFileCount,
            status.StaleOwnedRomFsFileCount,
            status.ConflictingRomFsFileCount,
            status.StaleOwnedRomFsFiles,
            status.ConflictingRomFsFiles,
            status.RomFsCategories
                .Select(category => new FpsPatchRomFsCategoryStatusDto(
                    category.Category,
                    category.ManagedFileCount,
                    category.PatchedFileCount,
                    category.StaleOwnedFileCount,
                    category.ConflictingFileCount))
                .ToArray(),
            status.AnimationTimingComponents
                .Select(component => new FpsPatchAnimationTimingComponentStatusDto(
                    component.Id,
                    component.Enabled,
                    component.InputState,
                    component.InputDiagnostics.Select(ProjectBridgeMapper.ToDto).ToArray(),
                    component.ManagedFileCount,
                    component.PatchedFileCount,
                    component.StaleOwnedFileCount,
                    component.ConflictingFileCount))
                .ToArray(),
            status.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static ProfanityFilterStatusDto ToDto(SwShProfanityFilterStatus status)
    {
        return new ProfanityFilterStatusDto(
            status.Status,
            status.Message,
            status.BuildId,
            status.DetectedGame is null ? null : ProjectBridgeMapper.ToDto(status.DetectedGame.Value),
            status.PatchOffsetHex,
            status.PatchShape,
            status.SourceLayer,
            status.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static ValidationDiagnostic CreateMixedSessionDiagnostic()
    {
        return new ValidationDiagnostic(
            DiagnosticSeverity.Error,
            "Edit sessions cannot mix workflow domains in one change plan yet.",
            Domain: "workflow.editSession",
            Expected: "Pending edits from one workflow domain");
    }

    private static SwShRandomizerConfig ToCore(RandomizerConfigDto config)
    {
        return new SwShRandomizerConfig(config.UserSeed, ToCore(config.Options), config.RollSeed, config.OutputHash);
    }

    private static SvModMergerSourceRequest ToCore(SvModMergerSourceDto source)
    {
        return new SvModMergerSourceRequest(source.Path, source.IsEnabled);
    }

    private static ZaModMergerSourceRequest ToCore(ZaModMergerSourceDto source)
    {
        return new ZaModMergerSourceRequest(source.Path, source.IsEnabled);
    }

    private static SwShRandomizerOptions ToCore(RandomizerOptionsDto options)
    {
        return new SwShRandomizerOptions(
            options.RandomizePokemonStats,
            options.ShufflePokemonStats,
            options.StatHp,
            options.StatAttack,
            options.StatDefense,
            options.StatSpecialAttack,
            options.StatSpecialDefense,
            options.StatSpeed,
            options.RandomizePokemonTypes,
            options.TypePrimary,
            options.TypeSecondary,
            options.AllowSameType,
            options.RandomizePokemonAbilities,
            options.Ability1,
            options.Ability2,
            options.HiddenAbility,
            options.RandomizePokemonHeldItems,
            options.RandomizePokemonCatchRates,
            options.RandomizePokemonLearnsets,
            options.LearnsetStabFirst,
            options.LearnsetExpandTo25,
            options.LearnsetBanFixedDamageMoves,
            options.LearnsetRequireDamagingMove,
            options.RandomizePokemonCompatibility,
            options.CompatibilityMachines,
            options.CompatibilityRecords,
            options.CompatibilityTutors,
            options.RandomizePokemonEvolutions,
            options.RandomizeWildEncounters,
            options.RandomizeStaticEncounters,
            options.RandomizeGiftEncounters,
            options.RandomizeRaidRewards,
            options.RandomizeRaidBonusRewards,
            options.RandomizeTypeChart,
            options.TypeChartNoImmunities,
            options.TypeChartOneImmunityPerType);
    }

    private static RandomizerConfigDto ToDto(SwShRandomizerConfig config)
    {
        return new RandomizerConfigDto(config.UserSeed, ToDto(config.Options), config.RollSeed, config.OutputHash);
    }

    private static RandomizerOptionsDto ToDto(SwShRandomizerOptions options)
    {
        return new RandomizerOptionsDto(
            options.RandomizePokemonStats,
            options.ShufflePokemonStats,
            options.StatHp,
            options.StatAttack,
            options.StatDefense,
            options.StatSpecialAttack,
            options.StatSpecialDefense,
            options.StatSpeed,
            options.RandomizePokemonTypes,
            options.TypePrimary,
            options.TypeSecondary,
            options.AllowSameType,
            options.RandomizePokemonAbilities,
            options.Ability1,
            options.Ability2,
            options.HiddenAbility,
            options.RandomizePokemonHeldItems,
            options.RandomizePokemonCatchRates,
            options.RandomizePokemonLearnsets,
            options.LearnsetStabFirst,
            options.LearnsetExpandTo25,
            options.LearnsetBanFixedDamageMoves,
            options.LearnsetRequireDamagingMove,
            options.RandomizePokemonCompatibility,
            options.CompatibilityMachines,
            options.CompatibilityRecords,
            options.CompatibilityTutors,
            options.RandomizePokemonEvolutions,
            options.RandomizeWildEncounters,
            options.RandomizeStaticEncounters,
            options.RandomizeGiftEncounters,
            options.RandomizeRaidRewards,
            options.RandomizeRaidBonusRewards,
            options.RandomizeTypeChart,
            options.TypeChartNoImmunities,
            options.TypeChartOneImmunityPerType);
    }

    private static bool TryParseSeed(string? value, out ulong seed)
    {
        seed = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return ulong.TryParse(
                trimmed[2..],
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out seed);
        }

        return ulong.TryParse(
            trimmed,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out seed);
    }

    private static string BoundSerializedResponse(string responseJson, string? requestId)
    {
        return Encoding.UTF8.GetByteCount(responseJson) <= MaximumBridgeResponseBytes
            ? responseJson
            : SerializeResponseTooLargeFailure(requestId);
    }

    private static BridgeRequest<TPayload> DeserializeRequest<TPayload>(string requestJson)
    {
        BridgeRequest<TPayload>? request;
        try
        {
            request = JsonSerializer.Deserialize<BridgeRequest<TPayload>>(
                requestJson,
                RequestSerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new BridgeRequestException(
                "Bridge request JSON could not be parsed.",
                exception,
                BridgeErrorCodes.InvalidJson);
        }

        if (request is null)
        {
            throw new BridgeRequestException("Bridge request could not be deserialized.");
        }

        if (request.Payload is null)
        {
            throw new BridgeRequestException("Bridge request payload is missing.");
        }

        return request;
    }

    private static BridgeCommandEnvelope? DeserializeEnvelope(string requestJson)
    {
        try
        {
            return JsonSerializer.Deserialize<BridgeCommandEnvelope>(
                requestJson,
                BridgeJson.SerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new BridgeRequestException(
                "Bridge request JSON could not be parsed.",
                exception,
                BridgeErrorCodes.InvalidJson);
        }
    }

    private static bool IsFatal(Exception exception)
    {
        if (exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or AppDomainUnloadedException)
        {
            return true;
        }

        if (exception is AggregateException aggregateException
            && aggregateException.InnerExceptions.Any(IsFatal))
        {
            return true;
        }

        return exception.InnerException is not null && IsFatal(exception.InnerException);
    }

    private static string GetWorkspaceErrorCode(WorkspaceDocumentStoreException exception)
    {
        return exception switch
        {
            WorkspaceDocumentConflictException => BridgeErrorCodes.WorkspaceConflict,
            WorkspaceDocumentSecurityException => BridgeErrorCodes.AccessDenied,
            WorkspaceDocumentFormatException => BridgeErrorCodes.DataInvalid,
            UnsupportedWorkspaceDocumentVersionException => BridgeErrorCodes.DataInvalid,
            WorkspaceDocumentTooLargeException => BridgeErrorCodes.DataInvalid,
            _ => BridgeErrorCodes.IoFailed,
        };
    }

    private static string SerializeSuccess<TPayload>(TPayload payload, string? requestId)
    {
        var response = BridgeResponse<TPayload>.Success(payload, requestId);

        return JsonSerializer.Serialize(response, BridgeJson.SerializerOptions);
    }

    private static string SerializeFailure(
        string code,
        string message,
        string? requestId,
        IReadOnlyList<ApiDiagnostic>? diagnostics = null)
    {
        var safeMessage = BridgeDiagnosticSanitizer.Sanitize(message);
        var safeDiagnostics = diagnostics is null
            ? Array.Empty<ApiDiagnostic>()
            : diagnostics.Select(BridgeDiagnosticSanitizer.Sanitize).ToArray();
        var response = BridgeResponse<object>.Failure(
            ApiError.Create(code, safeMessage, safeDiagnostics),
            requestId);

        return JsonSerializer.Serialize(response, BridgeJson.SerializerOptions);
    }

    private sealed class BridgeRequestException : Exception
    {
        public BridgeRequestException(
            string message,
            Exception? innerException = null,
            string? code = null)
            : base(message, innerException)
        {
            Code = code;
        }

        public string? Code { get; }
    }

    private sealed record BridgeCommandEnvelope(string? Command, string? RequestId);

    private enum EditSessionDomain
    {
        None,
        Items,
        Pokemon,
        Moves,
        Text,
        Trainers,
        Shops,
        Encounters,
        ExeFsPatches,
        BagHook,
        CatchCap,
        HyperTraining,
        ShinyRate,
        TypeChart,
        FairyGymBoosts,
        FashionUnlock,
        GymUniformRemoval,
        IvScreen,
        GiftPokemon,
        TradePokemon,
        RentalPokemon,
        DynamaxAdventures,
        StaticEncounters,
        Placement,
        Behavior,
        RaidBattles,
        RaidRewards,
        RaidBonusRewards,
        RoyalCandy,
        StartingItems,
        NpcItemGift,
        BattleCafeRewards,
        Mixed,
    }
}

