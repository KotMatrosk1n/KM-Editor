#!/usr/bin/env node
// SPDX-License-Identifier: GPL-3.0-only

import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = resolve(scriptDirectory, '..');
const files = {
  cargoLock: 'apps/desktop/src-tauri/Cargo.lock',
  cargoToml: 'apps/desktop/src-tauri/Cargo.toml',
  desktopPackage: 'apps/desktop/package.json',
  rootPackage: 'package.json',
  tauriConfig: 'apps/desktop/src-tauri/tauri.conf.json'
};
const versionPattern = /^(0|[1-9]\d{0,4})\.(0|[1-9]\d{0,4})\.(0|[1-9]\d{0,4})$/;

try {
  run();
} catch (error) {
  console.error(error instanceof Error ? error.message : String(error));
  process.exitCode = 1;
}

function run() {
  const [mode, version, ...extraArguments] = process.argv.slice(2);
  if (!['--check', '--set'].includes(mode) || extraArguments.length > 0) {
    failWithUsage();
  }

  if (mode === '--set' && !version) {
    failWithUsage();
  }

  const currentContents = readAllFiles();
  const currentState = readVersionState(currentContents);
  const expectedVersion = validateVersion(
    version ?? currentState.rootPackageVersion,
    mode === '--set' ? 'Requested version' : 'Expected version'
  );

  if (mode === '--check') {
    assertSynchronized(currentState, expectedVersion);
    console.log(
      `KM Editor release version ${expectedVersion} is synchronized across all six required fields.`
    );
    return;
  }

  const updatedContents = createUpdatedContents(currentContents, expectedVersion);
  assertSynchronized(readVersionState(updatedContents), expectedVersion);

  const changedFiles = [];
  for (const [key, relativePath] of Object.entries(files)) {
    if (updatedContents[key] === currentContents[key]) {
      continue;
    }

    writeFileSync(resolve(repositoryRoot, relativePath), updatedContents[key], 'utf8');
    changedFiles.push(relativePath);
  }

  console.log(`Synchronized KM Editor release version ${expectedVersion}.`);
  if (changedFiles.length === 0) {
    console.log('All required fields were already current.');
  } else {
    for (const relativePath of changedFiles) {
      console.log(`  ${relativePath}`);
    }
  }
}

function readAllFiles() {
  return Object.fromEntries(
    Object.entries(files).map(([key, relativePath]) => [
      key,
      readFileSync(resolve(repositoryRoot, relativePath), 'utf8')
    ])
  );
}

function readVersionState(contents) {
  const rootPackage = parseJson(contents.rootPackage, files.rootPackage);
  const desktopPackage = parseJson(contents.desktopPackage, files.desktopPackage);
  const tauriConfig = parseJson(contents.tauriConfig, files.tauriConfig);

  if (rootPackage.name !== 'km-editor') {
    throw new Error(`${files.rootPackage} does not describe the KM Editor workspace.`);
  }

  if (desktopPackage.name !== '@km-editor/desktop') {
    throw new Error(`${files.desktopPackage} does not describe the KM Editor desktop package.`);
  }

  const mainWindows = Array.isArray(tauriConfig.app?.windows)
    ? tauriConfig.app.windows.filter((window) => window?.label === 'main')
    : [];
  if (mainWindows.length !== 1) {
    throw new Error(
      `${files.tauriConfig} must contain exactly one desktop window labeled "main".`
    );
  }

  return {
    cargoLockVersion: readCargoLockVersion(contents.cargoLock),
    cargoTomlVersion: readCargoTomlVersion(contents.cargoToml),
    desktopPackageVersion: desktopPackage.version,
    mainWindowTitle: mainWindows[0].title,
    rootPackageVersion: rootPackage.version,
    tauriConfigVersion: tauriConfig.version
  };
}

function createUpdatedContents(contents, version) {
  const rootPackage = parseJson(contents.rootPackage, files.rootPackage);
  rootPackage.version = version;

  const desktopPackage = parseJson(contents.desktopPackage, files.desktopPackage);
  desktopPackage.version = version;

  const tauriConfig = parseJson(contents.tauriConfig, files.tauriConfig);
  const mainWindows = tauriConfig.app.windows.filter((window) => window?.label === 'main');
  if (mainWindows.length !== 1) {
    throw new Error(
      `${files.tauriConfig} must contain exactly one desktop window labeled "main".`
    );
  }
  tauriConfig.version = version;
  mainWindows[0].title = `KM Editor v${version}`;

  return {
    cargoLock: replaceCargoLockVersion(contents.cargoLock, version),
    cargoToml: replaceCargoTomlVersion(contents.cargoToml, version),
    desktopPackage: formatJson(desktopPackage, contents.desktopPackage),
    rootPackage: formatJson(rootPackage, contents.rootPackage),
    tauriConfig: formatJson(tauriConfig, contents.tauriConfig)
  };
}

