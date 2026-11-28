import { useEffect, useMemo, useState } from 'react';
import type { ReactElement } from 'react';
import {
  CartesianGrid,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
  useActiveTooltipDataPoints,
} from 'recharts';

import type { RepairSeries, TrustZone } from '../api/repair';
import {
  bandSegments,
  chartRows,
  hasHeartRate,
  lastWith,
  measureFor,
  valueDomain,
  type ChartRow,
} from '../chart';
import {
  duration,
  kilometres,
  pace,
  speed,
  type DistanceTexts,
  type PaceTexts,
  type SpeedTexts,
} from '../format';

/** `uk.result.chart` — every string of the chart (AD-18). */
export interface ChartTexts {
  readonly title: string;
  readonly alt: string;
  readonly paceRepaired: string;
  readonly paceOriginal: string;
  readonly speedRepaired: string;
  readonly speedOriginal: string;
  readonly heartRate: string;
  readonly heartRateUnit: string;
  readonly zones: string;
  readonly measured: string;
  readonly estimated: string;
  readonly showTable: string;
  readonly tableCaption: string;
  readonly columnTime: string;
  readonly columnDistance: string;
  readonly columnPace: string;
  readonly columnSpeed: string;
  readonly noValue: string;
}

/** Both axes are this wide, and the band below is padded by the same amount to line up with the plot. */
const axisWidth = 56;
const chartHeight = 280;

/** What the chart's size is until the browser has measured it: jsdom never does. */
const initialSize = { width: 800, height: chartHeight };

/**
 * The text at the end of a line, so a series is named where the eye is and not only in a legend
 * (a hue is never the only difference). Recharts calls this for every point; only the last one with
 * a value draws.
 */
function endLabel(last: number, text: string, className: string) {
  return function EndLabel(props: {
    x?: number | string;
    y?: number | string;
    index?: number;
  }): ReactElement {
    if (props.index !== last || props.x === undefined || props.y === undefined) return <g />;
    return (
      <text x={Number(props.x)} y={Number(props.y)} dy={-8} textAnchor="end" className={className}>
        {text}
      </text>
    );
  };
}

/**
 * Pace (or speed), the original's and the heart rate against time, with the trust zones as a band
 * beneath (Story 3.3). Rows are thinned in the browser to at most 1000 points that keep their
 * record index (AD-23); the band is drawn from the zones' intervals and is never thinned.
 *
 * The chart is a picture, so what it shows is also said in words: the zones as a list of
 * intervals, and the legend names both kinds. Nothing here is told by colour alone.
 */
