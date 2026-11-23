import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { App } from './App';
import { uk } from './i18n/uk';

/** The bytes are never read here: the stubbed service decides what each file turns out to be. */
function fitFile(name: string) {
  return new File([new Uint8Array([0x0e, 0x10, 0x43, 0x5a])], name, {
    type: 'application/octet-stream',
  });
}

/** One answer. The client reads `ok`, `status` and `json()`, and nothing else has to exist. */
function answers(payload: unknown, status = 200) {
  return { ok: status >= 200 && status < 300, status, json: async () => payload } as Response;
}

const anOriginal = {
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

const aTrack = {
  role: 'track',
  lengthMeters: 6537.4,
  pointCount: 1248,
  format: 'gpx',
  hasAltitude: true,
};

/** The 19.11.2026 case, trimmed to what this screen reads. `Result.test.tsx` has the full one. */
const aReport = {
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
  zones: [{ kind: 'measured', fromSeconds: 0, toSeconds: 2162 }],
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
  warnings: [],
  fitBase64: 'RklUAQID',
};

/**
 * The service, stubbed: each zone asks `/api/inspect` about its own file (Story 2.5) and the
 * button asks `/api/repair` for both (Story 2.6), so the page cannot be driven without answers.
 * The role is read back out of the inspect request, which is what makes one stub serve both
 * zones. `Dropzone.test.tsx` and `Result.test.tsx` cover the states themselves; what is checked
 * here is the one place the two zones and the repair meet.
 *
 * @param repair What the repair answers, or nothing when a test must not reach it at all.
 */
function serviceDescribesEverything(
  repair?: (request: RequestInit) => Response | Promise<Response>,
) {
  const fetched = vi.fn(async (url: string, request: RequestInit) => {
    if (url === '/api/repair') {
      if (repair === undefined) throw new Error(`No repair was expected: ${url}`);
      return repair(request);
    }
    const role = (request.body as FormData).get('role');
    return answers(role === 'original' ? anOriginal : aTrack);
  });
  vi.stubGlobal('fetch', fetched);
  return fetched;
}

/** How many times the repair was asked for, whatever else the page asked about. */
function repairCalls(fetched: ReturnType<typeof serviceDescribesEverything>) {
  return fetched.mock.calls.filter(([url]) => url === '/api/repair').length;
}

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

/**
 * Picks a file through the hidden input of one zone. The zone's own button opens the native
 * picker, which jsdom cannot show, so the test does what the picker would do.
 */
async function choose(zoneLabel: string, file: File) {
  const zone = screen.getByRole('button', { name: zoneLabel }).parentElement;
  expect(zone).not.toBeNull();
  const input = zone!.querySelector('input[type="file"]');
  expect(input).not.toBeNull();
  await userEvent.upload(input as HTMLInputElement, file);
}

describe('the empty state', () => {
  it('shows the heading, both zones and the three steps', () => {
    render(<App />);

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(uk.hero.title);
    expect(screen.getByRole('button', { name: uk.dropzones.original.pick })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: uk.dropzones.track.pick })).toBeInTheDocument();

    expect(screen.getByRole('heading', { name: uk.howItWorks.title })).toBeInTheDocument();
    expect(screen.getAllByRole('listitem')).toHaveLength(uk.howItWorks.steps.length);
    for (const step of uk.howItWorks.steps) {
      expect(screen.getByRole('heading', { name: step.title })).toBeInTheDocument();
      expect(screen.getByText(step.body)).toBeInTheDocument();
    }
  });

  it('names what each zone wants, so nobody has to be told which file is which', () => {
    render(<App />);

    expect(screen.getByText(uk.dropzones.original.title)).toBeInTheDocument();
    expect(screen.getByText(uk.dropzones.original.role)).toBeInTheDocument();
    expect(screen.getByText(uk.dropzones.track.title)).toBeInTheDocument();
    expect(screen.getByText(uk.dropzones.track.role)).toBeInTheDocument();
  });
});

describe('narrow screens', () => {
  it('stacks every multi-column block below the 768 px breakpoint', () => {
    const { container } = render(<App />);

    // jsdom applies no Tailwind, so what can be checked here is the contract: a block starts
    // at one column and only widens at `md:` (= 768 px, DESIGN.md). The rendered result at
    // 375 px is checked by eye, not here.
    const grids = [...container.querySelectorAll('[class*="grid-cols"]')];
    expect(grids.length).toBeGreaterThan(0);
    for (const grid of grids) {
      expect(grid.className).toContain('grid-cols-1');
      expect(grid.className).toMatch(/\bmd:grid-cols-\d/);
    }
  });
});

