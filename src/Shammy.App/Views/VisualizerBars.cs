using System.Windows;
using System.Windows.Media;
using Shammy.Core.Motion;
using Shammy.Motion;

namespace Shammy.Views;

/// <summary>
/// Equalizer bars. Each bar chases its level with its own spring, which gives the lively, slightly
/// elastic motion of Apple's Now Playing bars rather than a jittery meter. Stops requesting frames once
/// the bars have settled while inactive.
/// </summary>
internal sealed class VisualizerBars : FrameworkElement
{
    const double BarWidth = 3;
    const double IdleLevel = 0.2;

    Spring[] _springs = Array.Empty<Spring>();
    readonly double[] _shape = { 0.75, 1.0, 0.85, 0.95, 0.7, 0.9, 0.8 };
    IDisposable? _frames;
    System.Windows.Threading.DispatcherTimer? _slow;
    readonly System.Diagnostics.Stopwatch _since = new();
    bool _active;
    Brush _brush = Brushes.White;
    double _time;

    public VisualizerBars()
    {
        BarCount = 4;
        Height = 14;
        IsVisibleChanged += (_, _) => UpdateSubscription();
        Unloaded += (_, _) => Release();
    }

    public int BarCount
    {
        get => _springs.Length;
        set
        {
            _springs = Enumerable.Range(0, value).Select(_ => new Spring(IdleLevel, 0.16, 0.62) { RestThreshold = 0.002 }).ToArray();
            Width = value * BarWidth + (value - 1) * 2.4;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Redraws at most this often (0 = every frame). The notch is a layered, software-rendered window where each
    /// frame costs real CPU, so the compact notch's small bars step a few times a second on their own timer
    /// instead of riding the render loop.
    /// </summary>
    public int MaxFps { get; set; }

    /// <summary>The visualizer feeding <see cref="Source"/>: told when these bars actually need levels.</summary>
    public Playback.AudioVisualizer? Feed { get; set; }

    /// <summary>Holds the bars still where they are (no settling, no repaints) until unfrozen.</summary>
    public bool Frozen
    {
        get => _frozen;
        set
        {
            _frozen = value;
            if (value) Release();
            else UpdateSubscription();
        }
    }

    bool _frozen;

    /// <summary>Level for bar i in 0..1.</summary>
    public Func<int, float>? Source { get; set; }

    public Color Color
    {
        set
        {
            var b = new SolidColorBrush(value);
            b.Freeze();
            _brush = b;
            InvalidateVisual();
        }
    }

    public bool Active
    {
        get => _active;
        set
        {
            _active = value;
            UpdateSubscription();
        }
    }

    void UpdateSubscription()
    {
        // Frozen between bursts still counts: restarting the loopback capture every few seconds costs more.
        Feed?.Want(this, IsVisible && _active);
        bool run = IsVisible && !_frozen && (_active || !_springs.All(s => s.IsSettled && Math.Abs(s.Value - IdleLevel) < 0.01));
        if (run && MaxFps > 0)
        {
            if (_slow is null)
            {
                _slow = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromSeconds(1.0 / MaxFps),
                };
                _slow.Tick += (_, _) =>
                {
                    double dt = Math.Min(0.25, _since.Elapsed.TotalSeconds);
                    _since.Restart();
                    OnFrame(dt);
                };
                _since.Restart();
                _slow.Start();
            }
        }
        else if (run) _frames ??= Animator.Subscribe(OnFrame);
        else if (!IsVisible) Release();
    }

    void Release()
    {
        if (!IsVisible) Feed?.Want(this, false);
        _frames?.Dispose();
        _frames = null;
        _slow?.Stop();
        _slow = null;
    }

    void OnFrame(double dt)
    {
        _time += dt;
        bool allRest = true, moved = false;
        for (int i = 0; i < _springs.Length; i++)
        {
            double target = IdleLevel;
            if (_active)
            {
                double level = Source?.Invoke(i) ?? 0;
                // A little per-bar wobble keeps quiet passages from looking frozen.
                double wobble = 0.08 * Math.Sin(_time * (5.3 + i * 1.7) + i);
                target = Math.Clamp(IdleLevel + (1 - IdleLevel) * level * _shape[i % _shape.Length] + wobble * level, IdleLevel, 1);
            }
            _springs[i].Target = target;
            double before = _springs[i].Value;
            _springs[i].Step(dt);
            if (Math.Abs(_springs[i].Value - before) > 0.04) moved = true;
            if (!_springs[i].IsSettled) allRest = false;
        }
        // Nothing visibly moved (silence, or settled): don't repaint.
        if (moved) InvalidateVisual();
        if (!_active && allRest) Release();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double h = ActualHeight > 0 ? ActualHeight : Height;
        double gap = _springs.Length > 1 ? (Width - _springs.Length * BarWidth) / (_springs.Length - 1) : 0;
        for (int i = 0; i < _springs.Length; i++)
        {
            double bh = Math.Max(BarWidth, _springs[i].Value * h);
            var rect = new Rect(i * (BarWidth + gap), (h - bh) / 2, BarWidth, bh);
            dc.DrawRoundedRectangle(_brush, null, rect, BarWidth / 2, BarWidth / 2);
        }
    }
}
