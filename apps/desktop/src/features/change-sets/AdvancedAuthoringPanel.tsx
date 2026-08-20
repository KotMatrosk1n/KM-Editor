/* SPDX-License-Identifier: GPL-3.0-only */

import {
  ClipboardCopy,
  ClipboardPaste,
  History,
  Layers3,
  Redo2,
  Repeat2,
  Sparkles,
  Undo2
} from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import {
  AdvancedAuthoringController,
  type AuthoringStagedCommitMetadata
} from '../../authoring/advancedAuthoringController';
import {
  AdvancedAuthoringError,
  type AuthoringDraftSnapshot,
  type AuthoringOperationPreview,
  type AuthoringRelativeTransformKind,
  type AuthoringStagedHistoryExecutor,
  type AuthoringStageRequest,
  type AuthoringStagedSourceTransition,
  type AuthoringTransform
} from '../../authoring/advancedAuthoringTypes';
import { useLocalization } from '../../localization';
import { semanticRecordRefKey, type SemanticRecordRef } from '../../workbench/semanticContracts';

export type AdvancedAuthoringPanelProps = {
  controller: AdvancedAuthoringController;
  executeStagedHistory: AuthoringStagedHistoryExecutor;
  externalBusy?: boolean;
  onBusyChange?: (isBusy: boolean) => void;
  onDraftsChange: (
    drafts: AuthoringDraftSnapshot,
    sourceTransition?: AuthoringStagedSourceTransition
  ) => Promise<void> | void;
  onStageRequest: (request: AuthoringStageRequest) => Promise<AuthoringStagedCommitMetadata>;
  revisionKey?: string;
};

const maximumRenderedPreviewMutations = 100;
const maximumRenderedAuthoringRecords = 200;

