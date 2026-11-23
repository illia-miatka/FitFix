import { useEffect, useId, useRef, useState } from 'react';
import type { DragEvent, ReactNode } from 'react';

import type { InspectOutcome, OriginalInspection, TrackInspection } from '../api/inspect';
import { inspectOriginal, inspectTrack } from '../api/inspect';

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
  }, [file, role, onAcceptedChange]);

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
        {/* The dictionary's sentence for the code, and the code itself if the dictionary
            somehow lacks it: a zone that said nothing would be worse than a raw code. */}
        {state.status === 'refused' && <Refusal>{codes[state.code] ?? state.code}</Refusal>}
        {state.status === 'unreachable' && <Refusal>{inspection.unreachable}</Refusal>}
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

function Refusal({ children }: { readonly children: ReactNode }) {
  return (
    <p className="rounded-md border border-border bg-surface p-gutter text-sm text-danger">
      {children}
    </p>
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
        inspection.codes.map((code) => (
          <p key={code} className="mt-3 text-sm text-estimated">
            {codes[code] ?? code}
          </p>
        ))}
    </div>
  );
}

function originalFacts(inspection: OriginalInspection, texts: InspectionTexts) {
  return (
    <>
      <Fact label={texts.startedAt} value={dateTimeOf(inspection.startedAt)} />
      <Fact label={texts.duration} value={durationOf(inspection.elapsedSeconds)} />
      <Fact
        label={texts.recordedDistance}
        value={
          inspection.recordedDistanceMeters === null
            ? texts.noDistance
            : kilometresOf(inspection.recordedDistanceMeters, texts)
        }
      />
    </>
  );
}

function trackFacts(inspection: TrackInspection, texts: InspectionTexts) {
  return (
    <>
      <Fact label={texts.length} value={kilometresOf(inspection.lengthMeters, texts)} />
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

function pad(value: number) {
  return String(value).padStart(2, '0');
}

/**
 * The start as `19.11.2026, 07:12`, in the reader's own time zone: the question this answers is
 * "is this the run I mean", and a Kyiv morning shown in UTC reads as a different hour.
 */
function dateTimeOf(startedAt: string): string {
  // The contract sends UTC with a trailing `Z`. A string without a designator would be read as
  // local time instead, which is a silent shift, so one is ensured rather than assumed.
  const utc = /(?:Z|[+-]\d{2}:?\d{2})$/.test(startedAt) ? startedAt : `${startedAt}Z`;
  const at = new Date(utc);
  return `${pad(at.getDate())}.${pad(at.getMonth() + 1)}.${at.getFullYear()}, ${pad(at.getHours())}:${pad(at.getMinutes())}`;
}

/** `36:02`, and `1:12:40` once there is an hour to show. */
function durationOf(seconds: number): string {
  const whole = Math.round(seconds);
  const hours = Math.floor(whole / 3600);
  const minutes = Math.floor((whole % 3600) / 60);
  const rest = pad(whole % 60);
  return hours > 0 ? `${hours}:${pad(minutes)}:${rest}` : `${minutes}:${rest}`;
}

function kilometresOf(metres: number, texts: InspectionTexts): string {
  const kilometres = (metres / 1000).toFixed(2).replace('.', texts.decimalSeparator);
  return `${kilometres} ${texts.kilometres}`;
}
