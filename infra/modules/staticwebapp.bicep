// The Vue app on the Free plan. Not linked to the repo: the pipeline builds web/ and uploads it
// with a deploy token that it fetches at run time, so no token is stored in GitHub.

param location string
param name string

resource site 'Microsoft.Web/staticSites@2023-12-01' = {
  name: name
  location: location
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {}
}

output name string = site.name
output origin string = 'https://${site.properties.defaultHostname}'
