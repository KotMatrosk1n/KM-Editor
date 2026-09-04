/* SPDX-License-Identifier: GPL-3.0-only */

import {
  ArrowDown,
  ArrowUp,
  Clipboard,
  Download,
  ExternalLink,
  ListChecks,
  Plus,
  RefreshCw,
  ShieldCheck,
  Sparkles,
  Trash2
} from 'lucide-react';
import {
  useEffect,
  useMemo,
  useRef,
  useState,
  type FormEvent
} from 'react';
import {
  guidedDesignInputSchema,
  guidedDesignMaximumChangeSetNameLength,
  guidedDesignMaximumEligibleTargetWindow,
  guidedDesignMaximumPins,
  guidedDesignMaximumTargets,
  guidedDesignMaximumTargetSearchLength,
  guidedDesignProposalKindValues,
  guidedDesignSchemaVersion,
  type GuidedDesignCanonicalExport,
  type GuidedDesignCapability,
  type GuidedDesignFeature,
  type GuidedDesignFieldCatalog,
  type GuidedDesignImportRequest,
  type GuidedDesignImportResponse,
  type GuidedDesignInput,
  type GuidedDesignMutation,
  type GuidedDesignPin,
  type GuidedDesignPreviewResponse,
  type GuidedDesignProposalKind,
  type GuidedDesignRounding,
  type GuidedDesignTargetOption,
  type GuidedDesignTrainerArchetype
} from '../../bridge/guidedDesignContracts';
import { useCoalescedTextInputState } from '../../components/useCoalescedTextInputState';
import type { ApiDiagnostic } from '../../bridge/contracts';
import type {
  SemanticExploreRecordRef,
  SemanticExploreRevision,
  SemanticExploreScope
} from '../../bridge/semanticExploreContracts';
import {
  PublishCommonEditorError,
  usePublishCommonEditorDiagnostics,
  usePublishCommonEditorError
} from '../../components/CommonEditorDiagnostics';
import { LoadingProgress } from '../../components/LoadingProgress';
import { reconcileSourceBackedDraft } from '../../components/localEditorDraftState';
import { ReportableDiagnosticIssuesLink } from '../../components/ReportableErrorScreen';
import { SearchableOptionInput } from '../../components/SearchableOptionInput';
import { useDiagnosticNavigation } from '../../diagnosticActions';
import { formatDiagnosticSummary } from '../../diagnostics';
import { useLocalization } from '../../localization';
import { userFacingFeatureVisibility } from '../../workbench/featureVisibility';
import {
  DiagnosticSeverityText,
  DiagnosticTechnicalDetails,
  OccurrenceCount,
  TechnicalDetails
} from '../workbench/AnalysisPresentation';
import {
  diagnosticTechnicalIdentity,
  diagnosticSeverityPriority,
  groupDiagnosticsForPresentation,
  presentationDiagnosticMessage,
  presentationDiagnosticSeverity
} from '../workbench/analysisPresentationUtils';
import type {
  GuidedDesignController,
  GuidedDesignQueryError
} from './useGuidedDesignController';
import './guidedDesign.css';

const proposalKinds: readonly GuidedDesignProposalKind[] = guidedDesignProposalKindValues;
const maximumVisibleTargetSuggestions = 8;

export type GuidedDesignSectionProps = {
  canImportChangeSet: boolean;
  canNavigateRecord: (record: SemanticExploreRecordRef) => boolean;
  controller: GuidedDesignController;
  expectedChangeSetETag: string | null;
  isChangeSetWorkspaceBusy: boolean;
  isChangeSetWorkspaceReady: boolean;
  onImportProposal: (
    request: GuidedDesignImportRequest
  ) => Promise<GuidedDesignImportResponse>;
  onNavigateRecord: (record: SemanticExploreRecordRef) => void;
  onOpenChanges: () => void;
  revision: SemanticExploreRevision | null;
  scope: SemanticExploreScope;
};

