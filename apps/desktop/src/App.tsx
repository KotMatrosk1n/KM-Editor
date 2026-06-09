/* SPDX-License-Identifier: GPL-3.0-only */

import {
  Activity,
  ArrowDown,
  ArrowLeftRight,
  ArrowUp,
  CheckCircle,
  ChevronDown,
  ClipboardCheck,
  Dna,
  ExternalLink,
  FileSpreadsheet,
  FolderOpen,
  GripVertical,
  Layers,
  ListChecks,
  MapPin,
  Package,
  Pencil,
  Plus,
  RefreshCw,
  Save,
  Search,
  ShieldCheck,
  Trash2,
  Wrench,
  X,
  Zap,
  type LucideIcon
} from 'lucide-react';
import {
  type ReactVirtualizerOptions,
  useVirtualizer
} from '@tanstack/react-virtual';
import { listen } from '@tauri-apps/api/event';
import {
  type ReactNode,
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState
} from 'react';
import {
  type ApiDiagnostic,
  type ApplyResult,
  type ChangePlan,
  type DynamaxAdventureEditableField,
  type DynamaxAdventureRecord,
  type DynamaxAdventuresWorkflow,
  type EditSession,
  type EncounterEditableField,
  type EncounterSlotRecord,
  type EncounterTableRecord,
  type EncountersWorkflow,
  type ExeFsPatchCheckRecord,
  type ExeFsPatchRecord,
  type ExeFsPatchWorkflow,
  type ExeFsSegmentRecord,
  type FlagRecord,
  type FlagworkSaveWorkflow,
  type GiftPokemonEditableField,
  type GiftPokemonRecord,
  type GiftPokemonWorkflow,
  type ItemEditableField,
  type ItemsWorkflow,
  type ItemRecord,
  type MoveEditableField,
  type MoveRecord,
  type MovesWorkflow,
  type PokemonCompatibilityGroup,
  type PokemonEditableField,
  type PokemonEditableFieldOption,
  type PokemonEvolutionMethodOption,
  type PokemonEvolutionRecord,
  type PokemonRecord,
  type PokemonWorkflow,
  type PlacedObjectRecord,
  type PlacementEditableField,
  type PlacementWorkflow,
  type ProjectHealth,
  type ProjectPathRole,
  type ProjectPathValidation,
  type RaidBattleEditableField,
  type RaidBattleSlotRecord,
  type RaidBattleTableRecord,
  type RaidBattlesWorkflow,
  type RaidRewardEditableField,
  type RaidRewardItemRecord,
  type RaidRewardTableRecord,
  type RaidRewardsWorkflow,
  type RentalPokemonEditableField,
  type RentalPokemonRecord,
  type RentalPokemonWorkflow,
  type RoyalCandyOutputRecord,
  type RoyalCandyWorkflow,
  type RoyalCandyWorkflowCheckRecord,
  type RoyalCandyWorkflowRecord,
  type SaveBlockRecord,
  type SaveFileRecord,
  type ShopEditableField,
  type ShopInventoryRecord,
  type ShopRecord,
  type ShopsWorkflow,
  type SpreadsheetImportPreview,
  type SpreadsheetImportProfileRecord,
  type SpreadsheetImportWorkflow,
  type StaticEncounterEditableField,
  type StaticEncounterRecord,
  type StaticEncountersWorkflow,
  type TextEditableField,
  type TextEntryRecord,
  type TextWorkflow,
  type TradePokemonEditableField,
  type TradePokemonRecord,
  type TradePokemonWorkflow,
  type TrainerEditableField,
  type TrainerPokemonRecord,
  type TrainerRecord,
  type TrainersWorkflow,
  type WorkflowSummary
} from './bridge/contracts';
import {
  ProjectBridgeError,
  projectBridge as defaultProjectBridge,
  type ProjectBridge
} from './bridge/projectBridge';
import {
  desktopServices as defaultDesktopServices,
  type DesktopServices
} from './desktopServices';
import {
  type ProjectPathDraft,
  type WorkbenchSection,
  useWorkbenchStore
} from './workbenchStore';
import kmLogoUrl from './assets/km-logo.png';

const sections: Array<{
  id: WorkbenchSection;
  label: string;
  icon: LucideIcon;
}> = [
  {
    id: 'health',
    label: 'Project Health',
    icon: Activity
  },
  {
    id: 'workflows',
    label: 'Workflows',
    icon: ListChecks
  },
  {
    id: 'items',
    label: 'Items',
    icon: Package
  },
  {
    id: 'pokemon',
    label: 'Pokemon Data',
    icon: Dna
  },
  {
    id: 'moves',
    label: 'Moves Data',
    icon: Zap
  },
  {
    id: 'text',
    label: 'Text',
    icon: ListChecks
  },
  {
    id: 'trainers',
    label: 'Trainers',
    icon: Activity
  },
  {
    id: 'giftPokemon',
    label: 'Gift Pokemon',
    icon: Dna
  },
  {
    id: 'tradePokemon',
    label: 'Trade Pokemon',
    icon: ArrowLeftRight
  },
  {
    id: 'staticEncounters',
    label: 'Static Encounters',
    icon: MapPin
  },
  {
    id: 'rentalPokemon',
    label: 'Rental Pokemon',
    icon: Dna
  },
  {
    id: 'dynamaxAdventures',
    label: 'Dynamax Adventures',
    icon: ShieldCheck
  },
  {
    id: 'shops',
    label: 'Shops',
    icon: ListChecks
  },
  {
    id: 'encounters',
    label: 'Encounters',
    icon: Layers
  },
  {
    id: 'raidBattles',
    label: 'Raid Battles',
    icon: ShieldCheck
  },
  {
    id: 'raidRewards',
    label: 'Raid Rewards',
    icon: ShieldCheck
  },
  {
    id: 'placement',
    label: 'Placement',
    icon: MapPin
  },
  {
    id: 'flagworkSave',
    label: 'Flagwork / Save',
    icon: Save
  },
  {
    id: 'exefsPatches',
    label: 'ExeFS Patches',
    icon: Wrench
  },
  {
    id: 'royalCandy',
    label: 'Royal Candy',
    icon: CheckCircle
  },
  {
    id: 'spreadsheetImport',
    label: 'Spreadsheet Import',
    icon: FileSpreadsheet
  },
  {
    id: 'changes',
    label: 'Changes',
    icon: ClipboardCheck
  }
];

type WorkflowNavigationGroup = {
  id: 'viewers' | 'editors' | 'advancedEditors';
  label: string;
  sectionIds: WorkbenchSection[];
};

const workflowNavigationGroups: WorkflowNavigationGroup[] = [
  {
    id: 'viewers',
    label: 'Viewers',
    sectionIds: ['flagworkSave']
  },
  {
    id: 'editors',
    label: 'Editors',
    sectionIds: [
      'items',
      'pokemon',
      'moves',
      'text',
      'trainers',
      'giftPokemon',
      'tradePokemon',
      'staticEncounters',
      'rentalPokemon',
      'dynamaxAdventures',
      'shops',
      'encounters',
      'raidBattles',
      'raidRewards',
      'placement',
      'spreadsheetImport'
    ]
  },
  {
    id: 'advancedEditors',
    label: 'Advanced Editors',
    sectionIds: ['exefsPatches', 'royalCandy']
  }
];

const groupedWorkflowSectionIds = new Set(
  workflowNavigationGroups.flatMap((group) => group.sectionIds)
);
const primaryNavigationSections = sections.filter(
  (section) => section.id === 'health' || section.id === 'workflows'
);
const utilityNavigationSections = sections.filter((section) => section.id === 'changes');

const workflowDefinitions: Array<{
  id: string;
  label: string;
  description: string;
  icon: LucideIcon;
}> = [
  {
    id: 'items',
    label: 'Items',
    description: 'Item records, names, and source provenance.',
    icon: Package
  },
  {
    id: 'pokemon',
    label: 'Pokemon Data',
    description: 'Pokemon personal stats, forms, evolutions, learnsets, and source provenance.',
    icon: Dna
  },
  {
    id: 'moves',
    label: 'Moves Data',
    description: 'Move stats, target behavior, secondary effects, flags, and source provenance.',
    icon: Zap
  },
  {
    id: 'text',
    label: 'Text and Dialogue Map',
    description: 'Text entries, dialogue references, and source provenance.',
    icon: ListChecks
  },
  {
    id: 'trainers',
    label: 'Trainers',
    description: 'Trainer parties, classes, battle types, and source provenance.',
    icon: Activity
  },
  {
    id: 'giftPokemon',
    label: 'Gift Pokemon',
    description: 'Scripted gift Pokemon records, IV modes, items, moves, and source provenance.',
    icon: Dna
  },
  {
    id: 'tradePokemon',
    label: 'Trade Pokemon',
    description: 'In-game trade records, requested Pokemon, IV modes, relearn moves, and source provenance.',
    icon: ArrowLeftRight
  },
  {
    id: 'staticEncounters',
    label: 'Static Encounters',
    description: 'Scripted overworld and story encounter records, IV modes, moves, rules, and source provenance.',
    icon: MapPin
  },
  {
    id: 'rentalPokemon',
    label: 'Rental Pokemon',
    description: 'Rental Pokemon records, fixed IVs, EVs, items, moves, and source provenance.',
    icon: Dna
  },
  {
    id: 'dynamaxAdventures',
    label: 'Dynamax Adventures',
    description:
      'Adventure encounter Pokemon, ability rolls, moves, IV overrides, capture rules, and source provenance.',
    icon: ShieldCheck
  },
  {
    id: 'shops',
    label: 'Shops',
    description: 'Shop inventories, prices, stock limits, and source provenance.',
    icon: ListChecks
  },
  {
    id: 'encounters',
    label: 'Encounters and Wild Data',
    description: 'Encounter tables, wild slots, levels, weather, and source provenance.',
    icon: Layers
  },
  {
    id: 'raidBattles',
    label: 'Raid Battles',
    description: 'Raid Pokemon slots, star probabilities, ability rolls, guaranteed perfect IVs, and source provenance.',
    icon: ShieldCheck
  },
  {
    id: 'raidRewards',
    label: 'Raid Rewards',
    description: 'Raid reward tables, den ranks, item quantities, and source provenance.',
    icon: ShieldCheck
  },
  {
    id: 'placement',
    label: 'Placement',
    description: 'Placed objects, map coordinates, script links, and source provenance.',
    icon: MapPin
  },
  {
    id: 'flagworkSave',
    label: 'Flagwork and Save Inspectors',
    description: 'Game flags, save blocks, inspector metadata, and source provenance.',
    icon: Save
  },
  {
    id: 'exefsPatches',
    label: 'ExeFS Patch Manager',
    description: 'ExeFS main validation, patch anchors, segment hashes, and source provenance.',
    icon: Wrench
  },
  {
    id: 'royalCandy',
    label: 'Royal Candy Workflows',
    description: 'Royal Candy source readiness, ExeFS compatibility, and LayeredFS output preview.',
    icon: CheckCircle
  },
  {
    id: 'spreadsheetImport',
    label: 'Spreadsheet Import',
    description: 'CSV and TSV import profiles that execute through backend edit sessions.',
    icon: FileSpreadsheet
  }
];

const pathFields: Array<{
  field: keyof ProjectPathDraft;
  kind: 'directory' | 'file';
  label: string;
  role: ProjectPathRole;
}> = [
  {
    field: 'baseRomFsPath',
    kind: 'directory',
    label: 'Base RomFS',
    role: 'baseRomFs'
  },
  {
    field: 'baseExeFsPath',
    kind: 'directory',
    label: 'Base ExeFS',
    role: 'baseExeFs'
  },
  {
    field: 'outputRootPath',
    kind: 'directory',
    label: 'Output Root',
    role: 'outputRoot'
  },
  {
    field: 'saveFilePath',
    kind: 'file',
    label: 'Save File',
    role: 'saveFile'
  }
];
type ProjectPathField = (typeof pathFields)[number];

const healthLabels = {
  blocked: 'Blocked',
  editableReady: 'Editable ready',
  needsPaths: 'Needs paths',
  readOnlyReady: 'Read-only ready'
} as const satisfies Record<ProjectHealth['state'], string>;

const pathStatusLabels = {
  missing: 'Missing',
  notSet: 'Not set',
  unsafe: 'Unsafe',
  valid: 'Valid',
  wrongKind: 'Wrong kind'
} as const;

const buyPriceFieldName = 'buyPrice';
const sellPriceFieldName = 'sellPrice';
const wattsPriceFieldName = 'wattsPrice';
const alternatePriceFieldName = 'alternatePrice';
const trainerClassIdFieldName = 'trainerClassId';
const classBallIdFieldName = 'classBallId';
const battleTypeFieldName = 'battleType';
const trainerItemFieldNames = [
  'trainerItem1Id',
  'trainerItem2Id',
  'trainerItem3Id',
  'trainerItem4Id'
] as const;
const aiFlagsFieldName = 'aiFlags';
const healFieldName = 'heal';
const moneyFieldName = 'money';
const giftFieldName = 'gift';
const speciesIdFieldName = 'speciesId';
const formFieldName = 'form';
const levelFieldName = 'level';
const heldItemIdFieldName = 'heldItemId';
const moveFieldNames = ['move1Id', 'move2Id', 'move3Id', 'move4Id'] as const;
const genderFieldName = 'gender';
const abilityFieldName = 'ability';
const natureFieldName = 'nature';
const evFieldNames = [
  'evHp',
  'evAttack',
  'evDefense',
  'evSpecialAttack',
  'evSpecialDefense',
  'evSpeed'
] as const;
const dynamaxLevelFieldName = 'dynamaxLevel';
const canGigantamaxFieldName = 'canGigantamax';
const ivFieldNames = [
  'ivHp',
  'ivAttack',
  'ivDefense',
  'ivSpecialAttack',
  'ivSpecialDefense',
  'ivSpeed'
] as const;
const shinyFieldName = 'shiny';
const canDynamaxFieldName = 'canDynamax';
const windowCloseRequestedEvent = 'km-editor://window-close-requested';
const trainerDataFieldNames = [
  trainerClassIdFieldName,
  classBallIdFieldName,
  battleTypeFieldName,
  ...trainerItemFieldNames,
  healFieldName,
  moneyFieldName,
  giftFieldName
] as const;
const trainerPokemonFieldNames = [
  speciesIdFieldName,
  formFieldName,
  levelFieldName,
  heldItemIdFieldName,
  ...moveFieldNames,
  genderFieldName,
  abilityFieldName,
  natureFieldName,
  ...evFieldNames,
  dynamaxLevelFieldName,
  canGigantamaxFieldName,
  ...ivFieldNames,
  shinyFieldName,
  canDynamaxFieldName
] as const;
const giftSpeciesFieldName = 'species';
const giftBallItemIdFieldName = 'ballItemId';
const giftShinyLockFieldName = 'shinyLock';
const giftSpecialMoveIdFieldName = 'specialMoveId';
const giftFlawlessIvCountFieldName = 'flawlessIvCount';
const giftPokemonFieldNames = [
  giftSpeciesFieldName,
  formFieldName,
  levelFieldName,
  heldItemIdFieldName,
  giftBallItemIdFieldName,
  abilityFieldName,
  natureFieldName,
  genderFieldName,
  giftShinyLockFieldName,
  dynamaxLevelFieldName,
  canGigantamaxFieldName,
  giftSpecialMoveIdFieldName,
  ...ivFieldNames,
  giftFlawlessIvCountFieldName
] as const;
const tradeField03FieldName = 'field03';
const tradeRequiredSpeciesFieldName = 'requiredSpecies';
const tradeRequiredFormFieldName = 'requiredForm';
const tradeRequiredNatureFieldName = 'requiredNature';
const tradeUnknownRequirementFieldName = 'unknownRequirement';
const tradeTrainerIdFieldName = 'trainerId';
const tradeOtGenderFieldName = 'otGender';
const tradeMemoryCodeFieldName = 'memoryCode';
const tradeMemoryTextVariableFieldName = 'memoryTextVariable';
const tradeMemoryFeelFieldName = 'memoryFeel';
const tradeMemoryIntensityFieldName = 'memoryIntensity';
const lockedTradeFieldNames = [
  tradeField03FieldName,
  tradeUnknownRequirementFieldName,
  tradeMemoryCodeFieldName,
  tradeMemoryTextVariableFieldName,
  tradeMemoryFeelFieldName,
  tradeMemoryIntensityFieldName
] as const;
const tradeRelearnMoveFieldNames = [
  'relearnMove0',
  'relearnMove1',
  'relearnMove2',
  'relearnMove3'
] as const;
const tradePokemonFieldNames = [
  giftSpeciesFieldName,
  formFieldName,
  levelFieldName,
  heldItemIdFieldName,
  giftBallItemIdFieldName,
  tradeField03FieldName,
  abilityFieldName,
  natureFieldName,
  genderFieldName,
  giftShinyLockFieldName,
  dynamaxLevelFieldName,
  canGigantamaxFieldName,
  tradeRequiredSpeciesFieldName,
  tradeRequiredFormFieldName,
  tradeRequiredNatureFieldName,
  tradeUnknownRequirementFieldName,
  tradeTrainerIdFieldName,
  tradeOtGenderFieldName,
  tradeMemoryCodeFieldName,
  tradeMemoryTextVariableFieldName,
  tradeMemoryFeelFieldName,
  tradeMemoryIntensityFieldName,
  ...tradeRelearnMoveFieldNames,
  ...ivFieldNames,
  giftFlawlessIvCountFieldName
] as const;
const staticEncounterScenarioFieldName = 'encounterScenario';
const staticEncounterMoveFieldNames = ['move0Id', 'move1Id', 'move2Id', 'move3Id'] as const;
const staticEncounterFieldNames = [
  giftSpeciesFieldName,
  formFieldName,
  levelFieldName,
  heldItemIdFieldName,
  abilityFieldName,
  natureFieldName,
  genderFieldName,
  giftShinyLockFieldName,
  staticEncounterScenarioFieldName,
  dynamaxLevelFieldName,
  canGigantamaxFieldName,
  ...staticEncounterMoveFieldNames,
  ...evFieldNames,
  ...ivFieldNames,
  giftFlawlessIvCountFieldName
] as const;
const rentalFixedIvPresetFieldName = 'fixedIvPreset';
const rentalPokemonFieldNames = [
  giftSpeciesFieldName,
  formFieldName,
  levelFieldName,
  heldItemIdFieldName,
  giftBallItemIdFieldName,
  abilityFieldName,
  natureFieldName,
  genderFieldName,
  tradeTrainerIdFieldName,
  ...staticEncounterMoveFieldNames,
  ...evFieldNames,
  ...ivFieldNames,
  rentalFixedIvPresetFieldName
] as const;
const dynamaxAdventureBallItemIdFieldName = 'ballItemId';
const dynamaxAdventureGigantamaxStateFieldName = 'gigantamaxState';
const dynamaxAdventureVersionFieldName = 'version';
const dynamaxAdventureShinyRollFieldName = 'shinyRoll';
const dynamaxAdventureGuaranteedPerfectIvsFieldName = 'guaranteedPerfectIvs';
const dynamaxAdventureIvFieldNames = [
  'ivAttack',
  'ivDefense',
  'ivSpecialAttack',
  'ivSpecialDefense',
  'ivSpeed'
] as const;
const dynamaxAdventureIsSingleCaptureFieldName = 'isSingleCapture';
const dynamaxAdventureIsStoryProgressGatedFieldName = 'isStoryProgressGated';
const dynamaxAdventureOtGenderFieldName = 'otGender';
const dynamaxAdventureFieldNames = [
  giftSpeciesFieldName,
  formFieldName,
  levelFieldName,
  dynamaxAdventureBallItemIdFieldName,
  abilityFieldName,
  dynamaxAdventureGigantamaxStateFieldName,
  dynamaxAdventureVersionFieldName,
  dynamaxAdventureShinyRollFieldName,
  ...staticEncounterMoveFieldNames,
  dynamaxAdventureGuaranteedPerfectIvsFieldName,
  ...dynamaxAdventureIvFieldNames,
  dynamaxAdventureIsSingleCaptureFieldName,
  dynamaxAdventureIsStoryProgressGatedFieldName,
  dynamaxAdventureOtGenderFieldName
] as const;
const shopItemIdFieldName = 'itemId';
const encounterSpeciesFieldName = speciesIdFieldName;
const encounterFormFieldName = 'form';
const encounterProbabilityFieldName = 'probability';
const encounterLevelMinFieldName = 'levelMin';
const encounterLevelMaxFieldName = 'levelMax';
const encounterConditionLabels = [
  'Normal',
  'Overcast',
  'Raining',
  'Thunderstorm',
  'Intense Sun',
  'Snowing',
  'Snowstorm',
  'Sandstorm',
  'Heavy Fog',
  'Fishing',
  'Shaking Trees'
] as const;
const encounterWeatherCopyLabels = new Set([
  'Overcast',
  'Raining',
  'Thunderstorm',
  'Intense Sun',
  'Snowing',
  'Snowstorm',
  'Sandstorm',
  'Heavy Fog'
]);
const raidBattleSpeciesFieldName = 'species';
const raidBattleFormFieldName = 'form';
const raidBattleAbilityFieldName = 'ability';
const raidBattleIsGigantamaxFieldName = 'isGigantamax';
const raidBattleGenderFieldName = 'gender';
const raidBattleFlawlessIvsFieldName = 'flawlessIvs';
const raidBattleProbabilityFieldNames = [
  'star1Probability',
  'star2Probability',
  'star3Probability',
  'star4Probability',
  'star5Probability'
] as const;
const raidRewardItemIdFieldName = 'itemId';
const raidRewardValueFieldNames = [
  'star1Value',
  'star2Value',
  'star3Value',
  'star4Value',
  'star5Value'
] as const;
const placementLocationXFieldName = 'locationX';
const placementLocationYFieldName = 'locationY';
const placementLocationZFieldName = 'locationZ';
const placementRotationYFieldName = 'rotationY';
const placementItemIdFieldName = 'itemId';
const placementQuantityFieldName = 'quantity';
const placementChanceFieldName = 'chance';
const virtualTableInitialRect = { height: 480, width: 800 };
const virtualTableOverscan = 8;
const virtualTableRowHeight = 40;
const observeVirtualTableElementRect:
  | ReactVirtualizerOptions<HTMLDivElement, HTMLDivElement>['observeElementRect']
  | undefined =
  typeof ResizeObserver === 'undefined'
    ? (_instance, callback) => {
        callback(virtualTableInitialRect);
        return () => undefined;
      }
    : undefined;

export function App({
  bridge = defaultProjectBridge,
  desktopServices = defaultDesktopServices
}: {
  bridge?: ProjectBridge;
  desktopServices?: DesktopServices;
} = {}) {
  const activeSection = useWorkbenchStore((state) => state.activeSection);
  const applyResult = useWorkbenchStore((state) => state.applyResult);
  const changePlan = useWorkbenchStore((state) => state.changePlan);
  const draftPaths = useWorkbenchStore((state) => state.draftPaths);
  const editSession = useWorkbenchStore((state) => state.editSession);
  const editValidationDiagnostics = useWorkbenchStore((state) => state.editValidationDiagnostics);
  const encounterSearchText = useWorkbenchStore((state) => state.encounterSearchText);
  const encountersWorkflow = useWorkbenchStore((state) => state.encountersWorkflow);
  const exeFsPatchSearchText = useWorkbenchStore((state) => state.exeFsPatchSearchText);
  const exeFsPatchWorkflow = useWorkbenchStore((state) => state.exeFsPatchWorkflow);
  const flagworkSaveSearchText = useWorkbenchStore((state) => state.flagworkSaveSearchText);
  const flagworkSaveWorkflow = useWorkbenchStore((state) => state.flagworkSaveWorkflow);
  const giftPokemonSearchText = useWorkbenchStore((state) => state.giftPokemonSearchText);
  const giftPokemonWorkflow = useWorkbenchStore((state) => state.giftPokemonWorkflow);
  const tradePokemonSearchText = useWorkbenchStore((state) => state.tradePokemonSearchText);
  const tradePokemonWorkflow = useWorkbenchStore((state) => state.tradePokemonWorkflow);
  const staticEncounterSearchText = useWorkbenchStore(
    (state) => state.staticEncounterSearchText
  );
  const staticEncountersWorkflow = useWorkbenchStore(
    (state) => state.staticEncountersWorkflow
  );
  const rentalPokemonSearchText = useWorkbenchStore(
    (state) => state.rentalPokemonSearchText
  );
  const rentalPokemonWorkflow = useWorkbenchStore((state) => state.rentalPokemonWorkflow);
  const dynamaxAdventureSearchText = useWorkbenchStore(
    (state) => state.dynamaxAdventureSearchText
  );
  const dynamaxAdventuresWorkflow = useWorkbenchStore(
    (state) => state.dynamaxAdventuresWorkflow
  );
  const itemSearchText = useWorkbenchStore((state) => state.itemSearchText);
  const itemsWorkflow = useWorkbenchStore((state) => state.itemsWorkflow);
  const movesSearchText = useWorkbenchStore((state) => state.movesSearchText);
  const movesWorkflow = useWorkbenchStore((state) => state.movesWorkflow);
  const openProject = useWorkbenchStore((state) => state.openProject);
  const placementSearchText = useWorkbenchStore((state) => state.placementSearchText);
  const placementWorkflow = useWorkbenchStore((state) => state.placementWorkflow);
  const pokemonSearchText = useWorkbenchStore((state) => state.pokemonSearchText);
  const pokemonWorkflow = useWorkbenchStore((state) => state.pokemonWorkflow);
  const projectStatus = useWorkbenchStore((state) => state.projectStatus);
  const raidBattleSearchText = useWorkbenchStore((state) => state.raidBattleSearchText);
  const raidBattlesWorkflow = useWorkbenchStore((state) => state.raidBattlesWorkflow);
  const raidRewardSearchText = useWorkbenchStore((state) => state.raidRewardSearchText);
  const raidRewardsWorkflow = useWorkbenchStore((state) => state.raidRewardsWorkflow);
  const royalCandySearchText = useWorkbenchStore((state) => state.royalCandySearchText);
  const royalCandyWorkflow = useWorkbenchStore((state) => state.royalCandyWorkflow);
  const spreadsheetImportPreview = useWorkbenchStore(
    (state) => state.spreadsheetImportPreview
  );
  const spreadsheetImportSearchText = useWorkbenchStore(
    (state) => state.spreadsheetImportSearchText
  );
  const spreadsheetImportSourcePath = useWorkbenchStore(
    (state) => state.spreadsheetImportSourcePath
  );
  const spreadsheetImportWorkflow = useWorkbenchStore(
    (state) => state.spreadsheetImportWorkflow
  );
  const selectedEncounterTableId = useWorkbenchStore((state) => state.selectedEncounterTableId);
  const selectedItemId = useWorkbenchStore((state) => state.selectedItemId);
  const selectedMoveId = useWorkbenchStore((state) => state.selectedMoveId);
  const selectedPokemonPersonalId = useWorkbenchStore(
    (state) => state.selectedPokemonPersonalId
  );
  const selectedRaidBattleTableId = useWorkbenchStore(
    (state) => state.selectedRaidBattleTableId
  );
  const selectedRaidRewardTableId = useWorkbenchStore(
    (state) => state.selectedRaidRewardTableId
  );
  const selectedPlacementObjectId = useWorkbenchStore(
    (state) => state.selectedPlacementObjectId
  );
  const selectedFlagId = useWorkbenchStore((state) => state.selectedFlagId);
  const selectedExeFsCheckId = useWorkbenchStore((state) => state.selectedExeFsCheckId);
  const selectedExeFsPatchId = useWorkbenchStore((state) => state.selectedExeFsPatchId);
  const selectedGiftPokemonIndex = useWorkbenchStore(
    (state) => state.selectedGiftPokemonIndex
  );
  const selectedTradePokemonIndex = useWorkbenchStore(
    (state) => state.selectedTradePokemonIndex
  );
  const selectedStaticEncounterIndex = useWorkbenchStore(
    (state) => state.selectedStaticEncounterIndex
  );
  const selectedRentalPokemonIndex = useWorkbenchStore(
    (state) => state.selectedRentalPokemonIndex
  );
  const selectedDynamaxAdventureEntryIndex = useWorkbenchStore(
    (state) => state.selectedDynamaxAdventureEntryIndex
  );
  const selectedRoyalCandyCheckId = useWorkbenchStore(
    (state) => state.selectedRoyalCandyCheckId
  );
  const selectedRoyalCandyWorkflowId = useWorkbenchStore(
    (state) => state.selectedRoyalCandyWorkflowId
  );
  const selectedSpreadsheetImportProfileId = useWorkbenchStore(
    (state) => state.selectedSpreadsheetImportProfileId
  );
  const selectedShopId = useWorkbenchStore((state) => state.selectedShopId);
  const selectedSaveBlockId = useWorkbenchStore((state) => state.selectedSaveBlockId);
  const selectedTextKey = useWorkbenchStore((state) => state.selectedTextKey);
  const selectedTrainerId = useWorkbenchStore((state) => state.selectedTrainerId);
  const shopSearchText = useWorkbenchStore((state) => state.shopSearchText);
  const shopsWorkflow = useWorkbenchStore((state) => state.shopsWorkflow);
  const textSearchText = useWorkbenchStore((state) => state.textSearchText);
  const textWorkflow = useWorkbenchStore((state) => state.textWorkflow);
  const trainerSearchText = useWorkbenchStore((state) => state.trainerSearchText);
  const trainersWorkflow = useWorkbenchStore((state) => state.trainersWorkflow);
  const workflows = useWorkbenchStore((state) => state.workflows);
  const setActiveSection = useWorkbenchStore((state) => state.setActiveSection);
  const setApplyResult = useWorkbenchStore((state) => state.setApplyResult);
  const setChangePlan = useWorkbenchStore((state) => state.setChangePlan);
  const setDraftPath = useWorkbenchStore((state) => state.setDraftPath);
  const setEditSession = useWorkbenchStore((state) => state.setEditSession);
  const setEditValidationDiagnostics = useWorkbenchStore(
    (state) => state.setEditValidationDiagnostics
  );
  const setEncounterSearchText = useWorkbenchStore((state) => state.setEncounterSearchText);
  const setEncountersWorkflow = useWorkbenchStore((state) => state.setEncountersWorkflow);
  const setExeFsPatchSearchText = useWorkbenchStore(
    (state) => state.setExeFsPatchSearchText
  );
  const setExeFsPatchWorkflow = useWorkbenchStore((state) => state.setExeFsPatchWorkflow);
  const setFlagworkSaveSearchText = useWorkbenchStore(
    (state) => state.setFlagworkSaveSearchText
  );
  const setFlagworkSaveWorkflow = useWorkbenchStore((state) => state.setFlagworkSaveWorkflow);
  const setGiftPokemonSearchText = useWorkbenchStore(
    (state) => state.setGiftPokemonSearchText
  );
  const setGiftPokemonWorkflow = useWorkbenchStore((state) => state.setGiftPokemonWorkflow);
  const setTradePokemonSearchText = useWorkbenchStore(
    (state) => state.setTradePokemonSearchText
  );
  const setTradePokemonWorkflow = useWorkbenchStore((state) => state.setTradePokemonWorkflow);
  const setStaticEncounterSearchText = useWorkbenchStore(
    (state) => state.setStaticEncounterSearchText
  );
  const setStaticEncountersWorkflow = useWorkbenchStore(
    (state) => state.setStaticEncountersWorkflow
  );
  const setRentalPokemonSearchText = useWorkbenchStore(
    (state) => state.setRentalPokemonSearchText
  );
  const setRentalPokemonWorkflow = useWorkbenchStore(
    (state) => state.setRentalPokemonWorkflow
  );
  const setDynamaxAdventureSearchText = useWorkbenchStore(
    (state) => state.setDynamaxAdventureSearchText
  );
  const setDynamaxAdventuresWorkflow = useWorkbenchStore(
    (state) => state.setDynamaxAdventuresWorkflow
  );
  const setItemSearchText = useWorkbenchStore((state) => state.setItemSearchText);
  const setItemsWorkflow = useWorkbenchStore((state) => state.setItemsWorkflow);
  const setMovesSearchText = useWorkbenchStore((state) => state.setMovesSearchText);
  const setMovesWorkflow = useWorkbenchStore((state) => state.setMovesWorkflow);
  const setOpenProject = useWorkbenchStore((state) => state.setOpenProject);
  const setPlacementSearchText = useWorkbenchStore((state) => state.setPlacementSearchText);
  const setPlacementWorkflow = useWorkbenchStore((state) => state.setPlacementWorkflow);
  const setPokemonSearchText = useWorkbenchStore((state) => state.setPokemonSearchText);
  const setPokemonWorkflow = useWorkbenchStore((state) => state.setPokemonWorkflow);
  const setProjectHealth = useWorkbenchStore((state) => state.setProjectHealth);
  const setProjectStatus = useWorkbenchStore((state) => state.setProjectStatus);
  const setRaidBattleSearchText = useWorkbenchStore((state) => state.setRaidBattleSearchText);
  const setRaidBattlesWorkflow = useWorkbenchStore((state) => state.setRaidBattlesWorkflow);
  const setRaidRewardSearchText = useWorkbenchStore((state) => state.setRaidRewardSearchText);
  const setRaidRewardsWorkflow = useWorkbenchStore((state) => state.setRaidRewardsWorkflow);
  const setRoyalCandySearchText = useWorkbenchStore((state) => state.setRoyalCandySearchText);
  const setRoyalCandyWorkflow = useWorkbenchStore((state) => state.setRoyalCandyWorkflow);
  const setSpreadsheetImportPreview = useWorkbenchStore(
    (state) => state.setSpreadsheetImportPreview
  );
  const setSpreadsheetImportSearchText = useWorkbenchStore(
    (state) => state.setSpreadsheetImportSearchText
  );
  const setSpreadsheetImportSourcePath = useWorkbenchStore(
    (state) => state.setSpreadsheetImportSourcePath
  );
  const setSpreadsheetImportWorkflow = useWorkbenchStore(
    (state) => state.setSpreadsheetImportWorkflow
  );
  const setSelectedRaidRewardTableId = useWorkbenchStore(
    (state) => state.setSelectedRaidRewardTableId
  );
  const setSelectedRaidBattleTableId = useWorkbenchStore(
    (state) => state.setSelectedRaidBattleTableId
  );
  const setSelectedPlacementObjectId = useWorkbenchStore(
    (state) => state.setSelectedPlacementObjectId
  );
  const setSelectedEncounterTableId = useWorkbenchStore(
    (state) => state.setSelectedEncounterTableId
  );
  const setSelectedExeFsCheckId = useWorkbenchStore(
    (state) => state.setSelectedExeFsCheckId
  );
  const setSelectedExeFsPatchId = useWorkbenchStore(
    (state) => state.setSelectedExeFsPatchId
  );
  const setSelectedGiftPokemonIndex = useWorkbenchStore(
    (state) => state.setSelectedGiftPokemonIndex
  );
  const setSelectedTradePokemonIndex = useWorkbenchStore(
    (state) => state.setSelectedTradePokemonIndex
  );
  const setSelectedStaticEncounterIndex = useWorkbenchStore(
    (state) => state.setSelectedStaticEncounterIndex
  );
  const setSelectedRentalPokemonIndex = useWorkbenchStore(
    (state) => state.setSelectedRentalPokemonIndex
  );
  const setSelectedDynamaxAdventureEntryIndex = useWorkbenchStore(
    (state) => state.setSelectedDynamaxAdventureEntryIndex
  );
  const setSelectedRoyalCandyCheckId = useWorkbenchStore(
    (state) => state.setSelectedRoyalCandyCheckId
  );
  const setSelectedRoyalCandyWorkflowId = useWorkbenchStore(
    (state) => state.setSelectedRoyalCandyWorkflowId
  );
  const setSelectedSpreadsheetImportProfileId = useWorkbenchStore(
    (state) => state.setSelectedSpreadsheetImportProfileId
  );
  const setSelectedFlagId = useWorkbenchStore((state) => state.setSelectedFlagId);
  const setSelectedItemId = useWorkbenchStore((state) => state.setSelectedItemId);
  const setSelectedMoveId = useWorkbenchStore((state) => state.setSelectedMoveId);
  const setSelectedPokemonPersonalId = useWorkbenchStore(
    (state) => state.setSelectedPokemonPersonalId
  );
  const setSelectedSaveBlockId = useWorkbenchStore((state) => state.setSelectedSaveBlockId);
  const setSelectedShopId = useWorkbenchStore((state) => state.setSelectedShopId);
  const setSelectedTextKey = useWorkbenchStore((state) => state.setSelectedTextKey);
  const setSelectedTrainerId = useWorkbenchStore((state) => state.setSelectedTrainerId);
  const setShopSearchText = useWorkbenchStore((state) => state.setShopSearchText);
  const setShopsWorkflow = useWorkbenchStore((state) => state.setShopsWorkflow);
  const setTextSearchText = useWorkbenchStore((state) => state.setTextSearchText);
  const setTextWorkflow = useWorkbenchStore((state) => state.setTextWorkflow);
  const setTrainerSearchText = useWorkbenchStore((state) => state.setTrainerSearchText);
  const setTrainersWorkflow = useWorkbenchStore((state) => state.setTrainersWorkflow);
  const setWorkflows = useWorkbenchStore((state) => state.setWorkflows);
  const health = openProject?.health ?? null;
  const activeSectionLabel = sections.find((section) => section.id === activeSection)?.label;
  const isBusy = projectStatus === 'opening' || projectStatus === 'validating';
  const [bridgeDiagnostics, setBridgeDiagnostics] = useState<ApiDiagnostic[]>([]);
  const [isEditStarting, setIsEditStarting] = useState(false);
  const [isItemsLoading, setIsItemsLoading] = useState(false);
  const [isItemUpdating, setIsItemUpdating] = useState(false);
  const [isPokemonLoading, setIsPokemonLoading] = useState(false);
  const [isPokemonUpdating, setIsPokemonUpdating] = useState(false);
  const [isMovesLoading, setIsMovesLoading] = useState(false);
  const [isMoveUpdating, setIsMoveUpdating] = useState(false);
  const [isTextLoading, setIsTextLoading] = useState(false);
  const [isTextUpdating, setIsTextUpdating] = useState(false);
  const [isTrainersLoading, setIsTrainersLoading] = useState(false);
  const [isTrainerUpdating, setIsTrainerUpdating] = useState(false);
  const [isGiftPokemonLoading, setIsGiftPokemonLoading] = useState(false);
  const [isGiftPokemonUpdating, setIsGiftPokemonUpdating] = useState(false);
  const [isTradePokemonLoading, setIsTradePokemonLoading] = useState(false);
  const [isTradePokemonUpdating, setIsTradePokemonUpdating] = useState(false);
  const [isStaticEncountersLoading, setIsStaticEncountersLoading] = useState(false);
  const [isStaticEncounterUpdating, setIsStaticEncounterUpdating] = useState(false);
  const [isRentalPokemonLoading, setIsRentalPokemonLoading] = useState(false);
  const [isRentalPokemonUpdating, setIsRentalPokemonUpdating] = useState(false);
  const [isDynamaxAdventuresLoading, setIsDynamaxAdventuresLoading] = useState(false);
  const [isDynamaxAdventureUpdating, setIsDynamaxAdventureUpdating] = useState(false);
  const [isShopsLoading, setIsShopsLoading] = useState(false);
  const [isShopUpdating, setIsShopUpdating] = useState(false);
  const [isEncountersLoading, setIsEncountersLoading] = useState(false);
  const [isEncounterUpdating, setIsEncounterUpdating] = useState(false);
  const [isRaidBattlesLoading, setIsRaidBattlesLoading] = useState(false);
  const [isRaidBattleUpdating, setIsRaidBattleUpdating] = useState(false);
  const [isRaidRewardsLoading, setIsRaidRewardsLoading] = useState(false);
  const [isRaidRewardUpdating, setIsRaidRewardUpdating] = useState(false);
  const [isPlacementLoading, setIsPlacementLoading] = useState(false);
  const [isPlacementUpdating, setIsPlacementUpdating] = useState(false);
  const [isFlagworkSaveLoading, setIsFlagworkSaveLoading] = useState(false);
  const [isExeFsPatchLoading, setIsExeFsPatchLoading] = useState(false);
  const [isExeFsPatchStaging, setIsExeFsPatchStaging] = useState(false);
  const [isRoyalCandyLoading, setIsRoyalCandyLoading] = useState(false);
  const [isRoyalCandyStaging, setIsRoyalCandyStaging] = useState(false);
  const [isSpreadsheetImportLoading, setIsSpreadsheetImportLoading] = useState(false);
  const [isSpreadsheetImportPreviewing, setIsSpreadsheetImportPreviewing] = useState(false);
  const [isChangePlanApplying, setIsChangePlanApplying] = useState(false);
  const [isChangePlanCreating, setIsChangePlanCreating] = useState(false);
  const [isSessionValidating, setIsSessionValidating] = useState(false);
  const [lazyLoadedWorkflowSections, setLazyLoadedWorkflowSections] = useState<
    Set<WorkbenchSection>
  >(() => new Set());
  const [validatedEditSessionSignature, setValidatedEditSessionSignature] = useState<
    string | null
  >(null);
  const [changePlanSessionSignature, setChangePlanSessionSignature] = useState<string | null>(
    null
  );
  const [appliedChangePlan, setAppliedChangePlan] = useState<ChangePlan | null>(null);
  const [saveProgress, setSaveProgress] = useState<SaveProgressState | null>(null);
  const [exitPrompt, setExitPrompt] = useState<ExitPromptState | null>(null);
  const [expandedWorkflowGroups, setExpandedWorkflowGroups] = useState<
    Set<WorkflowNavigationGroup['id']>
  >(() => new Set(['editors']));
  const editSessionRef = useRef<EditSession | null>(editSession);
  const exitPromptRef = useRef<ExitPromptState | null>(exitPrompt);
  const pendingEditCount = editSession?.pendingEdits.length ?? 0;
  const currentEditSessionSignature = useMemo(
    () => getEditSessionSignature(editSession),
    [editSession]
  );
  const isEditSessionValidated =
    currentEditSessionSignature !== null &&
    currentEditSessionSignature === validatedEditSessionSignature;
  const isChangePlanCurrent =
    changePlan !== null &&
    currentEditSessionSignature !== null &&
    currentEditSessionSignature === changePlanSessionSignature;
  const visibleChangePlan = isChangePlanCurrent ? changePlan : appliedChangePlan;
  const canSaveValidatedChanges =
    pendingEditCount > 0 &&
    isEditSessionValidated &&
    visibleChangePlan !== null &&
    visibleChangePlan.canApply &&
    visibleChangePlan.writes.length > 0 &&
    !isChangePlanApplying &&
    !isChangePlanCreating &&
    !isSessionValidating;
  const activeSectionIsEditor = groupedWorkflowSectionIds.has(activeSection);

  const clearPendingEditState = useCallback(() => {
    editSessionRef.current = null;
    setEditSession(null);
    setChangePlan(null);
    setApplyResult(null);
    setEditValidationDiagnostics([]);
    setValidatedEditSessionSignature(null);
    setChangePlanSessionSignature(null);
    setAppliedChangePlan(null);
  }, [setApplyResult, setChangePlan, setEditSession, setEditValidationDiagnostics]);

  const requestEditorExit = useCallback(
    (destination: WorkbenchSection | null, kind: ExitPromptState['kind']) => {
      if (editSession) {
        setExitPrompt({ destination, kind, mode: 'confirm' });
        return;
      }

      if (kind === 'editor' && destination) {
        setActiveSection(destination);
      }
    },
    [editSession, setActiveSection]
  );

  const handleCloseActiveEditor = useCallback(() => {
    requestEditorExit('workflows', 'editor');
  }, [requestEditorExit]);

  const handleConfirmExitDiscard = useCallback(async () => {
    const prompt = exitPrompt;
    if (!prompt) {
      return;
    }

    clearPendingEditState();
    setExitPrompt(null);

    if (prompt.kind === 'editor' && prompt.destination) {
      setActiveSection(prompt.destination);
      return;
    }

    if (prompt.kind === 'window' && desktopServices.isAvailable) {
      editSessionRef.current = null;
      try {
        await desktopServices.setCloseGuardEnabled(false);
        await desktopServices.exitApp();
      } catch (error) {
        setBridgeDiagnostics(
          toDesktopDiagnostics(error, 'Could not close KM Editor after discarding pending changes.')
        );
      }
    }
  }, [
    clearPendingEditState,
    desktopServices.isAvailable,
    desktopServices.exitApp,
    desktopServices.setCloseGuardEnabled,
    exitPrompt,
    setActiveSection,
    setBridgeDiagnostics
  ]);

  const handleDeclineExitDiscard = useCallback(() => {
    setExitPrompt((prompt) => (prompt ? { ...prompt, mode: 'redirect' } : prompt));
  }, []);

  const handleStayAfterExitDecline = useCallback(() => {
    setExitPrompt(null);
  }, []);

  const handleGoToChangesAfterExitDecline = useCallback(() => {
    setActiveSection('changes');
    setExitPrompt(null);
  }, [setActiveSection]);

  useEffect(() => {
    editSessionRef.current = editSession;
  }, [editSession]);

  useEffect(() => {
    exitPromptRef.current = exitPrompt;
  }, [exitPrompt]);

  useEffect(() => {
    if (!desktopServices.isAvailable) {
      return;
    }

    void desktopServices.setCloseGuardEnabled(editSession !== null).catch((error) => {
      setBridgeDiagnostics(
        toDesktopDiagnostics(error, 'Could not update the desktop close guard.')
      );
    });
  }, [
    desktopServices.isAvailable,
    desktopServices.setCloseGuardEnabled,
    editSession,
    setBridgeDiagnostics
  ]);

  const handleToggleWorkflowGroup = useCallback((groupId: WorkflowNavigationGroup['id']) => {
    setExpandedWorkflowGroups((currentGroups) => {
      const nextGroups = new Set(currentGroups);
      if (nextGroups.has(groupId)) {
        nextGroups.delete(groupId);
      } else {
        nextGroups.add(groupId);
      }

      return nextGroups;
    });
  }, []);

  const handleValidateProject = async () => {
    setProjectStatus('validating');
    setBridgeDiagnostics([]);

    try {
      const paths = toProjectPaths(draftPaths);
      const response = await bridge.validateProject({ paths });
      setProjectHealth(response.health);
      setLazyLoadedWorkflowSections(new Set());
      await refreshWorkflows(paths, response.health.canOpenReadOnlyWorkflows);
    } catch (error) {
      setProjectStatus('idle');
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    }
  };

  const handleOpenProject = async () => {
    setProjectStatus('opening');
    setBridgeDiagnostics([]);

    try {
      const paths = toProjectPaths(draftPaths);
      const response = await bridge.openProject({ paths });
      setOpenProject({
        fileGraph: response.fileGraph,
        health: response.health,
        projectId: response.projectId
      });
      setActiveSection('health');
      setLazyLoadedWorkflowSections(new Set());
      await refreshWorkflows(paths, response.health.canOpenReadOnlyWorkflows);
    } catch (error) {
      setProjectStatus('idle');
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    }
  };

  useEffect(() => {
    if (!desktopServices.isAvailable) {
      return undefined;
    }

    let isDisposed = false;
    let unlisten: (() => void) | null = null;

    void listen(windowCloseRequestedEvent, () => {
      if (editSessionRef.current === null) {
        void desktopServices
          .setCloseGuardEnabled(false)
          .then(() => desktopServices.exitApp())
          .catch((error) => {
            setBridgeDiagnostics(toDesktopDiagnostics(error, 'Could not close KM Editor.'));
          });
        return;
      }

      if (exitPromptRef.current?.kind !== 'window') {
        setExitPrompt({ destination: null, kind: 'window', mode: 'confirm' });
      }
    })
      .then((nextUnlisten) => {
        if (isDisposed) {
          nextUnlisten();
        } else {
          unlisten = nextUnlisten;
        }
      })
      .catch((error) => {
        setBridgeDiagnostics(
          toDesktopDiagnostics(error, 'Could not listen for desktop close requests.')
        );
      });

    return () => {
      isDisposed = true;
      unlisten?.();
    };
  }, [
    desktopServices.isAvailable,
    desktopServices.exitApp,
    desktopServices.setCloseGuardEnabled,
    setBridgeDiagnostics
  ]);

  const handlePickProjectPath = async (pathField: ProjectPathField) => {
    try {
      const pickPath =
        pathField.kind === 'file' ? desktopServices.pickFile : desktopServices.pickFolder;
      const selectedPath = await pickPath({
        defaultPath: draftPaths[pathField.field] || undefined,
        title: `Select ${pathField.label}`
      });

      if (selectedPath) {
        setDraftPath(pathField.field, selectedPath);
      }
    } catch (error) {
      setBridgeDiagnostics(toDesktopDiagnostics(error, `Could not choose ${pathField.label}.`));
    }
  };

  const handleOpenOutputRoot = async () => {
    const outputRootPath = draftPaths.outputRootPath.trim();

    if (!outputRootPath) {
      setBridgeDiagnostics([
        {
          domain: 'desktop',
          message: 'Output root is not configured.',
          severity: 'warning'
        }
      ]);
      return;
    }

    try {
      await desktopServices.openPath(outputRootPath);
    } catch (error) {
      setBridgeDiagnostics(toDesktopDiagnostics(error, 'Could not open output root.'));
    }
  };

  const handleOpenItemsWorkflow = async () => {
    setIsItemsLoading(true);
    setBridgeDiagnostics([]);

    try {
      const response = await bridge.loadItemsWorkflow({ paths: toProjectPaths(draftPaths) });
      setItemsWorkflow(response.workflow);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsItemsLoading(false);
    }
  };

  const handleOpenPokemonWorkflow = async () => {
    setIsPokemonLoading(true);
    setBridgeDiagnostics([]);

    try {
      const response = await bridge.loadPokemonWorkflow({ paths: toProjectPaths(draftPaths) });
      setPokemonWorkflow(response.workflow);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsPokemonLoading(false);
    }
  };

  const handleOpenMovesWorkflow = async () => {
    setIsMovesLoading(true);
    setBridgeDiagnostics([]);

    try {
      const response = await bridge.loadMovesWorkflow({ paths: toProjectPaths(draftPaths) });
      setMovesWorkflow(response.workflow);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsMovesLoading(false);
    }
  };

  const handleOpenTextWorkflow = async () => {
    setIsTextLoading(true);
    setBridgeDiagnostics([]);

    try {
      const response = await bridge.loadTextWorkflow({ paths: toProjectPaths(draftPaths) });
      setTextWorkflow(response.workflow);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsTextLoading(false);
    }
  };

  const handleOpenTrainersWorkflow = async () => {
    setIsTrainersLoading(true);
    setBridgeDiagnostics([]);

    try {
      const response = await bridge.loadTrainersWorkflow({ paths: toProjectPaths(draftPaths) });
      setTrainersWorkflow(response.workflow);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsTrainersLoading(false);
    }
  };

  const handleOpenGiftPokemonWorkflow = async () => {
    setIsGiftPokemonLoading(true);
    setBridgeDiagnostics([]);

    try {
      const response = await bridge.loadGiftPokemonWorkflow({ paths: toProjectPaths(draftPaths) });
      setGiftPokemonWorkflow(response.workflow);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsGiftPokemonLoading(false);
    }
  };

  const handleOpenTradePokemonWorkflow = async () => {
    setIsTradePokemonLoading(true);
    setBridgeDiagnostics([]);

    try {
      const response = await bridge.loadTradePokemonWorkflow({ paths: toProjectPaths(draftPaths) });
      setTradePokemonWorkflow(response.workflow);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsTradePokemonLoading(false);
    }
  };

  const handleOpenStaticEncountersWorkflow = async () => {
    setIsStaticEncountersLoading(true);
    setBridgeDiagnostics([]);

    try {
      const response = await bridge.loadStaticEncountersWorkflow({
        paths: toProjectPaths(draftPaths)
      });
      setStaticEncountersWorkflow(response.workflow);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsStaticEncountersLoading(false);
    }
  };

  const handleOpenRentalPokemonWorkflow = async () => {
    setIsRentalPokemonLoading(true);
    setBridgeDiagnostics([]);

    try {
      const response = await bridge.loadRentalPokemonWorkflow({
        paths: toProjectPaths(draftPaths)
      });
      setRentalPokemonWorkflow(response.workflow);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsRentalPokemonLoading(false);
    }
  };

  const handleOpenDynamaxAdventuresWorkflow = async () => {
    setIsDynamaxAdventuresLoading(true);
    setBridgeDiagnostics([]);

    try {
      const response = await bridge.loadDynamaxAdventuresWorkflow({
        paths: toProjectPaths(draftPaths)
      });
      setDynamaxAdventuresWorkflow(response.workflow);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsDynamaxAdventuresLoading(false);
    }
  };

  const handleOpenShopsWorkflow = async () => {
    setIsShopsLoading(true);
    setBridgeDiagnostics([]);

    try {
      const response = await bridge.loadShopsWorkflow({ paths: toProjectPaths(draftPaths) });
      setShopsWorkflow(response.workflow);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsShopsLoading(false);
    }
  };

  const handleOpenEncountersWorkflow = async () => {
    setIsEncountersLoading(true);
    setBridgeDiagnostics([]);

    try {
      const response = await bridge.loadEncountersWorkflow({ paths: toProjectPaths(draftPaths) });
      setEncountersWorkflow(response.workflow);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsEncountersLoading(false);
    }
  };

  const handleOpenRaidBattlesWorkflow = async () => {
    setIsRaidBattlesLoading(true);
    setBridgeDiagnostics([]);

    try {
      const response = await bridge.loadRaidBattlesWorkflow({ paths: toProjectPaths(draftPaths) });
      setRaidBattlesWorkflow(response.workflow);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsRaidBattlesLoading(false);
    }
  };

  const handleOpenRaidRewardsWorkflow = async () => {
    setIsRaidRewardsLoading(true);
    setBridgeDiagnostics([]);

    try {
      const response = await bridge.loadRaidRewardsWorkflow({ paths: toProjectPaths(draftPaths) });
      setRaidRewardsWorkflow(response.workflow);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsRaidRewardsLoading(false);
    }
  };

  const handleOpenPlacementWorkflow = async () => {
    setIsPlacementLoading(true);
    setBridgeDiagnostics([]);

    try {
      const response = await bridge.loadPlacementWorkflow({ paths: toProjectPaths(draftPaths) });
      setPlacementWorkflow(response.workflow);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsPlacementLoading(false);
    }
  };

  const handleOpenFlagworkSaveWorkflow = async () => {
    setIsFlagworkSaveLoading(true);
    setBridgeDiagnostics([]);

    try {
      const response = await bridge.loadFlagworkSaveWorkflow({
        paths: toProjectPaths(draftPaths)
      });
      setFlagworkSaveWorkflow(response.workflow);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsFlagworkSaveLoading(false);
    }
  };

  const handleOpenExeFsPatchWorkflow = async () => {
    setIsExeFsPatchLoading(true);
    setBridgeDiagnostics([]);

    try {
      const response = await bridge.loadExeFsPatchWorkflow({
        paths: toProjectPaths(draftPaths)
      });
      setExeFsPatchWorkflow(response.workflow);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsExeFsPatchLoading(false);
    }
  };

  const handleStageExeFsPatch = async (patchId: string) => {
    setIsExeFsPatchStaging(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);
    setChangePlan(null);
    setApplyResult(null);

    try {
      const response = await bridge.stageExeFsPatch({
        patchId,
        paths: toProjectPaths(draftPaths),
        session: editSession
      });
      setExeFsPatchWorkflow(response.workflow);
      setEditSession(response.session);
      setEditValidationDiagnostics(response.diagnostics);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsExeFsPatchStaging(false);
    }
  };

  const handleOpenRoyalCandyWorkflow = async () => {
    setIsRoyalCandyLoading(true);
    setBridgeDiagnostics([]);

    try {
      const response = await bridge.loadRoyalCandyWorkflow({
        paths: toProjectPaths(draftPaths)
      });
      setRoyalCandyWorkflow(response.workflow);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsRoyalCandyLoading(false);
    }
  };

  const handleStageRoyalCandyWorkflow = async (workflowId: string) => {
    setIsRoyalCandyStaging(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);
    setChangePlan(null);
    setApplyResult(null);

    try {
      const response = await bridge.stageRoyalCandyWorkflow({
        paths: toProjectPaths(draftPaths),
        session: editSession,
        workflowId
      });
      setRoyalCandyWorkflow(response.workflow);
      setEditSession(response.session);
      setEditValidationDiagnostics(response.diagnostics);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsRoyalCandyStaging(false);
    }
  };

  const handleOpenSpreadsheetImportWorkflow = async () => {
    setIsSpreadsheetImportLoading(true);
    setBridgeDiagnostics([]);

    try {
      const response = await bridge.loadSpreadsheetImportWorkflow({
        paths: toProjectPaths(draftPaths)
      });
      setSpreadsheetImportWorkflow(response.workflow);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsSpreadsheetImportLoading(false);
    }
  };

  useEffect(() => {
    if (!health?.canOpenReadOnlyWorkflows || lazyLoadedWorkflowSections.has(activeSection)) {
      return;
    }

    const workflowSummary = workflows.find((workflow) => workflow.id === activeSection);
    if (workflowSummary?.availability === 'disabled') {
      return;
    }

    const markLazyLoadStarted = () =>
      setLazyLoadedWorkflowSections((currentSections) => {
        const nextSections = new Set(currentSections);
        nextSections.add(activeSection);
        return nextSections;
      });

    switch (activeSection) {
      case 'items':
        if (!itemsWorkflow && !isItemsLoading) {
          markLazyLoadStarted();
          void handleOpenItemsWorkflow();
        }
        break;
      case 'pokemon':
        if (!pokemonWorkflow && !isPokemonLoading) {
          markLazyLoadStarted();
          void handleOpenPokemonWorkflow();
        }
        break;
      case 'moves':
        if (!movesWorkflow && !isMovesLoading) {
          markLazyLoadStarted();
          void handleOpenMovesWorkflow();
        }
        break;
      case 'text':
        if (!textWorkflow && !isTextLoading) {
          markLazyLoadStarted();
          void handleOpenTextWorkflow();
        }
        break;
      case 'trainers':
        if (!trainersWorkflow && !isTrainersLoading) {
          markLazyLoadStarted();
          void handleOpenTrainersWorkflow();
        }
        break;
      case 'giftPokemon':
        if (!giftPokemonWorkflow && !isGiftPokemonLoading) {
          markLazyLoadStarted();
          void handleOpenGiftPokemonWorkflow();
        }
        break;
      case 'tradePokemon':
        if (!tradePokemonWorkflow && !isTradePokemonLoading) {
          markLazyLoadStarted();
          void handleOpenTradePokemonWorkflow();
        }
        break;
      case 'staticEncounters':
        if (!staticEncountersWorkflow && !isStaticEncountersLoading) {
          markLazyLoadStarted();
          void handleOpenStaticEncountersWorkflow();
        }
        break;
      case 'rentalPokemon':
        if (!rentalPokemonWorkflow && !isRentalPokemonLoading) {
          markLazyLoadStarted();
          void handleOpenRentalPokemonWorkflow();
        }
        break;
      case 'dynamaxAdventures':
        if (!dynamaxAdventuresWorkflow && !isDynamaxAdventuresLoading) {
          markLazyLoadStarted();
          void handleOpenDynamaxAdventuresWorkflow();
        }
        break;
      case 'shops':
        if (!shopsWorkflow && !isShopsLoading) {
          markLazyLoadStarted();
          void handleOpenShopsWorkflow();
        }
        break;
      case 'encounters':
        if (!encountersWorkflow && !isEncountersLoading) {
          markLazyLoadStarted();
          void handleOpenEncountersWorkflow();
        }
        break;
      case 'raidBattles':
        if (!raidBattlesWorkflow && !isRaidBattlesLoading) {
          markLazyLoadStarted();
          void handleOpenRaidBattlesWorkflow();
        }
        break;
      case 'raidRewards':
        if (!raidRewardsWorkflow && !isRaidRewardsLoading) {
          markLazyLoadStarted();
          void handleOpenRaidRewardsWorkflow();
        }
        break;
      case 'placement':
        if (!placementWorkflow && !isPlacementLoading) {
          markLazyLoadStarted();
          void handleOpenPlacementWorkflow();
        }
        break;
      case 'flagworkSave':
        if (!flagworkSaveWorkflow && !isFlagworkSaveLoading) {
          markLazyLoadStarted();
          void handleOpenFlagworkSaveWorkflow();
        }
        break;
      case 'exefsPatches':
        if (!exeFsPatchWorkflow && !isExeFsPatchLoading) {
          markLazyLoadStarted();
          void handleOpenExeFsPatchWorkflow();
        }
        break;
      case 'royalCandy':
        if (!royalCandyWorkflow && !isRoyalCandyLoading) {
          markLazyLoadStarted();
          void handleOpenRoyalCandyWorkflow();
        }
        break;
      case 'spreadsheetImport':
        if (!spreadsheetImportWorkflow && !isSpreadsheetImportLoading) {
          markLazyLoadStarted();
          void handleOpenSpreadsheetImportWorkflow();
        }
        break;
      default:
        break;
    }
  }, [
    activeSection,
    dynamaxAdventuresWorkflow,
    encountersWorkflow,
    exeFsPatchWorkflow,
    flagworkSaveWorkflow,
    giftPokemonWorkflow,
    health?.canOpenReadOnlyWorkflows,
    isEncountersLoading,
    isExeFsPatchLoading,
    isFlagworkSaveLoading,
    isGiftPokemonLoading,
    isRentalPokemonLoading,
    isDynamaxAdventuresLoading,
    isTradePokemonLoading,
    isStaticEncountersLoading,
    isItemsLoading,
    isMovesLoading,
    isPlacementLoading,
    isPokemonLoading,
    isRaidBattlesLoading,
    isRaidRewardsLoading,
    isRoyalCandyLoading,
    isShopsLoading,
    isSpreadsheetImportLoading,
    isTextLoading,
    isTrainersLoading,
    itemsWorkflow,
    lazyLoadedWorkflowSections,
    movesWorkflow,
    placementWorkflow,
    pokemonWorkflow,
    raidBattlesWorkflow,
    raidRewardsWorkflow,
    rentalPokemonWorkflow,
    royalCandyWorkflow,
    shopsWorkflow,
    spreadsheetImportWorkflow,
    staticEncountersWorkflow,
    textWorkflow,
    tradePokemonWorkflow,
    trainersWorkflow,
    workflows
  ]);

  const handlePreviewSpreadsheetImport = async (profileId: string, sourcePath: string) => {
    setIsSpreadsheetImportPreviewing(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);

    try {
      const response = await bridge.previewSpreadsheetImport({
        paths: toProjectPaths(draftPaths),
        profileId,
        session: editSession,
        sourcePath
      });
      setSpreadsheetImportWorkflow(response.workflow);
      setSpreadsheetImportPreview(response.preview);
      setEditSession(response.session);
      setEditValidationDiagnostics(response.diagnostics);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsSpreadsheetImportPreviewing(false);
    }
  };

  const handleStartEditSession = async () => {
    setIsEditStarting(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);
    setChangePlan(null);
    setApplyResult(null);
    setValidatedEditSessionSignature(null);
    setChangePlanSessionSignature(null);
    setAppliedChangePlan(null);

    try {
      const response = await bridge.startEditSession({ paths: toProjectPaths(draftPaths) });
      setEditSession(response.session);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsEditStarting(false);
    }
  };

  const handleCancelEditSession = () => {
    setBridgeDiagnostics([]);
    clearPendingEditState();
  };

  const handleUpdateItemField = async (itemId: number, field: string, value: string) => {
    setIsItemUpdating(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);

    try {
      const response = await bridge.updateItemField({
        field,
        itemId,
        paths: toProjectPaths(draftPaths),
        session: editSession,
        value
      });
      setItemsWorkflow(response.workflow);
      setEditSession(response.session);
      setEditValidationDiagnostics(response.diagnostics);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsItemUpdating(false);
    }
  };

  const handleUpdateItemFields = async (
    itemId: number,
    changes: Array<{ field: string; value: string }>
  ) => {
    if (changes.length === 0) {
      return;
    }

    setIsItemUpdating(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);

    try {
      let nextSession = editSession;
      let nextWorkflow = itemsWorkflow;
      let nextDiagnostics: ApiDiagnostic[] = [];

      for (const change of changes) {
        const response = await bridge.updateItemField({
          field: change.field,
          itemId,
          paths: toProjectPaths(draftPaths),
          session: nextSession,
          value: change.value
        });
        nextSession = response.session;
        nextWorkflow = response.workflow;
        nextDiagnostics = response.diagnostics;
      }

      if (nextWorkflow) {
        setItemsWorkflow(nextWorkflow);
      }
      setEditSession(nextSession);
      setEditValidationDiagnostics(nextDiagnostics);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsItemUpdating(false);
    }
  };

  const handleUpdatePokemonField = async (personalId: number, field: string, value: string) => {
    setIsPokemonUpdating(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);

    try {
      const response = await bridge.updatePokemonField({
        field,
        paths: toProjectPaths(draftPaths),
        personalId,
        session: editSession,
        value
      });
      setPokemonWorkflow(response.workflow);
      setEditSession(response.session);
      setEditValidationDiagnostics(response.diagnostics);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsPokemonUpdating(false);
    }
  };

  const handleUpdatePokemonFields = async (
    personalId: number,
    changes: Array<{ field: string; value: string }>
  ) => {
    if (changes.length === 0) {
      return;
    }

    setIsPokemonUpdating(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);

    try {
      let nextSession = editSession;
      let nextWorkflow = pokemonWorkflow;
      let nextDiagnostics: ApiDiagnostic[] = [];

      for (const change of changes) {
        const response = await bridge.updatePokemonField({
          field: change.field,
          paths: toProjectPaths(draftPaths),
          personalId,
          session: nextSession,
          value: change.value
        });
        nextSession = response.session;
        nextWorkflow = response.workflow;
        nextDiagnostics = response.diagnostics;
      }

      if (nextWorkflow) {
        setPokemonWorkflow(nextWorkflow);
      }
      setEditSession(nextSession);
      setEditValidationDiagnostics(nextDiagnostics);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsPokemonUpdating(false);
    }
  };

  const handleUpdatePokemonLearnset = async (
    personalId: number,
    action: string,
    slot: number | null,
    moveId: number | null,
    level: number | null
  ) => {
    setIsPokemonUpdating(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);

    try {
      const response = await bridge.updatePokemonLearnset({
        action,
        level,
        moveId,
        paths: toProjectPaths(draftPaths),
        personalId,
        session: editSession,
        slot
      });
      setPokemonWorkflow(response.workflow);
      setEditSession(response.session);
      setEditValidationDiagnostics(response.diagnostics);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsPokemonUpdating(false);
    }
  };

  const handleUpdatePokemonEvolution = async (
    personalId: number,
    action: string,
    slot: number | null,
    method: number | null,
    argument: number | null,
    species: number | null,
    form: number | null,
    level: number | null
  ) => {
    setIsPokemonUpdating(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);

    try {
      const response = await bridge.updatePokemonEvolution({
        action,
        argument,
        form,
        level,
        method,
        paths: toProjectPaths(draftPaths),
        personalId,
        session: editSession,
        slot,
        species
      });
      setPokemonWorkflow(response.workflow);
      setEditSession(response.session);
      setEditValidationDiagnostics(response.diagnostics);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsPokemonUpdating(false);
    }
  };

  const handleUpdateMoveField = async (moveId: number, field: string, value: string) => {
    setIsMoveUpdating(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);

    try {
      const response = await bridge.updateMoveField({
        field,
        moveId,
        paths: toProjectPaths(draftPaths),
        session: editSession,
        value
      });
      setMovesWorkflow(response.workflow);
      setEditSession(response.session);
      setEditValidationDiagnostics(response.diagnostics);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsMoveUpdating(false);
    }
  };

  const handleUpdateMoveFields = async (
    moveId: number,
    changes: Array<{ field: string; value: string }>
  ) => {
    if (changes.length === 0) {
      return;
    }

    setIsMoveUpdating(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);

    try {
      let nextSession = editSession;
      let nextWorkflow = movesWorkflow;
      let nextDiagnostics: ApiDiagnostic[] = [];

      for (const change of changes) {
        const response = await bridge.updateMoveField({
          field: change.field,
          moveId,
          paths: toProjectPaths(draftPaths),
          session: nextSession,
          value: change.value
        });
        nextSession = response.session;
        nextWorkflow = response.workflow;
        nextDiagnostics = response.diagnostics;
      }

      if (nextWorkflow) {
        setMovesWorkflow(nextWorkflow);
      }
      setEditSession(nextSession);
      setEditValidationDiagnostics(nextDiagnostics);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsMoveUpdating(false);
    }
  };

  const handleUpdateTextEntry = async (textKey: string, value: string) => {
    setIsTextUpdating(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);

    try {
      const response = await bridge.updateTextEntry({
        paths: toProjectPaths(draftPaths),
        session: editSession,
        textKey,
        value
      });
      setTextWorkflow(response.workflow);
      setEditSession(response.session);
      setEditValidationDiagnostics(response.diagnostics);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsTextUpdating(false);
    }
  };

  const handleUpdateTrainerField = async (
    trainerId: number,
    slot: number | null,
    field: string,
    value: string
  ) => {
    setIsTrainerUpdating(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);

    try {
      const response = await bridge.updateTrainerField({
        field,
        paths: toProjectPaths(draftPaths),
        session: editSession,
        slot,
        trainerId,
        value
      });
      setTrainersWorkflow(response.workflow);
      setEditSession(response.session);
      setEditValidationDiagnostics(response.diagnostics);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsTrainerUpdating(false);
    }
  };

  const handleUpdateTrainerFields = async (
    trainerId: number,
    slot: number | null,
    changes: Array<{ field: string; value: string }>
  ) => {
    if (changes.length === 0) {
      return;
    }

    setIsTrainerUpdating(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);

    try {
      let nextSession = editSession;
      let nextWorkflow = trainersWorkflow;
      let nextDiagnostics: ApiDiagnostic[] = [];

      for (const change of changes) {
        const response = await bridge.updateTrainerField({
          field: change.field,
          paths: toProjectPaths(draftPaths),
          session: nextSession,
          slot,
          trainerId,
          value: change.value
        });
        nextSession = response.session;
        nextWorkflow = response.workflow;
        nextDiagnostics = response.diagnostics;
      }

      if (nextWorkflow) {
        setTrainersWorkflow(nextWorkflow);
      }
      setEditSession(nextSession);
      setEditValidationDiagnostics(nextDiagnostics);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsTrainerUpdating(false);
    }
  };

  const handleUpdateGiftPokemonField = async (
    giftIndex: number,
    field: string,
    value: string
  ) => {
    setIsGiftPokemonUpdating(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);

    try {
      const response = await bridge.updateGiftPokemonField({
        field,
        giftIndex,
        paths: toProjectPaths(draftPaths),
        session: editSession,
        value
      });
      setGiftPokemonWorkflow(response.workflow);
      setEditSession(response.session);
      setEditValidationDiagnostics(response.diagnostics);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsGiftPokemonUpdating(false);
    }
  };

  const handleUpdateGiftPokemonFields = async (
    giftIndex: number,
    changes: Array<{ field: string; value: string }>
  ) => {
    if (changes.length === 0) {
      return;
    }

    setIsGiftPokemonUpdating(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);

    try {
      let nextSession = editSession;
      let nextWorkflow = giftPokemonWorkflow;
      let nextDiagnostics: ApiDiagnostic[] = [];

      for (const change of changes) {
        const response = await bridge.updateGiftPokemonField({
          field: change.field,
          giftIndex,
          paths: toProjectPaths(draftPaths),
          session: nextSession,
          value: change.value
        });
        nextSession = response.session;
        nextWorkflow = response.workflow;
        nextDiagnostics = response.diagnostics;
      }

      if (nextWorkflow) {
        setGiftPokemonWorkflow(nextWorkflow);
      }
      setEditSession(nextSession);
      setEditValidationDiagnostics(nextDiagnostics);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsGiftPokemonUpdating(false);
    }
  };

  const handleUpdateTradePokemonField = async (
    tradeIndex: number,
    field: string,
    value: string
  ) => {
    setIsTradePokemonUpdating(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);

    try {
      const response = await bridge.updateTradePokemonField({
        field,
        paths: toProjectPaths(draftPaths),
        session: editSession,
        tradeIndex,
        value
      });
      setTradePokemonWorkflow(response.workflow);
      setEditSession(response.session);
      setEditValidationDiagnostics(response.diagnostics);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsTradePokemonUpdating(false);
    }
  };

  const handleUpdateTradePokemonFields = async (
    tradeIndex: number,
    changes: Array<{ field: string; value: string }>
  ) => {
    if (changes.length === 0) {
      return;
    }

    setIsTradePokemonUpdating(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);

    try {
      let nextSession = editSession;
      let nextWorkflow = tradePokemonWorkflow;
      let nextDiagnostics: ApiDiagnostic[] = [];

      for (const change of changes) {
        const response = await bridge.updateTradePokemonField({
          field: change.field,
          paths: toProjectPaths(draftPaths),
          session: nextSession,
          tradeIndex,
          value: change.value
        });
        nextSession = response.session;
        nextWorkflow = response.workflow;
        nextDiagnostics = response.diagnostics;
      }

      if (nextWorkflow) {
        setTradePokemonWorkflow(nextWorkflow);
      }
      setEditSession(nextSession);
      setEditValidationDiagnostics(nextDiagnostics);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsTradePokemonUpdating(false);
    }
  };

  const handleUpdateStaticEncounterField = async (
    encounterIndex: number,
    field: string,
    value: string
  ) => {
    setIsStaticEncounterUpdating(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);

    try {
      const response = await bridge.updateStaticEncounterField({
        encounterIndex,
        field,
        paths: toProjectPaths(draftPaths),
        session: editSession,
        value
      });
      setStaticEncountersWorkflow(response.workflow);
      setEditSession(response.session);
      setEditValidationDiagnostics(response.diagnostics);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsStaticEncounterUpdating(false);
    }
  };

  const handleUpdateStaticEncounterFields = async (
    encounterIndex: number,
    changes: Array<{ field: string; value: string }>
  ) => {
    if (changes.length === 0) {
      return;
    }

    setIsStaticEncounterUpdating(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);

    try {
      let nextSession = editSession;
      let nextWorkflow = staticEncountersWorkflow;
      let nextDiagnostics: ApiDiagnostic[] = [];

      for (const change of changes) {
        const response = await bridge.updateStaticEncounterField({
          encounterIndex,
          field: change.field,
          paths: toProjectPaths(draftPaths),
          session: nextSession,
          value: change.value
        });
        nextSession = response.session;
        nextWorkflow = response.workflow;
        nextDiagnostics = response.diagnostics;
      }

      if (nextWorkflow) {
        setStaticEncountersWorkflow(nextWorkflow);
      }
      setEditSession(nextSession);
      setEditValidationDiagnostics(nextDiagnostics);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsStaticEncounterUpdating(false);
    }
  };

  const handleUpdateRentalPokemonField = async (
    rentalIndex: number,
    field: string,
    value: string
  ) => {
    setIsRentalPokemonUpdating(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);

    try {
      const response = await bridge.updateRentalPokemonField({
        field,
        paths: toProjectPaths(draftPaths),
        rentalIndex,
        session: editSession,
        value
      });
      setRentalPokemonWorkflow(response.workflow);
      setEditSession(response.session);
      setEditValidationDiagnostics(response.diagnostics);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsRentalPokemonUpdating(false);
    }
  };

  const handleUpdateRentalPokemonFields = async (
    rentalIndex: number,
    changes: Array<{ field: string; value: string }>
  ) => {
    if (changes.length === 0) {
      return;
    }

    setIsRentalPokemonUpdating(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);

    try {
      let nextSession = editSession;
      let nextWorkflow = rentalPokemonWorkflow;
      let nextDiagnostics: ApiDiagnostic[] = [];

      for (const change of changes) {
        const response = await bridge.updateRentalPokemonField({
          field: change.field,
          paths: toProjectPaths(draftPaths),
          rentalIndex,
          session: nextSession,
          value: change.value
        });
        nextSession = response.session;
        nextWorkflow = response.workflow;
        nextDiagnostics = response.diagnostics;
      }

      if (nextWorkflow) {
        setRentalPokemonWorkflow(nextWorkflow);
      }
      setEditSession(nextSession);
      setEditValidationDiagnostics(nextDiagnostics);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsRentalPokemonUpdating(false);
    }
  };

  const handleUpdateDynamaxAdventureField = async (
    entryIndex: number,
    field: string,
    value: string
  ) => {
    setIsDynamaxAdventureUpdating(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);

    try {
      const response = await bridge.updateDynamaxAdventureField({
        entryIndex,
        field,
        paths: toProjectPaths(draftPaths),
        session: editSession,
        value
      });
      setDynamaxAdventuresWorkflow(response.workflow);
      setEditSession(response.session);
      setEditValidationDiagnostics(response.diagnostics);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsDynamaxAdventureUpdating(false);
    }
  };

  const handleUpdateDynamaxAdventureFields = async (
    entryIndex: number,
    changes: Array<{ field: string; value: string }>
  ) => {
    if (changes.length === 0) {
      return;
    }

    setIsDynamaxAdventureUpdating(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);

    try {
      let nextSession = editSession;
      let nextWorkflow = dynamaxAdventuresWorkflow;
      let nextDiagnostics: ApiDiagnostic[] = [];

      for (const change of changes) {
        const response = await bridge.updateDynamaxAdventureField({
          entryIndex,
          field: change.field,
          paths: toProjectPaths(draftPaths),
          session: nextSession,
          value: change.value
        });
        nextSession = response.session;
        nextWorkflow = response.workflow;
        nextDiagnostics = response.diagnostics;
      }

      if (nextWorkflow) {
        setDynamaxAdventuresWorkflow(nextWorkflow);
      }
      setEditSession(nextSession);
      setEditValidationDiagnostics(nextDiagnostics);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsDynamaxAdventureUpdating(false);
    }
  };

  const handleUpdateShopInventoryItem = async (
    shopId: string,
    slot: number,
    field: string,
    value: string
  ) => {
    setIsShopUpdating(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);

    try {
      const response = await bridge.updateShopInventoryItem({
        field,
        paths: toProjectPaths(draftPaths),
        session: editSession,
        shopId,
        slot,
        value
      });
      setShopsWorkflow(response.workflow);
      setEditSession(response.session);
      setEditValidationDiagnostics(response.diagnostics);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsShopUpdating(false);
    }
  };

  const handleOpenShopItem = (itemId: number) => {
    setSelectedItemId(itemId);
    setItemSearchText('');
    setActiveSection('items');
  };

  const handleUpdateEncounterSlotField = async (
    tableId: string,
    slot: number,
    field: string,
    value: string
  ) => {
    setIsEncounterUpdating(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);

    try {
      const response = await bridge.updateEncounterSlotField({
        field,
        paths: toProjectPaths(draftPaths),
        session: editSession,
        slot,
        tableId,
        value
      });
      setEncountersWorkflow(response.workflow);
      setEditSession(response.session);
      setEditValidationDiagnostics(response.diagnostics);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsEncounterUpdating(false);
    }
  };

  const handleUpdateEncounterSlotFields = async (
    tableId: string,
    slot: number,
    changes: Array<{ field: string; value: string }>
  ) => {
    if (changes.length === 0) {
      return;
    }

    setIsEncounterUpdating(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);

    try {
      let nextSession = editSession;
      let nextWorkflow = encountersWorkflow;
      let nextDiagnostics: ApiDiagnostic[] = [];

      for (const change of changes) {
        const response = await bridge.updateEncounterSlotField({
          field: change.field,
          paths: toProjectPaths(draftPaths),
          session: nextSession,
          slot,
          tableId,
          value: change.value
        });
        nextWorkflow = response.workflow;
        nextSession = response.session;
        nextDiagnostics = response.diagnostics;
      }

      if (nextWorkflow) {
        setEncountersWorkflow(nextWorkflow);
      }
      setEditSession(nextSession);
      setEditValidationDiagnostics(nextDiagnostics);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsEncounterUpdating(false);
    }
  };

  const handleApplyEncounterNormalToAllWeather = async (
    sourceTableId: string,
    targetTableIds: string[]
  ) => {
    if (!encountersWorkflow || targetTableIds.length === 0) {
      return;
    }

    const sourceTable = encountersWorkflow.tables.find((table) => table.tableId === sourceTableId);
    if (!sourceTable) {
      return;
    }

    const sourceSlots = sourceTable.slots
      .filter((slot) => slot.slot >= 1 && slot.slot <= 7)
      .map((slot) => ({
        form: slot.form.toString(),
        probability: slot.weight.toString(),
        slot: slot.slot,
        speciesId: slot.speciesId.toString()
      }));

    if (sourceSlots.length === 0) {
      return;
    }

    setIsEncounterUpdating(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);

    try {
      let nextSession = editSession;
      let nextWorkflow = encountersWorkflow;
      let nextDiagnostics: ApiDiagnostic[] = [];

      for (const targetTableId of targetTableIds) {
        const targetTable = nextWorkflow?.tables.find((table) => table.tableId === targetTableId);
        if (!targetTable) {
          continue;
        }

        for (const sourceSlot of sourceSlots) {
          if (!targetTable.slots.some((slot) => slot.slot === sourceSlot.slot)) {
            continue;
          }

          const changes = [
            { field: encounterSpeciesFieldName, value: sourceSlot.speciesId },
            { field: encounterFormFieldName, value: sourceSlot.form },
            { field: encounterProbabilityFieldName, value: sourceSlot.probability }
          ];

          for (const change of changes) {
            const response = await bridge.updateEncounterSlotField({
              field: change.field,
              paths: toProjectPaths(draftPaths),
              session: nextSession,
              slot: sourceSlot.slot,
              tableId: targetTableId,
              value: change.value
            });
            nextWorkflow = response.workflow;
            nextSession = response.session;
            nextDiagnostics = response.diagnostics;
          }
        }
      }

      if (nextWorkflow) {
        setEncountersWorkflow(nextWorkflow);
      }
      setEditSession(nextSession);
      setEditValidationDiagnostics(nextDiagnostics);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsEncounterUpdating(false);
    }
  };

  const handleUpdateRaidRewardField = async (
    tableId: string,
    slot: number,
    field: string,
    value: string
  ) => {
    setIsRaidRewardUpdating(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);

    try {
      const response = await bridge.updateRaidRewardField({
        field,
        paths: toProjectPaths(draftPaths),
        session: editSession,
        slot,
        tableId,
        value
      });
      setRaidRewardsWorkflow(response.workflow);
      setEditSession(response.session);
      setEditValidationDiagnostics(response.diagnostics);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsRaidRewardUpdating(false);
    }
  };

  const handleUpdateRaidRewardFields = async (
    tableId: string,
    slot: number,
    changes: Array<{ field: string; value: string }>
  ) => {
    if (changes.length === 0) {
      return;
    }

    setIsRaidRewardUpdating(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);

    try {
      let nextSession = editSession;
      let nextWorkflow = raidRewardsWorkflow;
      let nextDiagnostics: ApiDiagnostic[] = [];

      for (const change of changes) {
        const response = await bridge.updateRaidRewardField({
          field: change.field,
          paths: toProjectPaths(draftPaths),
          session: nextSession,
          slot,
          tableId,
          value: change.value
        });
        nextWorkflow = response.workflow;
        nextSession = response.session;
        nextDiagnostics = response.diagnostics;
      }

      if (nextWorkflow) {
        setRaidRewardsWorkflow(nextWorkflow);
      }
      setEditSession(nextSession);
      setEditValidationDiagnostics(nextDiagnostics);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsRaidRewardUpdating(false);
    }
  };

  const handleUpdateRaidBattleSlotField = async (
    tableId: string,
    slot: number,
    field: string,
    value: string
  ) => {
    setIsRaidBattleUpdating(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);

    try {
      const response = await bridge.updateRaidBattleSlotField({
        field,
        paths: toProjectPaths(draftPaths),
        session: editSession,
        slot,
        tableId,
        value
      });
      setRaidBattlesWorkflow(response.workflow);
      setEditSession(response.session);
      setEditValidationDiagnostics(response.diagnostics);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsRaidBattleUpdating(false);
    }
  };

  const handleUpdateRaidBattleSlotFields = async (
    tableId: string,
    slot: number,
    changes: Array<{ field: string; value: string }>
  ) => {
    if (changes.length === 0) {
      return;
    }

    setIsRaidBattleUpdating(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);

    try {
      let nextSession = editSession;
      let nextWorkflow = raidBattlesWorkflow;
      let nextDiagnostics: ApiDiagnostic[] = [];

      for (const change of changes) {
        const response = await bridge.updateRaidBattleSlotField({
          field: change.field,
          paths: toProjectPaths(draftPaths),
          session: nextSession,
          slot,
          tableId,
          value: change.value
        });
        nextWorkflow = response.workflow;
        nextSession = response.session;
        nextDiagnostics = response.diagnostics;
      }

      if (nextWorkflow) {
        setRaidBattlesWorkflow(nextWorkflow);
      }
      setEditSession(nextSession);
      setEditValidationDiagnostics(nextDiagnostics);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsRaidBattleUpdating(false);
    }
  };

  const handleUpdatePlacementObjectField = async (
    objectId: string,
    field: string,
    value: string
  ) => {
    setIsPlacementUpdating(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);

    try {
      const response = await bridge.updatePlacementObjectField({
        field,
        objectId,
        paths: toProjectPaths(draftPaths),
        session: editSession,
        value
      });
      setPlacementWorkflow(response.workflow);
      setEditSession(response.session);
      setEditValidationDiagnostics(response.diagnostics);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsPlacementUpdating(false);
    }
  };

  const handleUpdatePlacementObjectFields = async (
    objectId: string,
    changes: Array<{ field: string; value: string }>
  ) => {
    if (changes.length === 0) {
      return;
    }

    setIsPlacementUpdating(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);

    try {
      let nextSession = editSession;
      let nextWorkflow = placementWorkflow;
      let nextDiagnostics: ApiDiagnostic[] = [];

      for (const change of changes) {
        const response = await bridge.updatePlacementObjectField({
          field: change.field,
          objectId,
          paths: toProjectPaths(draftPaths),
          session: nextSession,
          value: change.value
        });
        nextWorkflow = response.workflow;
        nextSession = response.session;
        nextDiagnostics = response.diagnostics;
      }

      if (nextWorkflow) {
        setPlacementWorkflow(nextWorkflow);
      }
      setEditSession(nextSession);
      setEditValidationDiagnostics(nextDiagnostics);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsPlacementUpdating(false);
    }
  };

  const handleValidateEditSession = async () => {
    if (!editSession) {
      return;
    }

    setIsSessionValidating(true);
    setIsChangePlanCreating(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);
    setChangePlan(null);
    setApplyResult(null);
    setValidatedEditSessionSignature(null);
    setChangePlanSessionSignature(null);
    setAppliedChangePlan(null);

    try {
      const response = await bridge.validateEditSession({
        paths: toProjectPaths(draftPaths),
        session: editSession
      });
      const nextSessionSignature = getEditSessionSignature(response.session);

      if (!response.isValid || nextSessionSignature === null) {
        setEditSession(response.session);
        setEditValidationDiagnostics(response.diagnostics);
        return;
      }

      const planResponse = await bridge.createChangePlan({
        paths: toProjectPaths(draftPaths),
        session: response.session
      });
      setEditSession(response.session);
      setEditValidationDiagnostics(response.diagnostics);
      setValidatedEditSessionSignature(nextSessionSignature);
      setChangePlan(planResponse.changePlan);
      setChangePlanSessionSignature(nextSessionSignature);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsSessionValidating(false);
      setIsChangePlanCreating(false);
    }
  };

  const handleCreateChangePlan = async () => {
    if (!editSession) {
      return;
    }

    setIsChangePlanCreating(true);
    setBridgeDiagnostics([]);
    setChangePlan(null);
    setApplyResult(null);
    setAppliedChangePlan(null);

    try {
      const response = await bridge.createChangePlan({
        paths: toProjectPaths(draftPaths),
        session: editSession
      });
      setChangePlan(response.changePlan);
      setChangePlanSessionSignature(getEditSessionSignature(editSession));
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsChangePlanCreating(false);
    }
  };

  const handleSaveValidatedChanges = async () => {
    if (!editSession || !visibleChangePlan || !canSaveValidatedChanges) {
      return;
    }

    const filesToWrite = visibleChangePlan.writes.map((write) => write.targetRelativePath);
    const totalSteps = Math.max(filesToWrite.length, 1);
    setIsChangePlanApplying(true);
    setBridgeDiagnostics([]);
    setApplyResult(null);

    try {
      setSaveProgress({
        detail: 'Preparing output plan',
        label: 'Preparing',
        percent: 8,
        step: 0,
        totalSteps
      });
      await delay(250);

      for (const [index, file] of filesToWrite.entries()) {
        setSaveProgress({
          detail: file,
          label: `Writing file ${index + 1} of ${totalSteps}`,
          percent: Math.round(15 + ((index + 1) / totalSteps) * 70),
          step: index + 1,
          totalSteps
        });
        await delay(325);
      }

      setSaveProgress({
        detail: 'Finalizing output files',
        label: 'Finalizing',
        percent: 92,
        step: totalSteps,
        totalSteps
      });

      const paths = toProjectPaths(draftPaths);
      const response = await bridge.applyChangePlan({
        changePlan: visibleChangePlan,
        paths,
        session: editSession
      });
      const hasApplyErrors = response.applyResult.diagnostics.some(
        (diagnostic) => diagnostic.severity === 'error'
      );

      if (!hasApplyErrors) {
        setAppliedChangePlan(visibleChangePlan);
        editSessionRef.current = null;
        setEditSession(null);
        setEditValidationDiagnostics([]);
        setValidatedEditSessionSignature(null);
        setChangePlanSessionSignature(null);
      }

      if (!hasApplyErrors && response.applyResult.writtenFiles.length > 0) {
        await refreshLoadedWorkflowsAfterApply(paths);
      }

      setApplyResult(response.applyResult);
      setSaveProgress({
        detail: hasApplyErrors ? 'Some files need attention' : 'Saved pending changes',
        label: hasApplyErrors ? 'Save needs attention' : 'Save complete',
        percent: 100,
        step: totalSteps,
        totalSteps
      });
      await delay(500);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsChangePlanApplying(false);
      setSaveProgress(null);
    }
  };

  const refreshLoadedWorkflowsAfterApply = async (paths: ReturnType<typeof toProjectPaths>) => {
    const fileGraphResponse = await bridge.refreshFileGraph({ paths });
    if (openProject) {
      setOpenProject({ ...openProject, fileGraph: fileGraphResponse.fileGraph });
    }

    await refreshWorkflows(paths, health?.canOpenReadOnlyWorkflows ?? true);

    const reloadTasks: Array<() => Promise<void>> = [];
    if (itemsWorkflow) {
      reloadTasks.push(
        async () => {
          const response = await bridge.loadItemsWorkflow({ paths });
          setItemsWorkflow(response.workflow);
        }
      );
    }
    if (pokemonWorkflow) {
      reloadTasks.push(
        async () => {
          const response = await bridge.loadPokemonWorkflow({ paths });
          setPokemonWorkflow(response.workflow);
        }
      );
    }
    if (movesWorkflow) {
      reloadTasks.push(
        async () => {
          const response = await bridge.loadMovesWorkflow({ paths });
          setMovesWorkflow(response.workflow);
        }
      );
    }
    if (textWorkflow) {
      reloadTasks.push(
        async () => {
          const response = await bridge.loadTextWorkflow({ paths });
          setTextWorkflow(response.workflow);
        }
      );
    }
    if (trainersWorkflow) {
      reloadTasks.push(
        async () => {
          const response = await bridge.loadTrainersWorkflow({ paths });
          setTrainersWorkflow(response.workflow);
        }
      );
    }
    if (giftPokemonWorkflow) {
      reloadTasks.push(
        async () => {
          const response = await bridge.loadGiftPokemonWorkflow({ paths });
          setGiftPokemonWorkflow(response.workflow);
        }
      );
    }
    if (tradePokemonWorkflow) {
      reloadTasks.push(
        async () => {
          const response = await bridge.loadTradePokemonWorkflow({ paths });
          setTradePokemonWorkflow(response.workflow);
        }
      );
    }
    if (staticEncountersWorkflow) {
      reloadTasks.push(
        async () => {
          const response = await bridge.loadStaticEncountersWorkflow({ paths });
          setStaticEncountersWorkflow(response.workflow);
        }
      );
    }
    if (rentalPokemonWorkflow) {
      reloadTasks.push(
        async () => {
          const response = await bridge.loadRentalPokemonWorkflow({ paths });
          setRentalPokemonWorkflow(response.workflow);
        }
      );
    }
    if (dynamaxAdventuresWorkflow) {
      reloadTasks.push(
        async () => {
          const response = await bridge.loadDynamaxAdventuresWorkflow({ paths });
          setDynamaxAdventuresWorkflow(response.workflow);
        }
      );
    }
    if (shopsWorkflow) {
      reloadTasks.push(
        async () => {
          const response = await bridge.loadShopsWorkflow({ paths });
          setShopsWorkflow(response.workflow);
        }
      );
    }
    if (encountersWorkflow) {
      reloadTasks.push(
        async () => {
          const response = await bridge.loadEncountersWorkflow({ paths });
          setEncountersWorkflow(response.workflow);
        }
      );
    }
    if (raidBattlesWorkflow) {
      reloadTasks.push(
        async () => {
          const response = await bridge.loadRaidBattlesWorkflow({ paths });
          setRaidBattlesWorkflow(response.workflow);
        }
      );
    }
    if (raidRewardsWorkflow) {
      reloadTasks.push(
        async () => {
          const response = await bridge.loadRaidRewardsWorkflow({ paths });
          setRaidRewardsWorkflow(response.workflow);
        }
      );
    }
    if (placementWorkflow) {
      reloadTasks.push(
        async () => {
          const response = await bridge.loadPlacementWorkflow({ paths });
          setPlacementWorkflow(response.workflow);
        }
      );
    }
    if (flagworkSaveWorkflow) {
      reloadTasks.push(
        async () => {
          const response = await bridge.loadFlagworkSaveWorkflow({ paths });
          setFlagworkSaveWorkflow(response.workflow);
        }
      );
    }
    if (exeFsPatchWorkflow) {
      reloadTasks.push(
        async () => {
          const response = await bridge.loadExeFsPatchWorkflow({ paths });
          setExeFsPatchWorkflow(response.workflow);
        }
      );
    }
    if (royalCandyWorkflow) {
      reloadTasks.push(
        async () => {
          const response = await bridge.loadRoyalCandyWorkflow({ paths });
          setRoyalCandyWorkflow(response.workflow);
        }
      );
    }
    if (spreadsheetImportWorkflow) {
      reloadTasks.push(
        async () => {
          const response = await bridge.loadSpreadsheetImportWorkflow({ paths });
          setSpreadsheetImportWorkflow(response.workflow);
        }
      );
    }

    for (const reloadTask of reloadTasks) {
      await reloadTask();
    }
  };

  const handleApplyChangePlan = async () => {
    if (!editSession || !changePlan) {
      return;
    }

    setIsChangePlanApplying(true);
    setBridgeDiagnostics([]);
    setApplyResult(null);

    try {
      const paths = toProjectPaths(draftPaths);
      const response = await bridge.applyChangePlan({
        changePlan,
        paths,
        session: editSession
      });
      const hasApplyErrors = response.applyResult.diagnostics.some(
        (diagnostic) => diagnostic.severity === 'error'
      );

      if (!hasApplyErrors) {
        setEditSession(null);
        setChangePlan(null);
        setValidatedEditSessionSignature(null);
        setChangePlanSessionSignature(null);
      }

      if (!hasApplyErrors && response.applyResult.writtenFiles.length > 0) {
        await refreshLoadedWorkflowsAfterApply(paths);
      }
      setApplyResult(response.applyResult);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsChangePlanApplying(false);
    }
  };

  const refreshWorkflows = async (
    paths: ReturnType<typeof toProjectPaths>,
    canOpenReadOnlyWorkflows: boolean
  ) => {
    if (!canOpenReadOnlyWorkflows) {
      setWorkflows([]);
      return;
    }

    const response = await bridge.listWorkflows({ paths });
    setWorkflows(response.workflows);
  };

  return (
    <main className="app-shell">
      <aside className="sidebar">
        <div className="brand">
          <img alt="" aria-hidden="true" className="brand-logo" src={kmLogoUrl} />
          <span>KM Editor</span>
        </div>

        <nav aria-label="Workspace" className="section-nav">
          {primaryNavigationSections.map((section) => {
            const Icon = section.icon;
            const isActive = activeSection === section.id;

            return (
              <button
                aria-current={isActive ? 'page' : undefined}
                aria-label={section.label}
                className="nav-button"
                key={section.id}
                onClick={() => setActiveSection(section.id)}
                type="button"
              >
                <Icon aria-hidden="true" size={18} />
                <span>{section.label}</span>
              </button>
            );
          })}

          {workflowNavigationGroups.map((group) => {
            const isExpanded = expandedWorkflowGroups.has(group.id);

            return (
              <div className="nav-workflow-group" key={group.id}>
                <button
                  aria-expanded={isExpanded}
                  className="nav-group-button"
                  onClick={() => handleToggleWorkflowGroup(group.id)}
                  type="button"
                >
                  <Layers aria-hidden="true" size={16} />
                  <span>{group.label}</span>
                </button>
                {isExpanded ? (
                  <div className="nav-group-items">
                    {group.sectionIds.map((sectionId) => {
                      const section = sections.find((candidate) => candidate.id === sectionId);
                      if (!section) {
                        return null;
                      }

                      const Icon = section.icon;
                      const isActive = activeSection === section.id;

                      return (
                        <button
                          aria-current={isActive ? 'page' : undefined}
                          aria-label={section.label}
                          className="nav-button nav-child-button"
                          key={section.id}
                          onClick={() => setActiveSection(section.id)}
                          type="button"
                        >
                          <Icon aria-hidden="true" size={16} />
                          <span>{section.label}</span>
                        </button>
                      );
                    })}
                  </div>
                ) : null}
              </div>
            );
          })}

          {utilityNavigationSections.map((section) => {
            const Icon = section.icon;
            const isActive = activeSection === section.id;

            return (
              <button
                aria-current={isActive ? 'page' : undefined}
                aria-label={section.label}
                className="nav-button"
                key={section.id}
                onClick={() => setActiveSection(section.id)}
                type="button"
              >
                <Icon aria-hidden="true" size={18} />
                <span>{section.label}</span>
                {pendingEditCount > 0 ? (
                  <span className="nav-count" aria-label={`${pendingEditCount} pending changes`}>
                    {pendingEditCount}
                  </span>
                ) : null}
              </button>
            );
          })}
        </nav>
      </aside>

      <section className="workspace">
        <header className="toolbar">
          <div className="title-block">
            <p className="project-state">{getProjectStateLabel(health, projectStatus)}</p>
            <h1>{activeSectionLabel}</h1>
          </div>

          <label className="search-box">
            <Search aria-hidden="true" size={18} />
            <input disabled placeholder="Search project" type="search" />
          </label>

          <button
            className="primary-button"
            disabled={isBusy}
            onClick={handleOpenProject}
            type="button"
          >
            <FolderOpen aria-hidden="true" size={18} />
            <span>{projectStatus === 'opening' ? 'Opening' : 'Open Project'}</span>
          </button>
          {activeSectionIsEditor ? (
            <button
              aria-label="Close Editor"
              className="secondary-button icon-button"
              onClick={handleCloseActiveEditor}
              title="Close editor"
              type="button"
            >
              <X aria-hidden="true" size={18} />
            </button>
          ) : null}
        </header>

        <div className="workspace-content">
          {activeSection === 'health' ? (
            <HealthSection
              draftPaths={draftPaths}
              health={health}
              isDesktopAvailable={desktopServices.isAvailable}
              bridgeDiagnostics={bridgeDiagnostics}
              isBusy={isBusy}
              onOpenProject={handleOpenProject}
              onOpenOutputRoot={handleOpenOutputRoot}
              onPickProjectPath={handlePickProjectPath}
              onSetDraftPath={setDraftPath}
              onValidateProject={handleValidateProject}
              pendingEditCount={pendingEditCount}
              projectStatus={projectStatus}
            />
          ) : null}
          {activeSection === 'workflows' ? (
            <WorkflowsSection
              health={health}
              isItemsLoading={isItemsLoading}
              isMovesLoading={isMovesLoading}
              isPokemonLoading={isPokemonLoading}
              isTextLoading={isTextLoading}
              isTrainersLoading={isTrainersLoading}
              isShopsLoading={isShopsLoading}
              isEncountersLoading={isEncountersLoading}
              isRaidBattlesLoading={isRaidBattlesLoading}
              isRaidRewardsLoading={isRaidRewardsLoading}
              isPlacementLoading={isPlacementLoading}
              isFlagworkSaveLoading={isFlagworkSaveLoading}
              isGiftPokemonLoading={isGiftPokemonLoading}
              isTradePokemonLoading={isTradePokemonLoading}
              isStaticEncountersLoading={isStaticEncountersLoading}
              isRentalPokemonLoading={isRentalPokemonLoading}
              isDynamaxAdventuresLoading={isDynamaxAdventuresLoading}
              isExeFsPatchLoading={isExeFsPatchLoading}
              isRoyalCandyLoading={isRoyalCandyLoading}
              isSpreadsheetImportLoading={isSpreadsheetImportLoading}
              onOpenEncountersWorkflow={handleOpenEncountersWorkflow}
              onOpenExeFsPatchWorkflow={handleOpenExeFsPatchWorkflow}
              onOpenFlagworkSaveWorkflow={handleOpenFlagworkSaveWorkflow}
              onOpenGiftPokemonWorkflow={handleOpenGiftPokemonWorkflow}
              onOpenTradePokemonWorkflow={handleOpenTradePokemonWorkflow}
              onOpenStaticEncountersWorkflow={handleOpenStaticEncountersWorkflow}
              onOpenRentalPokemonWorkflow={handleOpenRentalPokemonWorkflow}
              onOpenDynamaxAdventuresWorkflow={handleOpenDynamaxAdventuresWorkflow}
              onOpenItemsWorkflow={handleOpenItemsWorkflow}
              onOpenMovesWorkflow={handleOpenMovesWorkflow}
              onOpenPokemonWorkflow={handleOpenPokemonWorkflow}
              onOpenPlacementWorkflow={handleOpenPlacementWorkflow}
              onOpenRaidBattlesWorkflow={handleOpenRaidBattlesWorkflow}
              onOpenRaidRewardsWorkflow={handleOpenRaidRewardsWorkflow}
              onOpenRoyalCandyWorkflow={handleOpenRoyalCandyWorkflow}
              onOpenShopsWorkflow={handleOpenShopsWorkflow}
              onOpenSpreadsheetImportWorkflow={handleOpenSpreadsheetImportWorkflow}
              onOpenTextWorkflow={handleOpenTextWorkflow}
              onOpenTrainersWorkflow={handleOpenTrainersWorkflow}
              pendingEditCount={pendingEditCount}
              workflows={workflows}
            />
          ) : null}
          {activeSection === 'items' ? (
            isItemsLoading && !itemsWorkflow ? (
              <WorkflowLoadingPanel label="Items" />
            ) : (
              <ItemsSection
                onSearchChange={setItemSearchText}
                onSelectItem={setSelectedItemId}
                onStartEditSession={handleStartEditSession}
                onUpdateItemField={handleUpdateItemField}
                onUpdateItemFields={handleUpdateItemFields}
                searchText={itemSearchText}
                selectedItemId={selectedItemId}
                editSession={editSession}
                isEditStarting={isEditStarting}
                isItemUpdating={isItemUpdating}
                workflow={itemsWorkflow}
              />
            )
          ) : null}
          {activeSection === 'pokemon' ? (
            isPokemonLoading && !pokemonWorkflow ? (
              <WorkflowLoadingPanel label="Pokemon Data" />
            ) : (
              <PokemonSection
                editSession={editSession}
                isEditStarting={isEditStarting}
                isPokemonUpdating={isPokemonUpdating}
                onSearchChange={setPokemonSearchText}
                onSelectPokemon={setSelectedPokemonPersonalId}
                onStartEditSession={handleStartEditSession}
                onUpdatePokemonField={handleUpdatePokemonField}
                onUpdatePokemonFields={handleUpdatePokemonFields}
                onUpdatePokemonEvolution={handleUpdatePokemonEvolution}
                onUpdatePokemonLearnset={handleUpdatePokemonLearnset}
                searchText={pokemonSearchText}
                selectedPokemonPersonalId={selectedPokemonPersonalId}
                workflow={pokemonWorkflow}
              />
            )
          ) : null}
          {activeSection === 'moves' ? (
            isMovesLoading && !movesWorkflow ? (
              <WorkflowLoadingPanel label="Moves Data" />
            ) : (
              <MovesSection
                editSession={editSession}
                isEditStarting={isEditStarting}
                isMoveUpdating={isMoveUpdating}
                onSearchChange={setMovesSearchText}
                onSelectMove={setSelectedMoveId}
                onStartEditSession={handleStartEditSession}
                onUpdateMoveField={handleUpdateMoveField}
                onUpdateMoveFields={handleUpdateMoveFields}
                searchText={movesSearchText}
                selectedMoveId={selectedMoveId}
                workflow={movesWorkflow}
              />
            )
          ) : null}
          {activeSection === 'text' ? (
            isTextLoading && !textWorkflow ? (
              <WorkflowLoadingPanel label="Text and Dialogue Map" />
            ) : (
              <TextSection
                editSession={editSession}
                isEditStarting={isEditStarting}
                isTextUpdating={isTextUpdating}
                onSearchChange={setTextSearchText}
                onSelectTextEntry={setSelectedTextKey}
                onStartEditSession={handleStartEditSession}
                onUpdateTextEntry={handleUpdateTextEntry}
                searchText={textSearchText}
                selectedTextKey={selectedTextKey}
                workflow={textWorkflow}
              />
            )
          ) : null}
          {activeSection === 'trainers' ? (
            isTrainersLoading && !trainersWorkflow ? (
              <WorkflowLoadingPanel label="Trainers" />
            ) : (
              <TrainersSection
                editSession={editSession}
                isEditStarting={isEditStarting}
                isTrainerUpdating={isTrainerUpdating}
                onSearchChange={setTrainerSearchText}
                onSelectTrainer={setSelectedTrainerId}
                onStartEditSession={handleStartEditSession}
                onUpdateTrainerField={handleUpdateTrainerField}
                onUpdateTrainerFields={handleUpdateTrainerFields}
                searchText={trainerSearchText}
                selectedTrainerId={selectedTrainerId}
                workflow={trainersWorkflow}
              />
            )
          ) : null}
          {activeSection === 'giftPokemon' ? (
            isGiftPokemonLoading && !giftPokemonWorkflow ? (
              <WorkflowLoadingPanel label="Gift Pokemon" />
            ) : (
              <GiftPokemonSection
                editSession={editSession}
                isEditStarting={isEditStarting}
                isGiftPokemonUpdating={isGiftPokemonUpdating}
                onSearchChange={setGiftPokemonSearchText}
                onSelectGift={setSelectedGiftPokemonIndex}
                onStartEditSession={handleStartEditSession}
                onUpdateGiftPokemonField={handleUpdateGiftPokemonField}
                onUpdateGiftPokemonFields={handleUpdateGiftPokemonFields}
                searchText={giftPokemonSearchText}
                selectedGiftIndex={selectedGiftPokemonIndex}
                workflow={giftPokemonWorkflow}
              />
            )
          ) : null}
          {activeSection === 'tradePokemon' ? (
            isTradePokemonLoading && !tradePokemonWorkflow ? (
              <WorkflowLoadingPanel label="Trade Pokemon" />
            ) : (
              <TradePokemonSection
                editSession={editSession}
                isEditStarting={isEditStarting}
                isTradePokemonUpdating={isTradePokemonUpdating}
                onSearchChange={setTradePokemonSearchText}
                onSelectTrade={setSelectedTradePokemonIndex}
                onStartEditSession={handleStartEditSession}
                onUpdateTradePokemonField={handleUpdateTradePokemonField}
                onUpdateTradePokemonFields={handleUpdateTradePokemonFields}
                searchText={tradePokemonSearchText}
                selectedTradeIndex={selectedTradePokemonIndex}
                workflow={tradePokemonWorkflow}
              />
            )
          ) : null}
          {activeSection === 'staticEncounters' ? (
            isStaticEncountersLoading && !staticEncountersWorkflow ? (
              <WorkflowLoadingPanel label="Static Encounters" />
            ) : (
              <StaticEncountersSection
                editSession={editSession}
                isEditStarting={isEditStarting}
                isStaticEncounterUpdating={isStaticEncounterUpdating}
                onSearchChange={setStaticEncounterSearchText}
                onSelectEncounter={setSelectedStaticEncounterIndex}
                onStartEditSession={handleStartEditSession}
                onUpdateStaticEncounterField={handleUpdateStaticEncounterField}
                onUpdateStaticEncounterFields={handleUpdateStaticEncounterFields}
                searchText={staticEncounterSearchText}
                selectedEncounterIndex={selectedStaticEncounterIndex}
                workflow={staticEncountersWorkflow}
              />
            )
          ) : null}
          {activeSection === 'rentalPokemon' ? (
            isRentalPokemonLoading && !rentalPokemonWorkflow ? (
              <WorkflowLoadingPanel label="Rental Pokemon" />
            ) : (
              <RentalPokemonSection
                editSession={editSession}
                isEditStarting={isEditStarting}
                isRentalPokemonUpdating={isRentalPokemonUpdating}
                onSearchChange={setRentalPokemonSearchText}
                onSelectRental={setSelectedRentalPokemonIndex}
                onStartEditSession={handleStartEditSession}
                onUpdateRentalPokemonField={handleUpdateRentalPokemonField}
                onUpdateRentalPokemonFields={handleUpdateRentalPokemonFields}
                searchText={rentalPokemonSearchText}
                selectedRentalIndex={selectedRentalPokemonIndex}
                workflow={rentalPokemonWorkflow}
              />
            )
          ) : null}
          {activeSection === 'dynamaxAdventures' ? (
            isDynamaxAdventuresLoading && !dynamaxAdventuresWorkflow ? (
              <WorkflowLoadingPanel label="Dynamax Adventures" />
            ) : (
              <DynamaxAdventuresSection
                editSession={editSession}
                isDynamaxAdventureUpdating={isDynamaxAdventureUpdating}
                isEditStarting={isEditStarting}
                onSearchChange={setDynamaxAdventureSearchText}
                onSelectAdventure={setSelectedDynamaxAdventureEntryIndex}
                onStartEditSession={handleStartEditSession}
                onUpdateDynamaxAdventureField={handleUpdateDynamaxAdventureField}
                onUpdateDynamaxAdventureFields={handleUpdateDynamaxAdventureFields}
                searchText={dynamaxAdventureSearchText}
                selectedEntryIndex={selectedDynamaxAdventureEntryIndex}
                workflow={dynamaxAdventuresWorkflow}
              />
            )
          ) : null}
          {activeSection === 'shops' ? (
            isShopsLoading && !shopsWorkflow ? (
              <WorkflowLoadingPanel label="Shops" />
            ) : (
              <ShopsSection
                editSession={editSession}
                isEditStarting={isEditStarting}
                isShopUpdating={isShopUpdating}
                onSearchChange={setShopSearchText}
                onOpenItem={handleOpenShopItem}
                onSelectShop={setSelectedShopId}
                onStartEditSession={handleStartEditSession}
                onUpdateShopInventoryItem={handleUpdateShopInventoryItem}
                searchText={shopSearchText}
                selectedShopId={selectedShopId}
                workflow={shopsWorkflow}
              />
            )
          ) : null}
          {activeSection === 'encounters' ? (
            isEncountersLoading && !encountersWorkflow ? (
              <WorkflowLoadingPanel label="Encounters and Wild Data" />
            ) : (
              <EncountersSection
                editSession={editSession}
                isEditStarting={isEditStarting}
                isEncounterUpdating={isEncounterUpdating}
                onApplyNormalToAllWeather={handleApplyEncounterNormalToAllWeather}
                onSearchChange={setEncounterSearchText}
                onSelectTable={setSelectedEncounterTableId}
                onStartEditSession={handleStartEditSession}
                onUpdateEncounterSlotField={handleUpdateEncounterSlotField}
                onUpdateEncounterSlotFields={handleUpdateEncounterSlotFields}
                searchText={encounterSearchText}
                selectedTableId={selectedEncounterTableId}
                workflow={encountersWorkflow}
              />
            )
          ) : null}
          {activeSection === 'raidRewards' ? (
            isRaidRewardsLoading && !raidRewardsWorkflow ? (
              <WorkflowLoadingPanel label="Raid Rewards" />
            ) : (
              <RaidRewardsSection
                editSession={editSession}
                isEditStarting={isEditStarting}
                isRaidRewardUpdating={isRaidRewardUpdating}
                onSearchChange={setRaidRewardSearchText}
                onSelectTable={setSelectedRaidRewardTableId}
                onStartEditSession={handleStartEditSession}
                onUpdateRaidRewardField={handleUpdateRaidRewardField}
                onUpdateRaidRewardFields={handleUpdateRaidRewardFields}
                searchText={raidRewardSearchText}
                selectedTableId={selectedRaidRewardTableId}
                workflow={raidRewardsWorkflow}
              />
            )
          ) : null}
          {activeSection === 'raidBattles' ? (
            isRaidBattlesLoading && !raidBattlesWorkflow ? (
              <WorkflowLoadingPanel label="Raid Battles" />
            ) : (
              <RaidBattlesSection
                editSession={editSession}
                isEditStarting={isEditStarting}
                isRaidBattleUpdating={isRaidBattleUpdating}
                onSearchChange={setRaidBattleSearchText}
                onSelectTable={setSelectedRaidBattleTableId}
                onStartEditSession={handleStartEditSession}
                onUpdateRaidBattleSlotField={handleUpdateRaidBattleSlotField}
                onUpdateRaidBattleSlotFields={handleUpdateRaidBattleSlotFields}
                searchText={raidBattleSearchText}
                selectedTableId={selectedRaidBattleTableId}
                workflow={raidBattlesWorkflow}
              />
            )
          ) : null}
          {activeSection === 'placement' ? (
            isPlacementLoading && !placementWorkflow ? (
              <WorkflowLoadingPanel label="Placement" />
            ) : (
              <PlacementSection
                editSession={editSession}
                isEditStarting={isEditStarting}
                isPlacementUpdating={isPlacementUpdating}
                onSearchChange={setPlacementSearchText}
                onSelectObject={setSelectedPlacementObjectId}
                onStartEditSession={handleStartEditSession}
                onUpdatePlacementObjectField={handleUpdatePlacementObjectField}
                onUpdatePlacementObjectFields={handleUpdatePlacementObjectFields}
                searchText={placementSearchText}
                selectedObjectId={selectedPlacementObjectId}
                workflow={placementWorkflow}
              />
            )
          ) : null}
          {activeSection === 'flagworkSave' ? (
            isFlagworkSaveLoading && !flagworkSaveWorkflow ? (
              <WorkflowLoadingPanel label="Flagwork and Save Inspectors" />
            ) : (
              <FlagworkSaveSection
                onSearchChange={setFlagworkSaveSearchText}
                onSelectFlag={setSelectedFlagId}
                onSelectSaveBlock={setSelectedSaveBlockId}
                searchText={flagworkSaveSearchText}
                selectedFlagId={selectedFlagId}
                selectedSaveBlockId={selectedSaveBlockId}
                workflow={flagworkSaveWorkflow}
              />
            )
          ) : null}
          {activeSection === 'exefsPatches' ? (
            isExeFsPatchLoading && !exeFsPatchWorkflow ? (
              <WorkflowLoadingPanel label="ExeFS Patch Manager" />
            ) : (
              <ExeFsPatchSection
                isStaging={isExeFsPatchStaging}
                onSearchChange={setExeFsPatchSearchText}
                onSelectCheck={setSelectedExeFsCheckId}
                onSelectPatch={setSelectedExeFsPatchId}
                onStagePatch={handleStageExeFsPatch}
                searchText={exeFsPatchSearchText}
                selectedCheckId={selectedExeFsCheckId}
                selectedPatchId={selectedExeFsPatchId}
                workflow={exeFsPatchWorkflow}
              />
            )
          ) : null}
          {activeSection === 'royalCandy' ? (
            isRoyalCandyLoading && !royalCandyWorkflow ? (
              <WorkflowLoadingPanel label="Royal Candy Workflows" />
            ) : (
              <RoyalCandySection
                changePlan={changePlan}
                editSession={editSession}
                isChangePlanApplying={isChangePlanApplying}
                isChangePlanCreating={isChangePlanCreating}
                isStaging={isRoyalCandyStaging}
                onApplyChangePlan={handleApplyChangePlan}
                onCreateChangePlan={handleCreateChangePlan}
                onSearchChange={setRoyalCandySearchText}
                onSelectCheck={setSelectedRoyalCandyCheckId}
                onSelectWorkflow={setSelectedRoyalCandyWorkflowId}
                onStageWorkflow={handleStageRoyalCandyWorkflow}
                searchText={royalCandySearchText}
                selectedCheckId={selectedRoyalCandyCheckId}
                selectedWorkflowId={selectedRoyalCandyWorkflowId}
                workflow={royalCandyWorkflow}
              />
            )
          ) : null}
          {activeSection === 'spreadsheetImport' ? (
            isSpreadsheetImportLoading && !spreadsheetImportWorkflow ? (
              <WorkflowLoadingPanel label="Spreadsheet Import" />
            ) : (
              <SpreadsheetImportSection
                editSession={editSession}
                isPreviewing={isSpreadsheetImportPreviewing}
                onPreviewImport={handlePreviewSpreadsheetImport}
                onSearchChange={setSpreadsheetImportSearchText}
                onSelectProfile={setSelectedSpreadsheetImportProfileId}
                onSourcePathChange={setSpreadsheetImportSourcePath}
                preview={spreadsheetImportPreview}
                searchText={spreadsheetImportSearchText}
                selectedProfileId={selectedSpreadsheetImportProfileId}
                sourcePath={spreadsheetImportSourcePath}
                workflow={spreadsheetImportWorkflow}
              />
            )
          ) : null}
          {activeSection === 'changes' ? (
            <ChangesSection
              applyResult={applyResult}
              canSaveValidatedChanges={canSaveValidatedChanges}
              changePlan={visibleChangePlan}
              diagnostics={editValidationDiagnostics}
              editSession={editSession}
              isEditSessionValidated={isEditSessionValidated}
              isChangePlanApplying={isChangePlanApplying}
              isChangePlanCreating={isChangePlanCreating}
              isSessionValidating={isSessionValidating}
              onCancelEditSession={handleCancelEditSession}
              onSaveValidatedChanges={handleSaveValidatedChanges}
              onValidateEditSession={handleValidateEditSession}
            />
          ) : null}
          {activeSection !== 'health' && bridgeDiagnostics.length > 0 ? (
            <DiagnosticsSection diagnostics={bridgeDiagnostics} />
          ) : null}
        </div>
      </section>
      {saveProgress ? <SaveProgressModal progress={saveProgress} /> : null}
      {exitPrompt ? (
        <ExitPromptModal
          mode={exitPrompt.mode}
          onConfirmDiscard={handleConfirmExitDiscard}
          onDeclineDiscard={handleDeclineExitDiscard}
          onGoToChanges={handleGoToChangesAfterExitDecline}
          onStay={handleStayAfterExitDecline}
        />
      ) : null}
    </main>
  );
}

function WorkflowLoadingPanel({ label }: { label: string }) {
  return (
    <section aria-labelledby="workflow-loading-heading" className="panel wide-panel">
      <div className="panel-heading">
        <RefreshCw aria-hidden="true" size={18} />
        <h2 id="workflow-loading-heading">{label}</h2>
      </div>

      <p className="empty-copy">Loading backend workflow data.</p>
    </section>
  );
}

function VirtualTableBody<T>({
  getKey,
  items,
  renderRow
}: {
  getKey: (item: T, index: number) => string | number;
  items: T[];
  renderRow: (item: T) => ReactNode;
}) {
  const scrollParentRef = useRef<HTMLDivElement | null>(null);
  const rowVirtualizer = useVirtualizer({
    count: items.length,
    estimateSize: () => virtualTableRowHeight,
    getItemKey: (index) => getKey(items[index]!, index),
    getScrollElement: () => scrollParentRef.current,
    initialRect: virtualTableInitialRect,
    overscan: virtualTableOverscan,
    ...(observeVirtualTableElementRect
      ? { observeElementRect: observeVirtualTableElementRect }
      : {})
  });

  return (
    <div className="virtual-table-body" ref={scrollParentRef} role="rowgroup">
      <div
        className="virtual-table-spacer"
        style={{ height: `${rowVirtualizer.getTotalSize()}px` }}
      >
        {rowVirtualizer.getVirtualItems().map((virtualRow) => {
          const item = items[virtualRow.index];

          if (item === undefined) {
            return null;
          }

          return (
            <div
              className="virtual-table-row"
              key={virtualRow.key}
              role="presentation"
              style={{
                height: `${virtualRow.size}px`,
                transform: `translateY(${virtualRow.start}px)`
              }}
            >
              {renderRow(item)}
            </div>
          );
        })}
      </div>
    </div>
  );
}

function HealthSection({
  bridgeDiagnostics,
  draftPaths,
  health,
  isBusy,
  isDesktopAvailable,
  onOpenProject,
  onOpenOutputRoot,
  onPickProjectPath,
  onSetDraftPath,
  onValidateProject,
  pendingEditCount,
  projectStatus
}: {
  bridgeDiagnostics: ApiDiagnostic[];
  draftPaths: ProjectPathDraft;
  health: ProjectHealth | null;
  isBusy: boolean;
  isDesktopAvailable: boolean;
  onOpenProject: () => void;
  onOpenOutputRoot: () => void;
  onPickProjectPath: (pathField: ProjectPathField) => void;
  onSetDraftPath: (field: keyof ProjectPathDraft, value: string) => void;
  onValidateProject: () => void;
  pendingEditCount: number;
  projectStatus: 'idle' | 'validating' | 'opening' | 'open';
}) {
  const outputRootPath = draftPaths.outputRootPath.trim();

  return (
    <>
      <section aria-labelledby="project-gate-heading" className="panel project-gate">
        <div className="panel-heading">
          <FolderOpen aria-hidden="true" size={18} />
          <h2 id="project-gate-heading">Project Paths</h2>
        </div>

        <div className="path-form">
          {pathFields.map((pathField) => {
            const pathValidation = health?.paths.find((path) => path.role === pathField.role);
            const inputId = `${pathField.field}-input`;

            return (
              <div className="path-field" key={pathField.field}>
                <label htmlFor={inputId}>{pathField.label}</label>
                <div className="path-input-row">
                  <input
                    aria-describedby={`${pathField.field}-status`}
                    id={inputId}
                    onChange={(event) => onSetDraftPath(pathField.field, event.target.value)}
                    placeholder="Not set"
                    value={draftPaths[pathField.field]}
                  />
                  <button
                    aria-label={`Browse for ${pathField.label}`}
                    className="secondary-button icon-button"
                    disabled={!isDesktopAvailable || isBusy}
                    onClick={() => onPickProjectPath(pathField)}
                    title={`Browse for ${pathField.label}`}
                    type="button"
                  >
                    {pathField.kind === 'file' ? (
                      <Save aria-hidden="true" size={18} />
                    ) : (
                      <FolderOpen aria-hidden="true" size={18} />
                    )}
                  </button>
                </div>
                <small
                  className={getPathStatusClassName(pathValidation)}
                  id={`${pathField.field}-status`}
                >
                  {pathValidation ? pathStatusLabels[pathValidation.status] : 'Not checked'}
                </small>
              </div>
            );
          })}
        </div>

        <div className="action-row">
          <button
            className="secondary-button"
            disabled={isBusy}
            onClick={onValidateProject}
            type="button"
          >
            <RefreshCw aria-hidden="true" size={18} />
            <span>{projectStatus === 'validating' ? 'Validating' : 'Validate Paths'}</span>
          </button>
          <button
            className="primary-button"
            disabled={isBusy}
            onClick={onOpenProject}
            type="button"
          >
            <FolderOpen aria-hidden="true" size={18} />
            <span>{projectStatus === 'opening' ? 'Opening' : 'Open Project'}</span>
          </button>
          <button
            className="secondary-button"
            disabled={!isDesktopAvailable || isBusy || outputRootPath.length === 0}
            onClick={onOpenOutputRoot}
            type="button"
          >
            <ExternalLink aria-hidden="true" size={18} />
            <span>Open Output Root</span>
          </button>
        </div>
      </section>

      <section aria-labelledby="health-heading" className="panel">
        <div className="panel-heading">
          <ShieldCheck aria-hidden="true" size={18} />
          <h2 id="health-heading">Health Summary</h2>
        </div>

        <div className="health-grid">
          <Metric label="State" value={health ? healthLabels[health.state] : 'No project'} />
          <Metric
            label="Read-only workflows"
            value={health?.canOpenReadOnlyWorkflows ? 'Enabled' : 'Disabled'}
          />
          <Metric
            label="Write workflows"
            value={health?.canOpenEditableWorkflows ? 'Enabled' : 'Disabled'}
          />
          <Metric label="Pending changes" value={pendingEditCount.toString()} />
        </div>
      </section>

      <PathStatusSection health={health} />
      <DiagnosticsSection diagnostics={[...bridgeDiagnostics, ...(health?.diagnostics ?? [])]} />
    </>
  );
}

function WorkflowsSection({
  health,
  isEncountersLoading,
  isExeFsPatchLoading,
  isItemsLoading,
  isMovesLoading,
  isPokemonLoading,
  isShopsLoading,
  isTextLoading,
  isTrainersLoading,
  isRaidBattlesLoading,
  isRaidRewardsLoading,
  isPlacementLoading,
  isFlagworkSaveLoading,
  isGiftPokemonLoading,
  isTradePokemonLoading,
  isStaticEncountersLoading,
  isRentalPokemonLoading,
  isDynamaxAdventuresLoading,
  isRoyalCandyLoading,
  isSpreadsheetImportLoading,
  onOpenEncountersWorkflow,
  onOpenExeFsPatchWorkflow,
  onOpenFlagworkSaveWorkflow,
  onOpenGiftPokemonWorkflow,
  onOpenTradePokemonWorkflow,
  onOpenStaticEncountersWorkflow,
  onOpenRentalPokemonWorkflow,
  onOpenDynamaxAdventuresWorkflow,
  onOpenItemsWorkflow,
  onOpenMovesWorkflow,
  onOpenPokemonWorkflow,
  onOpenPlacementWorkflow,
  onOpenRaidBattlesWorkflow,
  onOpenRaidRewardsWorkflow,
  onOpenRoyalCandyWorkflow,
  onOpenShopsWorkflow,
  onOpenSpreadsheetImportWorkflow,
  onOpenTextWorkflow,
  onOpenTrainersWorkflow,
  pendingEditCount,
  workflows
}: {
  health: ProjectHealth | null;
  isEncountersLoading: boolean;
  isExeFsPatchLoading: boolean;
  isItemsLoading: boolean;
  isMovesLoading: boolean;
  isPokemonLoading: boolean;
  isShopsLoading: boolean;
  isTextLoading: boolean;
  isTrainersLoading: boolean;
  isRaidBattlesLoading: boolean;
  isRaidRewardsLoading: boolean;
  isPlacementLoading: boolean;
  isFlagworkSaveLoading: boolean;
  isGiftPokemonLoading: boolean;
  isTradePokemonLoading: boolean;
  isStaticEncountersLoading: boolean;
  isRentalPokemonLoading: boolean;
  isDynamaxAdventuresLoading: boolean;
  isRoyalCandyLoading: boolean;
  isSpreadsheetImportLoading: boolean;
  onOpenEncountersWorkflow: () => void;
  onOpenExeFsPatchWorkflow: () => void;
  onOpenFlagworkSaveWorkflow: () => void;
  onOpenGiftPokemonWorkflow: () => void;
  onOpenTradePokemonWorkflow: () => void;
  onOpenStaticEncountersWorkflow: () => void;
  onOpenRentalPokemonWorkflow: () => void;
  onOpenDynamaxAdventuresWorkflow: () => void;
  onOpenItemsWorkflow: () => void;
  onOpenMovesWorkflow: () => void;
  onOpenPokemonWorkflow: () => void;
  onOpenPlacementWorkflow: () => void;
  onOpenRaidBattlesWorkflow: () => void;
  onOpenRaidRewardsWorkflow: () => void;
  onOpenRoyalCandyWorkflow: () => void;
  onOpenShopsWorkflow: () => void;
  onOpenSpreadsheetImportWorkflow: () => void;
  onOpenTextWorkflow: () => void;
  onOpenTrainersWorkflow: () => void;
  pendingEditCount: number;
  workflows: WorkflowSummary[];
}) {
  return (
    <section aria-labelledby="workflows-heading" className="panel wide-panel">
      <div className="panel-heading">
        <ListChecks aria-hidden="true" size={18} />
        <h2 id="workflows-heading">Workflow List</h2>
      </div>

      <div className="workflow-list">
        {workflowDefinitions.map((definition) => {
          const workflow = workflows.find((candidate) => candidate.id === definition.id);
          const workflowState = getWorkflowState(health, workflow);
          const Icon = definition.icon;
          const isItemsWorkflow = definition.id === 'items';
          const isPokemonWorkflow = definition.id === 'pokemon';
          const isMovesWorkflow = definition.id === 'moves';
          const isTextWorkflow = definition.id === 'text';
          const isTrainersWorkflow = definition.id === 'trainers';
          const isGiftPokemonWorkflow = definition.id === 'giftPokemon';
          const isTradePokemonWorkflow = definition.id === 'tradePokemon';
          const isStaticEncountersWorkflow = definition.id === 'staticEncounters';
          const isRentalPokemonWorkflow = definition.id === 'rentalPokemon';
          const isDynamaxAdventuresWorkflow = definition.id === 'dynamaxAdventures';
          const isShopsWorkflow = definition.id === 'shops';
          const isEncountersWorkflow = definition.id === 'encounters';
          const isRaidBattlesWorkflow = definition.id === 'raidBattles';
          const isRaidRewardsWorkflow = definition.id === 'raidRewards';
          const isPlacementWorkflow = definition.id === 'placement';
          const isFlagworkSaveWorkflow = definition.id === 'flagworkSave';
          const isExeFsPatchWorkflow = definition.id === 'exefsPatches';
          const isRoyalCandyWorkflow = definition.id === 'royalCandy';
          const isSpreadsheetImportWorkflow = definition.id === 'spreadsheetImport';
          const canOpenItems = isItemsWorkflow && workflowState.availability !== 'disabled';
          const canOpenPokemon = isPokemonWorkflow && workflowState.availability !== 'disabled';
          const canOpenMoves = isMovesWorkflow && workflowState.availability !== 'disabled';
          const canOpenText = isTextWorkflow && workflowState.availability !== 'disabled';
          const canOpenTrainers = isTrainersWorkflow && workflowState.availability !== 'disabled';
          const canOpenGiftPokemon =
            isGiftPokemonWorkflow && workflowState.availability !== 'disabled';
          const canOpenTradePokemon =
            isTradePokemonWorkflow && workflowState.availability !== 'disabled';
          const canOpenStaticEncounters =
            isStaticEncountersWorkflow && workflowState.availability !== 'disabled';
          const canOpenRentalPokemon =
            isRentalPokemonWorkflow && workflowState.availability !== 'disabled';
          const canOpenDynamaxAdventures =
            isDynamaxAdventuresWorkflow && workflowState.availability !== 'disabled';
          const canOpenShops = isShopsWorkflow && workflowState.availability !== 'disabled';
          const canOpenEncounters =
            isEncountersWorkflow && workflowState.availability !== 'disabled';
          const canOpenRaidBattles =
            isRaidBattlesWorkflow && workflowState.availability !== 'disabled';
          const canOpenRaidRewards =
            isRaidRewardsWorkflow && workflowState.availability !== 'disabled';
          const canOpenPlacement =
            isPlacementWorkflow && workflowState.availability !== 'disabled';
          const canOpenFlagworkSave =
            isFlagworkSaveWorkflow && workflowState.availability !== 'disabled';
          const canOpenExeFsPatch =
            isExeFsPatchWorkflow && workflowState.availability !== 'disabled';
          const canOpenRoyalCandy =
            isRoyalCandyWorkflow && workflowState.availability !== 'disabled';
          const canOpenSpreadsheetImport =
            isSpreadsheetImportWorkflow && workflowState.availability !== 'disabled';

          return (
            <article className="workflow-row" key={definition.id}>
              <div>
                <h3>{workflow?.label ?? definition.label}</h3>
                <p>{workflow?.description ?? definition.description}</p>
                {isItemsWorkflow ? (
                  <span className="inline-metric">Pending changes: {pendingEditCount}</span>
                ) : null}
              </div>
              <div className="workflow-actions">
                <span className={`status-pill ${workflowState.statusClass}`}>
                  {workflowState.label}
                </span>
                {isItemsWorkflow ? (
                  <button
                    className="secondary-button compact-button"
                    disabled={!canOpenItems || isItemsLoading}
                    onClick={onOpenItemsWorkflow}
                    type="button"
                  >
                    <Icon aria-hidden="true" size={16} />
                    <span>{isItemsLoading ? 'Loading' : 'Open Items'}</span>
                  </button>
                ) : null}
                {isPokemonWorkflow ? (
                  <button
                    className="secondary-button compact-button"
                    disabled={!canOpenPokemon || isPokemonLoading}
                    onClick={onOpenPokemonWorkflow}
                    type="button"
                  >
                    <Icon aria-hidden="true" size={16} />
                    <span>{isPokemonLoading ? 'Loading' : 'Open Pokemon'}</span>
                  </button>
                ) : null}
                {isMovesWorkflow ? (
                  <button
                    className="secondary-button compact-button"
                    disabled={!canOpenMoves || isMovesLoading}
                    onClick={onOpenMovesWorkflow}
                    type="button"
                  >
                    <Icon aria-hidden="true" size={16} />
                    <span>{isMovesLoading ? 'Loading' : 'Open Moves'}</span>
                  </button>
                ) : null}
                {isTextWorkflow ? (
                  <button
                    className="secondary-button compact-button"
                    disabled={!canOpenText || isTextLoading}
                    onClick={onOpenTextWorkflow}
                    type="button"
                  >
                    <Icon aria-hidden="true" size={16} />
                    <span>{isTextLoading ? 'Loading' : 'Open Text'}</span>
                  </button>
                ) : null}
                {isTrainersWorkflow ? (
                  <button
                    className="secondary-button compact-button"
                    disabled={!canOpenTrainers || isTrainersLoading}
                    onClick={onOpenTrainersWorkflow}
                    type="button"
                  >
                    <Icon aria-hidden="true" size={16} />
                    <span>{isTrainersLoading ? 'Loading' : 'Open Trainers'}</span>
                  </button>
                ) : null}
                {isGiftPokemonWorkflow ? (
                  <button
                    className="secondary-button compact-button"
                    disabled={!canOpenGiftPokemon || isGiftPokemonLoading}
                    onClick={onOpenGiftPokemonWorkflow}
                    type="button"
                  >
                    <Icon aria-hidden="true" size={16} />
                    <span>{isGiftPokemonLoading ? 'Loading' : 'Open Gifts'}</span>
                  </button>
                ) : null}
                {isTradePokemonWorkflow ? (
                  <button
                    className="secondary-button compact-button"
                    disabled={!canOpenTradePokemon || isTradePokemonLoading}
                    onClick={onOpenTradePokemonWorkflow}
                    type="button"
                  >
                    <Icon aria-hidden="true" size={16} />
                    <span>{isTradePokemonLoading ? 'Loading' : 'Open Trades'}</span>
                  </button>
                ) : null}
                {isStaticEncountersWorkflow ? (
                  <button
                    className="secondary-button compact-button"
                    disabled={!canOpenStaticEncounters || isStaticEncountersLoading}
                    onClick={onOpenStaticEncountersWorkflow}
                    type="button"
                  >
                    <Icon aria-hidden="true" size={16} />
                    <span>
                      {isStaticEncountersLoading ? 'Loading' : 'Open Static Encounters'}
                    </span>
                  </button>
                ) : null}
                {isRentalPokemonWorkflow ? (
                  <button
                    className="secondary-button compact-button"
                    disabled={!canOpenRentalPokemon || isRentalPokemonLoading}
                    onClick={onOpenRentalPokemonWorkflow}
                    type="button"
                  >
                    <Icon aria-hidden="true" size={16} />
                    <span>{isRentalPokemonLoading ? 'Loading' : 'Open Rentals'}</span>
                  </button>
                ) : null}
                {isDynamaxAdventuresWorkflow ? (
                  <button
                    className="secondary-button compact-button"
                    disabled={!canOpenDynamaxAdventures || isDynamaxAdventuresLoading}
                    onClick={onOpenDynamaxAdventuresWorkflow}
                    type="button"
                  >
                    <Icon aria-hidden="true" size={16} />
                    <span>
                      {isDynamaxAdventuresLoading ? 'Loading' : 'Open Adventures'}
                    </span>
                  </button>
                ) : null}
                {isShopsWorkflow ? (
                  <button
                    className="secondary-button compact-button"
                    disabled={!canOpenShops || isShopsLoading}
                    onClick={onOpenShopsWorkflow}
                    type="button"
                  >
                    <Icon aria-hidden="true" size={16} />
                    <span>{isShopsLoading ? 'Loading' : 'Open Shops'}</span>
                  </button>
                ) : null}
                {isEncountersWorkflow ? (
                  <button
                    className="secondary-button compact-button"
                    disabled={!canOpenEncounters || isEncountersLoading}
                    onClick={onOpenEncountersWorkflow}
                    type="button"
                  >
                    <Icon aria-hidden="true" size={16} />
                    <span>{isEncountersLoading ? 'Loading' : 'Open Encounters'}</span>
                  </button>
                ) : null}
                {isRaidBattlesWorkflow ? (
                  <button
                    className="secondary-button compact-button"
                    disabled={!canOpenRaidBattles || isRaidBattlesLoading}
                    onClick={onOpenRaidBattlesWorkflow}
                    type="button"
                  >
                    <Icon aria-hidden="true" size={16} />
                    <span>{isRaidBattlesLoading ? 'Loading' : 'Open Raid Battles'}</span>
                  </button>
                ) : null}
                {isRaidRewardsWorkflow ? (
                  <button
                    className="secondary-button compact-button"
                    disabled={!canOpenRaidRewards || isRaidRewardsLoading}
                    onClick={onOpenRaidRewardsWorkflow}
                    type="button"
                  >
                    <Icon aria-hidden="true" size={16} />
                    <span>{isRaidRewardsLoading ? 'Loading' : 'Open Raid Rewards'}</span>
                  </button>
                ) : null}
                {isPlacementWorkflow ? (
                  <button
                    className="secondary-button compact-button"
                    disabled={!canOpenPlacement || isPlacementLoading}
                    onClick={onOpenPlacementWorkflow}
                    type="button"
                  >
                    <Icon aria-hidden="true" size={16} />
                    <span>{isPlacementLoading ? 'Loading' : 'Open Placement'}</span>
                  </button>
                ) : null}
                {isFlagworkSaveWorkflow ? (
                  <button
                    className="secondary-button compact-button"
                    disabled={!canOpenFlagworkSave || isFlagworkSaveLoading}
                    onClick={onOpenFlagworkSaveWorkflow}
                    type="button"
                  >
                    <Icon aria-hidden="true" size={16} />
                    <span>{isFlagworkSaveLoading ? 'Loading' : 'Open Flagwork'}</span>
                  </button>
                ) : null}
                {isExeFsPatchWorkflow ? (
                  <button
                    className="secondary-button compact-button"
                    disabled={!canOpenExeFsPatch || isExeFsPatchLoading}
                    onClick={onOpenExeFsPatchWorkflow}
                    type="button"
                  >
                    <Icon aria-hidden="true" size={16} />
                    <span>{isExeFsPatchLoading ? 'Loading' : 'Open ExeFS'}</span>
                  </button>
                ) : null}
                {isRoyalCandyWorkflow ? (
                  <button
                    className="secondary-button compact-button"
                    disabled={!canOpenRoyalCandy || isRoyalCandyLoading}
                    onClick={onOpenRoyalCandyWorkflow}
                    type="button"
                  >
                    <Icon aria-hidden="true" size={16} />
                    <span>{isRoyalCandyLoading ? 'Loading' : 'Open Candy'}</span>
                  </button>
                ) : null}
                {isSpreadsheetImportWorkflow ? (
                  <button
                    className="secondary-button compact-button"
                    disabled={!canOpenSpreadsheetImport || isSpreadsheetImportLoading}
                    onClick={onOpenSpreadsheetImportWorkflow}
                    type="button"
                  >
                    <Icon aria-hidden="true" size={16} />
                    <span>{isSpreadsheetImportLoading ? 'Loading' : 'Open Import'}</span>
                  </button>
                ) : null}
              </div>
            </article>
          );
        })}
      </div>
    </section>
  );
}

function ItemsSection({
  editSession,
  isEditStarting,
  isItemUpdating,
  onSearchChange,
  onSelectItem,
  onStartEditSession,
  onUpdateItemField,
  onUpdateItemFields,
  searchText,
  selectedItemId,
  workflow
}: {
  editSession: EditSession | null;
  isEditStarting: boolean;
  isItemUpdating: boolean;
  onSearchChange: (searchText: string) => void;
  onSelectItem: (itemId: number | null) => void;
  onStartEditSession: () => void;
  onUpdateItemField: (itemId: number, field: string, value: string) => void;
  onUpdateItemFields: (itemId: number, changes: Array<{ field: string; value: string }>) => void;
  searchText: string;
  selectedItemId: number | null;
  workflow: ItemsWorkflow | null;
}) {
  const items = useMemo(
    () => (workflow?.items ?? []).filter((item) => item.itemId !== 0),
    [workflow?.items]
  );
  const filteredItems = useMemo(() => filterItems(items, searchText), [items, searchText]);
  const selectedItem = useMemo(
    () => items.find((item) => item.itemId === selectedItemId) ?? filteredItems[0] ?? null,
    [filteredItems, items, selectedItemId]
  );
  const canEditItems = workflow?.summary.availability === 'available';
  const pendingItemIds = useMemo(() => getPendingItemIds(editSession), [editSession]);

  return (
    <>
      <section aria-labelledby="items-heading" className="panel wide-panel">
        <div className="panel-heading">
          <Package aria-hidden="true" size={18} />
          <h2 id="items-heading">Items</h2>
        </div>

        <div className="items-toolbar">
          <label className="search-box items-search">
            <Search aria-hidden="true" size={18} />
            <input
              aria-label="Search items"
              disabled={!workflow}
              onChange={(event) => onSearchChange(event.target.value)}
              placeholder="Search items"
              type="search"
              value={searchText}
            />
          </label>
          <Metric
            label="Loaded records"
            value={workflow ? workflow.stats.totalItemCount.toString() : '0'}
          />
          <Metric
            label="Pending changes"
            value={(editSession?.pendingEdits.length ?? 0).toString()}
          />
        </div>

        {workflow ? (
          <div className="items-layout">
            <div
              aria-colcount={8}
              aria-label="Items"
              aria-rowcount={filteredItems.length + 1}
              className="items-table"
              role="table"
            >
              <div className="items-row items-row-heading" role="row">
                <span role="columnheader">ID</span>
                <span role="columnheader">Name</span>
                <span role="columnheader">Category</span>
                <span role="columnheader">Buy</span>
                <span role="columnheader">Sell</span>
                <span role="columnheader">Watts</span>
                <span role="columnheader">Alt</span>
                <span role="columnheader">Source</span>
              </div>
              <VirtualTableBody
                getKey={(item) => item.itemId}
                items={filteredItems}
                renderRow={(item) => (
                  <button
                    className={`items-row ${selectedItem?.itemId === item.itemId ? 'items-row-selected' : ''} ${
                      pendingItemIds.has(item.itemId) ? 'items-row-pending' : ''
                    }`}
                    onClick={() => onSelectItem(item.itemId)}
                    role="row"
                    type="button"
                  >
                    <span role="cell">{item.itemId}</span>
                    <span role="cell">{item.name}</span>
                    <span role="cell">{item.category}</span>
                    <span role="cell">{item.buyPrice}</span>
                    <span role="cell">{item.sellPrice}</span>
                    <span role="cell">{item.wattsPrice}</span>
                    <span role="cell">{item.alternatePrice}</span>
                    <span role="cell">{formatSourceLayer(item.provenance.sourceLayer)}</span>
                  </button>
                )}
              />
            </div>

            <SelectedItemPanel
              canEditItems={canEditItems}
              editSession={editSession}
              isEditStarting={isEditStarting}
              isItemUpdating={isItemUpdating}
              item={selectedItem}
              editableFields={workflow.editableFields}
              onStartEditSession={onStartEditSession}
              onUpdateItemField={onUpdateItemField}
              onUpdateItemFields={onUpdateItemFields}
            />
          </div>
        ) : (
          <p className="empty-copy">Open Items from Workflows to load backend item data.</p>
        )}
      </section>

      <DiagnosticsSection diagnostics={workflow?.diagnostics ?? []} />
    </>
  );
}

function SelectedItemPanel({
  canEditItems,
  editSession,
  editableFields,
  isEditStarting,
  isItemUpdating,
  item,
  onStartEditSession,
  onUpdateItemField,
  onUpdateItemFields
}: {
  canEditItems: boolean;
  editSession: EditSession | null;
  editableFields: ItemEditableField[];
  isEditStarting: boolean;
  isItemUpdating: boolean;
  item: ItemRecord | null;
  onStartEditSession: () => void;
  onUpdateItemField: (itemId: number, field: string, value: string) => void;
  onUpdateItemFields: (itemId: number, changes: Array<{ field: string; value: string }>) => void;
}) {
  const [fieldDrafts, setFieldDrafts] = useState<Record<string, string>>({});
  const cancelActiveEditSession = useCancelActiveEditSession();
  const itemFieldGroups = useMemo(
    () => groupNumericEditableFields(editableFields, getItemEditableFieldGroup),
    [editableFields]
  );
  const itemDraftSummary = useMemo(
    () =>
      getTrainerDraftSummary(
        editableFields,
        fieldDrafts,
        item ? (field) => getEditableItemFieldValue(item, field) : null
      ),
    [editableFields, fieldDrafts, item]
  );
  const canSaveItemDrafts =
    item !== null &&
    editSession !== null &&
    canEditItems &&
    !isItemUpdating &&
    itemDraftSummary.changedFields.length > 0 &&
    itemDraftSummary.invalidFields.length === 0;

  useEffect(() => {
    if (!item) {
      setFieldDrafts({});
      return;
    }

    setFieldDrafts(
      Object.fromEntries(
        editableFields.map((field) => [
          field.field,
          (getEditableItemFieldValue(item, field.field) ?? '').toString()
        ])
      )
    );
  }, [editableFields, item]);

  return (
    <aside aria-label="Selected item provenance" className="item-inspector">
      <div className="panel-heading">
        <ShieldCheck aria-hidden="true" size={18} />
        <h3>Selected Item</h3>
      </div>

      {item ? (
        <>
          <dl className="item-provenance-list">
            <div>
              <dt>Name</dt>
              <dd>{item.name}</dd>
            </div>
            <div>
              <dt>Source file</dt>
              <dd>{item.provenance.sourceFile}</dd>
            </div>
            <div>
              <dt>Layer</dt>
              <dd>{formatSourceLayer(item.provenance.sourceLayer)}</dd>
            </div>
            <div>
              <dt>File state</dt>
              <dd>{formatFileState(item.provenance.fileState)}</dd>
            </div>
            <div>
              <dt>Shared row</dt>
              <dd>{formatSharedItemIds(item)}</dd>
            </div>
          </dl>

          <div className="item-edit-form">
            {editSession ? (
              <div className="draft-action-row">
                <button
                  className="primary-button"
                  disabled={!canSaveItemDrafts}
                  onClick={() =>
                    onUpdateItemFields(
                      item.itemId,
                      itemDraftSummary.changedFields.map((change) => ({
                        field: change.field,
                        value: change.value
                      }))
                    )
                  }
                  type="button"
                >
                  <Save aria-hidden="true" size={16} />
                  <span>{isItemUpdating ? 'Saving' : 'Save Item'}</span>
                </button>
                <button
                  className="danger-button"
                  disabled={isItemUpdating}
                  onClick={() => {
                    setFieldDrafts(createTrainerDrafts(editableFields, (field) =>
                      getEditableItemFieldValue(item, field)
                    ));
                    cancelActiveEditSession();
                  }}
                  type="button"
                >
                  <X aria-hidden="true" size={16} />
                  <span>Cancel</span>
                </button>
                <span className="draft-action-summary">{formatDraftSummary(itemDraftSummary)}</span>
              </div>
            ) : (
              <button
                className="secondary-button"
                disabled={!canEditItems || isEditStarting}
                onClick={onStartEditSession}
                type="button"
              >
                <Pencil aria-hidden="true" size={16} />
                <span>{isEditStarting ? 'Starting' : 'Edit'}</span>
              </button>
            )}

            <div className="editable-field-groups">
              {itemFieldGroups.map((group) => (
                <fieldset className="editable-field-group" key={group.group}>
                  <legend>{group.group}</legend>
                  <div className="editable-field-grid">
                    {group.fields.map((field) => {
                      const currentValue = getEditableItemFieldValue(item, field.field);
                      const draftValue = fieldDrafts[field.field] ?? '';
                      const draftState = getTrainerFieldDraftState(
                        draftValue,
                        currentValue,
                        field
                      );

                      return (
                        <GiftPokemonDraftField
                          currentValue={currentValue}
                          disabled={!canEditItems || editSession === null || isItemUpdating}
                          draftState={draftState}
                          draftValue={draftValue}
                          field={field}
                          idPrefix="item-field"
                          key={field.field}
                          onChange={(value) =>
                            setFieldDrafts((currentDrafts) => ({
                              ...currentDrafts,
                              [field.field]: value
                            }))
                          }
                        />
                      );
                    })}
                  </div>
                </fieldset>
              ))}
            </div>
          </div>

          {item.detailGroups.map((group) => (
            <section className="inspector-block" key={group.label}>
              <h4>{group.label}</h4>
              <dl className="item-provenance-list compact-dl">
                {group.details.map((detail) => (
                  <div key={`${group.label}:${detail.label}`}>
                    <dt>{detail.label}</dt>
                    <dd>{detail.value}</dd>
                  </div>
                ))}
              </dl>
            </section>
          ))}
        </>
      ) : (
        <p className="empty-copy">No item selected.</p>
      )}
    </aside>
  );
}

function isPlaceholderPokemonRecord(pokemon: Pick<PokemonRecord, 'name' | 'personalId'>) {
  return Number(pokemon.personalId) === 0 || pokemon.name.trim().toLowerCase() === 'egg';
}

function PokemonSection({
  editSession,
  isEditStarting,
  isPokemonUpdating,
  onSearchChange,
  onSelectPokemon,
  onStartEditSession,
  onUpdatePokemonField,
  onUpdatePokemonFields,
  onUpdatePokemonEvolution,
  onUpdatePokemonLearnset,
  searchText,
  selectedPokemonPersonalId,
  workflow
}: {
  editSession: EditSession | null;
  isEditStarting: boolean;
  isPokemonUpdating: boolean;
  onSearchChange: (searchText: string) => void;
  onSelectPokemon: (personalId: number | null) => void;
  onStartEditSession: () => void;
  onUpdatePokemonField: (personalId: number, field: string, value: string) => void;
  onUpdatePokemonFields: (
    personalId: number,
    changes: Array<{ field: string; value: string }>
  ) => void;
  onUpdatePokemonEvolution: (
    personalId: number,
    action: string,
    slot: number | null,
    method: number | null,
    argument: number | null,
    species: number | null,
    form: number | null,
    level: number | null
  ) => void;
  onUpdatePokemonLearnset: (
    personalId: number,
    action: string,
    slot: number | null,
    moveId: number | null,
    level: number | null
  ) => void;
  searchText: string;
  selectedPokemonPersonalId: number | null;
  workflow: PokemonWorkflow | null;
}) {
  const pokemon = workflow?.pokemon ?? [];
  const filteredPokemon = useMemo(
    () => filterPokemon(pokemon, searchText),
    [pokemon, searchText]
  );
  const selectedPokemon = useMemo(
    () => {
      const selectedPokemonId =
        selectedPokemonPersonalId === null ? null : Number(selectedPokemonPersonalId);
      const explicitlySelectedPokemon =
        selectedPokemonId === null || selectedPokemonId === 0
          ? null
          : filteredPokemon.find(
              (candidate) =>
                Number(candidate.personalId) === selectedPokemonId &&
                !isPlaceholderPokemonRecord(candidate)
            );

      return (
        explicitlySelectedPokemon ??
        filteredPokemon.find((candidate) => !isPlaceholderPokemonRecord(candidate)) ??
        filteredPokemon[0] ??
        null
      );
    },
    [filteredPokemon, selectedPokemonPersonalId]
  );
  const canEditPokemon = workflow?.summary.availability === 'available';
  const pendingPokemonIds = useMemo(() => getPendingPokemonIds(editSession), [editSession]);

  return (
    <>
      <section aria-labelledby="pokemon-heading" className="panel wide-panel">
        <div className="panel-heading">
          <Dna aria-hidden="true" size={18} />
          <h2 id="pokemon-heading">Pokemon Data</h2>
        </div>

        <div className="items-toolbar">
          <label className="search-box items-search">
            <Search aria-hidden="true" size={18} />
            <input
              aria-label="Search Pokemon"
              disabled={!workflow}
              onChange={(event) => onSearchChange(event.target.value)}
              placeholder="Search Pokemon"
              type="search"
              value={searchText}
            />
          </label>
          <Metric
            label="Loaded records"
            value={workflow ? workflow.stats.totalPokemonCount.toString() : '0'}
          />
          <Metric
            label="Present"
            value={workflow ? workflow.stats.presentPokemonCount.toString() : '0'}
          />
          <Metric
            label="Learnset moves"
            value={workflow ? workflow.stats.totalLearnsetMoveCount.toString() : '0'}
          />
        </div>

        {workflow ? (
          <div className="items-layout pokemon-layout">
            <div
              aria-colcount={8}
              aria-label="Pokemon Data"
              aria-rowcount={filteredPokemon.length + 1}
              className="items-table pokemon-table"
              role="table"
            >
              <div className="items-row items-row-heading" role="row">
                <span role="columnheader">ID</span>
                <span role="columnheader">Name</span>
                <span role="columnheader">Form</span>
                <span role="columnheader">Types</span>
                <span role="columnheader">HP</span>
                <span role="columnheader">BST</span>
                <span role="columnheader">Evo</span>
                <span role="columnheader">Learn</span>
              </div>
              <VirtualTableBody
                getKey={(record) => record.personalId}
                items={filteredPokemon}
                renderRow={(record) => (
                  <button
                    className={`items-row ${
                      selectedPokemon?.personalId === record.personalId
                        ? 'items-row-selected'
                        : ''
                    } ${pendingPokemonIds.has(record.personalId) ? 'moves-row-pending' : ''}`}
                    onClick={() => onSelectPokemon(record.personalId)}
                    role="row"
                    type="button"
                  >
                    <span role="cell">{record.personalId}</span>
                    <span role="cell">{record.name}</span>
                    <span role="cell">{record.formLabel}</span>
                    <span role="cell">{formatPokemonTypes(record)}</span>
                    <span role="cell">{record.baseStats.hp}</span>
                    <span role="cell">{record.baseStats.total}</span>
                    <span role="cell">{record.evolutions.length}</span>
                    <span role="cell">{record.learnset.length}</span>
                  </button>
                )}
              />
            </div>

            <SelectedPokemonPanel
              canEditPokemon={canEditPokemon}
              editSession={editSession}
              editableFields={workflow.editableFields}
              evolutionMethodOptions={workflow.evolutionMethodOptions}
              isEditStarting={isEditStarting}
              isPokemonUpdating={isPokemonUpdating}
              learnsetMoveOptions={workflow.learnsetMoveOptions}
              onStartEditSession={onStartEditSession}
              onUpdatePokemonField={onUpdatePokemonField}
              onUpdatePokemonFields={onUpdatePokemonFields}
              onUpdatePokemonEvolution={onUpdatePokemonEvolution}
              onUpdatePokemonLearnset={onUpdatePokemonLearnset}
              pokemon={selectedPokemon}
            />
          </div>
        ) : (
          <p className="empty-copy">Open Pokemon Data from Workflows to load backend Pokemon data.</p>
        )}
      </section>

      <DiagnosticsSection diagnostics={workflow?.diagnostics ?? []} />
    </>
  );
}

function SelectedPokemonPanel({
  canEditPokemon,
  editSession,
  editableFields,
  evolutionMethodOptions,
  isEditStarting,
  isPokemonUpdating,
  learnsetMoveOptions,
  onStartEditSession,
  onUpdatePokemonField,
  onUpdatePokemonFields,
  onUpdatePokemonEvolution,
  onUpdatePokemonLearnset,
  pokemon
}: {
  canEditPokemon: boolean;
  editSession: EditSession | null;
  editableFields: PokemonEditableField[];
  evolutionMethodOptions: PokemonEvolutionMethodOption[];
  isEditStarting: boolean;
  isPokemonUpdating: boolean;
  learnsetMoveOptions: PokemonEditableFieldOption[];
  onStartEditSession: () => void;
  onUpdatePokemonField: (personalId: number, field: string, value: string) => void;
  onUpdatePokemonFields: (
    personalId: number,
    changes: Array<{ field: string; value: string }>
  ) => void;
  onUpdatePokemonEvolution: (
    personalId: number,
    action: string,
    slot: number | null,
    method: number | null,
    argument: number | null,
    species: number | null,
    form: number | null,
    level: number | null
  ) => void;
  onUpdatePokemonLearnset: (
    personalId: number,
    action: string,
    slot: number | null,
    moveId: number | null,
    level: number | null
  ) => void;
  pokemon: PokemonRecord | null;
}) {
  const personalDraftDefaults = useMemo(
    () => createPokemonPersonalDrafts(pokemon, editableFields),
    [editableFields, pokemon]
  );
  const [personalDrafts, setPersonalDrafts] = useState<Record<string, string>>(
    personalDraftDefaults
  );
  const cancelActiveEditSession = useCancelActiveEditSession();
  const [selectedCompatibilityGroupId, setSelectedCompatibilityGroupId] = useState(
    pokemon?.compatibility[0]?.groupId ?? ''
  );
  const [compatibilitySearchText, setCompatibilitySearchText] = useState('');
  const [selectedEvolutionSlot, setSelectedEvolutionSlot] = useState(
    pokemon?.evolutions[0]?.slot ?? 0
  );
  const selectedEvolution =
    pokemon?.evolutions.find((evolution) => evolution.slot === selectedEvolutionSlot) ??
    pokemon?.evolutions[0] ??
    null;
  const pokemonSpeciesOptions = useMemo(
    () => editableFields.find((field) => field.field === 'hatchedSpecies')?.options ?? [],
    [editableFields]
  );
  const pokemonSpeciesLabels = useMemo(
    () => new Map(pokemonSpeciesOptions.map((option) => [option.value, option.label])),
    [pokemonSpeciesOptions]
  );
  const [evolutionMethodDraft, setEvolutionMethodDraft] = useState(
    selectedEvolution?.method.toString() ?? ''
  );
  const [evolutionArgumentDraft, setEvolutionArgumentDraft] = useState(
    selectedEvolution?.argument.toString() ?? ''
  );
  const [evolutionSpeciesDraft, setEvolutionSpeciesDraft] = useState(
    selectedEvolution?.species.toString() ?? ''
  );
  const [evolutionFormDraft, setEvolutionFormDraft] = useState(
    selectedEvolution?.form.toString() ?? ''
  );
  const [evolutionLevelDraft, setEvolutionLevelDraft] = useState(
    selectedEvolution?.level.toString() ?? ''
  );
  const [newEvolutionMethodDraft, setNewEvolutionMethodDraft] = useState('');
  const [newEvolutionArgumentDraft, setNewEvolutionArgumentDraft] = useState('0');
  const [newEvolutionSpeciesDraft, setNewEvolutionSpeciesDraft] = useState('');
  const [newEvolutionFormDraft, setNewEvolutionFormDraft] = useState('0');
  const [newEvolutionLevelDraft, setNewEvolutionLevelDraft] = useState('');
  const selectedEvolutionMethodOptions = useMemo(
    () => addCurrentEvolutionMethodOption(evolutionMethodOptions, evolutionMethodDraft),
    [evolutionMethodOptions, evolutionMethodDraft]
  );
  const selectedEvolutionMethodOption = useMemo(
    () => findEvolutionMethodOption(selectedEvolutionMethodOptions, evolutionMethodDraft),
    [evolutionMethodDraft, selectedEvolutionMethodOptions]
  );
  const selectedEvolutionArgumentOptions = useMemo(
    () => addCurrentPokemonFieldOption(
      selectedEvolutionMethodOption?.argumentOptions ?? [],
      evolutionArgumentDraft,
      selectedEvolutionMethodOption?.argumentLabel ?? 'Argument'
    ),
    [
      evolutionArgumentDraft,
      selectedEvolutionMethodOption?.argumentLabel,
      selectedEvolutionMethodOption?.argumentOptions
    ]
  );
  const newEvolutionMethodOptions = useMemo(
    () => addCurrentEvolutionMethodOption(evolutionMethodOptions, newEvolutionMethodDraft),
    [evolutionMethodOptions, newEvolutionMethodDraft]
  );
  const newEvolutionMethodOption = useMemo(
    () => findEvolutionMethodOption(newEvolutionMethodOptions, newEvolutionMethodDraft),
    [newEvolutionMethodDraft, newEvolutionMethodOptions]
  );
  const newEvolutionArgumentOptions = useMemo(
    () => addCurrentPokemonFieldOption(
      newEvolutionMethodOption?.argumentOptions ?? [],
      newEvolutionArgumentDraft,
      newEvolutionMethodOption?.argumentLabel ?? 'Argument'
    ),
    [
      newEvolutionArgumentDraft,
      newEvolutionMethodOption?.argumentLabel,
      newEvolutionMethodOption?.argumentOptions
    ]
  );
  const [selectedLearnsetSlot, setSelectedLearnsetSlot] = useState(
    pokemon?.learnset[0]?.slot ?? 0
  );
  const selectedLearnsetMove =
    pokemon?.learnset.find((move) => move.slot === selectedLearnsetSlot) ??
    pokemon?.learnset[0] ??
    null;
  const [learnsetMoveIdDraft, setLearnsetMoveIdDraft] = useState(
    selectedLearnsetMove?.moveId.toString() ?? ''
  );
  const [learnsetLevelDraft, setLearnsetLevelDraft] = useState(
    selectedLearnsetMove?.level.toString() ?? ''
  );
  const [newLearnsetMoveIdDraft, setNewLearnsetMoveIdDraft] = useState('');
  const [newLearnsetLevelDraft, setNewLearnsetLevelDraft] = useState('');
  const [draggedLearnsetSlot, setDraggedLearnsetSlot] = useState<number | null>(null);
  const [dragOverLearnsetSlot, setDragOverLearnsetSlot] = useState<number | null>(null);
  const learnsetMoveOptionsForDraft = useMemo(
    () => addCurrentPokemonFieldOption(learnsetMoveOptions, learnsetMoveIdDraft, 'Move'),
    [learnsetMoveIdDraft, learnsetMoveOptions]
  );
  const newLearnsetMoveOptions = useMemo(
    () => addCurrentPokemonFieldOption(learnsetMoveOptions, newLearnsetMoveIdDraft, 'Move'),
    [learnsetMoveOptions, newLearnsetMoveIdDraft]
  );

  useEffect(() => {
    setPersonalDrafts(personalDraftDefaults);
  }, [personalDraftDefaults]);

  useEffect(() => {
    if (!pokemon) {
      setSelectedCompatibilityGroupId('');
      return;
    }

    if (
      pokemon.compatibility.length > 0 &&
      !pokemon.compatibility.some((group) => group.groupId === selectedCompatibilityGroupId)
    ) {
      setSelectedCompatibilityGroupId(pokemon.compatibility[0].groupId);
    }
  }, [pokemon, selectedCompatibilityGroupId]);

  useEffect(() => {
    if (!pokemon || pokemon.learnset.length === 0) {
      setSelectedLearnsetSlot(0);
      return;
    }

    if (!pokemon.learnset.some((move) => move.slot === selectedLearnsetSlot)) {
      setSelectedLearnsetSlot(pokemon.learnset[0].slot);
    }
  }, [pokemon, selectedLearnsetSlot]);

  useEffect(() => {
    if (!pokemon || pokemon.evolutions.length === 0) {
      setSelectedEvolutionSlot(0);
      return;
    }

    if (!pokemon.evolutions.some((evolution) => evolution.slot === selectedEvolutionSlot)) {
      setSelectedEvolutionSlot(pokemon.evolutions[0].slot);
    }
  }, [pokemon, selectedEvolutionSlot]);

  useEffect(() => {
    setLearnsetMoveIdDraft(selectedLearnsetMove?.moveId.toString() ?? '');
    setLearnsetLevelDraft(selectedLearnsetMove?.level.toString() ?? '');
  }, [
    selectedLearnsetMove?.level,
    selectedLearnsetMove?.moveId,
    selectedLearnsetMove?.slot
  ]);

  useEffect(() => {
    setEvolutionMethodDraft(selectedEvolution?.method.toString() ?? '');
    setEvolutionArgumentDraft(selectedEvolution?.argument.toString() ?? '');
    setEvolutionSpeciesDraft(selectedEvolution?.species.toString() ?? '');
    setEvolutionFormDraft(selectedEvolution?.form.toString() ?? '');
    setEvolutionLevelDraft(selectedEvolution?.level.toString() ?? '');
  }, [
    selectedEvolution?.argument,
    selectedEvolution?.form,
    selectedEvolution?.level,
    selectedEvolution?.method,
    selectedEvolution?.slot,
    selectedEvolution?.species
  ]);

  const personalFieldGroups = useMemo(
    () => groupPokemonEditableFields(editableFields),
    [editableFields]
  );
  const personalDraftSummary = useMemo(
    () => getPokemonPersonalDraftSummary(pokemon, editableFields, personalDrafts),
    [editableFields, personalDrafts, pokemon]
  );
  const canSavePersonalDrafts =
    pokemon !== null &&
    editSession !== null &&
    canEditPokemon &&
    !isPokemonUpdating &&
    personalDraftSummary.changedFields.length > 0 &&
    personalDraftSummary.invalidFields.length === 0;
  const selectedCompatibilityGroup =
    pokemon?.compatibility.find((group) => group.groupId === selectedCompatibilityGroupId) ??
    pokemon?.compatibility[0] ??
    null;
  const filteredCompatibilityEntries = useMemo(
    () =>
      selectedCompatibilityGroup
        ? filterPokemonCompatibilityEntries(selectedCompatibilityGroup, compatibilitySearchText)
        : [],
    [compatibilitySearchText, selectedCompatibilityGroup]
  );
  const canToggleCompatibility = canEditPokemon && editSession !== null && !isPokemonUpdating;
  const canEditEvolution = canEditPokemon && editSession !== null && !isPokemonUpdating;
  const canEditLearnset = canEditPokemon && editSession !== null && !isPokemonUpdating;
  const parsedEvolutionMethod = parseEditableIntegerDraft(
    evolutionMethodDraft,
    selectedEvolutionMethodOptions
  );
  const parsedEvolutionArgument = parseEditableIntegerDraft(
    evolutionArgumentDraft,
    selectedEvolutionArgumentOptions
  );
  const parsedEvolutionSpecies = parseEditableIntegerDraft(
    evolutionSpeciesDraft,
    pokemonSpeciesOptions
  );
  const selectedEvolutionFormOptions = useMemo(
    () => createEvolutionFormOptions(parsedEvolutionSpecies, pokemonSpeciesLabels, evolutionFormDraft),
    [evolutionFormDraft, parsedEvolutionSpecies, pokemonSpeciesLabels]
  );
  const parsedEvolutionForm = parseEditableIntegerDraft(
    evolutionFormDraft,
    selectedEvolutionFormOptions
  );
  const parsedEvolutionLevel = Number.parseInt(evolutionLevelDraft, 10);
  const parsedNewEvolutionMethod = parseEditableIntegerDraft(
    newEvolutionMethodDraft,
    newEvolutionMethodOptions
  );
  const parsedNewEvolutionArgument = parseEditableIntegerDraft(
    newEvolutionArgumentDraft,
    newEvolutionArgumentOptions
  );
  const parsedNewEvolutionSpecies = parseEditableIntegerDraft(
    newEvolutionSpeciesDraft,
    pokemonSpeciesOptions
  );
  const newEvolutionFormOptions = useMemo(
    () => createEvolutionFormOptions(parsedNewEvolutionSpecies, pokemonSpeciesLabels, newEvolutionFormDraft),
    [newEvolutionFormDraft, parsedNewEvolutionSpecies, pokemonSpeciesLabels]
  );
  const parsedNewEvolutionForm = parseEditableIntegerDraft(
    newEvolutionFormDraft,
    newEvolutionFormOptions
  );
  const parsedNewEvolutionLevel = Number.parseInt(newEvolutionLevelDraft, 10);
  const parsedLearnsetMoveId = parseEditableIntegerDraft(
    learnsetMoveIdDraft,
    learnsetMoveOptionsForDraft
  );
  const parsedLearnsetLevel = Number.parseInt(learnsetLevelDraft, 10);
  const parsedNewLearnsetMoveId = parseEditableIntegerDraft(
    newLearnsetMoveIdDraft,
    newLearnsetMoveOptions
  );
  const parsedNewLearnsetLevel = Number.parseInt(newLearnsetLevelDraft, 10);
  const canSaveLearnsetMove =
    canEditLearnset &&
    selectedLearnsetMove !== null &&
    Number.isInteger(parsedLearnsetMoveId) &&
    Number.isInteger(parsedLearnsetLevel);
  const canAddLearnsetMove =
    canEditLearnset &&
    Number.isInteger(parsedNewLearnsetMoveId) &&
    Number.isInteger(parsedNewLearnsetLevel);
  const handleDropLearnsetMove = useCallback(
    (targetSlot: number, sourceSlot = draggedLearnsetSlot) => {
      if (
        !canEditLearnset ||
        !pokemon ||
        sourceSlot === null ||
        !Number.isInteger(sourceSlot) ||
        sourceSlot === targetSlot
      ) {
        setDraggedLearnsetSlot(null);
        setDragOverLearnsetSlot(null);
        return;
      }

      onUpdatePokemonLearnset(
        pokemon.personalId,
        'moveTo',
        sourceSlot,
        targetSlot,
        null
      );
      setSelectedLearnsetSlot(targetSlot);
      setDraggedLearnsetSlot(null);
      setDragOverLearnsetSlot(null);
    },
    [canEditLearnset, draggedLearnsetSlot, onUpdatePokemonLearnset, pokemon]
  );
  const canSaveEvolution =
    canEditEvolution &&
    selectedEvolution !== null &&
    Number.isInteger(parsedEvolutionMethod) &&
    Number.isInteger(parsedEvolutionArgument) &&
    Number.isInteger(parsedEvolutionSpecies) &&
    Number.isInteger(parsedEvolutionForm) &&
    Number.isInteger(parsedEvolutionLevel);
  const canAddEvolution =
    canEditEvolution &&
    Number.isInteger(parsedNewEvolutionMethod) &&
    Number.isInteger(parsedNewEvolutionArgument) &&
    Number.isInteger(parsedNewEvolutionSpecies) &&
    Number.isInteger(parsedNewEvolutionForm) &&
    Number.isInteger(parsedNewEvolutionLevel);

  return (
    <aside aria-label="Selected Pokemon provenance" className="item-inspector pokemon-inspector">
      <div className="pokemon-selected-stack">
        <div className="panel-heading">
          <ShieldCheck aria-hidden="true" size={18} />
          <h3>Selected Pokemon</h3>
        </div>

        {pokemon ? (
          <>
          <div className="pokemon-summary-card">
            <PokemonSprite className="pokemon-summary-sprite" name={pokemon.name} />
            <div>
              <strong>{pokemon.name}</strong>
              <span>
                {pokemon.speciesId} / {pokemon.formLabel}
              </span>
            </div>
          </div>

          <dl className="item-provenance-list">
            <div>
              <dt>Name</dt>
              <dd>{pokemon.name}</dd>
            </div>
            <div>
              <dt>Personal ID</dt>
              <dd>{pokemon.personalId}</dd>
            </div>
            <div>
              <dt>Species / form</dt>
              <dd>
                {pokemon.speciesId} / {pokemon.formLabel}
              </dd>
            </div>
            <div>
              <dt>Types</dt>
              <dd>{formatPokemonTypes(pokemon)}</dd>
            </div>
            <div>
              <dt>Dex</dt>
              <dd>{formatPokemonDexPresence(pokemon)}</dd>
            </div>
            <div>
              <dt>Source file</dt>
              <dd>{pokemon.provenance.sourceFile}</dd>
            </div>
            <div>
              <dt>Layer</dt>
              <dd>{formatSourceLayer(pokemon.provenance.sourceLayer)}</dd>
            </div>
            <div>
              <dt>File state</dt>
              <dd>{formatFileState(pokemon.provenance.fileState)}</dd>
            </div>
          </dl>

          <div className="inspector-block pokemon-stats-block">
            <h4>Base Stats</h4>
            <dl className="item-provenance-list compact-dl">
              <div>
                <dt>HP</dt>
                <dd>{pokemon.baseStats.hp}</dd>
              </div>
              <div>
                <dt>Attack</dt>
                <dd>{pokemon.baseStats.attack}</dd>
              </div>
              <div>
                <dt>Defense</dt>
                <dd>{pokemon.baseStats.defense}</dd>
              </div>
              <div>
                <dt>Sp. Atk</dt>
                <dd>{pokemon.baseStats.specialAttack}</dd>
              </div>
              <div>
                <dt>Sp. Def</dt>
                <dd>{pokemon.baseStats.specialDefense}</dd>
              </div>
              <div>
                <dt>Speed</dt>
                <dd>{pokemon.baseStats.speed}</dd>
              </div>
              <div>
                <dt>Total</dt>
                <dd>{pokemon.baseStats.total}</dd>
              </div>
            </dl>
          </div>

          <div className="inspector-block pokemon-traits-block">
            <h4>Traits</h4>
            <dl className="item-provenance-list compact-dl">
              <div>
                <dt>Ability 1</dt>
                <dd>{pokemon.abilities.ability1Label}</dd>
              </div>
              <div>
                <dt>Ability 2</dt>
                <dd>{pokemon.abilities.ability2Label}</dd>
              </div>
              <div>
                <dt>Hidden</dt>
                <dd>{pokemon.abilities.hiddenAbilityLabel}</dd>
              </div>
              <div>
                <dt>Catch rate</dt>
                <dd>{pokemon.catchRate}</dd>
              </div>
              <div>
                <dt>Base EXP</dt>
                <dd>{pokemon.baseExperience}</dd>
              </div>
              <div>
                <dt>Gender</dt>
                <dd>{pokemon.genderRatioLabel}</dd>
              </div>
              <div>
                <dt>Height / weight</dt>
                <dd>
                  {pokemon.height} / {pokemon.weight}
                </dd>
              </div>
            </dl>
          </div>

          <div className="inspector-block pokemon-personal-edit-block">
            <h4>Personal Edit</h4>
            <div className="editable-field-groups">
              {personalFieldGroups.map((group) => (
                <fieldset className="editable-field-group" key={group.group}>
                  <legend>{group.group}</legend>
                  <div className="editable-field-grid">
                    {group.fields.map((field) => {
                      const currentValue = pokemon
                        ? getEditablePersonalFieldValue(pokemon, field.field)
                        : null;
                      const draftValue = personalDrafts[field.field] ?? '';
                      const draftState = getPokemonPersonalFieldDraftState(
                        draftValue,
                        currentValue,
                        field
                      );

                      return (
                        <PokemonPersonalFieldInput
                          currentValue={currentValue}
                          disabled={
                            !canEditPokemon ||
                            editSession === null ||
                            isPokemonUpdating ||
                            (field.field === formFieldName && pokemon.personal.formCount <= 1)
                          }
                          disabledReason={
                            field.field === formFieldName && pokemon.personal.formCount <= 1
                              ? 'No alternate forms available for this Pokemon.'
                              : undefined
                          }
                          draftState={draftState}
                          draftValue={draftValue}
                          field={field}
                          key={field.field}
                          onChange={(value) =>
                            setPersonalDrafts((currentDrafts) => ({
                              ...currentDrafts,
                              [field.field]: value
                            }))
                          }
                        />
                      );
                    })}
                  </div>
                </fieldset>
              ))}
            </div>
            {editSession ? (
              <div className="draft-action-row">
                <button
                  className="primary-button"
                  disabled={!canSavePersonalDrafts}
                  onClick={() => {
                    if (pokemon) {
                      onUpdatePokemonFields(
                        pokemon.personalId,
                        personalDraftSummary.changedFields.map((change) => ({
                          field: change.field,
                          value: change.value
                        }))
                      );
                    }
                  }}
                  type="button"
                >
                  <Save aria-hidden="true" size={16} />
                  <span>{isPokemonUpdating ? 'Saving' : 'Save Changes'}</span>
                </button>
                <button
                  className="danger-button"
                  disabled={isPokemonUpdating}
                  onClick={() => {
                    setPersonalDrafts(personalDraftDefaults);
                    cancelActiveEditSession();
                  }}
                  type="button"
                >
                  <X aria-hidden="true" size={16} />
                  <span>Cancel</span>
                </button>
                <span className="draft-action-summary">
                  {personalDraftSummary.invalidFields.length > 0
                    ? `${personalDraftSummary.invalidFields.length} field${
                        personalDraftSummary.invalidFields.length === 1 ? '' : 's'
                      } need valid values`
                    : `${personalDraftSummary.changedFields.length} changed`}
                </span>
              </div>
            ) : null}
            {canEditPokemon && editSession === null ? (
              <button
                className="secondary-button"
                disabled={isEditStarting}
                onClick={onStartEditSession}
                type="button"
              >
                <Pencil aria-hidden="true" size={16} />
                <span>{isEditStarting ? 'Starting' : 'Edit'}</span>
              </button>
            ) : null}
          </div>
          </>
        ) : (
          <p className="empty-copy">No Pokemon selected.</p>
        )}
      </div>

      {pokemon ? (
        <>
          <div className="inspector-block pokemon-compatibility-block">
            <h4>Compatibility</h4>
            {pokemon.compatibility.length > 0 ? (
              <div className="compatibility-editor">
                <div className="compatibility-controls">
                  <label className="path-field">
                    <span>Compatibility group</span>
                    <select
                      onChange={(event) => setSelectedCompatibilityGroupId(event.target.value)}
                      value={selectedCompatibilityGroup?.groupId ?? ''}
                    >
                      {pokemon.compatibility.map((group) => (
                        <option key={group.groupId} value={group.groupId}>
                          {group.label} ({group.enabledCount}/{group.entries.length})
                        </option>
                      ))}
                    </select>
                  </label>
                  <label className="search-box compatibility-search">
                    <Search aria-hidden="true" size={16} />
                    <input
                      aria-label="Search compatibility"
                      onChange={(event) => setCompatibilitySearchText(event.target.value)}
                      placeholder="Search compatibility"
                      type="search"
                      value={compatibilitySearchText}
                    />
                  </label>
                </div>
                <ul className="compatibility-list">
                  {filteredCompatibilityEntries.map((entry) => (
                    <li key={`${selectedCompatibilityGroup?.groupId}-${entry.slot}`}>
                      <label className="compatibility-toggle">
                        <input
                          checked={entry.canLearn}
                          disabled={!canToggleCompatibility}
                          onChange={(event) => {
                            if (pokemon && selectedCompatibilityGroup) {
                              onUpdatePokemonField(
                                pokemon.personalId,
                                createPokemonCompatibilityFieldName(
                                  selectedCompatibilityGroup.groupId,
                                  entry.slot
                                ),
                                event.target.checked ? '1' : '0'
                              );
                            }
                          }}
                          type="checkbox"
                        />
                        <span>{entry.label}</span>
                        <small>{entry.moveId}</small>
                      </label>
                    </li>
                  ))}
                </ul>
                {filteredCompatibilityEntries.length === 0 ? (
                  <p className="empty-copy">No compatibility entries matched.</p>
                ) : null}
              </div>
            ) : (
              <p className="empty-copy">No compatibility data.</p>
            )}
          </div>

          <div className="inspector-block pokemon-evolutions-block">
            <h4>Evolutions</h4>
            <div className="learnset-editor">
              {pokemon.evolutions.length > 0 ? (
                <ul className="learnset-list">
                  {pokemon.evolutions.map((evolution) => {
                    const evolutionSpeciesLabel = formatReferenceLabel(
                      pokemonSpeciesLabels,
                      evolution.species,
                      'Species'
                    );
                    const evolutionFormLabel = formatEvolutionFormReference(
                      pokemonSpeciesLabels,
                      evolution.species,
                      evolution.form
                    );

                    return (
                      <li key={evolution.slot}>
                        <button
                          className={`learnset-row evolution-row ${
                            selectedEvolution?.slot === evolution.slot
                              ? 'learnset-row-selected'
                              : ''
                          }`}
                          onClick={() => setSelectedEvolutionSlot(evolution.slot)}
                          type="button"
                        >
                          <PokemonSprite
                            className="pokemon-row-sprite"
                            name={getReferenceSpriteName(evolutionSpeciesLabel)}
                            preferStatic
                          />
                          <span>#{evolution.slot + 1}</span>
                          <span>{formatEvolutionMethodSummary(evolution)}</span>
                          <strong>{evolutionSpeciesLabel}</strong>
                          <span>{evolutionFormLabel}</span>
                          <span>Lv. {evolution.level}</span>
                          <span>{formatEvolutionArgumentSummary(evolution)}</span>
                        </button>
                      </li>
                    );
                  })}
                </ul>
              ) : (
                <p className="empty-copy">No evolution entries.</p>
              )}

              {selectedEvolution ? (
                <div className="learnset-edit-grid evolution-edit-grid">
                  <label className="path-field">
                    <span>Method</span>
                    <SearchableOptionInput
                      ariaLabel="Method"
                      disabled={!canEditEvolution}
                      onChange={(nextMethod) => {
                        const nextOption = findEvolutionMethodOption(
                          selectedEvolutionMethodOptions,
                          nextMethod
                        );
                        setEvolutionMethodDraft(nextMethod);
                        setEvolutionArgumentDraft(getDefaultEvolutionArgumentDraft(nextOption));
                      }}
                      options={selectedEvolutionMethodOptions}
                      value={evolutionMethodDraft}
                    />
                  </label>
                  <label className="path-field">
                    <span>{selectedEvolutionMethodOption?.argumentLabel ?? 'Argument'}</span>
                    {usesEvolutionArgumentSelector(selectedEvolutionMethodOption) &&
                    selectedEvolutionArgumentOptions.length > 0 ? (
                      <SearchableOptionInput
                        ariaLabel={selectedEvolutionMethodOption?.argumentLabel ?? 'Argument'}
                        disabled={!canEditEvolution}
                        onChange={setEvolutionArgumentDraft}
                        options={selectedEvolutionArgumentOptions}
                        value={evolutionArgumentDraft}
                      />
                    ) : (
                      <input
                        disabled={
                          !canEditEvolution ||
                          !usesEvolutionArgumentNumberInput(selectedEvolutionMethodOption)
                        }
                        max={65535}
                        min={0}
                        onChange={(event) => setEvolutionArgumentDraft(event.target.value)}
                        type="number"
                        value={evolutionArgumentDraft}
                      />
                    )}
                  </label>
                  <label className="path-field">
                    <span>Species</span>
                    {pokemonSpeciesOptions.length > 0 ? (
                    <SearchableOptionInput
                        ariaLabel="Species"
                        disabled={!canEditEvolution}
                        onChange={setEvolutionSpeciesDraft}
                        options={addCurrentPokemonFieldOption(
                          pokemonSpeciesOptions,
                          evolutionSpeciesDraft,
                          'Species'
                        )}
                        value={evolutionSpeciesDraft}
                      />
                    ) : (
                      <input
                        disabled={!canEditEvolution}
                        max={65535}
                        min={0}
                        onChange={(event) => setEvolutionSpeciesDraft(event.target.value)}
                        type="number"
                        value={evolutionSpeciesDraft}
                      />
                    )}
                  </label>
                  <label className="path-field">
                    <span>Form</span>
                    <SearchableOptionInput
                      ariaLabel="Form"
                      disabled={!canEditEvolution}
                      onChange={setEvolutionFormDraft}
                      options={selectedEvolutionFormOptions}
                      value={evolutionFormDraft}
                    />
                  </label>
                  <label className="path-field">
                    <span>Level</span>
                    <input
                      disabled={!canEditEvolution}
                      max={255}
                      min={0}
                      onChange={(event) => setEvolutionLevelDraft(event.target.value)}
                      type="number"
                      value={evolutionLevelDraft}
                    />
                  </label>
                  <div className="learnset-button-row">
                    <button
                      aria-label="Save evolution row"
                      className="secondary-button icon-button"
                      disabled={!canSaveEvolution}
                      onClick={() =>
                        onUpdatePokemonEvolution(
                          pokemon.personalId,
                          'upsert',
                          selectedEvolution.slot,
                          parsedEvolutionMethod,
                          parsedEvolutionArgument,
                          parsedEvolutionSpecies,
                          parsedEvolutionForm,
                          parsedEvolutionLevel
                        )
                      }
                      title="Save evolution row"
                      type="button"
                    >
                      <Save aria-hidden="true" size={16} />
                    </button>
                    <button
                      aria-label="Move evolution row up"
                      className="secondary-button icon-button"
                      disabled={!canEditEvolution || selectedEvolution.slot === 0}
                      onClick={() =>
                        onUpdatePokemonEvolution(
                          pokemon.personalId,
                          'moveUp',
                          selectedEvolution.slot,
                          null,
                          null,
                          null,
                          null,
                          null
                        )
                      }
                      title="Move evolution row up"
                      type="button"
                    >
                      <ArrowUp aria-hidden="true" size={16} />
                    </button>
                    <button
                      aria-label="Move evolution row down"
                      className="secondary-button icon-button"
                      disabled={
                        !canEditEvolution || selectedEvolution.slot >= pokemon.evolutions.length - 1
                      }
                      onClick={() =>
                        onUpdatePokemonEvolution(
                          pokemon.personalId,
                          'moveDown',
                          selectedEvolution.slot,
                          null,
                          null,
                          null,
                          null,
                          null
                        )
                      }
                      title="Move evolution row down"
                      type="button"
                    >
                      <ArrowDown aria-hidden="true" size={16} />
                    </button>
                    <button
                      aria-label="Remove evolution row"
                      className="secondary-button icon-button danger-icon-button"
                      disabled={!canEditEvolution}
                      onClick={() =>
                        onUpdatePokemonEvolution(
                          pokemon.personalId,
                          'remove',
                          selectedEvolution.slot,
                          null,
                          null,
                          null,
                          null,
                          null
                        )
                      }
                      title="Remove evolution row"
                      type="button"
                    >
                      <Trash2 aria-hidden="true" size={16} />
                    </button>
                  </div>
                </div>
              ) : null}

              <div className="learnset-edit-grid evolution-edit-grid">
                <label className="path-field">
                  <span>New method</span>
                  <SearchableOptionInput
                    ariaLabel="New method"
                    disabled={!canEditEvolution}
                    onChange={(nextMethod) => {
                      const nextOption = findEvolutionMethodOption(
                        newEvolutionMethodOptions,
                        nextMethod
                      );
                      setNewEvolutionMethodDraft(nextMethod);
                      setNewEvolutionArgumentDraft(getDefaultEvolutionArgumentDraft(nextOption));
                    }}
                    options={newEvolutionMethodOptions}
                    value={newEvolutionMethodDraft}
                  />
                </label>
                <label className="path-field">
                  <span>{newEvolutionMethodOption?.argumentLabel ?? 'New argument'}</span>
                  {usesEvolutionArgumentSelector(newEvolutionMethodOption) &&
                  newEvolutionArgumentOptions.length > 0 ? (
                    <SearchableOptionInput
                      ariaLabel={newEvolutionMethodOption?.argumentLabel ?? 'New argument'}
                      disabled={!canEditEvolution}
                      onChange={setNewEvolutionArgumentDraft}
                      options={newEvolutionArgumentOptions}
                      value={newEvolutionArgumentDraft}
                    />
                  ) : (
                    <input
                      disabled={
                        !canEditEvolution ||
                        !usesEvolutionArgumentNumberInput(newEvolutionMethodOption)
                      }
                      max={65535}
                      min={0}
                      onChange={(event) => setNewEvolutionArgumentDraft(event.target.value)}
                      type="number"
                      value={newEvolutionArgumentDraft}
                    />
                  )}
                </label>
                <label className="path-field">
                  <span>New species</span>
                  {pokemonSpeciesOptions.length > 0 ? (
                    <SearchableOptionInput
                      ariaLabel="New species"
                      disabled={!canEditEvolution}
                      onChange={setNewEvolutionSpeciesDraft}
                      options={addCurrentPokemonFieldOption(
                        pokemonSpeciesOptions,
                        newEvolutionSpeciesDraft,
                        'Species'
                      )}
                      value={newEvolutionSpeciesDraft}
                    />
                  ) : (
                    <input
                      disabled={!canEditEvolution}
                      max={65535}
                      min={0}
                      onChange={(event) => setNewEvolutionSpeciesDraft(event.target.value)}
                      type="number"
                      value={newEvolutionSpeciesDraft}
                    />
                  )}
                </label>
                <label className="path-field">
                  <span>New form</span>
                  <SearchableOptionInput
                    ariaLabel="New form"
                    disabled={!canEditEvolution}
                    onChange={setNewEvolutionFormDraft}
                    options={newEvolutionFormOptions}
                    value={newEvolutionFormDraft}
                  />
                </label>
                <label className="path-field">
                  <span>New level</span>
                  <input
                    disabled={!canEditEvolution}
                    max={255}
                    min={0}
                    onChange={(event) => setNewEvolutionLevelDraft(event.target.value)}
                    type="number"
                    value={newEvolutionLevelDraft}
                  />
                </label>
                <button
                  aria-label="Add evolution row"
                  className="secondary-button learnset-add-button"
                  disabled={!canAddEvolution}
                  onClick={() => {
                    onUpdatePokemonEvolution(
                      pokemon.personalId,
                      'add',
                      null,
                      parsedNewEvolutionMethod,
                      parsedNewEvolutionArgument,
                      parsedNewEvolutionSpecies,
                      parsedNewEvolutionForm,
                      parsedNewEvolutionLevel
                    );
                    setNewEvolutionMethodDraft('');
                    setNewEvolutionArgumentDraft('0');
                    setNewEvolutionSpeciesDraft('');
                    setNewEvolutionFormDraft('0');
                    setNewEvolutionLevelDraft('');
                  }}
                  type="button"
                >
                  <Plus aria-hidden="true" size={16} />
                  <span>Add Row</span>
                </button>
              </div>
            </div>
          </div>

          <div className="inspector-block pokemon-learnset-block">
            <h4>Learnset</h4>
            <div className="learnset-editor">
              {pokemon.learnset.length > 0 ? (
                <ul className="learnset-list">
                  {pokemon.learnset.map((move) => {
                    const isSelected = selectedLearnsetMove?.slot === move.slot;

                    return (
                      <li
                        className={`learnset-list-item ${
                          draggedLearnsetSlot === move.slot ? 'learnset-dragging' : ''
                        } ${
                          dragOverLearnsetSlot === move.slot ? 'learnset-drop-target' : ''
                        }`}
                        draggable={canEditLearnset}
                        key={move.slot}
                        onDragEnd={() => {
                          setDraggedLearnsetSlot(null);
                          setDragOverLearnsetSlot(null);
                        }}
                        onDragLeave={() => {
                          setDragOverLearnsetSlot((currentSlot) =>
                            currentSlot === move.slot ? null : currentSlot
                          );
                        }}
                        onDragOver={(event) => {
                          const transferredSlot = Number.parseInt(
                            event.dataTransfer.getData('text/plain'),
                            10
                          );
                          const sourceSlot =
                            draggedLearnsetSlot ??
                            (Number.isInteger(transferredSlot) ? transferredSlot : null);
                          if (
                            !canEditLearnset ||
                            sourceSlot === null ||
                            sourceSlot === move.slot
                          ) {
                            return;
                          }

                          event.preventDefault();
                          event.dataTransfer.dropEffect = 'move';
                          setDragOverLearnsetSlot(move.slot);
                        }}
                        onDragStart={(event) => {
                          if (!canEditLearnset) {
                            event.preventDefault();
                            return;
                          }

                          event.dataTransfer.effectAllowed = 'move';
                          event.dataTransfer.setData('text/plain', move.slot.toString());
                          setDraggedLearnsetSlot(move.slot);
                          setDragOverLearnsetSlot(null);
                          setSelectedLearnsetSlot(move.slot);
                        }}
                        onDrop={(event) => {
                          const transferredSlot = Number.parseInt(
                            event.dataTransfer.getData('text/plain'),
                            10
                          );
                          const sourceSlot =
                            draggedLearnsetSlot ??
                            (Number.isInteger(transferredSlot) ? transferredSlot : null);
                          event.preventDefault();
                          handleDropLearnsetMove(move.slot, sourceSlot);
                        }}
                      >
                        {isSelected && editSession !== null ? (
                          <div className="learnset-row learnset-inline-row">
                            <span className="learnset-drag-cell" aria-hidden="true">
                              <GripVertical size={15} />
                            </span>
                            <span>#{move.slot + 1}</span>
                            <label className="path-field learnset-inline-field">
                              <span>Move</span>
                              {learnsetMoveOptionsForDraft.length > 0 ? (
                                <SearchableOptionInput
                                  ariaLabel="Move"
                                  disabled={!canEditLearnset}
                                  onChange={setLearnsetMoveIdDraft}
                                  options={learnsetMoveOptionsForDraft}
                                  value={learnsetMoveIdDraft}
                                />
                              ) : (
                                <input
                                  disabled={!canEditLearnset}
                                  min={0}
                                  onChange={(event) => setLearnsetMoveIdDraft(event.target.value)}
                                  type="number"
                                  value={learnsetMoveIdDraft}
                                />
                              )}
                            </label>
                            <label className="path-field learnset-inline-field">
                              <span>Level</span>
                              <input
                                disabled={!canEditLearnset}
                                min={0}
                                onChange={(event) => setLearnsetLevelDraft(event.target.value)}
                                type="number"
                                value={learnsetLevelDraft}
                              />
                            </label>
                            <span>{move.moveId}</span>
                            <div className="learnset-inline-actions">
                              <button
                                aria-label="Save learnset row"
                                className="secondary-button icon-button"
                                disabled={!canSaveLearnsetMove}
                                onClick={() =>
                                  onUpdatePokemonLearnset(
                                    pokemon.personalId,
                                    'upsert',
                                    move.slot,
                                    parsedLearnsetMoveId,
                                    parsedLearnsetLevel
                                  )
                                }
                                title="Save learnset row"
                                type="button"
                              >
                                <Save aria-hidden="true" size={16} />
                              </button>
                              <button
                                aria-label="Move learnset row up"
                                className="secondary-button icon-button"
                                disabled={!canEditLearnset || move.slot === 0}
                                onClick={() =>
                                  onUpdatePokemonLearnset(
                                    pokemon.personalId,
                                    'moveUp',
                                    move.slot,
                                    null,
                                    null
                                  )
                                }
                                title="Move learnset row up"
                                type="button"
                              >
                                <ArrowUp aria-hidden="true" size={16} />
                              </button>
                              <button
                                aria-label="Move learnset row down"
                                className="secondary-button icon-button"
                                disabled={!canEditLearnset || move.slot >= pokemon.learnset.length - 1}
                                onClick={() =>
                                  onUpdatePokemonLearnset(
                                    pokemon.personalId,
                                    'moveDown',
                                    move.slot,
                                    null,
                                    null
                                  )
                                }
                                title="Move learnset row down"
                                type="button"
                              >
                                <ArrowDown aria-hidden="true" size={16} />
                              </button>
                              <button
                                aria-label="Remove learnset row"
                                className="secondary-button icon-button danger-icon-button"
                                disabled={!canEditLearnset}
                                onClick={() =>
                                  onUpdatePokemonLearnset(
                                    pokemon.personalId,
                                    'remove',
                                    move.slot,
                                    null,
                                    null
                                  )
                                }
                                title="Remove learnset row"
                                type="button"
                              >
                                <Trash2 aria-hidden="true" size={16} />
                              </button>
                            </div>
                          </div>
                        ) : (
                          <button
                            className={`learnset-row ${
                              isSelected ? 'learnset-row-selected' : ''
                            }`}
                            onClick={() => setSelectedLearnsetSlot(move.slot)}
                            type="button"
                          >
                            <span className="learnset-drag-cell" aria-hidden="true">
                              <GripVertical size={15} />
                            </span>
                            <span>#{move.slot + 1}</span>
                            <span>Lv. {move.level}</span>
                            <strong>{move.moveName}</strong>
                            <span>{move.moveId}</span>
                          </button>
                        )}
                      </li>
                    );
                  })}
                </ul>
              ) : (
                <p className="empty-copy">No level-up moves.</p>
              )}

              <div className="learnset-edit-grid">
                <label className="path-field">
                  <span>New move</span>
                  {newLearnsetMoveOptions.length > 0 ? (
                    <SearchableOptionInput
                      ariaLabel="New move"
                      disabled={!canEditLearnset}
                      onChange={setNewLearnsetMoveIdDraft}
                      options={newLearnsetMoveOptions}
                      value={newLearnsetMoveIdDraft}
                    />
                  ) : (
                    <input
                      disabled={!canEditLearnset}
                      min={0}
                      onChange={(event) => setNewLearnsetMoveIdDraft(event.target.value)}
                      type="number"
                      value={newLearnsetMoveIdDraft}
                    />
                  )}
                </label>
                <label className="path-field">
                  <span>New level</span>
                  <input
                    disabled={!canEditLearnset}
                    min={0}
                    onChange={(event) => setNewLearnsetLevelDraft(event.target.value)}
                    type="number"
                    value={newLearnsetLevelDraft}
                  />
                </label>
                <button
                  aria-label="Add learnset row"
                  className="secondary-button learnset-add-button"
                  disabled={!canAddLearnsetMove}
                  onClick={() => {
                    onUpdatePokemonLearnset(
                      pokemon.personalId,
                      'add',
                      null,
                      parsedNewLearnsetMoveId,
                      parsedNewLearnsetLevel
                    );
                    setNewLearnsetMoveIdDraft('');
                    setNewLearnsetLevelDraft('');
                  }}
                  type="button"
                >
                  <Plus aria-hidden="true" size={16} />
                  <span>Add Row</span>
                </button>
              </div>
            </div>
          </div>
        </>
      ) : null}
    </aside>
  );
}

function MovesSection({
  editSession,
  isEditStarting,
  isMoveUpdating,
  onSearchChange,
  onSelectMove,
  onStartEditSession,
  onUpdateMoveField,
  onUpdateMoveFields,
  searchText,
  selectedMoveId,
  workflow
}: {
  editSession: EditSession | null;
  isEditStarting: boolean;
  isMoveUpdating: boolean;
  onSearchChange: (searchText: string) => void;
  onSelectMove: (moveId: number | null) => void;
  onStartEditSession: () => void;
  onUpdateMoveField: (moveId: number, field: string, value: string) => void;
  onUpdateMoveFields: (moveId: number, changes: Array<{ field: string; value: string }>) => void;
  searchText: string;
  selectedMoveId: number | null;
  workflow: MovesWorkflow | null;
}) {
  const moves = workflow?.moves ?? [];
  const filteredMoves = useMemo(() => filterMoves(moves, searchText), [moves, searchText]);
  const selectedMove = useMemo(
    () =>
      filteredMoves.find((candidate) => candidate.moveId === selectedMoveId) ??
      filteredMoves[0] ??
      null,
    [filteredMoves, selectedMoveId]
  );
  const canEditMoves = workflow?.summary.availability === 'available';
  const pendingMoveIds = useMemo(() => getPendingMoveIds(editSession), [editSession]);

  return (
    <>
      <section aria-labelledby="moves-heading" className="panel wide-panel">
        <div className="panel-heading">
          <Zap aria-hidden="true" size={18} />
          <h2 id="moves-heading">Moves Data</h2>
        </div>

        <div className="items-toolbar moves-toolbar">
          <label className="search-box items-search">
            <Search aria-hidden="true" size={18} />
            <input
              aria-label="Search moves"
              disabled={!workflow}
              onChange={(event) => onSearchChange(event.target.value)}
              placeholder="Search moves"
              type="search"
              value={searchText}
            />
          </label>
          <Metric
            label="Loaded records"
            value={workflow ? workflow.stats.totalMoveCount.toString() : '0'}
          />
          <Metric
            label="Enabled"
            value={workflow ? workflow.stats.enabledMoveCount.toString() : '0'}
          />
          <Metric
            label="Active flags"
            value={workflow ? workflow.stats.activeFlagCount.toString() : '0'}
          />
        </div>

        {workflow ? (
          <div className="items-layout moves-layout">
            <div
              aria-colcount={8}
              aria-label="Moves Data"
              aria-rowcount={filteredMoves.length + 1}
              className="moves-table"
              role="table"
            >
              <div className="moves-row moves-row-heading" role="row">
                <span role="columnheader">ID</span>
                <span role="columnheader">Move</span>
                <span role="columnheader">Type</span>
                <span role="columnheader">Category</span>
                <span role="columnheader">Power</span>
                <span role="columnheader">Acc</span>
                <span role="columnheader">PP</span>
                <span role="columnheader">Flags</span>
              </div>
              <VirtualTableBody
                getKey={(move) => move.moveId}
                items={filteredMoves}
                renderRow={(move) => (
                  <button
                    className={`moves-row ${
                      selectedMove?.moveId === move.moveId ? 'moves-row-selected' : ''
                    } ${
                      pendingMoveIds.has(move.moveId) ? 'moves-row-pending' : ''
                    }`}
                    onClick={() => onSelectMove(move.moveId)}
                    role="row"
                    type="button"
                  >
                    <span role="cell">{move.moveId}</span>
                    <span role="cell">{move.name}</span>
                    <span role="cell">{move.typeName}</span>
                    <span role="cell">{move.categoryName}</span>
                    <span role="cell">{formatMovePower(move.power)}</span>
                    <span role="cell">{formatMoveAccuracy(move.accuracy)}</span>
                    <span role="cell">{move.pp}</span>
                    <span role="cell">{formatMoveActiveFlags(move)}</span>
                  </button>
                )}
              />
            </div>

            <SelectedMovePanel
              canEditMoves={canEditMoves}
              editSession={editSession}
              editableFields={workflow.editableFields}
              isEditStarting={isEditStarting}
              isMoveUpdating={isMoveUpdating}
              move={selectedMove}
              onStartEditSession={onStartEditSession}
              onUpdateMoveField={onUpdateMoveField}
              onUpdateMoveFields={onUpdateMoveFields}
            />
          </div>
        ) : (
          <p className="empty-copy">Open Moves Data from Workflows to load backend move data.</p>
        )}
      </section>

      <DiagnosticsSection diagnostics={workflow?.diagnostics ?? []} />
    </>
  );
}

function SelectedMovePanel({
  canEditMoves,
  editSession,
  editableFields,
  isEditStarting,
  isMoveUpdating,
  move,
  onStartEditSession,
  onUpdateMoveField,
  onUpdateMoveFields
}: {
  canEditMoves: boolean;
  editSession: EditSession | null;
  editableFields: MoveEditableField[];
  isEditStarting: boolean;
  isMoveUpdating: boolean;
  move: MoveRecord | null;
  onStartEditSession: () => void;
  onUpdateMoveField: (moveId: number, field: string, value: string) => void;
  onUpdateMoveFields: (moveId: number, changes: Array<{ field: string; value: string }>) => void;
}) {
  const [moveDrafts, setMoveDrafts] = useState<Record<string, string>>({});
  const cancelActiveEditSession = useCancelActiveEditSession();
  const moveFields = useMemo(
    () => editableFields.map((field) => toNumericEditableField(field)),
    [editableFields]
  );
  const moveFieldGroups = useMemo(
    () => groupNumericEditableFields(moveFields, getMoveEditableFieldGroup),
    [moveFields]
  );
  const moveDraftSummary = useMemo(
    () =>
      getTrainerDraftSummary(
        moveFields,
        moveDrafts,
        move ? (field) => getEditableMoveFieldValue(move, field) : null
      ),
    [move, moveDrafts, moveFields]
  );
  const activeFlags = move?.flags.filter((flag) => flag.enabled) ?? [];
  const visibleStatChanges =
    move?.statChanges.filter(
      (statChange) => statChange.stat !== 0 || statChange.stage !== 0 || statChange.percent !== 0
    ) ?? [];
  const canSaveMoveDrafts =
    move !== null &&
    editSession !== null &&
    canEditMoves &&
    !isMoveUpdating &&
    moveDraftSummary.changedFields.length > 0 &&
    moveDraftSummary.invalidFields.length === 0;

  useEffect(() => {
    if (!move) {
      setMoveDrafts({});
      return;
    }

    setMoveDrafts(
      Object.fromEntries(
        moveFields.map((field) => [
          field.field,
          (getEditableMoveFieldValue(move, field.field) ?? '').toString()
        ])
      )
    );
  }, [move, moveFields]);

  return (
    <aside aria-label="Selected move details" className="item-inspector">
      <div className="panel-heading">
        <ShieldCheck aria-hidden="true" size={18} />
        <h3>Selected Move</h3>
      </div>

      {move ? (
        <>
          <dl className="item-provenance-list">
            <div>
              <dt>Name</dt>
              <dd>{move.name}</dd>
            </div>
            <div>
              <dt>Move ID</dt>
              <dd>{move.moveId}</dd>
            </div>
            <div>
              <dt>Status</dt>
              <dd>{move.canUseMove ? 'Enabled' : 'Disabled'}</dd>
            </div>
            <div>
              <dt>Type / category</dt>
              <dd>
                {move.typeName} / {move.categoryName}
              </dd>
            </div>
            <div>
              <dt>Description</dt>
              <dd>{move.description ?? 'No description text.'}</dd>
            </div>
            <div>
              <dt>Source file</dt>
              <dd>{move.provenance.sourceFile}</dd>
            </div>
            <div>
              <dt>Layer</dt>
              <dd>{formatSourceLayer(move.provenance.sourceLayer)}</dd>
            </div>
            <div>
              <dt>File state</dt>
              <dd>{formatFileState(move.provenance.fileState)}</dd>
            </div>
          </dl>

          <div className="item-edit-form move-edit-form">
            {!editSession ? (
              <button
                className="secondary-button"
                disabled={!canEditMoves || isEditStarting}
                onClick={onStartEditSession}
                type="button"
              >
                <Pencil aria-hidden="true" size={16} />
                <span>{isEditStarting ? 'Starting' : 'Edit'}</span>
              </button>
            ) : null}

            <div className="editable-field-groups">
              <fieldset className="editable-field-group">
                <legend>Read-only</legend>
                <div className="editable-field-grid">
                  <label className="path-field editable-field-control editable-field-disabled">
                    <span>Move ID</span>
                    <input disabled readOnly value={move.moveId} />
                  </label>
                  <label className="path-field editable-field-control editable-field-disabled">
                    <span>Version</span>
                    <input disabled readOnly value={move.version} />
                  </label>
                </div>
              </fieldset>
              {moveFieldGroups.map((group) => (
                <fieldset className="editable-field-group" key={group.group}>
                  <legend>{group.group}</legend>
                  <div className="editable-field-grid">
                    {group.fields.map((field) => {
                      const currentValue = getEditableMoveFieldValue(move, field.field);
                      const draftValue = moveDrafts[field.field] ?? '';
                      const draftState = getTrainerFieldDraftState(
                        draftValue,
                        currentValue,
                        field
                      );

                      return (
                        <GiftPokemonDraftField
                          currentValue={currentValue}
                          disabled={!canEditMoves || editSession === null || isMoveUpdating}
                          draftState={draftState}
                          draftValue={draftValue}
                          field={field}
                          idPrefix="move-field"
                          key={field.field}
                          onChange={(value) =>
                            setMoveDrafts((currentDrafts) => ({
                              ...currentDrafts,
                              [field.field]: value
                            }))
                          }
                        />
                      );
                    })}
                  </div>
                </fieldset>
              ))}
            </div>

            {editSession ? (
              <div className="draft-action-row">
                <button
                  className="primary-button"
                  disabled={!canSaveMoveDrafts}
                  onClick={() =>
                    onUpdateMoveFields(
                      move.moveId,
                      moveDraftSummary.changedFields.map((change) => ({
                        field: change.field,
                        value: change.value
                      }))
                    )
                  }
                  type="button"
                >
                  <Save aria-hidden="true" size={16} />
                  <span>{isMoveUpdating ? 'Saving' : 'Save Move'}</span>
                </button>
                <button
                  className="danger-button"
                  disabled={isMoveUpdating}
                  onClick={() => {
                    setMoveDrafts(createTrainerDrafts(moveFields, (field) =>
                      getEditableMoveFieldValue(move, field)
                    ));
                    cancelActiveEditSession();
                  }}
                  type="button"
                >
                  <X aria-hidden="true" size={16} />
                  <span>Cancel</span>
                </button>
                <span className="draft-action-summary">{formatDraftSummary(moveDraftSummary)}</span>
              </div>
            ) : null}
          </div>

          <div className="inspector-block">
            <h4>Core Stats</h4>
            <dl className="item-provenance-list compact-dl">
              <div>
                <dt>Power</dt>
                <dd>{formatMovePower(move.power)}</dd>
              </div>
              <div>
                <dt>Accuracy</dt>
                <dd>{formatMoveAccuracy(move.accuracy)}</dd>
              </div>
              <div>
                <dt>PP</dt>
                <dd>{move.pp}</dd>
              </div>
              <div>
                <dt>Priority</dt>
                <dd>{move.priority}</dd>
              </div>
              <div>
                <dt>Crit stage</dt>
                <dd>{move.critStage}</dd>
              </div>
              <div>
                <dt>Max move</dt>
                <dd>{move.maxMovePower}</dd>
              </div>
            </dl>
          </div>

          <div className="inspector-block">
            <h4>Targeting</h4>
            <dl className="item-provenance-list compact-dl">
              <div>
                <dt>Target</dt>
                <dd>{move.targetName}</dd>
              </div>
              <div>
                <dt>Hits</dt>
                <dd>
                  {move.hitMin}-{move.hitMax}
                </dd>
              </div>
              <div>
                <dt>Turns</dt>
                <dd>
                  {move.turnMin}-{move.turnMax}
                </dd>
              </div>
              <div>
                <dt>Quality</dt>
                <dd>{move.quality}</dd>
              </div>
            </dl>
          </div>

          <div className="inspector-block">
            <h4>Secondary Effects</h4>
            <dl className="item-provenance-list compact-dl">
              <div>
                <dt>Inflict</dt>
                <dd>{move.inflictName}</dd>
              </div>
              <div>
                <dt>Inflict %</dt>
                <dd>{move.inflictPercent}</dd>
              </div>
              <div>
                <dt>Raw count</dt>
                <dd>{move.rawInflictCount}</dd>
              </div>
              <div>
                <dt>Flinch</dt>
                <dd>{move.flinch}</dd>
              </div>
              <div>
                <dt>Recoil</dt>
                <dd>{move.recoil}</dd>
              </div>
              <div>
                <dt>Healing</dt>
                <dd>{move.rawHealing}</dd>
              </div>
              <div>
                <dt>Effect seq</dt>
                <dd>{move.effectSequence}</dd>
              </div>
            </dl>
          </div>

          <div className="inspector-block">
            <h4>Stat Changes</h4>
            {visibleStatChanges.length > 0 ? (
              <ul className="inspector-list">
                {visibleStatChanges.map((statChange) => (
                  <li key={statChange.slot}>
                    {statChange.statName}: {statChange.stage} stage, {statChange.percent}%
                  </li>
                ))}
              </ul>
            ) : (
              <p className="empty-copy">No stat change effects.</p>
            )}
          </div>

          <div className="inspector-block">
            <h4>Active Flags</h4>
            {activeFlags.length > 0 ? (
              <ul className="inspector-list">
                {activeFlags.map((flag) => (
                  <li key={flag.field}>{flag.label}</li>
                ))}
              </ul>
            ) : (
              <p className="empty-copy">No active flags.</p>
            )}
          </div>
        </>
      ) : (
        <p className="empty-copy">No move selected.</p>
      )}
    </aside>
  );
}

function TextSection({
  editSession,
  isEditStarting,
  isTextUpdating,
  onSearchChange,
  onSelectTextEntry,
  onStartEditSession,
  onUpdateTextEntry,
  searchText,
  selectedTextKey,
  workflow
}: {
  editSession: EditSession | null;
  isEditStarting: boolean;
  isTextUpdating: boolean;
  onSearchChange: (searchText: string) => void;
  onSelectTextEntry: (textKey: string | null) => void;
  onStartEditSession: () => void;
  onUpdateTextEntry: (textKey: string, value: string) => void;
  searchText: string;
  selectedTextKey: string | null;
  workflow: TextWorkflow | null;
}) {
  const entries = workflow?.entries ?? [];
  const filteredEntries = useMemo(
    () => filterTextEntries(entries, searchText),
    [entries, searchText]
  );
  const selectedEntry = useMemo(
    () =>
      entries.find((entry) => entry.textKey === selectedTextKey) ?? filteredEntries[0] ?? null,
    [entries, filteredEntries, selectedTextKey]
  );
  const canEditText = workflow?.summary.availability === 'available';
  const pendingTextKeys = useMemo(() => getPendingTextKeys(editSession), [editSession]);

  return (
    <>
      <section aria-labelledby="text-heading" className="panel wide-panel">
        <div className="panel-heading">
          <ListChecks aria-hidden="true" size={18} />
          <h2 id="text-heading">Text and Dialogue Map</h2>
        </div>

        <div className="items-toolbar">
          <label className="search-box items-search">
            <Search aria-hidden="true" size={18} />
            <input
              aria-label="Search text entries"
              disabled={!workflow}
              onChange={(event) => onSearchChange(event.target.value)}
              placeholder="Search text"
              type="search"
              value={searchText}
            />
          </label>
          <Metric
            label="Loaded entries"
            value={workflow ? workflow.stats.totalTextEntryCount.toString() : '0'}
          />
          <Metric
            label="Dialogue refs"
            value={workflow ? workflow.stats.dialogueReferenceCount.toString() : '0'}
          />
          <Metric
            label="Pending changes"
            value={(editSession?.pendingEdits.length ?? 0).toString()}
          />
        </div>

        {workflow ? (
          <div className="text-layout">
            <div
              aria-colcount={5}
              aria-label="Text entries"
              aria-rowcount={filteredEntries.length + 1}
              className="text-table"
              role="table"
            >
              <div className="text-row text-row-heading" role="row">
                <span role="columnheader">ID</span>
                <span role="columnheader">File</span>
                <span role="columnheader">Line</span>
                <span role="columnheader">Value</span>
                <span role="columnheader">Source</span>
              </div>
              <VirtualTableBody
                getKey={(entry) => entry.textKey}
                items={filteredEntries}
                renderRow={(entry) => (
                  <button
                    className={`text-row ${selectedEntry?.textKey === entry.textKey ? 'text-row-selected' : ''} ${
                      pendingTextKeys.has(entry.textKey) ? 'text-row-pending' : ''
                    }`}
                    onClick={() => onSelectTextEntry(entry.textKey)}
                    role="row"
                    type="button"
                  >
                    <span role="cell">{entry.textId}</span>
                    <span role="cell">{entry.sourceFile}</span>
                    <span role="cell">{entry.lineIndex}</span>
                    <span role="cell">{entry.value}</span>
                    <span role="cell">{formatSourceLayer(entry.provenance.sourceLayer)}</span>
                  </button>
                )}
              />
            </div>

            <SelectedTextPanel
              canEditText={canEditText}
              editSession={editSession}
              editableFields={workflow.editableFields}
              entry={selectedEntry}
              isEditStarting={isEditStarting}
              isTextUpdating={isTextUpdating}
              onStartEditSession={onStartEditSession}
              onUpdateTextEntry={onUpdateTextEntry}
            />
          </div>
        ) : (
          <p className="empty-copy">Open Text from Workflows to load backend message tables.</p>
        )}
      </section>

      <DiagnosticsSection diagnostics={workflow?.diagnostics ?? []} />
    </>
  );
}

function SelectedTextPanel({
  canEditText,
  editSession,
  editableFields,
  entry,
  isEditStarting,
  isTextUpdating,
  onStartEditSession,
  onUpdateTextEntry
}: {
  canEditText: boolean;
  editSession: EditSession | null;
  editableFields: TextEditableField[];
  entry: TextEntryRecord | null;
  isEditStarting: boolean;
  isTextUpdating: boolean;
  onStartEditSession: () => void;
  onUpdateTextEntry: (textKey: string, value: string) => void;
}) {
  const [draftValue, setDraftValue] = useState('');
  const valueField = editableFields.find((field) => field.field === 'value');

  useEffect(() => {
    setDraftValue(entry?.value ?? '');
  }, [entry?.textKey, entry?.value]);

  const draftState = getTextDraftState(draftValue, entry, valueField);
  const canSubmit = editSession !== null && draftState.canSubmit;

  return (
    <aside aria-label="Selected text provenance" className="text-inspector">
      <div className="panel-heading">
        <ShieldCheck aria-hidden="true" size={18} />
        <h3>Selected Text</h3>
      </div>

      {entry ? (
        <>
          <dl className="item-provenance-list">
            <div>
              <dt>Label</dt>
              <dd>{entry.label}</dd>
            </div>
            <div>
              <dt>Source file</dt>
              <dd>{entry.sourceFile}</dd>
            </div>
            <div>
              <dt>Line</dt>
              <dd>{entry.lineIndex}</dd>
            </div>
            <div>
              <dt>Layer</dt>
              <dd>{formatSourceLayer(entry.provenance.sourceLayer)}</dd>
            </div>
            <div>
              <dt>File state</dt>
              <dd>{formatFileState(entry.provenance.fileState)}</dd>
            </div>
          </dl>

          <div className="text-edit-form">
            <label className="path-field">
              <span>{valueField?.label ?? 'Text value'}</span>
              <textarea
                aria-label={valueField?.label ?? 'Text value'}
                disabled={!canEditText || editSession === null || isTextUpdating || !entry.canEdit}
                maxLength={valueField?.maximumLength ?? undefined}
                onChange={(event) => setDraftValue(event.target.value)}
                rows={8}
                value={draftValue}
              />
            </label>

            {!entry.canEdit ? (
              <p className="empty-copy">{entry.editBlockedReason ?? 'This text line is read-only.'}</p>
            ) : null}

            {editSession ? (
              <button
                className="primary-button"
                disabled={!canSubmit || isTextUpdating}
                onClick={() => onUpdateTextEntry(entry.textKey, draftValue)}
                type="button"
              >
                <Save aria-hidden="true" size={16} />
                <span>{isTextUpdating ? 'Saving' : 'Save Text'}</span>
              </button>
            ) : (
              <button
                className="secondary-button"
                disabled={!canEditText || isEditStarting || !entry.canEdit}
                onClick={onStartEditSession}
                type="button"
              >
                <Pencil aria-hidden="true" size={16} />
                <span>{isEditStarting ? 'Starting' : 'Edit'}</span>
              </button>
            )}
          </div>
        </>
      ) : (
        <p className="empty-copy">No text entry selected.</p>
      )}
    </aside>
  );
}

function TrainersSection({
  editSession,
  isEditStarting,
  isTrainerUpdating,
  onSearchChange,
  onSelectTrainer,
  onStartEditSession,
  onUpdateTrainerField,
  onUpdateTrainerFields,
  searchText,
  selectedTrainerId,
  workflow
}: {
  editSession: EditSession | null;
  isEditStarting: boolean;
  isTrainerUpdating: boolean;
  onSearchChange: (searchText: string) => void;
  onSelectTrainer: (trainerId: number | null) => void;
  onStartEditSession: () => void;
  onUpdateTrainerField: (
    trainerId: number,
    slot: number | null,
    field: string,
    value: string
  ) => void;
  onUpdateTrainerFields: (
    trainerId: number,
    slot: number | null,
    changes: Array<{ field: string; value: string }>
  ) => void;
  searchText: string;
  selectedTrainerId: number | null;
  workflow: TrainersWorkflow | null;
}) {
  const [selectedSlot, setSelectedSlot] = useState<number | null>(null);
  const trainers = workflow?.trainers ?? [];
  const filteredTrainers = useMemo(
    () => filterTrainers(trainers, searchText),
    [searchText, trainers]
  );
  const selectedTrainer = useMemo(
    () =>
      trainers.find((trainer) => trainer.trainerId === selectedTrainerId) ??
      filteredTrainers[0] ??
      null,
    [filteredTrainers, selectedTrainerId, trainers]
  );
  const selectedPokemon =
    selectedTrainer?.team.find((pokemon) => pokemon.slot === selectedSlot) ??
    selectedTrainer?.team[0] ??
    null;
  const canEditTrainers = workflow?.summary.availability === 'available';
  const pendingTrainerIds = useMemo(() => getPendingTrainerIds(editSession), [editSession]);

  useEffect(() => {
    if (!selectedTrainer) {
      setSelectedSlot(null);
      return;
    }

    const hasSelectedSlot = selectedTrainer.team.some((pokemon) => pokemon.slot === selectedSlot);
    if (!hasSelectedSlot) {
      setSelectedSlot(selectedTrainer.team[0]?.slot ?? null);
    }
  }, [selectedSlot, selectedTrainer?.trainerId, selectedTrainer?.team]);

  return (
    <>
      <section aria-labelledby="trainers-heading" className="panel wide-panel">
        <div className="panel-heading">
          <Activity aria-hidden="true" size={18} />
          <h2 id="trainers-heading">Trainers</h2>
        </div>

        <div className="items-toolbar trainers-toolbar">
          <label className="search-box items-search">
            <Search aria-hidden="true" size={18} />
            <input
              aria-label="Search trainers"
              disabled={!workflow}
              onChange={(event) => onSearchChange(event.target.value)}
              placeholder="Search trainers"
              type="search"
              value={searchText}
            />
          </label>
          <Metric
            label="Loaded trainers"
            value={workflow ? workflow.stats.totalTrainerCount.toString() : '0'}
          />
          <Metric
            label="Party Pokemon"
            value={workflow ? workflow.stats.totalPokemonCount.toString() : '0'}
          />
          <Metric
            label="Pending changes"
            value={(editSession?.pendingEdits.length ?? 0).toString()}
          />
        </div>

        {workflow ? (
          <div className="trainers-layout">
            <div
              aria-colcount={6}
              aria-label="Trainers"
              aria-rowcount={filteredTrainers.length + 1}
              className="trainers-table"
              role="table"
            >
              <div className="trainers-row trainers-row-heading" role="row">
                <span role="columnheader">ID</span>
                <span role="columnheader">Name</span>
                <span role="columnheader">Class</span>
                <span role="columnheader">Battle</span>
                <span role="columnheader">Team</span>
                <span role="columnheader">Source</span>
              </div>
              <VirtualTableBody
                getKey={(trainer) => trainer.trainerId}
                items={filteredTrainers}
                renderRow={(trainer) => (
                  <button
                    className={`trainers-row ${
                      selectedTrainer?.trainerId === trainer.trainerId ? 'trainers-row-selected' : ''
                    } ${pendingTrainerIds.has(trainer.trainerId) ? 'trainers-row-pending' : ''}`}
                    onClick={() => onSelectTrainer(trainer.trainerId)}
                    role="row"
                    type="button"
                  >
                    <span role="cell">{trainer.trainerId}</span>
                    <span role="cell">{trainer.name}</span>
                    <span role="cell">{trainer.trainerClass}</span>
                    <span role="cell">{trainer.battleType}</span>
                    <span role="cell">{trainer.team.length}</span>
                    <span role="cell">{formatSourceLayer(trainer.provenance.sourceLayer)}</span>
                  </button>
                )}
              />
            </div>

            <SelectedTrainerPanel
              canEditTrainers={canEditTrainers}
              editSession={editSession}
              editableFields={workflow.editableFields}
              isEditStarting={isEditStarting}
              isTrainerUpdating={isTrainerUpdating}
              onSelectSlot={setSelectedSlot}
              onStartEditSession={onStartEditSession}
              onUpdateTrainerField={onUpdateTrainerField}
              onUpdateTrainerFields={onUpdateTrainerFields}
              selectedPokemon={selectedPokemon}
              selectedSlot={selectedSlot}
              trainer={selectedTrainer}
            />
          </div>
        ) : (
          <p className="empty-copy">Open Trainers from Workflows to load backend trainer data.</p>
        )}
      </section>

      <DiagnosticsSection diagnostics={workflow?.diagnostics ?? []} />
    </>
  );
}

function SelectedTrainerPanel({
  canEditTrainers,
  editSession,
  editableFields,
  isEditStarting,
  isTrainerUpdating,
  onSelectSlot,
  onStartEditSession,
  onUpdateTrainerField,
  onUpdateTrainerFields,
  selectedPokemon,
  selectedSlot,
  trainer
}: {
  canEditTrainers: boolean;
  editSession: EditSession | null;
  editableFields: TrainerEditableField[];
  isEditStarting: boolean;
  isTrainerUpdating: boolean;
  onSelectSlot: (slot: number | null) => void;
  onStartEditSession: () => void;
  onUpdateTrainerField: (
    trainerId: number,
    slot: number | null,
    field: string,
    value: string
  ) => void;
  onUpdateTrainerFields: (
    trainerId: number,
    slot: number | null,
    changes: Array<{ field: string; value: string }>
  ) => void;
  selectedPokemon: TrainerPokemonRecord | null;
  selectedSlot: number | null;
  trainer: TrainerRecord | null;
}) {
  const [trainerDrafts, setTrainerDrafts] = useState<Record<string, string>>({});
  const [pokemonDrafts, setPokemonDrafts] = useState<Record<string, string>>({});
  const cancelActiveEditSession = useCancelActiveEditSession();
  const trainerFields = useMemo(
    () =>
      editableFields.filter((field) =>
        trainerDataFieldNames.includes(field.field as (typeof trainerDataFieldNames)[number])
      ),
    [editableFields]
  );
  const pokemonFields = useMemo(
    () =>
      editableFields.filter((field) =>
        trainerPokemonFieldNames.includes(field.field as (typeof trainerPokemonFieldNames)[number])
      ),
    [editableFields]
  );
  const contextualPokemonFields = useMemo(
    () =>
      pokemonFields.map((field) => {
        const options = getContextualFieldOptions(
          field,
          selectedPokemon
            ? {
                abilityOptions: selectedPokemon.abilityOptions,
                species: selectedPokemon.species,
                speciesId: selectedPokemon.speciesId
              }
            : undefined
        );

        return options === field.options ? field : { ...field, options };
      }),
    [pokemonFields, selectedPokemon]
  );
  const aiFlagsField = editableFields.find((field) => field.field === aiFlagsFieldName) ?? null;
  const canToggleAiFlags =
    canEditTrainers && editSession !== null && !isTrainerUpdating && aiFlagsField !== null;
  const aiFlagsMaskLabel = trainer
    ? `0x${trainer.aiFlags.toString(16).padStart(4, '0').toLocaleUpperCase()}`
    : '0x0000';
  const trainerFieldGroups = useMemo(
    () => groupTrainerEditableFields(trainerFields, getTrainerDataFieldGroup),
    [trainerFields]
  );
  const pokemonFieldGroups = useMemo(
    () => groupTrainerEditableFields(contextualPokemonFields, getTrainerPokemonFieldGroup),
    [contextualPokemonFields]
  );
  const trainerDraftSummary = useMemo(
    () =>
      getTrainerDraftSummary(
        trainerFields,
        trainerDrafts,
        trainer ? (field) => getEditableTrainerFieldValue(trainer, field) : null
      ),
    [trainer, trainerDrafts, trainerFields]
  );
  const pokemonDraftSummary = useMemo(
    () =>
      getTrainerDraftSummary(
        contextualPokemonFields,
        pokemonDrafts,
        selectedPokemon ? (field) => getEditablePokemonFieldValue(selectedPokemon, field) : null
      ),
    [contextualPokemonFields, pokemonDrafts, selectedPokemon]
  );
  const canSaveTrainerDrafts =
    trainer !== null &&
    editSession !== null &&
    canEditTrainers &&
    !isTrainerUpdating &&
    trainerDraftSummary.changedFields.length > 0 &&
    trainerDraftSummary.invalidFields.length === 0;
  const canSavePokemonDrafts =
    trainer !== null &&
    selectedPokemon !== null &&
    editSession !== null &&
    canEditTrainers &&
    !isTrainerUpdating &&
    pokemonDraftSummary.changedFields.length > 0 &&
    pokemonDraftSummary.invalidFields.length === 0;

  useEffect(() => {
    if (!trainer) {
      setTrainerDrafts({});
      return;
    }

    setTrainerDrafts(
      Object.fromEntries(
        trainerFields.map((field) => [
          field.field,
          (getEditableTrainerFieldValue(trainer, field.field) ?? '').toString()
        ])
      )
    );
  }, [trainer, trainerFields]);

  useEffect(() => {
    if (!selectedPokemon) {
      setPokemonDrafts({});
      return;
    }

    setPokemonDrafts(
      Object.fromEntries(
        contextualPokemonFields.map((field) => [
          field.field,
          (getEditablePokemonFieldValue(selectedPokemon, field.field) ?? '').toString()
        ])
      )
    );
  }, [contextualPokemonFields, selectedPokemon]);

  return (
    <aside aria-label="Selected trainer provenance" className="trainer-inspector">
      <div className="panel-heading">
        <ShieldCheck aria-hidden="true" size={18} />
        <h3>Selected Trainer</h3>
      </div>

      {trainer ? (
        <>
          <dl className="item-provenance-list">
            <div>
              <dt>Name</dt>
              <dd>{trainer.name}</dd>
            </div>
            <div>
              <dt>Data file</dt>
              <dd>{trainer.provenance.sourceFile}</dd>
            </div>
            <div>
              <dt>Party file</dt>
              <dd>{trainer.provenance.teamSourceFile}</dd>
            </div>
            <div>
              <dt>Class file</dt>
              <dd>{trainer.provenance.classSourceFile ?? 'Not loaded'}</dd>
            </div>
            <div>
              <dt>Class ball scope</dt>
              <dd>{trainer.classBallScope}</dd>
            </div>
            <div>
              <dt>Data layer</dt>
              <dd>{formatSourceLayer(trainer.provenance.sourceLayer)}</dd>
            </div>
            <div>
              <dt>Party layer</dt>
              <dd>{formatSourceLayer(trainer.provenance.teamSourceLayer)}</dd>
            </div>
            <div>
              <dt>Class layer</dt>
              <dd>
                {trainer.provenance.classSourceLayer
                  ? formatSourceLayer(trainer.provenance.classSourceLayer)
                  : 'Not loaded'}
              </dd>
            </div>
          </dl>

          <div className="trainer-edit-form">
            {trainer.aiFlagStates.length > 0 ? (
              <div className="trainer-ai-flags-panel">
                <div className="trainer-ai-flags-header">
                  <strong>AI Flags</strong>
                  <span>{aiFlagsMaskLabel}</span>
                </div>
                <div className="trainer-ai-flags-grid">
                  {trainer.aiFlagStates.map((flag) => {
                    const isReserved = flag.label.toLocaleLowerCase().startsWith('unused');

                    return (
                      <label
                        className={`trainer-ai-flag ${
                          isReserved ? 'trainer-ai-flag-disabled' : ''
                        }`}
                        key={flag.bit}
                        title={isReserved ? 'Reserved for later research.' : flag.description}
                      >
                        <input
                          checked={flag.enabled}
                          disabled={!canToggleAiFlags || isReserved}
                          onChange={(event) => {
                            const nextValue = event.target.checked
                              ? trainer.aiFlags | flag.mask
                              : trainer.aiFlags & ~flag.mask;
                            onUpdateTrainerField(
                              trainer.trainerId,
                              null,
                              aiFlagsFieldName,
                              nextValue.toString()
                            );
                          }}
                          type="checkbox"
                        />
                        <span>
                          <strong>{flag.label}</strong>
                          <small>{isReserved ? 'Reserved and locked.' : flag.description}</small>
                        </span>
                      </label>
                    );
                  })}
                </div>
              </div>
            ) : null}

            <div className="editable-field-groups">
              {trainerFieldGroups.map((group) => (
                <fieldset className="editable-field-group" key={group.group}>
                  <legend>{group.group}</legend>
                  <div className="editable-field-grid">
                    {group.fields.map((field) => {
                      const currentValue = getEditableTrainerFieldValue(trainer, field.field);
                      const draftValue = trainerDrafts[field.field] ?? '';
                      const draftState = getTrainerFieldDraftState(draftValue, currentValue, field);
                      const isFieldBlocked =
                        field.field === classBallIdFieldName && !trainer.canEditClassBall;

                      return (
                        <TrainerDraftField
                          currentValue={currentValue}
                          disabled={
                            isFieldBlocked ||
                            !canEditTrainers ||
                            editSession === null ||
                            isTrainerUpdating
                          }
                          draftState={draftState}
                          draftValue={draftValue}
                          field={field}
                          key={field.field}
                          onChange={(value) =>
                            setTrainerDrafts((currentDrafts) => ({
                              ...currentDrafts,
                              [field.field]: value
                            }))
                          }
                        />
                      );
                    })}
                  </div>
                </fieldset>
              ))}
            </div>
            {editSession ? (
              <div className="draft-action-row">
                <button
                  className="primary-button"
                  disabled={!canSaveTrainerDrafts}
                  onClick={() =>
                    onUpdateTrainerFields(
                      trainer.trainerId,
                      null,
                      trainerDraftSummary.changedFields.map((change) => ({
                        field: change.field,
                        value: change.value
                      }))
                    )
                  }
                  type="button"
                >
                  <Save aria-hidden="true" size={16} />
                  <span>{isTrainerUpdating ? 'Saving' : 'Save Trainer'}</span>
                </button>
                <button
                  className="danger-button"
                  disabled={isTrainerUpdating}
                  onClick={() => {
                    setTrainerDrafts(createTrainerDrafts(trainerFields, (field) =>
                      getEditableTrainerFieldValue(trainer, field)
                    ));
                    cancelActiveEditSession();
                  }}
                  type="button"
                >
                  <X aria-hidden="true" size={16} />
                  <span>Cancel</span>
                </button>
                <span className="draft-action-summary">
                  {formatDraftSummary(trainerDraftSummary)}
                </span>
              </div>
            ) : null}

            <div className="trainer-party-header">
              <strong>Party</strong>
            </div>

            {trainer.team.length > 0 ? (
              <div className="trainer-party-card-grid" aria-label="Trainer party Pokemon">
                {trainer.team.map((pokemon) => {
                  const pokemonLabel = formatSpeciesFormLabel(
                    pokemon.species,
                    pokemon.form,
                    pokemon.speciesId
                  );

                  return (
                    <button
                      aria-pressed={selectedSlot === pokemon.slot}
                      className="trainer-party-card"
                      key={pokemon.slot}
                      onClick={() => onSelectSlot(pokemon.slot)}
                      type="button"
                    >
                      <PokemonSprite className="trainer-party-sprite" name={pokemonLabel} />
                      <strong>{pokemonLabel}</strong>
                      <span>Lv. {pokemon.level}</span>
                    </button>
                  );
                })}
              </div>
            ) : null}

            {selectedPokemon ? (
              <div className="trainer-party-edit-stack">
                <div className="editable-field-groups">
                  {pokemonFieldGroups.map((group) => (
                    <fieldset className="editable-field-group" key={group.group}>
                      <legend>{group.group}</legend>
                      <div className="editable-field-grid">
                        {group.fields.map((field) => {
                          const currentValue = getEditablePokemonFieldValue(
                            selectedPokemon,
                            field.field
                          );
                          const draftValue = pokemonDrafts[field.field] ?? '';
                          const draftState = getTrainerFieldDraftState(
                            draftValue,
                            currentValue,
                            field
                          );

                          return (
                            <TrainerDraftField
                              currentValue={currentValue}
                              disabled={
                                !canEditTrainers || editSession === null || isTrainerUpdating
                              }
                              draftState={draftState}
                              draftValue={draftValue}
                              field={field}
                              formOptionContext={{
                                abilityOptions: selectedPokemon.abilityOptions,
                                species: selectedPokemon.species,
                                speciesId: selectedPokemon.speciesId
                              }}
                              key={field.field}
                              onChange={(value) =>
                                setPokemonDrafts((currentDrafts) => ({
                                  ...currentDrafts,
                                  [field.field]: value
                                }))
                              }
                            />
                          );
                        })}
                      </div>
                    </fieldset>
                  ))}
                </div>
                {editSession ? (
                  <div className="draft-action-row">
                    <button
                      className="primary-button"
                      disabled={!canSavePokemonDrafts}
                      onClick={() =>
                        onUpdateTrainerFields(
                          trainer.trainerId,
                          selectedPokemon.slot,
                          pokemonDraftSummary.changedFields.map((change) => ({
                            field: change.field,
                            value: change.value
                          }))
                        )
                      }
                      type="button"
                    >
                      <Save aria-hidden="true" size={16} />
                      <span>{isTrainerUpdating ? 'Saving' : 'Save Pokemon'}</span>
                    </button>
                    <button
                      className="danger-button"
                      disabled={isTrainerUpdating}
                      onClick={() => {
                        setPokemonDrafts(createTrainerDrafts(contextualPokemonFields, (field) =>
                          getEditablePokemonFieldValue(selectedPokemon, field)
                        ));
                        cancelActiveEditSession();
                      }}
                      type="button"
                    >
                      <X aria-hidden="true" size={16} />
                      <span>Cancel</span>
                    </button>
                    <span className="draft-action-summary">
                      {formatDraftSummary(pokemonDraftSummary)}
                    </span>
                  </div>
                ) : null}
              </div>
            ) : (
              <p className="empty-copy">No party Pokemon selected.</p>
            )}

            {!editSession ? (
              <button
                className="secondary-button"
                disabled={!canEditTrainers || isEditStarting}
                onClick={onStartEditSession}
                type="button"
              >
                <Pencil aria-hidden="true" size={16} />
                <span>{isEditStarting ? 'Starting' : 'Edit'}</span>
              </button>
            ) : null}
          </div>
        </>
      ) : (
        <p className="empty-copy">No trainer selected.</p>
      )}
    </aside>
  );
}

type TrainerDraftState = {
  error: string | null;
  isChanged: boolean;
  isValid: boolean;
  normalizedValue: string | null;
};

type TrainerDraftChange = {
  field: string;
  label: string;
  value: string;
};

type NumericEditableField = {
  field: string;
  label: string;
  maximumValue: number | null;
  minimumValue: number | null;
  options: Array<{ label: string; value: number }>;
  valueKind: string;
};

function TrainerDraftField({
  currentValue,
  disabled,
  draftState,
  draftValue,
  field,
  formOptionContext,
  idPrefix = 'trainer-field',
  onChange
}: {
  currentValue: number | null;
  disabled: boolean;
  draftState: TrainerDraftState;
  draftValue: string;
  field: NumericEditableField;
  formOptionContext?: SpeciesFormOptionContext;
  idPrefix?: string;
  onChange: (value: string) => void;
}) {
  const inputId = `${idPrefix}-${field.field}`;
  const options = getContextualFieldOptions(field, formOptionContext);
  const knownFormCount = useWorkbenchStore((state) => {
    if (!formOptionContext?.speciesId) {
      return undefined;
    }

    return state.pokemonWorkflow?.pokemon.find(
      (pokemon) => pokemon.speciesId === formOptionContext.speciesId && pokemon.form === 0
    )?.personal.formCount;
  });
  const disabledReason = getFormFieldDisabledReason(
    field,
    formOptionContext,
    currentValue,
    knownFormCount
  );
  const effectiveDisabled = disabled || Boolean(disabledReason);
  const statusText = draftState.error ?? (draftState.isChanged ? 'Changed' : null);

  return (
    <label
      className={`path-field editable-field-control ${
        draftState.isChanged ? 'editable-field-changed' : ''
      } ${!draftState.isValid ? 'editable-field-invalid' : ''} ${
        disabledReason ? 'editable-field-disabled' : ''
      }`}
      htmlFor={inputId}
      title={disabledReason ?? getEditableFieldHelp(field)}
    >
      <span>{field.label}</span>
      {field.valueKind === 'boolean' ? (
        <select
          aria-label={field.label}
          disabled={effectiveDisabled}
          id={inputId}
          onChange={(event) => onChange(event.target.value)}
          title={disabledReason ?? getEditableFieldHelp(field)}
          value={draftValue === '1' ? '1' : '0'}
        >
          <option value="1">Yes</option>
          <option value="0">No</option>
        </select>
      ) : options.length > 0 ? (
        <SearchableOptionInput
          ariaLabel={field.label}
          disabled={effectiveDisabled}
          id={inputId}
          onChange={onChange}
          options={options}
          title={disabledReason ?? getEditableFieldHelp(field)}
          value={draftValue}
        />
      ) : (
        <input
          aria-label={field.label}
          disabled={effectiveDisabled}
          id={inputId}
          max={field.maximumValue ?? undefined}
          min={field.minimumValue ?? undefined}
          onChange={(event) => onChange(event.target.value)}
          title={disabledReason ?? getEditableFieldHelp(field)}
          type="number"
          value={draftValue}
        />
      )}
      {disabledReason ? (
        <small className="editable-field-status">{disabledReason}</small>
      ) : statusText ? (
        <small className={draftState.error ? 'editable-field-error' : 'editable-field-status'}>
          {statusText}
        </small>
      ) : null}
    </label>
  );
}

function createTrainerDrafts(
  fields: NumericEditableField[],
  getValue: (field: string) => number | null
) {
  return Object.fromEntries(
    fields.map((field) => [field.field, (getValue(field.field) ?? '').toString()])
  );
}

type NumericEditableFieldSource = EditableFieldWithOptions & {
  field: string;
  valueKind: string;
};

function toNumericEditableControlField(
  field: NumericEditableFieldSource,
  optionsOverride?: EditableFieldOption[]
): NumericEditableField {
  return {
    field: field.field,
    label: field.label,
    maximumValue: field.maximumValue ?? null,
    minimumValue: field.minimumValue ?? null,
    options: optionsOverride ?? field.options ?? [],
    valueKind: field.valueKind
  };
}

function groupTrainerEditableFields(
  fields: TrainerEditableField[],
  getGroup: (field: TrainerEditableField) => string
) {
  const groups: Array<{ group: string; fields: TrainerEditableField[] }> = [];

  for (const field of fields) {
    const groupName = getGroup(field);
    let group = groups.find((candidate) => candidate.group === groupName);
    if (!group) {
      group = { group: groupName, fields: [] };
      groups.push(group);
    }

    group.fields.push(field);
  }

  return groups;
}

function getTrainerDataFieldGroup(field: TrainerEditableField) {
  if (
    field.field === trainerClassIdFieldName ||
    field.field === classBallIdFieldName ||
    field.field === battleTypeFieldName
  ) {
    return 'Trainer Setup';
  }

  if (trainerItemFieldNames.includes(field.field as (typeof trainerItemFieldNames)[number])) {
    return 'Battle Items';
  }

  if (
    field.field === healFieldName ||
    field.field === moneyFieldName ||
    field.field === giftFieldName
  ) {
    return 'Battle Rewards';
  }

  return 'Trainer Data';
}

function getTrainerPokemonFieldGroup(field: TrainerEditableField) {
  if (
    field.field === speciesIdFieldName ||
    field.field === formFieldName ||
    field.field === levelFieldName ||
    field.field === heldItemIdFieldName
  ) {
    return 'Pokemon';
  }

  if (moveFieldNames.includes(field.field as (typeof moveFieldNames)[number])) {
    return 'Moves';
  }

  if (
    field.field === genderFieldName ||
    field.field === abilityFieldName ||
    field.field === natureFieldName ||
    field.field === dynamaxLevelFieldName ||
    field.field === canGigantamaxFieldName ||
    field.field === shinyFieldName ||
    field.field === canDynamaxFieldName
  ) {
    return 'Traits';
  }

  if (evFieldNames.includes(field.field as (typeof evFieldNames)[number])) {
    return 'Stats - EVs';
  }

  if (ivFieldNames.includes(field.field as (typeof ivFieldNames)[number])) {
    return 'Stats - IVs';
  }

  return 'Pokemon Data';
}

function getTrainerDraftSummary(
  fields: NumericEditableField[],
  drafts: Record<string, string>,
  getValue: ((field: string) => number | null) | null
): { changedFields: TrainerDraftChange[]; dirtyFieldCount: number; invalidFields: TrainerDraftChange[] } {
  const changedFields: TrainerDraftChange[] = [];
  const invalidFields: TrainerDraftChange[] = [];
  let dirtyFieldCount = 0;

  if (!getValue) {
    return { changedFields, dirtyFieldCount, invalidFields };
  }

  for (const field of fields) {
    const currentValue = getValue(field.field);
    const draftValue = drafts[field.field] ?? '';
    const draftState = getTrainerFieldDraftState(draftValue, currentValue, field);

    if (draftState.isChanged || !draftState.isValid) {
      dirtyFieldCount += 1;
    }

    if (!draftState.isValid) {
      invalidFields.push({ field: field.field, label: field.label, value: draftValue });
      continue;
    }

    if (draftState.isChanged && draftState.normalizedValue !== null) {
      changedFields.push({
        field: field.field,
        label: field.label,
        value: draftState.normalizedValue
      });
    }
  }

  return { changedFields, dirtyFieldCount, invalidFields };
}

function getTrainerFieldDraftState(
  draftValue: string,
  currentValue: number | null,
  field: NumericEditableField
): TrainerDraftState {
  const normalizedValue = draftValue.trim();

  if (currentValue === null && normalizedValue === '') {
    return {
      error: null,
      isChanged: false,
      isValid: true,
      normalizedValue: null
    };
  }

  const currentText = currentValue?.toString() ?? '';

  if (field.valueKind === 'boolean') {
    if (normalizedValue !== '0' && normalizedValue !== '1') {
      return {
        error: 'Choose Yes or No.',
        isChanged: normalizedValue !== currentText,
        isValid: false,
        normalizedValue: null
      };
    }

    return {
      error: null,
      isChanged: normalizedValue !== currentText,
      isValid: true,
      normalizedValue
    };
  }

  const parsedValue = parseEditableIntegerDraft(normalizedValue, field.options);

  if (parsedValue === null) {
    return {
      error: 'Enter a whole number.',
      isChanged: normalizedValue !== currentText,
      isValid: false,
      normalizedValue: null
    };
  }

  const minimumValue = field.minimumValue ?? null;
  const maximumValue = field.maximumValue ?? null;

  if (minimumValue !== null && parsedValue < minimumValue) {
    return {
      error: `Minimum value is ${minimumValue}.`,
      isChanged: currentValue === null || parsedValue !== currentValue,
      isValid: false,
      normalizedValue: null
    };
  }

  if (maximumValue !== null && parsedValue > maximumValue) {
    return {
      error: `Maximum value is ${maximumValue}.`,
      isChanged: currentValue === null || parsedValue !== currentValue,
      isValid: false,
      normalizedValue: null
    };
  }

  if (
    field.options.length > 0 &&
    (currentValue === null || parsedValue !== currentValue) &&
    !field.options.some((option) => option.value === parsedValue)
  ) {
    return {
      error: 'Choose one of the available options.',
      isChanged: true,
      isValid: false,
      normalizedValue: null
    };
  }

  return {
    error: null,
    isChanged: currentValue === null || parsedValue !== currentValue,
    isValid: true,
    normalizedValue: parsedValue.toString()
  };
}

function formatDraftSummary(summary: {
  changedFields: unknown[];
  dirtyFieldCount: number;
  invalidFields: unknown[];
}) {
  if (summary.invalidFields.length > 0) {
    return `${summary.invalidFields.length} field${
      summary.invalidFields.length === 1 ? '' : 's'
    } need valid values`;
  }

  return `${summary.changedFields.length} changed`;
}

function formatPendingEditDomain(domain: string) {
  const labels: Record<string, string> = {
    'workflow.dynamaxAdventures': 'Dynamax Adventures',
    'workflow.encounters': 'Encounters',
    'workflow.exefs': 'ExeFS Patches',
    'workflow.giftPokemon': 'Gift Pokemon',
    'workflow.items': 'Items',
    'workflow.moves': 'Moves',
    'workflow.placement': 'Placement',
    'workflow.pokemon': 'Pokemon Data',
    'workflow.raidBattles': 'Raid Battles',
    'workflow.raidRewards': 'Raid Rewards',
    'workflow.rentalPokemon': 'Rental Pokemon',
    'workflow.royalCandy': 'Royal Candy',
    'workflow.shops': 'Shops',
    'workflow.staticEncounters': 'Static Encounters',
    'workflow.text': 'Text',
    'workflow.tradePokemon': 'Trade Pokemon',
    'workflow.trainers': 'Trainers'
  };

  return labels[domain] ?? domain;
}

function groupNumericEditableFields<TField extends NumericEditableField>(
  fields: TField[],
  getGroup: (field: TField) => string
) {
  const groups: Array<{ group: string; fields: TField[] }> = [];

  for (const field of fields) {
    const groupName = getGroup(field);
    let group = groups.find((candidate) => candidate.group === groupName);
    if (!group) {
      group = { group: groupName, fields: [] };
      groups.push(group);
    }

    group.fields.push(field);
  }

  return groups;
}

function getItemEditableFieldGroup(field: ItemEditableField) {
  const fieldName = field.field.toLocaleLowerCase();

  if (
    field.field === 'buyPrice' ||
    field.field === 'sellPrice' ||
    field.field === 'wattsPrice' ||
    field.field === 'alternatePrice'
  ) {
    return 'Prices';
  }

  if (
    field.field === 'pouch' ||
    fieldName.includes('sort') ||
    fieldName.includes('category')
  ) {
    return 'Bag Metadata';
  }

  if (
    fieldName.includes('heal') ||
    fieldName.startsWith('ev') ||
    fieldName.includes('friendship') ||
    fieldName.includes('pp')
  ) {
    return 'Use Effects';
  }

  if (field.valueKind === 'boolean') {
    return 'Use Flags';
  }

  return 'Item Data';
}

function toNumericEditableField(field: MoveEditableField): NumericEditableField {
  return {
    field: field.field,
    label: field.label,
    maximumValue: field.maximumValue,
    minimumValue: field.minimumValue,
    options: field.options,
    valueKind: field.valueKind
  };
}

function getMoveEditableFieldGroup(field: NumericEditableField) {
  if (
    field.field === 'type' ||
    field.field === 'category' ||
    field.field === 'power' ||
    field.field === 'accuracy' ||
    field.field === 'pp' ||
    field.field === 'priority' ||
    field.field === 'critStage' ||
    field.field === 'maxMovePower'
  ) {
    return 'Core Stats';
  }

  if (
    field.field === 'target' ||
    field.field === 'hitMin' ||
    field.field === 'hitMax' ||
    field.field === 'turnMin' ||
    field.field === 'turnMax' ||
    field.field === 'quality'
  ) {
    return 'Targeting';
  }

  if (
    field.field === 'inflict' ||
    field.field === 'inflictPercent' ||
    field.field === 'rawInflictCount' ||
    field.field === 'flinch' ||
    field.field === 'effectSequence' ||
    field.field === 'recoil' ||
    field.field === 'rawHealing'
  ) {
    return 'Secondary Effects';
  }

  if (field.field.startsWith('stat')) {
    return 'Stat Changes';
  }

  if (field.valueKind === 'boolean' || field.field === 'canUseMove') {
    return 'Flags';
  }

  return 'Move Data';
}

function getEncounterEditableFieldGroup(field: NumericEditableField) {
  if (field.field === raidBattleSpeciesFieldName || field.field === encounterFormFieldName) {
    return 'Pokemon';
  }

  if (field.field === encounterLevelMinFieldName || field.field === encounterLevelMaxFieldName) {
    return 'Levels';
  }

  if (field.field === encounterProbabilityFieldName) {
    return 'Probability';
  }

  return 'Encounter Data';
}

function getRaidBattleEditableFieldGroup(field: NumericEditableField) {
  if (field.field === raidBattleSpeciesFieldName || field.field === raidBattleFormFieldName) {
    return 'Pokemon';
  }

  if (
    field.field === raidBattleAbilityFieldName ||
    field.field === raidBattleGenderFieldName ||
    field.field === raidBattleIsGigantamaxFieldName ||
    field.field === raidBattleFlawlessIvsFieldName
  ) {
    return 'Traits';
  }

  if (
    raidBattleProbabilityFieldNames.includes(
      field.field as (typeof raidBattleProbabilityFieldNames)[number]
    )
  ) {
    return 'Star Odds';
  }

  return 'Battle Data';
}

function getRaidRewardEditableFieldGroup(field: NumericEditableField) {
  if (field.field === raidRewardItemIdFieldName) {
    return 'Reward Item';
  }

  if (
    raidRewardValueFieldNames.includes(
      field.field as (typeof raidRewardValueFieldNames)[number]
    )
  ) {
    return field.label.includes('drop chance') ? 'Drop Chances' : 'Quantities';
  }

  return 'Reward Data';
}

function getPlacementEditableFieldGroup(field: PlacementEditableField) {
  if (
    field.field === placementItemIdFieldName ||
    field.field === placementQuantityFieldName ||
    field.field === placementChanceFieldName
  ) {
    return 'Item';
  }

  if (
    field.field === placementLocationXFieldName ||
    field.field === placementLocationYFieldName ||
    field.field === placementLocationZFieldName ||
    field.field === placementRotationYFieldName
  ) {
    return 'Position';
  }

  return 'Placement Data';
}

function getPokemonInstanceFieldGroup(field: NumericEditableField) {
  if (
    field.field === giftSpeciesFieldName ||
    field.field === formFieldName ||
    field.field === levelFieldName ||
    field.field === heldItemIdFieldName ||
    field.field === giftBallItemIdFieldName ||
    field.field === dynamaxAdventureBallItemIdFieldName
  ) {
    return 'Pokemon';
  }

  if (
    moveFieldNames.includes(field.field as (typeof moveFieldNames)[number]) ||
    staticEncounterMoveFieldNames.includes(
      field.field as (typeof staticEncounterMoveFieldNames)[number]
    ) ||
    tradeRelearnMoveFieldNames.includes(field.field as (typeof tradeRelearnMoveFieldNames)[number]) ||
    field.field === giftSpecialMoveIdFieldName
  ) {
    return 'Moves';
  }

  if (evFieldNames.includes(field.field as (typeof evFieldNames)[number])) {
    return 'Stats - EVs';
  }

  if (
    ivFieldNames.includes(field.field as (typeof ivFieldNames)[number]) ||
    dynamaxAdventureIvFieldNames.includes(
      field.field as (typeof dynamaxAdventureIvFieldNames)[number]
    ) ||
    field.field === giftFlawlessIvCountFieldName ||
    field.field === rentalFixedIvPresetFieldName ||
    field.field === dynamaxAdventureGuaranteedPerfectIvsFieldName
  ) {
    return 'Stats - IVs';
  }

  if (
    field.field === abilityFieldName ||
    field.field === natureFieldName ||
    field.field === genderFieldName ||
    field.field === giftShinyLockFieldName ||
    field.field === dynamaxLevelFieldName ||
    field.field === canGigantamaxFieldName ||
    field.field === dynamaxAdventureGigantamaxStateFieldName ||
    field.field === dynamaxAdventureShinyRollFieldName ||
    field.field === dynamaxAdventureOtGenderFieldName ||
    field.field === dynamaxAdventureIsSingleCaptureFieldName ||
    field.field === dynamaxAdventureIsStoryProgressGatedFieldName
  ) {
    return 'Traits';
  }

  if (
    field.field === tradeRequiredSpeciesFieldName ||
    field.field === tradeRequiredFormFieldName ||
    field.field === tradeRequiredNatureFieldName ||
    field.field === tradeUnknownRequirementFieldName
  ) {
    return 'Trade Request';
  }

  if (
    field.field === tradeTrainerIdFieldName ||
    field.field === tradeOtGenderFieldName ||
    field.field === tradeMemoryCodeFieldName ||
    field.field === tradeMemoryTextVariableFieldName ||
    field.field === tradeMemoryFeelFieldName ||
    field.field === tradeMemoryIntensityFieldName
  ) {
    return 'Trade Memory';
  }

  if (
    field.field === tradeField03FieldName ||
    field.field === staticEncounterScenarioFieldName ||
    field.field === dynamaxAdventureVersionFieldName
  ) {
    return 'Advanced';
  }

  return 'Details';
}

function getTradeFieldDisabledReason(fieldName: string) {
  return lockedTradeFieldNames.includes(fieldName as (typeof lockedTradeFieldNames)[number])
    ? 'Locked until this field has a confirmed editor-safe meaning.'
    : null;
}

function GiftPokemonDraftField({
  currentValue,
  disabled,
  disabledReason,
  draftState,
  draftValue,
  field,
  formOptionContext,
  idPrefix = 'pokemon-instance',
  onChange
}: {
  currentValue: number | null;
  disabled: boolean;
  disabledReason?: string;
  draftState: TrainerDraftState;
  draftValue: string;
  field: NumericEditableField;
  formOptionContext?: SpeciesFormOptionContext;
  idPrefix?: string;
  onChange: (value: string) => void;
}) {
  const inputId = `${idPrefix}-${field.field}`;
  const options = getContextualFieldOptions(field, formOptionContext);
  const knownFormCount = useWorkbenchStore((state) => {
    if (!formOptionContext?.speciesId) {
      return undefined;
    }

    return state.pokemonWorkflow?.pokemon.find(
      (pokemon) => pokemon.speciesId === formOptionContext.speciesId && pokemon.form === 0
    )?.personal.formCount;
  });
  const formDisabledReason = getFormFieldDisabledReason(
    field,
    formOptionContext,
    currentValue,
    knownFormCount
  );
  const effectiveDisabledReason = disabledReason ?? formDisabledReason ?? undefined;
  const effectiveDisabled = disabled || Boolean(effectiveDisabledReason);
  const statusText = draftState.error ?? (draftState.isChanged ? 'Changed' : null);

  return (
    <label
      className={`path-field editable-field-control ${
        draftState.isChanged ? 'editable-field-changed' : ''
      } ${!draftState.isValid ? 'editable-field-invalid' : ''} ${
        effectiveDisabledReason ? 'editable-field-disabled' : ''
      }`}
      htmlFor={inputId}
      title={effectiveDisabledReason ?? getEditableFieldHelp(field)}
    >
      <span>{field.label}</span>
      {field.valueKind === 'boolean' ? (
        <select
          aria-label={field.label}
          disabled={effectiveDisabled}
          id={inputId}
          onChange={(event) => onChange(event.target.value)}
          title={effectiveDisabledReason ?? getEditableFieldHelp(field)}
          value={draftValue === '1' ? '1' : '0'}
        >
          <option value="1">Yes</option>
          <option value="0">No</option>
        </select>
      ) : options.length > 0 ? (
        <SearchableOptionInput
          ariaLabel={field.label}
          disabled={effectiveDisabled}
          id={inputId}
          onChange={onChange}
          options={addCurrentPokemonFieldOption(options, draftValue, field.label)}
          title={effectiveDisabledReason ?? getEditableFieldHelp(field)}
          value={draftValue}
        />
      ) : (
        <input
          aria-label={field.label}
          disabled={effectiveDisabled}
          id={inputId}
          max={field.maximumValue ?? undefined}
          min={field.minimumValue ?? undefined}
          onChange={(event) => onChange(event.target.value)}
          title={effectiveDisabledReason ?? getEditableFieldHelp(field)}
          type="number"
          value={draftValue}
        />
      )}
      {effectiveDisabledReason ? (
        <small className="editable-field-status">{effectiveDisabledReason}</small>
      ) : statusText ? (
        <small className={draftState.error ? 'editable-field-error' : 'editable-field-status'}>
          {statusText}
        </small>
      ) : null}
    </label>
  );
}

function GiftPokemonSection({
  editSession,
  isEditStarting,
  isGiftPokemonUpdating,
  onSearchChange,
  onSelectGift,
  onStartEditSession,
  onUpdateGiftPokemonField,
  onUpdateGiftPokemonFields,
  searchText,
  selectedGiftIndex,
  workflow
}: {
  editSession: EditSession | null;
  isEditStarting: boolean;
  isGiftPokemonUpdating: boolean;
  onSearchChange: (searchText: string) => void;
  onSelectGift: (giftIndex: number | null) => void;
  onStartEditSession: () => void;
  onUpdateGiftPokemonField: (giftIndex: number, field: string, value: string) => void;
  onUpdateGiftPokemonFields: (
    giftIndex: number,
    changes: Array<{ field: string; value: string }>
  ) => void;
  searchText: string;
  selectedGiftIndex: number | null;
  workflow: GiftPokemonWorkflow | null;
}) {
  const gifts = workflow?.gifts ?? [];
  const filteredGifts = useMemo(
    () => filterGiftPokemon(gifts, searchText),
    [gifts, searchText]
  );
  const selectedGift = useMemo(
    () =>
      gifts.find((gift) => gift.giftIndex === selectedGiftIndex) ?? filteredGifts[0] ?? null,
    [filteredGifts, gifts, selectedGiftIndex]
  );
  const canEditGifts = workflow?.summary.availability === 'available';
  const pendingGiftIndexes = useMemo(() => getPendingGiftPokemonIndexes(editSession), [editSession]);

  return (
    <>
      <section aria-labelledby="gift-pokemon-heading" className="panel wide-panel">
        <div className="panel-heading">
          <Dna aria-hidden="true" size={18} />
          <h2 id="gift-pokemon-heading">Gift Pokemon</h2>
        </div>

        <div className="items-toolbar trainers-toolbar">
          <label className="search-box items-search">
            <Search aria-hidden="true" size={18} />
            <input
              aria-label="Search gift Pokemon"
              disabled={!workflow}
              onChange={(event) => onSearchChange(event.target.value)}
              placeholder="Search gift Pokemon"
              type="search"
              value={searchText}
            />
          </label>
          <Metric
            label="Loaded gifts"
            value={workflow ? workflow.stats.totalGiftCount.toString() : '0'}
          />
          <Metric
            label="Egg gifts"
            value={workflow ? workflow.stats.eggGiftCount.toString() : '0'}
          />
          <Metric
            label="Fixed IV rows"
            value={workflow ? workflow.stats.fixedIvGiftCount.toString() : '0'}
          />
        </div>

        {workflow ? (
          <div className="trainers-layout">
            <div
              aria-colcount={6}
              aria-label="Gift Pokemon"
              aria-rowcount={filteredGifts.length + 1}
              className="trainers-table"
              role="table"
            >
              <div className="trainers-row trainers-row-heading" role="row">
                <span role="columnheader">Index</span>
                <span role="columnheader">Gift</span>
                <span role="columnheader">Species</span>
                <span role="columnheader">Level</span>
                <span role="columnheader">IVs</span>
                <span role="columnheader">Source</span>
              </div>
              <VirtualTableBody
                getKey={(gift) => gift.giftIndex}
                items={filteredGifts}
                renderRow={(gift) => (
                  <button
                    className={`trainers-row ${
                      selectedGift?.giftIndex === gift.giftIndex ? 'trainers-row-selected' : ''
                    } ${pendingGiftIndexes.has(gift.giftIndex) ? 'trainers-row-pending' : ''}`}
                    onClick={() => onSelectGift(gift.giftIndex)}
                    role="row"
                    type="button"
                  >
                    <span role="cell">{gift.giftIndex + 1}</span>
                    <span role="cell">{gift.label}</span>
                    <span role="cell">{gift.species}</span>
                    <span role="cell">{gift.level}</span>
                    <span role="cell">{gift.ivSummary}</span>
                    <span role="cell">{formatSourceLayer(gift.provenance.sourceLayer)}</span>
                  </button>
                )}
              />
            </div>

            <SelectedGiftPokemonPanel
              canEditGifts={canEditGifts}
              editSession={editSession}
              editableFields={workflow.editableFields}
              gift={selectedGift}
              isEditStarting={isEditStarting}
              isGiftPokemonUpdating={isGiftPokemonUpdating}
              onStartEditSession={onStartEditSession}
              onUpdateGiftPokemonField={onUpdateGiftPokemonField}
              onUpdateGiftPokemonFields={onUpdateGiftPokemonFields}
            />
          </div>
        ) : (
          <p className="empty-copy">Open Gift Pokemon from Workflows to load backend gift data.</p>
        )}
      </section>

      <DiagnosticsSection diagnostics={workflow?.diagnostics ?? []} />
    </>
  );
}

function SelectedGiftPokemonPanel({
  canEditGifts,
  editSession,
  editableFields,
  gift,
  isEditStarting,
  isGiftPokemonUpdating,
  onStartEditSession,
  onUpdateGiftPokemonField,
  onUpdateGiftPokemonFields
}: {
  canEditGifts: boolean;
  editSession: EditSession | null;
  editableFields: GiftPokemonEditableField[];
  gift: GiftPokemonRecord | null;
  isEditStarting: boolean;
  isGiftPokemonUpdating: boolean;
  onStartEditSession: () => void;
  onUpdateGiftPokemonField: (giftIndex: number, field: string, value: string) => void;
  onUpdateGiftPokemonFields: (
    giftIndex: number,
    changes: Array<{ field: string; value: string }>
  ) => void;
}) {
  const [giftDrafts, setGiftDrafts] = useState<Record<string, string>>({});
  const cancelActiveEditSession = useCancelActiveEditSession();
  const giftFields = useMemo(
    () =>
      editableFields.filter((field) =>
        giftPokemonFieldNames.includes(field.field as (typeof giftPokemonFieldNames)[number])
      ),
    [editableFields]
  );
  const giftFieldGroups = useMemo(
    () => groupNumericEditableFields(giftFields, getPokemonInstanceFieldGroup),
    [giftFields]
  );
  const giftDraftSummary = useMemo(
    () =>
      getTrainerDraftSummary(
        giftFields,
        giftDrafts,
        gift ? (field) => getEditableGiftPokemonFieldValue(gift, field) : null
      ),
    [gift, giftDrafts, giftFields]
  );
  const canSaveGiftDrafts =
    gift !== null &&
    editSession !== null &&
    canEditGifts &&
    !isGiftPokemonUpdating &&
    giftDraftSummary.changedFields.length > 0 &&
    giftDraftSummary.invalidFields.length === 0;

  useEffect(() => {
    if (!gift) {
      setGiftDrafts({});
      return;
    }

    setGiftDrafts(
      Object.fromEntries(
        giftFields.map((field) => [
          field.field,
          (getEditableGiftPokemonFieldValue(gift, field.field) ?? '').toString()
        ])
      )
    );
  }, [editableFields, gift]);

  return (
    <aside aria-label="Selected gift Pokemon provenance" className="trainer-inspector">
      <div className="panel-heading">
        <ShieldCheck aria-hidden="true" size={18} />
        <h3>Selected Gift</h3>
      </div>

      {gift ? (
        <>
          <PokemonSummaryCard
            name={formatSpeciesFormLabel(gift.species, gift.form, gift.speciesId)}
            subtitle={`Gift #${gift.giftIndex} | Lv. ${gift.level}`}
            title={formatSpeciesFormLabel(gift.species, gift.form, gift.speciesId)}
          />

          <dl className="item-provenance-list">
            <div>
              <dt>Gift</dt>
              <dd>{gift.label}</dd>
            </div>
            <div>
              <dt>Data file</dt>
              <dd>{gift.provenance.sourceFile}</dd>
            </div>
            <div>
              <dt>Layer</dt>
              <dd>{formatSourceLayer(gift.provenance.sourceLayer)}</dd>
            </div>
            <div>
              <dt>File state</dt>
              <dd>{formatFileState(gift.provenance.fileState)}</dd>
            </div>
            <div>
              <dt>Ball</dt>
              <dd>{gift.ballItem}</dd>
            </div>
            <div>
              <dt>Held item</dt>
              <dd>{gift.heldItem ?? 'None'}</dd>
            </div>
            <div>
              <dt>Special Move</dt>
              <dd>{gift.specialMove ?? 'None'}</dd>
            </div>
            <div>
              <dt>IV detail</dt>
              <dd>{formatGiftPokemonIvs(gift)}</dd>
            </div>
          </dl>

          <div className="trainer-edit-form">
            <div className="editable-field-groups">
              {giftFieldGroups.map((group) => (
                <fieldset className="editable-field-group" key={group.group}>
                  <legend>{group.group}</legend>
                  <div className="editable-field-grid">
                    {group.fields.map((field) => {
                      const currentValue = getEditableGiftPokemonFieldValue(gift, field.field);
                      const draftValue = giftDrafts[field.field] ?? '';
                      const draftState = getTrainerFieldDraftState(
                        draftValue,
                        currentValue,
                        field
                      );

                      return (
                        <GiftPokemonDraftField
                          currentValue={currentValue}
                          disabled={!canEditGifts || editSession === null || isGiftPokemonUpdating}
                          draftState={draftState}
                          draftValue={draftValue}
                          field={field}
                          formOptionContext={{
                            abilityOptions: gift.abilityOptions,
                            species: gift.species,
                            speciesId: gift.speciesId
                          }}
                          key={field.field}
                          onChange={(value) =>
                            setGiftDrafts((currentDrafts) => ({
                              ...currentDrafts,
                              [field.field]: value
                            }))
                          }
                        />
                      );
                    })}
                  </div>
                </fieldset>
              ))}
            </div>
            {editSession ? (
              <div className="draft-action-row">
                <button
                  className="primary-button"
                  disabled={!canSaveGiftDrafts}
                  onClick={() =>
                    onUpdateGiftPokemonFields(
                      gift.giftIndex,
                      giftDraftSummary.changedFields.map((change) => ({
                        field: change.field,
                        value: change.value
                      }))
                    )
                  }
                  type="button"
                >
                  <Save aria-hidden="true" size={16} />
                  <span>{isGiftPokemonUpdating ? 'Saving' : 'Save Gift'}</span>
                </button>
                <button
                  className="danger-button"
                  disabled={isGiftPokemonUpdating}
                  onClick={() => {
                    setGiftDrafts(createTrainerDrafts(giftFields, (field) =>
                      getEditableGiftPokemonFieldValue(gift, field)
                    ));
                    cancelActiveEditSession();
                  }}
                  type="button"
                >
                  <X aria-hidden="true" size={16} />
                  <span>Cancel</span>
                </button>
                <span className="draft-action-summary">{formatDraftSummary(giftDraftSummary)}</span>
              </div>
            ) : null}

            {!editSession ? (
              <button
                className="secondary-button"
                disabled={!canEditGifts || isEditStarting}
                onClick={onStartEditSession}
                type="button"
              >
                <Pencil aria-hidden="true" size={16} />
                <span>{isEditStarting ? 'Starting' : 'Edit'}</span>
              </button>
            ) : null}
          </div>
        </>
      ) : (
        <p className="empty-copy">No gift selected.</p>
      )}
    </aside>
  );
}

function GiftPokemonFieldInput({
  disabled,
  draftValue,
  field,
  formOptionContext,
  onChange
}: {
  disabled: boolean;
  draftValue: string;
  field: GiftPokemonEditableField;
  formOptionContext?: SpeciesFormOptionContext;
  onChange: (value: string) => void;
}) {
  const options = getContextualFieldOptions(field, formOptionContext);

  if (options.length > 0) {
    return (
      <SearchableOptionInput
        ariaLabel={field.label}
        disabled={disabled}
        title={getEditableFieldHelp(field)}
        onChange={onChange}
        options={addDraftFallbackOption(
          options,
          draftValue,
          draftValue === '' ? 'Custom fixed IVs' : draftValue
        )}
        value={draftValue}
      />
    );
  }

  return (
    <input
      aria-label={field.label}
      disabled={disabled}
      max={field.maximumValue ?? undefined}
      min={field.minimumValue ?? undefined}
      title={getEditableFieldHelp(field)}
      onChange={(event) => onChange(event.target.value)}
      type="number"
      value={draftValue}
    />
  );
}

function TradePokemonSection({
  editSession,
  isEditStarting,
  isTradePokemonUpdating,
  onSearchChange,
  onSelectTrade,
  onStartEditSession,
  onUpdateTradePokemonField,
  onUpdateTradePokemonFields,
  searchText,
  selectedTradeIndex,
  workflow
}: {
  editSession: EditSession | null;
  isEditStarting: boolean;
  isTradePokemonUpdating: boolean;
  onSearchChange: (searchText: string) => void;
  onSelectTrade: (tradeIndex: number | null) => void;
  onStartEditSession: () => void;
  onUpdateTradePokemonField: (tradeIndex: number, field: string, value: string) => void;
  onUpdateTradePokemonFields: (
    tradeIndex: number,
    changes: Array<{ field: string; value: string }>
  ) => void;
  searchText: string;
  selectedTradeIndex: number | null;
  workflow: TradePokemonWorkflow | null;
}) {
  const trades = workflow?.trades ?? [];
  const filteredTrades = useMemo(
    () => filterTradePokemon(trades, searchText),
    [searchText, trades]
  );
  const selectedTrade = useMemo(
    () =>
      trades.find((trade) => trade.tradeIndex === selectedTradeIndex) ??
      filteredTrades[0] ??
      null,
    [filteredTrades, selectedTradeIndex, trades]
  );
  const canEditTrades = workflow?.summary.availability === 'available';
  const pendingTradeIndexes = useMemo(
    () => getPendingTradePokemonIndexes(editSession),
    [editSession]
  );

  return (
    <>
      <section aria-labelledby="trade-pokemon-heading" className="panel wide-panel">
        <div className="panel-heading">
          <ArrowLeftRight aria-hidden="true" size={18} />
          <h2 id="trade-pokemon-heading">Trade Pokemon</h2>
        </div>

        <div className="items-toolbar trainers-toolbar">
          <label className="search-box items-search">
            <Search aria-hidden="true" size={18} />
            <input
              aria-label="Search trade Pokemon"
              disabled={!workflow}
              onChange={(event) => onSearchChange(event.target.value)}
              placeholder="Search trade Pokemon"
              type="search"
              value={searchText}
            />
          </label>
          <Metric
            label="Loaded trades"
            value={workflow ? workflow.stats.totalTradeCount.toString() : '0'}
          />
          <Metric
            label="Fixed IV rows"
            value={workflow ? workflow.stats.fixedIvTradeCount.toString() : '0'}
          />
          <Metric
            label="Sources"
            value={workflow ? workflow.stats.sourceFileCount.toString() : '0'}
          />
        </div>

        {workflow ? (
          <div className="trainers-layout">
            <div
              aria-colcount={7}
              aria-label="Trade Pokemon"
              aria-rowcount={filteredTrades.length + 1}
              className="trainers-table"
              role="table"
            >
              <div className="trainers-row trade-pokemon-row trainers-row-heading" role="row">
                <span role="columnheader">Index</span>
                <span role="columnheader">Requested</span>
                <span role="columnheader">Received</span>
                <span role="columnheader">Level</span>
                <span role="columnheader">IVs</span>
                <span role="columnheader">Moves</span>
                <span role="columnheader">Source</span>
              </div>
              <VirtualTableBody
                getKey={(trade) => trade.tradeIndex}
                items={filteredTrades}
                renderRow={(trade) => (
                  <button
                    className={`trainers-row trade-pokemon-row ${
                      selectedTrade?.tradeIndex === trade.tradeIndex
                        ? 'trainers-row-selected'
                        : ''
                    } ${
                      pendingTradeIndexes.has(trade.tradeIndex) ? 'trainers-row-pending' : ''
                    }`}
                    onClick={() => onSelectTrade(trade.tradeIndex)}
                    role="row"
                    type="button"
                  >
                    <span role="cell">{trade.tradeIndex + 1}</span>
                    <span role="cell">
                      {formatSpeciesFormLabel(
                        trade.requiredSpecies,
                        trade.requiredForm,
                        trade.requiredSpeciesId
                      )}
                    </span>
                    <span role="cell">
                      {formatSpeciesFormLabel(trade.species, trade.form, trade.speciesId)}
                    </span>
                    <span role="cell">{trade.level}</span>
                    <span role="cell">{trade.ivSummary}</span>
                    <span role="cell">{formatTradePokemonRelearnMoves(trade)}</span>
                    <span role="cell">{formatSourceLayer(trade.provenance.sourceLayer)}</span>
                  </button>
                )}
              />
            </div>

            <SelectedTradePokemonPanel
              canEditTrades={canEditTrades}
              editSession={editSession}
              editableFields={workflow.editableFields}
              isEditStarting={isEditStarting}
              isTradePokemonUpdating={isTradePokemonUpdating}
              onStartEditSession={onStartEditSession}
              onUpdateTradePokemonField={onUpdateTradePokemonField}
              onUpdateTradePokemonFields={onUpdateTradePokemonFields}
              trade={selectedTrade}
            />
          </div>
        ) : (
          <p className="empty-copy">Open Trade Pokemon from Workflows to load backend trade data.</p>
        )}
      </section>

      <DiagnosticsSection diagnostics={workflow?.diagnostics ?? []} />
    </>
  );
}

function SelectedTradePokemonPanel({
  canEditTrades,
  editSession,
  editableFields,
  isEditStarting,
  isTradePokemonUpdating,
  onStartEditSession,
  onUpdateTradePokemonField,
  onUpdateTradePokemonFields,
  trade
}: {
  canEditTrades: boolean;
  editSession: EditSession | null;
  editableFields: TradePokemonEditableField[];
  isEditStarting: boolean;
  isTradePokemonUpdating: boolean;
  onStartEditSession: () => void;
  onUpdateTradePokemonField: (tradeIndex: number, field: string, value: string) => void;
  onUpdateTradePokemonFields: (
    tradeIndex: number,
    changes: Array<{ field: string; value: string }>
  ) => void;
  trade: TradePokemonRecord | null;
}) {
  const [tradeDrafts, setTradeDrafts] = useState<Record<string, string>>({});
  const cancelActiveEditSession = useCancelActiveEditSession();
  const tradeFields = editableFields.filter((field) =>
    tradePokemonFieldNames.includes(field.field as (typeof tradePokemonFieldNames)[number])
  );
  const tradeFieldGroups = useMemo(
    () => groupNumericEditableFields(tradeFields, getPokemonInstanceFieldGroup),
    [tradeFields]
  );
  const tradeDraftSummary = useMemo(
    () =>
      getTrainerDraftSummary(
        tradeFields,
        tradeDrafts,
        trade ? (field) => getEditableTradePokemonFieldValue(trade, field) : null
      ),
    [trade, tradeDrafts, tradeFields]
  );
  const canSaveTradeDrafts =
    trade !== null &&
    editSession !== null &&
    canEditTrades &&
    !isTradePokemonUpdating &&
    tradeDraftSummary.changedFields.length > 0 &&
    tradeDraftSummary.invalidFields.length === 0;

  useEffect(() => {
    if (!trade) {
      setTradeDrafts({});
      return;
    }

    setTradeDrafts(
      Object.fromEntries(
        tradeFields.map((field) => [
          field.field,
          (getEditableTradePokemonFieldValue(trade, field.field) ?? '').toString()
        ])
      )
    );
  }, [editableFields, trade]);

  return (
    <aside aria-label="Selected trade Pokemon provenance" className="trainer-inspector">
      <div className="panel-heading">
        <ShieldCheck aria-hidden="true" size={18} />
        <h3>Selected Trade</h3>
      </div>

      {trade ? (
        <>
          <div className="pokemon-summary-grid">
            <PokemonSummaryCard
              name={formatSpeciesFormLabel(
                trade.requiredSpecies,
                trade.requiredForm,
                trade.requiredSpeciesId
              )}
              subtitle="Requested"
              title={formatSpeciesFormLabel(
                trade.requiredSpecies,
                trade.requiredForm,
                trade.requiredSpeciesId
              )}
            />
            <PokemonSummaryCard
              name={formatSpeciesFormLabel(trade.species, trade.form, trade.speciesId)}
              subtitle={`Received | Lv. ${trade.level}`}
              title={formatSpeciesFormLabel(trade.species, trade.form, trade.speciesId)}
            />
          </div>

          <dl className="item-provenance-list">
            <div>
              <dt>Trade</dt>
              <dd>{trade.label}</dd>
            </div>
            <div>
              <dt>Data file</dt>
              <dd>{trade.provenance.sourceFile}</dd>
            </div>
            <div>
              <dt>Layer</dt>
              <dd>{formatSourceLayer(trade.provenance.sourceLayer)}</dd>
            </div>
            <div>
              <dt>File state</dt>
              <dd>{formatFileState(trade.provenance.fileState)}</dd>
            </div>
            <div>
              <dt>Requested</dt>
              <dd>
                {formatSpeciesFormLabel(
                  trade.requiredSpecies,
                  trade.requiredForm,
                  trade.requiredSpeciesId
                )}
              </dd>
            </div>
            <div>
              <dt>Received</dt>
              <dd>{`${formatSpeciesFormLabel(trade.species, trade.form, trade.speciesId)} Lv. ${trade.level}`}</dd>
            </div>
            <div>
              <dt>Ball</dt>
              <dd>{trade.ballItem}</dd>
            </div>
            <div>
              <dt>Held item</dt>
              <dd>{trade.heldItem ?? 'None'}</dd>
            </div>
            <div>
              <dt>Relearn moves</dt>
              <dd>{formatTradePokemonRelearnMoves(trade)}</dd>
            </div>
            <div>
              <dt>Memory</dt>
              <dd>{formatTradePokemonMemory(trade)}</dd>
            </div>
            <div>
              <dt>Identifiers</dt>
              <dd>{`${trade.hash0} / ${trade.hash1} / ${trade.hash2}`}</dd>
            </div>
            <div>
              <dt>IV detail</dt>
              <dd>{formatTradePokemonIvs(trade)}</dd>
            </div>
          </dl>

          <div className="trainer-edit-form">
            <div className="editable-field-groups">
              {tradeFieldGroups.map((group) => (
                <fieldset className="editable-field-group" key={group.group}>
                  <legend>{group.group}</legend>
                  <div className="editable-field-grid">
                    {group.fields.map((field) => {
                      const currentValue = getEditableTradePokemonFieldValue(trade, field.field);
                      const draftValue = tradeDrafts[field.field] ?? '';
                      const draftState = getTrainerFieldDraftState(
                        draftValue,
                        currentValue,
                        field
                      );
                      const disabledReason = getTradeFieldDisabledReason(field.field);

                      return (
                        <GiftPokemonDraftField
                          currentValue={currentValue}
                          disabled={
                            !canEditTrades ||
                            editSession === null ||
                            isTradePokemonUpdating ||
                            disabledReason !== null
                          }
                          disabledReason={disabledReason ?? undefined}
                          draftState={draftState}
                          draftValue={draftValue}
                          field={field}
                          formOptionContext={
                            field.field === tradeRequiredFormFieldName
                              ? {
                                  species: trade.requiredSpecies,
                                  speciesId: trade.requiredSpeciesId
                                }
                              : {
                                  abilityOptions: trade.abilityOptions,
                                  species: trade.species,
                                  speciesId: trade.speciesId
                                }
                          }
                          key={field.field}
                          onChange={(value) =>
                            setTradeDrafts((currentDrafts) => ({
                              ...currentDrafts,
                              [field.field]: value
                            }))
                          }
                        />
                      );
                    })}
                  </div>
                </fieldset>
              ))}
            </div>
            {editSession ? (
              <div className="draft-action-row">
                <button
                  className="primary-button"
                  disabled={!canSaveTradeDrafts}
                  onClick={() =>
                    onUpdateTradePokemonFields(
                      trade.tradeIndex,
                      tradeDraftSummary.changedFields.map((change) => ({
                        field: change.field,
                        value: change.value
                      }))
                    )
                  }
                  type="button"
                >
                  <Save aria-hidden="true" size={16} />
                  <span>{isTradePokemonUpdating ? 'Saving' : 'Save Trade'}</span>
                </button>
                <button
                  className="danger-button"
                  disabled={isTradePokemonUpdating}
                  onClick={() => {
                    setTradeDrafts(createTrainerDrafts(tradeFields, (field) =>
                      getEditableTradePokemonFieldValue(trade, field)
                    ));
                    cancelActiveEditSession();
                  }}
                  type="button"
                >
                  <X aria-hidden="true" size={16} />
                  <span>Cancel</span>
                </button>
                <span className="draft-action-summary">{formatDraftSummary(tradeDraftSummary)}</span>
              </div>
            ) : null}

            {!editSession ? (
              <button
                className="secondary-button"
                disabled={!canEditTrades || isEditStarting}
                onClick={onStartEditSession}
                type="button"
              >
                <Pencil aria-hidden="true" size={16} />
                <span>{isEditStarting ? 'Starting' : 'Edit'}</span>
              </button>
            ) : null}
          </div>
        </>
      ) : (
        <p className="empty-copy">No trade selected.</p>
      )}
    </aside>
  );
}

function TradePokemonFieldInput({
  disabled,
  draftValue,
  field,
  formOptionContext,
  onChange
}: {
  disabled: boolean;
  draftValue: string;
  field: TradePokemonEditableField;
  formOptionContext?: SpeciesFormOptionContext;
  onChange: (value: string) => void;
}) {
  const options = getContextualFieldOptions(field, formOptionContext);

  if (options.length > 0) {
    return (
      <SearchableOptionInput
        ariaLabel={field.label}
        disabled={disabled}
        title={getEditableFieldHelp(field)}
        onChange={onChange}
        options={addDraftFallbackOption(
          options,
          draftValue,
          draftValue === '' ? 'Custom fixed IVs' : draftValue
        )}
        value={draftValue}
      />
    );
  }

  return (
    <input
      aria-label={field.label}
      disabled={disabled}
      max={field.maximumValue ?? undefined}
      min={field.minimumValue ?? undefined}
      title={getEditableFieldHelp(field)}
      onChange={(event) => onChange(event.target.value)}
      type="number"
      value={draftValue}
    />
  );
}

function RentalPokemonSection({
  editSession,
  isEditStarting,
  isRentalPokemonUpdating,
  onSearchChange,
  onSelectRental,
  onStartEditSession,
  onUpdateRentalPokemonField,
  onUpdateRentalPokemonFields,
  searchText,
  selectedRentalIndex,
  workflow
}: {
  editSession: EditSession | null;
  isEditStarting: boolean;
  isRentalPokemonUpdating: boolean;
  onSearchChange: (searchText: string) => void;
  onSelectRental: (rentalIndex: number | null) => void;
  onStartEditSession: () => void;
  onUpdateRentalPokemonField: (rentalIndex: number, field: string, value: string) => void;
  onUpdateRentalPokemonFields: (
    rentalIndex: number,
    changes: Array<{ field: string; value: string }>
  ) => void;
  searchText: string;
  selectedRentalIndex: number | null;
  workflow: RentalPokemonWorkflow | null;
}) {
  const rentals = workflow?.rentals ?? [];
  const filteredRentals = useMemo(
    () => filterRentalPokemon(rentals, searchText),
    [rentals, searchText]
  );
  const selectedRental = useMemo(
    () =>
      rentals.find((rental) => rental.rentalIndex === selectedRentalIndex) ??
      filteredRentals[0] ??
      null,
    [filteredRentals, rentals, selectedRentalIndex]
  );
  const canEditRentals = workflow?.summary.availability === 'available';
  const pendingRentalIndexes = useMemo(
    () => getPendingRentalPokemonIndexes(editSession),
    [editSession]
  );

  return (
    <>
      <section aria-labelledby="rental-pokemon-heading" className="panel wide-panel">
        <div className="panel-heading">
          <Dna aria-hidden="true" size={18} />
          <h2 id="rental-pokemon-heading">Rental Pokemon</h2>
        </div>

        <div className="items-toolbar trainers-toolbar">
          <label className="search-box items-search">
            <Search aria-hidden="true" size={18} />
            <input
              aria-label="Search rental Pokemon"
              disabled={!workflow}
              onChange={(event) => onSearchChange(event.target.value)}
              placeholder="Search rental Pokemon"
              type="search"
              value={searchText}
            />
          </label>
          <Metric
            label="Loaded rentals"
            value={workflow ? workflow.stats.totalRentalCount.toString() : '0'}
          />
          <Metric
            label="Perfect IV rows"
            value={workflow ? workflow.stats.perfectIvRentalCount.toString() : '0'}
          />
          <Metric
            label="Sources"
            value={workflow ? workflow.stats.sourceFileCount.toString() : '0'}
          />
        </div>

        {workflow ? (
          <div className="trainers-layout">
            <div
              aria-colcount={7}
              aria-label="Rental Pokemon"
              aria-rowcount={filteredRentals.length + 1}
              className="trainers-table"
              role="table"
            >
              <div className="trainers-row rental-pokemon-row trainers-row-heading" role="row">
                <span role="columnheader">Index</span>
                <span role="columnheader">Pokemon</span>
                <span role="columnheader">Level</span>
                <span role="columnheader">IVs</span>
                <span role="columnheader">EVs</span>
                <span role="columnheader">Moves</span>
                <span role="columnheader">Source</span>
              </div>
              <VirtualTableBody
                getKey={(rental) => rental.rentalIndex}
                items={filteredRentals}
                renderRow={(rental) => (
                  <button
                    className={`trainers-row rental-pokemon-row ${
                      selectedRental?.rentalIndex === rental.rentalIndex
                        ? 'trainers-row-selected'
                        : ''
                    } ${
                      pendingRentalIndexes.has(rental.rentalIndex) ? 'trainers-row-pending' : ''
                    }`}
                    onClick={() => onSelectRental(rental.rentalIndex)}
                    role="row"
                    type="button"
                  >
                    <span role="cell">{rental.rentalIndex + 1}</span>
                    <span role="cell">
                      {formatSpeciesFormLabel(rental.species, rental.form, rental.speciesId)}
                    </span>
                    <span role="cell">{rental.level}</span>
                    <span role="cell">{rental.ivSummary}</span>
                    <span role="cell">{formatRentalPokemonStats(rental.evs)}</span>
                    <span role="cell">{formatRentalPokemonMoves(rental)}</span>
                    <span role="cell">{formatSourceLayer(rental.provenance.sourceLayer)}</span>
                  </button>
                )}
              />
            </div>

            <SelectedRentalPokemonPanel
              canEditRentals={canEditRentals}
              editSession={editSession}
              editableFields={workflow.editableFields}
              isEditStarting={isEditStarting}
              isRentalPokemonUpdating={isRentalPokemonUpdating}
              onStartEditSession={onStartEditSession}
              onUpdateRentalPokemonField={onUpdateRentalPokemonField}
              onUpdateRentalPokemonFields={onUpdateRentalPokemonFields}
              rental={selectedRental}
            />
          </div>
        ) : (
          <p className="empty-copy">Open Rental Pokemon from Workflows to load backend rental data.</p>
        )}
      </section>

      <DiagnosticsSection diagnostics={workflow?.diagnostics ?? []} />
    </>
  );
}

function SelectedRentalPokemonPanel({
  canEditRentals,
  editSession,
  editableFields,
  isEditStarting,
  isRentalPokemonUpdating,
  onStartEditSession,
  onUpdateRentalPokemonField,
  onUpdateRentalPokemonFields,
  rental
}: {
  canEditRentals: boolean;
  editSession: EditSession | null;
  editableFields: RentalPokemonEditableField[];
  isEditStarting: boolean;
  isRentalPokemonUpdating: boolean;
  onStartEditSession: () => void;
  onUpdateRentalPokemonField: (rentalIndex: number, field: string, value: string) => void;
  onUpdateRentalPokemonFields: (
    rentalIndex: number,
    changes: Array<{ field: string; value: string }>
  ) => void;
  rental: RentalPokemonRecord | null;
}) {
  const [rentalDrafts, setRentalDrafts] = useState<Record<string, string>>({});
  const cancelActiveEditSession = useCancelActiveEditSession();
  const rentalFields = editableFields.filter((field) =>
    rentalPokemonFieldNames.includes(field.field as (typeof rentalPokemonFieldNames)[number])
  );
  const rentalFieldGroups = useMemo(
    () => groupNumericEditableFields(rentalFields, getPokemonInstanceFieldGroup),
    [rentalFields]
  );
  const rentalDraftSummary = useMemo(
    () =>
      getTrainerDraftSummary(
        rentalFields,
        rentalDrafts,
        rental ? (field) => getEditableRentalPokemonFieldValue(rental, field) : null
      ),
    [rental, rentalDrafts, rentalFields]
  );
  const canSaveRentalDrafts =
    rental !== null &&
    editSession !== null &&
    canEditRentals &&
    !isRentalPokemonUpdating &&
    rentalDraftSummary.changedFields.length > 0 &&
    rentalDraftSummary.invalidFields.length === 0;

  useEffect(() => {
    if (!rental) {
      setRentalDrafts({});
      return;
    }

    setRentalDrafts(
      Object.fromEntries(
        rentalFields.map((field) => [
          field.field,
          (getEditableRentalPokemonFieldValue(rental, field.field) ?? '').toString()
        ])
      )
    );
  }, [editableFields, rental]);

  return (
    <aside aria-label="Selected rental Pokemon provenance" className="trainer-inspector">
      <div className="panel-heading">
        <ShieldCheck aria-hidden="true" size={18} />
        <h3>Selected Rental</h3>
      </div>

      {rental ? (
        <>
          <PokemonSummaryCard
            name={formatSpeciesFormLabel(rental.species, rental.form, rental.speciesId)}
            subtitle={`Rental #${rental.rentalIndex} | Lv. ${rental.level}`}
            title={formatSpeciesFormLabel(rental.species, rental.form, rental.speciesId)}
          />

          <dl className="item-provenance-list">
            <div>
              <dt>Rental</dt>
              <dd>{rental.label}</dd>
            </div>
            <div>
              <dt>Data file</dt>
              <dd>{rental.provenance.sourceFile}</dd>
            </div>
            <div>
              <dt>Layer</dt>
              <dd>{formatSourceLayer(rental.provenance.sourceLayer)}</dd>
            </div>
            <div>
              <dt>File state</dt>
              <dd>{formatFileState(rental.provenance.fileState)}</dd>
            </div>
            <div>
              <dt>Pokemon</dt>
              <dd>{`${formatSpeciesFormLabel(rental.species, rental.form, rental.speciesId)} Lv. ${rental.level}`}</dd>
            </div>
            <div>
              <dt>Ball</dt>
              <dd>{rental.ballItem}</dd>
            </div>
            <div>
              <dt>Held item</dt>
              <dd>{rental.heldItem ?? 'None'}</dd>
            </div>
            <div>
              <dt>Ability</dt>
              <dd>{rental.abilityLabel}</dd>
            </div>
            <div>
              <dt>Nature / Gender</dt>
              <dd>{`${rental.natureLabel} / ${rental.genderLabel}`}</dd>
            </div>
            <div>
              <dt>Trainer ID</dt>
              <dd>{rental.trainerId}</dd>
            </div>
            <div>
              <dt>Identifiers</dt>
              <dd>{`${rental.hash1} / ${rental.hash2}`}</dd>
            </div>
            <div>
              <dt>Moves</dt>
              <dd>{formatRentalPokemonMoves(rental)}</dd>
            </div>
            <div>
              <dt>EV detail</dt>
              <dd>{formatRentalPokemonStats(rental.evs)}</dd>
            </div>
            <div>
              <dt>IV detail</dt>
              <dd>{formatRentalPokemonIvs(rental)}</dd>
            </div>
          </dl>

          <div className="trainer-edit-form">
            <div className="editable-field-groups">
              {rentalFieldGroups.map((group) => (
                <fieldset className="editable-field-group" key={group.group}>
                  <legend>{group.group}</legend>
                  <div className="editable-field-grid">
                    {group.fields.map((field) => {
                      const currentValue = getEditableRentalPokemonFieldValue(
                        rental,
                        field.field
                      );
                      const draftValue = rentalDrafts[field.field] ?? '';
                      const draftState = getTrainerFieldDraftState(
                        draftValue,
                        currentValue,
                        field
                      );

                      return (
                        <GiftPokemonDraftField
                          currentValue={currentValue}
                          disabled={
                            !canEditRentals || editSession === null || isRentalPokemonUpdating
                          }
                          draftState={draftState}
                          draftValue={draftValue}
                          field={field}
                          formOptionContext={{
                            abilityOptions: rental.abilityOptions,
                            species: rental.species,
                            speciesId: rental.speciesId
                          }}
                          key={field.field}
                          onChange={(value) =>
                            setRentalDrafts((currentDrafts) => ({
                              ...currentDrafts,
                              [field.field]: value
                            }))
                          }
                        />
                      );
                    })}
                  </div>
                </fieldset>
              ))}
            </div>
            {editSession ? (
              <div className="draft-action-row">
                <button
                  className="primary-button"
                  disabled={!canSaveRentalDrafts}
                  onClick={() =>
                    onUpdateRentalPokemonFields(
                      rental.rentalIndex,
                      rentalDraftSummary.changedFields.map((change) => ({
                        field: change.field,
                        value: change.value
                      }))
                    )
                  }
                  type="button"
                >
                  <Save aria-hidden="true" size={16} />
                  <span>{isRentalPokemonUpdating ? 'Saving' : 'Save Rental'}</span>
                </button>
                <button
                  className="danger-button"
                  disabled={isRentalPokemonUpdating}
                  onClick={() => {
                    setRentalDrafts(createTrainerDrafts(rentalFields, (field) =>
                      getEditableRentalPokemonFieldValue(rental, field)
                    ));
                    cancelActiveEditSession();
                  }}
                  type="button"
                >
                  <X aria-hidden="true" size={16} />
                  <span>Cancel</span>
                </button>
                <span className="draft-action-summary">
                  {formatDraftSummary(rentalDraftSummary)}
                </span>
              </div>
            ) : null}

            {!editSession ? (
              <button
                className="secondary-button"
                disabled={!canEditRentals || isEditStarting}
                onClick={onStartEditSession}
                type="button"
              >
                <Pencil aria-hidden="true" size={16} />
                <span>{isEditStarting ? 'Starting' : 'Edit'}</span>
              </button>
            ) : null}
          </div>
        </>
      ) : (
        <p className="empty-copy">No rental selected.</p>
      )}
    </aside>
  );
}

function RentalPokemonFieldInput({
  disabled,
  draftValue,
  field,
  formOptionContext,
  onChange
}: {
  disabled: boolean;
  draftValue: string;
  field: RentalPokemonEditableField;
  formOptionContext?: SpeciesFormOptionContext;
  onChange: (value: string) => void;
}) {
  const options = getContextualFieldOptions(field, formOptionContext);

  if (options.length > 0) {
    return (
      <SearchableOptionInput
        ariaLabel={field.label}
        disabled={disabled}
        title={getEditableFieldHelp(field)}
        onChange={onChange}
        options={addDraftFallbackOption(
          options,
          draftValue,
          draftValue === '' ? 'Mixed fixed IVs' : draftValue
        )}
        value={draftValue}
      />
    );
  }

  return (
    <input
      aria-label={field.label}
      disabled={disabled}
      max={field.maximumValue ?? undefined}
      min={field.minimumValue ?? undefined}
      title={getEditableFieldHelp(field)}
      onChange={(event) => onChange(event.target.value)}
      type="number"
      value={draftValue}
    />
  );
}

function DynamaxAdventuresSection({
  editSession,
  isDynamaxAdventureUpdating,
  isEditStarting,
  onSearchChange,
  onSelectAdventure,
  onStartEditSession,
  onUpdateDynamaxAdventureField,
  onUpdateDynamaxAdventureFields,
  searchText,
  selectedEntryIndex,
  workflow
}: {
  editSession: EditSession | null;
  isDynamaxAdventureUpdating: boolean;
  isEditStarting: boolean;
  onSearchChange: (searchText: string) => void;
  onSelectAdventure: (entryIndex: number | null) => void;
  onStartEditSession: () => void;
  onUpdateDynamaxAdventureField: (entryIndex: number, field: string, value: string) => void;
  onUpdateDynamaxAdventureFields: (
    entryIndex: number,
    changes: Array<{ field: string; value: string }>
  ) => void;
  searchText: string;
  selectedEntryIndex: number | null;
  workflow: DynamaxAdventuresWorkflow | null;
}) {
  const encounters = workflow?.encounters ?? [];
  const filteredEncounters = useMemo(
    () => filterDynamaxAdventures(encounters, searchText),
    [encounters, searchText]
  );
  const selectedEncounter = useMemo(
    () =>
      encounters.find((encounter) => encounter.entryIndex === selectedEntryIndex) ??
      filteredEncounters[0] ??
      null,
    [encounters, filteredEncounters, selectedEntryIndex]
  );
  const canEditDynamaxAdventures = workflow?.summary.availability === 'available';
  const pendingEntryIndexes = useMemo(
    () => getPendingDynamaxAdventureIndexes(editSession),
    [editSession]
  );

  return (
    <>
      <section aria-labelledby="dynamax-adventures-heading" className="panel wide-panel">
        <div className="panel-heading">
          <ShieldCheck aria-hidden="true" size={18} />
          <h2 id="dynamax-adventures-heading">Dynamax Adventures</h2>
        </div>

        <div className="items-toolbar trainers-toolbar">
          <label className="search-box items-search">
            <Search aria-hidden="true" size={18} />
            <input
              aria-label="Search Dynamax Adventures"
              disabled={!workflow}
              onChange={(event) => onSearchChange(event.target.value)}
              placeholder="Search Dynamax Adventures"
              type="search"
              value={searchText}
            />
          </label>
          <Metric
            label="Loaded encounters"
            value={workflow ? workflow.stats.totalEncounterCount.toString() : '0'}
          />
          <Metric
            label="Single capture"
            value={workflow ? workflow.stats.singleCaptureCount.toString() : '0'}
          />
          <Metric
            label="Guaranteed IV rows"
            value={
              workflow ? workflow.stats.guaranteedPerfectIvEncounterCount.toString() : '0'
            }
          />
        </div>

        {workflow ? (
          <div className="trainers-layout">
            <div
              aria-colcount={7}
              aria-label="Dynamax Adventure encounters"
              aria-rowcount={filteredEncounters.length + 1}
              className="trainers-table"
              role="table"
            >
              <div className="trainers-row static-encounters-row trainers-row-heading" role="row">
                <span role="columnheader">Index</span>
                <span role="columnheader">Adventure</span>
                <span role="columnheader">Pokemon</span>
                <span role="columnheader">Version</span>
                <span role="columnheader">IVs</span>
                <span role="columnheader">Moves</span>
                <span role="columnheader">Source</span>
              </div>
              <VirtualTableBody
                getKey={(encounter) => encounter.entryIndex}
                items={filteredEncounters}
                renderRow={(encounter) => (
                  <button
                    className={`trainers-row static-encounters-row ${
                      selectedEncounter?.entryIndex === encounter.entryIndex
                        ? 'trainers-row-selected'
                        : ''
                    } ${
                      pendingEntryIndexes.has(encounter.entryIndex)
                        ? 'trainers-row-pending'
                        : ''
                    }`}
                    onClick={() => onSelectAdventure(encounter.entryIndex)}
                    role="row"
                    type="button"
                  >
                    <span role="cell">{encounter.entryIndex + 1}</span>
                    <span role="cell">{encounter.label}</span>
                    <span role="cell">
                      {formatSpeciesFormLabel(
                        encounter.species,
                        encounter.form,
                        encounter.speciesId
                      )}
                    </span>
                    <span role="cell">{encounter.versionLabel}</span>
                    <span role="cell">{encounter.ivSummary}</span>
                    <span role="cell">{formatDynamaxAdventureMoves(encounter)}</span>
                    <span role="cell">
                      {formatSourceLayer(encounter.provenance.sourceLayer)}
                    </span>
                  </button>
                )}
              />
            </div>

            <SelectedDynamaxAdventurePanel
              canEditDynamaxAdventures={canEditDynamaxAdventures}
              editSession={editSession}
              editableFields={workflow.editableFields}
              encounter={selectedEncounter}
              isDynamaxAdventureUpdating={isDynamaxAdventureUpdating}
              isEditStarting={isEditStarting}
              onStartEditSession={onStartEditSession}
              onUpdateDynamaxAdventureField={onUpdateDynamaxAdventureField}
              onUpdateDynamaxAdventureFields={onUpdateDynamaxAdventureFields}
            />
          </div>
        ) : (
          <p className="empty-copy">Open Dynamax Adventures from Workflows to load backend encounter data.</p>
        )}
      </section>

      <DiagnosticsSection diagnostics={workflow?.diagnostics ?? []} />
    </>
  );
}

function SelectedDynamaxAdventurePanel({
  canEditDynamaxAdventures,
  editSession,
  editableFields,
  encounter,
  isDynamaxAdventureUpdating,
  isEditStarting,
  onStartEditSession,
  onUpdateDynamaxAdventureField,
  onUpdateDynamaxAdventureFields
}: {
  canEditDynamaxAdventures: boolean;
  editSession: EditSession | null;
  editableFields: DynamaxAdventureEditableField[];
  encounter: DynamaxAdventureRecord | null;
  isDynamaxAdventureUpdating: boolean;
  isEditStarting: boolean;
  onStartEditSession: () => void;
  onUpdateDynamaxAdventureField: (entryIndex: number, field: string, value: string) => void;
  onUpdateDynamaxAdventureFields: (
    entryIndex: number,
    changes: Array<{ field: string; value: string }>
  ) => void;
}) {
  const [drafts, setDrafts] = useState<Record<string, string>>({});
  const cancelActiveEditSession = useCancelActiveEditSession();
  const adventureFields = editableFields.filter((field) =>
    dynamaxAdventureFieldNames.includes(
      field.field as (typeof dynamaxAdventureFieldNames)[number]
    )
  );
  const adventureFieldGroups = useMemo(
    () => groupNumericEditableFields(adventureFields, getPokemonInstanceFieldGroup),
    [adventureFields]
  );
  const adventureDraftSummary = useMemo(
    () =>
      getTrainerDraftSummary(
        adventureFields,
        drafts,
        encounter ? (field) => getEditableDynamaxAdventureFieldValue(encounter, field) : null
      ),
    [adventureFields, drafts, encounter]
  );
  const canSaveAdventureDrafts =
    encounter !== null &&
    editSession !== null &&
    canEditDynamaxAdventures &&
    !isDynamaxAdventureUpdating &&
    adventureDraftSummary.changedFields.length > 0 &&
    adventureDraftSummary.invalidFields.length === 0;

  useEffect(() => {
    if (!encounter) {
      setDrafts({});
      return;
    }

    setDrafts(
      Object.fromEntries(
        adventureFields.map((field) => [
          field.field,
          (getEditableDynamaxAdventureFieldValue(encounter, field.field) ?? '').toString()
        ])
      )
    );
  }, [editableFields, encounter]);

  return (
    <aside aria-label="Selected Dynamax Adventure provenance" className="trainer-inspector">
      <div className="panel-heading">
        <ShieldCheck aria-hidden="true" size={18} />
        <h3>Selected Adventure</h3>
      </div>

      {encounter ? (
        <>
          <PokemonSummaryCard
            name={formatSpeciesFormLabel(encounter.species, encounter.form, encounter.speciesId)}
            subtitle={`Adventure #${encounter.adventureIndex} | Lv. ${encounter.level}`}
            title={formatSpeciesFormLabel(encounter.species, encounter.form, encounter.speciesId)}
          />

          <dl className="item-provenance-list">
            <div>
              <dt>Encounter</dt>
              <dd>{encounter.label}</dd>
            </div>
            <div>
              <dt>Data file</dt>
              <dd>{encounter.provenance.sourceFile}</dd>
            </div>
            <div>
              <dt>Layer</dt>
              <dd>{formatSourceLayer(encounter.provenance.sourceLayer)}</dd>
            </div>
            <div>
              <dt>File state</dt>
              <dd>{formatFileState(encounter.provenance.fileState)}</dd>
            </div>
            <div>
              <dt>Pokemon</dt>
              <dd>{`${formatSpeciesFormLabel(encounter.species, encounter.form, encounter.speciesId)} Lv. ${encounter.level}`}</dd>
            </div>
            <div>
              <dt>Ball</dt>
              <dd>{encounter.ballItem}</dd>
            </div>
            <div>
              <dt>Ability</dt>
              <dd>{encounter.abilityLabel}</dd>
            </div>
            <div>
              <dt>G-Max / Version</dt>
              <dd>{`${encounter.gigantamaxLabel} / ${encounter.versionLabel}`}</dd>
            </div>
            <div>
              <dt>Shiny roll</dt>
              <dd>{encounter.shinyRollLabel}</dd>
            </div>
            <div>
              <dt>Rules</dt>
              <dd>
                {`${encounter.isSingleCapture ? 'Single capture' : 'Repeat capture'} / ${
                  encounter.isStoryProgressGated ? 'Story gated' : 'Ungated'
                }`}
              </dd>
            </div>
            <div>
              <dt>Hashes</dt>
              <dd>{`${encounter.singleCaptureFlagBlock} / ${encounter.uiMessageId}`}</dd>
            </div>
            <div>
              <dt>OT gender</dt>
              <dd>{encounter.otGenderLabel}</dd>
            </div>
            <div>
              <dt>Moves</dt>
              <dd>{formatDynamaxAdventureMoves(encounter)}</dd>
            </div>
            <div>
              <dt>IV detail</dt>
              <dd>{formatDynamaxAdventureIvs(encounter)}</dd>
            </div>
          </dl>

          <div className="trainer-edit-form">
            <div className="editable-field-groups">
              {adventureFieldGroups.map((group) => (
                <fieldset className="editable-field-group" key={group.group}>
                  <legend>{group.group}</legend>
                  <div className="editable-field-grid">
                    {group.fields.map((field) => {
                      const currentValue = getEditableDynamaxAdventureFieldValue(
                        encounter,
                        field.field
                      );
                      const draftValue = drafts[field.field] ?? '';
                      const draftState = getTrainerFieldDraftState(
                        draftValue,
                        currentValue,
                        field
                      );

                      return (
                        <GiftPokemonDraftField
                          currentValue={currentValue}
                          disabled={
                            !canEditDynamaxAdventures ||
                            editSession === null ||
                            isDynamaxAdventureUpdating
                          }
                          draftState={draftState}
                          draftValue={draftValue}
                          field={field}
                          formOptionContext={{
                            abilityOptions: encounter.abilityOptions,
                            species: encounter.species,
                            speciesId: encounter.speciesId
                          }}
                          key={field.field}
                          onChange={(value) =>
                            setDrafts((currentDrafts) => ({
                              ...currentDrafts,
                              [field.field]: value
                            }))
                          }
                        />
                      );
                    })}
                  </div>
                </fieldset>
              ))}
            </div>
            {editSession ? (
              <div className="draft-action-row">
                <button
                  className="primary-button"
                  disabled={!canSaveAdventureDrafts}
                  onClick={() =>
                    onUpdateDynamaxAdventureFields(
                      encounter.entryIndex,
                      adventureDraftSummary.changedFields.map((change) => ({
                        field: change.field,
                        value: change.value
                      }))
                    )
                  }
                  type="button"
                >
                  <Save aria-hidden="true" size={16} />
                  <span>{isDynamaxAdventureUpdating ? 'Saving' : 'Save Adventure'}</span>
                </button>
                <button
                  className="danger-button"
                  disabled={
                    isDynamaxAdventureUpdating
                  }
                  onClick={() => {
                    setDrafts(createTrainerDrafts(adventureFields, (field) =>
                      getEditableDynamaxAdventureFieldValue(encounter, field)
                    ));
                    cancelActiveEditSession();
                  }}
                  type="button"
                >
                  <X aria-hidden="true" size={16} />
                  <span>Cancel</span>
                </button>
                <span className="draft-action-summary">
                  {formatDraftSummary(adventureDraftSummary)}
                </span>
              </div>
            ) : null}

            {!editSession ? (
              <button
                className="secondary-button"
                disabled={!canEditDynamaxAdventures || isEditStarting}
                onClick={onStartEditSession}
                type="button"
              >
                <Pencil aria-hidden="true" size={16} />
                <span>{isEditStarting ? 'Starting' : 'Edit'}</span>
              </button>
            ) : null}
          </div>
        </>
      ) : (
        <p className="empty-copy">No Adventure encounter selected.</p>
      )}
    </aside>
  );
}

function DynamaxAdventureFieldInput({
  disabled,
  draftValue,
  field,
  formOptionContext,
  onChange
}: {
  disabled: boolean;
  draftValue: string;
  field: DynamaxAdventureEditableField;
  formOptionContext?: SpeciesFormOptionContext;
  onChange: (value: string) => void;
}) {
  const options = getContextualFieldOptions(field, formOptionContext);

  if (options.length > 0) {
    return (
      <SearchableOptionInput
        ariaLabel={field.label}
        disabled={disabled}
        title={getEditableFieldHelp(field)}
        onChange={onChange}
        options={addDraftFallbackOption(
          options,
          draftValue,
          draftValue === '' ? 'Custom value' : draftValue
        )}
        value={draftValue}
      />
    );
  }

  return (
    <input
      aria-label={field.label}
      disabled={disabled}
      max={field.maximumValue ?? undefined}
      min={field.minimumValue ?? undefined}
      title={getEditableFieldHelp(field)}
      onChange={(event) => onChange(event.target.value)}
      type="number"
      value={draftValue}
    />
  );
}

function StaticEncountersSection({
  editSession,
  isEditStarting,
  isStaticEncounterUpdating,
  onSearchChange,
  onSelectEncounter,
  onStartEditSession,
  onUpdateStaticEncounterField,
  onUpdateStaticEncounterFields,
  searchText,
  selectedEncounterIndex,
  workflow
}: {
  editSession: EditSession | null;
  isEditStarting: boolean;
  isStaticEncounterUpdating: boolean;
  onSearchChange: (searchText: string) => void;
  onSelectEncounter: (encounterIndex: number | null) => void;
  onStartEditSession: () => void;
  onUpdateStaticEncounterField: (encounterIndex: number, field: string, value: string) => void;
  onUpdateStaticEncounterFields: (
    encounterIndex: number,
    changes: Array<{ field: string; value: string }>
  ) => void;
  searchText: string;
  selectedEncounterIndex: number | null;
  workflow: StaticEncountersWorkflow | null;
}) {
  const encounters = workflow?.encounters ?? [];
  const filteredEncounters = useMemo(
    () => filterStaticEncounters(encounters, searchText),
    [encounters, searchText]
  );
  const selectedEncounter = useMemo(
    () =>
      encounters.find((encounter) => encounter.encounterIndex === selectedEncounterIndex) ??
      filteredEncounters[0] ??
      null,
    [encounters, filteredEncounters, selectedEncounterIndex]
  );
  const canEditStaticEncounters = workflow?.summary.availability === 'available';
  const pendingEncounterIndexes = useMemo(
    () => getPendingStaticEncounterIndexes(editSession),
    [editSession]
  );

  return (
    <>
      <section aria-labelledby="static-encounters-heading" className="panel wide-panel">
        <div className="panel-heading">
          <MapPin aria-hidden="true" size={18} />
          <h2 id="static-encounters-heading">Static Encounters</h2>
        </div>

        <div className="items-toolbar trainers-toolbar">
          <label className="search-box items-search">
            <Search aria-hidden="true" size={18} />
            <input
              aria-label="Search static encounters"
              disabled={!workflow}
              onChange={(event) => onSearchChange(event.target.value)}
              placeholder="Search static encounters"
              type="search"
              value={searchText}
            />
          </label>
          <Metric
            label="Loaded encounters"
            value={workflow ? workflow.stats.totalEncounterCount.toString() : '0'}
          />
          <Metric
            label="Gigantamax"
            value={workflow ? workflow.stats.gigantamaxEncounterCount.toString() : '0'}
          />
          <Metric
            label="Fixed IV rows"
            value={workflow ? workflow.stats.fixedIvEncounterCount.toString() : '0'}
          />
        </div>

        {workflow ? (
          <div className="trainers-layout">
            <div
              aria-colcount={7}
              aria-label="Static Encounters"
              aria-rowcount={filteredEncounters.length + 1}
              className="trainers-table"
              role="table"
            >
              <div className="trainers-row static-encounters-row trainers-row-heading" role="row">
                <span role="columnheader">Index</span>
                <span role="columnheader">Encounter</span>
                <span role="columnheader">Species</span>
                <span role="columnheader">Level</span>
                <span role="columnheader">Scenario</span>
                <span role="columnheader">IVs</span>
                <span role="columnheader">Source</span>
              </div>
              <VirtualTableBody
                getKey={(encounter) => encounter.encounterIndex}
                items={filteredEncounters}
                renderRow={(encounter) => (
                  <button
                    className={`trainers-row static-encounters-row ${
                      selectedEncounter?.encounterIndex === encounter.encounterIndex
                        ? 'trainers-row-selected'
                        : ''
                    } ${
                      pendingEncounterIndexes.has(encounter.encounterIndex)
                        ? 'trainers-row-pending'
                        : ''
                    }`}
                    onClick={() => onSelectEncounter(encounter.encounterIndex)}
                    role="row"
                    type="button"
                  >
                    <span role="cell">{encounter.encounterIndex + 1}</span>
                    <span role="cell">{encounter.label}</span>
                    <span role="cell">{encounter.species}</span>
                    <span role="cell">{encounter.level}</span>
                    <span role="cell">{encounter.encounterScenarioLabel}</span>
                    <span role="cell">{encounter.ivSummary}</span>
                    <span role="cell">{formatSourceLayer(encounter.provenance.sourceLayer)}</span>
                  </button>
                )}
              />
            </div>

            <SelectedStaticEncounterPanel
              canEditStaticEncounters={canEditStaticEncounters}
              editSession={editSession}
              editableFields={workflow.editableFields}
              encounter={selectedEncounter}
              isEditStarting={isEditStarting}
              isStaticEncounterUpdating={isStaticEncounterUpdating}
              onStartEditSession={onStartEditSession}
              onUpdateStaticEncounterField={onUpdateStaticEncounterField}
              onUpdateStaticEncounterFields={onUpdateStaticEncounterFields}
            />
          </div>
        ) : (
          <p className="empty-copy">Open Static Encounters from Workflows to load backend encounter data.</p>
        )}
      </section>

      <DiagnosticsSection diagnostics={workflow?.diagnostics ?? []} />
    </>
  );
}

function SelectedStaticEncounterPanel({
  canEditStaticEncounters,
  editSession,
  editableFields,
  encounter,
  isEditStarting,
  isStaticEncounterUpdating,
  onStartEditSession,
  onUpdateStaticEncounterField,
  onUpdateStaticEncounterFields
}: {
  canEditStaticEncounters: boolean;
  editSession: EditSession | null;
  editableFields: StaticEncounterEditableField[];
  encounter: StaticEncounterRecord | null;
  isEditStarting: boolean;
  isStaticEncounterUpdating: boolean;
  onStartEditSession: () => void;
  onUpdateStaticEncounterField: (encounterIndex: number, field: string, value: string) => void;
  onUpdateStaticEncounterFields: (
    encounterIndex: number,
    changes: Array<{ field: string; value: string }>
  ) => void;
}) {
  const [encounterDrafts, setEncounterDrafts] = useState<Record<string, string>>({});
  const cancelActiveEditSession = useCancelActiveEditSession();
  const encounterFields = editableFields.filter((field) =>
    staticEncounterFieldNames.includes(
      field.field as (typeof staticEncounterFieldNames)[number]
    )
  );
  const encounterFieldGroups = useMemo(
    () => groupNumericEditableFields(encounterFields, getPokemonInstanceFieldGroup),
    [encounterFields]
  );
  const encounterDraftSummary = useMemo(
    () =>
      getTrainerDraftSummary(
        encounterFields,
        encounterDrafts,
        encounter ? (field) => getEditableStaticEncounterFieldValue(encounter, field) : null
      ),
    [encounter, encounterDrafts, encounterFields]
  );
  const canSaveEncounterDrafts =
    encounter !== null &&
    editSession !== null &&
    canEditStaticEncounters &&
    !isStaticEncounterUpdating &&
    encounterDraftSummary.changedFields.length > 0 &&
    encounterDraftSummary.invalidFields.length === 0;

  useEffect(() => {
    if (!encounter) {
      setEncounterDrafts({});
      return;
    }

    setEncounterDrafts(
      Object.fromEntries(
        encounterFields.map((field) => [
          field.field,
          (getEditableStaticEncounterFieldValue(encounter, field.field) ?? '').toString()
        ])
      )
    );
  }, [editableFields, encounter]);

  return (
    <aside aria-label="Selected static encounter provenance" className="trainer-inspector">
      <div className="panel-heading">
        <ShieldCheck aria-hidden="true" size={18} />
        <h3>Selected Static Encounter</h3>
      </div>

      {encounter ? (
        <>
          <PokemonSummaryCard
            name={formatSpeciesFormLabel(encounter.species, encounter.form, encounter.speciesId)}
            subtitle={`Static #${encounter.encounterIndex} | Lv. ${encounter.level}`}
            title={formatSpeciesFormLabel(encounter.species, encounter.form, encounter.speciesId)}
          />

          <dl className="item-provenance-list">
            <div>
              <dt>Encounter</dt>
              <dd>{encounter.label}</dd>
            </div>
            <div>
              <dt>Encounter ID</dt>
              <dd>{encounter.encounterId}</dd>
            </div>
            <div>
              <dt>Data file</dt>
              <dd>{encounter.provenance.sourceFile}</dd>
            </div>
            <div>
              <dt>Layer</dt>
              <dd>{formatSourceLayer(encounter.provenance.sourceLayer)}</dd>
            </div>
            <div>
              <dt>File state</dt>
              <dd>{formatFileState(encounter.provenance.fileState)}</dd>
            </div>
            <div>
              <dt>Scenario</dt>
              <dd>{encounter.encounterScenarioLabel}</dd>
            </div>
            <div>
              <dt>Held item</dt>
              <dd>{encounter.heldItem ?? 'None'}</dd>
            </div>
            <div>
              <dt>Moves</dt>
              <dd>{formatStaticEncounterMoves(encounter)}</dd>
            </div>
            <div>
              <dt>EV detail</dt>
              <dd>{formatStaticEncounterStats(encounter.evs)}</dd>
            </div>
            <div>
              <dt>IV detail</dt>
              <dd>{formatStaticEncounterIvs(encounter)}</dd>
            </div>
          </dl>

          <div className="trainer-edit-form">
            <div className="editable-field-groups">
              {encounterFieldGroups.map((group) => (
                <fieldset className="editable-field-group" key={group.group}>
                  <legend>{group.group}</legend>
                  <div className="editable-field-grid">
                    {group.fields.map((field) => {
                      const currentValue = getEditableStaticEncounterFieldValue(
                        encounter,
                        field.field
                      );
                      const draftValue = encounterDrafts[field.field] ?? '';
                      const draftState = getTrainerFieldDraftState(
                        draftValue,
                        currentValue,
                        field
                      );

                      return (
                        <GiftPokemonDraftField
                          currentValue={currentValue}
                          disabled={
                            !canEditStaticEncounters ||
                            editSession === null ||
                            isStaticEncounterUpdating
                          }
                          draftState={draftState}
                          draftValue={draftValue}
                          field={field}
                          formOptionContext={{
                            abilityOptions: encounter.abilityOptions,
                            species: encounter.species,
                            speciesId: encounter.speciesId
                          }}
                          key={field.field}
                          onChange={(value) =>
                            setEncounterDrafts((currentDrafts) => ({
                              ...currentDrafts,
                              [field.field]: value
                            }))
                          }
                        />
                      );
                    })}
                  </div>
                </fieldset>
              ))}
            </div>
            {editSession ? (
              <div className="draft-action-row">
                <button
                  className="primary-button"
                  disabled={!canSaveEncounterDrafts}
                  onClick={() =>
                    onUpdateStaticEncounterFields(
                      encounter.encounterIndex,
                      encounterDraftSummary.changedFields.map((change) => ({
                        field: change.field,
                        value: change.value
                      }))
                    )
                  }
                  type="button"
                >
                  <Save aria-hidden="true" size={16} />
                  <span>{isStaticEncounterUpdating ? 'Saving' : 'Save Encounter'}</span>
                </button>
                <button
                  className="danger-button"
                  disabled={
                    isStaticEncounterUpdating
                  }
                  onClick={() => {
                    setEncounterDrafts(createTrainerDrafts(encounterFields, (field) =>
                      getEditableStaticEncounterFieldValue(encounter, field)
                    ));
                    cancelActiveEditSession();
                  }}
                  type="button"
                >
                  <X aria-hidden="true" size={16} />
                  <span>Cancel</span>
                </button>
                <span className="draft-action-summary">
                  {formatDraftSummary(encounterDraftSummary)}
                </span>
              </div>
            ) : null}

            {!editSession ? (
              <button
                className="secondary-button"
                disabled={!canEditStaticEncounters || isEditStarting}
                onClick={onStartEditSession}
                type="button"
              >
                <Pencil aria-hidden="true" size={16} />
                <span>{isEditStarting ? 'Starting' : 'Edit'}</span>
              </button>
            ) : null}
          </div>
        </>
      ) : (
        <p className="empty-copy">No static encounter selected.</p>
      )}
    </aside>
  );
}

function StaticEncounterFieldInput({
  disabled,
  draftValue,
  field,
  formOptionContext,
  onChange
}: {
  disabled: boolean;
  draftValue: string;
  field: StaticEncounterEditableField;
  formOptionContext?: SpeciesFormOptionContext;
  onChange: (value: string) => void;
}) {
  const options = getContextualFieldOptions(field, formOptionContext);

  if (options.length > 0) {
    return (
      <SearchableOptionInput
        ariaLabel={field.label}
        disabled={disabled}
        title={getEditableFieldHelp(field)}
        onChange={onChange}
        options={addDraftFallbackOption(
          options,
          draftValue,
          draftValue === '' ? 'Custom fixed IVs' : draftValue
        )}
        value={draftValue}
      />
    );
  }

  return (
    <input
      aria-label={field.label}
      disabled={disabled}
      max={field.maximumValue ?? undefined}
      min={field.minimumValue ?? undefined}
      title={getEditableFieldHelp(field)}
      onChange={(event) => onChange(event.target.value)}
      type="number"
      value={draftValue}
    />
  );
}

function ShopsSection({
  editSession,
  isEditStarting,
  isShopUpdating,
  onOpenItem,
  onSearchChange,
  onSelectShop,
  onStartEditSession,
  onUpdateShopInventoryItem,
  searchText,
  selectedShopId,
  workflow
}: {
  editSession: EditSession | null;
  isEditStarting: boolean;
  isShopUpdating: boolean;
  onOpenItem: (itemId: number) => void;
  onSearchChange: (searchText: string) => void;
  onSelectShop: (shopId: string | null) => void;
  onStartEditSession: () => void;
  onUpdateShopInventoryItem: (
    shopId: string,
    slot: number,
    field: string,
    value: string
  ) => void;
  searchText: string;
  selectedShopId: string | null;
  workflow: ShopsWorkflow | null;
}) {
  const [selectedSlot, setSelectedSlot] = useState<number | null>(null);
  const filteredShops = filterShops(workflow?.shops ?? [], searchText);
  const selectedShop =
    workflow?.shops.find((shop) => shop.shopId === selectedShopId) ?? filteredShops[0] ?? null;
  const selectedInventoryItem =
    selectedShop?.inventory.find((item) => item.slot === selectedSlot) ??
    selectedShop?.inventory[0] ??
    null;
  const canEditShops = workflow?.summary.availability === 'available';
  const pendingShopIds = getPendingShopIds(editSession);

  useEffect(() => {
    if (!selectedShop) {
      setSelectedSlot(null);
      return;
    }

    const hasSelectedSlot = selectedShop.inventory.some((item) => item.slot === selectedSlot);
    if (!hasSelectedSlot) {
      setSelectedSlot(selectedShop.inventory[0]?.slot ?? null);
    }
  }, [selectedShop?.inventory, selectedShop?.shopId, selectedSlot]);

  return (
    <>
      <section aria-labelledby="shops-heading" className="panel wide-panel">
        <div className="panel-heading">
          <ListChecks aria-hidden="true" size={18} />
          <h2 id="shops-heading">Shops</h2>
        </div>

        <div className="items-toolbar shops-toolbar">
          <label className="search-box items-search">
            <Search aria-hidden="true" size={18} />
            <input
              aria-label="Search shops"
              disabled={!workflow}
              onChange={(event) => onSearchChange(event.target.value)}
              placeholder="Search shops"
              type="search"
              value={searchText}
            />
          </label>
          <Metric
            label="Loaded shops"
            value={workflow ? workflow.stats.totalShopCount.toString() : '0'}
          />
          <Metric
            label="Inventory rows"
            value={workflow ? workflow.stats.totalInventoryItemCount.toString() : '0'}
          />
          <Metric
            label="Pending changes"
            value={(editSession?.pendingEdits.length ?? 0).toString()}
          />
        </div>

        {workflow ? (
          <div className="shops-layout">
            <div className="shops-table" role="table" aria-label="Shops">
              <div className="shops-row shops-row-heading" role="row">
                <span role="columnheader">Type</span>
                <span role="columnheader">Name</span>
                <span role="columnheader">Inventory</span>
                <span role="columnheader">Location</span>
                <span role="columnheader">Items</span>
                <span role="columnheader">Summary</span>
              </div>
              {filteredShops.map((shop) => (
                <button
                  className={`shops-row ${
                    selectedShop?.shopId === shop.shopId ? 'shops-row-selected' : ''
                  } ${pendingShopIds.has(shop.shopId) ? 'shops-row-pending' : ''}`}
                  key={shop.shopId}
                  onClick={() => onSelectShop(shop.shopId)}
                  role="row"
                  type="button"
                >
                  <span role="cell">{shop.kind}</span>
                  <span role="cell">{shop.name}</span>
                  <span role="cell">{shop.inventoryLabel}</span>
                  <span role="cell">{shop.location}</span>
                  <span role="cell">{shop.inventory.length}</span>
                  <span role="cell">{shop.inventorySummary}</span>
                </button>
              ))}
            </div>

            <SelectedShopPanel
              canEditShops={canEditShops}
              editSession={editSession}
              editableFields={workflow.editableFields}
              inventoryItem={selectedInventoryItem}
              isEditStarting={isEditStarting}
              isShopUpdating={isShopUpdating}
              onOpenItem={onOpenItem}
              onSelectSlot={setSelectedSlot}
              onStartEditSession={onStartEditSession}
              onUpdateShopInventoryItem={onUpdateShopInventoryItem}
              selectedSlot={selectedSlot}
              shop={selectedShop}
            />
          </div>
        ) : (
          <p className="empty-copy">Open Shops from Workflows to load backend shop data.</p>
        )}
      </section>

      <DiagnosticsSection diagnostics={workflow?.diagnostics ?? []} />
    </>
  );
}

function SelectedShopPanel({
  canEditShops,
  editSession,
  editableFields,
  isEditStarting,
  isShopUpdating,
  onOpenItem,
  onSelectSlot,
  onStartEditSession,
  onUpdateShopInventoryItem,
  selectedSlot,
  shop
}: {
  canEditShops: boolean;
  editSession: EditSession | null;
  editableFields: ShopEditableField[];
  inventoryItem: ShopInventoryRecord | null;
  isEditStarting: boolean;
  isShopUpdating: boolean;
  onOpenItem: (itemId: number) => void;
  onSelectSlot: (slot: number | null) => void;
  onStartEditSession: () => void;
  onUpdateShopInventoryItem: (
    shopId: string,
    slot: number,
    field: string,
    value: string
  ) => void;
  selectedSlot: number | null;
  shop: ShopRecord | null;
}) {
  const [itemIdDrafts, setItemIdDrafts] = useState<Record<number, string>>({});
  const cancelActiveEditSession = useCancelActiveEditSession();
  const itemIdField = editableFields.find((field) => field.field === shopItemIdFieldName);
  const itemIdOptions = itemIdField?.options ?? [];
  const hasItemIdOptions = itemIdOptions.length > 0;

  useEffect(() => {
    setItemIdDrafts(
      Object.fromEntries(shop?.inventory.map((item) => [item.slot, item.itemId.toString()]) ?? [])
    );
  }, [shop?.inventory, shop?.shopId]);

  const changedSlotCount =
    shop?.inventory.filter((item) => itemIdDrafts[item.slot] !== item.itemId.toString()).length ?? 0;

  return (
    <aside aria-label="Selected shop provenance" className="shop-inspector">
      <div className="panel-heading">
        <ShieldCheck aria-hidden="true" size={18} />
        <h3>Selected Shop</h3>
      </div>

      {shop ? (
        <>
          <dl className="item-provenance-list">
            <div>
              <dt>Name</dt>
              <dd>{shop.name}</dd>
            </div>
            <div>
              <dt>Inventory</dt>
              <dd>{shop.inventoryLabel}</dd>
            </div>
            <div>
              <dt>Type</dt>
              <dd>{shop.kind}</dd>
            </div>
            <div>
              <dt>Hash</dt>
              <dd>{shop.sourceHash}</dd>
            </div>
            <div>
              <dt>Summary</dt>
              <dd>{shop.inventorySummary}</dd>
            </div>
            <div>
              <dt>Source file</dt>
              <dd>{shop.provenance.sourceFile}</dd>
            </div>
            <div>
              <dt>Layer</dt>
              <dd>{formatSourceLayer(shop.provenance.sourceLayer)}</dd>
            </div>
            <div>
              <dt>File state</dt>
              <dd>{formatFileState(shop.provenance.fileState)}</dd>
            </div>
          </dl>

          <div className="shop-edit-form">
            <div className="shop-inventory-header">
              <strong>Inventory</strong>
              <span className="draft-action-summary">
                {changedSlotCount} changed / {shop.inventory.length} slots
              </span>
            </div>

            {!editSession ? (
              <button
                className="secondary-button"
                disabled={!canEditShops || isEditStarting || shop.inventory.length === 0}
                onClick={onStartEditSession}
                type="button"
              >
                <Pencil aria-hidden="true" size={16} />
                <span>{isEditStarting ? 'Starting' : 'Edit'}</span>
              </button>
            ) : null}

            {shop.inventory.length > 0 ? (
              <div className="shop-inventory-editor-grid">
                <div className="shop-inventory-editor-row shop-inventory-editor-heading">
                  <span>Slot</span>
                  <span>Item</span>
                  <span>Price</span>
                  <span>Stock</span>
                  <span>Actions</span>
                </div>
                {shop.inventory.map((item) => {
                  const draftValue = itemIdDrafts[item.slot] ?? item.itemId.toString();
                  const draftState = getIntegerDraftState(draftValue, item.itemId, itemIdField);
                  const canSubmitSlot =
                    editSession !== null &&
                    draftState.canSubmit &&
                    draftState.parsedValue !== null &&
                    !isShopUpdating;

                  return (
                    <div
                      className={`shop-inventory-editor-row ${
                        item.slot === selectedSlot ? 'shop-inventory-editor-row-selected' : ''
                      }`}
                      key={item.slot}
                      onClick={() => onSelectSlot(item.slot)}
                    >
                      <span className="shop-slot-index">#{item.slot}</span>
                      <label className="path-field shop-inventory-item-field">
                        <span>{itemIdField?.label ?? 'Item ID'}</span>
                        {hasItemIdOptions ? (
                          <SearchableOptionInput
                            ariaLabel={`Shop slot ${item.slot} item`}
                            disabled={!canEditShops || editSession === null || isShopUpdating}
                            onChange={(value) =>
                              setItemIdDrafts((currentDrafts) => ({
                                ...currentDrafts,
                                [item.slot]: value
                              }))
                            }
                            options={addDraftFallbackOption(
                              itemIdOptions,
                              draftValue,
                              formatShopItemFallbackOption(draftValue)
                            )}
                            value={draftValue}
                          />
                        ) : (
                          <input
                            aria-label={`Shop slot ${item.slot} item`}
                            disabled={!canEditShops || editSession === null || isShopUpdating}
                            max={itemIdField?.maximumValue ?? undefined}
                            min={itemIdField?.minimumValue ?? undefined}
                            onChange={(event) =>
                              setItemIdDrafts((currentDrafts) => ({
                                ...currentDrafts,
                                [item.slot]: event.target.value
                              }))
                            }
                            type="number"
                            value={draftValue}
                          />
                        )}
                      </label>
                      <label className="path-field shop-read-only-field">
                        <span>{shop.currency}</span>
                        <input aria-label={`Shop slot ${item.slot} price`} disabled value={item.price} />
                      </label>
                      <label className="path-field shop-read-only-field">
                        <span>Stock</span>
                        <input
                          aria-label={`Shop slot ${item.slot} stock`}
                          disabled
                          value={item.stockLimit ?? 'None'}
                        />
                      </label>
                      <div className="shop-inventory-row-actions">
                        {item.isKnownItem ? (
                          <button
                            aria-label={`Open ${item.itemName} in Items`}
                            className="secondary-button compact-button shop-item-link"
                            onClick={(event) => {
                              event.stopPropagation();
                              onOpenItem(item.itemId);
                            }}
                            title="Open in Items"
                            type="button"
                          >
                            <ExternalLink aria-hidden="true" size={14} />
                            <span>Open</span>
                          </button>
                        ) : (
                          <span className="path-status-muted">Missing metadata</span>
                        )}
                        {editSession ? (
                          <>
                            <button
                              aria-label={`Save shop slot ${item.slot}`}
                              className="primary-button compact-button"
                              disabled={!canSubmitSlot}
                              onClick={(event) => {
                                event.stopPropagation();
                                onUpdateShopInventoryItem(
                                  shop.shopId,
                                  item.slot,
                                  shopItemIdFieldName,
                                  draftState.parsedValue!.toString()
                                );
                              }}
                              type="button"
                            >
                              <Save aria-hidden="true" size={14} />
                              <span>{isShopUpdating ? 'Saving' : 'Save'}</span>
                            </button>
                            <button
                              aria-label={`Cancel shop slot ${item.slot}`}
                              className="danger-button compact-button"
                              disabled={isShopUpdating}
                              onClick={(event) => {
                                event.stopPropagation();
                                setItemIdDrafts((currentDrafts) => ({
                                  ...currentDrafts,
                                  [item.slot]: item.itemId.toString()
                                }));
                                cancelActiveEditSession();
                              }}
                              type="button"
                            >
                              <X aria-hidden="true" size={14} />
                              <span>Cancel</span>
                            </button>
                          </>
                        ) : null}
                      </div>
                    </div>
                  );
                })}
              </div>
            ) : (
              <p className="empty-copy">No inventory slots.</p>
            )}
          </div>
        </>
      ) : (
        <p className="empty-copy">No shop selected.</p>
      )}
    </aside>
  );
}

function formatShopItemFallbackOption(value: string) {
  return `Item ${value}`;
}

function EncountersSection({
  editSession,
  isEditStarting,
  isEncounterUpdating,
  onApplyNormalToAllWeather,
  onSearchChange,
  onSelectTable,
  onStartEditSession,
  onUpdateEncounterSlotField,
  onUpdateEncounterSlotFields,
  searchText,
  selectedTableId,
  workflow
}: {
  editSession: EditSession | null;
  isEditStarting: boolean;
  isEncounterUpdating: boolean;
  onApplyNormalToAllWeather: (sourceTableId: string, targetTableIds: string[]) => void;
  onSearchChange: (searchText: string) => void;
  onSelectTable: (tableId: string | null) => void;
  onStartEditSession: () => void;
  onUpdateEncounterSlotField: (
    tableId: string,
    slot: number,
    field: string,
    value: string
  ) => void;
  onUpdateEncounterSlotFields: (
    tableId: string,
    slot: number,
    changes: Array<{ field: string; value: string }>
  ) => void;
  searchText: string;
  selectedTableId: string | null;
  workflow: EncountersWorkflow | null;
}) {
  const [selectedSlot, setSelectedSlot] = useState<number | null>(null);
  const filteredTables = filterEncounterTables(workflow?.tables ?? [], searchText);
  const selectedTable =
    workflow?.tables.find((table) => table.tableId === selectedTableId) ??
    filteredTables[0] ??
    null;
  const selectedEncounterSlot =
    selectedTable?.slots.find((slot) => slot.slot === selectedSlot) ??
    selectedTable?.slots[0] ??
    null;
  const conditionTabs = useMemo(
    () =>
      selectedTable && workflow
        ? buildEncounterConditionTabs(selectedTable, workflow.tables)
        : [],
    [selectedTable, workflow]
  );
  const canEditEncounters = workflow?.summary.availability === 'available';
  const pendingEncounterTableIds = getPendingEncounterTableIds(editSession);

  useEffect(() => {
    if (!selectedTable) {
      setSelectedSlot(null);
      return;
    }

    const hasSelectedSlot = selectedTable.slots.some((slot) => slot.slot === selectedSlot);
    if (!hasSelectedSlot) {
      setSelectedSlot(selectedTable.slots[0]?.slot ?? null);
    }
  }, [selectedSlot, selectedTable?.slots, selectedTable?.tableId]);

  return (
    <>
      <section aria-labelledby="encounters-heading" className="panel wide-panel">
        <div className="panel-heading">
          <Layers aria-hidden="true" size={18} />
          <h2 id="encounters-heading">Encounters and Wild Data</h2>
        </div>

        <div className="items-toolbar encounters-toolbar">
          <label className="search-box items-search">
            <Search aria-hidden="true" size={18} />
            <input
              aria-label="Search encounters"
              disabled={!workflow}
              onChange={(event) => onSearchChange(event.target.value)}
              placeholder="Search encounters"
              type="search"
              value={searchText}
            />
          </label>
          <Metric
            label="Loaded tables"
            value={workflow ? workflow.stats.totalTableCount.toString() : '0'}
          />
          <Metric
            label="Encounter slots"
            value={workflow ? workflow.stats.totalSlotCount.toString() : '0'}
          />
          <Metric
            label="Pending changes"
            value={(editSession?.pendingEdits.length ?? 0).toString()}
          />
        </div>

        {workflow ? (
          <div className="encounters-layout">
            <div className="encounters-table" role="table" aria-label="Encounter tables">
              <div className="encounters-row encounters-row-heading" role="row">
                <span role="columnheader">Location</span>
                <span role="columnheader">Game</span>
                <span role="columnheader">Area</span>
                <span role="columnheader">Weather</span>
                <span role="columnheader">Slots</span>
                <span role="columnheader">Member</span>
              </div>
              {filteredTables.map((table) => (
                <button
                  className={`encounters-row ${
                    selectedTable?.tableId === table.tableId ? 'encounters-row-selected' : ''
                  } ${
                    pendingEncounterTableIds.has(table.tableId) ? 'encounters-row-pending' : ''
                  }`}
                  key={table.tableId}
                  onClick={() => onSelectTable(table.tableId)}
                  role="row"
                  type="button"
                >
                  <span role="cell">{table.location}</span>
                  <span role="cell">{table.gameVersion}</span>
                  <span role="cell">{table.area}</span>
                  <span role="cell">{table.encounterType}</span>
                  <span role="cell">{table.slots.length}</span>
                  <span role="cell">{table.archiveMember}</span>
                </button>
              ))}
            </div>

            <SelectedEncounterPanel
              canEditEncounters={canEditEncounters}
              editSession={editSession}
              editableFields={workflow.editableFields}
              encounterSlot={selectedEncounterSlot}
              conditionTabs={conditionTabs}
              isEditStarting={isEditStarting}
              isEncounterUpdating={isEncounterUpdating}
              onApplyNormalToAllWeather={onApplyNormalToAllWeather}
              onSelectSlot={setSelectedSlot}
              onSelectTable={onSelectTable}
              onStartEditSession={onStartEditSession}
              onUpdateEncounterSlotField={onUpdateEncounterSlotField}
              onUpdateEncounterSlotFields={onUpdateEncounterSlotFields}
              selectedSlot={selectedSlot}
              table={selectedTable}
            />
          </div>
        ) : (
          <p className="empty-copy">Open Encounters from Workflows to load backend wild data.</p>
        )}
      </section>

      <DiagnosticsSection diagnostics={workflow?.diagnostics ?? []} />
    </>
  );
}

function SelectedEncounterPanel({
  canEditEncounters,
  conditionTabs,
  editSession,
  editableFields,
  encounterSlot,
  isEditStarting,
  isEncounterUpdating,
  onApplyNormalToAllWeather,
  onSelectSlot,
  onSelectTable,
  onStartEditSession,
  onUpdateEncounterSlotField,
  onUpdateEncounterSlotFields,
  selectedSlot,
  table
}: {
  canEditEncounters: boolean;
  conditionTabs: EncounterConditionTab[];
  editSession: EditSession | null;
  editableFields: EncounterEditableField[];
  encounterSlot: EncounterSlotRecord | null;
  isEditStarting: boolean;
  isEncounterUpdating: boolean;
  onApplyNormalToAllWeather: (sourceTableId: string, targetTableIds: string[]) => void;
  onSelectSlot: (slot: number | null) => void;
  onSelectTable: (tableId: string | null) => void;
  onStartEditSession: () => void;
  onUpdateEncounterSlotField: (
    tableId: string,
    slot: number,
    field: string,
    value: string
  ) => void;
  onUpdateEncounterSlotFields: (
    tableId: string,
    slot: number,
    changes: Array<{ field: string; value: string }>
  ) => void;
  selectedSlot: number | null;
  table: EncounterTableRecord | null;
}) {
  const [drafts, setDrafts] = useState<Record<string, string>>({});
  const cancelActiveEditSession = useCancelActiveEditSession();
  const encounterFields = useMemo(
    () =>
      editableFields.map((field) =>
        toNumericEditableControlField(
          field,
          encounterSlot
            ? getContextualFieldOptions(field, {
                species: encounterSlot.species,
                speciesId: encounterSlot.speciesId
              })
            : undefined
        )
      ),
    [editableFields, encounterSlot?.species, encounterSlot?.speciesId]
  );
  const encounterFieldGroups = useMemo(
    () => groupNumericEditableFields(encounterFields, getEncounterEditableFieldGroup),
    [encounterFields]
  );
  const encounterDraftDefaults = useMemo(
    () =>
      encounterSlot
        ? createTrainerDrafts(encounterFields, (field) =>
            getEditableEncounterFieldValue(encounterSlot, field)
          )
        : {},
    [
      encounterFields,
      encounterSlot?.form,
      encounterSlot?.levelMax,
      encounterSlot?.levelMin,
      encounterSlot?.slot,
      encounterSlot?.speciesId,
      encounterSlot?.weight,
      table?.tableId
    ]
  );
  const encounterDraftSummary = useMemo(
    () =>
      getTrainerDraftSummary(
        encounterFields,
        drafts,
        encounterSlot ? (field) => getEditableEncounterFieldValue(encounterSlot, field) : null
      ),
    [drafts, encounterFields, encounterSlot]
  );
  const canSaveEncounterDrafts =
    table !== null &&
    encounterSlot !== null &&
    editSession !== null &&
    canEditEncounters &&
    !isEncounterUpdating &&
    encounterDraftSummary.changedFields.length > 0 &&
    encounterDraftSummary.invalidFields.length === 0;
  const encounterProbabilityTotal =
    table?.slots.slice(0, 10).reduce((total, slot) => total + slot.weight, 0) ?? 0;
  const targetWeatherTableIds = conditionTabs
    .filter((tab) => tab.isWeatherCopyTarget && tab.tableId !== null)
    .map((tab) => tab.tableId!);
  const canApplyNormalToAllWeather =
    table !== null &&
    table.encounterType === 'Normal' &&
    editSession !== null &&
    canEditEncounters &&
    !isEncounterUpdating &&
    targetWeatherTableIds.length > 0;

  useEffect(() => {
    setDrafts(encounterDraftDefaults);
  }, [encounterDraftDefaults]);

  return (
    <aside aria-label="Selected encounter provenance" className="encounter-inspector">
      <div className="panel-heading">
        <ShieldCheck aria-hidden="true" size={18} />
        <h3>Selected Encounter</h3>
      </div>

      {table ? (
        <>
          <dl className="item-provenance-list">
            <div>
              <dt>Location</dt>
              <dd>{table.location}</dd>
            </div>
            <div>
              <dt>Table</dt>
              <dd>{table.tableId}</dd>
            </div>
            <div>
              <dt>Archive member</dt>
              <dd>{table.archiveMember}</dd>
            </div>
            <div>
              <dt>Source file</dt>
              <dd>{table.provenance.sourceFile}</dd>
            </div>
            <div>
              <dt>Layer</dt>
              <dd>{formatSourceLayer(table.provenance.sourceLayer)}</dd>
            </div>
            <div>
              <dt>File state</dt>
              <dd>{formatFileState(table.provenance.fileState)}</dd>
            </div>
          </dl>

          <div className="encounter-edit-form">
            {conditionTabs.length > 0 ? (
              <div
                className="encounter-condition-tabs"
                role="tablist"
                aria-label="Encounter conditions"
              >
                {conditionTabs.map((conditionTab) => (
                  <button
                    aria-selected={conditionTab.tableId === table.tableId}
                    className={`condition-tab-button ${
                      conditionTab.isAvailable ? '' : 'condition-tab-button-unavailable'
                    }`}
                    disabled={!conditionTab.isAvailable}
                    key={conditionTab.label}
                    onClick={() => {
                      if (conditionTab.tableId) {
                        onSelectTable(conditionTab.tableId);
                      }
                    }}
                    role="tab"
                    title={
                      conditionTab.isAvailable
                        ? conditionTab.label
                        : `${conditionTab.label} is not available for this location.`
                    }
                    type="button"
                  >
                    {conditionTab.label}
                  </button>
                ))}
                <button
                  className="secondary-button encounter-apply-weather-button"
                  disabled={!canApplyNormalToAllWeather}
                  onClick={() => onApplyNormalToAllWeather(table.tableId, targetWeatherTableIds)}
                  title="Copy Normal slots 1-7 species, forms, and probabilities to every weather table."
                  type="button"
                >
                  Apply to All Weather
                </button>
              </div>
            ) : null}

            <div className="encounter-slot-header">
              <strong>Slots</strong>
              <span
                className={
                  encounterProbabilityTotal === 100
                    ? 'encounter-total-status'
                    : 'encounter-total-status encounter-total-warning'
                }
              >
                Total chance: {encounterProbabilityTotal}%
              </span>
            </div>

            {encounterSlot ? (
              <>
                <div className="encounter-slot-tabs" aria-label="Encounter slot list">
                  {table.slots.slice(0, 10).map((slot) => {
                    const slotLabel = formatSpeciesFormLabel(
                      slot.species,
                      slot.form,
                      slot.speciesId
                    );

                    return (
                      <button
                        aria-pressed={slot.slot === selectedSlot}
                        className="slot-tab-button"
                        key={slot.slot}
                        onClick={() => onSelectSlot(slot.slot)}
                        type="button"
                      >
                        <PokemonSprite className="slot-tab-sprite" name={slotLabel} preferStatic />
                        <strong>{`#${slot.slot}`}</strong>
                        <span>{slotLabel}</span>
                        <small>{`${slot.levelMin}-${slot.levelMax} / ${slot.weight}%`}</small>
                      </button>
                    );
                  })}
                </div>

                <dl className="encounter-slot-detail">
                  <div>
                    <dt>Species</dt>
                    <dd>
                      {formatSpeciesFormLabel(
                        encounterSlot.species,
                        encounterSlot.form,
                        encounterSlot.speciesId
                      )}
                    </dd>
                  </div>
                  <div>
                    <dt>Levels</dt>
                    <dd>
                      {encounterSlot.levelMin}-{encounterSlot.levelMax}
                    </dd>
                  </div>
                  <div>
                    <dt>Probability</dt>
                    <dd>{encounterSlot.weight}</dd>
                  </div>
                </dl>

                <div className="editable-field-groups">
                  {encounterFieldGroups.map((group) => (
                    <fieldset className="editable-field-group" key={group.group}>
                      <legend>{group.group}</legend>
                      <div className="editable-field-grid">
                        {group.fields.map((field) => {
                          const currentValue = getEditableEncounterFieldValue(
                            encounterSlot,
                            field.field
                          );
                          const draftValue = drafts[field.field] ?? '';
                          const draftState = getTrainerFieldDraftState(
                            draftValue,
                            currentValue,
                            field
                          );

                          return (
                            <TrainerDraftField
                              currentValue={currentValue}
                              disabled={
                                !canEditEncounters ||
                                editSession === null ||
                                isEncounterUpdating
                              }
                              draftState={draftState}
                              draftValue={draftValue}
                              field={field}
                              formOptionContext={{
                                species: encounterSlot.species,
                                speciesId: encounterSlot.speciesId
                              }}
                              idPrefix="encounter-field"
                              key={field.field}
                              onChange={(value) =>
                                setDrafts((currentDrafts) => ({
                                  ...currentDrafts,
                                  [field.field]: value
                                }))
                              }
                            />
                          );
                        })}
                      </div>
                    </fieldset>
                  ))}
                </div>
                {editSession ? (
                  <div className="draft-action-row">
                    <button
                      className="primary-button"
                      disabled={!canSaveEncounterDrafts}
                      onClick={() =>
                        table &&
                        encounterSlot &&
                        onUpdateEncounterSlotFields(
                          table.tableId,
                          encounterSlot.slot,
                          encounterDraftSummary.changedFields.map((change) => ({
                            field: change.field,
                            value: change.value
                          }))
                        )
                      }
                      type="button"
                    >
                      <Save aria-hidden="true" size={16} />
                      <span>{isEncounterUpdating ? 'Saving' : 'Save Encounter'}</span>
                    </button>
                    <button
                      className="danger-button"
                      disabled={isEncounterUpdating}
                      onClick={() => {
                        setDrafts(encounterDraftDefaults);
                        cancelActiveEditSession();
                      }}
                      type="button"
                    >
                      <X aria-hidden="true" size={16} />
                      <span>Cancel</span>
                    </button>
                    <span className="draft-action-summary">
                      {formatDraftSummary(encounterDraftSummary)}
                    </span>
                  </div>
                ) : null}
              </>
            ) : (
              <p className="empty-copy">No encounter slot selected.</p>
            )}

            {!editSession ? (
              <button
                className="secondary-button"
                disabled={!canEditEncounters || isEditStarting || table.slots.length === 0}
                onClick={onStartEditSession}
                type="button"
              >
                <Pencil aria-hidden="true" size={16} />
                <span>{isEditStarting ? 'Starting' : 'Edit'}</span>
              </button>
            ) : null}
          </div>
        </>
      ) : (
        <p className="empty-copy">No encounter table selected.</p>
      )}
    </aside>
  );
}

function RaidBattlesSection({
  editSession,
  isEditStarting,
  isRaidBattleUpdating,
  onSearchChange,
  onSelectTable,
  onStartEditSession,
  onUpdateRaidBattleSlotField,
  onUpdateRaidBattleSlotFields,
  searchText,
  selectedTableId,
  workflow
}: {
  editSession: EditSession | null;
  isEditStarting: boolean;
  isRaidBattleUpdating: boolean;
  onSearchChange: (value: string) => void;
  onSelectTable: (tableId: string) => void;
  onStartEditSession: () => void;
  onUpdateRaidBattleSlotField: (
    tableId: string,
    slot: number,
    field: string,
    value: string
  ) => void;
  onUpdateRaidBattleSlotFields: (
    tableId: string,
    slot: number,
    changes: Array<{ field: string; value: string }>
  ) => void;
  searchText: string;
  selectedTableId: string | null;
  workflow: RaidBattlesWorkflow | null;
}) {
  const [selectedSlot, setSelectedSlot] = useState<number | null>(null);
  const normalizedSearch = searchText.trim().toLocaleLowerCase();
  const filteredTables =
    workflow?.tables.filter((table) => {
      if (!normalizedSearch) {
        return true;
      }

      return [
        table.denId,
        table.gameVersion,
        table.sourceTableHash,
        ...table.slots.flatMap((slot) => [
          slot.species,
          slot.speciesId.toString(),
          slot.levelTableHash,
          slot.dropTableHash,
          slot.dropRewardLink.preview,
          slot.dropRewardLink.rewardKindLabel,
          slot.dropRewardLink.sourceTableHash,
          slot.dropRewardLink.tableId,
          slot.bonusTableHash,
          slot.bonusRewardLink.preview,
          slot.bonusRewardLink.rewardKindLabel,
          slot.bonusRewardLink.sourceTableHash,
          slot.bonusRewardLink.tableId
        ])
      ]
        .join(' ')
        .toLocaleLowerCase()
        .includes(normalizedSearch);
    }) ?? [];
  const selectedTable =
    filteredTables.find((table) => table.tableId === selectedTableId) ??
    workflow?.tables.find((table) => table.tableId === selectedTableId) ??
    filteredTables[0] ??
    workflow?.tables[0] ??
    null;
  const selectedBattleSlot =
    selectedTable?.slots.find((slot) => slot.slot === selectedSlot) ??
    selectedTable?.slots[0] ??
    null;
  const canEditRaidBattles = workflow?.summary.availability === 'available';
  const pendingRaidBattleTableIds = getPendingRaidBattleTableIds(editSession);

  useEffect(() => {
    if (!selectedTable) {
      setSelectedSlot(null);
      return;
    }

    const hasSelectedSlot = selectedTable.slots.some((slot) => slot.slot === selectedSlot);
    if (!hasSelectedSlot) {
      setSelectedSlot(selectedTable.slots[0]?.slot ?? null);
    }
  }, [selectedSlot, selectedTable?.slots, selectedTable?.tableId]);

  return (
    <>
      <section aria-labelledby="raid-battles-heading" className="panel wide-panel">
        <div className="panel-heading">
          <ShieldCheck aria-hidden="true" size={18} />
          <h2 id="raid-battles-heading">Raid Battles</h2>
        </div>

        <div className="items-toolbar encounters-toolbar">
          <label className="search-box items-search">
            <Search aria-hidden="true" size={18} />
            <input
              aria-label="Search raid battles"
              disabled={!workflow}
              onChange={(event) => onSearchChange(event.target.value)}
              placeholder="Search raid battles"
              type="search"
              value={searchText}
            />
          </label>
          <Metric
            label="Loaded tables"
            value={workflow ? workflow.stats.totalTableCount.toString() : '0'}
          />
          <Metric
            label="Battle slots"
            value={workflow ? workflow.stats.totalSlotCount.toString() : '0'}
          />
          <Metric
            label="G-Max slots"
            value={workflow ? workflow.stats.gigantamaxSlotCount.toString() : '0'}
          />
          <Metric
            label="Pending changes"
            value={(editSession?.pendingEdits.length ?? 0).toString()}
          />
        </div>

        {workflow ? (
          <div className="encounters-layout">
            <div className="raid-rewards-table" role="table" aria-label="Raid battle tables">
              <div className="raid-rewards-row raid-rewards-row-heading" role="row">
                <span role="columnheader">Table</span>
                <span role="columnheader">Version</span>
                <span role="columnheader">Slots</span>
                <span role="columnheader">G-Max</span>
              </div>
              {filteredTables.map((table) => (
                <button
                  className={`raid-rewards-row ${
                    selectedTable?.tableId === table.tableId ? 'raid-rewards-row-selected' : ''
                  } ${
                    pendingRaidBattleTableIds.has(table.tableId) ? 'raid-rewards-row-pending' : ''
                  }`}
                  key={table.tableId}
                  onClick={() => onSelectTable(table.tableId)}
                  role="row"
                  type="button"
                >
                  <span role="cell">{table.sourceTableHash}</span>
                  <span role="cell">{table.gameVersion}</span>
                  <span role="cell">{table.slots.length}</span>
                  <span role="cell">{table.slots.filter((slot) => slot.isGigantamax).length}</span>
                </button>
              ))}
            </div>

            <SelectedRaidBattlePanel
              battleSlot={selectedBattleSlot}
              canEditRaidBattles={canEditRaidBattles}
              editSession={editSession}
              editableFields={workflow.editableFields}
              isEditStarting={isEditStarting}
              isRaidBattleUpdating={isRaidBattleUpdating}
              onSelectSlot={setSelectedSlot}
              onStartEditSession={onStartEditSession}
              onUpdateRaidBattleSlotField={onUpdateRaidBattleSlotField}
              onUpdateRaidBattleSlotFields={onUpdateRaidBattleSlotFields}
              selectedSlot={selectedSlot}
              table={selectedTable}
            />
          </div>
        ) : (
          <p className="empty-copy">Open Raid Battles from Workflows to load backend battle data.</p>
        )}
      </section>

      <DiagnosticsSection diagnostics={workflow?.diagnostics ?? []} />
    </>
  );
}

function SelectedRaidBattlePanel({
  battleSlot,
  canEditRaidBattles,
  editSession,
  editableFields,
  isEditStarting,
  isRaidBattleUpdating,
  onSelectSlot,
  onStartEditSession,
  onUpdateRaidBattleSlotField,
  onUpdateRaidBattleSlotFields,
  selectedSlot,
  table
}: {
  battleSlot: RaidBattleSlotRecord | null;
  canEditRaidBattles: boolean;
  editSession: EditSession | null;
  editableFields: RaidBattleEditableField[];
  isEditStarting: boolean;
  isRaidBattleUpdating: boolean;
  onSelectSlot: (slot: number | null) => void;
  onStartEditSession: () => void;
  onUpdateRaidBattleSlotField: (
    tableId: string,
    slot: number,
    field: string,
    value: string
  ) => void;
  onUpdateRaidBattleSlotFields: (
    tableId: string,
    slot: number,
    changes: Array<{ field: string; value: string }>
  ) => void;
  selectedSlot: number | null;
  table: RaidBattleTableRecord | null;
}) {
  const [drafts, setDrafts] = useState<Record<string, string>>({});
  const cancelActiveEditSession = useCancelActiveEditSession();
  const raidBattleFields = useMemo(
    () =>
      editableFields.map((field) =>
        toNumericEditableControlField(
          field,
          battleSlot
            ? getContextualFieldOptions(field, {
                abilityOptions: battleSlot.abilityOptions,
                species: battleSlot.species,
                speciesId: battleSlot.speciesId
              })
            : undefined
        )
      ),
    [battleSlot?.abilityOptions, battleSlot?.species, battleSlot?.speciesId, editableFields]
  );
  const raidBattleFieldGroups = useMemo(
    () => groupNumericEditableFields(raidBattleFields, getRaidBattleEditableFieldGroup),
    [raidBattleFields]
  );
  const raidBattleDraftDefaults = useMemo(
    () =>
      battleSlot
        ? createTrainerDrafts(raidBattleFields, (field) =>
            getEditableRaidBattleFieldValue(battleSlot, field)
          )
        : {},
    [
      battleSlot?.ability,
      battleSlot?.flawlessIvs,
      battleSlot?.form,
      battleSlot?.gender,
      battleSlot?.isGigantamax,
      battleSlot?.probabilities.join('|'),
      battleSlot?.slot,
      battleSlot?.speciesId,
      raidBattleFields,
      table?.tableId
    ]
  );
  const raidBattleDraftSummary = useMemo(
    () =>
      getTrainerDraftSummary(
        raidBattleFields,
        drafts,
        battleSlot ? (field) => getEditableRaidBattleFieldValue(battleSlot, field) : null
      ),
    [battleSlot, drafts, raidBattleFields]
  );
  const canSaveRaidBattleDrafts =
    table !== null &&
    battleSlot !== null &&
    editSession !== null &&
    canEditRaidBattles &&
    !isRaidBattleUpdating &&
    raidBattleDraftSummary.changedFields.length > 0 &&
    raidBattleDraftSummary.invalidFields.length === 0;

  useEffect(() => {
    setDrafts(raidBattleDraftDefaults);
  }, [raidBattleDraftDefaults]);

  return (
    <aside aria-label="Selected raid battle provenance" className="encounter-inspector">
      <div className="panel-heading">
        <ShieldCheck aria-hidden="true" size={18} />
        <h3>Selected Battle</h3>
      </div>

      {table ? (
        <>
          <dl className="item-provenance-list">
            <div>
              <dt>Table</dt>
              <dd>{table.sourceTableHash}</dd>
            </div>
            <div>
              <dt>Game</dt>
              <dd>{table.gameVersion}</dd>
            </div>
            <div>
              <dt>Source file</dt>
              <dd>{table.provenance.sourceFile}</dd>
            </div>
            <div>
              <dt>Layer</dt>
              <dd>{formatSourceLayer(table.provenance.sourceLayer)}</dd>
            </div>
            <div>
              <dt>File state</dt>
              <dd>{formatFileState(table.provenance.fileState)}</dd>
            </div>
          </dl>

          <div className="encounter-edit-form">
            <div className="encounter-slot-header">
              <strong>Battle slots</strong>
              <select
                aria-label="Raid battle slot"
                disabled={table.slots.length === 0}
                onChange={(event) => onSelectSlot(Number(event.target.value))}
                value={selectedSlot ?? ''}
              >
                {table.slots.map((candidate) => (
                  <option key={candidate.slot} value={candidate.slot}>
                    Slot {candidate.slot}: {formatSpeciesFormLabel(
                      candidate.species,
                      candidate.form,
                      candidate.speciesId
                    )}
                  </option>
                ))}
              </select>
            </div>

            {battleSlot ? (
              <>
                <div className="encounter-slot-tabs" aria-label="Raid battle slot list">
                  {table.slots.slice(0, 10).map((candidate) => {
                    const slotLabel = formatSpeciesFormLabel(
                      candidate.species,
                      candidate.form,
                      candidate.speciesId
                    );

                    return (
                      <button
                        aria-pressed={candidate.slot === selectedSlot}
                        className="slot-tab-button"
                        key={candidate.slot}
                        onClick={() => onSelectSlot(candidate.slot)}
                        type="button"
                      >
                        <PokemonSprite className="slot-tab-sprite" name={slotLabel} preferStatic />
                        <strong>{`#${candidate.slot}`}</strong>
                        <span>{slotLabel}</span>
                        <small>{candidate.probabilitySummary}</small>
                      </button>
                    );
                  })}
                </div>

                <dl className="encounter-slot-detail">
                  <div>
                    <dt>Pokemon</dt>
                    <dd>
                      {formatSpeciesFormLabel(
                        battleSlot.species,
                        battleSlot.form,
                        battleSlot.speciesId
                      )} ({battleSlot.speciesId})
                    </dd>
                  </div>
                  <div>
                    <dt>Ability</dt>
                    <dd>{battleSlot.abilityLabel}</dd>
                  </div>
                  <div>
                    <dt>Gender</dt>
                    <dd>{battleSlot.genderLabel}</dd>
                  </div>
                  <div>
                    <dt>G-Max</dt>
                    <dd>{battleSlot.isGigantamax ? 'Yes' : 'No'}</dd>
                  </div>
                  <div>
                    <dt>Perfect IVs</dt>
                    <dd>{battleSlot.flawlessIvs}</dd>
                  </div>
                  <div>
                    <dt>Star odds</dt>
                    <dd>{battleSlot.probabilitySummary}</dd>
                  </div>
                  <div>
                    <dt>Level table</dt>
                    <dd>{battleSlot.levelTableHash}</dd>
                  </div>
                  <div>
                    <dt>Drop table</dt>
                    <dd>{battleSlot.dropTableHash}</dd>
                  </div>
                  <div>
                    <dt>Drop rewards</dt>
                    <dd>{formatRaidBattleRewardLink(battleSlot.dropRewardLink)}</dd>
                  </div>
                  <div>
                    <dt>Bonus table</dt>
                    <dd>{battleSlot.bonusTableHash}</dd>
                  </div>
                  <div>
                    <dt>Bonus rewards</dt>
                    <dd>{formatRaidBattleRewardLink(battleSlot.bonusRewardLink)}</dd>
                  </div>
                </dl>

                <div className="editable-field-groups">
                  {raidBattleFieldGroups.map((group) => (
                    <fieldset className="editable-field-group" key={group.group}>
                      <legend>{group.group}</legend>
                      <div className="editable-field-grid">
                        {group.fields.map((field) => {
                          const currentValue = getEditableRaidBattleFieldValue(
                            battleSlot,
                            field.field
                          );
                          const draftValue = drafts[field.field] ?? '';
                          const draftState = getTrainerFieldDraftState(
                            draftValue,
                            currentValue,
                            field
                          );

                          return (
                            <TrainerDraftField
                              currentValue={currentValue}
                              disabled={
                                !canEditRaidBattles ||
                                editSession === null ||
                                isRaidBattleUpdating
                              }
                              draftState={draftState}
                              draftValue={draftValue}
                              field={field}
                              formOptionContext={{
                                abilityOptions: battleSlot.abilityOptions,
                                species: battleSlot.species,
                                speciesId: battleSlot.speciesId
                              }}
                              idPrefix="raid-battle-field"
                              key={field.field}
                              onChange={(value) =>
                                setDrafts((currentDrafts) => ({
                                  ...currentDrafts,
                                  [field.field]: value
                                }))
                              }
                            />
                          );
                        })}
                      </div>
                    </fieldset>
                  ))}
                </div>
                {editSession ? (
                  <div className="draft-action-row">
                    <button
                      className="primary-button"
                      disabled={!canSaveRaidBattleDrafts}
                      onClick={() =>
                        table &&
                        battleSlot &&
                        onUpdateRaidBattleSlotFields(
                          table.tableId,
                          battleSlot.slot,
                          raidBattleDraftSummary.changedFields.map((change) => ({
                            field: change.field,
                            value: change.value
                          }))
                        )
                      }
                      type="button"
                    >
                      <Save aria-hidden="true" size={16} />
                      <span>{isRaidBattleUpdating ? 'Saving' : 'Save Battle'}</span>
                    </button>
                    <button
                      className="danger-button"
                      disabled={isRaidBattleUpdating}
                      onClick={() => {
                        setDrafts(raidBattleDraftDefaults);
                        cancelActiveEditSession();
                      }}
                      type="button"
                    >
                      <X aria-hidden="true" size={16} />
                      <span>Cancel</span>
                    </button>
                    <span className="draft-action-summary">
                      {formatDraftSummary(raidBattleDraftSummary)}
                    </span>
                  </div>
                ) : null}
              </>
            ) : (
              <p className="empty-copy">No raid battle selected.</p>
            )}

            {!editSession ? (
              <button
                className="secondary-button"
                disabled={!canEditRaidBattles || isEditStarting || table.slots.length === 0}
                onClick={onStartEditSession}
                type="button"
              >
                <Pencil aria-hidden="true" size={16} />
                <span>{isEditStarting ? 'Starting' : 'Edit'}</span>
              </button>
            ) : null}
          </div>
        </>
      ) : (
        <p className="empty-copy">No raid battle table selected.</p>
      )}
    </aside>
  );
}

function RaidRewardsSection({
  editSession,
  isEditStarting,
  isRaidRewardUpdating,
  onSearchChange,
  onSelectTable,
  onStartEditSession,
  onUpdateRaidRewardField,
  onUpdateRaidRewardFields,
  searchText,
  selectedTableId,
  workflow
}: {
  editSession: EditSession | null;
  isEditStarting: boolean;
  isRaidRewardUpdating: boolean;
  onSearchChange: (value: string) => void;
  onSelectTable: (tableId: string) => void;
  onStartEditSession: () => void;
  onUpdateRaidRewardField: (
    tableId: string,
    slot: number,
    field: string,
    value: string
  ) => void;
  onUpdateRaidRewardFields: (
    tableId: string,
    slot: number,
    changes: Array<{ field: string; value: string }>
  ) => void;
  searchText: string;
  selectedTableId: string | null;
  workflow: RaidRewardsWorkflow | null;
}) {
  const [selectedSlot, setSelectedSlot] = useState<number | null>(null);
  const normalizedSearch = searchText.trim().toLocaleLowerCase();
  const filteredTables =
    workflow?.tables.filter((table) => {
      if (!normalizedSearch) {
        return true;
      }

      return [
        table.archiveMember,
        table.denId,
        table.rewardKindLabel,
        table.sourceTableHash,
        ...table.rewards.flatMap((reward) => [reward.itemName, reward.itemId.toString()])
      ]
        .join(' ')
        .toLocaleLowerCase()
        .includes(normalizedSearch);
    }) ?? [];
  const selectedTable =
    filteredTables.find((table) => table.tableId === selectedTableId) ??
    workflow?.tables.find((table) => table.tableId === selectedTableId) ??
    filteredTables[0] ??
    workflow?.tables[0] ??
    null;
  const selectedReward =
    selectedTable?.rewards.find((reward) => reward.slot === selectedSlot) ??
    selectedTable?.rewards[0] ??
    null;
  const canEditRaidRewards = workflow?.summary.availability === 'available';
  const pendingRaidRewardTableIds = getPendingRaidRewardTableIds(editSession);

  useEffect(() => {
    if (!selectedTable) {
      setSelectedSlot(null);
      return;
    }

    const hasSelectedSlot = selectedTable.rewards.some((reward) => reward.slot === selectedSlot);
    if (!hasSelectedSlot) {
      setSelectedSlot(selectedTable.rewards[0]?.slot ?? null);
    }
  }, [selectedSlot, selectedTable?.rewards, selectedTable?.tableId]);

  return (
    <>
      <section aria-labelledby="raid-rewards-heading" className="panel wide-panel">
        <div className="panel-heading">
          <ShieldCheck aria-hidden="true" size={18} />
          <h2 id="raid-rewards-heading">Raid Rewards</h2>
        </div>

        <div className="items-toolbar encounters-toolbar">
          <label className="search-box items-search">
            <Search aria-hidden="true" size={18} />
            <input
              aria-label="Search raid rewards"
              disabled={!workflow}
              onChange={(event) => onSearchChange(event.target.value)}
              placeholder="Search raid rewards"
              type="search"
              value={searchText}
            />
          </label>
          <Metric
            label="Loaded tables"
            value={workflow ? workflow.stats.totalTableCount.toString() : '0'}
          />
          <Metric
            label="Reward rows"
            value={workflow ? workflow.stats.totalRewardItemCount.toString() : '0'}
          />
          <Metric
            label="Pending changes"
            value={(editSession?.pendingEdits.length ?? 0).toString()}
          />
        </div>

        {workflow ? (
          <div className="encounters-layout">
            <div className="raid-rewards-table" role="table" aria-label="Raid reward tables">
              <div className="raid-rewards-row raid-rewards-row-heading" role="row">
                <span role="columnheader">Table</span>
                <span role="columnheader">Kind</span>
                <span role="columnheader">Rewards</span>
                <span role="columnheader">Member</span>
              </div>
              {filteredTables.map((table) => (
                <button
                  className={`raid-rewards-row ${
                    selectedTable?.tableId === table.tableId ? 'raid-rewards-row-selected' : ''
                  } ${
                    pendingRaidRewardTableIds.has(table.tableId) ? 'raid-rewards-row-pending' : ''
                  }`}
                  key={table.tableId}
                  onClick={() => onSelectTable(table.tableId)}
                  role="row"
                  type="button"
                >
                  <span role="cell">{table.sourceTableHash}</span>
                  <span role="cell">{table.rewardKindLabel}</span>
                  <span role="cell">{table.rewards.length}</span>
                  <span role="cell">{table.archiveMember}</span>
                </button>
              ))}
            </div>

            <SelectedRaidRewardPanel
              canEditRaidRewards={canEditRaidRewards}
              editSession={editSession}
              editableFields={workflow.editableFields}
              isEditStarting={isEditStarting}
              isRaidRewardUpdating={isRaidRewardUpdating}
              onSelectSlot={setSelectedSlot}
              onStartEditSession={onStartEditSession}
              onUpdateRaidRewardField={onUpdateRaidRewardField}
              onUpdateRaidRewardFields={onUpdateRaidRewardFields}
              reward={selectedReward}
              selectedSlot={selectedSlot}
              table={selectedTable}
            />
          </div>
        ) : (
          <p className="empty-copy">Open Raid Rewards from Workflows to load backend reward data.</p>
        )}
      </section>

      <DiagnosticsSection diagnostics={workflow?.diagnostics ?? []} />
    </>
  );
}

function SelectedRaidRewardPanel({
  canEditRaidRewards,
  editSession,
  editableFields,
  isEditStarting,
  isRaidRewardUpdating,
  onSelectSlot,
  onStartEditSession,
  onUpdateRaidRewardField,
  onUpdateRaidRewardFields,
  reward,
  selectedSlot,
  table
}: {
  canEditRaidRewards: boolean;
  editSession: EditSession | null;
  editableFields: RaidRewardEditableField[];
  isEditStarting: boolean;
  isRaidRewardUpdating: boolean;
  onSelectSlot: (slot: number | null) => void;
  onStartEditSession: () => void;
  onUpdateRaidRewardField: (
    tableId: string,
    slot: number,
    field: string,
    value: string
  ) => void;
  onUpdateRaidRewardFields: (
    tableId: string,
    slot: number,
    changes: Array<{ field: string; value: string }>
  ) => void;
  reward: RaidRewardItemRecord | null;
  selectedSlot: number | null;
  table: RaidRewardTableRecord | null;
}) {
  const [drafts, setDrafts] = useState<Record<string, string>>({});
  const cancelActiveEditSession = useCancelActiveEditSession();
  const raidRewardFields = useMemo(
    () =>
      editableFields.map((field) =>
        toNumericEditableControlField(getRaidRewardFieldForKind(field, table?.rewardKind))
      ),
    [editableFields, table?.rewardKind]
  );
  const raidRewardFieldGroups = useMemo(
    () => groupNumericEditableFields(raidRewardFields, getRaidRewardEditableFieldGroup),
    [raidRewardFields]
  );
  const raidRewardDraftDefaults = useMemo(
    () =>
      reward
        ? createTrainerDrafts(raidRewardFields, (field) =>
            getEditableRaidRewardFieldValue(reward, field)
          )
        : {},
    [raidRewardFields, reward?.itemId, reward?.slot, reward?.values.join('|'), table?.tableId]
  );
  const raidRewardDraftSummary = useMemo(
    () =>
      getTrainerDraftSummary(
        raidRewardFields,
        drafts,
        reward ? (field) => getEditableRaidRewardFieldValue(reward, field) : null
      ),
    [drafts, raidRewardFields, reward]
  );
  const canSaveRaidRewardDrafts =
    table !== null &&
    reward !== null &&
    editSession !== null &&
    canEditRaidRewards &&
    !isRaidRewardUpdating &&
    raidRewardDraftSummary.changedFields.length > 0 &&
    raidRewardDraftSummary.invalidFields.length === 0;

  useEffect(() => {
    setDrafts(raidRewardDraftDefaults);
  }, [raidRewardDraftDefaults]);

  return (
    <aside aria-label="Selected raid reward provenance" className="encounter-inspector">
      <div className="panel-heading">
        <ShieldCheck aria-hidden="true" size={18} />
        <h3>Selected Reward</h3>
      </div>

      {table ? (
        <>
          <dl className="item-provenance-list">
            <div>
              <dt>Table</dt>
              <dd>{table.sourceTableHash}</dd>
            </div>
            <div>
              <dt>Kind</dt>
              <dd>{table.rewardKindLabel}</dd>
            </div>
            <div>
              <dt>Archive member</dt>
              <dd>{table.archiveMember}</dd>
            </div>
            <div>
              <dt>Source file</dt>
              <dd>{table.provenance.sourceFile}</dd>
            </div>
            <div>
              <dt>Layer</dt>
              <dd>{formatSourceLayer(table.provenance.sourceLayer)}</dd>
            </div>
            <div>
              <dt>File state</dt>
              <dd>{formatFileState(table.provenance.fileState)}</dd>
            </div>
          </dl>

          <div className="encounter-edit-form">
            <div className="encounter-slot-header">
              <strong>Rewards</strong>
              <select
                aria-label="Raid reward slot"
                disabled={table.rewards.length === 0}
                onChange={(event) => onSelectSlot(Number(event.target.value))}
                value={selectedSlot ?? ''}
              >
                {table.rewards.map((candidate) => (
                  <option key={candidate.slot} value={candidate.slot}>
                    Slot {candidate.slot}: {candidate.itemName}
                  </option>
                ))}
              </select>
            </div>

            {reward ? (
              <>
                <dl className="encounter-slot-detail">
                  <div>
                    <dt>Item</dt>
                    <dd>
                      {reward.itemName} ({reward.itemId})
                    </dd>
                  </div>
                  <div>
                    <dt>Entry ID</dt>
                    <dd>{reward.entryId}</dd>
                  </div>
                  <div>
                    <dt>{getRaidRewardValuesLabel(table.rewardKind)}</dt>
                    <dd>{formatRaidRewardValues(table.rewardKind, reward.values)}</dd>
                  </div>
                </dl>

                <div className="raid-reward-slot-grid" aria-label="Raid reward slots">
                  {table.rewards.slice(0, 10).map((candidate) => (
                    <button
                      aria-pressed={candidate.slot === selectedSlot}
                      className="slot-tab-button"
                      key={candidate.slot}
                      onClick={() => onSelectSlot(candidate.slot)}
                      type="button"
                    >
                      <strong>{`#${candidate.slot}`}</strong>
                      <span>{candidate.itemName}</span>
                      <small>{formatRaidRewardSlotSummary(table.rewardKind, candidate)}</small>
                    </button>
                  ))}
                </div>

                <div className="editable-field-groups">
                  {raidRewardFieldGroups.map((group) => (
                    <fieldset className="editable-field-group" key={group.group}>
                      <legend>{group.group}</legend>
                      <div className="editable-field-grid">
                        {group.fields.map((field) => {
                          const currentValue = getEditableRaidRewardFieldValue(
                            reward,
                            field.field
                          );
                          const draftValue = drafts[field.field] ?? '';
                          const draftState = getTrainerFieldDraftState(
                            draftValue,
                            currentValue,
                            field
                          );

                          return (
                            <TrainerDraftField
                              currentValue={currentValue}
                              disabled={
                                !canEditRaidRewards ||
                                editSession === null ||
                                isRaidRewardUpdating
                              }
                              draftState={draftState}
                              draftValue={draftValue}
                              field={field}
                              idPrefix="raid-reward-field"
                              key={field.field}
                              onChange={(value) =>
                                setDrafts((currentDrafts) => ({
                                  ...currentDrafts,
                                  [field.field]: value
                                }))
                              }
                            />
                          );
                        })}
                      </div>
                    </fieldset>
                  ))}
                </div>
                {editSession ? (
                  <div className="draft-action-row">
                    <button
                      className="primary-button"
                      disabled={!canSaveRaidRewardDrafts}
                      onClick={() =>
                        table &&
                        reward &&
                        onUpdateRaidRewardFields(
                          table.tableId,
                          reward.slot,
                          raidRewardDraftSummary.changedFields.map((change) => ({
                            field: change.field,
                            value: change.value
                          }))
                        )
                      }
                      type="button"
                    >
                      <Save aria-hidden="true" size={16} />
                      <span>{isRaidRewardUpdating ? 'Saving' : 'Save Reward'}</span>
                    </button>
                    <button
                      className="danger-button"
                      disabled={isRaidRewardUpdating}
                      onClick={() => {
                        setDrafts(raidRewardDraftDefaults);
                        cancelActiveEditSession();
                      }}
                      type="button"
                    >
                      <X aria-hidden="true" size={16} />
                      <span>Cancel</span>
                    </button>
                    <span className="draft-action-summary">
                      {formatDraftSummary(raidRewardDraftSummary)}
                    </span>
                  </div>
                ) : null}
              </>
            ) : (
              <p className="empty-copy">No raid reward selected.</p>
            )}

            {!editSession ? (
              <button
                className="secondary-button"
                disabled={!canEditRaidRewards || isEditStarting || table.rewards.length === 0}
                onClick={onStartEditSession}
                type="button"
              >
                <Pencil aria-hidden="true" size={16} />
                <span>{isEditStarting ? 'Starting' : 'Edit'}</span>
              </button>
            ) : null}
          </div>
        </>
      ) : (
        <p className="empty-copy">No raid reward table selected.</p>
      )}
    </aside>
  );
}

function getRaidRewardFieldForKind(
  field: RaidRewardEditableField,
  rewardKind?: string
): RaidRewardEditableField {
  if (
    !raidRewardValueFieldNames.includes(field.field as (typeof raidRewardValueFieldNames)[number])
  ) {
    return field;
  }

  const starLabel = field.label.match(/^(\d-star)/)?.[1];
  if (!starLabel) {
    return field;
  }

  const valueLabel = rewardKind === 'drop' ? 'drop chance' : 'quantity';
  return { ...field, label: `${starLabel} ${valueLabel}` };
}

function getRaidRewardValuesLabel(rewardKind: string) {
  return rewardKind === 'drop' ? 'Drop chances by star' : 'Reward quantities by star';
}

function formatRaidRewardValues(rewardKind: string, values: number[]) {
  return values
    .slice(0, 5)
    .map((value, index) =>
      rewardKind === 'drop'
        ? `${index + 1}-star ${value}% chance`
        : `${index + 1}-star ${value} item${value === 1 ? '' : 's'}`
    )
    .join(' / ');
}

function formatRaidRewardSlotSummary(rewardKind: string, reward: RaidRewardItemRecord) {
  return rewardKind === 'drop'
    ? `Drop chance ${reward.values.slice(0, 5).join('/')}%`
    : `Quantity ${reward.values.slice(0, 5).join('/')}`;
}

function PlacementSection({
  editSession,
  isEditStarting,
  isPlacementUpdating,
  onSearchChange,
  onSelectObject,
  onStartEditSession,
  onUpdatePlacementObjectField,
  onUpdatePlacementObjectFields,
  searchText,
  selectedObjectId,
  workflow
}: {
  editSession: EditSession | null;
  isEditStarting: boolean;
  isPlacementUpdating: boolean;
  onSearchChange: (value: string) => void;
  onSelectObject: (objectId: string | null) => void;
  onStartEditSession: () => void;
  onUpdatePlacementObjectField: (objectId: string, field: string, value: string) => void;
  onUpdatePlacementObjectFields: (
    objectId: string,
    changes: Array<{ field: string; value: string }>
  ) => void;
  searchText: string;
  selectedObjectId: string | null;
  workflow: PlacementWorkflow | null;
}) {
  const normalizedSearch = searchText.trim().toLocaleLowerCase();
  const filteredObjects =
    workflow?.objects.filter((placedObject) => {
      if (!normalizedSearch) {
        return true;
      }

      return [
        placedObject.archiveMember,
        placedObject.itemHash,
        placedObject.itemId?.toString() ?? '',
        placedObject.itemName,
        placedObject.label,
        placedObject.map,
        placedObject.objectType,
        placedObject.scriptId ?? ''
      ]
        .join(' ')
        .toLocaleLowerCase()
        .includes(normalizedSearch);
    }) ?? [];
  const selectedObject =
    filteredObjects.find((placedObject) => placedObject.objectId === selectedObjectId) ??
    workflow?.objects.find((placedObject) => placedObject.objectId === selectedObjectId) ??
    filteredObjects[0] ??
    workflow?.objects[0] ??
    null;
  const canEditPlacement = workflow?.summary.availability === 'available';
  const pendingPlacementObjectIds = getPendingPlacementObjectIds(editSession);

  useEffect(() => {
    if (selectedObject && selectedObject.objectId !== selectedObjectId) {
      onSelectObject(selectedObject.objectId);
    }
  }, [onSelectObject, selectedObject?.objectId, selectedObjectId]);

  return (
    <>
      <section aria-labelledby="placement-heading" className="panel wide-panel">
        <div className="panel-heading">
          <MapPin aria-hidden="true" size={18} />
          <h2 id="placement-heading">Placement</h2>
        </div>

        <div className="items-toolbar encounters-toolbar">
          <label className="search-box items-search">
            <Search aria-hidden="true" size={18} />
            <input
              aria-label="Search placement"
              disabled={!workflow}
              onChange={(event) => onSearchChange(event.target.value)}
              placeholder="Search placement"
              type="search"
              value={searchText}
            />
          </label>
          <Metric
            label="Loaded objects"
            value={workflow ? workflow.stats.totalObjectCount.toString() : '0'}
          />
          <Metric
            label="Areas"
            value={workflow ? workflow.stats.totalAreaCount.toString() : '0'}
          />
          <Metric
            label="Pending changes"
            value={(editSession?.pendingEdits.length ?? 0).toString()}
          />
        </div>

        {workflow ? (
          <div className="encounters-layout">
            <div className="raid-rewards-table" role="table" aria-label="Placed objects">
              <div className="raid-rewards-row raid-rewards-row-heading" role="row">
                <span role="columnheader">Object</span>
                <span role="columnheader">Map</span>
                <span role="columnheader">Item</span>
                <span role="columnheader">Position</span>
              </div>
              {filteredObjects.map((placedObject) => (
                <button
                  className={`raid-rewards-row ${
                    selectedObject?.objectId === placedObject.objectId
                      ? 'raid-rewards-row-selected'
                      : ''
                  } ${
                    pendingPlacementObjectIds.has(placedObject.objectId)
                      ? 'raid-rewards-row-pending'
                      : ''
                  }`}
                  key={placedObject.objectId}
                  onClick={() => onSelectObject(placedObject.objectId)}
                  role="row"
                  type="button"
                >
                  <span role="cell">{placedObject.label}</span>
                  <span role="cell">{placedObject.map}</span>
                  <span role="cell">{formatPlacementItem(placedObject)}</span>
                  <span role="cell">{formatPlacementCoordinates(placedObject)}</span>
                </button>
              ))}
            </div>

            <SelectedPlacementPanel
              canEditPlacement={canEditPlacement}
              editSession={editSession}
              editableFields={workflow.editableFields}
              isEditStarting={isEditStarting}
              isPlacementUpdating={isPlacementUpdating}
              onStartEditSession={onStartEditSession}
              onUpdatePlacementObjectField={onUpdatePlacementObjectField}
              onUpdatePlacementObjectFields={onUpdatePlacementObjectFields}
              placedObject={selectedObject}
            />
          </div>
        ) : (
          <p className="empty-copy">Open Placement from Workflows to load backend placement data.</p>
        )}
      </section>

      <DiagnosticsSection diagnostics={workflow?.diagnostics ?? []} />
    </>
  );
}

function SelectedPlacementPanel({
  canEditPlacement,
  editSession,
  editableFields,
  isEditStarting,
  isPlacementUpdating,
  onStartEditSession,
  onUpdatePlacementObjectField,
  onUpdatePlacementObjectFields,
  placedObject
}: {
  canEditPlacement: boolean;
  editSession: EditSession | null;
  editableFields: PlacementEditableField[];
  isEditStarting: boolean;
  isPlacementUpdating: boolean;
  onStartEditSession: () => void;
  onUpdatePlacementObjectField: (objectId: string, field: string, value: string) => void;
  onUpdatePlacementObjectFields: (
    objectId: string,
    changes: Array<{ field: string; value: string }>
  ) => void;
  placedObject: PlacedObjectRecord | null;
}) {
  const [drafts, setDrafts] = useState<Record<string, string>>({});
  const cancelActiveEditSession = useCancelActiveEditSession();
  const visibleFields = editableFields.filter((field) =>
    placedObject ? isPlacementFieldVisible(placedObject, field.field) : false
  );
  const placementFieldGroups = useMemo(() => {
    const groups: Array<{ group: string; fields: PlacementEditableField[] }> = [];

    for (const field of visibleFields) {
      const groupName = getPlacementEditableFieldGroup(field);
      let group = groups.find((candidate) => candidate.group === groupName);
      if (!group) {
        group = { group: groupName, fields: [] };
        groups.push(group);
      }

      group.fields.push(field);
    }

    return groups;
  }, [visibleFields]);
  const placementDraftDefaults = useMemo(
    () =>
      placedObject
        ? createTrainerDrafts(
            visibleFields.map((field) => toNumericEditableControlField(field)),
            (field) => getEditablePlacementFieldValue(placedObject, field)
          )
        : {},
    [
      placedObject?.chance,
      placedObject?.itemId,
      placedObject?.objectId,
      placedObject?.quantity,
      placedObject?.rotationY,
      placedObject?.x,
      placedObject?.y,
      placedObject?.z,
      visibleFields.map((field) => field.field).join('|')
    ]
  );
  const placementDraftSummary = useMemo(
    () =>
      getPlacementDraftSummary(
        visibleFields,
        drafts,
        placedObject ? (field) => getEditablePlacementFieldValue(placedObject, field) : null
      ),
    [drafts, placedObject, visibleFields]
  );
  const canSavePlacementDrafts =
    placedObject !== null &&
    editSession !== null &&
    canEditPlacement &&
    !isPlacementUpdating &&
    placementDraftSummary.changedFields.length > 0 &&
    placementDraftSummary.invalidFields.length === 0;

  useEffect(() => {
    setDrafts(placementDraftDefaults);
  }, [placementDraftDefaults]);

  return (
    <aside aria-label="Selected placement object provenance" className="encounter-inspector">
      <div className="panel-heading">
        <MapPin aria-hidden="true" size={18} />
        <h3>Selected Object</h3>
      </div>

      {placedObject ? (
        <>
          <dl className="item-provenance-list">
            <div>
              <dt>Object</dt>
              <dd>{placedObject.label}</dd>
            </div>
            <div>
              <dt>Type</dt>
              <dd>{placedObject.objectType}</dd>
            </div>
            <div>
              <dt>Map</dt>
              <dd>{placedObject.map}</dd>
            </div>
            <div>
              <dt>Archive member</dt>
              <dd>{placedObject.archiveMember}</dd>
            </div>
            <div>
              <dt>Source file</dt>
              <dd>{placedObject.provenance.sourceFile}</dd>
            </div>
            <div>
              <dt>Layer</dt>
              <dd>{formatSourceLayer(placedObject.provenance.sourceLayer)}</dd>
            </div>
            <div>
              <dt>File state</dt>
              <dd>{formatFileState(placedObject.provenance.fileState)}</dd>
            </div>
          </dl>

          <div className="encounter-edit-form">
            <dl className="encounter-slot-detail">
              <div>
                <dt>Item</dt>
                <dd>{formatPlacementItem(placedObject)}</dd>
              </div>
              <div>
                <dt>Quantity</dt>
                <dd>{placedObject.quantity}</dd>
              </div>
              <div>
                <dt>Chance</dt>
                <dd>{placedObject.chance ?? 'n/a'}</dd>
              </div>
              <div>
                <dt>Position</dt>
                <dd>{formatPlacementCoordinates(placedObject)}</dd>
              </div>
              <div>
                <dt>Link</dt>
                <dd>{placedObject.scriptId || 'n/a'}</dd>
              </div>
            </dl>

            <div className="editable-field-groups">
              {placementFieldGroups.map((group) => (
                <fieldset className="editable-field-group" key={group.group}>
                  <legend>{group.group}</legend>
                  <div className="editable-field-grid">
                    {group.fields.map((field) => {
                      const currentValue = getEditablePlacementFieldValue(
                        placedObject,
                        field.field
                      );
                      const draftValue = drafts[field.field] ?? '';
                      const draftState = getPlacementDraftState(draftValue, currentValue, field);
                      const isInvalid =
                        draftValue.trim() !== '' && draftState.normalizedValue === null;
                      const isChanged = draftState.normalizedValue !== null;
                      const fieldOptions = field.options ?? [];
                      const statusText = isInvalid
                        ? `Allowed range: ${field.minimumValue}-${field.maximumValue}.`
                        : isChanged
                          ? 'Changed'
                          : null;

                      return (
                        <label
                          className={`path-field editable-field-control ${
                            isChanged ? 'editable-field-changed' : ''
                          } ${isInvalid ? 'editable-field-invalid' : ''}`}
                          htmlFor={`placement-field-${field.field}`}
                          key={field.field}
                        >
                          <span>{field.label}</span>
                          {fieldOptions.length > 0 ? (
                            <SearchableOptionInput
                              ariaLabel={field.label}
                              disabled={
                                !canEditPlacement ||
                                editSession === null ||
                                isPlacementUpdating
                              }
                              id={`placement-field-${field.field}`}
                              onChange={(value) =>
                                setDrafts((currentDrafts) => ({
                                  ...currentDrafts,
                                  [field.field]: value
                                }))
                              }
                              options={addDraftFallbackOption(
                                fieldOptions,
                                draftValue,
                                `${field.label} ${draftValue}`
                              )}
                              title={getEditableFieldHelp(field)}
                              value={draftValue}
                            />
                          ) : (
                            <input
                              aria-label={field.label}
                              disabled={
                                !canEditPlacement ||
                                editSession === null ||
                                isPlacementUpdating
                              }
                              id={`placement-field-${field.field}`}
                              max={field.maximumValue}
                              min={field.minimumValue}
                              onChange={(event) =>
                                setDrafts((currentDrafts) => ({
                                  ...currentDrafts,
                                  [field.field]: event.target.value
                                }))
                              }
                              step={field.valueKind === 'integer' ? 1 : 'any'}
                              title={getEditableFieldHelp(field)}
                              type="number"
                              value={draftValue}
                            />
                          )}
                          {statusText ? (
                            <small
                              className={
                                isInvalid ? 'editable-field-error' : 'editable-field-status'
                              }
                            >
                              {statusText}
                            </small>
                          ) : null}
                        </label>
                      );
                    })}
                  </div>
                </fieldset>
              ))}
            </div>
            {editSession ? (
              <div className="draft-action-row">
                <button
                  className="primary-button"
                  disabled={!canSavePlacementDrafts}
                  onClick={() =>
                    placedObject &&
                    onUpdatePlacementObjectFields(
                      placedObject.objectId,
                      placementDraftSummary.changedFields.map((change) => ({
                        field: change.field,
                        value: change.value
                      }))
                    )
                  }
                  type="button"
                >
                  <Save aria-hidden="true" size={16} />
                  <span>{isPlacementUpdating ? 'Saving' : 'Save Object'}</span>
                </button>
                <button
                  className="danger-button"
                  disabled={isPlacementUpdating}
                  onClick={() => {
                    setDrafts(placementDraftDefaults);
                    cancelActiveEditSession();
                  }}
                  type="button"
                >
                  <X aria-hidden="true" size={16} />
                  <span>Cancel</span>
                </button>
                <span className="draft-action-summary">
                  {formatDraftSummary(placementDraftSummary)}
                </span>
              </div>
            ) : null}

            {!editSession ? (
              <button
                className="secondary-button"
                disabled={!canEditPlacement || isEditStarting}
                onClick={onStartEditSession}
                type="button"
              >
                <Pencil aria-hidden="true" size={16} />
                <span>{isEditStarting ? 'Starting' : 'Edit'}</span>
              </button>
            ) : null}
          </div>
        </>
      ) : (
        <p className="empty-copy">No placement object selected.</p>
      )}
    </aside>
  );
}

function FlagworkSaveSection({
  onSearchChange,
  onSelectFlag,
  onSelectSaveBlock,
  searchText,
  selectedFlagId,
  selectedSaveBlockId,
  workflow
}: {
  onSearchChange: (value: string) => void;
  onSelectFlag: (flagId: string | null) => void;
  onSelectSaveBlock: (blockId: string | null) => void;
  searchText: string;
  selectedFlagId: string | null;
  selectedSaveBlockId: string | null;
  workflow: FlagworkSaveWorkflow | null;
}) {
  const filteredFlags = filterFlagRecords(workflow?.flags ?? [], searchText);
  const filteredSaveBlocks = filterSaveBlockRecords(workflow?.saveBlocks ?? [], searchText);
  const selectedFlag =
    filteredFlags.find((flag) => flag.flagId === selectedFlagId) ??
    workflow?.flags.find((flag) => flag.flagId === selectedFlagId) ??
    filteredFlags[0] ??
    workflow?.flags[0] ??
    null;
  const selectedSaveBlock =
    filteredSaveBlocks.find((saveBlock) => saveBlock.blockId === selectedSaveBlockId) ??
    workflow?.saveBlocks.find((saveBlock) => saveBlock.blockId === selectedSaveBlockId) ??
    filteredSaveBlocks[0] ??
    workflow?.saveBlocks[0] ??
    null;

  useEffect(() => {
    if (selectedFlag && selectedFlag.flagId !== selectedFlagId) {
      onSelectFlag(selectedFlag.flagId);
    }
  }, [onSelectFlag, selectedFlag?.flagId, selectedFlagId]);

  useEffect(() => {
    if (selectedSaveBlock && selectedSaveBlock.blockId !== selectedSaveBlockId) {
      onSelectSaveBlock(selectedSaveBlock.blockId);
    }
  }, [onSelectSaveBlock, selectedSaveBlock?.blockId, selectedSaveBlockId]);

  return (
    <>
      <section aria-labelledby="flagwork-save-heading" className="panel wide-panel">
        <div className="panel-heading">
          <Save aria-hidden="true" size={18} />
          <h2 id="flagwork-save-heading">Flagwork and Save Inspectors</h2>
        </div>

        <div className="items-toolbar flagwork-toolbar">
          <label className="search-box items-search">
            <Search aria-hidden="true" size={18} />
            <input
              aria-label="Search flagwork and save keys"
              disabled={!workflow}
              onChange={(event) => onSearchChange(event.target.value)}
              placeholder="Search flagwork"
              type="search"
              value={searchText}
            />
          </label>
          <Metric
            label="Flags and works"
            value={workflow ? workflow.stats.totalFlagCount.toString() : '0'}
          />
          <Metric
            label="Save keys"
            value={workflow ? workflow.stats.totalSaveBlockCount.toString() : '0'}
          />
          <Metric
            label="Source files"
            value={workflow ? workflow.stats.sourceFileCount.toString() : '0'}
          />
          <Metric
            label="Save file"
            value={workflow?.stats.hasSaveFile ? 'Configured' : 'Not set'}
          />
        </div>

        {workflow ? (
          <div className="flagwork-layout">
            <div className="flagwork-stack">
              <div className="flagwork-table" role="table" aria-label="Flagwork records">
                <div className="flagwork-row flagwork-row-heading" role="row">
                  <span role="columnheader">Table</span>
                  <span role="columnheader">Index</span>
                  <span role="columnheader">Kind</span>
                  <span role="columnheader">Name</span>
                  <span role="columnheader">Save key</span>
                </div>
                {filteredFlags.map((flag) => (
                  <button
                    className={`flagwork-row ${
                      selectedFlag?.flagId === flag.flagId ? 'flagwork-row-selected' : ''
                    }`}
                    key={flag.flagId}
                    onClick={() => onSelectFlag(flag.flagId)}
                    role="row"
                    type="button"
                  >
                    <span role="cell">{flag.table}</span>
                    <span role="cell">{flag.index}</span>
                    <span role="cell">{flag.kind}</span>
                    <span role="cell">{flag.name}</span>
                    <span role="cell">{flag.low32Key}</span>
                  </button>
                ))}
              </div>

              <div className="flagwork-table" role="table" aria-label="Save key records">
                <div className="flagwork-row flagwork-row-heading" role="row">
                  <span role="columnheader">Key</span>
                  <span role="columnheader">Kind</span>
                  <span role="columnheader">Value</span>
                  <span role="columnheader">Name</span>
                  <span role="columnheader">Hash</span>
                </div>
                {filteredSaveBlocks.map((saveBlock) => (
                  <button
                    className={`flagwork-row ${
                      selectedSaveBlock?.blockId === saveBlock.blockId
                        ? 'flagwork-row-selected'
                        : ''
                    }`}
                    key={saveBlock.blockId}
                    onClick={() => onSelectSaveBlock(saveBlock.blockId)}
                    role="row"
                    type="button"
                  >
                    <span role="cell">{saveBlock.key}</span>
                    <span role="cell">{saveBlock.kind}</span>
                    <span role="cell">{saveBlock.valueKind}</span>
                    <span role="cell">{saveBlock.name}</span>
                    <span role="cell">{saveBlock.hash}</span>
                  </button>
                ))}
              </div>
            </div>

            <SelectedFlagworkSavePanel
              flag={selectedFlag}
              saveBlock={selectedSaveBlock}
              saveFile={workflow.saveFile}
            />
          </div>
        ) : (
          <p className="empty-copy">
            Open Flagwork from Workflows to inspect backend flagwork tables.
          </p>
        )}
      </section>

      <DiagnosticsSection diagnostics={workflow?.diagnostics ?? []} />
    </>
  );
}

function SelectedFlagworkSavePanel({
  flag,
  saveBlock,
  saveFile
}: {
  flag: FlagRecord | null;
  saveBlock: SaveBlockRecord | null;
  saveFile: SaveFileRecord | null;
}) {
  const provenance = saveBlock?.provenance ?? flag?.provenance ?? null;

  return (
    <aside aria-label="Selected flagwork provenance" className="encounter-inspector">
      <div className="panel-heading">
        <Save aria-hidden="true" size={18} />
        <h3>Selected Save Key</h3>
      </div>

      {flag || saveBlock || saveFile ? (
        <>
          <dl className="item-provenance-list">
            <div>
              <dt>Flagwork name</dt>
              <dd>{flag?.name ?? 'n/a'}</dd>
            </div>
            <div>
              <dt>Table</dt>
              <dd>{flag ? `${flag.table} #${flag.index}` : 'n/a'}</dd>
            </div>
            <div>
              <dt>Kind</dt>
              <dd>{flag?.kind ?? saveBlock?.kind ?? 'n/a'}</dd>
            </div>
            <div>
              <dt>Hash</dt>
              <dd>{flag?.hash ?? saveBlock?.hash ?? 'n/a'}</dd>
            </div>
            <div>
              <dt>Low32 key</dt>
              <dd>{flag?.low32Key ?? saveBlock?.key ?? 'n/a'}</dd>
            </div>
            <div>
              <dt>Save key name</dt>
              <dd>{saveBlock?.name ?? 'n/a'}</dd>
            </div>
            <div>
              <dt>Value kind</dt>
              <dd>{saveBlock?.valueKind ?? flag?.valueKind ?? 'n/a'}</dd>
            </div>
            <div>
              <dt>Source file</dt>
              <dd>{provenance?.sourceFile ?? 'n/a'}</dd>
            </div>
            <div>
              <dt>Layer</dt>
              <dd>{provenance ? formatSourceLayer(provenance.sourceLayer) : 'n/a'}</dd>
            </div>
            <div>
              <dt>File state</dt>
              <dd>{provenance ? formatFileState(provenance.fileState) : 'n/a'}</dd>
            </div>
            <div>
              <dt>Save file</dt>
              <dd>{saveFile?.fileName ?? 'n/a'}</dd>
            </div>
            <div>
              <dt>Save size</dt>
              <dd>{saveFile ? formatByteCount(saveFile.sizeBytes) : 'n/a'}</dd>
            </div>
            <div>
              <dt>Save status</dt>
              <dd>{saveFile?.status ?? 'n/a'}</dd>
            </div>
          </dl>

          <div className="encounter-edit-form">
            <dl className="encounter-slot-detail">
              <div>
                <dt>Flag ID</dt>
                <dd>{flag?.flagId ?? 'n/a'}</dd>
              </div>
              <div>
                <dt>Save ID</dt>
                <dd>{saveBlock?.blockId ?? 'n/a'}</dd>
              </div>
              <div>
                <dt>Default</dt>
                <dd>{flag?.defaultValue ?? 'n/a'}</dd>
              </div>
              <div>
                <dt>Save SHA-256</dt>
                <dd>{saveFile?.sha256 ?? 'n/a'}</dd>
              </div>
            </dl>
          </div>
        </>
      ) : (
        <p className="empty-copy">No flagwork record selected.</p>
      )}
    </aside>
  );
}

function ExeFsPatchSection({
  isStaging,
  onSearchChange,
  onSelectCheck,
  onSelectPatch,
  onStagePatch,
  searchText,
  selectedCheckId,
  selectedPatchId,
  workflow
}: {
  isStaging: boolean;
  onSearchChange: (value: string) => void;
  onSelectCheck: (checkId: string | null) => void;
  onSelectPatch: (patchId: string | null) => void;
  onStagePatch: (patchId: string) => void;
  searchText: string;
  selectedCheckId: string | null;
  selectedPatchId: string | null;
  workflow: ExeFsPatchWorkflow | null;
}) {
  const filteredPatches = filterExeFsPatchRecords(workflow?.patches ?? [], searchText);
  const filteredChecks = filterExeFsPatchCheckRecords(workflow?.checks ?? [], searchText);
  const filteredSegments = filterExeFsSegmentRecords(workflow?.segments ?? [], searchText);
  const visibleSegments = filteredSegments.length > 0 ? filteredSegments : (workflow?.segments ?? []);
  const selectedPatch =
    filteredPatches.find((patch) => patch.patchId === selectedPatchId) ??
    workflow?.patches.find((patch) => patch.patchId === selectedPatchId) ??
    filteredPatches[0] ??
    workflow?.patches[0] ??
    null;
  const selectedCheck =
    filteredChecks.find((check) => check.checkId === selectedCheckId) ??
    workflow?.checks.find((check) => check.checkId === selectedCheckId) ??
    filteredChecks[0] ??
    workflow?.checks[0] ??
    null;

  useEffect(() => {
    if (selectedPatch && selectedPatch.patchId !== selectedPatchId) {
      onSelectPatch(selectedPatch.patchId);
    }
  }, [onSelectPatch, selectedPatch?.patchId, selectedPatchId]);

  useEffect(() => {
    if (selectedCheck && selectedCheck.checkId !== selectedCheckId) {
      onSelectCheck(selectedCheck.checkId);
    }
  }, [onSelectCheck, selectedCheck?.checkId, selectedCheckId]);

  return (
    <>
      <section aria-labelledby="exefs-patches-heading" className="panel wide-panel">
        <div className="panel-heading">
          <Wrench aria-hidden="true" size={18} />
          <h2 id="exefs-patches-heading">ExeFS Patch Manager</h2>
        </div>

        <div className="items-toolbar exefs-toolbar">
          <label className="search-box items-search">
            <Search aria-hidden="true" size={18} />
            <input
              aria-label="Search ExeFS compatibility checks"
              disabled={!workflow}
              onChange={(event) => onSearchChange(event.target.value)}
              placeholder="Search ExeFS"
              type="search"
              value={searchText}
            />
          </label>
          <Metric label="Checks" value={workflow ? workflow.stats.totalCheckCount.toString() : '0'} />
          <Metric label="Passing" value={workflow ? workflow.stats.passCount.toString() : '0'} />
          <Metric label="Warnings" value={workflow ? workflow.stats.warningCount.toString() : '0'} />
          <Metric label="Failing" value={workflow ? workflow.stats.failCount.toString() : '0'} />
        </div>

        {workflow ? (
          <div className="flagwork-layout">
            <div className="flagwork-stack">
              <div className="exefs-table" role="table" aria-label="ExeFS patch records">
                <div className="exefs-row exefs-row-heading" role="row">
                  <span role="columnheader">Patch</span>
                  <span role="columnheader">Status</span>
                  <span role="columnheader">Target</span>
                  <span role="columnheader">Kind</span>
                  <span role="columnheader">Details</span>
                </div>
                {filteredPatches.map((patch) => (
                  <button
                    className={`exefs-row ${
                      selectedPatch?.patchId === patch.patchId ? 'exefs-row-selected' : ''
                    }`}
                    key={patch.patchId}
                    onClick={() => onSelectPatch(patch.patchId)}
                    role="row"
                    type="button"
                  >
                    <span role="cell">{patch.name}</span>
                    <span role="cell">
                      <span className={`status-pill ${getExeFsStatusClassName(patch.status)}`}>
                        {patch.status}
                      </span>
                    </span>
                    <span role="cell">{patch.targetFile}</span>
                    <span role="cell">{patch.patchKind}</span>
                    <span role="cell">{patch.details[0] ?? patch.description}</span>
                  </button>
                ))}
              </div>

              <div className="exefs-table" role="table" aria-label="ExeFS compatibility checks">
                <div className="exefs-row exefs-check-row exefs-row-heading" role="row">
                  <span role="columnheader">Check</span>
                  <span role="columnheader">Status</span>
                  <span role="columnheader">Area</span>
                  <span role="columnheader">Offset</span>
                  <span role="columnheader">Actual</span>
                </div>
                {filteredChecks.map((check) => (
                  <button
                    className={`exefs-row exefs-check-row ${
                      selectedCheck?.checkId === check.checkId ? 'exefs-row-selected' : ''
                    }`}
                    key={check.checkId}
                    onClick={() => {
                      onSelectCheck(check.checkId);
                      onSelectPatch(check.patchId);
                    }}
                    role="row"
                    type="button"
                  >
                    <span role="cell">{check.name}</span>
                    <span role="cell">
                      <span className={`status-pill ${getExeFsStatusClassName(check.status)}`}>
                        {check.status}
                      </span>
                    </span>
                    <span role="cell">{check.area}</span>
                    <span role="cell">{check.offset || 'n/a'}</span>
                    <span role="cell">{check.actual}</span>
                  </button>
                ))}
              </div>
            </div>

            <SelectedExeFsPatchPanel
              check={selectedCheck}
              isStaging={isStaging}
              onStagePatch={onStagePatch}
              patch={selectedPatch}
              segments={visibleSegments}
            />
          </div>
        ) : (
          <p className="empty-copy">
            Open ExeFS from Workflows to inspect backend patch compatibility.
          </p>
        )}
      </section>

      <DiagnosticsSection diagnostics={workflow?.diagnostics ?? []} />
    </>
  );
}

function SelectedExeFsPatchPanel({
  check,
  isStaging,
  onStagePatch,
  patch,
  segments
}: {
  check: ExeFsPatchCheckRecord | null;
  isStaging: boolean;
  onStagePatch: (patchId: string) => void;
  patch: ExeFsPatchRecord | null;
  segments: ExeFsSegmentRecord[];
}) {
  const provenance = check?.provenance ?? patch?.provenance ?? segments[0]?.provenance ?? null;
  const canStagePatch = patch?.status === 'available' || patch?.status === 'warning';

  return (
    <aside aria-label="Selected ExeFS provenance" className="encounter-inspector">
      <div className="panel-heading">
        <Wrench aria-hidden="true" size={18} />
        <h3>Selected Check</h3>
      </div>

      {patch || check ? (
        <>
          <dl className="item-provenance-list">
            <div>
              <dt>Patch</dt>
              <dd>{patch?.name ?? check?.patchId ?? 'n/a'}</dd>
            </div>
            <div>
              <dt>Status</dt>
              <dd>{check?.status ?? patch?.status ?? 'n/a'}</dd>
            </div>
            <div>
              <dt>Check</dt>
              <dd>{check?.name ?? 'n/a'}</dd>
            </div>
            <div>
              <dt>Area</dt>
              <dd>{check ? `${check.area} ${check.offset}`.trim() : 'n/a'}</dd>
            </div>
            <div>
              <dt>Expected</dt>
              <dd>{check?.expected ?? 'n/a'}</dd>
            </div>
            <div>
              <dt>Actual</dt>
              <dd>{check?.actual ?? 'n/a'}</dd>
            </div>
            <div>
              <dt>Source file</dt>
              <dd>{provenance?.sourceFile ?? 'n/a'}</dd>
            </div>
            <div>
              <dt>Layer</dt>
              <dd>{provenance ? formatSourceLayer(provenance.sourceLayer) : 'n/a'}</dd>
            </div>
            <div>
              <dt>File state</dt>
              <dd>{provenance ? formatFileState(provenance.fileState) : 'n/a'}</dd>
            </div>
          </dl>

          <div className="encounter-edit-form">
            {patch ? (
              <div className="form-actions">
                <button
                  className="primary-button"
                  disabled={!canStagePatch || isStaging}
                  onClick={() => onStagePatch(patch.patchId)}
                  type="button"
                >
                  <Wrench aria-hidden="true" size={16} />
                  <span>{isStaging ? 'Staging' : 'Stage Patch'}</span>
                </button>
              </div>
            ) : null}

            <dl className="encounter-slot-detail">
              <div>
                <dt>Notes</dt>
                <dd>{check?.notes ?? patch?.description ?? 'n/a'}</dd>
              </div>
              <div>
                <dt>Patch details</dt>
                <dd>{patch?.details.join(' | ') ?? 'n/a'}</dd>
              </div>
            </dl>

            <div className="exefs-segment-list" aria-label="ExeFS segments">
              {segments.map((segment) => (
                <dl className="encounter-slot-detail" key={segment.segmentId}>
                  <div>
                    <dt>{segment.name}</dt>
                    <dd>{segment.hashStatus}</dd>
                  </div>
                  <div>
                    <dt>File</dt>
                    <dd>{segment.fileOffset}</dd>
                  </div>
                  <div>
                    <dt>Memory</dt>
                    <dd>{segment.memoryOffset}</dd>
                  </div>
                  <div>
                    <dt>Size</dt>
                    <dd>{segment.decompressedSize}</dd>
                  </div>
                </dl>
              ))}
            </div>
          </div>
        </>
      ) : (
        <p className="empty-copy">No ExeFS check selected.</p>
      )}
    </aside>
  );
}

function RoyalCandySection({
  changePlan,
  editSession,
  isChangePlanApplying,
  isChangePlanCreating,
  isStaging,
  onApplyChangePlan,
  onCreateChangePlan,
  onSearchChange,
  onSelectCheck,
  onSelectWorkflow,
  onStageWorkflow,
  searchText,
  selectedCheckId,
  selectedWorkflowId,
  workflow
}: {
  changePlan: ChangePlan | null;
  editSession: EditSession | null;
  isChangePlanApplying: boolean;
  isChangePlanCreating: boolean;
  isStaging: boolean;
  onApplyChangePlan: () => void;
  onCreateChangePlan: () => void;
  onSearchChange: (value: string) => void;
  onSelectCheck: (checkId: string | null) => void;
  onSelectWorkflow: (workflowId: string | null) => void;
  onStageWorkflow: (workflowId: string) => void;
  searchText: string;
  selectedCheckId: string | null;
  selectedWorkflowId: string | null;
  workflow: RoyalCandyWorkflow | null;
}) {
  const filteredWorkflows = filterRoyalCandyWorkflows(workflow?.workflows ?? [], searchText);
  const filteredChecks = filterRoyalCandyChecks(workflow?.checks ?? [], searchText);
  const filteredOutputs = filterRoyalCandyOutputs(workflow?.outputs ?? [], searchText);
  const selectedWorkflow =
    filteredWorkflows.find((candidate) => candidate.workflowId === selectedWorkflowId) ??
    workflow?.workflows.find((candidate) => candidate.workflowId === selectedWorkflowId) ??
    filteredWorkflows[0] ??
    workflow?.workflows[0] ??
    null;
  const visibleChecks = filteredChecks.filter(
    (check) =>
      !selectedWorkflow ||
      check.workflowId === selectedWorkflow.workflowId ||
      check.workflowId === 'royal-candy-preflight'
  );
  const selectedCheck =
    visibleChecks.find((check) => check.checkId === selectedCheckId) ??
    workflow?.checks.find((check) => check.checkId === selectedCheckId) ??
    visibleChecks[0] ??
    workflow?.checks[0] ??
    null;
  const visibleOutputs = selectedWorkflow
    ? filteredOutputs.filter((output) => output.workflowId === selectedWorkflow.workflowId)
    : filteredOutputs;
  const visibleDiagnostics =
    selectedWorkflow?.workflowId === 'royal-candy-uninstall'
      ? (workflow?.diagnostics ?? []).filter(
          (diagnostic) => !diagnostic.message.includes('preflight is blocked')
        )
      : (workflow?.diagnostics ?? []);

  useEffect(() => {
    if (selectedWorkflow && selectedWorkflow.workflowId !== selectedWorkflowId) {
      onSelectWorkflow(selectedWorkflow.workflowId);
    }
  }, [onSelectWorkflow, selectedWorkflow?.workflowId, selectedWorkflowId]);

  useEffect(() => {
    if (selectedCheck && selectedCheck.checkId !== selectedCheckId) {
      onSelectCheck(selectedCheck.checkId);
    }
  }, [onSelectCheck, selectedCheck?.checkId, selectedCheckId]);

  return (
    <>
      <section aria-labelledby="royal-candy-heading" className="panel wide-panel">
        <div className="panel-heading">
          <CheckCircle aria-hidden="true" size={18} />
          <h2 id="royal-candy-heading">Royal Candy Workflows</h2>
        </div>

        <div className="items-toolbar exefs-toolbar">
          <label className="search-box items-search">
            <Search aria-hidden="true" size={18} />
            <input
              aria-label="Search Royal Candy workflows"
              disabled={!workflow}
              onChange={(event) => onSearchChange(event.target.value)}
              placeholder="Search Royal Candy"
              type="search"
              value={searchText}
            />
          </label>
          <Metric
            label="Checks"
            value={workflow ? workflow.stats.totalCheckCount.toString() : '0'}
          />
          <Metric label="Passing" value={workflow ? workflow.stats.passCount.toString() : '0'} />
          <Metric
            label="Warnings"
            value={workflow ? workflow.stats.warningCount.toString() : '0'}
          />
          <Metric label="Outputs" value={workflow ? workflow.stats.outputCount.toString() : '0'} />
        </div>

        {workflow ? (
          <div className="flagwork-layout">
            <div className="flagwork-stack">
              <div className="exefs-table" role="table" aria-label="Royal Candy workflows">
                <div className="exefs-row royal-candy-workflow-row exefs-row-heading" role="row">
                  <span role="columnheader">Workflow</span>
                  <span role="columnheader">Status</span>
                  <span role="columnheader">Mode</span>
                  <span role="columnheader">Item</span>
                  <span role="columnheader">Target</span>
                </div>
                {filteredWorkflows.map((candidate) => (
                  <button
                    className={`exefs-row royal-candy-workflow-row ${
                      selectedWorkflow?.workflowId === candidate.workflowId
                        ? 'exefs-row-selected'
                        : ''
                    }`}
                    key={candidate.workflowId}
                    onClick={() => onSelectWorkflow(candidate.workflowId)}
                    role="row"
                    type="button"
                  >
                    <span role="cell">{candidate.name}</span>
                    <span role="cell">
                      <span className={`status-pill ${getExeFsStatusClassName(candidate.status)}`}>
                        {candidate.status}
                      </span>
                    </span>
                    <span role="cell">{candidate.mode}</span>
                    <span role="cell">
                      {candidate.itemId} from {candidate.templateItemId}
                    </span>
                    <span role="cell">{candidate.target}</span>
                  </button>
                ))}
              </div>

              <div className="exefs-table" role="table" aria-label="Royal Candy preflight checks">
                <div className="exefs-row royal-candy-check-row exefs-row-heading" role="row">
                  <span role="columnheader">Check</span>
                  <span role="columnheader">Status</span>
                  <span role="columnheader">Area</span>
                  <span role="columnheader">Target</span>
                  <span role="columnheader">Message</span>
                </div>
                {visibleChecks.map((check) => (
                  <button
                    className={`exefs-row royal-candy-check-row ${
                      selectedCheck?.checkId === check.checkId ? 'exefs-row-selected' : ''
                    }`}
                    key={check.checkId}
                    onClick={() => onSelectCheck(check.checkId)}
                    role="row"
                    type="button"
                  >
                    <span role="cell">{check.checkId.split(':').pop()}</span>
                    <span role="cell">
                      <span className={`status-pill ${getExeFsStatusClassName(check.status)}`}>
                        {check.status}
                      </span>
                    </span>
                    <span role="cell">{check.area}</span>
                    <span role="cell">{check.target}</span>
                    <span role="cell">{check.message}</span>
                  </button>
                ))}
              </div>
            </div>

            <SelectedRoyalCandyPanel
              check={selectedCheck}
              changePlan={changePlan}
              editSession={editSession}
              isChangePlanApplying={isChangePlanApplying}
              isChangePlanCreating={isChangePlanCreating}
              isStaging={isStaging}
              onApplyChangePlan={onApplyChangePlan}
              onCreateChangePlan={onCreateChangePlan}
              onStageWorkflow={onStageWorkflow}
              outputs={visibleOutputs}
              selectedWorkflow={selectedWorkflow}
            />
          </div>
        ) : (
          <p className="empty-copy">
            Open Royal Candy from Workflows to inspect backend preflight and output targets.
          </p>
        )}
      </section>

      <DiagnosticsSection diagnostics={visibleDiagnostics} />
    </>
  );
}

function SelectedRoyalCandyPanel({
  check,
  changePlan,
  editSession,
  isChangePlanApplying,
  isChangePlanCreating,
  isStaging,
  onApplyChangePlan,
  onCreateChangePlan,
  onStageWorkflow,
  outputs,
  selectedWorkflow
}: {
  check: RoyalCandyWorkflowCheckRecord | null;
  changePlan: ChangePlan | null;
  editSession: EditSession | null;
  isChangePlanApplying: boolean;
  isChangePlanCreating: boolean;
  isStaging: boolean;
  onApplyChangePlan: () => void;
  onCreateChangePlan: () => void;
  onStageWorkflow: (workflowId: string) => void;
  outputs: RoyalCandyOutputRecord[];
  selectedWorkflow: RoyalCandyWorkflowRecord | null;
}) {
  const provenance = check?.provenance ?? selectedWorkflow?.provenance ?? outputs[0]?.provenance ?? null;
  const canStage =
    selectedWorkflow !== null &&
    (selectedWorkflow.workflowId === 'royal-candy-unlimited' ||
      selectedWorkflow.workflowId === 'royal-candy-story-limits' ||
      selectedWorkflow.workflowId === 'royal-candy-uninstall') &&
    (selectedWorkflow.status === 'available' || selectedWorkflow.status === 'warning');
  const isCleanupWorkflow = selectedWorkflow?.workflowId === 'royal-candy-uninstall';
  const stagedRoyalCandyEdit = editSession?.pendingEdits.find(
    (edit) => edit.domain === 'workflow.royalCandy'
  );
  const isSelectedWorkflowStaged =
    selectedWorkflow !== null && stagedRoyalCandyEdit?.recordId === selectedWorkflow.workflowId;
  const canReviewPlan = isSelectedWorkflowStaged && !isChangePlanCreating;
  const canApplyPlan =
    isSelectedWorkflowStaged &&
    changePlan !== null &&
    changePlan.canApply &&
    changePlan.writes.length > 0 &&
    !isChangePlanApplying;

  return (
    <aside aria-label="Selected Royal Candy workflow provenance" className="encounter-inspector">
      <div className="panel-heading">
        <CheckCircle aria-hidden="true" size={18} />
        <h3>Selected Workflow</h3>
      </div>

      {selectedWorkflow ? (
        <>
          <dl className="item-provenance-list">
            <div>
              <dt>Workflow</dt>
              <dd>{selectedWorkflow.name}</dd>
            </div>
            <div>
              <dt>Status</dt>
              <dd>{selectedWorkflow.status}</dd>
            </div>
            <div>
              <dt>Mode</dt>
              <dd>{selectedWorkflow.mode}</dd>
            </div>
            <div>
              <dt>Item</dt>
              <dd>
                {selectedWorkflow.itemId} from {selectedWorkflow.templateItemId}
              </dd>
            </div>
            <div>
              <dt>Check</dt>
              <dd>{check?.checkId.split(':').pop() ?? 'n/a'}</dd>
            </div>
            <div>
              <dt>Check status</dt>
              <dd>{check?.status ?? 'n/a'}</dd>
            </div>
            <div>
              <dt>Source file</dt>
              <dd>{provenance?.sourceFile ?? 'n/a'}</dd>
            </div>
            <div>
              <dt>Layer</dt>
              <dd>{provenance ? formatSourceLayer(provenance.sourceLayer) : 'n/a'}</dd>
            </div>
            <div>
              <dt>File state</dt>
              <dd>{provenance ? formatFileState(provenance.fileState) : 'n/a'}</dd>
            </div>
          </dl>

          <div className="encounter-edit-form">
            <div className="form-actions">
              <button
                className="primary-button"
                disabled={!canStage || isStaging}
                onClick={() => {
                  if (selectedWorkflow) {
                    onStageWorkflow(selectedWorkflow.workflowId);
                  }
                }}
                type="button"
              >
                <ClipboardCheck aria-hidden="true" size={16} />
                <span>{isStaging ? 'Staging' : isCleanupWorkflow ? 'Stage Cleanup' : 'Stage Workflow'}</span>
              </button>
              <button
                className="secondary-button"
                disabled={!canReviewPlan}
                onClick={onCreateChangePlan}
                type="button"
              >
                <ClipboardCheck aria-hidden="true" size={16} />
                <span>{isChangePlanCreating ? 'Reviewing' : 'Review Plan'}</span>
              </button>
              <button
                className="primary-button"
                disabled={!canApplyPlan}
                onClick={onApplyChangePlan}
                type="button"
              >
                <Save aria-hidden="true" size={16} />
                <span>{isChangePlanApplying ? 'Applying' : isCleanupWorkflow ? 'Apply Cleanup' : 'Apply Plan'}</span>
              </button>
            </div>

            <dl className="encounter-slot-detail">
              <div>
                <dt>Description</dt>
                <dd>{selectedWorkflow.description}</dd>
              </div>
              <div>
                <dt>Check message</dt>
                <dd>{check?.message ?? 'n/a'}</dd>
              </div>
            </dl>

            <ol className="royal-candy-step-list">
              {selectedWorkflow.steps.map((step) => (
                <li key={step.step}>
                  <strong>{step.label}</strong>
                  <span>{step.description}</span>
                </li>
              ))}
            </ol>

            <ul className="royal-candy-output-list" aria-label="Royal Candy planned outputs">
              {outputs.map((output) => (
                <li key={output.outputId}>
                  <div className="royal-candy-output-main">
                    <span className="royal-candy-output-kind">{output.outputKind}</span>
                    <span className={`status-pill ${getExeFsStatusClassName(output.status)}`}>
                      {output.status}
                    </span>
                    <strong>{output.relativePath}</strong>
                  </div>
                  <span>{output.description}</span>
                  {output.sourceFile !== output.relativePath ? (
                    <small>Source: {output.sourceFile}</small>
                  ) : null}
                </li>
              ))}
            </ul>
          </div>
        </>
      ) : (
        <p className="empty-copy">No Royal Candy workflow selected.</p>
      )}
    </aside>
  );
}

function SpreadsheetImportSection({
  editSession,
  isPreviewing,
  onPreviewImport,
  onSearchChange,
  onSelectProfile,
  onSourcePathChange,
  preview,
  searchText,
  selectedProfileId,
  sourcePath,
  workflow
}: {
  editSession: EditSession | null;
  isPreviewing: boolean;
  onPreviewImport: (profileId: string, sourcePath: string) => void;
  onSearchChange: (searchText: string) => void;
  onSelectProfile: (profileId: string | null) => void;
  onSourcePathChange: (sourcePath: string) => void;
  preview: SpreadsheetImportPreview | null;
  searchText: string;
  selectedProfileId: string | null;
  sourcePath: string;
  workflow: SpreadsheetImportWorkflow | null;
}) {
  const filteredProfiles = filterSpreadsheetImportProfiles(workflow?.profiles ?? [], searchText);
  const selectedProfile =
    filteredProfiles.find((profile) => profile.profileId === selectedProfileId) ??
    workflow?.profiles.find((profile) => profile.profileId === selectedProfileId) ??
    filteredProfiles[0] ??
    workflow?.profiles[0] ??
    null;
  const canPreview =
    workflow?.summary.availability === 'available' &&
    selectedProfile?.status === 'available' &&
    sourcePath.trim().length > 0;
  const previewDiagnostics = preview?.rows.flatMap((row) => row.diagnostics) ?? [];

  useEffect(() => {
    if (selectedProfile && selectedProfile.profileId !== selectedProfileId) {
      onSelectProfile(selectedProfile.profileId);
    }
  }, [onSelectProfile, selectedProfile?.profileId, selectedProfileId]);

  return (
    <>
      <section aria-labelledby="spreadsheet-import-heading" className="panel wide-panel">
        <div className="panel-heading">
          <FileSpreadsheet aria-hidden="true" size={18} />
          <h2 id="spreadsheet-import-heading">Spreadsheet Import</h2>
        </div>

        <div className="items-toolbar spreadsheet-import-toolbar">
          <label className="search-box items-search">
            <Search aria-hidden="true" size={18} />
            <input
              aria-label="Search import profiles"
              disabled={!workflow}
              onChange={(event) => onSearchChange(event.target.value)}
              placeholder="Search imports"
              type="search"
              value={searchText}
            />
          </label>
          <Metric
            label="Profiles"
            value={workflow ? workflow.stats.totalProfileCount.toString() : '0'}
          />
          <Metric
            label="Accepted"
            value={preview ? preview.acceptedRowCount.toString() : '0'}
          />
          <Metric
            label="Pending changes"
            value={(editSession?.pendingEdits.length ?? 0).toString()}
          />
        </div>

        {workflow ? (
          <div className="flagwork-layout">
            <div className="flagwork-stack">
              <div className="exefs-table" role="table" aria-label="Spreadsheet import profiles">
                <div className="exefs-row spreadsheet-profile-row exefs-row-heading" role="row">
                  <span role="columnheader">Profile</span>
                  <span role="columnheader">Status</span>
                  <span role="columnheader">Target</span>
                  <span role="columnheader">Source</span>
                  <span role="columnheader">Columns</span>
                </div>
                {filteredProfiles.map((profile) => (
                  <button
                    className={`exefs-row spreadsheet-profile-row ${
                      selectedProfile?.profileId === profile.profileId ? 'exefs-row-selected' : ''
                    }`}
                    key={profile.profileId}
                    onClick={() => onSelectProfile(profile.profileId)}
                    role="row"
                    type="button"
                  >
                    <span role="cell">{profile.name}</span>
                    <span role="cell">
                      <span className={`status-pill ${getExeFsStatusClassName(profile.status)}`}>
                        {profile.status}
                      </span>
                    </span>
                    <span role="cell">{profile.targetWorkflow}</span>
                    <span role="cell">{profile.sourceKind}</span>
                    <span role="cell">{profile.columns.length}</span>
                  </button>
                ))}
              </div>

              <div className="spreadsheet-source-row">
                <label className="path-field">
                  <span>CSV/TSV source path</span>
                  <input
                    aria-label="CSV or TSV source path"
                    onChange={(event) => onSourcePathChange(event.target.value)}
                    placeholder="items.csv"
                    type="text"
                    value={sourcePath}
                  />
                </label>
                <button
                  className="primary-button"
                  disabled={!canPreview || isPreviewing}
                  onClick={() => {
                    if (selectedProfile) {
                      onPreviewImport(selectedProfile.profileId, sourcePath);
                    }
                  }}
                  type="button"
                >
                  <FileSpreadsheet aria-hidden="true" size={16} />
                  <span>{isPreviewing ? 'Previewing' : 'Preview Import'}</span>
                </button>
              </div>

              {preview ? (
                <div className="exefs-table" role="table" aria-label="Spreadsheet import preview">
                  <div className="exefs-row spreadsheet-preview-row exefs-row-heading" role="row">
                    <span role="columnheader">Row</span>
                    <span role="columnheader">Status</span>
                    <span role="columnheader">Record</span>
                    <span role="columnheader">Summary</span>
                  </div>
                  {preview.rows.map((row) => (
                    <div className="exefs-row spreadsheet-preview-row" key={row.rowNumber} role="row">
                      <span role="cell">{row.rowNumber}</span>
                      <span role="cell">
                        <span className={`status-pill ${getImportStatusClassName(row.status)}`}>
                          {row.status}
                        </span>
                      </span>
                      <span role="cell">{row.recordId || 'n/a'}</span>
                      <span role="cell">{row.summary}</span>
                    </div>
                  ))}
                </div>
              ) : null}
            </div>

            <SelectedSpreadsheetImportPanel profile={selectedProfile} preview={preview} />
          </div>
        ) : (
          <p className="empty-copy">
            Open Spreadsheet Import from Workflows to load backend import profiles.
          </p>
        )}
      </section>

      <DiagnosticsSection diagnostics={[...(workflow?.diagnostics ?? []), ...previewDiagnostics]} />
    </>
  );
}

function SelectedSpreadsheetImportPanel({
  preview,
  profile
}: {
  preview: SpreadsheetImportPreview | null;
  profile: SpreadsheetImportProfileRecord | null;
}) {
  return (
    <aside aria-label="Selected spreadsheet import provenance" className="encounter-inspector">
      <div className="panel-heading">
        <FileSpreadsheet aria-hidden="true" size={18} />
        <h3>Selected Import</h3>
      </div>

      {profile ? (
        <>
          <dl className="item-provenance-list">
            <div>
              <dt>Profile</dt>
              <dd>{profile.name}</dd>
            </div>
            <div>
              <dt>Status</dt>
              <dd>{profile.status}</dd>
            </div>
            <div>
              <dt>Target</dt>
              <dd>{profile.targetWorkflow}</dd>
            </div>
            <div>
              <dt>Source file</dt>
              <dd>{profile.provenance.sourceFile}</dd>
            </div>
            <div>
              <dt>Layer</dt>
              <dd>{formatSourceLayer(profile.provenance.sourceLayer)}</dd>
            </div>
            <div>
              <dt>File state</dt>
              <dd>{formatFileState(profile.provenance.fileState)}</dd>
            </div>
          </dl>

          <div className="encounter-edit-form">
            <dl className="encounter-slot-detail">
              <div>
                <dt>Rows</dt>
                <dd>{preview ? preview.totalRowCount : 0}</dd>
              </div>
              <div>
                <dt>Accepted</dt>
                <dd>{preview ? preview.acceptedRowCount : 0}</dd>
              </div>
              <div>
                <dt>Rejected</dt>
                <dd>{preview ? preview.rejectedRowCount : 0}</dd>
              </div>
            </dl>

            <div className="exefs-segment-list" aria-label="Spreadsheet import columns">
              {profile.columns.map((column) => (
                <dl className="encounter-slot-detail" key={column.header}>
                  <div>
                    <dt>{column.header}</dt>
                    <dd>{column.isRequired ? 'Required' : 'Optional'}</dd>
                  </div>
                  <div>
                    <dt>Kind</dt>
                    <dd>{column.valueKind}</dd>
                  </div>
                  <div>
                    <dt>Description</dt>
                    <dd>{column.description}</dd>
                  </div>
                </dl>
              ))}
            </div>
          </div>
        </>
      ) : (
        <p className="empty-copy">No import profile selected.</p>
      )}
    </aside>
  );
}

function ChangesSection({
  applyResult,
  canSaveValidatedChanges,
  changePlan,
  diagnostics,
  editSession,
  isEditSessionValidated,
  isChangePlanApplying,
  isChangePlanCreating,
  isSessionValidating,
  onCancelEditSession,
  onSaveValidatedChanges,
  onValidateEditSession
}: {
  applyResult: ApplyResult | null;
  canSaveValidatedChanges: boolean;
  changePlan: ChangePlan | null;
  diagnostics: ApiDiagnostic[];
  editSession: EditSession | null;
  isEditSessionValidated: boolean;
  isChangePlanApplying: boolean;
  isChangePlanCreating: boolean;
  isSessionValidating: boolean;
  onCancelEditSession: () => void;
  onSaveValidatedChanges: () => void;
  onValidateEditSession: () => void;
}) {
  const pendingEdits = editSession?.pendingEdits ?? [];
  const combinedDiagnostics = [
    ...diagnostics,
    ...(changePlan?.diagnostics ?? []),
    ...(applyResult?.diagnostics ?? [])
  ];

  return (
    <>
      <section aria-labelledby="changes-heading" className="panel wide-panel">
        <div className="panel-heading">
          <ClipboardCheck aria-hidden="true" size={18} />
          <h2 id="changes-heading">Edit Session</h2>
        </div>

        <div className="changes-summary">
          <Metric label="Pending changes" value={pendingEdits.length.toString()} />
          <Metric label="Target files" value={(changePlan?.writes.length ?? 0).toString()} />
          <Metric label="Written files" value={(applyResult?.writtenFiles.length ?? 0).toString()} />
          <Metric
            label="Validation"
            value={
              pendingEdits.length === 0
                ? 'No changes'
                : isEditSessionValidated
                  ? 'Valid'
                  : 'Needed'
            }
          />
          <button
            className="secondary-button"
            disabled={pendingEdits.length === 0 || isSessionValidating || isChangePlanApplying}
            onClick={onValidateEditSession}
            type="button"
          >
            <CheckCircle aria-hidden="true" size={18} />
            <span>
              {isSessionValidating || isChangePlanCreating
                ? 'Validating'
                : 'Validate Pending Changes'}
            </span>
          </button>
          <button
            className="primary-button"
            disabled={!canSaveValidatedChanges}
            onClick={onSaveValidatedChanges}
            type="button"
          >
            <Save aria-hidden="true" size={18} />
            <span>{isChangePlanApplying ? 'Saving' : 'Save'}</span>
          </button>
          <button
            className="danger-button"
            disabled={!editSession}
            onClick={onCancelEditSession}
            type="button"
          >
            <X aria-hidden="true" size={18} />
            <span>Cancel</span>
          </button>
        </div>

        {pendingEdits.length > 0 ? (
          <ul className="pending-edit-list">
            {pendingEdits.map((edit, index) => (
              <li key={`${edit.domain}-${edit.recordId ?? index}-${edit.field ?? 'field'}`}>
                <strong>{edit.summary}</strong>
                <dl className="pending-edit-meta">
                  <div>
                    <dt>Editor</dt>
                    <dd>{formatPendingEditDomain(edit.domain)}</dd>
                  </div>
                  <div>
                    <dt>Record</dt>
                    <dd>{edit.recordId ?? 'n/a'}</dd>
                  </div>
                  <div>
                    <dt>Field</dt>
                    <dd>{edit.field ?? 'n/a'}</dd>
                  </div>
                </dl>
                <span>{edit.sources.map((source) => source.relativePath).join(', ')}</span>
              </li>
            ))}
          </ul>
        ) : (
          <p className="empty-copy">No pending changes.</p>
        )}
      </section>

      {changePlan ? (
        <ChangePlanSection
          changePlan={changePlan}
        />
      ) : null}
      {applyResult ? <ApplyResultSection applyResult={applyResult} /> : null}
      <DiagnosticsSection diagnostics={combinedDiagnostics} />
    </>
  );
}

function ChangePlanSection({
  changePlan
}: {
  changePlan: ChangePlan;
}) {
  return (
    <section aria-labelledby="change-plan-heading" className="panel wide-panel">
      <div className="panel-heading">
        <ClipboardCheck aria-hidden="true" size={18} />
        <h2 id="change-plan-heading">Output Plan</h2>
      </div>

      <div className="change-plan-status">
        <Metric label="Plan status" value={changePlan.canApply ? 'Ready' : 'Needs fixes'} />
        <Metric label="Session" value={changePlan.sessionId} />
      </div>

      {changePlan.writes.length > 0 ? (
        <ul className="change-plan-list">
          {changePlan.writes.map((write) => (
            <li key={write.targetRelativePath}>
              <div>
                <strong>{write.targetRelativePath}</strong>
                <span>{write.reason}</span>
              </div>
              <dl>
                <div>
                  <dt>Output state</dt>
                  <dd>{write.replacesExistingOutput ? 'Replaces output file' : 'Creates output file'}</dd>
                </div>
                <div>
                  <dt>Sources</dt>
                  <dd>
                    {write.sources
                      .map((source) => `${formatProjectFileLayer(source.layer)} ${source.relativePath}`)
                      .join(', ')}
                  </dd>
                </div>
              </dl>
            </li>
          ))}
        </ul>
      ) : (
        <p className="empty-copy">No target files in this plan.</p>
      )}
    </section>
  );
}

function ApplyResultSection({ applyResult }: { applyResult: ApplyResult }) {
  return (
    <section aria-labelledby="apply-result-heading" className="panel wide-panel">
      <div className="panel-heading">
        <CheckCircle aria-hidden="true" size={18} />
        <h2 id="apply-result-heading">Save Result</h2>
      </div>

      <div className="change-plan-status">
        <Metric label="Save ID" value={applyResult.applyId} />
        <Metric label="Written files" value={applyResult.writtenFiles.length.toString()} />
      </div>

      {applyResult.writtenFiles.length > 0 ? (
        <ul className="written-file-list">
          {applyResult.writtenFiles.map((writtenFile) => (
            <li key={writtenFile}>{writtenFile}</li>
          ))}
        </ul>
      ) : (
        <p className="empty-copy">No files were written.</p>
      )}
    </section>
  );
}

function SaveProgressModal({ progress }: { progress: SaveProgressState }) {
  return (
    <div className="modal-backdrop" role="presentation">
      <section
        aria-labelledby="save-progress-heading"
        aria-modal="true"
        className="modal-panel save-progress-panel"
        role="dialog"
      >
        <div className="panel-heading">
          <Save aria-hidden="true" size={18} />
          <h2 id="save-progress-heading">{progress.label}</h2>
        </div>
        <div className="save-progress-track" aria-label="Save progress">
          <div className="save-progress-fill" style={{ width: `${progress.percent}%` }} />
        </div>
        <dl className="save-progress-detail">
          <div>
            <dt>Progress</dt>
            <dd>{progress.percent}%</dd>
          </div>
          <div>
            <dt>File</dt>
            <dd>{progress.detail}</dd>
          </div>
          <div>
            <dt>Step</dt>
            <dd>
              {progress.step} / {progress.totalSteps}
            </dd>
          </div>
        </dl>
      </section>
    </div>
  );
}

function ExitPromptModal({
  mode,
  onConfirmDiscard,
  onDeclineDiscard,
  onGoToChanges,
  onStay
}: {
  mode: ExitPromptState['mode'];
  onConfirmDiscard: () => void;
  onDeclineDiscard: () => void;
  onGoToChanges: () => void;
  onStay: () => void;
}) {
  const isConfirmMode = mode === 'confirm';

  return (
    <div className="modal-backdrop" role="presentation">
      <section
        aria-labelledby="exit-prompt-heading"
        aria-modal="true"
        className="modal-panel"
        role="dialog"
      >
        <div className="panel-heading">
          <X aria-hidden="true" size={18} />
          <h2 id="exit-prompt-heading">
            {isConfirmMode ? 'Discard Pending Changes?' : 'Where To Go?'}
          </h2>
        </div>
        <p className="modal-copy">
          {isConfirmMode
            ? 'This editor has pending changes or an active edit session. Exiting will discard those pending edits.'
            : 'You can stay on this editor or go to Changes to validate and save the pending edits.'}
        </p>
        <div className="modal-actions">
          {isConfirmMode ? (
            <>
              <button className="danger-button" onClick={onConfirmDiscard} type="button">
                <Trash2 aria-hidden="true" size={16} />
                <span>Yes, Discard</span>
              </button>
              <button className="secondary-button" onClick={onDeclineDiscard} type="button">
                <X aria-hidden="true" size={16} />
                <span>No</span>
              </button>
            </>
          ) : (
            <>
              <button className="secondary-button" onClick={onStay} type="button">
                <ArrowLeftRight aria-hidden="true" size={16} />
                <span>Stay Here</span>
              </button>
              <button className="primary-button" onClick={onGoToChanges} type="button">
                <ClipboardCheck aria-hidden="true" size={16} />
                <span>Go To Changes</span>
              </button>
            </>
          )}
        </div>
      </section>
    </div>
  );
}

function PathStatusSection({ health }: { health: ProjectHealth | null }) {
  return (
    <section aria-labelledby="paths-heading" className="panel">
      <div className="panel-heading">
        <ShieldCheck aria-hidden="true" size={18} />
        <h2 id="paths-heading">Paths</h2>
      </div>

      <dl className="path-list">
        {pathFields.map((pathField) => {
          const pathValidation = health?.paths.find((path) => path.role === pathField.role);

          return (
            <div className="path-row" key={pathField.role}>
              <dt>{pathField.label}</dt>
              <dd className={getPathStatusClassName(pathValidation)}>
                {pathValidation ? pathStatusLabels[pathValidation.status] : 'Not checked'}
              </dd>
            </div>
          );
        })}
      </dl>
    </section>
  );
}

function DiagnosticsSection({ diagnostics }: { diagnostics: ApiDiagnostic[] }) {
  return (
    <section aria-labelledby="diagnostics-heading" className="panel">
      <div className="panel-heading">
        <Activity aria-hidden="true" size={18} />
        <h2 id="diagnostics-heading">Diagnostics</h2>
      </div>

      {diagnostics.length > 0 ? (
        <ul className="diagnostic-list">
          {diagnostics.map((diagnostic) => (
            <li className={`diagnostic diagnostic-${diagnostic.severity}`} key={diagnostic.message}>
              <strong>{diagnostic.severity}</strong>
              <span>{diagnostic.message}</span>
            </li>
          ))}
        </ul>
      ) : (
        <p className="empty-copy">No diagnostics.</p>
      )}
    </section>
  );
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div className="metric">
      <span className="metric-label">{label}</span>
      <span className="metric-value metric-value-small">{value}</span>
    </div>
  );
}

function filterItems(items: ItemRecord[], searchText: string) {
  const normalizedSearch = searchText.trim().toLocaleLowerCase();

  if (normalizedSearch.length === 0) {
    return items;
  }

  return items.filter((item) =>
    [
      item.itemId.toString(),
      item.name,
      item.category,
      item.buyPrice.toString(),
      item.sellPrice.toString(),
      item.wattsPrice.toString(),
      item.alternatePrice.toString(),
      ...item.detailGroups.flatMap((group) => [
        group.label,
        ...group.details.flatMap((detail) => [detail.label, detail.value])
      ])
    ].some((value) => value.toLocaleLowerCase().includes(normalizedSearch))
  );
}

function filterPokemon(pokemon: PokemonRecord[], searchText: string) {
  const normalizedSearch = searchText.trim().toLocaleLowerCase();

  if (normalizedSearch.length === 0) {
    return pokemon;
  }

  return pokemon.filter((record) =>
    [
      record.personalId.toString(),
      record.speciesId.toString(),
      record.form.toString(),
      record.name,
      record.formLabel,
      record.type1,
      record.type2,
      record.baseStats.hp.toString(),
      record.baseStats.attack.toString(),
      record.baseStats.defense.toString(),
      record.baseStats.specialAttack.toString(),
      record.baseStats.specialDefense.toString(),
      record.baseStats.speed.toString(),
      record.baseStats.total.toString(),
      record.abilities.ability1.toString(),
      record.abilities.ability1Label,
      record.abilities.ability2.toString(),
      record.abilities.ability2Label,
      record.abilities.hiddenAbility.toString(),
      record.abilities.hiddenAbilityLabel,
      record.genderRatio.toString(),
      record.genderRatioLabel,
      record.provenance.sourceFile,
      ...record.compatibility.flatMap((group) => [
        group.groupId,
        group.label,
        group.enabledCount.toString(),
        ...group.entries.flatMap((entry) => [
          entry.slot.toString(),
          entry.moveId.toString(),
          entry.moveName,
          entry.label,
          entry.canLearn ? 'enabled' : 'disabled'
        ])
      ]),
      ...record.evolutions.flatMap((evolution) => [
        evolution.slot.toString(),
        evolution.method.toString(),
        evolution.methodName,
        evolution.argument.toString(),
        evolution.argumentKind,
        evolution.argumentLabel,
        evolution.argumentValue,
        evolution.species.toString(),
        evolution.form.toString(),
        evolution.level.toString()
      ]),
      ...record.learnset.flatMap((move) => [
        move.slot.toString(),
        move.moveId.toString(),
        move.moveName,
        move.level.toString()
      ])
    ].some((value) => value.toLocaleLowerCase().includes(normalizedSearch))
  );
}

function filterPokemonCompatibilityEntries(
  group: PokemonCompatibilityGroup,
  searchText: string
) {
  const normalizedSearch = searchText.trim().toLocaleLowerCase();

  if (normalizedSearch.length === 0) {
    return group.entries;
  }

  return group.entries.filter((entry) =>
    [
      entry.slot.toString(),
      entry.moveId.toString(),
      entry.moveName,
      entry.label,
      entry.canLearn ? 'enabled' : 'disabled'
    ].some((value) => value.toLocaleLowerCase().includes(normalizedSearch))
  );
}

function createPokemonCompatibilityFieldName(groupId: string, slot: number) {
  return `compatibility:${groupId}:${slot}`;
}

function formatReferenceLabel(
  labels: ReadonlyMap<number, string>,
  value: number,
  fallbackPrefix: string
) {
  return labels.get(value) ?? `${fallbackPrefix} ${value}`;
}

function formatEvolutionFormReference(
  speciesLabels: ReadonlyMap<number, string>,
  speciesId: number,
  form: number
) {
  return createEvolutionFormOptions(speciesId, speciesLabels, form.toString()).find(
    (option) => option.value === form
  )?.label ?? `Form ${form}`;
}

function createEvolutionFormOptions(
  speciesId: number | null,
  speciesLabels: ReadonlyMap<number, string>,
  draftValue: string
): EditableFieldOption[] {
  const formValues = new Set<number>([0]);
  const parsedDraft = Number.parseInt(draftValue, 10);
  if (Number.isInteger(parsedDraft) && parsedDraft >= 0 && parsedDraft <= 255) {
    formValues.add(parsedDraft);
  }

  const context = createEvolutionFormOptionContext(speciesId, speciesLabels);
  if (context !== null) {
    addKnownSpeciesFormValues(context, formValues);
    if (formValues.size === 1 && speciesHasKnownAlternateForms(context)) {
      formValues.add(1);
    }
  }

  return [...formValues]
    .sort((left, right) => left - right)
    .map((form) => ({
      label: context === null ? (form === 0 ? 'Base' : `Form ${form}`) : formatSpeciesFormOptionLabel(form, context),
      value: form
    }));
}

function createEvolutionFormOptionContext(
  speciesId: number | null,
  speciesLabels: ReadonlyMap<number, string>
): SpeciesFormOptionContext | null {
  if (speciesId === null || !Number.isInteger(speciesId)) {
    return null;
  }

  const speciesLabel = formatReferenceLabel(speciesLabels, speciesId, 'Species');
  const species = getReferenceSpriteName(speciesLabel);
  return {
    species: species || speciesLabel,
    speciesId
  };
}

function addKnownSpeciesFormValues(context: SpeciesFormOptionContext, formValues: Set<number>) {
  if (context.speciesId !== undefined) {
    for (const key of regionalFormLabelsBySpeciesId.keys()) {
      const [speciesIdText, formText] = key.split(':');
      if (Number.parseInt(speciesIdText, 10) === context.speciesId) {
        formValues.add(Number.parseInt(formText, 10));
      }
    }
  }

  const normalizedSpecies = normalizeSpeciesName(context.species);
  for (const key of regionalFormLabelsBySpeciesName.keys()) {
    const [speciesName, formText] = key.split(':');
    if (speciesName === normalizedSpecies) {
      formValues.add(Number.parseInt(formText, 10));
    }
  }
}

function addCurrentEvolutionMethodOption(
  options: PokemonEvolutionMethodOption[],
  draftValue: string
) {
  const parsedValue = parseEditableIntegerDraft(draftValue, options);
  if (
    parsedValue === null ||
    !Number.isInteger(parsedValue) ||
    options.some((option) => option.value === parsedValue)
  ) {
    return options;
  }

  return [
    ...options,
    {
      argumentKind: 'value',
      argumentLabel: 'Argument',
      argumentOptions: [],
      label: `${parsedValue.toString().padStart(3, '0')} Method ${parsedValue}`,
      value: parsedValue
    }
  ];
}

function addCurrentPokemonFieldOption(
  options: PokemonEditableFieldOption[],
  draftValue: string,
  fallbackLabel: string
) {
  const parsedValue = parseEditableIntegerDraft(draftValue, options);
  if (
    parsedValue === null ||
    !Number.isInteger(parsedValue) ||
    options.some((option) => option.value === parsedValue)
  ) {
    return options;
  }

  return [
    ...options,
    {
      label: `${parsedValue} ${fallbackLabel}`,
      value: parsedValue
    }
  ];
}

function addDraftFallbackOption(
  options: EditableFieldOption[],
  draftValue: string,
  fallbackLabel: string
) {
  const parsedValue = parseEditableIntegerDraft(draftValue, options);
  return parsedValue !== null &&
    Number.isInteger(parsedValue) &&
    !options.some((option) => option.value === parsedValue)
    ? [
        {
          label: fallbackLabel,
          value: parsedValue!
        },
        ...options
      ]
    : options;
}

function findEvolutionMethodOption(
  options: PokemonEvolutionMethodOption[],
  draftValue: string
) {
  const parsedValue = parseEditableIntegerDraft(draftValue, options);
  return parsedValue !== null && Number.isInteger(parsedValue)
    ? options.find((option) => option.value === parsedValue) ?? null
    : null;
}

function getDefaultEvolutionArgumentDraft(methodOption: PokemonEvolutionMethodOption | null) {
  if (
    !methodOption ||
    methodOption.argumentKind === 'none' ||
    methodOption.argumentKind === 'level'
  ) {
    return '0';
  }

  return methodOption.argumentOptions[0]?.value.toString() ?? '0';
}

function usesEvolutionArgumentSelector(methodOption: PokemonEvolutionMethodOption | null) {
  return Boolean(
    methodOption &&
      methodOption.argumentKind !== 'none' &&
      methodOption.argumentKind !== 'level' &&
      methodOption.argumentOptions.length > 0
  );
}

function usesEvolutionArgumentNumberInput(methodOption: PokemonEvolutionMethodOption | null) {
  return Boolean(
    methodOption &&
      methodOption.argumentKind !== 'none' &&
      methodOption.argumentKind !== 'level' &&
      methodOption.argumentOptions.length === 0
  );
}

function formatEvolutionMethodSummary(evolution: PokemonEvolutionRecord) {
  const methodName = evolution.methodName || `Method ${evolution.method}`;
  return `${evolution.method.toString().padStart(3, '0')} ${methodName}`;
}

function formatEvolutionArgumentSummary(evolution: PokemonEvolutionRecord) {
  if (evolution.argumentKind === 'none' || evolution.argumentKind === 'level') {
    return 'Arg -';
  }

  return `${evolution.argumentLabel} ${evolution.argumentValue || evolution.argument}`;
}

function filterMoves(moves: MoveRecord[], searchText: string) {
  const normalizedSearch = searchText.trim().toLocaleLowerCase();

  if (normalizedSearch.length === 0) {
    return moves;
  }

  return moves.filter((move) =>
    [
      move.moveId.toString(),
      move.name,
      move.description ?? '',
      move.typeName,
      move.categoryName,
      move.power.toString(),
      move.accuracy.toString(),
      move.pp.toString(),
      move.priority.toString(),
      move.critStage.toString(),
      move.maxMovePower.toString(),
      move.targetName,
      move.hitMin.toString(),
      move.hitMax.toString(),
      move.turnMin.toString(),
      move.turnMax.toString(),
      move.inflictName,
      move.inflictPercent.toString(),
      move.flinch.toString(),
      move.recoil.toString(),
      move.rawHealing.toString(),
      move.provenance.sourceFile,
      ...move.flags.flatMap((flag) => [flag.field, flag.label, flag.enabled ? 'enabled' : '']),
      ...move.statChanges.flatMap((statChange) => [
        statChange.slot.toString(),
        statChange.statName,
        statChange.stage.toString(),
        statChange.percent.toString()
      ])
    ].some((value) => value.toLocaleLowerCase().includes(normalizedSearch))
  );
}

function filterTextEntries(entries: TextEntryRecord[], searchText: string) {
  const normalizedSearch = searchText.trim().toLocaleLowerCase();

  if (normalizedSearch.length === 0) {
    return entries;
  }

  return entries.filter((entry) =>
    [
      entry.textId.toString(),
      entry.label,
      entry.language,
      entry.sourceFile,
      entry.lineIndex.toString(),
      entry.value
    ].some((value) => value.toLocaleLowerCase().includes(normalizedSearch))
  );
}

function matchesSearchPrefix(value: string, normalizedSearch: string) {
  return value
    .toLocaleLowerCase()
    .split(/[^a-z0-9]+/)
    .some((token) => token.startsWith(normalizedSearch));
}

function filterTrainers(trainers: TrainerRecord[], searchText: string) {
  const normalizedSearch = searchText.trim().toLocaleLowerCase();

  if (normalizedSearch.length === 0) {
    return trainers;
  }

  return trainers.filter((trainer) =>
    [
      trainer.trainerId.toString(),
      trainer.name,
      trainer.trainerClass,
      trainer.trainerClassId.toString(),
      trainer.classBall ?? '',
      trainer.classBallId?.toString() ?? '',
      trainer.classBallScope,
      trainer.battleType,
      ...trainer.itemIds.map((itemId) => itemId.toString()),
      ...trainer.items,
      trainer.aiFlags.toString(),
      ...trainer.aiFlagStates.flatMap((flag) => [
        flag.label,
        flag.description,
        flag.enabled ? 'enabled' : ''
      ]),
      trainer.heal ? 'heal' : '',
      trainer.money.toString(),
      trainer.gift.toString(),
      trainer.provenance.sourceFile,
      trainer.provenance.teamSourceFile,
      ...trainer.team.flatMap((pokemon) => [
        pokemon.species,
        pokemon.speciesId.toString(),
        pokemon.level.toString(),
        pokemon.heldItem ?? '',
        pokemon.genderLabel,
        pokemon.gender.toString(),
        pokemon.abilityLabel,
        pokemon.ability.toString(),
        pokemon.natureLabel,
        pokemon.nature.toString(),
        ...pokemon.moves,
        ...pokemon.moveIds.map((moveId) => moveId.toString())
      ])
    ].some((value) => matchesSearchPrefix(value, normalizedSearch))
  );
}

function filterGiftPokemon(gifts: GiftPokemonRecord[], searchText: string) {
  const normalizedSearch = searchText.trim().toLocaleLowerCase();

  if (normalizedSearch.length === 0) {
    return gifts;
  }

  return gifts.filter((gift) =>
    [
      gift.giftIndex.toString(),
      (gift.giftIndex + 1).toString(),
      gift.label,
      gift.species,
      gift.speciesId.toString(),
      gift.form.toString(),
      gift.level.toString(),
      gift.isEgg ? 'egg' : '',
      gift.heldItem ?? 'None',
      gift.heldItemId.toString(),
      gift.ballItem,
      gift.ballItemId.toString(),
      gift.abilityLabel,
      gift.ability.toString(),
      gift.natureLabel,
      gift.nature.toString(),
      gift.genderLabel,
      gift.gender.toString(),
      gift.shinyLockLabel,
      gift.shinyLock.toString(),
      gift.dynamaxLevel.toString(),
      gift.canGigantamax ? 'gigantamax' : '',
      gift.specialMove ?? 'None',
      gift.specialMoveId.toString(),
      gift.ivSummary,
      formatGiftPokemonIvs(gift),
      gift.provenance.sourceFile
    ].some((value) => value.toLocaleLowerCase().includes(normalizedSearch))
  );
}

function filterTradePokemon(trades: TradePokemonRecord[], searchText: string) {
  const normalizedSearch = searchText.trim().toLocaleLowerCase();

  if (normalizedSearch.length === 0) {
    return trades;
  }

  return trades.filter((trade) =>
    [
      trade.tradeIndex.toString(),
      (trade.tradeIndex + 1).toString(),
      trade.label,
      trade.species,
      trade.speciesId.toString(),
      trade.form.toString(),
      trade.level.toString(),
      trade.requiredSpecies,
      trade.requiredSpeciesId.toString(),
      trade.requiredForm.toString(),
      trade.requiredNatureLabel,
      trade.requiredNature.toString(),
      trade.heldItem ?? 'None',
      trade.heldItemId.toString(),
      trade.ballItem,
      trade.ballItemId.toString(),
      trade.abilityLabel,
      trade.ability.toString(),
      trade.natureLabel,
      trade.nature.toString(),
      trade.genderLabel,
      trade.gender.toString(),
      trade.shinyLockLabel,
      trade.shinyLock.toString(),
      trade.dynamaxLevel.toString(),
      trade.canGigantamax ? 'gigantamax' : '',
      trade.trainerId.toString(),
      trade.otGenderLabel,
      trade.memoryCode.toString(),
      trade.memoryTextVariable.toString(),
      trade.memoryFeel.toString(),
      trade.memoryIntensity.toString(),
      trade.field03.toString(),
      trade.hash0,
      trade.hash1,
      trade.hash2,
      trade.ivSummary,
      formatTradePokemonIvs(trade),
      formatTradePokemonRelearnMoves(trade),
      trade.provenance.sourceFile
    ].some((value) => value.toLocaleLowerCase().includes(normalizedSearch))
  );
}

function filterRentalPokemon(rentals: RentalPokemonRecord[], searchText: string) {
  const normalizedSearch = searchText.trim().toLocaleLowerCase();

  if (normalizedSearch.length === 0) {
    return rentals;
  }

  return rentals.filter((rental) =>
    [
      rental.rentalIndex.toString(),
      (rental.rentalIndex + 1).toString(),
      rental.label,
      rental.species,
      rental.speciesId.toString(),
      rental.form.toString(),
      rental.level.toString(),
      rental.heldItem ?? 'None',
      rental.heldItemId.toString(),
      rental.ballItem,
      rental.ballItemId.toString(),
      rental.abilityLabel,
      rental.ability.toString(),
      rental.natureLabel,
      rental.nature.toString(),
      rental.genderLabel,
      rental.gender.toString(),
      rental.trainerId.toString(),
      rental.hash1,
      rental.hash2,
      rental.ivSummary,
      rental.hasPerfectIvs ? 'perfect ivs' : '',
      formatRentalPokemonIvs(rental),
      formatRentalPokemonStats(rental.evs),
      formatRentalPokemonMoves(rental),
      rental.provenance.sourceFile
    ].some((value) => value.toLocaleLowerCase().includes(normalizedSearch))
  );
}

function filterDynamaxAdventures(
  encounters: DynamaxAdventureRecord[],
  searchText: string
) {
  const normalizedSearch = searchText.trim().toLocaleLowerCase();

  if (normalizedSearch.length === 0) {
    return encounters;
  }

  return encounters.filter((encounter) =>
    [
      encounter.entryIndex.toString(),
      (encounter.entryIndex + 1).toString(),
      encounter.adventureIndex.toString(),
      encounter.label,
      encounter.species,
      encounter.speciesId.toString(),
      encounter.form.toString(),
      encounter.level.toString(),
      encounter.ballItem,
      encounter.ballItemId.toString(),
      encounter.abilityLabel,
      encounter.ability.toString(),
      encounter.gigantamaxLabel,
      encounter.gigantamaxState.toString(),
      encounter.versionLabel,
      encounter.version.toString(),
      encounter.shinyRollLabel,
      encounter.shinyRoll.toString(),
      encounter.isSingleCapture ? 'single capture' : 'repeat capture',
      encounter.isStoryProgressGated ? 'story gated' : 'ungated',
      encounter.singleCaptureFlagBlock,
      encounter.uiMessageId,
      encounter.otGender.toString(),
      encounter.ivSummary,
      formatDynamaxAdventureIvs(encounter),
      formatDynamaxAdventureMoves(encounter),
      encounter.provenance.sourceFile
    ].some((value) => value.toLocaleLowerCase().includes(normalizedSearch))
  );
}

function filterStaticEncounters(encounters: StaticEncounterRecord[], searchText: string) {
  const normalizedSearch = searchText.trim().toLocaleLowerCase();

  if (normalizedSearch.length === 0) {
    return encounters;
  }

  return encounters.filter((encounter) =>
    [
      encounter.encounterIndex.toString(),
      (encounter.encounterIndex + 1).toString(),
      encounter.label,
      encounter.encounterId,
      encounter.species,
      encounter.speciesId.toString(),
      encounter.form.toString(),
      encounter.level.toString(),
      encounter.heldItem ?? 'None',
      encounter.heldItemId.toString(),
      encounter.abilityLabel,
      encounter.ability.toString(),
      encounter.natureLabel,
      encounter.nature.toString(),
      encounter.genderLabel,
      encounter.gender.toString(),
      encounter.shinyLockLabel,
      encounter.shinyLock.toString(),
      encounter.encounterScenarioLabel,
      encounter.encounterScenario.toString(),
      encounter.dynamaxLevel.toString(),
      encounter.canGigantamax ? 'gigantamax' : '',
      encounter.ivSummary,
      formatStaticEncounterIvs(encounter),
      formatStaticEncounterStats(encounter.evs),
      formatStaticEncounterMoves(encounter),
      encounter.provenance.sourceFile
    ].some((value) => value.toLocaleLowerCase().includes(normalizedSearch))
  );
}

function filterShops(shops: ShopRecord[], searchText: string) {
  const normalizedSearch = searchText.trim().toLocaleLowerCase();

  if (normalizedSearch.length === 0) {
    return shops;
  }

  return shops.filter((shop) =>
    [
      shop.shopId,
      shop.name,
      shop.kind,
      shop.inventoryLabel,
      shop.inventoryIndex.toString(),
      shop.inventoryCount.toString(),
      shop.sourceHash,
      shop.inventorySummary,
      shop.location,
      shop.currency,
      shop.provenance.sourceFile,
      ...shop.inventory.flatMap((item) => [
        item.slot.toString(),
        item.itemId.toString(),
        item.itemName,
        item.price.toString(),
        item.stockLimit?.toString() ?? ''
      ])
    ].some((value) => value.toLocaleLowerCase().includes(normalizedSearch))
  );
}

function buildEncounterConditionTabs(
  selectedTable: EncounterTableRecord,
  tables: EncounterTableRecord[]
): EncounterConditionTab[] {
  const selectedGroupKey = getEncounterTableGroupKey(selectedTable);
  const groupTables = tables.filter((table) => {
    const tableGroupKey = getEncounterTableGroupKey(table);
    if (selectedGroupKey && tableGroupKey) {
      return tableGroupKey === selectedGroupKey;
    }

    return (
      table.archiveMember === selectedTable.archiveMember &&
      table.area === selectedTable.area &&
      table.gameVersion === selectedTable.gameVersion &&
      table.location === selectedTable.location
    );
  });
  const tablesByLabel = new Map(groupTables.map((table) => [table.encounterType, table]));
  const knownLabels = new Set<string>(encounterConditionLabels);
  const knownTabs = encounterConditionLabels.map((label) => {
    const table = tablesByLabel.get(label) ?? null;
    return {
      isAvailable: table !== null,
      isWeatherCopyTarget: encounterWeatherCopyLabels.has(label),
      label,
      table,
      tableId: table?.tableId ?? null
    };
  });
  const extraTabs = groupTables
    .filter((table) => !knownLabels.has(table.encounterType))
    .map((table) => ({
      isAvailable: true,
      isWeatherCopyTarget: encounterWeatherCopyLabels.has(table.encounterType),
      label: table.encounterType,
      table,
      tableId: table.tableId
    }));

  return [...knownTabs, ...extraTabs];
}

function getEncounterTableGroupKey(table: EncounterTableRecord) {
  const parts = table.tableId.split(':');
  return parts.length >= 5 ? parts.slice(0, 4).join(':') : null;
}

function filterEncounterTables(tables: EncounterTableRecord[], searchText: string) {
  const normalizedSearch = searchText.trim().toLocaleLowerCase();

  if (normalizedSearch.length === 0) {
    return tables;
  }

  return tables.filter((table) =>
    [
      table.tableId,
      table.location,
      table.area,
      table.encounterType,
      table.gameVersion,
      table.archiveMember,
      table.provenance.sourceFile,
      ...table.slots.flatMap((slot) => [
        slot.slot.toString(),
        slot.species,
        slot.speciesId.toString(),
        slot.form.toString(),
        slot.levelMin.toString(),
        slot.levelMax.toString(),
        slot.weight.toString(),
        slot.weather
      ])
    ].some((value) => value.toLocaleLowerCase().includes(normalizedSearch))
  );
}

function filterFlagRecords(flags: FlagRecord[], searchText: string) {
  const normalizedSearch = searchText.trim().toLocaleLowerCase();

  if (normalizedSearch.length === 0) {
    return flags;
  }

  return flags.filter((flag) =>
    [
      flag.flagId,
      flag.name,
      flag.category,
      flag.kind,
      flag.valueKind,
      flag.table,
      flag.index.toString(),
      flag.hash,
      flag.low32Key,
      flag.provenance.sourceFile
    ].some((value) => value.toLocaleLowerCase().includes(normalizedSearch))
  );
}

function filterSaveBlockRecords(saveBlocks: SaveBlockRecord[], searchText: string) {
  const normalizedSearch = searchText.trim().toLocaleLowerCase();

  if (normalizedSearch.length === 0) {
    return saveBlocks;
  }

  return saveBlocks.filter((saveBlock) =>
    [
      saveBlock.blockId,
      saveBlock.name,
      saveBlock.key,
      saveBlock.hash,
      saveBlock.kind,
      saveBlock.valueKind,
      saveBlock.provenance.sourceFile
    ].some((value) => value.toLocaleLowerCase().includes(normalizedSearch))
  );
}

function filterExeFsPatchRecords(patches: ExeFsPatchRecord[], searchText: string) {
  const normalizedSearch = searchText.trim().toLocaleLowerCase();

  if (normalizedSearch.length === 0) {
    return patches;
  }

  return patches.filter((patch) =>
    [
      patch.patchId,
      patch.name,
      patch.targetFile,
      patch.patchKind,
      patch.status,
      patch.description,
      patch.provenance.sourceFile,
      ...patch.details
    ].some((value) => value.toLocaleLowerCase().includes(normalizedSearch))
  );
}

function filterExeFsPatchCheckRecords(checks: ExeFsPatchCheckRecord[], searchText: string) {
  const normalizedSearch = searchText.trim().toLocaleLowerCase();

  if (normalizedSearch.length === 0) {
    return checks;
  }

  return checks.filter((check) =>
    [
      check.checkId,
      check.patchId,
      check.status,
      check.area,
      check.offset,
      check.name,
      check.expected,
      check.actual,
      check.notes,
      check.provenance.sourceFile
    ].some((value) => value.toLocaleLowerCase().includes(normalizedSearch))
  );
}

function filterExeFsSegmentRecords(segments: ExeFsSegmentRecord[], searchText: string) {
  const normalizedSearch = searchText.trim().toLocaleLowerCase();

  if (normalizedSearch.length === 0) {
    return segments;
  }

  return segments.filter((segment) =>
    [
      segment.segmentId,
      segment.name,
      segment.fileOffset,
      segment.memoryOffset,
      segment.decompressedSize,
      segment.compressedSize,
      segment.sha256,
      segment.hashStatus,
      segment.provenance.sourceFile
    ].some((value) => value.toLocaleLowerCase().includes(normalizedSearch))
  );
}

function filterRoyalCandyWorkflows(workflows: RoyalCandyWorkflowRecord[], searchText: string) {
  const normalizedSearch = searchText.trim().toLocaleLowerCase();

  if (normalizedSearch.length === 0) {
    return workflows;
  }

  return workflows.filter((workflow) =>
    [
      workflow.workflowId,
      workflow.name,
      workflow.category,
      workflow.target,
      workflow.mode,
      workflow.status,
      workflow.itemId.toString(),
      workflow.templateItemId.toString(),
      workflow.description,
      workflow.provenance.sourceFile,
      ...workflow.steps.flatMap((step) => [step.label, step.description])
    ].some((value) => value.toLocaleLowerCase().includes(normalizedSearch))
  );
}

function filterRoyalCandyChecks(checks: RoyalCandyWorkflowCheckRecord[], searchText: string) {
  const normalizedSearch = searchText.trim().toLocaleLowerCase();

  if (normalizedSearch.length === 0) {
    return checks;
  }

  return checks.filter((check) =>
    [
      check.checkId,
      check.workflowId,
      check.status,
      check.area,
      check.target,
      check.message,
      check.provenance.sourceFile
    ].some((value) => value.toLocaleLowerCase().includes(normalizedSearch))
  );
}

function filterRoyalCandyOutputs(outputs: RoyalCandyOutputRecord[], searchText: string) {
  const normalizedSearch = searchText.trim().toLocaleLowerCase();

  if (normalizedSearch.length === 0) {
    return outputs;
  }

  return outputs.filter((output) =>
    [
      output.outputId,
      output.workflowId,
      output.relativePath,
      output.sourceFile,
      output.outputKind,
      output.status,
      output.description,
      output.provenance.sourceFile
    ].some((value) => value.toLocaleLowerCase().includes(normalizedSearch))
  );
}

function filterSpreadsheetImportProfiles(
  profiles: SpreadsheetImportProfileRecord[],
  searchText: string
) {
  const normalizedSearch = searchText.trim().toLocaleLowerCase();

  if (normalizedSearch.length === 0) {
    return profiles;
  }

  return profiles.filter((profile) =>
    [
      profile.profileId,
      profile.name,
      profile.sourceKind,
      profile.targetWorkflow,
      profile.status,
      profile.description,
      profile.provenance.sourceFile,
      ...profile.columns.flatMap((column) => [
        column.header,
        column.valueKind,
        column.description
      ])
    ].some((value) => value.toLocaleLowerCase().includes(normalizedSearch))
  );
}

function getEditableItemFieldValue(item: ItemRecord, field: string) {
  switch (field) {
    case buyPriceFieldName:
      return item.buyPrice;
    case sellPriceFieldName:
      return item.sellPrice;
    case wattsPriceFieldName:
      return item.wattsPrice;
    case alternatePriceFieldName:
      return item.alternatePrice;
    case 'pouch':
      return item.metadata.pouch;
    case 'pouchFlags':
      return item.metadata.pouchFlags;
    case 'flingPower':
      return item.metadata.flingPower;
    case 'fieldUseType':
      return item.metadata.fieldUseType;
    case 'fieldFlags':
      return item.metadata.fieldFlags;
    case 'canUseOnPokemon':
      return item.metadata.canUseOnPokemon ? 1 : 0;
    case 'itemType':
      return item.metadata.itemType;
    case 'sortIndex':
      return item.metadata.sortIndex;
    case 'itemSprite':
      return item.metadata.itemSprite;
    case 'groupType':
      return item.metadata.groupType;
    case 'groupIndex':
      return item.metadata.groupIndex;
    case 'cureStatusFlags':
      return item.metadata.cureStatusFlags;
    case 'cureSleep':
      return getPackedBit(item.metadata.cureStatusFlags, 0);
    case 'curePoison':
      return getPackedBit(item.metadata.cureStatusFlags, 1);
    case 'cureBurn':
      return getPackedBit(item.metadata.cureStatusFlags, 2);
    case 'cureFreeze':
      return getPackedBit(item.metadata.cureStatusFlags, 3);
    case 'cureParalysis':
      return getPackedBit(item.metadata.cureStatusFlags, 4);
    case 'cureConfusion':
      return getPackedBit(item.metadata.cureStatusFlags, 5);
    case 'cureInfatuation':
      return getPackedBit(item.metadata.cureStatusFlags, 6);
    case 'guardSpec':
      return getPackedBit(item.metadata.cureStatusFlags, 7);
    case 'canTargetFaintedPokemon':
      return getPackedBit(item.metadata.boost0, 0);
    case 'revivesWholeParty':
      return getPackedBit(item.metadata.boost0, 1);
    case 'levelUpItem':
      return getPackedBit(item.metadata.boost0, 2);
    case 'evolutionItem':
      return getPackedBit(item.metadata.boost0, 3);
    case 'attackBoost':
      return getHighNibble(item.metadata.boost0);
    case 'defenseBoost':
      return getLowNibble(item.metadata.boost1);
    case 'specialAttackBoost':
      return getHighNibble(item.metadata.boost1);
    case 'specialDefenseBoost':
      return getLowNibble(item.metadata.boost2);
    case 'speedBoost':
      return getHighNibble(item.metadata.boost2);
    case 'accuracyBoost':
      return getLowNibble(item.metadata.boost3);
    case 'criticalHitBoost':
      return (item.metadata.boost3 >> 4) & 0x03;
    case 'ppUpFlag':
      return getPackedBit(item.metadata.boost3, 6);
    case 'ppMaxFlag':
      return getPackedBit(item.metadata.boost3, 7);
    case 'useFlags1':
      return item.metadata.useFlags1;
    case 'useFlags2':
      return item.metadata.useFlags2;
    case 'restorePpFlag':
      return getPackedBit(item.metadata.useFlags1, 0);
    case 'restoreAllPpFlag':
      return getPackedBit(item.metadata.useFlags1, 1);
    case 'restoreHpFlag':
      return getPackedBit(item.metadata.useFlags1, 2);
    case 'hpEvFlag':
      return getPackedBit(item.metadata.useFlags1, 3);
    case 'attackEvFlag':
      return getPackedBit(item.metadata.useFlags1, 4);
    case 'defenseEvFlag':
      return getPackedBit(item.metadata.useFlags1, 5);
    case 'speedEvFlag':
      return getPackedBit(item.metadata.useFlags1, 6);
    case 'specialAttackEvFlag':
      return getPackedBit(item.metadata.useFlags1, 7);
    case 'specialDefenseEvFlag':
      return getPackedBit(item.metadata.useFlags2, 0);
    case 'evAbove100Flag':
      return getPackedBit(item.metadata.useFlags2, 1);
    case 'friendship1Flag':
      return getPackedBit(item.metadata.useFlags2, 2);
    case 'friendship2Flag':
      return getPackedBit(item.metadata.useFlags2, 3);
    case 'friendship3Flag':
      return getPackedBit(item.metadata.useFlags2, 4);
    case 'evHp':
      return item.metadata.evHp;
    case 'evAttack':
      return item.metadata.evAttack;
    case 'evDefense':
      return item.metadata.evDefense;
    case 'evSpeed':
      return item.metadata.evSpeed;
    case 'evSpecialAttack':
      return item.metadata.evSpecialAttack;
    case 'evSpecialDefense':
      return item.metadata.evSpecialDefense;
    case 'healAmount':
      return item.metadata.healAmount;
    case 'ppGain':
      return item.metadata.ppGain;
    case 'friendshipGain1':
      return item.metadata.friendshipGain1;
    case 'friendshipGain2':
      return item.metadata.friendshipGain2;
    case 'friendshipGain3':
      return item.metadata.friendshipGain3;
    case 'machineMoveId':
      return item.metadata.machineMoveId;
    default:
      return null;
  }
}

function getPackedBit(value: number, bitOffset: number) {
  return (value & (1 << bitOffset)) !== 0 ? 1 : 0;
}

function getLowNibble(value: number) {
  return value & 0x0f;
}

function getHighNibble(value: number) {
  return (value >> 4) & 0x0f;
}

function getEditableMoveFieldValue(move: MoveRecord, field: string) {
  switch (field) {
    case 'canUseMove':
      return move.canUseMove ? 1 : 0;
    case 'type':
      return move.type;
    case 'quality':
      return move.quality;
    case 'category':
      return move.category;
    case 'power':
      return move.power;
    case 'accuracy':
      return move.accuracy;
    case 'pp':
      return move.pp;
    case 'priority':
      return move.priority;
    case 'critStage':
      return move.critStage;
    case 'maxMovePower':
      return move.maxMovePower;
    case 'target':
      return move.target;
    case 'hitMin':
      return move.hitMin;
    case 'hitMax':
      return move.hitMax;
    case 'turnMin':
      return move.turnMin;
    case 'turnMax':
      return move.turnMax;
    case 'inflict':
      return move.inflict;
    case 'inflictPercent':
      return move.inflictPercent;
    case 'rawInflictCount':
      return move.rawInflictCount;
    case 'flinch':
      return move.flinch;
    case 'effectSequence':
      return move.effectSequence;
    case 'recoil':
      return move.recoil;
    case 'rawHealing':
      return move.rawHealing;
    case 'stat1':
      return move.statChanges.find((stat) => stat.slot === 1)?.stat ?? null;
    case 'stat1Stage':
      return move.statChanges.find((stat) => stat.slot === 1)?.stage ?? null;
    case 'stat1Percent':
      return move.statChanges.find((stat) => stat.slot === 1)?.percent ?? null;
    case 'stat2':
      return move.statChanges.find((stat) => stat.slot === 2)?.stat ?? null;
    case 'stat2Stage':
      return move.statChanges.find((stat) => stat.slot === 2)?.stage ?? null;
    case 'stat2Percent':
      return move.statChanges.find((stat) => stat.slot === 2)?.percent ?? null;
    case 'stat3':
      return move.statChanges.find((stat) => stat.slot === 3)?.stat ?? null;
    case 'stat3Stage':
      return move.statChanges.find((stat) => stat.slot === 3)?.stage ?? null;
    case 'stat3Percent':
      return move.statChanges.find((stat) => stat.slot === 3)?.percent ?? null;
    default: {
      const flag = move.flags.find((candidate) => candidate.field === field);
      return flag ? (flag.enabled ? 1 : 0) : null;
    }
  }
}

function getEditablePersonalFieldValue(pokemon: PokemonRecord, field: string) {
  switch (field) {
    case 'hp':
      return pokemon.baseStats.hp;
    case 'attack':
      return pokemon.baseStats.attack;
    case 'defense':
      return pokemon.baseStats.defense;
    case 'specialAttack':
      return pokemon.baseStats.specialAttack;
    case 'specialDefense':
      return pokemon.baseStats.specialDefense;
    case 'speed':
      return pokemon.baseStats.speed;
    case 'type1':
      return pokemon.personal.type1;
    case 'type2':
      return pokemon.personal.type2;
    case 'catchRate':
      return pokemon.catchRate;
    case 'evolutionStage':
      return pokemon.evolutionStage;
    case 'evYieldHP':
      return pokemon.personal.evYieldHP;
    case 'evYieldAttack':
      return pokemon.personal.evYieldAttack;
    case 'evYieldDefense':
      return pokemon.personal.evYieldDefense;
    case 'evYieldSpecialAttack':
      return pokemon.personal.evYieldSpecialAttack;
    case 'evYieldSpecialDefense':
      return pokemon.personal.evYieldSpecialDefense;
    case 'evYieldSpeed':
      return pokemon.personal.evYieldSpeed;
    case 'heldItem1':
      return pokemon.personal.heldItem1;
    case 'heldItem2':
      return pokemon.personal.heldItem2;
    case 'heldItem3':
      return pokemon.personal.heldItem3;
    case 'genderRatio':
      return pokemon.genderRatio;
    case 'hatchCycles':
      return pokemon.personal.hatchCycles;
    case 'baseFriendship':
      return pokemon.personal.baseFriendship;
    case 'expGrowth':
      return pokemon.personal.expGrowth;
    case 'eggGroup1':
      return pokemon.personal.eggGroup1;
    case 'eggGroup2':
      return pokemon.personal.eggGroup2;
    case 'ability1':
      return pokemon.abilities.ability1;
    case 'ability2':
      return pokemon.abilities.ability2;
    case 'hiddenAbility':
      return pokemon.abilities.hiddenAbility;
    case 'formStatsIndex':
      return pokemon.personal.formStatsIndex;
    case 'formCount':
      return pokemon.personal.formCount;
    case 'color':
      return pokemon.personal.color;
    case 'isPresentInGame':
      return pokemon.dexPresence.isPresentInGame ? 1 : 0;
    case 'hasSpriteForm':
      return pokemon.personal.hasSpriteForm ? 1 : 0;
    case 'baseExperience':
      return pokemon.baseExperience;
    case 'height':
      return pokemon.height;
    case 'weight':
      return pokemon.weight;
    case 'modelId':
      return pokemon.personal.modelId;
    case 'hatchedSpecies':
      return pokemon.personal.hatchedSpecies;
    case 'localFormIndex':
      return pokemon.personal.localFormIndex;
    case 'isRegionalForm':
      return pokemon.personal.isRegionalForm ? 1 : 0;
    case 'canNotDynamax':
      return pokemon.personal.canNotDynamax ? 1 : 0;
    case 'regionalDexIndex':
      return pokemon.dexPresence.regionalDexIndex;
    case 'form':
      return pokemon.form;
    case 'armorDexIndex':
      return pokemon.dexPresence.armorDexIndex;
    case 'crownDexIndex':
      return pokemon.dexPresence.crownDexIndex;
    default:
      return null;
  }
}

function getEditableEncounterFieldValue(encounterSlot: EncounterSlotRecord, field: string) {
  switch (field) {
    case speciesIdFieldName:
      return encounterSlot.speciesId;
    case encounterFormFieldName:
      return encounterSlot.form;
    case encounterProbabilityFieldName:
      return encounterSlot.weight;
    case encounterLevelMinFieldName:
      return encounterSlot.levelMin;
    case encounterLevelMaxFieldName:
      return encounterSlot.levelMax;
    default:
      return null;
  }
}

function getEditableTrainerFieldValue(trainer: TrainerRecord, field: string) {
  switch (field) {
    case trainerClassIdFieldName:
      return trainer.trainerClassId;
    case classBallIdFieldName:
      return trainer.classBallId;
    case battleTypeFieldName:
      return trainer.battleTypeValue;
    case trainerItemFieldNames[0]:
      return trainer.itemIds[0] ?? null;
    case trainerItemFieldNames[1]:
      return trainer.itemIds[1] ?? null;
    case trainerItemFieldNames[2]:
      return trainer.itemIds[2] ?? null;
    case trainerItemFieldNames[3]:
      return trainer.itemIds[3] ?? null;
    case aiFlagsFieldName:
      return trainer.aiFlags;
    case healFieldName:
      return trainer.heal ? 1 : 0;
    case moneyFieldName:
      return trainer.money;
    case giftFieldName:
      return trainer.gift;
    default:
      return null;
  }
}

function getEditablePokemonFieldValue(pokemon: TrainerPokemonRecord, field: string) {
  switch (field) {
    case speciesIdFieldName:
      return pokemon.speciesId;
    case formFieldName:
      return pokemon.form;
    case levelFieldName:
      return pokemon.level;
    case heldItemIdFieldName:
      return pokemon.heldItemId;
    case moveFieldNames[0]:
      return pokemon.moveIds[0] ?? null;
    case moveFieldNames[1]:
      return pokemon.moveIds[1] ?? null;
    case moveFieldNames[2]:
      return pokemon.moveIds[2] ?? null;
    case moveFieldNames[3]:
      return pokemon.moveIds[3] ?? null;
    case genderFieldName:
      return pokemon.gender;
    case abilityFieldName:
      return pokemon.ability;
    case natureFieldName:
      return pokemon.nature;
    case evFieldNames[0]:
      return pokemon.evs.hp;
    case evFieldNames[1]:
      return pokemon.evs.attack;
    case evFieldNames[2]:
      return pokemon.evs.defense;
    case evFieldNames[3]:
      return pokemon.evs.specialAttack;
    case evFieldNames[4]:
      return pokemon.evs.specialDefense;
    case evFieldNames[5]:
      return pokemon.evs.speed;
    case dynamaxLevelFieldName:
      return pokemon.dynamaxLevel;
    case canGigantamaxFieldName:
      return pokemon.canGigantamax ? 1 : 0;
    case ivFieldNames[0]:
      return pokemon.ivs.hp;
    case ivFieldNames[1]:
      return pokemon.ivs.attack;
    case ivFieldNames[2]:
      return pokemon.ivs.defense;
    case ivFieldNames[3]:
      return pokemon.ivs.specialAttack;
    case ivFieldNames[4]:
      return pokemon.ivs.specialDefense;
    case ivFieldNames[5]:
      return pokemon.ivs.speed;
    case shinyFieldName:
      return pokemon.shiny ? 1 : 0;
    case canDynamaxFieldName:
      return pokemon.canDynamax ? 1 : 0;
    default:
      return null;
  }
}

function getEditableGiftPokemonFieldValue(gift: GiftPokemonRecord, field: string) {
  switch (field) {
    case giftSpeciesFieldName:
      return gift.speciesId;
    case formFieldName:
      return gift.form;
    case levelFieldName:
      return gift.level;
    case heldItemIdFieldName:
      return gift.heldItemId;
    case giftBallItemIdFieldName:
      return gift.ballItemId;
    case abilityFieldName:
      return gift.ability;
    case natureFieldName:
      return gift.nature;
    case genderFieldName:
      return gift.gender;
    case giftShinyLockFieldName:
      return gift.shinyLock;
    case dynamaxLevelFieldName:
      return gift.dynamaxLevel;
    case canGigantamaxFieldName:
      return gift.canGigantamax ? 1 : 0;
    case giftSpecialMoveIdFieldName:
      return gift.specialMoveId;
    case ivFieldNames[0]:
      return gift.ivs.hp;
    case ivFieldNames[1]:
      return gift.ivs.attack;
    case ivFieldNames[2]:
      return gift.ivs.defense;
    case ivFieldNames[3]:
      return gift.ivs.specialAttack;
    case ivFieldNames[4]:
      return gift.ivs.specialDefense;
    case ivFieldNames[5]:
      return gift.ivs.speed;
    case giftFlawlessIvCountFieldName:
      return gift.flawlessIvCount;
    default:
      return null;
  }
}

function getEditableTradePokemonFieldValue(trade: TradePokemonRecord, field: string) {
  switch (field) {
    case giftSpeciesFieldName:
      return trade.speciesId;
    case formFieldName:
      return trade.form;
    case levelFieldName:
      return trade.level;
    case heldItemIdFieldName:
      return trade.heldItemId;
    case giftBallItemIdFieldName:
      return trade.ballItemId;
    case tradeField03FieldName:
      return trade.field03;
    case abilityFieldName:
      return trade.ability;
    case natureFieldName:
      return trade.nature;
    case genderFieldName:
      return trade.gender;
    case giftShinyLockFieldName:
      return trade.shinyLock;
    case dynamaxLevelFieldName:
      return trade.dynamaxLevel;
    case canGigantamaxFieldName:
      return trade.canGigantamax ? 1 : 0;
    case tradeRequiredSpeciesFieldName:
      return trade.requiredSpeciesId;
    case tradeRequiredFormFieldName:
      return trade.requiredForm;
    case tradeRequiredNatureFieldName:
      return trade.requiredNature;
    case tradeUnknownRequirementFieldName:
      return trade.unknownRequirement;
    case tradeTrainerIdFieldName:
      return trade.trainerId;
    case tradeOtGenderFieldName:
      return trade.otGender;
    case tradeMemoryCodeFieldName:
      return trade.memoryCode;
    case tradeMemoryTextVariableFieldName:
      return trade.memoryTextVariable;
    case tradeMemoryFeelFieldName:
      return trade.memoryFeel;
    case tradeMemoryIntensityFieldName:
      return trade.memoryIntensity;
    case tradeRelearnMoveFieldNames[0]:
      return trade.relearnMoves[0]?.moveId ?? null;
    case tradeRelearnMoveFieldNames[1]:
      return trade.relearnMoves[1]?.moveId ?? null;
    case tradeRelearnMoveFieldNames[2]:
      return trade.relearnMoves[2]?.moveId ?? null;
    case tradeRelearnMoveFieldNames[3]:
      return trade.relearnMoves[3]?.moveId ?? null;
    case ivFieldNames[0]:
      return trade.ivs.hp;
    case ivFieldNames[1]:
      return trade.ivs.attack;
    case ivFieldNames[2]:
      return trade.ivs.defense;
    case ivFieldNames[3]:
      return trade.ivs.specialAttack;
    case ivFieldNames[4]:
      return trade.ivs.specialDefense;
    case ivFieldNames[5]:
      return trade.ivs.speed;
    case giftFlawlessIvCountFieldName:
      return trade.flawlessIvCount;
    default:
      return null;
  }
}

function getEditableStaticEncounterFieldValue(encounter: StaticEncounterRecord, field: string) {
  switch (field) {
    case giftSpeciesFieldName:
      return encounter.speciesId;
    case formFieldName:
      return encounter.form;
    case levelFieldName:
      return encounter.level;
    case heldItemIdFieldName:
      return encounter.heldItemId;
    case abilityFieldName:
      return encounter.ability;
    case natureFieldName:
      return encounter.nature;
    case genderFieldName:
      return encounter.gender;
    case giftShinyLockFieldName:
      return encounter.shinyLock;
    case staticEncounterScenarioFieldName:
      return encounter.encounterScenario;
    case dynamaxLevelFieldName:
      return encounter.dynamaxLevel;
    case canGigantamaxFieldName:
      return encounter.canGigantamax ? 1 : 0;
    case staticEncounterMoveFieldNames[0]:
      return encounter.moves[0]?.moveId ?? null;
    case staticEncounterMoveFieldNames[1]:
      return encounter.moves[1]?.moveId ?? null;
    case staticEncounterMoveFieldNames[2]:
      return encounter.moves[2]?.moveId ?? null;
    case staticEncounterMoveFieldNames[3]:
      return encounter.moves[3]?.moveId ?? null;
    case evFieldNames[0]:
      return encounter.evs.hp;
    case evFieldNames[1]:
      return encounter.evs.attack;
    case evFieldNames[2]:
      return encounter.evs.defense;
    case evFieldNames[3]:
      return encounter.evs.specialAttack;
    case evFieldNames[4]:
      return encounter.evs.specialDefense;
    case evFieldNames[5]:
      return encounter.evs.speed;
    case ivFieldNames[0]:
      return encounter.ivs.hp;
    case ivFieldNames[1]:
      return encounter.ivs.attack;
    case ivFieldNames[2]:
      return encounter.ivs.defense;
    case ivFieldNames[3]:
      return encounter.ivs.specialAttack;
    case ivFieldNames[4]:
      return encounter.ivs.specialDefense;
    case ivFieldNames[5]:
      return encounter.ivs.speed;
    case giftFlawlessIvCountFieldName:
      return encounter.flawlessIvCount;
    default:
      return null;
  }
}

function getEditableRentalPokemonFieldValue(rental: RentalPokemonRecord, field: string) {
  switch (field) {
    case giftSpeciesFieldName:
      return rental.speciesId;
    case formFieldName:
      return rental.form;
    case levelFieldName:
      return rental.level;
    case heldItemIdFieldName:
      return rental.heldItemId;
    case giftBallItemIdFieldName:
      return rental.ballItemId;
    case abilityFieldName:
      return rental.ability;
    case natureFieldName:
      return rental.nature;
    case genderFieldName:
      return rental.gender;
    case tradeTrainerIdFieldName:
      return rental.trainerId;
    case staticEncounterMoveFieldNames[0]:
      return rental.moves[0]?.moveId ?? null;
    case staticEncounterMoveFieldNames[1]:
      return rental.moves[1]?.moveId ?? null;
    case staticEncounterMoveFieldNames[2]:
      return rental.moves[2]?.moveId ?? null;
    case staticEncounterMoveFieldNames[3]:
      return rental.moves[3]?.moveId ?? null;
    case evFieldNames[0]:
      return rental.evs.hp;
    case evFieldNames[1]:
      return rental.evs.attack;
    case evFieldNames[2]:
      return rental.evs.defense;
    case evFieldNames[3]:
      return rental.evs.specialAttack;
    case evFieldNames[4]:
      return rental.evs.specialDefense;
    case evFieldNames[5]:
      return rental.evs.speed;
    case ivFieldNames[0]:
      return rental.ivs.hp;
    case ivFieldNames[1]:
      return rental.ivs.attack;
    case ivFieldNames[2]:
      return rental.ivs.defense;
    case ivFieldNames[3]:
      return rental.ivs.specialAttack;
    case ivFieldNames[4]:
      return rental.ivs.specialDefense;
    case ivFieldNames[5]:
      return rental.ivs.speed;
    case rentalFixedIvPresetFieldName:
      return getFixedRentalPokemonIvPreset(rental);
    default:
      return null;
  }
}

function getEditableDynamaxAdventureFieldValue(
  encounter: DynamaxAdventureRecord,
  field: string
) {
  switch (field) {
    case giftSpeciesFieldName:
      return encounter.speciesId;
    case formFieldName:
      return encounter.form;
    case levelFieldName:
      return encounter.level;
    case dynamaxAdventureBallItemIdFieldName:
      return encounter.ballItemId;
    case abilityFieldName:
      return encounter.ability;
    case dynamaxAdventureGigantamaxStateFieldName:
      return encounter.gigantamaxState;
    case dynamaxAdventureVersionFieldName:
      return encounter.version;
    case dynamaxAdventureShinyRollFieldName:
      return encounter.shinyRoll;
    case staticEncounterMoveFieldNames[0]:
      return encounter.moves[0]?.moveId ?? null;
    case staticEncounterMoveFieldNames[1]:
      return encounter.moves[1]?.moveId ?? null;
    case staticEncounterMoveFieldNames[2]:
      return encounter.moves[2]?.moveId ?? null;
    case staticEncounterMoveFieldNames[3]:
      return encounter.moves[3]?.moveId ?? null;
    case dynamaxAdventureGuaranteedPerfectIvsFieldName:
      return encounter.guaranteedPerfectIvs;
    case dynamaxAdventureIvFieldNames[0]:
      return encounter.ivs.attack;
    case dynamaxAdventureIvFieldNames[1]:
      return encounter.ivs.defense;
    case dynamaxAdventureIvFieldNames[2]:
      return encounter.ivs.specialAttack;
    case dynamaxAdventureIvFieldNames[3]:
      return encounter.ivs.specialDefense;
    case dynamaxAdventureIvFieldNames[4]:
      return encounter.ivs.speed;
    case dynamaxAdventureIsSingleCaptureFieldName:
      return encounter.isSingleCapture ? 1 : 0;
    case dynamaxAdventureIsStoryProgressGatedFieldName:
      return encounter.isStoryProgressGated ? 1 : 0;
    case dynamaxAdventureOtGenderFieldName:
      return encounter.otGender;
    default:
      return null;
  }
}

function getItemFieldSaveLabel(field: ItemEditableField) {
  return `Save ${field.label.replace(/\s+price$/i, '')}`;
}

function getItemPriceDraftState(
  draftValue: string,
  currentValue: number | null,
  field: ItemEditableField | undefined
) {
  const normalizedValue = draftValue.trim();
  const parsedValue = parseEditableIntegerDraft(normalizedValue, field?.options);
  const minimumValue = field?.minimumValue ?? null;
  const maximumValue = field?.maximumValue ?? null;
  const inRange =
    parsedValue !== null &&
    (minimumValue === null || parsedValue >= minimumValue) &&
    (maximumValue === null || parsedValue <= maximumValue);

  return {
    canSubmit:
      field !== undefined &&
      currentValue !== null &&
      inRange &&
      parsedValue !== currentValue,
    parsedValue
  };
}

function getMoveDraftState(
  draftValue: string,
  currentValue: number | null,
  field: MoveEditableField
) {
  const normalizedValue = draftValue.trim();

  if (field.valueKind === 'boolean') {
    const normalizedBoolean = normalizedValue === '1' ? '1' : '0';
    const parsedValue = normalizedBoolean === '1' ? 1 : 0;

    return {
      canSubmit: currentValue !== null && parsedValue !== currentValue,
      normalizedValue: normalizedBoolean
    };
  }

  const parsedValue = parseEditableIntegerDraft(normalizedValue, field.options);
  const minimumValue = field.minimumValue ?? null;
  const maximumValue = field.maximumValue ?? null;
  const inRange =
    parsedValue !== null &&
    (minimumValue === null || parsedValue >= minimumValue) &&
    (maximumValue === null || parsedValue <= maximumValue);

  return {
    canSubmit:
      currentValue !== null &&
      inRange &&
      parsedValue !== currentValue,
    normalizedValue: inRange && parsedValue !== null ? parsedValue.toString() : null
  };
}

function getPokemonDraftState(pokemon: PokemonRecord, field: PokemonEditableField) {
  const value = getEditablePersonalFieldValue(pokemon, field.field);

  return value === null
    ? null
    : {
        field: field.field,
        recordId: pokemon.personalId,
        value: value.toString()
      };
}

type PokemonPersonalFieldDraftState = {
  error: string | null;
  isChanged: boolean;
  isValid: boolean;
  normalizedValue: string | null;
};

type PokemonPersonalDraftChange = {
  field: string;
  group: string;
  label: string;
  value: string;
};

function createPokemonPersonalDrafts(
  pokemon: PokemonRecord | null,
  fields: PokemonEditableField[]
) {
  if (!pokemon) {
    return {};
  }

  return Object.fromEntries(
    fields
      .map((field) => {
        const value = getEditablePersonalFieldValue(pokemon, field.field);
        return value === null ? null : [field.field, value.toString()];
      })
      .filter((entry): entry is [string, string] => entry !== null)
  );
}

function groupPokemonEditableFields(fields: PokemonEditableField[]) {
  const groups: Array<{ group: string; fields: PokemonEditableField[] }> = [];

  for (const field of fields) {
    let group = groups.find((candidate) => candidate.group === field.group);
    if (!group) {
      group = { group: field.group, fields: [] };
      groups.push(group);
    }

    group.fields.push(field);
  }

  return groups;
}

function getPokemonPersonalDraftSummary(
  pokemon: PokemonRecord | null,
  fields: PokemonEditableField[],
  drafts: Record<string, string>
) {
  const changedFields: PokemonPersonalDraftChange[] = [];
  const invalidFields: PokemonPersonalDraftChange[] = [];
  let dirtyFieldCount = 0;

  if (!pokemon) {
    return { changedFields, dirtyFieldCount, invalidFields };
  }

  for (const field of fields) {
    const currentValue = getEditablePersonalFieldValue(pokemon, field.field);
    const draftValue = drafts[field.field] ?? '';
    const draftState = getPokemonPersonalFieldDraftState(draftValue, currentValue, field);

    if (draftState.isChanged || !draftState.isValid) {
      dirtyFieldCount += 1;
    }

    if (!draftState.isValid) {
      invalidFields.push({
        field: field.field,
        group: field.group,
        label: field.label,
        value: draftValue
      });
      continue;
    }

    if (draftState.isChanged && draftState.normalizedValue !== null) {
      changedFields.push({
        field: field.field,
        group: field.group,
        label: field.label,
        value: draftState.normalizedValue
      });
    }
  }

  return { changedFields, dirtyFieldCount, invalidFields };
}

function getPokemonPersonalFieldDraftState(
  draftValue: string,
  currentValue: number | null,
  field: PokemonEditableField
): PokemonPersonalFieldDraftState {
  if (currentValue === null) {
    return {
      error: 'This value is not available for this record.',
      isChanged: false,
      isValid: false,
      normalizedValue: null
    };
  }

  const normalizedValue = draftValue.trim();
  const currentText = currentValue.toString();

  if (field.valueKind === 'boolean') {
    if (normalizedValue !== '0' && normalizedValue !== '1') {
      return {
        error: 'Choose Yes or No.',
        isChanged: normalizedValue !== currentText,
        isValid: false,
        normalizedValue: null
      };
    }

    return {
      error: null,
      isChanged: normalizedValue !== currentText,
      isValid: true,
      normalizedValue
    };
  }

  const parsedValue = parseEditableIntegerDraft(normalizedValue, field.options);

  if (parsedValue === null) {
    return {
      error: 'Enter a whole number.',
      isChanged: normalizedValue !== currentText,
      isValid: false,
      normalizedValue: null
    };
  }

  const minimumValue = field.minimumValue ?? null;
  const maximumValue = field.maximumValue ?? null;

  if (minimumValue !== null && parsedValue < minimumValue) {
    return {
      error: `Minimum value is ${minimumValue}.`,
      isChanged: parsedValue !== currentValue,
      isValid: false,
      normalizedValue: null
    };
  }

  if (maximumValue !== null && parsedValue > maximumValue) {
    return {
      error: `Maximum value is ${maximumValue}.`,
      isChanged: parsedValue !== currentValue,
      isValid: false,
      normalizedValue: null
    };
  }

  if (
    field.options.length > 0 &&
    parsedValue !== currentValue &&
    !field.options.some((option) => option.value === parsedValue)
  ) {
    return {
      error: 'Choose one of the available options.',
      isChanged: true,
      isValid: false,
      normalizedValue: null
    };
  }

  return {
    error: null,
    isChanged: parsedValue !== currentValue,
    isValid: true,
    normalizedValue: parsedValue.toString()
  };
}

function PokemonPersonalFieldInput({
  currentValue,
  disabled,
  disabledReason,
  draftState,
  draftValue,
  field,
  onChange
}: {
  currentValue: number | null;
  disabled: boolean;
  disabledReason?: string;
  draftState: PokemonPersonalFieldDraftState;
  draftValue: string;
  field: PokemonEditableField;
  onChange: (value: string) => void;
}) {
  const inputId = `pokemon-personal-${field.field}`;
  const helpText = disabledReason ?? getEditableFieldHelp(field);
  const statusText = draftState.error ?? (draftState.isChanged ? 'Changed' : null);

  return (
    <label
      className={`path-field editable-field-control ${
        draftState.isChanged ? 'editable-field-changed' : ''
      } ${!draftState.isValid ? 'editable-field-invalid' : ''} ${
        disabledReason ? 'editable-field-disabled' : ''
      }`}
      htmlFor={inputId}
    >
      <span>{field.label}</span>
      {field.valueKind === 'boolean' ? (
        <select
          aria-label={field.label}
          disabled={disabled}
          id={inputId}
          onChange={(event) => onChange(event.target.value)}
          title={helpText}
          value={draftValue === '1' ? '1' : '0'}
        >
          <option value="1">Yes</option>
          <option value="0">No</option>
        </select>
      ) : field.options.length > 0 ? (
        <SearchableOptionInput
          ariaLabel={field.label}
          disabled={disabled}
          id={inputId}
          onChange={onChange}
          options={addCurrentPokemonFieldOption(field.options, draftValue, field.label)}
          title={helpText}
          value={draftValue}
        />
      ) : (
        <input
          aria-label={field.label}
          disabled={disabled}
          id={inputId}
          max={field.maximumValue ?? undefined}
          min={field.minimumValue ?? undefined}
          onChange={(event) => onChange(event.target.value)}
          title={helpText}
          type="number"
          value={draftValue}
        />
      )}
      {disabledReason ? (
        <small className="editable-field-status">{disabledReason}</small>
      ) : statusText ? (
        <small className={draftState.error ? 'editable-field-error' : 'editable-field-status'}>
          {statusText}
        </small>
      ) : null}
    </label>
  );
}

type EditableFieldWithOptions = {
  field?: string;
  label: string;
  minimumValue?: number | null;
  maximumValue?: number | null;
  options?: Array<{ label: string; value: number }>;
};

type EditableFieldOption = {
  label: string;
  value: number;
};

type SpeciesFormOptionContext = {
  abilityOptions?: EditableFieldOption[];
  formOptions?: EditableFieldOption[];
  species: string;
  speciesId?: number;
};

type EncounterConditionTab = {
  isAvailable: boolean;
  isWeatherCopyTarget: boolean;
  label: string;
  table: EncounterTableRecord | null;
  tableId: string | null;
};

type SaveProgressState = {
  detail: string;
  label: string;
  percent: number;
  step: number;
  totalSteps: number;
};

type ExitPromptState = {
  destination: WorkbenchSection | null;
  kind: 'editor' | 'window';
  mode: 'confirm' | 'redirect';
};

function SearchableOptionInput({
  ariaLabel,
  disabled,
  id,
  onChange,
  options,
  title,
  value
}: {
  ariaLabel: string;
  disabled: boolean;
  id?: string;
  onChange: (value: string) => void;
  options: EditableFieldOption[];
  title?: string;
  value: string;
}) {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const [isOpen, setIsOpen] = useState(false);
  const formattedValue = useMemo(() => formatOptionInputValue(value, options), [options, value]);
  const [query, setQuery] = useState(formattedValue);
  const filteredOptions = useMemo(
    () => getSmartOptionMatches(query, options),
    [options, query]
  );
  const hasMenu = isOpen && !disabled && filteredOptions.length > 0;

  useEffect(() => {
    if (!isOpen) {
      setQuery(formattedValue);
    }
  }, [formattedValue, isOpen]);

  useEffect(() => {
    if (disabled) {
      setIsOpen(false);
    }
  }, [disabled]);

  useEffect(() => {
    if (!isOpen) {
      return undefined;
    }

    const handlePointerDown = (event: MouseEvent) => {
      if (!containerRef.current?.contains(event.target as Node)) {
        setIsOpen(false);
      }
    };

    document.addEventListener('mousedown', handlePointerDown);
    return () => document.removeEventListener('mousedown', handlePointerDown);
  }, [isOpen]);

  const selectOption = (option: EditableFieldOption) => {
    onChange(option.value.toString());
    setQuery(option.label);
    setIsOpen(false);
  };

  const handleInputChange = (nextValue: string) => {
    setQuery(nextValue);
    setIsOpen(true);
    onChange(normalizeExactOptionInputValue(nextValue, options));
  };

  return (
    <div
      className={`searchable-option-input ${disabled ? 'searchable-option-disabled' : ''}`}
      ref={containerRef}
    >
      <input
        aria-expanded={hasMenu}
        aria-label={ariaLabel}
        aria-haspopup="listbox"
        autoComplete="off"
        disabled={disabled}
        id={id}
        inputMode="search"
        onBlur={() => setIsOpen(false)}
        onChange={(event) => handleInputChange(event.target.value)}
        onFocus={() => {
          setQuery(formattedValue);
          setIsOpen(true);
        }}
        onKeyDown={(event) => {
          if (event.key === 'Escape') {
            setIsOpen(false);
            return;
          }

          if (event.key === 'Enter' && hasMenu) {
            event.preventDefault();
            selectOption(filteredOptions[0]);
          }
        }}
        title={title}
        type="text"
        value={query}
      />
      <button
        aria-label={`Show ${ariaLabel} options`}
        className="searchable-option-toggle"
        disabled={disabled}
        onMouseDown={(event) => {
          event.preventDefault();
          setQuery(formattedValue);
          setIsOpen((current) => !current);
        }}
        tabIndex={-1}
        type="button"
      >
        <ChevronDown aria-hidden="true" size={16} />
      </button>
      {hasMenu ? (
        <div className="searchable-option-menu" role="listbox">
          {filteredOptions.map((option) => (
            <button
              className="searchable-option-row"
              key={`${ariaLabel}:${option.value}`}
              onMouseDown={(event) => {
                event.preventDefault();
                selectOption(option);
              }}
              role="option"
              type="button"
            >
              <span>{option.label}</span>
            </button>
          ))}
        </div>
      ) : null}
    </div>
  );
}

function normalizeExactOptionInputValue(value: string, options: EditableFieldOption[]) {
  const normalizedValue = value.trim().toLocaleLowerCase();
  if (normalizedValue.length === 0) {
    return value;
  }

  const optionMatch = options.find(
    (option) =>
      option.label.toLocaleLowerCase() === normalizedValue ||
      option.value.toString() === normalizedValue
  );

  return optionMatch ? optionMatch.value.toString() : value;
}

function formatOptionInputValue(value: string, options: EditableFieldOption[]) {
  const trimmedValue = value.trim();
  if (trimmedValue.length === 0) {
    return value;
  }

  return (
    options.find((option) => option.value.toString() === trimmedValue)?.label ??
    options.find((option) => option.label === value)?.label ??
    value
  );
}

function parseEditableIntegerDraft(value: string, options?: EditableFieldOption[] | null) {
  const normalizedValue = value.trim();
  if (normalizedValue.length === 0) {
    return null;
  }

  const optionMatch = options?.find(
    (option) =>
      option.label.toLocaleLowerCase() === normalizedValue.toLocaleLowerCase() ||
      option.value.toString() === normalizedValue
  );
  if (optionMatch) {
    return optionMatch.value;
  }

  const prefixMatch = normalizedValue.match(/^-?\d+/);
  return prefixMatch ? Number.parseInt(prefixMatch[0], 10) : null;
}

function getSmartOptionMatches(value: string, options: EditableFieldOption[]) {
  const query = value.trim();
  if (query.length === 0) {
    return options.slice(0, 100);
  }

  const normalizedQuery = query.toLocaleLowerCase();
  const numericPrefix = normalizedQuery.match(/^\d+/)?.[0] ?? null;
  const letterPrefix = normalizedQuery.match(/^[a-z]+/)?.[0] ?? null;

  if (numericPrefix) {
    const normalizedNumericPrefix = numericPrefix.replace(/^0+/, '') || '0';

    return options
      .filter((option) => {
        const rawValue = option.value.toString();
        const normalizedValue = rawValue.replace(/^0+/, '') || '0';
        const labelNumericPrefix = option.label.match(/^\s*0*(\d+)/)?.[1] ?? null;

        return (
          rawValue.startsWith(numericPrefix) ||
          normalizedValue.startsWith(normalizedNumericPrefix) ||
          labelNumericPrefix?.startsWith(normalizedNumericPrefix)
        );
      })
      .slice(0, 100);
  }

  if (letterPrefix) {
    return options
      .filter((option) =>
        option.label
          .toLocaleLowerCase()
          .split(/[^a-z0-9]+/)
          .some((token) => token.startsWith(letterPrefix))
      )
      .slice(0, 100);
  }

  return options
    .filter((option) => option.label.toLocaleLowerCase().startsWith(normalizedQuery))
    .slice(0, 100);
}

function useCancelActiveEditSession() {
  const setApplyResult = useWorkbenchStore((state) => state.setApplyResult);
  const setChangePlan = useWorkbenchStore((state) => state.setChangePlan);
  const setEditSession = useWorkbenchStore((state) => state.setEditSession);
  const setEditValidationDiagnostics = useWorkbenchStore(
    (state) => state.setEditValidationDiagnostics
  );

  return useCallback(() => {
    setApplyResult(null);
    setChangePlan(null);
    setEditValidationDiagnostics([]);
    setEditSession(null);
  }, [setApplyResult, setChangePlan, setEditSession, setEditValidationDiagnostics]);
}

function PokemonSummaryCard({
  name,
  subtitle,
  title
}: {
  name: string;
  subtitle: string;
  title: string;
}) {
  return (
    <div className="pokemon-summary-card">
      <PokemonSprite className="pokemon-summary-sprite" name={name} />
      <div>
        <strong>{title}</strong>
        <span>{subtitle}</span>
      </div>
    </div>
  );
}

function PokemonSprite({
  className = '',
  name,
  preferStatic = false
}: {
  className?: string;
  name: string;
  preferStatic?: boolean;
}) {
  const urls = useMemo(() => getPokemonSpriteUrls(name, preferStatic), [name, preferStatic]);
  const [urlIndex, setUrlIndex] = useState(0);

  useEffect(() => {
    setUrlIndex(0);
  }, [urls]);

  if (urls.length === 0 || !urls[urlIndex]) {
    return <span aria-hidden="true" className={`pokemon-sprite-placeholder ${className}`} />;
  }

  return (
    <img
      alt=""
      className={`pokemon-sprite ${className} ${
        urls[urlIndex].includes('/ani/') ? 'pokemon-sprite-animated' : ''
      }`}
      onError={() => setUrlIndex((currentIndex) => currentIndex + 1)}
      src={urls[urlIndex]}
    />
  );
}

function getPokemonSpriteUrls(name: string, preferStatic: boolean) {
  const spriteId = getPokemonSpriteId(name);
  if (!spriteId) {
    return [];
  }

  const localStatic = `/sprites/gen5/${spriteId}.png`;
  const localAnimated = `/sprites/ani/${spriteId}.gif`;
  const remoteStatic = `https://play.pokemonshowdown.com/sprites/gen5/${spriteId}.png`;

  return preferStatic
    ? [localStatic, remoteStatic]
    : [localAnimated, localStatic, remoteStatic];
}

const pokemonSpriteIdOverrides = new Map<string, string>([
  ['jangmo-o', 'jangmoo'],
  ['hakamo-o', 'hakamoo'],
  ['kommo-o', 'kommoo'],
  ['toxtricity-low-key-gmax', 'toxtricity-gmax']
]);

export function getPokemonSpriteId(name: string) {
  const normalizedName = normalizePokemonSpriteName(name);
  if (!normalizedName) {
    return '';
  }

  const normalizedKey = normalizedName.toLocaleLowerCase();
  const override = pokemonSpriteIdOverrides.get(normalizedKey);
  if (override) {
    return override;
  }

  return normalizedName
    .split('-')
    .map(toPokemonSpriteIdPart)
    .filter(Boolean)
    .join('-');
}

function normalizePokemonSpriteName(name: string) {
  const trimmedName = name.trim();
  if (!trimmedName || /^Pokemon \d+$/i.test(trimmedName) || /^Unused \d+$/i.test(trimmedName)) {
    return '';
  }

  return trimmedName
    .replace(/\s*\((Alolan)\)$/i, '-Alola')
    .replace(/\s*\((Galarian)\)$/i, '-Galar')
    .replace(/\s*\((Low Key)\)/i, '-Low-Key')
    .replace(/\s*\((Gigantamax|G-Max)\)$/i, '-Gmax')
    .replace(/\s*\((Regional Form \d+|Regional Form|Form \d+)\)$/i, '')
    .replace(/\s+/g, '-');
}

function getReferenceSpriteName(label: string) {
  return label.replace(/^\d+\s+/, '').replace(/^Species\s+\d+$/i, '');
}

function toPokemonSpriteIdPart(value: string) {
  return value.toLocaleLowerCase().replace(/[^a-z0-9]+/g, '');
}

function getEditableFieldHelp(field: EditableFieldWithOptions) {
  const specificHelp: Record<string, string> = {
    aiFlags: 'Battle AI behavior bitmask. Use the named AI flag checkboxes when they are shown.',
    canDynamax: 'Whether this Pokemon is allowed to Dynamax in trainer battles.',
    canGigantamax: 'Whether this Pokemon can use its Gigantamax form when eligible.',
    dynamaxLevel: 'Dynamax level. Valid game values are 0 through 10.',
    gift: 'Trainer gift/event identifier. 0 means no linked gift; review event scripts before changing.',
    money: 'Trainer payout multiplier used by the game formula, not the final post-battle cash amount.',
    specialMoveId: 'Gift table special move field.'
  };
  const range =
    field.minimumValue === null || field.maximumValue === null
      ? null
      : field.minimumValue === undefined || field.maximumValue === undefined
        ? null
        : `${field.minimumValue}-${field.maximumValue}`;
  const optionCount = field.options?.length ?? 0;
  const optionHint = optionCount > 0 ? `${optionCount} available option${optionCount === 1 ? '' : 's'}` : null;

  return [
    field.label,
    field.field ? specificHelp[field.field] : null,
    range ? `Allowed range: ${range}` : null,
    optionHint
  ]
    .filter(Boolean)
    .join('. ');
}

function getContextualFieldOptions(
  field: EditableFieldWithOptions,
  formOptionContext?: SpeciesFormOptionContext
): EditableFieldOption[] {
  const options = field.options ?? [];
  if (
    field.field === abilityFieldName ||
    field.field === raidBattleAbilityFieldName
  ) {
    return formOptionContext?.abilityOptions?.length ? formOptionContext.abilityOptions : options;
  }

  if (!isSpeciesFormField(field.field) || formOptionContext === undefined) {
    return options;
  }

  return options.map((option) => ({
    ...option,
    label: formatSpeciesFormOptionLabel(option.value, formOptionContext)
  }));
}

function isSpeciesFormField(fieldName: string | undefined) {
  return fieldName === 'form' || fieldName === tradeRequiredFormFieldName;
}

function getFormFieldDisabledReason(
  field: EditableFieldWithOptions,
  formOptionContext: SpeciesFormOptionContext | undefined,
  currentValue: number | null,
  knownFormCount?: number
) {
  if (!isSpeciesFormField(field.field)) {
    return null;
  }

  if (currentValue !== null && currentValue !== 0) {
    return null;
  }

  if (knownFormCount !== undefined) {
    return knownFormCount <= 1 ? 'No alternate forms available for this Pokemon.' : null;
  }

  if (!formOptionContext) {
    return null;
  }

  return speciesHasKnownAlternateForms(formOptionContext)
    ? null
    : 'No alternate forms available for this Pokemon.';
}

function speciesHasKnownAlternateForms(context: SpeciesFormOptionContext) {
  return (
    (context.speciesId !== undefined &&
      [...regionalFormLabelsBySpeciesId.keys()].some((key) =>
        key.startsWith(`${context.speciesId}:`)
      )) ||
    [...regionalFormLabelsBySpeciesName.keys()].some((key) =>
      key.startsWith(`${normalizeSpeciesName(context.species)}:`)
    ) ||
    knownAlternateFormSpeciesNames.has(normalizeSpeciesName(context.species))
  );
}

function getIntegerDraftState(
  draftValue: string,
  currentValue: number | null,
  field:
    | EncounterEditableField
    | RaidBattleEditableField
    | RaidRewardEditableField
    | ShopEditableField
    | TrainerEditableField
    | undefined
) {
  const parsedValue = parseEditableIntegerDraft(draftValue, field?.options);
  const minimumValue = field?.minimumValue ?? null;
  const maximumValue = field?.maximumValue ?? null;
  const inRange =
    parsedValue !== null &&
    (minimumValue === null || parsedValue >= minimumValue) &&
    (maximumValue === null || parsedValue <= maximumValue);

  return {
    canSubmit:
      field !== undefined &&
      currentValue !== null &&
      inRange &&
      parsedValue !== currentValue,
    parsedValue
  };
}

function getGiftPokemonDraftState(
  draftValue: string,
  currentValue: number | null,
  field: GiftPokemonEditableField | undefined
) {
  const parsedValue = parseEditableIntegerDraft(draftValue, field?.options);
  const minimumValue = field?.minimumValue ?? null;
  const maximumValue = field?.maximumValue ?? null;
  const inRange =
    parsedValue !== null &&
    (minimumValue === null || parsedValue >= minimumValue) &&
    (maximumValue === null || parsedValue <= maximumValue);

  return {
    canSubmit:
      field !== undefined &&
      inRange &&
      (currentValue === null || parsedValue !== currentValue),
    parsedValue
  };
}

function getTradePokemonDraftState(
  draftValue: string,
  currentValue: number | null,
  field: TradePokemonEditableField | undefined
) {
  const normalizedValue = draftValue.trim();
  const parsedValue = parseEditableIntegerDraft(normalizedValue, field?.options);
  const minimumValue = field?.minimumValue ?? null;
  const maximumValue = field?.maximumValue ?? null;
  const inRange =
    parsedValue !== null &&
    (minimumValue === null || parsedValue >= minimumValue) &&
    (maximumValue === null || parsedValue <= maximumValue);

  return {
    canSubmit:
      field !== undefined &&
      inRange &&
      (currentValue === null || parsedValue !== currentValue),
    parsedValue
  };
}

function getStaticEncounterDraftState(
  draftValue: string,
  currentValue: number | null,
  field: StaticEncounterEditableField | undefined
) {
  const normalizedValue = draftValue.trim();
  const parsedValue = parseEditableIntegerDraft(normalizedValue, field?.options);
  const minimumValue = field?.minimumValue ?? null;
  const maximumValue = field?.maximumValue ?? null;
  const inRange =
    parsedValue !== null &&
    (minimumValue === null || parsedValue >= minimumValue) &&
    (maximumValue === null || parsedValue <= maximumValue);

  return {
    canSubmit:
      field !== undefined &&
      inRange &&
      (currentValue === null || parsedValue !== currentValue),
    parsedValue
  };
}

function getRentalPokemonDraftState(
  draftValue: string,
  currentValue: number | null,
  field: RentalPokemonEditableField | undefined
) {
  const normalizedValue = draftValue.trim();
  const parsedValue = parseEditableIntegerDraft(normalizedValue, field?.options);
  const minimumValue = field?.minimumValue ?? null;
  const maximumValue = field?.maximumValue ?? null;
  const inRange =
    parsedValue !== null &&
    (minimumValue === null || parsedValue >= minimumValue) &&
    (maximumValue === null || parsedValue <= maximumValue);

  return {
    canSubmit:
      field !== undefined &&
      inRange &&
      (currentValue === null || parsedValue !== currentValue),
    parsedValue
  };
}

function getDynamaxAdventureDraftState(
  draftValue: string,
  currentValue: number | null,
  field: DynamaxAdventureEditableField | undefined
) {
  const normalizedValue = draftValue.trim();
  const parsedValue = parseEditableIntegerDraft(normalizedValue, field?.options);
  const minimumValue = field?.minimumValue ?? null;
  const maximumValue = field?.maximumValue ?? null;
  const inRange =
    parsedValue !== null &&
    (minimumValue === null || parsedValue >= minimumValue) &&
    (maximumValue === null || parsedValue <= maximumValue);

  return {
    canSubmit:
      field !== undefined &&
      inRange &&
      (currentValue === null || parsedValue !== currentValue),
    parsedValue
  };
}

function formatRaidBattleRewardLink(link: RaidBattleSlotRecord['dropRewardLink']) {
  const status = link.isMatched ? 'Matched' : 'Unmatched';
  const target = link.tableId || link.sourceTableHash;
  return `${status}: ${link.preview} (${target})`;
}

function getPendingItemIds(editSession: EditSession | null) {
  return new Set(
    (editSession?.pendingEdits ?? [])
      .filter((edit) => edit.domain === 'workflow.items')
      .map((edit) => Number.parseInt(edit.recordId ?? '', 10))
      .filter(Number.isInteger)
  );
}

function getPendingMoveIds(editSession: EditSession | null) {
  return new Set(
    (editSession?.pendingEdits ?? [])
      .filter((edit) => edit.domain === 'workflow.moves')
      .map((edit) => Number.parseInt(edit.recordId ?? '', 10))
      .filter(Number.isInteger)
  );
}

function getPendingPokemonIds(editSession: EditSession | null) {
  return new Set(
    (editSession?.pendingEdits ?? [])
      .filter((edit) => edit.domain === 'workflow.pokemon')
      .map((edit) => Number.parseInt(edit.recordId ?? '', 10))
      .filter(Number.isInteger)
  );
}

function getPendingTextKeys(editSession: EditSession | null) {
  return new Set(
    (editSession?.pendingEdits ?? [])
      .filter((edit) => edit.domain === 'workflow.text' && edit.recordId)
      .map((edit) => edit.recordId!)
  );
}

function getPendingTrainerIds(editSession: EditSession | null) {
  return new Set(
    (editSession?.pendingEdits ?? [])
      .filter((edit) => edit.domain === 'workflow.trainers')
      .map((edit) => Number.parseInt((edit.recordId ?? '').split(':')[0] ?? '', 10))
      .filter(Number.isInteger)
  );
}

function getPendingGiftPokemonIndexes(editSession: EditSession | null) {
  return new Set(
    (editSession?.pendingEdits ?? [])
      .filter((edit) => edit.domain === 'workflow.giftPokemon')
      .map((edit) => {
        const recordId = edit.recordId ?? '';
        const normalizedRecordId = recordId.startsWith('gift:') ? recordId.slice(5) : recordId;
        return Number.parseInt(normalizedRecordId, 10);
      })
      .filter(Number.isInteger)
  );
}

function getPendingTradePokemonIndexes(editSession: EditSession | null) {
  return new Set(
    (editSession?.pendingEdits ?? [])
      .filter((edit) => edit.domain === 'workflow.tradePokemon')
      .map((edit) => {
        const recordId = edit.recordId ?? '';
        const normalizedRecordId = recordId.startsWith('trade:') ? recordId.slice(6) : recordId;
        return Number.parseInt(normalizedRecordId, 10);
      })
      .filter(Number.isInteger)
  );
}

function getPendingStaticEncounterIndexes(editSession: EditSession | null) {
  return new Set(
    (editSession?.pendingEdits ?? [])
      .filter((edit) => edit.domain === 'workflow.staticEncounters')
      .map((edit) => {
        const recordId = edit.recordId ?? '';
        const normalizedRecordId = recordId.startsWith('static:')
          ? recordId.slice(7)
          : recordId;
        return Number.parseInt(normalizedRecordId, 10);
      })
      .filter(Number.isInteger)
  );
}

function getPendingRentalPokemonIndexes(editSession: EditSession | null) {
  return new Set(
    (editSession?.pendingEdits ?? [])
      .filter((edit) => edit.domain === 'workflow.rentalPokemon')
      .map((edit) => {
        const recordId = edit.recordId ?? '';
        const normalizedRecordId = recordId.startsWith('rental:')
          ? recordId.slice(7)
          : recordId;
        return Number.parseInt(normalizedRecordId, 10);
      })
      .filter(Number.isInteger)
  );
}

function getPendingDynamaxAdventureIndexes(editSession: EditSession | null) {
  return new Set(
    (editSession?.pendingEdits ?? [])
      .filter((edit) => edit.domain === 'workflow.dynamaxAdventures')
      .map((edit) => {
        const recordId = edit.recordId ?? '';
        const normalizedRecordId = recordId.startsWith('dynamaxAdventure:')
          ? recordId.slice(17)
          : recordId;
        return Number.parseInt(normalizedRecordId, 10);
      })
      .filter(Number.isInteger)
  );
}

function getPendingShopIds(editSession: EditSession | null) {
  return new Set(
    (editSession?.pendingEdits ?? [])
      .filter((edit) => edit.domain === 'workflow.shops' && edit.recordId)
      .map((edit) => edit.recordId!.split('#')[0])
  );
}

function getPendingEncounterTableIds(editSession: EditSession | null) {
  return new Set(
    (editSession?.pendingEdits ?? [])
      .filter((edit) => edit.domain === 'workflow.encounters' && edit.recordId)
      .map((edit) => edit.recordId!.split('#')[0])
  );
}

function getPendingRaidRewardTableIds(editSession: EditSession | null) {
  return new Set(
    (editSession?.pendingEdits ?? [])
      .filter((edit) => edit.domain === 'workflow.raidRewards' && edit.recordId)
      .map((edit) => edit.recordId!.split('#')[0])
  );
}

function getPendingRaidBattleTableIds(editSession: EditSession | null) {
  return new Set(
    (editSession?.pendingEdits ?? [])
      .filter((edit) => edit.domain === 'workflow.raidBattles' && edit.recordId)
      .map((edit) => edit.recordId!.split('#')[0])
  );
}

function getPendingPlacementObjectIds(editSession: EditSession | null) {
  return new Set(
    (editSession?.pendingEdits ?? [])
      .filter((edit) => edit.domain === 'workflow.placement' && edit.recordId)
      .map((edit) => edit.recordId!)
  );
}

function getTextDraftState(
  draftValue: string,
  entry: TextEntryRecord | null,
  field: TextEditableField | undefined
) {
  const minimumLength = field?.minimumLength ?? null;
  const maximumLength = field?.maximumLength ?? null;
  const inRange =
    (minimumLength === null || draftValue.length >= minimumLength) &&
    (maximumLength === null || draftValue.length <= maximumLength);
  const hasVariablePlaceholder = draftValue.includes('[VAR');

  return {
    canSubmit:
      entry !== null &&
      entry.canEdit &&
      inRange &&
      !hasVariablePlaceholder &&
      draftValue !== entry.value
  };
}

function formatSharedItemIds(item: ItemRecord) {
  if (item.sharedItemIds.length <= 1) {
    return 'No';
  }

  return item.sharedItemIds.join(', ');
}

function getWorkflowState(health: ProjectHealth | null, workflow: WorkflowSummary | undefined) {
  if (!health?.canOpenReadOnlyWorkflows) {
    return {
      availability: 'disabled',
      label: 'Disabled',
      statusClass: 'status-blocked'
    } as const;
  }

  if (workflow) {
    return {
      availability: workflow.availability,
      label: workflowAvailabilityLabels[workflow.availability],
      statusClass: workflowAvailabilityClassNames[workflow.availability]
    } as const;
  }

  return {
    availability: 'readOnly',
    label: 'Read-only',
    statusClass: 'status-warning'
  } as const;
}

function getExeFsStatusClassName(status: string) {
  switch (status.toLocaleLowerCase()) {
    case 'pass':
    case 'info':
    case 'available':
    case 'ready':
      return 'status-ready';
    case 'readonly':
    case 'read-only':
    case 'warning':
    case 'review':
      return 'status-warning';
    case 'fail':
    case 'blocked':
      return 'status-blocked';
    default:
      return 'status-warning';
  }
}

function getImportStatusClassName(status: string) {
  switch (status.toLocaleLowerCase()) {
    case 'accepted':
      return 'status-ready';
    case 'skipped':
      return 'status-warning';
    case 'rejected':
      return 'status-blocked';
    default:
      return getExeFsStatusClassName(status);
  }
}

function getEditableRaidRewardFieldValue(reward: RaidRewardItemRecord, field: string) {
  if (field === raidRewardItemIdFieldName) {
    return reward.itemId;
  }

  const valueIndex = raidRewardValueFieldNames.findIndex((fieldName) => fieldName === field);
  return valueIndex >= 0 ? (reward.values[valueIndex] ?? 0) : null;
}

function getEditableRaidBattleFieldValue(battleSlot: RaidBattleSlotRecord, field: string) {
  switch (field) {
    case raidBattleSpeciesFieldName:
      return battleSlot.speciesId;
    case raidBattleFormFieldName:
      return battleSlot.form;
    case raidBattleAbilityFieldName:
      return battleSlot.ability;
    case raidBattleIsGigantamaxFieldName:
      return battleSlot.isGigantamax ? 1 : 0;
    case raidBattleGenderFieldName:
      return battleSlot.gender;
    case raidBattleFlawlessIvsFieldName:
      return battleSlot.flawlessIvs;
    default: {
      const probabilityIndex = raidBattleProbabilityFieldNames.findIndex(
        (fieldName) => fieldName === field
      );
      return probabilityIndex >= 0 ? (battleSlot.probabilities[probabilityIndex] ?? 0) : null;
    }
  }
}

function isPlacementFieldVisible(placedObject: PlacedObjectRecord, field: string) {
  if (field === placementChanceFieldName) {
    return placedObject.objectType === 'HiddenItem';
  }

  if (field === placementItemIdFieldName) {
    return placedObject.itemId !== null || placedObject.itemHash.length > 0;
  }

  return [
    placementLocationXFieldName,
    placementLocationYFieldName,
    placementLocationZFieldName,
    placementRotationYFieldName,
    placementQuantityFieldName
  ].includes(field);
}

function getEditablePlacementFieldValue(placedObject: PlacedObjectRecord, field: string) {
  switch (field) {
    case placementLocationXFieldName:
      return placedObject.x;
    case placementLocationYFieldName:
      return placedObject.y;
    case placementLocationZFieldName:
      return placedObject.z;
    case placementRotationYFieldName:
      return placedObject.rotationY;
    case placementItemIdFieldName:
      return placedObject.itemId;
    case placementQuantityFieldName:
      return placedObject.quantity;
    case placementChanceFieldName:
      return placedObject.chance;
    default:
      return null;
  }
}

function getPlacementDraftState(
  draftValue: string,
  currentValue: number | null,
  field: PlacementEditableField
) {
  const normalizedValue = draftValue.trim();
  if (!normalizedValue) {
    return {
      canSubmit: false,
      normalizedValue: null
    };
  }

  const parsedValue =
    field.valueKind === 'integer'
      ? parseEditableIntegerDraft(normalizedValue, field.options) ?? Number.NaN
      : Number(normalizedValue);
  const isValidNumber = Number.isFinite(parsedValue);
  const inRange =
    isValidNumber &&
    parsedValue >= field.minimumValue &&
    parsedValue <= field.maximumValue &&
    (field.valueKind !== 'integer' || Number.isInteger(parsedValue));
  const nextValue =
    field.valueKind === 'integer'
      ? parsedValue.toString()
      : parsedValue.toString();

  return {
    canSubmit:
      inRange &&
      (currentValue === null || Math.abs(parsedValue - currentValue) > Number.EPSILON),
    normalizedValue: inRange ? nextValue : null
  };
}

function getPlacementDraftSummary(
  fields: PlacementEditableField[],
  drafts: Record<string, string>,
  getValue: ((field: string) => number | null) | null
): { changedFields: TrainerDraftChange[]; dirtyFieldCount: number; invalidFields: TrainerDraftChange[] } {
  const changedFields: TrainerDraftChange[] = [];
  const invalidFields: TrainerDraftChange[] = [];
  let dirtyFieldCount = 0;

  if (!getValue) {
    return { changedFields, dirtyFieldCount, invalidFields };
  }

  for (const field of fields) {
    const currentValue = getValue(field.field);
    const draftValue = drafts[field.field] ?? '';
    const draftState = getPlacementDraftState(draftValue, currentValue, field);
    const isChanged = draftState.normalizedValue !== null;
    const isInvalid = draftValue.trim() !== '' && draftState.normalizedValue === null;

    if (isChanged || isInvalid) {
      dirtyFieldCount += 1;
    }

    if (isInvalid) {
      invalidFields.push({ field: field.field, label: field.label, value: draftValue });
      continue;
    }

    const normalizedValue = draftState.normalizedValue;

    if (normalizedValue !== null) {
      changedFields.push({
        field: field.field,
        label: field.label,
        value: normalizedValue
      });
    }
  }

  return { changedFields, dirtyFieldCount, invalidFields };
}

function formatPlacementItem(placedObject: PlacedObjectRecord) {
  if (placedObject.itemId === null) {
    return placedObject.itemHash || placedObject.itemName;
  }

  return `${placedObject.itemName} (${placedObject.itemId})`;
}

function formatPlacementCoordinates(placedObject: PlacedObjectRecord) {
  return `${formatCoordinate(placedObject.x)}, ${formatCoordinate(placedObject.y)}, ${formatCoordinate(placedObject.z)}`;
}

function formatCoordinate(value: number) {
  return Number.isInteger(value) ? value.toString() : value.toFixed(2);
}

function formatPokemonTypes(pokemon: PokemonRecord) {
  return pokemon.type1 === pokemon.type2
    ? pokemon.type1
    : `${pokemon.type1} / ${pokemon.type2}`;
}

function formatPokemonDexPresence(pokemon: PokemonRecord) {
  if (!pokemon.dexPresence.isPresentInGame) {
    return 'Not present';
  }

  if (!pokemon.dexPresence.isInAnyDex) {
    return 'Present, no dex index';
  }

  return [
    pokemon.dexPresence.regionalDexIndex > 0
      ? `Regional ${pokemon.dexPresence.regionalDexIndex}`
      : null,
    pokemon.dexPresence.armorDexIndex > 0 ? `Armor ${pokemon.dexPresence.armorDexIndex}` : null,
    pokemon.dexPresence.crownDexIndex > 0 ? `Crown ${pokemon.dexPresence.crownDexIndex}` : null
  ]
    .filter((value): value is string => value !== null)
    .join(', ');
}

function formatGiftPokemonIvs(gift: GiftPokemonRecord) {
  return [
    `HP ${formatGiftPokemonIvValue(gift.ivs.hp)}`,
    `Atk ${formatGiftPokemonIvValue(gift.ivs.attack)}`,
    `Def ${formatGiftPokemonIvValue(gift.ivs.defense)}`,
    `SpA ${formatGiftPokemonIvValue(gift.ivs.specialAttack)}`,
    `SpD ${formatGiftPokemonIvValue(gift.ivs.specialDefense)}`,
    `Spe ${formatGiftPokemonIvValue(gift.ivs.speed)}`
  ].join(' / ');
}

function formatGiftPokemonIvValue(value: number) {
  switch (value) {
    case -4:
      return '3 perfect';
    case -1:
      return 'Random';
    default:
      return value.toString();
  }
}

function formatTradePokemonIvs(trade: TradePokemonRecord) {
  return [
    `HP ${formatGiftPokemonIvValue(trade.ivs.hp)}`,
    `Atk ${formatGiftPokemonIvValue(trade.ivs.attack)}`,
    `Def ${formatGiftPokemonIvValue(trade.ivs.defense)}`,
    `SpA ${formatGiftPokemonIvValue(trade.ivs.specialAttack)}`,
    `SpD ${formatGiftPokemonIvValue(trade.ivs.specialDefense)}`,
    `Spe ${formatGiftPokemonIvValue(trade.ivs.speed)}`
  ].join(' / ');
}

function formatTradePokemonRelearnMoves(trade: TradePokemonRecord) {
  const moves = trade.relearnMoves
    .filter((move) => move.moveId > 0)
    .map((move) => move.move ?? `Move ${move.moveId}`);

  return moves.length > 0 ? moves.join(' / ') : 'None';
}

function formatTradePokemonMemory(trade: TradePokemonRecord) {
  return [
    `Trainer ${trade.trainerId}`,
    `OT ${trade.otGenderLabel}`,
    `Memory ${trade.memoryCode}`,
    `Text ${trade.memoryTextVariable}`,
    `Feeling ${trade.memoryFeel}`,
    `Intensity ${trade.memoryIntensity}`
  ].join(' / ');
}

const regionalFormLabelsBySpeciesId = new Map<string, string>([
  ['19:1', 'Alolan'],
  ['20:1', 'Alolan'],
  ['26:1', 'Alolan'],
  ['27:1', 'Alolan'],
  ['28:1', 'Alolan'],
  ['37:1', 'Alolan'],
  ['38:1', 'Alolan'],
  ['50:1', 'Alolan'],
  ['51:1', 'Alolan'],
  ['52:1', 'Alolan'],
  ['52:2', 'Galarian'],
  ['53:1', 'Alolan'],
  ['74:1', 'Alolan'],
  ['75:1', 'Alolan'],
  ['76:1', 'Alolan'],
  ['77:1', 'Galarian'],
  ['78:1', 'Galarian'],
  ['79:1', 'Galarian'],
  ['80:1', 'Galarian'],
  ['83:1', 'Galarian'],
  ['88:1', 'Alolan'],
  ['89:1', 'Alolan'],
  ['103:1', 'Alolan'],
  ['105:1', 'Alolan'],
  ['110:1', 'Galarian'],
  ['122:1', 'Galarian'],
  ['144:1', 'Galarian'],
  ['145:1', 'Galarian'],
  ['146:1', 'Galarian'],
  ['199:1', 'Galarian'],
  ['222:1', 'Galarian'],
  ['263:1', 'Galarian'],
  ['264:1', 'Galarian'],
  ['554:1', 'Galarian'],
  ['555:1', 'Galarian'],
  ['562:1', 'Galarian'],
  ['618:1', 'Galarian']
]);

const regionalFormLabelsBySpeciesName = new Map<string, string>([
  ['rattata:1', 'Alolan'],
  ['raticate:1', 'Alolan'],
  ['raichu:1', 'Alolan'],
  ['sandshrew:1', 'Alolan'],
  ['sandslash:1', 'Alolan'],
  ['vulpix:1', 'Alolan'],
  ['ninetales:1', 'Alolan'],
  ['diglett:1', 'Alolan'],
  ['dugtrio:1', 'Alolan'],
  ['meowth:1', 'Alolan'],
  ['meowth:2', 'Galarian'],
  ['persian:1', 'Alolan'],
  ['geodude:1', 'Alolan'],
  ['graveler:1', 'Alolan'],
  ['golem:1', 'Alolan'],
  ['ponyta:1', 'Galarian'],
  ['rapidash:1', 'Galarian'],
  ['slowpoke:1', 'Galarian'],
  ['slowbro:1', 'Galarian'],
  ['farfetchd:1', 'Galarian'],
  ['grimer:1', 'Alolan'],
  ['muk:1', 'Alolan'],
  ['exeggutor:1', 'Alolan'],
  ['marowak:1', 'Alolan'],
  ['weezing:1', 'Galarian'],
  ['mrmime:1', 'Galarian'],
  ['articuno:1', 'Galarian'],
  ['zapdos:1', 'Galarian'],
  ['moltres:1', 'Galarian'],
  ['slowking:1', 'Galarian'],
  ['corsola:1', 'Galarian'],
  ['zigzagoon:1', 'Galarian'],
  ['linoone:1', 'Galarian'],
  ['darumaka:1', 'Galarian'],
  ['darmanitan:1', 'Galarian'],
  ['yamask:1', 'Galarian'],
  ['stunfisk:1', 'Galarian']
]);
const knownAlternateFormSpeciesNames = new Set([
  'aegislash',
  'alcremie',
  'articuno',
  'basculin',
  'calyrex',
  'corsola',
  'cramorant',
  'darmanitan',
  'darumaka',
  'eiscue',
  'farfetchd',
  'gastrodon',
  'gourgeist',
  'indeedee',
  'keldeo',
  'kyurem',
  'landorus',
  'linoone',
  'meowth',
  'mimikyu',
  'moltres',
  'mrmime',
  'morpeko',
  'necrozma',
  'ponyta',
  'polteageist',
  'pumpkaboo',
  'rapidash',
  'rotom',
  'shellos',
  'sinistea',
  'slowbro',
  'slowking',
  'slowpoke',
  'stunfisk',
  'thundurus',
  'tornadus',
  'toxtricity',
  'unown',
  'urshifu',
  'weezing',
  'yamask',
  'xerneas',
  'zacian',
  'zamazenta',
  'zapdos',
  'zarude',
  'zigzagoon',
  'zygarde'
]);

function formatSpeciesFormLabel(species: string, form: number, speciesId?: number) {
  if (form === 0) {
    return species;
  }

  const formLabel =
    resolveRegionalFormLabel(species, form, speciesId) ??
    `Form ${form}`;

  return `${species} (${formLabel})`;
}

function formatSpeciesFormOptionLabel(form: number, context: SpeciesFormOptionContext) {
  if (form === 0) {
    return 'Base';
  }

  return resolveRegionalFormLabel(context.species, form, context.speciesId) ?? `Form ${form}`;
}

function resolveRegionalFormLabel(species: string, form: number, speciesId?: number) {
  return (
    (speciesId !== undefined
      ? regionalFormLabelsBySpeciesId.get(`${speciesId}:${form}`) ??
        resolveOnlyRegionalFormLabelBySpeciesId(speciesId)
      : undefined) ??
    regionalFormLabelsBySpeciesName.get(`${normalizeSpeciesName(species)}:${form}`) ??
    resolveOnlyRegionalFormLabelBySpeciesName(species)
  );
}

function resolveOnlyRegionalFormLabelBySpeciesId(speciesId: number) {
  const labels = new Set<string>();

  for (const [key, label] of regionalFormLabelsBySpeciesId) {
    const [candidateSpeciesId] = key.split(':');
    if (Number.parseInt(candidateSpeciesId, 10) === speciesId) {
      labels.add(label);
    }
  }

  return labels.size === 1 ? [...labels][0] : undefined;
}

function resolveOnlyRegionalFormLabelBySpeciesName(species: string) {
  const normalizedSpecies = normalizeSpeciesName(species);
  const labels = new Set<string>();

  for (const [key, label] of regionalFormLabelsBySpeciesName) {
    const [candidateSpecies] = key.split(':');
    if (candidateSpecies === normalizedSpecies) {
      labels.add(label);
    }
  }

  return labels.size === 1 ? [...labels][0] : undefined;
}

function normalizeSpeciesName(species: string) {
  return species
    .normalize('NFKD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/[^a-zA-Z0-9]/g, '')
    .toLocaleLowerCase();
}

function formatStaticEncounterIvs(encounter: StaticEncounterRecord) {
  return [
    `HP ${formatGiftPokemonIvValue(encounter.ivs.hp)}`,
    `Atk ${formatGiftPokemonIvValue(encounter.ivs.attack)}`,
    `Def ${formatGiftPokemonIvValue(encounter.ivs.defense)}`,
    `SpA ${formatGiftPokemonIvValue(encounter.ivs.specialAttack)}`,
    `SpD ${formatGiftPokemonIvValue(encounter.ivs.specialDefense)}`,
    `Spe ${formatGiftPokemonIvValue(encounter.ivs.speed)}`
  ].join(' / ');
}

function formatStaticEncounterStats(stats: StaticEncounterRecord['evs']) {
  return [
    `HP ${stats.hp}`,
    `Atk ${stats.attack}`,
    `Def ${stats.defense}`,
    `SpA ${stats.specialAttack}`,
    `SpD ${stats.specialDefense}`,
    `Spe ${stats.speed}`
  ].join(' / ');
}

function formatStaticEncounterMoves(encounter: StaticEncounterRecord) {
  const moves = encounter.moves
    .filter((move) => move.moveId > 0)
    .map((move) => move.move ?? `Move ${move.moveId}`);

  return moves.length > 0 ? moves.join(' / ') : 'None';
}

function formatRentalPokemonIvs(rental: RentalPokemonRecord) {
  return [
    `HP ${rental.ivs.hp}`,
    `Atk ${rental.ivs.attack}`,
    `Def ${rental.ivs.defense}`,
    `SpA ${rental.ivs.specialAttack}`,
    `SpD ${rental.ivs.specialDefense}`,
    `Spe ${rental.ivs.speed}`
  ].join(' / ');
}

function formatRentalPokemonStats(stats: RentalPokemonRecord['evs']) {
  return [
    `HP ${stats.hp}`,
    `Atk ${stats.attack}`,
    `Def ${stats.defense}`,
    `SpA ${stats.specialAttack}`,
    `SpD ${stats.specialDefense}`,
    `Spe ${stats.speed}`
  ].join(' / ');
}

function formatRentalPokemonMoves(rental: RentalPokemonRecord) {
  const moves = rental.moves
    .filter((move) => move.moveId > 0)
    .map((move) => move.move ?? `Move ${move.moveId}`);

  return moves.length > 0 ? moves.join(' / ') : 'None';
}

function formatDynamaxAdventureIvs(encounter: DynamaxAdventureRecord) {
  const hpValue =
    encounter.guaranteedPerfectIvs > 0
      ? `${encounter.guaranteedPerfectIvs} guaranteed perfect`
      : formatDynamaxAdventureIvValue(encounter.ivs.hp);

  return [
    `HP ${hpValue}`,
    `Atk ${formatDynamaxAdventureIvValue(encounter.ivs.attack)}`,
    `Def ${formatDynamaxAdventureIvValue(encounter.ivs.defense)}`,
    `SpA ${formatDynamaxAdventureIvValue(encounter.ivs.specialAttack)}`,
    `SpD ${formatDynamaxAdventureIvValue(encounter.ivs.specialDefense)}`,
    `Spe ${formatDynamaxAdventureIvValue(encounter.ivs.speed)}`
  ].join(' / ');
}

function formatDynamaxAdventureIvValue(value: number) {
  return value === -1 ? 'Random' : value.toString();
}

function formatDynamaxAdventureMoves(encounter: DynamaxAdventureRecord) {
  const moves = encounter.moves
    .filter((move) => move.moveId > 0)
    .map((move) => move.move ?? `Move ${move.moveId}`);

  return moves.length > 0 ? moves.join(' / ') : 'None';
}

function getFixedRentalPokemonIvPreset(rental: RentalPokemonRecord) {
  const values = [
    rental.ivs.hp,
    rental.ivs.attack,
    rental.ivs.defense,
    rental.ivs.specialAttack,
    rental.ivs.specialDefense,
    rental.ivs.speed
  ];
  const firstValue = values[0] ?? null;

  return firstValue !== null && values.every((value) => value === firstValue)
    ? firstValue
    : null;
}

function formatMovePower(power: number) {
  return power === 0 ? '-' : power.toString();
}

function formatMoveAccuracy(accuracy: number) {
  return accuracy === 0 ? '-' : accuracy.toString();
}

function formatMoveActiveFlags(move: MoveRecord) {
  const activeFlags = move.flags.filter((flag) => flag.enabled);

  if (activeFlags.length === 0) {
    return '-';
  }

  return activeFlags
    .slice(0, 3)
    .map((flag) => flag.label)
    .join(', ');
}

const workflowAvailabilityLabels = {
  available: 'Available',
  disabled: 'Disabled',
  readOnly: 'Read-only'
} as const;

const workflowAvailabilityClassNames = {
  available: 'status-ready',
  disabled: 'status-blocked',
  readOnly: 'status-warning'
} as const;

function formatSourceLayer(
  layer:
    | EncounterTableRecord['provenance']['sourceLayer']
    | DynamaxAdventureRecord['provenance']['sourceLayer']
    | FlagRecord['provenance']['sourceLayer']
    | GiftPokemonRecord['provenance']['sourceLayer']
    | ItemRecord['provenance']['sourceLayer']
    | MoveRecord['provenance']['sourceLayer']
    | PokemonRecord['provenance']['sourceLayer']
    | RaidRewardTableRecord['provenance']['sourceLayer']
    | RentalPokemonRecord['provenance']['sourceLayer']
    | SaveBlockRecord['provenance']['sourceLayer']
    | ShopRecord['provenance']['sourceLayer']
    | SpreadsheetImportProfileRecord['provenance']['sourceLayer']
    | StaticEncounterRecord['provenance']['sourceLayer']
    | TextEntryRecord['provenance']['sourceLayer']
    | TradePokemonRecord['provenance']['sourceLayer']
    | TrainerRecord['provenance']['sourceLayer']
) {
  return {
    base: 'Base',
    generated: 'Generated',
    layered: 'LayeredFS',
    pending: 'Pending'
  }[layer];
}

function formatProjectFileLayer(layer: ChangePlan['writes'][number]['sources'][number]['layer']) {
  return {
    base: 'Base',
    generated: 'Generated',
    layered: 'LayeredFS',
    pending: 'Pending'
  }[layer];
}

function formatFileState(
  state:
    | EncounterTableRecord['provenance']['fileState']
    | DynamaxAdventureRecord['provenance']['fileState']
    | FlagRecord['provenance']['fileState']
    | GiftPokemonRecord['provenance']['fileState']
    | ItemRecord['provenance']['fileState']
    | MoveRecord['provenance']['fileState']
    | PokemonRecord['provenance']['fileState']
    | RaidRewardTableRecord['provenance']['fileState']
    | RentalPokemonRecord['provenance']['fileState']
    | SaveBlockRecord['provenance']['fileState']
    | ShopRecord['provenance']['fileState']
    | SpreadsheetImportProfileRecord['provenance']['fileState']
    | StaticEncounterRecord['provenance']['fileState']
    | TextEntryRecord['provenance']['fileState']
    | TradePokemonRecord['provenance']['fileState']
    | TrainerRecord['provenance']['fileState']
) {
  return {
    baseOnly: 'Base only',
    layeredOnly: 'Layered only',
    layeredOverride: 'Layered override'
  }[state];
}

function getPathStatusClassName(pathValidation: ProjectPathValidation | undefined) {
  if (!pathValidation) {
    return 'path-status path-status-muted';
  }

  return `path-status path-status-${pathValidation.status}`;
}

function getProjectStateLabel(
  health: ProjectHealth | null,
  projectStatus: 'idle' | 'validating' | 'opening' | 'open'
) {
  if (projectStatus === 'opening') {
    return 'Opening project';
  }

  if (projectStatus === 'validating') {
    return 'Validating paths';
  }

  return health ? healthLabels[health.state] : 'No project open';
}

function toProjectPaths(draftPaths: ProjectPathDraft) {
  return {
    baseExeFsPath: normalizeDraftPath(draftPaths.baseExeFsPath),
    baseRomFsPath: normalizeDraftPath(draftPaths.baseRomFsPath),
    outputRootPath: normalizeDraftPath(draftPaths.outputRootPath),
    saveFilePath: normalizeDraftPath(draftPaths.saveFilePath)
  };
}

function formatByteCount(value: number) {
  return `${value.toLocaleString()} bytes`;
}

function normalizeDraftPath(path: string) {
  const trimmedPath = path.trim();

  return trimmedPath.length > 0 ? trimmedPath : null;
}

function getEditSessionSignature(editSession: EditSession | null) {
  if (!editSession) {
    return null;
  }

  return JSON.stringify({
    edits: editSession.pendingEdits.map((edit) => ({
      domain: edit.domain,
      field: edit.field,
      newValue: edit.newValue,
      recordId: edit.recordId,
      sources: edit.sources.map((source) => source.relativePath)
    })),
    sessionId: editSession.sessionId
  });
}

function delay(milliseconds: number) {
  if (import.meta.env.MODE === 'test') {
    return Promise.resolve();
  }

  return new Promise<void>((resolve) => {
    window.setTimeout(resolve, milliseconds);
  });
}

function toBridgeDiagnostics(error: unknown): ApiDiagnostic[] {
  if (error instanceof ProjectBridgeError) {
    return error.apiError.diagnostics.length > 0
      ? error.apiError.diagnostics
      : [
          {
            domain: 'bridge',
            message: error.apiError.message,
            severity: 'error'
          }
        ];
  }

  return [
    {
      domain: 'bridge',
      message: error instanceof Error ? error.message : 'Project bridge request failed.',
      severity: 'error'
    }
  ];
}

function toDesktopDiagnostics(error: unknown, fallbackMessage: string): ApiDiagnostic[] {
  return [
    {
      domain: 'desktop',
      message:
        error instanceof Error
          ? error.message
          : typeof error === 'string'
            ? error
            : fallbackMessage,
      severity: 'error'
    }
  ];
}
