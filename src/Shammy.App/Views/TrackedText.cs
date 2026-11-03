using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Media;

namespace Shammy.Views;

/// <summary>
/// One line of letter-spaced text: the Focus Wall's small tracked uppercase labels ("RECENT", "SEND TO PHONE").
/// WPF's TextBlock has no letter-spacing, so this lays the glyphs out itself (each advance plus
/// <see cref="Tracking"/> × size) and trims with an ellipsis when it runs out of room. Screen readers get the
/// text as written, not shouted.
/// </summary>
internal sealed class TrackedText : FrameworkElement
{
    static readonly FrameworkPropertyMetadataOptions Layout = FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender;

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(TrackedText),
        new FrameworkPropertyMetadata("", Layout, (d, _) => ((TrackedText)d)._dirty = true));

    public static readonly DependencyProperty FontSizeProperty = DependencyProperty.Register(nameof(FontSize), typeof(double), typeof(TrackedText),
        new FrameworkPropertyMetadata(11.0, Layout, (d, _) => ((TrackedText)d)._dirty = true));

    public static readonly DependencyProperty FontFamilyProperty = DependencyProperty.Register(nameof(FontFamily), typeof(FontFamily), typeof(TrackedText),
        new FrameworkPropertyMetadata(null, Layout, (d, _) => ((TrackedText)d)._dirty = true));

    public static readonly DependencyProperty FontWeightProperty = DependencyProperty.Register(nameof(FontWeight), typeof(FontWeight), typeof(TrackedText),
        new FrameworkPropertyMetadata(FontWeights.Bold, Layout, (d, _) => ((TrackedText)d)._dirty = true));

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(TrackedText),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Extra space after each letter, in ems (the design's 0.12em labels, 0.1em chips).</summary>
    public static readonly DependencyProperty TrackingProperty = DependencyProperty.Register(nameof(Tracking), typeof(double), typeof(TrackedText),
        new FrameworkPropertyMetadata(0.12, Layout, (d, _) => ((TrackedText)d)._dirty = true));

    public static readonly DependencyProperty UppercaseProperty = DependencyProperty.Register(nameof(Uppercase), typeof(bool), typeof(TrackedText),
        new FrameworkPropertyMetadata(true, Layout, (d, _) => ((TrackedText)d)._dirty = true));

    GlyphRun? _run;
    FormattedText? _fallback;
    bool _dirty = true;
    Size _size;
    double _builtFor = double.NaN;

    public TrackedText()
    {
        SetResourceReference(FontFamilyProperty, "WallFont");
        SetResourceReference(ForegroundProperty, "WallText2");
        VerticalAlignment = VerticalAlignment.Center;
        SnapsToDevicePixels = true;
    }

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public FontWeight FontWeight { get => (FontWeight)GetValue(FontWeightProperty); set => SetValue(FontWeightProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public double Tracking { get => (double)GetValue(TrackingProperty); set => SetValue(TrackingProperty, value); }
    public bool Uppercase { get => (bool)GetValue(UppercaseProperty); set => SetValue(UppercaseProperty, value); }

    protected override Size MeasureOverride(Size available)
    {
        double max = double.IsInfinity(available.Width) ? double.PositiveInfinity : available.Width;
        if (_dirty || !_builtFor.Equals(max)) Build(max);
        return _size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // Narrower than measured (a star column shrank): trim to what is really there.
        if (finalSize.Width + 0.5 < _size.Width) Build(finalSize.Width);
        return finalSize;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var brush = Foreground ?? Brushes.White;
        if (_run is not null) dc.DrawGlyphRun(brush, _run);
        else if (_fallback is not null)
        {
            _fallback.SetForegroundBrush(brush);
            dc.DrawText(_fallback, new Point(0, 0));
        }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        // The glyph run bakes in pixels-per-dip; lay it out again for the new monitor.
        _dirty = true;
        InvalidateMeasure();
    }

    void Build(double maxWidth)
    {
        _dirty = false;
        _builtFor = maxWidth;
        _run = null;
        _fallback = null;
        string text = Uppercase ? (Text ?? "").ToUpper(CultureInfo.CurrentCulture) : Text ?? "";
        double size = FontSize;
        var family = FontFamily ?? new FontFamily("Segoe UI");
        var typeface = new Typeface(family, FontStyles.Normal, FontWeight, FontStretches.Normal);
        double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double lineHeight = Math.Ceiling(family.LineSpacing * size);
        if (text.Length == 0)
        {
            _size = new Size(0, lineHeight);
            return;
        }

        if (!typeface.TryGetGlyphTypeface(out var glyphs) || !text.All(c => glyphs.CharacterToGlyphMap.ContainsKey(c)))
        {
            // A letter the face lacks (an arrow, an emoji): plain text with fallback fonts beats a missing box.
            _fallback = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection, typeface, size, Foreground ?? Brushes.White, ppd)
            {
                Trimming = TextTrimming.CharacterEllipsis,
                MaxLineCount = 1,
            };
            if (!double.IsInfinity(maxWidth)) _fallback.MaxTextWidth = Math.Max(1, maxWidth);
            _size = new Size(Math.Ceiling(_fallback.WidthIncludingTrailingWhitespace), Math.Ceiling(_fallback.Height));
            return;
        }

        double track = Tracking * size;
        var indices = new List<ushort>(text.Length + 1);
        var advances = new List<double>(text.Length + 1);
        double width = 0;
        foreach (char c in text)
        {
            ushort g = glyphs.CharacterToGlyphMap[c];
            indices.Add(g);
            double a = glyphs.AdvanceWidths[g] * size + track;
            advances.Add(a);
            width += a;
        }
        width -= track; // no tracking after the last letter, so the label's box ends where its ink does

        if (width > maxWidth && glyphs.CharacterToGlyphMap.TryGetValue('…', out var ellipsis))
        {
            double ellipsisWidth = glyphs.AdvanceWidths[ellipsis] * size;
            while (indices.Count > 0 && width + ellipsisWidth > maxWidth)
            {
                width -= advances[^1];
                indices.RemoveAt(indices.Count - 1);
                advances.RemoveAt(advances.Count - 1);
            }
            indices.Add(ellipsis);
            advances.Add(ellipsisWidth);
            width += ellipsisWidth;
        }

        double baseline = glyphs.Baseline * size;
        // Centre the caps in the line box rather than sitting them on the font's tall ascender space.
        double top = Math.Max(0, (lineHeight - glyphs.Height * size) / 2);
        _run = new GlyphRun(glyphs, 0, false, size, (float)ppd, indices, new Point(0, top + baseline), advances,
            null, null, null, null, null, null);
        _size = new Size(Math.Ceiling(Math.Max(0, width)), lineHeight);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    sealed class Peer : FrameworkElementAutomationPeer
    {
        public Peer(TrackedText owner) : base(owner) { }

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
        protected override string GetClassNameCore() => nameof(TrackedText);

        protected override string GetNameCore()
        {
            var name = base.GetNameCore();
            return string.IsNullOrEmpty(name) ? ((TrackedText)Owner).Text ?? "" : name;
        }
    }
}
