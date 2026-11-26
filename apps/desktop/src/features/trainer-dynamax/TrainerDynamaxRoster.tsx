/* SPDX-License-Identifier: GPL-3.0-only */
import { EditorRecordHeading, EditorWorkspaceHeader, EditorWorkspaceSection, RecordEditorWorkspace } from '../../components/SectionedEditorWorkspace';
import { useState, type ReactNode } from 'react';
import { ContextHelp } from '../../components/ContextHelp';
import { EditorRecordTable } from '../../components/EditorRecordTable';
import { EditorDetailGroup } from '../../components/EditorRecordDetails';
import { RotateCcw, Search } from 'lucide-react';
import type { TrainerDynamaxSettings, TrainerDynamaxStatus } from '../../bridge/trainerDynamaxContracts';
import { SearchableOptionInput } from '../../components/SearchableOptionInput';
import { useLocalization } from '../../localization';

type Props = { status: TrainerDynamaxStatus | null; settings: TrainerDynamaxSettings; disabled: boolean;
  children: ReactNode; activeSectionRequest?: { sectionId: string; focus?: boolean };
  onChange: (settings: TrainerDynamaxSettings) => void };
export const dynamaxSettingsKey = (settings: TrainerDynamaxSettings) => JSON.stringify({
  disablePlayer: settings.disablePlayer, disableOpponents: settings.disableOpponents,
  enablePlayer: settings.enablePlayer ?? false, enableOpponents: settings.enableOpponents ?? false,
  trainers: [...settings.trainers ?? []].filter(row => row.player !== 0 || row.opponent !== 0).sort((a, b) => a.trainerId - b.trainerId)
});

type Trainer = NonNullable<TrainerDynamaxStatus['trainers']>[number];
export function dynamaxPermission(settings: TrainerDynamaxSettings, trainer: Trainer, side: 'player' | 'opponent'): boolean | null {
  const state = settings.trainers?.find(row => row.trainerId === trainer.trainerId)?.[side] ?? 0;
  if (state === 2) return false;
  if (state === 3) return true;
  if (state === 0) {
    if (side === 'player' ? settings.disablePlayer : settings.disableOpponents) return false;
    if (side === 'player' ? settings.enablePlayer : settings.enableOpponents) return true;
  }
  return (side === 'player' ? trainer.vanillaPlayer : trainer.vanillaOpponent) ?? null;
}
export function dynamaxAll(settings: TrainerDynamaxSettings, trainers: Trainer[], side: 'player' | 'opponent', enabled: boolean): TrainerDynamaxSettings {
  const rows = new Map((settings.trainers ?? []).map(row => [row.trainerId, row]));
  return { ...settings,
    ...(side === 'player' ? { disablePlayer: !enabled, enablePlayer: enabled } : { disableOpponents: !enabled, enableOpponents: enabled }),
    trainers: trainers.map(row => ({ trainerId: row.trainerId, player: 0, opponent: 0, ...rows.get(row.trainerId), [side]: enabled ? 3 : 2 })) };
}

