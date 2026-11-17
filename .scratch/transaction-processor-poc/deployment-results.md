# Live deployment results (2026-11-17)

First real deployment of the POC to the "Visual Studio Professional" subscription, Australia East, resource group `rg-txproc-poc`. This records what was verified live, what failed, and what deviates from the spec. Benchmarks (tickets 12 and 13) have not been run.

## Deviation from the spec

- **SQL database is not zone-redundant.** Azure refused a zone-redundant database on this subscription with `ProvisioningDisabled` ("Provisioning of zone redundant database/pool is not supported for your current request"), although the capability listing reports `GP_Gen5_4` as zone-redundant and available. The user chose to deploy the database without zone redundancy and keep everything else zone-redundant. Backups are still zone-redundant. A non-zone-redundant database has no cross-zone replication, so database latency in benchmark results is likely flattered slightly.
- Everything else was confirmed zone-redundant on the deployed resource: Service Bus Premium (1 MU), Event Hubs Standard, App Service plan P1v3 with 3 instances.

## Defects found only by the live run (all fixed and committed)

1. **VM bootstrap failed.** `apt-get install sqlcmd` does not exist on Ubuntu 24.04 (the Microsoft repo carries only the ODBC `mssql-tools18`). cloud-init stopped there, so the Azure CLI and the ready marker were never created. Fixed: install Go sqlcmd v1.10.0 from a pinned GitHub release. The live VM was fixed by hand with the same steps; a fresh deployment has not yet been tested with the corrected script.
2. **Entra SQL authentication missing.** Microsoft.Data.SqlClient 7.x moved Entra authentication into `Microsoft.Data.SqlClient.Extensions.Azure`. Without it the managed-identity login in both the seeder and the processor host failed at login. Fixed by referencing the package from the shared library, with a regression test.
3. **SQL zone redundancy was not a deploy parameter.** Now `-SqlNotZoneRedundant` on `deploy.ps1`.

## Verified live

| Check | Result |
|---|---|
| Pre-deployment: .NET 10 on App Service Linux in Australia East | Offered (`DOTNETCORE:10.0`) |
| Pre-deployment: tooling VM size, zones, quota | `Standard_D4s_v5` in zones 1, 2, 3; 20 vCPU quota free |
| What-if | 31 to create; 7 role assignments cannot be analysed by what-if (expected) |
| Public network access | Disabled on Service Bus, Event Hubs and SQL (confirmed on resources) |
| Private resolution from the VM | SQL 10.20.2.6, Service Bus 10.20.2.5, Event Hubs 10.20.2.4; ports open |
| Isolation from the laptop | Refused at the application layer with a valid Entra token: Service Bus and Event Hubs "Ip has been prevented to connect", SQL "Deny Public Network Access is set to Yes". Note a raw TCP connect to the public address still succeeds, so TCP alone proves nothing |
| SQL users | Processor: `db_datareader`. Tooling: `db_datareader`, `db_datawriter`, `db_ddladmin` |
| `verify-access.sh` | All checks passed (Service Bus, Event Hubs, SQL, including refusals). Tooling AMQP receive on Event Hubs not tested |
| Processor host | Deployed to 3 instances; `/health` returns 200; runtime .NET 10; Always On |
| Seeder on the VM, tooling identity, over the private endpoint | 1,000,000 items + 1,000,000 SKUs in **21.4 s** (local container: 5.1 s); second run adds 0 rows in 0.8 s |
| Load generator publishing with the tooling identity over the private endpoint | 30 messages, 0 failed, 99.7% of the 5 msg/s target |
| Smoke test, matched SKU | 16 messages on the output queue, JSON has 41 fields incl. `ExternalItemId` (`itemId` 368496 gave `EXT-368496`), about 4.2 KB |
| Smoke test, unmatched SKU | 14 events on the event hub (Azure Monitor `IncomingMessages`) |
| Smoke test, bad messages | Dead-lettered with exactly the expected reasons: `InvalidXml`, `MissingItemId`, `MissingSkuId`, `ItemNotFound` |
| Subscription backlog after the run | 0 active messages |

The output queue and dead-letter queue also hold the `verify-access.sh` probe messages (1 in the queue, 1 dead-lettered as `InvalidXml`, 1 event). Purge or ignore these before a benchmark run.

## Not verified live

- One-command teardown, and a deployment from scratch using the corrected cloud-init.
- SSH refused from other addresses; budget alerts actually existing; Application Insights telemetry arriving (including the `routeAllTraffic` question) and the RunReport queries against real data.
- Tooling-identity AMQP receive on Event Hubs.
- Anything at benchmark volume: sustained TPS, latency, the bottleneck, cost per hour.
