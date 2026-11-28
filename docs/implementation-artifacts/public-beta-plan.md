# Public beta readiness plan

Written 2026-11-28. Scope: put the existing single image on a public URL for a few dozen beta
testers, and make the service safe to leave there unattended.

CLAUDE.md records that public hosting was deliberately out of scope for the MVP. This plan is the
decision to change that, and nothing in it is a change to the repair itself: the algorithm, the
contract and the page stay as Epics 1, 2, 3 and 5 left them.

Nothing here has been deployed and nothing has been bought. The provider is Ilya's choice
(§5), and the work in §2 and §4 lands as four small pull requests that are useful whichever
provider wins.

What the service is today, as far as hosting is concerned:

- One image (`Dockerfile`), SPA and API on one origin, port 8080 (AD-12).
- `compose.yaml` runs it read-only, unprivileged, with a tmpfs `/tmp` and no volume.
- Stateless: an upload is held in memory, nothing is written, nothing survives the request
  (AD-3, AD-11).
- No session, no account, no database, no outbound call of its own. The browser — not the
  server — fetches map tiles from `tile.openstreetmap.org`.
- CI already builds the image and smoke-tests it (`.github/workflows/ci.yml`, job `image`).

That shape is what makes hosting cheap: one container, no state, no backups worth taking,
and a redeploy is "replace the image".

---

## 1. Where to host it

Prices read 2026-11-28 from the providers' own pricing pages. Hetzner is EUR excluding VAT;
Fly.io, Render and Railway are USD.

| | Hetzner CPX02 + Caddy | Fly.io | Render Hobby | Railway Hobby |
|---|---|---|---|---|
| Monthly, one small always-on instance | €5.49 (Helsinki) / €6.49 (Falkenstein, Nuremberg), 1 vCPU, 1 GB, 20 GB NVMe | $3.69 (shared-cpu-1x, 512 MB) or $6.70 (1 GB); no platform fee | $7 (0.5 CPU, 512 MB) on a $0 workspace | $5 workspace fee including $5 of usage; compute metered at ~$10/GB-month RAM and ~$20/vCPU-month |
| Billing unit | Fixed month (hourly €0.0088 available) | Per second of runtime | Month, prorated to the second | Per second of measured CPU and RAM |
| Included traffic | 20 TB; €1.00 per extra TB | None stated; egress $0.02/GB in NA/EU, inbound free | 5 GB, then $0.15/GB | None stated; egress $0.05/GB |
| HTTPS for a custom domain | Caddy gets a Let's Encrypt certificate itself, free | First 10 single-hostname certificates free, then $0.10/month each | Automatic TLS included; 2 custom domains on Hobby | Listed as a platform limit (2 on Hobby); the pricing page does not say whether TLS is included |
| How a new image arrives | We build it. Either `docker compose pull && up -d` against a registry, or `docker build` on the box | `fly deploy` builds the repo's Dockerfile and swaps machines | Connect the repo; Docker build and auto-deploy on push are listed on Hobby | Not stated on the pricing page |
| What we have to run | Debian, Docker, Caddy, unattended upgrades, firewall, the deploy script, the ssh key | Nothing | Nothing | Nothing |
| Catch | Both the CPX plans and the Cost-Optimized CX plans currently read "This product is currently unavailable. Please check back later." on Hetzner's own order page | Free plan covers $5 of usage a month, which is roughly this whole service | Twice the price of Fly for the same 512 MB; 5 GB of egress is the tightest allowance here | Metered CPU and RAM make a quiet month cheap and a busy one unpredictable; no fixed price for "512 MB always on" |

### Recommendation: Fly.io

Reasons, in the order they matter:

1. **It deploys the thing we already have.** `fly deploy` builds `Dockerfile` as it stands. No
   registry to run, no box to prepare, no second definition of how the service starts.
