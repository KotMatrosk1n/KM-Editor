/* SPDX-License-Identifier: GPL-3.0-only */

import {
  semanticFieldRefKey,
  semanticRecordRefKey,
  type SemanticFieldRef,
  type SemanticRecordRef
} from '../workbench/semanticContracts';
import {
  AdvancedAuthoringError,
  advancedAuthoringMaximumClipboardFieldCount,
  advancedAuthoringMaximumHistoryCount,
  advancedAuthoringMaximumMutationCount,
  advancedAuthoringMaximumSelectionCount,
  type AdvancedAuthoringControllerSnapshot,
  type AdvancedAuthoringScope,
  type AdvancedAuthoringScopeUpdateResult,
  type AdvancedAuthoringSourceBinding,
  type AuthoringClipboard,
  type AuthoringDomainWorkspace,
  type AuthoringDraftSnapshot,
  type AuthoringDraftEntry,
  type AuthoringFieldDescriptor,
  type AuthoringFieldMutation,
  type AuthoringOperationKind,
  type AuthoringOperationPreview,
  type AuthoringRecordSnapshot,
  type AuthoringRepeatTemplate,
  type AuthoringSelectionSnapshot,
  type AuthoringStageRequest,
  type AuthoringStagedCommitMetadata,
  type AuthoringStagedHistoryExecutor,
  type AuthoringStagedHistoryState,
  type AuthoringTransform
} from './advancedAuthoringTypes';
import { getAdvancedAuthoringAdapter } from './authoringAdapterRegistry';

export type { AuthoringStagedCommitMetadata } from './advancedAuthoringTypes';

type DraftHistoryChange = {
  after: AuthoringDraftEntry | null;
  before: AuthoringDraftEntry | null;
  key: string;
};

type DraftHistoryEvent = {
  changes: readonly DraftHistoryChange[];
};

export type AdvancedAuthoringControllerOptions = {
  historyLimit?: number;
  mutationLimit?: number;
  scope: AdvancedAuthoringScope;
  selectionLimit?: number;
  workspaces: readonly AuthoringDomainWorkspace[];
};

export type AuthoringPreviewOptions = {
  adapterId: string;
  targetRecords?: readonly SemanticRecordRef[];
};

export type AuthoringBulkPreviewOptions = AuthoringPreviewOptions & {
  fieldKey: string;
  transform: AuthoringTransform;
};

export type AuthoringCopyPreviewOptions = AuthoringPreviewOptions & {
  fieldKeys?: readonly string[];
};

export type AuthoringPasteSpecialPreviewOptions = AuthoringPreviewOptions & {
  groupId: string;
};

export class AdvancedAuthoringController {
  private clipboard: AuthoringClipboard | null = null;
  private readonly draftEntries = new Map<string, AuthoringDraftEntry>();
  private readonly draftRedo: DraftHistoryEvent[] = [];
  private readonly draftUndo: DraftHistoryEvent[] = [];
  private readonly historyLimit: number;
  private readonly mutationLimit: number;
  private repeatTemplate: AuthoringRepeatTemplate | null = null;
  private scope: AdvancedAuthoringScope;
  private readonly selectedRecords = new Map<string, SemanticRecordRef>();
  private selectionAdapterId: string | null = null;
  private readonly selectionLimit: number;
  private stagedHistory: AuthoringStagedHistoryState | null = null;
  private workspaces = new Map<string, AuthoringDomainWorkspace>();

  public constructor(options: AdvancedAuthoringControllerOptions) {
    this.historyLimit = options.historyLimit ?? advancedAuthoringMaximumHistoryCount;
    this.mutationLimit = options.mutationLimit ?? advancedAuthoringMaximumMutationCount;
    this.selectionLimit = options.selectionLimit ?? advancedAuthoringMaximumSelectionCount;
    assertPositiveBound(this.historyLimit, advancedAuthoringMaximumHistoryCount);
    assertPositiveBound(this.mutationLimit, advancedAuthoringMaximumMutationCount);
    assertPositiveBound(this.selectionLimit, advancedAuthoringMaximumSelectionCount);
    validateScope(options.scope);
    this.scope = options.scope;
    this.workspaces = createWorkspaceMap(this.scope, options.workspaces);
  }

  public getWorkspaces() {
    return [...this.workspaces.values()];
  }

  public getSnapshot(): AdvancedAuthoringControllerSnapshot {
    return {
      canRedoDraft: this.draftRedo.length > 0,
      canRedoStaged: this.stagedHistory?.canRedo ?? false,
      canUndoDraft: this.draftUndo.length > 0,
      canUndoStaged: this.stagedHistory?.canUndo ?? false,
      clipboard: this.clipboard,
      drafts: {
        entries: sortDraftEntries([...this.draftEntries.values()]),
        schemaVersion: 1,
        scope: this.scope
      },
      repeatTemplate: this.repeatTemplate,
      selection: this.getSelection(),
      stagedHistory: this.stagedHistory,
      stagedHistoryRevision: this.stagedHistory?.changeSetETag ?? null
    };
  }

  public hydrateDrafts(snapshot: AdvancedAuthoringControllerSnapshot['drafts']) {
    if (
      snapshot.schemaVersion !== 1 ||
      !scopeCompositionEqual(snapshot.scope, this.scope) ||
      snapshot.entries.length > this.mutationLimit
    ) {
      throw new AdvancedAuthoringError('invalid-scope');
    }
    const nextEntries = new Map<string, AuthoringDraftEntry>();
    const mutationsByAdapter = new Map<string, AuthoringFieldMutation[]>();
    for (const entry of snapshot.entries) {
      const workspace = this.findWorkspaceForRecord(entry.field.record);
      const record = requireRecord(workspace, entry.field.record);
      const field = requireField(workspace, entry.field.fieldKey);
      validateFieldValue(field, entry.value);
      const baseline = requireRecordFieldValue(record, field.fieldKey);
      if (entry.value === baseline) {
        continue;
      }
      const key = semanticFieldRefKey(entry.field);
      if (nextEntries.has(key)) {
        throw new AdvancedAuthoringError('invalid-field-value');
      }
      nextEntries.set(key, entry);
      const mutations = mutationsByAdapter.get(workspace.adapterId) ?? [];
      mutationsByAdapter.set(workspace.adapterId, [
        ...mutations,
        createMutation(record.record, field, baseline, entry.value)
      ]);
    }
    for (const [adapterId, mutations] of mutationsByAdapter) {
      validateProjectedMutations(
        this.requireWorkspace(adapterId),
        mutations,
        (record, fieldKey) =>
          nextEntries.get(semanticFieldRefKey(createFieldRef(record.record, fieldKey)))
            ?.value ?? record.fieldValues[fieldKey]
      );
    }
    this.draftEntries.clear();
    for (const [key, entry] of nextEntries) {
      this.draftEntries.set(key, entry);
    }
    this.draftUndo.length = 0;
    this.draftRedo.length = 0;
  }

