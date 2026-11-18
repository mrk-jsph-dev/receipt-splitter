#!/usr/bin/env bash
# One-time setup so GitHub Actions can deploy to Azure without any stored password or key.
#
#   az login
#   infra/setup-oidc.sh <subscription-id> <resource-group> <region> [owner/repo]
#
# Creates (or reuses) the resource group, an Entra app registration with federated credentials
# for this repo's main branch and its "production" environment, and two role assignments on the
# resource group:
#   - Contributor, to create and update the resources in main.bicep
#   - User Access Administrator, limited by a condition to assigning only Storage Blob Data
#     Contributor and Cognitive Services User (infra/modules/roles.bicep) and the kill switch's
#     custom stop role (infra/modules/killswitch.bicep)
# Safe to run again: it widens or narrows that condition to match. Prints the GitHub secrets and
# variable to add at the end.
set -euo pipefail

if [[ $# -lt 3 ]]; then
  echo "Usage: $0 <subscription-id> <resource-group> <region> [owner/repo]" >&2
  exit 1
fi

subscription=$1
group=$2
region=$3
repo=${4:-$(gh repo view --json nameWithOwner -q .nameWithOwner)}
app_name="github-${repo//\//-}-deploy"

storage_blob_data_contributor=ba92f5b4-2d11-453d-a403-e96b0029c9fe
cognitive_services_user=a97b65f3-24c7-4388-baec-2e87135dc908
# The custom role's fixed id. Must match stopRoleId in infra/modules/killswitch.bicep.
api_stop_role=f07c88ec-d902-4240-b0f0-31d94f3445c2

az account set --subscription "$subscription"
tenant=$(az account show --query tenantId -o tsv)

echo "Registering resource providers (once per subscription)..."
for namespace in Microsoft.App Microsoft.CognitiveServices Microsoft.Storage Microsoft.Web Microsoft.Consumption \
  Microsoft.Automation Microsoft.Insights; do
  az provider register --namespace "$namespace" --output none
done

echo "Resource group $group in $region..."
az group create --name "$group" --location "$region" --output none
scope=$(az group show --name "$group" --query id -o tsv)

echo "App registration $app_name..."
client_id=$(az ad app list --display-name "$app_name" --query "[0].appId" -o tsv)
if [[ -z "$client_id" ]]; then
  client_id=$(az ad app create --display-name "$app_name" --query appId -o tsv)
fi
if ! az ad sp show --id "$client_id" --output none 2>/dev/null; then
  az ad sp create --id "$client_id" --output none
fi
principal_id=$(az ad sp show --id "$client_id" --query id -o tsv)

# GitHub decides the subject's prefix: "repo:owner/repo", or with immutable subjects
# "repo:owner@<id>/repo@<id>". Ask GitHub rather than guess, so the credential always matches.
prefix=$(gh api "repos/$repo/actions/oidc/customization/sub" -q '.sub_claim_prefix // empty' 2>/dev/null || true)
prefix=${prefix:-repo:$repo}

add_credential() {
  local name=$1 subject=$2 current parameters
  parameters="{
    \"name\": \"$name\",
    \"issuer\": \"https://token.actions.githubusercontent.com\",
    \"subject\": \"$subject\",
    \"audiences\": [\"api://AzureADTokenExchange\"]
  }"
  current=$(az ad app federated-credential list --id "$client_id" --query "[?name=='$name'].subject" -o tsv)
  if [[ -z "$current" ]]; then
    az ad app federated-credential create --id "$client_id" --output none --parameters "$parameters"
  elif [[ "$current" != "$subject" ]]; then
    az ad app federated-credential update --id "$client_id" --federated-credential-id "$name" \
      --output none --parameters "$parameters"
  fi
}
echo "Federated credentials for $prefix..."
add_credential github-main "$prefix:ref:refs/heads/main"
add_credential github-production "$prefix:environment:production"

echo "Role assignments on $group..."
az role assignment create --assignee-object-id "$principal_id" --assignee-principal-type ServicePrincipal \
  --role Contributor --scope "$scope" --output none

roles="$storage_blob_data_contributor, $cognitive_services_user, $api_stop_role"
condition="((!(ActionMatches{'Microsoft.Authorization/roleAssignments/write'})) OR (@Request[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$roles})) AND ((!(ActionMatches{'Microsoft.Authorization/roleAssignments/delete'})) OR (@Resource[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$roles}))"
# create returns an existing assignment unchanged, so an older condition is updated in place.
existing=$(az role assignment list --assignee "$principal_id" --role "User Access Administrator" \
  --scope "$scope" --query "[0]" -o json)
if [[ -z "$existing" || "$existing" == null ]]; then
  az role assignment create --assignee-object-id "$principal_id" --assignee-principal-type ServicePrincipal \
    --role "User Access Administrator" --scope "$scope" \
    --condition "$condition" --condition-version 2.0 --output none
elif [[ $(jq -r .condition <<<"$existing") != "$condition" ]]; then
  az role assignment update --output none \
    --role-assignment "$(jq --arg c "$condition" '.condition = $c | .conditionVersion = "2.0"' <<<"$existing")"
fi

cat <<EOF

Done. Add these to GitHub (Settings > Secrets and variables > Actions). The ids and the email are
secrets so that public workflow logs show them as ***; the resource group name is a variable.

  gh secret set AZURE_CLIENT_ID       --body $client_id
  gh secret set AZURE_TENANT_ID       --body $tenant
  gh secret set AZURE_SUBSCRIPTION_ID --body $subscription
  gh secret set BUDGET_ALERT_EMAIL    --body <your email>
  gh variable set AZURE_RESOURCE_GROUP --body $group
EOF