export function TrustChart({
  series,
  zones,
  sport,
  texts,
  units,
  className,
  onActiveIndex,
}: {
  readonly series: RepairSeries;
  readonly zones: readonly TrustZone[];
  readonly sport: string;
  readonly texts: ChartTexts;
  readonly units: PaceTexts & SpeedTexts & DistanceTexts;
  readonly className?: string;
  /** The record index under the pointer or the keyboard cursor, null when there is none. */
  readonly onActiveIndex?: (index: number | null) => void;
}) {
  const [table, setTable] = useState(false);
  const rows = useMemo(() => chartRows(series, sport), [series, sport]);
  const segments = useMemo(() => bandSegments(zones), [zones]);
  const domain = useMemo(() => valueDomain(rows), [rows]);

  // Nothing to draw: a chart of one point reads as a failure of the repair.
  if (series.secondsFromStart.length < 2 || domain === null) return null;

  const cycling = measureFor(sport) === 'speed';
  const heart = hasHeartRate(rows);
  const names = cycling
    ? { repaired: texts.speedRepaired, original: texts.speedOriginal }
    : { repaired: texts.paceRepaired, original: texts.paceOriginal };

  const left = (value: number) => (cycling ? String(Math.round(value)) : duration(value));
  const full = (value: number) =>
    cycling ? speed(value, units) : (pace(1000, value, units) ?? '');

  return (
    <section className={className} aria-label={texts.title}>
      <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
        <h2 className="text-sm text-text-muted">{texts.title}</h2>
        <button
          type="button"
          aria-pressed={table}
          onClick={() => setTable((value) => !value)}
          className={`rounded-pill border px-4 py-2 text-sm transition-colors ${
            table
              ? 'border-text-muted bg-surface-raised text-text'
              : 'border-border bg-surface text-text-muted'
          }`}
        >
          {texts.showTable}
        </button>
      </div>

      {table ? (
        <RowsTable rows={rows} texts={texts} units={units} cycling={cycling} format={full} />
      ) : (
        <div role="img" aria-label={texts.alt} className="h-70 w-full">
          <ResponsiveContainer width="100%" height={chartHeight} initialDimension={initialSize}>
            <LineChart data={rows} margin={{ top: 16, right: 0, bottom: 0, left: 0 }}>
              {onActiveIndex !== undefined && <ActiveReporter onChange={onActiveIndex} />}
              <CartesianGrid className="chart-grid" vertical={false} />
              <XAxis
                dataKey="seconds"
                type="number"
                domain={['dataMin', 'dataMax']}
                tickFormatter={(value: number) => duration(value)}
                className="chart-axis"
              />
              <YAxis
                yAxisId="main"
                orientation="left"
                width={axisWidth}
                reversed={!cycling}
                domain={domain}
                allowDataOverflow
                tickFormatter={left}
                className="chart-axis"
              />
              {heart && (
                <YAxis
                  yAxisId="heart"
                  orientation="right"
                  width={axisWidth}
                  domain={['auto', 'auto']}
                  className="chart-axis"
                />
              )}
              <Tooltip
                isAnimationActive={false}
                content={
                  <ChartTooltip
                    names={names}
                    heartRate={texts.heartRate}
                    heartRateUnit={texts.heartRateUnit}
                    format={full}
                  />
                }
              />
              <Line
                yAxisId="main"
                dataKey="original"
                dot={false}
                activeDot={false}
                isAnimationActive={false}
                connectNulls={false}
                className="chart-original"
                label={endLabel(
                  lastWith(rows, 'original', domain),
                  names.original,
                  'chart-label-original',
                )}
              />
              <Line
                yAxisId="main"
                dataKey="repaired"
                dot={false}
                isAnimationActive={false}
                connectNulls={false}
                className="chart-repaired"
                label={endLabel(
                  lastWith(rows, 'repaired', domain),
                  names.repaired,
                  'chart-label-repaired',
                )}
              />
              {heart && (
                <Line
                  yAxisId="heart"
                  dataKey="heartRate"
                  dot={false}
                  isAnimationActive={false}
                  connectNulls={false}
                  className="chart-heart"
                  label={endLabel(
                    lastWith(rows, 'heartRate'),
                    texts.heartRate,
                    'chart-label-heart',
                  )}
                />
              )}
            </LineChart>
          </ResponsiveContainer>
        </div>
      )}

      <ZoneBand segments={segments} texts={texts} rightAxis={heart} />
    </section>
  );
}

/**
 * Tells the parent which record the pointer or the keyboard cursor is on. The row's own `index` is
 * the Original's record index, so thinning never shifts it (AD-23). It lives inside the chart
 * because that is where Recharts keeps the active point, for the pointer and the arrow keys alike.
 * Leaving the chart, or swapping it for the table, clears it.
 */
function ActiveReporter({ onChange }: { readonly onChange: (index: number | null) => void }) {
  const points = useActiveTooltipDataPoints<ChartRow>();
  const index = points?.[0]?.index ?? null;
  useEffect(() => {
    onChange(index);
  }, [index, onChange]);
  useEffect(() => () => onChange(null), [onChange]);
  return null;
}

/**
 * The chart's rows as a table (Story 3.4): the same thinned rows the plot draws, so the two can
 * not disagree. A focusable region so the keyboard can scroll it, a caption and column headers so
 * a screen reader announces each cell with its meaning; a missing value is a dash, never empty.
 */