  public resetScope(
    scope: AdvancedAuthoringScope,
    workspaces: readonly AuthoringDomainWorkspace[]
  ) {
    validateScope(scope);
    const nextWorkspaces = createWorkspaceMap(scope, workspaces);
    this.scope = scope;
    this.workspaces = nextWorkspaces;
    this.selectedRecords.clear();
    this.selectionAdapterId = null;
    this.draftEntries.clear();
    this.draftUndo.length = 0;
    this.draftRedo.length = 0;
    this.stagedHistory = null;
    this.clipboard = null;
    this.repeatTemplate = null;
  }

  public updateScope(
    scope: AdvancedAuthoringScope,
    workspaces: readonly AuthoringDomainWorkspace[]
  ): AdvancedAuthoringScopeUpdateResult {
    validateScope(scope);
    const nextWorkspaces = createWorkspaceMap(scope, workspaces);
    if (!scopeCompositionEqual(this.scope, scope)) {
      this.resetScope(scope, workspaces);
      return { kind: 'reset', reason: 'scope-changed' };
    }
    if (!this.canPreserveWorkspaceState(nextWorkspaces)) {
      this.resetScope(scope, workspaces);
      return { kind: 'reset', reason: 'workspace-incompatible' };
    }

    const metadataChanged = !sourceBindingsEqual(
      this.scope.sourceBinding!,
      scope.sourceBinding!
    );
    const etagChanged =
      this.scope.sourceBinding!.workspaceETag !== scope.sourceBinding!.workspaceETag;
    this.scope = scope;
    this.workspaces = nextWorkspaces;
    this.reconcileSelection();
    if (etagChanged) {
      if (this.stagedHistory?.changeSetETag !== scope.sourceBinding!.workspaceETag) {
        this.stagedHistory = null;
      }
    }
    return { kind: 'preserved', metadataChanged };
  }

  public replaceWorkspaces(workspaces: readonly AuthoringDomainWorkspace[]) {
    return this.updateScope(this.scope, workspaces);
  }

  public getSelection(): AuthoringSelectionSnapshot {
    return {
      adapterId: this.selectionAdapterId,
      records: [...this.selectedRecords.values()]
    };
  }

  public selectRecords(adapterId: string, records: readonly SemanticRecordRef[]) {
    if (records.length > this.selectionLimit) {
      throw new AdvancedAuthoringError('selection-limit-exceeded');
    }
    const workspace = this.requireWorkspace(adapterId);
    const uniqueRecords = new Map<string, SemanticRecordRef>();
    for (const record of records) {
      const snapshot = requireRecord(workspace, record);
      uniqueRecords.set(semanticRecordRefKey(snapshot.record), snapshot.record);
    }
    this.selectionAdapterId = uniqueRecords.size > 0 ? adapterId : null;
    this.selectedRecords.clear();
    for (const [key, record] of uniqueRecords) {
      this.selectedRecords.set(key, record);
    }
  }

  public toggleRecord(adapterId: string, record: SemanticRecordRef) {
    const workspace = this.requireWorkspace(adapterId);
    const snapshot = requireRecord(workspace, record);
    const key = semanticRecordRefKey(snapshot.record);
    if (this.selectionAdapterId !== null && this.selectionAdapterId !== adapterId) {
      throw new AdvancedAuthoringError('selection-incompatible');
    }
    if (this.selectedRecords.delete(key)) {
      if (this.selectedRecords.size === 0) {
        this.selectionAdapterId = null;
      }
      return;
    }
    if (this.selectedRecords.size >= this.selectionLimit) {
      throw new AdvancedAuthoringError('selection-limit-exceeded');
    }
    this.selectionAdapterId = adapterId;
    this.selectedRecords.set(key, snapshot.record);
  }

  public clearSelection() {
    this.selectedRecords.clear();
    this.selectionAdapterId = null;
  }

  public setDraftValue(
    adapterId: string,
    record: SemanticRecordRef,
    fieldKey: string,
    value: number
  ) {
    const workspace = this.requireWorkspace(adapterId);
    const recordSnapshot = requireRecord(workspace, record);
    const field = requireField(workspace, fieldKey);
    validateFieldValue(field, value);
    const baseline = requireRecordFieldValue(recordSnapshot, fieldKey);
    const fieldRef = createFieldRef(recordSnapshot.record, fieldKey);
    const key = semanticFieldRefKey(fieldRef);
    const before = this.draftEntries.get(key) ?? null;
    const after = value === baseline ? null : { field: fieldRef, value };
    if (draftEntriesEqual(before, after)) {
      return false;
    }
    validateProjectedMutations(
      workspace,
      [
        createMutation(
          recordSnapshot.record,
          field,
          this.getEffectiveValue(recordSnapshot, fieldKey),
          value
        )
      ],
      (record, candidateFieldKey) =>
        this.getEffectiveValue(record, candidateFieldKey)
    );
    this.applyDraftHistoryEvent({ changes: [{ after, before, key }] }, 'forward');
    this.pushDraftHistory({ changes: [{ after, before, key }] });
    return true;
  }

  public undoDraft() {
    const event = this.draftUndo.pop();
    if (!event) {
      return false;
    }
    this.applyDraftHistoryEvent(event, 'backward');
    pushBounded(this.draftRedo, event, this.historyLimit);
    return true;
  }

  public redoDraft() {
    const event = this.draftRedo.pop();
    if (!event) {
      return false;
    }
    this.applyDraftHistoryEvent(event, 'forward');
    pushBounded(this.draftUndo, event, this.historyLimit);
    return true;
  }

  public previewBulk(options: AuthoringBulkPreviewOptions) {
    const workspace = this.requireWorkspace(options.adapterId);
    const field = requireField(workspace, options.fieldKey);
    const records = this.resolveTargets(workspace, options.targetRecords);
    const mutations = records.map((record) => {
      const beforeValue = this.getEffectiveValue(record, field.fieldKey);
      return createMutation(
        record.record,
        field,
        beforeValue,
        applyTransform(field, beforeValue, options.transform)
      );
    });
    return this.createPreview(
      workspace,
      'bulkEdit',
      compactMutations(mutations),
      {
        sourceFieldKeys: [field.fieldKey],
        sourceRecord: null,
        sourceValues: {},
        targetCount: records.length
      },
      options.transform
    );
  }

  public copyFields(
    adapterId: string,
    sourceRecord: SemanticRecordRef,
    fieldKeys: readonly string[]
  ) {
    if (
      fieldKeys.length === 0 ||
      fieldKeys.length > advancedAuthoringMaximumClipboardFieldCount
    ) {
      throw new AdvancedAuthoringError('field-unavailable');
    }
    const workspace = this.requireWorkspace(adapterId);
    const record = requireRecord(workspace, sourceRecord);
    const uniqueFieldKeys = [...new Set(fieldKeys)];
    const fieldValues = Object.fromEntries(
      uniqueFieldKeys.map((fieldKey) => {
        requireField(workspace, fieldKey);
        return [fieldKey, this.getEffectiveValue(record, fieldKey)];
      })
    );
    this.clipboard = {
      adapterId,
      copiedAtRevisionFingerprint: workspace.sourceBinding.workspaceFingerprint,
      fieldValues,
      game: this.scope.game,
      projectId: this.scope.projectId,
      sourceRecord: record.record
    };
    return this.clipboard;
  }

