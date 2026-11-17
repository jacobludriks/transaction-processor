Status: ready-for-agent

# Transaction Processor Proof of Concept

## Problem Statement

The team is considering a transaction processor on Azure. It would read XML transaction messages from a Service Bus topic, enrich each one with data from a SQL database, and route it to one of two destinations depending on whether its SKU is recognised. Before anyone commits to the design, they need evidence that it can sustain **300 transactions per second** (TPS) end to end, how much more it can handle at peak, and what limits it (the database, Service Bus, or the processor itself). They need that evidence cheaply, in a short time, in an environment that resembles production: an App Service Environment, zone-redundant, on private networking, in Australia East.

Without a proof of concept the team would be committing to the architecture blind. Throughput, latency and cost per message are unknown, and so is whether the database lookup, performed twice per message, becomes the bottleneck.

## Solution

Build a small, throwaway-quality but correct end-to-end pipeline, plus the tooling to load-test it and measure it:

- A **processor** that receives XML messages from a Service Bus topic subscription, decodes each into an object, looks up the item and the SKU in an Azure SQL database, and then routes the result as JSON: to a Service Bus **queue** if the SKU matches, or to an **event hub** if it does not. Messages that can never succeed are dead-lettered with a reason. Transient failures are retried by Service Bus.
- A **load generator** that publishes synthetic XML messages to the topic at a configurable rate and a **data seeder** that populates the database with realistic reference data.
- **Infrastructure as code** that stands up the whole zone-redundant, private-endpoint environment in one command and tears it down in one command, so the POC costs only what its short runtime costs.
- A **benchmark procedure** that steps load up, records throughput, latency and errors, and compares the result with the go/no-go criteria.

The outcome is a written result: go or no-go against 300 TPS, the peak TPS observed, and the bottleneck that limited it.

## User Stories