export function GuidedDesignSection({
  canImportChangeSet,
  canNavigateRecord,
  controller,
  expectedChangeSetETag,
  isChangeSetWorkspaceBusy,
  isChangeSetWorkspaceReady,
  onImportProposal,
  onNavigateRecord,
  onOpenChanges,
  revision,
  scope
}: GuidedDesignSectionProps) {
  const { t } = useLocalization();
  const capabilities = controller.capabilities.data?.capabilities ?? [];
  const fieldCatalogs = controller.capabilities.data?.fieldCatalogs ?? [];
  const availableKinds = useMemo(
    () => proposalKinds.filter((kind) => capabilities.some(
      (capability) => capability.state !== 'unavailable' &&
        capability.proposalKinds.includes(kind)
    )),
    [capabilities]
  );
  const [kind, setKind] = useState<GuidedDesignProposalKind | null>(null);
  const [delta, setDelta] = useState('5');
  const [fieldKeys, setFieldKeys] = useState('');
  const [multiplierBasisPoints, setMultiplierBasisPoints] = useState('10000');
  const [minimumValue, setMinimumValue] = useState('1');
  const [maximumValue, setMaximumValue] = useState('100');
  const [rounding, setRounding] = useState<GuidedDesignRounding>('nearest');
  const [archetype, setArchetype] = useState<GuidedDesignTrainerArchetype>('balanced');
  const [seed, setSeed] = useState('');
  const [targetSearchText, setTargetSearchText] = useCoalescedTextInputState();
  const [targetSelection, setTargetSelection] = useState<{
    inputIdentity: string | null;
    targets: Map<string, GuidedDesignTargetOption>;
  }>({ inputIdentity: null, targets: new Map() });
  const [submittedDraftIdentity, setSubmittedDraftIdentity] = useState<string | null>(null);
  const [importReceipt, setImportReceipt] = useState<{
    diagnostics: readonly ApiDiagnostic[];
    importedChangeSetId: string;
  } | null>(null);
  const projectSourceIdentity = useMemo(() => JSON.stringify([
    scope.projectId,
    scope.paths,
    revision
      ? [revision.projectId, revision.gameFamily, revision.generation, revision.fingerprint]
      : null
  ]), [revision, scope.paths, scope.projectId]);

  useEffect(() => {
    setImportReceipt(null);
    setSubmittedDraftIdentity(null);
    setTargetSelection({ inputIdentity: null, targets: new Map() });
  }, [projectSourceIdentity]);

  useEffect(() => {
    if (controller.capabilities.data === null) {
      setSubmittedDraftIdentity(null);
      setTargetSelection({ inputIdentity: null, targets: new Map() });
    }
  }, [controller.capabilities.data]);

  useEffect(() => {
    if (kind && availableKinds.includes(kind)) return;
    setKind(availableKinds[0] ?? null);
  }, [availableKinds, kind]);

  const selectedFieldCatalog = kind
    ? fieldCatalogs.find((catalog) => catalog.kind === kind) ?? null
    : null;
  const selectedFieldCatalogIdentity = selectedFieldCatalog
    ? JSON.stringify(selectedFieldCatalog)
    : null;
  useEffect(() => {
    setFieldKeys(selectedFieldCatalog?.fieldKeys.join(',') ?? '');
  }, [selectedFieldCatalogIdentity]);

  const parsedInput = useMemo(
    () => buildInput({
      archetype,
      delta,
      fieldKeys,
      kind,
      maximumValue,
      minimumValue,
      multiplierBasisPoints,
      rounding,
      seed
    }),
    [
      archetype,
      delta,
      fieldKeys,
      kind,
      maximumValue,
      minimumValue,
      multiplierBasisPoints,
      rounding,
      seed
    ]
  );
  const selectedCapability = capabilities.find((capability) => (
    kind !== null &&
    capability.state !== 'unavailable' &&
    capability.proposalKinds.includes(kind)
  )) ?? null;
  const currentDraftIdentity = parsedInput.success
    ? JSON.stringify([
        parsedInput.data,
        targetSearchText.trim().normalize('NFC') || null
      ])
    : null;
  const isReviewedDraftCurrent = currentDraftIdentity !== null &&
    submittedDraftIdentity === currentDraftIdentity;
  const retainedProposalInput = targetSearchText.trim().length === 0 &&
    controller.preview.data?.selectionRequired === false &&
    controller.preview.data.normalizedInput.kind === kind
    ? controller.preview.data.normalizedInput
    : null;
  const targetSearchRequired = targetSearchText.trim().length === 0 &&
    retainedProposalInput === null;

  const handlePreview = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (
      parsedInput.success &&
      isChangeSetWorkspaceReady &&
      !isChangeSetWorkspaceBusy
    ) {
      const normalizedSearch = targetSearchText.trim().normalize('NFC') || null;
      const activeInput = controller.preview.data?.normalizedInput;
      const previewInput = normalizedSearch === null &&
        activeInput?.kind === parsedInput.data.kind &&
        !controller.preview.data?.selectionRequired
        ? {
            ...parsedInput.data,
            pins: activeInput.pins,
            targets: activeInput.targets
          }
        : parsedInput.data;
      const inputIdentity = JSON.stringify(parsedInput.data);
      setSubmittedDraftIdentity(JSON.stringify([parsedInput.data, normalizedSearch]));
      setTargetSelection((current) => current.inputIdentity === inputIdentity
        ? current
        : { inputIdentity, targets: new Map() });
      setImportReceipt(null);
      void controller.previewDesign(
        previewInput,
        previewInput.targets.length === 0 ? normalizedSearch : null
      );
    }
  };

  return (
    <section
      aria-busy={controller.isQuerying || isChangeSetWorkspaceBusy || undefined}
      aria-labelledby="guided-design-title"
      className="km-guided-design wide-panel"
    >
      <header className="km-guided-heading">
        <div>
          <p>{t('guidedDesign.eyebrow')}</p>
          <h2 id="guided-design-title">{t('guidedDesign.title')}</h2>
          <span>{t(userFacingFeatureVisibility.namedChangeSets
            ? 'guidedDesign.description'
            : 'guidedDesign.inputs.description')}</span>
        </div>
        <button
          aria-busy={controller.isQuerying || undefined}
          className="secondary-button compact-button"
          disabled={
            !controller.preview.data ||
            !isReviewedDraftCurrent ||
            controller.isQuerying ||
            !isChangeSetWorkspaceReady ||
            isChangeSetWorkspaceBusy
          }
          onClick={() => void controller.refresh()}
          type="button"
        >
          <RefreshCw aria-hidden="true" size={15} />
          <span>{t(controller.isQuerying
            ? 'guidedDesign.preview.loading'
            : 'guidedDesign.refresh')}</span>
        </button>
      </header>

      {userFacingFeatureVisibility.namedChangeSets ? (
        <aside className="km-guided-safety-note">
          <ShieldCheck aria-hidden="true" size={18} />
          <p>{t('guidedDesign.safety')}</p>
        </aside>
      ) : null}

      {controller.capabilities.status === 'loading' ? (
        <StatusPanel kind="loading" />
      ) : null}
      {controller.capabilities.status === 'error' ? (
        <StatusPanel
          error={controller.capabilities.error}
          kind="error"
          onRetry={() => void controller.ensureCapabilities()}
        />
      ) : null}

      {controller.capabilities.data ? (
        <>
          <CapabilityGrid capabilities={capabilities} />
          {availableKinds.length > 0 ? (
            <form
              aria-busy={controller.preview.status === 'loading'}
              className="km-guided-inputs"
              onSubmit={handlePreview}
            >
              <div className="km-guided-section-heading">
                <Sparkles aria-hidden="true" size={18} />
                <div>
                  <h3>{t('guidedDesign.inputs.title')}</h3>
                  <p>{t('guidedDesign.inputs.description')}</p>
                </div>
              </div>
              <fieldset>
                <legend>{t('guidedDesign.inputs.constraints')}</legend>
              <div className="km-guided-control-grid">
                <div className="km-searchable-select-field km-guided-control-field">
                  <label htmlFor="guided-design-proposal-kind">
                    <span>{t('guidedDesign.inputs.kind')}</span>
                  </label>
                  <SearchableOptionInput
                    ariaLabel={t('guidedDesign.inputs.kind')}
                    data-km-source-site="guided-design-proposal-kind"
                    disabled={false}
                    id="guided-design-proposal-kind"
                    isFiniteCatalog
                    onChange={(value) => {
                      const nextKind = value as GuidedDesignProposalKind;
                      setKind(nextKind);
                      setFieldKeys(
                        fieldCatalogs.find((catalog) => catalog.kind === nextKind)
                          ?.fieldKeys.join(',') ?? ''
                      );
                    }}
                    options={proposalKinds.map((value) => ({
                      disabled: !availableKinds.includes(value),
                      label: `${t(`guidedDesign.kind.${value}`)}${
                        !availableKinds.includes(value)
                          ? ` (${t('guidedDesign.coverage.unavailable')})`
                          : ''
                      }`,
                      value
                    }))}
                    value={kind ?? ''}
                  />
                </div>
                {selectedFieldCatalog ? (
                  <GuidedFieldSelector
                    catalog={selectedFieldCatalog}
                    onChange={(values) => setFieldKeys(values.join(','))}
                    selectedValues={parseFieldKeys(fieldKeys)}
                  />
                ) : null}
                <label className="km-guided-control-wide">
                  <span>{t('guidedDesign.inputs.targetSearch')}</span>
                  <input
                    autoComplete="off"
                    maxLength={guidedDesignMaximumTargetSearchLength}
                    onChange={(event) => setTargetSearchText(event.currentTarget.value)}
                    placeholder={t('guidedDesign.inputs.targetSearchPlaceholder')}
                    type="search"
                    value={targetSearchText}
                  />
                  <small>{t('guidedDesign.inputs.targetSearchHelp')}</small>
                </label>
                {kind && usesDelta(kind) ? (
                  <NumberControl
                    labelKey="guidedDesign.inputs.delta"
                    maximum={100}
                    minimum={-100}
                    onChange={setDelta}
                    value={delta}
                  />
                ) : null}
                {kind && usesMultiplier(kind) ? (
                  <>
                    <NumberControl
                      labelKey="guidedDesign.inputs.multiplier"
                      maximum={100000}
                      minimum={0}
                      onChange={setMultiplierBasisPoints}
                      value={multiplierBasisPoints}
                    />
                    <SelectControl
                      id="guided-design-rounding"
                      labelKey="guidedDesign.inputs.rounding"
                      onChange={(value) => setRounding(value as GuidedDesignRounding)}
                      options={['floor', 'nearest', 'ceiling']}
                      translationPrefix="guidedDesign.rounding"
                      value={rounding}
                    />
                  </>
                ) : null}
                {kind === 'evolutionLevelClamp' ? (
                  <>
                    <NumberControl
                      labelKey="guidedDesign.inputs.minimum"
                      maximum={100}
                      minimum={0}
                      onChange={setMinimumValue}
                      value={minimumValue}
                    />
                    <NumberControl
                      labelKey="guidedDesign.inputs.maximum"
                      maximum={100}
                      minimum={0}
                      onChange={setMaximumValue}
                      value={maximumValue}
                    />
                  </>
                ) : null}
                {kind === 'trainerEvArchetype' ? (
                  <SelectControl
                    id="guided-design-trainer-archetype"
                    labelKey="guidedDesign.inputs.archetype"
                    onChange={(value) => setArchetype(value as GuidedDesignTrainerArchetype)}
                    options={['physicalAttackSpeed', 'specialAttackSpeed', 'balanced']}
                    translationPrefix="guidedDesign.archetype"
                    value={archetype}
                  />
                ) : null}
                {kind === 'pokemonBaseStatShuffle' ? (
                  <label>
                    <span>{t('guidedDesign.inputs.seed')}</span>
                    <input
                      autoComplete="off"
                      maxLength={32}
                      onChange={(event) => setSeed(event.currentTarget.value)}
                      pattern="[0-9a-fA-F]{32}"
                      placeholder={t('guidedDesign.inputs.seedPlaceholder')}
                      spellCheck={false}
                      type="text"
                      value={seed}
                    />
                    <small>{t('guidedDesign.inputs.seedHelp')}</small>
                  </label>
                ) : null}
              </div>
              <dl className="km-guided-constraint-summary">
                <div>
                  <dt>{t('guidedDesign.inputs.layer')}</dt>
                  <dd>{t('guidedDesign.layer.layered')}</dd>
                </div>
                <div>
                  <dt>{t('guidedDesign.inputs.targets')}</dt>
                  <dd>{retainedProposalInput
                    ? t('guidedDesign.targets.selected', {
                        count: retainedProposalInput.targets.length
                      })
                    : t('guidedDesign.inputs.allEligible')}</dd>
                </div>
                <div>
                  <dt>{t('guidedDesign.inputs.pins')}</dt>
                  <dd>{retainedProposalInput?.pins.length
                    ? String(retainedProposalInput.pins.length)
                    : t('guidedDesign.inputs.noPins')}</dd>
                </div>
                <div>
                  <dt>{t('guidedDesign.inputs.coverage')}</dt>
                  <dd>
                    {selectedCapability
                      ? t(`guidedDesign.coverage.${selectedCapability.state}`)
                      : t('guidedDesign.coverage.unavailable')}
                  </dd>
                </div>
              </dl>
              </fieldset>
              {!parsedInput.success ? (
                <>
                  <PublishCommonEditorError
                    domain="analysis.guidedDesign"
                    field="inputs"
                    message={t('guidedDesign.inputs.invalid')}
                  />
                  <p className="km-guided-form-error" role="alert">
                    {t('guidedDesign.inputs.invalid')}
                  </p>
                </>
              ) : null}
              {!isChangeSetWorkspaceReady || isChangeSetWorkspaceBusy ? (
                <p className="km-guided-advisory">
                  {t('guidedDesign.preview.workspaceBusy')}
                </p>
              ) : null}
              {targetSearchRequired ? (
                <p className="km-guided-advisory">
                  {t('guidedDesign.inputs.targetSearchRequired')}
                </p>
              ) : null}
              <button
                aria-busy={
                  controller.preview.status === 'loading' &&
                  !controller.preview.isAppending ||
                  undefined
                }
                className="primary-button"
                disabled={
                  !parsedInput.success ||
                  targetSearchRequired ||
                  controller.isQuerying ||
                  !isChangeSetWorkspaceReady ||
                  isChangeSetWorkspaceBusy
                }
                type="submit"
              >
                <Sparkles aria-hidden="true" size={16} />
                <span>
                  {controller.preview.status === 'loading' && !controller.preview.isAppending
                    ? t('guidedDesign.preview.loading')
                    : t('guidedDesign.preview.action')}
                </span>
              </button>
            </form>
          ) : (
            <p className="km-workbench-empty">{t('guidedDesign.unavailable')}</p>
          )}
          {controller.preview.status === 'loading' && !controller.preview.isAppending ? (
            <LoadingProgress
              className="is-compact"
              label={t('guidedDesign.preview.loading')}
            />
          ) : null}
        </>
      ) : null}

      {controller.preview.status === 'error' && isReviewedDraftCurrent ? (
        <InlineError
          error={controller.preview.error}
          onRetry={() => void controller.refresh()}
        />
      ) : null}
      {controller.preview.data && isReviewedDraftCurrent ? (
        <GuidedDesignResults
          canImportChangeSet={canImportChangeSet}
          canNavigateRecord={canNavigateRecord}
          controller={controller}
          expectedChangeSetETag={expectedChangeSetETag}
          isChangeSetWorkspaceBusy={isChangeSetWorkspaceBusy}
          onExactTargetsGenerated={() => {
            setTargetSearchText('');
            if (parsedInput.success) {
              setSubmittedDraftIdentity(JSON.stringify([parsedInput.data, null]));
            }
          }}
          onImportProposal={onImportProposal}
          onImported={setImportReceipt}
          onNavigateRecord={onNavigateRecord}
          response={controller.preview.data}
          revision={revision ?? controller.capabilities.data?.revision ?? null}
          scope={scope}
          selectedDiscoveryTargets={targetSelection.targets}
          onSelectedDiscoveryTargetsChange={(targets) => setTargetSelection((current) => ({
            ...current,
            targets
          }))}
        />
      ) : null}
      {userFacingFeatureVisibility.namedChangeSets && importReceipt ? (
        <ImportReceipt diagnostics={importReceipt.diagnostics} onOpenChanges={onOpenChanges} />
      ) : null}
    </section>
  );
}

function CapabilityGrid({ capabilities }: { capabilities: readonly GuidedDesignCapability[] }) {
  const { t, translateLiteral } = useLocalization();
  return (
    <section aria-labelledby="guided-design-coverage-title" className="km-guided-coverage">
      <div className="km-guided-section-heading">
        <ShieldCheck aria-hidden="true" size={18} />
        <div>
          <h3 id="guided-design-coverage-title">{t('guidedDesign.coverage.title')}</h3>
          <p>{t('guidedDesign.coverage.disclaimer')}</p>
        </div>
      </div>
      <ul>
        {capabilities.map((capability) => (
          <li key={capability.feature}>
            <div>
              <strong>{t(`guidedDesign.feature.${capability.feature}`)}</strong>
              <span className="km-guided-coverage-state" data-state={capability.state}>
                {t(`guidedDesign.coverage.${capability.state}`)}
              </span>
            </div>
            <span>{t(`guidedDesign.confidence.${capability.confidence}`)}</span>
            {capability.proposalKinds.length > 0 ? (
              <small>
                {capability.proposalKinds.map((kind) => (
                  t(`guidedDesign.kind.${kind}`)
                )).join(', ')}
              </small>
            ) : null}
            {capability.reasonCode ? (
              <div className="km-guided-coverage-reason">
                <span>{t(coverageReasonKey(capability.reasonCode))}</span>
                <TechnicalDetails summary={translateLiteral('Technical details')}>
                  <code>{capability.reasonCode}</code>
                </TechnicalDetails>
              </div>
            ) : null}
          </li>
        ))}
      </ul>
    </section>
  );
}

function GuidedDesignResults({
  ...props
}: {
  canImportChangeSet: boolean;
  canNavigateRecord: (record: SemanticExploreRecordRef) => boolean;
  controller: GuidedDesignController;
  expectedChangeSetETag: string | null;
  isChangeSetWorkspaceBusy: boolean;
  onExactTargetsGenerated: () => void;
  onImportProposal: (
    request: GuidedDesignImportRequest
  ) => Promise<GuidedDesignImportResponse>;
  onImported: (receipt: {
    diagnostics: readonly ApiDiagnostic[];
    importedChangeSetId: string;
  }) => void;
  onNavigateRecord: (record: SemanticExploreRecordRef) => void;
  onSelectedDiscoveryTargetsChange: (
    targets: Map<string, GuidedDesignTargetOption>
  ) => void;
  response: GuidedDesignPreviewResponse;
  revision: SemanticExploreRevision | null;
  scope: SemanticExploreScope;
  selectedDiscoveryTargets: ReadonlyMap<string, GuidedDesignTargetOption>;
}) {
  return props.response.selectionRequired
    ? <GuidedDesignTargetSelection {...props} />
    : <GuidedDesignProposalResults {...props} />;
}

