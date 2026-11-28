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

  it('darkens the light OpenStreetMap basemap, since no DOM test can see the pixels', () => {
    // The tile pane is its own layer, so the lines and pins above it keep their token colours.
    // Without the filter the map is a white slab in a dark page.
    expect(theme).toMatch(/\.leaflet-tile-pane\s*\{[^}]*\bfilter:\s*invert\(1\)/);
  });

  it.each(others.map((path) => relative(srcRoot, path)))(
    '%s holds no hex or rgb() colour',
    (name) => {
      const source = readFileSync(join(srcRoot, name), 'utf8');
      const literals = [
        ...source.matchAll(/#[0-9a-fA-F]{3,8}\b/g),
        ...source.matchAll(/\b(?:rgba?|hsla?|oklch)\(/g),
      ].map((match) => match[0]);
      expect(literals, `${name} should use a token from theme.css instead`).toEqual([]);
    },
  );
});

describe('contrast (Story 3.6)', () => {
  /** WCAG 2.x relative luminance of a `#rrggbb` colour. */
  function luminance(hex: string): number {
    const channel = (offset: number) => {
      const value = parseInt(hex.slice(offset, offset + 2), 16) / 255;
      return value <= 0.03928 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4;
    };
    return 0.2126 * channel(1) + 0.7152 * channel(3) + 0.0722 * channel(5);
  }
  const ratio = (foreground: string, background: string) => {
    const [light, dark] = [luminance(foreground), luminance(background)].sort((a, b) => b - a);
    return ((light as number) + 0.05) / ((dark as number) + 0.05);
  };
  const token = (name: string) => {
    const match = new RegExp(`--color-${name}:\\s*(#[0-9a-fA-F]{6})`).exec(theme);
    expect(match, `--color-${name} is defined`).not.toBeNull();
    return (match as RegExpExecArray)[1] as string;
  };

  const surfaces = ['bg', 'surface', 'surface-raised'];
  // Every colour a piece of text is set in on the result screen, each against every surface that
  // text can sit on. `accent-ink` is the one text colour that sits on a colour, not a surface.
  const text = ['text', 'text-muted', 'accent', 'estimated', 'danger', 'info', 'heart'];
  // The original's grey is text in one place only, the end label of its chart line, which sits on
  // the page and never on a raised card (it is 4.44:1 there, a pair the screen does not have).
  const pairs = [
    ...text.flatMap((name) => surfaces.map((surface) => [name, surface] as const)),
    ['original', 'bg'] as const,
    ['original', 'surface'] as const,
  ];

  it.each(pairs)('%s on %s is at least 4.5:1', (name, surface) => {
    expect(ratio(token(name), token(surface))).toBeGreaterThanOrEqual(4.5);
  });

  it('keeps the ink of a filled button readable on the accent', () => {
    expect(ratio(token('accent-ink'), token('accent'))).toBeGreaterThanOrEqual(4.5);
  });

  it('computes the ratio the way WCAG states it', () => {
    expect(ratio('#ffffff', '#000000')).toBeCloseTo(21, 5);
    expect(ratio('#777777', '#ffffff')).toBeCloseTo(4.48, 2);
  });
});

describe('the hero card glow (Story 3.1)', () => {
  it('is a radial gradient of about 6 % accent over transparent, derived from the token', () => {
    const rule = /\.hero-card\s*\{([^}]*)\}/.exec(theme)?.[1] ?? '';

    expect(rule).toContain('radial-gradient');
    expect(rule).toMatch(/color-mix\(in srgb, var\(--color-accent\) 6%, transparent\)/);
  });
});
