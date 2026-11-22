# Receipt Splitter

A bill splitter for group meals: **ASP.NET Core 10** minimal API, **Vue 3** frontend, and
**Azure** infrastructure in **Bicep**, designed to run for near $0: free tiers, scale-to-zero and a
budget kill switch. Scan or type a receipt, check every line, share one link, and each friend
claims what they had. The server computes every share in integer minor units and the fewest
transfers to settle up. No accounts; bills delete themselves 30 days after the last change.

<p align="center"><img src=".github/media/demo-desktop.gif" alt="The sample receipt on a laptop: pick a name, claim an item, drag two items onto friends, check your share, then Settle" width="900"></p>

<p align="center"><em>The sample bill on a laptop: pick a name, claim an item, drag items onto friends, check the share, then settle up.</em></p>

<details>
<summary>On a phone</summary>
<p align="center"><img src=".github/media/demo-phone.gif" alt="The same sample bill on a phone, using Mine buttons instead of drag and drop" width="320"></p>
</details>

## Features

- **Three ways in:** scan from the camera or gallery (two photos for a long receipt), type it in,
  or load the bundled sample (**Try the sample receipt**: a Ramen Nagi bill with 12 made-up names,
  no upload and no Document Intelligence call).
- **Nothing is auto-confirmed:** the organizer checks every line before sharing, and sharing stays
  blocked until each receipt matches its printed total.
- **Exact splits:** integer minor units with `checked` arithmetic and largest-remainder
  allocation (ties broken by people order), so every split sums to the receipt to the smallest
  coin.
- **Fees and discounts per receipt:** service charge, tax, tip and discounts split by what each
  person ordered or equally, and never touch another receipt. Senior citizen, PWD and other
  person-scoped discounts stay with their holders in every split mode.
- **Multi-receipt bills:** each receipt has its own fees, payer and list of who was there. Split by
  item, equally or by shares, and share a platter between people.
- **Settle up:** the fewest transfers across all receipts, a payment note on every card, an
  "I've paid" mark the receiver confirms, and a plain-text summary for the group chat.
- **Organizer controls:** join by link or QR, optional bill PIN, undo claims, lock or delete the
  bill.
- **Responsive:** phone, tablet and desktop layouts from 360 px to 1600 px, with drag-and-drop
  on desktop and taps on touch screens.

## Privacy

- **No accounts.** No email, phone number or password. A friend gives a name, scoped to the bill
  they joined.
- **Bills are isolated.** One blob per bill, 128-bit random ids, no route that lists or searches
  bills, and identical 404s for missing, deleted and expired bills.
- **Organizer key in the fragment.** The organizer link carries a separate 128-bit key after the
  `#`, so it never reaches server logs. The API stores only its SHA-256 hash and compares in
  constant time.