  public copyPasteSpecialGroup(
    adapterId: string,
    sourceRecord: SemanticRecordRef,
    groupId: string
  ) {
    const workspace = this.requireWorkspace(adapterId);
    const group = workspace.pasteSpecialGroups.find((candidate) => candidate.id === groupId);
    if (!group) {
      throw new AdvancedAuthoringError('paste-group-unavailable');
    }
    return this.copyFields(adapterId, sourceRecord, group.fieldKeys);
  }

  public previewMultiTargetCopy(options: AuthoringCopyPreviewOptions) {
    const fieldKeys = options.fieldKeys ?? Object.keys(this.requireClipboard().fieldValues);
    return this.previewClipboardValues(options, fieldKeys, 'multiTargetCopy');
  }

  public previewPasteSpecial(options: AuthoringPasteSpecialPreviewOptions) {
    const workspace = this.requireWorkspace(options.adapterId);
    const group = workspace.pasteSpecialGroups.find((candidate) => candidate.id === options.groupId);
    if (!group) {
      throw new AdvancedAuthoringError('paste-group-unavailable');
    }
    return this.previewClipboardValues(options, group.fieldKeys, 'pasteSpecial');
  }

  public previewRepeat(targetRecords?: readonly SemanticRecordRef[]) {
    const template = this.repeatTemplate;
    if (
      !template ||
      template.createdAtRevisionFingerprint !==
        this.scope.sourceBinding?.workspaceFingerprint
    ) {
      throw new AdvancedAuthoringError('source-assumption-changed');
    }
    if (template.transform) {
      const preview = this.previewBulk({
        adapterId: template.adapterId,
        fieldKey: template.fieldKeys[0]!,
        targetRecords,
        transform: template.transform
      });
      return { ...preview, kind: 'repeatLastEdit' as const };
    }
    return this.previewValues(
      {
        adapterId: template.adapterId,
        targetRecords
      },
      template.fieldKeys,
      template.sourceRecord,
      template.sourceValues,
      'repeatLastEdit'
    );
  }

  public applyPreviewToDrafts(preview: AuthoringOperationPreview) {
    this.validatePreview(preview);
    const workspace = this.requireWorkspace(preview.adapterId);
    const changes = preview.mutations.map((mutation) => {
      const key = semanticFieldRefKey(mutation.field);
      const before = this.draftEntries.get(key) ?? null;
      const record = requireRecord(workspace, mutation.field.record);
      const baseline = requireRecordFieldValue(record, mutation.field.fieldKey);
      return {
        after:
          mutation.afterValue === baseline
            ? null
            : { field: mutation.field, value: mutation.afterValue },
        before,
        key
      };
    });
    const event = { changes };
    this.applyDraftHistoryEvent(event, 'forward');
    this.pushDraftHistory(event);
    this.rememberPreview(preview);
    return this.getSnapshot().drafts;
  }

  public createStageRequest(preview: AuthoringOperationPreview): AuthoringStageRequest {
    this.validatePreview(preview);
    return {
      activeChangeSetId: this.scope.activeChangeSetId,
      adapterId: preview.adapterId,
      game: this.scope.game,
      kind: preview.kind,
      mutations: preview.mutations,
      projectId: this.scope.projectId,
      schemaVersion: 1,
      sourceBinding: this.scope.sourceBinding!
    };
  }

  public recordStagedCommit(
    preview: AuthoringOperationPreview,
    metadata: AuthoringStagedCommitMetadata,
    priorDrafts: AuthoringDraftSnapshot
  ) {
    validateScope(preview.scope);
    const previousBinding = preview.scope.sourceBinding!;
    const transition = metadata.sourceTransition;
    const nextScope: AdvancedAuthoringScope = {
      activeChangeSetId: preview.scope.activeChangeSetId,
      game: preview.scope.game,
      projectId: preview.scope.projectId,
      sourceBinding: transition.nextSourceBinding
    };
    validateScope(nextScope);
    if (
      preview.adapterId.length === 0 ||
      preview.mutations.length === 0 ||
      preview.mutations.length > this.mutationLimit ||
      priorDrafts.schemaVersion !== 1 ||
      priorDrafts.entries.length > this.mutationLimit ||
      !scopesEqual(priorDrafts.scope, preview.scope) ||
      !sourceBindingsEqual(transition.previousSourceBinding, previousBinding) ||
      metadata.changeSetETag !== transition.nextSourceBinding.workspaceETag ||
      transition.nextSourceBinding.workspaceETag === previousBinding.workspaceETag ||
      !captureTransitionPreservesBoundary(previousBinding, transition.nextSourceBinding) ||
      !/^[A-Fa-f0-9]{64}$/u.test(transition.sourceRevisionFingerprint) ||
      (!scopesEqual(this.scope, preview.scope) && !scopesEqual(this.scope, nextScope))
    ) {
      throw new AdvancedAuthoringError('history-conflict');
    }
    const affectedOperationIds = [
      ...metadata.capturedOperationIds,
      ...metadata.removedOperationIds
    ];
    if (affectedOperationIds.length === 0) {
      throw new AdvancedAuthoringError('no-effective-change');
    }
    if (
      metadata.activeChangeSetId !== preview.scope.activeChangeSetId ||
      affectedOperationIds.length > this.mutationLimit ||
      new Set(affectedOperationIds).size !== affectedOperationIds.length ||
      affectedOperationIds.some((operationId) => !validAssociationId(operationId))
    ) {
      throw new AdvancedAuthoringError('history-conflict');
    }

    const stagedFieldKeys = new Set<string>();
    for (const mutation of preview.mutations) {
      const key = semanticFieldRefKey(mutation.field);
      if (stagedFieldKeys.has(key)) {
        throw new AdvancedAuthoringError('history-conflict');
      }
      stagedFieldKeys.add(key);
    }

    const nextWorkspaces = scopesEqual(this.scope, nextScope)
      ? new Map(this.workspaces)
      : advanceWorkspacesAfterCapture(
          preview,
          priorDrafts,
          nextScope,
          this.workspaces
        );
    validateCapturedMutations(preview, nextWorkspaces);
    const nextDraftEntries = revalidateDraftsAfterCapture(
      priorDrafts,
      stagedFieldKeys,
      nextWorkspaces
    );

    this.scope = nextScope;
    this.workspaces = nextWorkspaces;
    this.draftEntries.clear();
    for (const [key, entry] of nextDraftEntries) {
      this.draftEntries.set(key, entry);
    }
    this.draftUndo.length = 0;
    this.draftRedo.length = 0;
    this.clipboard = null;
    this.repeatTemplate = null;
    this.reconcileSelection();
    this.setStagedHistory(metadata);
    return metadata;
  }

