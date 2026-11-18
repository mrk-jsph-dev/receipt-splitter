<#
Stops the Receipt Splitter API Container App. The budget's action group starts this through a
webhook when actual spend passes the monthly budget, and you can start it by hand. It ignores
whatever the caller sends: the only thing it can do is stop the one app.

Signs in with the Automation account's managed identity, whose only rights are to read and stop
that app (infra/modules/killswitch.bicep). Turn the API back on with infra/kill-switch.sh on.
#>
$ErrorActionPreference = 'Stop'

$appId = Get-AutomationVariable -Name 'ApiAppId'
$token = Invoke-RestMethod -Method Get -Headers @{ 'X-IDENTITY-HEADER' = $env:IDENTITY_HEADER } `
  -Uri "$($env:IDENTITY_ENDPOINT)?resource=https://management.azure.com/&api-version=2019-08-01"
Invoke-RestMethod -Method Post -Headers @{ Authorization = "Bearer $($token.access_token)" } `
  -Uri "https://management.azure.com$appId/stop?api-version=2026-01-01" | Out-Null
Write-Output "Stop requested for $appId"
