import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { RouteMap } from './RouteMap';
import type { DrawAnswer } from './StretchDrawing';
import type { Point } from '../drawing';
import type { TrustZone } from '../api/repair';
import { uk } from '../i18n/uk';
import { aRoute } from '../test/fixtures';

/**
 * Story 5.4: drawing the missing stretch on the map. The pure parts (ends, history, GPX) have their
 * own tests in `drawing.test.ts`; this file drives the mode through the DOM a person's tools see.
 */

const draw = uk.result.map.draw;
const measured = (fromSeconds: number, toSeconds: number): TrustZone => ({
  kind: 'measured',
  fromSeconds,
  toSeconds,
});
const estimated = (fromSeconds: number, toSeconds: number): TrustZone => ({
  kind: 'estimated',
  fromSeconds,
  toSeconds,
});

/** The tail the fixture has, and a middle stretch with a measured zone after it. */
const tailZones = [measured(0, 5), estimated(5, 9)];
const middleZones = [measured(0, 3), estimated(3, 6), measured(6, 9)];

function show(
  zones: readonly TrustZone[] = tailZones,
  onDraw: ((line: Point[]) => Promise<DrawAnswer>) | null = async () => ({ ok: true }),
) {
  const { series } = aRoute();
  return render(
    <RouteMap series={series} zones={zones} texts={uk.result.map} onDraw={onDraw ?? undefined} />,
  );
}

const map = () => screen.getByRole('region', { name: uk.result.map.label });
const begin = () => screen.getByRole('button', { name: draw.begin });
const confirm = () => screen.getByRole('button', { name: draw.confirm });
const points = (count: number) => screen.getByText(`${draw.points} ${count}`);

/** One point at the map centre, the way a keyboard places it. */
function placeAtCentre() {
  fireEvent.keyDown(map(), { key: 'Enter' });
}

describe('offering the drawing', () => {
  it('is offered when an estimated stretch follows a measured one', () => {
    show();
    expect(begin()).toBeEnabled();
  });

  it('is not offered when the screen cannot act on a drawing', () => {
    show(tailZones, null);
    expect(screen.queryByRole('button', { name: draw.begin })).toBeNull();
  });

  it('is not offered when the whole run was measured', () => {
    show([measured(0, 9)]);
    expect(screen.queryByRole('button', { name: draw.begin })).toBeNull();
  });

  it('is not offered when the estimate begins at the first record: nothing to start from', () => {
    show([estimated(0, 4), measured(4, 9)]);
    expect(screen.queryByRole('button', { name: draw.begin })).toBeNull();
  });
});

describe('starting to draw', () => {
  it('shows the start end and the toolbar, and keeps confirming off until a point is placed', async () => {
    show();
    await userEvent.click(begin());

    expect(screen.getByRole('button', { name: draw.startEnd })).toBeInTheDocument();
    expect(screen.getByRole('toolbar', { name: draw.toolbar })).toBeInTheDocument();
    expect(points(0)).toBeInTheDocument();
    expect(confirm()).toBeDisabled();
  });

  it('a tail has no finish end', async () => {
    show();
    await userEvent.click(begin());
    expect(screen.queryByRole('button', { name: draw.finishEnd })).toBeNull();
  });

  it('a middle stretch shows both ends', async () => {
    show(middleZones);
    await userEvent.click(begin());

    expect(screen.getByRole('button', { name: draw.startEnd })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: draw.finishEnd })).toBeInTheDocument();
  });

  it('closing the drawing forgets the points', async () => {
    show();
    await userEvent.click(begin());
    placeAtCentre();
    expect(points(1)).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: draw.cancel }));
    await userEvent.click(begin());

    expect(points(0)).toBeInTheDocument();
  });
});

