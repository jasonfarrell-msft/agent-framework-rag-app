@description('Azure region for the Static Web App.')
param location string

@description('Name of the Static Web App.')
param staticWebAppName string

@description('Tags to apply to the resource.')
param tags object = {}

resource staticWebApp 'Microsoft.Web/staticSites@2023-12-01' = {
  name: staticWebAppName
  location: location
  tags: tags
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {
    stagingEnvironmentPolicy: 'Enabled'
    allowConfigFileUpdates: true
    buildProperties: {
      appLocation: '/frontend'
      outputLocation: 'dist'
    }
  }
}

@description('The resource ID of the Static Web App.')
output id string = staticWebApp.id

@description('The name of the Static Web App.')
output name string = staticWebApp.name

@description('The default hostname of the Static Web App.')
output defaultHostName string = staticWebApp.properties.defaultHostname
