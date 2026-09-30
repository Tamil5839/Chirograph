namespace Chirograph.Application.Common;

internal static class Clock
{
    /// <summary>
    /// Current UTC time truncated to whole microseconds, the finest precision the supported databases store, so an
    /// entity's timestamps are the same before and after a round trip.
    /// </summary>
    public static DateTimeOffset UtcNow(this TimeProvider time)
    {
        var now = time.GetUtcNow();
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
    }
}
