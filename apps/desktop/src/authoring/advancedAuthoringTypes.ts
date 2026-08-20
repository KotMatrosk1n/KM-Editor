/* SPDX-License-Identifier: GPL-3.0-only */

import type { EditSession, ProjectGame } from '../bridge/contracts';
import type {
  SemanticFieldRef,
  SemanticRecordRef
} from '../workbench/semanticContracts';

export const advancedAuthoringMaximumSelectionCount = 128;
export const advancedAuthoringMaximumMutationCount = 128;
export const advancedAuthoringMaximumHistoryCount = 64;
export const advancedAuthoringMaximumClipboardFieldCount = 32;

export type AdvancedAuthoringSourceBinding = NonNullable<
  EditSession['authoringBinding']
>;

export type AdvancedAuthoringScope = {
  activeChangeSetId: string;
  game: ProjectGame;
  projectId: string;
  sourceBinding: AdvancedAuthoringSourceBinding | null;
};

export type AuthoringFieldValueKind = 'boolean' | 'enum' | 'integer' | 'number';
export type AuthoringRelativeTransformKind = 'add' | 'clamp' | 'multiply';
export type AuthoringOperationKind =
  | 'bulkEdit'
  | 'multiTargetCopy'
  | 'pasteSpecial'
  | 'repeatLastEdit';

export type AuthoringFieldOption = {
  label: string;
  value: number;
};

export type AuthoringFieldDescriptor = {
  fieldKey: string;
  label: string;
  maximumValue: number | null;
  minimumValue: number | null;
  options: readonly AuthoringFieldOption[];
  supportedTransforms: readonly AuthoringRelativeTransformKind[];
  valueKind: AuthoringFieldValueKind;
};

export type AuthoringPasteSpecialGroup = {
  fieldKeys: readonly string[];
  id: string;
};

export type AuthoringRecordSnapshot = {
  adapterId: string;
  displayName: string;
  fieldValues: Readonly<Record<string, number>>;
  record: SemanticRecordRef;
};

export type AuthoringDomainWorkspace = {
  adapterId: string;
  fields: readonly AuthoringFieldDescriptor[];
  game: ProjectGame;
  pasteSpecialGroups: readonly AuthoringPasteSpecialGroup[];
  records: readonly AuthoringRecordSnapshot[];
  sourceBinding: AdvancedAuthoringSourceBinding;
};

export type AuthoringReplaceTransform = {
  kind: 'replace';
  value: number;
};

export type AuthoringAddTransform = {
  amount: number;
  kind: 'add';
};

export type AuthoringMultiplyTransform = {
  factor: number;
  kind: 'multiply';
  rounding: 'ceil' | 'floor' | 'nearest';
};

export type AuthoringClampTransform = {
  kind: 'clamp';
  maximum: number;
  minimum: number;
};

export type AuthoringTransform =
  | AuthoringReplaceTransform
  | AuthoringAddTransform
  | AuthoringMultiplyTransform
  | AuthoringClampTransform;

export type AuthoringFieldMutation = {
  afterValue: number;
  beforeValue: number;
  field: SemanticFieldRef;
};

export type AuthoringPreviewAssumptions = {
  sourceFieldKeys: readonly string[];
  sourceRecord: SemanticRecordRef | null;
  sourceValues: Readonly<Record<string, number>>;
  targetCount: number;
};

export type AuthoringOperationPreview = {
  adapterId: string;
  assumptions: AuthoringPreviewAssumptions;
  kind: AuthoringOperationKind;
  mutations: readonly AuthoringFieldMutation[];
  scope: AdvancedAuthoringScope;
  transform: AuthoringTransform | null;
};

export type AuthoringDraftEntry = {
  field: SemanticFieldRef;
  value: number;
};

export type AdvancedAuthoringDraftSnapshot = {
  entries: readonly AuthoringDraftEntry[];
  schemaVersion: 1;
  scope: AdvancedAuthoringScope;
};

