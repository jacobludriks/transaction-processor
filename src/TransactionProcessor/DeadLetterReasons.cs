namespace TransactionProcessor;

/// <summary>
/// The machine-readable reasons a message is dead-lettered by the handler. The values are stable
/// identifiers (they appear as the dead-letter reason in Service Bus), so they can be grouped and
/// counted. Do not rename them.
/// </summary>
public static class DeadLetterReasons
{
    /// <summary>The body is not XML that decodes into a transaction message.</summary>
    public const string InvalidXml = "InvalidXml";

    /// <summary>The message has no <c>itemId</c>.</summary>
    public const string MissingItemId = "MissingItemId";

    /// <summary>The message has no <c>skuId</c>.</summary>
    public const string MissingSkuId = "MissingSkuId";

    /// <summary>No item row exists for the message's <c>itemId</c>. A failure, never a routing outcome.</summary>
    public const string ItemNotFound = "ItemNotFound";
}
