---
title: 'Story 1.4 — Garbage points reach neither the computation nor the map'
type: 'feature'
created: '2026-11-20'
status: 'done'
route: 'dispatch'
review_loop_iteration: 2
baseline_commit: '928d9523d563df06ecf37166c4bef65d0f5bd2ec'
context: ['{project-root}/docs/implementation-artifacts/epic-1-context.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A watch that teleports for one second leaves a GPS point thousands of kilometres away. Nothing in the core removes it: the anchor search rejects it (its speed fails the sport cap), but it still counts as the original's first position for the track-compatibility check — so a perfectly matching track can be reported as `TRACK_FAR_FROM_ORIGINAL` — and it would still be drawn on the preview map and feed the zones, because every consumer reads the same raw record series.

**Approach:** Clean the series once, in the core, before anything else looks at it: a position is discarded when the distance from the last valid position cannot be covered at the sport's maximum speed in the time between them — judged over a time base that is never shorter than a few seconds, so one second of GPS jitter is never called impossible. The criterion is impossibility in time, never distance, so a hundred-kilometre ride across several cities survives intact. The leading position, which has nothing behind it to be judged by, is chosen by agreement among the positions that follow rather than by a single look ahead. Cleaning runs before the compatibility check, before anchoring and before any series a shell will show; the number of discarded positions goes into the report and raises a warning code.

</frozen-after-approval>

## Code Map

- `src/FitFix.Core/Models.cs:11-26` -- `ActivityRecord` is a record whose `Position` is derived from `Latitude`/`Longitude`. Discarding a position therefore means `record with { Latitude = null, Longitude = null }` — never dropping the record, because FR-6 requires every timestamp and sensor value to survive.
- `src/FitFix.Core/Models.cs:28-42` -- `OriginalActivity`. `RecordedDistance` reads the device's own distance field, which cleaning must not touch: the report's "before" distance is what the user saw in Strava.
- `src/FitFix.Core/RepairService.cs:18-40` -- both `Repair` overloads. The sport-derived one resolves options, the explicit one runs the pipeline; cleaning belongs at the top of the explicit one, before `Aligner.Align`.
- `src/FitFix.Core/Aligner.cs:19-30` -- the FR-3 check: `positioned[0].Position` against the track decides `TRACK_FAR_FROM_ORIGINAL`, and `positioned.Length == 0` decides `NO_ORIGINAL_GPS`. This is the consumer that must see the cleaned series, and the reason ordering matters.
- `src/FitFix.Core/AlignmentOptions.cs` -- `MaxSpeedMetersPerSecond` (per sport via `ForSport`) and `SpeedToleranceMeters` (3 m) already exist and are the thresholds this story reuses. No new threshold is introduced.
- `src/FitFix.Core/RepairReport.cs` -- the report the CLI prints and the API will serialise; it gains the discarded count.
- `src/FitFix.Core/RepairReport.cs` -- `RepairResult` carries records, report and alignment. A shell building the "before" map series needs the cleaned original, so it belongs here too.
- `tests/FitFix.Core.Tests/Fixtures/SyntheticRun.cs` -- the fixture. It has no spike today; a variant that injects one is needed, and the existing no-spike numbers (1129 anchors, 6499.5036 m) must stay untouched.
- `tests/FitFix.Core.Tests/RepairServiceTests.cs:135-147` -- the existing `TRACK_FAR_FROM_ORIGINAL` test shifts the whole track by a degree. It must keep passing: a genuinely wrong track still warns.

## Tasks & Acceptance

**Execution:**
- [x] `src/FitFix.Core/GpsCleaner.cs` -- new. One function over an `OriginalActivity` and `AlignmentOptions` returning the cleaned activity and the number of discarded positions. A position is discarded when the distance from the last valid position exceeds `MaxSpeedMetersPerSecond * max(dt, ImpossibleSpeedJudgedOverSeconds) + SpeedToleranceMeters`. A non-positive `dt` cannot be judged by speed at all, so such a pair is never called impossible. A discarded point is never the reference for the next one, and the reference's allowance does not keep growing while it stays stale: a reference is stale while garbage it has not resolved still stands behind it — that is, while discards have happened since it was accepted — and not for a time window after the last discard. A stale reference vouches for at most `StaleReferenceVouchesForSeconds`. When discards run on past `LeadingAgreementPositions` in a row, the reference has clearly been left behind by the activity: it is dropped and a new one is seated by the same agreement procedure that seats the first, so a burst cannot condemn everything after it. -- jitter at one-second sampling must never read as impossible, and a teleport must not be laundered into the reference.
- [x] `src/FitFix.Core/GpsCleaner.cs` -- the leading reference is chosen by agreement, not by one look ahead: the first position is accepted when any of the next `LeadingAgreementPositions` positions that are somewhere else can be reached from it, otherwise it is discarded and the next candidate is tried. Each voter is judged over one span from the candidate to that voter, exactly as the forward rule would judge it — never by summing a per-step allowance, which would hand out several times the real allowance and launder a distant fix. A follower within `StallRadiusMeters` of the candidate is the same place and casts no vote, so a stale fix that repeats with jitter cannot vouch for itself at any burst length. An interval that speed cannot judge contributes no allowance and no vote. -- a cold-start fix that lasts two or three records must not become the reference and condemn the rest of the activity.
- [x] `src/FitFix.Core/RepairService.cs` -- clean at the top of the explicit `Repair` overload, then align and repair on the cleaned activity. -- one owner, and every entry point inherits it (AD-25).
- [x] `src/FitFix.Core/RepairReport.cs` -- add the discarded-position count. -- the user is told how much was thrown away.
- [x] `src/FitFix.Core/RepairReport.cs` -- `RepairResult` exposes the cleaned original. -- a shell's "before" map and zones must read the cleaned series, not the raw one.
- [x] `tests/FitFix.Core.Tests/Fixtures/SyntheticRun.cs` -- add a way to inject a teleport spike at a chosen second, leaving the default fixture exactly as it is. -- the no-spike numbers must stay comparable.
- [x] `src/FitFix.Core/AlignmentOptions.cs` -- add `ImpossibleSpeedJudgedOverSeconds` (5) and `LeadingAgreementPositions` (3), both with XML docs naming what they govern. -- AD-10: the two new policy numbers are thresholds like any other.
- [x] `src/FitFix.Core/Diagnostics/DiagnosticCodes.cs` -- declare `POSITIONS_DISCARDED` with severity `info`. -- AD-18: a user-visible condition belongs in the registry, not only in an integer.
- [x] `src/FitFix.Core/Aligner.cs` or `src/FitFix.Core/RepairService.cs` -- raise that code when anything was discarded. -- so a shell can say why the map looks different, and `NO_ORIGINAL_GPS` after a full discard is no longer the only signal.
- [x] `src/FitFix.Cli/Program.cs` -- print the discarded count when it is not zero, next to the stall line. -- the only shell that exists must not stay silent about deleted positions.
- [x] `src/FitFix.Core/Models.cs` -- make `OriginalActivity` a record so cleaning copies it with `with`. -- Story 1.6 adds `Laps`; a hand-written copy would silently drop it.
- [x] `tests/FitFix.Core.Tests/Fixtures/SyntheticRun.cs` -- `WithTeleportAt` takes a range-guarded record index, refuses a record that has no position, and can place the spike in any direction including the 0,0 point real watches emit; add a way to inject a burst of consecutive spikes. -- the leading-burst case is the defect this loopback exists for.
- [x] `tests/FitFix.Core.Tests/GpsCleanerTests.cs` -- new. Cover the I/O matrix below, and assert on the cleaned activity, never on the input fixture. -- the invariant is about what no longer exists, which only a test can show.
- [x] `tests/FitFix.Core.Tests/AlignmentOptionsTests.cs` -- the two sport-cap tests must assert that cleaning discarded nothing, so they keep measuring the anchor stage. -- cleaning with the same threshold would otherwise satisfy them before `Aligner` is reached.

**Acceptance Criteria:**
- Given an activity with no impossible point, when it is repaired, then the discarded count is zero and every number the suite already pins is unchanged.
- Given the cleaned activity on the result, when its records are inspected, then no discarded position is present anywhere a shell could read it, while every timestamp and sensor value from the original is still there.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| A teleport is removed | the synthetic run with one point moved 2000 km for a single second | that position is gone from the cleaned series, the count is 1, and the repaired distance still matches the no-spike result | N/A |
| Nothing is removed without a spike | the untouched synthetic run | the count is zero and anchors, distance and measured boundary equal the pre-story numbers | N/A |
| The record survives its position | the same spike | the record keeps its timestamp, heart rate, cadence and power; only latitude and longitude are gone | N/A |
| A spike first point does not break FR-3 | a run whose very first position is a teleport, with a track that matches the rest | no `TRACK_FAR_FROM_ORIGINAL`, because the check reads the cleaned series | N/A |
| A genuinely wrong track still warns | the existing degree-shifted track | `TRACK_FAR_FROM_ORIGINAL` as before | N/A |
| Distance alone is not suspicious | a ride covering 100 km across cities within a plausible time | nothing discarded — the criterion is time, not distance | N/A |
| A long gap is not a teleport | a 20-minute gap in records followed by a position 2 km away | nothing discarded: 1.7 m/s is walkable | N/A |
| A frozen GPS is not garbage | the synthetic stall, where the position stops moving | nothing discarded — a stall is handled by the stall detector, not here | N/A |
| Cleaning never invents a stall | an activity with a hundred seconds of varied mid-activity garbage and no frozen stretch | the reported stall time and measured boundary are the same as before cleaning | N/A |
| Garbage does not condemn what follows it | eighty seconds, then five minutes, of per-second garbage mid-activity, then genuine running | only the garbage is discarded; every position after it survives | N/A |
| A distant fix is not laundered by repetition | thirty identical leading fixes a kilometre away, and five leading fixes a kilometre away jittering by 5 m | the fixes are discarded, the real route survives | N/A |
| A standing start is not garbage | sixty seconds of genuine standing-start positions followed by two spikes | the standing-start positions survive, the two spikes do not | N/A |
| A pause does not launder a teleport | a long garbage run, then a quiet stretch, then a point five kilometres on | that point is still discarded | N/A |
| A duplicate timestamp cannot launder a spike | a leading spike followed by two genuine records sharing one timestamp, then the real route | the spike is discarded and the route survives | N/A |
| A leading burst does not invert the rule | two, then three, consecutive teleports at the very start, followed by the real route | only the burst is discarded; the anchor count and measured boundary match the no-spike run | N/A |
| A burst anywhere else | several consecutive teleports mid-activity and at the very end | only those positions are discarded | N/A |
| Only two positions exist | the first a teleport, the second real | the teleport is discarded, not the real point | N/A |
| Ordinary noise at a realistic pace | 1 Hz sampling at 5 m/s with jitter up to 5 m, a plausible consumer-watch file | nothing discarded | N/A |
| A ride judged with running thresholds | the 15 m/s ride cleaned with the running profile | nothing discarded — one-second steps of 15 m are not impossible over the judging window | N/A |
| Timestamps that do not move forward | two records sharing a timestamp 50 m apart, and a record whose timestamp goes backwards | nothing discarded — speed cannot judge a non-positive interval | N/A |

## Spec Change Log

- **Trigger:** review pass 2, third layer. The vote summed a per-step allowance instead of judging one span, so three 1 Hz steps granted 114 m where the forward rule grants 38 — measured: thirty identical leading fixes a kilometre away were confirmed as the reference and 263 genuine positions discarded, and five fixes jittering by 5 m voted for themselves. A mid-activity burst of about seventy seconds at running pace discarded every genuine position after it (measured 300 of 300). Staleness expiring on a timer handed the garbage run's own elapsed time back, so a point five kilometres on was accepted after a thousand seconds of teleports and a pause.
  **Amended:** a voter is judged over one span from the candidate, the same-place filter uses `StallRadiusMeters` so a jittering repetition is still one place, staleness lasts while unresolved garbage stands behind the reference rather than for a time window, and a run of discards longer than the agreement window re-seats the reference by the same procedure that seats the first one.
  **Known-bad state avoided:** a cleaner whose own leading rule is laxer than its forward rule, that wipes everything after a mid-activity burst, and that can be talked into trusting a distant fix by repetition or by a pause.
  **KEEP:** the judging window, the majority vote, the clean-file instance guarantee, and that a wiped activity must never be reported as a plain success.

- **Trigger:** review pass 2, two measured defects and one that the suite could not see. Cleaning fed the stall detector, which walks positioned records only, so blanking a varied mid-activity burst bridged its window and manufactured a stall at 295 s where there was none — after which every later anchor was skipped and the remainder fell into the estimated zone. The stale bound, measured from the last discard, made the reference permanently stale once garbage outlasted the window: 900 of 1501 positions discarded, including all 600 genuine ones after the burst. And the leading vote accumulated `AllowanceMeters`, which returns infinity for a non-positive interval, so one duplicate timestamp inside the look-ahead window made every follower vote in favour: 200 of 201 positions discarded with the spike kept as the reference.
  **Amended:** the stall detector reads the series as it arrived, never the cleaned one; the stale bound is counted from the reference's own timestamp and gets its own threshold instead of borrowing `LongGapSeconds`; the vote's allowance is computed from the candidate's timestamp to each voter rather than summed per step. Four measured rows joined the matrix.
  **Known-bad state avoided:** a cleaner that invents a GPS failure, that eats a legitimate resumption after a long burst, and that is inverted by a duplicate timestamp — all three producing the false `TRACK_FAR_FROM_ORIGINAL` this story exists to remove.
  **KEEP:** the judging window, the majority vote with its "same place casts no vote" filter, the clean-file instance guarantee, and the refusal of a point laundered by a long run of garbage.

- **Trigger:** the implementation hit a contradiction between two parts of this spec. The Execution bullet said the first positioned record "is kept", while the Intent and matrix row 4 require that a teleport *as the first position* must not trip `TRACK_FAR_FROM_ORIGINAL`. Keeping it unconditionally would have left that bug unfixed and, worse, made every later real point impossible relative to it, wiping the whole series.
  **Amended:** the Execution bullet now describes the first position as an unconfirmed reference resolved by one look ahead. The frozen Intent was not touched — it already demanded the cleaned behaviour.
  **Known-bad state avoided:** a spec that reads as satisfied while the defect its own matrix names is still live.
  **KEEP:** the clean-file guarantee that an activity with nothing impossible comes back as the same instance so no pinned number can move.

- **Trigger:** review pass 1. Three layers converged on the cleaning rule, two of them with measurements: a leading burst of two or more teleports made the look-ahead confirm the garbage as the reference and discard 2159 of 2161 positions, raising the exact `TRACK_FAR_FROM_ORIGINAL` this story removes; the `max * dt + tolerance` threshold discarded 265 of 1300 genuine positions on a 5 m/s file with 5 m jitter; and the same threshold satisfied the two anchor-cap tests before `Aligner` was reached, so that cap could be deleted with the suite green.
  **Amended:** the frozen Intent now says impossibility is judged over a time base of several seconds and that the leading position is chosen by agreement among those that follow. The Execution list gains the judging window, the agreement rule, the two thresholds in `AlignmentOptions`, a `POSITIONS_DISCARDED` code, the CLI line, `OriginalActivity` as a record, and the fixture and test obligations. Seven measured rows joined the matrix.
  **Known-bad state avoided:** a cleaner that deletes ordinary jittered points on real files, inverts itself on a cold-start burst, and hides the anchor cap's own tests.
  **KEEP:** cleaning blanks coordinates and never drops a record; the clean-file instance guarantee; cleaning runs before the FR-3 check; `OriginalDistanceMeters` keeps reading the device's own recorded distance.
  **Note on authority:** the frozen block is the human's. Ілля delegated the decisions for this unattended window, so this renegotiation is mine and is flagged here for him to overturn. The epic's acceptance criterion says "exceeds the sport maximum from `AlignmentOptions`", which the judging window keeps — it changes the time base the maximum is applied over, not the maximum.

- **Trigger:** the re-derivation measured two of the amended spec's own wordings as wrong. An unbroken chain over a fixed horizon was defeated by a burst of horizon + 1: four identical leading teleports discarded 2157 of 2161 positions, the loop-0 defect moved one record outward. And clamping the time base for a stale reference discarded 302 of 963 positions on a spike followed by a five-minute outage and a resumption 900 m away — the Chabany failure pattern.
  **Amended:** agreement is a majority vote among followers that are somewhere else, which is horizon-independent; staleness expires when garbage stops arriving rather than clamping the time base.
  **Known-bad state avoided:** a rule that works for the burst lengths someone thought to test and inverts on the next one, and a rule that eats a legitimate resumption after an outage.
  **KEEP:** the judging window, the vote's "same place casts no vote" filter, and the refusal of a point laundered by a long run of garbage.

- **Trigger:** with one-span judging, a majority of voters turned out to be unreachable for the matrix's own rows: the 15 m/s ride under the running profile is disowned by its own body three seconds out — 45 m against a 38 m allowance, honestly impossible at a 7 m/s cap — so voters went one for, two against and the whole ride was deleted, breaking both the ride row and the `DiscardedPositionCount == 0` assertion the sport-cap tests now carry.
  **Amended:** one voter that is somewhere else is enough to vouch for a candidate. Every garbage row rejects unanimously, so nothing is weakened: the leading-burst, repetition, laundering and standing-start cases all still discard exactly the garbage.
  **Known-bad state avoided:** a vote strict enough to delete a legitimate fast ride.
  **KEEP:** one span per voter, the `StallRadiusMeters` same-place filter, staleness as unresolved garbage, and re-seating after a run of discards.

## Verification

**Commands:**
- `dotnet build -c Release` -- expected: no errors, no new warnings
- `dotnet test -c Release` -- expected: all tests pass; no number pinned before this story moves

## Design Notes

The threshold the first implementation used — `max * dt + tolerance`, with the anchor rule's own numbers — was measured to delete real data. At 1 Hz sampling a runner at 5 m/s with 5 m of jitter shows steps up to 15 m against a 10 m allowance: a probe over the fixture's route discarded 265 of 1300 genuine positions. The same arithmetic also quietly defused the two tests that pin the anchor-stage speed cap: cleaning a 15 m/s ride with the running profile threw away 432 of 434 positions, so `running.AnchorCount < cycling.AnchorCount` held no matter what the anchor loop did.

Hence the judging window. Impossibility is assessed at the sport's maximum speed over `max(dt, ImpossibleSpeedJudgedOverSeconds)`, so a single second of jitter is never impossible while a sustained impossible speed still is. With five seconds, a runner's allowance is 38 m per step instead of 10 m — jitter survives, a 2000 km teleport does not, and a ride's 15 m steps stay legal under the running profile, which puts the anchor-cap tests back to measuring the anchor stage.

The leading position needs agreement, not a glance. One look ahead was measured to invert the whole rule: with two identical teleports at the start of the synthetic run, the second teleport vouched for the first, became the confirmed reference, and 2159 of 2161 positions were discarded — producing the very `TRACK_FAR_FROM_ORIGINAL` false alarm this story exists to remove. A stale cold-start fix normally lasts several records, so the first position is accepted only when a short run of following positions agrees with it.

Discarding a position is not discarding a record. The repaired file must carry every original timestamp with its sensors (FR-6), so cleaning only blanks the coordinates; `Aligner`'s own `Where(r => r.Position is not null)` then skips them with no further change.

## Implementation Notes

- `GpsCleaner.Clean` is the single owner. Impossibility is judged at the sport cap over `max(dt, ImpossibleSpeedJudgedOverSeconds)` plus `SpeedToleranceMeters`; an interval speed cannot judge is never impossible; a discarded point never becomes the reference.
- **The leading position is decided by a vote**, because it has nothing behind it. A follower within `StallRadiusMeters` is the same place and casts no vote, each voter is judged over one span, and one voter that is elsewhere is enough. A reference is stale while unresolved garbage stands behind it, vouches for at most `StaleReferenceVouchesForSeconds`, and is re-seated by the same vote once discards run on longer than the vote looks at — that last rule is what keeps a mid-activity burst from condemning everything after it.
- **The stall detector reads the series as it arrived.** `Aligner.Align` now takes both the cleaned activity and the original, so the alignment works from the cleaned one and `GpsStallDetector` from the raw one. Blanking a burst would otherwise bridge the detector's window and invent a GPS failure where there was none — measured at 195 s on an out-and-back run with no stall at all, after which every later anchor was skipped.
- `ImpossibleSpeedJudgedOverSeconds` (5), `LeadingAgreementPositions` (3) and `StaleReferenceVouchesForSeconds` (30) live in `AlignmentOptions`; `Clean` refuses nonsensical values with `ArgumentOutOfRangeException`.
- `POSITIONS_DISCARDED` is declared `info`: throwing away an impossible point improves the result, so it must not close the download gate. The CLI prints the count only when it is not zero.
- `OriginalActivity` became a record so cleaning copies it with `with` — Story 1.6's `Laps` will survive cleaning without anyone remembering to copy it.
- **Verified independently of the implementation report:** `dotnet build -c Release` 0 warnings; `dotnet test -c Release` 98 passed (57 at the story's baseline). I re-ran the mutation the review used to expose AD-10 as unguarded — hardcoding `25.0` in place of `options.MaxSpeedMetersPerSecond` inside the cleaner — and two tests now fail where all 83 passed before. The CLI still reports 1129 anchors and no discarded line on the clean synthetic pair.
- **Two deviations from this spec are the implementer's, both measured and both recorded in the Spec Change Log:** one voter instead of a majority, and staleness as unresolved garbage rather than a clamped time base.
- **The frozen Intent was renegotiated twice by me, not by Ілля.** He delegated the decisions for an unattended window; the Spec Change Log flags both changes for him to overturn. The epic's own criterion — impossibility against the sport maximum from `AlignmentOptions` — still holds; what changed is the time base it is applied over.
- Known cost: the leading vote is quadratic in a run of leading garbage — 0.31 s for a two-hour all-garbage file, 1.63 s for five and a half hours, against SM-3's three seconds. A twelve-hour all-garbage file would exceed it. The timing test now covers the two-hour case.

## Review Triage Log

Two review passes, three layers each (blind hunter, edge cases, verification gaps). Pass 1 found 38 findings, pass 2 found 33. Duplicates across layers are logged once, naming the layers. Both passes ended in a loopback: the cleaner was reverted and re-derived rather than patched into coherence.

### Pass 1

| Finding | Verdict | Evidence and route |
|---|---|---|
| A leading burst of two or more teleports confirms itself and discards the whole activity (all three layers, measured twice) | high | Two independent probes: 2159 of 2161 positions discarded, `TRACK_FAR_FROM_ORIGINAL` raised on a matching track. The defect the story exists to remove. bad_spec — the spec's own look-ahead rule was wrong; reverted and re-derived with a vote. |
| `max * dt + tolerance` discards ordinary jittered points (verification, measured) | high | 265 of 1300 genuine positions on a 1 Hz file at 5 m/s with 5 m jitter. My Design Notes' claim that the fixture proved the tolerance wide enough held only for its own 3 m/s and 3 m. bad_spec — replaced by the judging window. |
| The same threshold defuses the two anchor-cap tests (verification, measured) | high | Cleaning a 15 m/s ride with the running profile discarded 432 of 434 positions, so `running.AnchorCount < cycling.AnchorCount` held with the anchor cap deleted. bad_spec; the judging window fixed it, and both tests now assert nothing was discarded. |
| With only two positioned records the teleport is kept and the good point discarded (blind, measured) | high | Probe: first record 2000 km away, second real, `kept index: 0`. Same root cause, same re-derivation. |
| Nothing warns when cleaning empties the series (blind, edge) | medium | Real: the user would see `NO_ORIGINAL_GPS` with no hint that cleaning caused it. Patched — `POSITIONS_DISCARDED` declared and raised. |
| The only shell never shows the count (blind, verification) | medium | `PrintReport` was untouched while the spec claimed the user is told. Patched — one conditional line. |
| `A_frozen_gps_is_not_garbage` asserts on the input fixture (blind, verification) | medium | True by construction: `Clean` is copy-on-write and cannot modify the input. Patched to read the cleaned activity. |
| The look-ahead depth is a tuning constant outside the options (blind) | medium | Correct under AD-10. Patched — `LeadingAgreementPositions` joined `AlignmentOptions`. |
| Out-of-order timestamps make the cleaner eat good positions (blind, edge, measured) | medium | Probe: one record timestamped backwards, one genuine position discarded. Patched — an interval speed cannot judge is never impossible. |
| Duplicate timestamps more than the tolerance apart are discarded (edge) | medium | Same root cause and same patch. |
| `OriginalActivity` is copied by hand, so a future member is dropped (edge) | medium | Real forward trap: Story 1.6 adds `Laps`. Patched — the type is a record and cleaning uses `with`. |
| `WithTeleportAt` conflates index with second, guards nothing, throws on an unpositioned record, only goes north (blind, edge) | low | All true. Patched, plus a burst helper and a 0,0 fix. |
| Cleaning inside `RepairService` leaves `/api/inspect` to clean for itself (blind) | medium | Correct: AD-25 says before any preview series, and inspect does not repair. Deferred — `GpsCleaner` is public, and the obligation is recorded for that story. |
| Blanked positions could merge two sub-threshold stalls (edge) | medium | Filed as unverified in pass 1; pass 2 measured the stronger version of it and it was fixed there. |
| The spec names `Alignment.cs` for a change that landed in `RepairReport.cs` (blind, edge) | low | Correct; the Code Map already said so. Fixed in the spec. |
| `AnchorCount - 1` is a tight assertion on the greedy anchor walk (blind) | low | Real but acceptable: it is exact for the clean run and the burst tests use a range. Noted, not changed. |
| The spine's Ukrainian-comment convention and the AD-10 open question are stale (blind) | low | Correct. Deferred — the fix edits planning and agent-context files. |
| A separate, much higher cleaning cap would be safer than reusing the anchor cap (edge) | low | Rejected: the epic's criterion names the sport maximum, and the judging window addresses the same risk without a second cap. |
| Genuine speed above the cap (a fast descent) could delete real positions (edge) | low | Rejected on arithmetic: at 1 Hz the allowance is 28 m per step for cycling, so only a sustained 100 km/h would discard, and then a single isolated point. |
| No test covers a multi-record garbage burst (blind) | medium | Correct and central. Covered by the re-derivation's new tests. |

### Pass 2

| Finding | Verdict | Evidence and route |
|---|---|---|
| Cleaning feeds the stall detector and invents a GPS failure (blind, edge, measured) | high | No stall before cleaning, a stall at 195–295 s after it, every later anchor skipped and the remainder estimated. bad_spec — the detector now reads the series as it arrived. |
| The staleness timer cascades and condemns everything after a mid-activity burst (blind, edge, measured) | high | 900 of 1501 positions discarded including all 600 genuine ones after the burst; an 80 s burst cost all 300. bad_spec — staleness is unresolved garbage, plus re-seating. |
| The leading vote sums a per-step allowance, so it is far laxer than the forward rule (edge, measured) | high | 114 m granted over three 1 Hz steps against 38 m; 30 identical fixes a kilometre away were confirmed and 263 genuine positions discarded. bad_spec — one span per voter. |
| A jittering stale fix votes for itself (edge, measured) | high | Five leading fixes 5 m apart discarded 264 genuine positions. bad_spec — the same-place filter is `StallRadiusMeters`. |
| An infinite allowance from a duplicate timestamp launders a leading spike (verification, edge, measured) | high | 200 of 201 positions discarded with the spike kept as the reference. bad_spec — a non-judgeable interval contributes no allowance and no vote. |
| Staleness expiring on a timer launders a teleport after a pause (edge, measured) | high | A point five kilometres on accepted after 1000 s of teleports and a quiet stretch. Same re-derivation; the laundering test now fails if it reopens. |
| Both new thresholds and the sport cap can be hardcoded with the suite green (verification, measured) | high | Three mutations, all 83 tests passing. Patched with four "read from the options" tests; I re-ran the sport-cap mutation myself and two tests now fail. |
| `LeadingAgreementPositions` at zero makes the vote vacuous (blind, edge) | medium | Real and option-reachable: a leading teleport would be accepted. Patched — `Clean` refuses both new options when nonsensical. |
| `POSITIONS_DISCARDED` severity is unpinned and `Blocking` is the enum's zero value (blind, verification, measured) | medium | Flipping it to `Blocking` kept the suite green. Patched with an assertion beside its siblings. |
| `LongGapSeconds` is reused for a second, unrelated policy (blind, edge) | medium | Correct under AD-10: retuning the anchor gate would retune cleaning. Patched — `StaleReferenceVouchesForSeconds`. |
| The full-discard case the code claims to distinguish is untested (blind) | medium | Correct. Patched — an all-garbage activity now asserts both `POSITIONS_DISCARDED` and `NO_ORIGINAL_GPS`. |
| The leading vote is quadratic (verification, measured) | medium | 0.34 s at 8000 leading garbage records, 2.16 s at 20000, against SM-3's 3 s. Patched with a two-hour all-garbage timing test, which passes; the review's suggested fix is not exact, so the rescan stays and the cost is recorded. |
| The standing-start vote accumulates kilometres of allowance before the first real voter (blind, edge) | medium | Real under the summing rule. Closed by one-span judging. |
| The CLI's new line is executed by no test (verification) | medium | Correct: nothing runs `Program.cs`. Deferred onto the existing CLI-harness entry. |
| `ForSport` does not tune the judging window (blind) | low | Correct, and nothing is known to be wrong. Deferred with a note that only a real broken-GPS ride can settle the number. |
| AD-25 in the spine no longer describes the rule that shipped (blind) | medium | Correct and the most consequential of the doc items: a future story would re-derive the wrong rule. Deferred — the fix edits the spine. |
| The measurement counts in the code comment and the spec disagree (blind) | low | Correct; one of them was a second probe. Patched — the numbers live in the spec only. |
| `RepairService` mixes `original` and `cleaned` for provably identical values (blind) | low | Rejected: cleaning never drops a record nor touches timestamps or distance, so both reads are correct; the split names which series each value belongs to, which is the clearer contract once laps arrive. |
| The CLI reports the fact twice, as a count and as a warning code (blind) | low | Rejected: the code is for the shell, the line is for the reader, and the CLI's report format belongs to Story 1.11. |
| `FarAway` guards nothing and is always called with one bearing; `Home` duplicates its maths (blind, verification) | low | Correct. Patched — guarded, shared, and other bearings exercised. |
| `BiggestStep` is evaluated twice and throws below two positions (blind) | low | Correct. Patched. |
| Non-identical garbage that drifts beyond the same-place radius can still vote (implementer, pass 2) | low | Real but inside the judging window by design, where the forward rule would also accept it; only distance from the route betrays it, which the reference catches. Noted. |
| `PrivateFixtureTests` never runs the cleaner over the real pair (verification, implementer) | medium | Correct and unavoidable here — the private files are not on this machine. Deferred with the entry asking for one run where they are. |