function GuidedDesignTargetSelection({
  canNavigateRecord,
  controller,
  isChangeSetWorkspaceBusy,
  onExactTargetsGenerated,
  onNavigateRecord,
  onSelectedDiscoveryTargetsChange,
  selectedDiscoveryTargets,
  response
}: {
  canNavigateRecord: (record: SemanticExploreRecordRef) => boolean;
  controller: GuidedDesignController;
  isChangeSetWorkspaceBusy: boolean;
  onExactTargetsGenerated: () => void;
  onNavigateRecord: (record: SemanticExploreRecordRef) => void;
  onSelectedDiscoveryTargetsChange: (
    targets: Map<string, GuidedDesignTargetOption>
  ) => void;
  response: GuidedDesignPreviewResponse;
  selectedDiscoveryTargets: ReadonlyMap<string, GuidedDesignTargetOption>;
}) {
  const { t } = useLocalization();
  const headingRef = useRef<HTMLHeadingElement | null>(null);
  const [targetOrder, setTargetOrder] = useState<'name' | 'record'>('name');
  useEffect(() => {
    headingRef.current?.focus({ preventScroll: true });
  }, [response.proposalId]);
  const selectedTargets = [...selectedDiscoveryTargets.values()];
  const exactTargets = selectedTargets.map((option) => option.record);
  const isBusy = controller.isQuerying || isChangeSetWorkspaceBusy;
  const selectionMutationLocked =
    controller.preview.status === 'loading' && !controller.preview.isAppending;
  const orderedTargets = useMemo(() => [...response.eligibleTargets].sort((left, right) => (
    targetOrder === 'record'
      ? formatSemanticRecord(left.record).localeCompare(formatSemanticRecord(right.record)) ||
        left.recordLabel.localeCompare(right.recordLabel)
      : left.recordLabel.localeCompare(right.recordLabel) ||
        formatSemanticRecord(left.record).localeCompare(formatSemanticRecord(right.record))
  )), [response.eligibleTargets, targetOrder]);
  const availableTargets = orderedTargets.filter((option) => (
    !selectedDiscoveryTargets.has(semanticRecordKey(option.record))
  ));
  const visibleTargetSuggestions = availableTargets.slice(0, maximumVisibleTargetSuggestions);
  const moveSelectedTarget = (key: string, direction: -1 | 1) => {
    const entries = [...selectedDiscoveryTargets.entries()];
    const index = entries.findIndex(([candidate]) => candidate === key);
    const destination = index + direction;
    if (index < 0 || destination < 0 || destination >= entries.length) return;
    [entries[index], entries[destination]] = [entries[destination]!, entries[index]!];
    onSelectedDiscoveryTargetsChange(new Map(entries));
  };
  return (
    <section
      aria-busy={controller.preview.isAppending}
      aria-labelledby="guided-design-target-selection-title"
      className="km-guided-results km-guided-target-selection"
    >
      <div className="km-guided-section-heading">
        <ListChecks aria-hidden="true" size={18} />
        <div>
          <h3
            id="guided-design-target-selection-title"
            ref={headingRef}
            tabIndex={-1}
          >
            {t('guidedDesign.selection.title')}
          </h3>
          <p>{t('guidedDesign.selection.description')}</p>
        </div>
      </div>
      <p aria-live="polite" className="km-guided-advisory">
        {t('guidedDesign.selection.summary', {
          loaded: response.eligibleTargets.length,
          selected: selectedDiscoveryTargets.size,
          total: response.totalEligibleTargetCount
        })}
      </p>
      {response.normalizedTargetSearchText ? (
        <p>
          {t('guidedDesign.selection.search', {
            query: response.normalizedTargetSearchText
          })}
        </p>
      ) : null}
      {response.eligibleTargetWindowCapped || (
        response.eligibleTargets.length >= guidedDesignMaximumEligibleTargetWindow &&
        response.nextCursor !== null
      ) ? (
        <p className="km-guided-advisory">{t('guidedDesign.selection.windowCapped')}</p>
      ) : null}
      <section className="km-guided-selected-targets">
        <div className="km-guided-selected-targets-heading">
          <h4>{t('guidedDesign.selection.selectedTitle')}</h4>
          {selectedTargets.length > 0 ? (
            <button
              className="secondary-button compact-button"
              disabled={selectionMutationLocked}
              onClick={() => onSelectedDiscoveryTargetsChange(new Map())}
              type="button"
            >
              {t('guidedDesign.selection.clear')}
            </button>
          ) : null}
        </div>
        {selectedTargets.length > 0 ? (
          <ol className="km-guided-selected-target-list">
            {selectedTargets.map((option, index) => {
              const key = semanticRecordKey(option.record);
              const accessibleName = `${option.recordLabel}, ${formatSemanticRecord(option.record)}`;
              return (
                <li key={key}>
                  <span>
                    <strong data-localization-ignore="true">{option.recordLabel}</strong>
                    <code data-localization-ignore="true">{formatSemanticRecord(option.record)}</code>
                  </span>
                  <div className="km-guided-target-order-actions">
                    <button
                      aria-label={t('guidedDesign.selection.moveUpNamed', { name: accessibleName })}
                      className="secondary-button compact-button"
                      disabled={selectionMutationLocked || index === 0}
                      onClick={() => moveSelectedTarget(key, -1)}
                      type="button"
                    >
                      <ArrowUp aria-hidden="true" size={14} />
                    </button>
                    <button
                      aria-label={t('guidedDesign.selection.moveDownNamed', { name: accessibleName })}
                      className="secondary-button compact-button"
                      disabled={selectionMutationLocked || index === selectedTargets.length - 1}
                      onClick={() => moveSelectedTarget(key, 1)}
                      type="button"
                    >
                      <ArrowDown aria-hidden="true" size={14} />
                    </button>
                    <button
                      aria-label={t('guidedDesign.selection.removeNamed', { name: accessibleName })}
                      className="secondary-button compact-button"
                      disabled={selectionMutationLocked}
                      onClick={() => {
                        const next = new Map(selectedDiscoveryTargets);
                        next.delete(key);
                        onSelectedDiscoveryTargetsChange(next);
                      }}
                      type="button"
                    >
                      <Trash2 aria-hidden="true" size={14} />
                    </button>
                  </div>
                </li>
              );
            })}
          </ol>
        ) : (
          <p className="km-workbench-empty">{t('guidedDesign.selection.none')}</p>
        )}
      </section>
      {response.normalizedTargetSearchText ? (
        <fieldset>
          <legend>{t('guidedDesign.selection.legend')}</legend>
          <div className="km-guided-result-controls">
            <div className="km-searchable-select-field km-guided-result-field">
              <label htmlFor="guided-design-target-sort">
                <span>{t('analysisPresentation.controls.sort')}</span>
              </label>
              <SearchableOptionInput
                ariaLabel={t('analysisPresentation.controls.sort')}
                data-km-source-site="guided-design-target-sort"
                disabled={false}
                id="guided-design-target-sort"
                isFiniteCatalog
                onChange={(value) => setTargetOrder(value as typeof targetOrder)}
                options={[
                  { label: t('analysisPresentation.controls.record'), value: 'name' },
                  { label: t('analysisPresentation.controls.identifier'), value: 'record' }
                ]}
                value={targetOrder}
              />
            </div>
          </div>
          {visibleTargetSuggestions.length > 0 ? (
            <div className="km-guided-target-option-list">
              {visibleTargetSuggestions.map((option) => {
                const key = semanticRecordKey(option.record);
                return (
                  <div className="km-guided-target-option" key={key}>
                    <span className="km-guided-target-option-copy">
                      <strong data-localization-ignore="true">{option.recordLabel}</strong>
                      <code data-localization-ignore="true">
                        {formatSemanticRecord(option.record)}
                      </code>
                    </span>
                    <div className="km-guided-target-option-actions">
                      <button
                        aria-label={`${t('guidedDesign.selection.add')}: ${option.recordLabel}, ${formatSemanticRecord(option.record)}`}
                        className="secondary-button compact-button"
                        disabled={
                          selectionMutationLocked ||
                          selectedDiscoveryTargets.size >= guidedDesignMaximumTargets
                        }
                        onClick={() => {
                          const next = new Map(selectedDiscoveryTargets);
                          if (next.size < guidedDesignMaximumTargets) {
                            next.set(key, option);
                          }
                          onSelectedDiscoveryTargetsChange(next);
                        }}
                        type="button"
                      >
                        <Plus aria-hidden="true" size={14} />
                        <span>{t('guidedDesign.selection.add')}</span>
                      </button>
                      <OpenRecordButton
                        accessibleName={
                          `${option.recordLabel}, ${formatSemanticRecord(option.record)}`
                        }
                        canNavigate={canNavigateRecord(option.record)}
                        onNavigate={() => onNavigateRecord(option.record)}
                      />
                    </div>
                  </div>
                );
              })}
            </div>
          ) : (
            <p className="km-workbench-empty">{t('analysisPresentation.controls.noMatches')}</p>
          )}
          {availableTargets.length > maximumVisibleTargetSuggestions ? (
            <p className="km-guided-advisory">
              {t('guidedDesign.selection.refineSearch', {
                count: maximumVisibleTargetSuggestions
              })}
            </p>
          ) : null}
        </fieldset>
      ) : (
        <p className="km-guided-advisory">{t('guidedDesign.inputs.targetSearchRequired')}</p>
      )}
      {selectedDiscoveryTargets.size >= guidedDesignMaximumTargets ? (
        <p className="km-guided-advisory">{t('guidedDesign.selection.limit')}</p>
      ) : null}
      <div className="km-guided-selection-actions">
        {response.normalizedTargetSearchText && response.nextCursor &&
        response.eligibleTargets.length < guidedDesignMaximumEligibleTargetWindow ? (
          <button
            aria-busy={controller.preview.isAppending || undefined}
            className="secondary-button"
            disabled={isBusy}
            onClick={() => void controller.loadMore()}
            type="button"
          >
            {controller.preview.isAppending
              ? t('guidedDesign.selection.loadingMore')
              : t('guidedDesign.selection.loadMore')}
          </button>
        ) : null}
        <button
          className="primary-button"
          disabled={isBusy || exactTargets.length === 0}
          onClick={() => {
            onExactTargetsGenerated();
            void controller.previewDesign({
              ...response.normalizedInput,
              targets: exactTargets
            });
          }}
          type="button"
        >
          <Sparkles aria-hidden="true" size={16} />
          <span>{t('guidedDesign.selection.generate')}</span>
        </button>
      </div>
      {controller.preview.isAppending ? (
        <LoadingProgress
          className="is-compact"
          completed={response.eligibleTargets.length}
          label={t('guidedDesign.selection.loadingMore')}
          total={Math.min(
            response.totalEligibleTargetCount,
            guidedDesignMaximumEligibleTargetWindow
          )}
        />
      ) : null}
      <DiagnosticList diagnostics={response.diagnostics} />
    </section>
  );
}

