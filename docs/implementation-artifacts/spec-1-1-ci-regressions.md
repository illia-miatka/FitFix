---
title: 'Story 1.1 — CI catches algorithm regressions'
type: 'chore'
created: '2026-11-19'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: ['{project-root}/docs/implementation-artifacts/epic-1-context.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Every remaining story in Epic 1 rewrites an algorithm that already produces a result the author verified by hand. There is no automated net under those rewrites: a regression in anchoring, smoothing or monotonicity would only surface when someone runs `dotnet test` locally and happens to notice. The repository has no `.github` directory at all.

**Approach:** Add a GitHub Actions workflow that restores, builds and runs the test suite on every push and on pull requests, so a red test marks the commit and blocks the merge. Scope is the .NET test run only — the frontend build steps belong to Story 2.8, when `web/` exists.

</frozen-after-approval>

## Implementation Notes

- Added `.github/workflows/ci.yml`; the repository had no `.github` directory before this story.
- **Triggers:** `push` on every branch (the story asks for any branch) plus `pull_request` against `main`, so a PR shows its own check.
- **Concurrency.** The two triggers report different refs — `push` gives `refs/heads/<branch>`, `pull_request` gives `refs/pull/<n>/merge` — so a group keyed on `github.ref` alone would put them in separate groups and both would run. The group collapses onto `github.event.pull_request.number` when there is one, falling back to the ref otherwise. `cancel-in-progress` is disabled on `main`, so a merge landing while an earlier run is still going does not leave the earlier commit unverified.
- **Runner:** `ubuntu-latest`. macOS runners bill at ten times the rate and nothing in the test suite needs macOS.
- **Timeout:** `timeout-minutes: 15`. The suite finishes in under a second, so anything near the limit is a hang; without it a wedged run would hold a runner for the six-hour default.
- **SDK pin:** `setup-dotnet` reads `global-json-file: global.json` rather than a hardcoded version, so CI follows the same pin as local builds and a future bump happens in one place. `global.json` requested `10.0.0`, which is not a real SDK version — .NET feature bands start at `10.0.100` — and only worked locally because of `rollForward: latestFeature`; it is now pinned to `10.0.100`, a version that exists. With `latestFeature` the runner may still install a later feature band than the local `10.0.302`, so a `dotnet --info` step records which SDK each run actually resolved.
- **Configuration:** restore, then build and test in `Release`. `dotnet test --no-build` reuses the build output instead of compiling twice.
- **No NuGet cache.** `setup-dotnet`'s cache needs a `packages.lock.json`, which the projects do not have; adding one is a separate decision. Restore pulls three packages and takes about a second, so the cache would buy little.
- **Least privilege:** `permissions: contents: read` — the workflow only reads the repository.
- **Action versions** checked against the registry at authoring time: `actions/checkout@v7`, `actions/setup-dotnet@v6`.
- **Verified locally** by running the exact CI sequence (`dotnet restore`, `dotnet build --no-restore -c Release`, `dotnet test --no-build -c Release`): 25 tests pass. Everything specific to GitHub Actions — action resolution, `global-json-file` handling on the runner, `.slnx` auto-discovery — is first exercised by the first push; the run that proves it is the one this branch triggers.
- **What CI actually asserts:** 24 of the 25 tests. `PrivateFixtureTests` exits early when `tests/fixtures/private/` is absent, which is always true in CI because those files are real GPS data and stay out of the repository, so the test asserting the 19.11.2026 case (6537 m, elapsed-time match, monotonicity, measured fraction, FIT round-trip) reports green while asserting nothing there. Deferred: a visible skip or a synthetic equivalent.
- **Blocking a merge is a repository setting, not a workflow property.** The acceptance criteria only require the check to run and its result to be visible on the commit, which the workflow delivers. Making a red check actually prevent a merge needs `CI / Build and test` marked as a required status check in branch protection or a ruleset on `main` — that is a GitHub setting the repository owner applies, and it is not part of this diff.
- Frontend steps are deliberately absent; `web/` does not exist until Epic 2, and Story 2.8 adds them.

## Review Triage Log

Review pass 1 — one blind reviewer with no prior context, 16 findings.

| Finding | Verdict | Evidence and action |
|---|---|---|
| `concurrency` group does not collapse `push` and `pull_request` | high | Correct: the two events report different refs, so both runs would execute. Group now keys on the PR number with a ref fallback. |
| `cancel-in-progress` also cancels runs on `main` | high | Correct: a merge would cancel verification of the previous commit. Now gated on `github.ref != 'refs/heads/main'`. |
| `global.json` pins `10.0.0`, which no SDK ships | high | Correct: feature bands start at `10.0.100`; only `rollForward: latestFeature` made it work. Pinned to `10.0.100`; still resolves `10.0.302` locally. |
| The real-case test passes vacuously in CI | high | Correct: the early return at `PrivateFixtureTests.cs:28` is always taken in CI. The quiet exit is an agreed convention, so the honest counts are documented here and the fix is deferred. |
| No `timeout-minutes` on the job | medium | Correct. Added, 15 minutes. |
| "Blocks the merge" is not implemented | medium | Partly correct: no workflow can block a merge by itself, but the acceptance criteria only require a visible failing check. Recorded as the owner's branch-protection step with the exact check name. |
| No evidence the workflow has ever executed | medium | Correct, and unavoidable before the first push. Stated plainly in the notes. |
| A red run leaves nothing to diagnose with (no TRX, no artifact) | medium | Correct. A `dotnet --info` step is in; test-result and coverage publishing is deferred, together with the unused `coverlet.collector`. |
| Build does not fail on warnings; no analyzer or format gate | medium | Correct and worth doing, but turning warnings into errors is a project-wide decision that can break a build on an SDK bump. Deferred. |
| `coverlet.collector` referenced but never collected | low | Correct. Folded into the deferred reporting item. |
| Bare `push:` burns minutes on documentation-only commits | low | Correct, but `paths-ignore` makes a skipped run never report, which blocks a PR once the check is required. Deferred until branch protection is settled. |
| `epic-1-context.md` has no compile date or sources; omits the FR4 speed values | medium | Correct. Header now carries the date and the source documents; the sport maximums (running 7, cycling 25, other 15 m/s) are spelled out where they are invoked. |
| Cross-story dependencies omit 1.3 as a prerequisite and 1.11 entirely | medium | Correct, and it could misorder work. Both added. |
| Spec carries no acceptance criteria and no verification section | false | The oneshot route deletes those template sections by design. The substantive half — the untested "red check is visible" criterion — is the branch-protection finding above, already recorded. |
| `sprint-status.yaml` `story_location` points at a non-existent `stories/` directory | low | Correct, but the field is written by `sprint_plan.py` from its own argument, so editing it by hand is reverted on the next generate. Left for a sprint-planning run to pass the real path. |
| `last_updated` uses `MM-DD-YYYY` while other artifacts use ISO | false | The format is `DATE_FORMAT = "%m-%d-%Y %H:%M"` in `sprint_plan.py`; the file is tool-owned and hand-editing it would diverge from the generator. |
