import { useState } from 'react';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { Dropzone } from './Dropzone';
import { uk } from '../i18n/uk';

/**
 * Story 2.5: what a zone shows about the file it was given. The fetch is stubbed throughout —
 * these tests are about the three states and never about the service, which has its own tests
 * in `tests/FitFix.Api.Tests/InspectTests.cs`.
 */

/** The bytes are never read here: the stub decides what the file turns out to be. */
function someFile(name: string) {
  return new File([new Uint8Array([0x0e, 0x10, 0x43, 0x5a])], name, {
    type: 'application/octet-stream',
  });
}

/**
 * One answer from the service. A hand-made object rather than a `Response`: the client reads
 * `ok`, `status` and `json()`, and nothing in jsdom has to provide the rest.
 */
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

/** The same date the component formats, built from the primitives rather than from its code. */
function expectedStart(startedAt: string) {
  const at = new Date(startedAt);
  const pad = (value: number) => String(value).padStart(2, '0');
  return `${pad(at.getDate())}.${pad(at.getMonth() + 1)}.${at.getFullYear()}, ${pad(at.getHours())}:${pad(at.getMinutes())}`;
}

/** Holds the file and the acceptance the way `App` does, so the zone is driven as it is used. */
function Zone({ role }: { readonly role: 'original' | 'track' }) {
  const [file, setFile] = useState<File | null>(null);
  const [accepted, setAccepted] = useState(false);

  return (
    <>
      <Dropzone
        role={role}
        texts={role === 'original' ? uk.dropzones.original : uk.dropzones.track}
        inspection={uk.inspection}
        clearLabel={uk.dropzones.clear}
        retryLabel={uk.errors.retry}
        codes={uk.codes}
        file={file}
        onFileChange={setFile}
        onAcceptedChange={setAccepted}
      />
      <span data-testid="accepted">{String(accepted)}</span>
    </>
  );
}

function pickLabel(role: 'original' | 'track') {
  return role === 'original' ? uk.dropzones.original.pick : uk.dropzones.track.pick;
}

/**
 * Picks a file through the hidden input. The zone's button opens the native picker, which jsdom
 * cannot show, so the test does what the picker would do.
 */
async function choose(role: 'original' | 'track', file: File) {
  const zone = screen.getByRole('button', { name: pickLabel(role) }).parentElement;
  expect(zone).not.toBeNull();
  const input = zone!.querySelector('input[type="file"]');
  await userEvent.upload(input as HTMLInputElement, file);
}

function accepted() {
  return screen.getByTestId('accepted').textContent;
}

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('a file the service describes', () => {
  it('shows the original it understood, asked for in that role', async () => {
    const fetched = vi.fn().mockResolvedValue(answers(anOriginal));
    vi.stubGlobal('fetch', fetched);

    render(<Zone role="original" />);
    await choose('original', someFile('activity.fit'));

    // The region is in the document from the start — what has to be waited for is its answer.
    await screen.findByText(expectedStart(anOriginal.startedAt));
    const answer = screen.getByRole('status');
    expect(answer).toHaveTextContent('activity.fit');
    expect(answer).toHaveTextContent('36:02');
    expect(answer).toHaveTextContent(`3,91 ${uk.inspection.kilometres}`);
    expect(accepted()).toBe('true');

    // The role travels with the file, and a FIT is valid in both: a zone that let the service
    // guess would be answered with the other zone's fields (AD-15).
    const [url, request] = fetched.mock.calls[0] as [string, RequestInit];
    expect(url).toBe('/api/inspect');
    expect(request.method).toBe('POST');
    expect((request.body as FormData).get('role')).toBe('original');
  });

  it('shows the track it understood, with the format it detected', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(answers(aTrack)));

    render(<Zone role="track" />);
    await choose('track', someFile('route.gpx'));

    await screen.findByText(`6,54 ${uk.inspection.kilometres}`);
    const answer = screen.getByRole('status');
    expect(answer).toHaveTextContent('1248');
    expect(answer).toHaveTextContent('GPX');
    expect(accepted()).toBe('true');
  });

  it('says a device recorded no distance rather than showing zero', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(answers({ ...anOriginal, recordedDistanceMeters: null })),
    );

    render(<Zone role="original" />);
    await choose('original', someFile('activity.fit'));

    expect(await screen.findByText(uk.inspection.noDistance)).toBeInTheDocument();
    expect(screen.getByRole('status')).not.toHaveTextContent(`0,00 ${uk.inspection.kilometres}`);
  });

  it('repeats what the repair would say about the file, from the one dictionary', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        answers({ ...anOriginal, discardedPositionCount: 7, codes: ['POSITIONS_DISCARDED'] }),
      ),
    );

    render(<Zone role="original" />);
    await choose('original', someFile('activity.fit'));

    expect(await screen.findByText(uk.codes.POSITIONS_DISCARDED)).toBeInTheDocument();
    expect(accepted()).toBe('true');
  });
});