function GuidedDesignProposalResults({
  canImportChangeSet,
  canNavigateRecord,
  controller,
  expectedChangeSetETag,
  isChangeSetWorkspaceBusy,
  onImportProposal,
  onImported,
  onNavigateRecord,
  onSelectedDiscoveryTargetsChange,
  response,
  revision,
  scope
}: {
  canImportChangeSet: boolean;
  canNavigateRecord: (record: SemanticExploreRecordRef) => boolean;
  controller: GuidedDesignController;
  expectedChangeSetETag: string | null;
  isChangeSetWorkspaceBusy: boolean;
  onImportProposal: (
    request: GuidedDesignImportRequest
  ) => Promise<GuidedDesignImportResponse>;
  onImported: (receipt: {
    diagnostics: readonly ApiDiagnostic[];
    importedChangeSetId: string;
  }) => void;
  onNavigateRecord: (record: SemanticExploreRecordRef) => void;
  onSelectedDiscoveryTargetsChange: (
    targets: Map<string, GuidedDesignTargetOption>
  ) => void;
  response: GuidedDesignPreviewResponse;
  revision: SemanticExploreRevision | null;
  scope: SemanticExploreScope;
}) {
  const { t } = useLocalization();
  const headingRef = useRef<HTMLHeadingElement | null>(null);
  const importErrorRef = useRef<HTMLParagraphElement | null>(null);
  const [changeSetName, setChangeSetName] = useState(() => (
    t('guidedDesign.import.defaultName')
  ));
  const [importState, setImportState] = useState<{
    error: boolean;
    importedChangeSetId: string | null;
    proposalId: string | null;
    status: 'idle' | 'busy' | 'success' | 'error';
  }>({ error: false, importedChangeSetId: null, proposalId: null, status: 'idle' });
  const importOperationRef = useRef<object | null>(null);
  const isMountedRef = useRef(true);

  useEffect(() => () => {
    isMountedRef.current = false;
    importOperationRef.current = null;
  }, []);

  useEffect(() => {
    importOperationRef.current = null;
    headingRef.current?.focus({ preventScroll: true });
    setImportState({
      error: false,
      importedChangeSetId: null,
      proposalId: response.proposalId,
      status: 'idle'
    });
  }, [response.proposalId]);

  const normalizedName = changeSetName.trim();
  const importAllowed = Boolean(
    response.canImport &&
    response.nextCursor === null &&
    revision &&
    controller.preview.status === 'ready' &&
    canImportChangeSet &&
    !isChangeSetWorkspaceBusy &&
    importState.status !== 'busy' &&
    importState.status !== 'success' &&
    normalizedName &&
    normalizedName.length <= guidedDesignMaximumChangeSetNameLength
  );
  const handleImport = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!importAllowed || !revision || importOperationRef.current !== null) return;
    const importOperation = {};
    importOperationRef.current = importOperation;
    setImportState((state) => ({ ...state, error: false, status: 'busy' }));
    try {
      const imported = await onImportProposal({
        changeSetName: normalizedName,
        expectedChangeSetETag,
        expectedRevision: revision,
        input: response.normalizedInput,
        proposalFingerprint: response.proposalFingerprint,
        proposalId: response.proposalId,
        scope
      });
      if (!isMountedRef.current || importOperationRef.current !== importOperation) return;
      if (
        imported.proposalId !== response.proposalId ||
        imported.proposalFingerprint !== response.proposalFingerprint ||
        semanticRevisionKey(imported.revision) !== semanticRevisionKey(revision)
      ) {
        throw new Error('Guided Design imported another proposal.');
      }
      setImportState({
        error: false,
        importedChangeSetId: imported.importedChangeSetId,
        proposalId: response.proposalId,
        status: 'success'
      });
      onImported({
        diagnostics: imported.diagnostics,
        importedChangeSetId: imported.importedChangeSetId
      });
    } catch {
      if (isMountedRef.current && importOperationRef.current === importOperation) {
        setImportState({
          error: true,
          importedChangeSetId: null,
          proposalId: response.proposalId,
          status: 'error'
        });
      }
    } finally {
      if (importOperationRef.current === importOperation) {
        importOperationRef.current = null;
      }
    }
  };

  useEffect(() => {
    if (importState.status === 'error') {
      importErrorRef.current?.focus({ preventScroll: true });
    }
  }, [importState.status]);

  return (
    <section
      aria-busy={controller.preview.isAppending}
      aria-labelledby="guided-design-results-title"
      className="km-guided-results"
    >
      <div className="km-guided-section-heading">
        <ListChecks aria-hidden="true" size={18} />
        <div>
          <h3 id="guided-design-results-title" ref={headingRef} tabIndex={-1}>
            {t('guidedDesign.results.title')}
          </h3>
          {userFacingFeatureVisibility.namedChangeSets ? (
            <p>{t('guidedDesign.results.description')}</p>
          ) : null}
        </div>
      </div>

      <dl className="km-guided-result-summary">
        <SummaryValue labelKey="guidedDesign.results.seed" raw value={response.seed ?? t('guidedDesign.results.noSeed')} />
        <SummaryValue
          labelKey="guidedDesign.results.affected"
          value={String(response.affectedRecords.length)}
        />
        <SummaryValue
          labelKey="guidedDesign.results.mutations"
          value={t('guidedDesign.results.loadedTotal', {
            loaded: response.mutations.length,
            total: response.totalMutationCount
          })}
        />
        <SummaryValue
          labelKey="guidedDesign.results.findings"
          value={t('guidedDesign.results.loadedTotal', {
            loaded: response.findings.length,
            total: response.totalFindingCount
          })}
        />
      </dl>

      <SeedInspector response={response} />
      <NormalizedInput
        controller={controller}
        input={response.normalizedInput}
        isChangeSetWorkspaceBusy={isChangeSetWorkspaceBusy}
      />
      <TargetConstraints
        controller={controller}
        isChangeSetWorkspaceBusy={isChangeSetWorkspaceBusy}
        onExactTargetsChange={(targets) => onSelectedDiscoveryTargetsChange(new Map(
          targets.map((record) => [
            semanticRecordKey(record),
            {
              record,
              recordLabel: guidedDesignRecordLabel(response, record)
            }
          ])
        ))}
        response={response}
      />
      <AffectedRecords
        canNavigateRecord={canNavigateRecord}
        onNavigateRecord={onNavigateRecord}
        records={response.affectedRecords}
      />
      <Findings
        canNavigateRecord={canNavigateRecord}
        findings={response.findings}
        onNavigateRecord={onNavigateRecord}
      />
      <MutationDiff
        canNavigateRecord={canNavigateRecord}
        controller={controller}
        isChangeSetWorkspaceBusy={isChangeSetWorkspaceBusy}
        mutations={response.mutations}
        onNavigateRecord={onNavigateRecord}
      />
      {response.nextCursor ? (
        <button
          aria-busy={controller.preview.isAppending || undefined}
          className="secondary-button km-guided-load-more"
          disabled={controller.preview.isAppending || isChangeSetWorkspaceBusy}
          onClick={() => void controller.loadMore()}
          type="button"
        >
          {controller.preview.isAppending
            ? t('guidedDesign.preview.loadingMore')
            : t('guidedDesign.preview.loadMore')}
        </button>
      ) : null}
      {controller.preview.isAppending ? (
        <LoadingProgress
          className="is-compact"
          completed={response.mutations.length + response.findings.length}
          label={t('guidedDesign.preview.loadingMore')}
          total={response.totalMutationCount + response.totalFindingCount}
        />
      ) : null}
      <DiagnosticList diagnostics={response.diagnostics} />
      <CanonicalExports key={response.proposalFingerprint} exports={response.exports} />

      {userFacingFeatureVisibility.namedChangeSets ? (
        <form
          aria-busy={importState.status === 'busy'}
          className="km-guided-import"
          onSubmit={handleImport}
        >
          <div>
            <h4>{t('guidedDesign.import.title')}</h4>
            <p>{t('guidedDesign.import.description')}</p>
          </div>
          <label>
            <span>{t('guidedDesign.import.name')}</span>
            <input
              disabled={importState.status === 'success'}
              maxLength={guidedDesignMaximumChangeSetNameLength}
              onChange={(event) => setChangeSetName(event.currentTarget.value)}
              type="text"
              value={changeSetName}
            />
          </label>
          {!response.canImport ? (
            <p className="km-guided-advisory">{t('guidedDesign.import.blocked')}</p>
          ) : response.nextCursor ? (
            <p className="km-guided-advisory">{t('guidedDesign.import.loadAll')}</p>
          ) : !canImportChangeSet ? (
            <p className="km-guided-advisory">{t('guidedDesign.import.unavailable')}</p>
          ) : isChangeSetWorkspaceBusy ? (
            <p className="km-guided-advisory">{t('guidedDesign.import.workspaceBusy')}</p>
          ) : null}
          <button
            aria-busy={importState.status === 'busy' || undefined}
            className="primary-button"
            disabled={!importAllowed}
            type="submit"
          >
            {importState.status === 'busy'
              ? t('guidedDesign.import.loading')
              : t('guidedDesign.import.action')}
          </button>
          {importState.status === 'busy' ? (
            <LoadingProgress className="is-compact" label={t('guidedDesign.import.loading')} />
          ) : null}
          {importState.error ? (
            <>
              <PublishCommonEditorError
                domain="analysis.guidedDesign"
                field="import"
                message={t('guidedDesign.import.error')}
              />
              <p
                className="km-guided-form-error"
                ref={importErrorRef}
                role="alert"
                tabIndex={-1}
              >
                {t('guidedDesign.import.error')}
              </p>
            </>
          ) : null}
        </form>
      ) : null}
    </section>
  );
}

function NormalizedInput({
  controller,
  input,
  isChangeSetWorkspaceBusy
}: {
  controller: GuidedDesignController;
  input: GuidedDesignInput;
  isChangeSetWorkspaceBusy: boolean;
}) {
  const { t } = useLocalization();
  const values: Array<{ label: string; raw?: boolean; value: string }> = [
    { label: t('guidedDesign.inputs.kind'), value: t(`guidedDesign.kind.${input.kind}`) },
    { label: t('guidedDesign.inputs.targets'), value: String(input.targets.length) },
    { label: t('guidedDesign.inputs.pins'), value: String(input.pins.length) },
    {
      label: t('guidedDesign.inputs.fields'),
      raw: input.fieldKeys.length > 0,
      value: input.fieldKeys.length > 0
        ? input.fieldKeys.join(', ')
        : t('guidedDesign.inputs.providerFields')
    }
  ];
  const optionalValues: Array<[string, string | number | null]> = [
    ['guidedDesign.inputs.delta', input.delta],
    ['guidedDesign.inputs.multiplier', input.multiplierBasisPoints],
    ['guidedDesign.inputs.minimum', input.minimumValue],
    ['guidedDesign.inputs.maximum', input.maximumValue],
    [
      'guidedDesign.inputs.rounding',
      input.rounding ? t(`guidedDesign.rounding.${input.rounding}`) : null
    ],
    [
      'guidedDesign.inputs.archetype',
      input.archetype ? t(`guidedDesign.archetype.${input.archetype}`) : null
    ]
  ];
  for (const [labelKey, value] of optionalValues) {
    if (value !== null) values.push({ label: t(labelKey), value: String(value) });
  }
  return (
    <details className="km-guided-details">
      <summary>{t('guidedDesign.results.normalizedInputs')}</summary>
      <dl>
        {values.map((item) => (
          <div key={item.label}>
            <dt>{item.label}</dt>
            <dd data-localization-ignore={item.raw ? 'true' : undefined}>{item.value}</dd>
          </div>
        ))}
      </dl>
      {input.targets.length > 0 ? (
        <div className="km-guided-constraint-manifest">
          <h5>{t('guidedDesign.inputs.targetManifest')}</h5>
          <ul>
            {input.targets.map((target) => (
              <li key={semanticRecordKey(target)}>
                <code data-localization-ignore="true">{formatSemanticRecord(target)}</code>
              </li>
            ))}
          </ul>
        </div>
      ) : null}
      {input.pins.length > 0 ? (
        <div className="km-guided-constraint-manifest">
          <h5>{t('guidedDesign.inputs.pinManifest')}</h5>
          <ul>
            {input.pins.map((pin) => (
              <li key={mutationPinKey(pin.record, pin.fieldKey)}>
                <ExistingPinConstraint
                  controller={controller}
                  input={input}
                  isChangeSetWorkspaceBusy={isChangeSetWorkspaceBusy}
                  pin={pin}
                />
              </li>
            ))}
          </ul>
        </div>
      ) : null}
    </details>
  );
}

