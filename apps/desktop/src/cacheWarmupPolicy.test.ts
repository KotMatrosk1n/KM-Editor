/* SPDX-License-Identifier: GPL-3.0-only */

import { describe, expect, it } from 'vitest';
import {
  maxConsecutiveNoProgressWarmupAttempts,
  updateWarmupNoProgressBudget
} from './cacheWarmupPolicy';

describe('Trinity cache warmup policy', () => {
  it('stops after a small fixed number of consecutive no-progress steps', () => {
    let remaining = maxConsecutiveNoProgressWarmupAttempts;
    for (let index = 0; index < maxConsecutiveNoProgressWarmupAttempts; index += 1) {
      remaining = updateWarmupNoProgressBudget(remaining, 10, 300, 10, 300);
    }

    expect(remaining).toBe(0);
  });

  it('resets the fixed budget when warmup makes progress', () => {
    expect(updateWarmupNoProgressBudget(1, 10, 300, 11, 300)).toBe(
      maxConsecutiveNoProgressWarmupAttempts
    );
  });
});
