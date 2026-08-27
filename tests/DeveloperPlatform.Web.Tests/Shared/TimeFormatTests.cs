using DeveloperPlatform.Web.Components.Shared;

namespace DeveloperPlatform.Web.Tests.Shared;

public class TimeFormatTests
{
    private static readonly DateTime Now = new(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(30, "just now")]
    [InlineData(60, "1m ago")]
    [InlineData(5 * 60, "5m ago")]
    [InlineData(60 * 60, "1h ago")]
    [InlineData(3 * 60 * 60, "3h ago")]
    [InlineData(24 * 60 * 60, "1d ago")]
    [InlineData(10 * 24 * 60 * 60, "10d ago")]
    public void Formats_Recent_Spans(int secondsAgo, string expected)
    {
        Assert.Equal(expected, TimeFormat.Relative(Now.AddSeconds(-secondsAgo), Now));
    }

    [Fact]
    public void Formats_Months_And_Years()
    {
        Assert.Equal("2mo ago", TimeFormat.Relative(Now.AddDays(-60), Now));
        Assert.Equal("1y ago", TimeFormat.Relative(Now.AddDays(-400), Now));
    }

    [Fact]
    public void Clamps_Future_To_Just_Now()
    {
        Assert.Equal("just now", TimeFormat.Relative(Now.AddMinutes(5), Now));
    }
}
