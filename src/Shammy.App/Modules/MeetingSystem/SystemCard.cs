using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shammy.Core.Cards;
using Shammy.Core.MeetingSystem;
using Shammy.SystemInfo;
using Shammy.Views.Cards;

namespace Shammy.Modules.MeetingSystem;

/// <summary>What the System card shows, gathered by <see cref="SystemModule"/>.</summary>
internal sealed record SystemCardData(
    SystemSample Sample,
    TimeSpan? TimeLeft,
    DateTime? FullAt,
    IReadOnlyList<(DateTime At, int Percent)> BatteryHistory,
    IReadOnlyList<BluetoothAudioDevice> Headsets);

/// <summary>
/// The v2 battery / system card (Battery.dc.html): the laptop's battery ring, a ring per connected Bluetooth
/// headset (Windows reports one level per headset, so earbuds get one "Earbuds" ring rather than made-up
/// left/right numbers), and in the detail the time left, battery history since Shammy started, and CPU and
/// memory meters. Replaces the old System tab.
/// </summary>
internal sealed class SystemCard
{
    readonly Brush _accent = CardKit.Res("BatteryAccent"), _alarm = CardKit.Res("AlarmAccent"), _buds = CardKit.Res("CalendarAccent");
    readonly Grid _compact = new() { Width = 176, Height = NotchPalette.CardHeight };
    readonly Ring _laptop = new() { Width = 92, Height = 92, Thickness = 7.4 };
    readonly ContentControl _laptopGlyph = new() { HorizontalAlignment = HorizontalAlignment.Center };
    readonly TextBlock _laptopPercent = CardKit.Big("", 24);
    readonly TextBlock _laptopLabel = CardKit.Text("Laptop", 11, FontWeights.SemiBold, secondary: true);
    readonly StackPanel _budRings = new() { HorizontalAlignment = HorizontalAlignment.Center };
    readonly Border _footDot = new() { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _foot = CardKit.Text("", 12, FontWeights.SemiBold);

    SystemCardData? _data;
    Detail? _detail;
    bool _detailOpen;

    public SystemCard()
    {
        _laptop.Effect = CardKit.Shadow(CardKit.Color(NotchPalette.Battery), 7, 0.45);
        _laptopPercent.HorizontalAlignment = HorizontalAlignment.Center;
        var inside = CardKit.Column(1, _laptopGlyph, _laptopPercent);
        inside.HorizontalAlignment = HorizontalAlignment.Center;
        inside.VerticalAlignment = VerticalAlignment.Center;
        var dial = new Grid { Width = 92, Height = 92 };
        dial.Children.Add(_laptop);
        dial.Children.Add(inside);
        _laptopLabel.HorizontalAlignment = HorizontalAlignment.Center;
        var left = CardKit.Column(4, dial, _laptopLabel);

        var top = new DockPanel();
        DockPanel.SetDock(left, Dock.Left);
        top.Children.Add(left);
        _budRings.Margin = new Thickness(10, 0, 0, 0);
        top.Children.Add(_budRings);

        var foot = CardKit.Row(6, _footDot, _foot);
        foot.VerticalAlignment = VerticalAlignment.Bottom;
        var content = new Grid { Margin = CardKit.CompactPadding };
        content.Children.Add(top);
        content.Children.Add(foot);
        _compact.Children.Add(content);
        _compact.IsVisibleChanged += (_, _) => VisibilityChanged?.Invoke();
    }

    public FrameworkElement Compact => _compact;

    /// <summary>The compact or the detail went on or off screen (the module samples faster while one shows).</summary>
    public event Action? VisibilityChanged;

    public bool IsShowing => _compact.IsVisible || _detailOpen;

    public bool IsRelevant(bool hasBattery, int headsets) => hasBattery || headsets > 0;

    public void Show(SystemCardData data)
    {
        _data = data;
        var s = data.Sample;
        bool low = PowerText.IsLow(s.BatteryPercent, s.PluggedIn);
        if (s.HasBattery)
        {
            _laptop.Fill = low ? _alarm : _accent;
            _laptop.Value = CardMath.Percent(s.BatteryPercent);
            _laptopPercent.Text = $"{s.BatteryPercent}%";
            _laptopGlyph.Content = s.Charging
                ? CardKit.Icon(CardKit.Paths.Bolt, Brushes.Transparent, 14, 0, _accent)
                : CardKit.Icon(CardKit.Paths.Laptop, CardKit.Res("CardText2"), 16);
            _laptopLabel.Text = "Laptop";
        }
        else
        {
            // A desktop has no battery: the big ring shows the processor instead.
            _laptop.Fill = _accent;
            _laptop.Value = CardMath.Percent(s.Cpu);
            _laptopPercent.Text = SystemFormat.Percent(s.Cpu);
            _laptopGlyph.Content = CardKit.Icon(CardKit.Paths.Chip, CardKit.Res("CardText2"), 14);
            _laptopLabel.Text = "CPU";
        }

        _budRings.Children.Clear();
        foreach (var h in data.Headsets.Take(2))
            _budRings.Children.Add(BudRing(h, 40, 4, 12, 10));

        _foot.Text = FootText(data);
        _footDot.Background = low ? _alarm : _accent;
        _detail?.Show(data);
    }

    static string FootText(SystemCardData d)
    {
        var s = d.Sample;
        if (!s.HasBattery) return $"Memory {s.MemoryLoad}%";
        if (s.Charging) return PowerText.ChargingLine(d.FullAt);
        if (s.PluggedIn) return PowerText.Status(true, true, false, s.BatteryPercent);
        return d.TimeLeft is { } left ? PowerText.TimeLeft(left) : "On battery";
    }

    /// <summary>One headset ring: blue arc, the level inside, a name under it; "–" when Windows has no level.</summary>
    FrameworkElement BudRing(BluetoothAudioDevice h, double size, double thickness, double fontSize, double labelSize)
    {
        var ring = new Ring { Width = size, Height = size, Thickness = thickness, Fill = _buds, Value = CardMath.Percent(h.Battery ?? 0) };
        var pct = CardKit.Text(h.Battery is { } b ? (size > 50 ? $"{b}%" : b.ToString(CultureInfo.InvariantCulture)) : "\u2013", fontSize, FontWeights.Bold);
        pct.HorizontalAlignment = HorizontalAlignment.Center;
        var dial = new Grid { Width = size, Height = size };
        dial.Children.Add(ring);
        dial.Children.Add(pct);
        var name = CardKit.Text(ShortName(h.Name), labelSize, FontWeights.SemiBold, secondary: true);
        name.HorizontalAlignment = HorizontalAlignment.Center;
        name.MaxWidth = size + 30;
        var column = CardKit.Column(2, dial, name);
        column.Margin = new Thickness(0, 0, 0, 6);
        column.ToolTip = h.Battery is { } level ? $"{h.Name} \u00B7 {level}%" : $"{h.Name} \u00B7 battery not reported";
        return column;
    }

    /// <summary>Earbuds and headphones read better as what they are than by model name in a tiny ring label.</summary>
    static string ShortName(string name) =>
        name.Contains("bud", StringComparison.OrdinalIgnoreCase) || name.Contains("dopes", StringComparison.OrdinalIgnoreCase)
        || name.Contains("pods", StringComparison.OrdinalIgnoreCase) ? "Earbuds"
        : name.Length > 12 ? name[..11].TrimEnd() + "\u2026" : name;

    // ---- Detail ---------------------------------------------------------------------------------

    public FrameworkElement BuildDetail()
    {
        var detail = new Detail(this);
        _detail = detail;
        detail.Root.IsVisibleChanged += (_, _) =>
        {
            _detailOpen = detail.Root.IsVisible;
            if (_detailOpen) _detail = detail;
            else if (_detail == detail) _detail = null;
            VisibilityChanged?.Invoke();
        };
        if (_data is { } d) detail.Show(d);
        return detail.Root;
    }

    sealed class Detail
    {
        readonly SystemCard _card;
        readonly Ring _ring = new() { Width = 132, Height = 132, Thickness = 9.2 };
        readonly ContentControl _glyph = new() { HorizontalAlignment = HorizontalAlignment.Center };
        readonly TextBlock _percent = CardKit.Big("", 34), _ringSub = CardKit.Text("", 11, FontWeights.Medium, secondary: true);
        readonly TextBlock _time = CardKit.Text("", 20, FontWeights.Bold), _mode = CardKit.Text("", 12, FontWeights.Medium, secondary: true);
        readonly Grid _history = new() { Height = 40, Margin = new Thickness(0, 14, 0, 0) };
        readonly DockPanel _historyLabels = new() { Margin = new Thickness(0, 4, 0, 0) };
        readonly TextBlock _laptopHead = CardKit.Label("Laptop");
        readonly StackPanel _headsets = new();
        readonly StackPanel _meters = new() { Margin = new Thickness(0, 16, 0, 0) };
        readonly Border _top = new() { Background = CardKit.RowBg, CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 8, 10, 8), VerticalAlignment = VerticalAlignment.Bottom };

        public Detail(SystemCard card)
        {
            _card = card;
            _ring.Effect = CardKit.Shadow(CardKit.Color(NotchPalette.Battery), 9, 0.45);
            foreach (var t in new FrameworkElement[] { _percent, _ringSub }) t.HorizontalAlignment = HorizontalAlignment.Center;
            var inside = CardKit.Column(0, _glyph, _percent, _ringSub);
            inside.VerticalAlignment = VerticalAlignment.Center;
            var dial = new Grid { Width = 132, Height = 132 };
            dial.Children.Add(_ring);
            dial.Children.Add(inside);
            _mode.Margin = new Thickness(0, 2, 0, 0);
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0) };
            words.Children.Add(_time);
            words.Children.Add(_mode);
            words.Children.Add(_history);
            words.Children.Add(_historyLabels);
            var laptopRow = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            DockPanel.SetDock(dial, Dock.Left);
            laptopRow.Children.Add(dial);
            laptopRow.Children.Add(words);
            var laptop = new DockPanel { Width = 316 };
            DockPanel.SetDock(_laptopHead, Dock.Top);
            laptop.Children.Add(_laptopHead);
            laptop.Children.Add(laptopRow);

