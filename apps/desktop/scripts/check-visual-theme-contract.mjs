// SPDX-License-Identifier: GPL-3.0-only

import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';

function read(relativePath) {
  return readFileSync(new URL(relativePath, import.meta.url), 'utf8').replace(/\r\n?/g, '\n');
}

function contrastRatio(foreground, background) {
  const luminance = (hex) => {
    const channels = hex
      .slice(1)
      .match(/.{2}/gu)
      .map((channel) => Number.parseInt(channel, 16) / 255)
      .map((channel) =>
        channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4
      );
    return 0.2126 * channels[0] + 0.7152 * channels[1] + 0.0722 * channels[2];
  };
  const first = luminance(foreground);
  const second = luminance(background);
  return (Math.max(first, second) + 0.05) / (Math.min(first, second) + 0.05);
}

function requireContrast(name, foreground, background, minimum = 4.5) {
  const ratio = contrastRatio(foreground, background);
  assert.ok(
    ratio >= minimum,
    `${name} contrast is ${ratio.toFixed(2)}:1; expected at least ${minimum}:1.`
  );
}

function mixSrgbHex(first, firstPercentage, second) {
  const firstWeight = firstPercentage / 100;
  const secondWeight = 1 - firstWeight;
  const channels = [1, 3, 5].map((offset) =>
    Math.round(
      Number.parseInt(first.slice(offset, offset + 2), 16) * firstWeight +
        Number.parseInt(second.slice(offset, offset + 2), 16) * secondWeight
    )
  );
  return `#${channels.map((channel) => channel.toString(16).padStart(2, '0')).join('')}`;
}

const appearance = read('../src/features/settings/AppearancePreferencesProvider.tsx');
const themePanel = read('../src/features/settings/PersonalizationSettingsPanel.tsx');
const app = read('../src/App.tsx');
const main = read('../src/main.tsx');
const styles = read('../src/styles.css');

