import { describe, expect, it } from 'vitest';

import { codeText } from './errors';
import { uk } from './i18n/uk';

/**
 * Story 2.7: what a code reads as. The dictionary itself is checked against the core's registry
 * in `i18n/uk.test.ts`; what is checked here is the decision between a sentence and the generic
 * one, which is the decision four components used to take separately.
 */

describe('a code the dictionary knows', () => {
  it('reads as its own sentence, with nothing to carry in the DOM', () => {
    expect(codeText(uk.codes, 'NOT_A_FIT_FILE')).toEqual({
      text: uk.codes.NOT_A_FIT_FILE,
      code: null,
    });
  });

  it('reads as a sentence for every code in the dictionary', () => {
    // The dictionary is held equal to the core's registry, so this is every code the service can
    // hand the interface: none of them may come back generic.
    for (const [code, text] of Object.entries(uk.codes)) {
      expect(codeText(uk.codes, code)).toEqual({ text, code: null });
    }
  });
});

describe('a code the dictionary does not know', () => {
  it('reads as the generic sentence and keeps the code for the DOM', () => {
    expect(codeText(uk.codes, 'WHAT_IS_THIS')).toEqual({
      text: uk.errors.unexpected,
      code: 'WHAT_IS_THIS',
    });
  });

  it('never puts the code in the sentence a person reads', () => {
    // The failure this story exists to remove: `TRACK_LENGTH_MODE_INVALID` on the screen of
    // somebody who wanted to repair a run.
    expect(codeText(uk.codes, 'A_CODE_FROM_THE_FUTURE').text).not.toContain('A_CODE');
    expect(codeText({}, 'TRACK_LENGTH_MODE_INVALID').text).not.toContain('TRACK_LENGTH_MODE');
  });

  it('treats a text that is there but empty as no text at all', () => {
    expect(codeText({ A_CODE: '   ' }, 'A_CODE')).toEqual({
      text: uk.errors.unexpected,
      code: 'A_CODE',
    });
  });

  it('carries nothing when there is no code to carry', () => {
    // A body with `code: ""` would otherwise promise support an attribute to look up.
    expect(codeText(uk.codes, '')).toEqual({ text: uk.errors.unexpected, code: null });
    expect(codeText(uk.codes, '  ')).toEqual({ text: uk.errors.unexpected, code: null });
  });
});

describe('the generic sentence itself', () => {
  it('says what to do, and is not a code', () => {
    expect(uk.errors.unexpected).not.toBe('');
    expect(uk.errors.unexpected).not.toMatch(/^[A-Z0-9_]+$/);
    // A failure says what happened and what to do (EXPERIENCE.md, Voice and Tone).
    expect(uk.errors.unexpected).toMatch(/спробуй/i);
  });
});
