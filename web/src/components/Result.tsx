import { Info, TriangleAlert } from 'lucide-react';
import { useState } from 'react';

import type {
  RedrawnSpan,
  RepairReport,
  RepairWarning,
  TrackLengthMode,
  TrackLengths,
} from '../api/repair';
import type { CoverageAnswer } from '../App';
// The one place a code becomes a sentence (Story 2.7).
import { codeText } from '../errors';
import { measureFor } from '../chart';
import type { Point } from '../drawing';
import { averageSpeed, duration, heartRate, kilometres, pace } from '../format';
import { RouteMap, type MapTexts } from './RouteMap';
import { TrustChart, type ChartTexts } from './TrustChart';

/** `uk.result` — every string of this screen. No component here holds a literal (AD-18). */
export interface ResultTexts {
  readonly before: string;
  readonly after: string;
  readonly distance: string;
  readonly time: string;
  readonly pace: string;
  readonly speed: string;
  readonly heartRate: string;
  readonly noDistance: string;
  readonly noPace: string;
  readonly warnings: string;
  readonly blocking: string;
  readonly info: string;
  readonly redrawn: string;
  readonly keptOwn: string;
  readonly withheld: string;
  readonly confirmFragment: string;
  readonly rejectFragment: string;
  readonly confirming: string;
  readonly confirmFailed: string;
  readonly download: string;
  readonly startAgain: string;
  readonly strava: string;
  readonly longTrack: LongTrackTexts;
  readonly farTrack: FarTrackTexts;
  readonly map: MapTexts;
  readonly chart: ChartTexts;
}

/** `uk.result.longTrack` — the decision about a track longer than the activity could cover. */
export interface LongTrackTexts {
  readonly trackLength: string;
  readonly usedLength: string;
  readonly hint: string;
  readonly withheld: string;
  readonly keepTrimmed: string;
  readonly trustTrack: string;
  readonly choosing: string;
  readonly failed: string;
}

/** `uk.result.farTrack` — the confirmation a track far from the activity's start asks for. */
export interface FarTrackTexts {
  readonly distance: string;
  readonly unknownDistance: string;
  readonly hint: string;
  readonly withheld: string;
  readonly confirm: string;
  readonly confirmed: string;
}

/** `uk.units` — what a distance and a pace are written in. */
export interface UnitTexts {
  readonly kilometres: string;
  readonly perKilometre: string;
  readonly kilometresPerHour: string;
  readonly decimalSeparator: string;
}

export interface ResultProps {
  readonly report: RepairReport;
  /**
   * The Original's own file name, which the saved file is named after: somebody who repaired
   * three runs this evening has to be able to tell the three downloads apart.
   */
  readonly originalName: string;
  readonly texts: ResultTexts;
  readonly units: UnitTexts;
  /** `uk.codes` — a warning's sentence, keyed by the code the service returned (AD-18). */
  readonly codes: Readonly<Record<string, string>>;
  readonly onStartAgain: () => void;
  /**
   * Repeats the repair with the fragment confirmed, and answers whether the repeat arrived
   * (Story 5.2). The page owns the two files and the result, so the new answer replaces this one
   * there; what this component does with a `false` is say so and leave the result standing.
   */
  readonly onConfirmFragment: () => Promise<CoverageAnswer>;
  /** The same question answered the other way: the track is the whole route (Story 5.2). */
  readonly onUseWholeRoute: () => Promise<CoverageAnswer>;
  /**
   * Repeats the repair with the drawn line as the track (Story 5.4). Absent, the map offers no
   * drawing: a screen that cannot act on a drawing has no business inviting one.
   */
  readonly onDrawStretch?: (line: Point[]) => Promise<CoverageAnswer>;
  /** Which length the athlete chose for an over-long track, or null while nothing is chosen. */
  readonly lengthDecision: TrackLengthMode | null;
  /** Answers the over-long track warning (Story 3.5); the page repeats the repair if it must. */
  readonly onChooseLength: (mode: TrackLengthMode) => Promise<CoverageAnswer>;
}

/** What the saved file is called, as KF-1 names it: `Run_4k_Chabany_fixed.fit`. */
const fixedSuffix = '_fixed.fit';

