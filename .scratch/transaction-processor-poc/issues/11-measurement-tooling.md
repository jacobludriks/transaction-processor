# 11: Measurement tooling

**What to build:** a repeatable way to turn a benchmark run into numbers that can be judged against the go/no-go criteria. The processor and the generator emit the telemetry needed to compute sustained TPS (the drain rate over 10 minutes with no growth in backlog), p50/p95/p99 latency from enqueue to output sent, backlog size over time, and error and dead-letter counts. Each run is recorded with its configuration: instance count, maximum concurrent calls, database tier and publish rate. Spec: `.scratch/transaction-processor-poc/spec.md`.

**Blocked by:** 08, 10

**Status:** ready-for-agent

- [ ] After a short test run, one command or saved query produces sustained TPS, p50/p95/p99 latency, backlog trend, and error and dead-letter counts for that run
- [ ] Latency is measured from the Service Bus enqueue time to the moment the output send completes, and covers both the queue and event hub paths
- [ ] Dead-letters are counted by reason, so unexplained ones are visible
- [ ] Each run's result is saved with the configuration used, so runs can be compared side by side
- [ ] The tooling works from the VM or from Application Insights without opening any public access to the data services
- [ ] The measurements are validated on a short run by comparing them against the generator's own count of messages sent and the number of outputs received
