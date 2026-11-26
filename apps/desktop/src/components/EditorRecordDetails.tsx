/* SPDX-License-Identifier: GPL-3.0-only */
import { useId, type ComponentPropsWithoutRef, type ReactNode } from 'react';
import './editorRecordDetails.css';

type EditorGroupHeading = { label: ReactNode; headingValue?: ReactNode };

function EditorGroupHeader({ id, label, headingValue }: EditorGroupHeading & { id: string }) {
  return <header><h4 id={id}>{label}</h4>{headingValue}</header>;
}

export function EditorDetailGroup({ label, headingValue, className, children, ...props }:
  EditorGroupHeading & ComponentPropsWithoutRef<'section'>) {
  const id = useId();
  return <section {...props} className={['editor-detail-group', className].filter(Boolean).join(' ')} aria-labelledby={id}>
    <EditorGroupHeader id={id} label={label} headingValue={headingValue} />
    {children}
  </section>;
}

export function EditorFieldGroup({ label, headingValue, className, children, ...props }:
  EditorGroupHeading & ComponentPropsWithoutRef<'fieldset'>) {
  const id = useId();
  return <fieldset {...props} className={['editor-detail-group', className].filter(Boolean).join(' ')} aria-labelledby={id}>
    <EditorGroupHeader id={id} label={label} headingValue={headingValue} />
    {children}
  </fieldset>;
}

export function EditorDetailValue({ label, children, dataValue, path }: {
  label: string; children: ReactNode; dataValue?: boolean; path?: boolean;
}) {
  return <div className={path ? 'editor-detail-path' : undefined}>
    <dt>{label}</dt>
    <dd data-localization-ignore={dataValue ? 'true' : undefined}>{children}</dd>
  </div>;
}

export function PokemonDetailsStats({ stats }: { stats: { label: string; value: ReactNode; number: number | null }[] }) {
  const total = stats.every(stat => stat.number !== null)
    ? stats.reduce((sum, stat) => sum + (stat.number ?? 0), 0) : null;
  return <EditorDetailGroup label="Base Stats" headingValue={<span className="editor-detail-total">Total <strong>{total ?? '—'}</strong></span>}>
    <dl className="editor-detail-list pokemon-overview-stats">
      {stats.map(stat => <div key={stat.label}>
        <dt>{stat.label}</dt>
        <dd>
          <span className="pokemon-overview-track" aria-hidden="true"><span style={{
            inlineSize: `${Math.max(0, Math.min(100, (stat.number ?? 0) / 2.55))}%`
          }} /></span>
          <span>{stat.value}</span>
        </dd>
      </div>)}
    </dl>
  </EditorDetailGroup>;
}
