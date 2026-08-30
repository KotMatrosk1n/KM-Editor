/* SPDX-License-Identifier: GPL-3.0-only */

import { CheckCircle2, ClipboardCheck, Coffee, RotateCcw, Save, Search, TriangleAlert } from 'lucide-react';
import { useEffect, useId, useMemo, useRef, useState } from 'react';
import { type EditSession } from '../../bridge/contracts';
import {
  type BattleCafeRewardItemOption,
  type BattleCafeRewardRow,
  type BattleCafeRewardRowEdit,
  type BattleCafeRewardsWorkflow
} from '../../bridge/battleCafeRewardsContracts';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import {
  WorkflowPanelOutputSections,
  type WorkflowPanelOutput
} from '../../components/workflowPanels';
import { reconcileSourceBackedDraft } from '../../components/localEditorDraftState';
import {
  filterAndRankSearchableOptions,
  findExactSearchableOption,
  parseBoundedWholeNumberDraft
} from '../gameplayInputDrafts';
import { useLocalization } from '../../localization';
import { formatFileState, formatSourceLayer } from '../../utils/workflowFormatters';

const battleCafeRewardsDomain = 'workflow.battleCafeRewards';
const battleCafeRewardsRecordId = 'battle-cafe-rewards';
const battleCafeRewardsField = 'rows';
const maximumSuggestions = 20;

type OwnerKey = 'dwightPercent' | 'bernardPercent' | 'richardPercent';

type DraftRow = Pick<BattleCafeRewardRow, 'rowIndex' | 'itemId'> &
  Record<OwnerKey, string>;

