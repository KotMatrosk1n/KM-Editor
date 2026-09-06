// SPDX-License-Identifier: GPL-3.0-only

import { useState, type CSSProperties, type ReactNode } from 'react';
import catalog from './itemIconCatalog.json';
import './itemIcon.css';

const itemIcons: Readonly<Record<string, string>> = catalog;

// Original inventory symbols cover catalog entries without dedicated artwork.
const symbols: Record<string, ReactNode> = {
  mega: <><circle cx="16" cy="16" r="13"/><path d="M16 3C5 13 23 15 10 28 29 24 27 11 16 3Z" fill="#f4d883" stroke="none"/><path d="M16 3c13 12-4 14 7 23C9 22 6 11 16 3Z" fill="#ea88af" stroke="none"/><path d="m8 9 4-3" fill="none" stroke="#fff" strokeWidth="3" opacity=".6"/></>,
  battery: <><path d="M12 2h8v4h-8Z" fill="#bdcbd8"/><rect x="7" y="5" width="18" height="24" rx="4"/><path d="m17 8-6 10h5l-1 7 7-11h-6Z" fill="#f6e094" stroke="none"/></>,
  shield: <><path d="M16 3 28 8v11c0 5-12 11-12 11S4 24 4 19V8Z"/><path d="m16 7 8 4v7c0 3-8 8-8 8s-8-5-8-8v-7Z" fill="none" stroke="#f4d594" strokeWidth="2"/><path d="m11 16 4 4 6-8" fill="none" stroke="#f4d594" strokeWidth="2"/></>,
  glove: <><path d="M9 20C0 15 6 3 13 3h8c9 0 10 14 3 19l-1 7H10Z"/><path d="M10 23h14v6H10Z" fill="#e8d6bc"/><path d="M10 20c6 0 8-8 2-9-5-1-5 7-2 9Z" fill="none"/></>,
  cloak: <><path d="M11 3h10l9 24-10 3-4-5-4 5-10-3Z"/><path d="m11 3 5 8 5-8M16 11v14" fill="none" stroke="#d2b58c"/><circle cx="16" cy="12" r="2" fill="#e2cf8b"/></>,
  dice: <><rect x="3" y="10" width="17" height="19" rx="3" fill="#efd9b3"/><rect x="14" y="3" width="15" height="17" rx="3"/><g fill="#29323c" stroke="none"><circle cx="8" cy="15" r="1.5"/><circle cx="15" cy="24" r="1.5"/><circle cx="8" cy="24" r="1.5"/><circle cx="19" cy="8" r="1.5"/><circle cx="25" cy="15" r="1.5"/><circle cx="22" cy="11.5" r="1.5"/></g></>,
  mask: <><path d="m3 5 9 4 4-6 4 6 9-4-2 17-11 8L5 22Z"/><path d="m7 12 6 3-5 3Zm18 0-6 3 5 3Z" fill="#f1d99c"/><path d="m12 23 4-3 4 3" fill="none" stroke="#f1d99c" strokeWidth="2"/></>,
  tool: <><path d="M9 3 5 7l3 6 5 1 13 14 4-4-15-13V6l-4-3 1 6-4 1Z" fill="#b5c4cf"/><circle cx="25" cy="24" r="1" fill="#26303c" stroke="none"/></>,
  spark: <path d="m17 2-13 17h10l-2 12L29 12H18l4-10Z" fill="#f6d568"/>,
  disc: <><circle cx="16" cy="16" r="12"/><path d="M8 7 24 25M6 11 21 26" stroke="#ffffff" opacity=".45"/><circle cx="16" cy="16" r="4" fill="#26374c"/></>,
  record: <><circle cx="16" cy="16" r="12" fill="#344359"/><circle cx="16" cy="16" r="8" fill="none"/><circle cx="16" cy="16" r="4"/></>,
  crystal: <><path d="m16 3 10 9-3 14-14 3-6-16Z"/><path d="m16 3-4 11 11 12M3 13l9 1 14-2M12 14 9 29" fill="none" stroke="#ffffff" opacity=".5"/></>,
  berry: <><path d="M16 10C0 2 1 28 13 29c5 2 16-6 16-14 0-8-9-11-13-5Z"/><path d="m15 12-5-8 8 3 7-4-3 8Z" fill="#79bd75"/><circle cx="9" cy="17" r="2" fill="#fff" stroke="none" opacity=".6"/></>,
  candy: <><path d="m4 10 7 3v7l-7 3 1-7Z M28 10l-7 3v7l7 3-1-7Z" fill="#f6d9a5"/><rect x="9" y="8" width="14" height="17" rx="6"/><path d="m12 13 7-2" stroke="#fff" opacity=".6"/></>,
  feather: <><path d="M7 25C0 7 20 0 27 4c0 16-8 26-20 21Z"/><path d="m5 29 18-21M11 22l-1-8M15 18l8-1M18 14l-1-6" fill="none"/></>,
  fang: <><path d="M5 4h15c8 12-4 24-16 25 10-8 9-14 1-25Z" fill="#f3e2b7"/><path d="m5 4 3 6 15 1-3-7Z"/></>,
  fur: <path d="m5 24-2-8 6 2-1-9 7 4 3-10 4 10 6-4-2 12 3 4-11 4Z"/>,
  drop: <><path d="M16 3C14 10 4 15 4 22a12 9 0 0 0 24 0c0-7-10-12-12-19Z"/><path d="M10 18q-4 7 3 8" fill="none" stroke="#fff" opacity=".6"/></>,
  leaf: <><path d="M6 26C0 9 16 1 28 4 29 22 17 31 6 26Z" fill="#7dc878"/><path d="M3 29 23 9M11 21l-1-7M16 16l8 1" fill="none"/></>,
  powder: <><path d="m5 10 5-6h12l5 6-2 19H7Z" fill="#ead3a5"/><path d="M5 10h22M10 5l3 5M22 5l-3 5" fill="none"/><path d="m10 24 6-10 6 10Z" stroke="none"/></>,
  thread: <><rect x="10" y="5" width="12" height="23" rx="2" fill="#eac79b"/><path d="M8 8h16v17H8Z"/><path d="m8 11 16 3-16 3 16 3-16 3" fill="none" stroke="#fff" opacity=".5"/><path d="M24 21q7 0 4 9" fill="none"/></>,
  coin: <><ellipse cx="16" cy="16" rx="11" ry="13" fill="#eac35f"/><ellipse cx="16" cy="16" rx="7" ry="9" fill="none"/><path d="m16 9 2 4 4 1-3 3 1 5-4-2-4 2 1-5-3-3 4-1Z" fill="#fff0a9" stroke="none"/></>,
  bottle: <><rect x="11" y="3" width="10" height="5" rx="1" fill="#cfdae5"/><path d="M11 8 7 13v14q9 4 18 0V13l-4-5Z"/><path d="M7 16h18v8H7Z" fill="#f2e7cf"/><path d="M11 11v4" stroke="#fff" opacity=".6"/></>,
  cloth: <><path d="m5 5 20-1 4 22-7-1-4 4-7-3-7 1Z"/><path d="m11 5 2 20M21 5l1 20M5 12h21M5 20h22" fill="none" stroke="#fff" opacity=".5"/></>,
  chair: <><path d="M8 16V4h17v12Z"/><path d="M5 16h23v5H5Z" fill="#e3c595"/><path d="M8 21v8M25 21v8" fill="none" strokeWidth="3"/></>,
  pick: <><path d="m12 17 2 13 3-13" fill="#e2cca0"/><path d="m16 2 3 6 7 1-5 5 1 7-6-3-6 3 1-7-5-5 7-1Z"/></>,
  book: <><path d="M5 5 25 3v23L5 29Z"/><path d="m5 25 20-3v4L5 29Z" fill="#f2e7cf"/><path d="M9 5v19M13 10l8-1M13 14l8-1" fill="none" stroke="#f2e7cf"/></>,
  plush: <><circle cx="8" cy="7" r="5"/><circle cx="24" cy="7" r="5"/><ellipse cx="16" cy="21" rx="10" ry="9"/><circle cx="16" cy="12" r="10"/><ellipse cx="16" cy="16" rx="5" ry="3" fill="#f5dfc3"/><path d="M11 11h1M20 11h1M15 15h2" fill="none" strokeWidth="2.5"/></>,
  bread: <><path d="M6 13C-1 3 11 0 16 5 22 0 34 5 26 13v16H6Z" fill="#dfa85f"/><path d="M10 13C5 6 12 5 16 9c5-4 11-1 6 4v12H10Z" fill="#f4da99" stroke="none"/></>,
  fruit: <><path d="M16 10C1 2 1 24 12 29h8C31 24 31 2 16 10Z"/><path d="M16 10V4q7-3 10 1-4 6-10 3" fill="#79bd75"/><path d="M9 14q-3 3-1 7" fill="none" stroke="#fff" opacity=".6"/></>,
  jar: <><path d="M7 7h18v21q-9 4-18 0Z"/><rect x="6" y="3" width="20" height="6" rx="2" fill="#d4bf8c"/><path d="M7 15h18v9H7Z" fill="#f4e4c4"/><circle cx="16" cy="19" r="3"/></>,
  meat: <><path d="M7 9C17-1 31 7 28 18s-13 16-22 9C-1 22 2 15 7 9Z" fill="#ce7276"/><path d="M9 12c8-8 20-1 15 9s-23 2-15-9Z" fill="#f0aaa0"/><ellipse cx="19" cy="15" rx="4" ry="3" fill="#f7e5c7"/></>,
  food: <><ellipse cx="16" cy="24" rx="14" ry="5" fill="#dbe3eb"/><path d="M5 22C5 8 12 12 16 8c6 5 10 1 11 14Z" fill="#e7bd76"/><path d="m10 16 2-1m7 1 3 1m-7 3 2 1" stroke="#88a064" strokeWidth="3"/></>,
  ball: <><circle cx="16" cy="16" r="13" fill="#e7edf2"/><path d="M3 16a13 13 0 0 1 26 0Z"/><path d="M3 16h26"/><circle cx="16" cy="16" r="4" fill="#f0f5fa"/></>,
  stone: <><path d="m10 4 14 2 5 14-10 9-14-5-2-12Z"/><path d="m9 9 9-1 6 10-6 6-9-3Z" fill="#eef3ed" opacity=".5" stroke="none"/><path d="m17 10-4 7 7 4-3-11Z" fill="#dc739f" stroke="none"/></>,
  lens: <><circle cx="13" cy="13" r="10" fill="#9cd9e8"/><circle cx="13" cy="13" r="7" fill="none"/><path d="m21 21 8 8" strokeWidth="5"/><path d="m8 13 5-6" stroke="#fff" opacity=".6"/></>,
  ring: <><ellipse cx="16" cy="19" rx="11" ry="9" fill="none" stroke="#e5bd66" strokeWidth="5"/><path d="m16 2 7 6-7 9-7-9Z"/></>,
  bag: <><path d="m10 4 6 3 6-3-2 9c14 13 6 17-4 17S-2 26 12 13Z"/><path d="M11 13h10M13 16l-2 7" fill="none"/><circle cx="18" cy="24" r="3" fill="#f0d6a2"/></>,
  parcel: <><path d="m4 7 12-4 12 4v21H4Z" fill="#d7b77f"/><path d="M4 7h24M13 7v7h6V7" fill="#f3dcaf"/></>
};

