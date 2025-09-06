targetScope = 'subscription'

param resourceGroupName string

param location string

param principalId string

resource rg 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: resourceGroupName
  location: location
}

module deploy_storage 'deploy-storage/deploy-storage.bicep' = {
  name: 'deploy-storage'
  scope: rg
  params: {
    location: location
  }
}

module deploy_afd 'deploy-afd/deploy-afd.bicep' = {
  name: 'deploy-afd'
  scope: rg
  params: {
    location: location
    frontDoorName: 'deploy-afd'
    storageAccountName: deploy_storage.outputs.name
  }
}

module deploy_storage_roles 'deploy-storage-roles/deploy-storage-roles.bicep' = {
  name: 'deploy-storage-roles'
  scope: rg
  params: {
    location: location
    deploy_storage_outputs_name: deploy_storage.outputs.name
    principalType: ''
    principalId: principalId
  }
}