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
});
