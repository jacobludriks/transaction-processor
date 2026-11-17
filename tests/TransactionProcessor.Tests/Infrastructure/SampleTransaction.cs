using System.Text;
using System.Text.Json;

namespace TransactionProcessor.Tests.Infrastructure;

/// <summary>
/// A hand-written sample transaction (about 40 fields) used to build raw XML bodies and to state
/// the JSON values expected back. It is deliberately independent of the production message class,
/// so the tests agree with the spec rather than with the code.
/// </summary>
public static class SampleTransaction
{
    public const long ItemId = 4711;
    public const string SkuId = "SKU-000123";

    /// <summary>XML element name, XML text, and the value expected in the JSON output.</summary>
    public static IReadOnlyList<(string Name, string XmlText, object Expected)> Fields { get; } =
    [
        ("transactionId", "7d5f2c1e-9a4b-4f3e-8c21-0b6a5d4e3f21", "7d5f2c1e-9a4b-4f3e-8c21-0b6a5d4e3f21"),
        ("itemId", ItemId.ToString(), ItemId),
        ("skuId", SkuId, SkuId),
        ("storeId", "STORE-0042", "STORE-0042"),
        ("registerId", "REG-07", "REG-07"),
        ("cashierId", "EMP-1138", "EMP-1138"),
        ("transactionType", "SALE", "SALE"),
        ("transactionTimestamp", "2026-03-14T09:26:53Z", "2026-03-14T09:26:53Z"),
        ("quantity", "3", 3L),
        ("unitPrice", "19.99", 19.99m),
        ("lineTotal", "59.97", 59.97m),
        ("discountAmount", "5.00", 5.00m),
        ("taxAmount", "5.50", 5.50m),
        ("taxRate", "0.10", 0.10m),
        ("currency", "AUD", "AUD"),
        ("paymentMethod", "CARD", "CARD"),
        ("cardLast4", "4242", "4242"),
        ("customerId", "CUST-998877", "CUST-998877"),
        ("loyaltyNumber", "LY-5566778899", "LY-5566778899"),
        ("channel", "IN_STORE", "IN_STORE"),
        ("deviceId", "DEV-A1B2C3", "DEV-A1B2C3"),
        ("batchId", "BATCH-20260314-01", "BATCH-20260314-01"),
        ("sequenceNumber", "1000234", 1000234L),
        ("isRefund", "false", false),
        ("isTaxExempt", "false", false),
        ("isManualEntry", "true", true),
        ("department", "Homewares", "Homewares"),
        ("category", "Kitchen", "Kitchen"),
        ("brand", "Acme", "Acme"),
        ("description", "Stainless steel saucepan 18cm", "Stainless steel saucepan 18cm"),
        ("unitOfMeasure", "EA", "EA"),
        ("weightKg", "1.250", 1.250m),
        ("promotionCode", "SPRING5", "SPRING5"),
        ("couponCode", "CPN-0099", "CPN-0099"),
        ("shippingMethod", "PICKUP", "PICKUP"),
        ("warehouseCode", "WH-SYD-02", "WH-SYD-02"),
        ("supplierCode", "SUP-3321", "SUP-3321"),
        ("notes", "Customer asked for a gift receipt & extra bag", "Customer asked for a gift receipt & extra bag"),
        ("sourceSystem", "POS", "POS"),
        ("schemaVersion", "1.0", "1.0"),
    ];

    /// <summary>The sample as a raw XML body, with <c>itemId</c> and <c>skuId</c> replaced as given.</summary>
    public static string Xml(long itemId = ItemId, string skuId = SkuId)
    {
        var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<transaction>\n");
        foreach (var (name, text, _) in Fields)
        {
            var value = name switch
            {
                "itemId" => itemId.ToString(),
                "skuId" => skuId,
                _ => text,
            };
            xml.Append("  <").Append(name).Append('>')
               .Append(System.Security.SecurityElement.Escape(value))
               .Append("</").Append(name).Append(">\n");
        }
        return xml.Append("</transaction>").ToString();
    }

    /// <summary>Asserts the JSON has exactly the sample's fields (with the given key values) plus ExternalItemId.</summary>
    public static void AssertJson(string json, string expectedExternalItemId, long itemId = ItemId, string skuId = SkuId)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(Fields.Count + 1, root.EnumerateObject().Count());

        foreach (var (name, _, expected) in Fields)
        {
            Assert.True(root.TryGetProperty(name, out var actual), $"JSON is missing '{name}'");
            switch (name == "itemId" ? itemId : name == "skuId" ? skuId : expected)
            {
                case string s: Assert.Equal(s, actual.GetString()); break;
                case long l: Assert.Equal(l, actual.GetInt64()); break;
                case decimal d: Assert.Equal(d, actual.GetDecimal()); break;
                case bool b: Assert.Equal(b, actual.GetBoolean()); break;
                default: throw new InvalidOperationException($"Unhandled expected type for '{name}'");
            }
        }

        Assert.Equal(expectedExternalItemId, root.GetProperty("ExternalItemId").GetString());
    }
}
