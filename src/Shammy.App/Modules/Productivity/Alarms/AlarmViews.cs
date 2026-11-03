using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shammy.Core.Productivity;
using Shammy.Modules.Kit;

namespace Shammy.Modules.Productivity.Alarms;

/// <summary>
/// What the alarm views need from <see cref="AlarmController"/>. The views below are deliberately plain (CardKit
/// pieces, no layout polish): the designed "Focus Wall" alarm screens replace them, against this interface.
/// </summary>
internal interface IAlarmHost
{
    IReadOnlyList<Alarm> Alarms { get; }
    Alarm? Ringing { get; }
    int DefaultSnoozeMinutes { get; }

    /// <summary>When it rings next (local time; a snooze counts), or null when off.</summary>
    DateTime? UpcomingLocal(Alarm a);

    void SetEnabled(Alarm a, bool on);
    void Delete(Alarm a);
    void Stop();
    void Snooze(int minutes);
}

/// <summary>
/// The Alarms card. Compact (176×176): the next alarm's time, its days and how long until it rings (or RINGING /
/// MISSED). Detail (924×248): every alarm with an on/off switch and delete.
/// </summary>
internal sealed class AlarmsCard
{
    const string Accent = NotchPalette.Alarm;
    readonly IAlarmHost _host;
    readonly Grid _compact = new() { Margin = new Thickness(14) };

    public AlarmsCard(IAlarmHost host)
    {
        _host = host;
        // "in 8 h 20 min" only needs to be right when it's looked at.
        _compact.IsVisibleChanged += (_, _) =>
        {
            if (_compact.IsVisible) Refresh();
        };
        Refresh();
    }

    public FrameworkElement Compact => _compact;

    (Alarm Alarm, DateTime At)? Next() =>
        _host.Alarms.Select(a => (Alarm: a, At: _host.UpcomingLocal(a)))
            .Where(x => x.At is not null)
            .OrderBy(x => x.At)
            .Select(x => ((Alarm, DateTime)?)(x.Alarm, x.At!.Value))
            .FirstOrDefault();

    public void Refresh()
    {
        if (_detail is { IsVisible: true } detail) detail.Content = DetailContent();
        _compact.Children.Clear();
        var stack = new StackPanel();
        _compact.Children.Add(stack);
        var now = DateTime.Now;
        var missed = _host.Alarms.FirstOrDefault(a => a.MissedUtc is not null);

        if (_host.Ringing is { } ringing)
        {
            stack.Children.Add(CardKit.Caption("Ringing", CardKit.Brush(Accent)));
            stack.Children.Add(TimeBig(ringing, 34));
            stack.Children.Add(Sub(ringing.Label.Length > 0 ? ringing.Label : "Alarm"));
            return;
        }
        if (Next() is not { } next)
        {
            stack.Children.Add(CardKit.Caption("Alarms"));
            stack.Children.Add(CardKit.Label("No alarms on", 15));
            stack.Children.Add(Sub("Say “alarm at 7 every weekday”"));
        }
        else
        {
            stack.Children.Add(CardKit.Caption(next.Alarm.IsSnoozed ? "Snoozed" : "Next alarm", CardKit.Brush(Accent)));
            var (time, suffix) = ClockPreference.Parts(next.At.Hour, next.At.Minute);
            var big = CardKit.Row(4, CardKit.Big(time, 34), CardKit.Label(suffix, 13, brush: CardKit.Text2));
            stack.Children.Add(big);
            stack.Children.Add(Sub(AlarmText.Repeat(next.Alarm, now, next.At)));
            stack.Children.Add(Sub(AlarmText.Until(next.At - now)));
        }
        if (missed is not null) stack.Children.Add(Sub(AlarmText.Missed(missed), Accent));
    }

    static FrameworkElement TimeBig(Alarm a, double size)
    {
        var (time, suffix) = ClockPreference.Parts(a.Hour, a.Minute);
        return CardKit.Row(4, CardKit.Big(time, size), CardKit.Label(suffix, 13, brush: CardKit.Text2));
    }

    static TextBlock Sub(string text, string? accent = null)
    {
        var t = CardKit.Label(text, 12, FontWeights.Medium, accent is null ? CardKit.Text2 : CardKit.Brush(accent));
        t.Margin = new Thickness(0, 4, 0, 0);
        return t;
    }

    // ---- Detail ----------------------------------------------------------------------------------

    ContentControl? _detail;

    /// <summary>A fresh detail view; it rebuilds itself on every change while it's showing.</summary>
    public FrameworkElement BuildDetail()
    {
        _detail = new ContentControl { Content = DetailContent() };
        return _detail;
    }

