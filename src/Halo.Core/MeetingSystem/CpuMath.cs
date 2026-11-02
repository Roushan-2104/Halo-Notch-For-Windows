namespace Halo.Core.MeetingSystem;

public static class CpuMath
{
    /// <summary>
    /// Busy share of all cores between two GetSystemTimes calls. Kernel time already includes idle
    /// time, so total = kernel + user and busy = total - idle.
    /// </summary>
    public static double SystemPercent(ulong idleDelta, ulong kernelDelta, ulong userDelta)
    {
        ulong total = kernelDelta + userDelta;
        if (total == 0) return 0;
        ulong busy = total > idleDelta ? total - idleDelta : 0;
        return Math.Clamp(100.0 * busy / total, 0, 100);
    }

    /// <summary>A process's share of the whole machine (all cores = 100%), like Task Manager.</summary>
    public static double ProcessPercent(TimeSpan cpuDelta, TimeSpan wallDelta, int processorCount)
    {
        if (wallDelta <= TimeSpan.Zero || processorCount <= 0 || cpuDelta <= TimeSpan.Zero) return 0;
        return Math.Clamp(100.0 * cpuDelta.Ticks / (wallDelta.Ticks * (double)processorCount), 0, 100);
    }

    public static ulong FileTime(uint low, uint high) => ((ulong)high << 32) | low;
}

public sealed record ProcessSample(int Pid, string Name, TimeSpan Cpu);

public sealed record ProcessUsage(string Name, double Percent);

/// <summary>
/// Turns successive snapshots of per-process CPU time into per-app usage. Processes are grouped by
/// name because browsers and Electron apps spread one app over dozens of processes.
/// </summary>
public sealed class ProcessCpuTracker
{
    Dictionary<int, (string Name, TimeSpan Cpu)> _last = new();
    DateTime _lastAt;
    bool _hasBaseline;

    /// <summary>Usage since the previous snapshot, highest first; empty for the first snapshot.</summary>
    public IReadOnlyList<ProcessUsage> Update(IEnumerable<ProcessSample> samples, DateTime now, int processorCount)
    {
        var next = new Dictionary<int, (string Name, TimeSpan Cpu)>();
        var byName = new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in samples)
        {
            next[s.Pid] = (s.Name, s.Cpu);
            if (!_hasBaseline) continue;
            // A new pid (or a reused one with another name) only counts from its next snapshot.
            if (!_last.TryGetValue(s.Pid, out var prev) || !string.Equals(prev.Name, s.Name, StringComparison.OrdinalIgnoreCase)) continue;
            var delta = s.Cpu - prev.Cpu;
            if (delta <= TimeSpan.Zero) continue;
            byName[s.Name] = byName.TryGetValue(s.Name, out var sum) ? sum + delta : delta;
        }

        var wall = now - _lastAt;
        bool had = _hasBaseline;
        _last = next;
        _lastAt = now;
        _hasBaseline = true;
        if (!had) return Array.Empty<ProcessUsage>();

        return byName
            .Select(kv => new ProcessUsage(kv.Key, CpuMath.ProcessPercent(kv.Value, wall, processorCount)))
            .Where(u => u.Percent > 0)
            .OrderByDescending(u => u.Percent)
            .ToList();
    }

    public void Reset()
    {
        _last = new();
        _hasBaseline = false;
    }
}
