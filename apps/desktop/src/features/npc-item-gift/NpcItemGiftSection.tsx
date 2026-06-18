/* SPDX-License-Identifier: GPL-3.0-only */

import { ClipboardCheck, Gift, RotateCcw, Save, TriangleAlert } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { type EditSession } from '../../bridge/contracts';
import {
  type NpcItemGiftItemOptionRecord,
  type NpcItemGiftNpcGroup,
  type NpcItemGiftRecord,
  type NpcItemGiftSelection,
  type NpcItemGiftWorkflow
} from '../../bridge/npcItemGiftContracts';
import {
  Metric,
  WorkflowPanelOutputSections,
  type WorkflowPanelOutput
} from '../../components/workflowPanels';
import { formatFileState, formatSourceLayer } from '../../utils/workflowFormatters';

type NpcGiftDrafts = Record<string, NpcItemGiftSelection>;

export function NpcItemGiftSection({
  editSession,
  isChangePlanApplying,
  isChangePlanCreating,
  isStaging,
  onApplyChangePlan,
  onCreateChangePlan,
  onDirtyChange,
  onStageGifts,
  panelOutput,
  workflow
}: {
  editSession: EditSession | null;
  isChangePlanApplying: boolean;
  isChangePlanCreating: boolean;
  isStaging: boolean;
  onApplyChangePlan: () => void;
  onCreateChangePlan: () => void;
  onDirtyChange: (isDirty: boolean) => void;
  onStageGifts: (selections: NpcItemGiftSelection[]) => void;
  panelOutput: WorkflowPanelOutput;
  workflow: NpcItemGiftWorkflow | null;
}) {
  const sortedNpcs = useMemo(
    () =>
      workflow?.npcs
        .slice()
        .sort((left, right) => left.displayOrder - right.displayOrder) ?? [],
    [workflow?.npcs]
  );
  const workflowSelections = useMemo(() => getWorkflowSelections(workflow), [workflow]);
  const stagedNpcGiftEdit = editSession?.pendingEdits.find(
    (edit) => edit.domain === 'workflow.npcItemGift'
  );
  const stagedSelections = useMemo(
    () => decodeNpcItemGiftPendingSelections(stagedNpcGiftEdit?.newValue),
    [stagedNpcGiftEdit?.newValue]
  );
  const cleanSelections = useMemo(
    () => mergeSelections(workflowSelections, stagedSelections),
    [workflowSelections, stagedSelections]
  );
  const vanillaSelections = useMemo(() => getVanillaSelections(workflow), [workflow]);
  const cleanSelectionsKey = encodeSelectionsKey(cleanSelections);

  const [selectedNpcId, setSelectedNpcId] = useState<string | null>(null);
  const [drafts, setDrafts] = useState<NpcGiftDrafts>(() => createDrafts(cleanSelections));

  useEffect(() => {
    setDrafts(createDrafts(cleanSelections));
  }, [cleanSelectionsKey]);

  useEffect(() => {
    if (sortedNpcs.length === 0) {
      setSelectedNpcId(null);
      return;
    }

    setSelectedNpcId((current) =>
      current && sortedNpcs.some((npc) => npc.npcId === current)
        ? current
        : sortedNpcs[0].npcId
    );
  }, [sortedNpcs]);

  const selectedNpc =
    sortedNpcs.find((npc) => npc.npcId === selectedNpcId) ?? sortedNpcs[0] ?? null;
  const selectedDraftSelections = useMemo(
    () => getNpcDraftSelections(selectedNpc, drafts, cleanSelections),
    [selectedNpc, drafts, cleanSelections]
  );
  const selectedCleanSelections = useMemo(
    () => getNpcCleanSelections(selectedNpc, cleanSelections),
    [selectedNpc, cleanSelections]
  );
  const selectedVanillaSelections = useMemo(
    () => getNpcCleanSelections(selectedNpc, vanillaSelections),
    [selectedNpc, vanillaSelections]
  );
  const isDirty = !areSelectionsEqual(selectedDraftSelections, selectedCleanSelections);
  const hasStagedChange = stagedSelections !== null;
  const stagedNpcId = useMemo(
    () => getStagedNpcId(workflow, stagedSelections),
    [workflow, stagedSelections]
  );
  const canEdit =
    workflow?.summary.availability === 'available' &&
    workflow.sources.every((source) => source.status === 'available') &&
    !isStaging &&
    !isChangePlanApplying;
  const canStage = canEdit && selectedDraftSelections.length > 0 && isDirty && !hasStagedChange;
  const canRestoreVanilla =
    canEdit &&
    selectedDraftSelections.length > 0 &&
    !areSelectionsEqual(selectedDraftSelections, selectedVanillaSelections) &&
    !hasStagedChange;
  const canReviewPlan =
    hasStagedChange &&
    !isDirty &&
    stagedNpcId === selectedNpc?.npcId &&
    !isChangePlanCreating;
  const canApplyPlan =
    canReviewPlan &&
    panelOutput.changePlan !== null &&
    panelOutput.changePlan.canApply &&
    panelOutput.changePlan.writes.length > 0 &&
    !isChangePlanApplying;

  useEffect(() => {
    onDirtyChange(isDirty);
  }, [isDirty, onDirtyChange]);

  const selectNpc = (npc: NpcItemGiftNpcGroup) => {
    if (npc.npcId === selectedNpc?.npcId) {
      return;
    }

    if (hasStagedChange) {
      window.alert(
        'NPC Item Gift stages one NPC at a time. Review and apply the staged NPC before opening another NPC.'
      );
      return;
    }

    if (
      isDirty &&
      !window.confirm('Discard the un-staged NPC Item Gift edits and open another NPC?')
    ) {
      return;
    }

    setDrafts(createDrafts(cleanSelections));
    setSelectedNpcId(npc.npcId);
  };

  const updateQuantity = (giftId: string, value: string) => {
    const quantity = Math.max(1, Math.min(999, Number.parseInt(value, 10) || 1));
    setDrafts((current) => ({
      ...current,
      [giftId]: {
        ...(current[giftId] ?? cleanSelections[giftId]),
        quantity
      }
    }));
  };

  const updateItem = (giftId: string, slotId: string, itemId: number) => {
    setDrafts((current) => {
      const selection = current[giftId] ?? cleanSelections[giftId];
      if (!selection) {
        return current;
      }

      return {
        ...current,
        [giftId]: {
          ...selection,
          items: selection.items.map((item) =>
            item.slotId === slotId ? { ...item, itemId } : item
          )
        }
      };
    });
  };

  return (
    <>
      <section aria-labelledby="npc-item-gift-heading" className="panel wide-panel">
        <div className="panel-heading">
          <Gift aria-hidden="true" size={18} />
          <h2 id="npc-item-gift-heading">NPC Item Gift</h2>
        </div>

        <div className="items-toolbar exefs-toolbar">
          <Metric label="NPCs" value={workflow?.stats.npcCount.toString() ?? '0'} />
          <Metric label="Gift groups" value={workflow?.stats.giftCount.toString() ?? '0'} />
          <Metric label="Sources" value={workflow?.stats.sourceFileCount.toString() ?? '0'} />
          <Metric label="Items" value={workflow?.stats.itemOptionCount.toString() ?? '0'} />
          <Metric
            label="Staged"
            value={stagedNpcId ? getNpcName(workflow, stagedNpcId) : 'No'}
          />
        </div>

        <div className="npc-item-gift-warning" role="note">
          <TriangleAlert aria-hidden="true" size={18} />
          <span>
            Stage, review, and apply one NPC before switching to another. This keeps each
            AMX script patch scoped to the item cells for that NPC and preserves other mods
            in the same files.
          </span>
        </div>

        {workflow ? (
          <div className="npc-item-gift-editor">
            <div className="npc-item-gift-tabs" role="tablist">
              {sortedNpcs.map((npc) => {
                const isSelected = selectedNpc?.npcId === npc.npcId;
                const isStaged = stagedNpcId === npc.npcId;
                return (
                  <button
                    aria-selected={isSelected}
                    className={[
                      'npc-item-gift-tab',
                      isSelected ? 'is-selected' : '',
                      isStaged ? 'is-staged' : ''
                    ]
                      .filter(Boolean)
                      .join(' ')}
                    key={npc.npcId}
                    onClick={() => selectNpc(npc)}
                    role="tab"
                    type="button"
                  >
                    {npc.npcName}
                  </button>
                );
              })}
            </div>

            {selectedNpc ? (
              <div className="npc-item-gift-stack">
                {selectedNpc.gifts.map((gift) => (
                  <NpcItemGiftCard
                    disabled={!canEdit || hasStagedChange}
                    gift={gift}
                    itemOptions={workflow.itemOptions}
                    key={gift.giftId}
                    onItemChange={updateItem}
                    onQuantityChange={updateQuantity}
                    selection={drafts[gift.giftId] ?? cleanSelections[gift.giftId]}
                  />
                ))}
              </div>
            ) : (
              <p className="empty-copy">No NPC item gifts are loaded.</p>
            )}

            <div className="type-chart-actions npc-item-gift-actions">
              <button
                className="danger-button"
                disabled={!canRestoreVanilla}
                onClick={() =>
                  setDrafts((current) => ({
                    ...current,
                    ...createDrafts(selectedVanillaSelections)
                  }))
                }
                type="button"
              >
                <RotateCcw aria-hidden="true" size={16} />
                <span>Restore NPC Defaults</span>
              </button>
              <button
                className="primary-button"
                disabled={!canStage}
                onClick={() => onStageGifts(selectedDraftSelections)}
                type="button"
              >
                <Save aria-hidden="true" size={16} />
                <span>{isStaging ? 'Staging' : 'Stage NPC'}</span>
              </button>
              <button
                className="secondary-button"
                disabled={!canReviewPlan}
                onClick={onCreateChangePlan}
                type="button"
              >
                <ClipboardCheck aria-hidden="true" size={16} />
                <span>{isChangePlanCreating ? 'Reviewing' : 'Review'}</span>
              </button>
              <button
                className="primary-button"
                disabled={!canApplyPlan}
                onClick={onApplyChangePlan}
                type="button"
              >
                <Save aria-hidden="true" size={16} />
                <span>{isChangePlanApplying ? 'Applying' : 'Apply'}</span>
              </button>
            </div>

            <NpcItemGiftSourceSummary sources={workflow.sources} />
          </div>
        ) : (
          <p className="empty-copy">Open NPC Item Gift from Advanced Editors.</p>
        )}
      </section>

      <WorkflowPanelOutputSections
        output={panelOutput}
        workflowDiagnostics={workflow?.diagnostics ?? []}
      />
    </>
  );
}

