/* SPDX-License-Identifier: GPL-3.0-only */

import { z, type ZodTypeAny } from 'zod';

export const kmCommandNameValues = [
  'project.open',
  'project.validate',
  'project.fileGraph.refresh',
  'workflow.list',
  'items.load',
  'items.field.update',
  'pokemon.load',
  'pokemon.field.update',
  'pokemon.learnset.update',
  'pokemon.evolution.update',
  'moves.load',
  'moves.field.update',
  'text.load',
  'text.entry.update',
  'trainers.load',
  'trainers.field.update',
  'shops.load',
  'shops.inventory.update',
  'encounters.load',
  'encounters.slot.update',
  'raidRewards.load',
  'raidRewards.reward.update',
  'placement.load',
  'placement.object.update',
  'flagworkSave.load',
  'exefsPatches.load',
  'exefsPatches.patch.stage',
  'royalCandy.load',
  'royalCandy.workflow.stage',
  'spreadsheetImport.load',
  'spreadsheetImport.preview',
  'editSession.start',
  'editSession.get',
  'editSession.discard',
  'editSession.validate',
  'changePlan.create',
  'changePlan.apply'
] as const;

export const kmCommandNameSchema = z.enum(kmCommandNameValues);
export type KmCommandName = z.infer<typeof kmCommandNameSchema>;

export const kmCommandNames = {
  applyChangePlan: 'changePlan.apply',
  createChangePlan: 'changePlan.create',
  discardEditSession: 'editSession.discard',
  getEditSession: 'editSession.get',
  listWorkflows: 'workflow.list',
  loadItemsWorkflow: 'items.load',
  loadPokemonWorkflow: 'pokemon.load',
  updatePokemonField: 'pokemon.field.update',
  updatePokemonLearnset: 'pokemon.learnset.update',
  updatePokemonEvolution: 'pokemon.evolution.update',
  loadMovesWorkflow: 'moves.load',
  updateMoveField: 'moves.field.update',
  loadTextWorkflow: 'text.load',
  updateTextEntry: 'text.entry.update',
  loadTrainersWorkflow: 'trainers.load',
  updateTrainerField: 'trainers.field.update',
  loadShopsWorkflow: 'shops.load',
  updateShopInventoryItem: 'shops.inventory.update',
  loadEncountersWorkflow: 'encounters.load',
  updateEncounterSlotField: 'encounters.slot.update',
  loadRaidRewardsWorkflow: 'raidRewards.load',
  updateRaidRewardField: 'raidRewards.reward.update',
  loadPlacementWorkflow: 'placement.load',
  updatePlacementObjectField: 'placement.object.update',
  loadFlagworkSaveWorkflow: 'flagworkSave.load',
  loadExeFsPatchWorkflow: 'exefsPatches.load',
  stageExeFsPatch: 'exefsPatches.patch.stage',
  loadRoyalCandyWorkflow: 'royalCandy.load',
  stageRoyalCandyWorkflow: 'royalCandy.workflow.stage',
  loadSpreadsheetImportWorkflow: 'spreadsheetImport.load',
  previewSpreadsheetImport: 'spreadsheetImport.preview',
  openProject: 'project.open',
  refreshFileGraph: 'project.fileGraph.refresh',
  startEditSession: 'editSession.start',
  updateItemField: 'items.field.update',
  validateEditSession: 'editSession.validate',
  validateProject: 'project.validate'
} as const satisfies Record<string, KmCommandName>;

export const apiDiagnosticSeveritySchema = z.enum(['info', 'warning', 'error']);

export const apiDiagnosticSchema = z.strictObject({
  domain: z.string().nullable().optional(),
  expected: z.string().nullable().optional(),
  field: z.string().nullable().optional(),
  file: z.string().nullable().optional(),
  message: z.string(),
  severity: apiDiagnosticSeveritySchema
});

export const apiErrorSchema = z.strictObject({
  code: z.string(),
  diagnostics: z.array(apiDiagnosticSchema),
  message: z.string()
});

export const projectPathsSchema = z.strictObject({
  baseExeFsPath: z.string().nullable(),
  baseRomFsPath: z.string().nullable(),
  outputRootPath: z.string().nullable(),
  saveFilePath: z.string().nullable()
});

export const openProjectRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const validateProjectRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const refreshFileGraphRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const listWorkflowsRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadItemsWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadPokemonWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadMovesWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadTextWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadTrainersWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadShopsWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadEncountersWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadRaidRewardsWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadPlacementWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadFlagworkSaveWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadExeFsPatchWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadRoyalCandyWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadSpreadsheetImportWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const startEditSessionRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const projectHealthStateSchema = z.enum([
  'needsPaths',
  'readOnlyReady',
  'editableReady',
  'blocked'
]);

export const projectPathRoleSchema = z.enum(['baseRomFs', 'baseExeFs', 'outputRoot', 'saveFile']);

export const projectPathStatusSchema = z.enum(['notSet', 'missing', 'wrongKind', 'valid', 'unsafe']);

export const projectPathValidationSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  isRequired: z.boolean(),
  path: z.string().nullable(),
  role: projectPathRoleSchema,
  status: projectPathStatusSchema
});

export const projectFileGraphSummarySchema = z.strictObject({
  baseFileCount: z.number().int().nonnegative(),
  layeredFileCount: z.number().int().nonnegative(),
  layeredOnlyCount: z.number().int().nonnegative(),
  overrideCount: z.number().int().nonnegative()
});

export const projectFileGraphEntryStateSchema = z.enum([
  'baseOnly',
  'layeredOverride',
  'layeredOnly'
]);

export const projectFileLayerSchema = z.enum(['base', 'layered', 'pending', 'generated']);

export const projectFileReferenceSchema = z.strictObject({
  layer: projectFileLayerSchema,
  relativePath: z.string()
});

export const pendingEditSchema = z.strictObject({
  domain: z.string(),
  field: z.string().nullable().optional(),
  newValue: z.string().nullable().optional(),
  recordId: z.string().nullable().optional(),
  sources: z.array(projectFileReferenceSchema),
  summary: z.string()
});

export const editSessionSchema = z.strictObject({
  hasPendingChanges: z.boolean(),
  pendingEdits: z.array(pendingEditSchema),
  sessionId: z.string()
});

export const validateEditSessionRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  session: editSessionSchema
});

export const createChangePlanRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  session: editSessionSchema
});

export const applyChangePlanRequestSchema = z.strictObject({
  changePlan: z.lazy(() => changePlanSchema),
  paths: projectPathsSchema,
  session: editSessionSchema
});

export const previewSpreadsheetImportRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  profileId: z.string(),
  session: editSessionSchema.nullable(),
  sourcePath: z.string()
});

export const projectFileGraphEntrySchema = z.strictObject({
  baseFile: projectFileReferenceSchema.nullable(),
  layeredFile: projectFileReferenceSchema.nullable(),
  relativePath: z.string(),
  state: projectFileGraphEntryStateSchema
});

export const projectFileGraphSchema = z.strictObject({
  entries: z.array(projectFileGraphEntrySchema),
  summary: projectFileGraphSummarySchema
});

