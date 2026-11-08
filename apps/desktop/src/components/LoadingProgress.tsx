/* SPDX-License-Identifier: GPL-3.0-only */

import { Check, Circle, CircleAlert, LoaderCircle, Pause, XCircle } from 'lucide-react';
import { useEffect, useRef, useState, type CSSProperties, type ReactNode } from 'react';
import { useLocalization } from '../localization';
import './LoadingProgress.css';

export type ProgressStep = { label: string; state: 'active' | 'complete' | 'pending' };
export type ProgressStatus = 'running' | 'complete' | 'error' | 'canceled' | 'paused' | 'idle';

export type LoadingProgressProps = {
  className?: string;
  completed?: number;
  countLabel?: string;
  detail?: string;
  detailAsTitle?: boolean;
  elapsedMilliseconds?: number;
  label: string;
  rawLabel?: boolean;
  onCancel?: () => void;
  cancelLabel?: string;
  cancellationRequested?: boolean;
  operationKey?: string | number;
  status?: ProgressStatus;
  statusLabel?: string;
  steps?: readonly ProgressStep[];
  total?: number;
  children?: ReactNode;
};

const statusIcons = {
  running: LoaderCircle,
  complete: Check,
  error: CircleAlert,
  canceled: XCircle,
  paused: Pause,
  idle: Circle
};

export function LoadingProgressSteps({ label, steps, status = 'running' }: {
  label: string;
  steps: readonly ProgressStep[];
  status?: ProgressStatus;
}) {
  const { t } = useLocalization();
  return (
    <ol className="km-loading-progress-steps"
      aria-label={t('editorText.progress.phases', { operation: label })}>
      {steps.map((step, index) => {
        const active = step.state === 'active';
        const Icon = step.state === 'complete' ? Check : active ? statusIcons[status] : Circle;
        return (
          <li key={`${index}-${step.label}`} className={`is-${step.state}`}
            aria-current={active ? 'step' : undefined}>
            <Icon aria-hidden="true" className={active && status === 'running' ? 'km-loading-progress-spinner' : undefined}
              size={16} />
            <span>{step.label}</span>
          </li>
        );
      })}
    </ol>
  );
}

