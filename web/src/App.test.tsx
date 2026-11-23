import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import { App } from './App';
import { uk } from './i18n/uk';

/** A file object is all the page holds in this story; the bytes are never read here. */
function fitFile(name: string) {
  return new File([new Uint8Array([0x0e, 0x10, 0x43, 0x5a])], name, {
    type: 'application/octet-stream',
  });
}

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
  it('waits for the second file', async () => {
    render(<App />);
    const repair = () => screen.getByRole('button', { name: uk.repair.action });

    expect(repair()).toBeDisabled();
    expect(screen.getByText(uk.repair.waitingForFiles)).toBeInTheDocument();

    await choose(uk.dropzones.original.pick, fitFile('Run_4k.fit'));
    expect(repair()).toBeDisabled();

    await choose(uk.dropzones.track.pick, fitFile('route.gpx'));
    expect(repair()).toBeEnabled();
    expect(screen.queryByText(uk.repair.waitingForFiles)).not.toBeInTheDocument();
  });

  it('goes back to waiting when a zone is emptied', async () => {
    render(<App />);
    await choose(uk.dropzones.original.pick, fitFile('Run_4k.fit'));
    await choose(uk.dropzones.track.pick, fitFile('route.gpx'));
    expect(screen.getByRole('button', { name: uk.repair.action })).toBeEnabled();

    const [clear] = screen.getAllByRole('button', { name: uk.dropzones.clear });
    await userEvent.click(clear as HTMLElement);

    expect(screen.getByRole('button', { name: uk.repair.action })).toBeDisabled();
  });

  it('shows the chosen file in its own zone and nowhere else', async () => {
    render(<App />);
    await choose(uk.dropzones.original.pick, fitFile('Run_4k_Chabany.fit'));

    expect(screen.getByText('Run_4k_Chabany.fit')).toBeInTheDocument();
    // The track zone still asks for a file: one choice never fills both.
    expect(screen.getAllByText(/Перетягни файл/)).toHaveLength(1);
    expect(screen.getAllByRole('button', { name: uk.dropzones.clear })).toHaveLength(1);
  });
});
