/* SPDX-License-Identifier: GPL-3.0-only */

import type { GameModule } from '../../bridge/gameModuleContracts';
import type { WorkbenchSection } from '../../workbench/workbenchSections';

export const gameModuleOwnerSections: Readonly<Record<GameModule, readonly WorkbenchSection[]>> = {
  swordShieldRewardEcosystem: [
    'trainers',
    'npcItemGift',
    'raidRewards',
    'raidBonusRewards',
    'shops',
    'placement'
  ],
  swordShieldExeFsCompatibility: ['exefsPatches'],
  swordShieldDynamaxAdventures: [
    'dynamaxAdventures',
    'rentalPokemon',
    'raidRewards'
  ],
  swordShieldRoyalCandyProgression: ['royalCandy'],
  swordShieldBattleCafeRewards: [],
  swordShieldEventAssignments: [],
  scarletVioletTeraRaidAnalysis: ['teraRaids'],
  scarletVioletPackedLooseComparison: [],
  scarletVioletEventDataComparison: ['giftPokemon', 'tradePokemon', 'teraRaids'],
  scarletVioletScenePlacementEditing: [],
  scarletVioletStellarBehavior: [],
  legendsZaScriptedBossTimeline: ['encounters', 'moves'],
  legendsZaTrainerArchetypes: ['trainers'],
  legendsZaWildSpawnExplorer: ['encounters', 'placement'],
  legendsZaEncounterCompatibility: ['encounters', 'placement'],
  legendsZaAlphaMoveDistribution: ['pokemon'],
  legendsZaDexLayoutPlanning: ['dexLayout'],
  legendsZaMoveVariantComparison: ['moves'],
  legendsZaTrainerPoolSwitching: []
};

export function gameModuleTitleKey(module: GameModule) {
  return `gameModules.module.${module}.title`;
}

export function gameModuleDescriptionKey(module: GameModule) {
  return `gameModules.module.${module}.description`;
}

export function gameModuleReasonKey(reasonCode: string) {
  return `gameModules.reason.${reasonCode}`;
}