function assertSynchronized(state, expectedVersion) {
  const expectedWindowTitle = `KM Editor v${expectedVersion}`;
  const fields = [
    ['package.json version', state.rootPackageVersion, expectedVersion],
    ['apps/desktop/package.json version', state.desktopPackageVersion, expectedVersion],
    ['tauri.conf.json version', state.tauriConfigVersion, expectedVersion],
    ['tauri.conf.json main window title', state.mainWindowTitle, expectedWindowTitle],
    ['Cargo.toml package version', state.cargoTomlVersion, expectedVersion],
    ['Cargo.lock km-editor-desktop version', state.cargoLockVersion, expectedVersion]
  ];
  const mismatches = fields.filter(([, actual, expected]) => actual !== expected);

  if (mismatches.length === 0) {
    return;
  }

  const details = mismatches
    .map(([label, actual, expected]) => `  ${label}: expected ${expected}, found ${actual}`)
    .join('\n');
  throw new Error(
    `KM Editor release version metadata is not synchronized:\n${details}\n` +
      `Run "pnpm version:set ${expectedVersion}" to update every required field together.`
  );
}

function validateVersion(value, label) {
  if (typeof value !== 'string') {
    throw new Error(`${label} is missing.`);
  }

  const match = versionPattern.exec(value);
  if (!match) {
    throw new Error(`${label} must be a canonical numeric X.Y.Z version.`);
  }

  const [, majorText, minorText, patchText] = match;
  const major = Number(majorText);
  const minor = Number(minorText);
  const patch = Number(patchText);
  if (major > 255 || minor > 255 || patch > 65535) {
    throw new Error(
      `${label} exceeds setup limits: major and minor must be at most 255, and patch must be at most 65535.`
    );
  }

  return value;
}

function readCargoTomlVersion(content) {
  const section = findTomlSection(content, 'package', files.cargoToml);
  return readSingleVersionAssignment(section.content, `${files.cargoToml} [package]`);
}

function replaceCargoTomlVersion(content, version) {
  const section = findTomlSection(content, 'package', files.cargoToml);
  const updatedSection = replaceSingleVersionAssignment(
    section.content,
    version,
    `${files.cargoToml} [package]`
  );
  return content.slice(0, section.start) + updatedSection + content.slice(section.end);
}

function findTomlSection(content, sectionName, relativePath) {
  const headerPattern = new RegExp(`^\\[${escapeRegExp(sectionName)}\\]\\s*$`, 'gm');
  const headers = [...content.matchAll(headerPattern)];
  if (headers.length !== 1) {
    throw new Error(
      `${relativePath} must contain exactly one [${sectionName}] section; found ${headers.length}.`
    );
  }

  const start = headers[0].index;
  const nextHeaderPattern = /^\s*\[[^\r\n]+\]\s*$/gm;
  nextHeaderPattern.lastIndex = start + headers[0][0].length;
  const nextHeader = nextHeaderPattern.exec(content);
  const end = nextHeader?.index ?? content.length;
  return { content: content.slice(start, end), end, start };
}

function readCargoLockVersion(content) {
  return readSingleCargoLockPackage(content).version;
}

function replaceCargoLockVersion(content, version) {
  const packageBlock = readSingleCargoLockPackage(content);
  const updatedBlock = replaceSingleVersionAssignment(
    packageBlock.content,
    version,
    `${files.cargoLock} km-editor-desktop package`
  );
  return content.slice(0, packageBlock.start) + updatedBlock + content.slice(packageBlock.end);
}

function readSingleCargoLockPackage(content) {
  const headers = [...content.matchAll(/^\[\[package\]\]\s*$/gm)];
  const matchingBlocks = [];
  for (let index = 0; index < headers.length; index += 1) {
    const start = headers[index].index;
    const end = headers[index + 1]?.index ?? content.length;
    const block = content.slice(start, end);
    const nameMatches = [...block.matchAll(/^name\s*=\s*"([^"]+)"\s*$/gm)];
    if (nameMatches.length === 1 && nameMatches[0][1] === 'km-editor-desktop') {
      matchingBlocks.push({ content: block, end, start });
    }
  }

  if (matchingBlocks.length !== 1) {
    throw new Error(
      `${files.cargoLock} must contain exactly one km-editor-desktop package; found ${matchingBlocks.length}.`
    );
  }

  const packageBlock = matchingBlocks[0];
  return {
    ...packageBlock,
    version: readSingleVersionAssignment(
      packageBlock.content,
      `${files.cargoLock} km-editor-desktop package`
    )
  };
}

function readSingleVersionAssignment(content, description) {
  const matches = [...content.matchAll(/^version\s*=\s*"([^"]+)"\s*$/gm)];
  if (matches.length !== 1) {
    throw new Error(`${description} must contain exactly one version assignment.`);
  }

  return matches[0][1];
}

function replaceSingleVersionAssignment(content, version, description) {
  const pattern = /^(version\s*=\s*")([^"]+)("\s*)$/gm;
  const matches = [...content.matchAll(pattern)];
  if (matches.length !== 1) {
    throw new Error(`${description} must contain exactly one version assignment.`);
  }

  return content.replace(pattern, (_match, prefix, _currentVersion, suffix) => {
    return `${prefix}${version}${suffix}`;
  });
}

function parseJson(content, relativePath) {
  try {
    return JSON.parse(content);
  } catch (error) {
    throw new Error(`${relativePath} is not valid JSON: ${error.message}`);
  }
}

function formatJson(value, originalContent) {
  const newline = originalContent.includes('\r\n') ? '\r\n' : '\n';
  return `${JSON.stringify(value, null, 2).replaceAll('\n', newline)}${newline}`;
}

function escapeRegExp(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function failWithUsage() {
  console.error('Usage:');
  console.error('  node scripts/release-version.mjs --check [X.Y.Z]');
  console.error('  node scripts/release-version.mjs --set X.Y.Z');
  process.exit(1);
}
