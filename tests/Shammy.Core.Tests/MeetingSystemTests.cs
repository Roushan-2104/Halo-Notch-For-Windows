using Shammy.Core.MeetingSystem;

namespace Shammy.Core.Tests;

public class CpuMathTests
{
    [Fact]
    public void System_percent_treats_kernel_time_as_including_idle()
    {
        // 4 s of kernel (3 s of it idle) + 2 s user over 6 s of core time: 3 s busy.
        Assert.Equal(50, CpuMath.SystemPercent(idleDelta: 30_000_000, kernelDelta: 40_000_000, userDelta: 20_000_000), 3);
        Assert.Equal(0, CpuMath.SystemPercent(10, 10, 0));
        Assert.Equal(100, CpuMath.SystemPercent(0, 5, 5));
        Assert.Equal(0, CpuMath.SystemPercent(0, 0, 0));
        Assert.Equal(0, CpuMath.SystemPercent(50, 10, 10)); // inconsistent counters never go negative
    }

    [Fact]
    public void Process_percent_is_share_of_all_cores()
    {
        Assert.Equal(25, CpuMath.ProcessPercent(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), 8), 3);
        Assert.Equal(100, CpuMath.ProcessPercent(TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(1), 8));
        Assert.Equal(0, CpuMath.ProcessPercent(TimeSpan.FromSeconds(1), TimeSpan.Zero, 8));
    }

    [Fact]
    public void File_time_combines_halves()
    {
        Assert.Equal(0x0000000500000007UL, CpuMath.FileTime(7, 5));
    }

    [Fact]
    public void Tracker_groups_by_name_and_skips_new_processes()
    {
        var t = new ProcessCpuTracker();
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0);
        Assert.Empty(t.Update(new[]
        {
            new ProcessSample(1, "chrome", TimeSpan.FromSeconds(10)),
            new ProcessSample(2, "chrome", TimeSpan.FromSeconds(5)),
            new ProcessSample(3, "code", TimeSpan.FromSeconds(1)),
        }, t0, 4));

        var usage = t.Update(new[]
        {
            new ProcessSample(1, "chrome", TimeSpan.FromSeconds(11)),
            new ProcessSample(2, "chrome", TimeSpan.FromSeconds(6)),
            new ProcessSample(3, "code", TimeSpan.FromSeconds(1.4)),
            new ProcessSample(4, "game", TimeSpan.FromSeconds(100)), // started meanwhile: no baseline yet
        }, t0.AddSeconds(1), 4);

        Assert.Equal(2, usage.Count);
        Assert.Equal("chrome", usage[0].Name);
        Assert.Equal(50, usage[0].Percent, 3);
        Assert.Equal("code", usage[1].Name);
        Assert.Equal(10, usage[1].Percent, 3);
    }

    [Fact]
    public void Tracker_ignores_reused_pids_and_reset_clears_baseline()
    {
        var t = new ProcessCpuTracker();
        var t0 = DateTime.UtcNow;
        t.Update(new[] { new ProcessSample(9, "old", TimeSpan.FromSeconds(1)) }, t0, 1);
        Assert.Empty(t.Update(new[] { new ProcessSample(9, "new", TimeSpan.FromSeconds(5)) }, t0.AddSeconds(1), 1));
        t.Reset();
        Assert.Empty(t.Update(new[] { new ProcessSample(9, "new", TimeSpan.FromSeconds(6)) }, t0.AddSeconds(2), 1));
    }
}

public class BatteryDrainTests
{
    static readonly DateTime T0 = new(2026, 1, 1, 9, 0, 0);

    [Fact]
    public void Needs_two_ticks_before_estimating()
    {
        var d = new DrainEstimator();
        d.Add(T0, 80, false);
        d.Add(T0.AddMinutes(2), 79, false);
        Assert.Null(d.RatePerHour(T0.AddMinutes(2)));
    }

    [Fact]
    public void Rate_is_measured_between_ticks()
    {
        var d = new DrainEstimator();
        d.Add(T0, 80, false);
        d.Add(T0.AddMinutes(1), 79, false);  // first tick: phase unknown before it
        d.Add(T0.AddMinutes(7), 78, false);
        d.Add(T0.AddMinutes(13), 77, false);
        // 2% over 12 minutes = 10%/h
        Assert.Equal(10, d.RatePerHour(T0.AddMinutes(13))!.Value, 3);
    }

