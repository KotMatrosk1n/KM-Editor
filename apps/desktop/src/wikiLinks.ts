/* SPDX-License-Identifier: GPL-3.0-only */

import { type ProjectGame } from './bridge/contracts';
import { type WorkbenchSection } from './workbenchStore';
import { isScarletVioletGame } from './workflowGameSupport';

const wikiBaseUrl = 'https://github.com/KotMatrosk1n/KM-Editor/wiki';

const commonWikiSlugs: Partial<Record<WorkbenchSection, string>> = {
  bagHook: 'Bag-Hook',
  behavior: 'Behavior-Editor',
  catchCap: 'Catch-Cap-Editor',
  changes: 'Changes-and-Apply',
  dynamaxAdventures: 'Dynamax-Adventures',
  encounters: 'Wild-Encounters-Editor',
  exefsPatches: 'ExeFS-Patches',
  fairyGymBoosts: 'Fairy-Gym-Boosts',
  fashionUnlock: 'Fashion-Unlock',
  flagworkSave: 'Flagwork-and-Save-Viewers',
  fpsPatch: '60FPS-Patch',
  giftPokemon: 'Gift-Pokemon-Editor',
  gymUniformRemoval: 'Gym-Uniform-Removal',
  health: 'Project-Setup',
  hyperTraining: 'Hyper-Training',
  items: 'Items-Editor',
  ivScreen: 'IV-Screen',
  modMerger: 'Mod-Merger',
  moves: 'Moves-Editor',
  npcItemGift: 'NPC-Item-Gift',
  placement: 'Placement-Editor',
  pokemon: 'Pokemon-Editor',
  raidBattles: 'Raid-Battles-Editor',
  raidBonusRewards: 'Raid-Bonus-Rewards-Editor',
  raidRewards: 'Raid-Rewards-Editor',
  randomizer: 'Randomizer',
  rentalPokemon: 'Rental-Pokemon-Editor',
  royalCandy: 'Royal-Candy',
  shinyRate: 'Shiny-Rate',
  shops: 'Shops-Editor',
  spreadsheetImport: 'Spreadsheet-Import',
  startingItems: 'Starting-Items',
  staticEncounters: 'Static-Encounters-Editor',
  text: 'Text-Viewer',
  tradePokemon: 'Trade-Pokemon-Editor',
  trainers: 'Trainers-Editor',
  typeChart: 'Type-Chart',
  workflows: 'Editing-Workflow'
};

const scarletVioletWikiSlugs: Partial<Record<WorkbenchSection, string>> = {
  encounters: 'Scarlet-and-Violet-Wild-Encounters-Editor',
  hyperspaceBypass: 'Hyperspace-Bypass',
  items: 'Scarlet-and-Violet-Items-Editor',
  modMerger: 'Scarlet-and-Violet-Mod-Merger',
  moves: 'Scarlet-and-Violet-Moves-Editor',
  placement: 'Scarlet-and-Violet-Placement-Editor',
  pokemon: 'Scarlet-and-Violet-Pokemon-Editor',
  trainers: 'Scarlet-and-Violet-Trainers-Editor'
};

export function getSectionWikiUrl(section: WorkbenchSection, selectedGame: ProjectGame | null) {
  const slug = isScarletVioletGame(selectedGame)
    ? scarletVioletWikiSlugs[section] ?? commonWikiSlugs[section]
    : commonWikiSlugs[section];

  return slug ? `${wikiBaseUrl}/${slug}` : null;
}
