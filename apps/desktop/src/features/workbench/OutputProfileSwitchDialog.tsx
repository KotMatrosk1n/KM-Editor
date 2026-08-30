/* SPDX-License-Identifier: GPL-3.0-only */

import { AlertCircle, CheckCircle, Move, ShieldCheck, X } from 'lucide-react';
import { useCallback, useEffect, useId, useMemo, useRef, useState } from 'react';
import type { ApiDiagnostic, ProjectPathRole } from '../../bridge/contracts';
import type {
  ApplyProjectRelocationResponse,
  OutputSafetyScope,
  PreviewProjectRelocationResponse
} from '../../bridge/outputSafetyContracts';
import type { ProjectBridge } from '../../bridge/projectBridge';
import { LoadingProgress } from '../../components/LoadingProgress';
import { DiagnosticsSection } from '../../components/workflowPanels';
import { useModalDialog } from '../../components/useModalDialog';
import { useLocalization } from '../../localization';
import { toProjectBridgeDiagnostics } from '../../uiErrorDiagnostics';
import './workbench.css';

type CandidatePaths = OutputSafetyScope['paths'];

export type OutputProfileSwitchDialogProps = {
  armCriticalWriteGuard: () => Promise<boolean>;
  bridge: ProjectBridge;
  canApply: boolean;
  candidatePaths: CandidatePaths;
  onApplyBusyChange: (isBusy: boolean) => void;
  onApplied: (
    response: ApplyProjectRelocationResponse,
    candidatePaths: CandidatePaths
  ) => Promise<void> | void;
  onClose: () => void;
  profileName: string;
  source: OutputSafetyScope;
};

