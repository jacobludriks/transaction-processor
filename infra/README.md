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
| `modules/monitoring.bicep`, `modules/app-service.bicep` | Application Insights, and the processor host: App Service plan (Linux, P1v3, zone-redundant, 3 pinned instances) and web app (see "Processor host"). |
| `scripts/deploy.ps1` | One-command deploy (or `-WhatIf`). Also takes `-ProcessorInstanceCount`, `-MaxConcurrentCalls`, `-PrefetchCount`, `-ProcessorCount`, `-RuntimeStack`. |
| `scripts/deploy-app.ps1` | Publishes the host and deploys the package to the web app (run after `deploy.ps1`). |
| `scripts/teardown.ps1` | One-command delete of the resource group. |

### Data services (ticket 06)

- Names (in `main.bicep`, `names`): SQL server `txproc-sql-<suffix>`, database `txproc`, Service Bus `txproc-sb-<suffix>` with topic `transactions-in`, subscription `processor` and queue `transactions-recognised`, Event Hubs `txproc-eh-<suffix>` with event hub `transactions-unrecognised`. The suffix is `uniqueSuffix`. Deployment outputs list the names and FQDNs.
- **SQL admin, no password.** The server is Entra-only. `deploy.ps1` takes `-SqlAdminObjectId` and `-SqlAdminName` (Entra object id and name of the admin user or group). Ticket 07 connects as this admin from the VM to create the identities as SQL users (`az login` as that user, then `scripts/vm/setup-sql-users.sh`).
- **Scaling SQL between runs.** Redeploy with a different capacity, for example `deploy.ps1 ... -SqlSkuCapacity 8`. The value flows through `POC_SQL_SKU_CAPACITY` into `sqlSkuCapacity` in `main.bicepparam`; nothing else is rebuilt. Tier, family and name are the `sqlSku*` parameters in the same file. Note that a scale operation on SQL Database can briefly drop connections, and zone redundancy must stay supported at the target size.
- **Service Bus "no filters".** No rule is declared on the subscription, so Service Bus creates its own `$Default` rule that matches every message. The subscription therefore receives all messages. The default maximum delivery count of 10 is also set explicitly on the subscription and the queue so it is visible and checkable.
- **No connection strings.** Local (SAS) authentication is disabled on Service Bus and Event Hubs, so only managed identities can authenticate (ticket 07 assigns the roles).
- **DNS.** Service Bus and Event Hubs both register in `privatelink.servicebus.windows.net`; SQL registers in `privatelink.database.windows.net`. The private endpoints sit in `snet-privateendpoints`.

### Extending (tickets 07, 08)

Tickets 06 to 08 are in place. Add further modules to `main.bicep` at the `SEAM` comments and new parameters to `main.bicepparam`. Do not restructure the foundation. Outputs you can use: `network.outputs.privateEndpointSubnetId`, `appServiceSubnetId`, `serviceBusDnsZoneId`, `sqlDnsZoneId`, and `uniqueSuffix` for globally unique names. The processor identity outputs (`identities.outputs.processorIdentityId`, `processorIdentityClientId`) are consumed by the processor host.

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

It prints PASS or FAIL per check and exits non-zero on any failure. Run it before the processor is started if you can. It gets tokens for each identity from the VM's identity endpoint and calls the Service Bus and Event Hubs REST endpoints, and signs in to SQL with `sqlcmd --authentication-method ActiveDirectoryManagedIdentity -U <client id>`. It checks:

- tooling sends to the topic (accepted), and the processor cannot (401/403);
- tooling can read the subscription, the queue and both dead-letter queues; the processor can read the subscription but not the queue;
- processor sends to the queue and event hub; tooling cannot send to the event hub;
- tooling creates a scratch table and writes a row; the processor can read it, its insert is refused (`permission was denied`), and it cannot create a table. The scratch table is dropped at the end (`--keep` leaves it).

Event Hubs receive is an AMQP operation, so "tooling can receive from the event hub" is not covered by curl. Confirm it with the throwaway event hub reader, or check the role assignment on the event hub in the portal.

