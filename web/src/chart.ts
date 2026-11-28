/**
 * The pure half of the trust chart (Story 3.3): which records are drawn, what each is worth in
 * pace or speed, and where the zone band's intervals fall.
 *
 * Thinning happens here and only here (AD-23): the server sends every record and never thins
 * (AD-5), and a thinned point keeps its original record index, so a chart point and a map point
 * can never disagree about which record they are (Story 3.4 relies on it).
 */

import type { RepairSeries, TrustZone } from './api/repair';

/** The most points the chart draws. Recharts writes SVG, and 7200 nodes make hover stutter. */
export const maxChartPoints = 1000;

/**
 * Below this speed a record has no pace: dividing by something close to zero prints an absurd
 * pace and stretches the axis for one stationary second. A gap says "not moving" honestly.
 */
export const standingSpeedMetersPerSecond = 0.3;

/**
 * Half the width of the moving average applied to speed before it is drawn. A watch reports a speed
 * every second and a footfall makes it jump by minutes per kilometre, which on a chart is a
 * scribble nobody can read; fifteen seconds either side is the window the repair itself smooths
 * with, so the line the athlete reads is the one the file carries (Story 3.3, agreed on real data).
 */
export const smoothingHalfWidthSeconds = 15;

/**
 * A centred moving average over `±halfWidth` seconds. A missing value stays missing — a hole in
 * the original is not filled in by its neighbours — and missing values never count towards a
 * neighbour's average. Linear: prefix sums and two pointers, so a two-hour run costs one pass.
 */
export function movingAverage(
  values: readonly (number | null | undefined)[],
  seconds: readonly number[],
  halfWidth: number = smoothingHalfWidthSeconds,
): (number | null)[] {
  const sums = [0];
  const counts = [0];
  for (const value of values) {
    const present = value !== null && value !== undefined && Number.isFinite(value);
    sums.push((sums[sums.length - 1] as number) + (present ? value : 0));
    counts.push((counts[counts.length - 1] as number) + (present ? 1 : 0));
  }

  const result: (number | null)[] = [];
  let from = 0;
  let to = 0;
  for (let i = 0; i < values.length; i++) {
    const centre = values[i];
    const t = seconds[i] ?? 0;
    while (from < i && (seconds[from] ?? 0) < t - halfWidth) from++;
    while (to < values.length && (seconds[to] ?? 0) <= t + halfWidth) to++;

    const count = (counts[to] as number) - (counts[from] as number);
    const present = centre !== null && centre !== undefined && Number.isFinite(centre);
    result.push(
      present && count > 0 ? ((sums[to] as number) - (sums[from] as number)) / count : null,
    );
  }
  return result;
}

/** What the left axis measures: pace for a runner, speed for a cyclist (Story 3.3). */
export type Measure = 'pace' | 'speed';

/** Only cycling switches to speed; the screen's own numbers show a pace for everything else. */
export function measureFor(sport: string): Measure {
  return sport === 'cycling' ? 'speed' : 'pace';
}

/**
 * The record indices to draw, ascending. All of them when they fit; otherwise an even stride that
 * always includes the first and the last, so the line starts and ends where the run did.
 */
export function thinIndices(length: number, max: number = maxChartPoints): number[] {
  if (length <= 0) return [];
  if (length <= max || max < 2) return Array.from({ length }, (_, i) => i);

  const kept: number[] = [];
  for (let i = 0; i < max; i++) {
    const index = Math.round((i * (length - 1)) / (max - 1));
    if (kept[kept.length - 1] !== index) kept.push(index);
  }
  return kept;
}

/** One drawn point: the record it stands for, and what each series is worth there. */
export interface ChartRow {
  /** The index of the Original's record this point is (AD-5). */
  readonly index: number;
  readonly seconds: number;
  /** The repaired distance covered by this record, in metres (the table's column). */
  readonly distanceMeters: number | null;
  /** Seconds per kilometre for a pace chart, km/h for a speed one; null where there is none. */
  readonly repaired: number | null;
  readonly original: number | null;
  readonly heartRate: number | null;
}

