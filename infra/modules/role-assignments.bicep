@description('Principal ID of the App Service managed identity.')
param appServicePrincipalId string

@description('Principal ID of the deploying user (for local dev access).')
param userPrincipalId string = ''

@description('Resource ID of the Azure OpenAI account.')
param openAiAccountId string

@description('Name of the Azure OpenAI account (used for existing resource reference).')
param openAiAccountName string

@description('Resource ID of the Cosmos DB account.')
param cosmosAccountId string

@description('Name of the Cosmos DB account (used for existing resource reference).')
param cosmosAccountName string

@description('Resource ID of the Azure AI Search service.')
param searchServiceId string

@description('Name of the Azure AI Search service (used for existing resource reference).')
param searchServiceName string

// ─── Well-known role definition IDs ───────────────────────────────────────────
// Cognitive Services OpenAI User
var cognitiveServicesOpenAiUserRoleId = '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'

// Search Index Data Reader
var searchIndexDataReaderRoleId = '1407120a-92aa-4202-b7e9-c0e197c71c8f'

// Search Service Contributor
var searchServiceContributorRoleId = '7ca78c08-252a-4471-8644-bb5a5e366d16'

// Cosmos DB Built-in Data Contributor (data plane)
var cosmosDataContributorRoleDefinitionId = '00000000-0000-0000-0000-000000000002'

// DocumentDB Account Contributor (control plane)
var documentDbAccountContributorRoleId = '5bd9cd88-fe45-4216-938b-f97437e15450'

// ─── Existing resource references ─────────────────────────────────────────────

resource openAiAccount 'Microsoft.CognitiveServices/accounts@2024-10-01' existing = {
  name: openAiAccountName
}

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' existing = {
  name: cosmosAccountName
}

resource searchService 'Microsoft.Search/searchServices@2024-06-01-preview' existing = {
  name: searchServiceName
}

// ─── Azure OpenAI: Cognitive Services OpenAI User ─────────────────────────────

resource openAiRoleAppService 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(openAiAccountId, appServicePrincipalId, cognitiveServicesOpenAiUserRoleId)
  scope: openAiAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesOpenAiUserRoleId)
    principalId: appServicePrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource openAiRoleUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(userPrincipalId)) {
  name: guid(openAiAccountId, userPrincipalId, cognitiveServicesOpenAiUserRoleId)
  scope: openAiAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesOpenAiUserRoleId)
    principalId: userPrincipalId
    principalType: 'User'
  }
}

// ─── Azure AI Search: Search Index Data Reader ────────────────────────────────

resource searchDataReaderRoleAppService 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(searchServiceId, appServicePrincipalId, searchIndexDataReaderRoleId)
  scope: searchService
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', searchIndexDataReaderRoleId)
    principalId: appServicePrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource searchDataReaderRoleUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(userPrincipalId)) {
  name: guid(searchServiceId, userPrincipalId, searchIndexDataReaderRoleId)
  scope: searchService
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', searchIndexDataReaderRoleId)
    principalId: userPrincipalId
    principalType: 'User'
  }
}

// ─── Azure AI Search: Search Service Contributor (management) ─────────────────

resource searchContributorRoleAppService 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(searchServiceId, appServicePrincipalId, searchServiceContributorRoleId)
  scope: searchService
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', searchServiceContributorRoleId)
    principalId: appServicePrincipalId
    principalType: 'ServicePrincipal'
  }
}

// ─── Cosmos DB: DocumentDB Account Contributor (control plane) ────────────────

resource cosmosControlPlaneRoleAppService 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(cosmosAccountId, appServicePrincipalId, documentDbAccountContributorRoleId)
  scope: cosmosAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', documentDbAccountContributorRoleId)
    principalId: appServicePrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource cosmosControlPlaneRoleUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(userPrincipalId)) {
  name: guid(cosmosAccountId, userPrincipalId, documentDbAccountContributorRoleId)
  scope: cosmosAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', documentDbAccountContributorRoleId)
    principalId: userPrincipalId
    principalType: 'User'
  }
}

// ─── Cosmos DB: Built-in Data Contributor (data plane) ────────────────────────
// Data-plane roles use the native CosmosDB SQL role assignment resource type,
// not standard ARM role assignments.

resource cosmosDataRoleAppService 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = {
  parent: cosmosAccount
  name: guid(cosmosAccountId, appServicePrincipalId, cosmosDataContributorRoleDefinitionId)
  properties: {
    roleDefinitionId: '${cosmosAccount.id}/sqlRoleDefinitions/${cosmosDataContributorRoleDefinitionId}'
    principalId: appServicePrincipalId
    scope: cosmosAccount.id
  }
}

resource cosmosDataRoleUser 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = if (!empty(userPrincipalId)) {
  parent: cosmosAccount
  name: guid(cosmosAccountId, userPrincipalId, cosmosDataContributorRoleDefinitionId)
  properties: {
    roleDefinitionId: '${cosmosAccount.id}/sqlRoleDefinitions/${cosmosDataContributorRoleDefinitionId}'
    principalId: userPrincipalId
    scope: cosmosAccount.id
  }
}
