/* SPDX-License-Identifier: GPL-3.0-only */

import type { EditSession } from './bridge/contracts';
import type { WorkbenchSection } from './workbenchStore';

export const workflowRetentionMaxCount = 6;
export const workflowRetentionMaxUnits = 60_000;
export const workflowRetentionMinimumRecent = 2;

export const workflowStoreKeyBySection = {
  bagHook: 'bagHookWorkflow',
  behavior: 'behaviorWorkflow',
  catchCap: 'catchCapWorkflow',
  dynamaxAdventures: 'dynamaxAdventuresWorkflow',
  encounters: 'encountersWorkflow',
  exefsPatches: 'exeFsPatchWorkflow',
  fairyGymBoosts: 'fairyGymBoostsWorkflow',
  fashionUnlock: 'fashionUnlockWorkflow',
  flagworkSave: 'flagworkSaveWorkflow',
  giftPokemon: 'giftPokemonWorkflow',
  gymUniformRemoval: 'gymUniformRemovalWorkflow',
  hyperTraining: 'hyperTrainingWorkflow',
  hyperspaceBypass: 'hyperspaceBypassWorkflow',
  items: 'itemsWorkflow',
  ivScreen: 'ivScreenWorkflow',
  moves: 'movesWorkflow',
  npcItemGift: 'npcItemGiftWorkflow',
  placement: 'placementWorkflow',
  pokemon: 'pokemonWorkflow',
  raidBattles: 'raidBattlesWorkflow',
  raidBonusRewards: 'raidBonusRewardsWorkflow',
  raidRewards: 'raidRewardsWorkflow',
  rentalPokemon: 'rentalPokemonWorkflow',
  royalCandy: 'royalCandyWorkflow',
  shinyRate: 'shinyRateWorkflow',
  shops: 'shopsWorkflow',
  spreadsheetImport: 'spreadsheetImportWorkflow',
  startingItems: 'startingItemsWorkflow',
  staticEncounters: 'staticEncountersWorkflow',
  teraRaids: 'teraRaidsWorkflow',
  text: 'textWorkflow',
  tradePokemon: 'tradePokemonWorkflow',
  trainers: 'trainersWorkflow',
  typeChart: 'typeChartWorkflow'
} as const satisfies Partial<Record<WorkbenchSection, string>>;

export type StoredRetainedWorkflowSection = keyof typeof workflowStoreKeyBySection;
export type RetainedWorkflowSection = StoredRetainedWorkflowSection | 'modMerger';
export type WorkflowStoreKey = (typeof workflowStoreKeyBySection)[keyof typeof workflowStoreKeyBySection];
export type WorkflowEvictionKey = WorkflowStoreKey | 'spreadsheetImportPreview';
export type WorkflowRetentionState = Partial<Record<WorkflowStoreKey, unknown>>;
export const storedRetainedWorkflowSections = Object.keys(
  workflowStoreKeyBySection
) as StoredRetainedWorkflowSection[];

const refreshDependentsBySection: Partial<
  Record<RetainedWorkflowSection, readonly RetainedWorkflowSection[]>
> = {
  royalCandy: ['placement'],
  staticEncounters: ['placement'],
  text: ['placement']
};

export type LoadedWorkflowRetentionEntry = {
  cost: number;
  section: RetainedWorkflowSection;
};

type WorkflowRetentionSizeHint = {
  readonly estimatedWorkflowRetentionUnits: number;
};

type WorkflowRetentionLimits = {
  maxCount?: number;
  maxUnits?: number;
  minimumRecent?: number;
};

const pendingEditSectionByDomain: Readonly<Record<string, RetainedWorkflowSection>> = {
  'workflow.bagHook': 'bagHook',
  'workflow.behavior': 'behavior',
  'workflow.catchCap': 'catchCap',
  'workflow.dynamaxAdventures': 'dynamaxAdventures',
  'workflow.encounters': 'encounters',
  'workflow.exefs': 'exefsPatches',
  'workflow.exefsPatches': 'exefsPatches',
  'workflow.fairyGymBoosts': 'fairyGymBoosts',
  'workflow.fashionUnlock': 'fashionUnlock',
  'workflow.flagworkSave': 'flagworkSave',
  'workflow.giftPokemon': 'giftPokemon',
  'workflow.gymUniformRemoval': 'gymUniformRemoval',
  'workflow.hyperTraining': 'hyperTraining',
  'workflow.hyperspaceBypass': 'hyperspaceBypass',
  'workflow.items': 'items',
  'workflow.ivScreen': 'ivScreen',
  'workflow.modMerger': 'modMerger',
  'workflow.moves': 'moves',
  'workflow.npcItemGift': 'npcItemGift',
  'workflow.placement': 'placement',
  'workflow.pokemon': 'pokemon',
  'workflow.raidBattles': 'raidBattles',
  'workflow.raidBonusRewards': 'raidBonusRewards',
  'workflow.raidRewards': 'raidRewards',
  'workflow.rentalPokemon': 'rentalPokemon',
  'workflow.royalCandy': 'royalCandy',
  'workflow.shinyRate': 'shinyRate',
  'workflow.shops': 'shops',
  'workflow.spreadsheetImport': 'spreadsheetImport',
  'workflow.startingItems': 'startingItems',
  'workflow.staticEncounters': 'staticEncounters',
  'workflow.svModMerger': 'modMerger',
  'workflow.teraRaids': 'teraRaids',
  'workflow.text': 'text',
  'workflow.tradePokemon': 'tradePokemon',
  'workflow.trainers': 'trainers',
  'workflow.typeChart': 'typeChart',
  'workflow.zaModMerger': 'modMerger'
};