2. **Statelessness pays off.** Because the service keeps nothing between requests (AD-3), Fly's
   machines can auto-stop when idle and auto-start on the next request, and a stopped machine
   costs only its rootfs ($0.15/GB-month). A beta nobody touches on a Tuesday costs almost
   nothing; a cold start of the Alpine ASP.NET image is a second or two, paid by whoever opens
   the page first.
3. **Cheapest always-on figure of the four**, and no platform fee, so the bill is the resources
   and nothing else.
4. **Nothing to patch.** A Hetzner box is a second thing to maintain forever — kernel updates,
   Caddy, Docker, a firewall, and a deploy path someone has to write. For one container that is
   work without a return.

Size: **shared-cpu-1x, 1 GB** ($6.70/month, or less with auto-stop). Not 512 MB: a repair holds
the whole upload in memory by design (the upload bound is the in-memory buffering threshold, see
`UploadLimit`), the default bound is 20 MB, and the FIT SDK allocates on top of that while
reading and writing. 512 MB would probably hold, but an out-of-memory kill during somebody's
repair is a bad way to find out. Drop to 512 MB later if the numbers say it is safe.

Region: `fra` (Frankfurt) or `waw` (Warsaw) — closest to Kyiv testers of the European regions.

Deploy steps, once (nothing below has been run):

1. `fly auth signup` / `fly auth login`.
2. `fly launch --no-deploy` in the repository root. Keep the generated `fly.toml` in the repo:
   it is the deployment's only configuration, and it belongs under review like everything else.
3. In `fly.toml`: `internal_port = 8080`, `force_https = true`, `auto_stop_machines = "stop"`,
   `auto_start_machines = true`, `min_machines_running = 0`, one machine, 1 GB memory,
   `[[vm]] cpu_kind = "shared"`. Add `[checks]` pointing at `/health`, which already exists and
   is what the image's own healthcheck uses.
4. `FITFIX_MAX_UPLOAD_BYTES` and the rate-limit variables of §2 go in `[env]`, not in a secret:
   none of them is a credential.
5. `fly deploy`.
6. Point a hostname at it (`fly certs add fitfix.example`) or share the `*.fly.dev` name for the
   beta. The first ten single-hostname certificates are free.

How a new image reaches it afterwards: `fly deploy` from a checkout, or a GitHub Actions job on
`main` that runs `flyctl deploy --remote-only` with a `FLY_API_TOKEN` secret. Keep it manual
until the beta is actually running — CI already builds and smoke-tests the image, so an automatic
deploy adds a way to ship a bad build at 2 a.m. and nothing else.

Fallback if Ilya would rather own the box: **Hetzner CPX02 in Helsinki, €5.49/month, with Caddy**
in front for HTTPS. It is a defensible choice — fixed price, 20 TB of traffic, room for whatever
else he wants to run — but check availability first, because the order page refuses the CPX plans
today, and budget an evening for Docker, Caddy, unattended upgrades, a firewall that exposes only
80, 443 and ssh, and a deploy script. In that shape Caddy terminates TLS and proxies to
`127.0.0.1:8080`, and the container is published on loopback only, never on `0.0.0.0`.

Not recommended: Render at $7 for less memory than Fly's $6.70 and a 5 GB egress allowance;
Railway because metered CPU and RAM give no answer to "what will this cost me a month".

---

## 2. Hardening the public service

The honest summary of today's state: the input bound, the failure contract and the
no-geodata-in-logs rule are already there and tested. What is missing is everything that assumes
a stranger on the other end — nothing stops one IP from asking for a thousand repairs, nothing
stops a slow client from holding a request open, and the service sends no security headers at all.

### 2.1 What already holds

- **Upload limit.** `FITFIX_MAX_UPLOAD_BYTES`, default 20 MB, applied in three places at once:
  Kestrel's `MaxRequestBodySize`, the form reader's `MultipartBodyLengthLimit`, and
  `MemoryBufferThreshold` so no part ever spills to disk (AD-13, AD-3). Clamped to `int.MaxValue`.
  A refusal arrives as `FILE_TOO_LARGE` with 413. **20 MB stays** for the beta: a four-hour FIT
  file is a couple of megabytes, and the number is one `[env]` entry away from being lowered if
  the bill says otherwise. Worth lowering to 8 MB at the same time as §2.2 if we want a tighter
  blast radius — a decision, not a requirement.
