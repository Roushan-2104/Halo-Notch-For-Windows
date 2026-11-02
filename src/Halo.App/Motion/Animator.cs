using System.Windows.Media;
using Halo.Core.Motion;

namespace Halo.Motion;

/// <summary>A <see cref="Spring"/> driven by the shared <see cref="Animator"/>, with an optional start delay.</summary>
internal sealed class SpringValue
{
    readonly Spring _spring;
    double _delay;
    double _pendingTarget, _pendingResponse, _pendingDamping;
    bool _hasPending;

    public SpringValue(double value, double restThreshold = 0.05)
    {
        _spring = new Spring(value) { RestThreshold = restThreshold };
    }

    public double Value => _spring.Value;
    public double Target => _hasPending ? _pendingTarget : _spring.Target;
    public bool IsSettled => !_hasPending && _spring.IsSettled;

    public void AnimateTo(double target, double response = 0.45, double damping = 0.85, double delay = 0)
    {
        if (delay > 0)
        {
            _pendingTarget = target;
            _pendingResponse = response;
            _pendingDamping = damping;
            _hasPending = true;
            _delay = delay;
        }
        else
        {
            _hasPending = false;
            _spring.Response = response;
            _spring.DampingFraction = damping;
            _spring.Target = target;
        }
        Animator.Wake(this);
    }

    public void Snap(double value)
    {
        _hasPending = false;
        _spring.Snap(value);
        Animator.Kick();
    }

    /// <returns>true once the spring is at rest and can stop being stepped.</returns>
    internal bool Step(double dt)
    {
        if (_hasPending)
        {
            _delay -= dt;
            if (_delay > 0) return false;
            _hasPending = false;
            _spring.Response = _pendingResponse;
            _spring.DampingFraction = _pendingDamping;
            _spring.Target = _pendingTarget;
        }
        _spring.Step(dt);
        return _spring.IsSettled;
    }
}

/// <summary>
/// One frame clock for the whole app, tied to WPF's render loop so every spring steps exactly once per
/// displayed frame. It unhooks itself when nothing is moving, so an idle notch costs no CPU.
/// </summary>
internal static class Animator
{
    static readonly HashSet<SpringValue> Active = new();
    static readonly List<Action<double>> Subscribers = new();
    static bool _hooked, _kicked;
    static TimeSpan _last;

    /// <summary>Raised after springs step, on every frame while anything is animating.</summary>
    public static event Action<double>? Frame;

    public static string Diagnostics =>
        $"hooked={_hooked} springs={Active.Count} subscribers={Subscribers.Count} " +
        $"[{string.Join(",", Subscribers.Select(s => s.Target?.GetType().Name ?? "?"))}]";

    public static void Wake(SpringValue s)
    {
        Active.Add(s);
        Ensure();
    }

    /// <summary>Request one frame even if nothing is moving (e.g. after a snap).</summary>
    public static void Kick()
    {
        _kicked = true;
        Ensure();
    }

    /// <summary>Keep frames coming until the returned handle is disposed (for continuous visuals).</summary>
    public static IDisposable Subscribe(Action<double> onFrame)
    {
        Subscribers.Add(onFrame);
        Ensure();
        return new Subscription(onFrame);
    }

    static void Ensure()
    {
        if (_hooked) return;
        _hooked = true;
        _last = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    static void OnRendering(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _last) return; // Rendering can fire more than once per frame
        double dt = _last == TimeSpan.Zero ? 1.0 / 60 : (now - _last).TotalSeconds;
        _last = now;
        dt = Math.Clamp(dt, 0.0005, 0.05);
        _kicked = false;

        if (Active.Count > 0)
        {
            foreach (var s in Active.ToArray())
                if (s.Step(dt)) Active.Remove(s);
        }

        foreach (var sub in Subscribers.ToArray()) sub(dt);
        Frame?.Invoke(dt);

        if (Active.Count == 0 && Subscribers.Count == 0 && !_kicked)
        {
            CompositionTarget.Rendering -= OnRendering;
            _hooked = false;
        }
    }

    sealed class Subscription(Action<double> handler) : IDisposable
    {
        bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Subscribers.Remove(handler);
        }
    }
}
