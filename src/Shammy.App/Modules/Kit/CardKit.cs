using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shammy.Shell;

namespace Shammy.Modules.Kit;

/// <summary>
/// Small builders for the v2 cards (calendar, mirror, clipboard, notes), so each card reads like the
/// mockup: text in the design's sizes and colours, round buttons, pills and tiles. Code-built rather
/// than XAML because each card is a handful of elements that change with live data.
/// </summary>
internal static class CardKit
{
    static readonly Dictionary<string, SolidColorBrush> Brushes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A frozen brush for "#RRGGBB" or "#AARRGGBB".</summary>
    public static SolidColorBrush Brush(string hex)
    {
        if (Brushes.TryGetValue(hex, out var b)) return b;
        b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        Brushes[hex] = b;
        return b;
    }

    /// <summary>The accent at an alpha (0–1), e.g. the 12 % purple behind a pill.</summary>
    public static SolidColorBrush Tint(string hex, double alpha)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        return Brush(Color.FromArgb((byte)Math.Round(alpha * 255), c.R, c.G, c.B).ToString());
    }

    public static readonly SolidColorBrush Text = Brush(Modules.NotchPalette.Text);
    public static readonly SolidColorBrush Text2 = Brush(Modules.NotchPalette.Text2);
    public static readonly SolidColorBrush Raised = Brush("#1A1A1D");
    public static readonly SolidColorBrush RaisedStroke = Brush("#26262B");
    public static readonly SolidColorBrush Round = Brush("#232326");
    public static readonly SolidColorBrush RoundHover = Brush("#2E2E33");
    public static readonly SolidColorBrush Track = Brush("#232326");

    public static FontFamily Font => (FontFamily)Application.Current.Resources["ShammyFont"];
    public static FontFamily DisplayFont => (FontFamily)Application.Current.Resources["ShammyDisplayFont"];
    public static FontFamily IconFont => (FontFamily)Application.Current.Resources["IconFont"];

    public static TextBlock Label(string text, double size = 13, FontWeight? weight = null, Brush? brush = null) => new()
    {
        Text = text,
        FontFamily = Font,
        FontSize = size,
        FontWeight = weight ?? FontWeights.SemiBold,
        Foreground = brush ?? Text,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>The design's tiny uppercase caption ("NEXT", "CLIPBOARD").</summary>
    public static TextBlock Caption(string text, Brush? brush = null)
    {
        var t = Label(text.ToUpperInvariant(), 11, FontWeights.SemiBold, brush ?? Text2);
        // WPF has no letter-spacing; a hair of tracking comes from the display font's metrics instead.
        t.FontFamily = DisplayFont;
        return t;
    }

    /// <summary>A big bold number ("10:30", "30°").</summary>
    public static TextBlock Big(string text, double size = 30) => new()
    {
        Text = text,
        FontFamily = DisplayFont,
        FontSize = size,
        FontWeight = FontWeights.Bold,
        Foreground = Text,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static TextBlock Glyph(string glyph, double size, Brush? brush = null) => new()
    {
        Text = glyph,
        FontFamily = IconFont,
        FontSize = size,
        Foreground = brush ?? Text,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static System.Windows.Shapes.Ellipse Dot(double size, Brush fill) => new() { Width = size, Height = size, Fill = fill, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>
    /// A clickable rounded element with a hover colour. Mouse-up runs <paramref name="onClick"/> and marks the
    /// event handled, so a button inside a card doesn't also open the card.
    /// </summary>
    public static Border Button(UIElement content, Action onClick, Brush background, Brush hover, double height,
        double? width = null, double radius = -1, string? tip = null)
    {
        var b = new Border
        {
            Height = height,
            CornerRadius = new CornerRadius(radius < 0 ? height / 2 : radius),
            Background = background,
            Cursor = Cursors.Hand,
            Child = content,
            ToolTip = tip,
            SnapsToDevicePixels = true,
        };
        if (width is { } w) b.Width = w;
        b.MouseEnter += (_, _) => b.Background = hover;
        b.MouseLeave += (_, _) => b.Background = background;
        b.MouseLeftButtonDown += (_, e) => e.Handled = true;
        b.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            onClick();
        };
        AutomationName(b, tip);
        return b;
    }

    /// <summary>A round icon button (36 px Back-style, 28 px −/+).</summary>
    public static Border RoundButton(string glyph, Action onClick, double size = 36, string? tip = null, double glyphSize = 13) =>
        Button(Glyph(glyph, glyphSize), onClick, Round, RoundHover, size, size, tip: tip);

    /// <summary>A text pill button ("Copy", "Close").</summary>
    public static Border PillButton(string text, Action onClick, double height = 26, string? glyph = null, Brush? background = null,
        Brush? hover = null, Brush? foreground = null, double fontSize = 11, string? tip = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (glyph is not null)
        {
            var g = Glyph(glyph, fontSize + 1, foreground ?? Text);
            g.Margin = new Thickness(0, 0, 6, 0);
            row.Children.Add(g);
        }
        var label = Label(text, fontSize, FontWeights.SemiBold, foreground ?? Text);
        row.Children.Add(label);
        var b = Button(row, onClick, background ?? Round, hover ?? RoundHover, height, tip: tip ?? text);
        b.Padding = new Thickness(glyph is null ? 10 : 11, 0, glyph is null ? 10 : 14, 0);
        return b;
    }

    /// <summary>A soft accent-tinted chip ("Pinned · 2").</summary>
    public static Border Chip(UIElement content, string accentHex, double height = 26) => new()
    {
        Height = height,
        CornerRadius = new CornerRadius(height / 2),
        Background = Tint(accentHex, 0.12),
        BorderBrush = Tint(accentHex, 0.35),
        BorderThickness = new Thickness(1),
        Padding = new Thickness(10, 0, 10, 0),
        Child = content,
    };

    /// <summary>A thin progress bar (track + fill), <paramref name="fraction"/> 0–1.</summary>
    public static Grid Progress(double fraction, Brush fill, double height = 4)
    {
        var g = new Grid { Height = height, VerticalAlignment = VerticalAlignment.Center };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Clamp(fraction, 0, 1), GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - Math.Clamp(fraction, 0, 1), GridUnitType.Star) });
        var track = new Border { CornerRadius = new CornerRadius(height / 2), Background = Track };
        Grid.SetColumnSpan(track, 2);
        g.Children.Add(track);
        if (fraction > 0) g.Children.Add(new Border { CornerRadius = new CornerRadius(height / 2), Background = fill });
        return g;
    }

    public static StackPanel Row(double gap, params UIElement[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < children.Length; i++)
        {
            if (i > 0 && children[i] is FrameworkElement f && gap > 0) f.Margin = new Thickness(gap, f.Margin.Top, f.Margin.Right, f.Margin.Bottom);
            p.Children.Add(children[i]);
        }
        return p;
    }

    public static void AutomationName(DependencyObject d, string? name)
    {
        if (!string.IsNullOrEmpty(name)) System.Windows.Automation.AutomationProperties.SetName(d, name);
    }

    /// <summary>
    /// Shows a card's detail as a popup card in the notch, for commands ("show my notes") until the notch
    /// can open a card's detail in place. Fits the window: the detail stretches to the width available.
    /// </summary>
    public static void ShowDetailPopup(ModuleContext ctx, FrameworkElement detail)
    {
        double width = Math.Min(Modules.NotchPalette.DetailWidth + 32, NotchWindow.WindowWidth - 24);
        var frame = new Border { Padding = new Thickness(16), Child = detail };
        detail.Width = double.NaN;
        ctx.ShowCard(frame, width, Modules.NotchPalette.DetailHeight + 32, interactive: true);
    }
}