export function TrainerDynamaxRoster({ status, settings, disabled, onChange, children, activeSectionRequest }: Props) {
  const { t, translateLiteral } = useLocalization();
  const [query, setQuery] = useState('');
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const trainers = status?.trainers ?? [];
  const selected = trainers.find(row => row.trainerId === selectedId) ?? trainers[0];
  const rows = new Map((settings.trainers ?? []).map(row => [row.trainerId, row]));
  const applied = new Map((status?.settings.trainers ?? []).map(row => [row.trainerId, row]));
  const value = (id: number) => rows.get(id) ?? { trainerId: id, player: 0, opponent: 0 };
  const label = (state: boolean | null) => t(state === null ? 'trainerDynamax.unknownVanilla' : state ? 'trainerDynamax.enabled' : 'trainerDynamax.disabled');
  const visible = trainers.filter(row => `${row.trainerId} ${row.name}`.toLocaleLowerCase().includes(query.trim().toLocaleLowerCase()));
  const setRow = (id: number, player: number, opponent: number) => {
    if (disabled) return;
    const next = (settings.trainers ?? []).filter(row => row.trainerId !== id);
    if (player !== 0 || opponent !== 0) next.push({ trainerId: id, player, opponent });
    onChange({ ...settings, trainers: next.sort((a, b) => a.trainerId - b.trainerId) });
  };
  const setSide = (id: number, field: 'player' | 'opponent', raw: string) => {
    if (raw !== '2' && raw !== '3') return;
    const row = value(id);
    setRow(id, field === 'player' ? Number(raw) : row.player, field === 'opponent' ? Number(raw) : row.opponent);
  };
  const setAll = (state: number) => onChange(state === 0 ? { disablePlayer: false, disableOpponents: false, trainers: [] }
    : dynamaxAll(dynamaxAll(settings, trainers, 'player', state === 3), trainers, 'opponent', state === 3));
  const rowChanged = (row: Trainer) => {
    const draft = value(row.trainerId);
    const saved = applied.get(row.trainerId);
    return draft.player !== (saved?.player ?? 0) || draft.opponent !== (saved?.opponent ?? 0);
  };
  const sides = [{ key: 'player', title: 'player', field: 'rowPlayer' },
    { key: 'opponent', title: 'opponents', field: 'rowOpponent' }] as const;
  return <RecordEditorWorkspace id="trainerDynamax" className="trainer-dynamax-roster-layout" initialSection="trainer"
    activeSectionRequest={activeSectionRequest}
    browserControls={<div className="editor-record-context">
      <label className="search-box"><Search size={17} aria-hidden="true" /><span className="sr-only">{t('trainerDynamax.search')}</span>
        <input type="search" value={query} onChange={event => setQuery(event.target.value)} placeholder={t('trainerDynamax.search')} />
      </label>
      <span className="muted">{visible.length} / {trainers.length} {t('trainerDynamax.roster')} · {t('trainerDynamax.overrideCount', { count: rows.size })}</span>
    </div>}
    browser={<EditorRecordTable label={t('trainerDynamax.roster')} identityLabel={translateLiteral('Trainer')}
      rows={visible} rowId={row => String(row.trainerId)} identity={row => row.name} selection={selected ? String(selected.trainerId) : undefined}
      onSelect={row => setSelectedId(row.trainerId)} empty={t('trainerDynamax.noResults')}
      columns={[
        { id: 'id', label: translateLiteral('ID'), value: row => row.trainerId, numeric: true },
        ...sides.map(side => ({ id: side.key, label: t(`trainerDynamax.${side.title}`), value: (row: Trainer) => label(dynamaxPermission(settings, row, side.key)) })),
        { id: 'state', label: translateLiteral('State'), value: row => rowChanged(row) ? t('trainerDynamax.draft') : '' }
      ]} />}>
    {selected ? <EditorWorkspaceHeader afterBrowser>
      <EditorRecordHeading name={selected.name} id={selected.trainerId} />
    </EditorWorkspaceHeader> : null}
    <EditorWorkspaceSection id="trainer" label={translateLiteral('Trainer Permissions')} modified={!!selected && rowChanged(selected)}>
      {selected ? <EditorDetailGroup label={translateLiteral('Dynamax permissions')}
        headingValue={<ContextHelp label={t('trainerDynamax.roster')}>
          <p>{t('trainerDynamax.rosterHelp')}</p><p>{t('trainerDynamax.resetTrainerHelp')}</p>
        </ContextHelp>}>
        <table className="editor-data-matrix"><thead><tr>
          <th scope="col">{t('trainerDynamax.permission')}</th><th scope="col">{t('trainerDynamax.current')}</th>
          <th scope="col">{translateLiteral('Vanilla')}</th>
        </tr></thead><tbody>
          {sides.map(side => {
            const permission = dynamaxPermission(settings, selected, side.key);
            return <tr key={side.key}><th scope="row">{t(`trainerDynamax.${side.title}`)}</th>
              <td><SearchableOptionInput ariaLabel={t(`trainerDynamax.${side.field}`)}
                value={permission === null ? t('trainerDynamax.unknownVanilla') : permission ? '3' : '2'} disabled={disabled}
                isFiniteCatalog localizeOptions={false} options={[{ value: '3', label: label(true) }, { value: '2', label: label(false) }]}
                onChange={raw => setSide(selected.trainerId, side.key, raw)} onReselect={raw => setSide(selected.trainerId, side.key, raw)} /></td>
              <td>{label((side.key === 'player' ? selected.vanillaPlayer : selected.vanillaOpponent) ?? null)}</td>
            </tr>;
          })}
        </tbody></table>
        <p>{t('trainerDynamax.pairedRule')}</p>
        <button type="button" className="secondary-button compact-button" disabled={disabled} onClick={() => setRow(selected.trainerId, 1, 1)}>
          <RotateCcw size={16} aria-hidden="true" />{t('trainerDynamax.resetTrainer')}
        </button>
      </EditorDetailGroup> : <p>{t('trainerDynamax.noResults')}</p>}
    </EditorWorkspaceSection>
    <EditorWorkspaceSection id="all" label={t('trainerDynamax.global')}>
      <p className="trainer-dynamax-scope">{t('trainerDynamax.rowScope')}</p>
      {children}
      <div className="trainer-dynamax-actions">
        <button type="button" className="secondary-button" disabled={disabled || !trainers.length} onClick={() => setAll(3)}>{t('trainerDynamax.normalAll')}</button>
        <button type="button" className="secondary-button" disabled={disabled || !trainers.length} onClick={() => setAll(2)}>{t('trainerDynamax.disableAll')}</button>
        <button type="button" className="secondary-button" disabled={disabled || !trainers.length} onClick={() => setAll(0)}>{t('trainerDynamax.clearAll')}</button>
      </div>
    </EditorWorkspaceSection>
  </RecordEditorWorkspace>;
}

