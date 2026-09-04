/* SPDX-License-Identifier: GPL-3.0-only */

import {
  AlertTriangle,
  ArrowRight,
  ClipboardCheck,
  Info,
  ListOrdered,
  RefreshCw,
  RotateCcw,
  Save,
  Search,
  X
} from 'lucide-react';
import { type ReactNode, useEffect, useMemo, useRef, useState } from 'react';
import {
  type ApiDiagnostic,
  type EditSession,
  type PokemonDexPlacement,
  type PokemonWorkflow
} from '../../bridge/contracts';
import {
  EditorSessionBar,
  EditorSessionBarActions
} from '../../components/EditorSessionBar';
import { ContextHelp } from '../../components/ContextHelp';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { FieldLabel } from '../../components/FieldLabel';
import { reconcileSourceBackedDraft } from '../../components/localEditorDraftState';
import { SearchableOptionInput } from '../../components/SearchableOptionInput';
import { useCoalescedTextInputState } from '../../components/useCoalescedTextInputState';
import { useModalDialog } from '../../components/useModalDialog';
import { DiagnosticsSection, Metric } from '../../components/workflowPanels';
import { useLocalization } from '../../localization';

type DexKind = PokemonDexPlacement['dexKind'];

type DexMovePreview = {
  destinationDexKind: DexKind;
  destinationDisplayedNumber: number;
  resultingHyperspaceCount: number;
  resultingRegularCount: number;
  shiftedEntryCount: number;
  source: PokemonDexPlacement;
};

type DexResizeMembershipChange = {
  destinationDexKind: DexKind;
  destinationDisplayedNumber: number;
  source: PokemonDexPlacement;
};

type DexResizePreview = {
  changes: DexResizeMembershipChange[];
  currentHyperspaceCount: number;
  currentRegularCount: number;
  proposedHyperspaceCount: number;
  proposedRegularCount: number;
};

const POKEDEX_SPECIES_COUNT = 364;
const MINIMUM_DEX_SIZE = 1;
const MAXIMUM_DEX_SIZE = POKEDEX_SPECIES_COUNT - MINIMUM_DEX_SIZE;
const REGULAR_COUNTER_EXCLUDED_SPECIES_IDS = new Set([720, 721]);

