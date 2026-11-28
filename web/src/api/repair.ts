/**
 * `POST /api/repair` as the client sees it (Story 2.6).
 *
 * The contract is hand-written on the server (AD-14), so it is read here exactly once: the two
 * summaries, the track's two lengths, the zones, the series, the warnings with their severities,
 * and the repaired file as base64. A component that parsed the body itself would be a second
 * transcription of the same contract, and the two would drift on the first field the server
 * renames.
 *
 * Only base quantities arrive — metres, seconds, counts. Every pace, share and difference the
 * screen shows is computed from them in `format.ts`, which is the whole reason the contract
 * refuses to carry them (AD-14).
 *
 * No base URL and no CORS, for the same reason as the inspect call: one origin serves the SPA and
 * `/api/*` in production, and Vite proxies `/api` in development (AD-12).
 */

import { worthRepeating } from './failure';

/** The route, spelled as `FitFix.Api.RepairEndpoint.Route` spells it. */
const route = '/api/repair';

/**
 * The two file parts, named for the roles the endpoint matches exactly. A FIT file is valid as an
 * activity and as a track, so the server never guesses which is which (AD-15) — these names are
 * the whole of what tells it.
 */
const originalField = 'original';
const trackField = 'track';

/** `RepairEndpoint.TrackLengthModeField` — absent means the conservative length. */
const trackLengthModeField = 'trackLengthMode';

/** `RepairEndpoint.TrackCoverageField` — absent means the service decides and asks (AD-27). */
const trackCoverageField = 'trackCoverage';

/**
 * Which of the two track lengths to end on, as `TrackLengthModes` names them (AD-17). `full` is
 * sent only after the user has chosen to trust the track at the `TRACK_LONGER_THAN_ACTIVITY`
 * warning (Story 3.5); a request that has asked nobody anything never sends it.
 */
export type TrackLengthMode = 'plausible' | 'full';

/**
 * What the client knows about how much of the route the track covers, as `TrackCoverageModes`
 * names them (AD-27). `fragment` is sent when the user has answered the `TRACK_IS_A_FRAGMENT`
 * warning — the stretch they were shown is the stretch they meant — and `whole` says the track is
 * the whole route whatever its length suggests, which the result screen offers as the other answer to the fragment question.
 */
export type TrackCoverageMode = 'detect' | 'fragment' | 'whole';

/** `OriginalSummaryResponse`: what the device itself recorded. */
export interface OriginalSummary {
  readonly sport: string;
  /** The first record's timestamp, in UTC, with a designator — the contract sends `…Z`. */
  readonly startedAt: string;
  readonly recordCount: number;
  /** Null when no record carried a distance: a device that measured nothing has not measured zero. */
  readonly recordedDistanceMeters: number | null;
  readonly discardedPositionCount: number;
}

/**
 * `RepairedSummaryResponse`: the repaired activity, as the written file states it. The durations
 * are the Original's own — the repair keeps its clock — so they are also the "before" ones, and
 * `movingSeconds` is the denominator of both paces.
 */
export interface RepairedSummary {
  readonly distanceMeters: number;
  readonly elapsedSeconds: number;
  readonly movingSeconds: number;
  readonly pausedSeconds: number;
  readonly averageSpeedMetersPerSecond: number | null;
  readonly maximumSpeedMetersPerSecond: number | null;
  readonly averageHeartRate: number | null;
  readonly maximumHeartRate: number | null;
  readonly averagePower: number | null;
  readonly maximumPower: number | null;
  readonly averageCadence: number | null;
  readonly maximumCadence: number | null;
  readonly elevationGainMeters: number | null;
  readonly elevationLossMeters: number | null;
  readonly lapCount: number;
}

/** `TrackLengthsResponse`: both lengths, equal whenever the track was taken whole (AD-17). */
export interface TrackLengths {
  readonly lengthMeters: number;
  readonly usedLengthMeters: number;
}

/** `TrustZoneResponse`: one stretch and what the repair knew about the position across it. */
export interface TrustZone {
  readonly kind: 'measured' | 'estimated';
  readonly fromSeconds: number;
  readonly toSeconds: number;
}

/**
 * `RedrawnSpanResponse`: the stretch a fragment of the route redrew, in the same seconds the zones
 * and the series are stated in, and the metres the fragment drew there (AD-27). Null on the report
 * when the track covered the whole activity.
 */
export interface RedrawnSpan {
  readonly fromSeconds: number;
  readonly toSeconds: number;
  readonly distanceMeters: number;
}

/**
 * `RepairWarningResponse`. The severity decides how loudly the warning is shown, and for
 * `TRACK_IS_A_FRAGMENT` the download gate too (AD-22); the text itself is the dictionary's, keyed
 * by the code (AD-18).
 */
export interface RepairWarning {
  readonly code: string;
  readonly severity: 'blocking' | 'info';
}