function valueOf(speed: number | null | undefined, measure: Measure): number | null {
  if (speed === null || speed === undefined || !Number.isFinite(speed)) return null;
  if (measure === 'speed') return speed * 3.6;
  return speed > standingSpeedMetersPerSecond ? 1000 / speed : null;
}

/** The thinned rows of the chart, read from the series by record index. */
export function chartRows(series: RepairSeries, sport: string): ChartRow[] {
  const measure = measureFor(sport);
  // Smoothed over every record first and thinned afterwards, so the average sees the whole second-
  // by-second series and not only the records that happen to be drawn.
  const repaired = movingAverage(series.repaired.speedMetersPerSecond, series.secondsFromStart);
  const original = movingAverage(series.original.speedMetersPerSecond, series.secondsFromStart);
  return thinIndices(series.secondsFromStart.length).map((index) => ({
    index,
    seconds: series.secondsFromStart[index] ?? 0,
    distanceMeters: series.repaired.distanceMeters[index] ?? null,
    repaired: valueOf(repaired[index], measure),
    original: valueOf(original[index], measure),
    heartRate: series.repaired.heartRate[index] ?? null,
  }));
}

/** Whether any row carries a heart rate: a run without a strap has no line and no axis for it. */
export function hasHeartRate(rows: readonly ChartRow[]): boolean {
  return rows.some((row) => row.heartRate !== null);
}

/**
 * The position in `rows` of the last row where `key` has a value, or -1. With `within`, only a
 * value inside that range counts: a label placed at a point the axis cuts off would never be seen.
 */
export function lastWith(
  rows: readonly ChartRow[],
  key: 'repaired' | 'original' | 'heartRate',
  within?: readonly [number, number],
): number {
  for (let i = rows.length - 1; i >= 0; i--) {
    const value = rows[i]?.[key];
    if (value === null || value === undefined) continue;
    if (within === undefined || (value >= within[0] && value <= within[1])) return i;
  }
  return -1;
}

/**
 * The left axis range between a low and a high quantile, so one stray second does not flatten the
 * whole run. Points beyond it leave the plot instead of stretching it. Null when nothing has a value.
 */
export function valueDomain(rows: readonly ChartRow[], quantile = 0.98): [number, number] | null {
  const values = rows
    .flatMap((row) => [row.repaired, row.original])
    .filter((value): value is number => value !== null)
    .sort((a, b) => a - b);
  if (values.length === 0) return null;

  const at = (share: number) =>
    values[Math.min(values.length - 1, Math.floor(values.length * share))] as number;
  const low = at(1 - quantile);
  const high = at(quantile);
  return [Math.floor(low), Math.ceil(Math.max(high, low + 1))];
}

/** One interval of the band, with the share of the whole span it covers. */
export interface BandSegment {
  readonly kind: TrustZone['kind'];
  readonly fromSeconds: number;
  readonly toSeconds: number;
  /** Between 0 and 1; they add up to 1 when the zones tile the span, as the service's do. */
  readonly share: number;
}

/**
 * Every interval of the zones in time order, however many times they alternate (AD-20), with its
 * share of the span. An interval that has no length is dropped: it would be an invisible sliver
 * that still shows up in the legend of intervals.
 */
export function bandSegments(zones: readonly TrustZone[]): BandSegment[] {
  const ordered = zones
    .filter((zone) => zone.toSeconds > zone.fromSeconds)
    .toSorted((a, b) => a.fromSeconds - b.fromSeconds);
  if (ordered.length === 0) return [];

  const start = ordered[0]?.fromSeconds ?? 0;
  const end = Math.max(...ordered.map((zone) => zone.toSeconds));
  const span = end - start;
  return ordered.map((zone) => ({
    kind: zone.kind,
    fromSeconds: zone.fromSeconds,
    toSeconds: zone.toSeconds,
    share: (zone.toSeconds - zone.fromSeconds) / span,
  }));
}
