/* SPDX-License-Identifier: GPL-3.0-only */
import { Fragment, useState, type ReactNode } from 'react';
import { Swords } from 'lucide-react';
import type { PlaPage } from '../../bridge/plaProjectBridge';
import { ContextHelp } from '../../components/ContextHelp';
import { EditorWorkspaceSection, RecordEditorWorkspace } from '../../components/SectionedEditorWorkspace';
import { SearchableOptionInput } from '../../components/SearchableOptionInput';
import { useLocalization } from '../../localization';
import { TypeChartCellControl, TypeChartTypeBadge } from '../type-chart/TypeChartSection';

const colors = ['#A8A878', '#C03028', '#A890F0', '#A040A0', '#E0C068', '#B8A038', '#A8B820', '#705898', '#B8B8D0',
  '#F08030', '#6890F0', '#78C850', '#F8D030', '#F85888', '#98D8D8', '#7038F8', '#705848', '#EE99AC'];

export function PlaTypeChartWorkspace({ page, rows, enabled, onChange, sessionBar, actions, refresh, loading }: {
  page: PlaPage | null; rows: string[][]; enabled: boolean; onChange: (rows: string[][]) => void;
  sessionBar: ReactNode; actions: ReactNode; refresh: ReactNode; loading: boolean;
}) {
  const { t, translateLiteral } = useLocalization();
  const [attackingType, setAttackingType] = useState('');
  const metadata = page?.records[0]?.metadata;
  const names: string[] = metadata?.typeNames ? JSON.parse(metadata.typeNames) : [];
  const types = names.slice(0, 18).map((label, typeIndex) => ({ typeIndex, label, shortLabel: label.slice(0, 3).toLocaleUpperCase(), color: colors[typeIndex] }));
  const available = types.length === 18 && rows.length === 18 && rows.every(row => row.length === 18);
  return <section aria-labelledby="pla-editor-title" className="panel wide-panel pla-editor">
    <div className="panel-heading"><Swords aria-hidden="true" size={18} /><h2 id="pla-editor-title">{translateLiteral('Type Chart')}</h2>{refresh}</div>
    {sessionBar}{actions}
    {loading ? <p role="status">{translateLiteral('Loading')}</p> : null}
    <RecordEditorWorkspace id="pla-type-chart" className="type-chart-layout" initialSection="matchups">
      {available ? <EditorWorkspaceSection id="matchups" label={translateLiteral('Matchups')}>
        <div className="editor-record-context type-chart-filter">
          <label htmlFor="pla-type-chart-attacking">{translateLiteral('Attacking')}</label>
          <SearchableOptionInput id="pla-type-chart-attacking" ariaLabel={translateLiteral('Attacking')}
            value={attackingType} onChange={setAttackingType} disabled={loading} isFiniteCatalog
            options={[
              { value: '', label: translateLiteral('All types') },
              ...types.map(type => ({ value: String(type.typeIndex), label: type.label }))
            ]} />
          <ContextHelp label={translateLiteral('Type Chart')}>{t('pla.notice.typeChart')}</ContextHelp>
        </div>
        <div className="type-chart-editor">
      <div className="type-chart-scroll" aria-label={translateLiteral('Type effectiveness chart')}>
        <div className="type-chart-grid">
          <div className="type-chart-axis-label">
            <span className="type-chart-axis-line"><span>{translateLiteral('Defending')}</span><span aria-hidden="true">→</span></span>
            <span className="type-chart-axis-line"><span>{translateLiteral('Attacking')}</span><span aria-hidden="true">↓</span></span>
          </div>
          {types.map(type => <TypeChartTypeBadge key={`defense-${type.typeIndex}`} type={type} />)}
          {types.filter(type => attackingType === '' || String(type.typeIndex) === attackingType).map(attack => <Fragment key={`attack-${attack.typeIndex}`}>
            <TypeChartTypeBadge isRowHeader type={attack} />
            {types.map(defense => {
              const value = Number(rows[attack.typeIndex][defense.typeIndex]);
              return <TypeChartCellControl key={defense.typeIndex} attackTypeLabel={attack.label} defenseTypeLabel={defense.label}
                disabled={!enabled} id={`type-chart-${attack.typeIndex}-${defense.typeIndex}`} value={value === 0 || value === 2 || value === 8 ? value : 4}
                onChange={nextValue => onChange(rows.map((row, index) => index === attack.typeIndex
                  ? row.map((cell, column) => column === defense.typeIndex ? String(nextValue) : cell) : row))} />;
            })}
          </Fragment>)}
        </div>
      </div>
        </div>
      </EditorWorkspaceSection> : null}
      {metadata ? <EditorWorkspaceSection id="source" label={translateLiteral('Source')}>
        <dl className="item-provenance-list">
          <div><dt>{translateLiteral('Game')}</dt><dd>Pokémon Legends: Arceus</dd></div>
          <div><dt>{translateLiteral('Offset')}</dt><dd>{metadata.offset ?? translateLiteral('Unknown')}</dd></div>
          <div><dt>{translateLiteral('Build')}</dt><dd>{metadata.buildId ?? translateLiteral('Unknown')}</dd></div>
        </dl>
      </EditorWorkspaceSection> : null}
    </RecordEditorWorkspace>
  </section>;
}
