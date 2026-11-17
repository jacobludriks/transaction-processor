using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;

namespace TransactionProcessor.Tests.Infrastructure;

/// <summary>
/// Runs one SQL Server container for the whole test run by driving the <c>wslc</c> CLI
/// (Docker is not installed; Testcontainers cannot reach wslc). The container is removed on
/// teardown, including when startup fails part-way. Tests get isolation by calling
/// <see cref="CreateDatabaseAsync"/>, which returns a fresh database with empty reference tables
/// that is dropped on dispose.
/// </summary>
public sealed class WslcSqlServerFixture : IAsyncLifetime
{
    private const string Image = "mcr.microsoft.com/mssql/server:2022-latest";
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromMinutes(3);

    private readonly string _containerName = $"txproc-tests-{Guid.NewGuid():N}"[..24];
    private readonly string _saPassword = GeneratePassword();
    private int _port;
    private bool _containerCreated;

    private string MasterConnectionString => new SqlConnectionStringBuilder
    {
        DataSource = $"127.0.0.1,{_port}",
        UserID = "sa",
        Password = _saPassword,
        InitialCatalog = "master",
        TrustServerCertificate = true,
        Encrypt = SqlConnectionEncryptOption.Optional,
        ConnectTimeout = 5,
    }.ConnectionString;

    public async Task InitializeAsync()
    {
        try
        {
            _port = FindFreePort();
            _containerCreated = true; // set first: a failure mid-run may still leave a container behind
            await Wslc.RunAsync(
                "run", "-d", "--rm", "--name", _containerName,
                "-e", "ACCEPT_EULA=Y",
                "-e", $"MSSQL_SA_PASSWORD={_saPassword}",
                "-p", $"{_port}:1433",
                Image);
            await WaitUntilReadyAsync();
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (!_containerCreated) return;
        _containerCreated = false;
        // Best effort: --rm removes the container once killed; the explicit remove covers the case where it did not.
        await Wslc.TryRunAsync("kill", _containerName);
        await Wslc.TryRunAsync("remove", _containerName);
    }

    /// <summary>Creates a uniquely named database with the reference tables and returns a handle to seed it.</summary>
    public async Task<TestDatabase> CreateDatabaseAsync()
    {
        var name = $"test_{Guid.NewGuid():N}";
        await using (var connection = new SqlConnection(MasterConnectionString))
        {
            await connection.OpenAsync();
            await using var create = connection.CreateCommand();
            create.CommandText = $"CREATE DATABASE [{name}]";
            await create.ExecuteNonQueryAsync();
        }

        var connectionString = new SqlConnectionStringBuilder(MasterConnectionString) { InitialCatalog = name }.ConnectionString;
        var database = new TestDatabase(name, connectionString, MasterConnectionString);
        try
        {
            await database.CreateSchemaAsync();
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
        return database;
    }

    private async Task WaitUntilReadyAsync()
    {
        var deadline = DateTime.UtcNow + ReadyTimeout;
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await using var connection = new SqlConnection(MasterConnectionString);
                await connection.OpenAsync();
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
        var logs = await Wslc.TryRunAsync("logs", _containerName);
        throw new TimeoutException(
            $"SQL Server container '{_containerName}' was not ready within {ReadyTimeout}. Last error: {last?.Message}\n{logs}");
    }

    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }

    private static string GeneratePassword() =>
        "Aa1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12));

    private static class Wslc
    {
        public static async Task<string> RunAsync(params string[] args)
        {
            var (exitCode, output) = await ExecuteAsync(args);
            if (exitCode != 0)
                throw new InvalidOperationException($"wslc {args[0]} failed with exit code {exitCode}: {output}");
            return output;
        }

        public static async Task<string> TryRunAsync(params string[] args)
        {
            try { return (await ExecuteAsync(args)).Output; }
            catch (Exception ex) { return ex.Message; }
        }

        private static async Task<(int ExitCode, string Output)> ExecuteAsync(string[] args)
        {
            var startInfo = new ProcessStartInfo("wslc")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var arg in args) startInfo.ArgumentList.Add(arg);

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start wslc.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return (process.ExitCode, (await stdout + await stderr).Trim());
        }
    }
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<WslcSqlServerFixture>
{
    public const string Name = "SQL Server";
}
