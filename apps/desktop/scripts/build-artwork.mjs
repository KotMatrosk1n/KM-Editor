// SPDX-License-Identifier: GPL-3.0-only

import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { closeSync, mkdirSync, openSync, readFileSync, readdirSync, renameSync, rmSync, writeFileSync, writeSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../../..', import.meta.url));
const [mode, output, tag] = process.argv.slice(2);
if (!output || !['pack', 'envelope'].includes(mode)) {
  throw new Error('Usage: build-artwork.mjs pack <output> <tag> | envelope <output>');
}
const destination = resolve(output);
const manifestPath = resolve(destination, 'artwork-manifest.json');
if (mode === 'envelope') {
  const manifest = readFileSync(manifestPath, 'utf8');
  const signature = readFileSync(manifestPath + '.sig', 'utf8').trim();
  writeFileSync(resolve(destination, 'artwork.json'), JSON.stringify({ manifest, signature }));
  rmSync(manifestPath);
  rmSync(manifestPath + '.sig');
} else {
  if (!/^(artwork|v\d+\.\d+\.\d+)$/.test(tag ?? '')) {
    throw new Error('Supply the client release tag or artwork channel');
  }
  mkdirSync(destination, { recursive: true });
  const temporary = resolve(destination, 'artwork.pack.tmp');
  const file = openSync(temporary, 'wx');
  const hash = createHash('sha256');
  const files = [];
  let offset = 0;
  try {
    for (const [directory, extension] of [['ani', 'gif'], ['gen5', 'png']]) {
      const source = resolve(root, 'apps/desktop/public/sprites', directory);
      for (const name of readdirSync(source).sort()) {
        if (!name.endsWith('.' + extension)) continue;
        if (!/^[a-z0-9_-]{1,120}\.[a-z]+$/.test(name)) throw new Error('Unsupported artwork name: ' + name);
        const bytes = readFileSync(resolve(source, name));
        if (bytes.length === 0 || bytes.length > 4 * 1024 * 1024) throw new Error('Unsupported artwork size: ' + name);
        let written = 0;
        while (written < bytes.length) {
          written += writeSync(file, bytes, written, bytes.length - written);
        }
        hash.update(bytes);
        files.push({ path: directory + '/' + name, offset, size: bytes.length, sha256: createHash('sha256').update(bytes).digest('hex') });
        offset += bytes.length;
      }
    }
  } finally {
    closeSync(file);
  }
  if (files.length === 0 || offset > 512 * 1024 * 1024) throw new Error('Unsupported artwork pack size');
  const pack = 'artwork-' + hash.digest('hex') + '.pack';
  renameSync(temporary, resolve(destination, pack));
  const sourceCommit = execFileSync('git', ['rev-parse', 'HEAD'], { cwd: root, encoding: 'utf8' }).trim();
  writeFileSync(manifestPath, JSON.stringify({ revision: Date.now(), tag, pack, sourceCommit, files }));
  console.log('Packed ' + files.length + ' artwork files (' + offset + ' bytes)');
}
