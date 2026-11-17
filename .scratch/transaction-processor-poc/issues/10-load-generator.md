# 10: Load generator

**What to build:** a command-line tool, run on the tooling VM, that publishes synthetic XML transaction messages to the input topic at a configurable rate. Each message uses the contract from ticket 01 (about 40 fields, 4–6 KB). Item and SKU keys are random across the full seeded range. About 90% of messages carry an existing SKU and about 10% carry one that doesn't exist, and load runs never include a missing `itemId`. A separate small functional set sends one malformed message and one missing-`itemId` message, to prove the dead-letter paths without distorting load runs. It must be able to sustain well beyond the peak rates being tested, so it is never the bottleneck. Spec: `.scratch/transaction-processor-poc/spec.md`.

**Blocked by:** 01, 07

**Status:** ready-for-agent

- [x] The publish rate is configurable, and the generator holds a set rate within a tight tolerance
- [x] Generated messages are valid against the contract and are 4–6 KB
- [x] Keys are drawn evenly across the full seeded range, using the key scheme from the seeder
- [x] The matched/unmatched SKU mix is configurable and defaults to 90/10
- [x] Load-run mode never produces a missing `itemId`. The malformed and missing-`itemId` messages come only from the separate functional mode
- [ ] It authenticates with the tooling identity and publishes over the private endpoint
- [ ] It reports the rate it actually achieved, and it has been shown to sustain at least double the target rate on the VM, so it is not the limiting factor
- [ ] Each message carries enough to measure latency (an enqueue timestamp or the Service Bus enqueue time is available at the output)

## Progress notes

Built and verified offline: the generator, pacing, dry-run and functional mode, with tests. Pacing was checked on a virtual clock and with a real-clock dry run (300 per second held at 99.9%). In a dry run on 4 cores the generator's own ceiling was about 300,000 messages per second (generation and pacing only, nothing sent).

Still needs Azure or the VM (left unticked): authenticating with the tooling identity and publishing over the private endpoint, showing on the VM that it sustains at least double the target rate when really sending, and seeing the send-time property and the Service Bus enqueue time at the output.
