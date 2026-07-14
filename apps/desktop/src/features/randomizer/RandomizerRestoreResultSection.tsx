/* SPDX-License-Identifier: GPL-3.0-only */

import { AlertTriangle, CheckCircle, Info } from 'lucide-react';
import { type ApplyResult } from '../../bridge/contracts';
import { Metric } from '../../components/workflowPanels';
import { useLocalization } from '../../localization/LocalizationProvider';

export function RandomizerRestoreResultSection({ applyResult }: { applyResult: ApplyResult }) {
  const { t, translateLiteral } = useLocalization();
  const needsAttention = applyResult.diagnostics.some(
    (diagnostic) => diagnostic.severity !== 'info'
  );
  const hasNoChanges = !needsAttention && applyResult.writtenFiles.length === 0;
  const heading = needsAttention
    ? 'Restore needs attention'
    : hasNoChanges
      ? 'No changes'
      : 'Restore Complete';
  const ResultIcon = needsAttention ? AlertTriangle : hasNoChanges ? Info : CheckCircle;

  return (
    <section
      aria-labelledby="randomizer-restore-result-heading"
      aria-live="polite"
      className="panel wide-panel randomizer-restore-result"
    >
      <div className="panel-heading">
        <ResultIcon aria-hidden="true" size={18} />
        <h2 id="randomizer-restore-result-heading">{translateLiteral(heading)}</h2>
      </div>
      <div className="change-plan-status">
        <Metric
          label="Status"
          value={needsAttention ? 'Warning' : hasNoChanges ? 'No changes' : 'Written'}
        />
        <Metric label="Output files" value={applyResult.writtenFiles.length.toString()} />
      </div>
      {hasNoChanges ? null : (
        <p className="randomizer-result-copy">{t('randomizer.recovery.resultHelp')}</p>
      )}
      {applyResult.writtenFiles.length > 0 ? (
        <ul className="written-file-list">
          {applyResult.writtenFiles.map((writtenFile) => (
            <li data-localization-ignore="true" key={writtenFile}>
              {writtenFile}
            </li>
          ))}
        </ul>
      ) : null}
    </section>
  );
}
