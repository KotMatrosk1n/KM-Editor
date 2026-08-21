/* SPDX-License-Identifier: GPL-3.0-only */

import {
  ArrowDown,
  ArrowUp,
  BarChart3,
  Search,
  SlidersHorizontal,
  X
} from 'lucide-react';
import {
  useEffect,
  useMemo,
  useRef,
  useState,
  type CSSProperties
} from 'react';
import type {
  GameModuleFact,
  GameModuleRecord,
  QueryGameModuleResponse
} from '../../bridge/gameModuleContracts';
import { useLocalization } from '../../localization';
import {
  humanizeIdentifier,
  presentFactValue,
  presentationFactLabelKey,
  relativeRecordTitle
} from '../workbench/analysisPresentationUtils';
import { ConfidenceBadge } from './GameModuleResults';

type RecordPickerOrder = 'titleAscending' | 'titleDescending' | 'recordKind';
type ComparisonOrder =
  | 'custom'
  | 'titleAscending'
  | 'titleDescending'
  | 'recordKind'
  | 'measureAscending'
  | 'measureDescending';

type ComparisonField = {
  fieldKey: string;
  identity: string;
  label: string;
  numeric: boolean;
  providerId: string;
  recordCount: number;
  unit: string | null;
};

type NumericFact = {
  fact: GameModuleFact;
  value: number;
};

const numericKinds = new Set(['signedInteger', 'unsignedInteger', 'decimal']);
const identityFieldKeysByRecordKind: Readonly<Record<string, readonly string[]>> = {
  moveVariant: ['variant'],
  moveVariantSet: ['moveId'],
  raidReward: ['slot'],
  raidRewardTable: ['tableIndex'],
  scriptedBossPhase: ['battleStage', 'hpPhase'],
  spawnSlot: ['slot']
};

