using System.Globalization;
using System.Text;
using TransactionProcessor;

namespace LoadGenerator;

/// <summary>
/// Pure message generation: no clock, no network. Produces XML bodies for the processor's transaction contract
/// (about 40 fields, 4-6 KB). All keys come from <see cref="ReferenceDataKeys"/>. Not thread safe: use one
/// instance per thread, each with its own seed.
/// </summary>
public sealed class MessageGenerator
{
    public const int MinBodyBytes = 4_300;
    public const int MaxBodyBytes = 5_700;

    private static readonly string[] Words =
    [
        "item", "scanned", "at", "register", "customer", "requested", "receipt", "bag", "promotion", "applied",
        "manager", "override", "price", "check", "shelf", "label", "barcode", "weight", "loyalty", "points",
        "refund", "exchange", "gift", "wrap", "delivery", "pickup", "backorder", "supplier", "batch", "audit",
        "return", "damaged", "packaging", "seasonal", "clearance", "bundle", "voucher", "tender", "rounding", "adjustment",
    ];

    private static readonly string[] Departments = ["Homewares", "Grocery", "Apparel", "Electronics", "Garden", "Toys", "Health"];
    private static readonly string[] Categories = ["Kitchen", "Dairy", "Mens", "Audio", "Outdoor", "Puzzles", "Vitamins"];
    private static readonly string[] Brands = ["Acme", "Northwind", "Contoso", "Fabrikam", "Tailspin", "Litware"];
    private static readonly string[] Currencies = ["AUD", "AUD", "AUD", "NZD"];
    private static readonly string[] PaymentMethods = ["CARD", "CASH", "EFTPOS", "GIFTCARD", "MOBILE"];
    private static readonly string[] Channels = ["IN_STORE", "ONLINE", "CLICK_COLLECT"];
    private static readonly string[] TransactionTypes = ["SALE", "SALE", "SALE", "RETURN", "VOID"];
    private static readonly string[] Units = ["EA", "KG", "PK", "L"];
    private static readonly string[] Shipping = ["PICKUP", "STANDARD", "EXPRESS"];
    private static readonly string[] Warehouses = ["WH-SYD-02", "WH-MEL-01", "WH-BNE-03"];
    private static readonly DateTime BaseTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly Random _random;
    private readonly double _unmatchedRatio;
    private readonly StringBuilder _builder = new(8 * 1024);

    public MessageGenerator(int seed, double unmatchedRatio = 0.10)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(unmatchedRatio, 0.0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(unmatchedRatio, 1.0);
        _random = new Random(seed);
        _unmatchedRatio = unmatchedRatio;
    }

    /// <summary>A well-formed load message: always has an item id in range, and a SKU that matches with probability 1 - unmatchedRatio.</summary>
    public GeneratedMessage NextLoadMessage()
    {
        var itemId = NextItemId();
        var unmatched = _random.NextDouble() < _unmatchedRatio;
        var skuNumber = unmatched
            ? _random.Next(ReferenceDataKeys.UnknownSkuFirst, ReferenceDataKeys.UnknownSkuLast + 1)
            : _random.Next(1, ReferenceDataKeys.SkuCount + 1);
        return new GeneratedMessage(
            BuildXml(itemId, ReferenceDataKeys.SkuId(skuNumber)),
            unmatched ? "load-unmatched-sku" : "load-matched-sku");
    }

    /// <summary>
    /// The functional set, each message labelled with what the operator should expect: one malformed XML message
    /// (InvalidXml), one with no itemId (MissingItemId), and optionally one with no skuId (MissingSkuId) and one
    /// whose itemId is above the seeded range (ItemNotFound). All are expected to be dead-lettered.
    /// </summary>
    public IReadOnlyList<GeneratedMessage> FunctionalSet(bool includeMissingSku = false, bool includeUnknownItem = false)
    {
        var set = new List<GeneratedMessage>
        {
            new(BuildMalformedXml(), "functional-malformed-xml", DeadLetterReasons.InvalidXml),
            new(BuildXml(NextItemId(), NextMatchedSku(), omitItemId: true), "functional-missing-itemId", DeadLetterReasons.MissingItemId),
        };
        if (includeMissingSku)
            set.Add(new(BuildXml(NextItemId(), NextMatchedSku(), omitSkuId: true), "functional-missing-skuId", DeadLetterReasons.MissingSkuId));
        if (includeUnknownItem)
        {
            var unknownItem = ReferenceDataKeys.ItemCount + _random.NextInt64(1, 1_000_000);
            set.Add(new(BuildXml(unknownItem, NextMatchedSku()), "functional-unknown-itemId", DeadLetterReasons.ItemNotFound));
        }
        return set;
    }

    private long NextItemId() => _random.NextInt64(1, ReferenceDataKeys.ItemCount + 1);

    private string NextMatchedSku() => ReferenceDataKeys.SkuId(_random.Next(1, ReferenceDataKeys.SkuCount + 1));

    private string BuildMalformedXml()
    {
        // Well-formed start, then cut off mid-element so the body is not XML.
        var full = BuildXml(NextItemId(), NextMatchedSku());
        return full[..(full.IndexOf("<storeId>", StringComparison.Ordinal) + 12)];
    }

