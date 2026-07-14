/* SPDX-License-Identifier: GPL-3.0-only */

import { Activity, AlertCircle, AlertTriangle, CheckCircle, ClipboardCheck } from 'lucide-react';
import { type ApiDiagnostic, type ApplyResult, type ChangePlan } from '../bridge/contracts';
import { formatDiagnosticMessage } from '../diagnostics';
import { useLocalization } from '../localization';

export type WorkflowPanelOutput = {
  actionDiagnostics: ApiDiagnostic[];
  applyResult: ApplyResult | null;
  changePlan: ChangePlan | null;
};

export function Metric({ label, value }: { label: string; value: string }) {
  const { translateLiteral } = useLocalization();

  return (
    <div className="metric">
      <span className="metric-label">{translateLiteral(label)}</span>
      <span className="metric-value metric-value-small">{translateLiteral(value)}</span>
    </div>
  );
}

export function ApplyResultSection({ applyResult }: { applyResult: ApplyResult }) {
  const { translateLiteral } = useLocalization();
  const hasErrors = applyResult.diagnostics.some((diagnostic) => diagnostic.severity === 'error');
  const hasWarnings = applyResult.diagnostics.some(
    (diagnostic) => diagnostic.severity === 'warning'
  );
  const status = hasErrors
    ? 'Error'
    : hasWarnings
      ? 'Warning'
      : applyResult.writtenFiles.length > 0
        ? 'Written'
        : 'No changes';
  const ResultIcon = hasErrors ? AlertCircle : hasWarnings ? AlertTriangle : CheckCircle;

  return (
    <section
      aria-labelledby="apply-result-heading"
      className={`panel wide-panel apply-result-panel apply-result-${hasErrors ? 'error' : hasWarnings ? 'warning' : 'success'}`}
    >
      <div className="panel-heading">
        <ResultIcon aria-hidden="true" size={18} />
        <h2 id="apply-result-heading">{translateLiteral('Apply Result')}</h2>
      </div>

      <div className="change-plan-status">
        <Metric label="Status" value={status} />
        <Metric label="Written files" value={applyResult.writtenFiles.length.toString()} />
      </div>

      {applyResult.writtenFiles.length > 0 ? (
        <ul className="written-file-list">
          {applyResult.writtenFiles.map((writtenFile) => (
            <li data-localization-ignore="true" key={writtenFile}>
              {writtenFile}
            </li>
          ))}
        </ul>
      ) : (
        <p className="empty-copy">{translateLiteral('No files were written.')}</p>
      )}
    </section>
  );
}

export function WorkflowPanelOutputSections({
  output,
  scrollAfterEntries,
  workflowDiagnostics
}: {
  output: WorkflowPanelOutput;
  scrollAfterEntries?: number;
  workflowDiagnostics: ApiDiagnostic[];
}) {
  const combinedDiagnostics = [
    ...workflowDiagnostics,
    ...output.actionDiagnostics,
    ...(output.changePlan?.diagnostics ?? []),
    ...(output.applyResult?.diagnostics ?? [])
  ];

  return (
    <>
      {output.changePlan ? <ChangePlanSection changePlan={output.changePlan} /> : null}
      {output.applyResult ? <ApplyResultSection applyResult={output.applyResult} /> : null}
      <DiagnosticsSection
        diagnostics={combinedDiagnostics}
        scrollAfterEntries={scrollAfterEntries}
      />
    </>
  );
}

