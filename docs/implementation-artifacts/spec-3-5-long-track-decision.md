---
title: 'Story 3.5 — Understand the warning and decide about a long track'
type: 'feature'
created: '2026-11-28'
status: 'done'
baseline_commit: '7e739e017865ea01474b289ac43829ccd1491be7'
route: 'dispatch'
review_loop_iteration: 0
context: ['{project-root}/docs/implementation-artifacts/epic-3-context.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** When the track is longer than the activity could have covered, the service trims it and warns, but the result screen only prints a sentence beside an open download button: the athlete cannot see by how much, cannot choose, and can walk away with a file that was trimmed without ever knowing.

**Approach:** The warning becomes a decision. A card under the callouts states both lengths (the track's and the one used) and offers two explicit answers: keep the trimmed length, or trust the track fully. The download is withheld until one is chosen; "trust fully" repeats the repair with `trackLengthMode=full` and replaces the whole preview, and the warning stays visible after it. The download gate becomes one rule over the decisions a screen can ask for, instead of one code matched by hand. Every callout gets an icon beside its severity word.

**Split by the human on 2026-11-28:** `TRACK_FAR_FROM_ORIGINAL` (a distance to show and an explicit "the track is right" confirmation) needs a new contract field and is its own story; it is deferred, not part of this one.

## Boundaries & Constraints

**Always:** Text only from `uk.ts`, never a raw code on screen (AD-18); colours through `theme.css` (the severity stripe stays `estimated`/`danger`). Both lengths come from `report.track` as sent (AD-14). The repeat is the same two files with `trackLengthMode=full`, and any coverage answer already given (fragment, whole, the drawn file) is repeated with it, as the held length answer is repeated with every later coverage answer (AD-3, AD-22). The decision is forgotten when new files are chosen or "start again" is pressed. Blocking warnings with no screen of their own stay shown beside an open download, as now; `info` never blocks.

**Never:** No server, core or contract change. No `TRACK_FAR_FROM_ORIGINAL` gate or distance. No change to the fragment question's behaviour. No decision stored beyond the page's own state.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Undecided | `TRACK_LONGER_THAN_ACTIVITY` blocking, no choice yet | Callout, a card with both lengths and two buttons, neither pressed; download withheld | N/A |
| Keep trimmed | Press it | No request; button pressed; download offered; warning stays | N/A |
| Trust fully | Press it | One repeat with `trackLengthMode=full`; preview (numbers, map, chart) replaced; button pressed; warning still shown; download offered | Refused: that code's sentence; unreachable: the dictionary's retry sentence; result and withholding unchanged |
| Change of mind | Trusted fully, then keep trimmed | One repeat with `trackLengthMode=plausible` | As above |
| Both answers given | Fragment confirmed or drawn, then a length choice (or the reverse) | The repeat carries both answers | As above |
| Info only | `NO_ORIGINAL_GPS` | Callout with an icon; no card; download open | N/A |
| New start | New file or "start again" | Decision forgotten | N/A |

</frozen-after-approval>

## Code Map

- `web/src/App.tsx` -- holds `lengthMode` (`null` undecided, `'plausible'`, `'full'`) and the coverage already answered; `chooseLength(mode)`; the existing `answerCoverage` also sends the held length answer; both forgotten in `startAgain`, `takeOriginal`, `takeTrack`.
- `web/src/components/Result.tsx` -- `gated` becomes "a fragment warning stands, or a longer-track warning stands and no length answer was chosen"; `LongTrackGate` card; `Callout` icon (lucide `TriangleAlert` for blocking, `Info` for info, `aria-hidden`). New props `lengthDecision`, `onChooseLength`. Reuse `FragmentGate`'s pending/failure pattern and `codeText`.
- `web/src/api/repair.ts` -- `repair(original, track, mode?, coverage?)` already takes both; unchanged.
- `web/src/i18n/uk.ts` -- `result.longTrack` strings; the `TRACK_LONGER_THAN_ACTIVITY` sentence stops asking for a confirmation.
- `web/package.json` -- add `lucide-react`.

## Tasks & Acceptance

**Execution:**
- [x] `web/package.json` -- add `lucide-react` -- the icon set the epic fixed
- [x] `web/src/App.tsx` + `App.test.tsx` -- held length and coverage answers, `chooseLength`, forgetting -- request bodies asserted per matrix row
- [x] `web/src/components/Result.tsx` + `Result.test.tsx` -- `LongTrackGate`, generalised gate, icons
- [x] `web/src/i18n/uk.ts` -- strings

**Acceptance Criteria:**
- Given `TRACK_LONGER_THAN_ACTIVITY`, when the result shows, then both lengths and two options are shown and the download is not.
- Given "trust the track fully", when pressed, then the repair repeats with `trackLengthMode=full`, the preview is replaced and the warning is still visible.
- Given "keep trimmed", when pressed, then the download is offered with no new request.
- Given any warning, when shown, then it has an icon, its severity in words and the dictionary's sentence, never its code.
- Given an `info` warning, then nothing is withheld.

## Implementation Notes

- `App` keeps the two answers in a ref (for the memoised handlers) and the length in state (for the screen); `chooseLength` asks nothing for the first "keep trimmed", and otherwise repeats the repair with the held coverage. `answerCoverage` sends the held length.
- `Result` reads `gated` as: a fragment warning stands, or an over-long track warning stands with no length chosen. `LongTrackGate` shows both lengths and two `aria-pressed` buttons. Callouts carry a `lucide-react` icon beside the severity word.
- Not looked at in a real browser: the pair in `tests/fixtures/private/` has a plausible track, so the card cannot be seen on it; the screen is covered by component and page tests only.

## Review Triage Log

Layer run: verification-gap only (standing instruction to spend fewer tokens).

| Finding | Verdict | Route | Evidence |
|---|---|---|---|
| Two stale comments still said the length choice belongs to Story 1.8 and that the client never sends `full` | low | patch | Real: both now point at `chooseLength` and Story 3.5. |
| Concurrency guard in `chooseLength` untested | low | defer | The sibling guard in `answerCoverage` is equally untested, the gate buttons have their own `pending` guard, and the race needs two gates answered in one request window. Recorded in `deferred-work.md`. |

## Design Notes

AD-22 says confirming this warning changes the result, so the answer is a repeat request, not a flag. After a repeat the same warning still comes back (the core reports it in both modes), which is why the gate reads the page's recorded answer rather than the absence of the warning.
