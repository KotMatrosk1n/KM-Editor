/* SPDX-License-Identifier: GPL-3.0-only */

import { AlertTriangle, Download, FolderOpen, RefreshCw } from 'lucide-react';
import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  type ApiDiagnostic,
  type ProjectHealth
} from '../../bridge/contracts';
import {
  type GameDumpCategory,
  type GameDumpFormat,
  type GameDumpResult,
  type GameDumpSelection,
  type LoadGameDumpWorkflowRequest
} from '../../bridge/gameDumpContracts';
import { ProjectBridgeError, type ProjectBridge } from '../../bridge/projectBridge';
import type { DesktopServices } from '../../desktopServices';
import { DiagnosticsSection, Metric } from '../../components/workflowPanels';
import { formatDiagnosticMessage } from '../../diagnostics';
import { useLocalization } from '../../localization';

type ProjectPaths = LoadGameDumpWorkflowRequest['paths'];

type GameDumpSelectionState = Record<
  string,
  {
    format: GameDumpFormat;
    selected: boolean;
  }
>;

type GameDumpProgress = {
  detail: string;
  label: string;
  mode: 'determinate' | 'indeterminate';
  percent?: number;
  selectedCategoryCount?: number;
  writtenFileCount?: number;
};

export function GameDumpSection({
  bridge,
  desktopServices,
  health,
  paths
}: {
  bridge: ProjectBridge;
  desktopServices: DesktopServices;
  health: ProjectHealth | null;
  paths: ProjectPaths;
}) {
  const [workflowCategories, setWorkflowCategories] = useState<GameDumpCategory[]>([]);
  const [workflowDiagnostics, setWorkflowDiagnostics] = useState<ApiDiagnostic[]>([]);
  const [selectionState, setSelectionState] = useState<GameDumpSelectionState>({});
  const [destinationFolder, setDestinationFolder] = useState('');
  const [result, setResult] = useState<GameDumpResult | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [isGenerating, setIsGenerating] = useState(false);
  const [isConfirmOpen, setIsConfirmOpen] = useState(false);
  const [actionDiagnostics, setActionDiagnostics] = useState<ApiDiagnostic[]>([]);
  const [progress, setProgress] = useState<GameDumpProgress | null>(null);
  const { translateLiteral } = useLocalization();

  const selectedCategories = useMemo(
    () =>
      workflowCategories.filter(
        (category) => category.isAvailable && selectionState[category.id]?.selected
      ),
    [selectionState, workflowCategories]
  );
  const selectedCount = selectedCategories.length;

  const loadWorkflow = useCallback(async () => {
    if (!health?.canOpenReadOnlyWorkflows || paths.selectedGame === null) {
      setWorkflowCategories([]);
      setWorkflowDiagnostics([]);
      setSelectionState({});
      setProgress(null);
      return;
    }

    setIsLoading(true);
    setActionDiagnostics([]);
    setProgress({
      detail: 'Reading available dump categories.',
      label: 'Loading Game Dump',
      mode: 'indeterminate'
    });
    try {
      const response = await bridge.loadGameDumpWorkflow({ paths });
      setWorkflowCategories(response.workflow.categories);
      setWorkflowDiagnostics(response.workflow.diagnostics);
      setSelectionState((current) =>
        Object.fromEntries(
          response.workflow.categories.map((category) => [
            category.id,
            {
              format: current[category.id]?.format ?? category.defaultFormat,
              selected: current[category.id]?.selected ?? category.isAvailable
            }
          ])
        )
      );
      setProgress(null);
    } catch (error) {
      setActionDiagnostics(toDiagnostics(error));
      setProgress({
        detail: 'Review diagnostics before using these dump files.',
        label: 'Game Dump failed.',
        mode: 'determinate',
        percent: 100
      });
    } finally {
      setIsLoading(false);
    }
  }, [bridge, health?.canOpenReadOnlyWorkflows, paths]);

  useEffect(() => {
    void loadWorkflow();
  }, [loadWorkflow]);

  const handleBrowseDestination = async () => {
    const selectedFolder = await desktopServices.pickFolder({
      defaultPath: destinationFolder || undefined,
      title: translateLiteral('Select Game Dump destination')
    });
    if (selectedFolder) {
      setDestinationFolder(selectedFolder);
      setResult(null);
      setActionDiagnostics([]);
      setProgress(null);
    }
  };

  const handleOpenDestination = async () => {
    if (!destinationFolder) {
      return;
    }

    try {
      await desktopServices.openPath(destinationFolder);
    } catch (error) {
      setActionDiagnostics(toDiagnostics(error));
    }
  };

  const handleGenerate = async () => {
    setIsConfirmOpen(false);
    setIsGenerating(true);
    setResult(null);
    setActionDiagnostics([]);
    try {
      const selections: GameDumpSelection[] = selectedCategories.map((category) => ({
        categoryId: category.id,
        format: selectionState[category.id]?.format ?? category.defaultFormat
      }));
      setProgress({
        detail: 'Preparing selected categories.',
        label: 'Generating Dump Files',
        mode: 'determinate',
        percent: 10,
        selectedCategoryCount: selections.length
      });
      await Promise.resolve();
      setProgress({
        detail: 'Writing selected dump files.',
        label: 'Generating Dump Files',
        mode: 'indeterminate',
        percent: 45,
        selectedCategoryCount: selections.length
      });
      const response = await bridge.runGameDump({
        destinationFolder,
        paths,
        selections
      });
      setResult(response.result);
      setActionDiagnostics(response.result.diagnostics);
      setProgress({
        detail: response.result.succeeded
          ? 'Dump files are ready in the selected destination.'
          : 'Review diagnostics before using these dump files.',
        label: response.result.succeeded ? 'Dump files generated' : 'Dump completed with issues',
        mode: 'determinate',
        percent: 100,
        selectedCategoryCount: selections.length,
        writtenFileCount: response.result.writtenFiles.length
      });
    } catch (error) {
      setActionDiagnostics(toDiagnostics(error));
      setProgress({
        detail: 'Review diagnostics before using these dump files.',
        label: 'Game Dump failed.',
        mode: 'determinate',
        percent: 100
      });
    } finally {
      setIsGenerating(false);
    }
  };

  const canGenerate = selectedCount > 0 && destinationFolder.trim().length > 0 && !isGenerating;
  const availableCount = workflowCategories.filter((category) => category.isAvailable).length;

  return (
    <section aria-labelledby="game-dump-heading" className="panel wide-panel game-dump-section">
      <div className="panel-heading">
        <Download aria-hidden="true" size={18} />
        <h2 id="game-dump-heading">{translateLiteral('Game Dump')}</h2>
      </div>

      {!health?.canOpenReadOnlyWorkflows ? (
        <p className="empty-copy">
          {translateLiteral('Validate project paths before generating dump files.')}
        </p>
      ) : (
        <>
          <div className="game-dump-destination-panel">
            <label className="path-field game-dump-destination-field">
              <span>{translateLiteral('Destination folder')}</span>
              <div className="game-dump-destination-input-row">
                <input
                  aria-label={translateLiteral('Destination folder')}
                  data-localization-ignore="true"
                  onChange={(event) => {
                    setDestinationFolder(event.target.value);
                    setResult(null);
                    setProgress(null);
                  }}
                  placeholder={translateLiteral('Select a destination folder')}
                  type="text"
                  value={destinationFolder}
                />
                <button
                  aria-label={translateLiteral('Browse for destination folder')}
                  className="secondary-button icon-button"
                  disabled={!desktopServices.isAvailable || isGenerating}
                  onClick={handleBrowseDestination}
                  title={translateLiteral('Browse for destination folder')}
                  type="button"
                >
                  <FolderOpen aria-hidden="true" size={18} />
                </button>
                <button
                  aria-label={translateLiteral('Open destination folder')}
                  className="secondary-button icon-button"
                  disabled={!destinationFolder || isGenerating}
                  onClick={handleOpenDestination}
                  title={translateLiteral('Open destination folder')}
                  type="button"
                >
                  <FolderOpen aria-hidden="true" size={18} />
                </button>
                <button
                  aria-label={translateLiteral('Refresh dump categories')}
                  className="secondary-button icon-button"
                  disabled={isLoading || isGenerating}
                  onClick={() => void loadWorkflow()}
                  title={translateLiteral('Refresh dump categories')}
                  type="button"
                >
                  <RefreshCw aria-hidden="true" size={18} />
                </button>
              </div>
            </label>
          </div>

          <div className="metrics-grid game-dump-metrics">
            <Metric label={translateLiteral('Categories')} value={String(workflowCategories.length)} />
            <Metric label={translateLiteral('Available')} value={String(availableCount)} />
            <Metric label={translateLiteral('Selected')} value={String(selectedCount)} />
            <Metric
              label={translateLiteral('Written files')}
              value={String(result?.writtenFiles.length ?? 0)}
            />
          </div>

          <div className="game-dump-actions">
            <button
              className="secondary-button compact-button"
              disabled={availableCount === 0 || isGenerating}
              onClick={() =>
                setSelectionState((current) =>
                  Object.fromEntries(
                    workflowCategories.map((category) => [
                      category.id,
                      {
                        format: current[category.id]?.format ?? category.defaultFormat,
                        selected: category.isAvailable
                      }
                    ])
                  )
                )
              }
              type="button"
            >
              {translateLiteral('Select All')}
            </button>
            <button
              className="secondary-button compact-button"
              disabled={workflowCategories.length === 0 || isGenerating}
              onClick={() =>
                setSelectionState((current) =>
                  Object.fromEntries(
                    workflowCategories.map((category) => [
                      category.id,
                      {
                        format: current[category.id]?.format ?? category.defaultFormat,
                        selected: false
                      }
                    ])
                  )
                )
              }
              type="button"
            >
              {translateLiteral('Clear')}
            </button>
            <button
              className="primary-button"
              disabled={!canGenerate}
              onClick={() => setIsConfirmOpen(true)}
              type="button"
            >
              <Download aria-hidden="true" size={16} />
              {translateLiteral(isGenerating ? 'Generating...' : 'Generate Dump Files')}
            </button>
          </div>

          {progress ? (
            <GameDumpProgressPanel progress={progress} translateLiteral={translateLiteral} />
          ) : null}

          <div className="game-dump-category-list">
            {workflowCategories.map((category) => {
              const state = selectionState[category.id] ?? {
                format: category.defaultFormat,
                selected: false
              };
              const blockedReason =
                category.diagnostics.find((diagnostic) => diagnostic.severity === 'error')
                  ?.message ?? category.diagnostics[0]?.message;

              return (
                <article
                  className={`game-dump-category ${state.selected ? 'is-selected' : ''}`}
                  key={category.id}
                >
                  <label className="game-dump-category-check">
                    <input
                      checked={state.selected && category.isAvailable}
                      disabled={!category.isAvailable || isGenerating}
                      onChange={(event) =>
                        setSelectionState((current) => ({
                          ...current,
                          [category.id]: {
                            format: current[category.id]?.format ?? category.defaultFormat,
                            selected: event.target.checked
                          }
                        }))
                      }
                      type="checkbox"
                    />
                    <span>
                      <strong>{translateLiteral(category.label)}</strong>
                      <small>{translateLiteral(category.description)}</small>
                    </span>
                  </label>
                  <div className="game-dump-category-controls">
                    <span className={`status-pill ${category.isAvailable ? 'status-ready' : 'status-blocked'}`}>
                      {translateLiteral(category.isAvailable ? 'Available' : 'Unavailable')}
                    </span>
                    <label className="path-field game-dump-format-field">
                      <span>{translateLiteral('Format')}</span>
                      <select
                        aria-label={`${translateLiteral(category.label)} ${translateLiteral('Format')}`}
                        disabled={!category.isAvailable || !state.selected || isGenerating}
                        onChange={(event) =>
                          setSelectionState((current) => ({
                            ...current,
                            [category.id]: {
                              format: event.target.value as GameDumpFormat,
                              selected: current[category.id]?.selected ?? true
                            }
                          }))
                        }
                        value={state.format}
                      >
                        {category.formats.map((format) => (
                          <option key={format} value={format}>
                            {formatGameDumpFormat(format, translateLiteral)}
                          </option>
                        ))}
                      </select>
                    </label>
                  </div>
                  {blockedReason ? (
                    <p className="workflow-disabled-reason">
                      {formatDiagnosticMessage(
                        { message: blockedReason, severity: 'warning' },
                        translateLiteral
                      )}
                    </p>
                  ) : null}
                </article>
              );
            })}
          </div>

          {isLoading ? (
            <p className="empty-copy">{translateLiteral('Loading dump categories...')}</p>
          ) : null}

          {result ? (
            <div className="game-dump-result">
              <h3>
                {translateLiteral(
                  result.succeeded ? 'Dump files generated' : 'Dump completed with issues'
                )}
              </h3>
              <p data-localization-ignore="true">{result.destinationFolder}</p>
              <div className="game-dump-file-list">
                {result.writtenFiles.map((file) => (
                  <span data-localization-ignore="true" key={`${file.categoryId}:${file.relativePath}`}>
                    {file.relativePath} ({formatBytes(file.sizeBytes)})
                  </span>
                ))}
              </div>
            </div>
          ) : null}
        </>
      )}

      <DiagnosticsSection diagnostics={[...workflowDiagnostics, ...actionDiagnostics]} />

      {isConfirmOpen ? (
        <GameDumpConfirmationModal
          categoryCount={selectedCount}
          destinationFolder={destinationFolder}
          isGenerating={isGenerating}
          onCancel={() => setIsConfirmOpen(false)}
          onConfirm={() => void handleGenerate()}
          translateLiteral={translateLiteral}
        />
      ) : null}
    </section>
  );
}

