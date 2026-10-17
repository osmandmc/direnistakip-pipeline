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

Three layers, each deployed differently:

| Layer | Template | Runs | By |
|---|---|---|---|
| Bootstrap | `infra/bootstrap/` — resource group + GitHub deploy identity | Once | You (Owner of the subscription) |
| Infrastructure | `infra/main.bicep` | When `infra/` changes | `infra` workflow |
| Code | `src/` | When code changes | `functions` workflow |

**1. Bootstrap (Azure portal → Cloud Shell, Bash).** Check first that the subscription has no Cosmos DB free-tier account; if it has, set `cosmosFreeTier = false` in `infra/main.bicepparam`.

```sh
git clone https://github.com/osmandmc/direnistakip-pipeline && cd direnistakip-pipeline
az deployment sub create --name bootstrap --location westeurope \
  --template-file infra/bootstrap/main.bicep \
  --query properties.outputs
```

This creates `rg-direnistakip-prod` and the managed identity `id-github-deploy`. GitHub signs in as this identity via OIDC (no secret). It has Contributor on the resource group and may grant only the roles the Function App needs.

**2. GitHub (repo → Settings).**
- *Environments* → create `prod` (optionally add yourself as required reviewer).
- *Secrets and variables → Actions → Variables* → add:

| Variable | Value |
|---|---|
| `AZURE_CLIENT_ID` | Bootstrap output |
| `AZURE_TENANT_ID` | Bootstrap output |
| `AZURE_SUBSCRIPTION_ID` | Bootstrap output |
| `AZURE_RESOURCE_GROUP` | Bootstrap output |
| `BUDGET_EMAIL` | Budget alert address |

**3. Deploy.** Actions → `infra` → *Run workflow*. Copy `functionAppName` from the deployment output (portal → resource group → Deployments) into a new variable `AZURE_FUNCTIONAPP_NAME`, then run `functions`. Until these variables exist, both workflows are skipped.
