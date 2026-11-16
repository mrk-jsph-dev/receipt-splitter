# Execution plan

How to build Receipt Splitter one module at a time with Claude Code CLI on your own machine.
The full product spec is in [`SPEC.md`](SPEC.md). The clickable screens are in
[`design/walkthrough.html`](design/walkthrough.html) (open it in a browser).

## How every module runs

1. **Branch.** `git switch main && git pull && git switch -c module/NN-short-name`
2. **Start Claude.** Run `claude` in the repo root. It reads `CLAUDE.md` automatically.
3. **Plan first.** Switch to plan mode (Shift+Tab until it says *plan mode*) and paste the
   module's prompt from below. Read the plan, push back, and approve it. Nothing is written
   before you approve. This is the project's "show a plan before building" rule.
4. **Build.** Claude writes code and tests and runs them locally.
5. **Check locally.** Run the module's *Done when* commands yourself. All must pass.
6. **Commit and PR.** Ask Claude to commit and open a PR, or do it yourself.
   Nothing runs on the PR (by design).
7. **Run CI once.** `gh workflow run ci-cd.yml --ref module/NN-short-name -f target=test`,
   then `gh run watch`. Paste the run link in the PR checklist.
8. **Merge.** Squash-merge to `main`, delete the branch.
9. **Deploy if the module says so.** `gh workflow run ci-cd.yml -f target=<target>`.

Keep each module's PR small. If a module grows past about 800 changed lines, split it.

## Local tools

| Tool | Why | Check |
|---|---|---|
| .NET 10 SDK | API, maths engine, tests | `dotnet --version` shows `10.x` |
| Node 22 | Web app | `node -v` |
| Azure CLI + Bicep | Module 3 onwards | `az version`, `az bicep version` |
| GitHub CLI | Run the pipeline | `gh auth status` |
| Docker | Optional: build the API image locally | `docker version` |
| Azurite | Local Blob Storage emulator from module 2 | `npx azurite --version` |

## Modules

Each module lists the prompt to paste, what it touches, and how you know it is done.

---

### Module 0: Scaffold (done in the cloud, PR #1)

Repo layout, `CLAUDE.md`, spec, design files, empty .NET 10 solution with a `/healthz`
endpoint, empty React app, `ci-cd.yml` with the `test` job.

**First thing to do locally:** `dotnet build && dotnet test && (cd web && npm ci && npm test)`.
The scaffold was written in a cloud session without the .NET SDK, so this is the first real
compile. If anything fails, fix it as the first commit of module 1.

---

### Module 1: Maths engine (C#)

**Prompt**

> Implement module 1 from docs/EXECUTION_PLAN.md: the money maths engine in
> src/ReceiptSplitter.Core, following docs/SPEC.md section 6 exactly. Pure functions, no I/O,
> integer minor units (long, checked). Include: Allocate (largest remainder, tie-break by
> people order), per-receipt calculation for split modes items/equal/shares, proportional and
> equal adjustments, scoped adjustments (units and people), person-scoped discounts in equal
> and shares modes (senior/PWD), the Unclaimed bucket, the per-receipt invariant, multi-receipt
> totals and settle-up. Write the xUnit tests listed in section 6 first, then the code.
> Show me the plan before writing anything.

**Touches:** `src/ReceiptSplitter.Core/`, `tests/ReceiptSplitter.Core.Tests/`

**Done when**
- `dotnet test` is green.
- Tests include: the Ramen Nagi fixture (20 units, ₱5,255.80, 4 × 56.74 + 8 × 56.73),
  Ben = 385.74, the senior example (400.00 and 3 × 560.00), two receipts (Ben nets +64.26),
  JPY and KWD, a 3-for-100.00 line, a 2:1:1 shared unit, and an Allocate property test
  (10,000 random cases sum exactly).
- No `double` or `float` anywhere in Core (`grep -rn "double\|float" src/ReceiptSplitter.Core` is empty).

**Deploy:** none.

---

### Module 2: Bill API (.NET 10 minimal API)

**Prompt**

