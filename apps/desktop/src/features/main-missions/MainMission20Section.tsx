/* SPDX-License-Identifier: GPL-3.0-only */

import { Clapperboard, Save, X } from 'lucide-react';
import { useEffect, useId, useState } from 'react';
import type { EditSession } from '../../bridge/contracts';
import type { MainMission20Update, MainMission20Workflow } from '../../bridge/mainMission20Contracts';
import { ContextHelp } from '../../components/ContextHelp';
import { EditorFieldGroup } from '../../components/EditorRecordDetails';
import { EditorSessionBar, EditorSessionBarActions } from '../../components/EditorSessionBar';
import { FieldLabel } from '../../components/FieldLabel';
import { FocusedEditorWorkspace } from '../../components/FocusedEditorWorkspace';
import { SearchableOptionInput } from '../../components/SearchableOptionInput';
import { WorkflowPanelOutputSections, type WorkflowPanelOutput } from '../../components/workflowPanels';
import { useLocalization } from '../../localization';
import './mainMission20Section.css';

type Props = {
  workflow: MainMission20Workflow | null;
  session: EditSession | null;
  isEditStarting: boolean;
  isStaging: boolean;
  onStartEditSession: () => void | Promise<void>;
  onCancelEditSession: (discard: () => void) => void;
  onStage: (sourceRevision: string, updates: MainMission20Update[]) => Promise<boolean>;
  onDirtyStateChange: (dirty: boolean) => void;
  panelOutput: WorkflowPanelOutput;
};

type SlotId = MainMission20Update['slotId'];
type Drafts = Partial<Record<SlotId, number>>;

export function MainMission20Section({ workflow, session, isEditStarting, isStaging, onStartEditSession,
  onCancelEditSession, onStage, onDirtyStateChange, panelOutput }: Props) {
  const { t, translateLiteral } = useLocalization();
  const id = useId();
  const [drafts, setDrafts] = useState<Drafts>({});
  const title = t('workbench.section.main-mission-20.label');
  const sceneLabel = t('mainMission20.scene');
  const revision = workflow?.sourceRevision ?? '';
  const canEdit = workflow?.summary.availability === 'available' && Boolean(workflow.slots.length);
  const pending = session?.pendingEdits.filter(edit => edit.domain === 'workflow.main-mission-20'
    && edit.field === 'species' && edit.owner === revision) ?? [];
  const slots = (workflow?.slots ?? []).map(slot => {
    const staged = pending.find(edit => edit.recordId === slot.id)?.newValue;
    return { ...slot, stagedSpecies: staged ? Number(staged) : slot.species };
  });
  const options = workflow?.speciesOptions ?? [];
  const updates = slots.flatMap(slot => {
    const species = drafts[slot.id];
    return species !== undefined && species !== slot.stagedSpecies ? [{ slotId: slot.id, species }] : [];
  });
  const dirty = updates.length > 0;

  useEffect(() => { onDirtyStateChange(dirty); }, [dirty, onDirtyStateChange]);
  useEffect(() => () => onDirtyStateChange(false), [onDirtyStateChange]);

  const stage = async () => {
    if (!canEdit || !session || isStaging || !dirty) return;
    if (!await onStage(revision, updates)) return;
    setDrafts(current => {
      const next = { ...current };
      for (const { slotId, species } of updates) {
        if (next[slotId] === species) delete next[slotId];
      }
      return next;
    });
  };

  return (
    <FocusedEditorWorkspace>
      <section className="panel wide-panel" aria-labelledby={`${id}-heading`}>
        <div className="panel-heading">
          <Clapperboard size={20} aria-hidden="true" />
          <h2 id={`${id}-heading`}>{title}</h2>
        </div>
        <EditorSessionBar canEdit={canEdit} isEditing={session !== null} isStarting={isEditStarting}
          label={title} onStart={onStartEditSession} />
        {session ? (
          <EditorSessionBarActions>
            <button type="button" className="primary-button" aria-busy={isStaging || undefined}
              disabled={!canEdit || isStaging || !dirty} onClick={() => void stage()}>
              <Save size={16} aria-hidden="true" />
              {translateLiteral(isStaging ? 'Staging' : 'Stage')}
            </button>
            <button type="button" className="danger-button" disabled={isStaging}
              onClick={() => onCancelEditSession(() => setDrafts({}))}>
              <X size={16} aria-hidden="true" />
              {translateLiteral('Cancel')}
            </button>
          </EditorSessionBarActions>
        ) : null}
        {slots.length > 0 ? (
          <EditorFieldGroup label={sceneLabel} headingValue={
            <ContextHelp label={sceneLabel}>
              <p data-localization-ignore="true">{workflow?.missionTitle}</p>
              <p>{t('mainMission20.sceneHelp')}</p>
            </ContextHelp>
          }>
            <div className="editable-field-grid main-mission-20-fields">
              {slots.map(slot => {
                const inputId = `main-mission-20-${slot.id}`;
                const label = t(`mainMission20.slot.${slot.id}`);
                const value = drafts[slot.id] ?? slot.stagedSpecies;
                const original = options.find(option => option.value === slot.originalSpecies)?.label ?? String(slot.originalSpecies);
                return (
                  <div className={`path-field editable-field-control${value !== slot.stagedSpecies ? ' editable-field-changed' : ''}`} key={slot.id}>
                    <FieldLabel htmlFor={inputId} label={label} help={t('mainMission20.original', { pokemon: original })} />
                    <SearchableOptionInput ariaLabel={label} id={inputId} disabled={!session || !canEdit}
                      data-km-source-site="main-mission-20-species" isFiniteCatalog localizeOptions={false}
                      options={options} value={String(value)} wrapSelectedValue
                      onChange={next => {
                        const species = Number(next);
                        if (!options.some(option => option.value === species)) return;
                        setDrafts(current => {
                          const drafts = { ...current };
                          if (species === slot.stagedSpecies && !isStaging) delete drafts[slot.id];
                          else drafts[slot.id] = species;
                          return drafts;
                        });
                      }} />
                  </div>
                );
              })}
            </div>
          </EditorFieldGroup>
        ) : null}
      </section>
      <WorkflowPanelOutputSections output={panelOutput} workflowDiagnostics={workflow?.diagnostics ?? []} />
    </FocusedEditorWorkspace>
  );
}
