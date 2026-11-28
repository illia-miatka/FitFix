import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { App } from './App';
import { uk } from './i18n/uk';
import {
  aConfirmedFragment,
  aFragment,
  aFarTrack,
  aReport,
  anOriginal,
  aRoute,
  aTrack,
  answers,
  fitFile,
} from './test/fixtures';

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

    answerRepair(answers(aReport()));

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
    const fetched = serviceDescribesEverything(() =>
      answers({ code: 'TRACK_FAR_FROM_ORIGINAL' }, 400),
    );
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
    const fetched = serviceDescribesEverything(() =>
      answers({ code: 'TRACK_TOO_FEW_POINTS' }, 400),
    );
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
      return asks === 1 ? answers({ code: 'SUMMARY_SERIES_MISMATCH' }, 500) : answers(aReport());
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
      return answers(aReport());
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

describe('confirming a fragment', () => {
  const repair = () => screen.getByRole('button', { name: uk.repair.action });

  async function withBothFiles() {
    render(<App />);
    await choose(uk.dropzones.original.pick, fitFile('Run_4k_Chabany.fit'));
    await choose(uk.dropzones.track.pick, fitFile('route.gpx'));
    await screen.findByText(uk.inspection.length);
    expect(repair()).toBeEnabled();
  }

  /**
   * The service that calls the pair a stretch of the route once, and then accepts the answer.
   * What the answer has to be is asserted inside the stub: the first ask names no coverage at
   * all, so the service decides and asks, and the second carries exactly what the pressed button
   * means (AD-27).
   */
  function asksOnceThenAccepts(expected: 'fragment' | 'whole') {
    let asks = 0;
    return serviceDescribesEverything((request) => {
      asks += 1;
      const coverage = (request.body as FormData).get('trackCoverage');
      expect(coverage).toBe(asks === 1 ? null : expected);
      // Confirming the stretch answers the question and keeps the span; calling the track the
      // whole route leaves no stretch at all.
      const accepted = expected === 'fragment' ? aReport(aConfirmedFragment) : aReport();
      return answers(asks === 1 ? aReport(aFragment) : accepted);
    });
  }

  /** Both files chosen and described, repaired once, and the result screen up at the gate. */
  async function atTheGate() {
    await withBothFiles();
    await userEvent.click(repair());
    await screen.findByRole('heading', { name: uk.result.title });
  }

  it('repeats the two files with the coverage confirmed, and then offers the file', async () => {
    const fetched = asksOnceThenAccepts('fragment');
    await atTheGate();

    // The gate: the stretch, no file, and one thing to press.
    expect(screen.getByText(uk.codes.TRACK_IS_A_FRAGMENT)).toBeInTheDocument();
    expect(screen.getByText('26:52–32:56')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: uk.result.download })).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: uk.result.confirmFragment }));

    expect(await screen.findByRole('button', { name: uk.result.download })).toBeInTheDocument();
    expect(screen.queryByText(uk.codes.TRACK_IS_A_FRAGMENT)).not.toBeInTheDocument();
    expect(repairCalls(fetched)).toBe(2);

    // The question is answered and the stretch is still on the screen that offers the file
    // (Story 5.3), together with what was left alone.
    expect(screen.getByText('26:52–32:56')).toBeInTheDocument();
    expect(screen.getByText(uk.result.keptOwn)).toBeInTheDocument();

    // Both files went again, each named for its role (AD-15): nothing is stored, so confirming is
    // the same request with one field more (AD-3).
    const repairs = fetched.mock.calls.filter(([url]) => url === '/api/repair');
    const body = (repairs[1] as [string, RequestInit])[1].body as FormData;
    expect((body.get('original') as File).name).toBe('Run_4k_Chabany.fit');
    expect((body.get('track') as File).name).toBe('route.gpx');
  });

  it('repeats the two files as a whole route when the detector was wrong', async () => {
    // The other answer to the same question. A lap run twice looks like a stretch of itself, and
    // only the athlete knows; the request goes again saying so, and the repair leaves the
    // fragment mechanism altogether (AD-27).
    const fetched = asksOnceThenAccepts('whole');
    await atTheGate();

    await userEvent.click(screen.getByRole('button', { name: uk.result.rejectFragment }));

    expect(await screen.findByRole('button', { name: uk.result.download })).toBeInTheDocument();
    expect(screen.queryByText(uk.codes.TRACK_IS_A_FRAGMENT)).not.toBeInTheDocument();
    expect(screen.queryByText(uk.result.redrawn)).not.toBeInTheDocument();
    expect(repairCalls(fetched)).toBe(2);
  });

  it('keeps the gate and says so when the repeated request never arrives', async () => {
    let asks = 0;
    const fetched = serviceDescribesEverything(() => {
      asks += 1;
      if (asks === 1) return answers(aReport(aFragment));
      throw new TypeError('Failed to fetch');
    });
    await withBothFiles();

    await userEvent.click(repair());
    await screen.findByRole('heading', { name: uk.result.title });
    await userEvent.click(screen.getByRole('button', { name: uk.result.confirmFragment }));

    expect(await screen.findByText(uk.result.confirmFailed)).toBeInTheDocument();
    // The result of the first answer is untouched, and the file is still withheld: nothing was
    // confirmed, so nothing may be taken away.
    expect(screen.getByText(uk.codes.TRACK_IS_A_FRAGMENT)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: uk.result.download })).not.toBeInTheDocument();
    expect(repairCalls(fetched)).toBe(2);
  });
});

