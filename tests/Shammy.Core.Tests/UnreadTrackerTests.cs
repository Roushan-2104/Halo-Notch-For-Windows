using Shammy.Core.Notifications;

namespace Shammy.Core.Tests;

public class UnreadTrackerTests
{
    [Fact]
    public void Counts_only_what_arrived_since_it_was_last_seen()
    {
        var t = new UnreadTracker();
        Assert.Equal(3, t.CountUnread(new uint[] { 1, 2, 3 }));

        t.MarkSeen(new uint[] { 1, 2, 3 });
        Assert.Equal(0, t.CountUnread(new uint[] { 1, 2, 3 }));
        Assert.Equal(2, t.CountUnread(new uint[] { 5, 4, 1, 2, 3 }));
    }

    [Fact]
    public void Dismissed_items_are_forgotten_and_dont_count()
    {
        var t = new UnreadTracker();
        t.MarkSeen(new uint[] { 1, 2 });
        // 2 was dismissed, 3 arrived: one new.
        Assert.Equal(1, t.CountUnread(new uint[] { 3, 1 }));
        t.MarkSeen(new uint[] { 3, 1 });
        Assert.Equal(0, t.CountUnread(new uint[] { 3, 1 }));
        Assert.Equal(0, t.CountUnread(Array.Empty<uint>()));
    }
}
