/* SPDX-License-Identifier: GPL-3.0-only */

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type {
  ApplyGameplaySettingsUpdateResponse,
  GameplaySettingsSnapshot,
  GameplaySettingsState,
  GameplaySettingsValues,
  PreviewGameplaySettingsUpdateResponse,
} from "../../bridge/gameplaySettingsContracts";
import {
  gameplaySettingsExperienceRateStepBasisPoints,
  gameplaySettingsMaximumExperienceRateBasisPoints,
} from "../../bridge/gameplaySettingsContracts";
import type { OutputSafetyScope } from "../../bridge/outputSafetyContracts";
import type { ProjectBridge } from "../../bridge/projectBridge";
import { useLocalization } from "../../localization";
import "./GameplaySettingsSection.css";

type GameplaySettingsBridge = Pick<
  ProjectBridge,
  | "applyGameplaySettingsUpdate"
  | "getGameplaySettings"
  | "previewGameplaySettingsUpdate"
>;

type GameplaySettingsSectionProps = {
  armCriticalWriteGuard: () => Promise<boolean>;
  bridge: GameplaySettingsBridge;
  canApply?: boolean;
  hideWhenUnavailable?: boolean;
  onApplied?: (scope: OutputSafetyScope) => Promise<void> | void;
  onApplyBusyChange?: (isBusy: boolean) => void;
  onError?: (
    error: unknown,
    operation: "apply" | "load" | "preview",
    scope: OutputSafetyScope,
  ) => Promise<void> | void;
  scope: OutputSafetyScope;
};

type LoadState = {
  snapshot: GameplaySettingsSnapshot | null;
  state: GameplaySettingsState;
};

type GameplaySettingsLoadMode = "applyRefresh" | "standalone";

const rateChoices = Array.from(
  {
    length:
      gameplaySettingsMaximumExperienceRateBasisPoints /
        gameplaySettingsExperienceRateStepBasisPoints +
      1,
  },
  (_, index) => index * gameplaySettingsExperienceRateStepBasisPoints,
);

export const gameplaySettingsGuardFailureMessageKey = "gameplaySettings.error";