describe('two questions at once', () => {
  const repair = () => screen.getByRole('button', { name: uk.repair.action });
  const confirmFragment = () => screen.getByRole('button', { name: uk.result.confirmFragment });
  const trustTrack = () => screen.getByRole('button', { name: uk.result.longTrack.trustTrack });

  /** A repair that is both an over-long track and a fragment, so both questions are on screen. */
  const longTrack = { code: 'TRACK_LONGER_THAN_ACTIVITY', severity: 'blocking' } as const;
  const fragment = { code: 'TRACK_IS_A_FRAGMENT', severity: 'blocking' } as const;
  const both = aReport({
    ...aFragment,
    track: { lengthMeters: 7400, usedLengthMeters: 6537.4 },
    warnings: [longTrack, fragment],
  });

  /** The first repair answers at once; the next one waits until the test lets it go. */
  async function withTheSecondRepairHeld() {
    let release: (answer: Response) => void = () => {};
    const held = new Promise<Response>((resolve) => (release = resolve));
    let asked = 0;
    const fetched = serviceDescribesEverything(() => (++asked === 1 ? answers(both) : held));

    render(<App />);
    await choose(uk.dropzones.original.pick, fitFile('Run_4k_Chabany.fit'));
    await choose(uk.dropzones.track.pick, fitFile('route.gpx'));
    await screen.findByText(uk.inspection.length);
    await userEvent.click(repair());
    await screen.findByRole('heading', { name: uk.result.title });
    return { fetched, release };
  }

  it('refuses a length answer while the fragment answer is out, and sends nothing', async () => {
    const { fetched, release } = await withTheSecondRepairHeld();
    await userEvent.click(confirmFragment());
    expect(repairCalls(fetched)).toBe(2);

    await userEvent.click(trustTrack());

    // One repair at a time: the second answer is turned away with the gate's own sentence, and
    // the service is not asked a third time (the page's `running` guard).
    expect(await screen.findByText(uk.result.longTrack.failed)).toBeInTheDocument();
    expect(repairCalls(fetched)).toBe(2);

    release(answers(aReport({ ...both, warnings: [longTrack] })));
    await waitFor(() => expect(screen.queryByText(uk.result.confirmFragment)).toBeNull());
    expect(repairCalls(fetched)).toBe(2);
  });

  it('refuses a fragment answer while the length answer is out, and sends nothing', async () => {
    const { fetched, release } = await withTheSecondRepairHeld();
    await userEvent.click(trustTrack());
    expect(repairCalls(fetched)).toBe(2);

    await userEvent.click(confirmFragment());

    expect(await screen.findByText(uk.result.confirmFailed)).toBeInTheDocument();
    expect(repairCalls(fetched)).toBe(2);

    release(answers(both));
    await waitFor(() => expect(trustTrack()).toHaveAttribute('aria-pressed', 'true'));
    expect(repairCalls(fetched)).toBe(2);
  });
});

