// SPDX-License-Identifier: GPL-3.0-only
import { readFileSync } from 'node:fs';
import ts from 'typescript';

const root = new URL('../src/features/welcome/', import.meta.url);
const source = readFileSync(new URL('welcomeContentSchema.ts', root), 'utf8');
const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText
  .replaceAll("from 'zod'", `from '${import.meta.resolve('zod')}'`);
const { welcomeContentSchema } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`);
const result = welcomeContentSchema.safeParse(JSON.parse(readFileSync(new URL('content.json', root), 'utf8')));
if (!result.success) {
  for (const issue of result.error.issues) console.error(`Welcome content: ${issue.path.join('.')}: ${issue.message}`);
  process.exitCode = 1;
} else {
  console.log(`Welcome content valid: ${result.data.releases.length} releases, ${result.data.announcements.length} announcements.`);
}
