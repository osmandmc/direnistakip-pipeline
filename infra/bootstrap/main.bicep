// One-time bootstrap (T2): resource group + the identity GitHub Actions signs in as.
// Run once by an Owner of the subscription, e.g. from Azure Cloud Shell:
//   az deployment sub create --location westeurope --template-file infra/bootstrap/main.bicep
// Safe to re-run. Pipeline resources themselves come from infra/main.bicep via the infra workflow.

targetScope = 'subscription'

param location string = 'westeurope'
param environmentName string = 'prod'
param resourceGroupName string = 'rg-direnistakip-${environmentName}'

@description('GitHub repository allowed to sign in, as owner/name.')
param githubRepo string = 'osmandmc/direnistakip-pipeline'

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
  tags: {
    project: 'direnistakip-pipeline'
    environment: environmentName
  }
}

module identity 'identity.bicep' = {
  scope: rg
  name: 'github-deploy-identity'
  params: {
    location: location
    githubRepo: githubRepo
    githubEnvironment: environmentName
  }
}

// Values for the GitHub repository variables.
output AZURE_CLIENT_ID string = identity.outputs.clientId
output AZURE_TENANT_ID string = tenant().tenantId
output AZURE_SUBSCRIPTION_ID string = subscription().subscriptionId
output AZURE_RESOURCE_GROUP string = rg.name
