/**
 * Whether a failure the service stated is worth asking again, which is a question only the
 * response's status can answer.
 *
 * `FitFix.Api.FailureStatus` decides the status from the code, and it deliberately splits the
 * registry in two (`FailureContractTests` pins the split). A 400 says the input was wrong:
 * `NOT_A_FIT_FILE` means those bytes are not a FIT, and they will not have become one by the time
 * a second request arrives. A 500 says our own invariant broke on the way —
 * `SUMMARY_SERIES_MISMATCH`, `SUMMARY_WITHOUT_LAPS`, `DISTANCE_SERIES_IMPLAUSIBLE` and
 * `INTERNAL_ERROR` all keep their code while leaving as 500 precisely so the athlete is not told
 * their file is wrong. That is the failure the dictionary ends with «Спробуй ще раз» and the one
 * EXPERIENCE.md asks for a retry on, so the interface has to be able to tell the two apart.
 *
 * It lives beside the two API modules rather than inside either: a zone and the repair must not
 * be able to disagree about which failures may be repeated.
 */

/**
 * The codes that are about the journey rather than about the request's contents. `REQUEST_MALFORMED`
 * is `FailureStatus.ForStatus`'s answer to every unnamed sub-500 status, so it is what a truncated
 * upload or a timed-out one arrives as: the file on disk is intact and the transfer is what broke,
 * which is the one 4xx that a second attempt can end differently. Its own sentence says so.
 *
 * `TOO_MANY_REQUESTS` is the other: the service refused to work right now, not to work on these
 * files. Pressing again a minute later is exactly the right thing to do, so the button has to be
 * there — a 429 with no retry would read as a verdict on the run.
 */
const repeatableCodes: ReadonlySet<string> = new Set(['REQUEST_MALFORMED', 'TOO_MANY_REQUESTS']);

/**
 * Whether a failure that left with <paramref name="status" /> and <paramref name="code" /> may be
 * repeated with any hope.
 *
 * The server's own failures qualify, and so does a request that never arrived whole. Every other
 * 4xx is about what was sent — the files, their size, the route — and sending it again would meet
 * the same answer.
 */
export function worthRepeating(status: number, code: string): boolean {
  return status >= 500 || repeatableCodes.has(code);
}
