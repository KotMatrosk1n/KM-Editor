/* SPDX-License-Identifier: GPL-3.0-only */

import type { GameModuleFact, GameModuleRecord } from '../../bridge/gameModuleContracts';

type Translate = (
  key: string,
  values?: Readonly<Record<string, number | string>>
) => string;

const directFactKeys: Readonly<Record<string, string>> = {
  addedEntities: 'addedEntities',
  attackType: 'attackType',
  attackTypeIndex: 'attackTypeIndex',
  baseEffectiveness: 'baseEffectiveness',
  baseSourceLayer: 'baseSourceLayer',
  baseValue: 'baseStoredValue',
  buildId: 'buildId',
  cellCount: 'cellCount',
  changedCellCount: 'changedCellCount',
  changedFields: 'changedOwnedFields',
  chartOffset: 'chartOffset',
  chartState: 'chartState',
  comparedEntities: 'comparedEntities',
  dualLooseOutputState: 'dualLooseOutputState',
  domain: 'domain',
  defenseType: 'defenseType',
  defenseTypeIndex: 'defenseTypeIndex',
  differenceCount: 'differenceCount',
  differsFromBase: 'differsFromBase',
  differsFromVanilla: 'differsFromVanilla',
  effectiveSource: 'effectiveSource',
  effectiveSourceLayer: 'effectiveSourceLayer',
  editProposalCount: 'editProposalCount',
  fileState: 'fileState',
  game: 'game',
  modifiedEntities: 'modifiedEntities',
  occurrence: 'physicalOccurrence',
  ownedFieldCount: 'ownedFieldCount',
  ownedFieldsPerEntity: 'ownedFieldsPerEntity',
  presence: 'presence',
  presentSourceCount: 'presentSourceCount',
  recordCount: 'recordCount',
  removedEntities: 'removedEntities',
  sourceCount: 'sourceCount',
  sourceIdentity: 'sourceIdentity',
  sourceLayer: 'sourceLayer',
  currentEffectiveness: 'currentEffectiveness',
  currentValue: 'currentValue',
  runtimeClaimCount: 'runtimeClaimCount',
  unchangedEntities: 'unchangedEntities',
  vanillaEffectiveness: 'vanillaEffectiveness',
  vanillaValue: 'vanillaValue',
  virtualIdentity: 'virtualIdentity'
};

const sourceKeys = new Set([
  'baseArchive',
  'baseLoose',
  'standaloneLooseOutput',
  'managerLooseOutput',
  'outputArchive'
]);

const sourceFactSuffixes: Readonly<Record<string, string>> = {
  ByteLength: 'sourceByteLength',
  MatchesBaseArchive: 'sourceMatchesBaseArchive',
  MatchesEffective: 'sourceMatchesEffective',
  Present: 'sourcePresent'
};

const eventFieldKeys = new Set([
  'species',
  'form',
  'level',
  'heldItemId',
  'ballItemId',
  'ability',
  'nature',
  'gender',
  'shinyLock',
  'teraType',
  'move1Id',
  'move2Id',
  'move3Id',
  'move4Id',
  'flawlessIvCount',
  'ivHp',
  'ivAttack',
  'ivDefense',
  'ivSpecialAttack',
  'ivSpecialDefense',
  'ivSpeed',
  'scaleMode',
  'scaleValue',
  'requiredSpecies',
  'requiredForm',
  'trainerId',
  'otGender',
  'version',
  'difficulty',
  'deliveryGroupId',
  'spawnRate',
  'captureRate',
  'captureLevel',
  'moveMode',
  'heightMode',
  'heightValue',
  'weightMode',
  'weightValue',
  'hpMultiplier',
  'shieldTriggerHp',
  'shieldTriggerTime',
  'doubleActionHp',
  'doubleActionTime',
  'doubleActionRate',
  'fixedRewardTable',
  'lotteryRewardTable'
]);

const sceneDomainKeys = new Set([
  'visibleItems',
  'hiddenItemPools',
  'rummagingItemPools'
]);

const typeKeys = new Set([
  'normal', 'fighting', 'flying', 'poison', 'ground', 'rock', 'bug', 'ghost',
  'steel', 'fire', 'water', 'grass', 'electric', 'psychic', 'ice', 'dragon',
  'dark', 'fairy'
]);

const typeLiterals: Readonly<Record<string, string>> = {
  normal: 'Normal', fighting: 'Fighting', flying: 'Flying', poison: 'Poison',
  ground: 'Ground', rock: 'Rock', bug: 'Bug', ghost: 'Ghost', steel: 'Steel',
  fire: 'Fire', water: 'Water', grass: 'Grass', electric: 'Electric',
  psychic: 'Psychic', ice: 'Ice', dragon: 'Dragon', dark: 'Dark', fairy: 'Fairy'
};

