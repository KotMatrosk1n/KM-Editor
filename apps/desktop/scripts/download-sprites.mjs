// SPDX-License-Identifier: GPL-3.0-only

import fs from 'node:fs';
import https from 'node:https';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const spriteRoot = path.resolve(scriptDirectory, '..', 'public', 'sprites');
const concurrency = 16;
const refreshCache = process.argv.includes('--refresh');
const sources = [
  {
    extension: 'png',
    indexUrl: 'https://play.pokemonshowdown.com/sprites/gen5/',
    name: 'gen5'
  },
  {
    extension: 'gif',
    indexUrl: 'https://play.pokemonshowdown.com/sprites/ani/',
    name: 'ani'
  }
];

function countExistingSprites(source) {
  const targetDirectory = path.join(spriteRoot, source.name);
  try {
    return fs
      .readdirSync(targetDirectory, { withFileTypes: true })
      .filter(
        (entry) =>
          entry.isFile() &&
          path.extname(entry.name).toLocaleLowerCase() === `.${source.extension}`
      ).length;
  } catch {
    return 0;
  }
}

function hasReadyCache() {
  if (!hasExistingFile(path.join(spriteRoot, 'manifest.json'))) {
    return false;
  }

  return sources.every((source) => countExistingSprites(source) > 0);
}

function request(url, redirectsRemaining = 5) {
  return new Promise((resolve, reject) => {
    https
      .get(url, (response) => {
        const statusCode = response.statusCode ?? 0;
        const location = response.headers.location;

        if (statusCode >= 300 && statusCode < 400 && location && redirectsRemaining > 0) {
          response.resume();
          resolve(request(new URL(location, url).toString(), redirectsRemaining - 1));
          return;
        }

        if (statusCode < 200 || statusCode >= 300) {
          response.resume();
          reject(new Error(`Request failed for ${url} with HTTP ${statusCode}`));
          return;
        }

        const chunks = [];
        response.on('data', (chunk) => chunks.push(chunk));
        response.on('end', () => resolve(Buffer.concat(chunks)));
      })
      .on('error', reject);
  });
}

function parseSpriteIndex(html, extension) {
  const sprites = [];
  const seen = new Set();
  const pattern = /href="\.\/([^"]+)"/g;
  let match;

  while ((match = pattern.exec(html)) !== null) {
    const fileName = decodeURIComponent(match[1]);
    if (path.extname(fileName).toLocaleLowerCase() !== `.${extension}`) {
      continue;
    }

    if (path.basename(fileName) !== fileName || seen.has(fileName)) {
      continue;
    }

    seen.add(fileName);
    sprites.push(fileName);
  }

  sprites.sort();
  return sprites;
}

function hasExistingFile(filePath) {
  try {
    return fs.statSync(filePath).size > 0;
  } catch {
    return false;
  }
}

async function runLimited(items, worker) {
  let nextIndex = 0;

  await Promise.all(
    Array.from({ length: Math.min(concurrency, items.length) }, async () => {
      while (nextIndex < items.length) {
        const item = items[nextIndex];
        nextIndex += 1;
        await worker(item);
      }
    })
  );
}

async function downloadSource(source) {
  const targetDirectory = path.join(spriteRoot, source.name);
  fs.mkdirSync(targetDirectory, { recursive: true });

  const indexHtml = (await request(source.indexUrl)).toString('utf8');
  const spriteFiles = parseSpriteIndex(indexHtml, source.extension);
  let downloaded = 0;
  let skipped = 0;

  await runLimited(spriteFiles, async (fileName) => {
    const targetPath = path.join(targetDirectory, fileName);
    if (hasExistingFile(targetPath)) {
      skipped += 1;
      return;
    }

    const bytes = await request(new URL(fileName, source.indexUrl).toString());
    fs.writeFileSync(targetPath, bytes);
    downloaded += 1;
  });

  return {
    downloaded,
    extension: source.extension,
    files: spriteFiles.length,
    indexUrl: source.indexUrl,
    name: source.name,
    skipped
  };
}

function writeManifest(results) {
  const manifest = {
    sources: results.map((result) => ({
      extension: result.extension,
      files: result.files,
      indexUrl: result.indexUrl,
      name: result.name
    }))
  };

  fs.writeFileSync(
    path.join(spriteRoot, 'manifest.json'),
    `${JSON.stringify(manifest, null, 2)}\n`
  );
}

if (!refreshCache && hasReadyCache()) {
  const summary = sources
    .map((source) => `${source.name}: ${countExistingSprites(source)} cached`)
    .join(', ');
  console.log(`sprite cache ready: ${summary}`);
} else {
  const results = [];
  for (const source of sources) {
    results.push(await downloadSource(source));
  }

  writeManifest(results);

  const summary = results
    .map(
      (result) =>
        `${result.name}: ${result.files} files (${result.downloaded} new, ${result.skipped} cached)`
    )
    .join(', ');
  console.log(`sprite cache ready: ${summary}`);
}
