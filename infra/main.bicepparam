using 'main.bicep'

param environmentName = 'prod'
param location = 'swedencentral'

// Placeholder until the budget is decided (see "Cost options" in architecture.md).
param monthlyBudget = 5
param budgetStartDate = '2026-10-01'

// The RSS timer (E7): reads Evrensel's and Kızılbayrak's feeds every two hours. Switched on 2026-10-21.
param feedCollectionEnabled = true

// Kept out of the repository; set as a GitHub variable (BUDGET_EMAIL) or local env var.
param budgetContactEmail = readEnvironmentVariable('BUDGET_EMAIL', '')
