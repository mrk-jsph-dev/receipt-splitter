// Bills and rate-limit counters. One JSON blob per bill under bills/, counters and the privacy
// salt under limits/. No keys: shared key access is off, so only Entra identities with a data role
// can read or write. Lifecycle rules do retention, so the API has no cleanup timer.

param location string
param name string

resource account 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: name
  location: location
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    accessTier: 'Hot'
    allowSharedKeyAccess: false
    allowBlobPublicAccess: false
    allowCrossTenantReplication: false
    defaultToOAuthAuthentication: true
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    publicNetworkAccess: 'Enabled'
  }
}

resource blobs 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: account
  name: 'default'
}

resource billsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobs
  name: 'bills'
  properties: { publicAccess: 'None' }
}

resource limitsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobs
  name: 'limits'
  properties: { publicAccess: 'None' }
}

// Bills delete 30 days after their last change. Counters and the salt delete after 2 days.
resource lifecycle 'Microsoft.Storage/storageAccounts/managementPolicies@2023-05-01' = {
  parent: account
  name: 'default'
  properties: {
    policy: {
      rules: [
        {
          name: 'delete-bills-after-30-days'
          enabled: true
          type: 'Lifecycle'
          definition: {
            filters: { blobTypes: [ 'blockBlob' ], prefixMatch: [ 'bills/' ] }
            actions: { baseBlob: { delete: { daysAfterModificationGreaterThan: 30 } } }
          }
        }
        {
          name: 'delete-limits-after-2-days'
          enabled: true
          type: 'Lifecycle'
          definition: {
            filters: { blobTypes: [ 'blockBlob' ], prefixMatch: [ 'limits/' ] }
            actions: { baseBlob: { delete: { daysAfterModificationGreaterThan: 2 } } }
          }
        }
      ]
    }
  }
}

output name string = account.name
output blobEndpoint string = account.properties.primaryEndpoints.blob
