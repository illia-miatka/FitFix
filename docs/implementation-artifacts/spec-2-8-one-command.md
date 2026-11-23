---
title: 'Story 2.8 — Everything comes up with one command'
type: 'feature'
created: '2026-11-23'
status: 'done'
route: 'dispatch'
review_loop_iteration: 0
baseline_commit: 'f7d505c1cf861f1012e6b8251c0ad86a5a05da8a'
context: ['{project-root}/docs/implementation-artifacts/epic-2-context.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The service exists in two halves that only meet on a developer's laptop: Kestrel on 5000 and Vite on 5173, joined by a dev-server proxy. There is no image, no `compose.yaml`, and the API serves no static file at all — so AD-12's one origin is a decision nobody has executed, and the only way to use FitFix is to run two commands in two terminals from a checkout.

**Approach:** `FitFix.Api` serves the built SPA beside `/api/*`, and an unknown path outside `/api` answers with `index.html` so a reload of a deep link works. One multi-stage image builds the frontend in `node:26-alpine`, publishes the API with `dotnet/sdk:10.0`, and ships on `aspnet:10.0-alpine` as an unprivileged user with a read-only filesystem; `GET /health` is its healthcheck. `docker compose up` serves the whole thing on `localhost:8080`.

</frozen-after-approval>

## Code Map

- `src/FitFix.Api/FitFixApi.cs` -- `Build` composes the failure handler, `/health`, the two endpoints and the test host's extra ones. Static serving goes inside it, so the API tests exercise the same pipeline the image runs (the reason `Build` exists). The failure middleware is outermost and rewrites a bodiless 404 into `ROUTE_NOT_FOUND`; the fallback must not take that away from `/api/*`.
- `src/FitFix.Api/FitFixApi.cs` -- `CreateBuilder` already sets `MemoryBufferThreshold` to the whole upload bound, with a comment naming this story: nothing uploaded may reach a disk that is read-only anyway (AD-3).
- `src/FitFix.Api/FitFix.Api.csproj` -- `Microsoft.NET.Sdk.Web`, so `wwwroot` is the web root by convention; the image puts the built SPA there.
- `tests/FitFix.Api.Tests/` -- the suites run over real HTTP against this pipeline with no `wwwroot` on disk. A static-file test has to make its own web root; nothing in the repository does that yet.
- `.github/workflows/ci.yml` -- the `web` job already runs `npm ci`, `npm run typecheck`, `npm run build` and `npm run test`, which Story 2.4 added. The story's CI criterion is therefore already satisfied; do not duplicate those steps.
- `.gitignore` -- `tests/fixtures/private/*` holds real GPS traces. The build context must never carry them into an image.
- `web/vite.config.ts` -- the dev proxy pinned to `127.0.0.1:5000`; development keeps working exactly as it does, and nothing here changes it.
- `docs/planning-artifacts/architecture/.../ARCHITECTURE-SPINE.md` -- AD-12 is the binding rule: one origin, unknown paths answer `index.html`, CORS is never configured.

## Tasks & Acceptance

**Execution:**
- [ ] `src/FitFix.Api/FitFixApi.cs` -- serve default and static files, and map a fallback that answers `index.html` for an unknown path. A request under `/api` never falls back, and a fallback with no `index.html` on disk leaves the request to the existing `ROUTE_NOT_FOUND` contract. -- AD-12 and AD-22: the API's own 404 is a problem document, not a page.
- [ ] `Dockerfile` -- new. Three stages: `node:26-alpine` runs `npm ci` and `npm run build`; `dotnet/sdk:10.0` publishes `src/FitFix.Api` in Release; `aspnet:10.0-alpine` takes the publish output plus the built SPA as `wwwroot`. Runs as the image's unprivileged user, listens on 8080, and declares a healthcheck against `/health`. -- the story's second criterion, verbatim.
- [ ] `.dockerignore` -- new. Keep the context to what the build needs: no `bin`, `obj`, `node_modules`, `.git`, `docs`, `_bmad`, `prototype`, and above all no `tests/fixtures/private`. -- a real GPS trace must never enter an image.
- [ ] `compose.yaml` -- new. One service, built from this context, `8080:8080`, `read_only: true` with a writable `/tmp`, no volume carrying state, and the image's healthcheck. -- `docker compose up` is the whole command.
- [ ] `tests/FitFix.Api.Tests/` -- a static-serving suite: a temporary web root with an `index.html` and an asset, driven over HTTP. -- the fallback is the part of AD-12 that can silently invert.
- [ ] `README.md` -- how to run it: `docker compose up`, then `localhost:8080`. -- the command belongs where somebody looks for it.

**Acceptance Criteria:**
- Given the repository, when `docker compose up` runs, then `localhost:8080` serves the application, with the SPA and `/api/*` on that one origin and no CORS configuration anywhere.
- Given the running container, when an unknown path such as `/result` is opened, then `index.html` is served and the application works.
- Given the running container, when `/api/nonsense` is requested, then the answer is a `ProblemDetails` carrying `ROUTE_NOT_FOUND`, not a page.
- Given the image, when it is inspected, then it ships from `aspnet:10.0-alpine`, runs as a non-root user with a read-only filesystem, and carries no file from `tests/fixtures/private`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| The page | `GET /` with a web root | `index.html` | N/A |
| An asset | `GET /assets/<name>` | the file, with its own content type | a missing asset is a 404 |
| A deep link | `GET /result` | `index.html`, status 200 | N/A |
| An unknown API route | `GET /api/nonsense` | `ProblemDetails` with `ROUTE_NOT_FOUND` | never `index.html` |
| An API route with the wrong method | `GET /api/repair` | the existing method refusal, unchanged | never `index.html` |
| Health | `GET /health` | `{"status":"ok"}` | N/A |
| No web root at all | the test host as it stands today | every path behaves as it does now, `ROUTE_NOT_FOUND` included | no startup failure |
| An upload still never touches disk | a repair in the container | the answer carries the file; the filesystem stays read-only | N/A |
| The private fixtures | the build context | absent from the image | N/A |

## Verification

**Commands:**
- `dotnet test -c Release` -- expected: all pass, including the new static-serving suite
- `docker build -t fitfix .` -- expected: succeeds
- `docker compose up` then `curl -fsS localhost:8080/health`, `curl -fsS localhost:8080/result | head -c 200`, `curl -s -o /dev/null -w '%{http_code}' localhost:8080/api/nonsense` -- expected: ok, the page's HTML, 404 with a problem document

## Design Notes

The fallback has to be narrower than "everything unmatched". `/api/*` is a contract (AD-22: every failure is a `ProblemDetails` carrying a code), and a fallback that answered `index.html` there would hand a parser a page and turn a typo in a route into a mystery. The same goes for a wrong method on a real endpoint.

Static serving belongs in `Build` rather than in `Program`, for the reason `Build` exists: the API tests host the shipped pipeline, so a fallback that only existed in production would be a rule nobody tests. A host with no `wwwroot` must therefore behave exactly as it does today, which is also the state every existing API test runs in.

The read-only filesystem is not decoration: `CreateBuilder` already raises `MemoryBufferThreshold` to the whole upload bound so a multipart part is never spooled to a temporary file (AD-3), and a read-only root is what makes that guarantee enforced rather than merely intended. `/tmp` stays writable because the runtime may want it, and nothing of ours is written there.

## Review Triage Log

One layer ran, the verification-gap one, as the economy Ілля asked for allows. The API half came
back fully covered: it traced each guard in `ShouldServeIndex` to a test that fails when the guard
is inverted, including the one nobody would think to write -- dropping the `GetEndpoint()` check
rewrites `/health` itself into the page, and the health test's exact body catches it.

It found one real gap and it is fixed. Nothing automated built the image or started the container:
the two CI jobs each verify their own half in isolation, so losing the `COPY` that puts `dist` in
`wwwroot` would ship a service answering `ROUTE_NOT_FOUND` to every navigation with all 348 tests
and both jobs green. `.github/workflows/ci.yml` now has a third job that builds the image, starts
it the way `compose.yaml` does (`--read-only`, tmpfs `/tmp`), waits for the image's own
healthcheck, and then checks the page, a deep link, `/api/nonsense` and that the process is
unprivileged on a filesystem it cannot write. It is not a required check in branch protection --
only `Build and test` is -- so it reports rather than blocks until Ілля adds it.

Two smaller things went with it. `UseDefaultFiles` was dead middleware: the rewrite ahead of it
already answers `/` with `index.html`, and where there is no `index.html` there is no default file
either. And the `.dockerignore` comment claimed the wide `tests` rule kept the private traces out
of the image, when the Dockerfile never copies `tests` at all -- it keeps them out of the build
context, which is a different and still worthwhile claim, now stated as such.

The implementer's own deviation was right and is kept: a path rewrite rather than `MapFallback`,
because a catch-all endpoint carries no method metadata and would beat `POST /api/repair` when the
verb is wrong, turning AD-22's 405 into a page.

Verified independently, against the container rather than the report: 348 tests (231 core, 117
API); `docker compose up --build --wait` reports healthy; `/` and `/result` answer `text/html`,
`/api/nonsense` answers `application/problem+json`, `GET /api/repair` answers 405; the process runs
as uid 1654 and `touch /app/x` fails with "Read-only file system". The real pair was repaired
through the container -- 6295.1 m against a 6345.2 m chosen length, the same number the CLI
produces -- which is the first time the whole service has done its job end to end in the shape it
ships in. The empty page was looked at on a 375 px screen: one column, the zones stacked, the
button disabled with its hint beneath.

Not checked by eye: the failure notice and the Strava button of Story 2.7, which need an upload the
browser pane cannot perform.

Left for Ілля: the frontend and image jobs are not required checks, and the alpine runtime ships
with invariant globalization (no ICU) -- nothing in the service is culture-aware, and a repair ran
inside the container, but it is a property of the base image worth knowing.
