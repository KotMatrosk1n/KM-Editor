// SPDX-License-Identifier: GPL-3.0-only
import { useId, useState } from 'react';
import { createPortal } from 'react-dom';
import { useModalDialog } from '../../components/useModalDialog';
import { usePublishCommonEditorDiagnostics } from '../../components/CommonEditorDiagnostics';
import { useLocalization } from '../../localization';
import type { MergePackage, MergePackageCatalog } from './mergeWorkspaceBridge';
import { changePackageSelection, defaultPackageSelection, packageSelectionValid, requiredPackages } from './mergePackageSelection';

export function MergePackageDialog({ catalog, initial, changed, onCancel, onConfirm }: {
  catalog: MergePackageCatalog; initial?: string[] | null; changed: boolean;
  onCancel: () => void; onConfirm: (ids: string[]) => void;
}) {
  const { t, translateLiteral } = useLocalization();
  const label = (key: string) => t(`mergeWorkspace.packages.${key}`);
  const titleId = useId();
  const ref = useModalDialog({ onClose: onCancel });
  const [selected, setSelected] = useState(() => initial ?? defaultPackageSelection(catalog));
  const [query, setQuery] = useState('');
  const [selectionError, setSelectionError] = useState(false);
  const [adjustments, setAdjustments] = useState<{ added: string[]; removed: string[] }>({ added: [], removed: [] });
  const requiredIds = requiredPackages(catalog);
  usePublishCommonEditorDiagnostics(selectionError ? [{ code: 'KM-MERGE-PACKAGE-SELECTION-INVALID', severity: 'error',
    message: label('requiredConflict'), domain: 'workflow.modMerger' }] : []);
  const valid = packageSelectionValid(catalog, selected);
  function change(id: string, include: boolean) {
    const next = changePackageSelection(catalog, selected, id, include);
    setSelectionError(next === null);
    if (next) {
      setAdjustments({ added: next.filter(value => value !== id && !selected.includes(value)), removed: selected.filter(value => value !== id && !next.includes(value)) });
      setSelected(next);
    }
  }
  const visible = (item: MergePackage) => `${item.name} ${item.root} ${item.description ?? ''}`.toLocaleLowerCase().includes(query.toLocaleLowerCase());
  function row(item: MergePackage) {
    return <article className="merge-package-row" key={item.id} hidden={!visible(item)}>
      <label className="merge-package-choice"><input type={item.group ? 'radio' : 'checkbox'}
        name={item.group ? `${titleId}-${item.group}` : undefined} checked={selected.includes(item.id)}
        disabled={requiredIds.has(item.id)} onChange={event => change(item.id, event.target.checked)} />
        <span><strong data-localization-ignore>{item.name}</strong><small>{item.fileCount} {label('files')}
          {requiredIds.has(item.id) ? ` · ${label('required')}` : item.recommended ? ` · ${label('recommended')}` : ''}
          {item.game && <> · {translateLiteral(item.game === 'za' ? 'Pokemon Legends Z-A' : `Pokemon ${item.game[0]!.toUpperCase()}${item.game.slice(1)}`)}</>}
        </small></span></label>
      {item.description && <p data-localization-ignore>{item.description}</p>}
      {item.evidence.includes('unclassified') && <p className="field-note">{label('unclassified')}</p>}
      {item.requires.length > 0 && <p>{label('requires')}: <span data-localization-ignore>{item.requires.map(id => catalog.packages.find(other => other.id === id)?.name).join(', ')}</span></p>}
      {item.overlapCount > 0 && <p className="field-note">{label('overlap')}: {item.overlapCount}</p>}
      <details><summary>{label('details')}</summary>
        <p><span>{label('location')}: </span><code data-localization-ignore>{item.root || '.'}</code></p>
        <p>{label('detectedBy')}: {item.evidence.filter(value => ['manifest', 'installationRoot', 'virtualRoot', 'patchFiles', 'nestedArchive', 'unclassified'].includes(value)).map(value => label(`evidence.${value}`)).join(', ')}</p>
        <p>{t(`mergeWorkspace.${item.layout}`)} · {(item.size / 1024 / 1024).toLocaleString(undefined, { maximumFractionDigits: 2 })} MiB</p>
        <ul className="merge-package-files" data-localization-ignore>{item.files.map(file => <li key={file}><code>{file}</code></li>)}</ul>
        {item.files.length < item.fileCount && <p className="field-note">{label('filePreview')}</p>}
      </details>
    </article>;
  }
  return createPortal(<div className="merge-package-backdrop"><section ref={ref} className="merge-package-dialog" role="dialog" aria-modal="true" aria-labelledby={titleId} tabIndex={-1}>
    <header><div><h2 id={titleId}>{label('title')}</h2><p data-localization-ignore>{catalog.name}</p></div>
      <button type="button" onClick={onCancel}>{label('cancel')}</button></header>
    <div className="merge-package-body">
      <p>{label('help')}</p>
      {catalog.packages.some(item => item.overlapCount > 0) && <p className="field-note">{label('overlapHelp')}</p>}
      {changed && <p role="status" className="field-note">{label('changed')}</p>}
      <div className="merge-package-toolbar"><input aria-label={label('search')} placeholder={label('search')} value={query} onChange={event => setQuery(event.target.value)} />
        {catalog.packages.some(item => item.recommended) && <button type="button" onClick={() => { setSelected(defaultPackageSelection(catalog)); setSelectionError(false); setAdjustments({ added: [], removed: [] }); }}>{label('useRecommended')}</button>}</div>
      {catalog.packages.filter(item => !item.group).map(row)}
      {catalog.groups.map(group => <fieldset key={group.id} hidden={!catalog.packages.some(item => item.group === group.id && visible(item))}>
        <legend><span data-localization-ignore>{group.name}</span> · {group.required ? label('chooseOneRequired') : label('chooseOne')}</legend>
        {!group.required && <label className="merge-package-none"><input type="radio" name={`${titleId}-${group.id}`}
          checked={!catalog.packages.some(item => item.group === group.id && selected.includes(item.id))}
          disabled={catalog.packages.some(item => item.group === group.id && requiredIds.has(item.id))}
          onChange={() => {
            const current = catalog.packages.find(item => item.group === group.id && selected.includes(item.id));
            if (current) change(current.id, false);
          }} />{label('none')}</label>}
        {catalog.packages.filter(item => item.group === group.id).map(row)}
      </fieldset>)}
      {!catalog.packages.some(visible) && <p>{label('noMatches')}</p>}
      {catalog.documents.length > 0 && <details className="merge-package-documents"><summary>{label('documentation')}</summary>
        <p className="field-note">{label('documentationHelp')}</p>
        {catalog.documents.map(document => <details key={document.path}><summary data-localization-ignore>{document.path}</summary>
          {document.text ? <pre data-localization-ignore>{document.text}</pre> : <p>{label('externalDocument')}</p>}
          {document.truncated && document.text && <p className="field-note">{label('truncated')}</p>}
        </details>)}
      </details>}
    </div>
    <footer><div><p role="status">{selected.length} / {catalog.packages.length} {label('selected')}</p>
      {(['added', 'removed'] as const).map(kind => adjustments[kind].length > 0 && <p role="status" key={kind}>{label(kind)}: <span data-localization-ignore>{adjustments[kind].map(id => catalog.packages.find(item => item.id === id)?.name).join(', ')}</span></p>)}
      {selectionError && <p role="alert" className="field-error">{label('requiredConflict')}</p>}
      {!valid && <p className="field-note">{label('selectionHelp')}</p>}</div>
      <button type="button" className="primary-button" disabled={!valid} onClick={() => onConfirm(selected)}>{label('confirm')}</button></footer>
  </section></div>, document.body);
}
