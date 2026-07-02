/* SPDX-License-Identifier: GPL-3.0-only */

import { ChevronDown, ClipboardCheck, Gift, RotateCcw, Save, TriangleAlert } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
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

type NpcItemGiftSelectableItemOption = NpcItemGiftItemOptionRecord & {
  isUnavailable: boolean;
  label: string;
};

type NpcItemGiftTabGroup = {
  groupId: string;
  label: string;
  npcs: NpcItemGiftNpcGroup[];
};

const NPC_TAB_GROUPS = [
  { groupId: 'gym-leaders', label: 'Gym Leaders' },
  { groupId: 'important-characters', label: 'Important Characters' },
  { groupId: 'main-game', label: 'Main Game NPCs' },
  { groupId: 'isle-of-armor', label: 'Isle of Armor' },
  { groupId: 'crown-tundra', label: 'Crown Tundra' },
  { groupId: 'other', label: 'Other' }
] as const;

const GYM_LEADER_ORDER = [
  'milo',
  'nessa',
  'kabu',
  'bea',
  'allister',
  'opal',
  'gordie',
  'melony',
  'piers',
  'raihan',
  'leon'
];

const GYM_LEADER_NPCS = new Set(GYM_LEADER_ORDER);
const IMPORTANT_CHARACTER_NPCS = new Set(['sonia', 'hop', 'marnie', 'mum', 'ball-guy']);

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
  const npcTabGroups = useMemo(() => groupNpcTabs(sortedNpcs), [sortedNpcs]);
  const orderedNpcs = useMemo(
    () => npcTabGroups.flatMap((group) => group.npcs),
    [npcTabGroups]
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
  const cleanSelectionsKey = encodeSelectionsKey(cleanSelections);

  const [selectedNpcId, setSelectedNpcId] = useState<string | null>(null);
  const [drafts, setDrafts] = useState<NpcGiftDrafts>(() => createDrafts(cleanSelections));
  const [quantityInputOverrides, setQuantityInputOverrides] = useState<Record<string, string>>(
    {}
  );
  const draftsRef = useRef(drafts);
  const cleanSelectionsRef = useRef(cleanSelections);

  cleanSelectionsRef.current = cleanSelections;

  const replaceDrafts = (nextDrafts: NpcGiftDrafts) => {
    draftsRef.current = nextDrafts;
    setQuantityInputOverrides({});
    setDrafts(nextDrafts);
  };

  const updateDrafts = (updater: (current: NpcGiftDrafts) => NpcGiftDrafts) => {
    const nextDrafts = updater(draftsRef.current);
    draftsRef.current = nextDrafts;
    setDrafts(nextDrafts);
  };

  useEffect(() => {
    replaceDrafts(createDrafts(cleanSelections));
  }, [cleanSelectionsKey]);

  useEffect(() => {
    if (orderedNpcs.length === 0) {
      setSelectedNpcId(null);
      return;
    }

    setSelectedNpcId((current) =>
      current && orderedNpcs.some((npc) => npc.npcId === current)
        ? current
        : orderedNpcs[0].npcId
    );
  }, [orderedNpcs]);

  const selectedNpc =
    orderedNpcs.find((npc) => npc.npcId === selectedNpcId) ?? orderedNpcs[0] ?? null;
  const selectedDraftSelections = useMemo(
    () => getNpcDraftSelections(selectedNpc, drafts, cleanSelections),
    [selectedNpc, drafts, cleanSelections]
  );
  const selectedCleanSelections = useMemo(
    () => getNpcCleanSelections(selectedNpc, cleanSelections),
    [selectedNpc, cleanSelections]
  );
  const isDirty = !areSelectionsEqual(selectedDraftSelections, selectedCleanSelections);
  const hasDirtyDrafts = haveDraftsChanged(drafts, cleanSelections);
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
  const canReviewPlan =
    hasStagedChange &&
    !hasDirtyDrafts &&
    stagedNpcId === selectedNpc?.npcId &&
    !isChangePlanCreating;
  const canApplyPlan =
    canReviewPlan &&
    panelOutput.changePlan !== null &&
    panelOutput.changePlan.canApply &&
    panelOutput.changePlan.writes.length > 0 &&
    !isChangePlanApplying;

  useEffect(() => {
    onDirtyChange(hasDirtyDrafts);
  }, [hasDirtyDrafts, onDirtyChange]);

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

    const latestCleanSelections = cleanSelectionsRef.current;
    if (
      haveDraftsChanged(draftsRef.current, latestCleanSelections) &&
      !window.confirm('Discard the un-staged NPC Item Gift edits and open another NPC?')
    ) {
      return;
    }

    replaceDrafts(createDrafts(latestCleanSelections));
    setSelectedNpcId(npc.npcId);
  };

  const updateQuantity = (giftId: string, value: string) => {
    setQuantityInputOverrides((current) => ({
      ...current,
      [giftId]: value
    }));

    const parsedQuantity = Number.parseInt(value, 10);
    if (!Number.isInteger(parsedQuantity)) {
      return;
    }

    const quantity = clampGiftQuantity(parsedQuantity);
    updateDrafts((current) => ({
      ...current,
      [giftId]: {
        ...(current[giftId] ?? cleanSelectionsRef.current[giftId]),
        quantity
      }
    }));
  };

  const commitQuantity = (giftId: string) => {
    const override = quantityInputOverrides[giftId];
    if (override === undefined) {
      return;
    }

    const parsedQuantity = Number.parseInt(override, 10);
    if (Number.isInteger(parsedQuantity)) {
      const quantity = clampGiftQuantity(parsedQuantity);
      updateDrafts((current) => ({
        ...current,
        [giftId]: {
          ...(current[giftId] ?? cleanSelectionsRef.current[giftId]),
          quantity
        }
      }));
    }

    setQuantityInputOverrides((current) => {
      const { [giftId]: _, ...rest } = current;
      return rest;
    });
  };

  const updateItem = (giftId: string, slotId: string, itemId: number) => {
    updateDrafts((current) => {
      const selection = current[giftId] ?? cleanSelectionsRef.current[giftId];
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

  const restoreGiftDefault = (gift: NpcItemGiftRecord) => {
    setQuantityInputOverrides((current) => {
      const { [gift.giftId]: _, ...rest } = current;
      return rest;
    });
    updateDrafts((current) => ({
      ...current,
      [gift.giftId]: getGiftVanillaSelection(gift)
    }));
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
            <div className="npc-item-gift-tab-groups">
              {npcTabGroups.map((group) => (
                <section className="npc-item-gift-tab-group" key={group.groupId}>
                  <h3>{group.label}</h3>
                  <div
                    aria-label={`${group.label} NPCs`}
                    className="npc-item-gift-tabs"
                    role="tablist"
                  >
                    {group.npcs.map((npc) => {
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
                </section>
              ))}
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
                    onQuantityBlur={commitQuantity}
                    onQuantityChange={updateQuantity}
                    onRestoreDefault={restoreGiftDefault}
                    quantityValue={
                      quantityInputOverrides[gift.giftId] ?? (
                        drafts[gift.giftId] ?? cleanSelections[gift.giftId]
                      )?.quantity.toString() ?? ''
                    }
                    selection={drafts[gift.giftId] ?? cleanSelections[gift.giftId]}
                  />
                ))}
              </div>
            ) : (
              <p className="empty-copy">No NPC item gifts are loaded.</p>
            )}

            <div className="type-chart-actions npc-item-gift-actions">
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
        scrollAfterEntries={6}
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
  onQuantityBlur,
  onQuantityChange,
  onRestoreDefault,
  quantityValue,
  selection
}: {
  disabled: boolean;
  gift: NpcItemGiftRecord;
  itemOptions: NpcItemGiftItemOptionRecord[];
  onItemChange: (giftId: string, slotId: string, itemId: number) => void;
  onQuantityBlur: (giftId: string) => void;
  onQuantityChange: (giftId: string, value: string) => void;
  onRestoreDefault: (gift: NpcItemGiftRecord) => void;
  quantityValue: string;
  selection: NpcItemGiftSelection | undefined;
}) {
  if (!selection) {
    return null;
  }

  const vanillaSelection = getGiftVanillaSelection(gift);
  const isDefault = areSelectionsEqual([selection], [vanillaSelection]);

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
              <NpcItemGiftItemPicker
                aria-label={`${gift.label} ${item.label}`}
                disabled={disabled}
                onChange={(itemId) => onItemChange(gift.giftId, item.slotId, itemId)}
                options={getSelectableItemOptions(itemOptions, selectedItemId, item.itemName)}
                value={selectedItemId}
              />
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
            onBlur={() => onQuantityBlur(gift.giftId)}
            onChange={(event) => onQuantityChange(gift.giftId, event.target.value)}
            type="number"
            value={quantityValue}
          />
        </label>
      </div>

      <div className="npc-item-gift-defaults">
        <p>
          Default: {gift.vanillaQuantity} x{' '}
          {gift.items.map((item) => item.vanillaItemName).join(', ')}
        </p>
        <button
          className="secondary-button npc-item-gift-restore-button"
          disabled={disabled || isDefault}
          onClick={() => onRestoreDefault(gift)}
          type="button"
        >
          <RotateCcw aria-hidden="true" size={16} />
          <span>Restore NPC default</span>
        </button>
      </div>
    </article>
  );
}