describe('while the service is being asked', () => {
  it('shows a skeleton, and the zone counts as unchosen until the answer', async () => {
    // Never resolves: the state under test is the one in flight.
    vi.stubGlobal('fetch', vi.fn().mockReturnValue(new Promise<Response>(() => {})));

    render(<Zone role="original" />);
    await choose('original', someFile('activity.fit'));

    expect(await screen.findByText(uk.inspection.reading)).toBeInTheDocument();
    expect(accepted()).toBe('false');
  });
});

describe('a file the service refuses', () => {
  it('shows the dictionary sentence for the code and does not count as chosen', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        answers(
          {
            type: 'urn:fitfix:problem:NOT_A_FIT_FILE',
            title: 'NOT_A_FIT_FILE',
            status: 400,
            code: 'NOT_A_FIT_FILE',
          },
          400,
        ),
      ),
    );

    render(<Zone role="original" />);
    await choose('original', someFile('photo.jpg'));

    expect(await screen.findByText(uk.codes.NOT_A_FIT_FILE)).toBeInTheDocument();
    expect(accepted()).toBe('false');
    // The way out: the whole card stays a button, and it now offers another file.
    expect(screen.getByText(uk.inspection.another)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: uk.dropzones.clear })).toBeInTheDocument();
  });

  it('offers no retry at 400: the same bytes would meet the same code', async () => {
    const fetched = vi.fn().mockResolvedValue(answers({ code: 'NOT_A_FIT_FILE' }, 400));
    vi.stubGlobal('fetch', fetched);

    render(<Zone role="original" />);
    await choose('original', someFile('photo.jpg'));
    await screen.findByText(uk.codes.NOT_A_FIT_FILE);

    expect(screen.queryByRole('button', { name: uk.errors.retry })).not.toBeInTheDocument();
    expect(fetched).toHaveBeenCalledTimes(1);
  });

  it('offers the attempt when the failure was ours, and pressing it re-asks', async () => {
    // `FailureStatus` serves our own broken invariants at 500 while keeping their code, exactly
    // so the athlete is not told their file is wrong — and the dictionary's text for one of
    // those ends in «Спробуй ще раз», which is a promise only an attempt can keep.
    const fetched = vi
      .fn()
      .mockResolvedValueOnce(answers({ code: 'INTERNAL_ERROR' }, 500))
      .mockResolvedValueOnce(answers(aTrack));
    vi.stubGlobal('fetch', fetched);

    render(<Zone role="track" />);
    await choose('track', someFile('route.gpx'));

    expect(await screen.findByText(uk.codes.INTERNAL_ERROR)).toBeInTheDocument();
    expect(accepted()).toBe('false');

    await userEvent.click(screen.getByRole('button', { name: uk.errors.retry }));

    expect(await screen.findByText(`6,54 ${uk.inspection.kilometres}`)).toBeInTheDocument();
    expect(fetched).toHaveBeenCalledTimes(2);
    expect(((fetched.mock.calls[1]![1] as RequestInit).body as FormData).get('role')).toBe('track');
    expect(accepted()).toBe('true');
  });

  it('offers the attempt when the upload itself was cut short, though that is a 400', async () => {
    // `FailureStatus.ForStatus` answers every unnamed sub-500 status with `REQUEST_MALFORMED`, so
    // a truncated upload arrives as a 400 whose file is intact: the journey broke, not the bytes,
    // and its own sentence ends in «Спробуй ще раз».
    const fetched = vi
      .fn()
      .mockResolvedValueOnce(answers({ code: 'REQUEST_MALFORMED' }, 400))
      .mockResolvedValueOnce(answers(aTrack));
    vi.stubGlobal('fetch', fetched);

    render(<Zone role="track" />);
    await choose('track', someFile('route.gpx'));

    expect(await screen.findByText(uk.codes.REQUEST_MALFORMED)).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: uk.errors.retry }));

    expect(await screen.findByText(`6,54 ${uk.inspection.kilometres}`)).toBeInTheDocument();
    expect(fetched).toHaveBeenCalledTimes(2);
    expect(accepted()).toBe('true');
  });

  it('reads as the generic sentence when the dictionary lacks the code, keeping it in the DOM', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(answers({ code: 'A_CODE_FROM_THE_FUTURE' }, 400)));

    const { container } = render(<Zone role="original" />);
    await choose('original', someFile('activity.fit'));

    // A code DESIGN.md forbids on the screen is on nobody's screen — and still reaches whoever
    // gets the bug report, through the attribute (Story 2.7).
    expect(await screen.findByText(uk.errors.unexpected)).toBeInTheDocument();
    expect(screen.queryByText('A_CODE_FROM_THE_FUTURE')).not.toBeInTheDocument();
    expect(screen.getByRole('status').textContent).not.toContain('A_CODE_FROM_THE_FUTURE');
    expect(container.querySelector('[data-code="A_CODE_FROM_THE_FUTURE"]')).not.toBeNull();
    expect(accepted()).toBe('false');
  });

  it('says a warning of the file in words, and an unregistered one generically', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(answers({ ...anOriginal, codes: ['NOT_A_CODE_WE_KNOW'] })),
    );

    const { container } = render(<Zone role="original" />);
    await choose('original', someFile('activity.fit'));

    // Accepted, so this is a remark and not a refusal: the generic sentence, severity unchanged.
    expect(await screen.findByText(uk.errors.unexpected)).toBeInTheDocument();
    expect(container.querySelector('[data-code="NOT_A_CODE_WE_KNOW"]')).not.toBeNull();
    expect(screen.queryByText('NOT_A_CODE_WE_KNOW')).not.toBeInTheDocument();
    expect(accepted()).toBe('true');
  });
});

