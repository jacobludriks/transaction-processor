using System.Globalization;

namespace TransactionProcessor;

/// <summary>
/// The key scheme for the seeded reference data. The data seeder creates these exact keys and the load generator
/// draws from them, so both must use this class and never invent their own formats.
/// Items: ItemId 1..ItemCount, ExternalItemId "EXT-{ItemId}".
/// SKUs that exist: "SKU-" plus a 7-digit zero-padded number in 1..SkuCount.
/// SKUs that do not exist: the same format using a number in UnknownSkuFirst..UnknownSkuLast, which is never seeded.
/// </summary>
public static class ReferenceDataKeys
{
    public const int ItemCount = 1_000_000;
    public const int SkuCount = 1_000_000;
    public const int UnknownSkuFirst = 5_000_001;
    public const int UnknownSkuLast = 9_999_999;

    public static string ExternalItemId(long itemId) => "EXT-" + itemId.ToString(CultureInfo.InvariantCulture);

    public static string SkuId(int number) => "SKU-" + number.ToString("D7", CultureInfo.InvariantCulture);

    public static bool IsSeededSku(int number) => number is >= 1 and <= SkuCount;
}
