// SPDX-License-Identifier: GPL-3.0-only

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import * as lucide from 'lucide-react';
import ts from 'typescript';

const source = ts.createSourceFile(
  'capabilityRegistry.ts',
  readFileSync(new URL('../src/workbench/capabilityRegistry.ts', import.meta.url), 'utf8'),
  ts.ScriptTarget.Latest,
  true
);
const iconImports = new Map();
for (const statement of source.statements) {
  if (!ts.isImportDeclaration(statement) || statement.moduleSpecifier.text !== 'lucide-react') continue;
  const bindings = statement.importClause?.namedBindings;
  assert.ok(bindings && ts.isNamedImports(bindings), 'Editor icons must use named Lucide imports.');
  for (const specifier of bindings.elements) {
    if (!specifier.isTypeOnly) {
      iconImports.set(specifier.name.text, specifier.propertyName?.text ?? specifier.name.text);
    }
  }
}

function unwrap(expression) {
  while (ts.isAsExpression(expression) || ts.isSatisfiesExpression(expression) || ts.isParenthesizedExpression(expression)) {
    expression = expression.expression;
  }
  return expression;
}

const declarations = source.statements.filter(ts.isVariableStatement)
  .flatMap((statement) => statement.declarationList.declarations);
const registry = declarations.find((declaration) => declaration.name.getText(source) === 'workbenchCapabilityRegistry');
assert.ok(registry?.initializer, 'The editor capability registry must be present.');
const entries = unwrap(registry.initializer);
assert.ok(ts.isArrayLiteralExpression(entries), 'The editor capability registry must be an explicit list.');

const owners = new Map();
for (const entry of entries.elements) {
  const expression = unwrap(entry);
  const registration = ts.isCallExpression(expression) ? expression.arguments[0] : expression;
  assert.ok(registration && ts.isObjectLiteralExpression(registration), 'Each editor must declare its icon explicitly.');
  const fields = new Map(registration.properties.filter(ts.isPropertyAssignment)
    .map((property) => [property.name.getText(source), property.initializer]));
  const id = fields.get('id');
  const icon = fields.get('icon');
  assert.ok(id && ts.isStringLiteral(id) && icon && ts.isIdentifier(icon), 'Each editor must have a literal ID and named icon.');
  const exportName = iconImports.get(icon.text);
  const component = exportName && lucide[exportName];
  assert.ok(component, `Editor ${id.text} references an unavailable Lucide icon.`);
  // Aliases resolve to the same component and must not bypass uniqueness.
  assert.ok(!owners.has(component), `Editor ${id.text} reuses ${exportName} from ${owners.get(component)}. Choose a distinct icon.`);
  owners.set(component, id.text);
}

console.log(`Editor icon contract passed: ${owners.size} distinct assignments across all games.`);