export function ZaDexLayoutSection({
  diagnostics,
  editSession,
  isEditStarting,
  isPokemonUpdating,
  onCancelEditSession,
  onDirtyChange,
  onMovePlacement,
  onOpenChanges,
  onResizeDex,
  onStageMegaSync,
  onStageReturnToVanilla,
  onStartEditSession,
  workflow
}: {
  diagnostics: ApiDiagnostic[];
  editSession: EditSession | null;
  isEditStarting: boolean;
  isPokemonUpdating: boolean;
  onCancelEditSession: (onDiscard?: () => void) => void;
  onDirtyChange: (isDirty: boolean) => void;
  onMovePlacement: (
    sourceSpeciesId: number,
    destinationDexKind: DexKind,
    destinationDisplayedNumber: number
  ) => Promise<boolean>;
  onOpenChanges: () => void;
  onResizeDex: (regularCount: number) => Promise<boolean>;
  onStageMegaSync: () => void;
  onStageReturnToVanilla: () => void;
  onStartEditSession: () => void;
  workflow: PokemonWorkflow | null;
}) {
  const { translateLiteral } = useLocalization();
  const dexEditor = workflow?.dexEditor ?? null;
  const placements = useMemo(
    () =>
      [...(dexEditor?.placements ?? [])].sort(
        (left, right) =>
          compareDexKinds(left.dexKind, right.dexKind) ||
          left.displayedNumber - right.displayedNumber ||
          left.speciesId - right.speciesId
    ),
    [dexEditor?.placements]
  );
  const excludedRegularPlacements = placements.filter(
    (placement) =>
      placement.dexKind === 'regular' &&
      REGULAR_COUNTER_EXCLUDED_SPECIES_IDS.has(placement.speciesId)
  );
  const expectedFullRegularCounter = Math.max(
    0,
    (dexEditor?.regularCount ?? 0) - excludedRegularPlacements.length
  );
  const [searchText, setSearchText] = useCoalescedTextInputState();
  const [isMegaSyncConfirmationOpen, setIsMegaSyncConfirmationOpen] = useState(false);
  const [selectedSpeciesId, setSelectedSpeciesId] = useState<number | null>(null);
  const [destinationDexKind, setDestinationDexKind] = useState<DexKind>('regular');
  const [destinationNumberDraft, setDestinationNumberDraft] = useState('');
  const [moveDraftBaseKey, setMoveDraftBaseKey] = useState('');
  const [regularSizeDraft, setRegularSizeDraft] = useState('');
  const [hyperspaceSizeDraft, setHyperspaceSizeDraft] = useState('');
  const [resizeDraftBaseKey, setResizeDraftBaseKey] = useState('');
  const resizeDraftRef = useRef(createDexSizeDraft('', ''));
  const previousResizeSourceRef = useRef<ReturnType<typeof createDexSizeDraft> | null>(
    null
  );
  const moveDraftRef = useRef(createDexMoveDraft('regular', ''));
  const previousMoveSourceRef = useRef<{
    speciesId: number;
    draft: ReturnType<typeof createDexMoveDraft>;
  } | null>(null);

  resizeDraftRef.current = createDexSizeDraft(
    regularSizeDraft,
    hyperspaceSizeDraft
  );
  moveDraftRef.current = createDexMoveDraft(
    destinationDexKind,
    destinationNumberDraft
  );

  useEffect(() => {
    if (!dexEditor) {
      setRegularSizeDraft('');
      setHyperspaceSizeDraft('');
      setResizeDraftBaseKey('');
      previousResizeSourceRef.current = null;
      return;
    }

    const nextSource = createDexSizeDraft(
      dexEditor.regularCount.toString(),
      dexEditor.hyperspaceCount.toString()
    );
    const previousSource = previousResizeSourceRef.current;
    const nextDraft =
      previousSource === null
        ? nextSource
        : reconcileSourceBackedDraft(
            resizeDraftRef.current,
            previousSource,
            nextSource,
            areDexSizeDraftsEqual
          );

    setRegularSizeDraft(nextDraft.regular);
    setHyperspaceSizeDraft(nextDraft.hyperspace);
    setResizeDraftBaseKey(
      createDexSizeKey(dexEditor.regularCount, dexEditor.hyperspaceCount)
    );
    previousResizeSourceRef.current = nextSource;
  }, [dexEditor?.hyperspaceCount, dexEditor?.regularCount]);

  useEffect(() => {
    if (
      selectedSpeciesId !== null &&
      placements.some((placement) => placement.speciesId === selectedSpeciesId)
    ) {
      return;
    }

    setSelectedSpeciesId(placements[0]?.speciesId ?? null);
  }, [placements, selectedSpeciesId]);

  const selectedPlacement =
    placements.find((placement) => placement.speciesId === selectedSpeciesId) ?? null;

  useEffect(() => {
    if (!selectedPlacement) {
      setDestinationDexKind('regular');
      setDestinationNumberDraft('');
      setMoveDraftBaseKey('');
      previousMoveSourceRef.current = null;
      return;
    }

    const nextSource = createDexMoveDraft(
      selectedPlacement.dexKind,
      selectedPlacement.displayedNumber.toString()
    );
    const previousSource = previousMoveSourceRef.current;
    const nextDraft =
      previousSource === null ||
      previousSource.speciesId !== selectedPlacement.speciesId
        ? nextSource
        : reconcileSourceBackedDraft(
            moveDraftRef.current,
            previousSource.draft,
            nextSource,
            areDexMoveDraftsEqual
          );

    setDestinationDexKind(nextDraft.dexKind);
    setDestinationNumberDraft(nextDraft.displayedNumber);
    setMoveDraftBaseKey(createDexMoveKey(selectedPlacement));
    previousMoveSourceRef.current = {
      speciesId: selectedPlacement.speciesId,
      draft: nextSource
    };
  }, [
    selectedPlacement?.dexKind,
    selectedPlacement?.displayedNumber,
    selectedPlacement?.speciesId
  ]);

  const normalizedSearch = searchText.trim().toLocaleLowerCase();
  const filteredPlacements = useMemo(
    () =>
      placements.filter((placement) => {
        if (normalizedSearch.length === 0) {
          return true;
        }

        const searchableValues = [
          placement.label,
          placement.speciesId.toString(),
          placement.displayedNumber.toString(),
          formatDexNumber(placement.displayedNumber),
          placement.dexKind
        ];
        return searchableValues.some((value) =>
          value.toLocaleLowerCase().includes(normalizedSearch)
        );
      }),
    [normalizedSearch, placements]
  );
  const regularPlacements = filteredPlacements.filter(
    (placement) => placement.dexKind === 'regular'
  );
  const hyperspacePlacements = filteredPlacements.filter(
    (placement) => placement.dexKind === 'hyperspace'
  );
  const maximumDestination = getMaximumDestination(
    selectedPlacement,
    destinationDexKind,
    dexEditor?.regularCount ?? 0,
    dexEditor?.hyperspaceCount ?? 0
  );
  const destinationDisplayedNumber = parseDestinationNumber(
    destinationNumberDraft,
    maximumDestination
  );
  const preview =
    selectedPlacement && dexEditor && destinationDisplayedNumber !== null
      ? createDexMovePreview(
          selectedPlacement,
          destinationDexKind,
          destinationDisplayedNumber,
          dexEditor.regularCount,
          dexEditor.hyperspaceCount
        )
      : null;
  const proposedRegularCount = parseDexSize(regularSizeDraft);
  const proposedHyperspaceCount = parseDexSize(hyperspaceSizeDraft);
  const currentResizeBaseKey = dexEditor
    ? createDexSizeKey(dexEditor.regularCount, dexEditor.hyperspaceCount)
    : '';
  const resizeDraftIsDirty =
    dexEditor !== null &&
    resizeDraftBaseKey === currentResizeBaseKey &&
    (proposedRegularCount !== null && proposedHyperspaceCount !== null
      ? proposedRegularCount !== dexEditor.regularCount ||
        proposedHyperspaceCount !== dexEditor.hyperspaceCount
      : regularSizeDraft !== dexEditor.regularCount.toString() ||
        hyperspaceSizeDraft !== dexEditor.hyperspaceCount.toString());
  const resizePreview =
    dexEditor &&
    proposedRegularCount !== null &&
    proposedHyperspaceCount !== null &&
    proposedRegularCount + proposedHyperspaceCount === POKEDEX_SPECIES_COUNT
      ? createDexResizePreview(
          placements,
          dexEditor.regularCount,
          dexEditor.hyperspaceCount,
          proposedRegularCount,
          proposedHyperspaceCount
        )
      : null;
  const resizeIsNoOp =
    resizePreview !== null &&
    resizePreview.currentRegularCount === resizePreview.proposedRegularCount;
  const isNoOp =
    preview !== null &&
    preview.source.dexKind === preview.destinationDexKind &&
    preview.source.displayedNumber === preview.destinationDisplayedNumber;
  const currentMoveBaseKey = selectedPlacement
    ? createDexMoveKey(selectedPlacement)
    : '';
  const moveDraftIsDirty =
    selectedPlacement !== null &&
    moveDraftBaseKey === currentMoveBaseKey &&
    (destinationDexKind !== selectedPlacement.dexKind ||
      (destinationDisplayedNumber !== null
        ? destinationDisplayedNumber !== selectedPlacement.displayedNumber
        : destinationNumberDraft !== selectedPlacement.displayedNumber.toString()));
  const hasLocalDraft = resizeDraftIsDirty || moveDraftIsDirty;
  const requiresAdvancedLayout =
    preview !== null && preview.source.dexKind !== preview.destinationDexKind;
  const workflowAvailable = workflow?.summary.availability === 'available';
  const hasActiveEditSession = editSession !== null;
  const hasStagedChange = (editSession?.pendingEdits.length ?? 0) > 0;
  const sessionActionChangedCount =
    (editSession?.pendingEdits.length ?? 0) + (hasLocalDraft ? 1 : 0);
  const isWorkflowActionBusy = isEditStarting || isPokemonUpdating;
  const canStage =
    workflowAvailable &&
    dexEditor?.canEdit === true &&
    (!requiresAdvancedLayout || dexEditor.canEditAdvanced) &&
    editSession !== null &&
    !isWorkflowActionBusy &&
    !resizeDraftIsDirty &&
    preview !== null &&
    !isNoOp;
  const canStageResize =
    workflowAvailable &&
    dexEditor?.canEdit === true &&
    dexEditor.canEditAdvanced &&
    editSession !== null &&
    !isWorkflowActionBusy &&
    !moveDraftIsDirty &&
    resizePreview !== null &&
    !resizeIsNoOp;
  const canStageSessionDraft = canStageResize || canStage;
  const returnToVanillaNeedsEditSession =
    workflowAvailable &&
    dexEditor?.canReturnToVanilla === true &&
    !dexEditor.isVanillaLayout &&
    !hasActiveEditSession &&
    !hasLocalDraft &&
    !isWorkflowActionBusy;
  const canReturnToVanilla =
    workflowAvailable &&
    dexEditor?.canReturnToVanilla === true &&
    !dexEditor.isVanillaLayout &&
    hasActiveEditSession &&
    !hasLocalDraft &&
    !isWorkflowActionBusy;
  const canStageMegaSync =
    workflowAvailable &&
    dexEditor?.canSyncMegasToRegular === true &&
    hasActiveEditSession &&
    !hasLocalDraft &&
    !isWorkflowActionBusy;
  const canOpenChanges =
    hasStagedChange && !hasLocalDraft && !isWorkflowActionBusy;
  usePublishCommonEditorError({
    domain: 'workflow.pokemon.dexLayout',
    field: 'sizes',
    message:
      resizeDraftIsDirty && resizePreview === null
        ? translateLiteral(
            'Enter a whole number from 1 to 363. The linked size is calculated automatically.'
          )
        : null
  });
  usePublishCommonEditorError({
    domain: 'workflow.pokemon.dexLayout',
    field: 'destination',
    message:
      moveDraftIsDirty && preview === null
        ? translateLiteral('Choose a valid destination number.')
        : null
  });

  useEffect(() => {
    onDirtyChange(hasLocalDraft);
  }, [hasLocalDraft, onDirtyChange]);

  const updateSizeDraft = (dexKind: DexKind, value: string) => {
    if (!hasActiveEditSession) {
      return;
    }

    const parsed = parseDexSize(value);
    const dependentValue =
      parsed === null ? '' : (POKEDEX_SPECIES_COUNT - parsed).toString();

    if (dexKind === 'regular') {
      setRegularSizeDraft(value);
      setHyperspaceSizeDraft(dependentValue);
      return;
    }

    setHyperspaceSizeDraft(value);
    setRegularSizeDraft(dependentValue);
  };

  const discardLocalDrafts = () => {
    if (dexEditor) {
      setRegularSizeDraft(dexEditor.regularCount.toString());
      setHyperspaceSizeDraft(dexEditor.hyperspaceCount.toString());
      setResizeDraftBaseKey(
        createDexSizeKey(dexEditor.regularCount, dexEditor.hyperspaceCount)
      );
    } else {
      setRegularSizeDraft('');
      setHyperspaceSizeDraft('');
      setResizeDraftBaseKey('');
    }

    if (selectedPlacement) {
      setDestinationDexKind(selectedPlacement.dexKind);
      setDestinationNumberDraft(selectedPlacement.displayedNumber.toString());
      setMoveDraftBaseKey(createDexMoveKey(selectedPlacement));
    } else {
      setDestinationDexKind('regular');
      setDestinationNumberDraft('');
      setMoveDraftBaseKey('');
    }
  };

  const stageLocalDraft = async () => {
    if (canStageResize && resizePreview && !resizeIsNoOp) {
      await onResizeDex(resizePreview.proposedRegularCount);
      return;
    }

    if (canStage && preview && !isNoOp) {
      await onMovePlacement(
        preview.source.speciesId,
        preview.destinationDexKind,
        preview.destinationDisplayedNumber
      );
    }
  };

  return (
    <>
      <section
        aria-labelledby="za-dex-layout-heading"
        className="panel wide-panel za-dex-layout-section"
      >
      <div className="panel-heading">
        <ListOrdered aria-hidden="true" size={18} />
        <h2 id="za-dex-layout-heading">
          {translateLiteral('Dex Layout')}
        </h2>
      </div>

      <EditorSessionBar
        canEdit={workflowAvailable && dexEditor?.canEdit === true}
        isEditing={hasActiveEditSession}
        isStarting={isEditStarting}
        label="Dex Layout"
        onStart={onStartEditSession}
        readOnlyReason={dexEditor?.blockedReason}
      />

      {editSession ? (
        <EditorSessionBarActions>
          <button
            aria-busy={isPokemonUpdating || undefined}
            className="primary-button"
            disabled={!canStageSessionDraft}
            onClick={stageLocalDraft}
            type="button"
          >
            <Save aria-hidden="true" size={16} />
            <span>
              {translateLiteral(isPokemonUpdating ? 'Staging' : 'Stage')}
            </span>
          </button>
          <button
            className="danger-button"
            disabled={isWorkflowActionBusy}
            onClick={() => onCancelEditSession(discardLocalDrafts)}
            type="button"
          >
            <X aria-hidden="true" size={16} />
            <span>{translateLiteral('Cancel')}</span>
          </button>
          <span className="draft-action-summary">
            <span data-localization-ignore="true">
              {sessionActionChangedCount.toLocaleString()}
            </span>{' '}
            {translateLiteral('Pending changes')}
          </span>
        </EditorSessionBarActions>
      ) : null}

      <p className="za-dex-layout-intro">
        {translateLiteral(
          'Move one species to an exact number and shift the entries between. The occupied-slot Swap control remains available in the Pokemon editor.'
        )}
      </p>
      <p className="za-dex-layout-intro">
        {translateLiteral(
          'Dex Layout changes are staged here. Review and output them from Changes.'
        )}
      </p>

      <div className="za-dex-layout-metrics">
        <Metric
          label="Regular Dex entries"
          value={dexEditor?.regularCount.toLocaleString() ?? '0'}
          valueIsRaw
        />
        <Metric
          label="Hyperspace Dex entries"
          value={dexEditor?.hyperspaceCount.toLocaleString() ?? '0'}
          valueIsRaw
        />
        <Metric
          label="Expected full Seen/Caught"
          value={expectedFullRegularCounter.toLocaleString()}
          valueIsRaw
        />
        <Metric
          label="Executable Regular count"
          value={dexEditor?.executableRegularCount?.toLocaleString() ?? 'Not available'}
          valueIsRaw={dexEditor?.executableRegularCount != null}
        />
        <Metric
          label="Build ID"
          value={dexEditor?.executableBuildId ?? 'Not available'}
          valueIsRaw={dexEditor?.executableBuildId != null}
        />
      </div>

      {excludedRegularPlacements.length > 0 ? (
        <div className="za-dex-layout-warning" role="note">
          <Info aria-hidden="true" size={17} />
          <span>
            {translateLiteral(
              'The game excludes these visible Regular Dex species from Number Seen and Number Caught:'
            )}{' '}
            <span data-localization-ignore="true">
              {excludedRegularPlacements
                .map((placement) => placement.label)
                .join(', ')}
            </span>
          </span>
        </div>
      ) : null}

        {!workflow ? (
        <p className="empty-copy">
          {translateLiteral(
            'Open Dex Layout to inspect the Regular and Hyperspace Pokédexes.'
          )}
        </p>
      ) : !dexEditor ? (
        <DexLayoutWarning>
          {translateLiteral(
            'Advanced layout editing is unavailable for the loaded game data.'
          )}
        </DexLayoutWarning>
      ) : (
        <>
          {!dexEditor.canEdit ? (
            <DexLayoutWarning>
              {translateLiteral(
                dexEditor.blockedReason ??
                  'Pokédex placement is unavailable for the loaded game data.'
              )}
            </DexLayoutWarning>
          ) : !dexEditor.canEditAdvanced ? (
            <DexLayoutWarning>
              <span>
                {translateLiteral(
                  dexEditor.advancedBlockedReason ??
                    'Cross-Dex moves are unavailable for the loaded game data.'
                )}{' '}
                {translateLiteral(
                  'Same-Dex exact moves remain available, and occupied-slot Swap remains available in the Pokemon editor.'
                )}
              </span>
            </DexLayoutWarning>
          ) : null}

          <label className="za-dex-layout-search">
            <Search aria-hidden="true" size={17} />
            <input
              aria-label={translateLiteral('Search Pokédex entries')}
              onChange={(event) => setSearchText(event.currentTarget.value)}
              placeholder={translateLiteral('Search by number, species ID, or name')}
              type="search"
              value={searchText}
            />
          </label>

          <div className="za-dex-layout-workspace">
            <div className="za-dex-layout-dexes">
              <div className="za-dex-layout-dex-column">
                <DexSizeControl
                  dexKind="regular"
                  disabled={
                    !hasActiveEditSession ||
                    !dexEditor.canEditAdvanced ||
                    moveDraftIsDirty
                  }
                  isValid={proposedRegularCount !== null}
                  onChange={(value) => updateSizeDraft('regular', value)}
                  value={regularSizeDraft}
                />
                <DexPlacementList
                  dexKind="regular"
                  placements={regularPlacements}
                  selectedSpeciesId={selectedSpeciesId}
                  selectionLocked={moveDraftIsDirty}
                  totalCount={dexEditor.regularCount}
                  onSelect={setSelectedSpeciesId}
                />
              </div>
              <div className="za-dex-layout-dex-column">
                <DexSizeControl
                  dexKind="hyperspace"
                  disabled={
                    !hasActiveEditSession ||
                    !dexEditor.canEditAdvanced ||
                    moveDraftIsDirty
                  }
                  isValid={proposedHyperspaceCount !== null}
                  onChange={(value) => updateSizeDraft('hyperspace', value)}
                  value={hyperspaceSizeDraft}
                />
                <DexPlacementList
                  dexKind="hyperspace"
                  placements={hyperspacePlacements}
                  selectedSpeciesId={selectedSpeciesId}
                  selectionLocked={moveDraftIsDirty}
                  totalCount={dexEditor.hyperspaceCount}
                  onSelect={setSelectedSpeciesId}
                />
              </div>
            </div>

            <div className="za-dex-layout-inspectors">
              <section
                aria-labelledby="za-dex-layout-resize-heading"
                className="za-dex-layout-inspector za-dex-layout-resize-inspector"
              >
                <div className="za-dex-layout-inspector-heading">
                  <div>
                    <div className="editable-field-label-row">
                      <h3 id="za-dex-layout-resize-heading">
                        {translateLiteral('Resize Pokédexes')}
                      </h3>
                      <ContextHelp label={translateLiteral('Resize Pokédexes')}>
                        {translateLiteral(
                          'Both sizes are linked and must total 364. Each Pokédex can contain 1 to 363 entries.'
                        )}
                      </ContextHelp>
                    </div>
                  </div>
                </div>

                {resizePreview ? (
                  <DexResizePreviewPanel preview={resizePreview} />
                ) : (
                  <p className="za-dex-layout-validation" role="alert">
                    {translateLiteral(
                      'Enter a whole number from 1 to 363. The linked size is calculated automatically.'
                    )}
                  </p>
                )}

                {resizeIsNoOp ? (
                  <p className="za-dex-layout-validation">
                    {translateLiteral('These sizes match the current Pokédex layout.')}
                  </p>
                ) : null}
                {!dexEditor.canEditAdvanced ? (
                  <p className="za-dex-layout-validation" role="alert">
                    {translateLiteral(
                      dexEditor.advancedBlockedReason ??
                        'Pokédex resizing is unavailable for the loaded game data.'
                    )}
                  </p>
                ) : null}

                {editSession === null && dexEditor.canEditAdvanced ? (
                  <div className="za-dex-layout-actions">
                    <span className="draft-action-summary">
                      {translateLiteral('Start editing to stage a Pokédex resize.')}
                    </span>
                  </div>
                ) : null}
              </section>

              <aside
                aria-labelledby="za-dex-layout-move-heading"
                className="za-dex-layout-inspector"
              >
              <div className="za-dex-layout-inspector-heading">
                <div>
                  <div className="editable-field-label-row">
                    <h3 id="za-dex-layout-move-heading">
                      {translateLiteral('Move selected entry')}
                    </h3>
                    <ContextHelp label={translateLiteral('Move selected entry')}>
                      {translateLiteral(
                        'Choose the destination Pokédex and exact displayed number.'
                      )}
                    </ContextHelp>
                  </div>
                </div>
              </div>

              {selectedPlacement ? (
                <>
                  <dl className="za-dex-layout-selection">
                    <div>
                      <dt>{translateLiteral('Selected entry')}</dt>
                      <dd data-localization-ignore="true">
                        {selectedPlacement.label}
                      </dd>
                    </div>
                    <div>
                      <dt>{translateLiteral('Current placement')}</dt>
                      <dd>
                        {translateLiteral(formatDexKind(selectedPlacement.dexKind))}{' '}
                        <span data-localization-ignore="true">
                          #{formatDexNumber(selectedPlacement.displayedNumber)}
                        </span>
                      </dd>
                    </div>
                  </dl>

                  <div className="za-dex-layout-destination">
                    <div className="path-field">
                      <FieldLabel
                        help={translateLiteral(
                          'Choose the destination Pokédex and exact displayed number.'
                        )}
                        htmlFor="za-dex-layout-destination-dex"
                        label={translateLiteral('Destination Pokédex')}
                      />
                      <SearchableOptionInput
                        ariaLabel={translateLiteral('Destination Pokédex')}
                        data-km-source-site="za-dex-layout-destination-dex"
                        disabled={
                          !hasActiveEditSession ||
                          !dexEditor.canEdit ||
                          resizeDraftIsDirty
                        }
                        id="za-dex-layout-destination-dex"
                        isFiniteCatalog
                        localizeOptions={false}
                        onChange={(value) => {
                          if (!hasActiveEditSession) {
                            return;
                          }

                          const nextDexKind =
                            value === 'hyperspace'
                              ? 'hyperspace'
                              : 'regular';
                          setDestinationDexKind(nextDexKind);
                          const nextMaximum = getMaximumDestination(
                            selectedPlacement,
                            nextDexKind,
                            dexEditor.regularCount,
                            dexEditor.hyperspaceCount
                          );
                          setDestinationNumberDraft(
                            Math.min(selectedPlacement.displayedNumber, nextMaximum).toString()
                          );
                        }}
                        options={[
                          {
                            label: `${translateLiteral('Regular Dex')} (${dexEditor.regularCount})`,
                            value: 'regular'
                          },
                          {
                            label: `${translateLiteral('Hyperspace Dex')} (${dexEditor.hyperspaceCount})`,
                            value: 'hyperspace'
                          }
                        ]}
                        value={destinationDexKind}
                      />
                    </div>

                    <div className="path-field">
                      <FieldLabel
                        help={translateLiteral(
                          'The selected species moves to the requested number and intervening entries shift automatically.'
                        )}
                        htmlFor="za-dex-layout-destination-number"
                        label={translateLiteral('Destination number')}
                      />
                      <input
                        aria-invalid={
                          destinationNumberDraft.length > 0 &&
                          destinationDisplayedNumber === null
                            ? 'true'
                            : undefined
                        }
                        disabled={
                          !hasActiveEditSession ||
                          !dexEditor.canEdit ||
                          resizeDraftIsDirty
                        }
                        id="za-dex-layout-destination-number"
                        inputMode="numeric"
                        max={maximumDestination}
                        min={1}
                        onChange={(event) => {
                          if (hasActiveEditSession) {
                            setDestinationNumberDraft(event.currentTarget.value);
                          }
                        }}
                        step={1}
                        type="number"
                        value={destinationNumberDraft}
                      />
                      <small>
                        {translateLiteral('Destination range')}:{' '}
                        <span data-localization-ignore="true">
                          1-{maximumDestination}
                        </span>
                      </small>
                    </div>
                  </div>

                  {preview ? (
                    <DexMovePreviewPanel preview={preview} />
                  ) : (
                    <p className="za-dex-layout-validation" role="alert">
                      {translateLiteral('Choose a valid destination number.')}
                    </p>
                  )}

                  {isNoOp ? (
                    <p className="za-dex-layout-validation">
                      {translateLiteral('This move keeps the current placement.')}
                    </p>
                  ) : null}
                  {requiresAdvancedLayout && !dexEditor.canEditAdvanced ? (
                    <p className="za-dex-layout-validation" role="alert">
                      {translateLiteral(
                        dexEditor.advancedBlockedReason ??
                          'Cross-Dex moves are unavailable for the loaded game data.'
                      )}
                    </p>
                  ) : null}

                  {editSession === null && dexEditor.canEdit ? (
                    <div className="za-dex-layout-actions">
                      <span className="draft-action-summary">
                        {translateLiteral('Start editing to stage a Pokédex move.')}
                      </span>
                    </div>
                  ) : null}
                </>
              ) : (
                <p className="empty-copy">
                  {translateLiteral('Select a Pokédex entry to move.')}
                </p>
              )}
              </aside>
            </div>
          </div>
        </>
        )}

        {workflow && dexEditor ? (
          <div className="type-chart-actions za-dex-layout-workflow-actions">
            {dexEditor.canSyncMegasToRegular ? (
              <button
                className="secondary-button"
                disabled={!canStageMegaSync}
                onClick={() => setIsMegaSyncConfirmationOpen(true)}
                title={translateLiteral(
                  hasActiveEditSession
                    ? 'Stage a repair that makes Mega Pokédex membership follow the current Regular and Hyperspace Pokédexes.'
                    : 'Start editing to stage Mega Pokédex synchronization.'
                )}
                type="button"
              >
                <RefreshCw aria-hidden="true" size={16} />
                <span>{translateLiteral('Sync Megas to Regular')}</span>
              </button>
            ) : null}
            <button
              aria-busy={isPokemonUpdating || undefined}
              className="danger-button"
              disabled={!canReturnToVanilla}
              onClick={onStageReturnToVanilla}
              title={translateLiteral(
                returnToVanillaNeedsEditSession
                  ? 'Start editing to stage a Pokédex resize.'
                  : dexEditor.returnToVanillaBlockedReason ??
                    'Restore the verified vanilla Pokédex sizes and species order.'
              )}
              type="button"
            >
              <RotateCcw aria-hidden="true" size={16} />
              <span>
                {translateLiteral(
                  isPokemonUpdating ? 'Staging' : 'Return to Vanilla'
                )}
              </span>
            </button>
            <button
              className="secondary-button"
              disabled={!canOpenChanges}
              onClick={onOpenChanges}
              type="button"
            >
              <ClipboardCheck aria-hidden="true" size={16} />
              <span>{translateLiteral('Open Changes')}</span>
            </button>
          </div>
        ) : null}
      </section>

      <DiagnosticsSection
        diagnostics={[...(workflow?.diagnostics ?? []), ...diagnostics]}
      />
      {isMegaSyncConfirmationOpen ? (
        <MegaDexSyncConfirmationModal
          onCancel={() => setIsMegaSyncConfirmationOpen(false)}
          onConfirm={() => {
            setIsMegaSyncConfirmationOpen(false);
            onStageMegaSync();
          }}
        />
      ) : null}
    </>
  );
}