export function TrainerDynamaxRowReview({ before, settings, trainers }: { before: TrainerDynamaxSettings;
  settings: TrainerDynamaxSettings; trainers: TrainerDynamaxStatus['trainers'] }) {
  const { t } = useLocalization();
  const old = new Map((before.trainers ?? []).map(row => [row.trainerId, row]));
  const next = new Map((settings.trainers ?? []).map(row => [row.trainerId, row]));
  const records = new Map((trainers ?? []).map(row => [row.trainerId, row]));
  const permission = (id: number, side: 'player' | 'opponent') => dynamaxPermission(settings, records.get(id) ?? { trainerId: id, name: `#${id}` }, side);
  const changed = [...new Set([...old.keys(), ...next.keys()])].sort((a, b) => a - b).filter(id =>
    (old.get(id)?.player ?? 0) !== (next.get(id)?.player ?? 0) || (old.get(id)?.opponent ?? 0) !== (next.get(id)?.opponent ?? 0));
  const label = (state: boolean | null) => t(state === null ? 'trainerDynamax.unknownVanilla' : state ? 'trainerDynamax.enabled' : 'trainerDynamax.disabled');
  return <div><p>{t('trainerDynamax.overrideCount', { count: next.size })}</p>{changed.length ? <ul className="trainer-dynamax-row-review">{changed.map(id => <li key={id}>
    <strong>{records.get(id)?.name ?? `#${id}`} (#{id})</strong><span>{t('trainerDynamax.player')}: {label(permission(id, 'player'))}</span>
    <span>{t('trainerDynamax.opponents')}: {label(permission(id, 'opponent'))}</span>
  </li>)}</ul> : null}</div>;
}