const estimatedCostByObject = new WeakMap<object, number>();

export class WorkflowLoadGeneration {
  private readonly activeTokens = new Map<RetainedWorkflowSection, symbol>();

  begin(section: RetainedWorkflowSection) {
    const token = Symbol(section);
    this.activeTokens.set(section, token);
    return token;
  }

  canCommit(section: RetainedWorkflowSection, token: symbol) {
    return this.activeTokens.get(section) === token;
  }

  getActiveSections() {
    return [...this.activeTokens.keys()];
  }

  finish(section: RetainedWorkflowSection, token: symbol) {
    const activeToken = this.activeTokens.get(section);
    if (activeToken === token) {
      this.activeTokens.delete(section);
      return 'current' as const;
    }

    return activeToken === undefined ? 'invalidated' as const : 'superseded' as const;
  }

  invalidate(section: RetainedWorkflowSection) {
    this.activeTokens.delete(section);
  }

  invalidateAll() {
    this.activeTokens.clear();
  }
}

export function isRetainedWorkflowSection(
  section: WorkbenchSection
): section is RetainedWorkflowSection {
  return section === 'modMerger' || section in workflowStoreKeyBySection;
}

export function getLoadedWorkflowRetentionEntries(
  state: WorkflowRetentionState,
  modMergerWorkflow: unknown = null
) {
  const entries: LoadedWorkflowRetentionEntry[] = [];
  for (const [section, key] of Object.entries(workflowStoreKeyBySection) as Array<
    [keyof typeof workflowStoreKeyBySection, WorkflowStoreKey]
  >) {
    const workflow = state[key];
    if (workflow !== null && workflow !== undefined) {
      entries.push({ cost: estimateWorkflowRetentionUnits(workflow), section });
    }
  }

  if (modMergerWorkflow !== null && modMergerWorkflow !== undefined) {
    entries.push({ cost: estimateWorkflowRetentionUnits(modMergerWorkflow), section: 'modMerger' });
  }

  return entries;
}

export function createWorkflowRetentionSizeHint(units: number): WorkflowRetentionSizeHint {
  return { estimatedWorkflowRetentionUnits: Math.max(0, Math.round(units)) };
}

export function createLoadedWorkflowEvictionState(
  sections: Iterable<WorkbenchSection>
): Partial<Record<WorkflowEvictionKey, null>> {
  const resetState: Partial<Record<WorkflowEvictionKey, null>> = {};
  for (const section of sections) {
    if (section === 'modMerger') {
      continue;
    }

    const key = workflowStoreKeyBySection[section as keyof typeof workflowStoreKeyBySection];
    if (key) {
      resetState[key] = null;
    }
    if (section === 'spreadsheetImport') {
      resetState.spreadsheetImportPreview = null;
    }
  }

  return resetState;
}

export function getEditSessionOwnerSections(
  editSession: EditSession | null,
  editSessionSection: WorkbenchSection | null
) {
  const sections = new Set<RetainedWorkflowSection>();
  if (editSessionSection && isRetainedWorkflowSection(editSessionSection)) {
    sections.add(editSessionSection);
  }

  for (const edit of editSession?.pendingEdits ?? []) {
    const section = pendingEditSectionByDomain[edit.domain];
    if (section) {
      sections.add(section);
    }
  }

  return sections;
}

export function touchWorkflowRecency(
  currentOrder: readonly RetainedWorkflowSection[],
  section: RetainedWorkflowSection
) {
  return [...currentOrder.filter((candidate) => candidate !== section), section];
}

export function removeWorkflowRecency(
  currentOrder: readonly RetainedWorkflowSection[],
  sections: ReadonlySet<WorkbenchSection>
) {
  return currentOrder.filter((section) => !sections.has(section));
}

