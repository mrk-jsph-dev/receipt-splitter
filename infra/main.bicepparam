// Settings for main.bicep. No secrets here. The image and alert email come from the environment
// that infra/deploy.sh sets, so this file stays the same between releases.
using 'main.bicep'

param namePrefix = 'receipt-splitter'
param webLocation = 'eastasia'

param budgetAmount = 1
// Must be the first of a month. Azure doesn't allow changing it once the budget exists.
param budgetStartDate = '2026-11-01'

param githubRepo = 'mrk-jsph-dev/receipt-splitter'

param apiImage = readEnvironmentVariable('API_IMAGE', '')
param runbookCommit = readEnvironmentVariable('RUNBOOK_COMMIT', 'main')
param createStopWebhook = bool(readEnvironmentVariable('CREATE_STOP_WEBHOOK', 'false'))
param stopWebhookUri = readEnvironmentVariable('STOP_WEBHOOK_URI', '')
param budgetAlertEmail = readEnvironmentVariable('BUDGET_ALERT_EMAIL')
