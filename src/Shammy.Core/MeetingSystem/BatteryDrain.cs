namespace Shammy.Core.MeetingSystem;

/// <summary>
/// Estimates battery drain (%/hour) from the whole-percent readings Windows reports. Only the moments
/// the percentage ticks down are trusted: the time between two ticks is exact, whereas the time from
/// the first reading to the first tick depends on where inside that percent we started.
/// </summary>
public sealed class DrainEstimator
{
    readonly List<(DateTime At, int Percent)> _drops = new();
    readonly TimeSpan _window;
    int? _lastPercent;

    public DrainEstimator(TimeSpan? window = null) => _window = window ?? TimeSpan.FromMinutes(60);

    public void Add(DateTime now, int percent, bool pluggedIn)
    {
        if (pluggedIn)
        {
            Reset();
            return;
        }
        if (_lastPercent is int last)
        {
            if (percent < last) _drops.Add((now, percent));
            else if (percent > last) _drops.Clear(); // recalibration jump; old ticks no longer line up
        }
        _lastPercent = percent;
        // Keep the window recent but never fewer than two ticks, so slow drains still get an estimate.
        while (_drops.Count > 2 && now - _drops[0].At > _window) _drops.RemoveAt(0);
    }

    public double? RatePerHour(DateTime now)
    {
        if (_drops.Count < 2) return null;
        var first = _drops[0];
        var last = _drops[^1];
        double hours = (last.At - first.At).TotalHours;
        if (hours <= 0) return null;
        double dropped = first.Percent - last.Percent;
        double rate = dropped / hours;
        // No further tick by now means less than one more percent was used since: cap the rate so a
        // drain that just slowed down stops being reported at its old speed.
        double since = (now - first.At).TotalHours;
        if (since > hours) rate = Math.Min(rate, (dropped + 1) / since);
        return rate;
    }

    public void Reset()
    {
        _drops.Clear();
        _lastPercent = null;
    }

    /// <summary>Time until empty at the given drain; null when not draining meaningfully.</summary>
    public static TimeSpan? Remaining(int percent, double? ratePerHour)
    {
        if (ratePerHour is not double r || r < 0.2 || percent <= 0) return null;
        return TimeSpan.FromHours(Math.Min(percent / r, 48));
    }

    /// <summary>Drain from an instantaneous battery report: negative charge rate over full capacity.</summary>
    public static double? FromReport(int? chargeRateMilliwatts, int? fullCapacityMilliwattHours)
    {
        if (chargeRateMilliwatts is not int rate || fullCapacityMilliwattHours is not int full || full <= 0) return null;
        return -100.0 * rate / full;
    }
}

/// <summary>Exponential moving average whose smoothing depends on elapsed time, not sample count.</summary>
public sealed class TimeSmoother
{
    readonly TimeSpan _timeConstant;
    DateTime _at;

    public TimeSmoother(TimeSpan timeConstant) => _timeConstant = timeConstant;

    public double? Value { get; private set; }

    public double Add(double value, DateTime now)
    {
        if (Value is not double v)
        {
            Value = value;
        }
        else
        {
            double dt = Math.Max(0, (now - _at).TotalSeconds);
            double alpha = 1 - Math.Exp(-dt / _timeConstant.TotalSeconds);
            Value = v + alpha * (value - v);
        }
        _at = now;
        return Value.Value;
    }

    public void Reset() => Value = null;
}
