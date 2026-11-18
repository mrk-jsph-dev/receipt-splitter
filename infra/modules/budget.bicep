// A monthly cost budget on the resource group. It emails when actual spend passes 80% of the
// amount, or when Azure forecasts the month will pass 100%. When actual spend passes 100%, it also
// calls the kill switch's action group, which stops the API (infra/modules/killswitch.bicep).
// Cost data can lag by up to a day, so the stop can come that long after the spend.

param name string
param amount int
param startDate string
param alertEmail string

@description('Action group that stops the API.')
param stopActionGroupId string

resource budget 'Microsoft.Consumption/budgets@2023-11-01' = {
  name: name
  properties: {
    category: 'Cost'
    amount: amount
    timeGrain: 'Monthly'
    timePeriod: { startDate: startDate }
    notifications: {
      actual80: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 80
        thresholdType: 'Actual'
        contactEmails: [ alertEmail ]
      }
      actual100: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        thresholdType: 'Actual'
        contactEmails: [ alertEmail ]
        contactGroups: [ stopActionGroupId ]
      }
      forecast100: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        thresholdType: 'Forecasted'
        contactEmails: [ alertEmail ]
      }
    }
  }
}