export function getItemIconEntry(itemId: number): string | null {
  return Number.isSafeInteger(itemId) && itemId > 0 ? itemIcons[itemId] ?? '@parcel' : null;
}

function ItemSymbol({ itemId, kind, tint }: { itemId: number; kind: string; tint?: string }) {
  const shape = symbols[kind] ?? symbols.parcel;
  const color = tint && /^[a-f0-9]{6}$/.test(tint) ? `#${tint}` : `hsl(${(itemId * 47) % 360} 55% 65%)`;
  return <svg aria-hidden="true" viewBox="0 0 32 32" className="item-icon-symbol"
    style={{ '--item-symbol-color': color } as CSSProperties}>
    <g fill="var(--item-symbol-color)" stroke="#29323c" strokeWidth="1.25" strokeLinejoin="round" strokeLinecap="round">{shape}</g>
    {kind === 'parcel' ? <text x="16" y="24" textAnchor="middle" fill="#26303c" fontSize={itemId > 9999 ? 6 : 8} fontWeight="800">{itemId}</text> : null}
  </svg>;
}

function ItemArtwork({ asset, itemId }: { asset: string; itemId: number }) {
  const [failed, setFailed] = useState(false);
  const [pixelArt, setPixelArt] = useState(false);
  return failed ? <ItemSymbol itemId={itemId} kind="parcel"/> : <img alt="" draggable={false}
    className="item-icon-artwork" width={32} height={32} data-pixel-art={pixelArt}
    onLoad={(event) => setPixelArt(event.currentTarget.naturalWidth <= 40)}
    src={`${import.meta.env.BASE_URL}item-icons/${asset}.png`} onError={() => setFailed(true)}/>;
}

export function ItemIcon({ itemId }: { itemId: number }) {
  const entry = getItemIconEntry(itemId);
  if (!entry) return null;
  const [kind, tint] = entry.slice(1).split(':');
  return <span aria-hidden="true" className="item-icon" data-item-id={itemId}>
    {entry.startsWith('@') ? <ItemSymbol itemId={itemId} kind={kind} tint={tint}/> :
      <ItemArtwork key={entry} asset={entry} itemId={itemId}/>}
  </span>;
}
