/* SPDX-License-Identifier: GPL-3.0-only */

import {
  createRowClipboardEnvelopeV1,
  normalizeRowClipboardAdapterRegistration,
  normalizeRowClipboardScope,
  rowClipboardAdapterRegistrationKey,
  rowClipboardAdapterRegistrationSignature,
  rowClipboardRegistrationSupportsScope,
  validateRowClipboardEnvelopeV1,
  type RowClipboardHashFunction
} from './rowClipboardCanonical';
import {
  RowClipboardError,
  rowClipboardPreviewSchemaVersion,
  type RowClipboardAdapterRegistration,
  type RowClipboardBoundPreviewV1,
  type RowClipboardCommitAuthorizationV1,
  type RowClipboardControllerSnapshot,
  type RowClipboardCopyRequestV1,
  type RowClipboardEditorSchemaRef,
  type RowClipboardEnvelopeV1,
  type RowClipboardPreviewRequestV1,
  type RowClipboardRegistrationUpdateResult,
  type RowClipboardScope,
  type RowClipboardScopeUpdateResult
} from './rowClipboardTypes';

export type RowClipboardControllerOptions = Readonly<{
  hash?: RowClipboardHashFunction;
  registrations: readonly RowClipboardAdapterRegistration[];
  scope: RowClipboardScope;
}>;

type RegisteredAdapter = Readonly<{
  registration: RowClipboardAdapterRegistration;
  signature: string;
}>;

export class RowClipboardController {
  private clipboard: RowClipboardEnvelopeV1 | null = null;
  private clipboardAdapterSignature: string | null = null;
  private readonly hash: RowClipboardHashFunction | undefined;
  private lifecycleRevision = 0;
  private preview: RowClipboardBoundPreviewV1 | null = null;
  private registrations: ReadonlyMap<string, RegisteredAdapter>;
  private scope: RowClipboardScope;

  public constructor(options: RowClipboardControllerOptions) {
    this.scope = normalizeRowClipboardScope(options.scope);
    this.registrations = createRegistrationMap(options.registrations);
    this.hash = options.hash;
  }

  public getSnapshot(): RowClipboardControllerSnapshot {
    return Object.freeze({
      clipboard: this.clipboard,
      preview: this.preview,
      scope: this.scope
    });
  }

  public async copy(request: RowClipboardCopyRequestV1) {
    const lifecycleRevision = ++this.lifecycleRevision;
    const scope = this.scope;
    const registered = this.requireAdapter(request.editor);
    const envelope = await createRowClipboardEnvelopeV1(
      { ...request, scope },
      registered.registration,
      this.hash
    );
    if (lifecycleRevision !== this.lifecycleRevision || scope !== this.scope) {
      throw new RowClipboardError('scope-changed-during-copy');
    }
    this.clipboard = envelope;
    this.clipboardAdapterSignature = registered.signature;
    this.preview = null;
    return envelope;
  }

  public async importEnvelope(input: unknown) {
    const lifecycleRevision = ++this.lifecycleRevision;
    const scope = this.scope;
    this.preview = null;
    const registered = this.requireAdapter(readUntrustedEditorSchema(input));
    const envelope = await validateRowClipboardEnvelopeV1(
      input,
      registered.registration,
      this.hash
    );
    if (lifecycleRevision !== this.lifecycleRevision || scope !== this.scope) {
      throw new RowClipboardError('scope-changed-during-copy');
    }
    if (
      envelope.scope.projectId !== scope.projectId ||
      envelope.scope.game !== scope.game ||
      envelope.scope.profileId !== scope.profileId
    ) {
      throw new RowClipboardError('invalid-scope');
    }
    this.clipboard = envelope;
    this.clipboardAdapterSignature = registered.signature;
    return envelope;
  }

  public bindPreview(request: RowClipboardPreviewRequestV1) {
    const clipboard = this.requireClipboard();
    const registered = this.requireAdapter(clipboard.editor);
    if (registered.signature !== this.clipboardAdapterSignature) {
      this.clearClipboardState();
      throw new RowClipboardError('schema-incompatible');
    }
    if (!registered.registration.pasteModes.includes(request.mode)) {
      throw new RowClipboardError('operation-unavailable');
    }
    const targetRevision = requireRevision(request.targetRevision);
    const targetIdentity = normalizePreviewIdentity(request.targetIdentity);
    const preview = deepFreeze({
      atomicHistoryEvent: true as const,
      clipboardChecksum: clipboard.checksum,
      editor: clipboard.editor,
      mode: request.mode,
      operationCount: clipboard.rows.length,
      previewSchemaVersion: rowClipboardPreviewSchemaVersion,
      scope: clipboard.scope,
      targetIdentity,
      targetRevision
    });
    this.preview = preview;
    return preview;
  }

  public requireFreshPreview(
    preview: RowClipboardBoundPreviewV1,
    currentTargetRevision: string
  ): RowClipboardCommitAuthorizationV1 {
    const clipboard = this.requireClipboard();
    if (
      preview !== this.preview ||
      preview.clipboardChecksum !== clipboard.checksum ||
      preview.previewSchemaVersion !== rowClipboardPreviewSchemaVersion
    ) {
      throw new RowClipboardError('preview-mismatch');
    }
    const currentRevision = requireRevision(currentTargetRevision);
    if (preview.targetRevision !== currentRevision) {
      this.preview = null;
      throw new RowClipboardError('target-revision-stale');
    }
    const registered = this.requireAdapter(clipboard.editor);
    if (registered.signature !== this.clipboardAdapterSignature) {
      this.clearClipboardState();
      throw new RowClipboardError('schema-incompatible');
    }
    return deepFreeze({
      atomicHistoryEvent: true as const,
      envelope: clipboard,
      mode: preview.mode,
      operationCount: preview.operationCount,
      targetIdentity: preview.targetIdentity,
      targetRevision: preview.targetRevision
    });
  }

