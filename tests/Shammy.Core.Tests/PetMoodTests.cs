using Shammy.Core.Notch;

namespace Shammy.Core.Tests;

public class PetMoodTests
{
    static readonly DateTime Now = new(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc);
    static readonly DateTime Noon = new(2026, 11, 3, 12, 0, 0), Night = new(2026, 11, 3, 23, 30, 0);

    static PetInputs Base => new() { Now = Now, LocalNow = Noon, BatteryPercent = 80 };

    [Fact]
    public void Idle_when_nothing_is_going_on()
    {
        Assert.Equal(PetMood.Idle, PetMoods.Decide(Base));
        Assert.Equal(PetMood.Idle, PetMoods.Decide(Base with { BatteryPercent = null }));
    }

    [Fact]
    public void Follows_the_priority_order()
    {
        // Everything at once; peel off the winner each time.
        var all = Base with
        {
            Listening = true, Thinking = true, PettedAt = Now.AddSeconds(-1), TimerDone = true,
            NotifiedAt = Now.AddSeconds(-2), MeetingSoon = true, MusicPlaying = true, BatteryPercent = 12, LocalNow = Night,
        };
        var order = new List<PetMood>();
        var i = all;
        for (int step = 0; step < 10; step++)
        {
            var mood = PetMoods.Decide(i);
            order.Add(mood);
            i = mood switch
            {
                PetMood.Listening => i with { Listening = false },
                PetMood.Thinking => i with { Thinking = false },
                PetMood.Happy => i with { PettedAt = null },
                PetMood.TimerDone => i with { TimerDone = false },
                PetMood.Notify => i with { NotifiedAt = null },
                PetMood.MeetingSoon => i with { MeetingSoon = false },
                PetMood.Music => i with { MusicPlaying = false },
                PetMood.Tired => i with { BatteryPercent = 90 },
                PetMood.Sleepy => i with { LocalNow = Noon },
                _ => i,
            };
            if (mood == PetMood.Idle) break;
        }
        Assert.Equal(new[]
        {
            PetMood.Listening, PetMood.Thinking, PetMood.Happy, PetMood.TimerDone, PetMood.Notify, PetMood.MeetingSoon,
            PetMood.Music, PetMood.Tired, PetMood.Sleepy, PetMood.Idle,
        }, order);
    }

    [Fact]
    public void Notification_excitement_ends_when_seen_or_after_eight_seconds()
    {
        Assert.Equal(PetMood.Notify, PetMoods.Decide(Base with { NotifiedAt = Now.AddSeconds(-7.9) }));
        Assert.Equal(PetMood.Idle, PetMoods.Decide(Base with { NotifiedAt = Now.AddSeconds(-8) }));
        Assert.Equal(PetMood.Idle, PetMoods.Decide(Base with { NotifiedAt = Now.AddSeconds(-1), NotificationSeen = true }));
    }

    [Fact]
    public void Happy_for_two_and_a_half_seconds_after_a_pat()
    {
        Assert.Equal(PetMood.Happy, PetMoods.Decide(Base with { PettedAt = Now.AddSeconds(-2.4) }));
        Assert.Equal(PetMood.Idle, PetMoods.Decide(Base with { PettedAt = Now.AddSeconds(-2.6) }));
    }

    [Fact]
    public void Tired_only_when_low_and_not_charging()
    {
        Assert.Equal(PetMood.Tired, PetMoods.Decide(Base with { BatteryPercent = 19 }));
        Assert.Equal(PetMood.Idle, PetMoods.Decide(Base with { BatteryPercent = 20 }));
        Assert.Equal(PetMood.Idle, PetMoods.Decide(Base with { BatteryPercent = 12, Charging = true }));
    }

    [Fact]
    public void Sleepy_at_night_until_the_mouse_comes_by()
    {
        Assert.Equal(PetMood.Sleepy, PetMoods.Decide(Base with { LocalNow = Night }));
        Assert.Equal(PetMood.Idle, PetMoods.Decide(Base with { LocalNow = Night, PointerNearAt = Now.AddSeconds(-3) }));
        Assert.Equal(PetMood.Sleepy, PetMoods.Decide(Base with { LocalNow = Night, PointerNearAt = Now.AddSeconds(-30) }));
        Assert.Equal(PetMood.Idle, PetMoods.Decide(Base with { LocalNow = new DateTime(2026, 11, 3, 6, 0, 0) }));
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(5, true)]
    [InlineData(0, true)]
    [InlineData(6, false)]
    [InlineData(-1, false)]   // already started: the meeting pill has it, not the pet
    [InlineData(90, false)]
    public void Meeting_soon_comes_from_the_start_time(int minutesAway, bool soon)
    {
        var now = new DateTimeOffset(2026, 11, 3, 10, 25, 0, TimeSpan.FromHours(5.5));
        Assert.Equal(soon, PetMoods.IsMeetingSoon(now.AddMinutes(minutesAway), now));
    }

    [Fact]
    public void Meeting_soon_compares_instants_across_offsets()
    {
        var now = new DateTimeOffset(2026, 11, 3, 10, 25, 0, TimeSpan.FromHours(5.5));
        Assert.True(PetMoods.IsMeetingSoon(now.ToUniversalTime().AddMinutes(3), now));
    }
}

public class WiggleTests
{
    static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void A_quick_side_to_side_wiggle_pets_once()
    {
        var w = new WiggleDetector();
        double[] xs = { 100, 110, 120, 110, 100, 110, 120, 110, 100 };
        var fired = xs.Select((x, i) => w.Add(x, Ms(i * 50))).ToList();
        Assert.Equal(1, fired.Count(f => f));
    }

    [Fact]
    public void Moving_across_or_jittering_is_not_petting()
    {
        var w = new WiggleDetector();
        for (int i = 0; i < 30; i++) Assert.False(w.Add(100 + i * 5, Ms(i * 20)));
        var j = new WiggleDetector();
        for (int i = 0; i < 30; i++) Assert.False(j.Add(100 + (i % 2) * 3, Ms(i * 20)));
    }

    [Fact]
    public void Slow_back_and_forth_is_not_a_wiggle()
    {
        var w = new WiggleDetector();
        double[] xs = { 100, 120, 100, 120, 100 };
        Assert.DoesNotContain(true, xs.Select((x, i) => w.Add(x, Ms(i * 700))));
    }
}