export function GameplaySettingsSection({
  armCriticalWriteGuard,
  bridge,
  canApply = true,
  hideWhenUnavailable = false,
  onApplied,
  onApplyBusyChange,
  onError,
  scope,
}: GameplaySettingsSectionProps) {
  const { t } = useLocalization();
  const [loadState, setLoadState] = useState<LoadState | null>(null);
  const [draft, setDraft] = useState<GameplaySettingsValues | null>(null);
  const [preview, setPreview] =
    useState<PreviewGameplaySettingsUpdateResponse | null>(null);
  const [busy, setBusy] = useState<"apply" | "load" | "preview" | null>("load");
  const [messageKey, setMessageKey] = useState<string | null>(null);
  const actionGenerationRef = useRef(0);
  const isMountedRef = useRef(true);
  const armCriticalWriteGuardRef = useRef(armCriticalWriteGuard);
  armCriticalWriteGuardRef.current = armCriticalWriteGuard;
  const onApplyBusyChangeRef = useRef(onApplyBusyChange);
  onApplyBusyChangeRef.current = onApplyBusyChange;
  const applyGuardControllerRef = useRef<GameplaySettingsApplyGuardController | null>(
    null,
  );
  if (applyGuardControllerRef.current === null) {
    applyGuardControllerRef.current = createGameplaySettingsApplyGuardController(
      () => armCriticalWriteGuardRef.current,
      () => onApplyBusyChangeRef.current,
    );
  }
  const stableScope = useMemo<OutputSafetyScope>(
    () => copyGameplaySettingsScope(scope),
    [
      scope.paths.baseExeFsPath,
      scope.paths.baseRomFsPath,
      scope.paths.gameTextLanguage,
      scope.paths.outputRootPath,
      scope.paths.pokemonLegendsZASupportFolderPath,
      scope.paths.saveFilePath,
      scope.paths.scarletVioletSupportFolderPath,
      scope.paths.selectedGame,
      scope.projectId,
    ],
  );
  const scopeKey = useMemo(
    () => gameplaySettingsScopeKey(stableScope),
    [stableScope],
  );
  const scopeKeyRef = useRef(scopeKey);
  scopeKeyRef.current = scopeKey;

  const beginApplyGuard = useCallback(async (generation: number) => {
    return applyGuardControllerRef.current!.begin(generation);
  }, []);
  const endApplyGuard = useCallback((generation?: number) => {
    applyGuardControllerRef.current!.end(generation);
  }, []);
  const isCurrentApply = useCallback(
    (generation: number, requestScopeKey: string) =>
      isMountedRef.current &&
      actionGenerationRef.current === generation &&
      scopeKeyRef.current === requestScopeKey,
    [],
  );

  useEffect(() => {
    isMountedRef.current = true;
    return () => {
      isMountedRef.current = false;
      actionGenerationRef.current += 1;
      endApplyGuard();
    };
  }, [endApplyGuard]);

  useEffect(() => {
    actionGenerationRef.current += 1;
    endApplyGuard();
  }, [endApplyGuard, scopeKey]);

  const reportError = useCallback(
    async (
      error: unknown,
      operation: "apply" | "load" | "preview",
    ) => {
      setMessageKey("gameplaySettings.error");
      await onError?.(error, operation, stableScope);
    },
    [onError, stableScope],
  );

  const load = useCallback(async (
    mode: GameplaySettingsLoadMode = "standalone",
  ): Promise<boolean> => {
    const ownsBusy = gameplaySettingsLoadOwnsBusy(mode);
    const requestScopeKey = scopeKey;
    if (ownsBusy) {
      setBusy("load");
    }
    setMessageKey(null);
    setPreview(null);
    setLoadState(null);
    setDraft(null);
    try {
      const response = await bridge.getGameplaySettings({
        scope: stableScope,
      });
      if (scopeKeyRef.current !== requestScopeKey) return false;
      setLoadState(response);
      setDraft(response.snapshot?.values ?? null);
      return true;
    } catch (error) {
      if (scopeKeyRef.current !== requestScopeKey) return false;
      await reportError(error, "load");
      return false;
    } finally {
      if (ownsBusy && scopeKeyRef.current === requestScopeKey) {
        setBusy(null);
      }
    }
  }, [bridge, reportError, scopeKey, stableScope]);

  useEffect(() => {
    void load();
  }, [load]);

  const snapshot = loadState?.snapshot ?? null;
  const hasChanges = useMemo(
    () =>
      snapshot !== null &&
      draft !== null &&
      !sameValues(snapshot.values, draft),
    [draft, snapshot],
  );

  const updateDraft = useCallback((next: GameplaySettingsValues) => {
    setDraft(next);
    setPreview(null);
    setMessageKey(null);
  }, []);

  const review = useCallback(async () => {
    if (!snapshot || !draft || !hasChanges) return;
    const requestScopeKey = scopeKey;
    setBusy("preview");
    setMessageKey(null);
    try {
      const response = await bridge.previewGameplaySettingsUpdate({
        expectedGeneration: snapshot.generation,
        experienceRateBasisPoints:
          draft.experienceRateBasisPoints ===
          snapshot.values.experienceRateBasisPoints
            ? undefined
            : draft.experienceRateBasisPoints,
        experienceShareEnabled:
          draft.experienceShareEnabled ===
          snapshot.values.experienceShareEnabled
            ? undefined
            : draft.experienceShareEnabled,
        levelCap:
          draft.levelCap === snapshot.values.levelCap
            ? undefined
            : draft.levelCap,
        levelCapEnabled:
          draft.levelCapEnabled === snapshot.values.levelCapEnabled
            ? undefined
            : draft.levelCapEnabled,
        scope: stableScope,
      });
      if (scopeKeyRef.current !== requestScopeKey) return;
      setPreview(response);
    } catch (error) {
      if (scopeKeyRef.current !== requestScopeKey) return;
      await reportError(error, "preview");
    } finally {
      if (scopeKeyRef.current === requestScopeKey) {
        setBusy(null);
      }
    }
  }, [bridge, draft, hasChanges, reportError, scopeKey, snapshot, stableScope]);

  const apply = useCallback(async () => {
    if (!preview || !canApply) return;
    const requestScopeKey = scopeKey;
    actionGenerationRef.current += 1;
    const generation = actionGenerationRef.current;
    setBusy("apply");
    setMessageKey(null);
    try {
      const didArmCriticalWriteGuard = await beginApplyGuard(generation);
      if (
        !didArmCriticalWriteGuard ||
        !applyGuardControllerRef.current!.isActive(generation) ||
        !isCurrentApply(generation, requestScopeKey)
      ) {
        if (isCurrentApply(generation, requestScopeKey)) {
          setMessageKey(gameplaySettingsGuardFailureMessageKey);
        }
        return;
      }

      const response = await bridge.applyGameplaySettingsUpdate({
        reviewId: preview.reviewId,
        scope: stableScope,
      });
      if (!isCurrentApply(generation, requestScopeKey)) return;
      setPreview(null);
      const disposition = getGameplaySettingsApplyDisposition(response);
      if (disposition === "committed") {
        let hasCurrentSnapshot = response.snapshot !== null;
        if (response.snapshot) {
          setLoadState({ state: "ready", snapshot: response.snapshot });
          setDraft(response.snapshot.values);
        } else {
          hasCurrentSnapshot = await load("applyRefresh");
        }
        if (
          hasCurrentSnapshot &&
          isCurrentApply(generation, requestScopeKey)
        ) {
          setMessageKey("gameplaySettings.applied");
        }
        if (!isCurrentApply(generation, requestScopeKey)) return;
        try {
          await onApplied?.(stableScope);
        } catch (error) {
          if (isCurrentApply(generation, requestScopeKey)) {
            await reportError(error, "load");
          }
        }
      } else if (disposition === "rolledBack") {
        const didReload = await load("applyRefresh");
        if (didReload && isCurrentApply(generation, requestScopeKey)) {
          setMessageKey("gameplaySettings.rolledBack");
        }
      } else {
        setMessageKey("gameplaySettings.recoveryRequired");
      }
    } catch (error) {
      if (!isCurrentApply(generation, requestScopeKey)) return;
      await reportError(error, "apply");
    } finally {
      endApplyGuard(generation);
      if (isCurrentApply(generation, requestScopeKey)) {
        setBusy(null);
      }
    }
  }, [
    beginApplyGuard,
    bridge,
    canApply,
    endApplyGuard,
    isCurrentApply,
    load,
    onApplied,
    preview,
    reportError,
    scopeKey,
    stableScope,
  ]);

  const reset = useCallback(() => {
    if (!snapshot) return;
    setDraft(snapshot.values);
    setPreview(null);
    setMessageKey(null);
  }, [snapshot]);

  if (!shouldRenderGameplaySettings(hideWhenUnavailable, loadState?.state ?? null)) {
    return null;
  }

  return (
    <section
      className="gameplay-settings"
      aria-labelledby="gameplay-settings-title"
    >
      <header className="gameplay-settings__header">
        <div>
          <h2 id="gameplay-settings-title">{t("gameplaySettings.title")}</h2>
          <p>{t("gameplaySettings.description")}</p>
        </div>
        <button
          type="button"
          onClick={() => void load()}
          disabled={busy !== null}
        >
          {busy === "load"
            ? t("gameplaySettings.loading")
            : t("gameplaySettings.refresh")}
        </button>
      </header>

      {messageKey ? (
        <p
          className="gameplay-settings__message"
          role="status"
          aria-live="polite"
        >
          {t(messageKey)}
        </p>
      ) : null}

      {busy === "load" && loadState === null ? (
        <p className="gameplay-settings__empty" role="status">
          {t("gameplaySettings.loading")}
        </p>
      ) : null}

      {loadState && !isEditableState(loadState.state) ? (
        <div className="gameplay-settings__empty" role="status">
          <h3>{t(`gameplaySettings.state.${loadState.state}.title`)}</h3>
          <p>{t(`gameplaySettings.state.${loadState.state}.description`)}</p>
        </div>
      ) : null}

      {snapshot && draft && loadState && isEditableState(loadState.state) ? (
        <>
          <div
            className="gameplay-settings__identity"
            aria-label={t("gameplaySettings.package")}
          >
            <span>
              {t("gameplaySettings.packageVersion", {
                version: snapshot.packageVersion,
              })}
            </span>
            <span>
              {t("gameplaySettings.generation", {
                generation: snapshot.generation,
              })}
            </span>
          </div>

          {loadState.state === "repairable" ? (
            <p className="gameplay-settings__warning" role="status">
              {t("gameplaySettings.state.repairable.description")}
            </p>
          ) : null}

          <div className="gameplay-settings__controls">
            {snapshot.hasExperienceShare ? (
              <label className="gameplay-settings__toggle">
                <input
                  type="checkbox"
                  checked={draft.experienceShareEnabled}
                  onChange={(event) =>
                    updateDraft({
                      ...draft,
                      experienceShareEnabled: event.currentTarget.checked,
                    })
                  }
                  disabled={busy !== null}
                />
                <span>
                  <strong>{t("gameplaySettings.experienceShare")}</strong>
                  <small>{t("gameplaySettings.experienceShareHelp")}</small>
                </span>
              </label>
            ) : null}

            {snapshot.hasExperienceRate ? (
              <label className="gameplay-settings__field">
                <span>
                  <strong>{t("gameplaySettings.experienceRate")}</strong>
                  <small>{t("gameplaySettings.experienceRateHelp")}</small>
                </span>
                <select
                  value={draft.experienceRateBasisPoints}
                  onChange={(event) =>
                    updateDraft({
                      ...draft,
                      experienceRateBasisPoints: Number(
                        event.currentTarget.value,
                      ),
                    })
                  }
                  disabled={busy !== null}
                >
                  {rateChoices.map((value) => (
                    <option key={value} value={value}>
                      {t("gameplaySettings.percent", { value: value / 100 })}
                    </option>
                  ))}
                </select>
              </label>
            ) : null}

            {snapshot.hasLevelCap ? (
              <div className="gameplay-settings__cap">
                <label className="gameplay-settings__toggle">
                  <input
                    type="checkbox"
                    checked={draft.levelCapEnabled}
                    onChange={(event) =>
                      updateDraft({
                        ...draft,
                        levelCap: event.currentTarget.checked
                          ? draft.levelCap
                          : 100,
                        levelCapEnabled: event.currentTarget.checked,
                      })
                    }
                    disabled={busy !== null}
                  />
                  <span>
                    <strong>{t("gameplaySettings.levelCap")}</strong>
                    <small>{t("gameplaySettings.levelCapHelp")}</small>
                  </span>
                </label>
                <label className="gameplay-settings__field gameplay-settings__field--compact">
                  <span>{t("gameplaySettings.level")}</span>
                  <select
                    value={draft.levelCap}
                    onChange={(event) =>
                      updateDraft({
                        ...draft,
                        levelCap: Number(event.currentTarget.value),
                      })
                    }
                    disabled={busy !== null || !draft.levelCapEnabled}
                  >
                    {Array.from({ length: 100 }, (_, index) => index + 1).map(
                      (level) => (
                        <option key={level} value={level}>
                          {level}
                        </option>
                      ),
                    )}
                  </select>
                </label>
              </div>
            ) : null}
          </div>

          <div className="gameplay-settings__actions">
            <button
              type="button"
              onClick={reset}
              disabled={busy !== null || !hasChanges}
            >
              {t("gameplaySettings.resetDraft")}
            </button>
            <button
              type="button"
              className="primary"
              onClick={() => void review()}
              disabled={busy !== null || !hasChanges}
            >
              {busy === "preview"
                ? t("gameplaySettings.reviewing")
                : t("gameplaySettings.review")}
            </button>
          </div>

          {preview ? (
            <div
              className="gameplay-settings__review"
              aria-labelledby="gameplay-settings-review-title"
            >
              <h3 id="gameplay-settings-review-title">
                {t("gameplaySettings.reviewTitle")}
              </h3>
              <p>{t("gameplaySettings.reviewDescription")}</p>
              <dl>
                {changedRows(preview.before.values, preview.after.values).map(
                  (row) => (
                    <div key={row.key}>
                      <dt>{t(`gameplaySettings.${row.key}`)}</dt>
                      <dd>
                        {formatValue(row.key, row.before, t)}
                        <span aria-hidden="true"> {">"} </span>
                        <span className="sr-only">
                          {t("gameplaySettings.changesTo")}
                        </span>
                        {formatValue(row.key, row.after, t)}
                      </dd>
                    </div>
                  ),
                )}
              </dl>
              <div className="gameplay-settings__actions">
                <button
                  type="button"
                  onClick={() => setPreview(null)}
                  disabled={busy !== null}
                >
                  {t("gameplaySettings.cancelReview")}
                </button>
                <button
                  type="button"
                  className="primary"
                  onClick={() => void apply()}
                  disabled={busy !== null || !canApply}
                >
                  {busy === "apply"
                    ? t("gameplaySettings.applying")
                    : t("gameplaySettings.apply")}
                </button>
              </div>
            </div>
          ) : null}
        </>
      ) : null}
    </section>
  );
}

