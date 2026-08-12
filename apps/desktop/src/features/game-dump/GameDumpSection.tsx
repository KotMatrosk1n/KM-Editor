/* SPDX-License-Identifier: GPL-3.0-only */

import { AlertTriangle, Download, FolderOpen, RefreshCw, Search } from 'lucide-react';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
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
import { type ProjectBridge } from '../../bridge/projectBridge';
import type { DesktopServices } from '../../desktopServices';
import { FieldLabel } from '../../components/FieldLabel';
import { DiagnosticsSection, Metric } from '../../components/workflowPanels';
import { useModalDialog } from '../../components/useModalDialog';
import { formatDiagnosticMessage } from '../../diagnostics';
import { desktopErrorCodes } from '../../errorCodes';
import { useLocalization } from '../../localization';
import {
  toDesktopErrorDiagnostics,
  toProjectBridgeDiagnostics
} from '../../uiErrorDiagnostics';

type ProjectPaths = LoadGameDumpWorkflowRequest['paths'];

type GameDumpSelectionState = Record<
  string,
  {
    format: GameDumpFormat;
    languageCodes?: string[];
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

type GameDumpCategoryFilter = 'all' | 'available' | 'selected';

const destinationStorageKey = 'km-editor.game-dump-destinations.v1';
const allGameDumpLanguagesValue = '__all__';

export function GameDumpSection({
  appVersion,
  bridge,
  desktopServices,
  health,
  onWriteStateChange,
  paths
}: {
  appVersion: string;
  bridge: ProjectBridge;
  desktopServices: DesktopServices;
  health: ProjectHealth | null;
  onWriteStateChange?: (isWriting: boolean) => void;
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
  const [categoryFilter, setCategoryFilter] = useState<GameDumpCategoryFilter>('all');
  const [categorySearch, setCategorySearch] = useState('');
  const loadWorkflowRunRef = useRef(0);
  const { t, translateLiteral } = useLocalization();

  const selectedCategories = useMemo(
    () =>
      workflowCategories.filter(
        (category) => category.isAvailable && selectionState[category.id]?.selected
      ),
    [selectionState, workflowCategories]
  );
  const selectedCount = selectedCategories.length;
  const filteredCategories = useMemo(() => {
    const normalizedSearch = categorySearch.trim().toLocaleLowerCase();
    return workflowCategories.filter((category) => {
      const state = selectionState[category.id];
      if (categoryFilter === 'available' && !category.isAvailable) {
        return false;
      }
      if (categoryFilter === 'selected' && !state?.selected) {
        return false;
      }
      if (!normalizedSearch) {
        return true;
      }

      return [category.id, translateLiteral(category.label), translateLiteral(category.description)]
        .join(' ')
        .toLocaleLowerCase()
        .includes(normalizedSearch);
    });
  }, [categoryFilter, categorySearch, selectionState, translateLiteral, workflowCategories]);

  const invalidateGeneratedState = useCallback(() => {
    setIsConfirmOpen(false);
    setResult(null);
    setActionDiagnostics([]);
    setProgress(null);
  }, []);

  useEffect(() => {
    if (!paths.selectedGame) {
      setDestinationFolder('');
      return;
    }
    setDestinationFolder(loadRememberedDestination(paths.selectedGame));
  }, [paths.selectedGame]);

  const updateDestinationFolder = useCallback(
    (destination: string) => {
      setDestinationFolder(destination);
      if (paths.selectedGame) {
        rememberDestination(paths.selectedGame, destination);
      }
      invalidateGeneratedState();
    },
    [invalidateGeneratedState, paths.selectedGame]
  );

  const loadWorkflow = useCallback(async () => {
    const runId = ++loadWorkflowRunRef.current;
    if (!health?.canOpenReadOnlyWorkflows || paths.selectedGame === null) {
      setIsLoading(false);
      setWorkflowCategories([]);
      setWorkflowDiagnostics([]);
      setSelectionState({});
      setResult(null);
      setProgress(null);
      return;
    }

    setIsLoading(true);
    setIsConfirmOpen(false);
    setResult(null);
    setActionDiagnostics([]);
    setProgress({
      detail: 'Reading available dump categories.',
      label: 'Loading Game Dump',
      mode: 'indeterminate'
    });
    try {
      const response = await bridge.loadGameDumpWorkflow({ paths });
      if (loadWorkflowRunRef.current !== runId) {
        return;
      }

      setWorkflowCategories(response.workflow.categories);
      setWorkflowDiagnostics(response.workflow.diagnostics);
      setSelectionState((current) =>
        Object.fromEntries(
          response.workflow.categories.map((category) => [
            category.id,
            {
              format: current[category.id]?.format ?? category.defaultFormat,
              languageCodes: resolveLanguageSelection(
                category,
                current[category.id]?.languageCodes
              ),
              selected: current[category.id]?.selected ?? category.isAvailable
            }
          ])
        )
      );
      setProgress(null);
    } catch (error) {
      if (loadWorkflowRunRef.current !== runId) {
        return;
      }

      const failureDiagnostics = toProjectBridgeDiagnostics(
        error,
        'Game Dump could not be loaded.'
      );
      setWorkflowCategories([]);
      setWorkflowDiagnostics([]);
      setSelectionState({});
      if (failureDiagnostics.length === 0) {
        setProgress(null);
        return;
      }

      setActionDiagnostics(failureDiagnostics);
      setProgress({
        detail: 'Review diagnostics before using these dump files.',
        label: 'Game Dump failed.',
        mode: 'determinate',
        percent: 100
      });
    } finally {
      if (loadWorkflowRunRef.current === runId) {
        setIsLoading(false);
      }
    }
  }, [bridge, health?.canOpenReadOnlyWorkflows, paths]);

  useEffect(() => {
    void loadWorkflow();
  }, [loadWorkflow]);

  const handleBrowseDestination = async () => {
    try {
      const selectedFolder = await desktopServices.pickFolder({
        defaultPath: destinationFolder || undefined,
        title: translateLiteral('Select Game Dump destination')
      });
      if (selectedFolder) {
        updateDestinationFolder(selectedFolder);
      }
    } catch (error) {
      setActionDiagnostics(
        toDesktopErrorDiagnostics(
          error,
          'Could not choose the Game Dump destination.',
          desktopErrorCodes.folderPickerFailed
        )
      );
    }
  };

  const handleOpenDestination = async () => {
    if (!destinationFolder) {
      return;
    }

    try {
      await desktopServices.openPath(destinationFolder);
    } catch (error) {
      setActionDiagnostics(
        toDesktopErrorDiagnostics(
          error,
          'Could not open the Game Dump destination.',
          desktopErrorCodes.pathOpenFailed
        )
      );
    }
  };

  const handleGenerate = async () => {
    if (
      isLoading ||
      isGenerating ||
      selectedCategories.length === 0 ||
      destinationFolder.trim().length === 0
    ) {
      return;
    }

    setIsConfirmOpen(false);
    onWriteStateChange?.(true);
    setIsGenerating(true);
    setResult(null);
    setActionDiagnostics([]);
    try {
      const selections: GameDumpSelection[] = selectedCategories.map((category) => ({
        categoryId: category.id,
        format: selectionState[category.id]?.format ?? category.defaultFormat,
        ...(category.languageOptions
          ? {
              languageCodes: resolveLanguageSelection(
                category,
                selectionState[category.id]?.languageCodes
              )
            }
          : {})
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
        producerVersion: appVersion,
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
      const failureDiagnostics = toProjectBridgeDiagnostics(
        error,
        'Game Dump generation failed.'
      );
      if (failureDiagnostics.length === 0) {
        return;
      }

      setActionDiagnostics(failureDiagnostics);
      setProgress({
        detail: 'Review diagnostics before using these dump files.',
        label: 'Game Dump failed.',
        mode: 'determinate',
        percent: 100
      });
    } finally {
      setIsGenerating(false);
      onWriteStateChange?.(false);
    }
  };

  const canGenerate =
    selectedCount > 0 &&
    destinationFolder.trim().length > 0 &&
    !isLoading &&
    !isGenerating;
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
          <div className="game-dump-step-heading">
            <span aria-hidden="true">1</span>
            <h3>{translateLiteral('Destination')}</h3>
          </div>
          <div className="game-dump-destination-panel">
            <div className="path-field game-dump-destination-field">
              <FieldLabel
                help={t('workflowHelp.gameDump.destination')}
                htmlFor="game-dump-destination-folder"
                label={translateLiteral('Destination folder')}
              />
              <div className="game-dump-destination-input-row">
                <input
                  aria-label={translateLiteral('Destination folder')}
                  data-localization-ignore="true"
                  disabled={isGenerating}
                  id="game-dump-destination-folder"
                  onChange={(event) => updateDestinationFolder(event.target.value)}
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
            </div>
          </div>

          <div className="game-dump-step-heading">
            <span aria-hidden="true">2</span>
            <h3>{translateLiteral('Categories')}</h3>
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

          <div className="game-dump-category-toolbar">
            <label className="search-box game-dump-search">
              <Search aria-hidden="true" size={16} />
              <input
                aria-label={translateLiteral('Search')}
                onChange={(event) => setCategorySearch(event.target.value)}
                placeholder={translateLiteral('Search')}
                type="search"
                value={categorySearch}
              />
            </label>
            <div
              aria-label={translateLiteral('Categories')}
              className="game-dump-category-filters"
              role="group"
            >
              {(['all', 'available', 'selected'] as const).map((filter) => (
                <button
                  aria-pressed={categoryFilter === filter}
                  className="secondary-button compact-button"
                  key={filter}
                  onClick={() => setCategoryFilter(filter)}
                  type="button"
                >
                  {translateLiteral(
                    filter === 'all' ? 'All' : filter === 'available' ? 'Available' : 'Selected'
                  )}
                </button>
              ))}
            </div>
            <div className="game-dump-actions">
            <button
              className="secondary-button compact-button"
              disabled={availableCount === 0 || isLoading || isGenerating}
              onClick={() => {
                invalidateGeneratedState();
                setSelectionState((current) =>
                  Object.fromEntries(
                    workflowCategories.map((category) => [
                      category.id,
                      {
                        format: current[category.id]?.format ?? category.defaultFormat,
                        languageCodes: resolveLanguageSelection(
                          category,
                          current[category.id]?.languageCodes
                        ),
                        selected: category.isAvailable
                      }
                    ])
                  )
                );
              }}
              type="button"
            >
              {translateLiteral('Select All')}
            </button>
            <button
              className="secondary-button compact-button"
              disabled={workflowCategories.length === 0 || isLoading || isGenerating}
              onClick={() => {
                invalidateGeneratedState();
                setSelectionState((current) =>
                  Object.fromEntries(
                    workflowCategories.map((category) => [
                      category.id,
                      {
                        format: current[category.id]?.format ?? category.defaultFormat,
                        languageCodes: resolveLanguageSelection(
                          category,
                          current[category.id]?.languageCodes
                        ),
                        selected: false
                      }
                    ])
                  )
                );
              }}
              type="button"
            >
              {translateLiteral('Clear')}
            </button>
            </div>
          </div>

          {progress ? (
            <GameDumpProgressPanel progress={progress} translateLiteral={translateLiteral} />
          ) : null}

          <div className="game-dump-category-list">
            {filteredCategories.map((category) => {
              const state = selectionState[category.id] ?? {
                format: category.defaultFormat,
                languageCodes: resolveLanguageSelection(category),
                selected: false
              };
              const blockedReason =
                category.diagnostics.find((diagnostic) => diagnostic.severity === 'error')
                  ?.message ?? category.diagnostics[0]?.message;
              const categoryInputId = `game-dump-category-${category.id}`;
              const formatInputId = `game-dump-format-${category.id}`;
              const languageInputId = `game-dump-language-${category.id}`;

              return (
                <article
                  className={`game-dump-category ${state.selected ? 'is-selected' : ''}`}
                  key={category.id}
                >
                  <div className="game-dump-category-check">
                    <input
                      checked={state.selected && category.isAvailable}
                      disabled={!category.isAvailable || isLoading || isGenerating}
                      id={categoryInputId}
                      onChange={(event) => {
                        invalidateGeneratedState();
                        setSelectionState((current) => ({
                          ...current,
                          [category.id]: {
                            format: current[category.id]?.format ?? category.defaultFormat,
                            languageCodes: resolveLanguageSelection(
                              category,
                              current[category.id]?.languageCodes
                            ),
                            selected: event.target.checked
                          }
                        }));
                      }}
                      type="checkbox"
                    />
                    <FieldLabel
                      help={translateLiteral(category.description)}
                      htmlFor={categoryInputId}
                      label={translateLiteral(category.label)}
                    />
                  </div>
                  <div
                    className={`game-dump-category-controls${
                      category.languageOptions ? ' has-language-options' : ''
                    }`}
                  >
                    <span className={`status-pill ${category.isAvailable ? 'status-ready' : 'status-blocked'}`}>
                      {translateLiteral(category.isAvailable ? 'Available' : 'Unavailable')}
                    </span>
                    <div className="path-field game-dump-format-field">
                      <FieldLabel
                        help={t('workflowHelp.gameDump.format')}
                        htmlFor={formatInputId}
                        label={translateLiteral('Format')}
                      />
                      <select
                        aria-label={`${translateLiteral(category.label)} ${translateLiteral('Format')}`}
                        disabled={
                          !category.isAvailable || !state.selected || isLoading || isGenerating
                        }
                        id={formatInputId}
                        onChange={(event) => {
                          invalidateGeneratedState();
                          setSelectionState((current) => ({
                            ...current,
                            [category.id]: {
                              format: event.target.value as GameDumpFormat,
                              languageCodes: resolveLanguageSelection(
                                category,
                                current[category.id]?.languageCodes
                              ),
                              selected: current[category.id]?.selected ?? true
                            }
                          }));
                        }}
                        value={state.format}
                      >
                        {category.formats.map((format) => (
                          <option key={format} value={format}>
                            {formatGameDumpFormat(format, translateLiteral)}
                          </option>
                        ))}
                      </select>
                    </div>
                    {category.languageOptions ? (
                      <div className="path-field game-dump-language-field">
                        <FieldLabel
                          help={t('gameDump.language.help')}
                          htmlFor={languageInputId}
                          label={t('gameDump.language.label')}
                        />
                        <select
                          aria-label={`${translateLiteral(category.label)} ${t(
                            'gameDump.language.label'
                          )}`}
                          data-localization-ignore="true"
                          disabled={
                            !category.isAvailable || !state.selected || isLoading || isGenerating
                          }
                          id={languageInputId}
                          onChange={(event) => {
                            invalidateGeneratedState();
                            const languageCodes =
                              event.target.value === allGameDumpLanguagesValue
                                ? category.languageOptions!.options.map((option) => option.code)
                                : [event.target.value];
                            setSelectionState((current) => ({
                              ...current,
                              [category.id]: {
                                format: current[category.id]?.format ?? category.defaultFormat,
                                languageCodes,
                                selected: current[category.id]?.selected ?? true
                              }
                            }));
                          }}
                          value={formatLanguageSelection(category, state.languageCodes)}
                        >
                          {category.languageOptions.supportsAllLanguages ? (
                            <option value={allGameDumpLanguagesValue}>
                              {t('gameDump.language.all', {
                                count: category.languageOptions.options.length
                              })}
                            </option>
                          ) : null}
                          {category.languageOptions.options.map((option) => (
                            <option key={option.code} value={option.code}>
                              {option.label}
                            </option>
                          ))}
                        </select>
                      </div>
                    ) : null}
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

          <div className="game-dump-step-heading">
            <span aria-hidden="true">3</span>
            <h3>{translateLiteral('Review')}</h3>
          </div>
          <div className="game-dump-review-bar">
            <div>
              <strong>{translateLiteral('Selected categories')}</strong>
              <span>{selectedCount}</span>
            </div>
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
          confirmationCopy={t('gameDump.confirm.replaceOwned')}
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
  confirmationCopy,
  destinationFolder,
  isGenerating,
  onCancel,
  onConfirm,
  translateLiteral
}: {
  categoryCount: number;
  confirmationCopy: string;
  destinationFolder: string;
  isGenerating: boolean;
  onCancel: () => void;
  onConfirm: () => void;
  translateLiteral: (literal: string) => string;
}) {
  const dialogRef = useModalDialog<HTMLElement>({
    canClose: !isGenerating,
    onClose: onCancel
  });

  return (
    <div className="modal-backdrop" role="presentation">
      <section
        aria-labelledby="game-dump-confirm-heading"
        aria-modal="true"
        className="modal-panel"
        ref={dialogRef}
        role="dialog"
        tabIndex={-1}
      >
        <div className="panel-heading">
          <AlertTriangle aria-hidden="true" size={18} />
          <h2 id="game-dump-confirm-heading">{translateLiteral('Generate Dump Files')}</h2>
        </div>
        <p className="modal-copy">
          {confirmationCopy}
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

function resolveLanguageSelection(
  category: GameDumpCategory,
  requestedLanguageCodes?: readonly string[]
) {
  const languageOptions = category.languageOptions;
  if (!languageOptions || languageOptions.options.length === 0) {
    return undefined;
  }

  const supportedCodes = languageOptions.options.map((option) => option.code);
  const supportedCodeSet = new Set(supportedCodes);
  const requested = Array.from(
    new Set((requestedLanguageCodes ?? []).filter((code) => supportedCodeSet.has(code)))
  );
  if (
    languageOptions.supportsAllLanguages &&
    requested.length === supportedCodes.length &&
    supportedCodes.every((code) => requested.includes(code))
  ) {
    return supportedCodes;
  }
  if (requested.length > 0) {
    return [requested[0]!];
  }

  const defaults = Array.from(
    new Set(languageOptions.defaultLanguageCodes.filter((code) => supportedCodeSet.has(code)))
  );
  if (
    languageOptions.supportsAllLanguages &&
    defaults.length === supportedCodes.length &&
    supportedCodes.every((code) => defaults.includes(code))
  ) {
    return supportedCodes;
  }

  return [defaults[0] ?? supportedCodes[0]!];
}

function formatLanguageSelection(
  category: GameDumpCategory,
  requestedLanguageCodes?: readonly string[]
) {
  const resolved = resolveLanguageSelection(category, requestedLanguageCodes);
  if (!category.languageOptions || !resolved || resolved.length === 0) {
    return '';
  }

  if (
    category.languageOptions.supportsAllLanguages &&
    resolved.length === category.languageOptions.options.length
  ) {
    return allGameDumpLanguagesValue;
  }

  return resolved[0]!;
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

function loadRememberedDestination(game: NonNullable<ProjectPaths['selectedGame']>) {
  try {
    const parsed = JSON.parse(localStorage.getItem(destinationStorageKey) ?? '{}') as Record<
      string,
      unknown
    >;
    return typeof parsed[game] === 'string' ? parsed[game] : '';
  } catch {
    return '';
  }
}

function rememberDestination(
  game: NonNullable<ProjectPaths['selectedGame']>,
  destination: string
) {
  try {
    const parsed = JSON.parse(localStorage.getItem(destinationStorageKey) ?? '{}') as Record<
      string,
      unknown
    >;
    if (destination.trim()) {
      parsed[game] = destination;
    } else {
      delete parsed[game];
    }
    localStorage.setItem(destinationStorageKey, JSON.stringify(parsed));
  } catch {
    // Destination memory is an optional convenience; generation still works without it.
  }
}