export type AuthoringDraftSnapshot = AdvancedAuthoringDraftSnapshot;

export type AuthoringClipboard = {
  adapterId: string;
  copiedAtRevisionFingerprint: string;
  fieldValues: Readonly<Record<string, number>>;
  game: ProjectGame;
  projectId: string;
  sourceRecord: SemanticRecordRef;
};

export type AuthoringRepeatTemplate = {
  adapterId: string;
  createdAtRevisionFingerprint: string;
  fieldKeys: readonly string[];
  kind: AuthoringOperationKind;
  sourceRecord: SemanticRecordRef | null;
  sourceValues: Readonly<Record<string, number>>;
  transform: AuthoringTransform | null;
};

export type AuthoringStageRequest = {
  activeChangeSetId: string;
  adapterId: string;
  game: ProjectGame;
  kind: AuthoringOperationKind;
  mutations: readonly AuthoringFieldMutation[];
  projectId: string;
  sourceBinding: AdvancedAuthoringSourceBinding;
  schemaVersion: 1;
};

export type AuthoringStagedHistoryState = {
  activeChangeSetId: string;
  canRedo: boolean;
  canUndo: boolean;
  changeSetETag: string;
  redoLabel: string | null;
  undoLabel: string | null;
};

export type AuthoringStagedSourceTransition = {
  nextSourceBinding: AdvancedAuthoringSourceBinding;
  previousSourceBinding: AdvancedAuthoringSourceBinding;
  sourceRevisionFingerprint: string;
};

export type AuthoringStagedCommitMetadata = AuthoringStagedHistoryState & {
  capturedOperationIds: readonly string[];
  removedOperationIds: readonly string[];
  sourceTransition: AuthoringStagedSourceTransition;
};

export type AuthoringStagedHistoryDirection = 'redo' | 'undo';

export type AuthoringStagedHistoryRequest = {
  activeChangeSetId: string;
  direction: AuthoringStagedHistoryDirection;
  expectedETag: string;
};

export type AuthoringStagedHistoryResult = {
  committed: boolean;
  history: AuthoringStagedHistoryState;
};

export type AuthoringStagedHistoryExecutor = (
  request: AuthoringStagedHistoryRequest
) => Promise<AuthoringStagedHistoryResult>;

export type AuthoringSelectionSnapshot = {
  adapterId: string | null;
  records: readonly SemanticRecordRef[];
};

export type AdvancedAuthoringControllerSnapshot = {
  canRedoDraft: boolean;
  canRedoStaged: boolean;
  canUndoDraft: boolean;
  canUndoStaged: boolean;
  clipboard: AuthoringClipboard | null;
  drafts: AuthoringDraftSnapshot;
  repeatTemplate: AuthoringRepeatTemplate | null;
  selection: AuthoringSelectionSnapshot;
  stagedHistory: AuthoringStagedHistoryState | null;
  stagedHistoryRevision: string | null;
};

export type AdvancedAuthoringScopeUpdateResult =
  | {
      kind: 'preserved';
      metadataChanged: boolean;
    }
  | {
      kind: 'reset';
      reason: 'scope-changed' | 'workspace-incompatible';
    };

export type AuthoringIssueCode =
  | 'active-change-set-required'
  | 'adapter-unavailable'
  | 'clipboard-incompatible'
  | 'field-unavailable'
  | 'history-conflict'
  | 'invalid-field-value'
  | 'invalid-scope'
  | 'mutation-limit-exceeded'
  | 'no-effective-change'
  | 'paste-group-unavailable'
  | 'record-unavailable'
  | 'selection-incompatible'
  | 'selection-limit-exceeded'
  | 'source-assumption-changed'
  | 'transform-unavailable';

export class AdvancedAuthoringError extends Error {
  public readonly code: AuthoringIssueCode;

  public constructor(code: AuthoringIssueCode) {
    super(code);
    this.name = 'AdvancedAuthoringError';
    this.code = code;
  }
}
