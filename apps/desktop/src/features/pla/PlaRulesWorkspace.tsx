/* SPDX-License-Identifier: GPL-3.0-only */
import { EditorFieldGroup } from '../../components/EditorRecordDetails';
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
  const bands = ['Low', 'Mid', 'High'];
  const friendshipTriggers = new Map<string, Record<string, PlaField>>();
  const charmTypes = ['warding', 'survival', 'tempting'];
  const charmSteps = new Map<number, Record<string, PlaField>>();
  for (const field of fields) {
    const friendship = /^friendship(.+?)(Low|Mid|High)$/u.exec(field.key);
    if (friendship) {
      const [, trigger, band] = friendship;
      friendshipTriggers.set(trigger, { ...friendshipTriggers.get(trigger), [band]: field });
    }
    const charm = /^(warding|survival|tempting)Price\.(\d+)$/u.exec(field.key);
    if (charm) {
      const [, type, step] = charm;
      charmSteps.set(Number(step), { ...charmSteps.get(Number(step)), [type]: field });
    }
  }
  const bagPrices = fields.filter(field => /^bagPrice\.\d+$/u.test(field.key));
  const groups = kind === 'capture' ? [
    { title: t('pla.rules.balls'), fields: fields.filter(field => field.key.startsWith('ball')) },
    { title: t('pla.rules.conditions'), fields: fields.filter(field => !field.key.startsWith('ball')) }
  ] : kind === 'friendship' ? [
    { title: t('pla.rules.experience'), fields: fields.filter(field => !/^friendship.+(?:Low|Mid|High)$/u.test(field.key)) }
  ] : kind === 'charmPrices' ? [
    { title: t('pla.rules.sharedCharms'), fields: fields.filter(field => !/^(warding|survival|tempting)Price\.\d+$/u.test(field.key)) }
  ] : kind === 'bagPrices' ? [
    { title: t('pla.kind.bagPrices'), fields: fields.filter(field => !bagPrices.includes(field)) }
  ] : [{ title: t('pla.kind.shiny'), fields }];
  const renderRule = (field: PlaField, inTable = false, tableLabel?: string) => {
    const label = fieldLabel(field);
    return <div className="path-field editable-field-control" key={field.key}>
      <div className={inTable ? 'pla-matrix-field-label' : undefined}>
        <FieldLabel htmlFor={'pla-field-' + field.key} label={tableLabel ?? label}
          help={plaFieldHelp(t, kind, field, label)} />
      </div>
      {renderInput(field)}
      <small className="field-note">{t(field.kind === 'factor' ? 'pla.rules.factorRange' : 'pla.rules.range',
        { min: field.minimum ?? 0, max: field.maximum ?? 0 })}</small>
    </div>;
  };
  return <section className="panel wide-panel pla-editor" aria-labelledby="pla-editor-title">
    <div className="panel-heading"><SlidersVertical size={18} aria-hidden="true" /><h2 id="pla-editor-title">{t('workbench.section.gameplayRules.label')}</h2><ContextHelp label={t('workbench.section.gameplayRules.label')}>{t('pla.notice.' + kind)}</ContextHelp>{refresh}</div>
    <SectionedEditorWorkspace sessionKey={sessionKey} initialSection="rules" compactFields sectionTransitionDuration={250}
      sessionBar={<>{sessionBar}{actions}</>} browserControls={controls}>
      <EditorWorkspaceSection id="rules" label={t('pla.kind.' + kind)}>
        {loading ? <p role="status">{translateLiteral('Loading')}</p> : null}
        {kind === 'friendship' && friendshipTriggers.size > 0 ? (
          <table className="editor-data-matrix pla-rule-matrix" aria-label={t('pla.kind.friendship')}>
            <thead><tr><th scope="col">{translateLiteral('Trigger')}</th>
              {bands.map(band => <th scope="col" key={band}>{t('pla.rules.band' + band)}</th>)}
            </tr></thead>
            <tbody>{[...friendshipTriggers].map(([trigger, row]) => <tr key={trigger}>
              <th scope="row">{fieldLabel(row.Low ?? Object.values(row)[0])}</th>
              {bands.map(band => <td key={band}>{row[band] ? renderRule(row[band], true, t('pla.rules.band' + band)) : null}</td>)}
            </tr>)}</tbody>
          </table>
        ) : null}
        {kind === 'charmPrices' && charmSteps.size > 0 ? (
          <table className="editor-data-matrix pla-rule-matrix" aria-label={t('pla.kind.charmPrices')}>
            <thead><tr><th scope="col">{translateLiteral('Purchase')}</th>
              {charmTypes.map(type => <th scope="col" key={type}>{t('pla.rules.' + type)}</th>)}
            </tr></thead>
            <tbody>{[...charmSteps].map(([step, row]) => <tr key={step}>
              <th scope="row">{step}</th>
              {charmTypes.map(type => <td key={type}>{row[type] ? renderRule(row[type], true) : null}</td>)}
            </tr>)}</tbody>
          </table>
        ) : null}
        {kind === 'bagPrices' && bagPrices.length > 0 ? (
          <div className="editor-slot-browser">
            <table className="editor-data-matrix pla-rule-matrix" aria-label={t('pla.kind.bagPrices')}>
              <thead><tr><th scope="col">{translateLiteral('Expansion')}</th><th scope="col">{translateLiteral('Price')}</th></tr></thead>
              <tbody>{bagPrices.map(field => <tr key={field.key}>
                <th scope="row">{field.key.split('.')[1]}</th><td>{renderRule(field, true)}</td>
              </tr>)}</tbody>
            </table>
          </div>
        ) : null}
        {groups.some(group => group.fields.length > 0) ? (
          <div className="editor-task-groups pla-rule-groups">
            {groups.filter(group => group.fields.length > 0).map(group => (
              <EditorFieldGroup className="editable-field-group" key={group.title} label={group.title}>
                <div className="pokemon-instance-field-list">{group.fields.map(field => renderRule(field))}</div>
              </EditorFieldGroup>
            ))}
          </div>
        ) : null}
      </EditorWorkspaceSection>
    </SectionedEditorWorkspace>
  </section>;
}
