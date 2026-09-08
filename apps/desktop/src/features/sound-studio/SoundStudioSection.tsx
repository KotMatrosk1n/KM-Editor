// SPDX-License-Identifier: GPL-3.0-only
import { isTauri } from '@tauri-apps/api/core';
import { useVirtualizer } from '@tanstack/react-virtual';
import { Music, Pause, Play, Square, Volume2, VolumeX } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import { z } from 'zod';
import type { ProjectPaths } from '../../bridge/contracts';
import { LoadingProgress } from '../../components/LoadingProgress';
import { SearchableOptionInput } from '../../components/SearchableOptionInput';
import { usePublishCommonEditorDiagnostics } from '../../components/CommonEditorDiagnostics';
import { useLocalization } from '../../localization';
import { closeSoundCatalog, loadSoundCatalog, loadSoundMedia, soundRequest, type SoundEntry } from './soundStudioBridge';
import { SoundPlayer, type SoundInfo } from './SoundPlayer';
import { SoundAnalysis } from './SoundAnalysis';
import './SoundStudioSection.css';

const categories = ['all', 'music', 'cries', 'battle', 'ambience', 'interface', 'characters', 'events', 'other'];
const volumeKey = 'km-editor.sound-studio.volume';
function readVolume() { try { const value = Number(localStorage.getItem(volumeKey) ?? '0.5'); return Number.isFinite(value) ? Math.max(0, Math.min(1, value)) : 0.5; } catch { return 0.5; } }
function time(seconds: number) { return `${Math.floor(seconds / 60)}:${Math.floor(seconds % 60).toString().padStart(2, '0')}`; }
export default function SoundStudioSection({ paths }: { paths: ProjectPaths }) {
  const { t } = useLocalization();
  const [catalog, setCatalog] = useState<SoundEntry[]>([]), [selected, setSelected] = useState<SoundEntry | null>(null);
  const [category, setCategory] = useState('all'), [query, setQuery] = useState(''), [everywhere, setEverywhere] = useState(false), [kind, setKind] = useState('sample');
  const [loading, setLoading] = useState(true), [progress, setProgress] = useState([0, 0]), [revision, setRevision] = useState(0);
  const [preparing, setPreparing] = useState(false), [error, setError] = useState<string | null>(null);
  const [player, setPlayer] = useState<SoundPlayer | null>(null), [info, setInfo] = useState<SoundInfo | null>(null);
  const [playing, setPlaying] = useState(false), [position, setPosition] = useState(0), [volume, setVolume] = useState(readVolume);
  const [muted, setMuted] = useState(false), [repeat, setRepeat] = useState(false), [visualizer, setVisualizer] = useState(true);
  const token = useRef(''), catalogAbort = useRef<AbortController | null>(null), scroll = useRef<HTMLDivElement>(null);
  const latestPlayer = useRef(player); latestPlayer.current = player;
  const pathKey = JSON.stringify(paths);
  useEffect(() => {
    if (!isTauri()) { setLoading(false); return; }
    const scope: ProjectPaths = JSON.parse(pathKey); const id = crypto.randomUUID().replaceAll('-', ''); token.current = id;
    const abort = new AbortController(); catalogAbort.current = abort;
    setCatalog([]); setSelected(null); setLoading(true); setError(null); setProgress([0, 0]);
    void loadSoundCatalog(scope, id, abort.signal, (completed, total) => setProgress([completed, total]))
      .then(entries => { if (!abort.signal.aborted) setCatalog(entries); })
      .catch(() => { if (!abort.signal.aborted) setError('KM-AUDIO-SOURCE-UNAVAILABLE'); })
      .finally(() => { if (!abort.signal.aborted) setLoading(false); });
    const heartbeat = setInterval(() => { if (!abort.signal.aborted) void soundRequest(scope, id, 'status', z.unknown()).catch(() => {}); }, 30000);
    return () => { abort.abort(); clearInterval(heartbeat); void closeSoundCatalog(scope, id).catch(() => {}); };
  }, [pathKey, revision]);
  useEffect(() => {
    setInfo(null); setPosition(0); setPlaying(false); setRepeat(false); setPlayer(null); setPreparing(false); setError(null);
    if (!selected || selected.status !== 'sample') return;
    const abort = new AbortController(); const active = new SoundPlayer(selected.size, selected.bank.toLowerCase().endsWith('.wav')); setPlayer(active); setPreparing(true); setError(null);
    active.onReady = details => { if (!abort.signal.aborted) { setInfo(details); setPreparing(false); } };
    active.onChange = () => { if (!abort.signal.aborted) { setPlaying(active.playing); setPosition(active.getPosition()); } };
    active.onError = () => { if (!abort.signal.aborted) { setPreparing(false); setError('KM-AUDIO-DECODE-UNSUPPORTED'); setInfo(null); } };
    void loadSoundMedia(JSON.parse(pathKey), token.current, selected, abort.signal, active.worker)
      .catch(() => { if (!abort.signal.aborted) { setError('KM-AUDIO-SOURCE-UNAVAILABLE'); setPreparing(false); active.dispose(); } });
    return () => { abort.abort(); active.dispose(); };
  }, [selected, pathKey]);
  useEffect(() => { player?.setVolume(volume, muted); try { localStorage.setItem(volumeKey, String(volume)); } catch { /* Keep session controls available. */ } }, [volume, muted, player]);
  useEffect(() => { if (!player) return; const timer = setInterval(() => { if (player.playing) setPosition(player.getPosition()); }, 150); return () => clearInterval(timer); }, [player]);
  const filtered = useMemo(() => { const search = query.trim().toLocaleLowerCase(); return catalog.filter(entry => (everywhere || category === 'all' || entry.category === category)
    && (kind === 'all' || (kind === 'sample' ? entry.kind === 'sample' || entry.kind === 'reference' : entry.kind === 'event'))
    && (!search || `${entry.name} ${entry.identifier} ${entry.bank} ${entry.codec}`.toLocaleLowerCase().includes(search))); }, [catalog, category, query, everywhere, kind]);
  const counts = useMemo(() => { const result: Record<string, number> = { all: 0 }; for (const entry of catalog) {
    if (kind !== 'all' && (kind === 'event' ? entry.kind !== 'event' : entry.kind !== 'sample' && entry.kind !== 'reference')) continue;
    result.all++; result[entry.category] = (result[entry.category] ?? 0) + 1;
  } return result; }, [catalog, kind]);
  const virtual = useVirtualizer({ count: filtered.length, getScrollElement: () => scroll.current, estimateSize: () => 70, overscan: 6 });
  useEffect(() => { scroll.current?.scrollTo({ top: 0 }); }, [category, query, everywhere, kind]);
  usePublishCommonEditorDiagnostics(error ? [{ code: error, domain: 'workflow.soundStudio', severity: 'error', message: t(error === 'KM-AUDIO-SOURCE-UNAVAILABLE' ? 'soundStudio.sourceError' : error === 'KM-AUDIO-PLAYBACK-FAILED' ? 'soundStudio.playbackError' : 'soundStudio.decodeError') }] : []);
  const control = (action: () => Promise<void>) => { const active = player; void action().catch(() => { if (latestPlayer.current === active) setError('KM-AUDIO-PLAYBACK-FAILED'); }); };
  const stopCatalog = () => { catalogAbort.current?.abort(); setLoading(false); void closeSoundCatalog(paths, token.current).catch(() => {}); };
  return <section className="panel wide-panel sound-studio" aria-labelledby="sound-studio-title">
    <header className="sound-studio__header"><Music size={24} aria-hidden="true" /><div><h2 id="sound-studio-title">{t('soundStudio.title')}</h2><p>{t('soundStudio.description')}</p></div>
      <button type="button" onClick={() => { player?.stop(); setSelected(null); setRevision(value => value + 1); }}>{t('soundStudio.reload')}</button></header>
    {!isTauri() ? <p>{t('soundStudio.desktopRequired')}</p> : null}
    {loading ? <div className="sound-studio__loading"><LoadingProgress label={t('soundStudio.loading')} completed={progress[0]} total={progress[1] || undefined} /><button type="button" onClick={stopCatalog}>{t('soundStudio.cancel')}</button></div> : null}
    <div className="sound-studio__workspace">
      <aside className="sound-studio__categories" aria-label={t('soundStudio.categories')}>
        {categories.map(value => <button type="button" key={value} aria-pressed={value === category && !everywhere} onClick={() => { setCategory(value); setEverywhere(false); }}><span>{t(`soundStudio.category.${value}`)}</span><span>{counts[value] ?? 0}</span></button>)}
      </aside>
      <div className="sound-studio__browser">
        <label htmlFor="sound-search">{t('soundStudio.search')}</label><input id="sound-search" type="search" value={query} onChange={event => setQuery(event.target.value)} placeholder={t('soundStudio.searchHint')} />
        <div className="sound-studio__filters"><label><input type="checkbox" checked={everywhere} onChange={event => setEverywhere(event.target.checked)} />{t('soundStudio.everywhere')}</label>
          <SearchableOptionInput id="sound-entry-type" ariaLabel={t('soundStudio.entryType')} disabled={false} value={kind} onChange={setKind} options={['sample', 'event', 'all'].map(value => ({ value, label: t(`soundStudio.kind.${value}`) }))} /></div>
        <small>{t('soundStudio.results', { count: filtered.length })}</small>
        <div ref={scroll} className="sound-studio__catalog" role="listbox" tabIndex={0} aria-label={t('soundStudio.catalog')}
          aria-activedescendant={selected && virtual.getVirtualItems().some(row => filtered[row.index]?.id === selected.id) ? `sound-entry-${selected.id}` : undefined}
          onKeyDown={event => { if (!['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key) || filtered.length === 0) return; event.preventDefault();
            const index = filtered.findIndex(entry => entry.id === selected?.id); const next = event.key === 'Home' ? 0 : event.key === 'End' ? filtered.length - 1 : Math.max(0, Math.min(filtered.length - 1, index + (event.key === 'ArrowDown' ? 1 : -1)));
            setSelected(filtered[next]); virtual.scrollToIndex(next); }}>
          <div style={{ height: virtual.getTotalSize(), position: 'relative' }}>{virtual.getVirtualItems().map(row => { const entry = filtered[row.index]; return <div key={entry.id} id={`sound-entry-${entry.id}`} role="option" aria-selected={selected?.id === entry.id}
            className="sound-studio__entry" style={{ position: 'absolute', width: '100%', height: row.size, transform: `translateY(${row.start}px)` }} onClick={() => setSelected(entry)}>
            <strong>{entry.name}</strong><small>{entry.codec || t(`soundStudio.kind.${entry.kind === 'event' ? 'event' : 'all'}`)} · {t(`soundStudio.status.${entry.status}`)}</small></div>; })}</div>
          {!loading && filtered.length === 0 ? <p role="status">{t(catalog.length ? 'soundStudio.noResults' : 'soundStudio.empty')}</p> : null}
        </div>
      </div>
      <div className="sound-studio__player">
        <div className="sound-studio__now"><small>{selected ? t(`soundStudio.category.${selected.category}`) : t('soundStudio.title')}</small><h3>{selected?.name ?? t('soundStudio.select')}</h3></div>
        <SoundAnalysis player={player} enabled={visualizer} muted={muted} />
        <label className="sound-studio__visualizer-toggle"><input type="checkbox" checked={visualizer} onChange={event => setVisualizer(event.target.checked)} />{t('soundStudio.visualizer')}</label>
        {preparing ? <LoadingProgress label={t('soundStudio.preparing')} /> : null}
        {selected && selected.status !== 'sample' ? <p role="status">{t(`soundStudio.explain.${selected.status}`)}</p> : null}
        <div className="sound-studio__transport">
          <label htmlFor="sound-seek" className="sound-studio__time"><span>{t('soundStudio.position')}</span><span>{time(position / (info?.rate ?? 1))} / {time(info ? info.frames / info.rate : 0)}</span></label>
          <input id="sound-seek" type="range" min={0} max={info?.frames || 1} step={1} value={position} disabled={!info} onChange={event => control(() => player!.seek(Number(event.target.value)))} />
          <div className="sound-studio__controls"><button type="button" disabled={!info} aria-label={t(playing ? 'soundStudio.pause' : 'soundStudio.play')} onClick={() => playing ? player?.pause() : control(() => player!.play())}>{playing ? <Pause aria-hidden="true" /> : <Play aria-hidden="true" />}{t(playing ? 'soundStudio.pause' : 'soundStudio.play')}</button>
            <button type="button" disabled={!info} aria-label={t('soundStudio.stop')} onClick={() => player?.stop()}><Square size={18} aria-hidden="true" /></button>
            <label><input type="checkbox" checked={repeat} onChange={event => { setRepeat(event.target.checked); if (player) control(() => player.setRepeat(event.target.checked)); }} />{t('soundStudio.repeat')}</label></div>
          <div className="sound-studio__volume"><button type="button" aria-pressed={muted} aria-label={t('soundStudio.mute')} onClick={() => setMuted(value => !value)}>{muted ? <VolumeX aria-hidden="true" /> : <Volume2 aria-hidden="true" />}</button>
            <label htmlFor="sound-volume">{t('soundStudio.volume')} {Math.round(volume * 100)}%</label><input id="sound-volume" type="range" min={0} max={1} step={0.01} value={volume} onChange={event => setVolume(Number(event.target.value))} /></div>
        </div>
        {selected ? <details className="sound-studio__details"><summary>{t('soundStudio.details')}</summary><dl>
          <dt>{t('soundStudio.identifier')}</dt><dd>{selected.identifier}</dd><dt>{t('soundStudio.bank')}</dt><dd>{selected.bank}</dd>
          <dt>{t('soundStudio.format')}</dt><dd>{selected.codec || t('soundStudio.unknown')} · {info?.rate ?? selected.sampleRate} Hz · {info?.channels ?? selected.channels} {t('soundStudio.channels')}</dd>
          <dt>{t('soundStudio.loop')}</dt><dd>{info && info.loopEnd > info.loopStart ? `${time(info.loopStart / info.rate)} → ${time(info.loopEnd / info.rate)}` : t('soundStudio.noLoop')}</dd>
        </dl><p>{t('soundStudio.meterHelp')}</p>{(info?.channels ?? 0) > 2 ? <p>{t('soundStudio.multichannel')}</p> : null}<p>{t('soundStudio.eventHelp')}</p><a href="https://github.com/KotMatrosk1n/KM-Editor/blob/master/apps/desktop/public/audio-decoder/NOTICE.txt" target="_blank" rel="noreferrer">{t('soundStudio.licenses')}</a></details> : null}
      </div>
    </div>
  </section>;
}
