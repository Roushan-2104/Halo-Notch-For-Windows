using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using Shammy.Core.Activities;
using Shammy.Infrastructure;
using Shammy.Interop;
using Shammy.Notifications;
using Shammy.Playback;
using Shammy.Search;
using Shammy.Shelf;
using Shammy.Shell;
using Shammy.SystemInfo;
using Shammy.Views;
using Microsoft.Win32;

namespace Shammy;

/// <summary>Creates every service, wires them to the notch, and turns their events into activities.</summary>
internal sealed class ShammyHost : IDisposable
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
    KeyboardIndicators? _keyboardPills;
    WhisperService _whisper = null!;
    UpdateService _updates = null!;
    int _testActionClicks, _testBubbleClicks;
    DateTime _lastPing;
    DispatcherTimer? _updateReminder;
    SearchCoordinator _search = null!;
    DownloadsService _downloads = null!;
    ShelfStore _shelf = null!;
    NotificationService _notifications = null!;
    DispatcherTimer _tick = null!;
    bool _wasPlaying;

    public ShammyHost(AppSettings settings) => _settings = settings;

    public void Start()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        _media = Own(new MediaService(_ui));
        _visualizer = Own(new AudioVisualizer());
        _battery = new BatteryService(_ui);
        _shelf = new ShelfStore(_settings);
        _notifications = Own(new NotificationService(_ui));
        var services = new ShammyServices(_media, _visualizer, _battery, _shelf, _notifications, _settings);

        _notch = new NotchWindow(services);
        _notch.GlanceProviders = _modules.Glances;
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
            services.Volume = _volume;
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
            // The level arrives a moment after the connect pill (the headset reports it once attached):
            // update the pill in place while it's still showing that device.
            bt.BatteryRead += (name, percent) =>
            {
                if (_activities.Get("bluetooth") is { Payload: BluetoothPayload { Connected: true } p } a && p.Name == name)
                    Post(a with { Payload = p with { Battery = percent }, ExpiresAt = In(3.5) });
            };
            bt.Start();
            services.Bluetooth = bt;
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
            _notch.ActionClicked += id => _activities.Remove(id);
            // Same for a clickable message: it waits while you move to click it.
            _notch.ActionHovered += (id, hovered) =>
            {
                if (_activities.Get(id) is { } a) Post(a with { ExpiresAt = hovered ? null : In(4) });
            };
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

        _whisper = Own(new WhisperService(_settings)
        {
            Language = _settings.VoiceLanguage,
            Vocabulary = () => _modules.Vocabulary.SelectMany(f => f()),
            SongHistory = () => _modules.SongVocabulary.SelectMany(f => f()),
        });
        _whisper.Upgraded += model => _ui.BeginInvoke(() =>
            Post(new Activity("voice-upgraded", ActivityKind.Message, PriorityMessage,
                new MessagePayload("", "Voice upgraded · better accuracy"), In(3))));
        _modules.VocabularyChanged += () => _ = Task.Run(_whisper.ResetProcessors);
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
        _whisper.HinglishDownloading += () => _ui.BeginInvoke(() =>
            Post(new Activity("voice-hinglish-get", ActivityKind.Message, PriorityMessage,
                new MessagePayload("", "Getting Hinglish voice · 574 MB"), In(3))));
        _whisper.HinglishProgress += p => _ui.BeginInvoke(() => Post(new Activity("voice-hinglish", ActivityKind.Progress, PriorityProgress,
            new ProgressPayload("", "Hinglish voice", p))));
        _whisper.HinglishReady += () => _ui.BeginInvoke(() =>
        {
            _activities.Remove("voice-hinglish");
            Post(new Activity("voice-hinglish-ready", ActivityKind.Message, PriorityMessage, new MessagePayload("", "Hinglish voice ready"), In(2.5)));
        });
        _whisper.HinglishFailed += reason => _ui.BeginInvoke(() =>
        {
            _activities.Remove("voice-hinglish");
            Post(new Activity("voice-hinglish-failed", ActivityKind.Message, PriorityMessage, new MessagePayload("", reason, true), In(4)));
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
        Safe("keyboard pills", () => _keyboardPills = new KeyboardIndicators(_keys, _ui, Post));
        _search = new SearchCoordinator(_notch, _keys, _whisper, _settings, _modules);
        _notch.ExpandedView.ShelfActions = _modules.ShelfActions;
        _notch.ExpandedView.SearchRequested += _search.OpenTyping;
        // The v2 open notch, for scripted checks: "v2-state", "v2-open <id|chip label>", "v2-back".
        _modules.PipeCommands["v2-state"] = _ => $"mode={_notch.Mode} {_notch.ExpandedView.DescribeV2()}";
        _modules.PipeCommands["v2-open"] = arg => _notch.OpenView(arg) ? "ok" : $"error: no view '{arg.Trim()}'";
        _modules.PipeCommands["v2-back"] = _ =>
        {
            _notch.ExpandedView.ShowHome();
            return "ok";
        };
        // Typing in an open detail: "v2-type-focus" acts like a click into its first text box; "v2-typing" reports.
        _modules.PipeCommands["v2-type-focus"] = _ => _notch.FocusFirstInputForTest();
        _modules.PipeCommands["v2-typing"] = _ => _notch.DescribeTyping();
        NotchSound.Enabled = _settings.NotchSounds;
        _modules.TrayItems.Add(new Modules.ModuleTrayItem("Notch sounds (ping and chime)", null, () => _settings.NotchSounds, on =>
        {
            _settings.NotchSounds = on;
            _settings.Save();
            NotchSound.Enabled = on;
            if (on) NotchSound.Play(Shammy.Core.Audio.PingKind.Notification);
        }));
        _modules.TrayItems.Add(new Modules.ModuleTrayItem("Colour edge on events", null, () => _settings.ShammyGlow, on =>
        {
            _settings.ShammyGlow = on;
            _settings.Save();
            _notch.SetShammyEnabled(on);
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
        SystemEvents.TimeChanged += OnTimeChanged;

        _updates = Own(new UpdateService(_ui));
        _updates.Ready += _ => AnnounceUpdate();
        // A prompt that was missed (a call outranked it, the notch was busy) comes back later; the update
        // is also applied whenever Shammy quits, so nothing is lost if it's never clicked.
        _updateReminder = new DispatcherTimer(TimeSpan.FromHours(2), DispatcherPriority.Background, (_, _) => AnnounceUpdate(), _ui);
        _updates.Start();
        AskAboutStartup();

        Own(new TrayIcon(_settings, () => Application.Current.Shutdown(),
            replace => _keys.InterceptVolume = replace && _volume is not null,
            viz => OnMediaChanged(),
            () => _notch.Reposition(),
            () =>
            {
                _whisper.Language = _settings.VoiceLanguage;
                _ = Task.Run(_whisper.ResetProcessors);
            },
            () => _whisper.HinglishAvailable,
            () =>
            {
                bool on = _settings.VoiceMode == AppSettings.HinglishVoice;
                if (!on) _activities.Remove("voice-hinglish");
                _ = _whisper.SetHinglishAsync(on);
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
            Log.Warn("Test command pipe is ON (SHAMMY_TEST_PIPE=1)");
        }

        // Warm up the browser engine in the background so the first search opens instantly.
        _ = WarmUpBrowserAsync();

        Log.Info($"Shammy ready in {clock.ElapsedMilliseconds} ms");
    }

    /// <summary>Find every IShammyModule in the app and start it; one broken module never stops the rest.</summary>
    void StartModules(ShammyServices services)
    {
        var types = typeof(ShammyHost).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(Modules.IShammyModule).IsAssignableFrom(t))
            .OrderBy(t => t.Name);
        // Diagnostics: SHAMMY_DISABLE_MODULES=music,clipboard starts Shammy without those modules.
        var disabled = (Environment.GetEnvironmentVariable("SHAMMY_DISABLE_MODULES") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var type in types)
        {
            Safe($"module {type.Name}", () =>
            {
                var module = (Modules.IShammyModule)Activator.CreateInstance(type)!;
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
    // That's why Shammy deliberately keeps showing notifications while DND is on.
    void OnNotification(ShammyNotification n)
    {
        if (!_settings.ShowNotifications || _settings.MutedNotificationApps.Contains(n.AppId)) return;
        // During a call or screen capture others may see the notch: show who it's from, not what it says.
        if (PrivacyState.Presenting)
        {
            var (title, body) = Shammy.Core.Notifications.NotificationText.ForPopup(n.AppName, n.Title, n.Body, presenting: true);
            n = new ShammyNotification
            {
                Id = n.Id, AppId = n.AppId, AppName = n.AppName, Icon = n.Icon, Time = n.Time,
                Title = title, Body = body,
            };
        }

        int more = _activities.Get("notification") is { Payload: NotificationPayload p } ? p.More + 1 : 0;
        // Shammy's own soft ping (Windows' sound can be turned off in its notification settings). A burst
        // of toasts pings once.
        if (DateTime.UtcNow - _lastPing > TimeSpan.FromSeconds(2))
        {
            _lastPing = DateTime.UtcNow;
            NotchSound.Play(Shammy.Core.Audio.PingKind.Notification);
        }
        Post(new Activity("notification", ActivityKind.Notification, PriorityNotification, new NotificationPayload(n, more), In(6))
        {
            Click = () => OpenNotificationApp(n),
        });
    }

    void OpenNotificationApp(ShammyNotification n)
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
            case "test-action":
                // A clickable message like "update ready", for testing hover and click behaviour. SHAMMY_TEST_TOP=1
                // puts it above everything (e.g. a real call's mic pill) so tests still see it.
                Post(new Activity("test-action", ActivityKind.Message,
                    Environment.GetEnvironmentVariable("SHAMMY_TEST_TOP") == "1" ? 1000 : PriorityDownloadDone,
                    new MessagePayload("", arg.Length > 0 ? arg : "Test action · click me"), In(6))
                {
                    Click = () => _testActionClicks++,
                });
                return "ok";
            case "hover-diag": return _notch.HoverDiag;
            case "test-bubble":
                // A clickable long-running activity, which shows in the side bubble when something outranks it.
                Post(new Activity("test-bubble", ActivityKind.Progress, 30, new ProgressPayload("", "Test", 0.4), In(30))
                {
                    Click = () => _testBubbleClicks++,
                });
                return "ok";
            case "test-action-state":
                return $"clicks={_testActionClicks} bubbleClicks={_testBubbleClicks} shown={(_activities.Get("test-action") is { } t ? $"yes expires={(t.ExpiresAt is null ? "held" : "timed")}" : "no")}";
            case "sound":
                NotchSound.Play(arg.Trim() == "alert" ? Shammy.Core.Audio.PingKind.Alert : Shammy.Core.Audio.PingKind.Notification);
                return "ok";
            case "startup-state":
                return $"asked={_settings.StartupAsked} enabled={_settings.StartWithWindows} card={_startupAnswer}";
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
            // "notifications list": the rows the Notifications tab shows right now (text hidden while private).
            case "notifications" when arg.Trim() == "list":
                return $"presenting={PrivacyState.Presenting} rows={_notifications.History.Count} {_notch.ExpandedView.DescribeAlertRows()}";
            // "notify-test App|Title|Body": a made-up toast through the real path (history, pop-up, privacy).
            case "notify-test":
            {
                var f = arg.Split('|');
                _notifications.InjectForTest(new ShammyNotification
                {
                    Id = (uint)Random.Shared.Next(1 << 30, int.MaxValue),
                    AppId = "",
                    AppName = f[0].Trim().Length > 0 ? f[0].Trim() : "Test",
                    Title = f.Length > 1 ? f[1].Trim() : "Test",
                    Body = f.Length > 2 ? f[2].Trim() : "",
                    Time = DateTimeOffset.Now,
                });
                return "ok";
            }
            case "notifications":
                return $"available={_notifications.IsAvailable} problem={_notifications.Problem ?? "-"} history={_notifications.History.Count} " +
                       $"muted={_settings.MutedNotificationApps.Count} presenting={PrivacyState.Presenting} " +
                       $"shown={(_activities.Get("notification") is { Payload: NotificationPayload p } ? $"{p.Notification.Title} | {p.Notification.Body}" : "-")}";
            case "state":
                return $"mode={_notch.Mode} activity={_activities.Current?.Id ?? "-"} secondary={_activities.Secondary?.Id ?? "-"} " +
                       $"whisper={(_whisper.IsReady ? "ready:" + _whisper.ActiveModel.Replace(' ', '_') : _whisper.IsDownloading ? $"downloading {_whisper.DownloadProgress:P0}" : _whisper.Failure ?? "loading")} " +
                       $"keys={_keys.IsInstalled} media={(_media.HasSession ? _media.Title : "-")} playing={_media.IsPlaying} height={_notch.Height} " +
                       $"fullscreen={_notch.IsFullscreen}";
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
                // Test-only (the pipe is off unless SHAMMY_TEST_PIPE=1): load raw HTML into the results card.
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
            // "hold" / "release" = the look key (freeze); "hold talk" / "release talk" = Right Alt.
            case "hold": _search.OnKeyDown(arg.Trim() == "talk" ? HotKey.Talk : HotKey.Look); return "ok";
            case "release": _search.OnKeyUp(arg.Trim() == "talk" ? HotKey.Talk : HotKey.Look); return "ok";
            case "hud":
            {
                var a = arg.Split(' ');
                PostHud(a[0] == "brightness" ? HudKind.Brightness : HudKind.Volume, Num(a[1]), a.Length > 2 && a[2] == "muted");
                return "ok";
            }
            case "charging": Post(new Activity("battery", ActivityKind.Charging, PriorityCharging, new BatteryPayload((int)Num(arg), true), In(3.2))); return "ok";
            case "lowbattery": Post(new Activity("battery", ActivityKind.LowBattery, PriorityBatteryLow, new BatteryPayload((int)Num(arg), false), In(5))); return "ok";
            case "bluetooth":
                // "bluetooth Airdopes 311 Pro|80" previews the pill with a battery level.
                var btParts = arg.Split('|', 2);
                Post(new Activity("bluetooth", ActivityKind.Bluetooth, PriorityBluetooth, new BluetoothPayload(btParts[0].Trim(), true, true,
                    btParts.Length > 1 && int.TryParse(btParts[1], out var btLevel) ? btLevel : null), In(3.5)));
                return "ok";
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
            // ---- v2 resting notch, peek, gestures and keyboard pills ----
            // "v2-idle": pet mood, eyes and glance state; "v2-idle bounce" makes the pet hop.
            case "v2-idle":
                if (arg.Trim() == "bounce") _notch.BounceEyes();
                return _notch.DescribeIdle();
            // "v2-hover x,y,ms;x,y,ms;..." runs pointer samples through the hover rule (dry run, nothing opens).
            case "v2-hover": return DryRunHover(arg);
            // "pet-mood music" forces a mood (idle|music|notify|timer|listen|think|meeting|tired|sleepy|happy); "pet-mood auto" undoes it.
            case "pet-mood": return _notch.SetPetMood(arg);
            case "v2-peek": return _notch.ForcePeek(true);
            case "v2-rest": return _notch.ForcePeek(false);
            // "v2-gesture next|prev|volup|voldown [dry]".
            case "v2-gesture":
            {
                var g = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return g.Length == 0 ? "error: usage v2-gesture next|prev|volup|voldown [dry]"
                    : _notch.SimulateGesture(g[0].ToLowerInvariant(), g.Contains("dry", StringComparer.OrdinalIgnoreCase));
            }
            case "capslock-test":
                if (_keyboardPills is null) return "error: keyboard pills unavailable";
                _keyboardPills.ShowCaps(arg.Trim() != "off");
                return "ok";
            case "lang-test":
                if (_keyboardPills is null) return "error: keyboard pills unavailable";
                _keyboardPills.ShowLanguage(KeyboardIndicators.CurrentLabel(), arg.Trim().Length > 0 ? arg.Trim() : "हिन्दी");
                return "ok";
            case "quit": Application.Current.Shutdown(); return "bye";
            case "modules": return string.Join(", ", _disposables.OfType<Modules.IShammyModule>().Select(m => m.Name));
            default:
                return _modules.PipeCommands.TryGetValue(parts[0], out var handler) ? handler(arg) : "error: unknown command";
        }
    }

    /// <summary>Feeds "x,y,ms" samples (separated by ';' or spaces) to a fresh <see cref="Core.Notch.HoverIntent"/>
    /// and lists its decisions, e.g. "peek@266 open@970". Append "nopeek" for a clickable pill.</summary>
    static string DryRunHover(string arg)
    {
        var intent = new Core.Notch.HoverIntent { AllowPeek = !arg.Contains("nopeek") };
        var decisions = new List<string>();
        foreach (var token in arg.Split(new[] { ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var f = token.Split(',');
            if (f.Length != 3) continue;
            if (!double.TryParse(f[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
                !double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms)) return $"error: bad sample '{token}'";
            var d = intent.Sample(x, y, TimeSpan.FromMilliseconds(ms));
            if (d != Core.Notch.HoverDecision.None) decisions.Add($"{d.ToString().ToLowerInvariant()}@{ms:0}");
        }
        return decisions.Count == 0 ? $"none (stage={intent.Stage} speed={intent.Speed:0.00})" : string.Join(" ", decisions);
    }

    /// <summary>
    /// .NET caches the time zone; after travelling (or an automatic zone switch) DateTime.Now, reminders,
    /// "time in Tokyo" and {time} snippets would keep using the old one until Shammy restarted.
    /// </summary>
    static void OnTimeChanged(object? sender, EventArgs e)
    {
        TimeZoneInfo.ClearCachedData();
        Log.Info("System time or time zone changed");
    }

    /// <summary>
    /// Once, after installing: ask whether Shammy should start with Windows. Only for installed copies (a dev
    /// build's path changes with every build). Ignoring the card for a minute just asks again next launch.
    /// </summary>
    void AskAboutStartup()
    {
        if (_settings.StartupAsked) return;
        if (_settings.StartWithWindows)
        {
            _settings.StartupAsked = true;
            _settings.Save();
            return;
        }
        bool test = Environment.GetEnvironmentVariable("SHAMMY_TEST_STARTUP_CARD") == "1" && CommandServer.IsEnabled;
        if (!_updates.IsInstalled && !test) return;

        DispatcherTimer? dismiss = null;
        void Answer(bool yes)
        {
            dismiss?.Stop();
            _notch.CloseCard();
            _settings.StartupAsked = true;
            if (yes && !test)
            {
                try { _settings.StartWithWindows = true; }
                catch (Exception ex) { Log.Warn("Adding Shammy to startup failed", ex); }
            }
            _settings.Save();
            _startupAnswer = yes ? "yes" : "no";
            Log.Info($"Start with Windows: {(yes ? "on" : "not now")} (first-run question)");
            Post(new Activity("startup-answer", ActivityKind.Message, PriorityMessage,
                new MessagePayload(yes ? "" : "", yes ? "Shammy will start with Windows" : "OK · change it any time in the tray"), In(2.5)));
        }

        // A few seconds in, once the first-run messages (voice model download etc.) have had their moment.
        // (The interval+callback constructor rejects a null callback, so set the interval separately.)
        var show = new DispatcherTimer(DispatcherPriority.Background, _ui) { Interval = TimeSpan.FromSeconds(6) };
        show.Tick += (_, _) =>
        {
            show.Stop();
            _notch.ShowCard(new StartupCard(() => Answer(true), () => Answer(false)), StartupCard.CardWidth, StartupCard.CardHeight, interactive: false);
            _startupAnswer = "showing";
            dismiss = new DispatcherTimer(TimeSpan.FromSeconds(60), DispatcherPriority.Background, (_, _) =>
            {
                dismiss!.Stop();
                if (_startupAnswer != "showing") return;
                _notch.CloseCard();
                _startupAnswer = "ignored";
            }, _ui);
            dismiss.Start();
        };
        show.Start();
    }

    string _startupAnswer = "-";

    void AnnounceUpdate()
    {
        if (_updates.ReadyVersion is not { } version) return;
        _updateReminder?.Start();
        // Above routine messages, so a battery or clipboard toast can't bump it off the notch.
        Post(new Activity("update-ready", ActivityKind.Message, PriorityDownloadDone,
            new MessagePayload("", "Update ready · click to restart"), In(20))
        {
            Click = RestartForUpdate,
        });
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
        SystemEvents.TimeChanged -= OnTimeChanged;
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
