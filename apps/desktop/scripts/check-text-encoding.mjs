// SPDX-License-Identifier: GPL-3.0-only

import { readFileSync, readdirSync } from 'node:fs';
import { extname, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const utf8 = new TextDecoder('utf-8', { fatal: true });
const legacyBytes = new Map(Array.from({ length: 256 }, (_, byte) => [byte, byte]));
const windows1252 = [
  0x20AC, 0x0081, 0x201A, 0x0192, 0x201E, 0x2026, 0x2020, 0x2021,
  0x02C6, 0x2030, 0x0160, 0x2039, 0x0152, 0x008D, 0x017D, 0x008F,
  0x0090, 0x2018, 0x2019, 0x201C, 0x201D, 0x2022, 0x2013, 0x2014,
  0x02DC, 0x2122, 0x0161, 0x203A, 0x0153, 0x009D, 0x017E, 0x0178
];
windows1252.forEach((codePoint, index) => legacyBytes.set(codePoint, 0x80 + index));

// Valid UTF-8 can still contain text previously decoded as a legacy code page.
// Detect complete byte sequences without rejecting correctly written accents.
export function verifyTextEncoding(input, label = 'text') {
  let text;
  try {
    text = typeof input === 'string' ? input : utf8.decode(input);
  } catch {
    throw new Error(`${label}: source must be valid UTF-8.`);
  }

  for (let index = 0; index < text.length; index += 1) {
    const codePoint = text.charCodeAt(index);
    const line = () => text.slice(0, index).split('\n').length;
    if (codePoint === 0xFFFD || (codePoint >= 0x80 && codePoint <= 0x9F)) {
      throw new Error(`${label}:${line()}: replacement character or legacy text control found.`);
    }
    const first = legacyBytes.get(codePoint);
    const length = first >= 0xC2 && first <= 0xDF ? 2
      : first >= 0xE0 && first <= 0xEF ? 3
        : first >= 0xF0 && first <= 0xF4 ? 4 : 0;
    if (!length || index + length > text.length) continue;

    const bytes = [first];
    for (let offset = 1; offset < length; offset += 1) {
      const byte = legacyBytes.get(text.charCodeAt(index + offset));
      if (byte === undefined || byte < 0x80 || byte > 0xBF) break;
      bytes.push(byte);
    }
    if (bytes.length !== length) continue;
    try {
      utf8.decode(Uint8Array.from(bytes));
    } catch {
      continue;
    }
    throw new Error(`${label}:${line()}: text appears to contain a misdecoded UTF-8 sequence.`);
  }
  return text;
}

function verifyJsonText(value, label) {
  if (typeof value === 'string') {
    verifyTextEncoding(value, label);
  } else if (Array.isArray(value)) {
    value.forEach((entry) => verifyJsonText(entry, label));
  } else if (value && typeof value === 'object') {
    for (const [key, entry] of Object.entries(value)) {
      verifyTextEncoding(key, label);
      verifyJsonText(entry, label);
    }
  }
}

const desktopRoot = fileURLToPath(new URL('../', import.meta.url));
const textExtensions = new Set(['.ts', '.tsx', '.js', '.jsx', '.mjs', '.json', '.css', '.html', '.svg', '.md']);
let checked = 0;

function verifyFile(path) {
  const label = relative(desktopRoot, path).replaceAll('\\', '/');
  const text = verifyTextEncoding(readFileSync(path), label);
  if (extname(path) === '.json') verifyJsonText(JSON.parse(text), `${label} (JSON text)`);
  checked += 1;
}

function verifyDirectory(directory) {
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const path = fileURLToPath(new URL(entry.name + (entry.isDirectory() ? '/' : ''), directory));
    if (entry.isDirectory()) verifyDirectory(new URL(`${entry.name}/`, directory));
    else if (entry.isFile() && textExtensions.has(extname(entry.name))) verifyFile(path);
  }
}

verifyDirectory(new URL('../src/', import.meta.url));
verifyDirectory(new URL('./', import.meta.url));
verifyFile(fileURLToPath(new URL('../index.html', import.meta.url)));
console.log(`Source text encoding passed (${checked} files, including all locale catalogs).`);
