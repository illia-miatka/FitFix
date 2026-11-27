---
title: 'Story 5.1 — A track shorter than the activity repairs its own stretch'
type: 'feature'
created: '2026-11-23'
status: 'done'
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

## Review Triage Log

The first implementation passed 239 synthetic tests and was wrong on the files the story exists
for. On Ілля's own run, a tail fragment of the route returned **706 m**: the chain attached the
first three minutes to the fragment's far end -- his route is a lap, start and finish 51 m apart --
and thirty-one minutes of good GPS became one estimated zone with no track beneath it. A fragment
of a different stretch returned 5552 m with the fragment ignored entirely. The cause was one
mechanism: zone kind was derived from how far the anchor chain reached, which with a fragment says
nothing about where the GPS broke. It was rebuilt on what AD-27 actually describes -- the fragment
attaches by its own two endpoints, each matched against the whole activity, the pair ordered in
time, the fragment's length reachable in the span's moving seconds, and of the survivors the span
the activity could least account for itself. The same two files now return 6417.5 m and 6985.9 m,
with the span straddling the failure in the first.

The lesson is the fixtures, again. Every synthetic course is open and tidy; a lap whose fragment
ends near its own start cannot exist in them, so nothing could fail. `CreateTwoLaps` and the two
real rows in `PrivateFixtureTests` exist for that reason.

The verification-gap layer ran against the first version and found five things, all folded into the
rebuild. Two were regressions that would have shipped: a record with no own position inside a
measured zone was drawn at the span's boundary -- 1752 m from where the same run repairs with a
whole track, with distance frozen for a minute, and monotone, so nothing noticed -- and an activity
whose watch froze at second 0 stopped reporting `TRACK_FAR_FROM_ORIGINAL` at all, so a route from
129 km away repaired silently past a gate that withholds the download (AD-22). It is a refusal now.
The other three were unpinned mechanisms: the smoothing clip's forward half could be deleted with
the suite green, the 200 m attachment threshold was pinned only by private files CI never has, and
a whole-track number in `TrackLengthTests` had moved with the fixture adapted around it rather than
explained. The threshold is now synthetic and 100 m, both clip halves fail a named test, and
`TrackLengthTests` is back to HEAD bit-for-bit.

One number moved that the report said had not, and finding it took a bisection. The real
whole-route pair repairs to **6324.2330 m** where it gave 6295.08 m before, with every zone, anchor
and warning identical. Not the interpolation clamp and not the bridging -- both were ruled out by
measurement. It is `Accumulate`: the step into the first measured second after an estimated zone
used to be the distance from the previous record's *raw* position, even though that record sat in
an estimated zone and was emitted at a point taken from the track. The step mixed a position we
never wrote with one we did. It now takes the track's increment there, like the rest of that zone.
Restoring the old lookup returns 6295.08 m exactly, which is how the cause was confirmed. The new
figure is the coherent one -- 29 m over four seams -- and it is pinned in `PrivateFixtureTests` to
half a metre, because a 3 % window is what let it through unnoticed in the first place.

Verified independently: 246 core and 117 API tests, 85 on the frontend; the real pair at the
baseline commit measured in a separate worktree for the comparison above; both fragments and the
whole route run through the CLI.

For Ілля: a fragment of a stretch that was never broken is used all the same -- `mid.gpx` covers
route metres 2508-3486, which he ran cleanly at 828-1148 s, and those 320 seconds are redrawn from
his fragment. Refusing would hand nothing back to somebody whose fragment matches their own route
to within 2 m, so it is used and scored last among candidates. Story 5.2 is where the service says
which stretch it means and asks before doing it.

Parked and untouched: the greedy whole-track chain (DP/LIS), which is why a middle fragment with a
dropout longer than the search window attaches only through the fragment path.
