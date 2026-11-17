# 03: Handler: permanent failures are dead-lettered with a reason

**What to build:** the handler recognises messages that can never succeed and returns a "dead-letter" outcome with a machine-readable reason, so they are not retried pointlessly. The cases are: XML that can't be parsed, a missing `itemId`, a missing `skuId`, and an `itemId` with no row in the item table (reason `ItemNotFound`). `ItemNotFound` is a failure, not a routing outcome, so it never goes to the event hub. Spec: `.scratch/transaction-processor-poc/spec.md`.

**Blocked by:** 01

**Status:** ready-for-agent

- [x] Unparseable XML gives a dead-letter outcome with its own distinct reason
- [x] A message missing `itemId` gives a dead-letter outcome with its own distinct reason
- [x] A message missing `skuId` gives a dead-letter outcome with its own distinct reason
- [x] A message whose `itemId` has no row gives a dead-letter outcome with the reason `ItemNotFound`, and nothing is sent to the queue or the event hub
- [x] Each reason is a stable, machine-readable value that can be grouped and counted
- [x] No dead-letter case triggers a database write or an outbound send
