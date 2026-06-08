// SPDX-License-Identifier: GPL-3.0-only

import { execFileSync } from 'node:child_process';
import { copyFileSync, mkdirSync, rmSync, statSync } from 'node:fs';
import { dirname, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const desktopRoot = resolve(scriptDirectory, '..');
const repositoryRoot = resolve(desktopRoot, '..', '..');
const tauriRoot = resolve(desktopRoot, 'src-tauri');
const sidecarBaseName = 'km-tools-bridge';
const targetTriple = getRustTargetTriple();
const runtimeIdentifier = getDotNetRuntimeIdentifier(targetTriple);
const isWindowsTarget = targetTriple.includes('windows');
const binaryExtension = isWindowsTarget ? '.exe' : '';
const publishDirectory = resolve(
  tauriRoot,
  'binaries',
  '.publish',
  runtimeIdentifier
);
const projectPath = resolve(repositoryRoot, 'src', 'KM.Tools', 'KM.Tools.csproj');
const publishedBinary = resolve(publishDirectory, `KM.Tools${binaryExtension}`);
const stagedBinary = resolve(
  tauriRoot,
  'binaries',
  `${sidecarBaseName}-${targetTriple}${binaryExtension}`
);

rmSync(publishDirectory, { force: true, recursive: true });
mkdirSync(publishDirectory, { recursive: true });

execFileSync(
  'dotnet',
  [
    'publish',
    projectPath,
    '-c',
    'Release',
    '-r',
    runtimeIdentifier,
    '--self-contained',
    'true',
    '-p:PublishSingleFile=true',
    '-p:EnableCompressionInSingleFile=true',
    '-p:PublishTrimmed=false',
    '-o',
    publishDirectory
  ],
  {
    cwd: repositoryRoot,
    stdio: 'inherit'
  }
);

assertFile(publishedBinary, 'KM.Tools publish did not produce the expected executable.');
mkdirSync(dirname(stagedBinary), { recursive: true });
copyFileSync(publishedBinary, stagedBinary);

console.log(`Staged KM.Tools sidecar at ${relative(repositoryRoot, stagedBinary)}`);

function getRustTargetTriple() {
  try {
    return execFileSync('rustc', ['--print', 'host-tuple'], {
      encoding: 'utf8'
    }).trim();
  } catch {
    const versionDetails = execFileSync('rustc', ['-Vv'], {
      encoding: 'utf8'
    });
    const hostMatch = /^host:\s*(\S+)$/m.exec(versionDetails);

    if (hostMatch) {
      return hostMatch[1];
    }

    throw new Error('Could not determine the Rust target triple for the Tauri sidecar.');
  }
}

function getDotNetRuntimeIdentifier(triple) {
  switch (triple) {
    case 'x86_64-pc-windows-msvc':
      return 'win-x64';
    case 'aarch64-pc-windows-msvc':
      return 'win-arm64';
    case 'i686-pc-windows-msvc':
      return 'win-x86';
    case 'x86_64-unknown-linux-gnu':
      return 'linux-x64';
    case 'aarch64-unknown-linux-gnu':
      return 'linux-arm64';
    case 'x86_64-apple-darwin':
      return 'osx-x64';
    case 'aarch64-apple-darwin':
      return 'osx-arm64';
    default:
      throw new Error(`No .NET runtime identifier is mapped for Rust target '${triple}'.`);
  }
}

function assertFile(path, message) {
  try {
    const stat = statSync(path);

    if (stat.isFile()) {
      return;
    }
  } catch {
    // Fall through to the clearer build-oriented error below.
  }

  throw new Error(message);
}