function RowsTable({
  rows,
  texts,
  units,
  cycling,
  format,
}: {
  readonly rows: readonly ChartRow[];
  readonly texts: ChartTexts;
  readonly units: DistanceTexts;
  readonly cycling: boolean;
  readonly format: (value: number) => string;
}) {
  return (
    <div
      role="region"
      aria-label={texts.tableCaption}
      // eslint-disable-next-line jsx-a11y/no-noninteractive-tabindex
      tabIndex={0}
      className="max-h-70 overflow-auto rounded-md border border-border"
    >
      <table className="w-full text-left text-sm">
        <caption className="sr-only">{texts.tableCaption}</caption>
        <thead className="sticky top-0 bg-surface-raised text-text-muted">
          <tr>
            <th scope="col" className="px-3 py-2 font-normal">
              {texts.columnTime}
            </th>
            <th scope="col" className="px-3 py-2 font-normal">
              {texts.columnDistance}
            </th>
            <th scope="col" className="px-3 py-2 font-normal">
              {cycling ? texts.columnSpeed : texts.columnPace}
            </th>
            <th scope="col" className="px-3 py-2 font-normal">
              {texts.heartRate}
            </th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.index} className="border-t border-border text-text">
              <td className="px-3 py-1">{duration(row.seconds)}</td>
              <td className="px-3 py-1">
                {row.distanceMeters === null
                  ? texts.noValue
                  : kilometres(row.distanceMeters, units)}
              </td>
              <td className="px-3 py-1">
                {row.repaired === null ? texts.noValue : format(row.repaired)}
              </td>
              <td className="px-3 py-1">
                {row.heartRate === null ? texts.noValue : `${row.heartRate} ${texts.heartRateUnit}`}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/** What the hover says: the time, then each series with its unit. */
export function ChartTooltip({
  active,
  payload,
  names,
  heartRate,
  heartRateUnit,
  format,
}: {
  readonly active?: boolean;
  readonly payload?: readonly { readonly payload?: ChartRow }[];
  readonly names: { readonly repaired: string; readonly original: string };
  readonly heartRate: string;
  readonly heartRateUnit: string;
  readonly format: (value: number) => string;
}) {
  const row = payload?.[0]?.payload;
  if (active !== true || row === undefined) return null;

  const lines: [string, string][] = [];
  if (row.repaired !== null) lines.push([names.repaired, format(row.repaired)]);
  if (row.original !== null) lines.push([names.original, format(row.original)]);
  if (row.heartRate !== null) lines.push([heartRate, `${row.heartRate} ${heartRateUnit}`]);

  return (
    <div className="chart-tooltip rounded-md border border-border bg-surface-raised p-3 text-sm">
      <p className="text-text">{duration(row.seconds)}</p>
      <dl className="mt-1 text-text-muted">
        {lines.map(([name, value]) => (
          <div key={name} className="flex justify-between gap-4">
            <dt>{name}</dt>
            <dd className="text-text">{value}</dd>
          </div>
        ))}
      </dl>
    </div>
  );
}

/**
 * The trust zones as a strip under the plot: every interval in its colour, widths in proportion
 * to its seconds, padded to line up with the plot area. The same intervals are a list in words
 * for a screen reader, and the legend names both kinds, so colour carries nothing alone.
 */
function ZoneBand({
  segments,
  texts,
  rightAxis,
}: {
  readonly segments: ReturnType<typeof bandSegments>;
  readonly texts: ChartTexts;
  /** Whether the plot has a right axis: without one it reaches the edge and the band must too. */
  readonly rightAxis: boolean;
}) {
  if (segments.length === 0) return null;

  const name = (kind: 'measured' | 'estimated') =>
    kind === 'measured' ? texts.measured : texts.estimated;

  return (
    <div className="mt-2">
      <div
        aria-hidden="true"
        className="flex h-3 overflow-hidden rounded-pill"
        style={{ marginLeft: axisWidth, marginRight: rightAxis ? axisWidth : 0 }}
      >
        {segments.map((segment) => (
          <span
            key={segment.fromSeconds}
            data-zone={segment.kind}
            className={segment.kind === 'measured' ? 'bg-accent' : 'bg-estimated'}
            style={{ width: `${segment.share * 100}%` }}
          />
        ))}
      </div>

      <ul aria-label={texts.zones} className="sr-only">
        {segments.map((segment) => (
          <li key={segment.fromSeconds}>
            {name(segment.kind)}: {duration(segment.fromSeconds)}–{duration(segment.toSeconds)}
          </li>
        ))}
      </ul>

      <ul
        aria-hidden="true"
        className="mt-2 flex flex-wrap gap-x-6 gap-y-1 text-sm text-text-muted"
      >
        <li className="flex items-center gap-2">
          <span className="inline-block h-3 w-3 rounded-pill bg-accent" />
          {texts.measured}
        </li>
        <li className="flex items-center gap-2">
          <span className="inline-block h-3 w-3 rounded-pill bg-estimated" />
          {texts.estimated}
        </li>
      </ul>
    </div>
  );
}
