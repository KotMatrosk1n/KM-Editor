/* SPDX-License-Identifier: GPL-3.0-only */

export type FieldHelpGame = 'swsh' | 'sv' | 'za';

export type FieldHelpDomain =
  | 'items'
  | 'pokemon'
  | 'moves'
  | 'trainers'
  | 'gifts'
  | 'trades'
  | 'staticEncounters'
  | 'rentals'
  | 'adventures'
  | 'encounters'
  | 'spawners'
  | 'raids'
  | 'raidRewards'
  | 'shops'
  | 'behavior'
  | 'placement';

export type FieldHelpInput = {
  context?: string;
  domain?: FieldHelpDomain;
  fieldId?: string;
  game?: FieldHelpGame;
  label: string;
  maximum?: number | null;
  minimum?: number | null;
  optionCount?: number;
  optionValues?: readonly (number | string)[];
};

export type FieldHelpTranslator = (
  key: string,
  params?: Record<string, string | number>
) => string;

type FieldHelpKey = `fieldHelp.catalog.${string}`;

const pokemonInstanceDomains = new Set<FieldHelpDomain>([
  'trainers',
  'gifts',
  'trades',
  'staticEncounters',
  'rentals',
  'adventures',
  'encounters',
  'spawners',
  'raids'
]);

const domainFallbackKeys: Record<FieldHelpDomain, FieldHelpKey> = {
  adventures: 'fieldHelp.catalog.domain.pokemonInstance',
  behavior: 'fieldHelp.catalog.domain.behavior',
  encounters: 'fieldHelp.catalog.domain.encounters',
  gifts: 'fieldHelp.catalog.domain.pokemonInstance',
  items: 'fieldHelp.catalog.domain.items',
  moves: 'fieldHelp.catalog.domain.moves',
  placement: 'fieldHelp.catalog.domain.placement',
  pokemon: 'fieldHelp.catalog.domain.pokemon',
  raidRewards: 'fieldHelp.catalog.domain.raidRewards',
  raids: 'fieldHelp.catalog.domain.raids',
  rentals: 'fieldHelp.catalog.domain.pokemonInstance',
  shops: 'fieldHelp.catalog.domain.shops',
  spawners: 'fieldHelp.catalog.domain.encounters',
  staticEncounters: 'fieldHelp.catalog.domain.pokemonInstance',
  trades: 'fieldHelp.catalog.domain.pokemonInstance',
  trainers: 'fieldHelp.catalog.domain.trainers'
};

