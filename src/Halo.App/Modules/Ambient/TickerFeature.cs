using System.Net.NetworkInformation;
using System.Windows.Threading;
using Halo.Core.Activities;
using Halo.Core.Ambient;

namespace Halo.Modules.Ambient;

/// <summary>
/// The customizable ticker: any mix of cricket, stocks and crypto rotating in the compact notch
/// below media (priority 15), so it shows only when nothing more important is going on. Each provider
/// polls on its own schedule and only while it is switched on.
/// </summary>
internal sealed class TickerFeature : IDisposable
{
    const string ActivityId = "ambient.ticker";
    const int Priority = 15;
    const string Glyph = "\uEAFC";

    readonly ModuleContext _ctx;
    readonly AmbientData _data;
    readonly Action _save;
    readonly TickerContent _content = new();
    readonly Dictionary<TickerKind, Source> _sources = new();

    sealed class Source(ITickerProvider provider, DispatcherTimer timer)
    {
        public ITickerProvider Provider { get; } = provider;
        public DispatcherTimer Timer { get; } = timer;
        public IReadOnlyList<TickerItem> Items { get; set; } = Array.Empty<TickerItem>();
        public int Failures { get; set; }
        public DateTime? UpdatedUtc { get; set; }
        public string? Error { get; set; }
        public CancellationTokenSource? Cts { get; set; }
    }

    public TickerFeature(ModuleContext ctx, AmbientData data, Action save)
    {
        _ctx = ctx;
        _data = data;
        _save = save;
        Add(new CricketProvider(() => _data.CricketTeam));
        Add(new StocksProvider(() => _data.StockSymbols.ToList()));
        Add(new CryptoProvider());
    }