export function AdvancedAuthoringPanel({
  controller,
  executeStagedHistory,
  externalBusy = false,
  onBusyChange,
  onDraftsChange,
  onStageRequest,
  revisionKey
}: AdvancedAuthoringPanelProps) {
  const { formatLocale, t } = useLocalization();
  const workspaces = controller.getWorkspaces();
  const [snapshot, setSnapshot] = useState(() => controller.getSnapshot());
  const [adapterId, setAdapterId] = useState(workspaces[0]?.adapterId ?? '');
  const [fieldKey, setFieldKey] = useState(workspaces[0]?.fields[0]?.fieldKey ?? '');
  const [transformKind, setTransformKind] = useState<'replace' | AuthoringRelativeTransformKind>('replace');
  const [primaryValue, setPrimaryValue] = useState('');
  const [secondaryValue, setSecondaryValue] = useState('');
  const [rounding, setRounding] = useState<'ceil' | 'floor' | 'nearest'>('nearest');
  const [recordQuery, setRecordQuery] = useState('');
  const [sourceRecordKey, setSourceRecordKey] = useState('');
  const [pasteGroupId, setPasteGroupId] = useState('');
  const [preview, setPreview] = useState<AuthoringOperationPreview | null>(null);
  const [errorKey, setErrorKey] = useState<string | null>(null);
  const [isBusy, setIsBusy] = useState(false);

  const workspace = workspaces.find((candidate) => candidate.adapterId === adapterId)
    ?? workspaces[0]
    ?? null;
  const field = workspace?.fields.find((candidate) => candidate.fieldKey === fieldKey)
    ?? workspace?.fields[0]
    ?? null;
  const selectedRecordKeys = useMemo(
    () => new Set(snapshot.selection.records.map(semanticRecordRefKey)),
    [snapshot.selection.records]
  );
  const selectedRecords = workspace?.records
    .filter((record) => selectedRecordKeys.has(semanticRecordRefKey(record.record)))
    .map((record) => record.record) ?? [];
  const pasteGroup = workspace?.pasteSpecialGroups.find((group) => group.id === pasteGroupId)
    ?? workspace?.pasteSpecialGroups[0]
    ?? null;
  const controlsBusy = externalBusy || isBusy;
  const filteredRecordResult = useMemo(() => {
    if (!workspace) return { records: [], total: 0 };
    const normalizedQuery = recordQuery.trim().toLocaleLowerCase(formatLocale);
    const matches = normalizedQuery
      ? workspace.records.filter((record) => (
          record.displayName.toLocaleLowerCase(formatLocale).includes(normalizedQuery) ||
          record.record.recordId.toLocaleLowerCase(formatLocale).includes(normalizedQuery)
        ))
      : workspace.records;
    return {
      records: matches.slice(0, maximumRenderedAuthoringRecords),
      total: matches.length
    };
  }, [formatLocale, recordQuery, workspace]);
  const sourceRecord = filteredRecordResult.records.find((record) => (
    semanticRecordRefKey(record.record) === sourceRecordKey
  )) ?? filteredRecordResult.records[0] ?? null;
  const hasCompatibleClipboard = Boolean(
    snapshot.clipboard && snapshot.clipboard.adapterId === workspace?.adapterId
  );
  const hasCompatiblePasteGroup = Boolean(
    hasCompatibleClipboard &&
    pasteGroup?.fieldKeys.every((key) => Object.hasOwn(snapshot.clipboard!.fieldValues, key))
  );

  useEffect(() => {
    const nextWorkspaces = controller.getWorkspaces();
    const nextWorkspace = nextWorkspaces[0];
    setSnapshot(controller.getSnapshot());
    setAdapterId(nextWorkspace?.adapterId ?? '');
    setFieldKey(nextWorkspace?.fields[0]?.fieldKey ?? '');
    setSourceRecordKey(nextWorkspace?.records[0]
      ? semanticRecordRefKey(nextWorkspace.records[0].record)
      : '');
    setPasteGroupId(nextWorkspace?.pasteSpecialGroups[0]?.id ?? '');
    setRecordQuery('');
    setPreview(null);
    setErrorKey(null);
  }, [controller, revisionKey]);

  useEffect(() => {
    if (!workspace) return;
    if (!workspace.fields.some((candidate) => candidate.fieldKey === fieldKey)) {
      setFieldKey(workspace.fields[0]?.fieldKey ?? '');
    }
    if (!filteredRecordResult.records.some((record) => (
      semanticRecordRefKey(record.record) === sourceRecordKey
    ))) {
      setSourceRecordKey(filteredRecordResult.records[0]
        ? semanticRecordRefKey(filteredRecordResult.records[0].record)
        : '');
    }
    if (!workspace.pasteSpecialGroups.some((group) => group.id === pasteGroupId)) {
      setPasteGroupId(workspace.pasteSpecialGroups[0]?.id ?? '');
    }
    const activeField = workspace.fields.find((candidate) => candidate.fieldKey === fieldKey);
    if (
      transformKind !== 'replace' &&
      !activeField?.supportedTransforms.includes(transformKind)
    ) {
      setTransformKind('replace');
    }
  }, [fieldKey, filteredRecordResult.records, pasteGroupId, sourceRecordKey, transformKind, workspace]);

  if (workspaces.length === 0 || !workspace || !field) {
    return null;
  }

  const refreshSnapshot = () => setSnapshot(controller.getSnapshot());
  const setAuthoringBusy = (nextBusy: boolean) => {
    setIsBusy(nextBusy);
    onBusyChange?.(nextBusy);
  };
  const run = (operation: () => void) => {
    try {
      operation();
      setErrorKey(null);
      refreshSnapshot();
    } catch (error) {
      setErrorKey(toAuthoringErrorKey(error));
    }
  };
  const setOperationPreview = (operation: () => AuthoringOperationPreview) => {
    run(() => setPreview(operation()));
  };
  const handleAdapterChange = (nextAdapterId: string) => {
    run(() => controller.clearSelection());
    const nextWorkspace = workspaces.find((candidate) => candidate.adapterId === nextAdapterId);
    setAdapterId(nextAdapterId);
    setFieldKey(nextWorkspace?.fields[0]?.fieldKey ?? '');
    setSourceRecordKey(nextWorkspace?.records[0]
      ? semanticRecordRefKey(nextWorkspace.records[0].record)
      : '');
    setPasteGroupId(nextWorkspace?.pasteSpecialGroups[0]?.id ?? '');
    setRecordQuery('');
    setPreview(null);
  };
  const handleBulkPreview = () => {
    const transform = createTransform(
      transformKind,
      primaryValue,
      secondaryValue,
      rounding
    );
    if (!transform) {
      setErrorKey('changeSets.authoring.error.invalid-field-value');
      return;
    }
    setOperationPreview(() => controller.previewBulk({
      adapterId: workspace.adapterId,
      fieldKey: field.fieldKey,
      targetRecords: selectedRecords,
      transform
    }));
  };
  const handleCopyField = () => {
    if (!sourceRecord) return;
    run(() => {
      controller.copyFields(workspace.adapterId, sourceRecord.record, [field.fieldKey]);
      setPreview(null);
    });
  };
  const handleCopyGroup = () => {
    if (!sourceRecord || !pasteGroup) return;
    run(() => {
      controller.copyPasteSpecialGroup(workspace.adapterId, sourceRecord.record, pasteGroup.id);
      setPreview(null);
    });
  };
  const runDraftMutation = async (operation: () => AuthoringDraftSnapshot) => {
    if (controlsBusy) return;
    const priorDrafts = controller.getSnapshot().drafts;
    let didMutate = false;
    setAuthoringBusy(true);
    setErrorKey(null);
    try {
      const drafts = operation();
      didMutate = true;
      await onDraftsChange(drafts);
      setPreview(null);
      refreshSnapshot();
    } catch (error) {
      if (didMutate) {
        try {
          controller.hydrateDrafts(priorDrafts);
          setErrorKey('changeSets.authoring.error.persistenceRolledBack');
        } catch (rollbackError) {
          setErrorKey(toAuthoringErrorKey(rollbackError));
        }
      } else {
        setErrorKey(toAuthoringErrorKey(error));
      }
      refreshSnapshot();
    } finally {
      setAuthoringBusy(false);
    }
  };
  const handleApplyDrafts = () => {
    if (!preview) return;
    void runDraftMutation(() => controller.applyPreviewToDrafts(preview));
  };
  const handleStage = async () => {
    if (!preview || controlsBusy) return;
    setAuthoringBusy(true);
    setErrorKey(null);
    try {
      const priorDrafts = controller.getSnapshot().drafts;
      const request = controller.createStageRequest(preview);
      const metadata = await onStageRequest(request);
      controller.recordStagedCommit(preview, metadata, priorDrafts);
      setPreview(null);
      refreshSnapshot();
      try {
        await onDraftsChange(
          controller.getSnapshot().drafts,
          metadata.sourceTransition
        );
      } catch {
        setErrorKey('changeSets.authoring.error.persistence');
      }
    } catch (error) {
      setErrorKey(toAuthoringErrorKey(error));
    } finally {
      setAuthoringBusy(false);
    }
  };
  const runStagedHistory = async (direction: 'undo' | 'redo') => {
    if (controlsBusy) return;
    setAuthoringBusy(true);
    setErrorKey(null);
    try {
      if (direction === 'undo') await controller.undoStaged(executeStagedHistory);
      else await controller.redoStaged(executeStagedHistory);
      refreshSnapshot();
      setPreview(null);
    } catch (error) {
      setErrorKey(toAuthoringErrorKey(error));
    } finally {
      setAuthoringBusy(false);
    }
  };

  return (
    <section
      aria-busy={controlsBusy || undefined}
      aria-labelledby="advanced-authoring-heading"
      className="panel wide-panel change-set-authoring"
    >
      <header className="change-set-authoring-heading">
        <div>
          <Sparkles aria-hidden="true" size={19} />
          <div>
            <h2 id="advanced-authoring-heading">{t('changeSets.authoring.title')}</h2>
            <p>{t('changeSets.authoring.description')}</p>
          </div>
        </div>
        <div className="change-set-authoring-history">
          <button
            className="secondary-button compact-button"
            disabled={controlsBusy || !snapshot.canUndoDraft}
            onClick={() => void runDraftMutation(() => {
              controller.undoDraft();
              return controller.getSnapshot().drafts;
            })}
            type="button"
          >
            <Undo2 aria-hidden="true" size={15} />
            <span>{t('changeSets.authoring.undoDraft')}</span>
          </button>
          <button
            className="secondary-button compact-button"
            disabled={controlsBusy || !snapshot.canRedoDraft}
            onClick={() => void runDraftMutation(() => {
              controller.redoDraft();
              return controller.getSnapshot().drafts;
            })}
            type="button"
          >
            <Redo2 aria-hidden="true" size={15} />
            <span>{t('changeSets.authoring.redoDraft')}</span>
          </button>
          <button
            className="secondary-button compact-button"
            disabled={controlsBusy || !snapshot.canUndoStaged}
            onClick={() => void runStagedHistory('undo')}
            type="button"
          >
            <Undo2 aria-hidden="true" size={15} />
            <span>{t('changeSets.authoring.undoStaged')}</span>
          </button>
          <button
            className="secondary-button compact-button"
            disabled={controlsBusy || !snapshot.canRedoStaged}
            onClick={() => void runStagedHistory('redo')}
            type="button"
          >
            <Redo2 aria-hidden="true" size={15} />
            <span>{t('changeSets.authoring.redoStaged')}</span>
          </button>
        </div>
      </header>

      {errorKey ? <p className="change-set-local-error" role="alert">{t(errorKey)}</p> : null}

      <div className="change-set-authoring-grid">
        <section className="change-set-card change-set-authoring-selection">
          <div className="change-set-card-heading">
            <Layers3 aria-hidden="true" size={17} />
            <h3>{t('changeSets.authoring.selection')}</h3>
          </div>
          <label htmlFor="change-set-authoring-adapter">{t('changeSets.authoring.loadedEditor')}</label>
          <select
            disabled={controlsBusy}
            id="change-set-authoring-adapter"
            onChange={(event) => handleAdapterChange(event.currentTarget.value)}
            value={workspace.adapterId}
          >
            {workspaces.map((candidate) => (
              <option key={candidate.adapterId} value={candidate.adapterId}>
                {t(authoringAdapterLabelKeys[candidate.adapterId] ?? 'changeSets.authoring.loadedEditor')}
              </option>
            ))}
          </select>
          <label htmlFor="change-set-authoring-record-search">
            {t('changeSets.authoring.searchRecords')}
          </label>
          <input
            disabled={controlsBusy}
            id="change-set-authoring-record-search"
            maxLength={512}
            onChange={(event) => setRecordQuery(event.currentTarget.value)}
            placeholder={t('changeSets.authoring.searchPlaceholder')}
            type="search"
            value={recordQuery}
          />
          <div className="change-set-authoring-records" role="group" aria-label={t('changeSets.authoring.records')}>
            {filteredRecordResult.records.map((record) => {
              const key = semanticRecordRefKey(record.record);
              return (
                <label data-localization-ignore="true" key={key}>
                  <input
                    checked={selectedRecordKeys.has(key)}
                    disabled={controlsBusy}
                    onChange={() => {
                      run(() => controller.toggleRecord(workspace.adapterId, record.record));
                      setPreview(null);
                    }}
                    type="checkbox"
                  />
                  <span>{record.displayName}</span>
                </label>
              );
            })}
          </div>
          {filteredRecordResult.total > filteredRecordResult.records.length ? (
            <p>{t('changeSets.authoring.recordsTruncated', {
              shown: filteredRecordResult.records.length,
              total: filteredRecordResult.total
            })}</p>
          ) : null}
          <p>{t('changeSets.authoring.selectedCount', { count: selectedRecords.length })}</p>
        </section>

        <section className="change-set-card change-set-authoring-operation">
          <div className="change-set-card-heading">
            <Sparkles aria-hidden="true" size={17} />
            <h3>{t('changeSets.authoring.bulkTitle')}</h3>
          </div>
          <label htmlFor="change-set-authoring-field">{t('changeSets.authoring.field')}</label>
          <select
            disabled={controlsBusy}
            id="change-set-authoring-field"
            onChange={(event) => {
              setFieldKey(event.currentTarget.value);
              setPreview(null);
            }}
            value={field.fieldKey}
          >
            {workspace.fields.map((candidate) => (
              <option data-localization-ignore="true" key={candidate.fieldKey} value={candidate.fieldKey}>
                {candidate.label}
              </option>
            ))}
          </select>
          <label htmlFor="change-set-authoring-transform">{t('changeSets.authoring.transform')}</label>
          <select
            disabled={controlsBusy}
            id="change-set-authoring-transform"
            onChange={(event) => {
              setTransformKind(event.currentTarget.value as typeof transformKind);
              setPreview(null);
            }}
            value={transformKind}
          >
            <option value="replace">{t('changeSets.authoring.transform.replace')}</option>
            {field.supportedTransforms.map((transform) => (
              <option key={transform} value={transform}>
                {t(`changeSets.authoring.transform.${transform}`)}
              </option>
            ))}
          </select>
          <TransformInputs
            disabled={controlsBusy}
            kind={transformKind}
            onPrimaryChange={(value) => {
              setPrimaryValue(value);
              setPreview(null);
            }}
            onRoundingChange={(value) => {
              setRounding(value);
              setPreview(null);
            }}
            onSecondaryChange={(value) => {
              setSecondaryValue(value);
              setPreview(null);
            }}
            primaryValue={primaryValue}
            rounding={rounding}
            secondaryValue={secondaryValue}
          />
          <button
            className="primary-button compact-button"
            disabled={controlsBusy || selectedRecords.length === 0}
            onClick={handleBulkPreview}
            type="button"
          >
            {t('changeSets.authoring.previewBulk')}
          </button>
        </section>

        <section className="change-set-card change-set-authoring-copy">
          <div className="change-set-card-heading">
            <ClipboardCopy aria-hidden="true" size={17} />
            <h3>{t('changeSets.authoring.copyTitle')}</h3>
          </div>
          <label htmlFor="change-set-authoring-source">{t('changeSets.authoring.source')}</label>
          <select
            disabled={controlsBusy}
            id="change-set-authoring-source"
            onChange={(event) => {
              setSourceRecordKey(event.currentTarget.value);
              setPreview(null);
            }}
            value={sourceRecord ? semanticRecordRefKey(sourceRecord.record) : ''}
          >
            {filteredRecordResult.records.map((record) => (
              <option
                data-localization-ignore="true"
                key={semanticRecordRefKey(record.record)}
                value={semanticRecordRefKey(record.record)}
              >
                {record.displayName}
              </option>
            ))}
          </select>
          <div className="change-set-authoring-copy-actions">
            <button
              className="secondary-button compact-button"
              disabled={controlsBusy || !sourceRecord}
              onClick={handleCopyField}
              type="button"
            >
              <ClipboardCopy aria-hidden="true" size={15} />
              <span>{t('changeSets.authoring.copyField')}</span>
            </button>
            <button
              className="secondary-button compact-button"
              disabled={controlsBusy || !hasCompatibleClipboard || selectedRecords.length === 0}
              onClick={() => setOperationPreview(() => controller.previewMultiTargetCopy({
                adapterId: workspace.adapterId,
                targetRecords: selectedRecords
              }))}
              type="button"
            >
              <ClipboardPaste aria-hidden="true" size={15} />
              <span>{t('changeSets.authoring.previewPaste')}</span>
            </button>
          </div>
          {workspace.pasteSpecialGroups.length > 0 ? (
            <>
              <label htmlFor="change-set-authoring-paste-group">
                {t('changeSets.authoring.pasteGroup')}
              </label>
              <select
                disabled={controlsBusy}
                id="change-set-authoring-paste-group"
                onChange={(event) => {
                  setPasteGroupId(event.currentTarget.value);
                  setPreview(null);
                }}
                value={pasteGroup?.id ?? ''}
              >
                {workspace.pasteSpecialGroups.map((group) => (
                  <option key={group.id} value={group.id}>
                    {t(authoringPasteGroupLabelKeys[group.id] ?? 'changeSets.authoring.pasteGroup')}
                  </option>
                ))}
              </select>
              <div className="change-set-authoring-copy-actions">
                <button
                  className="secondary-button compact-button"
                  disabled={controlsBusy || !sourceRecord || !pasteGroup}
                  onClick={handleCopyGroup}
                  type="button"
                >
                  <ClipboardCopy aria-hidden="true" size={15} />
                  <span>{t('changeSets.authoring.copyGroup')}</span>
                </button>
                <button
                  className="secondary-button compact-button"
                  disabled={controlsBusy || !hasCompatiblePasteGroup || selectedRecords.length === 0}
                  onClick={() => setOperationPreview(() => controller.previewPasteSpecial({
                    adapterId: workspace.adapterId,
                    groupId: pasteGroup!.id,
                    targetRecords: selectedRecords
                  }))}
                  type="button"
                >
                  <ClipboardPaste aria-hidden="true" size={15} />
                  <span>{t('changeSets.authoring.previewPasteSpecial')}</span>
                </button>
              </div>
            </>
          ) : null}
          <button
            className="secondary-button compact-button"
            disabled={
              controlsBusy ||
              snapshot.repeatTemplate?.adapterId !== workspace.adapterId ||
              selectedRecords.length === 0
            }
            onClick={() => setOperationPreview(() => controller.previewRepeat(selectedRecords))}
            type="button"
          >
            <Repeat2 aria-hidden="true" size={15} />
            <span>{t('changeSets.authoring.previewRepeat')}</span>
          </button>
        </section>
      </div>

      <AuthoringPreview
        busy={controlsBusy}
        onApplyDrafts={handleApplyDrafts}
        onStage={() => void handleStage()}
        preview={preview}
        workspaceRecords={workspace.records}
      />

      <div className="change-set-authoring-summary" role="status">
        <History aria-hidden="true" size={16} />
        <span>{t('changeSets.authoring.draftCount', { count: snapshot.drafts.entries.length })}</span>
        <span>{t(snapshot.clipboard
          ? 'changeSets.authoring.clipboardReady'
          : 'changeSets.authoring.clipboardEmpty')}</span>
      </div>
    </section>
  );
}