- **No geodata in logs (AD-9).** One request line per request: method, matched route pattern,
  status, duration, declared input size, code. Not the path (it arrives percent-decoded and could
  carry a file name), not a coordinate, not a file's contents. Control characters are stripped and
  the value is truncated. The framework's own request logging is pinned to `Warning` in
  `appsettings.json` so it cannot leak a URL at information level.
- **Stateless (AD-3, AD-11).** Read-only filesystem, tmpfs `/tmp`, no volume, unprivileged user,
  and CI asserts all three on the running container.
- **One origin (AD-12).** No CORS surface at all: the page and the API share an origin, so there
  is no cross-origin policy to get wrong.

### 2.2 Per-IP rate limiting — missing (PR 2)

`AddRateLimiter` is not wired up. For a public URL it is the one thing that cannot be left out:
a repair is CPU-bound and synchronous within the request (AD-11), so a handful of parallel
repairs is the whole machine.

Plan:

- `RateLimitOptions` beside `UploadLimit`, same shape: every number read from configuration with a
  default, so a deployment can change it without a rebuild and no threshold lives in a signature
  (the spirit of AD-10 applied to the shell).
- Partition on the client IP, which behind a proxy means `UseForwardedHeaders` with
  `KnownProxies`/`KnownNetworks` set for the platform — otherwise every request partitions on the
  proxy's address and the limiter becomes a global one. This is why forwarded headers belong in
  the same PR as the limiter.
- Two named policies and one queue:
  - `/api/inspect`: fixed window, **60 requests per minute per IP**. It reads a file's metadata
    and returns; the generous number is deliberate, because the page inspects on every drop.
  - `/api/repair`: fixed window, **20 requests per 5 minutes per IP**. A tester repairing the
    same run four times in a row must not meet a wall; a script must.
  - A **concurrency limiter of 2 with a queue of 4** across both repair and inspect, so a burst
    waits instead of thrashing a 1 vCPU machine.
- `/health` and the static files are not rate-limited: the platform polls `/health`, and the SPA
  is a dozen cacheable files.
- Rejection goes out through `ProblemDetailsFactory` like every other refusal (AD-22), with a new
  code `TOO_MANY_REQUESTS` in `DiagnosticCodes`, mapped to 429 in `FailureStatus` both ways, a
  Ukrainian sentence in `i18n/uk.ts` (the dictionary test fails until it has one, AD-18), and a
  `Retry-After` header.
- **The IP is never logged.** It is a partition key and nothing else. AD-9 is about geodata, but
  an address is personal data on the same footing, and the request line has no field for it. A
  test asserts the rate-limit refusal logs its code and no address.

Known limit: the counters live in the process, so two machines mean two sets of counters. For a
beta on one machine that is correct, and it is one more reason to keep `min_machines_running` at
one.

### 2.3 Request timeouts — missing (PR 3)

Nothing bounds how long a request may take. Kestrel's defaults already cover the slow-body case
(`MinRequestBodyDataRate` 240 bytes/s, a 30 s header timeout), so the gap is the handler itself.

- `AddRequestTimeouts` / `UseRequestTimeouts`, default **30 s**, a `repair` policy at **120 s**.
  Epic 3's budget for a repair is about three seconds; 120 s is "something is wrong", not a
  target.
- A timeout leaves as 504 with a new `REQUEST_TIMED_OUT` code and its Ukrainian text, because
  `FailureStatus.ForStatus` currently folds every 5xx into `INTERNAL_ERROR` and "it took too
  long" is not "we broke".
