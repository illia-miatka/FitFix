import { useCallback, useEffect, useId, useRef, useState } from 'react';
import type { DragEvent } from 'react';

import type { InspectOutcome, OriginalInspection, TrackInspection } from '../api/inspect';
import { inspectOriginal, inspectTrack } from '../api/inspect';
// The one place a code becomes a sentence, and the one shape a failure is said in (Story 2.7).
import { codeText } from '../errors';
import { Notice } from './Notice';
// The one place a metre or a second becomes a string (AD-14): a zone and the result screen must
// not be able to write the same distance two ways.
import { dateTime, duration, kilometres } from '../format';

/** The strings of one zone, taken from `uk.dropzones.original` or `uk.dropzones.track`. */
export interface DropzoneTexts {
  readonly title: string;
  readonly hint: string;
  readonly role: string;
  readonly prompt: string;
  readonly pick: string;
}

/** `uk.inspection` — the labels of the service's answer, shared by both zones. */
export interface InspectionTexts {
  readonly reading: string;
  readonly another: string;
  readonly unreachable: string;
  readonly startedAt: string;
  readonly duration: string;
  readonly recordedDistance: string;
  readonly noDistance: string;
  readonly length: string;
  readonly points: string;
  readonly format: string;
  readonly kilometres: string;
  readonly decimalSeparator: string;
}

export interface DropzoneProps {
  /** Which answer this zone asks for and shows. A FIT reads as both, so it is never guessed (AD-15). */
  readonly role: 'original' | 'track';
  readonly texts: DropzoneTexts;
  readonly inspection: InspectionTexts;
  /** `uk.dropzones.clear` — the label of the × that empties the zone. */
  readonly clearLabel: string;
  /**
   * `uk.errors.retry` — the attempt offered when the inspection never arrived. It comes from the
   * shared `errors` rather than from `uk.inspection`, because the repair offers the same words.
   */
  readonly retryLabel: string;
  /** `uk.codes` — a refusal's sentence, keyed by the code the service returned (AD-18). */
  readonly codes: Readonly<Record<string, string>>;
  readonly file: File | null;
  readonly onFileChange: (file: File | null) => void;
  /**
   * Whether this zone now holds a file the service described. A refused file is not a file, so
   * the repair button upstream is gated on this and not on `file !== null`.
   */
  readonly onAcceptedChange: (accepted: boolean) => void;
}

/** Empty, then the three states of an ask. `InspectOutcome` is the ask's own three. */
type ZoneState = { readonly status: 'empty' } | { readonly status: 'reading' } | InspectOutcome;

/**
 * One zone: it names what it wants, asks the service what the file it was given actually is,
 * and shows that rather than a file name. A name is not confirmation — yesterday's run and
 * today's are both `activity.fit`.
 *
 * A real `button`, so Tab, Enter and Space reach it and drag-and-drop is never the only way in
 * (Accessibility Floor; WCAG 2.1.1). It is also the way out of a refusal: the whole card stays
 * clickable, which is why the choosing control may not be nested inside it and the answer is
 * rendered below it rather than within.
 *
 * No `accept` filter: AD-13 keeps format detection in the core, which reads the bytes. A
 * picker that hid a valid FIT because of its extension would refuse a file the service accepts.
 */
export function Dropzone({
  role,
  texts,
  inspection,
  clearLabel,
  retryLabel,
  codes,
  file,
  onFileChange,
  onAcceptedChange,
}: DropzoneProps) {
  const inputId = useId();
  const promptId = useId();
  const answerId = useId();
  const input = useRef<HTMLInputElement>(null);
  const [isDraggedOver, setIsDraggedOver] = useState(false);
  const [state, setState] = useState<ZoneState>({ status: 'empty' });

  // Which attempt at this file the zone is on. A retry asks about the file already in the zone,
  // and that object does not change, so the ask is keyed on this counter as well: without it the
  // effect below has no reason to run a second time. It is never reset — a new file arrives with
  // its own identity, and the number only has to differ from the last one.
  const [attempt, setAttempt] = useState(0);
  const retry = useCallback(() => setAttempt((previous) => previous + 1), []);

  useEffect(() => {
    if (file === null) {
      setState({ status: 'empty' });
      onAcceptedChange(false);
      return;
    }

    // The tag of this file's ask (Design Notes). Somebody drops the wrong file, sees the
    // skeleton and drops the right one at once; the first answer then arrives last, and
    // without this it would overwrite the second. Ignoring the stale answer is cheaper than
    // cancelling the request, and the request is one a proxy may already have cached.
    let holds = true;

    setState({ status: 'reading' });
    onAcceptedChange(false);

    const asked = role === 'original' ? inspectOriginal(file) : inspectTrack(file);
    void asked.then((outcome) => {
      if (!holds) return;
      setState(outcome);
      onAcceptedChange(outcome.status === 'described');
    });

    return () => {
      holds = false;
    };
  }, [file, role, attempt, onAcceptedChange]);

  function take(files: FileList | null) {
    const first = files?.[0];
    if (first !== undefined) onFileChange(first);
  }

  function onDrop(event: DragEvent<HTMLDivElement>) {
    event.preventDefault();
    setIsDraggedOver(false);
    take(event.dataTransfer.files);
  }

  return (
    <div
      className="relative"
      onDragOver={(event) => {
        event.preventDefault();
        setIsDraggedOver(true);
      }}
      onDragLeave={() => setIsDraggedOver(false)}
      onDrop={onDrop}
    >
      <button
        type="button"
        aria-label={texts.pick}
        // The answer is part of the zone's description, so somebody who tabs to a described
        // zone hears what is in it instead of only what it wants.
        aria-describedby={`${promptId} ${answerId}`}
        onClick={() => input.current?.click()}
        className={`flex w-full flex-col items-start gap-2 rounded-lg border-[1.5px] border-dashed bg-surface p-gutter text-left transition-colors ${
          isDraggedOver ? 'border-accent bg-surface-raised' : 'border-border hover:border-text-muted'
        }`}
      >
        <span className="type-display text-base text-text">{texts.title}</span>
        <span className="text-sm text-text-muted">{texts.role}</span>
        <span id={promptId} className="mt-4 text-sm text-text-muted">
          {file === null ? `${texts.prompt} · ${texts.hint}` : inspection.another}
        </span>
      </button>

      {/* A live region: an answer that arrives seconds after the drop is announced when it
          arrives, not only to whoever happens to be looking at the zone. */}
      <div id={answerId} role="status" className="mt-2 empty:mt-0">
        {state.status === 'reading' && <Reading label={inspection.reading} />}
        {state.status === 'described' && (
          <Described
            name={file?.name ?? ''}
            inspection={state.inspection}
            texts={inspection}
            codes={codes}
          />
        )}
        {/* A 400 is about these bytes, so it is stated and not offered again: the same file would
            meet the same code, and the way out is the «обрати інший» the card already is. A
            failure of ours (500, and still carrying its code) is worth one more press. */}
        {state.status === 'refused' && (
          <Notice
            {...codeText(codes, state.code)}
            retry={state.worthRepeating ? { label: retryLabel, onPress: retry } : undefined}
          />
        )}
        {/* A request that never arrived says nothing about the file, so this one is worth
            repeating — and repeating it is one press rather than another drop. */}
        {state.status === 'unreachable' && (
          <Notice
            text={inspection.unreachable}
            retry={{ label: retryLabel, onPress: retry }}
          />
        )}
      </div>

      {/* Outside the button rather than inside it: one control may not contain another. */}
      {file !== null && (
        <button
          type="button"
          aria-label={clearLabel}
          onClick={() => onFileChange(null)}
          className="absolute top-3 right-3 flex size-8 items-center justify-center rounded-pill border border-border bg-surface-raised text-text-muted transition-colors hover:text-text"
        >
          <span aria-hidden="true">×</span>
        </button>
      )}

      <input
        ref={input}
        id={inputId}
        type="file"
        className="hidden"
        onChange={(event) => take(event.target.files)}
      />
    </div>
  );
}

