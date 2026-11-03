using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Documents;
using System.Windows.Media;

namespace Shammy.Views.Cards;

/// <summary>
/// One line of text with letter-spacing, which WPF's TextBlock can't do: the Focus Wall's tracked uppercase labels
/// (+0.12 em) and its tight poster titles and numbers (−0.035 em). Characters are drawn one by one; digits get the
/// same advance so numbers don't jiggle as they change (tabular figures). Too long for its room, it ends in "…".
/// </summary>
internal sealed class Tracked : FrameworkElement
{
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(Tracked),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    string _text = "";
    FontFamily _family = CardKit.Font;
    double _size = 11, _tracking, _lineFactor = 1.25;
    FontWeight _weight = FontWeights.Bold;
    bool _upper;

    // Laid-out glyphs for the current text, size and room.
    List<(FormattedText Text, double X)>? _runs;
    double _runWidth, _laidFor = -1;

    public Tracked()
    {
        SnapsToDevicePixels = true;
        IsHitTestVisible = false;
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public string Text
    {
        get => _text;
        set
        {
            value ??= "";
            if (_text == value) return;
            _text = value;
            Changed();
        }
    }

    public double FontSize { get => _size; set { _size = value; Changed(); } }
    public FontWeight FontWeight { get => _weight; set { _weight = value; Changed(); } }
    public FontFamily FontFamily { get => _family; set { _family = value; Changed(); } }

    /// <summary>Extra space between letters, in em (0.12 for labels, −0.035 for posters).</summary>
    public double Tracking { get => _tracking; set { _tracking = value; Changed(); } }

    /// <summary>Line height as a multiple of the size (0.9 for posters, like the mockup's line-height: .9).</summary>
    public double LineFactor { get => _lineFactor; set { _lineFactor = value; Changed(); } }

    /// <summary>Draw the text in capitals (labels), keeping <see cref="Text"/> as given for screen readers.</summary>
    public bool Uppercase { get => _upper; set { _upper = value; Changed(); } }

    void Changed()
    {
        _runs = null;
        _laidFor = -1;
        InvalidateMeasure();
        InvalidateVisual();
    }

    string Shown => _upper ? _text.ToUpper(CultureInfo.CurrentCulture) : _text;

    double LineHeight => Math.Ceiling(_size * _lineFactor);

    FormattedText Glyph(string s, double pixelsPerDip) => new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        new Typeface(_family, FontStyles.Normal, _weight, FontStretches.Normal), _size, Foreground ?? Brushes.White, pixelsPerDip);

    /// <summary>Positions every character; with less room than the whole line, cuts it and adds "…".</summary>
    void Lay(double room)
    {
        if (_runs is not null && Math.Abs(_laidFor - room) < 0.5) return;
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        string text = Shown;
        double track = _tracking * _size;
        double digit = 0;
        foreach (char d in "0123456789") digit = Math.Max(digit, Glyph(d.ToString(), dip).WidthIncludingTrailingWhitespace);

        List<(FormattedText, double)> Build(string s)
        {
            var list = new List<(FormattedText, double)>(s.Length);
            double x = 0;
            for (int i = 0; i < s.Length; i++)
            {
                // Keep surrogate pairs together.
                string ch = char.IsHighSurrogate(s[i]) && i + 1 < s.Length ? s.Substring(i++, 2) : s[i].ToString();
                var ft = Glyph(ch, dip);
                double adv = ft.WidthIncludingTrailingWhitespace;
                if (ch.Length == 1 && char.IsDigit(ch[0]))
                {
                    list.Add((ft, x + (digit - adv) / 2));
                    adv = digit;
                }
                else list.Add((ft, x));
                x += adv + (i < s.Length - 1 ? track : 0);
            }
            _runWidth = Math.Max(0, x);
            return list;
        }

        var runs = Build(text);
        if (_runWidth > room + 0.5 && text.Length > 1)
        {
            for (int n = text.Length - 1; n > 0; n--)
            {
                runs = Build(text[..n].TrimEnd() + "…");
                if (_runWidth <= room + 0.5) break;
            }
        }
        _runs = runs;
        _laidFor = room;
    }

    protected override Size MeasureOverride(Size available)
    {
        Lay(double.PositiveInfinity);
        double full = _runWidth;
        if (full > available.Width) Lay(available.Width);
        return new Size(Math.Min(full, available.Width), LineHeight);
    }

    protected override Size ArrangeOverride(Size final)
    {
        Lay(final.Width);
        return final;
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == ForegroundProperty) _runs = null;
    }

    protected override void OnRender(DrawingContext dc)
    {
        Lay(ActualWidth > 0 ? ActualWidth : double.PositiveInfinity);
        if (_runs is null || _runs.Count == 0) return;
        // Centre the glyphs' ink box (cap height to baseline) in the line, as CSS does with a tight line-height.
        double baseline = _runs[0].Text.Baseline;
        double y = (LineHeight - _size * 1.0) / 2 + _size * 0.86 - baseline;
        foreach (var (text, x) in _runs) dc.DrawText(text, new Point(x, y));
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new TrackedPeer(this);

    sealed class TrackedPeer(Tracked owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetNameCore() => owner.Text;
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
        protected override bool IsControlElementCore() => false;
    }
}
