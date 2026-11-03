using System.Diagnostics;
using System.Globalization;
using System.Windows.Threading;
using Shammy.Core.MeetingSystem;

namespace Shammy.Modules.MeetingSystem;

/// <summary>
/// System monitor: a "System" tab with CPU, memory and battery, refreshed every second only while it
/// is on screen, plus a cheap 10 s background sample that raises alerts for runaway apps, full memory
/// and fast battery drain.
/// </summary>
internal sealed class SystemModule : IShammyModule
{
    const double MemoryAlertPercent = 90, DrainAlertPerHour = 25;

    /// <summary>
    /// An app keeping one and a half cores busy for 30 s is the runaway worth flagging (a single busy video
    /// tab stays under it). As a share of the whole machine that's under 19% on 8 cores; the old "80% of the
    /// machine" rule never fired there. Capped at 80% so small machines behave as before.
    /// </summary>
    static readonly double HogPercent = Math.Min(80, 150.0 / Environment.ProcessorCount);
    static readonly TimeSpan VisibleInterval = TimeSpan.FromSeconds(1);
    static readonly TimeSpan HiddenInterval = TimeSpan.FromSeconds(10);
    static readonly TimeSpan HistorySpan = TimeSpan.FromSeconds(60);

    readonly List<(DateTime At, double Value)> _history = new();
    readonly DrainEstimator _drain = new();
    readonly TimeSmoother _reportedDrain = new(TimeSpan.FromMinutes(2));
    readonly AlertCooldown _cooldown = new(TimeSpan.FromMinutes(10));
    readonly SustainedCondition _hog = new(TimeSpan.FromSeconds(30));
    readonly SustainedCondition _fastDrain = new(TimeSpan.FromMinutes(2));

    ModuleContext? _ctx;
    SystemSampler? _sampler;
    SystemPanel? _panel;
    DispatcherTimer? _timer;
    SystemSample? _last;
    double? _drainRate;
    bool _sampling;

    public string Name => "system";

    public void Start(ModuleContext ctx)
    {
        _ctx = ctx;
        _sampler = new SystemSampler(ctx.Warn);
        _panel = new SystemPanel();
        ctx.AddTab("", "System", _panel);
        _panel.IsVisibleChanged += (_, _) => OnVisibilityChanged();

        _timer = new DispatcherTimer(HiddenInterval, DispatcherPriority.Background, (_, _) => _ = SampleAsync(), ctx.Ui);
        _timer.Start();

        ctx.AddCommand(new SystemCommand(this));
        ctx.AddPipeCommand("system-state", _ => State());
        ctx.AddPipeCommand("system-open", _ =>
        {
            OpenTab();
            return "ok";
        });
    }

    void OnVisibilityChanged()
    {
        if (_panel is null || _timer is null) return;
        bool visible = _panel.IsVisible;
        _timer.Interval = visible ? VisibleInterval : HiddenInterval;
        if (!visible) return;
        if (_last is not null) Render();
        _ = SampleAsync();
    }

    async Task SampleAsync()
    {
        if (_sampling || _sampler is null || _panel is null) return;
        _sampling = true;
        try
        {
            var sampler = _sampler;
            bool visible = _panel.IsVisible;
            var sample = await Task.Run(() => sampler.Sample(visible, HogPercent));
            Apply(sample);
        }
        catch (Exception ex)
        {
            _ctx?.Warn("System sample failed", ex);
        }
        finally
        {
            _sampling = false;
        }
    }

    void Apply(SystemSample s)
    {
        _last = s;
        _history.Add((s.At, s.Cpu));
        _history.RemoveAll(p => s.At - p.At > HistorySpan + TimeSpan.FromSeconds(2));

        if (s.HasBattery && !s.PluggedIn)
        {
            _drain.Add(s.At, s.BatteryPercent, false);
            // The battery's own power reading reacts in seconds; whole percentages need many minutes.
            double? reported = s.ReportedDrain is double r ? _reportedDrain.Add(r, s.At) : null;
            _drainRate = reported ?? _drain.RatePerHour(s.At);
        }
        else
        {
            _drain.Reset();
            _reportedDrain.Reset();
            _drainRate = null;
        }

        CheckAlerts(s);
        if (_panel?.IsVisible == true) Render();
    }

    void Render()
    {
        if (_panel is null || _last is null) return;
        _panel.Show(_last, _history, _drainRate);
    }

