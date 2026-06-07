/* SPDX-License-Identifier: GPL-3.0-only */

import {
  Activity,
  CheckCircle,
  ClipboardCheck,
  FileSpreadsheet,
  FolderOpen,
  Layers,
  ListChecks,
  MapPin,
  Package,
  Pencil,
  RefreshCw,
  Save,
  Search,
  ShieldCheck,
  Wrench,
  type LucideIcon
} from 'lucide-react';
import { useEffect, useState } from 'react';
import {
  type ApiDiagnostic,
  type ApplyResult,
  type ChangePlan,
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
  type ItemEditableField,
  type ItemsWorkflow,
  type ItemRecord,
  type PlacedObjectRecord,
  type PlacementEditableField,
  type PlacementWorkflow,
  type ProjectHealth,
  type ProjectPathRole,
  type ProjectPathValidation,
  type RaidRewardEditableField,
  type RaidRewardItemRecord,
  type RaidRewardTableRecord,
  type RaidRewardsWorkflow,
  type SaveBlockRecord,
  type ShopEditableField,
  type ShopInventoryRecord,
  type ShopRecord,
  type ShopsWorkflow,
  type TextEditableField,
  type TextEntryRecord,
  type TextWorkflow,
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
  type ProjectPathDraft,
  type WorkbenchSection,
  useWorkbenchStore
} from './workbenchStore';

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
    id: 'changes',
    label: 'Changes',
    icon: ClipboardCheck
  }
];

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
    description: 'Curated batch workflow recipes, targets, steps, and source provenance.',
    icon: CheckCircle
  },
  {
    id: 'spreadsheetImport',
    label: 'Spreadsheet Import Tooling',
    description: 'Spreadsheet import profiles, target workflows, columns, and source provenance.',
    icon: FileSpreadsheet
  }
];

const pathFields: Array<{
  field: keyof ProjectPathDraft;
  label: string;
  role: ProjectPathRole;
}> = [
  {
    field: 'baseRomFsPath',
    label: 'Base RomFS',
    role: 'baseRomFs'
  },
  {
    field: 'baseExeFsPath',
    label: 'Base ExeFS',
    role: 'baseExeFs'
  },
  {
    field: 'outputRootPath',
    label: 'Output Root',
    role: 'outputRoot'
  }
];

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
const battleTypeFieldName = 'battleType';
const speciesIdFieldName = 'speciesId';
const levelFieldName = 'level';
const heldItemIdFieldName = 'heldItemId';
const moveFieldNames = ['move1Id', 'move2Id', 'move3Id', 'move4Id'] as const;
const shopItemIdFieldName = 'itemId';
const encounterFormFieldName = 'form';
const encounterProbabilityFieldName = 'probability';
const encounterLevelMinFieldName = 'levelMin';
const encounterLevelMaxFieldName = 'levelMax';
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

