using System.Windows;
using System.Windows.Controls;
using Shammy.Core.Productivity;
using Shammy.Views;
using Shammy.Views.Cards;

namespace Shammy.Modules.Productivity.Alarms;

/// <summary>
/// The Alarms tile on the Focus Wall rail (on it while one rings, is snoozed or was missed): the next alarm's time as
/// a poster on the dark dial field — the meaning red while it rings — and, focused, its label, when it rings and
/// Stop (the primary control) while ringing. The ringing card and the Alarms detail are their own views.
/// </summary>
internal sealed class AlarmTile
{
    readonly IAlarmHost _host;
    readonly WallTile _tile = new();
    readonly TrackedText _time = CardKit.Poster("", 40), _suffix = CardKit.Label(""), _rest = CardKit.Label("");
    readonly TextBlock _title, _line;
    readonly ContentControl _primary = new() { Focusable = false, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 10, 0) };

    public AlarmTile(IAlarmHost host)
    {
        _host = host;
        _suffix.Margin = new Thickness(0, 6, 0, 0);
        var top = CardKit.Column(0, _time, _suffix);
        top.Margin = new Thickness(WallTile.Pad);
        top.HorizontalAlignment = HorizontalAlignment.Left;
        top.VerticalAlignment = VerticalAlignment.Top;
        _tile.Art.Children.Add(top);
        _tile.Rest.Children.Add(WallTile.BottomLeft(_rest));
        var words = WallTile.Words("", "", out _title, out _line);
        _tile.Lit.Children.Add(CardKit.At(words, HorizontalAlignment.Stretch, VerticalAlignment.Bottom, WallTile.Pad, 0, WallTile.Pad, 10));
        _tile.Lit.Children.Add(_primary);
        _tile.IsVisibleChanged += (_, _) =>
        {
            if (_tile.IsVisible) Refresh();
        };
        Refresh();
    }

    public FrameworkElement View => _tile;

    /// <summary>A ringing alarm beats everything; a snoozed or missed one is worth a look.</summary>
    public int Live => _host.Ringing is not null ? 100 : _host.Alarms.Any(a => a.IsSnoozed || a.MissedUtc is not null) ? 20 : 0;

    (Alarm Alarm, DateTime At)? Next() =>
        _host.Alarms.Select(a => (Alarm: a, At: _host.UpcomingLocal(a)))
            .Where(x => x.At is not null)
            .OrderBy(x => x.At)
            .Select(x => ((Alarm, DateTime)?)(x.Alarm, x.At!.Value))
            .FirstOrDefault();

    /// <summary>The peek: "Ringing · 7:00 · Wake up", with Stop; or the next alarm and when.</summary>
    public WallPeek? Peek()
    {
        if (_host.Ringing is { } r)
            return new WallPeek("Ringing", $"{AlarmText.Time(r)} {Label(r)}", "Stop it here, or snooze from the card", Fields.Alarm(), new WallAction("Stop", _host.Stop));
        if (Next() is not { } next) return null;
        return new WallPeek(next.Alarm.IsSnoozed ? "Snoozed" : "Next alarm", $"{AlarmText.Time(next.Alarm)} {Label(next.Alarm)}", AlarmText.Until(next.At - DateTime.Now), Fields.Timer(), null);
    }

    static string Label(Alarm a) => a.Label.Length > 0 ? a.Label : "Alarm";

    public void Refresh()
    {
        var now = DateTime.Now;
        _primary.Content = null;
        if (_host.Ringing is { } ringing)
        {
            _tile.Field = Fields.Alarm();
            Show(ringing.Hour, ringing.Minute);
            _rest.Text = "Ringing";
            _title.Text = Label(ringing);
            _line.Text = "Ringing now";
            _primary.Content = CardKit.Primary("Stop the alarm", CardKit.Glyph(CardKit.Paths.Stop, CardKit.OnAccent, 10), _host.Stop, 28);
            CardKit.Name(_tile, $"Alarm ringing: {Label(ringing)}");
            return;
        }
        _tile.Field = Fields.Timer();
        var missed = _host.Alarms.FirstOrDefault(a => a.MissedUtc is not null);
        if (Next() is { } next)
        {
            Show(next.At.Hour, next.At.Minute);
            _rest.Text = next.Alarm.IsSnoozed ? "Snoozed" : "Next alarm";
            _title.Text = Label(next.Alarm);
            _line.Text = missed is not null ? AlarmText.Missed(missed) : $"{AlarmText.Until(next.At - now)} · {AlarmText.Repeat(next.Alarm, now, next.At)}";
            CardKit.Name(_tile, $"Alarms: next {AlarmText.Time(next.Alarm)}, {AlarmText.Until(next.At - now)}");
        }
        else
        {
            _time.Text = "Off";
            _suffix.Text = "";
            _rest.Text = "Alarms";
            _title.Text = missed is not null ? "Missed alarm" : "No alarms on";
            _line.Text = missed is not null ? AlarmText.Missed(missed) : "Say “alarm at 7 every weekday”";
            CardKit.Name(_tile, "Alarms: none on");
        }
    }

    void Show(int hour, int minute)
    {
        var (time, suffix) = ClockPreference.Parts(hour, minute);
        _time.Text = time;
        _suffix.Text = suffix;
    }
}
