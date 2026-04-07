targetScope = 'resourceGroup'

// ─── Parameters ───────────────────────────────────────────────────────────────

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Base name used to generate resource names (e.g., "pseg-main-eus2-mx01").')
param baseName string

@description('Name of the Azure OpenAI model deployment.')
param openAiDeploymentName string = 'gpt-4.1-deployment'

@description('The OpenAI model name to deploy.')
param openAiModelName string = 'gpt-4.1'

@description('The OpenAI model version.')
param openAiModelVersion string = '2025-04-14'

@description('Cosmos DB database name.')
param cosmosDatabaseName string = 'chat-app'

@description('Cosmos DB container name.')
param cosmosContainerName string = 'conversations'

@description('Azure AI Search index name.')
param searchIndexName string = 'multimodal-rag-1771601932521-single-manual'

@description('Whether to deploy Azure AI Search (set false if region has no capacity).')
param deploySearch bool = true

@description('Tags to apply to all resources.')
param tags object = {}

// ─── Resource naming ──────────────────────────────────────────────────────────

var storageAccountName = replace('st${baseName}', '-', '')
var openAiAccountName = 'aoi-${baseName}'
var cosmosAccountName = 'cosmos-${baseName}'
var searchServiceName = 'search-${baseName}'
var appServicePlanName = 'plan-${baseName}'
var webAppName = 'app-${baseName}'
var staticWebAppName = 'swa-${baseName}'

// ─── Modules ──────────────────────────────────────────────────────────────────

module storage 'modules/storage.bicep' = {
  params: {
    location: location
    storageAccountName: length(storageAccountName) > 24
      ? substring(storageAccountName, 0, 24)
      : storageAccountName
    tags: tags
  }
}

module openAi 'modules/openai.bicep' = {
  params: {
    location: location
    openAiAccountName: openAiAccountName
    deploymentName: openAiDeploymentName
    modelName: openAiModelName
    modelVersion: openAiModelVersion
    tags: tags
  }
}

module cosmos 'modules/cosmos.bicep' = {
  params: {
    location: location
    cosmosAccountName: cosmosAccountName
    databaseName: cosmosDatabaseName
    containerName: cosmosContainerName
    tags: tags
  }
}

module search 'modules/search.bicep' = if (deploySearch) {
  params: {
    location: location
    searchServiceName: searchServiceName
    tags: tags
  }
}

module appService 'modules/app-service.bicep' = {
  params: {
    location: location
    appServicePlanName: appServicePlanName
    webAppName: webAppName
    tags: tags
    appSettings: {
      AzureOpenAI__Endpoint: openAi.outputs.endpoint
      AzureOpenAI__DeploymentName: openAiDeploymentName
      CosmosDb__Endpoint: cosmos.outputs.endpoint
      CosmosDb__DatabaseId: cosmosDatabaseName
      CosmosDb__ContainerId: cosmosContainerName
      AzureSearch__Endpoint: search.outputs.endpoint
      AzureSearch__IndexName: searchIndexName
    }
  }
}

module staticWebApp 'modules/static-web-app.bicep' = {
  params: {
    location: location
    staticWebAppName: staticWebAppName
    tags: tags
  }
}

module roleAssignments 'modules/role-assignments.bicep' = {
  params: {
    appServicePrincipalId: appService.outputs.principalId
    openAiAccountId: openAi.outputs.id
    openAiAccountName: openAiAccountName
    cosmosAccountId: cosmos.outputs.id
    cosmosAccountName: cosmosAccountName
    deploySearch: deploySearch
    searchServiceId: deploySearch ? search.outputs.id : ''
    searchServiceName: deploySearch ? searchServiceName : ''
  }
}

// ─── Outputs ──────────────────────────────────────────────────────────────────

@description('The Web App default hostname.')
output webAppHostName string = appService.outputs.defaultHostName

@description('The Static Web App default hostname.')
output staticWebAppHostName string = staticWebApp.outputs.defaultHostName

@description('Azure OpenAI endpoint.')
output openAiEndpoint string = openAi.outputs.endpoint

@description('Cosmos DB endpoint.')
output cosmosEndpoint string = cosmos.outputs.endpoint

@description('Azure AI Search endpoint.')
output searchEndpoint string = deploySearch ? search.outputs.endpoint : ''

@description('Web App principal ID (for manual role verifications).')
output webAppPrincipalId string = appService.outputs.principalId
