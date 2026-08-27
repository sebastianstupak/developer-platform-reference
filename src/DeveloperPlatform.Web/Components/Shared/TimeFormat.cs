namespace DeveloperPlatform.Web.Components.Shared;

public static class TimeFormat
{
    public static string Relative(DateTime utc) => Relative(utc, DateTime.UtcNow);

    public static string Relative(DateTime utc, DateTime now)
    {
        var span = now - utc;
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        if (span.TotalSeconds < 60)
        {
            return "just now";
        }

        if (span.TotalMinutes < 60)
        {
            return $"{(int)span.TotalMinutes}m ago";
        }

        if (span.TotalHours < 24)
        {
            return $"{(int)span.TotalHours}h ago";
        }

        if (span.TotalDays < 30)
        {
            return $"{(int)span.TotalDays}d ago";
        }

        if (span.TotalDays < 365)
        {
            return $"{(int)(span.TotalDays / 30)}mo ago";
        }

        return $"{(int)(span.TotalDays / 365)}y ago";
    }
}