export const projectHealthSchema = z.strictObject({
  canOpenEditableWorkflows: z.boolean(),
  canOpenReadOnlyWorkflows: z.boolean(),
  diagnostics: z.array(apiDiagnosticSchema),
  fileGraph: projectFileGraphSummarySchema,
  paths: z.array(projectPathValidationSchema),
  state: projectHealthStateSchema
});

export const openProjectResponseSchema = z.strictObject({
  fileGraph: projectFileGraphSchema,
  health: projectHealthSchema,
  projectId: z.string()
});

export const validateProjectResponseSchema = z.strictObject({
  health: projectHealthSchema
});

export const refreshFileGraphResponseSchema = z.strictObject({
  fileGraph: projectFileGraphSchema
});

export const workflowAvailabilitySchema = z.enum(['disabled', 'readOnly', 'available']);

export const workflowSummarySchema = z.strictObject({
  availability: workflowAvailabilitySchema,
  description: z.string(),
  diagnostics: z.array(apiDiagnosticSchema),
  id: z.string(),
  label: z.string()
});

export const listWorkflowsResponseSchema = z.strictObject({
  workflows: z.array(workflowSummarySchema)
});

export const itemProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema
});

export const itemRecordSchema = z.strictObject({
  alternatePrice: z.number().int().nonnegative(),
  buyPrice: z.number().int().nonnegative(),
  category: z.string(),
  itemId: z.number().int().nonnegative(),
  name: z.string(),
  provenance: itemProvenanceSchema,
  sellPrice: z.number().int().nonnegative(),
  sharedItemIds: z.array(z.number().int().nonnegative()),
  wattsPrice: z.number().int().nonnegative()
});

export const itemEditableFieldSchema = z.strictObject({
  field: z.string(),
  label: z.string(),
  maximumValue: z.number().int().nullable(),
  minimumValue: z.number().int().nullable(),
  valueKind: z.string()
});

export const itemsWorkflowStatsSchema = z.strictObject({
  sourceFileCount: z.number().int().nonnegative(),
  totalItemCount: z.number().int().nonnegative()
});

export const itemsWorkflowSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  editableFields: z.array(itemEditableFieldSchema),
  items: z.array(itemRecordSchema),
  stats: itemsWorkflowStatsSchema,
  summary: workflowSummarySchema
});

export const loadItemsWorkflowResponseSchema = z.strictObject({
  workflow: itemsWorkflowSchema
});

export const pokemonProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema
});

export const pokemonBaseStatsSchema = z.strictObject({
  attack: z.number().int().nonnegative(),
  defense: z.number().int().nonnegative(),
  hp: z.number().int().nonnegative(),
  specialAttack: z.number().int().nonnegative(),
  specialDefense: z.number().int().nonnegative(),
  speed: z.number().int().nonnegative(),
  total: z.number().int().nonnegative()
});

export const pokemonAbilitySetSchema = z.strictObject({
  ability1: z.number().int().nonnegative(),
  ability2: z.number().int().nonnegative(),
  hiddenAbility: z.number().int().nonnegative()
});

export const pokemonDexPresenceSchema = z.strictObject({
  armorDexIndex: z.number().int().nonnegative(),
  crownDexIndex: z.number().int().nonnegative(),
  isInAnyDex: z.boolean(),
  isPresentInGame: z.boolean(),
  regionalDexIndex: z.number().int().nonnegative()
});

export const pokemonPersonalDetailsSchema = z.strictObject({
  ability1: z.number().int().nonnegative().optional(),
  ability2: z.number().int().nonnegative().optional(),
  armorDexIndex: z.number().int().nonnegative().optional(),
  baseExperience: z.number().int().nonnegative().optional(),
  baseFriendship: z.number().int().nonnegative(),
  canNotDynamax: z.boolean(),
  catchRate: z.number().int().nonnegative(),
  color: z.number().int().nonnegative(),
  crownDexIndex: z.number().int().nonnegative().optional(),
  eggGroup1: z.number().int().nonnegative(),
  eggGroup2: z.number().int().nonnegative(),
  evYieldAttack: z.number().int().nonnegative(),
  evYieldDefense: z.number().int().nonnegative(),
  evYieldHP: z.number().int().nonnegative(),
  evYieldSpecialAttack: z.number().int().nonnegative(),
  evYieldSpecialDefense: z.number().int().nonnegative(),
  evYieldSpeed: z.number().int().nonnegative(),
  evolutionStage: z.number().int().nonnegative(),
  expGrowth: z.number().int().nonnegative(),
  form: z.number().int().nonnegative(),
  formCount: z.number().int().nonnegative(),
  formStatsIndex: z.number().int().nonnegative(),
  genderRatio: z.number().int().nonnegative(),
  hasSpriteForm: z.boolean(),
  hatchedSpecies: z.number().int().nonnegative(),
  hatchCycles: z.number().int().nonnegative(),
  heldItem1: z.number().int().nonnegative(),
  heldItem2: z.number().int().nonnegative(),
  heldItem3: z.number().int().nonnegative(),
  hiddenAbility: z.number().int().nonnegative().optional(),
  height: z.number().int().nonnegative().optional(),
  isPresentInGame: z.boolean(),
  isRegionalForm: z.boolean(),
  localFormIndex: z.number().int().nonnegative(),
  modelId: z.number().int().nonnegative(),
  regionalDexIndex: z.number().int().nonnegative().optional(),
  type1: z.number().int().nonnegative(),
  type2: z.number().int().nonnegative(),
  weight: z.number().int().nonnegative().optional()
});

export const pokemonEvolutionRecordSchema = z.strictObject({
  argument: z.number().int().nonnegative(),
  form: z.number().int().nonnegative(),
  level: z.number().int().nonnegative(),
  method: z.number().int().nonnegative(),
  slot: z.number().int().nonnegative(),
  species: z.number().int().nonnegative()
});

export const pokemonLearnsetMoveSchema = z.strictObject({
  level: z.number().int().nonnegative(),
  moveId: z.number().int().nonnegative(),
  moveName: z.string(),
  slot: z.number().int().nonnegative()
});

export const pokemonCompatibilityEntrySchema = z.strictObject({
  canLearn: z.boolean(),
  label: z.string(),
  moveId: z.number().int().nonnegative(),
  moveName: z.string(),
  slot: z.number().int().nonnegative()
});

export const pokemonCompatibilityGroupSchema = z.strictObject({
  enabledCount: z.number().int().nonnegative(),
  entries: z.array(pokemonCompatibilityEntrySchema),
  groupId: z.string(),
  label: z.string()
});

export const pokemonEditableFieldOptionSchema = z.strictObject({
  label: z.string(),
  value: z.number().int()
});

export const pokemonEditableFieldSchema = z.strictObject({
  field: z.string(),
  group: z.string(),
  label: z.string(),
  maximumValue: z.number().int().nullable(),
  minimumValue: z.number().int().nullable(),
  options: z.array(pokemonEditableFieldOptionSchema),
  valueKind: z.string()
});

