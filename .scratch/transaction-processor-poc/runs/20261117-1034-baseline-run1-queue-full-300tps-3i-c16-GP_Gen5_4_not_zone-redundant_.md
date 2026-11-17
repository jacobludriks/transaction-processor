# Run 20261117-1034-baseline-run1-queue-full-300tps-3i-c16-GP_Gen5_4_not_zone-redundant_

**Verdict: this run DOES NOT meet the go criteria.** See the failed criteria below.

| Result | Criterion | Detail |
|---|---|---|
| PASS | Sustained throughput | Sustained 300.1 TPS over 660 s (lowest minute 299.2 TPS). The bar is 294 TPS: 300 TPS less a 2% tolerance. |
| PASS | Backlog not growing | The backlog trend is -0.08/s (-50 over the window, limit 600); peak 128. |
| PASS | p95 latency under 2 s | p95 is about 0.135 s (at most 0.2 s); the limit is 2 s. The verdict uses the bucket's upper bound. |
| PASS | Zero lost messages | Generator sent 252,000; outputs 252,055 (queue 227,229, event hub 24,826), dead-lettered 251; none lost; 306 more outputs than messages sent (duplicates from at-least-once delivery). |
| FAIL | No unexplained dead-letters | 251 unexplained: 0 counted by the host for reasons this run did not expect, 251 added to the dead-letter queue that the host did not count (most likely maximum delivery count exceeded). |
| PASS | Generator held the target rate | Generator achieved 299.9 msg/s against a target of 300. |

## Configuration

| Setting | Value |
|---|---|
| Name | baseline-run1-queue-full |
| Instances | 3 |
| Max concurrent calls | 16 |
| Prefetch count | 32 |
| Database tier | GP_Gen5_4 (not zone-redundant) |
| Target publish rate | 300 msg/s |
| Run start (UTC) | 2026-11-17 10:34:49Z |
| Run end (UTC) | 2026-11-17 10:48:50Z |
| Notes | Run 1. Output queue hit its 1 GB size cap at 10:48:38 (no consumer); last ~12 s of sends failed, 251 messages dead-lettered by Service Bus after 10 deliveries. Cap raised to 40 GB afterwards. |

## Results

| Measure | Value |
|---|---|
| Measurement window | 660 s (11 of 11 whole minutes with data, from 10:36 UTC) |
| Sustained throughput | 300.1 TPS |
| Lowest / highest minute | 299.2 / 300.6 TPS |
| Latency p50 | 0.076 s |
| Latency p95 | 0.135 s |
| Latency p99 | 0.190 s |
| Latency samples | 198,035 |
| Backlog trend | not growing: -0.08/s, -50 over the window (start 128, end 65, peak 128) |

## Outcomes (whole run)

| Outcome | Count |
|---|---|
| Sent to queue | 227,229 |
| Sent to event hub | 24,826 |
| Dead-lettered (counted by the host) | 0 |
| Abandoned for redelivery (errors) | 2,861 |

### Dead-letters by reason

| Reason | Count |
|---|---|
| (added by Service Bus, not counted by the host: usually MaxDeliveryCountExceeded) | 251 |

### Abandoned by reason

| Reason | Count |
|---|---|
| transient_failure | 2,861 |

## Reconciliation: generator sent against outputs

| Item | Messages |
|---|---|
| Generator sent | 252,000 |
| Sent to queue | 227,229 |
| Sent to event hub | 24,826 |
| Dead-lettered | 251 |
| Still in the backlog at the end of the data | 0 |
| Lost | 0 |
| Duplicates (more outputs than sent) | 306 |

## Throughput per minute (TPS)

299.8, 300.0, 300.5, 300.6, 299.8, 300.1, 300.1, 299.2, 300.4, 300.1, 300.1

Recorded 2026-11-17 10:56:10Z.
