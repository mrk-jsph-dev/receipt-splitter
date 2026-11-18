#!/usr/bin/env bash
# Turns the API off or on at once, by hand. Off stops the Container App: no replica runs, so no
# compute is billed, and Azure answers requests itself without them reaching the API. The address,
# port and settings stay as they are, so on brings back the same API.
#
#   RG=<resource-group> infra/kill-switch.sh off|on|status
#
# The budget's automatic stop (infra/modules/killswitch.bicep) does the same as off. Turning the API
# back on is always by hand: nothing else starts it, and infra/deploy.sh refuses to run while it's off.
set -euo pipefail

action=${1:-}
[[ "$action" =~ ^(off|on|status)$ ]] || { echo "Usage: RG=<resource-group> $0 off|on|status" >&2; exit 1; }
: "${RG:?Set RG to the resource group}"
app=${API_APP:-receipt-splitter-api}
api_version=2026-01-01

subscription=$(az account show --query id -o tsv)
url="https://management.azure.com/subscriptions/$subscription/resourceGroups/$RG/providers/Microsoft.App/containerApps/$app"

show() { az rest --method get --url "$url?api-version=$api_version" --query "$1" -o tsv; }
health() { curl -s -o /dev/null -m 60 -w '%{http_code}' "https://$host/healthz" || true; }

# Waits up to $2 seconds for the running status to become $1.
wait_for() {
  local _
  for _ in $(seq 1 $(($2 / 5))); do
    [[ $(show properties.runningStatus) == "$1" ]] && return 0
    sleep 5
  done
  echo "The API is still not $1 after $2 seconds. Check: RG=$RG $0 status" >&2
  exit 1
}

host=$(show properties.configuration.ingress.fqdn)

case $action in
  status)
    echo "Running status: $(show properties.runningStatus)"
    echo "https://$host/healthz answers: $(health)"
    ;;
  off)
    az rest --method post --url "$url/stop?api-version=$api_version" --output none
    wait_for Stopped 120
    echo "The API is off. https://$host/healthz answers $(health)."
    echo "Turn it back on with: RG=$RG $0 on"
    ;;
  on)
    az rest --method post --url "$url/start?api-version=$api_version" --output none
    wait_for Running 300
    "$(dirname "$0")/smoke.sh" "https://$host"
    ;;
esac
