---
title: 'Story 2.6 — Take the repaired file away'
type: 'feature'
created: '2026-11-23'
status: 'done'
route: 'dispatch'
review_loop_iteration: 0
baseline_commit: '071b98ce48c1ae12273da2dc348a21f8430071c9'
context: ['{project-root}/docs/implementation-artifacts/epic-2-context.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The button is disabled until two files are accepted and then does nothing: there is no `onClick`. Everything the service can do is reachable only from a terminal, which is the thing Epic 2 exists to change.

**Approach:** Pressing the button sends both files to `/api/repair`, shows that it is working, and on the answer turns the page into its result state: distance, time and pace as "before → after" with the new value in the accent colour, a button that saves the FIT as `<original name>_fixed.fit`, and a way to start again with empty zones. Pace and time are formatted on the client from metres and seconds, because the contract carries no derived values.

</frozen-after-approval>

## Code Map

- `web/src/App.tsx` -- holds both accepted files and the disabled state; this story gives it the three page states from EXPERIENCE.md, of which the empty one exists.
- `web/src/api/inspect.ts` -- the shape to follow for a typed call and an outcome union of described, refused and unreachable.
- `src/FitFix.Api/Contracts/RepairResponse.cs` -- what comes back: `before` (sport, startedAt, recordCount, recordedDistanceMeters, discardedPositionCount), `after` (distance, elapsed, moving and paused seconds, averages and maxima, elevation, lapCount), `track` (lengthMeters, usedLengthMeters), `zones`, `series`, `warnings` (code and severity), `fitBase64`.
- `web/src/i18n/uk.ts` -- every string, and `uk.codes` keyed by the registry. The warnings on the result screen read from there.
- `web/src/components/Dropzone.tsx` -- where the clear control and the inspect call already live; the repair call belongs beside them in `api/`, not here.
- `docs/planning-artifacts/ux-designs/ux-fitfix-2026-11-19/DESIGN.md` -- the metric delta component: "3,91 км → **6,54 км**" with the new value in `accent`, and `tnum` on metrics.
- `docs/planning-artifacts/ux-designs/ux-fitfix-2026-11-19/EXPERIENCE.md` -- the result state's contents and that a warning callout sits above the numbers.

## Tasks & Acceptance

**Execution:**
- [x] `web/src/api/repair.ts` -- new. One typed call posting both files and the optional track-length mode, returning repaired, refused or unreachable, as the inspect call does. -- one place reads the contract.
- [x] `web/src/format.ts` -- new. Metres to kilometres, seconds to a duration, and metres with seconds to a pace, all with the dictionary's separators. -- AD-14: the contract carries base quantities and the client formats them.
- [x] `web/src/App.tsx` -- the working state while the request is in flight, and the result state after it. The button cannot be pressed twice. -- a second request would repair the same files again.
- [x] `web/src/components/Result.tsx` -- new. Distance, time and pace as before → after with the new value in the accent colour, the warnings above them from the dictionary, the download button and "start again". -- the screen the story is for.
- [x] `web/src/components/Result.tsx` -- the download saves `<original name without extension>_fixed.fit` from the base64, and the object URL it creates is released. -- a name the user recognises, and no leaked blob.
- [x] `web/src/App.tsx` -- "start again" returns to empty zones with no file and no result. -- the same page, not a reload.
- [x] `web/src/i18n/uk.ts` -- the result screen's strings and labels. -- no literal in a component.
- [x] `web/src/components/Result.test.tsx`, `web/src/App.test.tsx` -- cover the I/O matrix below with the fetch stubbed. -- the states are the story.

**Acceptance Criteria:**
- Given two accepted files, when the button is pressed, then the page shows it is working and then the result, with distance, time and pace as before → after.
- Given the result, when the download is pressed, then the saved file is named after the original with `_fixed.fit`, and its bytes are the base64 decoded.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| The repair runs | both files accepted, button pressed | working state, then the result | N/A |
| Before and after | the answer | distance, time and pace for both, the new value in the accent colour | N/A |
| Pace is the client's | distance in metres and moving seconds | the pace shown equals distance over moving seconds, formatted | N/A |
| No distance before | an original whose device recorded none | the before column says so rather than showing zero or a pace | N/A |
| The download | the download button | a file named `<original>_fixed.fit` whose bytes are the decoded base64 | the object URL is revoked |
| Start again | the result state | empty zones, no result, the button disabled again | N/A |
| One press only | the button pressed twice quickly | one request | N/A |
| A refusal | the service refuses with a code | the dictionary's text for it, and the page stays where the files are | N/A |
| The service is unreachable | the request rejects | the page says the service could not be reached, not that a file is bad | N/A |
| Warnings are shown | an answer carrying codes | each code's text above the numbers, blocking ones distinguishable from info | N/A |

## Verification

**Commands:**
- `npm --prefix web run typecheck` -- expected: clean
- `npm --prefix web run test` -- expected: all tests pass
- `npm --prefix web run build` -- expected: succeeds

## Design Notes

The pace is computed here and nowhere else on the client. The contract gives distance in metres and moving seconds because a second number travelling beside them could disagree with the file; the screen must therefore divide them itself, and the one place that happens is the formatter, so the result screen and any later preview cannot drift apart.

The blocking warning is shown but does not gate the download in this story. AD-22 says a blocking severity withholds the file until the user confirms, and the confirmation that matters — trusting the whole track — changes the result rather than merely opening a gate, so it belongs with the story that offers that choice. What this story must not do is hide a blocking warning or present it as an aside: the one about an over-long track is the difference between a true pace and a flattering one.

## Review Triage Log

No review layer ran: every row of the matrix has a test, including the decoded bytes, the `_fixed.fit` name and the released object URL, and the pipeline behind the screen was already exercised against the real pair through the API. Verified independently: typecheck, 59 tests and the production build.

Three decisions the implementer surfaced rather than buried. The time is shown as `movingSeconds`, so distance over time on the screen equals the pace beside it and matches the file's own `total_timer_time`. The object URL is released after the click rather than inside it, because revoking it synchronously cancels the download in some browsers. And the formatters moved out of the dropzone into one module, so a zone and the result screen cannot write the same distance two ways.

For Ілля: the time row reads as a before → after delta whose two sides are the same number, because the repair keeps the original's clock. It is labelled and documented as exactly that claim — the distance changed, the duration did not — but a single value would also be defensible.

Not in this story: the disabled "Залити у Strava" button EXPERIENCE.md mentions, and the AD-22 download gate, which belongs with the choice that changes the result.
