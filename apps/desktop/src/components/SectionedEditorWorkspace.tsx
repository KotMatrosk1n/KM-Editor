/* SPDX-License-Identifier: GPL-3.0-only */
import {
  Children, Fragment, isValidElement, useEffect, useId, useLayoutEffect, useRef, useState,
  type ReactElement, type ReactNode
} from 'react';
import { useAppearancePreferences } from '../features/settings/AppearancePreferencesProvider';
import { useLocalization } from '../localization';
import './sectionedEditorWorkspace.css';

type SectionProps = {
  id: string;
  label: string;
  modified?: boolean;
  invalid?: boolean;
  children: ReactNode;
};

// These markers let editors retain their existing controlled fields and draft owners.
export function EditorWorkspaceSection({ children }: SectionProps) { return <>{children}</>; }
type HeaderProps = { children: ReactNode; afterBrowser?: boolean };
export function EditorWorkspaceHeader({ children }: HeaderProps) { return <>{children}</>; }

type Session = { active: string | null; tableHeight: number | null };
const sessions = new Map<string, Session>();
function sessionFor(key: string, initial: string): Session {
  const remembered = sessions.get(key);
  if (remembered) return remembered;
  const session = { active: initial, tableHeight: null };
  sessions.set(key, session);
  if (sessions.size > 128) sessions.delete(sessions.keys().next().value!);
  return session;
}

function collect(children: ReactNode, sections: ReactElement<SectionProps>[], headers: ReactElement<HeaderProps>[], empty: ReactNode[]) {
  Children.forEach(children, child => {
    if (!isValidElement<{ children?: ReactNode }>(child)) return;
    if (child.type === Fragment) collect(child.props.children, sections, headers, empty);
    else if (child.type === EditorWorkspaceSection) sections.push(child as ReactElement<SectionProps>);
    else if (child.type === EditorWorkspaceHeader) headers.push(child as ReactElement<HeaderProps>);
    else empty.push(child);
  });
}

type WorkspaceProps = {
  sessionKey: string;
  initialSection?: string;
  browser?: ReactNode;
  browserControls?: ReactNode;
  browserVisibleRows?: number;
  sessionBar?: ReactNode;
  onActiveSectionChange?: (section: string | null) => void;
  children: ReactNode;
};

export function SectionedEditorWorkspace(props: WorkspaceProps) {
  return <WorkspaceSession key={props.sessionKey} {...props} />;
}