export function ChangePlanSection({ changePlan }: { changePlan: ChangePlan }) {
  const { translateLiteral } = useLocalization();

  return (
    <section aria-labelledby="change-plan-heading" className="panel wide-panel">
      <div className="panel-heading">
        <ClipboardCheck aria-hidden="true" size={18} />
        <h2 id="change-plan-heading">{translateLiteral('Output Plan')}</h2>
      </div>

      <div className="change-plan-status">
        <Metric label="Plan status" value={changePlan.canApply ? 'Ready' : 'Needs fixes'} />
        <Metric label="Target files" value={changePlan.writes.length.toString()} />
      </div>

      {changePlan.writes.length > 0 ? (
        <ul className="change-plan-list">
          {changePlan.writes.map((write) => (
            <li key={write.targetRelativePath}>
              <div>
                <strong data-localization-ignore="true">{write.targetRelativePath}</strong>
                <span>{translateLiteral(write.reason)}</span>
              </div>
              <dl>
                <div>
                  <dt>{translateLiteral('Output state')}</dt>
                  <dd>
                    {translateLiteral(
                      write.replacesExistingOutput ? 'Replaces output file' : 'Creates output file'
                    )}
                  </dd>
                </div>
                <div>
                  <dt>{translateLiteral('Sources')}</dt>
                  <dd>
                    {write.sources
                      .map(
                        (source) =>
                          `${translateLiteral(formatProjectFileLayer(source.layer))} ${source.relativePath}`
                      )
                      .join(', ')}
                  </dd>
                </div>
              </dl>
            </li>
          ))}
        </ul>
      ) : (
        <p className="empty-copy">{translateLiteral('No target files in this plan.')}</p>
      )}
    </section>
  );
}

export function DiagnosticsSection({
  diagnostics,
  scrollAfterEntries
}: {
  diagnostics: ApiDiagnostic[];
  scrollAfterEntries?: number;
}) {
  const isScrollable = scrollAfterEntries !== undefined && diagnostics.length > scrollAfterEntries;
  const { translateLiteral } = useLocalization();
  const groups = [
    {
      diagnostics: diagnostics.filter((diagnostic) => diagnostic.severity === 'error'),
      label: 'Error',
      severity: 'error' as const
    },
    {
      diagnostics: diagnostics.filter((diagnostic) => diagnostic.severity === 'warning'),
      label: 'Warnings',
      severity: 'warning' as const
    },
    {
      diagnostics: diagnostics.filter((diagnostic) => diagnostic.severity === 'info'),
      label: 'Information',
      severity: 'info' as const
    }
  ].filter((group) => group.diagnostics.length > 0);

  if (groups.length === 0) {
    return null;
  }

  return (
    <section aria-labelledby="diagnostics-heading" className="panel">
      <div className="panel-heading">
        <Activity aria-hidden="true" size={18} />
        <h2 id="diagnostics-heading">{translateLiteral('Diagnostics')}</h2>
      </div>

      <div className={`diagnostic-groups ${isScrollable ? 'diagnostic-list-scrollable' : ''}`}>
        {groups.map((group) => (
          <details
            className={`diagnostic-group diagnostic-group-${group.severity}`}
            key={group.severity}
            open={group.severity !== 'info' || groups.length === 1}
          >
            <summary>
              <span>{translateLiteral(group.label)}</span>
              <span className="diagnostic-count">{group.diagnostics.length}</span>
            </summary>
            <ul className="diagnostic-list">
              {group.diagnostics.map((diagnostic, index) => (
                <li
                  className={`diagnostic diagnostic-${diagnostic.severity}`}
                  key={`${diagnostic.severity}-${diagnostic.message}-${index}`}
                >
                  <strong>
                    {translateLiteral(formatDiagnosticSeverity(diagnostic.severity))}
                  </strong>
                  <span>{formatDiagnosticMessage(diagnostic, translateLiteral)}</span>
                </li>
              ))}
            </ul>
          </details>
        ))}
      </div>
    </section>
  );
}

function formatProjectFileLayer(layer: ChangePlan['writes'][number]['sources'][number]['layer']) {
  return {
    base: 'Base',
    generated: 'Generated',
    layered: 'LayeredFS',
    pending: 'Pending'
  }[layer];
}

function formatDiagnosticSeverity(severity: ApiDiagnostic['severity']) {
  switch (severity) {
    case 'error':
      return 'Error';
    case 'warning':
      return 'Warning';
    case 'info':
      return 'Info';
  }
}
