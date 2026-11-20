---
title: 'Story 1.6 — Original laps survive the repair'
type: 'feature'
created: '2026-11-20'
status: 'done'
route: 'dispatch'
review_loop_iteration: 1
baseline_commit: 'a6eb28bf612b1f8f3be9cad48c0380ef083230fd'
context: ['{project-root}/docs/implementation-artifacts/epic-1-context.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The reader ignores `lap` messages and the writer always emits exactly one lap spanning the whole activity, so an interval session comes back from the repair as a single undifferentiated effort. Everything the athlete structured — eight by four hundred, a warm-up, a cool-down — is lost, and Strava has nothing to lay out.

**Approach:** Read the original's lap boundaries into the activity as intervals, and have the writer emit one lap message per original lap, keeping each boundary where the original put it in time and recomputing each lap's distance from the repaired series. The laps partition the activity, so their distances sum to the total. An original with no laps still gets one lap covering everything.

</frozen-after-approval>

## Code Map

- `src/FitFix.Core/Fit/FitActivityReader.cs` -- reads `record`, `session` and (since Story 1.5) `event` messages. `LapMesgEvent` is not subscribed, which is why laps do not exist in the model. `LapMesg` carries `start_time` and `timestamp`, the latter being the lap's end.
- `src/FitFix.Core/Models.cs` -- `OriginalActivity` is a record carrying `Records` and, since Story 1.5, `Pauses` normalised in the init accessor. Laps belong beside them, normalised the same way.
- `src/FitFix.Core/Fit/FitActivityWriter.cs` -- `Write` emits `BuildLap(result, start, end, elapsed, timer, totalDistance)` once with `message_index` 0. `elapsed` and `timer` are computed for the whole activity at the top of `Write`; `ApplyAverages` averages over every repaired record.
- `src/FitFix.Core/Fit/FitActivityWriter.cs` -- `BuildSession` and `BuildActivity` keep their whole-activity numbers; this story must not change them.
- `src/FitFix.Core/RepairReport.cs` -- `RepairResult.Records` is the repaired series the lap distances are read from, and `CleanedOriginal` carries the pauses each lap's timer time needs.
- `src/FitFix.Core/Models.cs` -- `PausedSecondsBefore` and the merged pause list are what make a per-lap timer time possible without new arithmetic.
- `tests/FitFix.Core.Tests/Fixtures/SyntheticFitBuilder.cs` -- writes `file_id`, timer events, records, one lap, session and activity. It needs to write several laps so a fixture can express them.
- `tests/FitFix.Core.Tests/Fixtures/FitMessageInspector.cs` -- `InspectTiming` already returns one summary per lap, added in Story 1.5 for exactly this story.
- `tests/FitFix.Core.Tests/FitRoundTripTests.cs` -- asserts the written message order and the single lap today; those assertions move to the multi-lap shape.

## Tasks & Acceptance

**Execution:**
- [x] `src/FitFix.Core/Models.cs` -- add a lap interval type and `OriginalActivity.Laps`, ordered in the init accessor with backwards and zero-length intervals dropped. Clipping to the activity's span happens where the partition is derived, because a record initializer runs in source order and the span needs `Records`. -- AD-24: laps are part of the model, not something the writer invents.
- [x] `src/FitFix.Core/Fit/FitActivityReader.cs` -- read `lap` messages into those intervals, using `start_time` and `timestamp`. A lap whose end is missing is ignored rather than guessed at; a lap that has an end but no start keeps the end, which is a cut point in its own right. Laps belonging to another session in the same file are not this activity's. -- the file says where the laps were; nothing else may.
- [x] `src/FitFix.Core/Models.cs` -- derive the lap partition the writer needs: **both** the starts and the ends of the original's laps become cut points, so a lap that does not begin at the activity's start keeps its own beginning and the unlapped stretch before it becomes an interval of its own. The first interval starts at the activity's start and the last ends at its end, so the laps cover the activity exactly once. Cuts closer to the previous one than the gap between two records are dropped rather than written as a zero-length lap. An activity with no laps yields one lap covering everything, and one with no records yields none. -- the sum of the parts has to equal the whole, whatever the file's laps looked like.
- [x] `src/FitFix.Core/Fit/FitActivityWriter.cs` -- emit one lap message per interval, in time order, with `message_index` counting from zero: boundaries from the interval, distance as the repaired distance at its end minus at its start, elapsed as its own span, timer as that span minus the pauses inside it, and start and end positions from the repaired records at its boundaries. -- FR-6: the laps come back where the athlete put them, with distances the repair earned.
- [x] `src/FitFix.Core/Fit/FitActivityWriter.cs` -- each lap's averages are computed over that lap's own records, not the whole activity. -- a lap's average speed that describes the whole run is worse than none.
- [x] `tests/FitFix.Core.Tests/Fixtures/SyntheticFitBuilder.cs`, `tests/FitFix.Core.Tests/Fixtures/SyntheticRun.cs` -- write several `lap` messages, and offer a run with three laps of unequal length. The lap-free fixture stays byte-identical. -- nothing can be proven about laps until a fixture has them.
- [x] `tests/FitFix.Core.Tests/LapTests.cs` -- new. Cover the I/O matrix below. -- a lost lap is invisible unless a test counts them.

**Acceptance Criteria:**
- Given an original with no laps, when it is repaired, then the written file carries exactly one lap covering the activity and every number the suite already pins is unchanged.
- Given a repaired file, when its laps are read back, then their distances sum to the session's total distance within a metre and their spans tile the activity without gap or overlap.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Laps are read as intervals | a FIT with three `lap` messages | `Laps` holds three intervals in time order with the file's own boundaries | N/A |
| Laps survive the repair | the same activity repaired | the written file carries exactly three laps with those time boundaries | N/A |
| Lap distances are recomputed | the same file read back | each lap's distance is the repaired distance gained over its own span, and the three sum to the total within a metre | N/A |
| No laps at all | an original with no `lap` message | one lap covering the whole activity, with the activity's own distance | N/A |
| A lap over a pause | a lap containing a five-minute pause | its elapsed span is unchanged and its timer time is that span minus 300 s | N/A |
| Laps of unequal length | three laps of 300 s, 1200 s and the rest | each lap's distance matches what the repaired series gained in it, not the average | N/A |
| A lap boundary between records | a boundary falling between two record timestamps | the lap is cut at the nearest record, and no distance is counted twice or lost | N/A |
| Laps that overlap or repeat | two `lap` messages with the same end, or overlapping spans | the partition still tiles the activity exactly once | N/A |
| A lap beyond the activity | a lap whose end is after the last record | clipped to the activity's end | N/A |
| Averages are per lap | three laps run at different paces | each lap's average speed is its own distance over its own timer time | N/A |

## Spec Change Log

- **Trigger:** review pass 1. Cutting the partition at lap ends alone discards every lap *start*, so a lap running from 500 s to 1000 s inside a 2160 s activity came back as `[0, 1000]` and `[1000, 2160]` — neither of them the lap the athlete pressed, which this spec's own documentation promises nothing may move. Two layers raised it, one measuring that a four-lap input becomes three. Near-coincident cut points also wrote zero-length laps, an activity with no records threw instead of returning nothing, and the reader dropped a lap that had the end the partition actually needs because its start was missing.
  **Amended:** starts and ends are both cut points, unlapped stretches become intervals of their own, cuts too close to the previous one are dropped, an activity with no records yields no laps, and a lap with only an end is kept.
  **Known-bad state avoided:** a repaired file whose lap boundaries are not the ones in the original, with the arithmetic still adding up so nothing looks wrong.
  **KEEP:** the laps tile the activity exactly once; lap *i*'s end record is lap *i+1*'s start record so the distances telescope to the total; the pause fallback is decided once for the activity.

- **Trigger:** two of this spec's task wordings could not be implemented as written. Clipping the laps to the activity's span "in the init accessor" dereferences `Records` before it is assigned, since a record initializer runs in source order — `Pauses` has the same shape and also clips at the point of use. And deciding the "pauses swallow the whole activity" fallback per lap made the laps' timer times sum to elapsed time while the session stated a shorter figure, so the file contradicted itself.
  **Amended:** clipping is part of deriving the partition; the fallback is decided once for the activity and handed to the lap builder.
  **Known-bad state avoided:** a null dereference on construction, and a written file whose lap times do not add up to its own session.
  **KEEP:** the laps are a partition cut at the original's lap ends; lap *i*'s end record is lap *i+1*'s start record, so the distances telescope to the total.

## Verification

**Commands:**
- `dotnet build -c Release` -- expected: no errors, no new warnings
- `dotnet test -c Release` -- expected: all tests pass; no number pinned before this story moves

## Design Notes

The laps are a partition, not a list of spans copied across. A device may write laps that overlap, repeat an end, or stop short of the last record; a file whose lap distances do not add up to its total is worse than one with a single lap, because Strava will show the arithmetic. Taking the original's lap *starts and ends* as cut points keeps every boundary where the athlete pressed the button while guaranteeing the laps tile the activity exactly once.

**The limit of the nearest-record rule.** A lap boundary is written as the athlete made it, but its distance is read at the nearest record, so a boundary that falls between two records attributes the metres in that gap to one lap or the other. On a 1 Hz file the error is under a metre. Under smart recording — a point every four to ten seconds — a lap's distance can be out by what the athlete covered in one record gap, up to about thirty metres for a runner and considerably more for a cyclist, with the neighbouring lap out by the same amount in the other direction. The total is unaffected: the laps still tile the activity and still sum to it, because both laps read the shared boundary at the same record. Interpolating the distance at the boundary instant instead would narrow it, and that is a change to make when the lap distances move into the core with the rest of the summaries in Story 1.7, not here.

Per-lap averages stay in the writer for now, computed over each lap's own records. That deepens the known AD-16 violation — aggregates belong in the core — but moving them is Story 1.7's whole subject, and doing it here would mean writing the same code twice. The lap distances themselves come from the repaired series, which is the core's own output, so the writer transcribes rather than recomputes them.

## Implementation Notes

- `OriginalActivity.Laps` carries the file's own lap boundaries, ordered, with backwards intervals dropped; a lap that has only an end survives as a point, because the end is a cut point in its own right.
- `LapPartition()` is a method, not a property, so its sort-and-allocate cost is visible where it is called. It cuts at **starts and ends**, so a lap that begins mid-run keeps its own beginning and the unlapped stretch before it becomes an interval of its own. Every cut is resolved through `NearestRecordIndex` and kept only if its record advances past the previous boundary's, which is what makes near-coincident laps impossible without assuming a recording rate — it holds at 1 Hz and under smart recording alike.
- The writer starts each lap on the record the previous one ended on, so the distances telescope to the total by construction. A partition over 4096 laps is refused with `ACTIVITY_TOO_MANY_LAPS`: `message_index` has twelve usable bits.
- `lap_trigger` is now written — `SessionEnd` on the last lap, `Manual` on the others — because Strava reads it to tell a manual press from an auto-lap. **This is the one thing that changes the bytes of a repaired lap-free file** against the baseline; every number the suite pins is unchanged, which is what the acceptance criterion asks.
- The record two adjacent laps share counts in the earlier lap's sensor window only, matching how the distances telescope.
- `RepairReport.LapCount` reaches the CLI, which prints the lap count only when there is more than one.
- **Verified independently of the implementation report:** `dotnet build -c Release` 0 warnings; `dotnet test -c Release` 155 passed (122 at the story's baseline). I re-ran the central mutation myself — cutting at lap ends only — and two tests fail where all 155 pass with the fix.
- **Known limit, stated rather than hidden:** a lap boundary between records is measured at the nearest record. At 1 Hz that is under a metre; under smart recording a lap can be out by one record gap, with its neighbour out the same amount the other way and the total unaffected. Interpolating the distance at the exact instant is the narrower fix and belongs with Story 1.7, when the lap arithmetic moves into the core.
- Per-lap aggregates still live in the writer. AD-16 and AD-24 both put them in the core's `ActivitySummary`, which does not exist yet; Story 1.7 is the story that creates it, and the deferral is recorded in `deferred-work.md` rather than implied.

## Review Triage Log

One review pass, three layers (blind hunter 12, edge cases 7, verification gaps 7). The verification layer disproved two of its own candidates by mutation before filing, and the implementer's patch round was itself checked by twelve mutations, each caught by a test.

| Finding | Verdict | Evidence and route |
|---|---|---|
| The partition discards every lap start, so a lap that begins mid-run is moved (blind, edge) | high | Real and measured by the edge layer: a four-lap input came back as three, and a 500–1000 s lap became `[0,1000]` and `[1000,2160]`. The spec's own documentation promised nothing may move a press the athlete made. bad_spec — the rule now cuts at starts and ends; cutting at ends only fails two tests. |
| Per-lap sensor averages are pinned by nothing (verification, measured) | high | Reverting `ApplyAverages` to the whole-activity window kept all 140 tests green, and the fixture could not tell the difference: heart rate `150 + second % 10` averages the same on every lap, cadence is constant. Patched — the inspector reads the sensor fields and a new fixture varies them per lap. |
| Per-lap start and end positions are pinned by nothing (verification, measured) | high | Reverting them to `records[0]`/`records[^1]` also kept the suite green; on a loop every lap pin would land in one spot. Patched, with the positions chaining across laps and three distinct pins asserted. |
| The fixture never writes a device lap distance, so the story's central claim is untested (blind) | high | Correct: the comment described a `SetTotalDistance` that was absent. Patched — every fixture lap now carries an absurd 42 195 m and the written distance is asserted not to be it. |
| A lap with an end but no start is dropped, though the end is the cut point (blind, edge) | medium | Real, and the file this project exists for is exactly the kind that omits fields. Patched — a lap needs only its end. |
| Laps of another session in the same file cut this one (edge) | medium | Real. Patched — a lap whose end falls outside the record span is not this activity's. |
| Near-coincident cuts write a zero-length lap with 0 m and 0 m/s (blind, edge) | medium | Real: `Distinct()` collapses only exactly equal ends, and a manual press beside an auto-lap is 200 ms apart. Patched through record resolution rather than an assumed rate. |
| `LapPartition` throws on an activity with no records (edge) | medium | Real: `IndexOutOfRangeException` where the project's rule is a code or nothing. Patched — no records, no laps. |
| No ceiling on the lap count; `message_index` and `num_laps` are cast unchecked (blind, edge) | medium | Real: `message_index` has twelve usable bits. Patched — over 4096 laps is refused with `ACTIVITY_TOO_MANY_LAPS`, and a 40-lap file still writes without complaint. |
| AD-19's message order is claimed by a comment but checked by nothing (blind) | medium | Correct: the inspector returned six counts, so a file writing the session before the laps would pass. Patched — the message-type sequence is recorded and asserted. |
| A lap ending at or before the first record has no test, and weakening the lower bound stays green (verification) | medium | Correct. Patched, together with the reader's missing-boundary branch, which no fixture could previously produce. |
| The report and the CLI say nothing about laps (blind) | medium | Correct — the one thing the story is for was invisible to its only shell. Patched; the count prints only when there is more than one lap. |
| `ApplyAverages` copies each span and makes three LINQ passes; the shared record counts in both laps (blind) | low | Both correct. Patched — one indexed pass, and the shared record belongs to the earlier lap. |
| `LapPartition` recomputes on every read while `Laps` normalises once (blind) | low | Correct. Patched — it is a method now, so the cost is visible at the call site. |
| `lap_trigger` is never set (blind) | low | Correct: Strava reads it to tell a manual press from an auto-lap. Patched, and it is the one change that moves the bytes of a lap-free repaired file. |
| The round-trip theory restates the lap count beside the boundaries (blind) | low | Correct. Patched — the expectation comes from the partition. |
| The boundary-between-records case fakes sparseness on a 1 Hz fixture (blind) | medium | Correct, and the real limit was unmeasured. Patched with a smart-recording fixture that bounds the misattribution to one record gap, and the limit is stated in the Design Notes. |
| `Laps`'s documented ordering is observed by nothing (verification) | low | Correct — removing the `OrderBy` failed nothing. Patched with a test rather than dropping the claim. |
| Lap arithmetic sits in the writer, against AD-16 and AD-24 (blind) | medium | Correct, and AD-24 names `ActivitySummary` as the destination. Deferred to Story 1.7, which creates it — recorded in `deferred-work.md` so the deferral is explicit rather than implied by the spec's Design Notes. |
| A lap's written `avg_speed` disagrees slightly with its own `distance / elapsed` (verification) | low | Real and sub-second at 1 Hz: the span is the athlete's instant while the distance is read at the nearest record. Accepted consciously and stated in the Design Notes; the exact-instant interpolation is deferred with the lap arithmetic. |
| `NearestRecord`'s tie-break has no test (verification) | false | The layer filed it as not-a-gap and verified why: both adjacent laps resolve a shared instant identically, so flipping the tie-break cannot change the tiling. |
