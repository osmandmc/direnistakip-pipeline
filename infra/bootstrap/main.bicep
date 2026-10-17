// One-time bootstrap (T2): resource group + the identity GitHub Actions signs in as.
// Run once by an Owner of the subscription, e.g. from Azure Cloud Shell:
//   az deployment sub create --location swedencentral --template-file infra/bootstrap/main.bicep
// Safe to re-run. Pipeline resources themselves come from infra/main.bicep via the infra workflow.

targetScope = 'subscription'

param location string = 'swedencentral'
param environmentName string = 'prod'
param resourceGroupName string = 'rg-direnistakip-${environmentName}'

// GitHub's immutable OIDC subject format: owner@ownerId/repo@repoId (IDs are public, not secrets).
// The exact value appears in an AADSTS700213 login error if it ever changes.
@description('GitHub repository allowed to sign in, as it appears in the OIDC subject claim.')
param githubRepo string = 'osmandmc@20256505/direnistakip-pipeline@1423618021'

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