function MegaDexSyncConfirmationModal({
  onCancel,
  onConfirm
}: {
  onCancel: () => void;
  onConfirm: () => void;
}) {
  const { translateLiteral } = useLocalization();
  const dialogRef = useModalDialog<HTMLElement>({ onClose: onCancel });

  return (
    <div className="modal-backdrop" role="presentation">
      <section
        aria-labelledby="mega-dex-sync-confirmation-heading"
        aria-modal="true"
        className="modal-panel"
        ref={dialogRef}
        role="dialog"
        tabIndex={-1}
      >
        <div className="panel-heading">
          <RefreshCw aria-hidden="true" size={18} />
          <h2 id="mega-dex-sync-confirmation-heading">
            {translateLiteral('Sync Megas to Regular')}
          </h2>
        </div>
        <p className="modal-copy">
          {translateLiteral(
            "This will update every Mega Evolution's Pokédex membership to match its base species in the current Regular and Hyperspace Pokédexes."
          )}
        </p>
        <p className="modal-copy modal-copy-muted">
          {translateLiteral(
            'It will not change species order, Pokédex sizes, personal data, or Regular/Hyperspace membership. The Mega Pokédex change will be staged for review in Changes; no files are written until you approve the change plan.'
          )}
        </p>
        <div className="modal-actions">
          <button className="secondary-button" onClick={onCancel} type="button">
            <X aria-hidden="true" size={16} />
            <span>{translateLiteral('Cancel')}</span>
          </button>
          <button className="primary-button" onClick={onConfirm} type="button">
            <RefreshCw aria-hidden="true" size={16} />
            <span>{translateLiteral('Sync Megas to Regular')}</span>
          </button>
        </div>
      </section>
    </div>
  );
}

