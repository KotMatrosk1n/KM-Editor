/* SPDX-License-Identifier: GPL-3.0-only */
import { useState } from 'react';
import { RotateCcw, Search } from 'lucide-react';
import type { TrainerDynamaxSettings, TrainerDynamaxStatus } from '../../bridge/trainerDynamaxContracts';
import { SearchableOptionInput } from '../../components/SearchableOptionInput';
import { useLocalization } from '../../localization';

type Props = { status: TrainerDynamaxStatus; settings: TrainerDynamaxSettings; disabled: boolean;
  onChange: (settings: TrainerDynamaxSettings) => void };
export const dynamaxSettingsKey = (settings: TrainerDynamaxSettings) => JSON.stringify({
  disablePlayer: settings.disablePlayer, disableOpponents: settings.disableOpponents,
  trainers: [...settings.trainers ?? []].filter(row => row.player !== 0 || row.opponent !== 0).sort((a, b) => a.trainerId - b.trainerId)
});

export function TrainerDynamaxRoster({ status, settings, disabled, onChange }: Props) {
  const { t, translateLiteral } = useLocalization();
  const [query, setQuery] = useState('');
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const trainers = status.trainers ?? [];
  const selected = trainers.find(row => row.trainerId === selectedId) ?? trainers[0];
  const rows = new Map((settings.trainers ?? []).map(row => [row.trainerId, row]));
  const applied = new Map((status.settings.trainers ?? []).map(row => [row.trainerId, row]));
  const value = (id: number) => rows.get(id) ?? { trainerId: id, player: 0, opponent: 0 };
  const label = (state: number) => t(state === 0 ? 'trainerDynamax.inherit' : state === 1 ? 'trainerDynamax.normal' : 'trainerDynamax.disabled');
  const visible = trainers.filter(row => `${row.trainerId} ${row.name}`.toLocaleLowerCase().includes(query.trim().toLocaleLowerCase()));
  const setRow = (id: number, player: number, opponent: number) => {
    if (disabled) return;
    const next = (settings.trainers ?? []).filter(row => row.trainerId !== id);
    if (player !== 0 || opponent !== 0) next.push({ trainerId: id, player, opponent });
    onChange({ ...settings, trainers: next.sort((a, b) => a.trainerId - b.trainerId) });
  };
  const setAll = (state: number) => onChange({ ...settings, trainers: state === 0 ? [] : trainers.map(row => ({ trainerId: row.trainerId, player: state, opponent: state })) });
  return <section className="trainer-dynamax-roster" aria-labelledby="dynamax-roster-title">
    <div className="trainer-dynamax-heading"><div><h3 id="dynamax-roster-title">{t('trainerDynamax.roster')}</h3><p>{t('trainerDynamax.rosterHelp')}</p></div>
      <span className="status-pill status-pill-info">{t('trainerDynamax.overrideCount', { count: rows.size })}</span></div>
    <p className="trainer-dynamax-scope">{t('trainerDynamax.rowScope')}</p>
    <div className="trainer-dynamax-actions">
      <button type="button" className="secondary-button" disabled={disabled || trainers.length === 0} onClick={() => setAll(1)}>{t('trainerDynamax.normalAll')}</button>
      <button type="button" className="secondary-button" disabled={disabled || trainers.length === 0} onClick={() => setAll(2)}>{t('trainerDynamax.disableAll')}</button>
      <button type="button" className="secondary-button" disabled={disabled || rows.size === 0} onClick={() => setAll(0)}>{t('trainerDynamax.clearAll')}</button>
    </div>
    <div className="trainer-dynamax-roster-layout">
      <div className="trainer-dynamax-list">
        <label className="trainer-dynamax-search"><Search size={17} aria-hidden="true" /><span className="sr-only">{t('trainerDynamax.search')}</span>
          <input type="search" value={query} onChange={event => setQuery(event.target.value)} placeholder={t('trainerDynamax.search')} /></label>
        <div className="trainer-dynamax-table-scroll">
          <table className="trainer-dynamax-table"><thead><tr><th>{translateLiteral('ID')}</th><th>{translateLiteral('Trainer')}</th><th>{t('trainerDynamax.player')}</th><th>{t('trainerDynamax.opponents')}</th></tr></thead>
            <tbody>{visible.map(row => {
              const draft = value(row.trainerId); const saved = applied.get(row.trainerId);
              const changed = draft.player !== (saved?.player ?? 0) || draft.opponent !== (saved?.opponent ?? 0);
              return <tr key={row.trainerId} tabIndex={0} aria-selected={selected?.trainerId === row.trainerId}
                onClick={() => setSelectedId(row.trainerId)} onKeyDown={event => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); setSelectedId(row.trainerId); } }}>
                <td>{row.trainerId}</td><td><span>{row.name}</span>{changed ? <span className="status-pill status-pill-info">{t('trainerDynamax.draft')}</span> : null}</td>
                <td>{label(draft.player)}</td><td>{label(draft.opponent)}</td></tr>;
            })}</tbody></table>
          {visible.length === 0 ? <p className="trainer-dynamax-empty">{t('trainerDynamax.noResults')}</p> : null}
        </div>
      </div>
      {selected ? <section className="trainer-dynamax-card trainer-dynamax-selected" aria-labelledby="dynamax-selected-title">
        <div className="trainer-dynamax-heading"><h3 id="dynamax-selected-title">{selected.name}</h3><span className="status-pill">#{selected.trainerId}</span></div>
        {(['player', 'opponent'] as const).map((field, index) => <div className="trainer-dynamax-field" key={field}>
          <span>{t(index === 0 ? 'trainerDynamax.player' : 'trainerDynamax.opponents')}</span>
          <SearchableOptionInput ariaLabel={t(index === 0 ? 'trainerDynamax.rowPlayer' : 'trainerDynamax.rowOpponent')}
            value={String(value(selected.trainerId)[field])} disabled={disabled} isFiniteCatalog localizeOptions={false}
            options={[0, 1, 2].map(state => ({ value: String(state), label: label(state) }))}
            onChange={raw => { const row = value(selected.trainerId); setRow(selected.trainerId, field === 'player' ? Number(raw) : row.player, field === 'opponent' ? Number(raw) : row.opponent); }} />
          <span className="trainer-dynamax-effective">{t('trainerDynamax.effective', { value: label(value(selected.trainerId)[field] || ((index === 0 ? settings.disablePlayer : settings.disableOpponents) ? 2 : 1)) })}</span>
        </div>)}
        <p>{t('trainerDynamax.pairedRule')}</p>
        <button type="button" className="secondary-button" disabled={disabled || !rows.has(selected.trainerId)} onClick={() => setRow(selected.trainerId, 0, 0)}>
          <RotateCcw size={16} aria-hidden="true" />{t('trainerDynamax.resetTrainer')}</button>
        <p>{t('trainerDynamax.resetTrainerHelp')}</p>
      </section> : null}
    </div>
  </section>;
}

