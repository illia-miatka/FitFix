export interface HowItWorksStep {
  readonly title: string;
  readonly body: string;
}

export interface HowItWorksProps {
  readonly title: string;
  readonly steps: readonly HowItWorksStep[];
}

/**
 * The three steps that let the page explain itself in about ten seconds, so nobody has to be
 * told beside it which two files it wants and which is which.
 *
 * The step number is `display` type and decorative to a screen reader — the list already
 * carries the order, and reading "1" before every heading says it twice.
 */
export function HowItWorks({ title, steps }: HowItWorksProps) {
  return (
    <section aria-labelledby="how-it-works">
      <h2 id="how-it-works" className="type-display text-lg text-text">
        {title}
      </h2>
      <ol className="mt-gutter grid grid-cols-1 gap-gutter md:grid-cols-3">
        {steps.map((step, index) => (
          <li
            key={step.title}
            className="rounded-lg border border-border bg-surface p-gutter"
          >
            <span aria-hidden="true" className="type-metric text-2xl text-accent">
              {index + 1}
            </span>
            <h3 className="type-display mt-2 text-base text-text">{step.title}</h3>
            <p className="mt-2 text-sm text-text-muted">{step.body}</p>
          </li>
        ))}
      </ol>
    </section>
  );
}
