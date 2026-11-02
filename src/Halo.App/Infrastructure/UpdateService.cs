using System.Windows.Threading;
using Velopack;
using Velopack.Sources;

namespace Halo.Infrastructure;

/// <summary>
/// Automatic updates from the GitHub Releases page, via Velopack. Checks shortly after start and then
/// every few hours, downloads quietly in the background (usually just a small delta), then offers a
/// restart. An update nobody clicks on is applied the next time Halo exits. Copies that weren't
/// installed with Setup (e.g. a dev build) never update themselves.
/// </summary>
internal sealed class UpdateService : IDisposable
{
    const string RepoUrl = "https://github.com/Roushan-2104/Halo-Notch-For-Windows";
    static readonly TimeSpan FirstCheck = TimeSpan.FromSeconds(45);
    static readonly TimeSpan Interval = TimeSpan.FromHours(4);

    readonly Dispatcher _ui;
    readonly UpdateManager _manager;
    readonly DispatcherTimer _timer;
    readonly string _source;
    VelopackAsset? _ready;

    public UpdateService(Dispatcher ui)
    {
        _ui = ui;
        // Test builds can point at a local folder of packages to rehearse an update end to end.
        var testFeed = CommandServer.IsEnabled ? Environment.GetEnvironmentVariable("HALO_UPDATE_SOURCE") : null;
        IUpdateSource source = Directory.Exists(testFeed)
            ? new SimpleFileSource(new DirectoryInfo(testFeed))
            : new GithubSource(RepoUrl, null, false);
        _source = source is SimpleFileSource ? "local test feed" : "GitHub";
        _manager = new UpdateManager(source);
        _timer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = FirstCheck };
        _timer.Tick += (_, _) =>
        {
            _timer.Interval = Interval;
            _ = CheckAsync();
        };
    }

    /// <summary>Raised on the UI thread with the new version once it's downloaded and ready.</summary>
    public event Action<string>? Ready;

    public bool IsInstalled => _manager.IsInstalled;
    public string CurrentVersion => _manager.CurrentVersion?.ToString() ?? typeof(UpdateService).Assembly.GetName().Version?.ToString(3) ?? "?";
    public string? ReadyVersion => _ready?.Version.ToString();
    public string LastResult { get; private set; } = "not checked";

    public void Start()
    {
        if (!IsInstalled)
        {
            LastResult = "off (not installed with Setup)";
            Log.Info("Updates off: this copy wasn't installed with Setup");
            return;
        }
        Log.Info($"Updates on: version {CurrentVersion}, checking {_source}");
        _timer.Start();
    }

    /// <summary>
    /// Checks now; returns a short description of what happened. A check already running (the background
    /// one) is shared, so "Check for updates" never reports a stale "up to date" while downloading.
    /// </summary>
    public Task<string> CheckAsync()
    {
        if (!IsInstalled) return Task.FromResult(LastResult);
        if (_inFlight is { IsCompleted: false } running) return running;
        return _inFlight = CheckCoreAsync();
    }

    Task<string>? _inFlight;

    async Task<string> CheckCoreAsync()
    {
        if (_ready is not null) return LastResult = $"ready {_ready.Version}";
        try
        {
            var info = await _manager.CheckForUpdatesAsync();
            if (info is null) return LastResult = "up to date";
            Log.Info($"Update {info.TargetFullRelease.Version} found; downloading");
            await _manager.DownloadUpdatesAsync(info);
            _ready = info.TargetFullRelease;
            Log.Info($"Update {_ready.Version} downloaded");
            var version = _ready.Version.ToString();
            _ = _ui.BeginInvoke(() => Ready?.Invoke(version));
            return LastResult = $"ready {_ready.Version}";
        }
        catch (Exception ex)
        {
            // Offline, GitHub rate limits, a release mid-upload: just try again at the next interval.
            Log.Warn("Update check failed", ex);
            return LastResult = "check failed: " + ex.GetType().Name;
        }
    }

    /// <summary>Restart into the downloaded update now. The caller shuts the app down right after.</summary>
    public bool RestartIntoUpdate()
    {
        if (_ready is null) return false;
        Log.Info($"Restarting into update {_ready.Version}");
        _manager.WaitExitThenApplyUpdates(_ready, silent: true, restart: true);
        _ready = null;
        return true;
    }

    public void Dispose()
    {
        _timer.Stop();
        if (_ready is null) return;
        // Nobody clicked "restart": apply it now that Halo is exiting anyway.
        try { _manager.WaitExitThenApplyUpdates(_ready, silent: true, restart: false); }
        catch (Exception ex) { Log.Warn("Applying the update on exit failed", ex); }
    }
}
