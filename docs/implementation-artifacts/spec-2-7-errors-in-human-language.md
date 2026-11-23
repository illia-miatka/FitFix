---
title: 'Story 2.7 — Errors speak human language'
type: 'feature'
created: '2026-11-23'
status: 'done'
route: 'dispatch'
review_loop_iteration: 0
baseline_commit: '72d431f3e0757dc1f6deb53480c9070dc8aa8dca'
context: ['{project-root}/docs/implementation-artifacts/epic-2-context.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A code the dictionary does not know is printed raw, in four places (`codes[code] ?? code`), so the interface's worst case is also its ugliest: `TRACK_LENGTH_MODE_INVALID` on the screen of somebody who wanted to repair a run. A request that never arrived states the problem and offers nothing to do about it — the only way to retry an inspection is to drop the same file again. And the result screen lacks the Strava row EXPERIENCE.md asks for.

**Approach:** One lookup turns a code into a sentence and, when the dictionary has none, into the generic sentence instead; the code travels on in a `data-code` attribute, where support can read it and a person never does. Every failure that is worth another attempt carries the attempt: a zone retries its inspection, the repair retries itself, both through one notice component so the two read alike. The result screen gains the disabled "Залити у Strava — незабаром" button.

</frozen-after-approval>

## Code Map

- `web/src/i18n/uk.ts` -- `codeTexts` already holds one two-part sentence per code, and `uk.test.ts` holds its keys equal to `DiagnosticCodes`; nothing in it needs rewording. The shared `unreachable` constant is where the generic sentence, the retry label and the Strava label go. `format.ts` is the precedent for a pure shared module.
- `web/src/App.tsx` -- line 151: `codes[state.code] ?? state.code`, and the `role="status"` block that renders `refused` and `unreachable` with no action. `onRepair` is what a retry re-runs; `running` already guards a double press.
- `web/src/components/Dropzone.tsx` -- line 167 and line 245: the same fallback twice, once for a refusal and once for an original's own warning codes. The inspect call lives in a `useEffect` keyed on `[file, role, onAcceptedChange]`, so a retry needs a counter in that dependency list -- the file object does not change.
- `web/src/components/Result.tsx` -- line 153: the fallback again, inside `Callout`. The download and "start again" controls at the end of the component are where the Strava button belongs.
- `web/src/components/Dropzone.test.tsx`, `web/src/App.test.tsx`, `web/src/components/Result.test.tsx` -- the existing suites, including the refusal rows that currently assert the raw code is shown. Those assertions invert.
- Do not touch: the API, the core, `DiagnosticCodes`, or any text in `codeTexts`. No code is added to the registry by this story, and `theme.css` stays the only place with a colour.

## Tasks & Acceptance

**Execution:**
- [x] `web/src/errors.ts` -- new. `codeText(codes, code)` returns the dictionary's sentence or the generic one, and says which it returned, so a caller can carry the code in `data-code` only when the sentence is generic. -- one place decides what an unknown code reads as.
- [x] `web/src/i18n/uk.ts` -- `errors.unexpected` ("Щось пішло не так" with what to do), `errors.retry` ("Спробувати ще"), and `result.strava`. -- AD-4: no literal in a component.
- [x] `web/src/components/Notice.tsx` -- new. A failure sentence with an optional retry button, used by both the zones and the repair, carrying `data-code` when there is a code behind it. -- the two failures must read alike, and a shared component is how they stay that way.
- [x] `web/src/components/Dropzone.tsx` -- route both lookups through `codeText`, render refusals and unreachable through `Notice`, and add a retry that re-runs the inspection for the file already in the zone. -- the file object is unchanged by a retry, so the counter is what makes the effect run again.
- [x] `web/src/App.tsx` -- route the lookup through `codeText`, render both failures through `Notice`, and give the unreachable repair a retry that calls `onRepair` again. -- a failed repair keeps both accepted files, so one press is the whole fix.
- [x] `web/src/components/Result.tsx` -- `Callout` through `codeText`, and the disabled Strava button beside the download. -- EXPERIENCE.md, Result state.
- [x] `web/src/errors.test.ts` -- new, and update the three component suites to the I/O matrix below. -- the inverted assertions are the story.

**Acceptance Criteria:**
- Given a response whose code the dictionary knows, when the interface shows it, then the person reads the two-part sentence and no code appears in the rendered text.
- Given a response whose code the dictionary does not know, when the interface shows it, then the person reads the generic sentence, and the code is present only as `data-code`.
- Given a request that never arrived, when it failed, then the message offers "Спробувати ще", and pressing it repeats that same request.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| A known code | a refusal carrying `NOT_A_FIT_FILE` | the dictionary's sentence; the rendered text contains no code | N/A |
| An unknown code | a refusal carrying `WHAT_IS_THIS` | the generic sentence, `data-code="WHAT_IS_THIS"` | N/A |
| A warning the dictionary knows | a result carrying `POSITIONS_DISCARDED` | its sentence, severity still said in words | N/A |
| An unknown warning | a result carrying an unregistered code | the generic sentence, severity unchanged | N/A |
| Retry an inspection | a zone whose request never arrived | pressing retry asks `/api/inspect` again for the same file | a second failure leaves the retry in place |
| Retry a repair | the repair's request never arrived | pressing retry posts both files again | the double-press guard still holds |
| A refusal offers no retry | a 400 with a code | no retry button: the same file would be refused again | N/A |
| Retry while reading | retry pressed, answer still in flight | one request, and a late answer for the earlier attempt cannot overwrite it | N/A |
| The Strava button | the result state | present, disabled, named "Залити у Strava — незабаром" | N/A |
| Clearing after a failure | the clear control on a failed zone | the zone returns to empty and the retry goes with it | N/A |

## Verification

**Commands:**
- `npm --prefix web run typecheck` -- expected: clean
- `npm --prefix web run test` -- expected: all pass, including the inverted refusal assertions
- `npm --prefix web run build` -- expected: succeeds

## Design Notes

EXPERIENCE.md asks for a toast on a server failure. The sentence and the retry go where the control that failed is instead: a toast takes the explanation away on a timer and the retry with it, which is the opposite of the story's point. The copy it specifies is kept exactly, and the zones already answer in a live region.

A refusal gets no retry on purpose. The service refused that file for a reason in the registry, and pressing retry would send the same bytes to the same code -- the way out of a refusal is another file, which the zone already offers. Only a request that never arrived is worth repeating.

The code survives in `data-code`, not in the sentence: an unregistered code is our bug, and the generic sentence is what the person can act on, while an attribute still reaches whoever gets the report.

## Review Triage Log

One layer ran, the verification-gap one, under the economy Ілля asked for; the blind hunter and
the edge-case hunter were skipped. It found no verification gaps -- every one of the four former
`codes[code] ?? code` sites has an assertion that the generic sentence renders, the code is absent
from the text and `[data-code]` carries it, and it mutation-checked the mechanism that looked
fragile: `attempt` sits in the effect's dependency list without being read in the body, so an
"unused dependency" cleanup could silently kill the retry -- removing it fails four tests.

It did find a defect in the story's own point, and it was fixed. `FailureStatus.cs` deliberately
serves `INTERNAL_ERROR`, `SUMMARY_SERIES_MISMATCH`, `SUMMARY_WITHOUT_LAPS` and
`DISTANCE_SERIES_IMPLAUSIBLE` at 500 while keeping their code, precisely so the athlete is not
told their file is wrong -- and the client discarded `response.status`, mapping every failure with
a code to a refusal, which this story then gave no attempt. The one case EXPERIENCE.md asks for a
retry on had nothing to press, under a sentence ending in «Спробуй ще раз». The status now travels
as a decision rather than a number: `api/failure.ts` answers whether a failure is worth repeating,
and both the zone and the repair offer the attempt when it is. Two misleading fixtures went with
it -- `App.test.tsx` paired `DISTANCE_SERIES_IMPLAUSIBLE` with a 400 twice, a code the API serves
as 500, so those tests would have passed for the wrong reason.

Two further decisions. `Notice`'s `retryLabel` and `onRetry` were independently optional, so a
call site passing the handler alone would have lost the button with no type error; they are one
optional object now, and a probe confirmed the half-passed form is a compile error. And the
implementer left `REQUEST_MALFORMED` without an attempt, reading the rule as "the status decides";
I overrode that. `FailureStatus.ForStatus` answers every unnamed sub-500 status with that code, so
it is what a truncated or timed-out upload arrives as -- the bytes on disk are intact and the
journey is what broke, which makes it the one 4xx a second attempt can end differently. It is a
code-keyed exception in `failure.ts`, mutation-checked in both directions.

The matrix row "Retry while reading" is not reachable as written: the attempt exists only in a
failed state, so no ask can be in flight when it is pressed. The two reachable halves are tested
instead -- the press replaces the failure with the skeleton and takes the button with it, exactly
two asks; and a file dropped during a retry wins over the retry's late answer.

Verified independently: typecheck clean, 85 tests, the production build. Not checked by eye in a
browser: the notice box and the Strava button use existing tokens only (`theme.test.ts` passes),
but neither was looked at on a 375 px screen.

Left for Ілля: nothing blocking. `CLIENT_DISCONNECTED` is unreachable client-side by definition,
and `api/failure.ts` has no test file of its own, following the two API modules it sits beside --
its branch is pinned from both sides through the zone and the repair.
