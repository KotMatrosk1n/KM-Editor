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

export function TrainerDynamaxRoster({ status, settings, disabled, onChange }: Props) {
  const { t, translateLiteral } = useLocalization();
  const [query, setQuery] = useState('');
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const trainers = status.trainers ?? [];
  const selected = trainers.find(row => row.trainerId === selectedId) ?? trainers[0];
  const rows = new Map((settings.trainers ?? []).map(row => [row.trainerId, row]));
  const applied = new Map((status.settings.trainers ?? []).map(row => [row.trainerId, row]));
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
  return <section className="trainer-dynamax-roster" aria-labelledby="dynamax-roster-title">
    <div className="trainer-dynamax-heading"><div><h3 id="dynamax-roster-title">{t('trainerDynamax.roster')}</h3><p>{t('trainerDynamax.rosterHelp')}</p></div>
      <span className="status-pill status-pill-info">{t('trainerDynamax.overrideCount', { count: rows.size })}</span></div>
    <p className="trainer-dynamax-scope">{t('trainerDynamax.rowScope')}</p>
    <div className="trainer-dynamax-actions">
      <button type="button" className="secondary-button" disabled={disabled || trainers.length === 0} onClick={() => setAll(3)}>{t('trainerDynamax.normalAll')}</button>
      <button type="button" className="secondary-button" disabled={disabled || trainers.length === 0} onClick={() => setAll(2)}>{t('trainerDynamax.disableAll')}</button>
      <button type="button" className="secondary-button" disabled={disabled} onClick={() => setAll(0)}>{t('trainerDynamax.clearAll')}</button>
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
                <td>{label(dynamaxPermission(settings, row, 'player'))}</td><td>{label(dynamaxPermission(settings, row, 'opponent'))}</td></tr>;
            })}</tbody></table>
          {visible.length === 0 ? <p className="trainer-dynamax-empty">{t('trainerDynamax.noResults')}</p> : null}
        </div>
      </div>
      {selected ? <section className="trainer-dynamax-card trainer-dynamax-selected" aria-labelledby="dynamax-selected-title">
        <div className="trainer-dynamax-heading"><h3 id="dynamax-selected-title">{selected.name}</h3><span className="status-pill">#{selected.trainerId}</span></div>
        {(['player', 'opponent'] as const).map((field, index) => <div className="trainer-dynamax-field" key={field}>
          <span>{t(index === 0 ? 'trainerDynamax.player' : 'trainerDynamax.opponents')}</span>
          <SearchableOptionInput ariaLabel={t(index === 0 ? 'trainerDynamax.rowPlayer' : 'trainerDynamax.rowOpponent')}
            value={dynamaxPermission(settings, selected, field) === null ? t('trainerDynamax.unknownVanilla') : dynamaxPermission(settings, selected, field) ? '3' : '2'} disabled={disabled} isFiniteCatalog localizeOptions={false}
            options={[{ value: '3', label: label(true) }, { value: '2', label: label(false) }]}
            onChange={raw => setSide(selected.trainerId, field, raw)} onReselect={raw => setSide(selected.trainerId, field, raw)} />
          <span className="trainer-dynamax-effective">{t('trainerDynamax.vanilla', { value: label((index === 0 ? selected.vanillaPlayer : selected.vanillaOpponent) ?? null) })}</span>
        </div>)}
        <p>{t('trainerDynamax.pairedRule')}</p>
        <button type="button" className="secondary-button" disabled={disabled} onClick={() => setRow(selected.trainerId, 1, 1)}>
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