export function App({ bridge = defaultProjectBridge }: { bridge?: ProjectBridge } = {}) {
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
  const itemSearchText = useWorkbenchStore((state) => state.itemSearchText);
  const itemsWorkflow = useWorkbenchStore((state) => state.itemsWorkflow);
  const openProject = useWorkbenchStore((state) => state.openProject);
  const placementSearchText = useWorkbenchStore((state) => state.placementSearchText);
  const placementWorkflow = useWorkbenchStore((state) => state.placementWorkflow);
  const projectStatus = useWorkbenchStore((state) => state.projectStatus);
  const raidRewardSearchText = useWorkbenchStore((state) => state.raidRewardSearchText);
  const raidRewardsWorkflow = useWorkbenchStore((state) => state.raidRewardsWorkflow);
  const selectedEncounterTableId = useWorkbenchStore((state) => state.selectedEncounterTableId);
  const selectedItemId = useWorkbenchStore((state) => state.selectedItemId);
  const selectedRaidRewardTableId = useWorkbenchStore(
    (state) => state.selectedRaidRewardTableId
  );
  const selectedPlacementObjectId = useWorkbenchStore(
    (state) => state.selectedPlacementObjectId
  );
  const selectedFlagId = useWorkbenchStore((state) => state.selectedFlagId);
  const selectedExeFsCheckId = useWorkbenchStore((state) => state.selectedExeFsCheckId);
  const selectedExeFsPatchId = useWorkbenchStore((state) => state.selectedExeFsPatchId);
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
  const setItemSearchText = useWorkbenchStore((state) => state.setItemSearchText);
  const setItemsWorkflow = useWorkbenchStore((state) => state.setItemsWorkflow);
  const setOpenProject = useWorkbenchStore((state) => state.setOpenProject);
  const setPlacementSearchText = useWorkbenchStore((state) => state.setPlacementSearchText);
  const setPlacementWorkflow = useWorkbenchStore((state) => state.setPlacementWorkflow);
  const setProjectHealth = useWorkbenchStore((state) => state.setProjectHealth);
  const setProjectStatus = useWorkbenchStore((state) => state.setProjectStatus);
  const setRaidRewardSearchText = useWorkbenchStore((state) => state.setRaidRewardSearchText);
  const setRaidRewardsWorkflow = useWorkbenchStore((state) => state.setRaidRewardsWorkflow);
  const setSelectedRaidRewardTableId = useWorkbenchStore(
    (state) => state.setSelectedRaidRewardTableId
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
  const setSelectedFlagId = useWorkbenchStore((state) => state.setSelectedFlagId);
  const setSelectedItemId = useWorkbenchStore((state) => state.setSelectedItemId);
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
  const [isTextLoading, setIsTextLoading] = useState(false);
  const [isTextUpdating, setIsTextUpdating] = useState(false);
  const [isTrainersLoading, setIsTrainersLoading] = useState(false);
  const [isTrainerUpdating, setIsTrainerUpdating] = useState(false);
  const [isShopsLoading, setIsShopsLoading] = useState(false);
  const [isShopUpdating, setIsShopUpdating] = useState(false);
  const [isEncountersLoading, setIsEncountersLoading] = useState(false);
  const [isEncounterUpdating, setIsEncounterUpdating] = useState(false);
  const [isRaidRewardsLoading, setIsRaidRewardsLoading] = useState(false);
  const [isRaidRewardUpdating, setIsRaidRewardUpdating] = useState(false);
  const [isPlacementLoading, setIsPlacementLoading] = useState(false);
  const [isPlacementUpdating, setIsPlacementUpdating] = useState(false);
  const [isFlagworkSaveLoading, setIsFlagworkSaveLoading] = useState(false);
  const [isExeFsPatchLoading, setIsExeFsPatchLoading] = useState(false);
  const [isChangePlanApplying, setIsChangePlanApplying] = useState(false);
  const [isChangePlanCreating, setIsChangePlanCreating] = useState(false);
  const [isSessionValidating, setIsSessionValidating] = useState(false);
  const pendingEditCount = editSession?.pendingEdits.length ?? 0;

  const handleValidateProject = async () => {
    setProjectStatus('validating');
    setBridgeDiagnostics([]);

    try {
      const paths = toProjectPaths(draftPaths);
      const response = await bridge.validateProject({ paths });
      setProjectHealth(response.health);
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
      await refreshWorkflows(paths, response.health.canOpenReadOnlyWorkflows);
      setActiveSection('health');
    } catch (error) {
      setProjectStatus('idle');
      setBridgeDiagnostics(toBridgeDiagnostics(error));
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

  const handleStartEditSession = async () => {
    setIsEditStarting(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);

    try {
      const response = await bridge.startEditSession({ paths: toProjectPaths(draftPaths) });
      setEditSession(response.session);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsEditStarting(false);
    }
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

  const handleValidateEditSession = async () => {
    if (!editSession) {
      return;
    }

    setIsSessionValidating(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);

    try {
      const response = await bridge.validateEditSession({
        paths: toProjectPaths(draftPaths),
        session: editSession
      });
      setEditSession(response.session);
      setEditValidationDiagnostics(response.diagnostics);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsSessionValidating(false);
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

    try {
      const response = await bridge.createChangePlan({
        paths: toProjectPaths(draftPaths),
        session: editSession
      });
      setChangePlan(response.changePlan);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsChangePlanCreating(false);
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
      const response = await bridge.applyChangePlan({
        changePlan,
        paths: toProjectPaths(draftPaths),
        session: editSession
      });
      const hasApplyErrors = response.applyResult.diagnostics.some(
        (diagnostic) => diagnostic.severity === 'error'
      );

      if (!hasApplyErrors) {
        setEditSession(null);
        setChangePlan(null);
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
          <Layers aria-hidden="true" size={24} />
          <span>KM Editor</span>
        </div>

        <nav aria-label="Workspace" className="section-nav">
          {sections.map((section) => {
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
        </header>

        <div className="workspace-content">
          {activeSection === 'health' ? (
            <HealthSection
              draftPaths={draftPaths}
              health={health}
              bridgeDiagnostics={bridgeDiagnostics}
              isBusy={isBusy}
              onOpenProject={handleOpenProject}
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
              isTextLoading={isTextLoading}
              isTrainersLoading={isTrainersLoading}
              isShopsLoading={isShopsLoading}
              isEncountersLoading={isEncountersLoading}
              isRaidRewardsLoading={isRaidRewardsLoading}
              isPlacementLoading={isPlacementLoading}
              isFlagworkSaveLoading={isFlagworkSaveLoading}
              isExeFsPatchLoading={isExeFsPatchLoading}
              onOpenEncountersWorkflow={handleOpenEncountersWorkflow}
              onOpenExeFsPatchWorkflow={handleOpenExeFsPatchWorkflow}
              onOpenFlagworkSaveWorkflow={handleOpenFlagworkSaveWorkflow}
              onOpenItemsWorkflow={handleOpenItemsWorkflow}
              onOpenPlacementWorkflow={handleOpenPlacementWorkflow}
              onOpenRaidRewardsWorkflow={handleOpenRaidRewardsWorkflow}
              onOpenShopsWorkflow={handleOpenShopsWorkflow}
              onOpenTextWorkflow={handleOpenTextWorkflow}
              onOpenTrainersWorkflow={handleOpenTrainersWorkflow}
              pendingEditCount={pendingEditCount}
              workflows={workflows}
            />
          ) : null}
          {activeSection === 'items' ? (
            <ItemsSection
              onSearchChange={setItemSearchText}
              onSelectItem={setSelectedItemId}
              onStartEditSession={handleStartEditSession}
              onUpdateItemField={handleUpdateItemField}
              searchText={itemSearchText}
              selectedItemId={selectedItemId}
              editSession={editSession}
              isEditStarting={isEditStarting}
              isItemUpdating={isItemUpdating}
              workflow={itemsWorkflow}
            />
          ) : null}
          {activeSection === 'text' ? (
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
          ) : null}
          {activeSection === 'trainers' ? (
            <TrainersSection
              editSession={editSession}
              isEditStarting={isEditStarting}
              isTrainerUpdating={isTrainerUpdating}
              onSearchChange={setTrainerSearchText}
              onSelectTrainer={setSelectedTrainerId}
              onStartEditSession={handleStartEditSession}
              onUpdateTrainerField={handleUpdateTrainerField}
              searchText={trainerSearchText}
              selectedTrainerId={selectedTrainerId}
              workflow={trainersWorkflow}
            />
          ) : null}
          {activeSection === 'shops' ? (
            <ShopsSection
              editSession={editSession}
              isEditStarting={isEditStarting}
              isShopUpdating={isShopUpdating}
              onSearchChange={setShopSearchText}
              onSelectShop={setSelectedShopId}
              onStartEditSession={handleStartEditSession}
              onUpdateShopInventoryItem={handleUpdateShopInventoryItem}
              searchText={shopSearchText}
              selectedShopId={selectedShopId}
              workflow={shopsWorkflow}
            />
          ) : null}
          {activeSection === 'encounters' ? (
            <EncountersSection
              editSession={editSession}
              isEditStarting={isEditStarting}
              isEncounterUpdating={isEncounterUpdating}
              onSearchChange={setEncounterSearchText}
              onSelectTable={setSelectedEncounterTableId}
              onStartEditSession={handleStartEditSession}
              onUpdateEncounterSlotField={handleUpdateEncounterSlotField}
              searchText={encounterSearchText}
              selectedTableId={selectedEncounterTableId}
              workflow={encountersWorkflow}
            />
          ) : null}
          {activeSection === 'raidRewards' ? (
            <RaidRewardsSection
              editSession={editSession}
              isEditStarting={isEditStarting}
              isRaidRewardUpdating={isRaidRewardUpdating}
              onSearchChange={setRaidRewardSearchText}
              onSelectTable={setSelectedRaidRewardTableId}
              onStartEditSession={handleStartEditSession}
              onUpdateRaidRewardField={handleUpdateRaidRewardField}
              searchText={raidRewardSearchText}
              selectedTableId={selectedRaidRewardTableId}
              workflow={raidRewardsWorkflow}
            />
          ) : null}
          {activeSection === 'placement' ? (
            <PlacementSection
              editSession={editSession}
              isEditStarting={isEditStarting}
              isPlacementUpdating={isPlacementUpdating}
              onSearchChange={setPlacementSearchText}
              onSelectObject={setSelectedPlacementObjectId}
              onStartEditSession={handleStartEditSession}
              onUpdatePlacementObjectField={handleUpdatePlacementObjectField}
              searchText={placementSearchText}
              selectedObjectId={selectedPlacementObjectId}
              workflow={placementWorkflow}
            />
          ) : null}
          {activeSection === 'flagworkSave' ? (
            <FlagworkSaveSection
              onSearchChange={setFlagworkSaveSearchText}
              onSelectFlag={setSelectedFlagId}
              onSelectSaveBlock={setSelectedSaveBlockId}
              searchText={flagworkSaveSearchText}
              selectedFlagId={selectedFlagId}
              selectedSaveBlockId={selectedSaveBlockId}
              workflow={flagworkSaveWorkflow}
            />
          ) : null}
          {activeSection === 'exefsPatches' ? (
            <ExeFsPatchSection
              onSearchChange={setExeFsPatchSearchText}
              onSelectCheck={setSelectedExeFsCheckId}
              onSelectPatch={setSelectedExeFsPatchId}
              searchText={exeFsPatchSearchText}
              selectedCheckId={selectedExeFsCheckId}
              selectedPatchId={selectedExeFsPatchId}
              workflow={exeFsPatchWorkflow}
            />
          ) : null}
          {activeSection === 'changes' ? (
            <ChangesSection
              applyResult={applyResult}
              changePlan={changePlan}
              diagnostics={editValidationDiagnostics}
              editSession={editSession}
              isChangePlanApplying={isChangePlanApplying}
              isChangePlanCreating={isChangePlanCreating}
              isSessionValidating={isSessionValidating}
              onApplyChangePlan={handleApplyChangePlan}
              onCreateChangePlan={handleCreateChangePlan}
              onValidateEditSession={handleValidateEditSession}
            />
          ) : null}
        </div>
      </section>
    </main>
  );
}

function HealthSection({
  bridgeDiagnostics,
  draftPaths,
  health,
  isBusy,
  onOpenProject,
  onSetDraftPath,
  onValidateProject,
  pendingEditCount,
  projectStatus
}: {
  bridgeDiagnostics: ApiDiagnostic[];
  draftPaths: ProjectPathDraft;
  health: ProjectHealth | null;
  isBusy: boolean;
  onOpenProject: () => void;
  onSetDraftPath: (field: keyof ProjectPathDraft, value: string) => void;
  onValidateProject: () => void;
  pendingEditCount: number;
  projectStatus: 'idle' | 'validating' | 'opening' | 'open';
}) {
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

            return (
              <label className="path-field" key={pathField.field}>
                <span>{pathField.label}</span>
                <input
                  aria-label={pathField.label}
                  aria-describedby={`${pathField.field}-status`}
                  onChange={(event) => onSetDraftPath(pathField.field, event.target.value)}
                  placeholder="Not set"
                  value={draftPaths[pathField.field]}
                />
                <small
                  className={getPathStatusClassName(pathValidation)}
                  id={`${pathField.field}-status`}
                >
                  {pathValidation ? pathStatusLabels[pathValidation.status] : 'Not checked'}
                </small>
              </label>
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
  isShopsLoading,
  isTextLoading,
  isTrainersLoading,
  isRaidRewardsLoading,
  isPlacementLoading,
  isFlagworkSaveLoading,
  onOpenEncountersWorkflow,
  onOpenExeFsPatchWorkflow,
  onOpenFlagworkSaveWorkflow,
  onOpenItemsWorkflow,
  onOpenPlacementWorkflow,
  onOpenRaidRewardsWorkflow,
  onOpenShopsWorkflow,
  onOpenTextWorkflow,
  onOpenTrainersWorkflow,
  pendingEditCount,
  workflows
}: {
  health: ProjectHealth | null;
  isEncountersLoading: boolean;
  isExeFsPatchLoading: boolean;
  isItemsLoading: boolean;
  isShopsLoading: boolean;
  isTextLoading: boolean;
  isTrainersLoading: boolean;
  isRaidRewardsLoading: boolean;
  isPlacementLoading: boolean;
  isFlagworkSaveLoading: boolean;
  onOpenEncountersWorkflow: () => void;
  onOpenExeFsPatchWorkflow: () => void;
  onOpenFlagworkSaveWorkflow: () => void;
  onOpenItemsWorkflow: () => void;
  onOpenPlacementWorkflow: () => void;
  onOpenRaidRewardsWorkflow: () => void;
  onOpenShopsWorkflow: () => void;
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
          const isTextWorkflow = definition.id === 'text';
          const isTrainersWorkflow = definition.id === 'trainers';
          const isShopsWorkflow = definition.id === 'shops';
          const isEncountersWorkflow = definition.id === 'encounters';
          const isRaidRewardsWorkflow = definition.id === 'raidRewards';
          const isPlacementWorkflow = definition.id === 'placement';
          const isFlagworkSaveWorkflow = definition.id === 'flagworkSave';
          const isExeFsPatchWorkflow = definition.id === 'exefsPatches';
          const canOpenItems = isItemsWorkflow && workflowState.availability !== 'disabled';
          const canOpenText = isTextWorkflow && workflowState.availability !== 'disabled';
          const canOpenTrainers = isTrainersWorkflow && workflowState.availability !== 'disabled';
          const canOpenShops = isShopsWorkflow && workflowState.availability !== 'disabled';
          const canOpenEncounters =
            isEncountersWorkflow && workflowState.availability !== 'disabled';
          const canOpenRaidRewards =
            isRaidRewardsWorkflow && workflowState.availability !== 'disabled';
          const canOpenPlacement =
            isPlacementWorkflow && workflowState.availability !== 'disabled';
          const canOpenFlagworkSave =
            isFlagworkSaveWorkflow && workflowState.availability !== 'disabled';
          const canOpenExeFsPatch =
            isExeFsPatchWorkflow && workflowState.availability !== 'disabled';

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
  searchText: string;
  selectedItemId: number | null;
  workflow: ItemsWorkflow | null;
}) {
  const filteredItems = filterItems(workflow?.items ?? [], searchText);
  const selectedItem =
    workflow?.items.find((item) => item.itemId === selectedItemId) ?? filteredItems[0] ?? null;
  const canEditItems = workflow?.summary.availability === 'available';
  const pendingItemIds = getPendingItemIds(editSession);

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
            <div className="items-table" role="table" aria-label="Items">
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
              {filteredItems.map((item) => (
                <button
                  className={`items-row ${selectedItem?.itemId === item.itemId ? 'items-row-selected' : ''} ${
                    pendingItemIds.has(item.itemId) ? 'items-row-pending' : ''
                  }`}
                  key={item.itemId}
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
              ))}
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
  onUpdateItemField
}: {
  canEditItems: boolean;
  editSession: EditSession | null;
  editableFields: ItemEditableField[];
  isEditStarting: boolean;
  isItemUpdating: boolean;
  item: ItemRecord | null;
  onStartEditSession: () => void;
  onUpdateItemField: (itemId: number, field: string, value: string) => void;
}) {
  const [fieldDrafts, setFieldDrafts] = useState<Record<string, string>>({});

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
  }, [
    editableFields,
    item?.alternatePrice,
    item?.buyPrice,
    item?.itemId,
    item?.sellPrice,
    item?.wattsPrice
  ]);

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
            <div className="item-price-editor">
              {editableFields.map((field) => {
                const currentValue = getEditableItemFieldValue(item, field.field);
                const draftValue = fieldDrafts[field.field] ?? '';
                const draftState = getItemPriceDraftState(draftValue, currentValue, field);
                const canSubmit =
                  editSession !== null && draftState.canSubmit && draftState.parsedValue !== null;

                return (
                  <div className="item-price-editor-row" key={field.field}>
                    <label className="path-field">
                      <span>{field.label}</span>
                      <input
                        aria-label={field.label}
                        disabled={!canEditItems || editSession === null || isItemUpdating}
                        max={field.maximumValue ?? undefined}
                        min={field.minimumValue ?? undefined}
                        onChange={(event) =>
                          setFieldDrafts((currentDrafts) => ({
                            ...currentDrafts,
                            [field.field]: event.target.value
                          }))
                        }
                        type="number"
                        value={draftValue}
                      />
                    </label>

                    {editSession ? (
                      <button
                        aria-label={`Save ${field.label.toLocaleLowerCase()}`}
                        className="primary-button compact-button"
                        disabled={!canSubmit || isItemUpdating}
                        onClick={() =>
                          onUpdateItemField(
                            item.itemId,
                            field.field,
                            draftState.parsedValue!.toString()
                          )
                        }
                        type="button"
                      >
                        <Save aria-hidden="true" size={16} />
                        <span>{isItemUpdating ? 'Saving' : getItemFieldSaveLabel(field)}</span>
                      </button>
                    ) : null}
                  </div>
                );
              })}
            </div>

            {!editSession ? (
              <button
                className="secondary-button"
                disabled={!canEditItems || isEditStarting}
                onClick={onStartEditSession}
                type="button"
              >
                <Pencil aria-hidden="true" size={16} />
                <span>{isEditStarting ? 'Starting' : 'Start Edit Session'}</span>
              </button>
            ) : null}
          </div>
        </>
      ) : (
        <p className="empty-copy">No item selected.</p>
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
  const filteredEntries = filterTextEntries(workflow?.entries ?? [], searchText);
  const selectedEntry =
    workflow?.entries.find((entry) => entry.textKey === selectedTextKey) ??
    filteredEntries[0] ??
    null;
  const canEditText = workflow?.summary.availability === 'available';
  const pendingTextKeys = getPendingTextKeys(editSession);

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
            <div className="text-table" role="table" aria-label="Text entries">
              <div className="text-row text-row-heading" role="row">
                <span role="columnheader">ID</span>
                <span role="columnheader">File</span>
                <span role="columnheader">Line</span>
                <span role="columnheader">Value</span>
                <span role="columnheader">Source</span>
              </div>
              {filteredEntries.map((entry) => (
                <button
                  className={`text-row ${selectedEntry?.textKey === entry.textKey ? 'text-row-selected' : ''} ${
                    pendingTextKeys.has(entry.textKey) ? 'text-row-pending' : ''
                  }`}
                  key={entry.textKey}
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
              ))}
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
                <span>{isEditStarting ? 'Starting' : 'Start Edit Session'}</span>
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
  searchText: string;
  selectedTrainerId: number | null;
  workflow: TrainersWorkflow | null;
}) {
  const [selectedSlot, setSelectedSlot] = useState<number | null>(null);
  const filteredTrainers = filterTrainers(workflow?.trainers ?? [], searchText);
  const selectedTrainer =
    workflow?.trainers.find((trainer) => trainer.trainerId === selectedTrainerId) ??
    filteredTrainers[0] ??
    null;
  const selectedPokemon =
    selectedTrainer?.team.find((pokemon) => pokemon.slot === selectedSlot) ??
    selectedTrainer?.team[0] ??
    null;
  const canEditTrainers = workflow?.summary.availability === 'available';
  const pendingTrainerIds = getPendingTrainerIds(editSession);

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
            <div className="trainers-table" role="table" aria-label="Trainers">
              <div className="trainers-row trainers-row-heading" role="row">
                <span role="columnheader">ID</span>
                <span role="columnheader">Name</span>
                <span role="columnheader">Class</span>
                <span role="columnheader">Battle</span>
                <span role="columnheader">Team</span>
                <span role="columnheader">Source</span>
              </div>
              {filteredTrainers.map((trainer) => (
                <button
                  className={`trainers-row ${
                    selectedTrainer?.trainerId === trainer.trainerId ? 'trainers-row-selected' : ''
                  } ${pendingTrainerIds.has(trainer.trainerId) ? 'trainers-row-pending' : ''}`}
                  key={trainer.trainerId}
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
              ))}
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
  selectedPokemon: TrainerPokemonRecord | null;
  selectedSlot: number | null;
  trainer: TrainerRecord | null;
}) {
  const [trainerDrafts, setTrainerDrafts] = useState<Record<string, string>>({});
  const [pokemonDrafts, setPokemonDrafts] = useState<Record<string, string>>({});
  const trainerFields = editableFields.filter((field) =>
    [trainerClassIdFieldName, battleTypeFieldName].includes(field.field)
  );
  const pokemonFields = editableFields.filter((field) =>
    [speciesIdFieldName, levelFieldName, heldItemIdFieldName, ...moveFieldNames].includes(
      field.field as (typeof moveFieldNames)[number]
    )
  );

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
  }, [editableFields, trainer?.battleTypeValue, trainer?.trainerClassId, trainer?.trainerId]);

  useEffect(() => {
    if (!selectedPokemon) {
      setPokemonDrafts({});
      return;
    }

    setPokemonDrafts(
      Object.fromEntries(
        pokemonFields.map((field) => [
          field.field,
          (getEditablePokemonFieldValue(selectedPokemon, field.field) ?? '').toString()
        ])
      )
    );
  }, [
    editableFields,
    selectedPokemon?.heldItemId,
    selectedPokemon?.level,
    selectedPokemon?.moveIds,
    selectedPokemon?.slot,
    selectedPokemon?.speciesId
  ]);

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
              <dt>Data layer</dt>
              <dd>{formatSourceLayer(trainer.provenance.sourceLayer)}</dd>
            </div>
            <div>
              <dt>Party layer</dt>
              <dd>{formatSourceLayer(trainer.provenance.teamSourceLayer)}</dd>
            </div>
          </dl>

          <div className="trainer-edit-form">
            <div className="trainer-field-grid">
              {trainerFields.map((field) => {
                const currentValue = getEditableTrainerFieldValue(trainer, field.field);
                const draftValue = trainerDrafts[field.field] ?? '';
                const draftState = getIntegerDraftState(draftValue, currentValue, field);
                const canSubmit =
                  editSession !== null && draftState.canSubmit && draftState.parsedValue !== null;

                return (
                  <div className="trainer-editor-row" key={field.field}>
                    <label className="path-field">
                      <span>{field.label}</span>
                      <input
                        aria-label={field.label}
                        disabled={!canEditTrainers || editSession === null || isTrainerUpdating}
                        max={field.maximumValue ?? undefined}
                        min={field.minimumValue ?? undefined}
                        onChange={(event) =>
                          setTrainerDrafts((currentDrafts) => ({
                            ...currentDrafts,
                            [field.field]: event.target.value
                          }))
                        }
                        type="number"
                        value={draftValue}
                      />
                    </label>
                    {editSession ? (
                      <button
                        aria-label={`Save ${field.label.toLocaleLowerCase()}`}
                        className="primary-button compact-button"
                        disabled={!canSubmit || isTrainerUpdating}
                        onClick={() =>
                          onUpdateTrainerField(
                            trainer.trainerId,
                            null,
                            field.field,
                            draftState.parsedValue!.toString()
                          )
                        }
                        type="button"
                      >
                        <Save aria-hidden="true" size={16} />
                        <span>{isTrainerUpdating ? 'Saving' : 'Save'}</span>
                      </button>
                    ) : null}
                  </div>
                );
              })}
            </div>

            <div className="trainer-party-header">
              <strong>Party</strong>
              <select
                aria-label="Trainer party slot"
                disabled={trainer.team.length === 0}
                onChange={(event) => onSelectSlot(Number(event.target.value))}
                value={selectedSlot ?? ''}
              >
                {trainer.team.map((pokemon) => (
                  <option key={pokemon.slot} value={pokemon.slot}>
                    Slot {pokemon.slot}: {pokemon.species}
                  </option>
                ))}
              </select>
            </div>

            {selectedPokemon ? (
              <div className="trainer-field-grid">
                {pokemonFields.map((field) => {
                  const currentValue = getEditablePokemonFieldValue(selectedPokemon, field.field);
                  const draftValue = pokemonDrafts[field.field] ?? '';
                  const draftState = getIntegerDraftState(draftValue, currentValue, field);
                  const canSubmit =
                    editSession !== null && draftState.canSubmit && draftState.parsedValue !== null;

                  return (
                    <div className="trainer-editor-row" key={field.field}>
                      <label className="path-field">
                        <span>{field.label}</span>
                        <input
                          aria-label={field.label}
                          disabled={!canEditTrainers || editSession === null || isTrainerUpdating}
                          max={field.maximumValue ?? undefined}
                          min={field.minimumValue ?? undefined}
                          onChange={(event) =>
                            setPokemonDrafts((currentDrafts) => ({
                              ...currentDrafts,
                              [field.field]: event.target.value
                            }))
                          }
                          type="number"
                          value={draftValue}
                        />
                      </label>
                      {editSession ? (
                        <button
                          aria-label={`Save ${field.label.toLocaleLowerCase()}`}
                          className="primary-button compact-button"
                          disabled={!canSubmit || isTrainerUpdating}
                          onClick={() =>
                            onUpdateTrainerField(
                              trainer.trainerId,
                              selectedPokemon.slot,
                              field.field,
                              draftState.parsedValue!.toString()
                            )
                          }
                          type="button"
                        >
                          <Save aria-hidden="true" size={16} />
                          <span>{isTrainerUpdating ? 'Saving' : 'Save'}</span>
                        </button>
                      ) : null}
                    </div>
                  );
                })}
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
                <span>{isEditStarting ? 'Starting' : 'Start Edit Session'}</span>
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

function ShopsSection({
  editSession,
  isEditStarting,
  isShopUpdating,
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
                <span role="columnheader">ID</span>
                <span role="columnheader">Name</span>
                <span role="columnheader">Location</span>
                <span role="columnheader">Currency</span>
                <span role="columnheader">Items</span>
                <span role="columnheader">Source</span>
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
                  <span role="cell">{shop.shopId}</span>
                  <span role="cell">{shop.name}</span>
                  <span role="cell">{shop.location}</span>
                  <span role="cell">{shop.currency}</span>
                  <span role="cell">{shop.inventory.length}</span>
                  <span role="cell">{formatSourceLayer(shop.provenance.sourceLayer)}</span>
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
  inventoryItem,
  isEditStarting,
  isShopUpdating,
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
  const [itemIdDraft, setItemIdDraft] = useState('');
  const itemIdField = editableFields.find((field) => field.field === shopItemIdFieldName);

  useEffect(() => {
    setItemIdDraft(inventoryItem?.itemId.toString() ?? '');
  }, [inventoryItem?.itemId, inventoryItem?.slot, shop?.shopId]);

  const draftState = getIntegerDraftState(
    itemIdDraft,
    inventoryItem?.itemId ?? null,
    itemIdField
  );
  const canSubmit =
    editSession !== null && inventoryItem !== null && draftState.canSubmit && draftState.parsedValue !== null;

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
              <select
                aria-label="Shop inventory slot"
                disabled={shop.inventory.length === 0}
                onChange={(event) => onSelectSlot(Number(event.target.value))}
                value={selectedSlot ?? ''}
              >
                {shop.inventory.map((item) => (
                  <option key={item.slot} value={item.slot}>
                    Slot {item.slot}: {item.itemName}
                  </option>
                ))}
              </select>
            </div>

            {inventoryItem ? (
              <>
                <dl className="shop-inventory-detail">
                  <div>
                    <dt>Item</dt>
                    <dd>{inventoryItem.itemName}</dd>
                  </div>
                  <div>
                    <dt>Price</dt>
                    <dd>{inventoryItem.price}</dd>
                  </div>
                  <div>
                    <dt>Stock</dt>
                    <dd>{inventoryItem.stockLimit ?? 'None'}</dd>
                  </div>
                </dl>

                <div className="shop-editor-row">
                  <label className="path-field">
                    <span>{itemIdField?.label ?? 'Item ID'}</span>
                    <input
                      aria-label={itemIdField?.label ?? 'Item ID'}
                      disabled={!canEditShops || editSession === null || isShopUpdating}
                      max={itemIdField?.maximumValue ?? undefined}
                      min={itemIdField?.minimumValue ?? undefined}
                      onChange={(event) => setItemIdDraft(event.target.value)}
                      type="number"
                      value={itemIdDraft}
                    />
                  </label>
                  {editSession ? (
                    <button
                      className="primary-button compact-button"
                      disabled={!canSubmit || isShopUpdating}
                      onClick={() =>
                        onUpdateShopInventoryItem(
                          shop.shopId,
                          inventoryItem.slot,
                          shopItemIdFieldName,
                          draftState.parsedValue!.toString()
                        )
                      }
                      type="button"
                    >
                      <Save aria-hidden="true" size={16} />
                      <span>{isShopUpdating ? 'Saving' : 'Save Item'}</span>
                    </button>
                  ) : null}
                </div>
              </>
            ) : (
              <p className="empty-copy">No inventory slot selected.</p>
            )}

            {!editSession ? (
              <button
                className="secondary-button"
                disabled={!canEditShops || isEditStarting || shop.inventory.length === 0}
                onClick={onStartEditSession}
                type="button"
              >
                <Pencil aria-hidden="true" size={16} />
                <span>{isEditStarting ? 'Starting' : 'Start Edit Session'}</span>
              </button>
            ) : null}
          </div>
        </>
      ) : (
        <p className="empty-copy">No shop selected.</p>
      )}
    </aside>
  );
}

function EncountersSection({
  editSession,
  isEditStarting,
  isEncounterUpdating,
  onSearchChange,
  onSelectTable,
  onStartEditSession,
  onUpdateEncounterSlotField,
  searchText,
  selectedTableId,
  workflow
}: {
  editSession: EditSession | null;
  isEditStarting: boolean;
  isEncounterUpdating: boolean;
  onSearchChange: (searchText: string) => void;
  onSelectTable: (tableId: string | null) => void;
  onStartEditSession: () => void;
  onUpdateEncounterSlotField: (
    tableId: string,
    slot: number,
    field: string,
    value: string
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
              isEditStarting={isEditStarting}
              isEncounterUpdating={isEncounterUpdating}
              onSelectSlot={setSelectedSlot}
              onStartEditSession={onStartEditSession}
              onUpdateEncounterSlotField={onUpdateEncounterSlotField}
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
  editSession,
  editableFields,
  encounterSlot,
  isEditStarting,
  isEncounterUpdating,
  onSelectSlot,
  onStartEditSession,
  onUpdateEncounterSlotField,
  selectedSlot,
  table
}: {
  canEditEncounters: boolean;
  editSession: EditSession | null;
  editableFields: EncounterEditableField[];
  encounterSlot: EncounterSlotRecord | null;
  isEditStarting: boolean;
  isEncounterUpdating: boolean;
  onSelectSlot: (slot: number | null) => void;
  onStartEditSession: () => void;
  onUpdateEncounterSlotField: (
    tableId: string,
    slot: number,
    field: string,
    value: string
  ) => void;
  selectedSlot: number | null;
  table: EncounterTableRecord | null;
}) {
  const [drafts, setDrafts] = useState<Record<string, string>>({});

  useEffect(() => {
    if (!encounterSlot) {
      setDrafts({});
      return;
    }

    setDrafts(
      Object.fromEntries(
        editableFields.map((field) => [
          field.field,
          (getEditableEncounterFieldValue(encounterSlot, field.field) ?? '').toString()
        ])
      )
    );
  }, [
    editableFields,
    encounterSlot?.form,
    encounterSlot?.levelMax,
    encounterSlot?.levelMin,
    encounterSlot?.slot,
    encounterSlot?.speciesId,
    encounterSlot?.weight,
    table?.tableId
  ]);

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
            <div className="encounter-slot-header">
              <strong>Slots</strong>
              <select
                aria-label="Encounter slot"
                disabled={table.slots.length === 0}
                onChange={(event) => onSelectSlot(Number(event.target.value))}
                value={selectedSlot ?? ''}
              >
                {table.slots.map((slot) => (
                  <option key={slot.slot} value={slot.slot}>
                    Slot {slot.slot}: {slot.species}
                  </option>
                ))}
              </select>
            </div>

            {encounterSlot ? (
              <>
                <dl className="encounter-slot-detail">
                  <div>
                    <dt>Species</dt>
                    <dd>{encounterSlot.species}</dd>
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

                <div className="encounter-field-grid">
                  {editableFields.map((field) => {
                    const currentValue = getEditableEncounterFieldValue(
                      encounterSlot,
                      field.field
                    );
                    const draftValue = drafts[field.field] ?? '';
                    const draftState = getIntegerDraftState(draftValue, currentValue, field);
                    const canSubmit =
                      editSession !== null &&
                      draftState.canSubmit &&
                      draftState.parsedValue !== null;

                    return (
                      <div className="trainer-editor-row" key={field.field}>
                        <label className="path-field">
                          <span>{field.label}</span>
                          <input
                            aria-label={field.label}
                            disabled={
                              !canEditEncounters ||
                              editSession === null ||
                              isEncounterUpdating
                            }
                            max={field.maximumValue ?? undefined}
                            min={field.minimumValue ?? undefined}
                            onChange={(event) =>
                              setDrafts((currentDrafts) => ({
                                ...currentDrafts,
                                [field.field]: event.target.value
                              }))
                            }
                            type="number"
                            value={draftValue}
                          />
                        </label>
                        {editSession ? (
                          <button
                            aria-label={`Save ${field.label.toLocaleLowerCase()}`}
                            className="primary-button compact-button"
                            disabled={!canSubmit || isEncounterUpdating}
                            onClick={() =>
                              onUpdateEncounterSlotField(
                                table.tableId,
                                encounterSlot.slot,
                                field.field,
                                draftState.parsedValue!.toString()
                              )
                            }
                            type="button"
                          >
                            <Save aria-hidden="true" size={16} />
                            <span>{isEncounterUpdating ? 'Saving' : 'Save'}</span>
                          </button>
                        ) : null}
                      </div>
                    );
                  })}
                </div>
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
                <span>{isEditStarting ? 'Starting' : 'Start Edit Session'}</span>
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

function RaidRewardsSection({
  editSession,
  isEditStarting,
  isRaidRewardUpdating,
  onSearchChange,
  onSelectTable,
  onStartEditSession,
  onUpdateRaidRewardField,
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
  reward: RaidRewardItemRecord | null;
  selectedSlot: number | null;
  table: RaidRewardTableRecord | null;
}) {
  const [drafts, setDrafts] = useState<Record<string, string>>({});

  useEffect(() => {
    if (!reward) {
      setDrafts({});
      return;
    }

    setDrafts(
      Object.fromEntries(
        editableFields.map((field) => [
          field.field,
          (getEditableRaidRewardFieldValue(reward, field.field) ?? '').toString()
        ])
      )
    );
  }, [editableFields, reward?.itemId, reward?.slot, reward?.values.join('|'), table?.tableId]);

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
                    <dt>Values</dt>
                    <dd>{reward.values.slice(0, 5).join(' / ')}</dd>
                  </div>
                </dl>

                <div className="encounter-field-grid">
                  {editableFields.map((field) => {
                    const currentValue = getEditableRaidRewardFieldValue(reward, field.field);
                    const draftValue = drafts[field.field] ?? '';
                    const draftState = getIntegerDraftState(draftValue, currentValue, field);
                    const canSubmit =
                      editSession !== null &&
                      draftState.canSubmit &&
                      draftState.parsedValue !== null;

                    return (
                      <div className="trainer-editor-row" key={field.field}>
                        <label className="path-field">
                          <span>{field.label}</span>
                          <input
                            aria-label={field.label}
                            disabled={
                              !canEditRaidRewards ||
                              editSession === null ||
                              isRaidRewardUpdating
                            }
                            max={field.maximumValue ?? undefined}
                            min={field.minimumValue ?? undefined}
                            onChange={(event) =>
                              setDrafts((currentDrafts) => ({
                                ...currentDrafts,
                                [field.field]: event.target.value
                              }))
                            }
                            type="number"
                            value={draftValue}
                          />
                        </label>
                        {editSession ? (
                          <button
                            aria-label={`Save ${field.label.toLocaleLowerCase()}`}
                            className="primary-button compact-button"
                            disabled={!canSubmit || isRaidRewardUpdating}
                            onClick={() =>
                              onUpdateRaidRewardField(
                                table.tableId,
                                reward.slot,
                                field.field,
                                draftState.parsedValue!.toString()
                              )
                            }
                            type="button"
                          >
                            <Save aria-hidden="true" size={16} />
                            <span>{isRaidRewardUpdating ? 'Saving' : 'Save'}</span>
                          </button>
                        ) : null}
                      </div>
                    );
                  })}
                </div>
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
                <span>{isEditStarting ? 'Starting' : 'Start Edit Session'}</span>
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

function PlacementSection({
  editSession,
  isEditStarting,
  isPlacementUpdating,
  onSearchChange,
  onSelectObject,
  onStartEditSession,
  onUpdatePlacementObjectField,
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
  placedObject
}: {
  canEditPlacement: boolean;
  editSession: EditSession | null;
  editableFields: PlacementEditableField[];
  isEditStarting: boolean;
  isPlacementUpdating: boolean;
  onStartEditSession: () => void;
  onUpdatePlacementObjectField: (objectId: string, field: string, value: string) => void;
  placedObject: PlacedObjectRecord | null;
}) {
  const [drafts, setDrafts] = useState<Record<string, string>>({});
  const visibleFields = editableFields.filter((field) =>
    placedObject ? isPlacementFieldVisible(placedObject, field.field) : false
  );

  useEffect(() => {
    if (!placedObject) {
      setDrafts({});
      return;
    }

    setDrafts(
      Object.fromEntries(
        visibleFields.map((field) => [
          field.field,
          (getEditablePlacementFieldValue(placedObject, field.field) ?? '').toString()
        ])
      )
    );
  }, [
    placedObject?.chance,
    placedObject?.itemId,
    placedObject?.objectId,
    placedObject?.quantity,
    placedObject?.rotationY,
    placedObject?.x,
    placedObject?.y,
    placedObject?.z,
    visibleFields.map((field) => field.field).join('|')
  ]);

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

            <div className="encounter-field-grid">
              {visibleFields.map((field) => {
                const currentValue = getEditablePlacementFieldValue(placedObject, field.field);
                const draftValue = drafts[field.field] ?? '';
                const draftState = getPlacementDraftState(draftValue, currentValue, field);
                const canSubmit =
                  editSession !== null &&
                  draftState.canSubmit &&
                  draftState.normalizedValue !== null;

                return (
                  <div className="trainer-editor-row" key={field.field}>
                    <label className="path-field">
                      <span>{field.label}</span>
                      <input
                        aria-label={field.label}
                        disabled={
                          !canEditPlacement || editSession === null || isPlacementUpdating
                        }
                        max={field.maximumValue}
                        min={field.minimumValue}
                        onChange={(event) =>
                          setDrafts((currentDrafts) => ({
                            ...currentDrafts,
                            [field.field]: event.target.value
                          }))
                        }
                        step={field.valueKind === 'integer' ? 1 : 'any'}
                        type="number"
                        value={draftValue}
                      />
                    </label>
                    {editSession ? (
                      <button
                        aria-label={`Save ${field.label.toLocaleLowerCase()}`}
                        className="primary-button compact-button"
                        disabled={!canSubmit || isPlacementUpdating}
                        onClick={() =>
                          onUpdatePlacementObjectField(
                            placedObject.objectId,
                            field.field,
                            draftState.normalizedValue!
                          )
                        }
                        type="button"
                      >
                        <Save aria-hidden="true" size={16} />
                        <span>{isPlacementUpdating ? 'Saving' : 'Save'}</span>
                      </button>
                    ) : null}
                  </div>
                );
              })}
            </div>

            {!editSession ? (
              <button
                className="secondary-button"
                disabled={!canEditPlacement || isEditStarting}
                onClick={onStartEditSession}
                type="button"
              >
                <Pencil aria-hidden="true" size={16} />
                <span>{isEditStarting ? 'Starting' : 'Start Edit Session'}</span>
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

            <SelectedFlagworkSavePanel flag={selectedFlag} saveBlock={selectedSaveBlock} />
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
  saveBlock
}: {
  flag: FlagRecord | null;
  saveBlock: SaveBlockRecord | null;
}) {
  const provenance = saveBlock?.provenance ?? flag?.provenance ?? null;

  return (
    <aside aria-label="Selected flagwork provenance" className="encounter-inspector">
      <div className="panel-heading">
        <Save aria-hidden="true" size={18} />
        <h3>Selected Save Key</h3>
      </div>

      {flag || saveBlock ? (
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
  onSearchChange,
  onSelectCheck,
  onSelectPatch,
  searchText,
  selectedCheckId,
  selectedPatchId,
  workflow
}: {
  onSearchChange: (value: string) => void;
  onSelectCheck: (checkId: string | null) => void;
  onSelectPatch: (patchId: string | null) => void;
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
  patch,
  segments
}: {
  check: ExeFsPatchCheckRecord | null;
  patch: ExeFsPatchRecord | null;
  segments: ExeFsSegmentRecord[];
}) {
  const provenance = check?.provenance ?? patch?.provenance ?? segments[0]?.provenance ?? null;

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

function ChangesSection({
  applyResult,
  changePlan,
  diagnostics,
  editSession,
  isChangePlanApplying,
  isChangePlanCreating,
  isSessionValidating,
  onApplyChangePlan,
  onCreateChangePlan,
  onValidateEditSession
}: {
  applyResult: ApplyResult | null;
  changePlan: ChangePlan | null;
  diagnostics: ApiDiagnostic[];
  editSession: EditSession | null;
  isChangePlanApplying: boolean;
  isChangePlanCreating: boolean;
  isSessionValidating: boolean;
  onApplyChangePlan: () => void;
  onCreateChangePlan: () => void;
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
          <button
            className="secondary-button"
            disabled={pendingEdits.length === 0 || isSessionValidating}
            onClick={onValidateEditSession}
            type="button"
          >
            <CheckCircle aria-hidden="true" size={18} />
            <span>{isSessionValidating ? 'Validating' : 'Validate Pending Change'}</span>
          </button>
          <button
            className="primary-button"
            disabled={pendingEdits.length === 0 || isChangePlanCreating}
            onClick={onCreateChangePlan}
            type="button"
          >
            <ClipboardCheck aria-hidden="true" size={18} />
            <span>{isChangePlanCreating ? 'Reviewing' : 'Review Change Plan'}</span>
          </button>
        </div>

        {pendingEdits.length > 0 ? (
          <ul className="pending-edit-list">
            {pendingEdits.map((edit, index) => (
              <li key={`${edit.domain}-${edit.recordId ?? index}-${edit.field ?? 'field'}`}>
                <strong>{edit.summary}</strong>
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
          isApplying={isChangePlanApplying}
          onApplyChangePlan={onApplyChangePlan}
        />
      ) : null}
      {applyResult ? <ApplyResultSection applyResult={applyResult} /> : null}
      <DiagnosticsSection diagnostics={combinedDiagnostics} />
    </>
  );
}

function ChangePlanSection({
  changePlan,
  isApplying,
  onApplyChangePlan
}: {
  changePlan: ChangePlan;
  isApplying: boolean;
  onApplyChangePlan: () => void;
}) {
  const canApply = changePlan.canApply && changePlan.writes.length > 0;

  return (
    <section aria-labelledby="change-plan-heading" className="panel wide-panel">
      <div className="panel-heading">
        <ClipboardCheck aria-hidden="true" size={18} />
        <h2 id="change-plan-heading">Change Plan Review</h2>
      </div>

      <div className="change-plan-status">
        <Metric label="Plan status" value={changePlan.canApply ? 'Ready' : 'Needs fixes'} />
        <Metric label="Session" value={changePlan.sessionId} />
        <button
          className="primary-button"
          disabled={!canApply || isApplying}
          onClick={onApplyChangePlan}
          type="button"
        >
          <Save aria-hidden="true" size={18} />
          <span>{isApplying ? 'Applying' : 'Apply Plan'}</span>
        </button>
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
        <h2 id="apply-result-heading">Apply Result</h2>
      </div>

      <div className="change-plan-status">
        <Metric label="Apply ID" value={applyResult.applyId} />
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
      item.alternatePrice.toString()
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
      trainer.battleType,
      trainer.provenance.sourceFile,
      trainer.provenance.teamSourceFile,
      ...trainer.team.flatMap((pokemon) => [
        pokemon.species,
        pokemon.speciesId.toString(),
        pokemon.level.toString(),
        pokemon.heldItem ?? '',
        ...pokemon.moves,
        ...pokemon.moveIds.map((moveId) => moveId.toString())
      ])
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
    case battleTypeFieldName:
      return trainer.battleTypeValue;
    default:
      return null;
  }
}

function getEditablePokemonFieldValue(pokemon: TrainerPokemonRecord, field: string) {
  switch (field) {
    case speciesIdFieldName:
      return pokemon.speciesId;
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
  const parsedValue = /^\d+$/.test(normalizedValue)
    ? Number.parseInt(normalizedValue, 10)
    : null;
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

function getIntegerDraftState(
  draftValue: string,
  currentValue: number | null,
  field:
    | EncounterEditableField
    | RaidRewardEditableField
    | ShopEditableField
    | TrainerEditableField
    | undefined
) {
  const normalizedValue = draftValue.trim();
  const parsedValue = /^\d+$/.test(normalizedValue)
    ? Number.parseInt(normalizedValue, 10)
    : null;
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

function getPendingItemIds(editSession: EditSession | null) {
  return new Set(
    (editSession?.pendingEdits ?? [])
      .filter((edit) => edit.domain === 'workflow.items')
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
      return 'status-ready';
    case 'warning':
      return 'status-warning';
    case 'fail':
    case 'blocked':
      return 'status-blocked';
    default:
      return 'status-warning';
  }
}

function getEditableRaidRewardFieldValue(reward: RaidRewardItemRecord, field: string) {
  if (field === raidRewardItemIdFieldName) {
    return reward.itemId;
  }

  const valueIndex = raidRewardValueFieldNames.findIndex((fieldName) => fieldName === field);
  return valueIndex >= 0 ? (reward.values[valueIndex] ?? 0) : null;
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
      ? /^-?\d+$/.test(normalizedValue)
        ? Number.parseInt(normalizedValue, 10)
        : Number.NaN
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
    | FlagRecord['provenance']['sourceLayer']
    | ItemRecord['provenance']['sourceLayer']
    | RaidRewardTableRecord['provenance']['sourceLayer']
    | SaveBlockRecord['provenance']['sourceLayer']
    | ShopRecord['provenance']['sourceLayer']
    | TextEntryRecord['provenance']['sourceLayer']
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
    | FlagRecord['provenance']['fileState']
    | ItemRecord['provenance']['fileState']
    | RaidRewardTableRecord['provenance']['fileState']
    | SaveBlockRecord['provenance']['fileState']
    | ShopRecord['provenance']['fileState']
    | TextEntryRecord['provenance']['fileState']
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
    outputRootPath: normalizeDraftPath(draftPaths.outputRootPath)
  };
}

function normalizeDraftPath(path: string) {
  const trimmedPath = path.trim();

  return trimmedPath.length > 0 ? trimmedPath : null;
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
