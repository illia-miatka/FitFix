import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import { ChartTooltip, TrustChart } from './TrustChart';
import type { RepairSeries, TrustZone } from '../api/repair';
import { uk } from '../i18n/uk';
import { thinIndices, type ChartRow } from '../chart';
import { aRoute } from '../test/fixtures';

/**
 * Story 3.3: the chart under the map. The arithmetic (thinning, pace, zones) is `chart.test.ts`;
 * this drives the component through the DOM Recharts writes, where lines are paths with a class,
 * labels are text, and the zone band is plain elements.
 */

const texts = uk.result.chart;

function show(
  options: { series?: RepairSeries; zones?: readonly TrustZone[]; sport?: string } = {},
) {
  const route = aRoute();
  return render(
    <TrustChart
      series={options.series ?? route.series}
      zones={options.zones ?? route.zones}
      sport={options.sport ?? 'running'}
      texts={texts}
      units={uk.units}
    />,
  );
}

/** The fixture's series with the original's speed and the heart rate replaced. */
function withSeries(change: {
  heartRate?: (number | null)[];
  repairedSpeed?: number[];
}): RepairSeries {
  const { series } = aRoute();
  return {
    ...series,
    repaired: {
      ...series.repaired,
      heartRate: change.heartRate ?? series.repaired.heartRate,
      speedMetersPerSecond: change.repairedSpeed ?? series.repaired.speedMetersPerSecond,
    },
  };
}

describe('the lines', () => {
  it('draws the repaired line solid and the original dashed, each in its own class', () => {
    const { container } = show();

    expect(container.querySelector('g.chart-repaired path.recharts-line-curve')).not.toBeNull();
    expect(container.querySelector('g.chart-original path.recharts-line-curve')).not.toBeNull();
  });

  it('names every series at the end of its line, in words', () => {
    const { container } = show();
    const labels = [...container.querySelectorAll('text')].map((node) => node.textContent);

    expect(labels).toContain(texts.paceRepaired);
    expect(labels).toContain(texts.paceOriginal);
    expect(labels).toContain(texts.heartRate);
  });

  it('draws a heart-rate line and a second axis when there is a heart rate', () => {
    const { container } = show();
    expect(container.querySelector('g.chart-heart path.recharts-line-curve')).not.toBeNull();
  });

  it('draws neither the heart-rate line nor its label when no record has one', () => {
    const { container } = show({ series: withSeries({ heartRate: Array(10).fill(null) }) });

    expect(container.querySelector('g.chart-heart path.recharts-line-curve')).toBeNull();
    expect(
      [...container.querySelectorAll('text')].some((node) => node.textContent === texts.heartRate),
    ).toBe(false);
  });

  it('says speed instead of pace for cycling', () => {
    const { container } = show({ sport: 'cycling' });
    const labels = [...container.querySelectorAll('text')].map((node) => node.textContent);

    expect(labels).toContain(texts.speedRepaired);
    expect(labels).toContain(texts.speedOriginal);
    expect(labels).not.toContain(texts.paceRepaired);
  });
});

/** A two-hour run, one record a second, for the thinning tests. */
function aLongRun(): RepairSeries {
  const length = 7200;
  const each = <T,>(value: (i: number) => T) => Array.from({ length }, (_, i) => value(i));
  return {
    secondsFromStart: each((i) => i),
    original: {
      latitude: each(() => null),
      longitude: each(() => null),
      distanceMeters: each(() => null),
      speedMetersPerSecond: each((i) => 3 + Math.sin(i / 50)),
    },
    repaired: {
      latitude: each(() => 50),
      longitude: each(() => 30),
      altitudeMeters: each(() => null),
      distanceMeters: each((i) => i * 3),
      speedMetersPerSecond: each((i) => 4 + Math.sin(i / 70)),
      heartRate: each(() => 150),
      power: each(() => null),
      cadence: each(() => null),
      temperatureCelsius: each(() => null),
    },
  };
}

describe('a long run', () => {
  it('draws no more than a thousand points of a two-hour run', () => {
    const long = aLongRun();

    const { container } = show({
      series: long,
      zones: [{ kind: 'measured', fromSeconds: 0, toSeconds: 7199 }],
    });

    const curve = container.querySelector('g.chart-repaired path.recharts-line-curve');
    const points = (curve?.getAttribute('d') ?? '').match(/[ML]/g)?.length ?? 0;
    expect(points).toBeGreaterThan(900);
    expect(points).toBeLessThanOrEqual(1000);
  });
});