> Implement module 2 from docs/EXECUTION_PLAN.md: the bill API in src/ReceiptSplitter.Api as a
> .NET 10 minimal API, following docs/SPEC.md sections 5, 7 and 9. Bill storage behind an
> IBillStore interface with an in-memory store for tests and a Blob Storage store
> (Azure.Storage.Blobs, DefaultAzureCredential, If-Match ETags). Endpoints: create bill
> (sample/manual), get (ETag + 304), patch ops with from-preconditions, receipts CRUD,
> settings, share, lock, confirm, undo, delete. Organizer key hashed with SHA-256, constant-time
> compare. 128-bit ids. Identical 404s. Rate limits with ASP.NET Core rate limiting plus the
> Blob counter for not-found probes. Responses include the computed breakdown from Core. Write
> the isolation and security tests from section 9 with WebApplicationFactory. Add the bundled
> sample receipt as JSON with made-up names. Show me the plan first.

**Touches:** `src/ReceiptSplitter.Api/`, `tests/ReceiptSplitter.Api.Tests/`, `samples/`

**Done when**
- `dotnet test` is green, including every security test in SPEC section 9.
- `dotnet run --project src/ReceiptSplitter.Api` with Azurite running, then
  `curl -X POST localhost:5080/api/bills -d '{"source":"sample"}' -H 'content-type: application/json'`
  returns an id and organizer key, and `GET /api/bills/{id}` returns the bill with totals.
- No connection strings or keys in `appsettings*.json` (only the Azurite dev default in
  `appsettings.Development.json`).

**Deploy:** none yet.

---

### Module 3: Infrastructure and pipeline

**Prompt**

> Implement module 3 from docs/EXECUTION_PLAN.md: Bicep in infra/ (main.bicep, modules/,
> main.bicepparam) for storage (LRS, shared key off, lifecycle rules for bills/ 30 days and
> limits/ 2 days), Document Intelligence F0, Container Apps environment (no Log Analytics) and
> app (system-assigned identity, min 0 max 1, 0.25 vCPU, /healthz probe), Static Web App Free,
> role assignments, and a budget alert. Add the API Dockerfile. Add infra/setup-oidc.sh that
> creates the Entra app, federated credential for main + the production environment, and the
> scoped role assignments. Extend .github/workflows/ci-cd.yml with infra, api, web and smoke
> jobs gated by the target input, all needing test. Show me the plan first.

**Touches:** `infra/`, `src/ReceiptSplitter.Api/Dockerfile`, `.github/workflows/ci-cd.yml`, `README.md`

**You do (once)**
1. `az login`, then run `infra/setup-oidc.sh <subscription-id> <resource-group> <region>`.
2. Add the printed values as GitHub **variables** (not secrets): `AZURE_CLIENT_ID`,
   `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_RESOURCE_GROUP`, plus
   `BUDGET_ALERT_EMAIL` for the budget alerts.
3. Create a GitHub environment named `production` with yourself as required reviewer.
4. Make the GHCR package public after the first API push (the api job stops with the link).

**Done when**
- `az bicep build -f infra/main.bicep` has no warnings.
- `gh workflow run ci-cd.yml -f target=infra` succeeds.
- `gh workflow run ci-cd.yml -f target=api` succeeds and `/healthz` returns 200.
- Azure Cost Management shows the budget.

**Deploy:** `infra`, then `api`.

---

### Module 4: Web shell and design system

**Prompt**

> Implement module 4 from docs/EXECUTION_PLAN.md: the React + TypeScript + Vite app in web/.
> Port the design tokens (graphite and deep teal, light and dark) and components from
> docs/design/walkthrough.html into CSS variables and small components. Build Welcome,
> Your name, and Home (steps 1 to 3 of the walkthrough), the API client, 10-second polling
> with If-None-Match that pauses when the tab is hidden, and per-bill local storage keys
> (profile:name, bill:{id}:me, bill:{id}:key). Add "Forget this device". No money maths in
> the browser: display what the API returns. Show me the plan first.

**Touches:** `web/`

**Done when**
- `cd web && npm test && npm run build` pass.
- The three screens match the walkthrough at 390 px and 1280 px widths, in light and dark.
- `grep -rn "parseFloat\|toFixed" web/src` finds nothing used for money.

**Deploy:** `web`.

---

### Module 5: Friend flow (join, claim, share, pay)

