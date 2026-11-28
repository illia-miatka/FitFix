---
title: 'Story 3.3 — See where the GPS lied'
type: 'feature'
created: '2026-11-28'
status: 'done'
baseline_commit: '230f0e8a50ca061a86003ac1edc817b662646305'
route: 'dispatch'
review_loop_iteration: 0
context: ['{project-root}/docs/implementation-artifacts/epic-3-context.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The result shows a map and three numbers, but not which part of the run the position was measured over and which part was calculated, nor whether the repaired pace and the heart rate tell the same story as the watch's own. Somebody deciding whether to trust the file cannot see where to be careful.

**Approach:** One chart under the map: the repaired pace (solid), the original's pace (dashed) and the heart rate against time, with a band beneath it that colours every trust zone and a legend in words. The series are thinned in the browser only, to at most 1000 points that keep their original record index (AD-23). For cycling the chart shows speed instead of pace. Recharts 3 draws it.

## Boundaries & Constraints

**Always:** Read the repaired and original series as the contract sends them, by record index, never thinned by the server (AD-5); SI units in, pace and speed formatted in the UI (`format.ts`, AD-6, AD-14). Zones come from `report.zones` as explicit intervals, any number of alternations (AD-20). Series differ by line style and a direct label, never by hue alone. Colours only through `theme.css` tokens, strings only from `uk.ts` (AD-18). Animations off (reduced motion is global).

**Never:** No server, core or contract change. No hover-to-map sync (Story 3.4) and no zone colouring on the map (deferred to 3.4, where the two views meet). No smoothing of the heart rate. No hex colour outside `theme.css`.

**Smoothing (renegotiated by the human on 2026-11-28 after the real pair showed a scribble):** speed is smoothed with a centred moving average of ±15 s over every record, then thinned, and pace is computed from the smoothed speed. A hole in a series stays a hole.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Running | `sport: running` | Left axis shows pace (min/km, faster is up), tooltip in `m:ss /км` | N/A |
| Cycling | `sport: cycling` | Speed in km/h instead of pace, labelled as speed | N/A |
| Original has holes | Original speed `null` for a stretch | The dashed line breaks there; it is not drawn across the gap | N/A |
| Jittery speed | A one-second spike in speed | Averaged into its neighbours within ±15 s, not drawn as a spike | N/A |
| Standing still | Speed at or below 0.3 m/s | No pace for that record (a gap), never `Infinity` | N/A |
| No heart rate | Every heart-rate value `null` | No heart-rate line and no right axis | N/A |
| 7200 records | Series of 7200 | At most 1000 points drawn, first and last kept, each with its original index | N/A |
| Several zones | Zones alternate measured, estimated, measured, estimated | The band shows every interval in order, widths proportional to seconds | N/A |
| Nothing to draw | Fewer than two records | No chart section | N/A |

</frozen-after-approval>

## Code Map

- `web/src/components/Result.tsx` -- mount the chart between the map and the actions; passes `report.series`, `report.zones`, `report.before.sport`.
- `web/src/format.ts` -- `pace` and `duration` already exist; add a speed formatter (km/h) here, not in the chart.
- `web/src/chart.ts` (new) -- pure: `thinIndices(length, max)`, `chartRows(series, sport)`, `bandSegments(zones)`.
- `web/src/components/TrustChart.tsx` (new) -- Recharts `LineChart` with two Y axes, the band and the legend. Colours as `var(--color-*)` values.
- `web/src/i18n/uk.ts`, `web/src/theme.css`, `web/src/test/setup.ts` -- chart strings, band classes, a `ResizeObserver` shim for jsdom.
- `web/package.json` -- add `recharts` and its peer `react-is`.

## Tasks & Acceptance

**Execution:**
- [x] `web/package.json` -- add `recharts` ^3 and `react-is` -- the chart library the epic fixed
- [x] `web/src/chart.ts` + test -- thinning that keeps first, last and original indices; pace/speed rows with gaps; band segments -- every matrix row as a unit test
- [x] `web/src/format.ts` + test -- speed in km/h
- [x] `web/src/components/TrustChart.tsx` + test -- lines (solid, dashed, heart), direct labels, band, legend in words, text alternative
- [x] `web/src/components/Result.tsx`, `web/src/i18n/uk.ts`, `web/src/theme.css` -- mount and style

**Acceptance Criteria:**
- Given a result, when the chart is shown, then the repaired pace is solid, the original dashed, the heart rate a third line, and each has a label next to it.
- Given zones, when the band is drawn, then every interval has its colour (`accent` measured, `estimated` calculated) and a legend names both in words.
- Given a 2-hour run, when the chart is drawn, then at most 1000 points are rendered and each keeps its original record index.
- Given a cycling activity, when the chart is shown, then it shows speed, not pace.

## Implementation Notes

- `chart.ts` holds the pure parts: `thinIndices` (even stride, first and last always kept), `movingAverage` (prefix sums and two pointers, window in seconds, holes stay holes), `chartRows` (smooth over all records, thin, then convert speed to pace or km/h), `valueDomain` (2nd to 98th percentile so a stray second does not flatten the axis), `lastWith` (end-label position, only a value inside the axis range), `bandSegments`.
- `TrustChart.tsx` is Recharts 3: `ResponsiveContainer` with an `initialDimension` so jsdom renders; two Y axes (the left reversed for pace), curves styled by class on the Line group's `.recharts-line-curve` because Recharts writes its own default `stroke` on the path, which beats anything inherited; labels at the end of each line with a halo in the page colour; the zone band is plain elements padded by the axis width, plus a screen-reader list of intervals and a legend in words.
- Verified in a real browser on the Chabany pair: lines, three end labels, hover tooltip (17:12, pace, original pace, heart rate), the band with all seven intervals. The first real look found the unreadable raw pace (now smoothed), a label placed at a value the axis cut off (now skipped), and clipped paths from Recharts' default stroke (now aimed at the curve).
- The bundle is now 843 kB (Recharts); code-splitting the chart is deferred.

## Spec Change Log

- 2026-11-28, human decision during implementation: the frozen "No smoothing of the series" was lifted for speed and pace. Trigger: on the real Chabany pair the per-second pace jumped between 1:44 and 7:40 /km and lines left the plot, unreadable. Amended: Never (heart rate stays unsmoothed), a Smoothing paragraph, one matrix row. Avoids a chart faithful to the second and useless to read. KEEP: smooth every record first and thin afterwards, holes stay holes, the 2nd and 98th percentile axis range, labels placed at the last value inside the axis range.

## Review Triage Log

Layer run: verification-gap only (standing instruction to spend fewer tokens).

| Finding | Verdict | Route | Evidence |
|---|---|---|---|
| Hover read-out (`ChartTooltip`) untested | medium | patch | Real: swapping names or dropping the null guard left every test green. `ChartTooltip` is exported and tested directly (time, each series under its own name, null series omitted, empty when not hovered). |
| Smoothing before thinning not pinned; the two lines indistinguishable in component tests | medium | patch | Real: reverting to raw speeds kept the suite green. Added spike and smooth-all-records tests in `chart.test.ts`, a distinct-series component test; mutation (raw repaired speeds) now fails. |
| Y-axis direction, tick format, end-label position unasserted | low | patch (ticks) / defer (direction, placement) | Tick text asserted for running (`m:ss`) and cycling (whole km/h). Direction and label placement are layout, checked by eye in the browser; jsdom gives no real geometry. |
| Band misaligned with the plot when there is no heart rate | medium | patch | Real: the band always kept a 56 px right inset, but the right axis exists only with a heart rate. Fixed with a `rightAxis` flag; mutation (always inset) fails the new test. |
| `bandSegments` shares assume contiguous zones; band span vs x-axis span | low | rejected | The service's zones tile the activity; the doc comment now says so. A gap-aware layout would add complexity for a state the service does not produce. |
| Dead `void series` in a test | low | patch | Removed. |

## Design Notes

Pace is a derived quantity and speed is what the contract carries, so the chart plots seconds per kilometre computed from speed, with the Y axis reversed (faster upward). Thinning is a stride that always keeps the first and last record; the zone band is not thinned, since it is drawn from intervals rather than points.

## Verification

**Commands:**
- `npm --prefix web run typecheck` -- expected: no errors
- `npm --prefix web run test` -- expected: all pass
- `npm --prefix web run build` -- expected: builds

**Manual checks (if no CLI):**
- Against a real pair in the browser: the chart draws, the band shows the three real estimated stretches, hover reads values.