describe('the zone band', () => {
  it('colours each interval by its kind, in order', () => {
    const { container } = show({
      zones: [
        { kind: 'measured', fromSeconds: 0, toSeconds: 3 },
        { kind: 'estimated', fromSeconds: 3, toSeconds: 6 },
        { kind: 'measured', fromSeconds: 6, toSeconds: 7 },
        { kind: 'estimated', fromSeconds: 7, toSeconds: 9 },
      ],
    });

    const kinds = [...container.querySelectorAll('[data-zone]')].map((n) =>
      n.getAttribute('data-zone'),
    );
    expect(kinds).toEqual(['measured', 'estimated', 'measured', 'estimated']);
  });

  it('sizes an interval by its share of the seconds', () => {
    const { container } = show({
      zones: [
        { kind: 'measured', fromSeconds: 0, toSeconds: 6 },
        { kind: 'estimated', fromSeconds: 6, toSeconds: 8 },
      ],
    });

    const widths = [...container.querySelectorAll<HTMLElement>('[data-zone]')].map(
      (n) => n.style.width,
    );
    expect(widths).toEqual(['75%', '25%']);
  });

  it('states every interval in words for somebody who cannot see the colours', () => {
    show();
    const list = screen.getByRole('list', { name: texts.zones });

    const items = within(list)
      .getAllByRole('listitem')
      .map((item) => item.textContent);
    expect(items).toEqual([`${texts.measured}: 0:00–0:05`, `${texts.estimated}: 0:05–0:09`]);
  });

  it('has a legend that names both kinds', () => {
    show();
    expect(screen.getAllByText(texts.measured).length).toBeGreaterThan(0);
    expect(screen.getAllByText(texts.estimated).length).toBeGreaterThan(0);
  });
});

describe('when there is nothing to draw', () => {
  it('shows no chart section for fewer than two records', () => {
    const one: RepairSeries = {
      secondsFromStart: [0],
      original: {
        latitude: [null],
        longitude: [null],
        distanceMeters: [null],
        speedMetersPerSecond: [null],
      },
      repaired: {
        latitude: [50],
        longitude: [30],
        altitudeMeters: [null],
        distanceMeters: [0],
        speedMetersPerSecond: [3],
        heartRate: [150],
        power: [null],
        cadence: [null],
        temperatureCelsius: [null],
      },
    };

    const { container } = show({ series: one });
    expect(container).toBeEmptyDOMElement();
  });

  it('draws no repaired line where no record has a pace, while the original still draws', () => {
    const { container } = show({ series: withSeries({ repairedSpeed: Array(10).fill(0) }) });

    const repaired = container.querySelector('g.chart-repaired path.recharts-line-curve');
    expect(repaired?.getAttribute('d') ?? '').toBe('');
    const original = container.querySelector('g.chart-original path.recharts-line-curve');
    expect(original?.getAttribute('d') ?? '').not.toBe('');
  });

  it('shows no chart at all when nothing at all has a pace', () => {
    const { series } = aRoute();
    const standing: RepairSeries = {
      ...series,
      original: { ...series.original, speedMetersPerSecond: Array(10).fill(0) },
      repaired: { ...series.repaired, speedMetersPerSecond: Array(10).fill(0) },
    };

    const { container } = show({ series: standing });
    expect(container).toBeEmptyDOMElement();
  });

  it('exposes the chart to assistive technology as one image with a name', () => {
    show();
    expect(screen.getByRole('img', { name: texts.alt })).toBeInTheDocument();
  });
});

