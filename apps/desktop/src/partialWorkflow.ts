/* SPDX-License-Identifier: GPL-3.0-only */
import type { ApiDiagnostic, WorkflowSummary, BehaviorWorkflow, PlacementWorkflow, TeraRaidsWorkflow, GiftPokemonWorkflow, TradePokemonWorkflow, RentalPokemonWorkflow, StaticEncountersWorkflow, RaidBattlesWorkflow, PokemonWorkflow, ItemsWorkflow, MovesWorkflow, EditSession, EncountersWorkflow, TrainersWorkflow } from './bridge/contracts';
type PendingEdit = EditSession['pendingEdits'][number];

function mergeRows<T>(previous: readonly T[], incoming: readonly T[], key: (row: T) => number): T[] {
  const replacements = new Map(incoming.map(row => [key(row), row]));
  const existing = new Set(previous.map(key));
  return previous.map(row => replacements.get(key(row)) ?? row)
    .concat(incoming.filter(row => !existing.has(key(row))));
}

// Partial loaders replace diagnostics for the files they assessed. Keep warnings
// from explicitly skipped files until those files are loaded again.
function mergeDiagnostics(previous: { summary: WorkflowSummary; diagnostics: readonly ApiDiagnostic[] },
  incoming: { summary: WorkflowSummary; diagnostics: readonly ApiDiagnostic[] }) {
  const unassessed = new Set(incoming.summary.unassessedDiagnosticFiles ?? []);
  const merge = (before: readonly ApiDiagnostic[], after: readonly ApiDiagnostic[]) =>
    [...before.filter(diagnostic => diagnostic.file && unassessed.has(diagnostic.file)), ...after];
  return {
    diagnostics: merge(previous.diagnostics, incoming.diagnostics),
    summary: { ...incoming.summary, isPartial: previous.summary.isPartial,
      unassessedDiagnosticFiles: previous.summary.unassessedDiagnosticFiles,
      diagnostics: merge(previous.summary.diagnostics, incoming.summary.diagnostics) }
  };
}

export function mergeGiftWorkflow(previous: GiftPokemonWorkflow | null, incoming: GiftPokemonWorkflow): GiftPokemonWorkflow {
  if (!previous || !incoming.summary.isPartial) return incoming;
  const gifts = mergeRows(previous.gifts, incoming.gifts, row => row.giftIndex);
  return { ...incoming, ...mergeDiagnostics(previous, incoming), editableFields: previous.editableFields, gifts,
    stats: { ...previous.stats, totalGiftCount: gifts.length,
      eggGiftCount: gifts.filter(row => row.isEgg).length,
      fixedIvGiftCount: gifts.filter(row => row.ivSummary !== 'Random IVs').length } };
}

export function mergeTradeWorkflow(previous: TradePokemonWorkflow | null, incoming: TradePokemonWorkflow): TradePokemonWorkflow {
  if (!previous || !incoming.summary.isPartial) return incoming;
  const trades = mergeRows(previous.trades, incoming.trades, row => row.tradeIndex);
  return { ...incoming, ...mergeDiagnostics(previous, incoming), editableFields: previous.editableFields, trades,
    stats: { ...previous.stats, totalTradeCount: trades.length,
      fixedIvTradeCount: trades.filter(row => row.ivSummary !== 'Random IVs').length } };
}

export function mergeRentalWorkflow(previous: RentalPokemonWorkflow | null, incoming: RentalPokemonWorkflow): RentalPokemonWorkflow {
  if (!previous || !incoming.summary.isPartial) return incoming;
  const rentals = mergeRows(previous.rentals, incoming.rentals, row => row.rentalIndex);
  return { ...incoming, ...mergeDiagnostics(previous, incoming), editableFields: previous.editableFields, rentals,
    stats: { ...previous.stats, totalRentalCount: rentals.length,
      perfectIvRentalCount: rentals.filter(row => row.hasPerfectIvs).length } };
}

