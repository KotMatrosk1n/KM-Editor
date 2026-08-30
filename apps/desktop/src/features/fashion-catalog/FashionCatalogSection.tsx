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
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import {
  Metric,
  WorkflowPanelOutputSections,
  type WorkflowPanelOutput
} from '../../components/workflowPanels';
import {
  FocusedEditorMetrics,
  FocusedEditorWorkspace
} from '../../components/FocusedEditorWorkspace';
import { useLocalization } from '../../localization';
import {
  clearStagedFashionCatalogDraftValue,
  createFashionCatalogDraftKey,
  setFashionCatalogDraftValue
} from './fashionCatalogDraftState';
import './FashionCatalogSection.css';

const pageSize = 50;
const optionRenderLimit = 500;
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
  onDirtyStateChange?: (isDirty: boolean) => void;
  onOpenChanges: () => void;
  onStageFieldEdit: (edit: FashionCatalogFieldEditInput) => Promise<boolean>;
  panelOutput: WorkflowPanelOutput;
  workflow: FashionCatalogWorkflow | null;
};

type FieldDefinition = {
  field: string;
  hintKey: string;
  label: string;
  optional?: boolean;
  readOnly?: boolean;
  valueKind: 'number' | 'option';
};

type FashionCatalogPresentation = {
  catalogGroupCodeByModelPart: ReadonlyMap<string, string>;
  colorLabelByValue: ReadonlyMap<string, string>;
  dressItemTitleById: ReadonlyMap<string, string>;
  dressVariantLabelByValue: ReadonlyMap<string, string>;
  groupLabelByModelPart: ReadonlyMap<string, string>;
  hairItemTitleById: ReadonlyMap<string, string>;
  hairModelLabelByValue: ReadonlyMap<string, string>;
  textLabelByKey: ReadonlyMap<string, string>;
};

const dressUpCatalogGroupTextKeys: Readonly<Record<string, string>> = {
  '0': 'dressup_07_01',
  '1': 'dressup_07_02',
  '2': 'dressup_07_00',
  '3': 'dressup_07_03',
  '4': 'dressup_07_04',
  '5': 'dressup_07_05',
  '6': 'dressup_07_06',
  '7': 'dressup_07_07',
  '8': 'dressup_07_08',
  '9': 'dressup_07_09'
};

const hairCatalogTypeTextKeys: Readonly<Record<string, string>> = {
  '0': 'dressup_06_13',
  '1': 'dressup_06_14',
  '2': 'dressup_06_15',
  '3': 'dressup_06_16',
  '4': 'dressup_06_17',
  '5': 'dressup_06_00',
  '6': 'dressup_06_01',
  '7': 'dressup_06_02',
  '8': 'dressup_06_03',
  '9': 'dressup_06_04',
  '10': 'dressup_06_06',
  '11': 'dressup_06_08',
  '12': 'dressup_06_09',
  '13': 'dressup_06_10',
  '14': 'dressup_06_18'
};

type Translate = (key: string, values?: Record<string, string | number>) => string;
type TranslateLiteral = (literal: string) => string;