describe('the repair button', () => {
  const repair = () => screen.getByRole('button', { name: uk.repair.action });

  it('waits for both files to be described, not merely chosen', async () => {
    serviceDescribesEverything();
    render(<App />);

    expect(repair()).toBeDisabled();
    expect(screen.getByText(uk.repair.waitingForFiles)).toBeInTheDocument();

    await choose(uk.dropzones.original.pick, fitFile('Run_4k.fit'));
    await screen.findByText(uk.inspection.recordedDistance);
    expect(repair()).toBeDisabled();

    await choose(uk.dropzones.track.pick, fitFile('route.gpx'));
    await screen.findByText(uk.inspection.length);
    expect(repair()).toBeEnabled();
    expect(screen.queryByText(uk.repair.waitingForFiles)).not.toBeInTheDocument();
  });

  it('stays disabled while a zone is still being described', async () => {
    // The original's answer never comes: a zone that cannot say what it holds is not chosen.
    vi.stubGlobal('fetch', vi.fn().mockReturnValue(new Promise<Response>(() => {})));
    render(<App />);

    await choose(uk.dropzones.original.pick, fitFile('Run_4k.fit'));
    await choose(uk.dropzones.track.pick, fitFile('route.gpx'));

    expect(screen.getAllByText(uk.inspection.reading)).toHaveLength(2);
    expect(repair()).toBeDisabled();
  });

  it('counts a refused file as no file at all', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (_url: string, request: RequestInit) => {
        const role = (request.body as FormData).get('role');
        return role === 'track'
          ? answers({ code: 'TRACK_FORMAT_UNKNOWN' }, 400)
          : answers(anOriginal);
      }),
    );
    render(<App />);

    await choose(uk.dropzones.original.pick, fitFile('Run_4k.fit'));
    await choose(uk.dropzones.track.pick, fitFile('notes.txt'));

    await screen.findByText(uk.codes.TRACK_FORMAT_UNKNOWN);
    expect(repair()).toBeDisabled();
    expect(screen.getByText(uk.repair.waitingForFiles)).toBeInTheDocument();
  });

  it('goes back to waiting when a zone is emptied', async () => {
    serviceDescribesEverything();
    render(<App />);
    await choose(uk.dropzones.original.pick, fitFile('Run_4k.fit'));
    await choose(uk.dropzones.track.pick, fitFile('route.gpx'));
    await screen.findByText(uk.inspection.length);
    expect(repair()).toBeEnabled();

    const [clear] = screen.getAllByRole('button', { name: uk.dropzones.clear });
    await userEvent.click(clear as HTMLElement);

    expect(repair()).toBeDisabled();
  });

  it('shows the chosen file in its own zone and nowhere else', async () => {
    serviceDescribesEverything();
    render(<App />);
    await choose(uk.dropzones.original.pick, fitFile('Run_4k_Chabany.fit'));
    await screen.findByText(uk.inspection.recordedDistance);

    expect(screen.getByText('Run_4k_Chabany.fit')).toBeInTheDocument();
    // The track zone still asks for a file: one choice never fills both.
    expect(screen.getAllByText(/Перетягни файл/)).toHaveLength(1);
    expect(screen.getAllByRole('button', { name: uk.dropzones.clear })).toHaveLength(1);
    expect(screen.queryByText(uk.inspection.length)).not.toBeInTheDocument();
  });
});

