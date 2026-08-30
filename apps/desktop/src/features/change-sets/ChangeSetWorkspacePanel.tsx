/* SPDX-License-Identifier: GPL-3.0-only */

import {
  Archive,
  ArchiveRestore,
  ArrowDown,
  ArrowUp,
  Check,
  ChevronRight,
  ClipboardList,
  Copy,
  Download,
  GitCompareArrows,
  Layers3,
  Plus,
  Redo2,
  RefreshCw,
  Save,
  Tags,
  Trash2,
  TriangleAlert,
  Undo2,
  Upload
} from 'lucide-react';
import {
  useEffect,
  useMemo,
  useRef,
  useState,
  type FormEvent,
  type KeyboardEvent
} from 'react';
import {
  changeSetMaximumDependencyCount,
  changeSetMaximumPortablePackageBytes
} from '../../bridge/changeSetContracts';
import { LoadingProgress } from '../../components/LoadingProgress';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import {
  areStringSetsEqual,
  reconcileEligibleDraftSelection,
  reconcileSourceBackedDraft,
  resolveSubmittedEditorDraft
} from '../../components/localEditorDraftState';
import { DiagnosticsSection } from '../../components/workflowPanels';
import { useLocalization } from '../../localization';
import type {
  ChangeSetBuildVariantViewModel,
  ChangeSetComparisonViewModel,
  ChangeSetOperationViewModel,
  ChangeSetViewModel,
  ChangeSetWorkspaceController
} from './changeSetWorkspaceTypes';
import {
  AdvancedAuthoringPanel,
  type AdvancedAuthoringPanelProps
} from './AdvancedAuthoringPanel';
import './changeSets.css';

export type ChangeSetWorkspacePanelProps = {
  advancedAuthoring?: AdvancedAuthoringPanelProps | null;
  controller: ChangeSetWorkspaceController;
};

export function ChangeSetWorkspacePanel({
  advancedAuthoring,
  controller
}: ChangeSetWorkspacePanelProps) {
  const { formatLocale, t } = useLocalization();
  const [showArchived, setShowArchived] = useState(false);
  const [newName, setNewName] = useState('');
  const [importError, setImportError] = useState<string | null>(null);
  const [isAdvancedAuthoringBusy, setIsAdvancedAuthoringBusy] = useState(false);
  usePublishCommonEditorError({
    domain: 'workflow.changeSets',
    field: 'import',
    message: importError
  });
  const importInputRef = useRef<HTMLInputElement>(null);
  const selectedChangeSet = useMemo(
    () => controller.changeSets.find((changeSet) => (
      changeSet.id === controller.selectedChangeSetId
    )) ?? null,
    [controller.changeSets, controller.selectedChangeSetId]
  );
  const visibleChangeSets = useMemo(
    () => controller.changeSets.filter((changeSet) => changeSet.isArchived === showArchived),
    [controller.changeSets, showArchived]
  );
  const isBusy =
    controller.busyAction !== null ||
    controller.externalBusy ||
    isAdvancedAuthoringBusy;
  const isReady = controller.readiness === 'ready';

  useEffect(() => {
    if (
      selectedChangeSet &&
      selectedChangeSet.isArchived !== showArchived
    ) {
      setShowArchived(selectedChangeSet.isArchived);
    }
  }, [selectedChangeSet, showArchived]);

  const handleCreate = async (event: FormEvent) => {
    event.preventDefault();
    const submittedName = newName;
    const name = submittedName.trim();
    if (!name || !isReady || isBusy) return;
    const created = await controller.onCreate(name);
    if (created) {
      setNewName((current) =>
        resolveSubmittedEditorDraft(current, submittedName, '')
      );
    }
  };

  return (
    <>
      <section
        aria-busy={isBusy || controller.readiness === 'loading' || undefined}
        aria-labelledby="change-sets-heading"
        className="panel wide-panel change-sets-workspace"
      >
        <header className="change-sets-heading">
          <div className="change-sets-heading-copy">
            <Layers3 aria-hidden="true" size={20} />
            <div>
              <h2 id="change-sets-heading">{t('changeSets.title')}</h2>
              <p>{t('changeSets.description')}</p>
            </div>
          </div>
          <div className="change-sets-heading-actions">
            <input
              accept="application/json,.json"
              className="change-set-hidden-input"
              disabled={!isReady || isBusy}
              id="change-set-import-file"
              onChange={(event) => {
                const input = event.currentTarget;
                const file = input.files?.[0];
                if (!file) return;
                if (file.size > changeSetMaximumPortablePackageBytes) {
                  setImportError(t('changeSets.importTooLarge'));
                  input.value = '';
                  return;
                }
                setImportError(null);
                void file.text().then(
                  (packageJson) => controller.onImport(packageJson, false),
                  () => setImportError(t('changeSets.importReadError'))
                ).finally(() => {
                  input.value = '';
                });
              }}
              ref={importInputRef}
              tabIndex={-1}
              type="file"
            />
            <button
              className="secondary-button compact-button"
              disabled={!isReady || isBusy}
              onClick={() => importInputRef.current?.click()}
              type="button"
            >
              <Upload aria-hidden="true" size={16} />
              <span>{t('changeSets.import')}</span>
            </button>
            <button
              aria-busy={isBusy || undefined}
              aria-label={t(isBusy ? 'changeSets.loading' : 'changeSets.refresh')}
              className="secondary-button compact-button"
              disabled={isBusy || controller.readiness === 'unavailable'}
              onClick={controller.onRefresh}
              type="button"
            >
              <RefreshCw aria-hidden="true" size={16} />
              <span>{t('changeSets.refresh')}</span>
            </button>
            <button
              aria-label={controller.undoLabel
                ? t('changeSets.undoNamed', { action: controller.undoLabel })
                : t('changeSets.undo')}
              className="secondary-button compact-button"
              disabled={!isReady || isBusy || !controller.canUndo}
              onClick={controller.onUndo}
              type="button"
            >
              <Undo2 aria-hidden="true" size={16} />
              <span>{t('changeSets.undo')}</span>
            </button>
            <button
              aria-label={controller.redoLabel
                ? t('changeSets.redoNamed', { action: controller.redoLabel })
                : t('changeSets.redo')}
              className="secondary-button compact-button"
              disabled={!isReady || isBusy || !controller.canRedo}
              onClick={controller.onRedo}
              type="button"
            >
              <Redo2 aria-hidden="true" size={16} />
              <span>{t('changeSets.redo')}</span>
            </button>
          </div>
        </header>

        <WorkspaceStatus controller={controller} />
        {isBusy && controller.readiness === 'ready' ? (
          <LoadingProgress className="is-compact" label={t('changeSets.loading')} />
        ) : null}
        {controller.unassignedOperationCount > 0 ||
        controller.legacyUnsupportedOperationCount > 0 ? (
          <div className="change-set-legacy-status" role="status">
            {controller.unassignedOperationCount > 0 ? (
              <span>{t('changeSets.unassignedOperations', {
                count: controller.unassignedOperationCount
              })}</span>
            ) : null}
            {controller.legacyUnsupportedOperationCount > 0 ? (
              <span>{t('changeSets.legacyUnsupportedOperations', {
                count: controller.legacyUnsupportedOperationCount
              })}</span>
            ) : null}
          </div>
        ) : null}
        {controller.requiredOutputProfileId ? (
          <div className="change-set-profile-mismatch" role="alert">
            <div>
              <strong>{t('changeSets.profileMismatch.title')}</strong>
              <span>{controller.requiredOutputProfileName
                ? t('changeSets.profileMismatch.named', {
                    name: controller.requiredOutputProfileName
                  })
                : t('changeSets.profileMismatch.unknown')}</span>
            </div>
            {controller.onRequestOutputProfileSwitch ? (
              <button
                className="secondary-button compact-button"
                disabled={isBusy}
                onClick={() => controller.onRequestOutputProfileSwitch?.(
                  controller.requiredOutputProfileId!
                )}
                type="button"
              >
                {t('changeSets.profileMismatch.switch')}
              </button>
            ) : null}
          </div>
        ) : null}
        {importError ? (
          <p className="change-set-local-error" role="alert">{importError}</p>
        ) : null}

        {isReady ? (
          <div className="change-sets-layout">
            <aside aria-label={t('changeSets.collectionLabel')} className="change-sets-sidebar">
              <div aria-label={t('changeSets.collectionView')} className="change-sets-tabs">
                <button
                  aria-pressed={!showArchived}
                  className={!showArchived ? 'is-active' : ''}
                  onClick={() => setShowArchived(false)}
                  type="button"
                >
                  {t('changeSets.current')}
                </button>
                <button
                  aria-pressed={showArchived}
                  className={showArchived ? 'is-active' : ''}
                  onClick={() => setShowArchived(true)}
                  type="button"
                >
                  {t('changeSets.archived')}
                </button>
              </div>

              {!showArchived ? (
                <form className="change-set-create" onSubmit={handleCreate}>
                  <label htmlFor="change-set-new-name">{t('changeSets.newName')}</label>
                  <div>
                    <input
                      autoComplete="off"
                      id="change-set-new-name"
                      maxLength={128}
                      onChange={(event) => setNewName(event.currentTarget.value)}
                      placeholder={t('changeSets.newPlaceholder')}
                      value={newName}
                    />
                    <button
                      aria-label={t('changeSets.create')}
                      className="primary-button compact-button"
                      disabled={isBusy || !newName.trim()}
                      type="submit"
                    >
                      <Plus aria-hidden="true" size={16} />
                    </button>
                  </div>
                </form>
              ) : null}

              <ChangeSetList
                changeSets={visibleChangeSets}
                controller={controller}
                formatLocale={formatLocale}
                isBusy={isBusy}
                showArchived={showArchived}
              />
            </aside>

            <main className="change-set-detail">
              {selectedChangeSet ? (
                <ChangeSetDetail
                  changeSet={selectedChangeSet}
                  controller={controller}
                  isBusy={isBusy}
                />
              ) : (
                <div className="change-sets-empty-detail">
                  <ClipboardList aria-hidden="true" size={28} />
                  <h3>{t('changeSets.selectTitle')}</h3>
                  <p>{t(showArchived ? 'changeSets.selectArchived' : 'changeSets.selectDescription')}</p>
                </div>
              )}
            </main>
          </div>
        ) : null}
      </section>

      {isReady && advancedAuthoring ? (
        <AdvancedAuthoringPanel
          {...advancedAuthoring}
          externalBusy={isBusy || advancedAuthoring.externalBusy}
          onBusyChange={setIsAdvancedAuthoringBusy}
        />
      ) : null}

      {controller.diagnostics.length > 0 ? (
        <DiagnosticsSection diagnostics={[...controller.diagnostics]} scrollAfterEntries={8} />
      ) : null}
    </>
  );
}

