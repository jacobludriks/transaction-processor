using System.Globalization;
using Azure.Messaging.ServiceBus;
using LoadGenerator;

CliOptions options;
try
{
    options = CliOptions.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(CliOptions.Usage);
    return 2;
}

using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };

ServiceBusClient? client = options.DryRun ? null : ServiceBusSink.CreateClient(options.Namespace!, options.ClientId);
try
{
    return options.Mode == "load"
        ? await RunLoadAsync(options, client, cancel.Token)
        : await RunFunctionalAsync(options, client, cancel.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Cancelled.");
    return 130;
}
finally
{
    if (client is not null) await client.DisposeAsync();
}

static async Task<int> RunLoadAsync(CliOptions cli, ServiceBusClient? client, CancellationToken cancellationToken)
{
    var publishOptions = new PublishOptions
    {
        Rate = cli.Rate,
        Duration = TimeSpan.FromSeconds(cli.DurationSeconds),
        Senders = cli.Senders,
        BatchSize = cli.BatchSize,
        UnmatchedRatio = cli.UnmatchedRatio,
        Seed = cli.Seed,
    };
    Console.WriteLine(
        $"{(cli.DryRun ? "DRY RUN (nothing is sent) - " : "")}Target {cli.Rate:0.##} msg/s for {cli.DurationSeconds:0.##} s " +
        $"({cli.Rate * cli.DurationSeconds:N0} messages), {cli.Senders} senders, batch {cli.BatchSize}, " +
        $"{cli.UnmatchedRatio:P0} unmatched SKUs, seed {cli.Seed}" +
        (cli.DryRun ? "." : $", topic '{cli.Topic}' on {cli.Namespace}."));

    Func<int, IMessageSink> createSink = cli.DryRun
        ? _ => new NoOpSink()
        : _ => new ServiceBusSink(client!.CreateSender(cli.Topic));

    var startedAtUtc = DateTimeOffset.UtcNow;
    var summary = await new Publisher(new SystemClock()).RunAsync(
        publishOptions, createSink,
        p => Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"[{p.Elapsed.TotalSeconds,7:0.0}s] sent {p.Sent:N0}  failed {p.Failed:N0}  backlog {p.Backlog:N0}  rate {p.IntervalRate:0.0}/s (avg {p.AverageRate:0.0}/s)")),
        cancellationToken);

    var ratio = summary.AchievedRate / cli.Rate;
    Console.WriteLine();
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"Summary: sent {summary.Sent:N0}, failed {summary.Failed:N0}, elapsed {summary.Elapsed.TotalSeconds:0.00} s"));
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"Target rate {summary.TargetRate:0.##} msg/s, achieved {summary.AchievedRate:0.0} msg/s ({ratio:P1} of target)"));
    if (summary.FirstError is not null) Console.WriteLine($"First error: {summary.FirstError}");
    if (cli.SummaryFile is not null)
    {
        await File.WriteAllTextAsync(cli.SummaryFile, RunSummaryFile.ToJson(cli, summary, startedAtUtc), CancellationToken.None);
        Console.WriteLine($"Run summary written to {cli.SummaryFile}");
    }
    if (!summary.HeldTargetRate)
    {
        Console.WriteLine("WARNING: the generator did not hold the target rate (or had send failures). Do not use this run to judge the processor.");
        return 1;
    }
    return 0;
}

static async Task<int> RunFunctionalAsync(CliOptions cli, ServiceBusClient? client, CancellationToken cancellationToken)
{
    var generator = new MessageGenerator(cli.Seed);
    var set = generator.FunctionalSet(cli.IncludeMissingSku, cli.IncludeUnknownItem);
    Console.WriteLine($"{(cli.DryRun ? "DRY RUN (nothing is sent) - " : "")}Functional set: {set.Count} message(s). Each should be dead-lettered with the reason shown.");

    await using IMessageSink sink = cli.DryRun ? new NoOpSink() : new ServiceBusSink(client!.CreateSender(cli.Topic));
    foreach (var message in set)
    {
        await sink.SendAsync([message], cancellationToken);
        Console.WriteLine($"  {(cli.DryRun ? "generated" : "sent")}: {message.Label,-28} expect dead-letter reason {message.ExpectedDeadLetterReason}");
    }
    return 0;
}