  public completePreview(preview: RowClipboardBoundPreviewV1) {
    if (preview !== this.preview) {
      throw new RowClipboardError('preview-mismatch');
    }
    this.preview = null;
  }

  public invalidatePreview() {
    this.preview = null;
  }

  public clear() {
    this.lifecycleRevision += 1;
    this.clearClipboardState();
  }

  public resetScope(scope: RowClipboardScope) {
    this.lifecycleRevision += 1;
    this.scope = normalizeRowClipboardScope(scope);
    this.clearClipboardState();
  }

  public updateScope(scope: RowClipboardScope): RowClipboardScopeUpdateResult {
    const next = normalizeRowClipboardScope(scope);
    if (next.projectId !== this.scope.projectId) {
      this.resetScope(next);
      return { kind: 'reset', reason: 'project-changed' };
    }
    if (next.game !== this.scope.game) {
      this.resetScope(next);
      return { kind: 'reset', reason: 'game-changed' };
    }
    if (next.profileId !== this.scope.profileId) {
      this.resetScope(next);
      return { kind: 'reset', reason: 'profile-changed' };
    }
    this.scope = next;
    return { kind: 'preserved' };
  }

  public replaceRegistrations(
    registrations: readonly RowClipboardAdapterRegistration[]
  ): RowClipboardRegistrationUpdateResult {
    const next = createRegistrationMap(registrations);
    this.lifecycleRevision += 1;
    if (!this.clipboard) {
      this.registrations = next;
      return { kind: 'preserved' };
    }
    const key = rowClipboardAdapterRegistrationKey(this.clipboard.editor);
    const replacement = next.get(key);
    this.registrations = next;
    if (!replacement || !rowClipboardRegistrationSupportsScope(replacement.registration, this.scope)) {
      this.clearClipboardState();
      return { kind: 'reset', reason: 'adapter-removed' };
    }
    if (replacement.signature !== this.clipboardAdapterSignature) {
      this.clearClipboardState();
      return { kind: 'reset', reason: 'schema-incompatible' };
    }
    return { kind: 'preserved' };
  }

  private requireAdapter(editor: RowClipboardEditorSchemaRef): RegisteredAdapter {
    const registration = this.registrations.get(
      rowClipboardAdapterRegistrationKey(editor)
    );
    if (
      !registration ||
      !rowClipboardRegistrationSupportsScope(registration.registration, this.scope)
    ) {
      throw new RowClipboardError('adapter-unavailable');
    }
    return registration;
  }

  private requireClipboard() {
    if (!this.clipboard) {
      throw new RowClipboardError('clipboard-empty');
    }
    return this.clipboard;
  }

  private clearClipboardState() {
    this.clipboard = null;
    this.clipboardAdapterSignature = null;
    this.preview = null;
  }
}

function createRegistrationMap(
  registrations: readonly RowClipboardAdapterRegistration[]
): ReadonlyMap<string, RegisteredAdapter> {
  const result = new Map<string, RegisteredAdapter>();
  for (const candidate of registrations) {
    const registration = normalizeRowClipboardAdapterRegistration(candidate);
    const key = rowClipboardAdapterRegistrationKey(registration);
    if (result.has(key)) {
      throw new RowClipboardError('invalid-adapter-registration');
    }
    result.set(
      key,
      Object.freeze({
        registration,
        signature: rowClipboardAdapterRegistrationSignature(registration)
      })
    );
  }
  return result;
}

function requireRevision(value: string): string {
  if (
    typeof value !== 'string' ||
    value.length === 0 ||
    value.length > 512 ||
    value !== value.trim() ||
    /\p{Cc}/u.test(value)
  ) {
    throw new RowClipboardError('invalid-revision');
  }
  return value;
}

function normalizePreviewIdentity(
  value: RowClipboardPreviewRequestV1['targetIdentity']
) {
  if (
    value === null ||
    typeof value !== 'object' ||
    Array.isArray(value) ||
    Object.keys(value).length !== 2 ||
    !Object.hasOwn(value, 'key') ||
    !Object.hasOwn(value, 'kind') ||
    typeof value.key !== 'string' ||
    typeof value.kind !== 'string' ||
    value.key.length === 0 ||
    value.key.length > 512 ||
    value.key !== value.key.trim() ||
    /\p{Cc}/u.test(value.key) ||
    !/^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$/u.test(value.kind)
  ) {
    throw new RowClipboardError('invalid-logical-identity');
  }
  return deepFreeze({ key: value.key, kind: value.kind });
}

function readUntrustedEditorSchema(input: unknown): RowClipboardEditorSchemaRef {
  if (input === null || typeof input !== 'object' || Array.isArray(input)) {
    throw new RowClipboardError('invalid-envelope');
  }
  const editor = (input as Record<string, unknown>).editor;
  if (editor === null || typeof editor !== 'object' || Array.isArray(editor)) {
    throw new RowClipboardError('invalid-editor-schema');
  }
  const record = editor as Record<string, unknown>;
  if (
    typeof record.editorId !== 'string' ||
    typeof record.rowKind !== 'string' ||
    typeof record.rowSchemaVersion !== 'number'
  ) {
    throw new RowClipboardError('invalid-editor-schema');
  }
  return {
    editorId: record.editorId,
    rowKind: record.rowKind,
    rowSchemaVersion: record.rowSchemaVersion
  };
}

function deepFreeze<T>(value: T): T {
  if (value !== null && typeof value === 'object' && !Object.isFrozen(value)) {
    Object.freeze(value);
    for (const child of Object.values(value)) {
      deepFreeze(child);
    }
  }
  return value;
}
