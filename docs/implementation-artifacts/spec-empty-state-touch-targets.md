---
title: 'Touch target of the dropzone clear control'
type: 'bugfix'
created: '2026-11-28'
status: 'done'
baseline_commit: '493e286'
route: 'oneshot'
review_loop_iteration: 0
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

Epic 3 retrospective action item 4 and the Story 3.6 deferral: the clear (×) control of a filled dropzone is 32 px square, below the 44 px a thumb needs on a phone. Make it 44 px; nothing else about it changes.

</frozen-after-approval>

## Implementation Notes

- `Dropzone.tsx`: `size-8` → `size-11`, offset `top-3 right-3` → `top-2 right-2` so the larger target still sits in the same corner. Test pins the class (jsdom has no layout).
- The deferral's second half, the retry button of a notice, is already `min-h-11` (`Notice.tsx`), so nothing to do there.
- Deferred again, not done: a CLI test project for the far-track warning text (needs a new test project), and code-splitting the chart and map (changes how `Result` loads and every test that renders it). Both stay in `deferred-work.md`.
- Review layers skipped: a one-class change with its own test.
