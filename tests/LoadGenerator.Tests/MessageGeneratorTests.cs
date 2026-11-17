using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using TransactionProcessor;

namespace LoadGenerator.Tests;

/// <summary>Load-run messages, observed the way the processor sees them: raw XML bodies decoded with its contract.</summary>
public sealed class MessageGeneratorTests
{
    private static readonly XmlSerializer Serializer = new(typeof(TransactionMessage));

    private static TransactionMessage Decode(string body)
    {
        using var reader = XmlReader.Create(new StringReader(body), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        return (TransactionMessage)Serializer.Deserialize(reader)!;
    }

    private static List<GeneratedMessage> Sample(int count, int seed = 42, double unmatchedRatio = 0.10)
    {
        var generator = new MessageGenerator(seed, unmatchedRatio);
        return Enumerable.Range(0, count).Select(_ => generator.NextLoadMessage()).ToList();
    }

    [Fact]
    public void Load_messages_decode_with_the_handlers_contract_and_populate_every_field()
    {
        foreach (var message in Sample(200))
        {
            var decoded = Decode(message.Body);
            var empty = typeof(TransactionMessage).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetValue(decoded) is null or "")
                .Select(p => p.Name)
                .ToList();
            Assert.Empty(empty);
        }
        Assert.True(typeof(TransactionMessage).GetProperties().Length >= 40);
    }

    [Fact]
    public void Load_messages_are_between_4_and_6_KB()
    {
        var sizes = Sample(2_000).Select(m => Encoding.UTF8.GetByteCount(m.Body)).ToList();

        Assert.All(sizes, size => Assert.InRange(size, 4 * 1024, 6 * 1024));
        // Sizes vary rather than all sitting on one value.
        Assert.True(sizes.Max() - sizes.Min() > 500);
    }

    [Fact]
    public void Load_messages_never_omit_the_item_id_or_sku_id_and_item_ids_are_within_the_seeded_range()
    {
        foreach (var message in Sample(5_000))
        {
            var decoded = Decode(message.Body);
            Assert.NotNull(decoded.ItemId);
            Assert.InRange(decoded.ItemId!.Value, 1, ReferenceDataKeys.ItemCount);
            Assert.False(string.IsNullOrWhiteSpace(decoded.SkuId));
            Assert.Null(message.ExpectedDeadLetterReason);
        }
    }

    [Fact]
    public void Item_ids_are_spread_evenly_across_the_whole_range()
    {
        var itemIds = Sample(50_000).Select(m => Decode(m.Body).ItemId!.Value).ToList();

        // Ten equal-width bands of the range each hold about a tenth of the sample (expected 5,000, sd ~67).
        var bands = itemIds.GroupBy(id => (id - 1) * 10 / ReferenceDataKeys.ItemCount).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(10, bands.Count);
        Assert.All(bands.Values, count => Assert.InRange(count, 4_600, 5_400));
        Assert.True(itemIds.Min() < 1_000 && itemIds.Max() > ReferenceDataKeys.ItemCount - 1_000);
    }

    [Fact]
    public void Matched_and_unmatched_skus_use_the_seeders_key_scheme_and_mix_at_90_10_by_default()
    {
        var skus = Sample(100_000).Select(m => Decode(m.Body).SkuId!).ToList();

        var numbers = skus.Select(sku =>
        {
            Assert.Matches("^SKU-[0-9]{7}$", sku);
            return int.Parse(sku["SKU-".Length..]);
        }).ToList();
        var unmatched = numbers.Count(n => !ReferenceDataKeys.IsSeededSku(n));

        Assert.All(numbers.Where(n => !ReferenceDataKeys.IsSeededSku(n)),
            n => Assert.InRange(n, ReferenceDataKeys.UnknownSkuFirst, ReferenceDataKeys.UnknownSkuLast));
        Assert.InRange(unmatched / (double)numbers.Count, 0.095, 0.105);
    }

    [Fact]
    public void Matched_skus_are_spread_evenly_across_the_seeded_range()
    {
        var matched = Sample(50_000, unmatchedRatio: 0)
            .Select(m => int.Parse(Decode(m.Body).SkuId!["SKU-".Length..])).ToList();

        Assert.All(matched, n => Assert.True(ReferenceDataKeys.IsSeededSku(n)));
        var bands = matched.GroupBy(n => (n - 1) * 10L / ReferenceDataKeys.SkuCount).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(10, bands.Count);
        Assert.All(bands.Values, count => Assert.InRange(count, 4_600, 5_400));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void The_unmatched_ratio_is_configurable(double ratio)
    {
        var numbers = Sample(40_000, unmatchedRatio: ratio)
            .Select(m => int.Parse(Decode(m.Body).SkuId!["SKU-".Length..])).ToList();

        var unmatched = numbers.Count(n => !ReferenceDataKeys.IsSeededSku(n)) / (double)numbers.Count;
        Assert.InRange(unmatched, ratio - 0.01, ratio + 0.01);
    }

    [Fact]
    public void The_same_seed_produces_the_same_messages_and_different_seeds_differ()
    {
        Assert.Equal(Sample(50, seed: 7).Select(m => m.Body), Sample(50, seed: 7).Select(m => m.Body));
        Assert.NotEqual(Sample(50, seed: 7).Select(m => m.Body), Sample(50, seed: 8).Select(m => m.Body));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void An_unmatched_ratio_outside_0_to_1_is_rejected(double ratio) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new MessageGenerator(1, ratio));
}
