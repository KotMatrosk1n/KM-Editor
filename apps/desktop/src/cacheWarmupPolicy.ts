/* SPDX-License-Identifier: GPL-3.0-only */

export const maxConsecutiveNoProgressWarmupAttempts = 4;

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