    void CheckAlerts(SystemSample s)
    {
        if (_ctx is null) return;
        var now = s.At;
        var hog = s.Top is { } top && top.Percent >= HogPercent ? top.Name : null;
        if (_hog.Update(hog, now) && _cooldown.TryFire("cpu", now))
            _ctx.ShowMessage("", $"{AlertName(hog!)} is using a lot of CPU", seconds: 4);

        if (s.MemoryLoad >= MemoryAlertPercent && _cooldown.TryFire("memory", now))
            _ctx.ShowMessage("", $"Memory is {s.MemoryLoad}% full", seconds: 4);

        bool fast = s.HasBattery && !s.PluggedIn && _drainRate > DrainAlertPerHour;
        if (_fastDrain.Update(fast, now) && _cooldown.TryFire("battery", now))
            _ctx.ShowMessage("", $"Battery draining fast ({SystemFormat.Rate(_drainRate ?? 0)})", seconds: 4);
    }

    /// <summary>"Visual Studio Code" rather than "Code" when the exe says so.</summary>
    static string AlertName(string processName)
    {
        string? path = null, description = null;
        try
        {
            var processes = Process.GetProcessesByName(processName);
            foreach (var p in processes)
            {
                try { path ??= p.MainModule?.FileName; }
                catch (Exception) { }
                p.Dispose();
            }
            if (path is not null) description = FileVersionInfo.GetVersionInfo(path).FileDescription;
        }
        catch (Exception)
        {
            // Elevated processes hide their path; the process name is still fine.
        }
        return AppNames.Friendly(path ?? processName, null, description);
    }

    async Task<CommandResult> SummaryAsync(string text)
    {
        if (_sampler is null) return new CommandResult("", "System monitor isn't running", IsError: true);
        var s = _last;
        if (s is null)
        {
            var sampler = _sampler;
            s = await Task.Run(() => sampler.Sample(false));
            Apply(s);
        }
        // The host shows this result in the notch, then collapses it ~2.6 s later; open the tab after.
        OpenTab();

        string cpuMemory = $"CPU {SystemFormat.Percent(s.Cpu)} · Memory {s.MemoryLoad}%";
        string battery = BatteryLine(s);
        return text.Contains("battery", StringComparison.OrdinalIgnoreCase)
            ? new CommandResult("", battery, cpuMemory)
            : new CommandResult("", cpuMemory, s.Top is { } t ? $"{AppNames.Friendly(t.Name)} is busiest · {battery}" : battery);
    }

    string BatteryLine(SystemSample s)
    {
        if (!s.HasBattery) return "No battery";
        if (s.PluggedIn) return $"Battery {s.BatteryPercent}%, {(s.Charging ? "charging" : "plugged in")}";
        return DrainEstimator.Remaining(s.BatteryPercent, _drainRate) is TimeSpan left
            ? $"Battery {s.BatteryPercent}%, {SystemFormat.Duration(left)} left"
            : $"Battery {s.BatteryPercent}%";
    }

    // OpenTab waits for a command's confirmation card and holds the tab until it's been looked at.
    void OpenTab()
    {
        if (_ctx is null || _panel is null) return;
        _ctx.OpenTab(_panel);
    }

    string State()
    {
        if (_sampler is null) return "error: not started";
        var sampler = _sampler;
        // Two readings a moment apart so the busiest app has a delta to measure.
        var s = Task.Run(() =>
        {
            sampler.Sample(true);
            Thread.Sleep(400);
            return sampler.Sample(true);
        }).GetAwaiter().GetResult();
        Apply(s);

        var inv = CultureInfo.InvariantCulture;
        string battery = s.HasBattery ? $"{s.BatteryPercent}%" : "none";
        string drain = _drainRate is double r ? SystemFormat.Rate(r) : "-";
        string left = DrainEstimator.Remaining(s.BatteryPercent, _drainRate) is TimeSpan t && !s.PluggedIn ? SystemFormat.Duration(t) : "-";
        string top = s.Top is { } p ? $"{p.Name} {p.Percent.ToString("0.0", inv)}%" : "-";
        return $"cpu={s.Cpu.ToString("0.0", inv)}% ram={s.MemoryLoad}% ({SystemFormat.Gigabytes(s.UsedMemory)}/{SystemFormat.Gigabytes(s.TotalMemory)} GB) " +
               $"battery={battery} plugged={s.PluggedIn} charging={s.Charging} drain={drain} remaining={left} top={top} " +
               $"history={_history.Count} visible={_panel?.IsVisible == true}";
    }

    public void Dispose()
    {
        _timer?.Stop();
    }

    sealed class SystemCommand : ITextCommand
    {
        readonly SystemModule _owner;

        public SystemCommand(SystemModule owner) => _owner = owner;

        public bool CanHandle(string text) => Phrases.IsSystemQuery(text);

        public Task<CommandResult> RunAsync(string text) => _owner.SummaryAsync(text);
    }
}
