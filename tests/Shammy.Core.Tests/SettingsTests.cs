using Shammy.Core.Ambient;
using Shammy.Core.Audio;
using Shammy.Core.Conversions;
using Shammy.Core.Notch;
using Shammy.Core.Settings;

namespace Shammy.Core.Tests;

public class SettingValueTests
{
    enum Speed { Quick, Normal }

    [Theory]
    [InlineData("on", true)]
    [InlineData("TRUE", true)]
    [InlineData("yes", true)]
    [InlineData("1", true)]
    [InlineData("off", false)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    public void Bools(string text, bool expected)
    {
        Assert.True(SettingValue.TryParse(typeof(bool), text, out var v));
        Assert.Equal(expected, v);
    }

    [Fact]
    public void Nullable_bool_takes_auto()
    {
        Assert.True(SettingValue.TryParse(typeof(bool?), "auto", out var v));
        Assert.Null(v);
        Assert.True(SettingValue.TryParse(typeof(bool?), "on", out v));
        Assert.Equal(true, v);
        Assert.False(SettingValue.TryParse(typeof(bool), "auto", out _));
    }

    [Fact]
    public void Enums_by_name_only()
    {
        Assert.True(SettingValue.TryParse(typeof(Speed), "quick", out var v));
        Assert.Equal(Speed.Quick, v);
        Assert.False(SettingValue.TryParse(typeof(Speed), "1", out _));
        Assert.False(SettingValue.TryParse(typeof(Speed), "slow", out _));
    }

    [Fact]
    public void Numbers_and_lists()
    {
        Assert.True(SettingValue.TryParse(typeof(int), "25", out var n));
        Assert.Equal(25, n);
        Assert.False(SettingValue.TryParse(typeof(int), "lots", out _));
        Assert.True(SettingValue.TryParse(typeof(List<string>), "music, clipboard,music", out var list));
        Assert.Equal(new List<string> { "music", "clipboard" }, list);
        Assert.True(SettingValue.TryParse(typeof(List<string>), "none", out var empty));
        Assert.Empty((List<string>)empty!);
    }

    [Fact]
    public void Formats_back()
    {
        Assert.Equal("on", SettingValue.Format(true));
        Assert.Equal("auto", SettingValue.Format(null));
        Assert.Equal("a,b", SettingValue.Format(new List<string> { "a", "b" }));
        Assert.Equal("Quick", SettingValue.Format(Speed.Quick));
    }

    [Theory]
    [InlineData("usd", "USD")]
    [InlineData(" eur ", "EUR")]
    [InlineData("rupees", "INR")]
    [InlineData(null, "INR")]
    public void Currency_codes(string? text, string expected) => Assert.Equal(expected, SettingLimits.CurrencyCode(text));

    [Theory]
    [InlineData("HI", "hi")]
    [InlineData("zh-cn", "zh-CN")]
    [InlineData("x", "hi")]
    [InlineData("h1", "hi")]
    public void Language_codes(string text, string expected) => Assert.Equal(expected, SettingLimits.LanguageCode(text));

    [Fact]
    public void Limits_clamp()
    {
        Assert.Equal(1, SettingLimits.SnoozeMinutes(0));
        Assert.Equal(60, SettingLimits.SnoozeMinutes(999));
        Assert.Equal(100, SettingLimits.Volume(120));
        Assert.Equal(0, SettingLimits.Volume(-5));
    }

    [Fact]
    public void Microphone_is_found_by_name_even_when_truncated()
    {
        var names = new[] { "Microphone Array (Realtek(R) Au", "Headset (Airdopes 311 Pro Hands-" };
        Assert.Equal(0, DeviceMatch.Find(names, "Microphone Array (Realtek(R) Audio)"));
        Assert.Equal(1, DeviceMatch.Find(names, "headset (airdopes 311 pro hands-"));
        Assert.Equal(-1, DeviceMatch.Find(names, "USB Mic"));
        Assert.Equal(-1, DeviceMatch.Find(names, null));
    }
}

public class SettingWiringTests
{
    [Fact]
    public void Home_currency_is_the_default_target()
    {
        Assert.Equal("EUR", Currency.DefaultTarget("USD", "EUR"));
        Assert.Equal("USD", Currency.DefaultTarget("EUR", "EUR"));
        Assert.Equal("EUR", Currency.DefaultTarget("USD", "USD"));
        Assert.Equal("INR", Currency.DefaultTarget("USD", "INR"));
    }

