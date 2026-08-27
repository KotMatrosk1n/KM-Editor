/* SPDX-License-Identifier: GPL-3.0-only */

import { Layers, PanelLeftClose, PanelLeftOpen } from 'lucide-react';
import type { RefObject } from 'react';
import kmLogoUrl from '../assets/km-logo.png';
import type { ProjectGame } from '../bridge/contracts';
import { useLocalization } from '../localization';
import {
  canAccessWorkflowSectionForHealth,
  isWorkflowNavigationVisibleForGame,
  workflowNavigationGroups,
  type WorkflowNavigationGroup
} from '../workflowGameSupport';
import {
  getWorkbenchCapabilitiesByNavigationKind,
  getWorkbenchCapabilityRegistration,
  getWorkbenchSectionLabelKey
} from '../workbench/capabilityRegistry';
import type { WorkbenchSection } from '../workbenchStore';

const primaryNavigationSections = getWorkbenchCapabilitiesByNavigationKind('primary');
const utilityNavigationSections = getWorkbenchCapabilitiesByNavigationKind('utility');

export type AppSidebarProps = {
  activeSection: WorkbenchSection;
  appVersion: string;
  availableWorkflowSectionIds: ReadonlySet<WorkbenchSection>;
  canShowEditableWorkflowNavigation: boolean;
  canShowGameplaySettingsNavigation: boolean;
  canShowWorkflowNavigation: boolean;
  expandedWorkflowGroups: ReadonlySet<WorkflowNavigationGroup['id']>;
  hasAvailableUpdate: boolean;
  hasCriticalWriteOperation: boolean;
  isEditSessionOperationBusy: boolean;
  isSidebarCompact: boolean;
  onNavigate: (section: WorkbenchSection) => void;
  onToggle: () => void;
  onToggleWorkflowGroup: (groupId: WorkflowNavigationGroup['id']) => void;
  pendingEditCount: number;
  scrollRef: RefObject<HTMLDivElement | null>;
  selectedGame: ProjectGame;
  suppressedActiveWorkflowGroup: WorkflowNavigationGroup['id'] | null;
  toggleRef: RefObject<HTMLButtonElement | null>;
};

