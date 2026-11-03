using Shammy.Core.Activities;

namespace Shammy.Core.Tests;

public class FullscreenPolicyTests
{
    [Fact]
    public void Only_hud_and_breakthrough_alerts_show_over_fullscreen()
    {
        Assert.False(FullscreenPolicy.ShowsOver(null));
        Assert.True(FullscreenPolicy.ShowsOver(new Activity("hud", ActivityKind.Hud, 90)));
        Assert.True(FullscreenPolicy.ShowsOver(new Activity("productivity.alert", ActivityKind.Message, 85) { BreaksFullscreen = true }));
        Assert.False(FullscreenPolicy.ShowsOver(new Activity("productivity.alert", ActivityKind.Message, 85)));
        Assert.False(FullscreenPolicy.ShowsOver(new Activity("notification", ActivityKind.Notification, 80)));
        Assert.False(FullscreenPolicy.ShowsOver(new Activity("media", ActivityKind.NowPlaying, 20)));
        Assert.False(FullscreenPolicy.ShowsOver(new Activity("timer", ActivityKind.Custom, 45)));
    }

    [Fact]
    public void A_finished_timer_keeps_the_notch_over_a_notification_and_music()
    {
        var m = new ActivityManager();
        m.Post(new Activity("media", ActivityKind.NowPlaying, 20));
        m.Post(new Activity("productivity.alert", ActivityKind.Message, 85) { BreaksFullscreen = true });
        m.Post(new Activity("notification", ActivityKind.Notification, 80));
        Assert.True(FullscreenPolicy.ShowsOver(m.Current));

        // Dismissed: what's left (a notification, music) stays hidden in fullscreen.
        m.Remove("productivity.alert");
        Assert.False(FullscreenPolicy.ShowsOver(m.Current));
    }
}
