namespace Halo.Core.Music;

/// <summary>
/// Turns a stream of horizontal wheel deltas (two-finger swipes arrive as many small ones) into at most
/// one "next"/"previous" per gesture. A gesture ends after a short quiet gap; a cooldown stops a long
/// swipe's momentum tail from skipping twice.
/// </summary>
public sealed class SwipeDetector
{
    public int Threshold { get; init; } = 120;
    public TimeSpan Cooldown { get; init; } = TimeSpan.FromMilliseconds(600);
    public TimeSpan GestureGap { get; init; } = TimeSpan.FromMilliseconds(250);

    int _accumulated;
    bool _fired;
    DateTime _lastEvent = DateTime.MinValue;
    DateTime _lastFire = DateTime.MinValue;

    public int Accumulated => _accumulated;

    /// <returns>+1 for a swipe right (next), -1 for left (previous), 0 for nothing yet.</returns>
    public int Add(int delta, DateTime now)
    {
        if (now - _lastEvent > GestureGap)
        {
            _accumulated = 0;
            _fired = false;
        }
        _lastEvent = now;
        if (_fired || delta == 0) return 0;

        // Reversing direction mid-gesture starts the count again rather than cancelling out slowly.
        if (_accumulated != 0 && Math.Sign(delta) != Math.Sign(_accumulated)) _accumulated = 0;
        _accumulated += delta;

        if (Math.Abs(_accumulated) < Threshold || now - _lastFire < Cooldown) return 0;
        int direction = Math.Sign(_accumulated);
        _fired = true;
        _lastFire = now;
        _accumulated = 0;
        return direction;
    }
}

/// <summary>Converts wheel deltas (120 per notch, smaller on precision touchpads) into whole steps.</summary>
public sealed class WheelStepper
{
    int _remainder;

    public int Add(int delta)
    {
        if (delta == 0) return 0;
        if (_remainder != 0 && Math.Sign(delta) != Math.Sign(_remainder)) _remainder = 0;
        _remainder += delta;
        int steps = _remainder / 120;
        _remainder -= steps * 120;
        return steps;
    }
}
