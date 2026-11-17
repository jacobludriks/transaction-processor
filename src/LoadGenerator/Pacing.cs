using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace LoadGenerator;

/// <summary>A time source that can also wait. Injected so pacing can be tested on a virtual clock.</summary>
public interface IClock
{
    /// <summary>Time since the clock started. Monotonic.</summary>
    TimeSpan Elapsed { get; }

    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default);
}

public sealed class SystemClock : IClock
{
    private readonly long _start = Stopwatch.GetTimestamp();

    public TimeSpan Elapsed => Stopwatch.GetElapsedTime(_start);

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default) => Task.Delay(delay, cancellationToken);
}

/// <summary>A batch of messages released by the pacer: how many, and when (clock elapsed) they were released.</summary>
public readonly record struct PacedBatch(TimeSpan At, int Count);

public static class Pacer
{
    /// <summary>
    /// Releases exactly rate x duration messages, in batches of at most <paramref name="maxBatch"/>.
    /// The schedule is absolute: at time t the cumulative count released is floor(rate x t), so a late wake-up
    /// is made up on the next one and the rate does not drift, whatever the timer granularity.
    /// </summary>
    public static async IAsyncEnumerable<PacedBatch> BatchesAsync(
        double rate, TimeSpan duration, IClock clock, TimeSpan tick, int maxBatch,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(rate, 0.0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxBatch, 0);
        var total = (long)Math.Round(rate * duration.TotalSeconds);
        var start = clock.Elapsed;
        long released = 0;

        while (released < total)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var elapsed = clock.Elapsed - start;
            var due = Math.Min(total, (long)Math.Floor(rate * elapsed.TotalSeconds + 1e-9));
            while (released < due)
            {
                var count = (int)Math.Min(maxBatch, due - released);
                released += count;
                yield return new PacedBatch(elapsed, count);
            }
            if (released < total)
            {
                // Sleep to the next tick, or less if the next message falls due sooner.
                // Rounded up to a whole clock tick so that waking at nextDue always finds the message due.
                var nextDue = TimeSpan.FromTicks((long)Math.Ceiling((released + 1) / rate * TimeSpan.TicksPerSecond));
                var wait = nextDue - (clock.Elapsed - start);
                if (wait > tick) wait = tick;
                if (wait < TimeSpan.FromTicks(1)) wait = TimeSpan.FromTicks(1);
                await clock.DelayAsync(wait, cancellationToken);
            }
        }
    }
}