- Kestrel: `AddServerHeader = false` (the version of the server is nobody's business),
  and keep `KeepAliveTimeout` at its default.

### 2.4 HTTPS and security headers — missing (PR 3)

TLS is terminated by the platform (Fly, or Caddy on a VPS) and the container keeps speaking HTTP
on 8080. So:

- `UseForwardedHeaders` (from PR 2) makes `Request.IsHttps` true, which is what lets HSTS be
  emitted correctly. **No `UseHttpsRedirection` in the container** — the platform redirects
  (`force_https` on Fly, Caddy's own redirect), and a redirect inside the container behind a
  terminating proxy is how you get a loop.
- A small `SecurityHeaders` middleware, inside the failure handler so a problem document carries
  the headers too:
  - `Strict-Transport-Security: max-age=31536000; includeSubDomains`, on HTTPS requests only.
  - `Content-Security-Policy`: `default-src 'self'; img-src 'self' data: https://tile.openstreetmap.org;
    style-src 'self' 'unsafe-inline'; script-src 'self'; connect-src 'self'; font-src 'self';
    object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'`.
    `'unsafe-inline'` for styles is Leaflet: it positions tiles and panes with `style` attributes,
    which `style-src` governs. The tile host is named explicitly, which also documents the one
    outbound thing the page does.
  - `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`,
    `Cross-Origin-Opener-Policy: same-origin`, `Cross-Origin-Resource-Policy: same-origin`,
    `Permissions-Policy: geolocation=(), camera=(), microphone=()`.
  - `Referrer-Policy: strict-origin-when-cross-origin` — **and not `no-referrer`**. See §3: the
    OSM tile policy expects a valid `Referer` on tile requests and names a restrictive
    `Referrer-Policy` as a prohibited practice. This is the one security header where the strictest
    value is the wrong one.
- Tests: the headers are asserted on a page response and on a problem document, and the CSP's
  tile host is asserted against the URL `RouteMap.tsx` actually uses, so moving one without the
  other fails.

### 2.5 Statelessness, re-checked for a public URL

Nothing in §2.2–2.4 writes anything. The rate limiter's counters are in-process and hold a hashed
partition key, no file names and no coordinates; they vanish with the process, like everything
else here. The read-only filesystem of `compose.yaml` and the CI assertions that go with it stay
exactly as they are, and the same flags belong on whatever the provider runs.

Not in scope for the beta, named so nobody has to wonder: no accounts, no logging of request
bodies, no error-reporting service (a stack trace from this service can carry file-shaped data),
no analytics. If uptime monitoring is wanted, point it at `/health` and nothing else.

---

## 3. OpenStreetMap tiles: can we share the link?

Read 2026-11-28 from the OSMF tile usage policy (<https://operations.osmfoundation.org/policies/tiles/>).

**Verdict: yes, as the page is written today. No key and no self-hosted tile server is needed
before sharing the link with a few dozen testers.**

The policy gives no numeric request limit and no user-count threshold. It permits "normal
interactive viewing by a human where the client requests only the tiles needed for the current
viewport", which is exactly what `RouteMap.tsx` does: one map, shown after a repair, panned and
zoomed by hand. What it prohibits is bulk downloading — "any pre-emptive fetching of tiles other
than those a user is actively viewing" — pre-seeding, offline downloads, wide automated scans,
and headless bots that pan the map to force rendering. We do none of it.

What we already satisfy:

- The HTTPS `tile.openstreetmap.org/{z}/{x}/{y}.png` URL exactly, which the policy requires.
- Attribution is visible: `MapContainer` turns off Leaflet's default control and renders an
  explicit `<AttributionControl prefix={false} />` with "© Учасники OpenStreetMap" linking to
  the copyright page. It is not hidden behind a toggle or under other UI.
- The browser's own `User-Agent` and `Referer`, and the browser's default caching, which the
  policy says already meets its requirements. We send no `Cache-Control: no-cache` for tiles.
- Darkening the tiles with a CSS filter is not addressed by the policy either way — it has no
  clause about altering or restyling raster tiles. The attribution stays legible, which is the
  requirement that does exist.

What the beta must therefore not do, as rules rather than suggestions:

1. **No restrictive `Referrer-Policy`.** `no-referrer` and `same-origin` would stop the `Referer`
   reaching the tile host, which the policy names as prohibited. §2.4 uses
   `strict-origin-when-cross-origin`. The `rel="noreferrer"` on the attribution link is fine — it
   governs that link, not the tile requests.
2. **No prefetching, no "download the map", no warming the viewport.** Not now and not in Epic 3.
3. **Do not hard-code the tile URL as the only option.** The policy recommends against it and the
   reason is practical: access "may be blocked without notice", so the URL should be one
   `[env]`-shaped change away from another provider. Small follow-up, not a blocker.

Recommended, cheap, and worth doing while the privacy note is being written (PR 4): a contact
address on the page, and a "report a map issue" link to <https://www.openstreetmap.org/fixthemap>,
both of which the policy asks for in its recommendations.

If the beta ever grows past a handful of people a day, or if tiles start failing, the exits are a
community raster provider from the OSM wiki, a keyed commercial provider, or self-hosting — in
that order of effort. None of them is needed to share a link this week.

---

## 4. The privacy note (PR 4)

One page, in the repository as `docs/PRIVACY.md` (English, like every artifact written after
2026-11-19), with its substance on the page itself in Ukrainian, so a tester reads it where they
are rather than on GitHub.

What it says, all of it true of the service as built and each sentence traceable to a decision
above:

- Two files are uploaded over HTTPS and held in memory for the length of one request.
- Nothing is written to disk. The container's filesystem is read-only, there is no volume, and the
  upload buffering threshold is the upload limit precisely so that no part of a file can spill to
  a temporary file (AD-3, AD-13).
- The repaired file is returned in the response and kept nowhere. Closing the tab loses it.
- Logs hold one line per request: method, route, status, duration, size in bytes, and a code. No
  coordinates, no file names, no file contents, no IP address (AD-9, §2.2).
- There is no account, no cookie, no analytics and no third-party script.
- The map is drawn in the browser; its tiles come from OpenStreetMap, which therefore sees the
  requests for the tiles covering the route, and the service passes OpenStreetMap nothing.
- Rate limiting counts requests per IP in memory, for minutes, and stores nothing.
- A contact address for questions, and the fixthemap link from §3.

In the page: a footer under the hero and the result alike, with one sentence — the files are
processed in memory and stored nowhere — and a link to the full note. Text in `i18n/uk.ts`, colour
and spacing from `theme.css` tokens only, no new hex (the theme test enforces it).

---

## 5. What Ilya has to decide

**The provider.** Everything in §2 and §4 is written to be provider-independent and lands first;
the only thing waiting on an answer is `fly.toml` (or the Hetzner box and its Caddyfile), which is
why no account has been created and nothing has been bought.

- Fly.io, shared-cpu-1x 1 GB, ~$6.70/month and less with auto-stop — the recommendation.
- Hetzner CPX02 Helsinki, €5.49/month plus an evening of setup and ownership of a Linux box —
  if he would rather own it. Check availability first.
- Render Hobby, $7/month — if the simplest possible GitHub-to-running-service path is worth the
  premium and the 5 GB egress cap.

Two smaller calls that can wait for the first deploy: whether the upload bound drops from 20 MB,
and whether the beta runs on a `*.fly.dev` name or a domain of his own.

### Pull requests this plan asks for

1. This document.
2. Per-IP rate limiting, forwarded headers, `TOO_MANY_REQUESTS`.
3. Request timeouts, security headers, `REQUEST_TIMED_OUT`, no server header.
4. The privacy note and its footer.

Then, once the provider is chosen, a fifth: the deployment's own configuration and a README
section saying how to deploy and how a new image gets there.

### Sources

- <https://www.hetzner.com/cloud/> and <https://www.hetzner.com/cloud/regular-performance/>
- <https://docs.fly.io/about/pricing>
- <https://render.com/pricing>
- <https://railway.com/pricing>
- <https://operations.osmfoundation.org/policies/tiles/>
