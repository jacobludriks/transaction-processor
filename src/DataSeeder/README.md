# Data seeder

Fills the `Item` and `Sku` tables with about 1,000,000 rows each, using the shared key scheme in `ReferenceDataKeys` (items `1..N` with `ExternalItemId` `EXT-<id>`, SKUs `SKU-0000001..`) and the table definitions in `ReferenceDataSchema`. It creates the tables if they are missing, bulk loads in batches of 50,000, and is safe to run again: it only loads keys that are missing, so a second run adds nothing and an interrupted run is completed. It prints row counts and elapsed time.

## Run it on the tooling VM

The VM has the .NET 10 SDK (see `infra/modules/cloud-init.yaml`). Use the tooling identity (the one with SQL write access, set up by `infra/scripts/vm/setup-sql-users.sh`). No password is involved: the seeder signs in with the VM's user-assigned managed identity, selected by its client ID.

```bash
git clone <repo> && cd transaction-simulator        # or copy the repo to the VM
dotnet run -c Release --project src/DataSeeder -- \
  --server <sql-server-name>.database.windows.net \
  --database <database-name> \
  --client-id <tooling identity client id>
```

The server name resolves to the private endpoint from inside the VNet. `--items` and `--skus` override the default of 1,000,000 each. Run it once after each deployment (the database is empty after a teardown and rebuild). Exit code 0 means success.

## Run it locally against a SQL Server container (local testing only)

The plain `--connection-string` option exists only for a throwaway SQL Server container on the developer machine (the `wslc` container the tests and local timing use), where the `sa` password is a disposable value that exists nowhere else. **Never use it against the Azure SQL database**; on the VM use the managed identity form above, which takes no password.

```bash
# Local throwaway container only. The password is the container's own, read from an environment variable rather than typed on the command line or committed.
dotnet run -c Release --project src/DataSeeder -- \
  --connection-string "Server=127.0.0.1,<port>;User Id=sa;Password=$LOCAL_SA_PASSWORD;TrustServerCertificate=True;Encrypt=Optional;Initial Catalog=<db>"
```

The database must already exist. Do not commit connection strings.

## Check the lookups use the clustered index

In `sqlcmd` (same sign-in as above, for example `sqlcmd -S <server> -d <db> --authentication-method ActiveDirectoryManagedIdentity -U <client id>`):

```sql
SET STATISTICS PROFILE ON;
SELECT ExternalItemId FROM dbo.Item WHERE ItemId = 765432;
SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.Sku WHERE SkuId = N'SKU-0123456') THEN 1 ELSE 0 END;
```

Both plans should show `Clustered Index Seek` on `PK_Item` and `PK_Sku`.

## Tests

`dotnet test` runs the seeder tests against the `wslc` SQL Server fixture with small row counts.
