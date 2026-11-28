import { render, screen, fireEvent, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import { measuredEndIndex, originalSegments, RouteMap, routeMarkers } from './RouteMap';
import type { RepairReport } from '../api/repair';
import { uk } from '../i18n/uk';
import { aReport, aRoute } from '../test/fixtures';

/**
 * Story 3.2: the route map. The pure helpers are tested without Leaflet; the component is tested
 * through the DOM Leaflet writes (paths, markers, controls), which is what a person's tools see.
 */

function show(overrides: Partial<RepairReport> = {}, activeIndex?: number | null) {
  const { series, zones } = { ...aRoute(), ...overrides };
  return render(
    <RouteMap series={series} zones={zones} texts={uk.result.map} activeIndex={activeIndex} />,
  );
}

const repairedLines = (container: HTMLElement) => container.querySelectorAll('path.route-repaired');
const estimatedLines = (container: HTMLElement) =>
  container.querySelectorAll('path.route-estimated');
const cursors = (container: HTMLElement) => container.querySelectorAll('path.route-cursor');
const originalLines = (container: HTMLElement) => container.querySelectorAll('path.route-original');

describe('originalSegments', () => {
  it('draws one segment per run of positions and nothing for a lone point', () => {
    const { series } = aRoute();
    // Records 0-3, a hole, 6-7, a hole at 8, and 9 on its own.
    const segments = originalSegments(series.original.latitude, series.original.longitude);
    expect(segments).toHaveLength(2);
    expect(segments[0]).toHaveLength(4);
    expect(segments[1]).toHaveLength(2);
  });

  it('is empty when no record has a position', () => {
    expect(originalSegments([null, null], [null, null])).toEqual([]);
  });

  it('treats a record missing either coordinate as a hole', () => {
    expect(originalSegments([1, 2, 3, 4], [1, null, 3, 4])).toEqual([
      [
        [3, 3],
        [4, 4],
      ],
    ]);
  });
});

describe('measuredEndIndex', () => {
  const seconds = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];

  it('is the last record inside the last measured zone', () => {
    expect(
      measuredEndIndex(
        [
          { kind: 'measured', fromSeconds: 0, toSeconds: 5 },
          { kind: 'estimated', fromSeconds: 5, toSeconds: 9 },
        ],
        seconds,
      ),
    ).toBe(5);
  });

  it('takes the later of two measured zones, wherever they sit in the list', () => {
    expect(
      measuredEndIndex(
        [
          { kind: 'measured', fromSeconds: 6, toSeconds: 8 },
          { kind: 'estimated', fromSeconds: 3, toSeconds: 6 },
          { kind: 'measured', fromSeconds: 0, toSeconds: 3 },
        ],
        seconds,
      ),
    ).toBe(8);
  });

  it('is null when the measured zone reaches the last record', () => {
    expect(
      measuredEndIndex([{ kind: 'measured', fromSeconds: 0, toSeconds: 9 }], seconds),
    ).toBeNull();
    expect(
      measuredEndIndex([{ kind: 'measured', fromSeconds: 0, toSeconds: 20 }], seconds),
    ).toBeNull();
  });

  it('is null when the measured zone ends at or before the first record', () => {
    // A marker there would sit on top of the start pin.
    expect(
      measuredEndIndex([{ kind: 'measured', fromSeconds: 0, toSeconds: 0 }], seconds),
    ).toBeNull();
    expect(
      measuredEndIndex([{ kind: 'measured', fromSeconds: -3, toSeconds: -1 }], seconds),
    ).toBeNull();
  });

  it('is null when every zone is estimated', () => {
    expect(
      measuredEndIndex([{ kind: 'estimated', fromSeconds: 0, toSeconds: 9 }], seconds),
    ).toBeNull();
  });
});

describe('routeMarkers', () => {
  const at = (i: number): [number, number] => [50.33 + i * 0.001, 30.38 + i * 0.001];
  const route = Array.from({ length: 10 }, (_, i) => at(i));

  it('puts the start on the first record, the finish on the last, and the third where the zone ends', () => {
    expect(routeMarkers(route, 5)).toEqual({ start: at(0), measured: at(5), finish: at(9) });
  });

  it('has no measured marker without an index', () => {
    expect(routeMarkers(route, null).measured).toBeUndefined();
  });

  it('has no finish for a single record, which is not a place of its own', () => {
    expect(routeMarkers([at(0)], null)).toEqual({
      start: at(0),
      measured: undefined,
      finish: undefined,
    });
  });
});

