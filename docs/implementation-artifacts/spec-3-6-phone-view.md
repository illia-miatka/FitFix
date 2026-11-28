---
title: 'Story 3.6 — Look at the result from a phone'
type: 'feature'
created: '2026-11-28'
status: 'done'
baseline_commit: 'a338795a3e2efac8f5c69a9b0131977a069598b1'
route: 'dispatch'
review_loop_iteration: 0
context: ['{project-root}/docs/implementation-artifacts/epic-3-context.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The result screen stacks the map, the numbers and the chart in one column at every width, so a desktop screen wastes its width, and on a phone the map is 38 px-tall pills, 18 px pins and 30 px zoom buttons away from being usable with a thumb.

**Approach:** From 768 px up the result is a two-column grid: the map takes 7/12 and the three metrics 5/12 beside it, the chart full width under both. Below 768 px it is one column with the numbers first, a 320 px map and no horizontal scroll. Every control on the result is at least 44×44 px at every width. A test computes the contrast of every text and surface pair of the theme.

## Boundaries & Constraints

**Always:** Layout through Tailwind utilities and `theme.css` only; no new colour, no new text (AD-18). The container stays 1200 px (DESIGN.md). Map, metrics and chart keep their content and behaviour; only placement and target size change. Targets are made 44×44 with the visible shape unchanged where it is a pin (a larger hit box around it), and zoom buttons grow to 44 px. A text link inside a sentence (the basemap credit) keeps its size: it is the inline exception of WCAG 2.5.8 and the credit is required by OpenStreetMap. Tab order does not change: the metrics hold nothing focusable, so reordering them on a phone moves no focus.

**Never:** No server, core or contract change. No new breakpoint other than the existing 768 px `md`. No hover-only interaction. No change to the chart's or the map's data.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Desktop | Width 1024, 1200 or 1440 px | Map 7/12 beside the metrics 5/12, chart full width below, gates and warnings above | N/A |
| Tablet | 768 px | The same two columns | N/A |
| Phone | 375 px | One column: warnings, gates, metrics, map (320 px high), chart; `scrollWidth` equals the viewport | N/A |
| Targets | Any width | Toggles, draw and table buttons, drawing toolbar, decision buttons, pins and zoom buttons measure at least 44×44 px | N/A |
| Contrast | The theme's tokens | Every text colour on `bg`, `surface` and `surface-raised` is at least 4.5:1 | A failing pair fails the test |
| Reduced motion | `prefers-reduced-motion` | Transitions are off globally (already in `theme.css`, kept under its test) | N/A |

</frozen-after-approval>

## Code Map

- `web/src/components/Result.tsx` -- the result grid (map and metrics side by side from `md`, order classes for the phone), metrics as a column, chart `md:col-span-12`.
- `web/src/components/RouteMap.tsx` -- pins' `iconSize` and the toggle and draw buttons.
- `web/src/components/StretchDrawing.tsx`, `TrustChart.tsx` -- tool buttons and the table switch.
- `web/src/theme.css` -- `.route-pin`, `.route-pin-shape` sized inside a 44 px box; Leaflet zoom buttons at 44 px.
- `web/src/theme.test.ts` -- the contrast test (parses the tokens, computes WCAG ratios).
- `web/src/components/Result.test.tsx` -- the grid contract by class.

## Tasks & Acceptance

**Execution:**
- [x] `web/src/components/Result.tsx` + test -- grid and order classes
- [x] `web/src/theme.css`, `RouteMap.tsx`, `StretchDrawing.tsx`, `TrustChart.tsx` -- 44 px targets
- [x] `web/src/theme.test.ts` -- contrast of every text token on every surface token
- [x] Real browser at 375, 768, 1024 and 1440 px against a synthetic result: no horizontal scroll, columns where they should be, every target measured

**Acceptance Criteria:**
- Given 768 px or wider, when the result is shown, then the map is 7/12 and the metrics 5/12 side by side and the chart is full width under them.
- Given under 768 px, then one column, the metrics above the map, a 320 px map, no horizontal scroll.
- Given 375, 768, 1024 and 1440 px, then no control of the result measures under 44×44 px (the inline credit link excepted) and every text pair is at least 4.5:1.
- Given reduced motion, then transitions are off.

## Implementation Notes

- `Result` is one 12-column grid from `md`: map `md:col-span-7`, metrics `md:col-span-5`, chart `md:col-span-12`; on a phone `order-1/2/3` puts metrics, map, chart. Buttons carry `min-h-11`, pins a 44 px box around the 18 px shape, Leaflet's zoom buttons 44 px.
- Measured in a real browser (built image, synthetic 1200-record result) at 375, 768, 1024 and 1440 px: no horizontal scroll at any width; map 320 px high on a phone and 7/12 beside 5/12 from 768 px; the only control under 44×44 px is the inline basemap credit link (the WCAG 2.5.8 inline exception); with the drawing toolbar and the table open at 375 px the same holds.
- The contrast test lists `original` on `bg` and `surface` only: its text use is the chart's end label on the page, and it is 4.44:1 on `surface-raised`, a pair no screen has.

## Review Triage Log

Layer run: verification-gap only (standing instruction to spend fewer tokens).

| Finding | Verdict | Route | Evidence |
|---|---|---|---|
| Pin box size (`iconSize` 44) pinned by no test | medium | patch | Real: reverting to 18 left every test green. Added an inline 44 px width/height assertion per pin; mutation (back to 18) now fails it. The CSS margin half cannot be seen in jsdom and stays with the manual measurement. |
| Empty 7/12 when the map or chart returns null | low | rejected | A report with no repaired positions is not a state the service produces (the repaired series has a position everywhere). |
| Layout tests check class names, not layout | low | rejected | A limit of jsdom, not a defect; the real layout was measured in a browser. |
| Dropzone clear button is 32 px | low | defer | Outside the result screen, which is what the story scopes; recorded in `deferred-work.md`. |

## Design Notes

The DOM order is map, metrics, chart, so on a desktop the visual and the reading order agree; on a phone the metrics take `order-first` so the numbers are not pushed under a 320 px map.