export function FashionCatalogSection({
  editSession,
  isStaging,
  onDirtyStateChange,
  onOpenChanges,
  onStageFieldEdit,
  panelOutput,
  workflow
}: FashionCatalogSectionProps) {
  const { t, translateLiteral } = useLocalization();
  const [catalogFile, setCatalogFile] = useState<FashionCatalogFile>('dressUpItems');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(0);
  const [selectedRowId, setSelectedRowId] = useState<string | null>(null);
  const [selectedField, setSelectedField] = useState('itemId');
  const [draftValues, setDraftValues] = useState<Record<string, string>>({});
  const [optionSearch, setOptionSearch] = useState('');
  const [feedback, setFeedback] = useState<
    { kind: 'error' | 'success'; message: string } | null
  >(null);
  usePublishCommonEditorError({
    domain: 'workflow.fashionCatalog',
    field: 'stage',
    message: feedback?.kind === 'error' ? feedback.message : null
  });
  const feedbackRef = useRef<HTMLDivElement | null>(null);

  const presentation = useMemo(
    () => createFashionCatalogPresentation(workflow),
    [workflow]
  );

  const rows = useMemo(() => getRows(workflow, catalogFile), [catalogFile, workflow]);
  const filteredRows = useMemo(() => {
    const query = search.trim().toLocaleLowerCase();
    return query.length === 0
      ? rows
      : rows.filter((row) => getSearchText(row, presentation, catalogFile).toLocaleLowerCase().includes(query));
  }, [catalogFile, presentation, rows, search]);
  const pageCount = Math.max(1, Math.ceil(filteredRows.length / pageSize));
  const visibleRows = filteredRows.slice(page * pageSize, (page + 1) * pageSize);
  const selectedRow = visibleRows.find((row) => row.physicalRowId === selectedRowId) ?? null;
  const fields = useMemo(
    () => getFieldDefinitions(catalogFile, t),
    [catalogFile, t]
  );
  const field = fields.find((candidate) => candidate.field === selectedField) ?? fields[0];
  const draftKey = selectedRow && field
    ? createFashionCatalogDraftKey(catalogFile, selectedRow.physicalRowId, field.field)
    : null;
  const sourceValue = selectedRow && field ? getFieldValue(selectedRow, field.field) : '';
  const draftValue = draftKey ? draftValues[draftKey] ?? sourceValue : '';
  const draftContextRef = useRef({ key: draftKey, value: draftValue });
  draftContextRef.current = { key: draftKey, value: draftValue };
  const numericDraftError = Boolean(
    selectedRow &&
    field?.valueKind === 'number' &&
    draftValue !== sourceValue &&
    !isValidNumericDraft(field.field, draftValue)
  )
    ? translateLiteral('Enter a whole number within the supported range.')
    : null;
  usePublishCommonEditorError({
    domain: 'workflow.fashionCatalog',
    field: field?.field ?? 'value',
    message: numericDraftError
  });
  const options = useMemo(
    () => getLoadedOptions(workflow, catalogFile, field?.field ?? ''),
    [catalogFile, field?.field, workflow]
  );
  const optionWindow = useMemo(() => {
    const query = optionSearch.trim().toLocaleLowerCase();
    const matches = query.length === 0
      ? options
      : options.filter((option) => {
          const label = getOptionLabel(
            catalogFile,
            field?.field ?? '',
            option,
            presentation
          );
          return option.toLocaleLowerCase().includes(query)
            || label.toLocaleLowerCase().includes(query);
        });
    const selected = draftValue.length > 0 && options.includes(draftValue)
      ? [draftValue]
      : [];
    const selectedMatchesQuery = selected.length > 0 && matches.includes(draftValue);
    const nonSelectedMatches = matches.filter((option) => option !== draftValue);
    const visibleMatches = nonSelectedMatches.slice(
      0,
      Math.max(0, optionRenderLimit - selected.length)
    );
    return {
      matchingCount: matches.length,
      options: [...selected, ...visibleMatches],
      shownMatchingCount: visibleMatches.length + (selectedMatchesQuery ? 1 : 0)
    };
  }, [catalogFile, draftValue, field?.field, optionSearch, options, presentation]);
  const filteredOptions = optionWindow.options;
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
    setOptionSearch('');
    setFeedback(null);
  }, [draftKey]);

  useEffect(() => {
    onDirtyStateChange?.(Object.keys(draftValues).length > 0);
  }, [draftValues, onDirtyStateChange]);

  useEffect(() => () => onDirtyStateChange?.(false), [onDirtyStateChange]);

  useEffect(() => {
    if (feedback) {
      feedbackRef.current?.focus();
    }
  }, [feedback]);

  if (!workflow) {
    return (
      <FocusedEditorWorkspace className="fashion-catalog-workspace">
        <section aria-labelledby="fashion-catalog-heading" className="panel wide-panel">
          <div className="panel-heading">
            <Palette aria-hidden="true" size={18} />
            <h2 id="fashion-catalog-heading">{t('fashionCatalog.title')}</h2>
          </div>
          <p className="empty-copy focused-editor-readable-copy">
            {t('fashionCatalog.empty')}
          </p>
        </section>
      </FocusedEditorWorkspace>
    );
  }

  const canStage =
    workflow.canStage &&
    selectedRow !== null &&
    field !== undefined &&
    !field.readOnly &&
    draftValue !== sourceValue &&
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
    if (!workflow.canStage || !selectedRow || !field || !draftKey) {
      return;
    }

    const stagedDraftKey = draftKey;
    const stagedDraftValue = draftValue;
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
    if (
      draftContextRef.current.key === stagedDraftKey &&
      draftContextRef.current.value === stagedDraftValue
    ) {
      setFeedback({
        kind: succeeded ? 'success' : 'error',
        message: succeeded
          ? t('fashionCatalog.feedback.staged')
          : t('fashionCatalog.feedback.failed')
      });
    }
    if (succeeded) {
      setDraftValues((currentDrafts) =>
        clearStagedFashionCatalogDraftValue(
          currentDrafts,
          stagedDraftKey,
          stagedDraftValue
        )
      );
    }
  };

  const updateDraftValue = (value: string) => {
    if (!draftKey) {
      return;
    }

    setFeedback(null);
    setDraftValues((currentDrafts) =>
      setFashionCatalogDraftValue(currentDrafts, draftKey, value, sourceValue)
    );
  };

  return (
    <FocusedEditorWorkspace className="fashion-catalog-workspace">
      <section aria-labelledby="fashion-catalog-heading" className="panel wide-panel">
        <div className="panel-heading">
          <Palette aria-hidden="true" size={18} />
          <h2 id="fashion-catalog-heading">{t('fashionCatalog.title')}</h2>
          <span className="status-badge ready">{t('fashionCatalog.dataOnlyBadge')}</span>
        </div>
        <p className="section-copy focused-editor-readable-copy">
          {t('fashionCatalog.description')}
        </p>
        <p className="field-hint focused-editor-readable-copy">
          {t('fashionCatalog.shopScope')}
        </p>
        <FocusedEditorMetrics>
          <Metric label={t('fashionCatalog.metrics.items')} value={String(workflow.stats.dressUpItemCount)} />
          <Metric label={t('fashionCatalog.metrics.groups')} value={String(workflow.stats.dressUpGroupCount)} />
          <Metric label={t('fashionCatalog.metrics.hair')} value={String(workflow.stats.hairAndMakeupCount)} />
          <Metric label={t('fashionCatalog.metrics.dressUpLineups')} value={String(workflow.stats.dressUpLineupEntryCount)} />
          <Metric label={t('fashionCatalog.metrics.hairLineups')} value={String(workflow.stats.hairAndMakeupLineupEntryCount)} />
          <Metric label={t('fashionCatalog.metrics.staged')} value={String(pendingCount)} />
        </FocusedEditorMetrics>
      </section>

      <section aria-labelledby="fashion-catalog-editor-heading" className="panel wide-panel">
        <div className="panel-heading">
          <ShieldCheck aria-hidden="true" size={18} />
          <h2 id="fashion-catalog-editor-heading">{t('fashionCatalog.editor.title')}</h2>
        </div>
        <p className="field-hint focused-editor-readable-copy">
          {t('fashionCatalog.editor.safety')}
        </p>
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
              ) : visibleRows.map((row) => {
                const subtitle = getRowSubtitle(row, presentation, translateLiteral);
                return (
                  <button
                    aria-pressed={selectedRowId === row.physicalRowId}
                    className={selectedRowId === row.physicalRowId ? 'selected' : ''}
                    key={row.physicalRowId}
                    onClick={() => setSelectedRowId(row.physicalRowId)}
                    type="button"
                  >
                    <strong>{getRowTitle(row, presentation, catalogFile)}</strong>
                    {subtitle ? <span>{subtitle}</span> : null}
                  </button>
                );
              })}
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
                  <strong>{getRowTitle(selectedRow, presentation, catalogFile)}</strong>
                  <span>{t('fashionCatalog.editor.rowNumber', {
                    row: selectedRow.physicalIndex + 1
                  })}</span>
                </div>
                <label className="field-label" htmlFor="fashion-catalog-field">
                  {t('fashionCatalog.editor.field')}
                </label>
                <select
                  className="km-select-control"
                  id="fashion-catalog-field"
                  onChange={(event) => setSelectedField(event.target.value)}
                  value={field.field}
                >
                  {fields.map((candidate) => (
                    <option key={candidate.field} value={candidate.field}>
                      {candidate.label}
                      {candidate.readOnly ? ` - ${translateLiteral('Read-only')}` : ''}
                    </option>
                  ))}
                </select>

                <p className="field-hint">{t(field.hintKey)}</p>

                {field.readOnly ? (
                  <>
                    <label className="field-label" htmlFor="fashion-catalog-value">
                      {t('fashionCatalog.editor.value')}
                    </label>
                    <input
                      id="fashion-catalog-value"
                      readOnly
                      type="text"
                      value={getOptionLabel(
                        catalogFile,
                        field.field,
                        sourceValue,
                        presentation
                      )}
                    />
                  </>
                ) : field.valueKind === 'option' ? (
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
                      className="km-select-control"
                      id="fashion-catalog-value"
                      onChange={(event) => updateDraftValue(event.target.value)}
                      value={filteredOptions.includes(draftValue) ? draftValue : ''}
                    >
                      <option disabled={!field.optional} value="">
                        {field.optional
                          ? t('fashionCatalog.editor.clear')
                          : t('fashionCatalog.editor.chooseOption')}
                      </option>
                      {filteredOptions.map((option) => (
                        <option key={option} value={option}>
                          {getOptionLabel(
                            catalogFile,
                            field.field,
                            option,
                            presentation
                          )}
                        </option>
                      ))}
                    </select>
                    <p className="field-hint">
                      {t('fashionCatalog.editor.provenOptions', { count: options.length })}
                    </p>
                    {optionWindow.shownMatchingCount < optionWindow.matchingCount ? (
                      <p className="field-hint">
                        {t('fashionCatalog.editor.optionResultsLimited', {
                          count: optionWindow.matchingCount,
                          shown: optionWindow.shownMatchingCount
                        })}
                      </p>
                    ) : null}
                  </>
                ) : (
                  <>
                    <label className="field-label" htmlFor="fashion-catalog-value">
                      {t('fashionCatalog.editor.value')}
                    </label>
                    <input
                      aria-invalid={numericDraftError ? true : undefined}
                      id="fashion-catalog-value"
                      onChange={(event) => updateDraftValue(event.target.value)}
                      type="number"
                      value={draftValue}
                    />
                    {numericDraftError ? (
                      <p className="editable-field-error" role="alert">
                        {numericDraftError}
                      </p>
                    ) : null}
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
                  {field.optional && !field.readOnly ? (
                    <button
                      disabled={
                        isStaging ||
                        (sourceValue.length > 0 ? !workflow.canStage : draftValue.length === 0)
                      }
                      onClick={() => {
                        if (sourceValue.length === 0) {
                          updateDraftValue('');
                          setFeedback(null);
                          return;
                        }
                        void stage(true);
                      }}
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
    </FocusedEditorWorkspace>
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

function getSearchText(
  row: CatalogRecord,
  presentation: FashionCatalogPresentation,
  file: FashionCatalogFile
): string {
  if ('lineupId' in row) {
    return `${row.itemId} ${resolveLineupItemLabel(row.itemId, presentation, file)} ${row.lineupId} ${row.shopIds.join(' ')}`;
  }
  if ('modelVariant' in row) {
    return `${row.itemId} ${presentation.dressItemTitleById.get(String(row.itemId)) ?? ''} ${row.modelPart} ${row.modelVariant} ${row.primaryColorLabel} ${resolveTextLabel(row.primaryColorLabel, presentation)} ${row.secondaryColorLabel} ${resolveTextLabel(row.secondaryColorLabel, presentation)}`;
  }
  if ('displayLabel' in row) {
    return `${row.modelPart} ${row.displayLabel} ${resolveTextLabel(row.displayLabel, presentation)} ${row.displayOrder}`;
  }
  return `${row.itemId} ${presentation.hairItemTitleById.get(String(row.itemId)) ?? ''} ${row.modelKey} ${row.colorValue ?? ''} ${row.labelKey ?? ''} ${resolveTextLabel(row.labelKey, presentation)}`;
}

function getRowTitle(
  row: CatalogRecord,
  presentation: FashionCatalogPresentation,
  file: FashionCatalogFile
): string {
  if ('lineupId' in row) {
    return resolveLineupItemLabel(row.itemId, presentation, file);
  }
  if ('modelVariant' in row) {
    return presentation.dressItemTitleById.get(String(row.itemId))
      ?? `${row.itemId} · ${row.modelPart}`;
  }
  if ('displayLabel' in row) {
    return resolveTextLabel(row.displayLabel, presentation)
      || row.displayLabel
      || row.modelPart;
  }
  return presentation.hairItemTitleById.get(String(row.itemId))
    ?? `${row.itemId} · ${row.modelKey}`;
}

function getRowSubtitle(
  row: CatalogRecord,
  presentation: FashionCatalogPresentation,
  translateLiteral: TranslateLiteral
): string | null {
  if ('lineupId' in row) {
    const exactShopIds = row.shopIds.join(', ');
    return exactShopIds.length > 0
      ? `${translateLiteral('Shop')}: ${row.shopIds.length}`
      : String(row.entryPhysicalIndex + 1);
  }
  if ('modelVariant' in row) {
    return null;
  }
  if ('displayLabel' in row) {
    const catalogGroup = resolveDressUpCatalogGroupForModelPart(row.modelPart, presentation);
    if (catalogGroup) {
      return catalogGroup;
    }
    return row.modelPart;
  }
  return resolveHairCatalogTypeLabel(row.catalogTypeCode, presentation)
    ?? row.labelKey ?? row.colorValue ?? '';
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
      hintKey: getFieldHintKey(file, field),
      label: t(`fashionCatalog.fields.${field}`),
      readOnly: field === 'categoryCode' || field === 'colorVariantCode',
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
      hintKey: getFieldHintKey(file, field),
      label: t(`fashionCatalog.fields.${field}`),
      valueKind: valueKind as FieldDefinition['valueKind']
    }));
  }
  if (file === 'dressUpLineups' || file === 'hairAndMakeupLineups') {
    return [{
      field: 'itemId',
      hintKey: 'fashionCatalog.editor.safety',
      label: t('fashionCatalog.fields.itemId'),
      valueKind: 'option'
    }];
  }
  const definitions: Array<Omit<FieldDefinition, 'label'>> = [
    { field: 'itemId', hintKey: getFieldHintKey(file, 'itemId'), valueKind: 'number' },
    { field: 'modelKey', hintKey: getFieldHintKey(file, 'modelKey'), valueKind: 'option' },
    { field: 'catalogTypeCode', hintKey: getFieldHintKey(file, 'catalogTypeCode'), valueKind: 'option' },
    { field: 'colorValue', hintKey: getFieldHintKey(file, 'colorValue'), optional: true, valueKind: 'option' },
    { field: 'labelKey', hintKey: getFieldHintKey(file, 'labelKey'), optional: true, valueKind: 'option' },
    { field: 'displayOrder', hintKey: getFieldHintKey(file, 'displayOrder'), valueKind: 'number' },
    { field: 'groupCode', hintKey: getFieldHintKey(file, 'groupCode'), readOnly: true, valueKind: 'option' },
    { field: 'variantCode', hintKey: getFieldHintKey(file, 'variantCode'), readOnly: true, valueKind: 'option' }
  ];
  return definitions.map((definition) => ({
    ...definition,
    label: t(`fashionCatalog.fields.${definition.field}`)
  }));
}

