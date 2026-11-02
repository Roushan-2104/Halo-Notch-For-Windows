using System.Net.Http;
using System.Net.NetworkInformation;
using System.Windows.Threading;
using Halo.Core.Ambient;

namespace Halo.Modules.Ambient;

/// <summary>
/// Approximate location from the IP (cached a day), an Open-Meteo forecast every 20 minutes, the
/// Weather tab, and a one-off "rain likely" nudge. Pauses while offline and resumes when the network
/// comes back.
/// </summary>
internal sealed class WeatherFeature : IDisposable
{
    const string TabGlyph = "\uE706";

    readonly ModuleContext _ctx;
    readonly AmbientData _data;
    readonly Action _save;
    readonly WeatherPanel _panel = new();
    readonly DispatcherTimer _timer;
    WeatherReport? _report;
    DateTime? _updatedUtc;
    int _failures;
    string? _lastError;
    Task? _inFlight;

    public WeatherFeature(ModuleContext ctx, AmbientData data, Action save)
    {
        _ctx = ctx;
        _data = data;
        _save = save;
        _timer = new DispatcherTimer(DispatcherPriority.Background, ctx.Ui);
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            _ = RefreshAsync();
        };
    }

    public string State => _report is null
        ? $"no data (failures={_failures}{(_lastError is null ? "" : ", last error: " + _lastError)})"
        : $"{WeatherText.Now(_report, City)} (updated {AmbientNet.Ago(_updatedUtc)}, next in {(_timer.IsEnabled ? $"{_timer.Interval.TotalMinutes:0.#}m" : "-")})";

    string City => _data.Location?.City ?? "your area";

    public void Start()
    {
        _ctx.AddTab(TabGlyph, "Weather", _panel);
        _panel.ShowStatus("Finding the weather\u2026");
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
        // Let startup settle before the first request.
        Schedule(TimeSpan.FromSeconds(4));
    }

    void OnNetworkChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (!e.IsAvailable) return;
        _ctx.Ui.BeginInvoke(() =>
        {
            // Back online: refresh soon if the forecast is stale or never arrived.
            if (_report is null || _updatedUtc is null || DateTime.UtcNow - _updatedUtc > RefreshSchedule.Weather) Schedule(TimeSpan.FromSeconds(3));
        });
    }

    void Schedule(TimeSpan delay)
    {
        _timer.Stop();
        _timer.Interval = delay;
        _timer.Start();
    }

    public Task RefreshAsync() => _inFlight is { IsCompleted: false } ? _inFlight : _inFlight = RunRefreshAsync();

    async Task RunRefreshAsync()
    {
        if (!AmbientNet.IsOnline)
        {
            // Nothing to do until the network returns (OnNetworkChanged); check again later anyway.
            _lastError = "offline";
            if (_report is null) _panel.ShowStatus("Weather will appear when you\u2019re back online");
            Schedule(TimeSpan.FromMinutes(10));
            return;
        }
        var location = _data.Location;
        var result = await Task.Run(() => FetchAsync(_ctx.Http, location));
        Apply(result);
    }

    sealed record FetchResult(GeoLocation? Location, WeatherReport? Report, string? Error);

    static async Task<FetchResult> FetchAsync(HttpClient http, GeoLocation? cached)
    {
        var location = cached is not null && cached.IsFresh(DateTime.UtcNow) ? cached : await LocateAsync(http).ConfigureAwait(false) ?? cached;
        if (location is null) return new FetchResult(null, null, "location unavailable");
        try
        {
            var json = await AmbientNet.GetAsync(http, OpenMeteo.ForecastUrl(location.Latitude, location.Longitude)).ConfigureAwait(false);
            var report = OpenMeteo.Parse(json);
            return new FetchResult(location, report, report is null ? "unexpected forecast response" : null);
        }
        catch (Exception ex)
        {
            return new FetchResult(location, null, ex.Message);
        }
    }

    static async Task<GeoLocation?> LocateAsync(HttpClient http)
    {
        foreach (var (url, parse) in new (string, Func<string, DateTime, GeoLocation?>)[]
                 {
                     (IpLocation.PrimaryUrl, IpLocation.ParseIpApiCo),
                     (IpLocation.FallbackUrl, IpLocation.ParseIpApiCom),
                 })
        {
            try
            {
                if (parse(await AmbientNet.GetAsync(http, url).ConfigureAwait(false), DateTime.UtcNow) is { } loc) return loc;
            }
            catch (Exception)
            {
                // Rate limited or down; try the next one.
            }
        }
        return null;
    }

    void Apply(FetchResult result)
    {
        if (result.Location is { } loc && !Equals(loc, _data.Location))
        {
            _data.Location = loc;
            _save();
        }
        if (result.Report is not { } report)
        {
            _failures++;
            _lastError = result.Error;
            _ctx.Warn($"weather refresh failed ({_failures}): {result.Error}");
            if (_report is null) _panel.ShowStatus("Weather unavailable right now \u00B7 retrying");
            Schedule(RefreshSchedule.WeatherAfterFailure(_failures));
            return;
        }

        var previous = _report;
        _report = report;
        _updatedUtc = DateTime.UtcNow;
        _failures = 0;
        _lastError = null;
        _panel.Show(report, City, DateTime.UtcNow);
        Schedule(RefreshSchedule.Weather);

        if (RainAlert.Check(previous, report, DateTime.UtcNow, _data.LastRainAlertUtc) is { } minutes)
        {
            _data.LastRainAlertUtc = DateTime.UtcNow;
            _save();
            _ctx.ShowMessage(WeatherCodes.CloudGlyph, RainAlert.Message(minutes), seconds: 5);
        }
    }

    /// <summary>For the test pipe: refresh now and return a one-line summary.</summary>
    public string RefreshNowBlocking()
    {
        if (!AmbientNet.IsOnline) return "error: offline";
        var location = _data.Location;
        var result = AmbientNet.Block(() => FetchAsync(_ctx.Http, location), TimeSpan.FromSeconds(40), new FetchResult(null, null, "timed out"));
        Apply(result);
        if (result.Report is not { } r) return "error: " + result.Error;
        var hours = r.NextHours(r.LocalNow(DateTime.UtcNow), 6)
            .Select(h => $"{WeatherText.HourLabel(h.Time)} {WeatherText.Temp(h.Temperature)} {h.RainChance?.ToString() ?? "-"}%");
        return $"{WeatherText.Now(r, City)} | next: {string.Join(", ", hours)}";
    }

    public async Task<CommandResult> RunCommandAsync(WeatherQuery query)
    {
        if (_report is null || _updatedUtc is null || DateTime.UtcNow - _updatedUtc > TimeSpan.FromMinutes(30)) await RefreshAsync();
        if (_report is not { } r)
            return new CommandResult(WeatherCodes.CloudGlyph, "Weather unavailable",
                AmbientNet.IsOnline ? "Couldn't reach the forecast; trying again soon" : "You're offline", IsError: true);

        OpenTabSoon();
        if (query == WeatherQuery.Tomorrow && WeatherText.Tomorrow(r) is { } tomorrow)
            return new CommandResult(WeatherCodes.Glyph(r.TomorrowCode ?? r.Code), "Tomorrow in " + City, tomorrow);

        return new CommandResult(WeatherCodes.Glyph(r.Code, r.IsDay), $"{WeatherText.Temp(r.Temperature)} {WeatherCodes.Describe(r.Code)}",
            $"{City} \u00B7 feels {WeatherText.Temp(r.FeelsLike)} \u00B7 H {WeatherText.Temp(r.TodayHigh)} L {WeatherText.Temp(r.TodayLow)}");
    }

    /// <summary>
    /// The command card owns the notch for ~2.6 s, so the tab opens once it has gone. An unheld tab
    /// collapses as soon as the pointer is elsewhere, so hold it and let go after a few seconds.
    /// </summary>
    void OpenTabSoon()
    {
        var opened = DateTime.UtcNow;
        var timer = new DispatcherTimer(DispatcherPriority.Normal, _ctx.Ui) { Interval = TimeSpan.FromMilliseconds(2900) };
        bool open = false;
        timer.Tick += (_, _) =>
        {
            if (!open)
            {
                open = true;
                _ctx.OpenTab(_panel, hold: true);
                timer.Interval = TimeSpan.FromSeconds(1);
                return;
            }
            if (_ctx.Notch.IsMouseOver && DateTime.UtcNow - opened < TimeSpan.FromSeconds(60)) return;
            if (DateTime.UtcNow - opened < TimeSpan.FromSeconds(9)) return;
            timer.Stop();
            _ctx.Notch.Collapse();
        };
        timer.Start();
    }

    public void Dispose()
    {
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        _timer.Stop();
    }
}