            var headsets = new Grid { Width = 228 };
            headsets.Children.Add(_headsets);

            var system = new Grid();
            var meters = new StackPanel();
            meters.Children.Add(CardKit.Label("System"));
            meters.Children.Add(_meters);
            system.Children.Add(meters);
            system.Children.Add(_top);

            var row = new DockPanel();
            foreach (var e in new FrameworkElement[] { laptop, CardKit.VerticalDivider(), headsets, CardKit.VerticalDivider() })
            {
                DockPanel.SetDock(e, Dock.Left);
                row.Children.Add(e);
            }
            row.Children.Add(system);
            Root = CardKit.DetailPanel(row);
        }

        public Border Root { get; }

        public void Show(SystemCardData d)
        {
            var s = d.Sample;
            bool low = PowerText.IsLow(s.BatteryPercent, s.PluggedIn);
            if (s.HasBattery)
            {
                _laptopHead.Text = "LAPTOP";
                _ring.Fill = low ? _card._alarm : _card._accent;
                _ring.Value = CardMath.Percent(s.BatteryPercent);
                _percent.Text = $"{s.BatteryPercent}%";
                _glyph.Content = s.Charging ? CardKit.Icon(CardKit.Paths.Bolt, Brushes.Transparent, 16, 0, _card._accent) : null;
                _ringSub.Text = s.Charging ? "Charging" : "Battery";
                _time.Text = s.Charging ? (d.FullAt is { } at ? PowerText.FullBy(at) : "Charging")
                    : s.PluggedIn ? PowerText.Status(true, true, false, s.BatteryPercent)
                    : d.TimeLeft is { } left ? PowerText.TimeLeft(left) : "Estimating time left\u2026";
                _mode.Text = s.PluggedIn ? "Plugged in" : "On battery";
            }
            else
            {
                _laptopHead.Text = "PROCESSOR";
                _ring.Fill = _card._accent;
                _ring.Value = CardMath.Percent(s.Cpu);
                _percent.Text = SystemFormat.Percent(s.Cpu);
                _glyph.Content = null;
                _ringSub.Text = "CPU";
                _time.Text = "No battery";
                _mode.Text = "Desktop power";
            }
            ShowHistory(d.BatteryHistory, s.HasBattery);
            ShowHeadsets(d.Headsets);
            ShowMeters(s);
        }

        void ShowHistory(IReadOnlyList<(DateTime At, int Percent)> history, bool hasBattery)
        {
            _history.Children.Clear();
            _historyLabels.Children.Clear();
            bool show = hasBattery && history.Count >= 2;
            _history.Visibility = _historyLabels.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show) return;
            var heights = CardMath.BarHeights(history.Select(h => (double)h.Percent).ToList(), floor: 0.35);
            var bars = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Columns = history.Count };
            for (int i = 0; i < history.Count; i++)
            {
                var bar = CardKit.Bar(_card._accent, double.NaN, 40 * heights[i], 2);
                bar.Margin = new Thickness(1.5, 0, 1.5, 0);
                bar.VerticalAlignment = VerticalAlignment.Bottom;
                bar.Opacity = 0.35 + 0.65 * i / Math.Max(1, history.Count - 1);
                bar.ToolTip = $"{history[i].At.ToLocalTime():h:mm tt} \u00B7 {history[i].Percent}%";
                bars.Children.Add(bar);
            }
            _history.Children.Add(bars);
            var first = CardKit.Text(history[0].At.ToLocalTime().ToString("h tt", CultureInfo.InvariantCulture), 10, FontWeights.Medium, secondary: true);
            var now = CardKit.Text("Now", 10, FontWeights.Medium, secondary: true);
            DockPanel.SetDock(now, Dock.Right);
            _historyLabels.Children.Add(now);
            _historyLabels.Children.Add(first);
        }

        void ShowHeadsets(IReadOnlyList<BluetoothAudioDevice> headsets)
        {
            _headsets.Children.Clear();
            if (headsets.Count == 0)
            {
                _headsets.Children.Add(CardKit.Label("Headphones"));
                var none = CardKit.Text("None connected", 13, FontWeights.Medium, secondary: true);
                none.Margin = new Thickness(0, 10, 0, 0);
                _headsets.Children.Add(none);
                return;
            }
            var first = headsets[0];
            _headsets.Children.Add(CardKit.Label(first.Name));
            var connected = CardKit.Row(6, CardKit.Icon(CardKit.Paths.Bluetooth, _card._buds, 12, 2.2), CardKit.Text("Connected \u00B7 Bluetooth", 12, FontWeights.Medium, secondary: true));
            connected.Margin = new Thickness(0, 2, 0, 0);
            _headsets.Children.Add(connected);
            var rings = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 22, 0, 0) };
            foreach (var h in headsets.Take(3))
            {
                var ring = _card.BudRing(h, 62, 5.4, 15, 11);
                ring.Margin = new Thickness(0, 0, 16, 0);
                rings.Children.Add(ring);
            }
            _headsets.Children.Add(rings);
            if (first.Battery is null)
            {
                var note = CardKit.Text("Battery level not reported", 11, FontWeights.Medium, secondary: true);
                note.Margin = new Thickness(0, 8, 0, 0);
                _headsets.Children.Add(note);
            }
        }

        void ShowMeters(SystemSample s)
        {
            _meters.Children.Clear();
            _meters.Children.Add(Meter("CPU", s.Cpu, SystemFormat.Percent(s.Cpu), $"CPU {SystemFormat.Percent(s.Cpu)}"));
            _meters.Children.Add(Meter("Memory", s.MemoryLoad, $"{s.MemoryLoad}%",
                $"{SystemFormat.Gigabytes(s.UsedMemory)} of {SystemFormat.Gigabytes(s.TotalMemory)} GB"));
            if (s.Top is { } top)
            {
                var name = CardKit.Text($"{AppNames.Friendly(top.Name)} uses the most", 12, FontWeights.SemiBold);
                var rest = CardKit.Text($" \u00B7 {SystemFormat.Percent(top.Percent)} CPU", 12, FontWeights.Medium, secondary: true);
                var line = CardKit.Row(8, CardKit.Icon(CardKit.Paths.Chip, CardKit.Res("CardText"), 12), CardKit.Row(0, name, rest));
                _top.Child = line;
                _top.Visibility = Visibility.Visible;
            }
            else _top.Visibility = Visibility.Collapsed;
        }

        FrameworkElement Meter(string name, double percent, string value, string tooltip)
        {
            var head = new DockPanel();
            var v = CardKit.Big(value, 20);
            DockPanel.SetDock(v, Dock.Right);
            head.Children.Add(v);
            head.Children.Add(CardKit.Text(name, 13, FontWeights.SemiBold));
            var track = new Grid { Height = 8, Margin = new Thickness(0, 6, 0, 0), ClipToBounds = true };
            track.Children.Add(new Border { CornerRadius = new CornerRadius(4), Background = CardKit.RingTrack });
            var fill = new Border { CornerRadius = new CornerRadius(4), Background = _card._accent, HorizontalAlignment = HorizontalAlignment.Left };
            track.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * CardMath.Percent(percent);
            track.Children.Add(fill);
            var column = CardKit.Column(0, head, track);
            column.Margin = new Thickness(0, 0, 0, 14);
            column.ToolTip = tooltip;
            return column;
        }
    }

    // ---- Test pipe --------------------------------------------------------------------------------

    public string DescribeCompact()
    {
        if (_data is not { } d) return "system: no sample yet";
        var s = d.Sample;
        var laptop = s.HasBattery ? $"laptop={s.BatteryPercent}%{(s.Charging ? " charging" : "")} ring={CardMath.Percent(s.BatteryPercent):0.00}" : $"cpu={SystemFormat.Percent(s.Cpu)} (no battery)";
        var buds = d.Headsets.Count == 0 ? "-" : string.Join(", ", d.Headsets.Take(2).Select(h => $"{ShortName(h.Name)} {(h.Battery is { } b ? b + "%" : "?")}"));
        return $"system: {laptop} headsets=[{buds}] foot=\"{FootText(d)}\" visible={_compact.IsVisible}";
    }

    public string DescribeDetail()
    {
        if (_data is not { } d) return "system detail: no sample yet";
        var s = d.Sample;
        var time = s.Charging ? (d.FullAt is { } at ? PowerText.FullBy(at) : "charging, no estimate")
            : s.PluggedIn ? "plugged in" : d.TimeLeft is { } left ? PowerText.TimeLeft(left) : "estimating";
        var buds = d.Headsets.Count == 0 ? "none" : string.Join(", ", d.Headsets.Select(h => $"{h.Name} {(h.Battery is { } b ? b + "%" : "not reported")}"));
        var top = s.Top is { } t ? $"{AppNames.Friendly(t.Name)} {SystemFormat.Percent(t.Percent)}" : "-";
        return $"system detail: battery={(s.HasBattery ? s.BatteryPercent + "%" : "none")} time=\"{time}\" history={d.BatteryHistory.Count} " +
               $"headsets=[{buds}] cpu={SystemFormat.Percent(s.Cpu)} memory={s.MemoryLoad}% ({SystemFormat.Gigabytes(s.UsedMemory)}/{SystemFormat.Gigabytes(s.TotalMemory)} GB) top={top} open={_detailOpen}";
    }
}
