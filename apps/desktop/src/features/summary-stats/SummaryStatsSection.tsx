/* SPDX-License-Identifier: GPL-3.0-only */
import { EditorWorkspaceSection, RecordEditorWorkspace } from '../../components/SectionedEditorWorkspace';
import { useEffect, useRef, useState } from 'react';
import { ChartNoAxesCombined, RotateCcw, Save, X } from 'lucide-react';
import { type SummaryStatsWorkflow } from '../../bridge/summaryStatsContracts';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { FocusedEditorWorkspace } from '../../components/FocusedEditorWorkspace';
import { EditorSessionBar, EditorSessionBarActions } from '../../components/EditorSessionBar';
import { WorkflowPanelOutputSections, type WorkflowPanelOutput } from '../../components/workflowPanels';
import { ContextHelp } from '../../components/ContextHelp';
import { useLocalization } from '../../localization';
import './SummaryStatsSection.css';

type Props = {
  workflow: SummaryStatsWorkflow | null;
  isStaging: boolean;
  isEditing: boolean;
  isEditStarting: boolean;
  onStartEditSession: () => void;
  onCancelEditSession: (onDiscard: () => void) => void;
  onStage: (revision: string, enabled: boolean) => Promise<boolean>;
  onDirtyStateChange: (dirty: boolean) => void;
  panelOutput: WorkflowPanelOutput;
};
type Draft = { enabled: boolean; revision: string };

export function SummaryStatsSection({ workflow, isStaging, isEditing, isEditStarting,
  onStartEditSession, onCancelEditSession, onStage, onDirtyStateChange, panelOutput }: Props) {
  const { t, translateLiteral } = useLocalization();
  const [draft, setDraft] = useState<Draft | null>(null);
  const [failed, setFailed] = useState(false);
  const draftRef = useRef(draft);
  draftRef.current = draft;
  const revision = workflow?.sourceRevision ?? '';
  const available = workflow?.summary.availability === 'available';
  const canEdit = !!available && isEditing;
  const stale = draft !== null && draft.revision !== revision;
  const enabled = draft?.enabled ?? workflow?.enabled ?? false;
  useEffect(() => { onDirtyStateChange(draft !== null); }, [draft, onDirtyStateChange]);
  useEffect(() => () => onDirtyStateChange(false), [onDirtyStateChange]);
  usePublishCommonEditorError({ domain: 'workflow.summaryStats', field: 'enabled',
    message: stale ? t('summaryStats.stale') : failed ? t('summaryStats.failed') : null });
  const choose = (value: boolean) => {
    if (!canEdit) return;
    setFailed(false);
    setDraft({ enabled: value, revision });
  };
  const stage = async () => {
    if (!canEdit || isStaging || stale || !revision) return;
    const submitted = draftRef.current;
    const succeeded = await onStage(revision, submitted?.enabled ?? workflow?.enabled ?? false);
    if (draftRef.current === submitted) {
      setFailed(!succeeded);
      if (succeeded) setDraft(current => current === submitted ? null : current);
    }
  };
  return <FocusedEditorWorkspace className="summary-stats-editor">
    <section className="panel wide-panel" aria-labelledby="summary-stats-heading">
      <div className="panel-heading"><ChartNoAxesCombined size={20} aria-hidden="true" /><h2 id="summary-stats-heading">{t('summaryStats.title')}</h2>
        <ContextHelp label={t('summaryStats.title')}>
          <p>{t('summaryStats.intro')}</p><p>{t('summaryStats.controls')}</p>
          <p>{t('summaryStats.preservation')}</p><p>{t('summaryStats.package')}</p>
        </ContextHelp>
      </div>
      <EditorSessionBar canEdit={!!available} isEditing={isEditing} isStarting={isEditStarting}
        label={t('summaryStats.title')} onStart={onStartEditSession} />
      {isEditing ? <EditorSessionBarActions>
        <button type="button" className="primary-button" aria-busy={isStaging || undefined}
          disabled={!canEdit || isStaging || stale} onClick={() => void stage()}>
          <Save size={16} aria-hidden="true" />{translateLiteral(isStaging ? 'Staging' : 'Stage')}
        </button>
        <button type="button" className="danger-button" disabled={isStaging}
          onClick={() => onCancelEditSession(() => { setDraft(null); setFailed(false); })}>
          <X size={16} aria-hidden="true" />{translateLiteral('Cancel')}
        </button>
      </EditorSessionBarActions> : null}
      <RecordEditorWorkspace id="summary-stats" className="summary-stats-layout" initialSection="setting">
        <EditorWorkspaceSection id="setting" label={translateLiteral('Settings')}>
          <div className="summary-stats-setting editor-setting-row">
            <label><input type="checkbox" checked={enabled} disabled={!canEdit}
              onChange={event => choose(event.target.checked)} /><span>{t('summaryStats.enable')}</span></label>
            <button type="button" className="secondary-button" disabled={!canEdit} onClick={() => choose(false)}>
              <RotateCcw size={16} aria-hidden="true" />{t('summaryStats.restore')}
            </button>
          </div>
        </EditorWorkspaceSection>
        <EditorWorkspaceSection id="views" label={t('summaryStats.cycle')}>
          <ol className="summary-stats-modes" aria-label={t('summaryStats.cycle')}>
            {(['stats', 'ivs', 'evs', 'graph'] as const).map(mode => <li key={mode}>
              <strong>{t(`summaryStats.mode.${mode}`)}</strong><p>{t(`summaryStats.mode.${mode}.help`)}</p>
            </li>)}
          </ol>
        </EditorWorkspaceSection>
      </RecordEditorWorkspace>
    </section>
    <WorkflowPanelOutputSections output={panelOutput} workflowDiagnostics={workflow?.diagnostics ?? []} />
  </FocusedEditorWorkspace>;
}