/**
 * `DiagnosticCodes.TrackIsAFragment` — the first blocking code this screen withholds the file for
 * (AD-22, AD-27). A code and not a sentence, so it is the component's business rather than the
 * dictionary's (AD-18); each blocking code has a gate of its own, because each asks a different
 * question and a code with no question to ask must not hide the file behind a button nobody has.
 */
const fragmentCode = 'TRACK_IS_A_FRAGMENT';

/** `DiagnosticCodes.TrackLongerThanActivity` — the second code with a decision of its own (3.5). */
const longerCode = 'TRACK_LONGER_THAN_ACTIVITY';

/** `DiagnosticCodes.TrackFarFromOriginal` — the third code with a question of its own. */
const farCode = 'TRACK_FAR_FROM_ORIGINAL';

/**
 * The result state of EXPERIENCE.md: the warnings as a callout above the numbers, the three
 * metrics as before → after, the file, and the way back to two empty zones. A repair the service
 * made from a fragment of the route holds the file back until the stretch is confirmed, which is
 * the one question this screen asks (AD-22, AD-27).
 *
 * Every number here is divided or converted in `format.ts` and nowhere else. The contract carries
 * metres and seconds and refuses to carry a pace (AD-14), so the screen computes one; if it did so
 * inline, the pace under the hero card and the pace on Epic 3's chart would be two computations
 * that can disagree about the same run.
 */
