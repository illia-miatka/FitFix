---
title: 'Story 5.4 — Draw the missing stretch on the map'
type: 'feature'
created: '2026-11-28'
status: 'done'
baseline_commit: '632c3284b832b29675c56160e227fc57dbebd797'
route: 'dispatch'
review_loop_iteration: 0
context: ['{project-root}/docs/implementation-artifacts/epic-5-context.md', '{project-root}/docs/implementation-artifacts/epic-3-context.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A track that does not cover the broken stretch can only be fixed by finding or drawing the missing kilometres in an online editor, exporting a file and uploading it. The map of Story 3.2 already shows where the watch's own position ends and the estimate begins, but it is read-only.

**Approach:** On the result map the athlete opens a drawing mode, places points between the two ends of the estimated stretch, and confirms. The browser turns the points into a GPX and repeats the repair with that file as the track and the coverage stated as a fragment (Story 5.2's mechanism), so no file is chosen and the server changes nowhere. Straight segments between the points; no road snapping (a later story, with a switch).

## Boundaries & Constraints

**Always:** The ends come from the series and zones the service sent, never from the UI's own arithmetic on the contract (AD-14): the start end is the last record of the measured zone that precedes the estimated one, the finish end the first record of the measured zone that follows it, absent for a tail. Text only from `uk.ts` (AD-18), colours only from `theme.css`. Every action has an inverse (undo, redo, remove last, clear) and runs from the keyboard. The generated GPX never leaves the browser except as the repair request; coordinates are never logged (AD-9).

**Never:** No server, core or contract change. No road snapping, no "no track" entry, no outlier removal and no editing of existing points (deferred, split from this story). No persistence of a drawing. No second map library or drawing plugin.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Tail stretch | Result with one estimated zone ending at the last record | Start end pinned; the line is start end plus the points | N/A |
| Middle stretch | Estimated zone followed by a measured one | Both ends pinned; the line runs start end, points, finish end | N/A |
| No estimated zone or no start end | Whole run measured, or the estimate begins at 0 | Drawing is not offered | N/A |
| Confirm with no point | Zero points placed | Confirm is disabled | N/A |
| Repeat repair refused | Server answers with a code | The result stands unchanged and the dictionary's sentence for the code is shown | Unreachable shows the retry notice; the drawing is kept |
| Keyboard | Focus on the map | Arrows pan, Enter adds a point at the map centre, Backspace removes the last, Ctrl+Z / Ctrl+Shift+Z undo and redo | N/A |

</frozen-after-approval>

## Code Map

- `web/src/components/RouteMap.tsx` -- Story 3.2's map; `measuredEndIndex` already finds the start end. Mount the drawing layer and toolbar here; do not change the existing lines and pins.
- `web/src/drawing.ts` (new) -- pure: `stretchEnds(zones, secondsFromStart, route)`, the point history (add, remove last, undo, redo, clear) and `drawnGpx(points)`.
- `web/src/App.tsx` -- `answerCoverage` repeats the repair with `original` and `track`; add the drawn variant, which also stores the drawn file as `track` so a later answer repeats the right request.
- `web/src/api/repair.ts` -- `repair(original, track, mode?, coverage?)` already accepts any `File`; unchanged.
- `web/src/components/Result.tsx` -- passes the handler and state down to the map; reuse `CoverageAnswer`.
- `web/src/i18n/uk.ts`, `web/src/theme.css`, `web/src/test/fixtures.ts` -- strings, drawing classes (line, points), a route with a middle and a tail zone.

## Tasks & Acceptance

**Execution:**
- [x] `web/src/drawing.ts` + test -- ends and history as pure functions, GPX writer with escaping-free numeric output -- so every edge case is a unit test, not a click
- [x] `web/src/components/RouteMap.tsx` + test -- drawing mode: click adds, centre crosshair with Enter adds, toolbar with undo, redo, remove last, clear, confirm and cancel, all named from the dictionary; the drawn line in its own class
- [x] `web/src/App.tsx`, `web/src/components/Result.tsx` + tests -- repeat the repair from the drawn points, replace the report, show a refusal's sentence, keep the drawing on failure
- [x] `web/src/i18n/uk.ts`, `web/src/theme.css` -- strings and styles

**Acceptance Criteria:**
- Given a result with an estimated zone, when the athlete starts drawing, then the two ends are shown and the line runs from the start end through the points to the finish end.
- Given a drawn stretch, when the athlete confirms, then the repair repeats with a GPX of the drawn line as the track and `trackCoverage=fragment`, and no file was chosen.
- Given drawing, when the athlete works without a mouse, then points are placed and removed from the keyboard and every action can be undone.
- Given the repeat repair is refused or unreachable, then the previous result is untouched and the drawing is still there.

## Implementation Notes

- `drawing.ts` holds everything pure: `stretchEnds` (ends from zones and records by the core's own boundary rule), a snapshot history (add, remove last, clear, undo, redo), `drawnLine`, `drawnGpx`, `drawnTrackFile`. A snapshot history rather than inverse operations, so clear is undoable for free.
- `StretchDrawing.tsx` has the Leaflet layer (click adds; Enter at the map centre, Backspace, Ctrl+Z, Ctrl+Shift+Z act only while the map container itself has focus, so Enter on a marker still opens its label), the toolbar, and `DrawAnswer`. `RouteMap` owns the mode, the busy flag and the failure message; `Result` turns a `CoverageAnswer` into a `DrawAnswer` with the dictionary's sentence for a code.
- `App.answerCoverage` takes an optional replacement track; the drawn file also replaces the held `track`, so a later answer repeats the drawn request.
- A drawn stretch is amber (the estimated colour), its points filled, the two ends hollow rings; the crosshair is a sibling of the map in an `isolate` wrapper so the map's pane z-indexes cannot cover it.
- Verified in a real browser against a real pair: ends and crosshair appear, clicks place points, a real Enter and Ctrl+Z move the count, a nonsense drawing is refused with the service's own sentence and the drawing survives. The success path was checked through the API with a GPX written like ours: the middle fragment was accepted (200, `redrawn` 21-180 s, 479 m). The success path through the UI itself was not clicked end to end.
- Limitation, deliberate: the first estimated stretch that follows a measured one is the one drawn. A run with several broken stretches (the real pair has three) offers only the first; choosing among them is deferred.

## Review Triage Log

Layer run: verification-gap only (standing instruction to spend fewer tokens).

| Finding | Verdict | Route | Evidence |
|---|---|---|---|
| No test that the drawn file replaces the held track (`setTrack(replacement)`) | medium | patch | Real: deleting the line left every test green, and a later fragment answer would have resent the old `route.gpx`. Added an App test that answers the fragment question after a drawing and asserts the third request carries `drawn-stretch.gpx`; mutation (line removed) now fails it. |
| Result's adapter (no-code branch, absent prop) untested | low | patch | Cheap to close: three Result tests (no button without the prop, the map's own sentence for no code, the dictionary sentence for a code). Upgraded from the reviewer's defer. |
| First App test pins only the `trkpt` count, not the start end | low | patch | Added an assertion that the first position is record 5 of the fixture. |
| A refused redraw leaves the old track in place, untested | low | rejected | Behaviour is the default of `answerCoverage` (set only on success); a guard test adds no protection beyond the refusal test that already keeps the drawing. |
| Formatting-only reflows in the diff | low | rejected | A formatter pass over files this story touched; no behaviour change, and reverting it would be churn of its own. |

## Design Notes

A record on a zone's `toSeconds` belongs to the zone that ends there (`RepairService.MeasuredPositions`), so the start end is the last record with a second at or before the measured zone's end, and the finish end is the first record after the estimated zone's end. A GPX written from the points carries no time and no altitude: the service takes time from the Original and treats a track without altitude as it already does.

## Verification

**Commands:**
- `npm --prefix web run typecheck` -- expected: no errors
- `npm --prefix web run test` -- expected: all pass
- `npm --prefix web run build` -- expected: builds

**Manual checks (if no CLI):**
- In the browser against a real pair: draw a stretch, confirm, the map redraws with the drawn line and the file downloads; the same with the keyboard only.