function DexSizeControl({
  dexKind,
  disabled,
  isValid,
  onChange,
  value
}: {
  dexKind: DexKind;
  disabled: boolean;
  isValid: boolean;
  onChange: (value: string) => void;
  value: string;
}) {
  const { translateLiteral } = useLocalization();
  const inputId = `za-dex-layout-${dexKind}-size`;

  return (
    <div className="path-field za-dex-layout-size-control">
      <FieldLabel
        help={translateLiteral(
          'Both sizes are linked and must total 364. Each Pokédex can contain 1 to 363 entries.'
        )}
        htmlFor={inputId}
        label={translateLiteral(
          dexKind === 'regular' ? 'Regular Dex Size' : 'Hyperspace Dex Size'
        )}
      />
      <input
        aria-invalid={!isValid ? 'true' : undefined}
        disabled={disabled}
        id={inputId}
        inputMode="numeric"
        max={MAXIMUM_DEX_SIZE}
        min={MINIMUM_DEX_SIZE}
        onChange={(event) => onChange(event.currentTarget.value)}
        step={1}
        type="number"
        value={value}
      />
    </div>
  );
}

function DexPlacementList({
  dexKind,
  onSelect,
  placements,
  selectedSpeciesId,
  selectionLocked,
  totalCount
}: {
  dexKind: DexKind;
  onSelect: (speciesId: number) => void;
  placements: PokemonDexPlacement[];
  selectedSpeciesId: number | null;
  selectionLocked: boolean;
  totalCount: number;
}) {
  const { translateLiteral } = useLocalization();
  const headingId = `za-dex-layout-${dexKind}-heading`;

  return (
    <section aria-labelledby={headingId} className="za-dex-layout-dex">
      <div className="za-dex-layout-dex-heading">
        <h3 id={headingId}>{translateLiteral(formatDexKind(dexKind))}</h3>
        <span data-localization-ignore="true">
          {placements.length.toLocaleString()} / {totalCount.toLocaleString()}
        </span>
      </div>
      <div className="za-dex-layout-list">
        {placements.length > 0 ? (
          placements.map((placement) => {
            const isSelected = placement.speciesId === selectedSpeciesId;
            return (
              <button
                aria-pressed={isSelected}
                className={`za-dex-layout-row${isSelected ? ' is-selected' : ''}`}
                disabled={selectionLocked && !isSelected}
                key={placement.speciesId}
                onClick={() => onSelect(placement.speciesId)}
                type="button"
              >
                <span className="za-dex-layout-number" data-localization-ignore="true">
                  #{formatDexNumber(placement.displayedNumber)}
                </span>
                <span className="za-dex-layout-name" data-localization-ignore="true">
                  {placement.label}
                </span>
                <span className="za-dex-layout-species">
                  {translateLiteral('Species ID')}{' '}
                  <span data-localization-ignore="true">{placement.speciesId}</span>
                </span>
              </button>
            );
          })
        ) : (
          <p className="za-dex-layout-empty">
            {translateLiteral('No Pokédex entries match this search.')}
          </p>
        )}
      </div>
    </section>
  );
}

