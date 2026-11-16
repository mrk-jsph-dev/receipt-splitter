# Receipt Splitter

Split a restaurant or grocery bill with friends. Snap or type the receipt, check what was read,
share a link, and everyone taps what they had. The app shows who owes whom, to the centavo.

No accounts. Friends join by link. Bills delete themselves 30 days after the last change.

> **Status:** scaffold. Features land one module at a time; see
> [`docs/EXECUTION_PLAN.md`](docs/EXECUTION_PLAN.md).

## What it will do

- Try a bundled sample receipt in under a minute, with no upload and no AI call
- Scan a receipt (daily cap), or type one in
- Review every line before sharing; sharing stays locked until the total matches the receipt
- Several receipts in one bill, each with its own fees, payer and list of who was there
- Split by item, equally, or by shares
- Discounts, service charge, tax and tip, each split by what people ordered or equally
- Senior citizen and PWD discounts that stay with the card holder
- Settle up with the fewest transfers across all receipts

## Docs

| File | What it is |
|---|---|
| [`docs/SPEC.md`](docs/SPEC.md) | Data model, maths rules, API, security, deployment |
| [`docs/EXECUTION_PLAN.md`](docs/EXECUTION_PLAN.md) | Build order with Claude Code CLI prompts and done criteria |
| [`docs/design/walkthrough.html`](docs/design/walkthrough.html) | Every screen in order (open in a browser) |
| [`docs/design/spec.html`](docs/design/spec.html) | Rendered spec with mockups |
| [`CLAUDE.md`](CLAUDE.md) | Rules for Claude when working in this repo |

## Stack

Vue + TypeScript + Vite on Azure Static Web Apps (Free) · .NET 10 minimal API on Azure
Container Apps (Consumption, scales to zero) · Blob Storage · Azure AI Document Intelligence (F0).
Managed identity everywhere, no stored keys. Infrastructure in Bicep.

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

## CI/CD

One workflow, started by hand only: `.github/workflows/ci-cd.yml`.

```bash
gh workflow run ci-cd.yml -f target=test    # test | infra | api | web | all
```

Tests always run first, and nothing deploys unless they pass. Deploys run only from `main`, wait
for approval on the `production` environment, and sign in to Azure with OIDC (no stored secrets).
After any deploy, a smoke test creates the sample bill, claims an item, checks the totals and
deletes it.

First-time Azure setup, what each Bicep file does, and how to roll back:
[`infra/README.md`](infra/README.md).

## Privacy

- Receipt photos are read once and never stored.
- Only item names, prices, fees, the shop name, date and total are kept.
- Bills, names and payment notes delete automatically 30 days after the last change.
- Each bill is private to people with its link; there is no list or search of bills.

The full rules are in [`docs/SPEC.md`](docs/SPEC.md), sections "Privacy" and
"Many bills, nothing leaking between them".

## Cost

Built to cost close to $0 a month at small scale, using free tiers and scale-to-zero.
Free tiers and prices can change, so a small Azure budget alert is part of the deployment.

## License

[PLACEHOLDER]
