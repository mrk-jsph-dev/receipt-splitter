# Going public

Run these in order before and right after making the repository public. Once it is public, every
past and future Actions log is public too, so the email and Azure ids have to be masked first, and
old logs that hold them deleted.

The order differs from the module 13 plan in one place: the image package goes public **before**
the deploy, not after. That deploy removes the registry credential, and the API scales to zero, so
it pulls the image on every cold start. A private package at that point means the API goes down
after its next idle period.

## Before

**1. Add the four secrets.** Keep the variables until step 4, so a run on the old workflow still
works. The values are the same as the current variables (`gh variable list` shows them).

```bash
gh variable list
gh secret set AZURE_CLIENT_ID       --body <client id>
gh secret set AZURE_TENANT_ID       --body <tenant id>
gh secret set AZURE_SUBSCRIPTION_ID --body <subscription id>
gh secret set BUDGET_ALERT_EMAIL    --body <your email>
```

**2. Make the `receipt-splitter-api` package public.** The workflow token can't change package
visibility, so do it in GitHub: your profile > **Packages** > `receipt-splitter-api` >
**Package settings** > **Danger Zone** > **Change visibility** > **Public**. The old token still
works until step 5, so nothing breaks in between. Check that an anonymous pull works:

```bash
docker logout ghcr.io
docker pull ghcr.io/mrk-jsph-dev/receipt-splitter-api:$(git rev-parse origin/main)
```

If that tag isn't in the package (the last API release was an earlier commit), use the tag the app
runs now:

```bash
az containerapp show -n receipt-splitter-api -g rg-receipt-splitter \
  --query "properties.template.containers[0].image" -o tsv
```

**3. Merge module 13 and deploy.**

```bash
gh workflow run ci-cd.yml -f target=all --ref main
gh run watch
```

Approve the `production` deployments when asked. Then open the run's **infra** job log, step
**Deploy Bicep** and the `azure/login` steps, and check that the email and the three ids show as
`***`. Check that the app has no registry entry left:

```bash
az containerapp show -n receipt-splitter-api -g rg-receipt-splitter \
  --query properties.configuration.registries
```

It must print `[]`.

**4. Delete the four variables.**

```bash
gh variable delete AZURE_CLIENT_ID
gh variable delete AZURE_TENANT_ID
gh variable delete AZURE_SUBSCRIPTION_ID
gh variable delete BUDGET_ALERT_EMAIL
```

**5. Revoke the old `read:packages` token** at https://github.com/settings/tokens. The app no
longer uses it.

**6. Check the `production` environment.** Settings > **Environments** > `production`: you are a
required reviewer, and deployment branches are limited to `main`. GitHub Free enforces required
reviewers only on public repositories, so check this again right after step 9.

**7. Delete every workflow run from before step 3,** because their logs still hold the email and
ids. List them, check the cut-off, then delete:

```bash
gh run list --limit 200 --json databaseId,createdAt,displayTitle \
  --jq '.[] | "\(.databaseId)  \(.createdAt)  \(.displayTitle)"'

# Replace the date with the start time of the step 3 run (UTC, from the list above).
cutoff=2026-11-18T00:00:00Z
gh run list --limit 200 --json databaseId,createdAt \
  --jq ".[] | select(.createdAt < \"$cutoff\") | .databaseId" |
  while read -r id; do gh run delete "$id"; done
```

Run the list again until nothing from before the cut-off is left (it shows up to 200 at a time).

**8. Optional: your commit email.** To keep your email out of future public commits, turn on
**Keep my email addresses private** (Settings > **Emails**) and use the noreply address it shows:

```bash
git config --global user.email <id>+<user>@users.noreply.github.com
```

Commits already made keep the email they have.

## Go public

**9. Make the repository public:** Settings > **General** > **Danger Zone** > **Change
visibility** > **Public**. Before this, put a real license in `LICENSE` and the README in place of
`[PLACEHOLDER: license Mark picks]`.

## Right after

**10. Turn on the free code security features:** Settings > **Code security** > **Secret
scanning** (with **Push protection**) and **Dependabot alerts**. They are free on public
repositories and use no Actions minutes. Don't turn on CodeQL or Dependabot version updates.

Then repeat the step 6 check.