function NpcItemGiftCard({
  disabled,
  gift,
  itemOptions,
  onItemChange,
  onQuantityChange,
  selection
}: {
  disabled: boolean;
  gift: NpcItemGiftRecord;
  itemOptions: NpcItemGiftItemOptionRecord[];
  onItemChange: (giftId: string, slotId: string, itemId: number) => void;
  onQuantityChange: (giftId: string, value: string) => void;
  selection: NpcItemGiftSelection | undefined;
}) {
  if (!selection) {
    return null;
  }

  return (
    <article className="npc-item-gift-card">
      <div className="npc-item-gift-card-heading">
        <div>
          <h3>{gift.label}</h3>
          <p>{gift.relativePath}</p>
        </div>
        <span>{gift.location}</span>
      </div>

      <div className="npc-item-gift-fields">
        {gift.items.map((item) => {
          const selectedItem = selection.items.find((entry) => entry.slotId === item.slotId);
          const selectedItemId = selectedItem?.itemId ?? item.itemId;
          return (
            <label className="npc-item-gift-field" key={`${gift.giftId}:${item.slotId}`}>
              <span>{gift.items.length > 1 ? item.label : 'Item'}</span>
              <select
                aria-label={`${gift.label} ${item.label}`}
                disabled={disabled}
                onChange={(event) =>
                  onItemChange(gift.giftId, item.slotId, Number.parseInt(event.target.value, 10))
                }
                value={selectedItemId}
              >
                {getSelectableItemOptions(itemOptions, selectedItemId, item.itemName).map(
                  (option) => (
                    <option
                      disabled={option.isUnavailable}
                      key={option.itemId}
                      value={option.itemId}
                    >
                      {option.name} #{option.itemId}
                    </option>
                  )
                )}
              </select>
            </label>
          );
        })}

        <label className="npc-item-gift-field is-amount">
          <span>Amount</span>
          <input
            aria-label={`${gift.label} amount`}
            disabled={disabled}
            max={999}
            min={1}
            onChange={(event) => onQuantityChange(gift.giftId, event.target.value)}
            type="number"
            value={selection.quantity}
          />
        </label>
      </div>

      <div className="npc-item-gift-defaults">
        <span>Default amount {gift.vanillaQuantity}</span>
        <span>
          Default item{gift.items.length === 1 ? '' : 's'}{' '}
          {gift.items.map((item) => item.vanillaItemName).join(', ')}
        </span>
      </div>
    </article>
  );
}

