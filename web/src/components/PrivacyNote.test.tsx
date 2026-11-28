import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import { uk } from '../i18n/uk';
import { PrivacyNote } from './PrivacyNote';

describe('PrivacyNote', () => {
  it('says the one thing that matters without being opened', () => {
    render(<PrivacyNote texts={uk.privacy} />);

    // The answer for almost everybody, visible on the page rather than behind a control.
    expect(screen.getByText(uk.privacy.summary)).toBeInTheDocument();
  });

  it('holds the whole note, folded away until it is asked for', async () => {
    render(<PrivacyNote texts={uk.privacy} />);

    const disclosure = screen.getByRole('group');
    expect(disclosure).not.toHaveAttribute('open');

    await userEvent.click(screen.getByText(uk.privacy.more));

    expect(disclosure).toHaveAttribute('open');
    for (const point of uk.privacy.points) expect(screen.getByText(point)).toBeInTheDocument();
  });

  it('says where to ask, and sends map errors where they belong', () => {
    render(<PrivacyNote texts={uk.privacy} />);

    const contact = screen.getByRole('link', { name: uk.privacy.contactLabel });
    expect(contact).toHaveAttribute('href', uk.privacy.contactHref);

    // A wrong path on the basemap is OpenStreetMap's to fix, and its tile terms ask that the
    // way to report one be offered. Pointing it here would lose the report.
    const mapIssue = screen.getByRole('link', { name: uk.privacy.mapIssueLabel });
    expect(mapIssue).toHaveAttribute('href', uk.privacy.mapIssueHref);
  });

  it('promises only what the service keeps', () => {
    // The claims the rest of the repository has to go on keeping: nothing stored, nothing in
    // the logs. A note that drifted from them would be the worst kind of wrong.
    const note = [uk.privacy.summary, ...uk.privacy.points].join(' ');

    expect(note).toContain('не зберігаються');
    expect(note).toContain('IP-адреси');
  });
});
