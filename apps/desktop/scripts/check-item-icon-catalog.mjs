// SPDX-License-Identifier: GPL-3.0-only

import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';

const catalog = JSON.parse(readFileSync(new URL('../src/features/items/itemIconCatalog.json', import.meta.url), 'utf8'));
const root = new URL('../public/item-icons/', import.meta.url);
const assets = new Set();
const component = readFileSync(new URL('../src/features/items/ItemIcon.tsx', import.meta.url), 'utf8');
for (const [id, entry] of Object.entries(catalog)) {
  assert.ok(/^[1-9]\d*$/.test(id), 'Item icons must use positive item IDs.');
  if (entry.startsWith('@')) {
    const [kind, tint] = entry.slice(1).split(':');
    assert.ok(component.includes(`  ${kind}:`), `Missing item symbol for ${id}.`);
    assert.ok(tint === undefined || /^[a-f0-9]{6}$/.test(tint), `Invalid item symbol tint for ${id}.`);
    continue;
  }
  assert.ok(/^item-\d+$/.test(entry), `Invalid item artwork name for ${id}.`);
  assets.add(`${entry}.png`);
  const bytes = readFileSync(new URL(`${entry}.png`, root));
  assert.equal(bytes.subarray(0, 8).toString('hex'), '89504e470d0a1a0a', `Invalid item PNG for ${id}.`);
  assert.ok(bytes.readUInt32BE(16) > 0 && bytes.readUInt32BE(20) > 0, `Empty item artwork for ${id}.`);
}
assert.deepEqual(new Set(readdirSync(root).filter(name => name.endsWith('.png'))), assets,
  'Bundled item artwork must exactly match the item icon catalog.');
console.log('Item icon catalog and bundled artwork contract passed.');
