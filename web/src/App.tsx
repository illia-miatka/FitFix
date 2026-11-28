import { useCallback, useId, useRef, useState } from 'react';

import { repair as askForRepair } from './api/repair';
import type { RepairReport, TrackCoverageMode } from './api/repair';
import { Dropzone } from './components/Dropzone';
import { HowItWorks } from './components/HowItWorks';
import { Notice } from './components/Notice';
import { Result } from './components/Result';
import { drawnTrackFile, type Point } from './drawing';
import { codeText } from './errors';
import { uk } from './i18n/uk';

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

    // Neither mode: the conservative length is the only one a request that has asked nobody
    // anything may end on (AD-17), and the service's own answer about the track's coverage is the
    // only one it may act on unasked (AD-27). The length choice is Story 1.8's; the coverage one is
    // `confirmFragment` below, and both are the same request again with one field more.
    const outcome = await askForRepair(original, track);
    running.current = false;

    setState(
      outcome.status === 'repaired'
        ? { status: 'repaired', report: outcome.report, originalName: original.name }
        : outcome,
    );
  }

  /**
   * The answer to the fragment warning (Story 5.2): the very same two files again, with the
   * coverage confirmed, because confirming is a different request and not a flag on the answer
   * already in hand (AD-3, AD-22). The new report replaces the old one, warning and gate included.
   * Answers whether the repeat arrived, which is all the result screen needs to know.
   */
  const confirmFragment = useCallback(
    () => answerCoverage('fragment'),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [original, track],
  );

  /**
   * The other answer to the same question: the track is the whole route and the detector read a
   * lap for a stretch of one. The request goes again with the coverage stated, which puts the
   * repair back on the whole-track mechanism — a question with only a yes is not a question.
   */
  const useWholeRoute = useCallback(
    () => answerCoverage('whole'),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [original, track],
  );

  /**
   * The drawn stretch (Story 5.4): the same repair with the points the athlete placed as the
   * track and the coverage stated as a fragment, because drawing it is the answer to the question.
   * The drawn file replaces the track the page holds, so a later answer repeats this request and
   * not the one the drawing replaced.
   */
  const drawStretch = useCallback(
    (line: Point[]) => answerCoverage('fragment', drawnTrackFile(line)),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [original],
  );

  async function answerCoverage(
    coverage: TrackCoverageMode,
    replacement?: File,
  ): Promise<CoverageAnswer> {
    const chosen = replacement ?? track;
    if (original === null || chosen === null || running.current) return { ok: false };

    running.current = true;
    const outcome = await askForRepair(original, chosen, undefined, coverage);
    running.current = false;

    if (outcome.status === 'repaired') {
      if (replacement !== undefined) setTrack(replacement);
      setState({ status: 'repaired', report: outcome.report, originalName: original.name });
      return { ok: true };
    }

    // A refusal says which answer the service could not act on, and that sentence is the
    // dictionary's (Story 2.7): telling somebody to try again is wrong advice for a 400, where
    // the same two files would meet the same code.
    return outcome.status === 'refused' ? { ok: false, code: outcome.code } : { ok: false };
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
            onConfirmFragment={confirmFragment}
            onUseWholeRoute={useWholeRoute}
            onDrawStretch={drawStretch}
          />
        </div>
      </main>
    );
  }

  return (
    <main className="mx-auto w-full max-w-[1200px] px-gutter py-section">
      <h1 className="type-display max-w-[20ch] text-3xl text-text md:text-5xl">{uk.hero.title}</h1>
      <p className="mt-gutter max-w-[60ch] text-text-muted">{uk.hero.lede}</p>

      <div className="mt-section grid grid-cols-1 gap-gutter md:grid-cols-2">
        <Dropzone
          role="original"
          texts={uk.dropzones.original}
          inspection={uk.inspection}
          clearLabel={uk.dropzones.clear}
          retryLabel={uk.errors.retry}
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
          retryLabel={uk.errors.retry}
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
            service accepted, so neither failure costs a second drop. A live region, because the
            sentence arrives seconds after the button was pressed. */}
        <div role="status" className="empty:hidden">
          {/* A 400 is a verdict on the two files that were sent, so it is not offered again from
              the same pair: the service would answer with the same code. A repair that broke one
              of our own invariants leaves as 500 with its code, and that one is worth a press —
              `SUMMARY_SERIES_MISMATCH` is not the athlete's fault and not their file's. */}
          {state.status === 'refused' && (
            <Notice
              {...codeText(uk.codes, state.code)}
              retry={
                state.worthRepeating
                  ? { label: uk.errors.retry, onPress: () => void onRepair() }
                  : undefined
              }
            />
          )}
          {/* A request that never arrived is worth repeating, and both accepted files are still
              in their zones — so the whole fix is one press, in the same words a zone uses. */}
          {state.status === 'unreachable' && (
            <Notice
              text={uk.repair.unreachable}
              retry={{ label: uk.errors.retry, onPress: () => void onRepair() }}
            />
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
 * What answering the fragment question came back as: it worked, or it did not and the service
 * said why. The code travels so the screen can state the service's own sentence instead of
 * telling somebody to try again where trying again cannot help (Story 2.7).
 */
export interface CoverageAnswer {
  readonly ok: boolean;
  readonly code?: string;
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
  | {
      readonly status: 'refused';
      readonly code: string;
      /** `RepairOutcome`'s own: whether asking again could end differently (a 500 can). */
      readonly worthRepeating: boolean;
    }
  | { readonly status: 'unreachable' };

/**
 * The same state with a failed repair forgotten. A repair in flight and a result are left alone:
 * neither is a verdict on a file, and dropping a result because a zone changed would throw away
 * the file the person came for.
 */
function withoutFailure(state: RepairState): RepairState {
  return state.status === 'refused' || state.status === 'unreachable' ? { status: 'idle' } : state;
}
