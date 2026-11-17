# Measurement tooling (ticket 11)

Turns a benchmark run into numbers that can be judged against the go/no-go criteria, and saves each run with its configuration so runs can be compared.

Status: the computation is unit-tested against **synthetic** data (known TPS, known latency distribution, growing backlog, lost messages, dead-letters). That validates the arithmetic only. The telemetry names, the KQL and the `az` commands below have **not been run against Azure**; they need a short live run to validate (see "Validating on a short run").

## What is measured, and where it comes from

| Measure | Source |
|---|---|
| Outputs sent (queue, event hub), dead-lettered by reason, abandoned (errors) | Host counter `txproc.messages` in Application Insights |
| Latency p50/p95/p99, Service Bus enqueue time to output send completed, both paths | Host counter `txproc.message.latency.bucket` (and histogram `txproc.message.latency`) |
| Backlog over time | Azure Monitor metric `ActiveMessages` of the Service Bus subscription |
| Dead-letters including ones Service Bus makes itself (delivery count exhausted) | Azure Monitor metric `DeadletteredMessages` of the subscription |
| Messages sent, achieved rate, run start and end | The load generator's `--summary-file` |

### Why pre-aggregated metrics, not an event per message

At 300+ messages per second, a custom event per message would be sampled by Application Insights (so counts and percentiles would be wrong) and would cost ingestion money. The host instead keeps counters and a latency histogram in memory and exports them once a minute, so nothing is sampled away. Traces and dependencies in Application Insights are still sampled: do not count messages from them.

### Host instruments (meter `TransactionProcessor`)

| Instrument | Tags | Meaning |
|---|---|---|
| `txproc.messages` (counter) | `outcome` = `sent_to_queue`, `sent_to_event_hub`, `dead_lettered`, `abandoned`; `reason` for the last two | Dead-letter reasons are the handler's (`InvalidXml`, `MissingItemId`, `MissingSkuId`, `ItemNotFound`). Abandon reasons are `transient_failure` (handler asked for a retry) and `unhandled_exception`. |
| `txproc.message.latency` (histogram, seconds) | `destination` = `queue`, `event_hub` | Enqueue time (Service Bus `EnqueuedTime`) to the moment the output send completed. Measured when the handler returns, before the message is settled. Explicit bucket bounds in seconds: 0.025, 0.05, 0.1, 0.2, 0.3, 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2, 2.5, 3, 5, 10, 30 (dense around the 2 s target). |
| `txproc.message.latency.bucket` (counter) | `le` (bucket upper bound in seconds, or `+Inf`), `destination` | The same latency counted per bucket. **This is what the percentiles are computed from.** The Azure Monitor exporter is understood to keep only count, sum, min and max of a histogram (not its buckets), so the buckets are also exported as a plain counter. Confirm on the first live run with `queries/latency-crosscheck.kql`: its message count should equal the sum of the bucket counts. |

Metrics are exported only when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set (as on App Service). Messages that exhaust the maximum delivery count are dead-lettered by Service Bus itself and never produce a host outcome: they appear only in the `DeadletteredMessages` metric, which is why the report compares it with what the host counted.

Clock note: latency subtracts Service Bus's enqueue timestamp from the host's clock. Azure keeps these within milliseconds, which is small against a 2 s target, but it is not exact.

## Running a measured benchmark

