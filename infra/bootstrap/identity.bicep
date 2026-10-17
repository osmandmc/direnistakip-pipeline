// User-assigned managed identity for GitHub Actions, trusted via OIDC (no stored secret).

param location string
param githubRepo string
param githubEnvironment string

var roles = {
  contributor: 'b24988ac-6180-42a0-ab88-20f7382dd24c'
  rbacAdministrator: 'f58310d9-a9f6-439a-9e8d-f62e7b41a168'
}

// Roles main.bicep assigns to the Function App; the deploy identity may grant only these.
var grantableRoles = [
  'b7e6dc6d-f1e8-4753-8033-0f276bb0955b' // Storage Blob Data Owner
  '974c5e8b-45b9-4653-ba55-5f855dd0fb88' // Storage Queue Data Contributor
  '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3' // Storage Table Data Contributor
  '4633458b-17de-408a-b874-0445c86b69e6' // Key Vault Secrets User
]
var grantableList = join(grantableRoles, ', ')

// Which GitHub workflow runs may sign in: deploy jobs (environment) and PR what-if previews.
var federatedSubjects = [
  { name: 'github-env-${githubEnvironment}', subject: 'repo:${githubRepo}:environment:${githubEnvironment}' }
  { name: 'github-pull-request', subject: 'repo:${githubRepo}:pull_request' }
]

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-github-deploy'
  location: location
}

// Federated credentials on one identity cannot be written in parallel.
@batchSize(1)
resource federated 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2023-01-31' = [
  for f in federatedSubjects: {
    parent: identity
    name: f.name
    properties: {
      issuer: 'https://token.actions.githubusercontent.com'
      subject: f.subject
      audiences: ['api://AzureADTokenExchange']
    }
  }
]

resource contributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, identity.id, roles.contributor)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.contributor)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// Constrained: may create/delete role assignments only for the roles listed above.
resource rbacAdministrator 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, identity.id, roles.rbacAdministrator)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.rbacAdministrator)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    conditionVersion: '2.0'
    condition: '((!(ActionMatches{\'Microsoft.Authorization/roleAssignments/write\'})) OR (@Request[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {${grantableList}})) AND ((!(ActionMatches{\'Microsoft.Authorization/roleAssignments/delete\'})) OR (@Resource[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {${grantableList}}))'
  }
}

output clientId string = identity.properties.clientId
