# Epic 5 Context: Repair Only What Broke

<!-- Reviewed 2026-11-28 against the planning artifacts (only the 3.2 basemap wording changed; nothing here). Written 2026-11-23 by hand from the epics file, the architecture spine and the code, at the
     end of the grooming session that cut this epic. Regenerate with compile-epic-context if the
     planning artifacts change. -->

## Goal

A reference track may cover only the stretch where the GPS lied. Five broken kilometres out of fifty are repaired with a piece of the route rather than by redrawing all fifty. The service recognises a fragment itself and asks before using it; the fragment arrives as a file now, and drawn on the map once Epic 3 has one.

## Stories

- Story 5.1: A track shorter than the activity repairs its own stretch
- Story 5.2: The service asks before treating a track as a fragment
- Story 5.3: It is visible which stretch was redrawn
- Story 5.4: Draw the missing stretch on the map (depends on Story 3.2)

## Requirements & Constraints

- **AD-27** is the binding rule. The track is a *stretch* of the route, not necessarily the whole of it. It attaches to the activity by Anchors at its own ends: the fragment's start to the last true GPS position before the failure, its end to the first true position after it. Where no true position survives on that side, the activity's own start or finish becomes that end. A fragment that attaches at neither end is a refusal carrying a code, never a guess.
- **AD-27, second rule.** `Aligner.Measure` must stop opening the chain with `(0, 0, 0)` and `EndingAt` must stop pinning the finish to the track's end when the track is a fragment. Both are hard-coded today and are the reason a short track currently produces nonsense.
- **AD-26** still governs everything outside the fragment's span: measured zones keep the watch's own coordinates, altitude and geodesic distance. The fragment contributes an *increment* between the anchors bounding it, never an absolute position.
- **AD-17** applies only to what the estimated zones draw from the track, which after Story 1.12 is already true.
- Detection is the service's: anchors clustering in one contiguous window, with a track materially shorter than the activity's own measured distance. Detection raises a `blocking` warning and the repair is not applied silently — that gate is Story 5.2's, through the same mechanism `TRACK_LONGER_THAN_ACTIVITY` already uses (AD-22).
- Every threshold the detection and the attachment need lives in `AlignmentOptions` (AD-10). No default parameter values in public core signatures.
- A new diagnostic code needs its Ukrainian text in `web/src/i18n/uk.ts`, or `uk.test.ts` fails (AD-18).

## Technical Decisions

- **The core already does half of this.** Story 1.12 made a measured zone keep its own geometry and an estimated zone take an increment between its bounding anchors. A fragment is that machinery with the anchors no longer guaranteed to start at the track's beginning.
- **Three attachment shapes, one mechanism.** Tail (no true position after), head (none before), middle (both sides attach). The difference is only which end falls back to the activity's boundary.
- **The anchor search is the part that breaks.** `Aligner.Measure` projects every positioned record onto a monotone window of the track and keeps a projection within `AnchorToleranceMeters`. With a fragment most records have no projection at all, which today means `FEW_ANCHORS`, and the first position being far from the fragment means `TRACK_FAR_FROM_ORIGINAL`. Neither warning is true of a fragment.
- **`TRACK_FAR_FROM_ORIGINAL` compares the activity's first position to the whole track**, which is exactly wrong for a fragment that starts 45 km in.

## Cross-Story Dependencies

- Story 5.1 is the core and comes first; 5.2 is the gate over it; 5.3 is the interface over both.
- Story 5.4 needs Epic 3's map (Story 3.2) and nothing else from this epic beyond 5.1's core.
- Epic 1's parked stories (1.8 over-long track, 1.10, 1.11) do not block this epic. Story 5.2 reuses 1.8's confirmation mechanism, which exists in the core but has no API surface yet — the API's `trackLengthMode` is listed in deferred work.
