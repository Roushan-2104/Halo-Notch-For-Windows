using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Halo.Modules.MeetingSystem;

/// <summary>
/// Compact ears while an app has the microphone: mic (green live / red slashed muted) and a camera
/// when one is on, with the app's name on the right. Mute and camera update in place.
/// </summary>
internal sealed class MeetingContent : ICompactContent
{
    const double MaxNameWidth = 120;

    bool _muted, _camera;
    TextBlock? _mic, _cam, _bubbleMic;
    FrameworkElement? _gap;

    public MeetingContent(string appName, bool muted, bool camera)
    {
        AppName = appName;
        _muted = muted;
        _camera = camera;
    }

    public string AppName { get; }

    public double Build(ContentControl left, ContentControl right)
    {
        _mic = Glyph(15);
        _gap = new FrameworkElement { Width = 9 };
        _cam = Glyph(15);
        _cam.Text = "";
        _cam.Foreground = Res("Fg");
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(_mic);
        row.Children.Add(_gap);
        row.Children.Add(_cam);
        left.Content = row;

        var name = new TextBlock
        {
            Text = AppName,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Res("Fg"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = MaxNameWidth,
        };
        right.Content = name;
        Apply();

        name.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        // Wide enough for mic + camera on the left even when the name is short.
        return Math.Max(44, Math.Min(MaxNameWidth, Math.Ceiling(name.DesiredSize.Width) + 2));
    }

    public UIElement? BuildBubble()
    {
        _bubbleMic = Glyph(13);
        Apply();
        return _bubbleMic;
    }

    public void Set(bool muted, bool camera)
    {
        if (muted == _muted && camera == _camera) return;
        _muted = muted;
        _camera = camera;
        Apply();
    }

    void Apply()
    {
        foreach (var mic in new[] { _mic, _bubbleMic })
        {
            if (mic is null) continue;
            mic.Text = _muted ? "" : "";
            mic.Foreground = Res(_muted ? "Red" : "Green");
        }
        var camVisibility = _camera ? Visibility.Visible : Visibility.Collapsed;
        if (_cam is not null) _cam.Visibility = camVisibility;
        if (_gap is not null) _gap.Visibility = camVisibility;
    }

    static TextBlock Glyph(double size) => new()
    {
        FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
        FontSize = size,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    static Brush Res(string key) => (Brush)Application.Current.Resources[key];
}
