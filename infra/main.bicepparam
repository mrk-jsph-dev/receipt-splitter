// Settings for main.bicep. No secrets here. The image and alert email come from the environment
// that infra/deploy.sh sets, so this file stays the same between releases.
using 'main.bicep'

param namePrefix = 'receipt-splitter'
param webLocation = 'eastasia'

param budgetAmount = 1
// Must be the first of a month. Azure doesn't allow changing it once the budget exists.
param budgetStartDate = '2026-11-01'

param apiImage = readEnvironmentVariable('API_IMAGE', '')
param budgetAlertEmail = readEnvironmentVariable('BUDGET_ALERT_EMAIL')
