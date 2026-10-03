/* SPDX-License-Identifier: GPL-3.0-only */
import { useEffect, useRef, useState } from 'react';
import { RefreshCw, RotateCcw, Save, Settings2, X } from 'lucide-react';
import { type EditSession } from '../../bridge/contracts';
import { defaultGameOptionsSelections, encodeGameOptionsSelections, gameOptionsCatalog, getGameOptionsPendingSelections,
  normalizeGameOptionsSelections, type GameOptionsWorkflow } from '../../bridge/gameOptionsContracts';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { EditorSessionBar, EditorSessionBarActions } from '../../components/EditorSessionBar';
import { FocusedEditorWorkspace } from '../../components/FocusedEditorWorkspace';
import { SearchableOptionInput } from '../../components/SearchableOptionInput';
import { WorkflowPanelOutputSections, type WorkflowPanelOutput } from '../../components/workflowPanels';
import { useLocalization } from '../../localization';
import './GameOptionsSection.css';

type Props = {
  workflow: GameOptionsWorkflow | null; session: EditSession | null;
  isEditing: boolean; isEditStarting: boolean; isStaging: boolean; isLoading: boolean;
  onStartEditSession: () => void; onCancelEditSession: (discard: () => void) => void;
  onStage: (selections: number[]) => Promise<boolean>;
  onDirtyStateChange: (dirty: boolean) => void; onRefresh: () => void; panelOutput: WorkflowPanelOutput;
};

