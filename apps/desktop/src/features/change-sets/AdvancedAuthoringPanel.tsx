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
import { useEffect, useMemo, useRef, useState } from 'react';
import {
  AdvancedAuthoringController,
  type AuthoringStagedCommitMetadata
} from '../../authoring/advancedAuthoringController';
import {
  AdvancedAuthoringError,
  advancedAuthoringMaximumSelectionCount,
  type AuthoringDraftSnapshot,
  type AuthoringOperationPreview,
  type AuthoringRelativeTransformKind,
  type AuthoringStagedHistoryExecutor,
  type AuthoringStageRequest,
  type AuthoringStagedSourceTransition,
  type AuthoringTransform
} from '../../authoring/advancedAuthoringTypes';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { SearchableOptionInput } from '../../components/SearchableOptionInput';
import { useCoalescedTextInputState } from '../../components/useCoalescedTextInputState';
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
  unavailableDraft?: {
    fieldCount: number;
    isDiscarding: boolean;
    onDiscard: () => void;
  } | null;
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
  revisionKey,
  unavailableDraft = null
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
  const [recordQuery, setRecordQuery] = useCoalescedTextInputState();
  const [recordOrder, setRecordOrder] = useState<'name' | 'identifier'>('name');
  const [sourceRecordKey, setSourceRecordKey] = useState('');
  const [pasteGroupId, setPasteGroupId] = useState('');
  const [storedPreview, setStoredPreview] = useState<{
    inputKey: string;
    value: AuthoringOperationPreview;
  } | null>(null);
  const [errorKey, setErrorKey] = useState<string | null>(null);
  const [isBusy, setIsBusy] = useState(false);
  const activeOperationRef = useRef<object | null>(null);
  const errorMessage = errorKey ? t(errorKey) : null;
  usePublishCommonEditorError({
    domain: 'workflow.changeSets',
    field: 'advancedAuthoring',
    message: errorMessage
  });

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
  const operationBusy = externalBusy || isBusy || unavailableDraft?.isDiscarding === true;
  const controlsBusy = operationBusy || unavailableDraft !== null;
  const filteredRecordResult = useMemo(() => {
    if (!workspace) return { records: [], total: 0 };
    const normalizedQuery = recordQuery.trim().toLocaleLowerCase(formatLocale);
    const matches = normalizedQuery
      ? workspace.records.filter((record) => (
          record.displayName.toLocaleLowerCase(formatLocale).includes(normalizedQuery) ||
          formatAuthoringRecordIdentifier(record.record)
            .toLocaleLowerCase(formatLocale)
            .includes(normalizedQuery)
        ))
      : [...workspace.records];
    matches.sort((left, right) => recordOrder === 'identifier'
      ? formatAuthoringRecordIdentifier(left.record).localeCompare(
          formatAuthoringRecordIdentifier(right.record)
        ) ||
        left.displayName.localeCompare(right.displayName)
      : left.displayName.localeCompare(right.displayName) ||
        formatAuthoringRecordIdentifier(left.record).localeCompare(
          formatAuthoringRecordIdentifier(right.record)
        ));
    return {
      records: matches.slice(0, maximumRenderedAuthoringRecords),
      total: matches.length
    };
  }, [formatLocale, recordOrder, recordQuery, workspace]);
  const sourceRecord = workspace?.records.find((record) => (
    semanticRecordRefKey(record.record) === sourceRecordKey
  )) ?? workspace?.records[0] ?? null;
  const sourceRecordOptions = useMemo(() => {
    if (!sourceRecord) return filteredRecordResult.records;
    const sourceKey = semanticRecordRefKey(sourceRecord.record);
    return filteredRecordResult.records.some((record) => (
      semanticRecordRefKey(record.record) === sourceKey
    ))
      ? filteredRecordResult.records
      : [sourceRecord, ...filteredRecordResult.records];
  }, [filteredRecordResult.records, sourceRecord]);
  const hasCompatibleClipboard = Boolean(
    snapshot.clipboard && snapshot.clipboard.adapterId === workspace?.adapterId
  );
  const hasCompatiblePasteGroup = Boolean(
    hasCompatibleClipboard &&
    pasteGroup?.fieldKeys.every((key) => Object.hasOwn(snapshot.clipboard!.fieldValues, key))
  );
  const operationInputKey = JSON.stringify([
    revisionKey ?? null,
    workspace?.adapterId ?? null,
    field?.fieldKey ?? null,
    transformKind,
    primaryValue,
    secondaryValue,
    rounding,
    [...selectedRecordKeys].sort(),
    sourceRecord ? semanticRecordRefKey(sourceRecord.record) : null,
    pasteGroup?.id ?? null,
    snapshot.clipboard ? [
      snapshot.clipboard.adapterId,
      snapshot.clipboard.copiedAtRevisionFingerprint,
      semanticRecordRefKey(snapshot.clipboard.sourceRecord),
      Object.entries(snapshot.clipboard.fieldValues).sort(([left], [right]) => (
        left.localeCompare(right)
      ))
    ] : null,
    snapshot.repeatTemplate ? [
      snapshot.repeatTemplate.adapterId,
      snapshot.repeatTemplate.createdAtRevisionFingerprint,
      snapshot.repeatTemplate.kind,
      snapshot.repeatTemplate.sourceRecord
        ? semanticRecordRefKey(snapshot.repeatTemplate.sourceRecord)
        : null,
      [...snapshot.repeatTemplate.fieldKeys].sort(),
      Object.entries(snapshot.repeatTemplate.sourceValues).sort(([left], [right]) => (
        left.localeCompare(right)
      )),
      snapshot.repeatTemplate.transform
    ] : null
  ]);
  const preview = storedPreview?.inputKey === operationInputKey
    ? storedPreview.value
    : null;
  const operationInputKeyRef = useRef(operationInputKey);
  const controllerRef = useRef(controller);
  const adapterIdRef = useRef(adapterId);
  const fieldKeyRef = useRef(fieldKey);
  const sourceRecordKeyRef = useRef(sourceRecordKey);
  const pasteGroupIdRef = useRef(pasteGroupId);
  adapterIdRef.current = adapterId;
  fieldKeyRef.current = fieldKey;
  sourceRecordKeyRef.current = sourceRecordKey;
  pasteGroupIdRef.current = pasteGroupId;

  useEffect(() => {
    if (operationInputKeyRef.current === operationInputKey) return;
    operationInputKeyRef.current = operationInputKey;
    setStoredPreview(null);
  }, [operationInputKey]);

  useEffect(() => {
    const nextWorkspaces = controller.getWorkspaces();
    const controllerChanged = controllerRef.current !== controller;
    controllerRef.current = controller;
    const nextWorkspace = controllerChanged
      ? nextWorkspaces[0]
      : nextWorkspaces.find(
          (candidate) => candidate.adapterId === adapterIdRef.current
        ) ?? nextWorkspaces[0];
    const nextFieldKey =
      !controllerChanged &&
      nextWorkspace?.fields.some(
        (candidate) => candidate.fieldKey === fieldKeyRef.current
      )
        ? fieldKeyRef.current
        : nextWorkspace?.fields[0]?.fieldKey ?? '';
    const nextSourceRecordKey =
      !controllerChanged &&
      nextWorkspace?.records.some(
        (record) => semanticRecordRefKey(record.record) === sourceRecordKeyRef.current
      )
        ? sourceRecordKeyRef.current
        : nextWorkspace?.records[0]
          ? semanticRecordRefKey(nextWorkspace.records[0].record)
          : '';
    const nextPasteGroupId =
      !controllerChanged &&
      nextWorkspace?.pasteSpecialGroups.some(
        (group) => group.id === pasteGroupIdRef.current
      )
        ? pasteGroupIdRef.current
        : nextWorkspace?.pasteSpecialGroups[0]?.id ?? '';
    setSnapshot(controller.getSnapshot());
    setAdapterId(nextWorkspace?.adapterId ?? '');
    setFieldKey(nextFieldKey);
    setSourceRecordKey(nextSourceRecordKey);
    setPasteGroupId(nextPasteGroupId);
    if (controllerChanged) {
      setRecordQuery('');
    }
    setStoredPreview(null);
    setErrorKey(null);
  }, [controller, revisionKey]);

  useEffect(() => {
    if (!workspace) return;
    if (!workspace.fields.some((candidate) => candidate.fieldKey === fieldKey)) {
      setFieldKey(workspace.fields[0]?.fieldKey ?? '');
      setStoredPreview(null);
    }
    if (!workspace.records.some((record) => (
      semanticRecordRefKey(record.record) === sourceRecordKey
    ))) {
      setSourceRecordKey(workspace.records[0]
        ? semanticRecordRefKey(workspace.records[0].record)
        : '');
      setStoredPreview(null);
    }
    if (!workspace.pasteSpecialGroups.some((group) => group.id === pasteGroupId)) {
      setPasteGroupId(workspace.pasteSpecialGroups[0]?.id ?? '');
      setStoredPreview(null);
    }
    const activeField = workspace.fields.find((candidate) => candidate.fieldKey === fieldKey);
    if (
      transformKind !== 'replace' &&
      !activeField?.supportedTransforms.includes(transformKind)
    ) {
      setTransformKind('replace');
      setStoredPreview(null);
    }
  }, [fieldKey, pasteGroupId, sourceRecordKey, transformKind, workspace]);

  if (workspaces.length === 0 || !workspace || !field) {
    return null;
  }

  const refreshSnapshot = () => setSnapshot(controller.getSnapshot());
  const setAuthoringBusy = (nextBusy: boolean) => {
    setIsBusy(nextBusy);
    onBusyChange?.(nextBusy);
  };
  const beginAuthoringOperation = () => {
    if (controlsBusy || activeOperationRef.current !== null) return null;
    const operation = {};
    activeOperationRef.current = operation;
    setAuthoringBusy(true);
    return operation;
  };
  const finishAuthoringOperation = (operation: object) => {
    if (activeOperationRef.current !== operation) return;
    activeOperationRef.current = null;
    setAuthoringBusy(false);
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
    run(() => setStoredPreview({
      inputKey: operationInputKey,
      value: operation()
    }));
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
    setStoredPreview(null);
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
      setStoredPreview(null);
    });
  };
  const handleCopyGroup = () => {
    if (!sourceRecord || !pasteGroup) return;
    run(() => {
      controller.copyPasteSpecialGroup(workspace.adapterId, sourceRecord.record, pasteGroup.id);
      setStoredPreview(null);
    });
  };
  const runDraftMutation = async (operation: () => AuthoringDraftSnapshot) => {
    const activeOperation = beginAuthoringOperation();
    if (!activeOperation) return;
    const priorDrafts = controller.getSnapshot().drafts;
    let didMutate = false;
    setErrorKey(null);
    try {
      const drafts = operation();
      didMutate = true;
      await onDraftsChange(drafts);
      setStoredPreview(null);
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
      finishAuthoringOperation(activeOperation);
    }
  };
  const handleApplyDrafts = () => {
    if (!preview) return;
    void runDraftMutation(() => controller.applyPreviewToDrafts(preview));
  };
  const handleStage = async () => {
    if (!preview) return;
    const activeOperation = beginAuthoringOperation();
    if (!activeOperation) return;
    setErrorKey(null);
    try {
      const priorDrafts = controller.getSnapshot().drafts;
      const request = controller.createStageRequest(preview);
      const metadata = await onStageRequest(request);
      controller.recordStagedCommit(preview, metadata, priorDrafts);
      setStoredPreview(null);
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
      finishAuthoringOperation(activeOperation);
    }
  };
  const runStagedHistory = async (direction: 'undo' | 'redo') => {
    const activeOperation = beginAuthoringOperation();
    if (!activeOperation) return;
    setErrorKey(null);
    try {
      if (direction === 'undo') await controller.undoStaged(executeStagedHistory);
      else await controller.redoStaged(executeStagedHistory);
      refreshSnapshot();
      setStoredPreview(null);
    } catch (error) {
      setErrorKey(toAuthoringErrorKey(error));
    } finally {
      finishAuthoringOperation(activeOperation);
    }
  };

  return (
    <section
      aria-busy={operationBusy || undefined}
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

      {errorMessage ? <p className="change-set-local-error" role="alert">{errorMessage}</p> : null}

      {unavailableDraft ? (
        <div className="change-set-profile-mismatch" role="alert">
          <div>
            <strong>{t('editorDrafts.summary.unavailable', { count: 1 })}</strong>
            <span>
              {t('changeSets.authoring.unavailableDraftHelp', {
                fieldCount: unavailableDraft.fieldCount
              })}
            </span>
          </div>
          <button
            className="danger-button compact-button"
            disabled={operationBusy}
            onClick={unavailableDraft.onDiscard}
            type="button"
          >
            {t('editorDrafts.discardUnavailable')}
          </button>
        </div>
      ) : null}

      <div className="change-set-authoring-grid">
        <section className="change-set-card change-set-authoring-selection">
          <div className="change-set-card-heading">
            <Layers3 aria-hidden="true" size={17} />
            <h3>{t('changeSets.authoring.selection')}</h3>
          </div>
          <label htmlFor="change-set-authoring-adapter">{t('changeSets.authoring.loadedEditor')}</label>
          <SearchableOptionInput
            ariaLabel={t('changeSets.authoring.loadedEditor')}
            data-km-source-site="change-set-authoring-adapter"
            disabled={false}
            id="change-set-authoring-adapter"
            isFiniteCatalog
            onChange={handleAdapterChange}
            options={workspaces.map((candidate) => ({
              label: t(
                authoringAdapterLabelKeys[candidate.adapterId]
                  ?? 'changeSets.authoring.loadedEditor'
              ),
              value: candidate.adapterId
            }))}
            value={workspace.adapterId}
          />
          <label htmlFor="change-set-authoring-record-search">
            {t('changeSets.authoring.searchRecords')}
          </label>
          <input
            id="change-set-authoring-record-search"
            maxLength={512}
            onChange={(event) => setRecordQuery(event.currentTarget.value)}
            placeholder={t('changeSets.authoring.searchPlaceholder')}
            type="search"
            value={recordQuery}
          />
          <label htmlFor="change-set-authoring-record-order">
            {t('analysisPresentation.controls.sort')}
          </label>
          <SearchableOptionInput
            ariaLabel={t('analysisPresentation.controls.sort')}
            data-km-source-site="change-set-authoring-record-order"
            disabled={false}
            id="change-set-authoring-record-order"
            isFiniteCatalog
            onChange={(value) => setRecordOrder(value as typeof recordOrder)}
            options={[
              { label: t('analysisPresentation.controls.record'), value: 'name' },
              { label: t('analysisPresentation.controls.identifier'), value: 'identifier' }
            ]}
            value={recordOrder}
          />
          <div className="change-set-authoring-records" role="group" aria-label={t('changeSets.authoring.records')}>
            {filteredRecordResult.records.map((record) => {
              const key = semanticRecordRefKey(record.record);
              return (
                <label data-localization-ignore="true" key={key}>
                  <input
                    checked={selectedRecordKeys.has(key)}
                    className="km-choice-control"
                    onChange={() => {
                      run(() => controller.toggleRecord(workspace.adapterId, record.record));
                      setStoredPreview(null);
                    }}
                    type="checkbox"
                  />
                  <span>
                    <strong>{record.displayName}</strong>
                    <small>{formatAuthoringRecordIdentifier(record.record)}</small>
                  </span>
                </label>
              );
            })}
          </div>
          <div className="change-set-authoring-selection-actions">
            <button
              className="secondary-button compact-button"
              disabled={controlsBusy || filteredRecordResult.records.length === 0 || (
                filteredRecordResult.records.every((record) => (
                  selectedRecordKeys.has(semanticRecordRefKey(record.record))
                ))
              )}
              onClick={() => {
                const combined = new Map(snapshot.selection.records.map((record) => [
                  semanticRecordRefKey(record),
                  record
                ]));
                for (const record of filteredRecordResult.records) {
                  if (combined.size >= advancedAuthoringMaximumSelectionCount) break;
                  combined.set(semanticRecordRefKey(record.record), record.record);
                }
                run(() => controller.selectRecords(workspace.adapterId, [...combined.values()]));
                setStoredPreview(null);
              }}
              type="button"
            >
              {t('analysisPresentation.controls.selectVisible')}
            </button>
            <button
              className="secondary-button compact-button"
              disabled={controlsBusy || selectedRecordKeys.size === 0}
              onClick={() => {
                run(() => controller.clearSelection());
                setStoredPreview(null);
              }}
              type="button"
            >
              {t('analysisPresentation.controls.clearSelection')}
            </button>
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
          <SearchableOptionInput
            ariaLabel={t('changeSets.authoring.field')}
            data-localization-ignore="true"
            data-km-source-site="change-set-authoring-field"
            disabled={false}
            id="change-set-authoring-field"
            isFiniteCatalog
            localizeOptions={false}
            onChange={(value) => {
              setFieldKey(value);
              setStoredPreview(null);
            }}
            options={workspace.fields.map((candidate) => ({
              label: candidate.label,
              value: candidate.fieldKey
            }))}
            value={field.fieldKey}
          />
          <label htmlFor="change-set-authoring-transform">{t('changeSets.authoring.transform')}</label>
          <SearchableOptionInput
            ariaLabel={t('changeSets.authoring.transform')}
            data-km-source-site="change-set-authoring-transform"
            disabled={false}
            id="change-set-authoring-transform"
            isFiniteCatalog
            onChange={(value) => {
              setTransformKind(value as typeof transformKind);
              setStoredPreview(null);
            }}
            options={[
              { label: t('changeSets.authoring.transform.replace'), value: 'replace' },
              ...field.supportedTransforms.map((transform) => ({
                label: t(`changeSets.authoring.transform.${transform}`),
                value: transform
              }))
            ]}
            value={transformKind}
          />
          <TransformInputs
            kind={transformKind}
            onPrimaryChange={(value) => {
              setPrimaryValue(value);
              setStoredPreview(null);
            }}
            onRoundingChange={(value) => {
              setRounding(value);
              setStoredPreview(null);
            }}
            onSecondaryChange={(value) => {
              setSecondaryValue(value);
              setStoredPreview(null);
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
          <SearchableOptionInput
            ariaLabel={t('changeSets.authoring.source')}
            data-localization-ignore="true"
            data-km-source-site="change-set-authoring-source"
            disabled={false}
            id="change-set-authoring-source"
            isFiniteCatalog
            localizeOptions={false}
            onChange={(value) => {
              setSourceRecordKey(value);
              setStoredPreview(null);
            }}
            options={sourceRecordOptions.map((record) => ({
              label: `${record.displayName} - ${formatAuthoringRecordIdentifier(record.record)}`,
              value: semanticRecordRefKey(record.record)
            }))}
            value={sourceRecord ? semanticRecordRefKey(sourceRecord.record) : ''}
          />
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
              <SearchableOptionInput
                ariaLabel={t('changeSets.authoring.pasteGroup')}
                data-km-source-site="change-set-authoring-paste-group"
                disabled={false}
                id="change-set-authoring-paste-group"
                isFiniteCatalog
                onChange={(value) => {
                  setPasteGroupId(value);
                  setStoredPreview(null);
                }}
                options={workspace.pasteSpecialGroups.map((group) => ({
                  label: t(
                    authoringPasteGroupLabelKeys[group.id]
                      ?? 'changeSets.authoring.pasteGroup'
                  ),
                  value: group.id
                }))}
                value={pasteGroup?.id ?? ''}
              />
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
        {unavailableDraft ? (
          <span>{t('editorDrafts.summary.unavailable', { count: 1 })}</span>
        ) : null}
        <span>{t(snapshot.clipboard
          ? 'changeSets.authoring.clipboardReady'
          : 'changeSets.authoring.clipboardEmpty')}</span>
      </div>
    </section>
  );
}

function TransformInputs({
  kind,
  onPrimaryChange,
  onRoundingChange,
  onSecondaryChange,
  primaryValue,
  rounding,
  secondaryValue
}: {
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
          <input onChange={(event) => onPrimaryChange(event.currentTarget.value)} type="number" value={primaryValue} />
        </label>
        <label>
          <span>{t('changeSets.authoring.maximum')}</span>
          <input onChange={(event) => onSecondaryChange(event.currentTarget.value)} type="number" value={secondaryValue} />
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
          onChange={(event) => onPrimaryChange(event.currentTarget.value)}
          step={kind === 'multiply' ? 'any' : '1'}
          type="number"
          value={primaryValue}
        />
      </label>
      {kind === 'multiply' ? (
        <div className="km-searchable-select-field change-set-authoring-value-field">
          <label htmlFor="change-set-authoring-rounding">
            <span>{t('changeSets.authoring.rounding')}</span>
          </label>
          <SearchableOptionInput
            ariaLabel={t('changeSets.authoring.rounding')}
            data-km-source-site="change-set-authoring-rounding"
            disabled={false}
            id="change-set-authoring-rounding"
            isFiniteCatalog
            onChange={(value) => onRoundingChange(value as typeof rounding)}
            options={[
              { label: t('changeSets.authoring.rounding.nearest'), value: 'nearest' },
              { label: t('changeSets.authoring.rounding.floor'), value: 'floor' },
              { label: t('changeSets.authoring.rounding.ceil'), value: 'ceil' }
            ]}
            value={rounding}
          />
        </div>
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
  const recordsByKey = new Map(workspaceRecords.map((record) => [
    semanticRecordRefKey(record.record),
    record
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
              ? <AuthoringRecordIdentity
                  displayName={recordsByKey.get(
                    semanticRecordRefKey(preview.assumptions.sourceRecord)
                  )?.displayName}
                  record={preview.assumptions.sourceRecord}
                />
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
      <div
        aria-label={t('changeSets.authoring.previewTitle')}
        className="change-set-authoring-preview-table-wrap"
        role="region"
        tabIndex={0}
      >
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
                  <AuthoringRecordIdentity
                    displayName={recordsByKey.get(
                      semanticRecordRefKey(mutation.field.record)
                    )?.displayName}
                    record={mutation.field.record}
                  />
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

function AuthoringRecordIdentity({
  displayName,
  record
}: {
  displayName?: string;
  record: SemanticRecordRef;
}) {
  return (
    <span className="change-set-authoring-record-identity">
      <strong>{displayName ?? record.recordId}</strong>
      <small>{formatAuthoringRecordIdentifier(record)}</small>
    </span>
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

function formatAuthoringRecordIdentifier(record: SemanticRecordRef) {
  return [
    record.gameFamily,
    record.domain,
    `${record.recordKind.key}@${record.recordKind.schemaVersion}`,
    record.recordId,
    record.subrecordId
  ].filter((value): value is string => Boolean(value)).join(' / ');
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
