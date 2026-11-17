using System.ComponentModel.DataAnnotations;

namespace TransactionProcessor.Host;

/// <summary>
/// Everything the host needs from app settings (section <c>Processor</c>; on App Service Linux the
/// settings are named <c>Processor__ServiceBusNamespace</c> and so on). No secrets: the only credential
/// is the user-assigned managed identity named by <see cref="ManagedIdentityClientId"/>.
/// </summary>
public sealed class ProcessorOptions
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
    [Range(1, 10_000)] public int MaxConcurrentCalls { get; set; } = 16;

    /// <summary>Messages the Service Bus client fetches ahead of processing. 0 disables prefetch.</summary>
    [Range(0, 100_000)] public int PrefetchCount { get; set; } = 32;
}
