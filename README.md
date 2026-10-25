# direnistakip-pipeline

Collects labour news articles and pre-fills case drafts for review in the direnistakip app. Design and rationale: [decisions.md](decisions.md).

## Layout

| Path | Kind |
|---|---|
| `src/Pipeline.Contracts` | Messages and API DTOs (NuGet for the app) |
| `src/Pipeline.Core` | Domain logic, no Azure references |
| `src/Pipeline.Functions` | Function App: stages and HTTP API |
| `tests/` | Tests (`Pipeline.Infrastructure.Tests` needs Docker running) |
| `tools/FixtureTrimmer` | Trims saved pages into parser test fixtures: `dotnet run --project tools/FixtureTrimmer -- page.html fixture.html` |
| `infra/` | Bicep |
| `.github/workflows/` | CI/CD |

## Local development

Runs the whole pipeline on your machine against emulators: nothing touches Azure (F11).

**Once:** Docker Desktop, the .NET 9 SDK and Azure Functions Core Tools v4
(`brew tap azure/functions && brew install azure-functions-core-tools@4`), then

```sh
cp src/Pipeline.Functions/local.settings.example.json src/Pipeline.Functions/local.settings.json
```

Optional: put a **test** Gemini key (its own Google project, so it never uses up the pipeline's daily limit) in
`Gemini__ApiKey` in that file; without one the AI stages do nothing. `local.settings.json` is not committed.

**Each time:**

```sh
docker compose up -d                       # Azurite (blob, queue, table) and the Cosmos DB emulator
cd src/Pipeline.Functions && func start    # the functions on http://localhost:7071
```

At startup the database, containers, queues and the blob container are created in the emulators. No function key is
needed locally. To bring articles in: `curl -X POST "http://localhost:7071/api/collect?source=evrensel"` (the feed timer
stays off locally: `FeedCollection__Enabled` is false), or paste links in the app run locally against this pipeline.
Cosmos data can be browsed at http://localhost:1234; it is lost when the emulator stops. `docker compose down` stops
both.

**Tests:** `dotnet test` (the storage tests start their own emulators in Docker).

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

# A new subscription must register each service family once. The deploy identity
# only has rights on the resource group, so it cannot do this itself.
for p in Microsoft.Web Microsoft.Storage Microsoft.OperationalInsights Microsoft.Insights \
         Microsoft.DocumentDB Microsoft.KeyVault Microsoft.Consumption; do
  az provider register --namespace $p
done

az deployment sub create --name bootstrap --location swedencentral \
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
