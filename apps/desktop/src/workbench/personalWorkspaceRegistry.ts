/* SPDX-License-Identifier: GPL-3.0-only */

import type { ProjectGame } from '../bridge/contracts';
import { ProjectBridgeError } from '../bridge/projectBridgeError';
import {
  readWorkspaceProjectStateRequestSchema,
  workspaceApplicationStateDocumentSchema,
  workspaceBookmarkSchema,
  workspaceGameDumpDestinationSchema,
  workspaceLocalePackSchema,
  workspaceMaximumBookmarks,
  workspaceMaximumLocalePacks,
  workspaceMaximumGameDumpDestinations,
  workspaceMaximumNotes,
  workspaceMaximumOutputProfiles,
  workspaceMaximumRecentProjects,
  workspaceMaximumRecentTargets,
  workspaceMaximumSavedViews,
  workspaceMaximumShortcutOverrides,
  workspaceOutputProfileSchema,
  workspacePersonalStateSchemaVersion,
  workspaceProjectNoteSchema,
  workspaceProjectPersonalStateDocumentSchema,
  workspaceRecentProjectProfileSchema,
  workspaceRecentTargetSchema,
  workspaceSavedViewSchema,
  workspaceBookmarkTargetKey,
  workspaceScopedLocationSchema,
  workspaceScopedLocationKey,
  workspaceShortcutOverrideSchema,
  type WorkspaceApplicationStateDocument,
  type WorkspaceBookmark,
  type WorkspaceLocalePack,
  type WorkspaceOutputProfile,
  type WorkspaceProjectNote,
  type WorkspaceProjectPersonalStateDocument,
  type WorkspaceRecentProjectProfile,
  type WorkspaceRecentTarget,
  type WorkspaceSavedView,
  type WorkspaceScopedLocation
} from '../bridge/workspacePersonalStateContracts';
import type { WorkspacePersonalStateProjectBridgeApi } from '../bridge/workspacePersonalStateProjectBridge';
import { projectBridgeErrorCodes } from '../errorCodes';
import {
  LocalePackValidationError,
  validateCommunityLocalePack,
  type CommunityLocalePack,
  type LocalePackValidationFailureCode
} from '../localization/localePackContracts';
import { PrivateWorkspaceConflictError } from './privateWorkspaceStorage';
import { createWorkspaceShortcutRegistry } from './shortcutRegistry';

export type PersonalWorkspaceSnapshot<TDocument> = {
  document: TDocument | null;
  etag: string | null;
};

export type PersonalWorkspaceRegistryOptions = {
  maxConflictRetries?: number;
  now?: () => Date;
  onDiagnostic?: (diagnostic: PersonalWorkspaceRegistryDiagnostic) => void;
};

export type PersonalWorkspaceRegistryDiagnostic = {
  code: 'persisted-locale-packs-ignored';
  localePacks: readonly {
    failureCode: LocalePackValidationFailureCode;
    id: string;
  }[];
};

export type ProjectPersonalWorkspaceTarget = {
  game: ProjectGame;
  projectId: string;
};

type MutationResult<TDocument> = {
  changed: boolean;
  document: TDocument;
};

const applicationOperationKey = 'application';
const defaultMaxConflictRetries = 3;
const maximumConflictRetries = 3;

export class PersonalWorkspaceRegistry {
  private readonly bridge: WorkspacePersonalStateProjectBridgeApi;
  private readonly maxConflictRetries: number;
  private readonly now: () => Date;
  private readonly onDiagnostic: ((diagnostic: PersonalWorkspaceRegistryDiagnostic) => void) | undefined;
  private readonly pendingOperations = new Map<string, Promise<void>>();

  public constructor(
    bridge: WorkspacePersonalStateProjectBridgeApi,
    options: PersonalWorkspaceRegistryOptions = {}
  ) {
    this.bridge = bridge;
    this.maxConflictRetries = options.maxConflictRetries ?? defaultMaxConflictRetries;
    assertNonNegativeInteger(this.maxConflictRetries, 'maxConflictRetries');
    if (this.maxConflictRetries > maximumConflictRetries) {
      throw new Error(`maxConflictRetries cannot exceed ${maximumConflictRetries}.`);
    }
    this.now = options.now ?? (() => new Date());
    this.onDiagnostic = options.onDiagnostic;
  }

  public async readApplicationState(): Promise<
    PersonalWorkspaceSnapshot<WorkspaceApplicationStateDocument>
  > {
    await this.waitForPendingOperation(applicationOperationKey);
    return this.readApplicationStateUnsafe();
  }

  public async readProjectState(
    projectId: string
  ): Promise<PersonalWorkspaceSnapshot<WorkspaceProjectPersonalStateDocument>> {
    const validatedProjectId = validateProjectId(projectId);
    const operationKey = projectOperationKey(validatedProjectId);
    await this.waitForPendingOperation(operationKey);
    return this.readProjectStateUnsafe(validatedProjectId);
  }