function TransformInputs({
  disabled,
  kind,
  onPrimaryChange,
  onRoundingChange,
  onSecondaryChange,
  primaryValue,
  rounding,
  secondaryValue
}: {
  disabled: boolean;
  kind: 'replace' | AuthoringRelativeTransformKind;
  onPrimaryChange: (value: string) => void;
  onRoundingChange: (value: 'ceil' | 'floor' | 'nearest') => void;
  onSecondaryChange: (value: string) => void;
  primaryValue: string;
  rounding: 'ceil' | 'floor' | 'nearest';
  secondaryValue: string;
}) {
  const { t } = useLocalization();
  if (kind === 'clamp') {
    return (
      <div className="change-set-authoring-values">
        <label>
          <span>{t('changeSets.authoring.minimum')}</span>
          <input disabled={disabled} onChange={(event) => onPrimaryChange(event.currentTarget.value)} type="number" value={primaryValue} />
        </label>
        <label>
          <span>{t('changeSets.authoring.maximum')}</span>
          <input disabled={disabled} onChange={(event) => onSecondaryChange(event.currentTarget.value)} type="number" value={secondaryValue} />
        </label>
      </div>
    );
  }
  return (
    <div className="change-set-authoring-values">
      <label>
        <span>{t(kind === 'add'
          ? 'changeSets.authoring.amount'
          : kind === 'multiply'
            ? 'changeSets.authoring.factor'
            : 'changeSets.authoring.value')}</span>
        <input
          disabled={disabled}
          onChange={(event) => onPrimaryChange(event.currentTarget.value)}
          step={kind === 'multiply' ? 'any' : '1'}
          type="number"
          value={primaryValue}
        />
      </label>
      {kind === 'multiply' ? (
        <label>
          <span>{t('changeSets.authoring.rounding')}</span>
          <select disabled={disabled} onChange={(event) => onRoundingChange(event.currentTarget.value as typeof rounding)} value={rounding}>
            <option value="nearest">{t('changeSets.authoring.rounding.nearest')}</option>
            <option value="floor">{t('changeSets.authoring.rounding.floor')}</option>
            <option value="ceil">{t('changeSets.authoring.rounding.ceil')}</option>
          </select>
        </label>
      ) : null}
    </div>
  );
}

