/* SPDX-License-Identifier: GPL-3.0-only */

import { AlertCircle, CheckCircle, FolderOpen, Move, ShieldCheck, X } from 'lucide-react';
import { type ReactNode, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { type ApiDiagnostic, type ProjectPathRole } from '../../bridge/contracts';
import {
  type ApplyProjectRelocationResponse,
  type OutputSafetyScope,
  type PreviewProjectRelocationResponse
} from '../../bridge/outputSafetyContracts';
import { type ProjectBridge } from '../../bridge/projectBridge';
import { type DesktopServices } from '../../desktopServices';
import { DiagnosticsSection } from '../../components/workflowPanels';
import { useLocalization } from '../../localization';
import { useModalDialog } from '../../components/useModalDialog';
import { toDesktopErrorDiagnostics, toProjectBridgeDiagnostics } from '../../uiErrorDiagnostics';
import {
  reconcileRelocationCandidatePaths,
  type ProjectRelocationCandidatePathField,
  type ProjectRelocationCandidatePaths
} from './projectRelocationDraftState';

type CandidatePaths = ProjectRelocationCandidatePaths;
type CandidatePathField = ProjectRelocationCandidatePathField;

const relocationFields: ReadonlyArray<{
  field: CandidatePathField;
  kind: 'directory' | 'file';
  labelKey: string;
  scope?: 'scarletViolet' | 'za';
}> = [
  { field: 'baseRomFsPath', kind: 'directory', labelKey: 'outputSafety.relocation.path.baseRomFs' },
  { field: 'baseExeFsPath', kind: 'directory', labelKey: 'outputSafety.relocation.path.baseExeFs' },
  { field: 'outputRootPath', kind: 'directory', labelKey: 'outputSafety.relocation.path.outputRoot' },
  { field: 'saveFilePath', kind: 'file', labelKey: 'outputSafety.relocation.path.saveFile' },
  {
    field: 'scarletVioletSupportFolderPath',
    kind: 'directory',
    labelKey: 'outputSafety.relocation.path.supportFolder',
    scope: 'scarletViolet'
  },
  {
    field: 'pokemonLegendsZASupportFolderPath',
    kind: 'directory',
    labelKey: 'outputSafety.relocation.path.supportFolder',
    scope: 'za'
  }
];

export function ProjectRelocationPanel({
  armCriticalWriteGuard,
  bridge,
  canRelocate,
  desktopServices,
  onApplyBusyChange,
  onRelocated,
  source
}: {
  armCriticalWriteGuard: () => Promise<boolean>;
  bridge: ProjectBridge;
  canRelocate: boolean;
  desktopServices: DesktopServices;
  onApplyBusyChange: (isBusy: boolean) => void;
  onRelocated: (
    response: ApplyProjectRelocationResponse,
    candidatePaths: CandidatePaths
  ) => Promise<void> | void;
  source: OutputSafetyScope | null;
}) {
  const { t } = useLocalization();
  const [isExpanded, setIsExpanded] = useState(false);
  const [candidatePaths, setCandidatePaths] = useState<CandidatePaths | null>(source?.paths ?? null);
  const [preview, setPreview] = useState<PreviewProjectRelocationResponse | null>(null);
  const [reviewedCandidateSignature, setReviewedCandidateSignature] = useState<string | null>(null);
  const [diagnostics, setDiagnostics] = useState<ApiDiagnostic[]>([]);
  const [isBusy, setIsBusy] = useState(false);
  const busyRef = useRef(false);
  const actionGenerationRef = useRef(0);
  const actionPhaseRef = useRef<'apply' | 'pick' | 'review' | null>(null);
  const applyGenerationRef = useRef<number | null>(null);
  const isMountedRef = useRef(true);
  const armCriticalWriteGuardRef = useRef(armCriticalWriteGuard);
  armCriticalWriteGuardRef.current = armCriticalWriteGuard;
  const onApplyBusyChangeRef = useRef(onApplyBusyChange);
  onApplyBusyChangeRef.current = onApplyBusyChange;
  const sourceSignature = useMemo(() => source ? JSON.stringify(source) : null, [source]);
  const sourceSignatureRef = useRef(sourceSignature);
  sourceSignatureRef.current = sourceSignature;
  const previousSourceRef = useRef<OutputSafetyScope | null>(null);
  const candidateSignature = useMemo(
    () => candidatePaths ? JSON.stringify(candidatePaths) : null,
    [candidatePaths]
  );
  const candidatePathsRef = useRef(candidatePaths);
  const candidateSignatureRef = useRef(candidateSignature);
  candidatePathsRef.current = candidatePaths;
  candidateSignatureRef.current = candidateSignature;

  const beginApplyGuard = useCallback(async (generation: number) => {
    applyGenerationRef.current = generation;
    onApplyBusyChangeRef.current(true);
    return armCriticalWriteGuardRef.current();
  }, []);
  const endApplyGuard = useCallback((generation?: number) => {
    if (
      applyGenerationRef.current === null ||
      (generation !== undefined && applyGenerationRef.current !== generation)
    ) {
      return;
    }
    applyGenerationRef.current = null;
    onApplyBusyChangeRef.current(false);
  }, []);

  useEffect(() => {
    isMountedRef.current = true;
    return () => {
      isMountedRef.current = false;
      actionGenerationRef.current += 1;
      busyRef.current = false;
      actionPhaseRef.current = null;
      endApplyGuard();
    };
  }, [endApplyGuard]);

  useEffect(() => {
    actionGenerationRef.current += 1;
    busyRef.current = false;
    actionPhaseRef.current = null;
    endApplyGuard();
    const previousSource = previousSourceRef.current;
    previousSourceRef.current = source;
    setCandidatePaths((current) =>
      reconcileRelocationCandidatePaths(
        current,
        previousSource,
        source
      )
    );
    setPreview(null);
    setReviewedCandidateSignature(null);
    setDiagnostics([]);
    setIsBusy(false);
  }, [endApplyGuard, sourceSignature]);

  const beginAction = (phase: 'apply' | 'pick' | 'review') => {
    if (busyRef.current) {
      return null;
    }
    busyRef.current = true;
    actionPhaseRef.current = phase;
    setIsBusy(true);
    actionGenerationRef.current += 1;
    return actionGenerationRef.current;
  };

  const isCurrentAction = (generation: number, requestSourceSignature: string | null) =>
    isMountedRef.current &&
    actionGenerationRef.current === generation &&
    sourceSignatureRef.current === requestSourceSignature;

  const endAction = (generation: number, requestSourceSignature: string | null) => {
    if (isCurrentAction(generation, requestSourceSignature)) {
      busyRef.current = false;
      actionPhaseRef.current = null;
      setIsBusy(false);
    }
  };

  if (!source || !candidatePaths) {
    return null;
  }

  const visibleFields = relocationFields.filter((entry) => {
    if (!entry.scope) {
      return true;
    }
    return entry.scope === 'scarletViolet'
      ? candidatePaths.selectedGame === 'scarlet' || candidatePaths.selectedGame === 'violet'
      : candidatePaths.selectedGame === 'za';
  });
  const canApplyPreview =
    preview?.canApply === true &&
    candidateSignature !== null &&
    reviewedCandidateSignature === candidateSignature;

  const updateCandidatePath = (field: CandidatePathField, value: string | null) => {
    setCandidatePaths((current) => current ? { ...current, [field]: value } : current);
    setPreview(null);
    setReviewedCandidateSignature(null);
    setDiagnostics([]);
  };

  const pickCandidatePath = async (
    field: CandidatePathField,
    kind: 'directory' | 'file',
    label: string
  ) => {
    const generation = beginAction('pick');
    if (generation === null) {
      return;
    }
    const requestSourceSignature = sourceSignature;
    const requestedFieldValue = candidatePaths[field] ?? null;
    try {
      const selected = await (kind === 'file' ? desktopServices.pickFile : desktopServices.pickFolder)({
        defaultPath: candidatePaths[field] ?? undefined,
        title: t('outputSafety.relocation.pickTitle', { label })
      });
      if (
        selected &&
        isCurrentAction(generation, requestSourceSignature) &&
        (candidatePathsRef.current?.[field] ?? null) === requestedFieldValue
      ) {
        updateCandidatePath(field, selected);
      }
    } catch (error) {
      if (
        isCurrentAction(generation, requestSourceSignature) &&
        (candidatePathsRef.current?.[field] ?? null) === requestedFieldValue
      ) {
        setDiagnostics(toDesktopErrorDiagnostics(error, t('outputSafety.error.relocationPath')));
      }
    } finally {
      endAction(generation, requestSourceSignature);
    }
  };

  const reviewRelocation = async () => {
    if (!canRelocate || !candidateSignature) {
      return;
    }
    const generation = beginAction('review');
    if (generation === null) {
      return;
    }
    const requestSourceSignature = sourceSignature;
    const requestedCandidateSignature = candidateSignature;
    setDiagnostics([]);
    setPreview(null);
    try {
      const response = await bridge.previewProjectRelocation({
        candidatePaths,
        source
      });
      if (
        isCurrentAction(generation, requestSourceSignature) &&
        candidateSignatureRef.current === requestedCandidateSignature
      ) {
        setPreview(response);
        setReviewedCandidateSignature(candidateSignature);
        setDiagnostics(response.diagnostics);
      }
    } catch (error) {
      if (
        isCurrentAction(generation, requestSourceSignature) &&
        candidateSignatureRef.current === requestedCandidateSignature
      ) {
        setDiagnostics(toProjectBridgeDiagnostics(error, t('outputSafety.error.relocationPreview')));
      }
    } finally {
      endAction(generation, requestSourceSignature);
    }
  };

  const applyRelocation = async () => {
    if (!canRelocate || !preview || !canApplyPreview) {
      return;
    }
    const generation = beginAction('apply');
    if (generation === null) {
      return;
    }
    const requestSourceSignature = sourceSignature;
    setDiagnostics([]);
    try {
      if (
        !(await beginApplyGuard(generation)) ||
        applyGenerationRef.current !== generation ||
        !isCurrentAction(generation, requestSourceSignature)
      ) {
        return;
      }
      const response = await bridge.applyProjectRelocation({
        candidatePaths,
        reviewToken: preview.reviewToken,
        source
      });
      if (!isCurrentAction(generation, requestSourceSignature)) {
        return;
      }
      const expectedMigratedDocumentIds = preview.workspaceDocuments
        .filter((document) => document.status === 'copy')
        .map((document) => document.documentId);
      if (
        response.migratedDocumentIds.length !== expectedMigratedDocumentIds.length ||
        response.migratedDocumentIds.some((documentId, index) => (
          documentId !== expectedMigratedDocumentIds[index]
        ))
      ) {
        throw new Error('The relocation receipt does not match the reviewed private documents.');
      }
      setDiagnostics(response.diagnostics);
      setPreview(null);
      setReviewedCandidateSignature(null);
      setIsExpanded(false);
      await onRelocated(response, candidatePaths);
    } catch (error) {
      if (isCurrentAction(generation, requestSourceSignature)) {
        setDiagnostics(toProjectBridgeDiagnostics(error, t('outputSafety.error.relocationApply')));
      }
    } finally {
      endApplyGuard(generation);
      endAction(generation, requestSourceSignature);
    }
  };

  const draftControlsLocked =
    actionPhaseRef.current === 'apply';

  return (
    <>
      <button
        className="secondary-button"
        disabled={isBusy || !canRelocate}
        onClick={() => setIsExpanded(true)}
        title={!canRelocate ? t('outputSafety.relocation.pendingChanges') : undefined}
        type="button"
      >
        <Move aria-hidden="true" size={18} />
        <span>{t('outputSafety.relocation.open')}</span>
      </button>
      {isExpanded ? (
        <ProjectRelocationDialog
          canClose={!isBusy}
          onClose={() => setIsExpanded(false)}
        >
          <p className="modal-copy modal-copy-muted">{t('outputSafety.relocation.description')}</p>
          {!canRelocate ? (
            <p className="project-relocation-blocked" role="status">
              <AlertCircle aria-hidden="true" size={15} />
              <span>{t('outputSafety.relocation.pendingChanges')}</span>
            </p>
          ) : null}
          <div className="project-relocation-paths">
            {visibleFields.map((entry) => {
              const label = t(entry.labelKey);
              return (
                <label key={entry.field}>
                  <span>{label}</span>
                  <span className="project-relocation-path-input">
                    <input
                      disabled={draftControlsLocked}
                      id={`project-relocation-${entry.field}`}
                      onChange={(event) => updateCandidatePath(
                        entry.field,
                        event.currentTarget.value.length > 0 ? event.currentTarget.value : null
                      )}
                      type="text"
                      value={candidatePaths[entry.field] ?? ''}
                    />
                    <button
                      aria-label={t('outputSafety.relocation.pickTitle', { label })}
                      className="secondary-button icon-button"
                      disabled={
                        !desktopServices.isAvailable ||
                        draftControlsLocked ||
                        !canRelocate
                      }
                      onClick={() => void pickCandidatePath(entry.field, entry.kind, label)}
                      type="button"
                    >
                      <FolderOpen aria-hidden="true" size={16} />
                    </button>
                  </span>
                </label>
              );
            })}
          </div>

          <div className="project-relocation-actions">
            <button
              className="secondary-button"
              disabled={!canRelocate || isBusy}
              onClick={() => void reviewRelocation()}
              type="button"
            >
              <ShieldCheck aria-hidden="true" size={16} />
              <span>{t('outputSafety.relocation.review')}</span>
            </button>
            {preview ? (
              <button
                className="primary-button"
                disabled={!canApplyPreview || isBusy}
                onClick={() => void applyRelocation()}
                type="button"
              >
                <Move aria-hidden="true" size={16} />
                <span>{t('outputSafety.relocation.apply')}</span>
              </button>
            ) : null}
          </div>

          {preview ? (
            <div className={`project-relocation-review ${preview.canApply ? 'is-ready' : 'is-blocked'}`}>
              {preview.canApply ? <CheckCircle aria-hidden="true" size={17} /> : <AlertCircle aria-hidden="true" size={17} />}
              <div>
                <strong>{t(preview.canApply ? 'outputSafety.relocation.ready' : 'outputSafety.relocation.blocked')}</strong>
                <ul>
                  {preview.roles.map((role) => (
                    <li key={role.role}>
                      <span>{t(projectPathRoleLabelKeys[role.role])}</span>
                      <span>{t(`outputSafety.relocation.pathStatus.${role.status}`)}</span>
                    </li>
                  ))}
                  {preview.workspaceDocuments.map((document) => (
                    <li key={document.documentId}>
                      <span>{t('outputSafety.relocation.privateWorkspace')}</span>
                      <span>{t(`outputSafety.relocation.documentStatus.${document.status}`)}</span>
                    </li>
                  ))}
                </ul>
              </div>
            </div>
          ) : null}

          <DiagnosticsSection diagnostics={diagnostics} />
        </ProjectRelocationDialog>
      ) : null}
    </>
  );
}

function ProjectRelocationDialog({
  canClose,
  children,
  onClose
}: {
  canClose: boolean;
  children: ReactNode;
  onClose: () => void;
}) {
  const { t } = useLocalization();
  const dialogRef = useModalDialog<HTMLElement>({ canClose, onClose });

  return (
    <div className="modal-backdrop" role="presentation">
      <section
        aria-labelledby="project-relocation-heading"
        aria-modal="true"
        className="modal-panel project-relocation-dialog"
        ref={dialogRef}
        role="dialog"
        tabIndex={-1}
      >
        <div className="panel-heading project-relocation-heading">
          <Move aria-hidden="true" size={18} />
          <h2 id="project-relocation-heading">{t('outputSafety.relocation.title')}</h2>
          <button
            aria-label={t('outputSafety.relocation.close')}
            className="secondary-button icon-button"
            disabled={!canClose}
            onClick={onClose}
            type="button"
          >
            <X aria-hidden="true" size={16} />
          </button>
        </div>
        <div className="project-relocation-body">{children}</div>
      </section>
    </div>
  );
}

const projectPathRoleLabelKeys: Record<ProjectPathRole, string> = {
  baseExeFs: 'outputSafety.relocation.path.baseExeFs',
  baseRomFs: 'outputSafety.relocation.path.baseRomFs',
  outputRoot: 'outputSafety.relocation.path.outputRoot',
  pokemonLegendsZASupportFolder: 'outputSafety.relocation.path.supportFolder',
  saveFile: 'outputSafety.relocation.path.saveFile',
  scarletVioletSupportFolder: 'outputSafety.relocation.path.supportFolder'
};