1. As an architect, I want to know whether the pipeline sustains 300 TPS for 10 minutes, so that I can decide whether the design is worth pursuing.
2. As an architect, I want to find the peak TPS the pipeline can reach, so that I know how much headroom the design has beyond the target.
3. As an architect, I want to know which component limits throughput (database, Service Bus, or the processor), so that I know where to invest if the idea goes ahead.
4. As an architect, I want latency reported as p50, p95 and p99 from enqueue to output sent, so that I can judge whether the pipeline is fast enough, not only whether it keeps up.
5. As an architect, I want a clear go or no-go outcome against stated criteria, so that the result isn't a matter of opinion.
6. As an architect, I want to know the approximate cost per hour and per million messages, so that I can estimate production running costs.
7. As an operator, I want the processor to read XML messages from a Service Bus topic subscription, so that it integrates with the existing message source.
8. As an operator, I want each XML message decoded into a typed object, so that the rest of the pipeline works with structured data.
9. As an operator, I want the processor to look up the item's `ExternalItemId` using the message's `itemId`, so that downstream systems receive the external identifier.
10. As an operator, I want the processor to check whether the message's `skuId` exists in the SKU reference data, so that messages can be routed by SKU recognition.
11. As an operator, I want messages with a recognised SKU sent to a Service Bus queue as JSON, so that the downstream consumer of recognised transactions receives them.
12. As an operator, I want messages with an unrecognised SKU sent to an event hub as JSON, so that unrecognised transactions are captured for analysis without blocking the main flow.
13. As an operator, I want the JSON sent to the queue and the event hub to have the same shape (the decoded message plus `ExternalItemId`), so that consumers of either destination parse the same structure.
14. As an operator, I want a message whose XML cannot be parsed to be dead-lettered immediately with a reason, so that it isn't retried pointlessly.
15. As an operator, I want a message missing `itemId` or `skuId` to be dead-lettered immediately with a reason, so that bad data doesn't loop.
16. As an operator, I want a message whose `itemId` has no database row to be dead-lettered with the reason `ItemNotFound`, so that it is distinguishable from a normal SKU mismatch.
17. As an operator, I want transient failures (database or network errors, or a failed send) to abandon the message so Service Bus redelivers it, so that temporary outages don't lose data.
18. As an operator, I want redelivery to stop after Service Bus's default maximum delivery count and the message to be dead-lettered, so that a persistent fault doesn't block the subscription.
19. As an operator, I want each dead-lettered message to carry a machine-readable reason, so that I can group and investigate failures.
20. As an operator, I want a message to be completed only after its output has been sent successfully, so that a crash can cause a duplicate but never a loss.
21. As an operator, I want duplicates to be tolerated as part of at-least-once delivery, so that the design stays simple.
22. As an operator, I want messages processed in parallel with no ordering requirement, so that throughput isn't capped by sessions.
23. As an operator, I want the processor's concurrency to be configurable, so that I can tune it between benchmark runs.
24. As an operator, I want the number of processor instances to be fixed and configurable, so that benchmark results are stable and repeatable.
25. As an operator, I want the processor to run on an App Service plan that mirrors the planned production hosting, so that results carry over to production.
26. As an operator, I want the processor to expose a minimal health endpoint, so that App Service can tell it is healthy.
27. As a security-minded engineer, I want all authentication done with managed identities, so that no connection strings or passwords exist anywhere.
28. As a security-minded engineer, I want the processor identity limited to receiving from its subscription, sending to the output queue and event hub, and reading the lookup tables, so that a compromise limits damage.
29. As a security-minded engineer, I want a separate tooling identity for the load generator and seeder, so that the processor never has write access to the reference data.
30. As a security-minded engineer, I want all three data services to be reachable only through private endpoints, so that the environment behaves like production.
31. As a security-minded engineer, I want public network access disabled on Service Bus, Event Hubs and SQL, so that nothing can reach them from the internet.
32. As a security-minded engineer, I want private DNS zones configured so the services' names resolve to private addresses inside the network, so that private connectivity works.
33. As a tester, I want a load generator that publishes synthetic XML messages of about 40 fields (4–6 KB) to the topic, so that load looks like real traffic.
34. As a tester, I want to set the generator's publish rate, so that I can step the load up to find the peak.
35. As a tester, I want the generator to use random item and SKU keys drawn from across the whole reference data, so that the database sees realistic, evenly spread lookups.
36. As a tester, I want about 90% of generated messages to carry a recognised SKU and about 10% an unrecognised one, so that both routing paths are exercised.
37. As a tester, I want no generated load-run message to carry a missing `itemId`, so that load runs measure throughput rather than error handling.
38. As a tester, I want a separate small functional test set, with one malformed message and one missing-`itemId` message, so that the dead-letter paths are verified without distorting load runs.
39. As a tester, I want the load generator to run inside the private network on a VM sized so it is never the bottleneck, so that I measure the processor and not the generator.
40. As a tester, I want a seeder that fills the database with about 1 million item rows and 1 million SKU rows, so that lookups behave as they would at realistic table sizes.
41. As a tester, I want the seeded reference data indexed by its lookup key, so that lookups are efficient.
42. As a tester, I want to read the event hub output and check the queue and dead-letter contents from the tooling VM, so that I can verify where messages ended up.
43. As a tester, I want throughput defined as the sustained drain rate over 10 minutes with no growth in backlog, so that a burst isn't mistaken for sustained capacity.
44. As a tester, I want latency measured from enqueue time to the time the output is sent, so that it covers the whole pipeline.
45. As a tester, I want to step the publish rate upward (for example 300, 450, 600) until backlog grows or errors appear, so that the peak and its cause can be identified.
46. As a tester, I want to scale the database tier up and down between runs, so that I can see how much the database limits throughput.
47. As a tester, I want metrics and traces in Application Insights and Azure Monitor, so that I can see where time is spent and where limits are hit.
48. As a tester, I want each benchmark run recorded with its configuration (instance count, concurrency, database tier, message rate) and its results, so that runs can be compared.
49. As a budget owner, I want the entire environment created by one infrastructure-as-code deployment, so that it can be rebuilt quickly for each test session.
50. As a budget owner, I want the entire environment deleted by one command, so that nothing keeps costing money when it isn't in use.
51. As a budget owner, I want budget alerts at AU$20, AU$35 and AU$45, so that I find out early if spending is heading toward the AU$50 ceiling.
52. As a budget owner, I want full-stack runtime capped at about 6 hours in total, with test data and scripts prepared in advance, so that spend stays under AU$30.
53. As a budget owner, I want all resources in Australia East and zone-redundant, so that the POC matches the production resilience posture.
54. As a developer, I want the routing logic tested through a single handler-level seam against a real SQL Server, so that behaviour is verified without needing Azure.
55. As a developer, I want the Service Bus receive loop to be a thin adapter with no business logic, so that nearly all behaviour is covered by the handler tests.
56. As a team lead, I want a short written result (go or no-go, peak TPS, bottleneck, cost), so that the decision can be made and recorded.