1. Run the generator long enough. The report measures the whole minutes inside the run and drops one minute at each end (metrics are exported once a minute per instance, so the edge minutes are smeared). Ten measured minutes therefore need a run of at least 12 whole minutes; use 14:

   ```bash
   dotnet run -c Release -- load --namespace <sb>.servicebus.windows.net --client-id <tooling client id> \
     --rate 300 --duration 840 --summary-file run-summary.json
   ```

   Do not send the functional set during a load run (its messages are not in the generator's sent count). If you must, list their dead-letter reasons in `expectedDeadLetterReasons` and expect the reconciliation to be off by their number.
2. Note the configuration you deployed (instances, `MaxConcurrentCalls`, database tier) in a run configuration file (`run-config.example.json`).
3. Wait until the subscription has drained (backlog 0) and a few more minutes so the last metric exports arrive, then export the series (next section).
4. Analyse, and compare.

## Exporting the time series

`export-metrics.sh` does it all and writes `metrics.json`. It is documented here and has **not been executed**; it needs `az` (signed in), the Application Insights extension (`az extension add -n application-insights`) and `jq`.

```bash
./export-metrics.sh --app <app insights name> --resource-group <rg> --servicebus <namespace name> \
  --start 2026-11-17T09:55:00Z --end 2026-11-17T10:25:00Z --out metrics.json
```

`--start` a few minutes before the generator started (so the dead-letter baseline is before the run), `--end` a few minutes after the drain. The names are deployment outputs (`appInsightsName`, the Service Bus namespace name).

What it runs, if you prefer to do it by hand:

- the three queries in `queries/` (`outcomes.kql`, `latency-buckets.kql`; `latency-crosscheck.kql` is only a cross-check) through `az monitor app-insights query --app <name> --resource-group <rg> --start-time <iso> --end-time <iso> --analytics-query "<kql>"`, or pasted into Application Insights Logs in the portal (replace `__START__` and `__END__`);
- the two Azure Monitor metrics of the subscription:

  ```bash
  SB_ID=$(az servicebus namespace show -n <namespace> -g <rg> --query id -o tsv)
  az monitor metrics list --resource $SB_ID --metric ActiveMessages --aggregation Average --interval PT1M \
    --filter "EntityName eq 'processor'" --start-time <iso> --end-time <iso>
  az monitor metrics list --resource $SB_ID --metric DeadletteredMessages --aggregation Maximum --interval PT1M \
    --filter "EntityName eq 'processor'" --start-time <iso> --end-time <iso>
  ```

  The `EntityName` dimension is expected to be the subscription name. If a metric comes back empty, list the values with `--filter "EntityName eq '*'"` and use what Azure reports.

Check the metrics are arriving before a long run with `queries/metrics-arriving.kql`: one row per instrument, `instances` should equal the instance count.

### Input formats

All three inputs are JSON with camelCase names; unknown fields are ignored.

`run-config.json`: `name` (optional label), `instanceCount`, `maxConcurrentCalls`, `prefetchCount` (optional), `databaseTier`, `targetPublishRate`, optional `startUtc` and `endUtc` (default to the generator summary's), `expectedDeadLetterReasons` (default none: any dead-letter in a load run is unexplained), `notes`.

`metrics.json`, the exported series:

```json
{
  "binSeconds": 60,
  "outcomes":        [ { "timestamp": "2026-11-17T10:02:00Z", "outcome": "sent_to_queue", "reason": null, "count": 16200 } ],
  "latencyBuckets":  [ { "timestamp": "2026-11-17T10:02:00Z", "le": "0.5", "count": 5400 } ],
  "backlog":         [ { "timestamp": "2026-11-17T10:02:00Z", "activeMessages": 118 } ],
  "deadLetterQueue": [ { "timestamp": "2026-11-17T10:02:00Z", "messages": 0 } ]
}
```

`outcomes` and `latencyBuckets` are counts per bin (not cumulative, summed over instances); `le` is the bucket's upper bound in seconds or `+Inf`. `backlog` and `deadLetterQueue` are gauges.

`run-summary.json`: written by the load generator (`startedAtUtc`, `endedAtUtc`, `targetRate`, `sent`, `failed`, `achievedRate`, `heldTargetRate`, and the settings used).

## Producing the report

From the repository root:

```bash
dotnet run --project src/RunReport -- analyze --config run-config.json --metrics metrics.json --generator run-summary.json --out runs
dotnet run --project src/RunReport -- compare runs --out runs/comparison.md
```

`analyze` prints a markdown summary and saves `runs/<id>.json` (the full record, including the configuration) and `runs/<id>.md`. The id carries the start time and headline configuration, for example `20261117-1000-baseline-300tps-3i-c16-GP_Gen5_4`. Exit code 0 means the run meets the go criteria, 1 that it does not, 2 bad input. `compare` prints a table of all saved runs, oldest first (configuration and headline results side by side); it takes a directory or individual `.json` records.

### How each number is computed

- **Measurement window:** the whole minutes inside the generator's run, minus one at each end. Sustained TPS, latency and backlog trend use only this window.
- **Sustained TPS:** (sent to queue + sent to event hub) in the window divided by the window length; also the lowest and highest minute.
- **Backlog trend:** least-squares slope of `ActiveMessages` over the window. It counts as growing if the trend adds more than 2 seconds' worth of the target rate over the window (600 messages at 300 TPS). A spike that recovers is not growth.
- **Latency:** p50/p95/p99 from the per-bucket counts in the window, interpolated linearly inside the bucket. The verdict uses the **upper bound** of the bucket holding p95, so a bucket edge exactly at 2 s passes and anything above fails.
- **Errors and dead-letters:** counted over the whole run by reason. Abandons are errors (redelivered, not lost). Dead-letters are unexplained when their reason is not in `expectedDeadLetterReasons`, or when `DeadletteredMessages` grew by more than the host counted (typically maximum delivery count exceeded).
- **Reconciliation:** generator sent against queue + event hub + dead-lettered (host-counted plus the Service Bus excess), with the messages still in the backlog at the end of the data treated as in flight. More outputs than sent are reported as duplicates (at-least-once delivery) and do not fail the run. A limitation: duplicates and lost messages in the same run can cancel in the totals.

### Verdict (spec go criteria)

| Criterion | Passes when |
|---|---|
| Sustained throughput | window of at least 10 minutes and sustained TPS of at least 294 (300 less a 2% tolerance) |
| Backlog not growing | backlog data present and the trend is not growth |
| p95 latency under 2 s | p95 bucket upper bound is 2 s or less |
| Zero lost messages | none lost, and the backlog has drained (no backlog data or a non-empty backlog fails: export again after the drain) |
| No unexplained dead-letters | none unexplained, and a dead-letter series is present |
| Generator held the target rate | generator reached at least 98% of its target with no failed sends |

A run with a target below 300 cannot show 300 TPS and so cannot meet the criteria. "No-go" in the spec also needs a database scale-up attempt, so the verdict is per run ("meets" or "does not meet"); the go or no-go call is made across runs with `compare`.

## Without opening any public access to the data services

Nothing here connects to Service Bus, Event Hubs or SQL. Application Insights queries go to the Application Insights query API, and the metrics go to the Azure Monitor metrics API through Azure Resource Manager. Both are monitoring and management plane, separate from the data plane that the private endpoints protect, so the data services keep public network access disabled. The only access needed is an Azure sign-in that can read monitoring data (Monitoring Reader on the resource group is enough, plus reader on the Application Insights component).

- **From the laptop:** `az login` as the operator and run the script there. This is the simplest. Generator `run-summary.json` is copied from the VM (`scp poc@<vm-ip>:run-summary.json .`, SSH is already limited to the operator's IP).
- **From the VM:** `az login --use-device-code` as the operator, clone the repo or copy `src/RunReport`, and run the script and `dotnet run --project src/RunReport` there. The tooling identity has no monitoring roles by default, so a managed-identity login (`az login --identity`) will not work for the queries unless you grant it Monitoring Reader.
- **Portal alternative:** paste the KQL into Application Insights Logs, export to CSV/JSON, and build `metrics.json` by hand.

If the Log Analytics workspace or Application Insights component is later locked down with a private link scope and public query disabled, the queries must then be run from inside that network; that is not the case in the current deployment.

## Validating on a short run

The last acceptance criterion of ticket 11 needs a live run, which has not been done. After the processor host is deployed and the generator can publish:

1. Run `queries/metrics-arriving.kql` and confirm the `txproc.*` metrics appear from every instance.
2. Run a short load (for example `--rate 50 --duration 840 --summary-file`) and the export and `analyze`.
3. Check the reconciliation: generator `sent` should equal queue + event hub outputs (no dead-letters expected at this stage), `lost` 0, and `latency-crosscheck.kql` counts should equal the bucket totals. Check one count independently, for example the output queue's message count in the portal.
4. Look at the first real export for assumptions that were made from documentation rather than observation: that counters arrive in `customMetrics` with `value`/`valueSum` as the sum for the interval, that `customDimensions` holds the tags, the `EntityName` dimension value for subscriptions, and that bin timestamps fall where the trimming assumes.
