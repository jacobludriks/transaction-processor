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
| `modules/sql.bicep` | Azure SQL server (Entra-only authentication, no SQL password, public access disabled) and one General Purpose provisioned, zone-redundant database. SKU name, tier, family and vCores are parameters. Includes its private endpoint. |
| `modules/service-bus.bicep` | Service Bus Premium, 1 messaging unit, zone-redundant, public access disabled, local (SAS) auth disabled. One topic, one subscription (no filters, max delivery count 10) and the output queue. Includes its private endpoint. |
| `modules/event-hubs.bicep` | Event Hubs Standard, 1 throughput unit, auto-inflate off, zone-redundant, public access disabled, local auth disabled. One event hub with 2 partitions. Includes its private endpoint. |
| `modules/private-endpoint.bicep` | Reusable private endpoint plus private DNS zone group, registering in the existing zone from the network module. |
| `scripts/deploy.ps1` | One-command deploy (or `-WhatIf`). |
| `scripts/teardown.ps1` | One-command delete of the resource group. |

### Data services (ticket 06)

- Names (in `main.bicep`, `names`): SQL server `txproc-sql-<suffix>`, database `txproc`, Service Bus `txproc-sb-<suffix>` with topic `transactions-in`, subscription `processor` and queue `transactions-recognised`, Event Hubs `txproc-eh-<suffix>` with event hub `transactions-unrecognised`. The suffix is `uniqueSuffix`. Deployment outputs list the names and FQDNs.
- **SQL admin, no password.** The server is Entra-only. `deploy.ps1` takes `-SqlAdminObjectId` and `-SqlAdminName` (Entra object id and name of the admin user or group). Ticket 07 connects as this admin from the VM to create the identities as SQL users. The tooling VM needs to be able to sign in as that admin (for example `az login` as that user, then `sqlcmd -G`).
- **Scaling SQL between runs.** Redeploy with a different capacity, for example `deploy.ps1 ... -SqlSkuCapacity 8`. The value flows through `POC_SQL_SKU_CAPACITY` into `sqlSkuCapacity` in `main.bicepparam`; nothing else is rebuilt. Tier, family and name are the `sqlSku*` parameters in the same file. Note that a scale operation on SQL Database can briefly drop connections, and zone redundancy must stay supported at the target size.
- **Service Bus "no filters".** No rule is declared on the subscription, so Service Bus creates its own `$Default` rule that matches every message. The subscription therefore receives all messages. The default maximum delivery count of 10 is also set explicitly on the subscription and the queue so it is visible and checkable.
- **No connection strings.** Local (SAS) authentication is disabled on Service Bus and Event Hubs, so only managed identities can authenticate (ticket 07 assigns the roles).
- **DNS.** Service Bus and Event Hubs both register in `privatelink.servicebus.windows.net`; SQL registers in `privatelink.database.windows.net`. The private endpoints sit in `snet-privateendpoints`.

### Extending (tickets 07, 08)

Add modules to `main.bicep` at the `SEAM` comments and new parameters to `main.bicepparam`. Do not restructure the foundation. Outputs you can use: `network.outputs.privateEndpointSubnetId`, `appServiceSubnetId`, `serviceBusDnsZoneId`, `sqlDnsZoneId`, and `uniqueSuffix` for globally unique names. The tooling VM already accepts the tooling identity through `userAssignedIdentityIds`.

## Commands

Run these only when a real deployment has been approved. Always what-if first.

```powershell
./infra/scripts/deploy.ps1 -OperatorIp <your public IP> -BudgetEmail <address> -SqlAdminObjectId <entra object id> -SqlAdminName <entra upn> -WhatIf   # what-if, changes nothing
./infra/scripts/deploy.ps1 -OperatorIp <your public IP> -BudgetEmail <address> -SqlAdminObjectId <entra object id> -SqlAdminName <entra upn>           # deploy (add -SqlSkuCapacity 8 etc. to rescale SQL)
./infra/scripts/teardown.ps1                                                             # delete everything
```

Offline validation (no Azure resources contacted):