function ExistingPinConstraint({
  controller,
  input,
  isChangeSetWorkspaceBusy,
  pin
}: {
  controller: GuidedDesignController;
  input: GuidedDesignInput;
  isChangeSetWorkspaceBusy: boolean;
  pin: GuidedDesignPin;
}) {
  const { t } = useLocalization();
  const [canonicalValue, setCanonicalValue] = useState(pin.canonicalValue);
  const key = mutationPinKey(pin.record, pin.fieldKey);
  const sourceValueRef = useRef({ key, value: pin.canonicalValue });
  useEffect(() => {
    const previous = sourceValueRef.current;
    setCanonicalValue((current) =>
      previous.key !== key
        ? pin.canonicalValue
        : reconcileSourceBackedDraft(
            current,
            previous.value,
            pin.canonicalValue,
            Object.is
          )
    );
    sourceValueRef.current = { key, value: pin.canonicalValue };
  }, [key, pin.canonicalValue]);
  const isBusy = controller.isQuerying || isChangeSetWorkspaceBusy;
  const isValid = isCanonicalInteger(canonicalValue);
  usePublishCommonEditorError({
    domain: 'analysis.guidedDesign',
    field: `pins.${key}.canonicalValue`,
    message: isValid ? null : t('guidedDesign.diff.pinValueInvalid')
  });
  return (
    <div className="km-guided-existing-pin">
      <code data-localization-ignore="true">
        {formatSemanticRecord(pin.record)} / {pin.fieldKey}
      </code>
      <label>
        <span className="km-workbench-visually-hidden">
          {t('guidedDesign.diff.pinValue')}
        </span>
        <input
          aria-invalid={!isValid}
          maxLength={20}
          onChange={(event) => setCanonicalValue(event.currentTarget.value)}
          pattern="-?(?:0|[1-9][0-9]*)"
          spellCheck={false}
          type="text"
          value={canonicalValue}
        />
      </label>
      <button
        className="secondary-button compact-button"
        disabled={!isValid || isBusy || canonicalValue === pin.canonicalValue}
        onClick={() => void controller.previewDesign({
          ...input,
          pins: [
            ...input.pins.filter((candidate) => (
              mutationPinKey(candidate.record, candidate.fieldKey) !== key
            )),
            { ...pin, canonicalValue }
          ]
        })}
        type="button"
      >
        {t('guidedDesign.diff.updatePin')}
      </button>
      <button
        className="secondary-button compact-button"
        disabled={isBusy}
        onClick={() => void controller.previewDesign({
          ...input,
          pins: input.pins.filter((candidate) => (
            mutationPinKey(candidate.record, candidate.fieldKey) !== key
          ))
        })}
        type="button"
      >
        {t('guidedDesign.diff.removePin')}
      </button>
    </div>
  );
}

function SeedInspector({ response }: { response: GuidedDesignPreviewResponse }) {
  const { t } = useLocalization();
  const capability = response.capabilities.find(
    (candidate) => candidate.feature === 'seedInspector'
  );
  const commitments = [
    ['guidedDesign.seedInspector.schema', String(guidedDesignSchemaVersion)],
    ['guidedDesign.seedInspector.seed', response.seed ?? t('guidedDesign.results.noSeed')],
    ['guidedDesign.seedInspector.proposalId', response.proposalId],
    ['guidedDesign.seedInspector.proposalFingerprint', response.proposalFingerprint],
    [
      'guidedDesign.seedInspector.authoringContext',
      response.authoringContextFingerprint
    ],
    ['guidedDesign.seedInspector.queryFingerprint', response.queryFingerprint]
  ] as const;
  return (
    <details className="km-guided-details km-guided-seed-inspector">
      <summary>{t('guidedDesign.seedInspector.title')}</summary>
      <p>{t('guidedDesign.seedInspector.description')}</p>
      {capability ? (
        <p>
          {t(`guidedDesign.coverage.${capability.state}`)} ·{' '}
          {t(`guidedDesign.confidence.${capability.confidence}`)}
        </p>
      ) : null}
      <dl>
        {commitments.map(([labelKey, value]) => (
          <div key={labelKey}>
            <dt>{t(labelKey)}</dt>
            <dd><code data-localization-ignore="true">{value}</code></dd>
          </div>
        ))}
      </dl>
    </details>
  );
}

function AffectedRecords({
  canNavigateRecord,
  onNavigateRecord,
  records
}: {
  canNavigateRecord: (record: SemanticExploreRecordRef) => boolean;
  onNavigateRecord: (record: SemanticExploreRecordRef) => void;
  records: readonly SemanticExploreRecordRef[];
}) {
  const { t } = useLocalization();
  return (
    <details className="km-guided-details">
      <summary>{t('guidedDesign.results.affectedList', { count: records.length })}</summary>
      {records.length > 0 ? (
        <ul className="km-guided-record-list">
          {records.map((record) => (
            <li key={semanticRecordKey(record)}>
              <code data-localization-ignore="true">{formatSemanticRecord(record)}</code>
              <OpenRecordButton
                accessibleName={formatSemanticRecord(record)}
                canNavigate={canNavigateRecord(record)}
                onNavigate={() => onNavigateRecord(record)}
              />
            </li>
          ))}
        </ul>
      ) : <p>{t('guidedDesign.results.noAffected')}</p>}
    </details>
  );
}

function TargetConstraints({
  controller,
  isChangeSetWorkspaceBusy,
  onExactTargetsChange,
  response
}: {
  controller: GuidedDesignController;
  isChangeSetWorkspaceBusy: boolean;
  onExactTargetsChange: (targets: SemanticExploreRecordRef[]) => void;
  response: GuidedDesignPreviewResponse;
}) {
  const { t } = useLocalization();
  const currentRecords = useMemo(
    () => distinctRecords(response.normalizedInput.targets),
    [response.normalizedInput.targets]
  );
  const [selectedRecords, setSelectedRecords] = useState<SemanticExploreRecordRef[]>(
    () => currentRecords
  );
  const sourceRecordsRef = useRef(currentRecords);
  useEffect(() => {
    const previousSourceRecords = sourceRecordsRef.current;
    setSelectedRecords((current) => reconcileSourceBackedDraft(
      current,
      previousSourceRecords,
      currentRecords,
      sameSemanticRecordSelection
    ));
    sourceRecordsRef.current = currentRecords;
  }, [currentRecords]);
  const selectionChanged = selectedRecords.length !== currentRecords.length ||
    selectedRecords.some((record, index) => (
      semanticRecordKey(record) !== semanticRecordKey(currentRecords[index]!)
    ));
  const rerun = () => {
    const selectedKeys = new Set(selectedRecords.map(semanticRecordKey));
    onExactTargetsChange(selectedRecords);
    void controller.previewDesign({
      ...response.normalizedInput,
      pins: selectedRecords.length === 0
        ? []
        : response.normalizedInput.pins.filter((pin) => (
          selectedKeys.has(pinOwningTargetKey(pin.record))
        )),
      targets: selectedRecords
    });
  };
  const moveRecord = (index: number, direction: -1 | 1) => {
    const destination = index + direction;
    if (destination < 0 || destination >= selectedRecords.length) return;
    setSelectedRecords((current) => {
      const next = [...current];
      [next[index], next[destination]] = [next[destination]!, next[index]!];
      return next;
    });
  };
  return (
    <fieldset className="km-guided-targets">
      <legend>{t('guidedDesign.targets.title')}</legend>
      <p>{t('guidedDesign.targets.description')}</p>
      {selectedRecords.length > 0 ? (
        <ol className="km-guided-target-list">
          {selectedRecords.map((record, index) => {
            const key = semanticRecordKey(record);
            const accessibleName = `${guidedDesignRecordLabel(response, record)}, ${formatSemanticRecord(record)}`;
            return (
              <li key={key}>
                <span>
                  <strong data-localization-ignore="true">
                    {guidedDesignRecordLabel(response, record)}
                  </strong>
                  <code data-localization-ignore="true">{formatSemanticRecord(record)}</code>
                </span>
                <div className="km-guided-target-order-actions">
                  <button
                    aria-label={t('guidedDesign.selection.moveUpNamed', {
                      name: accessibleName
                    })}
                    className="secondary-button compact-button"
                    disabled={index === 0}
                    onClick={() => moveRecord(index, -1)}
                    type="button"
                  >
                    <ArrowUp aria-hidden="true" size={14} />
                  </button>
                  <button
                    aria-label={t('guidedDesign.selection.moveDownNamed', {
                      name: accessibleName
                    })}
                    className="secondary-button compact-button"
                    disabled={
                      index === selectedRecords.length - 1
                    }
                    onClick={() => moveRecord(index, 1)}
                    type="button"
                  >
                    <ArrowDown aria-hidden="true" size={14} />
                  </button>
                  <button
                    aria-label={t('guidedDesign.selection.removeNamed', {
                      name: accessibleName
                    })}
                    className="secondary-button compact-button"
                    onClick={() => setSelectedRecords((current) => (
                      current.filter((candidate) => semanticRecordKey(candidate) !== key)
                    ))}
                    type="button"
                  >
                    <Trash2 aria-hidden="true" size={14} />
                  </button>
                </div>
              </li>
            );
          })}
        </ol>
      ) : (
        <p className="km-workbench-empty">{t('guidedDesign.selection.none')}</p>
      )}
      <small>{selectedRecords.length === 0
        ? t('guidedDesign.targets.returnToSelection')
        : t('guidedDesign.targets.selected', { count: selectedRecords.length })}</small>
      <div className="km-guided-selection-actions">
        <button
          className="secondary-button compact-button"
          disabled={!selectionChanged || controller.isQuerying || isChangeSetWorkspaceBusy}
          onClick={rerun}
          type="button"
        >
          {t('guidedDesign.targets.rerun')}
        </button>
      </div>
    </fieldset>
  );
}

function Findings({
  canNavigateRecord,
  findings,
  onNavigateRecord
}: {
  canNavigateRecord: (record: SemanticExploreRecordRef) => boolean;
  findings: GuidedDesignPreviewResponse['findings'];
  onNavigateRecord: (record: SemanticExploreRecordRef) => void;
}) {
  const { t } = useLocalization();
  const [severityFilter, setSeverityFilter] = useState('all');
  const [resultOrder, setResultOrder] = useState<'severity' | 'title' | 'confidence'>('severity');
  const severities = useMemo(
    () => [...new Set(findings.map((finding) => finding.severity))].sort(),
    [findings]
  );
  useEffect(() => {
    if (severityFilter !== 'all' && !severities.some((severity) => (
      severity === severityFilter
    ))) {
      setSeverityFilter('all');
    }
  }, [severities, severityFilter]);
  const visibleFindings = useMemo(() => [...findings]
    .filter((finding) => severityFilter === 'all' || finding.severity === severityFilter)
    .sort((left, right) => {
      if (resultOrder === 'title') return left.title.localeCompare(right.title);
      if (resultOrder === 'confidence') {
        return left.confidence.localeCompare(right.confidence) || left.title.localeCompare(right.title);
      }
      return diagnosticSeverityPriority(right.severity) -
        diagnosticSeverityPriority(left.severity) ||
        left.title.localeCompare(right.title);
    }), [findings, resultOrder, severityFilter]);
  return (
    <section aria-labelledby="guided-design-findings-title" className="km-guided-findings">
      <h4 id="guided-design-findings-title">{t('guidedDesign.findings.title')}</h4>
      {findings.length > 0 ? (
        <div className="km-guided-result-controls">
          <div className="km-searchable-select-field km-guided-result-field">
            <label htmlFor="guided-design-findings-status-filter">
              <span>{t('analysisPresentation.controls.status')}</span>
            </label>
            <SearchableOptionInput
              ariaLabel={t('analysisPresentation.controls.status')}
              data-km-source-site="guided-design-findings-status-filter"
              disabled={false}
              id="guided-design-findings-status-filter"
              isFiniteCatalog
              onChange={setSeverityFilter}
              options={[
                { label: t('analysisPresentation.controls.allResults'), value: 'all' },
                ...severities.map((severity) => ({
                  label: t(`guidedDesign.severity.${severity}`),
                  value: severity
                }))
              ]}
              value={severityFilter}
            />
          </div>
          <div className="km-searchable-select-field km-guided-result-field">
            <label htmlFor="guided-design-findings-sort">
              <span>{t('analysisPresentation.controls.sort')}</span>
            </label>
            <SearchableOptionInput
              ariaLabel={t('analysisPresentation.controls.sort')}
              data-km-source-site="guided-design-findings-sort"
              disabled={false}
              id="guided-design-findings-sort"
              isFiniteCatalog
              onChange={(value) => setResultOrder(value as typeof resultOrder)}
              options={[
                { label: t('analysisPresentation.controls.status'), value: 'severity' },
                { label: t('analysisPresentation.controls.record'), value: 'title' },
                {
                  label: t('analysisPresentation.controls.confidence'),
                  value: 'confidence'
                }
              ]}
              value={resultOrder}
            />
          </div>
        </div>
      ) : null}
      {findings.length > 0 ? (
        <ul>
          {visibleFindings.map((finding) => (
            <li data-severity={finding.severity} key={finding.findingId}>
              <div>
                <span>{t(`guidedDesign.severity.${finding.severity}`)}</span>
                <span>{t(`guidedDesign.confidence.${finding.confidence}`)}</span>
              </div>
              <strong data-localization-ignore="true">{finding.title}</strong>
              <p data-localization-ignore="true">{finding.summary}</p>
              {finding.record ? (
                <OpenRecordButton
                  accessibleName={`${finding.title}, ${formatSemanticRecord(finding.record)}`}
                  canNavigate={canNavigateRecord(finding.record)}
                  onNavigate={() => onNavigateRecord(finding.record!)}
                />
              ) : null}
            </li>
          ))}
        </ul>
      ) : <p>{t('guidedDesign.findings.empty')}</p>}
      {findings.length > 0 && visibleFindings.length === 0 ? (
        <p>{t('analysisPresentation.controls.noMatches')}</p>
      ) : null}
    </section>
  );
}