const itemFieldKeys: Record<string, FieldHelpKey> = {
  accuracyboost: 'fieldHelp.catalog.pokemon.statValue',
  alternateprice: 'fieldHelp.catalog.item.price',
  attackboost: 'fieldHelp.catalog.pokemon.statValue',
  battlepouch: 'fieldHelp.catalog.item.battleUse',
  buyprice: 'fieldHelp.catalog.item.price',
  cannothold: 'fieldHelp.catalog.item.battleUse',
  canuseonpokemon: 'fieldHelp.catalog.item.battleUse',
  colorfulscrewprice: 'fieldHelp.catalog.item.price',
  criticalhitboost: 'fieldHelp.catalog.pokemon.statValue',
  cureburn: 'fieldHelp.catalog.item.statusCure',
  cureconfuse: 'fieldHelp.catalog.item.statusCure',
  curefreeze: 'fieldHelp.catalog.item.statusCure',
  cureinfatuation: 'fieldHelp.catalog.item.statusCure',
  cureparalyze: 'fieldHelp.catalog.item.statusCure',
  curepoison: 'fieldHelp.catalog.item.statusCure',
  curesleep: 'fieldHelp.catalog.item.statusCure',
  curestatusflags: 'fieldHelp.catalog.item.statusCure',
  defenseboost: 'fieldHelp.catalog.pokemon.statValue',
  effectguard: 'fieldHelp.catalog.raw.unverified',
  evolutionitem: 'fieldHelp.catalog.item.battleUse',
  expgain: 'fieldHelp.catalog.pokemon.progression',
  exppointgain: 'fieldHelp.catalog.pokemon.progression',
  fieldflags: 'fieldHelp.catalog.item.battleUse',
  fieldfunction: 'fieldHelp.catalog.item.battleUse',
  fieldusetype: 'fieldHelp.catalog.item.battleUse',
  flingpower: 'fieldHelp.catalog.item.flingPower',
  friendshipgain1: 'fieldHelp.catalog.pokemon.progression',
  friendshipgain2: 'fieldHelp.catalog.pokemon.progression',
  friendshipgain3: 'fieldHelp.catalog.pokemon.progression',
  groupindex: 'fieldHelp.catalog.item.category',
  grouptype: 'fieldHelp.catalog.item.category',
  healamount: 'fieldHelp.catalog.move.recovery',
  healpercentage: 'fieldHelp.catalog.move.recovery',
  healpower: 'fieldHelp.catalog.move.recovery',
  itemtype: 'fieldHelp.catalog.item.category',
  machinemoveid: 'fieldHelp.catalog.item.machine',
  maxuselevel: 'fieldHelp.catalog.item.maxUseLevel',
  megashardprice: 'fieldHelp.catalog.item.price',
  mintnature: 'fieldHelp.catalog.pokemon.trait',
  pocket: 'fieldHelp.catalog.item.category',
  ppgain: 'fieldHelp.catalog.item.ppGain',
  price: 'fieldHelp.catalog.item.price',
  revivalcount: 'fieldHelp.catalog.count',
  revivepercentage: 'fieldHelp.catalog.move.recovery',
  sellprice: 'fieldHelp.catalog.item.sellPrice',
  sortindex: 'fieldHelp.catalog.item.sortOrder',
  sortorder: 'fieldHelp.catalog.item.sortOrder',
  specialattackboost: 'fieldHelp.catalog.pokemon.statValue',
  specialdefenseboost: 'fieldHelp.catalog.pokemon.statValue',
  speedboost: 'fieldHelp.catalog.pokemon.statValue',
  stackcap: 'fieldHelp.catalog.count',
  tmnumber: 'fieldHelp.catalog.item.machine',
  throwpower: 'fieldHelp.catalog.item.flingPower',
  useflags1: 'fieldHelp.catalog.item.useFlags',
  useflags2: 'fieldHelp.catalog.item.useFlags',
  wattsprice: 'fieldHelp.catalog.item.price'
};

const pokemonFieldKeys: Record<string, FieldHelpKey> = {
  alphamove: 'fieldHelp.catalog.pokemon.alphaMove',
  baseexperience: 'fieldHelp.catalog.pokemon.progression',
  catchrate: 'fieldHelp.catalog.pokemon.progression',
  compatibility: 'fieldHelp.catalog.pokemon.compatibility',
  dexdestination: 'fieldHelp.catalog.pokemon.dexDestination',
  egghatchcycles: 'fieldHelp.catalog.pokemon.breeding',
  eggroup1: 'fieldHelp.catalog.pokemon.breeding',
  eggroup2: 'fieldHelp.catalog.pokemon.breeding',
  evolutionstage: 'fieldHelp.catalog.pokemon.progression',
  evolutionargument: 'fieldHelp.catalog.pokemon.evolutionArgument',
  evolutionlevel: 'fieldHelp.catalog.pokemon.evolutionLevel',
  evolutionmethod: 'fieldHelp.catalog.pokemon.evolutionMethod',
  evolutiontargetform: 'fieldHelp.catalog.pokemon.evolutionTargetForm',
  evolutiontargetspecies: 'fieldHelp.catalog.pokemon.evolutionTargetSpecies',
  expgrowth: 'fieldHelp.catalog.pokemon.progression',
  expgroup: 'fieldHelp.catalog.pokemon.progression',
  expyieldall: 'fieldHelp.catalog.pokemon.progression',
  evyieldall: 'fieldHelp.catalog.pokemon.statValue',
  friendship: 'fieldHelp.catalog.pokemon.progression',
  genderdetail: 'fieldHelp.catalog.pokemon.trait',
  genderratio: 'fieldHelp.catalog.pokemon.trait',
  growthrate: 'fieldHelp.catalog.pokemon.progression',
  learnsetlevel: 'fieldHelp.catalog.pokemon.learnsetLevel',
  learnsetmove: 'fieldHelp.catalog.pokemon.learnsetMove',
  type1: 'fieldHelp.catalog.pokemon.type',
  type2: 'fieldHelp.catalog.pokemon.type'
};

