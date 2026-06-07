/* SPDX-License-Identifier: GPL-3.0-only */

import {
  Activity,
  CheckCircle,
  ClipboardCheck,
  FolderOpen,
  Layers,
  ListChecks,
  Package,
  Pencil,
  RefreshCw,
  Save,
  Search,
  ShieldCheck,
  type LucideIcon
} from 'lucide-react';
import { useEffect, useState } from 'react';
import {
  type ApiDiagnostic,
  type ApplyResult,
  type ChangePlan,
  type EditSession,
  type ItemEditableField,
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

const workflowDefinitions: Array<{
  id: string;
  label: string;
  description: string;
  icon: LucideIcon;
}> = [
  {
    id: 'items',
    label: 'Items',
    description: 'Item records, names, and source provenance.',
    icon: Package
  },
  {
    id: 'text',
    label: 'Text and Dialogue Map',
    description: 'Text entries, dialogue references, and source provenance.',
    icon: ListChecks
  },
  {
    id: 'trainers',
    label: 'Trainers',
    description: 'Trainer parties, classes, battle types, and source provenance.',
    icon: Activity
  },
  {
    id: 'shops',
    label: 'Shops',
    description: 'Shop inventories, prices, stock limits, and source provenance.',
    icon: ListChecks
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

const buyPriceFieldName = 'buyPrice';
const sellPriceFieldName = 'sellPrice';

export function App({ bridge = defaultProjectBridge }: { bridge?: ProjectBridge } = {}) {
  const activeSection = useWorkbenchStore((state) => state.activeSection);
  const applyResult = useWorkbenchStore((state) => state.applyResult);
  const changePlan = useWorkbenchStore((state) => state.changePlan);
  const draftPaths = useWorkbenchStore((state) => state.draftPaths);
  const editSession = useWorkbenchStore((state) => state.editSession);
  const editValidationDiagnostics = useWorkbenchStore((state) => state.editValidationDiagnostics);
  const itemSearchText = useWorkbenchStore((state) => state.itemSearchText);
  const itemsWorkflow = useWorkbenchStore((state) => state.itemsWorkflow);
  const openProject = useWorkbenchStore((state) => state.openProject);
  const projectStatus = useWorkbenchStore((state) => state.projectStatus);
  const selectedItemId = useWorkbenchStore((state) => state.selectedItemId);
  const workflows = useWorkbenchStore((state) => state.workflows);
  const setActiveSection = useWorkbenchStore((state) => state.setActiveSection);
  const setApplyResult = useWorkbenchStore((state) => state.setApplyResult);
  const setChangePlan = useWorkbenchStore((state) => state.setChangePlan);
  const setDraftPath = useWorkbenchStore((state) => state.setDraftPath);
  const setEditSession = useWorkbenchStore((state) => state.setEditSession);
  const setEditValidationDiagnostics = useWorkbenchStore(
    (state) => state.setEditValidationDiagnostics
  );
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
  const [isEditStarting, setIsEditStarting] = useState(false);
  const [isItemsLoading, setIsItemsLoading] = useState(false);
  const [isItemUpdating, setIsItemUpdating] = useState(false);
  const [isChangePlanApplying, setIsChangePlanApplying] = useState(false);
  const [isChangePlanCreating, setIsChangePlanCreating] = useState(false);
  const [isSessionValidating, setIsSessionValidating] = useState(false);
  const pendingEditCount = editSession?.pendingEdits.length ?? 0;

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

  const handleStartEditSession = async () => {
    setIsEditStarting(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);

    try {
      const response = await bridge.startEditSession({ paths: toProjectPaths(draftPaths) });
      setEditSession(response.session);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsEditStarting(false);
    }
  };

  const handleUpdateItemField = async (itemId: number, field: string, value: string) => {
    setIsItemUpdating(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);

    try {
      const response = await bridge.updateItemField({
        field,
        itemId,
        paths: toProjectPaths(draftPaths),
        session: editSession,
        value
      });
      setItemsWorkflow(response.workflow);
      setEditSession(response.session);
      setEditValidationDiagnostics(response.diagnostics);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsItemUpdating(false);
    }
  };

  const handleValidateEditSession = async () => {
    if (!editSession) {
      return;
    }

    setIsSessionValidating(true);
    setBridgeDiagnostics([]);
    setEditValidationDiagnostics([]);

    try {
      const response = await bridge.validateEditSession({
        paths: toProjectPaths(draftPaths),
        session: editSession
      });
      setEditSession(response.session);
      setEditValidationDiagnostics(response.diagnostics);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsSessionValidating(false);
    }
  };

  const handleCreateChangePlan = async () => {
    if (!editSession) {
      return;
    }

    setIsChangePlanCreating(true);
    setBridgeDiagnostics([]);
    setChangePlan(null);
    setApplyResult(null);

    try {
      const response = await bridge.createChangePlan({
        paths: toProjectPaths(draftPaths),
        session: editSession
      });
      setChangePlan(response.changePlan);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsChangePlanCreating(false);
    }
  };

  const handleApplyChangePlan = async () => {
    if (!editSession || !changePlan) {
      return;
    }

    setIsChangePlanApplying(true);
    setBridgeDiagnostics([]);
    setApplyResult(null);

    try {
      const response = await bridge.applyChangePlan({
        changePlan,
        paths: toProjectPaths(draftPaths),
        session: editSession
      });
      const hasApplyErrors = response.applyResult.diagnostics.some(
        (diagnostic) => diagnostic.severity === 'error'
      );

      if (!hasApplyErrors) {
        setEditSession(null);
        setChangePlan(null);
      }

      setApplyResult(response.applyResult);
    } catch (error) {
      setBridgeDiagnostics(toBridgeDiagnostics(error));
    } finally {
      setIsChangePlanApplying(false);
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
              pendingEditCount={pendingEditCount}
              projectStatus={projectStatus}
            />
          ) : null}
          {activeSection === 'workflows' ? (
            <WorkflowsSection
              health={health}
              isItemsLoading={isItemsLoading}
              onOpenItemsWorkflow={handleOpenItemsWorkflow}
              pendingEditCount={pendingEditCount}
              workflows={workflows}
            />
          ) : null}
          {activeSection === 'items' ? (
            <ItemsSection
              onSearchChange={setItemSearchText}
              onSelectItem={setSelectedItemId}
              onStartEditSession={handleStartEditSession}
              onUpdateItemField={handleUpdateItemField}
              searchText={itemSearchText}
              selectedItemId={selectedItemId}
              editSession={editSession}
              isEditStarting={isEditStarting}
              isItemUpdating={isItemUpdating}
              workflow={itemsWorkflow}
            />
          ) : null}
          {activeSection === 'changes' ? (
            <ChangesSection
              applyResult={applyResult}
              changePlan={changePlan}
              diagnostics={editValidationDiagnostics}
              editSession={editSession}
              isChangePlanApplying={isChangePlanApplying}
              isChangePlanCreating={isChangePlanCreating}
              isSessionValidating={isSessionValidating}
              onApplyChangePlan={handleApplyChangePlan}
              onCreateChangePlan={handleCreateChangePlan}
              onValidateEditSession={handleValidateEditSession}
            />
          ) : null}
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
  pendingEditCount,
  projectStatus
}: {
  bridgeDiagnostics: ApiDiagnostic[];
  draftPaths: ProjectPathDraft;
  health: ProjectHealth | null;
  isBusy: boolean;
  onOpenProject: () => void;
  onSetDraftPath: (field: keyof ProjectPathDraft, value: string) => void;
  onValidateProject: () => void;
  pendingEditCount: number;
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
          <Metric label="Pending changes" value={pendingEditCount.toString()} />
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
  pendingEditCount,
  workflows
}: {
  health: ProjectHealth | null;
  isItemsLoading: boolean;
  onOpenItemsWorkflow: () => void;
  pendingEditCount: number;
  workflows: WorkflowSummary[];
}) {
  return (
    <section aria-labelledby="workflows-heading" className="panel wide-panel">
      <div className="panel-heading">
        <ListChecks aria-hidden="true" size={18} />
        <h2 id="workflows-heading">Workflow List</h2>
      </div>

      <div className="workflow-list">
        {workflowDefinitions.map((definition) => {
          const workflow = workflows.find((candidate) => candidate.id === definition.id);
          const workflowState = getWorkflowState(health, workflow);
          const Icon = definition.icon;
          const isItemsWorkflow = definition.id === 'items';
          const canOpenItems = isItemsWorkflow && workflowState.availability !== 'disabled';

          return (
            <article className="workflow-row" key={definition.id}>
              <div>
                <h3>{workflow?.label ?? definition.label}</h3>
                <p>{workflow?.description ?? definition.description}</p>
                {isItemsWorkflow ? (
                  <span className="inline-metric">Pending changes: {pendingEditCount}</span>
                ) : null}
              </div>
              <div className="workflow-actions">
                <span className={`status-pill ${workflowState.statusClass}`}>
                  {workflowState.label}
                </span>
                {isItemsWorkflow ? (
                  <button
                    className="secondary-button compact-button"
                    disabled={!canOpenItems || isItemsLoading}
                    onClick={onOpenItemsWorkflow}
                    type="button"
                  >
                    <Icon aria-hidden="true" size={16} />
                    <span>{isItemsLoading ? 'Loading' : 'Open Items'}</span>
                  </button>
                ) : null}
              </div>
            </article>
          );
        })}
      </div>
    </section>
  );
}

function ItemsSection({
  editSession,
  isEditStarting,
  isItemUpdating,
  onSearchChange,
  onSelectItem,
  onStartEditSession,
  onUpdateItemField,
  searchText,
  selectedItemId,
  workflow
}: {
  editSession: EditSession | null;
  isEditStarting: boolean;
  isItemUpdating: boolean;
  onSearchChange: (searchText: string) => void;
  onSelectItem: (itemId: number | null) => void;
  onStartEditSession: () => void;
  onUpdateItemField: (itemId: number, field: string, value: string) => void;
  searchText: string;
  selectedItemId: number | null;
  workflow: ItemsWorkflow | null;
}) {
  const filteredItems = filterItems(workflow?.items ?? [], searchText);
  const selectedItem =
    workflow?.items.find((item) => item.itemId === selectedItemId) ?? filteredItems[0] ?? null;
  const canEditItems = workflow?.summary.availability === 'available';
  const pendingItemIds = getPendingItemIds(editSession);

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
          <Metric
            label="Pending changes"
            value={(editSession?.pendingEdits.length ?? 0).toString()}
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
                <span role="columnheader">Sell</span>
                <span role="columnheader">Source</span>
              </div>
              {filteredItems.map((item) => (
                <button
                  className={`items-row ${selectedItem?.itemId === item.itemId ? 'items-row-selected' : ''} ${
                    pendingItemIds.has(item.itemId) ? 'items-row-pending' : ''
                  }`}
                  key={item.itemId}
                  onClick={() => onSelectItem(item.itemId)}
                  role="row"
                  type="button"
                >
                  <span role="cell">{item.itemId}</span>
                  <span role="cell">{item.name}</span>
                  <span role="cell">{item.category}</span>
                  <span role="cell">{item.buyPrice}</span>
                  <span role="cell">{item.sellPrice}</span>
                  <span role="cell">{formatSourceLayer(item.provenance.sourceLayer)}</span>
                </button>
              ))}
            </div>

            <SelectedItemPanel
              canEditItems={canEditItems}
              editSession={editSession}
              isEditStarting={isEditStarting}
              isItemUpdating={isItemUpdating}
              item={selectedItem}
              editableFields={workflow.editableFields}
              onStartEditSession={onStartEditSession}
              onUpdateItemField={onUpdateItemField}
            />
          </div>
        ) : (
          <p className="empty-copy">Open Items from Workflows to load backend item data.</p>
        )}
      </section>

      <DiagnosticsSection diagnostics={workflow?.diagnostics ?? []} />
    </>
  );
}

