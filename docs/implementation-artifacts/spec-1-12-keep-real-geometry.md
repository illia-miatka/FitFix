---
title: 'Story 1.12 — The original keeps its own coordinates where they are right'
type: 'feature'
created: '2026-11-23'
status: 'done'
route: 'dispatch'
review_loop_iteration: 0
baseline_commit: 'd1fed9414148e161894363769c155b5cc175e13b'
context: ['{project-root}/docs/implementation-artifacts/epic-1-context.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Every repaired record takes its position from the reference track, so the whole route is redrawn — including the stretches where the watch was telling the truth. On Ілля's own file that is nearly half the run: the zones report measured from 179 s to 1698 s, and all of it was replaced by a polyline drawn in an editor, which cuts corners and does not know which side of the path he ran on.

**Approach:** The track supplies geometry only inside estimated zones. Inside measured zones the records keep their own coordinates and altitude, as the watch recorded them after impossible points were discarded. An estimated zone takes from the track the *distance between the anchors that bound it*, as an increment rather than an absolute position, so the two sources meet without a jump. The repaired distance is therefore a sum and no longer equals the track's length — both numbers are reported, and the inflation that real GPS jitter adds on measured stretches is accepted as the price of real data (AD-26).

</frozen-after-approval>

## Code Map

- `src/FitFix.Core/RepairService.cs` -- `Repair` builds every `RepairedRecord` with `track.At(distances[i])`, which is the line this story narrows to estimated zones. `InterpolateDistances`, `Smooth` and `EnforceMonotonicity` all operate on one distance series.
- `src/FitFix.Core/RepairService.cs` -- `EnforceMonotonicity(distances, chosenTrackLength)` pins the last record to the chosen length and refuses a shortfall. The pinning is what AD-26 removes; the monotonicity is what it keeps.
- `src/FitFix.Core/Alignment.cs` -- `Zones` (Story 1.9) is the boundary between the two sources, and `Anchors` carry each one's `TrackDistance`, which is how an estimated zone's increment is read.
- `src/FitFix.Core/Geo.cs` -- `Distance` between two positions, which is how a measured zone's own distance accrues.
- `src/FitFix.Core/ReferenceTrack.cs` -- `At(distance)` and `Length`; the track is still the source inside estimated zones, including its altitude and the offset aligning it to the original's start.
- `src/FitFix.Core/RepairReport.cs`, `src/FitFix.Core/ActivitySummary.cs` -- the report already carries both `TrackLengthMeters` and `UsedTrackLengthMeters`; the repaired distance now differs from both legitimately, and every summary reads the same series.
- `src/FitFix.Core/RepairService.cs` -- the AD-17 choice (Story 1.8) bounds the whole activity today; under AD-26 it bounds only what the estimated zones draw from the track.
- `tests/FitFix.Core.Tests/` -- `ExpectedRepairedDistance`, `ExpectedAnchorCount` and every assertion that the series ends on the track length will move. Each new value has to be derived and asserted on purpose.

## Tasks & Acceptance

**Execution:**
- [x] `src/FitFix.Core/RepairService.cs` -- build the distance series per zone: inside a measured zone each step is the geodesic distance between the original's own consecutive positions; inside an estimated zone the series spans the increment the track holds between the anchors bounding that zone. -- AD-26, and the seam meets by construction because the increment is added to what the measured side accrued.
- [x] `src/FitFix.Core/RepairService.cs` -- a record in a measured zone keeps the original's own latitude, longitude and altitude; a record in an estimated zone takes them from the track as before. -- the point of the story.
- [x] `src/FitFix.Core/RepairService.cs` -- monotonicity is still verified after smoothing, and the last record is no longer pinned to the chosen track length. Smoothing applies to estimated zones only: a measured position is data, not an estimate. -- AD-8 keeps what survives of it.
- [x] `src/FitFix.Core/RepairService.cs` -- the AD-17 plausibility choice now bounds the metres the estimated zones draw from the track, not the activity's whole distance. -- Story 1.8's warning still means what it said.
- [x] `src/FitFix.Core/RepairReport.cs` -- say plainly that the repaired distance is a sum and need not equal either track length. -- a reader of the report should not take the difference for a bug.
- [x] `tests/FitFix.Core.Tests/GeometryTests.cs` -- new. Cover the I/O matrix below. -- the invariant is which source each record came from.
- [x] `tests/FitFix.Core.Tests/` -- re-derive every pinned number this changes, asserting each new value deliberately. A test that merely goes green again proves nothing. -- the suite's numbers are the record of what the algorithm does.

**Acceptance Criteria:**
- Given an activity with both kinds of zone, when it is repaired, then every record inside a measured zone carries the original's own coordinates, and every record inside an estimated zone carries the track's.
- Given an activity whose GPS worked throughout, when it is repaired, then its geometry equals the original's and its distance equals the original's own within the jitter, rather than the track's length.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Measured stays real | the recovering fixture | records inside measured zones equal the original's own positions to the millimetre | N/A |
| Broken is redrawn | the same fixture | records inside estimated zones lie on the track | N/A |
| The seam does not jump | the boundary between two zones | the distance is continuous across it, and no single second gains more than the sport cap allows | N/A |
| GPS worked throughout | an activity with no estimated zone | the geometry is the original's and the distance is its own, not the track's length | N/A |
| No GPS at all | an activity with no position | the whole result comes from the track, as before | N/A |
| Monotone still | any repaired activity | distance never decreases | the implausible-step guard still fires |
| Both numbers are reported | an activity whose sum differs from the track | the report states the track length and the repaired distance, and they differ | N/A |
| Smoothing leaves data alone | a measured zone | its positions are not smoothed; an estimated zone's series still is | N/A |
| Altitude follows its source | a measured zone and an estimated one | the first keeps the watch's altitude, the second takes the track's with its offset | N/A |
| Pauses still hold | the paused fixture | distance does not grow inside a pause, in either kind of zone | N/A |

## Verification

**Commands:**
- `dotnet test -c Release` -- expected: all tests pass, with every changed expectation re-derived rather than relaxed
- `npm --prefix web run test` -- expected: unchanged

## Design Notes

The increment is what makes the seam work. If an estimated zone took absolute positions along the track, the distance at its start would be the track's cumulative figure while the measured side had accrued its own — and the difference would appear as a jump of tens or hundreds of metres in one second. Taking only the difference between the bounding anchors' track distances keeps both sources' own totals intact and meets in the middle.

Smoothing stops at the measured zones for the same reason the whole story exists. A smoothed real position is an estimate wearing data's clothes; the jitter in it is what the watch saw, and Ілля has accepted its cost explicitly. Inside estimated zones the series is interpolation already, and smoothing it is what keeps the kinks out.

## Review Triage Log

No separate review layer ran. The implementer mutation-checked the two load-bearing claims
instead -- taking the track's position inside a measured zone fails the geometry tests, and
re-pinning the last record to the chosen length fails the distance ones -- and every pinned
number was re-derived rather than relaxed: `ExpectedRepairedDistance` 6499.5036 -> 6539.4331 m
(3925.21 measured along the original's own positions plus 2614.22 taken from the track),
`ExpectedAnchorCount` 1129 -> 1297, and a new `ExpectedMeasuredZoneSurplus` of 39.9295 m.

Two things were wrong beyond the story and are now fixed. The synthetic fixture's GPS error was
white noise, which inflated a measured stretch by 39 % and made every assertion about jitter
meaningless; it is bounded drift now (a 3 m radius wandering at 0.75 m/s, 1.03 % inflation),
which is what a watch under trees actually does. And AD-19 still said `total_distance` equals
`UsedTrackLength` within 0.5 %, which AD-26 overrides; the spine now says the distance is the
sum of the series' steps and need not equal either track length.

Verified on real data rather than asserted blind. `PrivateFixtureTests` became a theory over both
of Ілля's pairs, and the 23.11.2026 pair is present on this machine, so the row runs: the repair
returns 6295.08 m against a 6347.6 m chosen length -- 0.83 % **under** it, because a measured
stretch follows the watch rather than a polyline that goes the long way round a corner. The
plausibility window is therefore 3 % each way, not inflation only, and tightening it to 0.5 %
fails the row, which is how the assertion was shown to bite. The whole suite: 338 tests
(231 core, 107 API), frontend unchanged at 59.

For Ілля: the headline number moved the way the story intended. The same pair read 6.35 km before
(the track's length, by construction) and reads 6.30 km now, with 79 % of the run carrying his
watch's own geometry.

Not in this story: the greedy anchor chain stays greedy (the DP choice is still open in CLAUDE.md),
and the smoothing still stops at pause boundaries rather than anchor boundaries (AD-21).