  public recordRecentProject(
    profile: WorkspaceRecentProjectProfile
  ): Promise<PersonalWorkspaceSnapshot<WorkspaceApplicationStateDocument>> {
    const validatedProfile = workspaceRecentProjectProfileSchema.parse(profile);
    return this.mutateApplicationState(
      (document) => document.recentProjects.find(
        (candidate) => candidate.projectId === validatedProfile.projectId
      ),
      (document) => ({
        changed: true,
        document: {
          ...document,
          recentProjects: [
            validatedProfile,
            ...document.recentProjects.filter(
              (candidate) => candidate.projectId !== validatedProfile.projectId
            )
          ].slice(0, workspaceMaximumRecentProjects),
          updatedAtUtc: this.timestamp()
        }
      })
    );
  }

  public removeRecentProject(
    projectId: string
  ): Promise<PersonalWorkspaceSnapshot<WorkspaceApplicationStateDocument>> {
    const validatedProjectId = validateProjectId(projectId);
    return this.mutateApplicationState(
      (document) => document.recentProjects.find(
        (candidate) => candidate.projectId === validatedProjectId
      ),
      (document) => removeApplicationEntry(
        document,
        'recentProjects',
        (candidate) => candidate.projectId === validatedProjectId,
        this.timestamp()
      )
    );
  }

  public setShortcutOverride(
    commandId: string,
    shortcut: string
  ): Promise<PersonalWorkspaceSnapshot<WorkspaceApplicationStateDocument>> {
    const entry = workspaceShortcutOverrideSchema.parse({
      commandId,
      shortcut,
      updatedAtUtc: this.timestamp()
    });
    return this.mutateApplicationState(
      (document) => document.shortcutOverrides.find(
        (candidate) => candidate.commandId === entry.commandId
      ),
      (document) => {
        const remaining = document.shortcutOverrides.filter(
          (candidate) => candidate.commandId !== entry.commandId
        );
        assertReplacementCapacity(
          remaining.length,
          workspaceMaximumShortcutOverrides,
          'shortcut overrides'
        );
        const shortcutOverrides = [...remaining, entry];
        assertEffectiveShortcutRegistry(shortcutOverrides);
        return {
          changed: true,
          document: {
            ...document,
            shortcutOverrides,
            updatedAtUtc: this.timestamp()
          }
        };
      }
    );
  }

  public removeShortcutOverride(
    commandId: string
  ): Promise<PersonalWorkspaceSnapshot<WorkspaceApplicationStateDocument>> {
    const validatedCommandId = workspaceShortcutOverrideSchema.shape.commandId.parse(commandId);
    return this.mutateApplicationState(
      (document) => document.shortcutOverrides.find(
        (candidate) => candidate.commandId === validatedCommandId
      ),
      (document) => {
        const shortcutOverrides = document.shortcutOverrides.filter(
          (candidate) => candidate.commandId !== validatedCommandId
        );
        if (shortcutOverrides.length === document.shortcutOverrides.length) {
          return { changed: false, document };
        }
        assertEffectiveShortcutRegistry(shortcutOverrides);
        return {
          changed: true,
          document: {
            ...document,
            shortcutOverrides,
            updatedAtUtc: this.timestamp()
          }
        };
      }
    );
  }

  public setGameDumpDestination(
    game: ProjectGame,
    destinationPath: string | null
  ): Promise<PersonalWorkspaceSnapshot<WorkspaceApplicationStateDocument>> {
    const validatedGame = workspaceGameDumpDestinationSchema.shape.game.parse(game);
    if (destinationPath === null) {
      return this.mutateApplicationState(
        (document) => document.gameDumpDestinations.find(
          (candidate) => candidate.game === validatedGame
        ),
        (document) => removeApplicationEntry(
          document,
          'gameDumpDestinations',
          (candidate) => candidate.game === validatedGame,
          this.timestamp()
        )
      );
    }

    const destination = workspaceGameDumpDestinationSchema.parse({
      destinationPath,
      game: validatedGame,
      updatedAtUtc: this.timestamp()
    });
    return this.mutateApplicationState(
      (document) => document.gameDumpDestinations.find(
        (candidate) => candidate.game === validatedGame
      ),
      (document) => {
        const remaining = document.gameDumpDestinations.filter(
          (candidate) => candidate.game !== validatedGame
        );
        assertReplacementCapacity(
          remaining.length,
          workspaceMaximumGameDumpDestinations,
          'Game Dump destinations'
        );
        return {
          changed: true,
          document: {
            ...document,
            gameDumpDestinations: [...remaining, destination],
            updatedAtUtc: this.timestamp()
          }
        };
      }
    );
  }

