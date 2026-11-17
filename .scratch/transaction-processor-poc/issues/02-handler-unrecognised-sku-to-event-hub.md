# 02: Handler: unrecognised SKU goes to the event hub

**What to build:** the second routing path. When the item exists but the `skuId` has no row in the SKU table, the handler returns a "send to event hub" outcome carrying the same JSON shape as the queue path (the decoded message plus `ExternalItemId`). Spec: `.scratch/transaction-processor-poc/spec.md`.

**Blocked by:** 01

**Status:** ready-for-agent

- [ ] A test with an existing item and an unknown SKU gets a "send to event hub" outcome
- [ ] The event hub JSON has exactly the same shape as the queue JSON from ticket 01
- [ ] The queue path from ticket 01 still passes
- [ ] There is a single event hub destination (no topics, no per-case routing)
