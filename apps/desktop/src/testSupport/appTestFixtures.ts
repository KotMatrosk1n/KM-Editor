/* SPDX-License-Identifier: GPL-3.0-only */

import {
  type BagHookWorkflow,
  type BehaviorWorkflow,
  type CatchCapWorkflow,
  type ChangePlan,
  type DynamaxAdventuresWorkflow,
  type EditSession,
  type EncountersWorkflow,
  type EncounterTableRecord,
  type ExeFsPatchWorkflow,
  type FlagworkSaveWorkflow,
  type GiftPokemonWorkflow,
  type GymUniformRemovalWorkflow,
  type HyperTrainingWorkflow,
  type IvScreenWorkflow,
  type ItemRecord,
  type ItemsWorkflow,
  type ModMergerPreview,
  type ModMergerWorkflow,
  type MovesWorkflow,
  type PlacementWorkflow,
  type PokemonWorkflow,
  type ProjectFileGraph,
  type ProjectHealth,
  type RaidBattlesWorkflow,
  type RaidRewardsWorkflow,
  type RentalPokemonWorkflow,
  type RoyalCandyWorkflow,
  type ShopsWorkflow,
  type SpreadsheetImportWorkflow,
  type StartingItemsWorkflow,
  type SvModMergerPreview,
  type SvModMergerSource,
  type SvModMergerWorkflow,
  type StaticEncountersWorkflow,
  type TextWorkflow,
  type TradePokemonWorkflow,
  type TrainersWorkflow,
  type TypeChartWorkflow,
  type WorkflowSummary
} from '../bridge/contracts';
import { type ProjectBridge } from '../bridge/projectBridge';
import { type DesktopServices, type NativeUpdate } from '../desktopServices';

export function createNativeUpdate(overrides: Partial<NativeUpdate> = {}): NativeUpdate {
  return {
    close: async () => undefined,
    install: async () => undefined,
    version: '0.2.0',
    ...overrides
  };
}

export function createMockDesktopServices(overrides: Partial<DesktopServices> = {}): DesktopServices {
  return {
    checkForNativeUpdate: async () => null,
    createDirectory: async () => undefined,
    exitApp: async () => undefined,
    isAvailable: true,
    openExternalUrl: async () => undefined,
    openPath: async () => undefined,
    pickFile: async () => null,
    pickFolder: async () => null,
    relaunchApp: async () => undefined,
    setCloseGuardEnabled: async () => undefined,
    ...overrides
  };
}

export function createHealthForValidatedPaths(
  baseRomFsPath: string,
  baseExeFsPath: string,
  outputRootPath: string,
  saveFilePath: string | null
): ProjectHealth {
  return {
    canOpenEditableWorkflows: true,
    canOpenReadOnlyWorkflows: true,
    diagnostics: [],
    fileGraph: {
      baseFileCount: 2,
      layeredFileCount: 0,
      layeredOnlyCount: 0,
      overrideCount: 0
    },
    paths: [
      {
        diagnostics: [],
        isRequired: true,
        path: baseRomFsPath,
        role: 'baseRomFs',
        status: 'valid'
      },
      {
        diagnostics: [],
        isRequired: true,
        path: baseExeFsPath,
        role: 'baseExeFs',
        status: 'valid'
      },
      {
        diagnostics: [],
        isRequired: false,
        path: outputRootPath,
        role: 'outputRoot',
        status: 'valid'
      },
      {
        diagnostics: [],
        isRequired: false,
        path: saveFilePath,
        role: 'saveFile',
        status: saveFilePath ? 'valid' : 'notSet'
      }
    ],
    state: 'editableReady'
  };
}

function createItemDetailGroups(metadata = createItemMetadata()) {
  const pouchLabel = metadata.pouch === 4 ? 'Items (4)' : 'Medicine (0)';
  const healLabel =
    metadata.healAmount === 254
      ? 'Half HP'
      : metadata.healAmount === 255
      ? 'Full HP'
      : `${metadata.healAmount} HP`;

  return [
    {
      details: [
        { label: 'Pouch', value: pouchLabel },
        { label: 'Sprite', value: '12' },
        { label: 'Machine', value: 'No machine link' }
      ],
      label: 'Inventory'
    },
    {
      details: [
        { label: 'Field use type', value: 'Medicine (1)' },
        { label: 'Can use on Pokemon', value: 'Yes' },
        { label: 'Use flags 1 (decoded)', value: 'Restore HP' }
      ],
      label: 'Field Use'
    },
    {
      details: [
        { label: 'Fling power', value: '30' },
        { label: 'Cures status', value: 'None' }
      ],
      label: 'Battle'
    },
    {
      details: [
        { label: 'Heal', value: healLabel },
        { label: 'Friendship gains', value: '+1 / +1 / 0' }
      ],
      label: 'Pokemon Effects'
    }
  ];
}

function createItemMetadata(): ItemRecord['metadata'] {
  return {
    boost0: 0,
    boost1: 0,
    boost2: 0,
    boost3: 0,
    canUseOnPokemon: true,
    cureStatusFlags: 0,
    evAttack: 0,
    evDefense: 0,
    evHp: 0,
    evSpecialAttack: 0,
    evSpecialDefense: 0,
    evSpeed: 0,
    fieldFlags: 2,
    fieldUseType: 1,
    flingPower: 30,
    friendshipGain1: 1,
    friendshipGain2: 1,
    friendshipGain3: 0,
    groupIndex: 0,
    groupType: 0,
    healAmount: 20,
    itemSprite: 12,
    itemType: 9,
    machineMoveId: null,
    machineMoveName: null,
    machineSlot: null,
    pouch: 0,
    pouchFlags: 0,
    ppGain: 0,
    sortIndex: 5,
    useFlags1: 4,
    useFlags2: 0
  };
}

