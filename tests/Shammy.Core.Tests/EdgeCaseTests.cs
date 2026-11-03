using Shammy.Core.Ambient;
using Shammy.Core.Audio;
using Shammy.Core.Conversions;
using Shammy.Core.Launcher;
using Shammy.Core.Productivity;

namespace Shammy.Core.Tests;

/// <summary>Edge cases found in the full feature sweep.</summary>
public class MoneyEdgeTests
{
    [Theory]
    [InlineData("1.299 €", 1299, "EUR")]
    [InlineData("€1.299", 1299, "EUR")]
    [InlineData("1.299,99 €", 1299.99, "EUR")]
    [InlineData("12.500.000 ₹", 12500000, "INR")]
    [InlineData("$49.99", 49.99, "USD")]
    [InlineData("15,50 €", 15.5, "EUR")]
    [InlineData("$1,299.99", 1299.99, "USD")]
    [InlineData("rs.500", 500, "INR")]
    [InlineData("1.5 lakh rupees", 150000, "INR")]
    public void Prices_in_both_number_styles(string text, double amount, string code)
    {
        Assert.True(Currency.TryParseMoney(text, requireAmount: true, out var money), text);
        Assert.Equal(code, money.Code);
        Assert.Equal(amount, money.Amount, 6);
    }
}

public class SnippetClashTests
{
    static readonly string[] Existing = { ";date", ";time", ";shrug" };

    [Theory]
    [InlineData(";d", ";date")]
    [InlineData(";dates", ";date")]
    [InlineData(";s", ";shrug")]
    public void Prefix_triggers_clash(string trigger, string clash) => Assert.Equal(clash, SnippetText.Clash(trigger, Existing));

    [Theory]
    [InlineData(";date")]
    [InlineData(";DATE")]
    [InlineData(";sig")]
    [InlineData(";x")]
    public void Replacing_or_unrelated_triggers_dont_clash(string trigger) => Assert.Null(SnippetText.Clash(trigger, Existing));
}

public class PingTests
{
    [Theory]
    [InlineData(PingKind.Notification)]
    [InlineData(PingKind.Alert)]
    public void Ping_is_short_soft_and_click_free(PingKind kind)
    {
        var s = Ping.Render(kind);
        double seconds = (double)s.Length / Ping.SampleRate;
        Assert.InRange(seconds, 0.2, 1.0);
        // Well below full scale (it should be heard, not startle) and never clipped.
        Assert.InRange(s.Max(x => Math.Abs((int)x)), 8000, 12000);
        // Starts and ends at silence, so there's no click.
        Assert.True(Math.Abs((int)s[0]) < 200);
        Assert.True(Math.Abs((int)s[^1]) < 200);
    }
}

public class DaylightSavingTests
{
    /// <summary>Whatever the machine's time zone, "in 1 hour" is one real hour, including across DST changes.</summary>
    [Fact]
    public void Relative_reminders_are_real_elapsed_time_all_year()
    {
        var start = new DateTime(2026, 1, 1, 0, 30, 0, DateTimeKind.Local);
        for (var now = start; now < start.AddYears(1); now = now.AddHours(7))
        {
            var due = WhenParser.Parse("in 1 hour", now)!.Value;
            Assert.Equal(TimeSpan.FromHours(1), due.ToUniversalTime() - now.ToUniversalTime());
        }
    }
}

public class DaylightSavingZoneTests
{
    static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    [Fact]
    public void Relative_time_on_the_clocks_back_night_is_real_time()
    {
        // 1 Nov 2026: the clocks go back from 02:00 EDT to 01:00 EST. From 00:30, two real hours later the
        // wall clock reads 01:30 again (EST), not 02:30, which would be three real hours.
        var due = WhenParser.Ahead(new DateTime(2026, 11, 1, 0, 30, 0), TimeSpan.FromHours(2), Eastern);
        Assert.Equal(new DateTime(2026, 11, 1, 1, 30, 0), due);
    }

    [Fact]
    public void In_one_hour_on_the_clocks_forward_night_skips_the_missing_hour()
    {
        // 8 Mar 2026, 01:30 EST + 1 real hour = 03:30 EDT (02:xx doesn't exist that night).
        var due = WhenParser.Ahead(new DateTime(2026, 3, 8, 1, 30, 0), TimeSpan.FromHours(1), Eastern);
        Assert.Equal(new DateTime(2026, 3, 8, 3, 30, 0), due);
    }
}

public class TickerStalenessTests
{
    static readonly DateTime Now = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Fresh_data_is_shown() => Assert.False(RefreshSchedule.IsStale(TickerKind.Stocks, false, Now.AddMinutes(-2), Now));

    [Fact]
    public void Hours_old_data_is_hidden() => Assert.True(RefreshSchedule.IsStale(TickerKind.Crypto, false, Now.AddHours(-3), Now));

    [Fact]
    public void One_slow_refresh_doesnt_blank_a_live_score() =>
        Assert.False(RefreshSchedule.IsStale(TickerKind.Cricket, true, Now.AddMinutes(-5), Now));

    [Fact]
    public void Never_loaded_isnt_stale() => Assert.False(RefreshSchedule.IsStale(TickerKind.Cricket, false, null, Now));
}
