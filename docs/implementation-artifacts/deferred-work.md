# Deferred Work

Items split off from a story's scope, with the evidence that produced them.
Append-only: add entries, never rewrite existing ones.

- source_spec: spec-1-1-ci-regressions.md
  summary: Fail the build on compiler warnings and add a formatting gate (`TreatWarningsAsErrors`, `dotnet format --verify-no-changes`).
  evidence: Both projects enable nullable reference types, but nothing makes a nullability or analyzer warning fail CI, so the remaining Epic 1 rewrites can introduce them silently. Turning warnings into errors can break the build on an SDK bump, so it is a project-wide decision rather than part of the workflow this story adds.

- source_spec: spec-1-1-ci-regressions.md
  summary: Publish test results and coverage from CI (`--logger trx`, `--collect:"XPlat Code Coverage"`, `actions/upload-artifact`).
  evidence: A red run currently leaves only console output to diagnose with, and `coverlet.collector` is referenced by the test project but never collected. Both belong to a reporting decision that also has to say where coverage thresholds, if any, live.

- source_spec: spec-1-1-ci-regressions.md
  summary: Give the real-file regression test a visible skip or a committed synthetic equivalent.
  evidence: `tests/FitFix.Core.Tests/PrivateFixtureTests.cs` returns early when `tests/fixtures/private/` is absent, which is always the case in CI, so the one test asserting the 19.11.2026 case reports green while asserting nothing. The quiet exit is an agreed project convention; making the skip visible needs either a skip-capable xunit (the v3 migration already deferred) or a synthetic fixture reproducing the headline invariants.

- source_spec: spec-1-1-ci-regressions.md
  summary: Skip CI runs for documentation-only commits (`paths-ignore`).
  evidence: Every commit, including Markdown-only planning changes, currently spends runner minutes on a full build. Deferred because a run skipped by path never reports its status, which blocks a pull request once the check is required — the interaction has to be settled together with branch protection.

- source_spec: `spec-1-2-refusal-codes.md`
  summary: Pin the CLI's refusal contract with tests — exit code 2 and a stderr line carrying the code.
  evidence: No test executes `src/FitFix.Cli/Program.cs`; the only test project references `FitFix.Core` alone, so the exit codes and the printed code could change and CI would stay green. Deferred because `Program.cs` is top-level statements with local functions, so pinning it needs either a process-launching harness or a refactor that extracts the command into a testable type — both larger than this story.

- source_spec: `spec-1-2-refusal-codes.md`
  summary: Give the code registry the HTTP-status dimension AD-22 needs (400 for input, 413 for FILE_TOO_LARGE, 500 for INTERNAL_ERROR).
  evidence: Severity answers only the download gate, so the API shell will have to carry its own per-code status mapping — the second list of codes this story set out to remove. Deferred because the shell does not exist until Epic 3 and the mapping belongs with the story that writes `ProblemDetails`.

- source_spec: `spec-1-2-refusal-codes.md`
  summary: Declare a code for the malformed-multipart refusal AD-13 gives the shell.
  evidence: The registry claims to be the single source for shell codes but covers only `FILE_TOO_LARGE` and `INTERNAL_ERROR`; a request with the wrong part count has no code for `ProblemDetails.code`. Same reason as above — the shell is Epic 3.

- source_spec: `spec-1-2-refusal-codes.md`
  summary: `FitRoundTripTests.A_fit_without_records_is_rejected` rejects nothing.
  evidence: The test writes a one-record activity, reads it back successfully and asserts `Assert.Single(read.Records)`, so the `FIT_HAS_NO_RECORDS` throw site it appears to guard is never reached. Pre-existing, not caused by this story, but it is why that site looked covered. Fixing it needs a FIT file with zero record messages, which `SyntheticFitBuilder` cannot currently write.

