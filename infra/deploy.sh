#!/usr/bin/env bash
# Deploys infra/main.bicep to the resource group. Used by the infra and api jobs, and works from
# your machine too after az login.
#
#   RG=<resource-group> BUDGET_ALERT_EMAIL=<email> infra/deploy.sh [image]
#
# With an image, releases it. Without one, keeps the image that is running now, so an infra run
# never rolls back the API. The image is public on ghcr.io, so there is no registry credential.
#
# Refuses to run while the kill switch has the API off, so a deploy never turns it back on by
# accident. Turn it on first with infra/kill-switch.sh on.
set -euo pipefail

: "${RG:?Set RG to the resource group}"
: "${BUDGET_ALERT_EMAIL:?Set BUDGET_ALERT_EMAIL}"
app=${API_APP:-receipt-splitter-api}
prefix=${NAME_PREFIX:-receipt-splitter}

subscription=$(az account show --query id -o tsv)
group_id="/subscriptions/$subscription/resourceGroups/$RG/providers"

image=${1:-}
if current=$(az resource show --ids "$group_id/Microsoft.App/containerApps/$app" -o json 2>/dev/null); then
  if [[ $(jq -r '.properties.runningStatus // empty' <<<"$current") == Stopped ]]; then
    echo "::error::The API is off (kill switch). Turn it on first: RG=$RG infra/kill-switch.sh on" >&2
    exit 1
  fi
  [[ -z "$image" ]] && image=$(jq -r '.properties.template.containers[0].image' <<<"$current")
fi

# Azure shows a webhook's address only when it is created, so the stop webhook and the action group
# that holds it are created together, once. Without the action group, start over with a new webhook.
# Bicep can't read a new webhook's address back, so it is generated here first and passed in as a
# secure parameter. Generating needs the Automation account, so a bare one is created if missing;
# main.bicep then sets the rest of it.
CREATE_STOP_WEBHOOK=false
STOP_WEBHOOK_URI=
if ! az resource show --ids "$group_id/Microsoft.Insights/actionGroups/$prefix-api-stop" >/dev/null 2>&1; then
  account_url="https://management.azure.com$group_id/Microsoft.Automation/automationAccounts/$prefix-automation"
  api_version=2023-11-01
  az rest --method delete --url "$account_url/webhooks/budget-stop?api-version=$api_version" >/dev/null 2>&1 || true
  if ! az rest --method get --url "$account_url?api-version=$api_version" >/dev/null 2>&1; then
    location=$(az group show -n "$RG" --query location -o tsv)
    az rest --method put --url "$account_url?api-version=$api_version" --output none --body "{
      \"location\": \"$location\",
      \"identity\": { \"type\": \"SystemAssigned\" },
      \"properties\": { \"sku\": { \"name\": \"Basic\" }, \"publicNetworkAccess\": true }
    }"
  fi
  STOP_WEBHOOK_URI=$(az rest --method post --url "$account_url/webhooks/generateUri?api-version=2015-10-31" -o tsv)
  [[ -n "${GITHUB_ACTIONS:-}" ]] && echo "::add-mask::$STOP_WEBHOOK_URI"
  [[ "$STOP_WEBHOOK_URI" == https://* ]] || { echo "::error::Could not generate the stop webhook's address." >&2; exit 1; }
  CREATE_STOP_WEBHOOK=true
fi

# The runbook is published from GitHub at the last commit that changed it, which is on main.
RUNBOOK_COMMIT=$(git log -1 --format=%H -- infra/runbooks/stop-api.ps1)

export API_IMAGE=$image BUDGET_ALERT_EMAIL CREATE_STOP_WEBHOOK STOP_WEBHOOK_URI RUNBOOK_COMMIT
az deployment group create -g "$RG" -n main -p infra/main.bicepparam --query properties.outputs
