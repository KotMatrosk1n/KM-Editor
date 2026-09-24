/* SPDX-License-Identifier: GPL-3.0-only */
import { useEffect, useRef, useState, type KeyboardEvent } from 'react';
import { RefreshCw, RotateCcw, Save, Sparkles, X } from 'lucide-react';
import { type EditSession } from '../../bridge/contracts';
import { type FairyGymBoostSelection } from '../../bridge/fairyGymBoostsContracts';
import { encodeMarnieBoostSelections, getMarnieBoostPendingSelections, marnieBoostIds,
  type MarnieBoostsWorkflow } from '../../bridge/marnieBoostsContracts';
import { FairyGymBoostCard, parseOutcomeValue } from '../fairy-gym-boosts/FairyGymBoostsSection';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { EditorSessionBar, EditorSessionBarActions } from '../../components/EditorSessionBar';
import { FocusedEditorWorkspace } from '../../components/FocusedEditorWorkspace';
import { WorkflowPanelOutputSections, type WorkflowPanelOutput } from '../../components/workflowPanels';
import { useLocalization } from '../../localization';

const battles = [
  { question: "My cheers will really get you goin’!", answers: ['Thanks!', 'Thanks for the help!'], effect: 6 },
  { question: 'Feel that flow! Yeah! You feelin’ it, [player]?', answers: ['Yeah!', 'Let’s go!'], effect: 4 },
  { question: 'Yeah! Have some of my support! I know you can keep the beat goin’!',
    answers: ['You’re the best, Marnie!', 'Dance, Pokémon!'], effect: 2 }
];
const vanilla: FairyGymBoostSelection[] = marnieBoostIds.map((boostId, index) =>
  ({ boostId, effectId: battles[Math.floor(index / 2)].effect, resultKind: 'increase' }));
type Props = {
  workflow: MarnieBoostsWorkflow | null; session: EditSession | null;
  isEditing: boolean; isEditStarting: boolean; isStaging: boolean; isLoading: boolean;
  onStartEditSession: () => void; onCancelEditSession: (discard: () => void) => void;
  onStage: (selections: FairyGymBoostSelection[]) => Promise<boolean>;
  onDirtyStateChange: (dirty: boolean) => void; onRefresh: () => void; panelOutput: WorkflowPanelOutput;
};