export const pokemonRecordSchema = z.strictObject({
  abilities: pokemonAbilitySetSchema,
  baseExperience: z.number().int().nonnegative(),
  baseStats: pokemonBaseStatsSchema,
  catchRate: z.number().int().nonnegative(),
  compatibility: z.array(pokemonCompatibilityGroupSchema),
  dexPresence: pokemonDexPresenceSchema,
  evolutionStage: z.number().int().nonnegative(),
  evolutions: z.array(pokemonEvolutionRecordSchema),
  form: z.number().int().nonnegative(),
  formLabel: z.string(),
  genderRatio: z.number().int().nonnegative(),
  height: z.number().int().nonnegative(),
  learnset: z.array(pokemonLearnsetMoveSchema),
  name: z.string(),
  personal: pokemonPersonalDetailsSchema,
  personalId: z.number().int().nonnegative(),
  provenance: pokemonProvenanceSchema,
  speciesId: z.number().int().nonnegative(),
  type1: z.string(),
  type2: z.string(),
  weight: z.number().int().nonnegative()
});

export const pokemonWorkflowStatsSchema = z.strictObject({
  presentPokemonCount: z.number().int().nonnegative(),
  sourceFileCount: z.number().int().nonnegative(),
  totalEvolutionCount: z.number().int().nonnegative(),
  totalLearnsetMoveCount: z.number().int().nonnegative(),
  totalPokemonCount: z.number().int().nonnegative()
});

export const pokemonWorkflowSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  editableFields: z.array(pokemonEditableFieldSchema),
  pokemon: z.array(pokemonRecordSchema),
  stats: pokemonWorkflowStatsSchema,
  summary: workflowSummarySchema
});

export const loadPokemonWorkflowResponseSchema = z.strictObject({
  workflow: pokemonWorkflowSchema
});

export const updatePokemonFieldRequestSchema = z.strictObject({
  field: z.string(),
  paths: projectPathsSchema,
  personalId: z.number().int().nonnegative(),
  session: editSessionSchema.nullable(),
  value: z.string()
});

export const updatePokemonFieldResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: pokemonWorkflowSchema
});

export const updatePokemonLearnsetRequestSchema = z.strictObject({
  action: z.string(),
  level: z.number().int().nonnegative().nullable(),
  moveId: z.number().int().nonnegative().nullable(),
  paths: projectPathsSchema,
  personalId: z.number().int().nonnegative(),
  session: editSessionSchema.nullable(),
  slot: z.number().int().nonnegative().nullable()
});

export const updatePokemonLearnsetResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: pokemonWorkflowSchema
});

export const updatePokemonEvolutionRequestSchema = z.strictObject({
  action: z.string(),
  argument: z.number().int().nonnegative().nullable(),
  form: z.number().int().nonnegative().nullable(),
  level: z.number().int().nonnegative().nullable(),
  method: z.number().int().nonnegative().nullable(),
  paths: projectPathsSchema,
  personalId: z.number().int().nonnegative(),
  session: editSessionSchema.nullable(),
  slot: z.number().int().nonnegative().nullable(),
  species: z.number().int().nonnegative().nullable()
});

export const updatePokemonEvolutionResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: pokemonWorkflowSchema
});

export const moveProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema
});

export const moveStatChangeRecordSchema = z.strictObject({
  percent: z.number().int().nonnegative(),
  slot: z.number().int().nonnegative(),
  stage: z.number().int(),
  stat: z.number().int().nonnegative(),
  statName: z.string()
});

export const moveFlagRecordSchema = z.strictObject({
  enabled: z.boolean(),
  field: z.string(),
  label: z.string()
});

export const moveEditableFieldSchema = z.strictObject({
  field: z.string(),
  label: z.string(),
  maximumValue: z.number().int().nullable(),
  minimumValue: z.number().int().nullable(),
  valueKind: z.string()
});

export const moveRecordSchema = z.strictObject({
  accuracy: z.number().int().nonnegative(),
  canUseMove: z.boolean(),
  category: z.number().int().nonnegative(),
  categoryName: z.string(),
  critStage: z.number().int(),
  description: z.string().nullable(),
  effectSequence: z.number().int().nonnegative(),
  flags: z.array(moveFlagRecordSchema),
  flinch: z.number().int().nonnegative(),
  hitMax: z.number().int().nonnegative(),
  hitMin: z.number().int().nonnegative(),
  inflict: z.number().int().nonnegative(),
  inflictName: z.string(),
  inflictPercent: z.number().int().nonnegative(),
  maxMovePower: z.number().int().nonnegative(),
  moveId: z.number().int().nonnegative(),
  name: z.string(),
  power: z.number().int().nonnegative(),
  pp: z.number().int().nonnegative(),
  priority: z.number().int(),
  provenance: moveProvenanceSchema,
  quality: z.number().int().nonnegative(),
  rawHealing: z.number().int(),
  rawInflictCount: z.number().int().nonnegative(),
  recoil: z.number().int(),
  statChanges: z.array(moveStatChangeRecordSchema),
  target: z.number().int().nonnegative(),
  targetName: z.string(),
  turnMax: z.number().int().nonnegative(),
  turnMin: z.number().int().nonnegative(),
  type: z.number().int().nonnegative(),
  typeName: z.string(),
  version: z.number().int().nonnegative()
});

export const movesWorkflowStatsSchema = z.strictObject({
  activeFlagCount: z.number().int().nonnegative(),
  enabledMoveCount: z.number().int().nonnegative(),
  sourceFileCount: z.number().int().nonnegative(),
  totalMoveCount: z.number().int().nonnegative()
});

export const movesWorkflowSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  editableFields: z.array(moveEditableFieldSchema),
  moves: z.array(moveRecordSchema),
  stats: movesWorkflowStatsSchema,
  summary: workflowSummarySchema
});

export const loadMovesWorkflowResponseSchema = z.strictObject({
  workflow: movesWorkflowSchema
});

export const textProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema
});

export const textEntryRecordSchema = z.strictObject({
  canEdit: z.boolean(),
  editBlockedReason: z.string().nullable(),
  label: z.string(),
  language: z.string(),
  lineIndex: z.number().int().nonnegative(),
  provenance: textProvenanceSchema,
  sourceFile: z.string(),
  textId: z.number().int().nonnegative(),
  textKey: z.string(),
  value: z.string()
});

export const dialogueReferenceRecordSchema = z.strictObject({
  context: z.string(),
  dialogueId: z.string(),
  label: z.string(),
  preview: z.string(),
  provenance: textProvenanceSchema,
  textId: z.number().int().nonnegative()
});

export const textWorkflowStatsSchema = z.strictObject({
  dialogueReferenceCount: z.number().int().nonnegative(),
  sourceFileCount: z.number().int().nonnegative(),
  totalTextEntryCount: z.number().int().nonnegative()
});

export const textEditableFieldSchema = z.strictObject({
  field: z.string(),
  label: z.string(),
  maximumLength: z.number().int().nullable(),
  minimumLength: z.number().int().nullable(),
  valueKind: z.string()
});

export const textWorkflowSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  dialogueReferences: z.array(dialogueReferenceRecordSchema),
  editableFields: z.array(textEditableFieldSchema),
  entries: z.array(textEntryRecordSchema),
  stats: textWorkflowStatsSchema,
  summary: workflowSummarySchema
});

export const loadTextWorkflowResponseSchema = z.strictObject({
  workflow: textWorkflowSchema
});

