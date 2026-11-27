import { render, screen, waitFor, within } from '@testing-library/react';
import type { CoverageAnswer } from '../App';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { Result } from './Result';
import type { RepairReport } from '../api/repair';
import { uk } from '../i18n/uk';

/**
 * Story 2.6: the result screen. The report is a hand-made object throughout — these tests are
 * about what the screen says about an answer, never about the repair, which has its own tests in
 * `tests/FitFix.Core.Tests` and `tests/FitFix.Api.Tests`.
 *
 * Every expected number below is worked out from the base quantities in plain arithmetic rather
 * than by calling `format.ts`: a test that asked the formatter what the formatter should say would
 * agree with any pace at all.
 */

/** The 19.11.2026 case the project started from: 3,91 km recorded, 6,54 km of real track. */
function aReport(overrides: Partial<RepairReport> = {}): RepairReport {
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

/** The fragment warning as the service raises it, with the stretch it names (Story 5.2). */
const aFragment = {
  redrawn: { fromSeconds: 1612, toSeconds: 1976, distanceMeters: 1234 },
  warnings: [{ code: 'TRACK_IS_A_FRAGMENT', severity: 'blocking' }],
} satisfies Partial<RepairReport>;

function show(
  report: RepairReport,
  onStartAgain = () => {},
  onConfirmFragment: () => Promise<CoverageAnswer> = async () => ({ ok: true }),
  onUseWholeRoute: () => Promise<CoverageAnswer> = async () => ({ ok: true }),
) {
  return render(
    <Result
      report={report}
      originalName="Run_4k_Chabany.fit"
      texts={uk.result}
      units={uk.units}
      codes={uk.codes}
      onStartAgain={onStartAgain}
      onConfirmFragment={onConfirmFragment}
      onUseWholeRoute={onUseWholeRoute}
    />,
  );
}

/** The card of one metric: its label, and the two values beside it. */
function metric(label: string): HTMLElement {
  const term = screen.getByText(label);
  expect(term.parentElement).not.toBeNull();
  return term.parentElement!;
}

/** The value DESIGN.md puts in `accent` — the new one, which is the point of the component. */
function newValueOf(card: HTMLElement): string {
  const accented = card.querySelector('.text-accent');
  expect(accented, 'the new value should be the one in the accent colour').not.toBeNull();
  return accented!.textContent ?? '';
}

/** A blob's bytes. `FileReader`, because jsdom's `Blob` carries no `arrayBuffer`. */
function bytesOf(blob: Blob): Promise<Uint8Array> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(new Uint8Array(reader.result as ArrayBuffer));
    reader.onerror = () => reject(reader.error ?? new Error('The blob could not be read.'));
    reader.readAsArrayBuffer(blob);
  });
}

function paceOf(metres: number, seconds: number): string {
  const perKilometre = Math.round((seconds * 1000) / metres);
  const minutes = Math.floor(perKilometre / 60);
  const rest = String(perKilometre % 60).padStart(2, '0');
  return `${minutes}:${rest} ${uk.units.perKilometre}`;
}

describe('the numbers', () => {
  it('states distance, time and pace as before → after', () => {
    const report = aReport();
    show(report);

    const distance = metric(uk.result.distance);
    expect(distance).toHaveTextContent(`${uk.result.before}: 3,91 ${uk.units.kilometres}`);
    expect(newValueOf(distance)).toContain(`6,54 ${uk.units.kilometres}`);

    // The repair keeps the Original's clock, so the two sides of the time row are equal — which
    // is the claim worth making: the distance changed and the duration did not.
    const time = metric(uk.result.time);
    expect(time).toHaveTextContent('36:02');
    expect(newValueOf(time)).toContain('36:02');

    const pace = metric(uk.result.pace);
    expect(pace).toHaveTextContent(paceOf(3911, 2162));
    expect(newValueOf(pace)).toContain(paceOf(6537.4, 2162));
  });

  it('divides the metres by the moving seconds itself, because the answer carries no pace', () => {
    // AD-14: there is no pace field to read, so this is the one computation the screen owns.
    const report = aReport();
    expect('pace' in report.after).toBe(false);
    show(report);

    expect(paceOf(6537.4, 2162)).toBe(`5:31 ${uk.units.perKilometre}`);
    expect(metric(uk.result.pace)).toHaveTextContent(`5:31 ${uk.units.perKilometre}`);
    expect(metric(uk.result.pace)).toHaveTextContent(`9:13 ${uk.units.perKilometre}`);
  });

  it('uses the moving seconds, not the elapsed span, so distance ÷ time is the pace beside it', () => {
    show(aReport({ after: { ...aReport().after, elapsedSeconds: 2700, pausedSeconds: 538 } }));

    expect(metric(uk.result.time)).toHaveTextContent('36:02');
    expect(metric(uk.result.time)).not.toHaveTextContent('45:00');
    expect(metric(uk.result.pace)).toHaveTextContent(paceOf(6537.4, 2162));
  });

  it('says a watch recorded no distance rather than showing zero or a pace', () => {
    show(aReport({ before: { ...aReport().before, recordedDistanceMeters: null } }));

    const distance = metric(uk.result.distance);
    expect(distance).toHaveTextContent(uk.result.noDistance);
    expect(distance).not.toHaveTextContent(`0,00 ${uk.units.kilometres}`);

    const pace = metric(uk.result.pace);
    expect(pace).toHaveTextContent(uk.result.noPace);
    expect(pace).not.toHaveTextContent(`0:00 ${uk.units.perKilometre}`);
    // The repaired side still has one: the track's distance was never in doubt.
    expect(newValueOf(pace)).toContain(paceOf(6537.4, 2162));
  });

  it('names both sides, so the arrow is not the only thing saying which is which', () => {
    show(aReport());

    const distance = metric(uk.result.distance);
    expect(distance).toHaveTextContent(`${uk.result.before}: 3,91`);
    expect(distance).toHaveTextContent(`${uk.result.after}: 6,54`);
  });
});

