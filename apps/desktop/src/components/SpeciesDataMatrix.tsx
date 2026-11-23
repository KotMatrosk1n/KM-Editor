/* SPDX-License-Identifier: GPL-3.0-only */
import { ChartNoAxesColumnIncreasing, Search } from 'lucide-react';
import { useId, useLayoutEffect, useRef, useState, type CSSProperties, type ReactNode } from 'react';
import { useLocalization } from '../localization';
import './speciesDataMatrix.css';

export const speciesStatFields = [
  ['hp', 'evYieldHP'], ['attack', 'evYieldAttack'], ['defense', 'evYieldDefense'],
  ['specialAttack', 'evYieldSpecialAttack'], ['specialDefense', 'evYieldSpecialDefense'], ['speed', 'evYieldSpeed']
] as const;

type MatrixField = {
  key: string;
  label: string;
  inputId: string;
  value: string;
  changed: boolean;
  control: ReactNode;
};

type MatrixGroup = {
  key: string;
  label: string;
  fields: MatrixField[];
  note?: ReactNode;
};

type Props = {
  recordName: string;
  recordId: string;
  groups: MatrixGroup[];
  finderRequest: ReturnType<typeof useSpeciesFieldFinder>['request'];
};

export function useSpeciesFieldFinder() {
  const { t } = useLocalization();
  const [finding, setFinding] = useState(false);
  const finderId = useId();
  const trigger = useRef<HTMLButtonElement>(null);
  return {
    request: { finding, setFinding, finderId, trigger },
    shortcut: {
      sectionId: 'speciesData',
      label: t('speciesMatrix.findField'),
      icon: <Search size={15} aria-hidden="true" />,
      controls: finderId,
      expanded: finding,
      onClose: () => setFinding(false),
      onActivate: (button: HTMLButtonElement) => {
        const wasExpanded = button.getAttribute('aria-expanded') === 'true';
        trigger.current = button;
        setFinding(!wasExpanded);
      }
    }
  };
}

const statKeys = new Set<string>(speciesStatFields.flat());
const traitGroups = new Set(['Traits', 'Types', 'Battle Basics', 'Abilities', 'Held Items']);

function statValue(value: string | undefined) {
  return value !== undefined && /^\d+$/u.test(value) ? Number(value) : null;
}