export const trainerProvenanceSchema = z.strictObject({
  classFileState: projectFileGraphEntryStateSchema.nullable().default(null),
  classSourceFile: z.string().nullable().default(null),
  classSourceLayer: projectFileLayerSchema.nullable().default(null),
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema,
  teamFileState: projectFileGraphEntryStateSchema,
  teamSourceFile: z.string(),
  teamSourceLayer: projectFileLayerSchema
});

export const trainerPokemonStatsSchema = z.strictObject({
  attack: z.number().int().nonnegative(),
  defense: z.number().int().nonnegative(),
  hp: z.number().int().nonnegative(),
  specialAttack: z.number().int().nonnegative(),
  specialDefense: z.number().int().nonnegative(),
  speed: z.number().int().nonnegative()
});

export const trainerPokemonRecordSchema = z.strictObject({
  ability: z.number().int().nonnegative(),
  canDynamax: z.boolean(),
  canGigantamax: z.boolean(),
  dynamaxLevel: z.number().int().nonnegative(),
  evs: trainerPokemonStatsSchema,
  form: z.number().int().nonnegative(),
  gender: z.number().int().nonnegative(),
  heldItem: z.string().nullable(),
  heldItemId: z.number().int().nonnegative(),
  ivs: trainerPokemonStatsSchema,
  level: z.number().int().nonnegative(),
  moveIds: z.array(z.number().int().nonnegative()),
  moves: z.array(z.string()),
  nature: z.number().int().nonnegative(),
  shiny: z.boolean(),
  slot: z.number().int().nonnegative(),
  speciesId: z.number().int().nonnegative(),
  species: z.string()
});

export const trainerRecordSchema = z.strictObject({
  aiFlags: z.number().int().nonnegative().default(0),
  battleType: z.string(),
  battleTypeValue: z.number().int().nonnegative(),
  canEditClassBall: z.boolean().default(false),
  classBall: z.string().nullable().default(null),
  classBallId: z.number().int().nonnegative().nullable().default(null),
  classBallScope: z.string().default('Class file missing'),
  gift: z.number().int().nonnegative().default(0),
  heal: z.boolean().default(false),
  itemIds: z.array(z.number().int().nonnegative()).default([]),
  items: z.array(z.string()).default([]),
  location: z.string(),
  money: z.number().int().nonnegative().default(0),
  name: z.string(),
  provenance: trainerProvenanceSchema,
  team: z.array(trainerPokemonRecordSchema),
  trainerClass: z.string(),
  trainerClassId: z.number().int().nonnegative(),
  trainerId: z.number().int().nonnegative()
});

export const trainerEditableFieldOptionSchema = z.strictObject({
  label: z.string(),
  value: z.number().int()
});

export const trainerEditableFieldSchema = z.strictObject({
  field: z.string(),
  label: z.string(),
  maximumValue: z.number().int().nullable(),
  minimumValue: z.number().int().nullable(),
  options: z.array(trainerEditableFieldOptionSchema),
  valueKind: z.string()
});

export const trainersWorkflowStatsSchema = z.strictObject({
  sourceFileCount: z.number().int().nonnegative(),
  totalPokemonCount: z.number().int().nonnegative(),
  totalTrainerCount: z.number().int().nonnegative()
});

export const trainersWorkflowSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  editableFields: z.array(trainerEditableFieldSchema),
  stats: trainersWorkflowStatsSchema,
  summary: workflowSummarySchema,
  trainers: z.array(trainerRecordSchema)
});

export const loadTrainersWorkflowResponseSchema = z.strictObject({
  workflow: trainersWorkflowSchema
});

export const shopProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema
});

export const shopInventoryRecordSchema = z.strictObject({
  itemId: z.number().int().nonnegative(),
  itemName: z.string(),
  price: z.number().int().nonnegative(),
  slot: z.number().int().nonnegative(),
  stockLimit: z.number().int().nonnegative().nullable()
});

export const shopEditableFieldSchema = z.strictObject({
  field: z.string(),
  label: z.string(),
  maximumValue: z.number().int().nullable(),
  minimumValue: z.number().int().nullable(),
  valueKind: z.string()
});

export const shopRecordSchema = z.strictObject({
  currency: z.string(),
  inventory: z.array(shopInventoryRecordSchema),
  location: z.string(),
  name: z.string(),
  provenance: shopProvenanceSchema,
  shopId: z.string()
});

export const shopsWorkflowStatsSchema = z.strictObject({
  sourceFileCount: z.number().int().nonnegative(),
  totalInventoryItemCount: z.number().int().nonnegative(),
  totalShopCount: z.number().int().nonnegative()
});

export const shopsWorkflowSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  editableFields: z.array(shopEditableFieldSchema),
  shops: z.array(shopRecordSchema),
  stats: shopsWorkflowStatsSchema,
  summary: workflowSummarySchema
});

export const loadShopsWorkflowResponseSchema = z.strictObject({
  workflow: shopsWorkflowSchema
});

export const encounterProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema
});

export const encounterSlotRecordSchema = z.strictObject({
  form: z.number().int().nonnegative(),
  levelMax: z.number().int().nonnegative(),
  levelMin: z.number().int().nonnegative(),
  slot: z.number().int().nonnegative(),
  speciesId: z.number().int().nonnegative(),
  species: z.string(),
  timeOfDay: z.string().nullable(),
  weather: z.string(),
  weight: z.number().int().nonnegative()
});

export const encounterTableRecordSchema = z.strictObject({
  archiveMember: z.string(),
  area: z.string(),
  encounterType: z.string(),
  gameVersion: z.string(),
  location: z.string(),
  provenance: encounterProvenanceSchema,
  slots: z.array(encounterSlotRecordSchema),
  tableId: z.string()
});

export const encounterEditableFieldSchema = z.strictObject({
  field: z.string(),
  label: z.string(),
  maximumValue: z.number().int().nullable(),
  minimumValue: z.number().int().nullable(),
  valueKind: z.string()
});

export const encountersWorkflowStatsSchema = z.strictObject({
  sourceFileCount: z.number().int().nonnegative(),
  totalSlotCount: z.number().int().nonnegative(),
  totalTableCount: z.number().int().nonnegative()
});

export const encountersWorkflowSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  editableFields: z.array(encounterEditableFieldSchema),
  stats: encountersWorkflowStatsSchema,
  summary: workflowSummarySchema,
  tables: z.array(encounterTableRecordSchema)
});

export const loadEncountersWorkflowResponseSchema = z.strictObject({
  workflow: encountersWorkflowSchema
});

export const raidRewardProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema
});

export const raidRewardItemRecordSchema = z.strictObject({
  entryId: z.number().int().nonnegative(),
  itemId: z.number().int().nonnegative(),
  itemName: z.string(),
  quantity: z.number().int().nonnegative(),
  slot: z.number().int().nonnegative(),
  values: z.array(z.number().int().nonnegative()),
  weight: z.number().int().nonnegative()
});

export const raidRewardTableRecordSchema = z.strictObject({
  archiveMember: z.string(),
  denId: z.string(),
  gameVersion: z.string(),
  provenance: raidRewardProvenanceSchema,
  rank: z.number().int().nonnegative(),
  rewardKind: z.string(),
  rewardKindLabel: z.string(),
  rewards: z.array(raidRewardItemRecordSchema),
  sourceTableHash: z.string(),
  tableIndex: z.number().int().nonnegative(),
  tableId: z.string()
});

