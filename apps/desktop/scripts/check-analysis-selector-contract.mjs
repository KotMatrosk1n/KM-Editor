// SPDX-License-Identifier: GPL-3.0-only

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const desktopRoot = new URL('../', import.meta.url);

function read(relativePath) {
  return readFileSync(new URL(relativePath, desktopRoot), 'utf8')
    .replace(/\r\n?/gu, '\n');
}

const apiContract = read('../../src/KM.Api/GuidedDesign/GuidedDesignContracts.cs');
const provider = read('../../src/KM.Tools/Application/GuidedDesignProviders.cs');
const application = read('../../src/KM.Tools/Application/GuidedDesignApplicationService.cs');
const bridgeContract = read('src/bridge/guidedDesignContracts.ts');
const guidedSection = read('src/features/guided-design/GuidedDesignSection.tsx');
const semanticContract = read('src/bridge/semanticMergeContracts.ts');
const semanticSection = read('src/features/semantic-merge/SemanticMergeSection.tsx');
const workbenchStyles = read('src/features/workbench/workbench.css');

assert.match(
  apiContract,
  /GuidedDesignFieldCatalogDto\([\s\S]*?SelectionMode[\s\S]*?MinimumSelections[\s\S]*?FieldKeys/u,
  'Guided Design must expose a typed field-selection catalog.'
);
assert.match(
  application,
  /GuidedDesignProviders\.FieldCatalogs\([\s\S]*?capability\.State != SemanticCoverageStateDto\.Unavailable/u,
  'Guided Design must publish fields only for current-project available proposals.'
);
for (const catalog of [
  '["level"]',
  '["levelMin", "levelMax"]',
  '["probability"]',
  '["weight"]',
  '["price"]',
  '["buyPrice"]',
  '[.. EvFields]',
  '[.. StatFields]'
]) {
  assert.ok(provider.includes(catalog), `Missing verified Guided Design catalog ${catalog}.`);
}
assert.match(
  provider,
  /BuildEvolutionClamp\([\s\S]*?return Selection\([\s\S]*?\["level"\][\s\S]*?NormalizeInput\([\s\S]*?\["level"\],[\s\S]*?allowEvolutionPinChildren: true/u,
  'Evolution clamping must normalize the same fixed level field exposed by its catalog.'
);
assert.match(
  bridgeContract,
  /fieldCatalogs: z\.array\(guidedDesignFieldCatalogSchema\)[\s\S]*?exactly cover available proposals/u,
  'The desktop bridge must reject incomplete or extra Guided Design field catalogs.'
);
assert.match(
  bridgeContract,
  /trainerLevelAdjustment[\s\S]*?family === 'swordShield' \? null[\s\S]*?encounterWeightScale[\s\S]*?family === 'legendsZA' \? \['weight'\] : null[\s\S]*?evolutionLevelClamp[\s\S]*?family === 'legendsZA' \? \['level'\] : null[\s\S]*?trainerEvArchetype[\s\S]*?family === 'swordShield' \? null/u,
  'The desktop bridge must reject unsupported game/proposal field catalogs.'
);
assert.match(
  guidedSection,
  /function GuidedFieldSelector\([\s\S]*?<details>[\s\S]*?catalog\.fieldKeys\.map/u,
  'Guided Design must render the provider field catalog as a bounded selector.'
);
assert.doesNotMatch(
  guidedSection,
  /placeholder=\{t\('guidedDesign\.inputs\.fieldsPlaceholder'\)\}/u,
  'Guided Design must not require comma-separated field identifiers.'
);
assert.match(
  semanticSection,
  /eligibleFieldKeys = useMemo\([\s\S]*?capability\?\.domains[\s\S]*?fieldKeys=\{eligibleFieldKeys\}/u,
  'Semantic Merge field filters must come from complete capability catalogs, not one result page.'
);
assert.match(
  semanticContract,
  /kmRecipeCompatibilityStateValues = \[[\s\S]*?'compatible'[\s\S]*?'alreadyApplied'[\s\S]*?'conflict'[\s\S]*?'unsupported'/u,
  'Recipe compatibility filters must retain every contract-valid state.'
);
assert.match(
  workbenchStyles,
  /\.analysis-preparation-header strong \{[\s\S]*?color: var\(--color-text\);[\s\S]*?font-weight: 900;/u,
  'Analysis Tools must use the same strong title treatment as Settings data-cache headings.'
);

for (const locale of ['de', 'en', 'es', 'fr', 'ru', 'uk', 'zh']) {
  const resource = JSON.parse(read(`src/localization/resources/${locale}.json`)).keys;
  assert.ok(resource['analysisPreparation.title'], `${locale} is missing the Analysis Tools title.`);
  for (const key of [
    'guidedDesign.inputs.fieldsSelected',
    'guidedDesign.inputs.fieldsFixedHelp',
    'guidedDesign.inputs.fieldsSubsetHelp',
    'guidedDesign.field.level',
    'guidedDesign.field.levelMin',
    'guidedDesign.field.levelMax',
    'guidedDesign.field.probability',
    'guidedDesign.field.weight',
    'guidedDesign.field.price',
    'guidedDesign.field.buyPrice',
    'guidedDesign.field.hp',
    'guidedDesign.field.attack',
    'guidedDesign.field.defense',
    'guidedDesign.field.specialAttack',
    'guidedDesign.field.specialDefense',
    'guidedDesign.field.speed',
    'guidedDesign.field.evHp',
    'guidedDesign.field.evAttack',
    'guidedDesign.field.evDefense',
    'guidedDesign.field.evSpecialAttack',
    'guidedDesign.field.evSpecialDefense',
    'guidedDesign.field.evSpeed'
  ]) {
    assert.ok(resource[key], `${locale} is missing ${key}.`);
  }
}

console.log('Analysis selector contract passed.');
