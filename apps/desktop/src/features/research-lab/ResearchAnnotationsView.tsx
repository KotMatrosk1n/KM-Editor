/* SPDX-License-Identifier: GPL-3.0-only */

import { ExternalLink, MessageSquareText, RefreshCw, Trash2 } from 'lucide-react';
import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react';
import {
  researchLabMaximumAnnotationTags,
  researchLabMaximumAnnotationTextLength,
  researchAnnotationTargetIdentity,
  researchPortableCaseFold,
  researchRevisionIdentity,
  type ResearchAnnotation,
  type ResearchAnnotationDraft,
  type ResearchAnnotationTarget
} from '../../bridge/researchLabContracts';
import type {
  SemanticExploreRecordRef,
  SemanticExploreRevision
} from '../../bridge/semanticExploreContracts';
import {
  PublishCommonEditorError,
  usePublishCommonEditorError
} from '../../components/CommonEditorDiagnostics';
import { LoadingProgress } from '../../components/LoadingProgress';
import { getNextOutstandingEditorDraftKey } from '../../components/localEditorDraftState';
import { useLocalization } from '../../localization';
import {
  researchErrorKey,
  researchTargetKindKey
} from './researchLabPresentation';
import {
  clearSavedResearchAnnotationEditorDraft,
  discardResearchAnnotationEditorDraft,
  setResearchAnnotationEditorDraft,
  type ResearchAnnotationEditorDraft
} from './researchAnnotationDraftState';
import type { ResearchLabController } from './useResearchLabController';

const maximumTagEditorLength = researchLabMaximumAnnotationTags * 128 +
  (researchLabMaximumAnnotationTags - 1) * 2;

