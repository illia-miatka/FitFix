---
title: 'Story 1.7 — One owner computes the summaries'
type: 'refactor'
created: '2026-11-21'
status: 'done'
route: 'dispatch'
review_loop_iteration: 1
baseline_commit: '343e2203d153ed37a20592d10f8e078f70b0a48b'
context: ['{project-root}/docs/implementation-artifacts/epic-1-context.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Every summary the repaired file states is computed inside `FitActivityWriter`: the session's and each lap's average and maximum heart rate, power, cadence and speed, the distances, the timer times. The preview epic will need the same numbers and will compute them again, so FitFix will show one average heart rate and Strava another, and the preview stops being worth trusting. Nothing computes elevation gain at all, and the report's pace still divides by elapsed time while the file it writes divides by moving time, so the two already disagree.

**Approach:** The core computes every summary once, for the activity and for each lap, and hands them over as data. The writer transcribes those values and performs no arithmetic over any series. Averages use moving time as their denominator, so a wait at a traffic light no longer dilutes them, and the report carries the finished numbers instead of computing paces on the way out.

</frozen-after-approval>

## Code Map

- `src/FitFix.Core/Fit/FitActivityWriter.cs:274-340` -- `ApplyAverages`, the one-pass accumulator over a record window: average and maximum heart rate and power, average cadence, and `distance / timer` as average speed. Every caller of it is a place the arithmetic has to leave.
- `src/FitFix.Core/Fit/FitActivityWriter.cs:228-270` -- `LapsOf`, `NearestRecord` and `BuildLap`: each lap's distance, elapsed, timer, boundary records and averages. The partition itself comes from the model and stays there; the numbers move to the core.
- `src/FitFix.Core/Fit/FitActivityWriter.cs:278-312` -- `BuildSession`: elapsed, timer, distance, `num_laps`, positions and the session averages.
- `src/FitFix.Core/Fit/FitActivityWriter.cs:33-40` -- `Write` computes `elapsed`, `timer` and `totalDistance` and decides whether the pause list is trustworthy. That decision is about the activity, not about writing.
- `src/FitFix.Core/RepairService.cs` -- where the repaired series, the alignment and the cleaned activity all exist together, so the summary belongs here.
- `src/FitFix.Core/RepairReport.cs` -- `MeasuredFraction`, `OriginalPaceSecondsPerKm` and `RepairedPaceSecondsPerKm` are computed properties over `ElapsedSeconds`. AD-14 forbids derived values in a contract, and Story 1.5 recorded that the pace must move onto moving time.
- `src/FitFix.Core/RepairService.cs` -- `ComputeSpeeds` derives each record's speed from elapsed seconds, so the three records either side of a pause carry a diluted speed and the maximum is wrong.
- `src/FitFix.Core/Models.cs` -- `OriginalActivity.LapPartition()`, `MovingSecondsFromStart`, `PausedSeconds` and `PauseSegment`: the time arithmetic the summary needs already exists.
- `src/FitFix.Cli/Program.cs:113-145` -- `PrintReport` reads the report's computed paces; it should read finished numbers.
- `tests/FitFix.Core.Tests/Fixtures/FitMessageInspector.cs` -- already exposes per-lap and session summaries including the sensor fields, added for Story 1.6. This is what compares the written file against the summary.

## Tasks & Acceptance

**Execution:**
- [x] `src/FitFix.Core/ActivitySummary.cs` -- new. The activity's summary and one per lap: distance, elapsed and moving seconds, average and maximum heart rate, power, speed and cadence, elevation gain and loss, the pace, the measured fraction, and each lap's boundary record indices. A value nobody can know is absent, not zero — elevation included. No computed properties — every value is settled when the summary is built (AD-14). -- AD-16: one owner, and the owner is the core.
- [x] `src/FitFix.Core/ActivitySummary.cs` -- the accumulation itself: one pass per window, sensor values taken only from records outside a pause, average speed as distance over moving seconds, elevation gain as the sum of the rises in the repaired altitude series. -- a wait at a traffic light must not dilute an average.
- [x] `src/FitFix.Core/RepairService.cs` -- build the summary from the repaired series, the lap partition and the pauses, and hand it back on the result. -- the place where everything needed already exists.
- [x] `src/FitFix.Core/RepairService.cs` -- derive each record's speed from moving seconds rather than the clock. -- the maximum speed and the records beside a pause are wrong today.
- [x] `src/FitFix.Core/Fit/FitActivityWriter.cs` -- transcribe the summary: delete `ApplyAverages`, the lap arithmetic and the elapsed/timer/distance computations, and write `total_ascent` from the summary. Unit and type conversion stays; arithmetic over a series does not. -- AD-16 names the writer explicitly.
- [x] `src/FitFix.Core/RepairReport.cs` -- replace the computed paces and measured fraction with values the summary settles, and let the shells read every summary number through the report rather than reaching past it. -- AD-14: a contract carries no derived values.
- [x] `src/FitFix.Cli/Program.cs` -- print from the summary. -- the one shell must agree with the file it just wrote.
- [x] `tests/FitFix.Core.Tests/SummaryTests.cs` -- new. Cover the I/O matrix below. -- the invariant is an equality between two artifacts, which only a test can hold.

**Acceptance Criteria:**
- Given a repaired activity, when the written file's `session` and every `lap` message are read back, then each value equals the corresponding field of the summary the core returned.
- Given the whole existing suite, when it runs, then the pinned distance, anchor count and measured boundary are unchanged; averages may move, because their denominator is now moving time, and every such change must be asserted deliberately.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| The file states the summary | the three-lap fixture, written and read back | every session and lap value equals the summary's field for it | N/A |
| The writer holds no arithmetic | the writer's source | no averaging, summing or dividing over a record series remains | N/A |
| Pauses do not dilute | the paused fixture | average heart rate and cadence equal the average over the moving records only, and differ from the all-records average | N/A |
| Speed follows movement | the paused fixture | no record beside a pause carries a diluted speed, and the maximum is the fastest moving second | N/A |
| Elevation gain | the synthetic route, which climbs and descends | the gain is the sum of the rises, not the net change, and the descent is not counted | N/A |
| Laps sum to the activity | the three-lap fixture | the laps' distances and moving seconds sum to the activity's, within a metre and a second | N/A |
| An activity with no sensors | records carrying no heart rate, power or cadence | those fields are absent from both the summary and the written file, rather than zero | N/A |
| A lap that is entirely a pause | the standing lap from Story 1.6 | zero distance, zero moving seconds, no average speed, and no sensor averages invented | N/A |
| The report agrees with the file | any repaired activity | the pace the report states equals the distance over the moving seconds the file states, the before-pace is the device's own distance over the same moving seconds, and the two paces differ | N/A |
| No record moves while standing | the paused fixture | every record inside a pause states a speed of zero, and none beside it states more than the fixture's own pace | N/A |
| The maximum speed is plausible | any repaired activity | the written maximum never exceeds what the sport's cap allows, so the forced finish cannot leak into it | N/A |
| A track with no altitude | a track whose points carry no elevation | the summary states no elevation gain and the file carries no `total_ascent`, rather than claiming a flat zero | N/A |
| Summaries refuse a series that is not the activity's | a repaired series shorter than the original's records, and an empty one | both are refused with a declared code | the code is in the registry |

## Verification

**Commands:**
- `dotnet build -c Release` -- expected: no errors, no new warnings
- `dotnet test -c Release` -- expected: all tests pass
- `grep -nE "Sum\(|Average\(|Max\(|/ *timer|/ *elapsed" src/FitFix.Core/Fit/FitActivityWriter.cs` -- expected: no arithmetic over a series remains

## Design Notes

Moving time is the denominator everywhere, and that is a deliberate change of numbers. Strava reports average heart rate over moving time, so matching it is the point of the story; the suite's existing averages will move, and each move has to be asserted on purpose rather than accepted because a test went red.

Elevation gain is the sum of the rises in the repaired altitude series, which comes from the reference track. Garmin applies a few metres of hysteresis before counting a rise; this story does not, because the track's altitude is already smooth and inventing a threshold here would be a tuning number outside `AlignmentOptions` (AD-10). If a real file shows the gain inflated by noise, the threshold belongs in the options with a measurement behind it.

The summary is data, not a view: no computed properties, every value settled when it is built. That is what lets the API DTO copy fields across without recomputing anything (AD-14), and what makes the equality test between the file and the summary meaningful.

## Implementation Notes

- `ActivitySummary.Of(records, original, alignment, options)` is the one owner: the activity's summary and one per lap, each carrying distance, elapsed and moving seconds, pace, average and maximum heart rate, power, speed and cadence, elevation gain and loss, and its boundary record indices. Nothing is a computed property, so the API DTO copies fields across without choosing a formula (AD-14).
- `FitActivityWriter` holds no arithmetic over a series: `ApplyAverages`, `LapsOf` and `WrittenLap` are gone, and `grep -nE "Sum\(|Average\(|Max\(|/ *timer|/ *elapsed"` over it returns nothing. The test that checks this now fails loudly when it cannot find the source instead of passing silently, and it also bans a hand-rolled accumulator.
- **A record a pause covers states a speed of zero.** The first version divided by moving seconds, which inflated rather than diluted the edge: records frozen at 2700.70 m reported up to 3.865 m/s, above the fixture's own 3.0, so Strava would have shown the athlete running while standing at a light. All 301 paused records are now asserted to be exactly zero.
- **The maximum speed skips records above the sport's cap**, because `EnforceMonotonicity` pins the last record to the track length and the final seconds ramped to 7.827 m/s on a 3 m/s run. An activity whose every second exceeds its cap therefore states no maximum at all; that is recorded in `deferred-work.md` rather than hidden.
- Elevation gain and loss are absent when no record of a stretch carried an altitude, instead of claiming a flat zero, and each lap's walk is seeded from the last altitude known at or before its first record so a boundary without one loses no climb.
- The disbelief of a pause list that leaves no moving second moved into `OriginalActivity.WithoutImpossiblePauses()` and is applied before cleaning, so the whole pipeline survives such a file; it used to die in `EnforceMonotonicity` with a bare exception. That refusal now carries `DISTANCE_SERIES_IMPLAUSIBLE`, and the summary's own two refusals carry codes as well.
- The report takes elapsed, moving, measured fraction and both paces from the summary, and its paused seconds from the same activity, so moving plus paused always equals elapsed. The before-pace is the device's own distance over the **original's** moving time, which is what the user saw in Strava.
- **Verified independently of the implementation report:** `dotnet build -c Release` 0 warnings; `dotnet test -c Release` 171 passed (156 at the story's baseline); the writer grep is clean. I re-ran the speed-cap mutation myself — accepting every record into the maximum — and one test fails where all 171 pass with the cap.

## Review Triage Log

One review pass, three layers (blind hunter 17, edge cases 11, verification gaps 5). Two layers mutated the shipped code to prove their claims, and the central ones were measured rather than argued.

| Finding | Verdict | Evidence and route |
|---|---|---|
| Records a pause covers state an inflated moving speed (blind, measured) | high | Measured: records 900–902 frozen at 2700.70 m reporting 2.274, 2.618, 2.801 and 3.865 m/s, the last above the fixture's nominal 3.0. The new test's own windows skipped exactly 901–903 and 1197–1199. Patched — every paused record is zero and all 301 are asserted. |
| The new `max_speed` states 7.827 m/s on a 3 m/s run, and its guard is a tautology (blind, edge) | high | Real: `EnforceMonotonicity` pins the last record, so the final seconds ramp; the guard compared the maximum of a series with the maximum of the same series. Patched — records above the sport cap are skipped, pinned by a test with a 100 m/s spike. |
| The before-pace is asserted by nothing (verification, measured) | high | Changing it to the repaired distance kept all 165 tests green: the CLI could have printed `5:30/км → 5:30/км`. Patched — the formula and the inequality are both asserted. |
| Both refusals in `Of` can be deleted with the suite green, and they carry no code (verification, measured) | high | Measured: deleting them kept 165 green, and the writer explicitly delegates the check to `Of`. Patched — `SUMMARY_SERIES_MISMATCH` and `SUMMARY_WITHOUT_RECORDS`, both pinned. |
| The paces and the measured fraction stayed in `RepairService`, not the summary (edge) | medium | Correct, and it leaves the preview epic choosing a denominator — the second owner this story exists to remove. Patched — the summary carries them. |
| `ElapsedSeconds` still has two owners, and the equality test skips that field (blind) | medium | Correct and ironic for this story: the file wrote the summary's span while the report stated the original's. Patched and asserted against the file. |
| Elevation gain is non-nullable, so a track with no altitude claims a flat zero (blind, edge) | medium | Real, and against the summary's own rule that absence and zero are different claims. Patched — gain and loss are absent when no altitude exists, with a test. |
| A lap boundary without an altitude loses the rise across it (edge) | medium | Real: the laps' gains would stop summing to the activity's. Patched — each lap seeds from the last known altitude. |
| The report's paused seconds ignore the disbelief, so moving plus paused can exceed elapsed (blind, edge) | medium | Real: the CLI would print `Час у руху: 40:00 (пауз: 40:00)` under `Загальний час: 40:00`. Patched — both come from the same activity. |
| The disbelief branch is unreachable, and the reachable failure throws a bare exception (all three) | medium | Correct: `Repair` died in `EnforceMonotonicity` with English prose and no code. Patched — the disbelief moved upstream of cleaning and the refusal carries `DISTANCE_SERIES_IMPLAUSIBLE`. |
| `Measured` treats anything at or above 65535 as the sentinel, including distance and time (all three) | medium | Real: a correct 70 km or 18-hour file would read back as "field absent". Patched — each field is compared against its own invalid value. |
| The heart-rate and power sums are `int` (edge) | medium | Real: a `ushort` power over ~32 768 records overflows. Patched to `long`. |
| `Math.Clamp` throws where its comment promises it degrades (blind, edge) | medium | Real: min greater than max when the previous lap ended on the last record. Patched. |
| The writer-arithmetic guard passes silently when it cannot find the source (blind, verification) | medium | Real, and it is the story's central structural invariant — green while checking nothing in any run from published output. Patched: it fails loudly and the pattern now catches a hand-rolled accumulator too. |
| An empty lap list writes a file with no lap message; a summary indexing past the records throws (edge) | medium | Both real. Patched with `SUMMARY_WITHOUT_LAPS` and `SUMMARY_SERIES_MISMATCH`. |
| `WholeMetres` clamps to exactly the invalid sentinel (blind) | low | Correct: the file would say "no value" where it means enormous. Patched to one below. |
| `report.ElevationGainMeters` has no production reader; the CLI reaches past the report (blind, verification) | low | Correct, and it is the shape AD-14 and AD-16 exist to prevent. Patched — the CLI reads the report. |
| Maximum cadence and elevation loss are missing from an otherwise complete summary (blind) | low | Correct, and free in the same pass. Patched; both now reach the file, which is recorded in `deferred-work.md` as a change to what Strava displays. |
| Ukrainian doc comments left in a file the story rewrote (blind) | low | Correct; one of them sat directly above the new member and no longer described the type. Patched. |
| The spec's Code Map line anchors are wrong by 45–65 lines (blind) | low | Correct — they were written against the wrong revision. Fixed in the spec, which now names members rather than lines. |
| Three fields new to the written file were not declared by the spec (blind) | medium | Correct: the Execution list named only `total_ascent`, and the acceptance criteria allowed only for averages moving. Amended in the spec, and the two added this round are recorded for Ілля to overturn. |
| Elevation gain over altitudes that fall back to the watch's own (edge) | low | Rejected for this story: the fallback only fires where the track carries no elevation, and the repaired series then has nothing better to offer. Worth revisiting when a real file shows the gain inflated, where the hysteresis threshold would belong in `AlignmentOptions` with a measurement behind it. |
| `ActivitySummary.Of`'s count-mismatch guard is structurally unreachable (blind) | false | Unreachable from `RepairService`, which is the point: `Of` is public surface the API epic will call, and the guard is for that caller. It is now pinned by a test that calls it directly. |
