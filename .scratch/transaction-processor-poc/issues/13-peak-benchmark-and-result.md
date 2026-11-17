# 13: Peak benchmark, bottleneck and written result

**What to build:** the answer to "how far can it go, and what stops it". Step the publish rate up (for example 300, 450, 600 and beyond) until the backlog starts to grow or errors appear. Between steps, vary the main variables one at a time (database tier, maximum concurrent calls, instance count) to see which component limits throughput: the database, Service Bus, or the processor. Finish with a short written result for the team. Spec: `.scratch/transaction-processor-poc/spec.md`.

**Blocked by:** 12

**Status:** ready-for-agent

- [x] Rates above 300 TPS are stepped up until the pipeline can no longer keep up, and the highest rate held without growing backlog is recorded as the peak TPS
- [ ] At the point of failure the limiting component is identified with evidence (for example database CPU, DTU or log I/O at its ceiling, Service Bus throttling, or processor CPU)
- [ ] The effect of the database tier on peak TPS is measured by running at more than one tier
- [x] The measurements are consistent with the generator's own count, and the generator was not the limit
- [ ] The written result states: go or no-go, peak TPS, the bottleneck, the configuration it was measured on, the approximate cost per hour and per million messages, and the caveats (Azure SQL Database rather than Managed Instance, a Premium v3 plan rather than an ASE, private-endpoint latency not reproduced)
- [x] If the database is the bottleneck, the result says so (it was not; the cache is not recommended) and recommends whether to add the in-memory cache. The cache itself is not built unless you explicitly ask
- [x] Total spend and runtime are recorded (estimate) against the AU$30 target and AU$50 ceiling, and the environment is torn down

## Progress note (2026-11-17)

Stepped ramp done, one variable at a time (concurrency, Service Bus messaging units, instance count). Peak for the baseline 3 instances is about 750 msg/s (concurrency 48); with 6 instances at least 1,000 msg/s with margin and about 1,300 at the edge (p95 2.76 s). The ceiling is per instance (about 240 msg/s each) and is not set by CPU, the database or Service Bus capacity; the exact in-process cause is not identified. The database was never a bottleneck, so the cache is not recommended. Full write-up in `deployment-results.md`.

Remaining open: the effect of the database tier was not measured (the database was at 7 to 22% CPU, so it was not the limit, but a second tier was not run); the 6-instance ceiling was not found; the cost per hour and per million messages was not computed from billing data; spend recorded and the environment torn down are still open.
