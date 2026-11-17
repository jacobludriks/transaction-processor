// Single place for POC parameters. Tickets 06 and 07 add their values here.
//
// Values that are personal (operator IP, SSH key, alert email) are read from
// environment variables so they are never committed. scripts/deploy.ps1 sets
// them from its own arguments.
using 'main.bicep'

param location = 'australiaeast'
param resourceGroupName = 'rg-txproc-poc'
param namePrefix = 'txproc'

param operatorIpCidr = readEnvironmentVariable('POC_OPERATOR_IP_CIDR', '')
param sshPublicKey = readEnvironmentVariable('POC_SSH_PUBLIC_KEY', '')
param budgetContactEmails = [
  readEnvironmentVariable('POC_BUDGET_EMAIL', '')
]

param toolingVmSize = 'Standard_D4s_v5'
param toolingVmAdminUser = 'poc'

param vnetAddressPrefix = '10.20.0.0/16'
param appServiceSubnetPrefix = '10.20.1.0/24'
param privateEndpointSubnetPrefix = '10.20.2.0/24'
param toolingSubnetPrefix = '10.20.3.0/24'

// Budget: ceiling AU$50, alerts at AU$20, AU$35 and AU$45.
param budgetAmount = 50
param budgetAlertAmounts = [
  20
  35
  45
]
