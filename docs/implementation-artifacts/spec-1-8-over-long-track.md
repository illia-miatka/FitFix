---
title: 'Story 1.8 — An over-long track does not inflate the pace'
type: 'feature'
created: '2026-11-22'
status: 'done'
route: 'dispatch'
review_loop_iteration: 1
baseline_commit: '0be506c612506465476cac34d3d407fde9220e15'
context: ['{project-root}/docs/implementation-artifacts/epic-1-context.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The repair always ends on the whole length of the reference track. A route editor that drew one lap too many therefore turns a 6.5 km run into 7.7 km at 4:40/km — FitFix "improving" the result, which the PRD promised it would not do. Trimming silently would be just as wrong: the track may be right and the original's GPS bad from the first second. Nobody is told, and nobody is asked.

**Approach:** The core computes two lengths — the track's whole length, and the part consistent with the original's own time at a plausible speed — and reports both. When they differ by more than the threshold, the report carries `TRACK_LONGER_THAN_ACTIVITY` as a blocking warning with both numbers, and the conservative length is what the repair uses, so a preview never shows an inflated pace first. A caller that has asked the user can repeat the repair in a mode that trusts the track's whole length, and the warning stays in the report either way.

</frozen-after-approval>

## Code Map

- `src/FitFix.Core/Aligner.cs` -- the final anchor is `new Anchor(elapsed, track.Length, 0)`, and `MinDeviationToWholeTrack` decides `TRACK_FAR_FROM_ORIGINAL`. The end anchor is what has to target the chosen length rather than the whole one.
- `src/FitFix.Core/RepairService.cs` -- `Repair(original, track)` and `Repair(original, track, options)`; `EnforceMonotonicity(distances, trackLength)` pins the last record to the length it is given, and refuses a shortfall larger than the sport cap allows (Story 1.5). This is where the chosen length has to be decided, before the alignment runs.
- `src/FitFix.Core/RepairReport.cs` -- carries `TrackLengthMeters` today; `UsedTrackLengthMeters` joins it. `RepairResult` carries the summary (Story 1.7), whose distance must end on the chosen length.
- `src/FitFix.Core/Diagnostics/DiagnosticCodes.cs` -- `TRACK_LONGER_THAN_ACTIVITY` is already declared `blocking` and raised by nothing. This story is what raises it.
- `src/FitFix.Core/AlignmentOptions.cs` -- the home of every threshold (AD-10). The tolerance between the two lengths belongs here.
- `src/FitFix.Core/Models.cs` -- `OriginalActivity` carries records, sport, pauses, laps, `MovingSecondsFromStart`, `PausedSeconds`, `RecordedDistance` and `HasPosition`. A declared-sensor fact belongs beside them.
- `src/FitFix.Core/Fit/FitActivityReader.cs` -- reads `record`, `session`, `event` and `lap` messages. `device_info` is read nowhere, so nothing knows whether a real speed sensor existed.
- `src/FitFix.Core/RepairPipeline.cs` -- the single composition the CLI and the API call. A mode that trusts the whole track has to reach it without a default parameter (AD-10).
- `src/FitFix.Cli/Program.cs` -- prints the report; a blocking warning and two lengths are new things for it to say.
- `tests/FitFix.Core.Tests/Fixtures/SyntheticFitBuilder.cs` -- writes `file_id`, timer events, records, laps, session, activity. A `device_info` message is new.

## Tasks & Acceptance

**Execution:**
- [x] `src/FitFix.Core/Fit/FitActivityReader.cs`, `src/FitFix.Core/Models.cs` -- read `device_info` and record on the activity whether it declares a real speed source: a footpod, a wheel sensor or a power meter. Anything else, including a device whose only speed came from GPS, is not one. -- AD-17: a speed the device derived from GPS must never be a source, because after the failure it is zero.
- [x] `src/FitFix.Core/AlignmentOptions.cs` -- add the tolerance by which the two lengths may differ before anyone is warned, as a floor plus a fraction of the plausible distance rather than a flat number of metres. -- AD-10.
- [x] `src/FitFix.Core/RepairService.cs` -- compute the plausible distance as **what the anchors already measured plus what the cap allows for the moving time that remains**, never the cap over the whole activity. When a real sensor is declared, the device's own recorded distance is the plausible one instead — but it is the largest distance the device recorded, not the last one, and it never lowers the bound below what the anchors established. The chosen length is the smaller of the plausible distance and the track's length, and it is never below the distance the anchors measured. -- the two numbers AD-17 demands, by the priority it fixes.
- [x] `src/FitFix.Core/RepairService.cs`, `src/FitFix.Core/Aligner.cs` -- the alignment's end anchor, the interpolation and the monotonicity check all target the chosen length. -- a repair that ends anywhere else contradicts FR-5.
- [x] `src/FitFix.Core/RepairService.cs`, `src/FitFix.Core/RepairReport.cs` -- raise `TRACK_LONGER_THAN_ACTIVITY` when the lengths differ by more than the tolerance, and report both lengths always. -- the user cannot decide what they are not shown.
- [x] `src/FitFix.Core/RepairService.cs`, `src/FitFix.Core/RepairPipeline.cs` -- a mode that trusts the track's whole length, reaching both entry points through explicit overloads rather than a default parameter. The warning stays in the report in that mode. -- AD-10, and AD-17 says confirming changes the result rather than merely opening a gate.
- [x] `src/FitFix.Cli/Program.cs` -- print both lengths and the blocking warning when it is there, and say how to repeat the repair trusting the track. -- the only shell today must not hide a blocking warning.
- [x] `tests/FitFix.Core.Tests/Fixtures/SyntheticFitBuilder.cs`, `tests/FitFix.Core.Tests/Fixtures/SyntheticRun.cs` -- write a `device_info` message, and offer a run whose track is longer than its time can cover. The existing fixtures stay as they are. -- nothing can be proven about a declared sensor until a fixture declares one.
- [x] `tests/FitFix.Core.Tests/TrackLengthTests.cs` -- new. Cover the I/O matrix below. -- the invariant is a choice between two numbers, and only a test says which was taken.

**Acceptance Criteria:**
- Given a track no longer than the original's time can cover, when it is repaired, then no warning appears, the chosen length equals the whole length, and every number the suite already pins is unchanged.
- Given a track far longer than that, when it is repaired in each mode, then the distance ends exactly on the chosen length in both, monotonically, and the report carries both lengths and the blocking warning either way.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| An honest track | the synthetic run, whose track matches its time | no warning, chosen length equals the whole length, pinned numbers unchanged | N/A |
| One lap too many | a track twice as long as the time can cover | `TRACK_LONGER_THAN_ACTIVITY`, blocking, with both numbers; the repair ends on the shorter one | N/A |
| The user trusts the track | the same files in the whole-length mode | the repair ends on the whole length, and the warning is still in the report | N/A |
| Inside the tolerance | a track longer by less than the tolerance | no warning, and the chosen length is the whole length | N/A |
| A declared footpod | an activity whose `device_info` names a footpod, with a frozen GPS distance | the plausible distance comes from the device's own recorded distance, not from the sport cap | N/A |
| GPS-derived speed is never a source | the Chabany shape: no sensor declared, device distance frozen at 3.9 km | the sport cap over the moving time is used, so the chosen length is not 3.9 km | N/A |
| The cap comes from the options | the same activity repaired with a cap below its own pace | the track is trimmed, and with the default cap it is not | N/A |
| A long ride is not warned for ordinary disagreement | a 100 km route with a declared wheel sensor reading 1 km less | no warning, because the tolerance grows with the distance | N/A |
| The athlete never freezes | a declared sensor whose distance is below what the anchors measured, and the same with the sport cap | the series keeps advancing to the last record; the chosen length is never below the measured anchors | N/A |
| A sensor that measured nothing | a declared sensor whose recorded distance is zero, and one where it is absent | the cap decides instead, and no length of zero reaches the alignment | `ArgumentOutOfRangeException` if it ever did |
| The Chabany shape is protected | the real case's proportions: 2160 s, GPS measuring 3.9 km then frozen, a 6.5 km track | the plausible distance is the measured 3.9 km plus the cap over the remaining moving time, not the cap over all of it | N/A |
| Monotone in both modes | either mode on the over-long track | distance never decreases and ends exactly on the chosen length | the shortfall guard from Story 1.5 does not fire |
| The summary agrees | either mode | the summary's distance, the written file's total and the report's chosen length are the same number | N/A |
| Pauses count | an over-long track on an activity with a five-minute pause | the plausible distance uses moving time, not elapsed | N/A |

## Verification

**Commands:**
- `dotnet build -c Release` -- expected: no errors, no new warnings
- `dotnet test -c Release` -- expected: all tests pass over the whole solution; no number pinned before this story moves

## Design Notes

The conservative length is the default on purpose, and that is a product decision, not a heuristic: a preview that opens with an inflated pace has already told the user something flattering and false, and a service whose whole point is not improving results cannot afford that. The whole-length mode exists for the case where the track is right and the original's GPS was bad from the start — which the user knows and the service does not.

The sensor priority is about one real failure. In the Chabany case the device's own distance froze at 3.9 km when the GPS died, because that device derived speed from GPS; believing it would have trimmed a correct 6.5 km track to 3.9. A footpod, a wheel sensor or a power meter measures movement without GPS, so when `device_info` declares one, the device's own distance is the better witness than any cap. When none is declared, the sport's maximum over the moving time is the only honest bound left — generous by design, because its job is to catch a track twice too long, not to second-guess a plausible one.

## Implementation Notes

- **The plausible distance bounds the remainder, not the activity.** `RepairService.PlausibleDistance` is what the anchors measured plus the sport's cap over the moving seconds left after them. A cap spread over every second treats the measured stretch as if it too could have been run at 7 m/s: on the Chabany proportions that allows 15.1 km against a 3.9 km measurement, which on an over-long track opens the preview at 2:23/km. Bounding only what is unknown gives 9.9 km there and still leaves the honest 6.5 km track untouched.
- **The chosen length is never below what the anchors established.** A length inside the measured zone would have the series reach its finish there and hold the athlete still for the rest of the activity — worse than the inflated pace the whole decision exists to avoid. A declared sensor claiming less than the GPS proved is therefore set aside rather than clamped: it contradicts positions that can be pointed at, and the cap rule decides instead.
- **Two phases in the aligner.** The chosen length is computed from the measured distance, so one call could not do both: `Aligner.Measure` finds the anchors and `Aligner.EndingAt` pins the finish to the length `RepairService.ChooseTrackLength` picked from them. `Alignment.MeasuredDistanceMeters` carries the measured figure through, because after the finish is pinned `Anchors[^1]` is no longer it.
- **`EndingAt` refuses an impossible finish.** When the anchors reach the last record there is no estimated zone to spread a difference over, so the finish replaces that last anchor — and the replacement is refused with `DISTANCE_SERIES_IMPLAUSIBLE` when it moves the athlete further in that one interval than the cap plus the track-length tolerance allows, mirroring Story 1.5's shortfall guard in the other direction. The tolerance has to be part of that budget: without it the guard would refuse the very whole-length choice the tolerance had just made, and an ordinary 20 m projection shortfall at the finish line would fail. An appended finish is not guarded — a long interpolation across the estimated zone is the design, and in `Full` mode it is the user's confirmed choice.
- **The tolerance is a floor plus a fraction**: `max(TrackLengthToleranceMeters, TrackLengthToleranceFraction × plausible)`, 200 m and 2 % by default. Flat metres would warn about every long ride — a 100 km route and a wheel sensor disagree by 1–2 km as a matter of course, and warning about all of them is the same as warning about none. Both numbers are judgements rather than measurements; the deferral in `deferred-work.md` says so.
- `TrackLengthMode.Plausible` is the enum's zero value on purpose, like `DiagnosticSeverity.Blocking`: a mode nobody set must take the conservative length. `ChooseTrackLength` switches on it exhaustively and throws on anything else, so a cast or a member a later story adds cannot fall through the default that was meant to close the gate.
- **`LargestRecordedDistance` joins `RecordedDistance`** rather than replacing it. They answer different questions: the furthest the odometer ever reached, which is what may bound the chosen length, and what the watch was showing when it stopped, which is the "before" the athlete saw in Strava (AD-16). A counter that resets mid-file makes them differ by kilometres.
- **`AlignmentOptions` is validated where it is read.** `RepairService.Repair` refuses a non-positive speed cap and a negative tolerance: AD-10 puts every threshold in the caller's hands, and these are the ones whose nonsense values reach an invariant rather than merely a different number — a cap of nothing bounds an original without GPS at a chosen length of zero. The wider validation of the options record stays deferred from Story 1.3.
- The sensor profile is read through the FIT SDK's own subfield resolution, which answers only when `device_info` states `source_type` as `antplus` or `bluetooth_low_energy`, and only `device_info` messages timestamped inside the activity's own record span are counted — the same clipping the `lap` messages get, so a footpod paired for an earlier session in the same file says nothing about the run being read. A message with no timestamp cannot be placed and is not counted. All three choices err towards "no sensor", which is the safe direction: the fallback is a generous cap, while the opposite mistake is the 3.9 km trim.
- The trimmed mode leaves the last stretch of the drawn route out of the written file, which is what "the repair ends on the chosen length" means geometrically. The file is still monotone and still ends at the athlete's last timestamp.
- **CLI.** `--trust-track` is the flag, and its absence can only produce the shorter length. The track-length line prints both numbers when they differ and one when they do not. **Every** blocking warning then gets its own block at the end, not just this story's — `TRACK_FAR_FROM_ORIGINAL` is equally blocking and was equally buried in the one-line code list. **Exit code 3** is new: AD-22 withholds the result until the user confirms a blocking warning, and a command-line tool has nobody to ask and has already written the file, so the exit code is the only gate it can offer — otherwise a script cannot tell a trimmed, unconfirmed repair from a clean one. The codes are listed in `--help`. Every figure in the report now goes through `Km`/`Meters`/`Share`, which fix the invariant culture: the measured share already carried it while the distances did not, so one report could read `30 %` beside `32,50 км`. The `ArgumentException` arm no longer swallows `ArgumentOutOfRangeException` — the core raises that for a threshold or a chosen length that cannot be, which is this program's defect and must not be reported to the athlete as a bad file path.
- No test executes `Program.cs` (the deferral from Story 1.2 still stands), so the CLI was verified by hand against the exported synthetic pair with the loop drawn five times: honest track exit 0 and one length; over-long exit 3, `32.50 км (використано 9.94 км)` and the advice block; `--trust-track` exit 3 and the whole length; a track moved a degree away printing two blocking blocks; `repair "" track.fit` still exit 2 with one line.
- **Verified independently of this note:** `dotnet build -c Release` 0 warnings, `dotnet test -c Release` 290 passed (251 before the story: 171 core + 80 API; 210 core + 80 API after). Two mutations confirmed the new tests discriminate: pointing the end anchor back at `track.Length` fails `Distance_never_decreases_and_ends_exactly_on_the_chosen_length(Plausible)`, and pointing the monotonicity pass back at `track.Length` fails 14 tests. The private real-file test still exits quietly — on the Chabany pair the anchors measure 3.9 km and the cap allows six more over the moving time left, so its 6.5 km track stays whole and neither its warning nor its distance moves.

## Review Triage Log

One review pass, three layers, all converging on the rule itself. Patched: the plausible distance bounds only the remainder (the Chabany proportions gave 15.12 km and now give 9.9 km); the chosen length is never below what the anchors measured, and a sensor contradicting them is set aside rather than clamped, because clamping froze the athlete for the whole estimated zone (measured at 18:00/km and 72:00/km); the largest recorded distance replaces the last one, so a counter reset no longer trims the route to metres; the tolerance is a floor plus a fraction, so a 100 km ride disagreeing by 1 km is not warned; `device_info` is clipped to the record span; the mode switch is exhaustive; the aligner refuses an impossible finish; the cap and both tolerance parts are validated where they are read; the CLI prints every blocking warning, exits 3 for an unconfirmed one, no longer reports a caller defect as a bad file path, and formats every figure in one culture; the two lengths have their own summaries; and the suite's "ends on the track" assertions moved to the used length.

Rejected: the matrix row about a walk, which no sport profile can trim, so its test was renamed for what it checks and the unusable fixture removed. Deferred: the API's `trackLengthMode` and both lengths, which Story 2.3 owes; and the CLI's untested `--trust-track`, which is the first CLI behaviour whose failure changes the user's result.
