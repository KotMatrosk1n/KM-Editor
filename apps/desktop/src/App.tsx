/* SPDX-License-Identifier: GPL-3.0-only */

import {
  Activity,
  ClipboardCheck,
  FolderOpen,
  Layers,
  ListChecks,
  Package,
  RefreshCw,
  Search,
  ShieldCheck,
  type LucideIcon
} from 'lucide-react';
import { useState } from 'react';
import {
  type ApiDiagnostic,
  type ItemsWorkflow,
  type ItemRecord,
  type ProjectHealth,
  type ProjectPathRole,
  type ProjectPathValidation,
  type WorkflowSummary
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
    id: 'items',
    label: 'Items',
    icon: Package
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
  const itemSearchText = useWorkbenchStore((state) => state.itemSearchText);
  const itemsWorkflow = useWorkbenchStore((state) => state.itemsWorkflow);
  const openProject = useWorkbenchStore((state) => state.openProject);
  const projectStatus = useWorkbenchStore((state) => state.projectStatus);
  const selectedItemId = useWorkbenchStore((state) => state.selectedItemId);
  const workflows = useWorkbenchStore((state) => state.workflows);
  const setActiveSection = useWorkbenchStore((state) => state.setActiveSection);
  const setDraftPath = useWorkbenchStore((state) => state.setDraftPath);
  const setItemSearchText = useWorkbenchStore((state) => state.setItemSearchText);
  const setItemsWorkflow = useWorkbenchStore((state) => state.setItemsWorkflow);
  const setOpenProject = useWorkbenchStore((state) => state.setOpenProject);
  const setProjectHealth = useWorkbenchStore((state) => state.setProjectHealth);
  const setProjectStatus = useWorkbenchStore((state) => state.setProjectStatus);
  const setSelectedItemId = useWorkbenchStore((state) => state.setSelectedItemId);
  const setWorkflows = useWorkbenchStore((state) => state.setWorkflows);
  const health = openProject?.health ?? null;
  const activeSectionLabel = sections.find((section) => section.id === activeSection)?.label;
  const isBusy = projectStatus === 'opening' || projectStatus === 'validating';
  const [bridgeDiagnostics, setBridgeDiagnostics] = useState<ApiDiagnostic[]>([]);
  const [isItemsLoading, setIsItemsLoading] = useState(false);

  const handleValidateProject = async () => {
    setProjectStatus('validating');
    setBridgeDiagnostics([]);

    try {
      const paths = toProjectPaths(draftPaths);
      const response = await bridge.validateProject({ paths });
      setProjectHealth(response.health);
      await refreshWorkflows(paths, response.health.canOpenReadOnlyWorkflows);
    } catch (error) {
      setProjectStatus('idle');
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    }
  };

  const handleOpenProject = async () => {
    setProjectStatus('opening');
    setBridgeDiagnostics([]);

    try {
      const paths = toProjectPaths(draftPaths);
      const response = await bridge.openProject({ paths });
      setOpenProject({
        fileGraph: response.fileGraph,
        health: response.health,
        projectId: response.projectId
      });
      await refreshWorkflows(paths, response.health.canOpenReadOnlyWorkflows);
      setActiveSection('health');
    } catch (error) {
      setProjectStatus('idle');
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    }
  };

  const handleOpenItemsWorkflow = async () => {
    setIsItemsLoading(true);
    setBridgeDiagnostics([]);

    try {
      const response = await bridge.loadItemsWorkflow({ paths: toProjectPaths(draftPaths) });
      setItemsWorkflow(response.workflow);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsItemsLoading(false);
    }
  };

  const refreshWorkflows = async (
    paths: ReturnType<typeof toProjectPaths>,
    canOpenReadOnlyWorkflows: boolean
  ) => {
    if (!canOpenReadOnlyWorkflows) {
      setWorkflows([]);
      return;
    }

    const response = await bridge.listWorkflows({ paths });
    setWorkflows(response.workflows);
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
          {activeSection === 'workflows' ? (
            <WorkflowsSection
              health={health}
              isItemsLoading={isItemsLoading}
              onOpenItemsWorkflow={handleOpenItemsWorkflow}
              workflows={workflows}
            />
          ) : null}
          {activeSection === 'items' ? (
            <ItemsSection
              onSearchChange={setItemSearchText}
              onSelectItem={setSelectedItemId}
              searchText={itemSearchText}
              selectedItemId={selectedItemId}
              workflow={itemsWorkflow}
            />
          ) : null}
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

function WorkflowsSection({
  health,
  isItemsLoading,
  onOpenItemsWorkflow,
  workflows
}: {
  health: ProjectHealth | null;
  isItemsLoading: boolean;
  onOpenItemsWorkflow: () => void;
  workflows: WorkflowSummary[];
}) {
  const itemsWorkflow = workflows.find((workflow) => workflow.id === 'items');
  const itemsState = getItemsWorkflowState(health, itemsWorkflow);
  const canOpenItems = itemsState.availability !== 'disabled';

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
            <p>{itemsWorkflow?.description ?? 'Item records, names, and source provenance.'}</p>
          </div>
          <div className="workflow-actions">
            <span className={`status-pill ${itemsState.statusClass}`}>{itemsState.label}</span>
            <button
              className="secondary-button compact-button"
              disabled={!canOpenItems || isItemsLoading}
              onClick={onOpenItemsWorkflow}
              type="button"
            >
              <Package aria-hidden="true" size={16} />
              <span>{isItemsLoading ? 'Loading' : 'Open Items'}</span>
            </button>
          </div>
        </article>
      </div>
    </section>
  );
}

function ItemsSection({
  onSearchChange,
  onSelectItem,
  searchText,
  selectedItemId,
  workflow
}: {
  onSearchChange: (searchText: string) => void;
  onSelectItem: (itemId: number | null) => void;
  searchText: string;
  selectedItemId: number | null;
  workflow: ItemsWorkflow | null;
}) {
  const filteredItems = filterItems(workflow?.items ?? [], searchText);
  const selectedItem =
    workflow?.items.find((item) => item.itemId === selectedItemId) ?? filteredItems[0] ?? null;

  return (
    <>
      <section aria-labelledby="items-heading" className="panel wide-panel">
        <div className="panel-heading">
          <Package aria-hidden="true" size={18} />
          <h2 id="items-heading">Items</h2>
        </div>

        <div className="items-toolbar">
          <label className="search-box items-search">
            <Search aria-hidden="true" size={18} />
            <input
              aria-label="Search items"
              disabled={!workflow}
              onChange={(event) => onSearchChange(event.target.value)}
              placeholder="Search items"
              type="search"
              value={searchText}
            />
          </label>
          <Metric
            label="Loaded records"
            value={workflow ? workflow.stats.totalItemCount.toString() : '0'}
          />
        </div>

        {workflow ? (
          <div className="items-layout">
            <div className="items-table" role="table" aria-label="Items">
              <div className="items-row items-row-heading" role="row">
                <span role="columnheader">ID</span>
                <span role="columnheader">Name</span>
                <span role="columnheader">Category</span>
                <span role="columnheader">Buy</span>
                <span role="columnheader">Source</span>
              </div>
              {filteredItems.map((item) => (
                <button
                  className={`items-row ${selectedItem?.itemId === item.itemId ? 'items-row-selected' : ''}`}
                  key={item.itemId}
                  onClick={() => onSelectItem(item.itemId)}
                  role="row"
                  type="button"
                >
                  <span role="cell">{item.itemId}</span>
                  <span role="cell">{item.name}</span>
                  <span role="cell">{item.category}</span>
                  <span role="cell">{item.buyPrice}</span>
                  <span role="cell">{formatSourceLayer(item.provenance.sourceLayer)}</span>
                </button>
              ))}
            </div>

            <SelectedItemPanel item={selectedItem} />
          </div>
        ) : (
          <p className="empty-copy">Open Items from Workflows to load backend item data.</p>
        )}
      </section>

      <DiagnosticsSection diagnostics={workflow?.diagnostics ?? []} />
    </>
  );
}

function SelectedItemPanel({ item }: { item: ItemRecord | null }) {
  return (
    <aside aria-label="Selected item provenance" className="item-inspector">
      <div className="panel-heading">
        <ShieldCheck aria-hidden="true" size={18} />
        <h3>Provenance</h3>
      </div>

      {item ? (
        <dl className="item-provenance-list">
          <div>
            <dt>Name</dt>
            <dd>{item.name}</dd>
          </div>
          <div>
            <dt>Source file</dt>
            <dd>{item.provenance.sourceFile}</dd>
          </div>
          <div>
            <dt>Layer</dt>
            <dd>{formatSourceLayer(item.provenance.sourceLayer)}</dd>
          </div>
          <div>
            <dt>File state</dt>
            <dd>{formatFileState(item.provenance.fileState)}</dd>
          </div>
          <div>
            <dt>Sell price</dt>
            <dd>{item.sellPrice}</dd>
          </div>
        </dl>
      ) : (
        <p className="empty-copy">No item selected.</p>
      )}
    </aside>
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

function filterItems(items: ItemRecord[], searchText: string) {
  const normalizedSearch = searchText.trim().toLocaleLowerCase();

  if (normalizedSearch.length === 0) {
    return items;
  }

  return items.filter((item) =>
    [item.itemId.toString(), item.name, item.category].some((value) =>
      value.toLocaleLowerCase().includes(normalizedSearch)
    )
  );
}

function getItemsWorkflowState(health: ProjectHealth | null, workflow: WorkflowSummary | undefined) {
  if (!health?.canOpenReadOnlyWorkflows) {
    return {
      availability: 'disabled',
      label: 'Disabled',
      statusClass: 'status-blocked'
    } as const;
  }

  if (workflow) {
    return {
      availability: workflow.availability,
      label: workflowAvailabilityLabels[workflow.availability],
      statusClass: workflowAvailabilityClassNames[workflow.availability]
    } as const;
  }

  return {
    availability: 'readOnly',
    label: 'Read-only',
    statusClass: 'status-warning'
  } as const;
}

const workflowAvailabilityLabels = {
  available: 'Available',
  disabled: 'Disabled',
  readOnly: 'Read-only'
} as const;

const workflowAvailabilityClassNames = {
  available: 'status-ready',
  disabled: 'status-blocked',
  readOnly: 'status-warning'
} as const;

function formatSourceLayer(layer: ItemRecord['provenance']['sourceLayer']) {
  return layer === 'layered' ? 'LayeredFS' : 'Base';
}

function formatFileState(state: ItemRecord['provenance']['fileState']) {
  return {
    baseOnly: 'Base only',
    layeredOnly: 'Layered only',
    layeredOverride: 'Layered override'
  }[state];
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
