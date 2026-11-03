using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shammy.Core.PhoneSend;
using Shammy.Motion;

namespace Shammy.Modules.PhoneSend;

/// <summary>
/// The transfer card: QR code on the left; file, instructions and live progress on the right.
/// Built in code like the compact views so the module stays one folder of .cs files.
/// </summary>
internal sealed class PhoneSendCard : Grid
{
    public const double CardWidth = 460, CardHeight = 230;
    const double Tile = 190;

    // Inverted (light-on-dark) codes look more at home in the notch, but plenty of phone scanners
    // (ZXing-based apps, older camera stacks) only read dark-on-light. Scanning has to just work.
    const bool InvertedQr = false;

    readonly TextBlock _hint, _url, _progressText, _percentText, _received;
    readonly Border _track, _fill;
    readonly StackPanel _waiting, _progress, _sent;
    readonly SpringValue _level = new(0, 0.001);
    IDisposable? _frames;

    public PhoneSendCard(string url, string fileName, long size)
    {
        Width = CardWidth;
        Height = CardHeight;
        Background = Brushes.Transparent;
        Focusable = true;
        FocusVisualStyle = null;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Tile + 20) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Children.Add(BuildTile(url));

        var right = new Grid { Margin = new Thickness(0, 18, 18, 18) };
        SetColumn(right, 2);
        for (int i = 0; i < 6; i++)
            right.RowDefinitions.Add(new RowDefinition { Height = i == 3 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        Children.Add(right);

        // Header: what this is, plus close.
        var header = Row(Glyph("", 12, Res("Fg3")), Text("Send to phone", 11.5, Res("Fg3"), margin: new Thickness(6, 0, 0, 0)));
        header.VerticalAlignment = VerticalAlignment.Center;
        right.Children.Add(header);
        var close = new Button
        {
            Style = (Style)Application.Current.Resources["IconButton"],
            Content = "",
            FontSize = 10,
            Width = 26,
            Height = 26,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -5, -8, 0),
            ToolTip = "Stop sharing",
        };
        close.Click += (_, _) => CloseRequested?.Invoke();
        right.Children.Add(close);

        var name = Text(fileName, 15.5, Res("Fg"), FontWeights.SemiBold, new Thickness(0, 10, 0, 0));
        name.TextWrapping = TextWrapping.Wrap;
        name.MaxHeight = 42;
        name.ToolTip = fileName;
        SetRow(name, 1);
        right.Children.Add(name);

        var sizeText = Text(ByteSize.Format(size), 12.5, Res("Fg2"), margin: new Thickness(0, 2, 0, 0));
        SetRow(sizeText, 2);
        right.Children.Add(sizeText);

        _received = Text("", 11.5, Res("Green"), margin: new Thickness(0, 0, 0, 8));
        _received.Visibility = Visibility.Collapsed;
        SetRow(_received, 4);
        right.Children.Add(_received);

        // Bottom: instructions, swapped for progress and then "Sent".
        var status = new Grid { MinHeight = 44, VerticalAlignment = VerticalAlignment.Bottom };
        SetRow(status, 5);
        right.Children.Add(status);

        _hint = Text("Scan with your phone on the same Wi-Fi", 12, Res("Fg2"));
        _hint.TextWrapping = TextWrapping.Wrap;
        // The token is the only thing that lets someone in, so the card shows just the address;
        // the QR code carries the full link.
        _url = Text(MaskToken(url), 10.5, Res("Fg3"), margin: new Thickness(0, 4, 0, 0));
        _waiting = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        _waiting.Children.Add(_hint);
        _waiting.Children.Add(_url);
        status.Children.Add(_waiting);

        _fill = new Border { Height = 5, Width = 0, CornerRadius = new CornerRadius(2.5), HorizontalAlignment = HorizontalAlignment.Left };
        _fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        _track = new Border { Height = 5, CornerRadius = new CornerRadius(2.5), Background = Res("Fill2"), Child = _fill };
        _progressText = Text("", 11.5, Res("Fg2"));
        _percentText = Text("", 11.5, Res("Fg"), FontWeights.SemiBold);
        _percentText.HorizontalAlignment = HorizontalAlignment.Right;
        var labels = new Grid { Margin = new Thickness(0, 7, 0, 0) };
        labels.Children.Add(_progressText);
        labels.Children.Add(_percentText);
        _progress = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 2) };
        _progress.Children.Add(Text("Sending…", 12, Res("Fg"), FontWeights.SemiBold, new Thickness(0, 0, 0, 8)));
        _progress.Children.Add(_track);
        _progress.Children.Add(labels);
        status.Children.Add(_progress);

        _sent = Row(Glyph("", 18, Res("Green")), Text("Sent", 15, Res("Green"), FontWeights.SemiBold, new Thickness(8, 0, 0, 0)));
        _sent.VerticalAlignment = VerticalAlignment.Bottom;
        _sent.Visibility = Visibility.Collapsed;
        status.Children.Add(_sent);

        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            CloseRequested?.Invoke();
        };
        Loaded += (_, _) => Focus();
        Unloaded += (_, _) => StopFrames();
    }

    /// <summary>The close button or Escape.</summary>
    public event Action? CloseRequested;

    public void SetProgress(long sent, long total)
    {
        if (_sent.Visibility == Visibility.Visible) return;
        _waiting.Visibility = Visibility.Collapsed;
        _progress.Visibility = Visibility.Visible;
        double f = total <= 0 ? 1 : Math.Clamp(sent / (double)total, 0, 1);
        _progressText.Text = $"{ByteSize.Format(sent)} of {ByteSize.Format(total)}";
        _percentText.Text = $"{f:P0}";
        _level.AnimateTo(f, 0.35, 1);
        _frames ??= Animator.Subscribe(_ =>
        {
            _fill.Width = Math.Max(0, _level.Value * _track.ActualWidth);
            if (_level.IsSettled) StopFrames();
        });
    }

    public void ShowSent()
    {
        StopFrames();
        _waiting.Visibility = Visibility.Collapsed;
        _progress.Visibility = Visibility.Collapsed;
        _sent.Visibility = Visibility.Visible;
    }

    public void ShowReceived(string text)
    {
        _received.Text = text;
        _received.Visibility = Visibility.Visible;
    }

    void StopFrames()
    {
        _frames?.Dispose();
        _frames = null;
    }

    internal static string MaskToken(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? $"{uri.Scheme}://{uri.Authority}/••••" : "";

    static UIElement BuildTile(string url)
    {
        var fg = InvertedQr ? Colors.White : Colors.Black;
        var tileColor = InvertedQr ? Color.FromRgb(0x1C, 0x1C, 0x1E) : Colors.White;
        var image = new Image
        {
            Source = RenderQr(url, fg, tileColor),
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        return new Border
        {
            Width = Tile,
            Height = Tile,
            CornerRadius = new CornerRadius(20),
            Background = new SolidColorBrush(tileColor),
            Padding = new Thickness(8),
            Margin = new Thickness(20, 0, 0, 0),
            Child = image,
        };
    }

    static System.Windows.Media.Imaging.BitmapSource? RenderQr(string url, Color fg, Color bg)
    {
        // Several whole pixels per module at the drawn size, so nearest-neighbour scaling stays crisp.
        try { return QrBitmap.Render(url, 348, fg, bg, margin: 2); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ZXing.WriterException)
        {
            Infrastructure.Log.Warn("[phonesend] QR render failed", ex);
            return null;
        }
    }

    static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    static TextBlock Text(string text, double size, Brush brush, FontWeight? weight = null, Thickness? margin = null) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = brush,
        FontWeight = weight ?? FontWeights.Normal,
        Margin = margin ?? new Thickness(0),
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    static TextBlock Glyph(string glyph, double size, Brush brush) => new()
    {
        Text = glyph,
        FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
        FontSize = size,
        Foreground = brush,
        VerticalAlignment = VerticalAlignment.Center,
    };

    static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var c in children) row.Children.Add(c);
        return row;
    }
}
