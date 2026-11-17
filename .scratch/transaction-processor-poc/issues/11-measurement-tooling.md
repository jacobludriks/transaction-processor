# 11: Measurement tooling

**What to build:** a repeatable way to turn a benchmark run into numbers that can be judged against the go/no-go criteria. The processor and the generator emit the telemetry needed to compute sustained TPS (the drain rate over 10 minutes with no growth in backlog), p50/p95/p99 latency from enqueue to output sent, backlog size over time, and error and dead-letter counts. Each run is recorded with its configuration: instance count, maximum concurrent calls, database tier and publish rate. Spec: `.scratch/transaction-processor-poc/spec.md`.

**Blocked by:** 08, 10

**Status:** ready-for-agent

- [ ] After a short test run, one command or saved query produces sustained TPS, p50/p95/p99 latency, backlog trend, and error and dead-letter counts for that run
- [x] Latency is measured from the Service Bus enqueue time to the moment the output send completes, and covers both the queue and event hub paths
- [x] Dead-letters are counted by reason, so unexplained ones are visible
- [x] Each run's result is saved with the configuration used, so runs can be compared side by side
- [ ] The tooling works from the VM or from Application Insights without opening any public access to the data services
- [ ] The measurements are validated on a short run by comparing them against the generator's own count of messages sent and the number of outputs received

## Progress notes

Built and verified offline. The processor host now exports pre-aggregated metrics (an outcome counter with reasons, a latency histogram with bounds dense around 2 seconds, and the same latency as a per-bucket counter, because the Azure Monitor exporter keeps only count, sum, min and max of a histogram). A new report tool takes the run configuration, the exported series and the generator's run summary file, and produces sustained TPS, backlog trend, p50/p95/p99 latency, errors and dead-letters by reason, a reconciliation of generator sent count against outputs, and a verdict against the go criteria. It saves each run as JSON plus markdown with its configuration and can compare several saved runs. The load generator can write a machine-readable run summary. The tool's README documents the input formats, the KQL queries, the Azure Monitor metric commands (not executed), and how to run them from the laptop or the VM without any public access to the data services.

Tests: the computation is tested test-first against synthetic data with known answers (known TPS, known latency distribution, growing backlog, lost messages, dead-letters, duplicates, gaps). This validates the arithmetic only. The host metrics are tested through a meter listener. No real data has been seen.

Ticked: latency measured from enqueue time to output sent for both paths (at the host's processing seam; the real enqueue timestamp is passed in by the receive loop, which only a live run exercises), dead-letters counted by reason, runs saved with configuration and comparable.

Still needs Azure (left unticked): producing the numbers from a real short run; confirming that the queries and metric commands return what the tool expects (metric names and dimensions, how counters land in Application Insights, the subscription entity dimension); the check from the VM or laptop in practice; and the reconciliation against a real generator count and real output counts.