function WorkspaceStatus({ controller }: { controller: ChangeSetWorkspaceController }) {
  const { t } = useLocalization();
  if (controller.readiness === 'ready') {
    const active = controller.changeSets.find((changeSet) => (
      changeSet.id === controller.activeStagingTargetId
    ));
    const activeLabel = active
      ? formatCollisionAwareName(active, controller.changeSets)
      : null;
    return (
      <div
        className={`change-sets-status ${controller.canMaterialize ? 'is-ready' : 'is-blocked'}`}
        role="status"
      >
        {controller.canMaterialize
          ? <Check aria-hidden="true" size={16} />
          : <TriangleAlert aria-hidden="true" size={16} />}
        <div>
          <span>
            {active
              ? t('changeSets.activeTargetNamed', { name: activeLabel! })
              : t('changeSets.activeTargetMissing')}
          </span>
          <strong>{t(controller.canMaterialize
            ? 'changeSets.effectiveReady'
            : 'changeSets.effectiveBlocked')}</strong>
        </div>
      </div>
    );
  }

  const key = controller.readiness === 'loading'
    ? 'changeSets.loading'
    : controller.readiness === 'error'
      ? 'changeSets.loadError'
      : 'changeSets.unavailable';
  if (controller.readiness === 'loading') {
    return (
      <div className="change-sets-status is-loading">
        <LoadingProgress label={t(key)} />
      </div>
    );
  }
  return (
    <div className={`change-sets-status is-${controller.readiness}`} role="status">
      <span>{t(key)}</span>
    </div>
  );
}