function DexResizePreviewPanel({ preview }: { preview: DexResizePreview }) {
  const { translateLiteral } = useLocalization();

  return (
    <div aria-live="polite" className="za-dex-layout-preview za-dex-layout-resize-preview">
      <strong>{translateLiteral('Resize preview')}</strong>
      <dl className="za-dex-layout-resize-counts">
        <div>
          <dt>{translateLiteral('Regular Dex')}</dt>
          <dd>
            <span>
              <small>{translateLiteral('Current size')}</small>
              <span data-localization-ignore="true">
                {preview.currentRegularCount.toLocaleString()}
              </span>
            </span>
            <span>
              <small>{translateLiteral('Proposed size')}</small>
              <span data-localization-ignore="true">
                {preview.proposedRegularCount.toLocaleString()}
              </span>
            </span>
          </dd>
        </div>
        <div>
          <dt>{translateLiteral('Hyperspace Dex')}</dt>
          <dd>
            <span>
              <small>{translateLiteral('Current size')}</small>
              <span data-localization-ignore="true">
                {preview.currentHyperspaceCount.toLocaleString()}
              </span>
            </span>
            <span>
              <small>{translateLiteral('Proposed size')}</small>
              <span data-localization-ignore="true">
                {preview.proposedHyperspaceCount.toLocaleString()}
              </span>
            </span>
          </dd>
        </div>
      </dl>

      {preview.changes.length > 0 ? (
        <>
          <p>
            <span data-localization-ignore="true">
              {preview.changes.length.toLocaleString()}
            </span>{' '}
            {translateLiteral(
              'entries change Pokédex membership. Global species order is preserved.'
            )}
          </p>
          <div className="za-dex-layout-membership-preview">
            <strong>{translateLiteral('Entries changing membership')}</strong>
            <ul>
              {preview.changes.map((change) => (
                <li key={change.source.speciesId}>
                  <span data-localization-ignore="true">{change.source.label}</span>
                  <span>
                    {translateLiteral(formatDexKind(change.source.dexKind))}{' '}
                    <span data-localization-ignore="true">
                      #{formatDexNumber(change.source.displayedNumber)}
                    </span>{' '}
                    {translateLiteral('becomes')}{' '}
                    {translateLiteral(formatDexKind(change.destinationDexKind))}{' '}
                    <span data-localization-ignore="true">
                      #{formatDexNumber(change.destinationDisplayedNumber)}
                    </span>
                  </span>
                </li>
              ))}
            </ul>
          </div>
          <p>
            {translateLiteral(
              'Entries that stay in Hyperspace are renumbered automatically after the boundary moves.'
            )}
          </p>
          <p className="za-dex-layout-preview-warning">
            <AlertTriangle aria-hidden="true" size={15} />
            <span>
              {translateLiteral(
                'Resizing updates the verified executable Regular Dex boundary. The output choice controls RomFS packaging only.'
              )}
            </span>
          </p>
        </>
      ) : (
        <p>{translateLiteral('No entries change Pokédex membership.')}</p>
      )}
    </div>
  );
}