export function selectWorkflowSectionsToEvict(
  entries: readonly LoadedWorkflowRetentionEntry[],
  recency: readonly RetainedWorkflowSection[],
  protectedSections: ReadonlySet<WorkbenchSection>,
  limits: WorkflowRetentionLimits = {}
) {
  const maxCount = limits.maxCount ?? workflowRetentionMaxCount;
  const maxUnits = limits.maxUnits ?? workflowRetentionMaxUnits;
  const minimumRecent = limits.minimumRecent ?? workflowRetentionMinimumRecent;
  const loadedSections = new Set(entries.map((entry) => entry.section));
  const pinnedSections = new Set(
    [...protectedSections].filter(
      (section): section is RetainedWorkflowSection =>
        isRetainedWorkflowSection(section) && loadedSections.has(section)
    )
  );

  let recentCount = 0;
  for (let index = recency.length - 1; index >= 0 && recentCount < minimumRecent; index -= 1) {
    const section = recency[index];
    if (section && loadedSections.has(section) && !pinnedSections.has(section)) {
      pinnedSections.add(section);
      recentCount += 1;
    }
  }

  const recencyIndex = new Map(recency.map((section, index) => [section, index]));
  const evictionCandidates = entries
    .filter((entry) => !pinnedSections.has(entry.section))
    .sort((left, right) =>
      (recencyIndex.get(left.section) ?? -1) - (recencyIndex.get(right.section) ?? -1)
    );
  let retainedCount = entries.length;
  let retainedUnits = entries.reduce((total, entry) => total + entry.cost, 0);
  const evicted: RetainedWorkflowSection[] = [];

  for (const candidate of evictionCandidates) {
    if (retainedCount <= maxCount && retainedUnits <= maxUnits) {
      break;
    }

    evicted.push(candidate.section);
    retainedCount -= 1;
    retainedUnits -= candidate.cost;
  }

  return evicted;
}

export function selectWorkflowSectionsToRefresh(
  entries: readonly LoadedWorkflowRetentionEntry[],
  recency: readonly RetainedWorkflowSection[],
  preferredSections: ReadonlySet<WorkbenchSection>,
  recentCount = workflowRetentionMinimumRecent
) {
  const loadedSections = new Set(entries.map((entry) => entry.section));
  const selected = new Set<RetainedWorkflowSection>();
  for (const section of preferredSections) {
    if (
      section !== 'modMerger' &&
      isRetainedWorkflowSection(section) &&
      loadedSections.has(section)
    ) {
      selected.add(section);

      for (const dependentSection of refreshDependentsBySection[section] ?? []) {
        if (loadedSections.has(dependentSection)) {
          selected.add(dependentSection);
        }
      }
    }
  }

  let selectedRecentCount = 0;
  for (let index = recency.length - 1; index >= 0 && selectedRecentCount < recentCount; index -= 1) {
    const section = recency[index];
    if (
      section &&
      section !== 'modMerger' &&
      loadedSections.has(section) &&
      !selected.has(section)
    ) {
      selected.add(section);
      selectedRecentCount += 1;
    }
  }

  return selected;
}

export function estimateWorkflowRetentionUnits(value: unknown) {
  if (typeof value !== 'object' || value === null) {
    return estimateSampledValue(value, 0);
  }

  const cached = estimatedCostByObject.get(value);
  if (cached !== undefined) {
    return cached;
  }

  const estimated = Math.max(1, Math.round(estimateSampledValue(value, 0)));
  estimatedCostByObject.set(value, estimated);
  return estimated;
}

function estimateSampledValue(value: unknown, depth: number): number {
  if (value === null || value === undefined) {
    return 1;
  }

  if (typeof value === 'string') {
    return 1 + Math.min(256, Math.ceil(value.length / 16));
  }

  if (typeof value !== 'object') {
    return 1;
  }

  if ('estimatedWorkflowRetentionUnits' in value) {
    const hintedUnits = value.estimatedWorkflowRetentionUnits;
    if (typeof hintedUnits === 'number' && Number.isFinite(hintedUnits)) {
      return Math.max(1, hintedUnits);
    }
  }

  if (value instanceof Map || value instanceof Set) {
    return 2 + value.size * 2;
  }

  if (Array.isArray(value)) {
    if (value.length === 0) {
      return 2;
    }

    if (depth >= 3) {
      return 2 + value.length;
    }

    const sampleCount = Math.min(4, value.length);
    let sampleUnits = 0;
    for (let index = 0; index < sampleCount; index += 1) {
      const sampleIndex = Math.floor((index * value.length) / sampleCount);
      sampleUnits += estimateSampledValue(value[sampleIndex], depth + 1);
    }

    return 2 + value.length + (sampleUnits / sampleCount) * value.length;
  }

  let units = 2;
  let fieldCount = 0;
  for (const key in value) {
    if (!Object.prototype.hasOwnProperty.call(value, key)) {
      continue;
    }

    fieldCount += 1;
    units += 1;
    if (depth < 3) {
      units += estimateSampledValue((value as Record<string, unknown>)[key], depth + 1);
    }
  }

  return units + fieldCount;
}
