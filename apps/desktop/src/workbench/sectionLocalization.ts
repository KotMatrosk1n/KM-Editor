/* SPDX-License-Identifier: GPL-3.0-only */

import type en from '../localization/resources/en.json';
import type es from '../localization/resources/es.json';
import type fr from '../localization/resources/fr.json';
import type de from '../localization/resources/de.json';
import type ru from '../localization/resources/ru.json';
import type uk from '../localization/resources/uk.json';
import type zh from '../localization/resources/zh.json';
import type { WorkbenchSection } from './workbenchSections';

// A section's navigation metadata must exist in every bundled language.
// Keep the concrete JSON types so omitted translations fail compilation.
type SectionResourceKey = Extract<keyof (typeof en.keys | typeof es.keys | typeof fr.keys |
  typeof de.keys | typeof ru.keys | typeof uk.keys | typeof zh.keys), `workbench.section.${string}`>;

export const workbenchSectionLocalization = {
  health: { label: 'workbench.section.health.label', description: 'workbench.section.health.description' },
  workbench: { label: 'workbench.section.workbench.label', description: 'workbench.section.workbench.description' },
  workflows: { label: 'workbench.section.workflows.label', description: 'workbench.section.workflows.description' },
  items: { label: 'workbench.section.items.label', description: 'workbench.section.items.description' },
  pokemon: { label: 'workbench.section.pokemon.label', description: 'workbench.section.pokemon.description' },
  dexLayout: { label: 'workbench.section.dex-layout.label', description: 'workbench.section.dex-layout.description' },
  moves: { label: 'workbench.section.moves.label', description: 'workbench.section.moves.description' },
  text: { label: 'workbench.section.text.label', description: 'workbench.section.text.description' },
  trainers: { label: 'workbench.section.trainers.label', description: 'workbench.section.trainers.description' },
  starmobiles: { label: 'workbench.section.starmobiles.label', description: 'workbench.section.starmobiles.description' },
  trainerPools: { label: 'workbench.section.trainer-pools.label', description: 'workbench.section.trainer-pools.description' },
  fashionCatalog: { label: 'workbench.section.fashion-catalog.label', description: 'workbench.section.fashion-catalog.description' },
  giftPokemon: { label: 'workbench.section.gift-pokemon.label', description: 'workbench.section.gift-pokemon.description' },
  tradePokemon: { label: 'workbench.section.trade-pokemon.label', description: 'workbench.section.trade-pokemon.description' },
  staticEncounters: { label: 'workbench.section.static-encounters.label', description: 'workbench.section.static-encounters.description' },
  rentalPokemon: { label: 'workbench.section.rental-pokemon.label', description: 'workbench.section.rental-pokemon.description' },
  dynamaxAdventures: { label: 'workbench.section.dynamax-adventures.label', description: 'workbench.section.dynamax-adventures.description' },
  shops: { label: 'workbench.section.shops.label', description: 'workbench.section.shops.description' },
  battleCafeRewards: { label: 'workbench.section.battle-cafe-rewards.label', description: 'workbench.section.battle-cafe-rewards.description' },
  tmMachineControls: { label: 'workbench.section.tm-machine-controls.label', description: 'workbench.section.tm-machine-controls.description' },
  habitatCoordinates: { label: 'workbench.section.habitat-coordinates.label', description: 'workbench.section.habitat-coordinates.description' },
  encounters: { label: 'workbench.section.encounters.label', description: 'workbench.section.encounters.description' },
  teraRaids: { label: 'workbench.section.tera-raids.label', description: 'workbench.section.tera-raids.description' },
  raidBattles: { label: 'workbench.section.raid-battles.label', description: 'workbench.section.raid-battles.description' },
  raidRewards: { label: 'workbench.section.raid-rewards.label', description: 'workbench.section.raid-rewards.description' },
  raidBonusRewards: { label: 'workbench.section.raid-bonus-rewards.label', description: 'workbench.section.raid-bonus-rewards.description' },
  placement: { label: 'workbench.section.placement.label', description: 'workbench.section.placement.description' },
  behavior: { label: 'workbench.section.behavior.label', description: 'workbench.section.behavior.description' },
  flagworkSave: { label: 'workbench.section.flagwork-save.label', description: 'workbench.section.flagwork-save.description' },
  bagHook: { label: 'workbench.section.bag-hook.label', description: 'workbench.section.bag-hook.description' },
  catchCap: { label: 'workbench.section.catch-cap.label', description: 'workbench.section.catch-cap.description' },
  hyperTraining: { label: 'workbench.section.hyper-training.label', description: 'workbench.section.hyper-training.description' },
  shinyRate: { label: 'workbench.section.shiny-rate.label', description: 'workbench.section.shiny-rate.description' },
  typeChart: { label: 'workbench.section.type-chart.label', description: 'workbench.section.type-chart.description' },
  angeFight: { label: 'workbench.section.ange-fight.label', description: 'workbench.section.ange-fight.description' },
  fairyGymBoosts: { label: 'workbench.section.fairy-gym-boosts.label', description: 'workbench.section.fairy-gym-boosts.description' },
  fashionUnlock: { label: 'workbench.section.fashion-unlock.label', description: 'workbench.section.fashion-unlock.description' },
  gymUniformRemoval: { label: 'workbench.section.gym-uniform-removal.label', description: 'workbench.section.gym-uniform-removal.description' },
  hyperspaceBypass: { label: 'workbench.section.hyperspace-bypass.label', description: 'workbench.section.hyperspace-bypass.description' },
  ivScreen: { label: 'workbench.section.iv-screen.label', description: 'workbench.section.iv-screen.description' },
  exefsPatches: { label: 'workbench.section.exefs-patches.label', description: 'workbench.section.exefs-patches.description' },
  royalCandy: { label: 'workbench.section.royal-candy.label', description: 'workbench.section.royal-candy.description' },
  startingItems: { label: 'workbench.section.starting-items.label', description: 'workbench.section.starting-items.description' },
  npcItemGift: { label: 'workbench.section.npc-item-gift.label', description: 'workbench.section.npc-item-gift.description' },
  spreadsheetImport: { label: 'workbench.section.spreadsheet-import.label', description: 'workbench.section.spreadsheet-import.description' },
  modMerger: { label: 'workbench.section.mod-merger.label', description: 'workbench.section.mod-merger.description' },
  fpsPatch: { label: 'workbench.section.fps-patch.label', description: 'workbench.section.fps-patch.description' },
  profanityFilter: { label: 'workbench.section.profanity-filter.label', description: 'workbench.section.profanity-filter.description' },
  raidDens: { label: 'workbench.section.raid-dens.label', description: 'workbench.section.raid-dens.description' },
  trainerWhiteout: { label: 'workbench.section.trainer-whiteout.label', description: 'workbench.section.trainer-whiteout.description' },
  trainerDynamax: { label: 'workbench.section.trainer-dynamax.label', description: 'workbench.section.trainer-dynamax.description' },
  randomizer: { label: 'workbench.section.randomizer.label', description: 'workbench.section.randomizer.description' },
  gameDump: { label: 'workbench.section.game-dump.label', description: 'workbench.section.game-dump.description' },
  gameplaySettings: { label: 'workbench.section.gameplay-settings.label', description: 'workbench.section.gameplay-settings.description' },
  modelViewer: { label: 'workbench.section.model-viewer.label', description: 'workbench.section.model-viewer.description' },
  soundStudio: { label: 'workbench.section.sound-studio.label', description: 'workbench.section.sound-studio.description' },
  changes: { label: 'workbench.section.changes.label', description: 'workbench.section.changes.description' },
  history: { label: 'workbench.section.history.label', description: 'workbench.section.history.description' },
  settings: { label: 'workbench.section.settings.label', description: 'workbench.section.settings.description' },
} as const satisfies Record<WorkbenchSection, { label: Extract<SectionResourceKey, `${string}.label`>; description: Extract<SectionResourceKey, `${string}.description`> }>;
