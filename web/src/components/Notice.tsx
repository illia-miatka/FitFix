/**
 * One failure, said in one shape: a sentence, and the attempt when the failure is worth another.
 *
 * Both failures of this page read alike because they are the same component — a zone whose
 * inspection never arrived and a repair whose request never arrived are the same event to the
 * person, and two components would let the two drift into two voices (the same arrangement the
 * dictionary and the design tokens are under).
 *
 * Where the retry goes, EXPERIENCE.md asks for a toast. It is not one: a toast takes the
 * explanation away on a timer and the attempt with it, and the control that failed is where the
 * person is looking. The copy EXPERIENCE.md specifies is kept exactly.
 *
 * A 400 gets no retry, which is why `retry` is optional rather than always offered: the service
 * refused those bytes for a reason in the registry, and pressing retry would send the same bytes
 * to the same code. The way out of that one is another file, which the zone offers. A failure
 * that left as 500 is the opposite case — our invariant broke on a file the service accepted —
 * and `api/failure.ts` is where the two are told apart.
 */
/**
 * The attempt: its words and what it does, as one value. The two are a pair rather than two
 * optional props, because a call site that passed the handler and forgot the label would compile
 * and then silently show no button at all — the one thing this component exists to show.
 */
export interface NoticeRetry {
  /** `uk.errors.retry`. */
  readonly label: string;
  /** What one press does: ask the same question again. */
  readonly onPress: () => void;
}

export interface NoticeProps {
  /** The sentence, from `codeText` or from the dictionary's `unreachable`. */
  readonly text: string;
  /**
   * The code behind the sentence when the sentence is the generic one (`CodeSentence.code`). It
   * reaches the DOM as `data-code` and never the rendered text: support can read it there,
   * and a person cannot read it at all.
   */
  readonly code?: string | null;
  /** Present only when repeating the request could succeed — never on a 400. */
  readonly retry?: NoticeRetry;
}

export function Notice({ text, code, retry }: NoticeProps) {
  return (
    <div className="rounded-md border border-border bg-surface p-gutter">
      {/* The code rides on the element carrying the sentence, so whoever reads a screenshot's
          DOM finds the two together. */}
      <p data-code={code ?? undefined} className="text-sm text-danger">
        {text}
      </p>
      {retry !== undefined && (
        <button
          type="button"
          onClick={retry.onPress}
          className="mt-3 min-h-11 rounded-pill border border-border bg-surface-raised px-4 py-2 text-sm text-text-muted transition-colors hover:text-text"
        >
          {retry.label}
        </button>
      )}
    </div>
  );
}
