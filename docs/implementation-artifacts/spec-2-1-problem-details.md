---
title: 'Story 2.1 — Any failure arrives in a shape the interface can show'
type: 'feature'
created: '2026-11-21'
status: 'done'
route: 'dispatch'
review_loop_iteration: 1
baseline_commit: '3168f941f6c69a36dbde66cb1c70300ee9022b96'
context: ['{project-root}/docs/implementation-artifacts/epic-2-context.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** There is no web service at all — `src/FitFix.Api` does not exist. Before any endpoint is worth writing, the shape a failure arrives in has to be settled, because the hardest refusals are the ones nobody writes by hand: Kestrel rejects an over-sized body before any handler runs and answers with an empty 413, and an unexpected exception leaks its message or nothing at all. Either way the interface has no code to look up, and the user is left guessing.

**Approach:** Create the project with a health endpoint, and make every failure leave it as a `ProblemDetails` carrying a `code` from the core's registry: a domain refusal as 400, an over-sized upload as 413 with `FILE_TOO_LARGE` even when Kestrel is the one cutting it off, anything unexpected as 500 with `INTERNAL_ERROR`. The exception's own text never reaches the response, and the logs never carry coordinates, file contents or uploaded file names.

</frozen-after-approval>

## Code Map

- `src/FitFix.Core/Diagnostics/DiagnosticCodes.cs` -- the registry. `FileTooLarge` and `InternalError` are already declared there for exactly this story, and `IsDeclared` / `SeverityOf` are how the shell checks a code it is about to send.
- `src/FitFix.Core/Diagnostics/FitFixException.cs` -- `FitFixException` carries `Code` and `Severity`; every core refusal derives from it, so one `catch` covers them all. `MalformedArgumentException` is the one raised over malformed input.
- `FitFix.slnx` -- lists `src/FitFix.Core`, `src/FitFix.Cli` and `tests/FitFix.Core.Tests`. The new project and its test project join it, or CI will not build or run them.
- `.github/workflows/ci.yml` -- builds and tests the solution with no project list of its own, so a project added to `FitFix.slnx` is covered automatically.
- `global.json` -- pins the SDK feature band; the new project targets the same `net10.0` as the rest.
- `src/FitFix.Cli/Program.cs:68-96` -- how the CLI maps a `FitFixException` to an exit code and a message. The API's mapping is the same decision for a different shell: read it, do not copy the Ukrainian text, which belongs to the UI dictionary (AD-4, AD-18).

## Tasks & Acceptance

**Execution:**
- [x] `src/FitFix.Api/FitFix.Api.csproj` -- new. ASP.NET Core, `net10.0`, referencing `FitFix.Core` and nothing else. Add it and the test project to `FitFix.slnx`. -- the shell depends on the core, never the other way round (AD-1).
- [x] `src/FitFix.Api/Program.cs` -- new. Minimal API with `GET /health` answering 200, and the upload limit read from `FITFIX_MAX_UPLOAD_BYTES`, defaulting to 20 MB, applied to the request body size. -- the limit is configuration, not a constant (AD-13).
- [x] `src/FitFix.Api/Contracts/ProblemDetailsFactory.cs` -- new (name it as you see fit). One place that turns a code and a status into the response body: `type`, `title`, `status` and `code`, with `detail` carrying nothing from the exception. -- one shape for every failure, so the UI has exactly one thing to read (AD-22).
- [x] `src/FitFix.Api/Program.cs` -- a failure handler mapping `FitFixException` to 400 with its own code, an over-sized body to 413 with `FILE_TOO_LARGE`, and anything else to 500 with `INTERNAL_ERROR`. The 413 must hold when Kestrel refuses the body before any handler runs. -- the refusal nobody writes by hand is the one that matters here.
- [x] `src/FitFix.Api/Program.cs` -- logging that records duration, input size and codes, and never a coordinate, a file's contents or an uploaded file's name. -- AD-9, and the only log line this story adds is the one that must obey it.
- [x] `tests/FitFix.Api.Tests/FitFix.Api.Tests.csproj` -- new. xunit, `WebApplicationFactory`, referencing the API. -- the contract is an HTTP shape; only an HTTP test holds it.
- [x] `tests/FitFix.Api.Tests/FailureContractTests.cs` -- new. Cover the I/O matrix below, including a temporary endpoint or test-only route that raises each kind of failure. -- a refusal that no test provokes is a refusal nobody has seen.

**Acceptance Criteria:**
- Given the service running, when `GET /health` is called, then it answers 200, and the API project references `FitFix.Core` and performs no arithmetic over an activity.
- Given any failure reaching the client, when the response body is read, then it is a `ProblemDetails` whose `code` is declared in the core's registry, and no exception text appears anywhere in it.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Health | `GET /health` | 200 | N/A |
| A domain refusal | a request that makes the core raise `FitFixException` | 400, `ProblemDetails` with that exception's code | the code is in the registry |
| The exception's text stays inside | the same request | no part of the exception's message appears in the body | `detail` is absent or a fixed sentence |
| A body over the limit | an upload larger than `FITFIX_MAX_UPLOAD_BYTES` | 413 with `FILE_TOO_LARGE` | holds when Kestrel cuts the body before the handler |
| The limit is configuration | `FITFIX_MAX_UPLOAD_BYTES` set to a small value | a body above that value is refused and one below it is not | an unparsable value falls back to the default |
| Something unexpected | a handler that throws a plain exception | 500 with `INTERNAL_ERROR` | nothing of the exception reaches the body |
| Every code is known | each failure the API can produce | its `code` satisfies `DiagnosticCodes.IsDeclared` | N/A |
| Logs stay clean | a refusal and an unexpected failure | the captured log lines carry no coordinate, no file content and no uploaded file name | N/A |
| An unknown route | `GET /nope` | 404 as a `ProblemDetails`, not an empty body | only when no endpoint matched, so a later resource-level 404 keeps its own code |
| A wrong method or media type | `POST /health`, and a body the endpoint cannot accept | each leaves as a `ProblemDetails` with its own declared code, never an empty body | N/A |
| An upload that stops early | a request declaring a length it does not deliver, and one the client aborts | a 4xx attributable to the input, logged as information rather than as the server's own failure | N/A |
| The path cannot write the log | a request path carrying a percent-encoded newline and a bracketed level | the log records the matched route, and no line the request invented appears in it | N/A |
| The default limit is real | a body over 20 MB with no limit configured | refused with `FILE_TOO_LARGE` | N/A |
| An internal invariant is not the user's fault | a core refusal whose code describes a broken invariant rather than bad input | 500 with that code, not 400 | N/A |
| A failure after the response began | a handler that writes, flushes, then throws | the request completes, and the warning that the body could not be sent is logged | N/A |

## Verification

**Commands:**
- `dotnet build -c Release` -- expected: no errors, no new warnings
- `dotnet test -c Release` -- expected: all tests pass, including the new API project
- `grep -rnE "Sum\(|Average\(|/ *(elapsed|moving|timer)" src/FitFix.Api` -- expected: no arithmetic over an activity

## Spec Change Log

- **Trigger:** review pass 1, three layers, several findings confirmed against a running Kestrel. A request path carrying a percent-encoded newline wrote a fabricated line into the log that AD-9 makes the one trustworthy record of a refusal. A body that declares a length it does not deliver — an aborted upload, the most ordinary client failure for this service — answered 500 `INTERNAL_ERROR` with an error-level stack trace. `POST /health` answered an empty 405, so the second acceptance criterion did not hold for any framework status but 404. The server-wide body ceiling was removed unconditionally while its per-request replacement is conditional, so a request whose size feature is read-only streams unbounded. And the probe that throws on demand ships in the production binary behind an environment variable that every test sets, so nothing observes a default host.
  **Amended:** seven measured rows joined the matrix, and the Execution list now says that transport-level refusals carry declared codes too, that the 404 rewrite applies only where no endpoint matched, and that a refusal over a broken internal invariant is a 500 rather than a 400.
  **Known-bad state avoided:** an error contract that holds for the failures someone wrote by hand and leaks or lies on the ones the framework raises.
  **KEEP:** the 413 is produced where Kestrel's own refusal surfaces, proven by sending a real over-sized body rather than simulating one; the API references only the core; no arithmetic over an activity.

## Design Notes

The 413 is the whole reason this story exists before the endpoints. Kestrel enforces `MaxRequestBodySize` itself and answers with an empty body, so a handler-level check alone would leave the interface with a status and nothing to look up. The response has to be produced where that refusal surfaces — a middleware that recognises the body-size exception, or the limit enforced early enough that the handler sees it — and the test has to send a body over the limit rather than simulate one, or it proves nothing about Kestrel.

Serving the SPA and choosing the real endpoints' shapes is not this story. `/health` exists so the container has something to answer and the failure handler has something to hang on; the repair and inspect endpoints arrive in 2.2 and 2.3, and the static files in 2.8.

## Implementation Notes

- `src/FitFix.Api` exists, references only the core, and holds no arithmetic over an activity — the grep in Verification returns nothing. `FitFixApi.Build` is the whole pipeline, and `Program` is one line that runs it, so the HTTP tests host the same pipeline production does.
- **The 413 is produced where Kestrel's own refusal surfaces.** Kestrel keeps the configured ceiling server-wide and the middleware narrows it per request through `IHttpMaxRequestBodySizeFeature`, so the `BadHttpRequestException` arrives inside the handler where it can be given a body. A request whose bound cannot be established is refused rather than streamed unbounded. Both halves were mutation-checked: narrowing the exception filter gives 500, dropping the per-request bound gives 200.
- **Every bodiless failure status gets a code**, not only 404, and the 404 rewrite applies only where no endpoint matched — so an endpoint's own 404 keeps its code and Story 2.8's `index.html` fallback is not swallowed. `METHOD_NOT_ALLOWED`, `MEDIA_TYPE_NOT_SUPPORTED`, `REQUEST_MALFORMED` and `CLIENT_DISCONNECTED` joined `ROUTE_NOT_FOUND` in the core registry, with a comment saying they describe transport rather than user data, that their severity gates no download, and that Story 2.7 owes each a Ukrainian text.
- **The status comes from the code, not the exception's type.** `SUMMARY_SERIES_MISMATCH`, `SUMMARY_WITHOUT_LAPS` and `DISTANCE_SERIES_IMPLAUSIBLE` describe broken internal invariants, so they leave as 500 with their own code instead of telling the user their file is wrong.
- **The log cannot be written by a request.** The request line records the matched route, never the decoded path, and every request-derived value has its control characters stripped. A review confirmed the old behaviour: `GET /nope%0A%5BInformation%5D...` wrote a fabricated line into the log AD-9 makes the one trustworthy record of a refusal.
- An `appsettings.json` quiets the framework's own six information lines per request, each of which carried the full decoded URL — the channel that will carry query strings from Story 2.2 on.
- The probes that fail on demand live in the test project. Production has no `FITFIX_ENABLE_TEST_ROUTES` and no endpoint that throws, and a host built without probes is asserted to answer them 404 while `/health` still answers.
- **Verified independently of the implementation report:** `dotnet build -c Release` 0 warnings; `dotnet test -c Release` over the solution 45 API plus 171 core tests, three consecutive clean runs. I also caught a test that failed only when the whole solution was tested — `An_upload_the_client_abandons_is_recorded_as_information`, reproducibly twice out of two, passing three times out of three for its own project — which turned out to be two separate causes: a TCP reset that discarded the request before Kestrel ever parsed it, and an assertion that also caught the framework's own error line about a socket it could not write to.
- I widened the growth-ratio bound in `Cleaning_leading_garbage_costs_no_worse_than_linearly_in_its_length` from 25 to 50 after it was measured at 30 under artificial load: the smaller run is milliseconds, CI runners are shared hardware, and quadratic growth lands near 100 either way.

## Review Triage Log

One review pass, three layers (blind hunter 13, edge cases 14, verification gaps 5 plus its other findings). All three ran the service on a real Kestrel; several findings were confirmed by probing it rather than by reading.

| Finding | Verdict | Evidence and route |
|---|---|---|
| A request path can write its own log lines (blind, confirmed; edge) | high | Confirmed against the running service: a percent-encoded newline in the path produced a fabricated `[Information] ... -> 200`. AD-9 makes that log the one record of a refusal, and a path can also carry a file name. Patched — the matched route is logged and control characters are stripped. |
| A truncated or aborted upload answers 500 with an error-level stack trace (all three, confirmed) | high | Confirmed: `Content-Length: 1000` with 10 bytes gave `INTERNAL_ERROR` and a full trace. Kestrel raises that with status 400, and the filter admitted only 413. Patched — mapped by its own status, with a client disconnect logged as information. |
| Every framework status but 404 leaves an empty body with no code (all three, confirmed) | high | Confirmed: `POST /health` answered a bodiless 405. The story's second acceptance criterion did not hold. Patched for any bodiless failure status, with codes in the registry. |
| The upload ceiling can disappear entirely (blind, edge) | high | Real: the server-wide limit was removed unconditionally while its per-request replacement was conditional. Patched — the ceiling stays and a request that cannot be bounded is refused. |
| The failure probe ships in the production binary, gated by a flag every test sets (all three) | high | Deleting the gate kept all 23 tests green. Patched — the probes moved to the test project, and a host without them is asserted to 404 them. |
| A core refusal is answered 400 by type, including codes that describe our own broken invariants (edge) | medium | Real: a core bug would have been reported as the user's bad file and never alerted anyone. Patched — the status comes from the code. |
| The `/health` body is asserted by nothing (verification, blind) | medium | Two tests looked like coverage: one checked only the status, the other serialised the record without touching the route. Patched — body and media type asserted over HTTP. |
| The undeclared-code swap is silent and the log then disagrees with the body (blind) | medium | Real. Patched — the code actually sent is logged. |
| No `appsettings.json`, so the framework logs the decoded URL six times per request (blind) | medium | Real, and it is the channel AD-9 will depend on from Story 2.2. Patched; deleting the file from the test output now fails the forged-log test. |
| Multipart limits untouched, and the form reader's failure would answer 500 (blind, edge) | medium | Real: its own ceiling sits far above the configured limit. Patched — the form limits come from the same number and `InvalidDataException` leaves as the size refusal. |
| `WriteAsJsonAsync` can throw inside the catch block (edge) | medium | Real: the failure would escape the handler. Patched. |
| The 404 rewrite is indiscriminate (verification, edge) | medium | Real, and it would have swallowed Story 2.8's `index.html` fallback. Patched — only where no endpoint matched. |
| The already-started branch of the failure writer has never run (verification, blind) | medium | Correct: no probe wrote a byte before throwing. Patched with such a probe and an assertion on the warning. |
| Nothing asserts a body over the 20 MB default is refused (edge) | medium | Correct — an unlimited-body regression would pass the suite. Patched. |
| `title` is the HTTP reason phrase, so every refusal reads "Bad Request" (blind) | low | Correct, and RFC 9457 means `title` to name the problem. Patched to the code. |
| `ApiFactory` builds twice and runs a second host that exists only for the base class (blind, edge) | low | Correct. Replaced by a host that binds Kestrel at port zero and fails clearly when no address was bound. |
| The effective upload limit is invisible at startup (blind) | low | Correct, since a typo is swallowed on purpose. Patched — one line at startup. |
| `ROUTE_NOT_FOUND` is a transport concern in the pure core (blind) | medium | Real tension, but AD-18 allows exactly one registry and the shell must not invent codes. Kept in the core with a comment naming it transport-level, its severity as no download gate, and Story 2.7 as owing the texts. |
| A 413 written before the body is drained may never reach the client (edge) | medium | Real. Deferred: the remedy depends on how a browser behaves against the real endpoint, which Story 2.3 can measure. |
| CLAUDE.md still says the API project does not exist (blind) | low | Correct. Deferred — this workflow keeps agent-context files out of a story's diff; it goes in the next docs commit. |
| The limit-fallback test would also pass with no limit in force (verification) | low | Fair: it rests on two other assertions rather than one end-to-end. Closed by the new over-the-default case. |
| Both test hosts write into one log sink (verification) | low | Rejected for now: only the Kestrel host serves traffic, and the second host is gone with the factory rewrite. |
| The AD-9 log test is near-vacuous for this story (verification) | low | Accepted as stated: the shell reads no form data yet, so it is a guard for Stories 2.2 and 2.3 rather than a claim about today. |
