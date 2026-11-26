/* SPDX-License-Identifier: GPL-3.0-only */
import { useEffect, useRef, useState, type HTMLAttributes, type ReactNode } from 'react';
import { version } from '../../../package.json';
import { rowClipboardEditorSchemas } from '../../authoring/rowClipboardAdapters';
import { createScopedRowClipboardController } from '../../authoring/rowClipboardScopedController';
import { readRowClipboardEnvelopeFromEditorClipboard, writeRowClipboardEnvelopeToEditorClipboard } from '../../authoring/rowClipboardSystemClipboard';
import { handleRowClipboardShortcut } from '../../authoring/rowClipboardShortcuts';
import type { RowClipboardOwnedValue } from '../../authoring/rowClipboardTypes';
import type { PlaField } from '../../bridge/plaProjectBridge';
import { TrainerPartySlotContextMenu } from '../../components/TrainerPartySlotContextMenu';
import { ContextHelp } from '../../components/ContextHelp';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { useLocalization } from '../../localization';
import type { PlaEditorProps } from './PlaEditorSection';
import { isPlaFieldValueValid, resolvePlaField } from './plaDraftValidation';

type Row = { label: string; fields: PlaField[]; values: Record<string, string> };
type ClipboardKind = 'learnsets' | 'trainers' | 'encounters';
export type PlaRowClipboardBinding = { bind: (id: string) => HTMLAttributes<HTMLElement>; content: ReactNode };

