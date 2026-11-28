---
title: 'Story 3.1 — See what changed in two seconds'
type: 'feature'
created: '2026-11-28'
status: 'done'
baseline_commit: '76c7fcc381eff84dc11846789193db2154069218'
route: 'dispatch'
review_loop_iteration: 0
context: ['{project-root}/docs/implementation-artifacts/epic-3-context.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The result shows three loose cards (distance, time, pace) with the old value in grey and the new in lime. There is no heart rate, a cyclist sees a pace instead of a speed, the old value is not struck out, and nothing reads as the one headline of the screen.

**Approach:** One hero card holds up to four rows, each "was → now": distance, total time, average pace (average speed for cycling) and average heart rate. The new value is in the accent, the old muted and struck through, all figures tabular. The card has a soft lime glow: a radial gradient of about 6 % opacity.

## Boundaries & Constraints

**Always:** Every figure is computed in `format.ts` from the contract's metres and seconds, never inline (AD-6, AD-14). The old value of the pace and speed is the device's recorded distance over the same seconds, as now. Text only from `uk.ts` (AD-18), colour and the gradient only in `theme.css`. A row whose old and new values are equal (time, heart rate: the repair keeps the Original's clock and sensors) shows both values but does not strike the old one, since a struck-out unchanged figure says it was wrong. The card with the glow is a `dl` so a screen reader still reads label, then "was", then "now".

**Never:** No server, core or contract change. No card for a missing heart rate: neither a zero nor a dash stands in for it. No change to the download, the warnings or the map and chart placement (Story 3.6's grid stays: the hero card is the metrics grid item). No new colour token; the glow is derived from `accent` with `color-mix`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Running | `sport: running`, distance, time, pace, heart rate known | Four rows: distance, time, pace, heart rate, each was → now | N/A |
| Cycling | `sport: cycling` | The third row is average speed in km/h, labelled as speed | N/A |
| No heart rate | `averageHeartRate: null` | Three rows; no heart-rate row at all | N/A |
| No recorded distance | `recordedDistanceMeters: null` | Distance and pace "was" say there was none (as now), no strike-through on that text | N/A |
| Unchanged value | Time and heart rate | Old and new equal: both shown, old not struck through | N/A |
| Changed value | Distance, pace, speed | Old muted and struck through, new in the accent | N/A |
| Glow | The hero card | Radial gradient from `accent` at about 6 % over transparent, defined in `theme.css` | N/A |

</frozen-after-approval>

## Code Map

- `web/src/components/Result.tsx` -- the metrics `dl` becomes the hero card (keeps `order-1 md:order-2 md:col-span-5`); `Delta` loses its own card border, gets strike-through and `tabular-nums`; rows for speed and heart rate.
- `web/src/format.ts` -- add `averageSpeed(metres, seconds, texts)` next to `pace`; reuse `speed`.
- `web/src/chart.ts` -- `measureFor(sport)` already decides pace or speed; reuse it.
- `web/src/i18n/uk.ts` -- `result.speed`, `result.heartRate`.
- `web/src/theme.css` -- `.hero-card` with the gradient.
- `web/src/theme.test.ts`, `format.test.ts`, `Result.test.tsx` -- the glow, the speed, every matrix row.

## Tasks & Acceptance

**Execution:**
- [x] `web/src/format.ts` + test -- average speed
- [x] `web/src/components/Result.tsx`, `uk.ts`, `theme.css` + tests -- hero card, four rows, strike-through, glow
- [x] Real browser against a synthetic result at 375 and 1440 px: the card reads, the glow is faint, nothing overflows

**Acceptance Criteria:**
- Given a result, then a hero card shows distance, total time, average pace and average heart rate, each was → now, the new in the accent and the old muted and struck through where it differs.
- Given cycling, then the third row is average speed.
- Given no heart rate, then there is no heart-rate row.
- Given the hero card, then its glow is a radial gradient of about 6 % accent opacity defined in `theme.css`.

## Implementation Notes

- The metrics `dl` is the hero card (`hero-card`, still the grid item of Story 3.6). `Delta` takes `changed`: the old value is struck only when the repair changed it and the old side is a value, not the "recorded none" sentence. Speed and pace share the rule; the heart-rate row exists only when `averageHeartRate` is not null.
- Looked at in a real browser (built image, synthetic result) at 624 and 1440 px: four rows, struck distance and pace only, a computed gradient of accent at 0.06 alpha, no horizontal scroll. The cyclist card and the no-heart-rate card are covered by tests only.

## Review Triage Log

Layer run: verification-gap only (standing instruction to spend fewer tokens).

| Finding | Verdict | Route | Evidence |
|---|---|---|---|
| Cyclist speed row's strike-through untested | medium | patch | Real: the speed `Delta` could lose `changed` unnoticed. Added a cyclist strike test. |
| Pace and speed rows with no recorded distance: sentence strike untested | medium | patch | Real: only the distance row was checked. The no-distance test now runs for running and cycling and asserts the third row; mutation (pace always `changed`) fails it. |
| Heart-rate row with null average | none | no change | The reviewer checked it: `number \| null` and the API serialises nulls, so the guard is sound and already tested. |

## Design Notes

"Hero" is the card DESIGN.md names for the deltas; the lime area stays a small share of the screen because the gradient is 6 % opacity and the saturated accent is only the new values.
