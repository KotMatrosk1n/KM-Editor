/* SPDX-License-Identifier: GPL-3.0-only */
import { invoke } from '@tauri-apps/api/core';
import { listen } from '@tauri-apps/api/event';
import { useEffect, useRef, useState } from 'react';
import { z } from 'zod';
import type { ProjectPaths } from '../../bridge/contracts';
import { ProjectBridgeError } from '../../bridge/projectBridgeError';

const infoSchema = z.object({
  adapter: z.string(), backend: z.literal('DX12'), selection: z.literal('Auto'),
  clips: z.array(z.string()).max(2048), clip: z.string().nullable(), duration: z.number().nonnegative(),
  looped: z.boolean(), warnings: z.array(z.string())
});
export function modelError(cause: unknown): string {
  if (cause instanceof ProjectBridgeError && cause.semanticCode) return cause.semanticCode;
  if (typeof cause === 'string' && /^KM(?:-[A-Z0-9]+)+$/.test(cause)) return cause;
  if (cause && typeof cause === 'object' && 'code' in cause && typeof cause.code === 'string' && /^KM(?:-[A-Z0-9]+)+$/.test(cause.code)) return cause.code;
  return 'KM-MODEL-UNSUPPORTED';
}
export type ModelBackground = { color: string; grid: boolean };
export function useModelViewport(paths: ProjectPaths, id: string, animation: string | null, revision: number, covered: boolean, background: ModelBackground) {
  const viewport = useRef<HTMLDivElement>(null);
  const session = useRef<string | null>(null);
  const queue = useRef<Promise<unknown>>(Promise.resolve());
  const [info, setInfo] = useState<z.infer<typeof infoSchema> | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [position, setPosition] = useState(0);
  const [playing, setPlaying] = useState(false);
  const coveredRef = useRef(covered); coveredRef.current = covered;
  const backgroundRef = useRef(background); backgroundRef.current = background;
  const sync = useRef<() => void>(() => {});
  const pathKey = JSON.stringify(paths);
  useEffect(() => {
    setInfo(null); setError(null); setPosition(0); setPlaying(false);
    if (!id) { setLoading(false); return; }
    const active = crypto.randomUUID(); session.current = active;
    setLoading(true);
    let live = true;
    let lastBounds = '';
    let frame = 0;
    let scrolling = false;
    let settle = 0;
    let sending = false;
    let pending = false;
    let ready = false;
    const bounds = () => {
      const rect = viewport.current?.getBoundingClientRect();
      const ratio = window.devicePixelRatio;
      const unobstructed = !coveredRef.current && !document.querySelector('[role="dialog"], [role="alertdialog"], [role="listbox"], dialog[open], .sidebar-overlay-open');
      let left = Math.max(0, rect?.left ?? 0), top = Math.max(0, rect?.top ?? 0);
      let right = Math.min(window.innerWidth, rect?.right ?? 0), bottom = Math.min(window.innerHeight, rect?.bottom ?? 0);
      for (let parent = viewport.current?.parentElement; parent; parent = parent.parentElement) {
        const style = getComputedStyle(parent), area = parent.getBoundingClientRect();
        if (/auto|scroll|hidden|clip/.test(style.overflowX)) { left = Math.max(left, area.left + parent.clientLeft); right = Math.min(right, area.left + parent.clientLeft + parent.clientWidth); }
        if (/auto|scroll|hidden|clip/.test(style.overflowY)) { top = Math.max(top, area.top + parent.clientTop); bottom = Math.min(bottom, area.top + parent.clientTop + parent.clientHeight); }
      }
      return {
        x: Math.round((rect?.x ?? 0) * ratio), y: Math.round((rect?.y ?? 0) * ratio),
        width: Math.min(4096, Math.max(0, Math.round((rect?.width ?? 0) * ratio))),
        height: Math.min(4096, Math.max(0, Math.round((rect?.height ?? 0) * ratio))),
        clip: { x: Math.max(0, Math.round((left - (rect?.left ?? 0)) * ratio)), y: Math.max(0, Math.round((top - (rect?.top ?? 0)) * ratio)),
          width: Math.max(0, Math.round((right - left) * ratio)), height: Math.max(0, Math.round((bottom - top) * ratio)) },
        visible: !!rect && !document.hidden && !scrolling && unobstructed && right > left && bottom > top,
        background: [1, 3, 5].map(offset => parseInt(backgroundRef.current.color.slice(offset, offset + 2), 16)),
        grid: backgroundRef.current.grid
      };
    };
    // Coalesce bounds while IPC is in flight so an older position cannot arrive last.
    const send = async () => {
      if (!ready || !live) return;
      pending = true;
      if (sending) return;
      sending = true;
      try {
        while (live && pending) {
          pending = false;
          const current = bounds(), key = JSON.stringify(current);
          if (key === lastBounds) continue;
          await invoke('model_preview_viewport', { session: active, viewport: current });
          lastBounds = key;
        }
      } catch { lastBounds = ''; }
      finally { sending = false; }
    };
    const update = () => {
      cancelAnimationFrame(frame);
      frame = requestAnimationFrame(() => {
        if (!live) return;
        void send();
      });
    };
    const onScroll = (event: Event) => {
      const target = event.target;
      if (target instanceof Node && target !== document && !target.contains(viewport.current)) return;
      // A native child cannot participate in WebView compositor scrolling. Hide it
      // until the page settles, then restore at the final clipped position.
      scrolling = true;
      void send();
      window.clearTimeout(settle);
      settle = window.setTimeout(() => { scrolling = false; update(); }, 100);
    };
    sync.current = update;
    const observer = new ResizeObserver(update);
    if (viewport.current) observer.observe(viewport.current);
    const mutations = new MutationObserver(update); mutations.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['class', 'hidden', 'open'] });
    window.addEventListener('resize', update); window.addEventListener('scroll', onScroll, true);
    document.addEventListener('visibilitychange', update);
    const playback = listen<{ session: string; position: number; playing: boolean }>('model-preview-playback', event => {
      if (live && event.payload.session === active) { setPosition(event.payload.position); setPlaying(event.payload.playing); }
    });
    const status = listen<{ session: string; error: string | null; closed?: boolean }>('model-preview-status', event => {
      if (live && event.payload.session === active) {
        if (event.payload.error) setError(event.payload.error);
        if (event.payload.closed || event.payload.error) setInfo(null);
      }
    });
    const focus = listen<{ session: string }>('model-preview-return-focus', event => {
      if (live && event.payload.session === active)
        viewport.current?.parentElement?.querySelector<HTMLElement>('#model-animation, button:not(:disabled)')?.focus({ preventScroll: true });
    });
    const activated = invoke('model_preview_activate', { session: active });
    const previous = queue.current;
    queue.current = (async () => {
      try {
        await activated; await previous.catch(() => {});
        if (!live) return;
        ready = true;
        await send();
        const result = infoSchema.parse(await invoke('model_preview_open', {
          paths: JSON.parse(pathKey), id, animation, title: '3D Model Viewer', session: active
        }));
        if (live) { setInfo(result); lastBounds = ''; update(); }
      } catch (cause) { if (live && modelError(cause) !== 'KM-MODEL-CANCELLED') setError(modelError(cause)); }
      finally { if (live) setLoading(false); }
    })();
    return () => {
      live = false; cancelAnimationFrame(frame); window.clearTimeout(settle); observer.disconnect(); mutations.disconnect();
      window.removeEventListener('resize', update); window.removeEventListener('scroll', onScroll, true);
      document.removeEventListener('visibilitychange', update);
      void playback.then(unlisten => unlisten()).catch(() => {}); void status.then(unlisten => unlisten()).catch(() => {});
      void focus.then(unlisten => unlisten()).catch(() => {});
      if (session.current === active) session.current = null;
      void invoke('model_preview_close', { session: active }).catch(() => {});
    };
  }, [pathKey, id, animation, revision]);
  useEffect(() => { sync.current(); }, [covered, background.color, background.grid]);
  async function camera(action: string) { if (session.current) await invoke('model_preview_camera', { session: session.current, action }); }
  async function playback(action: string, value = 0) {
    if (!session.current) return;
    await invoke('model_preview_playback', { session: session.current, action, value });
    if (action === 'seek') setPosition(value);
    if (action === 'play' || action === 'restart') setPlaying(true);
    if (action === 'pause') setPlaying(false);
  }
  return { viewport, info, loading, error, position, playing, camera, playback };
}