export function LoadingProgress({
  className,
  completed,
  countLabel,
  detail,
  detailAsTitle = false,
  elapsedMilliseconds,
  label,
  rawLabel = false,
  onCancel,
  cancelLabel,
  cancellationRequested = false,
  operationKey,
  status = 'running',
  statusLabel,
  steps,
  total,
  children
}: LoadingProgressProps) {
  const { t, formatLocale } = useLocalization();
  const running = status === 'running';
  const externalClock = elapsedMilliseconds !== undefined;
  const startedAt = useRef<number | null>(null);
  const previousKey = useRef(operationKey);
  const [localElapsed, setLocalElapsed] = useState<number | null>(null);
  useEffect(() => {
    if (previousKey.current !== operationKey) {
      previousKey.current = operationKey;
      startedAt.current = null;
      setLocalElapsed(null);
    }
    if (externalClock) return;
    if (!running) {
      if (startedAt.current !== null) {
        setLocalElapsed(performance.now() - startedAt.current);
        startedAt.current = null;
      }
      return;
    }
    startedAt.current = performance.now();
    setLocalElapsed(0);
    const timer = window.setInterval(() => {
      if (startedAt.current !== null) setLocalElapsed(performance.now() - startedAt.current);
    }, 1000);
    return () => window.clearInterval(timer);
  }, [externalClock, operationKey, running]);
  const elapsed = elapsedMilliseconds ?? localElapsed;
  const seconds = Math.floor(Math.max(0, Number.isFinite(elapsed) ? elapsed ?? 0 : 0) / 1000);
  const time = `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
  const isDeterminate = Number.isFinite(completed) &&
    Number.isFinite(total) &&
    (total ?? 0) > 0;
  const boundedCompleted = isDeterminate
    ? Math.min(Math.max(completed ?? 0, 0), total ?? 0)
    : null;
  const percentage = boundedCompleted === null || total === undefined
    ? null
    : (boundedCompleted / total) * 100;
  const displayedPercentage = percentage === null || boundedCompleted === null || total === undefined
    ? null
    : boundedCompleted >= total
      ? 100
      : Math.min(99, Math.floor(percentage));
  const indicatorStyle = percentage === null
    ? undefined
    : { '--km-loading-progress-value': `${percentage}%` } as CSSProperties;
  const classes = ['km-loading-progress', `is-${status}`, className].filter(Boolean).join(' ');
  const Icon = statusIcons[status];
  const summary = cancellationRequested && running ? t('progressFeedback.canceling')
    : statusLabel ?? t(`progressFeedback.${status}`);
  const count = countLabel ?? (boundedCompleted !== null && total !== undefined
    ? `${boundedCompleted.toLocaleString(formatLocale)} / ${total.toLocaleString(formatLocale)}` : undefined);
  const titleDetail = detailAsTitle && !steps?.length ? detail : undefined;
  const caption = titleDetail ? undefined : detail;

  return (
    <section aria-label={label} className={classes}>
      <div className={steps?.length ? 'km-loading-progress-phases' : 'km-loading-progress-heading'}>
        {steps?.length ? <>
          {rawLabel ? <strong className="km-loading-progress-copy" data-localization-ignore="true">{label}</strong> : null}
          <LoadingProgressSteps label={label} steps={steps} status={status} />
          <p className={running && !cancellationRequested ? 'km-loading-progress-announcement' : 'km-loading-progress-state'}
            role={status === 'error' || status === 'paused' ? 'alert' : 'status'}>{summary}</p>
        </> : <>
          <Icon aria-hidden="true" className={running ? 'km-loading-progress-spinner' : undefined} size={22} />
          <div className="km-loading-progress-title">
            <strong className="km-loading-progress-copy" data-localization-ignore={rawLabel || undefined}
              role={titleDetail ? 'status' : undefined}>{titleDetail ?? label}</strong>
            {(!titleDetail || !running || cancellationRequested) ? (
              <p role={status === 'error' || status === 'paused' ? 'alert' : 'status'}>{summary}</p>
            ) : null}
          </div>
        </>}
        {elapsed !== null ? <span className="km-loading-progress-elapsed">
          {t('progressFeedback.elapsed', { time })}
        </span> : null}
        {running && onCancel ? <button className="secondary-button" type="button"
          disabled={cancellationRequested} onClick={onCancel}>
          {cancelLabel ?? t('progressFeedback.cancel')}
        </button> : null}
      </div>
      {caption || displayedPercentage !== null ? <div className="km-loading-progress-caption">
        {caption ? <p role="status" aria-live="polite">{caption}</p> : null}
        {displayedPercentage !== null && boundedCompleted !== null && total !== undefined ? (
          <span className="km-loading-progress-count" data-localization-ignore="true">
            {count} ({displayedPercentage}%)
          </span>
        ) : null}
      </div> : null}
      {running || isDeterminate ? <><div
        aria-hidden="true"
        className={`km-loading-progress-visual${isDeterminate ? ' is-determinate' : ''}`}
      >
        <span className="km-loading-progress-indicator" style={indicatorStyle} />
      </div>
      <progress
        aria-label={detail ?? label}
        aria-valuetext={isDeterminate && boundedCompleted !== null && total !== undefined
          ? `${count} (${displayedPercentage}%)`
          : undefined}
        className="km-loading-progress-native"
        {...(isDeterminate && boundedCompleted !== null && total !== undefined
          ? { max: total, value: boundedCompleted }
          : {})}
      /></> : null}
      {children ? <div className="km-loading-progress-footer">{children}</div> : null}
    </section>
  );
}
