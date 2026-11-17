# 01: Handler: recognised SKU goes to the queue

**What to build:** the project skeleton and the first end-to-end path through the message handler. Given a raw XML transaction message, the handler decodes it, looks up the item's `ExternalItemId` from the `itemId`, confirms the `skuId` exists in the SKU table, and returns a "send to queue" outcome whose JSON is the decoded message plus `ExternalItemId`. This ticket establishes the .NET 10 solution, the XML message contract (about 40 fields, including `itemId` and `skuId`), the item and SKU table definitions (numeric item key, text SKU key, `ExternalItemId` required), and the handler-level test seam: a real SQL Server in a container, seeded with small tables, and in-memory fakes for the queue and event hub senders. Spec: `.scratch/transaction-processor-poc/spec.md`.

**Blocked by:** None (can start immediately)

**Status:** resolved

- [x] The solution builds on .NET 10 and the test suite runs locally with a SQL Server container, with no Azure resources needed
- [x] The message contract covers about 40 fields, including `itemId` and `skuId`, and decodes from XML with the standard XML serializer (no XSD validation)
- [x] The item table (clustered primary key on the item key, required `ExternalItemId`) and SKU table (clustered primary key on the SKU key) are defined, and the test setup seeds them
- [x] A test with an existing item and an existing SKU gets a "send to queue" outcome
- [x] The queue JSON contains every decoded field plus `ExternalItemId`, and the test asserts on this output rather than on internal calls
- [x] The handler takes the raw message body and returns an outcome. It does not depend on Service Bus types, and outbound sends go through small interfaces so fakes can stand in
- [x] The tests check external behaviour only (outcome and payload)