function NpcItemGiftSourceSummary({
  sources
}: {
  sources: NpcItemGiftWorkflow['sources'];
}) {
  return (
    <div className="npc-item-gift-source-grid">
      {sources.map((source) => (
        <dl className="npc-item-gift-source-summary" key={source.sourceId}>
          <div>
            <dt>{source.label}</dt>
            <dd>{source.relativePath}</dd>
          </div>
          <div>
            <dt>Layer</dt>
            <dd>{formatSourceLayer(source.provenance.sourceLayer)}</dd>
          </div>
          <div>
            <dt>File state</dt>
            <dd>{formatFileState(source.provenance.fileState)}</dd>
          </div>
        </dl>
      ))}
    </div>
  );
}

export function decodeNpcItemGiftPendingSelections(
  value: string | null | undefined
): NpcItemGiftSelection[] | null {
  if (!value) {
    return null;
  }

  const selections: NpcItemGiftSelection[] = [];
  for (const entry of value.split(';').filter(Boolean)) {
    const [giftId, quantityText, itemsText] = entry.split('|');
    const quantity = Number.parseInt(quantityText ?? '', 10);
    if (!giftId || !Number.isInteger(quantity) || quantity < 1 || !itemsText) {
      return null;
    }

    const items = itemsText.split(',').map((itemEntry) => {
      const [slotId, itemIdText] = itemEntry.split('=');
      const itemId = Number.parseInt(itemIdText ?? '', 10);
      return { slotId: slotId ?? '', itemId };
    });

    if (items.some((item) => !item.slotId || !Number.isInteger(item.itemId))) {
      return null;
    }

    selections.push({ giftId, quantity, items });
  }

  return selections;
}