describe('placing and taking back points', () => {
  it('Enter places a point at the centre, Backspace takes it back, the undo keys bring it back', async () => {
    show();
    await userEvent.click(begin());

    placeAtCentre();
    expect(points(1)).toBeInTheDocument();
    expect(document.querySelectorAll('path.route-drawn-point')).toHaveLength(1);

    fireEvent.keyDown(map(), { key: 'Backspace' });
    expect(points(0)).toBeInTheDocument();

    fireEvent.keyDown(map(), { key: 'z', ctrlKey: true });
    expect(points(1)).toBeInTheDocument();

    fireEvent.keyDown(map(), { key: 'z', ctrlKey: true });
    expect(points(0)).toBeInTheDocument();

    fireEvent.keyDown(map(), { key: 'z', ctrlKey: true, shiftKey: true });
    expect(points(1)).toBeInTheDocument();
  });

  it('a click on the map places a point', async () => {
    show();
    await userEvent.click(begin());

    fireEvent.click(map(), { clientX: 100, clientY: 100 });

    expect(points(1)).toBeInTheDocument();
  });

  it('Enter on a marker belongs to the marker and places nothing', async () => {
    show();
    await userEvent.click(begin());

    fireEvent.keyDown(screen.getByRole('button', { name: draw.startEnd }), { key: 'Enter' });

    expect(points(0)).toBeInTheDocument();
  });

  it('every action has an inverse through the buttons too', async () => {
    show();
    await userEvent.click(begin());
    placeAtCentre();
    placeAtCentre();

    await userEvent.click(screen.getByRole('button', { name: draw.clear }));
    expect(points(0)).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: draw.undo }));
    expect(points(2)).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: draw.removeLast }));
    expect(points(1)).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: draw.undo }));
    expect(points(2)).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: draw.redo }));
    expect(points(1)).toBeInTheDocument();
  });

  it('draws the line in the drawing colour class, and none before the first point is wanted', async () => {
    show();
    expect(document.querySelectorAll('path.route-drawn')).toHaveLength(0);

    await userEvent.click(begin());
    placeAtCentre();

    expect(document.querySelectorAll('path.route-drawn')).toHaveLength(1);
  });
});

describe('confirming', () => {
  it('hands over the line: the start end, the points, and the finish end of a middle stretch', async () => {
    const onDraw = vi.fn(async (_line: Point[]): Promise<DrawAnswer> => ({ ok: true }));
    show(middleZones, onDraw);
    await userEvent.click(begin());
    placeAtCentre();
    placeAtCentre();

    await userEvent.click(confirm());

    const { series } = aRoute();
    const route = series.repaired.latitude.map((lat, i) => [lat, series.repaired.longitude[i]]);
    expect(onDraw).toHaveBeenCalledTimes(1);
    const line = onDraw.mock.calls[0]?.[0] as unknown as Point[];
    expect(line).toHaveLength(4);
    expect(line[0]).toEqual(route[3]);
    expect(line[3]).toEqual(route[7]);
  });

  it('a tail ends on the last point drawn', async () => {
    const onDraw = vi.fn(async (_line: Point[]): Promise<DrawAnswer> => ({ ok: true }));
    show(tailZones, onDraw);
    await userEvent.click(begin());
    placeAtCentre();

    await userEvent.click(confirm());

    const line = onDraw.mock.calls[0]?.[0] as unknown as Point[];
    expect(line).toHaveLength(2);
  });

  it('an answer that worked ends the drawing', async () => {
    show();
    await userEvent.click(begin());
    placeAtCentre();

    await userEvent.click(confirm());

    await waitFor(() => expect(begin()).toBeInTheDocument());
    expect(screen.queryByRole('toolbar', { name: draw.toolbar })).toBeNull();
  });

  it('a refusal keeps the drawing and says it in the sentence it was given', async () => {
    show(tailZones, async () => ({ ok: false, message: 'Службовий текст відмови' }));
    await userEvent.click(begin());
    placeAtCentre();

    await userEvent.click(confirm());

    expect(await screen.findByText('Службовий текст відмови')).toBeInTheDocument();
    expect(points(1)).toBeInTheDocument();
  });

  it('a request that never arrived keeps the drawing and uses the map’s own sentence', async () => {
    show(tailZones, async () => ({ ok: false }));
    await userEvent.click(begin());
    placeAtCentre();

    await userEvent.click(confirm());

    expect(await screen.findByText(draw.failed)).toBeInTheDocument();
    expect(points(1)).toBeInTheDocument();
  });

  it('while the repeat is out the buttons are off and a second press sends nothing', async () => {
    let finish: (answer: DrawAnswer) => void = () => {};
    const onDraw = vi.fn(
      () =>
        new Promise<DrawAnswer>((resolve) => {
          finish = resolve;
        }),
    );
    show(tailZones, onDraw);
    await userEvent.click(begin());
    placeAtCentre();

    await userEvent.click(confirm());
    const busy = screen.getByRole('button', { name: draw.confirming });
    expect(busy).toBeDisabled();
    expect(screen.getByRole('button', { name: draw.clear })).toBeDisabled();
    fireEvent.click(busy);
    expect(onDraw).toHaveBeenCalledTimes(1);

    finish({ ok: true });
    await waitFor(() => expect(begin()).toBeInTheDocument());
  });
});
