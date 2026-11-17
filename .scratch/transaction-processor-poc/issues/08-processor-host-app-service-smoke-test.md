# 08: Processor host deployed to App Service, end-to-end smoke test

**What to build:** the running processor. A thin receive loop reads messages from the topic subscription and hands each body to the handler. It calls the real queue and event hub senders, then completes a message only after its output has been sent, dead-letters it with the handler's reason, or abandons it so Service Bus redelivers it. It has no business logic of its own. Concurrency (maximum concurrent calls) is a configuration value. It includes a minimal health endpoint and Application Insights. It is deployed by Bicep to an App Service plan (Linux, Premium v3, zone-redundant, 3 instances, pinned) with VNet integration into the right subnet and the processor identity attached. Spec: `.scratch/transaction-processor-poc/spec.md`.

**Blocked by:** 02, 03, 04, 07

**Status:** ready-for-agent

- [ ] The app is deployed by the same one-command Bicep deployment and removed by the same teardown command
- [ ] It authenticates to Service Bus, Event Hubs and SQL using the processor identity only
- [ ] From the VM, send a message with a known SKU, one with an unknown SKU, a malformed message, and one with an unknown item. Each ends up respectively in the output queue, in the event hub, in the dead-letter queue with the right reason, and in the dead-letter queue with `ItemNotFound`
- [ ] A message is completed only after its output send succeeds, and a failed send leaves it to be redelivered
- [ ] Maximum concurrent calls and the instance count can be changed through configuration without a code change
- [ ] The health endpoint responds, and the app has Always On or the equivalent so it isn't idled
- [ ] The receive loop contains no routing or lookup logic. All of it lives in the handler
- [ ] All 3 instances are running and zone-redundant on the deployed plan
