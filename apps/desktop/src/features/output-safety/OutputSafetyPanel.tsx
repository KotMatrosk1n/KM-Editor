/* SPDX-License-Identifier: GPL-3.0-only */

import {
  AlertCircle,
  AlertTriangle,
  CheckCircle,
  ChevronDown,
  ChevronUp,
  Clipboard,
  History,
  RefreshCw,
  ShieldCheck,
  Trash2
} from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { type OutputCheckpoint } from '../../bridge/outputSafetyContracts';
import { formatDiagnosticMessage } from '../../diagnostics';
import { useLocalization } from '../../localization';
import { type OutputSafetyController } from './useOutputSafetyController';

export function OutputSafetyPanel({ controller }: { controller: OutputSafetyController }) {
  const { formatLocale, t, translateLiteral } = useLocalization();
  const [detailsOpen, setDetailsOpen] = useState(false);
  const [checkpointLabel, setCheckpointLabel] = useState('');
  const [checkpointPendingDelete, setCheckpointPendingDelete] = useState<string | null>(null);
  const [supportReportCopied, setSupportReportCopied] = useState(false);
  const forceOpen = controller.readiness === 'blocked' || controller.readiness === 'error';
  const bodyOpen = detailsOpen || forceOpen;
  const status = getStatusPresentation(controller.readiness);
  const StatusIcon = status.icon;
  const cleanupTargetCount = controller.integrity?.entries.filter(
    (entry) => entry.cleanupEligible
  ).length ?? 0;
  const canReconcileVerifiedRecovery =
    controller.recoveryStatus?.requiresRecovery === false &&
    controller.recoveryStatus.pendingReconciliationCount > 0;
  const unknownRecoveryTransactions = controller.recoveryStatus?.transactions.filter(
    (entry) => entry.unknownTargetCount > 0
  ) ?? [];
  const unreadableRecoveryTransactions = controller.recoveryStatus?.transactions.filter(
    (entry) => !entry.journalReadable
  ) ?? [];
  const allDiagnostics = useMemo(() => [
    ...controller.actionDiagnostics,
    ...(controller.recoveryStatus?.diagnostics ?? []),
    ...(controller.integrity?.diagnostics ?? []),
    ...(controller.cleanupPreview?.diagnostics ?? []),
    ...(controller.cleanupResult?.diagnostics ?? []),
    ...(controller.checkpointRestorePreview?.diagnostics ?? []),
    ...(controller.restoreResult?.diagnostics ?? [])
  ], [controller]);

  useEffect(() => {
    setCheckpointPendingDelete(null);
    setSupportReportCopied(false);
  }, [controller.checkpoints, controller.supportReport]);

  if (!controller.isAvailable) {
    return null;
  }

  return (
    <section
      aria-labelledby="output-safety-heading"
      className={`panel wide-panel output-safety-panel output-safety-tone-${status.tone}`}
    >
      <div className="output-safety-summary">
        <div className="panel-heading output-safety-heading">
          <StatusIcon aria-hidden="true" size={18} />
          <div>
            <h2 id="output-safety-heading">{t('outputSafety.title')}</h2>
            <p>{t(status.summaryKey)}</p>
          </div>
        </div>
        <div className="output-safety-summary-actions">
          <span className={`status-badge status-${status.tone}`}>{t(status.labelKey)}</span>
          {!forceOpen ? (
            <button
              aria-expanded={detailsOpen}
              className="secondary-button compact-button"
              onClick={() => setDetailsOpen((current) => !current)}
              type="button"
            >
              {detailsOpen ? <ChevronUp aria-hidden="true" size={15} /> : <ChevronDown aria-hidden="true" size={15} />}
              <span>{t(detailsOpen ? 'outputSafety.hideDetails' : 'outputSafety.showDetails')}</span>
            </button>
          ) : null}
        </div>
      </div>

      {bodyOpen ? (
        <div className="output-safety-details">
          <section className="output-safety-group" aria-labelledby="output-recovery-heading">
            <div className="output-safety-group-heading">
              <div>
                <h3 id="output-recovery-heading">{t('outputSafety.recovery.title')}</h3>
                <p>{formatRecoverySummary(controller, t)}</p>
              </div>
              <div className="output-safety-actions">
                <button
                  className="secondary-button compact-button"
                  disabled={controller.busyAction !== null}
                  onClick={() => void controller.refreshRecovery()}
                  type="button"
                >
                  <RefreshCw aria-hidden="true" size={15} />
                  <span>{t('outputSafety.refresh')}</span>
                </button>
                {canReconcileVerifiedRecovery ? (
                  <button
                    className="primary-button compact-button"
                    disabled={!controller.canMutate}
                    onClick={() => void controller.reconcileRecovery()}
                    type="button"
                  >
                    <ShieldCheck aria-hidden="true" size={15} />
                    <span>{t('outputSafety.reconcile')}</span>
                  </button>
                ) : null}
              </div>
            </div>
            {controller.recoveryStatus?.requiresRecovery ? (
              <div className="output-safety-warning output-recovery-manual">
                <AlertTriangle aria-hidden="true" size={15} />
                <div>
                  {unknownRecoveryTransactions.length > 0 ? (
                    <>
                      <p>{t('outputSafety.recovery.unknownTargets')}</p>
                      <p>{t('outputSafety.recovery.manualGuidance')}</p>
                    </>
                  ) : null}
                  {unknownRecoveryTransactions.map((transaction) => (
                    <details className="output-recovery-targets" key={transaction.transactionId}>
                      <summary>
                        {t('outputSafety.recovery.transactionUnknownTargets', {
                          count: transaction.unknownTargetCount
                        })}
                      </summary>
                      <ul>
                        {transaction.unknownTargets.map((target) => (
                          <li data-localization-ignore="true" key={target}>{target}</li>
                        ))}
                      </ul>
                      {transaction.unknownTargetsTruncated ? (
                        <p>{t('outputSafety.recovery.unknownTargetsTruncated', {
                          count: transaction.unknownTargetCount,
                          shown: transaction.unknownTargets.length
                        })}</p>
                      ) : null}
                    </details>
                  ))}
                  {unreadableRecoveryTransactions.length > 0 ? (
                    <>
                      <p>{t('outputSafety.recovery.metadataUnavailable')}</p>
                      <p>{t('outputSafety.recovery.metadataGuidance')}</p>
                      <ul>
                        {unreadableRecoveryTransactions.map((transaction) => (
                          <li key={transaction.transactionId}>
                            <span>{t('workflowPanels.outputTransaction.id')}</span>{' '}
                            <code data-localization-ignore="true">{transaction.transactionId}</code>
                          </li>
                        ))}
                      </ul>
                    </>
                  ) : null}
                </div>
              </div>
            ) : null}
            {controller.recoveryStatus?.transactionsTruncated ? (
              <p className="output-safety-muted">
                {t('outputSafety.recovery.transactionsTruncated', {
                  count: controller.recoveryStatus.transactionCount,
                  shown: controller.recoveryStatus.transactions.length
                })}
              </p>
            ) : null}
          </section>

          <section className="output-safety-group" aria-labelledby="output-integrity-heading">
            <div className="output-safety-group-heading">
              <div>
                <h3 id="output-integrity-heading">{t('outputSafety.integrity.title')}</h3>
                <p>
                  {controller.integrity
                    ? t('outputSafety.integrity.scanned', {
                        count: controller.integrity.entries.length,
                        time: formatDate(controller.integrity.scannedAtUtc, formatLocale)
                      })
                    : t('outputSafety.integrity.notScanned')}
                </p>
              </div>
              <button
                className="secondary-button compact-button"
                disabled={controller.busyAction !== null || controller.recoveryStatus === null}
                onClick={() => void controller.scanIntegrity()}
                type="button"
              >
                <RefreshCw aria-hidden="true" size={15} />
                <span>{t(controller.busyAction === 'scan' ? 'outputSafety.integrity.scanning' : 'outputSafety.integrity.scan')}</span>
              </button>
            </div>
            {controller.integrity ? (
              <>
                <dl className="output-integrity-counts">
                  {Object.entries(controller.integrity.counts).map(([classification, count]) => (
                    <div key={classification}>
                      <dt>{t(`outputSafety.integrity.classification.${classification}`)}</dt>
                      <dd>{count}</dd>
                    </div>
                  ))}
                </dl>
                {controller.integrity.truncated ? (
                  <p className="output-safety-warning">
                    <AlertTriangle aria-hidden="true" size={15} />
                    <span>{t('outputSafety.integrity.truncated')}</span>
                  </p>
                ) : null}
                <details className="output-safety-inventory">
                  <summary>{t('outputSafety.integrity.inventory', { count: controller.integrity.entries.length })}</summary>
                  <ul>
                    {controller.integrity.entries.map((entry) => (
                      <li key={entry.targetId}>
                        <span data-localization-ignore="true">{entry.relativePath}</span>
                        <span>{t(`outputSafety.integrity.classification.${entry.classification}`)}</span>
                      </li>
                    ))}
                  </ul>
                </details>
              </>
            ) : null}
          </section>

          <section className="output-safety-group" aria-labelledby="output-cleanup-heading">
            <div className="output-safety-group-heading">
              <div>
                <h3 id="output-cleanup-heading">{t('outputSafety.cleanup.title')}</h3>
                <p>{t('outputSafety.cleanup.description', { count: cleanupTargetCount })}</p>
              </div>
              <div className="output-safety-actions">
                <button
                  className="secondary-button compact-button"
                  disabled={controller.busyAction !== null || cleanupTargetCount === 0 || !controller.canApply}
                  onClick={() => void controller.previewCleanup()}
                  type="button"
                >
                  <ShieldCheck aria-hidden="true" size={15} />
                  <span>{t('outputSafety.cleanup.preview')}</span>
                </button>
                {controller.cleanupPreview && controller.cleanupPreview.candidates.length > 0 ? (
                  <button
                    className="danger-button compact-button"
                    disabled={controller.busyAction !== null || !controller.canApply}
                    onClick={() => void controller.applyCleanup()}
                    type="button"
                  >
                    <Trash2 aria-hidden="true" size={15} />
                    <span>{t('outputSafety.cleanup.apply')}</span>
                  </button>
                ) : null}
              </div>
            </div>
            {controller.cleanupPreview ? (
              <>
                <p>{t('outputSafety.cleanup.previewSummary', {
                  bytes: formatByteCount(controller.cleanupPreview.totalBytes, formatLocale),
                  count: controller.cleanupPreview.candidates.length
                })}</p>
                <ReviewedTargetList
                  paths={controller.cleanupPreview.candidates.map((candidate) => candidate.relativePath)}
                  total={controller.cleanupPreview.candidates.length}
                />
              </>
            ) : null}
            {controller.cleanupResult ? (
              <ul className="output-cleanup-results">
                {controller.cleanupResult.entries.map((entry) => (
                  <li
                    className={
                      entry.disposition === 'removed' || entry.disposition === 'forgotMissing'
                        ? 'is-success'
                        : entry.disposition === 'applyNotCommitted'
                          ? 'is-error'
                          : 'is-skipped'
                    }
                    key={entry.targetId}
                  >
                    <span data-localization-ignore="true">{entry.relativePath}</span>
                    <span>{t(`outputSafety.cleanup.disposition.${entry.disposition}`)}</span>
                  </li>
                ))}
              </ul>
            ) : null}
          </section>

          <section className="output-safety-group" aria-labelledby="output-activity-heading">
            <div className="output-safety-group-heading">
              <div>
                <h3 id="output-activity-heading">{t('outputSafety.activity.title')}</h3>
                <p>{t('outputSafety.activity.description')}</p>
              </div>
              <button
                className="secondary-button compact-button"
                disabled={controller.busyAction !== null}
                onClick={() => void controller.loadActivity()}
                type="button"
              >
                <History aria-hidden="true" size={15} />
                <span>{t('outputSafety.activity.load')}</span>
              </button>
            </div>

            {controller.history ? (
              <div className="output-safety-subgroup">
                <h4>{t('outputSafety.history.title')}</h4>
                {controller.history.receipts.length === 0 ? (
                  <p className="empty-copy">{t('outputSafety.history.empty')}</p>
                ) : (
                  <ol className="output-history-list">
                    {controller.history.receipts.map((receipt) => (
                      <li key={receipt.transactionId}>
                        <strong>{t(`outputSafety.transaction.outcome.${receipt.outcome}`)}</strong>
                        <span>{t('outputSafety.history.summary', {
                          count: receipt.targetCount,
                          mode: receipt.outputMode,
                          time: formatDate(receipt.completedAtUtc, formatLocale)
                        })}</span>
                      </li>
                    ))}
                  </ol>
                )}
                {controller.history.nextCursor ? (
                  <button
                    className="secondary-button compact-button"
                    disabled={controller.busyAction !== null}
                    onClick={() => void controller.loadMoreHistory()}
                    type="button"
                  >
                    <History aria-hidden="true" size={15} />
                    <span>{t('outputSafety.history.loadMore')}</span>
                  </button>
                ) : null}
              </div>
            ) : null}

            {controller.checkpoints ? (
              <div className="output-safety-subgroup">
                <div className="output-checkpoint-create">
                  <label>
                    <span>{t('outputSafety.checkpoints.label')}</span>
                    <input
                      maxLength={256}
                      onChange={(event) => setCheckpointLabel(event.currentTarget.value)}
                      placeholder={t('outputSafety.checkpoints.labelPlaceholder')}
                      type="text"
                      value={checkpointLabel}
                    />
                  </label>
                  <button
                    className="secondary-button compact-button"
                    disabled={controller.busyAction !== null || !controller.canApply}
                    onClick={() => {
                      void controller.createCheckpoint(checkpointLabel);
                      setCheckpointLabel('');
                    }}
                    type="button"
                  >
                    <ShieldCheck aria-hidden="true" size={15} />
                    <span>{t('outputSafety.checkpoints.create')}</span>
                  </button>
                </div>
                <h4>{t('outputSafety.checkpoints.title')}</h4>
                {controller.checkpoints.checkpoints.length === 0 ? (
                  <p className="empty-copy">{t('outputSafety.checkpoints.empty')}</p>
                ) : (
                  <ul className="output-checkpoint-list">
                    {controller.checkpoints.checkpoints.map((checkpoint) => (
                      <CheckpointRow
                        checkpoint={checkpoint}
                        controller={controller}
                        key={checkpoint.checkpointId}
                        formatLocale={formatLocale}
                        onDeletePendingChange={setCheckpointPendingDelete}
                        pendingDelete={checkpointPendingDelete === checkpoint.checkpointId}
                      />
                    ))}
                  </ul>
                )}
                {controller.checkpointRestorePreview ? (
                  <div className="output-checkpoint-restore-review" role="alert">
                    <p>{t('outputSafety.checkpoints.restoreSummary', {
                      bytes: formatByteCount(controller.checkpointRestorePreview.totalBytes, formatLocale),
                      count: controller.checkpointRestorePreview.targetCount
                    })}</p>
                    <ReviewedTargetList
                      paths={controller.checkpointRestorePreview.targets}
                      total={controller.checkpointRestorePreview.targetCount}
                    />
                    <button
                      className="danger-button compact-button"
                      disabled={!controller.checkpointRestorePreview.canRestore || controller.busyAction !== null || !controller.canApply}
                      onClick={() => void controller.restoreCheckpoint()}
                      type="button"
                    >
                      <RefreshCw aria-hidden="true" size={15} />
                      <span>{t('outputSafety.checkpoints.restore')}</span>
                    </button>
                  </div>
                ) : null}
              </div>
            ) : null}
          </section>

          <section className="output-safety-group" aria-labelledby="output-support-heading">
            <div className="output-safety-group-heading">
              <div>
                <h3 id="output-support-heading">{t('outputSafety.support.title')}</h3>
                <p>{t('outputSafety.support.description')}</p>
              </div>
              <button
                className="secondary-button compact-button"
                disabled={controller.busyAction !== null}
                onClick={() => void controller.buildSupportReport()}
                type="button"
              >
                <Clipboard aria-hidden="true" size={15} />
                <span>{t('outputSafety.support.build')}</span>
              </button>
            </div>
            {controller.supportReport ? (
              <div className="output-support-report">
                <pre data-localization-ignore="true">{JSON.stringify(controller.supportReport.report, null, 2)}</pre>
                <button
                  className="secondary-button compact-button"
                  onClick={() => {
                    if (!navigator.clipboard) {
                      return;
                    }
                    void navigator.clipboard
                      .writeText(JSON.stringify(controller.supportReport!.report, null, 2))
                      .then(() => setSupportReportCopied(true))
                      .catch(() => undefined);
                  }}
                  type="button"
                >
                  <Clipboard aria-hidden="true" size={15} />
                  <span>{t(supportReportCopied ? 'outputSafety.support.copied' : 'outputSafety.support.copy')}</span>
                </button>
              </div>
            ) : null}
          </section>

          {allDiagnostics.length > 0 ? (
            <ul className="output-safety-diagnostics">
              {allDiagnostics.map((diagnostic, index) => (
                <li className={`diagnostic-${diagnostic.severity}`} key={`${diagnostic.code ?? 'diagnostic'}-${index}`}>
                  {diagnostic.severity === 'error' ? <AlertCircle aria-hidden="true" size={15} /> : <AlertTriangle aria-hidden="true" size={15} />}
                  <span>{formatDiagnosticMessage(diagnostic, translateLiteral, t)}</span>
                </li>
              ))}
            </ul>
          ) : null}
        </div>
      ) : null}
    </section>
  );
}