function NpcItemGiftItemPicker({
  'aria-label': ariaLabel,
  disabled,
  onChange,
  options,
  value
}: {
  'aria-label': string;
  disabled: boolean;
  onChange: (value: number) => void;
  options: NpcItemGiftSelectableItemOption[];
  value: number;
}) {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const [isOpen, setIsOpen] = useState(false);
  const formattedValue = useMemo(
    () => formatItemPickerValue(value, options),
    [options, value]
  );
  const [query, setQuery] = useState(formattedValue);
  const [hasUserQuery, setHasUserQuery] = useState(false);
  const filteredOptions = useMemo(
    () => getSmartItemMatches(hasUserQuery ? query : '', options),
    [hasUserQuery, options, query]
  );
  const selectableOptions = filteredOptions.filter((option) => !option.isUnavailable);
  const hasMenu = isOpen && !disabled && filteredOptions.length > 0;

  useEffect(() => {
    if (!isOpen) {
      setQuery(formattedValue);
      setHasUserQuery(false);
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

  const selectOption = (option: NpcItemGiftSelectableItemOption) => {
    if (option.isUnavailable) {
      return;
    }

    onChange(option.itemId);
    setQuery(option.label);
    setHasUserQuery(false);
    setIsOpen(false);
  };

  const commitTypedOption = () => {
    const exactMatch = findExactItemOption(query, options);
    if (exactMatch) {
      selectOption(exactMatch);
      return;
    }

    if (hasUserQuery && selectableOptions.length === 1) {
      selectOption(selectableOptions[0]!);
      return;
    }

    setQuery(formattedValue);
    setHasUserQuery(false);
    setIsOpen(false);
  };

  const handleInputChange = (nextValue: string) => {
    setQuery(nextValue);
    setHasUserQuery(true);
    setIsOpen(true);
    const exactMatch = findExactItemOption(nextValue, options);
    if (exactMatch) {
      onChange(exactMatch.itemId);
    }
  };

  return (
    <div
      className={`searchable-option-input ${disabled ? 'searchable-option-disabled' : ''}`}
      ref={containerRef}
    >
      <input
        aria-expanded={hasMenu}
        aria-haspopup="listbox"
        aria-label={ariaLabel}
        autoComplete="off"
        disabled={disabled}
        inputMode="search"
        onBlur={commitTypedOption}
        onChange={(event) => handleInputChange(event.target.value)}
        onFocus={() => {
          setQuery(formattedValue);
          setHasUserQuery(false);
          setIsOpen(true);
        }}
        onKeyDown={(event) => {
          if (event.key === 'Escape') {
            setIsOpen(false);
            return;
          }

          if (event.key === 'Enter' && selectableOptions.length > 0) {
            event.preventDefault();
            selectOption(selectableOptions[0]!);
          }
        }}
        title={formattedValue}
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
          setHasUserQuery(false);
          setIsOpen((current) => (current && !hasUserQuery ? false : true));
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
              aria-disabled={option.isUnavailable}
              className="searchable-option-row"
              disabled={option.isUnavailable}
              key={`${ariaLabel}:${option.itemId}`}
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

function NpcItemGiftSourceSummary({
  sources
}: {
  sources: NpcItemGiftWorkflow['sources'];
}) {
  return (
    <div className="npc-item-gift-source-table-wrap">
      <table className="npc-item-gift-source-table">
        <thead>
          <tr>
            <th scope="col">Source</th>
            <th scope="col">Path</th>
            <th scope="col">Layer</th>
            <th scope="col">File state</th>
          </tr>
        </thead>
        <tbody>
          {sources.map((source) => (
            <tr key={source.sourceId}>
              <th scope="row">{source.label}</th>
              <td>{source.relativePath}</td>
              <td>{formatSourceLayer(source.provenance.sourceLayer)}</td>
              <td>{formatFileState(source.provenance.fileState)}</td>
            </tr>
          ))}
        </tbody>
      </table>
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

function getGiftVanillaSelection(gift: NpcItemGiftRecord): NpcItemGiftSelection {
  return {
    giftId: gift.giftId,
    quantity: gift.vanillaQuantity,
    items: gift.items.map((item) => ({
      slotId: item.slotId,
      itemId: item.vanillaItemId
    }))
  };
}

function clampGiftQuantity(quantity: number) {
  return Math.max(1, Math.min(999, quantity));
}

function groupNpcTabs(npcs: NpcItemGiftNpcGroup[]): NpcItemGiftTabGroup[] {
  const groups = new Map<string, NpcItemGiftNpcGroup[]>(
    NPC_TAB_GROUPS.map((group) => [group.groupId, [] as NpcItemGiftNpcGroup[]])
  );

  for (const npc of npcs) {
    groups.get(getNpcTabGroupId(npc))?.push(npc);
  }

  return NPC_TAB_GROUPS.map((group) => ({
    ...group,
    npcs: (groups.get(group.groupId) ?? []).slice().sort(compareNpcTabs)
  })).filter((group) => group.npcs.length > 0);
}

function getNpcTabGroupId(npc: NpcItemGiftNpcGroup) {
  if (GYM_LEADER_NPCS.has(npc.npcId)) {
    return 'gym-leaders';
  }

  if (IMPORTANT_CHARACTER_NPCS.has(npc.npcId)) {
    return 'important-characters';
  }

  if (npc.gifts.some((gift) => isIsleOfArmorPath(gift.relativePath))) {
    return 'isle-of-armor';
  }

  if (npc.gifts.some((gift) => isCrownTundraPath(gift.relativePath))) {
    return 'crown-tundra';
  }

  if (npc.gifts.every((gift) => isMainGamePath(gift.relativePath))) {
    return 'main-game';
  }

  return 'other';
}

function compareNpcTabs(left: NpcItemGiftNpcGroup, right: NpcItemGiftNpcGroup) {
  let leftGymOrder = GYM_LEADER_ORDER.indexOf(left.npcId);
  let rightGymOrder = GYM_LEADER_ORDER.indexOf(right.npcId);
  if (leftGymOrder !== -1 || rightGymOrder !== -1) {
    leftGymOrder = leftGymOrder === -1 ? Number.MAX_SAFE_INTEGER : leftGymOrder;
    rightGymOrder = rightGymOrder === -1 ? Number.MAX_SAFE_INTEGER : rightGymOrder;
    if (leftGymOrder !== rightGymOrder) {
      return leftGymOrder - rightGymOrder;
    }
  }

  return (
    getNpcFirstDisplayOrder(left) - getNpcFirstDisplayOrder(right) ||
    left.npcName.localeCompare(right.npcName)
  );
}

function getNpcFirstDisplayOrder(npc: NpcItemGiftNpcGroup) {
  return Math.min(npc.displayOrder, ...npc.gifts.map((gift) => gift.displayOrder));
}

function isMainGamePath(relativePath: string) {
  return !isIsleOfArmorPath(relativePath) && !isCrownTundraPath(relativePath);
}

function isIsleOfArmorPath(relativePath: string) {
  return relativePath.includes('/rigel01_') || relativePath.includes('/rigel1_');
}

function isCrownTundraPath(relativePath: string) {
  return relativePath.includes('/rigel02_') || relativePath.includes('/rigel2_');
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

function haveDraftsChanged(drafts: NpcGiftDrafts, cleanSelections: NpcGiftDrafts) {
  return encodeSelectionsKey(drafts) !== encodeSelectionsKey(cleanSelections);
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
): NpcItemGiftSelectableItemOption[] {
  const mappedOptions = options.map((option) => toSelectableItemOption(option, false));
  if (options.some((option) => option.itemId === selectedItemId)) {
    return mappedOptions;
  }

  return [
    toSelectableItemOption(
      {
        category: 'Unavailable',
        isKeyItem: false,
        itemId: selectedItemId,
        name: `${selectedItemName} unavailable`
      },
      true
    ),
    ...mappedOptions
  ];
}

function toSelectableItemOption(
  option: NpcItemGiftItemOptionRecord,
  isUnavailable: boolean
): NpcItemGiftSelectableItemOption {
  return {
    ...option,
    isUnavailable,
    label: `${option.name} #${option.itemId}`
  };
}

function findExactItemOption(
  value: string,
  options: NpcItemGiftSelectableItemOption[]
) {
  const normalizedValue = value.trim().toLocaleLowerCase();
  if (normalizedValue.length === 0) {
    return null;
  }

  return (
    options.find(
      (option) =>
        !option.isUnavailable &&
        (option.label.toLocaleLowerCase() === normalizedValue ||
          option.name.toLocaleLowerCase() === normalizedValue ||
          option.itemId.toString() === normalizedValue)
    ) ?? null
  );
}

function formatItemPickerValue(
  value: number,
  options: NpcItemGiftSelectableItemOption[]
) {
  return options.find((option) => option.itemId === value)?.label ?? `Item ${value}`;
}

function getSmartItemMatches(
  value: string,
  options: NpcItemGiftSelectableItemOption[]
) {
  const query = value.trim();
  if (query.length === 0) {
    return options.slice(0, 100);
  }

  const normalizedQuery = query.toLocaleLowerCase();
  const numericPrefix = normalizedQuery.match(/^\d+/)?.[0] ?? null;
  if (numericPrefix) {
    const normalizedNumericPrefix = numericPrefix.replace(/^0+/, '') || '0';
    return options
      .filter((option) => {
        const rawValue = option.itemId.toString();
        const normalizedValue = rawValue.replace(/^0+/, '') || '0';
        const labelNumericPrefix =
          option.label.match(/^\s*\$?\s*0*([\d,]+)/)?.[1]?.replace(/,/g, '') ?? null;

        return (
          rawValue.startsWith(numericPrefix) ||
          normalizedValue.startsWith(normalizedNumericPrefix) ||
          labelNumericPrefix?.startsWith(normalizedNumericPrefix)
        );
      })
      .slice(0, 100);
  }

  const tokens = normalizedQuery
    .split(/[^a-z0-9]+/)
    .filter((token) => token.length > 0);
  if (tokens.length === 0) {
    return options
      .filter((option) => option.label.toLocaleLowerCase().startsWith(normalizedQuery))
      .slice(0, 100);
  }

  return options
    .filter((option) => {
      const optionTokens = option.label.toLocaleLowerCase().split(/[^a-z0-9]+/);
      return tokens.every((token) =>
        optionTokens.some((optionToken) => optionToken.startsWith(token))
      );
    })
    .slice(0, 100);
}
