import { useCallback, useId, useRef, useState } from 'react';

import { repair as askForRepair } from './api/repair';
import type { RepairReport } from './api/repair';
import { Dropzone } from './components/Dropzone';
import { HowItWorks } from './components/HowItWorks';
import { Result } from './components/Result';
import { uk } from './i18n/uk';

/**
 * The dictionary as the service's codes reach it: a `string` the server sent, not one of the keys
 * the dictionary happens to declare. The two are held equal by `uk.test.ts`, and the lookup below
 * still falls back, because a code with no text must not render an empty page.
 */
const codes: Readonly<Record<string, string>> = uk.codes;

/**
 * The three states of EXPERIENCE.md on one page: the two zones, the button working, and the
 * result. There is no route and no reload between them — "start again" is this component putting
 * itself back to two empty zones, because the service stores nothing and a reload would only
 * throw away the files the person already chose (AD-3).
 *
 * What the button waits for is two *accepted* files, not two chosen ones: a zone whose file the
 * service refused holds nothing the repair could use, so each zone reports acceptance and the
 * files themselves say nothing about the gate.
 *
 * Below 768 px everything is one column (DESIGN.md, Layout & Spacing) — the grids start at
 * `grid-cols-1` and widen at `md:`, so the narrow layout is the one that needs no breakpoint.
 */
export function App() {
  const [original, setOriginal] = useState<File | null>(null);
  const [originalAccepted, setOriginalAccepted] = useState(false);
  const [track, setTrack] = useState<File | null>(null);
  const [trackAccepted, setTrackAccepted] = useState(false);
  const [state, setState] = useState<RepairState>({ status: 'idle' });
  const waitingHintId = useId();

  // The gate against a second request, and not the same thing as the disabled attribute: that one
  // only takes effect on the next render, and two clicks can land inside one. Repairing the same
  // two files twice costs the budget of the whole epic (three seconds a request) and produces a
  // second file identical to the first.
  const running = useRef(false);

  const bothChosen = originalAccepted && trackAccepted;
  const working = state.status === 'working';

  async function onRepair() {
    if (original === null || track === null || running.current) return;

    running.current = true;
    setState({ status: 'working' });

    // No track-length mode: the conservative length is the only one a request that has asked
    // nobody anything may end on (AD-17). The choice is Story 1.8's, and taking it would mean
    // repeating this request with `full`.
    const outcome = await askForRepair(original, track);
    running.current = false;

    setState(
      outcome.status === 'repaired'
        ? { status: 'repaired', report: outcome.report, originalName: original.name }
        : outcome,
    );
  }

  const startAgain = useCallback(() => {
    setOriginal(null);
    setTrack(null);
    setState({ status: 'idle' });
  }, []);

  // A refusal is about the two files that were sent, so it goes the moment one of them changes:
  // left standing, it would read as a verdict on the file just dropped.
  const takeOriginal = useCallback((file: File | null) => {
    setOriginal(file);
    setState(withoutFailure);
  }, []);

  const takeTrack = useCallback((file: File | null) => {
    setTrack(file);
    setState(withoutFailure);
  }, []);

  if (state.status === 'repaired') {
    return (
      <main className="mx-auto w-full max-w-[1200px] px-gutter py-section">
        <h1 className="type-display max-w-[20ch] text-3xl text-text md:text-5xl">
          {uk.result.title}
        </h1>
        <p className="mt-gutter max-w-[60ch] text-text-muted">{uk.result.lede}</p>

        <div className="mt-section">
          <Result
            report={state.report}
            originalName={state.originalName}
            texts={uk.result}
            units={uk.units}
            codes={uk.codes}
            onStartAgain={startAgain}
          />
        </div>
      </main>
    );
  }

  return (
    <main className="mx-auto w-full max-w-[1200px] px-gutter py-section">
      <h1 className="type-display max-w-[20ch] text-3xl text-text md:text-5xl">
        {uk.hero.title}
      </h1>
      <p className="mt-gutter max-w-[60ch] text-text-muted">{uk.hero.lede}</p>

      <div className="mt-section grid grid-cols-1 gap-gutter md:grid-cols-2">
        <Dropzone
          role="original"
          texts={uk.dropzones.original}
          inspection={uk.inspection}
          clearLabel={uk.dropzones.clear}
          codes={uk.codes}
          file={original}
          onFileChange={takeOriginal}
          onAcceptedChange={setOriginalAccepted}
        />
        <Dropzone
          role="track"
          texts={uk.dropzones.track}
          inspection={uk.inspection}
          clearLabel={uk.dropzones.clear}
          codes={uk.codes}
          file={track}
          onFileChange={takeTrack}
          onAcceptedChange={setTrackAccepted}
        />
      </div>

      <div className="mt-gutter flex flex-col items-start gap-2">
        <button
          type="button"
          disabled={!bothChosen || working}
          aria-busy={working}
          aria-describedby={bothChosen ? undefined : waitingHintId}
          onClick={() => void onRepair()}
          className="type-display rounded-pill bg-accent px-8 py-3 text-accent-ink transition-opacity disabled:cursor-not-allowed disabled:bg-surface-raised disabled:text-text-muted"
        >
          {working ? uk.repair.working : uk.repair.action}
        </button>
        {/* The reason sits under the button, not inside it: a disabled control cannot explain
            itself, and the hint has to stay readable once the button is no longer the subject. */}
        {!bothChosen && (
          <p id={waitingHintId} className="text-sm text-text-muted">
            {uk.repair.waitingForFiles}
          </p>
        )}
        {/* A failed repair leaves the page where the files are: both zones still hold what the
            service accepted, so the answer to a refusal is one more press, not two more drops.
            A live region, because the sentence arrives seconds after the button was pressed. */}
        <div role="status" className="empty:hidden">
          {state.status === 'refused' && (
            <p className="text-sm text-danger">{codes[state.code] ?? state.code}</p>
          )}
          {state.status === 'unreachable' && (
            <p className="text-sm text-danger">{uk.repair.unreachable}</p>
          )}
        </div>
      </div>

      <div className="mt-section">
        <HowItWorks title={uk.howItWorks.title} steps={uk.howItWorks.steps} />
      </div>
    </main>
  );
}

/**
 * Where the page is. `repaired` is the only state that carries anything, and it carries the
 * Original's file name with the report: the saved file is named after it, and by then the person
 * may already have pressed "start again" on a second repair.
 */
type RepairState =
  | { readonly status: 'idle' }
  | { readonly status: 'working' }
  | {
      readonly status: 'repaired';
      readonly report: RepairReport;
      readonly originalName: string;
    }
  | { readonly status: 'refused'; readonly code: string }
  | { readonly status: 'unreachable' };

/**
 * The same state with a failed repair forgotten. A repair in flight and a result are left alone:
 * neither is a verdict on a file, and dropping a result because a zone changed would throw away
 * the file the person came for.
 */
function withoutFailure(state: RepairState): RepairState {
  return state.status === 'refused' || state.status === 'unreachable'
    ? { status: 'idle' }
    : state;
}