function ReviewedTargetList({ paths, total }: { paths: string[]; total: number }) {
  const { t } = useLocalization();
  return (
    <details className="output-safety-inventory">
      <summary>{t('outputSafety.reviewedTargets', { shown: paths.length, total })}</summary>
      <ul>
        {paths.map((path) => (
          <li data-localization-ignore="true" key={path}>{path}</li>
        ))}
      </ul>
    </details>
  );
}

function CheckpointRow({
  checkpoint,
  controller,
  formatLocale,
  onDeletePendingChange,
  pendingDelete
}: {
  checkpoint: OutputCheckpoint;
  controller: OutputSafetyController;
  formatLocale: string;
  onDeletePendingChange: (checkpointId: string | null) => void;
  pendingDelete: boolean;
}) {
  const { t } = useLocalization();
  return (
    <li>
      <div>
        <strong>{checkpoint.label ?? t('outputSafety.checkpoints.unnamed')}</strong>
        <span>{t('outputSafety.checkpoints.summary', {
          bytes: formatByteCount(checkpoint.totalBytes, formatLocale),
          count: checkpoint.fileCount,
          coverage: t(`outputSafety.checkpoints.coverage.${checkpoint.coverage}`),
          time: formatDate(checkpoint.createdAtUtc, formatLocale)
        })}</span>
      </div>
      <div className="output-safety-actions">
        <button
          className="secondary-button compact-button"
          disabled={controller.busyAction !== null || !controller.canApply}
          onClick={() => void controller.previewCheckpointRestore(checkpoint)}
          type="button"
        >
          <RefreshCw aria-hidden="true" size={14} />
          <span>{t('outputSafety.checkpoints.reviewRestore')}</span>
        </button>
        <button
          className={pendingDelete ? 'danger-button compact-button' : 'secondary-button compact-button'}
          disabled={controller.busyAction !== null || !controller.canApply}
          onClick={() => {
            if (pendingDelete) {
              void controller.deleteCheckpoint(checkpoint);
              onDeletePendingChange(null);
            } else {
              onDeletePendingChange(checkpoint.checkpointId);
            }
          }}
          type="button"
        >
          <Trash2 aria-hidden="true" size={14} />
          <span>{t(pendingDelete ? 'outputSafety.checkpoints.confirmDelete' : 'outputSafety.checkpoints.delete')}</span>
        </button>
      </div>
    </li>
  );
}

