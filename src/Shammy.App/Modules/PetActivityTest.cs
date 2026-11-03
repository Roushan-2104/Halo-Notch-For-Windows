using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Shammy.Core.Activities;
using Shammy.Core.Notch;

namespace Shammy.Modules;

/// <summary>
/// "activity-test call|music|timer|rec|download|charging|earbuds|notify [on|off]": made-up live activities so the
/// pet's costumes and the action circle can be checked without a real call, song or download. Circle clicks act
/// on the fake state (mute, pause...), never on the real microphone or player.
/// </summary>
internal sealed class PetActivityTest
{
    const string Prefix = "test-pet.";

    readonly Action<Activity> _post;
    readonly Func<string, bool> _remove;
    readonly Dictionary<string, Fake> _live = new();
    readonly DispatcherTimer _tick;

    public PetActivityTest(Action<Activity> post, Func<string, bool> remove)
    {
        _post = post;
        _remove = remove;
        // Downloads creep up and timers count down by themselves, like the board's simulator.
        _tick = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Tick(), Dispatcher.CurrentDispatcher) { IsEnabled = false };
    }

    public static readonly string[] Kinds = { "call", "music", "timer", "rec", "download", "charging", "earbuds", "notify" };

    public string Run(string arg)
    {
        var words = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0 || words[0] == "list") return $"live=[{string.Join(",", _live.Keys)}] usage: activity-test {string.Join("|", Kinds)}|all [on|off]";
        string kind = words[0].ToLowerInvariant();
        bool on = words.Length < 2 || words[1] != "off";
        if (kind == "all" && !on)
        {
            foreach (var k in _live.Keys.ToList()) Off(k);
            return "ok: all off";
        }
        if (!Kinds.Contains(kind)) return $"error: usage activity-test {string.Join("|", Kinds)} [on|off]";
        if (!on)
        {
            Off(kind);
            return $"ok: {kind} off";
        }
        var fake = new Fake(kind, () => Repost(kind), () => Off(kind));
        _live[kind] = fake;
        Repost(kind);
        _tick.IsEnabled = true;
        return $"ok: {kind} on";
    }

    void Off(string kind)
    {
        _live.Remove(kind);
        _remove(Prefix + kind);
        if (_live.Count == 0) _tick.IsEnabled = false;
    }

    void Repost(string kind)
    {
        if (!_live.TryGetValue(kind, out var f)) return;
        var (priority, expires, breaks) = kind switch
        {
            "notify" => (80, DateTime.UtcNow.AddSeconds(12), false),
            "charging" => (70, DateTime.UtcNow.AddSeconds(6), false),
            "rec" => (66, (DateTime?)null, false),
            "call" => (65, null, false),
            "earbuds" => (60, DateTime.UtcNow.AddSeconds(6), false),
            "download" => (40, null, false),
            "timer" => (f.TimerLeft <= 0 ? 85 : 45, null, f.TimerLeft <= 0),
            _ => (20, null, false),
        };
        _post(new Activity(Prefix + kind, ActivityKind.Custom, priority, f, expires) { BreaksFullscreen = breaks, Click = f.Click });
    }

    void Tick()
    {
        foreach (var (kind, f) in _live.ToList())
        {
            if (f.Step()) Repost(kind);
        }
    }

    /// <summary>One made-up activity; its circle changes its own state.</summary>
    sealed class Fake : ICompactContent, IPetActivity
    {
        readonly string _kind;
        readonly Action _changed, _end;
        readonly DateTime _since = DateTime.UtcNow.AddSeconds(-761);
        bool _muted, _paused, _playing = true;
        int _download = 64;

        public Fake(string kind, Action changed, Action end)
        {
            _kind = kind;
            _changed = changed;
            _end = end;
            TimerLeft = 12;
        }

        public int TimerLeft { get; private set; }

        public Action? Click => _kind switch
        {
            "notify" or "download" => () => _end(),
            "timer" => () => { if (TimerLeft <= 0) _end(); },
            _ => null,
        };

        /// <returns>True when the change needs a re-post (a timer reaching zero raises its priority).</returns>
        public bool Step()
        {
            if (_kind == "timer" && !_paused && TimerLeft > 0) return --TimerLeft == 0;
            if (_kind == "download" && _download < 100)
            {
                _download = Math.Min(100, _download + 6);
                return _download == 100;
            }
            return false;
        }

        public PetActivityState Describe(DateTime utcNow) => _kind switch
        {
            "call" => new() { Costume = PetCostume.Call, Label = "Standup", Value = PetActivities.Clock(utcNow - _since), Muted = _muted },
            "music" => new() { Costume = PetCostume.Music, Playing = _playing, Label = _playing ? "Kesariya" : "Paused", Value = _playing ? "" : "1:58" },
            "timer" => TimerLeft > 0
                ? new() { Costume = PetCostume.Timer, Label = "Tea", Value = $"0:{TimerLeft:00}", Paused = _paused, Progress = TimerLeft / 12.0 }
                : new() { Costume = PetCostume.Timer, Label = "Tea’s ready", Value = "0:00", Done = true },
            "rec" => new() { Costume = PetCostume.Recording, Label = "Rec", Value = PetActivities.Clock(utcNow - _since.AddSeconds(569)), CanOpen = true },
            "download" => _download < 100
                ? new() { Costume = PetCostume.Download, Label = "Download", Value = $"{_download}%", Progress = _download / 100.0 }
                : new() { Costume = PetCostume.Download, Label = "Saved", Value = "Done", Done = true, Progress = 1, CanOpen = true },
            "charging" => new() { Costume = PetCostume.Charging, Label = "Charging", Value = "64%", Progress = 0.64 },
            "earbuds" => new() { Costume = PetCostume.Earbuds, Label = "Airdopes 311 Pro", Value = "60%", PlainLabel = true },
            _ => new() { Costume = PetCostume.Notify, Label = "WhatsApp", Value = PetActivities.NotifyValue("Riya", "WhatsApp", Infrastructure.PrivacyState.Presenting), CanOpen = true },
        };

        public void RunCircle(CircleAction action)
        {
            switch (action)
            {
                case CircleAction.Mute or CircleAction.Unmute: _muted = !_muted; break;
                case CircleAction.Pause or CircleAction.Play or CircleAction.Resume when _kind == "music": _playing = !_playing; break;
                case CircleAction.Pause or CircleAction.Resume: _paused = !_paused; break;
                case CircleAction.Dismiss or CircleAction.Open: _end(); return;
            }
            _changed();
        }

        /// <summary>A fake call talks for a second and a half, then listens.</summary>
        public double Level() =>
            _kind != "call" || _muted ? 0 : Environment.TickCount64 / 1500 % 2 == 0 ? Math.Abs(Math.Sin(Environment.TickCount64 / 120.0)) * 0.4 : 0;

        public double Build(ContentControl left, ContentControl right)
        {
            right.Content = new TextBlock { Text = $"test {_kind}", Foreground = System.Windows.Media.Brushes.White, VerticalAlignment = VerticalAlignment.Center };
            return 80;
        }
    }
}
