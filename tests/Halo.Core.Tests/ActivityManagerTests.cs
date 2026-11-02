using Halo.Core.Activities;

namespace Halo.Core.Tests;

public class ActivityManagerTests
{
    [Fact]
    public void Highest_priority_wins()
    {
        var m = new ActivityManager();
        m.Post(new Activity("media", ActivityKind.NowPlaying, 20));
        m.Post(new Activity("hud", ActivityKind.Hud, 90));
        Assert.Equal("hud", m.Current!.Id);
        Assert.Equal("media", m.Secondary!.Id);
    }

    [Fact]
    public void Same_priority_goes_to_most_recent()
    {
        var m = new ActivityManager();
        m.Post(new Activity("a", ActivityKind.Message, 50));
        m.Post(new Activity("b", ActivityKind.Message, 50));
        Assert.Equal("b", m.Current!.Id);
    }

    [Fact]
    public void Expired_activities_are_removed_on_tick()
    {
        var m = new ActivityManager();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        m.Post(new Activity("media", ActivityKind.NowPlaying, 20));
        m.Post(new Activity("hud", ActivityKind.Hud, 90, ExpiresAt: now.AddSeconds(1.5)));
        m.Tick(now.AddSeconds(1));
        Assert.Equal("hud", m.Current!.Id);
        m.Tick(now.AddSeconds(2));
        Assert.Equal("media", m.Current!.Id);
        Assert.Null(m.Secondary);
    }

    [Fact]
    public void Short_lived_kinds_are_never_secondary()
    {
        var m = new ActivityManager();
        m.Post(new Activity("bt", ActivityKind.Bluetooth, 60));
        m.Post(new Activity("hud", ActivityKind.Hud, 90));
        Assert.Null(m.Secondary);
    }

    [Fact]
    public void Reposting_updates_payload_in_place()
    {
        var m = new ActivityManager();
        int changes = 0;
        m.Changed += () => changes++;
        m.Post(new Activity("hud", ActivityKind.Hud, 90, new HudPayload(HudKind.Volume, 0.2)));
        m.Post(new Activity("hud", ActivityKind.Hud, 90, new HudPayload(HudKind.Volume, 0.4)));
        Assert.Single(m.All);
        Assert.Equal(0.4, ((HudPayload)m.Current!.Payload!).Level);
        Assert.Equal(2, changes);
    }
}
