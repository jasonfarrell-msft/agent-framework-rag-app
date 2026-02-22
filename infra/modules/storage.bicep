@description('Azure region for the storage account.')
param location string

@description('Name of the storage account.')
param storageAccountName string

@description('Tags to apply to the resource.')
param tags object = {}

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    networkAcls: {
      defaultAction: 'Allow'
    }
  }
}

@description('The resource ID of the storage account.')
output id string = storageAccount.id

@description('The name of the storage account.')
output name string = storageAccount.name