function ChangeSetList({
  changeSets,
  controller,
  formatLocale,
  isBusy,
  showArchived
}: {
  changeSets: readonly ChangeSetViewModel[];
  controller: ChangeSetWorkspaceController;
  formatLocale: string;
  isBusy: boolean;
  showArchived: boolean;
}) {
  const { t } = useLocalization();
  const handleListKeyDown = (event: KeyboardEvent<HTMLUListElement>) => {
    if (!['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) return;
    const buttons = Array.from(
      event.currentTarget.querySelectorAll<HTMLButtonElement>('[data-change-set-select="true"]')
    );
    if (buttons.length === 0) return;
    const currentIndex = buttons.indexOf(document.activeElement as HTMLButtonElement);
    const nextIndex = event.key === 'Home'
      ? 0
      : event.key === 'End'
        ? buttons.length - 1
        : event.key === 'ArrowDown'
          ? Math.min(buttons.length - 1, currentIndex + 1)
          : Math.max(0, currentIndex < 0 ? buttons.length - 1 : currentIndex - 1);
    event.preventDefault();
    buttons[nextIndex]?.focus();
  };

  if (changeSets.length === 0) {
    return <p className="change-sets-empty">{t(showArchived ? 'changeSets.noArchived' : 'changeSets.empty')}</p>;
  }

  return (
    <ul className="change-set-list" onKeyDown={handleListKeyDown}>
      {changeSets.map((changeSet, index) => {
        const exactName = formatCollisionAwareName(changeSet, controller.changeSets);
        return (
          <li
            className={changeSet.id === controller.selectedChangeSetId ? 'is-selected' : ''}
            key={changeSet.id}
          >
            <div className="change-set-list-primary">
              {!changeSet.isArchived ? (
                <label className="change-set-enabled-toggle">
                  <input
                    aria-label={t(changeSet.isEnabled
                      ? 'changeSets.disableNamed'
                      : 'changeSets.enableNamed', { name: exactName })}
                    checked={changeSet.isEnabled}
                    disabled={isBusy}
                    id={`change-set-enabled-${changeSet.id}`}
                    onChange={(event) => controller.onSetEnabled(
                      changeSet.id,
                      event.currentTarget.checked
                    )}
                    type="checkbox"
                  />
                  <span aria-hidden="true" />
                </label>
              ) : null}
              <button
                aria-current={changeSet.id === controller.selectedChangeSetId ? 'true' : undefined}
                aria-label={exactName}
                className="change-set-select"
                data-change-set-select="true"
                data-localization-ignore="true"
                onClick={() => controller.setSelectedChangeSetId(changeSet.id)}
                type="button"
              >
                <span>
                  <strong>{exactName}</strong>
                  {changeSet.isActiveStagingTarget ? (
                    <small>{t('changeSets.activeBadge')}</small>
                  ) : null}
                </span>
                <ChevronRight aria-hidden="true" size={16} />
              </button>
            </div>
            <div className="change-set-list-meta">
              <span>{t('changeSets.operationCount', { count: changeSet.operationCount })}</span>
              <time dateTime={changeSet.updatedAtUtc}>
                {formatTimestamp(changeSet.updatedAtUtc, formatLocale)}
              </time>
              {changeSet.conflictCount > 0 ? (
                <span className="is-conflict">
                  {t('changeSets.conflictCount', { count: changeSet.conflictCount })}
                </span>
              ) : null}
              {changeSet.staleOperationCount > 0 ? (
                <span className="is-stale">
                  {t('changeSets.staleCount', { count: changeSet.staleOperationCount })}
                </span>
              ) : null}
            </div>
            {!changeSet.isArchived ? (
              <div className="change-set-reorder-actions">
                <button
                  aria-label={t('changeSets.moveUpNamed', { name: exactName })}
                  className="icon-button"
                  disabled={isBusy || index === 0}
                  onClick={() => controller.onMove(changeSet.id, 'up')}
                  type="button"
                >
                  <ArrowUp aria-hidden="true" size={14} />
                </button>
                <button
                  aria-label={t('changeSets.moveDownNamed', { name: exactName })}
                  className="icon-button"
                  disabled={isBusy || index === changeSets.length - 1}
                  onClick={() => controller.onMove(changeSet.id, 'down')}
                  type="button"
                >
                  <ArrowDown aria-hidden="true" size={14} />
                </button>
              </div>
            ) : null}
          </li>
        );
      })}
    </ul>
  );
}

function ChangeSetDetail({
  changeSet,
  controller,
  isBusy
}: {
  changeSet: ChangeSetViewModel;
  controller: ChangeSetWorkspaceController;
  isBusy: boolean;
}) {
  const { t } = useLocalization();
  const [name, setName] = useState(changeSet.name);
  const [notes, setNotes] = useState(changeSet.notes);
  const [tags, setTags] = useState(changeSet.tags.join(', '));
  const [dependencyIds, setDependencyIds] = useState<Set<string>>(
    () => new Set(changeSet.dependencyIds)
  );
  const [confirmDelete, setConfirmDelete] = useState(false);
  const parsedTags = parseTags(tags);
  usePublishCommonEditorError({
    domain: 'workflow.changeSets',
    field: 'tags',
    message: parsedTags ? null : t('changeSets.tagsInvalid')
  });
  const exactName = formatCollisionAwareName(changeSet, controller.changeSets);
  const sourceTags = changeSet.tags.join(', ');
  const sourceDependencyKey = [...changeSet.dependencyIds].sort().join('|');
  const sourceDetailRef = useRef({
    dependencyIds: new Set(changeSet.dependencyIds),
    id: changeSet.id,
    name: changeSet.name,
    notes: changeSet.notes,
    tags: sourceTags
  });

  useEffect(() => {
    const previous = sourceDetailRef.current;
    const nextTags = sourceTags;
    const nextDependencyIds = new Set(changeSet.dependencyIds);
    if (previous.id !== changeSet.id) {
      setName(changeSet.name);
      setNotes(changeSet.notes);
      setTags(nextTags);
      setDependencyIds(nextDependencyIds);
      setConfirmDelete(false);
    } else {
      setName((current) =>
        reconcileSourceBackedDraft(current, previous.name, changeSet.name, Object.is)
      );
      setNotes((current) =>
        reconcileSourceBackedDraft(current, previous.notes, changeSet.notes, Object.is)
      );
      setTags((current) =>
        reconcileSourceBackedDraft(current, previous.tags, nextTags, Object.is)
      );
      setDependencyIds((current) =>
        reconcileSourceBackedDraft(
          current,
          previous.dependencyIds,
          nextDependencyIds,
          areStringSetsEqual
        )
      );
    }
    sourceDetailRef.current = {
      dependencyIds: nextDependencyIds,
      id: changeSet.id,
      name: changeSet.name,
      notes: changeSet.notes,
      tags: nextTags
    };
  }, [
    changeSet.id,
    changeSet.name,
    changeSet.notes,
    changeSet.updatedAtUtc,
    sourceDependencyKey,
    sourceTags
  ]);

  const handleRename = (event: FormEvent) => {
    event.preventDefault();
    const nextName = name.trim();
    if (nextName && nextName !== changeSet.name) {
      controller.onRename(changeSet.id, nextName);
    }
  };
  const handleMetadata = (event: FormEvent) => {
    event.preventDefault();
    if (!parsedTags) return;
    controller.onUpdateMetadata(
      changeSet.id,
      notes.trim(),
      parsedTags,
      [...dependencyIds]
    );
  };
  const dependencyCandidates = controller.changeSets.filter((candidate) => (
    candidate.id !== changeSet.id && !candidate.isArchived
  ));

  return (
    <>
      <header className="change-set-detail-heading">
        <div>
          <h3 data-localization-ignore="true">{exactName}</h3>
          <p>{changeSet.isArchived
            ? t('changeSets.archivedDescription')
            : t(changeSet.isEnabled ? 'changeSets.enabledDescription' : 'changeSets.disabledDescription')}</p>
        </div>
        <div className="change-set-detail-actions">
          {changeSet.isArchived ? (
            <>
              <button
                aria-label={`${t('changeSets.restore')}: ${exactName}`}
                className="secondary-button compact-button"
                disabled={isBusy}
                onClick={() => controller.onRestore(changeSet.id)}
                type="button"
              >
                <ArchiveRestore aria-hidden="true" size={16} />
                <span>{t('changeSets.restore')}</span>
              </button>
              <button
                aria-label={confirmDelete
                  ? t('changeSets.confirmDeleteNamed', { name: exactName })
                  : t('changeSets.deleteNamed', { name: exactName })}
                className={confirmDelete
                  ? 'danger-button compact-button'
                  : 'secondary-button compact-button'}
                disabled={isBusy}
                onBlur={() => setConfirmDelete(false)}
                onClick={() => {
                  if (confirmDelete) {
                    controller.onDeleteSet(changeSet.id);
                    setConfirmDelete(false);
                  } else {
                    setConfirmDelete(true);
                  }
                }}
                onKeyDown={(event) => {
                  if (event.key === 'Escape') setConfirmDelete(false);
                }}
                type="button"
              >
                <Trash2 aria-hidden="true" size={16} />
                <span>{t(confirmDelete
                  ? 'changeSets.confirmDelete'
                  : 'changeSets.delete')}</span>
              </button>
            </>
          ) : (
            <>
              <button
                aria-label={`${t(changeSet.isActiveStagingTarget
                  ? 'changeSets.activeTarget'
                  : 'changeSets.makeActive')}: ${exactName}`}
                aria-pressed={changeSet.isActiveStagingTarget}
                className={changeSet.isActiveStagingTarget
                  ? 'secondary-button compact-button is-active'
                  : 'secondary-button compact-button'}
                disabled={isBusy || changeSet.isActiveStagingTarget}
                onClick={() => controller.onSetActiveStagingTarget(changeSet.id)}
                type="button"
              >
                <Check aria-hidden="true" size={16} />
                <span>{t(changeSet.isActiveStagingTarget
                  ? 'changeSets.activeTarget'
                  : 'changeSets.makeActive')}</span>
              </button>
              <button
                aria-label={`${t('changeSets.duplicate')}: ${exactName}`}
                className="secondary-button compact-button"
                disabled={isBusy}
                onClick={() => controller.onDuplicate(changeSet.id)}
                type="button"
              >
                <Copy aria-hidden="true" size={16} />
                <span>{t('changeSets.duplicate')}</span>
              </button>
              <button
                aria-label={`${t('changeSets.export')}: ${exactName}`}
                className="secondary-button compact-button"
                disabled={isBusy}
                onClick={() => controller.onExport(changeSet.id)}
                type="button"
              >
                <Download aria-hidden="true" size={16} />
                <span>{t('changeSets.export')}</span>
              </button>
              <button
                aria-label={`${t('changeSets.archive')}: ${exactName}`}
                className="secondary-button compact-button"
                disabled={isBusy}
                onClick={() => controller.onArchive(changeSet.id)}
                type="button"
              >
                <Archive aria-hidden="true" size={16} />
                <span>{t('changeSets.archive')}</span>
              </button>
            </>
          )}
        </div>
      </header>

      <div className="change-set-detail-grid">
        <section aria-labelledby="change-set-metadata-heading" className="change-set-card">
          <div className="change-set-card-heading">
            <Tags aria-hidden="true" size={17} />
            <h4 id="change-set-metadata-heading">{t('changeSets.details')}</h4>
          </div>
          <form className="change-set-fields" onSubmit={handleRename}>
            <label htmlFor="change-set-name">{t('changeSets.name')}</label>
            <div className="change-set-inline-field">
              <input
                disabled={changeSet.isArchived}
                id="change-set-name"
                maxLength={128}
                onChange={(event) => setName(event.currentTarget.value)}
                value={name}
              />
              <button
                aria-label={t('changeSets.rename')}
                className="secondary-button compact-button"
                disabled={isBusy || changeSet.isArchived || !name.trim() || name.trim() === changeSet.name}
                type="submit"
              >
                <Save aria-hidden="true" size={15} />
              </button>
            </div>
          </form>
          <form className="change-set-fields" onSubmit={handleMetadata}>
            <label htmlFor="change-set-tags">{t('changeSets.tags')}</label>
            <input
              disabled={changeSet.isArchived}
              id="change-set-tags"
              maxLength={2110}
              onChange={(event) => setTags(event.currentTarget.value)}
              placeholder={t('changeSets.tagsPlaceholder')}
              value={tags}
            />
            {!parsedTags ? (
              <p className="change-set-field-error" role="alert">
                {t('changeSets.tagsInvalid')}
              </p>
            ) : null}
            <label htmlFor="change-set-notes">{t('changeSets.notes')}</label>
            <textarea
              disabled={changeSet.isArchived}
              id="change-set-notes"
              maxLength={32768}
              onChange={(event) => setNotes(event.currentTarget.value)}
              placeholder={t('changeSets.notesPlaceholder')}
              rows={4}
              value={notes}
            />
            <fieldset className="change-set-dependencies">
              <legend>{t('changeSets.dependencies')}</legend>
              <p>{t('changeSets.dependenciesDescription')}</p>
              {dependencyCandidates.length > 0 ? (
                <ul>
                  {dependencyCandidates.map((candidate) => (
                    <li key={candidate.id}>
                      <label data-localization-ignore="true">
                        <input
                          checked={dependencyIds.has(candidate.id)}
                          className="km-choice-control"
                          disabled={
                            changeSet.isArchived ||
                            (!dependencyIds.has(candidate.id) &&
                              dependencyIds.size >= changeSetMaximumDependencyCount)
                          }
                          onChange={(event) => {
                            const checked = event.currentTarget.checked;
                            setDependencyIds((current) => {
                              const next = new Set(current);
                              if (checked) next.add(candidate.id);
                              else next.delete(candidate.id);
                              return next;
                            });
                          }}
                          type="checkbox"
                        />
                        <span>{formatCollisionAwareName(candidate, controller.changeSets)}</span>
                      </label>
                    </li>
                  ))}
                </ul>
              ) : (
                <p>{t('changeSets.dependenciesEmpty')}</p>
              )}
            </fieldset>
            <button
              className="secondary-button compact-button"
              disabled={isBusy || changeSet.isArchived || !parsedTags}
              type="submit"
            >
              <Save aria-hidden="true" size={16} />
              <span>{t('changeSets.saveDetails')}</span>
            </button>
          </form>
        </section>

        <BuildVariants
          changeSet={changeSet}
          controller={controller}
          isBusy={isBusy}
          variants={controller.buildVariants}
        />
      </div>

      {changeSet.conflicts.length > 0 ? (
        <section
          aria-labelledby="change-set-conflicts-heading"
          className="change-set-card change-set-conflicts"
        >
          <div className="change-set-card-heading">
            <TriangleAlert aria-hidden="true" size={17} />
            <h4 id="change-set-conflicts-heading">{t('changeSets.conflicts.title')}</h4>
          </div>
          <ul>
            {changeSet.conflicts.map((conflict) => (
              <li data-localization-ignore="true" key={conflict.id}>
                <strong>{conflict.message}</strong>
                {conflict.targetLabel ? <span>{conflict.targetLabel}</span> : null}
              </li>
            ))}
          </ul>
        </section>
      ) : null}

      <OperationList
        changeSet={changeSet}
        controller={controller}
        isBusy={isBusy}
      />

      <ChangeSetComparison
        changeSet={changeSet}
        comparison={controller.comparison}
        isBusy={isBusy}
        onLoad={() => controller.onLoadComparison(changeSet.id)}
      />
    </>
  );
}

function BuildVariants({
  changeSet,
  controller,
  isBusy,
  variants
}: {
  changeSet: ChangeSetViewModel;
  controller: ChangeSetWorkspaceController;
  isBusy: boolean;
  variants: readonly ChangeSetBuildVariantViewModel[];
}) {
  const { t } = useLocalization();
  const [name, setName] = useState('');
  const [outputMode, setOutputMode] = useState(
    controller.availableOutputModes[0]?.id ?? ''
  );
  const [outputProfileId, setOutputProfileId] = useState(
    controller.availableOutputProfiles.find((profile) => profile.isActive)?.id ?? ''
  );
  const enabledChangeSets = controller.changeSets.filter((candidate) => (
    candidate.isEnabled && !candidate.isArchived
  ));
  const enabledChangeSetKey = enabledChangeSets.map((candidate) => candidate.id).join('|');
  const enabledChangeSetIds = new Set(enabledChangeSets.map((candidate) => candidate.id));
  const [selectedVariantSetIds, setSelectedVariantSetIds] = useState<Set<string>>(
    enabledChangeSetIds
  );
  const previousEnabledChangeSetIdsRef = useRef(enabledChangeSetIds);
  useEffect(() => {
    if (!outputMode && controller.availableOutputModes[0]) {
      setOutputMode(controller.availableOutputModes[0].id);
    }
    if (
      outputProfileId &&
      !controller.availableOutputProfiles.some((profile) => profile.id === outputProfileId)
    ) {
      const activeProfileId = controller.availableOutputProfiles.find(
        (profile) => profile.isActive
      )?.id;
      setOutputProfileId(activeProfileId ?? '');
    }
  }, [controller.availableOutputModes, controller.availableOutputProfiles, outputMode, outputProfileId]);
  useEffect(() => {
    const previousEligibleIds = previousEnabledChangeSetIdsRef.current;
    setSelectedVariantSetIds((current) =>
      reconcileEligibleDraftSelection(current, previousEligibleIds, enabledChangeSetIds)
    );
    previousEnabledChangeSetIdsRef.current = enabledChangeSetIds;
  // The stable key intentionally reconciles only when set eligibility changes.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [enabledChangeSetKey]);
  const createVariant = async (event: FormEvent) => {
    event.preventDefault();
    const submittedName = name;
    const nextName = submittedName.trim();
    if (!nextName || isBusy) return;
    const enabledChangeSetIds = enabledChangeSets
      .filter((candidate) => selectedVariantSetIds.has(candidate.id))
      .map((candidate) => candidate.id);
    const created = await controller.onCreateBuildVariant(
      nextName,
      enabledChangeSetIds,
      outputMode || null,
      outputProfileId || null
    );
    if (created) {
      setName((current) =>
        resolveSubmittedEditorDraft(current, submittedName, '')
      );
    }
  };

  return (
    <section aria-labelledby="change-set-variants-heading" className="change-set-card">
      <div className="change-set-card-heading">
        <Layers3 aria-hidden="true" size={17} />
        <h4 id="change-set-variants-heading">{t('changeSets.variants.title')}</h4>
      </div>
      <p>{t('changeSets.variants.description')}</p>
      <button
        aria-pressed={!variants.some((variant) => variant.isActive)}
        className="secondary-button compact-button"
        disabled={isBusy || !variants.some((variant) => variant.isActive)}
        onClick={() => controller.onSelectBuildVariant(null)}
        type="button"
      >
        {t('changeSets.variants.useEnabledSets')}
      </button>
      {variants.length > 0 ? (
        <ul className="change-set-variant-list">
          {variants.map((variant) => (
            <li className={variant.isActive ? 'is-active' : ''} key={variant.id}>
              <button
                aria-pressed={variant.isActive}
                data-localization-ignore="true"
                disabled={isBusy}
                onClick={() => controller.onSelectBuildVariant(variant.id)}
                type="button"
              >
                <strong>{formatCollisionAwareName(variant, variants)}</strong>
                <span>{t('changeSets.variants.summary', {
                  count: variant.enabledChangeSetCount,
                  mode: variant.outputModeLabel
                })}</span>
                {variant.outputProfileName ? <span>{variant.outputProfileName}</span> : null}
              </button>
              <button
                aria-label={t('changeSets.variants.deleteNamed', {
                  name: formatCollisionAwareName(variant, variants)
                })}
                className="icon-button"
                disabled={isBusy || variant.isActive}
                onClick={() => controller.onDeleteBuildVariant(variant.id)}
                type="button"
              >
                <Trash2 aria-hidden="true" size={14} />
              </button>
            </li>
          ))}
        </ul>
      ) : (
        <p className="change-sets-empty">{t('changeSets.variants.empty')}</p>
      )}
      {!changeSet.isArchived ? (
        <form className="change-set-create-variant" onSubmit={createVariant}>
          <label htmlFor="change-set-new-variant">{t('changeSets.variants.newName')}</label>
          <div>
            <input
              id="change-set-new-variant"
              maxLength={128}
              onChange={(event) => setName(event.currentTarget.value)}
              placeholder={t('changeSets.variants.placeholder')}
              value={name}
            />
            <button
              aria-label={t('changeSets.variants.create')}
              className="secondary-button compact-button"
              disabled={isBusy || !name.trim()}
              type="submit"
            >
              <Plus aria-hidden="true" size={15} />
            </button>
          </div>
          <label htmlFor="change-set-variant-output-mode">
            {t('changeSets.variants.outputMode')}
          </label>
          <select
            className="km-select-control"
            disabled={controller.availableOutputModes.length === 0}
            id="change-set-variant-output-mode"
            onChange={(event) => setOutputMode(event.currentTarget.value)}
            value={outputMode}
          >
            {controller.availableOutputModes.map((mode) => (
              <option data-localization-ignore="true" key={mode.id} value={mode.id}>
                {mode.label}
              </option>
            ))}
          </select>
          <label htmlFor="change-set-variant-output-profile">
            {t('changeSets.variants.outputProfile')}
          </label>
          <select
            className="km-select-control"
            id="change-set-variant-output-profile"
            onChange={(event) => setOutputProfileId(event.currentTarget.value)}
            value={outputProfileId}
          >
            <option value="">{t('changeSets.variants.currentProfile')}</option>
            {controller.availableOutputProfiles.map((profile) => (
              <option data-localization-ignore="true" key={profile.id} value={profile.id}>
                {profile.name}
              </option>
            ))}
          </select>
          {enabledChangeSets.length > 0 ? (
            <fieldset className="change-set-dependencies">
              <legend>{t('changeSets.collectionLabel')}</legend>
              <ul>
                {enabledChangeSets.map((candidate) => (
                  <li key={candidate.id}>
                    <label data-localization-ignore="true">
                      <input
                        checked={selectedVariantSetIds.has(candidate.id)}
                        className="km-choice-control"
                        onChange={(event) => {
                          const checked = event.currentTarget.checked;
                          setSelectedVariantSetIds((current) => {
                            const next = new Set(current);
                            if (checked) next.add(candidate.id);
                            else next.delete(candidate.id);
                            return next;
                          });
                        }}
                        type="checkbox"
                      />
                      <span>{formatCollisionAwareName(candidate, controller.changeSets)}</span>
                    </label>
                  </li>
                ))}
              </ul>
            </fieldset>
          ) : null}
          <p>{t('changeSets.variants.boundTarget')}</p>
        </form>
      ) : null}
    </section>
  );
}

function OperationList({
  changeSet,
  controller,
  isBusy
}: {
  changeSet: ChangeSetViewModel;
  controller: ChangeSetWorkspaceController;
  isBusy: boolean;
}) {
  const { t } = useLocalization();

  return (
    <section aria-labelledby="change-set-operations-heading" className="change-set-card change-set-operations">
      <div className="change-set-card-heading change-set-operation-heading">
        <ClipboardList aria-hidden="true" size={17} />
        <h4 id="change-set-operations-heading">{t('changeSets.operations.title')}</h4>
        <span>{t('changeSets.operationCount', { count: changeSet.operationCount })}</span>
      </div>
      {changeSet.operations.length > 0 ? (
        <>
          <ol className="change-set-operation-list">
            {changeSet.operations.map((operation, index) => (
              <OperationRow
                isBusy={isBusy || changeSet.isArchived}
                key={operation.id}
                onMove={(direction) => controller.onMoveOperation(
                  changeSet.id,
                  operation.id,
                  direction
                )}
                onRemove={() => controller.onRemoveOperation(changeSet.id, operation.id)}
                operation={operation}
                position={index}
                total={changeSet.operations.length}
              />
            ))}
          </ol>
          {changeSet.operationsAreTruncated ? (
            <p className="change-set-bounded-note">{t('changeSets.operations.truncated')}</p>
          ) : null}
        </>
      ) : (
        <p className="change-sets-empty">{t('changeSets.operations.empty')}</p>
      )}
    </section>
  );
}

function OperationRow({
  isBusy,
  onMove,
  onRemove,
  operation,
  position,
  total
}: {
  isBusy: boolean;
  onMove: (direction: 'up' | 'down') => void;
  onRemove: () => void;
  operation: ChangeSetOperationViewModel;
  position: number;
  total: number;
}) {
  const { t } = useLocalization();
  const [confirmRemove, setConfirmRemove] = useState(false);
  return (
    <li className={`is-${operation.state}`}>
      <div className="change-set-operation-copy" data-localization-ignore="true">
        <strong>{operation.title}</strong>
        <span>{operation.targetLabel}</span>
        {operation.description ? <p>{operation.description}</p> : null}
        <small>{operation.adapterLabel} | {operation.provenanceLabel}</small>
      </div>
      <span className={`change-set-operation-state is-${operation.state}`}>
        {t(`changeSets.operations.state.${operation.state}`)}
      </span>
      <div className="change-set-operation-reorder">
        <button
          aria-label={t('changeSets.operations.moveUpNamed', { name: operation.title })}
          className="icon-button"
          disabled={isBusy || position === 0}
          onClick={() => onMove('up')}
          type="button"
        >
          <ArrowUp aria-hidden="true" size={14} />
        </button>
        <button
          aria-label={t('changeSets.operations.moveDownNamed', { name: operation.title })}
          className="icon-button"
          disabled={isBusy || position === total - 1}
          onClick={() => onMove('down')}
          type="button"
        >
          <ArrowDown aria-hidden="true" size={14} />
        </button>
      </div>
      <button
        aria-label={confirmRemove
          ? t('changeSets.operations.confirmRemoveNamed', { name: operation.title })
          : t('changeSets.operations.removeNamed', { name: operation.title })}
        className={confirmRemove
          ? 'change-set-operation-remove danger-button compact-button'
          : 'change-set-operation-remove icon-button'}
        disabled={isBusy}
        onBlur={() => setConfirmRemove(false)}
        onClick={() => {
          if (confirmRemove) {
            onRemove();
            setConfirmRemove(false);
          } else {
            setConfirmRemove(true);
          }
        }}
        type="button"
      >
        <Trash2 aria-hidden="true" size={14} />
        {confirmRemove ? <span>{t('changeSets.operations.confirmRemove')}</span> : null}
      </button>
    </li>
  );
}

function ChangeSetComparison({
  changeSet,
  comparison,
  isBusy,
  onLoad
}: {
  changeSet: ChangeSetViewModel;
  comparison: ChangeSetComparisonViewModel | null;
  isBusy: boolean;
  onLoad: () => void;
}) {
  const { t } = useLocalization();
  const [resultFilter, setResultFilter] = useState('');
  const [kindFilter, setKindFilter] = useState('all');
  const [resultOrder, setResultOrder] = useState<'target' | 'kind' | 'owner'>('target');
  const [showSelectedOnly, setShowSelectedOnly] = useState(false);
  const [selectedEntryKeys, setSelectedEntryKeys] = useState<Set<string>>(new Set());
  const currentComparison = comparison?.selectedChangeSetId === changeSet.id
    ? comparison
    : null;
  const entriesWithKeys = useMemo(() => currentComparison?.entries.map((entry, index) => ({
    entry,
    key: changeSetComparisonEntryKey(entry, index)
  })) ?? [], [currentComparison?.entries]);
  const comparisonEntryKeys = new Set(entriesWithKeys.map(({ key }) => key));
  const previousComparisonEntryKeysRef = useRef(comparisonEntryKeys);
  const comparisonIdentityLabels = useMemo(
    () => createComparisonIdentityLabels(currentComparison?.entries ?? []),
    [currentComparison?.entries]
  );
  useEffect(() => {
    const previousEligibleIds = previousComparisonEntryKeysRef.current;
    setSelectedEntryKeys((current) =>
      reconcileEligibleDraftSelection(current, previousEligibleIds, comparisonEntryKeys)
    );
    previousComparisonEntryKeysRef.current = comparisonEntryKeys;
  // entriesWithKeys is the stable comparison-result trigger.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [entriesWithKeys]);
  const kinds = useMemo(
    () => [...new Set(entriesWithKeys.map(({ entry }) => entry.kind))].sort(),
    [entriesWithKeys]
  );
  useEffect(() => {
    if (kindFilter !== 'all' && !kinds.some((kind) => kind === kindFilter)) {
      setKindFilter('all');
    }
  }, [kindFilter, kinds]);
  const matchingEntries = useMemo(() => {
    const normalizedFilter = resultFilter.trim().toLocaleLowerCase();
    return [...entriesWithKeys]
      .filter(({ entry }) => (
        (kindFilter === 'all' || entry.kind === kindFilter) &&
        (
          !normalizedFilter ||
          entry.targetLabel.toLocaleLowerCase().includes(normalizedFilter) ||
          entry.ownerLabel?.toLocaleLowerCase().includes(normalizedFilter) ||
          entry.operationId.toLocaleLowerCase().includes(normalizedFilter) ||
          entry.ownerId?.toLocaleLowerCase().includes(normalizedFilter) ||
          entry.leftValue?.toLocaleLowerCase().includes(normalizedFilter) ||
          entry.rightValue?.toLocaleLowerCase().includes(normalizedFilter)
        )
      ))
      .sort((left, right) => {
        if (resultOrder === 'kind') {
          return left.entry.kind.localeCompare(right.entry.kind) ||
            left.entry.targetLabel.localeCompare(right.entry.targetLabel) ||
            left.entry.operationId.localeCompare(right.entry.operationId);
        }
        if (resultOrder === 'owner') {
          return (left.entry.ownerLabel ?? '').localeCompare(right.entry.ownerLabel ?? '') ||
            left.entry.targetLabel.localeCompare(right.entry.targetLabel) ||
            left.entry.operationId.localeCompare(right.entry.operationId);
        }
        return left.entry.targetLabel.localeCompare(right.entry.targetLabel) ||
          left.entry.operationId.localeCompare(right.entry.operationId);
      });
  }, [entriesWithKeys, kindFilter, resultFilter, resultOrder]);
  const visibleEntries = showSelectedOnly
    ? matchingEntries.filter(({ key }) => selectedEntryKeys.has(key))
    : matchingEntries;
  const visibleKeys = matchingEntries.map(({ key }) => key);
  return (
    <section aria-labelledby="change-set-comparison-heading" className="change-set-card change-set-comparison">
      <div className="change-set-card-heading">
        <GitCompareArrows aria-hidden="true" size={17} />
        <h4 id="change-set-comparison-heading">{t('changeSets.comparison.title')}</h4>
      </div>
      <p>{t('changeSets.comparison.description')}</p>
      <button
        className="secondary-button compact-button"
        disabled={isBusy || changeSet.isArchived}
        onClick={onLoad}
        type="button"
      >
        <GitCompareArrows aria-hidden="true" size={16} />
        <span>{t('changeSets.comparison.load')}</span>
      </button>
      {currentComparison?.state === 'unavailable' ? (
        <p className="change-set-comparison-unavailable" data-localization-ignore="true">
          {currentComparison.unavailableReason ?? t('changeSets.comparison.unavailable')}
        </p>
      ) : null}
      {currentComparison?.state === 'available' ? (
        currentComparison.entries.length > 0 ? (
          <>
            <div className="change-set-result-controls">
              <label>
                <span>{t('analysisPresentation.controls.filter')}</span>
                <input
                  onChange={(event) => setResultFilter(event.currentTarget.value)}
                  type="search"
                  value={resultFilter}
                />
              </label>
              <label>
                <span>{t('analysisPresentation.controls.resultType')}</span>
                <select
                  className="km-select-control"
                  onChange={(event) => setKindFilter(event.currentTarget.value)}
                  value={kindFilter}
                >
                  <option value="all">{t('analysisPresentation.controls.allResults')}</option>
                  {kinds.map((kind) => (
                    <option key={kind} value={kind}>{t(`changeSets.comparison.kind.${kind}`)}</option>
                  ))}
                </select>
              </label>
              <label>
                <span>{t('analysisPresentation.controls.sort')}</span>
                <select
                  className="km-select-control"
                  onChange={(event) => setResultOrder(event.currentTarget.value as typeof resultOrder)}
                  value={resultOrder}
                >
                  <option value="target">{t('changeSets.comparison.target')}</option>
                  <option value="kind">{t('analysisPresentation.controls.resultType')}</option>
                  <option value="owner">{t('changeSets.comparison.owner')}</option>
                </select>
              </label>
            </div>
            <div className="change-set-comparison-selection">
              <span role="status">{t('analysisPresentation.controls.selectedCount', {
                selected: selectedEntryKeys.size,
                total: currentComparison.entries.length
              })}</span>
              <button
                className="secondary-button compact-button"
                disabled={visibleKeys.every((key) => selectedEntryKeys.has(key))}
                onClick={() => setSelectedEntryKeys((current) => new Set([
                  ...current,
                  ...visibleKeys
                ]))}
                type="button"
              >
                {t('analysisPresentation.controls.selectVisible')}
              </button>
              <button
                className="secondary-button compact-button"
                disabled={selectedEntryKeys.size === 0}
                onClick={() => setSelectedEntryKeys(new Set())}
                type="button"
              >
                {t('analysisPresentation.controls.clearSelection')}
              </button>
              <label>
                <input
                  checked={showSelectedOnly}
                  className="km-choice-control"
                  onChange={(event) => setShowSelectedOnly(event.currentTarget.checked)}
                  type="checkbox"
                />
                <span>{t('analysisPresentation.controls.showSelectedOnly')}</span>
              </label>
            </div>
            <div
              aria-label={t('changeSets.comparison.title')}
              className="change-set-comparison-table-wrap"
              role="region"
              tabIndex={0}
            >
              <table className="change-set-comparison-table">
                <thead>
                  <tr>
                    <th scope="col">
                      <span className="km-workbench-visually-hidden">
                        {t('analysisPresentation.controls.selection')}
                      </span>
                    </th>
                    <th scope="col">{t('changeSets.comparison.target')}</th>
                    <th scope="col">{t('changeSets.comparison.selected')}</th>
                    <th scope="col">{t('changeSets.comparison.effective')}</th>
                    <th scope="col">{t('changeSets.comparison.owner')}</th>
                    <th scope="col">{t('changeSets.comparison.result')}</th>
                  </tr>
                </thead>
                <tbody>
                  {visibleEntries.map(({ entry, key }) => {
                    const exactTarget = comparisonIdentityLabels.targetByOperationId.get(
                      entry.operationId
                    ) ?? entry.targetLabel;
                    const exactOwner = comparisonIdentityLabels.ownerByOperationId.get(
                      entry.operationId
                    ) ?? entry.ownerLabel;
                    return (
                      <tr className={selectedEntryKeys.has(key) ? 'is-selected' : 'is-unselected'} key={key}>
                        <td>
                          <input
                            aria-label={[
                              `${t('analysisPresentation.controls.selection')}: ${exactTarget}`,
                              exactOwner,
                              t(`changeSets.comparison.kind.${entry.kind}`)
                            ].filter((value): value is string => Boolean(value)).join(' / ')}
                            checked={selectedEntryKeys.has(key)}
                            className="km-choice-control"
                            onChange={(event) => setSelectedEntryKeys((current) => {
                              const next = new Set(current);
                              if (event.currentTarget.checked) next.add(key);
                              else next.delete(key);
                              return next;
                            })}
                            type="checkbox"
                          />
                        </td>
                        <th data-localization-ignore="true" scope="row">
                          {exactTarget}
                        </th>
                        <td data-localization-ignore="true">{entry.leftValue ?? t('changeSets.comparison.none')}</td>
                        <td data-localization-ignore="true">{entry.rightValue ?? t('changeSets.comparison.none')}</td>
                        <td data-localization-ignore="true">
                          {exactOwner ?? t('changeSets.comparison.none')}
                        </td>
                        <td>{t(`changeSets.comparison.kind.${entry.kind}`)}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
              {visibleEntries.length === 0 ? (
                <p className="change-sets-empty">{t('analysisPresentation.controls.noMatches')}</p>
              ) : null}
              {currentComparison.isTruncated ? (
                <p className="change-set-bounded-note">{t('changeSets.comparison.truncated')}</p>
              ) : null}
            </div>
          </>
        ) : (
          <p className="change-sets-empty">{t('changeSets.comparison.empty')}</p>
        )
      ) : null}
    </section>
  );
}

function changeSetComparisonEntryKey(
  entry: ChangeSetComparisonViewModel['entries'][number],
  index: number
) {
  return JSON.stringify([
    entry.operationId,
    entry.ownerId,
    entry.targetLabel,
    entry.kind,
    entry.ownerLabel,
    entry.leftValue,
    entry.rightValue,
    index
  ]);
}

function createComparisonIdentityLabels(entries: ChangeSetComparisonViewModel['entries']) {
  const ownerByOperationId = new Map<string, string>();
  const targetByOperationId = new Map<string, string>();
  const ownerGroups = new Map<string, typeof entries[number][]>();
  const targetGroups = new Map<string, typeof entries[number][]>();

  for (const entry of entries) {
    const targetKey = entry.targetLabel.trim().toLocaleLowerCase();
    const targetGroup = targetGroups.get(targetKey);
    if (targetGroup) targetGroup.push(entry);
    else targetGroups.set(targetKey, [entry]);
    if (entry.ownerLabel) {
      const ownerKey = entry.ownerLabel.trim().toLocaleLowerCase();
      const ownerGroup = ownerGroups.get(ownerKey);
      if (ownerGroup) ownerGroup.push(entry);
      else ownerGroups.set(ownerKey, [entry]);
    }
  }

  for (const group of targetGroups.values()) {
    const ids = [...new Set(group.map((entry) => entry.operationId))];
    const shortIds = createCollisionAwareShortIdMap(ids);
    for (const entry of group) {
      targetByOperationId.set(
        entry.operationId,
        ids.length < 2
          ? entry.targetLabel
          : `${entry.targetLabel} [${shortIds.get(entry.operationId) ?? entry.operationId}]`
      );
    }
  }

  for (const group of ownerGroups.values()) {
    const ids = [...new Set(group.map((entry) => entry.ownerId ?? entry.operationId))];
    const shortIds = createCollisionAwareShortIdMap(ids);
    for (const entry of group) {
      if (!entry.ownerLabel) continue;
      const exactId = entry.ownerId ?? entry.operationId;
      ownerByOperationId.set(
        entry.operationId,
        ids.length < 2
          ? entry.ownerLabel
          : `${entry.ownerLabel} [${shortIds.get(exactId) ?? exactId}]`
      );
    }
  }

  return { ownerByOperationId, targetByOperationId };
}

function createCollisionAwareShortIdMap(ids: readonly string[]) {
  const uniqueIds = [...new Set(ids)];
  const result = new Map<string, string>();
  const pending = new Set(uniqueIds);
  const maximumLength = Math.max(0, ...uniqueIds.map((id) => id.length));

  for (let length = Math.min(12, maximumLength); pending.size > 0; length += 1) {
    const buckets = new Map<string, string[]>();
    for (const id of pending) {
      const candidate = id.slice(0, length);
      const bucket = buckets.get(candidate);
      if (bucket) bucket.push(id);
      else buckets.set(candidate, [id]);
    }
    for (const [candidate, bucket] of buckets) {
      if (bucket.length === 1 || length >= maximumLength) {
        for (const id of bucket) {
          result.set(id, candidate || id);
          pending.delete(id);
        }
      }
    }
  }

  return result;
}

function formatCollisionAwareName<T extends { id: string; name: string }>(
  item: T,
  peers: readonly T[]
) {
  const normalizedName = item.name.trim().toLocaleLowerCase();
  const sameNameIds = peers
    .filter((peer) => peer.name.trim().toLocaleLowerCase() === normalizedName)
    .map((peer) => peer.id);
  if (sameNameIds.length < 2) return item.name;
  return `${item.name} [${shortCollisionAwareId(item.id, sameNameIds)}]`;
}

function shortCollisionAwareId(id: string, peerIds: readonly string[]) {
  const minimumLength = Math.min(12, id.length);
  for (let length = minimumLength; length <= id.length; length += 1) {
    const candidate = id.slice(0, length);
    if (peerIds.every((peerId) => peerId === id || !peerId.startsWith(candidate))) {
      return candidate;
    }
  }
  return id;
}

function parseTags(value: string) {
  const seen = new Set<string>();
  const tags = value
    .split(',')
    .map((tag) => tag.trim())
    .filter((tag) => {
      const key = tag.toUpperCase();
      if (!tag || seen.has(key)) return false;
      seen.add(key);
      return true;
    });
  return tags.length <= 32 && tags.every((tag) => tag.length <= 64)
    ? tags
    : null;
}

function formatTimestamp(value: string, formatLocale: string) {
  const timestamp = Date.parse(value);
  if (!Number.isFinite(timestamp)) return value;
  return new Intl.DateTimeFormat(formatLocale, {
    dateStyle: 'medium',
    timeStyle: 'short'
  }).format(timestamp);
}
