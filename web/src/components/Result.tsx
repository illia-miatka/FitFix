import type { RepairReport, RepairWarning } from '../api/repair';
// The one place a code becomes a sentence (Story 2.7).
import { codeText } from '../errors';
import { duration, kilometres, pace } from '../format';

/** `uk.result` — every string of this screen. No component here holds a literal (AD-18). */
export interface ResultTexts {
  readonly before: string;
  readonly after: string;
  readonly distance: string;
  readonly time: string;
  readonly pace: string;
  readonly noDistance: string;
  readonly noPace: string;
  readonly warnings: string;
  readonly blocking: string;
  readonly info: string;
  readonly download: string;
  readonly startAgain: string;
  readonly strava: string;
}

/** `uk.units` — what a distance and a pace are written in. */
export interface UnitTexts {
  readonly kilometres: string;
  readonly perKilometre: string;
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
}

/** What the saved file is called, as KF-1 names it: `Run_4k_Chabany_fixed.fit`. */
const fixedSuffix = '_fixed.fit';

/**
 * The result state of EXPERIENCE.md: the warnings as a callout above the numbers, the three
 * metrics as before → after, the file, and the way back to two empty zones.
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
}: ResultProps) {
  const { before, after } = report;

  // The repair keeps the Original's clock, so these seconds are the "before" ones too — which is
  // why they are also the denominator of both paces, and why the time row states the same value
  // on both sides. `movingSeconds` rather than the elapsed span: it is the `total_timer_time` the
  // written file states, so distance ÷ time on this screen really is the pace beside it.
  const seconds = after.movingSeconds;
  const recorded = before.recordedDistanceMeters;

  return (
    <div>
      {report.warnings.length > 0 && (
        // Above the numbers, not beside them (EXPERIENCE.md): the one about an over-long track is
        // the difference between a true pace and a flattering one, so it is read first. It does
        // not gate the download in this story — the confirmation that matters changes the result
        // rather than opening a gate, and that belongs with the story offering the choice (AD-22).
        <ul aria-label={texts.warnings} className="flex flex-col gap-2">
          {report.warnings.map((warning) => (
            <Callout key={warning.code} warning={warning} texts={texts} codes={codes} />
          ))}
        </ul>
      )}

      <dl className="mt-gutter grid grid-cols-1 gap-gutter md:grid-cols-3">
        <Delta
          label={texts.distance}
          texts={texts}
          // A watch that recorded no distance has not recorded zero, and the column says which
          // of the two happened rather than printing a nought the athlete would read as measured.
          before={recorded === null ? texts.noDistance : kilometres(recorded, units)}
          after={kilometres(after.distanceMeters, units)}
        />
        <Delta
          label={texts.time}
          texts={texts}
          before={duration(seconds)}
          after={duration(seconds)}
        />
        <Delta
          label={texts.pace}
          texts={texts}
          before={
            (recorded === null ? null : pace(recorded, seconds, units)) ?? texts.noPace
          }
          after={pace(after.distanceMeters, seconds, units) ?? texts.noPace}
        />
      </dl>

      <div className="mt-section flex flex-col items-start gap-gutter md:flex-row md:items-center">
        <Download
          fitBase64={report.fitBase64}
          originalName={originalName}
          label={texts.download}
        />
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
      <p className={`type-display text-sm ${blocking ? 'text-danger' : 'text-estimated'}`}>
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
 * The metric delta of DESIGN.md: «3,91 км → **6,54 км**», the new value in `accent`. Both sides
 * are labelled for a screen reader, because an arrow is a glyph it may or may not read out — and
 * "was 3.91, is 6.54" is the whole sentence this component exists to say.
 */
function Delta({
  label,
  texts,
  before,
  after,
}: {
  readonly label: string;
  readonly texts: ResultTexts;
  readonly before: string;
  readonly after: string;
}) {
  return (
    <div className="rounded-lg border border-border bg-surface p-gutter">
      <dt className="text-sm text-text-muted">{label}</dt>
      <dd className="mt-2 flex flex-wrap items-baseline gap-2">
        <span className="type-metric text-text-muted">
          <span className="sr-only">{`${texts.before}: `}</span>
          {before}
        </span>
        <span aria-hidden="true" className="text-text-muted">
          →
        </span>
        <span className="type-display text-2xl text-accent">
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
