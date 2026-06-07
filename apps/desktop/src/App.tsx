/* SPDX-License-Identifier: GPL-3.0-only */

import {
  Activity,
  ClipboardCheck,
  FolderOpen,
  Layers,
  ListChecks,
  RefreshCw,
  Search,
  ShieldCheck,
  type LucideIcon
} from 'lucide-react';
import { useState } from 'react';
import {
  type ApiDiagnostic,
  type ProjectHealth,
  type ProjectPathRole,
  type ProjectPathValidation
} from './bridge/contracts';
import {
  ProjectBridgeError,
  projectBridge as defaultProjectBridge,
  type ProjectBridge
} from './bridge/projectBridge';
import {
  type ProjectPathDraft,
  type WorkbenchSection,
  useWorkbenchStore
} from './workbenchStore';

const sections: Array<{
  id: WorkbenchSection;
  label: string;
  icon: LucideIcon;
}> = [
  {
    id: 'health',
    label: 'Project Health',
    icon: Activity
  },
  {
    id: 'workflows',
    label: 'Workflows',
    icon: ListChecks
  },
  {
    id: 'changes',
    label: 'Changes',
    icon: ClipboardCheck
  }
];

const pathFields: Array<{
  field: keyof ProjectPathDraft;
  label: string;
  role: ProjectPathRole;
}> = [
  {
    field: 'baseRomFsPath',
    label: 'Base RomFS',
    role: 'baseRomFs'
  },
  {
    field: 'baseExeFsPath',
    label: 'Base ExeFS',
    role: 'baseExeFs'
  },
  {
    field: 'outputRootPath',
    label: 'Output Root',
    role: 'outputRoot'
  }
];

const healthLabels = {
  blocked: 'Blocked',
  editableReady: 'Editable ready',
  needsPaths: 'Needs paths',
  readOnlyReady: 'Read-only ready'
} as const satisfies Record<ProjectHealth['state'], string>;

const pathStatusLabels = {
  missing: 'Missing',
  notSet: 'Not set',
  unsafe: 'Unsafe',
  valid: 'Valid',
  wrongKind: 'Wrong kind'
} as const;

export function App({ bridge = defaultProjectBridge }: { bridge?: ProjectBridge } = {}) {
  const activeSection = useWorkbenchStore((state) => state.activeSection);
  const draftPaths = useWorkbenchStore((state) => state.draftPaths);
  const openProject = useWorkbenchStore((state) => state.openProject);
  const projectStatus = useWorkbenchStore((state) => state.projectStatus);
  const setActiveSection = useWorkbenchStore((state) => state.setActiveSection);
  const setDraftPath = useWorkbenchStore((state) => state.setDraftPath);
  const setOpenProject = useWorkbenchStore((state) => state.setOpenProject);
  const setProjectHealth = useWorkbenchStore((state) => state.setProjectHealth);
  const setProjectStatus = useWorkbenchStore((state) => state.setProjectStatus);
  const health = openProject?.health ?? null;
  const activeSectionLabel = sections.find((section) => section.id === activeSection)?.label;
  const isBusy = projectStatus === 'opening' || projectStatus === 'validating';
  const [bridgeDiagnostics, setBridgeDiagnostics] = useState<ApiDiagnostic[]>([]);

  const handleValidateProject = async () => {
    setProjectStatus('validating');
    setBridgeDiagnostics([]);

    try {
      const response = await bridge.validateProject({ paths: toProjectPaths(draftPaths) });
      setProjectHealth(response.health);
    } catch (error) {
      setProjectStatus('idle');
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    }
  };

  const handleOpenProject = async () => {
    setProjectStatus('opening');
    setBridgeDiagnostics([]);

    try {
      const response = await bridge.openProject({ paths: toProjectPaths(draftPaths) });
      setOpenProject({
        fileGraph: response.fileGraph,
        health: response.health,
        projectId: response.projectId
      });
      setActiveSection('health');
    } catch (error) {
      setProjectStatus('idle');
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    }
  };

  return (
    <main className="app-shell">
      <aside className="sidebar">
        <div className="brand">
          <Layers aria-hidden="true" size={24} />
          <span>KM Editor</span>
        </div>

        <nav aria-label="Workspace" className="section-nav">
          {sections.map((section) => {
            const Icon = section.icon;
            const isActive = activeSection === section.id;

            return (
              <button
                aria-current={isActive ? 'page' : undefined}
                aria-label={section.label}
                className="nav-button"
                key={section.id}
                onClick={() => setActiveSection(section.id)}
                type="button"
              >
                <Icon aria-hidden="true" size={18} />
                <span>{section.label}</span>
              </button>
            );
          })}
        </nav>
      </aside>

      <section className="workspace">
        <header className="toolbar">
          <div className="title-block">
            <p className="project-state">{getProjectStateLabel(health, projectStatus)}</p>
            <h1>{activeSectionLabel}</h1>
          </div>

          <label className="search-box">
            <Search aria-hidden="true" size={18} />
            <input disabled placeholder="Search project" type="search" />
          </label>

          <button
            className="primary-button"
            disabled={isBusy}
            onClick={handleOpenProject}
            type="button"
          >
            <FolderOpen aria-hidden="true" size={18} />
            <span>{projectStatus === 'opening' ? 'Opening' : 'Open Project'}</span>
          </button>
        </header>

        <div className="workspace-content">
          {activeSection === 'health' ? (
            <HealthSection
              draftPaths={draftPaths}
              health={health}
              bridgeDiagnostics={bridgeDiagnostics}
              isBusy={isBusy}
              onOpenProject={handleOpenProject}
              onSetDraftPath={setDraftPath}
              onValidateProject={handleValidateProject}
              projectStatus={projectStatus}
            />
          ) : null}
          {activeSection === 'workflows' ? <WorkflowsSection health={health} /> : null}
          {activeSection === 'changes' ? <ChangesSection /> : null}
        </div>
      </section>
    </main>
  );
}