describe('confirming a track far from the start', () => {
  const repair = () => screen.getByRole('button', { name: uk.repair.action });
  const download = () => screen.queryByRole('button', { name: uk.result.download });

  async function repairAPair() {
    await choose(uk.dropzones.original.pick, fitFile('Run_4k_Chabany.fit'));
    await choose(uk.dropzones.track.pick, fitFile('route.gpx'));
    await screen.findByText(uk.inspection.length);
    await userEvent.click(repair());
    await screen.findByRole('heading', { name: uk.result.title });
  }

  it('asks the service nothing, and asks again for the next pair', async () => {
    const fetched = serviceDescribesEverything(() => answers(aReport(aFarTrack)));
    render(<App />);
    await repairAPair();
    expect(download()).not.toBeInTheDocument();
    const requests = () => fetched.mock.calls.filter(([url]) => url === '/api/repair').length;

    await userEvent.click(screen.getByRole('button', { name: uk.result.farTrack.confirm }));

    expect(download()).toBeEnabled();
    // The very same bytes either way: nothing to repeat (AD-3).
    expect(requests()).toBe(1);

    await userEvent.click(screen.getByRole('button', { name: uk.result.startAgain }));
    await repairAPair();

    // A confirmation answers for one pair of files, not for the next (AD-22).
    expect(download()).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: uk.result.farTrack.confirm })).toBeEnabled();
  });
});

