#!/usr/bin/env node
// SPDX-License-Identifier: GPL-3.0-only

import { spawnSync } from 'node:child_process';

const blockedPrefixes = [
  'handoff/',
  'scratch-ghidra-da/',
  'scratch-ghidra-scripts/',
  'scratch-royal-candy-inspect/',
];

const blockedExactPaths = new Set([
  '0x71002e0ef0',
  'KM_Editor_Handoff.md',
]);

const result = spawnSync('git', ['ls-files'], {
  encoding: 'utf8',
  shell: false,
});

if (result.status !== 0) {
  process.stderr.write(result.stderr);
  process.exit(result.status ?? 1);
}

const blockedPaths = result.stdout
  .split(/\r?\n/)
  .filter(Boolean)
  .filter((file) => blockedExactPaths.has(file) || blockedPrefixes.some((prefix) => file.startsWith(prefix)));

if (blockedPaths.length > 0) {
  console.error('Local workspace paths must not be tracked:');
  for (const file of blockedPaths) {
    console.error(`  ${file}`);
  }
  process.exit(1);
}
