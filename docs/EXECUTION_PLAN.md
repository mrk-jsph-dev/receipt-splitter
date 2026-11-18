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
endpoint, empty React app (replaced by Vue in module 4), `ci-cd.yml` with the `test` job.

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
4. After the first `target=infra` run, add the GHCR pull token to the Container App once
   (`infra/README.md`, setup step 6). The image stays private.

**Done when**
- `az bicep build -f infra/main.bicep` has no warnings.
- `gh workflow run ci-cd.yml -f target=infra` succeeds.
- `gh workflow run ci-cd.yml -f target=api` succeeds and `/healthz` returns 200.
- Azure Cost Management shows the budget.

**Deploy:** `infra`, then `api`.

---

### Module 4: Web shell and design system

**Prompt**

> Implement module 4 from docs/EXECUTION_PLAN.md: the Vue 3 + TypeScript + Vite app in web/.
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

### Module 4b: Responsive layout (phone, tablet, desktop)

Module 4 built the screens as a phone layout: `.screen` in `web/src/design/base.css` caps the
app at 480 px and frames it like a phone on bigger screens. This module makes it one
responsive site. Do it before module 5 so every later screen is built responsive from the start.

**Prompt**

> Implement module 4b from docs/EXECUTION_PLAN.md: the "Responsive layout" section of
> docs/SPEC.md, matching docs/design/responsive.html (open it in a browser; it shows Home,
> Bill, Check receipt and Settle at 390, 834 and 1280 px). Nothing may look like an account:
> remove any greeting, profile avatar, "Me" tab, bell and cross-bill money totals. Apply the
> minimal look: the colour tokens in the spec table (light and dark), flat white cards with
> 1 px borders, neutral avatars, Inter only (drop Sora), JetBrains Mono for receipt lines,
> Lucide icons through `lucide-vue-next` (stroke 1.75), and the peso receipt logo from the spec
> (never a dollar sign). Remove the 480 px `.screen` cap, the
> phone frame on wide screens and `HeroBand`. Add `web/src/design/breakpoints.css` (compact
> < 640, medium 640 to 1023, expanded >= 1024, wide >= 1240), mobile-first `min-width`
> queries only. Rebuild Home as "Split a bill" (scan card with Take photo and From gallery on
> touch sizes, Upload photos and a drop zone on expanded; Type it in; Try the sample) plus
> "On this device" with a paid-percentage ring per bill (from the API, no money maths in the
> browser) and the Forget this device footnote. Add `BillShell.vue` (bottom tab bar, 76 px
> icon rail, 240 px sidebar with Items, People, Receipts, Settle) and put the bill route in
> it. Add `Overlay.vue` (bottom sheet, centred dialog or side drawer by size) and a
> `useBreakpoint()` composable for behaviour only. Follow the input rules: 44 px targets,
> hover only under (hover: hover), drag only under (pointer: fine), safe-area insets, reduced
> motion. The scan inputs only pick files for now; module 8 sends them. Add a local
> Playwright script (`npm run screens`) that screenshots each route at 360, 390, 768, 1024,
> 1280 and 1600 px in light and dark into `web/screens/` (git-ignored) and fails on
> horizontal scroll. Do not add it to CI. Show me the plan first.

**Touches:** `web/src/design/`, `web/src/App.vue`, `web/src/home/`, `web/src/bill/`,
`web/package.json` (adds `lucide-vue-next`), `web/.gitignore`

**Done when**
- `cd web && npm test && npm run build` pass.
- `npm run screens` passes: no horizontal scroll at any of the six widths, light and dark.
- Home at 390, 834 and 1280 px matches docs/design/responsive.html, with no greeting, avatar,
  bell or "Me" tab. A bill shows the bottom tab bar at 390 px, the icon rail at 834 px and
  the sidebar at 1280 px.
- On a phone, From gallery opens the photo library and Take photo opens the camera.
- At 1600 px content stops at 1280 px and centres; no phone frame.
- `grep -rn "max-width: *480px" web/src` finds nothing outside dialogs.
- Tab through Home with the keyboard: every control reachable, focus ring visible.

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
- Layout checked with `cd web && npm run screens` (360 to 1600 px, light and dark), per the
  "Responsive layout" section of docs/SPEC.md.

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
- Layout checked with `cd web && npm run screens` (360 to 1600 px, light and dark), per the
  "Responsive layout" section of docs/SPEC.md.

**Deploy:** `web` (and `api` if endpoints changed).

---

### Module 7: Organizer follow-up (activity, confirm, undo, settle, lock)

**Prompt**