function SelectedItemPanel({
  canEditItems,
  editSession,
  editableFields,
  isEditStarting,
  isItemUpdating,
  item,
  onStartEditSession,
  onUpdateItemField
}: {
  canEditItems: boolean;
  editSession: EditSession | null;
  editableFields: ItemEditableField[];
  isEditStarting: boolean;
  isItemUpdating: boolean;
  item: ItemRecord | null;
  onStartEditSession: () => void;
  onUpdateItemField: (itemId: number, field: string, value: string) => void;
}) {
  const [buyPriceDraft, setBuyPriceDraft] = useState('');
  const [sellPriceDraft, setSellPriceDraft] = useState('');
  const buyPriceField = editableFields.find((field) => field.field === buyPriceFieldName);
  const sellPriceField = editableFields.find((field) => field.field === sellPriceFieldName);
  const buyPriceState = getItemPriceDraftState(buyPriceDraft, item?.buyPrice ?? null, buyPriceField);
  const sellPriceState = getItemPriceDraftState(
    sellPriceDraft,
    item?.sellPrice ?? null,
    sellPriceField
  );

  useEffect(() => {
    setBuyPriceDraft(item ? item.buyPrice.toString() : '');
    setSellPriceDraft(item ? item.sellPrice.toString() : '');
  }, [item?.buyPrice, item?.itemId, item?.sellPrice]);

  const canSubmitBuyPrice =
    item !== null &&
    editSession !== null &&
    buyPriceState.canSubmit &&
    buyPriceState.parsedValue !== null;
  const canSubmitSellPrice =
    item !== null &&
    editSession !== null &&
    sellPriceState.canSubmit &&
    sellPriceState.parsedValue !== null;

  return (
    <aside aria-label="Selected item provenance" className="item-inspector">
      <div className="panel-heading">
        <ShieldCheck aria-hidden="true" size={18} />
        <h3>Selected Item</h3>
      </div>

      {item ? (
        <>
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
          </dl>

          <div className="item-edit-form">
            <div className="item-price-editor">
              <label className="path-field">
                <span>{buyPriceField?.label ?? 'Buy price'}</span>
                <input
                  aria-label="Buy price"
                  disabled={
                    !canEditItems ||
                    editSession === null ||
                    buyPriceField === undefined ||
                    isItemUpdating
                  }
                  max={buyPriceField?.maximumValue ?? undefined}
                  min={buyPriceField?.minimumValue ?? undefined}
                  onChange={(event) => setBuyPriceDraft(event.target.value)}
                  type="number"
                  value={buyPriceDraft}
                />
              </label>

              {editSession ? (
                <button
                  aria-label="Save buy price"
                  className="primary-button compact-button"
                  disabled={!canSubmitBuyPrice || isItemUpdating}
                  onClick={() =>
                    onUpdateItemField(
                      item.itemId,
                      buyPriceFieldName,
                      buyPriceState.parsedValue!.toString()
                    )
                  }
                  type="button"
                >
                  <Save aria-hidden="true" size={16} />
                  <span>{isItemUpdating ? 'Saving' : 'Save Buy'}</span>
                </button>
              ) : null}

              <label className="path-field">
                <span>{sellPriceField?.label ?? 'Sell price'}</span>
                <input
                  aria-label="Sell price"
                  disabled={
                    !canEditItems ||
                    editSession === null ||
                    sellPriceField === undefined ||
                    isItemUpdating
                  }
                  max={sellPriceField?.maximumValue ?? undefined}
                  min={sellPriceField?.minimumValue ?? undefined}
                  onChange={(event) => setSellPriceDraft(event.target.value)}
                  type="number"
                  value={sellPriceDraft}
                />
              </label>

              {editSession ? (
                <button
                  aria-label="Save sell price"
                  className="primary-button compact-button"
                  disabled={!canSubmitSellPrice || isItemUpdating}
                  onClick={() =>
                    onUpdateItemField(
                      item.itemId,
                      sellPriceFieldName,
                      sellPriceState.parsedValue!.toString()
                    )
                  }
                  type="button"
                >
                  <Save aria-hidden="true" size={16} />
                  <span>{isItemUpdating ? 'Saving' : 'Save Sell'}</span>
                </button>
              ) : null}
            </div>

            {!editSession ? (
              <button
                className="secondary-button"
                disabled={!canEditItems || isEditStarting}
                onClick={onStartEditSession}
                type="button"
              >
                <Pencil aria-hidden="true" size={16} />
                <span>{isEditStarting ? 'Starting' : 'Start Edit Session'}</span>
              </button>
            ) : null}
          </div>
        </>
      ) : (
        <p className="empty-copy">No item selected.</p>
      )}
    </aside>
  );
}