function DexMovePreviewPanel({ preview }: { preview: DexMovePreview }) {
  const { translateLiteral } = useLocalization();

  return (
    <div aria-live="polite" className="za-dex-layout-preview">
      <strong>{translateLiteral('Move preview')}</strong>
      <div className="za-dex-layout-preview-route">
        <span>
          {translateLiteral(formatDexKind(preview.source.dexKind))}{' '}
          <span data-localization-ignore="true">
            #{formatDexNumber(preview.source.displayedNumber)}
          </span>
        </span>
        <ArrowRight aria-hidden="true" size={16} />
        <span>
          {translateLiteral(formatDexKind(preview.destinationDexKind))}{' '}
          <span data-localization-ignore="true">
            #{formatDexNumber(preview.destinationDisplayedNumber)}
          </span>
        </span>
      </div>
      <p>
        {translateLiteral(
          'The selected species moves to the requested number and intervening entries shift automatically.'
        )}
      </p>
      {preview.source.dexKind !== preview.destinationDexKind ? (
        <p className="za-dex-layout-preview-warning">
          <AlertTriangle aria-hidden="true" size={15} />
          <span>
            {translateLiteral(
              'Cross-Dex moves update the verified executable Regular Dex boundary. The output choice controls RomFS packaging only.'
            )}
          </span>
        </p>
      ) : null}
      <dl>
        <div>
          <dt>{translateLiteral('Entries shifted')}</dt>
          <dd data-localization-ignore="true">
            {preview.shiftedEntryCount.toLocaleString()}
          </dd>
        </div>
        <div>
          <dt>{translateLiteral('Regular Dex')}</dt>
          <dd data-localization-ignore="true">
            {preview.resultingRegularCount.toLocaleString()}
          </dd>
        </div>
        <div>
          <dt>{translateLiteral('Hyperspace Dex')}</dt>
          <dd data-localization-ignore="true">
            {preview.resultingHyperspaceCount.toLocaleString()}
          </dd>
        </div>
      </dl>
    </div>
  );
}

