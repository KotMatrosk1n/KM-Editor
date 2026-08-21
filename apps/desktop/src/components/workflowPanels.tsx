/* SPDX-License-Identifier: GPL-3.0-only */

import { Activity, AlertCircle, AlertTriangle, CheckCircle, ClipboardCheck } from 'lucide-react';
import { type ReactNode } from 'react';
import { type ApiDiagnostic, type ApplyResult, type ChangePlan } from '../bridge/contracts';
import { formatDiagnosticSummary } from '../diagnostics';
import { useDiagnosticNavigation } from '../diagnosticActions';
import { useLocalization } from '../localization';
import {
  DiagnosticTechnicalDetails,
  OccurrenceCount,
} from '../features/workbench/AnalysisPresentation';
import {
  diagnosticSeverityPriority,
  groupDiagnosticsForPresentation,
  presentationDiagnosticMessage,
  presentationDiagnosticSeverity
} from '../features/workbench/analysisPresentationUtils';
import { ContextHelp } from './ContextHelp';

export type WorkflowPanelOutput = {
  actionDiagnostics: ApiDiagnostic[];
  applyResult: ApplyResult | null;
  changePlan: ChangePlan | null;
};

export function Metric({
  help,
  label,
  value,
  valueIsRaw = false
}: {
  help?: ReactNode;
  label: string;
  value: string;
  valueIsRaw?: boolean;
}) {
  const { translateLiteral } = useLocalization();

  return (
    <div className="metric">
      <span className="metric-label">
        <span>{translateLiteral(label)}</span>
        {help ? <ContextHelp label={translateLiteral(label)}>{help}</ContextHelp> : null}
      </span>
      <span
        className="metric-value metric-value-small"
        data-localization-ignore={valueIsRaw ? 'true' : undefined}
      >
        {valueIsRaw ? value : translateLiteral(value)}
      </span>
    </div>
  );
}