function ChangesSection({
  applyResult,
  changePlan,
  diagnostics,
  editSession,
  isChangePlanApplying,
  isChangePlanCreating,
  isSessionValidating,
  onApplyChangePlan,
  onCreateChangePlan,
  onValidateEditSession
}: {
  applyResult: ApplyResult | null;
  changePlan: ChangePlan | null;
  diagnostics: ApiDiagnostic[];
  editSession: EditSession | null;
  isChangePlanApplying: boolean;
  isChangePlanCreating: boolean;
  isSessionValidating: boolean;
  onApplyChangePlan: () => void;
  onCreateChangePlan: () => void;
  onValidateEditSession: () => void;
}) {
  const pendingEdits = editSession?.pendingEdits ?? [];
  const combinedDiagnostics = [
    ...diagnostics,
    ...(changePlan?.diagnostics ?? []),
    ...(applyResult?.diagnostics ?? [])
  ];

  return (
    <>
      <section aria-labelledby="changes-heading" className="panel wide-panel">
        <div className="panel-heading">
          <ClipboardCheck aria-hidden="true" size={18} />
          <h2 id="changes-heading">Edit Session</h2>
        </div>

        <div className="changes-summary">
          <Metric label="Pending changes" value={pendingEdits.length.toString()} />
          <Metric label="Target files" value={(changePlan?.writes.length ?? 0).toString()} />
          <Metric label="Written files" value={(applyResult?.writtenFiles.length ?? 0).toString()} />
          <button
            className="secondary-button"
            disabled={pendingEdits.length === 0 || isSessionValidating}
            onClick={onValidateEditSession}
            type="button"
          >
            <CheckCircle aria-hidden="true" size={18} />
            <span>{isSessionValidating ? 'Validating' : 'Validate Pending Change'}</span>
          </button>
          <button
            className="primary-button"
            disabled={pendingEdits.length === 0 || isChangePlanCreating}
            onClick={onCreateChangePlan}
            type="button"
          >
            <ClipboardCheck aria-hidden="true" size={18} />
            <span>{isChangePlanCreating ? 'Reviewing' : 'Review Change Plan'}</span>
          </button>
        </div>

        {pendingEdits.length > 0 ? (
          <ul className="pending-edit-list">
            {pendingEdits.map((edit, index) => (
              <li key={`${edit.domain}-${edit.recordId ?? index}-${edit.field ?? 'field'}`}>
                <strong>{edit.summary}</strong>
                <span>{edit.sources.map((source) => source.relativePath).join(', ')}</span>
              </li>
            ))}
          </ul>
        ) : (
          <p className="empty-copy">No pending changes.</p>
        )}
      </section>

      {changePlan ? (
        <ChangePlanSection
          changePlan={changePlan}
          isApplying={isChangePlanApplying}
          onApplyChangePlan={onApplyChangePlan}
        />
      ) : null}
      {applyResult ? <ApplyResultSection applyResult={applyResult} /> : null}
      <DiagnosticsSection diagnostics={combinedDiagnostics} />
    </>
  );
}

