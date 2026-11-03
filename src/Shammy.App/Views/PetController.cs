using System.Windows.Threading;
using Shammy.Core.Notch;
using Shammy.Infrastructure;
using Shammy.Modules;
using Shammy.Shell;

namespace Shammy.Views;

/// <summary>
/// Collects what the pet reacts to (voice phases, notifications, a finished timer, the calendar glance, music,
/// battery, time of day, the pointer, pats) and asks <see cref="PetMoods"/> for the mood whenever one changes.
/// Its own timer (for moods that wear off) runs only while the resting notch is on screen.
/// </summary>
internal sealed class PetController
{
    static readonly TimeSpan FastTick = TimeSpan.FromMilliseconds(250), SlowTick = TimeSpan.FromSeconds(10);

    readonly ShammyServices _services;
    readonly GlanceFeed _feed;
    readonly Action<PetMood> _apply;
    readonly DispatcherTimer _tick;
    NotchMode? _search;
    bool _timerDone, _seen;
    DateTime? _notifiedAt, _near, _petted;

    public PetController(ShammyServices services, GlanceFeed feed, Action<PetMood> apply)
    {
        _services = services;
        _feed = feed;
        _apply = apply;
        _tick = new DispatcherTimer(DispatcherPriority.Background) { Interval = SlowTick };
        _tick.Tick += (_, _) => Update();
    }

    public PetMood Mood { get; private set; } = PetMood.Idle;

    /// <summary>Set by the test pipe ("pet-mood music"); null follows the real inputs.</summary>
    public PetMood? Forced { get; set; }

    public string NotificationTitle { get; private set; } = "";
    public string NotificationApp { get; private set; } = "";

    /// <summary>The calendar glance that makes it "meeting soon", if any.</summary>
    public GlanceEntry? Meeting { get; private set; }

    public void SetActive(bool on)
    {
        if (on)
        {
            Update();
            _tick.Start();
        }
        else
        {
            _tick.Stop();
        }
    }

    public void SetSearch(NotchMode? mode)
    {
        _search = mode;
        Update();
    }

    public void SetTimerDone(bool done)
    {
        _timerDone = done;
        Update();
    }

    public void Notified(string app, string title)
    {
        _notifiedAt = DateTime.UtcNow;
        _seen = false;
        NotificationApp = app;
        NotificationTitle = title;
        Update();
    }

    /// <summary>The pointer came over the notch: a waiting notification or finished timer has been noticed.</summary>
    public void Noticed()
    {
        if (_seen && !_timerDone) return;
        _seen = true;
        _timerDone = false;
        Update();
    }

    public void PointerNear()
    {
        _near = DateTime.UtcNow;
        if (Mood == PetMood.Sleepy) Update();
    }

    public void Petted()
    {
        _petted = DateTime.UtcNow;
        Update();
    }

    public void Update()
    {
        var mood = Forced ?? PetMoods.Decide(Inputs());
        _tick.Interval = mood is PetMood.Happy or PetMood.Notify ? FastTick : SlowTick;
        if (mood == Mood) return;
        Mood = mood;
        _apply(mood);
    }

    PetInputs Inputs()
    {
        Meeting = _feed.Current().FirstOrDefault(e =>
            e.Kind == GlanceKind.Plain && string.Equals(e.Item.AccentHex, NotchPalette.Calendar, StringComparison.OrdinalIgnoreCase) &&
            PetMoods.IsMeetingSoon(e.Item.Text));

        int? battery = null;
        bool charging = false;
        try
        {
            var b = _services.Battery;
            if (b.HasBattery)
            {
                battery = b.Percent;
                charging = b.IsCharging;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Pet: reading the battery failed", ex);
        }

        return new PetInputs
        {
            Now = DateTime.UtcNow,
            LocalNow = DateTime.Now,
            Listening = _search is NotchMode.Listening or NotchMode.Circling,
            Thinking = _search is NotchMode.Thinking,
            TimerDone = _timerDone,
            NotifiedAt = _notifiedAt,
            NotificationSeen = _seen,
            MeetingSoon = Meeting is not null,
            MusicPlaying = _services.Media.IsPlaying,
            BatteryPercent = battery,
            Charging = charging,
            PointerNearAt = _near,
            PettedAt = _petted,
        };
    }
}