  public installLocalePack(
    pack: CommunityLocalePack
  ): Promise<PersonalWorkspaceSnapshot<WorkspaceApplicationStateDocument>> {
    const validatedPack = workspaceLocalePackSchema.parse(validateCommunityLocalePack(pack));
    return this.mutateApplicationState(
      (document) => document.localePacks.filter(
        (candidate) =>
          candidate.id === validatedPack.id ||
          candidate.localeTag.toLowerCase() === validatedPack.localeTag.toLowerCase()
      ),
      (document) => {
        const remaining = document.localePacks.filter(
          (candidate) =>
            candidate.id !== validatedPack.id &&
            candidate.localeTag.toLowerCase() !== validatedPack.localeTag.toLowerCase()
        );
        assertReplacementCapacity(
          remaining.length,
          workspaceMaximumLocalePacks,
          'community locale packs'
        );
        return {
          changed: true,
          document: {
            ...document,
            localePacks: [...remaining, validatedPack],
            updatedAtUtc: this.timestamp()
          }
        };
      }
    );
  }

  public removeLocalePack(
    packId: string
  ): Promise<PersonalWorkspaceSnapshot<WorkspaceApplicationStateDocument>> {
    const validatedPackId = workspaceLocalePackSchema.shape.id.parse(packId);
    return this.mutateApplicationState(
      (document) => document.localePacks.find((candidate) => candidate.id === validatedPackId),
      (document) => removeApplicationEntry(
        document,
        'localePacks',
        (candidate) => candidate.id === validatedPackId,
        this.timestamp()
      )
    );
  }

  public saveBookmark(
    target: ProjectPersonalWorkspaceTarget,
    bookmark: WorkspaceBookmark
  ): Promise<PersonalWorkspaceSnapshot<WorkspaceProjectPersonalStateDocument>> {
    const validatedBookmark = workspaceBookmarkSchema.parse(bookmark);
    const canonicalBookmark = canonicalizePinBookmark(validatedBookmark);
    const targetKey = bookmarkTargetKey(canonicalBookmark);
    return this.mutateProjectState(
      target,
      (document) => document.bookmarks.filter(
        (candidate) =>
          candidate.bookmarkId === canonicalBookmark.bookmarkId ||
          bookmarkTargetKey(candidate) === targetKey
      ),
      (document) => {
        const remaining = document.bookmarks.filter(
          (candidate) =>
            candidate.bookmarkId !== canonicalBookmark.bookmarkId &&
            bookmarkTargetKey(candidate) !== targetKey
        );
        assertReplacementCapacity(remaining.length, workspaceMaximumBookmarks, 'bookmarks');
        return {
          changed: true,
          document: {
            ...document,
            bookmarks: [...remaining, canonicalBookmark],
            updatedAtUtc: this.timestamp()
          }
        };
      }
    );
  }

  public removeBookmark(
    target: ProjectPersonalWorkspaceTarget,
    bookmarkId: string
  ): Promise<PersonalWorkspaceSnapshot<WorkspaceProjectPersonalStateDocument>> {
    const validatedBookmarkId = workspaceBookmarkSchema.shape.bookmarkId.parse(bookmarkId);
    return this.mutateProjectState(
      target,
      (document) => document.bookmarks.find(
        (candidate) => candidate.bookmarkId === validatedBookmarkId
      ),
      (document) => {
        const selected = document.bookmarks.find(
          (candidate) => candidate.bookmarkId === validatedBookmarkId
        );
        if (!selected) return { changed: false, document };
        const selectedPinKey = selected.kind === 'pin' ? bookmarkTargetKey(selected) : null;
        return {
          changed: true,
          document: {
            ...document,
            bookmarks: document.bookmarks.filter(
              (candidate) =>
                candidate.bookmarkId !== validatedBookmarkId &&
                (selectedPinKey === null ||
                  candidate.kind !== 'pin' ||
                  bookmarkTargetKey(candidate) !== selectedPinKey)
            ),
            updatedAtUtc: this.timestamp()
          }
        };
      }
    );
  }

  public saveNote(
    target: ProjectPersonalWorkspaceTarget,
    note: WorkspaceProjectNote
  ): Promise<PersonalWorkspaceSnapshot<WorkspaceProjectPersonalStateDocument>> {
    const validatedNote = workspaceProjectNoteSchema.parse(note);
    const locationKey = scopedLocationKey(validatedNote.location);
    return this.mutateProjectState(
      target,
      (document) => document.notes.filter(
        (candidate) =>
          candidate.noteId === validatedNote.noteId ||
          scopedLocationKey(candidate.location) === locationKey
      ),
      (document) => {
        const remaining = document.notes.filter(
          (candidate) =>
            candidate.noteId !== validatedNote.noteId &&
            scopedLocationKey(candidate.location) !== locationKey
        );
        assertReplacementCapacity(remaining.length, workspaceMaximumNotes, 'project notes');
        return {
          changed: true,
          document: {
            ...document,
            notes: [...remaining, validatedNote],
            updatedAtUtc: this.timestamp()
          }
        };
      }
    );
  }