export const raidRewardEditableFieldSchema = z.strictObject({
  field: z.string(),
  label: z.string(),
  maximumValue: z.number().int().nullable(),
  minimumValue: z.number().int().nullable(),
  valueKind: z.string()
});

export const raidRewardsWorkflowStatsSchema = z.strictObject({
  sourceFileCount: z.number().int().nonnegative(),
  totalRewardItemCount: z.number().int().nonnegative(),
  totalTableCount: z.number().int().nonnegative()
});

export const raidRewardsWorkflowSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  editableFields: z.array(raidRewardEditableFieldSchema),
  stats: raidRewardsWorkflowStatsSchema,
  summary: workflowSummarySchema,
  tables: z.array(raidRewardTableRecordSchema)
});

export const loadRaidRewardsWorkflowResponseSchema = z.strictObject({
  workflow: raidRewardsWorkflowSchema
});

export const placementProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema
});

export const placedObjectRecordSchema = z.strictObject({
  archiveMember: z.string(),
  chance: z.number().int().nullable(),
  chanceIndex: z.number().int().nonnegative().nullable(),
  itemHash: z.string(),
  itemId: z.number().int().nonnegative().nullable(),
  itemName: z.string(),
  label: z.string(),
  map: z.string(),
  objectId: z.string(),
  objectIndex: z.number().int().nonnegative(),
  objectType: z.string(),
  provenance: placementProvenanceSchema,
  quantity: z.number().int().nonnegative(),
  rotationY: z.number(),
  scriptId: z.string().nullable(),
  x: z.number(),
  y: z.number(),
  zoneIndex: z.number().int().nonnegative(),
  z: z.number()
});

export const placementEditableFieldSchema = z.strictObject({
  field: z.string(),
  label: z.string(),
  maximumValue: z.number(),
  minimumValue: z.number(),
  valueKind: z.string()
});

export const placementWorkflowStatsSchema = z.strictObject({
  sourceFileCount: z.number().int().nonnegative(),
  totalAreaCount: z.number().int().nonnegative(),
  totalObjectCount: z.number().int().nonnegative()
});

export const placementWorkflowSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  editableFields: z.array(placementEditableFieldSchema),
  objects: z.array(placedObjectRecordSchema),
  stats: placementWorkflowStatsSchema,
  summary: workflowSummarySchema
});

export const loadPlacementWorkflowResponseSchema = z.strictObject({
  workflow: placementWorkflowSchema
});

export const flagworkSaveProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema
});

export const flagRecordSchema = z.strictObject({
  category: z.string(),
  defaultValue: z.string(),
  description: z.string(),
  flagId: z.string(),
  hash: z.string(),
  index: z.number().int().nonnegative(),
  kind: z.string(),
  low32Key: z.string(),
  name: z.string(),
  provenance: flagworkSaveProvenanceSchema,
  table: z.string(),
  valueKind: z.string()
});

export const saveBlockRecordSchema = z.strictObject({
  blockId: z.string(),
  description: z.string(),
  hash: z.string(),
  key: z.string(),
  kind: z.string(),
  name: z.string(),
  provenance: flagworkSaveProvenanceSchema,
  valueKind: z.string()
});

export const saveFileRecordSchema = z.strictObject({
  description: z.string(),
  fileName: z.string(),
  sha256: z.string(),
  sizeBytes: z.number().int().nonnegative(),
  status: z.string()
});

export const flagworkSaveWorkflowStatsSchema = z.strictObject({
  hasSaveFile: z.boolean(),
  sourceFileCount: z.number().int().nonnegative(),
  totalFlagCount: z.number().int().nonnegative(),
  totalSaveBlockCount: z.number().int().nonnegative()
});

export const flagworkSaveWorkflowSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  flags: z.array(flagRecordSchema),
  saveFile: saveFileRecordSchema.nullable(),
  saveBlocks: z.array(saveBlockRecordSchema),
  stats: flagworkSaveWorkflowStatsSchema,
  summary: workflowSummarySchema
});

export const loadFlagworkSaveWorkflowResponseSchema = z.strictObject({
  workflow: flagworkSaveWorkflowSchema
});

export const exeFsPatchProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema
});

export const exeFsPatchRecordSchema = z.strictObject({
  description: z.string(),
  details: z.array(z.string()),
  name: z.string(),
  patchId: z.string(),
  patchKind: z.string(),
  provenance: exeFsPatchProvenanceSchema,
  status: z.string(),
  targetFile: z.string()
});

export const exeFsSegmentRecordSchema = z.strictObject({
  compressedSize: z.string(),
  decompressedSize: z.string(),
  fileOffset: z.string(),
  hashStatus: z.string(),
  memoryOffset: z.string(),
  name: z.string(),
  provenance: exeFsPatchProvenanceSchema,
  segmentId: z.string(),
  sha256: z.string()
});

export const exeFsPatchCheckRecordSchema = z.strictObject({
  actual: z.string(),
  area: z.string(),
  checkId: z.string(),
  expected: z.string(),
  name: z.string(),
  notes: z.string(),
  offset: z.string(),
  patchId: z.string(),
  provenance: exeFsPatchProvenanceSchema,
  status: z.string()
});

export const exeFsPatchWorkflowStatsSchema = z.strictObject({
  failCount: z.number().int().nonnegative(),
  passCount: z.number().int().nonnegative(),
  sourceFileCount: z.number().int().nonnegative(),
  totalCheckCount: z.number().int().nonnegative(),
  totalPatchCount: z.number().int().nonnegative(),
  warningCount: z.number().int().nonnegative()
});

export const exeFsPatchWorkflowSchema = z.strictObject({
  checks: z.array(exeFsPatchCheckRecordSchema),
  diagnostics: z.array(apiDiagnosticSchema),
  patches: z.array(exeFsPatchRecordSchema),
  segments: z.array(exeFsSegmentRecordSchema),
  stats: exeFsPatchWorkflowStatsSchema,
  summary: workflowSummarySchema
});

export const loadExeFsPatchWorkflowResponseSchema = z.strictObject({
  workflow: exeFsPatchWorkflowSchema
});

export const stageExeFsPatchRequestSchema = z.strictObject({
  patchId: z.string(),
  paths: projectPathsSchema,
  session: editSessionSchema.nullable()
});

export const stageExeFsPatchResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: exeFsPatchWorkflowSchema
});

export const royalCandyProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema
});

export const royalCandyWorkflowStepRecordSchema = z.strictObject({
  description: z.string(),
  label: z.string(),
  step: z.number().int().nonnegative()
});

export const royalCandyWorkflowCheckRecordSchema = z.strictObject({
  area: z.string(),
  checkId: z.string(),
  message: z.string(),
  provenance: royalCandyProvenanceSchema,
  status: z.string(),
  target: z.string(),
  workflowId: z.string()
});

export const royalCandyOutputRecordSchema = z.strictObject({
  description: z.string(),
  outputId: z.string(),
  outputKind: z.string(),
  provenance: royalCandyProvenanceSchema,
  relativePath: z.string(),
  sourceFile: z.string(),
  status: z.string(),
  workflowId: z.string()
});

