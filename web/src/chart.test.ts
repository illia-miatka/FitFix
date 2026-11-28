import { describe, expect, it } from 'vitest';

import type { RepairSeries, TrustZone } from './api/repair';
import {
  bandSegments,
  chartRows,
  hasHeartRate,
  lastWith,
  maxChartPoints,
  movingAverage,
  measureFor,
  thinIndices,
  valueDomain,
  type ChartRow,
} from './chart';

/** A series of `length` records at one second apart, every value overridable. */
function seriesOf(
  length: number,
  overrides: {
    repaired?: (i: number) => number;
    original?: (i: number) => number | null;
    heartRate?: (i: number) => number | null;
  } = {},
): RepairSeries {
  const each = <T>(value: (i: number) => T) => Array.from({ length }, (_, i) => value(i));
  return {
    secondsFromStart: each((i) => i),
    original: {
      latitude: each(() => null),
      longitude: each(() => null),
      distanceMeters: each(() => null),
      speedMetersPerSecond: each(overrides.original ?? (() => 3)),
    },
    repaired: {
      latitude: each(() => 50),
      longitude: each(() => 30),
      altitudeMeters: each(() => null),
      distanceMeters: each((i) => i * 3),
      speedMetersPerSecond: each(overrides.repaired ?? (() => 4)),
      heartRate: each(overrides.heartRate ?? (() => 150)),
      power: each(() => null),
      cadence: each(() => null),
      temperatureCelsius: each(() => null),
    },
  };
}

describe('thinIndices', () => {
  it('keeps every record when they fit', () => {
    expect(thinIndices(5, 10)).toEqual([0, 1, 2, 3, 4]);
  });

  it('thins 7200 records to at most 1000, keeping the first and the last', () => {
    const kept = thinIndices(7200);

    expect(kept.length).toBeLessThanOrEqual(maxChartPoints);
    expect(kept.length).toBeGreaterThan(900);
    expect(kept[0]).toBe(0);
    expect(kept[kept.length - 1]).toBe(7199);
  });

  it('is strictly ascending, so a line never doubles back', () => {
    const kept = thinIndices(7200);
    for (let i = 1; i < kept.length; i++) expect(kept[i]).toBeGreaterThan(kept[i - 1] as number);
  });

  it('is empty for nothing and a single index for one record', () => {
    expect(thinIndices(0)).toEqual([]);
    expect(thinIndices(1)).toEqual([0]);
  });
});

describe('chartRows', () => {
  it('gives each drawn row the index of the record it stands for', () => {
    const rows = chartRows(seriesOf(7200), 'running');

    expect(rows.length).toBeLessThanOrEqual(maxChartPoints);
    expect(rows.map((row) => row.index)).toEqual(thinIndices(7200));
    // The seconds are the record's own, not the row's position.
    expect(rows.every((row) => row.seconds === row.index)).toBe(true);
  });

  it('is a pace in seconds per kilometre for a runner', () => {
    const [row] = chartRows(seriesOf(3, { repaired: () => 4, original: () => 2 }), 'running');
    expect(row?.repaired).toBeCloseTo(250);
    expect(row?.original).toBeCloseTo(500);
  });

  it('is a speed in kilometres per hour for a cyclist', () => {
    const [row] = chartRows(seriesOf(3, { repaired: () => 10, original: () => 5 }), 'cycling');
    expect(row?.repaired).toBeCloseTo(36);
    expect(row?.original).toBeCloseTo(18);
  });

  it('has no pace for a standing record, and never an infinite one', () => {
    const rows = chartRows(seriesOf(3, { repaired: () => 0, original: () => 0.3 }), 'running');
    expect(rows.every((row) => row.repaired === null && row.original === null)).toBe(true);
  });

  it('smooths before it thins: a lone spike is damped and never drawn as a spike', () => {
    const rows = chartRows(seriesOf(7, { repaired: (i) => (i === 3 ? 40 : 4) }), 'cycling');
    // 40 m/s is 144 km/h; averaged into six 4 m/s neighbours within ±15 s it is far below that.
    expect(Math.max(...rows.map((row) => row.repaired ?? 0))).toBeLessThan(40);
  });

  it('smooths over every record, not only over the ones that are kept', () => {
    // A spike on a record the stride skips still counts in its neighbours' averages.
    const length = 3000;
    const rows = chartRows(
      seriesOf(length, { repaired: (i) => (i === 1501 ? 100 : 4) }),
      'cycling',
    );
    const kept = rows.find((row) => row.index === 1500) ?? rows.find((row) => row.index === 1502);
    if (kept !== undefined) expect(kept.repaired ?? 0).toBeGreaterThan(14.4);
  });

  it('keeps a hole in the original as a hole', () => {
    const rows = chartRows(seriesOf(4, { original: (i) => (i === 2 ? null : 3) }), 'running');
    expect(rows.map((row) => row.original === null)).toEqual([false, false, true, false]);
  });

  it('treats a sport it does not know as a pace, like the numbers above it', () => {
    expect(measureFor('other')).toBe('pace');
    expect(measureFor('running')).toBe('pace');
    expect(measureFor('cycling')).toBe('speed');
  });
});