export function usePlaRowClipboard(options: {
  source: Pick<PlaEditorProps, 'paths' | 'session' | 'bridge'>; kind: ClipboardKind; identity: string;
  enabled: boolean; busy: boolean; getRow: (id: string) => Row | null;
  onPaste: (id: string, values: Record<string, string>) => boolean;
}): PlaRowClipboardBinding {
  const { t } = useLocalization();
  const [menu, setMenu] = useState<{ id: string; left: number; top: number; trigger: HTMLElement } | null>(null);
  const [feedback, setFeedback] = useState<{ message: string; error: boolean } | null>(null);
  const [running, setRunning] = useState(false);
  const operation = useRef(false), mounted = useRef(true), latest = useRef(options);
  latest.current = options;
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
  const scope = JSON.stringify([options.source.paths, options.source.session, options.identity]);
  const scopeRef = useRef(scope); scopeRef.current = scope;
  useEffect(() => { setMenu(null); setFeedback(null); }, [scope]);
  usePublishCommonEditorError({ domain: `workflow.${options.kind === 'learnsets' ? 'pokemon' : options.kind}`, field: 'clipboard', message: feedback?.error ? feedback.message : null });
  const editor = rowClipboardEditorSchemas[options.kind === 'learnsets' ? 'plaLearnset' : options.kind === 'trainers' ? 'plaTrainerParty' : 'plaEncounterSlot'];
  const reason = (id: string, copy: boolean) => {
    const row = options.getRow(id);
    if (!options.enabled) return t('rowClipboard.menu.startEditing');
    if (options.busy || running) return t('rowClipboard.menu.busy');
    if (!row || !row.fields.length) return t('rowClipboard.menu.unavailable');
    if (copy && row.fields.some(field => !isPlaFieldValueValid(resolvePlaField(field, row.values), row.values[field.key]))) return t('rowClipboard.menu.invalidDraft');
    return undefined;
  };
  const run = async (id: string, copy: boolean) => {
    if (operation.current || reason(id, copy)) return;
    const row = options.getRow(id);
    if (!row) return;
    const snapshot = JSON.stringify(row.values);
    operation.current = true; setRunning(true); setFeedback(null);
    const current = () => mounted.current && scopeRef.current === scope;
    const fail = (key: string) => { if (current()) setFeedback({ message: t(key), error: true }); };
    try {
      const prepared = await options.source.bridge.prepareRowClipboardCopy({ paths: options.source.paths, session: options.source.session });
      if (!current()) return;
      if (!prepared.scope || prepared.diagnostics.some(diagnostic => diagnostic.severity === 'error')) { fail('rowClipboard.feedback.backendUnavailable'); return; }
      const controller = createScopedRowClipboardController(editor, prepared.scope);
      if (copy) {
        const values: RowClipboardOwnedValue[] = row.fields.map(field => ({ fieldKey: field.key, value: {
          kind: options.kind === 'learnsets' ? 'unsignedInteger' : field.kind === 'decimal' ? 'decimal' : 'signedInteger', value: String(Number(row.values[field.key]))
        } }));
        const identity = { kind: editor.rowKind, key: options.identity + ':' + id };
        const envelope = await controller.copy({ dependencies: [], editor, producerVersion: version,
          rows: [{ sourceIdentity: identity, values }], source: { logicalIdentity: identity, projectRevision: prepared.sourceRevision } });
        if (!current()) return;
        const result = await writeRowClipboardEnvelopeToEditorClipboard(envelope);
        if (result.kind === 'failure') { fail(result.feedbackKey); return; }
        if (current()) setFeedback({ message: t('rowClipboard.feedback.copySuccess', { target: row.label }), error: false });
      } else {
        const result = await readRowClipboardEnvelopeFromEditorClipboard(controller);
        if (!current()) return;
        if (result.kind === 'failure') { fail(result.feedbackKey); return; }
        if (result.value.source.projectRevision !== prepared.sourceRevision) { fail('rowClipboard.feedback.sourceStale'); return; }
        if (result.value.rows.length !== 1) { fail('rowClipboard.feedback.pasteRejected'); return; }
        const values = Object.fromEntries(result.value.rows[0].values.map(owned => [owned.fieldKey, String(owned.value.value)]));
        if (JSON.stringify(latest.current.getRow(id)?.values) !== snapshot) { fail('rowClipboard.feedback.targetChanged'); return; }
        if (Object.entries(values).some(([key, value]) => {
          const field = row.fields.find(field => field.key === key); return !field || !isPlaFieldValueValid(resolvePlaField(field, { ...row.values, ...values }), value);
        }) || !latest.current.onPaste(id, values)) { fail('rowClipboard.feedback.pasteRejected'); return; }
        setFeedback({ message: t('rowClipboard.feedback.pasteSuccess', { count: 1, target: row.label }), error: false });
      }
    } catch { fail(copy ? 'rowClipboard.feedback.copyInvalid' : 'rowClipboard.feedback.pasteRejected'); }
    finally { operation.current = false; if (mounted.current) setRunning(false); }
  };
  const bind = (id: string): HTMLAttributes<HTMLElement> => ({
    onContextMenu: event => {
      if (event.target instanceof HTMLElement && event.target.closest('input, textarea, select, [role="combobox"]')) return;
      event.preventDefault(); event.stopPropagation(); setMenu({ id, left: event.clientX, top: event.clientY, trigger: event.currentTarget });
    },
    onKeyDown: event => {
      if (handleRowClipboardShortcut(event, () => void run(id, true), () => void run(id, false))) return;
      if (event.key === 'ContextMenu' || event.shiftKey && event.key === 'F10') {
        event.preventDefault(); event.stopPropagation(); const bounds = event.currentTarget.getBoundingClientRect();
        setMenu({ id, left: bounds.left + 24, top: bounds.top + 24, trigger: event.currentTarget });
      }
    }
  });
  return { bind, content: <>
    {options.kind === 'learnsets'
      ? <p className="field-note">{t('rowClipboard.discoveryHint')}</p>
      : <ContextHelp label={t(options.kind === 'trainers' ? 'editorSections.party' : 'pla.kind.encounters')}>{t('rowClipboard.discoveryHint')}</ContextHelp>}
    {feedback ? <p role="status" className={feedback.error ? 'field-error' : 'field-note'}>{feedback.message}</p> : null}
    {menu ? <TrainerPartySlotContextMenu id={`pla-${options.kind}-clipboard-menu`} left={menu.left} top={menu.top} triggerElement={menu.trigger}
      targetLabel={options.getRow(menu.id)?.label ?? menu.id} menuLabel={t('rowClipboard.menu.label', { target: options.getRow(menu.id)?.label ?? menu.id })}
      copyActionLabel={t('rowClipboard.menu.copyAction')} pasteActionLabel={t('rowClipboard.menu.pasteAction')} noSourceSummaryLabel={t('rowClipboard.menu.noSource')}
      copyDisabledReason={reason(menu.id, true)} pasteDisabledReason={reason(menu.id, false)} onClose={() => setMenu(null)} onCopy={() => void run(menu.id, true)} onPaste={() => void run(menu.id, false)} /> : null}
  </> };
}
