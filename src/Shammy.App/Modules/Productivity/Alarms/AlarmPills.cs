using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Shammy.Core.Productivity;
using Shammy.Views.Screens;

namespace Shammy.Modules.Productivity.Alarms;

/// <summary>
/// The snoozed alarm in the compact notch: the pet dozing beside "SNOOZED" on the left, the countdown to the
/// next ring and a round "turn off for today" button on the right. Clicking the pill itself opens Alarms.
/// The countdown ticks only while the pill is on screen.
/// </summary>
internal sealed class SnoozedPill : ICompactContent
{
    readonly Alarm _alarm;
    readonly Action _turnOff;
    TrackedText? _countdown;
    DispatcherTimer? _tick;

    public SnoozedPill(Alarm alarm, Action turnOff)
    {
        _alarm = alarm;
        _turnOff = turnOff;
    }

    public Alarm Alarm => _alarm;

    public double Build(ContentControl left, ContentControl right)
    {
        var face = new PetFace(0.62, FaceMood.Sleep) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(-4, 0, 2, 0) };
        var label = ScreenKit.Label("Snoozed", size: 10.5);
        var l = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        l.Children.Add(face);
        l.Children.Add(label);
        left.Content = l;

        _countdown = ScreenKit.Display("", 14, Weight.Bold, 0);
        var off = ScreenKit.Round(ScreenKit.Paths.BellOff, _turnOff, 24, 12, ScreenKit.Tile,
            $"Turn off the {AlarmText.Time(_alarm)} alarm for today");
        off.Margin = new Thickness(8, 0, 0, 0);
        var r = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        r.Children.Add(_countdown);
        r.Children.Add(off);
        right.Content = r;
        if (_alarm.SnoozedUntilUtc is { } until) r.ToolTip = $"Rings again at {ClockPreference.Format(until.ToLocalTime())}";

        _tick?.Stop();
        _tick = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += (_, _) => Refresh();
        r.IsVisibleChanged += (_, _) =>
        {
            if (r.IsVisible)
            {
                Refresh();
                _tick.Start();
            }
            else _tick.Stop();
        };
        Refresh();
        _countdown.Text = "00:00";
        double width = Math.Max(Measure(l), Measure(r));
        Refresh();
        return Math.Ceiling(width) + 2;
    }

    void Refresh()
    {
        if (_countdown is null) return;
        var left = (_alarm.SnoozedUntilUtc ?? DateTime.UtcNow) - DateTime.UtcNow;
        _countdown.Text = TimeFormat.Countdown(left < TimeSpan.Zero ? TimeSpan.Zero : left);
    }

    /// <summary>For the pipe.</summary>
    public string Describe() => $"snoozed {_countdown?.Text ?? "-"}";

    static double Measure(FrameworkElement e)
    {
        e.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return e.DesiredSize.Width;
    }
}

/// <summary>
/// After Stop: the pet stretches and waves beside the time of day ("MORNING") with the temperature (or the
/// time when there's no weather), for a few seconds.
/// </summary>
internal sealed class StoppedPill(string greeting, string value) : ICompactContent
{
    public double Build(ContentControl left, ContentControl right)
    {
        var face = new PetFace(0.62, FaceMood.Stretch) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(-4, 0, 2, 0) };
        var l = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        l.Children.Add(face);
        l.Children.Add(ScreenKit.Label(greeting, size: 10.5));
        left.Content = l;
        var r = ScreenKit.Display(value, 14, Weight.Bold, 0);
        right.Content = r;
        l.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        r.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Ceiling(Math.Max(l.DesiredSize.Width, r.DesiredSize.Width)) + 2;
    }

    public string Describe() => $"stopped {greeting} {value}";
}
