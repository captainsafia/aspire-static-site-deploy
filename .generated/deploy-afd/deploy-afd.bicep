param frontDoorName string
param storageAccountName string

@description('Location for all resources, needed because Aspire always injects a location parameter')
param location string = resourceGroup().location

resource frontDoorProfile 'Microsoft.Cdn/profiles@2024-02-01' = {
  name: take('${frontDoorName}${uniqueString(resourceGroup().id)}', 50)
  location: 'Global'
  sku: {
    name: 'Standard_AzureFrontDoor'
  }
}

// Front Door Endpoint
resource frontDoorEndpoint 'Microsoft.Cdn/profiles/afdEndpoints@2024-02-01' = {
  parent: frontDoorProfile
  name: take('${frontDoorName}-endp-${uniqueString(resourceGroup().id)}', 50)
  location: 'Global'
  properties: {
    enabledState: 'Enabled'
  }
}

resource originGroup 'Microsoft.Cdn/profiles/originGroups@2025-06-01' = {
  parent: frontDoorProfile
  name: take('storage-origin-group-${uniqueString(resourceGroup().id)}', 50)
  properties: {
    loadBalancingSettings: {
      sampleSize: 4
      successfulSamplesRequired: 3
      additionalLatencyInMilliseconds: 50
    }
    healthProbeSettings: {
      probePath: '/'
      probeRequestType: 'HEAD'
      probeProtocol: 'Https'
      probeIntervalInSeconds: 240
    }
    sessionAffinityState: 'Disabled'
  }
}

// Origin pointing to storage account
resource origin 'Microsoft.Cdn/profiles/originGroups/origins@2024-02-01' = {
  parent: originGroup
  name: take('storage-origin-${uniqueString(resourceGroup().id)}', 50)
  properties: {
    hostName: '${storageAccountName}.z5.web.${environment().suffixes.storage}'
    httpPort: 80
    httpsPort: 443
    originHostHeader: '${storageAccountName}.z5.web.${environment().suffixes.storage}'
    priority: 1
    weight: 1000
    enabledState: 'Enabled'
    enforceCertificateNameCheck: true
  }
}

// Route connecting endpoint to origin group with rules
resource route 'Microsoft.Cdn/profiles/afdEndpoints/routes@2024-02-01' = {
  parent: frontDoorEndpoint
  name: take('default-route-${uniqueString(resourceGroup().id)}', 50)
  properties: {
    originGroup: {
      id: originGroup.id
    }
    supportedProtocols: [
      'Http'
      'Https'
    ]
    patternsToMatch: [
      '/*'
    ]
    forwardingProtocol: 'HttpsOnly'
    linkToDefaultDomain: 'Enabled'
    httpsRedirect: 'Enabled'
    enabledState: 'Enabled'
    originPath: '/'
    ruleSets: []
  }
}

@description('Front Door endpoint URL')
output endpointUrl string = 'https://${frontDoorEndpoint.properties.hostName}'