/** `OriginalSeriesResponse`: the cleaned series, never the raw one (AD-25). */
export interface OriginalSeries {
  readonly latitude: readonly (number | null)[];
  readonly longitude: readonly (number | null)[];
  readonly distanceMeters: readonly (number | null)[];
  readonly speedMetersPerSecond: readonly (number | null)[];
}

/** `RepairedSeriesResponse`: exactly what the returned file carries. */
export interface RepairedSeries {
  readonly latitude: readonly number[];
  readonly longitude: readonly number[];
  readonly altitudeMeters: readonly (number | null)[];
  readonly distanceMeters: readonly number[];
  readonly speedMetersPerSecond: readonly number[];
  readonly heartRate: readonly (number | null)[];
  readonly power: readonly (number | null)[];
  readonly cadence: readonly (number | null)[];
  readonly temperatureCelsius: readonly (number | null)[];
}

/**
 * `RepairSeriesResponse`: every series as long as `before.recordCount`, index `i` always the
 * `i`-th record of the Original, nothing thinned (AD-5). This story shows none of it — the map
 * and the chart are Epic 3 — but it is part of the one answer, so it is typed where the rest is.
 */
export interface RepairSeries {
  readonly secondsFromStart: readonly number[];
  readonly original: OriginalSeries;
  readonly repaired: RepairedSeries;
}

/** `RepairResponse`: one repair, numbers and file together, because nothing is stored (AD-3). */
export interface RepairReport {
  readonly before: OriginalSummary;
  readonly after: RepairedSummary;
  readonly track: TrackLengths;
  readonly zones: readonly TrustZone[];
  /** The stretch a fragment redrew, or null when the whole route was used (AD-27). */
  readonly redrawn: RedrawnSpan | null;
  readonly series: RepairSeries;
  readonly warnings: readonly RepairWarning[];
  /** The repaired FIT, base64-encoded. */
  readonly fitBase64: string;
}

/**
 * What one repair can come back as. The three are different things to say to a person, and
 * `unreachable` is not a refusal: a failure with a code means the service answered and the
 * dictionary has the sentence for it; a request that never arrived says nothing at all about
 * either file, and calling one bad would send somebody hunting for a problem that is not theirs.
 */
export type RepairOutcome =
  | { readonly status: 'repaired'; readonly report: RepairReport }
  | {
      readonly status: 'refused';
      readonly code: string;
      /**
       * Whether asking again could end differently. A 400 is about the two files that were sent
       * and never will (`worthRepeating`); the repair's own broken invariants leave as 500 with
       * their code, and those are the ones worth one more press.
       */
      readonly worthRepeating: boolean;
    }
  | { readonly status: 'unreachable' };

/**
 * One repair: both files in one request, and the numbers and the file in one answer.
 *
 * @param mode Which of the two track lengths to end on. Omitted is the conservative one, which is
 * the only length a request that has asked nobody anything may inherit (AD-17).
 * @param coverage What is already known about the track's coverage. Omitted leaves the service to
 * decide and to ask before the file is anybody's, which is the only coverage an unasked request may
 * inherit (AD-27).
 */
export async function repair(
  original: File,
  track: File,
  mode?: TrackLengthMode,
  coverage?: TrackCoverageMode,
): Promise<RepairOutcome> {
  const body = new FormData();
  body.append(originalField, original, original.name);
  body.append(trackField, track, track.name);
  if (mode !== undefined) body.append(trackLengthModeField, mode);
  if (coverage !== undefined) body.append(trackCoverageField, coverage);

  let response: Response;
  try {
    response = await fetch(route, { method: 'POST', body });
  } catch {
    // A rejected fetch is the network, not the files: offline, a dead dev server, a DNS failure.
    return { status: 'unreachable' };
  }

  const payload = await readObject(response);
  if (payload === null) return { status: 'unreachable' };

  if (!response.ok) {
    // Every failure is a problem document carrying a code from the core's registry (AD-22). A
    // body without one is not a refusal this interface can state, so it is not dressed up as
    // one — the dictionary would have nothing to look up.
    const { code } = payload;
    return typeof code === 'string'
      ? { status: 'refused', code, worthRepeating: worthRepeating(response.status, code) }
      : { status: 'unreachable' };
  }

  // The file is what the whole screen stands on, so its presence is what makes the cast below
  // true rather than hopeful. The rest of the body is the server's hand-written record, which
  // its own contract test pins (`ContractShapeTests`).
  if (typeof payload.fitBase64 !== 'string' || !isObject(payload.before) || !isObject(payload.after))
    return { status: 'unreachable' };

  return { status: 'repaired', report: payload as unknown as RepairReport };
}

function isObject(value: unknown): boolean {
  return typeof value === 'object' && value !== null;
}

/** The response's body as an object, or null when it is not one — an HTML error page, say. */
async function readObject(response: Response): Promise<Record<string, unknown> | null> {
  try {
    const parsed: unknown = await response.json();
    return typeof parsed === 'object' && parsed !== null
      ? (parsed as Record<string, unknown>)
      : null;
  } catch {
    return null;
  }
}