export function ApplyResultSection({ applyResult }: { applyResult: ApplyResult }) {
  const { formatLocale, t, translateLiteral } = useLocalization();
  const outputTransaction = applyResult.outputTransaction;
  const hasErrors =
    applyResult.diagnostics.some((diagnostic) => diagnostic.severity === 'error') ||
    outputTransaction?.outcome === 'recoveryRequired';
  const hasWarnings = applyResult.diagnostics.some(
    (diagnostic) => diagnostic.severity === 'warning'
  ) || outputTransaction?.outcome === 'rolledBack';
  const status = hasErrors
    ? outputTransaction?.outcome === 'recoveryRequired'
      ? t('workflowPanels.outputTransaction.outcome.recoveryRequired')
      : 'Error'
    : hasWarnings
      ? outputTransaction?.outcome === 'rolledBack'
        ? t('workflowPanels.outputTransaction.outcome.rolledBack')
        : 'Warning'
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
        <Metric
          help={t('workflowPanels.metric.applyStatusHelp')}
          label="Status"
          value={status}
        />
        <Metric
          help={t('workflowPanels.metric.writtenFilesHelp')}
          label="Written files"
          value={applyResult.writtenFiles.length.toString()}
        />
        {outputTransaction ? (
          <Metric
            label={t('workflowPanels.outputTransaction.label')}
            value={t(`workflowPanels.outputTransaction.outcome.${outputTransaction.outcome}`)}
          />
        ) : null}
      </div>

      {outputTransaction ? (
        <div className={`output-transaction-summary output-transaction-${outputTransaction.outcome}`}>
          <p>
            {t('workflowPanels.outputTransaction.summary', {
              count: outputTransaction.targetCount,
              outcome: t(`workflowPanels.outputTransaction.outcome.${outputTransaction.outcome}`),
              time: new Intl.DateTimeFormat(formatLocale, {
                dateStyle: 'medium',
                timeStyle: 'short'
              }).format(new Date(outputTransaction.completedAtUtc))
            })}
          </p>
          <details>
            <summary>{t('workflowPanels.outputTransaction.details')}</summary>
            <dl>
              <div>
                <dt>{t('workflowPanels.outputTransaction.id')}</dt>
                <dd data-localization-ignore="true">{outputTransaction.transactionId}</dd>
              </div>
              {outputTransaction.outcomeCode ? (
                <div>
                  <dt>{t('workflowPanels.outputTransaction.outcomeCode')}</dt>
                  <dd data-localization-ignore="true">{outputTransaction.outcomeCode}</dd>
                </div>
              ) : null}
            </dl>
          </details>
        </div>
      ) : null}

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
  const { t, translateLiteral } = useLocalization();

  return (
    <section aria-labelledby="change-plan-heading" className="panel wide-panel">
      <div className="panel-heading">
        <ClipboardCheck aria-hidden="true" size={18} />
        <h2 id="change-plan-heading">{translateLiteral('Output Plan')}</h2>
        <ContextHelp label={translateLiteral('Output Plan')}>
          {t('workflowPanels.outputPlanHelp')}
        </ContextHelp>
      </div>

      <div className="change-plan-status">
        <Metric
          help={t('workflowPanels.metric.planStatusHelp')}
          label="Plan status"
          value={changePlan.canApply ? 'Ready' : 'Needs fixes'}
        />
        <Metric
          help={t('workflowPanels.metric.targetFilesHelp')}
          label="Target files"
          value={changePlan.writes.length.toString()}
        />
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
  const { t, translateLiteral } = useLocalization();
  const diagnosticNavigation = useDiagnosticNavigation();
  const formatMessage = (diagnostic: ApiDiagnostic) => (
    formatDiagnosticSummary(diagnostic, translateLiteral, t)
  );
  const presentedMessage = (diagnostic: ApiDiagnostic) => (
    presentationDiagnosticMessage(diagnostic, diagnostics, formatMessage)
  );
  const presentedSeverity = (diagnostic: ApiDiagnostic) => (
    presentationDiagnosticSeverity(diagnostic, diagnostics, formatMessage)
  );
  const groupedDiagnostics = groupDiagnosticsForPresentation(
    diagnostics,
    (diagnostic) => [presentedSeverity(diagnostic), presentedMessage(diagnostic)],
    (diagnostic) => [
      diagnostic.severity,
      diagnostic.code,
      diagnostic.domain,
      diagnostic.field
    ],
    (diagnostic) => diagnosticSeverityPriority(presentedSeverity(diagnostic))
  );
  const isScrollable = scrollAfterEntries !== undefined &&
    groupedDiagnostics.length > scrollAfterEntries;
  const groups = [
    {
      diagnostics: groupedDiagnostics.filter(
        ({ diagnostics: identities }) => presentedSeverity(identities[0]!.diagnostic) === 'error'
      ),
      label: 'Error',
      severity: 'error' as const
    },
    {
      diagnostics: groupedDiagnostics.filter(
        ({ diagnostics: identities }) => presentedSeverity(identities[0]!.diagnostic) === 'warning'
      ),
      label: 'Warnings',
      severity: 'warning' as const
    },
    {
      diagnostics: groupedDiagnostics.filter(
        ({ diagnostics: identities }) => presentedSeverity(identities[0]!.diagnostic) === 'info'
      ),
      label: 'Information',
      severity: 'info' as const
    }
  ].filter((group) => group.diagnostics.length > 0);

  if (groups.length === 0) {
    return null;
  }
  const primaryAction = [...diagnostics]
    .sort((left, right) => (
      diagnosticSeverityPriority(right.severity) - diagnosticSeverityPriority(left.severity)
    ))
    .map((diagnostic) => diagnosticNavigation.resolveAction(diagnostic))
    .find((action) => action !== null);

  return (
    <section aria-labelledby="diagnostics-heading" className="panel">
      <div className="panel-heading">
        <Activity aria-hidden="true" size={18} />
        <h2 id="diagnostics-heading">{translateLiteral('Diagnostics')}</h2>
        <ContextHelp label={translateLiteral('Diagnostics')}>
          {t('workflowPanels.diagnosticsHelp')}
        </ContextHelp>
        {primaryAction ? (
          <button
            className="diagnostic-open-action secondary-button"
            onClick={() => diagnosticNavigation.navigate(primaryAction.location)}
            type="button"
          >
            {t('diagnostics.openAction', {
              target: translateLiteral(primaryAction.targetLabel)
            })}
          </button>
        ) : null}
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
              <span className="diagnostic-count">
                {group.diagnostics.reduce((total, entry) => total + entry.count, 0)}
              </span>
            </summary>
            <ul className="diagnostic-list">
              {group.diagnostics.map(({ count, diagnostics: identities, key }) => {
                const diagnostic = identities[0]!.diagnostic;
                const severity = presentedSeverity(diagnostic);
                return (
                <li
                  className={`diagnostic diagnostic-${severity}`}
                  key={key}
                >
                  <strong>
                    {translateLiteral(formatDiagnosticSeverity(severity))}
                  </strong>
                  <div className="km-analysis-diagnostic-copy">
                    {presentedMessage(diagnostic)}
                    <OccurrenceCount count={count} />
                    <DiagnosticTechnicalDetails
                      diagnostics={identities}
                      summary={translateLiteral('Technical details')}
                    />
                  </div>
                </li>
                );
              })}
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
