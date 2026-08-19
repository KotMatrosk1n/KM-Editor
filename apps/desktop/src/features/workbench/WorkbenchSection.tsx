/* SPDX-License-Identifier: GPL-3.0-only */

import {
  Bookmark,
  Clock3,
  Compass,
  FolderClock,
  LayoutDashboard,
  NotebookPen,
  Pin,
  Plus,
  Save,
  SlidersHorizontal,
  Trash2
} from 'lucide-react';
import { useState, type FormEvent, type ReactNode } from 'react';
import { useLocalization } from '../../localization';
import { workspaceMaximumNoteBytes } from '../../bridge/workspacePersonalStateContracts';
import type { CapabilityDiscoveryViewModel } from '../../workbench/capabilityDiscovery';
import type { WorkbenchLocation } from '../../workbench/workbenchLocation';
import type { WorkbenchSection as WorkbenchSectionId } from '../../workbench/workbenchSections';
import type {
  WorkspaceNoteViewModel,
  WorkspaceOutputProfileViewModel,
  WorkspaceRecentProjectViewModel,
  WorkspaceSavedViewViewModel,
  WorkspaceTargetViewModel
} from '../../workbench/workspaceShellViewModels';
import './workbench.css';

export type WorkbenchSectionProps = {
  bookmarks: readonly WorkspaceTargetViewModel[];
  capabilities: readonly CapabilityDiscoveryViewModel[];
  note: WorkspaceNoteViewModel | null;
  onCreateBookmark?: (label: string) => void;
  onCreateOutputProfile?: (name: string) => void;
  onDeleteBookmark?: (bookmarkId: string) => void;
  onDeleteOutputProfile?: (profileId: string) => void;
  onDeleteSavedView?: (viewId: string) => void;
  onNavigateTarget: (location: WorkbenchLocation) => void;
  onNoteChange?: (text: string) => void;
  onOpenCapability: (section: WorkbenchSectionId) => void;
  onOpenRecentProject?: (projectId: string) => void;
  onOpenSavedView?: (viewId: string) => void;
  onRemoveRecentProject?: (projectId: string) => void;
  onRemovePin?: (pinId: string) => void;
  onSaveNote?: () => void;
  onSelectOutputProfile?: (profileId: string) => void;
  outputProfiles: readonly WorkspaceOutputProfileViewModel[];
  pins: readonly WorkspaceTargetViewModel[];
  recentProjects: readonly WorkspaceRecentProjectViewModel[];
  recents: readonly WorkspaceTargetViewModel[];
  savedViews: readonly WorkspaceSavedViewViewModel[];
  workflowHome: ReactNode;
};