describe('when the service cannot be reached', () => {
  it('says so instead of blaming the file', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')));

    render(<Zone role="original" />);
    await choose('original', someFile('activity.fit'));

    expect(await screen.findByText(uk.inspection.unreachable)).toBeInTheDocument();
    expect(accepted()).toBe('false');
    // Not a refusal: none of the file's own codes may appear here.
    expect(screen.getByRole('status')).not.toHaveTextContent(uk.codes.NOT_A_FIT_FILE);
  });

  it('offers the attempt, and pressing it asks about the same file again', async () => {
    const fetched = vi
      .fn()
      .mockRejectedValueOnce(new TypeError('Failed to fetch'))
      .mockResolvedValueOnce(answers(aTrack));
    vi.stubGlobal('fetch', fetched);

    render(<Zone role="track" />);
    await choose('track', someFile('route.gpx'));
    await screen.findByText(uk.inspection.unreachable);

    await userEvent.click(screen.getByRole('button', { name: uk.errors.retry }));

    // The second ask is about the file the zone is still holding: no second drop, same bytes,
    // same role — and this time the service answers.
    expect(await screen.findByText(`6,54 ${uk.inspection.kilometres}`)).toBeInTheDocument();
    expect(fetched).toHaveBeenCalledTimes(2);
    const [url, request] = fetched.mock.calls[1] as [string, RequestInit];
    expect(url).toBe('/api/inspect');
    expect((request.body as FormData).get('role')).toBe('track');
    expect(((request.body as FormData).get('file') as File).name).toBe('route.gpx');
    expect(accepted()).toBe('true');
    expect(screen.queryByRole('button', { name: uk.errors.retry })).not.toBeInTheDocument();
  });

  it('leaves the attempt in place when the second ask fails too', async () => {
    const fetched = vi.fn().mockRejectedValue(new TypeError('Failed to fetch'));
    vi.stubGlobal('fetch', fetched);

    render(<Zone role="original" />);
    await choose('original', someFile('activity.fit'));
    await screen.findByText(uk.inspection.unreachable);

    await userEvent.click(screen.getByRole('button', { name: uk.errors.retry }));

    await waitFor(() => expect(fetched).toHaveBeenCalledTimes(2));
    expect(await screen.findByText(uk.inspection.unreachable)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: uk.errors.retry })).toBeInTheDocument();
    expect(accepted()).toBe('false');
  });

  it('asks once per press, and the answer shown is the attempt in flight', async () => {
    let answerSecond: (answer: Response) => void = () => {};
    const fetched = vi
      .fn()
      .mockRejectedValueOnce(new TypeError('Failed to fetch'))
      .mockReturnValueOnce(
        new Promise<Response>((resolve) => {
          answerSecond = resolve;
        }),
      );
    vi.stubGlobal('fetch', fetched);

    render(<Zone role="track" />);
    await choose('track', someFile('route.gpx'));
    await screen.findByText(uk.inspection.unreachable);

    await userEvent.click(screen.getByRole('button', { name: uk.errors.retry }));

    // The attempt replaces the failure with the skeleton, which is also what takes the button
    // away: a second press cannot reach it while the answer is in flight.
    expect(await screen.findByText(uk.inspection.reading)).toBeInTheDocument();
    expect(screen.queryByText(uk.inspection.unreachable)).not.toBeInTheDocument();
    expect(fetched).toHaveBeenCalledTimes(2);

    answerSecond(answers(aTrack));
    expect(await screen.findByText(`6,54 ${uk.inspection.kilometres}`)).toBeInTheDocument();
  });

  it('cannot have a repeated ask overwrite the file dropped while it was out', async () => {
    let answerRetry: (answer: Response) => void = () => {};
    const fetched = vi
      .fn()
      .mockRejectedValueOnce(new TypeError('Failed to fetch'))
      .mockReturnValueOnce(
        new Promise<Response>((resolve) => {
          answerRetry = resolve;
        }),
      )
      .mockResolvedValueOnce(answers({ ...aTrack, lengthMeters: 10400 }));
    vi.stubGlobal('fetch', fetched);

    render(<Zone role="track" />);
    await choose('track', someFile('wrong.gpx'));
    await screen.findByText(uk.inspection.unreachable);
    await userEvent.click(screen.getByRole('button', { name: uk.errors.retry }));

    // The right file arrives before the repeated ask about the wrong one answers.
    await choose('track', someFile('right.gpx'));
    await screen.findByText(`10,40 ${uk.inspection.kilometres}`);
    answerRetry(answers(aTrack));
    await waitFor(() => expect(fetched).toHaveBeenCalledTimes(3));

    expect(screen.getByRole('status')).toHaveTextContent('right.gpx');
    expect(screen.queryByText(`6,54 ${uk.inspection.kilometres}`)).not.toBeInTheDocument();
  });

  it('takes the attempt away with the file when the zone is cleared', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')));

    render(<Zone role="track" />);
    await choose('track', someFile('route.gpx'));
    await screen.findByText(uk.inspection.unreachable);

    await userEvent.click(screen.getByRole('button', { name: uk.dropzones.clear }));

    expect(screen.queryByText(uk.inspection.unreachable)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: uk.errors.retry })).not.toBeInTheDocument();
    expect(screen.getByText(new RegExp(uk.dropzones.track.prompt))).toBeInTheDocument();
    expect(accepted()).toBe('false');
  });
});