export function ResearchAnnotationsView({
  canNavigateRecord,
  controller,
  draftTarget,
  onClearDraftTarget,
  onDirtyStateChange,
  onNavigateRecord,
  revision
}: {
  canNavigateRecord: (record: SemanticExploreRecordRef) => boolean;
  controller: ResearchLabController;
  draftTarget: ResearchAnnotationTarget | null;
  onClearDraftTarget: () => void;
  onDirtyStateChange?: (isDirty: boolean) => void;
  onNavigateRecord: (record: SemanticExploreRecordRef) => void;
  revision: SemanticExploreRevision;
}) {
  const { t } = useLocalization();
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editorDrafts, setEditorDrafts] = useState<
    Record<string, ResearchAnnotationEditorDraft>
  >({});
  const [reviewedDraftKey, setReviewedDraftKey] = useState<string | null>(null);
  const editorDraftsRef = useRef(editorDrafts);
  const reviewFocusPendingRef = useRef(false);
  const annotations = controller.annotations.data?.document?.annotations ?? [];
  const editing = useMemo(() => (
    editingId ? annotations.find((annotation) => annotation.annotationId === editingId) ?? null : null
  ), [annotations, editingId]);
  const incomingTargetDraftKey = draftTarget ? targetDraftKey(draftTarget) : null;
  const draftKey = editingId
    ? annotationDraftKey(editingId)
    : reviewedDraftKey ?? incomingTargetDraftKey;
  const editorDraft = draftKey ? editorDrafts[draftKey] : undefined;
  const target = editorDraft?.target ?? editing?.target ?? (reviewedDraftKey ? null : draftTarget);
  const sourceTags = editing?.tags.join(', ') ?? '';
  const sourceText = editing?.text ?? '';
  const tags = editorDraft?.tags ?? sourceTags;
  const text = editorDraft?.text ?? sourceText;
  const targetIsCurrent = target !== null && isResearchTargetCurrent(target, revision, controller);

  useEffect(() => {
    if (incomingTargetDraftKey) {
      setEditingId(null);
      setReviewedDraftKey(null);
    }
  }, [incomingTargetDraftKey]);

  useEffect(() => {
    if (!reviewFocusPendingRef.current) {
      return;
    }

    reviewFocusPendingRef.current = false;
    const frame = requestAnimationFrame(() => {
      document.getElementById('research-annotation-text')?.focus({ preventScroll: true });
    });
    return () => cancelAnimationFrame(frame);
  }, [draftKey]);

  useEffect(() => {
    onDirtyStateChange?.(Object.keys(editorDrafts).length > 0);
  }, [editorDrafts, onDirtyStateChange]);

  useEffect(() => () => onDirtyStateChange?.(false), [onDirtyStateChange]);

  const parsedTags = tags.split(',')
    .map((tag) => tag.trim().normalize('NFC'))
    .filter(Boolean);
  const normalizedTagKeys = parsedTags.map(researchPortableCaseFold);
  const tagsInvalid = parsedTags.length > researchLabMaximumAnnotationTags ||
    new Set(normalizedTagKeys).size !== normalizedTagKeys.length;

  const beginEdit = (annotation: ResearchAnnotation) => {
    setEditingId(annotation.annotationId);
    setReviewedDraftKey(null);
    onClearDraftTarget();
  };
  const cancelEdit = () => {
    if (controller.annotations.isSaving) return;
    if (draftKey) {
      const nextDrafts = discardResearchAnnotationEditorDraft(
        editorDraftsRef.current,
        draftKey
      );
      editorDraftsRef.current = nextDrafts;
      setEditorDrafts(nextDrafts);
    }
    setEditingId(null);
    setReviewedDraftKey(null);
    onClearDraftTarget();
  };
  const updateEditorDraft = (patch: Partial<Pick<ResearchAnnotationEditorDraft, 'tags' | 'text'>>) => {
    if (!draftKey || !target) {
      return;
    }
    const nextDrafts = setResearchAnnotationEditorDraft(
      editorDraftsRef.current,
      draftKey,
      {
        annotationId: editingId,
        tags: patch.tags ?? tags,
        target,
        text: patch.text ?? text
      },
      { tags: sourceTags, text: sourceText }
    );
    editorDraftsRef.current = nextDrafts;
    setEditorDrafts(nextDrafts);
  };
  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (
      !draftKey ||
      !target ||
      !targetIsCurrent ||
      controller.annotations.isSaving ||
      controller.annotations.status !== 'ready'
    ) return;
    const stagedEditorDraft: ResearchAnnotationEditorDraft = {
      annotationId: editingId,
      tags,
      target,
      text
    };
    const draft: ResearchAnnotationDraft = {
      annotationId: editingId,
      tags: parsedTags,
      target,
      text: text.trim().normalize('NFC')
    };
    const didSave = await controller.upsertAnnotation(draft);
    if (!didSave) {
      return;
    }
    const latestDrafts = editorDraftsRef.current;
    const resolvedDrafts = clearSavedResearchAnnotationEditorDraft(
      latestDrafts,
      draftKey,
      stagedEditorDraft
    );
    const submittedDraftIsStillCurrent =
      latestDrafts[draftKey] === undefined || resolvedDrafts !== latestDrafts;
    editorDraftsRef.current = resolvedDrafts;
    setEditorDrafts(resolvedDrafts);
    if (!submittedDraftIsStillCurrent) {
      return;
    }
    setEditingId(null);
    setReviewedDraftKey(null);
    onClearDraftTarget();
  };
  const deleteAnnotation = async (annotationId: string) => {
    if (controller.annotations.isSaving) return;
    const deletedDraftKey = annotationDraftKey(annotationId);
    const submittedEditorDraft = editorDraftsRef.current[deletedDraftKey];
    const didDelete = await controller.deleteAnnotation(annotationId);
    if (!didDelete) {
      return;
    }
    const latestDrafts = editorDraftsRef.current;
    const resolvedDrafts = submittedEditorDraft
      ? clearSavedResearchAnnotationEditorDraft(
          latestDrafts,
          deletedDraftKey,
          submittedEditorDraft
        )
      : latestDrafts;
    const newerDraft = resolvedDrafts === latestDrafts
      ? latestDrafts[deletedDraftKey]
      : undefined;
    const nextDrafts = newerDraft
      ? {
          ...resolvedDrafts,
          [deletedDraftKey]: {
            ...newerDraft,
            annotationId: null
          }
        }
      : resolvedDrafts;
    editorDraftsRef.current = nextDrafts;
    setEditorDrafts(nextDrafts);
    if (editingId === annotationId) {
      setEditingId(null);
      setReviewedDraftKey(newerDraft ? deletedDraftKey : null);
      onClearDraftTarget();
    }
  };
  const outstandingDraftKeys = Object.keys(editorDrafts);
  const nextDraftKey = getNextOutstandingEditorDraftKey(outstandingDraftKeys, draftKey);
  const reviewNextDraft = () => {
    if (!nextDraftKey || controller.annotations.isSaving) {
      return;
    }

    const nextDraft = editorDraftsRef.current[nextDraftKey];
    if (!nextDraft) {
      return;
    }

    reviewFocusPendingRef.current = true;
    if (nextDraft.annotationId) {
      setEditingId(nextDraft.annotationId);
      setReviewedDraftKey(null);
    } else {
      setEditingId(null);
      setReviewedDraftKey(nextDraftKey);
    }
    onClearDraftTarget();
  };

  return (
    <section
      aria-labelledby="research-lab-tab-annotations"
      className="km-research-lab-panel"
      id="research-lab-panel-annotations"
      role="tabpanel"
    >
      <div className="km-research-lab-panel-heading">
        <div>
          <h3 id="research-annotations-title">{t('researchLab.annotations.title')}</h3>
          <p>{t('researchLab.annotations.description')}</p>
        </div>
        <button
          aria-busy={controller.annotations.status === 'loading' || undefined}
          className="secondary-button compact-button"
          disabled={controller.annotations.status === 'loading'}
          onClick={() => void controller.refreshAnnotations()}
          type="button"
        >
          <RefreshCw aria-hidden="true" size={14} />
          <span>{t(controller.annotations.status === 'loading'
            ? 'researchLab.annotations.loading'
            : 'researchLab.annotations.refresh')}</span>
        </button>
      </div>

      {controller.annotations.status === 'loading' && !controller.annotations.data ? (
        <Status messageKey="researchLab.annotations.loading" />
      ) : null}
      {controller.annotations.status === 'loading' && controller.annotations.data ? (
        <Status compact messageKey="researchLab.annotations.loading" />
      ) : null}
      {controller.annotations.error ? (
        <Status error messageKey={researchErrorKey(controller.annotations.error)} />
      ) : null}

      <div className="km-research-lab-annotation-actions">
        <span aria-live="polite" className="km-research-lab-help">
          {t('researchLab.annotations.draftSummary', {
            count: outstandingDraftKeys.length
          })}
        </span>
        {nextDraftKey ? (
          <button
            className="secondary-button compact-button"
            disabled={controller.annotations.isSaving}
            onClick={reviewNextDraft}
            type="button"
          >
            {t('researchLab.annotations.reviewNextDraft')}
          </button>
        ) : null}
      </div>

      {target ? (
        <form className="km-research-lab-annotation-form" onSubmit={submit}>
          <TargetSummary target={target} />
          <label>
            <span>{t('researchLab.annotations.text')}</span>
            <textarea
              id="research-annotation-text"
              maxLength={researchLabMaximumAnnotationTextLength}
              onChange={(event) => updateEditorDraft({ text: event.target.value })}
              placeholder={t('researchLab.annotations.textPlaceholder')}
              required
              value={text}
            />
          </label>
          <label>
            <span>{t('researchLab.annotations.tags')}</span>
            <input
              maxLength={maximumTagEditorLength}
              onChange={(event) => updateEditorDraft({ tags: event.target.value })}
              placeholder={t('researchLab.annotations.tagsPlaceholder')}
              type="text"
              value={tags}
            />
          </label>
          <p className="km-research-lab-help">{t('researchLab.annotations.privateHelp')}</p>
          {tagsInvalid ? (
            <>
              <PublishCommonEditorError
                domain="analysis.researchLab"
                field="annotationTags"
                message={t('researchLab.annotations.tagsInvalid', {
                  maximum: researchLabMaximumAnnotationTags
                })}
              />
              <p className="km-research-lab-inline-status" role="alert">
                {t('researchLab.annotations.tagsInvalid', {
                  maximum: researchLabMaximumAnnotationTags
                })}
              </p>
            </>
          ) : null}
          <div className="km-research-lab-annotation-actions">
            <button
              className="primary-button compact-button"
              disabled={
                controller.annotations.isSaving ||
                controller.annotations.status !== 'ready' ||
                !targetIsCurrent ||
                text.trim().length === 0 ||
                tagsInvalid
              }
              type="submit"
            >
              {controller.annotations.isSaving
                ? t('researchLab.annotations.saving')
                : t(editingId
                  ? 'researchLab.annotations.update'
                  : 'researchLab.annotations.save')}
            </button>
            <button
              className="secondary-button compact-button"
              disabled={controller.annotations.isSaving}
              onClick={cancelEdit}
              type="button"
            >
              {t('researchLab.annotations.cancel')}
            </button>
          </div>
          {controller.annotations.isSaving ? (
            <LoadingProgress
              className="is-compact"
              label={t('researchLab.annotations.saving')}
            />
          ) : null}
        </form>
      ) : (
        <p className="km-research-lab-empty">{t('researchLab.annotations.selectTarget')}</p>
      )}

      {annotations.length === 0 ? (
        <p className="km-research-lab-empty">{t('researchLab.annotations.empty')}</p>
      ) : (
        <ul aria-label={t('researchLab.annotations.list')} className="km-research-lab-results">
          {annotations.map((annotation) => {
            const isCurrent = isResearchTargetCurrent(annotation.target, revision, controller);
            const record = annotation.target.semanticRecord;
            const canNavigate = Boolean(isCurrent && record && canNavigateRecord(record));
            return (
              <li key={annotation.annotationId}>
                <article className="km-research-lab-card">
                  <header>
                    <div className="km-research-lab-card-title">
                      <MessageSquareText aria-hidden="true" size={17} />
                      <div>
                        <h4>{t(researchTargetKindKey(annotation.target.kind))}</h4>
                        <TargetSummary compact target={annotation.target} />
                      </div>
                    </div>
                    <span
                      className="km-research-lab-badge"
                      data-state={isCurrent ? 'complete' : 'unavailable'}
                    >
                      {t(isCurrent
                        ? 'researchLab.annotations.current'
                        : 'researchLab.annotations.stale')}
                    </span>
                  </header>
                  <p className="km-research-lab-annotation-text" data-localization-ignore="true">
                    {annotation.text}
                  </p>
                  {annotation.tags.length > 0 ? (
                    <div className="km-research-lab-badges" data-localization-ignore="true">
                      {annotation.tags.map((tag) => (
                        <span className="km-research-lab-badge" key={tag}>{tag}</span>
                      ))}
                    </div>
                  ) : null}
                  <div className="km-research-lab-card-actions">
                    {canNavigate ? (
                      <button
                        className="secondary-button compact-button"
                        onClick={() => onNavigateRecord(record!)}
                        type="button"
                      >
                        <ExternalLink aria-hidden="true" size={14} />
                        <span>{t('researchLab.annotations.openRecord')}</span>
                      </button>
                    ) : null}
                    <button
                      className="secondary-button compact-button"
                      disabled={
                        !isCurrent ||
                        controller.annotations.isSaving ||
                        controller.annotations.status !== 'ready'
                      }
                      onClick={() => beginEdit(annotation)}
                      type="button"
                    >
                      {t('researchLab.annotations.edit')}
                    </button>
                    <button
                      className="secondary-button compact-button"
                      disabled={
                        controller.annotations.isSaving ||
                        controller.annotations.status !== 'ready'
                      }
                      onClick={() => void deleteAnnotation(annotation.annotationId)}
                      type="button"
                    >
                      <Trash2 aria-hidden="true" size={14} />
                      <span>{t('researchLab.annotations.delete')}</span>
                    </button>
                  </div>
                  {!isCurrent ? (
                    <small>{t('researchLab.annotations.staleHelp')}</small>
                  ) : null}
                </article>
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}

function annotationDraftKey(annotationId: string) {
  return `annotation:${annotationId}`;
}

function targetDraftKey(target: ResearchAnnotationTarget) {
  return `target:${researchAnnotationTargetIdentity(target)}`;
}

function TargetSummary({
  compact = false,
  target
}: {
  compact?: boolean;
  target: ResearchAnnotationTarget;
}) {
  const { t } = useLocalization();
  let value = '';
  if (target.kind === 'semanticRecord') {
    const record = target.semanticRecord!;
    value = record.subrecordId
      ? `${record.domain} / ${record.recordId} / ${record.subrecordId}`
      : `${record.domain} / ${record.recordId}`;
  } else if (target.kind === 'relativeRange') {
    const range = target.relativeRange!;
    value = `${range.relativePath} @ ${range.offset} + ${range.length}`;
  } else {
    value = target.finding!.relativePath;
  }
  return compact ? (
    <p data-localization-ignore="true">{value}</p>
  ) : (
    <div className="km-research-lab-inline-status">
      <strong>{t('researchLab.annotations.targetLabel')}</strong>
      <p data-localization-ignore="true">{value}</p>
    </div>
  );
}

function isResearchTargetCurrent(
  target: ResearchAnnotationTarget,
  revision: SemanticExploreRevision,
  controller: ResearchLabController
) {
  if (researchRevisionIdentity(target.revision) !== researchRevisionIdentity(revision)) {
    return false;
  }
  if (target.kind === 'semanticRecord') {
    const expected = target.semanticSnapshot;
    return expected !== null && Boolean(controller.capabilities.data?.snapshots.some((snapshot) => (
      JSON.stringify(snapshot) === JSON.stringify(expected)
    )));
  }
  const comparison = controller.comparison.data;
  const sourcesCurrent = controller.sources.every((source) => (
    source.status === 'ready' &&
    source.data !== null &&
    Date.parse(source.data.expiresAtUtc) > Date.now()
  ));
  if (!comparison || !sourcesCurrent) return false;
  const expectedFingerprint = target.kind === 'finding'
    ? target.finding?.comparisonFingerprint
    : target.relativeRange?.comparisonFingerprint;
  return expectedFingerprint === comparison.comparisonFingerprint;
}

function Status({
  compact = false,
  error = false,
  messageKey
}: {
  compact?: boolean;
  error?: boolean;
  messageKey: string;
}) {
  const { t } = useLocalization();
  const errorMessage = error ? t(messageKey) : null;
  usePublishCommonEditorError({ domain: 'analysis.researchLab', message: errorMessage });
  if (!error) {
    return (
      <div className="km-research-lab-inline-status">
        <LoadingProgress className={compact ? 'is-compact' : undefined} label={t(messageKey)} />
      </div>
    );
  }
  return (
    <div
      aria-live="polite"
      className="km-research-lab-inline-status"
      role="alert"
    >
      <span>{errorMessage}</span>
    </div>
  );
}