## Implementation Decisions

**Purpose and decision criteria**
- Go if, on the pinned configuration, the pipeline sustains 300 TPS for 10 minutes with p95 enqueue-to-output latency under 2 seconds, zero lost messages and no unexplained dead-letters. No-go if 300 TPS can't be reached even after a reasonable scale-up of the database tier. Peak TPS and the limiting component are reported either way.
- This is a proof of concept. Production hardening, HA and DR testing are out of scope.

**Message handling (the processor)**
- Input is one XML message from a single subscription on a Service Bus topic, with no filters. Messages are about 40 fields (4–6 KB).
- The XML is deserialized with the standard XML serializer into a plain object. No XSD validation is performed. A message that fails to deserialize, or is missing `itemId` or `skuId`, is a permanent failure.
- Two independent lookups run per message. Item lookup: `itemId` to `ExternalItemId`. SKU check: a match means a row with that `skuId` exists in the SKU table. There is no check that the SKU belongs to the item.
- Routing: SKU match sends JSON to a Service Bus queue. No SKU match sends JSON to a single event hub. `itemId` not found is not a routing outcome. It is a permanent failure with the reason `ItemNotFound`.
- Output is JSON in one shape for both destinations: the decoded message plus `ExternalItemId`.
- Permanent failures (unparseable XML, missing key, `ItemNotFound`) are dead-lettered immediately with a machine-readable reason. Transient failures (database errors, network faults, failed sends) abandon the message so that Service Bus redelivers it, up to the default maximum delivery count of 10, after which Service Bus dead-letters it. There is no custom retry or backoff logic.
- A message is completed only after its output has been sent. Delivery is at-least-once. Duplicates are accepted. There are no ordering requirements and no sessions.
- Every lookup goes directly to the database. There is no caching in this spec (see Out of Scope).

**Handler design**
- The processor's behaviour is concentrated in one message handler that takes a raw message body and returns an outcome (send to queue, send to event hub, dead-letter with reason, or retry), with the output sent through two small outbound interfaces (queue sender and event hub sender). The Service Bus receive loop is a thin adapter that calls the handler and then completes, dead-letters or abandons the message according to the outcome.
- Concurrency (maximum concurrent calls) and the number of instances are configuration values, not constants, because they are the main test variables.

**Hosting and environment**
- The processor is a long-running .NET 10 background service on Azure App Service for Linux, Premium v3, zone-redundant, 3 instances (pinned for benchmark stability). It mirrors production, which would run on an App Service Environment. The ASE itself is not used in the POC because of cost and deployment time. Application Insights and a minimal health endpoint are included. If .NET 10 isn't available on App Service Linux in Australia East when deployed, .NET 8 is the fallback.
- Service Bus is Premium with 1 messaging unit, chosen to guarantee zone redundancy and private endpoints and to give predictable throughput. The namespace holds the input topic with a single subscription and the output queue.
- Event Hubs is Standard with 1 throughput unit and 2 partitions.
- The database is Azure SQL Database, General Purpose, 4 vCores, provisioned (not serverless) and zone-redundant, as the closest practical stand-in for production's Managed Instance. Results are indicative, not a guarantee of Managed Instance behavior. The tier is scaled between runs.
- Everything is in Australia East, zone-redundant where the service supports it. Zone redundancy for each service should be confirmed at deployment time, because the documentation for Service Bus Standard and the ASE minimum was conflicting.

**Data**
- Item table: a `BIGINT` primary key (the lookup key), `ExternalItemId` (required text) and a few filler columns. SKU table: a text primary key and a few filler columns. About 1,000,000 rows in each, with the key as the clustered primary key. The key types are assumed (`itemId` numeric, `skuId` text) and should be adjusted if the real ones differ.
- Lookups: select the `ExternalItemId` for an item by key, and test for the existence of a SKU by key.

**Networking and security**
- All three data services are reached over private endpoints inside a VNet, with public network access disabled. The VNet has separate subnets for App Service VNet integration, for the private endpoints and for the tooling VM. Private DNS zones cover Service Bus and Event Hubs (shared zone) and SQL.
- Authentication is by user-assigned managed identity only, with no connection strings. There are two identities. The processor identity (on the App Service) has: receive on the topic subscription, send on the output queue, send on the event hub, and a read-only SQL user. The tooling identity (on the VM) has: send on the input topic, receive on the event hub and the dead-letter path, and a SQL user with write access for seeding. The app is told which identity to use by its client ID. Creating the SQL users for the identities needs a Microsoft Entra admin on the SQL server and runs from the VM.

