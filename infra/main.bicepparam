using 'main.bicep'

param environmentName = 'prod'
param location = 'swedencentral'

// Placeholder until the budget is decided (see "Cost options" in architecture.md).
param monthlyBudget = 20
param budgetStartDate = '2026-10-01'

// Kept out of the repository; set as a GitHub variable (BUDGET_EMAIL) or local env var.
param budgetContactEmail = readEnvironmentVariable('BUDGET_EMAIL', '')
