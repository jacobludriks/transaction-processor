using TransactionProcessor;

namespace TransactionProcessor.Tests;

public class ReferenceDataKeysTests
{
    [Fact]
    public void ExternalItemId_is_the_item_key_with_an_EXT_prefix()
    {
        Assert.Equal("EXT-48213077", ReferenceDataKeys.ExternalItemId(48213077));
    }

    [Fact]
    public void SkuId_is_zero_padded_to_seven_digits()
    {
        Assert.Equal("SKU-0000001", ReferenceDataKeys.SkuId(1));
        Assert.Equal("SKU-0007312", ReferenceDataKeys.SkuId(7312));
        Assert.Equal("SKU-1000000", ReferenceDataKeys.SkuId(1_000_000));
    }

    [Fact]
    public void Unknown_skus_never_fall_in_the_seeded_range_and_still_have_seven_digits()
    {
        Assert.False(ReferenceDataKeys.IsSeededSku(ReferenceDataKeys.UnknownSkuFirst));
        Assert.False(ReferenceDataKeys.IsSeededSku(ReferenceDataKeys.UnknownSkuLast));
        Assert.Equal("SKU-9999999", ReferenceDataKeys.SkuId(ReferenceDataKeys.UnknownSkuLast));
        Assert.True(ReferenceDataKeys.IsSeededSku(ReferenceDataKeys.SkuCount));
    }
}
