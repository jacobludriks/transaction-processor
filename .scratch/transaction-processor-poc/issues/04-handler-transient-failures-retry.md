# 04: Handler: transient failures are retried

**What to build:** the handler distinguishes temporary faults from permanent ones. A database error or a failed send to the queue or event hub gives a "retry" outcome, so Service Bus redelivers the message (up to its default maximum delivery count of 10, after which it dead-letters it). It must never be reported as a success, and no custom backoff or retry logic is added. Spec: `.scratch/transaction-processor-poc/spec.md`.

**Blocked by:** 02

**Status:** ready-for-agent

- [ ] A database failure during either lookup gives a "retry" outcome
- [ ] A failed send to the queue gives a "retry" outcome
- [ ] A failed send to the event hub gives a "retry" outcome
- [ ] A transient failure is never classed as a dead-letter or a success
- [ ] The tests simulate the faults through the fake senders and through a database that is unreachable, not through internal hooks
