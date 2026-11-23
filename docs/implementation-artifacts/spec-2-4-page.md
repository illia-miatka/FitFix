---
title: 'Story 2.4 — A page that explains itself in ten seconds'
type: 'feature'
created: '2026-11-23'
status: 'done'
route: 'dispatch'
review_loop_iteration: 0
baseline_commit: '9cf2551fc7397043ad9cd3a64787cb9038d2eaba'
context: ['{project-root}/docs/implementation-artifacts/epic-2-context.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The service has three endpoints and no page. Someone who cannot use a terminal has no way in, and a newcomer who did reach a page would have to be told what two files it wants and which is which — a service that needs an explanation beside it has already lost them.

**Approach:** Create `web/` on Vite with React and TypeScript, carrying the design tokens as a Tailwind theme rather than as colours sprinkled through components. The page shows a heading, two dropzones naming their roles, and three steps saying how it works; the repair button stays disabled until both files are there. Every visible string comes from one Ukrainian dictionary, and a test holds its code-shaped keys equal to the core's registry, because the next stories would otherwise hardcode text. Below 768 px the zones stack. With `prefers-reduced-motion` nothing animates.

</frozen-after-approval>

## Code Map

- `docs/planning-artifacts/ux-designs/ux-fitfix-2026-11-19/DESIGN.md` -- the token block at the top is the source: thirteen colours, four radii, the spacing unit, gutter and section, and the three type roles. `metric` carries `tnum`.
- `docs/planning-artifacts/ux-designs/ux-fitfix-2026-11-19/EXPERIENCE.md` -- the page's three states and what belongs in each; this story builds the empty one.
- `src/FitFix.Core/Diagnostics/DiagnosticCodes.cs` -- `All` is the set the dictionary's code keys must equal. It carries the repair codes and the transport ones added in Stories 2.1 to 2.3.
- `src/FitFix.Api/Contracts/InspectResponse.cs`, `RepairResponse.cs` -- the shapes the later stories read; this story needs none of them, but the dictionary's keys for warning codes come from the same vocabulary.
- `CLAUDE.md` -- the stack decided for the frontend: React 19, Vite, TypeScript, Tailwind 4 through `@tailwindcss/vite`, `@fontsource/unbounded` and `@fontsource/manrope`. Node 26 and npm are installed.
- `.github/workflows/ci.yml` -- builds and tests the .NET solution only. The frontend's own install, type-check, build and tests join it here, or nothing guards them.

## Tasks & Acceptance

**Execution:**
- [x] `web/` -- new Vite project with React and TypeScript, built by `npm run build`, type-checked by `tsc --noEmit`, tested by the runner that comes with Vite. -- the shell Stories 2.5 to 2.8 grow inside.
- [x] `web/src/theme.css` (or the Tailwind config, whichever the version wants) -- every colour, radius and spacing value from DESIGN.md as a token, and the three type roles with `tnum` on the metric one. No component may name a raw hex. -- a design that lives in one place can be changed in one place.
- [x] `web/src/i18n/uk.ts` -- one dictionary for every visible string, with the code-shaped keys spelled exactly as the registry spells them. -- AD-4 and AD-18: the contract carries codes, the texts live here alone.
- [x] `web/src/i18n/uk.test.ts` -- a test that the dictionary's code keys equal the core's registry exactly, failing on a code with no text and on a text with no code. It reads the registry from the built core rather than a copy — generate the list at build time or read the C# file; say in the test which, and why it cannot drift. -- the equality AD-18 has been waiting for since Story 1.2.
- [x] `web/src/App.tsx` and its components -- the heading, two dropzones naming their roles, the three steps, and the repair button disabled until both files are chosen. Files are held in state; nothing is uploaded yet. -- Story 2.5 adds what the service says about each file, 2.6 the download.
- [x] `web/src/App.tsx` -- below 768 px the zones stack and the layout stays usable; with `prefers-reduced-motion` no animation runs. -- UX-DR, and a phone is how most people will meet this.
- [x] `.github/workflows/ci.yml` -- install, type-check, build and test the frontend beside the solution. -- a frontend nobody builds in CI is a frontend that breaks quietly.
- [x] `CLAUDE.md` -- record that `web/` exists and how it is run. -- the next session reads it first.

**Acceptance Criteria:**
- Given the built page, when it is opened, then the heading, both dropzones and the three steps are there, and the repair button is disabled until two files are chosen.
- Given the dictionary and the core's registry, when the test runs, then their code keys are equal, and the test fails if either side gains an entry the other lacks.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| The empty state | the page as opened | heading, two named dropzones, three steps | N/A |
| The button waits | no file, one file, both files | disabled, disabled, enabled | N/A |
| Narrow screens | a 375 px viewport | the zones stack, nothing overflows horizontally | N/A |
| Reduced motion | `prefers-reduced-motion: reduce` | no animation runs | N/A |
| One dictionary | every visible string | comes from `uk.ts`; no component holds a literal | N/A |
| Codes match the registry | the dictionary against `DiagnosticCodes.All` | equal sets | the test names the offending key |
| A code with no text | a code added to the core only | the test fails | N/A |
| A text with no code | a code key added to the dictionary only | the test fails | N/A |
| Tokens, not hexes | the component sources | no raw colour literal outside the theme | N/A |

## Verification

**Commands:**
- `npm --prefix web run build` -- expected: succeeds
- `npm --prefix web run test` -- expected: all tests pass, including the dictionary equality
- `dotnet test -c Release` -- expected: unchanged, 325 tests

## Design Notes

The dictionary's equality test is the point of doing this story before the ones that show results. Every warning the service can raise already has a code, and the only reason a text could go missing is that a story invented a string in a component instead. Holding the two sets equal from the first page means a code added in Epic 3 cannot ship without Ukrainian, and the test has to read the registry rather than a transcription of it — a copied list would drift on the first new code, which is exactly the failure it exists to catch.

Tokens before components, for the same reason the thresholds went into the options object: a colour named in three components is three decisions that can diverge. The accent is the colour of "repaired" and belongs to nothing else.

## Implementation Notes

- `web/` is Vite with React, TypeScript and Tailwind 4, whose theme lives in CSS: every DESIGN.md token sits in one `@theme` block, the three type roles are utilities with `tnum` on the metric one, and a test fails on any colour literal in a component source.
- The dictionary's equality test **parses the core's `DiagnosticCodes.cs`** rather than a transcription: it reads the severities table, resolves each constant in the same file, and refuses to pass if it matched fewer than twenty codes, so an empty parse cannot read as agreement. Drift was proved both ways — a code with no text and a text with no code each fail with the offending key named.
- The dropzones accept any file: AD-13 leaves the format to the core, so an `accept` filter would refuse a GPX a watch named `.fit`.
- **Verified independently:** `npm --prefix web run test` and `dotnet test -c Release` both pass; `setup-node@v6` was checked against the action's releases, where v7 is current, so the pin exists.

## Review Triage Log

No review layer ran on this story: it adds a new project rather than changing shared behaviour, and the three claims worth checking were cheap to check directly — the token test, the dictionary drift in both directions, and the page at 375 px with no horizontal overflow.

Known gaps, both stated rather than hidden: "no component holds a text literal" is held by construction and review, since a scanner cannot tell prose from a `className`; and the reduced-motion rule and the 768 px stacking are verified by eye plus structural assertions, because jsdom applies no Tailwind.
