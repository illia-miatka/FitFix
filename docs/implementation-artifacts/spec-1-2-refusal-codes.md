---
title: 'Story 1.2 — Every core refusal carries a code'
type: 'refactor'
created: '2026-11-19'
status: 'done'
route: 'dispatch'
review_loop_iteration: 0
baseline_commit: 'e02978923f61ab14d01193ba5e34376fbbc04bf1'
context: ['{project-root}/docs/implementation-artifacts/epic-1-context.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Core refusals currently carry Ukrainian sentences and nothing else: `InvalidFitFileException("Це не схоже на FIT-файл.")`, and two `ArgumentException`s thrown over user data. A shell has nothing to put in `ProblemDetails.code`, and the UI cannot decide whether a failure blocks a download or merely informs. Warning codes exist as four bare constants with no severity attached, declared next to the alignment model rather than in a registry.

**Approach:** One registry in the core declares every warning and error code together with its severity (`blocking` or `info`). Every domain exception inherits `FitFixException` and carries its code; the two `ArgumentException`s over user data become domain exceptions. Core messages become developer-facing English — human text belongs to the UI dictionary and to the CLI report in Story 1.11.

**Decided severities:** `TRACK_FAR_FROM_ORIGINAL` is `blocking` and `NO_ORIGINAL_GPS` is `info` because the acceptance criteria say so. `GPS_STALL_DETECTED` and `FEW_ANCHORS` are both `info`: a stalled GPS is the normal case this service exists for, and few anchors is a milder version of no anchors at all, so marking it `blocking` would treat a better input more harshly than a worse one.

</frozen-after-approval>

## Code Map

- `src/FitFix.Core/Alignment.cs:6-13` -- `RepairWarnings`, four codes as bare constants, no severity. The string values are asserted by tests and must not change; the declaration moves to the registry.
- `src/FitFix.Core/Aligner.cs:20,22,26,70` -- the only sites that raise warnings. Call sites change name only.
- `src/FitFix.Core/Fit/FitDecoder.cs:17,21,33` and `src/FitFix.Core/Tracks/GpxTrackReader.cs:23,50` -- throws plus the two exception declarations, both inheriting plain `Exception`.
- `src/FitFix.Core/Fit/FitActivityReader.cs:47`, `Fit/FitTrackReader.cs:31`, `Tracks/TcxTrackReader.cs:22`, `Tracks/TrackReader.cs:38,64` -- remaining throws, one code each.
- `src/FitFix.Core/ReferenceTrack.cs:53`, `src/FitFix.Core/Fit/FitActivityWriter.cs:19` -- the two `ArgumentException`s over user data that AD-18 forbids.
- `src/FitFix.Cli/Program.cs:67-70` -- catches the three types by name, prints `exception.Message`.
- `tests/FitFix.Core.Tests/FitRoundTripTests.cs:66`, `TrackReaderTests.cs:104,111` -- `Assert.Throws` on both exception types: keep both names so these compile.
- `src/FitFix.Core/RepairReport.cs:17` -- `Warnings` stays `IReadOnlyList<string>`; the DTO shape belongs to Story 1.7 and Epic 3.

## Tasks & Acceptance

**Execution:**
- [x] `src/FitFix.Core/Diagnostics/DiagnosticCodes.cs` -- new. Declare every code once with its severity: the four warnings, `TRACK_LONGER_THAN_ACTIVITY` (AD-17, `blocking`), one code per error throw site, and the two codes the shell raises (`FILE_TOO_LARGE`, `INTERNAL_ERROR`) so AD-22 has one source. Expose the full set and a severity lookup. -- AD-18 allows exactly one declaration site, and AD-22 reads its codes from it.
- [x] `src/FitFix.Core/Diagnostics/FitFixException.cs` -- new. Base exception carrying `Code`, with `InvalidFitFileException` and `InvalidTrackFileException` derived from it, plus a type for refusals over malformed arguments. -- a shell fills `ProblemDetails.code` from any core failure without a type switch.
- [x] `src/FitFix.Core/Fit/FitDecoder.cs`, `src/FitFix.Core/Tracks/GpxTrackReader.cs` -- delete the old exception declarations; the types now live in `Diagnostics`. -- not one declaration per reader.
- [x] `src/FitFix.Core/Fit/*.cs`, `src/FitFix.Core/Tracks/*.cs` -- every throw passes its code; messages become English developer text. -- AD-18: a code, not a Ukrainian sentence.
- [x] `src/FitFix.Core/ReferenceTrack.cs`, `src/FitFix.Core/Fit/FitActivityWriter.cs` -- replace both `ArgumentException`s with domain exceptions carrying codes. -- user data must not surface as an argument error.
- [x] `src/FitFix.Core/Alignment.cs`, `src/FitFix.Core/Aligner.cs` -- warning constants now come from the registry; `RepairWarnings` is removed or forwards to it. -- removes the second list of codes.
- [x] `src/FitFix.Cli/Program.cs` -- catch `FitFixException` and print its code with the developer message. -- human text is Story 1.11; until then the code is the honest output.
- [x] `tests/FitFix.Core.Tests/DiagnosticCodesTests.cs` -- new. Cover the I/O matrix below. -- the registry is a contract three layers depend on.

**Acceptance Criteria:**
- Given the core assembly, when every type derived from `Exception` declared in `FitFix.Core` is inspected, then each one derives from `FitFixException` and no `ArgumentException` remains at a throw site that validates user data.
- Given the existing 25 tests, when the suite runs, then all stay green and no expected number changes.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Severity pinned by the criteria | `TRACK_FAR_FROM_ORIGINAL`, `NO_ORIGINAL_GPS` | `blocking` and `info` respectively | N/A |
| Severity decided here | `GPS_STALL_DETECTED`, `FEW_ANCHORS` | both `info`, so neither withholds the download | N/A |
| Registry is complete | every code declared in the registry | each has exactly one severity, and the set has no duplicate code strings | N/A |
| Code values are stable | the four existing warning codes | unchanged strings | N/A |
| Every refusal is identifiable | any core throw site | the exception derives `FitFixException` and its `Code` is declared in the registry | the code is never empty |
| Not a FIT file | random bytes | `InvalidFitFileException` with the corresponding code | caught by the CLI, exit code 2 |
| Track with a single point | GPX with one trackpoint | domain exception with a code, not `ArgumentException` | caught by the CLI, exit code 2 |

## Verification

**Commands:**
- `dotnet build -c Release` -- expected: no errors, no new warnings
- `dotnet test -c Release` -- expected: all tests pass, count grows by the new registry tests, no existing expectation edited
- `grep -rn "throw new ArgumentException" src/FitFix.Core` -- expected: no match on a user-data path

## Design Notes

A code's severity answers one question only: may the user download the result without confirming first? `blocking` means the shell withholds the file until an explicit confirmation, `info` means it shows the warning and nothing else (AD-22). `TRACK_LONGER_THAN_ACTIVITY` is the one code whose confirmation also changes the result, because the client repeats the request with `trackLengthMode=full` (AD-17).

The test that the core's code set equals the keys of `i18n/uk.ts` (AD-18) cannot run yet — `web/` does not exist until Epic 2. The registry exposes its full code set now so that test is a few lines when the dictionary lands.

## Implementation Notes

- The registry is `src/FitFix.Core/Diagnostics/DiagnosticCodes.cs`: 14 codes, each with exactly one severity. `All` is exposed so the AD-18 equality test against `i18n/uk.ts` is a few lines once Epic 2 creates `web/`.
- `FitFixException` rejects a code the registry does not declare, at construction. A typo therefore fails where it is written instead of later, when a shell reads `Severity` while mapping the failure to `ProblemDetails`.
- `DiagnosticSeverity.Blocking` is the zero value on purpose: a severity nobody set must close the download gate rather than open it.
- **Type change not in the original Code Map:** `FitTrackReader` refused a track with fewer than two positioned points as `InvalidFitFileException` and now raises `InvalidTrackFileException`. Every track-file refusal is now one type and one code whatever the format. No test pinned the old type, and both derive `FitFixException`, so the CLI and the future shell are unaffected.
- **Two codes where the spec said one per throw site.** `TRACK_TOO_FEW_POINTS` is shared by the three readers because the cause is identical and only the format differs, which the message names. The de-duplication collapse in `ReferenceTrack.FromPoints` got its own `TRACK_WITHOUT_DISTINCT_POINTS`: "your file holds one point" and "every point in your file is the same point" need different fixes from the user.
- `TRACK_LONGER_THAN_ACTIVITY` is declared with its severity, and a test pins that severity, but nothing raises it yet — that is Story 1.8 (AD-17). A reader should not conclude AD-17 landed here.
- **`FIT_HAS_NO_RECORDS` has no test behind it.** Reaching it needs a FIT file with zero record messages, which `SyntheticFitBuilder` cannot write today; the gap and its cause are in `deferred-work.md`.
- CLI output stays Ukrainian-framed with the code in brackets (`Помилка [NOT_A_FIT_FILE]: ...`) and the developer message in English. Human text per code arrives with Story 1.11.
- **Verified independently of the implementation report:** `dotnet build -c Release` 0 warnings, `dotnet test -c Release` 43 passed (25 before the story, 38 after the first pass, 43 after the review patches). CLI smoke tests on the rebuilt binary: empty path, malformed-XML track and one-point track each print one stderr line and exit 2; a junk original prints `Помилка [NOT_A_FIT_FILE]`. `grep -rn "throw new ArgumentException" src/FitFix.Core` finds nothing and the only direct `Exception` descendant in the core is `FitFixException`.

## Review Triage Log

Review pass 1 — three layers (blind hunter 17, edge cases 8, verification gaps 6). Duplicates across layers are logged once, naming the layers that raised them.

| Finding | Verdict | Evidence and route |
|---|---|---|
| `decoder.Read` lets `FitException` / `EndOfStreamException` escape uncoded (blind, edge, verification) | high | Real: the integrity check passes on a file that still fails mid-read, and nothing wraps the call. Patched — rethrown as `InvalidFitFileException` with `FIT_INTEGRITY_FAILED` and the cause preserved. |
| `XDocument.Load` lets `XmlException` escape uncoded (edge, verification) | high | Confirmed by running the CLI: a GPX with a tag mismatch printed an unhandled `System.Xml.XmlException` and died. Pre-existing, but it contradicts this story's intent directly, and the fix is small. Patched in both XML readers. |
| The CLI catch lost its `ArgumentException` arm (blind, edge, verification) | high | Confirmed by running the built CLI: `repair "" track.gpx` printed a stack trace where it used to print one line. Regression caused by this change. Patched; exit 2 verified. |
| Nothing enforced that a `Code` is declared in the registry (blind, edge, verification) | high | Real: the constructor checked only for emptiness, so a typo would have surfaced as `KeyNotFoundException` inside error handling. Patched in the constructor, which closes all nine throw sites at once. |
| Five of nine throw sites never had their `Code` observed (verification) | medium | Pre-verified by the layer and confirmed by reading the tests. Patched: facts added for one-point TCX, one-point FIT track, failed integrity, malformed XML, and `.Code` on the unknown-format refusal. |
| `TRACK_TOO_FEW_POINTS` conflates "fewer than 2 points" with "all points collapse after de-duplication" (blind, edge, verification) | medium | Real for the de-duplication case: different user fix, same code. Patched with `TRACK_WITHOUT_DISTINCT_POINTS`. The three readers keep one shared code — same cause, format named in the message. |
| No inner-exception overload on the exception types (blind) | medium | Real once the two wrapping fixes above are made, or the cause would be discarded. Patched. |
| `DiagnosticSeverity.Info` is the zero value, so a default severity opens the gate (blind) | medium | Real as a design hazard on a gate, though no code path reads a default today. Patched by making `Blocking` zero — a reordering, not a new member. |
| The acceptance test only inspects types declared in the core, so framework exceptions pass it (blind, verification) | medium | Real: the criterion was satisfiable while the contract was broken. The hole itself is now closed by the two wrapping patches plus a malformed-XML fact; the reflection test's scope limit is inherent and recorded here rather than papered over. |
| `IsDeclared` declares a non-nullable parameter but tests for null (blind, edge) | low | Real: dead code under nullable reference types, and it disagreed with `SeverityOf`. Patched to `string?`. |
| Ukrainian XML doc comment added in `Alignment.cs` (blind) | low | Real: the project requires English in code, and every other comment in the change is English. Patched. |
| Dead `.Where` filter and a tautological `Assert.IsNotType` in the new tests (blind) | low | Real: the next filter already excludes the type, and the assertion follows an exact `Assert.Throws`. Both deleted. |
| The registry has no category or HTTP-status dimension for AD-22 (blind) | medium | Real, and it means the shell will carry a second mapping. Deferred: `ProblemDetails` belongs to Epic 3, and the status mapping belongs with the story that writes it. |
| No code for the malformed-multipart refusal of AD-13 (blind) | low | Real but unreachable today — the shell does not exist. Deferred with the entry above. |
| `FitRoundTripTests.A_fit_without_records_is_rejected` rejects nothing (verification) | medium | Confirmed by reading it: it writes one record, reads it back and asserts `Assert.Single`. Pre-existing, not caused by this story. Deferred; it is why `FIT_HAS_NO_RECORDS` looked covered. |
| The CLI's refusal contract has no test at all (verification) | medium | Real: no test executes `Program.cs`. Deferred — pinning it needs a process harness or a refactor extracting the command from top-level statements, both larger than this story. Behaviour verified by hand instead. |
| `SeverityOf` throws instead of degrading when mapping warning strings (edge) | low | Rejected: every warning a repair raises is a declared code, and a test pins that. A `TryGetSeverity` would add public surface to guard a state never demonstrated. |
| `IsBlocking` would read a future third severity as non-blocking (edge) | low | Rejected: the enum has two members, and the change that adds a third is the change that must revisit the gate. |
| Severity has no production consumer (blind) | low | Rejected: by design — this story is core-only, and the first consumer is the shell in Epic 3. No harm today. |
| The CLI prints a Ukrainian label with an English message (blind) | low | Rejected: the CLI speaks Ukrainian to its user and the code is the stable part; per-code human text is Story 1.11, named in the spec. |
| `MalformedArgumentException` discards `ArgumentException.ParamName` (blind) | low | Rejected: nothing reads `ParamName`, and both call sites carry the parameter name in the message. |
| The `FitTrackReader` type change is not in the spec's Code Map (blind) | low | Rejected as a finding whose only fix is to edit this build's spec. Recorded in the Implementation Notes above instead. |
| `TRACK_FORMAT_UNKNOWN` is shared by the sniff failure and the unhandled-enum arm (blind) | false | The enum arm is the default of a switch over a format this code just determined; no input reaches it. Defensive code that cannot fire is not a user-visible conflation. |
| Spec frontmatter says `in-review` while the tracker says `in-progress` (blind, verification) | false | Not a disagreement: the frontmatter tracks the build workflow's internal state, the tracker the story's state in the sprint. The tracker moves to `review` when the build finishes, which is after the diff the reviewers read. |
| `TRACK_LONGER_THAN_ACTIVITY` is declared but never raised (blind) | low | Real and intended — Story 1.8 raises it. Recorded in the Implementation Notes so no reader concludes AD-17 landed here. |
