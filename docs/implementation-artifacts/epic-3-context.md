# Epic 3 Context: A Preview You Can Trust

<!-- Compiled 2026-11-27 from the epics file, the architecture spine and the UX documents. Edit freely. Regenerate with compile-epic-context if planning docs change. -->

## Goal

After a repair the athlete sees, before downloading anything, the before → after numbers, a map with the broken and the repaired tracks, a pace/heart-rate chart with the trust zones, and every warning in plain language. The point is to let someone decide knowingly — about an over-long track in particular — without opening the file.

## Stories

- Story 3.1: See what changed in two seconds
- Story 3.2: See the real route on the map
- Story 3.3: See where the GPS lied
- Story 3.4: Find on the map what I see on the chart
- Story 3.5: Understand the warning and decide about a long track
- Story 3.6: Look at the result from a phone

## Requirements & Constraints

- The result carries series as long as the original's record count; index `i` is always the original's `i`-th record, absent values are `null`, and the server never thins them. Everything the map and chart draw is read from those series.
- The trust zones are explicit intervals (measured or estimated) from one source. The chart band, the map marker and the warning text all read that list; never assume the measured zone is one prefix of the activity.
- Thinning happens only in the browser and only to render. A thinned point keeps its original record index, and hover or focus always works in that index space, so a chart point and a map point can never disagree.
- The contract is SI units only; paces, times and distances are formatted in the UI (`web/src/format.ts`).
- No state is stored: everything on screen comes from the one repair answer.

## Technical Decisions

- Map: react-leaflet 5 on Leaflet 1.9, OpenStreetMap basemap darkened in CSS, with its attribution (CARTO now needs a key). Chart: Recharts 3. Icons: lucide-react. None is installed yet.
- Colour lives only in `web/src/theme.css`; a test fails on any hex elsewhere. Every string lives in `web/src/i18n/uk.ts`.
- Reduced-motion is handled globally in `theme.css`, not per component.

## UX & Interaction Patterns

- The repaired line is solid in the accent colour, the original is always dashed in its own muted colour, and every series has a direct label: lines never differ by hue alone. The estimated colour marks only the calculated zone (chart band, map segment, badge).
- Desktop result is a two-column grid, map 7/12 beside the metrics 5/12, chart full width; below 768 px a single column with a 320 px map.
- The map is fully operable without a mouse: arrows pan, zoom buttons are reachable by keyboard, markers take focus and Enter opens their label. Nothing may require dragging (WCAG 2.1.1, 2.5.7). Toggle buttons use `aria-pressed` with a visible focus ring and a state readable without hover.
- Warnings sit as a callout above the map.

## Cross-Story Dependencies

- Story 3.2 builds the map; 3.3 adds per-zone colouring on it; 3.4 syncs it with the chart; Epic 5's Story 5.4 (drawing a missing stretch) needs the map from 3.2.
- Story 3.1's metric cards overlap the existing result screen (Story 2.6), which already shows distance, time and pace.
