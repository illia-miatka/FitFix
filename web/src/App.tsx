import { useId, useState } from 'react';

import { Dropzone } from './components/Dropzone';
import { HowItWorks } from './components/HowItWorks';
import { uk } from './i18n/uk';

/**
 * The empty state of EXPERIENCE.md: a heading, two zones naming their roles, the three steps,
 * and one CTA that waits for both files. The result screen is Story 2.6; the repair itself is
 * not asked for here, and each zone asks `/api/inspect` about its own file.
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
  const waitingHintId = useId();

  const bothChosen = originalAccepted && trackAccepted;

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
          onFileChange={setOriginal}
          onAcceptedChange={setOriginalAccepted}
        />
        <Dropzone
          role="track"
          texts={uk.dropzones.track}
          inspection={uk.inspection}
          clearLabel={uk.dropzones.clear}
          codes={uk.codes}
          file={track}
          onFileChange={setTrack}
          onAcceptedChange={setTrackAccepted}
        />
      </div>

      <div className="mt-gutter flex flex-col items-start gap-2">
        <button
          type="button"
          disabled={!bothChosen}
          aria-describedby={bothChosen ? undefined : waitingHintId}
          className="type-display rounded-pill bg-accent px-8 py-3 text-accent-ink transition-opacity disabled:cursor-not-allowed disabled:bg-surface-raised disabled:text-text-muted"
        >
          {uk.repair.action}
        </button>
        {/* The reason sits under the button, not inside it: a disabled control cannot explain
            itself, and the hint has to stay readable once the button is no longer the subject. */}
        {!bothChosen && (
          <p id={waitingHintId} className="text-sm text-text-muted">
            {uk.repair.waitingForFiles}
          </p>
        )}
      </div>

      <div className="mt-section">
        <HowItWorks title={uk.howItWorks.title} steps={uk.howItWorks.steps} />
      </div>
    </main>
  );
}
