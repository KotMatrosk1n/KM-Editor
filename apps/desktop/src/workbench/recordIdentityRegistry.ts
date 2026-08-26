/* SPDX-License-Identifier: GPL-3.0-only */

import type { WorkbenchSection } from './workbenchSections';

export type RecordIdentityStability =
  | 'intrinsic'
  | 'fixedSchemaSlot'
  | 'sourceRevisionBound'
  | 'operationScoped'
  | 'notRecordScoped';

export type WorkbenchRecordIdentityRegistration = {
  basis: string;
  canNavigateAcrossSourceRevisions: boolean;
  canUseForStructuralMutation: boolean;
  section: WorkbenchSection;
  stability: RecordIdentityStability;
};

const registrations = {
  health: notRecordScoped('health'),
  workbench: notRecordScoped('workbench'),
  workflows: notRecordScoped('workflows'),
  items: intrinsic('items', 'stored item ID'),
  pokemon: sourceRevisionBound('pokemon', 'personal-table row plus exact source revision'),
  dexLayout: intrinsic('dexLayout', 'stored species ID and dex kind'),
  moves: intrinsic('moves', 'stored move ID'),
  text: intrinsic('text', 'stored message key'),
  trainers: intrinsic('trainers', 'stored trainer ID and fixed party slot'),
  trainerPools: fixedSchemaSlot('trainerPools', 'stored pool identity and fixed member slot'),
  fashionCatalog: sourceRevisionBound(
    'fashionCatalog',
    'catalog family, stored identity where present, and exact source revision'
  ),
  giftPokemon: sourceRevisionBound(
    'giftPokemon',
    'physical gift row plus exact source revision'
  ),
  tradePokemon: sourceRevisionBound(
    'tradePokemon',
    'physical trade row plus exact source revision'
  ),
  staticEncounters: sourceRevisionBound(
    'staticEncounters',
    'physical encounter row plus exact source revision'
  ),
  rentalPokemon: sourceRevisionBound(
    'rentalPokemon',
    'physical rental row plus exact source revision'
  ),
  dynamaxAdventures: sourceRevisionBound(
    'dynamaxAdventures',
    'physical Adventure row plus exact source revision'
  ),
  shops: sourceRevisionBound(
    'shops',
    'stored shop ID with inventory row identity bound to the exact source revision'
  ),
  battleCafeRewards: fixedSchemaSlot(
    'battleCafeRewards',
    'verified fixed reward row and owner columns'
  ),
  tmMachineControls: fixedSchemaSlot(
    'tmMachineControls',
    'verified executable control site'
  ),
  habitatCoordinates: sourceRevisionBound(
    'habitatCoordinates',
    'region cell binding plus exact source revision'
  ),
  encounters: sourceRevisionBound(
    'encounters',
    'stored table identity and physical slot plus exact source revision'
  ),
  teraRaids: sourceRevisionBound(
    'teraRaids',
    'source key and physical raid or reward row plus exact source revision'
  ),
  raidBattles: sourceRevisionBound(
    'raidBattles',
    'stored table identity and physical slot plus exact source revision'
  ),
  raidRewards: sourceRevisionBound(
    'raidRewards',
    'stored table identity and physical reward slot plus exact source revision'
  ),
  raidBonusRewards: sourceRevisionBound(
    'raidBonusRewards',
    'stored table identity and physical reward slot plus exact source revision'
  ),
  placement: sourceRevisionBound(
    'placement',
    'source member, object kind, and physical row plus exact source revision'
  ),
  behavior: sourceRevisionBound(
    'behavior',
    'source member and physical behavior row plus exact source revision'
  ),
  flagworkSave: intrinsic('flagworkSave', 'stored flag hash or save block key'),
  bagHook: operationScoped('bagHook'),
  catchCap: operationScoped('catchCap'),
  hyperTraining: operationScoped('hyperTraining'),
  shinyRate: operationScoped('shinyRate'),
  typeChart: fixedSchemaSlot('typeChart', 'verified attacking and defending type pair'),
  angeFight: sourceRevisionBound(
    'angeFight',
    'scripted action identity plus exact source revision'
  ),
  fairyGymBoosts: fixedSchemaSlot('fairyGymBoosts', 'verified quiz sequence and stat slot'),
  fashionUnlock: operationScoped('fashionUnlock'),
  gymUniformRemoval: operationScoped('gymUniformRemoval'),
  hyperspaceBypass: operationScoped('hyperspaceBypass'),
  ivScreen: operationScoped('ivScreen'),
  exefsPatches: intrinsic('exefsPatches', 'registered patch or verification check ID'),
  royalCandy: intrinsic('royalCandy', 'registered workflow, milestone, or check ID'),
  startingItems: fixedSchemaSlot('startingItems', 'verified hook slot'),
  npcItemGift: intrinsic('npcItemGift', 'registered NPC, gift, and operand slot ID'),
  spreadsheetImport: intrinsic('spreadsheetImport', 'registered import profile ID'),
  modMerger: operationScoped('modMerger'),
  fpsPatch: operationScoped('fpsPatch'),
  profanityFilter: operationScoped('profanityFilter'),
  randomizer: operationScoped('randomizer'),
  gameDump: operationScoped('gameDump'),
  changes: notRecordScoped('changes'),
  settings: operationScoped('settings')
} as const satisfies Readonly<Record<WorkbenchSection, WorkbenchRecordIdentityRegistration>>;

export const workbenchRecordIdentityRegistry: Readonly<
  Record<WorkbenchSection, WorkbenchRecordIdentityRegistration>
> = registrations;

export function getWorkbenchRecordIdentityRegistration(section: WorkbenchSection) {
  return workbenchRecordIdentityRegistry[section];
}

export function canUseWorkbenchIdentityForStructuralMutation(section: WorkbenchSection) {
  return getWorkbenchRecordIdentityRegistration(section).canUseForStructuralMutation;
}

function intrinsic(section: WorkbenchSection, basis: string) {
  // Intrinsic identity is necessary but not sufficient for structural mutation. A section may
  // opt in only after its schema-specific allocator, reference census, and rebuild proof exist.
  return registration(section, 'intrinsic', basis, true, false);
}

function fixedSchemaSlot(section: WorkbenchSection, basis: string) {
  return registration(section, 'fixedSchemaSlot', basis, true, false);
}

function sourceRevisionBound(section: WorkbenchSection, basis: string) {
  return registration(section, 'sourceRevisionBound', basis, false, false);
}

function operationScoped(section: WorkbenchSection) {
  return registration(section, 'operationScoped', 'workflow operation', false, false);
}

function notRecordScoped(section: WorkbenchSection) {
  return registration(section, 'notRecordScoped', 'section state', false, false);
}

function registration(
  section: WorkbenchSection,
  stability: RecordIdentityStability,
  basis: string,
  canNavigateAcrossSourceRevisions: boolean,
  canUseForStructuralMutation: boolean
): WorkbenchRecordIdentityRegistration {
  return {
    basis,
    canNavigateAcrossSourceRevisions,
    canUseForStructuralMutation,
    section,
    stability
  };
}