    [Fact]
    public void Rate_is_capped_when_no_tick_has_come_for_a_while()
    {
        var d = new DrainEstimator();
        d.Add(T0, 80, false);
        d.Add(T0.AddMinutes(1), 79, false);
        d.Add(T0.AddMinutes(4), 78, false); // looks like 20%/h
        // An hour later it is still 78%: less than 2% used in 60 minutes.
        Assert.Equal(2, d.RatePerHour(T0.AddMinutes(61))!.Value, 3);
    }

    [Fact]
    public void Plugging_in_or_a_jump_up_resets()
    {
        var d = new DrainEstimator();
        d.Add(T0, 80, false);
        d.Add(T0.AddMinutes(1), 79, false);
        d.Add(T0.AddMinutes(7), 78, false);
        d.Add(T0.AddMinutes(8), 78, true);
        Assert.Null(d.RatePerHour(T0.AddMinutes(8)));

        d.Add(T0.AddMinutes(9), 78, false);
        d.Add(T0.AddMinutes(10), 77, false);
        d.Add(T0.AddMinutes(11), 79, false);
        d.Add(T0.AddMinutes(12), 78, false);
        Assert.Null(d.RatePerHour(T0.AddMinutes(12)));
    }

    [Fact]
    public void Old_ticks_fall_out_of_the_window()
    {
        var d = new DrainEstimator(TimeSpan.FromMinutes(30));
        d.Add(T0, 90, false);
        d.Add(T0.AddMinutes(1), 89, false);
        d.Add(T0.AddMinutes(2), 88, false); // fast at first (60%/h)...
        for (int i = 1; i <= 6; i++) d.Add(T0.AddMinutes(2 + 10 * i), 88 - i, false); // ...then 6%/h
        Assert.Equal(6, d.RatePerHour(T0.AddMinutes(62))!.Value, 3);
    }

    [Fact]
    public void Remaining_time_and_report_rate()
    {
        Assert.Equal(TimeSpan.FromHours(4), DrainEstimator.Remaining(40, 10));
        Assert.Null(DrainEstimator.Remaining(40, null));
        Assert.Null(DrainEstimator.Remaining(40, -5));
        Assert.Equal(TimeSpan.FromHours(48), DrainEstimator.Remaining(100, 0.5));
        Assert.Equal(20, DrainEstimator.FromReport(-10_000, 50_000)!.Value, 3);
        Assert.Equal(-10, DrainEstimator.FromReport(5_000, 50_000)!.Value, 3);
        Assert.Null(DrainEstimator.FromReport(null, 50_000));
        Assert.Null(DrainEstimator.FromReport(-1, 0));
    }

    [Fact]
    public void Smoother_follows_elapsed_time()
    {
        var s = new TimeSmoother(TimeSpan.FromSeconds(10));
        Assert.Equal(10, s.Add(10, T0));
        Assert.Equal(10 + 10 * (1 - Math.Exp(-1)), s.Add(20, T0.AddSeconds(10)), 6);
        Assert.Equal(20, s.Add(20, T0.AddHours(1)), 6);
    }
}

public class AlertTests
{
    static readonly DateTime T0 = new(2026, 1, 1, 9, 0, 0);

    [Fact]
    public void Cooldown_is_per_kind()
    {
        var c = new AlertCooldown(TimeSpan.FromMinutes(10));
        Assert.True(c.TryFire("cpu", T0));
        Assert.False(c.TryFire("cpu", T0.AddMinutes(9)));
        Assert.True(c.TryFire("ram", T0.AddMinutes(9)));
        Assert.True(c.TryFire("cpu", T0.AddMinutes(10)));
    }

    [Fact]
    public void Sustained_needs_the_same_subject_throughout()
    {
        var s = new SustainedCondition(TimeSpan.FromSeconds(30));
        Assert.False(s.Update("chrome", T0));
        Assert.False(s.Update("chrome", T0.AddSeconds(20)));
        Assert.False(s.Update("game", T0.AddSeconds(30))); // someone else took over: restart
        Assert.True(s.Update("game", T0.AddSeconds(60)));
        Assert.False(s.Update(null, T0.AddSeconds(70)));
        Assert.False(s.Update("game", T0.AddSeconds(80)));

        var b = new SustainedCondition(TimeSpan.FromMinutes(2));
        Assert.False(b.Update(true, T0));
        Assert.True(b.Update(true, T0.AddMinutes(2)));
        Assert.False(b.Update(false, T0.AddMinutes(3)));
    }
}