  public syncStagedHistory(history: AuthoringStagedHistoryState | null) {
    if (history === null) {
      this.stagedHistory = null;
      return;
    }
    this.setStagedHistory(history);
  }

  public async undoStaged(executor: AuthoringStagedHistoryExecutor) {
    return this.runStagedHistory('undo', executor);
  }

  public async redoStaged(executor: AuthoringStagedHistoryExecutor) {
    return this.runStagedHistory('redo', executor);
  }

  private previewClipboardValues(
    options: AuthoringCopyPreviewOptions,
    fieldKeys: readonly string[],
    kind: Extract<AuthoringOperationKind, 'multiTargetCopy' | 'pasteSpecial'>
  ) {
    const clipboard = this.requireClipboard();
    if (
      clipboard.adapterId !== options.adapterId ||
      clipboard.projectId !== this.scope.projectId ||
      clipboard.game !== this.scope.game ||
      clipboard.copiedAtRevisionFingerprint !==
        this.scope.sourceBinding?.workspaceFingerprint
    ) {
      throw new AdvancedAuthoringError('clipboard-incompatible');
    }
    return this.previewValues(
      options,
      fieldKeys,
      clipboard.sourceRecord,
      clipboard.fieldValues,
      kind
    );
  }

  private previewValues(
    options: AuthoringPreviewOptions,
    fieldKeys: readonly string[],
    sourceRecord: SemanticRecordRef | null,
    sourceValues: Readonly<Record<string, number>>,
    kind: AuthoringOperationKind
  ) {
    const workspace = this.requireWorkspace(options.adapterId);
    const uniqueFieldKeys = [...new Set(fieldKeys)];
    if (
      uniqueFieldKeys.length === 0 ||
      uniqueFieldKeys.length > advancedAuthoringMaximumClipboardFieldCount
    ) {
      throw new AdvancedAuthoringError('field-unavailable');
    }
    const fields = uniqueFieldKeys.map((fieldKey) => requireField(workspace, fieldKey));
    for (const field of fields) {
      if (!Object.prototype.hasOwnProperty.call(sourceValues, field.fieldKey)) {
        throw new AdvancedAuthoringError('clipboard-incompatible');
      }
      validateFieldValue(field, sourceValues[field.fieldKey]!);
    }
    if (sourceRecord) {
      const currentSource = requireRecord(workspace, sourceRecord);
      if (
        fields.some(
          (field) =>
            this.getEffectiveValue(currentSource, field.fieldKey) !==
            sourceValues[field.fieldKey]
        )
      ) {
        throw new AdvancedAuthoringError('source-assumption-changed');
      }
    }

    const records = this.resolveTargets(workspace, options.targetRecords);
    const mutations = records.flatMap((record) =>
      fields.map((field) =>
        createMutation(
          record.record,
          field,
          this.getEffectiveValue(record, field.fieldKey),
          sourceValues[field.fieldKey]!
        )
      )
    );
    return this.createPreview(
      workspace,
      kind,
      compactMutations(mutations),
      {
        sourceFieldKeys: uniqueFieldKeys,
        sourceRecord,
        sourceValues: Object.fromEntries(
          uniqueFieldKeys.map((fieldKey) => [fieldKey, sourceValues[fieldKey]!])
        ),
        targetCount: records.length
      },
      null
    );
  }

  private createPreview(
    workspace: AuthoringDomainWorkspace,
    kind: AuthoringOperationKind,
    mutations: readonly AuthoringFieldMutation[],
    assumptions: AuthoringOperationPreview['assumptions'],
    transform: AuthoringTransform | null
  ): AuthoringOperationPreview {
    if (mutations.length === 0) {
      throw new AdvancedAuthoringError('no-effective-change');
    }
    if (mutations.length > this.mutationLimit) {
      throw new AdvancedAuthoringError('mutation-limit-exceeded');
    }
    validateProjectedMutations(
      workspace,
      mutations,
      (record, fieldKey) => this.getEffectiveValue(record, fieldKey)
    );
    return {
      adapterId: workspace.adapterId,
      assumptions,
      kind,
      mutations,
      scope: this.scope,
      transform
    };
  }

  private validatePreview(preview: AuthoringOperationPreview) {
    validateScope(this.scope);
    if (!scopesEqual(preview.scope, this.scope)) {
      throw new AdvancedAuthoringError('invalid-scope');
    }
    const workspace = this.requireWorkspace(preview.adapterId);
    for (const mutation of preview.mutations) {
      const record = requireRecord(workspace, mutation.field.record);
      const field = requireField(workspace, mutation.field.fieldKey);
      if (this.getEffectiveValue(record, field.fieldKey) !== mutation.beforeValue) {
        throw new AdvancedAuthoringError('source-assumption-changed');
      }
      validateFieldValue(field, mutation.afterValue);
    }
    validateProjectedMutations(
      workspace,
      preview.mutations,
      (record, fieldKey) => this.getEffectiveValue(record, fieldKey)
    );
  }

  private getEffectiveValue(record: AuthoringRecordSnapshot, fieldKey: string) {
    const field = createFieldRef(record.record, fieldKey);
    return (
      this.draftEntries.get(semanticFieldRefKey(field))?.value ??
      requireRecordFieldValue(record, fieldKey)
    );
  }

  private resolveTargets(
    workspace: AuthoringDomainWorkspace,
    requested?: readonly SemanticRecordRef[]
  ) {
    if (!requested && this.selectionAdapterId !== workspace.adapterId) {
      throw new AdvancedAuthoringError('selection-incompatible');
    }
    const records = requested ?? [...this.selectedRecords.values()];
    if (records.length === 0 || records.length > this.selectionLimit) {
      throw new AdvancedAuthoringError(
        records.length > this.selectionLimit
          ? 'selection-limit-exceeded'
          : 'selection-incompatible'
      );
    }
    const unique = new Map<string, AuthoringRecordSnapshot>();
    for (const record of records) {
      const snapshot = requireRecord(workspace, record);
      unique.set(semanticRecordRefKey(snapshot.record), snapshot);
    }
    return [...unique.values()];
  }

  private requireWorkspace(adapterId: string) {
    validateScope(this.scope);
    const workspace = this.workspaces.get(adapterId);
    if (!workspace) {
      throw new AdvancedAuthoringError('adapter-unavailable');
    }
    return workspace;
  }

  private findWorkspaceForRecord(record: SemanticRecordRef) {
    const recordKey = semanticRecordRefKey(record);
    const candidates = [...this.workspaces.values()].filter((workspace) =>
      workspace.records.some(
        (candidate) => semanticRecordRefKey(candidate.record) === recordKey
      )
    );
    if (candidates.length !== 1) {
      throw new AdvancedAuthoringError('record-unavailable');
    }
    return candidates[0]!;
  }

