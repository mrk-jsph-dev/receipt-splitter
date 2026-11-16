# Receipt Splitter: instructions for Claude

A small public web app for splitting a bill with friends. Snap or type a receipt, check what
was read, share a link, friends claim what they had, everyone sees who owes whom.

Read before changing anything substantial:

- `docs/SPEC.md`: the source of truth (data model, maths rules, API, security, deployment).
- `docs/EXECUTION_PLAN.md`: build order, one module at a time, with done criteria.
- `docs/design/walkthrough.html`: every screen in order. Match it.
- `docs/design/responsive.html`: phone, tablet and desktop layouts. Every screen must work
  from 360 px to 1600 px (SPEC.md, "Responsive layout"). Never cap the app at phone width.

## How to work

- One module per branch and PR (`module/NN-name`). Keep PRs small.
- Show a plan before writing code for a module, and wait for approval.
- Write the tests first for anything that touches money or security.
- Run `dotnet test` and `cd web && npm test` before saying a module is done.
- CI runs only when started by hand: `gh workflow run ci-cd.yml -f target=test`. Never add
  `push` or `pull_request` triggers. Never add CodeQL (the owner is on GitHub Free).
- Unknown details get a clear `[PLACEHOLDER]` or `[VERIFY]`, never a guess or an invented number.
- Write user-facing copy in plain, confident English. No buzzwords.

## Stack (decided)

- API: .NET 10 minimal API (ASP.NET Core) in `src/ReceiptSplitter.Api`, hosted on Azure
  Container Apps (Consumption, scale 0 to 1). Not Azure Functions.
- Maths: `src/ReceiptSplitter.Core`, pure C#, no I/O. The server owns all money maths and
  returns computed breakdowns. The browser never calculates money.
- .NET layout: vertical slices. Organise code by feature, not by layer (no `Services/`,
  `Repositories/`, `Models/` folders). Core: `Shared/` (bill model, money, `Allocate`),
  `Receipts/`, `Bills/`, `Settlement/`. API: one folder per endpoint under `Features/`
  (request, handler, endpoint mapping together); cross-cutting code (storage, auth, rate
  limits) under `Infrastructure/`. Test projects mirror the same folders.
- Web: Vue 3 + TypeScript + Vite in `web/` (`<script setup>` single-file components, Vue
  Router), on Azure Static Web Apps Free. Code is by feature here too (`welcome/`, `home/`,
  `bill/`), with `design/` for tokens and components and `shared/` for the API client and storage.
- Storage: Blob Storage, one JSON blob per bill, ETag concurrency, lifecycle deletes.
- Scanning: Azure AI Document Intelligence F0, prebuilt receipt model.
- Infra: Bicep in `infra/`. Deploy: one manual workflow, `.github/workflows/ci-cd.yml`.

Do not add: databases (SQL, Cosmos), Redis, SignalR or WebSockets, accounts or login,
Azure Container Registry (use GHCR: a private image, pulled with a `read:packages` token kept
only as a Container App secret, see `infra/README.md`), Log Analytics workspaces, always-on compute,
or extra third-party services. Keep it near $0/month.

## Money rules

- Integer minor units (`long`) everywhere in Core, with `checked` arithmetic. No `double`
  or `float`. `decimal` only to parse input and format output.
- Allocation is largest remainder with ties broken by people order. Every split sums exactly.
- Maths runs per receipt. A receipt's fees and discounts never touch another receipt.
- Senior, PWD and any person-scoped discount stays with its people in every split mode.
- Never auto-confirm amounts. Sharing is blocked until each receipt matches its printed total.

## Security and privacy rules

- Each bill is isolated: one blob per bill, no route that lists bills, 128-bit random ids,
  identical 404s for missing, deleted and expired bills.
- Organizer key: 128-bit, stored only as a SHA-256 hash, compared in constant time, carried
  in the URL fragment (`#`), never in a query string or log.
- Managed identity only. No connection strings, account keys or API keys in code, config or
  GitHub secrets. GitHub signs in to Azure with OIDC.
- Receipt images stay in memory for one request and are never stored or logged.
- Logs hold a salted hash of the bill id, never names, amounts or request bodies.
- Browser storage is keyed per bill (`bill:{id}:...`); "Forget this device" clears it all.

## Commands

```bash
dotnet build
dotnet test
dotnet run --project src/ReceiptSplitter.Api      # http://localhost:5080
cd web && npm ci && npm run dev                    # http://localhost:5173
cd web && npm test
gh workflow run ci-cd.yml -f target=test           # or infra | api | web | all
```