function WorkspaceSession({ sessionKey, initialSection = 'details', browser, browserControls, browserVisibleRows, sessionBar, onActiveSectionChange, children }: WorkspaceProps) {
  const { t } = useLocalization();
  const { preferences } = useAppearancePreferences();
  const [session] = useState(() => sessionFor(sessionKey, initialSection));
  const sections: ReactElement<SectionProps>[] = [];
  const headers: ReactElement<HeaderProps>[] = [];
  const empty: ReactNode[] = [];
  collect(children, sections, headers, empty);
  const [requested, setRequested] = useState<string | null>(session.active);
  const active = requested !== null && sections.some(section => section.props.id === requested)
    ? requested : requested === null ? null : sections[0]?.props.id ?? null;
  const [outgoing, setOutgoing] = useState<string | null>(null);
  const [tablePreference, setTablePreference] = useState(session.tableHeight);
  const [defaultTableHeight, setDefaultTableHeight] = useState(browserVisibleRows ? (browserVisibleRows + 1) * 40 + 2 : 164);
  const [tableMinimum, setTableMinimum] = useState(80);
  const [tableMax, setTableMax] = useState(280);
  const [systemReduce, setSystemReduce] = useState(() => window.matchMedia('(prefers-reduced-motion: reduce)').matches);
  const reduced = systemReduce || preferences.motion === 'reduce';
  const root = useRef<HTMLDivElement>(null);
  const chrome = useRef<HTMLDivElement>(null);
  const table = useRef<HTMLDivElement>(null);
  const surface = useRef<HTMLDivElement>(null);
  const settledHeight = useRef(0);
  const panels = useRef(new Map<string, HTMLDivElement>());
  const selectors = useRef(new Map<string, HTMLButtonElement>());
  const animations = useRef<Animation[]>([]);
  const generation = useRef(0);
  const previous = useRef(active);
  const prefix = useId();
  const tableMin = Math.min(tableMinimum, tableMax);
  const tableHeight = Math.max(tableMin, Math.min(tablePreference ?? defaultTableHeight, tableMax));

  useEffect(() => { onActiveSectionChange?.(active); }, [active, onActiveSectionChange]);

  useEffect(() => {
    const query = window.matchMedia('(prefers-reduced-motion: reduce)');
    const update = () => setSystemReduce(query.matches);
    query.addEventListener('change', update);
    return () => query.removeEventListener('change', update);
  }, []);

  useLayoutEffect(() => {
    let frame = 0;
    const measure = () => {
      if (!root.current || !chrome.current) return;
      const viewportBottom = (window.visualViewport?.height ?? window.innerHeight) + (window.visualViewport?.offsetTop ?? 0);
      const available = Math.max(160, viewportBottom - Math.max(0, root.current.getBoundingClientRect().top) - 18);
      const sessionHeight = root.current.querySelector(':scope > .editor-session-bar')?.getBoundingClientRect().height ?? 0;
      const fixed = chrome.current.scrollHeight - (table.current?.getBoundingClientRect().height ?? 0)
        + (sessionHeight ? sessionHeight + 12 : 0);
      const browserTable = table.current?.querySelector<HTMLElement>('[role="table"]');
      let minimumTableHeight = 80;
      if (browserVisibleRows && browserTable) {
        const style = getComputedStyle(browserTable);
        const rowHeight = parseFloat(style.getPropertyValue('--editor-row-height')) || 40;
        const headingHeight = browserTable.querySelector('[role="row"]')?.getBoundingClientRect().height ?? rowHeight;
        const borders = parseFloat(style.borderTopWidth) + parseFloat(style.borderBottomWidth);
        const scrollbar = browserTable.offsetHeight - browserTable.clientHeight - borders;
        setDefaultTableHeight(Math.ceil(headingHeight + browserVisibleRows * rowHeight + borders + scrollbar));
        minimumTableHeight = Math.ceil(headingHeight + rowHeight + borders + scrollbar);
      }
      setTableMinimum(minimumTableHeight);
      // Reserve a usable content area after measuring every persistent control, including wrapped labels.
      setTableMax(Math.max(minimumTableHeight, Math.floor(available - fixed - Math.min(220, available * .3))));
    };
    const schedule = () => { cancelAnimationFrame(frame); frame = requestAnimationFrame(measure); };
    const observer = new ResizeObserver(schedule);
    if (chrome.current) observer.observe(chrome.current);
    const sessionElement = root.current?.querySelector(':scope > .editor-session-bar');
    if (sessionElement) observer.observe(sessionElement);
    window.addEventListener('resize', schedule);
    window.visualViewport?.addEventListener('resize', schedule);
    measure();
    return () => {
      observer.disconnect();
      cancelAnimationFrame(frame);
      window.removeEventListener('resize', schedule);
      window.visualViewport?.removeEventListener('resize', schedule);
    };
  }, [browserVisibleRows]);

  useLayoutEffect(() => {
    const token = ++generation.current;
    const old = previous.current;
    previous.current = active;
    session.active = active;
    const container = surface.current;
    if (!container) return;
    // Capture the interrupted visual height before cancelling. The latest selection owns the transition.
    const from = animations.current.length ? container.getBoundingClientRect().height : settledHeight.current;
    const oldPanel = old ? panels.current.get(old) : undefined;
    const nextPanel = active ? panels.current.get(active) : undefined;
    const interruptedStyle = (panel: HTMLElement | undefined) => panel && animations.current.some(animation =>
      (animation.effect as KeyframeEffect | null)?.target === panel)
      ? { opacity: getComputedStyle(panel).opacity, transform: getComputedStyle(panel).transform } : undefined;
    const oldStyle = interruptedStyle(oldPanel);
    const nextStyle = interruptedStyle(nextPanel);
    animations.current.forEach(animation => animation.cancel());
    animations.current = [];
    if (oldPanel && old !== active && oldPanel.contains(document.activeElement)) {
      selectors.current.get(old!)?.focus({ preventScroll: true });
    }
    const destination = nextPanel?.firstElementChild?.getBoundingClientRect().height ?? 0;
    settledHeight.current = destination;
    if (reduced || old === active || typeof container.animate !== 'function') {
      container.style.height = 'auto';
      setOutgoing(null);
      return;
    }
    container.style.height = `${destination}px`;
    setOutgoing(old);
    const duration = old && active ? 180 : active ? 200 : 160;
    animations.current.push(container.animate([{ height: `${from}px` }, { height: `${destination}px` }], {
      duration, easing: active ? 'cubic-bezier(.2,.7,.2,1)' : 'ease-in'
    }));
    if (oldPanel) animations.current.push(oldPanel.animate([
      oldStyle ?? { opacity: 1, transform: 'translateY(0)' }, { opacity: 0, transform: 'translateY(6px)' }
    ], { duration: active ? 80 : 160, easing: 'ease-in', fill: 'forwards' }));
    if (nextPanel) animations.current.push(nextPanel.animate([
      nextStyle ?? { opacity: 0, transform: 'translateY(6px)' }, { opacity: 1, transform: 'translateY(0)' }
    ], { duration: old ? 130 : 200, delay: old ? 50 : 0, easing: 'ease-out', fill: 'backwards' }));
    void Promise.all(animations.current.map(animation => animation.finished.catch(() => undefined))).then(() => {
      if (token === generation.current) {
        setOutgoing(null);
        animations.current.forEach(animation => animation.cancel());
        animations.current = [];
        container.style.height = 'auto';
        settledHeight.current = container.getBoundingClientRect().height;
      }
    });
  }, [active, reduced, session]);

  useLayoutEffect(() => {
    const update = () => {
      if (surface.current && animations.current.length === 0) {
        settledHeight.current = surface.current.getBoundingClientRect().height;
      }
    };
    const observer = new ResizeObserver(update);
    if (surface.current) observer.observe(surface.current);
    update();
    return () => observer.disconnect();
  }, []);

  useEffect(() => () => {
    ++generation.current;
    animations.current.forEach(animation => animation.cancel());
  }, []);

  const resizeTable = (height: number) => {
    const value = Math.max(tableMin, Math.min(tableMax, height));
    session.tableHeight = value;
    setTablePreference(value);
  };

  return <div className="sectioned-editor-workspace" data-reduced-motion={reduced || undefined} ref={root}>
    {sessionBar}
    <div className="editor-workspace-chrome" ref={chrome}>
      {headers.filter(header => !header.props.afterBrowser).map((header, index) => <Fragment key={index}>{header}</Fragment>)}
      {browserControls}
      {browser ? <>
        <div className="editor-workspace-browser" ref={table} style={{ height: tableHeight }}>{browser}</div>
        <div className="editor-workspace-resizer" role="separator" tabIndex={0}
          aria-label={t('editorSections.resizeBrowser')} aria-orientation="horizontal"
          aria-valuemin={tableMin} aria-valuemax={tableMax} aria-valuenow={tableHeight}
          onKeyDown={event => {
            if (!['ArrowUp', 'ArrowDown', 'Home', 'End'].includes(event.key)) return;
            event.preventDefault();
            resizeTable(event.key === 'Home' ? tableMin : event.key === 'End' ? tableMax : tableHeight + (event.key === 'ArrowUp' ? -16 : 16));
          }}
          onPointerDown={event => {
            if (event.button !== 0) return;
            const handle = event.currentTarget;
            const start = event.clientY;
            const initial = tableHeight;
            handle.setPointerCapture(event.pointerId);
            const move = (pointer: PointerEvent) => resizeTable(initial + pointer.clientY - start);
            const finish = () => {
              handle.removeEventListener('pointermove', move);
              handle.removeEventListener('lostpointercapture', finish);
            };
            handle.addEventListener('pointermove', move);
            handle.addEventListener('lostpointercapture', finish);
          }} />
      </> : null}
      {headers.filter(header => header.props.afterBrowser).map((header, index) => <Fragment key={index}>{header}</Fragment>)}
      <div className="editor-section-selectors" aria-label={t('editorSections.sections')}>
        {sections.map(({ props: section }) => <button key={section.id} type="button"
          id={`${prefix}-${section.id}-selector`} aria-controls={`${prefix}-${section.id}-panel`}
          aria-expanded={active === section.id} className="editor-section-selector"
          data-active={active === section.id} data-modified={section.modified || undefined} data-invalid={section.invalid || undefined}
          ref={element => { if (element) selectors.current.set(section.id, element); else selectors.current.delete(section.id); }}
          onClick={() => {
            if (active && panels.current.get(active)?.contains(document.activeElement)) {
              selectors.current.get(active)?.focus({ preventScroll: true });
            }
            setRequested(active === section.id ? null : section.id);
          }}>
          <span className="editor-section-label" title={section.label}>{section.label}</span>
          <span className="editor-section-status">
            {section.modified ? <span className="editor-section-modified" title={t('editorSections.modified')} aria-label={t('editorSections.modified')}>●</span> : null}
            {section.invalid ? <span className="editor-section-invalid" title={t('editorSections.invalid')} aria-label={t('editorSections.invalid')}>!</span> : null}
          </span>
        </button>)}
      </div>
      {sections.length === 0 ? empty.map((content, index) => <Fragment key={index}>{content}</Fragment>) : null}
    </div>
    <div className="editor-workspace-slot">
      <div className="editor-workspace-surface" ref={surface}>
        {sections.map(({ props: section }) => {
          const visible = active === section.id;
          const leaving = outgoing === section.id && !visible;
          return <div id={`${prefix}-${section.id}-panel`} key={section.id} role="region"
            aria-labelledby={`${prefix}-${section.id}-selector`} aria-hidden={!visible || undefined}
            tabIndex={visible ? 0 : -1}
            hidden={!visible && !leaving} inert={!visible} className="editor-workspace-section"
            data-outgoing={leaving || undefined}
            ref={element => { if (element) panels.current.set(section.id, element); else panels.current.delete(section.id); }}>
            <div className="editor-workspace-section-content">{section.children}</div>
          </div>;
        })}
      </div>
    </div>
  </div>;
}
