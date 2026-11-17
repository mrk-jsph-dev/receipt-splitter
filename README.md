# Receipt Splitter

Split a restaurant or grocery bill with friends. Snap or type the receipt, check what was read,
share a link, and everyone taps what they had. The app shows who owes whom, to the centavo.

No accounts. Friends join by link. Bills delete themselves 30 days after the last change.

**Live:** [PLACEHOLDER: the Static Web App address, filled in after the first `web` deploy]

## Try it in under a minute

1. Open the live link and tap **Try the sample receipt**. No upload and no AI call: it builds a
   bill from a bundled Ramen Nagi receipt with made-up names (Ana to Lia, 12 people).
2. Pick a name under **Who are you?** and tap **Mine** on a couple of items.
3. Tap **Breakdown** to see your share, with the discount and service charge applied.
4. Open **Settle** to see who pays whom once everything is claimed.

A 60-second demo script is in [`docs/DEMO.md`](docs/DEMO.md).

## What it does

- Scan a receipt (camera or gallery, several photos for a long receipt), type one in, or try the sample
- Review every line before sharing; sharing stays locked until each receipt matches its printed total
- Several receipts in one bill, each with its own fees, payer and list of who was there
- Split by item, equally, or by shares
- Discounts, service charge, tax and tip, each split by what people ordered or equally
- Senior citizen and PWD discounts that stay with the card holder
- Settle up with the fewest transfers across all receipts
- The organizer can confirm payments, undo claims, lock the bill or delete it

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

## Cost

Built to cost close to $0 a month, using free tiers and scale-to-zero:

| Service | Plan | Free allowance |
|---|---|---|
| Static Web Apps | Free | Hosting at no charge |
| Container Apps | Consumption, 0 to 1 replicas | 180,000 vCPU-seconds, 360,000 GiB-seconds and 2 million requests per subscription per month |
| Document Intelligence | F0 | 500 pages a month. The app caps scans at 15 a day and 3 per IP per hour, so a 31-day month stays at 465 |
| Blob Storage | Standard LRS | Not free, but small JSON files cost cents a month at most |
| Container image | GitHub Container Registry, private | Free |

Prices and free tiers can change. A **budget alert** watches the resource group: $1 a month in
your billing currency. It emails `BUDGET_ALERT_EMAIL` when actual spend passes 80% or the forecast
passes 100%. It only alerts; it never stops anything. Change the amount in `infra/main.bicepparam`.

## Run locally

Needs the .NET 10 SDK and Node 22.

```bash
dotnet test
dotnet run --project src/ReceiptSplitter.Api     # http://localhost:5080/healthz

cd web
npm ci
npm test
npm run dev                                       # http://localhost:5173
```

Scanning without Azure: start the API with `Scans__UseSample=true` and every photo reads as the
bundled Ramen Nagi receipt (with one price misread on purpose, so the total check has work to do).
Without it, and with no `DocumentIntelligence__Endpoint`, scanning answers "not available" and the
app offers typing the receipt in. Production refuses to start without the endpoint.

### Screenshots at every size

`npm run screens` (in `web/`) screenshots each screen at 360, 390, 768, 1024, 1280 and 1600 px,
light and dark, into `web/screens/` (git-ignored), and fails if a page scrolls sideways. It needs
no API: requests are answered from `web/scripts/fixtures/`, which `node scripts/make-fixtures.mjs`
rebuilds from a locally running API. First time only: `npx playwright install chromium`.
Local only; it is not part of CI.

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
variables, `production` environment), then `target=infra`, then the registry token (setup step 6),
then `target=all`.

### Rollback

- **API:** every image is tagged with its commit. Point the Container App back at the previous tag:

  ```bash
  az containerapp update -g <resource-group> -n receipt-splitter-api \
    --image ghcr.io/mrk-jsph-dev/receipt-splitter-api:<previous-commit-sha>
  ```

  The new revision must pass `/healthz` before it takes traffic.
- **Web:** revert the bad commit on `main`, then run `target=web`.

Details in [`infra/README.md`](infra/README.md), "Rollback".

## Stack

Vue + TypeScript + Vite on Azure Static Web Apps (Free) · .NET 10 minimal API on Azure
Container Apps (Consumption, scales to zero) · Blob Storage · Azure AI Document Intelligence (F0).
Managed identity everywhere, no stored keys. Infrastructure in Bicep.

## Docs

| File | What it is |
|---|---|
| [`docs/SPEC.md`](docs/SPEC.md) | Data model, maths rules, API, security, deployment |
| [`docs/EXECUTION_PLAN.md`](docs/EXECUTION_PLAN.md) | Build order with Claude Code CLI prompts and done criteria |
| [`docs/DEMO.md`](docs/DEMO.md) | 60-second demo script using the sample |
| [`docs/design/walkthrough.html`](docs/design/walkthrough.html) | Every screen in order (open in a browser) |
| [`docs/design/responsive.html`](docs/design/responsive.html) | Phone, tablet and desktop layouts and breakpoints |
| [`docs/design/spec.html`](docs/design/spec.html) | Rendered spec with mockups |
| [`CLAUDE.md`](CLAUDE.md) | Rules for Claude when working in this repo |

## License

Private. Copyright © 2026 MJ Dev. All rights reserved. This code is not licensed for use,
copying or distribution. Choose a license before making the repository public.