> Implement module 7 from docs/EXECUTION_PLAN.md: walkthrough steps 18 to 20 and 25 to 26.
> Activity list, confirm and decline payments, undo a claim, settle-up with fewest transfers
> across receipts, copy summary text, lock, delete now, and the auto-delete date. Show me the
> plan first.

**Done when** the settle-up list for the two-receipt example has 11 transfers and Ben sends nothing.
- Layout checked with `cd web && npm run screens` (360 to 1600 px, light and dark), per the
  "Responsive layout" section of docs/SPEC.md.

**Deploy:** `web`, `api`.

---

### Module 8: Receipt scanning

**Prompt**

> Implement module 8 from docs/EXECUTION_PLAN.md: POST /api/scans with Document Intelligence
> prebuilt receipt (Azure.AI.DocumentIntelligence, DefaultAzureCredential), image held in
> memory only and never logged or stored, daily cap and per-IP limit, quota endpoint, append
> mode for long receipts, and the keyword/sign classifier for service charge, discounts, tax,
> senior/PWD and VAT exempt lines with flags for review. In the web app (walkthrough steps 4, 5, 21
> and 22): camera capture and gallery upload via file inputs (the module 4b scan card; several files at
> once, plus drag and drop on desktop), client-side resize under 4 MB, the receipt tray, "new receipt or next page?",
> and the reading screen. Check the current Document Intelligence receipt field list first and
> update the [VERIFY] notes in SPEC.md. Show me the plan first.

**Done when**
- Scanning the Ramen Nagi photo (kept outside the repo) produces the 9 lines, flags the
  service charge and discount, and matches 5,255.80 after review.
- Tests prove the image bytes never reach storage or logs.
- A receipt photo picked from the phone gallery scans the same as one taken with the camera.
- At the cap, Scan is disabled and Sample still works.
- Layout checked with `cd web && npm run screens` (360 to 1600 px, light and dark), per the
  "Responsive layout" section of docs/SPEC.md.

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

---

### Module 13: Go public safely (logs, image, repo settings)

Run this before making the repository public. Once the repository is public, every past and future
Actions log is public too. Today the deploy logs print `BUDGET_ALERT_EMAIL` in full, because the
workflow reads it as a repository **variable** (`vars.BUDGET_ALERT_EMAIL`), and variables are not
masked. They also print the Azure tenant, subscription and client ids. A secret with the same name
doesn't help, because the workflow never reads it.

**Prompt**

> Implement module 13 from docs/EXECUTION_PLAN.md. Show me the plan first.
>
> 1. Logs. In `.github/workflows/ci-cd.yml`, read `BUDGET_ALERT_EMAIL`, `AZURE_CLIENT_ID`,
>    `AZURE_TENANT_ID` and `AZURE_SUBSCRIPTION_ID` from `secrets.*` instead of `vars.*`, so the
>    logs show `***`. Keep `AZURE_RESOURCE_GROUP` as a variable. Check that no step echoes them
>    or prints the deployment's parameters. Update the `gh variable set` lines at the end of
>    `infra/setup-oidc.sh` to `gh secret set`, and update `infra/README.md` setup step 3.
> 2. Public API image. When the repository is public, the image holds nothing that the code
>    doesn't already show, so make the `ghcr.io` image public. Then the Container App pulls it
>    with no credential, and there's no `read:packages` token to keep or renew. Remove the
>    registry credential from `infra/modules/containerapp.bicep`, the token carry-over and the
>    "no registry credential" stop from `infra/deploy.sh`, and setup step 6 from
>    `infra/README.md`. First confirm the Dockerfile and image hold no secrets or config values.
>    List the GitHub UI step (Package settings > Change visibility > Public) for me, because the
>    workflow token can't change package visibility.
> 3. Demo script. `docs/DEMO.md` says claims show up on the other phone within about 10 seconds
>    on their own. Auto sync is off (`VITE_AUTO_SYNC`), so add a **Refresh** tap on the organizer
>    phone at 0:40 and remove the "about 10 seconds" note.
> 4. License. Add a `LICENSE` file with [PLACEHOLDER: license Mark picks] and replace the
>    "Private. All rights reserved" section of the README to match. Don't pick one for me.
> 5. Write `docs/GO_PUBLIC.md`: the checklist below, in order, with the exact commands.
>
> Change no Azure resource sizes, tiers or limits in this module.

**Checklist for `docs/GO_PUBLIC.md`** (you run these, in this order)
1. `gh secret set` for the four values above. Keep the variables for now.
2. Merge this module and run `target=all`. Open the new run's infra log and check that the email
   and ids show as `***`. Then `gh variable delete` the same four.