describe('the warnings', () => {
  it('shows each code above the numbers, in the dictionary words', () => {
    const { container } = show(
      aReport({
        warnings: [
          { code: 'GPS_STALL_DETECTED', severity: 'info' },
          { code: 'TRACK_LONGER_THAN_ACTIVITY', severity: 'blocking' },
        ],
      }),
    );

    const callouts = screen.getByRole('list', { name: uk.result.warnings });
    expect(within(callouts).getByText(uk.codes.GPS_STALL_DETECTED)).toBeInTheDocument();
    expect(
      within(callouts).getByText(uk.codes.TRACK_LONGER_THAN_ACTIVITY),
    ).toBeInTheDocument();

    // Above the numbers (EXPERIENCE.md): the over-long track is the difference between a true
    // pace and a flattering one, so it is not read after the flattering number.
    const order = [...container.querySelectorAll('*')];
    expect(order.indexOf(callouts)).toBeLessThan(
      order.indexOf(screen.getByText(uk.result.distance)),
    );
  });

  it('distinguishes a blocking warning from an informational one in words, not only in colour', () => {
    show(
      aReport({
        warnings: [
          { code: 'GPS_STALL_DETECTED', severity: 'info' },
          { code: 'TRACK_FAR_FROM_ORIGINAL', severity: 'blocking' },
        ],
      }),
    );

    expect(screen.getByText(uk.result.info)).toBeInTheDocument();
    expect(screen.getByText(uk.result.blocking)).toBeInTheDocument();
  });

  it('reads generically when the dictionary lacks the code, saying the severity all the same', () => {
    const { container } = show(
      aReport({ warnings: [{ code: 'A_CODE_FROM_THE_FUTURE', severity: 'blocking' }] }),
    );

    expect(screen.getByText(uk.errors.unexpected)).toBeInTheDocument();
    expect(screen.queryByText('A_CODE_FROM_THE_FUTURE')).not.toBeInTheDocument();
    // The severity is the answer's, not the dictionary's, so an unknown code does not downgrade
    // it — and the code still reaches whoever gets the report (Story 2.7).
    expect(screen.getByText(uk.result.blocking)).toBeInTheDocument();
    expect(container.querySelector('[data-code="A_CODE_FROM_THE_FUTURE"]')).not.toBeNull();
  });

  it('leaves no code in the DOM for a warning it has the words for', () => {
    const { container } = show(
      aReport({ warnings: [{ code: 'POSITIONS_DISCARDED', severity: 'info' }] }),
    );

    expect(screen.getByText(uk.codes.POSITIONS_DISCARDED)).toBeInTheDocument();
    expect(screen.getByText(uk.result.info)).toBeInTheDocument();
    expect(container.querySelector('[data-code]')).toBeNull();
  });

  it('shows no callout at all when the repair had nothing to warn about', () => {
    show(aReport());

    expect(screen.queryByRole('list', { name: uk.result.warnings })).not.toBeInTheDocument();
  });

  it('does not withhold the file behind another blocking warning', () => {
    // AD-22 gates the download on a blocking severity, and the over-long track's confirmation is
    // Story 1.8's: its screen does not exist, so a gate here would withhold the file behind a
    // question nobody can answer. Only the fragment is gated in this story.
    show(aReport({ warnings: [{ code: 'TRACK_LONGER_THAN_ACTIVITY', severity: 'blocking' }] }));

    expect(screen.getByRole('button', { name: uk.result.download })).toBeEnabled();
    expect(
      screen.queryByRole('button', { name: uk.result.confirmFragment }),
    ).not.toBeInTheDocument();
  });
});

