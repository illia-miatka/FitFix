# Deploying FitFix to Cloud Run

Written 2026-11-28. The provider question of the public-beta plan (§5) was answered: **Google
Cloud Run**. This is the runbook for getting the existing image there and keeping the bill
predictable.

Nothing here has been run. The account, the card, the project and the budget are Ilya's to
create; every command below is written so it can be read before it is pasted.

What this expects: `gcloud` installed and logged in (`gcloud auth login`), Docker running
locally, and a Google Cloud project with billing enabled.

---

## 0. The settings this deployment uses, and why

| Setting | Value | Why |
|---|---|---|
| Region | `europe-central2` (Warsaw) | Closest Cloud Run region to Kyiv. `europe-west3` (Frankfurt) is the alternative. Artifact Registry must be in the same region. |
| `--port` | `8080` | Cloud Run sends requests to 8080 unless told otherwise, and the image already listens there. The service now also honours `PORT` if the platform names another one. |
| `--cpu` | `1` | A repair is CPU-bound and runs inside the request (AD-11). |
| `--memory` | `1Gi` | An upload is held in memory by design and the limit is 20 MB, with the FIT SDK allocating on top. Cloud Run's own filesystem is in-memory too, so memory is the only resource that matters here. |
| `--min-instances` | `0` | Nothing runs when nobody is testing, which is the whole reason this is affordable. Costs a cold start: see §5. |
| `--max-instances` | `1` | The cap that makes the bill bounded, and the thing that makes the rate limiting exact — see §4. |
| `--concurrency` | `4` | The service serves 2 `/api` requests at once and queues 4 (`FITFIX_RATE_CONCURRENCY`, `FITFIX_RATE_QUEUE`). Cloud Run's default is multiple concurrency with a much higher number, which would hand one instance more requests than it will accept and turn honest load into 429s. |
| `--timeout` | `150` | Longer than the service's own repair timeout of 120 s, so **our** 504 arrives with `REQUEST_TIMED_OUT` in it rather than Cloud Run's bare one. Cloud Run's default is 300 s, its maximum 3600 s. |
| `FITFIX_TRUSTED_PROXIES` | `any` | A Cloud Run instance is reachable only through Cloud Run's own ingress, so the peer is always that ingress. |
| `FITFIX_FORWARD_HOPS` | `2` | Google's front end appends two entries to `X-Forwarded-For`: the client's address and its own forwarding rule's, in that order, and its documentation says the entries before those two are not verified. Reading two from the right lands on the client whatever a caller put in front of it. One hop would partition the rate limit on the front end instead — one bucket for everybody. |

`FITFIX_FORWARD_HOPS=2` is safe even if Cloud Run's ingress turns out to append only one entry:
asking for more hops than the header has stops at the leftmost entry, which in that case is the
client. It is also not something a caller can exploit — a forged entry stays to the left of the
two Google appends. A test (`PlatformContractTests`) pins both halves of that.

---

## 1. Enable the APIs, once

```bash
gcloud services enable run.googleapis.com artifactregistry.googleapis.com
```

## 2. A place for the image

```bash
gcloud artifacts repositories create fitfix --repository-format=docker --location=europe-central2 --description="FitFix container images"
```

```bash
gcloud auth configure-docker europe-central2-docker.pkg.dev
```

## 3. Build, push, deploy

