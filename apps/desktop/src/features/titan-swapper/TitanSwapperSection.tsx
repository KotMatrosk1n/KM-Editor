/* SPDX-License-Identifier: GPL-3.0-only */
import { useEffect, useRef, useState, type ReactNode } from 'react';
import { ArrowRightLeft, FlaskConical, LockKeyhole, RotateCcw, Save, X } from 'lucide-react';
import { type TitanSwapperUpdate, type TitanSwapperWorkflow } from '../../bridge/titanSwapperContracts';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { FocusedEditorWorkspace } from '../../components/FocusedEditorWorkspace';
import { EditorSessionBar, EditorSessionBarActions } from '../../components/EditorSessionBar';
import { SearchableOptionInput } from '../../components/SearchableOptionInput';
import { WorkflowPanelOutputSections, type WorkflowPanelOutput } from '../../components/workflowPanels';
import { useLocalization } from '../../localization';
import './TitanSwapperSection.css';

type Props = {
  workflow: TitanSwapperWorkflow | null;
  isStaging: boolean;
  isEditing: boolean;
  isEditStarting: boolean;
  onStartEditSession: () => void;
  onCancelEditSession: (onDiscard: () => void) => void;
  onStage: (revision: string, updates: TitanSwapperUpdate[]) => Promise<boolean>;
  onDirtyStateChange: (dirty: boolean) => void;
  panelOutput: WorkflowPanelOutput;
  renderPokemon: (species: number, name: string) => ReactNode;
};
type Draft = TitanSwapperUpdate & { text: string; revision: string };
type Row = TitanSwapperWorkflow['rows'][number];

