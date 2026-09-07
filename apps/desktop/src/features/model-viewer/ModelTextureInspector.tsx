/* SPDX-License-Identifier: GPL-3.0-only */
import { useEffect, useRef, useState } from 'react';
import type { ProjectPaths } from '../../bridge/contracts';
import { useLocalization } from '../../localization';
import { SearchableOptionInput } from '../../components/SearchableOptionInput';
import { usePublishCommonEditorDiagnostics } from '../../components/CommonEditorDiagnostics';
import { loadTextureImage, type ModelTexture, type TextureRule } from './modelTextureBridge';

export type ModelUv = { vertices: [number, number][]; indices: number[] };
export function ModelTextureInspector({ paths, model, texture, changes, uv, onClose }: {
  paths: ProjectPaths; model: string; texture: ModelTexture; changes: TextureRule[]; uv: ModelUv[]; onClose: () => void;
}) {
  const { t } = useLocalization();
  const canvas = useRef<HTMLCanvasElement>(null), area = useRef<HTMLDivElement>(null);
  const [pixels, setPixels] = useState<Uint8ClampedArray | null>(null);
  const [error, setError] = useState(false), [channel, setChannel] = useState('rgba'), [showUv, setShowUv] = useState(false);
  usePublishCommonEditorDiagnostics(error ? [{ code: 'KM-MODEL-TEXTURE-EDIT-INVALID', domain: 'workflow.modelTextures', severity: 'error', message: t('modelViewer.texture.error') }] : []);
  const [zoom, setZoom] = useState(1), [pan, setPan] = useState({ x: 0, y: 0 });
  const drag = useRef<{ x: number; y: number; pan: { x: number; y: number } } | null>(null);
  const fitted = useRef('');
  const pathKey = JSON.stringify(paths), changesKey = JSON.stringify(changes);
  const fit = () => { const bounds = area.current?.getBoundingClientRect(); if (bounds) setZoom(Math.min((bounds.width - 32)/texture.width, (bounds.height - 32)/texture.height)); setPan({ x: 0, y: 0 }); };
  useEffect(() => {
    let live = true; setPixels(null); setError(false);
    void loadTextureImage(JSON.parse(pathKey), model, texture, JSON.parse(changesKey)).then(image => {
      if (!live) return;
      const data = Uint8ClampedArray.from(atob(image.pixels), c => c.charCodeAt(0));
      if (image.width !== texture.width || image.height !== texture.height || data.length !== image.width * image.height * 4) throw Error('texture');
      setPixels(data); if (fitted.current !== texture.id) { fitted.current = texture.id; fit(); }
    }).catch(() => { if (live) setError(true); });
    return () => { live = false; };
  }, [pathKey, model, texture, changesKey]);
  useEffect(() => {
    if (!pixels) return;
    const context = canvas.current?.getContext('2d'); if (!context) return;
    const data = new Uint8ClampedArray(pixels);
    if (channel !== 'rgba') for (let i = 0; i < data.length; i += 4) {
      if (channel !== 'rgb') data.fill(pixels[i + 'rgba'.indexOf(channel)], i, i + 3);
      data[i + 3] = 255;
    }
    context.putImageData(new ImageData(data, texture.width, texture.height), 0, 0);
  }, [pixels, channel, texture]);
  const exportImage = () => {
    if (!pixels) return;
    const image = document.createElement('canvas'); image.width = texture.width; image.height = texture.height;
    image.getContext('2d')?.putImageData(new ImageData(new Uint8ClampedArray(pixels), texture.width, texture.height), 0, 0);
    image.toBlob(blob => { if (!blob) return; const url = URL.createObjectURL(blob); const link = document.createElement('a'); link.href = url;
      link.download = (texture.id.split('/').at(-1) ?? 'texture').replace(/\.[^.]+$/, '') + '.png'; link.click(); setTimeout(() => URL.revokeObjectURL(url), 1000); }, 'image/png');
  };
  return <section className="model-texture-inspector" aria-label={t('modelWorkspace.inspectTexture')}>
    <div className="model-workspace__bar">
      <button type="button" onClick={onClose}>{t('modelWorkspace.backToModel')}</button>
      <strong data-localization-ignore="true">{texture.id.split('/').at(-1)}</strong>
      <button type="button" onClick={fit}>{t('modelWorkspace.fitTexture')}</button>
      <button type="button" onClick={() => { setZoom(1); setPan({ x: 0, y: 0 }); }}>100%</button>
      <SearchableOptionInput id="model-image-channel" ariaLabel={t('modelWorkspace.channel')} disabled={false} isFiniteCatalog localizeOptions={false} value={channel} onChange={setChannel}
        options={['rgba', 'rgb', 'r', 'g', 'b', 'a'].map(value => ({ value, label: value.toUpperCase() }))} />
      <label><input type="checkbox" checked={showUv} disabled={!uv.length} onChange={event => setShowUv(event.target.checked)} />{t('modelWorkspace.uvOverlay')}</label>
      <button type="button" disabled={!pixels} onClick={exportImage}>{t('modelWorkspace.exportTexture')}</button>
    </div>
    <div className="model-texture-inspector__area" ref={area} tabIndex={0} aria-label={t('modelWorkspace.textureNavigation')}
      onWheel={event => { setZoom(old => Math.max(.02, Math.min(32, old * Math.exp(-event.deltaY * .001)))); }}
      onPointerDown={event => { event.currentTarget.setPointerCapture(event.pointerId); drag.current = { x: event.clientX, y: event.clientY, pan }; }}
      onPointerMove={event => { if (drag.current) setPan({ x: drag.current.pan.x + event.clientX - drag.current.x, y: drag.current.pan.y + event.clientY - drag.current.y }); }}
      onPointerUp={() => { drag.current = null; }} onPointerCancel={() => { drag.current = null; }}
      onKeyDown={event => { if (event.key === '0') fit(); if (event.key === '+' || event.key === '=') setZoom(z => Math.min(32,z*1.2)); if (event.key === '-') setZoom(z => Math.max(.02,z/1.2));
        if (event.key.startsWith('Arrow')) { event.preventDefault(); setPan(p => ({ x: p.x + (event.key === 'ArrowRight' ? 20 : event.key === 'ArrowLeft' ? -20 : 0), y: p.y + (event.key === 'ArrowDown' ? 20 : event.key === 'ArrowUp' ? -20 : 0) })); } }}>
      {error ? <p role="alert">{t('modelViewer.texture.error')}</p> : !pixels ? <p role="status">{t('modelViewer.loading')}</p> : null}
      <div className="model-texture-inspector__image" style={{ width: texture.width, height: texture.height, transform: `translate(-50%, -50%) translate(${pan.x}px, ${pan.y}px) scale(${zoom})` }}>
        <canvas ref={canvas} width={texture.width} height={texture.height} />
        {showUv ? <svg viewBox="0 0 1 1" preserveAspectRatio="none" aria-hidden="true">{uv.map((mesh, index) => <path key={index} fill="none" stroke="#ffcd48" vectorEffect="non-scaling-stroke" strokeWidth="1"
          d={mesh.indices.reduce((segments: string[], _, i) => { if (i%3 === 0) { const a = mesh.vertices[mesh.indices[i]], b = mesh.vertices[mesh.indices[i+1]], c = mesh.vertices[mesh.indices[i+2]];
            if (a && b && c) segments.push(`M${a[0]},${a[1]}L${b[0]},${b[1]}L${c[0]},${c[1]}Z`); } return segments; }, []).join('')} />)}</svg> : null}
      </div>
    </div>
    <small>{t('modelWorkspace.textureNavigation')} · {texture.width} × {texture.height} · {Math.round(zoom * 100)}%</small>
  </section>;
}