export const royalCandyWorkflowRecordSchema = z.strictObject({
  category: z.string(),
  description: z.string(),
  itemId: z.number().int().nonnegative(),
  mode: z.string(),
  name: z.string(),
  provenance: royalCandyProvenanceSchema,
  status: z.string(),
  steps: z.array(royalCandyWorkflowStepRecordSchema),
  target: z.string(),
  templateItemId: z.number().int().nonnegative(),
  workflowId: z.string()
});

export const royalCandyWorkflowStatsSchema = z.strictObject({
  failCount: z.number().int().nonnegative(),
  outputCount: z.number().int().nonnegative(),
  passCount: z.number().int().nonnegative(),
  sourceFileCount: z.number().int().nonnegative(),
  totalCheckCount: z.number().int().nonnegative(),
  totalStepCount: z.number().int().nonnegative(),
  totalWorkflowCount: z.number().int().nonnegative(),
  warningCount: z.number().int().nonnegative()
});

export const royalCandyWorkflowSchema = z.strictObject({
  checks: z.array(royalCandyWorkflowCheckRecordSchema),
  diagnostics: z.array(apiDiagnosticSchema),
  outputs: z.array(royalCandyOutputRecordSchema),
  stats: royalCandyWorkflowStatsSchema,
  summary: workflowSummarySchema,
  workflows: z.array(royalCandyWorkflowRecordSchema)
});

export const loadRoyalCandyWorkflowResponseSchema = z.strictObject({
  workflow: royalCandyWorkflowSchema
});

export const stageRoyalCandyWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  session: editSessionSchema.nullable(),
  workflowId: z.string()
});

export const stageRoyalCandyWorkflowResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: royalCandyWorkflowSchema
});

export const spreadsheetImportProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema
});

export const spreadsheetImportColumnRecordSchema = z.strictObject({
  column: z.number().int().nonnegative(),
  description: z.string(),
  header: z.string(),
  isRequired: z.boolean(),
  valueKind: z.string()
});

export const spreadsheetImportProfileRecordSchema = z.strictObject({
  columns: z.array(spreadsheetImportColumnRecordSchema),
  description: z.string(),
  name: z.string(),
  profileId: z.string(),
  provenance: spreadsheetImportProvenanceSchema,
  sourceKind: z.string(),
  status: z.string(),
  targetWorkflow: z.string()
});

export const spreadsheetImportWorkflowStatsSchema = z.strictObject({
  sourceFileCount: z.number().int().nonnegative(),
  totalColumnCount: z.number().int().nonnegative(),
  totalProfileCount: z.number().int().nonnegative()
});

export const spreadsheetImportWorkflowSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  profiles: z.array(spreadsheetImportProfileRecordSchema),
  stats: spreadsheetImportWorkflowStatsSchema,
  summary: workflowSummarySchema
});

export const loadSpreadsheetImportWorkflowResponseSchema = z.strictObject({
  workflow: spreadsheetImportWorkflowSchema
});

export const spreadsheetImportCellPreviewRecordSchema = z.strictObject({
  field: z.string(),
  header: z.string(),
  message: z.string(),
  status: z.string(),
  value: z.string()
});

export const spreadsheetImportRowPreviewRecordSchema = z.strictObject({
  cells: z.array(spreadsheetImportCellPreviewRecordSchema),
  diagnostics: z.array(apiDiagnosticSchema),
  recordId: z.string(),
  rowNumber: z.number().int().nonnegative(),
  status: z.string(),
  summary: z.string()
});

export const spreadsheetImportPreviewSchema = z.strictObject({
  acceptedRowCount: z.number().int().nonnegative(),
  profileId: z.string(),
  rejectedRowCount: z.number().int().nonnegative(),
  rows: z.array(spreadsheetImportRowPreviewRecordSchema),
  skippedRowCount: z.number().int().nonnegative(),
  sourcePath: z.string(),
  totalRowCount: z.number().int().nonnegative()
});

export const previewSpreadsheetImportResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  preview: spreadsheetImportPreviewSchema,
  session: editSessionSchema,
  workflow: spreadsheetImportWorkflowSchema
});

export const updateItemFieldRequestSchema = z.strictObject({
  field: z.string(),
  itemId: z.number().int().nonnegative(),
  paths: projectPathsSchema,
  session: editSessionSchema.nullable(),
  value: z.string()
});

export const updateItemFieldResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: itemsWorkflowSchema
});

export const updateMoveFieldRequestSchema = z.strictObject({
  field: z.string(),
  moveId: z.number().int().nonnegative(),
  paths: projectPathsSchema,
  session: editSessionSchema.nullable(),
  value: z.string()
});

export const updateMoveFieldResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: movesWorkflowSchema
});

export const updateTextEntryRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  session: editSessionSchema.nullable(),
  textKey: z.string(),
  value: z.string()
});

export const updateTextEntryResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: textWorkflowSchema
});

export const updateTrainerFieldRequestSchema = z.strictObject({
  field: z.string(),
  paths: projectPathsSchema,
  session: editSessionSchema.nullable(),
  slot: z.number().int().nonnegative().nullable(),
  trainerId: z.number().int().nonnegative(),
  value: z.string()
});

export const updateTrainerFieldResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: trainersWorkflowSchema
});

export const updateShopInventoryItemRequestSchema = z.strictObject({
  field: z.string(),
  paths: projectPathsSchema,
  session: editSessionSchema.nullable(),
  shopId: z.string(),
  slot: z.number().int().nonnegative(),
  value: z.string()
});

export const updateShopInventoryItemResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: shopsWorkflowSchema
});

export const updateEncounterSlotFieldRequestSchema = z.strictObject({
  field: z.string(),
  paths: projectPathsSchema,
  session: editSessionSchema.nullable(),
  slot: z.number().int().nonnegative(),
  tableId: z.string(),
  value: z.string()
});

export const updateEncounterSlotFieldResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: encountersWorkflowSchema
});

export const updateRaidRewardFieldRequestSchema = z.strictObject({
  field: z.string(),
  paths: projectPathsSchema,
  session: editSessionSchema.nullable(),
  slot: z.number().int().nonnegative(),
  tableId: z.string(),
  value: z.string()
});

export const updateRaidRewardFieldResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: raidRewardsWorkflowSchema
});

export const updatePlacementObjectFieldRequestSchema = z.strictObject({
  field: z.string(),
  objectId: z.string(),
  paths: projectPathsSchema,
  session: editSessionSchema.nullable(),
  value: z.string()
});

export const updatePlacementObjectFieldResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: placementWorkflowSchema
});

export const startEditSessionResponseSchema = z.strictObject({
  session: editSessionSchema
});

export const validateEditSessionResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  isValid: z.boolean(),
  session: editSessionSchema
});

export const plannedFileWriteSchema = z.strictObject({
  reason: z.string(),
  replacesExistingOutput: z.boolean(),
  sources: z.array(projectFileReferenceSchema),
  targetRelativePath: z.string()
});

export const changePlanSchema = z.strictObject({
  canApply: z.boolean(),
  diagnostics: z.array(apiDiagnosticSchema),
  sessionId: z.string(),
  writes: z.array(plannedFileWriteSchema)
});

export const createChangePlanResponseSchema = z.strictObject({
  changePlan: changePlanSchema
});

