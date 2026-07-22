/* SPDX-License-Identifier: GPL-3.0-only */
import { z, type ZodTypeAny } from 'zod';
export const kmCommandNameValues = [
  'project.open',
  'project.validate',
  'project.fileGraph.refresh',
  'workflow.list',
  'items.load',
  'items.field.update', 'items.fields.update',
  'pokemon.load',
  'pokemon.field.update', 'pokemon.fields.update',
  'pokemon.learnset.update',
  'pokemon.evolution.update',
  'pokemon.dex.swap',
  'moves.load',
  'moves.field.update', 'moves.fields.update',
  'text.load',
  'text.entry.update',
  'trainers.load',
  'trainers.field.update', 'trainers.fields.update',
  'giftPokemon.load',
  'giftPokemon.field.update', 'giftPokemon.fields.update',
  'tradePokemon.load',
  'tradePokemon.field.update', 'tradePokemon.fields.update',
  'staticEncounters.load',
  'staticEncounters.field.update',
  'staticEncounters.fields.update',
  'rentalPokemon.load',
  'rentalPokemon.field.update',
  'rentalPokemon.fields.update',
  'dynamaxAdventures.load',
  'dynamaxAdventures.field.update', 'dynamaxAdventures.defaults.preview',
  'dynamaxAdventures.repair.stage',
  'dynamaxAdventures.restore.stage',
  'shops.load',
  'shops.inventory.update',
  'encounters.load',
  'encounters.slots.update',
  'raidBattles.load',
  'raidBattles.slot.update',
  'raidBattles.slots.update',
  'teraRaids.load',
  'teraRaids.field.update', 'teraRaids.fields.update',
  'raidRewards.load',
  'raidRewards.reward.update',
  'raidRewards.rewards.update',
  'raidBonusRewards.load',
  'raidBonusRewards.reward.update',
  'raidBonusRewards.rewards.update',
  'placement.load',
  'placement.objects.update',
  'behavior.load',
  'behavior.entry.update',
  'behavior.fields.update',
  'flagworkSave.load',
  'bagHook.load',
  'bagHook.install.stage',
  'bagHook.uninstall.stage',
  'catchCap.load',
  'catchCap.stage',
  'catchCap.uninstall.stage',
  'hyperTraining.load',
  'hyperTraining.stage',
  'shinyRate.load',
  'shinyRate.stage',
  'typeChart.load',
  'typeChart.stage', 'typeChart.uninstall.stage', 'fairyGymBoosts.load', 'fairyGymBoosts.stage',
  'angeFight.load',
  'angeFight.stage',
  'angeFight.uninstall.stage',
  'fashionUnlock.load',
  'fashionUnlock.install.stage',
  'fashionUnlock.uninstall.stage',
  'gymUniformRemoval.load', 'gymUniformRemoval.install.stage',
  'gymUniformRemoval.uninstall.stage', 'hyperspaceBypass.load', 'hyperspaceBypass.install.stage', 'hyperspaceBypass.uninstall.stage',
  'ivScreen.load',
  'ivScreen.install.stage',
  'ivScreen.uninstall.stage',
  'exefsPatches.load',
  'exefsPatches.patch.stage',
  'royalCandy.load',
  'royalCandy.workflow.stage',
  'startingItems.load',
  'startingItems.stage',
  'npcItemGift.load',
  'npcItemGift.stage',
  'spreadsheetImport.load',
  'spreadsheetImport.preview',
  'modMerger.load',
  'modMerger.stage',
  'modMerger.apply',
  'svModMerger.load',
  'svModMerger.stage',
  'svModMerger.apply',
  'zaModMerger.load',
  'zaModMerger.stage',
  'zaModMerger.apply',
  'svCache.status', 'svCache.settings.update', 'svCache.clear', 'svCache.warmup.step',
  'zaCache.status', 'zaCache.settings.update', 'zaCache.clear', 'zaCache.warmup.step',
  'fpsPatch.load', 'fpsPatch.apply', 'fpsPatch.restore',
  'profanityFilter.load', 'profanityFilter.apply', 'profanityFilter.restore',
  'randomizer.seed.import',
  'randomizer.apply',
  'randomizer.restore',
  'gameDump.load',
  'gameDump.run',
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
  updateItemFields: 'items.fields.update',
  loadPokemonWorkflow: 'pokemon.load',
  updatePokemonField: 'pokemon.field.update',
  updatePokemonFields: 'pokemon.fields.update',
  updatePokemonLearnset: 'pokemon.learnset.update',
  updatePokemonEvolution: 'pokemon.evolution.update',
  swapPokemonDexPlacement: 'pokemon.dex.swap',
  loadMovesWorkflow: 'moves.load',
  updateMoveField: 'moves.field.update',
  updateMoveFields: 'moves.fields.update',
  loadTextWorkflow: 'text.load',
  updateTextEntry: 'text.entry.update',
  loadTrainersWorkflow: 'trainers.load',
  updateTrainerField: 'trainers.field.update',
  updateTrainerFields: 'trainers.fields.update',
  loadGiftPokemonWorkflow: 'giftPokemon.load',
  updateGiftPokemonField: 'giftPokemon.field.update',
  updateGiftPokemonFields: 'giftPokemon.fields.update',
  loadTradePokemonWorkflow: 'tradePokemon.load',
  updateTradePokemonField: 'tradePokemon.field.update',
  updateTradePokemonFields: 'tradePokemon.fields.update',
  loadStaticEncountersWorkflow: 'staticEncounters.load',
  updateStaticEncounterField: 'staticEncounters.field.update',
  updateStaticEncounterFields: 'staticEncounters.fields.update',
  loadRentalPokemonWorkflow: 'rentalPokemon.load',
  updateRentalPokemonField: 'rentalPokemon.field.update',
  updateRentalPokemonFields: 'rentalPokemon.fields.update',
  loadDynamaxAdventuresWorkflow: 'dynamaxAdventures.load',
  updateDynamaxAdventureField: 'dynamaxAdventures.field.update', previewDynamaxAdventureDefaults: 'dynamaxAdventures.defaults.preview',
  stageDynamaxAdventureRepair: 'dynamaxAdventures.repair.stage',
  stageDynamaxAdventureRestore: 'dynamaxAdventures.restore.stage',
  loadShopsWorkflow: 'shops.load',
  updateShopInventoryItem: 'shops.inventory.update',
  loadEncountersWorkflow: 'encounters.load',
  updateEncounterSlotFields: 'encounters.slots.update',
  loadRaidBattlesWorkflow: 'raidBattles.load',
  updateRaidBattleSlotField: 'raidBattles.slot.update',
  updateRaidBattleSlotFields: 'raidBattles.slots.update',
  loadTeraRaidsWorkflow: 'teraRaids.load',
  updateTeraRaidField: 'teraRaids.field.update',
  updateTeraRaidFields: 'teraRaids.fields.update',
  loadRaidRewardsWorkflow: 'raidRewards.load',
  updateRaidRewardField: 'raidRewards.reward.update',
  updateRaidRewardFields: 'raidRewards.rewards.update',
  loadRaidBonusRewardsWorkflow: 'raidBonusRewards.load',
  updateRaidBonusRewardField: 'raidBonusRewards.reward.update',
  updateRaidBonusRewardFields: 'raidBonusRewards.rewards.update',
  loadPlacementWorkflow: 'placement.load',
  updatePlacementObjectFields: 'placement.objects.update',
  loadBehaviorWorkflow: 'behavior.load',
  updateBehaviorEntryField: 'behavior.entry.update',
  updateBehaviorEntryFields: 'behavior.fields.update',
  loadFlagworkSaveWorkflow: 'flagworkSave.load',
  loadBagHookWorkflow: 'bagHook.load',
  stageBagHookInstall: 'bagHook.install.stage',
  stageBagHookUninstall: 'bagHook.uninstall.stage',
  loadCatchCapWorkflow: 'catchCap.load',
  stageCatchCap: 'catchCap.stage',
  stageCatchCapUninstall: 'catchCap.uninstall.stage',
  loadHyperTrainingWorkflow: 'hyperTraining.load',
  stageHyperTraining: 'hyperTraining.stage',
  loadShinyRateWorkflow: 'shinyRate.load',
  stageShinyRate: 'shinyRate.stage',
  loadTypeChartWorkflow: 'typeChart.load',
  stageTypeChart: 'typeChart.stage',
  stageTypeChartUninstall: 'typeChart.uninstall.stage',
  loadAngeFightWorkflow: 'angeFight.load',
  stageAngeFight: 'angeFight.stage',
  stageAngeFightUninstall: 'angeFight.uninstall.stage',
  loadFairyGymBoostsWorkflow: 'fairyGymBoosts.load', stageFairyGymBoosts: 'fairyGymBoosts.stage',
  loadFashionUnlockWorkflow: 'fashionUnlock.load',
  stageFashionUnlockInstall: 'fashionUnlock.install.stage',
  stageFashionUnlockUninstall: 'fashionUnlock.uninstall.stage',
  loadGymUniformRemovalWorkflow: 'gymUniformRemoval.load',
  stageGymUniformRemovalInstall: 'gymUniformRemoval.install.stage',
  stageGymUniformRemovalUninstall: 'gymUniformRemoval.uninstall.stage',
  loadHyperspaceBypassWorkflow: 'hyperspaceBypass.load', stageHyperspaceBypassInstall: 'hyperspaceBypass.install.stage', stageHyperspaceBypassUninstall: 'hyperspaceBypass.uninstall.stage',
  loadIvScreenWorkflow: 'ivScreen.load',
  stageIvScreenInstall: 'ivScreen.install.stage',
  stageIvScreenUninstall: 'ivScreen.uninstall.stage',
  loadExeFsPatchWorkflow: 'exefsPatches.load',
  stageExeFsPatch: 'exefsPatches.patch.stage',
  loadRoyalCandyWorkflow: 'royalCandy.load',
  stageRoyalCandyWorkflow: 'royalCandy.workflow.stage',
  loadStartingItemsWorkflow: 'startingItems.load',
  stageStartingItems: 'startingItems.stage',
  loadNpcItemGiftWorkflow: 'npcItemGift.load',
  stageNpcItemGift: 'npcItemGift.stage',
  loadSpreadsheetImportWorkflow: 'spreadsheetImport.load',
  previewSpreadsheetImport: 'spreadsheetImport.preview',
  loadModMergerWorkflow: 'modMerger.load',
  stageModMerge: 'modMerger.stage',
  applyModMerge: 'modMerger.apply',
  loadSvModMergerWorkflow: 'svModMerger.load', stageSvModMerge: 'svModMerger.stage', applySvModMerge: 'svModMerger.apply',
  loadZaModMergerWorkflow: 'zaModMerger.load', stageZaModMerge: 'zaModMerger.stage', applyZaModMerge: 'zaModMerger.apply',
  getSvCacheStatus: 'svCache.status', updateSvCacheSettings: 'svCache.settings.update', clearSvCache: 'svCache.clear', warmupSvCacheStep: 'svCache.warmup.step',
  getZaCacheStatus: 'zaCache.status', updateZaCacheSettings: 'zaCache.settings.update', clearZaCache: 'zaCache.clear', warmupZaCacheStep: 'zaCache.warmup.step',
  loadFpsPatch: 'fpsPatch.load', applyFpsPatch: 'fpsPatch.apply', restoreFpsPatch: 'fpsPatch.restore',
  loadProfanityFilter: 'profanityFilter.load', applyProfanityFilter: 'profanityFilter.apply', restoreProfanityFilter: 'profanityFilter.restore',
  importRandomizerSeed: 'randomizer.seed.import',
  applyRandomizer: 'randomizer.apply',
  restoreRandomizer: 'randomizer.restore',
  loadGameDumpWorkflow: 'gameDump.load', runGameDump: 'gameDump.run',
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
export const projectGameSchema = z.enum(['sword', 'shield', 'scarlet', 'violet', 'za']);
export type ProjectGame = z.infer<typeof projectGameSchema>;
export const projectPathsSchema = z.strictObject({
  baseExeFsPath: z.string().nullable(), baseRomFsPath: z.string().nullable(), gameTextLanguage: z.string().nullable().optional(), outputRootPath: z.string().nullable(), pokemonLegendsZASupportFolderPath: z.string().nullable().optional(), saveFilePath: z.string().nullable(), scarletVioletSupportFolderPath: z.string().nullable().optional(), selectedGame: projectGameSchema.nullable().default(null)
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

export const textWorkflowQuerySchema = z.strictObject({
  limit: z.number().int().positive().nullable().optional(),
  offset: z.number().int().nonnegative().nullable().optional(),
  searchText: z.string().nullable().optional()
});

export const loadTextWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  query: textWorkflowQuerySchema.nullable().optional()
});

export const loadTrainersWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadGiftPokemonWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadTradePokemonWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadStaticEncountersWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadRentalPokemonWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const dynamaxAdventureEditableFieldNameSchema = z.enum([
  'species',
  'form',
  'level',
  'ability',
  'gigantamaxState',
  'move0Id',
  'move1Id',
  'move2Id',
  'move3Id',
  'guaranteedPerfectIvs',
  'ivAttack',
  'ivDefense',
  'ivSpecialAttack',
  'ivSpecialDefense',
  'ivSpeed'
]);

const dynamaxAdventureEncounterCount = 273;
const dynamaxAdventureNormalEncounterCount = 226;
const dynamaxAdventureFieldRanges = {
  ability: [0, 2],
  form: [0, 255],
  gigantamaxState: [0, 2],
  guaranteedPerfectIvs: [0, 6],
  ivAttack: [-1, 31],
  ivDefense: [-1, 31],
  ivSpecialAttack: [-1, 31],
  ivSpecialDefense: [-1, 31],
  ivSpeed: [-1, 31],
  level: [1, 100],
  move0Id: [0, 826],
  move1Id: [0, 826],
  move2Id: [0, 826],
  move3Id: [0, 826],
  species: [1, 898]
} as const satisfies Record<
  z.infer<typeof dynamaxAdventureEditableFieldNameSchema>,
  readonly [number, number]
>;
const dynamaxAdventureIvOptionValues = [
  -1,
  ...Array.from({ length: 32 }, (_, value) => value)
];
const dynamaxAdventureExactFieldOptionValues: Partial<Record<
  z.infer<typeof dynamaxAdventureEditableFieldNameSchema>,
  readonly number[]
>> = {
  ability: [0, 1, 2],
  gigantamaxState: [0, 1, 2],
  guaranteedPerfectIvs: [0, 2, 3, 4, 5, 6],
  ivAttack: dynamaxAdventureIvOptionValues,
  ivDefense: dynamaxAdventureIvOptionValues,
  ivSpecialAttack: dynamaxAdventureIvOptionValues,
  ivSpecialDefense: dynamaxAdventureIvOptionValues,
  ivSpeed: dynamaxAdventureIvOptionValues
};

export const loadDynamaxAdventuresWorkflowRequestSchema = z
  .strictObject({ paths: projectPathsSchema })
  .superRefine(validateDynamaxAdventureRequestGame);

export const loadShopsWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadEncountersWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadRaidBattlesWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadTeraRaidsWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadRaidRewardsWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadRaidBonusRewardsWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadPlacementWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadBehaviorWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadFlagworkSaveWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadBagHookWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadCatchCapWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadHyperTrainingWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadTypeChartWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadIvScreenWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadExeFsPatchWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadRoyalCandyWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadStartingItemsWorkflowRequestSchema = z.strictObject({
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

export const projectPathRoleSchema = z.enum([
  'baseRomFs',
  'baseExeFs',
  'outputRoot',
  'saveFile',
  'scarletVioletSupportFolder',
  'pokemonLegendsZASupportFolder'
]);

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

export const changePlanOutputModeSchema = z.enum(['standalone', 'trinityModManager', 'trinityBypass']);

export const createChangePlanRequestSchema = z.strictObject({ outputMode: changePlanOutputModeSchema.optional(), paths: projectPathsSchema, session: editSessionSchema });

export const applyChangePlanRequestSchema = z.strictObject({
  changePlan: z.lazy(() => changePlanSchema),
  outputMode: changePlanOutputModeSchema.optional(),
  paths: projectPathsSchema,
  session: editSessionSchema
});

export const previewDynamaxAdventureDefaultsRequestSchema = z
  .strictObject({
    entryIndex: z.number().int().min(0).max(dynamaxAdventureNormalEncounterCount - 1),
    form: z.number().int().min(0).max(255),
    level: z.number().int().min(1).max(100),
    paths: projectPathsSchema,
    session: editSessionSchema.nullable(),
    species: z.number().int().min(1).max(898)
  })
  .superRefine(validateDynamaxAdventureRequestGame);

export const stageDynamaxAdventureRepairRequestSchema = z
  .strictObject({
    paths: projectPathsSchema,
    session: editSessionSchema.nullable()
  })
  .superRefine(validateDynamaxAdventureRequestGame)
  .superRefine((request, context) => {
    if (
      request.session?.hasPendingChanges ||
      (request.session?.pendingEdits.length ?? 0) > 0
    ) {
      addDynamaxAdventureContractIssue(
        context,
        ['session'],
        'Dynamax Adventures repair requires an empty edit session.'
      );
    }
  });

function isCanonicalDynamaxAdventureRestoreRetryEdit(edit: {
  domain: string;
  field?: string | null;
  newValue?: string | null;
  recordId?: string | null;
  sources: Array<{ layer: string; relativePath: string }>;
  summary: string;
}) {
  return (
    edit.domain === 'workflow.dynamaxAdventures' &&
    edit.recordId === 'dynamaxAdventure:0' &&
    edit.field === 'level' &&
    typeof edit.newValue === 'string' &&
    /^(?:[1-9]|[1-9]\d|100)$/.test(edit.newValue) &&
    edit.summary === 'Restore the vanilla Dynamax Adventures table.' &&
    edit.sources.length === 1 &&
    edit.sources[0]?.layer === 'layered' &&
    edit.sources[0]?.relativePath ===
      'romfs/bin/appli/chika/data_table/underground_exploration_poke.bin'
  );
}

export const stageDynamaxAdventureRestoreRequestSchema = z
  .strictObject({
    paths: projectPathsSchema,
    session: editSessionSchema.nullable()
  })
  .superRefine(validateDynamaxAdventureRequestGame)
  .superRefine((request, context) => {
    const pendingEdits = request.session?.pendingEdits ?? [];
    const retryEdit = pendingEdits[0];
    const isCanonicalRestoreRetry =
      request.session?.hasPendingChanges === true &&
      pendingEdits.length === 1 &&
      retryEdit !== undefined &&
      isCanonicalDynamaxAdventureRestoreRetryEdit(retryEdit);
    const claimsPendingSession =
      request.session?.hasPendingChanges === true || pendingEdits.length > 0;
    if (claimsPendingSession && !isCanonicalRestoreRetry) {
      addDynamaxAdventureContractIssue(
        context,
        ['session'],
        'Dynamax Adventures table restore requires an empty edit session or its exact staged restore marker.'
      );
    }
  });

export const previewSpreadsheetImportRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  profileId: z.string(),
  session: editSessionSchema.nullable(),
  sourcePath: z.string()
});

export const modMergerConflictResolutionSchema = z.strictObject({
  conflictId: z.string(),
  source: z.enum(['mod1', 'mod2'])
});

export const modMergerMergeModeSchema = z.enum(['smart', 'preferMod1', 'preferMod2']);

export const loadModMergerWorkflowRequestSchema = z.strictObject({
  modDirectory1: z.string().nullable(),
  modDirectory2: z.string().nullable(),
  paths: projectPathsSchema
});

export const stageModMergeRequestSchema = z.strictObject({
  mergeMode: modMergerMergeModeSchema.default('smart'),
  modDirectory1: z.string().nullable(),
  modDirectory2: z.string().nullable(),
  paths: projectPathsSchema,
  resolutions: z.array(modMergerConflictResolutionSchema),
  selectedDirectory1Files: z.array(z.string()),
  selectedDirectory2Files: z.array(z.string())
});

export const applyModMergeRequestSchema = z.strictObject({
  mergeMode: modMergerMergeModeSchema.default('smart'),
  modDirectory1: z.string().nullable(),
  modDirectory2: z.string().nullable(),
  paths: projectPathsSchema,
  reviewToken: z.string().min(1),
  resolutions: z.array(modMergerConflictResolutionSchema),
  selectedDirectory1Files: z.array(z.string()),
  selectedDirectory2Files: z.array(z.string())
});

export const svModMergerSourceSchema = z.strictObject({
  isEnabled: z.boolean(),
  path: z.string()
});

export const loadSvModMergerWorkflowRequestSchema = z.strictObject({
  modSources: z.array(svModMergerSourceSchema),
  paths: projectPathsSchema
});

export const stageSvModMergeRequestSchema = z.strictObject({
  modSources: z.array(svModMergerSourceSchema),
  paths: projectPathsSchema
});

export const applySvModMergeRequestSchema = z.strictObject({
  modSources: z.array(svModMergerSourceSchema),
  paths: projectPathsSchema
});

export const zaModMergerSourceSchema = z.strictObject({
  isEnabled: z.boolean(),
  path: z.string()
});

export const loadZaModMergerWorkflowRequestSchema = z.strictObject({
  modSources: z.array(zaModMergerSourceSchema),
  paths: projectPathsSchema
});

export const stageZaModMergeRequestSchema = z.strictObject({
  modSources: z.array(zaModMergerSourceSchema),
  paths: projectPathsSchema
});

export const applyZaModMergeRequestSchema = z.strictObject({
  modSources: z.array(zaModMergerSourceSchema),
  paths: projectPathsSchema
});

export const randomizerOptionsSchema = z.strictObject({
  ability1: z.boolean(),
  ability2: z.boolean(),
  allowSameType: z.boolean(),
  compatibilityMachines: z.boolean(),
  compatibilityRecords: z.boolean(),
  compatibilityTutors: z.boolean(),
  hiddenAbility: z.boolean(),
  learnsetBanFixedDamageMoves: z.boolean(),
  learnsetExpandTo25: z.boolean(),
  learnsetRequireDamagingMove: z.boolean(),
  learnsetStabFirst: z.boolean(),
  randomizeGiftEncounters: z.boolean(),
  randomizePokemonAbilities: z.boolean(),
  randomizePokemonCatchRates: z.boolean(),
  randomizePokemonCompatibility: z.boolean(),
  randomizePokemonEvolutions: z.boolean(),
  randomizePokemonHeldItems: z.boolean(),
  randomizePokemonLearnsets: z.boolean(),
  randomizePokemonStats: z.boolean(),
  randomizePokemonTypes: z.boolean(),
  randomizeWildEncounters: z.boolean(),
  randomizeRaidBonusRewards: z.boolean(),
  randomizeRaidRewards: z.boolean(),
  randomizeStaticEncounters: z.boolean(),
  randomizeTypeChart: z.boolean(),
  shufflePokemonStats: z.boolean(),
  statAttack: z.boolean(),
  statDefense: z.boolean(),
  statHp: z.boolean(),
  statSpecialAttack: z.boolean(),
  statSpecialDefense: z.boolean(),
  statSpeed: z.boolean(),
  typeChartNoImmunities: z.boolean(),
  typeChartOneImmunityPerType: z.boolean(),
  typePrimary: z.boolean(),
  typeSecondary: z.boolean()
});

export const randomizerConfigSchema = z.strictObject({
  options: randomizerOptionsSchema,
  outputHash: z.string().nullable().optional().default(null),
  rollSeed: z.string().nullable().optional().default(null),
  userSeed: z.string().max(20)
});

export const importRandomizerSeedRequestSchema = z.strictObject({
  seed: z.string()
});

export const applyRandomizerRequestSchema = z.strictObject({
  config: randomizerConfigSchema,
  paths: projectPathsSchema
});

export const restoreRandomizerRequestSchema = z.strictObject({
  paths: projectPathsSchema
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

export const itemDetailSchema = z.strictObject({
  label: z.string(),
  value: z.string()
});

export const itemDetailGroupSchema = z.strictObject({
  details: z.array(itemDetailSchema),
  label: z.string()
});

export const itemMetadataSchema = z.strictObject({
  boost0: z.number().int(),
  boost1: z.number().int(),
  boost2: z.number().int(),
  boost3: z.number().int(),
  canUseOnPokemon: z.boolean(),
  cureStatusFlags: z.number().int(),
  evAttack: z.number().int(),
  evDefense: z.number().int(),
  evHp: z.number().int(),
  evSpecialAttack: z.number().int(),
  evSpecialDefense: z.number().int(),
  evSpeed: z.number().int(),
  fieldFlags: z.number().int(),
  fieldUseType: z.number().int(),
  flingPower: z.number().int(),
  friendshipGain1: z.number().int(),
  friendshipGain2: z.number().int(),
  friendshipGain3: z.number().int(),
  groupIndex: z.number().int(),
  groupType: z.number().int(),
  healAmount: z.number().int(),
  itemSprite: z.number().int(),
  itemType: z.number().int(),
  machineMoveId: z.number().int().nullable(),
  machineMoveName: z.string().nullable(),
  machineSlot: z.number().int().nullable(),
  pouch: z.number().int(),
  pouchFlags: z.number().int(),
  ppGain: z.number().int(),
  sortIndex: z.number().int(),
  useFlags1: z.number().int(),
  useFlags2: z.number().int()
});

export const itemRecordSchema = z.strictObject({
  alternatePrice: z.number().int().nonnegative(),
  buyPrice: z.number().int().nonnegative(),
  category: z.string(),
  detailGroups: z.array(itemDetailGroupSchema),
  fieldValues: z.record(z.string(), z.number().int().nullable()).optional(),
  itemId: z.number().int().nonnegative(),
  metadata: itemMetadataSchema,
  name: z.string(),
  provenance: itemProvenanceSchema,
  sellPrice: z.number().int().nonnegative(),
  sharedItemIds: z.array(z.number().int().nonnegative()),
  wattsPrice: z.number().int().nonnegative()
});

export const itemEditableFieldOptionSchema = z.strictObject({
  label: z.string(),
  value: z.number().int()
});

export const itemEditableFieldSchema = z.strictObject({
  field: z.string(),
  isReadOnly: z.boolean().optional().default(false),
  label: z.string(),
  maximumValue: z.number().int().nullable(),
  minimumValue: z.number().int().nullable(),
  options: z.array(itemEditableFieldOptionSchema),
  readOnlyReason: z.string().nullable().optional().default(null),
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
  ability1Label: z.string(),
  ability2: z.number().int().nonnegative(),
  ability2Label: z.string(),
  hiddenAbility: z.number().int().nonnegative(),
  hiddenAbilityLabel: z.string()
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
  baseExperience: z.number().int().optional(),
  baseFriendship: z.number().int().nonnegative(),
  canNotDynamax: z.boolean().nullable().default(null),
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
  argumentKind: z.string().default('value'),
  argumentLabel: z.string().default('Argument'),
  argumentValue: z.string().default(''),
  form: z.number().int().nonnegative(),
  level: z.number().int().nonnegative(),
  method: z.number().int().nonnegative(),
  methodName: z.string().default(''),
  slot: z.number().int().nonnegative(),
  species: z.number().int().nonnegative()
});
export const pokemonLearnsetMoveSchema = z.strictObject({
  level: z.number().int().nonnegative(),
  levelLabel: z.string().nullable().optional(),
  moveId: z.number().int().nonnegative(),
  moveName: z.string(),
  rawLevel: z.number().int().nonnegative().nullable().optional(),
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

export const pokemonEvolutionMethodOptionSchema = z.strictObject({
  argumentKind: z.string(),
  argumentLabel: z.string(),
  argumentOptions: z.array(pokemonEditableFieldOptionSchema),
  label: z.string(),
  value: z.number().int()
});

export const pokemonDexPlacementSchema = z.strictObject({
  dexKind: z.enum(['regular', 'hyperspace']),
  displayedNumber: z.number().int().positive(),
  internalIndex: z.number().int().positive(),
  label: z.string(),
  speciesId: z.number().int().positive()
});

export const pokemonDexEditorSchema = z.strictObject({
  blockedReason: z.string().nullable(),
  canEdit: z.boolean(),
  hyperspaceCount: z.number().int().nonnegative(),
  placements: z.array(pokemonDexPlacementSchema),
  regularCount: z.number().int().nonnegative()
});

export const pokemonRecordSchema = z.strictObject({
  abilities: pokemonAbilitySetSchema,
  baseExperience: z.number().int(),
  baseStats: pokemonBaseStatsSchema,
  catchRate: z.number().int().nonnegative(),
  compatibility: z.array(pokemonCompatibilityGroupSchema),
  dexPresence: pokemonDexPresenceSchema,
  evolutionStage: z.number().int().nonnegative(),
  evolutions: z.array(pokemonEvolutionRecordSchema),
  form: z.number().int().nonnegative(),
  formLabel: z.string(),
  genderRatio: z.number().int().nonnegative(),
  genderRatioLabel: z.string(),
  height: z.number().int().nonnegative(),
  learnset: z.array(pokemonLearnsetMoveSchema),
  name: z.string(),
  personal: pokemonPersonalDetailsSchema,
  personalId: z.number().int().nonnegative(),
  provenance: pokemonProvenanceSchema,
  spriteName: z.string().nullable().default(null),
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
  dexEditor: pokemonDexEditorSchema.nullable().optional().default(null),
  diagnostics: z.array(apiDiagnosticSchema),
  editableFields: z.array(pokemonEditableFieldSchema),
  evolutionMethodOptions: z.array(pokemonEvolutionMethodOptionSchema).default([]),
  learnsetMoveOptions: z.array(pokemonEditableFieldOptionSchema).default([]),
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

export const swapPokemonDexPlacementRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  session: editSessionSchema.nullable(),
  sourceSpeciesId: z.number().int().positive(),
  targetSpeciesId: z.number().int().positive()
});

export const swapPokemonDexPlacementResponseSchema = z.strictObject({
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
  stat: z.number().int(),
  statName: z.string()
});

export const moveFlagRecordSchema = z.strictObject({
  enabled: z.boolean(),
  field: z.string(),
  label: z.string()
});

export const moveEditableFieldOptionSchema = z.strictObject({
  label: z.string(),
  value: z.number().int()
});

export const moveEditableFieldSchema = z.strictObject({
  field: z.string(),
  label: z.string(),
  maximumValue: z.number().int().nullable(),
  minimumValue: z.number().int().nullable(),
  options: z.array(moveEditableFieldOptionSchema).default([]),
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
  attack: z.number().int(),
  defense: z.number().int(),
  hp: z.number().int(),
  specialAttack: z.number().int(),
  specialDefense: z.number().int(),
  speed: z.number().int()
});

const trainerFormOptionSchema = z.strictObject({
  label: z.string(),
  value: z.number().int()
});

export const trainerEditableFieldOptionSchema = z.strictObject({
  ...trainerFormOptionSchema.shape,
  formOptions: z.array(trainerFormOptionSchema).nullable().optional()
});

export const trainerPokemonRecordSchema = z.strictObject({
  ability: z.number().int(),
  abilityLabel: z.string(),
  abilityOptions: z.array(z.strictObject({ label: z.string(), value: z.number().int() })).default([]),
  baseStats: trainerPokemonStatsSchema.nullable().optional(),
  canDynamax: z.boolean().nullable().default(null),
  canGigantamax: z.boolean().nullable().default(null),
  dynamaxLevel: z.number().int().nonnegative().nullable().default(null),
  evs: trainerPokemonStatsSchema,
  form: z.number().int(),
  formOptions: z.array(trainerEditableFieldOptionSchema).default([]),
  gender: z.number().int(),
  genderLabel: z.string(),
  heldItem: z.string().nullable(),
  heldItemId: z.number().int().nonnegative(),
  ivs: trainerPokemonStatsSchema,
  level: z.number().int().nonnegative(),
  moveIds: z.array(z.number().int()),
  moves: z.array(z.string()),
  nature: z.number().int(),
  natureLabel: z.string(),
  shiny: z.boolean(),
  slot: z.number().int().nonnegative(),
  spriteName: z.string().nullable().default(null),
  speciesId: z.number().int().nonnegative(),
  species: z.string(),
  teraType: z.number().int().nonnegative().nullable().default(null),
  teraTypeLabel: z.string().nullable().default(null)
});

export const trainerAiFlagStateSchema = z.strictObject({
  bit: z.number().int().nonnegative(),
  description: z.string(),
  enabled: z.boolean(),
  label: z.string(),
  mask: z.number().int().nonnegative()
});

export const trainerRecordSchema = z.strictObject({
  aiFlags: z.number().int().nonnegative().default(0),
  aiFlagStates: z.array(trainerAiFlagStateSchema).default([]),
  battleType: z.string(),
  battleTypeValue: z.number().int().nonnegative(),
  canEditClassBall: z.boolean().default(false),
  canTerastallize: z.boolean().nullable().default(null),
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
  teraTarget: z.string().nullable().default(null),
  trainerClass: z.string(),
  trainerClassId: z.number().int().nonnegative(),
  trainerId: z.number().int().nonnegative(),
  zaLastHand: z.boolean().nullable().default(null),
  zaMegaEvolution: z.boolean().nullable().default(null),
  zaRank: z.number().int().nonnegative().nullable().default(null)
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

export const giftPokemonProvenanceSchema = z.strictObject({ fileState: projectFileGraphEntryStateSchema, sourceFile: z.string(), sourceLayer: projectFileLayerSchema });

export const giftPokemonIvsSchema = z.strictObject({ attack: z.number().int(), defense: z.number().int(), hp: z.number().int(), specialAttack: z.number().int(), specialDefense: z.number().int(), speed: z.number().int() });

export const giftPokemonMoveSchema = z.strictObject({
  move: z.string().nullable(),
  moveId: z.number().int(),
  pointUps: z.number().int().nonnegative(),
  slot: z.number().int().min(0).max(3)
});

const giftPokemonFormOptionSchema = z.strictObject({
  label: z.string(),
  value: z.number().int()
});

export const giftPokemonEditableFieldOptionSchema = z.strictObject({
  ...giftPokemonFormOptionSchema.shape,
  formOptions: z.array(giftPokemonFormOptionSchema).nullable().optional()
});

export const giftPokemonEditableFieldSchema = z.strictObject({
  field: z.string(),
  label: z.string(),
  maximumValue: z.number().int().nullable(),
  minimumValue: z.number().int().nullable(),
  options: z.array(giftPokemonEditableFieldOptionSchema),
  valueKind: z.enum(['boolean', 'integer'])
});

export const giftPokemonRecordSchema = z.strictObject({
  ability: z.number().int(),
  abilityLabel: z.string(),
  abilityOptions: z.array(giftPokemonEditableFieldOptionSchema).default([]),
  ballItem: z.string(),
  ballItemId: z.number().int().nonnegative(),
  canGigantamax: z.boolean().nullable().default(null),
  dynamaxLevel: z.number().int().nonnegative().nullable().default(null),
  editorFamily: z.enum(['swsh', 'sv', 'za']).default('swsh'),
  eventLabel: z.string().nullable().default(null),
  flawlessIvCount: z.number().int().nullable(),
  form: z.number().int(),
  formOptions: z.array(giftPokemonEditableFieldOptionSchema).default([]),
  gender: z.number().int(),
  genderLabel: z.string(),
  genderOptions: z.array(giftPokemonEditableFieldOptionSchema).default([]),
  giftIndex: z.number().int().nonnegative(),
  heldItem: z.string().nullable(),
  heldItemId: z.number().int().nonnegative(),
  isEgg: z.boolean(),
  ivs: giftPokemonIvsSchema,
  ivSummary: z.string(),
  label: z.string(),
  level: z.number().int().nonnegative(),
  moves: z.array(giftPokemonMoveSchema).default([]),
  nature: z.number().int(),
  natureLabel: z.string(),
  provenance: giftPokemonProvenanceSchema,
  scaleMode: z.number().int().nullable().default(null),
  scaleModeLabel: z.string().nullable().default(null),
  scaleValue: z.number().int().nullable().default(null),
  shinyLock: z.number().int(),
  shinyLockLabel: z.string(),
  specialMove: z.string().nullable(),
  specialMoveId: z.number().int(),
  species: z.string(),
  speciesId: z.number().int().nonnegative(),
  teraType: z.number().int().nullable().default(null),
  teraTypeLabel: z.string().nullable().default(null)
});

export const giftPokemonWorkflowStatsSchema = z.strictObject({
  eggGiftCount: z.number().int().nonnegative(),
  fixedIvGiftCount: z.number().int().nonnegative(),
  sourceFileCount: z.number().int().nonnegative(),
  totalGiftCount: z.number().int().nonnegative()
});

export const giftPokemonWorkflowSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  editorFamily: z.enum(['swsh', 'sv', 'za']).default('swsh'),
  editableFields: z.array(giftPokemonEditableFieldSchema),
  gifts: z.array(giftPokemonRecordSchema),
  stats: giftPokemonWorkflowStatsSchema,
  summary: workflowSummarySchema
});

export const loadGiftPokemonWorkflowResponseSchema = z.strictObject({
  workflow: giftPokemonWorkflowSchema
});

export const tradePokemonProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema
});

export const tradePokemonIvsSchema = z.strictObject({
  attack: z.number().int(),
  defense: z.number().int(),
  hp: z.number().int(),
  specialAttack: z.number().int(),
  specialDefense: z.number().int(),
  speed: z.number().int()
});

export const tradePokemonMoveSchema = z.strictObject({
  move: z.string().nullable(),
  moveId: z.number().int(),
  slot: z.number().int().min(0).max(3)
});

const tradePokemonFormOptionSchema = z.strictObject({
  label: z.string(),
  value: z.number().int()
});

export const tradePokemonEditableFieldOptionSchema = z.strictObject({
  ...tradePokemonFormOptionSchema.shape,
  formOptions: z.array(tradePokemonFormOptionSchema).nullable().optional()
});

export const tradePokemonEditableFieldSchema = z.strictObject({
  field: z.string(),
  label: z.string(),
  maximumValue: z.number().int().nullable(),
  minimumValue: z.number().int().nullable(),
  options: z.array(tradePokemonEditableFieldOptionSchema),
  valueKind: z.enum(['boolean', 'integer'])
});

export const tradePokemonRecordSchema = z.strictObject({
  ability: z.number().int(),
  abilityLabel: z.string(),
  abilityOptions: z.array(tradePokemonEditableFieldOptionSchema).default([]),
  ballItem: z.string(),
  ballItemId: z.number().int().nonnegative(),
  canGigantamax: z.boolean().nullable().default(null),
  dynamaxLevel: z.number().int().nonnegative().nullable().default(null),
  editorFamily: z.enum(['swsh', 'sv', 'za']).default('swsh'),
  eventLabel: z.string().nullable().default(null),
  field03: z.number().int().nonnegative(),
  flawlessIvCount: z.number().int().nullable(),
  form: z.number().int(),
  formOptions: z.array(tradePokemonEditableFieldOptionSchema).default([]),
  gender: z.number().int(),
  genderLabel: z.string(),
  genderOptions: z.array(tradePokemonEditableFieldOptionSchema).default([]),
  hash0: z.string(),
  hash1: z.string(),
  hash2: z.string(),
  heldItem: z.string().nullable(),
  heldItemId: z.number().int().nonnegative(),
  ivs: tradePokemonIvsSchema,
  ivSummary: z.string(),
  label: z.string(),
  level: z.number().int().nonnegative(),
  memoryCode: z.number().int().nonnegative(),
  memoryFeel: z.number().int().nonnegative(),
  memoryIntensity: z.number().int().nonnegative(),
  memoryTextVariable: z.number().int().nonnegative(),
  moves: z.array(tradePokemonMoveSchema).default([]),
  nature: z.number().int(),
  natureLabel: z.string(),
  otGender: z.number().int().nonnegative(),
  otGenderLabel: z.string(),
  provenance: tradePokemonProvenanceSchema,
  relearnMoves: z.array(tradePokemonMoveSchema),
  requiredForm: z.number().int(),
  requiredNature: z.number().int(),
  requiredNatureLabel: z.string(),
  requiredSpecies: z.string(),
  requiredSpeciesId: z.number().int().nonnegative(),
  shinyLock: z.number().int(),
  shinyLockLabel: z.string(),
  species: z.string(),
  speciesId: z.number().int().nonnegative(),
  scaleMode: z.number().int().nullable().default(null),
  scaleModeLabel: z.string().nullable().default(null),
  scaleValue: z.number().int().nullable().default(null),
  teraType: z.number().int().nullable().default(null),
  teraTypeLabel: z.string().nullable().default(null),
  tradeIndex: z.number().int().nonnegative(),
  trainerId: z.number().int().nonnegative(),
  unknownRequirement: z.number().int().nonnegative()
});

export const tradePokemonWorkflowStatsSchema = z.strictObject({
  fixedIvTradeCount: z.number().int().nonnegative(),
  sourceFileCount: z.number().int().nonnegative(),
  totalTradeCount: z.number().int().nonnegative()
});

export const tradePokemonWorkflowSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  editableFields: z.array(tradePokemonEditableFieldSchema),
  editorFamily: z.enum(['swsh', 'sv', 'za']).default('swsh'),
  stats: tradePokemonWorkflowStatsSchema,
  summary: workflowSummarySchema,
  trades: z.array(tradePokemonRecordSchema)
});

export const loadTradePokemonWorkflowResponseSchema = z.strictObject({
  workflow: tradePokemonWorkflowSchema
});

export const staticEncounterProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema
});

