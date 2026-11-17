# POC infrastructure (Bicep)

Subscription-scope Bicep for the transaction processor POC. One command deploys, one command tears down. Everything lives in one resource group in Australia East, so deleting the group leaves nothing behind.

## Layout

| Path | Purpose |
|---|---|
| `main.bicep` | Entry point. Creates the resource group and calls the modules. Holds the shared names and the SEAM markers for tickets 06 to 08. |
| `main.bicepparam` | The single place for parameter values: region, resource group name, naming prefix, address ranges, VM size, budget amounts. Personal values (operator IP, SSH key, alert email) are read from environment variables, so they are never committed. |
| `modules/network.bicep` | VNet with three subnets (App Service integration, private endpoints, tooling VM), tooling subnet NSG, private DNS zones (`privatelink.servicebus.windows.net` shared by Service Bus and Event Hubs, and the SQL zone) linked to the VNet. |
| `modules/tooling-vm.bicep` | Ubuntu 24.04 VM (`Standard_D4s_v5`, 4 vCPU) with a public IP for SSH only, SSH key auth, and a `userAssignedIdentityIds` input for ticket 07. |
| `modules/cloud-init.yaml` | VM bootstrap: .NET 10 SDK, `sqlcmd`, Azure CLI, git, jq. Writes `/var/lib/tooling-ready` when done (log at `/var/log/tooling-setup.log`). |
| `modules/budget.bicep` | Resource-group budget (AU$50 ceiling) with actual-spend alerts at AU$20, AU$35 and AU$45 (computed as 40%, 70% and 90%). |
| `scripts/deploy.ps1` | One-command deploy (or `-WhatIf`). |
| `scripts/teardown.ps1` | One-command delete of the resource group. |

### Extending (tickets 06, 07, 08)

Add modules to `main.bicep` at the `SEAM` comments and new parameters to `main.bicepparam`. Do not restructure the foundation. Outputs you can use: `network.outputs.privateEndpointSubnetId`, `appServiceSubnetId`, `serviceBusDnsZoneId`, `sqlDnsZoneId`, and `uniqueSuffix` for globally unique names. The tooling VM already accepts the tooling identity through `userAssignedIdentityIds`.

## Commands

Run these only when a real deployment has been approved. Always what-if first.

```powershell
./infra/scripts/deploy.ps1 -OperatorIp <your public IP> -BudgetEmail <address> -WhatIf   # what-if, changes nothing
./infra/scripts/deploy.ps1 -OperatorIp <your public IP> -BudgetEmail <address>           # deploy
./infra/scripts/teardown.ps1                                                             # delete everything
```

Offline validation (no Azure resources contacted):

```powershell
az bicep build --file infra/main.bicep --stdout > $null
az bicep lint --file infra/main.bicep
$env:POC_OPERATOR_IP_CIDR='203.0.113.10/32'; $env:POC_SSH_PUBLIC_KEY='ssh-ed25519 AAAA...'; $env:POC_BUDGET_EMAIL='a@example.com'
az bicep build-params --file infra/main.bicepparam --stdout > $null
```

## Checks to run when a real deployment is approved

Nothing below has been run. The offline work was limited to `az bicep build`, `az bicep lint` and `az bicep build-params`. Record the results in the table at the end of this file.

1. **.NET 10 on App Service Linux in Australia East.** `az webapp list-runtimes --os linux` and look for `DOTNETCORE:10.0`. Microsoft announced .NET 10 support on App Service Linux at the .NET 10 release, but availability per region must be confirmed. If it is not listed in Australia East, use `DOTNETCORE:8.0` and set `net8.0` as the processor target (spec fallback).
2. **Zone redundancy per service in Australia East.** Australia East has availability zones. Confirm each service on the deployed resource, not from documentation (the docs conflicted for Service Bus Standard and for the App Service minimum instance count):
   - App Service plan Premium v3: `zoneRedundant` is true on the plan, and confirm the minimum instance count it accepts (documents give two figures; the POC pins 3).
   - Service Bus Premium: `zoneRedundant` on the namespace.
   - Event Hubs Standard: `zoneRedundant` on the namespace.
   - SQL Database General Purpose provisioned 4 vCores: `zoneRedundant` is true on the database, and confirm the chosen hardware generation supports it in this region.
   - The tooling VM and its public IP are not zone-pinned (a Standard public IP is zone-redundant by default in zoned regions). The VM is throwaway tooling, so a zone failure is accepted.
3. **What-if is clean.** Run `deploy.ps1 -WhatIf` and check it shows only creates (resource group, VNet, NSG, two DNS zones with links, public IP, NIC, VM, budget) and no errors or surprises. Do this before the first real deployment.
4. **VM quota.** Confirm the subscription has at least 4 vCPU of the `Standard Dsv5 Family` in Australia East, and change `toolingVmSize` in `main.bicepparam` if not.
5. **Budget currency.** Budget amounts are in the subscription billing currency. Confirm it is AUD, otherwise adjust `budgetAmount` and `budgetAlertAmounts`. Budget alerts are emails and lag actual spend by up to a day, so they do not replace tearing the environment down.
6. **After deploy.** Check SSH works from the operator IP and is refused from elsewhere, `/var/lib/tooling-ready` exists on the VM, `dotnet --list-sdks` shows 10.x, and `sqlcmd --version` and `az version` work. Then run `teardown.ps1` and confirm `az group exists --name rg-txproc-poc` returns `false`.

## Results of the checks

| Check | Result | Date | Notes |
|---|---|---|---|
| .NET 10 on App Service Linux, Australia East | not yet checked | | fallback is .NET 8 |
| Zone redundancy: App Service Premium v3 | not yet checked | | |
| Zone redundancy: Service Bus Premium | not yet checked | | |
| Zone redundancy: Event Hubs Standard | not yet checked | | |
| Zone redundancy: SQL Database General Purpose | not yet checked | | |
| What-if clean | not yet run | | |