  private requireClipboard() {
    if (!this.clipboard) {
      throw new AdvancedAuthoringError('clipboard-incompatible');
    }
    return this.clipboard;
  }

  private canPreserveWorkspaceState(
    nextWorkspaces: ReadonlyMap<string, AuthoringDomainWorkspace>
  ) {
    const entriesToValidate = [
      ...this.draftEntries.values(),
      ...this.draftUndo.flatMap((event) =>
        event.changes.flatMap((change) => [change.before, change.after])
      ),
      ...this.draftRedo.flatMap((event) =>
        event.changes.flatMap((change) => [change.before, change.after])
      )
    ].filter((entry): entry is AuthoringDraftEntry => entry !== null);

    for (const entry of entriesToValidate) {
      if (!fieldStateIsCompatible(this.workspaces, nextWorkspaces, entry)) {
        return false;
      }
    }

    if (this.selectionAdapterId) {
      const nextWorkspace = nextWorkspaces.get(this.selectionAdapterId);
      if (
        !nextWorkspace ||
        [...this.selectedRecords.values()].some(
          (record) => findRecord(nextWorkspace, record) === null
        )
      ) {
        return false;
      }
    }

    if (
      this.clipboard &&
      !clipboardIsCompatible(
        this.clipboard,
        this.scope.sourceBinding!.workspaceFingerprint,
        this.workspaces,
        nextWorkspaces
      )
    ) {
      return false;
    }
    if (
      this.repeatTemplate &&
      !repeatTemplateIsCompatible(
        this.repeatTemplate,
        this.scope.sourceBinding!.workspaceFingerprint,
        this.workspaces,
        nextWorkspaces
      )
    ) {
      return false;
    }

    try {
      validateDraftProjection(nextWorkspaces, this.draftEntries);
    } catch (error) {
      if (error instanceof AdvancedAuthoringError) {
        return false;
      }
      throw error;
    }
    return true;
  }

  private replaceDraftEntry(key: string, entry: AuthoringDraftEntry | null) {
    if (entry) {
      this.draftEntries.set(key, entry);
    } else {
      this.draftEntries.delete(key);
    }
  }

  private applyDraftHistoryEvent(event: DraftHistoryEvent, direction: 'backward' | 'forward') {
    for (const change of event.changes) {
      this.replaceDraftEntry(change.key, direction === 'forward' ? change.after : change.before);
    }
  }

  private pushDraftHistory(event: DraftHistoryEvent) {
    pushBounded(this.draftUndo, event, this.historyLimit);
    this.draftRedo.length = 0;
  }

  private rememberPreview(preview: AuthoringOperationPreview) {
    this.repeatTemplate = {
      adapterId: preview.adapterId,
      createdAtRevisionFingerprint: this.scope.sourceBinding!.workspaceFingerprint,
      fieldKeys: preview.assumptions.sourceFieldKeys,
      kind: preview.kind,
      sourceRecord: preview.assumptions.sourceRecord,
      sourceValues: preview.assumptions.sourceValues,
      transform: preview.transform
    };
  }

  private async runStagedHistory(
    direction: 'redo' | 'undo',
    executor: AuthoringStagedHistoryExecutor
  ) {
    const history = this.stagedHistory;
    if (
      !history ||
      (direction === 'undo' ? !history.canUndo : !history.canRedo)
    ) {
      return false;
    }
    const result = await executor({
      activeChangeSetId: this.scope.activeChangeSetId,
      direction,
      expectedETag: history.changeSetETag
    });
    this.setStagedHistory(result.history);
    return result.committed;
  }

  private setStagedHistory(history: AuthoringStagedHistoryState) {
    if (
      history.activeChangeSetId !== this.scope.activeChangeSetId ||
      !/^[A-Fa-f0-9]{64}$/u.test(history.changeSetETag) ||
      !validHistoryLabel(history.undoLabel) ||
      !validHistoryLabel(history.redoLabel)
    ) {
      throw new AdvancedAuthoringError('history-conflict');
    }
    this.stagedHistory = {
      activeChangeSetId: history.activeChangeSetId,
      canRedo: history.canRedo,
      canUndo: history.canUndo,
      changeSetETag: history.changeSetETag,
      redoLabel: history.redoLabel,
      undoLabel: history.undoLabel
    };
  }

  private reconcileSelection() {
    if (!this.selectionAdapterId) {
      return;
    }
    const workspace = this.workspaces.get(this.selectionAdapterId);
    if (!workspace) {
      this.clearSelection();
      return;
    }
    const available = new Set(workspace.records.map((record) => semanticRecordRefKey(record.record)));
    for (const key of this.selectedRecords.keys()) {
      if (!available.has(key)) {
        this.selectedRecords.delete(key);
      }
    }
    if (this.selectedRecords.size === 0) {
      this.selectionAdapterId = null;
    }
  }
}

function requireField(workspace: AuthoringDomainWorkspace, fieldKey: string) {
  const field = workspace.fields.find((candidate) => candidate.fieldKey === fieldKey);
  if (!field) {
    throw new AdvancedAuthoringError('field-unavailable');
  }
  return field;
}

function requireRecord(workspace: AuthoringDomainWorkspace, record: SemanticRecordRef) {
  const key = semanticRecordRefKey(record);
  const snapshot = workspace.records.find(
    (candidate) => semanticRecordRefKey(candidate.record) === key
  );
  if (!snapshot || snapshot.adapterId !== workspace.adapterId) {
    throw new AdvancedAuthoringError('record-unavailable');
  }
  return snapshot;
}

function requireRecordFieldValue(record: AuthoringRecordSnapshot, fieldKey: string) {
  const value = record.fieldValues[fieldKey];
  if (value === undefined) {
    throw new AdvancedAuthoringError('field-unavailable');
  }
  return value;
}

function createFieldRef(record: SemanticRecordRef, fieldKey: string): SemanticFieldRef {
  return { fieldKey, record };
}

function applyTransform(
  field: AuthoringFieldDescriptor,
  beforeValue: number,
  transform: AuthoringTransform
) {
  if (
    transform.kind !== 'replace' &&
    !field.supportedTransforms.includes(transform.kind)
  ) {
    throw new AdvancedAuthoringError('transform-unavailable');
  }
  let value: number;
  switch (transform.kind) {
    case 'replace':
      value = transform.value;
      break;
    case 'add':
      value = beforeValue + transform.amount;
      break;
    case 'multiply': {
      const multiplied = beforeValue * transform.factor;
      value =
        transform.rounding === 'floor'
          ? Math.floor(multiplied)
          : transform.rounding === 'ceil'
            ? Math.ceil(multiplied)
            : Math.round(multiplied);
      break;
    }
    case 'clamp':
      if (
        !Number.isFinite(transform.minimum) ||
        !Number.isFinite(transform.maximum) ||
        transform.minimum > transform.maximum
      ) {
        throw new AdvancedAuthoringError('invalid-field-value');
      }
      value = Math.min(transform.maximum, Math.max(transform.minimum, beforeValue));
      break;
  }
  validateFieldValue(field, value);
  return value;
}