export function MarnieBoostsSection({ workflow, session, isEditing, isEditStarting, isStaging, isLoading,
  onStartEditSession, onCancelEditSession, onStage, onDirtyStateChange, onRefresh, panelOutput }: Props) {
  const { t, translateLiteral } = useLocalization();
  const pending = getMarnieBoostPendingSelections(session);
  const clean = pending ?? workflow?.selections ?? [];
  const [draft, setDraft] = useState<FairyGymBoostSelection[] | null>(null);
  const draftRef = useRef(draft);
  draftRef.current = draft;
  const [failed, setFailed] = useState(false);
  const [selectedBattle, setSelectedBattle] = useState(0);
  const desired = draft ?? clean;
  const dirty = draft !== null && encodeMarnieBoostSelections(draft) !== encodeMarnieBoostSelections(clean);
  const canEdit = workflow?.canEdit === true && isEditing;
  useEffect(() => { onDirtyStateChange(dirty); }, [dirty, onDirtyStateChange]);
  useEffect(() => () => onDirtyStateChange(false), [onDirtyStateChange]);
  usePublishCommonEditorError({ domain: 'workflow.marnieBoosts', field: 'boostSelections',
    message: failed ? t('marnieBoosts.failed') : null });
  const stage = async () => {
    if (!canEdit || !dirty || isStaging) return;
    const submitted = desired;
    setFailed(false);
    if (!await onStage(submitted)) { setFailed(true); return; }
    if (draftRef.current === submitted) setDraft(null);
  };
  const change = (boostId: string, value: string) => {
    setDraft(desired.map(selection => selection.boostId === boostId ? { boostId, ...parseOutcomeValue(value) } : selection));
    setFailed(false);
  };
  const tabKey = (event: KeyboardEvent<HTMLButtonElement>, index: number) => {
    const next = event.key === 'Home' ? 0 : event.key === 'End' ? 2 :
      ['ArrowRight', 'ArrowDown'].includes(event.key) ? (index + 1) % 3 :
      ['ArrowLeft', 'ArrowUp'].includes(event.key) ? (index + 2) % 3 : null;
    if (next === null) return;
    event.preventDefault(); setSelectedBattle(next);
    event.currentTarget.parentElement?.querySelectorAll<HTMLButtonElement>('[role="tab"]').item(next).focus();
  };
  const battle = battles[selectedBattle];
  return <FocusedEditorWorkspace>
    <section className="panel wide-panel" aria-labelledby="marnie-boosts-heading">
      <div className="panel-heading"><Sparkles size={18} aria-hidden="true" />
        <h2 id="marnie-boosts-heading">{t('marnieBoosts.title')}</h2>
        <button className="secondary-button compact-button" type="button" onClick={onRefresh}
          disabled={isLoading || isStaging} aria-busy={isLoading || undefined}>
          <RefreshCw size={16} aria-hidden="true" /><span>{translateLiteral('Refresh')}</span>
        </button>
      </div>
      <p>{t('marnieBoosts.subtitle')}</p>
      <EditorSessionBar canEdit={workflow?.canEdit === true} isEditing={isEditing} isStarting={isEditStarting}
        label={t('marnieBoosts.title')} onStart={onStartEditSession} readOnlyReason={t('marnieBoosts.readOnly')} />
      {isEditing ? <EditorSessionBarActions>
        <button className="danger-button" type="button" disabled={!canEdit || encodeMarnieBoostSelections(desired) === encodeMarnieBoostSelections(vanilla)}
          onClick={() => { setDraft(vanilla); setFailed(false); }}>
          <RotateCcw size={16} aria-hidden="true" /><span>{translateLiteral('Restore to Vanilla')}</span>
        </button>
        <button className="primary-button" type="button" disabled={!canEdit || !dirty || isStaging}
          aria-busy={isStaging || undefined} onClick={() => void stage()}>
          <Save size={16} aria-hidden="true" /><span>{translateLiteral(isStaging ? 'Staging' : 'Stage')}</span>
        </button>
        <button className="danger-button" type="button" disabled={isStaging}
          onClick={() => onCancelEditSession(() => { setDraft(null); setFailed(false); })}>
          <X size={16} aria-hidden="true" /><span>{translateLiteral('Cancel')}</span>
        </button>
        <span className="draft-action-summary">{t(dirty ? 'marnieBoosts.draft' : pending ? 'marnieBoosts.staged' : 'marnieBoosts.clean')}</span>
      </EditorSessionBarActions> : null}
      <div className="fairy-gym-editor">
        <div className="fairy-gym-trainer-tabs" role="tablist" aria-labelledby="marnie-boosts-heading">
          {battles.map((_, index) => <button key={index} id={`marnie-tab-${index}`} role="tab" type="button"
            tabIndex={selectedBattle === index ? 0 : -1} aria-selected={selectedBattle === index}
            aria-controls={selectedBattle === index ? 'marnie-boost-panel' : undefined}
            className={`fairy-gym-trainer-tab${selectedBattle === index ? ' is-selected' : ''}`}
            onClick={() => setSelectedBattle(index)} onKeyDown={event => tabKey(event, index)}>
            {t('marnieBoosts.battle', { number: index + 1 })}
          </button>)}
        </div>
        <div className="fairy-gym-boost-stack" role="tabpanel" tabIndex={0} id="marnie-boost-panel"
          aria-labelledby={`marnie-tab-${selectedBattle}`}>
          {battle.answers.map((answerText, index) => {
            const original = vanilla[selectedBattle * 2 + index];
            const current = workflow?.selections.find(value => value.boostId === original.boostId);
            return <FairyGymBoostCard key={original.boostId} disabled={!canEdit} showAnswerRole={false}
              onChange={change} translateLiteral={translateLiteral}
              selection={desired.find(value => value.boostId === original.boostId) ?? original}
              boost={{ ...original, ...current, defaultEffectId: original.effectId, defaultResultKind: original.resultKind,
                questionText: battle.question, answerText, answerChoice: index + 1, isAvailable: current !== undefined,
                affectedStats: [], stageAmount: 2, effectLabel: '', sequenceFile: `bk${135 + selectedBattle}.bseq` }} />;
          })}
        </div>
        <p>{t('marnieBoosts.bounds')}</p>
        <p>{t('marnieBoosts.restoreHelp')}</p>
      </div>
    </section>
    <WorkflowPanelOutputSections output={panelOutput} workflowDiagnostics={workflow?.diagnostics ?? []} />
  </FocusedEditorWorkspace>;
}
