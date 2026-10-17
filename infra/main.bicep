// Pipeline infrastructure (D-013, D-014, D-020, D-021, D-022, D-026).
// Deployed at resource group scope by .github/workflows/infra.yml.

targetScope = 'resourceGroup'

@description('Short environment name, used in names and tags.')
param environmentName string = 'prod'

param location string = resourceGroup().location

@description('Monthly budget amount in the billing currency.')
param monthlyBudget int

@description('First day of the budget\'s first month (yyyy-MM-01). Keep fixed across deployments.')
param budgetStartDate string

@description('Address for budget alerts. Empty skips the budget.')
param budgetContactEmail string = ''

@description('Only one free-tier Cosmos DB account is allowed per subscription.')
param cosmosFreeTier bool = true

var suffix = uniqueString(resourceGroup().id, environmentName)
var tags = {
  project: 'direnistakip-pipeline'
  environment: environmentName
}

// One queue per stage input; poison queues are created by the Functions runtime.
var queueNames = [
  'article-submitted'
  'article-fetched'
  'article-parsed'
  'article-relevant'
  'article-extracted'
]

// Ledger: one record per article, partitioned by id (D-020).
// Stage records: immutable and versioned (D-003), several per article.
var cosmosContainers = [
  { name: 'ledger', partitionKey: '/id' }
  { name: 'parsed', partitionKey: '/articleId' }
  { name: 'relevance', partitionKey: '/articleId' }
  { name: 'drafts', partitionKey: '/articleId' }
  { name: 'rollups', partitionKey: '/id' }
]

// Built-in role definition IDs
var roles = {
  storageBlobDataOwner: 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
  storageQueueDataContributor: '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
  storageTableDataContributor: '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'
  keyVaultSecretsUser: '4633458b-17de-408a-b874-0445c86b69e6'
}

// ---------- Monitoring ----------

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${suffix}'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
    workspaceCapping: { dailyQuotaGb: 1 }
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${suffix}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
  }
}

// ---------- Storage: Functions host, queues, raw HTML ----------

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: 'st${suffix}'
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource deploymentContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'deployments'
}

resource rawHtmlContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'raw-html'
}

resource queueService 'Microsoft.Storage/storageAccounts/queueServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource queues 'Microsoft.Storage/storageAccounts/queueServices/queues@2023-05-01' = [
  for name in queueNames: {
    parent: queueService
    name: name
  }
]

// ---------- Cosmos DB (free tier, capped at the free 1000 RU/s) ----------

resource cosmos 'Microsoft.DocumentDB/databaseAccounts@2024-11-15' = {
  name: 'cosmos-${suffix}'
  location: location
  tags: tags
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    enableFreeTier: cosmosFreeTier
    disableLocalAuth: true
    minimalTlsVersion: 'Tls12'
    consistencyPolicy: { defaultConsistencyLevel: 'Session' }
    capacity: { totalThroughputLimit: 1000 }
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
  }
}

resource cosmosDb 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-11-15' = {
  parent: cosmos
  name: 'pipeline'
  properties: {
    resource: { id: 'pipeline' }
    options: { throughput: 1000 }
  }
}

resource containers 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = [
  for c in cosmosContainers: {
    parent: cosmosDb
    name: c.name
    properties: {
      resource: {
        id: c.name
        partitionKey: {
          paths: [c.partitionKey]
          kind: 'Hash'
        }
      }
    }
  }
]

// ---------- Key Vault (LLM key, later secrets) ----------

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: 'kv-${suffix}'
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

// ---------- Function App (Flex Consumption) ----------

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: 'plan-${suffix}'
  location: location
  tags: tags
  kind: 'functionapp'
  sku: {
    tier: 'FlexConsumption'
    name: 'FC1'
  }
  properties: {
    reserved: true
  }
}

resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
  name: 'func-${suffix}'
  location: location
  tags: tags
  kind: 'functionapp,linux'
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      minTlsVersion: '1.2'
      appSettings: [
        { name: 'AzureWebJobsStorage__accountName', value: storage.name }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
        { name: 'Cosmos__Endpoint', value: cosmos.properties.documentEndpoint }
        { name: 'Cosmos__Database', value: cosmosDb.name }
        { name: 'KeyVault__Uri', value: keyVault.properties.vaultUri }
        { name: 'RawHtml__Container', value: rawHtmlContainer.name }
      ]
    }
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storage.properties.primaryEndpoints.blob}${deploymentContainer.name}'
          authentication: { type: 'SystemAssignedIdentity' }
        }
      }
      scaleAndConcurrency: {
        maximumInstanceCount: 40
        instanceMemoryMB: 2048
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '9.0'
      }
    }
  }
}

// ---------- Role assignments for the Function App identity ----------

resource storageRoles 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for roleId in [roles.storageBlobDataOwner, roles.storageQueueDataContributor, roles.storageTableDataContributor]: {
    name: guid(storage.id, functionApp.id, roleId)
    scope: storage
    properties: {
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleId)
      principalId: functionApp.identity.principalId
      principalType: 'ServicePrincipal'
    }
  }
]

resource keyVaultRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, functionApp.id, roles.keyVaultSecretsUser)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.keyVaultSecretsUser)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Cosmos DB Built-in Data Contributor (data plane RBAC; local auth is disabled).
resource cosmosRole 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-11-15' = {
  parent: cosmos
  name: guid(cosmos.id, functionApp.id, 'data-contributor')
  properties: {
    roleDefinitionId: '${cosmos.id}/sqlRoleDefinitions/00000000-0000-0000-0000-000000000002'
    principalId: functionApp.identity.principalId
    scope: cosmos.id
  }
}

// ---------- Budget alert ----------

resource budget 'Microsoft.Consumption/budgets@2023-11-01' = if (!empty(budgetContactEmail)) {
  name: 'budget-${environmentName}'
  properties: {
    category: 'Cost'
    amount: monthlyBudget
    timeGrain: 'Monthly'
    timePeriod: { startDate: budgetStartDate }
    notifications: {
      actual80: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 80
        thresholdType: 'Actual'
        contactEmails: [budgetContactEmail]
      }
      forecast100: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        thresholdType: 'Forecasted'
        contactEmails: [budgetContactEmail]
      }
    }
  }
}

output functionAppName string = functionApp.name
output functionAppHostName string = functionApp.properties.defaultHostName
output storageAccountName string = storage.name
output cosmosEndpoint string = cosmos.properties.documentEndpoint
output keyVaultUri string = keyVault.properties.vaultUri