- source_spec: `spec-1-3-thresholds-in-options.md`
  summary: Validate `AlignmentOptions` values and refuse impossible ones with a declared code.
  evidence: The record accepts values that break the core: `SpeedWindowHalfWidthRecords = -1` makes `ComputeSpeeds` read `times[-1]` (`IndexOutOfRangeException`, an uncoded exception out of the core, which AD-18 forbids); `0` collapses every repaired speed to zero; a NaN or negative `TrackMinSpacingMeters` turns de-duplication off; a huge `DeviceDistanceStallWindowSeconds` makes the backward scan in `KeepPower` quadratic. Nothing user-facing can reach these today — the CLI only ever passes `ForSport` — so this becomes real when the API builds options from a request DTO (AD-14). The validation and its codes belong with that story.

- source_spec: `spec-1-3-thresholds-in-options.md`
  summary: Bring the write step inside the composition root, or state why it stays outside.
  evidence: `RepairPipeline` composes read, align and repair, but the CLI still calls `FitActivityWriter.WriteFile` itself with its own `utcOffsetSeconds = 0`, so the write side keeps exactly the divergence AD-10 closed on the read side: an API passing a real offset writes a different file than the CLI for the same inputs. Deferred to Story 1.10, which derives the offset from the original activity (AD-19) and so removes the caller's choice altogether rather than moving it.

- source_spec: `spec-1-3-thresholds-in-options.md`
  summary: The CLI writes `local_timestamp` as UTC, so a Kyiv activity is 2–3 hours off, and there is no flag to set the offset.
  evidence: Making `utcOffsetSeconds` required removed the silent default but left the only shipping caller passing zero, and `PrintUsage` says nothing about it. Story 1.10 reads the real offset from the original's `ActivityMesg`; a `--utc-offset` flag is only worth adding if that story does not land first.

- source_spec: `spec-1-3-thresholds-in-options.md`
  summary: The spine's open question on AD-10 violations and the matching note in CLAUDE.md are stale — this story closed them.
  evidence: `ARCHITECTURE-SPINE.md` still carries "Чи порушення AD-10 у наявному коді виправляємо окремою сторі — Відкрите", naming `minSpacingMeters` and the four constants, and CLAUDE.md still says the existing code violates AD-10. Both are now wrong. Deferred rather than patched because the fix edits planning and agent-context files, which this workflow keeps out of a story's diff.

- source_spec: `spec-1-4-discard-garbage-points.md`
  summary: `/api/inspect` must clean the series itself, or cleaning has to move to the reading boundary.
  evidence: Cleaning lives at the top of `RepairService.Repair`, so every repair inherits it — but AD-15's second entry point reads a file and builds a preview series without repairing, and would draw a teleport on the map, against AD-25's "before any preview series". `GpsCleaner` is public, so the inspect story can call it; this entry exists so that obligation is not discovered late.

- source_spec: `spec-1-4-discard-garbage-points.md`
  summary: The spine's conventions table still mandates Ukrainian XML docs and comments.
  evidence: `ARCHITECTURE-SPINE.md` says code comments are Ukrainian, which every comment written since 2026-11-19 contradicts under the newer CLAUDE.md rule that artifacts are English. The binding contract should be updated to match, together with the stale AD-10 open question already recorded above.

- source_spec: `spec-1-4-discard-garbage-points.md`
  summary: Run the cleaner against the real 2026-11-19 pair once the private fixtures are in place.
  evidence: `tests/fixtures/private/` is empty on this machine, so `PrivateFixtureTests` exits quietly and the cleaner has never seen `Run_4k_Chabany.fit`. A non-zero discarded count on that file would be new information about the original case, and the only way to get it is to run it where the files are.

- source_spec: `spec-1-4-discard-garbage-points.md`
  summary: AD-25 in the spine no longer describes the cleaning rule that shipped, and CLAUDE.md still counts 24 invariants.
  evidence: AD-25 states the plain rule — a position is discarded when the speed to it exceeds the sport maximum — with no judging window, no leading-agreement vote and no stale bound, all three of which review measured as necessary. The binding contract should say what the code does; this is larger than the spine's other stale notes already recorded above, because a future story would re-derive the wrong rule from it. CLAUDE.md's "24 інваріанти (AD-1…AD-24)" predates AD-25.

