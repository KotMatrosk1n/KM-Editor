/* SPDX-License-Identifier: GPL-3.0-only */

import {
  Binary,
  FileDiff,
  FolderOpen,
  MessageSquarePlus,
  X
} from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import {
  researchLabDefaultPageSize,
  researchLabMaximumAccumulatedFindings,
  researchLabMaximumByteWindowLength,
  researchLabMaximumSelectedFiles,
  researchRevisionIdentity,
  type ResearchAnnotationTarget,
  type ResearchByteWindowSide,
  type ResearchFileFinding
} from '../../bridge/researchLabContracts';
import type { SemanticExploreRevision } from '../../bridge/semanticExploreContracts';
import { useLocalization } from '../../localization';
import {
  researchDifferenceKey,
  researchErrorKey,
  researchRangeCoverageKey
} from './researchLabPresentation';
import type { ResearchLabController } from './useResearchLabController';

export function ResearchComparisonView({
  controller,
  onCreateAnnotation,
  onPickSource,
  revision
}: {
  controller: ResearchLabController;
  onCreateAnnotation: (target: ResearchAnnotationTarget) => void;
  onPickSource: (slot: 0 | 1) => Promise<string | null>;
  revision: SemanticExploreRevision;
}) {
  const { t } = useLocalization();
  const [selectedPaths, setSelectedPaths] = useState<Set<string>>(new Set());
  const pickerGenerationRef = useRef<[number, number]>([0, 0]);
  const isMountedRef = useRef(true);
  const sourceIdentity = [
    researchRevisionIdentity(revision),
    ...controller.sources.map((source) => (
      `${source.data?.sourceId ?? 'none'}:${source.status}`
    ))
  ].join('|');

  useEffect(() => {
    isMountedRef.current = true;
    return () => {
      isMountedRef.current = false;
      pickerGenerationRef.current[0] += 1;
      pickerGenerationRef.current[1] += 1;
    };
  }, []);
  useEffect(() => {
    setSelectedPaths(new Set());
    controller.clearByteWindow();
    pickerGenerationRef.current[0] += 1;
    pickerGenerationRef.current[1] += 1;
  }, [sourceIdentity]);

  const pickSource = async (slot: 0 | 1) => {
    const generation = ++pickerGenerationRef.current[slot];
    const rootPath = await onPickSource(slot);
    if (
      rootPath === null ||
      !isMountedRef.current ||
      pickerGenerationRef.current[slot] !== generation
    ) return;
    await controller.openSource(slot, rootPath);
  };
  const data = controller.comparison.data;
  const canCompare = controller.sources.every((source) => (
    source.status === 'ready' &&
    source.data !== null &&
    Date.parse(source.data.expiresAtUtc) > Date.now()
  ));
  const isWindowCapped = Boolean(
    data?.nextCursor &&
    data.items.length + researchLabDefaultPageSize > researchLabMaximumAccumulatedFindings
  );

  return (
    <section
      aria-labelledby="research-lab-tab-comparison"
      className="km-research-lab-panel"
      id="research-lab-panel-comparison"
      role="tabpanel"
    >
      <div className="km-research-lab-panel-heading">
        <div>
          <h3 id="research-comparison-title">{t('researchLab.comparison.title')}</h3>
          <p>{t('researchLab.comparison.description')}</p>
        </div>
        {controller.isBusy ? (
          <button
            className="secondary-button compact-button"
            onClick={controller.cancel}
            type="button"
          >
            <X aria-hidden="true" size={14} />
            <span>{t('researchLab.stopWaiting')}</span>
          </button>
        ) : null}
      </div>

      <p className="km-research-lab-help">{t('researchLab.cancellationHelp')}</p>
      <div className="km-research-lab-source-grid">
        {([0, 1] as const).map((slot) => (
          <SourceCard
            key={slot}
            controller={controller}
            onPick={() => void pickSource(slot)}
            slot={slot}
          />
        ))}
      </div>

      <div className="km-research-lab-toolbar">
        <button
          className="primary-button compact-button"
          disabled={!canCompare || controller.isBusy}
          onClick={() => void controller.compare([...selectedPaths])}
          type="button"
        >
          <FileDiff aria-hidden="true" size={15} />
          <span>{t(data
            ? 'researchLab.comparison.refresh'
            : 'researchLab.comparison.run')}</span>
        </button>
        {selectedPaths.size > 0 ? (
          <>
            <span role="status">
              {t('researchLab.comparison.selected', {
                count: selectedPaths.size,
                maximum: researchLabMaximumSelectedFiles
              })}
            </span>
            <button
              className="secondary-button compact-button"
              disabled={controller.isBusy}
              onClick={() => setSelectedPaths(new Set())}
              type="button"
            >
              {t('researchLab.comparison.clearSelection')}
            </button>
          </>
        ) : null}
      </div>

      {controller.comparison.status === 'loading' && !data ? (
        <Status messageKey="researchLab.comparison.loading" />
      ) : null}
      {controller.comparison.error ? (
        <Status error messageKey={researchErrorKey(controller.comparison.error)} />
      ) : null}
      {data ? (
        <>
          <p aria-live="polite" className="km-research-lab-result-count">
            {t('researchLab.comparison.loaded', { count: data.items.length })}
          </p>
          {data.items.length === 0 ? (
            <p className="km-research-lab-empty">{t('researchLab.comparison.empty')}</p>
          ) : (
            <ol aria-label={t('researchLab.comparison.results')} className="km-research-lab-results">
              {data.items.map((finding) => (
                <FindingCard
                  checked={selectedPaths.has(finding.relativePath)}
                  controller={controller}
                  disabled={
                    !selectedPaths.has(finding.relativePath) &&
                    selectedPaths.size >= researchLabMaximumSelectedFiles
                  }
                  finding={finding}
                  key={finding.findingId}
                  onAnnotateFinding={() => onCreateAnnotation({
                    finding: {
                      comparisonFingerprint: data.comparisonFingerprint,
                      findingId: finding.findingId,
                      relativePath: finding.relativePath
                    },
                    kind: 'finding',
                    relativeRange: null,
                    revision,
                    semanticRecord: null,
                    semanticSnapshot: null
                  })}
                  onAnnotateRange={(offset, length) => onCreateAnnotation({
                    finding: null,
                    kind: 'relativeRange',
                    relativeRange: {
                      comparisonFingerprint: data.comparisonFingerprint,
                      length,
                      offset,
                      relativePath: finding.relativePath
                    },
                    revision,
                    semanticRecord: null,
                    semanticSnapshot: null
                  })}
                  onToggle={(checked) => setSelectedPaths((current) => {
                    const next = new Set(current);
                    if (checked) next.add(finding.relativePath);
                    else next.delete(finding.relativePath);
                    return next;
                  })}
                />
              ))}
            </ol>
          )}
          {data.nextCursor && !isWindowCapped ? (
            <button
              className="secondary-button compact-button"
              disabled={controller.isBusy}
              onClick={() => void controller.loadMore()}
              type="button"
            >
              {controller.comparison.isAppending
                ? t('researchLab.comparison.loadingMore')
                : t('researchLab.comparison.more')}
            </button>
          ) : null}
          {isWindowCapped ? (
            <p className="km-research-lab-inline-status" role="status">
              {t('researchLab.comparison.windowLimit', {
                count: researchLabMaximumAccumulatedFindings
              })}
            </p>
          ) : null}
        </>
      ) : null}
    </section>
  );
}