  public removeNote(
    target: ProjectPersonalWorkspaceTarget,
    noteId: string
  ): Promise<PersonalWorkspaceSnapshot<WorkspaceProjectPersonalStateDocument>> {
    const validatedNoteId = workspaceProjectNoteSchema.shape.noteId.parse(noteId);
    return this.removeProjectEntry(target, 'notes', validatedNoteId, (entry) => entry.noteId);
  }

  public saveView(
    target: ProjectPersonalWorkspaceTarget,
    view: WorkspaceSavedView
  ): Promise<PersonalWorkspaceSnapshot<WorkspaceProjectPersonalStateDocument>> {
    const validatedView = workspaceSavedViewSchema.parse(view);
    return this.upsertProjectEntry(
      target,
      'savedViews',
      validatedView,
      (entry) => entry.viewId,
      workspaceMaximumSavedViews,
      'saved views'
    );
  }

  public removeView(
    target: ProjectPersonalWorkspaceTarget,
    viewId: string
  ): Promise<PersonalWorkspaceSnapshot<WorkspaceProjectPersonalStateDocument>> {
    const validatedViewId = workspaceSavedViewSchema.shape.viewId.parse(viewId);
    return this.removeProjectEntry(target, 'savedViews', validatedViewId, (entry) => entry.viewId);
  }

  public recordRecentTarget(
    target: ProjectPersonalWorkspaceTarget,
    recentTarget: WorkspaceRecentTarget
  ): Promise<PersonalWorkspaceSnapshot<WorkspaceProjectPersonalStateDocument>> {
    const validatedRecentTarget = workspaceRecentTargetSchema.parse(recentTarget);
    const canonicalRecentTarget = workspaceRecentTargetSchema.parse({
      ...validatedRecentTarget,
      location: withoutScopedLocationInspector(validatedRecentTarget.location)
    });
    const targetKey = recentTargetLocationKey(canonicalRecentTarget.location);
    return this.mutateProjectState(
      target,
      (document) => document.recentTargets.find(
        (candidate) => recentTargetLocationKey(candidate.location) === targetKey
      ),
      (document) => ({
        changed: true,
        document: {
          ...document,
          recentTargets: [
            canonicalRecentTarget,
            ...document.recentTargets.filter(
              (candidate) => recentTargetLocationKey(candidate.location) !== targetKey
            )
          ].slice(0, workspaceMaximumRecentTargets),
          updatedAtUtc: this.timestamp()
        }
      })
    );
  }

  public removeRecentTarget(
    target: ProjectPersonalWorkspaceTarget,
    location: WorkspaceScopedLocation
  ): Promise<PersonalWorkspaceSnapshot<WorkspaceProjectPersonalStateDocument>> {
    const validatedLocation = workspaceScopedLocationSchema.parse(location);
    const targetKey = recentTargetLocationKey(validatedLocation);
    return this.removeProjectEntry(
      target,
      'recentTargets',
      targetKey,
      (entry) => recentTargetLocationKey(entry.location)
    );
  }

  public saveOutputProfile(
    target: ProjectPersonalWorkspaceTarget,
    profile: WorkspaceOutputProfile
  ): Promise<PersonalWorkspaceSnapshot<WorkspaceProjectPersonalStateDocument>> {
    const validatedProfile = workspaceOutputProfileSchema.parse(profile);
    return this.mutateProjectState(
      target,
      (document) => document.outputProfiles.find(
        (candidate) => candidate.profileId === validatedProfile.profileId
      ),
      (document) => {
        const currentProfile = document.outputProfiles.find(
          (candidate) => candidate.profileId === validatedProfile.profileId
        );
        if (
          document.activeOutputProfileId === validatedProfile.profileId &&
          currentProfile !== undefined &&
          (currentProfile.outputRootPath !== validatedProfile.outputRootPath ||
            currentProfile.outputMode !== validatedProfile.outputMode)
        ) {
          throw new Error('Relocate output before changing the active output profile target.');
        }
        const remaining = document.outputProfiles.filter(
          (candidate) => candidate.profileId !== validatedProfile.profileId
        );
        assertReplacementCapacity(
          remaining.length,
          workspaceMaximumOutputProfiles,
          'output profiles'
        );
        return {
          changed: true,
          document: {
            ...document,
            outputProfiles: [...remaining, validatedProfile],
            updatedAtUtc: this.timestamp()
          }
        };
      }
    );
  }

