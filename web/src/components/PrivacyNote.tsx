export interface PrivacyNoteTexts {
  /** The one sentence that is always visible: what happens to the two files. */
  readonly summary: string;
  /** What the disclosure's control says when the rest is still folded away. */
  readonly more: string;
  /** The note itself, one paragraph per thing somebody might want to know. */
  readonly points: readonly string[];
  /** Where to ask, and the label of the link that goes there. */
  readonly contact: string;
  readonly contactLabel: string;
  readonly contactHref: string;
  /** The map's own errors belong with OpenStreetMap, not here. */
  readonly mapIssue: string;
  readonly mapIssueLabel: string;
  readonly mapIssueHref: string;
}

/**
 * What the service does with the two files, at the foot of every state of the page.
 *
 * The one sentence stays visible and the rest folds away, because the sentence is the whole
 * answer for almost everybody: nothing is stored. A disclosure rather than a page of its own —
 * there is no route here (the service stores nothing, so there is nothing to navigate back to),
 * and a note somebody has to leave the page to read is a note nobody reads.
 *
 * `details`/`summary` and not a state of ours: it opens without JavaScript, it is in the tab
 * order already, and a screen reader announces whether it is expanded without being told.
 */
export function PrivacyNote({ texts }: { readonly texts: PrivacyNoteTexts }) {
  return (
    <footer className="mt-section border-t border-border pt-gutter text-sm text-text-muted">
      <details className="group">
        <summary className="cursor-pointer list-none">
          <span>{texts.summary}</span>{' '}
          {/* The control's own words, so the sentence beside it stays a sentence and not a link. */}
          <span className="text-accent underline group-open:hidden">{texts.more}</span>
        </summary>

        <ul className="mt-gutter grid max-w-[75ch] gap-2">
          {texts.points.map((point) => (
            <li key={point}>{point}</li>
          ))}
        </ul>

        <p className="mt-gutter max-w-[75ch]">
          {texts.contact}{' '}
          <a
            href={texts.contactHref}
            target="_blank"
            rel="noopener noreferrer"
            className="text-accent underline"
          >
            {texts.contactLabel}
          </a>
        </p>

        <p className="mt-2 max-w-[75ch]">
          {texts.mapIssue}{' '}
          <a
            href={texts.mapIssueHref}
            target="_blank"
            rel="noopener noreferrer"
            className="text-accent underline"
          >
            {texts.mapIssueLabel}
          </a>
        </p>
      </details>
    </footer>
  );
}