function getFieldHintKey(file: FashionCatalogFile, field: string): string {
  if (
    field === 'categoryCode'
    || field === 'colorVariantCode'
    || field === 'groupCode'
    || field === 'variantCode'
  ) {
    return 'fieldHelp.catalog.raw.unverified';
  }
  if (
    field === 'displayLabel'
    || field === 'primaryColorLabel'
    || field === 'secondaryColorLabel'
    || field === 'labelKey'
  ) {
    return 'fashionCatalog.editor.safety';
  }
  if (field === 'catalogGroupCode' || field === 'catalogTypeCode') {
    return 'fashionCatalog.editor.safety';
  }
  if (field === 'modelPart' || field === 'modelVariant' || field === 'modelKey') {
    return 'fashionCatalog.editor.safety';
  }
  if ((file === 'dressUpLineups' || file === 'hairAndMakeupLineups') && field === 'itemId') {
    return 'fashionCatalog.editor.safety';
  }
  return 'fieldHelp.catalog.generic.value';
}

function createFashionCatalogPresentation(
  workflow: FashionCatalogWorkflow | null
): FashionCatalogPresentation {
  const textLabelByKey = new Map(
    workflow?.textLabels.map((label) => [label.key, label.label] as const) ?? []
  );
  const groupNamesByModelPart = new Map<string, Set<string>>();
  const catalogGroupCodesByModelPart = new Map<string, Set<string>>();
  const dressItemTitlesById = new Map<string, Set<string>>();
  const dressVariantNames = new Map<string, Set<string>>();
  const hairItemTitlesById = new Map<string, Set<string>>();
  const hairModelNames = new Map<string, Set<string>>();
  const colorNames = new Map<string, Set<string>>();

  for (const row of workflow?.dressUpGroups ?? []) {
    addCandidateLabel(
      groupNamesByModelPart,
      row.modelPart,
      textLabelByKey.get(row.displayLabel) ?? row.displayLabel
    );
  }
  const groupLabelByModelPart = createUniqueLabelMap(groupNamesByModelPart);

  for (const row of workflow?.dressUpItems ?? []) {
    addCandidateLabel(
      catalogGroupCodesByModelPart,
      row.modelPart,
      String(row.catalogGroupCode)
    );
    const groupLabel = groupLabelByModelPart.get(row.modelPart) ?? row.modelPart;
    const colorLabels = [row.primaryColorLabel, row.secondaryColorLabel]
      .map((key) => textLabelByKey.get(key) ?? key)
      .filter((label, index, labels) => label.length > 0 && labels.indexOf(label) === index);
    const label = colorLabels.length > 0
      ? `${groupLabel} - ${colorLabels.join(' / ')} (#${row.itemId})`
      : `${groupLabel} (#${row.itemId})`;
    addCandidateLabel(dressItemTitlesById, String(row.itemId), label);
    addCandidateLabel(dressVariantNames, row.modelVariant, label);
  }

  for (const row of workflow?.hairAndMakeup ?? []) {
    const resolvedName = row.labelKey
      ? textLabelByKey.get(row.labelKey) ?? row.labelKey
      : '';
    addCandidateLabel(
      hairItemTitlesById,
      String(row.itemId),
      resolvedName.length > 0 ? `${resolvedName} (#${row.itemId})` : `#${row.itemId}`
    );
    if (resolvedName.length > 0) {
      addCandidateLabel(hairModelNames, row.modelKey, resolvedName);
      if (row.colorValue) {
        addCandidateLabel(colorNames, row.colorValue, resolvedName);
      }
    }
  }

  return {
    catalogGroupCodeByModelPart: createUniqueLabelMap(catalogGroupCodesByModelPart),
    colorLabelByValue: createUniqueLabelMap(colorNames),
    dressItemTitleById: createUniqueLabelMap(dressItemTitlesById),
    dressVariantLabelByValue: createUniqueLabelMap(dressVariantNames),
    groupLabelByModelPart,
    hairItemTitleById: createUniqueLabelMap(hairItemTitlesById),
    hairModelLabelByValue: createUniqueLabelMap(hairModelNames),
    textLabelByKey
  };
}

