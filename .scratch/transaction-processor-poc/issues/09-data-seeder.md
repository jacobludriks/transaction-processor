# 09: Data seeder (1M items, 1M SKUs)

**What to build:** a tool that runs on the tooling VM and fills the database with realistic reference data: about 1,000,000 item rows (each with an `ExternalItemId`) and 1,000,000 SKU rows, with the lookup key as the clustered primary key. It uses the table definitions from ticket 01 and the tooling identity's write access. It must be repeatable, so the database can be re-seeded after the environment is torn down and rebuilt, and it must be fast enough not to burn the runtime budget. Spec: `.scratch/transaction-processor-poc/spec.md`.

**Blocked by:** 01, 07

**Status:** ready-for-agent

- [ ] Running the seeder on the VM produces about 1,000,000 rows in each table
- [x] Key values follow a predictable scheme (for example sequential item keys and zero-padded SKU keys), so the load generator can pick valid and deliberately invalid keys from the same scheme
- [x] Each `ExternalItemId` is derivable from the item key, so output can be checked
- [x] Running it a second time on a seeded database doesn't duplicate rows or fail
- [ ] The seed completes in a time short enough for the AU$30 target and the 6-hour runtime cap, and the time taken is recorded
- [x] A quick spot check shows lookups by key use the clustered index (verified locally on SQL Server 2022: Clustered Index Seek on PK_Item and PK_Sku)

## Comments

- Implementation notes (ticket 09): seeder is `src/DataSeeder` (README there). Locally, against a wslc SQL Server 2022 container, a full 1,000,000 items plus 1,000,000 SKUs load took 5.1 s; a second run added 0 rows in 0.2 s. Left unchecked, needs Azure: the run on the VM against Azure SQL (first criterion, with identity auth via `--client-id` implemented but untested against Azure) and the timing criterion (local timing is only a lower bound; Azure SQL General Purpose 4 vCores over a private endpoint will be slower, so the real time must be recorded on the VM).
