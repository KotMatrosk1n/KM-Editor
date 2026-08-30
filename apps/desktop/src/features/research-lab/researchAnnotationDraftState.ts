// SPDX-License-Identifier: GPL-3.0-only

import type { ResearchAnnotationTarget } from '../../bridge/researchLabContracts';

export type ResearchAnnotationEditorDraft = {
  annotationId: string | null;
  tags: string;
  target: ResearchAnnotationTarget;
  text: string;
};

export type ResearchAnnotationEditorDrafts = Record<
  string,
  ResearchAnnotationEditorDraft
>;

export function setResearchAnnotationEditorDraft(
  drafts: ResearchAnnotationEditorDrafts,
  key: string,
  draft: ResearchAnnotationEditorDraft,
  source: Pick<ResearchAnnotationEditorDraft, 'tags' | 'text'>
) {
  if (draft.tags === source.tags && draft.text === source.text) {
    return removeResearchAnnotationEditorDraft(drafts, key);
  }

  const current = drafts[key];
  if (
    current?.annotationId === draft.annotationId &&
    current.tags === draft.tags &&
    current.target === draft.target &&
    current.text === draft.text
  ) {
    return drafts;
  }

  return { ...drafts, [key]: draft };
}

export function clearSavedResearchAnnotationEditorDraft(
  drafts: ResearchAnnotationEditorDrafts,
  key: string,
  savedDraft: ResearchAnnotationEditorDraft
) {
  const current = drafts[key];
  return current &&
    current.annotationId === savedDraft.annotationId &&
    current.tags === savedDraft.tags &&
    current.target === savedDraft.target &&
    current.text === savedDraft.text
    ? removeResearchAnnotationEditorDraft(drafts, key)
    : drafts;
}

export function discardResearchAnnotationEditorDraft(
  drafts: ResearchAnnotationEditorDrafts,
  key: string
) {
  return removeResearchAnnotationEditorDraft(drafts, key);
}

function removeResearchAnnotationEditorDraft(
  drafts: ResearchAnnotationEditorDrafts,
  key: string
) {
  if (!(key in drafts)) {
    return drafts;
  }

  const nextDrafts = { ...drafts };
  delete nextDrafts[key];
  return nextDrafts;
}
