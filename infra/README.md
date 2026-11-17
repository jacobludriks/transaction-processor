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
| `modules/identities.bicep` | The processor and tooling user-assigned managed identities (name, id, client ID and principal ID as outputs). |
| `modules/role-assignments.bicep` | Built-in data-plane roles for both identities, scoped to the topic, subscription, queue and event hub (table in "Identities, roles and SQL users"). |
| `scripts/vm/setup-sql-users.sh`, `setup-sql-users.sql` | Run on the VM as the Entra admin: creates a contained SQL user per identity and grants roles. Idempotent. |
| `scripts/vm/verify-access.sh` | Run on the VM: proves each identity can do what it should and is refused the rest. |
| `scripts/deploy.ps1` | One-command deploy (or `-WhatIf`). |
| `scripts/teardown.ps1` | One-command delete of the resource group. |

### Data services (ticket 06)

- Names (in `main.bicep`, `names`): SQL server `txproc-sql-<suffix>`, database `txproc`, Service Bus `txproc-sb-<suffix>` with topic `transactions-in`, subscription `processor` and queue `transactions-recognised`, Event Hubs `txproc-eh-<suffix>` with event hub `transactions-unrecognised`. The suffix is `uniqueSuffix`. Deployment outputs list the names and FQDNs.
- **SQL admin, no password.** The server is Entra-only. `deploy.ps1` takes `-SqlAdminObjectId` and `-SqlAdminName` (Entra object id and name of the admin user or group). Ticket 07 connects as this admin from the VM to create the identities as SQL users (`az login` as that user, then `scripts/vm/setup-sql-users.sh`).
- **Scaling SQL between runs.** Redeploy with a different capacity, for example `deploy.ps1 ... -SqlSkuCapacity 8`. The value flows through `POC_SQL_SKU_CAPACITY` into `sqlSkuCapacity` in `main.bicepparam`; nothing else is rebuilt. Tier, family and name are the `sqlSku*` parameters in the same file. Note that a scale operation on SQL Database can briefly drop connections, and zone redundancy must stay supported at the target size.
- **Service Bus "no filters".** No rule is declared on the subscription, so Service Bus creates its own `$Default` rule that matches every message. The subscription therefore receives all messages. The default maximum delivery count of 10 is also set explicitly on the subscription and the queue so it is visible and checkable.
- **No connection strings.** Local (SAS) authentication is disabled on Service Bus and Event Hubs, so only managed identities can authenticate (ticket 07 assigns the roles).
- **DNS.** Service Bus and Event Hubs both register in `privatelink.servicebus.windows.net`; SQL registers in `privatelink.database.windows.net`. The private endpoints sit in `snet-privateendpoints`.

### Extending (tickets 07, 08)

Add modules to `main.bicep` at the `SEAM` comments and new parameters to `main.bicepparam`. Do not restructure the foundation. Outputs you can use: `network.outputs.privateEndpointSubnetId`, `appServiceSubnetId`, `serviceBusDnsZoneId`, `sqlDnsZoneId`, and `uniqueSuffix` for globally unique names. Ticket 07 is in place: use `identities.outputs.processorIdentityId` and `processorIdentityClientId` to attach the processor identity to the App Service and point the app at it.

## Identities, roles and SQL users (ticket 07)

Two user-assigned managed identities replace every connection string and password. The processor identity is for the App Service (ticket 08, which attaches it and points the app at it by **client ID**). The tooling identity is for the tooling VM. Their names are `txproc-processor-id` and `txproc-tooling-id` (`names` in `main.bicep`) and are also the SQL user names.

### What Bicep creates

`modules/identities.bicep` creates both identities. `modules/role-assignments.bicep` assigns built-in roles, each scoped to the single resource needed (no namespace-wide or resource-group assignments):

| Identity | Role | Scope |
|---|---|---|
| Processor | Azure Service Bus Data Receiver | the input topic's subscription `processor` |
| Processor | Azure Service Bus Data Sender | the output queue `transactions-recognised` |
| Processor | Azure Event Hubs Data Sender | the event hub `transactions-unrecognised` |
| Tooling | Azure Service Bus Data Sender | the input topic `transactions-in` |
| Tooling | Azure Service Bus Data Receiver | the subscription `processor` (reads its dead-letter queue) |
| Tooling | Azure Service Bus Data Receiver | the output queue (reads it and its dead-letter queue) |
| Tooling | Azure Event Hubs Data Receiver | the event hub |