  public removeOutputProfile(
    target: ProjectPersonalWorkspaceTarget,
    profileId: string
  ): Promise<PersonalWorkspaceSnapshot<WorkspaceProjectPersonalStateDocument>> {
    const validatedProfileId = workspaceOutputProfileSchema.shape.profileId.parse(profileId);
    return this.mutateProjectState(
      target,
      (document) => ({
        activeOutputProfileId: document.activeOutputProfileId,
        profile: document.outputProfiles.find(
          (candidate) => candidate.profileId === validatedProfileId
        )
      }),
      (document) => {
        const nextProfiles = document.outputProfiles.filter(
          (candidate) => candidate.profileId !== validatedProfileId
        );
        if (nextProfiles.length === document.outputProfiles.length) {
          return { changed: false, document };
        }
        return {
          changed: true,
          document: {
            ...document,
            activeOutputProfileId:
              document.activeOutputProfileId === validatedProfileId
                ? null
                : document.activeOutputProfileId,
            outputProfiles: nextProfiles,
            updatedAtUtc: this.timestamp()
          }
        };
      }
    );
  }

  public setActiveOutputProfile(
    target: ProjectPersonalWorkspaceTarget,
    profileId: string | null,
    appliedOutputRootPath: string | null
  ): Promise<PersonalWorkspaceSnapshot<WorkspaceProjectPersonalStateDocument>> {
    const validatedProfileId = profileId === null
      ? null
      : workspaceOutputProfileSchema.shape.profileId.parse(profileId);
    return this.mutateProjectState(
      target,
      (document) => ({
        activeOutputProfileId: document.activeOutputProfileId,
        profile: validatedProfileId === null
          ? undefined
          : document.outputProfiles.find(
            (candidate) => candidate.profileId === validatedProfileId
          )
      }),
      (document) => {
        if (validatedProfileId === null) {
          return document.activeOutputProfileId === null
            ? { changed: false, document }
            : {
                changed: true,
                document: {
                  ...document,
                  activeOutputProfileId: null,
                  updatedAtUtc: this.timestamp()
                }
              };
        }

        const profile = document.outputProfiles.find(
          (candidate) => candidate.profileId === validatedProfileId
        );
        if (!profile) {
          throw new Error('The requested output profile does not exist.');
        }
        if (appliedOutputRootPath === null || profile.outputRootPath !== appliedOutputRootPath) {
          throw new Error('The applied output path does not match the requested output profile.');
        }
        if (document.activeOutputProfileId === validatedProfileId) {
          return { changed: false, document };
        }
        return {
          changed: true,
          document: {
            ...document,
            activeOutputProfileId: validatedProfileId,
            updatedAtUtc: this.timestamp()
          }
        };
      }
    );
  }

  public deleteProjectState(projectId: string): Promise<boolean> {
    const validatedProjectId = validateProjectId(projectId);
    const operationKey = projectOperationKey(validatedProjectId);
    return this.enqueueOperation(operationKey, async () => {
      const snapshot = await this.readProjectStateUnsafe(validatedProjectId);
      if (!snapshot.document) {
        return false;
      }
      try {
        const result = await this.bridge.deleteWorkspaceProjectState({
          expectedETag: snapshot.etag,
          projectId: validatedProjectId
        });
        return result.deleted;
      } catch (error) {
        throw mapWorkspaceConflict(error);
      }
    });
  }

  private mutateApplicationState(
    selectTarget: (document: WorkspaceApplicationStateDocument) => unknown,
    mutate: (
      document: WorkspaceApplicationStateDocument
    ) => MutationResult<WorkspaceApplicationStateDocument>
  ) {
    return this.enqueueOperation(applicationOperationKey, async () => {
      let initialTargetFingerprint: string | undefined;
      for (let conflictCount = 0; ; conflictCount += 1) {
        const snapshot = await this.readApplicationStateUnsafe();
        const document = snapshot.document ?? createEmptyApplicationState(this.timestamp());
        const targetFingerprint = mutationTargetFingerprint(selectTarget(document));
        if (conflictCount === 0) {
          initialTargetFingerprint = targetFingerprint;
        } else if (targetFingerprint !== initialTargetFingerprint) {
          throw new PrivateWorkspaceConflictError();
        }

        const mutation = mutate(document);
        if (!mutation.changed) {
          return snapshot.document ? snapshot : { document: null, etag: null };
        }
        const validatedDocument = parseApplicationDocument(mutation.document);
        try {
          const result = await this.bridge.writeWorkspaceApplicationState({
            document: validatedDocument,
            expectedETag: snapshot.etag
          });
          return { document: validatedDocument, etag: result.etag };
        } catch (error) {
          const mappedError = mapWorkspaceConflict(error);
          if (
            !(mappedError instanceof PrivateWorkspaceConflictError) ||
            conflictCount >= this.maxConflictRetries
          ) {
            throw mappedError;
          }
        }
      }
    });
  }