function ChangePlanSection({
  changePlan,
  isApplying,
  onApplyChangePlan
}: {
  changePlan: ChangePlan;
  isApplying: boolean;
  onApplyChangePlan: () => void;
}) {
  const canApply = changePlan.canApply && changePlan.writes.length > 0;

  return (
    <section aria-labelledby="change-plan-heading" className="panel wide-panel">
      <div className="panel-heading">
        <ClipboardCheck aria-hidden="true" size={18} />
        <h2 id="change-plan-heading">Change Plan Review</h2>
      </div>

      <div className="change-plan-status">
        <Metric label="Plan status" value={changePlan.canApply ? 'Ready' : 'Needs fixes'} />
        <Metric label="Session" value={changePlan.sessionId} />
        <button
          className="primary-button"
          disabled={!canApply || isApplying}
          onClick={onApplyChangePlan}
          type="button"
        >
          <Save aria-hidden="true" size={18} />
          <span>{isApplying ? 'Applying' : 'Apply Plan'}</span>
        </button>
      </div>

      {changePlan.writes.length > 0 ? (
        <ul className="change-plan-list">
          {changePlan.writes.map((write) => (
            <li key={write.targetRelativePath}>
              <div>
                <strong>{write.targetRelativePath}</strong>
                <span>{write.reason}</span>
              </div>
              <dl>
                <div>
                  <dt>Output state</dt>
                  <dd>{write.replacesExistingOutput ? 'Replaces output file' : 'Creates output file'}</dd>
                </div>
                <div>
                  <dt>Sources</dt>
                  <dd>
                    {write.sources
                      .map((source) => `${formatProjectFileLayer(source.layer)} ${source.relativePath}`)
                      .join(', ')}
                  </dd>
                </div>
              </dl>
            </li>
          ))}
        </ul>
      ) : (
        <p className="empty-copy">No target files in this plan.</p>
      )}
    </section>
  );
}

