namespace Shammy.Core.Notch;

public enum HoverDecision { None, Peek, Open }

public enum HoverStage { Resting, Peek, Open }

/// <summary>
/// Decides whether the pointer over the notch means "show me" or is just passing through (on its way to a
/// browser tab or the address bar). It must slow down and rest on the notch for <see cref="PeekDwell"/> to
/// show the peek, then rest <see cref="OpenDwell"/> more to open the notch. Any fast movement starts the
/// current wait again, so sweeping across the top of the screen never opens anything.
/// Feed it pointer samples (from mouse moves and a short timer while the pointer is over the notch).
/// </summary>
public sealed class HoverIntent
{
    /// <summary>Resting for this long on the notch shows the peek.</summary>
    public static readonly TimeSpan PeekDwell = TimeSpan.FromMilliseconds(250);

    /// <summary>Resting this much longer on the peek opens the full notch.</summary>
    public static readonly TimeSpan OpenDwell = TimeSpan.FromMilliseconds(700);

    /// <summary>Speed is averaged over this window, so one jittery sample doesn't count as moving.</summary>
    public static readonly TimeSpan SpeedWindow = TimeSpan.FromMilliseconds(100);

    /// <summary>Under this speed (DIPs per millisecond, i.e. 250 px/s) the pointer counts as resting.</summary>
    public const double RestSpeed = 0.25;

    static readonly TimeSpan MinSpan = TimeSpan.FromMilliseconds(30);

    readonly List<(double X, double Y, TimeSpan T)> _samples = new();
    TimeSpan? _slowSince;

    /// <summary>False over a pill that does something on click (a running timer): skip the peek and open
    /// only after the whole wait, so the click lands first.</summary>
    public bool AllowPeek { get; set; } = true;

    public HoverStage Stage { get; private set; }

    /// <summary>The latest speed estimate (DIPs per ms), for diagnostics.</summary>
    public double Speed { get; private set; }

    public void Reset()
    {
        _samples.Clear();
        _slowSince = null;
        Stage = HoverStage.Resting;
        Speed = 0;
    }

    /// <summary>The peek was shown some other way (a test command); waiting to open starts now.</summary>
    public void ForcePeek(TimeSpan now)
    {
        Stage = HoverStage.Peek;
        _slowSince = now;
    }

    public HoverDecision Sample(double x, double y, TimeSpan now)
    {
        if (Stage == HoverStage.Open) return HoverDecision.None;
        _samples.Add((x, y, now));
        _samples.RemoveAll(s => now - s.T > SpeedWindow);

        double path = 0;
        for (int i = 1; i < _samples.Count; i++)
        {
            double dx = _samples[i].X - _samples[i - 1].X, dy = _samples[i].Y - _samples[i - 1].Y;
            path += Math.Sqrt(dx * dx + dy * dy);
        }
        var span = now - _samples[0].T;
        Speed = path / Math.Max(span.TotalMilliseconds, MinSpan.TotalMilliseconds);

        if (Speed > RestSpeed)
        {
            _slowSince = null;
            return HoverDecision.None;
        }
        _slowSince ??= now;
        var rested = now - _slowSince.Value;

        if (Stage == HoverStage.Resting)
        {
            if (AllowPeek && rested >= PeekDwell)
            {
                Stage = HoverStage.Peek;
                _slowSince = now;
                return HoverDecision.Peek;
            }
            if (!AllowPeek && rested >= PeekDwell + OpenDwell)
            {
                Stage = HoverStage.Open;
                return HoverDecision.Open;
            }
        }
        else if (Stage == HoverStage.Peek && rested >= OpenDwell)
        {
            Stage = HoverStage.Open;
            return HoverDecision.Open;
        }
        return HoverDecision.None;
    }
}