export function SpeciesDataMatrix({ recordName, recordId, groups, finderRequest }: Props) {
  const { t, translateLiteral } = useLocalization();
  const [query, setQuery] = useState('');
  const [changedOnly, setChangedOnly] = useState(false);
  const [highlighted, setHighlighted] = useState<string | null>(null);
  const finderInput = useRef<HTMLInputElement>(null);
  const matrix = useRef<HTMLDivElement>(null);
  const { finding, setFinding, finderId, trigger } = finderRequest;

  useLayoutEffect(() => {
    if (finding) finderInput.current?.focus();
  }, [finding]);
  const fields = groups.flatMap(group => group.fields.map(field => ({ ...field, group: group.label })));
  const fieldsByKey = new Map(fields.map(field => [field.key, field]));
  const stats = speciesStatFields.map(([base, yieldKey]) => ({
    key: base, base: fieldsByKey.get(base), yieldField: fieldsByKey.get(yieldKey)
  })).filter(row => row.base || row.yieldField);
  const hasYield = stats.some(row => row.yieldField);
  const hasBase = stats.some(row => row.base);
  const baseStats = stats.map(row => statValue(row.base?.value));
  const total = baseStats.length === speciesStatFields.length && baseStats.every(value => value !== null)
    ? baseStats.reduce((sum, value) => sum + value, 0) : null;
  const ordinaryGroups = groups.map(group => ({
    ...group, fields: group.fields.filter(field => !statKeys.has(field.key))
  })).filter(group => group.fields.length > 0);
  const search = query.trim().toLocaleLowerCase();
  const results = fields.filter(field => (!changedOnly || field.changed) &&
    [field.label, field.key, field.group].some(value => value.toLocaleLowerCase().includes(search)));

  const jumpToField = (field: MatrixField) => {
    const target = document.getElementById(field.inputId);
    const cell = target?.closest<HTMLElement>('.species-matrix-field');
    if (!target || !cell || !matrix.current?.contains(cell)) return;
    setFinding(false);
    setHighlighted(field.key);
    requestAnimationFrame(() => {
      const control = target.closest<HTMLElement>('button, input, select, textarea') ?? cell.querySelector<HTMLElement>('input, select, textarea, button:not(.hover-tooltip-trigger)');
      const focusTarget = !control || control.matches(':disabled') ? cell : control;
      focusTarget.focus({ preventScroll: true });
      // Searchable controls can have a non-focusable id on the labeled wrapper.
      if (!cell.contains(document.activeElement)) cell.focus({ preventScroll: true });
      cell.scrollIntoView({ block: 'nearest', inline: 'nearest' });
    });
  };

  const renderField = (field: MatrixField, stat = false) => <div key={field.key}
    className={`species-matrix-field${stat ? ' species-matrix-stat-field' : ''}`}
    data-highlighted={highlighted === field.key || undefined} tabIndex={-1}
    onBlur={event => { if (!event.currentTarget.contains(event.relatedTarget)) setHighlighted(null); }}>
    {field.control}
  </div>;
  const renderGroup = (group: MatrixGroup) => <section className="species-matrix-group" data-editor-disclosure-group key={group.key}>
    <h4>{group.label}</h4>
    {group.note}
    {group.fields.map(field => renderField(field))}
  </section>;
  const lanes = [
    ordinaryGroups.filter(group => group.key === 'Flags'),
    ordinaryGroups.filter(group => traitGroups.has(group.key)),
    ordinaryGroups.filter(group => group.key !== 'Flags' && !traitGroups.has(group.key))
  ];

  return <div className="species-matrix" ref={matrix}>
    <div className="species-matrix-heading">
      <div className="species-matrix-identity">
        <span className="species-matrix-id" data-localization-ignore="true">{recordId}</span>
        <h3 data-localization-ignore="true">{recordName}</h3>
        <span className="species-matrix-section-name">{t('editorSections.speciesData')}</span>
        <span className="species-matrix-count">{t('speciesMatrix.fieldCount', { count: fields.length })}</span>
      </div>
    </div>
    <div id={finderId} hidden={!finding} className="species-matrix-finder" onKeyDown={event => {
      if (event.key === 'Escape') {
        event.stopPropagation();
        setFinding(false);
        trigger.current?.focus();
      }
    }}>
      {finding ? <>
        <div className="species-matrix-finder-controls">
          <input ref={finderInput} type="search" aria-label={t('speciesMatrix.findField')} placeholder={t('speciesMatrix.findField')}
            value={query} onChange={event => setQuery(event.target.value)}
            onKeyDown={event => {
              if (event.key === 'Enter' && results[0]) {
                event.preventDefault();
                jumpToField(results[0]);
              }
            }} />
          <label><input type="checkbox" checked={changedOnly} onChange={event => setChangedOnly(event.target.checked)} />
            {t('speciesMatrix.changedOnly')}</label>
        </div>
        {results.length ? <ul className="species-matrix-results">
          {results.map(field => <li key={field.key}>
            <button type="button" onClick={() => jumpToField(field)}>
              <strong>{field.label}</strong><small>{field.group}</small>
            </button>
          </li>)}
        </ul> : <p className="quiet-copy" role="status">{t('speciesMatrix.noFields')}</p>}
      </> : null}
    </div>
    <div className="species-matrix-lanes">
      {lanes.map((groupsInLane, index) => (groupsInLane.length || (index === 0 && stats.length)) ? <div className="species-matrix-lane" key={index}>
        {index === 0 && stats.length ? <section className="species-matrix-group species-matrix-stats" data-editor-disclosure-group>
          <h4><ChartNoAxesColumnIncreasing size={15} aria-hidden="true" />
            {hasBase && hasYield ? t('speciesMatrix.baseAndYield') : translateLiteral(hasBase ? 'Base Stats' : 'EV Yield')}
            {total !== null ? <span>{t('speciesMatrix.statTotal', { total })}</span> : null}
          </h4>
          <div className="species-matrix-stat-table" data-has-base={hasBase} data-has-yield={hasYield}>
            <div className="species-matrix-stat-row species-matrix-stat-headings">
              <span>{t('speciesMatrix.stat')}</span>
              {hasBase ? <span className="species-matrix-base-column">{translateLiteral('Base')}</span> : null}
              {hasYield ? <span className="species-matrix-yield-column">{translateLiteral('EV Yield')}</span> : null}
            </div>
            {stats.map((row, index) => <div className="species-matrix-stat-row" key={row.key}>
              <label htmlFor={row.base?.inputId ?? row.yieldField?.inputId}>{row.base?.label ?? row.yieldField?.label}</label>
              {hasBase ? <>
                <span className="species-matrix-bar" aria-hidden="true"
                  style={{ '--species-stat-fill': `${Math.min(100, (baseStats[index] ?? 0) / 255 * 100)}%` } as CSSProperties} />
                <div className="species-matrix-base-column">{row.base ? renderField(row.base, true) : null}</div>
              </> : null}
              {hasYield ? <div className="species-matrix-yield-column">{row.yieldField ? renderField(row.yieldField, true) : null}</div> : null}
            </div>)}
          </div>
        </section> : null}
        {groupsInLane.map(renderGroup)}
      </div> : null)}
    </div>
  </div>;
}