export const applyResultSchema = z.strictObject({
  applyId: z.string(),
  diagnostics: z.array(apiDiagnosticSchema),
  writtenFiles: z.array(z.string())
});

export const applyChangePlanResponseSchema = z.strictObject({
  applyResult: applyResultSchema
});

export function createBridgeRequestSchema<TPayloadSchema extends ZodTypeAny>(
  payloadSchema: TPayloadSchema
) {
  return z.strictObject({
    command: kmCommandNameSchema,
    payload: payloadSchema,
    requestId: z.string().nullable().optional()
  });
}

export function createBridgeResponseSchema<TPayloadSchema extends ZodTypeAny>(
  payloadSchema: TPayloadSchema
) {
  return z
    .strictObject({
      error: apiErrorSchema.nullable().optional(),
      payload: payloadSchema.nullable().optional(),
      requestId: z.string().nullable().optional()
    })
    .superRefine((response, context) => {
      const hasPayload = response.payload !== null && response.payload !== undefined;
      const hasError = response.error !== null && response.error !== undefined;

      // A bridge response must be exactly one of success or failure; never both and never neither.
      if (hasPayload === hasError) {
        context.addIssue({
          code: 'custom',
          message: 'Bridge responses must contain either payload or error.'
        });
      }
    });
}

export type ApiDiagnostic = z.infer<typeof apiDiagnosticSchema>;
export type ApiError = z.infer<typeof apiErrorSchema>;
export type ApplyChangePlanRequest = z.infer<typeof applyChangePlanRequestSchema>;
export type ApplyChangePlanResponse = z.infer<typeof applyChangePlanResponseSchema>;
export type ApplyResult = z.infer<typeof applyResultSchema>;
export type ChangePlan = z.infer<typeof changePlanSchema>;
export type CreateChangePlanRequest = z.infer<typeof createChangePlanRequestSchema>;
export type CreateChangePlanResponse = z.infer<typeof createChangePlanResponseSchema>;
export type EditSession = z.infer<typeof editSessionSchema>;
export type ItemEditableField = z.infer<typeof itemEditableFieldSchema>;
export type ItemRecord = z.infer<typeof itemRecordSchema>;
export type ItemsWorkflow = z.infer<typeof itemsWorkflowSchema>;
export type PokemonEditableField = z.infer<typeof pokemonEditableFieldSchema>;
export type PokemonEvolutionRecord = z.infer<typeof pokemonEvolutionRecordSchema>;
export type PokemonLearnsetMove = z.infer<typeof pokemonLearnsetMoveSchema>;
export type PokemonCompatibilityEntry = z.infer<typeof pokemonCompatibilityEntrySchema>;
export type PokemonCompatibilityGroup = z.infer<typeof pokemonCompatibilityGroupSchema>;
export type PokemonRecord = z.infer<typeof pokemonRecordSchema>;
export type PokemonWorkflow = z.infer<typeof pokemonWorkflowSchema>;
export type MoveEditableField = z.infer<typeof moveEditableFieldSchema>;
export type MoveFlagRecord = z.infer<typeof moveFlagRecordSchema>;
export type MoveRecord = z.infer<typeof moveRecordSchema>;
export type MoveStatChangeRecord = z.infer<typeof moveStatChangeRecordSchema>;
export type MovesWorkflow = z.infer<typeof movesWorkflowSchema>;
export type TextEditableField = z.infer<typeof textEditableFieldSchema>;
export type TextEntryRecord = z.infer<typeof textEntryRecordSchema>;
export type TextWorkflow = z.infer<typeof textWorkflowSchema>;
export type TrainerEditableField = z.infer<typeof trainerEditableFieldSchema>;
export type TrainerPokemonRecord = z.infer<typeof trainerPokemonRecordSchema>;
export type TrainerRecord = z.infer<typeof trainerRecordSchema>;
export type TrainersWorkflow = z.infer<typeof trainersWorkflowSchema>;
export type ShopEditableField = z.infer<typeof shopEditableFieldSchema>;
export type ShopInventoryRecord = z.infer<typeof shopInventoryRecordSchema>;
export type ShopRecord = z.infer<typeof shopRecordSchema>;
export type ShopsWorkflow = z.infer<typeof shopsWorkflowSchema>;
export type EncounterEditableField = z.infer<typeof encounterEditableFieldSchema>;
export type EncounterSlotRecord = z.infer<typeof encounterSlotRecordSchema>;
export type EncounterTableRecord = z.infer<typeof encounterTableRecordSchema>;
export type EncountersWorkflow = z.infer<typeof encountersWorkflowSchema>;
export type RaidRewardEditableField = z.infer<typeof raidRewardEditableFieldSchema>;
export type RaidRewardItemRecord = z.infer<typeof raidRewardItemRecordSchema>;
export type RaidRewardTableRecord = z.infer<typeof raidRewardTableRecordSchema>;
export type RaidRewardsWorkflow = z.infer<typeof raidRewardsWorkflowSchema>;
export type PlacedObjectRecord = z.infer<typeof placedObjectRecordSchema>;
export type PlacementEditableField = z.infer<typeof placementEditableFieldSchema>;
export type PlacementWorkflow = z.infer<typeof placementWorkflowSchema>;
export type FlagRecord = z.infer<typeof flagRecordSchema>;
export type SaveBlockRecord = z.infer<typeof saveBlockRecordSchema>;
export type SaveFileRecord = z.infer<typeof saveFileRecordSchema>;
export type FlagworkSaveWorkflow = z.infer<typeof flagworkSaveWorkflowSchema>;
export type ExeFsPatchCheckRecord = z.infer<typeof exeFsPatchCheckRecordSchema>;
export type ExeFsPatchRecord = z.infer<typeof exeFsPatchRecordSchema>;
export type ExeFsSegmentRecord = z.infer<typeof exeFsSegmentRecordSchema>;
export type ExeFsPatchWorkflow = z.infer<typeof exeFsPatchWorkflowSchema>;
export type RoyalCandyOutputRecord = z.infer<typeof royalCandyOutputRecordSchema>;
export type RoyalCandyWorkflowCheckRecord = z.infer<
  typeof royalCandyWorkflowCheckRecordSchema
>;
export type RoyalCandyWorkflowRecord = z.infer<typeof royalCandyWorkflowRecordSchema>;
export type RoyalCandyWorkflow = z.infer<typeof royalCandyWorkflowSchema>;
export type SpreadsheetImportProfileRecord = z.infer<
  typeof spreadsheetImportProfileRecordSchema
