# Receipt Splitter

**Snap the receipt, share one link, and everyone taps what they had.**

[![ci-cd](https://github.com/mrk-jsph-dev/receipt-splitter/actions/workflows/ci-cd.yml/badge.svg)](https://github.com/mrk-jsph-dev/receipt-splitter/actions/workflows/ci-cd.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![Vue 3](https://img.shields.io/badge/Vue-3-42b883)
<!-- [PLACEHOLDER] version badge: add once there is a first release tag. -->

**Live:** https://icy-hill-027848400.3.azurestaticapps.net

## Overview

Splitting a group dinner by hand means a photo of the receipt in the group chat, someone doing
maths on a calculator, and a discount or service charge that nobody shares out fairly. Receipt
Splitter reads the receipt, lets you check every line, and gives the table one link. Friends open
it, tap what they had, and see what they owe, to the smallest coin. Then it shows who pays whom
with the fewest transfers.

There are no accounts. Friends join by link. Bills delete themselves 30 days after the last change.

**Who it's for:** anyone splitting a bill with friends, and developers who want to see a small,
near-$0 Azure app built with .NET 10 and Vue.

<p align="center"><img src="docs/media/demo-desktop.gif" alt="Trying the sample receipt on a laptop: pick a name, claim an item, drag two items onto friends, check your share, then Settle" width="900"></p>

<p align="center"><em>The sample bill on a laptop: pick your name, claim an item, drag items onto friends, check your share, then settle up.</em></p>

<details>
<summary>See it on a phone</summary>
<p align="center"><img src="docs/media/demo-phone.gif" alt="The same sample bill on a phone, using Mine buttons instead of drag and drop" width="320"></p>
</details>

### Try it in under a minute

1. Open the [live app](https://icy-hill-027848400.3.azurestaticapps.net) and tap
   **Try the sample receipt**. No upload and no AI call: it builds a bill from a bundled Ramen Nagi
   receipt with made-up names (Ana to Lia, 12 people).
2. Pick a name under **Who are you?** and tap **Mine** on a couple of items.
3. Tap **Breakdown** to see your share, with the discount and service charge applied.
4. Open **Settle** to see who pays whom once everything is claimed.

## Key features

- **Start any way you like.** Scan a receipt from the camera or gallery (two photos for a long
  one), type it in, or try the sample.
- **Nothing is guessed.** You check every line before sharing, and sharing stays locked until
  each receipt matches its printed total.
- **Totals always add up.** Every split sums exactly to the receipt, down to the centavo, cent or
  yen. Server-side maths, covered by tests.
- **Fair fees and discounts.** Service charge, tax, tip and discounts split by what each person
  ordered or equally. Senior citizen and PWD discounts stay with the card holder.
- **Real dinners, not just one receipt.** Several receipts in one bill, each with its own fees,
  payer and list of who was there. Split by item, equally, or by shares, and share a platter
  between people.
- **Settle up fast.** The fewest transfers across all receipts, a payment note on every card,
  "I've paid" that the receiver confirms, and a plain-text summary to paste in the group chat.
- **Join by link or QR.** No sign-up. The organizer can add an optional PIN, undo claims, lock
  the bill or delete it.
- **Works on any screen.** Phone, tablet and desktop layouts from 360 px to 1600 px, with
  drag-and-drop on desktop and taps on touch screens.

## Privacy

- **No accounts.** The app never asks for an email, phone number or password. A name is all a
  friend gives, and only inside the bill they joined.
- **Each bill is private to its link.** Bill ids are 128-bit random values. There is no list or
  search of bills. A missing, deleted or expired bill all look the same: "This bill isn't here".
- **Editing needs a second link.** The organizer link carries a separate 128-bit key after the
  `#`, so it never reaches the server's logs. The server keeps only its SHA-256 hash.
- **Receipt photos are never stored.** A photo is held in memory for one request and sent to Azure
  AI Document Intelligence. The API then asks Document Intelligence to delete the photo and its
  result. If that delete fails, Microsoft deletes both after 24 hours on its own
  ([Microsoft's data and privacy notes](https://learn.microsoft.com/en-us/azure/foundry/responsible-ai/document-intelligence/data-privacy-security)).
  Tests check the bytes never reach our storage or logs.
- **Only what the split needs is kept:** item names, prices, fees and discounts, the shop name,
  date and total, plus people's names, claims and payment notes. Raw scan text, card digits and
  staff names are dropped.
- **Logs hold no personal data.** They carry a salted hash of the bill id, never names, amounts or
  request bodies. Rate-limit counters hold a salted hash of the IP and expire after 2 days.
- **Pages send `noindex` and `no-referrer`,** so bill links don't reach search engines or other sites.

### Data deletion

- Every bill deletes automatically **30 days after its last change**. A storage lifecycle rule does
  this, so there's no job to forget to run.
- The organizer can delete a bill at once with **Delete this bill now**.
- **Forget this device** (home screen) clears everything the app saved in the browser: your name,
  organizer keys and the list of bills on this device.

The full rules are in [`docs/SPEC.md`](docs/SPEC.md), sections "Privacy" and
"Many bills, nothing leaking between them".

## Tech stack

| Layer | Technology | Hosting |
|---|---|---|
| Web app | Vue 3, TypeScript, Vite, Vue Router, Lucide icons | Azure Static Web Apps (Free) |
| API | .NET 10 minimal API (ASP.NET Core) | Azure Container Apps (Consumption, scales to zero) |
| Money maths | Pure C# library (`ReceiptSplitter.Core`), integer minor units, no I/O | Runs inside the API |
| Storage | Azure Blob Storage, one JSON blob per bill, ETag concurrency | Standard LRS, lifecycle delete after 30 days |
| Receipt scanning | Azure AI Document Intelligence, prebuilt receipt model | F0 (free) tier |
| Infrastructure | Bicep | `infra/` |
| CI/CD | GitHub Actions, one manual workflow, OIDC sign-in to Azure | Image on GitHub Container Registry |
| Tests | xUnit (`dotnet test`), Vitest | Run by the workflow before any deploy |

Managed identity everywhere. No connection strings or keys in code, config or GitHub secrets.

## Architecture

[![Receipt Splitter architecture: the browser loads the Vue app from Static Web Apps and calls the .NET API on Container Apps, which keeps bills in Blob Storage and reads photos with Document Intelligence. GitHub Actions pushes the API image to GitHub Container Registry, and a budget kill switch can stop the API.](docs/media/architecture.svg)](https://icy-hill-027848400.3.azurestaticapps.net/architecture.html)

**[Open the interactive version](https://icy-hill-027848400.3.azurestaticapps.net/architecture.html)** to step through the flow with Play, or tap a box to see what it does.

The browser loads the Vue app from Static Web Apps and talks to the API directly. The API owns
all money maths and returns computed breakdowns, so the browser never calculates money. Bills live
as single JSON blobs with ETag checks, so two people claiming at once never overwrite each other,
and a budget-driven kill switch stops the API if spending ever passes the cap.

Sync is manual for now: a bill updates after your own changes and when you tap **Refresh**. This
lets the API scale to zero between uses. Build the web app with `VITE_AUTO_SYNC=true` to turn
polling back on.

## Getting started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (`global.json` pins 10.0.100 or later)
- [Node.js 22](https://nodejs.org/)
- Optional: [Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite) to
  run against local Blob Storage (see below)

### Install and run

```bash
git clone https://github.com/mrk-jsph-dev/receipt-splitter.git
cd receipt-splitter

# API on http://localhost:5080
dotnet test
dotnet run --project src/ReceiptSplitter.Api

# Web app on http://localhost:5173 (in a second terminal)
cd web
npm ci
npm test
npm run dev
```

Vite proxies `/api` and `/healthz` to `http://localhost:5080`, so the web app needs no
configuration locally.

### Configuration

There are no secrets to set for local runs. The API reads `appsettings.json` and
`appsettings.Development.json`, and any setting can be overridden with an environment variable
(`Section__Key`):

| Setting | Default | What it does |
|---|---|---|
| `Storage__ConnectionString` | `UseDevelopmentStorage=true` (Development) | Local Blob Storage through Azurite. Set it to an empty value to keep bills in memory instead. |
| `Storage__BlobServiceUri` | empty | Production only. Blob endpoint reached with managed identity. |
| `Scans__UseSample` | `false` | `true` makes every scanned photo read as the bundled Ramen Nagi receipt, with one price misread on purpose so the total check has work to do. |
| `DocumentIntelligence__Endpoint` | empty | Production only. Without it (and without `Scans__UseSample`), scanning answers "not available" and the app offers typing the receipt in. Production refuses to start without it. |
| `Scans__DailyCap` / `Scans__PerIpPerHour` | `15` / `3` | Scan limits that keep Document Intelligence inside its free tier. |

The web app reads two build-time variables (see [`web/.env.example`](web/.env.example)):

| Variable | Default | What it does |
|---|---|---|
| `VITE_API_BASE_URL` | empty | The API's address in production. Empty locally, where Vite proxies `/api`. The deploy workflow sets it. |
| `VITE_AUTO_SYNC` | off | `true` turns automatic polling back on. |

```bash
cp web/.env.example web/.env.local   # only if you need to change either value
```

### Screenshots at every size

`npm run screens` (in `web/`) screenshots each screen at 360, 390, 768, 1024, 1280 and 1600 px,
light and dark, into `web/screens/` (git-ignored), and fails if a page scrolls sideways. It needs
no API: requests are answered from `web/scripts/fixtures/`, which `node scripts/make-fixtures.mjs`
rebuilds from a locally running API. First time only: `npx playwright install chromium`.
Local only; it is not part of CI.

## Usage examples

### In the app

The quickest path is **Try the sample receipt** on the home screen. To demo it to someone, follow
the 60-second script in [`docs/DEMO.md`](docs/DEMO.md).

### Against the API

These calls work against a local API (`http://localhost:5080`) or a deployed one. They follow the
deploy smoke test in [`infra/smoke.sh`](infra/smoke.sh). All money on the wire is integer minor
units (centavos for PHP).

Create a bill from the bundled sample. The organizer key comes back once and only its hash is kept:

```bash
API=http://localhost:5080

curl -s -X POST "$API/api/bills" \
  -H 'content-type: application/json' \
  -d '{"source":"sample"}'
```

```json
{ "id": "34KxpQWuvJRTl4FlhoNgaP", "organizerKey": "2yp6N5rZzE9dnHQuhTxshm" }
```

Save both, then read the bill and its computed breakdown:

```bash
ID=<id from above>
KEY=<organizerKey from above>

curl -s "$API/api/bills/$ID" | jq '{title: .bill.title, people: [.bill.people[].name], total: .breakdown.receipts[0].computedTotal}'
```

```json
{
  "title": "Ramen dinner",
  "people": ["Ana", "Ben", "Carla", "Dan", "Eli", "Fay", "Gio", "Hana", "Ivan", "Jo", "Kai", "Lia"],
  "total": 525580
}
```

`525580` is ₱5,255.80 in centavos. The response carries an `ETag`; send it back in
`If-None-Match` and an unchanged bill answers `304 Not Modified`, which keeps refreshes cheap.

Pick a name as a friend, then claim an item with the person token that comes back in a header:

```bash
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
```

```json
{ "subtotal": 48500, "total": 34774 }
```

One Butao King (₱485.00), plus Ben's equal share of the service charge, less his share of the 40%
discount: ₱347.74.

`from` is what you saw before the change. If someone else changed that item first, nothing is
written and the API answers `409` with the latest bill.

Delete the bill as the organizer:

```bash
curl -s -X DELETE "$API/api/bills/$ID" -H "X-Organizer-Key: $KEY"
```

Every endpoint, status code and header is listed in [`docs/SPEC.md`](docs/SPEC.md), section "API".

## Cost

Built to cost close to $0 a month, using free tiers and scale-to-zero. Every resource, and what
could make it cost money (prices checked November 2026):

| Resource | Plan | Free allowance | What could make it cost money | Source |
|---|---|---|---|---|
| Static Web App | Free | Hosting at no charge | Nothing on the Free plan | [Static Web Apps pricing](https://azure.microsoft.com/pricing/details/app-service/static/) |
| Container App (API) | Consumption, 0 to 1 replicas | 180,000 vCPU-seconds, 360,000 GiB-seconds and 2 million requests per subscription per month | Steady traffic that keeps it awake past the compute allowance, or more than 2 million requests. Per-IP limits reject extra requests, but each one still counts. Health probes don't count. | [Billing in Container Apps](https://learn.microsoft.com/azure/container-apps/billing) |
| Container Apps environment | Consumption only | No charge of its own, and no logs destination | Adding a Dedicated workload profile or Log Analytics | [Billing in Container Apps](https://learn.microsoft.com/azure/container-apps/billing) |
| Document Intelligence | F0 | 500 pages a month | Nothing: F0 can't bill. The app caps scans at 15 a day and 3 per IP per hour, so a 31-day month stays at 465 | [Document Intelligence pricing](https://azure.microsoft.com/pricing/details/ai-document-intelligence/) |
| Blob Storage | Standard LRS | None | Storage and operations, but small JSON files cost cents a month at most | [Blob Storage pricing](https://azure.microsoft.com/pricing/details/storage/blobs/) |
| Automation account (kill switch) | Basic | No charge for the account; 500 job minutes a month | More than 500 job minutes in a month. One stop takes about a minute, and it runs only when the budget is passed or you test it | [Automation pricing](https://azure.microsoft.com/pricing/details/automation/) |
| Action group (kill switch) | Webhook action | No charge for the group; 100,000 webhook calls a month | More than 100,000 calls. It is called once each time the budget is passed | [Azure Monitor pricing](https://azure.microsoft.com/pricing/details/monitor/) |
| Budget, custom role, role assignments, webhook | | No charge | Nothing | |
| Container image | GitHub Container Registry, public | Free | Nothing for a public package | |

Prices and free tiers can change.

### Budget and kill switch

A **budget** watches the resource group: $1 a month in your billing currency (change it in
`infra/main.bicepparam`). It emails `BUDGET_ALERT_EMAIL` when actual spend passes 80% or the
forecast passes 100%.

When **actual** spend passes 100%, it also **stops the API**: the budget calls an action group,
which calls a webhook that runs a runbook, which stops the Container App. Azure's cost data can be
up to a day late and budgets are checked once a day, so the stop can come many hours after the
spend. It limits the damage; it doesn't catch a spike as it happens.

While the API is off, no replica runs and nothing reaches it. The web app shows "Can't reach the
server", and deploys refuse to run, so nothing turns it back on by accident. The address and
settings stay as they are.

You can turn it off or on by hand at any time:

```bash
RG=<resource-group> infra/kill-switch.sh off      # stops answering within seconds
RG=<resource-group> infra/kill-switch.sh status   # running status and what /healthz answers
RG=<resource-group> infra/kill-switch.sh on       # starts it, then runs the smoke test
```

After an automatic stop, find out what spent the money first (Cost Management > Cost analysis on
the resource group), then turn the API back on with `kill-switch.sh on`. It stays off, even into
the next month, until you do.

## Deploy

One workflow, started by hand only: `.github/workflows/ci-cd.yml`.

```bash
gh workflow run ci-cd.yml -f target=test    # tests only, any branch
gh workflow run ci-cd.yml -f target=infra   # Bicep: storage, Document Intelligence, Container App, Static Web App, roles, budget
gh workflow run ci-cd.yml -f target=api     # build and push the API image, release it, wait for /healthz
gh workflow run ci-cd.yml -f target=web     # build and upload the Vue app
gh workflow run ci-cd.yml -f target=api-web # api, then web; skips infra (use when infra/ has not changed)
gh workflow run ci-cd.yml -f target=all     # infra, then api, then web
```

Tests always run first, and nothing deploys unless they pass. Deploys run only from `main`, wait
for approval on the `production` environment, and sign in to Azure with OIDC (no stored secrets).
After any deploy, a smoke test creates the sample bill, claims an item, checks the totals and
deletes it.

First deploy, in order: the one-time setup in [`infra/README.md`](infra/README.md) (OIDC, GitHub
secrets, `production` environment), then `target=all`, then make the image package public (setup
step 6). Going public with this repo: follow [`docs/GO_PUBLIC.md`](docs/GO_PUBLIC.md).

### Rollback

- **API:** every image is tagged with its commit. Point the Container App back at the previous tag:

  ```bash
  az containerapp update -g <resource-group> -n receipt-splitter-api \
    --image ghcr.io/mrk-jsph-dev/receipt-splitter-api:<previous-commit-sha>
  ```

  The new revision must pass `/healthz` before it takes traffic.
- **Web:** revert the bad commit on `main`, then run `target=web`.

Details in [`infra/README.md`](infra/README.md), "Rollback".

## Project docs

| File | What it is |
|---|---|
| [`docs/SPEC.md`](docs/SPEC.md) | Data model, maths rules, API, security, deployment |
| [`docs/EXECUTION_PLAN.md`](docs/EXECUTION_PLAN.md) | Build order with Claude Code CLI prompts and done criteria |
| [`docs/DEMO.md`](docs/DEMO.md) | 60-second demo script using the sample |
| [`docs/GO_PUBLIC.md`](docs/GO_PUBLIC.md) | Checklist for making the repository public |
| [`docs/design/walkthrough.html`](docs/design/walkthrough.html) | Every screen in order (open in a browser) |
| [`docs/design/responsive.html`](docs/design/responsive.html) | Phone, tablet and desktop layouts and breakpoints |
| [`docs/design/spec.html`](docs/design/spec.html) | Rendered spec with mockups |
| [`CLAUDE.md`](CLAUDE.md) | Rules for Claude when working in this repo |

## Contributing

Bug reports and ideas are welcome as [issues](https://github.com/mrk-jsph-dev/receipt-splitter/issues).
For a pull request:

1. Open an issue first for anything bigger than a small fix, so we can agree on the approach.
2. Keep each pull request to one change. Read [`docs/SPEC.md`](docs/SPEC.md) first: it is the
   source of truth for the maths, the API and the privacy rules.
3. Write the tests first for anything that touches money or security. Money is integer minor
   units with `checked` arithmetic, never `double` or `float`.
4. Run `dotnet test` and `cd web && npm ci && npm test` before you open it.
5. Keep it near $0: no databases, accounts, real-time services or always-on compute. See
   "Do not add" in [`CLAUDE.md`](CLAUDE.md).
6. Never commit real receipt photos, real names, keys or connection strings.

CI runs only when a maintainer starts it, so a maintainer will run the tests on your branch.

## License

MIT. See [`LICENSE`](LICENSE).
