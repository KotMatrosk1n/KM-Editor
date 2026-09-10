/* SPDX-License-Identifier: GPL-3.0-only */
import { useEffect, useRef, useState } from 'react';
import { ClipboardCheck, RefreshCw, RotateCcw, Save, Shield, UserRound, UsersRound } from 'lucide-react';
import type { ApiDiagnostic, ProjectPaths } from '../../bridge/contracts';
import type { ProjectBridge } from '../../bridge/projectBridge';
import type { ApplyTrainerDynamaxRequest, ApplyTrainerDynamaxResponse, TrainerDynamaxReview,
  TrainerDynamaxSettings, TrainerDynamaxStatus } from '../../bridge/trainerDynamaxContracts';
import { FocusedEditorWorkspace } from '../../components/FocusedEditorWorkspace';
import { usePublishCommonEditorDiagnostics } from '../../components/CommonEditorDiagnostics';
import { LoadingProgress } from '../../components/LoadingProgress';
import { SearchableOptionInput } from '../../components/SearchableOptionInput';
import { useLocalization } from '../../localization';
import { TrainerDynamaxRoster, TrainerDynamaxRowReview, dynamaxSettingsKey } from './TrainerDynamaxRoster';
import './TrainerDynamaxSection.css';

type Props = {
  bridge: ProjectBridge; paths: ProjectPaths; canApply: boolean;
  onApply: (request: ApplyTrainerDynamaxRequest) => Promise<ApplyTrainerDynamaxResponse | null>;
  onDirtyStateChange: (dirty: boolean) => void;
};
const normal: TrainerDynamaxSettings = { disablePlayer: false, disableOpponents: false };