- source_spec: `spec-1-4-discard-garbage-points.md`
  summary: `ImpossibleSpeedJudgedOverSeconds` is the same for every sport.
  evidence: The judging window multiplies the sport cap, so one 1 Hz step may be 128 m for cycling and 78 m for `Other`, against 38 m for running. Nothing is known to be wrong, but `ForSport` tunes the cap and not the window, so the per-sport argument is half-applied and no measurement says what cycling's per-step allowance should be. Worth revisiting once a real ride with broken GPS exists to measure against.

- source_spec: `spec-1-5-pauses-do-not-inflate.md`
  summary: The report's pace still divides by elapsed time; move it onto moving time with the rest of the summaries.
  evidence: `RepairedPaceSecondsPerKm` and `OriginalPaceSecondsPerKm` are computed properties over `ElapsedSeconds`, and `MeasuredFraction` likewise. Story 1.5 added the paused seconds to the report so the contradiction is at least visible, but the formulas belong with Story 1.7, which gives the summaries one owner and takes the derived values out of the contract (AD-14, AD-16).

- source_spec: `spec-1-5-pauses-do-not-inflate.md`
  summary: Anchors can be seated inside a pause, and AD-21 asks for smoothing to stop at anchors as well.
  evidence: `Aligner` is untouched by Story 1.5, so a device that keeps emitting moving positions while the timer is stopped can place an anchor inside the interval; distance then holds flat relative to that anchor rather than to the record that opened the pause. AD-7 says the alignment accrues no distance inside an interval, which is only true of the interpolation today. Both belong with the anchor rework (the DP/LIS chain selection noted in CLAUDE.md), which will move the pinned anchor counts anyway.

- source_spec: `spec-1-5-pauses-do-not-inflate.md`
  summary: A repaired file carries no pauses of its own, so repairing a repaired file re-inflates the distance.
  evidence: The writer emits only `timer start` and `stop_all`, because AD-19 pins the message order and has no slot for pause events. A test now asserts the loss rather than noting it, which is honest about today but locks it in. Story 1.10 owns the written structure and should decide whether pause events join the order; the spine's own open question about one file in both roles is the same question.

- source_spec: `spec-1-5-pauses-do-not-inflate.md`
  summary: Average heart rate still includes the records recorded while the athlete stood still.
  evidence: `ApplyAverages` averages heart rate over every record, pauses included, so a five-minute wait pulls the average down. Story 1.7 owns the summaries and is where the moving-time denominator belongs for every averaged field, not only speed.

- source_spec: `spec-1-5-pauses-do-not-inflate.md`
  summary: AD-7 and AD-19 in the spine do not yet describe what pauses did to the model and the written file.
  evidence: AD-7 says the alignment accrues no distance inside an interval, which the interpolation now honours but the anchor search does not; AD-19's fixed message order has no slot for pause events, so "the written file gets no new event messages" is a conflict with the invariant rather than a deferral. Both need the same treatment AD-25 got after Story 1.4, and the architecture memlog still carries A-06 and A-07 as open hostile-review findings.

- source_spec: `spec-1-5-pauses-do-not-inflate.md`
  summary: The suite fails intermittently, and the FIT encoder may not be thread-safe.
  evidence: One run in ten failed with a single test red; nine reruns, including two straight after a full rebuild, were green, and the failing test's name was not captured. Separately, the implementer measured `synthetic_track.fit` coming out with different bytes depending on whether the export test runs alone or the whole suite runs in parallel — three distinct hashes — and reproduced it on an untouched baseline tree, which points at shared state inside the FIT SDK's encoder rather than at any story. That matters beyond the fixture: the API will encode files concurrently (AD-3), so the writer has to be proven safe under parallel use before Epic 2 ships. Until then a red CI run may be this flake rather than a regression.