export async function tryArmGameplaySettingsWriteGuard(
  armCriticalWriteGuard: () => Promise<boolean>,
): Promise<boolean> {
  try {
    return await armCriticalWriteGuard();
  } catch {
    return false;
  }
}

export function gameplaySettingsLoadOwnsBusy(
  mode: GameplaySettingsLoadMode,
): boolean {
  return mode === "standalone";
}

export function getGameplaySettingsApplyDisposition(
  response: Pick<ApplyGameplaySettingsUpdateResponse, "outcome">,
): ApplyGameplaySettingsUpdateResponse["outcome"] {
  return response.outcome;
}

export type GameplaySettingsApplyGuardController = {
  begin: (generation: number) => Promise<boolean>;
  end: (generation?: number) => void;
  isActive: (generation: number) => boolean;
};

export function createGameplaySettingsApplyGuardController(
  readArmCriticalWriteGuard: () => () => Promise<boolean>,
  readOnApplyBusyChange: () => ((isBusy: boolean) => void) | undefined,
): GameplaySettingsApplyGuardController {
  let activeGeneration: number | null = null;
  return {
    async begin(generation) {
      activeGeneration = generation;
      readOnApplyBusyChange()?.(true);
      return tryArmGameplaySettingsWriteGuard(readArmCriticalWriteGuard());
    },
    end(generation) {
      if (
        activeGeneration === null ||
        (generation !== undefined && activeGeneration !== generation)
      ) {
        return;
      }
      activeGeneration = null;
      readOnApplyBusyChange()?.(false);
    },
    isActive(generation) {
      return activeGeneration === generation;
    },
  };
}

