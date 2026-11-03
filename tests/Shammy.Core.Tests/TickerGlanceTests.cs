using Shammy.Core.Ambient;

namespace Shammy.Core.Tests;

public class TickerGlanceTests
{
    static readonly DateTime T0 = new(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);

    static readonly TickerItem[] Lines =
    {
        new(TickerKind.Stocks, "NIFTY 24,512 ▲0.4%"),
        new(TickerKind.Stocks, "SENSEX 80,101 ▼0.2%"),
        new(TickerKind.Crypto, "BTC $97,000"),
    };

    [Fact]
    public void Steps_through_the_lines_by_the_clock_not_by_how_often_it_is_asked()
    {
        var first = TickerGlance.Pick(Lines, T0);
        // Asked again and again within one step: the same line.
        Assert.Equal(first, TickerGlance.Pick(Lines, T0.AddSeconds(1)));
        Assert.Equal(first, TickerGlance.Pick(Lines, T0.AddSeconds(1)));
        // Over three steps every line shows once.
        var shown = Enumerable.Range(0, 3).Select(i => TickerGlance.Pick(Lines, T0 + TickerGlance.Step * i)).ToHashSet();
        Assert.Equal(3, shown.Count);
    }

    [Fact]
    public void Nothing_to_show_means_no_glance()
    {
        Assert.Null(TickerGlance.Pick(Array.Empty<TickerItem>(), T0));
    }

    [Fact]
    public void A_live_match_lifts_the_ticker_above_the_weather()
    {
        Assert.Equal(TickerGlance.Priority, TickerGlance.PriorityOf(Lines));
        var live = Lines.Append(new TickerItem(TickerKind.Cricket, "IND 287/4 v AUS", Live: true)).ToList();
        Assert.Equal(TickerGlance.LivePriority, TickerGlance.PriorityOf(live));
        Assert.True(TickerGlance.Priority < 10 && TickerGlance.LivePriority > 10);
    }

    [Theory]
    [InlineData("NIFTY 24,512 ▲0.4%", TickerTrend.Up)]
    [InlineData("SENSEX 80,101 ▼0.2%", TickerTrend.Down)]
    [InlineData("IND 287/4 v AUS", TickerTrend.Flat)]
    public void Reads_the_trend_for_the_dot(string text, TickerTrend trend) => Assert.Equal(trend, TickerGlance.Trend(text));
}
