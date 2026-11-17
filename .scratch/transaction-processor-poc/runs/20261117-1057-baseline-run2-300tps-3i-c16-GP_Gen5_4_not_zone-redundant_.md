# Run 20261117-1057-baseline-run2-300tps-3i-c16-GP_Gen5_4_not_zone-redundant_

**Verdict: this run MEETS the go criteria** (300 TPS sustained for 10 minutes, p95 under 2 s, zero lost messages, no unexplained dead-letters).

| Result | Criterion | Detail |
|---|---|---|
| PASS | Sustained throughput | Sustained 300 TPS over 660 s (lowest minute 299.6 TPS). The bar is 294 TPS: 300 TPS less a 2% tolerance. |
| PASS | Backlog not growing | The backlog trend is -0.03/s (-17 over the window, limit 600); peak 95. |
| PASS | p95 latency under 2 s | p95 is about 0.158 s (at most 0.2 s); the limit is 2 s. The verdict uses the bucket's upper bound. |
| PASS | Zero lost messages | Generator sent 252,000; outputs 252,000 (queue 227,217, event hub 24,783), dead-lettered 0; none lost. |
| PASS | No unexplained dead-letters | No dead-letters. |
| PASS | Generator held the target rate | Generator achieved 299.9 msg/s against a target of 300. |

## Configuration

| Setting | Value |
|---|---|
| Name | baseline-run2 |
| Instances | 3 |
| Max concurrent calls | 16 |
| Prefetch count | 32 |
| Database tier | GP_Gen5_4 (not zone-redundant) |
| Target publish rate | 300 msg/s |
| Run start (UTC) | 2026-11-17 10:57:01Z |
| Run end (UTC) | 2026-11-17 11:11:01Z |
| Notes | Run 2 (clean rerun). Output queue cap raised to 40 GB and dead-letters drained beforehand. Azure SQL General Purpose 4 vCores NOT zone-redundant; App Service P1v3 x3 zone-redundant; Service Bus Premium 1 MU; public network access disabled, private endpoints. |

## Results

| Measure | Value |
|---|---|
| Measurement window | 660 s (11 of 11 whole minutes with data, from 10:59 UTC) |
| Sustained throughput | 300.0 TPS |
| Lowest / highest minute | 299.6 / 300.4 TPS |
| Latency p50 | 0.078 s |
| Latency p95 | 0.158 s |
| Latency p99 | 0.195 s |
| Latency samples | 198,008 |
| Backlog trend | not growing: -0.03/s, -17 over the window (start 73, end 62, peak 95) |

## Outcomes (whole run)

| Outcome | Count |
|---|---|
| Sent to queue | 227,217 |
| Sent to event hub | 24,783 |
| Dead-lettered (counted by the host) | 0 |
| Abandoned for redelivery (errors) | 0 |

### Dead-letters by reason

None.

## Reconciliation: generator sent against outputs

| Item | Messages |
|---|---|
| Generator sent | 252,000 |
| Sent to queue | 227,217 |
| Sent to event hub | 24,783 |
| Dead-lettered | 0 |
| Still in the backlog at the end of the data | 0 |
| Lost | 0 |
| Duplicates (more outputs than sent) | 0 |

## Throughput per minute (TPS)

299.8, 300.4, 300.0, 300.2, 299.7, 299.9, 300.1, 300.0, 299.6, 300.1, 300.3

Recorded 2026-11-17 11:14:10Z.
