// SPDX-License-Identifier: GPL-3.0-only

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const languages = ['en', 'es', 'fr', 'de', 'ru', 'uk', 'zh'];
const resources = Object.fromEntries(languages.map((language) => [
  language,
  JSON.parse(readFileSync(new URL(`../src/localization/resources/${language}.json`, import.meta.url), 'utf8'))
]));

const unchangedPackageTerms = new Set([
  'gameplaySettings.inGamePackage.target.atmosphere',
  'gameplaySettings.inGamePackage.target.ryujinx',
  'gameplaySettings.inGamePackage.target.eden',
  'gameplaySettings.inGamePackage.executableInput.length'
]);
const placeholders = (text) => [...text.matchAll(/\{[A-Za-z0-9_]+\}/gu)]
  .map((match) => match[0]).sort();
// "Trainer" is also the German UI term.
const unchangedLocalizedTerms = new Set(['de:editorText.trainerDraftSummary']);

export function verifyLocalizationResources(catalogs) {
  const english = catalogs.en;
  for (const language of languages) {
    for (const section of ['keys', 'literals']) {
      const entries = catalogs[language][section];
      assert.deepEqual(Object.keys(entries).sort(), Object.keys(english[section]).sort(), `${language} ${section} inventory differs`);
      for (const [key, value] of Object.entries(entries)) {
        assert.equal(typeof value, 'string', `${language} ${key} must be text`);
        assert.ok(value.trim(), `${language} ${key} is empty`);
        assert.ok(!/KMPLACEHOLDER|[\u200B-\u200D\uFEFF]/u.test(value), `${language} ${key} contains placeholder or invisible spacing`);
        assert.deepEqual(placeholders(value), placeholders(english[section][key]), `${language} ${key} parameters differ`);
        if (language !== 'en' && section === 'keys' && !unchangedPackageTerms.has(key) &&
          !unchangedLocalizedTerms.has(`${language}:${key}`) && (
          key.startsWith('gameplaySettings.inGamePackage.') ||
          key.startsWith('gameplaySettings.delivery.runtime') ||
          key.startsWith('editorText.') ||
          key.startsWith('diagnostics.condition.') ||
          key.startsWith('diagnostics.cache.')
        )) {
          assert.notEqual(value, english.keys[key], `${language} ${key} still contains the English fallback`);
        }
      }
    }
  }
}

verifyLocalizationResources(resources);
console.log('Seven language catalogs, parameters and required translations passed.');
