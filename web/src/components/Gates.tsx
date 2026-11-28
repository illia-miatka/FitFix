import { useState } from 'react';

import type { RedrawnSpan, TrackLengthMode, TrackLengths } from '../api/repair';
import type { CoverageAnswer } from '../App';
import { codeText } from '../errors';
import { duration, kilometres } from '../format';
import type { ResultTexts, UnitTexts } from './Result';

/**
 * The questions the result screen asks before it hands the file over (AD-22): the fragment, the
 * over-long track and the track that attached nowhere. Split out of `Result.tsx` by the Epic 3
 * retrospective; the screen decides which are asked, these say how.
 */

/** What a gate that repeats the repair shows while it waits and when the repeat fails. */
function useAnswer<K extends string>(failed: string, codes: Readonly<Record<string, string>>) {
  // Which answer is out, so the label that changes is the button that was pressed.
  const [pending, setPending] = useState<K | null>(null);
  const [failure, setFailure] = useState<string | null>(null);

  async function answer(which: K, ask: () => Promise<CoverageAnswer>) {
    // The same guard the repair button has: two clicks can land inside one render, and the second
    // would repair the same two files again for an identical answer.
    if (pending !== null) return;

    setPending(which);
    setFailure(null);
    const answered = await ask();
    setPending(null);
    // A failed repeat leaves the result where it is — the numbers on screen are still the ones the
    // service answered with — and says what happened. A refusal says it in the service's own
    // words, because "try again" is wrong advice for a file the service will refuse again.
    setFailure(
      answered.ok
        ? null
        : answered.code === undefined
          ? failed
          : codeText(codes, answered.code).text,
    );
  }

  return { pending, failure, answer };
}

/** A live region: the sentence arrives seconds after the press, as the repair's own does. */
function Failure({ message }: { readonly message: string | null }) {
  return (
    <div role="status" className="empty:hidden">
      {message !== null && <p className="mt-2 text-sm text-danger">{message}</p>}
    </div>
  );
}

/**
 * The gate of AD-22 for the one code this story owns: the stretch the service redrew, stated in the
 * seconds and the metres the answer carried, and the confirmation that repeats the repair without
 * the warning. The file is nowhere on the screen until that answer comes back.
 *
 * The span is read off the report and never worked out here from the zones: the CLI prints the same
 * two seconds, and two readings of one repair are two numbers free to disagree (AD-16).
 */
export function FragmentGate({
  span,
  texts,
  units,
  onConfirm,
  onUseWholeRoute,
  codes,
}: {
  readonly span: RedrawnSpan | null;
  readonly texts: ResultTexts;
  readonly units: UnitTexts;
  readonly onConfirm: () => Promise<CoverageAnswer>;
  readonly onUseWholeRoute: () => Promise<CoverageAnswer>;
  readonly codes: Readonly<Record<string, string>>;
}) {
  // Which of the two answers is out, so the label that changes is the button that was pressed:
  // a screen saying "Підтверджую…" while the whole-route answer is in flight states the opposite
  // of what was asked.
  const { pending, failure, answer } = useAnswer<'fragment' | 'whole'>(texts.confirmFailed, codes);

  return (
    <div className="mt-gutter rounded-md border border-border bg-surface p-gutter">
      {/* Null only if an answer carried the warning without the span, which the core does not
          produce: the question is still asked, with the words of the warning above. */}
      {span !== null && <RedrawnStretch span={span} texts={texts} units={units} />}

      <p className="mt-gutter text-sm text-text-muted">{texts.withheld}</p>

      <button
        type="button"
        disabled={pending !== null}
        aria-busy={pending === 'fragment'}
        onClick={() => void answer('fragment', onConfirm)}
        className="type-display mt-2 rounded-pill bg-accent px-8 py-3 text-accent-ink transition-opacity disabled:cursor-not-allowed disabled:bg-surface-raised disabled:text-text-muted"
      >
        {pending === 'fragment' ? texts.confirming : texts.confirmFragment}
      </button>

      {/* The answer the detector can be wrong about: a lap run twice looks like a stretch of
          itself, and the athlete is the one who knows. Secondary, because the common answer is
          the one above, but present — a question with only a yes is not a question. */}
      <button
        type="button"
        disabled={pending !== null}
        aria-busy={pending === 'whole'}
        onClick={() => void answer('whole', onUseWholeRoute)}
        className="mt-2 ml-0 rounded-pill border border-border bg-surface-raised px-6 py-3 text-sm text-text-muted transition-colors hover:text-text disabled:cursor-not-allowed md:ml-2"
      >
        {pending === 'whole' ? texts.confirming : texts.rejectFragment}
      </button>

      <Failure message={failure} />
    </div>
  );
}

/**
 * The decision of Story 3.5: how far the repaired run goes when the track is longer than the
 * activity could have covered. Both lengths come from the answer, and the two answers are two
 * buttons that state which one is chosen. Neither is chosen at first, and the file waits for one
 * (AD-22). Keeping the trimmed length asks the service nothing; trusting the track repeats the
 * repair, which is the page's business.
 */