function getStatusPresentation(readiness: OutputSafetyController['readiness']) {
  switch (readiness) {
    case 'ready':
      return { icon: CheckCircle, labelKey: 'outputSafety.status.ready', summaryKey: 'outputSafety.summary.ready', tone: 'success' } as const;
    case 'blocked':
      return { icon: AlertTriangle, labelKey: 'outputSafety.status.blocked', summaryKey: 'outputSafety.summary.blocked', tone: 'warning' } as const;
    case 'error':
      return { icon: AlertCircle, labelKey: 'outputSafety.status.error', summaryKey: 'outputSafety.summary.error', tone: 'error' } as const;
    case 'checking':
      return { icon: RefreshCw, labelKey: 'outputSafety.status.checking', summaryKey: 'outputSafety.summary.checking', tone: 'neutral' } as const;
    case 'unavailable':
      return { icon: ShieldCheck, labelKey: 'outputSafety.status.unavailable', summaryKey: 'outputSafety.summary.unavailable', tone: 'neutral' } as const;
  }
}

function formatRecoverySummary(
  controller: OutputSafetyController,
  t: ReturnType<typeof useLocalization>['t']
) {
  if (!controller.recoveryStatus) {
    return t('outputSafety.recovery.pending');
  }
  if (!controller.recoveryStatus.requiresRecovery) {
    return controller.recoveryStatus.pendingReconciliationCount > 0
      ? t('outputSafety.recovery.verifiedPending', {
          count: controller.recoveryStatus.pendingReconciliationCount
        })
      : t('outputSafety.recovery.none');
  }
  return t('outputSafety.recovery.required', {
    count: controller.recoveryStatus.transactionCount
  });
}

function formatDate(value: string, language: string) {
  return new Intl.DateTimeFormat(language, {
    dateStyle: 'medium',
    timeStyle: 'short'
  }).format(new Date(value));
}

function formatByteCount(value: string, language: string) {
  try {
    return new Intl.NumberFormat(language).format(BigInt(value));
  } catch {
    return value;
  }
}
