---
title: 'Track far from the original — say how far, and ask before the file'
type: 'feature'
created: '2026-11-28'
status: 'done'
baseline_commit: 'e28b5155cd013ae94edbf4b6737c863509f4b6db'
route: 'dispatch'
review_loop_iteration: 0
context: ['{project-root}/docs/implementation-artifacts/epic-3-context.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** When no position of the activity attaches to the reference track but the two still lie within 2 km, the repair lays the whole track over the athlete's time and raises `TRACK_FAR_FROM_ORIGINAL` — a sentence with no number, beside a download that is already offered. Somebody who picked the wrong track (the same loop last week, a neighbour's route) cannot tell a 50 m miss from a 1.9 km one and walks away with a confident, wrong file.

**Approach:** The core can measure how far the first position lies from the whole track (`MinDeviationToWholeTrack`) but states it nowhere. Report it: `Alignment` → `RepairReport` → a nullable `startToTrackMeters` on the response, present only with the warning. The result screen states the distance in kilometres, withholds the file, and offers one explicit confirmation ("this is the right track"), the same shape as the long-track gate (AD-22, Story 3.5).

## Boundaries & Constraints

**Always:** Metres in the contract, kilometres formatted in `format.ts` (AD-6, AD-14); DTO hand-written, no domain type serialised. The distance is the core's, never recomputed from series (AD-16). The gate reads the response's own `severity` (`blocking`) and the page's recorded answer; the confirmation changes nothing about the file, so it asks the service nothing (AD-3). Strings only in `uk.ts` (AD-18), colours only in `theme.css`. Touch targets ≥ 44 px.

**Never:** No new threshold (the 2 km line is `TrackFarFromOriginalMeters`, AD-10). No change to when the warning is raised or to the refusal `TRACK_ATTACHES_NOWHERE`. No server-side confirmation parameter. No second marker on the map (separate deferral). No coordinates in logs (AD-9).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Far track | Warning raised, first position 1 340 m from the track | Response carries `startToTrackMeters: 1340`; callout states "1,34 км"; download withheld | N/A |
| Confirmed | Athlete presses the confirmation | Gate closes, download enabled, callout stays, button shows pressed state | N/A |
| New repair | Another pair repaired after confirming | Confirmation forgotten; gate closed again | N/A |
| Normal repair | No warning | `startToTrackMeters: null`; no gate | Older server (field absent) reads the same as null |
| Warning without number | Warning present, field null/absent | Gate still asks; sentence says no distance | N/A |
| Long track and far track together | Both blocking | File waits for both answers | N/A |

</frozen-after-approval>

## Code Map

- `src/FitFix.Core/Aligner.cs:114-133` -- where `TrackFarFromOriginal` is added, also set `track.MinDeviationToWholeTrack(first position)` on the returned `Alignment`, null otherwise.
- `src/FitFix.Core/Alignment.cs:116` -- new `double? StartToTrackMeters`; `EndingAt` (Aligner.cs ~236) copies it from `measured`.
- `src/FitFix.Core/RepairReport.cs:153`, `RepairService.cs:~274` -- report field copied from the alignment.
- `src/FitFix.Api/Contracts/RepairResponse.cs:80-91`, `RepairEndpoint.cs:~230` -- new `double? StartToTrackMeters` after `Redrawn`, with doc; mapped from the report only.
- `src/FitFix.Cli/Program.cs:242` -- text names the distance (the old "first GPS point" wording is wrong about what is measured).
- `web/src/api/repair.ts:153`, `web/src/test/fixtures.ts` -- type and fixtures.
- `web/src/components/Result.tsx:104-210` -- `farCode`, `farGated = blockedBy(farCode) && !confirmedFar` in `gated`; `FarTrackGate` modelled on `LongTrackGate` (local `useState` for the answer; no async).
- `web/src/format.ts` `kilometres`; `web/src/i18n/uk.ts` (`result.farTrack`, update `TRACK_FAR_FROM_ORIGINAL`) -- reuse, don't copy.
- Tests to extend: `AlignerTests`/`GpsCleanerTests` (far case), `ContractShapeTests.cs:128-214` (key list gains `startToTrackMeters`), `Result.test.tsx:~310` (old "does not withhold" test is inverted), `App.test.tsx:~267`.

## Tasks & Acceptance

**Execution:**
- [x] `Alignment.cs`, `Aligner.cs`, `RepairReport.cs`, `RepairService.cs` -- carry the first position's distance to the track to the report, only with the warning -- one owner of the number (AD-16)
- [x] `RepairResponse.cs`, `RepairEndpoint.cs`, `ContractShapeTests.cs` -- nullable contract field, key list pinned -- AD-14
- [x] `Program.cs` -- CLI warning text states the distance -- shells agree
- [x] `repair.ts`, `fixtures.ts`, `Result.tsx`, `uk.ts`, `uk.test.ts` -- gate, text, type -- AD-22, AD-18
- [ ] Tests for every matrix row (core, contract, `Result.test.tsx`, `App.test.tsx` for "forgotten on new repair")

**Acceptance Criteria:**
- Given a track 1.3 km from every position of the activity, when repaired, then the report and the response carry that distance and the warning's severity is `blocking`.
- Given that response, when the result shows, then the distance is in the callout, the download is absent, and one button confirms; after pressing it the download is offered and the button reads as pressed.
- Given a track that attaches normally, then `startToTrackMeters` is null and nothing is withheld.
- Given a repair after a confirmed one, then the new result is gated again.

## Implementation Notes

- Core: `Alignment.StartToTrackMeters` is set in `Aligner` only where `TRACK_FAR_FROM_ORIGINAL` is added, from `track.MinDeviationToWholeTrack` of the first positioned record; `EndingAt` copies it; `RepairReport` and `RepairResponse.StartToTrackMeters` carry it (camelCase `startToTrackMeters`, nullable, key list pinned in `ContractShapeTests`).
- Web: `FarTrackGate` in `Result.tsx` (local `useState`, `min-h-11`, `aria-pressed`); `gated` now includes `farTrack && !farConfirmed`. The confirmation is forgotten because starting again or repairing again unmounts `Result` (`working` state).
- The old test "does not withhold the file behind a blocking warning with no screen" now uses an unknown code, since the far code has a screen.
- Mutation checks: dropping the far term from `gated` fails 4 tests; a constant distance in `Aligner` fails the core test.
- Prettier needs `--single-quote --print-width 100` here (no config in the repo); a default run reflows whole files.

## Spec Change Log

- 2026-11-28, human decision during implementation: the distance is the first position's distance to the whole track, not the search's closest approach. Trigger: a looped track crossing the athlete's route gave a closest approach of 0.8 m beside a "far" warning (nothing attached, because of the search window). Amended: Problem, Approach, Code Map, matrix. Avoids a caveat contradicting its own number. KEEP: the number appears only with the warning; the gate and confirmation unchanged.

## Review Triage Log

Layer run: verification-gap only (standing instruction to spend fewer tokens).

| Finding | Verdict | Route | Evidence |
|---|---|---|---|
| Endpoint mapping of `startToTrackMeters` not pinned by a test through the real `/api/repair` | medium | patch | Real: mapping the field to null kept all 127 API tests green, so every far-track result would show "distance unknown". Added two `RepairTests` cases (far track: value equals the core's and severity `blocking`; attached track: null); the same mutation now fails. A `WithTrackShiftedEast` fixture helper is shared with the core test. |
| CLI far-track text untested | low | defer | No CLI test project exists; wording only. Recorded in `deferred-work.md`. |
| Stale `closestApproach` comment in `Aligner.cs` | low | patch | Real: it said the first position was what the closest approach stood in for, the opposite of the new rule. Rewritten to say what the value decides and why the report states another. |
| `farConfirmed` survives in-place report replacement (`chooseLength`, `answerCoverage`) | low | rejected | Same pair of files, same bytes, so a confirmation answering for that pair stays true; a new pair goes through `working` and unmounts `Result`. |
| `App.test.tsx` uses `TRACK_FAR_FROM_ORIGINAL` as a refusal code | low | rejected | The test checks that a refusal is forgotten when a file changes; any code serves. Changing it adds churn for no defect. |

## Verification

**Commands:**
- `dotnet test` -- expected: all pass
- `npm --prefix web run typecheck && npm --prefix web run test && npm --prefix web run build` -- expected: pass

**Manual checks (if no CLI):**
- Browser with a synthetic far-track report: gate text, confirm, download appears; phone width has no horizontal scroll.
