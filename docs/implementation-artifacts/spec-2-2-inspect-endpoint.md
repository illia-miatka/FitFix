---
title: 'Story 2.2 — The service describes the file just chosen'
type: 'feature'
created: '2026-11-22'
status: 'done'
route: 'dispatch'
review_loop_iteration: 1
baseline_commit: 'e18a7feae6b685611d06269fd83ef17b36d39563'
context: ['{project-root}/docs/implementation-artifacts/epic-2-context.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The page will have two dropzones, and a person who drops a file into one has no way of knowing whether the service understood it until the whole repair has run. A wrong file — the track where the original belongs, a route with no elevation, an activity with no GPS at all — should be visible the moment it is chosen, not after a failed repair.

**Approach:** `POST /api/inspect` takes one file and a required role, and answers with what the core read from it: for an original, the sport, when it started, how long it ran, the distance the device recorded, how many records it holds and whether any position survived cleaning; for a track, its length, how many points it kept and whether it carries altitude. Every number comes from the same core models the repair uses, so the dropzone and the result screen cannot disagree. The role is never guessed.

</frozen-after-approval>

## Code Map

- `src/FitFix.Api/FitFixApi.cs` -- `Build(builder, additionalEndpoints)` is where endpoints are mapped and where the failure contract from Story 2.1 already wraps everything: a `FitFixException` becomes a 400 with its code, an over-sized body a 413, a broken invariant a 500.
- `src/FitFix.Api/FailureStatus.cs` -- maps a code to its status. A new code for a missing role belongs there and in the core registry.
- `src/FitFix.Core/Diagnostics/DiagnosticCodes.cs` -- the one registry. The transport-level section added in Story 2.1 is where a code that describes a malformed request rather than a file's contents belongs, with the same note that Story 2.7 owes it a Ukrainian text.
- `src/FitFix.Core/Fit/FitActivityReader.cs` -- `Read(Stream)` gives an `OriginalActivity` with records, sport, pauses and laps; it raises coded refusals for bytes that are not a FIT activity.
- `src/FitFix.Core/Tracks/TrackReader.cs` -- `ReadReferenceTrack(Stream, AlignmentOptions)` detects FIT, GPX or TCX from the content and returns a `ReferenceTrack` built with the de-duplication spacing from the options. `ReadPoints(stream, out format)` gives the points and the detected format.
- `src/FitFix.Core/GpsCleaner.cs` -- `Clean(activity, options)` is public and runs inside `RepairService.Repair`. An inspect request never repairs, so it must clean the series itself or it will describe a teleport the repair would have discarded (AD-25).
- `src/FitFix.Core/Models.cs` -- `OriginalActivity.ElapsedSeconds`, `RecordedDistance`, `Records`, `Sport`; `ReferenceTrack.Length`, `Points`, `StartAltitude`.
- `src/FitFix.Core/AlignmentOptions.cs` -- `TrackMinSpacingMeters` is the same for every sport, and `ForSport` only changes the speed cap, so a track can be read with the defaults and still match what the repair will build.
- `src/FitFix.Api/Contracts/` -- `ProblemResponse` and `HealthResponse` are the hand-written shape to follow: records, no derived values, enums as strings (AD-14).
- `tests/FitFix.Api.Tests/ApiHost.cs` -- hosts the real pipeline on Kestrel at port zero; `tests/FitFix.Api.Tests/Probes.cs` holds test-only endpoints. `tests/FitFix.Core.Tests/Fixtures/SyntheticFitBuilder.cs` and `SyntheticRun` can write a real FIT activity, a FIT track and a GPX.

## Tasks & Acceptance

**Execution:**
- [x] `src/FitFix.Core/Diagnostics/DiagnosticCodes.cs` -- declare the code for a request that names no role, beside the transport codes. -- the shell must not invent a code (AD-18).
- [x] `src/FitFix.Api/Contracts/InspectResponse.cs` -- new. Two hand-written records behind one discriminated answer carrying `role`: the original's sport as a string, start time, elapsed seconds, recorded distance, record count, whether a position survived and how many were discarded; the track's length, point count, detected format as a string and whether it carries altitude. No pace, no fractions, no deltas. -- AD-14, and the UI computes what it wants from these.
- [x] `src/FitFix.Api/InspectEndpoint.cs` -- new. `POST /api/inspect` reading one file part and a `role` of `original` or `track`. A missing, empty or unrecognised role is refused with that code; the file's content type and name never decide anything. -- AD-13: the shell checks size and parts, the core decides the format.
- [x] `src/FitFix.Api/InspectEndpoint.cs` -- an original is read, cleaned and described; a track is read through `ReferenceTrack` with the same spacing the repair uses. -- AD-15 and AD-25: one set of numbers, and no teleport in them.
- [x] `src/FitFix.Api/FitFixApi.cs`, `src/FitFix.Api/FailureStatus.cs` -- map the endpoint and give the new code its status. -- the failure contract of Story 2.1 carries the rest.
- [x] `tests/FitFix.Api.Tests/InspectTests.cs` -- new. Cover the I/O matrix below over real HTTP. -- a contract is an HTTP shape.
- [x] `tests/FitFix.Api.Tests/ContractShapeTests.cs` -- add the two response shapes. -- AD-14 asks for one shape test per DTO.

**Acceptance Criteria:**
- Given a FIT activity and the role `original`, when the response is read, then it states the sport, the start, the elapsed seconds, the recorded distance, the record count and whether a position survived — every one of them the number the core read.
- Given the same track file sent to inspect and then repaired, when both lengths are compared, then they are identical, because both were built by `ReferenceTrack` with the same spacing.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| An original | a FIT activity, role `original` | sport, start, elapsed, recorded distance, record count, a position survived | N/A |
| A track | a FIT, GPX or TCX track, role `track` | length, point count, detected format, whether altitude is present | N/A |
| One length, not two | the same track through inspect and through a repair | the two lengths are identical to the millimetre | N/A |
| The role is never guessed | no `role` part, an empty one, and `role=whatever` | 400 with the declared code for a missing role | the code is in the registry |
| Content type does not decide | a GPX sent as `application/octet-stream`, and a FIT sent as `text/plain` | both accepted, format from the content | N/A |
| The wrong file in the role | a track sent as `original`, and a file with no positions sent as `track` | the core's own coded refusal as a 400, naming what was wrong | `NOT_A_FIT_FILE` or the track's own refusal |
| No GPS at all | an activity whose records carry no position | the answer says no position survived, rather than failing | N/A |
| Only garbage GPS | an activity whose only positions are teleports | no position survived, and the discarded count says how many went | N/A |
| A track with no altitude | a GPX whose points carry no elevation | altitude absent, not a flat zero | N/A |
| Altitude is about the track, not its first point | a GPX whose first point has no elevation and whose rest do | altitude present | N/A |
| A ride is judged as a ride | a cycling original with segments above the running cap | the sport is `cycling`, and the discarded count is the one the repair of the same bytes reports | N/A |
| Inspect refuses what the repair refuses | an activity whose records span no time | the same coded refusal the repair raises, not a description | `ACTIVITY_WITHOUT_ELAPSED_TIME` |
| One number, one claim | a file whose records carry no distance field | inspect and the repair's report say the same thing about it | N/A |
| A malformed multipart is not a size problem | a `multipart/form-data` header with no boundary, and a request with more form fields than the limit | 400 with the malformed-request code, never 413 | N/A |
| The commonest wrong drops | a file that is no known track format, a track whose points all collapse, and a FIT with no records | each refused with the core's own code | `TRACK_FORMAT_UNKNOWN`, `TRACK_WITHOUT_DISTINCT_POINTS`, `FIT_HAS_NO_RECORDS` |
| Nothing attached | a request with no file part | 400 with a declared code | N/A |
| Two files attached | a request with two file parts | refused rather than silently reading the first | N/A |
| Over the size limit | a file above `FITFIX_MAX_UPLOAD_BYTES` | 413 `FILE_TOO_LARGE`, as Story 2.1 settled | N/A |
| Nothing is kept | any successful inspect | no file is written anywhere, and the log carries no coordinate, no file content and no file name | N/A |

## Spec Change Log

- **Trigger:** review pass 1. Two mutations stayed green because every original in the API suite is a run and `ForSport(Running)` returns the default options: replacing the per-sport cleaning with the defaults, and renaming the cycling sport string to `running`. `hasAltitude` was read from the track's first point alone, so a GPX whose first point lacks elevation reports none. An activity with no elapsed time was described as fine although the repair refuses it. A file with no distance field is `null` at the dropzone and `0` in the report — one number, two claims. And `InvalidDataException` from reading the form was mapped to 413, so a boundary-less or over-field-count request is told its file is too large.
  **Amended:** seven rows joined the matrix, and the Design Notes no longer claim the length test catches a sport-dependent spacing change: it does so only with an original that is not a run.
  **Known-bad state avoided:** a dropzone that describes a file the repair will refuse, and that disagrees with the result screen about the same number.
  **KEEP:** every refusal here is the core's own coded one; cleaning before describing; the track length identical to the one the repair spreads the activity over.

- **Trigger:** the matrix row for the wrong file in a role named "an activity sent as `track`" as a refusal, which is wrong: a positioned FIT activity *is* a legitimate track, and that is exactly how the real case's `FFF.fit` arrived. Refusing it would refuse the file this project exists for.
  **Amended:** the row now names a file with no positions, which is what a track genuinely cannot be.
  **KEEP:** every refusal in this endpoint is the core's own coded one, so Story 2.1's middleware owns every status and body.

## Verification

**Commands:**
- `dotnet build -c Release` -- expected: no errors, no new warnings
- `dotnet test -c Release` -- expected: all tests pass, over the whole solution
- `grep -rnE "Sum\(|Average\(|/ *(elapsed|moving|timer)" src/FitFix.Api` -- expected: no arithmetic over an activity

## Design Notes

The track is read with the default options on purpose. `TrackMinSpacingMeters` is the same for every sport and `ForSport` only changes the speed cap, so a track inspected before any activity is known is de-duplicated exactly as the repair will de-duplicate it. If a later story makes the spacing sport-dependent, inspect has to be given the sport — and the test that compares the two lengths only says so when its original is **not** a run, because `ForSport(Running)` returns the default options and the two option sets are then the same object's worth of values.

Cleaning before describing is what keeps the two screens honest. A file whose only positions are teleports has no usable GPS, and saying "GPS present" about it would promise a map the repair cannot draw. The discarded count is in the answer for the same reason: the dropzone can say that something was thrown away before the user waits for a repair to tell them.

## Implementation Notes

- `POST /api/inspect` takes one file part and a role, reads it through the core, and answers a hand-written record per role. The original's answer carries the sport as a string, the start, the elapsed seconds, the recorded distance, the record count, whether a position survived, how many were discarded, and the registry codes for those last two facts so the dropzone reuses the vocabulary the UI dictionary is built on rather than inventing prose.
- **Cleaning happens before describing.** A file whose only positions are teleports has no usable GPS, and saying otherwise would promise a map the repair cannot draw. The discarded count is in the answer for the same reason.
- **`FormOptions.MemoryBufferThreshold` is raised to the upload limit**, which was not in the task list and is required by AD-3: above the 64 KB default ASP.NET buffers a part to a temp file, so "no file is written anywhere" would have failed on every real FIT, and Story 2.8's read-only container would crash. `UploadLimit` now has its own ceiling so no configured value can push a part onto disk.
- `OriginalActivity.HasPosition` and `ReferenceTrack.HasAltitude` are core members now, each with one owner: the aligner reads the first for `NO_ORIGINAL_GPS`, and the second answers whether the track carries altitude *anywhere* — reading the first point alone reported none for a GPX whose first point lacks elevation and whose remaining 1300 carry it.
- `TrackReader.ReadReferenceTrack` gained an overload that yields the detected format, so the endpoint no longer re-implements that composition and a later step added to it cannot reach the repair while missing inspect.
- `OriginalDistanceMeters` became absent rather than zero in the core report, because the dropzone said `null` and the report said `0` about the same file. The CLI prints the before-distance only when it exists.
- **Verified independently of the implementation report:** `dotnet build -c Release` 0 warnings; `dotnet test -c Release` 171 core and 80 API tests, two consecutive clean runs. I re-ran the mutation the review used — renaming the cycling sport string to `running` — and one test fails where all 80 pass with it correct.
- Two small duplications remain, both noted rather than hidden: `ACTIVITY_WITHOUT_ELAPSED_TIME` is raised by the same one-line predicate in the endpoint and in `RepairService`, and the two new core members are covered through HTTP rather than by core unit tests of their own.

## Review Triage Log

One review pass, three layers (blind hunter 14, edge cases 11, verification gaps 2 plus its other findings). Two layers proved their central findings by mutation, and both stayed green for the same reason: every original in the API suite was a run, and `ForSport(Running)` returns the default options.

| Finding | Verdict | Evidence and route |
|---|---|---|
| Inspect's per-sport cleaning is unverified (verification, measured) | high | Replacing `ForSport(activity.Sport)` with the defaults kept all 240 tests green. A ride would be cleaned at the running cap, so the dropzone would report discarded positions the repair keeps — the disagreement AD-15 forbids. Patched with a cycling fixture; I re-ran the mutation and it now fails. |
| The cycling sport string is unpinned (verification, blind, measured) | high | Renaming it to `running` kept the suite green: a cyclist's file would be labelled a run. Closed by the same test. |
| `hasAltitude` reads the first point alone (blind, edge) | high | Real and user-visible: a GPX whose first point lacks elevation reports none, and the UI hides a profile that exists. Patched — the core track answers whether it carries altitude anywhere. |
| An activity with no elapsed time is described as fine, then refused by the repair (edge) | high | Real, and exactly the gap this story closes: the dropzone says the file is understood and the button fails. Patched — the same refusal is raised here. |
| A file with no distance field is `null` here and `0` in the report (edge) | high | Real: one number, two claims on two screens. Patched — the report's member is absent too, and the CLI prints it only when present. |
| `InvalidDataException` answers 413 for a malformed multipart (blind, edge) | high | Real: ASP.NET raises it for a boundary-less header and for over-count form fields, so a truncated request is told its file is too large. Patched — 413 only when the declared length reached the bound. |
| The shell re-implements the core's track composition (blind) | medium | Real: a later step added to it would reach the repair and miss inspect. Patched with an overload that yields the format. |
| `HasPosition` is a third copy of a core judgement (blind) | medium | Correct, and the epic context says the shell transcribes. Patched onto `OriginalActivity`, read by the aligner too. |
| Three client mistakes collapse into one code (blind) | medium | Real: the registry is the UI's only vocabulary, so Story 2.7 could not say "you dropped two files". Patched — a code each for a missing and for too many files. |
| `INSPECT_ROLE_MISSING` names one of three cases (blind) | medium | Correct and free to fix before 2.7 writes a text. Renamed. |
| A non-seekable part leaves as 500 on the original path (edge) | medium | Real: the decoder rewinds the stream and the track path guards this while the original's did not. Patched. |
| `ReadFormAsync` ignores the cancellation token (blind, edge) | medium | Real: the middleware's client-gone branch is keyed on exactly that token, and a 20 MB file was decoded for a response nobody would read. Patched. |
| The role is chosen by a ternary (edge) | medium | Real: a third role would be described as a track with a track's fields. Patched into an exhaustive switch. |
| The upload limit itself is not clamped, so a part can spill to disk (edge) | medium | Real, and it would crash Story 2.8's read-only container. Patched in `UploadLimit`. |
| The endpoint logs nothing (blind) | medium | Correct: AD-9 allows counts, sport and codes, and this is the one endpoint a file reaches. Patched. |
| The answer carries facts but not the codes that already name them (blind) | medium | Correct. Patched — the codes travel beside the numbers. |
| The commonest wrong drops have no tests (blind) | medium | Correct. Patched: an unknown track format, a track whose points collapse, and a FIT with no records. |
| The buffering test asserts configuration, not behaviour, in the wrong file (blind) | low | Correct: `BufferBody = true` would have kept it green. Moved and strengthened. |
| The new `FailureStatus` arm changes nothing (blind, verification) | low | Correct, and its neighbours exist to deviate from 400. Dropped. |
| A second FIT-activity generator landed in the API suite (blind) | low | Correct, and the csproj argues against exactly that. Moved into `SyntheticRun`. |
| `FitFixApi`'s summaries no longer describe what they do (blind) | low | Correct. Rewritten. |
| My Design Note claimed the length test would catch a sport-dependent spacing change (blind, verification) | low | Correct — it would not, with a running original. The note is fixed and the test now uses the ride. |
| Nothing bounds concurrent in-memory uploads (edge) | medium | Real, and the trade is deliberate: the old 64 KB threshold was what kept them off the heap. Deferred to Story 2.8, which measures the container's memory, since AD-11 keeps processing synchronous. |
| The fixtures are linked into a second project by source (blind) | low | Deferred: a shared fixture project is the usual shape but earns its churn only with a third consumer, and the frontend's tests will not be one. |
| An activity sent as `track` should be refused (my own matrix row) | false | A positioned FIT activity *is* a legitimate track — that is how the real case's `FFF.fit` arrived. Refusing it would refuse the file this project exists for. The row now names a file with no positions. |