function GameDumpConfirmationModal({
  categoryCount,
  destinationFolder,
  isGenerating,
  onCancel,
  onConfirm,
  translateLiteral
}: {
  categoryCount: number;
  destinationFolder: string;
  isGenerating: boolean;
  onCancel: () => void;
  onConfirm: () => void;
  translateLiteral: (literal: string) => string;
}) {
  return (
    <div aria-labelledby="game-dump-confirm-heading" aria-modal="true" className="modal-backdrop" role="dialog">
      <section className="modal-panel">
        <div className="panel-heading">
          <AlertTriangle aria-hidden="true" size={18} />
          <h2 id="game-dump-confirm-heading">{translateLiteral('Generate Dump Files')}</h2>
        </div>
        <p className="modal-copy">
          {translateLiteral(
            'Selected category folders and the manifest in the destination will be overwritten.'
          )}
        </p>
        <dl className="game-dump-confirm-details">
          <div>
            <dt>{translateLiteral('Selected categories')}</dt>
            <dd>{categoryCount}</dd>
          </div>
          <div>
            <dt>{translateLiteral('Destination')}</dt>
            <dd data-localization-ignore="true">{destinationFolder}</dd>
          </div>
        </dl>
        <div className="modal-actions">
          <button className="secondary-button" onClick={onCancel} type="button">
            {translateLiteral('Cancel')}
          </button>
          <button className="primary-button" disabled={isGenerating} onClick={onConfirm} type="button">
            <Download aria-hidden="true" size={16} />
            {translateLiteral('Generate Dump Files')}
          </button>
        </div>
      </section>
    </div>
  );
}