- source_spec: `spec-1-6-laps-survive.md`
  summary: Lap distances, timer times and averages are computed in the writer, where AD-16 and AD-24 both forbid arithmetic.
  evidence: AD-16 says every summary is computed in the core and that the writer and the DTOs transcribe finished values with no arithmetic of their own; AD-24 names `ActivitySummary` as the destination for recomputed lap distances. `ActivitySummary` does not exist yet, and Story 1.6 put the lap arithmetic in `FitActivityWriter` because Story 1.7 is the story that moves it. Half of AD-24 is therefore deferred to 1.7, not satisfied by 1.6 — the spine's own citation should say so.

- source_spec: `spec-1-6-laps-survive.md`
  summary: A lap boundary between records is measured at the nearest record, which under smart recording can misattribute tens of metres.
  evidence: The lap's span is written as the athlete's exact instant while its distance is read at the nearest repaired record. At 1 Hz the error is sub-second; with smart recording (a point every 4–10 s) one lap can gain or lose tens of metres against its neighbour, and the written `avg_speed` disagrees slightly with `distance / elapsed`. Interpolating the distance at the exact boundary instant would remove it, at the cost of a lap boundary that no record sits on.

- source_spec: `spec-1-4-discard-garbage-points.md`
  summary: The leading vote is still quadratic when a garbage run jitters beyond a few metres.
  evidence: The step-over that made an identical-repeat run linear (2 000 / 7 200 / 20 000 records: 0.017 / 0.213 / 1.631 s before, 0.002 / 0.002 / 0.005 s after) cannot apply when a run's own spatial extent approaches `StallRadiusMeters`, because its distant members genuinely do vote and the verdict depends on them. The same sizes with 5 m of jitter per axis still cost 0.011 / 0.156 / 1.234 s. A bounding box instead of a scalar reach would buy jitter up to about 4 m and nothing beyond. At the two-hour SM-3 size the jittering case is 0.156 s locally, so the budget holds today; it is the twelve-hour all-garbage file that would not.

- source_spec: `spec-1-7-one-owner-summaries.md`
  summary: An activity whose every second exceeds its sport cap states no maximum speed at all.
  evidence: The maximum skips records above `AlignmentOptions.MaxSpeedMetersPerSecond`, which is what keeps the forced finish (measured at 7.8 m/s on a 3 m/s run) out of it. If every record is above the cap — a ride recorded under the wrong sport, or a descent judged by the running profile — the summary states no maximum rather than a wrong one. That is the honest answer, but the user is told nothing; once the API carries warnings, a code saying the sport profile does not fit the data would be better than silence.

- source_spec: `spec-1-7-one-owner-summaries.md`
  summary: The written file now carries `total_descent` and `max_cadence`, which no story asked for.
  evidence: They follow from AD-16's "one owner computes every summary" and from this spec's own field list, and both are free in the same pass as the values the story needed. Worth a glance from Ілля: three fields new to the file in two stories (`max_speed`, `total_ascent`, then these) change what Strava displays, and only the first two were reviewed as part of a story's intent.

- source_spec: `spec-2-1-problem-details.md`
  summary: CLAUDE.md still says the API project does not exist.
  evidence: It reads "ASP.NET Core Minimal API (ще не створено)" and lists the projects as Core, Cli and Core.Tests with "Далі — `src/FitFix.Api`". Both are wrong now that `src/FitFix.Api` and `tests/FitFix.Api.Tests` are in the solution, and that file is the first thing the next session reads. Deferred only because this workflow keeps agent-context files out of a story's diff; it belongs in the next docs commit together with the spine's AD-7 and AD-19 updates.