export function GameModuleComparison({
  response
}: {
  response: QueryGameModuleResponse;
}) {
  const { formatLocale, t, translateLiteral } = useLocalization();
  const [comparisonOrder, setComparisonOrder] = useState<ComparisonOrder>('custom');
  const [fieldIdentities, setFieldIdentities] = useState<string[]>([]);
  const [measureIdentity, setMeasureIdentity] = useState<string>('');
  const [recordKind, setRecordKind] = useState('');
  const [recordPickerOrder, setRecordPickerOrder] = useState<RecordPickerOrder>(
    'titleAscending'
  );
  const [recordSearch, setRecordSearch] = useState('');
  const [selectedRecordIds, setSelectedRecordIds] = useState<string[]>([]);
  const initializedFieldsForQuery = useRef<string | null>(null);
  const comparisonCollator = useMemo(() => new Intl.Collator(formatLocale, {
    numeric: true,
    sensitivity: 'base'
  }), [formatLocale]);
  const recordById = useMemo(
    () => new Map(response.records.map((record) => [record.recordId, record])),
    [response.records]
  );
  const recordLabels = useMemo(
    () => buildRecordDisplayLabels(response.records),
    [response.records]
  );
  const recordKinds = useMemo(() => (
    Array.from(new Set(response.records.map((record) => record.recordKind)))
      .sort(comparisonCollator.compare)
  ), [comparisonCollator, response.records]);
  const visibleRecords = useMemo(() => {
    const normalizedSearch = recordSearch.trim().toLowerCase();
    return [...response.records]
      .filter((record) => (
        (!recordKind || record.recordKind === recordKind) &&
        (!normalizedSearch || searchableRecordText(
          record,
          recordDisplayLabel(record, recordLabels)
        ).includes(normalizedSearch))
      ))
      .sort(recordPickerComparator(recordPickerOrder, recordLabels, comparisonCollator));
  }, [
    comparisonCollator,
    recordKind,
    recordLabels,
    recordPickerOrder,
    recordSearch,
    response.records
  ]);
  const selectedRecords = useMemo(() => (
    selectedRecordIds.flatMap((recordId) => {
      const record = recordById.get(recordId);
      return record ? [record] : [];
    })
  ), [recordById, selectedRecordIds]);
  const availableFields = useMemo(
    () => collectComparisonFields(selectedRecords, comparisonCollator),
    [comparisonCollator, selectedRecords]
  );
  const fieldByIdentity = useMemo(
    () => new Map(availableFields.map((field) => [field.identity, field])),
    [availableFields]
  );
  const selectedFields = useMemo(() => (
    fieldIdentities.flatMap((identity) => {
      const field = fieldByIdentity.get(identity);
      return field ? [field] : [];
    })
  ), [fieldByIdentity, fieldIdentities]);
  const numericFields = useMemo(
    () => availableFields.filter((field) => field.numeric),
    [availableFields]
  );
  const fieldLabels = useMemo(
    () => buildFieldDisplayLabels(
      availableFields,
      (field) => {
        const labelKey = presentationFactLabelKey(field.label);
        const label = labelKey ? t(labelKey) : field.label;
        return field.unit ? `${label} (${translateLiteral(field.unit)})` : label;
      }
    ),
    [availableFields, t, translateLiteral]
  );
  const measure = fieldByIdentity.get(measureIdentity) ?? null;
  const orderedRecords = useMemo(() => (
    orderComparisonRecords(
      selectedRecords,
      comparisonOrder,
      measure,
      recordLabels,
      comparisonCollator
    )
  ), [comparisonCollator, comparisonOrder, measure, recordLabels, selectedRecords]);
  const displayedSelectedRecords = comparisonOrder === 'custom'
    ? selectedRecords
    : orderedRecords;

  useEffect(() => {
    setSelectedRecordIds((current) => current.filter((recordId) => recordById.has(recordId)));
  }, [recordById]);

  useEffect(() => {
    setFieldIdentities((current) => current.filter((identity) => fieldByIdentity.has(identity)));
    setMeasureIdentity((current) => (
      current && fieldByIdentity.get(current)?.numeric ? current : numericFields[0]?.identity ?? ''
    ));
  }, [fieldByIdentity, numericFields]);

  useEffect(() => {
    if (
      !measure &&
      (comparisonOrder === 'measureAscending' || comparisonOrder === 'measureDescending')
    ) {
      setComparisonOrder('custom');
    }
  }, [comparisonOrder, measure]);

  useEffect(() => {
    if (selectedRecords.length === 0) {
      initializedFieldsForQuery.current = null;
      setFieldIdentities([]);
      setMeasureIdentity('');
      return;
    }
    if (initializedFieldsForQuery.current === response.queryFingerprint) return;
    initializedFieldsForQuery.current = response.queryFingerprint;
    const common = availableFields.filter(
      (field) => field.recordCount === selectedRecords.length
    );
    const defaults = (common.length > 0 ? common : availableFields).slice(0, 6);
    setFieldIdentities(defaults.map((field) => field.identity));
    setMeasureIdentity(
      defaults.find((field) => field.numeric)?.identity ?? numericFields[0]?.identity ?? ''
    );
  }, [availableFields, numericFields, response.queryFingerprint, selectedRecords.length]);

  const toggleRecord = (recordId: string, checked: boolean) => {
    setSelectedRecordIds((current) => {
      if (checked) return current.includes(recordId) ? current : [...current, recordId];
      return current.filter((candidate) => candidate !== recordId);
    });
  };
  const toggleField = (identity: string, checked: boolean) => {
    setFieldIdentities((current) => {
      if (checked) return current.includes(identity) ? current : [...current, identity];
      return current.filter((candidate) => candidate !== identity);
    });
  };
  const moveSelectedRecord = (recordId: string, direction: -1 | 1) => {
    setSelectedRecordIds((current) => {
      const index = current.indexOf(recordId);
      const destination = index + direction;
      if (index < 0 || destination < 0 || destination >= current.length) return current;
      const reordered = [...current];
      [reordered[index], reordered[destination]] = [
        reordered[destination]!,
        reordered[index]!
      ];
      return reordered;
    });
  };
  const selectVisible = () => {
    setSelectedRecordIds((current) => {
      const selected = new Set(current);
      return [
        ...current,
        ...visibleRecords
          .map((record) => record.recordId)
          .filter((recordId) => !selected.has(recordId))
      ];
    });
  };
  const selectCommonFields = () => {
    setFieldIdentities(
      availableFields
        .filter((field) => field.recordCount === selectedRecords.length)
        .map((field) => field.identity)
    );
  };

  return (
    <section
      aria-labelledby="game-module-compare-title"
      className="km-game-module-compare"
    >
      <header className="km-game-module-compare-heading">
        <div>
          <span className="km-game-module-compare-icon" aria-hidden="true">
            <SlidersHorizontal size={18} />
          </span>
          <div>
            <h3 id="game-module-compare-title">{t('gameModules.compare.title')}</h3>
            <p>{t('gameModules.compare.description')}</p>
          </div>
        </div>
        <p className="km-game-module-compare-boundary">
          {t('gameModules.compare.loadedBoundary', {
            loaded: response.records.length,
            total: response.totalRecordCount
          })}
        </p>
      </header>

      <div className="km-game-module-record-workspace">
        <section aria-labelledby="game-module-record-picker-title">
          <header>
            <div>
              <h4 id="game-module-record-picker-title">
                {t('gameModules.compare.records.title')}
              </h4>
              <p>{t('gameModules.compare.records.description')}</p>
            </div>
            <strong>
              {t('gameModules.compare.records.visibleCount', {
                count: visibleRecords.length
              })}
            </strong>
          </header>
          <div className="km-game-module-record-filters">
            <label>
              <span>{t('gameModules.compare.records.search')}</span>
              <span className="km-game-module-search-control">
                <Search aria-hidden="true" size={15} />
                <input
                  className="km-input-control"
                  onChange={(event) => setRecordSearch(event.currentTarget.value)}
                  placeholder={t('gameModules.compare.records.searchPlaceholder')}
                  type="search"
                  value={recordSearch}
                />
              </span>
            </label>
            <label>
              <span>{t('gameModules.compare.records.type')}</span>
              <select
                className="km-select-control"
                onChange={(event) => setRecordKind(event.currentTarget.value)}
                value={recordKind}
              >
                <option value="">{t('gameModules.compare.records.allTypes')}</option>
                {recordKinds.map((kind) => (
                  <option data-localization-ignore="true" key={kind} value={kind}>
                    {humanizeIdentifier(kind)}
                  </option>
                ))}
              </select>
            </label>
            <label>
              <span>{t('gameModules.compare.records.order')}</span>
              <select
                className="km-select-control"
                onChange={(event) => setRecordPickerOrder(
                  event.currentTarget.value as RecordPickerOrder
                )}
                value={recordPickerOrder}
              >
                <option value="titleAscending">
                  {t('gameModules.compare.order.titleAscending')}
                </option>
                <option value="titleDescending">
                  {t('gameModules.compare.order.titleDescending')}
                </option>
                <option value="recordKind">
                  {t('gameModules.compare.order.recordKind')}
                </option>
              </select>
            </label>
          </div>
          <div className="km-game-module-selection-actions">
            <button
              className="secondary-button compact-button"
              disabled={visibleRecords.length === 0}
              onClick={selectVisible}
              type="button"
            >
              {t('gameModules.compare.records.selectVisible')}
            </button>
            <button
              className="secondary-button compact-button"
              disabled={selectedRecordIds.length === 0}
              onClick={() => setSelectedRecordIds([])}
              type="button"
            >
              {t('gameModules.compare.records.clear')}
            </button>
          </div>
          <ul
            aria-label={t('gameModules.compare.records.availableLabel')}
            className="km-game-module-record-picker"
          >
            {visibleRecords.map((record) => {
              const checked = selectedRecordIds.includes(record.recordId);
              const displayLabel = recordDisplayLabel(record, recordLabels);
              return (
                <li key={record.recordId}>
                  <label data-selected={checked || undefined}>
                    <input
                      checked={checked}
                      className="km-choice-control"
                      onChange={(event) => toggleRecord(
                        record.recordId,
                        event.currentTarget.checked
                      )}
                      type="checkbox"
                    />
                    <span data-localization-ignore="true">
                      <strong>{displayLabel}</strong>
                      <small>{humanizeIdentifier(record.recordKind)}</small>
                    </span>
                  </label>
                </li>
              );
            })}
          </ul>
        </section>

        <section aria-labelledby="game-module-selected-records-title">
          <header>
            <div>
              <h4 id="game-module-selected-records-title">
                {t('gameModules.compare.selected.title')}
              </h4>
              <p>{t('gameModules.compare.selected.description')}</p>
            </div>
            <strong aria-live="polite">
              {t('gameModules.compare.selected.count', {
                count: selectedRecords.length
              })}
            </strong>
          </header>
          <label className="km-game-module-comparison-order">
            <span>{t('gameModules.compare.selected.order')}</span>
            <select
              className="km-select-control"
              onChange={(event) => setComparisonOrder(
                event.currentTarget.value as ComparisonOrder
              )}
              value={comparisonOrder}
            >
              <option value="custom">{t('gameModules.compare.order.custom')}</option>
              <option value="titleAscending">
                {t('gameModules.compare.order.titleAscending')}
              </option>
              <option value="titleDescending">
                {t('gameModules.compare.order.titleDescending')}
              </option>
              <option value="recordKind">
                {t('gameModules.compare.order.recordKind')}
              </option>
              {measure ? (
                <>
                  <option value="measureAscending">
                    {t('gameModules.compare.order.measureAscending')}
                  </option>
                  <option value="measureDescending">
                    {t('gameModules.compare.order.measureDescending')}
                  </option>
                </>
              ) : null}
            </select>
          </label>
          {selectedRecords.length > 0 ? (
            <ol className="km-game-module-selected-records">
              {displayedSelectedRecords.map((record, index) => {
                const displayLabel = recordDisplayLabel(record, recordLabels);
                return (
                  <li key={record.recordId}>
                    <span data-localization-ignore="true">
                      <strong>{displayLabel}</strong>
                      <small>{humanizeIdentifier(record.recordKind)}</small>
                    </span>
                    <div>
                      <button
                        aria-label={t('gameModules.compare.selected.moveUp', {
                          name: displayLabel
                        })}
                        className="secondary-button compact-button icon-only-button"
                        disabled={comparisonOrder !== 'custom' || index === 0}
                        onClick={() => moveSelectedRecord(record.recordId, -1)}
                        type="button"
                      >
                        <ArrowUp aria-hidden="true" size={14} />
                      </button>
                      <button
                        aria-label={t('gameModules.compare.selected.moveDown', {
                          name: displayLabel
                        })}
                        className="secondary-button compact-button icon-only-button"
                        disabled={
                          comparisonOrder !== 'custom' ||
                          index === displayedSelectedRecords.length - 1
                        }
                        onClick={() => moveSelectedRecord(record.recordId, 1)}
                        type="button"
                      >
                        <ArrowDown aria-hidden="true" size={14} />
                      </button>
                      <button
                        aria-label={t('gameModules.compare.selected.remove', {
                          name: displayLabel
                        })}
                        className="secondary-button compact-button icon-only-button"
                        onClick={() => toggleRecord(record.recordId, false)}
                        type="button"
                      >
                        <X aria-hidden="true" size={14} />
                      </button>
                    </div>
                  </li>
                );
              })}
            </ol>
          ) : (
            <p className="km-workbench-empty">
              {t('gameModules.compare.selected.empty')}
            </p>
          )}
        </section>
      </div>

      {selectedRecords.length > 0 ? (
        <>
          <section
            aria-labelledby="game-module-field-picker-title"
            className="km-game-module-field-workspace"
          >
            <header>
              <div>
                <h4 id="game-module-field-picker-title">
                  {t('gameModules.compare.fields.title')}
                </h4>
                <p>{t('gameModules.compare.fields.description')}</p>
              </div>
              <strong>
                {t('gameModules.compare.fields.count', { count: selectedFields.length })}
              </strong>
            </header>
            <div className="km-game-module-selection-actions">
              <button
                className="secondary-button compact-button"
                disabled={!availableFields.some(
                  (field) => field.recordCount === selectedRecords.length
                )}
                onClick={selectCommonFields}
                type="button"
              >
                {t('gameModules.compare.fields.selectCommon')}
              </button>
              <button
                className="secondary-button compact-button"
                disabled={availableFields.length === 0}
                onClick={() => setFieldIdentities(
                  availableFields.map((field) => field.identity)
                )}
                type="button"
              >
                {t('gameModules.compare.fields.selectAll')}
              </button>
              <button
                className="secondary-button compact-button"
                disabled={fieldIdentities.length === 0}
                onClick={() => setFieldIdentities([])}
                type="button"
              >
                {t('gameModules.compare.fields.clear')}
              </button>
            </div>
            <ul
              aria-label={t('gameModules.compare.fields.availableLabel')}
              className="km-game-module-field-picker"
            >
              {availableFields.map((field) => {
                const labelKey = presentationFactLabelKey(field.label);
                const displayLabel = fieldDisplayLabel(field, fieldLabels);
                return (
                  <li key={field.identity}>
                    <label data-selected={fieldIdentities.includes(field.identity) || undefined}>
                      <input
                        aria-label={displayLabel}
                        checked={fieldIdentities.includes(field.identity)}
                        className="km-choice-control"
                        onChange={(event) => toggleField(
                          field.identity,
                          event.currentTarget.checked
                        )}
                        type="checkbox"
                      />
                      <span>
                        <strong data-localization-ignore={!labelKey || undefined}>
                          {displayLabel}
                        </strong>
                        <small>
                          {t('gameModules.compare.fields.availability', {
                            available: field.recordCount,
                            selected: selectedRecords.length
                          })}
                        </small>
                      </span>
                    </label>
                  </li>
                );
              })}
            </ul>
          </section>

          {selectedFields.length > 0 ? (
            <ComparisonTable
              fields={selectedFields}
              fieldLabels={fieldLabels}
              recordLabels={recordLabels}
              records={orderedRecords}
            />
          ) : (
            <p className="km-workbench-empty">
              {t('gameModules.compare.fields.empty')}
            </p>
          )}

          {numericFields.length > 0 ? (
            <section
              aria-labelledby="game-module-chart-title"
              className="km-game-module-chart"
            >
              <header>
                <div>
                  <BarChart3 aria-hidden="true" size={18} />
                  <div>
                    <h4 id="game-module-chart-title">{t('gameModules.compare.chart.title')}</h4>
                    <p>{t('gameModules.compare.chart.description')}</p>
                  </div>
                </div>
                <label>
                  <span>{t('gameModules.compare.chart.measure')}</span>
                  <select
                    className="km-select-control"
                    onChange={(event) => setMeasureIdentity(event.currentTarget.value)}
                    value={measureIdentity}
                  >
                    {numericFields.map((field) => {
                      const labelKey = presentationFactLabelKey(field.label);
                      return (
                        <option
                          data-localization-ignore={!labelKey || undefined}
                          key={field.identity}
                          value={field.identity}
                        >
                          {fieldDisplayLabel(field, fieldLabels)}
                        </option>
                      );
                    })}
                  </select>
                </label>
              </header>
              {measure ? (
                <NumericComparisonChart
                  field={measure}
                  recordLabels={recordLabels}
                  records={orderedRecords}
                  translateLiteral={translateLiteral}
                />
              ) : null}
            </section>
          ) : null}
        </>
      ) : null}
    </section>
  );
}

