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

## Peak throughput and bottleneck (ticket 13)

Method: a step-up ramp from the tooling VM, 6 minutes per step, message mix as the baseline (4 to 6 KB, 10% unmatched SKUs). The ramp changed one variable at a time between rounds. Every figure below is measured inside the step's own window (the first and last minute of each step are dropped), from the processor's metrics in Application Insights and Azure Monitor. Raw inputs are in `runs/ramp-raw/`; the driver and helper scripts are in `infra/scripts/vm/`.

| Configuration | Target msg/s | Sustained | p50 / p95 latency | Backlog | Dead-letters |
|---|---|---|---|---|---|
| 3 instances, concurrency 16, 1 MU | 450 | 450 | 0.08 / 0.19 s | stable | 0 |
| 3 instances, concurrency 16, 1 MU | 600 | 600 | 0.10 / 0.28 s | stable | 0 |
| 3 instances, concurrency 16, 1 MU | 750 | 653 | 17 / 29 s | **growing** (+94/s) | 0 |
| 3 instances, concurrency 48, 1 MU | 750 | 750 | 0.14 / 0.48 s | stable | 0 |
| 3 instances, concurrency 48, 1 MU | 1,000 | 695 | over 30 s | **growing** (+302/s) | 0 |
| 3 instances, concurrency 48, 2 MU | 1,000 | 738 | over 30 s | **growing** (+260/s) | 0 |
| 6 instances, concurrency 48, 2 MU | 1,000 | 998 | 0.09 / 0.19 s | stable | 0 |
| 6 instances, concurrency 48, 2 MU | 1,300 | 1,309 | 1.67 / **2.76 s** | borderline (peak 3,130) | 0 |

"MU" is Service Bus Premium messaging units; concurrency is `MaxConcurrentCalls` per instance. The database was Azure SQL General Purpose 4 vCores, not zone-redundant, throughout. The generator held every target (99.9% or better), so the offered load was real.

### What the peak is

Highest rate meeting the spec's p95 latency under 2 s with a stable backlog and nothing lost:

- **3 instances (the baseline deployment): about 750 msg/s** with concurrency 48 (600 with the original concurrency 16). Saturates at roughly 700 to 740.
- **6 instances: at least 1,000 msg/s with ample margin (p95 0.19 s); 1,300 msg/s is at the edge** (p95 2.76 s, backlog creeping), so the 6-instance peak is between 1,000 and roughly 1,300. I did not step further, so the exact ceiling was not found.

No step dropped or dead-lettered a message. Saturation shows as a growing backlog and rising latency, not as errors.

### What limits it

Established by experiment, one variable at a time:

1. **Not the database.** SQL lookups cost about 2.8 ms (p95 about 5 ms) at every step, and SQL CPU was 7 to 22%, workers at most 2%, no log or I/O pressure. The database was never close to a limit at 1,300 msg/s, so the in-memory cache (the stretch goal) would not help and was not built.
2. **Not CPU or memory.** App Service CPU stayed at 15 to 21% (memory about 33%) at every step, including the saturated ones.
3. **Not Service Bus capacity.** Doubling the namespace from 1 to 2 messaging units moved the saturated 1,000 msg/s step from 695 to 738 sustained (within noise), and there were no throttled requests or server errors at any step. Namespace CPU peaked at 67% on 1 MU and 44% on 2 MU.
4. **Concurrency helps a little.** Raising `MaxConcurrentCalls` from 16 to 48 per instance moved the 3-instance ceiling from about 650 to about 750.
5. **It scales with the number of instances.** Doubling instances from 3 to 6 moved the ceiling from about 740 to well over 1,000, about 240 msg/s per instance in both cases.

So the ceiling is **per processor instance**, about 240 messages per second each, and it is **not set by CPU, the database or Service Bus capacity**. What inside an instance sets it is **not identified**: I measured Service Bus operations averaging 55 to 65 ms (p95 about 210 to 230 ms) against 2.8 ms for SQL, which points at the Service Bus client path in the processor (receive, send and settle round trips over the private endpoint), but I did not test that directly. Candidates to try next: prefetch count (left at 32 throughout), batching sends, and the receive loop's connection use.

### Practical reading

To go higher than the baseline, add instances: it is the lever that worked, and every other resource had large headroom. At 6 instances the estimated cost rises by roughly three P1v3 instances.

### Changes made to the live environment during testing (not in the Bicep defaults)

`MaxConcurrentCalls` 16 to 48, Service Bus messaging units 1 to 2, App Service instances 3 to 6. The repo defaults are unchanged (16, 1, 3). The 2 MU and 6 instance settings were temporary experiments; 48 concurrency is the better setting and may be worth making the default.

### Caveats

- The database was not zone-redundant, which probably flatters database latency slightly (it was not the limiting factor anyway).
- Steps were 6 minutes, not 10, so the report tool's "10 minutes sustained" criterion could not be applied to them. Its throughput, latency and backlog figures are used here; its per-step "zero lost" reconciliation was not valid for back-to-back steps in one export (it counted all outputs in the export) and those reports were discarded.
- Sustained TPS for a saturated step is the measured drain rate, not a target.
- One run per configuration, so the differences of a few percent (for example 695 vs 738) are not significant.
- Latency is from Service Bus enqueue time to output sent, using the host's clock.
- The private-endpoint latency to an ASE was not reproduced; production on an ASE may differ.

## Spend, runtime and teardown

- **Runtime:** the environment existed from about 09:49 UTC (first deployment) to about 12:20 UTC (teardown), roughly 2.5 hours. About 1 hour 45 minutes of that was at the baseline size (3 instances, 1 messaging unit); the last 30 to 40 minutes were scaled up for the experiments (2 messaging units, then 6 instances).
- **Estimated spend: about AU$10 to 14.** This is my estimate from list-price rates (AU$4 to 6 per hour at the baseline size, about AU$7 to 8 per hour scaled up), **not checked against billing**. Azure Cost Management usually shows actual figures within a day. It is well inside the AU$30 target and the AU$50 ceiling.
- **Indicative cost per million messages** at the 3-instance peak (about 750 msg/s, 2.7 million messages per hour, AU$4 to 6 per hour): roughly AU$1.5 to 2.2. This inherits the same uncertainty and ignores that the ceiling was not reached on every resource, so treat it as an order of magnitude.
- **Teardown:** `teardown.ps1 -Force` deleted the resource group and returned "Nothing left behind". Verified independently afterwards: the group does not exist, the deployment record is gone, no resource tagged for the POC remains in the subscription, and the budget was removed (it showed in a listing for a short time after the group was deleted, then disappeared; the listing was stale).
- **Not deployed again from scratch:** the corrected VM bootstrap (cloud-init) and the SQL zone-redundancy parameter have only been exercised as live edits plus a template build, not by a clean redeployment.
