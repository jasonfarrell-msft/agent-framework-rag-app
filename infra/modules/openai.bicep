@description('Azure region for the OpenAI resource.')
param location string

@description('Name of the Azure OpenAI resource.')
param openAiAccountName string

@description('Name of the model deployment.')
param deploymentName string

@description('The OpenAI model name to deploy.')
param modelName string = 'gpt-4.1'

@description('The model version to deploy.')
param modelVersion string = '2025-04-14'

@description('Deployment SKU capacity (tokens-per-minute in thousands).')
param deploymentCapacity int = 30

@description('Tags to apply to the resource.')
param tags object = {}

resource openAiAccount 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: openAiAccountName
  location: location
  tags: tags
  kind: 'OpenAI'
  sku: {
    name: 'S0'
  }
  properties: {
    customSubDomainName: openAiAccountName
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      defaultAction: 'Allow'
    }
  }
}

resource deployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openAiAccount
  name: deploymentName
  sku: {
    name: 'Standard'
    capacity: deploymentCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: modelName
      version: modelVersion
    }
  }
}

@description('The resource ID of the OpenAI account.')
output id string = openAiAccount.id

@description('The endpoint URL of the OpenAI account.')
output endpoint string = openAiAccount.properties.endpoint

@description('The name of the OpenAI account.')
output name string = openAiAccount.name
