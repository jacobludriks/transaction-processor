using System.Globalization;

namespace LoadGenerator;

public sealed class CliOptions
{
    public const string Usage = """
        Usage:
          LoadGenerator load        [options]   publish load messages at a steady rate
          LoadGenerator functional  [options]   send the small labelled set of bad messages (once)

        Connection (not needed with --dry-run):
          --namespace <fqdn>          Service Bus namespace, e.g. mysb.servicebus.windows.net
          --topic <name>              input topic (default transactions-in)
          --client-id <guid>          client ID of the managed identity to use (the tooling identity);
                                      omit to use the signed-in developer account

        Load mode:
          --rate <msgs/sec>           target rate (default 300)
          --duration <seconds>        how long to run (default 60)
          --unmatched-ratio <0..1>    share of messages with an unknown SKU (default 0.10)
          --senders <n>               concurrent senders (default 4)
          --batch-size <n>            messages per send (default 100)
          --seed <n>                  random seed (default 12345)
          --dry-run                   generate and pace, but send nothing (no Azure needed)

        Functional mode:
          --include-missing-sku       also send one message with no skuId
          --include-unknown-item      also send one message whose itemId does not exist
          --dry-run                   print the messages' labels without sending
        """;

    public required string Mode { get; init; }
    public string? Namespace { get; init; }
    public string Topic { get; init; } = "transactions-in";
    public string? ClientId { get; init; }
    public double Rate { get; init; } = 300;
    public double DurationSeconds { get; init; } = 60;
    public double UnmatchedRatio { get; init; } = 0.10;
    public int Senders { get; init; } = 4;
    public int BatchSize { get; init; } = 100;
    public int Seed { get; init; } = 12345;
    public bool DryRun { get; init; }
    public bool IncludeMissingSku { get; init; }
    public bool IncludeUnknownItem { get; init; }

    /// <summary>Parses the command line. Throws <see cref="ArgumentException"/> with a message for the operator on bad input.</summary>
    public static CliOptions Parse(string[] args)
    {
        if (args.Length == 0 || args[0] is not ("load" or "functional"))
            throw new ArgumentException("The first argument must be 'load' or 'functional'.");

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "--dry-run" or "--include-missing-sku" or "--include-unknown-item") flags.Add(arg);
            else if (arg.StartsWith("--", StringComparison.Ordinal) && i + 1 < args.Length) values[arg] = args[++i];
            else throw new ArgumentException($"Unknown or incomplete option '{arg}'.");
        }

        var known = new HashSet<string> { "--namespace", "--topic", "--client-id", "--rate", "--duration", "--unmatched-ratio", "--senders", "--batch-size", "--seed" };
        var unknown = values.Keys.FirstOrDefault(k => !known.Contains(k));
        if (unknown is not null) throw new ArgumentException($"Unknown option '{unknown}'.");

        var options = new CliOptions
        {
            Mode = args[0],
            Namespace = values.GetValueOrDefault("--namespace"),
            Topic = values.GetValueOrDefault("--topic") ?? "transactions-in",
            ClientId = values.GetValueOrDefault("--client-id"),
            Rate = Double(values, "--rate", 300),
            DurationSeconds = Double(values, "--duration", 60),
            UnmatchedRatio = Double(values, "--unmatched-ratio", 0.10),
            Senders = (int)Double(values, "--senders", 4),
            BatchSize = (int)Double(values, "--batch-size", 100),
            Seed = (int)Double(values, "--seed", 12345),
            DryRun = flags.Contains("--dry-run"),
            IncludeMissingSku = flags.Contains("--include-missing-sku"),
            IncludeUnknownItem = flags.Contains("--include-unknown-item"),
        };

        if (options.Rate <= 0) throw new ArgumentException("--rate must be greater than 0.");
        if (options.DurationSeconds <= 0) throw new ArgumentException("--duration must be greater than 0.");
        if (options.UnmatchedRatio is < 0 or > 1) throw new ArgumentException("--unmatched-ratio must be between 0 and 1.");
        if (options.Senders < 1) throw new ArgumentException("--senders must be at least 1.");
        if (options.BatchSize < 1) throw new ArgumentException("--batch-size must be at least 1.");
        if (!options.DryRun && string.IsNullOrWhiteSpace(options.Namespace))
            throw new ArgumentException("--namespace is required unless --dry-run is given.");
        return options;
    }

    private static double Double(Dictionary<string, string> values, string name, double fallback) =>
        values.TryGetValue(name, out var text)
            ? double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : throw new ArgumentException($"{name} must be a number, got '{text}'.")
            : fallback;
}
