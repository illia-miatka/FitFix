---
title: 'Story 5.1 — A track shorter than the activity repairs its own stretch'
type: 'feature'
created: '2026-11-23'
status: 'in-progress'
route: 'dispatch'
review_loop_iteration: 0
baseline_commit: '42863bdb313a6b80a33a0ad91a3e1098df00f092'
context: ['{project-root}/docs/implementation-artifacts/epic-5-context.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Two assumptions are hard-coded into the aligner: the chain opens with `(0, 0, 0)` — the activity starts where the track starts — and the finish is pinned to the end of the chosen track length. Both are true only when the track covers the whole route. Give the service the five kilometres that broke out of fifty and it anchors the start of a 50 km run to the start of a 5 km fragment, which is nonsense dressed as a result.

**Approach:** The track becomes a *stretch* that attaches to the activity by anchors at its own ends (AD-27). The head is pinned to the track's start only when the activity's own first position actually attaches there; the finish is pinned to the chosen length only when no measured position survives after the last anchor. Everywhere the fragment does not reach, the watch's own geometry and its own geodesic distance carry the activity, exactly as Story 1.12 made them. A track that attaches at neither end is a refusal with a code, not a guess.

</frozen-after-approval>

## Code Map

- `src/FitFix.Core/Aligner.cs` -- `Measure` seeds `anchors` with `new(0, 0, 0)` (line ~57) and `EndingAt` either appends or replaces a finish at `chosenTrackLength` (line ~135). These two are what AD-27 removes for a fragment and keeps for a whole track.
- `src/FitFix.Core/Aligner.cs` -- `Measure` raises `TRACK_FAR_FROM_ORIGINAL` from `MinDeviationToWholeTrack(positioned[0])`: the activity's *first* position against the whole track. For a fragment starting 45 km in, that warning is false by construction and has to be judged against the fragment's attachment instead.
- `src/FitFix.Core/Aligner.cs` -- `Zones` makes `[0, anchors[0].Time]` and `[anchors[^1].Time, until]` estimated unconditionally. With a fragment, the stretch before it is where the watch was telling the truth, so the head and tail are measured whenever the original has its own positions across them.
- `src/FitFix.Core/RepairService.cs` -- `Repair` calls `Measure` then `EndingAt` (lines ~98-100), derives `MeasuredPositions` from `alignment.Zones`, and accrues distance per zone (Story 1.12). None of that changes: a fragment is this machinery with different anchors.
- `src/FitFix.Core/RepairService.cs` -- `ChooseTrackLength` (line ~269) bounds what the estimated zones draw from the track (AD-17). For a fragment the same arithmetic applies to the fragment's own length.
- `src/FitFix.Core/ReferenceTrack.cs` -- `Project(point, fromSegment, toSegment)`, `Cumulative`, `Length`, `MinDeviationToWholeTrack`. A fragment needs no new track capability; what changes is which projections are accepted as the chain's ends.
- `src/FitFix.Core/AlignmentOptions.cs` -- every threshold lives here (AD-10), and the public core signatures carry no default values; `AlignmentOptionsTests` catches a returning default by reflection.
- `src/FitFix.Core/Diagnostics/DiagnosticCodes.cs` -- the registry. A new code needs its Ukrainian text in `web/src/i18n/uk.ts` or `uk.test.ts` fails (AD-18).
- `tests/FitFix.Core.Tests/Fixtures/SyntheticRun.cs` -- the fixture builder: a course, a broken stretch, bounded GPS drift. A fragment fixture is a slice of the same course.
- Do not change: `GpsCleaner`, the FIT writer, the API, the frontend. The confirmation gate and anything a person sees belong to Stories 5.2 and 5.3.

## Tasks & Acceptance

**Execution:**
- [x] `src/FitFix.Core/Alignment.cs` -- carry what the anchors established about the track's reach: whether the chain attaches at the track's start, at its end, and the seconds the track's own span covers. -- the repair and the later stories read this rather than re-deriving it.
- [x] `src/FitFix.Core/Aligner.cs` -- `Measure` opens the chain with the track's start only when the activity's own first positions attach there; otherwise the chain opens with the first accepted projection. -- AD-27: the first anchor is no longer the track's beginning by assumption.
- [x] `src/FitFix.Core/Aligner.cs` -- `EndingAt` pins the finish to the chosen length only when no measured position survives after the last anchor. When the activity's own GPS continues past the fragment, the finish is the activity's own last position and the implausibility guard does not fire. -- AD-27 and AD-8.
- [x] `src/FitFix.Core/Aligner.cs` -- `TRACK_FAR_FROM_ORIGINAL` is judged against where the track attaches, not against the activity's first position. A fragment that attaches at neither end raises a refusal carrying a new code instead. -- a warning that is false for every fragment teaches people to ignore warnings.
- [x] `src/FitFix.Core/Aligner.cs` -- `Zones`: the stretch before the first anchor and after the last is measured when the original has its own positions across it, and estimated when it does not. -- AD-26: a zone's kind says where the geometry comes from, and outside a fragment it comes from the watch.
- [x] `src/FitFix.Core/AlignmentOptions.cs` -- the threshold deciding that an anchor sits at the track's own start or end. -- AD-10.
- [x] `src/FitFix.Core/Diagnostics/DiagnosticCodes.cs` and `web/src/i18n/uk.ts` -- the refusal code for a track that attaches nowhere, with its Ukrainian text. -- AD-18, and `uk.test.ts` holds the two equal.
- [x] `tests/FitFix.Core.Tests/` -- a fragment suite covering the matrix below, with fixtures built from a slice of the synthetic course. Every existing expectation for a whole track must still hold unchanged. -- the regression is the risk: this story edits the chain every repair is built on.

**Acceptance Criteria:**
- Given an activity whose GPS failed over one stretch and a track covering only that stretch, when it is repaired, then the fragment is used between the anchors bounding the failure and the rest of the activity keeps the watch's own coordinates and distance.
- Given a fragment with no true position after it, when it is repaired, then the activity finishes at the fragment's end; given one with no true position before it, then the activity starts at the fragment's start.
- Given a track covering the whole route, when it is repaired, then every number is what it is today.
- Given a track that attaches at neither end, when it is repaired, then the repair is refused with its own code.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| A tail fragment | GPS good for 45 km, dead for the last 5, track covers the last 5 | the first 45 km are the watch's own; the fragment fills the rest; distance is the sum | N/A |
| A head fragment | GPS dead from the start, good afterwards, track covers the start | the activity starts at the fragment's start and the rest is the watch's own | N/A |
| A middle fragment | GPS good, dead over one stretch, good again; track covers that stretch | only that stretch is redrawn, and the seam jumps at neither end | N/A |
| The whole track | the Story 1.12 fixtures | every pinned number unchanged | N/A |
| Attached nowhere | a track from another route entirely | a refusal carrying its own code | never a silent repair |
| Zones outside the fragment | a tail fragment | the stretch before it is a measured zone, not an estimated one | N/A |
| No GPS at all | an activity with no position and a whole track | as today: one estimated zone over everything | N/A |
| A fragment longer than the gap | the fragment overshoots the failure's end | the AD-17 choice bounds what the estimated zone draws | the over-long warning still fires |
| Monotone still | any of the above | distance never decreases, and no second gains more than the sport's cap | the implausible-step guard still fires |
| The finish is not invented | a tail fragment that ends before the activity does | the last seconds are the watch's own, with no pinned jump | N/A |

## Verification

**Commands:**
- `dotnet test -c Release` -- expected: all pass, the existing whole-track expectations unchanged
- `npm --prefix web run test` -- expected: passes, including the dictionary equality for the new code

## Design Notes

The two pins are the whole story. A whole track is the case where both happen to be right — the activity does start where the track starts and end where it ends — so the fix is not to remove them but to make each conditional on the evidence that justified it: the first positions attaching at the track's start, and no measured position surviving after the last anchor. Written that way, a whole track keeps today's behaviour by construction rather than by a flag, and the three fragment shapes are the three ways that evidence can be absent.

Zones are where the subtlety hides. Today the head and tail of the chain are estimated because, with a whole track, no anchor before the first means no fix before the first. With a fragment it means the opposite: the watch was right there, and that is precisely why no anchor attached. The kind has to follow the original's own positions, not the anchors' reach.

Detection is deliberately structural here, not a mode: the anchors either attach at the track's ends or they do not. Story 5.2 adds the user-facing gate that asks before a fragment is used, and that is where a mode belongs.
