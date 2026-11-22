// Azure AI Document Intelligence on the free tier, for receipt scanning. Local auth is
// off, so API keys don't work; the API signs in with its managed identity. A custom subdomain is
// required for Entra sign-in. A subscription can hold only one F0 account of this kind.

param location string
param name string

resource account 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: name
  location: location
  kind: 'FormRecognizer'
  sku: { name: 'F0' }
  properties: {
    customSubDomainName: name
    disableLocalAuth: true
    publicNetworkAccess: 'Enabled'
  }
}

output name string = account.name
output endpoint string = account.properties.endpoint
