import { describe, expect, it } from 'vitest';

import type { TrustZone } from './api/repair';
import {
  addPoint,
  clearPoints,
  drawnGpx,
  drawnLine,
  emptyHistory,
  redo,
  removeLastPoint,
  stretchEnds,
  undo,
  type Point,
} from './drawing';

const route: Point[] = Array.from({ length: 10 }, (_, i): Point => [50 + i * 0.01, 30 + i * 0.01]);
const seconds = Array.from({ length: 10 }, (_, i) => i);
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

describe('stretchEnds', () => {
  it('a tail has a start end and no finish end', () => {
    expect(stretchEnds([measured(0, 5), estimated(5, 9)], seconds, route)).toEqual({
      start: route[5],
    });
  });

  it('a middle stretch has the last measured record before and the first after', () => {
    // The record at 7 belongs to the estimated zone that ends there, so the finish is record 8.
    expect(stretchEnds([measured(0, 3), estimated(3, 7), measured(7, 9)], seconds, route)).toEqual({
      start: route[3],
      finish: route[8],
    });
  });

  it('is null when the whole run was measured', () => {
    expect(stretchEnds([measured(0, 9)], seconds, route)).toBeNull();
  });

  it('is null when the estimate begins at the first record: nothing to start from', () => {
    expect(stretchEnds([estimated(0, 4), measured(4, 9)], seconds, route)).toBeNull();
  });

  it('takes the first estimated stretch that follows a measured one', () => {
    expect(
      stretchEnds(
        [estimated(0, 1), measured(1, 3), estimated(3, 5), measured(5, 9)],
        seconds,
        route,
      ),
    ).toEqual({ start: route[3], finish: route[6] });
  });
});

describe('history', () => {
  const a: Point = [1, 1];
  const b: Point = [2, 2];

  it('adds points in order and undoes and redoes them', () => {
    const drawn = addPoint(addPoint(emptyHistory, a), b);
    expect(drawn.present).toEqual([a, b]);

    const undone = undo(drawn);
    expect(undone.present).toEqual([a]);
    expect(redo(undone).present).toEqual([a, b]);
  });

  it('a new action forgets what could have been redone', () => {
    const afterUndo = undo(addPoint(addPoint(emptyHistory, a), b));
    expect(addPoint(afterUndo, [3, 3]).future).toEqual([]);
  });

  it('removing the last point is itself undone by undo', () => {
    const drawn = addPoint(addPoint(emptyHistory, a), b);
    const removed = removeLastPoint(drawn);
    expect(removed.present).toEqual([a]);
    expect(undo(removed).present).toEqual([a, b]);
  });

  it('clear is undone by undo, so every action has an inverse', () => {
    const drawn = addPoint(addPoint(emptyHistory, a), b);
    expect(clearPoints(drawn).present).toEqual([]);
    expect(undo(clearPoints(drawn)).present).toEqual([a, b]);
  });

  it('removing from or clearing an empty drawing changes nothing and records nothing', () => {
    expect(removeLastPoint(emptyHistory)).toBe(emptyHistory);
    expect(clearPoints(emptyHistory)).toBe(emptyHistory);
  });

  it('undo and redo at the ends of the history change nothing', () => {
    expect(undo(emptyHistory)).toBe(emptyHistory);
    expect(redo(emptyHistory)).toBe(emptyHistory);
  });
});

describe('drawnLine and drawnGpx', () => {
  it('runs from the start end through the points to the finish end', () => {
    const points: Point[] = [[1, 1]];
    expect(drawnLine({ start: [0, 0], finish: [2, 2] }, points)).toEqual([
      [0, 0],
      [1, 1],
      [2, 2],
    ]);
    expect(drawnLine({ start: [0, 0] }, points)).toEqual([
      [0, 0],
      [1, 1],
    ]);
  });

  it('writes one trkpt per point in order, with no time and no altitude', () => {
    const gpx = drawnGpx([
      [50.1, 30.2],
      [50.3, 30.4],
    ]);
    const parsed = new DOMParser().parseFromString(gpx, 'application/xml');

    expect(parsed.querySelector('parsererror')).toBeNull();
    const points = [...parsed.getElementsByTagName('trkpt')];
    expect(points.map((p) => [p.getAttribute('lat'), p.getAttribute('lon')])).toEqual([
      ['50.1', '30.2'],
      ['50.3', '30.4'],
    ]);
    expect(gpx).not.toMatch(/<time|<ele/);
  });
});
