// What the API's managed identity may do, and nothing more: read and write blobs in this one
// storage account, and call this one Document Intelligence account. These are the only two roles
// the pipeline is allowed to assign (see setup-oidc.sh).

param principalId string
param storageName string
param docIntelName string

var storageBlobDataContributor = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
var cognitiveServicesUser = 'a97b65f3-24c7-4388-baec-2e87135dc908'

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageName
}

resource docIntel 'Microsoft.CognitiveServices/accounts@2024-10-01' existing = {
  name: docIntelName
}

resource blobAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, principalId, storageBlobDataContributor)
  scope: storage
  properties: {
    principalId: principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributor)
  }
}

resource scanAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(docIntel.id, principalId, cognitiveServicesUser)
  scope: docIntel
  properties: {
    principalId: principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesUser)
  }
}
