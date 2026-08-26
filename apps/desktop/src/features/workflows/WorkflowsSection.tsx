/* SPDX-License-Identifier: GPL-3.0-only */

import { ClipboardCheck, ListChecks, ListFilter, Search, X } from 'lucide-react';
import { useState } from 'react';
import { type ProjectHealth, type WorkflowSummary } from '../../bridge/contracts';
import { ContextHelp } from '../../components/ContextHelp';
import { useLocalization } from '../../localization';
import { readOnlyViewerSectionIds } from '../../workflowGameSupport';
import { workflowDefinitions } from './workflowDefinitions';

type WorkflowActionConfig = {
  iconLabel: string;
  isLoading: boolean;
  loadingLabel?: string;
  onOpen: () => void;
};

type WorkflowAvailabilityFilter = 'all' | 'available' | 'readOnly' | 'disabled';

export function WorkflowsSection({
  health,
  isEncountersLoading,
  isExeFsPatchLoading,
  isItemsLoading,
  isMovesLoading,
  isPokemonLoading,
  isShopsLoading,
  isTmMachineControlsLoading,
  isTextLoading,
  isTrainersLoading,
  isTrainerPoolsLoading,
  isFashionCatalogLoading,
  isHabitatCoordinatesLoading,
  isRaidBattlesLoading,
  isRaidRewardsLoading,
  isRaidBonusRewardsLoading,
  isPlacementLoading,
  isBehaviorLoading,
  isFlagworkSaveLoading,
  isGiftPokemonLoading,
  isTradePokemonLoading,
  isStaticEncountersLoading,
  isRentalPokemonLoading,
  isDynamaxAdventuresLoading,
  isTeraRaidsLoading,
  isBagHookLoading,
  isBattleCafeRewardsLoading,
  isCatchCapLoading,
  isHyperTrainingLoading,
  isShinyRateLoading,
  isFairyGymBoostsLoading,
  isFashionUnlockLoading,
  isGymUniformRemovalLoading,
  isHyperspaceBypassLoading,
  isIvScreenLoading,
  isTypeChartLoading,
  isAngeFightLoading,
  isRoyalCandyLoading,
  isStartingItemsLoading,
  isNpcItemGiftLoading,
  isSpreadsheetImportLoading,
  isModMergerLoading,
  onOpenEncountersWorkflow,
  onOpenExeFsPatchWorkflow,
  onOpenFlagworkSaveWorkflow,
  onOpenGiftPokemonWorkflow,
  onOpenTradePokemonWorkflow,
  onOpenStaticEncountersWorkflow,
  onOpenRentalPokemonWorkflow,
  onOpenDynamaxAdventuresWorkflow,
  onOpenTeraRaidsWorkflow,
  onOpenBagHookWorkflow,
  onOpenBattleCafeRewardsWorkflow,
  onOpenCatchCapWorkflow,
  onOpenHyperTrainingWorkflow,
  onOpenShinyRateWorkflow,
  onOpenFairyGymBoostsWorkflow,
  onOpenFashionUnlockWorkflow,
  onOpenGymUniformRemovalWorkflow,
  onOpenHyperspaceBypassWorkflow,
  onOpenIvScreenWorkflow,
  onOpenTypeChartWorkflow,
  onOpenAngeFightWorkflow,
  onOpenItemsWorkflow,
  onOpenMovesWorkflow,
  onOpenPokemonWorkflow,
  onOpenPlacementWorkflow,
  onOpenBehaviorWorkflow,
  onOpenRaidBattlesWorkflow,
  onOpenRaidRewardsWorkflow,
  onOpenRaidBonusRewardsWorkflow,
  onOpenRoyalCandyWorkflow,
  onOpenStartingItemsWorkflow,
  onOpenNpcItemGiftWorkflow,
  onOpenShopsWorkflow,
  onOpenTmMachineControlsWorkflow,
  onOpenSpreadsheetImportWorkflow,
  onOpenModMergerWorkflow,
  onOpenTextWorkflow,
  onOpenTrainersWorkflow,
  onOpenTrainerPoolsWorkflow,
  onOpenFashionCatalogWorkflow,
  onOpenHabitatCoordinatesWorkflow,
  onOpenChanges,
  loadedWorkflowIds,
  pendingEditCount,
  pendingWorkflowIds,
  draftWorkflowIds,
  workflows
}: {
  health: ProjectHealth | null;
  isEncountersLoading: boolean;
  isExeFsPatchLoading: boolean;
  isItemsLoading: boolean;
  isMovesLoading: boolean;
  isPokemonLoading: boolean;
  isShopsLoading: boolean;
  isTmMachineControlsLoading: boolean;
  isTextLoading: boolean;
  isTrainersLoading: boolean;
  isTrainerPoolsLoading: boolean;
  isFashionCatalogLoading: boolean;
  isHabitatCoordinatesLoading: boolean;
  isRaidBattlesLoading: boolean;
  isRaidRewardsLoading: boolean;
  isRaidBonusRewardsLoading: boolean;
  isPlacementLoading: boolean;
  isBehaviorLoading: boolean;
  isFlagworkSaveLoading: boolean;
  isGiftPokemonLoading: boolean;
  isTradePokemonLoading: boolean;
  isStaticEncountersLoading: boolean;
  isRentalPokemonLoading: boolean;
  isDynamaxAdventuresLoading: boolean;
  isTeraRaidsLoading: boolean;
  isBagHookLoading: boolean;
  isBattleCafeRewardsLoading: boolean;
  isCatchCapLoading: boolean;
  isHyperTrainingLoading: boolean;
  isShinyRateLoading: boolean;
  isFairyGymBoostsLoading: boolean;
  isFashionUnlockLoading: boolean;
  isGymUniformRemovalLoading: boolean;
  isHyperspaceBypassLoading: boolean;
  isIvScreenLoading: boolean;
  isTypeChartLoading: boolean;
  isAngeFightLoading: boolean;
  isRoyalCandyLoading: boolean;
  isStartingItemsLoading: boolean;
  isNpcItemGiftLoading: boolean;
  isSpreadsheetImportLoading: boolean;
  isModMergerLoading: boolean;
  onOpenEncountersWorkflow: () => void;
  onOpenExeFsPatchWorkflow: () => void;
  onOpenFlagworkSaveWorkflow: () => void;
  onOpenGiftPokemonWorkflow: () => void;
  onOpenTradePokemonWorkflow: () => void;
  onOpenStaticEncountersWorkflow: () => void;
  onOpenRentalPokemonWorkflow: () => void;
  onOpenDynamaxAdventuresWorkflow: () => void;
  onOpenTeraRaidsWorkflow: () => void;
  onOpenBagHookWorkflow: () => void;
  onOpenBattleCafeRewardsWorkflow: () => void;
  onOpenCatchCapWorkflow: () => void;
  onOpenHyperTrainingWorkflow: () => void;
  onOpenShinyRateWorkflow: () => void;
  onOpenFairyGymBoostsWorkflow: () => void;
  onOpenFashionUnlockWorkflow: () => void;
  onOpenGymUniformRemovalWorkflow: () => void;
  onOpenHyperspaceBypassWorkflow: () => void;
  onOpenIvScreenWorkflow: () => void;
  onOpenTypeChartWorkflow: () => void;
  onOpenAngeFightWorkflow: () => void;
  onOpenItemsWorkflow: () => void;
  onOpenMovesWorkflow: () => void;
  onOpenPokemonWorkflow: () => void;
  onOpenPlacementWorkflow: () => void;
  onOpenBehaviorWorkflow: () => void;
  onOpenRaidBattlesWorkflow: () => void;
  onOpenRaidRewardsWorkflow: () => void;
  onOpenRaidBonusRewardsWorkflow: () => void;
  onOpenRoyalCandyWorkflow: () => void;
  onOpenStartingItemsWorkflow: () => void;
  onOpenNpcItemGiftWorkflow: () => void;
  onOpenShopsWorkflow: () => void;
  onOpenTmMachineControlsWorkflow: () => void;
  onOpenSpreadsheetImportWorkflow: () => void;
  onOpenModMergerWorkflow: () => void;
  onOpenTextWorkflow: () => void;
  onOpenTrainersWorkflow: () => void;
  onOpenTrainerPoolsWorkflow: () => void;
  onOpenFashionCatalogWorkflow: () => void;
  onOpenHabitatCoordinatesWorkflow: () => void;
  onOpenChanges: () => void;
  loadedWorkflowIds: readonly string[];
  pendingEditCount: number;
  pendingWorkflowIds: readonly string[];
  draftWorkflowIds: readonly string[];
  workflows: WorkflowSummary[];
}) {
  const { t, translateLiteral } = useLocalization();
  const [searchText, setSearchText] = useState('');
  const [availabilityFilter, setAvailabilityFilter] =
    useState<WorkflowAvailabilityFilter>('all');
  const [loadedOnly, setLoadedOnly] = useState(false);
  const [changedOnly, setChangedOnly] = useState(false);
  const [invalidOnly, setInvalidOnly] = useState(false);
  const [pendingOnly, setPendingOnly] = useState(false);
  const visibleWorkflowDefinitions = workflowDefinitions.filter((definition) =>
    workflows.some((workflow) => workflow.id === definition.id)
  );
  const normalizedSearchText = searchText.trim().toLocaleLowerCase();
  const searchFilteredWorkflowDefinitions = visibleWorkflowDefinitions.filter((definition) => {
    if (!normalizedSearchText) {
      return true;
    }

    const workflow = workflows.find((candidate) => candidate.id === definition.id);
    return [
      definition.id,
      translateLiteral(workflow?.label ?? definition.label),
      translateLiteral(workflow?.description ?? definition.description)
    ].some((value) => value.toLocaleLowerCase().includes(normalizedSearchText));
  });

  if (!health?.canOpenReadOnlyWorkflows) {
    return (
      <section aria-labelledby="workflows-heading" className="panel wide-panel">
        <div className="panel-heading">
          <ListChecks aria-hidden="true" size={18} />
          <h2 id="workflows-heading">{translateLiteral('Workflow List')}</h2>
          <ContextHelp label={translateLiteral('Workflow List')}>
            {t('workflows.listHelp')}
          </ContextHelp>
        </div>
        <p className="empty-copy">
          Validate Base RomFS, Base ExeFS, and Output Root before opening editors.
        </p>
      </section>
    );
  }

  const actions: Record<string, WorkflowActionConfig> = {
    angeFight: action('Open Ange Fight', isAngeFightLoading, onOpenAngeFightWorkflow),
    bagHook: action('Open Bag Hook', isBagHookLoading, onOpenBagHookWorkflow),
    battleCafeRewards: action(
      'Open Battle Cafe Rewards',
      isBattleCafeRewardsLoading,
      onOpenBattleCafeRewardsWorkflow
    ),
    behavior: action('Open Behavior', isBehaviorLoading, onOpenBehaviorWorkflow),
    catchCap: action('Open Catch Cap', isCatchCapLoading, onOpenCatchCapWorkflow),
    dynamaxAdventures: action('Open Adventures', isDynamaxAdventuresLoading, onOpenDynamaxAdventuresWorkflow),
    encounters: action('Open Wild Encounters', isEncountersLoading, onOpenEncountersWorkflow),
    exefsPatches: action('Open ExeFS', isExeFsPatchLoading, onOpenExeFsPatchWorkflow),
    fairyGymBoosts: action('Open Fairy Gym Boosts', isFairyGymBoostsLoading, onOpenFairyGymBoostsWorkflow),
    fashionUnlock: action('Open Fashion Unlock', isFashionUnlockLoading, onOpenFashionUnlockWorkflow),
    flagworkSave: action('Open Flagwork', isFlagworkSaveLoading, onOpenFlagworkSaveWorkflow),
    giftPokemon: action('Open Gifts', isGiftPokemonLoading, onOpenGiftPokemonWorkflow),
    habitatCoordinates: action(
      'Open Habitat Coordinates',
      isHabitatCoordinatesLoading,
      onOpenHabitatCoordinatesWorkflow
    ),
    gymUniformRemoval: action('Open Gym Uniform', isGymUniformRemovalLoading, onOpenGymUniformRemovalWorkflow),
    hyperTraining: action('Open Hyper Training', isHyperTrainingLoading, onOpenHyperTrainingWorkflow),
    hyperspaceBypass: action('Open Hyperspace', isHyperspaceBypassLoading, onOpenHyperspaceBypassWorkflow),
    items: action('Open Items', isItemsLoading, onOpenItemsWorkflow),
    ivScreen: action('Open IV Screen', isIvScreenLoading, onOpenIvScreenWorkflow),
    modMerger: action('Open Merger', isModMergerLoading, onOpenModMergerWorkflow),
    moves: action('Open Moves', isMovesLoading, onOpenMovesWorkflow),
    npcItemGift: action('Open NPC Gifts', isNpcItemGiftLoading, onOpenNpcItemGiftWorkflow),
    placement: action('Open Placement', isPlacementLoading, onOpenPlacementWorkflow),
    pokemon: action('Open Pokemon', isPokemonLoading, onOpenPokemonWorkflow),
    raidBattles: action('Open Raid Battles', isRaidBattlesLoading, onOpenRaidBattlesWorkflow),
    raidBonusRewards: action('Open Raid Bonus Rewards', isRaidBonusRewardsLoading, onOpenRaidBonusRewardsWorkflow),
    raidRewards: action('Open Raid Rewards', isRaidRewardsLoading, onOpenRaidRewardsWorkflow),
    rentalPokemon: action('Open Rentals', isRentalPokemonLoading, onOpenRentalPokemonWorkflow),
    royalCandy: action('Open Candy', isRoyalCandyLoading, onOpenRoyalCandyWorkflow),
    shinyRate: action('Open Shiny Rate', isShinyRateLoading, onOpenShinyRateWorkflow),
    shops: action('Open Shops', isShopsLoading, onOpenShopsWorkflow),
    tmMachineControls: action(
      'Open TM Controls',
      isTmMachineControlsLoading,
      onOpenTmMachineControlsWorkflow
    ),
    spreadsheetImport: action('Open Import', isSpreadsheetImportLoading, onOpenSpreadsheetImportWorkflow),
    startingItems: action('Open Starting Items', isStartingItemsLoading, onOpenStartingItemsWorkflow),
    staticEncounters: action('Open Static Encounters', isStaticEncountersLoading, onOpenStaticEncountersWorkflow),
    teraRaids: action('Open Tera Raids', isTeraRaidsLoading, onOpenTeraRaidsWorkflow),
    text: action('Open Text', isTextLoading, onOpenTextWorkflow),
    trainers: action('Open Trainers', isTrainersLoading, onOpenTrainersWorkflow),
    trainerPools: action('Open Trainer Pools', isTrainerPoolsLoading, onOpenTrainerPoolsWorkflow),
    fashionCatalog: action('Open Fashion Catalog', isFashionCatalogLoading, onOpenFashionCatalogWorkflow),
    tradePokemon: action('Open Trades', isTradePokemonLoading, onOpenTradePokemonWorkflow),
    typeChart: action('Open Type Chart', isTypeChartLoading, onOpenTypeChartWorkflow)
  };
  const loadedWorkflowIdSet = new Set(loadedWorkflowIds);
  const pendingWorkflowIdSet = new Set(pendingWorkflowIds);
  const draftWorkflowIdSet = new Set(draftWorkflowIds);
  const filteredWorkflowDefinitions = searchFilteredWorkflowDefinitions.filter((definition) => {
    const workflow = workflows.find((candidate) => candidate.id === definition.id);
    const isReadOnlyViewer = readOnlyViewerSectionIds.has(definition.id);
    const workflowState = getWorkflowState(health, workflow, isReadOnlyViewer);
    const isChanged =
      draftWorkflowIdSet.has(definition.id) || pendingWorkflowIdSet.has(definition.id);
    const isInvalid =
      workflow?.diagnostics.some((diagnostic) => diagnostic.severity === 'error') ?? false;
    return (
      (availabilityFilter === 'all' || workflowState.availability === availabilityFilter) &&
      (!loadedOnly || loadedWorkflowIdSet.has(definition.id)) &&
      (!changedOnly || isChanged) &&
      (!invalidOnly || isInvalid) &&
      (!pendingOnly || pendingWorkflowIdSet.has(definition.id))
    );
  });
  const hasActiveFilters =
    availabilityFilter !== 'all' ||
    loadedOnly ||
    changedOnly ||
    invalidOnly ||
    pendingOnly ||
    searchText.length > 0;

  const clearFilters = () => {
    setAvailabilityFilter('all');
    setLoadedOnly(false);
    setChangedOnly(false);
    setInvalidOnly(false);
    setPendingOnly(false);
    setSearchText('');
  };

  return (
    <section aria-labelledby="workflows-heading" className="panel wide-panel">
      <div className="panel-heading">
        <ListChecks aria-hidden="true" size={18} />
        <h2 id="workflows-heading">{translateLiteral('Workflow List')}</h2>
        <ContextHelp label={translateLiteral('Workflow List')}>
          {t('workflows.listHelp')}
        </ContextHelp>
      </div>

      <div className="workflow-hub-toolbar">
        <div className="search-box workflow-hub-search">
          <Search aria-hidden="true" size={16} />
          <input
            aria-label={translateLiteral('Search')}
            onChange={(event) => setSearchText(event.target.value)}
            placeholder={translateLiteral('Search')}
            value={searchText}
          />
          <ContextHelp label={translateLiteral('Search')}>
            {t('workflows.searchHelp')}
          </ContextHelp>
        </div>
        {hasActiveFilters ? (
          <button
            className="secondary-button compact-button"
            onClick={clearFilters}
            type="button"
          >
            <X aria-hidden="true" size={16} />
            <span>{translateLiteral('Clear')}</span>
          </button>
        ) : null}
        <button
          className="secondary-button compact-button workflow-hub-pending"
          disabled={pendingEditCount === 0}
          onClick={onOpenChanges}
          type="button"
        >
          <ClipboardCheck aria-hidden="true" size={16} />
          <span>{t('Pending changes ({count})', { count: pendingEditCount })}</span>
        </button>
      </div>

      <div aria-label={t('workbench.browser.filters')} className="workflow-hub-filters">
        <span className="workflow-hub-filter-label">
          <ListFilter aria-hidden="true" size={15} />
          {t('workbench.browser.filters')}
        </span>
        {([
          ['all', 'All'],
          ['available', 'Editable'],
          ['readOnly', 'View Only'],
          ['disabled', 'Disabled']
        ] as const).map(([filter, label]) => (
          <button
            aria-pressed={availabilityFilter === filter}
            className="secondary-button compact-button workflow-hub-filter"
            key={filter}
            onClick={() => setAvailabilityFilter(filter)}
            type="button"
          >
            {translateLiteral(label)}
          </button>
        ))}
        <button
          aria-pressed={loadedOnly}
          className="secondary-button compact-button workflow-hub-filter"
          onClick={() => setLoadedOnly((current) => !current)}
          type="button"
        >
          {translateLiteral('Loaded')}
        </button>
        <button
          aria-pressed={changedOnly}
          className="secondary-button compact-button workflow-hub-filter"
          onClick={() => setChangedOnly((current) => !current)}
          type="button"
        >
          {translateLiteral('Changed')}
        </button>
        <button
          aria-pressed={invalidOnly}
          className="secondary-button compact-button workflow-hub-filter"
          onClick={() => setInvalidOnly((current) => !current)}
          type="button"
        >
          {translateLiteral('Invalid')}
        </button>
        <button
          aria-pressed={pendingOnly}
          className="secondary-button compact-button workflow-hub-filter"
          onClick={() => setPendingOnly((current) => !current)}
          type="button"
        >
          {translateLiteral('Pending')}
        </button>
      </div>

      {filteredWorkflowDefinitions.length > 0 ? (
      <div className="workflow-list">
        {filteredWorkflowDefinitions.map((definition) => {
          const workflow = workflows.find((candidate) => candidate.id === definition.id);
          const isReadOnlyViewer = readOnlyViewerSectionIds.has(definition.id);
          const workflowState = getWorkflowState(health, workflow, isReadOnlyViewer);
          const Icon = definition.icon;
          const workflowAction = actions[definition.id];
          const isInvalid =
            workflow?.diagnostics.some((diagnostic) => diagnostic.severity === 'error') ?? false;
          const blockedReason =
            workflowState.availability === 'disabled'
              ? workflow?.diagnostics.find((diagnostic) => diagnostic.severity === 'error')
                  ?.message ??
                workflow?.diagnostics.find((diagnostic) => diagnostic.severity === 'warning')
                  ?.message ??
                workflow?.diagnostics[0]?.message ??
                (!isReadOnlyViewer && !health.canOpenEditableWorkflows
                  ? translateLiteral(
                      'Validate Base RomFS, Base ExeFS, and Output Root before opening editors.'
                    )
                  : null)
              : null;

          return (
            <article className="workflow-row" key={definition.id}>
              <div>
                <h3>{translateLiteral(workflow?.label ?? definition.label)}</h3>
                <p>{translateLiteral(workflow?.description ?? definition.description)}</p>
                {blockedReason ? (
                  <p className="workflow-disabled-reason">{blockedReason}</p>
                ) : null}
              </div>
              <div className="workflow-actions">
                {loadedWorkflowIdSet.has(definition.id) ? (
                  <span className="status-pill status-pill-info">
                    {translateLiteral('Loaded')}
                  </span>
                ) : null}
                {draftWorkflowIdSet.has(definition.id) ? (
                  <span className="status-pill status-warning">
                    {translateLiteral('Draft')}
                  </span>
                ) : null}
                {pendingWorkflowIdSet.has(definition.id) ? (
                  <span className="status-pill status-ready">
                    {translateLiteral('Pending')}
                  </span>
                ) : null}
                {isInvalid ? (
                  <span className="status-pill status-blocked">
                    {translateLiteral('Invalid')}
                  </span>
                ) : null}
                <span className={`status-pill ${workflowState.statusClass}`}>
                  {translateLiteral(workflowState.label)}
                </span>
                <ContextHelp label={translateLiteral(workflowState.label)}>
                  {t(workflowAvailabilityHelpKeys[workflowState.availability])}
                </ContextHelp>
                {workflowAction ? (
                  <button
                    className="secondary-button compact-button"
                    disabled={workflowState.availability === 'disabled' || workflowAction.isLoading}
                    onClick={workflowAction.onOpen}
                    title={blockedReason ?? undefined}
                    type="button"
                  >
                    <Icon aria-hidden="true" size={16} />
                    <span>
                      {workflowAction.isLoading
                        ? translateLiteral(workflowAction.loadingLabel ?? 'Loading')
                        : translateLiteral(workflowAction.iconLabel)}
                    </span>
                  </button>
                ) : null}
              </div>
            </article>
          );
        })}
      </div>
      ) : (
        <p className="empty-copy">{translateLiteral('No matching workflows.')}</p>
      )}
    </section>
  );
}