function SourceCard({
  controller,
  onPick,
  slot
}: {
  controller: ResearchLabController;
  onPick: () => void;
  slot: 0 | 1;
}) {
  const { t } = useLocalization();
  const source = controller.sources[slot];
  const labelKey = slot === 0 ? 'researchLab.source.a' : 'researchLab.source.b';
  return (
    <article className="km-research-lab-source">
      <strong>{t(labelKey)}</strong>
      <span>{t(source.data
        ? 'researchLab.source.registered'
        : 'researchLab.source.private')}</span>
      {source.error ? (
        <span role="alert">{t(researchErrorKey(source.error))}</span>
      ) : null}
      <div className="km-research-lab-source-actions">
        <button
          className="secondary-button compact-button"
          disabled={source.status === 'loading'}
          onClick={onPick}
          type="button"
        >
          <FolderOpen aria-hidden="true" size={14} />
          <span>{t(source.data ? 'researchLab.source.replace' : 'researchLab.source.choose')}</span>
        </button>
        {source.data ? (
          <button
            className="secondary-button compact-button"
            disabled={source.status === 'loading'}
            onClick={() => void controller.clearSource(slot)}
            type="button"
          >
            {t('researchLab.source.clear')}
          </button>
        ) : null}
      </div>
    </article>
  );
}