const moveFieldKeys: Record<string, FieldHelpKey> = {
  appliescondition: 'fieldHelp.catalog.move.specialConditionFlag',
  canusemove: 'fieldHelp.catalog.move.enabled',
  conditioncount: 'fieldHelp.catalog.move.duration',
  conditionid: 'fieldHelp.catalog.move.runtimeCondition',
  conditionpercent: 'fieldHelp.catalog.move.effectChance',
  conditionturnmax: 'fieldHelp.catalog.move.duration',
  conditionturnmin: 'fieldHelp.catalog.move.duration',
  criticalrank: 'fieldHelp.catalog.move.critical',
  damagedrainratio: 'fieldHelp.catalog.move.recovery',
  damagerecoverratio: 'fieldHelp.catalog.move.recovery',
  damageclass: 'fieldHelp.catalog.move.core',
  effectcategory: 'fieldHelp.catalog.move.behavior',
  effectsequence: 'fieldHelp.catalog.move.rawEffect',
  flinch: 'fieldHelp.catalog.move.flinch',
  healing: 'fieldHelp.catalog.move.healingFlag',
  heal: 'fieldHelp.catalog.move.healingFlag',
  hprecoverratio: 'fieldHelp.catalog.move.recovery',
  hitpercent: 'fieldHelp.catalog.move.accuracy',
  inflict: 'fieldHelp.catalog.move.condition',
  inflictpercent: 'fieldHelp.catalog.move.effectChance',
  power: 'fieldHelp.catalog.move.power',
  pp: 'fieldHelp.catalog.move.core',
  priority: 'fieldHelp.catalog.move.priority',
  quality: 'fieldHelp.catalog.move.rawEffect',
  rawhealing: 'fieldHelp.catalog.move.recovery',
  rawinflictcount: 'fieldHelp.catalog.move.duration',
  recoil: 'fieldHelp.catalog.move.recovery',
  shrinkpercent: 'fieldHelp.catalog.move.damageReaction',
  stat1: 'fieldHelp.catalog.move.statChange',
  stat1percent: 'fieldHelp.catalog.move.effectChance',
  stat1stage: 'fieldHelp.catalog.move.statChange',
  stat2: 'fieldHelp.catalog.move.statChange',
  stat2percent: 'fieldHelp.catalog.move.effectChance',
  stat2stage: 'fieldHelp.catalog.move.statChange',
  stat3: 'fieldHelp.catalog.move.statChange',
  stat3percent: 'fieldHelp.catalog.move.effectChance',
  stat3stage: 'fieldHelp.catalog.move.statChange',
  turnmax: 'fieldHelp.catalog.move.duration',
  turnmin: 'fieldHelp.catalog.move.duration',
  type: 'fieldHelp.catalog.move.core',
  valueeffectratio: 'fieldHelp.catalog.move.rawEffect'
};

const moveTimingFieldKeys: Record<string, FieldHelpKey> = {
  cooldown: 'fieldHelp.catalog.move.cooldown',
  effecttime: 'fieldHelp.catalog.move.timing',
  effectvalue: 'fieldHelp.catalog.move.timing',
  hitpercent: 'fieldHelp.catalog.move.accuracy',
  projectilecountmax: 'fieldHelp.catalog.move.projectile',
  projectilecountmin: 'fieldHelp.catalog.move.projectile',
  rangemax: 'fieldHelp.catalog.move.range',
  rangemin: 'fieldHelp.catalog.move.range',
  spawnlocator: 'fieldHelp.catalog.move.projectile'
};

