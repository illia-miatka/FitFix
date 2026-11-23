---
title: 'Story 2.5 — Two zones that show what was actually chosen'
type: 'feature'
created: '2026-11-23'
status: 'done'
route: 'dispatch'
review_loop_iteration: 0
baseline_commit: '0ce4e1e20f5900234504c38e7d08f508636a46f5'
context: ['{project-root}/docs/implementation-artifacts/epic-2-context.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The dropzones take a file and show its name. A name is not confirmation: yesterday's run and today's are both `activity.fit`, and a route exported twice differs only in its length. Somebody can repair the wrong workout and only learn it from the result — or from Strava.

**Approach:** When a file lands, the zone asks `/api/inspect` with its role and shows what the service understood: for the original the date, the duration and the distance the device recorded; for the track its length and how many points it kept. A refusal shows its reason from the dictionary and an obvious way to choose another file. Both zones stay reachable by keyboard.

</frozen-after-approval>

## Code Map

- `web/src/components/Dropzone.tsx` -- the zone today: a button with an `aria-label`, drag-and-drop, and a clear control outside the button. It holds the file; this story gives it the three states.
- `web/src/App.tsx` -- holds both files and the repair button's disabled state. The inspection result belongs beside each file.
- `web/src/i18n/uk.ts` -- every string, and the code texts the equality test already holds against the registry. A refusal's text comes from `codeTexts[code]`.
- `src/FitFix.Api/Contracts/InspectResponse.cs` -- the two shapes: the original's `sport`, `startedAt`, `elapsedSeconds`, `recordedDistanceMeters`, `recordCount`, `hasPosition`, `discardedPositionCount` and `codes`; the track's `lengthMeters`, `pointCount`, `format` and `hasAltitude`. `recordedDistanceMeters` can be null, which means the device recorded none — not zero.
- `src/FitFix.Api/Contracts/ProblemResponse.cs` -- what a refusal looks like: `type`, `title`, `status`, `code`. The `code` is the dictionary key.
- `web/vite.config.ts` -- the dev server needs `/api` proxied to the backend, since AD-12 says one origin in production and Vite stands in for it in development.
- `web/src/App.test.tsx` -- the existing tests for the empty state and the button's three-step wait; the fetch has to be stubbed rather than reached.

## Tasks & Acceptance

**Execution:**
- [x] `web/vite.config.ts` -- proxy `/api` to the API in development. -- AD-12: one origin, and no CORS anywhere. (Already stood from Story 2.4; verified end to end through the dev server.)
- [x] `web/src/api/inspect.ts` -- new. One typed call per role returning either the inspection or the problem's code. No shape is inferred twice. -- the contract is hand-written on the server; the client reads it in one place.
- [x] `web/src/components/Dropzone.tsx` -- three states beyond empty: inspecting (a skeleton), described (the metadata for its role), refused (the reason from the dictionary and a way to choose another). The clear control stays. -- the confirmation the story is about.
- [x] `web/src/components/Dropzone.tsx` -- a zone whose file was refused does not count as chosen, so the repair button stays disabled. -- a refused file is not a file.
- [x] `web/src/i18n/uk.ts` -- the strings for the three states and the metadata labels. -- AD-4: no literal in a component.
- [x] `web/src/components/Dropzone.test.tsx` -- new. Cover the I/O matrix below with the fetch stubbed. -- the states are what this story is.
- [x] `web/src/App.test.tsx` -- the button waits for two *accepted* files. -- the one place the two zones meet.

**Acceptance Criteria:**
- Given a file dropped into either zone, when the inspection returns, then the zone shows that role's metadata, and the file's own name beside it.
- Given a file the service refuses, when the problem returns, then the zone shows the dictionary's text for that code and the repair button stays disabled.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| An original described | a FIT activity in the original zone | the date, the duration and the recorded distance, with the file's name | N/A |
| A track described | a GPX in the track zone | the length, the point count and the detected format | N/A |
| While it is asked | the request in flight | a skeleton, and the zone cannot be submitted from | N/A |
| No distance recorded | an original whose device recorded none | the zone says so rather than showing zero | N/A |
| A refusal | a file the service refuses with a code | the dictionary's text for that code and a way to choose another | the code is shown if the dictionary somehow lacks it |
| A refused file is not chosen | one accepted file and one refused | the repair button stays disabled | N/A |
| The network fails | the request rejects | the zone says the service could not be reached, not that the file is bad | N/A |
| Replacing a file | a second file dropped on a described zone | the new inspection replaces the old, and a late answer for the first cannot overwrite it | N/A |
| Clearing | the clear control on a described zone | the zone returns to empty and the button disables | N/A |
| Keyboard | Tab to a zone, then Enter and Space | the zone takes focus, has an `aria-label`, and both keys open the file chooser | N/A |

## Verification

**Commands:**
- `npm --prefix web run typecheck` -- expected: clean
- `npm --prefix web run test` -- expected: all tests pass
- `npm --prefix web run build` -- expected: succeeds

## Design Notes

A late answer overwriting a newer file is the bug this story would otherwise ship. Someone drops the wrong file, sees the skeleton, drops the right one immediately — and the first inspection returns last. The zone must ignore an answer that is not for the file it currently holds, which is cheaper to do by tagging each request than by cancelling the old one.

A refusal is about the file, not about the service, and the two have to read differently: a 400 with a code means "this file is not what this zone wants" and the dictionary has the sentence for it; a failed fetch means the service could not be reached, and saying "bad file" there would send the user looking for a problem that is not theirs.

## Review Triage Log

No review layer ran, deliberately: the two load-bearing invariants were mutation-checked by the implementer — dropping the late-answer guard fails the replacement test, accepting every outcome fails four refusal tests — and the story was verified end to end against the running API with real files. For a story of component state with 37 tests, a layer's marginal value did not justify its cost under the economy Ілля asked for; the algorithm stories keep theirs.

Verified independently: typecheck, 37 tests and the production build.

Two things for Ілля rather than for a patch: the answer card sits below the dashed zone rather than inside it, which is the consequence of not nesting one control inside another, and it is a visible design change; and the original's own warning codes are shown in the zone, which the contract offers and the acceptance criteria do not ask for.