    void Add(ITickerProvider provider)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background, _ctx.Ui);
        var source = new Source(provider, timer);
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _ = RefreshAsync(source);
        };
        _sources[provider.Kind] = source;
    }

    bool IsActive(TickerKind kind) => _data.TickerEnabled && _data.TickerKinds.Contains(kind);

    public void Start()
    {
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
        Apply();
    }

    void OnNetworkChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (!e.IsAvailable) return;
        _ctx.Ui.BeginInvoke(() =>
        {
            foreach (var s in _sources.Values.Where(s => IsActive(s.Provider.Kind) && s.Failures > 0))
                Schedule(s, TimeSpan.FromSeconds(2));
        });
    }

    public void SetEnabled(bool on)
    {
        if (on == _data.TickerEnabled) return; // the tray menu re-applies its state each time it opens
        _data.TickerEnabled = on;
        if (on && _data.TickerKinds.Count == 0) _data.TickerKinds.Add(TickerKind.Stocks);
        _save();
        Apply();
    }

    /// <summary>Start what should run, stop what shouldn't, and refresh the pill.</summary>
    void Apply()
    {
        foreach (var s in _sources.Values)
        {
            if (IsActive(s.Provider.Kind))
            {
                if (!s.Timer.IsEnabled && s.Cts is null) Schedule(s, TimeSpan.Zero);
            }
            else
            {
                s.Timer.Stop();
                s.Cts?.Cancel();
                s.Items = Array.Empty<TickerItem>();
                s.Failures = 0;
            }
        }
        UpdateDisplay();
    }

    void Schedule(Source s, TimeSpan delay)
    {
        s.Timer.Stop();
        s.Timer.Interval = delay;
        s.Timer.Start();
    }

    /// <summary>Refetch one source now (after its settings changed).</summary>
    void Restart(TickerKind kind)
    {
        var s = _sources[kind];
        s.Cts?.Cancel();
        s.Cts = null;
        s.Failures = 0;
        if (IsActive(kind)) Schedule(s, TimeSpan.Zero);
    }

    async Task RefreshAsync(Source s)
    {
        var kind = s.Provider.Kind;
        if (!IsActive(kind) || s.Cts is not null) return;
        if (!AmbientNet.IsOnline)
        {
            s.Error = "offline";
            Schedule(s, RefreshSchedule.AfterFailure(++s.Failures, RefreshSchedule.Ticker(kind, false)));
            return;
        }

        var cts = new CancellationTokenSource();
        s.Cts = cts;
        try
        {
            var items = await Task.Run(() => s.Provider.RefreshAsync(_ctx.Http, cts.Token));
            if (cts.IsCancellationRequested) return;
            s.Items = items;
            s.UpdatedUtc = DateTime.UtcNow;
            s.Failures = 0;
            s.Error = null;
        }
        catch (Exception) when (cts.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            s.Failures++;
            s.Error = ex.Message;
            _ctx.Warn($"{s.Provider.Name} ticker refresh failed ({s.Failures}): {ex.Message}");
        }
        finally
        {
            if (s.Cts == cts) s.Cts = null;
            cts.Dispose();
        }

        if (!IsActive(kind)) return;
        var normal = RefreshSchedule.Ticker(kind, s.Items.Any(i => i.Live));
        Schedule(s, s.Failures > 0 ? RefreshSchedule.AfterFailure(s.Failures, normal) : normal);
        UpdateDisplay();
    }

    void UpdateDisplay()
    {
        var items = _sources.Values
            .Where(s => IsActive(s.Provider.Kind))
            .OrderBy(s => s.Provider.Kind)
            .SelectMany(s => s.Items)
            .ToList();
        if (items.Count == 0)
        {
            // Nothing to say (off, still loading, or no live matches): give the notch back.
            _ctx.Remove(ActivityId);
            _content.SetItems(items);
            return;
        }
        _content.SetItems(items);
        if (_ctx.Get(ActivityId) is null) _ctx.Post(new Activity(ActivityId, ActivityKind.Custom, Priority, _content));
    }

    public CommandResult Run(TickerCommand cmd)
    {
        switch (cmd.Action)
        {
            case TickerAction.Status:
                return _data.TickerEnabled
                    ? new CommandResult(Glyph, "Ticker: " + Describe(), Detail())
                    : new CommandResult(Glyph, "Ticker is off", "Try \u201Cticker cricket\u201D or \u201Cticker stocks\u201D");
            case TickerAction.On:
                SetEnabled(true);
                return new CommandResult(Glyph, "Ticker on: " + Describe(), Detail());
            case TickerAction.Off:
                SetEnabled(false);
                return new CommandResult("\uE711", "Ticker off");
            case TickerAction.Enable:
                if (cmd.Kind == TickerKind.Cricket) _data.CricketTeam = cmd.Symbols?.FirstOrDefault();
                EnableKind(cmd.Kind!.Value);
                return new CommandResult(Glyph, "Ticker on: " + Describe(), Detail());
            case TickerAction.Disable:
                _data.TickerKinds.Remove(cmd.Kind!.Value);
                if (_data.TickerKinds.Count == 0) _data.TickerEnabled = false;
                _save();
                Apply();
                return new CommandResult("\uE711", $"{Title(cmd.Kind.Value)} removed from the ticker",
                    _data.TickerEnabled ? "Still showing " + Describe() : "Ticker off");
            case TickerAction.SetSymbols:
                _data.StockSymbols = cmd.Symbols!.Distinct().ToList();
                EnableKind(TickerKind.Stocks);
                return new CommandResult(Glyph, "Watching " + Labels(), "Ticker on: " + Describe());
            case TickerAction.AddSymbols:
                foreach (var sym in cmd.Symbols!)
                    if (!_data.StockSymbols.Contains(sym)) _data.StockSymbols.Add(sym);
                EnableKind(TickerKind.Stocks);
                return new CommandResult(Glyph, "Added " + string.Join(", ", cmd.Symbols!.Select(TickerFormat.SymbolLabel)), "Watching " + Labels());
            case TickerAction.RemoveSymbols:
                int removed = _data.StockSymbols.RemoveAll(s => cmd.Symbols!.Contains(s));
                if (removed == 0) return new CommandResult(Glyph, "Not in the ticker", "Watching " + Labels(), IsError: true);
                if (_data.StockSymbols.Count == 0) _data.TickerKinds.Remove(TickerKind.Stocks);
                if (_data.TickerKinds.Count == 0) _data.TickerEnabled = false;
                _save();
                Restart(TickerKind.Stocks);
                Apply();
                return new CommandResult(Glyph, "Removed " + string.Join(", ", cmd.Symbols!.Select(TickerFormat.SymbolLabel)),
                    _data.StockSymbols.Count > 0 ? "Watching " + Labels() : "No stocks left");
        }
        return new CommandResult("\uE783", "Unknown ticker command", IsError: true);
    }

    void EnableKind(TickerKind kind)
    {
        if (!_data.TickerKinds.Contains(kind)) _data.TickerKinds.Add(kind);
        if (kind == TickerKind.Stocks && _data.StockSymbols.Count == 0) _data.StockSymbols = new(StocksProvider.DefaultSymbols);
        _data.TickerEnabled = true;
        _save();
        Restart(kind);
        Apply();
    }

    static string Title(TickerKind kind) => kind switch
    {
        TickerKind.Cricket => "Cricket",
        TickerKind.Stocks => "Stocks",
        _ => "Crypto",
    };

    string Describe() => string.Join(", ", _data.TickerKinds.OrderBy(k => k).Select(k => Title(k).ToLowerInvariant()));

    string Labels() => string.Join(", ", _data.StockSymbols.Select(TickerFormat.SymbolLabel));

    string? Detail()
    {
        var parts = new List<string>();
        if (_data.TickerKinds.Contains(TickerKind.Cricket)) parts.Add(_data.CricketTeam is { } t ? $"cricket: {t}" : "all live cricket");
        if (_data.TickerKinds.Contains(TickerKind.Stocks)) parts.Add(Labels());
        if (_data.TickerKinds.Contains(TickerKind.Crypto)) parts.Add("BTC, ETH");
        return parts.Count > 0 ? string.Join(" \u00B7 ", parts) : null;
    }

    /// <summary>For the test pipe: "cricket [team]", "stocks [SYMBOLS...]" or "crypto", fetched once without enabling anything.</summary>
    public string PreviewBlocking(string arg)
    {
        var words = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "error: usage ambient-ticker-preview cricket|stocks|crypto [args]";
        var rest = words.Skip(1).ToList();
        ITickerProvider? provider = words[0].ToLowerInvariant() switch
        {
            "cricket" => new CricketProvider(() => rest.Count > 0 ? string.Join(' ', rest) : null),
            "stocks" => new StocksProvider(() => rest.Count > 0 ? rest.Select(s => s.ToUpperInvariant()).ToList() : _data.StockSymbols.ToList()),
            "crypto" => new CryptoProvider(),
            _ => null,
        };
        if (provider is null) return $"error: unknown provider '{words[0]}' (cricket, stocks, crypto)";
        return AmbientNet.Block(async () =>
        {
            try
            {
                var items = await provider.RefreshAsync(_ctx.Http, CancellationToken.None).ConfigureAwait(false);
                return items.Count == 0 ? "(no items)" : string.Join(" | ", items.Select(i => i.Live ? i.Text + " [live]" : i.Text));
            }
            catch (Exception ex)
            {
                return "error: " + ex.Message;
            }
        }, TimeSpan.FromSeconds(30), "error: timed out");
    }

    public string State
    {
        get
        {
            var sources = _sources.Values.Where(s => IsActive(s.Provider.Kind)).Select(s =>
                $"{s.Provider.Name}={s.Items.Count} items, updated {AmbientNet.Ago(s.UpdatedUtc)}, failures {s.Failures}" +
                (s.Error is null ? "" : $" ({s.Error})") +
                (s.Timer.IsEnabled ? $", next {s.Timer.Interval.TotalSeconds:0}s" : ""));
            return $"{(_data.TickerEnabled ? "on" : "off")} kinds=[{Describe()}] symbols=[{string.Join(",", _data.StockSymbols)}]" +
                   $"{(_data.CricketTeam is { } t ? $" team={t}" : "")} shown={(_ctx.Get(ActivityId) is null ? "no" : "yes")}" +
                   $" current='{_content.Current}' {string.Join("; ", sources)}";
        }
    }

    public void Dispose()
    {
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        foreach (var s in _sources.Values)
        {
            s.Timer.Stop();
            s.Cts?.Cancel();
        }
        _content.Dispose();
    }
}