export function BattleCafeRewardsSection({
  editSession,
  isChangePlanApplying,
  isChangePlanCreating,
  isStaging,
  onApplyChangePlan,
  onCreateChangePlan,
  onDirtyChange,
  onStageRows,
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
  onStageRows: (
    rows: BattleCafeRewardRowEdit[],
    isSubmittedDraftCurrent: () => boolean
  ) => void;
  panelOutput: WorkflowPanelOutput;
  workflow: BattleCafeRewardsWorkflow | null;
}) {
  const { t, translateLiteral } = useLocalization();
  const pendingEdits = editSession?.pendingEdits.filter(
    (edit) => edit.domain === battleCafeRewardsDomain
  ) ?? [];
  const stagedEdit = pendingEdits.length === 1 &&
    pendingEdits[0]?.recordId === battleCafeRewardsRecordId &&
    pendingEdits[0]?.field === battleCafeRewardsField
    ? pendingEdits[0]
    : null;
  const decodedStagedRows = useMemo(
    () => decodeStagedRows(
      stagedEdit?.newValue,
      workflow?.rewards ?? [],
      workflow?.itemOptions ?? []
    ),
    [stagedEdit?.newValue, workflow?.itemOptions, workflow?.rewards]
  );
  const hasStagedChange = pendingEdits.length > 0;
  const hasInvalidStagedChange = hasStagedChange && decodedStagedRows === null;
  const cleanRows = useMemo(
    () => mergeRows(workflow?.rewards ?? [], decodedStagedRows ?? []),
    [decodedStagedRows, workflow?.rewards]
  );
  const cleanRowsKey = useMemo(() => encodeDraftRows(cleanRows), [cleanRows]);
  const draftIdentityKey = [
    workflow?.provenance?.sourceFile ?? 'none',
    cleanRows.map((row) => row.rowIndex).join(',')
  ].join('|');
  const [draftRows, setDraftRows] = useState<DraftRow[]>(cleanRows);
  const draftContextSignature = JSON.stringify([
    draftIdentityKey,
    cleanRowsKey,
    encodeDraftRows(draftRows)
  ]);
  const draftContextSignatureRef = useRef(draftContextSignature);
  draftContextSignatureRef.current = draftContextSignature;
  const draftContextIsActiveRef = useRef(true);
  const cleanRowsRef = useRef(cleanRows);
  const sourceRowsRef = useRef({
    identityKey: draftIdentityKey,
    rows: cleanRows
  });
  cleanRowsRef.current = cleanRows;

  useEffect(() => {
    draftContextIsActiveRef.current = true;
    return () => {
      draftContextIsActiveRef.current = false;
    };
  }, []);

  useEffect(() => {
    const previous = sourceRowsRef.current;
    setDraftRows((current) =>
      previous.identityKey !== draftIdentityKey
        ? cleanRows
        : reconcileSourceBackedDraft(
            current,
            previous.rows,
            cleanRows,
            (left, right) => encodeDraftRows(left) === encodeDraftRows(right)
          )
    );
    sourceRowsRef.current = { identityKey: draftIdentityKey, rows: cleanRows };
  // cleanRowsKey is the stable source trigger; cleanRows is derived each render.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [cleanRowsKey, draftIdentityKey]);

  const dirtyRowIndexes = useMemo(() => {
    const cleanByIndex = new Map(cleanRows.map((row) => [row.rowIndex, row]));
    return new Set(
      draftRows
        .filter((row) => !sameDraftRow(row, cleanByIndex.get(row.rowIndex)))
        .map((row) => row.rowIndex)
    );
  }, [cleanRows, draftRows]);
  const totals = useMemo(() => calculateTotals(draftRows), [draftRows]);
  const totalsAreExact = ownerKeys.every((owner) => totals[owner] === 100);
  const percentagesAreValid = draftRows.every((row) =>
    ownerKeys.every((owner) => parsePercentDraft(row[owner]) !== null)
  );
  const itemChoicesAreUnique = new Set(draftRows.map((row) => row.itemId)).size === draftRows.length;
  const hasDirtyDraft = dirtyRowIndexes.size > 0;
  const isBusy = isStaging || isChangePlanCreating || isChangePlanApplying;
  const canEdit = workflow?.summary.availability === 'available';
  const canStage =
    canEdit &&
    !hasInvalidStagedChange &&
    !isBusy &&
    hasDirtyDraft &&
    percentagesAreValid;
  const canReview = hasStagedChange && !hasInvalidStagedChange && !hasDirtyDraft &&
    totalsAreExact && itemChoicesAreUnique && !isBusy;
  const canApply = canReview && panelOutput.changePlan !== null &&
    panelOutput.changePlan.canApply && panelOutput.changePlan.writes.length > 0;
  usePublishCommonEditorError({
    domain: battleCafeRewardsDomain,
    field: 'percentages',
    message: workflow === null || percentagesAreValid
      ? null
      : translateLiteral('Every reward percentage must be a whole number from 0 through 100.')
  });
  usePublishCommonEditorError({
    domain: battleCafeRewardsDomain,
    field: 'totals',
    message: workflow === null || totalsAreExact ? null : t('battleCafeRewards.validation.totals')
  });
  usePublishCommonEditorError({
    domain: battleCafeRewardsDomain,
    field: 'items',
    message: workflow === null || itemChoicesAreUnique
      ? null
      : t('battleCafeRewards.validation.uniqueItems')
  });
  usePublishCommonEditorError({
    domain: battleCafeRewardsDomain,
    field: battleCafeRewardsField,
    message: hasInvalidStagedChange
      ? t('battleCafeRewards.validation.invalidStaged')
      : null
  });

  useEffect(() => {
    onDirtyChange(hasDirtyDraft);
  }, [hasDirtyDraft, onDirtyChange]);

  const updateRow = (rowIndex: number, patch: Partial<DraftRow>) => {
    setDraftRows((current) => current.map((row) =>
      row.rowIndex === rowIndex ? { ...row, ...patch } : row
    ));
  };

  const restoreRow = (rowIndex: number) => {
    const clean = cleanRowsRef.current.find((row) => row.rowIndex === rowIndex);
    if (clean) {
      updateRow(rowIndex, clean);
    }
  };

  const stageRows = () => {
    if (!workflow) {
      return;
    }

    const sourceByIndex = new Map(workflow.rewards.map((row) => [row.rowIndex, row]));
    const rows = draftRows
      .filter((row) => dirtyRowIndexes.has(row.rowIndex))
      .map((row): BattleCafeRewardRowEdit | null => {
        const source = sourceByIndex.get(row.rowIndex);
        const parsed = parseDraftRow(row);
        return source && parsed ? {
          rowIndex: row.rowIndex,
          expectedItemId: source.itemId,
          expectedDwightPercent: source.dwightPercent,
          expectedBernardPercent: source.bernardPercent,
          expectedRichardPercent: source.richardPercent,
          itemId: parsed.itemId,
          dwightPercent: parsed.dwightPercent,
          bernardPercent: parsed.bernardPercent,
          richardPercent: parsed.richardPercent
        } : null;
      })
      .filter((row): row is BattleCafeRewardRowEdit => row !== null);
    if (rows.length > 0) {
      const submittedDraftSignature = draftContextSignature;
      onStageRows(
        rows,
        () =>
          draftContextIsActiveRef.current &&
          draftContextSignatureRef.current === submittedDraftSignature
      );
    }
  };

  return (
    <>
      <section aria-labelledby="battle-cafe-rewards-heading" className="panel wide-panel battle-cafe-rewards-panel">
        <div className="panel-heading">
          <Coffee aria-hidden="true" size={18} />
          <h2 id="battle-cafe-rewards-heading">{t('battleCafeRewards.title')}</h2>
        </div>

        <p className="panel-lede">{t('battleCafeRewards.description')}</p>

        {workflow ? (
          <div className="battle-cafe-rewards-editor">
            <div className="battle-cafe-rewards-summary" aria-label={t('battleCafeRewards.totals.heading')}>
              {ownerKeys.map((owner) => {
                const isExact = totals[owner] === 100;
                return (
                  <div className={`battle-cafe-total ${isExact ? 'is-valid' : 'is-invalid'}`} key={owner}>
                    {isExact ? <CheckCircle2 aria-hidden="true" size={18} /> : <TriangleAlert aria-hidden="true" size={18} />}
                    <span>{t(`battleCafeRewards.owner.${owner}`)}</span>
                    <strong>{t('battleCafeRewards.totals.value', { total: totals[owner] ?? '-' })}</strong>
                  </div>
                );
              })}
              <div className="battle-cafe-stage-status" role="status">
                <span>{t('battleCafeRewards.status.label')}</span>
                <strong>{hasDirtyDraft && hasStagedChange
                  ? t('battleCafeRewards.status.stagedWithDraft', {
                    count: dirtyRowIndexes.size,
                    staged: decodedStagedRows?.length ?? 0
                  })
                  : hasDirtyDraft
                    ? t('battleCafeRewards.status.draft', { count: dirtyRowIndexes.size })
                    : hasStagedChange
                      ? t('battleCafeRewards.status.staged', { count: decodedStagedRows?.length ?? 0 })
                      : t('battleCafeRewards.status.clean')}</strong>
              </div>
            </div>

            {!totalsAreExact ? (
              <div className="battle-cafe-notice" role="status">
                <TriangleAlert aria-hidden="true" size={18} />
                <span>{t('battleCafeRewards.validation.totals')}</span>
              </div>
            ) : null}
            {!itemChoicesAreUnique ? (
              <div className="battle-cafe-notice" role="alert">
                <TriangleAlert aria-hidden="true" size={18} />
                <span>{t('battleCafeRewards.validation.uniqueItems')}</span>
              </div>
            ) : null}
            {hasInvalidStagedChange ? (
              <div className="battle-cafe-notice" role="alert">
                <TriangleAlert aria-hidden="true" size={18} />
                <span>{t('battleCafeRewards.validation.invalidStaged')}</span>
              </div>
            ) : null}
            {workflow.summary.availability !== 'available' ? (
              <div className="battle-cafe-notice" role="status">
                <TriangleAlert aria-hidden="true" size={18} />
                <span>{t(`battleCafeRewards.availability.${workflow.summary.availability}`)}</span>
              </div>
            ) : null}

            <div className="battle-cafe-reward-grid">
              {draftRows.map((row) => (
                <BattleCafeRewardCard
                  disabled={!canEdit}
                  isDirty={dirtyRowIndexes.has(row.rowIndex)}
                  itemOptions={workflow.itemOptions}
                  key={row.rowIndex}
                  onChange={(patch) => updateRow(row.rowIndex, patch)}
                  onRestore={() => restoreRow(row.rowIndex)}
                  row={row}
                />
              ))}
            </div>

            <div className="battle-cafe-actions">
              <button
                aria-busy={isStaging}
                className="primary-button"
                disabled={!canStage}
                onClick={stageRows}
                type="button"
              >
                <Save aria-hidden="true" size={16} />
                <span>{isStaging ? t('battleCafeRewards.action.staging') : t('battleCafeRewards.action.stage')}</span>
              </button>
              <button
                aria-busy={isChangePlanCreating}
                className="secondary-button"
                disabled={!canReview}
                onClick={onCreateChangePlan}
                type="button"
              >
                <ClipboardCheck aria-hidden="true" size={16} />
                <span>{isChangePlanCreating ? t('battleCafeRewards.action.reviewing') : t('battleCafeRewards.action.review')}</span>
              </button>
              <button
                aria-busy={isChangePlanApplying}
                className="primary-button"
                disabled={!canApply}
                onClick={onApplyChangePlan}
                type="button"
              >
                <Save aria-hidden="true" size={16} />
                <span>{isChangePlanApplying ? t('battleCafeRewards.action.applying') : t('battleCafeRewards.action.apply')}</span>
              </button>
            </div>

            {workflow.provenance ? (
              <dl className="battle-cafe-provenance">
                <div>
                  <dt>{t('battleCafeRewards.source.file')}</dt>
                  <dd data-localization-ignore="true">{workflow.provenance.sourceFile}</dd>
                </div>
                <div>
                  <dt>{t('battleCafeRewards.source.layer')}</dt>
                  <dd>{translateLiteral(formatSourceLayer(workflow.provenance.sourceLayer))}</dd>
                </div>
                <div>
                  <dt>{t('battleCafeRewards.source.state')}</dt>
                  <dd>{translateLiteral(formatFileState(workflow.provenance.fileState))}</dd>
                </div>
              </dl>
            ) : null}
          </div>
        ) : (
          <p className="empty-copy">{t('battleCafeRewards.empty')}</p>
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

function BattleCafeRewardCard({
  disabled,
  isDirty,
  itemOptions,
  onChange,
  onRestore,
  row
}: {
  disabled: boolean;
  isDirty: boolean;
  itemOptions: BattleCafeRewardItemOption[];
  onChange: (patch: Partial<DraftRow>) => void;
  onRestore: () => void;
  row: DraftRow;
}) {
  const { t } = useLocalization();
  const option = itemOptions.find((candidate) => candidate.itemId === row.itemId) ?? null;

  return (
    <article className={`battle-cafe-reward-card ${isDirty ? 'is-dirty' : ''}`}>
      <header>
        <div>
          <span>{t('battleCafeRewards.row.label', { row: row.rowIndex })}</span>
          <strong data-localization-ignore="true">{option?.name ?? `#${row.itemId}`}</strong>
        </div>
        <button
          aria-label={t('battleCafeRewards.row.restore', { row: row.rowIndex })}
          className="icon-button"
          disabled={disabled || !isDirty}
          onClick={onRestore}
          title={t('battleCafeRewards.row.restore', { row: row.rowIndex })}
          type="button"
        >
          <RotateCcw aria-hidden="true" size={16} />
        </button>
      </header>

      <BattleCafeItemPicker
        disabled={disabled}
        itemOptions={itemOptions}
        onChange={(itemId) => onChange({ itemId })}
        rowIndex={row.rowIndex}
        value={row.itemId}
      />

      <div className="battle-cafe-percent-grid">
        {ownerKeys.map((owner) => (
          <label key={owner}>
            <span>{t(`battleCafeRewards.owner.${owner}`)}</span>
            <span className="battle-cafe-percent-input">
              <input
                aria-label={t('battleCafeRewards.row.ownerPercent', {
                  owner: t(`battleCafeRewards.owner.${owner}`),
                  row: row.rowIndex
                })}
                disabled={disabled}
                inputMode="numeric"
                aria-invalid={parsePercentDraft(row[owner]) === null ? 'true' : undefined}
                onChange={(event) => onChange({ [owner]: event.currentTarget.value })}
                onFocus={(event) => event.currentTarget.select()}
                pattern="[0-9]*"
                type="text"
                value={row[owner]}
              />
              <span aria-hidden="true">%</span>
            </span>
          </label>
        ))}
      </div>
    </article>
  );
}

function BattleCafeItemPicker({
  disabled,
  itemOptions,
  onChange,
  rowIndex,
  value
}: {
  disabled: boolean;
  itemOptions: BattleCafeRewardItemOption[];
  onChange: (itemId: number) => void;
  rowIndex: number;
  value: number;
}) {
  const { t } = useLocalization();
  const listId = useId();
  const inputRef = useRef<HTMLInputElement>(null);
  const selected = itemOptions.find((option) => option.itemId === value) ?? null;
  const [query, setQuery] = useState('');
  const [isOpen, setIsOpen] = useState(false);
  const [activeIndex, setActiveIndex] = useState(0);
  const suggestions = useMemo(
    () => filterAndRankSearchableOptions(
      itemOptions,
      query,
      maximumSuggestions,
      (option) => option.itemId,
      (option) => option.name,
      (option) => [option.name, option.category, option.itemId.toString()]
    ),
    [itemOptions, query]
  );

  useEffect(() => {
    setQuery(selected?.name ?? '');
  }, [selected?.itemId, selected?.name]);

  useEffect(() => {
    if (disabled) {
      setIsOpen(false);
      setActiveIndex(0);
      setQuery(selected?.name ?? '');
    }
  }, [disabled, selected?.name]);

  const select = (option: BattleCafeRewardItemOption) => {
    if (disabled) {
      return;
    }
    onChange(option.itemId);
    setQuery(option.name);
    setIsOpen(false);
    setActiveIndex(0);
    inputRef.current?.focus();
  };

  const resolveExactQuery = () => {
    if (disabled) {
      setQuery(selected?.name ?? '');
      setIsOpen(false);
      setActiveIndex(0);
      return;
    }
    const exact = findExactSearchableOption(
      itemOptions,
      query,
      (option) => option.itemId,
      (option) => option.name
    );
    if (exact) {
      onChange(exact.itemId);
      setQuery(exact.name);
    } else {
      setQuery(selected?.name ?? '');
    }
    setIsOpen(false);
    setActiveIndex(0);
  };

  return (
    <div className="battle-cafe-item-picker">
      <label htmlFor={`${listId}-input`}>{t('battleCafeRewards.row.item')}</label>
      <div className="battle-cafe-combobox">
        <Search aria-hidden="true" size={16} />
        <input
          aria-activedescendant={isOpen && suggestions[activeIndex] ? `${listId}-${suggestions[activeIndex].itemId}` : undefined}
          aria-autocomplete="list"
          aria-controls={listId}
           aria-expanded={isOpen && !disabled}
          aria-label={t('battleCafeRewards.row.itemSearch', { row: rowIndex })}
          disabled={disabled}
          id={`${listId}-input`}
          onBlur={resolveExactQuery}
          onChange={(event) => {
            setQuery(event.target.value);
            setIsOpen(true);
            setActiveIndex(0);
          }}
           onFocus={() => {
             if (!disabled) setIsOpen(true);
           }}
          onKeyDown={(event) => {
            if (event.key === 'ArrowDown') {
              event.preventDefault();
              setIsOpen(true);
              setActiveIndex((current) => Math.min(current + 1, suggestions.length - 1));
            } else if (event.key === 'ArrowUp') {
              event.preventDefault();
              setActiveIndex((current) => Math.max(0, current - 1));
             } else if (
               event.key === 'Enter' &&
               !event.nativeEvent.isComposing &&
               isOpen &&
               suggestions[activeIndex]
             ) {
              event.preventDefault();
              select(suggestions[activeIndex]);
            } else if (event.key === 'Escape') {
              setIsOpen(false);
              setQuery(selected?.name ?? '');
            }
          }}
          placeholder={t('battleCafeRewards.row.itemPlaceholder')}
          ref={inputRef}
          role="combobox"
          type="search"
          value={query}
        />
      </div>
       {isOpen && !disabled ? (
        <ul aria-label={t('battleCafeRewards.row.itemResults')} className="battle-cafe-item-results" id={listId} role="listbox">
          {suggestions.length > 0 ? suggestions.map((option, index) => (
            <li key={option.itemId} role="none">
              <button
                aria-selected={index === activeIndex}
                className={index === activeIndex ? 'is-active' : ''}
                id={`${listId}-${option.itemId}`}
                onMouseDown={(event) => event.preventDefault()}
                onClick={() => select(option)}
                role="option"
                tabIndex={-1}
                type="button"
              >
                <span data-localization-ignore="true">{option.name}</span>
                <small data-localization-ignore="true">{option.category} · #{option.itemId}</small>
              </button>
            </li>
          )) : (
            <li className="empty-copy" role="none">{t('battleCafeRewards.row.noItems')}</li>
          )}
        </ul>
      ) : null}
    </div>
  );
}

function decodeStagedRows(
  value: string | null | undefined,
  sourceRows: BattleCafeRewardRow[],
  itemOptions: BattleCafeRewardItemOption[]
): BattleCafeRewardRowEdit[] | null {
  if (!value) {
    return [];
  }

  if (!value.startsWith('v1|') || value.length > 4_096) {
    return null;
  }

  const sourceByIndex = new Map(sourceRows.map((row) => [row.rowIndex, row]));
  const itemIds = new Set(itemOptions.map((item) => item.itemId));
  const parts = value.slice(3).split(';');
  if (parts.length < 1 || parts.length > 23) {
    return null;
  }

  const rows: BattleCafeRewardRowEdit[] = [];
  const seen = new Set<number>();
  for (const part of parts) {
    const values = part.split(',').map((entry) => Number(entry));
    if (values.length !== 9 || values.some((entry) => !Number.isSafeInteger(entry))) {
      return null;
    }
    const [rowIndex, expectedItemId, expectedDwightPercent, expectedBernardPercent,
      expectedRichardPercent, itemId, dwightPercent, bernardPercent, richardPercent] = values;
    if (rowIndex === undefined || expectedItemId === undefined || expectedDwightPercent === undefined ||
      expectedBernardPercent === undefined || expectedRichardPercent === undefined || itemId === undefined ||
      dwightPercent === undefined || bernardPercent === undefined || richardPercent === undefined ||
      rowIndex < 1 || rowIndex > 23 || seen.has(rowIndex) || itemId < 1 || itemId > 65_535 ||
      !itemIds.has(itemId) ||
      [expectedDwightPercent, expectedBernardPercent, expectedRichardPercent, dwightPercent, bernardPercent, richardPercent]
        .some((entry) => entry < 0 || entry > 100)) {
      return null;
    }
    const source = sourceByIndex.get(rowIndex);
    if (!source || source.itemId !== expectedItemId || source.dwightPercent !== expectedDwightPercent ||
      source.bernardPercent !== expectedBernardPercent || source.richardPercent !== expectedRichardPercent) {
      return null;
    }
    seen.add(rowIndex);
    rows.push({ rowIndex, expectedItemId, expectedDwightPercent, expectedBernardPercent,
      expectedRichardPercent, itemId, dwightPercent, bernardPercent, richardPercent });
  }
  return rows.sort((left, right) => left.rowIndex - right.rowIndex);
}

function mergeRows(sourceRows: BattleCafeRewardRow[], stagedRows: BattleCafeRewardRowEdit[]): DraftRow[] {
  const stagedByIndex = new Map(stagedRows.map((row) => [row.rowIndex, row]));
  return sourceRows.map((source) => {
    const staged = stagedByIndex.get(source.rowIndex);
    return {
      rowIndex: source.rowIndex,
      itemId: staged?.itemId ?? source.itemId,
      dwightPercent: (staged?.dwightPercent ?? source.dwightPercent).toString(),
      bernardPercent: (staged?.bernardPercent ?? source.bernardPercent).toString(),
      richardPercent: (staged?.richardPercent ?? source.richardPercent).toString()
    };
  });
}

const ownerKeys: OwnerKey[] = ['dwightPercent', 'bernardPercent', 'richardPercent'];

function calculateTotals(rows: DraftRow[]) {
  return Object.fromEntries(ownerKeys.map((owner) => {
    const parsed = rows.map((row) => parsePercentDraft(row[owner]));
    return [
      owner,
      parsed.some((value) => value === null)
        ? null
        : parsed.reduce<number>((total, value) => total + (value ?? 0), 0)
    ];
  })) as Record<OwnerKey, number | null>;
}

function sameDraftRow(left: DraftRow, right: DraftRow | undefined) {
  return right !== undefined && left.itemId === right.itemId &&
    ownerKeys.every((owner) => {
      const leftValue = parsePercentDraft(left[owner]);
      const rightValue = parsePercentDraft(right[owner]);
      return leftValue !== null && leftValue === rightValue;
    });
}

function encodeDraftRows(rows: DraftRow[]) {
  return rows.map((row) => [row.rowIndex, row.itemId, row.dwightPercent,
    row.bernardPercent, row.richardPercent].join(',')).join(';');
}

function parsePercentDraft(value: string) {
  return parseBoundedWholeNumberDraft(value, 0, 100);
}

function parseDraftRow(row: DraftRow) {
  const dwightPercent = parsePercentDraft(row.dwightPercent);
  const bernardPercent = parsePercentDraft(row.bernardPercent);
  const richardPercent = parsePercentDraft(row.richardPercent);
  return dwightPercent === null || bernardPercent === null || richardPercent === null
    ? null
    : {
      rowIndex: row.rowIndex,
      itemId: row.itemId,
      dwightPercent,
      bernardPercent,
      richardPercent
    };
}
