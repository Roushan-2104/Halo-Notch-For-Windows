using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using Halo.Core.Activities;
using Halo.Infrastructure;
using Halo.Interop;
using Halo.Notifications;
using Halo.Playback;
using Halo.Search;
using Halo.Shelf;
using Halo.Shell;
using Halo.SystemInfo;
using Halo.Views;
using Microsoft.Win32;

namespace Halo;

/// <summary>Creates every service, wires them to the notch, and turns their events into activities.</summary>
internal sealed class HaloHost : IDisposable
{
    const int PriorityHud = 90, PriorityNotification = 80, PriorityBatteryLow = 75, PriorityCharging = 70, PriorityBluetooth = 60,
        PriorityDownloadDone = 55, PriorityMessage = 50, PriorityDownloading = 40, PriorityProgress = 30, PriorityMedia = 20;

    readonly AppSettings _settings;
    readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    readonly ActivityManager _activities = new();
    readonly List<IDisposable> _disposables = new();
    readonly Modules.ModuleRegistry _modules = new();

    NotchWindow _notch = null!;
    MediaService _media = null!;
    AudioVisualizer _visualizer = null!;
    VolumeService _volume = null!;
    BatteryService _battery = null!;
    GlobalKeys _keys = null!;
    WhisperService _whisper = null!;
    UpdateService _updates = null!;
    SearchCoordinator _search = null!;
    DownloadsService _downloads = null!;
    ShelfStore _shelf = null!;
    NotificationService _notifications = null!;
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
        _notifications = Own(new NotificationService(_ui));
        var services = new HaloServices(_media, _visualizer, _battery, _shelf, _notifications, _settings);

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
                Click = () => System.Diagnostics.Process.Start(Native.ExplorerPath, $"/select,\"{path}\""),
            });
            _downloads.AllFinished += () => _activities.Remove("download");
            _downloads.Start();
        });

        Safe("notifications", () =>
        {
            _notifications.Arrived += OnNotification;
            _notch.NotificationHovered += hovered =>
            {
                // Hold the notification while it's being read; give a moment after the pointer leaves.
                if (_activities.Get("notification") is { } a) Post(a with { ExpiresAt = hovered ? null : In(3) });
            };
            _notch.NotificationView.Opened += OpenNotificationApp;
            _notch.NotificationView.Dismissed += () => _activities.Remove("notification");
            _notch.NotificationView.MuteRequested += n =>
            {
                if (n.AppId.Length > 0 && !_settings.MutedNotificationApps.Contains(n.AppId))
                {
                    _settings.MutedNotificationApps.Add(n.AppId);
                    _settings.Save();
                }
                _activities.Remove("notification");
                Post(new Activity("muted", ActivityKind.Message, PriorityMessage, new MessagePayload("", $"Muted {n.AppName}"), In(2.5)));
            };
            _notch.ExpandedView.NotificationOpened += OpenNotificationApp;
            if (_settings.ShowNotifications) _ = StartNotificationsAsync();
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
        if (!WebView2Check.IsInstalled())
        {
            // Rare (it ships with Windows 11 and Edge), but without it search can't show results.
            Log.Warn("WebView2 Runtime not found");
            Post(new Activity("webview2-missing", ActivityKind.Message, PriorityMessage,
                new MessagePayload("", "Search needs WebView2 · click to install", true), In(12))
            {
                Click = WebView2Check.OpenDownload,
            });
        }
        // A first run without internet can't fetch the voice model; try again once a network comes up.
        System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += (_, e) =>
        {
            if (e.IsAvailable && _whisper.Failure is not null) _ui.BeginInvoke(() => _ = _whisper.InitAsync());
        };

        _keys = Own(new GlobalKeys(_ui));
        if (!_keys.IsInstalled)
            Post(new Activity("keys-failed", ActivityKind.Message, PriorityMessage, new MessagePayload("", "Hotkey unavailable", true), In(5)));
        _keys.InterceptVolume = _settings.ReplaceVolumeFlyout && _volume is not null;
        _keys.VolumeKey += dir => _volume?.Step(dir);
        _search = new SearchCoordinator(_notch, _keys, _whisper, _settings, _modules);
        _notch.ExpandedView.ShelfActions = _modules.ShelfActions;
        _modules.TrayItems.Add(new Modules.ModuleTrayItem("Colour edge on events", null, () => _settings.HaloGlow, on =>
        {
            _settings.HaloGlow = on;
            _settings.Save();
            _notch.SetHaloEnabled(on);
        }));
        StartModules(services);

        Safe("fullscreen", () =>
        {
            var fs = Own(new FullscreenWatcher(_ui));
            fs.Changed += full => _notch.SetFullscreen(full);
            fs.ForegroundChanged += _notch.BringToFront;
            fs.Check();
        });

        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        _updates = Own(new UpdateService(_ui));
        _updates.Ready += version =>
            // Above routine messages, so a battery or clipboard toast can't bump it off the notch.
            Post(new Activity("update-ready", ActivityKind.Message, PriorityDownloadDone,
                new MessagePayload("", $"Halo {version} is ready · click to restart"), In(20))
            {
                Click = RestartForUpdate,
            });
        _updates.Start();

        Own(new TrayIcon(_settings, () => Application.Current.Shutdown(),
            replace => _keys.InterceptVolume = replace && _volume is not null,
            viz => OnMediaChanged(),
            () => _notch.Reposition(),
            () =>
            {
                _whisper.Language = _settings.VoiceLanguage;
                _ = Task.Run(_whisper.ResetProcessors);
            },
            () =>
            {
                if (_settings.ShowNotifications) _ = StartNotificationsAsync();
                else _activities.Remove("notification");
            },
            _modules.TrayItems, _updates, RestartForUpdate));
        if (CommandServer.IsEnabled)
        {
            Own(new CommandServer(_ui, HandleCommand));
            Log.Warn("Test command pipe is ON (HALO_TEST_PIPE=1)");
        }

        // Warm up the browser engine in the background so the first search opens instantly.
        _ = WarmUpBrowserAsync();

        Log.Info($"Halo ready in {clock.ElapsedMilliseconds} ms");
    }

    /// <summary>Find every IHaloModule in the app and start it; one broken module never stops the rest.</summary>
    void StartModules(HaloServices services)
    {
        var types = typeof(HaloHost).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(Modules.IHaloModule).IsAssignableFrom(t))
            .OrderBy(t => t.Name);
        // Diagnostics: HALO_DISABLE_MODULES=music,clipboard starts Halo without those modules.
        var disabled = (Environment.GetEnvironmentVariable("HALO_DISABLE_MODULES") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var type in types)
        {
            Safe($"module {type.Name}", () =>
            {
                var module = (Modules.IHaloModule)Activator.CreateInstance(type)!;
                if (disabled.Contains(module.Name))
                {
                    module.Dispose();
                    Log.Info($"Module disabled: {module.Name}");
                    return;
                }
                module.Start(new Modules.ModuleContext(module.Name, _ui, services, _notch, _activities, _modules));
                Own(module);
                Log.Info($"Module started: {module.Name}");
            });
        }
    }

    async Task StartNotificationsAsync()
    {
        try
        {
            if (!_notifications.IsAvailable) await _notifications.StartAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Starting notifications failed", ex);
        }
    }

    // Windows doesn't let apps hide its own pop-ups, so Do Not Disturb is how users get "notch only".
    // That's why Halo deliberately keeps showing notifications while DND is on.
    void OnNotification(HaloNotification n)
    {
        if (!_settings.ShowNotifications || _settings.MutedNotificationApps.Contains(n.AppId)) return;
        // During a call or screen capture others may see the notch: show who it's from, not what it says.
        if (PrivacyState.Presenting)
        {
            n = new HaloNotification
            {
                Id = n.Id, AppId = n.AppId, AppName = n.AppName, Icon = n.Icon, Time = n.Time,
                Title = n.AppName, Body = "Hidden while you're in a call or sharing your screen",
            };
        }

        int more = _activities.Get("notification") is { Payload: NotificationPayload p } ? p.More + 1 : 0;
        Post(new Activity("notification", ActivityKind.Notification, PriorityNotification, new NotificationPayload(n, more), In(6))
        {
            Click = () => OpenNotificationApp(n),
        });
    }

    void OpenNotificationApp(HaloNotification n)
    {
        _activities.Remove("notification");
        _notch.Collapse();
        // The app ID comes from another app's toast, so it must look like an app ID and nothing more
        // (no spaces, quotes or paths that could smuggle extra arguments to explorer).
        if (!System.Text.RegularExpressions.Regex.IsMatch(n.AppId, @"^[A-Za-z0-9._!{}\-]{1,256}$")) return;
        // Windows doesn't let other apps trigger a toast's own action, so open the app it came from.
        try { System.Diagnostics.Process.Start(Native.ExplorerPath, $"shell:AppsFolder\\{n.AppId}"); }
        catch (Exception ex) { Log.Warn("Opening a notification's app failed", ex); }
    }

    async Task SaveFullPageAsync()
    {
        try
        {
            if (await _notch.ResultView.CaptureFullPageAsync() is { } image) ScreenshotSaver.Save(image, "page");
        }
        catch (Exception ex)
        {
            Log.Warn("Full-page screenshot failed", ex);
        }
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
            case "edge-flash":
                _notch.FlashEdge((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(arg.Trim()));
                return "ok";
            case "update-state":
                return $"installed={_updates.IsInstalled} version={_updates.CurrentVersion} ready={_updates.ReadyVersion ?? "-"} last={_updates.LastResult}";
            case "update-check": return Task.Run(_updates.CheckAsync).GetAwaiter().GetResult();
            case "update-restart":
                if (_updates.ReadyVersion is null) return "nothing ready";
                _ui.BeginInvoke(RestartForUpdate);
                return "restarting";
            case "diag": return Motion.Animator.Diagnostics;
            case "unmute":
                _settings.MutedNotificationApps.Clear();
                _settings.Save();
                return "ok";
            case "notifications":
                return $"available={_notifications.IsAvailable} problem={_notifications.Problem ?? "-"} history={_notifications.History.Count} " +
                       $"muted={_settings.MutedNotificationApps.Count} presenting={PrivacyState.Presenting} " +
                       $"shown={(_activities.Get("notification") is { Payload: NotificationPayload p } ? $"{p.Notification.Title} | {p.Notification.Body}" : "-")}";
            case "state":
                return $"mode={_notch.Mode} activity={_activities.Current?.Id ?? "-"} secondary={_activities.Secondary?.Id ?? "-"} " +
                       $"whisper={(_whisper.IsReady ? "ready" : _whisper.IsDownloading ? $"downloading {_whisper.DownloadProgress:P0}" : _whisper.Failure ?? "loading")} " +
                       $"keys={_keys.IsInstalled} media={(_media.HasSession ? _media.Title : "-")} playing={_media.IsPlaying} height={_notch.Height}";
            case "expand":
                _notch.ExpandTo(arg.Contains("shelf"), hold: arg.Contains("hold"));
                if (arg.Contains("alerts")) _notch.ExpandedView.ShowAlerts();
                return "ok";
            case "viz":
                return $"running={_visualizer.IsRunning} levels=" +
                       string.Join(",", Enumerable.Range(0, AudioVisualizer.Bands).Select(i => _visualizer.Level(i).ToString("0.00", CultureInfo.InvariantCulture)));
            case "shelf-add": return $"added {_shelf.Add(new[] { arg })}";
            case "collapse": _notch.Collapse(); return "ok";
            case "type": _search.OpenTyping(); return "ok";
            case "search": _ = _search.SearchTextAsync(arg); return "ok";
            case "browser-html":
                // Test-only (the pipe is off unless HALO_TEST_PIPE=1): load raw HTML into the results card.
                _ = _notch.ResultView.LoadTestHtmlAsync(arg);
                _search.ShowCardForTest();
                return "ok";
            case "fullpage":
                _ = SaveFullPageAsync();
                return "started";
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
            case "modules": return string.Join(", ", _disposables.OfType<Modules.IHaloModule>().Select(m => m.Name));
            default:
                return _modules.PipeCommands.TryGetValue(parts[0], out var handler) ? handler(arg) : "error: unknown command";
        }
    }

    void RestartForUpdate()
    {
        // Velopack waits for this process to exit, swaps in the new version and starts it again.
        if (_updates.RestartIntoUpdate()) Application.Current.Shutdown();
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
