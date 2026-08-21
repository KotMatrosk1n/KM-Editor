/* SPDX-License-Identifier: GPL-3.0-only */

import type { CSSProperties } from 'react';
import './LoadingProgress.css';

export type LoadingProgressProps = {
  className?: string;
  completed?: number;
  label: string;
  total?: number;
};

export function LoadingProgress({
  className,
  completed,
  label,
  total
}: LoadingProgressProps) {
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
  const classes = ['km-loading-progress', className].filter(Boolean).join(' ');

  return (
    <div aria-live="polite" className={classes} role="status">
      <div className="km-loading-progress-heading">
        <p className="km-loading-progress-copy">{label}</p>
        {displayedPercentage !== null && boundedCompleted !== null && total !== undefined ? (
          <span className="km-loading-progress-count" data-localization-ignore="true">
            {boundedCompleted} / {total} ({displayedPercentage}%)
          </span>
        ) : null}
      </div>
      <div
        aria-hidden="true"
        className={`km-loading-progress-visual${isDeterminate ? ' is-determinate' : ''}`}
      >
        <span className="km-loading-progress-indicator" style={indicatorStyle} />
      </div>
      <progress
        aria-label={label}
        aria-valuetext={isDeterminate && boundedCompleted !== null && total !== undefined
          ? `${boundedCompleted} / ${total}`
          : undefined}
        className="km-loading-progress-native"
        {...(isDeterminate && boundedCompleted !== null && total !== undefined
          ? { max: total, value: boundedCompleted }
          : {})}
      />
    </div>
  );
}
