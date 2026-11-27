/**
 * The answers the stubbed service gives, in one place.
 *
 * Three suites drive the same screen from the same two files — `App.test.tsx` through the whole
 * page, `Dropzone.test.tsx` through one zone, `Result.test.tsx` through the result — so each one
 * used to carry its own transcription of the same inspection and the same report. Three copies of
 * a hand-written contract (AD-14) drift on the first field the server renames, and the copy that
 * drifts silently is the one nobody typed: an untyped literal agrees with any contract at all.
 * Here the report is typed as `RepairReport` and the inspections as what `/api/inspect` returns,
 * so a contract change fails the typecheck instead of leaving a suite green against a shape the
 * service no longer sends.
 *
 * Every number is the 19.11.2026 case the project started from: 3,91 km recorded by the device,
 * 6,54 km of real track. A test that needs a different answer passes overrides rather than
 * writing a second report.
 */

import type { OriginalInspection, TrackInspection } from '../api/inspect';
import type { RepairReport } from '../api/repair';

/** The bytes are never read: the stubbed service decides what the file turns out to be. */
export function fitFile(name: string): File {
  return new File([new Uint8Array([0x0e, 0x10, 0x43, 0x5a])], name, {
    type: 'application/octet-stream',
  });
}

/**
 * One answer from the service. A hand-made object rather than a `Response`: the client reads
 * `ok`, `status` and `json()`, and nothing in jsdom has to provide the rest.
 */
export function answers(payload: unknown, status = 200): Response {
  return { ok: status >= 200 && status < 300, status, json: async () => payload } as Response;
}

/** The original as `/api/inspect` describes it. */
export const anOriginal: OriginalInspection = {
  role: 'original',
  sport: 'running',
  startedAt: '2026-11-19T07:12:03Z',
  elapsedSeconds: 2162,
  recordedDistanceMeters: 3911,
  recordCount: 2163,
  hasPosition: true,
  discardedPositionCount: 0,
  codes: [],
};

/** The track as `/api/inspect` describes it, after de-duplication. */
export const aTrack: TrackInspection = {
  role: 'track',
  lengthMeters: 6537.4,
  pointCount: 1248,
  format: 'gpx',
  hasAltitude: true,
};

/**
 * One repair of that pair: the device's own 3,91 km replaced by the track's 6,54 km, the clock
 * untouched. The two zones are the real shape of the case — the position was measured until the
 * GPS stalled and estimated from there (FR-4) — so a suite that shows zones gets them without
 * asking, and one that does not is unaffected.
 */
export function aReport(overrides: Partial<RepairReport> = {}): RepairReport {
  return {
    before: {
      sport: 'running',
      startedAt: '2026-11-19T07:12:03Z',
      recordCount: 2163,
      recordedDistanceMeters: 3911,
      discardedPositionCount: 0,
    },
    after: {
      distanceMeters: 6537.4,
      elapsedSeconds: 2162,
      movingSeconds: 2162,
      pausedSeconds: 0,
      averageSpeedMetersPerSecond: 3.024,
      maximumSpeedMetersPerSecond: 4.1,
      averageHeartRate: 154,
      maximumHeartRate: 171,
      averagePower: null,
      maximumPower: null,
      averageCadence: 84,
      maximumCadence: 96,
      elevationGainMeters: 41,
      elevationLossMeters: 39,
      lapCount: 1,
    },
    track: { lengthMeters: 6537.4, usedLengthMeters: 6537.4 },
    zones: [
      { kind: 'measured', fromSeconds: 0, toSeconds: 1700 },
      { kind: 'estimated', fromSeconds: 1700, toSeconds: 2162 },
    ],
    series: {
      secondsFromStart: [0, 1],
      original: {
        latitude: [50.3333, null],
        longitude: [30.3833, null],
        distanceMeters: [0, null],
        speedMetersPerSecond: [3, null],
      },
      repaired: {
        latitude: [50.3333, 50.3334],
        longitude: [30.3833, 30.3834],
        altitudeMeters: [182.8, 182.9],
        distanceMeters: [0, 3],
        speedMetersPerSecond: [3, 3],
        heartRate: [150, 151],
        power: [null, null],
        cadence: [85, 85],
        temperatureCelsius: [4, 4],
      },
    },
    // Nothing was redrawn from a fragment: this pair is the whole route (AD-27).
    redrawn: null,
    warnings: [],
    // `FIT\u0001\u0002\u0003` — short, and its bytes are checked after the round trip.
    fitBase64: 'RklUAQID',
    ...overrides,
  };
}

/**
 * The same repair of a track the service recognised as a stretch of the route: the blocking
 * warning and the span it names (Story 5.2). Pass it to `aReport` — the confirmed answer is the
 * plain report, the same numbers with nothing left to ask.
 */
export const aFragment = {
  redrawn: { fromSeconds: 1612, toSeconds: 1976, distanceMeters: 1234 },
  warnings: [{ code: 'TRACK_IS_A_FRAGMENT', severity: 'blocking' }],
} satisfies Partial<RepairReport>;