export function Result({
  report,
  originalName,
  texts,
  units,
  codes,
  onStartAgain,
  onConfirmFragment,
  onUseWholeRoute,
  onDrawStretch,
  lengthDecision,
  onChooseLength,
}: ResultProps) {
  const { before, after } = report;
  // The record the chart is on (Story 3.4): the one parent of both views holds it, so the map's
  // marker and the chart's cursor can never name different records.
  const [activeIndex, setActiveIndex] = useState<number | null>(null);

  // The file waits while the fragment warning stands (AD-22): somebody who grabbed the wrong track
  // would otherwise walk away with 320 s of their run replaced by a drawn line and no way to tell.
  // The warning is gone from the confirmed answer, so the gate opens without a flag of its own.
  const blockedBy = (code: string) =>
    report.warnings.some((warning) => warning.code === code && warning.severity === 'blocking');
  const fragmentGated = blockedBy(fragmentCode);
  // An over-long track keeps its warning after the athlete has chosen — trusting the track does not
  // make it plausible (AD-17) — so this gate reads the recorded answer, not the warning's absence.
  const longTrack = blockedBy(longerCode);
  // The far track's confirmation changes nothing about the file — the very same bytes either way —
  // so it asks the service nothing and lives here, forgotten with the screen when the next repair
  // replaces it (AD-3, AD-22).
  const [farConfirmed, setFarConfirmed] = useState(false);
  const farTrack = blockedBy(farCode);
  const gated =
    fragmentGated || (longTrack && lengthDecision === null) || (farTrack && !farConfirmed);

  // The repair keeps the Original's clock, so these seconds are the "before" ones too — which is
  // why they are also the denominator of both paces, and why the time row states the same value
  // on both sides. `movingSeconds` rather than the elapsed span: it is the `total_timer_time` the
  // written file states, so distance ÷ time on this screen really is the pace beside it.
  const seconds = after.movingSeconds;
  const cycling = measureFor(before.sport) === 'speed';
  const recorded = before.recordedDistanceMeters;

  return (
    <div>
      {report.warnings.length > 0 && (
        // Above the numbers, not beside them (EXPERIENCE.md): the one about an over-long track is
        // the difference between a true pace and a flattering one, so it is read first. Of the
        // blocking ones, every one withholds the file until its own question is answered (AD-22).
        <ul aria-label={texts.warnings} className="flex flex-col gap-2">
          {report.warnings.map((warning) => (
            <Callout key={warning.code} warning={warning} texts={texts} codes={codes} />
          ))}
        </ul>
      )}

      {/* The question itself, under the warning that raised it: which seconds and which metres
          were redrawn, and the one answer only the athlete has. */}
      {fragmentGated && (
        <FragmentGate
          // `?? null` and not a cast: a service a version ahead of this build may send a warning
          // whose span this contract does not know, and the question still has to be asked.
          span={report.redrawn ?? null}
          texts={texts}
          units={units}
          onConfirm={onConfirmFragment}
          onUseWholeRoute={onUseWholeRoute}
          codes={codes}
        />
      )}

      {/* The same stretch once the question is answered (Story 5.3): the confirmed answer still
          carries `redrawn`, and the screen that offers the file is the one that has to say which
          seconds of the run are a drawn line. `!= null` and not `!== null`: a service a version
          behind sends no field at all, which means the same as null. */}
      {!fragmentGated && report.redrawn != null && (
        <div className="mt-gutter rounded-md border border-border bg-surface p-gutter">
          <RedrawnStretch span={report.redrawn} texts={texts} units={units} />
          <p className="mt-gutter text-sm text-text-muted">{texts.keptOwn}</p>
        </div>
      )}

      {farTrack && (
        <FarTrackGate
          meters={report.startToTrackMeters ?? null}
          confirmed={farConfirmed}
          texts={texts}
          units={units}
          onConfirm={() => setFarConfirmed(true)}
        />
      )}

      {longTrack && (
        <LongTrackGate
          track={report.track}
          decision={lengthDecision}
          texts={texts}
          units={units}
          codes={codes}
          onChoose={onChooseLength}
        />
      )}

      {/* The result's grid (Story 3.6): the map 7/12 beside the metrics 5/12 from 768 px, the chart
          under both; on a phone one column with the numbers first, so a 320 px map does not push
          them off the screen. The DOM order is the desktop reading order, and the metrics hold
          nothing focusable, so the phone's `order` moves no focus. */}
      <div className="mt-gutter grid grid-cols-1 gap-gutter md:grid-cols-12">
        {/* The athlete checks that the line is their own route before taking the file (Story 3.2). */}
        <RouteMap
          className="order-2 min-w-0 md:order-1 md:col-span-7"
          series={report.series}
          zones={report.zones}
          texts={texts.map}
          activeIndex={activeIndex}
          onDraw={
            onDrawStretch === undefined
              ? undefined
              : async (line) => {
                  const answered = await onDrawStretch(line);
                  // A refusal in the service's own words, like the fragment question; no code is a
                  // request that never arrived, and the map has a sentence of its own for that.
                  return answered.ok || answered.code === undefined
                    ? { ok: answered.ok }
                    : { ok: false, message: codeText(codes, answered.code).text };
                }
          }
        />

        <dl className="hero-card order-1 flex min-w-0 flex-col content-start gap-4 rounded-lg border border-border bg-surface p-gutter md:order-2 md:col-span-5">
          <Delta
            label={texts.distance}
            texts={texts}
            // A watch that recorded no distance has not recorded zero, and the row says which of
            // the two happened rather than printing a nought the athlete would read as measured.
            before={recorded === null ? texts.noDistance : kilometres(recorded, units)}
            after={kilometres(after.distanceMeters, units)}
            changed={recorded !== null}
          />
          <Delta
            label={texts.time}
            texts={texts}
            before={duration(seconds)}
            after={duration(seconds)}
          />
          {cycling ? (
            <Delta
              label={texts.speed}
              texts={texts}
              before={
                (recorded === null ? null : averageSpeed(recorded, seconds, units)) ?? texts.noPace
              }
              after={averageSpeed(after.distanceMeters, seconds, units) ?? texts.noPace}
              changed={recorded !== null}
            />
          ) : (
            <Delta
              label={texts.pace}
              texts={texts}
              before={(recorded === null ? null : pace(recorded, seconds, units)) ?? texts.noPace}
              after={pace(after.distanceMeters, seconds, units) ?? texts.noPace}
              changed={recorded !== null}
            />
          )}
          {/* No strap, no row: a zero or a dash would read as a measurement. The repair keeps the
              Original's own readings, so the two sides are one number. */}
          {after.averageHeartRate !== null && (
            <Delta
              label={texts.heartRate}
              texts={texts}
              before={heartRate(after.averageHeartRate, texts.chart.heartRateUnit)}
              after={heartRate(after.averageHeartRate, texts.chart.heartRateUnit)}
            />
          )}
        </dl>

        {/* Under the map, over the actions: where the position was measured and where it was
          calculated, against the pace and the heart rate (Story 3.3). */}
        <TrustChart
          className="order-3 min-w-0 md:col-span-12"
          series={report.series}
          zones={report.zones}
          sport={before.sport}
          texts={texts.chart}
          units={units}
          onActiveIndex={setActiveIndex}
        />
      </div>

      <div className="mt-section flex flex-col items-start gap-gutter md:flex-row md:items-center">
        {/* Withheld rather than disabled: a download button that cannot be pressed invites the
            press, and the thing to press is the confirmation above (AD-22). */}
        {!gated && (
          <Download
            fitBase64={report.fitBase64}
            originalName={originalName}
            label={texts.download}
          />
        )}
        <button
          type="button"
          onClick={onStartAgain}
          className="rounded-pill border border-border bg-surface px-6 py-3 text-text-muted transition-colors hover:text-text"
        >
          {texts.startAgain}
        </button>
        {/* EXPERIENCE.md's Result state: the upload is its own epic, and the row says so in its
            own label rather than in a tooltip a touch screen cannot show. Genuinely `disabled`,
            not merely styled so — a control that looks dead and still fires is worse than both. */}
        <button
          type="button"
          disabled
          className="rounded-pill border border-border bg-surface px-6 py-3 text-text-muted disabled:cursor-not-allowed"
        >
          {texts.strava}
        </button>
      </div>
    </div>
  );
}

