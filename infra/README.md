# Infrastructure

Everything the app runs on, in one Azure resource group, written in Bicep. The `ci-cd` workflow
deploys it. Nothing here holds a secret.

```
main.bicep               entry point: wires the modules together
main.bicepparam          names, regions and budget (image and alert email come from the workflow)
modules/storage.bicep    Blob Storage for bills and rate-limit counters
modules/docintel.bicep   Document Intelligence F0 for receipt scanning
modules/containerapp.bicep  Container Apps environment and the API app
modules/staticwebapp.bicep  Static Web App (Free) for the Vue app
modules/roles.bicep      the API identity's two data roles
modules/budget.bicep     monthly cost budget: email alerts, and the automatic stop at 100%
modules/killswitch.bicep Automation runbook, webhook and action group that stop the API
runbooks/stop-api.ps1    the runbook: stops the API Container App
setup-oidc.sh            one-time GitHub to Azure sign-in setup
deploy.sh                deploys main.bicep, keeping the running image
kill-switch.sh           turns the API off or on by hand
smoke.sh                 post-deploy check against a running API
```

## What each file does

**`main.bicep`** deploys the modules in order and passes outputs between them. The storage
endpoint, the scan endpoint and the web origin go into the API's settings. The API's identity goes
into the role assignments. Storage and Document Intelligence names must be unique across Azure, so
they get a suffix derived from the resource group id. That suffix stays the same on every deploy.
If `apiImage` is empty, the Container App runs Microsoft's public quickstart image on port 80 with
no probes. That only happens before the first release, so that the app exists before the first
image is pushed.

**`main.bicepparam`** holds the values you might change: name prefix, Static Web App region, budget
amount and budget start month. The image tag and the alert email are read from environment
variables that `deploy.sh` sets. Releases therefore don't edit this file, and your email is not
committed to the repo.

**`modules/storage.bicep`** creates a Standard LRS storage account with two private containers,
`bills` and `limits`. Shared key access is off, so connection strings and account keys don't work;
only Entra identities with a data role get in. TLS 1.2 is the minimum, and plain HTTP and public
blob access are off. Two lifecycle rules do all retention: `bills/` blobs are deleted 30 days after
their last change, and `limits/` blobs (rate-limit counters and the salt for privacy hashes) after
2 days.

**`modules/docintel.bicep`** creates an Azure AI Document Intelligence account on the free F0 tier,
for module 8's receipt scanning. Local auth is off, so API keys are disabled and the API signs in
with its managed identity. The custom subdomain is what makes Entra sign-in work. A subscription can
hold only one F0 account of this kind. If you delete it, Azure keeps it soft-deleted for a while:
purge it with `az cognitiveservices account purge` before deploying again.

**`modules/containerapp.bicep`** creates two things:
- A Consumption-only Container Apps **environment** with no logs destination. That means no Log
  Analytics workspace and no log ingestion bill.
- The API **app**. It has a system-assigned managed identity, which is its only credential. Ingress
  is HTTPS-only on port 8080. Each replica gets 0.25 vCPU and 0.5 GiB, with 0 to 1 replicas, so it
  scales to zero when idle. One replica is also what the API's in-memory rate-limit cache expects.

  Startup, readiness and liveness probes all hit `/healthz`. In single-revision mode, a new revision
  only takes traffic once its probes pass, so a broken image never replaces a working one.

  The app's settings are `Storage__BlobServiceUri`, `Cors__AllowedOrigins__0` (the Static Web App's
  address) and `DocumentIntelligence__Endpoint`. All are plain addresses, not secrets.

  The image is **public** on GHCR. It holds only what the public code already shows, so the app
  pulls it with no credential, and the app has no secrets and no registry entry. There is no token
  to keep or renew.

**`modules/staticwebapp.bicep`** creates a Free-plan Static Web App with no link to the repo. The
workflow builds `web/` itself and uploads the result with a deploy token that it reads from Azure at
run time, so the token is never saved in GitHub. Static Web Apps is offered only in Central US, East US 2, West US 2, West Europe and East Asia
(checked November 2026 with `az provider show -n Microsoft.Web`), so it runs in East Asia while
everything else runs in the resource group's region.

**`modules/roles.bicep`** grants the API's managed identity exactly two roles:
- **Storage Blob Data Contributor** on this one storage account
- **Cognitive Services User** on this one Document Intelligence account

Each assignment name is a GUID built from scope, principal and role, so redeploying doesn't create
duplicates. These are the only roles the deploy identity is allowed to assign (see below).

