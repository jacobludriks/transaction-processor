using System.Globalization;

namespace RunReport;

/// <summary>
/// A latency distribution from per-bucket counts (the bucket with upper bound <c>le</c> holds the messages that
/// took more than the previous bound and at most <c>le</c>; <c>+Inf</c> is the overflow bucket).
/// </summary>
internal sealed class LatencyHistogram
{
    private readonly List<(double Le, long Count)> _buckets;

    public long Total { get; }

    public LatencyHistogram(IEnumerable<(string Le, long Count)> buckets)
    {
        _buckets = buckets
            .GroupBy(b => ParseBound(b.Le), b => b.Count)
            .Select(g => (Le: g.Key, Count: g.Sum()))
            .OrderBy(b => b.Le)
            .ToList();
        Total = _buckets.Sum(b => b.Count);
    }

    /// <summary>
    /// The latency at quantile <paramref name="q"/> (0 to 1). <c>Estimate</c> interpolates linearly inside the
    /// bucket that holds it; <c>UpperBound</c> is that bucket's upper bound, null when it is the overflow bucket
    /// (in which case the estimate is the last finite bound). Null when there are no samples.
    /// </summary>
    public (double Estimate, double? UpperBound)? Percentile(double q)
    {
        if (Total == 0) return null;

        var rank = q * Total;
        long before = 0;
        var lower = 0.0;
        foreach (var (le, count) in _buckets)
        {
            var through = before + count;
            // The tolerance keeps a rank that is exactly on a bucket edge in that bucket despite floating point error.
            if (count > 0 && through >= rank - 1e-9)
            {
                if (double.IsPositiveInfinity(le)) return (lower, null);
                return (lower + (le - lower) * ((rank - before) / count), le);
            }
            before = through;
            if (!double.IsPositiveInfinity(le)) lower = le;
        }
        return (lower, null);
    }

    private static double ParseBound(string le) =>
        le.Equals("+Inf", StringComparison.OrdinalIgnoreCase) || le.Equals("Infinity", StringComparison.OrdinalIgnoreCase) || le.Equals("Inf", StringComparison.OrdinalIgnoreCase)
            ? double.PositiveInfinity
            : double.Parse(le, NumberStyles.Float, CultureInfo.InvariantCulture);
}
