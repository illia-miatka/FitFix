/**
 * A code into the sentence a person reads, in one place.
 *
 * AD-18 gives every failure a code from the core's registry and no Ukrainian text, and
 * `i18n/uk.ts` is the other half of that bargain. This module is the lookup between them, and it
 * exists because the fallback was the interesting half: four components each wrote
 * `codes[code] ?? code`, so the one case the interface handles worst — a code nobody has written a
 * sentence for yet — was the one case it printed `TRACK_LENGTH_MODE_INVALID` on the screen of
 * somebody who wanted to repair a run.
 *
 * An unregistered code is our bug rather than the reader's problem, so the generic sentence is
 * what they get. The code is not lost: a caller puts it in `data-code` on the element carrying
 * that sentence, where whoever receives the report can read it and a person never does.
 *
 * `uk.test.ts` holds the dictionary's keys equal to the registry, so in a shipped build this
 * fallback should be unreachable. It is written as if it were not, because the dictionary is a
 * build older than the service it is talking to as soon as one of the two is deployed alone.
 */

import { uk } from './i18n/uk';

/** A sentence to render, and the code behind it when the sentence is not the code's own. */
export interface CodeSentence {
  /** What the person reads: the dictionary's sentence, or the generic one. */
  readonly text: string;
  /**
   * The code to carry in `data-code`, and null when there is nothing to carry: a known code is
   * already said in words, so repeating it in an attribute would only invite it onto the screen.
   */
  readonly code: string | null;
}

/**
 * The sentence for <paramref name="code" />, from <paramref name="codes" /> when it has one.
 *
 * @param codes `uk.codes` as the service's codes reach it — a `string` the server sent, not one
 *   of the keys the dictionary happens to declare.
 * @param code The code of a refusal, a warning or a problem document.
 */
export function codeText(codes: Readonly<Record<string, string>>, code: string): CodeSentence {
  const known = codes[code];

  // A blank code is no code: a body that carried `code: ""` would otherwise put an empty
  // `data-code` on the page and promise support something to look up.
  if (code.trim() === '') return { text: uk.errors.unexpected, code: null };

  // An empty text counts as no text. `uk.test.ts` forbids one, so this is the same defence as the
  // lookup itself: a dictionary a version behind the service may have anything in it.
  return known !== undefined && known.trim() !== ''
    ? { text: known, code: null }
    : { text: uk.errors.unexpected, code };
}
