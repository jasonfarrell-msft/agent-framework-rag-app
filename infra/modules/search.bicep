@description('Azure region for the search service.')
param location string

@description('Name of the Azure AI Search service.')
param searchServiceName string

@description('The SKU of the search service.')
@allowed(['basic', 'standard', 'standard2', 'standard3'])
param skuName string = 'basic'

@description('Tags to apply to the resource.')
param tags object = {}

resource searchService 'Microsoft.Search/searchServices@2024-06-01-preview' = {
  name: searchServiceName
  location: location
  tags: tags
  sku: {
    name: skuName
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    hostingMode: 'default'
    partitionCount: 1
    replicaCount: 1
    publicNetworkAccess: 'enabled'
    authOptions: {
      aadOrApiKey: {
        aadAuthFailureMode: 'http401WithBearerChallenge'
      }
    }
    semanticSearch: 'free'
  }
}

@description('The resource ID of the search service.')
output id string = searchService.id

@description('The name of the search service.')
output name string = searchService.name

@description('The endpoint URL of the search service.')
output endpoint string = 'https://${searchService.name}.search.windows.net'

@description('The principal ID of the search service managed identity.')
output principalId string = searchService.identity.principalId
