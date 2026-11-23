// @vitest-environment node -- a filesystem test, not a DOM one: under jsdom `import.meta.url`
// is an http: URL and resolves to nothing on disk.

import { readFileSync, readdirSync } from 'node:fs';
import { join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

/**
 * Tokens before components, for the same reason the thresholds went into `AlignmentOptions`
 * (AD-10): a colour named in three components is three decisions that can diverge. `theme.css`
 * is the only file allowed to spell one out, and this test is what keeps that true as Stories
 * 2.5 to 2.8 and Epic 3 add the map, the chart and the zone bar.
 */

const srcRoot = fileURLToPath(new URL('.', import.meta.url));
const themeFile = join(srcRoot, 'theme.css');

function sourceFiles(directory: string): string[] {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) return sourceFiles(path);
    return /\.(ts|tsx|css)$/.test(entry.name) ? [path] : [];
  });
}

const theme = readFileSync(themeFile, 'utf8');

describe('the design tokens', () => {
  it('carries every colour, radius and spacing value of DESIGN.md', () => {
    const expected = [
      // The thirteen colours.
      '--color-bg: #0b0d10',
      '--color-surface: #14171c',
      '--color-surface-raised: #1b1f26',
      '--color-border: #262b33',
      '--color-text: #f2f4f7',
      '--color-text-muted: #8a93a3',
      '--color-accent: #c8f135',
      '--color-accent-ink: #0b0d10',
      '--color-original: #7c8594',
      '--color-estimated: #f5b83d',
      '--color-heart: #ff5c7a',
      '--color-danger: #ff5c5c',
      '--color-info: #5cc8ff',
      // The four radii.
      '--radius-sm: 8px',
      '--radius-md: 14px',
      '--radius-lg: 22px',
      '--radius-pill: 999px',
      // The spacing unit and the two named steps.
      '--spacing: 4px',
      '--spacing-gutter: 24px',
      '--spacing-section: 48px',
      // The two faces, and the one breakpoint DESIGN.md names.
      "--font-display: 'Unbounded'",
      "--font-body: 'Manrope'",
      '--breakpoint-md: 768px',
    ];
    for (const token of expected) {
      expect(theme, `theme.css should define ${token}`).toContain(token);
    }
  });

  it('defines the three type roles, with tabular figures on the metric one', () => {
    expect(theme).toContain('@utility type-display');
    expect(theme).toContain('@utility type-body');
    expect(theme).toContain('@utility type-metric');

    const metric = /@utility type-metric \{(.*?)\}/s.exec(theme);
    expect(metric).not.toBeNull();
    expect(metric![1]).toContain("font-feature-settings: 'tnum'");
  });

  it('switches animation off under prefers-reduced-motion', () => {
    // UX-DR: it lives here rather than in each component, so a component that animates
    // without asking is still covered.
    expect(theme).toContain('@media (prefers-reduced-motion: reduce)');
    const block = /@media \(prefers-reduced-motion: reduce\) \{(.*)$/s.exec(theme);
    expect(block![1]).toContain('animation-duration: 0.01ms !important');
    expect(block![1]).toContain('transition-duration: 0.01ms !important');
  });
});

describe('no component names a raw colour', () => {
  // The component sources: not `theme.css`, which is where the values belong, and not the
  // tests, which quote them in order to check that they are there.
  const others = sourceFiles(srcRoot).filter(
    (path) => path !== themeFile && !/\.test\.tsx?$/.test(path),
  );

  it('finds the sources to check', () => {
    // Otherwise a broken walk would report agreement over an empty list.
    expect(others.length).toBeGreaterThan(4);
  });

  it.each(others.map((path) => relative(srcRoot, path)))('%s holds no hex or rgb() colour', (name) => {
    const source = readFileSync(join(srcRoot, name), 'utf8');
    const literals = [
      ...source.matchAll(/#[0-9a-fA-F]{3,8}\b/g),
      ...source.matchAll(/\b(?:rgba?|hsla?|oklch)\(/g),
    ].map((match) => match[0]);
    expect(literals, `${name} should use a token from theme.css instead`).toEqual([]);
  });
});
