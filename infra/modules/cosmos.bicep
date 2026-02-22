@description('Azure region for the Cosmos DB account.')
param location string

@description('Name of the Cosmos DB account.')
param cosmosAccountName string

@description('Name of the SQL database.')
param databaseName string = 'chat-app'

@description('Name of the container for conversations.')
param containerName string = 'conversations'

@description('Partition key path for the container.')
param partitionKeyPath string = '/conversationId'

@description('Tags to apply to the resource.')
param tags object = {}

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' = {
  name: cosmosAccountName
  location: location
  tags: tags
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
    capabilities: [
      {
        name: 'EnableServerless'
      }
    ]
    disableLocalAuth: false
  }
}

resource database 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-05-15' = {
  parent: cosmosAccount
  name: databaseName
  properties: {
    resource: {
      id: databaseName
    }
  }
}

resource container 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: database
  name: containerName
  properties: {
    resource: {
      id: containerName
      partitionKey: {
        paths: [partitionKeyPath]
        kind: 'Hash'
      }
      defaultTtl: -1
    }
  }
}

@description('The resource ID of the Cosmos DB account.')
output id string = cosmosAccount.id

@description('The document endpoint of the Cosmos DB account.')
output endpoint string = cosmosAccount.properties.documentEndpoint

@description('The name of the Cosmos DB account.')
output name string = cosmosAccount.name

@description('The name of the database.')
output databaseNameOutput string = database.name

@description('The name of the container.')
output containerNameOutput string = container.name
