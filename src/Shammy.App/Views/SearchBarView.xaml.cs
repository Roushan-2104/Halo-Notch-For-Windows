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

    internal event Action<string>? Submitted;

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
        Input.Text = text;
        Input.CaretIndex = text.Length;
        Wave.Active = false;
        Wave.Visibility = Visibility.Collapsed;
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
        UpdateFrames();
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

    void OnInputChanged(object sender, TextChangedEventArgs e) =>
        Placeholder.Visibility = Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        var text = Input.Text.Trim();
        if (text.Length > 0) Submitted?.Invoke(text);
    }
}