function DexLayoutWarning({ children }: { children: ReactNode }) {
  return (
    <div className="za-dex-layout-warning" role="note">
      <AlertTriangle aria-hidden="true" size={17} />
      <span>{children}</span>
    </div>
  );
}

function createDexMovePreview(
  source: PokemonDexPlacement,
  destinationDexKind: DexKind,
  destinationDisplayedNumber: number,
  regularCount: number,
  hyperspaceCount: number
): DexMovePreview {
  const isCrossDexMove = source.dexKind !== destinationDexKind;
  const sourceCount = source.dexKind === 'regular' ? regularCount : hyperspaceCount;
  const destinationCount =
    destinationDexKind === 'regular' ? regularCount : hyperspaceCount;
  const shiftedEntryCount = isCrossDexMove
    ? sourceCount -
      source.displayedNumber +
      (destinationCount - destinationDisplayedNumber + 1)
    : Math.abs(source.displayedNumber - destinationDisplayedNumber);
  const regularDelta = isCrossDexMove
    ? source.dexKind === 'regular'
      ? -1
      : 1
    : 0;

  return {
    destinationDexKind,
    destinationDisplayedNumber,
    resultingHyperspaceCount: hyperspaceCount - regularDelta,
    resultingRegularCount: regularCount + regularDelta,
    shiftedEntryCount,
    source
  };
}