Token audiences: the script requests a Service Bus token for the resource `https://servicebus.azure.net` and an Event Hubs token for `https://eventhubs.azure.net`. They are different Microsoft Entra resources (see [Authenticate an application to access Service Bus](https://learn.microsoft.com/azure/service-bus-messaging/authenticate-application) and [Authenticate an application to access Event Hubs](https://learn.microsoft.com/azure/event-hubs/authenticate-application)). The earlier version asked for the Service Bus resource on the Event Hubs calls, which the documentation does not support.

**The probe messages are identifiable, and one of them will dead-letter.** Every message the script sends carries the application property `PocAccessCheck=true` and a body that says it is an access check, not a transaction. The probe sent to the input topic is valid XML with no `itemId`, so once the processor runs it is dead-lettered with reason `MissingItemId` (if the script ran before the processor started, this happens at the processor's first start). **Ignore or purge that dead-letter before a benchmark run** (and the probe left in the output queue and event hub), so it cannot be mistaken for an unexplained dead-letter when checking the "no unexplained dead-letters" go/no-go criterion. Identify it by the `PocAccessCheck` property or the "Access check from verify-access.sh" body text.

The setup and verify scripts, and the REST and `sqlcmd` behaviours they rely on (for example `AZURE_TOKEN_CREDENTIALS=AzureCLICredential` being honoured by the installed `sqlcmd`, and bearer-token REST calls through the private endpoints), have been syntax-checked only. They have not run against Azure.

## Processor host (ticket 08)

The processor is a .NET background service plus a tiny HTTP endpoint (`src/TransactionProcessor.Host`). It contains no routing or lookup logic: it reads the subscription with `ServiceBusProcessor` (peek-lock, auto-complete off), gives each body to the handler, and settles the message from the handler's outcome:

| Handler outcome | Receive loop action |
|---|---|
| send to queue / send to event hub (output already sent) | complete the message |
| dead-letter with reason | dead-letter it; the reason becomes the Service Bus dead-letter reason |
| retry (database error or failed send) | abandon it; Service Bus redelivers it (and dead-letters it after 10 deliveries) |
| unexpected exception | abandon it and log |

### What Bicep creates

- `modules/monitoring.bicep`: Log Analytics workspace and a workspace-based Application Insights component.
- `modules/app-service.bicep`: App Service plan (Linux, Premium v3 `P1v3`, `zoneRedundant: true`, 3 instances; there is no autoscale resource, so the count is pinned) and the web app: HTTPS only, **Always On**, health check path `/health`, VNet integration into the App Service subnet, the processor user-assigned identity attached, runtime `DOTNETCORE|10.0` (parameter `processorRuntimeStack`, fallback `DOTNETCORE|8.0`), FTP disabled.
- Names: plan `txproc-plan`, app `txproc-app-<suffix>`. Deployment outputs: `processorAppName`, `processorAppHostName`, `appServicePlanName`, `appInsightsName`.

### Configuration (app settings, no code change)

All settings are plain app settings on the web app, set from Bicep. On App Service Linux `:` is not allowed in setting names, so they use `__`.

| Setting | Source |
|---|---|
| `Processor__ManagedIdentityClientId` | `processorIdentityClientId`. Used for `DefaultAzureCredential` (`ManagedIdentityClientId`) and as the SQL `User Id` with `Authentication=Active Directory Managed Identity`. No connection string, key or password exists. |
| `Processor__ServiceBusNamespace`, `__TopicName`, `__SubscriptionName`, `__QueueName` | Service Bus module outputs |
| `Processor__EventHubsNamespace`, `__EventHubName` | Event Hubs module outputs |
| `Processor__SqlServer`, `__SqlDatabase` | SQL module outputs |
| `Processor__MaxConcurrentCalls` | `processorMaxConcurrentCalls` (default 16, total per instance; `deploy.ps1 -MaxConcurrentCalls`, env `POC_MAX_CONCURRENT_CALLS`) |
| `Processor__PrefetchCount` | `processorPrefetchCount` (default 150, total per instance; `deploy.ps1 -PrefetchCount`, env `POC_PREFETCH_COUNT`). Keep it at or above the concurrency. |
| `Processor__ProcessorCount` | `processorCount` (default 4; `deploy.ps1 -ProcessorCount`, env `POC_PROCESSOR_COUNT`). Service Bus processors per instance, each with its own connection; concurrency and prefetch are split between them. Must not exceed the concurrency. |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | the Application Insights component. Telemetry is off when it is not set (local runs). |

The instance count is `processorInstanceCount` (default 3; `deploy.ps1 -ProcessorInstanceCount`, env `POC_PROCESSOR_INSTANCE_COUNT`). To change concurrency or instance count between benchmark runs, redeploy with the new value; nothing else is rebuilt and the app restarts with the new setting. The host refuses to start (and `/health` never goes green) if a required setting is missing.

`/health` returns 200 while the receive loop is running and 503 otherwise. It does not probe Service Bus or SQL, so a dependency outage does not make App Service recycle otherwise healthy instances.

### Deploy the app (not run yet)

`deploy.ps1` creates the plan and app, and `teardown.ps1` deletes them with the resource group. The package is deployed separately, after the infrastructure exists:

```powershell
./infra/scripts/deploy-app.ps1                   # dotnet publish (Release), zip, az webapp deploy; app name read from the deployment outputs
./infra/scripts/deploy-app.ps1 -Runtime net8.0   # only if .NET 10 is unavailable (after deploy.ps1 -RuntimeStack 'DOTNETCORE|8.0')
```

The zip goes to the app's SCM endpoint over the public network, so run it from the laptop. The .NET 8 fallback publishes the same code with `-p:ProcessorTargetFramework=net8.0` (an offline publish with that property builds).

Outbound routing: the app routes all traffic through the VNet (`processorRouteAllTraffic`, default `true`). Private endpoints resolve and connect either way, but Application Insights ingestion then leaves through the VNet subnet and needs outbound internet from it. If no telemetry arrives while messages flow, set `processorRouteAllTraffic` to `false` in `main.bicepparam` and redeploy.

### Smoke test (from the tooling VM, after deploy.ps1, setup-sql-users.sh and deploy-app.ps1)

Needs: infrastructure deployed, SQL users created and `verify-access.sh` passing, the app deployed and `https://<app>.azurewebsites.net/health` returning 200. The tooling identity can send to the topic and read the queue and the dead-letter queues. Replace `<sb>`, `<sql>` and `<tooling-client-id>` with the deployment outputs.

1. **Reference data.** If the seeder (ticket 09) has not run, create one item and one SKU as the tooling identity:

   ```bash
   sqlcmd -S <sql>.database.windows.net -d txproc --authentication-method ActiveDirectoryManagedIdentity -U <tooling-client-id> -b -Q "
   IF OBJECT_ID('dbo.Item') IS NULL CREATE TABLE dbo.Item (ItemId BIGINT NOT NULL PRIMARY KEY, ExternalItemId NVARCHAR(64) NOT NULL, Name NVARCHAR(200) NULL, Category NVARCHAR(100) NULL, CreatedUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME());
   IF OBJECT_ID('dbo.Sku')  IS NULL CREATE TABLE dbo.Sku  (SkuId NVARCHAR(64) NOT NULL PRIMARY KEY, Description NVARCHAR(200) NULL, Brand NVARCHAR(100) NULL, CreatedUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME());
   IF NOT EXISTS (SELECT 1 FROM dbo.Item WHERE ItemId = 1) INSERT dbo.Item (ItemId, ExternalItemId) VALUES (1, 'EXT-1');
   IF NOT EXISTS (SELECT 1 FROM dbo.Sku WHERE SkuId = 'SKU-0000001') INSERT dbo.Sku (SkuId) VALUES ('SKU-0000001');"
   ```

   Item 1 and `SKU-0000001` follow the seeded key scheme (external id `EXT-1`). Item 99999999 and `SKU-9999999` are never seeded.

2. **Send four messages** to the topic as the tooling identity (REST, no secrets):

   ```bash
   TOKEN=$(curl -sS -H Metadata:true "http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&client_id=<tooling-client-id>&resource=https://servicebus.azure.net" | jq -r .access_token)
   send() { curl -sS -o /dev/null -w '%{http_code}\n' -X POST "https://<sb>.servicebus.windows.net/transactions-in/messages" -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/atom+xml;type=entry;charset=utf-8' --data "$1"; }
   send '<transaction><transactionId>smoke-known</transactionId><itemId>1</itemId><skuId>SKU-0000001</skuId></transaction>'
   send '<transaction><transactionId>smoke-unknown-sku</transactionId><itemId>1</itemId><skuId>SKU-9999999</skuId></transaction>'
   send '<transaction><transactionId>smoke-malformed</itemId></transaction'
   send '<transaction><transactionId>smoke-unknown-item</transactionId><itemId>99999999</itemId><skuId>SKU-0000001</skuId></transaction>'
   ```

   Each call prints `201`.

3. **Check where each ended up** (allow a few seconds):

   | Message | Expected result | How to check |
   |---|---|---|
   | known SKU | one JSON message in the output queue `transactions-recognised`, with `ExternalItemId` `EXT-1` | `POST https://<sb>.servicebus.windows.net/transactions-recognised/messages/head?timeout=5` with the same token (peek-lock read: 201 and the body; use `DELETE` instead of `POST` to remove it) |
   | unknown SKU | one event in the event hub `transactions-unrecognised`, nothing extra in the queue | Event Hubs receive is AMQP only: the namespace metric "Incoming Messages" in the portal goes up by 1, or use the throwaway event hub reader |
   | malformed XML | in the subscription dead-letter queue with reason `InvalidXml` | `POST .../transactions-in/subscriptions/processor/$deadletterqueue/messages/head?timeout=5`; the `DeadLetterReason` response header carries the reason (use `curl -i` to see headers) |
   | unknown item | in the subscription dead-letter queue with reason `ItemNotFound` | same dead-letter read |

   The subscription itself must be empty afterwards (a message is completed only after its output was sent). Application Insights shows the receive and send operations for each message.

4. **A failed send leaves the message for redelivery** (optional): remove the processor's Azure Service Bus Data Sender role on the output queue, send the known-SKU message, and check that it is not completed: its delivery count rises on the subscription, and it dead-letters with `MaxDeliveryCountExceeded` after 10 deliveries. Restore the role by redeploying.

5. **Instances and zones.** `az appservice plan show -g rg-txproc-poc -n txproc-plan --query "{sku:sku, zoneRedundant:zoneRedundant}"` shows capacity 3 and `zoneRedundant: true`. In the portal (the web app, Scale out and Health check) all 3 instances are running and healthy. Record the results in the table below.

### Telemetry for benchmarks (ticket 11)

Besides Application Insights traces, the host exports pre-aggregated metrics (counters by outcome and a latency histogram, meter `TransactionProcessor`) to the same Application Insights component, once a minute and unsampled. They need no extra setting and no extra Azure resource. `src/RunReport/README.md` describes the instruments, the KQL and `az monitor metrics` queries that read them (Application Insights query API and Azure Monitor metrics API, so no public access to the data services), and the report tool that turns a run into a verdict.

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
3. **What-if is clean.** Run `deploy.ps1 -WhatIf` and check it shows only creates (resource group, VNet, NSG, two DNS zones with links, public IP, NIC, VM, budget, SQL server and database, Service Bus namespace with topic, subscription and queue, Event Hubs namespace and event hub, three private endpoints with DNS zone groups, Log Analytics workspace, Application Insights, App Service plan and web app) and no errors or surprises. Do this before the first real deployment.
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
| Processor host: app deployed by `deploy.ps1` and removed by `teardown.ps1`; 3 instances running, plan zone-redundant | not yet checked | | |
| Processor host authenticates to Service Bus, Event Hubs and SQL as the processor identity only (no secrets in app settings) | not yet checked | | |
| Smoke test: known SKU ends up in the output queue | not yet checked | | |
| Smoke test: unknown SKU ends up in the event hub | not yet checked | | |
| Smoke test: malformed message ends up in the dead-letter queue with `InvalidXml` | not yet checked | | |
| Smoke test: unknown item ends up in the dead-letter queue with `ItemNotFound` | not yet checked | | |
| Failed send leaves the message for redelivery (not completed) | not yet checked | | |
| Concurrency and instance count change by redeploy only, no code change | not yet checked | | |
| `/health` responds 200, Always On enabled | not yet checked | | |
| Application Insights receives telemetry (else set `processorRouteAllTraffic` to false) | not yet checked | | |
| No connection string or password in any template, script or setting | not yet checked | | grep the repo and the deployed app settings |