export const staticEncounterStatsSchema = z.strictObject({
  attack: z.number().int(),
  defense: z.number().int(),
  hp: z.number().int(),
  specialAttack: z.number().int(),
  specialDefense: z.number().int(),
  speed: z.number().int()
});

export const staticEncounterMoveSchema = z.strictObject({
  move: z.string().nullable(),
  moveId: z.number().int(),
  slot: z.number().int().nonnegative()
});

const staticEncounterFormOptionSchema = z.strictObject({
  label: z.string(),
  value: z.number().int()
});

export const staticEncounterEditableFieldOptionSchema = z.strictObject({
  ...staticEncounterFormOptionSchema.shape,
  formOptions: z.array(staticEncounterFormOptionSchema).nullable().optional()
});

export const staticEncounterEditableFieldSchema = z.strictObject({
  description: z.string().default(''),
  field: z.string(),
  group: z.string().nullable().default(null),
  isReadOnly: z.boolean().default(false),
  label: z.string(),
  maximumValue: z.number().int().nullable(),
  minimumValue: z.number().int().nullable(),
  options: z.array(staticEncounterEditableFieldOptionSchema),
  valueKind: z.string()
});

export const staticEncounterRecordSchema = z.strictObject({
  ability: z.number().int(),
  abilityLabel: z.string(),
  abilityOptions: z.array(staticEncounterEditableFieldOptionSchema).default([]),
  canGigantamax: z.boolean().nullable().default(null),
  categoryId: z.string().nullable().default(null),
  categoryLabel: z.string().nullable().default(null),
  dynamaxLevel: z.number().int().nonnegative().nullable().default(null),
  editorFamily: z.enum(['swsh', 'sv', 'za']).default('swsh'),
  encounterId: z.string(),
  encounterIndex: z.number().int().nonnegative(),
  encounterScenario: z.number().int().nonnegative(),
  encounterScenarioLabel: z.string(),
  scenarioDetails: z.string().nullable().optional(),
  evs: staticEncounterStatsSchema,
  fieldDisplayValues: z.record(z.string(), z.string()).default({}),
  fieldReadOnly: z.record(z.string(), z.boolean()).default({}),
  fieldValues: z.record(z.string(), z.string()).default({}),
  flawlessIvCount: z.number().int().nullable(),
  form: z.number().int(),
  formOptions: z.array(staticEncounterEditableFieldOptionSchema).default([]),
  gender: z.number().int(),
  genderLabel: z.string(),
  genderOptions: z.array(staticEncounterEditableFieldOptionSchema).optional(),
  heldItem: z.string().nullable(),
  heldItemId: z.number().int().nonnegative(),
  ivs: staticEncounterStatsSchema,
  ivSummary: z.string(),
  label: z.string(),
  level: z.number().int().nonnegative(),
  moves: z.array(staticEncounterMoveSchema),
  nature: z.number().int(),
  natureLabel: z.string(),
  provenance: staticEncounterProvenanceSchema,
  shinyLock: z.number().int(),
  shinyLockLabel: z.string(),
  species: z.string(),
  speciesId: z.number().int().nonnegative(),
  supportedFields: z.array(z.string()).default([])
});

