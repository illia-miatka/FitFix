import { render, screen } from '@testing-library/react';
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

/**
 * The service, stubbed: each zone asks `/api/inspect` about its own file (Story 2.5), so the
 * page cannot be driven without an answer. The role is read back out of the request, which is
 * what makes one stub serve both zones. `Dropzone.test.tsx` covers the states themselves; what
 * is checked here is the one place the two zones meet.
 */
function serviceDescribesEverything() {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (_url: string, request: RequestInit) => {
      const role = (request.body as FormData).get('role');
      const payload =
        role === 'original'
          ? {
              role: 'original',
              sport: 'running',
              startedAt: '2026-11-19T07:12:03Z',
              elapsedSeconds: 2162,
              recordedDistanceMeters: 3911,
              recordCount: 2163,
              hasPosition: true,
              discardedPositionCount: 0,
              codes: [],
            }
          : { role: 'track', lengthMeters: 6537.4, pointCount: 1248, format: 'gpx', hasAltitude: true };
      return { ok: true, status: 200, json: async () => payload } as Response;
    }),
  );
}

afterEach(() => {
  vi.unstubAllGlobals();
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
          ? ({
              ok: false,
              status: 400,
              json: async () => ({ code: 'TRACK_FORMAT_UNKNOWN' }),
            } as Response)
          : ({
              ok: true,
              status: 200,
              json: async () => ({
                role: 'original',
                sport: 'running',
                startedAt: '2026-11-19T07:12:03Z',
                elapsedSeconds: 2162,
                recordedDistanceMeters: 3911,
                recordCount: 2163,
                hasPosition: true,
                discardedPositionCount: 0,
                codes: [],
              }),
            } as Response);
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