function MutationDiff({
  canNavigateRecord,
  controller,
  isChangeSetWorkspaceBusy,
  mutations,
  onNavigateRecord
}: {
  canNavigateRecord: (record: SemanticExploreRecordRef) => boolean;
  controller: GuidedDesignController;
  isChangeSetWorkspaceBusy: boolean;
  mutations: GuidedDesignPreviewResponse['mutations'];
  onNavigateRecord: (record: SemanticExploreRecordRef) => void;
}) {
  const { t, translateLiteral } = useLocalization();
  const [recordFilter, setRecordFilter] = useState('all');
  const [fieldFilter, setFieldFilter] = useState('all');
  const [statusFilter, setStatusFilter] = useState<'all' | 'pinned' | 'proposed'>('all');
  const [resultOrder, setResultOrder] = useState<'record' | 'field' | 'status'>('record');
  const records = useMemo(() => {
    const byKey = new Map(mutations.map((mutation) => [
      semanticRecordKey(mutation.record),
      { label: mutation.recordLabel, record: mutation.record }
    ]));
    const labelCounts = new Map<string, number>();
    for (const { label } of byKey.values()) {
      labelCounts.set(label, (labelCounts.get(label) ?? 0) + 1);
    }
    return [...byKey].map(([key, value]) => ({
      key,
      label: (labelCounts.get(value.label) ?? 0) > 1
        ? `${value.label} - ${formatSemanticRecord(value.record)}`
        : value.label
    })).sort((left, right) => left.label.localeCompare(right.label));
  }, [mutations]);
  const fields = useMemo(() => [...new Map(mutations.map((mutation) => [
    mutation.fieldKey,
    mutation.fieldLabel
  ])).entries()].sort((left, right) => left[1].localeCompare(right[1])), [mutations]);
  useEffect(() => {
    if (recordFilter !== 'all' && !records.some(({ key }) => key === recordFilter)) {
      setRecordFilter('all');
    }
    if (fieldFilter !== 'all' && !fields.some(([key]) => key === fieldFilter)) {
      setFieldFilter('all');
    }
    if (
      statusFilter !== 'all' &&
      !mutations.some((mutation) => (statusFilter === 'pinned') === mutation.pinned)
    ) setStatusFilter('all');
  }, [fieldFilter, fields, mutations, recordFilter, records, statusFilter]);
  const visibleMutations = useMemo(() => [...mutations]
    .filter((mutation) => (
      (recordFilter === 'all' || semanticRecordKey(mutation.record) === recordFilter) &&
      (fieldFilter === 'all' || mutation.fieldKey === fieldFilter) &&
      (statusFilter === 'all' || (statusFilter === 'pinned') === mutation.pinned)
    ))
    .sort((left, right) => {
      if (resultOrder === 'field') {
        return left.fieldLabel.localeCompare(right.fieldLabel) ||
          left.recordLabel.localeCompare(right.recordLabel);
      }
      if (resultOrder === 'status') {
        return Number(right.pinned) - Number(left.pinned) ||
          left.recordLabel.localeCompare(right.recordLabel);
      }
      return left.recordLabel.localeCompare(right.recordLabel) ||
        left.fieldLabel.localeCompare(right.fieldLabel);
    }), [fieldFilter, mutations, recordFilter, resultOrder, statusFilter]);
  const groups = groupMutationsByRecord(visibleMutations);
  return (
    <section aria-labelledby="guided-design-diff-title" className="km-guided-diff">
      <h4 id="guided-design-diff-title">{t('guidedDesign.diff.title')}</h4>
      {mutations.length > 0 ? (
        <div className="km-guided-result-controls">
          <div className="km-searchable-select-field km-guided-result-field">
            <label htmlFor="guided-design-diff-record-filter">
              <span>{t('analysisPresentation.controls.record')}</span>
            </label>
            <SearchableOptionInput
              ariaLabel={t('analysisPresentation.controls.record')}
              data-localization-ignore="true"
              data-km-source-site="guided-design-diff-record-filter"
              disabled={false}
              id="guided-design-diff-record-filter"
              isFiniteCatalog
              localizeOptions={false}
              onChange={setRecordFilter}
              options={[
                { label: t('analysisPresentation.controls.allRecords'), value: 'all' },
                ...records.map(({ key, label }) => ({ label, value: key }))
              ]}
              value={recordFilter}
            />
          </div>
          <div className="km-searchable-select-field km-guided-result-field">
            <label htmlFor="guided-design-diff-field-filter">
              <span>{t('analysisPresentation.controls.field')}</span>
            </label>
            <SearchableOptionInput
              ariaLabel={t('analysisPresentation.controls.field')}
              data-localization-ignore="true"
              data-km-source-site="guided-design-diff-field-filter"
              disabled={false}
              id="guided-design-diff-field-filter"
              isFiniteCatalog
              localizeOptions={false}
              onChange={setFieldFilter}
              options={[
                { label: t('analysisPresentation.controls.allFields'), value: 'all' },
                ...fields.map(([key, label]) => ({ label, value: key }))
              ]}
              value={fieldFilter}
            />
          </div>
          <div className="km-searchable-select-field km-guided-result-field">
            <label htmlFor="guided-design-diff-status-filter">
              <span>{t('analysisPresentation.controls.status')}</span>
            </label>
            <SearchableOptionInput
              ariaLabel={t('analysisPresentation.controls.status')}
              data-km-source-site="guided-design-diff-status-filter"
              disabled={false}
              id="guided-design-diff-status-filter"
              isFiniteCatalog
              onChange={(value) => setStatusFilter(value as typeof statusFilter)}
              options={[
                { label: t('analysisPresentation.controls.allResults'), value: 'all' },
                { label: t('guidedDesign.diff.pinned'), value: 'pinned' },
                { label: t('guidedDesign.diff.proposed'), value: 'proposed' }
              ]}
              value={statusFilter}
            />
          </div>
          <div className="km-searchable-select-field km-guided-result-field">
            <label htmlFor="guided-design-diff-sort">
              <span>{t('analysisPresentation.controls.sort')}</span>
            </label>
            <SearchableOptionInput
              ariaLabel={t('analysisPresentation.controls.sort')}
              data-km-source-site="guided-design-diff-sort"
              disabled={false}
              id="guided-design-diff-sort"
              isFiniteCatalog
              onChange={(value) => setResultOrder(value as typeof resultOrder)}
              options={[
                { label: t('analysisPresentation.controls.record'), value: 'record' },
                { label: t('analysisPresentation.controls.field'), value: 'field' },
                { label: t('analysisPresentation.controls.status'), value: 'status' }
              ]}
              value={resultOrder}
            />
          </div>
        </div>
      ) : null}
      {mutations.length > 0 ? (
        <div
          aria-label={t('guidedDesign.diff.title')}
          className="km-guided-table-scroll"
          role="region"
          tabIndex={0}
        >
          <table>
            <thead>
              <tr>
                <th>{t('guidedDesign.diff.record')}</th>
                <th>{t('guidedDesign.diff.field')}</th>
                <th>{t('guidedDesign.diff.before')}</th>
                <th>{t('guidedDesign.diff.after')}</th>
                <th>{t('guidedDesign.diff.status')}</th>
                <th><span className="km-workbench-visually-hidden">{t('guidedDesign.diff.actions')}</span></th>
              </tr>
            </thead>
            <tbody>
              {groups.flatMap((group) => group.mutations.map((mutation, index) => (
                <tr key={mutation.mutationId}>
                  {index === 0 ? (
                    <td data-localization-ignore="true" rowSpan={group.mutations.length}>
                      <strong>{mutation.recordLabel}</strong>
                      <TechnicalDetails summary={translateLiteral('Technical details')}>
                        <code>{formatSemanticRecord(mutation.record)}</code>
                      </TechnicalDetails>
                    </td>
                  ) : null}
                  <td data-localization-ignore="true">
                    {mutation.fieldLabel}
                    <TechnicalDetails summary={translateLiteral('Technical details')}>
                      <code>{mutation.fieldKey}</code>
                    </TechnicalDetails>
                  </td>
                  <td data-localization-ignore="true">{mutation.before.displayValue}</td>
                  <td data-localization-ignore="true">{mutation.after.displayValue}</td>
                  <td>{mutation.pinned
                    ? t('guidedDesign.diff.pinned')
                    : t('guidedDesign.diff.proposed')}</td>
                  <td>
                    <div className="km-guided-row-actions">
                      <MutationPinControl
                        controller={controller}
                        isChangeSetWorkspaceBusy={isChangeSetWorkspaceBusy}
                        mutation={mutation}
                      />
                      <MutationOpenRecordButton
                        canNavigateRecord={canNavigateRecord}
                        fieldKey={mutation.fieldKey}
                        onNavigateRecord={onNavigateRecord}
                        record={mutation.record}
                      />
                    </div>
                  </td>
                </tr>
              )))}
            </tbody>
          </table>
        </div>
      ) : <p>{t('guidedDesign.diff.empty')}</p>}
      {mutations.length > 0 && visibleMutations.length === 0 ? (
        <p>{t('analysisPresentation.controls.noMatches')}</p>
      ) : null}
    </section>
  );
}