function validateFieldValue(field: AuthoringFieldDescriptor, value: number) {
  if (
    !Number.isFinite(value) ||
    ((field.valueKind === 'integer' ||
      field.valueKind === 'enum' ||
      field.valueKind === 'boolean') &&
      !Number.isSafeInteger(value)) ||
    (field.valueKind === 'boolean' && value !== 0 && value !== 1) ||
    (field.options.length > 0 && !field.options.some((option) => option.value === value)) ||
    (field.minimumValue !== null && value < field.minimumValue) ||
    (field.maximumValue !== null && value > field.maximumValue)
  ) {
    throw new AdvancedAuthoringError('invalid-field-value');
  }
}

function createMutation(
  record: SemanticRecordRef,
  field: AuthoringFieldDescriptor,
  beforeValue: number,
  afterValue: number
): AuthoringFieldMutation {
  validateFieldValue(field, afterValue);
  return {
    afterValue,
    beforeValue,
    field: createFieldRef(record, field.fieldKey)
  };
}

function compactMutations(mutations: readonly AuthoringFieldMutation[]) {
  return mutations.filter((mutation) => !Object.is(mutation.beforeValue, mutation.afterValue));
}

function validateProjectedMutations(
  workspace: AuthoringDomainWorkspace,
  mutations: readonly AuthoringFieldMutation[],
  getEffectiveValue?: (
    record: AuthoringRecordSnapshot,
    fieldKey: string
  ) => number | undefined
) {
  const registration = getAdvancedAuthoringAdapter(workspace.adapterId);
  if (!registration) {
    throw new AdvancedAuthoringError('adapter-unavailable');
  }
  const mutationsByRecord = new Map<string, AuthoringFieldMutation[]>();
  for (const mutation of mutations) {
    const key = semanticRecordRefKey(mutation.field.record);
    const existing = mutationsByRecord.get(key) ?? [];
    if (existing.some((candidate) => candidate.field.fieldKey === mutation.field.fieldKey)) {
      throw new AdvancedAuthoringError('invalid-field-value');
    }
    mutationsByRecord.set(key, [...existing, mutation]);
  }
  for (const groupedMutations of mutationsByRecord.values()) {
    const record = requireRecord(workspace, groupedMutations[0]!.field.record);
    const projectedValues = Object.fromEntries(
      Object.keys(record.fieldValues).flatMap((fieldKey) => {
        const value = getEffectiveValue?.(record, fieldKey) ?? record.fieldValues[fieldKey];
        return value === undefined ? [] : [[fieldKey, value]];
      })
    );
    const changedFieldKeys = new Set<string>();
    for (const mutation of groupedMutations) {
      projectedValues[mutation.field.fieldKey] = mutation.afterValue;
      changedFieldKeys.add(mutation.field.fieldKey);
    }
    if (
      registration.validateProjection &&
      !registration.validateProjection({ changedFieldKeys, projectedValues, record })
    ) {
      throw new AdvancedAuthoringError('invalid-field-value');
    }
  }
}

function createWorkspaceMap(
  scope: AdvancedAuthoringScope,
  workspaces: readonly AuthoringDomainWorkspace[]
) {
  const next = new Map<string, AuthoringDomainWorkspace>();
  for (const workspace of workspaces) {
    validateWorkspace(scope, workspace);
    if (next.has(workspace.adapterId)) {
      throw new AdvancedAuthoringError('adapter-unavailable');
    }
    next.set(workspace.adapterId, workspace);
  }
  return next;
}

function advanceWorkspacesAfterCapture(
  preview: AuthoringOperationPreview,
  priorDrafts: AuthoringDraftSnapshot,
  nextScope: AdvancedAuthoringScope,
  currentWorkspaces: ReadonlyMap<string, AuthoringDomainWorkspace>
) {
  const workspace = currentWorkspaces.get(preview.adapterId);
  if (!workspace) {
    throw new AdvancedAuthoringError('adapter-unavailable');
  }
  const priorEntries = new Map<string, AuthoringDraftEntry>();
  for (const entry of priorDrafts.entries) {
    const key = semanticFieldRefKey(entry.field);
    if (priorEntries.has(key)) {
      throw new AdvancedAuthoringError('history-conflict');
    }
    priorEntries.set(key, entry);
  }

  const mutationsByRecord = new Map<string, Map<string, number>>();
  for (const mutation of preview.mutations) {
    const record = requireRecord(workspace, mutation.field.record);
    const field = requireField(workspace, mutation.field.fieldKey);
    validateFieldValue(field, mutation.afterValue);
    const fieldKey = semanticFieldRefKey(mutation.field);
    const effectiveBefore =
      priorEntries.get(fieldKey)?.value ??
      requireRecordFieldValue(record, field.fieldKey);
    if (!Object.is(effectiveBefore, mutation.beforeValue)) {
      throw new AdvancedAuthoringError('source-assumption-changed');
    }
    const recordKey = semanticRecordRefKey(record.record);
    const recordMutations = mutationsByRecord.get(recordKey) ?? new Map();
    if (recordMutations.has(field.fieldKey)) {
      throw new AdvancedAuthoringError('history-conflict');
    }
    recordMutations.set(field.fieldKey, mutation.afterValue);
    mutationsByRecord.set(recordKey, recordMutations);
  }

  const nextBinding = nextScope.sourceBinding!;
  const advancedWorkspaces = [...currentWorkspaces.values()].map((candidate) => ({
    ...candidate,
    records:
      candidate.adapterId === preview.adapterId
        ? candidate.records.map((record) => {
            const updates = mutationsByRecord.get(
              semanticRecordRefKey(record.record)
            );
            return updates
              ? {
                  ...record,
                  fieldValues: {
                    ...record.fieldValues,
                    ...Object.fromEntries(updates)
                  }
                }
              : record;
          })
        : candidate.records,
    sourceBinding: nextBinding
  }));
  return createWorkspaceMap(nextScope, advancedWorkspaces);
}

function revalidateDraftsAfterCapture(
  priorDrafts: AuthoringDraftSnapshot,
  stagedFieldKeys: ReadonlySet<string>,
  nextWorkspaces: ReadonlyMap<string, AuthoringDomainWorkspace>
) {
  const nextEntries = new Map<string, AuthoringDraftEntry>();
  const seenKeys = new Set<string>();
  for (const entry of priorDrafts.entries) {
    const key = semanticFieldRefKey(entry.field);
    if (seenKeys.has(key)) {
      throw new AdvancedAuthoringError('history-conflict');
    }
    seenKeys.add(key);
    if (stagedFieldKeys.has(key)) {
      continue;
    }
    const located = findWorkspaceRecord(nextWorkspaces, entry.field.record);
    if (!located) {
      throw new AdvancedAuthoringError('record-unavailable');
    }
    const field = requireField(located.workspace, entry.field.fieldKey);
    validateFieldValue(field, entry.value);
    if (Object.is(requireRecordFieldValue(located.record, field.fieldKey), entry.value)) {
      continue;
    }
    nextEntries.set(key, entry);
  }
  validateDraftProjection(nextWorkspaces, nextEntries);
  return nextEntries;
}

