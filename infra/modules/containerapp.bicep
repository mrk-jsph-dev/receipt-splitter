// The .NET API. Consumption plan, scales to zero after a few idle minutes and never past one
// replica, which the in-memory rate-limit cache relies on. The environment has no logs destination,
// so there is no Log Analytics workspace to pay for.
//
// The image is private on GHCR. The app pulls it with a read:packages GitHub token kept only as a
// Container App secret: set once by hand (infra/README.md), then read back and passed in by
// infra/deploy.sh on every deploy, because a deploy replaces the app's whole configuration.
// Before the first release there is no image or token yet, so the app runs a public placeholder.

param location string
param environmentName string
param appName string

@description('API image. Empty runs the placeholder.')
param image string

@description('GitHub user the registry token belongs to.')
param registryUsername string

@secure()
@description('read:packages token for ghcr.io. Empty means no registry credential.')
param registryToken string
param blobEndpoint string
param docIntelEndpoint string
param webOrigin string

var placeholderImage = 'mcr.microsoft.com/k8se/quickstart:latest'
var usePlaceholder = empty(image) || image == placeholderImage
var hasRegistry = !empty(registryToken)

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
      secrets: hasRegistry ? [ { name: 'ghcr-token', value: registryToken } ] : []
      registries: hasRegistry ? [ { server: 'ghcr.io', username: registryUsername, passwordSecretRef: 'ghcr-token' } ] : []
      ingress: {
        external: true
        targetPort: usePlaceholder ? 80 : 8080
        transport: 'auto'
        allowInsecure: false
      }
    }
    template: {
      containers: [
        {
          name: 'api'
          image: usePlaceholder ? placeholderImage : image
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
          // replaces a working one. The placeholder has no /healthz.
          probes: usePlaceholder ? [] : [
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
