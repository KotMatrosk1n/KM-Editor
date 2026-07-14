import { describe, expect, it } from 'vitest';

import styles from './styles.css?inline';

describe('editor table layout', () => {
  it('covers every horizontally scrollable sticky header family', () => {
    const stickyRowHeadings = Array.from(
      styles.matchAll(/(\.[a-z0-9-]+-row-heading)\s*\{[^}]*position:\s*sticky/gms),
      (match) => match[1]
    );

    expect(stickyRowHeadings.length).toBeGreaterThan(0);

    for (const headingSelector of stickyRowHeadings) {
      expect(styles).toContain(`${headingSelector}::before`);
    }

    expect(styles).toContain('inset-inline: -100vw;');
    expect(styles).toContain('background: var(--editor-table-heading-background);');
    expect(styles).toContain('isolation: isolate;');
  });

  it('lets horizontal-only editor scrollers chain vertical wheel input', () => {
    const horizontalScrollerRule = styles.match(
      /\.type-chart-scroll,\s*\.encounter-area-tabs,\s*\.encounter-condition-tabs,\s*\.encounter-slot-tabs,\s*\.raid-reward-slot-grid\s*\{([^}]*)\}/s
    )?.[1];

    expect(horizontalScrollerRule).toBeDefined();
    expect(horizontalScrollerRule).toMatch(/overflow-x:\s*auto;/);
    expect(horizontalScrollerRule).toMatch(/overflow-y:\s*hidden;/);
    expect(horizontalScrollerRule).toMatch(/overscroll-behavior-x:\s*contain;/);
    expect(horizontalScrollerRule).toMatch(/overscroll-behavior-y:\s*auto;/);
    expect(styles).toMatch(/\.type-chart-scroll\s*\{[^}]*justify-content:\s*start;/s);
    expect(styles).toMatch(/\.type-chart-grid\s*\{[^}]*margin-inline:\s*auto;/s);
    expect(styles).not.toMatch(
      /\.royal-candy-(?:workflow|target)-table\s*\{[^}]*overflow-x:\s*hidden;/s
    );
  });

  it('adapts dense editors to workspace width without hiding table fields', () => {
    const workspaceRule = styles.match(/\.workspace\s*\{([^}]*)\}/s)?.[1];

    expect(workspaceRule).toBeDefined();
    expect(workspaceRule).toMatch(/container-name:\s*workspace;/);
    expect(workspaceRule).toMatch(/container-type:\s*inline-size;/);
    expect(styles).toContain('@container workspace (max-width: 900px)');
    expect(styles).toContain('.za-items-section .items-layout {');

    for (const rowFamily of ['items', 'moves', 'trainers', 'shops', 'flagwork', 'exefs', 'text']) {
      expect(styles).not.toContain(`.${rowFamily}-row span:nth-child`);
    }
  });
});