function validateCapturedMutations(
  preview: AuthoringOperationPreview,
  workspaces: ReadonlyMap<string, AuthoringDomainWorkspace>
) {
  const workspace = workspaces.get(preview.adapterId);
  if (!workspace) {
    throw new AdvancedAuthoringError('adapter-unavailable');
  }
  const fields = new Set<string>();
  for (const mutation of preview.mutations) {
    const record = requireRecord(workspace, mutation.field.record);
    const field = requireField(workspace, mutation.field.fieldKey);
    validateFieldValue(field, mutation.afterValue);
    const key = semanticFieldRefKey(mutation.field);
    if (fields.has(key)) {
      throw new AdvancedAuthoringError('history-conflict');
    }
    fields.add(key);
    requireRecordFieldValue(record, field.fieldKey);
  }
}

function captureTransitionPreservesBoundary(
  previous: AdvancedAuthoringSourceBinding,
  next: AdvancedAuthoringSourceBinding
) {
  return (
    previous.version === 1 &&
    next.version === previous.version &&
    next.projectId === previous.projectId &&
    next.outputProfileId === previous.outputProfileId &&
    next.outputMode === previous.outputMode &&
    next.outputRootFingerprint === previous.outputRootFingerprint &&
    next.workspacePersonalStateETag === previous.workspacePersonalStateETag &&
    arraysEqual(previous.selectedChangeSetIds, next.selectedChangeSetIds)
  );
}

function fieldStateIsCompatible(
  currentWorkspaces: ReadonlyMap<string, AuthoringDomainWorkspace>,
  nextWorkspaces: ReadonlyMap<string, AuthoringDomainWorkspace>,
  entry: AuthoringDraftEntry
) {
  const current = findWorkspaceRecord(currentWorkspaces, entry.field.record);
  const next = findWorkspaceRecord(nextWorkspaces, entry.field.record);
  if (!current || !next || current.workspace.adapterId !== next.workspace.adapterId) {
    return false;
  }
  const currentField = current.workspace.fields.find(
    (field) => field.fieldKey === entry.field.fieldKey
  );
  const nextField = next.workspace.fields.find(
    (field) => field.fieldKey === entry.field.fieldKey
  );
  const currentBaseline = current.record.fieldValues[entry.field.fieldKey];
  const nextBaseline = next.record.fieldValues[entry.field.fieldKey];
  if (
    !currentField ||
    !nextField ||
    currentBaseline === undefined ||
    nextBaseline === undefined ||
    !fieldDescriptorsAreCompatible(currentField, nextField) ||
    !Object.is(currentBaseline, nextBaseline)
  ) {
    return false;
  }
  try {
    validateFieldValue(nextField, entry.value);
    return true;
  } catch (error) {
    if (error instanceof AdvancedAuthoringError) {
      return false;
    }
    throw error;
  }
}

function fieldDescriptorsAreCompatible(
  current: AuthoringFieldDescriptor,
  next: AuthoringFieldDescriptor
) {
  const currentOptions = current.options.map((option) => option.value).sort(compareNumbers);
  const nextOptions = next.options.map((option) => option.value).sort(compareNumbers);
  return (
    current.fieldKey === next.fieldKey &&
    current.valueKind === next.valueKind &&
    Object.is(current.minimumValue, next.minimumValue) &&
    Object.is(current.maximumValue, next.maximumValue) &&
    arraysEqual(current.supportedTransforms, next.supportedTransforms) &&
    arraysEqual(currentOptions, nextOptions)
  );
}

function clipboardIsCompatible(
  clipboard: AuthoringClipboard,
  workspaceFingerprint: string,
  currentWorkspaces: ReadonlyMap<string, AuthoringDomainWorkspace>,
  nextWorkspaces: ReadonlyMap<string, AuthoringDomainWorkspace>
) {
  if (clipboard.copiedAtRevisionFingerprint !== workspaceFingerprint) {
    return false;
  }
  const fieldKeys = Object.keys(clipboard.fieldValues);
  return (
    fieldKeys.length > 0 &&
    fieldKeys.length <= advancedAuthoringMaximumClipboardFieldCount &&
    fieldKeys.every((fieldKey) =>
      fieldStateIsCompatible(currentWorkspaces, nextWorkspaces, {
        field: createFieldRef(clipboard.sourceRecord, fieldKey),
        value: clipboard.fieldValues[fieldKey]!
      })
    )
  );
}

function repeatTemplateIsCompatible(
  template: AuthoringRepeatTemplate,
  workspaceFingerprint: string,
  currentWorkspaces: ReadonlyMap<string, AuthoringDomainWorkspace>,
  nextWorkspaces: ReadonlyMap<string, AuthoringDomainWorkspace>
) {
  if (
    template.createdAtRevisionFingerprint !== workspaceFingerprint ||
    template.fieldKeys.length === 0 ||
    template.fieldKeys.length > advancedAuthoringMaximumClipboardFieldCount
  ) {
    return false;
  }
  const currentWorkspace = currentWorkspaces.get(template.adapterId);
  const nextWorkspace = nextWorkspaces.get(template.adapterId);
  if (!currentWorkspace || !nextWorkspace) {
    return false;
  }
  return template.fieldKeys.every((fieldKey) => {
    const currentField = currentWorkspace.fields.find(
      (field) => field.fieldKey === fieldKey
    );
    const nextField = nextWorkspace.fields.find((field) => field.fieldKey === fieldKey);
    if (!currentField || !nextField || !fieldDescriptorsAreCompatible(currentField, nextField)) {
      return false;
    }
    if (!template.sourceRecord) {
      return true;
    }
    const sourceValue = template.sourceValues[fieldKey];
    return (
      sourceValue !== undefined &&
      fieldStateIsCompatible(currentWorkspaces, nextWorkspaces, {
        field: createFieldRef(template.sourceRecord, fieldKey),
        value: sourceValue
      })
    );
  });
}

