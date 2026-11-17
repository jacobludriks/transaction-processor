# 08: Processor host deployed to App Service, end-to-end smoke test

**What to build:** the running processor. A thin receive loop reads messages from the topic subscription and hands each body to the handler. It calls the real queue and event hub senders, then completes a message only after its output has been sent, dead-letters it with the handler's reason, or abandons it so Service Bus redelivers it. It has no business logic of its own. Concurrency (maximum concurrent calls) is a configuration value. It includes a minimal health endpoint and Application Insights. It is deployed by Bicep to an App Service plan (Linux, Premium v3, zone-redundant, 3 instances, pinned) with VNet integration into the right subnet and the processor identity attached. Spec: `.scratch/transaction-processor-poc/spec.md`.

**Blocked by:** 02, 03, 04, 07

**Status:** ready-for-agent

- [ ] The app is deployed by the same one-command Bicep deployment and removed by the same teardown command
- [ ] It authenticates to Service Bus, Event Hubs and SQL using the processor identity only
- [ ] From the VM, send a message with a known SKU, one with an unknown SKU, a malformed message, and one with an unknown item. Each ends up respectively in the output queue, in the event hub, in the dead-letter queue with the right reason, and in the dead-letter queue with `ItemNotFound`
- [x] A message is completed only after its output send succeeds, and a failed send leaves it to be redelivered
- [x] Maximum concurrent calls and the instance count can be changed through configuration without a code change
- [ ] The health endpoint responds, and the app has Always On or the equivalent so it isn't idled
- [x] The receive loop contains no routing or lookup logic. All of it lives in the handler
- [ ] All 3 instances are running and zone-redundant on the deployed plan

## Progress note (offline implementation)

The processor host is a new project that references the handler library. A background service reads the subscription (peek-lock, auto-complete off) and passes each body to the handler. A small settlement step applies the outcome: send-to-queue and send-to-event-hub complete the message (the handler has already sent the output), dead-letter uses the handler reason, retry abandons it so Service Bus redelivers it, and an unexpected exception also abandons it. That mapping is covered by seven tests through a fake settlement (no Azure), and the existing handler tests already show a failed send returns retry. The real queue and event hub senders and the Service Bus settlement adapter are thin and are only exercised by a benchmark run. All names, the identity client ID, maximum concurrent calls and prefetch come from app settings; the SQL connection uses managed identity authentication with the client ID and there is no connection string or secret. A health endpoint (200 while the loop runs) responded locally. Application Insights is enabled from an app setting.

Bicep adds Log Analytics, Application Insights, the Linux Premium v3 plan (P1v3, zone-redundant, 3 instances, no autoscale) and the web app (Always On, health check path, VNet integration, processor identity attached, .NET 10 with a parameterised .NET 8 fallback) at the existing seam, with settings in the same parameter file. Instance count, concurrency, prefetch and runtime are deploy parameters. A separate script publishes and deploys the package, and the README has the smoke test procedure for the VM with results rows marked not yet checked. Offline checks passed: dotnet build, dotnet test, a net8.0 publish, az bicep build, lint and build-params. Nothing was deployed.

Still needs Azure: one-command deploy and teardown of the app, identity-only authentication against the real services, the four smoke-test messages from the VM, 3 running zone-redundant instances, and the health endpoint and Always On on the deployed app. The criteria for those stay unchecked.