export function LongTrackGate({
  track,
  decision,
  texts,
  units,
  codes,
  onChoose,
}: {
  readonly track: TrackLengths;
  readonly decision: TrackLengthMode | null;
  readonly texts: ResultTexts;
  readonly units: UnitTexts;
  readonly codes: Readonly<Record<string, string>>;
  readonly onChoose: (mode: TrackLengthMode) => Promise<CoverageAnswer>;
}) {
  const words = texts.longTrack;
  const { pending, failure, answer } = useAnswer<TrackLengthMode>(words.failed, codes);

  const option = (mode: TrackLengthMode, label: string) => (
    <button
      type="button"
      aria-pressed={decision === mode}
      aria-busy={pending === mode}
      disabled={pending !== null}
      onClick={() => void answer(mode, () => onChoose(mode))}
      className={`rounded-pill border px-6 py-3 text-sm transition-colors disabled:cursor-not-allowed ${
        decision === mode
          ? 'border-accent bg-surface-raised text-text'
          : 'border-border bg-surface text-text-muted hover:text-text'
      }`}
    >
      {pending === mode ? words.choosing : label}
    </button>
  );

  return (
    <div className="mt-gutter rounded-md border border-border bg-surface p-gutter">
      <dl className="flex flex-wrap gap-x-gutter gap-y-2">
        <div>
          <dt className="text-sm text-text-muted">{words.trackLength}</dt>
          <dd className="type-metric mt-2 text-text">{kilometres(track.lengthMeters, units)}</dd>
        </div>
        <div>
          <dt className="text-sm text-text-muted">{words.usedLength}</dt>
          <dd className="type-metric mt-2 text-estimated">
            {kilometres(track.usedLengthMeters, units)}
          </dd>
        </div>
      </dl>

      <p className="mt-gutter max-w-[70ch] text-sm text-text-muted">{words.hint}</p>
      {decision === null && <p className="mt-2 text-sm text-text-muted">{words.withheld}</p>}

      <div className="mt-2 flex flex-wrap gap-2">
        {option('plausible', words.keepTrimmed)}
        {option('full', words.trustTrack)}
      </div>

      <Failure message={failure} />
    </div>
  );
}

/**
 * The question a track that attached nowhere asks: how far from the start it passes, and one
 * button that says "this is my track". A pressed state in words, not only in colour; and the
 * explanation of why the file waits stays until it is answered (AD-22).
 */
export function FarTrackGate({
  meters,
  confirmed,
  texts,
  units,
  onConfirm,
}: {
  readonly meters: number | null;
  readonly confirmed: boolean;
  readonly texts: ResultTexts;
  readonly units: UnitTexts;
  readonly onConfirm: () => void;
}) {
  const words = texts.farTrack;

  return (
    <div className="mt-gutter rounded-md border border-border bg-surface p-gutter">
      {meters === null ? (
        <p className="text-sm text-text-muted">{words.unknownDistance}</p>
      ) : (
        <dl>
          <dt className="text-sm text-text-muted">{words.distance}</dt>
          <dd className="type-metric mt-2 text-estimated">{kilometres(meters, units)}</dd>
        </dl>
      )}

      <p className="mt-gutter max-w-[70ch] text-sm text-text-muted">{words.hint}</p>
      {!confirmed && <p className="mt-2 text-sm text-text-muted">{words.withheld}</p>}

      <div className="mt-2">
        <button
          type="button"
          aria-pressed={confirmed}
          disabled={confirmed}
          onClick={onConfirm}
          className={`min-h-11 rounded-pill border px-6 py-3 text-sm transition-colors disabled:cursor-default ${
            confirmed
              ? 'border-accent bg-surface-raised text-text'
              : 'border-border bg-surface text-text-muted hover:text-text'
          }`}
        >
          {confirmed ? words.confirmed : words.confirm}
        </button>
      </div>
    </div>
  );
}

/**
 * The stretch a fragment redrew: its first and last second, and the metres the fragment drew
 * there (AD-27). One place states it, so the question that asks about the stretch and the result
 * that answers for it cannot word the same span two ways. The label is the dictionary's; the
 * numbers come from the answer and are never part of a sentence (AD-14).
 */
export function RedrawnStretch({
  span,
  texts,
  units,
}: {
  readonly span: RedrawnSpan;
  readonly texts: ResultTexts;
  readonly units: UnitTexts;
}) {
  return (
    <dl>
      <dt className="text-sm text-text-muted">{texts.redrawn}</dt>
      <dd className="mt-2 flex flex-wrap items-baseline gap-2">
        <span className="type-metric text-estimated">
          {`${duration(span.fromSeconds)}–${duration(span.toSeconds)}`}
        </span>
        <span className="text-text-muted">{kilometres(span.distanceMeters, units)}</span>
      </dd>
    </dl>
  );
}
