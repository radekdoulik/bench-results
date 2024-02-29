namespace Controller;

public static class TimeSpanExtensions
{
    public static TimeSpan RoundToNearestSeconds(this TimeSpan span)
    {
        return TimeSpan.FromSeconds(Math.Round(span.TotalSeconds));
    }
}