export function OutputProfileSwitchDialog({
  armCriticalWriteGuard,
  bridge,
  canApply,
  candidatePaths,
  onApplyBusyChange,
  onApplied,
  onClose,
  profileName,
  source
}: OutputProfileSwitchDialogProps) {
  const { t, translateLiteral } = useLocalization();
  const [preview, setPreview] = useState<PreviewProjectRelocationResponse | null>(null);
  const [diagnostics, setDiagnostics] = useState<readonly ApiDiagnostic[]>([]);
  const [isBusy, setIsBusy] = useState(false);
  const busyRef = useRef(false);
  const actionPhaseRef = useRef<'review' | 'apply' | 'activation' | null>(null);
  const generationRef = useRef(0);
  const applyGenerationRef = useRef<number | null>(null);
  const isMountedRef = useRef(true);
  const armCriticalWriteGuardRef = useRef(armCriticalWriteGuard);
  armCriticalWriteGuardRef.current = armCriticalWriteGuard;
  const onApplyBusyChangeRef = useRef(onApplyBusyChange);
  onApplyBusyChangeRef.current = onApplyBusyChange;
  const requestSignature = useMemo(
    () => JSON.stringify({ candidatePaths, source }),
    [candidatePaths, source]
  );
  const requestSignatureRef = useRef(requestSignature);
  requestSignatureRef.current = requestSignature;
  const headingId = useId();
  const dialogRef = useModalDialog<HTMLDivElement>({ canClose: !isBusy, onClose });

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
      generationRef.current += 1;
      busyRef.current = false;
      actionPhaseRef.current = null;
      endApplyGuard();
    };
  }, [endApplyGuard]);

  useEffect(() => {
    if (actionPhaseRef.current === 'activation') {
      return;
    }
    generationRef.current += 1;
    busyRef.current = false;
    actionPhaseRef.current = null;
    endApplyGuard();
    setPreview(null);
    setDiagnostics([]);
    setIsBusy(false);
  }, [endApplyGuard, requestSignature]);

  const beginAction = (phase: 'review' | 'apply') => {
    if (busyRef.current) {
      return null;
    }
    busyRef.current = true;
    actionPhaseRef.current = phase;
    setIsBusy(true);
    generationRef.current += 1;
    return generationRef.current;
  };
  const isCurrentAction = (generation: number, signature: string) =>
    isMountedRef.current &&
    generationRef.current === generation &&
    (actionPhaseRef.current === 'activation' || requestSignatureRef.current === signature);
  const finishAction = (generation: number) => {
    if (generationRef.current === generation) {
      busyRef.current = false;
      actionPhaseRef.current = null;
      setIsBusy(false);
    }
  };

  const review = async () => {
    if (!canApply) {
      return;
    }
    const generation = beginAction('review');
    if (generation === null) {
      return;
    }
    const signature = requestSignature;
    setPreview(null);
    setDiagnostics([]);
    try {
      const response = await bridge.previewProjectRelocation({ candidatePaths, source });
      if (isCurrentAction(generation, signature)) {
        setPreview(response);
        setDiagnostics(response.diagnostics);
      }
    } catch (error) {
      if (isCurrentAction(generation, signature)) {
        setDiagnostics(
          toProjectBridgeDiagnostics(error, t('workbench.outputProfileSwitch.previewError'))
        );
      }
    } finally {
      finishAction(generation);
    }
  };

  const apply = async () => {
    if (!canApply || !preview?.canApply) {
      return;
    }
    const generation = beginAction('apply');
    if (generation === null) {
      return;
    }
    const signature = requestSignature;
    setDiagnostics([]);
    try {
      if (
        !(await beginApplyGuard(generation)) ||
        applyGenerationRef.current !== generation ||
        !isCurrentAction(generation, signature)
      ) {
        return;
      }
      const response = await bridge.applyProjectRelocation({
        candidatePaths,
        reviewToken: preview.reviewToken,
        source
      });
      if (!isCurrentAction(generation, signature)) {
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
      actionPhaseRef.current = 'activation';
      setDiagnostics(response.diagnostics);
      setPreview(null);
      try {
        await onApplied(response, candidatePaths);
      } catch (error) {
        if (isCurrentAction(generation, signature)) {
          setDiagnostics([
            ...response.diagnostics,
            ...toProjectBridgeDiagnostics(
              error,
              t('workbench.outputProfileSwitch.activationError')
            )
          ]);
        }
        return;
      }
      if (isCurrentAction(generation, signature)) {
        onClose();
      }
    } catch (error) {
      if (isCurrentAction(generation, signature)) {
        setDiagnostics(
          toProjectBridgeDiagnostics(error, t('workbench.outputProfileSwitch.applyError'))
        );
      }
    } finally {
      endApplyGuard(generation);
      finishAction(generation);
    }
  };

  const busyPhase = isBusy ? actionPhaseRef.current : null;
  const busyLabel = busyPhase === 'review'
    ? translateLiteral('Reviewing')
    : busyPhase === 'activation'
      ? translateLiteral('Refreshing loaded editors')
      : translateLiteral('Applying');

  return (
    <div
      className="km-workbench-overlay"
      onMouseDown={(event) => {
        if (!isBusy && event.target === event.currentTarget) {
          onClose();
        }
      }}
    >
      <div
        aria-busy={isBusy || undefined}
        aria-labelledby={headingId}
        aria-modal="true"
        className="km-output-profile-switch-dialog"
        ref={dialogRef}
        role="dialog"
        tabIndex={-1}
      >
        <header className="km-output-profile-switch-heading">
          <Move aria-hidden="true" size={19} />
          <div>
            <h2 id={headingId}>{t('workbench.outputProfileSwitch.title')}</h2>
            <p>
              {t('workbench.outputProfileSwitch.description')}{' '}
              <strong data-localization-ignore="true">{profileName}</strong>
            </p>
          </div>
          <button
            aria-label={t('workbench.outputProfileSwitch.close')}
            className="secondary-button icon-button"
            disabled={isBusy}
            onClick={onClose}
            title={t('workbench.outputProfileSwitch.close')}
            type="button"
          >
            <X aria-hidden="true" size={16} />
          </button>
        </header>

        {isBusy ? <LoadingProgress className="is-compact" label={busyLabel} /> : null}

        {!canApply ? (
          <p className="km-output-profile-switch-blocked" role="status">
            <AlertCircle aria-hidden="true" size={16} />
            <span>{t('workbench.outputProfileSwitch.unavailable')}</span>
          </p>
        ) : null}

        <div className="km-output-profile-switch-actions">
          <button
            aria-busy={busyPhase === 'review' || undefined}
            className="secondary-button"
            disabled={!canApply || isBusy}
            onClick={() => void review()}
            type="button"
          >
            <ShieldCheck aria-hidden="true" size={16} />
            <span>
              {busyPhase === 'review'
                ? busyLabel
                : t('workbench.outputProfileSwitch.review')}
            </span>
          </button>
          {preview ? (
            <button
              aria-busy={busyPhase === 'apply' || undefined}
              className="primary-button"
              disabled={!preview.canApply || !canApply || isBusy}
              onClick={() => void apply()}
              type="button"
            >
              <Move aria-hidden="true" size={16} />
              <span>
                {busyPhase === 'apply'
                  ? busyLabel
                  : t('workbench.outputProfileSwitch.apply')}
              </span>
            </button>
          ) : null}
        </div>

        {preview ? (
          <div
            className={`km-output-profile-switch-review ${preview.canApply ? 'is-ready' : 'is-blocked'}`}
            role="status"
          >
            {preview.canApply ? (
              <CheckCircle aria-hidden="true" size={17} />
            ) : (
              <AlertCircle aria-hidden="true" size={17} />
            )}
            <div>
              <strong>
                {t(
                  preview.canApply
                    ? 'workbench.outputProfileSwitch.ready'
                    : 'workbench.outputProfileSwitch.blocked'
                )}
              </strong>
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
                    <span>
                      {t(`outputSafety.relocation.documentStatus.${document.status}`)}
                    </span>
                  </li>
                ))}
              </ul>
            </div>
          </div>
        ) : null}

        <DiagnosticsSection diagnostics={[...diagnostics]} />
      </div>
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
