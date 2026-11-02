using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using Halo.Core.Activities;
using Halo.Infrastructure;
using Halo.Interop;
using Halo.Playback;
using Halo.Search;
using Halo.Shelf;
using Halo.Shell;
using Halo.SystemInfo;
using Microsoft.Win32;

namespace Halo;

/// <summary>Creates every service, wires them to the notch, and turns their events into activities.</summary>
internal sealed class HaloHost : IDisposable
{
    const int PriorityHud = 90, PriorityBatteryLow = 75, PriorityCharging = 70, PriorityBluetooth = 60,
        PriorityDownloadDone = 55, PriorityMessage = 50, PriorityDownloading = 40, PriorityProgress = 30, PriorityMedia = 20;

    readonly AppSettings _settings;
    readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    readonly ActivityManager _activities = new();
    readonly List<IDisposable> _disposables = new();

    NotchWindow _notch = null!;
    MediaService _media = null!;
    AudioVisualizer _visualizer = null!;
    VolumeService _volume = null!;
    BatteryService _battery = null!;
    GlobalKeys _keys = null!;
    WhisperService _whisper = null!;
    SearchCoordinator _search = null!;
    DownloadsService _downloads = null!;
    ShelfStore _shelf = null!;
    DispatcherTimer _tick = null!;
    bool _wasPlaying;

    public HaloHost(AppSettings settings) => _settings = settings;

    public void Start()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        _media = Own(new MediaService(_ui));
        _visualizer = Own(new AudioVisualizer());
        _battery = new BatteryService(_ui);
        _shelf = new ShelfStore(_settings);
        var services = new HaloServices(_media, _visualizer, _battery, _shelf, _settings);

        _notch = new NotchWindow(services);
        _notch.FilesDropped += files =>
        {
            int added = _shelf.Add(files);
            Log.Info($"Shelf: added {added} of {files.Length}");
        };
        _notch.Show();