export function createMockProjectBridge(
  overrides: Partial<ProjectBridge> = {},
  canEdit = true
): ProjectBridge {
  const health: ProjectHealth = {
    canOpenEditableWorkflows: canEdit,
    canOpenReadOnlyWorkflows: true,
    diagnostics: [],
    fileGraph: {
      baseFileCount: 2,
      layeredFileCount: 0,
      layeredOnlyCount: 0,
      overrideCount: 0
    },
    paths: [
      {
        diagnostics: [],
        isRequired: true,
        path: 'base-romfs',
        role: 'baseRomFs',
        status: 'valid'
      },
      {
        diagnostics: [],
        isRequired: true,
        path: 'base-exefs',
        role: 'baseExeFs',
        status: 'valid'
      },
      {
        diagnostics: [],
        isRequired: false,
        path: canEdit ? 'output' : null,
        role: 'outputRoot',
        status: canEdit ? 'valid' : 'notSet'
      },
      {
        diagnostics: [],
        isRequired: false,
        path: null,
        role: 'saveFile',
        status: 'notSet'
      }
    ],
    state: canEdit ? 'editableReady' : 'readOnlyReady'
  };
  const fileGraph: ProjectFileGraph = {
    entries: [],
    summary: health.fileGraph
  };
  const itemsWorkflow: ItemsWorkflow = {
    diagnostics: [],
    editableFields: [
      {
        field: 'buyPrice',
        label: 'Buy price',
        maximumValue: 999_999,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'sellPrice',
        label: 'Sell price',
        maximumValue: 499_999,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'wattsPrice',
        label: 'Watts price',
        maximumValue: 999_999,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'alternatePrice',
        label: 'Alternate price',
        maximumValue: 999_999,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'pouch',
        label: 'Pouch',
        maximumValue: 8,
        minimumValue: 0,
        options: [
          { label: 'Medicine', value: 0 },
          { label: 'Items', value: 4 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'healAmount',
        label: 'Heal amount',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'evAttack',
        label: 'Attack EV gain',
        maximumValue: 127,
        minimumValue: -128,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'canUseOnPokemon',
        label: 'Can use on Pokemon',
        maximumValue: 1,
        minimumValue: 0,
        options: [
          { label: 'No', value: 0 },
          { label: 'Yes', value: 1 }
        ],
        valueKind: 'boolean'
      }
    ],
    items: [
      {
        alternatePrice: 3,
        buyPrice: 300,
        category: 'Medicine',
        detailGroups: createItemDetailGroups(),
        itemId: 1,
        metadata: createItemMetadata(),
        name: 'Potion',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/pml/item/item.dat',
          sourceLayer: 'base'
        },
        sellPrice: 150,
        sharedItemIds: [1],
        wattsPrice: 15
      },
      {
        alternatePrice: 5,
        buyPrice: 200,
        category: 'Medicine',
        detailGroups: createItemDetailGroups(),
        itemId: 2,
        metadata: createItemMetadata(),
        name: 'Antidote',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/pml/item/item.dat',
          sourceLayer: 'base'
        },
        sellPrice: 100,
        sharedItemIds: [2],
        wattsPrice: 10
      },
      {
        alternatePrice: 0,
        buyPrice: 1000,
        category: 'TMs',
        detailGroups: createItemDetailGroups(),
        itemId: 335,
        metadata: createItemMetadata(),
        name: 'TM02 (Razor Leaf)',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/pml/item/item.dat',
          sourceLayer: 'base'
        },
        sellPrice: 500,
        sharedItemIds: [335],
        wattsPrice: 0
      },
      {
        alternatePrice: 0,
        buyPrice: 1000,
        category: 'TMs',
        detailGroups: createItemDetailGroups(),
        itemId: 337,
        metadata: createItemMetadata(),
        name: 'TM10 (Magical Leaf)',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/pml/item/item.dat',
          sourceLayer: 'base'
        },
        sellPrice: 500,
        sharedItemIds: [337],
        wattsPrice: 0
      },
      {
        alternatePrice: 0,
        buyPrice: 3000,
        category: 'TRs',
        detailGroups: createItemDetailGroups(),
        itemId: 1120,
        metadata: createItemMetadata(),
        name: 'TR02 (Growl)',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/pml/item/item.dat',
          sourceLayer: 'base'
        },
        sellPrice: 1500,
        sharedItemIds: [1120],
        wattsPrice: 0
      },
      {
        alternatePrice: 0,
        buyPrice: 3000,
        category: 'TRs',
        detailGroups: createItemDetailGroups(),
        itemId: 1128,
        metadata: createItemMetadata(),
        name: 'TR10 (Magical Leaf)',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/pml/item/item.dat',
          sourceLayer: 'base'
        },
        sellPrice: 1500,
        sharedItemIds: [1128],
        wattsPrice: 0
      }
    ],
    stats: {
      sourceFileCount: 2,
      totalItemCount: 6
    },
    summary: {
      availability: canEdit ? 'available' : 'readOnly',
      description: 'Item records, names, and source provenance.',
      diagnostics: [],
      id: 'items',
      label: 'Items'
    }
  };
  const pokemonWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Pokemon personal stats, forms, evolutions, learnsets, and source provenance.',
    diagnostics: [],
    id: 'pokemon',
    label: 'Pokemon'
  };
  const pokemonWorkflow: PokemonWorkflow = {
    diagnostics: [],
    editableFields: [
      {
        field: 'hp',
        group: 'Base Stats',
        label: 'HP',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'type1',
        group: 'Traits',
        label: 'Type 1',
        maximumValue: 17,
        minimumValue: 0,
        options: [
          { label: 'Normal', value: 0 },
          { label: 'Grass', value: 11 },
          { label: 'Fire', value: 9 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'heldItem1',
        group: 'Held Items',
        label: 'Held Item 50%',
        maximumValue: 32767,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Potion', value: 1 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'ability1',
        group: 'Abilities',
        label: 'Ability 1',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '034 Chlorophyll', value: 34 },
          { label: '065 Overgrow', value: 65 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'hatchedSpecies',
        group: 'Forms/Dex',
        label: 'Hatched Species',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Bulbasaur', value: 1 },
          { label: '002 Ivysaur', value: 2 },
          { label: '003 Venusaur', value: 3 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'canNotDynamax',
        group: 'Flags',
        label: 'Cannot Dynamax',
        maximumValue: 1,
        minimumValue: 0,
        options: [],
        valueKind: 'boolean'
      }
    ],
    evolutionMethodOptions: [
      {
        argumentKind: 'level',
        argumentLabel: 'Level',
        argumentOptions: [],
        label: '004 Level Up',
        value: 4
      },
      {
        argumentKind: 'item',
        argumentLabel: 'Item',
        argumentOptions: [
          { label: '000 None', value: 0 },
          { label: '001 Potion', value: 1 },
          { label: '025 Thunder Stone', value: 25 }
        ],
        label: '008 Use Item',
        value: 8
      }
    ],
    learnsetMoveOptions: [
      { label: '033 Tackle', value: 33 },
      { label: '045 Growl', value: 45 },
      { label: '075 Razor Leaf', value: 75 },
      { label: '345 Magical Leaf', value: 345 }
    ],
    pokemon: [
      {
        abilities: {
          ability1: 65,
          ability1Label: 'Overgrow',
          ability2: 0,
          ability2Label: 'None',
          hiddenAbility: 34,
          hiddenAbilityLabel: 'Chlorophyll'
        },
        baseExperience: 64,
        baseStats: {
          attack: 49,
          defense: 49,
          hp: 45,
          specialAttack: 65,
          specialDefense: 65,
          speed: 45,
          total: 318
        },
        catchRate: 45,
        compatibility: [
          {
            enabledCount: 1,
            entries: [
              {
                canLearn: false,
                label: 'TM00 Mega Punch',
                moveId: 5,
                moveName: 'Mega Punch',
                slot: 0
              },
              {
                canLearn: true,
                label: 'TM10 Magical Leaf',
                moveId: 345,
                moveName: 'Magical Leaf',
                slot: 10
              }
            ],
            groupId: 'tm',
            label: 'TMs'
          },
          {
            enabledCount: 0,
            entries: [
              {
                canLearn: false,
                label: 'TR00 Swords Dance',
                moveId: 14,
                moveName: 'Swords Dance',
                slot: 0
              }
            ],
            groupId: 'tr',
            label: 'TRs'
          }
        ],
        dexPresence: {
          armorDexIndex: 0,
          crownDexIndex: 0,
          isInAnyDex: true,
          isPresentInGame: true,
          regionalDexIndex: 1
        },
        evolutionStage: 1,
        evolutions: [
          {
            argument: 0,
            argumentKind: 'level',
            argumentLabel: 'Level',
            argumentValue: 'None',
            form: 0,
            level: 16,
            method: 4,
            methodName: 'Level Up',
            slot: 0,
            species: 2
          }
        ],
        form: 0,
        formLabel: 'Base',
        genderRatio: 31,
        genderRatioLabel: '031 Male 87.5% / Female 12.5%',
        height: 7,
        learnset: [
          {
            level: 1,
            moveId: 33,
            moveName: 'Tackle',
            slot: 0
          },
          {
            level: 3,
            moveId: 45,
            moveName: 'Growl',
            slot: 1
          }
        ],
        name: 'Bulbasaur',
        personal: {
          baseFriendship: 70,
          canNotDynamax: false,
          catchRate: 45,
          color: 5,
          eggGroup1: 7,
          eggGroup2: 1,
          evYieldAttack: 0,
          evYieldDefense: 0,
          evYieldHP: 0,
          evYieldSpecialAttack: 1,
          evYieldSpecialDefense: 0,
          evYieldSpeed: 0,
          evolutionStage: 1,
          expGrowth: 4,
          form: 0,
          formCount: 1,
          formStatsIndex: 0,
          genderRatio: 31,
          hasSpriteForm: false,
          hatchedSpecies: 1,
          hatchCycles: 20,
          heldItem1: 0,
          heldItem2: 0,
          heldItem3: 0,
          isPresentInGame: true,
          isRegionalForm: false,
          localFormIndex: 0,
          modelId: 1,
          type1: 11,
          type2: 3
        },
        personalId: 1,
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/pml/personal/personal_total.bin',
          sourceLayer: 'base'
        },
        speciesId: 1,
        type1: 'Grass',
        type2: 'Poison',
        weight: 69
      },
      {
        abilities: {
          ability1: 66,
          ability1Label: 'Blaze',
          ability2: 0,
          ability2Label: 'None',
          hiddenAbility: 94,
          hiddenAbilityLabel: 'Solar Power'
        },
        baseExperience: 62,
        baseStats: {
          attack: 52,
          defense: 43,
          hp: 39,
          specialAttack: 60,
          specialDefense: 50,
          speed: 65,
          total: 309
        },
        catchRate: 45,
        compatibility: [
          {
            enabledCount: 1,
            entries: [
              {
                canLearn: true,
                label: 'TM00 Mega Punch',
                moveId: 5,
                moveName: 'Mega Punch',
                slot: 0
              }
            ],
            groupId: 'tm',
            label: 'TMs'
          }
        ],
        dexPresence: {
          armorDexIndex: 0,
          crownDexIndex: 0,
          isInAnyDex: true,
          isPresentInGame: true,
          regionalDexIndex: 4
        },
        evolutionStage: 1,
        evolutions: [],
        form: 0,
        formLabel: 'Base',
        genderRatio: 31,
        genderRatioLabel: '031 Male 87.5% / Female 12.5%',
        height: 6,
        learnset: [
          {
            level: 1,
            moveId: 10,
            moveName: 'Scratch',
            slot: 0
          }
        ],
        name: 'Charmander',
        personal: {
          baseFriendship: 70,
          canNotDynamax: false,
          catchRate: 45,
          color: 8,
          eggGroup1: 7,
          eggGroup2: 7,
          evYieldAttack: 0,
          evYieldDefense: 0,
          evYieldHP: 0,
          evYieldSpecialAttack: 0,
          evYieldSpecialDefense: 0,
          evYieldSpeed: 1,
          evolutionStage: 1,
          expGrowth: 4,
          form: 0,
          formCount: 1,
          formStatsIndex: 0,
          genderRatio: 31,
          hasSpriteForm: false,
          hatchedSpecies: 4,
          hatchCycles: 20,
          heldItem1: 0,
          heldItem2: 0,
          heldItem3: 0,
          isPresentInGame: true,
          isRegionalForm: false,
          localFormIndex: 0,
          modelId: 4,
          type1: 9,
          type2: 9
        },
        personalId: 4,
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/pml/personal/personal_total.bin',
          sourceLayer: 'base'
        },
        speciesId: 4,
        type1: 'Fire',
        type2: 'Fire',
        weight: 85
      }
    ],
    stats: {
      presentPokemonCount: 2,
      sourceFileCount: 5,
      totalEvolutionCount: 1,
      totalLearnsetMoveCount: 3,
      totalPokemonCount: 2
    },
    summary: pokemonWorkflowSummary
  };
  const movesWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Move stats, target behavior, secondary effects, flags, and source provenance.',
    diagnostics: [],
    id: 'moves',
    label: 'Moves'
  };
  const movesWorkflow: MovesWorkflow = {
    diagnostics: [],
    editableFields: [
      {
        field: 'power',
        label: 'Power',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'makesContact',
        label: 'Makes contact',
        maximumValue: 1,
        minimumValue: 0,
        options: [],
        valueKind: 'boolean'
      }
    ],
    moves: [
      {
        accuracy: 100,
        canUseMove: true,
        category: 1,
        categoryName: 'Physical',
        critStage: 0,
        description: 'A physical attack in which the user charges and slams into the target.',
        effectSequence: 12,
        flags: [
          {
            enabled: true,
            field: 'makesContact',
            label: 'Makes Contact'
          },
          {
            enabled: true,
            field: 'protect',
            label: 'Blocked By Protect'
          },
          {
            enabled: true,
            field: 'punch',
            label: 'Punch Move'
          }
        ],
        flinch: 0,
        hitMax: 1,
        hitMin: 1,
        inflict: 0,
        inflictName: 'None',
        inflictPercent: 0,
        maxMovePower: 90,
        moveId: 33,
        name: 'Tackle',
        power: 40,
        pp: 35,
        priority: 0,
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/pml/waza/waza_033.bin',
          sourceLayer: 'base'
        },
        quality: 2,
        rawHealing: 0,
        rawInflictCount: 0,
        recoil: 0,
        statChanges: [
          {
            percent: 0,
            slot: 1,
            stage: 0,
            stat: 0,
            statName: 'None'
          },
          {
            percent: 0,
            slot: 2,
            stage: 0,
            stat: 0,
            statName: 'None'
          },
          {
            percent: 0,
            slot: 3,
            stage: 0,
            stat: 0,
            statName: 'None'
          }
        ],
        target: 3,
        targetName: 'Opponent',
        turnMax: 0,
        turnMin: 0,
        type: 0,
        typeName: 'Normal',
        version: 1
      },
      {
        accuracy: 100,
        canUseMove: true,
        category: 2,
        categoryName: 'Special',
        critStage: 0,
        description: 'The target is attacked with small flames. This may also leave the target burned.',
        effectSequence: 22,
        flags: [
          {
            enabled: true,
            field: 'protect',
            label: 'Blocked By Protect'
          },
          {
            enabled: true,
            field: 'metronome',
            label: 'Callable By Metronome'
          }
        ],
        flinch: 0,
        hitMax: 1,
        hitMin: 1,
        inflict: 4,
        inflictName: 'Burn',
        inflictPercent: 10,
        maxMovePower: 90,
        moveId: 52,
        name: 'Ember',
        power: 40,
        pp: 25,
        priority: 0,
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/pml/waza/waza_052.bin',
          sourceLayer: 'base'
        },
        quality: 2,
        rawHealing: 0,
        rawInflictCount: 1,
        recoil: 0,
        statChanges: [
          {
            percent: 0,
            slot: 1,
            stage: 0,
            stat: 0,
            statName: 'None'
          },
          {
            percent: 0,
            slot: 2,
            stage: 0,
            stat: 0,
            statName: 'None'
          },
          {
            percent: 0,
            slot: 3,
            stage: 0,
            stat: 0,
            statName: 'None'
          }
        ],
        target: 3,
        targetName: 'Opponent',
        turnMax: 0,
        turnMin: 0,
        type: 9,
        typeName: 'Fire',
        version: 1
      }
    ],
    stats: {
      activeFlagCount: 5,
      enabledMoveCount: 2,
      sourceFileCount: 4,
      totalMoveCount: 2
    },
    summary: movesWorkflowSummary
  };
  const textWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Text entries, dialogue references, and source provenance.',
    diagnostics: [],
    id: 'text',
    label: 'Text and Dialogue Map'
  };
  const textWorkflow: TextWorkflow = {
    diagnostics: [],
    dialogueReferences: [
      {
        context: 'common/story.dat',
        dialogueId: 'common/story:0',
        label: 'story #0',
        preview: 'Welcome to the lab.',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/message/English/common/story.dat',
          sourceLayer: 'base'
        },
        textId: 0
      }
    ],
    editableFields: [
      {
        field: 'value',
        label: 'Text value',
        maximumLength: 4096,
        minimumLength: 0,
        valueKind: 'multilineText'
      }
    ],
    entries: [
      {
        canEdit: true,
        editBlockedReason: null,
        label: 'story #0',
        language: 'English',
        lineIndex: 0,
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/message/English/common/story.dat',
          sourceLayer: 'base'
        },
        sourceFile: 'romfs/bin/message/English/common/story.dat',
        textId: 0,
        textKey: 'romfs/bin/message/English/common/story.dat#0',
        value: 'Welcome to the lab.'
      }
    ],
    stats: {
      dialogueReferenceCount: 1,
      sourceFileCount: 1,
      totalTextEntryCount: 1
    },
    summary: textWorkflowSummary
  };
  const trainersWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Trainer parties, classes, battle types, and source provenance.',
    diagnostics: [],
    id: 'trainers',
    label: 'Trainers'
  };
  const trainersWorkflow: TrainersWorkflow = {
    diagnostics: [],
    editableFields: [
      {
        field: 'trainerClassId',
        label: 'Trainer class ID',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '005 Pokemon Trainer', value: 5 },
          { label: '006 Gym Leader', value: 6 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'classBallId',
        label: 'Class ball',
        maximumValue: 26,
        minimumValue: 0,
        options: [
          { label: '0 None', value: 0 },
          { label: '3 Great Ball', value: 3 },
          { label: '4 Poke Ball', value: 4 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'battleType',
        label: 'Battle type',
        maximumValue: 1,
        minimumValue: 0,
        options: [
          { label: '0 Singles', value: 0 },
          { label: '1 Doubles', value: 1 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'trainerItem1Id',
        label: 'Trainer item 1 ID',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Potion', value: 1 },
          { label: '002 Antidote', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'trainerItem2Id',
        label: 'Trainer item 2 ID',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Potion', value: 1 },
          { label: '002 Antidote', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'trainerItem3Id',
        label: 'Trainer item 3 ID',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Potion', value: 1 },
          { label: '002 Antidote', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'trainerItem4Id',
        label: 'Trainer item 4 ID',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Potion', value: 1 },
          { label: '002 Antidote', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'aiFlags',
        label: 'AI flags',
        maximumValue: 8191,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'heal',
        label: 'Heal flag',
        maximumValue: 1,
        minimumValue: 0,
        options: [
          { label: 'No', value: 0 },
          { label: 'Yes', value: 1 }
        ],
        valueKind: 'boolean'
      },
      {
        field: 'money',
        label: 'Prize money',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'gift',
        label: 'Gift ID',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Potion', value: 1 },
          { label: '007 Rare Candy', value: 7 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'speciesId',
        label: 'Species ID',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '810 Grookey', value: 810 },
          { label: '821 Rookidee', value: 821 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'level',
        label: 'Level',
        maximumValue: 100,
        minimumValue: 1,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'heldItemId',
        label: 'Held item ID',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Potion', value: 1 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'move1Id',
        label: 'Move 1 ID',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Scratch', value: 1 },
          { label: '002 Growl', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'move2Id',
        label: 'Move 2 ID',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Scratch', value: 1 },
          { label: '002 Growl', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'move3Id',
        label: 'Move 3 ID',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Scratch', value: 1 },
          { label: '002 Growl', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'move4Id',
        label: 'Move 4 ID',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Scratch', value: 1 },
          { label: '002 Growl', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'gender',
        label: 'Gender',
        maximumValue: 3,
        minimumValue: 0,
        options: [
          { label: 'Random', value: 0 },
          { label: 'Male', value: 1 },
          { label: 'Female', value: 2 },
          { label: 'Genderless', value: 3 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'ability',
        label: 'Ability',
        maximumValue: 3,
        minimumValue: 0,
        options: [
          { label: 'Default', value: 0 },
          { label: 'Ability 1', value: 1 },
          { label: 'Ability 2', value: 2 },
          { label: 'Hidden Ability', value: 3 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'nature',
        label: 'Nature',
        maximumValue: 24,
        minimumValue: 0,
        options: [
          { label: 'Hardy', value: 0 },
          { label: 'Adamant (+Atk/-Sp.Atk)', value: 3 },
          { label: 'Serious', value: 12 },
          { label: 'Jolly (+Spe/-Sp.Atk)', value: 13 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'dynamaxLevel',
        label: 'Dynamax level',
        maximumValue: 10,
        minimumValue: 0,
        options: [
          { label: '0', value: 0 },
          { label: '7', value: 7 },
          { label: '10', value: 10 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'canGigantamax',
        label: 'Can Gigantamax',
        maximumValue: 1,
        minimumValue: 0,
        options: [
          { label: 'No', value: 0 },
          { label: 'Yes', value: 1 }
        ],
        valueKind: 'boolean'
      },
      {
        field: 'shiny',
        label: 'Shiny',
        maximumValue: 1,
        minimumValue: 0,
        options: [
          { label: 'No', value: 0 },
          { label: 'Yes', value: 1 }
        ],
        valueKind: 'boolean'
      },
      {
        field: 'canDynamax',
        label: 'Can Dynamax',
        maximumValue: 1,
        minimumValue: 0,
        options: [
          { label: 'No', value: 0 },
          { label: 'Yes', value: 1 }
        ],
        valueKind: 'boolean'
      }
    ],
    stats: {
      sourceFileCount: 2,
      totalPokemonCount: 1,
      totalTrainerCount: 1
    },
    summary: trainersWorkflowSummary,
    trainers: [
      {
        aiFlags: 77,
        aiFlagStates: [
          { bit: 0, description: 'Enables basic battle decision logic.', enabled: true, label: 'Basic', mask: 1 },
          { bit: 1, description: 'Enables stronger move and target choices.', enabled: false, label: 'Strong', mask: 2 },
          { bit: 2, description: 'Enables expert battle decision logic.', enabled: true, label: 'Expert', mask: 4 },
          { bit: 3, description: 'Enables double-battle-aware decision logic.', enabled: true, label: 'Double', mask: 8 },
          { bit: 4, description: 'Enables raid-battle-specific decision logic.', enabled: false, label: 'Raid', mask: 16 },
          { bit: 5, description: 'Allows additional AI-controlled action checks.', enabled: false, label: 'Allowance', mask: 32 },
          { bit: 6, description: 'Allows AI-driven Pokemon switching.', enabled: true, label: 'PokeChange', mask: 64 },
          { bit: 7, description: 'Enables the first Fire Gym behavior bit.', enabled: false, label: 'Fire Gym (1)', mask: 128 },
          { bit: 8, description: 'Enables the second Fire Gym behavior bit.', enabled: false, label: 'Fire Gym (2)', mask: 256 },
          { bit: 9, description: 'Reserved trainer AI bit.', enabled: false, label: 'Unused 1', mask: 512 },
          { bit: 10, description: 'Allows AI-driven trainer item usage.', enabled: false, label: 'Item', mask: 1024 },
          { bit: 11, description: 'Enables the third Fire Gym behavior bit.', enabled: false, label: 'Fire Gym (3)', mask: 2048 },
          { bit: 12, description: 'Reserved trainer AI bit.', enabled: false, label: 'Unused 2', mask: 4096 }
        ],
        battleType: 'Doubles',
        battleTypeValue: 1,
        canEditClassBall: true,
        classBall: '4 Poke Ball',
        classBallId: 4,
        classBallScope: 'Unique trainer class: Avery',
        gift: 7,
        heal: true,
        itemIds: [1, 2, 0, 0],
        items: ['Potion', 'Antidote', 'None', 'None'],
        location: 'Trainer 10',
        money: 24,
        name: 'Avery',
        provenance: {
          classFileState: 'baseOnly',
          classSourceFile: 'romfs/bin/trainer/trainer_type/trainer_type_005.bin',
          classSourceLayer: 'base',
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/trainer/trainer_data/trainer_010.bin',
          sourceLayer: 'base',
          teamFileState: 'baseOnly',
          teamSourceFile: 'romfs/bin/trainer/trainer_poke/trainer_010.bin',
          teamSourceLayer: 'base'
        },
        team: [
          {
            ability: 2,
            abilityLabel: 'Ability 2',
            abilityOptions: [
              { label: 'Default - 065 Overgrow', value: 0 },
              { label: 'Ability 1 - 065 Overgrow', value: 1 },
              { label: 'Ability 2 - 065 Overgrow', value: 2 },
              { label: 'Hidden Ability - 000 None', value: 3 }
            ],
            canDynamax: false,
            canGigantamax: true,
            dynamaxLevel: 7,
            evs: {
              attack: 20,
              defense: 30,
              hp: 10,
              specialAttack: 40,
              specialDefense: 50,
              speed: 60
            },
            form: 0,
            gender: 1,
            genderLabel: 'Male',
            heldItem: 'Potion',
            heldItemId: 1,
            ivs: {
              attack: 2,
              defense: 3,
              hp: 1,
              specialAttack: 5,
              specialDefense: 6,
              speed: 4
            },
            level: 12,
            moveIds: [1, 2, 0, 0],
            moves: ['Scratch', 'Growl', 'None', 'None'],
            nature: 13,
            natureLabel: 'Jolly (+Spe/-Sp.Atk)',
            shiny: true,
            slot: 1,
            species: 'Grookey',
            speciesId: 810
          }
        ],
        trainerClass: 'Pokemon Trainer',
        trainerClassId: 5,
        trainerId: 10
      }
    ]
  };
  const giftPokemonWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Scripted gift Pokemon records, IV modes, items, moves, and source provenance.',
    diagnostics: [],
    id: 'giftPokemon',
    label: 'Gift Pokemon'
  };
  const giftPokemonWorkflow: GiftPokemonWorkflow = {
    diagnostics: [],
    editableFields: [
      {
        field: 'species',
        label: 'Species',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '001 Bulbasaur', value: 1 },
          { label: '810 Grookey', value: 810 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'form',
        label: 'Form',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'level',
        label: 'Level',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'heldItemId',
        label: 'Held item',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Potion', value: 1 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'ballItemId',
        label: 'Ball item',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '004 Poke Ball', value: 4 },
          { label: '003 Great Ball', value: 3 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'ability',
        label: 'Ability slot',
        maximumValue: 3,
        minimumValue: 0,
        options: [
          { label: 'Default', value: 0 },
          { label: 'Ability 1', value: 1 },
          { label: 'Hidden Ability', value: 3 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'nature',
        label: 'Nature',
        maximumValue: 25,
        minimumValue: 0,
        options: [
          { label: 'Hardy', value: 0 },
          { label: 'Adamant (+Atk/-Sp.Atk)', value: 3 },
          { label: 'Random', value: 25 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'gender',
        label: 'Gender',
        maximumValue: 255,
        minimumValue: 0,
        options: [
          { label: 'Random', value: 0 },
          { label: 'Male', value: 1 },
          { label: 'Female', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'shinyLock',
        label: 'Shiny lock',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: 'Random', value: 0 },
          { label: 'Always Shiny', value: 1 },
          { label: 'Never Shiny', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'dynamaxLevel',
        label: 'Dynamax level',
        maximumValue: 10,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'canGigantamax',
        label: 'Can Gigantamax',
        maximumValue: 1,
        minimumValue: 0,
        options: [
          { label: 'No', value: 0 },
          { label: 'Yes', value: 1 }
        ],
        valueKind: 'boolean'
      },
      {
        field: 'specialMoveId',
        label: 'Special Move',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Scratch', value: 1 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'ivHp',
        label: 'HP IV',
        maximumValue: 31,
        minimumValue: -4,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'ivAttack',
        label: 'Attack IV',
        maximumValue: 31,
        minimumValue: -1,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'ivDefense',
        label: 'Defense IV',
        maximumValue: 31,
        minimumValue: -1,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'ivSpecialAttack',
        label: 'Sp. Atk IV',
        maximumValue: 31,
        minimumValue: -1,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'ivSpecialDefense',
        label: 'Sp. Def IV',
        maximumValue: 31,
        minimumValue: -1,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'ivSpeed',
        label: 'Speed IV',
        maximumValue: 31,
        minimumValue: -1,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'flawlessIvCount',
        label: 'IV preset',
        maximumValue: 6,
        minimumValue: 0,
        options: [
          { label: 'Random IVs', value: 0 },
          { label: '3 Guaranteed Perfect IVs', value: 3 },
          { label: '6 Guaranteed Perfect IVs', value: 6 }
        ],
        valueKind: 'integer'
      }
    ],
    gifts: [
      {
        ability: 1,
        abilityLabel: 'Ability 1',
        abilityOptions: [
          { label: 'Default - 065 Overgrow', value: 0 },
          { label: 'Ability 1 - 065 Overgrow', value: 1 },
          { label: 'Ability 2 - 065 Overgrow', value: 2 },
          { label: 'Hidden Ability - 000 None', value: 3 }
        ],
        ballItem: 'Poke Ball',
        ballItemId: 4,
        canGigantamax: false,
        dynamaxLevel: 0,
        flawlessIvCount: 3,
        form: 0,
        gender: 0,
        genderLabel: 'Random',
        giftIndex: 0,
        heldItem: null,
        heldItemId: 0,
        isEgg: false,
        ivs: {
          attack: -1,
          defense: -1,
          hp: -4,
          specialAttack: -1,
          specialDefense: -1,
          speed: -1
        },
        ivSummary: '3 guaranteed perfect IVs',
        label: 'Gift 001: Bulbasaur Lv. 5',
        level: 5,
        nature: 25,
        natureLabel: 'Random',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/script_event_data/add_poke.bin',
          sourceLayer: 'base'
        },
        shinyLock: 1,
        shinyLockLabel: 'Never Shiny',
        specialMove: null,
        specialMoveId: 0,
        species: 'Bulbasaur',
        speciesId: 1
      }
    ],
    stats: {
      eggGiftCount: 0,
      fixedIvGiftCount: 0,
      sourceFileCount: 1,
      totalGiftCount: 1
    },
    summary: giftPokemonWorkflowSummary
  };
  const tradePokemonWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'In-game trade records, requested Pokemon, IV modes, relearn moves, and source provenance.',
    diagnostics: [],
    id: 'tradePokemon',
    label: 'Trade Pokemon'
  };
  const tradePokemonWorkflow: TradePokemonWorkflow = {
    diagnostics: [],
    editableFields: [
      {
        field: 'species',
        label: 'Species',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '133 Eevee', value: 133 },
          { label: '810 Grookey', value: 810 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'form',
        label: 'Form',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'level',
        label: 'Level',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'heldItemId',
        label: 'Held item',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Potion', value: 1 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'ballItemId',
        label: 'Ball item',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '004 Poke Ball', value: 4 },
          { label: '003 Great Ball', value: 3 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'field03',
        label: 'Unknown field 03',
        maximumValue: 65535,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'ability',
        label: 'Ability slot',
        maximumValue: 3,
        minimumValue: 0,
        options: [
          { label: 'Default', value: 0 },
          { label: 'Ability 1', value: 1 },
          { label: 'Hidden Ability', value: 3 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'nature',
        label: 'Nature',
        maximumValue: 25,
        minimumValue: 0,
        options: [
          { label: 'Hardy', value: 0 },
          { label: 'Adamant (+Atk/-Sp.Atk)', value: 3 },
          { label: 'Random', value: 25 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'gender',
        label: 'Gender',
        maximumValue: 2,
        minimumValue: 0,
        options: [
          { label: 'Random', value: 0 },
          { label: 'Male', value: 1 },
          { label: 'Female', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'shinyLock',
        label: 'Shiny lock',
        maximumValue: 2,
        minimumValue: 0,
        options: [
          { label: 'Random', value: 0 },
          { label: 'Never Shiny', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'dynamaxLevel',
        label: 'Dynamax level',
        maximumValue: 10,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'canGigantamax',
        label: 'Can Gigantamax',
        maximumValue: 1,
        minimumValue: 0,
        options: [
          { label: 'No', value: 0 },
          { label: 'Yes', value: 1 }
        ],
        valueKind: 'boolean'
      },
      {
        field: 'requiredSpecies',
        label: 'Requested species',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '133 Eevee', value: 133 },
          { label: '810 Grookey', value: 810 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'requiredForm',
        label: 'Requested form',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'requiredNature',
        label: 'Requested nature',
        maximumValue: 25,
        minimumValue: 0,
        options: [
          { label: 'Hardy', value: 0 },
          { label: 'Adamant (+Atk/-Sp.Atk)', value: 3 },
          { label: 'Random', value: 25 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'unknownRequirement',
        label: 'Unknown requirement',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'trainerId',
        label: 'Trainer ID',
        maximumValue: 65535,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'otGender',
        label: 'OT gender',
        maximumValue: 1,
        minimumValue: 0,
        options: [
          { label: 'Male', value: 0 },
          { label: 'Female', value: 1 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'memoryCode',
        label: 'Memory code',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'memoryTextVariable',
        label: 'Memory text variable',
        maximumValue: 65535,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'memoryFeel',
        label: 'Memory feeling',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'memoryIntensity',
        label: 'Memory intensity',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'relearnMove0',
        label: 'Relearn move 1',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Scratch', value: 1 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'ivHp',
        label: 'HP IV',
        maximumValue: 31,
        minimumValue: -4,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'ivAttack',
        label: 'Attack IV',
        maximumValue: 31,
        minimumValue: -1,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'ivDefense',
        label: 'Defense IV',
        maximumValue: 31,
        minimumValue: -1,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'ivSpecialAttack',
        label: 'Sp. Atk IV',
        maximumValue: 31,
        minimumValue: -1,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'ivSpecialDefense',
        label: 'Sp. Def IV',
        maximumValue: 31,
        minimumValue: -1,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'ivSpeed',
        label: 'Speed IV',
        maximumValue: 31,
        minimumValue: -1,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'flawlessIvCount',
        label: 'IV preset',
        maximumValue: 6,
        minimumValue: 0,
        options: [
          { label: 'Random IVs', value: 0 },
          { label: '3 Guaranteed Perfect IVs', value: 3 },
          { label: '6 Guaranteed Perfect IVs', value: 6 }
        ],
        valueKind: 'integer'
      }
    ],
    stats: {
      fixedIvTradeCount: 0,
      sourceFileCount: 1,
      totalTradeCount: 1
    },
    summary: tradePokemonWorkflowSummary,
    trades: [
      {
        ability: 1,
        abilityLabel: 'Ability 1',
        abilityOptions: [],
        ballItem: 'Poke Ball',
        ballItemId: 4,
        canGigantamax: false,
        dynamaxLevel: 0,
        field03: 7,
        flawlessIvCount: 3,
        form: 1,
        gender: 0,
        genderLabel: 'Random',
        hash0: '0x0000000000000001',
        hash1: '0x0000000000000002',
        hash2: '0x0000000000000003',
        heldItem: null,
        heldItemId: 0,
        ivs: {
          attack: -1,
          defense: -1,
          hp: -4,
          specialAttack: -1,
          specialDefense: -1,
          speed: -1
        },
        ivSummary: '3 guaranteed perfect IVs',
        label: 'Trade 001: Meowth (Galarian) -> Farfetch’d (Galarian) Lv. 15',
        level: 15,
        memoryCode: 12,
        memoryFeel: 3,
        memoryIntensity: 4,
        memoryTextVariable: 99,
        nature: 25,
        natureLabel: 'Random',
        otGender: 0,
        otGenderLabel: 'Male',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/script_event_data/field_trade.bin',
          sourceLayer: 'base'
        },
        relearnMoves: [
          { move: 'Scratch', moveId: 1, slot: 0 },
          { move: null, moveId: 0, slot: 1 },
          { move: null, moveId: 0, slot: 2 },
          { move: null, moveId: 0, slot: 3 }
        ],
        requiredForm: 2,
        requiredNature: 25,
        requiredNatureLabel: 'Random',
        requiredSpecies: 'Meowth',
        requiredSpeciesId: 52,
        shinyLock: 2,
        shinyLockLabel: 'Never Shiny',
        species: 'Farfetch’d',
        speciesId: 83,
        tradeIndex: 0,
        trainerId: 12345,
        unknownRequirement: 0
      }
    ]
  };
  const staticEncountersWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Scripted overworld and story encounter records, IV modes, moves, rules, and source provenance.',
    diagnostics: [],
    id: 'staticEncounters',
    label: 'Static Encounters'
  };
  const staticEncountersWorkflow: StaticEncountersWorkflow = {
    diagnostics: [],
    editableFields: [
      {
        field: 'species',
        label: 'Species',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '001 Bulbasaur', value: 1 },
          { label: '810 Grookey', value: 810 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'form',
        label: 'Form',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'level',
        label: 'Level',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'heldItemId',
        label: 'Held item',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Potion', value: 1 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'ability',
        label: 'Ability slot',
        maximumValue: 3,
        minimumValue: 0,
        options: [
          { label: 'Default', value: 0 },
          { label: 'Ability 1', value: 1 },
          { label: 'Hidden Ability', value: 3 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'nature',
        label: 'Nature',
        maximumValue: 25,
        minimumValue: 0,
        options: [
          { label: 'Hardy', value: 0 },
          { label: 'Adamant (+Atk/-Sp.Atk)', value: 3 },
          { label: 'Random', value: 25 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'gender',
        label: 'Gender',
        maximumValue: 2,
        minimumValue: 0,
        options: [
          { label: 'Random', value: 0 },
          { label: 'Male', value: 1 },
          { label: 'Female', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'shinyLock',
        label: 'Shiny lock',
        maximumValue: 2,
        minimumValue: 0,
        options: [
          { label: 'Random', value: 0 },
          { label: 'Never Shiny', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'encounterScenario',
        label: 'Scenario',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: 'None', value: 0 },
          { label: 'Calyrex', value: 17 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'dynamaxLevel',
        label: 'Dynamax level',
        maximumValue: 10,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'canGigantamax',
        label: 'Can Gigantamax',
        maximumValue: 1,
        minimumValue: 0,
        options: [
          { label: 'No', value: 0 },
          { label: 'Yes', value: 1 }
        ],
        valueKind: 'boolean'
      },
      {
        field: 'move0Id',
        label: 'Move 1',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Scratch', value: 1 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'ivHp',
        label: 'HP IV',
        maximumValue: 31,
        minimumValue: -4,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'ivAttack',
        label: 'Attack IV',
        maximumValue: 31,
        minimumValue: -1,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'flawlessIvCount',
        label: 'IV preset',
        maximumValue: 6,
        minimumValue: 0,
        options: [
          { label: 'Random IVs', value: 0 },
          { label: '3 Guaranteed Perfect IVs', value: 3 },
          { label: '6 Guaranteed Perfect IVs', value: 6 }
        ],
        valueKind: 'integer'
      }
    ],
    encounters: [
      {
        ability: 3,
        abilityLabel: 'Hidden Ability',
        abilityOptions: [],
        canGigantamax: true,
        dynamaxLevel: 10,
        encounterId: '0x0102030405060708',
        encounterIndex: 0,
        encounterScenario: 17,
        encounterScenarioLabel: 'Calyrex',
        evs: {
          attack: 2,
          defense: 3,
          hp: 1,
          specialAttack: 4,
          specialDefense: 5,
          speed: 6
        },
        flawlessIvCount: null,
        form: 1,
        gender: 1,
        genderLabel: 'Male',
        heldItem: 'Potion',
        heldItemId: 1,
        ivs: {
          attack: 30,
          defense: 29,
          hp: 31,
          specialAttack: 27,
          specialDefense: 26,
          speed: 28
        },
        ivSummary: 'HP 31 / Atk 30 / Def 29 / SpA 27 / SpD 26 / Spe 28',
        label: 'Static 001: Grookey (Form 1) Lv. 50 | Calyrex',
        level: 50,
        moves: [
          { move: 'Scratch', moveId: 1, slot: 0 },
          { move: 'Growl', moveId: 2, slot: 1 },
          { move: null, moveId: 0, slot: 2 },
          { move: null, moveId: 0, slot: 3 }
        ],
        nature: 25,
        natureLabel: 'Random',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/script_event_data/event_encount_data.bin',
          sourceLayer: 'base'
        },
        shinyLock: 2,
        shinyLockLabel: 'Never Shiny',
        species: 'Grookey',
        speciesId: 810
      }
    ],
    stats: {
      fixedIvEncounterCount: 1,
      gigantamaxEncounterCount: 1,
      sourceFileCount: 1,
      totalEncounterCount: 1
    },
    summary: staticEncountersWorkflowSummary
  };
  const rentalPokemonWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Rental Pokemon records, fixed IVs, EVs, items, moves, and source provenance.',
    diagnostics: [],
    id: 'rentalPokemon',
    label: 'Rental Pokemon'
  };
  const rentalPokemonWorkflow: RentalPokemonWorkflow = {
    diagnostics: [],
    editableFields: [
      {
        field: 'species',
        label: 'Species',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '001 Bulbasaur', value: 1 },
          { label: '810 Grookey', value: 810 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'form',
        label: 'Form',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'level',
        label: 'Level',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'heldItemId',
        label: 'Held item',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Potion', value: 1 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'ballItemId',
        label: 'Ball',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '004 Poke Ball', value: 4 },
          { label: '005 Great Ball', value: 5 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'ability',
        label: 'Ability slot',
        maximumValue: 3,
        minimumValue: 0,
        options: [
          { label: 'Default', value: 0 },
          { label: 'Ability 1', value: 1 },
          { label: 'Hidden Ability', value: 3 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'nature',
        label: 'Nature',
        maximumValue: 24,
        minimumValue: 0,
        options: [
          { label: 'Hardy', value: 0 },
          { label: 'Adamant (+Atk/-Sp.Atk)', value: 3 },
          { label: 'Jolly (+Spe/-Sp.Atk)', value: 13 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'gender',
        label: 'Gender',
        maximumValue: 2,
        minimumValue: 0,
        options: [
          { label: 'Random', value: 0 },
          { label: 'Male', value: 1 },
          { label: 'Female', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'trainerId',
        label: 'Trainer ID',
        maximumValue: 65535,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'move0Id',
        label: 'Move 1',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Scratch', value: 1 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'evHp',
        label: 'HP EV',
        maximumValue: 252,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'ivHp',
        label: 'HP IV',
        maximumValue: 31,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'fixedIvPreset',
        label: 'IV preset',
        maximumValue: 31,
        minimumValue: 0,
        options: [
          { label: '0 IVs', value: 0 },
          { label: '6 Guaranteed Perfect IVs', value: 31 }
        ],
        valueKind: 'integer'
      }
    ],
    rentals: [
      {
        ability: 1,
        abilityLabel: 'Ability 1',
        abilityOptions: [],
        ballItem: 'Poke Ball',
        ballItemId: 4,
        evs: {
          attack: 252,
          defense: 0,
          hp: 4,
          specialAttack: 0,
          specialDefense: 0,
          speed: 252
        },
        form: 0,
        gender: 1,
        genderLabel: 'Male',
        hash1: '0x0000000000000010',
        hash2: '0x0000000000000020',
        hasPerfectIvs: true,
        heldItem: 'Potion',
        heldItemId: 1,
        ivs: {
          attack: 31,
          defense: 31,
          hp: 31,
          specialAttack: 31,
          specialDefense: 31,
          speed: 31
        },
        ivSummary: 'HP 31 / Atk 31 / Def 31 / SpA 31 / SpD 31 / Spe 31',
        label: 'Rental 001: Grookey Lv. 50',
        level: 50,
        moves: [
          { move: 'Scratch', moveId: 1, slot: 0 },
          { move: null, moveId: 0, slot: 1 },
          { move: null, moveId: 0, slot: 2 },
          { move: null, moveId: 0, slot: 3 }
        ],
        nature: 0,
        natureLabel: 'Hardy',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/script_event_data/rental.bin',
          sourceLayer: 'base'
        },
        rentalIndex: 0,
        species: 'Grookey',
        speciesId: 810,
        trainerId: 12345
      }
    ],
    stats: {
      perfectIvRentalCount: 1,
      sourceFileCount: 1,
      totalRentalCount: 1
    },
    summary: rentalPokemonWorkflowSummary
  };
  const dynamaxAdventuresWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description:
      'Advanced Adventure Pokemon editor that updates the loose table and matching ExeFS mirrors for safe Pokemon fields.',
    diagnostics: [],
    id: 'dynamaxAdventures',
    label: 'Dynamax Adventures'
  };
  const dynamaxAdventuresWorkflow: DynamaxAdventuresWorkflow = {
    diagnostics: [],
    editableFields: [
      {
        field: 'species',
        label: 'Species',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '001 Bulbasaur', value: 1 },
          { label: '810 Grookey', value: 810 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'form',
        label: 'Form',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'level',
        label: 'Level',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'ballItemId',
        label: 'Ball item',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '004 Poke Ball', value: 4 },
          { label: '005 Great Ball', value: 5 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'ability',
        label: 'Ability roll',
        maximumValue: 4,
        minimumValue: 0,
        options: [
          { label: 'Ability 1', value: 0 },
          { label: 'Hidden Ability', value: 2 },
          { label: 'Any Ability', value: 4 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'gigantamaxState',
        label: 'Gigantamax state',
        maximumValue: 2,
        minimumValue: 0,
        options: [
          { label: 'Normal', value: 1 },
          { label: 'Gigantamax', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'version',
        label: 'Game version',
        maximumValue: 2,
        minimumValue: 0,
        options: [
          { label: 'Both', value: 0 },
          { label: 'Sword', value: 1 },
          { label: 'Shield', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'shinyRoll',
        label: 'Shiny roll',
        maximumValue: 2,
        minimumValue: 0,
        options: [
          { label: 'Enabled', value: 1 },
          { label: 'Disabled', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'move0Id',
        label: 'Move 1',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Scratch', value: 1 },
          { label: '002 Growl', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'guaranteedPerfectIvs',
        label: 'Guaranteed perfect IVs',
        maximumValue: 6,
        minimumValue: 0,
        options: [
          { label: 'Random IVs', value: 0 },
          { label: '5 Guaranteed Perfect IVs', value: 5 },
          { label: '6 Guaranteed Perfect IVs', value: 6 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'ivAttack',
        label: 'Attack IV override',
        maximumValue: 31,
        minimumValue: -1,
        options: [
          { label: 'Random', value: -1 },
          { label: '0 IV', value: 0 },
          { label: '31 IV', value: 31 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'isSingleCapture',
        label: 'Single-capture Pokemon',
        maximumValue: 1,
        minimumValue: 0,
        options: [
          { label: 'No', value: 0 },
          { label: 'Yes', value: 1 }
        ],
        valueKind: 'integer'
      }
    ],
    encounters: [
      {
        ability: 0,
        abilityLabel: 'Ability 1',
        abilityOptions: [],
        adventureIndex: 0,
        ballItem: 'Poke Ball',
        ballItemId: 4,
        entryIndex: 0,
        form: 0,
        gigantamaxLabel: 'Normal',
        gigantamaxState: 1,
        guaranteedPerfectIvs: 5,
        isSingleCapture: true,
        isStoryProgressGated: false,
        ivs: {
          attack: -1,
          defense: -1,
          hp: -5,
          specialAttack: -1,
          specialDefense: -1,
          speed: -1
        },
        ivSummary: '5 guaranteed perfect / Atk Random / Def Random / SpA Random / SpD Random / Spe Random',
        label: 'Adventure 001: Grookey Lv. 65',
        level: 65,
        moves: [
          { move: 'Scratch', moveId: 1, slot: 0 },
          { move: 'Growl', moveId: 2, slot: 1 },
          { move: 'None', moveId: 0, slot: 2 },
          { move: 'None', moveId: 0, slot: 3 }
        ],
        otGender: 0,
        otGenderLabel: 'Male',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/appli/chika/data_table/underground_exploration_poke.bin',
          sourceLayer: 'base'
        },
        shinyRoll: 1,
        shinyRollLabel: 'Enabled',
        singleCaptureFlagBlock: '0x0000000000000010',
        species: 'Grookey',
        speciesId: 810,
        uiMessageId: '0x0000000000000020',
        vanillaPokemon: {
          ability: 0,
          abilityLabel: 'Ability 1',
          form: 0,
          gigantamaxLabel: 'Normal',
          gigantamaxState: 1,
          guaranteedPerfectIvs: 6,
          ivs: {
            attack: 31,
            defense: -1,
            hp: -5,
            specialAttack: -1,
            specialDefense: -1,
            speed: -1
          },
          ivSummary:
            '6 guaranteed perfect / Atk 31 / Def Random / SpA Random / SpD Random / Spe Random',
          level: 60,
          moves: [
            { move: 'Growl', moveId: 2, slot: 0 },
            { move: 'None', moveId: 0, slot: 1 },
            { move: 'None', moveId: 0, slot: 2 },
            { move: 'None', moveId: 0, slot: 3 }
          ],
          species: 'Bulbasaur',
          speciesId: 1
        },
        version: 0,
        versionLabel: 'Both'
      }
    ],
    stats: {
      guaranteedPerfectIvEncounterCount: 1,
      singleCaptureCount: 1,
      sourceFileCount: 1,
      storyGatedCount: 0,
      totalEncounterCount: 1
    },
    summary: dynamaxAdventuresWorkflowSummary
  };
  const shopsWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Shop inventories, item metadata, and source provenance.',
    diagnostics: [],
    id: 'shops',
    label: 'Shops'
  };
  const shopsWorkflow: ShopsWorkflow = {
    diagnostics: [],
    editableFields: [
      {
        field: 'itemId',
        label: 'Item',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { itemName: 'Potion', label: '0001 Potion (Medicine)', price: 300, value: 1 },
          { itemName: 'Antidote', label: '0002 Antidote (Medicine)', price: 200, value: 2 },
          { itemName: 'None', label: '0000 None (Medicine)', price: 0, value: 0 }
        ],
        valueKind: 'integer'
      }
    ],
    shops: [
      {
        currency: 'Money',
        inventory: [
          {
            isKnownItem: true,
            itemId: 1,
            itemName: 'Potion',
            price: 300,
            slot: 1,
            stockLimit: null
          },
          {
            isKnownItem: true,
            itemId: 2,
            itemName: 'Antidote',
            price: 200,
            slot: 2,
            stockLimit: null
          }
        ],
        inventoryCount: 1,
        inventoryIndex: 1,
        inventoryLabel: 'Inventory',
        inventorySummary: 'Potion, Antidote',
        kind: 'Single',
        location: 'Poke Mart',
        name: 'Poke Mart',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/appli/shop/bin/shop_data.bin',
          sourceLayer: 'base'
        },
        shopId: 'single:1F3FF031A3A24490',
        sourceHash: '0x1F3FF031A3A24490'
      }
    ],
    stats: {
      sourceFileCount: 1,
      totalInventoryItemCount: 2,
      totalShopCount: 1
    },
    summary: shopsWorkflowSummary
  };
  const encountersWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Encounter tables, wild slots, levels, weather, and source provenance.',
    diagnostics: [],
    id: 'encounters',
    label: 'Wild Encounters'
  };
  const encountersWorkflow: EncountersWorkflow = {
    diagnostics: [],
    editableFields: [
      {
        field: 'speciesId',
        label: 'Species ID',
        maximumValue: 65535,
        minimumValue: 0,
        valueKind: 'integer'
      },
      {
        field: 'form',
        label: 'Form',
        maximumValue: 255,
        minimumValue: 0,
        valueKind: 'integer'
      },
      {
        field: 'probability',
        label: 'Probability',
        maximumValue: 100,
        minimumValue: 0,
        valueKind: 'integer'
      },
      {
        field: 'levelMin',
        label: 'Min Level',
        maximumValue: 100,
        minimumValue: 0,
        valueKind: 'integer'
      },
      {
        field: 'levelMax',
        label: 'Max Level',
        maximumValue: 100,
        minimumValue: 0,
        valueKind: 'integer'
      }
    ],
    stats: {
      sourceFileCount: 1,
      totalSlotCount: 2,
      totalTableCount: 1
    },
    summary: encountersWorkflowSummary,
    tables: [
      {
        archiveMember: 'encount_symbol_k.bin',
        area: 'Symbol',
        encounterType: 'Normal',
        gameVersion: 'Sword',
        location: 'Zone 0x1122334455667788',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/archive/field/resident/data_table.gfpak',
          sourceLayer: 'base'
        },
        slots: [
          {
            form: 0,
            levelMax: 8,
            levelMin: 3,
            slot: 1,
            speciesId: 1,
            species: 'Bulbasaur',
            timeOfDay: null,
            weather: 'Normal',
            weight: 35
          },
          {
            form: 1,
            levelMax: 8,
            levelMin: 3,
            slot: 2,
            speciesId: 4,
            species: 'Charmander',
            timeOfDay: null,
            weather: 'Normal',
            weight: 15
          }
        ],
        tableId: 'sword:symbol:0:1122334455667788:0'
      }
    ]
  };
  const raidRewardsWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Raid reward tables, den ranks, item quantities, and source provenance.',
    diagnostics: [],
    id: 'raidRewards',
    label: 'Raid Rewards'
  };
  const raidBonusRewardsWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Raid bonus reward tables, item quantities, den usage, and source provenance.',
    diagnostics: [],
    id: 'raidBonusRewards',
    label: 'Raid Bonus Rewards'
  };
  const raidBattlesWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Raid Pokemon slots, star probabilities, ability rolls, guaranteed perfect IVs, and source provenance.',
    diagnostics: [],
    id: 'raidBattles',
    label: 'Raid Battles'
  };
  const raidBattlesWorkflow: RaidBattlesWorkflow = {
    diagnostics: [],
    editableFields: [
      {
        field: 'species',
        label: 'Species',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '025 Pikachu', value: 25 },
          { label: '133 Eevee', value: 133 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'form',
        label: 'Form',
        maximumValue: 31,
        minimumValue: 0,
        options: [
          { label: 'Base', value: 0 },
          { label: 'Form 1', value: 1 },
          { label: 'Form 2', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'flawlessIvs',
        label: 'Guaranteed perfect IVs',
        maximumValue: 6,
        minimumValue: 0,
        options: [
          { label: 'Random IVs', value: 0 },
          { label: '4 Guaranteed Perfect IVs', value: 4 },
          { label: '6 Guaranteed Perfect IVs', value: 6 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'star5Probability',
        label: '5-star probability',
        maximumValue: 100,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      }
    ],
    stats: {
      gigantamaxSlotCount: 1,
      sourceFileCount: 2,
      totalSlotCount: 2,
      totalTableCount: 1
    },
    summary: raidBattlesWorkflowSummary,
    tables: [
      {
        denId: 'table_AABBCCDD00112233',
        displayName: 'Sword - 0',
        gameVersion: 'Sword',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/archive/field/resident/data_table.gfpak',
          sourceLayer: 'base'
        },
        slots: [
          {
            ability: 4,
            abilityLabel: 'Any Ability',
            abilityOptions: [],
            bonusTableHash: '0x1020304050607080',
            bonusRewardLink: {
              isMatched: true,
              preview: '1 reward: Armorite Ore',
              rewardItemCount: 1,
              rewardKind: 'bonus',
              rewardKindLabel: 'Bonus',
              sourceTableHash: '0x1020304050607080',
              tableId: 'bonus:0:1020304050607080'
            },
            dropTableHash: '0xAABBCCDD00112233',
            dropRewardLink: {
              isMatched: true,
              preview: '2 rewards: Exp. Candy L, Rare Candy',
              rewardItemCount: 2,
              rewardKind: 'drop',
              rewardKindLabel: 'Drop',
              sourceTableHash: '0xAABBCCDD00112233',
              tableId: 'drop:0:AABBCCDD00112233'
            },
            entryIndex: 0,
            flawlessIvs: 4,
            form: 1,
            formOptions: [
              { label: 'Base', value: 0 },
              { label: 'Form 1', value: 1 }
            ],
            gender: 1,
            genderLabel: 'Male',
            isGigantamax: true,
            levelTableHash: '0x1122334455667788',
            probabilities: [100, 20, 30, 40, 50],
            probabilitySummary: '1-star 100% / 2-star 20% / 3-star 30% / 4-star 40% / 5-star 50%',
            slot: 1,
            species: 'Eevee',
            speciesId: 133
          },
          {
            ability: 0,
            abilityLabel: 'Ability 1',
            abilityOptions: [],
            bonusTableHash: '0x0807060504030201',
            bonusRewardLink: {
              isMatched: false,
              preview: 'No loaded bonus table matches this hash',
              rewardItemCount: 0,
              rewardKind: 'bonus',
              rewardKindLabel: 'Bonus',
              sourceTableHash: '0x0807060504030201',
              tableId: ''
            },
            dropTableHash: '0xAABBCCDD00112233',
            dropRewardLink: {
              isMatched: true,
              preview: '2 rewards: Exp. Candy L, Rare Candy',
              rewardItemCount: 2,
              rewardKind: 'drop',
              rewardKindLabel: 'Drop',
              sourceTableHash: '0xAABBCCDD00112233',
              tableId: 'drop:0:AABBCCDD00112233'
            },
            entryIndex: 1,
            flawlessIvs: 0,
            form: 0,
            formOptions: [{ label: 'Base', value: 0 }],
            gender: 0,
            genderLabel: 'Random',
            isGigantamax: false,
            levelTableHash: '0x2233445566778899',
            probabilities: [5, 10, 15, 20, 25],
            probabilitySummary: '1-star 5% / 2-star 10% / 3-star 15% / 4-star 20% / 5-star 25%',
            slot: 2,
            species: 'Pikachu',
            speciesId: 25
          }
        ],
        sourceTableHash: '0xAABBCCDD00112233',
        tableId: 'raid:0:AABBCCDD00112233',
        tableIndex: 0
      }
    ]
  };
  const raidRewardsWorkflow: RaidRewardsWorkflow = {
    diagnostics: [],
    editableFields: [
      {
        field: 'itemId',
        label: 'Item ID',
        maximumValue: 65535,
        minimumValue: 0,
        valueKind: 'integer'
      },
      {
        field: 'star5Value',
        label: '5-star value',
        maximumValue: 999,
        minimumValue: 0,
        valueKind: 'integer'
      }
    ],
    stats: {
      sourceFileCount: 1,
      totalRewardItemCount: 1,
      totalTableCount: 1
    },
    summary: raidRewardsWorkflowSummary,
    tables: [
      {
        archiveMember: 'nest_hole_drop_rewards.bin',
        denId: 'table_AABBCCDD00112233',
        displayName: 'Drop 000 | SW Den 0 Slot 00, 1-5-Star Eevee-1',
        gameVersion: 'Sword/Shield',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/archive/field/resident/data_table.gfpak',
          sourceLayer: 'base'
        },
        rank: 0,
        rewardKind: 'drop',
        rewardKindLabel: 'Drop',
        rewards: [
          {
            entryId: 10,
            itemId: 3,
            itemName: 'Exp. Candy L',
            quantity: 0,
            slot: 1,
            values: [40, 30, 20, 10, 5],
            weight: 40
          }
        ],
        sourceTableHash: '0xAABBCCDD00112233',
        tableId: 'drop:0:AABBCCDD00112233',
        tableIndex: 0
      }
    ]
  };
  const raidBonusRewardsWorkflow: RaidRewardsWorkflow = {
    ...raidRewardsWorkflow,
    summary: raidBonusRewardsWorkflowSummary,
    tables: raidRewardsWorkflow.tables.map((table) => ({
      ...table,
      archiveMember: 'nest_hole_bonus_rewards.bin',
      displayName: 'Bonus 000 | SW Den 0 Slot 00, 1-5-Star Eevee-1',
      rewardKind: 'bonus',
      rewardKindLabel: 'Bonus',
      sourceTableHash: '0x1020304050607080',
      tableId: 'bonus:0:1020304050607080'
    }))
  };
  const placementWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Placed objects, map coordinates, item pickups, and source provenance.',
    diagnostics: [],
    id: 'placement',
    label: 'Placement'
  };
  const placementWorkflow: PlacementWorkflow = {
    diagnostics: [],
    editableFields: [
      {
        field: 'quantity',
        label: 'Quantity',
        maximumValue: 999,
        minimumValue: 0,
        valueKind: 'integer'
      }
    ],
    objects: [
      {
        archiveMember: 'a_test.bin',
        chance: null,
        chanceIndex: null,
        itemHash: '0xAABBCCDD00112233',
        itemId: 1,
        itemName: 'Potion',
        label: 'Field item: Potion',
        map: 'Route 1',
        objectId: 'a_test.bin|0|fieldItem|0|-',
        objectIndex: 0,
        objectType: 'FieldItem',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/archive/field/resident/placement.gfpak',
          sourceLayer: 'base'
        },
        quantity: 1,
        rotationY: 90,
        scriptId: 'visible_potion',
        x: 10.5,
        y: 0,
        zoneIndex: 0,
        z: -4.25
      }
    ],
    stats: {
      sourceFileCount: 3,
      totalAreaCount: 1,
      totalObjectCount: 1
    },
    summary: placementWorkflowSummary
  };
  const behaviorWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Symbol encounter behavior parameters and source provenance.',
    diagnostics: [],
    id: 'behavior',
    label: 'Behavior'
  };
  const behaviorWorkflow: BehaviorWorkflow = {
    diagnostics: [],
    entries: [
      {
        behavior: 'Common',
        behaviorLabel: 'Common',
        entryId: 'behavior:0',
        fields: [
          { field: 'speciesId', value: '25' },
          { field: 'form', value: '0' },
          { field: 'behavior', value: 'Common' },
          { field: 'modelPart', value: 'body' },
          { field: 'hitboxRadius', value: '1.5' },
          { field: 'grassShakeRadius', value: '2' },
          { field: 'hash1', value: '0x0000000000000001' }
        ],
        form: 0,
        grassShakeRadius: 2,
        hash1: '0x0000000000000001',
        hash2: '0x0000000000000002',
        hitboxRadius: 1.5,
        index: 0,
        internalSpeciesName: 'PIKACHU',
        label: '#0 Pikachu - Common',
        modelPart: 'body',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/field/param/symbol_encount_mons_param/symbol_encount_mons_param.bin',
          sourceLayer: 'base'
        },
        speciesId: 25,
        speciesName: 'Pikachu'
      }
    ],
    fields: [
      {
        description: 'Species ID used by this symbol encounter behavior entry.',
        field: 'speciesId',
        group: 'Identity',
        isReadOnly: false,
        label: 'Pokemon',
        maximumValue: 999,
        minimumValue: 0,
        options: [{ label: 'Pikachu', value: '25' }],
        valueKind: 'integer'
      },
      {
        description: 'Form index used by this symbol encounter behavior entry.',
        field: 'form',
        group: 'Identity',
        isReadOnly: false,
        label: 'Form',
        maximumValue: 999,
        minimumValue: 0,
        valueKind: 'integer'
      },
      {
        description: 'Named movement behavior used by this symbol encounter.',
        field: 'behavior',
        group: 'Behavior',
        isReadOnly: false,
        label: 'Behavior',
        maximumValue: 128,
        minimumValue: 0,
        options: [{ label: 'Common', value: 'Common' }],
        valueKind: 'string'
      },
      {
        description: 'Internal reference hash. This is shown for inspection only.',
        field: 'hash1',
        group: 'Internal References',
        isReadOnly: true,
        label: 'Hash 1',
        maximumValue: Number.MAX_SAFE_INTEGER,
        minimumValue: 0,
        valueKind: 'hash'
      }
    ],
    stats: {
      sourceFileCount: 1,
      totalBehaviorCount: 1,
      totalEntryCount: 1
    },
    summary: behaviorWorkflowSummary
  };
  const flagworkSaveWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Flagwork hash tables, save keys, and source provenance.',
    diagnostics: [],
    id: 'flagworkSave',
    label: 'Flagwork and Save Inspectors'
  };
  const flagworkSaveWorkflow: FlagworkSaveWorkflow = {
    diagnostics: [],
    flags: [
      {
        category: 'system_flags',
        defaultValue: 'false',
        description: 'Flag hash 0x1122334455667788 uses save key 0x55667788.',
        flagId: 'system_flags:0000',
        hash: '0x1122334455667788',
        index: 0,
        kind: 'Flag',
        low32Key: '0x55667788',
        name: 'FE_TEST_FLAG',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/flagwork/system_flags.tbl',
          sourceLayer: 'base'
        },
        table: 'system_flags',
        valueKind: 'boolean'
      },
      {
        category: 'scene_work',
        defaultValue: '0',
        description: 'Work hash 0x99AABBCCDDEEFF00 uses save key 0xDDEEFF00.',
        flagId: 'scene_work:0000',
        hash: '0x99AABBCCDDEEFF00',
        index: 0,
        kind: 'Work',
        low32Key: '0xDDEEFF00',
        name: 'WK_SCENE_MAIN',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/flagwork/scene_work.tbl',
          sourceLayer: 'base'
        },
        table: 'scene_work',
        valueKind: 'integer'
      }
    ],
    saveBlocks: [
      {
        blockId: 'scene_work:0000:0xDDEEFF00',
        description: 'Save work key 0xDDEEFF00 is derived from WK_SCENE_MAIN.',
        hash: '0x99AABBCCDDEEFF00',
        key: '0xDDEEFF00',
        kind: 'Work',
        name: 'WK_SCENE_MAIN',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/flagwork/scene_work.tbl',
          sourceLayer: 'base'
        },
        valueKind: 'integer'
      }
    ],
    saveFile: {
      description: 'Save file is configured for read-only inspection.',
      fileName: 'main',
      sha256: '01020304',
      sizeBytes: 4,
      status: 'available'
    },
    stats: {
      hasSaveFile: true,
      sourceFileCount: 2,
      totalFlagCount: 2,
      totalSaveBlockCount: 1
    },
    summary: flagworkSaveWorkflowSummary
  };
  const exeFsPatchWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'ExeFS main validation, patch anchors, segment hashes, and source provenance.',
    diagnostics: [],
    id: 'exefsPatches',
    label: 'ExeFS Patch Manager'
  };
  const bagHookWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description:
      'Installs the shared Bag Hook V2 startup script with 20 disabled grant slots.',
    diagnostics: [],
    id: 'bagHook',
    label: 'Bag Hook'
  };
  const bagHookWorkflow: BagHookWorkflow = {
    diagnostics: [],
    installMessage: 'Bag Hook V2 can be installed with all grant slots disabled.',
    installStatus: canEdit ? 'available' : 'readOnly',
    slots: Array.from({ length: 20 }, (_, index) => {
      const slot = index + 1;
      return {
        isReserved: true,
        itemId: slot === 1 ? 1128 : null,
        itemName: slot === 1 ? 'Royal Candy' : 'None',
        notes: slot === 1 ? 'Royal Candy occupies slot 1.' : 'Disabled empty slot.',
        owner: slot === 1 ? 'Royal Candy' : 'Available for Starting Items',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/script/amx/main_event_0020.amx',
          sourceLayer: 'base'
        },
        quantity: slot === 1 ? 1 : null,
        reservedFor: slot === 1 ? 'Royal Candy' : 'Starting Items',
        slot,
        status: slot === 1 ? 'occupied' : 'empty'
      };
    }),
    stats: {
      emptySlotCount: 19,
      occupiedSlotCount: 1,
      reservedSlotCount: 20,
      sourceFileCount: 1,
      totalSlotCount: 20
    },
    summary: bagHookWorkflowSummary
  };
  const catchCapWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description:
      'Patches the display and runtime capture checks for badge-level catch caps 0-7; eight badges is locked at Lv.100.',
    diagnostics: [],
    id: 'catchCap',
    label: 'Catch Cap Editor'
  };
  const catchCapWorkflow: CatchCapWorkflow = {
    capLogicSha256: 'AABBCC',
    caps: Array.from({ length: 9 }, (_, badgeCount) => ({
      badgeCount,
      label: `${badgeCount} badges`,
      levelCap: badgeCount === 8 ? 100 : 20 + badgeCount * 5,
      maximumLevelCap: 100,
      minimumLevelCap: badgeCount === 8 ? 100 : 1
    })),
    diagnostics: [],
    installMessage: 'Catch Cap Editor can patch display and runtime capture checks in exefs/main.',
    installStatus: canEdit ? 'available' : 'readOnly',
    logicExpression: 'badge_count < 8 ? cap_table[badge_count] : 100',
    provenance: {
      fileState: 'baseOnly',
      sourceFile: 'exefs/main',
      sourceLayer: 'base'
    },
    stats: {
      sourceFileCount: 1,
      totalCapCount: 9
    },
    summary: catchCapWorkflowSummary
  };
  const hyperTrainingWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description:
      'Advanced editor for the Battle Tower Hyper Training NPC minimum level cutoff, matching English dialogue, and picker cutoff checks.',
    diagnostics: [],
    id: 'hyperTraining',
    label: 'Hyper Training'
  };
  const hyperTrainingWorkflow: HyperTrainingWorkflow = {
    diagnostics: [],
    installMessage: 'Hyper Training is using the vanilla Lv.100 minimum.',
    installStatus: canEdit ? 'available' : 'readOnly',
    levelRule: {
      dialogueSummary: 'English dialogue lines 0 and 3 mention the cutoff.',
      maximumAllowedLevel: 100,
      minimumAllowedLevel: 1,
      minimumLevel: 100,
      runtimeSummary:
        'Picker cutoff lives at main.text+0x00F9A314 and related Hyper Training list/detail checks.',
      scriptCell: 'AMX code cell 2294 (RND_TO_FLOOR operand)',
      vanillaMinimumLevel: 100
    },
    sources: [
      {
        label: 'Hyper Training script',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/script/amx/hyper_training.amx',
          sourceLayer: 'base'
        },
        relativePath: 'romfs/bin/script/amx/hyper_training.amx',
        sourceId: 'script',
        status: 'available'
      },
      {
        label: 'English Hyper Training dialogue',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/message/English/script/sub_event_007.dat',
          sourceLayer: 'base'
        },
        relativePath: 'romfs/bin/message/English/script/sub_event_007.dat',
        sourceId: 'dialogue',
        status: 'available'
      },
      {
        label: 'Hyper Training picker runtime',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'exefs/main',
          sourceLayer: 'base'
        },
        relativePath: 'exefs/main',
        sourceId: 'runtime',
        status: 'available'
      }
    ],
    stats: {
      outputFileCount: 3,
      sourceFileCount: 3
    },
    summary: hyperTrainingWorkflowSummary
  };
  const typeChartWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Advanced editor for the Sword/Shield type-effectiveness table in exefs/main.',
    diagnostics: [],
    id: 'typeChart',
    label: 'Type Chart'
  };
  const typeChartTypes = [
    ['NOR', 'Normal', '#A8A878'],
    ['FIR', 'Fire', '#F05030'],
    ['WAT', 'Water', '#6890F0'],
    ['ELE', 'Electric', '#F8D030'],
    ['GRA', 'Grass', '#78C850'],
    ['ICE', 'Ice', '#78C8F0'],
    ['FIG', 'Fighting', '#A05038'],
    ['POI', 'Poison', '#A040A0'],
    ['GRO', 'Ground', '#E0C068'],
    ['FLY', 'Flying', '#8080F0'],
    ['PSY', 'Psychic', '#F85888'],
    ['BUG', 'Bug', '#A8B820'],
    ['ROC', 'Rock', '#B8A038'],
    ['GHO', 'Ghost', '#6060B0'],
    ['DRA', 'Dragon', '#7038F8'],
    ['DAR', 'Dark', '#705848'],
    ['STE', 'Steel', '#B8B8D0'],
    ['FAI', 'Fairy', '#EE99EE']
  ] as const;
  const typeChartWorkflow: TypeChartWorkflow = {
    buildId: 'A3B75BCD3311385AEED67FBEEB79CBB7BF02F471',
    cells: Array.from({ length: 18 * 18 }, (_, index) => ({
      attackTypeIndex: Math.floor(index / 18),
      defenseTypeIndex: index % 18,
      effectiveness: 4 as const,
      vanillaEffectiveness: 4 as const
    })),
    chartOffsetHex: 'main.ro+0x00743600',
    detectedGame: 'sword',
    diagnostics: [],
    installMessage: 'Type Chart is using the vanilla Sword/Shield effectiveness table.',
    installStatus: canEdit ? 'available' : 'readOnly',
    source: {
      label: 'ExeFS main',
      provenance: {
        fileState: 'baseOnly',
        sourceFile: 'exefs/main',
        sourceLayer: 'base'
      },
      relativePath: 'exefs/main',
      sourceId: 'runtime',
      status: 'available'
    },
    stats: {
      chartCellCount: 18 * 18,
      outputFileCount: 1,
      sourceFileCount: 1
    },
    summary: typeChartWorkflowSummary,
    types: typeChartTypes.map(([shortLabel, label, color], typeIndex) => ({
      color,
      label,
      shortLabel,
      typeIndex
    }))
  };
  const gymUniformRemovalWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Keeps the player in their current outfit during gym challenges and gym battles.',
    diagnostics: [],
    id: 'gymUniformRemoval',
    label: 'Gym Uniform Removal'
  };
  const gymUniformRemovalWorkflow: GymUniformRemovalWorkflow = {
    buildId: 'A3B75BCD3311385AEED67FBEEB79CBB7BF02F471',
    diagnostics: [],
    installMessage: 'Gym Uniform Removal can create a build-ID IPS patch in exefs.',
    installStatus: canEdit ? 'available' : 'readOnly',
    patchOffsetHex: 'main.text+0x01472600',
    provenance: {
      fileState: 'baseOnly',
      sourceFile: 'exefs/main',
      sourceLayer: 'base'
    },
    reservedRegions: [
      {
        label: 'Gym Uniform Removal gym outfit handler override',
        length: 8,
        offsetLabel: 'text+0x1472600..0x1472607',
        regionId: 'gym-uniform-removal-sword-handler',
        rule: 'do-not-overwrite',
        startOffset: 0x01472600
      }
    ],
    stats: {
      reservedMainTextRegionCount: 1,
      sourceFileCount: 1
    },
    stubKind: 'vanilla handler',
    summary: gymUniformRemovalWorkflowSummary
  };
  const ivScreenWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Installs the Pokemon Summary raw-IV screen hook.',
    diagnostics: [],
    id: 'ivScreen',
    label: 'IV Screen'
  };
  const ivScreenWorkflow: IvScreenWorkflow = {
    diagnostics: [],
    hookSiteOffsetHex: 'main.text+0x0138F268',
    hyperTrainingWrapperOffsetHex: 'main.text+0x007790D0',
    installMessage: 'IV Screen can patch exefs/main.',
    installStatus: canEdit ? 'available' : 'readOnly',
    marker: 'SWSH_IV_DISPLAY_V1',
    provenance: {
      fileState: 'baseOnly',
      sourceFile: 'exefs/main',
      sourceLayer: 'base'
    },
    rawIvGetterOffsetHex: 'main.text+0x00779070',
    reservedRegions: [
      {
        label: 'IV Screen normal stats graph refresh hook branch site',
        length: 4,
        offsetLabel: 'text+0x138F268..0x138F26B',
        regionId: 'iv-screen-hook-site',
        rule: 'do-not-overwrite',
        startOffset: 0x0138f268
      }
    ],
    stats: {
      reservedMainTextRegionCount: 1,
      sourceFileCount: 1
    },
    summary: ivScreenWorkflowSummary
  };
  const startingItemsWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Adds selected startup item grants through Bag Hook slots 2-20.',
    diagnostics: [],
    id: 'startingItems',
    label: 'Starting Items'
  };
  const startingItemsWorkflow: StartingItemsWorkflow = {
    diagnostics: [],
    grants: Array.from({ length: 19 }, (_, index) => {
      const slot = index + 2;
      return {
        isKeyItem: false,
        itemId: slot === 2 ? 1 : null,
        itemName: slot === 2 ? 'Master Ball' : 'None',
        owner: slot === 2 ? 'Starting Items' : 'Available for Starting Items',
        provenance: {
          fileState: 'layeredOnly',
          sourceFile: 'romfs/bin/script/amx/main_event_0020.amx',
          sourceLayer: 'layered'
        },
        quantity: slot === 2 ? 5 : 1,
        slot,
        status: slot === 2 ? 'occupied' : 'empty'
      };
    }),
    installMessage: 'Starting Items can claim Bag Hook slots 2-20.',
    installStatus: canEdit ? 'available' : 'blocked',
    itemOptions: [
      {
        category: 'Items',
        isKeyItem: false,
        itemId: 1,
        name: 'Master Ball'
      },
      {
        category: 'Key Items',
        isKeyItem: true,
        itemId: 700,
        name: 'Bike'
      }
    ],
    stats: {
      itemOptionCount: 2,
      occupiedGrantSlotCount: 1,
      sourceFileCount: 2,
      totalGrantSlotCount: 19
    },
    summary: startingItemsWorkflowSummary
  };
  const exeFsPatchWorkflow: ExeFsPatchWorkflow = {
    checks: [
      {
        actual: 'text+0x7BC338',
        area: '.text',
        checkId: 'exefs-main-compatibility:patch-code-cave',
        expected: '12 zero bytes after text+0x7BC338',
        name: 'Patch code cave',
        notes: 'A code cave is available for small stubs.',
        offset: 'text+0x7BC338',
        patchId: 'exefs-main-compatibility',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'exefs/main',
          sourceLayer: 'base'
        },
        status: 'Pass'
      },
      {
        actual: '0',
        area: '.text',
        checkId: 'exefs-main-compatibility:royal-candy-immediate-scan',
        expected: '0 patched CMP immediates in vanilla main',
        name: 'Royal Candy immediate scan',
        notes: 'No obvious item-id 1128 CMP immediates were found in the known route registers.',
        offset: '',
        patchId: 'exefs-main-compatibility',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'exefs/main',
          sourceLayer: 'base'
        },
        status: 'Info'
      }
    ],
    diagnostics: [],
    patches: [
      {
        description:
          'Validates Sword/Shield ExeFS main structure, segment hashes, code-cave availability, and known patch anchors.',
        details: [
          'Build ID: ABABABABABABABABABABABABABABABABABABABABABABABABABABABABABABAB',
          'File size: 0x7DDAB0 bytes',
          'Checks: 26 total, 0 failing, 0 warnings'
        ],
        name: 'ExeFS main compatibility',
        patchId: 'exefs-main-compatibility',
        patchKind: 'NSO signature scan',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'exefs/main',
          sourceLayer: 'base'
        },
        status: 'available',
        targetFile: 'exefs/main'
      }
    ],
    segments: [
      {
        compressedSize: '0x7DDA90',
        decompressedSize: '0x7DDA90',
        fileOffset: 'file+0x100',
        hashStatus: 'Pass',
        memoryOffset: '0x0',
        name: '.text',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'exefs/main',
          sourceLayer: 'base'
        },
        segmentId: 'text',
        sha256: 'ABCD'
      }
    ],
    stats: {
      failCount: 0,
      passCount: 24,
      sourceFileCount: 1,
      totalCheckCount: 26,
      totalPatchCount: 1,
      warningCount: 0
    },
    summary: exeFsPatchWorkflowSummary
  };
  const royalCandyWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Royal Candy source readiness, ExeFS compatibility, and LayeredFS output preview.',
    diagnostics: [],
    id: 'royalCandy',
    label: 'Royal Candy Workflows'
  };
  const royalCandyLevelCaps = [
    {
      label: 'Hop 004/005/006',
      levelCap: 10,
      maximumLevelCap: 100,
      milestoneId: '0:A9C039F0598B8A31:0',
      minimumLevelCap: 1,
      progressHash: '0xA9C039F0598B8A31',
      progressKind: 'flag',
      slot: 0,
      workMinimum: null
    },
    {
      label: 'Hop 007/008/009',
      levelCap: 16,
      maximumLevelCap: 100,
      milestoneId: '1:005A329212277F11:0',
      minimumLevelCap: 1,
      progressHash: '0x005A329212277F11',
      progressKind: 'flag',
      slot: 1,
      workMinimum: null
    }
  ];
  const spreadsheetImportWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'CSV and TSV import profiles that execute through backend edit sessions.',
    diagnostics: [],
    id: 'spreadsheetImport',
    label: 'Spreadsheet Import'
  };
  const spreadsheetImportWorkflow: SpreadsheetImportWorkflow = {
    diagnostics: [],
    profiles: [
      {
        columns: [
          {
            column: 1,
            description: 'Existing item ID.',
            header: 'ItemId',
            isRequired: true,
            valueKind: 'integer'
          },
          {
            column: 2,
            description: 'New buy price.',
            header: 'BuyPrice',
            isRequired: false,
            valueKind: 'integer'
          },
          {
            column: 3,
            description: 'New Watts price.',
            header: 'WattsPrice',
            isRequired: false,
            valueKind: 'integer'
          }
        ],
        description: 'Imports item price columns into the Items workflow for change-plan review.',
        name: 'Items Price CSV/TSV',
        profileId: 'items-price-csv',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/pml/item/item.dat',
          sourceLayer: 'base'
        },
        sourceKind: 'csv/tsv',
        status: canEdit ? 'available' : 'readOnly',
        targetWorkflow: 'items'
      }
    ],
    stats: {
      sourceFileCount: 1,
      totalColumnCount: 3,
      totalProfileCount: 1
    },
    summary: spreadsheetImportWorkflowSummary
  };
  const modMergerWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Merge matching RomFS files from two mod folders.',
    diagnostics: [],
    id: 'modMerger',
    label: 'Mod Merger'
  };
  const modMergerWorkflow: ModMergerWorkflow = {
    diagnostics: [],
    directory1Files: [
      {
        name: 'shop_data.bin',
        relativePath: 'romfs/bin/shop_data.bin',
        size: 24,
        status: 'available',
        supportKind: 'Shop data'
      }
    ],
    directory2Files: [
      {
        name: 'shop_data.bin',
        relativePath: 'romfs/bin/shop_data.bin',
        size: 24,
        status: 'available',
        supportKind: 'Shop data'
      }
    ],
    modDirectory1: 'mod-directory-1',
    modDirectory2: 'mod-directory-2',
    outputRootPath: 'output',
    stats: {
      directory1FileCount: 1,
      directory2FileCount: 1,
      matchingFileCount: 1
    },
    summary: modMergerWorkflowSummary
  };
  const createModMergerPreview = (
    selectedDirectory1Files: string[],
    selectedDirectory2Files: string[]
  ): ModMergerPreview => {
    const selectedDirectory2Set = new Set(selectedDirectory2Files);
    const selectedDirectory1Only = selectedDirectory1Files.filter(
      (relativePath) => !selectedDirectory2Set.has(relativePath)
    );
    const selectedDirectory1Set = new Set(selectedDirectory1Files);
    const selectedDirectory2Only = selectedDirectory2Files.filter(
      (relativePath) => !selectedDirectory1Set.has(relativePath)
    );
    const matchedFiles = selectedDirectory1Files.filter((relativePath) =>
      selectedDirectory2Set.has(relativePath)
    );
    const selectedFiles = matchedFiles.map((relativePath) => ({
      conflictCount: 0,
      directory1ChangeCount: 1,
      directory2ChangeCount: 1,
      mergeKind: 'smartMerge',
      outputRelativePath: relativePath,
      relativePath,
      status: 'ready',
      summary: 'Non-overlapping byte changes can be merged safely.',
      supportKind: 'Shop data'
    }));
    const diagnostics =
      selectedDirectory1Only.length === 0 && selectedDirectory2Only.length === 0
        ? []
      : [
          {
            message: 'Files missing from one side were ignored for the merge.',
            severity: 'warning' as const
          }
        ];

    return {
      canApply: selectedFiles.length > 0,
      conflictFileCount: 0,
      conflicts: [],
      diagnostics,
      files: selectedFiles,
      mergeMode: 'smart',
      readyFileCount: selectedFiles.length,
      selectedFileCount: selectedFiles.length,
      status: selectedFiles.length > 0 ? 'ready' : 'empty',
      unresolvedConflictCount: 0
    };
  };
  const svModMergerWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Smart merge ordered Scarlet/Violet RomFS mods.',
    diagnostics: [],
    id: 'modMerger',
    label: 'S/V Mod Merger'
  };
  const createSvModMergerWorkflow = (
    modSources: SvModMergerSource[],
    outputRootPath: string | null
  ): SvModMergerWorkflow => ({
    diagnostics: [],
    outputRootPath,
    sources: modSources.map((source, index) => ({
      diagnostics: [],
      fileCount: source.isEnabled ? 1 : 0,
      isEnabled: source.isEnabled,
      kind: source.path.endsWith('.zip') || source.path.endsWith('.rar') ? 'archive' : 'folder',
      name: source.path.split(/[\\/]/).pop()?.replace(/\.(zip|rar)$/i, '') || `Source ${index + 1}`,
      overrideCount: index > 0 && source.isEnabled ? 1 : 0,
      path: source.path,
      sourceIndex: index,
      status: source.isEnabled ? 'ready' : 'disabled'
    })),
    stats: {
      enabledSourceCount: modSources.filter((source) => source.isEnabled).length,
      outputFileCount: modSources.some((source) => source.isEnabled) ? 1 : 0,
      overrideCount: Math.max(0, modSources.filter((source) => source.isEnabled).length - 1),
      sourceCount: modSources.length,
      sourceFileCount: modSources.filter((source) => source.isEnabled).length
    },
    summary: svModMergerWorkflowSummary
  });
  const createSvModMergerPreview = (
    modSources: SvModMergerSource[]
  ): SvModMergerPreview => {
    const enabledSources = modSources.filter((source) => source.isEnabled);
    const sourceName =
      enabledSources.at(-1)?.path.split(/[\\/]/).pop()?.replace(/\.(zip|rar)$/i, '') ?? '';

    return {
      canApply: enabledSources.length > 0,
      conflictFileCount: 0,
      diagnostics: [],
      files:
        enabledSources.length > 0
          ? [
              {
                mergeKind: enabledSources.length > 1 ? 'smartMerge' : 'singleSource',
                outputRelativePath: 'romfs/bin/mock/data.bin',
                overrideCount: Math.max(0, enabledSources.length - 1),
                relativePath: 'romfs/bin/mock/data.bin',
                sourceIndex: modSources.lastIndexOf(enabledSources.at(-1)!),
                sourceName,
                status: 'ready',
                summary: 'Smart merge preview fixture.',
                supportKind: 'Scarlet/Violet RomFS file'
              }
            ]
          : [],
      readyFileCount: enabledSources.length > 0 ? 1 : 0,
      selectedFileCount: enabledSources.length > 0 ? 1 : 0,
      status: enabledSources.length > 0 ? 'ready' : 'empty',
      unresolvedConflictCount: 0
    };
  };

  let currentGiftPokemonWorkflow = giftPokemonWorkflow;
  let currentTradePokemonWorkflow = tradePokemonWorkflow;
  const createDynamaxAdventurePlanWrites = (session: EditSession): ChangePlan['writes'] => {
    const requiresMainPatch = session.pendingEdits.some((edit) =>
      ['species', 'form', 'gigantamaxState'].includes(edit.field ?? '')
    );
    const writes: ChangePlan['writes'] = [
      {
        reason:
          'Apply pending Dynamax Adventures edit: Set Adventure 001 safe Pokemon fields.',
        replacesExistingOutput: false,
        sources: [
          {
            layer: 'base',
            relativePath:
              'romfs/bin/appli/chika/data_table/underground_exploration_poke.bin'
          }
        ],
        targetRelativePath:
          'romfs/bin/appli/chika/data_table/underground_exploration_poke.bin'
      }
    ];

    return requiresMainPatch
      ? [
          ...writes,
          {
            reason: 'Patch Dynamax Adventures ExeFS mirrors for edited Adventure identity data.',
            replacesExistingOutput: false,
            sources: [
              {
                layer: 'base',
                relativePath: 'exefs/main'
              }
            ],
            targetRelativePath: 'exefs/main'
          }
        ]
      : writes;
  };
  const ivFieldToKey = {
    ivAttack: 'attack',
    ivDefense: 'defense',
    ivHp: 'hp',
    ivSpecialAttack: 'specialAttack',
    ivSpecialDefense: 'specialDefense',
    ivSpeed: 'speed'
  } as const;
  const randomizerOptions = {
    ability1: true,
    ability2: true,
    allowSameType: false,
    compatibilityMachines: true,
    compatibilityRecords: true,
    compatibilityTutors: true,
    hiddenAbility: true,
    learnsetBanFixedDamageMoves: true,
    learnsetExpandTo25: false,
    learnsetRequireDamagingMove: true,
    learnsetStabFirst: true,
    randomizeGiftEncounters: false,
    randomizePokemonAbilities: false,
    randomizePokemonCatchRates: false,
    randomizePokemonCompatibility: false,
    randomizePokemonEvolutions: false,
    randomizePokemonHeldItems: false,
    randomizePokemonLearnsets: false,
    randomizePokemonStats: false,
    randomizePokemonTypes: false,
    randomizeRaidBonusRewards: false,
    randomizeRaidRewards: false,
    randomizeStaticEncounters: false,
    randomizeTypeChart: false,
    randomizeWildEncounters: false,
    shufflePokemonStats: true,
    statAttack: true,
    statDefense: true,
    statHp: true,
    statSpecialAttack: true,
    statSpecialDefense: true,
    statSpeed: true,
    typeChartNoImmunities: false,
    typeChartOneImmunityPerType: false,
    typePrimary: true,
    typeSecondary: true
  };

  return {
    applyChangePlan: (request) =>
      Promise.resolve({
        applyResult: {
          applyId: 'apply-1',
          diagnostics: [
            {
              message: getApplyMessage(
                request.changePlan.writes[0]?.targetRelativePath ?? '',
                request.session.pendingEdits[0]?.domain
              ),
              severity: 'info'
            }
          ],
          writtenFiles: request.changePlan.writes.map((write) => write.targetRelativePath)
        }
      }),
    createChangePlan: (request) =>
      Promise.resolve({
        changePlan: {
          canApply: true,
          diagnostics: [
            {
              message: 'Change plan preview contains 1 target file.',
              severity: 'info'
            }
          ],
          sessionId: request.session.sessionId,
          writes:
            request.session.pendingEdits[0]?.domain === 'workflow.text'
              ? [
                  {
                    reason: 'Apply pending Text edit: Set story #0 to "Hello there.".',
                    replacesExistingOutput: false,
                    sources: [
                      {
                        layer: 'base',
                        relativePath: 'romfs/bin/message/English/common/story.dat'
                      }
                    ],
                    targetRelativePath: 'romfs/bin/message/English/common/story.dat'
                  }
                ]
                : request.session.pendingEdits[0]?.domain === 'workflow.trainers'
                  ? [
                      {
                        reason: 'Apply pending Trainers edit: Set Avery slot 1 level to 25.',
                      replacesExistingOutput: false,
                      sources: [
                        {
                          layer: 'base',
                          relativePath: 'romfs/bin/trainer/trainer_poke/trainer_010.bin'
                        }
                      ],
                      targetRelativePath: 'romfs/bin/trainer/trainer_poke/trainer_010.bin'
                    }
                  ]
                : request.session.pendingEdits[0]?.domain === 'workflow.pokemon'
                  ? [
                      {
                        reason: 'Apply pending Pokemon edit: Set Bulbasaur hp to 99.',
                        replacesExistingOutput: false,
                        sources: [
                          {
                            layer: 'base',
                            relativePath: 'romfs/bin/pml/personal/personal_total.bin'
                          }
                        ],
                        targetRelativePath: 'romfs/bin/pml/personal/personal_total.bin'
                      }
                    ]
                : request.session.pendingEdits[0]?.domain === 'workflow.moves'
                  ? [
                      {
                        reason: 'Apply pending Moves edit: Set Tackle power to 80.',
                        replacesExistingOutput: false,
                        sources: [
                          {
                            layer: 'base',
                            relativePath: 'romfs/bin/pml/waza/waza_033.bin'
                          }
                        ],
                        targetRelativePath: 'romfs/bin/pml/waza/waza_033.bin'
                      }
                    ]
                : request.session.pendingEdits[0]?.domain === 'workflow.giftPokemon'
                  ? [
                      {
                        reason: 'Apply pending Gift Pokemon edit: Set Gift 001 HP IV to 31.',
                        replacesExistingOutput: false,
                        sources: [
                          {
                            layer: 'base',
                            relativePath: 'romfs/bin/script_event_data/add_poke.bin'
                          }
                        ],
                        targetRelativePath: 'romfs/bin/script_event_data/add_poke.bin'
                      }
                    ]
                : request.session.pendingEdits[0]?.domain === 'workflow.tradePokemon'
                  ? [
                      {
                        reason: 'Apply pending Trade Pokemon edit: Set Trade 001 HP IV to 31.',
                        replacesExistingOutput: false,
                        sources: [
                          {
                            layer: 'base',
                            relativePath: 'romfs/bin/script_event_data/field_trade.bin'
                          }
                        ],
                        targetRelativePath: 'romfs/bin/script_event_data/field_trade.bin'
                      }
                    ]
                : request.session.pendingEdits[0]?.domain === 'workflow.staticEncounters'
                  ? [
                      {
                        reason: 'Apply pending Static Encounter edit: Set Static 001 HP IV to 0.',
                        replacesExistingOutput: false,
                        sources: [
                          {
                            layer: 'base',
                            relativePath:
                              'romfs/bin/script_event_data/event_encount_data.bin'
                          }
                        ],
                        targetRelativePath:
                          'romfs/bin/script_event_data/event_encount_data.bin'
                      }
                    ]
                : request.session.pendingEdits[0]?.domain === 'workflow.rentalPokemon'
                  ? [
                      {
                        reason: 'Apply pending Rental Pokemon edit: Set Rental 001 HP IV to 0.',
                        replacesExistingOutput: false,
                        sources: [
                          {
                            layer: 'base',
                            relativePath: 'romfs/bin/script_event_data/rental.bin'
                          }
                        ],
                        targetRelativePath: 'romfs/bin/script_event_data/rental.bin'
                      }
                    ]
                : request.session.pendingEdits[0]?.domain === 'workflow.dynamaxAdventures'
                  ? createDynamaxAdventurePlanWrites(request.session)
                : request.session.pendingEdits[0]?.domain === 'workflow.shops'
                  ? [
                      {
                        reason: 'Apply pending Shops edit: Set Poke Mart inventory order to 2 items.',
                        replacesExistingOutput: false,
                        sources: [
                          {
                            layer: 'base',
                            relativePath: 'romfs/bin/appli/shop/bin/shop_data.bin'
                          }
                        ],
                        targetRelativePath: 'romfs/bin/appli/shop/bin/shop_data.bin'
                      }
                    ]
                  : request.session.pendingEdits[0]?.domain === 'workflow.encounters'
                    ? [
                        {
                          reason:
                            'Apply pending Encounters edit: Set Sword Symbol Zone 0x1122334455667788 Normal slot 2 probability to 40.',
                          replacesExistingOutput: false,
                          sources: [
                            {
                              layer: 'base',
                              relativePath:
                                'romfs/bin/archive/field/resident/data_table.gfpak'
                            }
                          ],
                          targetRelativePath:
                            'romfs/bin/archive/field/resident/data_table.gfpak'
                        }
                      ]
                    : request.session.pendingEdits[0]?.domain === 'workflow.raidBattles'
                      ? [
                          {
                            reason:
                              'Apply pending Raid Battles edit: Set Raid Battles 0xAABBCCDD00112233 slot 2 guaranteed perfect IVs to 6.',
                            replacesExistingOutput: false,
                            sources: [
                              {
                                layer: 'base',
                                relativePath:
                                  'romfs/bin/archive/field/resident/data_table.gfpak'
                              }
                            ],
                            targetRelativePath:
                              'romfs/bin/archive/field/resident/data_table.gfpak'
                          }
                        ]
                      : request.session.pendingEdits[0]?.domain === 'workflow.raidRewards'
                        ? [
                            {
                              reason:
                                'Apply pending Raid Rewards edit: Set Drop 0xAABBCCDD00112233 slot 1 5-star drop chance to 77.',
                              replacesExistingOutput: false,
                              sources: [
                                {
                                  layer: 'base',
                                  relativePath:
                                    'romfs/bin/archive/field/resident/data_table.gfpak'
                                }
                              ],
                              targetRelativePath:
                                'romfs/bin/archive/field/resident/data_table.gfpak'
                            }
                          ]
                        : request.session.pendingEdits[0]?.domain === 'workflow.bagHook'
                        ? [
                            {
                              reason: 'Install Bag Hook V2 with 20 disabled startup item grant slots.',
                              replacesExistingOutput: false,
                              sources: [
                                {
                                  layer: 'base',
                                  relativePath: 'romfs/bin/script/amx/main_event_0020.amx'
                                }
                              ],
                              targetRelativePath: 'romfs/bin/script/amx/main_event_0020.amx'
                            }
                          ]
                        : request.session.pendingEdits[0]?.domain === 'workflow.catchCap'
                        ? [
                            {
                              reason:
                                'Apply Catch Cap Editor display/runtime hook and badge cap values 0-7 to exefs/main; eight badges remains Lv.100.',
                              replacesExistingOutput: false,
                              sources: [
                                {
                                  layer: 'base',
                                  relativePath: 'exefs/main'
                                }
                              ],
                              targetRelativePath: 'exefs/main'
                            }
                          ]
                        : request.session.pendingEdits[0]?.domain === 'workflow.hyperTraining'
                        ? [
                            {
                              reason:
                                'Set the Battle Tower Hyper Training script minimum level.',
                              replacesExistingOutput: false,
                              sources: [
                                {
                                  layer: 'base',
                                  relativePath: 'romfs/bin/script/amx/hyper_training.amx'
                                }
                              ],
                              targetRelativePath: 'romfs/bin/script/amx/hyper_training.amx'
                            },
                            {
                              reason:
                                'Update the Hyper Training party/box picker cutoff checks in exefs/main.',
                              replacesExistingOutput: false,
                              sources: [
                                {
                                  layer: 'base',
                                  relativePath: 'exefs/main'
                                }
                              ],
                              targetRelativePath: 'exefs/main'
                            },
                            {
                              reason:
                                'Update English Hyper Training NPC dialogue to mention the selected level.',
                              replacesExistingOutput: false,
                              sources: [
                                {
                                  layer: 'base',
                                  relativePath:
                                    'romfs/bin/message/English/script/sub_event_007.dat'
                                }
                              ],
                              targetRelativePath:
                                'romfs/bin/message/English/script/sub_event_007.dat'
                            }
                          ]
                        : request.session.pendingEdits[0]?.domain === 'workflow.gymUniformRemoval'
                        ? [
                            {
                              reason:
                                'Install or refresh Gym Uniform Removal build-ID IPS patch in exefs.',
                              replacesExistingOutput: false,
                              sources: [
                                {
                                  layer: 'base',
                                  relativePath: 'exefs/main'
                                }
                              ],
                              targetRelativePath:
                                'exefs/A3B75BCD3311385AEED67FBEEB79CBB7BF02F471.ips'
                            }
                          ]
                        : request.session.pendingEdits[0]?.domain === 'workflow.ivScreen'
                        ? [
                            {
                              reason: 'Install or refresh IV Screen raw-IV hook in exefs/main.',
                              replacesExistingOutput: false,
                              sources: [
                                {
                                  layer: 'base',
                                  relativePath: 'exefs/main'
                                }
                              ],
                              targetRelativePath: 'exefs/main'
                            }
                          ]
                        : request.session.pendingEdits[0]?.domain === 'workflow.royalCandy'
                        ? [
                            {
                              reason:
                                'Apply Royal Candy workflow: Royal Candy item row patch.',
                              replacesExistingOutput: false,
                              sources: [
                                {
                                  layer: 'base',
                                  relativePath: 'romfs/bin/pml/item/item.dat'
                                }
                              ],
                              targetRelativePath: 'romfs/bin/pml/item/item.dat'
                            }
                          ]
                        : request.session.pendingEdits[0]?.domain === 'workflow.exefsPatches'
                          ? [
                              {
                                reason:
                                  'Apply ExeFS patch: Royal Candy UI route and usage patch.',
                                replacesExistingOutput: false,
                                sources: [
                                  {
                                    layer: 'base',
                                    relativePath: 'exefs/main'
                                  }
                                ],
                              targetRelativePath: 'exefs/main'
                            }
                          ]
                        : request.session.pendingEdits[0]?.domain === 'workflow.startingItems'
                          ? [
                              {
                                reason: 'Update Bag Hook slots 2-20 with reviewed Starting Items grants.',
                                replacesExistingOutput: true,
                                sources: [
                                  {
                                    layer: 'layered',
                                    relativePath: 'romfs/bin/script/amx/main_event_0020.amx'
                                  }
                                ],
                                targetRelativePath: 'romfs/bin/script/amx/main_event_0020.amx'
                              }
                            ]
                        : [
                            {
                              reason: 'Apply pending Items edit: Set Potion buy price to 450.',
                              replacesExistingOutput: false,
                              sources: [
                                {
                                  layer: 'base',
                                  relativePath: 'romfs/bin/pml/item/item.dat'
                                }
                              ],
                              targetRelativePath: 'romfs/bin/pml/item/item.dat'
                            }
                          ]
        }
      }),
    listWorkflows: () =>
      Promise.resolve({
        workflows: [
          itemsWorkflow.summary,
          pokemonWorkflowSummary,
          movesWorkflowSummary,
          textWorkflowSummary,
          trainersWorkflowSummary,
          giftPokemonWorkflowSummary,
          tradePokemonWorkflowSummary,
          staticEncountersWorkflowSummary,
          rentalPokemonWorkflowSummary,
          dynamaxAdventuresWorkflowSummary,
          shopsWorkflowSummary,
          encountersWorkflowSummary,
          raidBattlesWorkflowSummary,
          raidRewardsWorkflowSummary,
          raidBonusRewardsWorkflowSummary,
          placementWorkflowSummary,
          flagworkSaveWorkflowSummary,
          bagHookWorkflowSummary,
          catchCapWorkflowSummary,
          hyperTrainingWorkflowSummary,
          typeChartWorkflowSummary,
          gymUniformRemovalWorkflowSummary,
          ivScreenWorkflowSummary,
          royalCandyWorkflowSummary,
          startingItemsWorkflowSummary,
          spreadsheetImportWorkflowSummary,
          modMergerWorkflowSummary
        ]
      }),
    loadEncountersWorkflow: () =>
      Promise.resolve({
        workflow: encountersWorkflow
      }),
    loadFlagworkSaveWorkflow: () =>
      Promise.resolve({
        workflow: flagworkSaveWorkflow
      }),
    loadBagHookWorkflow: () =>
      Promise.resolve({
        workflow: bagHookWorkflow
      }),
    stageBagHookInstall: (request) =>
      Promise.resolve({
        diagnostics: [
          {
            message: 'Bag Hook V2 install is staged for change-plan review.',
            severity: 'info'
          }
        ],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.bagHook',
              field: 'install',
              newValue: 'v2-empty',
              recordId: 'bag-hook-v2',
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/script/amx/main_event_0020.amx'
                }
              ],
              summary: 'Stage Bag Hook install: 20 disabled startup item grant slots.'
            }
          ],
          sessionId: request.session?.sessionId ?? 'session-bag-hook'
        },
        workflow: bagHookWorkflow
      }),
    stageBagHookUninstall: (request) =>
      Promise.resolve({
        diagnostics: [
          {
            message: 'Bag Hook V2 uninstall is staged for change-plan review.',
            severity: 'info'
          }
        ],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.bagHook',
              field: 'uninstall',
              newValue: 'remove-bag-hook-and-dependents',
              recordId: 'bag-hook-v2-uninstall',
              sources: [
                {
                  layer: 'layered',
                  relativePath: 'romfs/bin/script/amx/main_event_0020.amx'
                }
              ],
              summary:
                'Stage Bag Hook uninstall: remove Bag Hook plus dependent Royal Candy and Starting Items outputs.'
            }
          ],
          sessionId: request.session?.sessionId ?? 'session-bag-hook-uninstall'
        },
        workflow: {
          ...bagHookWorkflow,
          installMessage: 'Bag Hook V2 is installed.',
          installStatus: 'installed'
        }
      }),
    loadCatchCapWorkflow: () =>
      Promise.resolve({
        workflow: catchCapWorkflow
      }),
    stageCatchCap: (request) =>
      Promise.resolve({
        diagnostics: [
          {
            message: 'Catch Cap Editor values are staged for change-plan review.',
            severity: 'info'
          }
        ],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.catchCap',
              field: 'caps',
              newValue: request.caps
                .map((cap) => `${cap.badgeCount}=${cap.levelCap}`)
                .join(';'),
              recordId: 'catch-cap-v1',
              sources: [
                {
                  layer: 'base',
                  relativePath: 'exefs/main'
                }
              ],
              summary:
                'Stage Catch Cap Editor values for badge counts 0-7 and the display/runtime hook; eight badges remains Lv.100.'
            }
          ],
          sessionId: request.session?.sessionId ?? 'session-catch-cap'
        },
        workflow: catchCapWorkflow
      }),
    stageCatchCapUninstall: (request) =>
      Promise.resolve({
        diagnostics: [
          {
            message: 'Catch Cap Editor uninstall is staged for change-plan review.',
            severity: 'info'
          }
        ],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.catchCap',
              field: 'uninstall',
              newValue: 'true',
              recordId: 'catch-cap-v1-uninstall',
              sources: [
                {
                  layer: 'layered',
                  relativePath: 'exefs/main'
                },
                {
                  layer: 'base',
                  relativePath: 'exefs/main'
                }
              ],
              summary: 'Stage Catch Cap Editor uninstall.'
            }
          ],
          sessionId: request.session?.sessionId ?? 'session-catch-cap-uninstall'
        },
        workflow: {
          ...catchCapWorkflow,
          installMessage: 'Catch Cap Editor hook is installed for display and runtime capture checks.',
          installStatus: 'installed',
          provenance: {
            fileState: 'layeredOverride',
            sourceFile: 'exefs/main',
            sourceLayer: 'layered'
          }
        }
      }),
    loadHyperTrainingWorkflow: () =>
      Promise.resolve({
        workflow: hyperTrainingWorkflow
      }),
    stageHyperTraining: (request) =>
      Promise.resolve({
        diagnostics: [
          {
            message: `Hyper Training minimum level Lv.${request.minimumLevel} is staged for change-plan review.`,
            severity: 'info'
          }
        ],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.hyperTraining',
              field: 'minimumLevel',
              newValue: request.minimumLevel.toString(),
              recordId: 'hyper-training-minimum-level',
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/script/amx/hyper_training.amx'
                },
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/message/English/script/sub_event_007.dat'
                },
                {
                  layer: 'base',
                  relativePath: 'exefs/main'
                }
              ],
              summary: `Stage Hyper Training minimum level Lv.${request.minimumLevel}.`
            }
          ],
          sessionId: request.session?.sessionId ?? 'session-hyper-training'
        },
        workflow: {
          ...hyperTrainingWorkflow,
          installMessage: `Hyper Training currently accepts Pokemon at Lv.${request.minimumLevel} or higher.`,
          installStatus:
            request.minimumLevel === 100
              ? hyperTrainingWorkflow.installStatus
              : 'installed',
          levelRule: {
            ...hyperTrainingWorkflow.levelRule,
            minimumLevel: request.minimumLevel
          }
        }
      }),
    loadTypeChartWorkflow: () =>
      Promise.resolve({
        workflow: typeChartWorkflow
      }),
    stageTypeChart: (request) =>
      Promise.resolve({
        diagnostics: [
          {
            message: 'Type Chart effectiveness values are staged for change-plan review.',
            severity: 'info'
          }
        ],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.typeChart',
              field: 'effectiveness',
              newValue: request.values
                .map((value) => value.toString(16).padStart(2, '0'))
                .join('')
                .toUpperCase(),
              recordId: 'type-chart',
              sources: [
                {
                  layer: 'base',
                  relativePath: 'exefs/main'
                }
              ],
              summary: 'Stage Type Chart effectiveness values.'
            }
          ],
          sessionId: request.session?.sessionId ?? 'session-type-chart'
        },
        workflow: {
          ...typeChartWorkflow,
          cells: typeChartWorkflow.cells.map((cell, index) => ({
            ...cell,
            effectiveness: request.values[index] ?? cell.effectiveness
          }))
        }
      }),
    loadGymUniformRemovalWorkflow: () =>
      Promise.resolve({
        workflow: gymUniformRemovalWorkflow
      }),
    stageGymUniformRemovalInstall: (request) =>
      Promise.resolve({
        diagnostics: [
          {
            message: 'Gym Uniform Removal install is staged for change-plan review.',
            severity: 'info'
          }
        ],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.gymUniformRemoval',
              field: 'install',
              newValue: 'true',
              recordId: 'gym-uniform-removal-v1-install',
              sources: [
                {
                  layer: 'generated',
                  relativePath: 'exefs/A3B75BCD3311385AEED67FBEEB79CBB7BF02F471.ips'
                },
                {
                  layer: 'base',
                  relativePath: 'exefs/main'
                }
              ],
              summary: 'Stage Gym Uniform Removal install.'
            }
          ],
          sessionId: request.session?.sessionId ?? 'session-gym-uniform-install'
        },
        workflow: gymUniformRemovalWorkflow
      }),
    stageGymUniformRemovalUninstall: (request) =>
      Promise.resolve({
        diagnostics: [
          {
            message: 'Gym Uniform Removal uninstall is staged for change-plan review.',
            severity: 'info'
          }
        ],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.gymUniformRemoval',
              field: 'uninstall',
              newValue: 'true',
              recordId: 'gym-uniform-removal-v1-uninstall',
              sources: [
                {
                  layer: 'generated',
                  relativePath: 'exefs/A3B75BCD3311385AEED67FBEEB79CBB7BF02F471.ips'
                },
                {
                  layer: 'base',
                  relativePath: 'exefs/main'
                }
              ],
              summary: 'Stage Gym Uniform Removal uninstall.'
            }
          ],
          sessionId: request.session?.sessionId ?? 'session-gym-uniform-uninstall'
        },
        workflow: {
          ...gymUniformRemovalWorkflow,
          installMessage: 'Gym Uniform Removal is installed.',
          installStatus: 'installed',
          provenance: {
            fileState: 'layeredOverride',
            sourceFile: 'exefs/main',
            sourceLayer: 'layered'
          }
        }
      }),
    loadIvScreenWorkflow: () =>
      Promise.resolve({
        workflow: ivScreenWorkflow
      }),
    stageIvScreenInstall: (request) =>
      Promise.resolve({
        diagnostics: [
          {
            message: 'IV Screen install is staged for change-plan review.',
            severity: 'info'
          }
        ],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.ivScreen',
              field: 'install',
              newValue: 'true',
              recordId: 'iv-screen-v1-install',
              sources: [
                {
                  layer: 'layered',
                  relativePath: 'exefs/main'
                },
                {
                  layer: 'base',
                  relativePath: 'exefs/main'
                }
              ],
              summary: 'Stage IV Screen install.'
            }
          ],
          sessionId: request.session?.sessionId ?? 'session-iv-screen-install'
        },
        workflow: ivScreenWorkflow
      }),
    stageIvScreenUninstall: (request) =>
      Promise.resolve({
        diagnostics: [
          {
            message: 'IV Screen uninstall is staged for change-plan review.',
            severity: 'info'
          }
        ],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.ivScreen',
              field: 'uninstall',
              newValue: 'true',
              recordId: 'iv-screen-v1-uninstall',
              sources: [
                {
                  layer: 'layered',
                  relativePath: 'exefs/main'
                },
                {
                  layer: 'base',
                  relativePath: 'exefs/main'
                }
              ],
              summary: 'Stage IV Screen uninstall.'
            }
          ],
          sessionId: request.session?.sessionId ?? 'session-iv-screen-uninstall'
        },
        workflow: {
          ...ivScreenWorkflow,
          installMessage: 'IV Screen is installed.',
          installStatus: 'installed',
          provenance: {
            fileState: 'layeredOverride',
            sourceFile: 'exefs/main',
            sourceLayer: 'layered'
          }
        }
      }),
    loadExeFsPatchWorkflow: () =>
      Promise.resolve({
        workflow: exeFsPatchWorkflow
      }),
    stageExeFsPatch: (request) =>
      Promise.resolve({
        diagnostics: [
          {
            message: 'ExeFS patch is staged for change-plan review.',
            severity: 'info'
          }
        ],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.exefsPatches',
              field: 'patchId',
              newValue: 'exefs/main',
              recordId: request.patchId,
              sources: [
                {
                  layer: 'base',
                  relativePath: 'exefs/main'
                }
              ],
              summary: 'Stage ExeFS patch: ExeFS main compatibility.'
            }
          ],
          sessionId: request.session?.sessionId ?? 'session-exefs'
        },
        workflow: exeFsPatchWorkflow
      }),
    loadRoyalCandyWorkflow: () =>
      Promise.resolve({
        workflow: {
          checks: [
            {
              area: 'RomFS',
              checkId: 'royal-candy-preflight:item-data',
              message: 'Item data found.',
              provenance: {
                fileState: 'baseOnly',
                sourceFile: 'romfs/bin/pml/item/item.dat',
                sourceLayer: 'base'
              },
              status: 'Pass',
              target: 'romfs/bin/pml/item/item.dat',
              workflowId: 'royal-candy-preflight'
            },
            {
              area: 'ExeFS',
              checkId: 'royal-candy-preflight:exefs:exefs-main-compatibility:patch-code-cave',
              message: 'Patch code cave: expected 0xC bytes, actual text+0x7BC338.',
              provenance: {
                fileState: 'baseOnly',
                sourceFile: 'exefs/main',
                sourceLayer: 'base'
              },
              status: 'Pass',
              target: '.text text+0x7BC338',
              workflowId: 'royal-candy-preflight'
            }
          ],
          diagnostics: [],
          outputs: [
            {
              description: 'Royal Candy item row patch.',
              outputId: 'royal-candy-unlimited:romfs/bin/pml/item/item.dat',
              outputKind: 'RomFS data',
              provenance: {
                fileState: 'baseOnly',
                sourceFile: 'romfs/bin/pml/item/item.dat',
                sourceLayer: 'base'
              },
              relativePath: 'romfs/bin/pml/item/item.dat',
              sourceFile: 'romfs/bin/pml/item/item.dat',
              status: canEdit ? 'ready' : 'readOnly',
              workflowId: 'royal-candy-unlimited'
            },
            {
              description: 'Royal Candy ExeFS UI and usage patch.',
              outputId: 'royal-candy-unlimited:exefs/main',
              outputKind: 'ExeFS NSO',
              provenance: {
                fileState: 'baseOnly',
                sourceFile: 'exefs/main',
                sourceLayer: 'base'
              },
              relativePath: 'exefs/main',
              sourceFile: 'exefs/main',
              status: canEdit ? 'ready' : 'readOnly',
              workflowId: 'royal-candy-unlimited'
            }
          ],
          stats: {
            failCount: 0,
            outputCount: 2,
            passCount: 2,
            sourceFileCount: 2,
            totalCheckCount: 2,
            totalStepCount: 4,
            totalWorkflowCount: 2,
            warningCount: 0
          },
          summary: royalCandyWorkflowSummary,
          workflows: [
            {
              category: 'Build',
              description: 'Prepares Royal Candy item 1128 from Rare Candy item 50.',
              itemId: 1128,
              levelCaps: [],
              mode: 'unlimited',
              name: 'Unlimited Royal Candy',
              provenance: {
                fileState: 'baseOnly',
                sourceFile: 'romfs/bin/pml/item/item.dat',
                sourceLayer: 'base'
              },
              status: canEdit ? 'available' : 'readOnly',
              steps: [
                {
                  description: 'Resolve required RomFS files and ExeFS inputs.',
                  label: 'Validate sources',
                  step: 1
                },
                {
                  description: 'Review generated output targets before apply.',
                  label: 'Review LayeredFS output',
                  step: 2
                }
              ],
              target: 'RomFS + ExeFS LayeredFS',
              templateItemId: 50,
              workflowId: 'royal-candy-unlimited'
            },
            {
              category: 'Build',
              description: 'Prepares Royal Candy item 1128 with story-cap checks.',
              itemId: 1128,
              levelCaps: royalCandyLevelCaps,
              mode: 'storyLimits',
              name: 'Royal Candy with Story Limits',
              provenance: {
                fileState: 'baseOnly',
                sourceFile: 'romfs/bin/pml/item/item.dat',
                sourceLayer: 'base'
              },
              status: canEdit ? 'available' : 'readOnly',
              steps: [
                {
                  description: 'Use story-cap flag milestones before enabling higher levels.',
                  label: 'Apply story limits',
                  step: 1
                },
                {
                  description: 'Review generated output targets before apply.',
                  label: 'Review LayeredFS output',
                  step: 2
                }
              ],
              target: 'RomFS + ExeFS LayeredFS',
              templateItemId: 50,
              workflowId: 'royal-candy-story-limits'
            }
          ]
        }
      }),
    stageRoyalCandyWorkflow: (request) =>
      Promise.resolve({
        diagnostics: [
          {
            message: 'Royal Candy workflow is staged for change-plan review.',
            severity: 'info'
          }
        ],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.royalCandy',
              field: 'workflowId',
              newValue:
                request.workflowId === 'royal-candy-story-limits'
                  ? `storyLimits|${(request.levelCaps ?? [])
                      .map((levelCap) => `${levelCap.slot}=${levelCap.levelCap}`)
                      .join(';')}`
                  : 'unlimited',
              recordId: request.workflowId,
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/pml/item/item.dat'
                }
              ],
              summary:
                request.workflowId === 'royal-candy-story-limits'
                  ? 'Stage Royal Candy workflow: Royal Candy with Story Limits.'
                  : 'Stage Royal Candy workflow: Unlimited Royal Candy.'
            }
          ],
          sessionId: request.session?.sessionId ?? 'session-royal-candy'
        },
        workflow: {
          checks: [
            {
              area: 'RomFS',
              checkId: 'royal-candy-preflight:item-data',
              message: 'Item data found.',
              provenance: {
                fileState: 'baseOnly',
                sourceFile: 'romfs/bin/pml/item/item.dat',
                sourceLayer: 'base'
              },
              status: 'Pass',
              target: 'romfs/bin/pml/item/item.dat',
              workflowId: 'royal-candy-preflight'
            }
          ],
          diagnostics: [],
          outputs: [
            {
              description: 'Royal Candy item row patch.',
              outputId: 'royal-candy-unlimited:romfs/bin/pml/item/item.dat',
              outputKind: 'RomFS data',
              provenance: {
                fileState: 'baseOnly',
                sourceFile: 'romfs/bin/pml/item/item.dat',
                sourceLayer: 'base'
              },
              relativePath: 'romfs/bin/pml/item/item.dat',
              sourceFile: 'romfs/bin/pml/item/item.dat',
              status: 'ready',
              workflowId: 'royal-candy-unlimited'
            }
          ],
          stats: {
            failCount: 0,
            outputCount: 1,
            passCount: 1,
            sourceFileCount: 1,
            totalCheckCount: 1,
            totalStepCount: 4,
            totalWorkflowCount: 2,
            warningCount: 0
          },
          summary: royalCandyWorkflowSummary,
          workflows: [
            {
              category: 'Build',
              description: 'Prepares Royal Candy item 1128 from Rare Candy item 50.',
              itemId: 1128,
              levelCaps: [],
              mode: 'unlimited',
              name: 'Unlimited Royal Candy',
              provenance: {
                fileState: 'baseOnly',
                sourceFile: 'romfs/bin/pml/item/item.dat',
                sourceLayer: 'base'
              },
              status: 'available',
              steps: [
                {
                  description: 'Resolve required RomFS files and ExeFS inputs.',
                  label: 'Validate sources',
                  step: 1
                },
                {
                  description: 'Review generated output targets before apply.',
                  label: 'Review LayeredFS output',
                  step: 2
                }
              ],
              target: 'RomFS + ExeFS LayeredFS',
              templateItemId: 50,
              workflowId: 'royal-candy-unlimited'
            },
            {
              category: 'Build',
              description: 'Prepares Royal Candy item 1128 with story-cap checks.',
              itemId: 1128,
              levelCaps: royalCandyLevelCaps,
              mode: 'storyLimits',
              name: 'Royal Candy with Story Limits',
              provenance: {
                fileState: 'baseOnly',
                sourceFile: 'romfs/bin/pml/item/item.dat',
                sourceLayer: 'base'
              },
              status: 'available',
              steps: [
                {
                  description: 'Use story-cap flag milestones before enabling higher levels.',
                  label: 'Apply story limits',
                  step: 1
                },
                {
                  description: 'Review generated output targets before apply.',
                  label: 'Review LayeredFS output',
                  step: 2
                }
              ],
              target: 'RomFS + ExeFS LayeredFS',
              templateItemId: 50,
              workflowId: 'royal-candy-story-limits'
            }
          ]
        }
      }),
    loadStartingItemsWorkflow: () =>
      Promise.resolve({
        workflow: startingItemsWorkflow
      }),
    stageStartingItems: (request) =>
      Promise.resolve({
        diagnostics: [
          {
            message: 'Starting Items grants are staged for change-plan review.',
            severity: 'info'
          }
        ],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.startingItems',
              field: 'grants',
              newValue: request.grants
                .filter((grant) => grant.itemId !== null)
                .map((grant) => `${grant.slot}:${grant.itemId}:${grant.quantity}`)
                .join(';'),
              recordId: 'starting-items',
              sources: [
                {
                  layer: 'layered',
                  relativePath: 'romfs/bin/script/amx/main_event_0020.amx'
                }
              ],
              summary: 'Stage Starting Items grants in Bag Hook slots 2-20.'
            }
          ],
          sessionId: request.session?.sessionId ?? 'session-starting-items'
        },
        workflow: startingItemsWorkflow
      }),
    loadSpreadsheetImportWorkflow: () =>
      Promise.resolve({
        workflow: spreadsheetImportWorkflow
      }),
    loadModMergerWorkflow: (request) =>
      Promise.resolve({
        workflow: {
          ...modMergerWorkflow,
          modDirectory1: request.modDirectory1,
          modDirectory2: request.modDirectory2
        }
      }),
    stageModMerge: (request) => {
      const preview = createModMergerPreview(
        request.selectedDirectory1Files,
        request.selectedDirectory2Files
      );

      return Promise.resolve({
        diagnostics: preview.diagnostics,
        preview,
        workflow: {
          ...modMergerWorkflow,
          modDirectory1: request.modDirectory1,
          modDirectory2: request.modDirectory2
        }
      });
    },
    applyModMerge: (request) => {
      const preview = createModMergerPreview(
        request.selectedDirectory1Files,
        request.selectedDirectory2Files
      );

      return Promise.resolve({
        diagnostics: preview.diagnostics,
        preview,
        workflow: {
          ...modMergerWorkflow,
          modDirectory1: request.modDirectory1,
          modDirectory2: request.modDirectory2
        },
        writtenFiles: preview.canApply ? preview.files.map((file) => file.relativePath) : []
      });
    },
    loadSvModMergerWorkflow: (request) =>
      Promise.resolve({
        workflow: createSvModMergerWorkflow(request.modSources, request.paths.outputRootPath)
      }),
    stageSvModMerge: (request) => {
      const preview = createSvModMergerPreview(request.modSources);

      return Promise.resolve({
        diagnostics: preview.diagnostics,
        preview,
        workflow: createSvModMergerWorkflow(request.modSources, request.paths.outputRootPath)
      });
    },
    applySvModMerge: (request) => {
      const preview = createSvModMergerPreview(request.modSources);

      return Promise.resolve({
        diagnostics: preview.diagnostics,
        preview,
        workflow: createSvModMergerWorkflow(request.modSources, request.paths.outputRootPath),
        writtenFiles: preview.canApply ? preview.files.map((file) => file.relativePath) : []
      });
    },
    importRandomizerSeed: (request) =>
      Promise.resolve({
        config: {
          options: {
            ...randomizerOptions,
            randomizePokemonStats: true
          },
          outputHash: 'mock-output',
          rollSeed: 'mock-roll',
          userSeed: 'mock-seed'
        },
        diagnostics: [],
        seed: request.seed
      }),
    applyRandomizer: (request) =>
      Promise.resolve({
        applyResult: {
          applyId: 'randomizer-apply-1',
          diagnostics: [
            {
              message: 'Randomizer applied selected output.',
              severity: 'info'
            }
          ],
          writtenFiles: request.config.options.randomizePokemonStats
            ? ['romfs/bin/pml/personal/personal_total.bin']
            : []
        },
        seed: `KM1-MOCK-${request.config.userSeed || 'generated'}`
      }),
    restoreRandomizer: () =>
      Promise.resolve({
        applyResult: {
          applyId: 'randomizer-restore-1',
          diagnostics: [
            {
              message: 'Restore Vanilla Values removed tracked Randomizer output.',
              severity: 'info'
            }
          ],
          writtenFiles: ['romfs/bin/pml/personal/personal_total.bin']
        }
      }),
    previewSpreadsheetImport: (request) =>
      Promise.resolve({
        diagnostics: [
          {
            message: 'Spreadsheet import preview accepted 1 row and rejected 0.',
            severity: 'info'
          }
        ],
        preview: {
          acceptedRowCount: 1,
          profileId: request.profileId,
          rejectedRowCount: 0,
          rows: [
            {
              cells: [
                {
                  field: 'itemId',
                  header: 'ItemId',
                  message: 'Potion',
                  status: 'accepted',
                  value: '1'
                },
                {
                  field: 'buyPrice',
                  header: 'BuyPrice',
                  message: 'Pending edit.',
                  status: 'accepted',
                  value: '450'
                }
              ],
              diagnostics: [],
              recordId: '1',
              rowNumber: 2,
              status: 'accepted',
              summary: 'Potion: Buy price -> 450.'
            }
          ],
          skippedRowCount: 0,
          sourcePath: request.sourcePath,
          totalRowCount: 1
        },
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.items',
              field: 'buyPrice',
              newValue: '450',
              recordId: '1',
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/pml/item/item.dat'
                }
              ],
              summary: 'Set Potion buy price to 450.'
            }
          ],
          sessionId: request.session?.sessionId ?? 'session-import'
        },
        workflow: spreadsheetImportWorkflow
      }),
    loadPlacementWorkflow: () =>
      Promise.resolve({
        workflow: placementWorkflow
      }),
    loadBehaviorWorkflow: () =>
      Promise.resolve({
        workflow: behaviorWorkflow
      }),
    loadRaidBattlesWorkflow: () =>
      Promise.resolve({
        workflow: raidBattlesWorkflow
      }),
    loadRaidRewardsWorkflow: () =>
      Promise.resolve({
        workflow: raidRewardsWorkflow
      }),
    loadRaidBonusRewardsWorkflow: () =>
      Promise.resolve({
        workflow: raidBonusRewardsWorkflow
      }),
    loadItemsWorkflow: () =>
      Promise.resolve({
        workflow: itemsWorkflow
      }),
    loadPokemonWorkflow: () =>
      Promise.resolve({
        workflow: pokemonWorkflow
      }),
    loadMovesWorkflow: () =>
      Promise.resolve({
        workflow: movesWorkflow
      }),
    loadTextWorkflow: () =>
      Promise.resolve({
        workflow: textWorkflow
      }),
    loadTrainersWorkflow: () =>
      Promise.resolve({
        workflow: trainersWorkflow
      }),
    loadGiftPokemonWorkflow: () =>
      Promise.resolve({
        workflow: currentGiftPokemonWorkflow
      }),
    loadTradePokemonWorkflow: () =>
      Promise.resolve({
        workflow: currentTradePokemonWorkflow
      }),
    loadStaticEncountersWorkflow: () =>
      Promise.resolve({
        workflow: staticEncountersWorkflow
      }),
    loadRentalPokemonWorkflow: () =>
      Promise.resolve({
        workflow: rentalPokemonWorkflow
      }),
    loadDynamaxAdventuresWorkflow: () =>
      Promise.resolve({
        workflow: dynamaxAdventuresWorkflow
      }),
    loadShopsWorkflow: () =>
      Promise.resolve({
        workflow: shopsWorkflow
      }),
    openProject: () =>
      Promise.resolve({
        fileGraph,
        health,
        projectId: 'project-1'
      }),
    refreshFileGraph: () => Promise.resolve({ fileGraph }),
    startEditSession: () =>
      Promise.resolve({
        session: {
          hasPendingChanges: false,
          pendingEdits: [],
          sessionId: 'session-1'
        }
      }),
    updateItemField: (request) => {
      const fieldLabels: Record<string, string> = {
        buyPrice: 'buy price',
        canUseOnPokemon: 'can use on Pokemon',
        evAttack: 'Attack EV gain',
        healAmount: 'heal amount',
        pouch: 'pouch',
        sellPrice: 'sell price',
        wattsPrice: 'Watts price',
        alternatePrice: 'alternate price'
      };
      const fieldLabel = fieldLabels[request.field] ?? request.field;

      return Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.items',
              field: request.field,
              newValue: request.value,
              recordId: request.itemId.toString(),
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/pml/item/item.dat'
                }
              ],
              summary: `Set Potion ${fieldLabel} to ${request.value}.`
            }
          ],
          sessionId: 'session-1'
        },
        workflow: {
          ...itemsWorkflow,
          items: itemsWorkflow.items.map((item) => {
            if (item.itemId !== request.itemId) {
              return item;
            }

            const value = Number.parseInt(request.value, 10);
            switch (request.field) {
              case 'sellPrice':
                return { ...item, buyPrice: value * 2, sellPrice: value };
              case 'wattsPrice':
                return { ...item, wattsPrice: value };
              case 'alternatePrice':
                return { ...item, alternatePrice: value };
              case 'pouch': {
                const metadata = { ...item.metadata, pouch: value };
                return {
                  ...item,
                  category: value === 4 ? 'Items' : item.category,
                  detailGroups: createItemDetailGroups(metadata),
                  metadata
                };
              }
              case 'healAmount': {
                const metadata = { ...item.metadata, healAmount: value };
                return {
                  ...item,
                  detailGroups: createItemDetailGroups(metadata),
                  metadata
                };
              }
              case 'evAttack':
                return { ...item, metadata: { ...item.metadata, evAttack: value } };
              case 'canUseOnPokemon':
                return {
                  ...item,
                  metadata: { ...item.metadata, canUseOnPokemon: value !== 0 }
                };
              default:
                return { ...item, buyPrice: value, sellPrice: Math.floor(value / 2) };
            }
          })
        }
      });
    },
    updatePokemonField: (request) =>
      Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.pokemon',
              field: request.field,
              newValue: request.value,
              recordId: request.personalId.toString(),
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/pml/personal/personal_total.bin'
                }
              ],
              summary:
                request.field.startsWith('compatibility:')
                  ? `${request.value === '1' ? 'Enable' : 'Disable'} Bulbasaur ${
                      getMockPokemonCompatibilityLabel(
                        pokemonWorkflow,
                        request.personalId,
                        request.field
                      ) ?? request.field
                    } compatibility.`
                  : request.field === 'canNotDynamax'
                  ? `Set Bulbasaur cannot dynamax to ${
                      request.value === '1' ? 'enabled' : 'disabled'
                    }.`
                  : `Set Bulbasaur ${request.field} to ${request.value}.`
            }
          ],
          sessionId: 'session-1'
        },
        workflow: {
          ...pokemonWorkflow,
          pokemon: pokemonWorkflow.pokemon.map((pokemon) => {
            if (pokemon.personalId !== request.personalId) {
              return pokemon;
            }

            const value = Number.parseInt(request.value, 10);
            if (request.field === 'hp') {
              const baseStats = {
                ...pokemon.baseStats,
                hp: value
              };

              return {
                ...pokemon,
                baseStats: {
                  ...baseStats,
                  total:
                    baseStats.hp +
                    baseStats.attack +
                    baseStats.defense +
                    baseStats.specialAttack +
                    baseStats.specialDefense +
                    baseStats.speed
                }
              };
            }

            if (request.field === 'canNotDynamax') {
              return {
                ...pokemon,
                personal: {
                  ...pokemon.personal,
                  canNotDynamax: value !== 0
                }
              };
            }

            if (request.field.startsWith('compatibility:')) {
              const [, groupId, slotText] = request.field.split(':');
              const slot = Number.parseInt(slotText ?? '', 10);
              const compatibility = pokemon.compatibility.map((group) => {
                if (group.groupId !== groupId) {
                  return group;
                }

                const entries = group.entries.map((entry) =>
                  entry.slot === slot ? { ...entry, canLearn: value !== 0 } : entry
                );

                return {
                  ...group,
                  enabledCount: entries.filter((entry) => entry.canLearn).length,
                  entries
                };
              });

              return {
                ...pokemon,
                compatibility
              };
            }

            return pokemon;
          })
        }
      }),
    updatePokemonLearnset: (request) =>
      Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.pokemon',
              field: `learnset:${request.action === 'add' ? 'upsert' : request.action}:${
                request.action === 'add'
                  ? pokemonWorkflow.pokemon.find((pokemon) => pokemon.personalId === request.personalId)
                      ?.learnset.length ?? 0
                  : request.slot ?? 0
              }`,
              newValue:
                request.moveId !== null && request.level !== null
                  ? `${request.moveId}:${request.level}`
                  : request.action === 'moveTo' && request.moveId !== null
                  ? request.moveId.toString()
                  : '1',
              recordId: request.personalId.toString(),
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/pml/waza_oboe/wazaoboe_total.bin'
                }
              ],
              summary:
                request.action === 'remove'
                  ? `Remove Bulbasaur learnset slot ${request.slot}.`
                  : request.action === 'moveUp'
                  ? `Move Bulbasaur learnset slot ${request.slot} up.`
                  : request.action === 'moveDown'
                  ? `Move Bulbasaur learnset slot ${request.slot} down.`
                  : request.action === 'moveTo'
                  ? `Move Bulbasaur learnset slot ${request.slot} to slot ${request.moveId}.`
                  : `Set Bulbasaur learnset slot ${request.slot ?? 0} to Lv. ${
                      request.level
                    } ${request.moveId === 345 ? 'Magical Leaf' : `Move ${request.moveId}`}.`
            }
          ],
          sessionId: 'session-1'
        },
        workflow: {
          ...pokemonWorkflow,
          pokemon: pokemonWorkflow.pokemon.map((pokemon) => {
            if (pokemon.personalId !== request.personalId) {
              return pokemon;
            }

            const learnset = [...pokemon.learnset];
            const keepLevelsWithSlots =
              request.action === 'moveUp' ||
              request.action === 'moveDown' ||
              request.action === 'moveTo';
            const slotLevels = keepLevelsWithSlots
              ? pokemon.learnset.map((move) => move.level)
              : [];
            const targetSlot = request.action === 'add' ? learnset.length : request.slot ?? 0;
            if (
              (request.action === 'upsert' || request.action === 'add') &&
              request.moveId !== null &&
              request.level !== null
            ) {
              const row = {
                level: request.level,
                moveId: request.moveId,
                moveName: request.moveId === 345 ? 'Magical Leaf' : `Move ${request.moveId}`,
                slot: targetSlot
              };
              if (targetSlot < learnset.length) {
                learnset[targetSlot] = row;
              } else {
                learnset.push(row);
              }
            } else if (request.action === 'remove' && targetSlot < learnset.length) {
              learnset.splice(targetSlot, 1);
            } else if (request.action === 'moveUp' && targetSlot > 0) {
              [learnset[targetSlot - 1], learnset[targetSlot]] = [
                learnset[targetSlot]!,
                learnset[targetSlot - 1]!
              ];
            } else if (request.action === 'moveDown' && targetSlot < learnset.length - 1) {
              [learnset[targetSlot + 1], learnset[targetSlot]] = [
                learnset[targetSlot]!,
                learnset[targetSlot + 1]!
              ];
            } else if (
              request.action === 'moveTo' &&
              targetSlot < learnset.length &&
              request.moveId !== null &&
              request.moveId < learnset.length
            ) {
              const [moved] = learnset.splice(targetSlot, 1);
              if (moved) {
                learnset.splice(request.moveId, 0, moved);
              }
            }

            return {
              ...pokemon,
              learnset: learnset.map((move, slot) => ({
                ...move,
                level: keepLevelsWithSlots ? slotLevels[slot] ?? move.level : move.level,
                slot
              }))
            };
          })
        }
      }),
    updatePokemonEvolution: (request) =>
      Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.pokemon',
              field: `evolution:${request.action === 'add' ? 'upsert' : request.action}:${
                request.action === 'add'
                  ? pokemonWorkflow.pokemon.find((pokemon) => pokemon.personalId === request.personalId)
                      ?.evolutions.length ?? 0
                  : request.slot ?? 0
              }`,
              newValue:
                request.method !== null &&
                request.argument !== null &&
                request.species !== null &&
                request.form !== null &&
                request.level !== null
                  ? `${request.method}:${request.argument}:${request.species}:${request.form}:${request.level}`
                  : '1',
              recordId: request.personalId.toString(),
              sources: [
                {
                  layer: 'base',
                  relativePath: `romfs/bin/pml/evolution/evo_${request.personalId
                    .toString()
                    .padStart(3, '0')}.bin`
                }
              ],
              summary:
                request.action === 'remove'
                  ? `Remove Bulbasaur evolution slot ${request.slot}.`
                  : request.action === 'moveUp'
                  ? `Move Bulbasaur evolution slot ${request.slot} up.`
                  : request.action === 'moveDown'
                  ? `Move Bulbasaur evolution slot ${request.slot} down.`
                  : `Set Bulbasaur evolution slot ${request.slot ?? 0} to species ${
                      request.species
                    } at level ${request.level}.`
            }
          ],
          sessionId: 'session-1'
        },
        workflow: {
          ...pokemonWorkflow,
          pokemon: pokemonWorkflow.pokemon.map((pokemon) => {
            if (pokemon.personalId !== request.personalId) {
              return pokemon;
            }

            const evolutions = [...pokemon.evolutions];
            const targetSlot = request.action === 'add' ? evolutions.length : request.slot ?? 0;
            if (
              (request.action === 'upsert' || request.action === 'add') &&
              request.method !== null &&
              request.argument !== null &&
              request.species !== null &&
              request.form !== null &&
              request.level !== null
            ) {
              const methodOption =
                pokemonWorkflow.evolutionMethodOptions.find(
                  (option) => option.value === request.method
                ) ?? null;
              const methodPrefix = request.method.toString().padStart(3, '0');
              const methodName =
                methodOption?.label.startsWith(`${methodPrefix} `)
                  ? methodOption.label.slice(methodPrefix.length + 1)
                  : methodOption?.label ?? `Method ${request.method}`;
              const argumentKind = methodOption?.argumentKind ?? 'value';
              const argumentValue =
                argumentKind === 'none' || argumentKind === 'level'
                  ? 'None'
                  : methodOption?.argumentOptions.find((option) => option.value === request.argument)
                      ?.label ?? request.argument.toString();
              const row = {
                argument: request.argument,
                argumentKind,
                argumentLabel: methodOption?.argumentLabel ?? 'Argument',
                argumentValue,
                form: request.form,
                level: request.level,
                method: request.method,
                methodName,
                slot: targetSlot,
                species: request.species
              };
              if (targetSlot < evolutions.length) {
                evolutions[targetSlot] = row;
              } else {
                evolutions.push(row);
              }
            } else if (request.action === 'remove' && targetSlot < evolutions.length) {
              evolutions.splice(targetSlot, 1);
            } else if (request.action === 'moveUp' && targetSlot > 0) {
              [evolutions[targetSlot - 1], evolutions[targetSlot]] = [
                evolutions[targetSlot]!,
                evolutions[targetSlot - 1]!
              ];
            } else if (request.action === 'moveDown' && targetSlot < evolutions.length - 1) {
              [evolutions[targetSlot + 1], evolutions[targetSlot]] = [
                evolutions[targetSlot]!,
                evolutions[targetSlot + 1]!
              ];
            }

            return {
              ...pokemon,
              evolutions: evolutions.map((evolution, slot) => ({ ...evolution, slot }))
            };
          })
        }
      }),
    updateMoveField: (request) =>
      Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.moves',
              field: request.field,
              newValue: request.value,
              recordId: request.moveId.toString(),
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/pml/waza/waza_033.bin'
                }
              ],
              summary:
                request.field === 'makesContact'
                  ? `Set Tackle makes contact to ${request.value === '1' ? 'enabled' : 'disabled'}.`
                  : `Set Tackle ${request.field} to ${request.value}.`
            }
          ],
          sessionId: 'session-1'
        },
        workflow: {
          ...movesWorkflow,
          moves: movesWorkflow.moves.map((move) => {
            if (move.moveId !== request.moveId) {
              return move;
            }

            const value = Number.parseInt(request.value, 10);
            if (request.field === 'makesContact') {
              return {
                ...move,
                flags: move.flags.map((flag) =>
                  flag.field === 'makesContact' ? { ...flag, enabled: value !== 0 } : flag
                )
              };
            }

            if (request.field === 'power') {
              return { ...move, power: value };
            }

            return move;
          })
        }
      }),
    updateTextEntry: (request) =>
      Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.text',
              field: 'value',
              newValue: request.value,
              recordId: request.textKey,
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/message/English/common/story.dat'
                }
              ],
              summary: `Set story #0 to "${request.value}".`
            }
          ],
          sessionId: 'session-1'
        },
        workflow: {
          ...textWorkflow,
          dialogueReferences: textWorkflow.dialogueReferences.map((reference) => ({
            ...reference,
            preview: request.value
          })),
          entries: textWorkflow.entries.map((entry) =>
            entry.textKey === request.textKey ? { ...entry, value: request.value } : entry
          )
        }
      }),
    updateTrainerField: (request) =>
      Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.trainers',
              field: request.field,
              newValue: request.value,
              recordId: request.slot === null ? request.trainerId.toString() : `${request.trainerId}:${request.slot}`,
              sources: [
                {
                  layer: 'base',
                  relativePath:
                    request.slot === null
                      ? 'romfs/bin/trainer/trainer_data/trainer_010.bin'
                      : 'romfs/bin/trainer/trainer_poke/trainer_010.bin'
                }
              ],
              summary:
                request.slot === null
                  ? `Set Avery ${request.field} to ${request.value}.`
                  : `Set Avery slot ${request.slot} level to ${request.value}.`
            }
          ],
          sessionId: 'session-1'
        },
        workflow: {
          ...trainersWorkflow,
          trainers: trainersWorkflow.trainers.map((trainer) =>
            trainer.trainerId === request.trainerId
              ? {
                  ...trainer,
                  team: trainer.team.map((pokemon) =>
                    pokemon.slot === request.slot
                      ? { ...pokemon, level: Number.parseInt(request.value, 10) }
                      : pokemon
                  )
                }
              : trainer
            )
        }
      }),
    updateGiftPokemonField: (request) => {
      const value = Number.parseInt(request.value, 10);
      const ivKey = ivFieldToKey[request.field as keyof typeof ivFieldToKey] ?? null;
      const pendingEdit = {
        domain: 'workflow.giftPokemon',
        field: request.field,
        newValue: request.value,
        recordId: `gift:${request.giftIndex}`,
        sources: [
          {
            layer: 'base' as const,
            relativePath: 'romfs/bin/script_event_data/add_poke.bin'
          }
        ],
        summary: `Set Gift 001 ${request.field} to ${request.value}.`
      };
      const pendingEdits = [...(request.session?.pendingEdits ?? []), pendingEdit];
      currentGiftPokemonWorkflow = {
        ...currentGiftPokemonWorkflow,
        gifts: currentGiftPokemonWorkflow.gifts.map((gift) =>
          gift.giftIndex === request.giftIndex
            ? {
                ...gift,
                ivs: ivKey
                  ? {
                      ...gift.ivs,
                      [ivKey]: value
                    }
                  : gift.ivs,
                ivSummary: ivKey
                  ? `HP ${ivKey === 'hp' ? value : gift.ivs.hp} / Atk ${
                      ivKey === 'attack' ? value : gift.ivs.attack
                    } / Def ${ivKey === 'defense' ? value : gift.ivs.defense} / SpA ${
                      ivKey === 'specialAttack' ? value : gift.ivs.specialAttack
                    } / SpD ${
                      ivKey === 'specialDefense' ? value : gift.ivs.specialDefense
                    } / Spe ${ivKey === 'speed' ? value : gift.ivs.speed}`
                  : gift.ivSummary,
                level: request.field === 'level' ? value : gift.level,
                shinyLock: request.field === 'shinyLock' ? value : gift.shinyLock,
                shinyLockLabel: request.field === 'shinyLock' ? 'Random' : gift.shinyLockLabel
              }
            : gift
        )
      };

      return Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits,
          sessionId: 'session-1'
        },
        workflow: currentGiftPokemonWorkflow
      });
    },
    updateTradePokemonField: (request) => {
      const value = Number.parseInt(request.value, 10);
      const ivKey = ivFieldToKey[request.field as keyof typeof ivFieldToKey] ?? null;
      const pendingEdit = {
        domain: 'workflow.tradePokemon',
        field: request.field,
        newValue: request.value,
        recordId: `trade:${request.tradeIndex}`,
        sources: [
          {
            layer: 'base' as const,
            relativePath: 'romfs/bin/script_event_data/field_trade.bin'
          }
        ],
        summary: `Set Trade 001 ${request.field} to ${request.value}.`
      };
      const pendingEdits = [...(request.session?.pendingEdits ?? []), pendingEdit];
      currentTradePokemonWorkflow = {
        ...currentTradePokemonWorkflow,
        trades: currentTradePokemonWorkflow.trades.map((trade) =>
          trade.tradeIndex === request.tradeIndex
            ? {
                ...trade,
                ivs: ivKey
                  ? {
                      ...trade.ivs,
                      [ivKey]: value
                    }
                  : trade.ivs,
                ivSummary: ivKey
                  ? `HP ${ivKey === 'hp' ? value : trade.ivs.hp} / Atk ${
                      ivKey === 'attack' ? value : trade.ivs.attack
                    } / Def ${ivKey === 'defense' ? value : trade.ivs.defense} / SpA ${
                      ivKey === 'specialAttack' ? value : trade.ivs.specialAttack
                    } / SpD ${
                      ivKey === 'specialDefense' ? value : trade.ivs.specialDefense
                    } / Spe ${ivKey === 'speed' ? value : trade.ivs.speed}`
                  : trade.ivSummary,
                level: request.field === 'level' ? value : trade.level,
                shinyLock: request.field === 'shinyLock' ? value : trade.shinyLock,
                shinyLockLabel: request.field === 'shinyLock' ? 'Random' : trade.shinyLockLabel
              }
            : trade
        )
      };

      return Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits,
          sessionId: 'session-1'
        },
        workflow: currentTradePokemonWorkflow
      });
    },
    updateStaticEncounterField: (request) => {
      const value = Number.parseInt(request.value, 10);

      return Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.staticEncounters',
              field: request.field,
              newValue: request.value,
              recordId: `static:${request.encounterIndex}`,
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/script_event_data/event_encount_data.bin'
                }
              ],
              summary: `Set Static 001 ${request.field} to ${request.value}.`
            }
          ],
          sessionId: 'session-1'
        },
        workflow: {
          ...staticEncountersWorkflow,
          encounters: staticEncountersWorkflow.encounters.map((encounter) =>
            encounter.encounterIndex === request.encounterIndex
              ? {
                  ...encounter,
                  ivs:
                    request.field === 'ivHp'
                      ? {
                          ...encounter.ivs,
                          hp: value
                        }
                      : encounter.ivs,
                  ivSummary:
                    request.field === 'ivHp'
                      ? `HP ${value} / Atk 30 / Def 29 / SpA 27 / SpD 26 / Spe 28`
                      : encounter.ivSummary,
                  level: request.field === 'level' ? value : encounter.level,
                  shinyLock: request.field === 'shinyLock' ? value : encounter.shinyLock,
                  shinyLockLabel:
                    request.field === 'shinyLock' ? 'Random' : encounter.shinyLockLabel
                }
              : encounter
          )
        }
      });
    },
    updateRentalPokemonField: (request) => {
      const value = Number.parseInt(request.value, 10);

      return Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.rentalPokemon',
              field: request.field,
              newValue: request.value,
              recordId: `rental:${request.rentalIndex}`,
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/script_event_data/rental.bin'
                }
              ],
              summary: `Set Rental 001 ${request.field} to ${request.value}.`
            }
          ],
          sessionId: 'session-1'
        },
        workflow: {
          ...rentalPokemonWorkflow,
          rentals: rentalPokemonWorkflow.rentals.map((rental) =>
            rental.rentalIndex === request.rentalIndex
              ? {
                  ...rental,
                  evs:
                    request.field === 'evHp'
                      ? {
                          ...rental.evs,
                          hp: value
                        }
                      : rental.evs,
                  ivs:
                    request.field === 'ivHp'
                      ? {
                          ...rental.ivs,
                          hp: value
                        }
                      : rental.ivs,
                  ivSummary:
                    request.field === 'ivHp'
                      ? `HP ${value} / Atk 31 / Def 31 / SpA 31 / SpD 31 / Spe 31`
                      : rental.ivSummary,
                  level: request.field === 'level' ? value : rental.level
                }
              : rental
          )
        }
      });
    },
    updateDynamaxAdventureField: (request) => {
      const value = Number.parseInt(request.value, 10);
      const recordId = `dynamaxAdventure:${request.entryIndex}`;
      const pendingEdit = {
        domain: 'workflow.dynamaxAdventures',
        field: request.field,
        newValue: request.value,
        recordId,
        sources: [
          {
            layer: 'base' as const,
            relativePath:
              'romfs/bin/appli/chika/data_table/underground_exploration_poke.bin'
          }
        ],
        summary: `Set Adventure 001 ${request.field} to ${request.value}.`
      };
      const pendingEdits = [
        ...(request.session?.pendingEdits.filter(
          (edit) =>
            !(
              edit.domain === pendingEdit.domain &&
              edit.recordId === pendingEdit.recordId &&
              edit.field === pendingEdit.field
            )
        ) ?? []),
        pendingEdit
      ];
      const speciesName =
        value === 1 ? 'Bulbasaur' : value === 810 ? 'Grookey' : `Species ${value}`;
      const moveName = value === 1 ? 'Scratch' : value === 2 ? 'Growl' : 'None';

      return Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits,
          sessionId: 'session-1'
        },
        workflow: {
          ...dynamaxAdventuresWorkflow,
          encounters: dynamaxAdventuresWorkflow.encounters.map((encounter) =>
            encounter.entryIndex === request.entryIndex
              ? {
                  ...encounter,
                  ability: request.field === 'ability' ? value : encounter.ability,
                  abilityLabel:
                    request.field === 'ability'
                      ? value === 2
                        ? 'Hidden Ability'
                        : value === 4
                          ? 'Any Ability'
                          : 'Ability 1'
                      : encounter.abilityLabel,
                  form: request.field === 'form' ? value : encounter.form,
                  gigantamaxLabel:
                    request.field === 'gigantamaxState'
                      ? value === 2
                        ? 'Gigantamax'
                        : 'Normal'
                      : encounter.gigantamaxLabel,
                  gigantamaxState:
                    request.field === 'gigantamaxState' ? value : encounter.gigantamaxState,
                  guaranteedPerfectIvs:
                    request.field === 'guaranteedPerfectIvs'
                      ? value
                      : encounter.guaranteedPerfectIvs,
                  ivs:
                    request.field === 'guaranteedPerfectIvs'
                      ? {
                          ...encounter.ivs,
                          hp: value === 0 ? -1 : -value
                        }
                      : request.field === 'ivAttack'
                      ? {
                          ...encounter.ivs,
                          attack: value
                        }
                      : request.field === 'ivDefense'
                        ? {
                            ...encounter.ivs,
                            defense: value
                          }
                      : request.field === 'ivSpecialAttack'
                        ? {
                            ...encounter.ivs,
                            specialAttack: value
                          }
                      : request.field === 'ivSpecialDefense'
                        ? {
                            ...encounter.ivs,
                            specialDefense: value
                          }
                      : request.field === 'ivSpeed'
                        ? {
                            ...encounter.ivs,
                            speed: value
                          }
                      : encounter.ivs,
                  ivSummary:
                    request.field === 'guaranteedPerfectIvs'
                      ? `${value} guaranteed perfect / Atk Random / Def Random / SpA Random / SpD Random / Spe Random`
                      : encounter.ivSummary,
                  label:
                    request.field === 'species'
                      ? `Adventure 001: ${speciesName} Lv. ${encounter.level}`
                      : encounter.label,
                  level: request.field === 'level' ? value : encounter.level,
                  moves: request.field.startsWith('move')
                    ? encounter.moves.map((move) =>
                        request.field === `move${move.slot}Id`
                          ? {
                              ...move,
                              move: moveName,
                              moveId: value
                            }
                          : move
                      )
                    : encounter.moves,
                  species: request.field === 'species' ? speciesName : encounter.species,
                  speciesId: request.field === 'species' ? value : encounter.speciesId
                }
              : encounter
          )
        }
      });
    },
    updateShopInventoryItem: (request) => {
      const orderedItemIds =
        request.field === 'setInventory'
          ? request.value
              .split(',')
              .filter((value) => value.length > 0)
              .map((value) => Number.parseInt(value, 10))
          : null;
      const formatItem = (itemId: number, slot: number) => {
        const item = itemsWorkflow.items.find((candidate) => candidate.itemId === itemId);

        return {
          isKnownItem: item !== undefined,
          itemId,
          itemName: item?.name ?? `Item ${itemId}`,
          price: item?.buyPrice ?? 0,
          slot,
          stockLimit: null
        };
      };

      return Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.shops',
              field: request.field,
              newValue: request.value,
              recordId: `${request.shopId}#${request.slot}`,
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/appli/shop/bin/shop_data.bin'
                }
              ],
              summary:
                request.field === 'setInventory'
                  ? `Set Poke Mart inventory order to ${orderedItemIds?.length ?? 0} items.`
                  : `Set Poke Mart slot ${request.slot} item ID to ${request.value}.`
            }
          ],
          sessionId: 'session-1'
        },
        workflow: {
          ...shopsWorkflow,
          shops: shopsWorkflow.shops.map((shop) =>
            shop.shopId === request.shopId
              ? {
                  ...shop,
                  inventory:
                    orderedItemIds !== null
                      ? orderedItemIds.map((itemId, index) => formatItem(itemId, index + 1))
                      : shop.inventory.map((item) =>
                          item.slot === request.slot
                            ? formatItem(Number.parseInt(request.value, 10), item.slot)
                            : item
                        )
                }
              : shop
          )
        }
      });
    },
    updateEncounterSlotField: (request) =>
      Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.encounters',
              field: request.field,
              newValue: request.value,
              recordId: `${request.tableId}#${request.slot}`,
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/archive/field/resident/data_table.gfpak'
                }
              ],
              summary: `Set Sword Symbol Zone 0x1122334455667788 Normal slot ${request.slot} probability to ${request.value}.`
            }
          ],
          sessionId: 'session-1'
        },
        workflow: {
          ...encountersWorkflow,
          tables: encountersWorkflow.tables.map((table) =>
            table.tableId === request.tableId
              ? {
                  ...table,
                  slots: table.slots.map((slot) =>
                    slot.slot === request.slot
                      ? { ...slot, weight: Number.parseInt(request.value, 10) }
                      : slot
                  )
                }
              : table
          )
        }
      }),
    updateRaidBattleSlotField: (request) =>
      Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.raidBattles',
              field: request.field,
              newValue: request.value,
              recordId: `${request.tableId}#${request.slot}`,
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/archive/field/resident/data_table.gfpak'
                }
              ],
              summary: `Set Raid Battles 0xAABBCCDD00112233 slot ${request.slot} ${request.field} to ${request.value}.`
            }
          ],
          sessionId: 'session-1'
        },
        workflow: {
          ...raidBattlesWorkflow,
          tables: raidBattlesWorkflow.tables.map((table) =>
            table.tableId === request.tableId
              ? {
                  ...table,
                  slots: table.slots.map((slot) =>
                    slot.slot === request.slot
                      ? {
                          ...slot,
                          flawlessIvs:
                            request.field === 'flawlessIvs'
                              ? Number.parseInt(request.value, 10)
                              : slot.flawlessIvs,
                          probabilities: slot.probabilities.map((value, index) =>
                            request.field === 'star5Probability' && index === 4
                              ? Number.parseInt(request.value, 10)
                              : value
                          )
                        }
                      : slot
                  )
                }
              : table
          )
        }
      }),
    updateRaidRewardField: (request) =>
      Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.raidRewards',
              field: request.field,
              newValue: request.value,
              recordId: `${request.tableId}#${request.slot}`,
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/archive/field/resident/data_table.gfpak'
                }
              ],
              summary: `Set Drop 0xAABBCCDD00112233 slot ${request.slot} 5-star drop chance to ${request.value}.`
            }
          ],
          sessionId: 'session-1'
        },
        workflow: {
          ...raidRewardsWorkflow,
          tables: raidRewardsWorkflow.tables.map((table) =>
            table.tableId === request.tableId
              ? {
                  ...table,
                  rewards: table.rewards.map((reward) =>
                    reward.slot === request.slot
                      ? {
                          ...reward,
                          values: reward.values.map((value, index) =>
                            request.field === 'star5Value' && index === 4
                              ? Number.parseInt(request.value, 10)
                              : value
                          )
                        }
                      : reward
                  )
                }
              : table
          )
        }
      }),
    updateRaidBonusRewardField: (request) =>
      Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.raidBonusRewards',
              field: request.field,
              newValue: request.value,
              recordId: `${request.tableId}#${request.slot}`,
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/archive/field/resident/data_table.gfpak'
                }
              ],
              summary: `Set Bonus 0x1020304050607080 slot ${request.slot} 5-star quantity to ${request.value}.`
            }
          ],
          sessionId: 'session-1'
        },
        workflow: {
          ...raidBonusRewardsWorkflow,
          tables: raidBonusRewardsWorkflow.tables.map((table) =>
            table.tableId === request.tableId
              ? {
                  ...table,
                  rewards: table.rewards.map((reward) =>
                    reward.slot === request.slot
                      ? {
                          ...reward,
                          values: reward.values.map((value, index) =>
                            request.field === 'star5Value' && index === 4
                              ? Number.parseInt(request.value, 10)
                              : value
                          )
                        }
                      : reward
                  )
                }
              : table
          )
        }
      }),
    updatePlacementObjectField: (request) =>
      Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.placement',
              field: request.field,
              newValue: request.value,
              recordId: request.objectId,
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/archive/field/resident/placement.gfpak'
                }
              ],
              summary: `Set Field item: Potion ${request.field} to ${request.value}.`
            }
          ],
          sessionId: 'session-1'
        },
        workflow: {
          ...placementWorkflow,
          objects: placementWorkflow.objects.map((placedObject) =>
            placedObject.objectId === request.objectId
              ? { ...placedObject, quantity: Number.parseInt(request.value, 10) }
              : placedObject
          )
        }
      }),
    updateBehaviorEntryField: (request) =>
      Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.behavior',
              field: request.field,
              newValue: request.value,
              recordId: request.entryId,
              sources: [
                {
                  layer: 'base',
                  relativePath:
                    'romfs/bin/field/param/symbol_encount_mons_param/symbol_encount_mons_param.bin'
                }
              ],
              summary: `Set Pikachu ${request.field} to ${request.value}.`
            }
          ],
          sessionId: 'session-1'
        },
        workflow: {
          ...behaviorWorkflow,
          entries: behaviorWorkflow.entries.map((entry) =>
            entry.entryId === request.entryId
              ? {
                  ...entry,
                  behavior:
                    request.field === 'behavior'
                      ? request.value
                      : entry.behavior,
                  behaviorLabel:
                    request.field === 'behavior'
                      ? request.value
                      : entry.behaviorLabel,
                  fields: entry.fields.map((field) =>
                    field.field === request.field ? { ...field, value: request.value } : field
                  ),
                  speciesId:
                    request.field === 'speciesId'
                      ? Number.parseInt(request.value, 10)
                      : entry.speciesId
                }
              : entry
          )
        }
      }),
    validateEditSession: (request) =>
      Promise.resolve({
        diagnostics: [
          {
            field: request.session.pendingEdits[0]?.field ?? 'value',
            message: getValidationMessage(request.session.pendingEdits[0]?.domain),
            severity: 'info'
          }
        ],
        isValid: true,
        session: request.session
      }),
    validateProject: () => Promise.resolve({ health }),
    ...overrides
  };
}

