---
title: 'Story 1.3 — No threshold outside the options object'
type: 'refactor'
created: '2026-11-20'
status: 'done'
route: 'dispatch'
review_loop_iteration: 0
baseline_commit: '9518f0f1da7c2e418119978dd17b6d587dbda103'
context: ['{project-root}/docs/implementation-artifacts/epic-1-context.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Five tuning numbers still live outside `AlignmentOptions` — the track de-duplication spacing as a default parameter on a public method, the device-distance window, its growth epsilon, the speed-estimation window, and the minimum anchor count — and three public core methods carry numeric or optional defaults (`minSpacingMeters = 0.3`, `utcOffsetSeconds = 0`, `options = null`). Two entry points can therefore produce different distances from the same two files while both look correct, which is what AD-10 exists to prevent.

**Approach:** Every tuning number moves into `AlignmentOptions` with its current value, so no expected test number changes. No public core signature keeps a default: where a caller legitimately has nothing to say, an explicit overload says it instead. A single composition root in the core performs read → align → repair, so the CLI and the future API cannot assemble the pipeline differently, and a test can prove the two agree.

</frozen-after-approval>

## Code Map

- `src/FitFix.Core/AlignmentOptions.cs` -- the destination. Twelve thresholds already live here with `ForSport`; the five new ones join them, keeping their present values.
- `src/FitFix.Core/ReferenceTrack.cs:39` -- `FromPoints(IEnumerable<TrackPoint>, double minSpacingMeters = 0.3)`: the numeric default the story names first. Line 48 uses it in the de-duplication loop.
- `src/FitFix.Core/Tracks/TrackReader.cs:20,44` -- `ReadReferenceTrack` and `ReadReferenceTrackFile` carry the same `minSpacingMeters = 0.3` default and forward it at line 23.
- `src/FitFix.Core/RepairService.cs:10,13` -- `DeviceDistanceStallWindowSeconds = 10` and `SpeedWindowRecords = 3` as private constants; line 185 holds a third, the `> 0.5` metre growth test that decides whether the device's own distance moved.
- `src/FitFix.Core/RepairService.cs:15` -- `Repair(original, track, AlignmentOptions? options = null)`: the optional parameter whose `null` branch silently applies `ForSport`.
- `src/FitFix.Core/Aligner.cs:71` -- `anchors.Count < 4`, the minimum anchor count, as a literal.
- `src/FitFix.Core/Fit/FitActivityWriter.cs:13,48,54` -- three public methods with `int utcOffsetSeconds = 0`. The real offset comes from the original activity, which is Story 1.10; this story only removes the silent default.
- `src/FitFix.Cli/Program.cs:59-63` -- the CLI's composition: read original, read track, repair, write. This is the code the composition root replaces.
- `tests/FitFix.Core.Tests/*.cs` -- roughly fifteen call sites of `RepairService.Repair`, `ReferenceTrack.FromPoints` and the writer. They must keep their present expected numbers.

## Tasks & Acceptance

**Execution:**
- [x] `src/FitFix.Core/AlignmentOptions.cs` -- add `TrackMinSpacingMeters` (0.3), `DeviceDistanceStallWindowSeconds` (10), `DeviceDistanceGrowthMeters` (0.5), `SpeedWindowHalfWidthRecords` (3), `MinimumAnchorCount` (4), each with an XML doc naming what it governs. -- one place to read a threshold from.
- [x] `src/FitFix.Core/ReferenceTrack.cs`, `src/FitFix.Core/Tracks/TrackReader.cs` -- replace the `minSpacingMeters` parameter with a required `AlignmentOptions`. -- removes the numeric default the story names.
- [x] `src/FitFix.Core/RepairService.cs`, `src/FitFix.Core/Aligner.cs` -- read all five values from the options object; delete the private constants and the literal. -- no threshold outside the options.
- [x] `src/FitFix.Core/RepairService.cs` -- split `Repair` into two overloads with no optional parameter: `Repair(original, track, options)` and `Repair(original, track)`, the latter deriving options from the activity's sport in one place. -- a signature cannot be called two ways that mean different things.
- [x] `src/FitFix.Core/Fit/FitActivityWriter.cs` -- make `utcOffsetSeconds` required on all three methods. -- the caller states the offset instead of inheriting a silent zero.
- [x] `src/FitFix.Core/RepairPipeline.cs` -- new. One static class composing read original, read track with the same options, repair; overloads with and without explicit options, no defaults. -- the single path both shells take, so they cannot diverge.
- [x] `src/FitFix.Cli/Program.cs` -- call the composition root instead of assembling the steps itself. -- the CLI stops being a second pipeline.
- [x] `tests/FitFix.Core.Tests/*.cs` -- update call sites to pass options or use the sport-derived overload. Do not change an expected number; if one moves, the port is wrong. -- the refactor must be behaviour-preserving.
- [x] `tests/FitFix.Core.Tests/AlignmentOptionsTests.cs` -- new. Cover the I/O matrix below. -- the invariant needs a net, not a convention.

**Acceptance Criteria:**
- Given the core assembly, when every public method is inspected by reflection, then none declares an optional parameter.
- Given the whole existing suite, when it runs after the refactor, then every test passes with its present expected numbers.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Defaults preserve behaviour | the synthetic run, repaired through the sport-derived overload | the same distance, elapsed time and anchor count as before the refactor | N/A |
| One pipeline, one answer | the same two files through the composition root and through a hand-assembled read/align/repair with the same options | identical repaired distance to the metre | N/A |
| Thresholds actually read | a non-default `TrackMinSpacingMeters` (e.g. 50 m) | the track keeps measurably fewer points than with 0.3 m | N/A |
| A threshold changes the result | a non-default `MinimumAnchorCount` above the run's anchor count | the report carries `FEW_ANCHORS` where the default did not | N/A |
| No public default survives | the core assembly by reflection | no public method declares an optional parameter | the test names each offender it finds |
| Options are mandatory where they matter | `ReferenceTrack.FromPoints` and the track readers | the signature requires `AlignmentOptions`; passing null throws | `ArgumentNullException` |

## Verification

**Commands:**
- `dotnet build -c Release` -- expected: no errors, no new warnings
- `dotnet test -c Release` -- expected: all tests pass, no existing expected number edited
- `grep -rn "= 0.3\|= 0.5\|SpeedWindowRecords\|DeviceDistanceStallWindowSeconds" src/FitFix.Core --include "*.cs"` -- expected: matches only inside `AlignmentOptions.cs`

## Design Notes

The story's second criterion asks for a test proving the CLI and a direct core call agree on distance for the same files. Without a shared composition root such a test can only duplicate the CLI's four lines and compare them with themselves, which proves nothing. Hence `RepairPipeline`: the CLI calls it, the API will call it, and the test compares it against a hand-assembled pipeline using the same options. It composes and nothing more — no logic of its own, no HTTP, no logging (AD-1).

Overloads rather than optional parameters: `Repair(original, track)` says "derive the options from this activity's sport" in its signature, and `ForSport` stays the single place that decision is made. An optional parameter would let the same call mean two things depending on what the caller omitted, which is the divergence AD-10 forbids.

## Implementation Notes

- The five thresholds live in `AlignmentOptions` with their former values, and the reflection test proves no public core method keeps an optional parameter. The implementer showed that test is not theatre by adding a public method with an optional parameter to `Geo` and watching it fail by name.
- `ForSport(SportKind.Running)` now returns the record defaults, so the 7 m/s running cap is written once instead of twice. That also made the sport derivation testable: a ride at 15 m/s keeps 433 anchors under the cycling cap and 2 under the running one.
- **An activity with no elapsed time is now refused with a code.** Records that all share one timestamp left a single anchor, so `Math.Clamp(index, 0, anchors.Count - 2)` became `Math.Clamp(index, 0, -1)` and threw a bare `ArgumentException` — which this story's new CLI arm would have reported as "Хибний шлях до файлу" for what is a broken file. `ACTIVITY_WITHOUT_ELAPSED_TIME` is declared `blocking` and a test pins the code rather than the framework exception.
- **The CLI's default output name was computed before the `try`.** `Path.GetFullPath("")` therefore crashed with a stack trace and made the `ArgumentException` arm unreachable, along with the new path validation in `RepairPipeline`. The three lines moved inside the `try`.
- The CLI now also handles `DirectoryNotFoundException` and `UnauthorizedAccessException`: `-o /nope/out.fit` prints one line and exits 2.
- **The write step stays outside the composition root** and the CLI still passes `utcOffsetSeconds = 0` itself. Story 1.10 reads the real offset from the original's `ActivityMesg`, which removes the caller's choice rather than moving it; until then a Kyiv activity's `local_timestamp` is UTC. Recorded in `deferred-work.md`.
- **Verified independently of the implementation report:** `dotnet build -c Release` 0 warnings; `dotnet test -c Release` 57 passed (43 before the story, 52 after the first pass, 57 after the review patches). CLI: an empty path with no `-o`, a missing output directory, and a one-point track each print one line and exit 2; the synthetic pair still repairs 3.90 km to 6.50 km in 36:00 with 1129 anchors.

## Review Triage Log

Review pass 1 — three layers (blind hunter 10, edge cases 9, verification gaps 7). All three were cut off mid-run by the account's five-hour limit and resumed from where they stopped rather than restarted. Duplicates are logged once, naming the layers that raised them.

| Finding | Verdict | Evidence and route |
|---|---|---|
| Both device-distance thresholds are read with nothing observing either (blind, verification) | high | The verification layer swapped the two reads in a scratch copy and all 52 tests still passed. Patched with a test that counts surviving zero-power records; the swap now fails it. |
| The repaired speed series is pinned by no test at all (verification) | high | Demonstrated: hard-coding a 20× wider window in place of the option kept the suite green, because nothing read `RepairedRecord.Speed`. Patched — the series is pinned and a wide window must change it. |
| The sport derivation of both new entry points is unverified (verification, blind) | high | Demonstrated: replacing `ForSport(...)` with `new AlignmentOptions()` in both kept the suite green, since every fixture was running and the running profile equalled the defaults. A ride could have been aligned with the 7 m/s cap. Patched with a cycling case. |
| An activity whose records share one timestamp throws a bare `ArgumentException` (edge) | high | Verified by reading the code: one anchor makes `Math.Clamp(index, 0, -1)` throw, and this story's new CLI arm would label it "Хибний шлях до файлу". Patched — refused with `ACTIVITY_WITHOUT_ELAPSED_TIME`. |
| `ForSport(Running)` duplicates the record's own default (blind, verification) | medium | Real: the 7 m/s cap was written twice and nothing would catch them drifting apart. Patched so the running branch is the defaults. |
| `RepairPipeline.RepairFiles(string, string)` validates nothing while its three siblings validate everything (blind, edge) | medium | Real inconsistency in the one class whose point is a single way to do this. Patched. |
| The CLI leaves `DirectoryNotFoundException` and `UnauthorizedAccessException` uncaught (edge) | medium | Real and easy to meet — `-o` into a directory that does not exist. Patched, exit 2. |
| `AlignmentOptions` accepts values that break the core (blind, edge ×4) | medium | Real mechanically: `SpeedWindowHalfWidthRecords = -1` reads `times[-1]`; `0` zeroes every speed; a NaN spacing disables de-duplication; a huge stall window makes the scan quadratic. Nothing user-facing can reach any of them today — the CLI only passes `ForSport`. Deferred to the Epic 3 story that builds options from a request DTO, where validation and its codes belong. |
| The composition root stops before the write, so the CLI still assembles that step with its own offset (blind, edge) | medium | Real: AD-10's divergence is closed on the read side and open on the write side. Deferred to Story 1.10, which derives the offset from the original and so removes the choice instead of relocating it. |
| The written `local_timestamp` is UTC, so a Kyiv activity is 2–3 hours off, with no flag to set it (blind) | medium | Real and user-visible. Same deferral: Story 1.10 reads the offset from the original's `ActivityMesg`. |
| The anchor-count test demands a pinned `1129 + 1` (blind) | low | Real: a legitimate rise in the synthetic run's anchor count would fail the test for an unrelated reason. Patched to derive the threshold from the reported count. |
| The spine's AD-10 open question and CLAUDE.md's matching note are stale (blind) | low | Correct — this story closes both. Deferred because the fix edits planning and agent-context files, which this workflow keeps out of a story's diff. |
| The CLI's composition is executed by no test (verification) | medium | Real: swapping the two path arguments would ship green. Already recorded from Story 1.2 — `Program.cs` is top-level statements, so pinning it needs a process harness or an extracted command type. Left on that entry. |
| The reflection guard catches optional parameters but not a threshold returning as a literal (blind) | low | Rejected: reflection cannot see inline literals, which is the actual risk, and a test over private constants would need an allowlist (`Geo`, the FIT header sizes in `TrackReader`) that rots. Left as a review-time concern. |
| The CLI's error ordering changed — both files are opened before the original is decoded (verification) | low | Rejected: for an unreadable original plus a missing track the message changes but the exit code stays 2, and preserving the old order would mean restructuring the root for no user-visible gain. |
| The spec's third verification grep cannot come back clean (blind, edge, verification) | low | Correct and harmless: `RepairService` now mentions `DeviceDistanceStallWindowSeconds` by name twice, which is the point of the refactor. No numeric literal survives outside `AlignmentOptions`. |
| New CLI behaviour ships with no deferred-work entry (blind) | false | The entry existed before the finding was filed — the CLI's untested refusal path was recorded during Story 1.2 and the write-side entries were appended during this triage. |
| Spec frontmatter says `in-progress` while the tracker says the same (blind) | false | Not a disagreement: the frontmatter tracks the build workflow's state and the tracker the story's state in the sprint. Both moved on completion, after the diff the reviewers read. |