public class CapabilityTests
{
    [Theory]
    [InlineData(133_000_000_000_000_000L, 0L, true)]
    [InlineData(133_000_000_000_000_000L, 133_000_000_100_000_000L, false)]
    [InlineData(0L, 0L, false)]
    public void In_use_while_stop_is_zero(long start, long stop, bool inUse)
    {
        Assert.Equal(inUse, CapabilityUsage.IsInUse(start, stop));
    }

    [Fact]
    public void Non_packaged_keys_are_paths()
    {
        var path = CapabilityUsage.PathFromKey(@"C:#Program Files#Google#Chrome#Application#chrome.exe");
        Assert.Equal(@"C:\Program Files\Google\Chrome\Application\chrome.exe", path);
        Assert.True(CapabilityUsage.SamePath(path, @"c:\program files\google\chrome\application\CHROME.EXE"));
        Assert.False(CapabilityUsage.SamePath(path, null));
    }
}

public class AppNameTests
{
    [Theory]
    [InlineData(@"C:\Program Files\Google\Chrome\Application\chrome.exe", "Chrome")]
    [InlineData(@"C:\Users\me\AppData\Roaming\Zoom\bin\Zoom.exe", "Zoom")]
    [InlineData("ms-teams.exe", "Teams")]
    [InlineData("Discord", "Discord")]
    [InlineData("MSTeams_8wekyb3d8bbwe", "Teams")]
    [InlineData("Microsoft.WindowsCamera_8wekyb3d8bbwe", "Camera")]
    [InlineData("Contoso.WindowsVoiceNotes_abcdefghjk123", "Voice Notes")]
    [InlineData("python", "Python")]
    public void Maps_known_apps(string identity, string expected)
    {
        Assert.Equal(expected, AppNames.Friendly(identity));
    }

    [Fact]
    public void Browsers_take_the_web_app_from_window_titles()
    {
        var edge = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
        Assert.Equal("Google Meet", AppNames.Friendly(edge, new[] { "Meet - abc-defg-hij and 2 more pages - Personal - Microsoft Edge" }));
        Assert.Equal("Teams", AppNames.Friendly("chrome.exe", new[] { "Inbox - Gmail", "Chat | Microsoft Teams - Google Chrome" }));
        Assert.Equal("Edge", AppNames.Friendly(edge, new[] { "Meeting notes - Word" }));
        Assert.Equal("Chrome", AppNames.Friendly("chrome.exe", new[] { "how to zoom in - Google Search" }));
    }

    [Fact]
    public void Unknown_apps_use_their_file_description()
    {
        Assert.Equal("Audacity", AppNames.Friendly(@"C:\Program Files\Audacity\audacity.exe", null, " Audacity "));
        // Known names win over descriptions, and titles only matter for browsers.
        Assert.Equal("Zoom", AppNames.Friendly("Zoom.exe", new[] { "Teams" }, "Zoom Meetings"));
    }
}

public class PhraseTests
{
    [Theory]
    [InlineData("mute mic", MicAction.Mute)]
    [InlineData("Mute microphone.", MicAction.Mute)]
    [InlineData("mute my mic", MicAction.Mute)]
    [InlineData("unmute mic", MicAction.Unmute)]
    [InlineData("unmute the microphone", MicAction.Unmute)]
    [InlineData("toggle  mic", MicAction.Toggle)]
    public void Parses_mic_commands(string text, MicAction expected)
    {
        Assert.Equal(expected, Phrases.ParseMic(text));
    }

    [Theory]
    [InlineData("mute")]
    [InlineData("best usb mic")]
    [InlineData("how to mute mic on zoom")]
    public void Leaves_other_text_alone(string text)
    {
        Assert.Null(Phrases.ParseMic(text));
    }

    [Theory]
    [InlineData("system", true)]
    [InlineData("CPU", true)]
    [InlineData("battery status?", true)]
    [InlineData("cpu usage", true)]
    [InlineData("best cpu for gaming", false)]
    [InlineData("battery", false)]
    public void System_queries_are_exact(string text, bool expected)
    {
        Assert.Equal(expected, Phrases.IsSystemQuery(text));
    }

    [Fact]
    public void Formats()
    {
        Assert.Equal("3h 40m", SystemFormat.Duration(TimeSpan.FromMinutes(220)));
        Assert.Equal("45m", SystemFormat.Duration(TimeSpan.FromMinutes(45.5)));
        Assert.Equal("<1m", SystemFormat.Duration(TimeSpan.FromSeconds(20)));
        Assert.Equal("15.8", SystemFormat.Gigabytes(16_965_000_000));
        Assert.Equal("9.4%/h", SystemFormat.Rate(9.44));
        Assert.Equal("23%", SystemFormat.Percent(23.4));
    }
}