Role definition GUIDs (checked against Microsoft's built-in roles reference, the Integration and Analytics categories): Service Bus Data Sender `69a216fc-b8fb-44d8-bc22-1f3c2cd27a39`, Service Bus Data Receiver `4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0`, Event Hubs Data Sender `2b629674-e913-4c01-ae53-ef4638d8f975`, Event Hubs Data Receiver `a638d3c7-ab3a-418d-83e6-5f17a39d4fde`.

Outputs for later tickets: `processorIdentityId`, `processorIdentityName`, `processorIdentityClientId`, `processorIdentityPrincipalId` and the same four for the tooling identity. Ticket 08 needs `processorIdentityId` and `processorIdentityClientId`.

The tooling identity is attached to the VM. The processor identity is **also** attached to the VM by default (`attachProcessorIdentityToVm`, default `true`), because the acceptance check "using the processor identity, a write is refused" must run from the VM. The processor identity cannot do anything the tooling identity cannot, except receive on the subscription and send to the queue and event hub. Set the parameter to `false` and redeploy to take it off the VM afterwards.

### Who is the SQL admin

The SQL server is Entra-only. Its administrator is the user or group passed to `deploy.ps1` as `-SqlAdminObjectId` and `-SqlAdminName`. The deployment output `sqlEntraAdminName` shows who it is. Only this admin can create the users below. Nothing else (no SQL password, no `sa`) exists.

### Create the SQL users (from the VM, as the Entra admin)

Copy the scripts to the VM and run the setup as the admin. Nothing is run against Azure by the repository itself.

```bash
# from the laptop (the VM public IP and admin user are deployment outputs)
scp -r infra/scripts/vm poc@<vm-ip>:~/vm-scripts
ssh poc@<vm-ip>

# on the VM, as the Entra admin
az login --use-device-code          # sign in as the SQL admin, not with --identity
cd ~/vm-scripts && chmod +x *.sh
./setup-sql-users.sh \
  --server <sql>.database.windows.net \
  --processor-name txproc-processor-id \
  --tooling-name txproc-tooling-id
```

It connects as the admin (and aborts if it finds it connected as a managed identity), creates a contained user for each identity with `CREATE USER ... FROM EXTERNAL PROVIDER`, then grants:

- processor: `db_datareader` only. If it ever holds `db_datawriter` or `db_ddladmin`, the script removes them.
- tooling: `db_datareader`, `db_datawriter` and `db_ddladmin` (the seeder in ticket 09 creates the item and SKU tables and their indexes).

It is idempotent: run it again at any time. It ends by listing each user and its roles. No password is used.

**If `FROM EXTERNAL PROVIDER` fails** with an error such as "Failed to find Entra principal" or "server identity does not have permission to read the directory", the SQL server cannot look names up in Entra (it needs a server identity with the Directory Readers role or the Microsoft Graph permissions, which Bicep cannot grant). Rerun with `--use-sid --processor-client-id <guid> --tooling-client-id <guid>` (the client IDs are deployment outputs). The users are then created from the client ID and need no directory lookup; the result is the same contained users.

### Verify (from the VM)

```bash
./verify-access.sh \
  --sql-server <sql>.database.windows.net \
  --servicebus <sb>.servicebus.windows.net \
  --eventhubs  <eh>.servicebus.windows.net \
  --processor-client-id <processorIdentityClientId> \
  --tooling-client-id   <toolingIdentityClientId>
```

It prints PASS or FAIL per check and exits non-zero on any failure. Run it before the processor is started. It gets tokens for each identity from the VM's identity endpoint and calls the Service Bus and Event Hubs REST endpoints, and signs in to SQL with `sqlcmd --authentication-method ActiveDirectoryManagedIdentity -U <client id>`. It checks:

- tooling sends to the topic (accepted), and the processor cannot (401/403);
- tooling can read the subscription, the queue and both dead-letter queues; the processor can read the subscription but not the queue;
- processor sends to the queue and event hub; tooling cannot send to the event hub;
- tooling creates a scratch table and writes a row; the processor can read it, its insert is refused (`permission was denied`), and it cannot create a table. The scratch table is dropped at the end (`--keep` leaves it).

Event Hubs receive is an AMQP operation, so "tooling can receive from the event hub" is not covered by curl. Confirm it with the throwaway event hub reader, or check the role assignment on the event hub in the portal.

The setup and verify scripts, and the REST and `sqlcmd` behaviours they rely on (for example `AZURE_TOKEN_CREDENTIALS=AzureCLICredential` being honoured by the installed `sqlcmd`, and bearer-token REST calls through the private endpoints), have been syntax-checked only. They have not run against Azure.

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
| Identities, roles and SQL users (ticket 07): what-if shows 2 identities and 7 role assignments | not yet checked | | |
| Role assignments visible on the topic, subscription, queue and event hub (`az role assignment list --scope ...`) | not yet checked | | |
| `setup-sql-users.sh` creates both SQL users (by name, or `--use-sid` fallback) and is idempotent on rerun | not yet checked | | note which mode was needed |
| Tooling identity: send to topic, read queue and dead-letter queues, write a SQL row | not yet checked | | `verify-access.sh` |
| Tooling identity: receive from the event hub | not yet checked | | needs the event hub reader (AMQP) |
| Processor identity: SQL read allowed, SQL write refused | not yet checked | | `verify-access.sh` |
| Processor identity: receive on subscription, send to queue and event hub; send to topic refused | not yet checked | | `verify-access.sh` |
| No connection string or password in any template, script or setting | not yet checked | | grep the repo and the deployed app settings |