function AuthoringPreview({
  busy,
  onApplyDrafts,
  onStage,
  preview,
  workspaceRecords
}: {
  busy: boolean;
  onApplyDrafts: () => void;
  onStage: () => void;
  preview: AuthoringOperationPreview | null;
  workspaceRecords: readonly { displayName: string; record: SemanticRecordRef }[];
}) {
  const { t } = useLocalization();
  if (!preview) {
    return <p className="change-set-authoring-no-preview">{t('changeSets.authoring.noPreview')}</p>;
  }
  const names = new Map(workspaceRecords.map((record) => [
    semanticRecordRefKey(record.record),
    record.displayName
  ]));
  const visibleMutations = preview.mutations.slice(0, maximumRenderedPreviewMutations);
  return (
    <section aria-labelledby="change-set-authoring-preview-heading" className="change-set-card change-set-authoring-preview">
      <div className="change-set-card-heading">
        <Sparkles aria-hidden="true" size={17} />
        <h3 id="change-set-authoring-preview-heading">{t('changeSets.authoring.previewTitle')}</h3>
        <span>{t('changeSets.authoring.previewCount', { count: preview.mutations.length })}</span>
      </div>
      <p>{t(`changeSets.authoring.kind.${preview.kind}`)}</p>
      <dl className="change-set-authoring-assumptions">
        <div>
          <dt>{t('changeSets.authoring.sourceRecord')}</dt>
          <dd data-localization-ignore="true">
            {preview.assumptions.sourceRecord
              ? names.get(semanticRecordRefKey(preview.assumptions.sourceRecord))
                ?? preview.assumptions.sourceRecord.recordId
              : t('changeSets.authoring.none')}
          </dd>
        </div>
        <div>
          <dt>{t('changeSets.authoring.sourceFields')}</dt>
          <dd data-localization-ignore="true">
            {preview.assumptions.sourceFieldKeys.length > 0
              ? preview.assumptions.sourceFieldKeys.join(', ')
              : t('changeSets.authoring.none')}
          </dd>
        </div>
        <div>
          <dt>{t('changeSets.authoring.sourceValues')}</dt>
          <dd data-localization-ignore="true">
            {Object.keys(preview.assumptions.sourceValues).length > 0
              ? Object.entries(preview.assumptions.sourceValues)
                  .map(([key, value]) => `${key}: ${value}`)
                  .join(', ')
              : t('changeSets.authoring.none')}
          </dd>
        </div>
        <div>
          <dt>{t('changeSets.authoring.targetCount')}</dt>
          <dd data-localization-ignore="true">{preview.assumptions.targetCount}</dd>
        </div>
        <div>
          <dt>{t('changeSets.authoring.transformAssumption')}</dt>
          <dd>{formatTransform(preview.transform, t)}</dd>
        </div>
        <div>
          <dt>{t('changeSets.authoring.workspaceBinding')}</dt>
          <dd>
            {t('changeSets.authoring.workspaceBound')}{' '}
            <code data-localization-ignore="true">
              {shortFingerprint(preview.scope.sourceBinding?.workspaceFingerprint)}
            </code>
          </dd>
        </div>
        <div>
          <dt>{t('changeSets.authoring.revisionBinding')}</dt>
          <dd>
            {preview.scope.sourceBinding?.workspaceETag ? (
              <code data-localization-ignore="true">
                {shortFingerprint(preview.scope.sourceBinding.workspaceETag)}
              </code>
            ) : t('changeSets.authoring.revisionUnavailable')}
          </dd>
        </div>
      </dl>
      <div className="change-set-authoring-preview-table-wrap">
        <table className="change-set-authoring-preview-table">
          <thead>
            <tr>
              <th scope="col">{t('changeSets.authoring.record')}</th>
              <th scope="col">{t('changeSets.authoring.field')}</th>
              <th scope="col">{t('changeSets.authoring.before')}</th>
              <th scope="col">{t('changeSets.authoring.after')}</th>
            </tr>
          </thead>
          <tbody>
            {visibleMutations.map((mutation) => (
              <tr key={`${semanticRecordRefKey(mutation.field.record)}:${mutation.field.fieldKey}`}>
                <th data-localization-ignore="true" scope="row">
                  {names.get(semanticRecordRefKey(mutation.field.record)) ?? mutation.field.record.recordId}
                </th>
                <td data-localization-ignore="true">{mutation.field.fieldKey}</td>
                <td data-localization-ignore="true">{mutation.beforeValue}</td>
                <td data-localization-ignore="true">{mutation.afterValue}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {preview.mutations.length > visibleMutations.length ? (
        <p>{t('changeSets.authoring.previewTruncated', {
          shown: visibleMutations.length,
          total: preview.mutations.length
        })}</p>
      ) : null}
      <div className="change-set-authoring-preview-actions">
        <button className="secondary-button" disabled={busy} onClick={onApplyDrafts} type="button">
          {t('changeSets.authoring.applyDrafts')}
        </button>
        <button className="primary-button" disabled={busy} onClick={onStage} type="button">
          {t('changeSets.authoring.stage')}
        </button>
      </div>
      <p>{t('changeSets.authoring.confirmation')}</p>
    </section>
  );
}

function createTransform(
  kind: 'replace' | AuthoringRelativeTransformKind,
  primaryValue: string,
  secondaryValue: string,
  rounding: 'ceil' | 'floor' | 'nearest'
): AuthoringTransform | null {
  const primary = Number(primaryValue);
  if (!primaryValue.trim() || !Number.isFinite(primary)) return null;
  switch (kind) {
    case 'replace':
      return { kind, value: primary };
    case 'add':
      return { amount: primary, kind };
    case 'multiply':
      return { factor: primary, kind, rounding };
    case 'clamp': {
      const secondary = Number(secondaryValue);
      return secondaryValue.trim() && Number.isFinite(secondary) && primary <= secondary
        ? { kind, maximum: secondary, minimum: primary }
        : null;
    }
  }
}

function toAuthoringErrorKey(error: unknown) {
  return error instanceof AdvancedAuthoringError
    ? `changeSets.authoring.error.${error.code}`
    : 'changeSets.authoring.error.unexpected';
}

function formatTransform(
  transform: AuthoringTransform | null,
  t: (key: string, params?: Record<string, string | number>) => string
) {
  if (!transform) return t('changeSets.authoring.none');
  switch (transform.kind) {
    case 'replace':
      return t('changeSets.authoring.transformSummary.replace', { value: transform.value });
    case 'add':
      return t('changeSets.authoring.transformSummary.add', { value: transform.amount });
    case 'multiply':
      return t('changeSets.authoring.transformSummary.multiply', {
        rounding: t(`changeSets.authoring.rounding.${transform.rounding}`),
        value: transform.factor
      });
    case 'clamp':
      return t('changeSets.authoring.transformSummary.clamp', {
        maximum: transform.maximum,
        minimum: transform.minimum
      });
  }
}

function shortFingerprint(value: string | null | undefined) {
  return value ? value.slice(0, 12) : 'unavailable';
}

const authoringAdapterLabelKeys: Readonly<Record<string, string>> = {
  'items.scalar.v1': 'changeSets.authoring.adapter.items',
  'moves.core.v1': 'changeSets.authoring.adapter.moves',
  'pokemon.personal.v1': 'changeSets.authoring.adapter.pokemon',
  'trainers.party.v1': 'changeSets.authoring.adapter.trainers'
};

const authoringPasteGroupLabelKeys: Readonly<Record<string, string>> = {
  abilities: 'changeSets.authoring.group.abilities',
  'base-stats': 'changeSets.authoring.group.baseStats',
  'core-stats': 'changeSets.authoring.group.coreStats',
  'effect-chances': 'changeSets.authoring.group.effectChances',
  evs: 'changeSets.authoring.group.evs',
  'held-item': 'changeSets.authoring.group.heldItem',
  ivs: 'changeSets.authoring.group.ivs',
  moves: 'changeSets.authoring.group.moves',
  traits: 'changeSets.authoring.group.traits',
  types: 'changeSets.authoring.group.types'
};