describe('the fragment gate', () => {
  it('withholds the file and states the stretch that was redrawn', () => {
    show(aReport(aFragment));

    // The words of the warning, the seconds and the metres it is about — and no file.
    expect(screen.getByText(uk.codes.TRACK_IS_A_FRAGMENT)).toBeInTheDocument();
    expect(screen.getByText(uk.result.redrawn)).toBeInTheDocument();
    expect(screen.getByText('26:52–32:56')).toBeInTheDocument();
    expect(screen.getByText(`1,23 ${uk.units.kilometres}`)).toBeInTheDocument();
    expect(screen.getByText(uk.result.withheld)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: uk.result.download })).not.toBeInTheDocument();

    // The numbers are still shown: the question is which stretch, not whether to look.
    expect(metric(uk.result.distance)).toBeInTheDocument();
  });

  it('repeats the repair when the stretch is confirmed', async () => {
    const confirm = vi.fn(async () => ({ ok: true }));
    show(aReport(aFragment), () => {}, confirm);

    await userEvent.click(screen.getByRole('button', { name: uk.result.confirmFragment }));

    expect(confirm).toHaveBeenCalledTimes(1);
  });

  it('offers the other answer too, and repeats the repair as a whole route', async () => {
    // A lap run twice looks like a stretch of itself, so the detector can be wrong and the
    // athlete is the one who knows. A question with only a yes is not a question (Story 5.2).
    const whole = vi.fn(async () => ({ ok: true }));
    show(aReport(aFragment), () => {}, async () => ({ ok: true }), whole);

    await userEvent.click(screen.getByRole('button', { name: uk.result.rejectFragment }));

    expect(whole).toHaveBeenCalledTimes(1);
  });

  it('cannot be answered both ways at once, and says which answer is out', async () => {
    const whole = vi.fn(() => new Promise<CoverageAnswer>(() => {}));
    const confirm = vi.fn(async () => ({ ok: true }));
    show(aReport(aFragment), () => {}, confirm, whole);

    await userEvent.click(screen.getByRole('button', { name: uk.result.rejectFragment }));

    // The busy label belongs to the button that was pressed: a screen saying it is confirming
    // the stretch while the opposite answer is in flight states the opposite of what was asked.
    const working = screen.getByRole('button', { name: uk.result.confirming });
    expect(working).toBeDisabled();
    expect(working).toHaveAttribute('aria-busy', 'true');
    expect(screen.getByRole('button', { name: uk.result.confirmFragment })).toBeDisabled();
    expect(confirm).not.toHaveBeenCalled();
  });

  it('offers the file as soon as the confirmed answer arrives', () => {
    // What the page renders once the repeated request has come back: the same repair without the
    // warning, so the gate is gone with it and the span was never a flag of its own.
    show(aReport({ redrawn: aFragment.redrawn, warnings: [] }));

    expect(screen.getByRole('button', { name: uk.result.download })).toBeEnabled();
    expect(
      screen.queryByRole('button', { name: uk.result.confirmFragment }),
    ).not.toBeInTheDocument();
    expect(screen.queryByText(uk.result.withheld)).not.toBeInTheDocument();
  });

  it('asks once however often the confirmation is pressed', async () => {
    const confirm = vi.fn(() => new Promise<CoverageAnswer>(() => {}));
    show(aReport(aFragment), () => {}, confirm);

    const button = screen.getByRole('button', { name: uk.result.confirmFragment });
    await userEvent.click(button);
    // The button says it is working and cannot be pressed again, which is what makes one press
    // one repair of a two-hour activity.
    const working = screen.getByRole('button', { name: uk.result.confirming });
    expect(working).toBeDisabled();
    expect(working).toHaveAttribute('aria-busy', 'true');

    expect(confirm).toHaveBeenCalledTimes(1);
  });

  it('says a failed confirmation and leaves the result standing', async () => {
    const confirm = vi.fn(async () => ({ ok: false }));
    show(aReport(aFragment), () => {}, confirm);

    await userEvent.click(screen.getByRole('button', { name: uk.result.confirmFragment }));

    expect(await screen.findByText(uk.result.confirmFailed)).toBeInTheDocument();
    // The numbers and the gate are both where they were: nothing was answered, so nothing changed.
    expect(metric(uk.result.distance)).toHaveTextContent(`6,54 ${uk.units.kilometres}`);
    expect(screen.getByRole('button', { name: uk.result.confirmFragment })).toBeEnabled();
    expect(screen.queryByRole('button', { name: uk.result.download })).not.toBeInTheDocument();
  });

  it("states the service's own sentence when the repeat was refused", async () => {
    // Telling somebody to try again is wrong advice for a 400: the same two files meet the same
    // code. The dictionary's text for it is what the page says instead (Story 2.7).
    const confirm = vi.fn(async () => ({ ok: false, code: 'TRACK_ATTACHES_NOWHERE' }));
    show(aReport(aFragment), () => {}, confirm);

    await userEvent.click(screen.getByRole('button', { name: uk.result.confirmFragment }));

    expect(await screen.findByText(uk.codes.TRACK_ATTACHES_NOWHERE)).toBeInTheDocument();
    expect(screen.queryByText(uk.result.confirmFailed)).not.toBeInTheDocument();
  });

  it('asks the question even when the answer named no stretch', async () => {
    // A warning without a span is not something the core produces, and the gate must not open on
    // one: the file would be handed over because the sentence beside it could not be written.
    show(aReport({ warnings: aFragment.warnings }));

    expect(screen.getByText(uk.codes.TRACK_IS_A_FRAGMENT)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: uk.result.confirmFragment })).toBeEnabled();
    expect(screen.queryByRole('button', { name: uk.result.download })).not.toBeInTheDocument();
  });
});