/** The ask in flight: the sentence for a screen reader, the bars for everyone else. */
function Reading({ label }: { readonly label: string }) {
  return (
    <div className="rounded-md border border-border bg-surface p-gutter">
      <p className="text-sm text-text-muted">{label}</p>
      {/* Decorative: the line above already says what is happening. `animate-pulse` is switched
          off under prefers-reduced-motion by theme.css, for every component at once. */}
      <div aria-hidden="true" className="mt-3 flex flex-col gap-2">
        <span className="h-3 w-2/3 animate-pulse rounded-sm bg-surface-raised" />
        <span className="h-3 w-1/2 animate-pulse rounded-sm bg-surface-raised" />
      </div>
    </div>
  );
}

/**
 * What the service understood, and the file's own name beside it. Which quantities those are
 * follows from the answer's `role`, which is the member that discriminates the two shapes.
 */
function Described({
  name,
  inspection,
  texts,
  codes,
}: {
  readonly name: string;
  readonly inspection: OriginalInspection | TrackInspection;
  readonly texts: InspectionTexts;
  readonly codes: Readonly<Record<string, string>>;
}) {
  return (
    <div className="rounded-md border border-border bg-surface p-gutter">
      <p className="truncate text-sm text-text-muted">{name}</p>
      <dl className="mt-3 flex flex-col gap-2">
        {inspection.role === 'original'
          ? originalFacts(inspection, texts)
          : trackFacts(inspection, texts)}
      </dl>
      {/* What the repair would say about this file, from the same dictionary the result screen
          reads (AD-18) — not prose this component invents from the numbers above. */}
      {inspection.role === 'original' &&
        inspection.codes.map((code) => {
          // Not a failure and not a `Notice`: the file was accepted, and this is what the repair
          // would remark about it. One code the dictionary has no sentence for still reads as a
          // sentence, with the code itself only in `data-code` (Story 2.7).
          const said = codeText(codes, code);
          return (
            <p key={code} data-code={said.code ?? undefined} className="mt-3 text-sm text-estimated">
              {said.text}
            </p>
          );
        })}
    </div>
  );
}

function originalFacts(inspection: OriginalInspection, texts: InspectionTexts) {
  return (
    <>
      <Fact label={texts.startedAt} value={dateTime(inspection.startedAt)} />
      <Fact label={texts.duration} value={duration(inspection.elapsedSeconds)} />
      <Fact
        label={texts.recordedDistance}
        value={
          inspection.recordedDistanceMeters === null
            ? texts.noDistance
            : kilometres(inspection.recordedDistanceMeters, texts)
        }
      />
    </>
  );
}

function trackFacts(inspection: TrackInspection, texts: InspectionTexts) {
  return (
    <>
      <Fact label={texts.length} value={kilometres(inspection.lengthMeters, texts)} />
      <Fact label={texts.points} value={String(inspection.pointCount)} />
      <Fact label={texts.format} value={inspection.format.toUpperCase()} />
    </>
  );
}

function Fact({ label, value }: { readonly label: string; readonly value: string }) {
  return (
    <div className="flex items-baseline justify-between gap-4">
      <dt className="text-sm text-text-muted">{label}</dt>
      <dd className="type-metric text-sm text-text">{value}</dd>
    </div>
  );
}

