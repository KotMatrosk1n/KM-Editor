import { describe, expect, it } from 'vitest';

import styles from './styles.css?inline';

const getRule = (selector: string) => {
  const escapedSelector = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  return styles.match(new RegExp(`${escapedSelector}\\s*\\{([^}]*)\\}`, 's'))?.[1];
};

describe('editor table layout', () => {
  it('keeps sticky heading backgrounds inside genuine row scroll widths', () => {
    const rowFloors = new Map([
      ['.shops-row', 790],
      ['.encounters-row', 394],
      ['.raid-rewards-row', 544],
      ['.flagwork-row', 612],
      ['.exefs-row', 738],
      ['.shop-inventory-editor-row', 640],
      ['.exefs-check-row', 692],
      ['.royal-candy-workflow-row', 658],
      ['.royal-candy-check-row', 764],
      ['.royal-candy-target-row', 764],
      ['.bag-hook-row', 700],
      ['.iv-screen-range-row', 474],
      ['.catch-cap-row', 650],
      ['.hyper-training-source-row', 504],
      ['.starting-items-row', 594],
      ['.spreadsheet-preview-row', 554],
      ['.mod-merger-preview-row', 856],
      ['.sv-encounter-condition-row', 1108]
    ]);

    for (const [selector, floor] of rowFloors) {
      const rule = getRule(selector);
      expect(rule, selector).toBeDefined();
      expect(rule, selector).toMatch(new RegExp(`min-width:\\s*${floor}px;`));
    }

    for (const selector of [
      '.shops-row',
      '.encounters-row',
      '.raid-rewards-row',
      '.flagwork-row',
      '.exefs-row'
    ]) {
      expect(getRule(selector), selector).toMatch(/width:\s*100%;/);
    }

    const placementRule = styles.match(
      /\.placement-object-row,\s*\.raid-rewards-row\.placement-object-row\s*\{([^}]*)\}/s
    )?.[1];
    expect(placementRule).toMatch(/min-width:\s*544px;/);
    expect(placementRule).toContain(
      'grid-template-columns: minmax(140px, 0.9fr) minmax(190px, 1.25fr) minmax(170px, 1fr);'
    );

    for (const headingSelector of [
      '.shops-row-heading',
      '.encounters-row-heading',
      '.raid-rewards-row-heading',
      '.flagwork-row-heading',
      '.exefs-row-heading'
    ]) {
      expect(styles).not.toContain(`${headingSelector}::before`);
    }

    expect(styles).not.toContain('inset-inline: -100vw;');
    expect(styles).toMatch(/::-webkit-scrollbar-corner\s*\{[^}]*background:\s*#0a0f1c;/s);
  });

  it('keeps wide shop inventory controls local until the rows stack', () => {
    const inventoryScrollerRule = getRule('.shop-inventory-editor-grid');

    expect(inventoryScrollerRule).toMatch(/overflow:\s*auto;/);
    expect(inventoryScrollerRule).toMatch(/scrollbar-gutter:\s*stable;/);
    expect(styles).not.toContain('.sv-shops-layout .shop-inventory-editor-grid');
    expect(getRule('.shop-inventory-editor-heading')).toMatch(/position:\s*sticky;/);
    expect(styles).toMatch(
      /@container workspace \(max-width: 760px\)[\s\S]*?\.shop-inventory-editor-grid\s*\{[^}]*max-height:\s*none;[^}]*overflow:\s*visible;/s
    );
    expect(styles).toMatch(
      /@container workspace \(max-width: 760px\)[\s\S]*?\.shop-inventory-editor-row,\s*\.mod-merger-preview-row\s*\{[^}]*min-width:\s*0;/s
    );
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
    expect(getRule('.type-chart-scroll')).toMatch(/display:\s*block;/);
    expect(getRule('.type-chart-scroll')).not.toMatch(/justify-content:/);
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
    expect(styles).toContain('@container workspace (max-width: 1300px)');
    expect(styles).toContain('@container workspace (max-width: 900px)');
    expect(styles).toMatch(
      /@container workspace \(max-width: 1300px\)[\s\S]*?\.items-layout,[\s\S]*?\.placement-layout,[\s\S]*?\.swsh-pokemon-layout,[\s\S]*?\{\s*grid-template-columns:\s*1fr;/
    );
    expect(styles).toMatch(/--editor-list-column:\s*minmax\(420px, 560px\);/);
    expect(styles).toMatch(
      /@media \(min-width: 2300px\)[\s\S]*?--editor-list-column:\s*minmax\(560px, 760px\);/
    );

    for (const rowFamily of ['items', 'moves', 'trainers', 'shops', 'flagwork', 'exefs', 'text']) {
      expect(styles).not.toContain(`.${rowFamily}-row span:nth-child`);
    }
  });
});