describe('the hover read-out', () => {
  const row = (change: Partial<ChartRow> = {}): ChartRow => ({
    index: 7,
    seconds: 3725,
    distanceMeters: 1000,
    repaired: 250,
    original: 300,
    heartRate: 150,
    ...change,
  });
  const read = (
    value: ChartRow,
    format: (v: number) => string,
    names = { repaired: texts.paceRepaired, original: texts.paceOriginal },
  ) =>
    render(
      <ChartTooltip
        active
        payload={[{ payload: value }]}
        names={names}
        heartRate={texts.heartRate}
        heartRateUnit={texts.heartRateUnit}
        format={format}
      />,
    );

  it('states the time and each series under its own name with its own value', () => {
    read(row(), (v) => `p${v}`);

    expect(screen.getByText('1:02:05')).toBeInTheDocument();
    expect(screen.getByText(texts.paceRepaired).nextSibling).toHaveTextContent('p250');
    expect(screen.getByText(texts.paceOriginal).nextSibling).toHaveTextContent('p300');
    expect(screen.getByText(texts.heartRate).nextSibling).toHaveTextContent(
      `150 ${texts.heartRateUnit}`,
    );
  });

  it('says nothing about a series that has no value there', () => {
    read(row({ original: null, heartRate: null }), (v) => `p${v}`);

    expect(screen.queryByText(texts.paceOriginal)).toBeNull();
    expect(screen.queryByText(texts.heartRate)).toBeNull();
    expect(screen.getByText(texts.paceRepaired)).toBeInTheDocument();
  });

  it('is empty when nothing is hovered', () => {
    const { container } = render(
      <ChartTooltip
        active={false}
        payload={[]}
        names={{ repaired: 'a', original: 'b' }}
        heartRate="c"
        heartRateUnit="d"
        format={String}
      />,
    );
    expect(container).toBeEmptyDOMElement();
  });
});

describe('what the lines are drawn from', () => {
  it('draws the original and the repaired line from their own, different, series', () => {
    const { series } = aRoute();
    const distinct: RepairSeries = {
      ...series,
      original: { ...series.original, speedMetersPerSecond: [2, 2, 2, 2, 2, 5, 5, 5, 5, 5] },
      repaired: { ...series.repaired, speedMetersPerSecond: [4, 4, 4, 4, 4, 4, 4, 4, 4, 4] },
    };
    const { container } = show({ series: distinct });

    const d = (name: string) =>
      container.querySelector(`g.${name} path.recharts-line-curve`)?.getAttribute('d');
    expect(d('chart-original')).toBeTruthy();
    expect(d('chart-repaired')).toBeTruthy();
    expect(d('chart-original')).not.toBe(d('chart-repaired'));
  });
});

describe('the axes', () => {
  const ticks = (container: HTMLElement) =>
    [...container.querySelectorAll('.recharts-yAxis-tick-labels text')].map(
      (node) => node.textContent ?? '',
    );

  it('writes a runner’s left-axis ticks as a pace', () => {
    const { container } = show({ sport: 'running' });
    // 3 m/s is 5:33 per kilometre, and the axis is minutes and seconds.
    expect(ticks(container).some((tick) => /^\d+:\d{2}$/.test(tick))).toBe(true);
  });

  it('writes a cyclist’s left-axis ticks as whole kilometres per hour', () => {
    const { container } = show({ sport: 'cycling' });
    expect(ticks(container).some((tick) => /^\d+$/.test(tick))).toBe(true);
  });
});

describe('the band beside the plot', () => {
  const band = (container: HTMLElement) =>
    container.querySelector<HTMLElement>('[data-zone]')?.parentElement;

  it('is inset on the right by the heart-rate axis when there is one', () => {
    const { container } = show();
    expect(band(container)?.style.marginRight).toBe('56px');
  });

  it('reaches the right edge when there is no heart-rate axis, like the plot does', () => {
    const { container } = show({ series: withSeries({ heartRate: Array(10).fill(null) }) });
    expect(band(container)?.style.marginRight).toBe('0px');
  });
});