describe('the download', () => {
  const created = vi.fn<(object: Blob | MediaSource) => string>(() => 'blob:fitfix/1');
  const revoked = vi.fn<(url: string) => void>();
  let clicked: HTMLAnchorElement[] = [];

  // jsdom implements neither of these, so they are installed rather than spied on, and put back
  // afterwards in case a later test ever needs the real pair.
  const realCreate = URL.createObjectURL;
  const realRevoke = URL.revokeObjectURL;

  beforeEach(() => {
    clicked = [];
    URL.createObjectURL = created;
    URL.revokeObjectURL = revoked;
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (
      this: HTMLAnchorElement,
    ) {
      clicked.push(this);
    });
  });

  afterEach(() => {
    URL.createObjectURL = realCreate;
    URL.revokeObjectURL = realRevoke;
    created.mockClear();
    revoked.mockClear();
    vi.restoreAllMocks();
  });

  it('saves the original name with _fixed.fit, holding the decoded bytes', async () => {
    show(aReport());

    await userEvent.click(screen.getByRole('button', { name: uk.result.download }));

    expect(clicked).toHaveLength(1);
    expect(clicked[0]!.download).toBe('Run_4k_Chabany_fixed.fit');
    expect(clicked[0]!.href).toContain('blob:fitfix/1');

    const [blob] = created.mock.calls[0] as [Blob];
    const bytes = await bytesOf(blob);
    // `RklUAQID` decoded: the FIT magic and three bytes of nothing in particular.
    expect([...bytes]).toEqual([0x46, 0x49, 0x54, 0x01, 0x02, 0x03]);
  });

  it('releases the object URL it created', async () => {
    show(aReport());

    await userEvent.click(screen.getByRole('button', { name: uk.result.download }));

    // Released after the browser has taken the blob, not inside the click handler.
    await waitFor(() => expect(revoked).toHaveBeenCalledWith('blob:fitfix/1'));
  });
});

describe('the Strava row', () => {
  it('names the upload that is coming, and cannot be pressed', () => {
    // EXPERIENCE.md's Result state. The upload is its own epic, and the label is where that is
    // said: a disabled control cannot explain itself in a tooltip.
    show(aReport());

    const strava = screen.getByRole('button', { name: uk.result.strava });
    expect(strava).toBeInTheDocument();
    expect(strava).toBeDisabled();

    // It is not the download under another name: that one still works beside it.
    expect(screen.getByRole('button', { name: uk.result.download })).toBeEnabled();
  });
});

describe('starting again', () => {
  it('asks the page to start again rather than reloading it', async () => {
    const again = vi.fn();
    show(aReport(), again);

    await userEvent.click(screen.getByRole('button', { name: uk.result.startAgain }));

    expect(again).toHaveBeenCalledTimes(1);
  });
});
