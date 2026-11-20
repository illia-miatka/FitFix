---
title: 'Story 1.5 — Pauses do not inflate distance'
type: 'feature'
created: '2026-11-20'
status: 'done'
route: 'dispatch'
review_loop_iteration: 0
baseline_commit: '77c2f96f1108d855e34f46f8bc9296342ba65004'
context: ['{project-root}/docs/implementation-artifacts/epic-1-context.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Nothing in the core knows that an activity was ever paused. The reader ignores FIT's `event` messages, so a five-minute wait at a traffic light looks like five minutes of running: distance is spread evenly over elapsed time, the pause silently earns metres, and the pace the user sees in Strava is wrong in their favour. The written file repeats the error — `total_timer_time` is set equal to `total_elapsed_time` in the session, the lap and the activity.

**Approach:** Read the `timer stop` / `timer start` pairs into the activity as intervals, and distribute distance over *moving* time rather than elapsed time, so a record inside a pause carries exactly the distance of the record that opened it. Smoothing windows stop at a pause boundary instead of carrying movement into it, and monotonicity is still verified after smoothing. The written file's timer time becomes elapsed time minus the pauses.

</frozen-after-approval>

## Code Map

- `src/FitFix.Core/Fit/FitActivityReader.cs:17-45` -- subscribes to `RecordMesgEvent` and `SessionMesgEvent` only. `event` messages are never read, which is why pauses do not exist in the model. `FitDecoder.Decode` hands over the broadcaster, so adding `EventMesgEvent` is local to this method.
- `src/FitFix.Core/Models.cs:28-42` -- `OriginalActivity`, a record since Story 1.4. `ElapsedSeconds` and `SecondsFromStart` are the time arithmetic everything else uses.
- `src/FitFix.Core/RepairService.cs:116-134` -- `InterpolateDistances` spreads distance linearly over elapsed time between anchors. This is where a pause earns its metres today.
- `src/FitFix.Core/RepairService.cs:140-160` -- `Smooth`, a symmetric moving average whose window is bounded only by time (`halfWidth`), so it currently averages straight across a pause boundary.
- `src/FitFix.Core/RepairService.cs` -- `EnforceMonotonicity` runs after `Smooth` and must keep doing so (AD-8).
- `src/FitFix.Core/Fit/FitActivityWriter.cs:33` -- `elapsed` is computed once from the first and last record and passed to the lap, the session and the activity; `:121-122`, `:155-156` and `:190` set `total_timer_time` to that same value.
- `src/FitFix.Core/RepairReport.cs` -- `RepairResult.CleanedOriginal` already carries the activity, so the writer can read the pauses from there without new plumbing.
- `tests/FitFix.Core.Tests/Fixtures/SyntheticFitBuilder.cs` -- writes `file_id`, a timer start, records and a stop. A fixture with a pause needs it to write a stop/start pair in the middle.
- `tests/FitFix.Core.Tests/Fixtures/SyntheticRun.cs` -- `Create()` produces the pause-free run whose numbers the suite pins (6499.5036 m, 1129 anchors, 1295 s). Those must not move: with no pauses, moving time is elapsed time.
- `tests/FitFix.Core.Tests/Fixtures/FitMessageInspector.cs` -- the helper the round-trip tests read messages back with; the timer-time assertions belong there.

## Tasks & Acceptance

**Execution:**
- [x] `src/FitFix.Core/Models.cs` -- add a pause interval type and `OriginalActivity.Pauses`, empty by default, plus the moving-time arithmetic: the seconds of pause that fall before a given instant, and the activity's total paused seconds. -- one model of a pause, derived nowhere else (AD-7).
- [x] `src/FitFix.Core/Fit/FitActivityReader.cs` -- read `event` messages: a `timer` event of type `stop` or `stop_all` opens a pause, the next `start` closes it. Pauses come back ordered by time. A stop that nothing reopens is the end of the activity, not a pause. -- FIT says a pause with `event` pairs; nothing else may invent one.
- [x] `src/FitFix.Core/RepairService.cs` -- interpolate over moving time: convert record and anchor times to moving seconds and spread distance between anchors by that measure, so a record inside a pause keeps the distance of the record that opened it. -- FR-5: distance follows movement, not the clock.
- [x] `src/FitFix.Core/RepairService.cs` -- clip each smoothing window at the nearest pause boundary, then verify monotonicity as before. -- AD-21: smoothing must not carry movement into a pause.
- [x] `src/FitFix.Core/Fit/FitActivityWriter.cs` -- `total_timer_time` becomes elapsed minus the paused seconds for the lap, the session and the activity; `total_elapsed_time` stays as it is. -- FR-6.
- [x] `tests/FitFix.Core.Tests/Fixtures/SyntheticFitBuilder.cs`, `tests/FitFix.Core.Tests/Fixtures/SyntheticRun.cs` -- write a stop/start pair into the FIT, and offer a run with a five-minute pause mid-route. `Create()` stays byte-identical. -- the fixture has to be able to express a pause before anything can be proven about one.
- [x] `tests/FitFix.Core.Tests/PauseTests.cs` -- new. Cover the I/O matrix below. -- a pause earning metres is invisible without a test that measures the two ends of it.

**Acceptance Criteria:**
- Given the pause-free synthetic run, when it is repaired, then every number the suite already pins is unchanged, because moving time equals elapsed time.
- Given a repaired activity with pauses, when the written file is read back, then `total_timer_time` is `total_elapsed_time` minus the paused seconds in the lap, the session and the activity.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Pauses are read as intervals | a FIT with two `timer stop` / `timer start` pairs | `Pauses` holds both, ordered by time, with the right boundaries | N/A |
| The final stop is not a pause | a FIT whose last event is `stop_all` with nothing after it | no pause is recorded for it | N/A |
| Distance stands still | a five-minute pause mid-route | the distance at the end of the pause equals the distance at its start, to the metre | N/A |
| Timer time excludes pauses | the same activity, written and read back | `total_timer_time` equals elapsed minus 300 s in lap, session and activity | N/A |
| Smoothing respects the boundary | a pause with movement on both sides of it, inside one smoothing window | no record inside the pause gains distance from the movement outside it | N/A |
| Monotonicity survives | the same activity after smoothing | distance never decreases anywhere, and ends on the chosen track length | N/A |
| Nothing changes without pauses | the untouched synthetic run | the pinned distance, anchor count and measured boundary are identical | N/A |
| A pause at the very start | `timer stop` before the first record, `start` a minute in | no distance before the pause ends; the first record still sits at the track's start | N/A |
| An unpaired start | a `timer start` with no preceding stop | ignored, no pause invented | N/A |
| Overlapping or nested pairs | a stop, another stop, then one start | the result is one interval, not two overlapping ones | N/A |

## Verification

**Commands:**
- `dotnet build -c Release` -- expected: no errors, no new warnings
- `dotnet test -c Release` -- expected: all tests pass; no number pinned before this story moves

## Design Notes

Moving time is the whole mechanism. Rather than special-casing "is this record inside a pause", convert every instant to the seconds of movement that preceded it: inside a pause that number stops advancing, so linear interpolation over it holds the distance flat by construction, and no separate flag is stored (AD-7). With no pauses the conversion is the identity, which is what keeps the pinned numbers of the pause-free fixture exactly where they are — including the last digits of 6499.5036 m.

Smoothing is clipped, not disabled. A window that would reach across a pause boundary stops at it, so the records just before a pause still get smoothed against their own side. AD-21 also asks for clipping at anchors; that is not this story — it would move the pinned numbers of every existing test and belongs with the anchor rework noted in CLAUDE.md.

The written file gets no new `event` messages. AD-19 pins the message order, and Story 1.10 owns that structure; this story only corrects `total_timer_time`, which is what makes the pace in Strava honest.

## Implementation Notes

- `PauseInterval(Start, End)` and `OriginalActivity.Pauses` are the one model of a pause. The intervals are ordered, merged and stripped of empty ones **once**, in the `Pauses` init accessor, so every consumer reads the same list: `PausedSecondsBefore`, `PausedSeconds`, `MovingSecondsFromStart`, `IsPaused` and `PauseSegment` are the whole of the arithmetic, and nothing is stored per record (AD-7). Pauses are clipped to the activity's own span, so a `timer stop` recorded before the first record counts only from the start. `PauseSegment` — which stretch between boundaries an instant falls in — is what both the smoothing clip and the immobility window compare, rather than deriving boundaries twice.
- `FitActivityReader` reads `timer` events of type `start`, `stop`, `stop_all`, `stop_disable` and `stop_disable_all`: a watch writes the disable variants when the athlete presses stop, and reading only the self-paused two left genuinely paused files behaving as if nothing had been read. A second stop before the next start extends the pause already open, an unpaired start closes nothing, and a stop and a start at the same instant are no pause at all. A stop nothing reopens is the end of the activity — unless records keep arriving after it, in which case the pause runs to the last record, which is the device behaviour the fixture itself models.
- `RepairService` interpolates over moving time, taking **both** the bucket and the fraction inside it from the moving-time array; an anchor's own `TimeSeconds` is elapsed time, and mixing the two divides moving seconds by an elapsed span. The smoothing window is clipped by `PauseSegment`, a no-op for an activity with no pauses, and `EnforceMonotonicity` still runs after smoothing (AD-8).
- `EnforceMonotonicity` no longer pins the finish unconditionally: the shortfall it pays at the last record must be within what the sport cap allows for that record's own interval, or it throws. That guard is what turns an upstream scaling mistake into a loud failure instead of a teleport at the finish line, and it is an assertion rather than behaviour — no input reaches it when the series is right.
- `GpsStallDetector` skips records that fall inside a pause and never extends its window across a pause boundary. Either one alone is insufficient: without the skip, a watch that reports no cadence at all while standing still is read as a frozen GPS; without the boundary, the record before a pause and the record after it are 15 m apart across five minutes, which reads as frozen however the two sides look. With both, the paused fixture is measured end to end (2460 s of 2460) instead of collapsing to the pause.
- The writer's `total_timer_time` is `elapsed - CleanedOriginal.PausedSeconds` for the lap, the session and the activity, and `total_elapsed_time` is untouched. Pauses that cover the whole activity are an event stream that cannot be true — zero timer time beside a full distance reads as an infinite pace — so they are ignored rather than written. `avg_speed` is derived from that same timer time, so the file cannot state one duration beside an average computed from another; the sensor averages stay Story 1.7's.
- `RepairReport.PausedSeconds` (and the derived `MovingSeconds`) carry the pauses out of the core, and the CLI prints moving time and the paused total beside the elapsed time when there is any. The pace formula is deliberately still elapsed-based — Story 1.7 owns it — but the contradiction is now on screen instead of hidden.
- `SyntheticFitBuilder` writes records and timer events as one timestamp-ordered stream and emits no event messages for an activity with no pauses, so the pause-free fixture's bytes do not move. A second overload takes an explicit event list, which is how the matrix's unpaired start, double stop, same-instant tie and trailing `stop_all` are expressed at all — `Pauses` cannot describe them. `SyntheticRun.Create` and `CreateWithPause` now share the course, the seeded noise, the jitter and the start clock, so the two cannot drift onto different routes while the pinned numbers compare them. `FitMessageInspector.InspectTiming` returns one summary per lap rather than one value overwritten by the last lap, ready for Story 1.6.
- **Verified independently of the implementation report:** `dotnet build -c Release` 0 warnings; `dotnet test -c Release` 122 passed (98 at the story's baseline). Byte identity measured against a worktree at the baseline commit: the exported synthetic original and track FITs and the CLI's repaired output for the pause-free pair all have the same MD5 as before the story (`0bc9c70b`, `916e36a8`, `70ed2ec2`), and the CLI still reports 1129 anchors, 6.50 km, 36:00 and the same stall warning on that pair. The paused pair end to end through the CLI: 41:00 elapsed, 36:00 moving, five minutes of pauses, 2200 anchors, no stall, 6.50 km. Ten mutations were run against the new guards — the half-converted interpolation, the forced-finish check, the stall boundary, the stall skip, the all-pause timer time, the averages over elapsed time, unmerged pauses, `stop_disable` ignored, an outlived stop treated as the end, and a report that forgets the pauses — and each failed between one and nine tests. The bug the review measured fails the pace assertion even with the forced-finish guard removed, so the test and the guard have independent teeth.
- **Not mine, but measured here:** `synthetic_track.fit` is not byte-stable when the suite runs in parallel — the same baseline commit writes `916e36a8` when only the export test runs and `ed21cdd4` when all of it does, and a third hash appeared on one run. The exported original is stable. It reproduces on the untouched baseline, so the FIT SDK's encoder appears to share something across threads. Byte-identity checks above were therefore run one test at a time in both trees.
- **Anchors inside a pause are not excluded.** The aligner is untouched, so a device that keeps emitting *moving* positions while the timer is stopped can still seat anchors inside the interval; the distance then holds flat relative to the last of them rather than to the record that opened the pause. Recorded in `deferred-work.md` with the anchor rework, which is also where AD-21's clipping at anchors belongs.
- **Repaired `speed` is still computed over elapsed seconds**, so the three records on either side of a pause carry a diluted speed while their distance is correct. The series and the summaries are Story 1.7's.

## Review Triage Log

One review pass, three layers (blind hunter 13, edge cases 9, verification gaps 4 including its other findings). Three layers converged on the same defect, two of them with measurements and one having verified the fix before filing. Duplicates are logged once, naming the layers.

| Finding | Verdict | Evidence and route |
|---|---|---|
| `InterpolateDistances` mixes moving and elapsed time, so every record after a pause is short (all three layers, measured) | high | Confirmed by reading the code: the bucket search uses moving seconds while `span` and the numerator read the anchors' elapsed times. Measured: 2.44 m/s after the pause against 2.98 before, 4160 m where the pause-free run has 4504 m at the same moving second, and a 733 m jump in the final second at 246 m/s. Patched; restoring the old lines now fails nine tests. |
| Nothing pinned how fast distance advances after a pause (blind, verification) | high | All 115 tests passed with the bug and with the fix. The pause tests pinned flatness inside the interval, monotonicity and the forced endpoint only. Patched with a rate assertion and a per-second bound. |
| `EnforceMonotonicity` absorbs any shortfall as a teleport at the finish (blind) | high | Real: `distances[^1] = trackLength` is unconditional, and the monotonicity test asserts exactly that record, so it certified the symptom. Patched into an assertion that throws when the shortfall exceeds what the sport cap allows for that interval. |
| A legitimate pause raises `GPS_STALL_DETECTED` and collapses the measured zone (edge, implementer) | high | Measured: the measured zone ended at the pause, 894 s of 2460. Patched — the detector skips records inside a pause and stops its window at a boundary; both are needed, and each is pinned separately. |
| The written file's averages contradict its own timer time (blind, edge) | medium | Introduced here: `total_timer_time` became elapsed minus pauses while `ApplyAverages` kept dividing by elapsed. Patched for speed; heart rate left to Story 1.7 and recorded. |
| `PauseBlocks` and `PausedSecondsBefore` disagree about overlapping intervals (blind) | medium | Real: overlapping pauses became three smoothing blocks, and a record alone in one was not smoothed at all. Patched — intervals are ordered and merged once in the model, and both consumers read that list (AD-7). |
| Pauses supplied out of order silently lose seconds (edge, measured) | medium | Measured: 100 s counted where 200 s was paused. Closed by the same normalisation. |
| `stop_disable` and `stop_disable_all` open no pause (edge) | medium | Real and central: that is what a watch writes when the athlete presses stop, so on a genuinely paused file the story changed nothing. Patched. |
| A stop the records outlive is treated as the end of the activity (blind) | medium | Real: those standing-still seconds earned full metres with nothing said, which is the device behaviour the fixture itself models. Patched — such a stop runs to the last record. |
| A pause spanning the whole activity writes a zero timer time beside a full distance (edge, measured) | medium | Real: the pace reads as infinite. Patched — nonsense intervals are ignored for the timer time rather than written as zero. |
| The report carries nothing about pauses, so the CLI's pace contradicts the file (blind, verification) | medium | Measured: 6:18/km on screen against 5:30/km in the file. Partly patched — the report carries the paused seconds and the CLI prints moving time, so the disagreement is visible. The pace formula is deferred to Story 1.7, which gives the summaries one owner. |
| `PauseInterval.DurationSeconds` has no caller and can be negative (blind) | low | Correct. Dropped; normalisation already rejects a backwards interval. |
| `CreateWithPause` duplicates the route, noise and clock of `Create` (blind) | low | Real coupling risk: the equality test between the two fixtures is only meaningful while both run the same route. Patched — the setup is shared. |
| `FitMessageInspector.Timing` keeps only the last lap (blind) | low | Correct, and Story 1.6 is laps. Patched to return one summary per lap. |
| A loose 1.0 m tolerance beside a strict 0.001 m one on the same records; the same-instant tie-break tested in one order only (blind) | low | Both correct. Patched. |
| The report's pace, the per-record speed and the average heart rate still use elapsed time | medium | Real and deliberate: Story 1.7 owns the summaries and the derived values the contract must not carry (AD-14, AD-16). Deferred with the measurement. |
| Anchors can sit inside a pause; AD-21 also asks for clipping at anchors | medium | Real: `Aligner` is untouched, so a device emitting moving positions while the timer is stopped can seat an anchor inside the interval. Deferred to the anchor rework, which moves the pinned anchor counts anyway. |
| A repaired file carries no pauses, so repairing it again re-inflates the distance (blind) | medium | Real: the writer emits only start and stop_all because AD-19 pins the order. Deferred to Story 1.10, which owns the written structure, together with the spine's own question about one file in both roles. |
| AD-7 and AD-19 do not describe what this story did; the architecture memlog still carries A-06 and A-07 open (blind) | medium | Correct, and the pattern Stories 1.3 and 1.4 set. Deferred — the fix edits the spine, which this workflow keeps out of a story's diff. |
| `SyntheticFitBuilder` can no longer express out-of-order records, so the reader's sort is unreachable from a fixture (edge) | low | Rejected: the reader sorts because a real file may be out of order, and the sort is exercised through every fixture that writes records in order; a fixture that disorders them would test the fixture, not the reader. |
| The report of a degenerate all-pause file disagrees with its timer time (implementer) | low | Rejected for this story: the writer ignores nonsense intervals, and such a file is refused by the new plausibility guard before it reaches the report. Noted rather than hoisted into the core. |
| The suite failed once in ten runs, and the FIT encoder looks unsafe under parallel use (implementer, reproduced by me) | medium | Real: I saw one red run and could not reproduce it in nine attempts; the encoder's byte instability was reproduced on an untouched baseline tree. Deferred, with the note that it must be settled before the API encodes concurrently (AD-3). |
