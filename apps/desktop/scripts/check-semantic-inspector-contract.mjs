// SPDX-License-Identifier: GPL-3.0-only

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

function read(relativePath) {
  return readFileSync(new URL(relativePath, import.meta.url), 'utf8').replace(/\r\n?/g, '\n');
}

const inspector = read('../src/features/semantic-explore/SemanticInspectorTabs.tsx');
assert.match(
  inspector,
  /const passiveSemanticInspectorRootKinds:[\s\S]*?'workflow\.items': 'item',[\s\S]*?'workflow\.moves': 'move',[\s\S]*?'workflow\.pokemon': 'pokemon-personal'/,
  'Passive semantic inspection must remain limited to the three canonical semantic root kinds.'
);
assert.match(
  inspector,
  /record\.gameFamily !== capabilities\.revision\.gameFamily[\s\S]*?record\.recordKind\.schemaVersion !== 1[\s\S]*?record\.subrecordId !== null[\s\S]*?passiveSemanticInspectorRootKinds\[record\.domain\] !== record\.recordKind\.key/,
  'Passive semantic inspection must reject another family, non-v1 kinds, subrecords, and noncanonical domain/kind pairs.'
);
for (const capabilityGate of [
  'provider.domains.includes(record.domain)',
  "provider.features.includes('entity')",
  "provider.coverage.state !== 'unavailable'",
  'provider.coverage.domains.includes(record.domain)'
]) {
  assert.ok(
    inspector.includes(capabilityGate),
    `Passive semantic inspection lost capability gate: ${capabilityGate}`
  );
}
assert.match(
  inspector,
  /if \(!stableRecord \|\| hasExactLoadedEntity\) return;[\s\S]*?controller\.ensureCapabilities\(\)\.then\(\(\) => \{[\s\S]*?controller\.getEntity\(stableRecord, layer\)/,
  'The inspector hook must issue an entity request only for the already-gated record supplied by its owner.'
);

const app = read('../src/App.tsx');
assert.match(
  app,
  /const canPopulateSemanticInspector = isPassiveSemanticInspectorRecordEligible\(\s*activeLocation\.entity \?\? null,\s*semanticExploreController\.capabilities\.data\s*\);/,
  'Workbench must capability-gate its passive semantic record before mounting inspector queries.'
);
assert.match(
  app,
  /record:\s*canPopulateSemanticInspector && semanticExploreController\.capabilities\.data\s*\? activeLocation\.entity \?\? null\s*: null/,
  'Unsupported Workbench records must reach the semantic inspector hook as null.'
);
assert.match(
  app,
  /<SemanticExploreSection[\s\S]*?controller=\{semanticExploreController\}/,
  'Explicit Semantic Explore must keep its direct controller behavior.'
);
