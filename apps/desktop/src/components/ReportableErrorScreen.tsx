/* SPDX-License-Identifier: GPL-3.0-only */

import { AlertTriangle, ExternalLink } from 'lucide-react';
import {
  useEffect,
  useId,
  useRef,
  useState,
  useSyncExternalStore,
  type MouseEvent,
  type ReactNode
} from 'react';
import { desktopServices } from '../desktopServices';
import {
  clearGlobalReportableError,
  getGlobalReportableErrorSnapshot,
  githubIssuesUrl,
  isReportableErrorMessage,
  subscribeGlobalReportableError,
  type ReportableError
} from '../errorReporting';
import { useLocalization } from '../localization';
import { useModalDialog } from './useModalDialog';

const maximumDisplayedReportMessageCharacters = 2_000;

export function GlobalReportableErrorHost({ children }: { children: ReactNode }) {
  const report = useSyncExternalStore(
    subscribeGlobalReportableError,
    getGlobalReportableErrorSnapshot,
    getGlobalReportableErrorSnapshot
  );
  return (
    <>
      {children}
      {report ? (
        <GlobalReportableErrorDialog
          onClose={clearGlobalReportableError}
          report={report}
        />
      ) : null}
    </>
  );
}

export function ReportableErrorScreen({ report }: { report: ReportableError }) {
  const panelRef = useRef<HTMLElement>(null);
  const isMountedRef = useRef(true);
  const headingId = useId();
  const descriptionId = useId();
  const [isClosing, setIsClosing] = useState(false);

  useEffect(() => {
    isMountedRef.current = true;
    panelRef.current?.focus({ preventScroll: true });
    return () => {
      isMountedRef.current = false;
    };
  }, []);

  const handleClose = () => {
    if (isClosing) return;
    setIsClosing(true);
    void (async () => {
      try {
        if (desktopServices.isAvailable) {
          await desktopServices.setCloseGuardEnabled(false).catch(() => undefined);
          await desktopServices.exitApp();
        } else {
          window.close();
        }
      } catch {
        if (isMountedRef.current) setIsClosing(false);
      }
    })();
  };

  return (
    <main className="fatal-error-screen">
      <section
        aria-describedby={descriptionId}
        aria-labelledby={headingId}
        className="panel fatal-error-panel"
        ref={panelRef}
        role="alert"
        tabIndex={-1}
      >
        <ReportableErrorNotice
          descriptionId={descriptionId}
          headingId={headingId}
          report={report}
        />
        <div className="fatal-error-actions">
          <GitHubIssuesLink />
          <CloseApplicationButton isClosing={isClosing} onClick={handleClose} />
        </div>
      </section>
    </main>
  );
}

function GlobalReportableErrorDialog({
  onClose,
  report
}: {
  onClose: () => void;
  report: ReportableError;
}) {
  const { translateLiteral } = useLocalization();
  const headingId = useId();
  const descriptionId = useId();
  const dialogRef = useModalDialog<HTMLDivElement>({ onClose });
  return (
    <div className="modal-backdrop reportable-error-backdrop" role="presentation">
      <div
        aria-describedby={descriptionId}
        aria-labelledby={headingId}
        aria-modal="true"
        className="modal-panel reportable-error-dialog"
        ref={dialogRef}
        role="alertdialog"
        tabIndex={-1}
      >
        <ReportableErrorNotice
          descriptionId={descriptionId}
          headingId={headingId}
          report={report}
        />
        <div className="fatal-error-actions">
          <GitHubIssuesLink />
          <button className="secondary-button" onClick={onClose} type="button">
            {translateLiteral('Close')}
          </button>
        </div>
      </div>
    </div>
  );
}

function ReportableErrorNotice({
  descriptionId,
  headingId,
  report
}: {
  descriptionId: string;
  headingId: string;
  report: ReportableError;
}) {
  const { translateLiteral } = useLocalization();
  return (
    <>
      <div className="panel-heading">
        <AlertTriangle aria-hidden="true" size={20} />
        <h1 id={headingId}>{translateLiteral(report.title)}</h1>
      </div>
      <div className="fatal-error-code">
        {report.semanticCode ? (
          <>
            <span>{translateLiteral('Error code')}</span>
            <code data-localization-ignore="true">{report.semanticCode}</code>
          </>
        ) : null}
        <span>{translateLiteral('Incident fingerprint')}</span>
        <code data-localization-ignore="true">{report.incidentFingerprint}</code>
      </div>
      <p id={descriptionId}>
        {translateLiteral(
          'Take a screenshot of this message and report it in GitHub Issues. Restart KM Editor before trying the same action again.'
        )}
      </p>
      <p className="reportable-error-message" data-localization-ignore="true">
        {boundedReportMessage(report.message)}
      </p>
    </>
  );
}

function CloseApplicationButton({
  isClosing,
  onClick
}: {
  isClosing: boolean;
  onClick: () => void;
}) {
  const { translateLiteral } = useLocalization();
  return (
    <button
      aria-busy={isClosing}
      className="secondary-button"
      disabled={isClosing}
      onClick={onClick}
      type="button"
    >
      {translateLiteral('Close')}
    </button>
  );
}

export function ReportableDiagnosticIssuesLink({
  messages
}: {
  messages: readonly string[];
}) {
  return messages.some(isReportableErrorMessage) ? <GitHubIssuesLink compact /> : null;
}

function GitHubIssuesLink({ compact = false }: { compact?: boolean }) {
  const { translateLiteral } = useLocalization();
  const handleOpen = (event: MouseEvent<HTMLAnchorElement>) => {
    if (!desktopServices.isAvailable) return;
    event.preventDefault();
    void desktopServices.openExternalUrl(githubIssuesUrl).catch(() => {
      window.open(githubIssuesUrl, '_blank', 'noopener,noreferrer');
    });
  };

  return (
    <a
      className={compact ? 'reportable-error-link' : 'secondary-button reportable-error-link'}
      href={githubIssuesUrl}
      onClick={handleOpen}
      rel="noopener noreferrer"
      target="_blank"
    >
      {translateLiteral('Open GitHub Issues')}
      <ExternalLink aria-hidden="true" size={compact ? 14 : 16} />
    </a>
  );
}

function boundedReportMessage(value: string) {
  const characters = [...value.slice(0, maximumDisplayedReportMessageCharacters + 1)];
  return characters.length <= maximumDisplayedReportMessageCharacters
    ? characters.join('')
    : `${characters.slice(0, maximumDisplayedReportMessageCharacters - 3).join('')}...`;
}