function canonicalTypeKey(value: string) {
  const key = value.trim().toLocaleLowerCase();
  return typeKeys.has(key) ? key : null;
}

function isSceneFieldKey(fieldKey: string) {
  return fieldKey === 'visibleItemId' ||
    fieldKey === 'visibleQuantity' ||
    fieldKey === 'rummagingCategory' ||
    fieldKey === 'rummagingPattern' ||
    /^hiddenItem(?:[1-9]|10)(?:ItemId|Chance|Count)$/u.test(fieldKey) ||
    /^rummagingItem[1-5]$/u.test(fieldKey);
}

export function presentGameModuleFactLabel(
  fact: Pick<GameModuleFact, 'fieldKey' | 'label'>,
  t: Translate
) {
  const directKey = directFactKeys[fact.fieldKey];
  if (directKey) {
    return t(`gameModules.fact.${directKey}`);
  }

  if (isSceneFieldKey(fact.fieldKey)) {
    return t(`gameModules.sceneField.${fact.fieldKey}`);
  }

  for (const [suffix, factKey] of Object.entries(sourceFactSuffixes)) {
    if (!fact.fieldKey.endsWith(suffix)) {
      continue;
    }
    const sourceKey = fact.fieldKey.slice(0, -suffix.length);
    if (sourceKeys.has(sourceKey)) {
      return t(`gameModules.fact.${factKey}`, {
        source: t(`gameModules.source.${sourceKey}`)
      });
    }
  }

  const eventMatch = /^(.*)(Base|Effective)$/u.exec(fact.fieldKey);
  if (eventMatch) {
    const canonicalFieldKey = eventMatch[1]!;
    const side = eventMatch[2]!;
    if (eventFieldKeys.has(canonicalFieldKey)) {
      return t(
        side === 'Base'
          ? 'gameModules.fact.baseValue'
          : 'gameModules.fact.effectiveValue',
        { field: t(`gameModules.eventField.${canonicalFieldKey}`) }
      );
    }
  }

  return null;
}

export function presentGameModuleFactValue(fact: GameModuleFact, t: Translate) {
  const value = fact.value.displayValue;
  if (fact.fieldKey === 'attackType' || fact.fieldKey === 'defenseType') {
    const typeKey = canonicalTypeKey(value);
    return typeKey ? t(typeLiterals[typeKey]!) : null;
  }
  if (
    fact.fieldKey === 'currentEffectiveness' ||
    fact.fieldKey === 'baseEffectiveness' ||
    fact.fieldKey === 'vanillaEffectiveness'
  ) {
    return value === '0x' || value === '0.5x' || value === '1x' || value === '2x'
      ? value
      : null;
  }
  if (fact.fieldKey === 'chartState') {
    return value === 'vanilla'
      ? t('gameModules.chartState.vanilla')
      : value === 'modified'
        ? t('gameModules.eventPresence.modified')
        : null;
  }
  if (fact.fieldKey === 'game') {
    return value === 'scarlet'
      ? t('gameModules.game.scarlet')
      : value === 'violet'
        ? t('gameModules.game.violet')
        : null;
  }
  if (fact.fieldKey === 'effectiveSource') {
    return t(`gameModules.effectiveSource.${value}`);
  }
  if (fact.fieldKey === 'dualLooseOutputState') {
    return t(`gameModules.dualLooseOutputState.${value}`);
  }
  if (fact.fieldKey === 'presence') {
    return t(`gameModules.eventPresence.${value}`);
  }
  if (fact.fieldKey === 'domain') {
    return t(
      sceneDomainKeys.has(value)
        ? `gameModules.sceneDomain.${value}`
        : `gameModules.eventDomain.${value}`
    );
  }
  if (
    fact.fieldKey === 'sourceLayer' ||
    fact.fieldKey === 'baseSourceLayer' ||
    fact.fieldKey === 'effectiveSourceLayer'
  ) {
    return t(`gameModules.sourceLayer.${value}`);
  }
  if (fact.fieldKey === 'fileState') {
    return t(`gameModules.fileState.${value}`);
  }
  return null;
}

