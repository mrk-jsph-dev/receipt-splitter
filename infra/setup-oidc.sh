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
#     Contributor and Cognitive Services User (the two roles in infra/modules/roles.bicep)
# Safe to run again. Prints the GitHub variables to add at the end.
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

az account set --subscription "$subscription"
tenant=$(az account show --query tenantId -o tsv)

echo "Registering resource providers (once per subscription)..."
for namespace in Microsoft.App Microsoft.CognitiveServices Microsoft.Storage Microsoft.Web Microsoft.Consumption; do
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

add_credential() {
  local name=$1 subject=$2
  if [[ -z "$(az ad app federated-credential list --id "$client_id" --query "[?name=='$name'].name" -o tsv)" ]]; then
    az ad app federated-credential create --id "$client_id" --output none --parameters "{
      \"name\": \"$name\",
      \"issuer\": \"https://token.actions.githubusercontent.com\",
      \"subject\": \"$subject\",
      \"audiences\": [\"api://AzureADTokenExchange\"]
    }"
  fi
}
echo "Federated credentials..."
add_credential github-main "repo:$repo:ref:refs/heads/main"
add_credential github-production "repo:$repo:environment:production"

echo "Role assignments on $group..."
az role assignment create --assignee-object-id "$principal_id" --assignee-principal-type ServicePrincipal \
  --role Contributor --scope "$scope" --output none

roles="$storage_blob_data_contributor, $cognitive_services_user"
condition="((!(ActionMatches{'Microsoft.Authorization/roleAssignments/write'})) OR (@Request[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$roles})) AND ((!(ActionMatches{'Microsoft.Authorization/roleAssignments/delete'})) OR (@Resource[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$roles}))"
az role assignment create --assignee-object-id "$principal_id" --assignee-principal-type ServicePrincipal \
  --role "User Access Administrator" --scope "$scope" \
  --condition "$condition" --condition-version 2.0 --output none

cat <<EOF

Done. Add these as GitHub repository variables (Settings > Secrets and variables > Actions >
Variables). They are identifiers, not secrets.

  gh variable set AZURE_CLIENT_ID       --body $client_id
  gh variable set AZURE_TENANT_ID       --body $tenant
  gh variable set AZURE_SUBSCRIPTION_ID --body $subscription
  gh variable set AZURE_RESOURCE_GROUP  --body $group
  gh variable set BUDGET_ALERT_EMAIL    --body <your email>
EOF
