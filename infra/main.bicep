// Azure Integration Infrastructure for PPDS Demo
// Deploys: App Service (Web API), Function App, Service Bus, Application Insights

targetScope = 'resourceGroup'

@description('Environment name (dev, qa, prod)')
@allowed(['dev', 'qa', 'prod'])
param environment string

@description('Application name prefix')
param appNamePrefix string = 'ppds-demo'

@description('Location for resources')
param location string = resourceGroup().location

@description('Service Bus queue name for account updates')
param serviceBusQueueName string = 'account-updates'

// Computed names
var uniqueSuffix = uniqueString(resourceGroup().id)
var appServicePlanName = '${appNamePrefix}-plan-${environment}'
var webAppName = '${appNamePrefix}-api-${environment}-${uniqueSuffix}'
var functionAppName = '${appNamePrefix}-func-${environment}-${uniqueSuffix}'
var storageAccountName = replace('${appNamePrefix}st${environment}${uniqueSuffix}', '-', '')
var serviceBusNamespaceName = '${appNamePrefix}-sb-${environment}-${uniqueSuffix}'
var appInsightsName = '${appNamePrefix}-ai-${environment}'
var logAnalyticsName = '${appNamePrefix}-log-${environment}'

// Log Analytics Workspace
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: logAnalyticsName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

// Application Insights
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

// App Service Plan (shared between Web API and Function App)
resource appServicePlan 'Microsoft.Web/serverfarms@2022-09-01' = {
  name: appServicePlanName
  location: location
  sku: {
    name: environment == 'prod' ? 'P1v3' : 'B1'
    tier: environment == 'prod' ? 'PremiumV3' : 'Basic'
  }
  properties: {
    reserved: false // Windows
  }
}

// Storage Account (for Function App)
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: take(storageAccountName, 24)
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

// Service Bus Namespace
resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: serviceBusNamespaceName
  location: location
  sku: {
    name: environment == 'prod' ? 'Standard' : 'Basic'
    tier: environment == 'prod' ? 'Standard' : 'Basic'
  }
}

// Service Bus Queue
resource serviceBusQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: serviceBusNamespace
  name: serviceBusQueueName
  properties: {
    maxDeliveryCount: 10
    lockDuration: 'PT5M'
    defaultMessageTimeToLive: 'P14D'
  }
}

// Web API App Service
resource webApp 'Microsoft.Web/sites@2022-09-01' = {
  name: webAppName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      netFrameworkVersion: 'v8.0'
      appSettings: [
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'ApplicationInsightsAgent_EXTENSION_VERSION'
          value: '~3'
        }
      ]
    }
  }
}

// Function App
resource functionApp 'Microsoft.Web/sites@2022-09-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      netFrameworkVersion: 'v8.0'
      appSettings: [
        {
          name: 'AzureWebJobsStorage'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};EndpointSuffix=${az.environment().suffixes.storage};AccountKey=${storageAccount.listKeys().keys[0].value}'
        }
        {
          name: 'WEBSITE_CONTENTAZUREFILECONNECTIONSTRING'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};EndpointSuffix=${az.environment().suffixes.storage};AccountKey=${storageAccount.listKeys().keys[0].value}'
        }
        {
          name: 'WEBSITE_CONTENTSHARE'
          value: toLower(functionAppName)
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'WebApiBaseUrl'
          value: 'https://${webApp.properties.defaultHostName}'
        }
        {
          name: 'ServiceBusConnection'
          value: listKeys('${serviceBusNamespace.id}/AuthorizationRules/RootManageSharedAccessKey', serviceBusNamespace.apiVersion).primaryConnectionString
        }
      ]
    }
  }
}

// Outputs
output webAppName string = webApp.name
output webAppUrl string = 'https://${webApp.properties.defaultHostName}'
output functionAppName string = functionApp.name
output functionAppUrl string = 'https://${functionApp.properties.defaultHostName}'
output serviceBusNamespace string = serviceBusNamespace.name
output appInsightsName string = appInsights.name
output webAppPrincipalId string = webApp.identity.principalId
output functionAppPrincipalId string = functionApp.identity.principalId