**Prompt**

> Implement module 5 from docs/EXECUTION_PLAN.md: walkthrough steps 13 to 17. Join the bill
> (pick or add a name, "Are you X?" when a device name exists), claim with Mine and Share,
> share sheet with equal split, breakdown sheet, and I've paid (waiting state). Optimistic
> claim display; totals come from the API response. Conflict handling: on 409 reload and
> show who changed it. Desktop drag-and-drop with click-to-assign fallback. Show me the plan
> first.

**Done when**
- Two browsers on the same bill see each other's claims within 10 seconds.
- Claiming the same unit at the same time gives one winner and a clear message to the other.
- Ben's numbers in the sample match the walkthrough (385.74).

**Deploy:** `web`.

---

### Module 6: Organizer setup (review, fees, split modes, receipts, people, share)

**Prompt**

> Implement module 6 from docs/EXECUTION_PLAN.md: walkthrough steps 6 to 12, 23, 24, 27 and 28,
> minus the camera itself. Manual entry and the sample, line editor, total check that
> blocks sharing until it matches, fees and discounts editor (kind, percent or amount, applies
> to, split), split modes (by item, equally, by shares), senior/PWD card holders, multiple
> receipts with who paid and who was there, add people, bill name, payment note, share link,
> QR code and organizer link. Show me the plan first.

**Done when**
- A bill built by hand from the Ramen Nagi receipt reproduces every number in the walkthrough.
- The senior lunch example gives 400.00 and 3 × 560.00.
- The two-receipt example gives Ben +64.26.

**Deploy:** `web` (and `api` if endpoints changed).

---

### Module 7: Organizer follow-up (activity, confirm, undo, settle, lock)

**Prompt**

> Implement module 7 from docs/EXECUTION_PLAN.md: walkthrough steps 18 to 20 and 25 to 26.
> Activity list, confirm and decline payments, undo a claim, settle-up with fewest transfers
> across receipts, copy summary text, lock, delete now, and the auto-delete date. Show me the
> plan first.

**Done when** the settle-up list for the two-receipt example has 11 transfers and Ben sends nothing.

**Deploy:** `web`, `api`.

---

### Module 8: Receipt scanning

**Prompt**

> Implement module 8 from docs/EXECUTION_PLAN.md: POST /api/scans with Document Intelligence
> prebuilt receipt (Azure.AI.DocumentIntelligence, DefaultAzureCredential), image held in
> memory only and never logged or stored, daily cap and per-IP limit, quota endpoint, append
> mode for long receipts, and the keyword/sign classifier for service charge, discounts, tax,
> senior/PWD and VAT exempt lines with flags for review. In the web app (walkthrough steps 4, 5, 21
> and 22): camera capture via file input, client-side resize under 4 MB, the receipt tray, "new receipt or next page?",
> and the reading screen. Check the current Document Intelligence receipt field list first and
> update the [VERIFY] notes in SPEC.md. Show me the plan first.

**Done when**
- Scanning the Ramen Nagi photo (kept outside the repo) produces the 9 lines, flags the
  service charge and discount, and matches 5,255.80 after review.
- Tests prove the image bytes never reach storage or logs.
- At the cap, Scan is disabled and Sample still works.

**Deploy:** `infra` (if Document Intelligence was not deployed yet), `api`, `web`.

---

### Module 9: Launch

**Prompt**

> Implement module 9 from docs/EXECUTION_PLAN.md: README (what it is, try the sample,
> privacy and data deletion, cost and the budget alert, deploy and rollback steps), resolve
> every [PLACEHOLDER] and [VERIFY] you can and list the rest for me, and a 60-second demo
> script using the sample. Show me the plan first.

**Done when** the live site lets a stranger try the sample in under a minute, and the README
states the privacy rules.

**Deploy:** `all`.

## Open items to settle before the module that needs them

| Item | Needed by | Default if you say nothing |
|---|---|---|
| Azure region | Module 3 | Southeast Asia |
| Budget alert amount | Module 3 | $1 |
| Daily scan cap | Module 8 | 15 per day, 3 per IP per hour |
| Custom domain | Module 9 | None, use the free default addresses |
| License | Before making the repo public | [PLACEHOLDER] |
