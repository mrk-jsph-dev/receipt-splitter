# Going public

Run these in order before and right after making the repository public. Once it is public, every
past and future Actions log is public too, so the email and Azure ids have to be secrets first, and
old logs that hold them deleted.

Two things set the order:
- **The image package goes public before the next deploy.** That deploy removes the registry
  credential, and the API scales to zero, so it pulls the image on every cold start. A private
  package at that point means the API goes down after its next idle period.
- **The next deploy runs after the repository is public.** It also creates the kill switch
  (module 14), whose runbook is published from the raw file on GitHub. That file can't be fetched
  while the repository is private, so the deploy would fail.

The workflow on `main` already reads the four values as secrets, so the first public run is masked.

## Before

**1. Add the four secrets.** The values are the same as the current variables (`gh variable list`
shows them).

```bash
gh variable list
gh secret set AZURE_CLIENT_ID       --body <client id>
gh secret set AZURE_TENANT_ID       --body <tenant id>
gh secret set AZURE_SUBSCRIPTION_ID --body <subscription id>
gh secret set BUDGET_ALERT_EMAIL    --body <your email>
```

**2. Delete the four variables.** Nothing on `main` reads them any more.

```bash
gh variable delete AZURE_CLIENT_ID
gh variable delete AZURE_TENANT_ID
gh variable delete AZURE_SUBSCRIPTION_ID
gh variable delete BUDGET_ALERT_EMAIL
```

**3. Make the `receipt-splitter-api` package public.** The workflow token can't change package
visibility, so do it in GitHub: your profile > **Packages** > `receipt-splitter-api` >
**Package settings** > **Danger Zone** > **Change visibility** > **Public**. The running app still
holds the old token until step 11, so nothing breaks in between. If you have Docker, check that an
anonymous pull works, using the tag the app runs now:

```bash
image=$(az containerapp show -n receipt-splitter-api -g rg-receipt-splitter \
  --query "properties.template.containers[0].image" -o tsv)
docker logout ghcr.io
docker pull "$image"
```

**4. Check the `production` environment.** Settings > **Environments** > `production`: you are a
required reviewer, and deployment branches are limited to `main`. GitHub Free enforces required
reviewers only on public repositories, so check this again right after step 8.

**5. Delete every workflow run so far,** because their logs hold the email and ids. Repeat until
the list is empty (it shows up to 200 at a time):

```bash
gh run list --limit 200 --json databaseId --jq '.[].databaseId' |
  while read -r id; do gh run delete "$id"; done
gh run list --limit 5
```

**6. Optional: your commit email.** To keep your email out of future public commits, turn on
**Keep my email addresses private** (Settings > **Emails**) and use the noreply address it shows:

```bash
git config --global user.email <id>+<user>@users.noreply.github.com
```

Commits already made keep the email they have.

**7. Pick the license.** Replace `[PLACEHOLDER: license Mark picks]` in `LICENSE` and the README,
and merge that to `main`.

## Go public

**8. Make the repository public:** Settings > **General** > **Danger Zone** > **Change
visibility** > **Public**. Then repeat the step 4 check.

## Right after

**9. Turn on the free code security features:** Settings > **Code security** > **Secret
scanning** (with **Push protection**) and **Dependabot alerts**. They are free on public
repositories and use no Actions minutes. Don't turn on CodeQL or Dependabot version updates.

**10. Re-run the one-time Azure setup.** It registers `Microsoft.Automation` and
`Microsoft.Insights` and lets the deploy identity assign the kill switch's stop role. Ignore the
`gh secret set` lines it prints; they're already done.

```bash
az login
infra/setup-oidc.sh <subscription-id> rg-receipt-splitter southeastasia
```

**11. Deploy.**

```bash
gh workflow run ci-cd.yml -f target=all --ref main
gh run watch
```

Approve the `production` deployments when asked. Open the run's **infra** job log, the `azure/login`
steps and **Deploy Bicep**, and check that the email and the three ids show as `***`. If any of
them shows in full, delete that run at once with `gh run delete <id>`. Then check that the app has
no registry entry left:

```bash
az containerapp show -n receipt-splitter-api -g rg-receipt-splitter \
  --query properties.configuration.registries
```

It must print `[]`.

**12. Revoke the old `read:packages` token** at https://github.com/settings/tokens. The app no
longer uses it.

**13. Test the automatic stop.** Start the runbook by hand, check that the API goes off, then turn
it back on:

```bash
export RG=rg-receipt-splitter
account="/subscriptions/$(az account show --query id -o tsv)/resourceGroups/$RG/providers/Microsoft.Automation/automationAccounts/receipt-splitter-automation"
az rest --method put --url "https://management.azure.com$account/jobs/$(uuidgen)?api-version=2023-11-01" \
  --body '{"properties":{"runbook":{"name":"Stop-Api"}}}' --query properties.status -o tsv
# A job takes a minute or two to start. Repeat until it says Stopped:
infra/kill-switch.sh status
infra/kill-switch.sh on
```

**14. A day later,** check that nothing new is being charged:

```bash
az consumption usage list --start-date $(date -v-2d +%F) --end-date $(date +%F) \
  --query "[].{resource:instanceName, cost:pretaxCost}" -o table
```

Every cost should be 0. Cost Management > Cost analysis on the resource group shows the same.