3. Make the `receipt-splitter-api` package public, then revoke the old `read:packages` token at
   https://github.com/settings/tokens.
4. Settings > Environments > `production`: you are a required reviewer, and deployment
   branches are limited to `main`. GitHub Free enforces required reviewers only on public
   repositories, so check this right after the switch.
5. Delete every workflow run from before step 2, because their logs still hold the email:
   `gh run list --limit 200 --json databaseId,createdAt` and `gh run delete <id>` for each.
6. Optional: if you don't want your commit email in public history, set GitHub's noreply address
   for future commits (Settings > Emails). The old commits keep the email they have.
7. Make the repository public.
8. Right after: turn on secret scanning, push protection and Dependabot alerts
   (Settings > Code security). They are free on public repositories and use no Actions minutes.
   Don't turn on CodeQL or Dependabot version updates.

**Done when**
- `dotnet test` and `cd web && npm test` pass.
- A `target=all` run on `main` passes, and its logs show `***` for the email and the three ids.
- The Container App runs the public image with no registry entry and no secret
  (`az containerapp show -n receipt-splitter-api -g <rg> --query properties.configuration.registries`
  returns `[]`).
- `git grep -n "vars.BUDGET_ALERT_EMAIL\|vars.AZURE_CLIENT_ID\|vars.AZURE_TENANT_ID\|vars.AZURE_SUBSCRIPTION_ID"` finds nothing.

**Cost:** none. Nothing is added to Azure, and public GHCR packages are free.

**Deploy:** `all`.

---

### Module 14: Cost guard and kill switch

The scan service (Document Intelligence F0) can't bill, and the new-bill and scan caps already
hold against many IPs. The remaining risk is the API itself: per-IP limits reject extra requests,
but Azure still counts each request against the Container Apps free allowance (2 million a
month), and steady traffic keeps the container awake past the free compute allowance. The $1
budget only sends an email, and budget data can lag by up to a day.

This module adds a way to stop the API at once, by hand or automatically, using only pieces that
cost nothing at this scale.

**Prompt**

> Implement module 14 from docs/EXECUTION_PLAN.md. Show me the plan first.
>
> Before you plan, check current Microsoft pricing pages for each piece below and quote the line
> that shows it's free at our scale. Mark anything you can't confirm as [VERIFY]. If a piece isn't
> free, stop and tell me instead of building it. Never add Azure Monitor metric alerts, Log
> Analytics, Application Insights, Front Door, a WAF, an API Management instance, or any always-on
> resource, because each of these costs money.
>
> 1. Manual kill switch. `infra/kill-switch.sh off|on|status` for the API Container App. `off`
>    must stop all billable traffic and compute right away. Disabling ingress is the likely way;
>    confirm that disabled ingress means requests don't reach the app or count as billed
>    requests [VERIFY], and that `on` restores the same address, port and settings (or that
>    `target=infra` does). The web app should show its normal "can't reach the server" state
>    while the API is off. Test it against the live app: off, check, on, smoke test.
> 2. Automatic stop. Add a second, hard budget threshold in `infra/modules/budget.bicep`: when
>    **actual** spend passes the budget amount, an action group runs the same "off" step. Prefer
>    the cheapest working option, likely an Azure Automation runbook with a managed identity that
>    can only change this one Container App (custom role or the narrowest built-in role, scoped to
>    the app, not the resource group). Confirm that the Automation account, the runbook's job
>    minutes and the action group are free at a few runs a month [VERIFY]. Keep the existing
>    80% email.
> 3. README "Cost" section: a table of every resource and what could make it cost money, the kill
>    switch commands, what the automatic stop does and its delay (budget data can lag by up to a
>    day), and how to turn the API back on.
> 4. Tests: a Bicep build (`az bicep build`) and a `what-if` run in the plan output, plus a
>    shellcheck of the script.

**Done when**
- `infra/kill-switch.sh off` makes the API stop answering within a minute, and `on` brings it
  back with the smoke test passing.
- The budget has the automatic stop, and a test run of the runbook (started by hand) turns the API
  off, then `on` restores it.
- Every new Azure resource is listed in the README cost table with its price source, and none
  costs anything at this scale.
- `az consumption usage list` (or Cost Management) shows no new charges a day after deploying.

**Deploy:** `infra`.

## Open items to settle before the module that needs them

| Item | Needed by | Default if you say nothing |
|---|---|---|
| Azure region | Module 3 | Southeast Asia |
| Budget alert amount | Module 3 | $1 |
| Daily scan cap | Module 8 | 15 per day, 3 per IP per hour |
| Custom domain | Module 9 | None, use the free default addresses |
| License | Module 13 | [PLACEHOLDER] |