export function WorkbenchSection({
  bookmarks,
  capabilities,
  note,
  onCreateBookmark,
  onCreateOutputProfile,
  onDeleteBookmark,
  onDeleteOutputProfile,
  onDeleteSavedView,
  onNavigateTarget,
  onNoteChange,
  onOpenCapability,
  onOpenRecentProject,
  onOpenSavedView,
  onRemoveRecentProject,
  onRemovePin,
  onSaveNote,
  onSelectOutputProfile,
  outputProfiles,
  pins,
  recentProjects,
  recents,
  savedViews,
  workflowHome
}: WorkbenchSectionProps) {
  const { t } = useLocalization();
  return (
    <section aria-labelledby="km-workbench-heading" className="km-workbench-home wide-panel">
      <header className="km-workbench-home-heading">
        <LayoutDashboard aria-hidden="true" size={20} />
        <div>
          <h2 id="km-workbench-heading">{t('workbench.home.title')}</h2>
          <p>{t('workbench.home.description')}</p>
        </div>
      </header>

      <div className="km-workbench-home-grid">
        <WorkspaceCollection
          emptyKey="workbench.recents.empty"
          icon={<Clock3 aria-hidden="true" size={17} />}
          items={recents}
          onNavigate={onNavigateTarget}
          titleKey="workbench.recents.title"
        />
        <WorkspaceCollection
          emptyKey="workbench.pins.empty"
          icon={<Pin aria-hidden="true" size={17} />}
          items={pins}
          onNavigate={onNavigateTarget}
          onRemove={onRemovePin}
          titleKey="workbench.pins.title"
        />
        <WorkspaceCollection
          emptyKey="workbench.bookmarks.empty"
          icon={<Bookmark aria-hidden="true" size={17} />}
          items={bookmarks}
          onNavigate={onNavigateTarget}
          onRemove={onDeleteBookmark}
          titleKey="workbench.bookmarks.title"
          footer={onCreateBookmark ? (
            <WorkspaceCreateControl
              inputLabelKey="workbench.bookmarks.createLabel"
              onCreate={onCreateBookmark}
              placeholderKey="workbench.bookmarks.createPlaceholder"
              submitKey="workbench.bookmarks.create"
            />
          ) : null}
        />

        <section className="km-workbench-card km-workbench-span-two">
          <div className="km-workbench-card-heading">
            <Compass aria-hidden="true" size={17} />
            <h3>{t('workbench.capabilities.title')}</h3>
          </div>
          <p className="km-workbench-card-description">
            {t('workbench.capabilities.description')}
          </p>
          <div className="km-capability-grid">
            {capabilities.map((capability) => (
              <article className="km-capability-card" key={capability.id}>
                <div>
                  <h4>{t(capability.labelKey)}</h4>
                  <p>{t(capability.descriptionKey)}</p>
                </div>
                <div className="km-capability-actions">
                  <span className={`km-capability-status is-${capability.status}`}>
                    {t(capability.statusKey)}
                  </span>
                  {capability.reason || capability.reasonKey ? (
                    <small data-localization-ignore={capability.reason ? 'true' : undefined}>
                      {capability.reason ?? t(capability.reasonKey!)}
                    </small>
                  ) : null}
                  <button
                    className="secondary-button compact-button"
                    disabled={capability.status === 'blocked'}
                    onClick={() => onOpenCapability(capability.id)}
                    type="button"
                  >
                    {t('workbench.capabilities.open')}
                  </button>
                </div>
              </article>
            ))}
          </div>
        </section>

        <section className="km-workbench-card">
          <div className="km-workbench-card-heading">
            <SlidersHorizontal aria-hidden="true" size={17} />
            <h3>{t('workbench.savedViews.title')}</h3>
          </div>
          {savedViews.length > 0 ? (
            <ul className="km-workbench-list">
              {savedViews.map((view) => (
                <li key={view.id}>
                  <button
                    className="km-workbench-target"
                    data-localization-ignore="true"
                    onClick={() => onOpenSavedView?.(view.id)}
                    type="button"
                  >
                    <strong>{view.name}</strong>
                    {view.description ? <small>{view.description}</small> : null}
                  </button>
                  {onDeleteSavedView ? (
                    <button
                      aria-label={t('workbench.savedViews.deleteLabel', { name: view.name })}
                      className="km-workbench-remove"
                      data-localization-ignore="true"
                      onClick={() => onDeleteSavedView(view.id)}
                      type="button"
                    >
                      <Trash2 aria-hidden="true" size={14} />
                      <span className="km-workbench-visually-hidden">
                        {t('workbench.savedViews.delete')}
                      </span>
                    </button>
                  ) : null}
                </li>
              ))}
            </ul>
          ) : (
            <p className="km-workbench-empty">{t('workbench.savedViews.empty')}</p>
          )}
        </section>

        <section className="km-workbench-card">
          <div className="km-workbench-card-heading">
            <Save aria-hidden="true" size={17} />
            <h3>{t('workbench.outputProfiles.title')}</h3>
          </div>
          {outputProfiles.length > 0 ? (
            <ul className="km-workbench-list">
              {outputProfiles.map((profile) => (
                <li key={profile.id}>
                  <button
                    aria-pressed={profile.isActive}
                    className="km-workbench-target"
                    data-localization-ignore="true"
                    onClick={() => onSelectOutputProfile?.(profile.id)}
                    type="button"
                  >
                    <strong>{profile.name}</strong>
                    {profile.description ? <small>{profile.description}</small> : null}
                    {profile.isActive ? (
                      <span>{t('workbench.outputProfiles.active')}</span>
                    ) : null}
                  </button>
                  {onDeleteOutputProfile ? (
                    <button
                      aria-label={t('workbench.outputProfiles.deleteLabel', {
                        name: profile.name
                      })}
                      className="km-workbench-remove"
                      data-localization-ignore="true"
                      onClick={() => onDeleteOutputProfile(profile.id)}
                      type="button"
                    >
                      <Trash2 aria-hidden="true" size={14} />
                      <span className="km-workbench-visually-hidden">
                        {t('workbench.outputProfiles.delete')}
                      </span>
                    </button>
                  ) : null}
                </li>
              ))}
            </ul>
          ) : (
            <p className="km-workbench-empty">{t('workbench.outputProfiles.empty')}</p>
          )}
          {onCreateOutputProfile ? (
            <WorkspaceCreateControl
              inputLabelKey="workbench.outputProfiles.createLabel"
              onCreate={onCreateOutputProfile}
              placeholderKey="workbench.outputProfiles.createPlaceholder"
              submitKey="workbench.outputProfiles.create"
            />
          ) : null}
        </section>

        <section className="km-workbench-card">
          <div className="km-workbench-card-heading">
            <FolderClock aria-hidden="true" size={17} />
            <h3>{t('workbench.recentProjects.title')}</h3>
          </div>
          {recentProjects.length > 0 ? (
            <ul className="km-workbench-list">
              {recentProjects.map((project) => (
                <li key={project.id}>
                  <button
                    className="km-workbench-target"
                    data-localization-ignore="true"
                    disabled={!project.isAvailable}
                    onClick={() => onOpenRecentProject?.(project.id)}
                    title={project.unavailableReason ?? undefined}
                    type="button"
                  >
                    <strong>{project.name}</strong>
                    <small>{project.game}</small>
                    {project.unavailableReason ? (
                      <small>{project.unavailableReason}</small>
                    ) : null}
                  </button>
                  {onRemoveRecentProject ? (
                    <button
                      aria-label={t('workbench.recentProjects.removeLabel', {
                        name: project.name
                      })}
                      className="km-workbench-remove"
                      data-localization-ignore="true"
                      onClick={() => onRemoveRecentProject(project.id)}
                      type="button"
                    >
                      <Trash2 aria-hidden="true" size={14} />
                      <span className="km-workbench-visually-hidden">
                        {t('workbench.recentProjects.remove')}
                      </span>
                    </button>
                  ) : null}
                </li>
              ))}
            </ul>
          ) : (
            <p className="km-workbench-empty">{t('workbench.recentProjects.empty')}</p>
          )}
        </section>

        <section className="km-workbench-card">
          <div className="km-workbench-card-heading">
            <NotebookPen aria-hidden="true" size={17} />
            <h3>{t('workbench.notes.title')}</h3>
          </div>
          {note ? (
            <div className="km-workbench-note">
              <label>
                <span data-localization-ignore="true">{note.entityLabel}</span>
                <textarea
                  disabled={note.isBusy}
                  maxLength={workspaceMaximumNoteBytes / 4}
                  onBlur={onSaveNote}
                  onChange={(event) => onNoteChange?.(event.target.value)}
                  placeholder={t('workbench.notes.placeholder')}
                  value={note.text}
                />
              </label>
              <div className="km-workbench-note-actions">
                <small>
                  {note.statusKey
                    ? t(note.statusKey)
                    : note.updatedAtLabel ?? t('workbench.notes.notSaved')}
                </small>
                <button
                  className="secondary-button compact-button"
                  disabled={note.isBusy || !onSaveNote}
                  onClick={onSaveNote}
                  type="button"
                >
                  {t('workbench.notes.save')}
                </button>
              </div>
            </div>
          ) : (
            <p className="km-workbench-empty">{t('workbench.notes.noSelection')}</p>
          )}
        </section>
      </div>

      <section aria-label={t('workbench.workflows.title')} className="km-workbench-workflows">
        {workflowHome}
      </section>
    </section>
  );
}

