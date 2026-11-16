// The .NET API. Consumption plan, scales to zero after a few idle minutes and never past one
// replica, which the in-memory rate-limit cache relies on. The environment has no logs destination,
// so there is no Log Analytics workspace to pay for. The image comes from public GHCR, so there is
// no registry login and no registry to pay for.

param location string
param environmentName string
param appName string
param image string
param blobEndpoint string
param docIntelEndpoint string
param webOrigin string

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: environmentName
  location: location
  properties: {
    zoneRedundant: false
  }
}

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: appName
  location: location
  identity: { type: 'SystemAssigned' }
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
    }
    template: {
      containers: [
        {
          name: 'api'
          image: image
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'Storage__BlobServiceUri', value: blobEndpoint }
            { name: 'Cors__AllowedOrigins__0', value: webOrigin }
            { name: 'DocumentIntelligence__Endpoint', value: docIntelEndpoint }
          ]
          // A new revision takes traffic only after /healthz answers, so a broken image never
          // replaces a working one.
          probes: [
            {
              type: 'Startup'
              httpGet: { path: '/healthz', port: 8080 }
              periodSeconds: 2
              failureThreshold: 30
            }
            {
              type: 'Readiness'
              httpGet: { path: '/healthz', port: 8080 }
              periodSeconds: 10
            }
            {
              type: 'Liveness'
              httpGet: { path: '/healthz', port: 8080 }
              periodSeconds: 30
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 1
        rules: [
          {
            name: 'http'
            http: { metadata: { concurrentRequests: '50' } }
          }
        ]
      }
    }
  }
}

output name string = app.name
output fqdn string = app.properties.configuration.ingress.fqdn
output principalId string = app.identity.principalId
