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
  swordShieldBattleCafeRewards: ['battleCafeRewards'],
  swordShieldEventAssignments: [],
  scarletVioletTeraRaidAnalysis: ['teraRaids'],
  scarletVioletPackedLooseComparison: [],
  scarletVioletEventDataComparison: ['giftPokemon', 'tradePokemon', 'teraRaids'],
  scarletVioletScenePlacementEditing: [],
  scarletVioletTypeEffectivenessState: ['typeChart'],
  scarletVioletStellarBehavior: [],
  legendsZaScriptedBossTimeline: ['encounters', 'moves'],
  legendsZaTrainerArchetypes: ['trainers'],
  legendsZaWildSpawnExplorer: ['encounters', 'placement'],
  legendsZaEncounterCompatibility: ['encounters', 'placement'],
  legendsZaAlphaMoveDistribution: ['pokemon'],
  legendsZaDexLayoutPlanning: ['dexLayout'],
  legendsZaMoveVariantComparison: ['moves'],
  legendsZaTrainerPoolSwitching: ['trainerPools'],
  legendsZaTypeEffectivenessState: ['typeChart'],
  legendsZaStaticMapMarkers: [],
  legendsZaNamedFlagCatalog: [],
  legendsZaPokemonResourceCatalog: []
};

export function gameModuleTitleKey(module: GameModule) {
  return `gameModules.module.${module}.title`;
}

export function gameModuleDescriptionKey(module: GameModule) {
  return `gameModules.module.${module}.description`;
}

const sourceUnavailableReasonCodes = new Set([
  'trainer-type-event-executable-build-unverified',
  'battle-cafe-source-unavailable',
  'battle-cafe-source-shape-unverified',
  'trainer-type-event-source-incomplete',
  'trainer-type-event-identity-ambiguous',
  'trainer-type-event-source-unavailable',
  'trainer-type-event-source-shape-unverified'
]);

export function gameModuleReasonKey(reasonCode: string) {
  return sourceUnavailableReasonCodes.has(reasonCode)
    ? 'gameModules.reason.workflow-source-unavailable'
    : `gameModules.reason.${reasonCode}`;
}
