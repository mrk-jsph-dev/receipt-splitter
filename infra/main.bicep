// Everything Receipt Splitter runs on, in one resource group. Deployed by the ci-cd workflow
// (target=infra, and again by target=api with a new image). See infra/README.md.

@description('Region for everything except the Static Web App.')
param location string = resourceGroup().location

@description('Static Web Apps Free is offered in fewer regions. [VERIFY] Southeast Asia is not one of them.')
param webLocation string = 'eastasia'

@description('Prefix for resource names.')
param namePrefix string = 'receipt-splitter'

@description('API container image. Empty runs a public placeholder until the first release.')
param apiImage string = ''

@description('GitHub user that owns the ghcr.io registry token.')
param registryUsername string = ''

@secure()
@description('read:packages token for the private ghcr.io image. infra/deploy.sh reads it back from the app.')
param registryToken string = ''

@description('Monthly budget in the billing currency.')
param budgetAmount int = 1

@description('First day of the month the budget starts counting from.')
param budgetStartDate string

@description('Who gets the budget alert emails.')
param budgetAlertEmail string

// Storage and Document Intelligence names are global, so they get a suffix unique to this group.
var suffix = uniqueString(resourceGroup().id)

module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    location: location
    name: 'rsbills${suffix}'
  }
}

module docIntel 'modules/docintel.bicep' = {
  name: 'docintel'
  params: {
    location: location
    name: '${namePrefix}-scan-${suffix}'
  }
}

module web 'modules/staticwebapp.bicep' = {
  name: 'staticwebapp'
  params: {
    location: webLocation
    name: '${namePrefix}-web'
  }
}

module api 'modules/containerapp.bicep' = {
  name: 'containerapp'
  params: {
    location: location
    environmentName: '${namePrefix}-env'
    appName: '${namePrefix}-api'
    image: apiImage
    registryUsername: registryUsername
    registryToken: registryToken
    blobEndpoint: storage.outputs.blobEndpoint
    docIntelEndpoint: docIntel.outputs.endpoint
    webOrigin: web.outputs.origin
  }
}

module roles 'modules/roles.bicep' = {
  name: 'roles'
  params: {
    principalId: api.outputs.principalId
    storageName: storage.outputs.name
    docIntelName: docIntel.outputs.name
  }
}

module budget 'modules/budget.bicep' = {
  name: 'budget'
  params: {
    name: '${namePrefix}-monthly'
    amount: budgetAmount
    startDate: budgetStartDate
    alertEmail: budgetAlertEmail
  }
}

output webUrl string = web.outputs.origin
output apiUrl string = 'https://${api.outputs.fqdn}'