export const staticEncountersWorkflowStatsSchema = z.strictObject({
  coinSymbolCount: z.number().int().nonnegative().default(0),
  fixedIvEncounterCount: z.number().int().nonnegative(),
  fixedSymbolCount: z.number().int().nonnegative().default(0),
  gigantamaxEncounterCount: z.number().int().nonnegative().nullable().default(null),
  sourceFileCount: z.number().int().nonnegative(),
  totalEncounterCount: z.number().int().nonnegative()
});

export const staticEncountersWorkflowSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  editableFields: z.array(staticEncounterEditableFieldSchema),
  editorFamily: z.enum(['swsh', 'sv', 'za']).default('swsh'),
  encounters: z.array(staticEncounterRecordSchema),
  stats: staticEncountersWorkflowStatsSchema,
  summary: workflowSummarySchema
});

export const loadStaticEncountersWorkflowResponseSchema = z.strictObject({
  workflow: staticEncountersWorkflowSchema
});

export const rentalPokemonProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema
});

export const rentalPokemonStatsSchema = z.strictObject({
  attack: z.number().int(),
  defense: z.number().int(),
  hp: z.number().int(),
  specialAttack: z.number().int(),
  specialDefense: z.number().int(),
  speed: z.number().int()
});

export const rentalPokemonMoveSchema = z.strictObject({
  move: z.string().nullable(),
  moveId: z.number().int().nonnegative(),
  slot: z.number().int().min(0).max(3)
});

export const rentalPokemonEditableFieldOptionSchema = z.strictObject({
  label: z.string(),
  value: z.number().int()
});

export const rentalPokemonEditableFieldSchema = z.strictObject({
  field: z.string(),
  label: z.string(),
  maximumValue: z.number().int().nullable(),
  minimumValue: z.number().int().nullable(),
  options: z.array(rentalPokemonEditableFieldOptionSchema),
  valueKind: z.enum(['boolean', 'integer'])
});

export const rentalPokemonRecordSchema = z.strictObject({
  ability: z.number().int().nonnegative(),
  abilityLabel: z.string(),
  abilityOptions: z.array(rentalPokemonEditableFieldOptionSchema).default([]),
  ballItem: z.string(),
  ballItemId: z.number().int().nonnegative(),
  evs: rentalPokemonStatsSchema,
  form: z.number().int().nonnegative(),
  gender: z.number().int().nonnegative(),
  genderLabel: z.string(),
  genderOptions: z.array(rentalPokemonEditableFieldOptionSchema).default([]),
  hash1: z.string(),
  hash2: z.string(),
  hasPerfectIvs: z.boolean(),
  heldItem: z.string().nullable(),
  heldItemId: z.number().int().nonnegative(),
  ivs: rentalPokemonStatsSchema,
  ivSummary: z.string(),
  label: z.string(),
  level: z.number().int().nonnegative(),
  moves: z.array(rentalPokemonMoveSchema),
  nature: z.number().int().nonnegative(),
  natureLabel: z.string(),
  provenance: rentalPokemonProvenanceSchema,
  rentalIndex: z.number().int().nonnegative(),
  species: z.string(),
  speciesId: z.number().int().nonnegative(),
  trainerId: z.number().int().nonnegative()
});

export const rentalPokemonWorkflowStatsSchema = z.strictObject({
  perfectIvRentalCount: z.number().int().nonnegative(),
  sourceFileCount: z.number().int().nonnegative(),
  totalRentalCount: z.number().int().nonnegative()
});

export const rentalPokemonWorkflowSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  editableFields: z.array(rentalPokemonEditableFieldSchema),
  rentals: z.array(rentalPokemonRecordSchema),
  stats: rentalPokemonWorkflowStatsSchema,
  summary: workflowSummarySchema
});

export const loadRentalPokemonWorkflowResponseSchema = z.strictObject({
  workflow: rentalPokemonWorkflowSchema
});

export const dynamaxAdventureProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.literal(
    'romfs/bin/appli/chika/data_table/underground_exploration_poke.bin'
  ),
  sourceLayer: projectFileLayerSchema
});

export const dynamaxAdventureMoveSchema = z.strictObject({
  move: z.string().min(1),
  moveId: z.number().int().min(0).max(826),
  slot: z.number().int().min(1).max(4)
});

export const dynamaxAdventureIvsSchema = z.strictObject({
  attack: z.number().int().min(-1).max(31),
  defense: z.number().int().min(-1).max(31),
  hp: z.number().int().min(-6).max(31),
  specialAttack: z.number().int().min(-1).max(31),
  specialDefense: z.number().int().min(-1).max(31),
  speed: z.number().int().min(-1).max(31)
});

export const dynamaxAdventureEditableFieldOptionSchema = z.strictObject({
  label: z.string().min(1),
  value: z.number().int()
});

const dynamaxAdventureGuaranteedPerfectIvsSchema = z.union([
  z.literal(0),
  z.literal(2),
  z.literal(3),
  z.literal(4),
  z.literal(5),
  z.literal(6)
]);

export const dynamaxAdventureDefaultFieldSchema = z.strictObject({
  field: dynamaxAdventureEditableFieldNameSchema,
  value: z.string().regex(/^-?\d+$/)
});

export const dynamaxAdventureEditableFieldSchema = z.strictObject({
  field: dynamaxAdventureEditableFieldNameSchema,
  label: z.string().min(1),
  maximumValue: z.number().int().nullable(),
  minimumValue: z.number().int().nullable(),
  options: z.array(dynamaxAdventureEditableFieldOptionSchema),
  valueKind: z.literal('integer')
});

export const dynamaxAdventurePokemonSnapshotSchema = z.strictObject({
  ability: z.number().int().min(0).max(2),
  abilityLabel: z.string().min(1),
  form: z.number().int().min(0).max(255),
  gigantamaxLabel: z.string().min(1),
  gigantamaxState: z.number().int().min(0).max(2),
  guaranteedPerfectIvs: dynamaxAdventureGuaranteedPerfectIvsSchema,
  ivs: dynamaxAdventureIvsSchema,
  ivSummary: z.string().min(1),
  level: z.number().int().min(1).max(100),
  moves: z.array(dynamaxAdventureMoveSchema).length(4),
  species: z.string().min(1),
  speciesId: z.number().int().min(1).max(898)
});

export const dynamaxAdventureBossTargetOptionSchema = z.strictObject({
  adventureIndex: z.number().int().nonnegative(),
  entryIndex: z.number().int().nonnegative(),
  form: z.number().int().min(0).max(255),
  isStoryProgressGated: z.boolean(),
  label: z.string().min(1),
  species: z.string().min(1),
  speciesId: z.number().int().min(1).max(898),
  version: z.number().int().min(0).max(2),
  versionLabel: z.string().min(1)
});

export const dynamaxAdventureRecordSchema = z.strictObject({
  ability: z.number().int().min(0).max(2),
  abilityLabel: z.string().min(1),
  abilityOptions: z.array(dynamaxAdventureEditableFieldOptionSchema),
  adventureIndex: z.number().int().nonnegative(),
  ballItem: z.string().min(1),
  ballItemId: z.number().int().min(0).max(65535),
  bossTargetSpecies: z.string().min(1),
  bossTargetSpeciesId: z.number().int().min(1).max(898),
  bossTargetOptions: z.array(dynamaxAdventureBossTargetOptionSchema).length(0),
  entryIndex: z.number().int().nonnegative(),
  form: z.number().int().min(0).max(255),
  gigantamaxLabel: z.string().min(1),
  gigantamaxOptions: z.array(dynamaxAdventureEditableFieldOptionSchema),
  gigantamaxState: z.number().int().min(0).max(2),
  guaranteedPerfectIvs: dynamaxAdventureGuaranteedPerfectIvsSchema,
  isEditable: z.boolean(),
  isSingleCapture: z.boolean(),
  isStoryProgressGated: z.boolean(),
  ivs: dynamaxAdventureIvsSchema,
  ivSummary: z.string().min(1),
  label: z.string().min(1),
  layoutWritableFields: z.array(dynamaxAdventureEditableFieldNameSchema),
  level: z.number().int().min(1).max(100),
  moveOptions: z.array(dynamaxAdventureEditableFieldOptionSchema),
  moves: z.array(dynamaxAdventureMoveSchema).length(4),
  otGender: z.number().int().min(0).max(1),
  otGenderLabel: z.string().min(1),
  provenance: dynamaxAdventureProvenanceSchema,
  shinyRoll: z.number().int().min(0).max(2),
  shinyRollLabel: z.string().min(1),
  singleCaptureFlagBlock: z.string().regex(/^0x[0-9A-F]{16}$/),
  species: z.string().min(1),
  speciesId: z.number().int().min(1).max(898),
  uiMessageId: z.string().regex(/^0x[0-9A-F]{16}$/),
  vanillaPokemon: dynamaxAdventurePokemonSnapshotSchema.nullable(),
  version: z.number().int().min(0).max(2),
  versionLabel: z.string().min(1)
});

export const dynamaxAdventuresWorkflowStatsSchema = z.strictObject({
  guaranteedPerfectIvEncounterCount: z.number().int().nonnegative(),
  singleCaptureCount: z.number().int().nonnegative(),
  sourceFileCount: z.number().int().nonnegative(),
  storyGatedCount: z.number().int().nonnegative(),
  totalEncounterCount: z.number().int().nonnegative()
});

export const dynamaxAdventureInstallStatusSchema = z.enum([
  'unknown',
  'blocked',
  'available',
  'repairable',
  'modified'
]);

export const dynamaxAdventureReservedRegionSchema = z.strictObject({
  area: z.enum(['main.ro', 'main.text']),
  label: z.string().min(1),
  offset: z.string().regex(/^(?:ro|text)\+0x[0-9A-F]+\.\.0x[0-9A-F]+$/),
  rule: z.enum(['do-not-overwrite', 'payload-preserve-stride-padding'])
});

const dynamaxAdventuresWorkflowShapeSchema = z.strictObject({
  buildId: z.union([z.literal('unknown'), z.string().regex(/^[A-F0-9]{64}$/)]),
  canRestoreVanillaTable: z.boolean(),
  detectedGame: z.enum(['sword', 'shield']).nullable(),
  diagnostics: z.array(apiDiagnosticSchema),
  editableFields: z.array(dynamaxAdventureEditableFieldSchema),
  encounters: z.array(dynamaxAdventureRecordSchema),
  hasLegacyBossTargetPatch: z.boolean(),
  installMessage: z.string().min(1),
  installStatus: dynamaxAdventureInstallStatusSchema,
  reservedRegions: z.array(dynamaxAdventureReservedRegionSchema),
  restoreVanillaTableMessage: z.string().min(1),
  safeNormalSpeciesOptions: z.array(dynamaxAdventureEditableFieldOptionSchema),
  stats: dynamaxAdventuresWorkflowStatsSchema,
  summary: workflowSummarySchema,
  usesVanillaRecoveryProjection: z.boolean()
});

export const dynamaxAdventuresWorkflowSchema =
  dynamaxAdventuresWorkflowShapeSchema.superRefine(validateDynamaxAdventuresWorkflow);

export const loadDynamaxAdventuresWorkflowResponseSchema = z.strictObject({
  workflow: dynamaxAdventuresWorkflowSchema
});

const previewDynamaxAdventureDefaultsResponseShapeSchema = z.strictObject({
  abilityOptions: z.array(dynamaxAdventureEditableFieldOptionSchema),
  changes: z.array(dynamaxAdventureDefaultFieldSchema),
  diagnostics: z.array(apiDiagnosticSchema),
  gigantamaxOptions: z.array(dynamaxAdventureEditableFieldOptionSchema),
  moveOptions: z.array(dynamaxAdventureEditableFieldOptionSchema)
});

export const previewDynamaxAdventureDefaultsResponseSchema =
  previewDynamaxAdventureDefaultsResponseShapeSchema.superRefine(
    validateDynamaxAdventureDefaultsPreview
  );

function validateDynamaxAdventureDefaultsPreview(
  preview: z.infer<typeof previewDynamaxAdventureDefaultsResponseShapeSchema>,
  context: z.RefinementCtx
) {
  if (preview.diagnostics.some((diagnostic) => diagnostic.severity === 'error')) {
    return;
  }

  const requiredFields = [
    'form',
    'ability',
    'gigantamaxState',
    'move0Id',
    'move1Id',
    'move2Id',
    'move3Id'
  ] as const;
  const changesByField = new Map(preview.changes.map((change) => [change.field, change.value]));
  if (
    preview.changes.length !== requiredFields.length ||
    changesByField.size !== requiredFields.length ||
    requiredFields.some((field) => !changesByField.has(field))
  ) {
    addDynamaxAdventureContractIssue(
      context,
      ['changes'],
      'Successful Dynamax Adventures defaults must return the exact dependent field set.'
    );
    return;
  }

  validateDynamaxAdventureOptions(preview.abilityOptions, context, ['abilityOptions'], 0, 2);
  validateDynamaxAdventureOptions(
    preview.gigantamaxOptions,
    context,
    ['gigantamaxOptions'],
    0,
    2
  );
  validateDynamaxAdventureOptions(preview.moveOptions, context, ['moveOptions'], 0, 826);
  if (
    preview.abilityOptions.length === 0 ||
    preview.gigantamaxOptions.length === 0 ||
    preview.moveOptions.length === 0
  ) {
    addDynamaxAdventureContractIssue(
      context,
      ['abilityOptions'],
      'Successful Dynamax Adventures defaults require exact dependency options.'
    );
  }

  const form = Number(changesByField.get('form'));
  const ability = Number(changesByField.get('ability'));
  const gigantamaxState = Number(changesByField.get('gigantamaxState'));
  if (!Number.isInteger(form) || form < 0 || form > 255) {
    addDynamaxAdventureContractIssue(
      context,
      ['changes'],
      'Dynamax Adventures default form is outside the supported range.'
    );
  }
  if (!preview.abilityOptions.some((option) => option.value === ability)) {
    addDynamaxAdventureContractIssue(
      context,
      ['changes'],
      'Dynamax Adventures default ability is not in the returned options.'
    );
  }
  if (!preview.gigantamaxOptions.some((option) => option.value === gigantamaxState)) {
    addDynamaxAdventureContractIssue(
      context,
      ['changes'],
      'Dynamax Adventures default Gigantamax state is not in the returned options.'
    );
  }
  for (const field of requiredFields.slice(3)) {
    const move = Number(changesByField.get(field));
    if (!preview.moveOptions.some((option) => option.value === move)) {
      addDynamaxAdventureContractIssue(
        context,
        ['changes'],
        'Dynamax Adventures default move is not in the returned options.'
      );
    }
  }
}

function validateDynamaxAdventureRequestGame(
  request: { paths: z.infer<typeof projectPathsSchema> },
  context: z.RefinementCtx
) {
  if (request.paths.selectedGame !== 'sword' && request.paths.selectedGame !== 'shield') {
    addDynamaxAdventureContractIssue(
      context,
      ['paths', 'selectedGame'],
      'Dynamax Adventures requires Sword or Shield.'
    );
  }
}