function addCandidateLabel(
  candidates: Map<string, Set<string>>,
  value: string,
  label: string
) {
  const labels = candidates.get(value) ?? new Set<string>();
  labels.add(label);
  candidates.set(value, labels);
}

function createUniqueLabelMap(
  candidates: ReadonlyMap<string, ReadonlySet<string>>
): ReadonlyMap<string, string> {
  return new Map(
    [...candidates]
      .filter(([, labels]) => labels.size === 1)
      .map(([value, labels]) => [value, [...labels][0]])
  );
}

function getOptionLabel(
  file: FashionCatalogFile,
  field: string,
  value: string,
  presentation: FashionCatalogPresentation
): string {
  if ((file === 'dressUpLineups' || file === 'hairAndMakeupLineups') && field === 'itemId') {
    return resolveLineupItemLabel(Number(value), presentation, file);
  }
  if (field === 'modelPart') {
    return presentation.groupLabelByModelPart.get(value)
      ?? value;
  }
  if (field === 'modelVariant') {
    return presentation.dressVariantLabelByValue.get(value)
      ?? value;
  }
  if (
    field === 'primaryColorLabel'
    || field === 'secondaryColorLabel'
    || field === 'displayLabel'
    || field === 'labelKey'
  ) {
    return presentation.textLabelByKey.get(value)
      ?? value;
  }
  if (field === 'catalogGroupCode') {
    return resolveCatalogGroupLabel(value, presentation)
      ?? value;
  }
  if (field === 'catalogTypeCode') {
    return resolveHairCatalogTypeLabel(Number(value), presentation)
      ?? value;
  }
  if (field === 'colorValue') {
    const colorName = presentation.colorLabelByValue.get(value);
    return colorName ? `${colorName} - ${value}` : value;
  }
  if (field === 'modelKey') {
    const modelName = presentation.hairModelLabelByValue.get(value);
    return modelName ? `${modelName} - ${value}` : value;
  }
  if (
    field === 'categoryCode'
    || field === 'colorVariantCode'
    || field === 'groupCode'
    || field === 'variantCode'
  ) {
    return value;
  }
  return value;
}