/**
 * One warning: the dictionary's sentence, and the severity said in words as well as in colour —
 * DESIGN.md forbids a state that differs by hue alone, and "important" is exactly the state a
 * colour-blind reader may not miss.
 */
function Callout({
  warning,
  texts,
  codes,
}: {
  readonly warning: RepairWarning;
  readonly texts: ResultTexts;
  readonly codes: Readonly<Record<string, string>>;
}) {
  const blocking = warning.severity === 'blocking';
  const said = codeText(codes, warning.code);
  return (
    <li
      className={`rounded-md border-l-4 bg-surface p-gutter ${
        blocking ? 'border-l-danger' : 'border-l-estimated'
      }`}
    >
      <p
        className={`type-display flex items-center gap-2 text-sm ${
          blocking ? 'text-danger' : 'text-estimated'
        }`}
      >
        {/* Beside the word, never instead of it: the severity is said in words too. */}
        {blocking ? (
          <TriangleAlert aria-hidden="true" size={16} />
        ) : (
          <Info aria-hidden="true" size={16} />
        )}
        {blocking ? texts.blocking : texts.info}
      </p>
      {/* The sentence for the code. One the dictionary has no sentence for still reads as a
          sentence, and the severity above is unchanged by it: the code travels in `data-code`. */}
      <p data-code={said.code ?? undefined} className="mt-2 text-sm text-text">
        {said.text}
      </p>
    </li>
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
function FragmentGate({
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
  const [pending, setPending] = useState<'fragment' | 'whole' | null>(null);
  const [failure, setFailure] = useState<string | null>(null);

  async function answer(which: 'fragment' | 'whole', with_: () => Promise<CoverageAnswer>) {
    // The same guard the repair button has: two clicks can land inside one render, and the second
    // would repair the same two files again for an identical answer.
    if (pending !== null) return;

    setPending(which);
    setFailure(null);
    const answered = await with_();
    setPending(null);
    // A failed repeat leaves the result where it is — the numbers on screen are still the ones the
    // service answered with — and says what happened. A refusal says it in the service's own
    // words, because "try again" is wrong advice for a file the service will refuse again.
    setFailure(
      answered.ok
        ? null
        : answered.code === undefined
          ? texts.confirmFailed
          : codeText(codes, answered.code).text,
    );
  }

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

      {/* A live region: the sentence arrives seconds after the press, as the repair's own does. */}
      <div role="status" className="empty:hidden">
        {failure !== null && <p className="mt-2 text-sm text-danger">{failure}</p>}
      </div>
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
function LongTrackGate({
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
  const [pending, setPending] = useState<TrackLengthMode | null>(null);
  const [failure, setFailure] = useState<string | null>(null);
  const words = texts.longTrack;

  async function choose(mode: TrackLengthMode) {
    // The same guard as the fragment question: two clicks can land inside one render.
    if (pending !== null) return;

    setPending(mode);
    setFailure(null);
    const answered = await onChoose(mode);
    setPending(null);
    setFailure(
      answered.ok
        ? null
        : answered.code === undefined
          ? words.failed
          : codeText(codes, answered.code).text,
    );
  }

  const option = (mode: TrackLengthMode, label: string) => (
    <button
      type="button"
      aria-pressed={decision === mode}
      aria-busy={pending === mode}
      disabled={pending !== null}
      onClick={() => void choose(mode)}
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

      <div role="status" className="empty:hidden">
        {failure !== null && <p className="mt-2 text-sm text-danger">{failure}</p>}
      </div>
    </div>
  );
}

/**
 * The question a track that attached nowhere asks: how far from the start it passes, and one
 * button that says "this is my track". A pressed state in words, not only in colour; and the
 * explanation of why the file waits stays until it is answered (AD-22).
 */
function FarTrackGate({
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
function RedrawnStretch({
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

/**
 * The metric delta of DESIGN.md: «3,91 км → **6,54 км**», the new value in `accent`. Both sides
 * are labelled for a screen reader, because an arrow is a glyph it may or may not read out — and
 * "was 3.91, is 6.54" is the whole sentence this component exists to say.
 */
function Delta({
  label,
  texts,
  before,
  after,
  changed = false,
}: {
  readonly label: string;
  readonly texts: ResultTexts;
  readonly before: string;
  readonly after: string;
  /**
   * Whether the repair changed this value, so the old one is struck out. An unchanged figure is
   * not: "was 19:59, now 19:59" with the first struck through would say it had been wrong. Also
   * false where the old side is a sentence ("the watch recorded none"), which is not a value.
   */
  readonly changed?: boolean;
}) {
  const struck = changed && before !== after;
  return (
    <div className="border-t border-border pt-4 first:border-t-0 first:pt-0">
      <dt className="text-sm text-text-muted">{label}</dt>
      <dd className="mt-2 flex flex-wrap items-baseline gap-2">
        <span className={`type-metric text-text-muted ${struck ? 'line-through' : ''}`}>
          <span className="sr-only">{`${texts.before}: `}</span>
          {before}
        </span>
        <span aria-hidden="true" className="text-text-muted">
          →
        </span>
        <span className="type-display text-2xl text-accent tabular-nums">
          <span className="sr-only">{`${texts.after}: `}</span>
          {after}
        </span>
      </dd>
    </div>
  );
}

/**
 * The file itself. It is already in memory, base64 beside the report (AD-3), so this decodes it
 * and hands it to the browser — there is nothing server-side to fetch later.
 */
function Download({
  fitBase64,
  originalName,
  label,
}: {
  readonly fitBase64: string;
  readonly originalName: string;
  readonly label: string;
}) {
  function save() {
    const url = URL.createObjectURL(
      new Blob([bytesOf(fitBase64)], { type: 'application/octet-stream' }),
    );
    const link = document.createElement('a');
    link.href = url;
    link.download = fixedNameOf(originalName);
    link.click();

    // Released once the browser has taken the blob, not before: a revoked URL cancels the
    // download in some browsers if it goes while the click is still being handled. Without the
    // revoke the blob would be held until the tab closed.
    setTimeout(() => URL.revokeObjectURL(url), 0);
  }

  return (
    <button
      type="button"
      onClick={save}
      className="type-display rounded-pill bg-accent px-8 py-3 text-accent-ink"
    >
      {label}
    </button>
  );
}

/** `Run_4k_Chabany.fit` → `Run_4k_Chabany_fixed.fit`; a name with no extension keeps all of it. */
export function fixedNameOf(originalName: string): string {
  const stem = originalName.replace(/\.[^.\\/]+$/, '');
  return `${stem === '' ? originalName : stem}${fixedSuffix}`;
}

/** The base64 of the contract as the bytes of the file. */
function bytesOf(base64: string): Uint8Array<ArrayBuffer> {
  const binary = atob(base64);
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index += 1) bytes[index] = binary.charCodeAt(index);
  return bytes;
}