function HealthSection({
  bridgeDiagnostics,
  draftPaths,
  health,
  isBusy,
  onOpenProject,
  onSetDraftPath,
  onValidateProject,
  projectStatus
}: {
  bridgeDiagnostics: ApiDiagnostic[];
  draftPaths: ProjectPathDraft;
  health: ProjectHealth | null;
  isBusy: boolean;
  onOpenProject: () => void;
  onSetDraftPath: (field: keyof ProjectPathDraft, value: string) => void;
  onValidateProject: () => void;
  projectStatus: 'idle' | 'validating' | 'opening' | 'open';
}) {
  return (
    <>
      <section aria-labelledby="project-gate-heading" className="panel project-gate">
        <div className="panel-heading">
          <FolderOpen aria-hidden="true" size={18} />
          <h2 id="project-gate-heading">Project Paths</h2>
        </div>

        <div className="path-form">
          {pathFields.map((pathField) => {
            const pathValidation = health?.paths.find((path) => path.role === pathField.role);

            return (
              <label className="path-field" key={pathField.field}>
                <span>{pathField.label}</span>
                <input
                  aria-label={pathField.label}
                  aria-describedby={`${pathField.field}-status`}
                  onChange={(event) => onSetDraftPath(pathField.field, event.target.value)}
                  placeholder="Not set"
                  value={draftPaths[pathField.field]}
                />
                <small
                  className={getPathStatusClassName(pathValidation)}
                  id={`${pathField.field}-status`}
                >
                  {pathValidation ? pathStatusLabels[pathValidation.status] : 'Not checked'}
                </small>
              </label>
            );
          })}
        </div>

        <div className="action-row">
          <button
            className="secondary-button"
            disabled={isBusy}
            onClick={onValidateProject}
            type="button"
          >
            <RefreshCw aria-hidden="true" size={18} />
            <span>{projectStatus === 'validating' ? 'Validating' : 'Validate Paths'}</span>
          </button>
          <button
            className="primary-button"
            disabled={isBusy}
            onClick={onOpenProject}
            type="button"
          >
            <FolderOpen aria-hidden="true" size={18} />
            <span>{projectStatus === 'opening' ? 'Opening' : 'Open Project'}</span>
          </button>
        </div>
      </section>

      <section aria-labelledby="health-heading" className="panel">
        <div className="panel-heading">
          <ShieldCheck aria-hidden="true" size={18} />
          <h2 id="health-heading">Health Summary</h2>
        </div>

        <div className="health-grid">
          <Metric label="State" value={health ? healthLabels[health.state] : 'No project'} />
          <Metric
            label="Read-only workflows"
            value={health?.canOpenReadOnlyWorkflows ? 'Enabled' : 'Disabled'}
          />
          <Metric
            label="Write workflows"
            value={health?.canOpenEditableWorkflows ? 'Enabled' : 'Disabled'}
          />
          <Metric label="Pending changes" value="0" />
        </div>
      </section>

      <PathStatusSection health={health} />
      <DiagnosticsSection diagnostics={[...bridgeDiagnostics, ...(health?.diagnostics ?? [])]} />
    </>
  );
}