function FindingCard({
  checked,
  controller,
  disabled,
  finding,
  onAnnotateFinding,
  onAnnotateRange,
  onToggle
}: {
  checked: boolean;
  controller: ResearchLabController;
  disabled: boolean;
  finding: ResearchFileFinding;
  onAnnotateFinding: () => void;
  onAnnotateRange: (offset: number, length: number) => void;
  onToggle: (checked: boolean) => void;
}) {
  const { t } = useLocalization();
  const window = controller.byteWindow.findingId === finding.findingId
    ? controller.byteWindow
    : null;
  const firstRange = finding.ranges[0] ?? null;
  const offset = firstRange?.offset ?? 0;
  const maximumLength = Math.max(finding.sourceA.length ?? 0, finding.sourceB.length ?? 0);
  const windowLength = Math.min(
    researchLabMaximumByteWindowLength,
    firstRange?.length ?? Math.max(0, maximumLength - offset)
  );
  return (
    <li>
      <article className="km-research-lab-card">
        <header>
          <div className="km-research-lab-card-title">
            <FileDiff aria-hidden="true" size={17} />
            <div>
              <h4 data-localization-ignore="true">{finding.relativePath}</h4>
              <p>{t(researchDifferenceKey(finding.differenceKind))}</p>
            </div>
          </div>
          <label>
            <input
              checked={checked}
              disabled={disabled}
              onChange={(event) => onToggle(event.target.checked)}
              type="checkbox"
            />
            <span>{t('researchLab.comparison.includeRanges')}</span>
          </label>
        </header>
        <dl className="km-research-lab-facts">
          <div>
            <dt>{t('researchLab.comparison.sourceABytes')}</dt>
            <dd>{formatLength(finding.sourceA.length, t('researchLab.comparison.missing'))}</dd>
          </div>
          <div>
            <dt>{t('researchLab.comparison.sourceBBytes')}</dt>
            <dd>{formatLength(finding.sourceB.length, t('researchLab.comparison.missing'))}</dd>
          </div>
          <div>
            <dt>{t('researchLab.comparison.rangeCoverage')}</dt>
            <dd>{t(researchRangeCoverageKey(finding.rangeCoverage))}</dd>
          </div>
          <div>
            <dt>{t('researchLab.comparison.rangeCount')}</dt>
            <dd>{finding.ranges.length.toLocaleString()}</dd>
          </div>
        </dl>
        <div className="km-research-lab-card-actions">
          <button
            className="secondary-button compact-button"
            disabled={windowLength < 1 || controller.isBusy}
            onClick={() => void controller.loadByteWindow(finding, offset, windowLength)}
            type="button"
          >
            <Binary aria-hidden="true" size={14} />
            <span>{t('researchLab.byteWindow.open')}</span>
          </button>
          <button
            className="secondary-button compact-button"
            onClick={onAnnotateFinding}
            type="button"
          >
            <MessageSquarePlus aria-hidden="true" size={14} />
            <span>{t('researchLab.annotations.addFinding')}</span>
          </button>
          {firstRange ? (
            <button
              className="secondary-button compact-button"
              onClick={() => onAnnotateRange(firstRange.offset, firstRange.length)}
              type="button"
            >
              {t('researchLab.annotations.addRange')}
            </button>
          ) : null}
        </div>
        {window?.status === 'loading' ? (
          <Status messageKey="researchLab.byteWindow.loading" />
        ) : null}
        {window?.error ? <Status error messageKey={researchErrorKey(window.error)} /> : null}
        {window?.data ? (
          <ByteWindow
            onClose={controller.clearByteWindow}
            offset={window.data.offset}
            sourceA={window.data.sourceA}
            sourceB={window.data.sourceB}
          />
        ) : null}
      </article>
    </li>
  );
}

function ByteWindow({
  offset,
  onClose,
  sourceA,
  sourceB
}: {
  offset: number;
  onClose: () => void;
  sourceA: ResearchByteWindowSide;
  sourceB: ResearchByteWindowSide;
}) {
  const { t } = useLocalization();
  return (
    <section aria-label={t('researchLab.byteWindow.title')}>
      <div className="km-research-lab-panel-heading">
        <div>
          <h4>{t('researchLab.byteWindow.title')}</h4>
          <p>{t('researchLab.byteWindow.ephemeral')}</p>
        </div>
        <button className="secondary-button compact-button" onClick={onClose} type="button">
          {t('researchLab.byteWindow.close')}
        </button>
      </div>
      <div className="km-research-lab-source-grid">
        <ByteSide label={t('researchLab.source.a')} offset={offset} side={sourceA} />
        <ByteSide label={t('researchLab.source.b')} offset={offset} side={sourceB} />
      </div>
    </section>
  );
}

function ByteSide({
  label,
  offset,
  side
}: {
  label: string;
  offset: number;
  side: ResearchByteWindowSide;
}) {
  const { t } = useLocalization();
  return (
    <div>
      <strong>{label}</strong>
      {side.exists ? (
        <pre className="km-research-lab-byte-window" data-localization-ignore="true">
          {formatHexWindow(side.bytesBase64!, offset)}
        </pre>
      ) : (
        <p>{t('researchLab.comparison.missing')}</p>
      )}
    </div>
  );
}

function Status({ error = false, messageKey }: { error?: boolean; messageKey: string }) {
  const { t } = useLocalization();
  return (
    <div
      aria-live="polite"
      className="km-research-lab-inline-status"
      role={error ? 'alert' : 'status'}
    >
      <span>{t(messageKey)}</span>
    </div>
  );
}

function formatLength(length: number | null, missing: string) {
  return length === null ? missing : length.toLocaleString();
}

function formatHexWindow(base64: string, initialOffset: number) {
  const binary = atob(base64);
  const bytes = Array.from(binary, (character) => character.charCodeAt(0));
  if (bytes.length === 0) return '';
  const lines: string[] = [];
  for (let index = 0; index < bytes.length; index += 16) {
    const slice = bytes.slice(index, index + 16);
    const address = (initialOffset + index).toString(16).padStart(8, '0');
    const hex = slice.map((value) => value.toString(16).padStart(2, '0')).join(' ');
    const text = slice.map((value) => value >= 32 && value <= 126
      ? String.fromCharCode(value)
      : '.').join('');
    lines.push(`${address}  ${hex.padEnd(47, ' ')}  ${text}`);
  }
  return lines.join('\n');
}
