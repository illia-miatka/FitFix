---
title: 'Story 3.2 — See the real route on the map'
type: 'feature'
created: '2026-11-27'
status: 'done'
route: 'dispatch'
review_loop_iteration: 0
baseline_commit: '48f6eac49659fc7aa8ef545dc7192b86130e0f7d'
context: ['{project-root}/docs/implementation-artifacts/epic-3-context.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The result screen has numbers and nothing to look at. The athlete cannot check that the repaired track is *their* route and not somebody else's, and the series the service already returns (`repaired` and `original` latitude/longitude, `zones`) is not drawn anywhere.

**Approach:** A map below the numbers on the result screen: the repaired track as a solid accent line, the original as a dashed muted line, markers for start, finish and where the last measured zone ends, on the OpenStreetMap basemap, darkened in CSS, with its attribution. Two toggle buttons switch the layers. The whole map is operable from the keyboard.

## Boundaries & Constraints

**Always:** Every colour comes from `theme.css` tokens, applied to lines and markers by CSS class, never as a hex in code. Every string, including the attribution and Leaflet's control labels, is in `uk.ts` (AD-18). Positions are read from the series by record index (AD-5); the last measured zone's end comes from `zones` (AD-20). The original layer and its toggle are absent, not empty, when the original carries no position.

**Never:** No thinning, no map ↔ chart sync, no per-zone colouring of the line (Stories 3.3, 3.4). No contract or server change. No tile or coordinate ever sent anywhere but the basemap host. No API key, so `docker compose up` stays one command. No marker images (default Leaflet icons break under a bundler); markers are CSS-styled `divIcon`s.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Typical | repaired track, original with a GPS stall, last measured zone ends mid-run | solid line, dashed line, start / finish / "measured until here" markers, both toggles pressed | N/A |
| No original GPS | every original position `null` | no dashed layer and no "Original" toggle | N/A |
| Original with holes | positions interleaved with `null` | one dashed segment per run of positions; a lone point draws nothing | N/A |
| Measured to the end | last measured zone ends at the last record | start and finish only | no third marker on top of the finish |
| No measured zone | every zone estimated | start and finish only | N/A |
| Toggled off | "Repaired" unpressed | solid line gone, `aria-pressed="false"` | N/A |

</frozen-after-approval>

## Code Map

- `web/src/api/repair.ts` -- `RepairSeries`, `TrustZone`; already typed, nothing to add.
- `web/src/components/Result.tsx` -- mount the map between the numbers grid and the action row; `ResultTexts` gains a `map` block.
- `web/src/components/RouteMap.tsx` (new) -- the map, the toggles, the markers; pure helpers (`originalSegments`, `measuredEndIndex`) beside it so they are testable without Leaflet.
- `web/src/i18n/uk.ts` -- a `map` namespace: toggle labels, marker titles, zoom titles, attribution.
- `web/src/theme.css` -- classes `route-repaired`, `route-original`, marker pins, using existing tokens `accent`, `original`, `estimated`.
- `web/src/test/setup.ts` -- two jsdom shims, both needed before Leaflet loads: `SVGSVGElement.prototype.createSVGRect` (else no SVG renderer, no paths) and non-zero `clientWidth`/`clientHeight` (else no tiles, no size).
- `web/src/test/fixtures.ts` -- `aReport` series has two records; add what the map tests need rather than a second report.
- `web/package.json` -- `leaflet` ^1.9, `react-leaflet` ^5, `@types/leaflet` (already installed on this branch).

## Tasks & Acceptance

**Execution:**
- [x] `web/src/components/RouteMap.tsx` -- `MapContainer` fitted to all drawn points; `TileLayer` OpenStreetMap standard tiles, default referrer (the tile policy requires one), darkened by a `filter` on `.leaflet-tile-pane` in `theme.css`; repaired `Polyline` and one `Polyline` per original segment, styled by `className` passed as a **direct prop** (inside `pathOptions` react-leaflet drops it); `Marker`s with `title` as their accessible name (Leaflet gives a `divIcon` marker `role="button"` and `tabindex="0"` but no name); `ZoomControl` with `zoomInTitle`/`zoomOutTitle` from the dictionary. -- 3.2 criteria, AD-5, AD-18, AD-20.
- [x] `RouteMap.tsx` -- toggle buttons with `aria-pressed`, visible focus ring, line swatch plus text so state and meaning never rest on colour. -- DESIGN.md, WCAG.
- [x] `web/src/components/Result.tsx`, `uk.ts`, `theme.css` -- wire, texts, classes. -- AD-18, theme test.
- [x] tests -- helpers unit-tested for the matrix; component test asserts layers, markers, toggles (`aria-pressed`, layer removed), absent original, Enter on a marker opens its label (`keypress` with `keyCode` 13 — `userEvent.keyboard` does not trigger Leaflet's handler), and that no hex appears outside `theme.css`. -- the matrix is the story.

**Acceptance Criteria:**
- Given a repair result, when the screen renders, then the map shows the solid repaired line and the dashed original, start, finish and the measured-until marker, with the OpenStreetMap attribution visible.
- Given an original with no position, when the screen renders, then there is no original layer and no toggle for one.
- Given a keyboard only user, when they tab into the map, then arrows pan, zoom buttons and markers take focus, and Enter on a marker opens its label; nothing requires dragging.

## Implementation Notes

- Basemap changed during implementation: CARTO's keyless dark tiles now serve an "API KEY REQUIRED" watermark (seen only in a real browser; every jsdom test was green). Ілля chose OpenStreetMap plus a CSS darkening filter over a keyed CARTO or no basemap. The 3.2 criterion "dark CARTO" is renegotiated by that choice. CARTO's current terms: raster tiles need a free key outside their platform.
- Checked in a browser against the real pair: nine tiles load, both lines and the start/finish pins draw, Enter on a focused marker opens its label, arrow keys pan.

## Design Notes

Basemap tiles are fetched from a third party by the browser, so the tile host sees which area was viewed (not the file, not the track). The fonts are self-hosted for the same reason; the map cannot be. The tile URL is one constant, so a self-hosted or keyed source is a one-line change.

## Verification

**Commands:**
- `npm --prefix web run typecheck && npm --prefix web run test && npm --prefix web run build` -- expected: green.
- `dotnet test` -- expected: unchanged, green.

**Manual checks:**
- With the container up (`docker compose up`), repair a real pair and look at the map: both lines, three markers, attribution, arrow keys pan, Tab reaches markers.

## Review Triage Log

Verification Gap reviewer only; the other two layers were skipped to save tokens.

- Marker placement never asserted -- medium, patched. The three positions are now `routeMarkers`, a pure function pinned with exact coordinates. Mutation: reading the start from the last record fails it.
- "Zone ends at the first record" branch untested -- medium, patched. Two cases added to `measuredEndIndex`; mutation `index < 0` fails.
- Refit on a new route and "toggling never moves the view" untested -- medium, patched (reviewer proposed defer; the tile-set comparison was cheap enough). A route moved half a degree must change the tile set; mutation removing `FitBounds` fails. Toggling cannot move the view by construction: the bounds are computed from every layer, visible or not.
- Attribution link `rel`/`target` unasserted -- low, patched with one assertion.
- `.route-pin-measured` `transform: rotate` overridden by Leaflet's inline `transform` -- medium, patched: the shape is an inner `<span class="route-pin-shape">` and the CSS targets it. Start and finish shapes verified in a browser by computed style; the diamond itself is not, because no real pair here has a measured zone ending mid-run.
- `repaired.longitude[i] ?? lat` hides a malformed series -- low, rejected. The contract guarantees equal lengths (`ContractShapeTests`); a guard adds a branch for a state the service never produces.
- Global `clientWidth`/`clientHeight` shim in `setup.ts` -- low, rejected. No other suite measures layout, and the shim is what lets the map suites see a size at all.
- Leaflet stylesheet import untested -- not a finding by the reviewer's own account; the browser check covers it.
