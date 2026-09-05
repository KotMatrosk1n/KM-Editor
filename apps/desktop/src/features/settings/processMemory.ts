/* SPDX-License-Identifier: GPL-3.0-only */

import { invoke, isTauri } from '@tauri-apps/api/core';
import { useSyncExternalStore } from 'react';

type MemoryGroup = {
  processCount: number;
  unreadableCount: number;
  privateRamBytes: number | null;
  committedBytes: number | null;
};
export type MemorySnapshot = Record<'desktop' | 'workers' | 'webView' | 'total', MemoryGroup> & {
  system: { totalBytes: number; availableBytes: number } | null;
  idleWorkerRetentionSeconds: number;
};
type MemoryState = {
  snapshot: MemorySnapshot | null;
  status: 'loading' | 'ready' | 'unavailable' | 'unsupported';
};
const initial: MemoryState = { snapshot: null, status: 'loading' };
let state = initial;
const listeners = new Set<() => void>();
let timer: ReturnType<typeof setTimeout> | undefined;
let inFlight = false;
let generation = 0;
const visible = () => document.visibilityState !== 'hidden';
const supported = () => state.status !== 'unsupported';
const publish = (next: MemoryState) => {
  state = next;
  listeners.forEach(listener => listener());
};

async function sample() {
  if (!listeners.size || inFlight || !visible() || !supported()) return;
  if (!isTauri()) { publish({ snapshot: null, status: 'unsupported' }); return; }
  inFlight = true;
  const epoch = generation;
  try {
    const snapshot = await invoke<MemorySnapshot>('get_app_memory');
    if (epoch === generation && listeners.size && visible()) publish({ snapshot, status: 'ready' });
  } catch (error) {
    if (epoch === generation && listeners.size) publish({ snapshot: null, status: error === 'unsupported' ? 'unsupported' : 'unavailable' });
  } finally {
    inFlight = false;
    if (listeners.size && visible() && supported()) {
      timer = setTimeout(() => void sample(), epoch === generation ? 5_000 : 0);
    }
  }
}

function visibilityChanged() {
  clearTimeout(timer);
  // Never redisplay a reading made before a hidden interval.
  generation++;
  if (state.status !== 'unsupported') publish(initial);
  if (visible()) void sample();
}

function subscribeMemory(listener: () => void) {
  listeners.add(listener);
  if (listeners.size === 1) {
    document.addEventListener('visibilitychange', visibilityChanged);
    void sample();
  }
  return () => {
    listeners.delete(listener);
    if (!listeners.size) {
      generation++;
      clearTimeout(timer);
      state = initial;
      document.removeEventListener('visibilitychange', visibilityChanged);
    }
  };
}
const subscribeDisabled = () => () => {};
const getSnapshot = () => state;
const getInitial = () => initial;

// The header and Diagnostics share one sampler, including during route changes.
export function useProcessMemory(enabled = true) {
  return useSyncExternalStore(enabled ? subscribeMemory : subscribeDisabled, enabled ? getSnapshot : getInitial, getInitial);
}

export const headerMemoryStorageKey = 'km-editor.header-memory.enabled';
const preferenceEvent = 'km-editor:header-memory-changed';
let sessionPreference: boolean | undefined;
function readPreference() {
  if (sessionPreference !== undefined) return sessionPreference;
  try { return localStorage.getItem(headerMemoryStorageKey) === 'true'; }
  catch { return false; }
}
function subscribePreference(listener: () => void) {
  window.addEventListener('storage', listener);
  window.addEventListener(preferenceEvent, listener);
  return () => {
    window.removeEventListener('storage', listener);
    window.removeEventListener(preferenceEvent, listener);
  };
}
export function useHeaderMemoryEnabled() {
  return useSyncExternalStore(subscribePreference, readPreference, () => false);
}
export function setHeaderMemoryEnabled(enabled: boolean) {
  try {
    localStorage.setItem(headerMemoryStorageKey, String(enabled));
    sessionPreference = undefined;
  } catch { sessionPreference = enabled; }
  window.dispatchEvent(new Event(preferenceEvent));
}

export function formatCompactMemory(bytes: number | null, locale: string, unavailable: string) {
  if (bytes === null) return unavailable;
  const gib = bytes >= 1024 ** 3;
  return `${(bytes / 1024 ** (gib ? 3 : 2)).toLocaleString(locale, { maximumFractionDigits: gib ? 1 : 0 })} ${gib ? 'GiB' : 'MiB'}`;
}