```powershell
az bicep build --file infra/main.bicep --stdout > $null
az bicep lint --file infra/main.bicep
$env:POC_OPERATOR_IP_CIDR='203.0.113.10/32'; $env:POC_SSH_PUBLIC_KEY='ssh-ed25519 AAAA...'; $env:POC_BUDGET_EMAIL='a@example.com'
$env:POC_SQL_ADMIN_OBJECT_ID='00000000-0000-0000-0000-000000000000'; $env:POC_SQL_ADMIN_NAME='admin@example.com'
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
   - Exact checks for the three data services are in "Data service checks" below.
   - The tooling VM and its public IP are not zone-pinned (a Standard public IP is zone-redundant by default in zoned regions). The VM is throwaway tooling, so a zone failure is accepted.
3. **What-if is clean.** Run `deploy.ps1 -WhatIf` and check it shows only creates (resource group, VNet, NSG, two DNS zones with links, public IP, NIC, VM, budget, SQL server and database, Service Bus namespace with topic, subscription and queue, Event Hubs namespace and event hub, three private endpoints with DNS zone groups) and no errors or surprises. Do this before the first real deployment.
4. **VM quota.** Confirm the subscription has at least 4 vCPU of the `Standard Dsv5 Family` in Australia East, and change `toolingVmSize` in `main.bicepparam` if not.
5. **Budget currency.** Budget amounts are in the subscription billing currency. Confirm it is AUD, otherwise adjust `budgetAmount` and `budgetAlertAmounts`. Budget alerts are emails and lag actual spend by up to a day, so they do not replace tearing the environment down.
6. **After deploy.** Check SSH works from the operator IP and is refused from elsewhere, `/var/lib/tooling-ready` exists on the VM, `dotnet --list-sdks` shows 10.x, and `sqlcmd --version` and `az version` work. Then run `teardown.ps1` and confirm `az group exists --name rg-txproc-poc` returns `false`.

## Data service checks (ticket 06, run after a real deployment)

Nothing here has been run. Replace `<sql>`, `<sb>`, `<eh>` with the names from the deployment outputs (`sqlServerFqdn`, `serviceBusNamespaceFqdn`, `eventHubsNamespaceFqdn`) and `<rg>` with `rg-txproc-poc`.

### A. From the tooling VM (inside the VNet): names resolve to private addresses and connections succeed

SSH to the VM, then:

```bash
# Each name must resolve to a 10.20.2.x address (the private-endpoint subnet), not a public IP.
for h in <sql>.database.windows.net <sb>.servicebus.windows.net <eh>.servicebus.windows.net; do
  echo "== $h"; getent hosts "$h"; nslookup "$h"
done

# TCP reachability: SQL 1433, Service Bus and Event Hubs 5671 (AMQP) and 443 (HTTPS/WebSockets).
nc -vz -w 5 <sql>.database.windows.net 1433
nc -vz -w 5 <sb>.servicebus.windows.net 5671
nc -vz -w 5 <sb>.servicebus.windows.net 443
nc -vz -w 5 <eh>.servicebus.windows.net 5671
nc -vz -w 5 <eh>.servicebus.windows.net 443

# Authenticated connection (SQL is Entra-only). Sign in as the Entra admin first: az login.
sqlcmd -S <sql>.database.windows.net -d txproc -G -Q "SELECT DB_NAME(), SUSER_SNAME();"
```

Expected: every name resolves to a private address and every port connects. The CNAME chain should pass through `privatelink.servicebus.windows.net` or `privatelink.database.windows.net`. If a name resolves to a public address, the zone link or the zone group is missing. Service Bus and Event Hubs have no SAS keys, so an AMQP or HTTPS login is only possible with a managed identity role (ticket 07). TCP connect plus TLS is enough for this ticket: `curl -sS -o /dev/null -w '%{http_code}\n' https://<sb>.servicebus.windows.net/` returns an HTTP status (401 or similar), which proves the endpoint is reached.

### B. From outside the VNet (operator laptop): none of the three is reachable

```powershell
foreach ($h in '<sql>.database.windows.net','<sb>.servicebus.windows.net','<eh>.servicebus.windows.net') {
  Resolve-DnsName $h | Select-Object Name, Type, IPAddress
}
Test-NetConnection <sql>.database.windows.net -Port 1433
Test-NetConnection <sb>.servicebus.windows.net -Port 5671
Test-NetConnection <eh>.servicebus.windows.net -Port 5671
curl.exe -sS -m 10 https://<sb>.servicebus.windows.net/
```