export function GameOptionsSection({ workflow, session, isEditing, isEditStarting, isStaging, isLoading,
  onStartEditSession, onCancelEditSession, onStage, onDirtyStateChange, onRefresh, panelOutput }: Props) {
  const { t, translateLiteral } = useLocalization();
  const pending = getGameOptionsPendingSelections(session);
  const clean = pending ?? workflow?.selections ?? [];
  const [draft, setDraft] = useState<number[] | null>(null);
  const draftRef = useRef(draft); draftRef.current = draft;
  const [failed, setFailed] = useState(false);
  const desired = draft ?? clean;
  const normalized = normalizeGameOptionsSelections(desired);
  const dirty = draft !== null && encodeGameOptionsSelections(draft) !== encodeGameOptionsSelections(clean);
  const canEdit = workflow?.canEdit === true && isEditing;
  useEffect(() => { onDirtyStateChange(dirty); }, [dirty, onDirtyStateChange]);
  useEffect(() => () => onDirtyStateChange(false), [onDirtyStateChange]);
  usePublishCommonEditorError({ domain: 'workflow.gameOptions', field: 'selections',
    message: dirty && !normalized ? t('gameOptions.invalid') : failed ? t('gameOptions.failed') : null });
  const stage = async () => {
    if (!canEdit || !dirty || !normalized || isStaging) return;
    const submitted = draft;
    setFailed(false);
    try {
      if (!await onStage(normalized)) { setFailed(true); return; }
      if (draftRef.current === submitted) setDraft(null);
    } catch { setFailed(true); }
  };
  let offset = 0;
  return <FocusedEditorWorkspace>
    <section className="panel wide-panel game-options" aria-labelledby="game-options-heading">
      <div className="panel-heading"><Settings2 size={18} aria-hidden="true" />
        <h2 id="game-options-heading">{t('gameOptions.title')}</h2>
        <button className="secondary-button compact-button" type="button" onClick={onRefresh}
          disabled={isLoading || isStaging} aria-busy={isLoading || undefined}>
          <RefreshCw size={16} aria-hidden="true" /><span>{translateLiteral('Refresh')}</span>
        </button>
      </div>
      <p>{t('gameOptions.subtitle')}</p>
      <p id="game-options-help">{t('gameOptions.help')}</p>
      <EditorSessionBar canEdit={workflow?.canEdit === true} isEditing={isEditing} isStarting={isEditStarting}
        label={t('gameOptions.title')} onStart={onStartEditSession} readOnlyReason={t('gameOptions.readOnly')} />
      {isEditing ? <EditorSessionBarActions>
        <button className="danger-button" type="button" disabled={!canEdit || desired.every(value => value === 0)}
          onClick={() => { setDraft([...defaultGameOptionsSelections]); setFailed(false); }}>
          <RotateCcw size={16} aria-hidden="true" /><span>{translateLiteral('Restore to Vanilla')}</span>
        </button>
        <button className="primary-button" type="button" disabled={!canEdit || !dirty || !normalized || isStaging}
          aria-busy={isStaging || undefined} onClick={() => void stage()}>
          <Save size={16} aria-hidden="true" /><span>{translateLiteral(isStaging ? 'Staging' : 'Stage')}</span>
        </button>
        <button className="danger-button" type="button" disabled={isStaging}
          onClick={() => onCancelEditSession(() => { setDraft(null); setFailed(false); })}>
          <X size={16} aria-hidden="true" /><span>{translateLiteral('Cancel')}</span>
        </button>
        <span className="draft-action-summary">{t(dirty ? 'gameOptions.draft' : pending ? 'gameOptions.staged' : 'gameOptions.clean')}</span>
      </EditorSessionBarActions> : null}
      <div className="game-options-grid">
        {gameOptionsCatalog.map(option => {
          const start = offset; offset += option.choices.length;
          const values = desired.slice(start, offset);
          const starting = values.indexOf(1) >= 0 ? values.indexOf(1)
            : values[option.retail] === 0 ? option.retail : values.indexOf(0);
          const label = t(`gameOptions.option.${option.id}`);
          const choiceLabel = (choice: string) => /^\d+$/.test(choice) ? choice : t(`gameOptions.choice.${choice}`);
          const invalid = values.length === option.choices.length && starting < 0;
          return <fieldset className="game-option-card" key={option.id} aria-describedby="game-options-help">
            <legend>{label}</legend>
            {option.id === 'characters' ? <p>{t('gameOptions.charactersHelp')}</p> : null}
            {['music', 'soundEffects', 'cries'].includes(option.id) ? <p>{t('gameOptions.audioHelp')}</p> : null}
            <div className="game-option-choices">
              {option.choices.map((choice, index) => {
                const value = values[index] ?? 0;
                return <div className="game-option-choice" key={choice}>
                  <span className="game-option-choice-name">{choiceLabel(choice)}</span>
                  <SearchableOptionInput disabled={!canEdit} value={String(value & 3)} ariaInvalid={invalid || undefined}
                    ariaLabel={`${label}: ${choiceLabel(choice)}: ${t('gameOptions.starting')}`}
                    options={[{ value: '0', label: t('gameOptions.default'), disabled: values.includes(1) && (value & 3) !== 1 }, { value: '1', label: t('gameOptions.on') },
                      { value: '2', label: t('gameOptions.off') }]} localizeOptions={false} isFiniteCatalog
                    onChange={selected => {
                      const next = [...desired]; const state = Number(selected);
                      if (state === 1) option.choices.forEach((_, sibling) => {
                        next[start + sibling] = sibling === index ? 1 : (next[start + sibling] & 4) | 2;
                      });
                      else next[start + index] = (value & 4) | (state === 0 && values.includes(1) && (value & 3) !== 1 ? 2 : state);
                      setDraft(next); setFailed(false);
                    }} />
                  <label className="game-option-hide"><input type="checkbox" checked={(value & 4) !== 0}
                    disabled={!canEdit || (value & 3) === 1}
                    aria-label={`${label}: ${choiceLabel(choice)}: ${t('gameOptions.hide')}`}
                    onChange={event => {
                      const next = [...desired]; next[start + index] = (value & 3) | (event.target.checked ? 4 : 0);
                      setDraft(next); setFailed(false);
                    }} />{t('gameOptions.hide')}</label>
                </div>;
              })}
            </div>
            <p className="game-option-start" aria-live="polite">{invalid ? t('gameOptions.invalid') :
              starting < 0 ? t('gameOptions.unavailable') : option.id === 'characters' && values.every(value => value === 0)
                ? t('gameOptions.languageDefault') : t('gameOptions.startValue', { value: choiceLabel(option.choices[starting]) })}</p>
          </fieldset>;
        })}
      </div>
      <p>{t('gameOptions.saveHelp')}</p>
      <p>{t('gameOptions.packageHelp')}</p>
    </section>
    <WorkflowPanelOutputSections output={panelOutput} workflowDiagnostics={workflow?.diagnostics ?? []} />
  </FocusedEditorWorkspace>;
}
