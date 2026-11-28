import { describe, expect, it } from 'vitest';

import type { TrustZone } from './api/repair';
import type { Point } from './drawing';
import { zoneRuns } from './routeZones';

const route: Point[] = Array.from({ length: 10 }, (_, i): Point => [50 + i * 0.01, 30]);
const seconds = Array.from({ length: 10 }, (_, i) => i);
const zone = (kind: TrustZone['kind'], fromSeconds: number, toSeconds: number): TrustZone => ({
  kind,
  fromSeconds,
  toSeconds,
});

describe('zoneRuns', () => {
  it('cuts the line where the kind changes and shares the join record', () => {
    const runs = zoneRuns(route, seconds, [zone('measured', 0, 4), zone('estimated', 4, 9)]);

    expect(runs.map((run) => run.kind)).toEqual(['measured', 'estimated']);
    // The record on 4 s belongs to the zone that ends there; the estimated run starts on it.
    expect(runs[0]?.positions).toEqual(route.slice(0, 5));
    expect(runs[1]?.positions).toEqual(route.slice(4, 10));
  });

  it('follows any number of alternations in order', () => {
    const runs = zoneRuns(route, seconds, [
      zone('estimated', 6, 9),
      zone('measured', 0, 3),
      zone('estimated', 3, 6),
    ]);

    expect(runs.map((run) => run.kind)).toEqual(['measured', 'estimated']);
    expect(runs[1]?.positions).toEqual(route.slice(3, 10));
  });

  it('is one measured run when there are no zones', () => {
    const runs = zoneRuns(route, seconds, []);
    expect(runs).toEqual([{ kind: 'measured', positions: route }]);
  });

  it('is one run when every zone is the same kind', () => {
    const runs = zoneRuns(route, seconds, [zone('measured', 0, 4), zone('measured', 4, 9)]);
    expect(runs).toHaveLength(1);
    expect(runs[0]?.positions).toHaveLength(10);
  });

  it('draws an estimated start with the next zone joined to it', () => {
    const runs = zoneRuns(route, seconds, [zone('estimated', 0, 2), zone('measured', 2, 9)]);
    expect(runs.map((run) => run.kind)).toEqual(['estimated', 'measured']);
    expect(runs[0]?.positions).toEqual(route.slice(0, 3));
  });

  it('is empty for no records and drops a run of one record', () => {
    expect(zoneRuns([], [], [zone('measured', 0, 9)])).toEqual([]);
    expect(zoneRuns([[1, 1]], [0], [zone('measured', 0, 9)])).toEqual([]);
  });
});
