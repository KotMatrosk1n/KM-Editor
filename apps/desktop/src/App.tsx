/* SPDX-License-Identifier: GPL-3.0-only */

import {
  Activity,
  ClipboardCheck,
  FolderOpen,
  Layers,
  ListChecks,
  Search,
  ShieldCheck,
  type LucideIcon
} from 'lucide-react';
import { type WorkbenchSection, useWorkbenchStore } from './workbenchStore';

const sections: Array<{
  id: WorkbenchSection;
  label: string;
  icon: LucideIcon;
}> = [
  {
    id: 'health',
    label: 'Project Health',
    icon: Activity
  },
  {
    id: 'workflows',
    label: 'Workflows',
    icon: ListChecks
  },
  {
    id: 'changes',
    label: 'Changes',
    icon: ClipboardCheck
  }
];

const pathStates = [
  {
    label: 'Base RomFS',
    value: 'Not set'
  },
  {
    label: 'Base ExeFS',
    value: 'Not set'
  },
  {
    label: 'Output Root',
    value: 'Not set'
  }
];

export function App() {
  const activeSection = useWorkbenchStore((state) => state.activeSection);
  const setActiveSection = useWorkbenchStore((state) => state.setActiveSection);

  return (
    <main className="app-shell">
      <aside className="sidebar">
        <div className="brand">
          <Layers aria-hidden="true" size={24} />
          <span>KM Editor</span>
        </div>

        <nav aria-label="Workspace" className="section-nav">
          {sections.map((section) => {
            const Icon = section.icon;
            const isActive = activeSection === section.id;

            return (
              <button
                aria-current={isActive ? 'page' : undefined}
                aria-label={section.label}
                className="nav-button"
                key={section.id}
                onClick={() => setActiveSection(section.id)}
                type="button"
              >
                <Icon aria-hidden="true" size={18} />
                <span>{section.label}</span>
              </button>
            );
          })}
        </nav>
      </aside>

      <section className="workspace">
        <header className="toolbar">
          <div className="title-block">
            <p className="project-state">No project open</p>
            <h1>{sections.find((section) => section.id === activeSection)?.label}</h1>
          </div>

          <label className="search-box">
            <Search aria-hidden="true" size={18} />
            <input disabled placeholder="Search project" type="search" />
          </label>

          <button className="primary-button" type="button">
            <FolderOpen aria-hidden="true" size={18} />
            <span>Open Project</span>
          </button>
        </header>

        <div className="content-grid">
          <section aria-labelledby="paths-heading" className="panel">
            <div className="panel-heading">
              <ShieldCheck aria-hidden="true" size={18} />
              <h2 id="paths-heading">Paths</h2>
            </div>

            <dl className="path-list">
              {pathStates.map((pathState) => (
                <div className="path-row" key={pathState.label}>
                  <dt>{pathState.label}</dt>
                  <dd>{pathState.value}</dd>
                </div>
              ))}
            </dl>
          </section>

          <section aria-labelledby="session-heading" className="panel">
            <div className="panel-heading">
              <ClipboardCheck aria-hidden="true" size={18} />
              <h2 id="session-heading">Edit Session</h2>
            </div>

            <div className="session-summary">
              <span className="metric-value">0</span>
              <span className="metric-label">Pending changes</span>
            </div>
          </section>
        </div>
      </section>
    </main>
  );
}
