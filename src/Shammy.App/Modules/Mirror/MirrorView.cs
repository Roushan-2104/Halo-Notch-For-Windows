using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Shammy.Core.Calendar;
using Shammy.Modules.Calendar;
using Shammy.Modules.Kit;

namespace Shammy.Modules.Mirror;

/// <summary>
/// The mirror's detail (924×248): status and the next-meeting hint on the left, the live 16:9 preview in the
/// middle, Flip and brightness on the right. Owns the camera: a 150 ms check while this view is visible starts
/// it when the view is really on screen and stops it as soon as the notch fades it, closes it or the PC locks.
/// </summary>
internal sealed class MirrorView : Grid
{
    readonly MirrorModule _module;
    readonly MirrorCamera _camera;
    readonly DispatcherTimer _watch;
    readonly Border _preview;
    readonly ImageBrush _brush = new() { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center };
    readonly ScaleTransform _flip = new(1, 1, 0.5, 0.5);
    readonly StackPanel _message;
    readonly TextBlock _messageText;
    readonly Border _settingsButton;
    readonly TextBlock _status;
    readonly Border _liveChip;
    readonly Border _flipButton;
    readonly TextBlock _flipText, _levelText;
    readonly Grid _levelBar = new() { Height = 16, VerticalAlignment = VerticalAlignment.Center };
    bool _starting, _wantRunning;

