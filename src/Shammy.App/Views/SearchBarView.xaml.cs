using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Shammy.Motion;

namespace Shammy.Views;

/// <summary>The pill shown while listening, while circling, while searching, and for typed queries.</summary>
public partial class SearchBarView : UserControl
{
    readonly SpringValue _pulse = new(0.8, 0.002);
    IDisposable? _frames;
    Func<float>? _micLevel;

    public SearchBarView()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) => UpdateFrames();
    }

    /// <summary>Enter with no palette row chosen: the text goes to commands, the calculator or the web, as always.</summary>
    internal event Action<string>? Submitted;

    /// <summary>The typed text changed (also raised with "" when the box opens).</summary>
    internal event Action<string>? QueryChanged;

    /// <summary>The palette's "Search Google for …" row: the web, even if the text is also a command.</summary>
    internal event Action<string>? WebSearchRequested;

    /// <summary>A palette row did its thing itself (opened an app); show the result, or just close on null.</summary>
    internal event Action<Modules.CommandResult?>? ActionCompleted;

    /// <summary>The palette's height changed; the notch resizes the typing bar to fit.</summary>
    internal event Action<double>? PaletteHeightChanged;

    /// <summary>Gets first look at keys in the box (Up/Down/Enter for the palette); true when it used the key.</summary>
    internal Func<KeyEventArgs, bool>? PaletteKeys { get; set; }

    internal string Text => Input.Text;

    internal double PaletteHeight => PaletteHost.Height;

    internal void Submit(string text) => Submitted?.Invoke(text);
    internal void SearchWeb(string text) => WebSearchRequested?.Invoke(text);
    internal void Complete(Modules.CommandResult? result) => ActionCompleted?.Invoke(result);

    /// <summary>Show palette rows below the box (null or 0 hides them).</summary>
    internal void SetPalette(FrameworkElement? content, double height)
    {
        if (content is null) height = 0;
        if (!ReferenceEquals(PaletteHost.Content, content)) PaletteHost.Content = content;
        bool changed = Math.Abs(PaletteHost.Height - height) > 0.5;
        PaletteHost.Height = height;
        UpdatePaletteVisibility();
        if (changed) PaletteHeightChanged?.Invoke(height);
    }

    /// <summary>Test hook: type into the box as if from the keyboard.</summary>
    internal void SetText(string text)
    {
        Input.Text = text;
        Input.CaretIndex = text.Length;
    }

    /// <summary>Listening to the mic; <paramref name="micLevel"/> drives the orb pulse and the wave.</summary>
    internal void ShowListening(Func<float> micLevel, string primary, string secondary)
    {
        _micLevel = micLevel;
        SetState(glyph: "", spinning: false, typing: false);
        Primary.Text = primary;
        Secondary.Text = secondary;
        Wave.Source = i => micLevel() * (0.55f + 0.45f * (float)Math.Abs(Math.Sin(Environment.TickCount64 / 90.0 + i * 1.3)));
        Wave.Color = ((System.Windows.Media.SolidColorBrush)Application.Current.Resources["AccentBrush"]).Color;
        Wave.Active = true;
        Wave.Visibility = Visibility.Visible;
        UpdateFrames();
    }

    internal void ShowCircling()
    {
        _micLevel = null;
        SetState(glyph: "", spinning: false, typing: false);
        Primary.Text = "Circle anything";
        Secondary.Text = "Draw around it, or tap a word · Esc to cancel";
        Wave.Active = false;
        Wave.Visibility = Visibility.Collapsed;
    }

    internal void ShowThinking(string primary, string secondary = "")
    {
        _micLevel = null;
        SetState(glyph: null, spinning: true, typing: false);
        Primary.Text = primary;
        Secondary.Text = secondary;
        Secondary.Visibility = secondary.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Wave.Active = false;
        Wave.Visibility = Visibility.Collapsed;
    }

    internal void ShowMessage(string glyph, string primary, string secondary)
    {
        _micLevel = null;
        SetState(glyph, spinning: false, typing: false);
        Primary.Text = primary;
        Secondary.Text = secondary;
        Wave.Active = false;
        Wave.Visibility = Visibility.Collapsed;
    }

    internal void ShowTyping(string text = "")
    {
        _micLevel = null;
        SetState(glyph: "", spinning: false, typing: true);
        bool same = Input.Text == text;
        Input.Text = text;
        Input.CaretIndex = text.Length;
        Wave.Active = false;
        Wave.Visibility = Visibility.Collapsed;
        // Reopening with the same (empty) text raises no TextChanged; the palette still has to start over.
        if (same) QueryChanged?.Invoke(text);
    }

    internal void FocusInput()
    {
        Input.Focus();
        Keyboard.Focus(Input);
    }

    void SetState(string? glyph, bool spinning, bool typing)
    {
        OrbGlyph.Text = glyph ?? "";
        OrbGlyph.Visibility = spinning ? Visibility.Collapsed : Visibility.Visible;
        OrbCore.Visibility = spinning ? Visibility.Collapsed : Visibility.Visible;
        OrbPulse.Visibility = spinning ? Visibility.Collapsed : Visibility.Visible;
        Spin.Visibility = spinning ? Visibility.Visible : Visibility.Collapsed;
        Spin.Stroke = (System.Windows.Media.Brush)Application.Current.Resources["AccentBrush"];
        TextStack.Visibility = typing ? Visibility.Collapsed : Visibility.Visible;
        Secondary.Visibility = Visibility.Visible;
        InputHost.Visibility = typing ? Visibility.Visible : Visibility.Collapsed;
        EnterHint.Visibility = typing ? Visibility.Visible : Visibility.Collapsed;
        _typing = typing;
        UpdatePaletteVisibility();
        UpdateFrames();
    }

    bool _typing;

    void UpdatePaletteVisibility()
    {
        bool show = _typing && PaletteHost.Height > 0;
        PaletteHost.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        // With rows below, each row carries its own hint; the bar's "Enter" would point at two things.
        EnterHint.Visibility = _typing && !show ? Visibility.Visible : Visibility.Collapsed;
    }

    void UpdateFrames()
    {
        bool want = IsVisible && _micLevel is not null;
        if (want) _frames ??= Animator.Subscribe(OnFrame);
        else
        {
            _frames?.Dispose();
            _frames = null;
            _pulse.Snap(0.8);
            PulseScale.ScaleX = PulseScale.ScaleY = 0.8;
        }
    }

    void OnFrame(double dt)
    {
        float level = _micLevel?.Invoke() ?? 0;
        _pulse.AnimateTo(0.82 + Math.Min(0.5, level * 2.2), 0.18, 0.7);
        PulseScale.ScaleX = PulseScale.ScaleY = _pulse.Value;
    }

    void OnInputChanged(object sender, TextChangedEventArgs e)
    {
        Placeholder.Visibility = Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_typing) QueryChanged?.Invoke(Input.Text);
    }

    void OnInputPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_typing && PaletteKeys?.Invoke(e) == true) e.Handled = true;
    }

    void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        var text = Input.Text.Trim();
        if (text.Length > 0) Submitted?.Invoke(text);
    }
}