function GameDumpProgressPanel({
  progress,
  translateLiteral
}: {
  progress: GameDumpProgress;
  translateLiteral: (literal: string) => string;
}) {
  const percent = Math.max(0, Math.min(100, progress.percent ?? 0));
  const isDeterminate = progress.mode === 'determinate';

  return (
    <div className="game-dump-progress-panel" role="status">
      <div className="game-dump-progress-header">
        <strong>{translateLiteral(progress.label)}</strong>
        <span>{isDeterminate ? `${percent}%` : translateLiteral('Working')}</span>
      </div>
      <div
        aria-label={translateLiteral('Game Dump progress')}
        aria-valuemax={100}
        aria-valuemin={0}
        aria-valuenow={isDeterminate ? percent : undefined}
        className={`work-progress-track${isDeterminate ? '' : ' work-progress-track-indeterminate'}`}
        role="progressbar"
      >
        <div className="work-progress-fill" style={{ width: isDeterminate ? `${percent}%` : undefined }} />
      </div>
      <dl className="work-progress-detail">
        <div>
          <dt>{translateLiteral('Status')}</dt>
          <dd>{translateLiteral(progress.detail)}</dd>
        </div>
        {progress.selectedCategoryCount !== undefined ? (
          <div>
            <dt>{translateLiteral('Selected categories')}</dt>
            <dd>{progress.selectedCategoryCount}</dd>
          </div>
        ) : null}
        {progress.writtenFileCount !== undefined ? (
          <div>
            <dt>{translateLiteral('Written files')}</dt>
            <dd>{progress.writtenFileCount}</dd>
          </div>
        ) : null}
      </dl>
    </div>
  );
}