Set these once per shell (`PROJECT_ID` is the project's id, not its display name):

```bash
export PROJECT_ID="$(gcloud config get-value project)" REGION=europe-central2 IMAGE_TAG="$(git rev-parse --short HEAD)"
```

**`--platform linux/amd64` is not optional on an Apple Silicon Mac.** Docker would otherwise
build an arm64 image, push it happily, and the deployment would fail with an architecture error
that says nothing about your laptop.

```bash
docker build --platform linux/amd64 --tag "$REGION-docker.pkg.dev/$PROJECT_ID/fitfix/fitfix:$IMAGE_TAG" .
```

```bash
docker push "$REGION-docker.pkg.dev/$PROJECT_ID/fitfix/fitfix:$IMAGE_TAG"
```

```bash
gcloud run deploy fitfix --image "$REGION-docker.pkg.dev/$PROJECT_ID/fitfix/fitfix:$IMAGE_TAG" --region "$REGION" --allow-unauthenticated --port 8080 --cpu 1 --memory 1Gi --min-instances 0 --max-instances 1 --concurrency 4 --timeout 150 --set-env-vars FITFIX_TRUSTED_PROXIES=any,FITFIX_FORWARD_HOPS=2
```

`--allow-unauthenticated` is what makes it a public beta: without it every request needs a Google
identity. It is also the one flag here that cannot be undone by a redeploy you forgot — check it
is what you meant.

The command prints the service URL (`https://fitfix-….run.app`). That is the link for testers.

### Checking it

```bash
curl -fsS https://fitfix-xxxxx.run.app/health
```

```bash
curl -fsS -D - -o /dev/null https://fitfix-xxxxx.run.app/ | grep -iE 'content-security-policy|referrer-policy|strict-transport-security'
```

The second one is worth doing once: `Strict-Transport-Security` appears only when the service can
see that the request arrived over HTTPS, which it learns from the forwarded headers. If that line
is missing, `FITFIX_TRUSTED_PROXIES` did not take effect, and the rate limiting is partitioned on
Cloud Run's ingress rather than per client.

## 4. The guards on the bill

Cloud Run has a free monthly allowance and bills per request and per resource-second after it,
but billing has to be enabled — a card on file — before anything can be deployed. So the
protection is not the free tier. It is these, in order:

1. **`--max-instances 1`.** The hard ceiling on how much can run at once, and therefore on the
   bill. A beta that goes viral gets slow instead of expensive.
2. **The rate limiting of #28.** 20 repairs per five minutes per client, 60 inspects a minute,
   two at a time. That is what stops one caller from using the whole instance.
3. **A budget alert.** It emails; it does not cap. Nothing in Google Cloud stops spending on a
   threshold, so this tells you to go and turn something off.

```bash
gcloud billing budgets create --billing-account=BILLING_ACCOUNT_ID --display-name="FitFix beta" --budget-amount=10USD --threshold-rule=percent=0.5 --threshold-rule=percent=0.9 --threshold-rule=percent=1.0
```

Find `BILLING_ACCOUNT_ID` with `gcloud billing accounts list`; the budget API
(`billingbudgets.googleapis.com`) has to be enabled first. Set the amount to what you would be
annoyed to lose, not to what you expect to spend.

**In-memory rate limiting is per instance.** The counters live in the process, so raising
`--max-instances` to 2 doubles every limit in practice: two instances, two sets of counters, and
a client whose requests land on both gets both budgets. For a beta keep it at 1. If it ever has
to go higher, the limits either get divided by the instance count or move to something shared,
and that is a decision, not a flag.

## 5. The cold start, and what a tester sees

With `--min-instances 0` nothing is running between sessions, so the first request after an idle
spell starts a container: the Alpine ASP.NET image plus this service, a second or two before the
page answers. Every request after it is warm. For a beta this is the right trade — the
alternative, `--min-instances 1`, means instance-based billing and a machine running all night
for nobody.

The service keeps nothing between requests (AD-3), so an instance starting, stopping or being
replaced mid-beta loses nothing but the rate-limiting counters.

## 6. What is different from `docker compose up`

- **The filesystem is not read-only.** `compose.yaml` mounts it read-only, which is what turns a
  regression in AD-3 into a crash; Cloud Run has no such flag, and its filesystem is in-memory
  and writable. Nothing in the service writes, and the upload's in-memory buffering threshold is
  the upload limit precisely so a part cannot spill to a file (`UploadLimit`) — but on Cloud Run
  that guarantee is the code's alone. CI still asserts the read-only behaviour on the image, so a
  regression is caught before it ships rather than in production.
- **TLS is terminated in front of the service**, so HSTS and the per-client rate limit both
  depend on the forwarded headers of §0.
- **No `docker compose`.** The deployed thing is the image, named by its tag. Keep deploying by
  commit tag, never `latest`: a rollback is then `gcloud run deploy` with the previous tag, or
  `gcloud run services update-traffic fitfix --to-revisions=PREVIOUS=100`.

## 7. Deploying from CI instead

`.github/workflows/deploy.yml` does the same three steps on a runner, and runs **only when
started by hand** (`workflow_dispatch`) — nothing deploys on a push to `main`. It needs four
repository settings, and until they exist the workflow fails on the authentication step and
changes nothing:

| Name | Kind | What |
|---|---|---|
| `GCP_PROJECT_ID` | variable | The project id. |
| `GCP_REGION` | variable | `europe-central2`. |
| `GCP_WIF_PROVIDER` | secret | The workload-identity provider's full resource name. |
| `GCP_DEPLOY_SERVICE_ACCOUNT` | secret | The deploying service account's email. |

Workload identity federation rather than a service-account key: a key in a repository secret is a
credential that never expires and can be copied out. The setup is a service account with
`roles/run.admin`, `roles/artifactregistry.writer` and `roles/iam.serviceAccountUser`, a
workload-identity pool with a GitHub OIDC provider, and — the part people skip — an **attribute
condition restricting it to this repository**, because without one any GitHub Actions workflow
anywhere can assume the identity. Google's own instructions have the exact flags; they change
often enough that copying them into this file would be worse than linking it.

Leave CI deploys off until the beta is actually running. CI already builds and smoke-tests the
image on every push; an automatic deploy on top of that only adds a way to ship a bad build while
nobody is watching.

## 8. If the beta ends

```bash
gcloud run services delete fitfix --region europe-central2
```

Then delete the Artifact Registry repository, which is the only thing that keeps costing after
the service is gone (it stores images by the gigabyte-month).
