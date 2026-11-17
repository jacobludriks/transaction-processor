# Load generator

Publishes synthetic XML transaction messages (about 40 fields, 4 to 6 KB each) to the Service Bus input topic at a steady rate. It runs on the tooling VM, inside the private network, and authenticates with the tooling managed identity (no connection strings or keys).

Messages use the processor's contract. `itemId` is uniform over the seeded items and `skuId` is uniform over the seeded SKUs, except that a configurable share (default 10%) uses a SKU that is never seeded. All keys come from the shared key scheme in the handler library, so they always agree with the data seeder. Load mode never omits `itemId`.

Each message carries the application property `generatedAtUtc` (ISO 8601, set just before the batch is sent), plus `label`. Service Bus also stamps its own enqueue time on every message. Either can be used to measure latency at the output.

## Run it on the VM

The VM has the .NET 10 SDK. Copy or clone the repo, then (the namespace FQDN and the tooling identity's client ID are deployment outputs):

```bash
cd src/LoadGenerator
dotnet run -c Release -- load \
  --namespace <name>.servicebus.windows.net \
  --client-id <tooling identity client id> \
  --rate 300 --duration 600
```

The tooling identity needs the Azure Service Bus Data Sender role on the topic (set up by the identity and role deployment). The topic defaults to `transactions-in`.

Options for `load`:

| Option | Default | Meaning |
|---|---|---|
| `--rate` | 300 | target messages per second |
| `--duration` | 60 | seconds to run |
| `--unmatched-ratio` | 0.10 | share of messages with an unknown SKU |
| `--senders` | 4 | concurrent senders (one Service Bus sender each) |
| `--batch-size` | 100 | messages per send (a batch is also split if it reaches the Service Bus size limit) |
| `--seed` | 12345 | random seed, so a run is repeatable |
| `--summary-file` | off | write a machine-readable run summary (JSON: start and end time, target and achieved rate, messages sent and failed, settings) for the measurement report in `src/RunReport`. Use `--duration 840` (14 minutes) so the report has ten whole minutes to measure |
| `--topic` | `transactions-in` | topic name |
| `--dry-run` | off | generate and pace but send nothing; needs no Azure access |

It prints progress every 5 seconds (sent, failed, backlog, interval and average rate) and a summary with the rate actually achieved. It exits with code 1 and a warning if it achieved less than 98% of the target or any send failed; do not use such a run to judge the processor. The pacing is on an absolute schedule, so a late timer wake-up is made up rather than slowing the run. If the senders cannot keep up, the achieved rate falls and the backlog figure grows.

To check the generator is not the limit before a benchmark, use a dry run at a multiple of the rate you will test, for example `--dry-run --rate 1200 --duration 30`, and confirm it holds 100%. A dry run measures generation and pacing only, not the network or the Service Bus client, so also run the real thing at 2x your target against a throwaway period and check it reports at least 98%.

## Functional set

Sends once: one malformed XML message and one message with no `itemId`. Add `--include-missing-sku` and `--include-unknown-item` for one message with no `skuId` and one whose `itemId` does not exist. Each message is labelled in the output and in its `label` property, and carries `expectedDeadLetterReason`.

```bash
dotnet run -c Release -- functional --namespace <name>.servicebus.windows.net --client-id <id> \
  --include-missing-sku --include-unknown-item
```

Expected dead-letter reasons: `InvalidXml`, `MissingItemId`, `MissingSkuId`, `ItemNotFound`. Check them on the subscription's dead-letter queue.

## Tests

`dotnet test` from the repo root. The generator tests need no Azure. Some functional-set tests use the SQL Server container fixture shared with the handler tests (`wslc`).
