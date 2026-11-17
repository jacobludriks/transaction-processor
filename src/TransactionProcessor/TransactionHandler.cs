using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.Data.SqlClient;

namespace TransactionProcessor;

/// <summary>
/// The processor's behaviour in one place: takes a raw XML message body, looks up the item and SKU,
/// sends the JSON output through the outbound interfaces and returns what the receive loop should do.
/// It has no dependency on Service Bus or Event Hubs types.
/// </summary>
public sealed class TransactionHandler(string sqlConnectionString, IQueueSender queue, IEventHubSender eventHub)
{
    private static readonly XmlSerializer Serializer = new(typeof(TransactionMessage));
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<HandlerOutcome> HandleAsync(string xmlBody, CancellationToken cancellationToken = default)
    {
        if (!TryDecode(xmlBody, out var message))
            return new HandlerOutcome.DeadLetter(DeadLetterReasons.InvalidXml);
        if (message.ItemId is null)
            return new HandlerOutcome.DeadLetter(DeadLetterReasons.MissingItemId);
        if (string.IsNullOrWhiteSpace(message.SkuId))
            return new HandlerOutcome.DeadLetter(DeadLetterReasons.MissingSkuId);

        try
        {
            var externalItemId = await FindExternalItemIdAsync(message.ItemId!.Value, cancellationToken);
            if (externalItemId is null)
                return new HandlerOutcome.DeadLetter(DeadLetterReasons.ItemNotFound);
            if (!await SkuExistsAsync(message.SkuId!, cancellationToken))
            {
                await SendAsync(eventHub.SendAsync, ToJson(message, externalItemId), cancellationToken);
                return new HandlerOutcome.SendToEventHub();
            }

            await SendAsync(queue.SendAsync, ToJson(message, externalItemId), cancellationToken);
            return new HandlerOutcome.SendToQueue();
        }
        // Transient failures only: a database error, or a failed send. Anything else (parse failures,
        // a missing item) is not caught here, so it is never turned into a retry.
        catch (Exception ex) when (ex is SqlException or SendFailedException)
        {
            return new HandlerOutcome.Retry();
        }
    }

    /// <summary>A send to the queue or event hub failed; the message should be redelivered.</summary>
    private sealed class SendFailedException(Exception inner) : Exception(inner.Message, inner);

    private static async Task SendAsync(Func<string, CancellationToken, Task> send, string json, CancellationToken cancellationToken)
    {
        try
        {
            await send(json, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new SendFailedException(ex);
        }
    }

    /// <summary>Decodes the body. Returns false when it is not XML that decodes into a transaction message.</summary>
    private static bool TryDecode(string xmlBody, out TransactionMessage message)
    {
        try
        {
            using var reader = XmlReader.Create(new StringReader(xmlBody), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            message = (TransactionMessage)Serializer.Deserialize(reader)!;
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or XmlException)
        {
            // XmlSerializer wraps XML and conversion errors in InvalidOperationException.
            message = null!;
            return false;
        }
    }

    private static string ToJson(TransactionMessage message, string externalItemId)
    {
        var node = JsonSerializer.SerializeToNode(message, JsonOptions)!.AsObject();
        node["ExternalItemId"] = externalItemId;
        return node.ToJsonString();
    }

    private async Task<string?> FindExternalItemIdAsync(long itemId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(sqlConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ExternalItemId FROM dbo.Item WHERE ItemId = @itemId";
        command.Parameters.AddWithValue("@itemId", itemId);
        return (string?)await command.ExecuteScalarAsync(cancellationToken);
    }

    private async Task<bool> SkuExistsAsync(string skuId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(sqlConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.Sku WHERE SkuId = @skuId) THEN 1 ELSE 0 END";
        command.Parameters.AddWithValue("@skuId", skuId);
        return (int)(await command.ExecuteScalarAsync(cancellationToken))! == 1;
    }
}
