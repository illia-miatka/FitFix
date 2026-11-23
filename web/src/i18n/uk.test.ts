// @vitest-environment node -- a filesystem test, not a DOM one: under jsdom `import.meta.url`
// is an http: URL and resolves to nothing on disk.

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

import { codeTexts } from './uk';

/**
 * The equality AD-18 has been waiting for since Story 1.2: every code the service can hand the
 * interface has a Ukrainian text, and every text belongs to a code.
 *
 * **How the registry is read, and why it cannot drift.** The expectation is parsed out of
 * `DiagnosticCodes.cs` itself — the very file that declares the registry — and not out of a
 * list copied into this repository's frontend. `DiagnosticCodes.All` is `Severities.Keys`, so
 * this test reads the `Severities` initialiser and resolves each `[ConstName]` through the
 * `public const string` declarations in the same file. A code added to the core therefore
 * appears here on the next test run with no step in between, which is exactly the failure a
 * transcribed list could not catch: a copy goes stale silently on the first new code, and
 * then Epic 3 ships a warning with no text.
 *
 * The alternative was generating the list from the built assembly at build time. It was not
 * taken because it would make `npm test` depend on a `dotnet build` having happened, which
 * turns a missing SDK into a frontend test failure; parsing the source needs nothing but the
 * file, and the parse is guarded below so a silent zero-match can never pass as agreement.
 */

const registryPath = fileURLToPath(
  new URL('../../../src/FitFix.Core/Diagnostics/DiagnosticCodes.cs', import.meta.url),
);

/** `public const string FewAnchors = "FEW_ANCHORS";` → `FewAnchors` ↦ `FEW_ANCHORS`. */
function readConstants(source: string): Map<string, string> {
  const constants = new Map<string, string>();
  const declaration = /public\s+const\s+string\s+(\w+)\s*=\s*"([A-Z0-9_]+)"\s*;/g;
  for (const match of source.matchAll(declaration)) {
    const [, name, code] = match;
    if (name !== undefined && code !== undefined) constants.set(name, code);
  }
  return constants;
}

/**
 * The codes `DiagnosticCodes.All` reports, in the order the initialiser lists them: the keys of
 * the `Severities` dictionary, resolved through the constants beside them.
 */
function readRegistry(source: string): string[] {
  const initialiser = /Severities\s*=\s*new\(.*?\)\s*\{(.*?)\n\s*\};/s.exec(source);
  if (initialiser === null) {
    throw new Error(
      `Could not find the Severities initialiser in ${registryPath}. ` +
        'DiagnosticCodes.All is built from it, so this test has nothing to compare against ' +
        'until the parse is fixed — it must fail rather than compare an empty set.',
    );
  }

  const constants = readConstants(source);
  const codes: string[] = [];
  for (const match of initialiser[1]!.matchAll(/\[\s*(\w+)\s*\]\s*=/g)) {
    const name = match[1]!;
    const code = constants.get(name);
    if (code === undefined) {
      throw new Error(
        `Severities lists ${name}, but no 'public const string ${name}' declares its code.`,
      );
    }
    codes.push(code);
  }
  return codes;
}

/**
 * The comparison itself, as a function so the two asymmetric failures of this story's matrix
 * can be exercised directly rather than only through the real pair, which is (correctly) equal.
 */
export function compareCodeSets(
  registry: readonly string[],
  dictionaryKeys: readonly string[],
): { withoutText: string[]; withoutCode: string[] } {
  const texts = new Set(dictionaryKeys);
  const codes = new Set(registry);
  return {
    withoutText: registry.filter((code) => !texts.has(code)).sort(),
    withoutCode: dictionaryKeys.filter((key) => !codes.has(key)).sort(),
  };
}

describe('the Ukrainian dictionary against the core registry', () => {
  const source = readFileSync(registryPath, 'utf8');
  const registry = readRegistry(source);
  const dictionaryKeys = Object.keys(codeTexts);

  it('parses a registry that is actually there', () => {
    // A guard, not a count to maintain: the point is that a parse matching nothing, or a
    // handful, can never be mistaken for the two sets agreeing.
    expect(registry.length).toBeGreaterThan(20);
    expect(new Set(registry).size).toBe(registry.length);
  });

  it('gives every code a text, and every text a code', () => {
    const { withoutText, withoutCode } = compareCodeSets(registry, dictionaryKeys);
    expect(withoutText, 'codes declared in the core with no text in uk.ts').toEqual([]);
    expect(withoutCode, 'texts in uk.ts whose key is no longer a code').toEqual([]);
  });

  it('holds the two key sets equal', () => {
    expect([...dictionaryKeys].sort()).toEqual([...registry].sort());
  });

  it('leaves no text empty or untranslated', () => {
    for (const [code, text] of Object.entries(codeTexts)) {
      expect(text.trim(), `text for ${code}`).not.toBe('');
      // A text that merely repeats the code is the "raw technical code" DESIGN.md forbids.
      expect(text).not.toBe(code);
    }
  });
});

describe('the comparison itself', () => {
  it('names a code that has no text', () => {
    const { withoutText, withoutCode } = compareCodeSets(['A_CODE', 'NEW_CODE'], ['A_CODE']);
    expect(withoutText).toEqual(['NEW_CODE']);
    expect(withoutCode).toEqual([]);
  });

  it('names a text that has no code', () => {
    const { withoutText, withoutCode } = compareCodeSets(['A_CODE'], ['A_CODE', 'STALE_KEY']);
    expect(withoutText).toEqual([]);
    expect(withoutCode).toEqual(['STALE_KEY']);
  });
});
