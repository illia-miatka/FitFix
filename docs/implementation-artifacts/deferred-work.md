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