function wrongGameHealthDiagnostic() {
  return [
    {
      domain: 'project',
      expected: '0x01008DB008C2C000 for Pokemon Shield',
      file: 'base-exefs',
      message:
        'Selected Pokemon Shield, but Base ExeFS contains Pokemon Sword title id 0x0100ABF008968000.',
      severity: 'error' as const
    }
  ];
}

export function createWrongGameHealth(): ProjectHealth {
  return {
    canOpenEditableWorkflows: false,
    canOpenReadOnlyWorkflows: false,
    diagnostics: wrongGameHealthDiagnostic(),
    fileGraph: {
      baseFileCount: 2,
      layeredFileCount: 0,
      layeredOnlyCount: 0,
      overrideCount: 0
    },
    paths: [
      {
        diagnostics: [],
        isRequired: true,
        path: 'base-romfs',
        role: 'baseRomFs',
        status: 'valid'
      },
      {
        diagnostics: wrongGameHealthDiagnostic(),
        isRequired: true,
        path: 'base-exefs',
        role: 'baseExeFs',
        status: 'unsafe'
      }
    ],
    state: 'blocked'
  };
}

function getApplyMessage(targetRelativePath: string, domain: string | undefined) {
  if (targetRelativePath.includes('/message/')) {
    return 'Applied Text change plan to the configured LayeredFS output root.';
  }

  if (targetRelativePath.includes('/trainer/')) {
    return 'Applied Trainers change plan to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.giftPokemon') {
    return 'Applied Gift Pokemon change plan to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.tradePokemon') {
    return 'Applied Trade Pokemon change plan to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.staticEncounters') {
    return 'Applied Static Encounter change plan to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.rentalPokemon') {
    return 'Applied Rental Pokemon change plan to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.dynamaxAdventures') {
    return 'Applied Dynamax Adventures change plan to the configured LayeredFS output root.';
  }

  if (targetRelativePath.includes('/shop/')) {
    return 'Applied Shops change plan to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.raidBattles') {
    return 'Applied Raid Battles change plan to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.raidRewards') {
    return 'Applied Raid Rewards change plan to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.moves') {
    return 'Applied Moves change plan to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.pokemon') {
    return 'Applied Pokemon change plan to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.royalCandy') {
    return 'Applied Royal Candy change plan to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.bagHook') {
    return 'Installed Bag Hook V2 to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.catchCap') {
    return 'Applied Catch Cap Editor changes to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.hyperTraining') {
    return 'Applied Hyper Training changes to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.gymUniformRemoval') {
    return 'Applied Gym Uniform Removal changes to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.ivScreen') {
    return 'Applied IV Screen changes to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.startingItems') {
    return 'Applied Starting Items grants to Bag Hook slots 2-20 in the configured LayeredFS output root.';
  }

  if (domain === 'workflow.exefsPatches') {
    return 'Applied ExeFS patch to the configured LayeredFS output root.';
  }

  if (targetRelativePath.includes('/archive/field/resident/')) {
    return 'Applied Wild Encounters change plan to the configured LayeredFS output root.';
  }

  return 'Applied Items change plan to the configured LayeredFS output root.';
}

