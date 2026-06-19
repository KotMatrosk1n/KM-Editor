// SPDX-License-Identifier: GPL-3.0-only

import { readdir, readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const rootPath = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const srcPath = path.join(rootPath, 'src');
const defaultMaxLines = 1250;
const filesWithoutLineBudgets = new Set(['src/App.tsx', 'src/App.test.tsx']);
const fileLineBudgets = new Map([
  ['src/bridge/contracts.ts', 3700],
  ['src/bridge/contracts.test.ts', 3650],
  ['src/bridge/projectBridge.test.ts', 2875],
  ['src/testSupport/appTestFixtures.ts', 7000],
  ['src/workbenchStore.ts', 1300]
]);

const sourceExtensions = new Set(['.ts', '.tsx']);
const failures = [];

for (const filePath of await collectSourceFiles(srcPath)) {
  const relativePath = toPosix(path.relative(rootPath, filePath));
  if (filesWithoutLineBudgets.has(relativePath)) {
    continue;
  }

  const content = await readFile(filePath, 'utf8');
  const lineCount = content.length === 0 ? 0 : content.split(/\r\n|\r|\n/).length;
  const maxLines = fileLineBudgets.get(relativePath) ?? defaultMaxLines;

  if (lineCount > maxLines) {
    failures.push(`${relativePath}: ${lineCount} lines exceeds ${maxLines}`);
  }
}

if (failures.length > 0) {
  console.error('Source file size guard failed. Split large files before adding more code.');
  for (const failure of failures) {
    console.error(`- ${failure}`);
  }

  process.exit(1);
}

async function collectSourceFiles(directoryPath) {
  const entries = await readdir(directoryPath, { withFileTypes: true });
  const files = [];

  for (const entry of entries) {
    const entryPath = path.join(directoryPath, entry.name);
    if (entry.isDirectory()) {
      files.push(...await collectSourceFiles(entryPath));
      continue;
    }

    if (entry.isFile() && sourceExtensions.has(path.extname(entry.name))) {
      files.push(entryPath);
    }
  }

  return files;
}

function toPosix(value) {
  return value.split(path.sep).join('/');
}
