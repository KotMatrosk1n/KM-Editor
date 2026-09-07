/* SPDX-License-Identifier: GPL-3.0-only */
import { invoke } from '@tauri-apps/api/core';
import { listen } from '@tauri-apps/api/event';
import { useEffect, useRef, useState } from 'react';
import { z } from 'zod';
import type { ProjectPaths } from '../../bridge/contracts';
import { ProjectBridgeError } from '../../bridge/projectBridgeError';
import type { AssetChange, TextureChange } from './modelTextureBridge';
import { useModelResolution } from './ModelResolutionSettings';
import { defaultModelLight, type ModelLight } from './ModelLightControls';

const infoSchema = z.object({
  adapter: z.string(), backend: z.literal('DX12'), selection: z.literal('Auto'),
  clips: z.array(z.string()).max(2048), clip: z.string().nullable(), duration: z.number().nonnegative(),
  looped: z.boolean(), warnings: z.array(z.string())
  ,frameRate: z.number().int().positive().default(30), frames: z.number().int().positive().default(1),
  parts: z.array(z.object({ id: z.number().int(), name: z.string(), material: z.string(), triangles: z.number().int() })).default([]),
  textures: z.array(z.tuple([z.number(), z.number()])).default([]), textureBytes: z.number().default(0)
});
export function modelError(cause: unknown): string {
  if (cause instanceof ProjectBridgeError && cause.semanticCode) return cause.semanticCode;
  if (typeof cause === 'string' && /^KM(?:-[A-Z0-9]+)+$/.test(cause)) return cause;
  if (cause && typeof cause === 'object' && 'code' in cause && typeof cause.code === 'string' && /^KM(?:-[A-Z0-9]+)+$/.test(cause.code)) return cause.code;
  return 'KM-MODEL-UNSUPPORTED';
}
export type ModelBackground = { color: string; grid: boolean };
export type ModelViewOptions = { display: number; wireframe: boolean; hidden: number[]; selected: number | null; statistics?: boolean; inGame?: boolean };
export function useModelViewport(paths: ProjectPaths, id: string, animation: string | null, revision: number, covered: boolean, background: ModelBackground, textures: TextureChange[] = [], assets: AssetChange[] = [], light: ModelLight = defaultModelLight, options: ModelViewOptions = { display: 0, wireframe: false, hidden: [], selected: null }, onSelect?: (part: number | null) => void, onHistory?: (redo: boolean) => void) {
  const viewport = useRef<HTMLDivElement>(null);
  const session = useRef<string | null>(null);
  const queue = useRef<Promise<unknown>>(Promise.resolve());
  const [info, setInfo] = useState<z.infer<typeof infoSchema> | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [position, setPosition] = useState(0);
  const [playing, setPlaying] = useState(false);
  const [frameMs, setFrameMs] = useState(0);
  const [stats, setStats] = useState({ fps: 0, yaw: .35, pitch: .15 });
  const optionsRef = useRef(options); optionsRef.current = options;
  const selectRef = useRef(onSelect); selectRef.current = onSelect;
  const historyRef = useRef(onHistory); historyRef.current = onHistory;
  const coveredRef = useRef(covered); coveredRef.current = covered;
  const backgroundRef = useRef(background); backgroundRef.current = background;
  const lightRef = useRef(light); lightRef.current = light;
  const sync = useRef<() => void>(() => {});
  const pathKey = JSON.stringify(paths);
  const resolution = useModelResolution();
  const textureKey = JSON.stringify({ textures, assets, animation, resolution });
  const textureRef = useRef(textureKey); textureRef.current = textureKey;
  const loadedTextures = useRef('');
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
      let left = Math.max(0, rect?.left ?? 0), top = Math.max(0, rect?.top ?? 0);
      let right = Math.min(window.innerWidth, rect?.right ?? 0), bottom = Math.min(window.innerHeight, rect?.bottom ?? 0);
      for (let parent = viewport.current?.parentElement; parent; parent = parent.parentElement) {
        const style = getComputedStyle(parent), area = parent.getBoundingClientRect();
        if (/auto|scroll|hidden|clip/.test(style.overflowX)) { left = Math.max(left, area.left + parent.clientLeft); right = Math.min(right, area.left + parent.clientLeft + parent.clientWidth); }
        if (/auto|scroll|hidden|clip/.test(style.overflowY)) { top = Math.max(top, area.top + parent.clientTop); bottom = Math.min(bottom, area.top + parent.clientTop + parent.clientHeight); }
        if (parent.classList.contains('model-viewer--editing')) break;
      }
      const unobstructed = !coveredRef.current && !Array.from(document.querySelectorAll('[role="dialog"], [role="alertdialog"], [role="listbox"], dialog[open], .sidebar-overlay-open'))
        .some(element => {
          if (element.contains(viewport.current) || element.closest('[hidden], [inert]')) return false;
          const style = getComputedStyle(element), area = element.getBoundingClientRect();
          return style.visibility !== 'hidden' && area.width > 0 && area.height > 0
            && area.left < right && area.right > left && area.top < bottom && area.bottom > top;
        });
      return {
        x: Math.round((rect?.x ?? 0) * ratio), y: Math.round((rect?.y ?? 0) * ratio),
        width: Math.min(4096, Math.max(0, Math.round((rect?.width ?? 0) * ratio))),
        height: Math.min(4096, Math.max(0, Math.round((rect?.height ?? 0) * ratio))),
        clip: { x: Math.max(0, Math.round((left - (rect?.left ?? 0)) * ratio)), y: Math.max(0, Math.round((top - (rect?.top ?? 0)) * ratio)),
          width: Math.max(0, Math.round((right - left) * ratio)), height: Math.max(0, Math.round((bottom - top) * ratio)) },
        visible: !!rect && !document.hidden && !scrolling && unobstructed && right > left && bottom > top,
        background: [1, 3, 5].map(offset => parseInt(backgroundRef.current.color.slice(offset, offset + 2), 16)),
        grid: backgroundRef.current.grid,
        light: lightRef.current, ...optionsRef.current
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
    const playback = listen<{ session: string; position: number; playing: boolean; frameMs?: number; stats?: { fps: number; yaw: number; pitch: number } }>('model-preview-playback', event => {
      if (live && event.payload.session === active) { setPosition(event.payload.position); setPlaying(event.payload.playing); setFrameMs(event.payload.frameMs ?? 0); if (event.payload.stats) setStats(event.payload.stats); }
    });
    const selection = listen<{ session: string; part: number | null }>('model-preview-selection', event => {
      if (live && event.payload.session === active) selectRef.current?.(event.payload.part);
    });
    const history = listen<{ session: string; redo: boolean }>('model-preview-history', event => {
      if (live && event.payload.session === active) historyRef.current?.(event.payload.redo);
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
        const openingTextures = textureRef.current;
        const result = infoSchema.parse(await invoke('model_preview_open', {
          paths: JSON.parse(pathKey), id, animation: JSON.parse(openingTextures).animation, resolution: JSON.parse(openingTextures).resolution, title: '3D Model Editor', session: active,
          textureChanges: JSON.parse(openingTextures).textures, assetChanges: JSON.parse(openingTextures).assets
        }));
        loadedTextures.current = openingTextures;
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
      void selection.then(unlisten => unlisten()).catch(() => {}); void history.then(unlisten => unlisten()).catch(() => {});
      if (session.current === active) session.current = null;
      void invoke('model_preview_close', { session: active }).catch(() => {});
    };
  }, [pathKey, id, revision]);
  useEffect(() => {
    const active = session.current;
    if (!active || !info || loadedTextures.current === textureKey) return;
    let live = true;
    const previous = queue.current;
    queue.current = (async () => {
      await previous.catch(() => {});
      if (!live || session.current !== active) return;
      try {
        const result = infoSchema.parse(await invoke('model_preview_open', { paths: JSON.parse(pathKey), id, animation, resolution,
          title: '3D Model Editor', session: active, textureChanges: JSON.parse(textureKey).textures, assetChanges: JSON.parse(textureKey).assets }));
        if (live) { loadedTextures.current = textureKey; setInfo(result); setError(null); if (info.clip !== result.clip) { setPosition(0); setPlaying(false); } }
      } catch (cause) { if (live) setError(modelError(cause)); }
    })();
    return () => { live = false; };
  }, [textureKey, info, pathKey, id, animation, resolution]);
  const optionsKey = JSON.stringify(options);
  useEffect(() => { sync.current(); }, [covered, background.color, background.grid, light, optionsKey]);
  async function inspect(part: number) {
    return z.object({ vertices: z.array(z.tuple([z.number(), z.number()])), indices: z.array(z.number().int()) })
      .parse(await invoke('model_preview_inspect', { session: session.current, part }));
  }
  async function camera(action: string) { if (session.current) await invoke('model_preview_camera', { session: session.current, action }); }
  async function playback(action: string, value = 0) {
    if (!session.current) return;
    await invoke('model_preview_playback', { session: session.current, action, value });
    if (action === 'seek') setPosition(value);
    if (action === 'play' || action === 'restart') setPlaying(true);
    if (action === 'pause') setPlaying(false);
  }
  return { viewport, info, loading, error, position, playing, frameMs, stats, camera, playback, inspect };
}
