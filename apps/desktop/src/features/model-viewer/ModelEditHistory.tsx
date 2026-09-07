/* SPDX-License-Identifier: GPL-3.0-only */
import { createContext, useContext, useRef, useState, useSyncExternalStore, type SetStateAction } from 'react';

type Entry = { key: string; label: string; undo: () => void; redo: () => void; time: number };
export class ModelEditHistory {
  private past: Entry[] = [];
  private future: Entry[] = [];
  private listeners = new Set<() => void>();
  private revision = 0;
  locked = false;
  lock(value: boolean) { this.locked = value; this.notify(); }
  subscribe = (listener: () => void) => { this.listeners.add(listener); return () => { this.listeners.delete(listener); }; };
  snapshot = () => this.revision;
  private notify() { this.revision++; this.listeners.forEach(listener => listener()); }
  record(key: string, label: string, undo: () => void, redo: () => void) {
    const time = Date.now(), previous = this.past.at(-1);
    if (!this.future.length && previous?.key === key && time - previous.time < 600) {
      previous.redo = redo; previous.time = time;
    } else this.past = [...this.past.slice(-99), { key, label, undo, redo, time }];
    this.future = []; this.notify();
  }
  undo = () => { if (this.locked) return; const entry = this.past.pop(); if (entry) { entry.undo(); this.future.push(entry); this.notify(); } };
  redo = () => { if (this.locked) return; const entry = this.future.pop(); if (entry) { entry.redo(); this.past.push({ ...entry, time: 0 }); this.notify(); } };
  clear = () => { this.past = []; this.future = []; this.notify(); };
  get canUndo() { return !this.locked && this.past.length > 0; }
  get canRedo() { return !this.locked && this.future.length > 0; }
  get entries() { return this.past.map(entry => entry.label); }
}
export const ModelHistoryContext = createContext<ModelEditHistory | null>(null);
export function useModelHistory() {
  const history = useContext(ModelHistoryContext);
  useSyncExternalStore(history?.subscribe ?? (() => () => {}), history?.snapshot ?? (() => 0));
  return history;
}
export function useModelDraft<T>(initial: T, scope: string) {
  const history = useContext(ModelHistoryContext);
  const [state, setState] = useState(initial);
  const current = useRef(state);
  const apply = (value: T) => { current.current = value; setState(value); };
  const edit = (update: SetStateAction<T>, label: string, group = scope) => {
    if (history?.locked) return;
    const previous = current.current;
    const next = typeof update === 'function' ? (update as (value: T) => T)(previous) : update;
    if (JSON.stringify(previous) === JSON.stringify(next)) return;
    history?.record(group, label, () => apply(previous), () => apply(next));
    apply(next);
  };
  return [state, edit, apply] as const;
}
