/* SPDX-License-Identifier: GPL-3.0-only */

import { ChevronLeft, ChevronRight, Palette, Search, ShieldCheck } from 'lucide-react';
import { useEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react';
import type { EditSession } from '../../bridge/contracts';
import type {
  DressUpGroupRecord,
  DressUpItemRecord,
  FashionCatalogFile,
  FashionLineupEntryRecord,
  FashionCatalogWorkflow,
  HairAndMakeupRecord
} from '../../bridge/fashionCatalogContracts';
import {
  Metric,
  WorkflowPanelOutputSections,
  type WorkflowPanelOutput
} from '../../components/workflowPanels';
import { useLocalization } from '../../localization';
import './FashionCatalogSection.css';

const pageSize = 50;
const catalogFiles = [
  'dressUpItems',
  'dressUpGroups',
  'hairAndMakeup',
  'dressUpLineups',
  'hairAndMakeupLineups'
] as const;

type CatalogRecord =
  | DressUpItemRecord
  | DressUpGroupRecord
  | HairAndMakeupRecord
  | FashionLineupEntryRecord;

export type FashionCatalogFieldEditInput = {
  binding: {
    physicalIndex: number;
    physicalRowId: string;
    rowRevision: string;
    sourceRevision: string;
  };
  catalogFile: FashionCatalogFile;
  clear: boolean;
  field: string;
  value: string | null;
};

type FashionCatalogSectionProps = {
  editSession: EditSession | null;
  isStaging: boolean;
  onOpenChanges: () => void;
  onStageFieldEdit: (edit: FashionCatalogFieldEditInput) => Promise<boolean>;
  panelOutput: WorkflowPanelOutput;
  workflow: FashionCatalogWorkflow | null;
};

type FieldDefinition = {
  field: string;
  label: string;
  optional?: boolean;
  valueKind: 'number' | 'option';
};

type Translate = (key: string, values?: Record<string, string | number>) => string;

export function FashionCatalogSection({
  editSession,
  isStaging,
  onOpenChanges,
  onStageFieldEdit,
  panelOutput,
  workflow
}: FashionCatalogSectionProps) {
  const { t } = useLocalization();
  const [catalogFile, setCatalogFile] = useState<FashionCatalogFile>('dressUpItems');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(0);
  const [selectedRowId, setSelectedRowId] = useState<string | null>(null);
  const [selectedField, setSelectedField] = useState('itemId');
  const [draftValue, setDraftValue] = useState('');
  const [optionSearch, setOptionSearch] = useState('');
  const [feedback, setFeedback] = useState<
    { kind: 'error' | 'success'; message: string } | null
  >(null);
  const feedbackRef = useRef<HTMLDivElement | null>(null);

  const rows = useMemo(() => getRows(workflow, catalogFile), [catalogFile, workflow]);
  const filteredRows = useMemo(() => {
    const query = search.trim().toLocaleLowerCase();
    return query.length === 0
      ? rows
      : rows.filter((row) => getSearchText(row).toLocaleLowerCase().includes(query));
  }, [rows, search]);
  const pageCount = Math.max(1, Math.ceil(filteredRows.length / pageSize));
  const visibleRows = filteredRows.slice(page * pageSize, (page + 1) * pageSize);
  const selectedRow = visibleRows.find((row) => row.physicalRowId === selectedRowId) ?? null;
  const fields = useMemo(
    () => getFieldDefinitions(catalogFile, t),
    [catalogFile, t]
  );
  const field = fields.find((candidate) => candidate.field === selectedField) ?? fields[0];
  const options = useMemo(
    () => getLoadedOptions(workflow, catalogFile, field?.field ?? ''),
    [catalogFile, field?.field, workflow]
  );
  const filteredOptions = useMemo(() => {
    const query = optionSearch.trim().toLocaleLowerCase();
    const matches = query.length === 0
      ? options
      : options.filter((option) => option.toLocaleLowerCase().includes(query));
    const selected = draftValue.length > 0 && options.includes(draftValue)
      ? [draftValue]
      : [];
    return Array.from(new Set([...selected, ...matches])).slice(0, 250);
  }, [draftValue, optionSearch, options]);
  const pendingCount = editSession?.pendingEdits.filter(
    (edit) => edit.domain === 'workflow.fashionCatalog'
  ).length ?? 0;

  useEffect(() => {
    setPage(0);
  }, [catalogFile, search]);

  useEffect(() => {
    if (page >= pageCount) {
      setPage(pageCount - 1);
    }
  }, [page, pageCount]);

  useEffect(() => {
    if (selectedRowId && visibleRows.some((row) => row.physicalRowId === selectedRowId)) {
      return;
    }

    setSelectedRowId(visibleRows[0]?.physicalRowId ?? null);
  }, [selectedRowId, visibleRows]);

  useEffect(() => {
    if (!fields.some((candidate) => candidate.field === selectedField)) {
      setSelectedField(fields[0]?.field ?? '');
    }
  }, [fields, selectedField]);

  useEffect(() => {
    setDraftValue(selectedRow && field ? getFieldValue(selectedRow, field.field) : '');
    setOptionSearch('');
  }, [field, selectedRow]);

  useEffect(() => {
    if (feedback) {
      feedbackRef.current?.focus();
    }
  }, [feedback]);

  if (!workflow) {
    return (
      <section aria-labelledby="fashion-catalog-heading" className="panel wide-panel">
        <div className="panel-heading">
          <Palette aria-hidden="true" size={18} />
          <h2 id="fashion-catalog-heading">{t('fashionCatalog.title')}</h2>
        </div>
        <p className="empty-copy">{t('fashionCatalog.empty')}</p>
      </section>
    );
  }

  const canStage =
    workflow.canStage &&
    selectedRow !== null &&
    field !== undefined &&
    draftValue !== getFieldValue(selectedRow, field.field) &&
    !isStaging &&
    (field.valueKind === 'option'
      ? options.includes(draftValue)
      : isValidNumericDraft(field.field, draftValue));

  const selectCatalogFile = (file: FashionCatalogFile) => {
    setCatalogFile(file);
    setFeedback(null);
  };

  const handleTabKeyDown = (
    event: KeyboardEvent<HTMLButtonElement>,
    file: FashionCatalogFile
  ) => {
    const currentIndex = catalogFiles.indexOf(file);
    let nextIndex: number | null = null;
    if (event.key === 'ArrowRight') {
      nextIndex = (currentIndex + 1) % catalogFiles.length;
    } else if (event.key === 'ArrowLeft') {
      nextIndex = (currentIndex - 1 + catalogFiles.length) % catalogFiles.length;
    } else if (event.key === 'Home') {
      nextIndex = 0;
    } else if (event.key === 'End') {
      nextIndex = catalogFiles.length - 1;
    }

    if (nextIndex === null) {
      return;
    }

    event.preventDefault();
    const nextFile = catalogFiles[nextIndex];
    selectCatalogFile(nextFile);
    requestAnimationFrame(() => {
      document.getElementById(`fashion-catalog-tab-${nextFile}`)?.focus();
    });
  };

  const stage = async (clear: boolean) => {
    if (!selectedRow || !field) {
      return;
    }

    setFeedback(null);
    const succeeded = await onStageFieldEdit({
      binding: {
        physicalIndex: selectedRow.physicalIndex,
        physicalRowId: selectedRow.physicalRowId,
        rowRevision: selectedRow.rowRevision,
        sourceRevision: workflow.sourceRevision
      },
      catalogFile,
      clear,
      field: field.field,
      value: clear ? null : draftValue
    });
    setFeedback({
      kind: succeeded ? 'success' : 'error',
      message: succeeded
        ? t('fashionCatalog.feedback.staged')
        : t('fashionCatalog.feedback.failed')
    });
  };

  return (
    <div className="fashion-catalog-workspace workflow-panel-stack">
      <section aria-labelledby="fashion-catalog-heading" className="panel wide-panel">
        <div className="panel-heading">
          <Palette aria-hidden="true" size={18} />
          <h2 id="fashion-catalog-heading">{t('fashionCatalog.title')}</h2>
          <span className="status-badge ready">{t('fashionCatalog.dataOnlyBadge')}</span>
        </div>
        <p className="section-copy">{t('fashionCatalog.description')}</p>
        <p className="field-hint">{t('fashionCatalog.shopScope')}</p>
        <div className="metrics-grid compact-metrics">
          <Metric label={t('fashionCatalog.metrics.items')} value={String(workflow.stats.dressUpItemCount)} />
          <Metric label={t('fashionCatalog.metrics.groups')} value={String(workflow.stats.dressUpGroupCount)} />
          <Metric label={t('fashionCatalog.metrics.hair')} value={String(workflow.stats.hairAndMakeupCount)} />
          <Metric label={t('fashionCatalog.metrics.dressUpLineups')} value={String(workflow.stats.dressUpLineupEntryCount)} />
          <Metric label={t('fashionCatalog.metrics.hairLineups')} value={String(workflow.stats.hairAndMakeupLineupEntryCount)} />
          <Metric label={t('fashionCatalog.metrics.staged')} value={String(pendingCount)} />
        </div>
      </section>

      <section aria-labelledby="fashion-catalog-editor-heading" className="panel wide-panel">
        <div className="panel-heading">
          <ShieldCheck aria-hidden="true" size={18} />
          <h2 id="fashion-catalog-editor-heading">{t('fashionCatalog.editor.title')}</h2>
        </div>
        <p className="field-hint">{t('fashionCatalog.editor.safety')}</p>
        <div aria-label={t('fashionCatalog.tabs.label')} className="fashion-catalog-tabs" role="tablist">
          {catalogFiles.map((file) => (
            <button
              aria-controls={`fashion-catalog-panel-${file}`}
              aria-selected={catalogFile === file}
              className={catalogFile === file ? 'active' : ''}
              id={`fashion-catalog-tab-${file}`}
              key={file}
              onClick={() => selectCatalogFile(file)}
              onKeyDown={(event) => handleTabKeyDown(event, file)}
              role="tab"
              tabIndex={catalogFile === file ? 0 : -1}
              type="button"
            >
              {t(`fashionCatalog.tabs.${file}`)}
            </button>
          ))}
        </div>

        <div
          aria-labelledby={`fashion-catalog-tab-${catalogFile}`}
          className="fashion-catalog-editor-grid"
          id={`fashion-catalog-panel-${catalogFile}`}
          role="tabpanel"
          tabIndex={0}
        >
          <div className="fashion-catalog-browser">
            <label className="field-label" htmlFor="fashion-catalog-search">
              {t('fashionCatalog.search.label')}
            </label>
            <div className="search-field">
              <Search aria-hidden="true" size={16} />
              <input
                id="fashion-catalog-search"
                onChange={(event) => setSearch(event.target.value)}
                placeholder={t('fashionCatalog.search.placeholder')}
                type="search"
                value={search}
              />
            </div>
            <p className="field-hint">
              {t('fashionCatalog.search.results', { count: filteredRows.length })}
            </p>
            <div className="fashion-catalog-row-list">
              {visibleRows.length === 0 ? (
                <p className="empty-copy">{t('fashionCatalog.search.noResults')}</p>
              ) : visibleRows.map((row) => (
                <button
                  aria-pressed={selectedRowId === row.physicalRowId}
                  className={selectedRowId === row.physicalRowId ? 'selected' : ''}
                  key={row.physicalRowId}
                  onClick={() => setSelectedRowId(row.physicalRowId)}
                  type="button"
                >
                  <strong>{getRowTitle(row)}</strong>
                  <span>{getRowSubtitle(row)}</span>
                </button>
              ))}
            </div>
            <div className="fashion-catalog-pagination">
              <button
                aria-label={t('fashionCatalog.pagination.previous')}
                disabled={page === 0}
                onClick={() => setPage((current) => Math.max(0, current - 1))}
                type="button"
              >
                <ChevronLeft aria-hidden="true" size={16} />
              </button>
              <span>{t('fashionCatalog.pagination.page', { current: page + 1, total: pageCount })}</span>
              <button
                aria-label={t('fashionCatalog.pagination.next')}
                disabled={page >= pageCount - 1}
                onClick={() => setPage((current) => Math.min(pageCount - 1, current + 1))}
                type="button"
              >
                <ChevronRight aria-hidden="true" size={16} />
              </button>
            </div>
          </div>

          <div className="fashion-catalog-field-editor">
            {selectedRow && field ? (
              <>
                <div className="fashion-catalog-selection-heading">
                  <strong>{getRowTitle(selectedRow)}</strong>
                  <span>{t('fashionCatalog.editor.rowNumber', {
                    row: selectedRow.physicalIndex + 1
                  })}</span>
                </div>
                <label className="field-label" htmlFor="fashion-catalog-field">
                  {t('fashionCatalog.editor.field')}
                </label>
                <select
                  id="fashion-catalog-field"
                  onChange={(event) => setSelectedField(event.target.value)}
                  value={field.field}
                >
                  {fields.map((candidate) => (
                    <option key={candidate.field} value={candidate.field}>{candidate.label}</option>
                  ))}
                </select>

                {field.valueKind === 'option' ? (
                  <>
                    <label className="field-label" htmlFor="fashion-catalog-option-search">
                      {t('fashionCatalog.editor.optionSearch')}
                    </label>
                    <input
                      id="fashion-catalog-option-search"
                      onChange={(event) => setOptionSearch(event.target.value)}
                      placeholder={t('fashionCatalog.editor.optionSearchPlaceholder')}
                      type="search"
                      value={optionSearch}
                    />
                    <label className="field-label" htmlFor="fashion-catalog-value">
                      {t('fashionCatalog.editor.value')}
                    </label>
                    <select
                      id="fashion-catalog-value"
                      onChange={(event) => setDraftValue(event.target.value)}
                      value={filteredOptions.includes(draftValue) ? draftValue : ''}
                    >
                      <option disabled value="">{t('fashionCatalog.editor.chooseOption')}</option>
                      {filteredOptions.map((option) => (
                        <option key={option} value={option}>{option}</option>
                      ))}
                    </select>
                    <p className="field-hint">
                      {t('fashionCatalog.editor.provenOptions', { count: options.length })}
                    </p>
                  </>
                ) : (
                  <>
                    <label className="field-label" htmlFor="fashion-catalog-value">
                      {t('fashionCatalog.editor.value')}
                    </label>
                    <input
                      id="fashion-catalog-value"
                      onChange={(event) => setDraftValue(event.target.value)}
                      type="number"
                      value={draftValue}
                    />
                  </>
                )}

                <div className="button-row fashion-catalog-actions">
                  <button
                    className="primary-button"
                    disabled={!canStage}
                    onClick={() => void stage(false)}
                    type="button"
                  >
                    {isStaging ? t('fashionCatalog.editor.staging') : t('fashionCatalog.editor.stage')}
                  </button>
                  {field.optional ? (
                    <button
                      disabled={isStaging || getFieldValue(selectedRow, field.field).length === 0}
                      onClick={() => void stage(true)}
                      type="button"
                    >
                      {t('fashionCatalog.editor.clear')}
                    </button>
                  ) : null}
                  {pendingCount > 0 ? (
                    <button onClick={onOpenChanges} type="button">
                      {t('fashionCatalog.editor.review', { count: pendingCount })}
                    </button>
                  ) : null}
                </div>
              </>
            ) : (
              <p className="empty-copy">{t('fashionCatalog.editor.noSelection')}</p>
            )}
          </div>
        </div>

        {feedback ? (
          <div
            className={`action-feedback ${feedback.kind}`}
            ref={feedbackRef}
            role={feedback.kind === 'error' ? 'alert' : 'status'}
            tabIndex={-1}
          >
            {feedback.message}
          </div>
        ) : null}
      </section>

      <WorkflowPanelOutputSections
        output={panelOutput}
        workflowDiagnostics={workflow.diagnostics}
      />
    </div>
  );
}

function getRows(
  workflow: FashionCatalogWorkflow | null,
  file: FashionCatalogFile
): CatalogRecord[] {
  if (!workflow) {
    return [];
  }

  switch (file) {
    case 'dressUpItems':
      return workflow.dressUpItems;
    case 'dressUpGroups':
      return workflow.dressUpGroups;
    case 'hairAndMakeup':
      return workflow.hairAndMakeup;
    case 'dressUpLineups':
      return workflow.dressUpLineups;
    case 'hairAndMakeupLineups':
      return workflow.hairAndMakeupLineups;
  }
}

function getSearchText(row: CatalogRecord): string {
  if ('lineupId' in row) {
    return `${row.itemId} ${row.lineupId} ${row.shopIds.join(' ')}`;
  }
  if ('modelVariant' in row) {
    return `${row.itemId} ${row.modelPart} ${row.modelVariant} ${row.primaryColorLabel} ${row.secondaryColorLabel}`;
  }
  if ('displayLabel' in row) {
    return `${row.modelPart} ${row.displayLabel} ${row.displayOrder}`;
  }
  return `${row.itemId} ${row.modelKey} ${row.colorValue ?? ''} ${row.labelKey ?? ''}`;
}

function getRowTitle(row: CatalogRecord): string {
  if ('lineupId' in row) {
    return `${row.itemId} · ${row.lineupId}`;
  }
  if ('modelVariant' in row) {
    return `${row.itemId} · ${row.modelPart}`;
  }
  if ('displayLabel' in row) {
    return row.displayLabel || row.modelPart;
  }
  return `${row.itemId} · ${row.modelKey}`;
}

function getRowSubtitle(row: CatalogRecord): string {
  if ('lineupId' in row) {
    return row.shopIds.join(', ') || String(row.entryPhysicalIndex + 1);
  }
  if ('modelVariant' in row) {
    return row.modelVariant;
  }
  if ('displayLabel' in row) {
    return row.modelPart;
  }
  return row.labelKey ?? row.colorValue ?? '';
}

function getFieldDefinitions(
  file: FashionCatalogFile,
  t: Translate
): FieldDefinition[] {
  if (file === 'dressUpItems') {
    return [
      ['itemId', 'number'],
      ['modelPart', 'option'],
      ['catalogGroupCode', 'option'],
      ['modelVariant', 'option'],
      ['categoryCode', 'option'],
      ['colorVariantCode', 'option'],
      ['primaryColorLabel', 'option'],
      ['secondaryColorLabel', 'option'],
      ['displayOrder', 'number'],
      ['variantOrder', 'number']
    ].map(([field, valueKind]) => ({
      field,
      label: t(`fashionCatalog.fields.${field}`),
      valueKind: valueKind as FieldDefinition['valueKind']
    }));
  }
  if (file === 'dressUpGroups') {
    return [
      ['modelPart', 'option'],
      ['displayOrder', 'number'],
      ['displayLabel', 'option']
    ].map(([field, valueKind]) => ({
      field,
      label: t(`fashionCatalog.fields.${field}`),
      valueKind: valueKind as FieldDefinition['valueKind']
    }));
  }
  if (file === 'dressUpLineups' || file === 'hairAndMakeupLineups') {
    return [{
      field: 'itemId',
      label: t('fashionCatalog.fields.itemId'),
      valueKind: 'option'
    }];
  }
  const definitions: Array<Omit<FieldDefinition, 'label'>> = [
    { field: 'itemId', valueKind: 'number' },
    { field: 'modelKey', valueKind: 'option' },
    { field: 'catalogTypeCode', valueKind: 'option' },
    { field: 'colorValue', optional: true, valueKind: 'option' },
    { field: 'labelKey', optional: true, valueKind: 'option' },
    { field: 'displayOrder', valueKind: 'number' },
    { field: 'groupCode', valueKind: 'option' },
    { field: 'variantCode', valueKind: 'option' }
  ];
  return definitions.map((definition) => ({
    ...definition,
    label: t(`fashionCatalog.fields.${definition.field}`)
  }));
}

function getLoadedOptions(
  workflow: FashionCatalogWorkflow | null,
  file: FashionCatalogFile,
  field: string
): string[] {
  if (!workflow) {
    return [];
  }

  let values: Array<string | number | null> = [];
  if (file === 'dressUpItems') {
    if (field === 'modelPart') {
      values = workflow.dressUpGroups.map((row) => row.modelPart);
    } else if (field === 'modelVariant') {
      values = workflow.dressUpItems.map((row) => row.modelVariant);
    } else if (field === 'primaryColorLabel' || field === 'secondaryColorLabel') {
      values = workflow.dressUpItems.flatMap((row) => [row.primaryColorLabel, row.secondaryColorLabel]);
    } else {
      values = workflow.dressUpItems.map((row) => getFieldValue(row, field));
    }
  } else if (file === 'dressUpGroups') {
    values = field === 'modelPart'
      ? workflow.dressUpItems.map((row) => row.modelPart)
      : workflow.dressUpGroups.map((row) => getFieldValue(row, field));
  } else if (file === 'hairAndMakeup') {
    values = workflow.hairAndMakeup.map((row) => getFieldValue(row, field));
  } else if (file === 'dressUpLineups') {
    values = workflow.dressUpItems.map((row) => row.itemId);
  } else {
    values = workflow.hairAndMakeup.map((row) => row.itemId);
  }

  return Array.from(new Set(values.filter((value) => value !== null).map(String)))
    .sort((left, right) => left.localeCompare(right, undefined, { numeric: true }));
}

function getFieldValue(row: CatalogRecord, field: string): string {
  const value = (row as unknown as Record<string, unknown>)[field];
  return value === null || value === undefined ? '' : String(value);
}

function isValidNumericDraft(field: string, value: string): boolean {
  if (!/^-?\d+$/u.test(value)) {
    return false;
  }

  const parsed = Number(value);
  if (!Number.isSafeInteger(parsed)) {
    return false;
  }

  return field === 'groupCode' || field === 'variantCode'
    ? parsed >= -2_147_483_648 && parsed <= 2_147_483_647
    : parsed >= 0 && parsed <= 4_294_967_295;
}
