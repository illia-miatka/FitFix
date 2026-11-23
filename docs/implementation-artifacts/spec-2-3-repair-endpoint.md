---
title: 'Story 2.3 — The service returns the repaired activity'
type: 'feature'
created: '2026-11-22'
status: 'done'
route: 'dispatch'
review_loop_iteration: 0
baseline_commit: 'fc033379b8bcccf374f1670e3edde844fd86c2fe'
context: ['{project-root}/docs/implementation-artifacts/epic-2-context.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The repair exists only behind the CLI. A browser has no way to send two files and get a corrected FIT back, and the page that Stories 2.4 to 2.6 build has no contract to draw: no numbers, no zones, no series, no file.

**Approach:** `POST /api/repair` takes the original and the track in one request and answers with everything the result screen and the later preview need: the summaries before and after, the trust zones, the per-record series at full length, the warnings with their codes and severities, and the repaired FIT itself as base64. One request, nothing stored, nothing thinned. A caller who has shown the user the over-long-track warning repeats the request asking for the whole track length.

</frozen-after-approval>

## Code Map

- `src/FitFix.Api/InspectEndpoint.cs` -- the shape to follow: one multipart part per file, roles matched exactly, the part made seekable, the core's own coded refusals left to the middleware, one log line of what AD-9 permits.
- `src/FitFix.Api/FitFixApi.cs` -- where endpoints are mapped, the upload limit and form limits are set, and every failure already becomes a `ProblemDetails`.
- `src/FitFix.Core/RepairPipeline.cs` -- the single composition of read, align and repair, with overloads carrying `TrackLengthMode`. The endpoint calls it; it assembles nothing itself.
- `src/FitFix.Core/RepairReport.cs` -- `RepairResult` carries `Records`, `Report`, `Alignment`, `CleanedOriginal` and `Summary`; the report carries both track lengths, the zones, the paused seconds, the discarded count, the lap count and the warning codes.
- `src/FitFix.Core/ActivitySummary.cs` -- `Total` and `Laps`, each with distance, elapsed and moving seconds, averages and maxima, elevation, pace and the measured fraction. The pace and the fraction are the derived values the contract must not carry (AD-14).
- `src/FitFix.Core/Diagnostics/DiagnosticCodes.cs` -- `SeverityOf` is how each warning's severity reaches the answer.
- `src/FitFix.Core/Fit/FitActivityWriter.cs` -- `WriteToArray(result, utcOffsetSeconds)` gives the bytes to encode; the offset stays zero until Story 1.10.
- `tests/FitFix.Api.Tests/InspectTests.cs`, `ApiHost.cs` -- the HTTP test style and the host; `SyntheticRun` and `SyntheticFitBuilder` are already linked into that project.

## Tasks & Acceptance

**Execution:**
- [x] `src/FitFix.Api/Contracts/RepairResponse.cs` -- new. Hand-written records: the before and after summaries, the zones as kind plus bounds, the series as parallel arrays, the warnings as code plus severity string, and the FIT as base64. No pace, no fraction, no delta; enums as strings. -- AD-14, and the UI computes what it wants from base quantities.
- [x] `src/FitFix.Api/RepairEndpoint.cs` -- new. `POST /api/repair` reading two file parts named for their roles and an optional track-length mode, calling `RepairPipeline` and transcribing its result. An unknown mode is refused with a declared code rather than guessed. -- AD-13 and AD-17.
- [x] `src/FitFix.Core/Diagnostics/DiagnosticCodes.cs` -- a code for a request naming a mode that does not exist, beside the transport codes. -- the shell invents no codes (AD-18).
- [x] `src/FitFix.Api/RepairEndpoint.cs` -- the series come back at the length of the record list, every array the same length, with null where a value is absent, and no thinning. -- AD-5: the series are indexed by the original's records so a shell can line them up.
- [x] `src/FitFix.Api/FitFixApi.cs` -- map the endpoint; one log line carrying only what AD-9 permits. -- the failure contract of Story 2.1 carries the rest.
- [x] `tests/FitFix.Api.Tests/RepairTests.cs` -- new. Cover the I/O matrix below over real HTTP. -- a contract is an HTTP shape.
- [x] `tests/FitFix.Api.Tests/ContractShapeTests.cs` -- add the response's shape. -- one shape test per DTO.

**Acceptance Criteria:**
- Given an original and a track, when the response is read, then the base64 decodes to a FIT the core can read back, whose session distance equals the after-summary's distance, and the series are all as long as the record list.
- Given the same two files with the whole-track mode, when the request is repeated, then the after-summary's distance is the track's whole length and the over-long warning is still in the answer.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| A repair | the synthetic pair | before and after summaries, zones, series, warnings, base64 FIT | N/A |
| The file is real | the base64 | decodes to a FIT the core reads back, with the session distance of the after-summary | N/A |
| Series line up | the same answer | every series is as long as the record list, and a missing value is null rather than absent or zero | N/A |
| Nothing is thinned | a two-hour activity | the series carry one entry per record, not a sample | N/A |
| Zones travel whole | an activity whose GPS returned | every zone with its kind and bounds, tiling the activity | N/A |
| Warnings carry severity | an activity with a blocking and an info warning | each code with its severity as a string | N/A |
| No derived values | the whole answer | no pace, no fraction, no delta anywhere in it | N/A |
| The mode is honoured | the over-long track, default and whole-track | the after distance differs between the two, and the warning is in both | N/A |
| An unknown mode | `trackLengthMode=whatever` | refused with the declared code, not guessed | N/A |
| A missing part | one file only, or neither named for its role | the declared codes from Story 2.2 | N/A |
| Refusals come from the core | a track with one point | the core's coded refusal as a 400 | N/A |
| Inside the budget | a two-hour activity | the answer arrives in under three seconds | N/A |
| Nothing is kept | any repair | no file is written anywhere, and the log carries no coordinate, no file content and no file name | N/A |

## Verification

**Commands:**
- `dotnet test -c Release` -- expected: all tests pass over the whole solution
- `grep -rnE "Sum\(|Average\(|/ *(elapsed|moving|timer)" src/FitFix.Api` -- expected: no arithmetic over an activity

## Design Notes

The series are the one part of this contract that has to be wasteful on purpose. AD-5 indexes them by the original's records so a shell can line a chart up with a map without guessing, and a server that thinned them would decide for the client what resolution it may have. Equal lengths with nulls in the gaps is what makes that honest: a shorter array would mean the client has to work out which record each entry belongs to.

The pace is the clearest case of AD-14's rule. The core computes it for the CLI, and the DTO still may not carry it: a UI that divides the distance it was given by the moving seconds it was given can never disagree with the file, while a second number travelling beside them can.

## Review Triage Log

One review layer, which mutated the transcription and watched the suite. Every finding was on the test side; no production code changed.

Patched: all five. Every repair fixture was 1 Hz and pause-free, so elapsed, moving and paused seconds were the same numbers and swapping them passed; `series.secondsFromStart` could be replaced by the record index; seven of the fifteen after-summary members — the four maxima, average power and cadence, elevation loss — were transcribed and never read, so writing an average into a maximum failed nothing; and null-rather-than-zero was pinned only on the inspect side, so the two endpoints could have made different claims about one file. The test named for AD-3 counted files in a directory this pipeline never writes to, so writing a temp file inside the request kept it green — that assertion is deleted rather than claiming the guarantee falsely, and the `FormOptions` test carries it.

Each fix was mutation-checked by the implementer, and the measured budget is 939 ms against three seconds.

Deferred: the Garmin encoder's thread-unsafety, now seen twice and recorded with the new evidence, because `/api/repair` encodes a file per request; and response compression with a size ceiling, which AD-23 owns.
