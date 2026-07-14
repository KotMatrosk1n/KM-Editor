/* SPDX-License-Identifier: GPL-3.0-only */

import { type ProjectGame } from './bridge/contracts';
import { type WorkbenchSection } from './workbenchStore';
import { isPokemonLegendsZAGame, isScarletVioletGame } from './workflowGameSupport';

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
  gameDump: 'Game-Dump',
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
  profanityFilter: 'Profanity-Filter',
  raidBattles: 'Raid-Battles-Editor',
  raidBonusRewards: 'Raid-Bonus-Rewards-Editor',
  raidRewards: 'Raid-Rewards-Editor',
  randomizer: 'Randomizer',
  rentalPokemon: 'Rental-Pokemon-Editor',
  royalCandy: 'Royal-Candy',
  settings: 'Settings',
  shinyRate: 'Shiny-Rate',
  shops: 'Shops-Editor',
  spreadsheetImport: 'Dump-Importer',
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
  fashionUnlock: 'Scarlet-and-Violet-Fashion-Unlock',
  hyperspaceBypass: 'Hyperspace-Bypass',
  items: 'Scarlet-and-Violet-Items-Editor',
  modMerger: 'Scarlet-and-Violet-Mod-Merger',
  moves: 'Scarlet-and-Violet-Moves-Editor',
  placement: 'Scarlet-and-Violet-Placement-Editor',
  pokemon: 'Scarlet-and-Violet-Pokemon-Editor',
  shops: 'Scarlet-and-Violet-Shops-Editor',
  staticEncounters: 'Scarlet-and-Violet-Static-Encounters-Editor',
  teraRaids: 'Scarlet-and-Violet-Tera-Raids-Editor',
  trainers: 'Scarlet-and-Violet-Trainers-Editor',
  typeChart: 'Scarlet-and-Violet-Type-Chart'
};

const pokemonLegendsZAWikiSlugs: Partial<Record<WorkbenchSection, string>> = {
  encounters: 'Legends-Z-A-Wild-Encounters-Editor',
  gameDump: 'Legends-Z-A-Game-Dump',
  giftPokemon: 'Legends-Z-A-Gift-Pokemon-Editor',
  items: 'Legends-Z-A-Items-Editor',
  modMerger: 'Legends-Z-A-Mod-Merger',
  moves: 'Legends-Z-A-Moves-Editor',
  placement: 'Legends-Z-A-Placement-Editor',
  pokemon: 'Legends-Z-A-Pokemon-Editor',
  shops: 'Legends-Z-A-Shops-Editor',
  spreadsheetImport: 'Legends-Z-A-Dump-Importer',
  staticEncounters: 'Legends-Z-A-Static-Encounters-Editor',
  tradePokemon: 'Legends-Z-A-Trade-Pokemon-Editor',
  trainers: 'Legends-Z-A-Trainers-Editor',
  typeChart: 'Legends-Z-A-Type-Chart'
};

export function getSectionWikiUrl(section: WorkbenchSection, selectedGame: ProjectGame | null) {
  const gameWikiSlugs = isScarletVioletGame(selectedGame)
    ? scarletVioletWikiSlugs
    : isPokemonLegendsZAGame(selectedGame)
      ? pokemonLegendsZAWikiSlugs
      : null;
  const slug = gameWikiSlugs?.[section] ?? commonWikiSlugs[section];

  return slug ? `${wikiBaseUrl}/${slug}` : null;
}