    [Fact]
    public void Translate_target_is_the_home_language()
    {
        Assert.Equal("ta", Translation.DefaultTarget("good morning", "ta"));
        Assert.Equal("en", Translation.DefaultTarget("नमस्ते", "ta"));
        Assert.Equal("en", Translation.DefaultTarget("good morning", "en"));
    }

    [Theory]
    [InlineData(0, TemperatureUnit.Celsius, "0°")]
    [InlineData(0, TemperatureUnit.Fahrenheit, "32°")]
    [InlineData(31, TemperatureUnit.Fahrenheit, "88°")]
    [InlineData(-40, TemperatureUnit.Fahrenheit, "-40°")]
    public void Temperatures(double celsius, TemperatureUnit unit, string expected) => Assert.Equal(expected, WeatherText.Temp(celsius, unit));

    [Fact]
    public void Hover_speed_changes_the_waits()
    {
        Assert.True(HoverIntent.Dwells(HoverSpeed.Quick).Peek < HoverIntent.Dwells(HoverSpeed.Normal).Peek);
        Assert.True(HoverIntent.Dwells(HoverSpeed.Relaxed).Open > HoverIntent.Dwells(HoverSpeed.Normal).Open);
        Assert.Equal((HoverIntent.PeekDwell, HoverIntent.OpenDwell), HoverIntent.Dwells(HoverSpeed.Normal));
    }

    [Theory]
    [InlineData(HoverSpeed.Quick, 160, true)]
    [InlineData(HoverSpeed.Normal, 160, false)]
    [InlineData(HoverSpeed.Normal, 260, true)]
    [InlineData(HoverSpeed.Relaxed, 260, false)]
    public void Resting_pointer_peeks_after_the_speeds_wait(HoverSpeed speed, int restMs, bool peeks)
    {
        var intent = new HoverIntent { SpeedSetting = speed };
        var decisions = new List<HoverDecision>();
        for (int t = 0; t <= restMs; t += 10) decisions.Add(intent.Sample(50, 10, TimeSpan.FromMilliseconds(t)));
        Assert.Equal(peeks, decisions.Contains(HoverDecision.Peek));
    }
}

public class AlarmToneTests
{
    [Fact]
    public void Loop_starts_and_ends_quietly_at_a_sane_level()
    {
        var pcm = AlarmTone.RenderLoop();
        Assert.Equal((int)(AlarmTone.LoopSeconds * AlarmTone.SampleRate), pcm.Length);
        Assert.Equal(0, pcm[0]);
        Assert.True(Math.Abs((int)pcm[^1]) < 50);
        int peak = pcm.Max(s => Math.Abs((int)s));
        Assert.InRange(peak, short.MaxValue * 0.45, short.MaxValue * 0.55);
    }

    [Fact]
    public void Volume_swells_over_thirty_seconds()
    {
        Assert.Equal(AlarmTone.StartGain, AlarmTone.RampGain(0), 3);
        Assert.True(AlarmTone.RampGain(10) < AlarmTone.RampGain(20));
        Assert.Equal(1, AlarmTone.RampGain(30), 3);
        Assert.Equal(1, AlarmTone.RampGain(300), 3);
        Assert.Equal(AlarmTone.StartGain, AlarmTone.RampGain(-5), 3);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 0.25)]
    [InlineData(100, 1)]
    [InlineData(150, 1)]
    public void Volume_setting_gain(int volume, double gain) => Assert.Equal(gain, SoundLevel.Gain(volume), 3);

    [Fact]
    public void Scaling_clips_instead_of_wrapping()
    {
        Assert.Equal(short.MaxValue, SoundLevel.Scale(30000, 2));
        Assert.Equal(short.MinValue, SoundLevel.Scale(-30000, 2));
        Assert.Equal(500, SoundLevel.Scale(1000, 0.5));
    }
}