describe('pressing the button', () => {
  const repair = () => screen.getByRole('button', { name: uk.repair.action });

  /** Both zones filled and described, which is the state the button waits for. */
  async function withBothFiles(fetched: ReturnType<typeof serviceDescribesEverything>) {
    render(<App />);
    await choose(uk.dropzones.original.pick, fitFile('Run_4k_Chabany.fit'));
    await choose(uk.dropzones.track.pick, fitFile('route.gpx'));
    await screen.findByText(uk.inspection.length);
    expect(repair()).toBeEnabled();
    return fetched;
  }

  it('shows that it is working, then the result', async () => {
    let answerRepair: (answer: Response) => void = () => {};
    const fetched = serviceDescribesEverything(
      () =>
        new Promise<Response>((resolve) => {
          answerRepair = resolve;
        }),
    );
    await withBothFiles(fetched);

    await userEvent.click(repair());

    const working = screen.getByRole('button', { name: uk.repair.working });
    expect(working).toBeDisabled();
    expect(working).toHaveAttribute('aria-busy', 'true');
    expect(screen.queryByRole('heading', { name: uk.result.title })).not.toBeInTheDocument();

    answerRepair(answers(aReport));

    expect(await screen.findByRole('heading', { name: uk.result.title })).toBeInTheDocument();
    expect(screen.getByText(uk.result.distance)).toBeInTheDocument();
    expect(screen.getByText(uk.result.time)).toBeInTheDocument();
    expect(screen.getByText(uk.result.pace)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: uk.result.download })).toBeInTheDocument();

    // The two files went in one request, each named for the role it was offered in (AD-15).
    const repairs = fetched.mock.calls.filter(([url]) => url === '/api/repair');
    expect(repairs).toHaveLength(1);
    const [, request] = repairs[0] as [string, RequestInit];
    expect(request.method).toBe('POST');
    const body = request.body as FormData;
    expect((body.get('original') as File).name).toBe('Run_4k_Chabany.fit');
    expect((body.get('track') as File).name).toBe('route.gpx');
    // No track-length mode: the conservative length is the only one nobody has to confirm (AD-17).
    expect(body.get('trackLengthMode')).toBeNull();
  });

  it('asks once however often the button is pressed', async () => {
    const fetched = serviceDescribesEverything(() => new Promise<Response>(() => {}));
    await withBothFiles(fetched);

    const button = repair();
    await userEvent.click(button);
    await userEvent.click(button);
    await userEvent.click(button);

    expect(repairCalls(fetched)).toBe(1);
  });

  it('states a refusal in the dictionary words and stays where the files are', async () => {
    // A code `FailureStatus` really answers 400 with: a fault in the files, not in the repair.
    const fetched = serviceDescribesEverything(() =>
      answers({ code: 'ACTIVITY_TOO_MANY_LAPS' }, 400),
    );
    await withBothFiles(fetched);

    await userEvent.click(repair());

    expect(await screen.findByText(uk.codes.ACTIVITY_TOO_MANY_LAPS)).toBeInTheDocument();
    // Both files are still there, and the button can be pressed again — the answer to a refusal
    // is one more press, not two more drops.
    expect(screen.queryByRole('heading', { name: uk.result.title })).not.toBeInTheDocument();
    expect(screen.getByText(uk.dropzones.original.title)).toBeInTheDocument();
    expect(repair()).toBeEnabled();
  });

  it('forgets a refusal as soon as one of the files changes', async () => {
    const fetched = serviceDescribesEverything(() => answers({ code: 'TRACK_FAR_FROM_ORIGINAL' }, 400));
    await withBothFiles(fetched);
    await userEvent.click(repair());
    await screen.findByText(uk.codes.TRACK_FAR_FROM_ORIGINAL);

    await choose(uk.dropzones.track.pick, fitFile('another.gpx'));

    expect(screen.queryByText(uk.codes.TRACK_FAR_FROM_ORIGINAL)).not.toBeInTheDocument();
  });

  it('says the service could not be reached, rather than blaming a file', async () => {
    const fetched = serviceDescribesEverything(() => {
      throw new TypeError('Failed to fetch');
    });
    await withBothFiles(fetched);

    await userEvent.click(repair());

    expect(await screen.findByText(uk.repair.unreachable)).toBeInTheDocument();
    for (const code of Object.values(uk.codes)) {
      expect(screen.queryByText(code)).not.toBeInTheDocument();
    }
  });

  it('states a code the dictionary lacks in the generic words, keeping the code in the DOM', async () => {
    const fetched = serviceDescribesEverything(() => answers({ code: 'WHAT_IS_THIS' }, 400));
    const { container } = render(<App />);
    await choose(uk.dropzones.original.pick, fitFile('Run_4k_Chabany.fit'));
    await choose(uk.dropzones.track.pick, fitFile('route.gpx'));
    await screen.findByText(uk.inspection.length);

    await userEvent.click(repair());

    expect(await screen.findByText(uk.errors.unexpected)).toBeInTheDocument();
    expect(screen.queryByText('WHAT_IS_THIS')).not.toBeInTheDocument();
    expect(container.querySelector('[data-code="WHAT_IS_THIS"]')).not.toBeNull();
    expect(repairCalls(fetched)).toBe(1);
  });

  it('offers no attempt after a 400: the same two files would be refused again', async () => {
    // `TRACK_TOO_FEW_POINTS` is a 400 in `FailureStatus`, which is what makes this test about the
    // status and not about the component's mood. `DISTANCE_SERIES_IMPLAUSIBLE` would not do: the
    // API serves that one at 500, so a fixture pairing it with 400 would pass for the wrong reason.
    const fetched = serviceDescribesEverything(() => answers({ code: 'TRACK_TOO_FEW_POINTS' }, 400));
    await withBothFiles(fetched);

    await userEvent.click(repair());
    await screen.findByText(uk.codes.TRACK_TOO_FEW_POINTS);

    expect(screen.queryByRole('button', { name: uk.errors.retry })).not.toBeInTheDocument();
    expect(repairCalls(fetched)).toBe(1);
  });

  it('offers the attempt when the repair failed on our side, and pressing it repairs', async () => {
    // The case the story exists for: `FailureStatus` keeps `DISTANCE_SERIES_IMPLAUSIBLE` and the
    // other broken invariants at 500 with their own code, so the person is told what happened in
    // the dictionary's words and handed the one thing they can do about it (EXPERIENCE.md).
    let asks = 0;
    const fetched = serviceDescribesEverything(() => {
      asks += 1;
      return asks === 1 ? answers({ code: 'SUMMARY_SERIES_MISMATCH' }, 500) : answers(aReport);
    });
    await withBothFiles(fetched);

    await userEvent.click(repair());

    expect(await screen.findByText(uk.codes.SUMMARY_SERIES_MISMATCH)).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: uk.errors.retry }));

    expect(await screen.findByRole('heading', { name: uk.result.title })).toBeInTheDocument();
    expect(repairCalls(fetched)).toBe(2);
    const repairs = fetched.mock.calls.filter(([url]) => url === '/api/repair');
    const body = (repairs[1] as [string, RequestInit])[1].body as FormData;
    expect((body.get('original') as File).name).toBe('Run_4k_Chabany.fit');
    expect((body.get('track') as File).name).toBe('route.gpx');
  });

  it('repeats the request when the attempt beside an unreachable repair is pressed', async () => {
    // The first ask never arrives; the second answers. Both files are still in their zones, so
    // the whole fix is one press.
    let asks = 0;
    const fetched = serviceDescribesEverything(() => {
      asks += 1;
      if (asks === 1) throw new TypeError('Failed to fetch');
      return answers(aReport);
    });
    await withBothFiles(fetched);

    await userEvent.click(repair());
    await screen.findByText(uk.repair.unreachable);

    await userEvent.click(screen.getByRole('button', { name: uk.errors.retry }));

    expect(await screen.findByRole('heading', { name: uk.result.title })).toBeInTheDocument();
    expect(repairCalls(fetched)).toBe(2);

    // Both files went again, each named for its role (AD-15): a retry is the same request.
    const repairs = fetched.mock.calls.filter(([url]) => url === '/api/repair');
    const [, second] = repairs[1] as [string, RequestInit];
    const body = second.body as FormData;
    expect((body.get('original') as File).name).toBe('Run_4k_Chabany.fit');
    expect((body.get('track') as File).name).toBe('route.gpx');
  });

  it('leaves nothing to press twice while the repeated request is in flight', async () => {
    let asks = 0;
    const fetched = serviceDescribesEverything(() => {
      asks += 1;
      if (asks === 1) throw new TypeError('Failed to fetch');
      // The second ask never answers: the page has to be holding the working state, not the
      // failure, for as long as it is out.
      return new Promise<Response>(() => {});
    });
    await withBothFiles(fetched);

    await userEvent.click(repair());
    await userEvent.click(await screen.findByRole('button', { name: uk.errors.retry }));

    // The attempt goes with the sentence it belonged to, and the button says it is working —
    // which, with the guard the main button already has, is why one press is one request.
    expect(screen.queryByRole('button', { name: uk.errors.retry })).not.toBeInTheDocument();
    expect(screen.queryByText(uk.repair.unreachable)).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: uk.repair.working })).toBeDisabled();
    expect(repairCalls(fetched)).toBe(2);
  });
});

