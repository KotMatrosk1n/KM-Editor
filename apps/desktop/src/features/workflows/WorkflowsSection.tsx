/* SPDX-License-Identifier: GPL-3.0-only */

import { ClipboardCheck, ListChecks, Search, X } from 'lucide-react';
import { useState } from 'react';
import { type ProjectHealth, type WorkflowSummary } from '../../bridge/contracts';
import { useLocalization } from '../../localization';
import { readOnlyViewerSectionIds } from '../../workflowGameSupport';
import { workflowDefinitions } from './workflowDefinitions';

type WorkflowActionConfig = {
  iconLabel: string;
  isLoading: boolean;
  loadingLabel?: string;
  onOpen: () => void;
};

export function WorkflowsSection({
  health,
  isEncountersLoading,
  isExeFsPatchLoading,
  isItemsLoading,
  isMovesLoading,
  isPokemonLoading,
  isShopsLoading,
  isTextLoading,
  isTrainersLoading,
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
  onOpenSpreadsheetImportWorkflow,
  onOpenModMergerWorkflow,
  onOpenTextWorkflow,
  onOpenTrainersWorkflow,
  onOpenChanges,
  pendingEditCount,
  workflows
}: {
  health: ProjectHealth | null;
  isEncountersLoading: boolean;
  isExeFsPatchLoading: boolean;
  isItemsLoading: boolean;
  isMovesLoading: boolean;
  isPokemonLoading: boolean;
  isShopsLoading: boolean;
  isTextLoading: boolean;
  isTrainersLoading: boolean;
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
  onOpenSpreadsheetImportWorkflow: () => void;
  onOpenModMergerWorkflow: () => void;
  onOpenTextWorkflow: () => void;
  onOpenTrainersWorkflow: () => void;
  onOpenChanges: () => void;
  pendingEditCount: number;
  workflows: WorkflowSummary[];
}) {
  const { t, translateLiteral } = useLocalization();
  const [searchText, setSearchText] = useState('');
  const visibleWorkflowDefinitions = workflowDefinitions.filter((definition) =>
    workflows.some((workflow) => workflow.id === definition.id)
  );
  const normalizedSearchText = searchText.trim().toLocaleLowerCase();
  const filteredWorkflowDefinitions = visibleWorkflowDefinitions.filter((definition) => {
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
          <h2 id="workflows-heading">Workflow List</h2>
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
    behavior: action('Open Behavior', isBehaviorLoading, onOpenBehaviorWorkflow),
    catchCap: action('Open Catch Cap', isCatchCapLoading, onOpenCatchCapWorkflow),
    dynamaxAdventures: action('Open Adventures', isDynamaxAdventuresLoading, onOpenDynamaxAdventuresWorkflow),
    encounters: action('Open Wild Encounters', isEncountersLoading, onOpenEncountersWorkflow),
    exefsPatches: action('Open ExeFS', isExeFsPatchLoading, onOpenExeFsPatchWorkflow),
    fairyGymBoosts: action('Open Fairy Gym Boosts', isFairyGymBoostsLoading, onOpenFairyGymBoostsWorkflow),
    fashionUnlock: action('Open Fashion Unlock', isFashionUnlockLoading, onOpenFashionUnlockWorkflow),
    flagworkSave: action('Open Flagwork', isFlagworkSaveLoading, onOpenFlagworkSaveWorkflow),
    giftPokemon: action('Open Gifts', isGiftPokemonLoading, onOpenGiftPokemonWorkflow),
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
    spreadsheetImport: action('Open Import', isSpreadsheetImportLoading, onOpenSpreadsheetImportWorkflow),
    startingItems: action('Open Starting Items', isStartingItemsLoading, onOpenStartingItemsWorkflow),
    staticEncounters: action('Open Static Encounters', isStaticEncountersLoading, onOpenStaticEncountersWorkflow),
    teraRaids: action('Open Tera Raids', isTeraRaidsLoading, onOpenTeraRaidsWorkflow),
    text: action('Open Text', isTextLoading, onOpenTextWorkflow),
    trainers: action('Open Trainers', isTrainersLoading, onOpenTrainersWorkflow),
    tradePokemon: action('Open Trades', isTradePokemonLoading, onOpenTradePokemonWorkflow),
    typeChart: action('Open Type Chart', isTypeChartLoading, onOpenTypeChartWorkflow)
  };

  return (
    <section aria-labelledby="workflows-heading" className="panel wide-panel">
      <div className="panel-heading">
        <ListChecks aria-hidden="true" size={18} />
        <h2 id="workflows-heading">Workflow List</h2>
      </div>

      <div className="workflow-hub-toolbar">
        <label className="search-box workflow-hub-search">
          <Search aria-hidden="true" size={16} />
          <input
            aria-label={translateLiteral('Search')}
            onChange={(event) => setSearchText(event.target.value)}
            placeholder={translateLiteral('Search')}
            value={searchText}
          />
        </label>
        {searchText ? (
          <button
            className="secondary-button compact-button"
            onClick={() => setSearchText('')}
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

      {filteredWorkflowDefinitions.length > 0 ? (
      <div className="workflow-list">
        {filteredWorkflowDefinitions.map((definition) => {
          const workflow = workflows.find((candidate) => candidate.id === definition.id);
          const isReadOnlyViewer = readOnlyViewerSectionIds.has(definition.id);
          const workflowState = getWorkflowState(health, workflow, isReadOnlyViewer);
          const Icon = definition.icon;
          const workflowAction = actions[definition.id];
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
                <span className={`status-pill ${workflowState.statusClass}`}>
                  {translateLiteral(workflowState.label)}
                </span>
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