export function formatNpcItemGiftPendingValue(value: string | null | undefined) {
  const selections = decodeNpcItemGiftPendingSelections(value);
  if (!selections || selections.length === 0) {
    return 'Unknown';
  }

  return `${selections.length} gift group${selections.length === 1 ? '' : 's'} staged`;
}

function getWorkflowSelections(workflow: NpcItemGiftWorkflow | null): NpcGiftDrafts {
  if (!workflow) {
    return {};
  }

  return createDrafts(
    workflow.npcs.flatMap((npc) =>
      npc.gifts.map((gift) => ({
        giftId: gift.giftId,
        quantity: gift.quantity,
        items: gift.items.map((item) => ({
          slotId: item.slotId,
          itemId: item.itemId
        }))
      }))
    )
  );
}

function getVanillaSelections(workflow: NpcItemGiftWorkflow | null): NpcGiftDrafts {
  if (!workflow) {
    return {};
  }

  return createDrafts(
    workflow.npcs.flatMap((npc) =>
      npc.gifts.map((gift) => ({
        giftId: gift.giftId,
        quantity: gift.vanillaQuantity,
        items: gift.items.map((item) => ({
          slotId: item.slotId,
          itemId: item.vanillaItemId
        }))
      }))
    )
  );
}

function createDrafts(selections: NpcItemGiftSelection[] | NpcGiftDrafts): NpcGiftDrafts {
  if (!Array.isArray(selections)) {
    return { ...selections };
  }

  return Object.fromEntries(selections.map((selection) => [selection.giftId, selection]));
}