function validateDynamaxAdventuresWorkflow(
  workflow: z.infer<typeof dynamaxAdventuresWorkflowShapeSchema>,
  context: z.RefinementCtx
) {
  const buildIds = {
    shield: 'A16802625E7826BF83B6F9708E475B912A9AB7DF000000000000000000000000',
    sword: 'A3B75BCD3311385AEED67FBEEB79CBB7BF02F471000000000000000000000000'
  } as const;
  if (
    workflow.summary.id !== 'dynamaxAdventures' ||
    workflow.summary.label !== 'Dynamax Adventures'
  ) {
    addDynamaxAdventureContractIssue(
      context,
      ['summary'],
      'Dynamax Adventures workflow identity is not canonical.'
    );
  }

  if (workflow.canRestoreVanillaTable && !workflow.usesVanillaRecoveryProjection) {
    addDynamaxAdventureContractIssue(
      context,
      ['canRestoreVanillaTable'],
      'Dynamax Adventures vanilla table restore requires the verified vanilla recovery projection.'
    );
  }

  if (workflow.canRestoreVanillaTable && workflow.summary.availability !== 'readOnly') {
    addDynamaxAdventureContractIssue(
      context,
      ['canRestoreVanillaTable'],
      'Dynamax Adventures vanilla table restore is only available in read-only recovery mode.'
    );
  }

  if (workflow.usesVanillaRecoveryProjection) {
    if (workflow.summary.availability !== 'readOnly') {
      addDynamaxAdventureContractIssue(
        context,
        ['usesVanillaRecoveryProjection'],
        'Dynamax Adventures recovery projection must remain read-only.'
      );
    }
    if (workflow.encounters.length !== dynamaxAdventureEncounterCount) {
      addDynamaxAdventureContractIssue(
        context,
        ['encounters'],
        'Dynamax Adventures recovery projection requires the canonical 273 verified vanilla rows.'
      );
    }
    workflow.encounters.forEach((encounter, encounterIndex) => {
      if (
        encounter.isEditable ||
        encounter.layoutWritableFields.length > 0 ||
        encounter.provenance.fileState !== 'layeredOverride' ||
        encounter.provenance.sourceLayer !== 'layered' ||
        encounter.vanillaPokemon === null ||
        (encounter.vanillaPokemon !== null &&
          !doesDynamaxAdventureRecordMatchSnapshot(
            encounter,
            encounter.vanillaPokemon
          ))
      ) {
        addDynamaxAdventureContractIssue(
          context,
          ['encounters', encounterIndex],
          'Dynamax Adventures recovery projection rows must be read-only verified vanilla targets.'
        );
      }
    });
  }

  if (workflow.canRestoreVanillaTable) {
    const workflowDiagnostics = [
      ...workflow.diagnostics,
      ...workflow.summary.diagnostics
    ];
    if (workflow.encounters.length !== dynamaxAdventureEncounterCount) {
      addDynamaxAdventureContractIssue(
        context,
        ['encounters'],
        'Dynamax Adventures vanilla table restore requires the canonical 273-row table.'
      );
    }
    if (
      workflow.encounters.some(
        (encounter) =>
          encounter.provenance.fileState !== 'layeredOverride' ||
          encounter.provenance.sourceLayer !== 'layered'
      )
    ) {
      addDynamaxAdventureContractIssue(
        context,
        ['encounters'],
        'Dynamax Adventures vanilla table restore requires a layered table source.'
      );
    }
    const hasRecoverableTableDiagnostic = workflowDiagnostics.some(
      (diagnostic) =>
        diagnostic.severity === 'error' &&
        (diagnostic.message.includes(
          'source table byte layout differs from the vanilla table'
        ) ||
          diagnostic.message.startsWith('Dynamax Adventures row ') ||
          diagnostic.message.includes(
            'contains changes on a hidden normal or boss row'
          ))
    );
    if (!hasRecoverableTableDiagnostic) {
      addDynamaxAdventureContractIssue(
        context,
        ['diagnostics'],
        'Dynamax Adventures vanilla table restore requires a recoverable layered-table diagnostic.'
      );
    }
    if (!['available', 'repairable', 'modified'].includes(workflow.installStatus)) {
      addDynamaxAdventureContractIssue(
        context,
        ['installStatus'],
        'Dynamax Adventures vanilla table restore requires a verified non-conflicting executable.'
      );
    }
  }

  if (
    workflow.detectedGame !== null &&
    workflow.buildId !== buildIds[workflow.detectedGame]
  ) {
    addDynamaxAdventureContractIssue(
      context,
      ['buildId'],
      'Dynamax Adventures build identity does not match the detected game.'
    );
  }

  if (
    workflow.detectedGame === null &&
    workflow.reservedRegions.length > 0
  ) {
    addDynamaxAdventureContractIssue(
      context,
      ['reservedRegions'],
      'Dynamax Adventures cannot claim owned regions without a detected game.'
    );
  }

  const reservedRegionIdentities = workflow.reservedRegions.map(
    (region) => `${region.area}:${region.offset}`
  );
  if (new Set(reservedRegionIdentities).size !== reservedRegionIdentities.length) {
    addDynamaxAdventureContractIssue(
      context,
      ['reservedRegions'],
      'Dynamax Adventures reserved regions must be unique.'
    );
  }

  if (
    workflow.installStatus === 'unknown' &&
    (workflow.buildId !== 'unknown' ||
      workflow.detectedGame !== null ||
      workflow.reservedRegions.length > 0)
  ) {
    addDynamaxAdventureContractIssue(
      context,
      ['installStatus'],
      'Unknown Dynamax Adventures executable state cannot claim inspected identity.'
    );
  }

  if (
    ['available', 'repairable', 'modified'].includes(workflow.installStatus) &&
    (workflow.buildId === 'unknown' ||
      workflow.detectedGame === null ||
      workflow.reservedRegions.length === 0)
  ) {
    addDynamaxAdventureContractIssue(
      context,
      ['installStatus'],
      'Writable Dynamax Adventures executable state requires exact identity and ownership.'
    );
  }

  if (
    workflow.hasLegacyBossTargetPatch &&
    workflow.installStatus !== 'repairable'
  ) {
    addDynamaxAdventureContractIssue(
      context,
      ['hasLegacyBossTargetPatch'],
      'A Dynamax Adventures legacy final-boss target remap must expose a repairable executable state.'
    );
  }

  const requiredFieldNames = new Set([
    'species',
    'form',
    'level',
    'ability',
    'gigantamaxState',
    'move0Id',
    'move1Id',
    'move2Id',
    'move3Id',
    'guaranteedPerfectIvs',
    'ivAttack',
    'ivDefense',
    'ivSpecialAttack',
    'ivSpecialDefense',
    'ivSpeed'
  ]);
  const fieldNames = workflow.editableFields.map((field) => field.field);
  const actualFieldNames = new Set<string>(fieldNames);
  if (
    actualFieldNames.size !== fieldNames.length ||
    actualFieldNames.size !== requiredFieldNames.size ||
    [...requiredFieldNames].some((field) => !actualFieldNames.has(field))
  ) {
    addDynamaxAdventureContractIssue(
      context,
      ['editableFields'],
      'Dynamax Adventures editable fields are not the exact safe field set.'
    );
  }

  workflow.editableFields.forEach((field, fieldIndex) => {
    const [minimumValue, maximumValue] = dynamaxAdventureFieldRanges[field.field];
    if (
      field.minimumValue !== minimumValue ||
      field.maximumValue !== maximumValue
    ) {
      addDynamaxAdventureContractIssue(
        context,
        ['editableFields', fieldIndex],
        'Dynamax Adventures editable field range is not canonical.'
      );
    }
    validateDynamaxAdventureOptions(
      field.options,
      context,
      ['editableFields', fieldIndex, 'options'],
      minimumValue,
      maximumValue
    );
    const expectedOptionValues = dynamaxAdventureExactFieldOptionValues[field.field];
    if (
      expectedOptionValues !== undefined &&
      (field.options.length !== expectedOptionValues.length ||
        field.options.some(
          (option, optionIndex) => option.value !== expectedOptionValues[optionIndex]
        ))
    ) {
      addDynamaxAdventureContractIssue(
        context,
        ['editableFields', fieldIndex, 'options'],
        'Dynamax Adventures editable field options are not canonical.'
      );
    }
  });

  const entryIndexes = workflow.encounters.map((encounter) => encounter.entryIndex);
  if (
    (workflow.encounters.length > 0 || workflow.summary.availability === 'available') &&
    workflow.encounters.length !== dynamaxAdventureEncounterCount
  ) {
    addDynamaxAdventureContractIssue(
      context,
      ['encounters'],
      'Dynamax Adventures must expose the canonical 273-row table.'
    );
  }

  if (new Set(entryIndexes).size !== entryIndexes.length) {
    addDynamaxAdventureContractIssue(
      context,
      ['encounters'],
      'Dynamax Adventures entry indexes must be unique.'
    );
  }

  if (entryIndexes.some((entryIndex, index) => entryIndex !== index)) {
    addDynamaxAdventureContractIssue(
      context,
      ['encounters'],
      'Dynamax Adventures entry indexes must match stable table order.'
    );
  }

  workflow.encounters.forEach((encounter, encounterIndex) => {
    if (
      new Set(encounter.layoutWritableFields).size !==
      encounter.layoutWritableFields.length
    ) {
      addDynamaxAdventureContractIssue(
        context,
        ['encounters', encounterIndex, 'layoutWritableFields'],
        'Dynamax Adventures layout-writable fields must be unique.'
      );
    }
    if (
      encounter.bossTargetSpeciesId !== encounter.speciesId ||
      encounter.bossTargetSpecies !== encounter.species
    ) {
      addDynamaxAdventureContractIssue(
        context,
        ['encounters', encounterIndex, 'bossTargetSpeciesId'],
        'Dynamax Adventures boss metadata must remain informational row identity.'
      );
    }
    if (
      encounterIndex >= dynamaxAdventureNormalEncounterCount &&
      encounter.isEditable
    ) {
      addDynamaxAdventureContractIssue(
        context,
        ['encounters', encounterIndex, 'isEditable'],
        'Dynamax Adventures boss rows 226 through 272 must remain read-only.'
      );
    }
    if (!encounter.isEditable && encounter.layoutWritableFields.length > 0) {
      addDynamaxAdventureContractIssue(
        context,
        ['encounters', encounterIndex, 'layoutWritableFields'],
        'Read-only Dynamax Adventures rows cannot claim writable layout fields.'
      );
    }
    validateDynamaxAdventureMoveSlots(
      encounter.moves,
      context,
      ['encounters', encounterIndex, 'moves']
    );
    validateDynamaxAdventureOptions(
      encounter.abilityOptions,
      context,
      ['encounters', encounterIndex, 'abilityOptions'],
      0,
      2
    );
    validateDynamaxAdventureOptions(
      encounter.gigantamaxOptions,
      context,
      ['encounters', encounterIndex, 'gigantamaxOptions'],
      0,
      2
    );
    validateDynamaxAdventureOptions(
      encounter.moveOptions,
      context,
      ['encounters', encounterIndex, 'moveOptions'],
      0,
      826
    );
    const expectedSourceLayer =
      encounter.provenance.fileState === 'baseOnly' ? 'base' : 'layered';
    if (encounter.provenance.sourceLayer !== expectedSourceLayer) {
      addDynamaxAdventureContractIssue(
        context,
        ['encounters', encounterIndex, 'provenance'],
        'Dynamax Adventures provenance layer does not match its file state.'
      );
    }

    const writableOptionValues: Array<{
      field: z.infer<typeof dynamaxAdventureEditableFieldNameSchema>;
      options: Array<{ value: number }>;
      path: string;
      value: number;
    }> = [
      {
        field: 'ability',
        options: encounter.abilityOptions,
        path: 'abilityOptions',
        value: encounter.ability
      },
      {
        field: 'gigantamaxState',
        options: encounter.gigantamaxOptions,
        path: 'gigantamaxOptions',
        value: encounter.gigantamaxState
      },
      ...encounter.moves.map((move, moveIndex) => ({
        field: `move${moveIndex}Id` as z.infer<
          typeof dynamaxAdventureEditableFieldNameSchema
        >,
        options: encounter.moveOptions,
        path: 'moveOptions',
        value: move.moveId
      }))
    ];
    for (const optionValue of writableOptionValues) {
      if (
        workflow.summary.availability === 'available' &&
        encounter.layoutWritableFields.includes(optionValue.field) &&
        !optionValue.options.some((option) => option.value === optionValue.value)
      ) {
        addDynamaxAdventureContractIssue(
          context,
          ['encounters', encounterIndex, optionValue.path],
          'A layout-writable Dynamax Adventures value is missing from its verified options.'
        );
      }
    }
    validateDynamaxAdventureGuaranteedIvs(
      encounter.guaranteedPerfectIvs,
      encounter.ivs.hp,
      context,
      ['encounters', encounterIndex, 'ivs', 'hp']
    );

    if (encounter.vanillaPokemon) {
      validateDynamaxAdventureMoveSlots(
        encounter.vanillaPokemon.moves,
        context,
        ['encounters', encounterIndex, 'vanillaPokemon', 'moves']
      );
      validateDynamaxAdventureGuaranteedIvs(
        encounter.vanillaPokemon.guaranteedPerfectIvs,
        encounter.vanillaPokemon.ivs.hp,
        context,
        ['encounters', encounterIndex, 'vanillaPokemon', 'ivs', 'hp']
      );
    }
  });

  validateDynamaxAdventureOptions(
    workflow.safeNormalSpeciesOptions,
    context,
    ['safeNormalSpeciesOptions'],
    1,
    898
  );

  if (workflow.summary.availability === 'available') {
    if (
      workflow.diagnostics.some((diagnostic) => diagnostic.severity === 'error') ||
      workflow.summary.diagnostics.some((diagnostic) => diagnostic.severity === 'error')
    ) {
      addDynamaxAdventureContractIssue(
        context,
        ['diagnostics'],
        'Available Dynamax Adventures cannot contain error diagnostics.'
      );
    }

    if (workflow.safeNormalSpeciesOptions.length === 0) {
      addDynamaxAdventureContractIssue(
        context,
        ['safeNormalSpeciesOptions'],
        'Available Dynamax Adventures requires safe species dependency data.'
      );
    }

    if (
      workflow.installStatus === 'unknown' ||
      workflow.installStatus === 'blocked'
    ) {
      addDynamaxAdventureContractIssue(
        context,
        ['installStatus'],
        'Available Dynamax Adventures requires a writable executable state.'
      );
    }

    workflow.encounters.forEach((encounter, encounterIndex) => {
      if (encounter.vanillaPokemon === null) {
        addDynamaxAdventureContractIssue(
          context,
          ['encounters', encounterIndex, 'vanillaPokemon'],
          'Available Dynamax Adventures rows require a verified vanilla snapshot.'
        );
      }
      if (
        encounter.isEditable &&
        (encounter.vanillaPokemon === null ||
          encounter.abilityOptions.length === 0 ||
          encounter.gigantamaxOptions.length === 0 ||
          encounter.moveOptions.length === 0)
      ) {
        addDynamaxAdventureContractIssue(
          context,
          ['encounters', encounterIndex],
          'Editable Dynamax Adventures rows require restore and exact option metadata.'
        );
      }
      if (
        !encounter.isEditable &&
        encounter.vanillaPokemon !== null &&
        !doesDynamaxAdventureRecordMatchSnapshot(encounter, encounter.vanillaPokemon)
      ) {
        addDynamaxAdventureContractIssue(
          context,
          ['encounters', encounterIndex, 'vanillaPokemon'],
          'Available read-only Dynamax Adventures rows must match verified vanilla Pokemon data.'
        );
      }
    });
  }

  const expectedStats = {
    guaranteedPerfectIvEncounterCount: workflow.encounters.filter(
      (encounter) => encounter.guaranteedPerfectIvs > 0
    ).length,
    singleCaptureCount: workflow.encounters.filter(
      (encounter) => encounter.isSingleCapture
    ).length,
    storyGatedCount: workflow.encounters.filter(
      (encounter) => encounter.isStoryProgressGated
    ).length,
    totalEncounterCount: workflow.encounters.length
  };
  for (const [field, value] of Object.entries(expectedStats)) {
    if (workflow.stats[field as keyof typeof expectedStats] !== value) {
      addDynamaxAdventureContractIssue(
        context,
        ['stats', field],
        'Dynamax Adventures statistics do not match the returned records.'
      );
    }
  }

  if (workflow.encounters.length > 0 && workflow.stats.sourceFileCount < 1) {
    addDynamaxAdventureContractIssue(
      context,
      ['stats', 'sourceFileCount'],
      'Dynamax Adventures records require at least one reported source file.'
    );
  }
}

function validateDynamaxAdventureMoveSlots(
  moves: Array<{ slot: number }>,
  context: z.RefinementCtx,
  path: Array<string | number>
) {
  if (moves.some((move, index) => move.slot !== index + 1)) {
    addDynamaxAdventureContractIssue(
      context,
      path,
      'Dynamax Adventures moves must be ordered in exact slots 1 through 4.'
    );
  }
}

function doesDynamaxAdventureRecordMatchSnapshot(
  encounter: z.infer<typeof dynamaxAdventureRecordSchema>,
  snapshot: z.infer<typeof dynamaxAdventurePokemonSnapshotSchema>
) {
  return (
    encounter.ability === snapshot.ability &&
    encounter.abilityLabel === snapshot.abilityLabel &&
    encounter.form === snapshot.form &&
    encounter.gigantamaxLabel === snapshot.gigantamaxLabel &&
    encounter.gigantamaxState === snapshot.gigantamaxState &&
    encounter.guaranteedPerfectIvs === snapshot.guaranteedPerfectIvs &&
    encounter.ivSummary === snapshot.ivSummary &&
    encounter.level === snapshot.level &&
    encounter.species === snapshot.species &&
    encounter.speciesId === snapshot.speciesId &&
    JSON.stringify(encounter.ivs) === JSON.stringify(snapshot.ivs) &&
    JSON.stringify(encounter.moves) === JSON.stringify(snapshot.moves)
  );
}

function validateDynamaxAdventureOptions(
  options: Array<{ value: number }>,
  context: z.RefinementCtx,
  path: Array<string | number>,
  minimum: number,
  maximum: number
) {
  const values = options.map((option) => option.value);
  if (
    new Set(values).size !== values.length ||
    values.some((value) => value < minimum || value > maximum)
  ) {
    addDynamaxAdventureContractIssue(
      context,
      path,
      'Dynamax Adventures options must be unique and within the field range.'
    );
  }
}

function validateDynamaxAdventureGuaranteedIvs(
  guaranteedPerfectIvs: number,
  hp: number,
  context: z.RefinementCtx,
  path: Array<string | number>
) {
  const hasMatchingGuaranteedEncoding =
    guaranteedPerfectIvs > 0 ? hp === -guaranteedPerfectIvs : hp >= -1;
  if (!hasMatchingGuaranteedEncoding) {
    addDynamaxAdventureContractIssue(
      context,
      path,
      'Dynamax Adventures HP IV metadata does not match the guaranteed IV count.'
    );
  }
}

function addDynamaxAdventureContractIssue(
  context: z.RefinementCtx,
  path: Array<string | number>,
  message: string
) {
  context.addIssue({ code: 'custom', message, path });
}

export const shopProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema
});

export const shopInventoryRecordSchema = z.strictObject({
  canEditPrice: z.boolean().default(true),
  fieldDisplayValues: z.record(z.string(), z.string()).default({}),
  fieldValues: z.record(z.string(), z.string()).default({}),
  isKnownItem: z.boolean().default(false),
  itemId: z.number().int().nonnegative(),
  itemName: z.string(),
  price: z.number().int().nonnegative(),
  priceField: z.string().nullable().default(null),
  rowId: z.string().nullable().default(null),
  slot: z.number().int().positive(),
  stockLimit: z.number().int().nonnegative().nullable(),
  supportedFields: z.array(z.string()).default([])
});

export const shopEditableFieldOptionSchema = z.strictObject({
  itemName: z.string(),
  label: z.string(),
  price: z.number().int().nonnegative(),
  prices: z.record(z.string(), z.number().int().nonnegative()).default({}),
  value: z.number().int()
});

export const shopEditableFieldSchema = z.strictObject({
  field: z.string(),
  label: z.string(),
  maximumValue: z.number().int().nullable(),
  minimumValue: z.number().int().nullable(),
  options: z.array(shopEditableFieldOptionSchema).default([]),
  valueKind: z.enum(['integer', 'text'])
});

export const shopRecordSchema = z.strictObject({
  canEditInventoryOrder: z.boolean().default(true),
  currency: z.string(),
  editorFamily: z.enum(['swsh', 'sv', 'za']).default('swsh'),
  globalPriceField: z.string().nullable().default(null),
  inventory: z.array(shopInventoryRecordSchema),
  inventoryCount: z.number().int().positive().default(1),
  inventoryIndex: z.number().int().positive().default(1),
  inventoryLabel: z.string().default('Inventory'),
  inventorySummary: z.string().default(''),
  kind: z.string().default('Unknown'),
  location: z.string(),
  name: z.string(),
  provenance: shopProvenanceSchema,
  shopId: z.string(),
  sourceHash: z.string().default('')
});

export const shopsWorkflowStatsSchema = z.strictObject({
  sourceFileCount: z.number().int().nonnegative(),
  totalInventoryItemCount: z.number().int().nonnegative(),
  totalShopCount: z.number().int().nonnegative()
});

export const shopsWorkflowSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  editableFields: z.array(shopEditableFieldSchema),
  editorFamily: z.enum(['swsh', 'sv', 'za']).default('swsh'),
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

const encounterFormOptionSchema = z.strictObject({
  label: z.string(),
  value: z.number().int()
});

export const encounterEditableFieldOptionSchema = z.strictObject({
  ...encounterFormOptionSchema.shape,
  formOptions: z.array(encounterFormOptionSchema).nullable().optional()
});

export const encounterSlotRecordSchema = z.strictObject({
  alphaChancePercent: z.number().int().min(0).max(100).nullable().optional(),
  alphaLevelBonus: z.number().int().min(0).max(100).nullable().optional(),
  appearanceMaxCount: z.number().int().nonnegative().nullable().optional(),
  appearanceMinCount: z.number().int().nonnegative().nullable().optional(),
  appearanceObjectCount: z.number().int().nonnegative().nullable().optional(),
  canEditAppearanceCounts: z.boolean().nullable().optional(),
  canEditSlotMaxCount: z.boolean().nullable().optional(),
  canEditWeight: z.boolean().nullable().optional(),
  contributesToWildZoneCompletion: z.boolean().nullable().optional(),
  encounterDataId: z.string().nullable().optional(),
  encounterKind: z.string().nullable().optional(),
  encounterRecordId: z.string().nullable().optional(),
  form: z.number().int(),
  formOptions: z.array(encounterEditableFieldOptionSchema).default([]),
  isAlpha: z.boolean().optional(),
  levelMax: z.number().int().nonnegative(),
  levelMin: z.number().int().nonnegative(),
  slot: z.number().int().nonnegative(),
  slotMaxCount: z.number().int().nonnegative().nullable().optional(),
  speciesId: z.number().int().nonnegative(),
  species: z.string(),
  timeOfDay: z.string().nullable(),
  weather: z.string(),
  weight: z.number().int().nonnegative()
});

const bossBattleContextSchema = z.strictObject({
  key: z.string(),
  label: z.string(),
  rank: z.number().int()
});