describe('deciding about a long track (Story 3.5)', () => {
  const repair = () => screen.getByRole('button', { name: uk.repair.action });
  const download = () => screen.queryByRole('button', { name: uk.result.download });
  const keep = () => screen.getByRole('button', { name: uk.result.longTrack.keepTrimmed });
  const trust = () => screen.getByRole('button', { name: uk.result.longTrack.trustTrack });

  /** The over-long track as the service answers it: trimmed by default, whole when trusted. */
  const trimmed = aReport({
    track: { lengthMeters: 7400, usedLengthMeters: 6537.4 },
    warnings: [{ code: 'TRACK_LONGER_THAN_ACTIVITY', severity: 'blocking' }],
  });
  const whole = aReport({
    ...trimmed,
    after: { ...trimmed.after, distanceMeters: 7400 },
    track: { lengthMeters: 7400, usedLengthMeters: 7400 },
  });

  /** What each request said, read back out of the form it was sent as. */
  function fieldsOf(fetched: ReturnType<typeof serviceDescribesEverything>, field: string) {
    return fetched.mock.calls
      .filter(([url]) => url === '/api/repair')
      .map(([, request]) => ((request as RequestInit).body as FormData).get(field));
  }

  function serviceTrimsUnlessTrusted() {
    return serviceDescribesEverything((request) => {
      const mode = (request.body as FormData).get('trackLengthMode');
      return answers(mode === 'full' ? whole : trimmed);
    });
  }

  async function atTheDecision() {
    render(<App />);
    await choose(uk.dropzones.original.pick, fitFile('Run_4k_Chabany.fit'));
    await choose(uk.dropzones.track.pick, fitFile('route.gpx'));
    await screen.findByText(uk.inspection.length);
    await userEvent.click(repair());
    await screen.findByRole('heading', { name: uk.result.title });
  }

  it('withholds the file, then repeats the repair with the full length and replaces the preview', async () => {
    const fetched = serviceTrimsUnlessTrusted();
    await atTheDecision();
    expect(download()).not.toBeInTheDocument();
    expect(fieldsOf(fetched, 'trackLengthMode')).toEqual([null]);

    await userEvent.click(trust());

    expect(await screen.findByRole('button', { name: uk.result.download })).toBeEnabled();
    expect(fieldsOf(fetched, 'trackLengthMode')).toEqual([null, 'full']);
    // The whole preview is the new answer's: the distance beside the old one, and the warning still
    // on the screen, because trusting the track does not make it plausible.
    expect(screen.getByText(uk.result.distance).parentElement).toHaveTextContent(
      `${uk.result.after}: 7,40`,
    );
    expect(screen.getByText(uk.codes.TRACK_LONGER_THAN_ACTIVITY)).toBeInTheDocument();
    expect(trust()).toHaveAttribute('aria-pressed', 'true');
  });

  it('offers the file without a second request when the trimmed length is kept', async () => {
    const fetched = serviceTrimsUnlessTrusted();
    await atTheDecision();

    await userEvent.click(keep());

    expect(await screen.findByRole('button', { name: uk.result.download })).toBeEnabled();
    expect(repairCalls(fetched)).toBe(1);
    expect(keep()).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByText(uk.codes.TRACK_LONGER_THAN_ACTIVITY)).toBeInTheDocument();
  });

  it('repeats with the plain length when the athlete changes their mind', async () => {
    const fetched = serviceTrimsUnlessTrusted();
    await atTheDecision();

    await userEvent.click(trust());
    await screen.findByRole('button', { name: uk.result.download });
    await userEvent.click(keep());

    await waitFor(() => expect(keep()).toHaveAttribute('aria-pressed', 'true'));
    expect(fieldsOf(fetched, 'trackLengthMode')).toEqual([null, 'full', 'plausible']);
  });

  it('keeps the file withheld and says so when the repeat never arrives', async () => {
    let asks = 0;
    serviceDescribesEverything(() => {
      asks += 1;
      if (asks === 1) return answers(trimmed);
      throw new TypeError('Failed to fetch');
    });
    await atTheDecision();

    await userEvent.click(trust());

    expect(await screen.findByText(uk.result.longTrack.failed)).toBeInTheDocument();
    expect(download()).not.toBeInTheDocument();
    expect(trust()).toHaveAttribute('aria-pressed', 'false');
  });

  it('carries the length answer into a later coverage answer, and the coverage into the length one', async () => {
    // Both warnings at once is not a state the core produces, but the requests have to be right
    // whichever answer comes second: nothing is stored, so each repeat restates the other.
    const both = aReport({
      ...trimmed,
      redrawn: aFragment.redrawn,
      warnings: [...trimmed.warnings, ...aFragment.warnings],
    });
    const fetched = serviceDescribesEverything(() => answers(both));
    await atTheDecision();

    await userEvent.click(trust());
    await userEvent.click(screen.getByRole('button', { name: uk.result.confirmFragment }));
    await waitFor(() => expect(repairCalls(fetched)).toBe(3));
    expect(fieldsOf(fetched, 'trackLengthMode')).toEqual([null, 'full', 'full']);
    expect(fieldsOf(fetched, 'trackCoverage')).toEqual([null, null, 'fragment']);

    await userEvent.click(keep());
    await waitFor(() => expect(repairCalls(fetched)).toBe(4));
    expect(fieldsOf(fetched, 'trackLengthMode')[3]).toBe('plausible');
    expect(fieldsOf(fetched, 'trackCoverage')[3]).toBe('fragment');
  });

  it('forgets the decision when the athlete starts again', async () => {
    const fetched = serviceTrimsUnlessTrusted();
    await atTheDecision();
    await userEvent.click(trust());
    await screen.findByRole('button', { name: uk.result.download });

    await userEvent.click(screen.getByRole('button', { name: uk.result.startAgain }));
    await choose(uk.dropzones.original.pick, fitFile('Run_4k_Chabany.fit'));
    await choose(uk.dropzones.track.pick, fitFile('route.gpx'));
    await screen.findByText(uk.inspection.length);
    await userEvent.click(repair());
    await screen.findByRole('heading', { name: uk.result.title });

    expect(fieldsOf(fetched, 'trackLengthMode')).toEqual([null, 'full', null]);
    expect(download()).not.toBeInTheDocument();
  });
});

describe('starting again', () => {
  it('returns to two empty zones with no result and the button disabled', async () => {
    const fetched = serviceDescribesEverything(() => answers(aReport()));
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

/** jsdom's `File` has no `text()`, so the content is read the way a browser did before it had one. */
function readText(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result));
    reader.onerror = () => reject(reader.error);
    reader.readAsText(file);
  });
}

