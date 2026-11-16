#!/usr/bin/env bash
# Deploys infra/main.bicep to the resource group. Used by the infra and api jobs, and works from
# your machine too after az login.
#
#   RG=<resource-group> BUDGET_ALERT_EMAIL=<email> infra/deploy.sh [image]
#
# With an image, releases it. Without one, keeps the image that is running now, so an infra run
# never rolls back the API.
#
# A deploy replaces the Container App's whole configuration, including its secrets. So this reads
# the ghcr.io registry token back from the app first and passes it in again. The token is only ever
# held in memory and masked in workflow logs. It is set once by hand (infra/README.md).
set -euo pipefail

: "${RG:?Set RG to the resource group}"
: "${BUDGET_ALERT_EMAIL:?Set BUDGET_ALERT_EMAIL}"
app=${API_APP:-receipt-splitter-api}
placeholder=mcr.microsoft.com/k8se/quickstart:latest

subscription=$(az account show --query id -o tsv)
app_id="/subscriptions/$subscription/resourceGroups/$RG/providers/Microsoft.App/containerApps/$app"

image=${1:-}
REGISTRY_USERNAME=""
REGISTRY_TOKEN=""

if current=$(az resource show --ids "$app_id" -o json 2>/dev/null); then
  [[ -z "$image" ]] && image=$(jq -r '.properties.template.containers[0].image' <<<"$current")
  REGISTRY_USERNAME=$(jq -r '[.properties.configuration.registries[]? | select(.server == "ghcr.io")][0].username // empty' <<<"$current")
  secret_name=$(jq -r '[.properties.configuration.registries[]? | select(.server == "ghcr.io")][0].passwordSecretRef // empty' <<<"$current")
  if [[ -n "$secret_name" ]]; then
    REGISTRY_TOKEN=$(az rest --method post --url "https://management.azure.com$app_id/listSecrets?api-version=2024-03-01" \
      --query "value[?name=='$secret_name'].value | [0]" -o tsv)
    [[ "${GITHUB_ACTIONS:-}" == true && -n "$REGISTRY_TOKEN" ]] && echo "::add-mask::$REGISTRY_TOKEN"
  fi
fi

# A ghcr.io image can't be pulled without the token. Stop rather than deploy an app that can't start.
if [[ -n "$image" && "$image" != "$placeholder" && -z "$REGISTRY_TOKEN" ]]; then
  cat >&2 <<EOF
::error::The Container App has no ghcr.io registry credential, so it can't pull the private image.
Add it once (a GitHub classic token with only read:packages), then run this again:
  az containerapp registry set -n $app -g $RG --server ghcr.io --username <github-user> --password <token>
EOF
  exit 1
fi

export API_IMAGE=$image REGISTRY_USERNAME REGISTRY_TOKEN BUDGET_ALERT_EMAIL
az deployment group create -g "$RG" -n main -p infra/main.bicepparam --query properties.outputs
