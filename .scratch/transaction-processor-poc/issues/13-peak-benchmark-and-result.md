# 13: Peak benchmark, bottleneck and written result

**What to build:** the answer to "how far can it go, and what stops it". Step the publish rate up (for example 300, 450, 600 and beyond) until the backlog starts to grow or errors appear. Between steps, vary the main variables one at a time (database tier, maximum concurrent calls, instance count) to see which component limits throughput: the database, Service Bus, or the processor. Finish with a short written result for the team. Spec: `.scratch/transaction-processor-poc/spec.md`.

**Blocked by:** 12

**Status:** ready-for-agent

- [ ] Rates above 300 TPS are stepped up until the pipeline can no longer keep up, and the highest rate held without growing backlog is recorded as the peak TPS
- [ ] At the point of failure the limiting component is identified with evidence (for example database CPU, DTU or log I/O at its ceiling, Service Bus throttling, or processor CPU)
- [ ] The effect of the database tier on peak TPS is measured by running at more than one tier
- [ ] The measurements are consistent with the generator's own count, and the generator was not the limit
- [ ] The written result states: go or no-go, peak TPS, the bottleneck, the configuration it was measured on, the approximate cost per hour and per million messages, and the caveats (Azure SQL Database rather than Managed Instance, a Premium v3 plan rather than an ASE, private-endpoint latency not reproduced)
- [ ] If the database is the bottleneck, the result says so and recommends whether to add the in-memory cache. The cache itself is not built unless you explicitly ask
- [ ] Total spend and runtime are recorded against the AU$30 target and AU$50 ceiling, and the environment is torn down