const trainerFieldKeys: Record<string, FieldHelpKey> = {
  aiflags: 'fieldHelp.catalog.trainer.ai',
  battletype: 'fieldHelp.catalog.trainer.battleSetup',
  canterastallize: 'fieldHelp.catalog.trainer.specialMechanic',
  changegem: 'fieldHelp.catalog.trainer.specialMechanic',
  classballid: 'fieldHelp.catalog.trainer.identity',
  gift: 'fieldHelp.catalog.raw.unverified',
  heal: 'fieldHelp.catalog.raw.unverified',
  lasthand: 'fieldHelp.catalog.trainer.lastHand',
  megaevolution: 'fieldHelp.catalog.trainer.specialMechanic',
  money: 'fieldHelp.catalog.trainer.money',
  rank: 'fieldHelp.catalog.trainer.battleSetup',
  trainerclassid: 'fieldHelp.catalog.trainer.identity'
};

const tradeFieldKeys: Record<string, FieldHelpKey> = {
  field03: 'fieldHelp.catalog.raw.unverified',
  memorycode: 'fieldHelp.catalog.raw.unverified',
  memoryfeel: 'fieldHelp.catalog.raw.unverified',
  memoryintensity: 'fieldHelp.catalog.raw.unverified',
  memorytextvariable: 'fieldHelp.catalog.raw.unverified',
  otgender: 'fieldHelp.catalog.trade.provenance',
  requiredform: 'fieldHelp.catalog.trade.requirement',
  requirednature: 'fieldHelp.catalog.trade.requirement',
  requiredspecies: 'fieldHelp.catalog.trade.requirement',
  trainerid: 'fieldHelp.catalog.trade.provenance',
  unknownrequirement: 'fieldHelp.catalog.raw.unverified'
};

const adventureFieldKeys: Record<string, FieldHelpKey> = {
  fixedivpreset: 'fieldHelp.catalog.pokemon.statValue',
  gigantamaxstate: 'fieldHelp.catalog.pokemon.specialMechanic',
  guaranteedperfectivs: 'fieldHelp.catalog.pokemon.statValue',
  issinglecapture: 'fieldHelp.catalog.adventure.rule',
  isstoryprogressgated: 'fieldHelp.catalog.adventure.rule',
  shinyroll: 'fieldHelp.catalog.adventure.rule',
  version: 'fieldHelp.catalog.adventure.rule'
};

const encounterFieldKeys: Record<string, FieldHelpKey> = {
  alphachancepercent: 'fieldHelp.catalog.pokemon.specialMechanic',
  alphalevelbonus: 'fieldHelp.catalog.pokemon.specialMechanic',
  appearancemaxcount: 'fieldHelp.catalog.encounter.population',
  appearancemincount: 'fieldHelp.catalog.encounter.population',
  levelmax: 'fieldHelp.catalog.encounter.level',
  levelmin: 'fieldHelp.catalog.encounter.level',
  playerpartnerlevel: 'fieldHelp.catalog.encounter.level',
  probability: 'fieldHelp.catalog.encounter.probability',
  slotmaxcount: 'fieldHelp.catalog.encounter.population',
  weight: 'fieldHelp.catalog.encounter.weight'
};

const raidFieldKeys: Record<string, FieldHelpKey> = {
  capturelevel: 'fieldHelp.catalog.pokemon.level',
  capturerate: 'fieldHelp.catalog.pokemon.progression',
  deliverygroupid: 'fieldHelp.catalog.raid.setup',
  difficulty: 'fieldHelp.catalog.raid.setup',
  doubleactionhp: 'fieldHelp.catalog.raid.bossRule',
  doubleactionrate: 'fieldHelp.catalog.raid.bossRule',
  doubleactiontime: 'fieldHelp.catalog.raid.bossRule',
  fixedcount: 'fieldHelp.catalog.raid.reward',
  fixeditemid: 'fieldHelp.catalog.raid.reward',
  fixedrewardtable: 'fieldHelp.catalog.raid.reward',
  flawlessivs: 'fieldHelp.catalog.pokemon.statValue',
  hpmultiplier: 'fieldHelp.catalog.raid.bossRule',
  isgigantamax: 'fieldHelp.catalog.pokemon.specialMechanic',
  lotterycount: 'fieldHelp.catalog.raid.reward',
  lotteryitemid: 'fieldHelp.catalog.raid.reward',
  lotteryrate: 'fieldHelp.catalog.encounter.weight',
  lotteryrewardtable: 'fieldHelp.catalog.raid.reward',
  shieldtriggerhp: 'fieldHelp.catalog.raid.bossRule',
  shieldtriggertime: 'fieldHelp.catalog.raid.bossRule',
  spawnrate: 'fieldHelp.catalog.encounter.weight',
  version: 'fieldHelp.catalog.raid.setup'
};