**`modules/budget.bicep`** creates a monthly cost budget on the resource group. Its default is $1,
in your billing currency. It emails `BUDGET_ALERT_EMAIL` when actual spend passes 80%, or when Azure
forecasts the month will pass 100%. When actual spend passes 100%, it emails and also calls the
kill switch's action group, which stops the API. Azure doesn't allow changing the start date once
the budget exists.

**`modules/killswitch.bicep`** is the automatic half of the kill switch:
- An **Automation account** with a system-assigned identity, and a PowerShell runbook,
  `Stop-Api` (`runbooks/stop-api.ps1`). The runbook gets a token from that identity and calls the
  Container App's `stop` action. It is published from the raw file on GitHub, pinned to the last
  commit that changed it, so the repository must be public.
- A custom role, **Receipt Splitter API stop**, with only `containerApps/read` and
  `containerApps/stop/action`, assigned to the runbook's identity on the API app alone. It can't
  start, change or delete anything.
- A **webhook** on the runbook and an **action group** that calls it. Azure shows a webhook's
  address only when it is created, so `deploy.sh` creates both together, only when the action group
  is missing, and later deploys leave them alone. The webhook lasts 10 years. Anyone with its
  address could stop the API, so it is kept only inside the action group, and never in GitHub,
  logs or deployment outputs.

**`kill-switch.sh off|on|status`** stops or starts the API at once by hand, with your `az login`.
`on` runs the smoke test. **`deploy.sh` refuses to run while the API is off**, so a deploy never
turns it back on by accident.

## One-time setup

1. `az login`
2. `infra/setup-oidc.sh <subscription-id> <resource-group> <region>`, for example
   `infra/setup-oidc.sh 0000... rg-receipt-splitter southeastasia`. It is safe to run again. It:
   - registers the resource providers;
   - creates the resource group and an Entra app registration;
   - adds federated credentials for this repo's `main` branch and its `production` environment;
   - gives the app **Contributor** on the resource group;
   - gives the app **User Access Administrator** on the resource group, with a condition that it
     can only assign or remove the two roles above and the kill switch's custom stop role. Run it
     again after pulling a change to that list: it updates the condition in place.
3. Run the `gh secret set` and `gh variable set` lines it prints. `AZURE_CLIENT_ID`,
   `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` and `BUDGET_ALERT_EMAIL` are repository **secrets**,
   so the public workflow logs show them as `***`. `AZURE_RESOURCE_GROUP` is a **variable**.
4. In GitHub, create an environment named `production` (Settings > Environments). Add yourself as
   a required reviewer and limit deployment branches to `main`.
5. Run `target=infra` once (below). It creates the Container App with a placeholder image.
6. After the first `target=api` run creates the `receipt-splitter-api` package, make it public:
   GitHub > your profile > Packages > `receipt-splitter-api` > Package settings > Change
   visibility > Public. The workflow token can't change package visibility, so this is a manual
   step. Until then the app can't pull the image.

## Deploying

```bash
gh workflow run ci-cd.yml -f target=infra   # first time: everything except the API
gh workflow run ci-cd.yml -f target=api     # build, push, release the API, wait for /healthz
gh workflow run ci-cd.yml -f target=web     # build and upload the Vue app
gh workflow run ci-cd.yml -f target=api-web # api, then web; skips infra (use when infra/ has not changed)
gh workflow run ci-cd.yml -f target=all     # infra, then api, then web
```

Deploy jobs run only from `main` and wait for your approval on the `production` environment. After
any deploy, the smoke job runs `smoke.sh` against the live API. You can also run it yourself with
`infra/smoke.sh https://<api-host>`.

## Checking locally

```bash
az bicep build -f infra/main.bicep          # must show no warnings
BUDGET_ALERT_EMAIL=you@example.com az bicep build-params -f infra/main.bicepparam
```

## Rollback

Every image is tagged with its commit, and a release (README, "Releases") also with its version,
so rolling back means pointing the app at the previous tag. Find it in the package's version list,
the GitHub Releases page or `git log`. The new revision passes `/healthz`
before it takes traffic, and later infra runs keep whatever image is running.

```bash
az containerapp update -g <resource-group> -n receipt-splitter-api \
  --image ghcr.io/mrk-jsph-dev/receipt-splitter-api:<previous-version-or-commit-sha>
```

To roll the web app back, run `target=web` from the previous commit on `main`.