function action(iconLabel: string, isLoading: boolean, onOpen: () => void): WorkflowActionConfig {
  return {
    iconLabel,
    isLoading,
    onOpen
  };
}

function getWorkflowState(
  health: ProjectHealth | null,
  workflow: WorkflowSummary | undefined,
  isReadOnlyViewer: boolean
) {
  if (
    !health?.canOpenReadOnlyWorkflows ||
    (!isReadOnlyViewer && !health.canOpenEditableWorkflows)
  ) {
    return {
      availability: 'disabled',
      label: 'Disabled',
      statusClass: 'status-blocked'
    } as const;
  }

  if (isReadOnlyViewer && workflow) {
    const availability = workflow.availability === 'disabled' ? 'disabled' : 'readOnly';
    return {
      availability,
      label: workflowAvailabilityLabels[availability],
      statusClass: workflowAvailabilityClassNames[availability]
    } as const;
  }

  if (workflow) {
    return {
      availability: workflow.availability,
      label: workflowAvailabilityLabels[workflow.availability],
      statusClass: workflowAvailabilityClassNames[workflow.availability]
    } as const;
  }

  return {
    availability: 'disabled',
    label: 'Disabled',
    statusClass: 'status-blocked'
  } as const;
}

const workflowAvailabilityLabels = {
  available: 'Editable',
  disabled: 'Disabled',
  readOnly: 'View Only'
} as const;

const workflowAvailabilityClassNames = {
  available: 'status-ready',
  disabled: 'status-blocked',
  readOnly: 'status-warning'
} as const;

const workflowAvailabilityHelpKeys = {
  available: 'workflows.availability.editableHelp',
  disabled: 'workflows.availability.disabledHelp',
  readOnly: 'workflows.availability.viewOnlyHelp'
} as const;