const raidRewardFieldKeys: Record<string, FieldHelpKey> = {
  itemid: 'fieldHelp.catalog.reference.item',
  value: 'fieldHelp.catalog.raid.reward'
};

const shopFieldKeys: Record<string, FieldHelpKey> = {
  conditionarguments: 'fieldHelp.catalog.shop.unlock',
  conditioncomparison: 'fieldHelp.catalog.shop.unlock',
  conditionkind: 'fieldHelp.catalog.shop.unlock',
  conditionvalue: 'fieldHelp.catalog.shop.unlock',
  gymbadgecount: 'fieldHelp.catalog.shop.unlock',
  itemid: 'fieldHelp.catalog.shop.inventory',
  price: 'fieldHelp.catalog.item.price',
  setinventory: 'fieldHelp.catalog.shop.inventory',
  zaconditionarguments: 'fieldHelp.catalog.shop.unlock',
  zaconditioncomparison: 'fieldHelp.catalog.shop.unlock',
  zaconditionkind: 'fieldHelp.catalog.shop.unlock'
};

const placementFieldKeys: Record<string, FieldHelpKey> = {
  chance: 'fieldHelp.catalog.probability',
  itemid: 'fieldHelp.catalog.reference.item',
  locationx: 'fieldHelp.catalog.placement.transform',
  locationy: 'fieldHelp.catalog.placement.transform',
  locationz: 'fieldHelp.catalog.placement.transform',
  positionx: 'fieldHelp.catalog.placement.transform',
  positiony: 'fieldHelp.catalog.placement.transform',
  positionz: 'fieldHelp.catalog.placement.transform',
  quantity: 'fieldHelp.catalog.count',
  rotationy: 'fieldHelp.catalog.placement.transform',
  rotationyaw: 'fieldHelp.catalog.placement.transform',
  rotationpitch: 'fieldHelp.catalog.placement.transform',
  rotationroll: 'fieldHelp.catalog.placement.transform',
  scalex: 'fieldHelp.catalog.placement.transform',
  scaley: 'fieldHelp.catalog.placement.transform',
  scalez: 'fieldHelp.catalog.placement.transform'
};

/**
 * Returns localized, self-contained field help. Pass the already-localized field label;
 * every explanatory and metadata fragment is resolved through the supplied translator.
 */
export function resolveFieldHelp(t: FieldHelpTranslator, input: FieldHelpInput): string {
  const normalizedField = normalizeFieldId(input.fieldId);
  const semanticKey = resolveSemanticKey(input, normalizedField);
  const details = [
    t(semanticKey),
    resolveRangeHelp(t, input.minimum, input.maximum),
    resolveOptionHelp(t, input.optionCount)
  ].filter((part): part is string => Boolean(part));

  return t('fieldHelp.catalog.summary', {
    details: details.join(t('fieldHelp.catalog.detailSeparator')),
    label: trimTerminalPunctuation(input.label)
  });
}

