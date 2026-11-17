using System.Globalization;
using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace TransactionProcessor;

/// <summary>
/// The incoming XML transaction message (about 40 fields). Every field is optional at the contract level;
/// whether a missing key is acceptable is a handler decision. Decoded with the standard XML serializer, no XSD.
/// </summary>
[XmlRoot("transaction")]
public sealed class TransactionMessage
{
    [XmlElement("transactionId")] public string? TransactionId { get; set; }

    /// <summary>The raw <c>itemId</c> element text. Decoded as text so that an empty element is "missing" rather than a conversion error.</summary>
    [XmlElement("itemId"), JsonIgnore] public string? ItemIdText { get; set; }

    /// <summary>The numeric item key: null when the element is absent, blank or not a number. Serialised to JSON as <c>itemId</c>.</summary>
    [XmlIgnore] public long? ItemId => long.TryParse(ItemIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;

    [XmlElement("skuId")] public string? SkuId { get; set; }
    [XmlElement("storeId")] public string? StoreId { get; set; }
    [XmlElement("registerId")] public string? RegisterId { get; set; }
    [XmlElement("cashierId")] public string? CashierId { get; set; }
    [XmlElement("transactionType")] public string? TransactionType { get; set; }
    [XmlElement("transactionTimestamp")] public DateTime? TransactionTimestamp { get; set; }
    [XmlElement("quantity")] public int? Quantity { get; set; }
    [XmlElement("unitPrice")] public decimal? UnitPrice { get; set; }
    [XmlElement("lineTotal")] public decimal? LineTotal { get; set; }
    [XmlElement("discountAmount")] public decimal? DiscountAmount { get; set; }
    [XmlElement("taxAmount")] public decimal? TaxAmount { get; set; }
    [XmlElement("taxRate")] public decimal? TaxRate { get; set; }
    [XmlElement("currency")] public string? Currency { get; set; }
    [XmlElement("paymentMethod")] public string? PaymentMethod { get; set; }
    [XmlElement("cardLast4")] public string? CardLast4 { get; set; }
    [XmlElement("customerId")] public string? CustomerId { get; set; }
    [XmlElement("loyaltyNumber")] public string? LoyaltyNumber { get; set; }
    [XmlElement("channel")] public string? Channel { get; set; }
    [XmlElement("deviceId")] public string? DeviceId { get; set; }
    [XmlElement("batchId")] public string? BatchId { get; set; }
    [XmlElement("sequenceNumber")] public long? SequenceNumber { get; set; }
    [XmlElement("isRefund")] public bool? IsRefund { get; set; }
    [XmlElement("isTaxExempt")] public bool? IsTaxExempt { get; set; }
    [XmlElement("isManualEntry")] public bool? IsManualEntry { get; set; }
    [XmlElement("department")] public string? Department { get; set; }
    [XmlElement("category")] public string? Category { get; set; }
    [XmlElement("brand")] public string? Brand { get; set; }
    [XmlElement("description")] public string? Description { get; set; }
    [XmlElement("unitOfMeasure")] public string? UnitOfMeasure { get; set; }
    [XmlElement("weightKg")] public decimal? WeightKg { get; set; }
    [XmlElement("promotionCode")] public string? PromotionCode { get; set; }
    [XmlElement("couponCode")] public string? CouponCode { get; set; }
    [XmlElement("shippingMethod")] public string? ShippingMethod { get; set; }
    [XmlElement("warehouseCode")] public string? WarehouseCode { get; set; }
    [XmlElement("supplierCode")] public string? SupplierCode { get; set; }
    [XmlElement("notes")] public string? Notes { get; set; }
    [XmlElement("sourceSystem")] public string? SourceSystem { get; set; }
    [XmlElement("schemaVersion")] public string? SchemaVersion { get; set; }
}
