// Resource-group-scoped budget with actual-spend alerts. Lives in the resource
// group, so deleting the group deletes the budget too.
targetScope = 'resourceGroup'

param budgetName string

@description('Budget ceiling. Alert thresholds are computed as a percentage of it.')
@minValue(1)
param budgetAmount int

@description('Spend levels that trigger an alert (same currency as the budget).')
param alertAmounts array

param contactEmails array

@description('Must be the first day of a month.')
param startDate string

resource budget 'Microsoft.Consumption/budgets@2023-11-01' = {
  name: budgetName
  properties: {
    category: 'Cost'
    amount: budgetAmount
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: startDate
      endDate: '2035-12-31'
    }
    notifications: toObject(alertAmounts, amt => 'actual-${amt}', amt => {
      enabled: true
      operator: 'GreaterThanOrEqualTo'
      threshold: (amt * 100) / budgetAmount
      thresholdType: 'Actual'
      contactEmails: contactEmails
    })
  }
}
