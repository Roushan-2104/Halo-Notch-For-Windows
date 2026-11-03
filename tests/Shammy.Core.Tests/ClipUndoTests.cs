using Shammy.Core.Clipboard;

namespace Shammy.Core.Tests;

public class ClipUndoTests
{
    static readonly DateTime T0 = new(2026, 11, 4, 9, 0, 0);

    static ClipHistory WithThree()
    {
        var h = new ClipHistory();
        h.Add(ClipEntry.FromText("a", T0));
        h.Add(ClipEntry.FromText("b", T0.AddMinutes(1)));
        h.Add(ClipEntry.FromText("c", T0.AddMinutes(2)));
        h.SetPinned(h.Items.First(e => e.Text == "a"), true);
        return h;
    }

    [Fact]
    public void Clearing_hands_back_what_went_and_keeps_pins()
    {
        var h = WithThree();
        var removed = h.ClearUnpinned();
        Assert.Equal(new[] { "c", "b" }, removed.Select(e => e.Text));
        Assert.Equal(new[] { "a" }, h.Items.Select(e => e.Text));
    }

    [Fact]
    public void Undo_puts_everything_back_in_its_place()
    {
        var h = WithThree();
        var before = h.Items.Select(e => e.Text).ToList();
        int changes = 0;
        h.Changed += () => changes++;
        Assert.Equal(2, h.Restore(h.ClearUnpinned()));
        Assert.Equal(before, h.Items.Select(e => e.Text));
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Something_copied_again_since_the_clear_stays_newest()
    {
        var h = WithThree();
        var removed = h.ClearUnpinned();
        h.Add(ClipEntry.FromText("b", T0.AddMinutes(5)));
        h.Add(ClipEntry.FromText("d", T0.AddMinutes(4)));
        Assert.Equal(1, h.Restore(removed));
        Assert.Equal(new[] { "a", "b", "d", "c" }, h.Items.Select(e => e.Text));
        Assert.Equal(T0.AddMinutes(5), h.Items.Single(e => e.Text == "b").Time);
    }

    [Fact]
    public void Undoing_twice_or_nothing_changes_nothing()
    {
        var h = WithThree();
        var removed = h.ClearUnpinned();
        h.Restore(removed);
        Assert.Equal(0, h.Restore(removed));
        Assert.Equal(0, h.Restore(Array.Empty<ClipEntry>()));
        Assert.Empty(new ClipHistory().ClearUnpinned());
    }

    [Fact]
    public void Restore_respects_capacity()
    {
        var h = new ClipHistory(capacity: 3);
        for (int i = 0; i < 3; i++) h.Add(ClipEntry.FromText($"old{i}", T0.AddMinutes(i)));
        var removed = h.ClearUnpinned();
        for (int i = 0; i < 2; i++) h.Add(ClipEntry.FromText($"new{i}", T0.AddMinutes(10 + i)));
        h.Restore(removed);
        Assert.Equal(new[] { "new1", "new0", "old2" }, h.Items.Select(e => e.Text));
    }
}