**Tooling and measurement**
- A load generator (a C# command-line program) publishes synthetic XML messages to the topic at a configurable rate, with random keys across the full key range, about 90% recognised SKUs and about 10% unrecognised, and no missing `itemId` in load runs. A separate small functional set sends one malformed and one missing-`itemId` message.
- A data seeder populates the two tables. Both tools run on one small Linux VM (about 4 vCPU, so it is never the bottleneck) in the VNet, with SSH limited to the operator's IP. The VM is also the admin box for SQL user setup and for inspecting the dead-letter queue and the event hub.
- Throughput is the sustained drain rate over 10 minutes with no backlog growth. Latency is p50, p95 and p99 from enqueue to output sent. Peak is found by stepping the rate (for example 300, 450, 600) until the backlog grows or errors begin. Metrics come from Azure Monitor and Application Insights. Each run records its configuration and results.

**Infrastructure and cost**
- All infrastructure is defined in Bicep and deployable and deletable with one command each. The environment is torn down when not in use. A budget with alerts at AU$20, AU$35 and AU$45 is part of the deployment. The target spend is under AU$30, with a hard ceiling of AU$50. The estimated running cost is AU$4–6 per hour with everything up, so total full-stack runtime is capped at roughly 6 hours. These cost figures are estimates and should be checked in the Azure pricing calculator.

## Testing Decisions

- **What makes a good test:** a test checks external behaviour only: given a message and the database contents, what outcome occurred and what JSON was produced. It does not check which methods were called or how the handler is organised internally.
- **One automated seam: the message handler.** Tests run the handler against a real SQL Server instance in a container, seeded with small item and SKU tables, with in-memory fakes for the queue sender and the event hub sender. The test cases are:
  - an item found and a SKU matched: sent to the queue, with JSON containing the decoded fields plus `ExternalItemId`;
  - an item found and a SKU not matched: sent to the event hub, with the same JSON shape;
  - an item not found: dead-lettered with `ItemNotFound`;
  - unparseable XML: dead-lettered with a reason;
  - a missing `itemId`: dead-lettered with a reason;
  - a missing `skuId`: dead-lettered with a reason;
  - a database error or a send failure: a retry outcome, not a completion.
- **The Service Bus receive loop stays untested in isolation.** It has no logic of its own. It is exercised by the benchmark run.
- **The benchmark run is a manual measurement, not an automated test.** It runs the load generator against the real Azure environment and is judged against the go/no-go criteria. It is not part of any automated suite, because each run costs money.
- **Not tested automatically:** the Bicep templates (validated with a what-if and a real deployment) and the Azure SDK wiring (covered by the benchmark run).
- **Prior art:** none. The repository is empty, so these tests set the pattern.

## Out of Scope

- The in-memory cache for lookups. It is a stretch goal, to be added only if the user explicitly asks, and only if the baseline TPS comes up short. If added, the intended design is both tables preloaded in memory, falling back to the database on a miss, with SQL Change Tracking polled about every 10 seconds to invalidate changed rows. Updates and deletes could be stale for up to that interval.
- A real App Service Environment deployment. Production hosting is on an ASE. The POC uses a Premium v3 plan as a stand-in.
- Production hardening: high-availability and disaster-recovery testing, multi-region, geo-replication, alerting and runbooks.
- Exactly-once processing, deduplication on the output queue, and message ordering or sessions.
- Event hub consumers beyond a throwaway reader used to verify output arrives.
- XSD validation and schema versioning of the incoming XML.
- A check that a SKU belongs to the item it is sent with.
- Measuring private-endpoint latency as it would be inside a production ASE network. That needs a separate check in production.
- Azure SQL Managed Instance. The POC uses Azure SQL Database as a lower-cost stand-in, and the results are indicative only.

## Further Notes

- The domain terms used here: *item* (looked up by `itemId`, yielding `ExternalItemId`) and *SKU* (matched by `skuId`). No glossary exists yet. `/domain-modeling` can create one if these terms need firmer definitions.
- Several facts were not verified and should be confirmed when building: Azure cost estimates, zone-redundancy support for each service in Australia East, the minimum instance count for a zone-redundant App Service plan (documentation gave two figures), and .NET 10 availability on App Service Linux in Australia East.
- "Event hub topic" in the original description is read as a single event hub, because Event Hubs has no topics.