        _activities.Changed += () => _notch.SetActivities(_activities.Current, _activities.Secondary);
        _tick = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Normal, (_, _) => _activities.Tick(DateTime.UtcNow), _ui);
        _tick.Start();

        Safe("media", () =>
        {
            _media.Changed += OnMediaChanged;
            _ = _media.StartAsync().ContinueWith(t => Log.Error("Media sessions unavailable", t.Exception), TaskContinuationOptions.OnlyOnFaulted);
        });

        Safe("volume", () =>
        {
            _volume = Own(new VolumeService(_ui));
            _volume.Changed += (level, muted) => PostHud(HudKind.Volume, level, muted);
            _volume.DefaultDeviceChanged += () => _visualizer.Restart();
            _volume.Start();
        });

        Safe("brightness", () =>
        {
            var brightness = Own(new BrightnessService(_ui));
            brightness.Changed += level => PostHud(HudKind.Brightness, level, false);
            brightness.Start();
        });

        Safe("battery", () =>
        {
            _battery.PluggedIn += p => Post(new Activity("battery", ActivityKind.Charging, PriorityCharging,
                new BatteryPayload(p, true), In(3.2)));
            _battery.Low += p => Post(new Activity("battery", ActivityKind.LowBattery, PriorityBatteryLow,
                new BatteryPayload(p, false), In(5)));
            _battery.Changed += _notch.RefreshExpanded;
            _battery.Start();
        });

        Safe("bluetooth", () =>
        {
            var bt = Own(new BluetoothService(_ui));
            bt.AudioDeviceChanged += (name, connected) => Post(new Activity("bluetooth", ActivityKind.Bluetooth, PriorityBluetooth,
                new BluetoothPayload(name, connected, true), In(3.5)));
            bt.Start();
        });

        Safe("downloads", () =>
        {
            _downloads = Own(new DownloadsService(_ui));
            _downloads.Progress += (name, bytes, count) => Post(new Activity("download", ActivityKind.Downloading, PriorityDownloading,
                new DownloadPayload(count > 1 ? $"{count} downloads" : name, null, bytes, count)));
            _downloads.Completed += (name, path) => Post(new Activity("download-done", ActivityKind.DownloadComplete, PriorityDownloadDone,
                new DownloadPayload(name, path, 0, 1), In(4))
            {
                Click = () => System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\""),
            });
            _downloads.AllFinished += () => _activities.Remove("download");
            _downloads.Start();
        });

        _whisper = Own(new WhisperService { Language = _settings.VoiceLanguage });
        _whisper.Progress += p => _ui.BeginInvoke(() => Post(new Activity("voice-model", ActivityKind.Progress, PriorityProgress,
            new ProgressPayload("", "Voice", p))));
        _whisper.Ready += () => _ui.BeginInvoke(() =>
        {
            if (_activities.Remove("voice-model"))
                Post(new Activity("voice-ready", ActivityKind.Message, PriorityMessage, new MessagePayload("", "Voice search ready"), In(2.5)));
        });
        _whisper.Failed += reason => _ui.BeginInvoke(() =>
        {
            _activities.Remove("voice-model");
            Post(new Activity("voice-failed", ActivityKind.Message, PriorityMessage, new MessagePayload("", reason, true), In(4)));
        });
        _ = _whisper.InitAsync();

        _keys = Own(new GlobalKeys(_ui));
        if (!_keys.IsInstalled)
            Post(new Activity("keys-failed", ActivityKind.Message, PriorityMessage, new MessagePayload("", "Hotkey unavailable", true), In(5)));
        _keys.InterceptVolume = _settings.ReplaceVolumeFlyout && _volume is not null;
        _keys.VolumeKey += dir => _volume?.Step(dir);
        _search = new SearchCoordinator(_notch, _keys, _whisper, _settings);

        Safe("fullscreen", () =>
        {
            var fs = Own(new FullscreenWatcher(_ui));
            fs.Changed += full => _notch.SetFullscreen(full);
            fs.ForegroundChanged += _notch.BringToFront;
            fs.Check();
        });

        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        Own(new TrayIcon(_settings, () => Application.Current.Shutdown(),
            replace => _keys.InterceptVolume = replace && _volume is not null,
            viz => OnMediaChanged(),
            () => _notch.Reposition(),
            () =>
            {
                _whisper.Language = _settings.VoiceLanguage;
                _ = Task.Run(_whisper.ResetProcessors);
            }));
        Own(new CommandServer(_ui, HandleCommand));

        // Warm up the browser engine in the background so the first search opens instantly.
        _ = WarmUpBrowserAsync();

        Log.Info($"Halo ready in {clock.ElapsedMilliseconds} ms");
    }

    async Task WarmUpBrowserAsync()
    {
        await Task.Delay(2500);
        if (_search.IsActive) return;
        try
        {
            await _notch.WarmUpResultsAsync();
            if (!_search.IsActive) _notch.ResultView.Release();
        }
        catch (Exception)
        {
            // Logged inside; the first search retries.
        }
    }

    void OnMediaChanged()
    {
        bool playing = _media.IsPlaying;
        if (playing)
        {
            Post(new Activity("media", ActivityKind.NowPlaying, PriorityMedia) { Click = () => _notch.ExpandTo(shelf: false) });
        }
        else if (_wasPlaying && _media.HasSession)
        {
            // Keep the paused track around briefly, like iOS does, then let the notch go back to idle.
            Post(new Activity("media", ActivityKind.NowPlaying, PriorityMedia, ExpiresAt: In(12)) { Click = () => _notch.ExpandTo(shelf: false) });
        }
        else if (!_media.HasSession)
        {
            _activities.Remove("media");
        }
        _wasPlaying = playing;

        if (playing && _settings.ShowVisualizer) _visualizer.Start();
        else _visualizer.Stop();
        _notch.RefreshMedia();
    }

    void PostHud(HudKind kind, double level, bool muted) =>
        Post(new Activity("hud", ActivityKind.Hud, PriorityHud, new HudPayload(kind, level, muted), In(1.6)));

    void Post(Activity a) => _activities.Post(a);

    static DateTime In(double seconds) => DateTime.UtcNow.AddSeconds(seconds);

    void OnDisplayChanged(object? sender, EventArgs e) => _ui.BeginInvoke(() =>
    {
        _notch.Reposition();
        _notch.BringToFront();
    });

    void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
            _ui.BeginInvoke(Theme.Accent.Apply);
        // Moving, resizing or auto-hiding the taskbar changes the work area (reported as "Desktop").
        if (e.Category is UserPreferenceCategory.Desktop or UserPreferenceCategory.General)
            _ui.BeginInvoke(_notch.Reposition);
    }

    /// <summary>Commands for scripted testing over the local pipe (see <see cref="CommandServer"/>).</summary>
    string HandleCommand(string line)
    {
        var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "error: empty";
        string arg = parts.Length > 1 ? parts[1] : "";
        double Num(string s) => double.Parse(s, CultureInfo.InvariantCulture);

        switch (parts[0].ToLowerInvariant())
        {
            case "ping": return "pong";
            case "diag": return Motion.Animator.Diagnostics;
            case "state":
                return $"mode={_notch.Mode} activity={_activities.Current?.Id ?? "-"} secondary={_activities.Secondary?.Id ?? "-"} " +
                       $"whisper={(_whisper.IsReady ? "ready" : _whisper.IsDownloading ? $"downloading {_whisper.DownloadProgress:P0}" : _whisper.Failure ?? "loading")} " +
                       $"keys={_keys.IsInstalled} media={(_media.HasSession ? _media.Title : "-")} playing={_media.IsPlaying} height={_notch.Height}";
            case "expand": _notch.ExpandTo(arg.Contains("shelf"), hold: arg.Contains("hold")); return "ok";
            case "viz":
                return $"running={_visualizer.IsRunning} levels=" +
                       string.Join(",", Enumerable.Range(0, AudioVisualizer.Bands).Select(i => _visualizer.Level(i).ToString("0.00", CultureInfo.InvariantCulture)));
            case "shelf-add": return $"added {_shelf.Add(new[] { arg })}";
            case "collapse": _notch.Collapse(); return "ok";
            case "type": _search.OpenTyping(); return "ok";
            case "search": _ = _search.SearchTextAsync(arg); return "ok";
            case "lens": _ = _search.SearchImageAsync(File.ReadAllBytes(arg)); return "ok";
            case "voice-language":
                _whisper.Language = arg;
                _whisper.ResetProcessors();
                return "ok";
            case "transcribe":
            {
                // Runs a WAV file through the same speech gate + Whisper path the mic uses.
                using var reader = new NAudio.Wave.AudioFileReader(arg);
                NAudio.Wave.ISampleProvider mono = reader.WaveFormat.Channels == 1
                    ? reader
                    : new NAudio.Wave.SampleProviders.StereoToMonoSampleProvider(reader);
                var resampled = new NAudio.Wave.SampleProviders.WdlResamplingSampleProvider(mono, MicRecorder.SampleRate);
                var samples = new List<float>();
                var buf = new float[4096];
                int n;
                while ((n = resampled.Read(buf, 0, buf.Length)) > 0) samples.AddRange(buf.Take(n));
                var arr = samples.ToArray();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                // Off the UI thread's sync context, otherwise blocking here would deadlock the awaits inside.
                var text = Task.Run(() => _whisper.TranscribeAsync(arr)).GetAwaiter().GetResult();
                return $"speech={MicRecorder.HasSpeech(arr)} ms={sw.ElapsedMilliseconds} text={text}";
            }
            case "cancel": _search.Cancel(); return "ok";
            case "hold": _search.OnKeyDown(); return "ok";
            case "release": _search.OnKeyUp(); return "ok";
            case "hud":
            {
                var a = arg.Split(' ');
                PostHud(a[0] == "brightness" ? HudKind.Brightness : HudKind.Volume, Num(a[1]), a.Length > 2 && a[2] == "muted");
                return "ok";
            }
            case "charging": Post(new Activity("battery", ActivityKind.Charging, PriorityCharging, new BatteryPayload((int)Num(arg), true), In(3.2))); return "ok";
            case "lowbattery": Post(new Activity("battery", ActivityKind.LowBattery, PriorityBatteryLow, new BatteryPayload((int)Num(arg), false), In(5))); return "ok";
            case "bluetooth": Post(new Activity("bluetooth", ActivityKind.Bluetooth, PriorityBluetooth, new BluetoothPayload(arg, true, true), In(3.5))); return "ok";
            case "downloading": Post(new Activity("download", ActivityKind.Downloading, PriorityDownloading, new DownloadPayload(arg, null, 12_400_000, 1))); return "ok";
            case "downloaded":
                _activities.Remove("download");
                Post(new Activity("download-done", ActivityKind.DownloadComplete, PriorityDownloadDone, new DownloadPayload(arg, null, 0, 1), In(4)));
                return "ok";
            case "message": Post(new Activity("message", ActivityKind.Message, PriorityMessage, new MessagePayload("", arg), In(3))); return "ok";
            case "clear":
                foreach (var a in _activities.All.Where(a => a.Id != "media").ToList()) _activities.Remove(a.Id);
                return "ok";
            case "fullscreen": _notch.SetFullscreen(arg == "on"); return "ok";
            case "volume": _volume?.Step(arg == "up" ? 1 : arg == "down" ? -1 : 0); return "ok";
            case "media":
                if (arg == "toggle") _media.TogglePlayPause();
                else if (arg == "next") _media.Next();
                return "ok";
            case "quit": Application.Current.Shutdown(); return "bye";
            default: return "error: unknown command";
        }
    }

    T Own<T>(T disposable) where T : IDisposable
    {
        _disposables.Add(disposable);
        return disposable;
    }

    static void Safe(string name, Action start)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try { start(); }
        catch (Exception ex) { Log.Error($"Starting {name} failed", ex); }
        if (sw.ElapsedMilliseconds > 150) Log.Info($"Starting {name} took {sw.ElapsedMilliseconds} ms");
    }

    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _tick?.Stop();
        _search?.Cancel();
        for (int i = _disposables.Count - 1; i >= 0; i--)
        {
            try { _disposables[i].Dispose(); }
            catch (Exception ex) { Log.Warn("Dispose failed", ex); }
        }
        _settings.Save();
    }
}