- source_spec: `spec-2-1-problem-details.md`
  summary: A 413 written before the body is drained may never reach the client.
  evidence: The refusal is written as soon as a declared `Content-Length` exceeds the limit, so a client still uploading can hit a write error and never read the response body. Draining the body first, or closing the connection deliberately, is the usual remedy; both cost something and the right choice depends on how the browser behaves against the real endpoint, which Story 2.3 is where that can be measured.

- source_spec: `spec-2-2-inspect-endpoint.md`
  summary: Nothing bounds how many uploads are buffered in memory at once.
  evidence: `MemoryBufferThreshold` is raised to the upload limit so no part ever spills to a temp file, which AD-3 requires and Story 2.8's read-only container needs — but the old 64 KB default was also what kept concurrent uploads off the heap. Several 20 MB requests at once now sit in memory together. AD-11 keeps processing synchronous inside the request, so the remedy is a concurrency gate rather than a queue; the number belongs with the story that measures the container's own memory, which is 2.8.

- source_spec: `spec-2-2-inspect-endpoint.md`
  summary: The synthetic fixtures are linked into a second test project by source.
  evidence: `tests/FitFix.Api.Tests` compiles `SyntheticRun.cs` and `SyntheticFitBuilder.cs` from the core suite so the two projects cannot drift, at the cost of duplicating both types into a second assembly. A small `tests/FitFix.TestFixtures` project is the usual shape and becomes worth the churn when a third suite needs them — the frontend's own tests in Epic 2 will not, so this can wait.

- source_spec: `spec-1-8-over-long-track.md`
  summary: A speed sensor declared over classic Bluetooth, or with no `source_type` at all, is read as no sensor.
  evidence: `FitActivityReader` asks the FIT SDK for `antplus_device_type` and `ble_device_type`, which are subfields the SDK resolves only when `device_info` states `source_type` as `antplus` or `bluetooth_low_energy`. A message with `source_type = bluetooth`, or with a bare `device_type` of 124 and nothing else, therefore declares nothing, and the chosen length falls back to the sport's speed cap. Deliberate for now: the fallback is generous, while reading a bare `device_type` as an ANT+ profile risks believing a device distance nothing vouched for — the mistake that would have trimmed the real 6.5 km Chabany run to 3.9 km. Becoming real needs a file from a watch that writes sensors that way, which nobody has yet.

- source_spec: `spec-1-8-over-long-track.md`
  summary: The track-length tolerance — a 200 m floor plus 2 % of the plausible distance — is a judgement nobody has checked against real files.
  evidence: No planning document fixes either number. They were chosen between two measured scales: a declared footpod and a correctly drawn route disagree by a percent or two (65–200 m on a 6.5 km run, 1–2 km on a 100 km ride, neither of which may raise a blocking warning), while an extra lap in a route editor is hundreds of metres at least. The whole of Epic 1 runs on one synthetic course and one absent private pair, so the first real evidence arrives with the browser service of Epic 2. Worth a glance from Ілля: too low teaches the athlete to click through the one blocking warning that changes the result, and the floor also widens the finish-line budget in `Aligner.EndingAt`.

- source_spec: `spec-1-8-over-long-track.md`
  summary: The CLI signals an unconfirmed blocking warning with exit code 3 instead of withholding the file.
  evidence: AD-22 gates the download on an explicit confirmation, which a command-line tool has nobody to ask for; the file is written and the exit code carries the gate instead. A script can act on it, but a person who ignores the warning text has a trimmed repair on disk that looks like any other. The real gate belongs to the browser service (Story 2.3 onwards), where there is a user to ask; whether the CLI should also grow a `--confirm`-style flag, or write to a temporary name until confirmed, is a product decision for Ілля rather than part of this story.