function MutationPinControl({
  controller,
  isChangeSetWorkspaceBusy,
  mutation
}: {
  controller: GuidedDesignController;
  isChangeSetWorkspaceBusy: boolean;
  mutation: GuidedDesignMutation;
}) {
  const { t } = useLocalization();
  const input = controller.preview.data?.normalizedInput ?? null;
  const existingPin = input && mutation.pinRecord && mutation.pinFieldKey
    ? input.pins.find((pin) => mutationPinKey(pin.record, pin.fieldKey) === (
      mutationPinKey(mutation.pinRecord!, mutation.pinFieldKey!)
    )) ?? null
    : null;
  const suggestedValue = existingPin?.canonicalValue ?? mutation.after.canonicalValue ?? '';
  const [canonicalValue, setCanonicalValue] = useState(suggestedValue);
  const sourceValueRef = useRef({
    mutationId: mutation.mutationId,
    value: suggestedValue
  });
  useEffect(() => {
    const previous = sourceValueRef.current;
    setCanonicalValue((current) =>
      previous.mutationId !== mutation.mutationId
        ? suggestedValue
        : reconcileSourceBackedDraft(
            current,
            previous.value,
            suggestedValue,
            Object.is
          )
    );
    sourceValueRef.current = {
      mutationId: mutation.mutationId,
      value: suggestedValue
    };
  }, [mutation.mutationId, suggestedValue]);
  const hasPinTarget = Boolean(mutation.pinRecord && mutation.pinFieldKey && input);
  const isValid = isCanonicalInteger(canonicalValue);
  const diagnosticPinKey = mutation.pinRecord && mutation.pinFieldKey
    ? mutationPinKey(mutation.pinRecord, mutation.pinFieldKey)
    : mutation.mutationId;
  usePublishCommonEditorError({
    domain: 'analysis.guidedDesign',
    field: `pins.${diagnosticPinKey}.canonicalValue`,
    message: hasPinTarget && !isValid ? t('guidedDesign.diff.pinValueInvalid') : null
  });
  if (!mutation.pinRecord || !mutation.pinFieldKey || !input) {
    return <span className="km-guided-shared-effect">{t('guidedDesign.diff.sharedEffect')}</span>;
  }
  const pinKey = mutationPinKey(mutation.pinRecord, mutation.pinFieldKey);
  const accessibleMutationName = `${formatSemanticRecord(mutation.pinRecord)}, ${mutation.pinFieldKey}`;
  const isBusy = controller.isQuerying || isChangeSetWorkspaceBusy;
  const pinLimitReached = !existingPin && input.pins.length >= guidedDesignMaximumPins;
  const writePin = () => {
    if (!isValid || isBusy || pinLimitReached) return;
    void controller.previewDesign({
      ...input,
      pins: [
        ...input.pins.filter((pin) => mutationPinKey(pin.record, pin.fieldKey) !== pinKey),
        {
          canonicalValue,
          fieldKey: mutation.pinFieldKey!,
          record: mutation.pinRecord!
        }
      ]
    });
  };
  const removePin = () => {
    if (!existingPin || isBusy) return;
    void controller.previewDesign({
      ...input,
      pins: input.pins.filter((pin) => mutationPinKey(pin.record, pin.fieldKey) !== pinKey)
    });
  };
  return (
    <div className="km-guided-pin-control">
      <label>
        <span className="km-workbench-visually-hidden">
          {t('guidedDesign.diff.pinValue')}
        </span>
        <input
          aria-label={`${t('guidedDesign.diff.pinValue')}: ${accessibleMutationName}`}
          aria-invalid={!isValid}
          maxLength={20}
          onChange={(event) => setCanonicalValue(event.currentTarget.value)}
          pattern="-?(?:0|[1-9][0-9]*)"
          spellCheck={false}
          type="text"
          value={canonicalValue}
        />
      </label>
      <button
        aria-label={`${existingPin ? t('guidedDesign.diff.updatePin') : t('guidedDesign.diff.pin')}: ${accessibleMutationName}`}
        className="secondary-button compact-button"
        disabled={!isValid || isBusy || pinLimitReached}
        onClick={writePin}
        type="button"
      >
        {existingPin ? t('guidedDesign.diff.updatePin') : t('guidedDesign.diff.pin')}
      </button>
      {existingPin ? (
        <button
          aria-label={`${t('guidedDesign.diff.unpin')}: ${accessibleMutationName}`}
          className="secondary-button compact-button"
          disabled={isBusy}
          onClick={removePin}
          type="button"
        >
          {t('guidedDesign.diff.unpin')}
        </button>
      ) : null}
    </div>
  );
}

function CanonicalExports({
  exports
}: {
  exports: GuidedDesignPreviewResponse['exports'];
}) {
  const { t } = useLocalization();
  const [status, setStatus] = useState<'idle' | 'copied' | 'downloaded' | 'error'>('idle');
  const values = [exports.spoiler, exports.race].filter(
    (value): value is GuidedDesignCanonicalExport => value !== null
  );
  const errorMessage = status === 'error'
    ? t('guidedDesign.exports.status.error')
    : null;
  usePublishCommonEditorError({
    domain: 'analysis.guidedDesign',
    field: 'exports',
    message: errorMessage
  });
  if (values.length === 0) return null;
  const copy = async (value: GuidedDesignCanonicalExport) => {
    try {
      await navigator.clipboard.writeText(value.content);
      setStatus('copied');
    } catch {
      setStatus('error');
    }
  };
  const download = (value: GuidedDesignCanonicalExport) => {
    try {
      const url = URL.createObjectURL(new Blob([value.content], { type: value.mediaType }));
      const anchor = document.createElement('a');
      anchor.download = value.suggestedFileName;
      anchor.href = url;
      anchor.click();
      URL.revokeObjectURL(url);
      setStatus('downloaded');
    } catch {
      setStatus('error');
    }
  };
  return (
    <section aria-labelledby="guided-design-exports-title" className="km-guided-exports">
      <h4 id="guided-design-exports-title">{t('guidedDesign.exports.title')}</h4>
      <p>{t('guidedDesign.exports.description')}</p>
      <div>
        {values.map((value) => (
          <article key={value.kind}>
            <strong>{t(`guidedDesign.exports.${value.kind}`)}</strong>
            <code data-localization-ignore="true">{value.sha256}</code>
            <div>
              <button
                className="secondary-button compact-button"
                onClick={() => void copy(value)}
                type="button"
              >
                <Clipboard aria-hidden="true" size={14} />
                <span>{t('guidedDesign.exports.copy')}</span>
              </button>
              <button
                className="secondary-button compact-button"
                onClick={() => download(value)}
                type="button"
              >
                <Download aria-hidden="true" size={14} />
                <span>{t('guidedDesign.exports.download')}</span>
              </button>
            </div>
          </article>
        ))}
      </div>
      <p aria-live="polite" role={status === 'error' ? 'alert' : 'status'}>
        {status === 'idle'
          ? ''
          : errorMessage ?? t(`guidedDesign.exports.status.${status}`)}
      </p>
    </section>
  );
}

function ImportReceipt({
  diagnostics,
  onOpenChanges
}: {
  diagnostics: readonly ApiDiagnostic[];
  onOpenChanges: () => void;
}) {
  const { t } = useLocalization();
  const receiptRef = useRef<HTMLDivElement | null>(null);
  useEffect(() => {
    receiptRef.current?.focus({ preventScroll: true });
  }, []);
  return (
    <div
      aria-live="polite"
      className="km-guided-import-success"
      ref={receiptRef}
      role="status"
      tabIndex={-1}
    >
      <p>{t('guidedDesign.import.success')}</p>
      <button className="secondary-button compact-button" onClick={onOpenChanges} type="button">
        <ExternalLink aria-hidden="true" size={15} />
        <span>{t('guidedDesign.import.openChanges')}</span>
      </button>
      <DiagnosticList diagnostics={diagnostics} />
    </div>
  );
}

function DiagnosticList({
  diagnostics
}: {
  diagnostics: readonly ApiDiagnostic[];
}) {
  const { t, translateLiteral } = useLocalization();
  const diagnosticNavigation = useDiagnosticNavigation();
  usePublishCommonEditorDiagnostics(diagnostics);
  if (diagnostics.length === 0) return null;
  const formatMessage = (diagnostic: ApiDiagnostic) => (
    safeDiagnosticMessage(diagnostic.message)
      ? formatDiagnosticSummary(diagnostic, translateLiteral, t)
      : t('guidedDesign.diagnostics.redacted')
  );
  const presentedMessage = (diagnostic: ApiDiagnostic) => (
    presentationDiagnosticMessage(diagnostic, diagnostics, formatMessage)
  );
  const presentedSeverity = (diagnostic: ApiDiagnostic) => (
    presentationDiagnosticSeverity(diagnostic, diagnostics, formatMessage)
  );
  const grouped = groupDiagnosticsForPresentation(
    diagnostics,
    (diagnostic) => [presentedSeverity(diagnostic), presentedMessage(diagnostic)],
    diagnosticTechnicalIdentity,
    (diagnostic) => diagnosticSeverityPriority(presentedSeverity(diagnostic))
  );
  const primaryAction = [...diagnostics]
    .sort((left, right) => (
      diagnosticSeverityPriority(right.severity) - diagnosticSeverityPriority(left.severity)
    ))
    .map((diagnostic) => diagnosticNavigation.resolveAction(diagnostic))
    .find((action) => action !== null);
  return (
    <section aria-label={t('guidedDesign.diagnostics.title')} className="km-guided-diagnostics">
      {primaryAction ? (
        <div className="km-analysis-diagnostic-action">
          <button
            className="secondary-button compact-button"
            onClick={() => diagnosticNavigation.navigate(primaryAction.location)}
            type="button"
          >
            {t('diagnostics.openAction', {
              target: translateLiteral(primaryAction.targetLabel)
            })}
          </button>
        </div>
      ) : null}
      <ul>
        {grouped.slice(0, 50).map(({ count, diagnostics: identities, key }) => {
          const diagnostic = identities[0]!.diagnostic;
          return (
          <li data-severity={presentedSeverity(diagnostic)} key={key}>
            <span>
              <span>
                <DiagnosticSeverityText severity={presentedSeverity(diagnostic)} />
                {presentedMessage(diagnostic)}
              </span>
              <OccurrenceCount count={count} />
              <ReportableDiagnosticIssuesLink
                messages={identities.map((identity) => identity.diagnostic.message)}
              />
            </span>
            <DiagnosticTechnicalDetails
              diagnostics={identities}
              summary={translateLiteral('Technical details')}
            />
          </li>
          );
        })}
      </ul>
      {grouped.length > 50 ? <p>{t('guidedDesign.diagnostics.bounded')}</p> : null}
    </section>
  );
}

function SummaryValue({
  labelKey,
  raw,
  value
}: {
  labelKey: string;
  raw?: boolean;
  value: string;
}) {
  const { t } = useLocalization();
  return (
    <div>
      <dt>{t(labelKey)}</dt>
      <dd data-localization-ignore={raw ? 'true' : undefined}>{value}</dd>
    </div>
  );
}

function OpenRecordButton({
  accessibleName,
  canNavigate,
  onNavigate
}: {
  accessibleName: string;
  canNavigate: boolean;
  onNavigate: () => void;
}) {
  const { t } = useLocalization();
  return (
    <button
      aria-label={`${t('guidedDesign.navigation.open')}: ${accessibleName}`}
      className="secondary-button compact-button"
      disabled={!canNavigate}
      onClick={onNavigate}
      title={!canNavigate ? t('guidedDesign.navigation.unavailable') : undefined}
      type="button"
    >
      <ExternalLink aria-hidden="true" size={14} />
      <span>{t('guidedDesign.navigation.open')}</span>
    </button>
  );
}

function MutationOpenRecordButton({
  canNavigateRecord,
  fieldKey,
  onNavigateRecord,
  record
}: {
  canNavigateRecord: (record: SemanticExploreRecordRef) => boolean;
  fieldKey: string;
  onNavigateRecord: (record: SemanticExploreRecordRef) => void;
  record: SemanticExploreRecordRef;
}) {
  const destination = guidedDesignMutationNavigationRecord(record, fieldKey);
  return (
    <OpenRecordButton
      accessibleName={`${formatSemanticRecord(destination ?? record)}, ${fieldKey}`}
      canNavigate={destination !== null && canNavigateRecord(destination)}
      onNavigate={() => {
        if (destination) onNavigateRecord(destination);
      }}
    />
  );
}

function NumberControl({
  labelKey,
  maximum,
  minimum,
  onChange,
  value
}: {
  labelKey: string;
  maximum: number;
  minimum: number;
  onChange: (value: string) => void;
  value: string;
}) {
  const { t } = useLocalization();
  return (
    <label>
      <span>{t(labelKey)}</span>
      <input
        max={maximum}
        min={minimum}
        onChange={(event) => onChange(event.currentTarget.value)}
        required
        step={1}
        type="number"
        value={value}
      />
    </label>
  );
}

