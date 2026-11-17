using System.Diagnostics;
using Microsoft.Data.SqlClient;
using TransactionProcessor;

namespace DataSeeder;

/// <summary>Command line front end: builds the connection, runs the seeder and prints elapsed time and row counts.</summary>
public static class SeederCli
{
    public const string Usage = """
        Usage:
          DataSeeder --connection-string <connection string> [--items <n>] [--skus <n>]
          DataSeeder --server <name>.database.windows.net --database <db> --client-id <tooling identity client id> [--items <n>] [--skus <n>]

        The first form is for a local throwaway SQL Server container only. The second form signs in with the VM's
        user-assigned managed identity (no password) and is the one to use against Azure. --items and --skus
        default to 1,000,000 each. Running again on a seeded database adds nothing.
        """;

    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken ct = default)
    {
        string? connectionString = null, server = null, database = null, clientId = null;
        var items = ReferenceDataKeys.ItemCount;
        var skus = ReferenceDataKeys.SkuCount;

        try
        {
            for (var i = 0; i < args.Length; i++)
            {
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {args[i]}.");
                switch (args[i])
                {
                    case "--connection-string": connectionString = Next(); break;
                    case "--server": server = Next(); break;
                    case "--database": database = Next(); break;
                    case "--client-id": clientId = Next(); break;
                    case "--items": items = int.Parse(Next()); break;
                    case "--skus": skus = int.Parse(Next()); break;
                    case "-h" or "--help": output.WriteLine(Usage); return 0;
                    default: throw new ArgumentException($"Unknown argument {args[i]}.");
                }
            }

            if (connectionString is null)
            {
                if (server is null || database is null || clientId is null)
                    throw new ArgumentException("Give --connection-string, or all of --server, --database and --client-id.");
                connectionString = SqlConnectionStrings.ForManagedIdentity(server, database, clientId, connectTimeoutSeconds: 60);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
        {
            error.WriteLine(ex.Message);
            error.WriteLine(Usage);
            return 2;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await ReferenceDataSeeder.SeedAsync(connectionString, items, skus, ct);
            stopwatch.Stop();
            output.WriteLine($"Item rows: {result.ItemRows:N0} ({result.ItemRowsAdded:N0} added this run)");
            output.WriteLine($"Sku rows:  {result.SkuRows:N0} ({result.SkuRowsAdded:N0} added this run)");
            output.WriteLine($"Elapsed: {stopwatch.Elapsed.TotalSeconds:F1} s");
            return 0;
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or TimeoutException)
        {
            error.WriteLine($"Seeding failed after {stopwatch.Elapsed.TotalSeconds:F1} s: {ex.Message}");
            return 1;
        }
    }
}
