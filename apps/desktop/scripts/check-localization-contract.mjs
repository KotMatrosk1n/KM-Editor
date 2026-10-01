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
const requiredEditorLiterals = [
  'Mum', 'Mum (Postwick)', 'Mum (Wedgehurst Station)',
  'Reviewing change plan', 'Writing output files', 'Preparing encounter updates',
  'Runtime Core', 'Runtime Flags', 'Advanced Battle Behavior',
  'This field is read-only.', 'Catalog group', 'Color variant',
  'Static Encounter', 'Object Hash', 'None (empty hash)'
];
const requiredKeyedLiterals = [
  'Held Effect', 'Added flinch chance (%)', 'Effect power (handler-specific)',
  'A handler-specific effect parameter whose meaning varies by item. Preserve it unless that item effect has been verified.',
  "Used only after a damaging hit that the target survives and when the move's native flinch chance is zero. It does not add to, replace, or multiply a nonzero native chance."
];
const literalPatternSource = readFileSync(new URL('../src/localization/editorLiteralPatterns.ts', import.meta.url), 'utf8');
const requiredLiteralTemplates = [...literalPatternSource.matchAll(/template: (['"])(.*?)\1, parameters:/gu)]
  .map((match) => match[2]);
assert.ok(requiredLiteralTemplates.length > 0, 'Editor literal template inventory is empty');

export function verifyLocalizationResources(catalogs) {
  const english = catalogs.en;
  for (const literal of [...requiredEditorLiterals, ...requiredLiteralTemplates]) {
    assert.ok(Object.hasOwn(english.literals, literal), `Missing editor literal: ${literal}`);
  }
  for (const literal of requiredKeyedLiterals) {
    assert.equal(english.keys[literal], literal, `Missing exact keyed UI literal: ${literal}`);
  }
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
          key.startsWith('npcItemGift.location.') ||
          key.startsWith('diagnostics.condition.') ||
          key.startsWith('diagnostics.cache.')
        )) {
          assert.notEqual(value, english.keys[key], `${language} ${key} still contains the English fallback`);
        }
      }
    }
    if (language !== 'en') {
      for (const literal of [...requiredEditorLiterals, ...requiredLiteralTemplates]) {
        if (/[A-Za-z]/u.test(literal.replace(/\{[A-Za-z0-9_]+\}/gu, ''))) {
          assert.notEqual(catalogs[language].literals[literal], english.literals[literal], `${language} ${literal} still contains the English fallback`);
        }
      }
      for (const literal of requiredKeyedLiterals) {
        assert.notEqual(catalogs[language].keys[literal], literal, `${language} ${literal} still contains the English fallback`);
      }
    }
  }
}

verifyLocalizationResources(resources);
console.log('Seven language catalogs, parameters and required translations passed.');
