#!/usr/bin/env node
// SPDX-License-Identifier: GPL-3.0-only

import { spawnSync } from 'node:child_process';

const blockedPrefixes = [
  'handoff/',
  '.codex-remote-attachments/',
  '.scratch/',
  '.tmp/',
  'scratch/',
  'tmp/',
  'temp/',
  'fixtures/private/',
];

const blockedExactPaths = new Set([
  '0x71002e0ef0',
  'KM_Editor_Handoff.md',
]);

const blockedPathPatterns = [
  /^scratch[-_][^/]+\//i,
  /^(?:base-)?(?:romfs|exefs)\//i,
  /^(?:dump|dumps|layeredfs|output|outputs|generated)\//i,
  /(^|\/)\.env(?:\.(?!example$)[^/]+)?$/i,
  /(^|\/)(?:[^/]+\.key(?:\.pub)?|[^/]+\.(?:p12|pfx|pem))$/i,
  /(^|\/)[^/]+\.(?:dll|dylib|exe|msi|pdb|so)$/i,
  /^.+\.(?:7z|docx|rar|zip)$/i,
];

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
  .filter(
    (file) =>
      blockedExactPaths.has(file) ||
      blockedPrefixes.some((prefix) => file.startsWith(prefix)) ||
      blockedPathPatterns.some((pattern) => pattern.test(file)),
  );

if (blockedPaths.length > 0) {
  console.error('Local workspace paths must not be tracked:');
  for (const file of blockedPaths) {
    console.error(`  ${file}`);
  }
  process.exit(1);
}

const blockedContentPattern = [
  String.raw`[A-Za-z]:\\Users\\`,
  String.raw`/Users/[^/]+/`,
  String.raw`/home/[^/]+/`,
  String.raw`OneDrive[\\/]`,
  String.raw`-----BEGIN (RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----`,
  String.raw`github_pat_[A-Za-z0-9_]{20,}`,
  String.raw`gh[pousr]_[A-Za-z0-9]{20,}`,
  String.raw`glpat-[A-Za-z0-9_-]{20,}`,
  String.raw`AKIA[0-9A-Z]{16}`,
  String.raw`AIza[0-9A-Za-z_-]{30,}`,
  String.raw`sk-[A-Za-z0-9_-]{20,}`,
  String.raw`xox[baprs]-[A-Za-z0-9-]{20,}`,
  String.raw`eyJ[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{10,}`,
].join('|');
const contentResult = spawnSync(
  'git',
  [
    'grep',
    '-I',
    '-n',
    '-E',
    '-e',
    blockedContentPattern,
    '--',
    '.',
    ':!scripts/check-workspace-paths.mjs',
  ],
  {
    encoding: 'utf8',
    shell: false,
  },
);

if (contentResult.status !== 0 && contentResult.status !== 1) {
  process.stderr.write(contentResult.stderr);
  process.exit(contentResult.status ?? 1);
}

if (contentResult.status === 0) {
  console.error('Sensitive content or account-specific paths must not be tracked:');
  for (const match of contentResult.stdout.split(/\r?\n/).filter(Boolean)) {
    const location = /^(.*?:\d+):/.exec(match)?.[1] ?? '(location unavailable)';
    console.error(`  ${location} [content hidden]`);
  }
  process.exit(1);
}