function GuidedFieldSelector({
  catalog,
  onChange,
  selectedValues
}: {
  catalog: GuidedDesignFieldCatalog;
  onChange: (values: readonly string[]) => void;
  selectedValues: readonly string[];
}) {
  const { t } = useLocalization();
  const selected = new Set(
    selectedValues.filter((value) => catalog.fieldKeys.includes(value))
  );
  const toggle = (fieldKey: string, checked: boolean) => {
    if (catalog.selectionMode === 'fixed') return;
    const next = new Set(selected);
    if (checked) next.add(fieldKey);
    else if (next.size > catalog.minimumSelections) next.delete(fieldKey);
    onChange(catalog.fieldKeys.filter((candidate) => next.has(candidate)));
  };
  return (
    <div className="km-guided-control-wide km-guided-field-selector">
      <span>{t('guidedDesign.inputs.fields')}</span>
      <details>
        <summary>
          {t('guidedDesign.inputs.fieldsSelected', { count: selected.size })}
        </summary>
        <ul aria-label={t('guidedDesign.inputs.fields')}>
          {catalog.fieldKeys.map((fieldKey) => {
            const checked = selected.has(fieldKey);
            return (
              <li key={fieldKey}>
                <label>
                  <input
                    checked={checked}
                    className="km-choice-control"
                    disabled={
                      catalog.selectionMode === 'fixed' ||
                      checked && selected.size <= catalog.minimumSelections
                    }
                    onChange={(event) => toggle(fieldKey, event.currentTarget.checked)}
                    type="checkbox"
                  />
                  <span>{t(`guidedDesign.field.${fieldKey}`)}</span>
                </label>
              </li>
            );
          })}
        </ul>
      </details>
      <small>
        {catalog.selectionMode === 'fixed'
          ? t('guidedDesign.inputs.fieldsFixedHelp')
          : t('guidedDesign.inputs.fieldsSubsetHelp', {
              minimum: catalog.minimumSelections
            })}
      </small>
    </div>
  );
}

function SelectControl({
  id,
  labelKey,
  onChange,
  options,
  translationPrefix,
  value
}: {
  id: string;
  labelKey: string;
  onChange: (value: string) => void;
  options: readonly string[];
  translationPrefix: string;
  value: string;
}) {
  const { t } = useLocalization();
  return (
    <div className="km-searchable-select-field km-guided-control-field">
      <label htmlFor={id}>
        <span>{t(labelKey)}</span>
      </label>
      <SearchableOptionInput
        ariaLabel={t(labelKey)}
        data-km-source-site="guided-design-select-control"
        disabled={false}
        id={id}
        isFiniteCatalog
        onChange={onChange}
        options={options.map((option) => ({
          label: t(`${translationPrefix}.${option}`),
          value: option
        }))}
        value={value}
      />
    </div>
  );
}

function StatusPanel({
  error,
  kind,
  onRetry
}: {
  error?: GuidedDesignQueryError | null;
  kind: 'loading' | 'error';
  onRetry?: () => void;
}) {
  const { t } = useLocalization();
  const errorMessage = kind === 'error' ? t(queryErrorKey(error)) : null;
  usePublishCommonEditorError({ domain: 'analysis.guidedDesign', message: errorMessage });
  if (kind === 'loading') {
    return (
      <div className="km-guided-status">
        <LoadingProgress label={t('guidedDesign.capabilities.loading')} />
      </div>
    );
  }
  return (
    <div aria-live="polite" className="km-guided-status" role="alert">
      <p>{errorMessage}</p>
      {onRetry ? <button onClick={onRetry} type="button">{t('guidedDesign.retry')}</button> : null}
    </div>
  );
}

function InlineError({
  error,
  onRetry
}: {
  error: GuidedDesignQueryError | null;
  onRetry: () => void;
}) {
  const { t } = useLocalization();
  const message = t(queryErrorKey(error));
  usePublishCommonEditorError({ domain: 'analysis.guidedDesign', message });
  return (
    <div className="km-guided-inline-error" role="alert">
      <span>{message}</span>
      <button className="secondary-button compact-button" onClick={onRetry} type="button">
        {t('guidedDesign.retry')}
      </button>
    </div>
  );
}

function buildInput(options: {
  archetype: GuidedDesignTrainerArchetype;
  delta: string;
  fieldKeys: string;
  kind: GuidedDesignProposalKind | null;
  maximumValue: string;
  minimumValue: string;
  multiplierBasisPoints: string;
  rounding: GuidedDesignRounding;
  seed: string;
}) {
  if (!options.kind) return guidedDesignInputSchema.safeParse(null);
  const input: GuidedDesignInput = {
    archetype: options.kind === 'trainerEvArchetype' ? options.archetype : null,
    delta: usesDelta(options.kind) ? parseInteger(options.delta) : null,
    fieldKeys: options.fieldKeys.trim() ? parseFieldKeys(options.fieldKeys) : [],
    kind: options.kind,
    maximumValue: options.kind === 'evolutionLevelClamp'
      ? parseInteger(options.maximumValue)
      : null,
    minimumValue: options.kind === 'evolutionLevelClamp'
      ? parseInteger(options.minimumValue)
      : null,
    multiplierBasisPoints: usesMultiplier(options.kind)
      ? parseInteger(options.multiplierBasisPoints)
      : null,
    pins: [],
    rounding: usesMultiplier(options.kind) ? options.rounding : null,
    seed: options.kind === 'pokemonBaseStatShuffle'
      ? options.seed.trim().toLocaleLowerCase() || null
      : null,
    targets: []
  };
  return guidedDesignInputSchema.safeParse(input);
}

function parseInteger(value: string) {
  if (!/^-?(?:0|[1-9][0-9]*)$/u.test(value)) return Number.NaN;
  return Number(value);
}

function parseFieldKeys(value: string) {
  return value.split(',').map((candidate) => candidate.trim()).filter(Boolean);
}

function usesDelta(kind: GuidedDesignProposalKind) {
  return kind === 'trainerLevelAdjustment' || kind === 'encounterLevelAdjustment';
}

function usesMultiplier(kind: GuidedDesignProposalKind) {
  return kind === 'encounterWeightScale' || kind === 'economyPrimaryPriceScale';
}

function semanticRecordKey(record: SemanticExploreRecordRef) {
  return JSON.stringify([
    record.gameFamily,
    record.domain,
    record.recordKind.key,
    record.recordKind.schemaVersion,
    record.recordId,
    record.subrecordId
  ]);
}

function sameSemanticRecordSelection(
  left: readonly SemanticExploreRecordRef[],
  right: readonly SemanticExploreRecordRef[]
) {
  return left.length === right.length && left.every(
    (record, index) => semanticRecordKey(record) === semanticRecordKey(right[index]!)
  );
}

function guidedDesignRecordLabel(
  response: GuidedDesignPreviewResponse,
  record: SemanticExploreRecordRef
) {
  const key = semanticRecordKey(record);
  return response.eligibleTargets.find((option) => (
    semanticRecordKey(option.record) === key
  ))?.recordLabel ?? response.mutations.find((mutation) => (
    semanticRecordKey(mutation.record) === key
  ))?.recordLabel ?? formatSemanticRecord(record);
}

function groupMutationsByRecord(mutations: GuidedDesignPreviewResponse['mutations']) {
  const groups = new Map<string, GuidedDesignMutation[]>();
  for (const mutation of mutations) {
    const key = semanticRecordKey(mutation.record);
    const group = groups.get(key);
    if (group) group.push(mutation);
    else groups.set(key, [mutation]);
  }
  return [...groups].map(([key, groupedMutations]) => ({
    key,
    mutations: groupedMutations
  }));
}

function semanticRevisionKey(revision: SemanticExploreRevision) {
  return JSON.stringify([
    revision.projectId,
    revision.gameFamily,
    revision.generation,
    revision.fingerprint
  ]);
}

function distinctRecords(records: readonly SemanticExploreRecordRef[]) {
  const seen = new Set<string>();
  return records.filter((record) => {
    const key = semanticRecordKey(record);
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}

function pinOwningTargetKey(record: SemanticExploreRecordRef) {
  if (
    record.domain === 'workflow.pokemon' &&
    record.recordKind.key === 'pokemon-personal' &&
    record.recordKind.schemaVersion === 1 &&
    /^evolution-slot:(0|[1-9][0-9]*)$/u.test(record.subrecordId ?? '')
  ) {
    return semanticRecordKey({ ...record, subrecordId: null });
  }
  return semanticRecordKey(record);
}

export function guidedDesignMutationNavigationRecord(
  record: SemanticExploreRecordRef,
  fieldKey: string
): SemanticExploreRecordRef | null {
  if (record.domain === 'workflow.pokemon' && record.recordKind.key === 'pokemon-personal') {
    if (record.subrecordId === null) return fieldKey === 'level' ? null : record;
    return fieldKey === 'level' &&
      /^evolution-slot:(0|[1-9][0-9]*)$/u.test(record.subrecordId) &&
      record.recordKind.schemaVersion === 1
      ? record
      : null;
  }
  return record;
}

function mutationPinKey(record: SemanticExploreRecordRef, fieldKey: string) {
  return JSON.stringify([semanticRecordKey(record), fieldKey]);
}

function isCanonicalInteger(value: string | null): value is string {
  if (value === null || !/^-?(?:0|[1-9][0-9]*)$/u.test(value) || value.length > 20) {
    return false;
  }
  try {
    const parsed = BigInt(value);
    return parsed >= -9_223_372_036_854_775_808n &&
      parsed <= 9_223_372_036_854_775_807n &&
      parsed.toString() === value;
  } catch {
    return false;
  }
}

function formatSemanticRecord(record: SemanticExploreRecordRef) {
  const child = record.subrecordId ? ` / ${record.subrecordId}` : '';
  return `${record.gameFamily} / ${record.domain} / ` +
    `${record.recordKind.key}@${record.recordKind.schemaVersion}:${record.recordId}${child}`;
}

function safeDiagnosticMessage(message: string) {
  return !(
    /(?:^|[^a-z0-9])file\s*:/iu.test(message) ||
    /(?:^|[^a-z0-9])~(?:[\\/]|\s|$)/iu.test(message) ||
    /(?:^|[^a-z0-9])[a-z]:[^\s]/iu.test(message) ||
    /[\\/]/u.test(message)
  );
}

function queryErrorKey(error: GuidedDesignQueryError | null | undefined) {
  return `guidedDesign.error.${error ?? 'generic'}`;
}

function coverageReasonKey(reasonCode: string) {
  switch (reasonCode) {
    case 'atomic-trainer-batch-unavailable':
      return 'guidedDesign.coverage.reason.trainerAtomic';
    case 'verified-level-method-metadata-unavailable':
      return 'guidedDesign.coverage.reason.evolutionMetadata';
    case 'probability-normalization-provider-unavailable':
      return 'guidedDesign.coverage.reason.encounterNormalization';
    case 'pending-overlay-unavailable':
      return 'guidedDesign.coverage.reason.pendingUnavailable';
    case 'workflow-disabled':
      return 'guidedDesign.coverage.reason.workflowDisabled';
    case 'workflow-source-invalid':
      return 'guidedDesign.coverage.reason.sourceInvalid';
    case 'workflow-source-unavailable':
      return 'guidedDesign.coverage.reason.sourceUnavailable';
    case 'replay-and-hiding-commitment-contract-unavailable':
      return 'guidedDesign.coverage.reason.raceContract';
    default:
      return 'guidedDesign.coverage.reason.provider';
  }
}

export function guidedDesignFeatureLabelKey(feature: GuidedDesignFeature) {
  return `guidedDesign.feature.${feature}`;
}