    FrameworkElement DetailContent()
    {
        var root = new Grid();
        var list = new WrapPanel { Orientation = Orientation.Horizontal };
        var now = DateTime.Now;
        var alarms = _host.Alarms
            .OrderByDescending(a => a.Enabled)
            .ThenBy(a => _host.UpcomingLocal(a) ?? DateTime.MaxValue)
            .ThenBy(a => a.Hour * 60 + a.Minute)
            .ToList();
        if (alarms.Count == 0)
        {
            root.Children.Add(new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    CardKit.Label("No alarms yet", 16),
                    Sub("Type or say “set an alarm for 7 am” or “wake me up at 6:30 every weekday”"),
                },
            });
            return root;
        }
        foreach (var a in alarms) list.Children.Add(Row(a, now));
        root.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = list,
        });
        return root;
    }

    FrameworkElement Row(Alarm a, DateTime now)
    {
        var upcoming = _host.UpcomingLocal(a);
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var time = TimeBig(a, 24);
        if (!a.Enabled) time.Opacity = 0.45;
        info.Children.Add(time);
        var what = AlarmText.Repeat(a, now, upcoming);
        if (a.Label.Length > 0) what += " · " + a.Label;
        if (a.IsSnoozed && a.SnoozedUntilUtc is { } s) what += $" · snoozed to {ClockPreference.Format(s.ToLocalTime())}";
        info.Children.Add(Sub(what));
        if (a.MissedUtc is not null) info.Children.Add(Sub("Missed", Accent));

        var toggle = CardKit.PillButton(a.Enabled ? "On" : "Off", () =>
        {
            _host.SetEnabled(a, !a.Enabled);
        }, background: a.Enabled ? CardKit.Tint(Accent, 0.25) : null, tip: a.Enabled ? "Turn off" : "Turn on");
        var delete = CardKit.RoundButton("", () => _host.Delete(a), 26, "Delete", 10);
        var buttons = CardKit.Row(6, toggle, delete);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;

        var grid = new Grid { Width = 270, Height = 58 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(info);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);
        return new Border
        {
            Background = CardKit.Raised,
            BorderBrush = CardKit.RaisedStroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 8, 10, 8),
            Margin = new Thickness(0, 0, 8, 8),
            Child = grid,
        };
    }

    // ---- Test pipes ------------------------------------------------------------------------------

    public string DescribeCompact()
    {
        if (_host.Ringing is { } r) return $"alarms: ringing {AlarmText.Time(r)}";
        return Next() is { } n
            ? $"alarms: next={ClockPreference.Format(n.At)} {AlarmText.Repeat(n.Alarm, DateTime.Now, n.At)} {AlarmText.Until(n.At - DateTime.Now)} count={_host.Alarms.Count}"
            : $"alarms: none on count={_host.Alarms.Count}";
    }

    public string DescribeDetail() =>
        "alarms detail: " + string.Join(" | ", _host.Alarms.Select(a =>
            $"{AlarmText.Time(a)} {AlarmText.Repeat(a.Days)}{(a.Label.Length > 0 ? " " + a.Label : "")} {(a.Enabled ? "on" : "off")}"));
}

/// <summary>
/// The ringing alarm, dropped out of the notch (over fullscreen apps too): the time, the label, Stop, and snooze
/// for 5, 10 or 15 minutes (or the default). Shown without taking focus; its buttons work by mouse.
/// </summary>
internal sealed class AlarmRingCard : Grid
{
    public const double CardWidth = 520, CardHeight = 112;

    public AlarmRingCard(Alarm alarm, int defaultSnooze, Action stop, Action<int> snooze)
    {
        Alarm = alarm;
        Width = CardWidth;
        Height = CardHeight;
        Background = Brushes.Transparent;

        var grid = new Grid { Margin = new Thickness(18, 14, 16, 14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Children.Add(grid);

        var icon = new Border
        {
            Width = 48,
            Height = 48,
            CornerRadius = new CornerRadius(24),
            Background = CardKit.Tint(NotchPalette.Alarm, 0.18),
            VerticalAlignment = VerticalAlignment.Center,
            Child = CardKit.Glyph("", 20, CardKit.Brush(NotchPalette.Alarm)),
        };
        grid.Children.Add(icon);

        var (time, suffix) = ClockPreference.Parts(alarm.Hour, alarm.Minute);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 12, 0) };
        text.Children.Add(CardKit.Row(4, CardKit.Big(time, 30), CardKit.Label(suffix, 13, brush: CardKit.Text2)));
        text.Children.Add(CardKit.Label(alarm.Label.Length > 0 ? alarm.Label : "Alarm", 13, FontWeights.Medium, CardKit.Text2));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var stopButton = CardKit.PillButton("Stop", stop, 32, background: CardKit.Brush(NotchPalette.Alarm),
            hover: CardKit.Tint(NotchPalette.Alarm, 0.8), fontSize: 13);
        stopButton.MinWidth = 120;
        var minutes = new SortedSet<int> { 5, 10, 15, defaultSnooze };
        var snoozes = CardKit.Row(6, minutes.Select(m => (UIElement)CardKit.PillButton($"{m} min", () => snooze(m), 26,
            tip: $"Snooze {m} minutes")).ToArray());
        snoozes.Margin = new Thickness(0, 8, 0, 0);
        var buttons = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(stopButton);
        buttons.Children.Add(snoozes);
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);
    }

    public Alarm Alarm { get; }
}
