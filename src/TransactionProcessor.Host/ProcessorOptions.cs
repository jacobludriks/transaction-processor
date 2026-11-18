using System.ComponentModel.DataAnnotations;

namespace TransactionProcessor.Host;

/// <summary>
/// Everything the host needs from app settings (section <c>Processor</c>; on App Service Linux the
/// settings are named <c>Processor__ServiceBusNamespace</c> and so on). No secrets: the only credential
/// is the user-assigned managed identity named by <see cref="ManagedIdentityClientId"/>.
/// </summary>
public sealed class ProcessorOptions : IValidatableObject
{
    public const string SectionName = "Processor";

    /// <summary>Client ID of the user-assigned managed identity used for Service Bus, Event Hubs and SQL.</summary>
    [Required] public string ManagedIdentityClientId { get; set; } = "";

    /// <summary>Fully qualified Service Bus namespace, for example <c>txproc-sb-abc123.servicebus.windows.net</c>.</summary>
    [Required] public string ServiceBusNamespace { get; set; } = "";

    [Required] public string TopicName { get; set; } = "";
    [Required] public string SubscriptionName { get; set; } = "";
    [Required] public string QueueName { get; set; } = "";

    /// <summary>Fully qualified Event Hubs namespace.</summary>
    [Required] public string EventHubsNamespace { get; set; } = "";

    [Required] public string EventHubName { get; set; } = "";

    /// <summary>Fully qualified SQL server name, for example <c>txproc-sql-abc123.database.windows.net</c>.</summary>
    [Required] public string SqlServer { get; set; } = "";

    [Required] public string SqlDatabase { get; set; } = "";

    /// <summary>Messages handled at once per instance. A benchmark variable, so it is a setting.</summary>
    /// <remarks>A total for the instance, split across the <see cref="ProcessorCount"/> processors.</remarks>
    [Range(1, 10_000)] public int MaxConcurrentCalls { get; set; } = 16;

    /// <summary>
    /// Messages the Service Bus client fetches ahead of processing, in total for the instance (split across the
    /// processors like <see cref="MaxConcurrentCalls"/>). 0 disables prefetch. Keep it at or above
    /// <see cref="MaxConcurrentCalls"/>, or handlers wait on receive round trips instead of taking prefetched messages.
    /// </summary>
    [Range(0, 100_000)] public int PrefetchCount { get; set; } = 150;

    /// <summary>
    /// Service Bus processors per instance, each with its own client and so its own AMQP connection and receive link.
    /// One shared connection serialises receive and settle traffic; several spread it.
    /// </summary>
    [Range(1, 64)] public int ProcessorCount { get; set; } = 4;

    /// <summary>Concurrent calls for each processor: <see cref="MaxConcurrentCalls"/> split as evenly as possible.</summary>
    public int[] MaxConcurrentCallsPerProcessor() => Split(MaxConcurrentCalls, ProcessorCount);

    /// <summary>Prefetch count for each processor: <see cref="PrefetchCount"/> split as evenly as possible.</summary>
    public int[] PrefetchCountPerProcessor() => Split(PrefetchCount, ProcessorCount);

    /// <summary>Splits <paramref name="total"/> into <paramref name="parts"/> parts that differ by at most one and sum to the total.</summary>
    public static int[] Split(int total, int parts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(parts, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(total);
        var result = new int[parts];
        for (var i = 0; i < parts; i++) result[i] = total / parts + (i < total % parts ? 1 : 0);
        return result;
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ProcessorCount > MaxConcurrentCalls)
            yield return new ValidationResult(
                $"{nameof(ProcessorCount)} ({ProcessorCount}) must not exceed {nameof(MaxConcurrentCalls)} ({MaxConcurrentCalls}): every processor needs at least one concurrent call.",
                [nameof(ProcessorCount), nameof(MaxConcurrentCalls)]);
    }
}