export function mergeStaticEncounterWorkflow(previous: StaticEncountersWorkflow | null, incoming: StaticEncountersWorkflow): StaticEncountersWorkflow {
  if (!previous || !incoming.summary.isPartial) return incoming;
  const encounters = mergeRows(previous.encounters, incoming.encounters, row => row.encounterIndex);
  return { ...incoming, ...mergeDiagnostics(previous, incoming), editableFields: previous.editableFields, encounters,
    stats: { ...previous.stats, totalEncounterCount: encounters.length,
      gigantamaxEncounterCount: encounters.filter(row => row.canGigantamax).length,
      fixedIvEncounterCount: encounters.filter(row => incoming.editableFields.some(field => field.field === 'ivMode')
        ? row.flawlessIvCount !== null && row.flawlessIvCount !== 0 : row.flawlessIvCount === null).length } };
}

// Scope a known record-only Apply. Structural and mixed-domain operations use
// the complete refresh, since they can alter records outside this selection.
export function appliedRecordIndices(edits: readonly PendingEdit[] | undefined, domain: string, prefix: string): number[] | undefined {
  if (!edits?.length || edits.some(edit => edit.domain !== domain)) return undefined;
  const indices = edits.map(edit => {
    const match = new RegExp(`^${prefix}:(0|[1-9][0-9]*)(?::[0-9a-fA-F]+)?$`, 'u').exec(edit.recordId ?? '');
    return match ? Number(match[1]) : -1;
  });
  return indices.every(index => Number.isSafeInteger(index) && index >= 0) ? [...new Set(indices)] : undefined;
}

export function mergePokemonWorkflow(previous: PokemonWorkflow | null, incoming: PokemonWorkflow): PokemonWorkflow {
  if (!previous || !incoming.summary.isPartial) return incoming;
  const pokemon = mergeRows(previous.pokemon, incoming.pokemon, row => row.personalId);
  return { ...incoming, ...mergeDiagnostics(previous, incoming), editableFields: previous.editableFields,
    dexEditor: previous.dexEditor, evolutionMethodOptions: previous.evolutionMethodOptions, learnsetMoveOptions: previous.learnsetMoveOptions, pokemon,
    stats: { ...previous.stats, totalPokemonCount: pokemon.length,
      presentPokemonCount: pokemon.filter(row => row.dexPresence.isPresentInGame).length,
      totalEvolutionCount: pokemon.reduce((sum, row) => sum + row.evolutions.length, 0),
      totalLearnsetMoveCount: pokemon.reduce((sum, row) => sum + row.learnset.length, 0) } };
}

export function mergeItemsWorkflow(previous: ItemsWorkflow | null, incoming: ItemsWorkflow): ItemsWorkflow {
  if (!previous || !incoming.summary.isPartial) return incoming;
  const items = mergeRows(previous.items, incoming.items, row => row.itemId);
  return { ...incoming, ...mergeDiagnostics(previous, incoming), editableFields: previous.editableFields, items,
    stats: { ...previous.stats, totalItemCount: items.length } };
}

export function mergeMovesWorkflow(previous: MovesWorkflow | null, incoming: MovesWorkflow): MovesWorkflow {
  if (!previous || !incoming.summary.isPartial) return incoming;
  const moves = mergeRows(previous.moves, incoming.moves, row => row.moveId);
  return { ...incoming, ...mergeDiagnostics(previous, incoming), editableFields: previous.editableFields, moves,
    stats: { ...previous.stats, totalMoveCount: moves.length,
      enabledMoveCount: moves.filter(row => row.canUseMove).length,
      activeFlagCount: moves.reduce((sum, row) => sum + row.flags.filter(flag => flag.enabled).length, 0) } };
}

