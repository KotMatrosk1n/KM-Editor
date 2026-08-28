// SPDX-License-Identifier: GPL-3.0-only

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const desktopRoot = fileURLToPath(new URL('../', import.meta.url));

function read(relativePath) {
  return readFileSync(new URL(relativePath, `file:///${desktopRoot.replaceAll('\\', '/')}/`), 'utf8');
}

function ruleBody(css, selector) {
  const marker = `${selector} {`;
  const start = css.indexOf(marker);
  assert.notEqual(start, -1, `Missing focused editor CSS rule: ${selector}`);
  const bodyStart = start + marker.length;
  const end = css.indexOf('}', bodyStart);
  assert.notEqual(end, -1, `Unterminated focused editor CSS rule: ${selector}`);
  return css.slice(bodyStart, end);
}

function functionBody(source, name) {
  const marker = `function ${name}(`;
  const start = source.indexOf(marker);
  assert.notEqual(start, -1, `Missing focused editor helper: ${name}`);
  const nextFunction = source.indexOf('\nfunction ', start + marker.length);
  return source.slice(start, nextFunction === -1 ? source.length : nextFunction);
}

const focusedEditors = [
  {
    label: 'Trainer Pools',
    source: read('src/features/trainer-pools/TrainerPoolsSection.tsx')
  },
  {
    label: 'Fashion Catalog',
    source: read('src/features/fashion-catalog/FashionCatalogSection.tsx')
  }
];

for (const editor of focusedEditors) {
  assert.ok(
    [...editor.source.matchAll(/<FocusedEditorWorkspace className=/g)].length >= 2,
    `${editor.label} must keep both ready and unavailable states in the full-width workspace.`
  );
  assert.match(
    editor.source,
    /<FocusedEditorWorkspace className=/,
    `${editor.label} must use the full-width focused editor workspace primitive.`
  );
  assert.match(
    editor.source,
    /<FocusedEditorMetrics>/,
    `${editor.label} must use the responsive focused editor metrics primitive.`
  );
  assert.match(
    editor.source,
    /focused-editor-readable-copy/,
    `${editor.label} must scope readability limits to copy instead of its editor root.`
  );
}

const sharedComponent = read('src/components/FocusedEditorWorkspace.tsx');
assert.match(
  sharedComponent,
  /joinClassNames\('focused-editor-workspace'/,
  'FocusedEditorWorkspace must retain the shared layout contract class.'
);
assert.match(
  sharedComponent,
  /joinClassNames\('focused-editor-metrics'/,
  'FocusedEditorMetrics must retain the shared responsive metric class.'
);

const styles = read('src/styles.css');
const workspaceRule = ruleBody(styles, '.focused-editor-workspace');
for (const declaration of [
  'container: km-focused-editor / inline-size;',
  'grid-column: 1 / -1;',
  'inline-size: 100%;',
  'max-inline-size: none;',
  'min-inline-size: 0;'
]) {
  assert.ok(
    workspaceRule.includes(declaration),
    `Focused editor workspace must retain ${declaration}`
  );
}

const metricsRule = ruleBody(styles, '.focused-editor-metrics');
assert.ok(
  metricsRule.includes('repeat(auto-fit, minmax(min(100%, 13rem), 1fr))'),
  'Focused editor metrics must fill available width before wrapping.'
);
const readableCopyRule = ruleBody(styles, '.focused-editor-readable-copy');
assert.ok(
  readableCopyRule.includes('max-inline-size: 78ch;'),
  'Focused editor readability limits must remain scoped to copy, not the editor root.'
);
assert.match(
  styles,
  /\.focused-editor-workspace :where\(button\):not\(:disabled, \[aria-disabled='true'\]\) \{\s*cursor: pointer;/,
  'Focused editor actions must retain the standard interactive cursor.'
);
assert.match(
  styles,
  /@container km-focused-editor \(max-width: 56rem\)[\s\S]*?\.trainer-pools-selection-grid/,
  'Trainer Pools must respond to its actual editor width.'
);

const fashionStyles = read('src/features/fashion-catalog/FashionCatalogSection.css');
assert.match(
  fashionStyles,
  /@container km-focused-editor \(max-width: 56rem\)[\s\S]*?\.fashion-catalog-editor-grid/,
  'Fashion Catalog must respond to its actual editor width.'
);

const fashionSource = focusedEditors.find((editor) => editor.label === 'Fashion Catalog').source;
const fashionSearchText = functionBody(fashionSource, 'getSearchText');
const fashionRowSubtitle = functionBody(fashionSource, 'getRowSubtitle');
const fashionFieldDefinitions = functionBody(fashionSource, 'getFieldDefinitions');
assert.match(
  fashionSearchText,
  /'modelVariant' in row[\s\S]*?row\.modelVariant/,
  'Fashion Catalog dress-up model variants must remain searchable.'
);
assert.match(
  fashionFieldDefinitions,
  /\['modelVariant', 'option'\]/,
  'Fashion Catalog dress-up model variants must remain editable.'
);
assert.match(
  fashionRowSubtitle,
  /if \('modelVariant' in row\) \{\s*return null;\s*\}/,
  'Fashion Catalog dress-up rows must suppress raw model variants in browser subtitles.'
);
assert.doesNotMatch(
  fashionRowSubtitle,
  /row\.modelVariant/,
  'Fashion Catalog browser subtitles must not expose raw dress-up model variants.'
);
for (const usefulSubtitle of [
  /row\.shopIds\.join/,
  /return row\.modelPart/,
  /row\.labelKey \?\? row\.colorValue/
]) {
  assert.match(
    fashionRowSubtitle,
    usefulSubtitle,
    'Fashion Catalog must retain useful subtitles for non-dress-up-item rows.'
  );
}
assert.match(
  fashionSource,
  /\{subtitle \? <span>\{subtitle\}<\/span> : null\}/,
  'Fashion Catalog must omit the subtitle element when a row has no useful subtitle.'
);