export function TrainerDynamaxSection({ bridge, paths, canApply, onApply, onDirtyStateChange }: Props) {
  const { t, translateLiteral } = useLocalization();
  const [status, setStatus] = useState<TrainerDynamaxStatus | null>(null);
  const [settings, setSettings] = useState(normal);
  const settingsRef = useRef(settings); settingsRef.current = settings;
  const [review, setReview] = useState<TrainerDynamaxReview | null>(null);
  const [busy, setBusy] = useState<'load' | 'review' | 'apply' | null>(null);
  const [diagnostics, setDiagnostics] = useState<ApiDiagnostic[]>([]);
  const [saved, setSaved] = useState(false);
  const alive = useRef(true);
  const lock = useRef(false);
  const dirty = status !== null && dynamaxSettingsKey(settings) !== dynamaxSettingsKey(status.settings);
  usePublishCommonEditorDiagnostics(diagnostics);
  useEffect(() => { onDirtyStateChange(dirty); }, [dirty, onDirtyStateChange]);
  useEffect(() => () => onDirtyStateChange(false), [onDirtyStateChange]);
  const failed = () => setDiagnostics([{ severity: 'error', domain: 'tool.trainerDynamax',
    code: 'KM-SWSH-TRAINER-DYNAMAX-INVALID', message: t('trainerDynamax.failed') }]);
  const load = async () => {
    if (lock.current) return;
    lock.current = true; setBusy('load'); setReview(null); setSaved(false); setDiagnostics([]);
    try {
      const response = await bridge.loadTrainerDynamax({ paths });
      if (!alive.current) return;
      setStatus(response.status); settingsRef.current = response.status.settings; setSettings(response.status.settings); setDiagnostics(response.status.diagnostics);
    } catch { if (alive.current) { setStatus(null); failed(); } }
    finally { lock.current = false; if (alive.current) setBusy(null); }
  };
  useEffect(() => { alive.current = true; void load(); return () => { alive.current = false; }; }, [bridge]);
  const change = (next: TrainerDynamaxSettings) => { settingsRef.current = next; setSettings(next); setReview(null); setSaved(false); };
  const prepare = async () => {
    if (lock.current || !status?.canEdit || !canApply) return;
    lock.current = true; setBusy('review'); setReview(null); setSaved(false); setDiagnostics([]);
    try {
      const submitted = settingsRef.current;
      const response = await bridge.reviewTrainerDynamax({ paths, settings: submitted });
      if (alive.current && dynamaxSettingsKey(settingsRef.current) === dynamaxSettingsKey(submitted)) {
        setReview(response.review); setDiagnostics(response.review.diagnostics);
      }
    } catch { if (alive.current) failed(); }
    finally { lock.current = false; if (alive.current) setBusy(null); }
  };
  const apply = async () => {
    if (lock.current || !review?.reviewToken || !canApply) return;
    lock.current = true; setBusy('apply'); setDiagnostics([]);
    try {
      const response = await onApply({ paths, settings: review.settings, reviewToken: review.reviewToken });
      if (!alive.current) return;
      setReview(null);
      if (!response) { failed(); return; }
      setStatus(response.status);
      const errors = response.applyResult.diagnostics.some(item => item.severity === 'error');
      setDiagnostics([...response.applyResult.diagnostics, ...response.status.diagnostics]);
      if (!errors && dynamaxSettingsKey(settingsRef.current) === dynamaxSettingsKey(review.settings)) { settingsRef.current = response.status.settings; setSettings(response.status.settings); setSaved(true); }
    } catch { if (alive.current) { setReview(null); failed(); } }
    finally { lock.current = false; if (alive.current) setBusy(null); }
  };
  const label = (disabled: boolean) => t(disabled ? 'trainerDynamax.disabled' : 'trainerDynamax.normal');
  return <FocusedEditorWorkspace className="trainer-dynamax-editor">
    <section className="panel wide-panel trainer-dynamax-panel" aria-labelledby="trainer-dynamax-title">
      <div className="trainer-dynamax-heading"><div className="trainer-dynamax-title"><Shield size={24} aria-hidden="true" /><div>
        <h2 id="trainer-dynamax-title">{t('trainerDynamax.title')} <span className="status-pill status-pill-info">{translateLiteral('Beta')}</span></h2>
        <p>{t('trainerDynamax.subtitle')}</p></div></div>
        <button type="button" className="secondary-button" disabled={busy !== null || dirty} onClick={() => void load()}>
          <RefreshCw size={16} aria-hidden="true" />{translateLiteral('Refresh')}</button></div>
      {busy ? <LoadingProgress label={t(`trainerDynamax.progress.${busy}`)} /> : null}
      <h3>{t('trainerDynamax.global')}</h3>
      <div className="trainer-dynamax-controls">
        {(['disablePlayer', 'disableOpponents'] as const).map((field, index) => {
          const Icon = index === 0 ? UserRound : UsersRound;
          const key = index === 0 ? 'player' : 'opponents';
          return <section className="trainer-dynamax-card" key={field} aria-labelledby={`dynamax-${key}`}>
            <div className="trainer-dynamax-card-heading"><Icon size={22} aria-hidden="true" /><h3 id={`dynamax-${key}`}>{t(`trainerDynamax.${key}`)}</h3></div>
            <p>{t(`trainerDynamax.${key}Help`)}</p>
            <div className="trainer-dynamax-field"><span>{t('trainerDynamax.permission')}</span>
              <SearchableOptionInput ariaLabel={t(`trainerDynamax.${key}`)} value={String(settings[field])} disabled={busy === 'load' || busy === 'apply' || !status?.canEdit}
                isFiniteCatalog localizeOptions={false} options={[{ value: 'false', label: label(false) }, { value: 'true', label: label(true) }]}
                onChange={value => change({ ...settings, [field]: value === 'true' })} />
            </div>
            <div className="trainer-dynamax-current"><span>{t('trainerDynamax.current')}</span><strong>{status ? label(status.settings[field]) : t('trainerDynamax.unavailable')}</strong></div>
          </section>;
        })}
      </div>
      <p className="trainer-dynamax-scope">{t('trainerDynamax.scope')}</p>
      {status && (status.trainers?.length ?? 0) > 0 ? <TrainerDynamaxRoster status={status} settings={settings}
        disabled={!status.canEdit || busy === 'load' || busy === 'apply'} onChange={change} /> : null}
      {status?.partial ? <p role="status">{t('trainerDynamax.partial')}</p> : null}
      <div className="trainer-dynamax-actions">
        <button type="button" className="primary-button" disabled={busy !== null || !status?.canEdit || !canApply} onClick={() => void prepare()}>
          <ClipboardCheck size={17} aria-hidden="true" />{t('trainerDynamax.review')}</button>
        <button type="button" className="secondary-button" disabled={busy !== null || !status?.canEdit} onClick={() => change(normal)}>
          <RotateCcw size={16} aria-hidden="true" />{t('trainerDynamax.reset')}</button>
        {dirty ? <button type="button" className="secondary-button" disabled={busy !== null} onClick={() => change(status!.settings)}>{t('trainerDynamax.discard')}</button> : null}
      </div>
      {!canApply && status?.canEdit ? <p role="status">{t('trainerDynamax.notReady')}</p> : null}
      {review?.reviewToken ? <section className="trainer-dynamax-review" aria-labelledby="trainer-dynamax-review-title">
        <div className="trainer-dynamax-heading"><h3 id="trainer-dynamax-review-title">{t('trainerDynamax.reviewTitle')}</h3>
          <span className="status-pill status-pill-info">{t(`trainerDynamax.action.${review.outputAction}`)}</span></div>
        <dl className="trainer-dynamax-review-values"><div><dt>{t('trainerDynamax.player')}</dt><dd>{label(review.settings.disablePlayer)}</dd></div>
          <div><dt>{t('trainerDynamax.opponents')}</dt><dd>{label(review.settings.disableOpponents)}</dd></div>
          <div><dt>{t('trainerDynamax.target')}</dt><dd data-localization-ignore="true">exefs/main</dd></div></dl>
        <TrainerDynamaxRowReview before={status?.settings ?? normal} settings={review.settings} trainers={status?.trainers} />
        <p>{t('trainerDynamax.reviewHelp')}</p>
        <button type="button" className="primary-button" disabled={busy !== null || !canApply || review.outputAction === 'none'} onClick={() => void apply()}>
          <Save size={17} aria-hidden="true" />{t('trainerDynamax.apply')}</button>
      </section> : null}
      {saved ? <p className="trainer-dynamax-result" role="status">{t('trainerDynamax.saved')}</p> : null}
      {!busy && !status?.canEdit ? <p role="status">{t('trainerDynamax.unavailableHelp')}</p> : null}
    </section>
  </FocusedEditorWorkspace>;
}