export function TrainerDynamaxRowReview({ before, settings, trainers }: { before: TrainerDynamaxSettings;
  settings: TrainerDynamaxSettings; trainers: TrainerDynamaxStatus['trainers'] }) {
  const { t } = useLocalization();
  const old = new Map((before.trainers ?? []).map(row => [row.trainerId, row]));
  const next = new Map((settings.trainers ?? []).map(row => [row.trainerId, row]));
  const names = new Map((trainers ?? []).map(row => [row.trainerId, row.name]));
  const changed = [...new Set([...old.keys(), ...next.keys()])].sort((a, b) => a - b).filter(id =>
    (old.get(id)?.player ?? 0) !== (next.get(id)?.player ?? 0) || (old.get(id)?.opponent ?? 0) !== (next.get(id)?.opponent ?? 0));
  const label = (state: number) => t(state === 0 ? 'trainerDynamax.inherit' : state === 1 ? 'trainerDynamax.normal' : 'trainerDynamax.disabled');
  return <div><p>{t('trainerDynamax.overrideCount', { count: next.size })}</p>{changed.length ? <ul className="trainer-dynamax-row-review">{changed.map(id => <li key={id}>
    <strong>{names.get(id) ?? `#${id}`} (#{id})</strong><span>{t('trainerDynamax.player')}: {label(next.get(id)?.player ?? 0)}</span>
    <span>{t('trainerDynamax.opponents')}: {label(next.get(id)?.opponent ?? 0)}</span>
  </li>)}</ul> : null}</div>;
}