describe('heart rate and the end of a line', () => {
  it('knows a run without a strap has no heart rate', () => {
    expect(hasHeartRate(chartRows(seriesOf(4, { heartRate: () => null }), 'running'))).toBe(false);
    expect(
      hasHeartRate(chartRows(seriesOf(4, { heartRate: (i) => (i === 3 ? 140 : null) }), 'running')),
    ).toBe(true);
  });

  it('skips a value the axis would cut off, so its label is not placed out of sight', () => {
    const rows: ChartRow[] = [10, 20, 9999].map((value, i) => ({
      index: i,
      seconds: i,
      repaired: value,
      original: null,
      heartRate: null,
    }));
    expect(lastWith(rows, 'repaired')).toBe(2);
    expect(lastWith(rows, 'repaired', [0, 100])).toBe(1);
  });

  it('finds the last row that has a value', () => {
    const rows = chartRows(seriesOf(5, { original: (i) => (i < 3 ? 3 : null) }), 'running');
    expect(lastWith(rows, 'original')).toBe(2);
    expect(lastWith(rows, 'repaired')).toBe(4);
    expect(
      lastWith(chartRows(seriesOf(3, { heartRate: () => null }), 'running'), 'heartRate'),
    ).toBe(-1);
  });
});

describe('valueDomain', () => {
  it('is null when nothing has a value', () => {
    expect(
      valueDomain(chartRows(seriesOf(3, { repaired: () => 0, original: () => 0 }), 'running')),
    ).toBeNull();
  });

  it('leaves one absurd value outside the range instead of stretching it', () => {
    // 99 rows at 250 s/km and one stray second at 3300 s/km, built directly: the chart's own
    // smoothing would already have flattened a lone spike before it got here.
    const rows: ChartRow[] = Array.from({ length: 100 }, (_, i) => ({
      index: i,
      seconds: i,
      repaired: i === 50 ? 3300 : 250,
      original: null,
      heartRate: null,
    }));
    const domain = valueDomain(rows);

    expect(domain?.[0]).toBe(250);
    expect(domain?.[1]).toBeLessThan(300);
  });

  it('always has some height, even for a perfectly even run', () => {
    const domain = valueDomain(chartRows(seriesOf(10), 'running'));
    expect((domain?.[1] ?? 0) - (domain?.[0] ?? 0)).toBeGreaterThanOrEqual(1);
  });
});

describe('bandSegments', () => {
  const zone = (kind: TrustZone['kind'], fromSeconds: number, toSeconds: number): TrustZone => ({
    kind,
    fromSeconds,
    toSeconds,
  });

  it('shows every interval of a run that alternates, in time order', () => {
    const segments = bandSegments([
      zone('estimated', 100, 150),
      zone('measured', 0, 100),
      zone('measured', 150, 200),
      zone('estimated', 200, 400),
    ]);

    expect(segments.map((s) => s.kind)).toEqual(['measured', 'estimated', 'measured', 'estimated']);
    expect(segments.map((s) => s.fromSeconds)).toEqual([0, 100, 150, 200]);
  });

  it('gives each interval a share of the span in proportion to its seconds, adding up to one', () => {
    const segments = bandSegments([zone('measured', 0, 300), zone('estimated', 300, 400)]);

    expect(segments.map((s) => s.share)).toEqual([0.75, 0.25]);
    expect(segments.reduce((sum, s) => sum + s.share, 0)).toBeCloseTo(1);
  });

  it('drops an interval with no length and is empty for no zones', () => {
    expect(
      bandSegments([zone('measured', 5, 5), zone('estimated', 5, 10)]).map((s) => s.kind),
    ).toEqual(['estimated']);
    expect(bandSegments([])).toEqual([]);
  });
});

describe('movingAverage', () => {
  const seconds = (length: number) => Array.from({ length }, (_, i) => i);

  it('leaves a constant series as it was', () => {
    expect(movingAverage([4, 4, 4, 4], seconds(4), 2)).toEqual([4, 4, 4, 4]);
  });

  it('averages the records within the window on both sides of each one', () => {
    // Window of one second either side: the middle record sees all three.
    expect(movingAverage([0, 3, 0], seconds(3), 1)).toEqual([1.5, 1, 1.5]);
  });

  it('smooths a lone spike across its neighbours rather than keeping it', () => {
    const smoothed = movingAverage([4, 4, 4, 40, 4, 4, 4], seconds(7), 2);
    expect(Math.max(...(smoothed as number[]))).toBeLessThan(40 / 2);
  });

  it('keeps a hole a hole and does not count it in a neighbour', () => {
    expect(movingAverage([2, null, 4], seconds(3), 2)).toEqual([3, null, 3]);
  });

  it('measures the window in seconds, not in records', () => {
    // Records two seconds apart: one second either side reaches only the record itself.
    expect(movingAverage([0, 10, 0], [0, 2, 4], 1)).toEqual([0, 10, 0]);
  });

  it('is empty for nothing', () => {
    expect(movingAverage([], [], 5)).toEqual([]);
  });
});