export const encounterTableRecordSchema = z.strictObject({
  archiveMember: z.string(),
  area: z.string(),
  bossBattleContextKey: z.string().nullable().optional(),
  bossBattleContextLabel: z.string().nullable().optional(),
  bossBattleContextRank: z.number().int().nullable().optional(),
  bossBattleContexts: z.array(bossBattleContextSchema).nullable().optional(),
  bossBattleWaveLabel: z.string().nullable().optional(),
  bossBattleWaveRank: z.number().int().nullable().optional(),
  encounterType: z.string(),
  gameVersion: z.string(),
  isPostgame: z.boolean().nullable().optional(),
  location: z.string(),
  locationDetails: z.string().nullable().optional(),
  locationKey: z.string().nullable().optional(),
  locationSort: z.number().int().nullable().optional(),
  provenance: encounterProvenanceSchema,
  rawSpawnerId: z.string().nullable().optional(),
  slots: z.array(encounterSlotRecordSchema),
  spawnerCategory: z
    .enum(['spawnGroup', 'spawnPoint', 'specialEncounter', 'alpha', 'other'])
    .nullable()
    .optional(),
  tableDetails: z.string().nullable().optional(),
  tableLabel: z.string().nullable().optional(),
  tableId: z.string()
});

export const encounterEditableFieldSchema = z.strictObject({
  field: z.string(),
  label: z.string(),
  maximumValue: z.number().int().nullable(),
  minimumValue: z.number().int().nullable(),
  options: z.array(encounterEditableFieldOptionSchema).optional(),
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

export const raidBattleProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema
});

export const raidBattleEditableFieldOptionSchema = z.strictObject({
  label: z.string(),
  value: z.number().int()
});

export const raidBattleRewardLinkSchema = z.strictObject({
  isMatched: z.boolean(),
  preview: z.string(),
  rewardItemCount: z.number().int().nonnegative(),
  rewardKind: z.enum(['drop', 'bonus']),
  rewardKindLabel: z.string(),
  sourceTableHash: z.string(),
  tableId: z.string()
});

export const raidBattleSlotRecordSchema = z.strictObject({
  ability: z.number().int().nonnegative(),
  abilityLabel: z.string(),
  abilityOptions: z.array(raidBattleEditableFieldOptionSchema).default([]),
  bonusTableHash: z.string(),
  bonusRewardLink: raidBattleRewardLinkSchema,
  dropTableHash: z.string(),
  dropRewardLink: raidBattleRewardLinkSchema,
  entryIndex: z.number().int().nonnegative(),
  flawlessIvs: z.number().int().nonnegative(),
  form: z.number().int().nonnegative(),
  formOptions: z.array(raidBattleEditableFieldOptionSchema).default([]),
  gender: z.number().int().nonnegative(),
  genderLabel: z.string(),
  isGigantamax: z.boolean(),
  levelTableHash: z.string(),
  probabilities: z.array(z.number().int().nonnegative()).min(5),
  probabilitySummary: z.string(),
  slot: z.number().int().positive(),
  species: z.string(),
  speciesId: z.number().int().nonnegative()
});

export const raidBattleTableRecordSchema = z.strictObject({
  denId: z.string(),
  displayName: z.string(),
  gameVersion: z.string(),
  provenance: raidBattleProvenanceSchema,
  slots: z.array(raidBattleSlotRecordSchema),
  sourceTableHash: z.string(),
  tableIndex: z.number().int().nonnegative(),
  tableId: z.string()
});

export const raidBattleEditableFieldSchema = z.strictObject({
  field: z.string(),
  label: z.string(),
  maximumValue: z.number().int().nullable(),
  minimumValue: z.number().int().nullable(),
  options: z.array(raidBattleEditableFieldOptionSchema),
  valueKind: z.string()
});

export const raidBattlesWorkflowStatsSchema = z.strictObject({
  gigantamaxSlotCount: z.number().int().nonnegative(),
  sourceFileCount: z.number().int().nonnegative(),
  totalSlotCount: z.number().int().nonnegative(),
  totalTableCount: z.number().int().nonnegative()
});

export const raidBattlesWorkflowSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  editableFields: z.array(raidBattleEditableFieldSchema),
  stats: raidBattlesWorkflowStatsSchema,
  summary: workflowSummarySchema,
  tables: z.array(raidBattleTableRecordSchema)
});

export const loadRaidBattlesWorkflowResponseSchema = z.strictObject({
  workflow: raidBattlesWorkflowSchema
});

export const teraRaidProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema
});

export const teraRaidEditableFieldOptionSchema = z.strictObject({
  label: z.string(),
  value: z.number().int()
});

export const teraRaidEditableFieldSchema = z.strictObject({
  field: z.string(),
  label: z.string(),
  maximumValue: z.number().int().nullable(),
  minimumValue: z.number().int().nullable(),
  options: z.array(teraRaidEditableFieldOptionSchema),
  valueKind: z.string()
});

export const teraRaidMoveSchema = z.strictObject({
  move: z.string().nullable(),
  moveId: z.number().int().nonnegative(),
  pointUps: z.number().int().nonnegative(),
  slot: z.number().int().nonnegative()
});

export const teraRaidIvsSchema = z.strictObject({
  attack: z.number().int(),
  defense: z.number().int(),
  hp: z.number().int(),
  specialAttack: z.number().int(),
  specialDefense: z.number().int(),
  speed: z.number().int()
});

export const teraRaidRewardItemSchema = z.strictObject({
  category: z.number().int(),
  categoryLabel: z.string(),
  count: z.number().int(),
  itemId: z.number().int(),
  itemName: z.string(),
  provenance: teraRaidProvenanceSchema,
  rareItemFlag: z.boolean().nullable(),
  rate: z.number().int().nullable(),
  recordId: z.string(),
  rewardKind: z.string(),
  rewardKindLabel: z.string(),
  slot: z.number().int().nonnegative(),
  subjectType: z.number().int().nullable(),
  subjectTypeLabel: z.string().nullable(),
  tableHash: z.string(),
  tableIndex: z.number().int().nonnegative()
});

export const teraRaidRewardTableSchema = z.strictObject({
  preview: z.string(),
  provenance: teraRaidProvenanceSchema,
  recordId: z.string(),
  rewardItemCount: z.number().int().nonnegative(),
  rewardKind: z.string(),
  rewardKindLabel: z.string(),
  rewards: z.array(teraRaidRewardItemSchema),
  tableHash: z.string(),
  tableIndex: z.number().int().nonnegative()
});

export const teraRaidRecordSchema = z.strictObject({
  ability: z.number().int(),
  abilityLabel: z.string(),
  abilityOptions: z.array(teraRaidEditableFieldOptionSchema).default([]),
  ballItem: z.string(),
  ballItemId: z.number().int(),
  captureLevel: z.number().int(),
  captureRate: z.number().int(),
  deliveryGroupId: z.number().int(),
  difficulty: z.number().int(),
  doubleActionHp: z.number().int(),
  doubleActionRate: z.number().int(),
  doubleActionTime: z.number().int(),
  fixedRewardPreview: z.string(),
  fixedRewardTableHash: z.string(),
  flawlessIvCount: z.number().int().nullable(),
  form: z.number().int(),
  gender: z.number().int(),
  genderLabel: z.string(),
  heldItem: z.string().nullable(),
  heldItemId: z.number().int(),
  heightMode: z.number().int(),
  heightModeLabel: z.string(),
  heightValue: z.number().int(),
  hpMultiplier: z.number().int(),
  ivSummary: z.string(),
  ivs: teraRaidIvsSchema,
  level: z.number().int(),
  lotteryRewardPreview: z.string(),
  lotteryRewardTableHash: z.string(),
  moveMode: z.number().int(),
  moveModeLabel: z.string(),
  moves: z.array(teraRaidMoveSchema),
  nature: z.number().int(),
  natureLabel: z.string(),
  provenance: teraRaidProvenanceSchema,
  entryIndex: z.number().int().nonnegative(),
  raidNo: z.number().int(),
  recordId: z.string(),
  region: z.string(),
  scaleMode: z.number().int(),
  scaleModeLabel: z.string(),
  scaleValue: z.number().int(),
  shieldTriggerHp: z.number().int(),
  shieldTriggerTime: z.number().int(),
  shinyLock: z.number().int(),
  shinyLockLabel: z.string(),
  spawnRate: z.number().int(),
  species: z.string(),
  speciesId: z.number().int(),
  starLabel: z.string(),
  starRank: z.number().int().nullable(),
  teraType: z.number().int(),
  teraTypeLabel: z.string(),
  version: z.number().int(),
  versionLabel: z.string(),
  weightMode: z.number().int(),
  weightModeLabel: z.string(),
  weightValue: z.number().int()
});

export const teraRaidsWorkflowStatsSchema = z.strictObject({
  sourceFileCount: z.number().int().nonnegative(),
  totalRaidCount: z.number().int().nonnegative(),
  totalRewardItemCount: z.number().int().nonnegative(),
  totalRewardTableCount: z.number().int().nonnegative()
});

export const teraRaidsWorkflowSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  editableFields: z.array(teraRaidEditableFieldSchema),
  fixedRewardTables: z.array(teraRaidRewardTableSchema),
  lotteryRewardTables: z.array(teraRaidRewardTableSchema),
  raids: z.array(teraRaidRecordSchema),
  stats: teraRaidsWorkflowStatsSchema,
  summary: workflowSummarySchema
});

export const loadTeraRaidsWorkflowResponseSchema = z.strictObject({
  workflow: teraRaidsWorkflowSchema
});

export const raidRewardProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema
});

const raidRewardUint32Schema = z.number().int().nonnegative().max(0xffff_ffff);

export const raidRewardItemRecordSchema = z.strictObject({
  entryId: raidRewardUint32Schema,
  itemId: raidRewardUint32Schema,
  itemName: z.string(),
  quantity: raidRewardUint32Schema,
  slot: z.number().int().positive(),
  values: z.array(raidRewardUint32Schema).min(5),
  weight: raidRewardUint32Schema
});

export const raidRewardTableRecordSchema = z.strictObject({
  archiveMember: z.string(),
  denId: z.string(),
  displayName: z.string(),
  gameVersion: z.string(),
  provenance: raidRewardProvenanceSchema,
  rank: z.number().int().nonnegative(),
  rewardKind: z.enum(['drop', 'bonus']),
  rewardKindLabel: z.string(),
  rewards: z.array(raidRewardItemRecordSchema),
  sourceTableHash: z.string(),
  tableIndex: z.number().int().nonnegative(),
  tableId: z.string()
});

export const raidRewardEditableFieldOptionSchema = z.strictObject({
  label: z.string(),
  value: raidRewardUint32Schema
});

export const raidRewardEditableFieldSchema = z.strictObject({
  field: z.string(),
  label: z.string(),
  maximumValue: z.number().int().nullable(),
  minimumValue: z.number().int().nullable(),
  options: z.array(raidRewardEditableFieldOptionSchema),
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

export const loadRaidBonusRewardsWorkflowResponseSchema = z.strictObject({
  workflow: raidRewardsWorkflowSchema
});

export const placementProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema, sourceFile: z.string(), sourceLayer: projectFileLayerSchema
});

export const placementEditableFieldOptionSchema = z.strictObject({
  label: z.string(), value: z.number().int()
});

export const placementFieldValueSchema = z.strictObject({
  description: z.string().optional().default(''), displayValue: z.string(), field: z.string(), group: z.string(), isReadOnly: z.boolean(), label: z.string(), maximumValue: z.number().optional().default(0), minimumValue: z.number().optional().default(0), options: z.array(placementEditableFieldOptionSchema).nullish(), value: z.string(), valueKind: z.string().optional().default('text')
});

export const placedObjectRecordSchema = z.strictObject({
  archiveMember: z.string(),
  categoryId: z.string().optional(),
  categoryLabel: z.string().optional(),
  chance: z.number().int().nullable(),
  chanceIndex: z.number().int().nonnegative().nullable(),
  fields: z.array(placementFieldValueSchema).nullish(),
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
  description: z.string().optional(), field: z.string(), group: z.string().optional(), isReadOnly: z.boolean().optional(), label: z.string(),
  maximumValue: z.number(), minimumValue: z.number(), options: z.array(placementEditableFieldOptionSchema).optional(), valueKind: z.string()
});

export const placementCategorySchema = z.strictObject({
  description: z.string(), id: z.string(), label: z.string(), objectCount: z.number().int().nonnegative()
});

export const placementWorkflowStatsSchema = z.strictObject({
  sourceFileCount: z.number().int().nonnegative(), totalAreaCount: z.number().int().nonnegative(), totalObjectCount: z.number().int().nonnegative()
});

export const placementWorkflowSchema = z.strictObject({
  categories: z.array(placementCategorySchema).nullish(), diagnostics: z.array(apiDiagnosticSchema),
  editableFields: z.array(placementEditableFieldSchema), objects: z.array(placedObjectRecordSchema),
  stats: placementWorkflowStatsSchema, summary: workflowSummarySchema
});

export const loadPlacementWorkflowResponseSchema = z.strictObject({ workflow: placementWorkflowSchema });

export const behaviorProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema
});

export const behaviorFieldOptionSchema = z.strictObject({
  label: z.string(),
  value: z.string()
});

export const behaviorFieldSchema = z.strictObject({
  description: z.string(),
  field: z.string(),
  group: z.string(),
  isReadOnly: z.boolean(),
  label: z.string(),
  maximumValue: z.number(),
  minimumValue: z.number(),
  options: z.array(behaviorFieldOptionSchema).optional(),
  valueKind: z.string()
});

export const behaviorFieldValueSchema = z.strictObject({
  field: z.string(),
  value: z.string()
});

export const behaviorEntryRecordSchema = z.strictObject({
  behavior: z.string(),
  behaviorLabel: z.string(),
  entryId: z.string(),
  fields: z.array(behaviorFieldValueSchema),
  formOptions: z.array(behaviorFieldOptionSchema).optional(),
  form: z.number().int(),
  grassShakeRadius: z.number(),
  hash1: z.string(),
  hash2: z.string(),
  hitboxRadius: z.number(),
  index: z.number().int().nonnegative(),
  internalSpeciesName: z.string(),
  label: z.string(),
  modelPart: z.string(),
  provenance: behaviorProvenanceSchema,
  speciesId: z.number().int(),
  speciesName: z.string()
});

export const behaviorWorkflowStatsSchema = z.strictObject({
  sourceFileCount: z.number().int().nonnegative(),
  totalBehaviorCount: z.number().int().nonnegative(),
  totalEntryCount: z.number().int().nonnegative()
});

export const behaviorWorkflowSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  entries: z.array(behaviorEntryRecordSchema),
  fields: z.array(behaviorFieldSchema),
  stats: behaviorWorkflowStatsSchema,
  summary: workflowSummarySchema
});

export const loadBehaviorWorkflowResponseSchema = z.strictObject({
  workflow: behaviorWorkflowSchema
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
  patchId: z.string().trim().min(1),
  paths: projectPathsSchema,
  session: editSessionSchema.nullable()
});

export const stageExeFsPatchResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: exeFsPatchWorkflowSchema
});

export const bagHookProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema
});

export const bagHookSlotRecordSchema = z.strictObject({
  isReserved: z.boolean(),
  itemId: z.number().int().nonnegative().nullable(),
  itemName: z.string(),
  notes: z.string(),
  owner: z.string(),
  provenance: bagHookProvenanceSchema,
  quantity: z.number().int().positive().nullable(),
  reservedFor: z.string(),
  slot: z.number().int().positive(),
  status: z.string()
});

export const bagHookWorkflowStatsSchema = z.strictObject({
  emptySlotCount: z.number().int().nonnegative(),
  occupiedSlotCount: z.number().int().nonnegative(),
  reservedSlotCount: z.number().int().nonnegative(),
  sourceFileCount: z.number().int().nonnegative(),
  totalSlotCount: z.number().int().nonnegative()
});

export const bagHookWorkflowSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  installMessage: z.string(),
  installStatus: z.string(),
  slots: z.array(bagHookSlotRecordSchema),
  stats: bagHookWorkflowStatsSchema,
  summary: workflowSummarySchema
});

export const loadBagHookWorkflowResponseSchema = z.strictObject({
  workflow: bagHookWorkflowSchema
});

export const stageBagHookInstallRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  session: editSessionSchema.nullable()
});

export const stageBagHookInstallResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: bagHookWorkflowSchema
});

export const stageBagHookUninstallRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  session: editSessionSchema.nullable()
});

export const stageBagHookUninstallResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: bagHookWorkflowSchema
});

export const catchCapProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema
});

export const catchCapRecordSchema = z.strictObject({
  badgeCount: z.number().int().min(0).max(8),
  label: z.string(),
  // Installed KM tables are owned bytes. Keep malformed raw values decodable so the
  // editor can show the problem and still let the user repair or uninstall the hook.
  levelCap: z.number().int().min(0).max(255),
  maximumLevelCap: z.number().int().min(1).max(100),
  minimumLevelCap: z.number().int().min(1).max(100)
});

export const catchCapSelectionSchema = z.strictObject({
  badgeCount: z.number().int().min(0).max(8),
  levelCap: z.number().int().min(1).max(100)
});

export const catchCapWorkflowStatsSchema = z.strictObject({
  sourceFileCount: z.number().int().nonnegative(),
  totalCapCount: z.number().int().nonnegative()
});

export const catchCapWorkflowSchema = z.strictObject({
  buildId: z.string(),
  capLogicSha256: z.string(),
  caps: z.array(catchCapRecordSchema),
  detectedGame: z.enum(['sword', 'shield']).nullable(),
  diagnostics: z.array(apiDiagnosticSchema),
  displayHookOffsetHex: z.string(),
  installMessage: z.string(),
  installStatus: z.enum(['disabled', 'readOnly', 'available', 'installed', 'blocked', 'foreign']),
  logicExpression: z.string(),
  provenance: catchCapProvenanceSchema,
  runtimeHookOffsetHex: z.string(),
  stats: catchCapWorkflowStatsSchema,
  summary: workflowSummarySchema
});

export const loadCatchCapWorkflowResponseSchema = z.strictObject({
  workflow: catchCapWorkflowSchema
});

export const stageCatchCapRequestSchema = z.strictObject({
  caps: z.array(catchCapSelectionSchema),
  paths: projectPathsSchema,
  session: editSessionSchema.nullable()
});

export const stageCatchCapResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: catchCapWorkflowSchema
});

export const stageCatchCapUninstallRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  session: editSessionSchema.nullable()
});

export const stageCatchCapUninstallResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: catchCapWorkflowSchema
});

export const hyperTrainingProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema
});

export const hyperTrainingSourceRecordSchema = z.strictObject({
  label: z.string(),
  provenance: hyperTrainingProvenanceSchema,
  relativePath: z.string(),
  sourceId: z.string(),
  status: z.enum(['available', 'missing', 'optionalMissing'])
});

export const hyperTrainingLevelRuleSchema = z.strictObject({
  dialogueMinimumLevel: z.number().int().min(1).max(100).nullable(),
  dialogueSummary: z.string(),
  levelsMatch: z.boolean(),
  maximumAllowedLevel: z.number().int(),
  minimumAllowedLevel: z.number().int(),
  minimumLevel: z.number().int().min(1).max(100),
  runtimeMinimumLevel: z.number().int().min(1).max(100),
  runtimeSummary: z.string(),
  scriptCell: z.string(),
  scriptMinimumLevel: z.number().int().min(1).max(100),
  vanillaMinimumLevel: z.number().int()
});

export const hyperTrainingWorkflowStatsSchema = z.strictObject({
  outputFileCount: z.number().int().nonnegative(),
  sourceFileCount: z.number().int().nonnegative()
});

export const hyperTrainingWorkflowSchema = z.strictObject({
  buildId: z.union([z.literal('unknown'), z.string().regex(/^[A-F0-9]{40}$/)]),
  detectedGame: z.enum(['sword', 'shield']).nullable(),
  diagnostics: z.array(apiDiagnosticSchema),
  installMessage: z.string(),
  installStatus: z.enum(['available', 'blocked', 'disabled', 'installed', 'readOnly']),
  levelRule: hyperTrainingLevelRuleSchema,
  sources: z.array(hyperTrainingSourceRecordSchema),
  stats: hyperTrainingWorkflowStatsSchema,
  summary: workflowSummarySchema
});

export const loadHyperTrainingWorkflowResponseSchema = z.strictObject({
  workflow: hyperTrainingWorkflowSchema
});

export const stageHyperTrainingRequestSchema = z.strictObject({
  minimumLevel: z.number().int().min(1).max(100),
  paths: projectPathsSchema,
  session: editSessionSchema.nullable()
});

export const stageHyperTrainingResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: hyperTrainingWorkflowSchema
});

