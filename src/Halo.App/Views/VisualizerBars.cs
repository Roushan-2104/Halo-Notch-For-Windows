using System.Windows;
using System.Windows.Media;
using Halo.Core.Motion;
using Halo.Motion;

namespace Halo.Views;

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
        if (IsVisible && (_active || !_springs.All(s => s.IsSettled && Math.Abs(s.Value - IdleLevel) < 0.01)))
            _frames ??= Animator.Subscribe(OnFrame);
        else if (!IsVisible) Release();
    }

    void Release()
    {
        _frames?.Dispose();
        _frames = null;
    }

    void OnFrame(double dt)
    {
        _time += dt;
        bool allRest = true;
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
            _springs[i].Step(dt);
            if (!_springs[i].IsSettled) allRest = false;
        }
        InvalidateVisual();
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