- source_spec: `spec-2-3-repair-endpoint.md`
  summary: The FIT encoder is not thread-safe, now observed twice, and the API encodes concurrently.
  evidence: A test asserting that a sensor field is absent passes alone and fails in the full suite: a FIT written by the fixture reads back `power = 65535`, the `ushort` sentinel, when other test classes encode at the same time — and the core reads the same value, so the corruption is in the written bytes. Story 1.5 saw the same shape as unstable fixture hashes. `/api/repair` encodes a file per request, so two simultaneous repairs are the production form of this. It has to be settled before the service is exposed to more than one user at a time: either the encoder is used under a lock, or each request gets its own instance if that is what the SDK requires.

- source_spec: none
  summary: The map should colour the repaired line per trust zone, not as one accent polyline.
  evidence: Ілля asked for the corrected stretches to read differently from the ones the watch got right (23.11.2026). Story 3.2's criteria give the map one solid `accent` line for the repaired track and a dashed `original` one, and only Story 3.3's zone bar under the chart splits `accent` from `estimated` by interval. Since Story 1.12 that boundary is exactly where the geometry changes source, and `/api/repair` already returns `zones`, so the map can segment the polyline the same way the bar does. A colour may not be the only carrier (UX-DR), so it needs the legend and the marker Story 3.2 already places at the end of the last measured zone.

- source_spec: none
  summary: Repair only the broken stretch from a fragment of a track, instead of requiring a reference track for the whole route.
  evidence: Ілля's case (23.11.2026): 50 km run, GPS dies over the last 5 km, and today the only way through is to draw the entire 50 km route. Story 1.12 already made the machinery half-ready — measured zones keep the watch's own geometry and an estimated zone takes an increment between its bounding anchors — so a fragment covering just the gap is the natural input. What is not ready: the anchor search assumes the track spans the activity, so a fragment leaves most GPS points without a projection and would raise FEW_ANCHORS or TRACK_FAR_FROM_ORIGINAL; AD-17's length plausibility compares a whole track against the whole activity and means nothing for a piece; both ends of the fragment have to anchor to real positions (or to the activity's end) or the seam jumps; and the interface has to let somebody say "this is a piece" or detect it from the anchors clustering in one contiguous window. Needs grooming into stories before it is built.

- source_spec: none
  summary: The partial-track idea above is groomed: it is Epic 5 in the epics file, four stories, with AD-27 in the spine.
  evidence: Ілля settled the three open questions on 23.11.2026 — the service detects a fragment itself and then asks for confirmation before using it (a blocking warning, as an over-long track already is); the first version covers a fragment anywhere in the activity, not only a tail; and the fragment may arrive either as a file or, once Epic 3's map exists, drawn on it. The fourth unknown, what an anchor search does when the track deliberately does not span the activity, is answered by AD-27: the fragment attaches by Anchors at its own ends, falling back to the activity's start or finish where no true position survives on that side, and a fragment that attaches at neither end is a refusal rather than a guess.

- source_spec: `spec-5-2-confirm-fragment.md`
  summary: The download gate covers one blocking code, where AD-22 asks for all of them.
  evidence: `Result.tsx` withholds the file for `TRACK_IS_A_FRAGMENT` only, by matching that code; `TRACK_LONGER_THAN_ACTIVITY` and `TRACK_FAR_FROM_ORIGINAL` are still shown beside an offered download, and a test now pins that. Deliberate, and this spec's own Design Notes say why: the over-long track's confirmation is Story 1.8's and its screen is 3.5's, so a gate built for both would ship half of a story nobody has approved. Whoever builds 3.5 should generalise the gate rather than add a second code to the match.

- source_spec: `spec-5-2-confirm-fragment.md`
  summary: Neither shell can say "this is the whole route" — only the API can.
  evidence: `TrackCoverageMode.WholeRoute` exists in the core and `trackCoverage=whole` in the API, because a lap course whose fragment-looking track is genuinely the route is a real answer. The CLI grew only `--trust-fragment` and the result screen only the confirmation, both as this spec's tasks list them, so a misdetected whole route cannot be repaired as one from the command line or the browser. Measured on the real pair: `trackCoverage=whole` over a 1235 m fragment spreads it across the whole 34-minute activity and raises `FEW_ANCHORS`, which is the honest consequence of insisting — so the escape is worth offering only together with words that explain it (Story 5.3's screen is where the stretch is already named).

