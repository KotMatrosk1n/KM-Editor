/* SPDX-License-Identifier: GPL-3.0-only */

import { z } from 'zod';
import catalog from './supportInventoryCatalog.json';

const count = z.number().int().nonnegative().max(Number.MAX_SAFE_INTEGER);
const status = z.enum(['available', 'not-configured', 'not-recorded', 'not-applicable', 'timeout', 'size-limit', 'access-denied', 'unsafe-path', 'invalid-data', 'io-error', 'unavailable']);
const key = (values: string[]) => z.string().max(128).refine((value) => values.includes(value));
const group = (values: string[]) => z.strictObject({ id: key(values), files: count, claims: count, records: count });

// Fixed shipped identifiers only. Metadata IDs, paths and edited values are never
// accepted as arbitrary report text, even from a compatible backend.
export const supportChangeInventorySchema = z.strictObject({
  schemaVersion: z.literal(1), source: z.literal('recorded-output-metadata'), verification: z.literal('game-files-not-checked'),
  ownershipStatus: status, files: count.nullable(), bytes: count.nullable(), runtimeMutableFiles: count.nullable(), otherScopeFiles: count.nullable(),
  owners: z.array(group(catalog.owners)).max(catalog.owners.length),
  domains: z.array(group(catalog.domains)).max(catalog.domains.length),
  historyStatus: status, retainedReceipts: count.max(1024).nullable(), historyLimit: z.literal(1024),
  receiptsWithoutDetails: count.max(1024).nullable(), omittedHistoryChanges: count.nullable(),
  history: z.array(z.strictObject({ id: key(catalog.domains), applies: count, changes: count, latestWriterFiles: count.nullable() })).max(catalog.domains.length),
  restorationStatus: status, restorationSelections: count.nullable(), removedSelections: count.nullable(), omittedRestorations: count,
  restorations: z.array(z.strictObject({
    species: count.min(1).max(1025), form: count.max(255), enabled: z.boolean(),
    assetRevision: count.min(1), currentAssetRevision: count.min(1), customized: z.boolean(), assetCount: count.max(1024), removedAssetCount: count.max(1024)
  })).max(468)
});