describe('the map', () => {
  it('draws both lines, three markers and the basemap credit', () => {
    const { container } = show();

    expect(repairedLines(container)).toHaveLength(1);
    expect(originalLines(container)).toHaveLength(2);
    for (const name of [uk.result.map.start, uk.result.map.measuredUntil, uk.result.map.finish]) {
      expect(screen.getByRole('button', { name })).toBeInTheDocument();
    }
    expect(container.querySelector('.leaflet-control-attribution')).toHaveTextContent(
      'OpenStreetMap',
    );
  });

  it('opens the credit link apart, on the copyright page', () => {
    const { container } = show();
    const link = container.querySelector<HTMLAnchorElement>('.leaflet-control-attribution a');
    expect(link?.href).toBe('https://www.openstreetmap.org/copyright');
    expect(link?.target).toBe('_blank');
    expect(link?.rel).toContain('noopener');
  });

  it("draws a pin per marker from the shape element, not from Leaflet's positioned one", () => {
    const { container } = show();
    for (const kind of ['start', 'measured', 'finish']) {
      expect(container.querySelector(`.route-pin-${kind} > .route-pin-shape`)).not.toBeNull();
    }
  });

  it('refits the view when a different route arrives', () => {
    const first = aRoute();
    const { container, rerender } = render(
      <RouteMap series={first.series} zones={first.zones} texts={uk.result.map} />,
    );
    const tilesOf = () =>
      [...container.querySelectorAll<HTMLImageElement>('img.leaflet-tile')]
        .map((t) => t.src)
        .sort();
    const before = tilesOf();
    expect(before.length).toBeGreaterThan(0);

    // The same route half a degree north: the map is a different place now.
    const moved = {
      ...first.series,
      repaired: {
        ...first.series.repaired,
        latitude: first.series.repaired.latitude.map((lat) => lat + 0.5),
      },
      original: {
        ...first.series.original,
        latitude: first.series.original.latitude.map(() => null),
      },
    };
    rerender(<RouteMap series={moved} zones={first.zones} texts={uk.result.map} />);

    expect(tilesOf()).not.toEqual(before);
  });

  it('requests tiles from the one basemap host, and keeps the referrer its policy asks for', () => {
    const { container } = show();
    const tiles = [...container.querySelectorAll<HTMLImageElement>('img.leaflet-tile')];
    expect(tiles.length).toBeGreaterThan(0);
    for (const tile of tiles) {
      expect(new URL(tile.src).hostname).toBe('tile.openstreetmap.org');
      // Not `no-referrer`: the tile policy requires a real Referer, and a client without one is
      // what gets blocked.
      expect(tile.referrerPolicy).not.toBe('no-referrer');
    }
  });

  it('styles both lines by class, so the colour and the dash come from theme.css', () => {
    const { container } = show();
    expect(repairedLines(container)[0]).toHaveClass('route-repaired');
    expect(originalLines(container)[0]).toHaveClass('route-original');
  });

  it('has both toggles pressed, and a toggle removes its layer', async () => {
    const user = userEvent.setup();
    const { container } = show();

    const repaired = screen.getByRole('button', { name: uk.result.map.repaired });
    const original = screen.getByRole('button', { name: uk.result.map.original });
    expect(repaired).toHaveAttribute('aria-pressed', 'true');
    expect(original).toHaveAttribute('aria-pressed', 'true');

    await user.click(repaired);
    expect(repaired).toHaveAttribute('aria-pressed', 'false');
    expect(repairedLines(container)).toHaveLength(0);
    // The calculated stretch is part of the repaired layer and goes with it.
    expect(estimatedLines(container)).toHaveLength(0);
    expect(originalLines(container)).toHaveLength(2);

    await user.click(original);
    expect(original).toHaveAttribute('aria-pressed', 'false');
    expect(originalLines(container)).toHaveLength(0);

    await user.click(repaired);
    expect(repairedLines(container)).toHaveLength(1);
  });

  it('has no original layer and no toggle when the original carries no position', () => {
    const { series } = aRoute();
    const nothing = series.original.latitude.map(() => null);
    const { container } = show({
      series: {
        ...series,
        original: { ...series.original, latitude: nothing, longitude: nothing },
      },
    });

    expect(originalLines(container)).toHaveLength(0);
    expect(screen.queryByRole('button', { name: uk.result.map.original })).toBeNull();
    expect(repairedLines(container)).toHaveLength(1);
  });

  it('shows start and finish only when the position was measured to the end', () => {
    show({ zones: [{ kind: 'measured', fromSeconds: 0, toSeconds: 9 }] });
    expect(screen.getByRole('button', { name: uk.result.map.start })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: uk.result.map.finish })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: uk.result.map.measuredUntil })).toBeNull();
  });

  it('shows start and finish only when every zone is estimated', () => {
    show({ zones: [{ kind: 'estimated', fromSeconds: 0, toSeconds: 9 }] });
    expect(screen.queryByRole('button', { name: uk.result.map.measuredUntil })).toBeNull();
    expect(screen.getByRole('button', { name: uk.result.map.finish })).toBeInTheDocument();
  });

  it('is operable from the keyboard: named controls, tab stops, Enter opens a label', () => {
    const { container } = show();

    const region = screen.getByRole('region', { name: uk.result.map.label });
    expect(region).toHaveAttribute('tabindex', '0');
    expect(screen.getByRole('button', { name: uk.result.map.zoomIn })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: uk.result.map.zoomOut })).toBeInTheDocument();

    const marker = screen.getByRole('button', { name: uk.result.map.measuredUntil });
    expect(marker).toHaveAttribute('tabindex', '0');
    expect(container.querySelector('.leaflet-popup')).toBeNull();

    // Leaflet listens for `keypress` and reads `keyCode`, which `userEvent.keyboard` does not send.
    fireEvent.keyPress(marker, { key: 'Enter', code: 'Enter', keyCode: 13, charCode: 13 });

    const popup = container.querySelector<HTMLElement>('.leaflet-popup');
    expect(popup).not.toBeNull();
    expect(within(popup!).getByText(uk.result.map.measuredUntil)).toBeInTheDocument();
    // Leaflet's English close button would be a string outside the dictionary.
    expect(popup!.querySelector('.leaflet-popup-close-button')).toBeNull();
  });

  it('writes no English of Leaflet’s own into the page', () => {
    const { container } = show();
    expect(container).not.toHaveTextContent('Leaflet');
    expect(container.querySelector('[aria-label="Close popup"]')).toBeNull();
    expect(
      container.querySelector('[title="A JavaScript library for interactive maps"]'),
    ).toBeNull();
  });

  it('draws nothing when the repaired track has no records', () => {
    const { series } = aReport({
      series: {
        secondsFromStart: [],
        original: { latitude: [], longitude: [], distanceMeters: [], speedMetersPerSecond: [] },
        repaired: {
          latitude: [],
          longitude: [],
          altitudeMeters: [],
          distanceMeters: [],
          speedMetersPerSecond: [],
          heartRate: [],
          power: [],
          cadence: [],
          temperatureCelsius: [],
        },
      },
    });
    const { container } = render(<RouteMap series={series} zones={[]} texts={uk.result.map} />);
    expect(container).toBeEmptyDOMElement();
  });
});

