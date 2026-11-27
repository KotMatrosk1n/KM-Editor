/* SPDX-License-Identifier: GPL-3.0-only */
import { type ReactNode } from 'react';
import { ContextHelp } from '../../components/ContextHelp';
import { EditorDetailGroup, EditorDetailValue, EditorFieldGroup } from '../../components/EditorRecordDetails';
import {
  EditorRecordHeading, EditorWorkspaceHeader, EditorWorkspaceSection, SectionedEditorWorkspace
} from '../../components/SectionedEditorWorkspace';
import { useLocalization } from '../../localization';
import './moveEditorWorkspace.css';

export type MoveFieldControl = {
  id: string;
  input: ReactNode;
  changed?: boolean;
  invalid?: boolean;
};

export type MoveFieldGroup = {
  label: string;
  fields: MoveFieldControl[];
  help?: ReactNode;
  notice?: ReactNode;
  layout?: 'flags' | 'stats';
};

export function MoveFlagCount({ count, name, onOpen }: {
  count: number; name: string; onOpen?: () => void;
}) {
  const { translateLiteral } = useLocalization();
  return <button type="button" className="move-flag-count"
    aria-label={`${name} · ${translateLiteral('Active flags')} ${count}`} disabled={!onOpen}
    onKeyDown={event => event.stopPropagation()}
    onClick={event => { event.stopPropagation(); onOpen?.(); }}>{count}</button>;
}

export function MoveDetails({ id, description, version, sourceFile, sourceLayer, fileState, extra, runtimeFiles, controls, actions }: {
  id: string | number;
  description?: string | null;
  version?: string | number | null;
  sourceFile?: string;
  sourceLayer?: string;
  fileState?: string;
  extra?: ReactNode;
  runtimeFiles?: string[];
  controls?: MoveFieldControl[];
  actions?: ReactNode;
}) {
  const { translateLiteral } = useLocalization();
  return <div className="move-groups">
    <EditorDetailGroup label={translateLiteral('Move')}>
      <dl className="editor-detail-list">
        <EditorDetailValue label="ID">{id}</EditorDetailValue>
        {version !== undefined && version !== null
          ? <EditorDetailValue label={translateLiteral('Version')}>{version}</EditorDetailValue> : null}
        {description ? <EditorDetailValue label={translateLiteral('Description')} dataValue>
          <span className="move-description">{description.replaceAll('\\n', '\n')}</span>
        </EditorDetailValue> : null}
        {extra}
      </dl>
      {controls?.map(field => <MoveControl key={field.id} field={field} />)}
      {actions}
    </EditorDetailGroup>
    {sourceFile || sourceLayer || fileState || runtimeFiles?.length ? <EditorDetailGroup label={translateLiteral('Source')}>
      <dl className="editor-detail-list">
        {sourceFile ? <EditorDetailValue label={translateLiteral('Source file')} dataValue path>{sourceFile}</EditorDetailValue> : null}
        {sourceLayer ? <EditorDetailValue label={translateLiteral('Source layer')}>{sourceLayer}</EditorDetailValue> : null}
        {fileState ? <EditorDetailValue label={translateLiteral('File state')}>{fileState}</EditorDetailValue> : null}
        {runtimeFiles?.map(file => <EditorDetailValue key={file} label={translateLiteral('Runtime data source')} dataValue path>{file}</EditorDetailValue>)}
      </dl>
    </EditorDetailGroup> : null}
  </div>;
}

function MoveControl({ field }: { field: MoveFieldControl }) {
  return <div className="move-control" data-move-field={field.id}
    data-changed={field.changed || undefined} data-invalid={field.invalid || undefined}>{field.input}</div>;
}

const hitBounds = ['minHitCount', 'maxHitCount', 'hitMin', 'hitMax', 'minHits', 'maxHits'];

function MoveStatRows({ fields }: { fields: MoveFieldControl[] }) {
  const { translateLiteral } = useLocalization();
  const rows = new Map<number, Partial<Record<'stat' | 'stage' | 'chance', MoveFieldControl>>>();
  const remaining: MoveFieldControl[] = [];
  for (const field of fields) {
    const match = /(?:^|\.)stat(\d+)(Stage|Percent)?$/i.exec(field.id);
    if (!match) {
      remaining.push(field);
      continue;
    }
    const slot = Number(match[1]);
    const row = rows.get(slot) ?? {};
    const key = match[2]?.toLowerCase();
    row[key === 'stage' ? 'stage' : key === 'percent' ? 'chance' : 'stat'] = field;
    rows.set(slot, row);
  }
  return <>
    {rows.size ? <div className="move-stat-grid" role="table" aria-label={translateLiteral('Stat changes')}>
      <div role="row" className="move-stat-heading">
        <span role="columnheader">{translateLiteral('Stat')}</span>
        <span role="columnheader">{translateLiteral('Stage change')}</span>
        <span role="columnheader">{translateLiteral('Chance')}</span>
      </div>
      {[...rows].sort(([left], [right]) => left - right).map(([slot, row]) => <div role="row" key={slot}>
        {(['stat', 'stage', 'chance'] as const).map(key => <div role="cell" key={key}>
          {row[key] ? <MoveControl field={row[key]} /> : null}
        </div>)}
      </div>)}
    </div> : null}
    {remaining.map(field => <MoveControl key={field.id} field={field} />)}
  </>;
}

