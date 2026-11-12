/* SPDX-License-Identifier: GPL-3.0-only */

import { useId, useState } from 'react';
import { createBugReport, saveBugReport } from '../supportReport';
import { useLocalization } from '../localization';
import { usePublishCommonEditorError } from './CommonEditorDiagnostics';

export function BugReportPanel() {
  const { t } = useLocalization();
  const heading = useId();
  const [report, setReport] = useState('');
  const [busy, setBusy] = useState(false);
  const [copied, setCopied] = useState(false);
  const [failure, setFailure] = useState<'generate' | 'copy' | 'save' | null>(null);
  const message = failure ? t(`bugReport.${failure}Failed`) : null;
  usePublishCommonEditorError({ domain: 'supportReport', field: 'export', message });

  const generate = async () => {
    setBusy(true);
    setFailure(null);
    setCopied(false);
    try { setReport(await createBugReport()); }
    catch { setFailure('generate'); }
    finally { setBusy(false); }
  };
  const copy = async () => {
    setFailure(null);
    try { await navigator.clipboard.writeText(report); setCopied(true); }
    catch { setCopied(false); setFailure('copy'); }
  };
  const save = async () => {
    setFailure(null);
    try { await saveBugReport(report); }
    catch { setFailure('save'); }
  };

  return (
    <section aria-labelledby={heading} className="km-settings-group bug-report-panel">
      <h3 id={heading}>{t('bugReport.title')}</h3>
      <div className="km-settings-group-body">
        <p>{t('bugReport.description')}</p>
        <button className="secondary-button compact-button" disabled={busy}
          onClick={() => void generate()} type="button">
          {t(busy ? 'bugReport.generating' : 'bugReport.generate')}
        </button>
        {report ? (
          <>
            <p>{t('bugReport.share')}</p>
            <label>
              <span>{t('bugReport.preview')}</span>
              <textarea className="bug-report-preview" data-localization-ignore="true"
                readOnly rows={10} value={report} />
            </label>
            <div className="fatal-error-actions">
              <button className="secondary-button compact-button" onClick={() => void copy()} type="button">
                {t(copied ? 'bugReport.copied' : 'bugReport.copy')}
              </button>
              <button className="secondary-button compact-button" onClick={() => void save()} type="button">
                {t('bugReport.save')}
              </button>
            </div>
          </>
        ) : null}
        {message ? <p role="status">{message}</p> : null}
      </div>
    </section>
  );
}
