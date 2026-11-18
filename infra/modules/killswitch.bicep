// The automatic half of the kill switch. When actual spend passes the budget, the budget calls the
// action group, the action group calls a webhook, and the webhook runs a runbook that stops the API
// Container App, the same as `infra/kill-switch.sh off`. Turning it back on is always by hand.
//
// Everything here is free at a few runs a month: the Automation account has no charge of its own
// and includes 500 job minutes a month (one stop takes about a minute), and an action group's
// webhook calls are free up to 100,000 a month. See the README "Cost" section for sources.
//
// The runbook's identity has one custom role on the API app only: read it and stop it. It can't
// start, change or delete anything.

param location string
param accountName string
param actionGroupName string
param appName string

@description('Raw URL of infra/runbooks/stop-api.ps1, pinned to a commit.')
param runbookUri string

@description('True only when the action group is missing (infra/deploy.sh checks). Azure shows a webhook\'s address only when it is created, so the webhook and the action group that holds it are created together and left alone after.')
param createWebhook bool

@secure()
@description('The new webhook\'s address, from the generateUri action (infra/deploy.sh). Bicep can\'t read it back from the webhook, so it is set on both the webhook and the action group.')
param webhookUri string = ''

@description('How long the webhook works. Only used when it is created.')
param webhookExpiry string = dateTimeAdd(utcNow(), 'P10Y')

// Fixed so that infra/setup-oidc.sh can allow the deploy identity to assign this one role.
var stopRoleId = 'f07c88ec-d902-4240-b0f0-31d94f3445c2'

resource api 'Microsoft.App/containerApps@2024-03-01' existing = {
  name: appName
}

resource account 'Microsoft.Automation/automationAccounts@2023-11-01' = {
  name: accountName
  location: location
  identity: { type: 'SystemAssigned' }
  properties: {
    sku: { name: 'Basic' }
    // Webhooks need local auth and public access. The webhook's address is its only credential.
    publicNetworkAccess: true
  }
}

resource appIdVariable 'Microsoft.Automation/automationAccounts/variables@2023-11-01' = {
  parent: account
  name: 'ApiAppId'
  properties: {
    value: '"${api.id}"'
    isEncrypted: false
  }
}

resource runbook 'Microsoft.Automation/automationAccounts/runbooks@2023-11-01' = {
  parent: account
  name: 'Stop-Api'
  location: location
  properties: {
    runbookType: 'PowerShell'
    logProgress: false
    logVerbose: false
    publishContentLink: { uri: runbookUri }
  }
}

resource webhook 'Microsoft.Automation/automationAccounts/webhooks@2015-10-31' = if (createWebhook) {
  parent: account
  name: 'budget-stop'
  properties: {
    isEnabled: true
    uri: webhookUri
    expiryTime: webhookExpiry
    runbook: { name: runbook.name }
  }
}

resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = if (createWebhook) {
  name: actionGroupName
  location: 'global'
  properties: {
    groupShortName: 'api-stop'
    enabled: true
    webhookReceivers: [
      {
        name: 'stop-api'
        serviceUri: webhookUri
        useCommonAlertSchema: false
      }
    ]
  }
}

resource stopRole 'Microsoft.Authorization/roleDefinitions@2022-04-01' = {
  name: stopRoleId
  properties: {
    roleName: 'Receipt Splitter API stop'
    description: 'Read and stop the Receipt Splitter API Container App. Used by the budget kill switch.'
    type: 'customRole'
    assignableScopes: [ resourceGroup().id ]
    permissions: [
      {
        actions: [
          'Microsoft.App/containerApps/read'
          'Microsoft.App/containerApps/stop/action'
        ]
        notActions: []
      }
    ]
  }
}

resource canStop 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(api.id, account.id, stopRoleId)
  scope: api
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', stopRole.name)
    principalId: account.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

output actionGroupId string = resourceId('Microsoft.Insights/actionGroups', actionGroupName)
