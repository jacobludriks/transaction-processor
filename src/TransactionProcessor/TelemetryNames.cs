namespace TransactionProcessor;

/// <summary>
/// The values of the <c>outcome</c> tag on the host's <c>txproc.messages</c> counter. They are a contract between
/// the host (which writes them), the exported metrics and the run report (which reads them back), so they live
/// here, with the handler library both sides reference. The values are stable identifiers: do not rename them.
/// </summary>
public static class MessageOutcomes
{
    public const string SentToQueue = "sent_to_queue";
    public const string SentToEventHub = "sent_to_event_hub";
    public const string DeadLettered = "dead_lettered";
    public const string Abandoned = "abandoned";

    /// <summary>True for the outcomes that mean the handler sent its output (and the message was completed).</summary>
    public static bool IsOutput(string outcome) => outcome is SentToQueue or SentToEventHub;
}

/// <summary>The values of the <c>reason</c> tag when the outcome is <see cref="MessageOutcomes.Abandoned"/>.</summary>
public static class AbandonReasons
{
    /// <summary>The handler returned a retry outcome (a database error or a failed send).</summary>
    public const string TransientFailure = "transient_failure";

    /// <summary>The handler threw something it should have turned into an outcome: a bug.</summary>
    public const string UnhandledException = "unhandled_exception";
}

/// <summary>Labels of the latency bucket counter (<c>txproc.message.latency.bucket</c>).</summary>
public static class LatencyBucketLabels
{
    /// <summary>The <c>le</c> label of the overflow bucket: latency above the largest finite bound.</summary>
    public const string Overflow = "+Inf";
}
