namespace LoadGenerator.Tests;

/// <summary>
/// A virtual clock: delays return immediately and advance virtual time by the requested amount plus a
/// deterministic overshoot (to imitate a timer that wakes late). Safe to call from several tasks.
/// </summary>
public sealed class FakeClock(TimeSpan overshootStep = default, int overshootCycle = 1) : IClock
{
    private readonly object _gate = new();
    private TimeSpan _now;
    private int _delays;

    public TimeSpan Elapsed { get { lock (_gate) return _now; } }

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            // Overshoot cycles 0, step, 2*step, ... so wake-ups are irregular.
            _now += delay + overshootStep * (_delays++ % overshootCycle);
        }
        return Task.CompletedTask;
    }
}
