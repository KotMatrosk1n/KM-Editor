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
  const { version } = JSON.parse(readFileSync(new URL('../package.json', import.meta.url), 'utf8'));
  const release = result.data.releases.find(entry => entry.version === version);
  if (!release) {
    console.error(`Welcome content: missing notes for application version ${version}.`);
    process.exitCode = 1;
  } else {
    const comparison = release.links.find(link => link.label.en === 'Full changelog');
    const expected = ['## Highlights', '', release.summary.en, ''];
    for (const section of release.sections) {
      expected.push(`## ${section.title.en}`, '', ...section.items.map(item => `* ${item.body.en}`), '');
    }
    expected.push(`**Full Changelog**: ${comparison?.url ?? ''}`, '');
    try {
      const notes = readFileSync(new URL(`../../../docs/release-notes/${version}.md`, import.meta.url), 'utf8');
      if (!comparison || notes.replaceAll('\r\n', '\n').trim() !== expected.join('\n').trim()) {
        throw new Error('The bundled English notes and curated release changelog must match.');
      }
      console.log(`Welcome release notes match the ${version} changelog.`);
    } catch {
      console.error(`Welcome content: missing or mismatched docs/release-notes/${version}.md.`);
      process.exitCode = 1;
    }
  }
}