function WorkspaceCollection({
  emptyKey,
  footer,
  icon,
  items,
  onNavigate,
  onRemove,
  titleKey
}: {
  emptyKey: string;
  footer?: ReactNode;
  icon: ReactNode;
  items: readonly WorkspaceTargetViewModel[];
  onNavigate: (location: WorkbenchLocation) => void;
  onRemove?: (itemId: string) => void;
  titleKey: string;
}) {
  const { t } = useLocalization();
  return (
    <section className="km-workbench-card">
      <div className="km-workbench-card-heading">
        {icon}
        <h3>{t(titleKey)}</h3>
      </div>
      {items.length > 0 ? (
        <ul className="km-workbench-list">
          {items.map((item) => (
            <li key={item.id}>
              <button
                className="km-workbench-target"
                data-localization-ignore={item.labelIsRawData ? 'true' : undefined}
                onClick={() => onNavigate(item.location)}
                type="button"
              >
                <strong>{item.label}</strong>
                {item.description ? <small>{item.description}</small> : null}
              </button>
              {onRemove ? (
                <button
                  aria-label={t('workbench.collection.remove', { label: item.label })}
                  className="km-workbench-remove"
                  onClick={() => onRemove(item.id)}
                  type="button"
                >
                  {t('workbench.collection.removeAction')}
                </button>
              ) : null}
            </li>
          ))}
        </ul>
      ) : (
        <p className="km-workbench-empty">{t(emptyKey)}</p>
      )}
      {footer}
    </section>
  );
}

function WorkspaceCreateControl({
  inputLabelKey,
  onCreate,
  placeholderKey,
  submitKey
}: {
  inputLabelKey: string;
  onCreate: (name: string) => void;
  placeholderKey: string;
  submitKey: string;
}) {
  const { t } = useLocalization();
  const [name, setName] = useState('');
  const normalizedName = name.trim();
  const handleSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!normalizedName) {
      return;
    }
    onCreate(normalizedName);
    setName('');
  };
  return (
    <form className="km-workbench-create-control" onSubmit={handleSubmit}>
      <label>
        <span className="km-workbench-visually-hidden">{t(inputLabelKey)}</span>
        <input
          maxLength={128}
          onChange={(event) => setName(event.target.value)}
          placeholder={t(placeholderKey)}
          type="text"
          value={name}
        />
      </label>
      <button
        className="secondary-button compact-button"
        disabled={!normalizedName}
        type="submit"
      >
        <Plus aria-hidden="true" size={14} />
        <span>{t(submitKey)}</span>
      </button>
    </form>
  );
}
