#!/usr/bin/env bash
# Deploys infra/main.bicep to the resource group. Used by the infra and api jobs, and works from
# your machine too after az login.
#
#   RG=<resource-group> BUDGET_ALERT_EMAIL=<email> infra/deploy.sh [image]
#
# With an image, releases it. Without one, keeps the image that is running now, so an infra run
# never rolls back the API. The image is public on ghcr.io, so there is no registry credential.
set -euo pipefail

: "${RG:?Set RG to the resource group}"
: "${BUDGET_ALERT_EMAIL:?Set BUDGET_ALERT_EMAIL}"
app=${API_APP:-receipt-splitter-api}

subscription=$(az account show --query id -o tsv)
app_id="/subscriptions/$subscription/resourceGroups/$RG/providers/Microsoft.App/containerApps/$app"

image=${1:-}
if [[ -z "$image" ]] && current=$(az resource show --ids "$app_id" -o json 2>/dev/null); then
  image=$(jq -r '.properties.template.containers[0].image' <<<"$current")
fi

export API_IMAGE=$image BUDGET_ALERT_EMAIL
az deployment group create -g "$RG" -n main -p infra/main.bicepparam --query properties.outputs