const typeChartTypeDefinitions = [
  ['Normal', 'NOR', '#A8A878'],
  ['Fire', 'FIR', '#F05030'],
  ['Water', 'WAT', '#6890F0'],
  ['Electric', 'ELE', '#F8D030'],
  ['Grass', 'GRA', '#78C850'],
  ['Ice', 'ICE', '#78C8F0'],
  ['Fighting', 'FIG', '#A05038'],
  ['Poison', 'POI', '#A040A0'],
  ['Ground', 'GRO', '#E0C068'],
  ['Flying', 'FLY', '#8080F0'],
  ['Psychic', 'PSY', '#F85888'],
  ['Bug', 'BUG', '#A8B820'],
  ['Rock', 'ROC', '#B8A038'],
  ['Ghost', 'GHO', '#6060B0'],
  ['Dragon', 'DRA', '#7038F8'],
  ['Dark', 'DAR', '#705848'],
  ['Steel', 'STE', '#B8B8D0'],
  ['Fairy', 'FAI', '#EE99EE']
] as const;

const typeChartIdentityByGame = {
  scarlet: {
    buildId: '421C5411B487EB4D049DD065FEC9547773E8E598',
    chartOffsetHex: 'main.ro+0x0082286C'
  },
  shield: {
    buildId: 'A16802625E7826BF83B6F9708E475B912A9AB7DF',
    chartOffsetHex: 'main.ro+0x00743600'
  },
  sword: {
    buildId: 'A3B75BCD3311385AEED67FBEEB79CBB7BF02F471',
    chartOffsetHex: 'main.ro+0x00743600'
  },
  violet: {
    buildId: '709BFD66115298640155FCC4979DBA151C7CC79A',
    chartOffsetHex: 'main.ro+0x0082286C'
  },
  za: {
    buildId: 'B1F12FD919EAE86AB8A978317677E64BCE443D1F',
    chartOffsetHex: 'main.ro+0x0019F2A4'
  }
} as const;

const typeChartEffectivenessSchema = z.union([
  z.literal(0),
  z.literal(2),
  z.literal(4),
  z.literal(8)
]);

export const typeChartProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.literal('exefs/main'),
  sourceLayer: projectFileLayerSchema
});

export const typeChartSourceRecordSchema = z.strictObject({
  label: z.string().min(1),
  provenance: typeChartProvenanceSchema,
  relativePath: z.literal('exefs/main'),
  sourceId: z.string().min(1),
  status: z.enum(['available', 'missing'])
});

export const typeChartTypeDefinitionSchema = z.strictObject({
  color: z.string().regex(/^#[A-F0-9]{6}$/),
  label: z.string().min(1),
  shortLabel: z.string().min(1),
  typeIndex: z.number().int().min(0).max(17)
});

const typeChartTypeDefinitionsSchema = z
  .array(typeChartTypeDefinitionSchema)
  .length(18)
  .superRefine((types, context) => {
    for (const [typeIndex, expected] of typeChartTypeDefinitions.entries()) {
      const type = types.find((candidate) => candidate.typeIndex === typeIndex);
      if (
        !type ||
        type.label !== expected[0] ||
        type.shortLabel !== expected[1] ||
        type.color !== expected[2]
      ) {
        context.addIssue({
          code: 'custom',
          message: `Type Chart definition ${typeIndex} does not match the canonical display mapping.`,
          path: [typeIndex]
        });
      }
    }
  });

export const typeChartCellSchema = z.strictObject({
  attackTypeIndex: z.number().int().min(0).max(17),
  defenseTypeIndex: z.number().int().min(0).max(17),
  effectiveness: typeChartEffectivenessSchema,
  vanillaEffectiveness: typeChartEffectivenessSchema
});

const typeChartCellsSchema = z
  .array(typeChartCellSchema)
  .length(18 * 18)
  .superRefine((cells, context) => {
    const coordinates = new Set(
      cells.map((cell) => `${cell.attackTypeIndex}:${cell.defenseTypeIndex}`)
    );
    if (coordinates.size !== 18 * 18) {
      context.addIssue({
        code: 'custom',
        message: 'Type Chart cells must contain every attack and defense coordinate exactly once.'
      });
    }
  });

export const typeChartWorkflowStatsSchema = z.strictObject({
  chartCellCount: z.literal(18 * 18),
  outputFileCount: z.number().int().nonnegative(),
  sourceFileCount: z.number().int().nonnegative()
});

export const typeChartWorkflowSchema = z.strictObject({
  buildId: z.union([z.literal('unknown'), z.string().regex(/^[A-F0-9]{40}$/)]),
  cells: typeChartCellsSchema,
  chartOffsetHex: z.string(),
  detectedGame: projectGameSchema.nullable(),
  diagnostics: z.array(apiDiagnosticSchema),
  installMessage: z.string(),
  installStatus: z.enum(['available', 'blocked', 'disabled', 'modified', 'readOnly']),
  source: typeChartSourceRecordSchema.nullable(),
  stats: typeChartWorkflowStatsSchema,
  summary: workflowSummarySchema,
  types: typeChartTypeDefinitionsSchema
}).superRefine((workflow, context) => {
  if (workflow.summary.id !== 'typeChart') {
    context.addIssue({
      code: 'custom',
      message: 'Type Chart workflow summary must use the Type Chart identity.',
      path: ['summary', 'id']
    });
  }

  if (workflow.detectedGame === null) {
    return;
  }

  const expectedIdentity = typeChartIdentityByGame[workflow.detectedGame];
  if (workflow.buildId !== expectedIdentity.buildId) {
    context.addIssue({
      code: 'custom',
      message: 'Type Chart build ID does not match the detected game.',
      path: ['buildId']
    });
  }
  const hasAllowedBlockedUnknownOffset =
    workflow.installStatus === 'blocked' && workflow.chartOffsetHex === 'unknown';
  if (
    workflow.chartOffsetHex !== expectedIdentity.chartOffsetHex &&
    !hasAllowedBlockedUnknownOffset
  ) {
    context.addIssue({
      code: 'custom',
      message: 'Type Chart offset does not match the detected game.',
      path: ['chartOffsetHex']
    });
  }
});

export const loadTypeChartWorkflowResponseSchema = z.strictObject({
  workflow: typeChartWorkflowSchema
});

export const stageTypeChartRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  session: editSessionSchema.nullable(),
  values: z.array(typeChartEffectivenessSchema).length(18 * 18)
});

export const stageTypeChartResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: typeChartWorkflowSchema
});

export const stageTypeChartUninstallRequestSchema = z.strictObject({ paths: projectPathsSchema, session: editSessionSchema.nullable() });

export const stageTypeChartUninstallResponseSchema = z.strictObject({ diagnostics: z.array(apiDiagnosticSchema), session: editSessionSchema, workflow: typeChartWorkflowSchema });

export const ivScreenProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema
});

export const ivScreenReservedRegionSchema = z.strictObject({
  label: z.string(),
  length: z.number().int().nullable(),
  offsetLabel: z.string(),
  regionId: z.string(),
  rule: z.string(),
  startOffset: z.number().int().nullable()
});

export const ivScreenWorkflowStatsSchema = z.strictObject({
  reservedMainTextRegionCount: z.number().int().nonnegative(),
  sourceFileCount: z.number().int().nonnegative()
});

export const ivScreenInstallStatusSchema = z.enum([
  'disabled',
  'readOnly',
  'available',
  'installed',
  'blocked',
  'foreign'
]);

export const ivScreenWorkflowSchema = z.strictObject({
  buildId: z.string(),
  canUninstall: z.boolean(),
  detectedGame: z.enum(['sword', 'shield']).nullable(),
  diagnostics: z.array(apiDiagnosticSchema),
  hyperTrainingWrapperOffsetHex: z.string(),
  installMessage: z.string(),
  installStatus: ivScreenInstallStatusSchema,
  marker: z.string(),
  primaryValueSourceOffsetHex: z.string(),
  provenance: ivScreenProvenanceSchema,
  rawIvGetterOffsetHex: z.string(),
  reservedRegions: z.array(ivScreenReservedRegionSchema),
  stats: ivScreenWorkflowStatsSchema,
  summary: workflowSummarySchema,
  xToggleRefreshOffsetHex: z.string()
});

export const loadIvScreenWorkflowResponseSchema = z.strictObject({
  workflow: ivScreenWorkflowSchema
});

export const stageIvScreenInstallRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  session: editSessionSchema.nullable()
});

export const stageIvScreenInstallResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: ivScreenWorkflowSchema
});

export const stageIvScreenUninstallRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  session: editSessionSchema.nullable()
});

export const stageIvScreenUninstallResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: ivScreenWorkflowSchema
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

export const royalCandyLevelCapRecordSchema = z.strictObject({
  label: z.string(),
  levelCap: z.number().int(),
  maximumLevelCap: z.number().int(),
  milestoneId: z.string(),
  minimumLevelCap: z.number().int(),
  progressHash: z.string(),
  progressKind: z.string(),
  slot: z.number().int().nonnegative(),
  workMinimum: z.number().int().nullable()
});

export const royalCandyLevelCapSelectionSchema = z.strictObject({
  levelCap: z.number().int(),
  slot: z.number().int().nonnegative()
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
  levelCaps: z.array(royalCandyLevelCapRecordSchema).default([]),
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
  levelCaps: z.array(royalCandyLevelCapSelectionSchema).optional(),
  paths: projectPathsSchema,
  session: editSessionSchema.nullable(),
  workflowId: z.string().trim().min(1)
});

export const stageRoyalCandyWorkflowResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: royalCandyWorkflowSchema
});

export const startingItemsProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema
});

export const startingItemOptionRecordSchema = z.strictObject({
  category: z.string(),
  isKeyItem: z.boolean(),
  itemId: z.number().int().positive(),
  name: z.string()
});

export const startingItemsBlockerKindSchema = z.enum([
  'none',
  'bagHookMissing',
  'bagHookDamaged',
  'itemMetadataUnavailable'
]);

export const startingItemGrantStatusSchema = z.enum([
  'empty',
  'occupied',
  'conflict',
  'unavailable'
]);

export const startingItemGrantRecordSchema = z.strictObject({
  isKeyItem: z.boolean(),
  itemId: z.number().int().positive().nullable(),
  itemName: z.string(),
  owner: z.string(),
  provenance: startingItemsProvenanceSchema,
  quantity: z.number().int().min(1).max(999),
  slot: z.number().int().min(2).max(20),
  status: startingItemGrantStatusSchema
});

export const startingItemGrantSelectionSchema = z.strictObject({
  itemId: z.number().int().nonnegative().nullable(),
  quantity: z.number().int().min(1).max(999),
  slot: z.number().int().min(2).max(20)
});

export const startingItemsWorkflowStatsSchema = z.strictObject({
  itemOptionCount: z.number().int().nonnegative(),
  occupiedGrantSlotCount: z.number().int().nonnegative(),
  sourceFileCount: z.number().int().nonnegative(),
  totalGrantSlotCount: z.number().int().nonnegative()
});

export const startingItemsWorkflowSchema = z.strictObject({
  blockerKind: startingItemsBlockerKindSchema,
  diagnostics: z.array(apiDiagnosticSchema),
  grants: z.array(startingItemGrantRecordSchema),
  installMessage: z.string(),
  installStatus: z.enum(['available', 'readOnly', 'blocked']),
  itemOptions: z.array(startingItemOptionRecordSchema),
  stats: startingItemsWorkflowStatsSchema,
  summary: workflowSummarySchema
});

export const loadStartingItemsWorkflowResponseSchema = z.strictObject({
  workflow: startingItemsWorkflowSchema
});

export const stageStartingItemsRequestSchema = z.strictObject({
  grants: z.array(startingItemGrantSelectionSchema),
  paths: projectPathsSchema,
  session: editSessionSchema.nullable()
});

export const stageStartingItemsResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: startingItemsWorkflowSchema
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

export const modMergerFileRecordSchema = z.strictObject({
  name: z.string(),
  relativePath: z.string(),
  size: z.number().int().nonnegative(),
  status: z.string(),
  supportKind: z.string()
});

export const modMergerWorkflowStatsSchema = z.strictObject({
  directory1FileCount: z.number().int().nonnegative(),
  directory2FileCount: z.number().int().nonnegative(),
  matchingFileCount: z.number().int().nonnegative()
});

export const modMergerWorkflowSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  directory1Files: z.array(modMergerFileRecordSchema),
  directory2Files: z.array(modMergerFileRecordSchema),
  modDirectory1: z.string().nullable(),
  modDirectory2: z.string().nullable(),
  outputRootPath: z.string().nullable(),
  stats: modMergerWorkflowStatsSchema,
  summary: workflowSummarySchema
});

export const modMergerConflictRecordSchema = z.strictObject({
  conflictId: z.string(),
  description: z.string(),
  directory1Value: z.string(),
  directory2Value: z.string(),
  label: z.string(),
  relativePath: z.string(),
  resolution: z.enum(['mod1', 'mod2']).nullable()
});

export const modMergerFilePreviewRecordSchema = z.strictObject({
  conflictCount: z.number().int().nonnegative(),
  directory1ChangeCount: z.number().int().nonnegative(),
  directory2ChangeCount: z.number().int().nonnegative(),
  mergeKind: z.string(),
  outputRelativePath: z.string(),
  relativePath: z.string(),
  status: z.string(),
  summary: z.string(),
  supportKind: z.string()
});

export const modMergerPreviewSchema = z.strictObject({
  canApply: z.boolean(),
  conflictFileCount: z.number().int().nonnegative(),
  conflicts: z.array(modMergerConflictRecordSchema),
  diagnostics: z.array(apiDiagnosticSchema),
  files: z.array(modMergerFilePreviewRecordSchema),
  mergeMode: modMergerMergeModeSchema,
  readyFileCount: z.number().int().nonnegative(),
  reviewToken: z.string(),
  selectedFileCount: z.number().int().nonnegative(),
  status: z.string(),
  unresolvedConflictCount: z.number().int().nonnegative()
});

export const loadModMergerWorkflowResponseSchema = z.strictObject({
  workflow: modMergerWorkflowSchema
});

export const stageModMergeResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  preview: modMergerPreviewSchema,
  workflow: modMergerWorkflowSchema
});

export const applyModMergeResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  preview: modMergerPreviewSchema,
  workflow: modMergerWorkflowSchema,
  writtenFiles: z.array(z.string())
});

export const svModMergerSourceRecordSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  fileCount: z.number().int().nonnegative(),
  isEnabled: z.boolean(),
  kind: z.string(),
  name: z.string(),
  overrideCount: z.number().int().nonnegative(),
  path: z.string(),
  sourceIndex: z.number().int().nonnegative(),
  status: z.string()
});

export const svModMergerWorkflowStatsSchema = z.strictObject({
  enabledSourceCount: z.number().int().nonnegative(),
  outputFileCount: z.number().int().nonnegative(),
  overrideCount: z.number().int().nonnegative(),
  sourceCount: z.number().int().nonnegative(),
  sourceFileCount: z.number().int().nonnegative()
});

export const svModMergerWorkflowSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  outputRootPath: z.string().nullable(),
  sources: z.array(svModMergerSourceRecordSchema),
  stats: svModMergerWorkflowStatsSchema,
  summary: workflowSummarySchema
});

export const svModMergerFilePreviewRecordSchema = z.strictObject({
  mergeKind: z.string(),
  outputRelativePath: z.string(),
  overrideCount: z.number().int().nonnegative(),
  relativePath: z.string(),
  sourceIndex: z.number().int(),
  sourceName: z.string(),
  status: z.string(),
  summary: z.string(),
  supportKind: z.string()
});

export const svModMergerPreviewSchema = z.strictObject({
  canApply: z.boolean(),
  conflictFileCount: z.number().int().nonnegative(),
  diagnostics: z.array(apiDiagnosticSchema),
  files: z.array(svModMergerFilePreviewRecordSchema),
  readyFileCount: z.number().int().nonnegative(),
  selectedFileCount: z.number().int().nonnegative(),
  status: z.string(),
  unresolvedConflictCount: z.number().int().nonnegative()
});

export const loadSvModMergerWorkflowResponseSchema = z.strictObject({
  workflow: svModMergerWorkflowSchema
});

export const stageSvModMergeResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  preview: svModMergerPreviewSchema,
  workflow: svModMergerWorkflowSchema
});

export const applySvModMergeResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  preview: svModMergerPreviewSchema,
  workflow: svModMergerWorkflowSchema,
  writtenFiles: z.array(z.string())
});

export const zaModMergerSourceRecordSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  fileCount: z.number().int().nonnegative(),
  isEnabled: z.boolean(),
  kind: z.string(),
  name: z.string(),
  overrideCount: z.number().int().nonnegative(),
  path: z.string(),
  sourceIndex: z.number().int(),
  status: z.string()
});
export const zaModMergerWorkflowStatsSchema = z.strictObject({
  enabledSourceCount: z.number().int().nonnegative(),
  outputFileCount: z.number().int().nonnegative(),
  overrideCount: z.number().int().nonnegative(),
  sourceCount: z.number().int().nonnegative(),
  sourceFileCount: z.number().int().nonnegative()
});
export const zaModMergerWorkflowSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  outputRootPath: z.string().nullable(),
  sources: z.array(zaModMergerSourceRecordSchema),
  stats: zaModMergerWorkflowStatsSchema,
  summary: workflowSummarySchema
});
export const zaModMergerFilePreviewRecordSchema = z.strictObject({
  mergeKind: z.string(),
  outputRelativePath: z.string(),
  overrideCount: z.number().int().nonnegative(),
  relativePath: z.string(),
  sourceIndex: z.number().int(),
  sourceName: z.string(),
  status: z.string(),
  summary: z.string(),
  supportKind: z.string()
});
export const zaModMergerPreviewSchema = z.strictObject({
  canApply: z.boolean(),
  conflictFileCount: z.number().int().nonnegative(),
  diagnostics: z.array(apiDiagnosticSchema),
  files: z.array(zaModMergerFilePreviewRecordSchema),
  readyFileCount: z.number().int().nonnegative(),
  selectedFileCount: z.number().int().nonnegative(),
  status: z.string(),
  unresolvedConflictCount: z.number().int().nonnegative()
});

export const loadZaModMergerWorkflowResponseSchema = z.strictObject({
  workflow: zaModMergerWorkflowSchema
});

export const stageZaModMergeResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  preview: zaModMergerPreviewSchema,
  workflow: zaModMergerWorkflowSchema
});

export const applyZaModMergeResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  preview: zaModMergerPreviewSchema,
  workflow: zaModMergerWorkflowSchema,
  writtenFiles: z.array(z.string())
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
  query: textWorkflowQuerySchema.nullable().optional(),
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

export const updateGiftPokemonFieldRequestSchema = z.strictObject({
  field: z.string(),
  giftIndex: z.number().int().nonnegative(),
  paths: projectPathsSchema,
  session: editSessionSchema.nullable(),
  value: z.string()
});

export const updateGiftPokemonFieldResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: giftPokemonWorkflowSchema
});

export const updateTradePokemonFieldRequestSchema = z.strictObject({
  field: z.string(),
  paths: projectPathsSchema,
  session: editSessionSchema.nullable(),
  tradeIndex: z.number().int().nonnegative(),
  value: z.string()
});

export const updateTradePokemonFieldResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: tradePokemonWorkflowSchema
});

export const updateStaticEncounterFieldRequestSchema = z.strictObject({
  encounterId: z.string().optional(),
  encounterIndex: z.number().int().nonnegative(),
  field: z.string(),
  paths: projectPathsSchema,
  session: editSessionSchema.nullable(),
  value: z.string()
});

export const staticEncounterFieldUpdateSchema = z.strictObject({
  encounterId: z.string().optional(),
  encounterIndex: z.number().int().nonnegative(),
  field: z.string(),
  value: z.string()
});

export const updateStaticEncounterFieldsRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  session: editSessionSchema.nullable(),
  updates: z.array(staticEncounterFieldUpdateSchema).min(1)
});

export const updateStaticEncounterFieldResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: staticEncountersWorkflowSchema
});

export const updateRentalPokemonFieldRequestSchema = z.strictObject({
  field: z.string(),
  paths: projectPathsSchema,
  rentalIndex: z.number().int().nonnegative(),
  session: editSessionSchema.nullable(),
  value: z.string()
});

export const updateRentalPokemonFieldResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: rentalPokemonWorkflowSchema
});

export const updateDynamaxAdventureFieldRequestSchema = z
  .strictObject({
    entryIndex: z.number().int().min(0).max(dynamaxAdventureNormalEncounterCount - 1),
    field: dynamaxAdventureEditableFieldNameSchema,
    paths: projectPathsSchema,
    session: editSessionSchema.nullable(),
    value: z.string().regex(/^-?\d+$/)
  })
  .superRefine(validateDynamaxAdventureRequestGame)
  .superRefine(validateDynamaxAdventureUpdateRequest);

const dynamaxAdventureFieldResponseShapeSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: dynamaxAdventuresWorkflowSchema
});

export const updateDynamaxAdventureFieldResponseSchema =
  dynamaxAdventureFieldResponseShapeSchema.superRefine(
    validateDynamaxAdventureUpdateResponse
  );

export const stageDynamaxAdventureRepairResponseSchema =
  updateDynamaxAdventureFieldResponseSchema.superRefine(
    validateDynamaxAdventureRepairResponse
  );

export const stageDynamaxAdventureRestoreResponseSchema =
  dynamaxAdventureFieldResponseShapeSchema.superRefine(
    validateDynamaxAdventureRestoreResponse
  );

function validateDynamaxAdventureRestoreResponse(
  response: z.infer<typeof dynamaxAdventureFieldResponseShapeSchema>,
  context: z.RefinementCtx
) {
  if (
    response.diagnostics.some((diagnostic) => diagnostic.severity === 'error')
  ) {
    if (
      response.session.hasPendingChanges ||
      response.session.pendingEdits.length > 0
    ) {
      addDynamaxAdventureContractIssue(
        context,
        ['session'],
        'Failed Dynamax Adventures table restore staging must return a sanitized empty session.'
      );
    }
    return;
  }

  const pendingEdits = response.session.pendingEdits;
  if (
    !response.session.hasPendingChanges ||
    pendingEdits.length !== 1 ||
    !response.workflow.canRestoreVanillaTable
  ) {
    addDynamaxAdventureContractIssue(
      context,
      ['session', 'pendingEdits'],
      'Dynamax Adventures table restore must stage exactly one canonical recovery marker.'
    );
    return;
  }

  const owner = response.workflow.encounters[0];
  const edit = pendingEdits[0]!;
  if (
    !owner ||
    owner.provenance.fileState !== 'layeredOverride' ||
    owner.provenance.sourceLayer !== 'layered' ||
    edit.domain !== 'workflow.dynamaxAdventures' ||
    edit.recordId !== `dynamaxAdventure:${owner.entryIndex}` ||
    edit.field !== 'level' ||
    edit.newValue !== owner.level.toString() ||
    edit.summary !== 'Restore the vanilla Dynamax Adventures table.' ||
    edit.sources.length !== 1 ||
    edit.sources[0]?.layer !== owner.provenance.sourceLayer ||
    edit.sources[0]?.relativePath !== owner.provenance.sourceFile
  ) {
    addDynamaxAdventureContractIssue(
      context,
      ['session', 'pendingEdits', 0],
      'Dynamax Adventures table restore marker does not match the first loaded layered row.'
    );
  }
}

function validateDynamaxAdventureRepairResponse(
  response: z.infer<typeof updateDynamaxAdventureFieldResponseSchema>,
  context: z.RefinementCtx
) {
  if (
    [
      ...response.diagnostics,
      ...response.workflow.diagnostics,
      ...response.workflow.summary.diagnostics
    ].some((diagnostic) => diagnostic.severity === 'error')
  ) {
    return;
  }

  const pendingEdits = response.session.pendingEdits;
  if (pendingEdits.length !== 1) {
    addDynamaxAdventureContractIssue(
      context,
      ['session', 'pendingEdits'],
      'Dynamax Adventures repair must stage exactly one canonical owner marker.'
    );
    return;
  }

  const edit = pendingEdits[0]!;
  const ownerMatch = /^dynamaxAdventure:(0|[1-9]\d*)$/.exec(edit.recordId ?? '');
  const ownerIndex = ownerMatch ? Number(ownerMatch[1]) : Number.NaN;
  const owner = Number.isSafeInteger(ownerIndex)
    ? response.workflow.encounters[ownerIndex]
    : undefined;
  if (
    edit.domain !== 'workflow.dynamaxAdventures' ||
    edit.field !== 'level' ||
    edit.summary !== 'Repair Dynamax Adventures executable projection.' ||
    !owner?.isEditable ||
    edit.newValue !== owner.level.toString()
  ) {
    addDynamaxAdventureContractIssue(
      context,
      ['session', 'pendingEdits', 0],
      'Dynamax Adventures repair marker does not match its safe normal-row owner.'
    );
  }

  if (
    !owner ||
    edit.sources.length !== 1 ||
    edit.sources[0]?.layer !== owner.provenance.sourceLayer ||
    edit.sources[0]?.relativePath !== owner.provenance.sourceFile
  ) {
    addDynamaxAdventureContractIssue(
      context,
      ['session', 'pendingEdits', 0, 'sources'],
      'Dynamax Adventures repair marker source must exactly match its owner provenance.'
    );
  }
}

function validateDynamaxAdventureUpdateRequest(
  request: {
    field: z.infer<typeof dynamaxAdventureEditableFieldNameSchema>;
    value: string;
  },
  context: z.RefinementCtx
) {
  if (!isValidDynamaxAdventureFieldValue(request.field, request.value)) {
    addDynamaxAdventureContractIssue(
      context,
      ['value'],
      `Dynamax Adventures ${request.field} is outside its supported range.`
    );
  }
}

function validateDynamaxAdventureUpdateResponse(
  response: {
    session: z.infer<typeof editSessionSchema>;
    workflow: z.infer<typeof dynamaxAdventuresWorkflowSchema>;
  },
  context: z.RefinementCtx
) {
  const { pendingEdits } = response.session;
  if (response.session.hasPendingChanges !== (pendingEdits.length > 0)) {
    addDynamaxAdventureContractIssue(
      context,
      ['session', 'hasPendingChanges'],
      'Dynamax Adventures session pending state does not match its edits.'
    );
  }

  const ownerIndexes = new Set<number>();
  pendingEdits.forEach((edit, editIndex) => {
    if (edit.summary === 'Restore the vanilla Dynamax Adventures table.') {
      addDynamaxAdventureContractIssue(
        context,
        ['session', 'pendingEdits', editIndex, 'summary'],
        'Ordinary Dynamax Adventures field updates cannot stage a full-table restore marker.'
      );
    }

    if (edit.domain !== 'workflow.dynamaxAdventures') {
      addDynamaxAdventureContractIssue(
        context,
        ['session', 'pendingEdits', editIndex, 'domain'],
        'Dynamax Adventures update responses cannot contain another workflow domain.'
      );
      return;
    }

    const ownerMatch = /^dynamaxAdventure:(0|[1-9]\d*)$/.exec(edit.recordId ?? '');
    const ownerIndex = ownerMatch ? Number(ownerMatch[1]) : Number.NaN;
    if (
      !Number.isSafeInteger(ownerIndex) ||
      ownerIndex < 0 ||
      ownerIndex >= dynamaxAdventureNormalEncounterCount
    ) {
      addDynamaxAdventureContractIssue(
        context,
        ['session', 'pendingEdits', editIndex, 'recordId'],
        'Dynamax Adventures pending edits require one canonical normal-row owner.'
      );
    } else {
      ownerIndexes.add(ownerIndex);
      const owner = response.workflow.encounters[ownerIndex];
      if (owner?.isEditable !== true) {
        addDynamaxAdventureContractIssue(
          context,
          ['session', 'pendingEdits', editIndex, 'recordId'],
          'Dynamax Adventures pending edits must target an editable normal row.'
        );
      }
      if (
        !owner ||
        edit.sources.length !== 1 ||
        edit.sources[0]?.layer !== owner.provenance.sourceLayer ||
        edit.sources[0]?.relativePath !== owner.provenance.sourceFile
      ) {
        addDynamaxAdventureContractIssue(
          context,
          ['session', 'pendingEdits', editIndex, 'sources'],
          'Dynamax Adventures pending edit source must exactly match its owner provenance.'
        );
      }
    }

    const fieldResult = dynamaxAdventureEditableFieldNameSchema.safeParse(edit.field);
    if (
      !fieldResult.success ||
      typeof edit.newValue !== 'string' ||
      !isValidDynamaxAdventureFieldValue(fieldResult.data, edit.newValue)
    ) {
      addDynamaxAdventureContractIssue(
        context,
        ['session', 'pendingEdits', editIndex],
        'Dynamax Adventures pending edit field or value is outside the safe contract.'
      );
    }
  });

  if (ownerIndexes.size > 1) {
    addDynamaxAdventureContractIssue(
      context,
      ['session', 'pendingEdits'],
      'Dynamax Adventures pending edits must belong to one normal row.'
    );
  }
}

function isValidDynamaxAdventureFieldValue(
  field: z.infer<typeof dynamaxAdventureEditableFieldNameSchema>,
  value: string
) {
  if (!/^-?\d+$/.test(value)) {
    return false;
  }

  const parsedValue = Number(value);
  if (!Number.isSafeInteger(parsedValue)) {
    return false;
  }

  const [minimumValue, maximumValue] = dynamaxAdventureFieldRanges[field];
  if (parsedValue < minimumValue || parsedValue > maximumValue) {
    return false;
  }

  return (
    field !== 'guaranteedPerfectIvs' ||
    [0, 2, 3, 4, 5, 6].includes(parsedValue)
  );
}

export const updateShopInventoryItemRequestSchema = z.strictObject({
  field: z.string(),
  paths: projectPathsSchema,
  rowId: z.string().optional(),
  session: editSessionSchema.nullable(),
  shopId: z.string(),
  slot: z.number().int().positive(),
  value: z.string()
});

export const updateShopInventoryItemResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: shopsWorkflowSchema
});

export const updateRaidBattleSlotFieldRequestSchema = z.strictObject({
  field: z.string(),
  paths: projectPathsSchema,
  session: editSessionSchema.nullable(),
  slot: z.number().int().positive(),
  tableId: z.string(),
  value: z.string()
});

export const raidBattleFieldUpdateSchema = z.strictObject({
  field: z.string(),
  slot: z.number().int().positive(),
  tableId: z.string(),
  value: z.string()
});

export const updateRaidBattleSlotFieldsRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  session: editSessionSchema.nullable(),
  updates: z.array(raidBattleFieldUpdateSchema).min(1)
});

export const updateRaidBattleSlotFieldResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: raidBattlesWorkflowSchema
});

export const updateRaidBattleSlotFieldsResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: raidBattlesWorkflowSchema
});

export const updateTeraRaidFieldRequestSchema = z.strictObject({
  field: z.string(),
  paths: projectPathsSchema,
  recordId: z.string(),
  session: editSessionSchema.nullable(),
  value: z.string()
});

export const teraRaidFieldUpdateSchema = z.strictObject({
  field: z.string(),
  recordId: z.string(),
  value: z.string()
});

export const updateTeraRaidFieldsRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  session: editSessionSchema.nullable(),
  updates: z.array(teraRaidFieldUpdateSchema)
});

export const updateTeraRaidFieldResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: teraRaidsWorkflowSchema
});

export const updateTeraRaidFieldsResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: teraRaidsWorkflowSchema
});

export const updateRaidRewardFieldRequestSchema = z.strictObject({
  field: z.string(),
  paths: projectPathsSchema,
  session: editSessionSchema.nullable(),
  slot: z.number().int().positive(),
  tableId: z.string(),
  value: z.string()
});

export const raidRewardFieldUpdateSchema = z.strictObject({
  field: z.string(),
  slot: z.number().int().positive(),
  tableId: z.string(),
  value: z.string()
});

export const updateRaidRewardFieldsRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  session: editSessionSchema.nullable(),
  updates: z.array(raidRewardFieldUpdateSchema).min(1)
});

export const updateRaidRewardFieldResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: raidRewardsWorkflowSchema
});

export const updateRaidRewardFieldsResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: raidRewardsWorkflowSchema
});

export const updateRaidBonusRewardFieldRequestSchema = z.strictObject({
  field: z.string(),
  paths: projectPathsSchema,
  session: editSessionSchema.nullable(),
  slot: z.number().int().positive(),
  tableId: z.string(),
  value: z.string()
});

export const updateRaidBonusRewardFieldsRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  session: editSessionSchema.nullable(),
  updates: z.array(raidRewardFieldUpdateSchema).min(1)
});

export const updateRaidBonusRewardFieldResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: raidRewardsWorkflowSchema
});

export const updateRaidBonusRewardFieldsResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: raidRewardsWorkflowSchema
});

export const updateBehaviorEntryFieldRequestSchema = z.strictObject({
  entryId: z.string(),
  field: z.string(),
  paths: projectPathsSchema,
  session: editSessionSchema.nullable(),
  value: z.string()
});

export const updateBehaviorEntryFieldResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: behaviorWorkflowSchema
});

export const updateBehaviorEntryFieldsRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  session: editSessionSchema.nullable(),
  updates: z
    .array(
      z.strictObject({
        entryId: z.string().min(1),
        field: z.string().min(1),
        value: z.string()
      })
    )
    .min(1)
});

export const updateBehaviorEntryFieldsResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: behaviorWorkflowSchema
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
  sourceFingerprint: z.string().nullable().optional(),
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

export const importRandomizerSeedResponseSchema = z.strictObject({
  config: randomizerConfigSchema.nullable(),
  diagnostics: z.array(apiDiagnosticSchema),
  seed: z.string().nullable()
});

export const applyRandomizerResponseSchema = z.strictObject({
  applyResult: applyResultSchema,
  seed: z.string()
});