function MoveGroups({ groups, flags = false }: { groups: MoveFieldGroup[]; flags?: boolean }) {
  if (groups.length === 0) return null;
  return <div className={flags ? 'move-groups move-flag-groups' : 'move-groups'}>{groups.map(group => {
    const bounds = group.fields.filter(field => hitBounds.includes(field.id.split('.').at(-1)!));
    return <EditorFieldGroup
      key={group.label}
      className="move-group"
      data-editor-disclosure-group
      label={group.label}
      headingValue={group.help ? <ContextHelp label={group.label}>{group.help}</ContextHelp> : null}
    >
      {group.notice}
      {group.layout === 'stats' ? <MoveStatRows fields={group.fields} /> : group.fields.map(field => {
        if (bounds.includes(field) && bounds.length === 2) {
          return field === bounds[0] ? <div key={field.id} className="move-hit-bounds">
            {bounds.map(bound => <MoveControl key={bound.id} field={bound} />)}
          </div> : null;
        }
        return <MoveControl key={field.id} field={field} />;
      })}
    </EditorFieldGroup>;
  })}</div>;
}

export function MoveEditorWorkspace({ sessionKey, record, browser, browserControls, browserFooter,
  sessionBar, header, details, detailsFields = [], core, effects, advanced = [], flags,
  flagRequest, flagNotice, recordContext, catalogStatus }: {
  sessionKey: string;
  record: { name: string; id: number | string } | null;
  browser: ReactNode;
  browserControls: ReactNode;
  browserFooter?: ReactNode;
  sessionBar: ReactNode;
  header?: ReactNode;
  details: ReactNode;
  detailsFields?: MoveFieldControl[];
  core: MoveFieldGroup[];
  effects: MoveFieldGroup[];
  advanced?: MoveFieldGroup[];
  flags: MoveFieldGroup[];
  flagRequest?: { sectionId: string };
  flagNotice?: ReactNode;
  recordContext?: ReactNode;
  catalogStatus?: ReactNode;
}) {
  const { translateLiteral } = useLocalization();
  const groupFields = (groups: MoveFieldGroup[]) => groups.flatMap(group => group.fields);
  const status = (fields: MoveFieldControl[]) => ({
    modified: fields.some(field => field.changed || field.invalid),
    invalid: fields.some(field => field.invalid)
  });
  const advancedStatus = status(groupFields(advanced));

  return <div className="moves-editor">
    <SectionedEditorWorkspace sessionKey={sessionKey} initialSection="core" sectionTransitionDuration={250}
      browserVisibleRows={4} browser={browser} browserControls={browserControls} browserFooter={browserFooter}
      sessionBar={sessionBar} compactFields activeSectionRequest={flagRequest}>
      <EditorWorkspaceHeader>{header}</EditorWorkspaceHeader>
      {record ? <>
        <EditorWorkspaceHeader afterBrowser>
          {recordContext}
          <div className="move-record-heading">
            <EditorRecordHeading name={record.name} id={record.id} />
            {catalogStatus ? <span className="move-catalog-status">{catalogStatus}</span> : null}
          </div>
        </EditorWorkspaceHeader>
        <EditorWorkspaceSection id="details" label={translateLiteral('Details')} {...status(detailsFields)}>
          {details}
        </EditorWorkspaceSection>
        {core.length ? <EditorWorkspaceSection id="core" label={translateLiteral('Core Stats')} {...status(groupFields(core))}>
          <MoveGroups groups={core} />
        </EditorWorkspaceSection> : null}
        {effects.length || advanced.length ? <EditorWorkspaceSection id="effects" label={translateLiteral('Effects')}
          {...status(groupFields([...effects, ...advanced]))}>
          <MoveGroups groups={effects} />
          {advanced.length ? <details className="move-advanced" key={advancedStatus.invalid ? 'invalid' : 'valid'} open={advancedStatus.invalid || undefined}>
            <summary>{translateLiteral('Advanced / Raw')}</summary>
            <MoveGroups groups={advanced} />
          </details> : null}
        </EditorWorkspaceSection> : null}
        {flags.length || flagNotice ? <EditorWorkspaceSection id="flags" label={translateLiteral('Flags')} {...status(groupFields(flags))}>
          {flagNotice}
          <MoveGroups groups={flags} flags />
        </EditorWorkspaceSection> : null}
      </> : null}
    </SectionedEditorWorkspace>
  </div>;
}