export function hasGameplaySettingsOutputScope(
  scope: OutputSafetyScope | null,
): scope is OutputSafetyScope {
  return Boolean(
    scope?.paths.outputRootPath?.trim() && scope.paths.selectedGame,
  );
}

export function shouldRenderGameplaySettings(
  hideWhenUnavailable: boolean,
  state: GameplaySettingsState | null,
) {
  return !hideWhenUnavailable || (state !== null && isEditableState(state));
}

export function copyGameplaySettingsScope(
  scope: OutputSafetyScope,
): OutputSafetyScope {
  return {
    paths: {
      baseExeFsPath: scope.paths.baseExeFsPath,
      baseRomFsPath: scope.paths.baseRomFsPath,
      gameTextLanguage: scope.paths.gameTextLanguage,
      outputRootPath: scope.paths.outputRootPath,
      pokemonLegendsZASupportFolderPath:
        scope.paths.pokemonLegendsZASupportFolderPath,
      saveFilePath: scope.paths.saveFilePath,
      scarletVioletSupportFolderPath:
        scope.paths.scarletVioletSupportFolderPath,
      selectedGame: scope.paths.selectedGame,
    },
    projectId: scope.projectId,
  };
}

export function gameplaySettingsScopeKey(scope: OutputSafetyScope) {
  return JSON.stringify(copyGameplaySettingsScope(scope));
}