function ComparisonTable({
  fieldLabels,
  fields,
  recordLabels,
  records
}: {
  fieldLabels: ReadonlyMap<string, string>;
  fields: readonly ComparisonField[];
  recordLabels: ReadonlyMap<string, string>;
  records: readonly GameModuleRecord[];
}) {
  const { t, translateLiteral } = useLocalization();
  return (
    <section aria-labelledby="game-module-comparison-table-title">
      <h4 className="km-game-module-subsection-title" id="game-module-comparison-table-title">
        {t('gameModules.compare.table.title')}
      </h4>
      <div
        aria-label={t('gameModules.compare.table.title')}
        className="km-game-module-comparison-table-wrap"
        role="region"
        tabIndex={0}
      >
        <table className="km-game-module-comparison-table">
          <thead>
            <tr>
              <th scope="col">{t('gameModules.compare.table.record')}</th>
              {fields.map((field) => {
                const labelKey = presentationFactLabelKey(field.label);
                return (
                  <th
                    data-localization-ignore={!labelKey || undefined}
                    key={field.identity}
                    scope="col"
                  >
                    <span>{fieldDisplayLabel(field, fieldLabels)}</span>
                  </th>
                );
              })}
            </tr>
          </thead>
          <tbody>
            {records.map((record) => (
              <tr key={record.recordId}>
                <th data-localization-ignore="true" scope="row">
                  <span>{recordDisplayLabel(record, recordLabels)}</span>
                  <small>{humanizeIdentifier(record.recordKind)}</small>
                </th>
                {fields.map((field) => {
                  const fact = findFieldFact(record, field);
                  if (!fact) {
                    return (
                      <td className="is-unavailable" key={field.identity}>
                        {t('gameModules.compare.table.unavailable')}
                      </td>
                    );
                  }
                  const value = presentFactValue(
                    fact.label,
                    fact.value.displayValue,
                    fact.unit,
                    translateLiteral
                  );
                  return (
                    <td data-localization-ignore="true" key={field.identity}>
                      <span>{value.displayValue}</span>
                      {value.unit ? <small>{value.unit}</small> : null}
                      <ConfidenceBadge confidence={fact.confidence} />
                    </td>
                  );
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

function NumericComparisonChart({
  field,
  recordLabels,
  records,
  translateLiteral
}: {
  field: ComparisonField;
  recordLabels: ReadonlyMap<string, string>;
  records: readonly GameModuleRecord[];
  translateLiteral: (value: string) => string;
}) {
  const { t } = useLocalization();
  const points = records.map((record) => ({
    numeric: numericFieldFact(record, field),
    record
  }));
  const values = points.flatMap(({ numeric }) => numeric ? [numeric.value] : []);
  const minimum = Math.min(0, ...values);
  const maximum = Math.max(0, ...values);
  const span = Math.max(maximum - minimum, 1);
  const zero = ((0 - minimum) / span) * 100;
  return (
    <div className="km-game-module-chart-plot">
      <div className="km-game-module-chart-axis" aria-hidden="true">
        <span>{formatNumericAxisValue(minimum)}</span>
        <span>{formatNumericAxisValue(maximum)}</span>
      </div>
      <ol>
        {points.map(({ numeric, record }) => {
          const presented = numeric
            ? presentFactValue(
                numeric.fact.label,
                numeric.fact.value.displayValue,
                numeric.fact.unit,
                translateLiteral
              )
            : null;
          const valuePosition = numeric
            ? ((numeric.value - minimum) / span) * 100
            : zero;
          const style = {
            '--km-game-module-bar-left': `${Math.min(zero, valuePosition)}%`,
            '--km-game-module-bar-width': `${Math.max(Math.abs(valuePosition - zero), 0.25)}%`,
            '--km-game-module-zero': `${zero}%`
          } as CSSProperties;
          return (
            <li key={record.recordId} style={style}>
              <span data-localization-ignore="true">
                <strong>{recordDisplayLabel(record, recordLabels)}</strong>
                <small>{humanizeIdentifier(record.recordKind)}</small>
              </span>
              <div className="km-game-module-chart-track">
                <i aria-hidden="true" />
                {numeric ? <b aria-hidden="true" /> : null}
              </div>
              <span data-localization-ignore={numeric ? true : undefined}>
                {presented ? (
                  <>
                    {presented.displayValue}
                    {presented.unit ? <small>{presented.unit}</small> : null}
                  </>
                ) : t('gameModules.compare.table.unavailable')}
              </span>
            </li>
          );
        })}
      </ol>
    </div>
  );
}

function collectComparisonFields(
  records: readonly GameModuleRecord[],
  collator: Intl.Collator
): ComparisonField[] {
  const fields = new Map<string, {
    fieldKey: string;
    identity: string;
    labelCounts: Map<string, number>;
    numeric: boolean;
    providerId: string;
    recordIds: Set<string>;
    unit: string | null;
  }>();
  for (const record of records) {
    for (const fact of record.facts) {
      const identity = factIdentity(fact);
      const existing = fields.get(identity);
      if (existing) {
        existing.labelCounts.set(
          fact.label,
          (existing.labelCounts.get(fact.label) ?? 0) + 1
        );
        existing.numeric ||= numericFactValue(fact) !== null;
        existing.recordIds.add(record.recordId);
        continue;
      }
      fields.set(identity, {
        fieldKey: fact.fieldKey,
        identity,
        labelCounts: new Map([[fact.label, 1]]),
        numeric: numericFactValue(fact) !== null,
        providerId: fact.providerId,
        recordIds: new Set([record.recordId]),
        unit: fact.unit
      });
    }
  }
  return Array.from(fields.values())
    .map(({ labelCounts, recordIds, ...field }) => ({
      ...field,
      label: representativeFieldLabel(labelCounts, collator),
      recordCount: recordIds.size
    }))
    .sort((left, right) => (
      Number(right.numeric) - Number(left.numeric) ||
      collator.compare(left.label, right.label) ||
      collator.compare(left.identity, right.identity)
    ));
}

function buildFieldDisplayLabels(
  fields: readonly ComparisonField[],
  baseLabel: (field: ComparisonField) => string
) {
  const baseLabels = new Map(fields.map((field) => [field.identity, baseLabel(field)]));
  const baseCounts = occurrenceCounts(
    fields.map((field) => fieldDisplayLabel(field, baseLabels).toLowerCase())
  );
  const keyedLabels = new Map(fields.map((field) => {
    const base = fieldDisplayLabel(field, baseLabels);
    return [
      field.identity,
      (baseCounts.get(base.toLowerCase()) ?? 0) > 1
        ? `${base} [${field.fieldKey}]`
        : base
    ];
  }));
  const keyedCounts = occurrenceCounts(
    fields.map((field) => fieldDisplayLabel(field, keyedLabels).toLowerCase())
  );
  const providerLabels = new Map(fields.map((field) => {
    const keyed = fieldDisplayLabel(field, keyedLabels);
    return [
      field.identity,
      (keyedCounts.get(keyed.toLowerCase()) ?? 0) > 1
        ? `${keyed} [${field.providerId}]`
        : keyed
    ];
  }));
  return ensureUniqueFieldLabels(fields, providerLabels);
}

function representativeFieldLabel(
  labelCounts: ReadonlyMap<string, number>,
  collator: Intl.Collator
) {
  return Array.from(labelCounts.entries())
    .sort((left, right) => (
      right[1] - left[1] ||
      collator.compare(left[0], right[0]) ||
      compareCodeUnits(left[0], right[0])
    ))[0]?.[0] ?? '';
}

function ensureUniqueFieldLabels(
  fields: readonly ComparisonField[],
  labels: Map<string, string>
) {
  for (let attempt = 0; attempt <= fields.length; attempt += 1) {
    const groups = new Map<string, ComparisonField[]>();
    for (const field of fields) {
      const key = fieldDisplayLabel(field, labels).toLowerCase();
      const group = groups.get(key);
      if (group) group.push(field);
      else groups.set(key, [field]);
    }
    const collisions = Array.from(groups.values()).filter((group) => group.length > 1);
    if (collisions.length === 0) {
      verifyUniqueFieldLabels(fields, labels);
      return labels;
    }
    for (const group of collisions) {
      const tokens = group.map((field) => safeIdentityToken(field.identity));
      for (const field of group) {
        labels.set(
          field.identity,
          `${fieldDisplayLabel(field, labels)} - #${shortestUniqueTokenSuffix(
            safeIdentityToken(field.identity),
            tokens
          )}`
        );
      }
    }
  }
  for (const field of fields) {
    labels.set(
      field.identity,
      `${fieldDisplayLabel(field, labels)} - #${safeIdentityToken(field.identity)}`
    );
  }
  verifyUniqueFieldLabels(fields, labels);
  return labels;
}

function verifyUniqueFieldLabels(
  fields: readonly ComparisonField[],
  labels: ReadonlyMap<string, string>
) {
  const normalized = fields.map((field) => (
    fieldDisplayLabel(field, labels).toLowerCase()
  ));
  if (new Set(normalized).size !== fields.length) {
    throw new Error('Game module comparison field labels are not unique.');
  }
}

function fieldDisplayLabel(
  field: ComparisonField,
  labels: ReadonlyMap<string, string>
) {
  return labels.get(field.identity) ??
    `${field.label}${field.unit ? ` (${field.unit})` : ''} [${field.fieldKey}]`;
}

function findFieldFact(record: GameModuleRecord, field: ComparisonField) {
  return record.facts.find((fact) => factIdentity(fact) === field.identity) ?? null;
}

function numericFieldFact(
  record: GameModuleRecord,
  field: ComparisonField | null
): NumericFact | null {
  if (!field) return null;
  const fact = findFieldFact(record, field);
  if (!fact) return null;
  const value = numericFactValue(fact);
  return value === null ? null : { fact, value };
}

function numericFactValue(fact: GameModuleFact) {
  if (!numericKinds.has(fact.value.kind) || fact.value.canonicalValue === null) return null;
  const value = Number(fact.value.canonicalValue);
  if (!Number.isFinite(value)) return null;
  if (
    (fact.value.kind === 'signedInteger' || fact.value.kind === 'unsignedInteger') &&
    !Number.isSafeInteger(value)
  ) return null;
  return value;
}

function factIdentity(fact: GameModuleFact) {
  return JSON.stringify([fact.providerId, fact.fieldKey, fact.unit]);
}

function compareCodeUnits(left: string, right: string) {
  if (left === right) return 0;
  return left < right ? -1 : 1;
}

function orderComparisonRecords(
  records: readonly GameModuleRecord[],
  order: ComparisonOrder,
  measure: ComparisonField | null,
  recordLabels: ReadonlyMap<string, string>,
  collator: Intl.Collator
) {
  if (order === 'custom') return records;
  const sorted = [...records];
  switch (order) {
    case 'titleAscending':
      return sorted.sort(recordTitleComparator(1, recordLabels, collator));
    case 'titleDescending':
      return sorted.sort(recordTitleComparator(-1, recordLabels, collator));
    case 'recordKind':
      return sorted.sort((left, right) => (
        collator.compare(left.recordKind, right.recordKind) ||
        recordTitleComparator(1, recordLabels, collator)(left, right)
      ));
    case 'measureAscending':
      return sorted.sort(measureComparator(measure, 1, recordLabels, collator));
    case 'measureDescending':
      return sorted.sort(measureComparator(measure, -1, recordLabels, collator));
    default:
      return records;
  }
}

function recordPickerComparator(
  order: RecordPickerOrder,
  recordLabels: ReadonlyMap<string, string>,
  collator: Intl.Collator
) {
  switch (order) {
    case 'titleDescending':
      return recordTitleComparator(-1, recordLabels, collator);
    case 'recordKind':
      return (left: GameModuleRecord, right: GameModuleRecord) => (
        collator.compare(left.recordKind, right.recordKind) ||
        recordTitleComparator(1, recordLabels, collator)(left, right)
      );
    default:
      return recordTitleComparator(1, recordLabels, collator);
  }
}

function recordTitleComparator(
  direction: 1 | -1,
  recordLabels: ReadonlyMap<string, string>,
  collator: Intl.Collator
) {
  return (left: GameModuleRecord, right: GameModuleRecord) => (
    collator.compare(
      recordDisplayLabel(left, recordLabels),
      recordDisplayLabel(right, recordLabels)
    ) * direction ||
    collator.compare(left.recordKind, right.recordKind) ||
    collator.compare(left.recordId, right.recordId)
  );
}

function measureComparator(
  field: ComparisonField | null,
  direction: 1 | -1,
  recordLabels: ReadonlyMap<string, string>,
  collator: Intl.Collator
) {
  return (left: GameModuleRecord, right: GameModuleRecord) => {
    const leftValue = numericFieldFact(left, field)?.value ?? null;
    const rightValue = numericFieldFact(right, field)?.value ?? null;
    if (leftValue === null && rightValue === null) {
      return recordTitleComparator(1, recordLabels, collator)(left, right);
    }
    if (leftValue === null) return 1;
    if (rightValue === null) return -1;
    return (leftValue - rightValue) * direction ||
      recordTitleComparator(1, recordLabels, collator)(left, right);
  };
}

function buildRecordDisplayLabels(records: readonly GameModuleRecord[]) {
  const recordsById = new Map(records.map((record) => [record.recordId, record]));
  const candidates = records.map((record) => {
    const parent = record.parentRecordId
      ? recordsById.get(record.parentRecordId) ?? null
      : null;
    const relativeTitle = parent
      ? relativeRecordTitle(record.title, parent.title)
      : record.title;
    const childTitle = parent &&
      relativeTitle.trim().toLowerCase() === parent.title.trim().toLowerCase()
        ? humanizeIdentifier(record.recordKind)
        : relativeTitle;
    const contextualTitle = parent
      ? `${boundedLabelPart(parent.title, 88)} - ${boundedLabelPart(
          childTitle,
          104
        )}`
      : boundedLabelPart(record.title, 176);
    const identityFacts = (identityFieldKeysByRecordKind[record.recordKind] ?? [])
      .flatMap((fieldKey) => {
        const fact = record.facts.find((candidate) => (
          candidate.fieldKey === fieldKey && candidate.value.kind !== 'null'
        ));
        return fact ? [fact] : [];
      })
      .slice(0, 3);
    const qualifier = identityFacts
      .map((fact) => (
        `${boundedLabelPart(fact.label, 36)}: ${boundedLabelPart(
          fact.value.displayValue,
          44
        )}`
      ))
      .join(', ');
    return {
      candidate: boundedLabelPart(
        qualifier ? `${contextualTitle} - ${qualifier}` : contextualTitle,
        236
      ),
      hasQualifier: qualifier.length > 0,
      record,
      token: safeIdentityToken(record.recordId)
    };
  });
  const candidateCounts = occurrenceCounts(
    candidates.map(({ candidate }) => candidate.toLowerCase())
  );
  const labels = new Map<string, string>();
  for (const entry of candidates) {
    const needsFallback = !entry.hasQualifier ||
      (candidateCounts.get(entry.candidate.toLowerCase()) ?? 0) > 1;
    const peers = candidates.filter((candidate) => (
      candidate.candidate.toLowerCase() === entry.candidate.toLowerCase()
    ));
    labels.set(
      entry.record.recordId,
      needsFallback
        ? `${entry.candidate} - #${shortestUniqueTokenSuffix(
            entry.token,
            peers.map((peer) => peer.token)
          )}`
        : entry.candidate
    );
  }
  return ensureUniqueRecordLabels(candidates, labels);
}

function recordDisplayLabel(
  record: GameModuleRecord,
  labels: ReadonlyMap<string, string>
) {
  return labels.get(record.recordId) ??
    `${boundedLabelPart(record.title, 208)} - #${safeIdentityToken(record.recordId)}`;
}

function occurrenceCounts(values: readonly string[]) {
  const counts = new Map<string, number>();
  for (const value of values) counts.set(value, (counts.get(value) ?? 0) + 1);
  return counts;
}

function ensureUniqueRecordLabels(
  entries: readonly {
    candidate: string;
    record: GameModuleRecord;
    token: string;
  }[],
  labels: Map<string, string>
) {
  for (let attempt = 0; attempt <= entries.length; attempt += 1) {
    const groups = new Map<string, typeof entries[number][]>();
    for (const entry of entries) {
      const key = labels.get(entry.record.recordId)!.toLowerCase();
      const group = groups.get(key);
      if (group) group.push(entry);
      else groups.set(key, [entry]);
    }
    const collisions = Array.from(groups.values()).filter((group) => group.length > 1);
    if (collisions.length === 0) {
      verifyUniqueRecordLabels(entries, labels);
      return labels;
    }
    for (const group of collisions) {
      const tokens = group.map((entry) => entry.token);
      for (const entry of group) {
        labels.set(
          entry.record.recordId,
          `${labels.get(entry.record.recordId)!} - #${shortestUniqueTokenSuffix(
            entry.token,
            tokens
          )}`
        );
      }
    }
  }
  for (const entry of entries) {
    labels.set(entry.record.recordId, `${entry.candidate} - #${entry.token}`);
  }
  verifyUniqueRecordLabels(entries, labels);
  return labels;
}

function verifyUniqueRecordLabels(
  entries: readonly { record: GameModuleRecord }[],
  labels: ReadonlyMap<string, string>
) {
  const normalized = entries.map((entry) => (
    labels.get(entry.record.recordId)!.toLowerCase()
  ));
  if (new Set(normalized).size !== entries.length) {
    throw new Error('Game module comparison record labels are not unique.');
  }
}

function shortestUniqueTokenSuffix(token: string, tokens: readonly string[]) {
  const maximumLength = Math.max(...tokens.map((candidate) => candidate.length));
  let length = Math.min(12, maximumLength);
  while (true) {
    const suffix = token.slice(-length);
    if (tokens.filter((candidate) => candidate.slice(-length) === suffix).length === 1) {
      return suffix;
    }
    if (length >= maximumLength) return token;
    length = Math.min(maximumLength, length + 4);
  }
}

function safeIdentityToken(identity: string) {
  let token = '';
  for (let index = 0; index < identity.length; index += 1) {
    const character = identity[index]!;
    token += /^[a-z0-9._-]$/u.test(character)
      ? character
      : `~${identity.charCodeAt(index).toString(16).padStart(4, '0')}`;
  }
  return token;
}

function boundedLabelPart(value: string, maximumLength: number) {
  const normalized = value.trim().replace(/\s+/gu, ' ');
  if (normalized.length <= maximumLength) return normalized;
  return `${normalized.slice(0, Math.max(1, maximumLength - 3)).trimEnd()}...`;
}

function searchableRecordText(record: GameModuleRecord, displayLabel: string) {
  return [
    displayLabel,
    record.title,
    record.summary,
    record.recordKind,
    ...record.facts.flatMap((fact) => [
      fact.fieldKey,
      fact.label,
      fact.value.displayValue,
      fact.value.canonicalValue ?? ''
    ])
  ].join('\n').toLowerCase();
}

function formatNumericAxisValue(value: number) {
  return Number.isInteger(value)
    ? value.toLocaleString()
    : value.toLocaleString(undefined, { maximumFractionDigits: 3 });
}
