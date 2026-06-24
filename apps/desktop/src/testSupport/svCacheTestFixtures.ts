/* SPDX-License-Identifier: GPL-3.0-only */

import { type ProjectBridge } from '../bridge/projectBridge';
import { type SvCacheStatus } from '../bridge/svCacheContracts';
import { type ZaCacheStatus } from '../bridge/zaCacheContracts';

export function createSvCacheStatusFixture(
  overrides: Partial<SvCacheStatus> = {}
): SvCacheStatus {
  return {
    cacheSizeBytes: 4 * 1024 * 1024,
    isActiveProjectPreserved: false,
    message: 'S/V cache metadata is ready.',
    phase: 'Ready',
    progressPercent: 100,
    settings: {
      maxCacheSizeBytes: 10 * 1024 ** 3,
      mode: 'balanced'
    },
    warmupCompleted: 3,
    warmupTotal: 3,
    ...overrides
  };
}

export function createSvCacheBridgeFixture(): Pick<
  ProjectBridge,
  'clearSvCache' | 'getSvCacheStatus' | 'updateSvCacheSettings' | 'warmupSvCacheStep'
> {
  let svCacheStatus = createSvCacheStatusFixture();

  return {
    clearSvCache: () => {
      svCacheStatus = createSvCacheStatusFixture({
        cacheSizeBytes: 0,
        isActiveProjectPreserved: true,
        message: 'S/V cache cleared.',
        progressPercent: 0,
        warmupCompleted: 0
      });

      return Promise.resolve({ status: svCacheStatus });
    },
    getSvCacheStatus: () => Promise.resolve({ status: svCacheStatus }),
    updateSvCacheSettings: (request) => {
      svCacheStatus = createSvCacheStatusFixture({
        settings: {
          maxCacheSizeBytes: request.maxCacheSizeBytes,
          mode: request.mode
        }
      });

      return Promise.resolve({ status: svCacheStatus });
    },
    warmupSvCacheStep: (request) => {
      const completed = Math.min(3, Math.max(0, request.stepIndex + 1));
      svCacheStatus = createSvCacheStatusFixture({
        message: completed >= 3 ? 'S/V cache warmup complete.' : 'Building S/V cache.',
        progressPercent: Math.round((completed / 3) * 100),
        warmupCompleted: completed
      });

      return Promise.resolve({ status: svCacheStatus });
    }
  };
}

export function createZaCacheStatusFixture(
  overrides: Partial<ZaCacheStatus> = {}
): ZaCacheStatus {
  return {
    cacheSizeBytes: 2 * 1024 * 1024,
    isActiveProjectPreserved: false,
    message: 'Z-A cache metadata is ready.',
    phase: 'Ready',
    progressPercent: 100,
    settings: {
      maxCacheSizeBytes: 10 * 1024 ** 3,
      mode: 'balanced'
    },
    warmupCompleted: 2,
    warmupTotal: 2,
    ...overrides
  };
}

export function createZaCacheBridgeFixture(): Pick<
  ProjectBridge,
  'clearZaCache' | 'getZaCacheStatus' | 'updateZaCacheSettings' | 'warmupZaCacheStep'
> {
  let zaCacheStatus = createZaCacheStatusFixture();

  return {
    clearZaCache: () => {
      zaCacheStatus = createZaCacheStatusFixture({
        cacheSizeBytes: 0,
        isActiveProjectPreserved: true,
        message: 'Z-A cache cleared.',
        progressPercent: 0,
        warmupCompleted: 0
      });

      return Promise.resolve({ status: zaCacheStatus });
    },
    getZaCacheStatus: () => Promise.resolve({ status: zaCacheStatus }),
    updateZaCacheSettings: (request) => {
      zaCacheStatus = createZaCacheStatusFixture({
        settings: {
          maxCacheSizeBytes: request.maxCacheSizeBytes,
          mode: request.mode
        }
      });

      return Promise.resolve({ status: zaCacheStatus });
    },
    warmupZaCacheStep: (request) => {
      const completed = Math.min(2, Math.max(0, request.stepIndex + 1));
      zaCacheStatus = createZaCacheStatusFixture({
        message: completed >= 2 ? 'Z-A cache warmup complete.' : 'Building Z-A cache.',
        progressPercent: Math.round((completed / 2) * 100),
        warmupCompleted: completed
      });

      return Promise.resolve({ status: zaCacheStatus });
    }
  };
}