function isEditableState(state: GameplaySettingsState) {
  return state === "ready" || state === "repairable";
}

function sameValues(
  left: GameplaySettingsValues,
  right: GameplaySettingsValues,
) {
  return (
    left.experienceShareEnabled === right.experienceShareEnabled &&
    left.experienceRateBasisPoints === right.experienceRateBasisPoints &&
    left.levelCapEnabled === right.levelCapEnabled &&
    left.levelCap === right.levelCap
  );
}

type ChangedRow = {
  after: boolean | number;
  before: boolean | number;
  key: "experienceRate" | "experienceShare" | "levelCap";
};

function changedRows(
  before: GameplaySettingsValues,
  after: GameplaySettingsValues,
): ChangedRow[] {
  const rows: ChangedRow[] = [];
  if (before.experienceShareEnabled !== after.experienceShareEnabled) {
    rows.push({
      key: "experienceShare",
      before: before.experienceShareEnabled,
      after: after.experienceShareEnabled,
    });
  }
  if (before.experienceRateBasisPoints !== after.experienceRateBasisPoints) {
    rows.push({
      key: "experienceRate",
      before: before.experienceRateBasisPoints,
      after: after.experienceRateBasisPoints,
    });
  }
  if (
    before.levelCapEnabled !== after.levelCapEnabled ||
    before.levelCap !== after.levelCap
  ) {
    rows.push({
      key: "levelCap",
      before: before.levelCapEnabled ? before.levelCap : false,
      after: after.levelCapEnabled ? after.levelCap : false,
    });
  }
  return rows;
}

function formatValue(
  key: ChangedRow["key"],
  value: boolean | number,
  t: (key: string, values?: Record<string, string | number>) => string,
) {
  if (typeof value === "boolean") {
    return t(value ? "gameplaySettings.on" : "gameplaySettings.off");
  }
  if (key === "experienceRate") {
    return t("gameplaySettings.percent", { value: value / 100 });
  }
  return String(value);
}
