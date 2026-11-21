# Epic 2 Context: Repair in the Browser

<!-- Compiled 2026-11-21 from the PRD, the architecture spine, the UX documents and the epics file
     in docs/planning-artifacts/. Edit freely. Regenerate with compile-epic-context if those change. -->

## Goal

A person with no terminal opens a page, drags in two files, presses one button and gets a correct FIT back. The whole thing comes up with one command. There is no preview yet: the result screen states the metrics as numbers and offers the download — the map, the chart and the zones are Epic 3. Epic 1 made the repair itself trustworthy, so this epic is about reaching it from a browser, and about every refusal arriving in a shape the interface can show.

## Stories

- Story 2.1: Any failure arrives in a shape the interface can show
- Story 2.2: The service describes the file just chosen
- Story 2.3: The service returns the repaired activity
- Story 2.4: A page that explains itself in ten seconds
- Story 2.5: Two zones that show what was actually chosen
- Story 2.6: Take the repaired file away
- Story 2.7: Errors speak human language
- Story 2.8: Everything comes up with one command

## Requirements & Constraints

- Repairing a two-hour activity must finish inside three seconds, synchronously, within the request. If that budget stops holding, the answer is a new architecture decision, not a quietly added worker.
- The server writes no uploaded file to disk and keeps nothing between requests. Every request is self-contained; the client sends the same files again. No sessions, no server-side result cache, no queues, and the container's filesystem is read-only.
- The shell checks only the size of an upload (`FITFIX_MAX_UPLOAD_BYTES`, 20 MB by default) and the number of request parts. Content type and file extension never decide a refusal — the core determines the format from the bytes and raises a domain exception carrying a code.
- Logs never contain coordinates, file contents or uploaded file names. Duration, input size, record and point counts, sport and warning codes are allowed.
- One image, one origin: the API serves the built SPA and `/api/*` from the same origin, and unknown paths return `index.html`. In development Vite proxies `/api` to the backend. CORS is never configured.
- Every error response is a `ProblemDetails` (RFC 9457) carrying a `code` from the core's registry — including a size refusal (`FILE_TOO_LARGE`, 413) and an unexpected failure (`INTERNAL_ERROR`, 500). Problems with the input itself are 400.
- Downloading the repaired file is gated only by warnings of severity `blocking`, and only until the user confirms explicitly; `info` blocks nothing. Confirming an over-long track does not merely open the gate — it changes the result, so the client repeats the request with `trackLengthMode=full`.

## Technical Decisions

- **Hand-written contract.** Every response body is a hand-written `record` DTO in `FitFix.Api/Contracts`. Core domain types are never serialised. **Derived values — pace, fractions, deltas — do not belong in the contract**: the UI computes them from the base quantities. Enums travel as strings, never as numbers, and each DTO has one contract test for its shape. Note the tension with Epic 1: the core's `ActivitySummary` does carry paces and a measured fraction, because the CLI needs them; the DTO still may not.
- **The inspect endpoint knows the file's role.** It takes a required `role` of `original` or `track` and answers with a discriminated response carrying that role. Every number in it is computed through the same core models as the repair — a track through `ReferenceTrack` with the same de-duplication — so the dropzone and the result screen cannot disagree about a distance.
- **The shell holds no arithmetic over an activity.** The core owns every summary (`ActivitySummary`, settled in Story 1.7) and every refusal code (`DiagnosticCodes`, Story 1.2); the API transcribes. `RepairPipeline` is the single composition of read, align and repair that the CLI already uses, so the API must call it rather than assembling the steps itself.
- **Cleaning is the core's, not the pipeline's alone.** `GpsCleaner` is public and runs inside `RepairService.Repair`. An inspect request that only reads a file does not repair it, so it has to clean the series itself before reporting anything about positions, or it will describe a teleport the repair would have discarded.
- **The repaired file comes back inside the JSON**, as base64 beside the report, so one request yields both the numbers and the file. Nothing is stored server-side to fetch later.
- Backend is .NET 10 with ASP.NET Core Minimal API; the frontend is React 19 with Vite, TypeScript and Tailwind 4. The repository has `src/FitFix.Core`, `src/FitFix.Cli` and `tests/FitFix.Core.Tests`; `src/FitFix.Api` and `web/` do not exist yet and this epic creates them.

## UX & Interaction Patterns

- The page has three states — empty, working, result — and the empty state has to explain the service in about ten seconds: what it does, which two files it wants, and which is which.
- Two dropzones, each naming its role, each showing what it understood about the file it was given rather than only its name.
- The result screen states the before and after numbers, the two zones (measured and estimated) that say what the repair actually knew, and the download. Warnings appear as a callout above the numbers.
- Error text is Ukrainian and lives only in the UI dictionary, keyed by the code the API returned. The keys and the core's registry must match, and a test holds them equal.

## Cross-Story Dependencies

- Story 2.1 comes first: it creates the project and the error contract every later endpoint answers through.
- Stories 2.2 and 2.3 are the two endpoints; 2.3 needs the summary and the warning severities from Epic 1, both of which exist.
- Stories 2.4 to 2.6 are the page, and each needs 2.3's response shape to be settled.
- Story 2.7 needs the code registry to be complete enough that every code the API can return has a Ukrainian text, and the test that holds the two equal is the one AD-18 has been waiting for since Story 1.2.
- Story 2.8 is last: one image serving both the API and the built SPA.
- Epic 1's remaining stories (1.8 over-long track, 1.9 zones, 1.10 written structure, 1.11 the CLI report) are parked by Ілля's decision and do not block this epic. Story 2.5's two zones read the measured boundary the core already reports; the explicit zone intervals of 1.9 are only needed for Epic 3's preview.
