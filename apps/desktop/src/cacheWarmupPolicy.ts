/* SPDX-License-Identifier: GPL-3.0-only */

import type { ProjectPaths } from './bridge/contracts';

export type CacheWarmupProgressTransition =
  | { kind: 'advanced' }
  | { kind: 'stalled' }
  | {
      kind: 'invalid';
      reason: 'completed-regressed' | 'invalid-counts' | 'total-changed';
    };

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

export function createProjectCacheScopeKey(projectId: string, paths: ProjectPaths) {
  const selectedGame = paths.selectedGame;

  return JSON.stringify({
    baseExeFsPath: normalizeCacheScopePath(paths.baseExeFsPath),
    baseRomFsPath: normalizeCacheScopePath(paths.baseRomFsPath),
    gameTextLanguage: paths.gameTextLanguage?.trim() || null,
    outputRootPath: normalizeCacheScopePath(paths.outputRootPath),
    projectId,
    supportFolderPath:
      selectedGame === 'za'
        ? normalizeCacheScopePath(paths.pokemonLegendsZASupportFolderPath)
        : selectedGame === 'scarlet' || selectedGame === 'violet'
          ? normalizeCacheScopePath(paths.scarletVioletSupportFolderPath)
          : null,
    selectedGame
  });
}

export function evaluateCacheWarmupProgressTransition(
  previousCompletedUnitCount: number,
  expectedTotalUnitCount: number,
  nextCompletedUnitCount: number,
  nextTotalUnitCount: number
): CacheWarmupProgressTransition {
  if (
    !isValidWarmupCount(previousCompletedUnitCount) ||
    !isValidWarmupCount(expectedTotalUnitCount) ||
    !isValidWarmupCount(nextCompletedUnitCount) ||
    !isValidWarmupCount(nextTotalUnitCount)
  ) {
    return { kind: 'invalid', reason: 'invalid-counts' };
  }

  if (nextTotalUnitCount !== expectedTotalUnitCount) {
    return { kind: 'invalid', reason: 'total-changed' };
  }

  if (
    previousCompletedUnitCount > expectedTotalUnitCount ||
    nextCompletedUnitCount > expectedTotalUnitCount
  ) {
    return { kind: 'invalid', reason: 'invalid-counts' };
  }

  if (nextCompletedUnitCount < previousCompletedUnitCount) {
    return { kind: 'invalid', reason: 'completed-regressed' };
  }

  return nextCompletedUnitCount === previousCompletedUnitCount
    ? { kind: 'stalled' }
    : { kind: 'advanced' };
}

function isValidWarmupCount(value: number) {
  return Number.isSafeInteger(value) && value >= 0;
}

function normalizeCacheScopePath(path: string | null | undefined) {
  let normalizedPath = path?.trim().replaceAll('\\', '/') ?? '';
  while (
    normalizedPath.length > 1 &&
    normalizedPath.endsWith('/') &&
    !/^[A-Za-z]:\/$/u.test(normalizedPath)
  ) {
    normalizedPath = normalizedPath.slice(0, -1);
  }

  return normalizedPath || null;
}