function formatGameDumpFormat(
  format: GameDumpFormat,
  translateLiteral: (literal: string) => string
) {
  switch (format) {
    case 'tsv':
      return 'TSV';
    case 'csv':
      return 'CSV';
    case 'json':
      return 'JSON';
    case 'tsvAndJson':
      return 'TSV + JSON';
    case 'txt':
      return 'TXT';
    case 'txtAndJson':
      return 'TXT + JSON';
    case 'raw':
      return translateLiteral('Raw');
    case 'rawAndJson':
      return translateLiteral('Raw + JSON');
    default:
      return format;
  }
}

function formatBytes(value: number) {
  if (value < 1024) {
    return `${value} B`;
  }

  if (value < 1024 * 1024) {
    return `${(value / 1024).toFixed(1)} KB`;
  }

  return `${(value / (1024 * 1024)).toFixed(1)} MB`;
}

function toDiagnostics(error: unknown): ApiDiagnostic[] {
  if (error instanceof ProjectBridgeError) {
    return error.apiError.diagnostics.length > 0
      ? error.apiError.diagnostics
      : [
          {
            message: error.message,
            severity: 'error'
          }
        ];
  }

  return [
    {
      message: error instanceof Error ? error.message : 'Game Dump failed.',
      severity: 'error'
    }
  ];
}