Expected: public DNS may return a public IP (the public name always exists) but connections fail or are refused with a "public network access is disabled" or 403 error (SQL error 47073 or similar). A `TcpTestSucceeded: True` followed by a rejection at the service is acceptable; a successful sign-in or data access is not. Also confirm the setting itself:

```powershell
az sql server show -g <rg> -n <sql-name> --query publicNetworkAccess -o tsv
az servicebus namespace show -g <rg> -n <sb-name> --query publicNetworkAccess -o tsv
az eventhubs namespace show -g <rg> -n <eh-name> --query publicNetworkAccess -o tsv
```

All three must print `Disabled`.

### C. Private endpoints and DNS records

```powershell
az network private-endpoint list -g <rg> --query "[].{name:name, state:privateLinkServiceConnections[0].privateLinkServiceConnectionState.status, subnet:subnet.id}" -o table
az network private-dns record-set a list -g <rg> -z privatelink.servicebus.windows.net -o table
az network private-dns record-set a list -g <rg> -z privatelink.database.windows.net -o table
```

Expected: three endpoints, status `Approved`, all in `snet-privateendpoints`; each zone holds an A record per endpoint.

### D. Zone redundancy on each deployed resource

```powershell
az sql db show -g <rg> -s <sql-name> -n txproc --query "{zoneRedundant:zoneRedundant, sku:sku.name, tier:sku.tier, capacity:sku.capacity, backup:currentBackupStorageRedundancy}" -o json
az servicebus namespace show -g <rg> -n <sb-name> --query "{zoneRedundant:zoneRedundant, sku:sku.name, units:sku.capacity}" -o json
az eventhubs namespace show -g <rg> -n <eh-name> --query "{zoneRedundant:zoneRedundant, sku:sku.name, tu:sku.capacity, autoInflate:isAutoInflateEnabled}" -o json
```

Expected: `zoneRedundant` is `true` on all three. If a service refuses the setting or reports `false`, record which one in the results table and why. Do not silently drop the flag in Bicep.

### E. Service Bus topology and SQL tier

```powershell
az servicebus topic subscription list -g <rg> --namespace-name <sb-name> --topic-name transactions-in -o table        # exactly one: processor
az servicebus topic subscription show -g <rg> --namespace-name <sb-name> --topic-name transactions-in -n processor --query maxDeliveryCount
az servicebus topic subscription rule list -g <rg> --namespace-name <sb-name> --topic-name transactions-in --subscription-name processor -o table   # only $Default, a TrueFilter
az servicebus queue show -g <rg> --namespace-name <sb-name> -n transactions-recognised --query maxDeliveryCount
az eventhubs eventhub show -g <rg> --namespace-name <eh-name> -n transactions-unrecognised --query partitionCount
```

Expected: one subscription, `maxDeliveryCount` 10, one `$Default` rule that is a TrueFilter, 2 partitions. To check scaling, rerun `deploy.ps1` with `-SqlSkuCapacity 6`, confirm `az sql db show ... --query sku.capacity` returns 6 and that the other resources are unchanged in what-if, then set it back to 4.

## Results of the checks

| Check | Result | Date | Notes |
|---|---|---|---|
| .NET 10 on App Service Linux, Australia East | not yet checked | | fallback is .NET 8 |
| Zone redundancy: App Service Premium v3 | not yet checked | | |
| Zone redundancy: Service Bus Premium | not yet checked | | |
| Zone redundancy: Event Hubs Standard | not yet checked | | |
| Zone redundancy: SQL Database General Purpose | not yet checked | | |
| What-if clean | not yet run | | |
| Names resolve to private addresses from the VM (SQL, Service Bus, Event Hubs) | not yet checked | | |
| Not reachable from outside the VNet | not yet checked | | |
| Public network access `Disabled` on all three | not yet checked | | |
| Service Bus: one subscription, `$Default` rule only, max delivery count 10 | not yet checked | | |
| SQL scale by parameter | not yet checked | | |
