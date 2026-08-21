/* SPDX-License-Identifier: GPL-3.0-only */

import { AlertCircle, CheckCircle, FolderOpen, Move, ShieldCheck, X } from 'lucide-react';
import { type ReactNode, useEffect, useMemo, useRef, useState } from 'react';
import { type ApiDiagnostic, type ProjectPathRole } from '../../bridge/contracts';
import {
  type ApplyProjectRelocationResponse,
  type OutputSafetyScope,
  type PreviewProjectRelocationResponse
} from '../../bridge/outputSafetyContracts';
import { type ProjectBridge } from '../../bridge/projectBridge';
import { type DesktopServices } from '../../desktopServices';
import { formatDiagnosticMessage } from '../../diagnostics';
import { useLocalization } from '../../localization';
import { useModalDialog } from '../../components/useModalDialog';
import { toDesktopErrorDiagnostics, toProjectBridgeDiagnostics } from '../../uiErrorDiagnostics';

type CandidatePaths = OutputSafetyScope['paths'];
type CandidatePathField = Exclude<keyof CandidatePaths, 'gameTextLanguage' | 'selectedGame'>;

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
  bridge,
  canRelocate,
  desktopServices,
  onRelocated,
  source
}: {
  bridge: ProjectBridge;
  canRelocate: boolean;
  desktopServices: DesktopServices;
  onRelocated: (
    response: ApplyProjectRelocationResponse,
    candidatePaths: CandidatePaths
  ) => Promise<void> | void;
  source: OutputSafetyScope | null;
}) {
  const { t, translateLiteral } = useLocalization();
  const [isExpanded, setIsExpanded] = useState(false);
  const [candidatePaths, setCandidatePaths] = useState<CandidatePaths | null>(source?.paths ?? null);
  const [preview, setPreview] = useState<PreviewProjectRelocationResponse | null>(null);
  const [reviewedCandidateSignature, setReviewedCandidateSignature] = useState<string | null>(null);
  const [diagnostics, setDiagnostics] = useState<ApiDiagnostic[]>([]);
  const [isBusy, setIsBusy] = useState(false);
  const busyRef = useRef(false);
  const actionGenerationRef = useRef(0);
  const sourceSignature = useMemo(() => source ? JSON.stringify(source) : null, [source]);
  const sourceSignatureRef = useRef(sourceSignature);
  sourceSignatureRef.current = sourceSignature;
  const candidateSignature = useMemo(
    () => candidatePaths ? JSON.stringify(candidatePaths) : null,
    [candidatePaths]
  );

  useEffect(() => {
    actionGenerationRef.current += 1;
    busyRef.current = false;
    setCandidatePaths(source?.paths ?? null);
    setPreview(null);
    setReviewedCandidateSignature(null);
    setDiagnostics([]);
    setIsBusy(false);
  }, [sourceSignature]);

  const beginAction = () => {
    if (busyRef.current) {
      return null;
    }
    busyRef.current = true;
    setIsBusy(true);
    actionGenerationRef.current += 1;
    return actionGenerationRef.current;
  };

  const isCurrentAction = (generation: number, requestSourceSignature: string | null) =>
    actionGenerationRef.current === generation &&
    sourceSignatureRef.current === requestSourceSignature;

  const endAction = (generation: number, requestSourceSignature: string | null) => {
    if (isCurrentAction(generation, requestSourceSignature)) {
      busyRef.current = false;
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
    const generation = beginAction();
    if (generation === null) {
      return;
    }
    const requestSourceSignature = sourceSignature;
    try {
      const selected = await (kind === 'file' ? desktopServices.pickFile : desktopServices.pickFolder)({
        defaultPath: candidatePaths[field] ?? undefined,
        title: t('outputSafety.relocation.pickTitle', { label })
      });
      if (selected && isCurrentAction(generation, requestSourceSignature)) {
        updateCandidatePath(field, selected);
      }
    } catch (error) {
      if (isCurrentAction(generation, requestSourceSignature)) {
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
    const generation = beginAction();
    if (generation === null) {
      return;
    }
    const requestSourceSignature = sourceSignature;
    setDiagnostics([]);
    setPreview(null);
    try {
      const response = await bridge.previewProjectRelocation({
        candidatePaths,
        source
      });
      if (isCurrentAction(generation, requestSourceSignature)) {
        setPreview(response);
        setReviewedCandidateSignature(candidateSignature);
        setDiagnostics(response.diagnostics);
      }
    } catch (error) {
      if (isCurrentAction(generation, requestSourceSignature)) {
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
    const generation = beginAction();
    if (generation === null) {
      return;
    }
    const requestSourceSignature = sourceSignature;
    setDiagnostics([]);
    try {
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
      endAction(generation, requestSourceSignature);
    }
  };

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
                      disabled={isBusy || !canRelocate}
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
                      disabled={!desktopServices.isAvailable || isBusy || !canRelocate}
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

          {diagnostics.length > 0 ? (
            <ul className="output-safety-diagnostics">
              {diagnostics.map((diagnostic, index) => (
                <li className={`diagnostic-${diagnostic.severity}`} key={`${diagnostic.code ?? 'diagnostic'}-${index}`}>
                  <AlertCircle aria-hidden="true" size={15} />
                  <span>{formatDiagnosticMessage(diagnostic, translateLiteral, t)}</span>
                </li>
              ))}
            </ul>
          ) : null}
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
