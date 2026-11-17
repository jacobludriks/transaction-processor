# 09: Data seeder (1M items, 1M SKUs)

**What to build:** a tool that runs on the tooling VM and fills the database with realistic reference data: about 1,000,000 item rows (each with an `ExternalItemId`) and 1,000,000 SKU rows, with the lookup key as the clustered primary key. It uses the table definitions from ticket 01 and the tooling identity's write access. It must be repeatable, so the database can be re-seeded after the environment is torn down and rebuilt, and it must be fast enough not to burn the runtime budget. Spec: `.scratch/transaction-processor-poc/spec.md`.

**Blocked by:** 01, 07

**Status:** ready-for-agent

- [ ] Running the seeder on the VM produces about 1,000,000 rows in each table
- [ ] Key values follow a predictable scheme (for example sequential item keys and zero-padded SKU keys), so the load generator can pick valid and deliberately invalid keys from the same scheme
- [ ] Each `ExternalItemId` is derivable from the item key, so output can be checked
- [ ] Running it a second time on a seeded database doesn't duplicate rows or fail
- [ ] The seed completes in a time short enough for the AU$30 target and the 6-hour runtime cap, and the time taken is recorded
- [ ] A quick spot check shows lookups by key use the clustered index