- **Receipt photos are never stored.** A photo is held in memory for one request and sent to
  Document Intelligence. The API then asks the service to delete the photo and its result; if
  that delete fails, the service drops both after 24 hours
  ([data and privacy notes](https://learn.microsoft.com/en-us/azure/foundry/responsible-ai/document-intelligence/data-privacy-security)).
  Tests assert the bytes never reach storage or logs.
- **Minimal data:** item names, prices, fees, discounts, shop name, date and total, plus people's
  names, claims and payment notes. Raw scan text, card digits and staff names are dropped.
- **Logs hold no personal data:** a salted hash of the bill id, never names, amounts or request
  bodies. Rate-limit counters use a salted IP hash and expire after 2 days.
- **`noindex` and `no-referrer`** on every page, so bill links don't reach search engines or
  other sites.
- **Retention:** a storage lifecycle rule deletes each bill **30 days after its last change**. The
  organizer can delete it at once, and **Forget this device** clears everything the app saved in
  the browser: the saved name, friends, recent bills, who you are in each bill, organizer keys
  and PIN tokens.

## Architecture

![Receipt Splitter architecture: the browser loads the Vue app from Static Web Apps and calls the .NET API on Container Apps, which keeps bills in Blob Storage and reads photos with Document Intelligence. GitHub Actions pushes the API image to GitHub Container Registry, and a budget kill switch can stop the API.](.github/media/architecture.svg)

- **Web:** Vue 3 + TypeScript + Vite on Static Web Apps (Free). It renders the breakdowns the API
  returns and never calculates money. Sync is manual (after your own changes and on **Refresh**)
  so the API can scale to zero; build with `VITE_AUTO_SYNC=true` to turn polling back on.
- **API:** ASP.NET Core 10 minimal API in vertical slices on Container Apps (Consumption, 0 to 1
  replicas). The money maths lives in `ReceiptSplitter.Core`, a pure C# library with no I/O.
- **Data:** one JSON blob per bill in Blob Storage (Standard LRS) with ETag concurrency, so two
  claims on the same item never overwrite each other: the loser gets `409` with the latest bill.
- **Scanning:** Azure AI Document Intelligence F0, prebuilt receipt model.
- **Identity:** managed identity to Blob Storage and Document Intelligence. No connection strings,
  account keys or API keys in code, config or GitHub secrets.
- **Delivery:** one manual GitHub Actions workflow signs in to Azure with OIDC, pushes a public
  image to GHCR and deploys Bicep after approval. A budget with an Automation runbook kill switch
  stops the API if spend passes the cap.

## Versions

| Component | Version | Notes |
|---|---|---|
| .NET | 10 (LTS) | `global.json` pins the 10.0.100 SDK with `latestFeature` roll-forward. |
| Node.js | 22 (LTS) | Frontend toolchain and CI. |

```
src/ReceiptSplitter.Core/   money maths, no I/O: Shared/ (Model, Money, Allocation), Receipts/, Bills/, Settlement/
src/ReceiptSplitter.Api/    minimal API: Features/ (one folder per endpoint), Infrastructure/ (Http, RateLimits,
                            Scanning, Security, Storage), Shared/ (stored bill and response shapes)
tests/                      ReceiptSplitter.Core.Tests and ReceiptSplitter.Api.Tests (xUnit)
web/                        Vue 3 + Vite + TypeScript: welcome/, home/, bill/, scan/, profile/, design/, shared/
infra/                      Bicep (main.bicep, modules/), runbooks/, setup, deploy, smoke and kill-switch scripts
samples/                    the bundled sample receipt
.github/                    ci-cd.yml (manual only), PR template, README media
```

## Versioning

Releases follow [Semantic Versioning](https://semver.org): `MAJOR.MINOR.PATCH`, starting at
`1.0.0`.

| Part | Bump when | Example |
|---|---|---|
| **MAJOR** | old bill links break, the stored bill format changes, or the API changes in a way the old web app can't use | Bill links move to a new address |
| **MINOR** | a backward-compatible feature | The bill PIN, the full breakdown |
| **PATCH** | a bug, copy or dependency fix | A rounding display fix |

- A release is a deploy with a version: `gh workflow run ci-cd.yml -f target=all -f version=1.1.0 --ref main`.
- The workflow validates the version first (`X.Y.Z`, no leading zeros, from `main`, a deploy
  target, not released before, higher than the last release). After every deploy job and the
  smoke test pass, it tags the commit `vX.Y.Z` and publishes a GitHub Release with notes from the
  PRs merged since the last one. A failed deploy gets no tag. The API image also gets the version tag.
- `/healthz` and the home screen show the running version. An unversioned deploy shows the last
  release plus the commit (`1.1.0+abc1234`); local runs show `0.0.0-dev` (API) and `dev` (web).

## Develop

No cloud account or secret is needed for day-to-day work.

**Requirements:** .NET SDK 10, Node 22. Optional:
[Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite) for local Blob
Storage.

```sh
# API on http://localhost:5080
dotnet test
dotnet run --project src/ReceiptSplitter.Api

# Frontend on http://localhost:5173 (proxies /api and /healthz to the API)
cd web
npm ci
npm test
npm run dev
```

API settings come from `appsettings*.json` and can be overridden with environment variables
(`Section__Key`):

| Setting | Default | Effect |
|---|---|---|
| `Storage__ConnectionString` | `UseDevelopmentStorage=true` (Development) | Azurite. Empty keeps bills in memory. |
| `Storage__BlobServiceUri` | empty | Production blob endpoint, reached with managed identity. |
| `Scans__UseSample` | `false` | Every scan reads as the bundled receipt, with one price misread on purpose to exercise the total check. |
| `DocumentIntelligence__Endpoint` | empty | Without it (and without `Scans__UseSample`) scanning answers "not available". Production refuses to start without it. |
| `Scans__DailyCap` / `Scans__PerIpPerHour` | `15` / `3` | Keeps Document Intelligence inside F0. |

Web build-time variables ([`web/.env.example`](web/.env.example)): `VITE_API_BASE_URL` (the
production API address, set by the deploy workflow) and `VITE_AUTO_SYNC` (`true` turns polling on).

**Screenshots:** `npm run screens` (in `web/`) captures every screen at 360, 390, 768, 1024, 1280
and 1600 px, light and dark, into `web/screens/` (git-ignored), and fails if a page scrolls
sideways. Requests are served from `web/scripts/fixtures/` (rebuild with
`node scripts/make-fixtures.mjs` against a running API). First run: `npx playwright install chromium`.
Local only.

### API walkthrough

These calls mirror the deploy smoke test in [`infra/smoke.sh`](infra/smoke.sh). Money on the wire
is integer minor units (centavos for PHP).

```sh
API=http://localhost:5080

# Create a bill from the sample. The organizer key is returned once; only its hash is stored.
curl -s -X POST "$API/api/bills" -H 'content-type: application/json' -d '{"source":"sample"}'
# { "id": "34KxpQWuvJRTl4FlhoNgaP", "organizerKey": "2yp6N5rZzE9dnHQuhTxshm" }

ID=<id>; KEY=<organizerKey>

# Read the bill and its computed breakdown. 525580 = ₱5,255.80.
curl -s "$API/api/bills/$ID" | jq '{title: .bill.title, total: .breakdown.receipts[0].computedTotal}'
```

Responses carry an `ETag`; send it back in `If-None-Match` and an unchanged bill answers `304`.

Pick a person, then claim a unit with the `X-Person-Token` returned by `pick`:

```sh
PERSON=$(curl -s "$API/api/bills/$ID" | jq -r '.bill.people[1].id')   # Ben
UNIT=$(curl -s "$API/api/bills/$ID" | jq -r '.bill.receipts[0].lines[0].units[0].id')

TOKEN=$(curl -s -o /dev/null -D - -X PATCH "$API/api/bills/$ID" \
  -H 'content-type: application/json' \
  -d "{\"op\":\"pick\",\"personId\":\"$PERSON\"}" \
  | tr -d '\r' | awk -F': ' 'tolower($1) == "x-person-token" { print $2 }')

curl -s -X PATCH "$API/api/bills/$ID" \
  -H 'content-type: application/json' -H "X-Person-Token: $TOKEN" \
  -d "{\"op\":\"claim\",\"unitId\":\"$UNIT\",\"personId\":\"$PERSON\",\"from\":[]}" \
  | jq --arg p "$PERSON" '.breakdown.receipts[0].people[] | select(.personId == $p) | {subtotal, total}'
# { "subtotal": 48500, "total": 34774 }

# Delete as the organizer
curl -s -X DELETE "$API/api/bills/$ID" -H "X-Organizer-Key: $KEY"
```

`total` is the ₱485.00 item plus Ben's equal share of the service charge, less his share of the
40% discount. `from` is the claim state the client last saw; if it no longer matches, nothing is
written and the API answers `409` with the latest bill.

## CI/CD

| Workflow | Runs on | Does |
|---|---|---|
| `ci-cd.yml` | manual only (`workflow_dispatch`) | tests, then the selected deploy target after `production` approval |

```sh
gh workflow run ci-cd.yml -f target=test    # tests only, any branch
gh workflow run ci-cd.yml -f target=infra   # Bicep
gh workflow run ci-cd.yml -f target=api     # build and push the API image, roll it out, wait for /healthz
gh workflow run ci-cd.yml -f target=web     # build and upload the Vue app
gh workflow run ci-cd.yml -f target=api-web # api, then web, without infra
gh workflow run ci-cd.yml -f target=all     # infra, then api, then web
```

Nothing runs on push or pull request, to save Actions minutes. Tests always run first. Deploy and
smoke jobs run only from `main`, in the `production` environment after approval, and sign in to
Azure with OIDC; no other job can reach Azure. The smoke job starts after the deploys, so it asks
for its own approval. It creates the sample bill, claims an item, checks the totals and deletes it.

**Rollback:** every API image is tagged with its commit (and a release with its version). Point the
Container App back at a previous one:

```sh
az containerapp update -g <resource-group> -n receipt-splitter-api \
  --image ghcr.io/mrk-jsph-dev/receipt-splitter-api:<previous-version-or-commit-sha>
```

The new revision must pass `/healthz` before it takes traffic. For the web app, revert the commit
on `main` and run `target=web`. Details in [`infra/README.md`](infra/README.md), "Rollback".

## First-time Azure setup (owner only)

Follow [`infra/README.md`](infra/README.md): OIDC federation, the `production` environment with
its secrets (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `BUDGET_ALERT_EMAIL`)
and the `AZURE_RESOURCE_GROUP` variable. Then run `target=all` and make the
`receipt-splitter-api` package public so the Container App pulls it without a credential.

### Operations

- **Health:** `/healthz` reports the running version.
- **Kill switch by hand:**

  ```sh
  RG=<resource-group> infra/kill-switch.sh off      # stops answering within seconds
  RG=<resource-group> infra/kill-switch.sh status   # running state and /healthz
  RG=<resource-group> infra/kill-switch.sh on       # start, then run the smoke test
  ```

- **After an automatic stop:** find the spend in Cost Management > Cost analysis on the resource
  group, then run `kill-switch.sh on`. The API stays off, even into the next month, until you do.
  While it is off the web app shows "Can't reach the server" and deploys refuse to run.

## Repository settings (owner checklist)

- Ruleset on `main`: require a pull request, block force pushes and deletion. `CODEOWNERS` names
  the owner for every path.
- `production` environment: owner as required reviewer, deployment branches limited to `main`.
  It holds the Azure ids and the alert email as environment secrets; the only repository variable
  is the resource group name.
- Actions → General: only GitHub-owned actions plus `azure/login`, `docker/login-action` and
  `Azure/static-web-apps-deploy`; full-SHA pinning required; read-only default token; approval
  required for all outside contributors.
- Code security: secret scanning with push protection, Dependabot alerts, Dependabot security
  updates and private vulnerability reporting. No CodeQL. See [`SECURITY.md`](SECURITY.md).

## Cost guardrails

Near $0, not guaranteed $0 (prices checked November 2026):

| Resource | Plan | Free allowance | What could cost money |
|---|---|---|---|
| Static Web App | Free | Hosting | Nothing on Free |
| Container App (API) | Consumption, 0 to 1 replicas | 180,000 vCPU-s, 360,000 GiB-s and 2 million requests per subscription per month | Traffic that keeps it awake past the grant, or over 2 million requests (rejected requests still count; health probes don't) |
| Container Apps environment | Consumption only | No charge, no logs destination | A Dedicated workload profile or Log Analytics |
| Document Intelligence | F0 | 500 pages a month | Nothing: F0 can't bill. Scans are capped at 15 a day and 3 per IP per hour (465 in a 31-day month) |
| Blob Storage | Standard LRS | None | Storage and operations; small JSON blobs cost cents a month at most |
| Automation account (kill switch) | Basic | 500 job minutes a month | Over 500 minutes; one stop takes about a minute |
| Action group (kill switch) | Webhook | 100,000 webhook calls a month | Over 100,000 calls; one per budget breach |
| Container image | GHCR, public | Free | Nothing |

Sources: [Static Web Apps](https://azure.microsoft.com/pricing/details/app-service/static/),
[Container Apps billing](https://learn.microsoft.com/azure/container-apps/billing),
[Document Intelligence](https://azure.microsoft.com/pricing/details/ai-document-intelligence/),
[Blob Storage](https://azure.microsoft.com/pricing/details/storage/blobs/),
[Automation](https://azure.microsoft.com/pricing/details/automation/),
[Azure Monitor](https://azure.microsoft.com/pricing/details/monitor/).

A monthly budget on the resource group (`budgetAmount = 1` in `infra/main.bicepparam`, in the
billing currency) emails `BUDGET_ALERT_EMAIL` when actual spend passes 80% and 100%, and when the
forecast passes 100%. At 100% actual spend it also calls an action group, whose webhook starts an
Automation runbook (`infra/runbooks/stop-api.ps1`) that stops the Container App. Cost data can lag up to a day and budgets are
evaluated daily, so this caps the damage rather than catching a spike in real time.

Trade-off: with the API scaled to zero, the first request after an idle period waits for a cold
start.

## Contributing

Issues are welcome. For a pull request: one change per PR, tests first for anything touching money
or security (integer minor units, `checked` arithmetic, never `double` or `float`), `dotnet test`
and `cd web && npm ci && npm test` passing, and no databases, accounts, real-time services or
always-on compute. Never commit real receipt photos, real names, keys or connection strings. CI
runs only when a maintainer starts it.

## License

MIT. See [`LICENSE`](LICENSE).
