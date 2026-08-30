// SPDX-License-Identifier: GPL-3.0-only

export type FashionCatalogDraftValues = Record<string, string>;

export function createFashionCatalogDraftKey(
  catalogFile: string,
  physicalRowId: string,
  field: string
) {
  return JSON.stringify([catalogFile, physicalRowId, field]);
}

export function setFashionCatalogDraftValue(
  drafts: FashionCatalogDraftValues,
  key: string,
  value: string,
  sourceValue: string
) {
  if (value === sourceValue) {
    return removeFashionCatalogDraftValue(drafts, key);
  }

  if (drafts[key] === value) {
    return drafts;
  }

  return { ...drafts, [key]: value };
}

export function clearStagedFashionCatalogDraftValue(
  drafts: FashionCatalogDraftValues,
  key: string,
  stagedValue: string
) {
  return drafts[key] === stagedValue
    ? removeFashionCatalogDraftValue(drafts, key)
    : drafts;
}

function removeFashionCatalogDraftValue(
  drafts: FashionCatalogDraftValues,
  key: string
) {
  if (!(key in drafts)) {
    return drafts;
  }

  const nextDrafts = { ...drafts };
  delete nextDrafts[key];
  return nextDrafts;
}