  private mutateProjectState(
    target: ProjectPersonalWorkspaceTarget,
    selectTarget: (document: WorkspaceProjectPersonalStateDocument) => unknown,
    mutate: (
      document: WorkspaceProjectPersonalStateDocument
    ) => MutationResult<WorkspaceProjectPersonalStateDocument>
  ) {
    const validatedTarget = validateProjectTarget(target);
    const operationKey = projectOperationKey(validatedTarget.projectId);
    return this.enqueueOperation(operationKey, async () => {
      let initialTargetFingerprint: string | undefined;
      for (let conflictCount = 0; ; conflictCount += 1) {
        const snapshot = await this.readProjectStateUnsafe(validatedTarget.projectId);
        const document = snapshot.document ?? createEmptyProjectState(
          validatedTarget.game,
          this.timestamp()
        );
        if (document.game !== validatedTarget.game) {
          throw new Error('Project personal state belongs to a different game.');
        }
        const targetFingerprint = mutationTargetFingerprint(selectTarget(document));
        if (conflictCount === 0) {
          initialTargetFingerprint = targetFingerprint;
        } else if (targetFingerprint !== initialTargetFingerprint) {
          throw new PrivateWorkspaceConflictError();
        }

        const mutation = mutate(document);
        if (!mutation.changed) {
          return snapshot.document ? snapshot : { document: null, etag: null };
        }
        const validatedDocument = workspaceProjectPersonalStateDocumentSchema.parse(
          mutation.document
        );
        try {
          if (isEmptyProjectState(validatedDocument)) {
            if (!snapshot.document) {
              return { document: null, etag: null };
            }
            const result = await this.bridge.deleteWorkspaceProjectState({
              expectedETag: snapshot.etag,
              projectId: validatedTarget.projectId
            });
            if (!result.deleted) {
              throw new PrivateWorkspaceConflictError();
            }
            return { document: null, etag: null };
          }
          const result = await this.bridge.writeWorkspaceProjectState({
            document: validatedDocument,
            expectedETag: snapshot.etag,
            projectId: validatedTarget.projectId
          });
          return { document: validatedDocument, etag: result.etag };
        } catch (error) {
          const mappedError = mapWorkspaceConflict(error);
          if (
            !(mappedError instanceof PrivateWorkspaceConflictError) ||
            conflictCount >= this.maxConflictRetries
          ) {
            throw mappedError;
          }
        }
      }
    });
  }

  private upsertProjectEntry<
    TKey extends 'bookmarks' | 'notes' | 'savedViews' | 'outputProfiles'
  >(
    target: ProjectPersonalWorkspaceTarget,
    collectionKey: TKey,
    entry: WorkspaceProjectPersonalStateDocument[TKey][number],
    getId: (candidate: WorkspaceProjectPersonalStateDocument[TKey][number]) => string,
    maximumCount: number,
    label: string
  ) {
    const entryId = getId(entry);
    return this.mutateProjectState(
      target,
      (document) => document[collectionKey].find((candidate) => getId(candidate) === entryId),
      (document) => {
        const remaining = document[collectionKey].filter(
          (candidate) => getId(candidate) !== entryId
        );
        assertReplacementCapacity(remaining.length, maximumCount, label);
        return {
          changed: true,
          document: {
            ...document,
            [collectionKey]: [...remaining, entry],
            updatedAtUtc: this.timestamp()
          }
        } as MutationResult<WorkspaceProjectPersonalStateDocument>;
      }
    );
  }

  private removeProjectEntry<
    TKey extends 'bookmarks' | 'notes' | 'savedViews' | 'recentTargets'
  >(
    target: ProjectPersonalWorkspaceTarget,
    collectionKey: TKey,
    entryId: string,
    getId: (candidate: WorkspaceProjectPersonalStateDocument[TKey][number]) => string
  ) {
    return this.mutateProjectState(
      target,
      (document) => document[collectionKey].find((candidate) => getId(candidate) === entryId),
      (document) => {
        const remaining = document[collectionKey].filter(
          (candidate) => getId(candidate) !== entryId
        );
        if (remaining.length === document[collectionKey].length) {
          return { changed: false, document };
        }
        return {
          changed: true,
          document: {
            ...document,
            [collectionKey]: remaining,
            updatedAtUtc: this.timestamp()
          }
        } as MutationResult<WorkspaceProjectPersonalStateDocument>;
      }
    );
  }

