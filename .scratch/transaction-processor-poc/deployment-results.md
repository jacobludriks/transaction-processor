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

## Baseline benchmark at 300 TPS (ticket 12)

Configuration: 3 App Service instances (P1v3, zone-redundant), `MaxConcurrentCalls` 16, prefetch 32, Azure SQL General Purpose 4 vCores (**not zone-redundant**), Service Bus Premium 1 MU, private endpoints, 252,000 messages (4 to 6 KB) over 14 minutes, 90% matched SKUs. The load generator ran on the tooling VM.

**Verdict: GO at 300 TPS.** Reports are in `runs/`.

| Criterion | Run 2 (clean) | Run 1 (queue filled) |
|---|---|---|
| Sustained throughput | 300.0 TPS over 11 measured minutes (slowest 299.6) | 300.1 TPS (slowest 299.2) |
| p95 latency (limit 2 s) | about 0.158 s (p50 0.078, p99 0.195) | about 0.135 s |
| Lost messages | 0 (252,000 in, 252,000 out, 0 duplicates) | 0 counted, 251 dead-lettered |
| Unexplained dead-letters | 0 | **251, FAIL** |
| Backlog | peak 95, trend -0.03/s | peak 128 |

Run 1 failed on one criterion, and the cause was the test setup, not the processor. The output queue has no consumer, so it filled to its default 1 GB cap at 10:48:38 (about 224,000 messages), in the last seconds of the run. Sends to it failed and the handler correctly asked for retries, but each message reached Service Bus's 10-delivery limit within seconds, so 251 were dead-lettered. Fix: queue cap raised to 40 GB (a live change; **`maxSizeInMegabytes` is not yet set in the Bicep**), and the dead-letters were drained before run 2. A 40 GB cap holds roughly 9.5 million messages of this size, hours of traffic even at 600 TPS, so it is not a constraint for the peak runs, but nothing drains the queue, so purge it between sessions.

### Defects in the measurement tooling found by the live run (fixed)

1. For a topic subscription Azure Monitor reports the `EntityName` dimension as the **topic name**. The export script filtered on the subscription name and silently received zero-filled series, so the backlog and dead-letter criteria passed vacuously on run 1 (a false PASS while 251 messages were in the dead-letter queue). The script now filters on the topic and refuses to export if the entity does not exist. The first (wrong) report was deleted and regenerated.
2. `routeAllTraffic = true` with no NAT gateway sent no telemetry to Application Insights. It is now false (private endpoints still resolve).
3. A jq precedence slip in the export script's summary line.

### Caveats on the result

- The database is not zone-redundant. This likely flatters database latency slightly.
- Duplicates were 0 in run 2 but 306 in run 1 (during the full-queue failure), consistent with at-least-once delivery under failure.
- The latency figure is the upper edge of the histogram bucket for the verdict; p95 is interpolated within the bucket.
- The throughput bar is 294 TPS (300 less a 2% tolerance); run 2 was within 0.1% of 300 so the tolerance did not matter.
- Latency is measured from the Service Bus enqueue time to the output send completing, using the host's clock.
- Peak TPS and the limiting component have **not** been measured (ticket 13).