    private string BuildXml(long itemId, string skuId, bool omitItemId = false, bool omitSkuId = false)
    {
        var r = _random;
        var quantity = r.Next(1, 9);
        var unitPrice = r.Next(99, 49_999) / 100m;
        var discount = r.Next(0, 4) == 0 ? Math.Round(unitPrice * quantity * r.Next(5, 31) / 100m, 2) : 0m;
        var lineTotal = unitPrice * quantity - discount;
        var taxRate = 0.10m;
        var department = r.Next(Departments.Length);

        var b = _builder.Clear();
        b.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<transaction>\n");
        Field(b, "transactionId", NextGuid().ToString());
        if (!omitItemId) Field(b, "itemId", itemId.ToString(CultureInfo.InvariantCulture));
        if (!omitSkuId) Field(b, "skuId", skuId);
        Field(b, "storeId", "STORE-" + r.Next(1, 500).ToString("D4", CultureInfo.InvariantCulture));
        Field(b, "registerId", "REG-" + r.Next(1, 20).ToString("D2", CultureInfo.InvariantCulture));
        Field(b, "cashierId", "EMP-" + r.Next(1000, 9999).ToString(CultureInfo.InvariantCulture));
        Field(b, "transactionType", Pick(TransactionTypes));
        Field(b, "transactionTimestamp", BaseTime.AddSeconds(r.Next(0, 300 * 24 * 3600)).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        Field(b, "quantity", quantity.ToString(CultureInfo.InvariantCulture));
        Field(b, "unitPrice", Money(unitPrice));
        Field(b, "lineTotal", Money(lineTotal));
        Field(b, "discountAmount", Money(discount));
        Field(b, "taxAmount", Money(Math.Round(lineTotal * taxRate / (1 + taxRate), 2)));
        Field(b, "taxRate", taxRate.ToString("0.00", CultureInfo.InvariantCulture));
        Field(b, "currency", Pick(Currencies));
        Field(b, "paymentMethod", Pick(PaymentMethods));
        Field(b, "cardLast4", r.Next(0, 10_000).ToString("D4", CultureInfo.InvariantCulture));
        Field(b, "customerId", "CUST-" + r.Next(100_000, 999_999).ToString(CultureInfo.InvariantCulture));
        Field(b, "loyaltyNumber", "LY-" + r.NextInt64(1_000_000_000, 9_999_999_999).ToString(CultureInfo.InvariantCulture));
        Field(b, "channel", Pick(Channels));
        Field(b, "deviceId", "DEV-" + r.Next(0x100000, 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture));
        Field(b, "batchId", "BATCH-2026" + r.Next(1, 13).ToString("D2", CultureInfo.InvariantCulture) + r.Next(1, 29).ToString("D2", CultureInfo.InvariantCulture) + "-" + r.Next(1, 100).ToString("D2", CultureInfo.InvariantCulture));
        Field(b, "sequenceNumber", r.NextInt64(1, 100_000_000).ToString(CultureInfo.InvariantCulture));
        Field(b, "isRefund", r.Next(0, 20) == 0 ? "true" : "false");
        Field(b, "isTaxExempt", r.Next(0, 50) == 0 ? "true" : "false");
        Field(b, "isManualEntry", r.Next(0, 10) == 0 ? "true" : "false");
        Field(b, "department", Departments[department]);
        Field(b, "category", Categories[department]);
        Field(b, "brand", Pick(Brands));
        Field(b, "description", Phrase(4, 8));
        Field(b, "unitOfMeasure", Pick(Units));
        Field(b, "weightKg", (r.Next(10, 20_000) / 1000m).ToString("0.000", CultureInfo.InvariantCulture));
        Field(b, "promotionCode", "PROMO" + r.Next(1, 50).ToString(CultureInfo.InvariantCulture));
        Field(b, "couponCode", "CPN-" + r.Next(1, 9999).ToString("D4", CultureInfo.InvariantCulture));
        Field(b, "shippingMethod", Pick(Shipping));
        Field(b, "warehouseCode", Pick(Warehouses));
        Field(b, "supplierCode", "SUP-" + r.Next(1000, 9999).ToString(CultureInfo.InvariantCulture));
        Field(b, "sourceSystem", "POS");
        Field(b, "schemaVersion", "1.0");

        // Free-text notes carry the filler that brings the body to its (random) target size.
        const string notesOpen = "  <notes>";
        const string notesClose = "</notes>\n</transaction>";
        var target = r.Next(MinBodyBytes, MaxBodyBytes + 1);
        var notesLength = Math.Max(1, target - b.Length - notesOpen.Length - notesClose.Length);
        b.Append(notesOpen);
        AppendFiller(b, notesLength);
        b.Append(notesClose);
        return b.ToString();
    }

    private static void Field(StringBuilder b, string name, string value) =>
        b.Append("  <").Append(name).Append('>').Append(value).Append("</").Append(name).Append(">\n");

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private string Pick(string[] values) => values[_random.Next(values.Length)];

    private Guid NextGuid()
    {
        Span<byte> bytes = stackalloc byte[16];
        _random.NextBytes(bytes);
        return new Guid(bytes);
    }

    private string Phrase(int minWords, int maxWords)
    {
        var count = _random.Next(minWords, maxWords + 1);
        var sb = new StringBuilder();
        for (var i = 0; i < count; i++)
            sb.Append(i == 0 ? "" : " ").Append(Words[_random.Next(Words.Length)]);
        return sb.ToString();
    }

    /// <summary>Appends exactly <paramref name="length"/> ASCII characters of plain words (nothing that needs XML escaping).</summary>
    private void AppendFiller(StringBuilder b, int length)
    {
        var end = b.Length + length;
        while (b.Length < end)
        {
            var word = Words[_random.Next(Words.Length)];
            var remaining = end - b.Length;
            if (word.Length + 1 <= remaining) b.Append(word).Append(' ');
            else b.Append('x', remaining);
        }
    }
}
