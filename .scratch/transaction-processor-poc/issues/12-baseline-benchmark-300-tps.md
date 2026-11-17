# 12: Baseline benchmark at 300 TPS, with the go/no-go verdict

**What to build:** the first real result. Seed the database, then run the load generator at a sustained 300 TPS for 10 minutes against the pinned configuration (3 instances, the default concurrency, the 4 vCore database), measure it with ticket 11's tooling, and judge it against the go/no-go criteria. This is a hands-on ticket that costs money: it runs inside the budget plan (target under AU$30, ceiling AU$50, about 6 hours of full-stack runtime in total) and the environment is torn down after the session. Spec: `.scratch/transaction-processor-poc/spec.md`.

**Blocked by:** 09, 10, 11

**Status:** ready-for-agent

- [ ] The environment is deployed, the database is seeded, and a short functional run confirms all four outcomes (queue, event hub, and both dead-letter cases) before load starts
- [ ] A 10-minute run at a sustained 300 TPS is completed and recorded with its configuration
- [ ] The result is stated against each criterion: sustained 300 TPS, p95 latency under 2 seconds, zero lost messages, and no unexplained dead-letters
- [ ] If the run falls short, the database tier is scaled up once or twice and the run repeated, to decide between go and no-go (no-go means 300 TPS can't be reached even after a reasonable DB scale-up)
- [ ] A clear go or no-go is written down
- [ ] Spend so far and runtime used are recorded, and the environment is torn down if no further runs are planned