function resolveSemanticKey(input: FieldHelpInput, field: string): FieldHelpKey {
  if (input.context?.toLocaleLowerCase().includes('unverified')) {
    return 'fieldHelp.catalog.raw.unverified';
  }

  const gameKey = resolveGameFieldKey(input, field);
  if (gameKey) {
    return gameKey;
  }

  if (field.startsWith('timing.') && input.domain === 'moves') {
    return moveTimingFieldKeys[field.slice('timing.'.length)] ?? 'fieldHelp.catalog.move.timing';
  }

  const domainKey = resolveDomainFieldKey(input.domain, field);
  if (domainKey) {
    return domainKey;
  }

  const sharedKey = resolveSharedFieldKey(field, input.domain);
  if (sharedKey) {
    return sharedKey;
  }

  if (input.domain) {
    return domainFallbackKeys[input.domain];
  }

  return (input.optionCount ?? 0) > 0
    ? 'fieldHelp.catalog.generic.selection'
    : 'fieldHelp.catalog.generic.value';
}

function resolveGameFieldKey(input: FieldHelpInput, field: string): FieldHelpKey | undefined {
  const hasZaMoveSentinels =
    input.optionValues?.some((value) => Number(value) === 0) === true &&
    input.optionValues.some((value) => Number(value) === -1);
  if (
    isMoveReferenceField(field) &&
    (input.game === 'za' || hasZaMoveSentinels) &&
    input.domain !== 'moves'
  ) {
    return 'fieldHelp.catalog.reference.move.za';
  }

  if (input.domain === 'trainers') {
    if (input.game === 'za' && field === 'lasthand') {
      return 'fieldHelp.catalog.trainer.lastHand';
    }
    if (input.game === 'za' && field === 'megaevolution') {
      return 'fieldHelp.catalog.trainer.specialMechanic';
    }
    if (input.game === 'sv' && field === 'changegem') {
      return 'fieldHelp.catalog.trainer.specialMechanic';
    }
    if (input.game === 'swsh' && (field === 'gift' || field === 'heal')) {
      return 'fieldHelp.catalog.raw.unverified';
    }
    if (input.game === 'swsh' && field === 'money') {
      return 'fieldHelp.catalog.trainer.money';
    }
  }

  if (input.game === 'swsh' && input.domain === 'items' && field === 'wattsprice') {
    return 'fieldHelp.catalog.item.price';
  }

  return undefined;
}

function resolveDomainFieldKey(
  domain: FieldHelpDomain | undefined,
  field: string
): FieldHelpKey | undefined {
  switch (domain) {
    case 'items':
      return itemFieldKeys[field];
    case 'pokemon':
      return pokemonFieldKeys[field];
    case 'moves':
      return moveFieldKeys[field];
    case 'trainers':
      return trainerFieldKeys[field];
    case 'trades':
      return tradeFieldKeys[field];
    case 'staticEncounters':
      return field === 'encounterscenario'
        ? 'fieldHelp.catalog.staticEncounter.scenario'
        : undefined;
    case 'rentals':
    case 'adventures':
      return adventureFieldKeys[field];
    case 'encounters':
    case 'spawners':
      return encounterFieldKeys[field];
    case 'raids':
      return resolveStarFieldKey(field, 'fieldHelp.catalog.raid.probability') ?? raidFieldKeys[field];
    case 'raidRewards':
      return resolveStarFieldKey(field, 'fieldHelp.catalog.raid.reward') ?? raidRewardFieldKeys[field];
    case 'shops':
      return shopFieldKeys[field];
    case 'behavior':
      return field === 'behavior'
        ? 'fieldHelp.catalog.behavior.mode'
        : 'fieldHelp.catalog.domain.behavior';
    case 'placement':
      return placementFieldKeys[field];
    case 'gifts':
    case undefined:
      return undefined;
  }
}

