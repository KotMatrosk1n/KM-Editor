/* SPDX-License-Identifier: GPL-3.0-only */

import { ListChecks } from 'lucide-react';
import { type ProjectHealth, type WorkflowSummary } from '../../bridge/contracts';
import { useLocalization } from '../../localization';
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
  isBagHookLoading,
  isCatchCapLoading,
  isHyperTrainingLoading,
  isShinyRateLoading,
  isFairyGymBoostsLoading,
  isFashionUnlockLoading,
  isGymUniformRemovalLoading,
  isHyperspaceBypassLoading,
  isIvScreenLoading,
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
  onOpenBagHookWorkflow,
  onOpenCatchCapWorkflow,
  onOpenHyperTrainingWorkflow,
  onOpenShinyRateWorkflow,
  onOpenFairyGymBoostsWorkflow,
  onOpenFashionUnlockWorkflow,
  onOpenGymUniformRemovalWorkflow,
  onOpenHyperspaceBypassWorkflow,
  onOpenIvScreenWorkflow,
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
  isBagHookLoading: boolean;
  isCatchCapLoading: boolean;
  isHyperTrainingLoading: boolean;
  isShinyRateLoading: boolean;
  isFairyGymBoostsLoading: boolean;
  isFashionUnlockLoading: boolean;
  isGymUniformRemovalLoading: boolean;
  isHyperspaceBypassLoading: boolean;
  isIvScreenLoading: boolean;
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
  onOpenBagHookWorkflow: () => void;
  onOpenCatchCapWorkflow: () => void;
  onOpenHyperTrainingWorkflow: () => void;
  onOpenShinyRateWorkflow: () => void;
  onOpenFairyGymBoostsWorkflow: () => void;
  onOpenFashionUnlockWorkflow: () => void;
  onOpenGymUniformRemovalWorkflow: () => void;
  onOpenHyperspaceBypassWorkflow: () => void;
  onOpenIvScreenWorkflow: () => void;
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
  pendingEditCount: number;
  workflows: WorkflowSummary[];
}) {
  const { translateLiteral } = useLocalization();
  const visibleWorkflowDefinitions = workflowDefinitions.filter((definition) =>
    workflows.some((workflow) => workflow.id === definition.id)
  );

  if (!health?.canOpenEditableWorkflows) {
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
    text: action('Open Text', isTextLoading, onOpenTextWorkflow),
    trainers: action('Open Trainers', isTrainersLoading, onOpenTrainersWorkflow),
    tradePokemon: action('Open Trades', isTradePokemonLoading, onOpenTradePokemonWorkflow)
  };

  return (
    <section aria-labelledby="workflows-heading" className="panel wide-panel">
      <div className="panel-heading">
        <ListChecks aria-hidden="true" size={18} />
        <h2 id="workflows-heading">Workflow List</h2>
      </div>

      <div className="workflow-list">
        {visibleWorkflowDefinitions.map((definition) => {
          const workflow = workflows.find((candidate) => candidate.id === definition.id);
          const workflowState = getWorkflowState(health, workflow);
          const Icon = definition.icon;
          const workflowAction = actions[definition.id];
          const isItemsWorkflow = definition.id === 'items';
          const blockedReason =
            workflowState.availability === 'disabled'
              ? workflow?.diagnostics.find((diagnostic) => diagnostic.severity === 'error')
                  ?.message ??
                workflow?.diagnostics.find((diagnostic) => diagnostic.severity === 'warning')
                  ?.message ??
                workflow?.diagnostics[0]?.message
              : null;

          return (
            <article className="workflow-row" key={definition.id}>
              <div>
                <h3>{translateLiteral(workflow?.label ?? definition.label)}</h3>
                <p>{translateLiteral(workflow?.description ?? definition.description)}</p>
                {blockedReason ? (
                  <p className="workflow-disabled-reason">{blockedReason}</p>
                ) : null}
                {isItemsWorkflow ? (
                  <span className="inline-metric">
                    {translateLiteral(`Pending changes: ${pendingEditCount}`)}
                  </span>
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

function getWorkflowState(health: ProjectHealth | null, workflow: WorkflowSummary | undefined) {
  if (!health?.canOpenEditableWorkflows) {
    return {
      availability: 'disabled',
      label: 'Disabled',
      statusClass: 'status-blocked'
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
    availability: 'readOnly',
    label: 'Read-only',
    statusClass: 'status-warning'
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