export function appliedNumericRecordIds(edits: readonly PendingEdit[] | undefined, domain: string): number[] | undefined {
  if (!edits?.length || edits.some(edit => edit.domain !== domain || !/^(0|[1-9][0-9]*)$/u.test(edit.recordId ?? '')
    || ['form', 'formStatsIndex', 'formCount', 'isRegionalForm', 'isPresentInGame', 'regionalDexIndex', 'dexPlacement', 'modelId', 'alphaMove', 'alphaSize', 'evYieldAll', 'expYieldAll', 'materialDropCountAll', 'pocket', 'itemType', 'groupType', 'groupIndex'].includes(edit.field ?? ''))) return undefined;
  const ids = edits.map(edit => Number(edit.recordId));
  return ids.every(Number.isSafeInteger) ? [...new Set(ids)] : undefined;
}


export function mergeEncountersWorkflow(previous: EncountersWorkflow | null, incoming: EncountersWorkflow): EncountersWorkflow {
  if (!previous || !incoming.summary.isPartial) return incoming;
  const replacements = new Map(incoming.tables.map(row => [row.tableId, row]));
  const existing = new Set(previous.tables.map(row => row.tableId));
  const tables = previous.tables.map(row => replacements.get(row.tableId) ?? row)
    .concat(incoming.tables.filter(row => !existing.has(row.tableId)));
  return { ...incoming, ...mergeDiagnostics(previous, incoming), editableFields: previous.editableFields, tables,
    stats: { ...previous.stats, totalTableCount: tables.length, totalSlotCount: tables.reduce((sum, row) => sum + row.slots.length, 0) } };
}