assert.match(
  appearance,
  /export type VisualTheme = 'classic' \| 'renegade' \| 'royal' \| 'sovereign' \| 'arcane' \| 'relic';/,
  'The visual-theme model must expose all six supported identities in stable order.'
);
assert.match(
  appearance,
  /export const defaultVisualTheme: VisualTheme = 'classic';/,
  'Classic must remain the default visual theme.'
);
assert.match(
  appearance,
  /visualThemeStorageKey = 'km-editor\.visual-theme\.v1'/,
  'Visual identity must use its own versioned storage key.'
);
assert.notEqual(
  appearance.match(/appearancePreferencesStorageKey = '([^']+)'/)?.[1],
  appearance.match(/visualThemeStorageKey = '([^']+)'/)?.[1],
  'Visual identity must remain persisted independently from accessibility preferences.'
);
assert.match(
  appearance,
  /const stored = readVisualTheme\(\);[\s\S]*?applyVisualTheme\(stored\);[\s\S]*?return stored;/,
  'The stored visual theme must be applied during provider initialization.'
);
assert.match(
  appearance,
  /setVisualTheme: \(theme\) => \{[\s\S]*?setVisualThemeState\(theme\);[\s\S]*?applyVisualTheme\(theme\);[\s\S]*?writeVisualTheme\(theme\);/,
  'A visual-theme choice must update state, the live document, and persistence without a restart.'
);
assert.match(
  appearance,
  /document\.documentElement\.dataset\.kmVisualTheme = theme;/,
  'Live visual-theme selection must be represented by a document dataset.'
);
assert.match(
  appearance,
  /root\.dataset\.kmTheme = preferences\.theme;/,
  'Accessibility colors must retain their independent document dataset.'
);
assert.match(
  appearance,
  /return stored === 'renegade' \|\| stored === 'royal' \|\| stored === 'sovereign' \|\| stored === 'arcane' \|\| stored === 'relic' \? stored : defaultVisualTheme;/,
  'Unknown stored values must safely resolve to Classic while all optional themes restore.'
);
assert.match(
  appearance,
  /setVisualTheme: \(theme\) => \{[\s\S]*?theme === 'classic' \|\| theme === 'renegade' \|\| theme === 'royal' \|\| theme === 'sovereign' \|\| theme === 'arcane' \|\| theme === 'relic'/,
  'The live visual-theme setter must accept exactly the six supported identities.'
);
assert.match(
  main,
  /applyStoredAppearancePreferences\(\);[\s\S]*?createRoot/,
  'Stored appearance and visual themes must apply before the first React render.'
);

assert.match(
  themePanel,
  /visualThemeOptions = \['classic', 'renegade', 'royal', 'sovereign', 'arcane', 'relic'\]/,
  'The Themes chooser must preserve the original row and append the three new themes.'
);
for (const [theme, assetName] of [
  ['classic', 'km-logo.png'],
  ['renegade', 'renegade-logo.png'],
  ['royal', 'royal-logo.png'],
  ['sovereign', 'sovereign-logo.png'],
  ['arcane', 'arcane-logo.png'],
  ['relic', 'relic-logo.png']
]) {
  assert.match(
    themePanel,
    new RegExp(
      `import\\s+${theme}ThemeIcon\\s+from\\s+['\"]\\.\\.\\/\\.\\.\\/assets\\/${assetName.replaceAll('.', '\\.')}['\"]`,
      'u'
    ),
    `${theme} must use its real KM logo asset in the Themes chooser.`
  );
  assert.match(
    themePanel,
    new RegExp(`${theme}:\\s*${theme}ThemeIcon`, 'u'),
    `${theme} must be wired to its imported chooser icon.`
  );
}
assert.match(
  themePanel,
  /<img[\s\S]*?alt=""[\s\S]*?className="visual-theme-option-icon"[\s\S]*?src=\{visualThemeIcons\[theme\]\}/,
  'Each theme option must render its real decorative icon without duplicating the visible theme name for assistive technology.'
);
assert.match(
  styles,
  /\.visual-theme-option-preview-royal\s*\{[\s\S]*?background:/,
  'The Themes chooser must include a polished Royal palette preview.'
);
assert.match(
  styles,
  /\.visual-theme-option-icon\s*\{[\s\S]*?width: 48px;[\s\S]*?height: 48px;[\s\S]*?object-fit: cover;/,
  'All real theme icons must use one bounded, consistently cropped chooser treatment.'
);
assert.match(
  themePanel,
  /role="radiogroup"[\s\S]*?aria-checked=\{isSelected\}[\s\S]*?role="radio"/,
  'The visual-theme chooser must expose an accessible single-selection contract.'
);
const usesNativeRadioNavigation = /<input[\s\S]*?type="radio"/u.test(themePanel);
const usesAuditableCustomRadioNavigation =
  /role="radio"/u.test(themePanel) &&
  /tabIndex=\{\s*isSelected\s*\?\s*0\s*:\s*-1\s*\}/u.test(themePanel) &&
  /onKeyDown=/u.test(themePanel) &&
  ['ArrowLeft', 'ArrowRight', 'Home', 'End'].every((key) =>
    new RegExp(`["']${key}["']`, 'u').test(themePanel)
  );
assert.ok(
  usesNativeRadioNavigation || usesAuditableCustomRadioNavigation,
  'The visual-theme chooser must use native radio navigation or an explicit roving-tabindex Arrow/Home/End contract.'
);
assert.match(
  themePanel,
  /onClick=\{\(\) => setVisualTheme\(theme\)\}/,
  'Theme options must invoke the live visual-theme setter.'
);
assert.match(
  app,
  /type SettingsTabId =[\s\S]*?\| 'themes'/,
  'Themes must have a dedicated Settings tab identity.'
);
assert.match(
  app,
  /\{ id: 'themes', icon: Palette, label: t\('settings\.themes\.title'\) \}/,
  'The Themes tab must use the existing palette icon and localized title.'
);
assert.match(
  app,
  /effectiveActiveSettingsTab === 'themes'[\s\S]*?role="tabpanel"[\s\S]*?\{themeSettings\}/,
  'Theme settings must be contained by their dedicated accessible tab panel.'
);

function requireExactPngAsset(name, relativePath, sha256, width, height, byteLength) {
  const suppliedLogo = readFileSync(new URL(relativePath, import.meta.url));
  assert.equal(
    createHash('sha256').update(suppliedLogo).digest('hex'),
    sha256,
    `The ${name} logo must remain the exact user-supplied image asset.`
  );
  assert.equal(
    suppliedLogo.subarray(0, 8).toString('hex'),
    '89504e470d0a1a0a',
    `The ${name} logo must be PNG.`
  );
  assert.equal(suppliedLogo.readUInt32BE(16), width, `The supplied ${name} logo width changed.`);
  assert.equal(suppliedLogo.readUInt32BE(20), height, `The supplied ${name} logo height changed.`);
  assert.equal(suppliedLogo.byteLength, byteLength, `The supplied ${name} logo byte count changed.`);
}

requireExactPngAsset(
  'Renegade',
  '../src/assets/renegade-logo.png',
  '7040fd7432d63ff3882c71b544eb176904fef17cbdf1b7ed95d18816771b5f8f',
  1254,
  1254,
  2474211
);
requireExactPngAsset(
  'Royal',
  '../src/assets/royal-logo.png',
  'a00c92f758919eaf16fe6e6ea713b00e7d97db44ee7ab3d643690219755ba621',
  1254,
  1254,
  3190846
);
assert.match(
  styles,
  /:root\[data-km-visual-theme='renegade'\] \.brand-logo,[\s\S]*?\.game-selection-logo \{\s*content: url\('\.\/assets\/renegade-logo\.png'\);\s*\}/,
  'Renegade must replace both KM logo surfaces with the exact supplied asset.'
);
assert.match(
  styles,
  /:root\[data-km-visual-theme='royal'\] \.brand-logo,[\s\S]*?\.game-selection-logo \{\s*content: url\('\.\/assets\/royal-logo\.png'\);\s*\}/,
  'Royal must replace both KM logo surfaces with the exact supplied asset.'
);

const baseThemeEnd = styles.indexOf(":root[data-km-type-scale='large']");
const classicBase = styles.slice(0, baseThemeEnd);
for (const declaration of [
  'background: #0b1120',
  '--color-bg: #070b12',
  '--color-canvas: #0b1120',
  '--color-surface: #121b2d',
  '--color-border: #26344d',
  '--color-text: #f4f7fb',
  '--color-accent: #4f46e5',
  '--color-accent-bright: #6d73ff',
  '--color-gold: #d6a842',
  '--color-focus: #ffd166'
]) {
  assert.ok(classicBase.includes(declaration), `Classic base styling lost ${declaration}.`);
}
assert.doesNotMatch(
  styles,
  /data-km-visual-theme='classic'/,
  'Classic must continue to use the unchanged base KM presentation rather than a divergent override.'
);

const renegadePaletteIndex = styles.indexOf(":root[data-km-visual-theme='renegade'] {");
const royalPaletteIndex = styles.indexOf(":root[data-km-visual-theme='royal'] {");
const highContrastPaletteIndex = styles.indexOf(":root[data-km-theme='highContrast'] {");
const colorSafePaletteIndex = styles.indexOf(":root[data-km-theme='colorSafe'] {");
assert.ok(renegadePaletteIndex > 0, 'Renegade must define a scoped semantic palette.');
assert.ok(royalPaletteIndex > 0, 'Royal must define a scoped semantic palette.');
assert.ok(
  renegadePaletteIndex < royalPaletteIndex &&
    royalPaletteIndex < highContrastPaletteIndex &&
    highContrastPaletteIndex < colorSafePaletteIndex,
  'Both visual palettes must precede and therefore yield to the accessibility palettes.'
);

const renegadePalette = styles.slice(renegadePaletteIndex, royalPaletteIndex);
for (const declaration of [
  '--color-bg: #08090a',
  '--color-canvas: #0b0c0e',
  '--color-surface: #141518',
  '--color-border: #2b2e34',
  '--color-text: #f6f3f4',
  '--color-text-muted: #aeb1b8',
  '--color-accent: #a4162d',
  '--color-accent-bright: #ff4d63',
  '--color-success: #68d9a0',
  '--color-warning: #efc76b',
  '--color-danger: #ff9388',
  '--color-focus: #ff7689'
]) {
  assert.ok(renegadePalette.includes(declaration), `Renegade palette lost ${declaration}.`);
}

requireContrast('Renegade primary text', '#f6f3f4', '#0b0c0e');
requireContrast('Renegade muted text', '#aeb1b8', '#141518');
requireContrast('Renegade accent action text', '#ffffff', '#a4162d');
requireContrast('Renegade focus indicator', '#ff7689', '#08090a', 3);
requireContrast('Renegade success state', '#68d9a0', '#141518');
requireContrast('Renegade warning state', '#efc76b', '#141518');
requireContrast('Renegade danger state', '#ff9388', '#141518');

const renegadeTreatmentIndex = styles.indexOf('/* Renegade visual treatment');
const royalTreatmentIndex = styles.indexOf('/* Royal visual treatment');
assert.ok(
  renegadeTreatmentIndex > colorSafePaletteIndex && royalTreatmentIndex > renegadeTreatmentIndex,
  'Renegade and Royal presentation overrides must remain separately scoped.'
);
const renegadeTreatment = styles.slice(renegadeTreatmentIndex, royalTreatmentIndex);
const renegadePrimaryHoverMatch = renegadeTreatment.match(
  /:is\(\.primary-button, \.purple-button\):hover[^}]*?color-mix\(in srgb, var\(--color-accent-bright\) (\d+)%, var\(--color-accent\)\)/u
);
assert.ok(
  renegadePrimaryHoverMatch,
  'Renegade primary actions must retain the audited accent hover mix.'
);
const renegadePrimaryHoverBrightPercentage = Number.parseInt(
  renegadePrimaryHoverMatch[1],
  10
);
assert.equal(
  renegadePrimaryHoverBrightPercentage,
  55,
  'Renegade primary hover must retain the reviewed 55% bright-accent mix.'
);
requireContrast(
  'Renegade primary hover text',
  '#ffffff',
  mixSrgbHex('#ff4d63', renegadePrimaryHoverBrightPercentage, '#a4162d')
);
for (const selector of [
  '.swsh-pokemon-summary-card',
  '.sv-pokemon-summary-card',
  '.za-pokemon-summary-card',
  '.pokemon-stat-total:not(.is-over-limit)',
  '.za-dex-layout-search',
  '.editable-field-group',
  '.move-player-damage-control',
  '.move-player-damage-launch-condition.is-when-reached',
  '.move-player-damage-launch-condition.is-when-reached\n  .move-player-damage-launch-condition-badge',
  '.move-player-damage-controller-availability-heading > svg',
  '.move-scripted-boss-owner-list li',
  '.move-scripted-boss-owner-list svg',
  '.text-category-tabs button.is-active',
  '.za-trainer-bulk-actions',
  '.npc-item-gift-tab.is-selected',
  ".game-dump-category-filters [aria-pressed='true']",
  ".spreadsheet-preview-filters [aria-pressed='true']",
  '.za-player-partner-title > svg',
  '.za-scripted-boss-status-locked',
  '.za-scripted-boss-phase-model-status:not(.is-unverified):not(.is-verified-none)',
  '.za-scripted-boss-phase-guide',
  '.za-scripted-boss-phase-stages > section > div > span',
  '.za-scripted-boss-action-list\n  li:not(.is-runtime-variant-normal):not(.is-runtime-variant-plus):not(.is-runtime-variant-boss)',
  '.za-scripted-boss-selection-compatibility:not(.is-base-verified):not(.is-gameplay-tested):not(.is-experimental)',
  '.za-encounter-advanced-data',
  '.settings-tabs'
]) {
  assert.ok(
    renegadeTreatment.includes(selector),
    `Renegade must replace the legacy Classic-blue editor surface ${selector}.`
  );
}
for (const protectedSemanticSelector of [
  '.type-chart-cell-immune',
  '.za-scripted-boss-actions,',
  '.za-scripted-boss-action-list li,'
]) {
  assert.ok(
    !renegadeTreatment
      .slice(renegadeTreatment.indexOf('Older editor cards'), renegadeTreatment.indexOf('/* Fieldset legends'))
      .includes(protectedSemanticSelector),
    `Renegade neutral-surface normalization must not override ${protectedSemanticSelector}.`
  );
}
for (const declaration of [
  'color-mix(in srgb, var(--color-accent) 7%, var(--color-surface-muted))',
  'border-color: color-mix(in srgb, var(--color-accent) 24%, var(--color-border))',
  'border-color: rgb(var(--color-accent-bright-rgb) / 38%)'
]) {
  assert.ok(
    renegadeTreatment.includes(declaration),
    `Renegade legacy editor surfaces lost ${declaration}.`
  );
}

const royalPalette = styles.slice(royalPaletteIndex, highContrastPaletteIndex);
for (const declaration of [
  'color-scheme: light',
  '--color-bg: #e8ddc7',
  '--color-canvas: #f4ecdc',
  '--color-surface: #fffaf0',
  '--color-border: #b89a62',
  '--color-border-strong: #806128',
  '--color-text: #251d12',
  '--color-text-muted: #665238',
  '--color-accent: #985b00',
  '--color-accent-bright: #d09112',
  '--color-gold: #8a5500',
  '--color-success: #21643f',
  '--color-warning: #865000',
  '--color-danger: #a12f3b',
  '--color-focus: #704000',
  '--color-on-accent: #fff'
]) {
  assert.ok(royalPalette.includes(declaration), `Royal palette lost ${declaration}.`);
}

requireContrast('Royal primary text', '#251d12', '#f4ecdc');
requireContrast('Royal muted text', '#665238', '#fffaf0');
requireContrast('Royal accent action text', '#ffffff', '#985b00');
requireContrast('Royal accent action hover text', '#ffffff', '#935700');
requireContrast('Royal gold detail text', '#8a5500', '#fffaf0');
requireContrast('Royal focus indicator', '#704000', '#e8ddc7', 3);
requireContrast('Royal success state', '#21643f', '#fffaf0');
requireContrast('Royal warning state', '#865000', '#fffaf0');
requireContrast('Royal danger state', '#a12f3b', '#fffaf0');

const royalColorSafeIndex = styles.indexOf(
  ":root[data-km-visual-theme='royal'][data-km-theme='colorSafe'] {"
);
assert.ok(
  royalColorSafeIndex > colorSafePaletteIndex,
  'Royal must deepen the generic color-safe semantic colors for its luminous surfaces.'
);
const royalColorSafePalette = styles.slice(
  royalColorSafeIndex,
  styles.indexOf('\n}', royalColorSafeIndex) + 2
);
for (const declaration of [
  '--color-accent: #075a8c',
  '--color-success: #006b4f',
  '--color-warning: #7a5100',
  '--color-danger: #a33e00',
  '--color-focus: #075a8c',
  '--color-on-accent: #fff'
]) {
  assert.ok(
    royalColorSafePalette.includes(declaration),
    `Royal color-safe palette lost ${declaration}.`
  );
}
requireContrast('Royal color-safe accent', '#075a8c', '#fffaf0');
requireContrast('Royal color-safe success', '#006b4f', '#fffaf0');
requireContrast('Royal color-safe warning', '#7a5100', '#fffaf0');
requireContrast('Royal color-safe danger', '#a33e00', '#fffaf0');

assert.match(
  styles,
  /:root:is\(\[data-km-visual-theme='renegade'\][^)]*\):not\(\[data-km-theme='highContrast'\]\) body/,
  'Renegade decorative presentation must yield to high-contrast mode.'
);
assert.match(
  styles,
  /:root\[data-km-visual-theme='royal'\]\[data-km-theme='highContrast'\] \{\s*color-scheme: dark;/,
  'Royal must switch native controls back to the dark color scheme when high contrast owns the palette.'
);
assert.match(
  styles,
  /:root\[data-km-visual-theme='royal'\]:not\(\[data-km-theme='highContrast'\]\) body/,
  'Royal decorative presentation must yield to high-contrast mode.'
);
assert.match(
  styles,
  /:root\[data-km-visual-theme='royal'\]:not\(\[data-km-theme='highContrast'\]\)[\s\S]*?:is\(\.primary-button, \.purple-button\):hover[\s\S]*?background: linear-gradient\(135deg, #7d4800, #935700\);/,
  'Royal hover actions must retain their reviewed deep-gold contrast instead of switching to decorative bright gold.'
);
assert.match(
  styles,
  /@media \(prefers-reduced-motion: reduce\) \{[\s\S]*?data-km-visual-theme='renegade'[\s\S]*?transition: none;/,
  'Renegade must honor the operating-system reduced-motion preference.'
);
assert.match(
  styles,
  /@media \(prefers-reduced-motion: reduce\) \{[\s\S]*?data-km-visual-theme='royal'[\s\S]*?transition: none;/,
  'Royal and its real chooser icon must honor the operating-system reduced-motion preference.'
);
assert.match(
  styles,
  /:root\[data-km-motion='reduce'\] \*[\s\S]*?animation-duration: 0\.01ms !important;[\s\S]*?transition-duration: 0\.01ms !important;/,
  'The explicit KM reduced-motion preference must continue to cover every visual theme.'
);
assert.match(
  styles,
  /@media \(forced-colors: active\) \{[\s\S]*?data-km-visual-theme='renegade'[\s\S]*?background: Canvas;[\s\S]*?border-color: CanvasText;/,
  'Renegade must preserve system colors in forced-colors mode.'
);
assert.match(
  styles,
  /@media \(forced-colors: active\) \{[\s\S]*?data-km-visual-theme='royal'[\s\S]*?background: Canvas;[\s\S]*?border-color: CanvasText;/,
  'Royal must preserve system colors in forced-colors mode.'
);

const localeCodes = ['de', 'en', 'es', 'fr', 'ru', 'uk', 'zh'];
const themeLocaleKeys = [
  'settings.themes.title',
  'settings.themes.description',
  'settings.themes.groupLabel',
  'settings.themes.classic',
  'settings.themes.classic.description',
  'settings.themes.renegade',
  'settings.themes.renegade.description',
  'settings.themes.royal',
  'settings.themes.royal.description',
  'settings.themes.sovereign',
  'settings.themes.sovereign.description',
  'settings.themes.arcane',
  'settings.themes.arcane.description',
  'settings.themes.relic',
  'settings.themes.relic.description',
  'settings.themes.liveNote'
];
for (const localeCode of localeCodes) {
  const locale = JSON.parse(read(`../src/localization/resources/${localeCode}.json`));
  for (const key of themeLocaleKeys) {
    assert.equal(
      typeof locale.keys[key],
      'string',
      `${localeCode} is missing visual-theme localization ${key}.`
    );
    assert.notEqual(locale.keys[key].trim(), '', `${localeCode} has an empty ${key} localization.`);
  }
  assert.equal(locale.keys['settings.themes.classic'], 'Classic');
  assert.equal(locale.keys['settings.themes.renegade'], 'Renegade');
  assert.equal(locale.keys['settings.themes.royal'], 'Royal');
}

for (const [theme, hash, size] of [
  ['sovereign', '058ff60309b9c67e802be2e19bdd50fed424f70900904129439e4744f4199670', 2605828],
  ['arcane', '4ea0eb322142c1ea006886e69d356a19ad4c1955359bd54381da70631c816665', 2700584],
  ['relic', 'a74a6f0e766cc63ab89067485bc138cebaa6cfcd7234685a0184ea0f0bf0aaa9', 2744547]
]) {
  requireExactPngAsset(theme, `../src/assets/${theme}-logo.png`, hash, 1254, 1254, size);
}

assert.match(styles, /\.visual-theme-options\s*\{[^}]*grid-template-columns: repeat\(3, minmax\(0, 1fr\)\)/u);
const typeChartStyles = read('../src/features/type-chart/typeChartTheme.css');
for (const theme of ['sovereign', 'arcane', 'relic']) {
  const index = styles.indexOf(`:root[data-km-visual-theme='${theme}'] {`);
  assert.ok(index > royalPaletteIndex && index < highContrastPaletteIndex);
  const palette = styles.slice(index, styles.indexOf('\n}', index));
  const hex = (token) => {
    const value = palette.match(new RegExp(`--color-${token}: (#[0-9a-f]{6});`, 'u'))?.[1];
    assert.ok(value, `${theme} must own ${token}.`);
    return value;
  };
  for (const token of ['text', 'text-muted', 'text-soft', 'text-subtle', 'accent-bright', 'gold', 'success', 'warning', 'danger']) {
    requireContrast(`${theme} ${token}`, hex(token), hex('surface'));
  }
  requireContrast(`${theme} action`, '#ffffff', hex('accent'));
  requireContrast(`${theme} action hover`, '#ffffff', hex('accent-hover'));
  requireContrast(`${theme} focus`, hex('focus'), hex('control'), 3);
  assert.ok(renegadeTreatment.includes(`[data-km-visual-theme='${theme}']`));
  assert.ok(styles.includes(`content: url('./assets/${theme}-logo.png')`));
  const chartIndex = typeChartStyles.indexOf(`:root[data-km-visual-theme='${theme}'] {`);
  const chartPalette = typeChartStyles.slice(chartIndex, typeChartStyles.indexOf('\n}', chartIndex));
  for (const state of ['immune', 'half', 'double']) {
    const value = (kind) => chartPalette.match(new RegExp(`--type-chart-${state}-${kind}: (#[0-9a-f]{6})`, 'u'))?.[1];
    requireContrast(`${theme} type chart ${state}`, value('text'), value('bg'));
  }
}

console.log('Visual-theme contract passed.');