function validateDraftProjection(
  workspaces: ReadonlyMap<string, AuthoringDomainWorkspace>,
  draftEntries: ReadonlyMap<string, AuthoringDraftEntry>
) {
  const mutationsByAdapter = new Map<string, AuthoringFieldMutation[]>();
  for (const entry of draftEntries.values()) {
    const located = findWorkspaceRecord(workspaces, entry.field.record);
    if (!located) {
      throw new AdvancedAuthoringError('record-unavailable');
    }
    const field = requireField(located.workspace, entry.field.fieldKey);
    const baseline = requireRecordFieldValue(located.record, field.fieldKey);
    const mutations = mutationsByAdapter.get(located.workspace.adapterId) ?? [];
    mutationsByAdapter.set(located.workspace.adapterId, [
      ...mutations,
      createMutation(located.record.record, field, baseline, entry.value)
    ]);
  }
  for (const [adapterId, mutations] of mutationsByAdapter) {
    const workspace = workspaces.get(adapterId);
    if (!workspace) {
      throw new AdvancedAuthoringError('adapter-unavailable');
    }
    validateProjectedMutations(workspace, mutations, (record, fieldKey) =>
      draftEntries.get(semanticFieldRefKey(createFieldRef(record.record, fieldKey)))
        ?.value ?? record.fieldValues[fieldKey]
    );
  }
}

function findWorkspaceRecord(
  workspaces: ReadonlyMap<string, AuthoringDomainWorkspace>,
  record: SemanticRecordRef
) {
  const matches = [...workspaces.values()].flatMap((workspace) => {
    const snapshot = findRecord(workspace, record);
    return snapshot ? [{ record: snapshot, workspace }] : [];
  });
  return matches.length === 1 ? matches[0]! : null;
}

function findRecord(workspace: AuthoringDomainWorkspace, record: SemanticRecordRef) {
  const key = semanticRecordRefKey(record);
  return (
    workspace.records.find(
      (candidate) => semanticRecordRefKey(candidate.record) === key
    ) ?? null
  );
}

function validateScope(scope: AdvancedAuthoringScope) {
  const binding = scope.sourceBinding;
  if (
    !validProjectId(scope.projectId) ||
    !validAssociationId(scope.activeChangeSetId) ||
    !binding ||
    binding.version !== 1 ||
    binding.projectId !== scope.projectId ||
    !/^[A-Fa-f0-9]{64}$/u.test(binding.workspaceETag) ||
    !/^[A-Fa-f0-9]{64}$/u.test(binding.workspaceFingerprint) ||
    !/^[A-Fa-f0-9]{64}$/u.test(binding.outputRootFingerprint) ||
    !validOutputMode(binding.outputMode) ||
    binding.selectedChangeSetIds.length > 64 ||
    new Set(binding.selectedChangeSetIds).size !== binding.selectedChangeSetIds.length ||
    binding.selectedChangeSetIds.some((id) => !validAssociationId(id)) ||
    (binding.outputProfileId !== null &&
      !validAssociationId(binding.outputProfileId)) ||
    (binding.outputProfileId === null
      ? binding.workspacePersonalStateETag !== null
      : !/^[A-Fa-f0-9]{64}$/u.test(
          binding.workspacePersonalStateETag ?? ''
        ))
  ) {
    throw new AdvancedAuthoringError(
      scope.activeChangeSetId.trim().length === 0
        ? 'active-change-set-required'
        : 'invalid-scope'
    );
  }
}

function validateWorkspace(scope: AdvancedAuthoringScope, workspace: AuthoringDomainWorkspace) {
  const registration = getAdvancedAuthoringAdapter(workspace.adapterId);
  if (
    !registration ||
    workspace.game !== scope.game ||
    !scope.sourceBinding ||
    !sourceBindingsEqual(workspace.sourceBinding, scope.sourceBinding) ||
    !registration.games.includes(scope.game)
  ) {
    throw new AdvancedAuthoringError('adapter-unavailable');
  }
}

function scopesEqual(left: AdvancedAuthoringScope, right: AdvancedAuthoringScope) {
  return (
    left.projectId === right.projectId &&
    left.game === right.game &&
    left.activeChangeSetId === right.activeChangeSetId &&
    left.sourceBinding !== null &&
    right.sourceBinding !== null &&
    sourceBindingsEqual(left.sourceBinding, right.sourceBinding)
  );
}

function scopeCompositionEqual(
  left: AdvancedAuthoringScope,
  right: AdvancedAuthoringScope
) {
  return (
    left.projectId === right.projectId &&
    left.game === right.game &&
    left.activeChangeSetId === right.activeChangeSetId &&
    left.sourceBinding?.workspaceFingerprint ===
      right.sourceBinding?.workspaceFingerprint
  );
}

function sourceBindingsEqual(
  left: NonNullable<AdvancedAuthoringScope['sourceBinding']>,
  right: NonNullable<AdvancedAuthoringScope['sourceBinding']>
) {
  return (
    left.version === right.version &&
    left.projectId === right.projectId &&
    left.workspaceETag === right.workspaceETag &&
    left.workspaceFingerprint === right.workspaceFingerprint &&
    left.outputProfileId === right.outputProfileId &&
    left.outputMode === right.outputMode &&
    left.outputRootFingerprint === right.outputRootFingerprint &&
    left.workspacePersonalStateETag === right.workspacePersonalStateETag &&
    arraysEqual(left.selectedChangeSetIds, right.selectedChangeSetIds)
  );
}

function validAssociationId(value: string) {
  return (
    value.length >= 1 &&
    value.length <= 128 &&
    /^[A-Za-z0-9][A-Za-z0-9._-]*$/u.test(value)
  );
}

function validProjectId(value: string) {
  return (
    value.length >= 1 &&
    value.length <= 128 &&
    value === value.trim() &&
    !/\p{Cc}/u.test(value)
  );
}

function validOutputMode(value: AdvancedAuthoringSourceBinding['outputMode']) {
  return (
    value === null ||
    value === 'standalone' ||
    value === 'trinityModManager' ||
    value === 'trinityBypass'
  );
}

function validHistoryLabel(value: string | null) {
  return (
    value === null ||
    (value.length >= 1 &&
      value.length <= 512 &&
      value === value.trim() &&
      !/\p{Cc}/u.test(value))
  );
}

function arraysEqual<T>(left: readonly T[], right: readonly T[]) {
  return (
    left.length === right.length &&
    left.every((value, index) => Object.is(value, right[index]))
  );
}

function compareNumbers(left: number, right: number) {
  return left - right;
}

function draftEntriesEqual(
  left: AuthoringDraftEntry | null,
  right: AuthoringDraftEntry | null
) {
  return (
    left === right ||
    (left !== null &&
      right !== null &&
      semanticFieldRefKey(left.field) === semanticFieldRefKey(right.field) &&
      Object.is(left.value, right.value))
  );
}

function sortDraftEntries(entries: AuthoringDraftEntry[]) {
  return entries.sort((left, right) =>
    semanticFieldRefKey(left.field).localeCompare(semanticFieldRefKey(right.field), 'en')
  );
}

function pushBounded<T>(items: T[], item: T, limit: number) {
  items.push(item);
  if (items.length > limit) {
    items.splice(0, items.length - limit);
  }
}

function assertPositiveBound(value: number, maximum: number) {
  if (!Number.isSafeInteger(value) || value < 1 || value > maximum) {
    throw new Error('Advanced authoring limits must be positive bounded integers.');
  }
}
