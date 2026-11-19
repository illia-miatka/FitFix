# Epic 1 Context: Repair You Can Trust

<!-- Compiled 2026-11-19 from the PRD, the architecture spine and the epics file in docs/planning-artifacts/.
     Edit freely. Regenerate with compile-epic-context if those planning docs change. -->

## Goal

A runner whose watch lost GPS mid-activity can already fix the file today from the command line, without a server or a browser — and the result now matches the architecture contract instead of only looking right. This epic closes the gap between working code and the invariants that govern it: garbage GPS points never reach the computation, pauses stop inflating distance, laps survive, every summary number has exactly one owner, measured and estimated stretches become explicit intervals, and failures carry machine-readable codes instead of human sentences. Nothing here needs a web layer, so the epic ships value on its own.

## Stories

- Story 1.1: CI catches algorithm regressions
- Story 1.2: Every core refusal carries a code
- Story 1.3: No threshold outside the options object
- Story 1.4: Garbage points reach neither the computation nor the map
- Story 1.5: Pauses do not inflate distance
- Story 1.6: Original laps survive the repair
- Story 1.7: One owner computes the summaries
- Story 1.8: An over-long track does not inflate pace
- Story 1.9: Zones become explicit intervals
- Story 1.10: The written FIT has a stable structure and correct local time
- Story 1.11: The CLI prints the full report

## Requirements & Constraints

- Repairing a two-hour activity must finish in under three seconds; the current implementation takes ~0.1 s on a 36-minute file, so synchronous processing stays realistic.
- Every generated file must parse back and carry well-formed session, lap and activity messages.
- Average pace across the measured stretch must stay within 5% of the athlete's real pace.
- The share of the activity covered by measured anchors is explicitly **not** a metric to optimize. Widening the anchor tolerance to produce prettier numbers is forbidden: it was tried at 35 m and the algorithm latched onto garbage.
- Total elapsed time must match the original within one second; moving time must account for pauses; total distance must match the chosen track length within 0.5%.
- Sensor values — heart rate, power, cadence, temperature — are never altered, only carried across at the same timestamps.
- Zero power that the device derived from a dead GPS signal must not be carried into the estimated stretch.
- Logs never contain coordinates, file contents or uploaded file names.
- Real user files live only in `tests/fixtures/private/` and stay out of the repository; tests that need them exit quietly when they are absent. Synthetic fixtures are generated in code.

## Technical Decisions

- **Pure core, thin shell.** Repair is a deterministic function over models. The core knows file formats but never HTTP, configuration or logging. Dependency direction is one-way; a reverse reference is a build error, not a style issue.
- **Every data source becomes a core model.** Anything that produces an activity — a file today, Strava streams later — is mapped to the domain model inside the core. The repair entry point accepts models, never bytes.
- **One code registry.** Warning and error codes are declared in a single place together with a severity of `blocking` or `info`. Domain exceptions carry the code; throwing a generic argument exception for user data is forbidden. Human-readable strings live only in the UI dictionary.
- **Every tuning threshold lives in the options object**, including track de-duplication spacing, the device-distance window, the speed-estimation window and the minimum anchor count. Public core methods expose no numeric default parameters, so two entry points cannot silently disagree on the same files.
- **Pauses are intervals**, read from timer stop/start events, not a per-record flag. A derived "moving" notion is computed, never stored.
- **Laps are part of the model.** The original's lap intervals are read in, their distances recomputed from the alignment, and the writer only transcribes.
- **One owner for aggregates.** All summary values are computed in the core; the FIT writer and any transport layer transcribe finished numbers and contain no arithmetic over series.
- **Physically impossible points are discarded first.** A point is dropped when the implied speed from the previous valid point exceeds the sport maximum (running 7 m/s, cycling 25 m/s, anything else 15 m/s) — the criterion is impossibility in time, not distance, so a long ride across several cities survives. Cleaning happens before the track-compatibility check, before anchoring and before preview series are built.
- **Chosen track length.** The core computes both the full track length and the length consistent with elapsed time and a plausible speed. Speed source priority: a real sensor when the device declares one, otherwise the same sport maximum as above. Device speed derived from GPS is never a source — after a GPS failure it reads zero.
- **Distance is monotonically non-decreasing** across the whole activity and ends exactly on the chosen track length. Smoothing windows are clipped at pause boundaries, and monotonicity is verified after smoothing, not instead of it.
- **Stable output structure.** The written file always orders messages the same way, carries the original device identity unchanged, and derives local time from the original's own offset so every entry point produces identical output.

## Cross-Story Dependencies

- Story 1.3 (the options object) comes before every story that introduces a threshold — 1.4, 1.5 and 1.8 all add tuning values that must land there rather than in a signature default.
- Story 1.7 (summaries) needs pauses from 1.5 and laps from 1.6 to compute moving time and lap distances.
- Story 1.9 (zones) operates on the series already cleaned by 1.4.
- Story 1.10 (file structure) transcribes the summaries produced by 1.7 and the laps from 1.6.
- Story 1.8 registers its own over-long-track warning in the registry established by 1.2.
- Story 1.11 (the CLI report) is last in the epic: it prints the summaries from 1.7, the zone intervals from 1.9, the laps and pauses from 1.6 and 1.5, and the warning codes from 1.2 rendered as human text.
- Story 1.1 deliberately covers only the .NET test run; frontend build steps are added in Epic 2, when a frontend exists.
- Nothing in this epic depends on Epic 2 or Epic 3.
