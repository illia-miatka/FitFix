---
title: 'Gates module — split the three gates out of Result.tsx'
type: 'refactor'
created: '2026-11-28'
status: 'done'
baseline_commit: '8bd07c9'
route: 'oneshot'
review_loop_iteration: 0
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

Epic 3 retrospective, finding 1 and action item 2. `Result.tsx` is 759 lines and holds three gates (`FragmentGate`, `LongTrackGate`, `FarTrackGate`) plus the redrawn-stretch block. Two of them copy the same pending / failure handling by hand. Move the gates and `RedrawnStretch` to `web/src/components/Gates.tsx`, share the pending / failure handling in one hook, and add the test the 3.5 review deferred: while one gate's repeat request is in flight, answering the other gate sends no request and says so. Pure refactor: no behaviour, text or markup change, and every existing test passes unchanged.

</frozen-after-approval>

## Implementation Notes

- `Gates.tsx` now holds `FragmentGate`, `LongTrackGate`, `FarTrackGate` and `RedrawnStretch` (moved verbatim apart from the shared parts) and two private helpers: `useAnswer` (pending / failure / the double-click guard) and `Failure` (the live region). `Result.tsx` is 494 lines, down from 759; `FarTrackGate` keeps its own local state because it asks nobody anything.
- Every existing test passes unchanged. Added two App tests in "two questions at once": with a report that is both an over-long track and a fragment, answering one gate while the other's repeat is in flight sends no request and shows the refused gate's own failure sentence. Removing either `running` guard in `App.tsx` fails exactly the matching test.
- Review: skipped the reviewer layers. The change is a move plus one extracted hook, the whole suite is the proof (316 tests, typecheck, build), and both guards were mutation-checked.
