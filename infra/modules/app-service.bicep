@description('Azure region for the App Service resources.')
param location string

@description('Name of the App Service Plan.')
param appServicePlanName string

@description('Name of the Web App.')
param webAppName string

@description('The .NET runtime stack version.')
param dotnetVersion string = 'dotnet|9.0'

@description('Tags to apply to the resource.')
param tags object = {}

@description('App settings to configure on the Web App.')
param appSettings object = {}

type appSettingEntry = {
  name: string
  value: string
}

resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: appServicePlanName
  location: location
  tags: tags
  kind: 'linux'
  sku: {
    name: 'B1'
    tier: 'Basic'
  }
  properties: {
    reserved: true
  }
}

// Convert the appSettings object to the array format required by Web App config
var appSettingsArray = [
  for key in objectKeys(appSettings): {
    name: key
    value: appSettings[key]
  }
]

resource webApp 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: dotnetVersion
      alwaysOn: true
      cors: {
        allowedOrigins: ['*']
      }
      appSettings: appSettingsArray
    }
  }
}

@description('The resource ID of the Web App.')
output id string = webApp.id

@description('The name of the Web App.')
output name string = webApp.name

@description('The default hostname of the Web App.')
output defaultHostName string = webApp.properties.defaultHostName

@description('The principal ID of the Web App managed identity.')
output principalId string = webApp.identity.principalId
