using Shammy.Core.Activities;
using Shammy.Core.Connect;
using Shammy.Core.MeetingSystem;

namespace Shammy.Modules.MeetingSystem;

/// <summary>
/// Meeting mode: while any app has the microphone open, the compact notch shows a live mic indicator
/// and the app's name; clicking it mutes/unmutes the system microphone.
/// </summary>
internal sealed class MeetingModule : IShammyModule
{
    const string ActivityId = "meeting.mic";
    // Above Bluetooth (60) so a call stays visible, below charging/low battery (70-75).
    const int Priority = 65;

    ModuleContext? _ctx;
    MicMute? _mic;
    CapabilityWatcher? _watcher;
    MeetingContent? _content;
    CapabilitySnapshot _snapshot = CapabilitySnapshot.Empty;

    // "meeting-fake" test state: shows the activity without a real app and without touching real mute.
    string? _fakeApp;
    bool _fakeCamera, _fakeMuted, _fakeScreen;

    public string Name => "meeting";

    public void Start(ModuleContext ctx)
    {
        _ctx = ctx;
        _mic = new MicMute(ctx.Ui, ctx.Warn);
        _mic.Changed += Refresh;
        _mic.Start();

        _watcher = new CapabilityWatcher(ctx.Ui, ctx.Warn);
        _watcher.Changed += snapshot =>
        {
            _snapshot = snapshot;
            Refresh();
        };
        _watcher.Start();

        ctx.AddCommand(new MicCommand(this));
        ctx.AddPipeCommand("meeting-fake", Fake);
        ctx.AddPipeCommand("meeting-clear", _ =>
        {
            _fakeApp = null;
            Refresh();
            return "ok";
        });
        ctx.AddPipeCommand("meeting-state", _ => State());
    }

    void Refresh()
    {
        if (_ctx is null || _mic is null) return;
        bool fake = _fakeApp is not null;
        // Only a shared screen can show a message to others; on a plain call (mic or camera) nobody sees it.
        Infrastructure.PrivacyState.Set((fake && _fakeScreen) || _snapshot.Screen.Count > 0);
        // A call app on the mic (not a recorder or a game) makes the notch quiet: notifications wait.
        Infrastructure.QuietState.Set(Shammy.Core.Connect.QuietReason.Call,
            QuietRules.AnyCallApp(fake ? new[] { _fakeApp! } : _snapshot.Microphone.Select(a => a.Name)));
        var name = _fakeApp ?? Describe(_snapshot.Microphone);
        if (name is null)
        {
            _content = null;
            _ctx.Remove(ActivityId);
            return;
        }
        bool muted = fake ? _fakeMuted : _mic.Muted;
        bool camera = fake ? _fakeCamera : _snapshot.Camera.Count > 0;
        if (_content is null || _content.AppName != name || _ctx.Get(ActivityId) is null)
        {
            // A new payload instance rebuilds the ears (and resizes the pill for the new name).
            _content = new MeetingContent(name, muted, camera);
            _ctx.Post(new Activity(ActivityId, ActivityKind.Custom, Priority, _content) { Click = OnClick });
        }
        else
        {
            _content.Set(muted, camera);
        }
    }

    static string? Describe(IReadOnlyList<CapabilityApp> apps)
    {
        var names = apps.Select(a => a.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return names.Count switch
        {
            0 => null,
            1 => names[0],
            _ => $"{names[0]} +{names.Count - 1}",
        };
    }

    void OnClick()
    {
        if (_fakeApp is not null) _fakeMuted = !_fakeMuted;
        else _mic?.Toggle();
        Refresh();
    }

    CommandResult RunMic(MicAction action)
    {
        if (_mic is null) return new CommandResult("", "No microphone found", IsError: true);
        bool target = action switch
        {
            MicAction.Mute => true,
            MicAction.Unmute => false,
            _ => !_mic.Muted,
        };
        if (_mic.Set(target) is not bool muted) return new CommandResult("", "No microphone found", IsError: true);
        Refresh();
        return muted
            ? new CommandResult("", "Microphone muted", _mic.DeviceName)
            : new CommandResult("", "Microphone on", _mic.DeviceName);
    }

    string Fake(string arg)
    {
        var words = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        _fakeCamera = words.RemoveAll(w => w.Equals("+cam", StringComparison.OrdinalIgnoreCase)) > 0;
        _fakeScreen = words.RemoveAll(w => w.Equals("+screen", StringComparison.OrdinalIgnoreCase)) > 0;
        _fakeApp = words.Count > 0 ? string.Join(' ', words) : "Zoom";
        _fakeMuted = false;
        Refresh();
        return $"ok: {_fakeApp} camera={_fakeCamera} screen={_fakeScreen}";
    }

    string State()
    {
        if (_watcher is null || _mic is null) return "error: not started";
        var now = _watcher.Scan();
        static string List(IReadOnlyList<CapabilityApp> apps) =>
            string.Join(", ", apps.Select(a => $"{a.Name} ({a.Source}: {a.Path ?? a.Key})"));
        return $"mic=[{List(now.Microphone)}] camera=[{List(now.Camera)}] screen=[{List(now.Screen)}] " +
               $"presenting={Infrastructure.PrivacyState.Presenting} muted={_mic.Muted} " +
               $"device={_mic.DeviceName ?? "-"} notify={_watcher.UsingNotifications} " +
               $"activity={(_ctx?.Get(ActivityId) is null ? "hidden" : "shown")} fake={_fakeApp ?? "-"} fakeMuted={_fakeMuted}";
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _mic?.Dispose();
        _ctx?.Remove(ActivityId);
    }

    sealed class MicCommand : ITextCommand
    {
        readonly MeetingModule _owner;

        public MicCommand(MeetingModule owner) => _owner = owner;

        public bool CanHandle(string text) => Phrases.ParseMic(text) is not null;

        public Task<CommandResult> RunAsync(string text) =>
            Task.FromResult(Phrases.ParseMic(text) is MicAction action
                ? _owner.RunMic(action)
                : new CommandResult("", "Say mute, unmute or toggle mic", IsError: true));
    }
}
