# direnistakip-pipeline

Collects labour news articles and pre-fills case drafts for review in the direnistakip app. Design and rationale: [decisions.md](decisions.md).

## Layout

| Path | Kind |
|---|---|
| `src/Pipeline.Contracts` | Messages and API DTOs (NuGet for the app) |
| `src/Pipeline.Core` | Domain logic, no Azure references |
| `src/Pipeline.Functions` | Function App: stages and HTTP API |
| `tests/` | Tests |
| `infra/` | Bicep |
| `.github/workflows/` | CI/CD |

## Local development

```sh
dotnet build
dotnet test
cp src/Pipeline.Functions/local.settings.example.json src/Pipeline.Functions/local.settings.json
# Run with Azure Functions Core Tools + Azurite:  cd src/Pipeline.Functions && func start
```

## One-time Azure setup

1. Create a resource group in West Europe.
2. Create an Entra app registration with a federated credential for this repo (`environment:prod` and `pull_request` subjects). Grant it **Contributor** and **Role Based Access Control Administrator** on the resource group (the template assigns roles).
3. Create the GitHub environment `prod` and set these repository variables:

| Variable | Value |
|---|---|
| `AZURE_CLIENT_ID` | App registration client ID |
| `AZURE_TENANT_ID` | Tenant ID |
| `AZURE_SUBSCRIPTION_ID` | Subscription ID |
| `AZURE_RESOURCE_GROUP` | Resource group name |
| `BUDGET_EMAIL` | Budget alert address |
| `AZURE_FUNCTIONAPP_NAME` | From the `functionAppName` output of the first infra run |

4. Run the `infra` workflow, set `AZURE_FUNCTIONAPP_NAME`, then run `functions`.
