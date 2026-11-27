---
title: 'Story 5.3 — It is visible which stretch was redrawn'
type: 'feature'
created: '2026-11-27'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: ['{project-root}/docs/implementation-artifacts/epic-5-context.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The service names the redrawn stretch only while it is asking about it. `Result.tsx` renders the span inside `FragmentGate`, and the gate disappears the moment the athlete confirms — although the confirmed answer still carries `redrawn` (`FragmentTests.Confirming_the_fragment_changes_nothing_but_the_question` pins that). So the screen that offers the file says nothing about which seconds of the run are a drawn line and which are the watch's own, which is exactly what Story 5.3 promises to show. Story 5.2's own web fixtures hid it: they modelled the confirmed answer with `redrawn: null`, which the real service never sends.

**Approach:** One `RedrawnStretch` component states the span (from, to, metres) and is placed wherever `report.redrawn` is non-null — inside the gate while it asks, beside the numbers once it has been answered — followed, outside the gate, by one sentence saying the rest was left as the watch recorded it. The web fixtures model the confirmed answer as the service sends it. The second acceptance criterion (a short track is not "far from the original") was delivered by Story 5.1's `closestApproach`; this story pins it with a test, because nothing asserts it for a fragment today.

## Boundaries & Constraints

**Always:** Numbers come from `report.redrawn` and are formatted by `format.ts`; no sentence in the dictionary holds a number (AD-14, AD-18). A whole-route answer (`redrawn: null`) shows nothing new.

**Never:** No contract, core or API change. No map or per-zone colouring (Epic 3, Story 3.2). No second copy of the span markup.

</frozen-after-approval>

## Implementation Notes

- The gap was in the screen, not the contract: the confirmed answer already carried `redrawn` (pinned by `FragmentTests.Confirming_the_fragment_changes_nothing_but_the_question` and `RepairTests`), and `Result.tsx` rendered the span only inside `FragmentGate`. Story 5.2's web fixtures modelled the confirmed answer with `redrawn: null`, which the service never sends; `aConfirmedFragment` now models it as sent.
- `RedrawnStretch` is the one place that words the span. The gate uses it while asking; `Result` uses it, plus `uk.result.keptOwn`, once the warning is gone. `keptOwn` is deliberately absent while the question is open: claiming the rest is the watch's before the athlete has said the stretch is right would pre-empt the answer.
- Second acceptance criterion was delivered by Story 5.1 (`closestApproach`, not the first position against the whole track) and had no assertion for a fragment. `FragmentTests.A_track_is_not_called_far_from_the_activity_for_being_short` pins it for a tail and a middle fragment. Mutation: forcing `TrackCoverageMode.WholeRoute` (the old whole-track reading) makes the false `TRACK_FAR_FROM_ORIGINAL` appear and the test fail.
- Mutation on the web side: switching off the standalone stretch fails exactly the two tests that need it (`Result` and `App`), 104 others stay green.
- Not checked: the rendered screen in a browser with a real fragment pair.
- Files: `web/src/components/Result.tsx`, `web/src/i18n/uk.ts`, `web/src/test/fixtures.ts`, `web/src/components/Result.test.tsx`, `web/src/App.test.tsx`, `tests/FitFix.Core.Tests/FragmentTests.cs`.
- `TRACK_FAR_FROM_ORIGINAL` exists only in `Aligner` and so surfaces only in the repair report; the track zone's inspection (`/api/inspect`) never carries it. The second criterion therefore has no inspect-path surface to pin, and the repair report is the whole of it.
- Wording of `keptOwn` is narrowed to what AD-26 and `AssertOutsideTheSpanIsUntouched` actually guarantee: coordinates, altitude and distance outside the span.

## Review Triage Log

- `keptOwn` unbacked by a core test -- medium, patched in part. `AssertOutsideTheSpanIsUntouched` already pins every record outside the span against the no-fragment repair, so the promise is backed; the wording ("the rest of the workout") was wider than AD-26 (coordinates, altitude, distance), so it is narrowed to exactly that.
- Second criterion only half pinned (inspect path) -- low, documented. `TRACK_FAR_FROM_ORIGINAL` is raised only by `Aligner`, so the repair report is its only surface; there is no inspect-path behaviour to test. Recorded in Implementation Notes.
- Spec and tracking left `in-progress`; hard-coded "104 tests" -- false for the first (finalize sets `done` and `review` after review, which is this step); the count was dropped from the notes. CLAUDE.md progress line -- rejected, it is updated when the epic's stories are merged, not per spec.
- Card classes repeated, no landmark on the standalone block -- low, rejected. The span markup itself exists once (`RedrawnStretch`); the card classes wrap different content in each place (gate: buttons; answer: one sentence). The `<dl>` with its `<dt>` label is the semantics the gate already used.
- `aConfirmedFragment` not used by an older inline test; stray backslash in a comment; unused destructured variable -- low, patched.
- A `redrawn` span with `toSeconds < fromSeconds` -- false: the core builds the span from `TrackReach`, whose ends are ordered by construction, and the contract test pins the shape.
- A `redrawn` span beside a different blocking warning -- false: `gated` is scoped to the fragment code, and a fragment that was used and is not asked about is exactly when the standalone stretch should show.