export function presentGameModuleRecordTitle(record: GameModuleRecord, t: Translate) {
  if (record.recordKind === 'typeEffectivenessState') {
    return t('gameModules.record.typeEffectivenessStateTitle');
  }
  if (record.recordKind === 'typeEffectivenessCell') {
    const attack = record.facts.find((fact) => fact.fieldKey === 'attackType')
      ?.value.displayValue;
    const defense = record.facts.find((fact) => fact.fieldKey === 'defenseType')
      ?.value.displayValue;
    const attackKey = attack ? canonicalTypeKey(attack) : null;
    const defenseKey = defense ? canonicalTypeKey(defense) : null;
    return attackKey && defenseKey
      ? t('gameModules.record.typeEffectivenessCellTitle', {
          attack: t(typeLiterals[attackKey]!),
          defense: t(typeLiterals[defenseKey]!)
        })
      : null;
  }
  if (record.recordKind === 'packedLooseSource') {
    return record.facts.find((fact) => fact.fieldKey === 'virtualIdentity')
      ?.value.displayValue ?? null;
  }
  if (
    record.recordKind === 'scenePlacementSummary' ||
    record.recordKind === 'scenePlacementSource' ||
    record.recordKind === 'scenePlacementRecord'
  ) {
    const domain = record.facts.find((fact) => fact.fieldKey === 'domain')
      ?.value.displayValue;
    if (!domain || !sceneDomainKeys.has(domain)) {
      return null;
    }
    const localizedDomain = t(`gameModules.sceneDomain.${domain}`);
    if (record.recordKind === 'scenePlacementSummary') {
      return t('gameModules.record.sceneCoverageTitle', {
        domain: localizedDomain
      });
    }
    if (record.recordKind === 'scenePlacementSource') {
      return t('gameModules.record.sceneSourceTitle', {
        domain: localizedDomain
      });
    }
    const occurrence = record.facts.find((fact) => fact.fieldKey === 'occurrence')
      ?.value.displayValue;
    return occurrence === undefined
      ? null
      : t('gameModules.record.sceneOccurrenceTitle', {
          domain: localizedDomain,
          occurrence
        });
  }
  if (
    record.recordKind !== 'eventComparisonSummary' &&
    record.recordKind !== 'eventComparisonChange'
  ) {
    return null;
  }

  const domain = record.facts.find((fact) => fact.fieldKey === 'domain')
    ?.value.displayValue;
  if (!domain) {
    return null;
  }
  const localizedDomain = t(`gameModules.eventDomain.${domain}`);
  if (record.recordKind === 'eventComparisonSummary') {
    return t('gameModules.record.eventSummaryTitle', { domain: localizedDomain });
  }

  const occurrence = record.facts.find((fact) => fact.fieldKey === 'occurrence')
    ?.value.displayValue;
  const presence = record.facts.find((fact) => fact.fieldKey === 'presence')
    ?.value.displayValue;
  if (occurrence === undefined || presence === undefined) {
    return null;
  }
  const canonicalFieldKeys = new Set(
    record.facts.flatMap((fact) => {
      const match = /^(.*)(Base|Effective)$/u.exec(fact.fieldKey);
      return match && eventFieldKeys.has(match[1]!) ? [match[1]!] : [];
    })
  );
  if (canonicalFieldKeys.size === 1) {
    const field = [...canonicalFieldKeys][0]!;
    return t('gameModules.record.eventFieldTitle', {
      domain: localizedDomain,
      field: t(`gameModules.eventField.${field}`),
      occurrence
    });
  }
  if (canonicalFieldKeys.size > 1) {
    return t('gameModules.record.eventChangedFieldsTitle', {
      count: canonicalFieldKeys.size,
      domain: localizedDomain,
      occurrence
    });
  }
  return t('gameModules.record.eventPresenceTitle', {
    domain: localizedDomain,
    occurrence,
    presence: t(`gameModules.eventPresence.${presence}`)
  });
}

export function presentGameModuleRecordSummary(record: GameModuleRecord, t: Translate) {
  if (record.recordKind === 'typeEffectivenessState') {
    return record.facts.some((fact) => fact.fieldKey === 'changedCellCount')
      ? t('gameModules.summary.typeEffectivenessVanillaState')
      : t('gameModules.summary.typeEffectivenessBaseState');
  }
  if (record.recordKind === 'typeEffectivenessCell') {
    return record.facts.some((fact) => fact.fieldKey === 'vanillaValue')
      ? t('gameModules.summary.typeEffectivenessVanillaCell')
      : t('gameModules.summary.typeEffectivenessBaseCell');
  }
  if (record.recordKind === 'packedLooseSource') {
    return t('gameModules.summary.sourceComparison');
  }
  if (record.recordKind === 'eventComparisonSummary') {
    return t('gameModules.summary.eventDomainComparison');
  }
  if (record.recordKind === 'eventComparisonChange') {
    return record.facts.some((fact) => /(?:Base|Effective)$/u.test(fact.fieldKey))
      ? t('gameModules.summary.eventOwnedFieldComparison')
      : t('gameModules.summary.eventPresenceComparison');
  }
  if (record.recordKind === 'scenePlacementSummary') {
    return t('gameModules.summary.sceneOwnedFields');
  }
  if (record.recordKind === 'scenePlacementSource') {
    return t('gameModules.summary.sceneSourceMetadata');
  }
  if (record.recordKind === 'scenePlacementRecord') {
    return t('gameModules.summary.sceneItemPoolFields');
  }
  return null;
}
