/* SPDX-License-Identifier: GPL-3.0-only */
import { useEffect, useRef, useState, type ReactNode } from 'react';
import { ArrowRight, BookOpen, ChevronRight, FileText, GitFork, Play, Settings, TriangleAlert, type LucideIcon } from 'lucide-react';
import type { ProjectGame } from '../../bridge/contracts';
import { useLocalization } from '../../localization';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { githubIssuesUrl } from '../../errorReporting';
import { getSectionWikiUrl } from '../../wikiLinks';
import { appliesToGame, contentText, gameFamily, readWelcomeGame, rememberWelcomeGame, welcomeContent, type WelcomeContent } from './welcomeContent';
import './WelcomeHub.css';

type HubTab = 'news' | 'start';
type GameDefinition = { icon: LucideIcon; label: string };
export type WelcomeHubProps = {
  games: readonly ProjectGame[]; definitions: Record<ProjectGame, GameDefinition>; logo: string; version: string;
  configuredGames: readonly ProjectGame[]; currentGame: ProjectGame | null; isLoading: boolean;
  onSelectionChange?: (game: ProjectGame) => void;
  onCancel?: () => void; onOpenGame: (game: ProjectGame) => Promise<void>;
  onOpenLink: (url: string) => Promise<void>; settings: ReactNode; diagnostics?: ReactNode;
  content?: WelcomeContent;
};
export default function WelcomeHub({ games, definitions, logo, version, configuredGames, currentGame, isLoading, onCancel,
  onSelectionChange, onOpenGame, onOpenLink, settings, diagnostics, content = welcomeContent }: WelcomeHubProps) {
  const { t, interfaceLocale, translateLiteral } = useLocalization();
  const [game, setGame] = useState<ProjectGame>(() => currentGame ?? readWelcomeGame());
  useEffect(() => { onSelectionChange?.(game); }, [game, onSelectionChange]);
  const [tab, setTab] = useState<HubTab>('news');
  const [familyOnly, setFamilyOnly] = useState(false);
  const [expanded, setExpanded] = useState(false);
  const [showSettings, setShowSettings] = useState(false);
  const [opening, setOpening] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  usePublishCommonEditorError({ domain: 'welcome', message: actionError });
  const openingRef = useRef(false);
  const tabRefs = useRef<(HTMLButtonElement | null)[]>([]);
  const configured = configuredGames.includes(game);
  const busy = isLoading || opening;
  const GameIcon = definitions[game].icon;
  const gameName = translateLiteral(definitions[game].label);
  const release = content.releases.find(item => item.version === version);
  const announcements = content.announcements.filter(item => appliesToGame(item.audience, game));
  const featured = announcements.find(item => item.featured);
  const tabs = [{ id: 'news', icon: FileText }, { id: 'start', icon: BookOpen }] as const;
  const text = (value: Parameters<typeof contentText>[0]) => contentText(value, interfaceLocale);
  const selectGame = (value: ProjectGame) => { setGame(value); rememberWelcomeGame(value); setActionError(null); };
  async function open() {
    if (busy || openingRef.current) return;
    openingRef.current = true; setOpening(true); setActionError(null);
    try { await onOpenGame(game); } catch { setActionError(t('welcome.openError')); }
    finally { openingRef.current = false; setOpening(false); }
  }
  async function openLink(url: string) {
    try { await onOpenLink(url); } catch { setActionError(t('welcome.linkError')); }
  }
  const resource = (label: string, url: string | null, Icon: LucideIcon) => url ? <a href={url} rel="noreferrer" onClick={event => {
    event.preventDefault(); void openLink(url);
  }}><Icon size={21} aria-hidden="true" /><span>{label}</span><ChevronRight size={16} aria-hidden="true" /></a> : null;
  const links = (items: WelcomeContent['announcements'][number]['links']) => items.length ? <div className="welcome-content-links" data-localization-ignore="true">
    {items.map(item => <a key={item.url} href={item.url} rel="noreferrer" onClick={event => { event.preventDefault(); void openLink(item.url); }}>{text(item.label)}<ArrowRight size={15} aria-hidden="true" /></a>)}
  </div> : null;
  const openLabel = t(configured ? 'welcome.openGame' : 'welcome.setupGame', { game: gameName });
  return <main className="game-selection-shell welcome-hub">
    <aside className="welcome-sidebar" aria-label={t('welcome.choose')}>
      <header className="welcome-brand"><img className="game-selection-logo" src={logo} alt="" /><div><strong>KM Editor</strong><span>v{version}</span><small className="brand-credit">{translateLiteral('Made by Matroskin')}</small></div></header>
      <h2>{t('welcome.choose')}</h2>
      <nav className="welcome-games" aria-label={t('welcome.choose')}>
        {games.map(value => { const Icon = definitions[value].icon; return <button type="button" key={value} aria-pressed={game === value}
          disabled={busy} onClick={() => selectGame(value)} className="welcome-game">
          <Icon size={26} aria-hidden="true" /><span><strong>{translateLiteral(definitions[value].label)}</strong>
            <small className={`welcome-status${configuredGames.includes(value) ? ' is-configured' : ''}`}>{t(configuredGames.includes(value) ? 'welcome.configured' : 'welcome.setup')}</small></span>
          <ChevronRight size={16} aria-hidden="true" /></button>; })}
      </nav>
      <nav className="welcome-resources" aria-label={t('welcome.resources')}>
        {resource(t('welcome.documentation'), 'https://github.com/KotMatrosk1n/KM-Editor/wiki', BookOpen)}
        {resource('GitHub', 'https://github.com/KotMatrosk1n/KM-Editor', GitFork)}
        {resource(t('welcome.report'), githubIssuesUrl, TriangleAlert)}
        <button type="button" aria-pressed={showSettings} onClick={() => setShowSettings(value => !value)}><Settings size={21} aria-hidden="true" /><span>{t('welcome.settings')}</span><ChevronRight size={16} aria-hidden="true" /></button>
        {onCancel ? <button type="button" disabled={busy} onClick={onCancel}><ArrowRight size={21} aria-hidden="true" /><span>{t('welcome.return')}</span></button> : null}
      </nav>
    </aside>
    <div className="welcome-main">
      <header className="welcome-game-header">
        <div className="welcome-game-emblem"><GameIcon size={40} aria-hidden="true" /></div>
        <div className="welcome-game-intro"><h1>{gameName}</h1><span className={`welcome-status${configured ? ' is-configured' : ''}`}>{t(configured ? 'welcome.projectConfigured' : 'welcome.projectSetup')}</span>
          <p>{t('welcome.description', { game: gameName })}</p></div>
        <button className="welcome-open primary-button" type="button" disabled={busy} aria-busy={busy || undefined} onClick={() => void open()}><Play size={23} aria-hidden="true" /><span>{busy ? t('welcome.loading') : openLabel}</span><ChevronRight size={17} aria-hidden="true" /></button>
        <button className="welcome-return-picker secondary-button" type="button" onClick={() => {
          const selected = document.querySelector<HTMLButtonElement>('.welcome-game[aria-pressed="true"]');
          selected?.scrollIntoView({ block: 'center' }); selected?.focus({ preventScroll: true });
        }}>{t('welcome.choose')}</button>
      </header>
      <div className="welcome-feed">
        <section className="welcome-content">
          <div className="welcome-tabs" role="tablist" aria-label={t('welcome.updates')}>
            {tabs.map(({ id, icon: Icon }, index) => <button type="button" key={id} role="tab" id={`welcome-tab-${id}`} aria-controls={`welcome-panel-${id}`}
              aria-selected={!showSettings && tab === id} tabIndex={tab === id ? 0 : -1} ref={node => { tabRefs.current[index] = node; }}
              onClick={() => { setShowSettings(false); setTab(id); }} onKeyDown={event => {
                const next = event.key === 'ArrowRight' ? (index + 1) % tabs.length : event.key === 'ArrowLeft' ? (index + tabs.length - 1) % tabs.length : event.key === 'Home' ? 0 : event.key === 'End' ? tabs.length - 1 : -1;
                if (next >= 0) { event.preventDefault(); setShowSettings(false); setTab(tabs[next].id); tabRefs.current[next]?.focus(); }
              }}><Icon size={20} aria-hidden="true" /><span>{t(`welcome.tab.${id}`)}</span></button>)}
          </div>
          {showSettings ? <section className="welcome-settings" aria-label={t('welcome.settings')}>{settings}</section> :
            <div className="welcome-panel" id={`welcome-panel-${tab}`} role="tabpanel" aria-labelledby={`welcome-tab-${tab}`} tabIndex={0}>
              {tab === 'news' ? <>
                <div className="welcome-release-heading"><div><span className="welcome-eyebrow">{t('welcome.installed')}</span><h2>{t('welcome.version', { version })}</h2></div>
                  <div className="welcome-filters" role="group" aria-label={t('welcome.filter')}><button type="button" aria-pressed={!familyOnly} onClick={() => setFamilyOnly(false)}>{t('welcome.allGames')}</button>
                    <button type="button" aria-pressed={familyOnly} onClick={() => setFamilyOnly(true)}>{t(`welcome.family.${gameFamily(game)}`)}</button></div></div>
                <p className="welcome-filter-help">{t(familyOnly ? 'welcome.sharedIncluded' : 'welcome.allIncluded')}</p>
                {release ? <>
                  <p className="welcome-release-summary" data-localization-ignore="true">{text(release.summary)}</p>
                  {release.sections.map((section, index) => {
                    const items = section.items.filter(item => !familyOnly || appliesToGame(item.audience, game));
                    if (!items.length) return null;
                    return <section className="welcome-release-section" key={index} data-localization-ignore="true"><h3>{text(section.title)}</h3><ul>{(expanded ? items : items.slice(0, 2)).map((item, i) => <li key={i}>{text(item.body)}</li>)}</ul></section>;
                  })}
                  <button className="secondary-button welcome-expand" type="button" aria-expanded={expanded} onClick={() => setExpanded(value => !value)}>{t(expanded ? 'welcome.less' : 'welcome.complete')}</button>
                  {links(release.links)}
                </> : <p className="welcome-empty">{t('welcome.noRelease')}</p>}
              </> : <>
                <span className="welcome-eyebrow">{gameName}</span><h2>{t('welcome.tab.start')}</h2><p>{t('welcome.startIntro')}</p>
                <ol className="welcome-steps"><li><h3>{t('welcome.step.setup')}</h3><p>{t(`welcome.start.${gameFamily(game)}`)}</p></li>
                  <li><h3>{t('welcome.step.validate')}</h3><p>{t('welcome.validateHelp')}</p></li><li><h3>{t('welcome.step.edit')}</h3><p>{t('welcome.editHelp')}</p></li></ol>
                <div className="welcome-guide-links">{resource(t('welcome.setupGuide'), getSectionWikiUrl('health', game), BookOpen)}{resource(t('welcome.workflowGuide'), getSectionWikiUrl('workflows', game), BookOpen)}</div>
              </>}
            </div>}
        </section>
        {featured && !showSettings ? <aside className="welcome-featured"><img src={logo} alt="" /><div><span className="welcome-eyebrow">{t('welcome.developer')}</span><div data-localization-ignore="true"><h3>{text(featured.title)}</h3><p>{text(featured.body)}</p>{links(featured.links)}</div></div></aside> : null}
        {diagnostics}
      </div>
    </div>
  </main>;
}