describe('drawing the missing stretch (Story 5.4)', () => {
  const draw = uk.result.map.draw;
  const withMap = aReport(aRoute());

  async function atTheMap(repair: (request: RequestInit) => Response | Promise<Response>) {
    const fetched = serviceDescribesEverything(repair);
    render(<App />);
    await choose(uk.dropzones.original.pick, fitFile('Run_4k_Chabany.fit'));
    await choose(uk.dropzones.track.pick, fitFile('route.gpx'));
    await screen.findByText(uk.inspection.length);
    await userEvent.click(screen.getByRole('button', { name: uk.repair.action }));
    await screen.findByRole('heading', { name: uk.result.title });
    await userEvent.click(screen.getByRole('button', { name: draw.begin }));
    // One point at the map centre, placed the way a keyboard does it.
    fireEvent.keyDown(screen.getByRole('region', { name: uk.result.map.label }), { key: 'Enter' });
    return fetched;
  }

  it('repeats the repair with the drawn line as the track and the coverage stated, with no file chosen', async () => {
    const fetched = await atTheMap(() => answers(withMap));

    await userEvent.click(screen.getByRole('button', { name: draw.confirm }));

    // The drawing ended: the toolbar is gone and the way in is back.
    expect(await screen.findByRole('button', { name: draw.begin })).toBeInTheDocument();
    const repairs = fetched.mock.calls.filter(([url]) => url === '/api/repair');
    expect(repairs).toHaveLength(2);

    const body = (repairs[1] as [string, RequestInit])[1].body as FormData;
    expect((body.get('original') as File).name).toBe('Run_4k_Chabany.fit');
    expect(body.get('trackCoverage')).toBe('fragment');
    const gpx = await readText(body.get('track') as File);
    // The start end of the tail, the one point drawn: two positions and nothing else.
    expect(gpx.match(/<trkpt /g)).toHaveLength(2);
    // It starts where the watch's own position ends (record 5 of the fixture), not at an arbitrary
    // end of the line: the order of the hand-off is what the repair anchors the fragment by.
    const first = /<trkpt lat="([^"]+)" lon="([^"]+)"/.exec(gpx);
    expect(Number(first?.[1])).toBeCloseTo(50.335, 6);
    expect(Number(first?.[2])).toBeCloseTo(30.385, 6);
  });

  it('answering the fragment question after a drawing repeats the drawn track, not the one it replaced', async () => {
    let asks = 0;
    const fetched = await atTheMap(() => {
      asks += 1;
      return answers(asks === 2 ? aReport({ ...aRoute(), ...aFragment }) : withMap);
    });
    await userEvent.click(screen.getByRole('button', { name: draw.confirm }));

    await userEvent.click(await screen.findByRole('button', { name: uk.result.confirmFragment }));
    await screen.findByRole('button', { name: uk.result.download });

    const repairs = fetched.mock.calls.filter(([url]) => url === '/api/repair');
    expect(repairs).toHaveLength(3);
    const body = (repairs[2] as [string, RequestInit])[1].body as FormData;
    expect((body.get('track') as File).name).toBe('drawn-stretch.gpx');
    expect(body.get('trackCoverage')).toBe('fragment');
  });

  it('a later answer repeats the drawn track and not the one it replaced', async () => {
    const fetched = await atTheMap(() => answers(withMap));
    await userEvent.click(screen.getByRole('button', { name: draw.confirm }));
    await screen.findByRole('button', { name: draw.begin });

    // Drawing again from the new result is a third request that still carries a drawn file.
    await userEvent.click(screen.getByRole('button', { name: draw.begin }));
    fireEvent.keyDown(screen.getByRole('region', { name: uk.result.map.label }), { key: 'Enter' });
    await userEvent.click(screen.getByRole('button', { name: draw.confirm }));
    await screen.findByRole('button', { name: draw.begin });

    const repairs = fetched.mock.calls.filter(([url]) => url === '/api/repair');
    expect(repairs).toHaveLength(3);
    const body = (repairs[2] as [string, RequestInit])[1].body as FormData;
    expect((body.get('track') as File).name).toBe('drawn-stretch.gpx');
  });

  it('a refusal keeps the drawing and states the service’s sentence', async () => {
    let asks = 0;
    await atTheMap(() => {
      asks += 1;
      return asks === 1 ? answers(withMap) : answers({ code: 'TRACK_TOO_FEW_POINTS' }, 400);
    });

    await userEvent.click(screen.getByRole('button', { name: draw.confirm }));

    expect(await screen.findByText(uk.codes.TRACK_TOO_FEW_POINTS)).toBeInTheDocument();
    expect(screen.getByText(`${draw.points} 1`)).toBeInTheDocument();
  });
});
