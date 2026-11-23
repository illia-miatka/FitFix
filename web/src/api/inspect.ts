/**
 * `POST /api/inspect` as the client sees it (Story 2.5).
 *
 * The contract is hand-written on the server (AD-14), so it is read here exactly once: the two
 * answer shapes, the problem's code, and the three outcomes a zone can show. A component that
 * parsed a response of its own would be a second transcription of the same contract, and the
 * two would drift on the first field the server renames.
 *
 * No base URL and no CORS: in production the API serves the SPA and `/api/*` from one origin,
 * and in development Vite proxies `/api` to Kestrel (AD-12), so a relative path is the whole
 * configuration.
 */

/** The route, spelled as `FitFix.Api.InspectEndpoint.Route` spells it. */
const route = '/api/inspect';

/** The form field naming the role, as `InspectEndpoint.RoleField` names it. */
const roleField = 'role';

/**
 * The field the file travels in. The server reads the request's first file part and never its
 * name, so this is for the request log of whoever is debugging it, nothing more.
 */
const fileField = 'file';

/** `OriginalInspectionResponse`: what the core read from a file offered as the original. */
export interface OriginalInspection {
  readonly role: 'original';
  readonly sport: string;
  /** The first record's timestamp, in UTC, with a designator — the contract sends `…Z`. */
  readonly startedAt: string;
  readonly elapsedSeconds: number;
  /** Null when no record carried a distance: a device that measured nothing has not measured zero. */
  readonly recordedDistanceMeters: number | null;
  readonly recordCount: number;
  readonly hasPosition: boolean;
  readonly discardedPositionCount: number;
  /** Codes from the core's registry, in the order the repair reports them (AD-18). */
  readonly codes: readonly string[];
}

/** `TrackInspectionResponse`: the track as the repair will build it, after de-duplication. */
export interface TrackInspection {
  readonly role: 'track';
  readonly lengthMeters: number;
  readonly pointCount: number;
  /** `fit`, `gpx` or `tcx`, decided by the file's own bytes and never by its name (AD-13). */
  readonly format: string;
  readonly hasAltitude: boolean;
}

export type Inspection = OriginalInspection | TrackInspection;

/**
 * What one ask can come back as. The three are different things to say to a person, which is
 * why `unreachable` is not a refusal: a 400 with a code means this file is not what the zone
 * wants, and the dictionary has the sentence for it; a request that never arrived says nothing
 * at all about the file, and calling it bad would send somebody hunting for a problem that is
 * not theirs.
 */
export type InspectOutcome<T extends Inspection = Inspection> =
  | { readonly status: 'described'; readonly inspection: T }
  | { readonly status: 'refused'; readonly code: string }
  | { readonly status: 'unreachable' };

/** The inspection of a file offered as the original. */
export function inspectOriginal(file: File): Promise<InspectOutcome<OriginalInspection>> {
  return inspect<OriginalInspection>('original', file);
}

/** The inspection of a file offered as the reference track. */
export function inspectTrack(file: File): Promise<InspectOutcome<TrackInspection>> {
  return inspect<TrackInspection>('track', file);
}

async function inspect<T extends Inspection>(
  role: T['role'],
  file: File,
): Promise<InspectOutcome<T>> {
  const body = new FormData();
  body.append(roleField, role);
  body.append(fileField, file, file.name);

  let response: Response;
  try {
    response = await fetch(route, { method: 'POST', body });
  } catch {
    // A rejected fetch is the network, not the file: offline, a dead dev server, a DNS failure.
    return { status: 'unreachable' };
  }

  const payload = await readObject(response);
  if (payload === null) return { status: 'unreachable' };

  if (!response.ok) {
    // Every failure is a problem document carrying a code from the core's registry (AD-22).
    // A body without one is not a refusal this interface can state, so it is not dressed up
    // as one — the dictionary would have nothing to look up.
    const { code } = payload;
    return typeof code === 'string' ? { status: 'refused', code } : { status: 'unreachable' };
  }

  // The answer repeats the role it was asked about, and that member is what discriminates the
  // two shapes (AD-15). Checking it is what makes the cast below true rather than hopeful: the
  // rest of the body is the server's hand-written record, which its own contract test pins.
  if (payload.role !== role) return { status: 'unreachable' };

  return { status: 'described', inspection: payload as unknown as T };
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