  private async readApplicationStateUnsafe(): Promise<
    PersonalWorkspaceSnapshot<WorkspaceApplicationStateDocument>
  > {
    const response = await this.bridge.readWorkspaceApplicationState();
    return {
      document: response.document
        ? parseApplicationDocumentForRead(response.document, (diagnostic) => {
            try {
              this.onDiagnostic?.(diagnostic);
            } catch {
              // Diagnostics must not make otherwise valid workspace state unavailable.
            }
          })
        : null,
      etag: response.etag
    };
  }

  private async readProjectStateUnsafe(
    projectId: string
  ): Promise<PersonalWorkspaceSnapshot<WorkspaceProjectPersonalStateDocument>> {
    const response = await this.bridge.readWorkspaceProjectState({ projectId });
    return {
      document: response.document
        ? normalizeProjectDocumentForRead(
            workspaceProjectPersonalStateDocumentSchema.parse(response.document)
          )
        : null,
      etag: response.etag
    };
  }

  private enqueueOperation<T>(operationKey: string, operation: () => Promise<T>) {
    const previousOperation = this.pendingOperations.get(operationKey)?.catch(() => undefined);
    const result = (previousOperation ?? Promise.resolve()).then(operation);
    const completion = result.then(
      () => undefined,
      () => undefined
    );
    this.pendingOperations.set(operationKey, completion);
    void completion.finally(() => {
      if (this.pendingOperations.get(operationKey) === completion) {
        this.pendingOperations.delete(operationKey);
      }
    });
    return result;
  }

  private async waitForPendingOperation(operationKey: string) {
    await this.pendingOperations.get(operationKey)?.catch(() => undefined);
  }

  private timestamp() {
    const timestamp = this.now();
    if (!Number.isFinite(timestamp.valueOf())) {
      throw new Error('The personal workspace clock returned an invalid date.');
    }
    return timestamp.toISOString();
  }
}

export function createBridgeBackedPersonalWorkspaceRegistry(
  bridge: WorkspacePersonalStateProjectBridgeApi,
  options: PersonalWorkspaceRegistryOptions = {}
) {
  return new PersonalWorkspaceRegistry(bridge, options);
}

function parseApplicationDocument(document: WorkspaceApplicationStateDocument) {
  const parsed = workspaceApplicationStateDocumentSchema.parse(document);
  const localePacks: WorkspaceLocalePack[] = parsed.localePacks.map((pack) =>
    workspaceLocalePackSchema.parse(validateCommunityLocalePack(pack))
  );
  return workspaceApplicationStateDocumentSchema.parse({ ...parsed, localePacks });
}

function parseApplicationDocumentForRead(
  document: WorkspaceApplicationStateDocument,
  onDiagnostic: (diagnostic: PersonalWorkspaceRegistryDiagnostic) => void
) {
  const parsed = workspaceApplicationStateDocumentSchema.parse(document);
  const localePacks: WorkspaceLocalePack[] = [];
  const ignoredLocalePacks: PersonalWorkspaceRegistryDiagnostic['localePacks'][number][] = [];
  for (const pack of parsed.localePacks) {
    try {
      localePacks.push(workspaceLocalePackSchema.parse(validateCommunityLocalePack(pack)));
    } catch (error) {
      if (!(error instanceof LocalePackValidationError)) {
        throw error;
      }
      ignoredLocalePacks.push({ failureCode: error.code, id: pack.id });
    }
  }
  if (ignoredLocalePacks.length > 0) {
    onDiagnostic({ code: 'persisted-locale-packs-ignored', localePacks: ignoredLocalePacks });
  }
  return workspaceApplicationStateDocumentSchema.parse({ ...parsed, localePacks });
}

function createEmptyApplicationState(updatedAtUtc: string): WorkspaceApplicationStateDocument {
  return {
    gameDumpDestinations: [],
    localePacks: [],
    recentProjects: [],
    schemaVersion: workspacePersonalStateSchemaVersion,
    shortcutOverrides: [],
    updatedAtUtc
  };
}

function createEmptyProjectState(
  game: ProjectGame,
  updatedAtUtc: string
): WorkspaceProjectPersonalStateDocument {
  return {
    activeOutputProfileId: null,
    bookmarks: [],
    game,
    notes: [],
    outputProfiles: [],
    recentTargets: [],
    savedViews: [],
    schemaVersion: workspacePersonalStateSchemaVersion,
    updatedAtUtc
  };
}

function removeApplicationEntry<
  TKey extends
    | 'recentProjects'
    | 'shortcutOverrides'
    | 'localePacks'
    | 'gameDumpDestinations'
>(
  document: WorkspaceApplicationStateDocument,
  collectionKey: TKey,
  remove: (entry: WorkspaceApplicationStateDocument[TKey][number]) => boolean,
  updatedAtUtc: string
): MutationResult<WorkspaceApplicationStateDocument> {
  const remaining = document[collectionKey].filter((entry) => !remove(entry));
  if (remaining.length === document[collectionKey].length) {
    return { changed: false, document };
  }
  return {
    changed: true,
    document: {
      ...document,
      [collectionKey]: remaining,
      updatedAtUtc
    }
  } as MutationResult<WorkspaceApplicationStateDocument>;
}

