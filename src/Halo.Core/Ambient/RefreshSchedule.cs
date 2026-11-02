namespace Halo.Core.Ambient;

/// <summary>How often each ambient source refreshes, and how it backs off when the network fails.</summary>
public static class RefreshSchedule
{
    public static readonly TimeSpan Weather = TimeSpan.FromMinutes(20);
    public static readonly TimeSpan CricketLive = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan CricketIdle = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan Stocks = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan Crypto = TimeSpan.FromSeconds(60);

    static readonly TimeSpan[] Steps =
    {
        TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(20),
    };

    /// <summary>The regular interval for a ticker; cricket only polls quickly while a match is live.</summary>
    public static TimeSpan Ticker(TickerKind kind, bool anyLive) => kind switch
    {
        TickerKind.Cricket => anyLive ? CricketLive : CricketIdle,
        TickerKind.Stocks => Stocks,
        _ => Crypto,
    };

    /// <summary>Delay after <paramref name="failures"/> consecutive failures: grows, never shorter than normal.</summary>
    public static TimeSpan AfterFailure(int failures, TimeSpan normal)
    {
        if (failures <= 0) return normal;
        var step = Steps[Math.Min(failures, Steps.Length) - 1];
        return step > normal ? step : normal;
    }

    /// <summary>Delay after failures for weather, where retrying sooner than 20 min is worth it.</summary>
    public static TimeSpan WeatherAfterFailure(int failures) =>
        failures <= 0 ? Weather : Steps[Math.Min(failures, Steps.Length) - 1];
}