export const restoreRandomizerResponseSchema = z.strictObject({
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
export type ChangePlanOutputMode = z.infer<typeof changePlanOutputModeSchema>;
export type CreateChangePlanRequest = z.infer<typeof createChangePlanRequestSchema>;
export type CreateChangePlanResponse = z.infer<typeof createChangePlanResponseSchema>;
export type EditSession = z.infer<typeof editSessionSchema>;
export type ItemEditableField = z.infer<typeof itemEditableFieldSchema>;
export type ItemRecord = z.infer<typeof itemRecordSchema>;
export type ItemsWorkflow = z.infer<typeof itemsWorkflowSchema>;
export type PokemonEditableField = z.infer<typeof pokemonEditableFieldSchema>;
export type PokemonEditableFieldOption = z.infer<typeof pokemonEditableFieldOptionSchema>;
export type PokemonDexEditor = z.infer<typeof pokemonDexEditorSchema>;
export type PokemonDexPlacement = z.infer<typeof pokemonDexPlacementSchema>;
export type PokemonEvolutionMethodOption = z.infer<
  typeof pokemonEvolutionMethodOptionSchema
>;
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
export type GiftPokemonEditableField = z.infer<typeof giftPokemonEditableFieldSchema>;
export type GiftPokemonEditableFieldOption = z.infer<
  typeof giftPokemonEditableFieldOptionSchema
>;
export type GiftPokemonRecord = z.infer<typeof giftPokemonRecordSchema>;
export type GiftPokemonWorkflow = z.infer<typeof giftPokemonWorkflowSchema>;
export type TradePokemonEditableField = z.infer<typeof tradePokemonEditableFieldSchema>;
export type TradePokemonEditableFieldOption = z.infer<
  typeof tradePokemonEditableFieldOptionSchema
>;
export type TradePokemonMoveRecord = z.infer<typeof tradePokemonMoveSchema>;
export type TradePokemonRecord = z.infer<typeof tradePokemonRecordSchema>;
export type TradePokemonWorkflow = z.infer<typeof tradePokemonWorkflowSchema>;
export type StaticEncounterEditableField = z.infer<typeof staticEncounterEditableFieldSchema>;
export type StaticEncounterEditableFieldOption = z.infer<
  typeof staticEncounterEditableFieldOptionSchema
>;
export type StaticEncounterMoveRecord = z.infer<typeof staticEncounterMoveSchema>;
export type StaticEncounterRecord = z.infer<typeof staticEncounterRecordSchema>;
export type StaticEncountersWorkflow = z.infer<typeof staticEncountersWorkflowSchema>;
export type RentalPokemonEditableField = z.infer<typeof rentalPokemonEditableFieldSchema>;
export type RentalPokemonEditableFieldOption = z.infer<typeof rentalPokemonEditableFieldOptionSchema>;
export type RentalPokemonMoveRecord = z.infer<typeof rentalPokemonMoveSchema>;
export type RentalPokemonRecord = z.infer<typeof rentalPokemonRecordSchema>;
export type RentalPokemonWorkflow = z.infer<typeof rentalPokemonWorkflowSchema>;
export type DynamaxAdventureEditableField = z.infer<typeof dynamaxAdventureEditableFieldSchema>;
export type DynamaxAdventureEditableFieldOption = z.infer<typeof dynamaxAdventureEditableFieldOptionSchema>;
export type DynamaxAdventureMoveRecord = z.infer<typeof dynamaxAdventureMoveSchema>;
export type DynamaxAdventurePokemonSnapshot = z.infer<typeof dynamaxAdventurePokemonSnapshotSchema>;
export type DynamaxAdventureRecord = z.infer<typeof dynamaxAdventureRecordSchema>;
export type DynamaxAdventuresWorkflow = z.infer<typeof dynamaxAdventuresWorkflowSchema>;
export type ShopEditableField = z.infer<typeof shopEditableFieldSchema>;
export type ShopEditableFieldOption = z.infer<typeof shopEditableFieldOptionSchema>;
export type ShopInventoryRecord = z.infer<typeof shopInventoryRecordSchema>;
export type ShopRecord = z.infer<typeof shopRecordSchema>;
export type ShopsWorkflow = z.infer<typeof shopsWorkflowSchema>;
export type EncounterEditableField = z.infer<typeof encounterEditableFieldSchema>;
export type EncounterSlotRecord = z.infer<typeof encounterSlotRecordSchema>;
export type EncounterTableRecord = z.infer<typeof encounterTableRecordSchema>;
export type EncountersWorkflow = z.infer<typeof encountersWorkflowSchema>;
export type RaidBattleEditableField = z.infer<typeof raidBattleEditableFieldSchema>;
export type RaidBattleEditableFieldOption = z.infer<typeof raidBattleEditableFieldOptionSchema>;
export type RaidBattleSlotRecord = z.infer<typeof raidBattleSlotRecordSchema>;
export type RaidBattleTableRecord = z.infer<typeof raidBattleTableRecordSchema>;
export type RaidBattlesWorkflow = z.infer<typeof raidBattlesWorkflowSchema>;
export type TeraRaidEditableField = z.infer<typeof teraRaidEditableFieldSchema>;
export type TeraRaidEditableFieldOption = z.infer<typeof teraRaidEditableFieldOptionSchema>;
export type TeraRaidMoveRecord = z.infer<typeof teraRaidMoveSchema>;
export type TeraRaidRecord = z.infer<typeof teraRaidRecordSchema>;
export type TeraRaidRewardItem = z.infer<typeof teraRaidRewardItemSchema>;
export type TeraRaidRewardTable = z.infer<typeof teraRaidRewardTableSchema>;
export type TeraRaidsWorkflow = z.infer<typeof teraRaidsWorkflowSchema>;
export type RaidRewardEditableField = z.infer<typeof raidRewardEditableFieldSchema>;
export type RaidRewardItemRecord = z.infer<typeof raidRewardItemRecordSchema>;
export type RaidRewardTableRecord = z.infer<typeof raidRewardTableRecordSchema>;
export type RaidRewardsWorkflow = z.infer<typeof raidRewardsWorkflowSchema>;
export type PlacedObjectRecord = z.infer<typeof placedObjectRecordSchema>;
export type PlacementEditableField = z.infer<typeof placementEditableFieldSchema>;
export type PlacementFieldValue = z.infer<typeof placementFieldValueSchema>;
export type PlacementWorkflow = z.infer<typeof placementWorkflowSchema>;
export type BehaviorEntryRecord = z.infer<typeof behaviorEntryRecordSchema>;
export type BehaviorField = z.infer<typeof behaviorFieldSchema>;
export type BehaviorFieldOption = z.infer<typeof behaviorFieldOptionSchema>;
export type BehaviorWorkflow = z.infer<typeof behaviorWorkflowSchema>;
export type FlagRecord = z.infer<typeof flagRecordSchema>;
export type SaveBlockRecord = z.infer<typeof saveBlockRecordSchema>;
export type SaveFileRecord = z.infer<typeof saveFileRecordSchema>;
export type FlagworkSaveWorkflow = z.infer<typeof flagworkSaveWorkflowSchema>;
export type ExeFsPatchCheckRecord = z.infer<typeof exeFsPatchCheckRecordSchema>;
export type ExeFsPatchRecord = z.infer<typeof exeFsPatchRecordSchema>;
export type ExeFsSegmentRecord = z.infer<typeof exeFsSegmentRecordSchema>;
export type ExeFsPatchWorkflow = z.infer<typeof exeFsPatchWorkflowSchema>;
export type BagHookSlotRecord = z.infer<typeof bagHookSlotRecordSchema>;
export type BagHookWorkflow = z.infer<typeof bagHookWorkflowSchema>;
export type CatchCapRecord = z.infer<typeof catchCapRecordSchema>;
export type CatchCapSelection = z.infer<typeof catchCapSelectionSchema>;
export type CatchCapWorkflow = z.infer<typeof catchCapWorkflowSchema>;
export type HyperTrainingSourceRecord = z.infer<typeof hyperTrainingSourceRecordSchema>;
export type HyperTrainingWorkflow = z.infer<typeof hyperTrainingWorkflowSchema>;
export type TypeChartCell = z.infer<typeof typeChartCellSchema>;
export type TypeChartSourceRecord = z.infer<typeof typeChartSourceRecordSchema>;
export type TypeChartTypeDefinition = z.infer<typeof typeChartTypeDefinitionSchema>;
export type TypeChartWorkflow = z.infer<typeof typeChartWorkflowSchema>;
export type IvScreenReservedRegion = z.infer<typeof ivScreenReservedRegionSchema>;
export type IvScreenWorkflow = z.infer<typeof ivScreenWorkflowSchema>;
export type RoyalCandyOutputRecord = z.infer<typeof royalCandyOutputRecordSchema>;
export type RoyalCandyWorkflowCheckRecord = z.infer<
  typeof royalCandyWorkflowCheckRecordSchema
>;
export type RoyalCandyLevelCapRecord = z.infer<typeof royalCandyLevelCapRecordSchema>;
export type RoyalCandyLevelCapSelection = z.infer<typeof royalCandyLevelCapSelectionSchema>;
export type RoyalCandyWorkflowRecord = z.infer<typeof royalCandyWorkflowRecordSchema>;
export type RoyalCandyWorkflow = z.infer<typeof royalCandyWorkflowSchema>;
export type StartingItemGrantRecord = z.infer<typeof startingItemGrantRecordSchema>;
export type StartingItemGrantSelection = z.infer<typeof startingItemGrantSelectionSchema>;
export type StartingItemOptionRecord = z.infer<typeof startingItemOptionRecordSchema>;
export type StartingItemsWorkflow = z.infer<typeof startingItemsWorkflowSchema>;
export type SpreadsheetImportProfileRecord = z.infer<
  typeof spreadsheetImportProfileRecordSchema
>;
export type SpreadsheetImportPreview = z.infer<typeof spreadsheetImportPreviewSchema>;
export type SpreadsheetImportWorkflow = z.infer<typeof spreadsheetImportWorkflowSchema>;
export type ModMergerConflictRecord = z.infer<typeof modMergerConflictRecordSchema>;
export type ModMergerConflictResolution = z.infer<
  typeof modMergerConflictResolutionSchema
>;
export type ModMergerFilePreviewRecord = z.infer<typeof modMergerFilePreviewRecordSchema>;
export type ModMergerFileRecord = z.infer<typeof modMergerFileRecordSchema>;
export type ModMergerMergeMode = z.infer<typeof modMergerMergeModeSchema>;
export type ModMergerPreview = z.infer<typeof modMergerPreviewSchema>;
export type ModMergerWorkflow = z.infer<typeof modMergerWorkflowSchema>;
export type SvModMergerFilePreviewRecord = z.infer<
  typeof svModMergerFilePreviewRecordSchema
>;
export type SvModMergerPreview = z.infer<typeof svModMergerPreviewSchema>;
export type SvModMergerSource = z.infer<typeof svModMergerSourceSchema>;
export type SvModMergerSourceRecord = z.infer<typeof svModMergerSourceRecordSchema>;
export type SvModMergerWorkflow = z.infer<typeof svModMergerWorkflowSchema>;
export type ZaModMergerFilePreviewRecord = z.infer<
  typeof zaModMergerFilePreviewRecordSchema
>;
export type ZaModMergerPreview = z.infer<typeof zaModMergerPreviewSchema>;
export type ZaModMergerSource = z.infer<typeof zaModMergerSourceSchema>;
export type ZaModMergerSourceRecord = z.infer<typeof zaModMergerSourceRecordSchema>;
export type ZaModMergerWorkflow = z.infer<typeof zaModMergerWorkflowSchema>;
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
export type SwapPokemonDexPlacementRequest = z.infer<
  typeof swapPokemonDexPlacementRequestSchema
>;
export type SwapPokemonDexPlacementResponse = z.infer<
  typeof swapPokemonDexPlacementResponseSchema
>;
export type LoadMovesWorkflowRequest = z.infer<typeof loadMovesWorkflowRequestSchema>;
export type LoadMovesWorkflowResponse = z.infer<typeof loadMovesWorkflowResponseSchema>;
export type UpdateMoveFieldRequest = z.infer<typeof updateMoveFieldRequestSchema>;
export type UpdateMoveFieldResponse = z.infer<typeof updateMoveFieldResponseSchema>;
export type TextWorkflowQuery = z.infer<typeof textWorkflowQuerySchema>;
export type LoadTextWorkflowRequest = z.infer<typeof loadTextWorkflowRequestSchema>;
export type LoadTextWorkflowResponse = z.infer<typeof loadTextWorkflowResponseSchema>;
export type LoadTrainersWorkflowRequest = z.infer<typeof loadTrainersWorkflowRequestSchema>;
export type LoadTrainersWorkflowResponse = z.infer<typeof loadTrainersWorkflowResponseSchema>;
export type LoadGiftPokemonWorkflowRequest = z.infer<
  typeof loadGiftPokemonWorkflowRequestSchema
>;
export type LoadGiftPokemonWorkflowResponse = z.infer<
  typeof loadGiftPokemonWorkflowResponseSchema
>;
export type LoadTradePokemonWorkflowRequest = z.infer<
  typeof loadTradePokemonWorkflowRequestSchema
>;
export type LoadTradePokemonWorkflowResponse = z.infer<
  typeof loadTradePokemonWorkflowResponseSchema
>;
export type LoadStaticEncountersWorkflowRequest = z.infer<
  typeof loadStaticEncountersWorkflowRequestSchema
>;
export type LoadStaticEncountersWorkflowResponse = z.infer<
  typeof loadStaticEncountersWorkflowResponseSchema
>;
export type LoadRentalPokemonWorkflowRequest = z.infer<
  typeof loadRentalPokemonWorkflowRequestSchema
>;
export type LoadRentalPokemonWorkflowResponse = z.infer<
  typeof loadRentalPokemonWorkflowResponseSchema
>;
export type LoadDynamaxAdventuresWorkflowRequest = z.infer<typeof loadDynamaxAdventuresWorkflowRequestSchema>;
export type LoadDynamaxAdventuresWorkflowResponse = z.infer<typeof loadDynamaxAdventuresWorkflowResponseSchema>;
export type PreviewDynamaxAdventureDefaultsRequest = z.infer<typeof previewDynamaxAdventureDefaultsRequestSchema>;
export type PreviewDynamaxAdventureDefaultsResponse = z.infer<typeof previewDynamaxAdventureDefaultsResponseSchema>;
export type StageDynamaxAdventureRepairRequest = z.infer<
  typeof stageDynamaxAdventureRepairRequestSchema
>;
export type StageDynamaxAdventureRepairResponse = z.infer<
  typeof stageDynamaxAdventureRepairResponseSchema
>;
export type StageDynamaxAdventureRestoreRequest = z.infer<
  typeof stageDynamaxAdventureRestoreRequestSchema
>;
export type StageDynamaxAdventureRestoreResponse = z.infer<
  typeof stageDynamaxAdventureRestoreResponseSchema
>;
export type LoadShopsWorkflowRequest = z.infer<typeof loadShopsWorkflowRequestSchema>;
export type LoadShopsWorkflowResponse = z.infer<typeof loadShopsWorkflowResponseSchema>;
export type LoadEncountersWorkflowRequest = z.infer<typeof loadEncountersWorkflowRequestSchema>;
export type LoadEncountersWorkflowResponse = z.infer<typeof loadEncountersWorkflowResponseSchema>;
export type LoadRaidBattlesWorkflowRequest = z.infer<
  typeof loadRaidBattlesWorkflowRequestSchema
>;
export type LoadRaidBattlesWorkflowResponse = z.infer<
  typeof loadRaidBattlesWorkflowResponseSchema
>;
export type LoadTeraRaidsWorkflowRequest = z.infer<
  typeof loadTeraRaidsWorkflowRequestSchema
>;
export type LoadTeraRaidsWorkflowResponse = z.infer<
  typeof loadTeraRaidsWorkflowResponseSchema
>;
export type LoadRaidRewardsWorkflowRequest = z.infer<typeof loadRaidRewardsWorkflowRequestSchema>;
export type LoadRaidRewardsWorkflowResponse = z.infer<typeof loadRaidRewardsWorkflowResponseSchema>;
export type LoadRaidBonusRewardsWorkflowRequest = z.infer<
  typeof loadRaidBonusRewardsWorkflowRequestSchema
>;
export type LoadRaidBonusRewardsWorkflowResponse = z.infer<
  typeof loadRaidBonusRewardsWorkflowResponseSchema
>;
export type UpdateRaidBattleSlotFieldRequest = z.infer<
  typeof updateRaidBattleSlotFieldRequestSchema
>;
export type UpdateRaidBattleSlotFieldResponse = z.infer<
  typeof updateRaidBattleSlotFieldResponseSchema
>;
export type RaidBattleFieldUpdate = z.infer<typeof raidBattleFieldUpdateSchema>;
export type UpdateRaidBattleSlotFieldsRequest = z.infer<
  typeof updateRaidBattleSlotFieldsRequestSchema
>;
export type UpdateRaidBattleSlotFieldsResponse = z.infer<
  typeof updateRaidBattleSlotFieldsResponseSchema
>;
export type TeraRaidFieldUpdate = z.infer<typeof teraRaidFieldUpdateSchema>;
export type UpdateTeraRaidFieldRequest = z.infer<
  typeof updateTeraRaidFieldRequestSchema
>;
export type UpdateTeraRaidFieldResponse = z.infer<
  typeof updateTeraRaidFieldResponseSchema
>;
export type UpdateTeraRaidFieldsRequest = z.infer<
  typeof updateTeraRaidFieldsRequestSchema
>;
export type UpdateTeraRaidFieldsResponse = z.infer<
  typeof updateTeraRaidFieldsResponseSchema
>;
export type UpdateRaidRewardFieldRequest = z.infer<typeof updateRaidRewardFieldRequestSchema>;
export type UpdateRaidRewardFieldResponse = z.infer<typeof updateRaidRewardFieldResponseSchema>;
export type RaidRewardFieldUpdate = z.infer<typeof raidRewardFieldUpdateSchema>;
export type UpdateRaidRewardFieldsRequest = z.infer<typeof updateRaidRewardFieldsRequestSchema>;
export type UpdateRaidRewardFieldsResponse = z.infer<typeof updateRaidRewardFieldsResponseSchema>;
export type UpdateRaidBonusRewardFieldRequest = z.infer<
  typeof updateRaidBonusRewardFieldRequestSchema
>;
export type UpdateRaidBonusRewardFieldResponse = z.infer<
  typeof updateRaidBonusRewardFieldResponseSchema
>;
export type UpdateRaidBonusRewardFieldsRequest = z.infer<
  typeof updateRaidBonusRewardFieldsRequestSchema
>;
export type UpdateRaidBonusRewardFieldsResponse = z.infer<
  typeof updateRaidBonusRewardFieldsResponseSchema
>;
export type LoadPlacementWorkflowRequest = z.infer<typeof loadPlacementWorkflowRequestSchema>;
export type LoadPlacementWorkflowResponse = z.infer<typeof loadPlacementWorkflowResponseSchema>;
export type LoadBehaviorWorkflowRequest = z.infer<typeof loadBehaviorWorkflowRequestSchema>;
export type LoadBehaviorWorkflowResponse = z.infer<typeof loadBehaviorWorkflowResponseSchema>;
export type UpdateBehaviorEntryFieldRequest = z.infer<
  typeof updateBehaviorEntryFieldRequestSchema
>;
export type UpdateBehaviorEntryFieldResponse = z.infer<
  typeof updateBehaviorEntryFieldResponseSchema
>;
export type UpdateBehaviorEntryFieldsRequest = z.infer<
  typeof updateBehaviorEntryFieldsRequestSchema
>;
export type UpdateBehaviorEntryFieldsResponse = z.infer<
  typeof updateBehaviorEntryFieldsResponseSchema
>;
export type LoadFlagworkSaveWorkflowRequest = z.infer<typeof loadFlagworkSaveWorkflowRequestSchema>;
export type LoadFlagworkSaveWorkflowResponse = z.infer<typeof loadFlagworkSaveWorkflowResponseSchema>;
export type LoadBagHookWorkflowRequest = z.infer<typeof loadBagHookWorkflowRequestSchema>;
export type LoadBagHookWorkflowResponse = z.infer<typeof loadBagHookWorkflowResponseSchema>;
export type StageBagHookInstallRequest = z.infer<typeof stageBagHookInstallRequestSchema>;
export type StageBagHookInstallResponse = z.infer<typeof stageBagHookInstallResponseSchema>;
export type StageBagHookUninstallRequest = z.infer<typeof stageBagHookUninstallRequestSchema>;
export type StageBagHookUninstallResponse = z.infer<typeof stageBagHookUninstallResponseSchema>;
export type LoadCatchCapWorkflowRequest = z.infer<typeof loadCatchCapWorkflowRequestSchema>;
export type LoadCatchCapWorkflowResponse = z.infer<typeof loadCatchCapWorkflowResponseSchema>;
export type StageCatchCapRequest = z.infer<typeof stageCatchCapRequestSchema>;
export type StageCatchCapResponse = z.infer<typeof stageCatchCapResponseSchema>;
export type StageCatchCapUninstallRequest = z.infer<typeof stageCatchCapUninstallRequestSchema>;
export type StageCatchCapUninstallResponse = z.infer<typeof stageCatchCapUninstallResponseSchema>;
export type LoadHyperTrainingWorkflowRequest = z.infer<
  typeof loadHyperTrainingWorkflowRequestSchema
>;
export type LoadHyperTrainingWorkflowResponse = z.infer<
  typeof loadHyperTrainingWorkflowResponseSchema
>;
export type StageHyperTrainingRequest = z.infer<typeof stageHyperTrainingRequestSchema>;
export type StageHyperTrainingResponse = z.infer<typeof stageHyperTrainingResponseSchema>;
export type LoadTypeChartWorkflowRequest = z.infer<
  typeof loadTypeChartWorkflowRequestSchema
>;
export type LoadTypeChartWorkflowResponse = z.infer<
  typeof loadTypeChartWorkflowResponseSchema
>;
export type StageTypeChartRequest = z.infer<typeof stageTypeChartRequestSchema>;
export type StageTypeChartResponse = z.infer<typeof stageTypeChartResponseSchema>;
export type StageTypeChartUninstallRequest = z.infer<typeof stageTypeChartUninstallRequestSchema>;
export type StageTypeChartUninstallResponse = z.infer<typeof stageTypeChartUninstallResponseSchema>;
export type LoadIvScreenWorkflowRequest = z.infer<typeof loadIvScreenWorkflowRequestSchema>;
export type LoadIvScreenWorkflowResponse = z.infer<typeof loadIvScreenWorkflowResponseSchema>;
export type StageIvScreenInstallRequest = z.infer<typeof stageIvScreenInstallRequestSchema>;
export type StageIvScreenInstallResponse = z.infer<typeof stageIvScreenInstallResponseSchema>;
export type StageIvScreenUninstallRequest = z.infer<
  typeof stageIvScreenUninstallRequestSchema
>;
export type StageIvScreenUninstallResponse = z.infer<
  typeof stageIvScreenUninstallResponseSchema
>;
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
export type LoadStartingItemsWorkflowRequest = z.infer<
  typeof loadStartingItemsWorkflowRequestSchema
>;
export type LoadStartingItemsWorkflowResponse = z.infer<
  typeof loadStartingItemsWorkflowResponseSchema
>;
export type StageStartingItemsRequest = z.infer<typeof stageStartingItemsRequestSchema>;
export type StageStartingItemsResponse = z.infer<typeof stageStartingItemsResponseSchema>;
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
export type LoadModMergerWorkflowRequest = z.infer<
  typeof loadModMergerWorkflowRequestSchema
>;
export type LoadModMergerWorkflowResponse = z.infer<
  typeof loadModMergerWorkflowResponseSchema
>;
export type StageModMergeRequest = z.infer<typeof stageModMergeRequestSchema>;
export type StageModMergeResponse = z.infer<typeof stageModMergeResponseSchema>;
export type ApplyModMergeRequest = z.infer<typeof applyModMergeRequestSchema>;
export type ApplyModMergeResponse = z.infer<typeof applyModMergeResponseSchema>;
export type LoadSvModMergerWorkflowRequest = z.infer<
  typeof loadSvModMergerWorkflowRequestSchema
>;
export type LoadSvModMergerWorkflowResponse = z.infer<
  typeof loadSvModMergerWorkflowResponseSchema
>;
export type StageSvModMergeRequest = z.infer<typeof stageSvModMergeRequestSchema>;
export type StageSvModMergeResponse = z.infer<typeof stageSvModMergeResponseSchema>;
export type ApplySvModMergeRequest = z.infer<typeof applySvModMergeRequestSchema>;
export type ApplySvModMergeResponse = z.infer<typeof applySvModMergeResponseSchema>;
export type LoadZaModMergerWorkflowRequest = z.infer<
  typeof loadZaModMergerWorkflowRequestSchema
>;
export type LoadZaModMergerWorkflowResponse = z.infer<
  typeof loadZaModMergerWorkflowResponseSchema
>;
export type StageZaModMergeRequest = z.infer<typeof stageZaModMergeRequestSchema>;
export type StageZaModMergeResponse = z.infer<typeof stageZaModMergeResponseSchema>;
export type ApplyZaModMergeRequest = z.infer<typeof applyZaModMergeRequestSchema>;
export type ApplyZaModMergeResponse = z.infer<typeof applyZaModMergeResponseSchema>;
export type RandomizerOptions = z.infer<typeof randomizerOptionsSchema>;
export type RandomizerConfig = z.infer<typeof randomizerConfigSchema>;
export type ImportRandomizerSeedRequest = z.infer<typeof importRandomizerSeedRequestSchema>;
export type ImportRandomizerSeedResponse = z.infer<typeof importRandomizerSeedResponseSchema>;
export type ApplyRandomizerRequest = z.infer<typeof applyRandomizerRequestSchema>;
export type ApplyRandomizerResponse = z.infer<typeof applyRandomizerResponseSchema>;
export type RestoreRandomizerRequest = z.infer<typeof restoreRandomizerRequestSchema>;
export type RestoreRandomizerResponse = z.infer<typeof restoreRandomizerResponseSchema>;
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
export type UpdateGiftPokemonFieldRequest = z.infer<
  typeof updateGiftPokemonFieldRequestSchema
>;
export type UpdateGiftPokemonFieldResponse = z.infer<
  typeof updateGiftPokemonFieldResponseSchema
>;
export type UpdateTradePokemonFieldRequest = z.infer<
  typeof updateTradePokemonFieldRequestSchema
>;
export type UpdateTradePokemonFieldResponse = z.infer<
  typeof updateTradePokemonFieldResponseSchema
>;
export type UpdateStaticEncounterFieldRequest = z.infer<
  typeof updateStaticEncounterFieldRequestSchema
>;
export type UpdateStaticEncounterFieldResponse = z.infer<
  typeof updateStaticEncounterFieldResponseSchema
>;
export type StaticEncounterFieldUpdate = z.infer<typeof staticEncounterFieldUpdateSchema>;
export type UpdateStaticEncounterFieldsRequest = z.infer<
  typeof updateStaticEncounterFieldsRequestSchema
>;
export type UpdateRentalPokemonFieldRequest = z.infer<
  typeof updateRentalPokemonFieldRequestSchema
>;
export type UpdateRentalPokemonFieldResponse = z.infer<
  typeof updateRentalPokemonFieldResponseSchema
>;
export type UpdateDynamaxAdventureFieldRequest = z.infer<typeof updateDynamaxAdventureFieldRequestSchema>;
export type UpdateDynamaxAdventureFieldResponse = z.infer<typeof updateDynamaxAdventureFieldResponseSchema>;
export type UpdateShopInventoryItemRequest = z.infer<
  typeof updateShopInventoryItemRequestSchema
>;
export type UpdateShopInventoryItemResponse = z.infer<
  typeof updateShopInventoryItemResponseSchema
>;
export type ValidateEditSessionRequest = z.infer<typeof validateEditSessionRequestSchema>;
export type ValidateEditSessionResponse = z.infer<typeof validateEditSessionResponseSchema>;
export type ValidateProjectRequest = z.infer<typeof validateProjectRequestSchema>;
export type ValidateProjectResponse = z.infer<typeof validateProjectResponseSchema>;
export type WorkflowSummary = z.infer<typeof workflowSummarySchema>;
