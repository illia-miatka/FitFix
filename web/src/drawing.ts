/**
 * The pure half of drawing a missing stretch on the map (Story 5.4): where the stretch's ends are,
 * the history of the points placed on it, and the GPX the browser sends as the track.
 *
 * Nothing here touches Leaflet or the DOM, so every edge case of the drawing is a unit test and
 * not a click. The ends are read from the series and zones the service sent — the contract carries
 * no derived value and the UI adds none of its own (AD-14).
 */

import type { TrustZone } from './api/repair';

/** A position as `[latitude, longitude]`, the order Leaflet and the rest of the web use. */
export type Point = [number, number];

/** The two places the drawn line must meet the watch's own track; a tail has no finish. */
export interface StretchEnds {
  readonly start: Point;
  readonly finish?: Point;
}

/**
 * Where the first estimated stretch that follows a measured one begins and ends.
 *
 * A record on a zone's `toSeconds` belongs to the zone that ends there (`MeasuredPositions` in the
 * core), so the start end is the last record at or before the measured zone's end, and the finish
 * end is the first record after the estimated zone's end. Both ends are therefore records whose
 * position is the watch's own (AD-26) — which is what the repair anchors the fragment to.
 *
 * Null when there is nothing to attach to: the whole run was measured, or the estimate begins at
 * the first record, which leaves no place the drawn line could start from.
 */
export function stretchEnds(
  zones: readonly TrustZone[],
  secondsFromStart: readonly number[],
  route: readonly Point[],
): StretchEnds | null {
  for (const estimated of zones) {
    if (estimated.kind !== 'estimated') continue;
    const before = zones.some(
      (zone) => zone.kind === 'measured' && zone.toSeconds === estimated.fromSeconds,
    );
    if (!before) continue;

    let startIndex = -1;
    for (let i = 0; i < secondsFromStart.length; i++) {
      if ((secondsFromStart[i] ?? Infinity) <= estimated.fromSeconds) startIndex = i;
      else break;
    }
    const start = route[startIndex];
    if (startIndex < 0 || start === undefined) continue;

    const follows = zones.some(
      (zone) => zone.kind === 'measured' && zone.fromSeconds === estimated.toSeconds,
    );
    const finishIndex = secondsFromStart.findIndex((seconds) => seconds > estimated.toSeconds);
    const finish = follows && finishIndex >= 0 ? route[finishIndex] : undefined;

    return finish === undefined ? { start } : { start, finish };
  }
  return null;
}

/**
 * The points placed so far, with what can be undone and redone. Whole snapshots rather than
 * inverse operations: a drawing is a handful of points, and a snapshot makes every action —
 * including "clear" — reversible without a second implementation of each.
 */
export interface History {
  readonly past: readonly (readonly Point[])[];
  readonly present: readonly Point[];
  readonly future: readonly (readonly Point[])[];
}

export const emptyHistory: History = { past: [], present: [], future: [] };

function commit(history: History, present: readonly Point[]): History {
  return { past: [...history.past, history.present], present, future: [] };
}

export function addPoint(history: History, point: Point): History {
  return commit(history, [...history.present, point]);
}

/** Takes the last point back; on an empty drawing there is nothing to take and nothing to undo. */
export function removeLastPoint(history: History): History {
  return history.present.length === 0 ? history : commit(history, history.present.slice(0, -1));
}

export function clearPoints(history: History): History {
  return history.present.length === 0 ? history : commit(history, []);
}

export function undo(history: History): History {
  const previous = history.past[history.past.length - 1];
  if (previous === undefined) return history;
  return {
    past: history.past.slice(0, -1),
    present: previous,
    future: [history.present, ...history.future],
  };
}

export function redo(history: History): History {
  const next = history.future[0];
  if (next === undefined) return history;
  return {
    past: [...history.past, history.present],
    present: next,
    future: history.future.slice(1),
  };
}

/**
 * The drawn line as the track the repair is asked to use: the start end, the points, and the
 * finish end when there is one. Straight segments between them; no snapping to roads.
 */
export function drawnLine(ends: StretchEnds, points: readonly Point[]): Point[] {
  return [ends.start, ...points, ...(ends.finish === undefined ? [] : [ends.finish])];
}

/**
 * A GPX 1.1 track of the line. No time and no altitude: the repair takes its clock from the
 * Original, and a track without altitude is one the core already handles. Numbers only, so there
 * is nothing to escape.
 */
export function drawnGpx(line: readonly Point[]): string {
  const points = line
    .map(([latitude, longitude]) => `<trkpt lat="${latitude}" lon="${longitude}"/>`)
    .join('');
  return (
    '<?xml version="1.0" encoding="UTF-8"?>' +
    '<gpx version="1.1" creator="FitFix" xmlns="http://www.topografix.com/GPX/1/1">' +
    `<trk><trkseg>${points}</trkseg></trk></gpx>`
  );
}

/** The file the repair request carries. The name is never logged and never shown (AD-9). */
export function drawnTrackFile(line: readonly Point[]): File {
  return new File([drawnGpx(line)], 'drawn-stretch.gpx', {
    type: 'application/gpx+xml',
  });
}