describe('the zones on the map (Story 3.4)', () => {
  it('draws the calculated stretch in its own class and says so in words', () => {
    const { container } = show();

    expect(repairedLines(container)).toHaveLength(1);
    expect(estimatedLines(container)).toHaveLength(1);
    expect(screen.getByText(uk.result.map.estimatedStretch)).toBeInTheDocument();
  });

  it('draws one accent line and no legend when everything was measured', () => {
    const { container } = show({ zones: [{ kind: 'measured', fromSeconds: 0, toSeconds: 9 }] });

    expect(repairedLines(container)).toHaveLength(1);
    expect(estimatedLines(container)).toHaveLength(0);
    expect(screen.queryByText(uk.result.map.estimatedStretch)).toBeNull();
  });

  it('follows every alternation of the zones, with the line unbroken at the joins', () => {
    const { container } = show({
      zones: [
        { kind: 'measured', fromSeconds: 0, toSeconds: 2 },
        { kind: 'estimated', fromSeconds: 2, toSeconds: 4 },
        { kind: 'measured', fromSeconds: 4, toSeconds: 6 },
        { kind: 'estimated', fromSeconds: 6, toSeconds: 9 },
      ],
    });

    expect(repairedLines(container)).toHaveLength(2);
    expect(estimatedLines(container)).toHaveLength(2);
  });
});

describe('the cursor from the chart (Story 3.4)', () => {
  it('draws no marker without an active record', () => {
    expect(cursors(show().container)).toHaveLength(0);
    expect(cursors(show({}, null).container)).toHaveLength(0);
  });

  it('draws a marker at the record the chart is on, and it moves with the index', () => {
    const { container, rerender } = show({}, 2);
    const first = cursors(container)[0]?.getAttribute('d');
    expect(first).toBeTruthy();

    const { series, zones } = aRoute();
    rerender(<RouteMap series={series} zones={zones} texts={uk.result.map} activeIndex={7} />);
    const second = cursors(container)[0]?.getAttribute('d');
    expect(cursors(container)).toHaveLength(1);
    expect(second).toBeTruthy();
    expect(second).not.toBe(first);
  });

  it('is not a tab stop and takes no click, so the map keeps its keys', () => {
    const { container } = show({}, 3);
    expect(cursors(container)[0]).not.toHaveClass('leaflet-interactive');
  });

  it('draws nothing for an index the route does not have', () => {
    expect(cursors(show({}, 99).container)).toHaveLength(0);
  });
});