describe('starting again', () => {
  it('returns to two empty zones with no result and the button disabled', async () => {
    const fetched = serviceDescribesEverything(() => answers(aReport));
    render(<App />);
    await choose(uk.dropzones.original.pick, fitFile('Run_4k_Chabany.fit'));
    await choose(uk.dropzones.track.pick, fitFile('route.gpx'));
    await screen.findByText(uk.inspection.length);
    await userEvent.click(screen.getByRole('button', { name: uk.repair.action }));
    await screen.findByRole('heading', { name: uk.result.title });

    await userEvent.click(screen.getByRole('button', { name: uk.result.startAgain }));

    // The same page, not a reload: the empty state, with nothing left of either file.
    expect(screen.getByRole('heading', { name: uk.hero.title })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: uk.repair.action })).toBeDisabled();
    expect(screen.getByText(uk.repair.waitingForFiles)).toBeInTheDocument();
    expect(screen.getAllByText(/Перетягни файл/)).toHaveLength(2);
    expect(screen.queryByRole('button', { name: uk.dropzones.clear })).not.toBeInTheDocument();
    expect(screen.queryByText('Run_4k_Chabany.fit')).not.toBeInTheDocument();

    // Nothing was asked of the service by starting again: a repair is one request (AD-3).
    await waitFor(() => expect(repairCalls(fetched)).toBe(1));
  });
});