describe('a second file dropped on a described zone', () => {
  it('replaces the answer, and a late answer for the first cannot overwrite it', async () => {
    let answerFirst: (answer: Response) => void = () => {};
    const fetched = vi
      .fn()
      .mockReturnValueOnce(
        new Promise<Response>((resolve) => {
          answerFirst = resolve;
        }),
      )
      .mockResolvedValueOnce(answers({ ...aTrack, lengthMeters: 10400, pointCount: 2000 }));
    vi.stubGlobal('fetch', fetched);

    render(<Zone role="track" />);
    await choose('track', someFile('wrong.gpx'));
    await choose('track', someFile('right.gpx'));

    const answer = await screen.findByText(`10,40 ${uk.inspection.kilometres}`);
    expect(answer).toBeInTheDocument();

    // The first ask returns last, carrying the track nobody is holding any more.
    answerFirst(answers(aTrack));
    await waitFor(() => expect(fetched).toHaveBeenCalledTimes(2));

    expect(screen.getByRole('status')).toHaveTextContent('right.gpx');
    expect(screen.getByRole('status')).toHaveTextContent(`10,40 ${uk.inspection.kilometres}`);
    expect(screen.queryByText(`6,54 ${uk.inspection.kilometres}`)).not.toBeInTheDocument();
    expect(accepted()).toBe('true');
  });
});

describe('the clear control', () => {
  it('returns a described zone to empty', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(answers(aTrack)));

    render(<Zone role="track" />);
    await choose('track', someFile('route.gpx'));
    await screen.findByText(`6,54 ${uk.inspection.kilometres}`);

    await userEvent.click(screen.getByRole('button', { name: uk.dropzones.clear }));

    expect(screen.queryByText(`6,54 ${uk.inspection.kilometres}`)).not.toBeInTheDocument();
    expect(screen.getByText(new RegExp(uk.dropzones.track.prompt))).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: uk.dropzones.clear })).not.toBeInTheDocument();
    expect(accepted()).toBe('false');
  });
});

describe('the keyboard', () => {
  it('reaches the zone with Tab and opens the chooser with Enter and with Space', async () => {
    const opened = vi.spyOn(HTMLInputElement.prototype, 'click').mockImplementation(() => {});
    render(<Zone role="original" />);

    await userEvent.tab();
    const zone = screen.getByRole('button', { name: uk.dropzones.original.pick });
    expect(zone).toHaveFocus();
    expect(zone).toHaveAttribute('aria-label', uk.dropzones.original.pick);

    await userEvent.keyboard('{Enter}');
    await userEvent.keyboard(' ');
    expect(opened).toHaveBeenCalledTimes(2);
  });
});

describe('the answer a screen reader is given', () => {
  it('describes the zone as well as announcing itself', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(answers(aTrack)));

    render(<Zone role="track" />);
    await choose('track', someFile('route.gpx'));
    await screen.findByText(`6,54 ${uk.inspection.kilometres}`);

    const answer = screen.getByRole('status');
    const zone = screen.getByRole('button', { name: uk.dropzones.track.pick });
    expect(zone.getAttribute('aria-describedby')?.split(' ')).toContain(answer.id);
    expect(within(answer).getByText(uk.inspection.length)).toBeInTheDocument();
  });
});
