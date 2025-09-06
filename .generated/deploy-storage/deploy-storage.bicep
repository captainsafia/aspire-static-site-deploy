@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

resource deploy_storage 'Microsoft.Storage/storageAccounts@2024-01-01' = {
  name: take('deploystorage${uniqueString(resourceGroup().id)}', 24)
  kind: 'StorageV2'
  location: location
  sku: {
    name: 'Standard_GRS'
  }
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: true
    allowSharedKeyAccess: false
    minimumTlsVersion: 'TLS1_2'
    networkAcls: {
      defaultAction: 'Allow'
    }
  }
  tags: {
    'aspire-resource-name': 'deploy-storage'
  }
}

output blobEndpoint string = deploy_storage.properties.primaryEndpoints.blob

output queueEndpoint string = deploy_storage.properties.primaryEndpoints.queue

output tableEndpoint string = deploy_storage.properties.primaryEndpoints.table

output name string = deploy_storage.name