export function AppSidebar({
  activeSection,
  appVersion,
  availableWorkflowSectionIds,
  canShowEditableWorkflowNavigation,
  canShowGameplaySettingsNavigation,
  canShowWorkflowNavigation,
  expandedWorkflowGroups,
  hasAvailableUpdate,
  hasCriticalWriteOperation,
  isEditSessionOperationBusy,
  isSidebarCompact,
  onNavigate,
  onToggle,
  onToggleWorkflowGroup,
  pendingEditCount,
  scrollRef,
  selectedGame,
  suppressedActiveWorkflowGroup,
  toggleRef
}: AppSidebarProps) {
  const { t, translateLiteral } = useLocalization();
  const toggleLabel = translateLiteral(
    isSidebarCompact ? 'Expand sidebar' : 'Collapse sidebar'
  );
  const navigationDisabled = isEditSessionOperationBusy || hasCriticalWriteOperation;

  return (
    <aside
      aria-label="Application navigation"
      className={`sidebar${isSidebarCompact ? ' sidebar-compact' : ''}`}
      id="application-sidebar"
    >
      <div className="brand">
        <img alt="" aria-hidden="true" className="brand-logo" src={kmLogoUrl} />
        <span className="brand-copy">
          <span className="brand-name">
            <span>KM Editor</span>
            <span className="brand-version">v{appVersion}</span>
          </span>
          <span className="brand-credit">Made by Matroskin</span>
        </span>
        <button
          aria-controls="application-sidebar"
          aria-expanded={!isSidebarCompact}
          aria-label={toggleLabel}
          aria-pressed={isSidebarCompact}
          className="sidebar-toggle"
          onClick={onToggle}
          ref={toggleRef}
          title={toggleLabel}
          type="button"
        >
          {isSidebarCompact ? (
            <PanelLeftOpen aria-hidden="true" size={18} />
          ) : (
            <PanelLeftClose aria-hidden="true" size={18} />
          )}
        </button>
      </div>

      <nav aria-label="Workspace" className="section-nav">
        <div className="sidebar-navigation-scroll" ref={scrollRef}>
          {primaryNavigationSections.map((section) => {
            const Icon = section.icon;
            const isActive = activeSection === section.id;
            const sectionLabel = section.id === 'workbench'
              ? t(getWorkbenchSectionLabelKey(section.id))
              : translateLiteral(section.label);

            return (
              <button
                aria-current={isActive ? 'page' : undefined}
                aria-label={sectionLabel}
                className="nav-button"
                disabled={navigationDisabled}
                key={section.id}
                onClick={() => onNavigate(section.id)}
                title={isSidebarCompact ? sectionLabel : undefined}
                type="button"
              >
                <Icon aria-hidden="true" size={18} />
                <span>{sectionLabel}</span>
              </button>
            );
          })}

          {canShowWorkflowNavigation || canShowGameplaySettingsNavigation
            ? workflowNavigationGroups.map((group) => {
                const visibleSectionIds = group.sectionIds.filter(
                  (sectionId) =>
                    (sectionId === 'gameplaySettings'
                      ? canShowGameplaySettingsNavigation
                      : canShowWorkflowNavigation &&
                        canAccessWorkflowSectionForHealth(
                          sectionId,
                          canShowWorkflowNavigation,
                          canShowEditableWorkflowNavigation
                        )) &&
                    isWorkflowNavigationVisibleForGame(
                      sectionId,
                      selectedGame,
                      availableWorkflowSectionIds
                    )
                );
                if (visibleSectionIds.length === 0) {
                  return null;
                }
                const groupLabel = group.labelKey
                  ? t(group.labelKey)
                  : translateLiteral(group.label);

                const hasActiveSection = visibleSectionIds.includes(activeSection);
                const isExpanded =
                  expandedWorkflowGroups.has(group.id) ||
                  (hasActiveSection && suppressedActiveWorkflowGroup !== group.id);

                return (
                  <div
                    className={`nav-workflow-group${hasActiveSection ? ' nav-workflow-group-active' : ''}`}
                    key={group.id}
                  >
                    <button
                      aria-expanded={isExpanded}
                      aria-label={groupLabel}
                      className="nav-group-button"
                      onClick={() => onToggleWorkflowGroup(group.id)}
                      title={isSidebarCompact ? groupLabel : undefined}
                      type="button"
                    >
                      <Layers aria-hidden="true" size={16} />
                      <span>{groupLabel}</span>
                    </button>
                    {isExpanded ? (
                      <div className="nav-group-items">
                        {visibleSectionIds.map((sectionId) => {
                          const section = getWorkbenchCapabilityRegistration(sectionId);
                          const Icon = section.icon;
                          const isActive = activeSection === section.id;
                          const sectionLabel = section.id === 'gameplaySettings'
                            ? t(getWorkbenchSectionLabelKey(section.id))
                            : translateLiteral(section.label);

                          return (
                            <button
                              aria-current={isActive ? 'page' : undefined}
                              aria-label={sectionLabel}
                              className="nav-button nav-child-button"
                              disabled={navigationDisabled}
                              key={section.id}
                              onClick={() => onNavigate(section.id)}
                              title={
                                isSidebarCompact
                                  ? sectionLabel
                                  : undefined
                              }
                              type="button"
                            >
                              <Icon aria-hidden="true" size={16} />
                              <span>{sectionLabel}</span>
                            </button>
                          );
                        })}
                      </div>
                    ) : null}
                  </div>
                );
              })
            : null}
        </div>

        <div className="sidebar-utility-nav">
          {utilityNavigationSections.map((section) => {
            const Icon = section.icon;
            const isActive = activeSection === section.id;
            const sectionHasAvailableUpdate = section.id === 'settings' && hasAvailableUpdate;
            const navigationLabel = sectionHasAvailableUpdate
              ? `${translateLiteral(section.label)}: ${translateLiteral('Update Available')}`
              : translateLiteral(section.label);

            return (
              <button
                aria-current={isActive ? 'page' : undefined}
                aria-label={navigationLabel}
                className="nav-button"
                disabled={navigationDisabled}
                key={section.id}
                onClick={() => onNavigate(section.id)}
                title={isSidebarCompact ? navigationLabel : undefined}
                type="button"
              >
                <Icon aria-hidden="true" size={18} />
                <span>{section.label}</span>
                {section.id === 'changes' && pendingEditCount > 0 ? (
                  <span className="nav-count" aria-label={`${pendingEditCount} pending changes`}>
                    {pendingEditCount}
                  </span>
                ) : null}
                {sectionHasAvailableUpdate ? (
                  <span aria-hidden="true" className="nav-count">
                    !
                  </span>
                ) : null}
              </button>
            );
          })}
        </div>
      </nav>
    </aside>
  );
}