function isEmptyProjectState(document: WorkspaceProjectPersonalStateDocument) {
  return (
    document.activeOutputProfileId === null &&
    document.bookmarks.length === 0 &&
    document.notes.length === 0 &&
    document.outputProfiles.length === 0 &&
    document.recentTargets.length === 0 &&
    document.savedViews.length === 0
  );
}

function validateProjectTarget(target: ProjectPersonalWorkspaceTarget) {
  const projectId = validateProjectId(target.projectId);
  return workspaceProjectPersonalStateDocumentSchema.shape.game.parse(target.game) === target.game
    ? { game: target.game, projectId }
    : neverReached();
}

function validateProjectId(projectId: string) {
  return readWorkspaceProjectStateRequestSchema.shape.projectId.parse(projectId);
}

function projectOperationKey(projectId: string) {
  return `project:${projectId}`;
}

function assertEffectiveShortcutRegistry(
  overrides: readonly WorkspaceApplicationStateDocument['shortcutOverrides'][number][]
) {
  createWorkspaceShortcutRegistry(
    Object.fromEntries(overrides.map((entry) => [entry.commandId, entry.shortcut]))
  );
}

function scopedLocationKey(location: WorkspaceScopedLocation) {
  return workspaceScopedLocationKey(location);
}

function recentTargetLocationKey(location: WorkspaceScopedLocation) {
  return workspaceScopedLocationKey(withoutScopedLocationInspector(location));
}

function withoutScopedLocationInspector(
  location: WorkspaceScopedLocation
): WorkspaceScopedLocation {
  return {
    ...location,
    inspectorTab: null
  };
}

function canonicalizePinBookmark(bookmark: WorkspaceBookmark): WorkspaceBookmark {
  return bookmark.kind === 'pin'
    ? { ...bookmark, location: withoutScopedLocationInspector(bookmark.location) }
    : bookmark;
}

function bookmarkTargetKey(bookmark: WorkspaceBookmark) {
  return workspaceBookmarkTargetKey(canonicalizePinBookmark(bookmark));
}

function normalizeProjectDocumentForRead(
  document: WorkspaceProjectPersonalStateDocument
): WorkspaceProjectPersonalStateDocument {
  const seenBookmarks = new Set<string>();
  const bookmarks = document.bookmarks.flatMap((entry) => {
    const canonicalEntry = canonicalizePinBookmark(entry);
    const key = bookmarkTargetKey(canonicalEntry);
    if (seenBookmarks.has(key)) return [];
    seenBookmarks.add(key);
    return [canonicalEntry];
  });
  const seenLocations = new Set<string>();
  const recentTargets = document.recentTargets.flatMap((entry) => {
    const location = withoutScopedLocationInspector(entry.location);
    const key = workspaceScopedLocationKey(location);
    if (seenLocations.has(key)) return [];
    seenLocations.add(key);
    return [{ ...entry, location }];
  });
  return { ...document, bookmarks, recentTargets };
}

function mutationTargetFingerprint(target: unknown) {
  return target === undefined ? 'undefined' : `value:${stableJson(target)}`;
}

function stableJson(value: unknown): string {
  if (value === undefined) return 'undefined';
  if (Array.isArray(value)) return `[${value.map(stableJson).join(',')}]`;
  if (typeof value === 'object' && value !== null) {
    return `{${Object.entries(value)
      .sort(([left], [right]) => compareOrdinal(left, right))
      .map(([key, entry]) => `${JSON.stringify(key)}:${stableJson(entry)}`)
      .join(',')}}`;
  }
  return JSON.stringify(value);
}

function compareOrdinal(left: string, right: string) {
  return left < right ? -1 : left > right ? 1 : 0;
}

function assertReplacementCapacity(currentCount: number, maximumCount: number, label: string) {
  if (currentCount >= maximumCount) {
    throw new Error(`The personal workspace cannot retain more ${label}.`);
  }
}

function assertNonNegativeInteger(value: number, name: string) {
  if (!Number.isSafeInteger(value) || value < 0) {
    throw new Error(`${name} must be a non-negative integer.`);
  }
}

function mapWorkspaceConflict(error: unknown) {
  if (error instanceof PrivateWorkspaceConflictError) {
    return error;
  }
  if (
    error instanceof ProjectBridgeError &&
    error.semanticCode === projectBridgeErrorCodes.workspaceConcurrentModification
  ) {
    return new PrivateWorkspaceConflictError({ cause: error });
  }
  return error;
}

function neverReached(): never {
  throw new Error('Project personal state has an unsupported game.');
}
