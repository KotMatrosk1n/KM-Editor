/* SPDX-License-Identifier: GPL-3.0-only */

export const maxConsecutiveNoProgressWarmupAttempts = 4;

export type CacheWarmupProgressSnapshot = {
  completedUnitCount: number;
  isReady: boolean;
  percent: number;
  totalUnitCount: number;
};

export function createCacheWarmupProgressSnapshot(
  completedUnitCount: number,
  totalUnitCount: number
): CacheWarmupProgressSnapshot {
  const normalizedTotalUnitCount = Number.isFinite(totalUnitCount)
    ? Math.max(0, Math.trunc(totalUnitCount))
    : 0;
  const normalizedCompletedUnitCount = Number.isFinite(completedUnitCount)
    ? Math.max(0, Math.trunc(completedUnitCount))
    : 0;
  const boundedCompletedUnitCount = Math.min(
    normalizedCompletedUnitCount,
    normalizedTotalUnitCount
  );
  const isReady = normalizedTotalUnitCount > 0 &&
    boundedCompletedUnitCount === normalizedTotalUnitCount;
  const measuredPercent = normalizedTotalUnitCount === 0
    ? 0
    : Math.floor((boundedCompletedUnitCount / normalizedTotalUnitCount) * 100);

  return {
    completedUnitCount: boundedCompletedUnitCount,
    isReady,
    percent: isReady ? 100 : Math.min(99, measuredPercent),
    totalUnitCount: normalizedTotalUnitCount
  };
}

export function updateWarmupNoProgressBudget(
  remainingAttempts: number,
  previousCompleted: number,
  previousTotal: number,
  currentCompleted: number,
  currentTotal: number
) {
  return currentCompleted <= previousCompleted && currentTotal === previousTotal
    ? Math.max(0, remainingAttempts - 1)
    : maxConsecutiveNoProgressWarmupAttempts;
}
