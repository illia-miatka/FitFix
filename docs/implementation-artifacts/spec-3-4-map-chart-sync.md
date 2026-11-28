---
title: 'Story 3.4 — Find on the map what I see on the chart'
type: 'feature'
created: '2026-11-28'
status: 'done'
baseline_commit: 'b062ef87b5ab382b0c92da2e276e95f236577970'
route: 'dispatch'
review_loop_iteration: 0
context: ['{project-root}/docs/implementation-artifacts/epic-3-context.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** An athlete who spots an odd stretch on the chart cannot tell where on the route it happened, and the map draws the whole repaired line in one colour, so the calculated stretches look as trustworthy as the measured ones.

**Approach:** The point under the pointer (or the keyboard cursor) on the chart puts a marker at the same record on the map; the index is the Original's record index, so thinning never shifts it (AD-23). The repaired line on the map takes the estimated colour inside estimated zones. A "Table" switch swaps the chart's plot for the same rows as a table. Chart to map only; no map-to-chart.

## Boundaries & Constraints

**Always:** The marker sits at the repaired position of that record index, read from `series.repaired` (AD-5). The map's zone colouring and the chart's band read the same `report.zones` list, with the core's boundary rule (a record on a zone's `toSeconds` belongs to the zone ending there), any number of alternations (AD-20). Keyboard: Tab onto the chart, arrows move the cursor, the marker follows exactly as for the pointer. The table is real `<table>` markup with a caption, scrollable and focusable, carrying time, distance, pace (speed for cycling) and heart rate. Strings only from `uk.ts` (AD-18), colours only through `theme.css`. The marker moves with no transition; reduced motion stays global.

**Never:** No server, core or contract change. No map-to-chart direction. No second marker for the original's position (deferred). No smoothing or thinning logic duplicated: table and chart read the same `chartRows`. No hex outside `theme.css`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Pointer over chart | Row with record index `i` | Marker at `route[i]`; moving off the plot removes it | N/A |
| Thinned chart | 7200 records, 1000 drawn | Marker at the row's own record index, not its position in the thinned list | N/A |
| Keyboard | Chart focused, arrow right | Cursor moves to the next drawn row, marker follows | N/A |
| Table on | Switch pressed | Plot replaced by a table of the same rows; marker cleared; switch `aria-pressed`, band and legend stay | N/A |
| Hole in a series | Pace or heart rate `null` | Cell shows a dash, never `NaN` or empty | N/A |
| No estimated zone | All zones measured | Whole line accent; no estimated legend item | N/A |
| Zones alternate | measured, estimated, measured | Estimated run amber, neighbours accent, line unbroken at the joins | N/A |
| No route | Fewer than one record | No map, no marker | N/A |

</frozen-after-approval>

## Code Map

- `web/src/components/Result.tsx` -- holds the active record index (`number | null`) and passes `onActiveIndex` to `TrustChart` and `activeIndex` to `RouteMap`.
- `web/src/components/TrustChart.tsx` -- a child inside `LineChart` using Recharts' `useActiveTooltipDataPoints` reports the active row's `index` (null when none); the table switch and `RowsTable`.
- `web/src/components/RouteMap.tsx` -- non-interactive cursor marker at `route[activeIndex]`; the repaired line split into zone runs; one legend item for the calculated stretch.
- `web/src/chart.ts` -- `ChartRow` gains `distanceMeters`; reuse `chartRows`, `bandSegments`.
- `web/src/routeZones.ts` (new) -- pure `zoneRuns(route, secondsFromStart, zones)`: runs of `{kind, positions}` sharing join records.
- `web/src/format.ts` -- reuse `duration`, `kilometres`, `pace`, `speed`.
- `web/src/i18n/uk.ts`, `web/src/theme.css` -- table strings, switch label, map legend item; `.route-repaired-estimated`, `.route-cursor`; no transition.

## Tasks & Acceptance

**Execution:**
- [x] `web/src/routeZones.ts` + test -- runs by zone with the boundary rule and shared joins -- every matrix row as a unit test
- [x] `web/src/chart.ts` + test -- `distanceMeters` on rows -- the table needs it
- [x] `web/src/components/TrustChart.tsx` + test -- active-index reporting (pointer and keyboard), table switch and table
- [x] `web/src/components/RouteMap.tsx`, `Result.tsx` + tests -- cursor marker, zone-coloured line, legend, wiring
- [x] `web/src/i18n/uk.ts`, `web/src/theme.css` -- strings and styles

**Acceptance Criteria:**
- Given a chart and a map, when the athlete hovers a chart point, then a marker appears on the map at the repaired position of that record, and disappears when the pointer leaves.
- Given thinning, when a row stands for record `i`, then the marker is at `route[i]`.
- Given the keyboard, when the athlete Tabs to the chart and presses arrows, then the values show and the marker moves as for the pointer.
- Given the Table switch, when pressed, then a labelled table with time, distance, pace and heart rate replaces the plot, reachable by a screen reader.
- Given estimated zones, when the map is drawn, then those runs of the line are in the estimated colour and the rest in the accent.

## Implementation Notes

- `routeZones.ts` cuts the repaired line into runs by zone (boundary rule of the core, shared join records); `RouteMap` draws measured runs as `route-repaired`, estimated ones as `route-estimated`, plus one legend line when an estimated run exists.
- `TrustChart` reports the active row's `index` through `useActiveTooltipDataPoints` inside the chart (pointer and arrow keys alike); the parent `Result` holds the index and `RouteMap` draws a non-interactive ring at `route[index]`.
- The table swaps only the plot; the band and legend stay. It is a focusable region with a caption and column headers.
- Not verified: the pointer path (jsdom has no layout, Recharts reports no hover there) and a look in a real browser on the real pair; the keyboard path, leaving by blur, and the unmount are tested. Deferred: the original's own position as a second marker.

## Review Triage Log

Layer run: verification-gap only (standing instruction to spend fewer tokens).

| Finding | Verdict | Route | Evidence |
|---|---|---|---|
| Cursor is verified arriving by keyboard but never leaving | medium | patch | Real: a stale marker after leaving would pass every test. Added a blur test in `TrustChart.test.tsx` and in `Result.test.tsx` (marker count returns to 0). The pointer-leave path cannot run in jsdom and stays unverified. |
| Cyclist table asserts the header, not the speed cell | low | rejected | Cells share the tooltip's `format`, which is tested; a second cell assertion adds nothing. |

## Design Notes

Result owns the index because it is the one parent of both views; a hover re-renders only light siblings, and `RouteMap`'s memoised lines do not redraw. The cursor is a `CircleMarker` with `interactive={false}`, so it takes no focus and Leaflet's own keys keep working.
