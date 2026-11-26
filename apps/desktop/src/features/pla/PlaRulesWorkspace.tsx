/* SPDX-License-Identifier: GPL-3.0-only */
import { plaFieldHelp } from './plaFieldHelp';
import type { ReactNode } from 'react';
import { SlidersVertical } from 'lucide-react';
import type { PlaField, PlaPage } from '../../bridge/plaProjectBridge';
import { EditorWorkspaceSection, SectionedEditorWorkspace } from '../../components/SectionedEditorWorkspace';
import { FieldLabel } from '../../components/FieldLabel';
import { ContextHelp } from '../../components/ContextHelp';
import { useLocalization } from '../../localization';

export function PlaRulesWorkspace({ sessionKey, kind, page, controls, sessionBar, actions, refresh, loading, fieldLabel, renderInput }: {
  sessionKey: string; kind: 'capture' | 'friendship' | 'shiny' | 'bagPrices' | 'charmPrices'; page: PlaPage | null; controls: ReactNode; sessionBar: ReactNode;
  actions: ReactNode; refresh: ReactNode; loading: boolean; fieldLabel: (field: PlaField) => string; renderInput: (field: PlaField) => ReactNode;
}) {
  const { t, translateLiteral } = useLocalization();
  const fields = page?.fields ?? [];
  const groups = kind === 'capture' ? [
    { title: t('pla.rules.balls'), fields: fields.filter(field => field.key.startsWith('ball')) },
    { title: t('pla.rules.conditions'), fields: fields.filter(field => !field.key.startsWith('ball')) }
  ] : kind === 'friendship' ? [
    ...['Low', 'Mid', 'High'].map(band => ({ title: t('pla.rules.band' + band), fields: fields.filter(field => field.key.endsWith(band)) })),
    { title: t('pla.rules.experience'), fields: fields.filter(field => field.key === 'friendshipExpChance') }
  ] : kind === 'bagPrices' ? [0, 1, 2, 3].map(group => ({
    title: t('pla.rules.expansions', { first: group * 10 + 1, last: Math.min(group * 10 + 10, 39) }),
    fields: fields.slice(group * 10, group * 10 + 10)
  })) : kind === 'charmPrices' ? [
    { title: t('pla.rules.sharedCharms'), fields: fields.filter(field => field.key === 'sharedCharmPrice') },
    ...['warding', 'survival', 'tempting'].map(charm => ({ title: t('pla.rules.' + charm), fields: fields.filter(field => field.key.startsWith(charm + 'Price.')) }))
  ] : [{ title: t('pla.kind.shiny'), fields }];
  return <section className="panel wide-panel pla-editor" aria-labelledby="pla-editor-title">
    <div className="panel-heading"><SlidersVertical size={18} aria-hidden="true" /><h2 id="pla-editor-title">{t('workbench.section.gameplayRules.label')}</h2><ContextHelp label={t('workbench.section.gameplayRules.label')}>{t('pla.notice.' + kind)}</ContextHelp>{refresh}</div>
    <SectionedEditorWorkspace sessionKey={sessionKey} initialSection="rules" compactFields sectionTransitionDuration={250}
      sessionBar={<>{sessionBar}{actions}</>} browserControls={controls}>
      <EditorWorkspaceSection id="rules" label={t('pla.kind.' + kind)}>
        {loading ? <p role="status">{translateLiteral('Loading')}</p> : null}
        <div className="editable-field-groups pla-rule-groups">
          {groups.filter(group => group.fields.length > 0).map(group => <fieldset className="editable-field-group" key={group.title}>
            <legend>{group.title}</legend>
            <div className="editable-field-grid">{group.fields.map(field => <div className="path-field editable-field-control" key={field.key}>
              <FieldLabel htmlFor={'pla-field-' + field.key} label={fieldLabel(field)} help={plaFieldHelp(t, kind, field, fieldLabel(field))} />
              {renderInput(field)}
              <small className="field-note">{t(field.kind === 'factor' ? 'pla.rules.factorRange' : 'pla.rules.range', { min: field.minimum ?? 0, max: field.maximum ?? 0 })}</small>
            </div>)}</div>
          </fieldset>)}
        </div>
      </EditorWorkspaceSection>
    </SectionedEditorWorkspace>
  </section>;
}
