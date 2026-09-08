/* SPDX-License-Identifier: GPL-3.0-only */

import type { PendingEditContext } from '../../App';
import type { ProjectGame, ProjectPaths } from '../../bridge/contracts';
import type { ProjectBridge } from '../../bridge/projectBridge';

export function createHistoryDisplayContext(selectedGame: ProjectGame): PendingEditContext {
  return {
    selectedGame,
    angeFightWorkflow: null,
    bagHookWorkflow: null,
    catchCapWorkflow: null,
    dynamaxAdventuresWorkflow: null,
    encountersWorkflow: null,
    exeFsPatchWorkflow: null,
    fairyGymBoostsWorkflow: null,
    fashionCatalogWorkflow: null,
    fashionUnlockWorkflow: null,
    flagworkSaveWorkflow: null,
    giftPokemonWorkflow: null,
    behaviorWorkflow: null,
    hyperTrainingWorkflow: null,
    hyperspaceBypassWorkflow: null,
    itemsWorkflow: null,
    ivScreenWorkflow: null,
    movesWorkflow: null,
    placementWorkflow: null,
    pokemonWorkflow: null,
    raidBattlesWorkflow: null,
    raidBonusRewardsWorkflow: null,
    raidRewardsWorkflow: null,
    rentalPokemonWorkflow: null,
    royalCandyWorkflow: null,
    shopsWorkflow: null,
    startingItemsWorkflow: null,
    staticEncountersWorkflow: null,
    teraRaidsWorkflow: null,
    textWorkflow: null,
    shinyRateWorkflow: null,
    trainerPoolsWorkflow: null,
    typeChartWorkflow: null,
    tradePokemonWorkflow: null,
    trainersWorkflow: null,
  };
}

// Load only the viewed history domain, without staging a session or changing editor state.
export async function loadHistoryDisplayContext(
  bridge: ProjectBridge, paths: ProjectPaths, domain: string
): Promise<PendingEditContext> {
  if (!paths.selectedGame) throw new Error('Select a game to resolve history labels.');
  const context = createHistoryDisplayContext(paths.selectedGame);
  switch (domain) {
    case 'workflow.items':
      context.itemsWorkflow = (await bridge.loadItemsWorkflow({ paths })).workflow;
      break;
    case 'workflow.pokemon':
      context.pokemonWorkflow = (await bridge.loadPokemonWorkflow({ paths })).workflow;
      break;
    case 'workflow.moves':
      context.movesWorkflow = (await bridge.loadMovesWorkflow({ paths })).workflow;
      break;
    case 'workflow.trainers':
      context.trainersWorkflow = (await bridge.loadTrainersWorkflow({ paths })).workflow;
      break;
    case 'workflow.giftPokemon':
      context.giftPokemonWorkflow = (await bridge.loadGiftPokemonWorkflow({ paths })).workflow;
      break;
    case 'workflow.tradePokemon':
      context.tradePokemonWorkflow = (await bridge.loadTradePokemonWorkflow({ paths })).workflow;
      break;
    case 'workflow.staticEncounters':
      context.staticEncountersWorkflow = (await bridge.loadStaticEncountersWorkflow({ paths })).workflow;
      break;
    case 'workflow.rentalPokemon':
      context.rentalPokemonWorkflow = (await bridge.loadRentalPokemonWorkflow({ paths })).workflow;
      break;
    case 'workflow.dynamaxAdventures':
      context.dynamaxAdventuresWorkflow = (await bridge.loadDynamaxAdventuresWorkflow({ paths })).workflow;
      break;
    case 'workflow.shops':
      context.shopsWorkflow = (await bridge.loadShopsWorkflow({ paths })).workflow;
      context.itemsWorkflow = (await bridge.loadItemsWorkflow({ paths })).workflow;
      break;
    case 'workflow.encounters':
      context.encountersWorkflow = (await bridge.loadEncountersWorkflow({ paths })).workflow;
      break;
    case 'workflow.teraRaids':
      context.teraRaidsWorkflow = (await bridge.loadTeraRaidsWorkflow({ paths })).workflow;
      break;
    case 'workflow.raidBattles':
      context.raidBattlesWorkflow = (await bridge.loadRaidBattlesWorkflow({ paths })).workflow;
      break;
    case 'workflow.raidRewards':
      context.raidRewardsWorkflow = (await bridge.loadRaidRewardsWorkflow({ paths })).workflow;
      break;
    case 'workflow.raidBonusRewards':
      context.raidBonusRewardsWorkflow = (await bridge.loadRaidBonusRewardsWorkflow({ paths })).workflow;
      break;
    case 'workflow.placement':
      context.placementWorkflow = (await bridge.loadPlacementWorkflow({ paths })).workflow;
      break;
    case 'workflow.behavior':
      context.behaviorWorkflow = (await bridge.loadBehaviorWorkflow({ paths })).workflow;
      break;
    case 'workflow.startingItems':
      context.startingItemsWorkflow = (await bridge.loadStartingItemsWorkflow({ paths })).workflow;
      break;
    case 'workflow.trainerPools':
      context.trainerPoolsWorkflow = (await bridge.loadTrainerPoolsWorkflow({ paths })).workflow;
      break;
    case 'workflow.fashionCatalog':
      context.fashionCatalogWorkflow = (await bridge.loadFashionCatalogWorkflow({ paths })).workflow;
      break;
    case 'workflow.exefs':
    case 'workflow.exefsPatches':
      context.exeFsPatchWorkflow = (await bridge.loadExeFsPatchWorkflow({ paths })).workflow;
      break;
    case 'workflow.starmobiles':
      context.starmobilesWorkflow = (await bridge.loadStarmobiles({ paths, session: null })).workflow;
      break;
  }
  return context;
}