function mergeSelections(
  workflowSelections: NpcGiftDrafts,
  stagedSelections: NpcItemGiftSelection[] | null
): NpcGiftDrafts {
  if (!stagedSelections) {
    return workflowSelections;
  }

  return {
    ...workflowSelections,
    ...createDrafts(stagedSelections)
  };
}

function getNpcDraftSelections(
  npc: NpcItemGiftNpcGroup | null,
  drafts: NpcGiftDrafts,
  fallback: NpcGiftDrafts
): NpcItemGiftSelection[] {
  if (!npc) {
    return [];
  }

  return npc.gifts
    .map((gift) => drafts[gift.giftId] ?? fallback[gift.giftId])
    .filter((selection): selection is NpcItemGiftSelection => Boolean(selection));
}

function getNpcCleanSelections(
  npc: NpcItemGiftNpcGroup | null,
  selections: NpcGiftDrafts
): NpcItemGiftSelection[] {
  if (!npc) {
    return [];
  }

  return npc.gifts
    .map((gift) => selections[gift.giftId])
    .filter((selection): selection is NpcItemGiftSelection => Boolean(selection));
}

function areSelectionsEqual(
  left: NpcItemGiftSelection[],
  right: NpcItemGiftSelection[]
) {
  return encodeSelectionsKey(createDrafts(left)) === encodeSelectionsKey(createDrafts(right));
}

function encodeSelectionsKey(selections: NpcGiftDrafts) {
  return Object.values(selections)
    .slice()
    .sort((left, right) => left.giftId.localeCompare(right.giftId))
    .map(
      (selection) =>
        `${selection.giftId}:${selection.quantity}:${selection.items
          .slice()
          .sort((left, right) => left.slotId.localeCompare(right.slotId))
          .map((item) => `${item.slotId}=${item.itemId}`)
          .join(',')}`
    )
    .join(';');
}

function getStagedNpcId(
  workflow: NpcItemGiftWorkflow | null,
  stagedSelections: NpcItemGiftSelection[] | null
) {
  if (!workflow || !stagedSelections || stagedSelections.length === 0) {
    return null;
  }

  const giftLookup = new Map<string, string>();
  for (const npc of workflow.npcs) {
    for (const gift of npc.gifts) {
      giftLookup.set(gift.giftId, npc.npcId);
    }
  }

  return giftLookup.get(stagedSelections[0].giftId) ?? null;
}

function getNpcName(workflow: NpcItemGiftWorkflow | null, npcId: string) {
  return workflow?.npcs.find((npc) => npc.npcId === npcId)?.npcName ?? 'Yes';
}

function getSelectableItemOptions(
  options: NpcItemGiftItemOptionRecord[],
  selectedItemId: number,
  selectedItemName: string
) {
  if (options.some((option) => option.itemId === selectedItemId)) {
    return options.map((option) => ({ ...option, isUnavailable: false }));
  }

  return [
    {
      category: 'Unavailable',
      isKeyItem: false,
      isUnavailable: true,
      itemId: selectedItemId,
      name: `${selectedItemName} unavailable`
    },
    ...options.map((option) => ({ ...option, isUnavailable: false }))
  ];
}