function WorkflowsSection({ health }: { health: ProjectHealth | null }) {
  const itemsState = getItemsWorkflowState(health);

  return (
    <section aria-labelledby="workflows-heading" className="panel wide-panel">
      <div className="panel-heading">
        <ListChecks aria-hidden="true" size={18} />
        <h2 id="workflows-heading">Workflow List</h2>
      </div>

      <div className="workflow-list">
        <article className="workflow-row">
          <div>
            <h3>Items</h3>
            <p>Item records, names, provenance, and pending item edits.</p>
          </div>
          <span className={`status-pill ${itemsState.statusClass}`}>{itemsState.label}</span>
        </article>
      </div>
    </section>
  );
}

function ChangesSection() {
  return (
    <section aria-labelledby="changes-heading" className="panel wide-panel">
      <div className="panel-heading">
        <ClipboardCheck aria-hidden="true" size={18} />
        <h2 id="changes-heading">Edit Session</h2>
      </div>

      <div className="empty-state">
        <span className="metric-value">0</span>
        <span className="metric-label">Pending changes</span>
      </div>
    </section>
  );
}

function PathStatusSection({ health }: { health: ProjectHealth | null }) {
  return (
    <section aria-labelledby="paths-heading" className="panel">
      <div className="panel-heading">
        <ShieldCheck aria-hidden="true" size={18} />
        <h2 id="paths-heading">Paths</h2>
      </div>

      <dl className="path-list">
        {pathFields.map((pathField) => {
          const pathValidation = health?.paths.find((path) => path.role === pathField.role);

          return (
            <div className="path-row" key={pathField.role}>
              <dt>{pathField.label}</dt>
              <dd className={getPathStatusClassName(pathValidation)}>
                {pathValidation ? pathStatusLabels[pathValidation.status] : 'Not checked'}
              </dd>
            </div>
          );
        })}
      </dl>
    </section>
  );
}

function DiagnosticsSection({ diagnostics }: { diagnostics: ApiDiagnostic[] }) {
  return (
    <section aria-labelledby="diagnostics-heading" className="panel">
      <div className="panel-heading">
        <Activity aria-hidden="true" size={18} />
        <h2 id="diagnostics-heading">Diagnostics</h2>
      </div>

      {diagnostics.length > 0 ? (
        <ul className="diagnostic-list">
          {diagnostics.map((diagnostic) => (
            <li className={`diagnostic diagnostic-${diagnostic.severity}`} key={diagnostic.message}>
              <strong>{diagnostic.severity}</strong>
              <span>{diagnostic.message}</span>
            </li>
          ))}
        </ul>
      ) : (
        <p className="empty-copy">No diagnostics.</p>
      )}
    </section>
  );
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div className="metric">
      <span className="metric-label">{label}</span>
      <span className="metric-value metric-value-small">{value}</span>
    </div>
  );
}

function getItemsWorkflowState(health: ProjectHealth | null) {
  if (!health?.canOpenReadOnlyWorkflows) {
    return {
      label: 'Disabled',
      statusClass: 'status-blocked'
    };
  }

  if (health.canOpenEditableWorkflows) {
    return {
      label: 'Available',
      statusClass: 'status-ready'
    };
  }

  return {
    label: 'Read-only',
    statusClass: 'status-warning'
  };
}

function getPathStatusClassName(pathValidation: ProjectPathValidation | undefined) {
  if (!pathValidation) {
    return 'path-status path-status-muted';
  }

  return `path-status path-status-${pathValidation.status}`;
}

function getProjectStateLabel(
  health: ProjectHealth | null,
  projectStatus: 'idle' | 'validating' | 'opening' | 'open'
) {
  if (projectStatus === 'opening') {
    return 'Opening project';
  }

  if (projectStatus === 'validating') {
    return 'Validating paths';
  }

  return health ? healthLabels[health.state] : 'No project open';
}

function toProjectPaths(draftPaths: ProjectPathDraft) {
  return {
    baseExeFsPath: normalizeDraftPath(draftPaths.baseExeFsPath),
    baseRomFsPath: normalizeDraftPath(draftPaths.baseRomFsPath),
    outputRootPath: normalizeDraftPath(draftPaths.outputRootPath)
  };
}

function normalizeDraftPath(path: string) {
  const trimmedPath = path.trim();

  return trimmedPath.length > 0 ? trimmedPath : null;
}

function toBridgeDiagnostics(error: unknown): ApiDiagnostic[] {
  if (error instanceof ProjectBridgeError) {
    return error.apiError.diagnostics.length > 0
      ? error.apiError.diagnostics
      : [
          {
            domain: 'bridge',
            message: error.apiError.message,
            severity: 'error'
          }
        ];
  }

  return [
    {
      domain: 'bridge',
      message: error instanceof Error ? error.message : 'Project bridge request failed.',
      severity: 'error'
    }
  ];
}
