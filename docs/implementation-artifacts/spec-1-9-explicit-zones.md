---
title: 'Story 1.9 — Zones become explicit intervals'
type: 'feature'
created: '2026-11-22'
status: 'done'
route: 'dispatch'
review_loop_iteration: 0
baseline_commit: 'cb10908ec797ffad25448a586b5e26c7f07e756f'
context: ['{project-root}/docs/implementation-artifacts/epic-2-context.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The core reports trust as a single number — `MeasuredUntilSeconds`, the time of the last anchor — which says the activity was measured up to a point and estimated after it. Real GPS does not fail that way: it drops out, comes back, drops again. A run whose signal returned for the last kilometre is reported as estimated from the first dropout onward, and a user looking at the preview cannot tell which stretches to trust.

**Approach:** The report carries the zones themselves: a list of intervals, each measured or estimated, covering the activity exactly once. A stretch is measured where anchors established the position and estimated where the distance between them had to be interpolated across a gap longer than the threshold. The share of the activity that was measured becomes the sum of the measured zones rather than a prefix, and an activity with no position at all is one estimated zone.

</frozen-after-approval>

## Code Map

- `src/FitFix.Core/Alignment.cs` -- `MeasuredUntilSeconds` is the prefix model this story replaces; `Anchors` is what the zones are derived from.
- `src/FitFix.Core/Aligner.cs:91,133,173` -- where it is set and read, including the decision whether to append or overwrite the finishing anchor.
- `src/FitFix.Core/RepairService.cs:118` -- `Smooth` widens its window outside the measured stretch, so it needs the zones rather than one boundary.
- `src/FitFix.Core/RepairService.cs:173-174`, `src/FitFix.Core/RepairReport.cs:58` -- the report's own prefix members.
- `src/FitFix.Core/ActivitySummary.cs:144,248` -- `MeasuredFraction`, computed from the prefix today.
- `src/FitFix.Core/AlignmentOptions.cs` -- every threshold lives here (AD-10); the gap above which an interval between anchors counts as estimated belongs here too.
- `src/FitFix.Cli/Program.cs` -- prints the measured zone as "up to X"; it should say how much was measured and in how many stretches.
- `tests/FitFix.Core.Tests/Fixtures/SyntheticRun.cs` -- `Create(withGpsFailure)` loses GPS once and never recovers. A run that loses it, recovers and loses it again is new.

## Tasks & Acceptance

**Execution:**
- [x] `src/FitFix.Core/Alignment.cs`, `src/FitFix.Core/Aligner.cs` -- derive the zones from the anchors: measured where consecutive anchors are closer in time than the threshold, estimated where they are not, plus an estimated head before the first anchor and tail after the last. The intervals cover the activity once, in order, with no gap or overlap. -- the model the preview needs, and the one FR-8 describes.
- [x] `src/FitFix.Core/AlignmentOptions.cs` -- add that threshold. -- AD-10.
- [x] `src/FitFix.Core/RepairService.cs`, `src/FitFix.Core/RepairReport.cs`, `src/FitFix.Core/ActivitySummary.cs` -- the report carries the zones; the measured share is the sum of the measured ones over the elapsed time. Remove the prefix members once nothing reads them. -- one model of trust, not two.
- [x] `src/FitFix.Core/RepairService.cs` -- smoothing widens its window inside an estimated zone rather than after a single boundary. -- AD-21, and the reason the boundary existed at all.
- [x] `src/FitFix.Cli/Program.cs` -- print the measured share and the number of stretches. -- a single "up to X" is the claim this story disproves.
- [x] `tests/FitFix.Core.Tests/Fixtures/SyntheticRun.cs` -- a run that loses GPS, recovers and loses it again. -- more than two zones cannot be shown without it.
- [x] `tests/FitFix.Core.Tests/ZoneTests.cs` -- new. Cover the I/O matrix below. -- the invariant is a tiling, which only a test can check.

**Acceptance Criteria:**
- Given an activity whose GPS dropped, returned and dropped again, when it is repaired, then the report carries more than two zones, alternating in kind, tiling the activity exactly once.
- Given the existing suite, when it runs, then every pinned number is unchanged except the measured share, whose new value must be asserted deliberately.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| GPS returns | a run losing GPS, recovering, losing it again | more than two zones, alternating, tiling the activity with no gap or overlap | N/A |
| No position at all | an activity with no GPS | one estimated zone covering the whole duration | N/A |
| Measured throughout | the honest synthetic run | the zones are measured except where the known stall is | N/A |
| The threshold decides | the same activity with a gap threshold below and above its anchor spacing | the same stretch is measured in one case and estimated in the other | N/A |
| The share is a sum | the recovering run | the measured share equals the measured zones' seconds over the elapsed time, and differs from what a prefix would give | N/A |
| Nothing moved behind the zones | any repaired activity | the distances and the written file are unchanged by this story, bar the smoothing the zones now govern | N/A |

## Verification

**Commands:**
- `dotnet test -c Release` -- expected: all tests pass; only the measured share moves, and deliberately

## Review Triage Log

One review layer, which mutated the code and watched the suite.

Patched by me: nothing observed the per-record smoothing width — deleting the per-zone choice, or classifying every record by the first zone, left all 217 tests green, so the widening inside a recovered dropout (the artefact this story removes) could have been dropped silently. A test now repairs the recovering fixture at two estimated-zone widths and asserts records near a dropout edge differ while records deep inside a measured stretch are bit-identical; reverting the width fails it. `EstimatedZoneGapSeconds` also joined the guarded thresholds: zero or negative would report a measured share of nothing about an activity the GPS covered.

Noted, not patched: `Aligner.Measure`'s zones are observed by nothing, because `EndingAt` derives them again over the full elapsed time — a caller of the public two-step gets a list that stops at the last anchor. Three defensive branches are unreachable through `Repair` (no anchors, the estimated head, the empty-zone fallback). `SummaryTests`' fraction check is near-tautological, which is acceptable only because `ZoneTests` pins the sum model independently.

Deferred: the private-fixture canary that the real 19.11 pair keeps a measured share above half never runs here, and under the sum model that share can only fall — worth one run by hand where the files are. The CLI's new trust line is unverified, as every CLI line is.