function resolveLineupItemLabel(
  itemId: number,
  presentation: FashionCatalogPresentation,
  file: FashionCatalogFile
): string {
  const key = String(itemId);
  if (file === 'dressUpLineups') {
    return presentation.dressItemTitleById.get(key) ?? `#${itemId}`;
  }
  if (file === 'hairAndMakeupLineups') {
    return presentation.hairItemTitleById.get(key) ?? `#${itemId}`;
  }
  return `#${itemId}`;
}

function resolveTextLabel(
  key: string | null | undefined,
  presentation: FashionCatalogPresentation
): string {
  return key ? presentation.textLabelByKey.get(key) ?? '' : '';
}

function resolveCatalogGroupLabel(
  code: string,
  presentation: FashionCatalogPresentation
): string | null {
  const key = dressUpCatalogGroupTextKeys[code];
  return key ? presentation.textLabelByKey.get(key) ?? null : null;
}

function resolveDressUpCatalogGroupForModelPart(
  modelPart: string,
  presentation: FashionCatalogPresentation
): string | null {
  const code = presentation.catalogGroupCodeByModelPart.get(modelPart);
  return code ? resolveCatalogGroupLabel(code, presentation) : null;
}

function resolveHairCatalogTypeLabel(
  code: number,
  presentation: FashionCatalogPresentation
): string | null {
  const key = hairCatalogTypeTextKeys[String(code)];
  return key ? presentation.textLabelByKey.get(key) ?? null : null;
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