>;
export type SpreadsheetImportPreview = z.infer<typeof spreadsheetImportPreviewSchema>;
export type SpreadsheetImportWorkflow = z.infer<typeof spreadsheetImportWorkflowSchema>;
export type ListWorkflowsRequest = z.infer<typeof listWorkflowsRequestSchema>;
export type ListWorkflowsResponse = z.infer<typeof listWorkflowsResponseSchema>;
export type LoadItemsWorkflowRequest = z.infer<typeof loadItemsWorkflowRequestSchema>;
export type LoadItemsWorkflowResponse = z.infer<typeof loadItemsWorkflowResponseSchema>;
export type LoadPokemonWorkflowRequest = z.infer<typeof loadPokemonWorkflowRequestSchema>;
export type LoadPokemonWorkflowResponse = z.infer<typeof loadPokemonWorkflowResponseSchema>;
export type UpdatePokemonFieldRequest = z.infer<typeof updatePokemonFieldRequestSchema>;
export type UpdatePokemonFieldResponse = z.infer<typeof updatePokemonFieldResponseSchema>;
export type UpdatePokemonLearnsetRequest = z.infer<typeof updatePokemonLearnsetRequestSchema>;
export type UpdatePokemonLearnsetResponse = z.infer<typeof updatePokemonLearnsetResponseSchema>;
export type UpdatePokemonEvolutionRequest = z.infer<typeof updatePokemonEvolutionRequestSchema>;
export type UpdatePokemonEvolutionResponse = z.infer<typeof updatePokemonEvolutionResponseSchema>;
export type LoadMovesWorkflowRequest = z.infer<typeof loadMovesWorkflowRequestSchema>;
export type LoadMovesWorkflowResponse = z.infer<typeof loadMovesWorkflowResponseSchema>;
export type UpdateMoveFieldRequest = z.infer<typeof updateMoveFieldRequestSchema>;
export type UpdateMoveFieldResponse = z.infer<typeof updateMoveFieldResponseSchema>;
export type LoadTextWorkflowRequest = z.infer<typeof loadTextWorkflowRequestSchema>;
export type LoadTextWorkflowResponse = z.infer<typeof loadTextWorkflowResponseSchema>;
export type LoadTrainersWorkflowRequest = z.infer<typeof loadTrainersWorkflowRequestSchema>;
export type LoadTrainersWorkflowResponse = z.infer<typeof loadTrainersWorkflowResponseSchema>;
export type LoadShopsWorkflowRequest = z.infer<typeof loadShopsWorkflowRequestSchema>;
export type LoadShopsWorkflowResponse = z.infer<typeof loadShopsWorkflowResponseSchema>;
export type LoadEncountersWorkflowRequest = z.infer<typeof loadEncountersWorkflowRequestSchema>;
export type LoadEncountersWorkflowResponse = z.infer<typeof loadEncountersWorkflowResponseSchema>;
export type LoadRaidRewardsWorkflowRequest = z.infer<typeof loadRaidRewardsWorkflowRequestSchema>;
export type LoadRaidRewardsWorkflowResponse = z.infer<typeof loadRaidRewardsWorkflowResponseSchema>;
export type UpdateRaidRewardFieldRequest = z.infer<typeof updateRaidRewardFieldRequestSchema>;
export type UpdateRaidRewardFieldResponse = z.infer<typeof updateRaidRewardFieldResponseSchema>;
export type LoadPlacementWorkflowRequest = z.infer<typeof loadPlacementWorkflowRequestSchema>;
export type LoadPlacementWorkflowResponse = z.infer<typeof loadPlacementWorkflowResponseSchema>;
export type UpdatePlacementObjectFieldRequest = z.infer<
  typeof updatePlacementObjectFieldRequestSchema
>;
export type UpdatePlacementObjectFieldResponse = z.infer<
  typeof updatePlacementObjectFieldResponseSchema
>;
export type LoadFlagworkSaveWorkflowRequest = z.infer<typeof loadFlagworkSaveWorkflowRequestSchema>;
export type LoadFlagworkSaveWorkflowResponse = z.infer<typeof loadFlagworkSaveWorkflowResponseSchema>;
export type LoadExeFsPatchWorkflowRequest = z.infer<typeof loadExeFsPatchWorkflowRequestSchema>;
export type LoadExeFsPatchWorkflowResponse = z.infer<typeof loadExeFsPatchWorkflowResponseSchema>;
export type StageExeFsPatchRequest = z.infer<typeof stageExeFsPatchRequestSchema>;
export type StageExeFsPatchResponse = z.infer<typeof stageExeFsPatchResponseSchema>;
export type LoadRoyalCandyWorkflowRequest = z.infer<typeof loadRoyalCandyWorkflowRequestSchema>;
export type LoadRoyalCandyWorkflowResponse = z.infer<typeof loadRoyalCandyWorkflowResponseSchema>;
export type StageRoyalCandyWorkflowRequest = z.infer<
  typeof stageRoyalCandyWorkflowRequestSchema
>;
export type StageRoyalCandyWorkflowResponse = z.infer<
  typeof stageRoyalCandyWorkflowResponseSchema
>;
export type LoadSpreadsheetImportWorkflowRequest = z.infer<
  typeof loadSpreadsheetImportWorkflowRequestSchema
>;
export type LoadSpreadsheetImportWorkflowResponse = z.infer<
  typeof loadSpreadsheetImportWorkflowResponseSchema
>;
export type PreviewSpreadsheetImportRequest = z.infer<
  typeof previewSpreadsheetImportRequestSchema
>;
export type PreviewSpreadsheetImportResponse = z.infer<
  typeof previewSpreadsheetImportResponseSchema
>;
export type OpenProjectRequest = z.infer<typeof openProjectRequestSchema>;
export type OpenProjectResponse = z.infer<typeof openProjectResponseSchema>;
export type ProjectFileGraph = z.infer<typeof projectFileGraphSchema>;
export type ProjectHealth = z.infer<typeof projectHealthSchema>;
export type ProjectPathRole = z.infer<typeof projectPathRoleSchema>;
export type ProjectPathValidation = z.infer<typeof projectPathValidationSchema>;
export type RefreshFileGraphRequest = z.infer<typeof refreshFileGraphRequestSchema>;
export type RefreshFileGraphResponse = z.infer<typeof refreshFileGraphResponseSchema>;
export type StartEditSessionRequest = z.infer<typeof startEditSessionRequestSchema>;
export type StartEditSessionResponse = z.infer<typeof startEditSessionResponseSchema>;
export type UpdateItemFieldRequest = z.infer<typeof updateItemFieldRequestSchema>;
export type UpdateItemFieldResponse = z.infer<typeof updateItemFieldResponseSchema>;
export type UpdateTextEntryRequest = z.infer<typeof updateTextEntryRequestSchema>;
export type UpdateTextEntryResponse = z.infer<typeof updateTextEntryResponseSchema>;
export type UpdateTrainerFieldRequest = z.infer<typeof updateTrainerFieldRequestSchema>;
export type UpdateTrainerFieldResponse = z.infer<typeof updateTrainerFieldResponseSchema>;
export type UpdateShopInventoryItemRequest = z.infer<
  typeof updateShopInventoryItemRequestSchema
>;
export type UpdateShopInventoryItemResponse = z.infer<
  typeof updateShopInventoryItemResponseSchema
>;
export type UpdateEncounterSlotFieldRequest = z.infer<
  typeof updateEncounterSlotFieldRequestSchema
>;
export type UpdateEncounterSlotFieldResponse = z.infer<
  typeof updateEncounterSlotFieldResponseSchema
>;
export type ValidateEditSessionRequest = z.infer<typeof validateEditSessionRequestSchema>;
export type ValidateEditSessionResponse = z.infer<typeof validateEditSessionResponseSchema>;
export type ValidateProjectRequest = z.infer<typeof validateProjectRequestSchema>;
export type ValidateProjectResponse = z.infer<typeof validateProjectResponseSchema>;
export type WorkflowSummary = z.infer<typeof workflowSummarySchema>;