export function appliedEncounterTableIds(edits: readonly PendingEdit[] | undefined): string[] | undefined {
  if (!edits?.length || edits.some(edit => edit.domain !== 'workflow.encounters' || !/^.+#(?:0|[1-9][0-9]*)$/u.test(edit.recordId ?? ''))) return undefined;
  return [...new Set(edits.map(edit => edit.recordId!.slice(0, edit.recordId!.lastIndexOf('#'))))];
}

export function appliedTrainerPartyIds(edits: readonly PendingEdit[] | undefined, writtenFiles: readonly string[] | undefined,
  workflow: TrainersWorkflow): number[] | undefined {
  if (!edits?.length || !writtenFiles?.length || edits.some(edit => edit.domain !== 'workflow.trainers'
    || !/^(0|[1-9][0-9]*):[0-9]+$/u.test(edit.recordId ?? ''))) return undefined;
  const ids = [...new Set(edits.map(edit => Number(edit.recordId!.split(':')[0])))];
  const sources = new Set(workflow.trainers.filter(row => ids.includes(row.trainerId)).map(row => row.provenance.teamSourceFile));
  return ids.every(Number.isSafeInteger) && writtenFiles.every(file => sources.has(file)) ? ids : undefined;
}

export function mergeRaidBattlesWorkflow(previous: RaidBattlesWorkflow | null, incoming: RaidBattlesWorkflow): RaidBattlesWorkflow {
  if (!previous || !incoming.summary.isPartial) return incoming;
  const replacements = new Map(incoming.tables.map(row => [row.tableId, row]));
  const existing = new Set(previous.tables.map(row => row.tableId));
  const tables = previous.tables.map(row => replacements.get(row.tableId) ?? row)
    .concat(incoming.tables.filter(row => !existing.has(row.tableId)));
  return { ...incoming, ...mergeDiagnostics(previous, incoming), editableFields: previous.editableFields, tables,
    stats: { ...previous.stats, totalTableCount: tables.length, totalSlotCount: tables.reduce((sum, row) => sum + row.slots.length, 0),
      gigantamaxSlotCount: tables.reduce((sum, row) => sum + row.slots.filter(slot => slot.isGigantamax).length, 0) } };
}

export function appliedRaidTableIds(edits: readonly PendingEdit[] | undefined): string[] | undefined {
  if (!edits?.length || edits.some(edit => edit.domain !== 'workflow.raidBattles' || !/^.+#[1-9][0-9]*$/u.test(edit.recordId ?? ''))) return undefined;
  return [...new Set(edits.map(edit => edit.recordId!.slice(0, edit.recordId!.lastIndexOf('#'))))];
}

export function mergeTeraRaidsWorkflow(previous: TeraRaidsWorkflow | null, incoming: TeraRaidsWorkflow): TeraRaidsWorkflow {
  if (!previous || !incoming.summary.isPartial) return incoming;
  const replacements = new Map(incoming.raids.map(row => [row.recordId, row]));
  const existing = new Set(previous.raids.map(row => row.recordId));
  const raids = previous.raids.map(row => replacements.get(row.recordId) ?? row)
    .concat(incoming.raids.filter(row => !existing.has(row.recordId)));
  return { ...incoming, ...mergeDiagnostics(previous, incoming), raids,
    stats: { ...previous.stats, totalRaidCount: raids.length } };
}

export function appliedTeraRaidRecordIds(edits: readonly PendingEdit[] | undefined): string[] | undefined {
  if (!edits?.length || edits.some(edit => edit.domain !== 'workflow.teraRaids'
    || !/^(?:raid:[^:]+:[0-9]+|(?:fixed|lottery):[0-9]+:[0-9]+)$/u.test(edit.recordId ?? ''))) return undefined;
  return [...new Set(edits.map(edit => edit.recordId!))];
}

export function appliedZaEncounterTableIds(edits: readonly PendingEdit[] | undefined, workflow: EncountersWorkflow): string[] | undefined {
  if (!edits?.length || edits.some(edit => edit.domain !== 'workflow.encounters')) return undefined;
  const selected = new Set<string>();
  for (const edit of edits) {
    const id = edit.recordId ?? '';
    const source = /^encount-data:([0-9]+)$/u.exec(id);
    if (source) {
      const consumers = workflow.tables.filter(table => table.slots.some(slot => slot.encounterRecordId === id));
      if (consumers.length === 0) return undefined;
      consumers.forEach(table => selected.add(table.tableId));
    } else {
      const tableId = id.slice(0, id.lastIndexOf('#'));
      if (!workflow.tables.some(table => table.tableId === tableId)) return undefined;
      selected.add(tableId);
    }
  }
  return selected.size ? [...selected] : undefined;
}

export function mergePlacementWorkflow(previous: PlacementWorkflow | null, incoming: PlacementWorkflow): PlacementWorkflow {
  if (!previous || !incoming.summary.isPartial) return incoming;
  const replacements = new Map(incoming.objects.map(row => [row.objectId, row]));
  const existing = new Set(previous.objects.map(row => row.objectId));
  const objects = previous.objects.map(row => replacements.get(row.objectId) ?? row)
    .concat(incoming.objects.filter(row => !existing.has(row.objectId)));
  return { ...incoming, ...mergeDiagnostics(previous, incoming), editableFields: previous.editableFields, objects,
    categories: previous.categories?.map(category => ({ ...category, objectCount: objects.filter(row => row.categoryId === category.id).length })),
    stats: { ...previous.stats, totalObjectCount: objects.length } };
}

export function appliedPlacementObjectIds(edits: readonly PendingEdit[] | undefined): string[] | undefined {
  if (!edits?.length || edits.some(edit => edit.domain !== 'workflow.placement' || !edit.recordId)) return undefined;
  return [...new Set(edits.map(edit => edit.recordId!))];
}

export function mergeBehaviorWorkflow(previous: BehaviorWorkflow | null, incoming: BehaviorWorkflow): BehaviorWorkflow {
  if (!previous || 'resources' in previous || 'resources' in incoming || !incoming.summary.isPartial) return incoming;
  const entries = mergeRows(previous.entries, incoming.entries, row => row.index);
  return { ...incoming, ...mergeDiagnostics(previous, incoming), fields: previous.fields, entries,
    stats: { ...previous.stats, totalEntryCount: entries.length } };
}