export function TitanSwapperSection({ workflow, isStaging, isEditing, isEditStarting, onStartEditSession,
  onCancelEditSession, onStage, onDirtyStateChange, panelOutput, renderPokemon }: Props) {
  const { t, translateLiteral } = useLocalization();
  const [drafts, setDrafts] = useState<Record<string, Draft>>({});
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [failed, setFailed] = useState(false);
  const draftsRef = useRef(drafts);
  draftsRef.current = drafts;
  const revision = workflow?.sourceRevision ?? '';
  const entries = Object.values(drafts);
  const available = workflow?.summary.availability === 'available';
  const canEdit = !!available && isEditing;
  const speciesName = (species: number) => workflow?.speciesOptions.find(option => option.value === species)?.label ?? `#${species}`;
  const phaseName = (phase: number) => t(`titanSwapper.phase.${phase}`);
  const value = (row: Row, field: TitanSwapperUpdate['field']) => drafts[`${row.id}/${field}`]?.text ?? String(row.values[field]);
  const invalidDraft = (draft: Draft) => draft.revision !== revision || !/^\d+$/u.test(draft.text)
    || (draft.field === 'level' ? Number(draft.text) < 1 || Number(draft.text) > 100
      : draft.field === 'species' ? !workflow?.speciesOptions.some(option => option.value === Number(draft.text))
      : Number(draft.text) > 1);
  const invalid = entries.some(invalidDraft);
  const rows = (workflow?.rows ?? []).filter(row => `${speciesName(row.storySpecies)} ${phaseName(row.phase)} ${row.id}`
    .toLocaleLowerCase().includes(search.toLocaleLowerCase()));
  const selected = rows.find(row => row.id === selectedId) ?? rows[0];
  const enabled = selected ? value(selected, 'enabled') === '1' : false;
  const combatSpecies = selected ? enabled ? Number(value(selected, 'species')) : selected.storySpecies : 0;

  useEffect(() => { onDirtyStateChange(entries.length > 0); }, [entries.length, onDirtyStateChange]);
  useEffect(() => () => onDirtyStateChange(false), [onDirtyStateChange]);
  usePublishCommonEditorError({ domain: 'workflow.titan-swapper', field: 'fields',
    message: invalid ? t('titanSwapper.invalid') : failed ? t('titanSwapper.failed') : null });

  const setValue = (row: Row, field: TitanSwapperUpdate['field'], text: string) => {
    if (!canEdit) return;
    setFailed(false);
    setDrafts(current => {
      const next = { ...current };
      const key = `${row.id}/${field}`;
      if (text === String(row.values[field])) delete next[key];
      else next[key] = { rowId: row.id, field, value: Number(text), text, revision };
      return next;
    });
  };
  const stage = async () => {
    if (!canEdit || isStaging || invalid || entries.length === 0) return;
    const submitted = { ...draftsRef.current };
    const succeeded = await onStage(revision, Object.values(submitted).map(draft => ({
      rowId: draft.rowId, field: draft.field, value: Number(draft.text)
    })));
    setFailed(!succeeded);
    if (succeeded) setDrafts(current => {
      const next = { ...current };
      for (const [key, draft] of Object.entries(submitted))
        if (next[key]?.text === draft.text && next[key]?.revision === draft.revision) delete next[key];
      return next;
    });
  };

  const restore = (row: Row) => {
    if (!canEdit) return;
    setFailed(false);
    setDrafts(current => {
      const next = Object.fromEntries(Object.entries(current).filter(([, draft]) => draft.rowId !== row.id));
      if (row.values.enabled !== 0) next[`${row.id}/enabled`] = { rowId: row.id, field: 'enabled', value: 0, text: '0', revision };
      return next;
    });
  };

  return <FocusedEditorWorkspace className="titan-swapper-editor">
    <section className="panel wide-panel titan-swapper-panel" aria-labelledby="titan-swapper-heading">
      <div className="panel-heading"><ArrowRightLeft size={20} aria-hidden="true" /><h2 id="titan-swapper-heading">{t('titanSwapper.title')}</h2></div>
      <p className="muted">{t('titanSwapper.intro')}</p>
      {workflow?.diagnostics.some(diagnostic => diagnostic.code === 'KM-SV-TITAN-SWAPPER-SCRIPT-CONFLICT')
        ? <p role="alert">{t('titanSwapper.compatibility')}</p> : null}
      <details className="titan-swapper-beta">
        <summary><FlaskConical size={16} aria-hidden="true" />{t('titanSwapper.beta')}</summary>
        <p>{t('titanSwapper.betaHelp')}</p><p>{t('titanSwapper.limits')}</p><p>{t('titanSwapper.compatibility')}</p><p>{t('titanSwapper.fallback')}</p>
      </details>
      <EditorSessionBar canEdit={!!available && !!workflow?.rows.length} isEditing={isEditing} isStarting={isEditStarting}
        label={t('titanSwapper.title')} onStart={onStartEditSession} />
      {isEditing ? <EditorSessionBarActions>
        <button type="button" className="primary-button" aria-busy={isStaging || undefined}
          disabled={!canEdit || isStaging || invalid || entries.length === 0} onClick={() => void stage()}>
          <Save size={16} aria-hidden="true" />{translateLiteral(isStaging ? 'Staging' : 'Stage')}
        </button>
        <button type="button" className="danger-button" disabled={isStaging}
          onClick={() => onCancelEditSession(() => { setDrafts({}); setFailed(false); })}>
          <X size={16} aria-hidden="true" />{translateLiteral('Cancel')}
        </button>
        <span className="draft-action-summary">{t('titanSwapper.draftCount', { count: entries.length })}</span>
      </EditorSessionBarActions> : null}
      <div className="titan-swapper-layout">
        <nav className="titan-swapper-list" aria-label={t('titanSwapper.encounters')}>
          <label className="field"><span>{t('titanSwapper.search')}</span>
            <input type="search" value={search} onChange={event => setSearch(event.target.value)} /></label>
          <div className="titan-swapper-rows">{rows.map(row => <button type="button" key={row.id}
            className="titan-swapper-row" aria-pressed={selected?.id === row.id} onClick={() => setSelectedId(row.id)}>
            <span className="titan-swapper-row-sprite">{renderPokemon(row.storySpecies, speciesName(row.storySpecies))}</span>
            <span><strong data-localization-ignore="true">{speciesName(row.storySpecies)}</strong><span>{phaseName(row.phase)}</span>
              <small>{entries.some(draft => draft.rowId === row.id) ? t('titanSwapper.edited')
                : value(row, 'enabled') === '1' ? t('titanSwapper.active') : t('titanSwapper.original')}</small></span>
          </button>)}</div>
          {rows.length === 0 ? <p className="muted">{t('titanSwapper.empty')}</p> : null}
        </nav>
        {selected ? <section className="titan-swapper-detail" aria-labelledby="titan-swapper-selected">
          <div className="titan-swapper-detail-heading"><div><h3 id="titan-swapper-selected" data-localization-ignore="true">{speciesName(selected.storySpecies)}</h3>
            <p className="muted">{phaseName(selected.phase)}</p></div>
            <button type="button" className="secondary-button" disabled={!canEdit || !enabled}
              onClick={() => restore(selected)}><RotateCcw size={16} aria-hidden="true" />{t('titanSwapper.restore')}</button>
          </div>
          <div className="titan-swapper-comparison">
            <section className="titan-swapper-card"><h4><LockKeyhole size={16} aria-hidden="true" />{t('titanSwapper.story')}</h4>
              <div className="titan-swapper-preview">{renderPokemon(selected.storySpecies, speciesName(selected.storySpecies))}
                <strong data-localization-ignore="true">{speciesName(selected.storySpecies)}</strong></div>
              <p className="muted">{t('titanSwapper.storyHelp')}</p>
            </section>
            <section className="titan-swapper-card titan-swapper-combat"><h4>{t('titanSwapper.combat')}</h4>
              <div className="titan-swapper-preview">{renderPokemon(combatSpecies, speciesName(combatSpecies))}
                <strong data-localization-ignore="true">{speciesName(combatSpecies)}</strong></div>
              <label className="titan-swapper-enable"><input type="checkbox" checked={enabled} disabled={!canEdit}
                onChange={event => setValue(selected, 'enabled', event.target.checked ? '1' : '0')} />{t('titanSwapper.enable')}</label>
              <div className="titan-swapper-fields"><div className="field"><span>{t('titanSwapper.field.species')}</span>
                <SearchableOptionInput ariaLabel={t('titanSwapper.field.species')} isFiniteCatalog localizeOptions={false}
                  disabled={!canEdit || !enabled} options={workflow?.speciesOptions ?? []} value={value(selected, 'species')}
                  ariaInvalid={drafts[`${selected.id}/species`] ? invalidDraft(drafts[`${selected.id}/species`]) : undefined}
                  onChange={text => setValue(selected, 'species', text)} />
              </div><label className="field"><span>{t('titanSwapper.field.level')}</span>
                <input type="text" inputMode="numeric" disabled={!canEdit || !enabled} value={value(selected, 'level')}
                  aria-invalid={drafts[`${selected.id}/level`] ? invalidDraft(drafts[`${selected.id}/level`]) || undefined : undefined}
                  onChange={event => setValue(selected, 'level', event.target.value)} /></label></div>
              <p className="muted">{t('titanSwapper.combatHelp')}</p>
            </section>
          </div>
          <p className="titan-swapper-phase-help">{t('titanSwapper.phaseHelp')}</p>
          <details><summary>{t('titanSwapper.details')}</summary><p data-localization-ignore="true"><code>{selected.id}</code></p>
            <p>{t('titanSwapper.outputHelp')}</p></details>
        </section> : null}
      </div>
    </section>
    <WorkflowPanelOutputSections output={panelOutput} workflowDiagnostics={workflow?.diagnostics ?? []} />
  </FocusedEditorWorkspace>;
}