function ApplyResultSection({ applyResult }: { applyResult: ApplyResult }) {
  return (
    <section aria-labelledby="apply-result-heading" className="panel wide-panel">
      <div className="panel-heading">
        <CheckCircle aria-hidden="true" size={18} />
        <h2 id="apply-result-heading">Apply Result</h2>
      </div>

      <div className="change-plan-status">
        <Metric label="Apply ID" value={applyResult.applyId} />
        <Metric label="Written files" value={applyResult.writtenFiles.length.toString()} />
      </div>

      {applyResult.writtenFiles.length > 0 ? (
        <ul className="written-file-list">
          {applyResult.writtenFiles.map((writtenFile) => (
            <li key={writtenFile}>{writtenFile}</li>
          ))}
        </ul>
      ) : (
        <p className="empty-copy">No files were written.</p>
      )}
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
    [
      item.itemId.toString(),
      item.name,
      item.category,
      item.buyPrice.toString(),
      item.sellPrice.toString()
    ].some((value) => value.toLocaleLowerCase().includes(normalizedSearch))
  );
}

function getItemPriceDraftState(
  draftValue: string,
  currentValue: number | null,
  field: ItemEditableField | undefined
) {
  const normalizedValue = draftValue.trim();
  const parsedValue = /^\d+$/.test(normalizedValue)
    ? Number.parseInt(normalizedValue, 10)
    : null;
  const minimumValue = field?.minimumValue ?? null;
  const maximumValue = field?.maximumValue ?? null;
  const inRange =
    parsedValue !== null &&
    (minimumValue === null || parsedValue >= minimumValue) &&
    (maximumValue === null || parsedValue <= maximumValue);

  return {
    canSubmit:
      field !== undefined &&
      currentValue !== null &&
      inRange &&
      parsedValue !== currentValue,
    parsedValue
  };
}

function getPendingItemIds(editSession: EditSession | null) {
  return new Set(
    (editSession?.pendingEdits ?? [])
      .filter((edit) => edit.domain === 'workflow.items')
      .map((edit) => Number.parseInt(edit.recordId ?? '', 10))
      .filter(Number.isInteger)
  );
}

function getWorkflowState(health: ProjectHealth | null, workflow: WorkflowSummary | undefined) {
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
  return {
    base: 'Base',
    generated: 'Generated',
    layered: 'LayeredFS',
    pending: 'Pending'
  }[layer];
}

function formatProjectFileLayer(layer: ChangePlan['writes'][number]['sources'][number]['layer']) {
  return {
    base: 'Base',
    generated: 'Generated',
    layered: 'LayeredFS',
    pending: 'Pending'
  }[layer];
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
