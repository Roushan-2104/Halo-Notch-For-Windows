using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
/// The system card on the Focus Wall (Wall.dc.html): a graphite tile whose artwork is two tall meters — the
/// laptop's battery and a connected headset (or CPU and memory on a desktop) — with the battery as a poster;
/// focused, a line about the headset and a control to open the detail. The detail grows the meters into the art
/// panel, says how things are in words, and shows battery, headsets, CPU and memory as rings. Windows reports one
/// level per headset, so earbuds get one "Earbuds" level rather than made-up left/right numbers. A low battery is
/// the one place colour appears: the meaning red.
/// </summary>
internal sealed class SystemCard
{
    readonly WallTile _tile = new();
    readonly Grid _meters = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(14, 0, 0, WallTile.Pad) };
    readonly Tracked _big = CardKit.Poster("", 28), _restLabel = CardKit.Label("Battery");
    readonly TextBlock _litTitle, _litLine;

    SystemCardData? _data;
    Detail? _detail;
    bool _detailOpen;

    public SystemCard()
    {
        _tile.Field = Fields.System();
        _tile.Art.Children.Add(_meters);
        _big.HorizontalAlignment = HorizontalAlignment.Right;
        _tile.Art.Children.Add(CardKit.At(_big, HorizontalAlignment.Right, VerticalAlignment.Bottom, 0, 0, WallTile.Pad, WallTile.Pad + 20));
        _restLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _tile.Rest.Children.Add(CardKit.At(_restLabel, HorizontalAlignment.Right, VerticalAlignment.Bottom, 0, 0, WallTile.Pad, WallTile.Pad));
        var words = WallTile.Words("", "", out _litTitle, out _litLine);
        _litTitle.FontSize = 13;
        _litTitle.TextAlignment = _litLine.TextAlignment = TextAlignment.Right;
        _litTitle.HorizontalAlignment = _litLine.HorizontalAlignment = HorizontalAlignment.Right;
        words.HorizontalAlignment = HorizontalAlignment.Right;
        words.VerticalAlignment = VerticalAlignment.Bottom;
        words.Margin = new Thickness(0, 0, WallTile.Pad, WallTile.Pad - 2);
        words.MaxWidth = 92;
        _tile.Lit.Children.Add(words);
        _tile.Lit.Children.Add(WallTile.OpenButton("Open system details", () => Open?.Invoke()));
        _tile.IsVisibleChanged += (_, _) => VisibilityChanged?.Invoke();
    }

    public FrameworkElement Compact => _tile;

    /// <summary>Opens the system detail in the notch (the tile's and the peek's primary control).</summary>
    public Action? Open { get; set; }

    /// <summary>The tile or the detail went on or off screen (the module samples faster while one shows).</summary>
    public event Action? VisibilityChanged;

    public bool IsShowing => _tile.IsVisible || _detailOpen;

    public bool IsRelevant(bool hasBattery, int headsets) => hasBattery || headsets > 0;

    /// <summary>A low battery on battery power wants attention; otherwise the system is quiet.</summary>
    public int Live => _data is { } d && PowerText.IsLow(d.Sample.BatteryPercent, d.Sample.PluggedIn) && d.Sample.HasBattery ? 60 : 0;

    /// <summary>The peek: "System · Battery 81% · Earbuds 60% · Airdopes", with Details.</summary>
    public WallPeek? Peek()
    {
        if (_data is not { } d) return null;
        var s = d.Sample;
        string title = s.HasBattery ? $"Battery {s.BatteryPercent}%" : $"CPU {SystemFormat.Percent(s.Cpu)}";
        return new WallPeek("System", title, Line(d), Fields.System(), Open is { } open ? new WallAction("Details", open) : null);
    }

    /// <summary>"Earbuds 60% · Airdopes 311 Pro", else how the power is.</summary>
    static string Line(SystemCardData d)
    {
        if (d.Headsets.FirstOrDefault() is { } h)
            return $"{ShortName(h.Name)} {(h.Battery is { } b ? b + "%" : "connected")}" + (ShortName(h.Name) != h.Name ? $" · {h.Name}" : "");
        return FootText(d);
    }

    public void Show(SystemCardData data)
    {
        _data = data;
        var s = data.Sample;
        bool low = s.HasBattery && PowerText.IsLow(s.BatteryPercent, s.PluggedIn);
        _meters.Children.Clear();
        var levels = Levels(data).Take(2).ToList();
        for (int i = 0; i < levels.Count; i++)
        {
            var meter = CardKit.Meter(levels[i].Fraction, 28, 84, 7);
            if (i == 0 && low) ((Border)meter.Children[1]).Background = CardKit.Alarm;
            meter.HorizontalAlignment = HorizontalAlignment.Left;
            meter.Margin = new Thickness(i * 36, 0, 0, 0);
            meter.ToolTip = $"{levels[i].Name} {levels[i].Value}";
            _meters.Children.Add(meter);
        }
        _big.Text = s.HasBattery ? $"{s.BatteryPercent}%" : SystemFormat.Percent(s.Cpu);
        _restLabel.Text = s.HasBattery ? "Battery" : "CPU";
        _litTitle.Text = s.HasBattery ? "Battery" : "CPU";
        _litLine.Text = data.Headsets.FirstOrDefault() is { } h ? $"{ShortName(h.Name)} {(h.Battery is { } b ? b + "%" : "on")}" : FootText(data);
        CardKit.Name(_tile, $"System: {(s.HasBattery ? $"battery {s.BatteryPercent} percent" : $"CPU {SystemFormat.Percent(s.Cpu)}")}, {Line(data)}");
        _detail?.Show(data);
    }

    /// <summary>The meters, in order: the laptop battery (or the processor), headsets, then memory.</summary>
    static IEnumerable<(string Name, double Fraction, string Value, string Meta)> Levels(SystemCardData d)
    {
        var s = d.Sample;
        if (s.HasBattery) yield return ("Battery", CardMath.Percent(s.BatteryPercent), $"{s.BatteryPercent}%", FootText(d));
        else yield return ("CPU", CardMath.Percent(s.Cpu), SystemFormat.Percent(s.Cpu), s.Top is { } t ? $"{AppNames.Friendly(t.Name)} uses most" : "Processor");
        foreach (var h in d.Headsets.Take(2))
            yield return (ShortName(h.Name), CardMath.Percent(h.Battery ?? 0), h.Battery is { } b ? $"{b}%" : "–", h.Battery is null ? "Level not reported" : h.Name);
        if (s.HasBattery) yield return ("CPU", CardMath.Percent(s.Cpu), SystemFormat.Percent(s.Cpu), s.Top is { } t ? $"{AppNames.Friendly(t.Name)} uses most" : "Processor");
        yield return ("Memory", CardMath.Percent(s.MemoryLoad), $"{s.MemoryLoad}%", $"{SystemFormat.Gigabytes(s.UsedMemory)} of {SystemFormat.Gigabytes(s.TotalMemory)} GB");
    }

    static string FootText(SystemCardData d)
    {
        var s = d.Sample;
        if (!s.HasBattery) return $"Memory {s.MemoryLoad}%";
        if (s.Charging) return PowerText.ChargingLine(d.FullAt);
        if (s.PluggedIn) return PowerText.Status(true, true, false, s.BatteryPercent);
        return d.TimeLeft is { } left ? PowerText.TimeLeft(left) : "On battery";
    }

    /// <summary>Earbuds and headphones read better as what they are than by model name in a tiny label.</summary>
    static string ShortName(string name) =>
        name.Contains("bud", StringComparison.OrdinalIgnoreCase) || name.Contains("dopes", StringComparison.OrdinalIgnoreCase)
        || name.Contains("pods", StringComparison.OrdinalIgnoreCase) ? "Earbuds"
        : name.Length > 12 ? name[..11].TrimEnd() + "…" : name;

    // ---- Detail ---------------------------------------------------------------------------------

    public FrameworkElement BuildDetail()
    {
        var detail = new Detail();
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
        readonly Grid _meters = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(24, 0, 0, 20) };
        readonly Tracked _big = CardKit.Poster("", 52), _bigLabel = CardKit.Label("Battery");
        readonly UniformGrid _rings = new() { Columns = 2, VerticalAlignment = VerticalAlignment.Top };

        public Detail()
        {
            Root = new WallDetail(Fields.System(), "System", "", "");
            Root.Art.Children.Add(_meters);
            _big.HorizontalAlignment = _bigLabel.HorizontalAlignment = HorizontalAlignment.Right;
            _bigLabel.Margin = new Thickness(0, 6, 0, 0);
            var right = CardKit.Column(0, _big, _bigLabel);
            Root.Art.Children.Add(CardKit.At(right, HorizontalAlignment.Right, VerticalAlignment.Bottom, 0, 0, 20, 20));
            Root.Body.Children.Add(_rings);
        }

        public WallDetail Root { get; }

        public void Show(SystemCardData d)
        {
            var s = d.Sample;
            bool low = s.HasBattery && PowerText.IsLow(s.BatteryPercent, s.PluggedIn);
            var levels = Levels(d).ToList();
            _meters.Children.Clear();
            for (int i = 0; i < Math.Min(2, levels.Count); i++)
            {
                var meter = CardKit.Meter(levels[i].Fraction, 40, 150, 9);
                if (i == 0 && low) ((Border)meter.Children[1]).Background = CardKit.Alarm;
                meter.HorizontalAlignment = HorizontalAlignment.Left;
                meter.Margin = new Thickness(i * 50, 0, 0, 0);
                _meters.Children.Add(meter);
            }
            _big.Text = levels[0].Value;
            _bigLabel.Text = levels[0].Name;

            Root.TitleText.Text = low ? "Battery low" : s.Charging ? "Charging" : "All good";
            var parts = new List<string>();
            if (s.HasBattery)
            {
                parts.Add(s.PluggedIn ? "Plugged in" : "On battery");
                parts.Add(s.Charging ? (d.FullAt is { } at ? PowerText.FullBy(at).ToLowerInvariant() : "charging")
                    : s.PluggedIn ? (s.BatteryPercent >= 99 ? "fully charged" : PowerText.Status(true, true, false, s.BatteryPercent).ToLowerInvariant())
                    : d.TimeLeft is { } left ? PowerText.TimeLeft(left).ToLowerInvariant() : "estimating time left");
            }
            else parts.Add("Desktop power");
            foreach (var h in d.Headsets.Take(1)) parts.Add($"{h.Name} connected");
            Root.Line.Text = string.Join(" · ", parts);

            _rings.Children.Clear();
            foreach (var level in levels.Take(4)) _rings.Children.Add(RingRow(level.Name, level.Fraction, level.Value, level.Meta, low && level.Name == "Battery"));
        }

        static FrameworkElement RingRow(string name, double fraction, string value, string meta, bool alarm)
        {
            var ring = new Ring { Width = 50, Height = 50, Thickness = 5, Value = fraction, Track = CardKit.TrackBg, Fill = alarm ? CardKit.Alarm : CardKit.Ink };
            var pct = CardKit.Big(value.TrimEnd('%'), 13);
            pct.HorizontalAlignment = HorizontalAlignment.Center;
            var dial = new Grid { Width = 50, Height = 50 };
            dial.Children.Add(ring);
            dial.Children.Add(pct);
            var title = CardKit.Text(name, 14, FontWeights.Bold);
            var sub = CardKit.Text(meta, 12, FontWeights.Medium, secondary: true);
            var words = CardKit.Column(0, title, sub);
            words.VerticalAlignment = VerticalAlignment.Center;
            words.Margin = new Thickness(12, 0, 8, 0);
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(dial, Dock.Left);
            row.Children.Add(dial);
            row.Children.Add(words);
            row.ToolTip = $"{name} {value} · {meta}";
            return row;
        }
    }

    // ---- Test pipe --------------------------------------------------------------------------------

    public string DescribeCompact()
    {
        if (_data is not { } d) return "system: no sample yet";
        var s = d.Sample;
        var laptop = s.HasBattery ? $"laptop={s.BatteryPercent}%{(s.Charging ? " charging" : "")} meter={CardMath.Percent(s.BatteryPercent):0.00}" : $"cpu={SystemFormat.Percent(s.Cpu)} (no battery)";
        var buds = d.Headsets.Count == 0 ? "-" : string.Join(", ", d.Headsets.Take(2).Select(h => $"{ShortName(h.Name)} {(h.Battery is { } b ? b + "%" : "?")}"));
        return $"system: {laptop} headsets=[{buds}] foot=\"{FootText(d)}\" focused={_tile.IsLit} visible={_tile.IsVisible}";
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
