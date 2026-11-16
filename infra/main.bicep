// Everything Receipt Splitter runs on, in one resource group. Deployed by the ci-cd workflow
// (target=infra, and again by target=api with a new image). See infra/README.md.

@description('Region for everything except the Static Web App.')
param location string = resourceGroup().location

@description('Static Web Apps Free is offered in fewer regions. [VERIFY] Southeast Asia is not one of them.')
param webLocation string = 'eastasia'

@description('Prefix for resource names.')
param namePrefix string = 'receipt-splitter'

@description('API container image. Empty skips the Container App, for the first infra run before any image exists.')
param apiImage string = ''

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

module api 'modules/containerapp.bicep' = if (!empty(apiImage)) {
  name: 'containerapp'
  params: {
    location: location
    environmentName: '${namePrefix}-env'
    appName: '${namePrefix}-api'
    image: apiImage
    blobEndpoint: storage.outputs.blobEndpoint
    docIntelEndpoint: docIntel.outputs.endpoint
    webOrigin: web.outputs.origin
  }
}

module roles 'modules/roles.bicep' = if (!empty(apiImage)) {
  name: 'roles'
  params: {
    principalId: api!.outputs.principalId
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
output apiUrl string = empty(apiImage) ? '' : 'https://${api!.outputs.fqdn}'