describe('the cursor and the table (Story 3.4)', () => {
  function showWithCursor(options: { sport?: string; series?: RepairSeries } = {}) {
    const seen: (number | null)[] = [];
    const route = aRoute();
    const view = render(
      <TrustChart
        series={options.series ?? route.series}
        zones={route.zones}
        sport={options.sport ?? 'running'}
        texts={texts}
        units={uk.units}
        onActiveIndex={(index) => {
          seen.push(index);
        }}
      />,
    );
    return { seen, ...view };
  }

  it('reports the record index of the row the keyboard is on', async () => {
    const user = userEvent.setup();
    const { seen, container } = showWithCursor();
    const surface = container.querySelector<HTMLElement>('.recharts-wrapper [tabindex="0"]');
    expect(surface).not.toBeNull();

    surface?.focus();
    await user.keyboard('{ArrowRight}{ArrowRight}');

    // The row's own record index, one step per arrow: nothing is thinned here.
    expect(seen.filter((index) => index !== null)).toEqual([0, 1, 2]);
  });

  it('clears the record again when the keyboard leaves the plot', async () => {
    const user = userEvent.setup();
    const { seen, container } = showWithCursor();
    const surface = container.querySelector<HTMLElement>('.recharts-wrapper [tabindex="0"]');

    surface?.focus();
    await user.keyboard('{ArrowRight}');
    expect(seen[seen.length - 1]).not.toBeNull();

    surface?.blur();
    await waitFor(() => expect(seen[seen.length - 1]).toBeNull());
  });

  it('swaps the plot for a labelled table of the same rows, and clears the cursor', async () => {
    const user = userEvent.setup();
    const { seen, container } = showWithCursor();
    container.querySelector<HTMLElement>('.recharts-wrapper [tabindex="0"]')?.focus();
    await user.keyboard('{ArrowRight}');
    expect(seen[seen.length - 1]).toBe(1);

    const toggle = screen.getByRole('button', { name: texts.showTable });
    expect(toggle).toHaveAttribute('aria-pressed', 'false');
    await user.click(toggle);

    expect(toggle).toHaveAttribute('aria-pressed', 'true');
    expect(screen.queryByRole('img', { name: texts.alt })).toBeNull();
    const table = screen.getByRole('table', { name: texts.tableCaption });
    expect(
      within(table)
        .getAllByRole('columnheader')
        .map((cell) => cell.textContent),
    ).toEqual([texts.columnTime, texts.columnDistance, texts.columnPace, texts.heartRate]);
    // Ten records, a header row and ten body rows; the band under the plot stays.
    expect(within(table).getAllByRole('row')).toHaveLength(11);
    expect(screen.getByRole('list', { name: texts.zones })).toBeInTheDocument();
    expect(seen[seen.length - 1]).toBeNull();

    await user.click(toggle);
    expect(screen.getByRole('img', { name: texts.alt })).toBeInTheDocument();
  });

  it('writes each cell in words and units, and a dash where the watch recorded nothing', async () => {
    const user = userEvent.setup();
    const heart = Array<number | null>(10).fill(null);
    heart[1] = 151;
    showWithCursor({ series: withSeries({ heartRate: heart }) });
    await user.click(screen.getByRole('button', { name: texts.showTable }));

    const rows = within(screen.getByRole('table')).getAllByRole('row');
    const cells = (row: HTMLElement) =>
      within(row)
        .getAllByRole('cell')
        .map((cell) => cell.textContent);
    expect(cells(rows[2] as HTMLElement)).toEqual(['0:01', '0,00 км', '5:33 /км', '151 уд/хв']);
    expect(cells(rows[1] as HTMLElement)[3]).toBe(texts.noValue);
  });

  it('reports the record index of a thinned row, not its place in the thinned list', async () => {
    const user = userEvent.setup();
    const seen: (number | null)[] = [];
    const { container } = render(
      <TrustChart
        series={aLongRun()}
        zones={aRoute().zones}
        sport="running"
        texts={texts}
        units={uk.units}
        onActiveIndex={(index) => {
          seen.push(index);
        }}
      />,
    );

    container.querySelector<HTMLElement>('.recharts-wrapper [tabindex="0"]')?.focus();
    await user.keyboard('{ArrowRight}');

    // Row 1 of the thinned list stands for record 7 of 7200 (stride 7.2), never record 1.
    expect(seen[seen.length - 1]).toBe(thinIndices(7200)[1]);
    expect(seen[seen.length - 1]).toBeGreaterThan(1);
  });

  it('says speed for a cyclist in the table too', async () => {
    const user = userEvent.setup();
    showWithCursor({ sport: 'cycling' });
    await user.click(screen.getByRole('button', { name: texts.showTable }));

    expect(screen.getByRole('columnheader', { name: texts.columnSpeed })).toBeInTheDocument();
    expect(screen.queryByRole('columnheader', { name: texts.columnPace })).toBeNull();
  });
});
