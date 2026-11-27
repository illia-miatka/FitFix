// @vitest-environment node -- a filesystem test, not a DOM one: under jsdom `import.meta.url`
// is an http: URL and resolves to nothing on disk.

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

/**
 * The request half of the contract, held against the server's own source — the arrangement
 * `i18n/uk.test.ts` already has for the code registry (AD-18), for the same reason.
 *
 * The response half is pinned on the server by `ContractShapeTests`, which asserts the JSON names
 * it writes. The request half had nobody: every .NET test reaches the field through
 * `RepairEndpoint.TrackCoverageField`, and every web test stubs `fetch` and reads back the
 * `FormData` it built itself, so the two ends verify themselves and never each other. Rename the
 * field on either side and the whole suite stays green while the one gate Story 5.2 exists to
 * build silently stops working: the server reads an absent field, falls back to `detect`, and
 * answers every confirmation with the same blocking warning the person just answered.
 */

const endpointPath = fileURLToPath(
  new URL('../../../src/FitFix.Api/RepairEndpoint.cs', import.meta.url),
);
const contractsPath = fileURLToPath(
  new URL('../../../src/FitFix.Api/Contracts/RepairResponse.cs', import.meta.url),
);
const registryPath = fileURLToPath(
  new URL('../../../src/FitFix.Core/Diagnostics/DiagnosticCodes.cs', import.meta.url),
);

/** `public const string Fragment = "fragment";` → `Fragment` ↦ `fragment`. */
function constants(path: string): Map<string, string> {
  const source = readFileSync(path, 'utf8');
  const found = new Map<string, string>();
  for (const match of source.matchAll(/public\s+const\s+string\s+(\w+)\s*=\s*"([^"]+)"\s*;/g)) {
    const [, name, value] = match;
    if (name !== undefined && value !== undefined) found.set(name, value);
  }

  // A parse that found nothing would agree with anything.
  expect(found.size).toBeGreaterThan(0);
  return found;
}

describe('the repair request', () => {
  it('names the coverage field exactly as the endpoint reads it', () => {
    expect(constants(endpointPath).get('TrackCoverageField')).toBe('trackCoverage');
  });

  it('spells the three coverage modes exactly as the endpoint matches them', () => {
    const modes = constants(contractsPath);

    expect(modes.get('Detect')).toBe('detect');
    expect(modes.get('Fragment')).toBe('fragment');
    expect(modes.get('Whole')).toBe('whole');
  });

  it('names the track-length modes the same way', () => {
    // The older of the two modes, unpinned for the same reason until now (Story 1.8, 2.6).
    const modes = constants(contractsPath);

    expect(constants(endpointPath).get('TrackLengthModeField')).toBe('trackLengthMode');
    expect(modes.get('Plausible')).toBe('plausible');
    expect(modes.get('Full')).toBe('full');
  });
});

describe('the gated code', () => {
  it('is one the core registry declares', () => {
    // `Result.tsx` withholds the download by matching this string. A core rename would otherwise
    // read as a translation problem in `uk.test.ts`, be fixed there, and leave the gate
    // permanently open — the download offered beside an unanswered blocking warning.
    const registry = readFileSync(registryPath, 'utf8');

    expect(registry).toContain('"TRACK_IS_A_FRAGMENT"');
    expect(registry).toContain('"TRACK_COVERAGE_MODE_INVALID"');
  });
});
