---
title: 'Story 5.2 — The service asks before treating a track as a fragment'
type: 'feature'
created: '2026-11-27'
status: 'done'
route: 'dispatch'
review_loop_iteration: 0
baseline_commit: '0c38ae633af4571680e994c7c962a91f205eced3'
context: ['{project-root}/docs/implementation-artifacts/epic-5-context.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Story 5.1 applies a fragment silently. Hand the service a short track and it decides by itself that this is the stretch to redraw, redraws it, and says nothing — so somebody who grabbed the wrong file, or a piece of a route they actually ran cleanly, gets 320 seconds of their run replaced by a drawn line and no way to tell. The detection is good enough to act on and not good enough to act on unasked.

**Approach:** Detecting a fragment raises a `blocking` warning naming the stretch it means, in seconds and metres, and the repaired file is withheld until the person confirms — the mechanism AD-22 already defines and an over-long track already uses. Confirming repeats the request with an explicit coverage mode, which is also how a caller who knows says "use it" or "this is a whole route" up front. The CLI says the same thing through its exit code, as it does for the over-long track.

</frozen-after-approval>

## Code Map

- `src/FitFix.Core/RepairService.cs` -- `Repair(original, track, options, trackLengthMode)` and `Aligner.AttachingFragment` (Story 5.1). The coverage mode belongs beside `trackLengthMode` on the same signatures, and `AttachingFragment` is already the one place that answers "is this a fragment".
- `src/FitFix.Core/Alignment.cs` -- `TrackReach(OpensAtTrackStart, ClosesAtTrackEnd, FromSeconds, ToSeconds)` carries the span the warning has to name; `RepairResult.Alignment` exposes it, and `RepairReport` does not. The report is what the shells read (AD-16).
- `src/FitFix.Core/RepairReport.cs` -- `Warnings`, `Zones` and the two track lengths. The redrawn span goes here, so the CLI and the API read one owner's answer rather than each deriving its own.
- `src/FitFix.Core/Diagnostics/DiagnosticCodes.cs` -- the registry and `DiagnosticSeverity`. `TrackLongerThanActivity` is the precedent: `Blocking`, repaired anyway, confirmed by repeating with a mode. `TrackAttachesNowhere` (5.1) stays a refusal.
- `src/FitFix.Api/RepairEndpoint.cs` -- `TrackLengthModeField`, `ModeFrom` and `TrackLengthModes`: the exact shape a second mode follows, including the invalid-value code.
- `src/FitFix.Api/Contracts/RepairResponse.cs` -- hand-written DTOs, no derived values (AD-14). The span is two numbers and a distance, not a sentence.
- `src/FitFix.Cli/Program.cs` -- `--trust-track`, `BlockingWarningText` and exit code 3 for an unconfirmed blocking warning. A fragment flag goes beside it with its own sentence.
- `web/src/api/repair.ts` -- already posts an optional `trackLengthMode` and never sets it; `RepairWarning` carries `code` and `severity`.
- `web/src/components/Result.tsx` -- the warning callouts and the download button, today ungated. `web/src/i18n/uk.ts` holds every text (AD-18).
- `docs/implementation-artifacts/deferred-work.md` -- the AD-22 download gate was deferred from Story 2.6 with the reason that the confirmation which matters changes the result. This story is that confirmation.

## Tasks & Acceptance

**Execution:**
- [ ] `src/FitFix.Core/` -- a coverage mode on the same public signatures as `trackLengthMode`: detect (the default), use the fragment, or never fragment. No default parameter value in a public core signature (AD-10). -- the caller states what it knows; the core never guesses twice.
- [ ] `src/FitFix.Core/RepairService.cs` -- under the default, a detected fragment is repaired as 5.1 repairs it and carries a new `blocking` warning naming the stretch. Under the confirming mode the same repair carries no such warning. Under "never fragment" the whole-track mechanism runs. -- AD-22: a blocking warning is withheld work, not a refusal.
- [ ] `src/FitFix.Core/RepairReport.cs` -- the redrawn span: from and to in seconds, and the metres the fragment contributes. Null when no fragment was used. -- AD-16: one owner, two shells.
- [ ] `src/FitFix.Core/Diagnostics/DiagnosticCodes.cs` + `web/src/i18n/uk.ts` -- the code and its Ukrainian text. -- AD-18, held equal by `uk.test.ts`.
- [ ] `src/FitFix.Api/RepairEndpoint.cs` + `Contracts/` -- the coverage mode as a form field with its own invalid-value code, and the span in the response DTO as base quantities. -- AD-14, AD-22.
- [ ] `src/FitFix.Cli/Program.cs` -- the flag that confirms, the sentence for the new warning, and exit code 3 when it is unconfirmed. -- the CLI has nobody to ask, so the exit code is the question.
- [ ] `web/` -- the result screen withholds the download while this warning is unconfirmed, states the stretch, and the confirm control repeats the repair with the confirming mode. Other blocking codes keep today's behaviour. -- the person sees what would be redrawn before it is theirs.
- [ ] tests -- core, API and web cover the matrix below. -- the gate is the story.

**Acceptance Criteria:**
- Given a track the service recognises as a fragment and no coverage mode, when it is repaired, then the answer carries a blocking warning naming the stretch and the client does not offer the file.
- Given the same request with the confirming mode, when it is repaired, then the file is offered and the warning is gone.
- Given a whole track, when it is repaired, then nothing about this story is visible anywhere.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Detected, unconfirmed | a fragment, no mode | repaired, blocking warning, span stated | the file is withheld by the client |
| Confirmed | the same, confirming mode | same numbers, no fragment warning | N/A |
| Never fragment | the same, whole-track mode | the whole-track mechanism, with whatever it says | N/A |
| A whole track | any mode | no fragment warning and a null span | N/A |
| The span | a tail fragment | from and to in seconds and the fragment's metres, matching the zones | N/A |
| An unknown mode | `trackCoverage=nonsense` | a refusal with its own code | never a silent default |
| The CLI, unconfirmed | a fragment | the file is written, the warning printed, exit code 3 | N/A |
| The CLI, confirmed | the flag | exit code 0 and no fragment warning | N/A |
| The web gate | the blocking fragment warning | no download until confirmed; confirming re-requests and then offers the file | a failed re-request says so and leaves the result |
| Another blocking code | an over-long track | unchanged from today: shown, download not gated | N/A |

## Verification

**Commands:**
- `dotnet test -c Release` -- expected: all pass
- `npm --prefix web run test`, `npm --prefix web run typecheck` -- expected: clean
- The real pair with a fragment of the route through the CLI and through `/api/repair`

## Design Notes

The gate is scoped to this one code on purpose. AD-22 asks for every blocking warning to withhold the file until confirmed, and the over-long track is the other one — but its confirmation is Story 1.8's and its screen is 3.5's, and a gate built for both now would ship half of a story nobody has approved. What this story must not do is leave the fragment warning as an aside beside the download, which is the shape that teaches people to click past warnings.

Three modes rather than two, because "this is a whole route, do not fragment it" is a real answer: a lap course whose fragment-looking track is genuinely the route the athlete ran twice should not be redrawn in one place because the detector preferred the span with the weakest evidence.

The span belongs in the report rather than being derived twice. The CLI prints it in a sentence and the web states it in a callout; if each read `Alignment` itself they could name different seconds for the same repair, which is exactly what AD-16 exists to prevent.

## Review Triage Log

One layer ran, the verification-gap one. The core and API half came back genuinely pinned -- the
warning, the span, `WholeRoute` skipping detection, the invalid-mode refusal, the null span on a
whole track and the response shape all fail a named test when inverted -- and it found three places
where the chain stops. Two are fixed here.

**The request field crossed the C#/TS boundary with nothing holding the two ends together.** Every
.NET test reaches `trackCoverage` through `RepairEndpoint.TrackCoverageField`, and every web test
stubs `fetch` and reads back the `FormData` it built itself, so each end verified itself and never
the other. Rename the field on either side and the whole suite stays green while the gate this
story exists to build stops working in the only way that matters: the server reads an absent field,
falls back to detecting, and answers every confirmation with the same blocking warning the person
just answered -- forever, with no error. `web/src/api/contract.test.ts` now parses the endpoint's
own source for the field name and the three tokens, the way `uk.test.ts` parses the code registry,
and pins the older `trackLengthMode` pair while it is there. Mutation-checked: renaming the field
to `coverage` fails it.

**The download gate matched a third copy of the code.** `Result.tsx` gates on the literal
`TRACK_IS_A_FRAGMENT`, and both gate tests built their warning from the same literal, so a core
rename would have read as a translation failure in `uk.test.ts`, been fixed there, and left the
gate permanently open -- the file offered beside an unanswered blocking warning, which is the one
shape the Design Notes above say must not ship. The contract test now asserts the gated code is one
the registry declares.

The third, that the CLI's flag, its `Перемальовано:` line and its exit code are reached by no test
at all, is deferred: this repository has never had a CLI test project, and standing one up is
larger than this story. It is recorded in `deferred-work.md` with the fixtures that would serve it.

Three things the review found in the work I had added myself before it ran, all fixed. Pressing the
secondary answer flipped the *primary* button's label to «Підтверджую…», so the screen said it was
confirming the stretch while the opposite answer was in flight; the gate now tracks which answer is
out. A refused repeat and an unreachable one were collapsed into one sentence telling the person to
try again, which is wrong advice for a 400 -- the same two files meet the same code -- so an answer
now carries the refusal's code and the screen states the dictionary's sentence for it, as every
other refusal on that page does since Story 2.7. And the entry the implementer appended to
`deferred-work.md` claimed the browser offers no whole-route answer, which the same change had
already made false; the file is append-only, so the correction is an entry of its own.

Why that second answer exists at all: the gate shipped with only «Так, це потрібна ділянка». A lap
run twice looks like a stretch of itself, the API accepts `whole`, and the person is the one who
knows -- a question with only a yes is not a question.

Verified independently: 253 core, 126 API, 101 frontend. On the real pair the whole route is
untouched at 6324.2330 m with no fragment warning; the tail fragment warns, prints
`Перемальовано: 26:52–32:56 (1.24 км із треку)` and exits 3; with `--trust-fragment` it exits 0 and
writes a byte-identical file.

Left for Ілля: the gate covers this one code, where AD-22 asks for every blocking warning. The
other one is the over-long track, whose confirmation is Story 1.8's and whose screen is 3.5's.
