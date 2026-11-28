/**
 * Numbers into the strings a person reads, in one place.
 *
 * AD-14: the contract carries base quantities only — metres and seconds — and deliberately no
 * pace, no fraction and no difference, because a derived number travelling beside the base ones
 * can disagree with the file. The division therefore happens on the client, and it happens here:
 * the result screen and the preview Epic 3 adds read the same functions, so the pace under the
 * hero card and the pace on the chart cannot drift apart.
 *
 * The separators come in as texts rather than being spelled here, for the same reason no
 * component holds a literal (AD-18): `i18n/uk.ts` is the only place Ukrainian lives, and a comma
 * written into a formatter is a Ukrainian decision taken outside the dictionary.
 */

/** The units a distance is written in — `uk.units`, and `uk.inspection` for the zones. */
export interface DistanceTexts {
  readonly kilometres: string;
  readonly decimalSeparator: string;
}

/** The unit a pace is written in. */
export interface PaceTexts {
  readonly perKilometre: string;
}

/** What a speed is written in, with the separator a decimal takes. */
export interface SpeedTexts {
  readonly kilometresPerHour: string;
  readonly decimalSeparator: string;
}

function pad(value: number): string {
  return String(value).padStart(2, '0');
}

/** `6537.4` → `6,54 км`: two decimals, which is the resolution a running distance is read at. */
export function kilometres(metres: number, texts: DistanceTexts): string {
  const value = (metres / 1000).toFixed(2).replace('.', texts.decimalSeparator);
  return `${value} ${texts.kilometres}`;
}

/** `36:02`, and `1:12:40` once there is an hour to show. */
export function duration(seconds: number): string {
  const whole = Math.round(seconds);
  const hours = Math.floor(whole / 3600);
  const minutes = Math.floor((whole % 3600) / 60);
  const rest = pad(whole % 60);
  return hours > 0 ? `${hours}:${pad(minutes)}:${rest}` : `${minutes}:${rest}`;
}

/**
 * The pace of covering <paramref name="metres" /> in <paramref name="seconds" />, as `5:30 /км`.
 *
 * Null when either quantity is missing or not positive: a watch that recorded no distance has no
 * pace, and an activity that stood still has none either. Zero would read as a measurement, and
 * dividing by zero would print `Infinity:NaN` at the top of the screen.
 */
export function pace(metres: number, seconds: number, texts: PaceTexts): string | null {
  if (!(metres > 0) || !(seconds > 0)) return null;
  return `${duration((seconds * 1000) / metres)} ${texts.perKilometre}`;
}

/** `12.34` km/h → `12,3 км/год`: one decimal, which is how a cyclist reads a speed. */
export function speed(kilometresPerHour: number, texts: SpeedTexts): string {
  return `${kilometresPerHour.toFixed(1).replace('.', texts.decimalSeparator)} ${texts.kilometresPerHour}`;
}

/**
 * An instant as `19.11.2026, 07:12`, in the reader's own time zone: the question it answers is
 * "is this the run I mean", and a Kyiv morning shown in UTC reads as a different hour.
 */
export function dateTime(startedAt: string): string {
  // The contract sends UTC with a trailing `Z`. A string without a designator would be read as
  // local time instead, which is a silent shift, so one is ensured rather than assumed.
  const utc = /(?:Z|[+-]\d{2}:?\d{2})$/.test(startedAt) ? startedAt : `${startedAt}Z`;
  const at = new Date(utc);
  return `${pad(at.getDate())}.${pad(at.getMonth() + 1)}.${at.getFullYear()}, ${pad(at.getHours())}:${pad(at.getMinutes())}`;
}
