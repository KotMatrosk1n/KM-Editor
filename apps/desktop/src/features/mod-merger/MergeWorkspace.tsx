// SPDX-License-Identifier: GPL-3.0-only
import { useEffect, useMemo, useRef, useState } from 'react';
import { open } from '@tauri-apps/plugin-dialog';
import type { ProjectGame, ProjectPaths } from '../../bridge/contracts';
import { ProjectBridgeError } from '../../bridge/projectBridgeError';
import { usePublishCommonEditorDiagnostics } from '../../components/CommonEditorDiagnostics';
import { SearchableOptionInput } from '../../components/SearchableOptionInput';
import { useLocalization } from '../../localization';
import { runMerge, type MergeRequest, type MergeResult, type MergeSource } from './mergeWorkspaceBridge';
import './MergeWorkspace.css';
import { projectBridge, type ProjectBridge } from '../../bridge/projectBridge';
import { MergeOutputSafety } from './MergeOutputSafety';
import { readMergeDraft, saveMergeDraft } from './mergeWorkspaceDraft';

const games = ['sword', 'shield', 'scarlet', 'violet', 'za'];
const pageSize = 50;
export default function MergeWorkspace({ paths, active, onExportingChange, armWriteGuard = async () => true, bridge = projectBridge }: {
  armWriteGuard?: () => Promise<boolean>;
  bridge?: ProjectBridge; paths: ProjectPaths; active: boolean; onExportingChange: (busy: boolean) => void;
}) {
  const { t, translateLiteral } = useLocalization();
  const label = (key: string) => t(`mergeWorkspace.${key}`);
  const [saved] = useState(readMergeDraft);
  const [draftSaved, setDraftSaved] = useState(true);
  const [mode, setMode] = useState<'basic' | 'advanced'>(saved?.mode ?? 'basic');
  const [game, setGame] = useState<string>(saved?.game ?? '');
  const [outputMode, setOutputMode] = useState<MergeRequest['outputMode']>(saved?.outputMode ?? 'standalone');
  const [outputRoot, setOutputRoot] = useState(saved?.outputRoot ?? '');
  const [baseRomFs, setBaseRomFs] = useState(saved?.baseRomFs ?? paths.baseRomFsPath ?? '');
  const [baseExeFs, setBaseExeFs] = useState(saved?.baseExeFs ?? paths.baseExeFsPath ?? '');
  const [supportFolder, setSupportFolder] = useState(saved?.supportFolder ?? paths.pokemonLegendsZASupportFolderPath ?? paths.scarletVioletSupportFolderPath ?? '');
  const [sources, setSources] = useState<MergeSource[]>(saved?.sources ?? []);
  const [sourcePath, setSourcePath] = useState('');
  const [choices, setChoices] = useState<Record<string, string>>(saved?.choices ?? {});
  const [result, setResult] = useState<MergeResult | null>(null);
  const [reviewed, setReviewed] = useState('');
  const [safetyBusy, setSafetyBusy] = useState(false);
  const [busy, setBusy] = useState<'analyze' | 'export' | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [query, setQuery] = useState('');
  const [selectedFile, setSelectedFile] = useState('');
  const [page, setPage] = useState(0);
  const [filePage, setFilePage] = useState(0);
  const [written, setWritten] = useState<number | null>(null);
  const [safetyScope, setSafetyScope] = useState<{ game: ProjectGame; projectId: string; outputRoot: string } | null>(null);
  const request: MergeRequest = useMemo(() => ({ mode, game: game || null, outputMode, outputRoot,
    sources, choices: Object.entries(choices).map(([conflictId, sourceId]) => ({ conflictId, sourceId })),
    baseRomFs: mode === 'advanced' ? baseRomFs || null : null,
    baseExeFs: mode === 'advanced' ? baseExeFs || null : null,
    supportFolder: supportFolder || null
  }), [mode, game, outputMode, outputRoot, sources, choices, baseRomFs, baseExeFs, supportFolder]);
  const requestKey = JSON.stringify(request);
  const latestKey = useRef(requestKey); latestKey.current = requestKey;
  const inFlight = useRef(false);
  const isCurrent = requestKey === reviewed;
  const issueMessage = (code: string) => {
    const message = label(`error.${code}`);
    return `${message.startsWith('mergeWorkspace.') ? label('error.generic') : message} (${code})`;
  };
  usePublishCommonEditorDiagnostics(active ? [
    ...(result?.issues ?? []).map(issue => ({ ...issue, message: issueMessage(issue.code), domain: 'workflow.modMerger' })),
    ...(error ? [{ code: error, severity: 'error' as const, message: issueMessage(error), domain: 'workflow.modMerger' }] : [])
  ] : []);
  useEffect(() => { setPage(0); setFilePage(0); }, [query, selectedFile, result]);
  useEffect(() => {
    setDraftSaved(saveMergeDraft({ mode, game, outputMode, outputRoot, baseRomFs, baseExeFs, supportFolder, sources, choices }));
  }, [mode, game, outputMode, outputRoot, baseRomFs, baseExeFs, supportFolder, sources, choices]);

  function addPaths(values: string[]) {
    setSources(current => [...current, ...values.filter(value => !current.some(source => source.path === value)).map(path => ({ id: crypto.randomUUID(), path, game: null, layout: null }))]);
    setSourcePath('');
  }
  async function browse(directory: boolean, setter?: (value: string) => void) {
    try {
      const selected = await open({ directory, multiple: !setter, filters: directory ? undefined : [{ name: label('archives'), extensions: ['zip', 'rar', '7z'] }] });
      if (!selected) return;
      if (setter) setter(Array.isArray(selected) ? selected[0]! : selected);
      else addPaths(Array.isArray(selected) ? selected : [selected]);
    } catch { setError('KM-MERGE-PICKER-FAILED'); }
  }
  async function execute(exportFiles: boolean) {
    if (inFlight.current || safetyBusy) return;
    inFlight.current = true;
    const snapshot = requestKey;
    setBusy(exportFiles ? 'export' : 'analyze'); setError(null); setWritten(null);
    try {
      if (exportFiles) {
        if (!await armWriteGuard()) return;
        if (latestKey.current !== snapshot) return;
        onExportingChange(true);
      }
      const response = await runMerge({ ...request, reviewToken: exportFiles ? result?.reviewToken : null }, exportFiles);
      if (latestKey.current === snapshot) { setResult(response); setReviewed(snapshot); setSelectedFile(''); }
      if (exportFiles && latestKey.current === snapshot && !response.canExport && !response.issues.some(issue => issue.severity === 'error')
        && response.conflicts.every(conflict => conflict.resolution !== null)) setWritten(response.writtenFiles.length);
    } catch (caught) {
      if (latestKey.current === snapshot) setError(caught instanceof ProjectBridgeError ? caught.apiError.code : exportFiles ? 'KM-MERGE-EXPORT-FAILED' : 'KM-MERGE-ARCHIVE-UNREADABLE');
    } finally {
      inFlight.current = false; setBusy(null);
      if (exportFiles) onExportingChange(false);
    }
  }
  const matchingFiles = (result?.files ?? []).filter(file => file.path.toLocaleLowerCase().includes(query.toLocaleLowerCase()));
  const matchingConflicts = (result?.conflicts ?? []).filter(conflict => (!selectedFile || conflict.file === selectedFile)
    && `${conflict.file} ${conflict.label}`.toLocaleLowerCase().includes(query.toLocaleLowerCase()));
  const unresolved = result?.conflicts.filter(conflict => !choices[conflict.id]).length ?? 0;
  const gameOptions = [{ value: '', label: label('automatic') }, ...games.map(value => ({ value, label: translateLiteral(value === 'za' ? 'Pokemon Legends Z-A' : `Pokemon ${value[0]!.toUpperCase()}${value.slice(1)}`) }))];
  const changeSource = (id: string, update: Partial<MergeSource>) => setSources(current => current.map(source => source.id === id ? { ...source, ...update } : source));
  const prettyLabel = (value: string) => value === 'Complete file' ? label('completeFile') : value === 'Packed archive and descriptor' ? label('packedPair')
    : value.split(' → ').map(part => /^\d{5}$/.test(part) ? `${label('record')} ${Number(part)}` : translateLiteral(part.replace(/([a-z])([A-Z])/g, '$1 $2'))).join(' → ');
  const displayValue = (value: string) => value === 'Removed' ? label('valueRemoved') : formatValue(value);

  return <section className="panel wide-panel merge-workspace" hidden={!active} aria-label={label('title')}>
    {!draftSaved && <p className="field-note">{label('draftUnavailable')}</p>}
    <header className="merge-workspace-heading"><div><h2>{label('title')}</h2><p>{label('intro')}</p></div>
      <div className="merge-workspace-modes" role="group" aria-label={label('mode')}>
        {(['basic', 'advanced'] as const).map(value => <button key={value} type="button" aria-pressed={mode === value}
          onClick={() => setMode(value)}>{label(value)}</button>)}
      </div>
    </header>
    <div className="merge-workspace-help">
    <p className="field-note">{label(`${mode}Help`)}</p>
    <p className="field-note">{label('formatHelp')}</p>
    <p className="field-note merge-workspace-limits">{label('limits')}</p>
    {outputMode === 'bypass' && <p className="field-note">{label('bypassHelp')}</p>}
    </div>
    <div className="merge-workspace-settings merge-workspace-output-settings">
      <div className="path-field"><label htmlFor="merge-game">{label('game')}</label><SearchableOptionInput id="merge-game" ariaLabel={label('game')}
        value={game} options={gameOptions} onChange={setGame} disabled={false} isFiniteCatalog /></div>
      <div className="path-field"><label htmlFor="merge-output-mode">{label('outputMode')}</label><SearchableOptionInput id="merge-output-mode" ariaLabel={label('outputMode')}
        value={outputMode} options={(['standalone', 'trinity', 'bypass'] as const).map(value => ({ value, label: label(value) }))}
        onChange={value => setOutputMode(value as MergeRequest['outputMode'])} disabled={false} isFiniteCatalog /></div>
      <div className="path-field merge-workspace-destination"><label htmlFor="merge-output">{label('destination')}</label>
        <div className="merge-workspace-path"><input id="merge-output" value={outputRoot} onChange={event => setOutputRoot(event.target.value)} />
          <button type="button" onClick={() => void browse(true, setOutputRoot)}>{label('browse')}</button></div></div>
    </div>
    {mode === 'advanced' && <fieldset className="editable-field-group"><legend>{label('originalFiles')}</legend>
      <div className="merge-workspace-settings">{([
        ['romfs', baseRomFs, setBaseRomFs], ['exefs', baseExeFs, setBaseExeFs], ['support', supportFolder, setSupportFolder]
      ] as const).map(([key, value, setter]) => <div className="path-field" key={key}><label htmlFor={`merge-${key}`}>{label(key)}</label>
        <div className="merge-workspace-path"><input id={`merge-${key}`} value={value} onChange={event => setter(event.target.value)} />
          <button type="button" onClick={() => void browse(true, setter)}>{label('browse')}</button></div></div>)}</div>
    </fieldset>}
    {mode === 'basic' && <details><summary>{label('support')}</summary><div className="merge-workspace-path">
      <input aria-label={label('support')} value={supportFolder} onChange={event => setSupportFolder(event.target.value)} />
      <button type="button" onClick={() => void browse(true, setSupportFolder)}>{label('browse')}</button></div></details>}
    <fieldset className="editable-field-group"><legend>{label('sources')}</legend>
      <div className="merge-workspace-source-actions"><button type="button" onClick={() => void browse(false)}>{label('addArchives')}</button>
        <button type="button" onClick={() => void browse(true)}>{label('addFolder')}</button>
        <form className="merge-workspace-path" onSubmit={event => { event.preventDefault(); if (sourcePath.trim()) addPaths([sourcePath.trim()]); }}>
          <input aria-label={label('sourcePath')} placeholder={label('sourcePath')} value={sourcePath} onChange={event => setSourcePath(event.target.value)} />
          <button type="submit" disabled={!sourcePath.trim()}>{label('add')}</button></form></div>
      {sources.length === 0 && <p className="field-note">{label('empty')}</p>}
      <div className="merge-workspace-sources">{sources.map(source => {
        const detected = result?.sources.find(candidate => candidate.id === source.id);
        return <article className="merge-workspace-source" key={source.id}>
          <div className="merge-workspace-source-name"><strong data-localization-ignore>{detected?.name ?? source.path.split(/[\\/]/).at(-1)}</strong><small data-localization-ignore>{source.path}</small>
            {detected && <small>{label('detected')}: {gameOptions.find(option => option.value === detected.game)?.label ?? detected.game ?? label('unknown')} · {label(detected.layout)} · {detected.fileCount} {label('files')}</small>}
            {detected && <details><summary>{label('detectionDetails')}</summary>{detected.evidence.map(item => <p className="field-note" key={item}>{label(`evidence.${item}`)}</p>)}</details>}</div>
          <SearchableOptionInput ariaLabel={label('sourceGame')} disabled={false} value={source.game ?? ''} options={gameOptions} isFiniteCatalog
            onChange={value => changeSource(source.id, { game: value || null })} />
          <SearchableOptionInput ariaLabel={label('sourceLayout')} disabled={false} value={source.layout ?? ''} options={['', 'standalone', 'trinity', 'bypass', 'independent'].map(value => ({ value, label: label(value || 'automatic') }))} isFiniteCatalog
            onChange={value => changeSource(source.id, { layout: value || null })} />
          <button type="button" aria-label={`${label('remove')} ${detected?.name ?? source.path}`} onClick={() => setSources(current => current.filter(candidate => candidate.id !== source.id))}>{label('remove')}</button>
        </article>;
      })}</div>
    </fieldset>
    <div className="merge-workspace-review-bar" role="group" aria-label={label('review')}>
      <button type="button" className="primary-button" disabled={safetyBusy || busy !== null || sources.length === 0 || !outputRoot.trim()}
        onClick={() => void execute(false)}>{busy === 'analyze' ? label('analyzing') : label('review')}</button>
      <button type="button" disabled={safetyBusy || busy !== null || sources.length === 0} onClick={() => {
        setSources([]); setChoices({}); setResult(null); setReviewed(''); setWritten(null); setError(null);
      }}>{label('clear')}</button>
      <span role="status">{result ? `${result.files.length} ${label('files')} · ${unresolved} ${label('unresolved')}${isCurrent ? '' : ` · ${label('needsReview')}`}` : label('readyToAnalyze')}</span>
      <button type="button" className="primary-button" disabled={safetyBusy || busy !== null || !isCurrent || !result?.canExport}
        onClick={() => void execute(true)}>{busy === 'export' ? label('exporting') : label('export')}</button>
    </div>
    {error && <p role="alert" className="field-error">{issueMessage(error)}</p>}
    {result?.issues.map((issue, index) => <p key={`${issue.code}-${index}`} className={issue.severity === 'error' ? 'field-error' : 'field-note'}>
      {issueMessage(issue.code)} {issue.file && <code data-localization-ignore>{issue.file}</code>}</p>)}
    {written !== null && <p role="status">{label('exported')}: {written} {label('files')}</p>}
    {result && <>
      <div className="merge-workspace-filter"><input aria-label={label('search')} placeholder={label('search')} value={query} onChange={event => setQuery(event.target.value)} />
        <button type="button" onClick={() => setSelectedFile('')}>{label('allFiles')}</button></div>
      <div className="merge-workspace-results"><aside aria-label={label('files')} className="merge-workspace-file-list">
        {matchingFiles.slice(filePage * pageSize, (filePage + 1) * pageSize).map(file => <button type="button" key={file.path} aria-pressed={selectedFile === file.path} onClick={() => setSelectedFile(file.path)}>
          <span data-localization-ignore>{file.path}</span><small>{label(file.status)}{file.conflictCount ? ` · ${file.conflictCount}` : ''}</small></button>)}
        <div className="merge-workspace-pagination"><button type="button" disabled={filePage === 0} onClick={() => setFilePage(value => value - 1)}>{label('previous')}</button>
          <button type="button" disabled={(filePage + 1) * pageSize >= matchingFiles.length} onClick={() => setFilePage(value => value + 1)}>{label('next')}</button></div>
      </aside><div className="merge-workspace-conflicts">
        {matchingConflicts.length === 0 && <p>{label('noConflicts')}</p>}
        {matchingConflicts.length > 0 && <div className="merge-workspace-source-actions"><span>{label('useForShown')}</span>
          {result.sources.map(source => <button type="button" key={source.id} onClick={() => setChoices(current => ({ ...current,
            ...Object.fromEntries(matchingConflicts.filter(conflict => conflict.values.some(value => value.sourceId === source.id)).map(conflict => [conflict.id, source.id])) }))}>{source.name}</button>)}</div>}
        <div className="merge-workspace-conflict-grid">{matchingConflicts.slice(page * pageSize, (page + 1) * pageSize).map(conflict => <article key={conflict.id} className="merge-workspace-conflict">
          <header><strong>{prettyLabel(conflict.label)}</strong><small data-localization-ignore>{conflict.file}</small></header>
          {conflict.original !== null && <div className="merge-workspace-original"><span>{label('original')}</span><pre data-localization-ignore>{displayValue(conflict.original)}</pre></div>}
          <div className="merge-workspace-values" role="group" aria-label={prettyLabel(conflict.label)}>{conflict.values.map(value => <button key={value.sourceId} type="button"
            aria-pressed={choices[conflict.id] === value.sourceId} onClick={() => setChoices(current => ({ ...current, [conflict.id]: value.sourceId }))}>
            <strong data-localization-ignore>{value.sourceName}</strong><pre data-localization-ignore>{displayValue(value.value)}</pre>
            <span>{choices[conflict.id] === value.sourceId ? label('selected') : label('choose')}</span></button>)}</div>
        </article>)}</div>
        <div className="merge-workspace-pagination"><button type="button" disabled={page === 0} onClick={() => setPage(value => value - 1)}>{label('previous')}</button>
          <button type="button" disabled={(page + 1) * pageSize >= matchingConflicts.length} onClick={() => setPage(value => value + 1)}>{label('next')}</button></div>
      </div></div>
    </>}
    {result?.projectId && result.game && <button type="button" disabled={safetyBusy || busy !== null || !isCurrent} onClick={() => {
      setReviewed('');
      setSafetyScope({ projectId: result.projectId!, game: result.game as ProjectGame, outputRoot });
    }}>{label('outputSafety')}</button>}
    {safetyScope && <div><p data-localization-ignore>{safetyScope.outputRoot}</p><MergeOutputSafety bridge={bridge} {...safetyScope} active={active}
      armWriteGuard={armWriteGuard} busy={busy !== null} onBusyChange={value => {
        setSafetyBusy(value); if (value) setReviewed(''); onExportingChange(value);
      }} /></div>}
  </section>;
}

function formatValue(value: string) {
  try { return JSON.stringify(JSON.parse(value), null, 2); } catch { return value; }
}