function resolveSharedFieldKey(
  field: string,
  domain: FieldHelpDomain | undefined
): FieldHelpKey | undefined {
  if (/(^|\.)(species|speciesid)$/.test(field) || /(fixed|coin|partner)speciesid$/.test(field)) {
    return 'fieldHelp.catalog.reference.species';
  }
  if (field === 'form' || /(fixed|coin|partner)form$/.test(field)) {
    return 'fieldHelp.catalog.reference.form';
  }
  if (field === 'level' || /(fixed|coin|partner)level$/.test(field)) {
    return 'fieldHelp.catalog.pokemon.level';
  }
  if (
    /^(helditemid|ballitemid|itemid)$/.test(field) ||
    /(helditem|dropitem|itemid)$/.test(field)
  ) {
    return 'fieldHelp.catalog.reference.item';
  }
  if (isMoveReferenceField(field)) {
    return 'fieldHelp.catalog.reference.move';
  }
  if (/^(ability|nature|gender)$/.test(field) || /(ability|nature|gender)$/.test(field)) {
    return 'fieldHelp.catalog.pokemon.trait';
  }
  if (/^(shiny|shinylock)$/.test(field) || /shiny$/.test(field)) {
    return 'fieldHelp.catalog.pokemon.shiny';
  }
  if (field === 'teratype' || /teratype$/.test(field)) {
    return 'fieldHelp.catalog.pokemon.specialMechanic';
  }
  if (/^(iv|ev|strengthen)(hp|attack|defense|specialattack|specialdefense|speed)$/.test(field)) {
    return field.startsWith('strengthen')
      ? 'fieldHelp.catalog.encounter.strength'
      : 'fieldHelp.catalog.pokemon.statValue';
  }
  if (/^(flawlessivcount|fixedivpreset|guaranteedperfectivs)$/.test(field)) {
    return 'fieldHelp.catalog.pokemon.statValue';
  }
  if (
    /^(dynamaxlevel|candynamax|cangigantamax|isgigantamax|scalemode|scalevalue)$/.test(field) ||
    /(scalemode|scalevalue|heightmode|heightvalue|weightmode|weightvalue)$/.test(field)
  ) {
    return 'fieldHelp.catalog.pokemon.specialMechanic';
  }
  if (/^traineritem\d+id$/.test(field)) {
    return 'fieldHelp.catalog.reference.item';
  }
  if (/^star\d+probability$/.test(field)) {
    return 'fieldHelp.catalog.raid.probability';
  }
  if (/^star\d+value$/.test(field)) {
    return 'fieldHelp.catalog.raid.reward';
  }
  if (field.includes('chance') || field.includes('percent') || field === 'probability') {
    return 'fieldHelp.catalog.probability';
  }
  if (field.includes('count') || field === 'quantity') {
    return 'fieldHelp.catalog.count';
  }
  if (domain !== undefined && pokemonInstanceDomains.has(domain) && field === 'name') {
    return 'fieldHelp.catalog.domain.pokemonInstance';
  }
  return undefined;
}

function isMoveReferenceField(field: string) {
  return (
    /^(move\d+id|move\d+|specialmoveid|relearnmove\d+(?:id)?)$/.test(field) ||
    /(fixed|coin|partner)move\d+(id)?$/.test(field)
  );
}

function resolveStarFieldKey(field: string, key: FieldHelpKey) {
  return /^star\d+(probability|value)$/.test(field) ? key : undefined;
}

function resolveRangeHelp(
  t: FieldHelpTranslator,
  minimum: number | null | undefined,
  maximum: number | null | undefined
) {
  if (minimum !== null && minimum !== undefined && maximum !== null && maximum !== undefined) {
    return t('fieldHelp.catalog.range.bounded', { maximum, minimum });
  }
  if (minimum !== null && minimum !== undefined) {
    return t('fieldHelp.catalog.range.minimum', { minimum });
  }
  if (maximum !== null && maximum !== undefined) {
    return t('fieldHelp.catalog.range.maximum', { maximum });
  }
  return null;
}

function resolveOptionHelp(t: FieldHelpTranslator, optionCount: number | undefined) {
  if (optionCount === undefined || optionCount <= 0) {
    return null;
  }

  return t(
    optionCount === 1
      ? 'fieldHelp.catalog.options.one'
      : 'fieldHelp.catalog.options.other',
    { count: optionCount }
  );
}

function normalizeFieldId(fieldId: string | undefined) {
  return (fieldId ?? '')
    .trim()
    .replace(/^battle\.\d+\./i, '')
    .replace(/^timing\.\d+\./i, 'timing.')
    .toLocaleLowerCase();
}

function trimTerminalPunctuation(value: string) {
  return value.trim().replace(/[.!?。！？]+$/u, '');
}