function getMockPokemonCompatibilityLabel(
  workflow: PokemonWorkflow,
  personalId: number,
  field: string
) {
  const [, groupId, slotText] = field.split(':');
  const slot = Number.parseInt(slotText ?? '', 10);
  const pokemon = workflow.pokemon.find((record) => record.personalId === personalId);

  return pokemon?.compatibility
    .find((group) => group.groupId === groupId)
    ?.entries
    .find((entry) => entry.slot === slot)
    ?.label;
}

function getValidationMessage(domain: string | undefined) {
  switch (domain) {
    case 'workflow.text':
      return 'Pending text change is valid.';
    case 'workflow.trainers':
      return 'Pending trainer change is valid.';
    case 'workflow.giftPokemon':
      return 'Pending gift Pokemon change is valid.';
    case 'workflow.tradePokemon':
      return 'Pending trade Pokemon change is valid.';
    case 'workflow.staticEncounters':
      return 'Pending static encounter change is valid.';
    case 'workflow.rentalPokemon':
      return 'Pending rental Pokemon change is valid.';
    case 'workflow.dynamaxAdventures':
      return 'Pending Dynamax Adventure change is valid.';
    case 'workflow.shops':
      return 'Pending shop change is valid.';
    case 'workflow.encounters':
      return 'Pending encounter change is valid.';
    case 'workflow.raidBattles':
      return 'Pending raid battle change is valid.';
    case 'workflow.raidRewards':
      return 'Pending raid reward change is valid.';
    case 'workflow.moves':
      return 'Pending move change is valid.';
    case 'workflow.bagHook':
      return 'Pending Bag Hook install is valid for change-plan review.';
    case 'workflow.catchCap':
      return 'Pending Catch Cap Editor values are valid for change-plan review.';
    case 'workflow.hyperTraining':
      return 'Pending Hyper Training change is valid for change-plan review.';
    case 'workflow.gymUniformRemoval':
      return 'Pending Gym Uniform Removal change is valid for change-plan review.';
    case 'workflow.ivScreen':
      return 'Pending IV Screen change is valid for change-plan review.';
    case 'workflow.royalCandy':
      return 'Pending Royal Candy workflow is valid.';
    case 'workflow.startingItems':
      return 'Pending Starting Items grants are valid for change-plan review.';
    case 'workflow.exefsPatches':
      return 'Pending ExeFS patch is valid.';
    default:
      return 'Pending item change is valid.';
  }
}
