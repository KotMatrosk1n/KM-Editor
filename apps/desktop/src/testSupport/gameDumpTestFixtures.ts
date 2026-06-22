/* SPDX-License-Identifier: GPL-3.0-only */

import type { ProjectBridge } from '../bridge/projectBridge';

export function createGameDumpBridgeFixture(): Pick<
  ProjectBridge,
  'loadGameDumpWorkflow' | 'runGameDump'
> {
  return {
    loadGameDumpWorkflow: () =>
      Promise.resolve({
        workflow: {
          categories: [
            {
              defaultFormat: 'tsvAndJson',
              description: 'Item records, prices, TM data, categories, and provenance.',
              diagnostics: [],
              formats: ['tsv', 'csv', 'json', 'tsvAndJson'],
              id: 'items',
              isAvailable: true,
              kind: 'table',
              label: 'Items'
            },
            {
              defaultFormat: 'tsvAndJson',
              description: 'Pokemon personal data, evolutions, learnsets, compatibility, and provenance.',
              diagnostics: [],
              formats: ['tsv', 'csv', 'json', 'tsvAndJson'],
              id: 'pokemon',
              isAvailable: true,
              kind: 'table',
              label: 'Pokemon'
            }
          ],
          diagnostics: []
        }
      }),
    runGameDump: (request) =>
      Promise.resolve({
        result: {
          destinationFolder: request.destinationFolder,
          diagnostics: [],
          succeeded: true,
          writtenFiles: request.selections.map((selection) => ({
            categoryId: selection.categoryId,
            relativePath: `${selection.categoryId}/${selection.categoryId}.json`,
            sizeBytes: 128
          }))
        }
      })
  };
}
