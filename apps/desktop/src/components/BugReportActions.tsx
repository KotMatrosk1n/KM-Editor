/* SPDX-License-Identifier: GPL-3.0-only */

import { useRef, useState } from 'react';
import { createBugReport, observeDiagnosticCode, openBugReport, saveBugReport } from '../supportReport';
import { useLocalization } from '../localization';
import { usePublishCommonEditorError } from './CommonEditorDiagnostics';

export function BugReportActions({ codes = [] }: { codes?: readonly (string | null | undefined)[] }) {
  const { t } = useLocalization();
  const pending = useRef(false);
  const [busy, setBusy] = useState(false);
  const [fileName, setFileName] = useState<string | null>(null);
  const [failure, setFailure] = useState<'generate' | 'save' | 'open' | null>(null);
  const message = failure ? t(`bugReport.${failure}Failed`) : null;
  usePublishCommonEditorError({ domain: 'supportReport', field: 'export', message });

  const generate = async () => {
    if (pending.current) return;
    pending.current = true;
    setBusy(true);
    setFailure(null);
    let operation: 'generate' | 'save' = 'generate';
    try {
      codes.forEach(observeDiagnosticCode);
      const report = await createBugReport();
      operation = 'save';
      setFileName(await saveBugReport(report));
    } catch { setFailure(operation); }
    finally { pending.current = false; setBusy(false); }
  };
  const open = async () => {
    if (!fileName) return;
    setFailure(null);
    try { await openBugReport(fileName); }
    catch { setFailure('open'); }
  };

  return (
    <div className="bug-report-actions">
      <button aria-busy={busy} className="secondary-button compact-button" disabled={busy}
        onClick={() => void generate()} type="button">
        {t(busy ? 'bugReport.generating' : 'bugReport.generate')}
      </button>
      {fileName ? (
        <button className="secondary-button compact-button" onClick={() => void open()} type="button">
          {t('bugReport.goToReport')}
        </button>
      ) : null}
      {message ? <span role="status">{message}</span> : null}
    </div>
  );
}
