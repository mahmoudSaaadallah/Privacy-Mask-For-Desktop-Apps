namespace PrivacyMask.Rendering.Benchmark;

// Fixed storage, no per-frame allocation. P95 is an upper bucket bound, not
// input-to-display latency. The last bucket reports its overflow explicitly.
internal sealed class TimingHistogram
{
    private readonly long[] buckets = new long[2001];
    private long count;
    private double total;
    private double maximum;

    public void Add(double milliseconds)
    {
        if (!double.IsFinite(milliseconds) || milliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(milliseconds));
        buckets[(int)Math.Min(2000, Math.Ceiling(milliseconds))]++;
        count++;
        total += milliseconds;
        maximum = Math.Max(maximum, milliseconds);
    }

    public void Reset()
    {
        Array.Clear(buckets);
        count = 0;
        total = maximum = 0;
    }

    public TimingSnapshot Snapshot()
    {
        if (count == 0) return new(0, null, null, null, false);
        var rank = (long)Math.Ceiling(count * 0.95);
        long seen = 0;
        for (var i = 0; i < buckets.Length; i++)
        {
            seen += buckets[i];
            if (seen >= rank)
                return new(count, total / count, maximum, i == 2000 ? null : i, i == 2000);
        }
        throw new InvalidOperationException("Missing timing bucket.");
    }
}

internal sealed record TimingSnapshot(long Count, double? MeanMilliseconds,
    double? MaximumMilliseconds, int? P95UpperBoundMilliseconds, bool P95Overflow);
