/* SPDX-License-Identifier: GPL-3.0-only */

import { ChevronDown, ClipboardCheck, Gift, RotateCcw, Save, TriangleAlert } from 'lucide-react';
import { useEffect, useId, useMemo, useRef, useState } from 'react';
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
import { useLocalization } from '../../localization';
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
  'raihan'
];

const GYM_LEADER_NPCS = new Set(GYM_LEADER_ORDER);
const IMPORTANT_CHARACTER_NPCS = new Set([
  'sonia',
  'hop',
  'marnie',
  'mum',
  'ball-guy',
  'leon'
]);
const NPC_ITEM_GIFT_DOMAIN = 'workflow.npcItemGift';
const NPC_ITEM_GIFT_FIELD = 'gifts';
const NPC_ITEM_GIFT_RECORD_ID = 'npc-item-gift';

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
  const { translateLiteral } = useLocalization();
  const sortedNpcs = useMemo(() => {
    const npcs = workflow?.npcs.flatMap(splitNpcGroupForTabs) ?? [];
    return npcs.sort((left, right) => left.displayOrder - right.displayOrder);
  }, [workflow?.npcs]);
  const npcTabGroups = useMemo(() => groupNpcTabs(sortedNpcs), [sortedNpcs]);
  const orderedNpcs = useMemo(
    () => npcTabGroups.flatMap((group) => group.npcs),
    [npcTabGroups]
  );
  const npcItemGiftPendingEdits =
    editSession?.pendingEdits.filter((edit) => edit.domain === NPC_ITEM_GIFT_DOMAIN) ?? [];
  const stagedNpcGiftEdit =
    npcItemGiftPendingEdits.length === 1 &&
    npcItemGiftPendingEdits[0]?.recordId === NPC_ITEM_GIFT_RECORD_ID &&
    npcItemGiftPendingEdits[0]?.field === NPC_ITEM_GIFT_FIELD
      ? npcItemGiftPendingEdits[0]
      : null;
  const decodedStagedSelections = useMemo(
    () => decodeNpcItemGiftPendingSelections(stagedNpcGiftEdit?.newValue),
    [stagedNpcGiftEdit?.newValue]
  );
  const stagedNpcId = useMemo(
    () => getStagedNpcId(orderedNpcs, decodedStagedSelections),
    [decodedStagedSelections, orderedNpcs]
  );
  const hasStagedChange = npcItemGiftPendingEdits.length > 0;
  const hasInvalidStagedChange = hasStagedChange && stagedNpcId === null;
  const stagedSelections = hasInvalidStagedChange ? null : decodedStagedSelections;
  const workflowSelections = useMemo(() => getWorkflowSelections(workflow), [workflow]);
  const cleanSelections = useMemo(
    () => mergeSelections(workflowSelections, stagedSelections),
    [workflowSelections, stagedSelections]
  );
  const cleanSelectionsKey = encodeSelectionsKey(cleanSelections);
  const itemOptionLookup = useMemo(
    () => new Map((workflow?.itemOptions ?? []).map((option) => [option.itemId, option])),
    [workflow?.itemOptions]
  );

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

    setSelectedNpcId((current) => {
      if (stagedNpcId && orderedNpcs.some((npc) => npc.npcId === stagedNpcId)) {
        return stagedNpcId;
      }

      return current && orderedNpcs.some((npc) => npc.npcId === current)
        ? current
        : orderedNpcs[0].npcId;
    });
  }, [orderedNpcs, stagedNpcId]);

  const selectedNpc =
    orderedNpcs.find((npc) => npc.npcId === selectedNpcId) ?? orderedNpcs[0] ?? null;
  const selectedDraftSelections = useMemo(
    () => getNpcDraftSelections(selectedNpc, drafts, cleanSelections),
    [selectedNpc, drafts, cleanSelections]
  );
  const invalidQuantityGiftIds = useMemo(
    () =>
      new Set(
        Object.entries(quantityInputOverrides)
          .filter(
            ([giftId, value]) =>
              parseGiftQuantity(value) === null &&
              cleanSelections[giftId]?.quantity.toString() !== value
          )
          .map(([giftId]) => giftId)
      ),
    [cleanSelections, quantityInputOverrides]
  );
  const dirtyGiftIds = useMemo(
    () =>
      getDirtyGiftIds(
        selectedNpc,
        drafts,
        cleanSelections,
        quantityInputOverrides
      ),
    [cleanSelections, drafts, quantityInputOverrides, selectedNpc]
  );
  const isDirty = dirtyGiftIds.size > 0;
  const hasRepairableGift =
    selectedNpc?.gifts.some((gift) => gift.status === 'repairable') ?? false;
  const hasDirtyDrafts =
    haveDraftsChanged(drafts, cleanSelections) ||
    haveQuantityInputsChanged(quantityInputOverrides, Object.values(cleanSelections));
  const globalEditBlockers = useMemo(
    () => getNpcItemGiftGlobalBlockers(workflow),
    [workflow]
  );
  const giftEditBlockers = useMemo(
    () =>
      new Map(
        (selectedNpc?.gifts ?? []).map((gift) => [
          gift.giftId,
          getNpcItemGiftGiftBlockers(workflow, gift)
        ])
      ),
    [selectedNpc, workflow]
  );
  const dirtyGiftBlockers = [...dirtyGiftIds].flatMap(
    (giftId) => giftEditBlockers.get(giftId) ?? []
  );
  const canEditWorkflow =
    globalEditBlockers.length === 0 &&
    !isStaging &&
    !isChangePlanCreating &&
    !isChangePlanApplying;
  const canStage =
    canEditWorkflow &&
    dirtyGiftBlockers.length === 0 &&
    invalidQuantityGiftIds.size === 0 &&
    selectedDraftSelections.length > 0 &&
    (isDirty || hasRepairableGift) &&
    !hasStagedChange;
  const canReviewPlan =
    hasStagedChange &&
    !hasInvalidStagedChange &&
    !hasDirtyDrafts &&
    stagedNpcId === selectedNpc?.npcId &&
    !isStaging &&
    !isChangePlanCreating &&
    !isChangePlanApplying;
  const canApplyPlan =
    canReviewPlan &&
    panelOutput.changePlan !== null &&
    panelOutput.changePlan.canApply &&
    panelOutput.changePlan.writes.length > 0 &&
    !isStaging &&
    !isChangePlanCreating &&
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
        translateLiteral(
          'NPC Item Gift stages one NPC at a time. Review and apply the staged NPC before opening another NPC.'
        )
      );
      return;
    }

    const latestCleanSelections = cleanSelectionsRef.current;
    if (
      hasDirtyDrafts &&
      !window.confirm(
        translateLiteral('Discard the un-staged NPC Item Gift edits and open another NPC?')
      )
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

    const quantity = parseGiftQuantity(value);
    if (quantity === null) {
      return;
    }

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

    const quantity = parseGiftQuantity(override);
    if (quantity !== null) {
      updateDrafts((current) => ({
        ...current,
        [giftId]: {
          ...(current[giftId] ?? cleanSelectionsRef.current[giftId]),
          quantity
        }
      }));
      setQuantityInputOverrides((current) => {
        const { [giftId]: _, ...rest } = current;
        return rest;
      });
      return;
    }

    if (cleanSelectionsRef.current[giftId]?.quantity.toString() === override) {
      setQuantityInputOverrides((current) => {
        const { [giftId]: _, ...rest } = current;
        return rest;
      });
    }
  };

  const updateItem = (giftId: string, slotId: string, itemId: number) => {
    const canNormalizeKeyItemQuantity =
      selectedNpc?.gifts.find((gift) => gift.giftId === giftId)?.canEditQuantity === true;
    let selectedKnownKeyItem = false;
    updateDrafts((current) => {
      const selection = current[giftId] ?? cleanSelectionsRef.current[giftId];
      if (!selection) {
        return current;
      }

      const items = selection.items.map((item) =>
        item.slotId === slotId ? { ...item, itemId } : item
      );
      selectedKnownKeyItem =
        canNormalizeKeyItemQuantity &&
        items.some((item) => itemOptionLookup.get(item.itemId)?.isKeyItem === true);

      return {
        ...current,
        [giftId]: {
          ...selection,
          items,
          quantity: selectedKnownKeyItem ? 1 : selection.quantity
        }
      };
    });

    if (selectedKnownKeyItem) {
      setQuantityInputOverrides((current) => {
        const { [giftId]: _, ...rest } = current;
        return rest;
      });
    }
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
            value={stagedNpcId ? getNpcName(orderedNpcs, stagedNpcId) : 'No'}
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
            {hasInvalidStagedChange ? (
              <div className="npc-item-gift-blocker" role="alert">
                <TriangleAlert aria-hidden="true" size={18} />
                <span>
                  The staged NPC Item Gift entry is invalid. Discard it from Pending Changes
                  before editing or reviewing this workflow.
                </span>
              </div>
            ) : globalEditBlockers.length > 0 || dirtyGiftBlockers.length > 0 ? (
              <div className="npc-item-gift-blocker" role="status">
                <TriangleAlert aria-hidden="true" size={18} />
                <div>
                  <strong>
                    {globalEditBlockers.length > 0
                      ? 'This NPC cannot be edited yet.'
                      : 'Some changed gifts cannot be staged yet.'}
                  </strong>
                  <ul>
                    {[...globalEditBlockers, ...dirtyGiftBlockers].map((blocker) => (
                      <li key={blocker}>{blocker}</li>
                    ))}
                  </ul>
                </div>
              </div>
            ) : null}

            <div className="npc-item-gift-tab-groups">
              {npcTabGroups.map((group) => (
                <section className="npc-item-gift-tab-group" key={group.groupId}>
                  <h3>{group.label}</h3>
                  <div
                    aria-label={formatNpcGroupAriaLabel(group.label)}
                    className="npc-item-gift-tabs"
                    role="group"
                  >
                    {group.npcs.map((npc) => {
                      const isSelected = selectedNpc?.npcId === npc.npcId;
                      const isStaged = stagedNpcId === npc.npcId;
                      return (
                        <button
                          aria-pressed={isSelected}
                          className={[
                            'npc-item-gift-tab',
                            isSelected ? 'is-selected' : '',
                            isStaged ? 'is-staged' : ''
                          ]
                            .filter(Boolean)
                            .join(' ')}
                          key={npc.npcId}
                          onClick={() => selectNpc(npc)}
                          type="button"
                        >
                          <span>{npc.npcName}</span>
                          {isStaged ? (
                            <span className="npc-item-gift-staged-badge">Staged</span>
                          ) : null}
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
                    blockers={giftEditBlockers.get(gift.giftId) ?? []}
                    disabled={
                      !canEditWorkflow ||
                      hasStagedChange ||
                      (giftEditBlockers.get(gift.giftId)?.length ?? 0) > 0
                    }
                    gift={gift}
                    itemOptions={workflow.itemOptions}
                    key={gift.giftId}
                    onItemChange={updateItem}
                    onQuantityBlur={commitQuantity}
                    onQuantityChange={updateQuantity}
                    onRestoreDefault={restoreGiftDefault}
                    quantityError={invalidQuantityGiftIds.has(gift.giftId)}
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
                aria-busy={isStaging}
                className="primary-button"
                disabled={!canStage}
                onClick={() => onStageGifts(selectedDraftSelections)}
                type="button"
              >
                <Save aria-hidden="true" size={16} />
                <span>{isStaging ? 'Staging' : 'Stage NPC'}</span>
              </button>
              <button
                aria-busy={isChangePlanCreating}
                className="secondary-button"
                disabled={!canReviewPlan}
                onClick={onCreateChangePlan}
                type="button"
              >
                <ClipboardCheck aria-hidden="true" size={16} />
                <span>{isChangePlanCreating ? 'Reviewing' : 'Review'}</span>
              </button>
              <button
                aria-busy={isChangePlanApplying}
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
  blockers,
  disabled,
  gift,
  itemOptions,
  onItemChange,
  onQuantityBlur,
  onQuantityChange,
  onRestoreDefault,
  quantityError,
  quantityValue,
  selection
}: {
  blockers: string[];
  disabled: boolean;
  gift: NpcItemGiftRecord;
  itemOptions: NpcItemGiftItemOptionRecord[];
  onItemChange: (giftId: string, slotId: string, itemId: number) => void;
  onQuantityBlur: (giftId: string) => void;
  onQuantityChange: (giftId: string, value: string) => void;
  onRestoreDefault: (gift: NpcItemGiftRecord) => void;
  quantityError: boolean;
  quantityValue: string;
  selection: NpcItemGiftSelection | undefined;
}) {
  if (!selection) {
    return null;
  }

  const vanillaSelection = getGiftVanillaSelection(gift);
  const isDefault = areSelectionsEqual([selection], [vanillaSelection]);
  const quantityErrorId = `npc-item-gift-${gift.giftId}-quantity-error`;
  const hasKnownKeyItem = selection.items.some(
    (item) => itemOptions.find((option) => option.itemId === item.itemId)?.isKeyItem === true
  );

  return (
    <article className="npc-item-gift-card">
      <div className="npc-item-gift-card-heading">
        <div>
          <h3>{gift.label}</h3>
          <p>{gift.relativePath}</p>
        </div>
        <div className="npc-item-gift-card-badges">
          <span className={`npc-item-gift-status is-${gift.status}`}>
            {formatNpcItemGiftStatus(gift.status)}
          </span>
          <span className="npc-item-gift-location">{gift.location}</span>
        </div>
      </div>

      {blockers.length > 0 ? (
        <div className="npc-item-gift-card-blocker" role="note">
          <TriangleAlert aria-hidden="true" size={16} />
          <span>{blockers.join(' ')}</span>
        </div>
      ) : null}

      <div
        className={`npc-item-gift-fields has-${Math.min(gift.items.length, 3)}-items`}
      >
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
            aria-describedby={quantityError ? quantityErrorId : undefined}
            aria-invalid={quantityError}
            aria-label={`${gift.label} amount`}
            disabled={disabled || !gift.canEditQuantity || hasKnownKeyItem}
            inputMode="numeric"
            onBlur={() => onQuantityBlur(gift.giftId)}
            onChange={(event) => onQuantityChange(gift.giftId, event.target.value)}
            pattern="[0-9]*"
            type="text"
            value={quantityValue}
          />
          {!gift.canEditQuantity ? (
            <small>Fixed amount</small>
          ) : hasKnownKeyItem ? (
            <small>Key item amount</small>
          ) : quantityError ? (
            <small className="field-error" id={quantityErrorId}>
              Enter a whole number from 1 to 999.
            </small>
          ) : null}
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
          <span>Restore gift default</span>
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
  const { translateLiteral } = useLocalization();
  const containerRef = useRef<HTMLDivElement | null>(null);
  const pickerId = useId().replace(/:/g, '');
  const listboxId = `npc-item-gift-options-${pickerId}`;
  const [isOpen, setIsOpen] = useState(false);
  const [activeItemId, setActiveItemId] = useState<number | null>(null);
  const formattedValue = useMemo(
    () => translateLiteral(formatItemPickerValue(value, options)),
    [options, translateLiteral, value]
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
      setActiveItemId(null);
    }
  }, [formattedValue, isOpen]);

  useEffect(() => {
    if (disabled) {
      setIsOpen(false);
    }
  }, [disabled]);

  useEffect(() => {
    if (!isOpen || activeItemId === null) {
      return;
    }

    document
      .getElementById(`${listboxId}-item-${activeItemId}`)
      ?.scrollIntoView?.({ block: 'nearest' });
  }, [activeItemId, isOpen, listboxId]);

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
    setActiveItemId(null);
    setIsOpen(false);
  };

  const commitTypedOption = (allowActiveOption = false) => {
    if (!hasUserQuery) {
      setIsOpen(false);
      return;
    }

    const exactMatch = findExactItemOption(query, options);
    if (exactMatch) {
      selectOption(exactMatch);
      return;
    }

    const activeOption = allowActiveOption
      ? selectableOptions.find((option) => option.itemId === activeItemId)
      : null;
    if (activeOption) {
      selectOption(activeOption);
      return;
    }

    if (selectableOptions.length === 1) {
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
    setActiveItemId(null);
    setIsOpen(true);
  };

  const moveActiveOption = (direction: 1 | -1) => {
    if (selectableOptions.length === 0) {
      return;
    }

    const currentIndex = selectableOptions.findIndex(
      (option) => option.itemId === activeItemId
    );
    const nextIndex =
      currentIndex === -1
        ? direction === 1
          ? 0
          : selectableOptions.length - 1
        : (currentIndex + direction + selectableOptions.length) % selectableOptions.length;
    setActiveItemId(selectableOptions[nextIndex]!.itemId);
    setIsOpen(true);
  };

  return (
    <div
      className={`searchable-option-input ${disabled ? 'searchable-option-disabled' : ''}`}
      ref={containerRef}
    >
      <input
        aria-activedescendant={
          activeItemId === null ? undefined : `${listboxId}-item-${activeItemId}`
        }
        aria-autocomplete="list"
        aria-controls={hasMenu ? listboxId : undefined}
        aria-expanded={hasMenu}
        aria-haspopup="listbox"
        aria-label={ariaLabel}
        autoComplete="off"
        disabled={disabled}
        inputMode="search"
        onBlur={() => commitTypedOption(false)}
        onChange={(event) => handleInputChange(event.target.value)}
        onFocus={() => {
          setQuery(formattedValue);
          setHasUserQuery(false);
          setActiveItemId(null);
          setIsOpen(true);
        }}
        onKeyDown={(event) => {
          if (event.key === 'Escape') {
            setQuery(formattedValue);
            setHasUserQuery(false);
            setIsOpen(false);
            return;
          }

          if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            event.preventDefault();
            moveActiveOption(event.key === 'ArrowDown' ? 1 : -1);
            return;
          }

          if (event.key === 'Enter') {
            event.preventDefault();
            commitTypedOption(true);
          }
        }}
        role="combobox"
        title={formattedValue}
        type="text"
        value={query}
      />
      <button
        aria-label={`Show ${ariaLabel} options`}
        className="searchable-option-toggle"
        disabled={disabled}
        onClick={() => {
          setQuery(formattedValue);
          setHasUserQuery(false);
          setActiveItemId(null);
          setIsOpen((current) => !current);
        }}
        onMouseDown={(event) => {
          event.preventDefault();
        }}
        tabIndex={-1}
        type="button"
      >
        <ChevronDown aria-hidden="true" size={16} />
      </button>
      {hasMenu ? (
        <div className="searchable-option-menu" id={listboxId} role="listbox">
          {filteredOptions.map((option) => (
            <button
              aria-disabled={option.isUnavailable}
              aria-selected={value === option.itemId}
              className={`searchable-option-row ${
                activeItemId === option.itemId ? 'is-active' : ''
              }`}
              disabled={option.isUnavailable}
              id={`${listboxId}-item-${option.itemId}`}
              key={`${ariaLabel}:${option.itemId}`}
              onMouseEnter={() => {
                if (!option.isUnavailable) {
                  setActiveItemId(option.itemId);
                }
              }}
              onMouseDown={(event) => {
                event.preventDefault();
                selectOption(option);
              }}
              role="option"
              tabIndex={-1}
              type="button"
            >
              <span>{option.label}</span>
              <small>{option.category}</small>
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
            <th scope="col">Status</th>
            <th scope="col">Layer</th>
            <th scope="col">File state</th>
          </tr>
        </thead>
        <tbody>
          {sources.map((source) => (
            <tr key={source.sourceId}>
              <th scope="row">{source.label}</th>
              <td>{source.relativePath}</td>
              <td>{formatNpcItemGiftStatus(source.status)}</td>
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
  if (!value || value.trim() !== value) {
    return null;
  }

  const entries = value.split(';');
  if (entries.length === 0 || entries.some((entry) => entry.length === 0)) {
    return null;
  }

  const selections: NpcItemGiftSelection[] = [];
  const giftIds = new Set<string>();
  for (const entry of entries) {
    const parts = entry.split('|');
    if (parts.length !== 3) {
      return null;
    }

    const [giftId, quantityText, itemsText] = parts as [string, string, string];
    const quantity = parseCanonicalSignedInteger(quantityText);
    if (
      !isNpcItemGiftIdentifier(giftId) ||
      giftIds.has(giftId) ||
      quantity === null ||
      itemsText.length === 0
    ) {
      return null;
    }

    giftIds.add(giftId);
    const itemEntries = itemsText.split(',');
    if (itemEntries.some((itemEntry) => itemEntry.length === 0)) {
      return null;
    }

    const slotIds = new Set<string>();
    const items: NpcItemGiftSelection['items'] = [];
    for (const itemEntry of itemEntries) {
      const itemParts = itemEntry.split('=');
      if (itemParts.length !== 2) {
        return null;
      }

      const [slotId, itemIdText] = itemParts as [string, string];
      const itemId = parseCanonicalSignedInteger(itemIdText);
      if (!isNpcItemGiftIdentifier(slotId) || slotIds.has(slotId) || itemId === null) {
        return null;
      }

      slotIds.add(slotId);
      items.push({ slotId, itemId });
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

function parseGiftQuantity(value: string) {
  return parseCanonicalPositiveInteger(value, 999);
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
  const baseNpcId = getBaseNpcId(npc.npcId);
  return getGiftTabGroupId(baseNpcId, npc.gifts[0]);
}

function formatNpcGroupAriaLabel(label: string) {
  return /\bNPCs?$/i.test(label) ? label : `${label} NPCs`;
}

function splitNpcGroupForTabs(npc: NpcItemGiftNpcGroup): NpcItemGiftNpcGroup[] {
  const giftsByGroup = new Map<string, NpcItemGiftRecord[]>();
  for (const gift of npc.gifts) {
    const groupId = getGiftTabGroupId(npc.npcId, gift);
    giftsByGroup.set(groupId, [...(giftsByGroup.get(groupId) ?? []), gift]);
  }

  if (giftsByGroup.size <= 1) {
    return [npc];
  }

  return [...giftsByGroup].map(([groupId, gifts]) => ({
    ...npc,
    displayOrder: Math.min(...gifts.map((gift) => gift.displayOrder)),
    gifts,
    npcId: `${npc.npcId}::${groupId}`
  }));
}

function getGiftTabGroupId(npcId: string, gift: NpcItemGiftRecord | undefined) {
  if (GYM_LEADER_NPCS.has(npcId)) {
    return 'gym-leaders';
  }

  if (IMPORTANT_CHARACTER_NPCS.has(npcId)) {
    return 'important-characters';
  }

  if (gift && isIsleOfArmorPath(gift.relativePath)) {
    return 'isle-of-armor';
  }

  if (gift && isCrownTundraPath(gift.relativePath)) {
    return 'crown-tundra';
  }

  if (gift && isMainGamePath(gift.relativePath)) {
    return 'main-game';
  }

  return 'other';
}

function getBaseNpcId(npcId: string) {
  return npcId.split('::', 1)[0] ?? npcId;
}

function compareNpcTabs(left: NpcItemGiftNpcGroup, right: NpcItemGiftNpcGroup) {
  let leftGymOrder = GYM_LEADER_ORDER.indexOf(getBaseNpcId(left.npcId));
  let rightGymOrder = GYM_LEADER_ORDER.indexOf(getBaseNpcId(right.npcId));
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

function areSelectionsEqual(
  left: NpcItemGiftSelection[],
  right: NpcItemGiftSelection[]
) {
  return encodeSelectionsKey(createDrafts(left)) === encodeSelectionsKey(createDrafts(right));
}

function haveDraftsChanged(drafts: NpcGiftDrafts, cleanSelections: NpcGiftDrafts) {
  return encodeSelectionsKey(drafts) !== encodeSelectionsKey(cleanSelections);
}

function haveQuantityInputsChanged(
  quantityInputs: Record<string, string>,
  cleanSelections: NpcItemGiftSelection[]
) {
  const cleanQuantities = new Map(
    cleanSelections.map((selection) => [selection.giftId, selection.quantity.toString()])
  );

  return Object.entries(quantityInputs).some(
    ([giftId, value]) => cleanQuantities.get(giftId) !== value
  );
}

function getDirtyGiftIds(
  npc: NpcItemGiftNpcGroup | null,
  drafts: NpcGiftDrafts,
  cleanSelections: NpcGiftDrafts,
  quantityInputs: Record<string, string>
) {
  const dirtyGiftIds = new Set<string>();
  for (const gift of npc?.gifts ?? []) {
    const draft = drafts[gift.giftId];
    const clean = cleanSelections[gift.giftId];
    if (
      !draft ||
      !clean ||
      !areSelectionsEqual([draft], [clean]) ||
      (quantityInputs[gift.giftId] !== undefined &&
        quantityInputs[gift.giftId] !== clean.quantity.toString())
    ) {
      dirtyGiftIds.add(gift.giftId);
    }
  }

  return dirtyGiftIds;
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
  npcs: NpcItemGiftNpcGroup[],
  stagedSelections: NpcItemGiftSelection[] | null
) {
  if (!stagedSelections || stagedSelections.length === 0) {
    return null;
  }

  const giftLookup = new Map<string, { gift: NpcItemGiftRecord; npcId: string }>();
  for (const npc of npcs) {
    for (const gift of npc.gifts) {
      giftLookup.set(gift.giftId, { gift, npcId: npc.npcId });
    }
  }

  let stagedNpcId: string | null = null;
  for (const selection of stagedSelections) {
    const match = giftLookup.get(selection.giftId);
    if (!match || (stagedNpcId !== null && match.npcId !== stagedNpcId)) {
      return null;
    }

    const expectedSlotIds = match.gift.items.map((item) => item.slotId);
    if (
      selection.items.length !== expectedSlotIds.length ||
      selection.items.some((item, index) => item.slotId !== expectedSlotIds[index])
    ) {
      return null;
    }

    stagedNpcId = match.npcId;
  }

  const stagedNpc = npcs.find((npc) => npc.npcId === stagedNpcId);
  if (!stagedNpc) {
    return null;
  }

  const stagedGiftIds = new Set(stagedSelections.map((selection) => selection.giftId));
  const expectedGiftIds = stagedNpc.gifts
    .map((gift) => gift.giftId)
    .filter((giftId) => stagedGiftIds.has(giftId));
  return stagedSelections.every(
    (selection, index) => selection.giftId === expectedGiftIds[index]
  )
    ? stagedNpcId
    : null;
}

function getNpcName(npcs: NpcItemGiftNpcGroup[], npcId: string) {
  return npcs.find((npc) => npc.npcId === npcId)?.npcName ?? 'Yes';
}

function getNpcItemGiftGlobalBlockers(workflow: NpcItemGiftWorkflow | null) {
  if (!workflow) {
    return ['No NPC item gifts are loaded.'];
  }

  const blockers: string[] = [];
  if (workflow.summary.availability !== 'available') {
    blockers.push('NPC Item Gift apply requires valid base paths and a valid output root.');
  }

  return blockers;
}

function getNpcItemGiftGiftBlockers(
  workflow: NpcItemGiftWorkflow | null,
  gift: NpcItemGiftRecord
) {
  if (!workflow) {
    return ['No NPC item gifts are loaded.'];
  }

  const blockers: string[] = [];
  if (!isEditableNpcItemGiftStatus(gift.status)) {
    blockers.push(`${gift.label} is ${formatNpcItemGiftStatus(gift.status).toLowerCase()}.`);
  }

  const source = workflow.sources.find(
    (candidate) =>
      normalizeNpcItemGiftPath(candidate.relativePath) ===
      normalizeNpcItemGiftPath(gift.relativePath)
  );
  if (!source) {
    blockers.push(`${gift.label} does not have a loaded source record.`);
  } else if (source.status === 'missing') {
    blockers.push(
      `${source.label} is ${formatNpcItemGiftStatus(source.status).toLowerCase()}.`
    );
  }

  return [...new Set(blockers)];
}

function normalizeNpcItemGiftPath(path: string) {
  return path.replace(/\\/g, '/').toLocaleLowerCase();
}

function isEditableNpcItemGiftStatus(status: NpcItemGiftRecord['status']) {
  return status === 'available' || status === 'repairable';
}

function formatNpcItemGiftStatus(status: NpcItemGiftRecord['status']) {
  return status.charAt(0).toLocaleUpperCase() + status.slice(1);
}

function isNpcItemGiftIdentifier(value: string) {
  return /^[A-Za-z0-9][A-Za-z0-9._-]*$/.test(value);
}

function parseCanonicalPositiveInteger(value: string, maximum: number) {
  if (!/^[1-9]\d*$/.test(value)) {
    return null;
  }

  const parsed = Number(value);
  return Number.isSafeInteger(parsed) && parsed <= maximum ? parsed : null;
}

function parseCanonicalSignedInteger(value: string) {
  if (!/^(?:0|-?[1-9]\d*)$/.test(value)) {
    return null;
  }

  const parsed = Number(value);
  return Number.isSafeInteger(parsed) && parsed >= -2_147_483_648 && parsed <= 2_147_483_647
    ? parsed
    : null;
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
    label: `${option.name} (#${option.itemId})${option.isKeyItem ? ' [Key]' : ''}`
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

  const availableOptions = options.filter((option) => !option.isUnavailable);
  const canonicalMatch = availableOptions.find(
    (option) =>
      option.label.toLocaleLowerCase() === normalizedValue ||
      option.itemId.toString() === normalizedValue
  );
  if (canonicalMatch) {
    return canonicalMatch;
  }

  const nameMatches = availableOptions.filter(
    (option) => option.name.toLocaleLowerCase() === normalizedValue
  );
  return nameMatches.length === 1 ? nameMatches[0]! : null;
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