    public MirrorView(MirrorModule module, Dispatcher ui)
    {
        _module = module;
        _camera = new MirrorCamera(ui);
        _camera.BitmapChanged += bmp => _brush.ImageSource = bmp;
        _brush.RelativeTransform = _flip;

        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        MinHeight = NotchPalette.DetailHeight;

        // Left: status, the next meeting, the privacy note.
        var left = new DockPanel { LastChildFill = false };
        var head = new StackPanel();
        head.Children.Add(CardKit.Caption("Mirror"));
        _status = CardKit.Label("Starting…", 15, FontWeights.Bold);
        head.Children.Add(CardKit.Row(6, CardKit.Dot(8, CardKit.Brush(NotchPalette.Alarm)), _status));
        DockPanel.SetDock(head, Dock.Top);
        left.Children.Add(head);
        if (MeetingHint() is { } hint)
        {
            hint.Margin = new Thickness(0, 12, 0, 0);
            DockPanel.SetDock(hint, Dock.Top);
            left.Children.Add(hint);
        }
        var note = new DockPanel();
        var lockGlyph = CardKit.Glyph("", 12, CardKit.Text2);
        lockGlyph.VerticalAlignment = VerticalAlignment.Top;
        lockGlyph.Margin = new Thickness(0, 1, 7, 0);
        note.Children.Add(lockGlyph);
        var noteText = CardKit.Label("Camera stays on your PC; nothing is recorded.", 11, FontWeights.Normal, CardKit.Text2);
        noteText.TextWrapping = TextWrapping.Wrap;
        noteText.TextTrimming = TextTrimming.None;
        note.Children.Add(noteText);
        DockPanel.SetDock(note, Dock.Bottom);
        left.Children.Add(note);
        Children.Add(left);

        // Middle: the preview, its live chip, and a message when there's no picture.
        _preview = new Border
        {
            CornerRadius = new CornerRadius(24),
            Background = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#2B2622"), (Color)ColorConverter.ConvertFromString("#0B0B0E"), 60),
            BorderBrush = CardKit.Brush(NotchPalette.CardStroke),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var picture = new Border { CornerRadius = new CornerRadius(23), Background = _brush };
        _liveChip = new Border
        {
            CornerRadius = new CornerRadius(11),
            Background = CardKit.Brush("#99000000"),
            Padding = new Thickness(9, 4, 9, 4),
            Margin = new Thickness(12),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
            Child = CardKit.Row(6, CardKit.Dot(6, CardKit.Brush(NotchPalette.Alarm)), CardKit.Label("LIVE · NOT RECORDING", 10, FontWeights.SemiBold)),
        };
        _messageText = CardKit.Label("", 12.5, FontWeights.SemiBold);
        _messageText.TextWrapping = TextWrapping.Wrap;
        _messageText.TextTrimming = TextTrimming.None;
        _messageText.TextAlignment = TextAlignment.Center;
        _messageText.MaxWidth = 280;
        _settingsButton = CardKit.PillButton("Open camera settings", _module.OpenSettings, height: 28, fontSize: 12);
        _settingsButton.HorizontalAlignment = HorizontalAlignment.Center;
        _settingsButton.Margin = new Thickness(0, 10, 0, 0);
        _message = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        var camGlyph = CardKit.Glyph("", 22, CardKit.Text2);
        camGlyph.Margin = new Thickness(0, 0, 0, 8);
        _message.Children.Add(camGlyph);
        _message.Children.Add(_messageText);
        _message.Children.Add(_settingsButton);
        _preview.Child = new Grid { Children = { picture, _liveChip, _message } };
        var middle = new Grid();
        middle.Children.Add(_preview);
        middle.SizeChanged += (_, e) =>
        {
            // Centred 16:9, as big as the design's 368×207 and never taller than the area.
            double w = Math.Min(368, Math.Max(160, e.NewSize.Width - 8));
            double h = w * 9 / 16;
            if (h > e.NewSize.Height - 8)
            {
                h = Math.Max(90, e.NewSize.Height - 8);
                w = h * 16 / 9;
            }
            _preview.Width = w;
            _preview.Height = h;
        };
        SetColumn(middle, 1);
        Children.Add(middle);

        // Right: Flip and brightness.
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _flipButton = CardKit.RoundButton("", ToggleFlip, 36, "Flip");
        _flipText = CardKit.Label("", 11, FontWeights.Normal, CardKit.Text2);
        var flipRow = new DockPanel();
        DockPanel.SetDock(_flipText, Dock.Right);
        flipRow.Children.Add(_flipText);
        flipRow.Children.Add(CardKit.Row(10, _flipButton, CardKit.Label("Flip", 12, FontWeights.SemiBold)));
        right.Children.Add(flipRow);

        var levelHead = new DockPanel { Margin = new Thickness(0, 18, 0, 8) };
        _levelText = CardKit.Label("", 12, FontWeights.Bold);
        DockPanel.SetDock(_levelText, Dock.Right);
        levelHead.Children.Add(_levelText);
        levelHead.Children.Add(CardKit.Caption("Brightness"));
        right.Children.Add(levelHead);
        var levelRow = new DockPanel();
        var minus = CardKit.RoundButton("", () => Step(-10), 28, "Decrease brightness", 10);
        var plus = CardKit.RoundButton("", () => Step(10), 28, "Increase brightness", 10);
        DockPanel.SetDock(minus, Dock.Left);
        DockPanel.SetDock(plus, Dock.Right);
        _levelBar.Margin = new Thickness(8, 0, 8, 0);
        _levelBar.SizeChanged += (_, _) => PlaceKnob();
        levelRow.Children.Add(minus);
        levelRow.Children.Add(plus);
        levelRow.Children.Add(_levelBar);
        right.Children.Add(levelRow);
        SetColumn(right, 2);
        Children.Add(right);

        ApplyFlip();
        ApplyLevel();

        _watch = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Normal, (_, _) => Evaluate(), ui) { IsEnabled = false };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) _watch.Start();
            else _watch.Stop();
            Evaluate();
        };
        Unloaded += (_, _) => Shutdown();
    }

    public long Frames => _camera.Frames;
    public string FrameSize => _camera.Size;
    public MirrorProblem Problem { get; private set; }
    public string CameraState => _starting ? "starting" : _camera.Running ? "running" : "stopped";

    /// <summary>
    /// On screen for real: visible, not faded out by the notch (its layers fade before they collapse), and the
    /// PC isn't locked. Starts or stops the camera to match.
    /// </summary>
    public void Evaluate()
    {
        bool shown = IsVisible && !_module.Locked && EffectiveOpacity() >= 0.5;
        if (shown == _wantRunning) return;
        _wantRunning = shown;
        if (shown) _ = StartAsync();
        else _ = StopAsync();
    }

    double EffectiveOpacity()
    {
        double o = 1;
        for (DependencyObject? d = this; d is not null; d = VisualTreeHelper.GetParent(d))
            if (d is UIElement u) o *= u.Opacity;
        return o;
    }

    async Task StartAsync()
    {
        if (_starting) return;
        _starting = true;
        _status.Text = "Starting…";
        ShowMessage(null);
        var problem = await _camera.StartAsync();
        _starting = false;
        Problem = problem;
        if (!_wantRunning)
        {
            // Closed while the camera was still warming up: release it straight away.
            await _camera.StopAsync();
            UpdateStatus();
            return;
        }
        if (problem != MirrorProblem.None)
        {
            _module.Info($"Mirror: camera unavailable ({problem})");
            ShowMessage(problem switch
            {
                MirrorProblem.NoCamera => "No camera found",
                MirrorProblem.Denied => "Camera access is off for desktop apps. Turn it on in Settings › Privacy & security › Camera.",
                MirrorProblem.InUse => "The camera is in use by another app",
                _ => "Couldn't start the camera",
            }, problem == MirrorProblem.Denied);
        }
        UpdateStatus();
    }

    async Task StopAsync()
    {
        await _camera.StopAsync();
        _brush.ImageSource = null;
        UpdateStatus();
    }

    /// <summary>Releases the camera now (module shutdown, view thrown away).</summary>
    public void Shutdown()
    {
        _watch.Stop();
        _wantRunning = false;
        _ = StopAsync();
    }

    void UpdateStatus()
    {
        bool live = _camera.Running;
        _status.Text = live ? "Live" : Problem != MirrorProblem.None ? "Off" : _starting ? "Starting…" : "Paused";
        _liveChip.Visibility = live ? Visibility.Visible : Visibility.Collapsed;
    }

    void ShowMessage(string? text, bool settings = false)
    {
        _message.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        _messageText.Text = text ?? "";
        _settingsButton.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
    }

    void ToggleFlip()
    {
        _module.Store.Mirrored = !_module.Store.Mirrored;
        _module.Save();
        ApplyFlip();
    }

    void ApplyFlip()
    {
        bool mirrored = _module.Store.Mirrored;
        _flip.ScaleX = mirrored ? -1 : 1;
        _flipText.Text = mirrored ? "Mirrored" : "As others see you";
        _flipButton.Background = CardKit.Brush(mirrored ? "#3A3A40" : "#232326");
        _flipButton.ToolTip = mirrored ? "Flip off (show what others see)" : "Flip on (mirrored view)";
    }

    void Step(int delta)
    {
        _module.Store.Level = Math.Clamp(_module.Store.Level + delta, 0, 100);
        _module.Save();
        ApplyLevel();
    }

    void ApplyLevel()
    {
        int level = _module.Store.Level;
        // The design's scale: 0 % → 0.6×, 50 % → 1.0×, 100 % → 1.4×.
        _camera.Brightness = 0.6 + level / 100.0 * 0.8;
        _levelText.Text = $"{level}%";
        _levelBar.Children.Clear();
        _levelBar.Children.Add(CardKit.Progress(level / 100.0, CardKit.Text, 6));
        _levelBar.Children.Add(_knob);
        PlaceKnob();
    }

    readonly System.Windows.Shapes.Ellipse _knob = new() { Width = 16, Height = 16, Fill = CardKit.Text, HorizontalAlignment = HorizontalAlignment.Left };

    void PlaceKnob() =>
        _knob.Margin = new Thickness(Math.Clamp(_levelBar.ActualWidth * _module.Store.Level / 100.0 - 8, 0, Math.Max(0, _levelBar.ActualWidth - 16)), 0, 0, 0);

    /// <summary>"Ready for your 10:30 Standup" when a meeting starts within the hour.</summary>
    static Border? MeetingHint()
    {
        var now = DateTime.Now;
        if (CalendarAgenda.Next(CalendarModule.Agenda, now) is not { } next || next.Start - now > TimeSpan.FromMinutes(60)) return null;
        var text = CardKit.Label($"Ready for your {CalendarText.Clock(next.Start, CardKit.H24)} {next.Title}", 12, FontWeights.SemiBold);
        text.TextWrapping = TextWrapping.Wrap;
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        text.MaxHeight = 34;
        var row = new DockPanel();
        var glyph = CardKit.Glyph("", 14, CardKit.Brush(NotchPalette.Calendar));
        glyph.Margin = new Thickness(0, 0, 8, 0);
        row.Children.Add(glyph);
        row.Children.Add(text);
        return new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = CardKit.Tint(NotchPalette.Calendar, 0.12),
            BorderBrush = CardKit.Tint(NotchPalette.Calendar, 0.35),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8, 10, 8),
            Child = row,
        };
    }
}