- source_spec: `spec-5-2-confirm-fragment.md`
  summary: Confirming a fragment repairs the same two files a second time, at the full cost.
  evidence: Nothing is stored (AD-3), so the confirmation is the same request with `trackCoverage=fragment` and the service repeats read, align and repair — three seconds for a two-hour activity, by Epic 2's own budget, for an answer whose bytes are identical (verified on the real pair: the same `fitBase64`). The over-long track's confirmation genuinely changes the result, which is why the mechanism was built this way; this one does not. A client could keep the first answer's file and only drop the warning, but that would be the interface deciding a repair's warnings for itself, which AD-22 puts on the server. Worth revisiting if the repeat ever becomes slow enough to notice.

- source_spec: `spec-5-2-confirm-fragment.md`
  summary: Correction to the entry above: the result screen does offer the whole-route answer, so only the CLI lacks it.
  evidence: The entry recorded during Story 5.2's implementation says a misdetected whole route "cannot be repaired as one from the command line or the browser". The browser half is false as shipped: the fragment gate carries both answers, and pressing «Ні, це цілий маршрут» repeats the repair with `trackCoverage=whole`. A question with only a yes is not a question, which is why it was added before the story closed. What remains true is the command line: `fitfix repair` has `--trust-fragment` and no `--whole-track`, so a lap course the detector reads as a stretch of itself can only be forced through the API or the browser. This file is append-only, so the correction is an entry of its own rather than an edit.

- source_spec: `docs/implementation-artifacts/spec-3-2-route-map.md`
  summary: A basemap with a dark style and no key would let the map drop the CSS inversion of OpenStreetMap tiles.
  evidence: CARTO's keyless dark tiles serve an "API KEY REQUIRED" watermark (seen in a browser, 2026-11-27), so 3.2 darkens OpenStreetMap with a CSS filter. The inversion leaves faint seams at tile borders and shifts colours; a keyed dark source or a self-hosted style would fix both, at the price of configuration the one-command setup does not have today.

- source_spec: none
  summary: A "No track" entry: repair an original without any reference file, drawing the whole missing stretch on the map.
  evidence: Split from Story 5.4 on 2026-11-28 at Ілля's request ("a button like 'missing', so no GPX has to be uploaded"). Story 5.4 takes the entry through a result made from a raw track; repairing with no track at all needs an API path that accepts an original alone and an estimated zone with no geometry until the drawing supplies it, which is wider than the drawing itself.

- source_spec: none
  summary: Drop outliers on the map, and edit the track there, beyond appending a drawn stretch.
  evidence: Split from Story 5.4 on 2026-11-28 (Ілля: "then we throw out the outliers on the map and let the track be edited on the map"). Needs a way to select or flag points of the original or the track and a contract for what the server does with the removal; AD-14 forbids deriving it in the UI.

- source_spec: none
  summary: Snap a drawn route to roads, with a switch to turn the snapping off.
  evidence: Ілля on 2026-11-28: snapping is wanted but must be switchable, and Story 5.4 ships without it. The PRD (section 6.2) and the spine put road snapping at v1.2 because it needs an external routing service, which sends the athlete's coordinates off the service (AD-9) and strains the stateless promise; decide the provider and the privacy position before building it.

- source_spec: `spec-5-4-draw-stretch.md`
  summary: Choose which estimated stretch to draw when a run has several.
  evidence: `stretchEnds` takes the first estimated zone that follows a measured one. The real Chabany pair has three estimated zones (21-179 s, 1698-1903 s, 1930-2009 s), so only the first can be redrawn from the map, and after a drawing it stays estimated and first. Needs a way to pick a stretch (click it, or a list), and a decision on whether one drawing request can carry several fragments, which the server's single-fragment attachment (AD-27) does not do today.