function createDexResizePreview(
  placements: PokemonDexPlacement[],
  currentRegularCount: number,
  currentHyperspaceCount: number,
  proposedRegularCount: number,
  proposedHyperspaceCount: number
): DexResizePreview {
  const changes: DexResizeMembershipChange[] = [];

  if (proposedRegularCount > currentRegularCount) {
    const entriesMovingToRegular = proposedRegularCount - currentRegularCount;
    const hyperspacePlacements = placements
      .filter((placement) => placement.dexKind === 'hyperspace')
      .sort(
        (left, right) =>
          left.displayedNumber - right.displayedNumber ||
          left.speciesId - right.speciesId
      )
      .slice(0, entriesMovingToRegular);

    hyperspacePlacements.forEach((source, index) => {
      changes.push({
        destinationDexKind: 'regular',
        destinationDisplayedNumber: currentRegularCount + index + 1,
        source
      });
    });
  } else if (proposedRegularCount < currentRegularCount) {
    const regularPlacements = placements
      .filter(
        (placement) =>
          placement.dexKind === 'regular' &&
          placement.displayedNumber > proposedRegularCount
      )
      .sort(
        (left, right) =>
          left.displayedNumber - right.displayedNumber ||
          left.speciesId - right.speciesId
      );

    regularPlacements.forEach((source, index) => {
      changes.push({
        destinationDexKind: 'hyperspace',
        destinationDisplayedNumber: index + 1,
        source
      });
    });
  }

  return {
    changes,
    currentHyperspaceCount,
    currentRegularCount,
    proposedHyperspaceCount,
    proposedRegularCount
  };
}

function getMaximumDestination(
  source: PokemonDexPlacement | null,
  destinationDexKind: DexKind,
  regularCount: number,
  hyperspaceCount: number
) {
  const destinationCount =
    destinationDexKind === 'regular' ? regularCount : hyperspaceCount;
  return Math.max(
    1,
    destinationCount + (source?.dexKind === destinationDexKind ? 0 : 1)
  );
}

function parseDestinationNumber(value: string, maximum: number) {
  if (!/^\d+$/.test(value)) {
    return null;
  }

  const parsed = Number(value);
  return Number.isSafeInteger(parsed) && parsed >= 1 && parsed <= maximum
    ? parsed
    : null;
}

function parseDexSize(value: string) {
  if (!/^\d+$/.test(value)) {
    return null;
  }

  const parsed = Number(value);
  return Number.isSafeInteger(parsed) &&
    parsed >= MINIMUM_DEX_SIZE &&
    parsed <= MAXIMUM_DEX_SIZE
    ? parsed
    : null;
}

function createDexSizeKey(regularCount: number, hyperspaceCount: number) {
  return `${regularCount}|${hyperspaceCount}`;
}

function createDexSizeDraft(regular: string, hyperspace: string) {
  return { hyperspace, regular };
}

function areDexSizeDraftsEqual(
  left: ReturnType<typeof createDexSizeDraft>,
  right: ReturnType<typeof createDexSizeDraft>
) {
  return left.regular === right.regular && left.hyperspace === right.hyperspace;
}

function createDexMoveKey(placement: PokemonDexPlacement) {
  return `${placement.speciesId}|${placement.dexKind}|${placement.displayedNumber}`;
}

function createDexMoveDraft(dexKind: DexKind, displayedNumber: string) {
  return { dexKind, displayedNumber };
}

function areDexMoveDraftsEqual(
  left: ReturnType<typeof createDexMoveDraft>,
  right: ReturnType<typeof createDexMoveDraft>
) {
  return (
    left.dexKind === right.dexKind &&
    left.displayedNumber === right.displayedNumber
  );
}

function compareDexKinds(left: DexKind, right: DexKind) {
  if (left === right) {
    return 0;
  }

  return left === 'regular' ? -1 : 1;
}

function formatDexNumber(value: number) {
  return value.toString().padStart(3, '0');
}

function formatDexKind(kind: DexKind) {
  return kind === 'hyperspace' ? 'Hyperspace Dex' : 'Regular Dex';
}
