using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Shammy.Core.Activities;
using Shammy.Core.Ambient;

namespace Shammy.Modules.Ambient;

/// <summary>
/// Pet mode: a small creature in the compact notch's left ear at the lowest priority, so it only
/// appears when the notch has nothing else to say. Input idleness comes from GetLastInputInfo, polled
/// slowly; no keyboard hook, so it never sees what is typed.
/// </summary>
internal sealed class PetFeature : IDisposable
{
    const string ActivityId = "ambient.pet";
    const int Priority = 5;

    readonly ModuleContext _ctx;
    readonly AmbientData _data;
    readonly Action _save;
    readonly PetBrain _brain = new();
    readonly PetContent _content = new();
    readonly DispatcherTimer _poll;
    uint _lastInput;
    bool _shown;

    public PetFeature(ModuleContext ctx, AmbientData data, Action save)
    {
        _ctx = ctx;
        _data = data;
        _save = save;
        _poll = new DispatcherTimer(DispatcherPriority.Background, ctx.Ui) { Interval = TimeSpan.FromMilliseconds(250) };
        _poll.Tick += (_, _) => Poll();
    }

    public void Start()
    {
        if (_data.PetEnabled) Show();
    }

    public void SetEnabled(bool on)
    {
        if (on == _data.PetEnabled && on == _shown) return; // the tray menu re-applies its state on open
        _data.PetEnabled = on;
        _save();
        if (on) Show();
        else Hide();
    }

    void Show()
    {
        _shown = true;
        _lastInput = LastInputTick() ?? 0;
        Poll();
        _poll.Start();
        _ctx.Post(new Activity(ActivityId, ActivityKind.Custom, Priority, _content));
    }

    void Hide()
    {
        _shown = false;
        _poll.Stop();
        _ctx.Remove(ActivityId);
        _content.View.Stop();
    }

    void Poll()
    {
        if (LastInputTick() is not { } last) return;
        var idle = TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - last));
        bool input = last != _lastInput;
        _lastInput = last;
        bool music = _ctx.Services.Media.IsPlaying;
        if (_brain.Update(DateTime.UtcNow, idle, input, music) || _brain.JustWoke)
            _content.SetMood(_brain.Mood, _brain.JustWoke);
        _content.ShowNote(_brain.Mood == PetMood.Excited && music);

        // Fast sampling only matters while the pet is visible and awake (to notice busy typing).
        var interval = _brain.Mood == PetMood.Asleep || !_content.View.IsVisible ? TimeSpan.FromSeconds(1) : TimeSpan.FromMilliseconds(250);
        if (_poll.Interval != interval) _poll.Interval = interval;
    }

    /// <summary>Pipe hook: "awake", "sleep", "excited" or "auto". Shows the pet if it is hidden (not saved).</summary>
    public string Force(string arg)
    {
        switch (arg.Trim().ToLowerInvariant())
        {
            case "awake" or "wake" or "idle": _brain.Forced = PetMood.Awake; break;
            case "sleep" or "asleep": _brain.Forced = PetMood.Asleep; break;
            case "excited" or "happy": _brain.Forced = PetMood.Excited; break;
            case "auto" or "": _brain.Forced = null; break;
            default: return "error: use awake, sleep, excited or auto";
        }
        if (!_shown) Show();
        else Poll();
        return State;
    }

    public string State
    {
        get
        {
            var idle = LastInputTick() is { } last ? $"{unchecked((uint)Environment.TickCount - last) / 1000}s" : "?";
            return $"{(_data.PetEnabled ? "on" : _shown ? "shown (not saved)" : "off")} mood={_brain.Mood} " +
                   $"forced={_brain.Forced?.ToString() ?? "-"} idle={idle} visible={_content.View.IsVisible} animating={_content.View.IsAnimating}";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")]
    static extern bool GetLastInputInfo(ref LastInputInfo info);

    static uint? LastInputTick()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        return GetLastInputInfo(ref info) ? info.Time : null;
    }

    public void Dispose()
    {
        _poll.Stop();
        _content.View.Stop();
    }
}

/// <summary>The pet in the left ear; a tiny mood glyph (a note while music plays) in the right.</summary>
internal sealed class PetContent : ICompactContent
{
    const string NoteGlyph = "\uE8D6";

    readonly TextBlock _note = new()
    {
        FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
        FontSize = 11,
        Foreground = (Brush)Application.Current.Resources["Fg3"],
        VerticalAlignment = VerticalAlignment.Center,
    };

    PetView? _bubble;
    PetMood _current = PetMood.Awake;

    public PetView View { get; } = new();

    public double Build(ContentControl left, ContentControl right)
    {
        left.Content = View;
        right.Content = _note;
        return PetView.EarWidth;
    }

    /// <summary>A still, smaller pet for the detached bubble when something else owns the pill.</summary>
    public UIElement? BuildBubble()
    {
        _bubble ??= new PetView { Animated = false, LayoutTransform = new ScaleTransform(0.7, 0.7) };
        _bubble.SetMood(_current, false);
        return _bubble;
    }

    public void SetMood(PetMood mood, bool woke)
    {
        _current = mood;
        _bubble?.SetMood(mood, false);
        View.SetMood(mood, woke);
    }

    /// <summary>A little note in the right ear while it dances to music.</summary>
    public void ShowNote(bool on) => _note.Text = on ? NoteGlyph : "";
}
