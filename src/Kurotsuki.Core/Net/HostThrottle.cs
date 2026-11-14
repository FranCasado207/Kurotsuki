namespace Kurotsuki.Core.Net;

/// <summary>Spaces out requests to the same host so bulk work does not hammer a booru and trigger its rate limit.</summary>
public sealed class HostThrottle(TimeSpan interval, Func<long>? clock = null)
{
    private readonly long _intervalTicks = Math.Max(0, interval.Ticks);
    private readonly Func<long> _clock = clock ?? (() => DateTime.UtcNow.Ticks);
    private readonly Dictionary<string, long> _nextFree = [];

    /// <summary>Books the next slot for the host and returns how long the caller must wait before using it.</summary>
    public TimeSpan Reserve(string host)
    {
        lock (_nextFree)
        {
            var now = _clock();
            var start = Math.Max(now, _nextFree.GetValueOrDefault(host, now));
            _nextFree[host] = start + _intervalTicks;
            return TimeSpan.FromTicks(start - now);
        }
    }

    public async Task AcquireAsync(string? host, CancellationToken ct = default)
    {
        var wait = Reserve(host ?? "");
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, ct).ConfigureAwait(false);
        }
    }
}
