/**
 * The repaired line cut by trust zone (Story 3.4), so the map can colour the calculated stretches
 * the way the chart's band does. Both read the same `report.zones` list (AD-20).
 */

import type { TrustZone } from './api/repair';
import type { Point } from './drawing';

/** One stretch of the line that has a single kind of trust. */
export interface ZoneRun {
  readonly kind: TrustZone['kind'];
  readonly positions: Point[];
}

/**
 * The zone a record at `seconds` belongs to. A record on a zone's `toSeconds` belongs to the zone
 * that ends there — the rule `RepairService.MeasuredPositions` uses — so the first zone whose end
 * is at or after the record wins; a record past every end takes the last zone.
 */
function kindAt(zones: readonly TrustZone[], seconds: number): TrustZone['kind'] {
  const hit = zones.find((zone) => seconds <= zone.toSeconds) ?? zones[zones.length - 1];
  return hit?.kind ?? 'measured';
}

/**
 * Runs of consecutive records with the same kind, in record order. Each run after the first starts
 * on the last record of the one before, so the line is unbroken at every join. A run of one record
 * draws nothing and is dropped. Without zones the whole line is one measured run.
 */
export function zoneRuns(
  route: readonly Point[],
  secondsFromStart: readonly number[],
  zones: readonly TrustZone[],
): ZoneRun[] {
  const ordered = zones.toSorted((a, b) => a.fromSeconds - b.fromSeconds);
  const runs: { kind: TrustZone['kind']; positions: Point[] }[] = [];

  route.forEach((position, i) => {
    const kind = kindAt(ordered, secondsFromStart[i] ?? 0);
    const last = runs[runs.length - 1];
    if (last !== undefined && last.kind === kind) {
      last.positions.push(position);
      return;
    }
    // A join: the new run begins where the previous one ended.
    const join = last?.positions[last.positions.length - 1];
    runs.push({ kind, positions: join === undefined ? [position] : [join, position] });
  });

  return runs.filter((run) => run.positions.length >= 2);
}
