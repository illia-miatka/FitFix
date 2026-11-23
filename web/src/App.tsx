import { useId, useState } from 'react';

import { Dropzone } from './components/Dropzone';
import { HowItWorks } from './components/HowItWorks';
import { uk } from './i18n/uk';

/**
 * The empty state of EXPERIENCE.md: a heading, two zones naming their roles, the three steps,
 * and one CTA that waits for both files. Working and result are Stories 2.5 to 2.6; nothing is
 * uploaded here, the two files only live in state.
 *
 * Below 768 px everything is one column (DESIGN.md, Layout & Spacing) — the grids start at
 * `grid-cols-1` and widen at `md:`, so the narrow layout is the one that needs no breakpoint.
 */
export function App() {
  const [original, setOriginal] = useState<File | null>(null);
  const [track, setTrack] = useState<File | null>(null);
  const waitingHintId = useId();

  const bothChosen = original !== null && track !== null;

  return (
    <main className="mx-auto w-full max-w-[1200px] px-gutter py-section">
      <h1 className="type-display max-w-[20ch] text-3xl text-text md:text-5xl">
        {uk.hero.title}
      </h1>
      <p className="mt-gutter max-w-[60ch] text-text-muted">{uk.hero.lede}</p>

      <div className="mt-section grid grid-cols-1 gap-gutter md:grid-cols-2">
        <Dropzone
          texts={uk.dropzones.original}
          clearLabel={uk.dropzones.clear}
          file={original}
          onFileChange={setOriginal}
        />
        <Dropzone
          texts={uk.dropzones.track}
          clearLabel={uk.dropzones.clear}
          file={track}
          onFileChange={setTrack}
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
