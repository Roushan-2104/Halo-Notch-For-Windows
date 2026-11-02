using Halo.Core.Clipboard;

namespace Halo.Core.Tests;

public class ClipHistoryTests
{
    static readonly DateTime T0 = new(2026, 11, 2, 9, 0, 0);

    static ClipEntry Text(string s, int second = 0) => ClipEntry.FromText(s, T0.AddSeconds(second));

    [Fact]
    public void Newest_first()
    {
        var h = new ClipHistory();
        h.Add(Text("a", 1));
        h.Add(Text("b", 2));
        h.Add(Text("c", 3));
        Assert.Equal(new[] { "c", "b", "a" }, h.Items.Select(e => e.Text));
    }

    [Fact]
    public void Duplicate_moves_to_top_instead_of_adding()
    {
        var h = new ClipHistory();
        var first = h.Add(Text("a", 1));
        h.Add(Text("b", 2));
        var again = h.Add(Text("a", 3));
        Assert.Same(first, again);
        Assert.Equal(new[] { "a", "b" }, h.Items.Select(e => e.Text));
        Assert.Equal(T0.AddSeconds(3), again.Time);
    }

    [Fact]
    public void Trims_oldest_unpinned_beyond_capacity()
    {
        var h = new ClipHistory(capacity: 3);
        var pinned = h.Add(Text("keep", 0));
        h.SetPinned(pinned, true);
        for (int i = 1; i <= 5; i++) h.Add(Text($"t{i}", i));
        Assert.Equal(new[] { "keep", "t5", "t4" }, h.Items.Select(e => e.Text));
    }

    [Fact]
    public void Pinned_stay_on_top_and_survive_clear()
    {
        var h = new ClipHistory();
        var a = h.Add(Text("a", 1));
        h.Add(Text("b", 2));
        h.SetPinned(a, true);
        h.Add(Text("c", 3));
        Assert.Equal(new[] { "a", "c", "b" }, h.Items.Select(e => e.Text));

        Assert.Equal(2, h.Clear());
        Assert.Equal(new[] { "a" }, h.Items.Select(e => e.Text));
        Assert.Equal(1, h.PinnedCount);
    }

    [Fact]
    public void Unpinning_returns_to_the_unpinned_section()
    {
        var h = new ClipHistory();
        var a = h.Add(Text("a", 1));
        var b = h.Add(Text("b", 2));
        h.SetPinned(a, true);
        h.SetPinned(b, true);
        h.Add(Text("c", 3));
        h.SetPinned(b, false);
        Assert.Equal(new[] { "a", "b", "c" }, h.Items.Select(e => e.Text));
        Assert.False(b.Pinned);
    }

    [Fact]
    public void Images_have_their_own_cap()
    {
        var h = new ClipHistory(capacity: 30, imageCapacity: 2);
        h.Add(ClipEntry.FromImage("h1", 10, 10, T0.AddSeconds(1)));
        h.Add(Text("text", 2));
        h.Add(ClipEntry.FromImage("h2", 10, 10, T0.AddSeconds(3)));
        h.Add(ClipEntry.FromImage("h3", 10, 10, T0.AddSeconds(4)));
        Assert.Equal(new[] { "i:h3", "i:h2", "t:text" }, h.Items.Select(e => e.Key));
    }

    [Fact]
    public void Same_files_in_any_case_are_one_entry()
    {
        var h = new ClipHistory();
        h.Add(ClipEntry.FromFiles(new[] { @"C:\A.txt", @"C:\b.txt" }, T0));
        h.Add(ClipEntry.FromFiles(new[] { @"c:\a.txt", @"C:\B.txt" }, T0.AddSeconds(1)));
        Assert.Single(h.Items);
    }

    [Fact]
    public void Touch_moves_to_top()
    {
        var h = new ClipHistory();
        var a = h.Add(Text("a", 1));
        h.Add(Text("b", 2));
        h.Touch(a, T0.AddSeconds(5));
        Assert.Equal("a", h.Items[0].Text);
        Assert.Equal(T0.AddSeconds(5), a.Time);
    }

    [Fact]
    public void Load_orders_by_pin_then_time_and_drops_duplicates()
    {
        var h = new ClipHistory();
        var pinned = Text("p", 1);
        pinned.Pinned = true;
        h.Load(new[] { Text("old", 2), pinned, Text("new", 9), Text("new", 3) });
        Assert.Equal(new[] { "p", "new", "old" }, h.Items.Select(e => e.Text));
    }

    [Fact]
    public void Saved_entries_round_trip_without_the_derived_key_or_tag()
    {
        var e = Text("hello", 1);
        e.Pinned = true;
        e.Tag = new object();
        var json = System.Text.Json.JsonSerializer.Serialize(e);
        Assert.DoesNotContain("Key", json);
        Assert.DoesNotContain("Tag", json);
        var back = System.Text.Json.JsonSerializer.Deserialize<ClipEntry>(json)!;
        Assert.Equal("t:hello", back.Key);
        Assert.True(back.Pinned);
        Assert.Equal(e.Time, back.Time);
    }

    [Fact]
    public void Raises_changed()
    {
        var h = new ClipHistory();
        int n = 0;
        h.Changed += () => n++;
        var a = h.Add(Text("a"));
        h.SetPinned(a, true);
        h.Remove(a);
        h.Clear();
        Assert.Equal(3, n);
    }
}

public class ClipTextTests
{
    [Theory]
    [InlineData("clipboard", ClipIntent.Show)]
    [InlineData("Show clipboard", ClipIntent.Show)]
    [InlineData("show me my clipboard history.", ClipIntent.Show)]
    [InlineData("open the clipboard", ClipIntent.Show)]
    [InlineData("clipboard history", ClipIntent.Show)]
    [InlineData("clear clipboard history", ClipIntent.Clear)]
    [InlineData("Clear my clipboard", ClipIntent.Clear)]
    [InlineData("best clipboard manager for windows", ClipIntent.None)]
    [InlineData("what is a clipboard", ClipIntent.None)]
    [InlineData("clear history", ClipIntent.None)]
    public void Parses_commands(string text, ClipIntent intent) => Assert.Equal(intent, ClipText.Parse(text));

    [Fact]
    public void Preview_collapses_whitespace_and_cuts()
    {
        Assert.Equal("hello world", ClipText.Preview("  hello\r\n\tworld "));
        var p = ClipText.Preview("The quick brown fox jumps over the lazy dog");
        Assert.Equal(22, p.Length);
        Assert.EndsWith("…", p);
        Assert.StartsWith("The quick brown fox", p);
    }

    [Fact]
    public void First_line_skips_blank_lines()
    {
        Assert.Equal("second", ClipText.FirstLine("\r\n   \r\nsecond\r\nthird"));
        Assert.Equal("", ClipText.FirstLine(" \n "));
    }

    [Fact]
    public void Files_title()
    {
        Assert.Equal("report.pdf", ClipText.FilesTitle(new[] { @"C:\docs\report.pdf" }));
        Assert.Equal("report.pdf + 2 more", ClipText.FilesTitle(new[] { @"C:\docs\report.pdf", @"C:\a", @"C:\b" }));
        Assert.Equal("docs", ClipText.FilesTitle(new[] { @"C:\docs\" }));
    }

    [Fact]
    public void Relative_time()
    {
        var now = new DateTime(2026, 11, 2, 12, 0, 0);
        Assert.Equal("now", ClipText.Relative(now.AddSeconds(-20), now));
        Assert.Equal("5m", ClipText.Relative(now.AddMinutes(-5), now));
        Assert.Equal("3h", ClipText.Relative(now.AddHours(-3), now));
        var old = now.AddDays(-3);
        Assert.Equal(old.ToString("d MMM"), ClipText.Relative(old, now));
    }
}

public class ClipPrivacyTests
{
    [Theory]
    [InlineData(true, null, null, true)]
    [InlineData(false, 0u, null, true)]
    [InlineData(false, null, 0u, true)]
    [InlineData(false, 1u, 1u, false)]
    [InlineData(false, null, null, false)]
    public void Skips_opted_out_content(bool exclude, uint? history, uint? cloud, bool skip) =>
        Assert.Equal(skip, ClipPrivacy.ShouldSkip(exclude, history, cloud));

    [Fact]
    public void Reads_dword_and_treats_garbage_as_no()
    {
        Assert.Equal(1u, ClipPrivacy.ReadDword(new byte[] { 1, 0, 0, 0 }));
        Assert.Equal(0u, ClipPrivacy.ReadDword(new byte[] { 1 }));
        Assert.Equal(0u, ClipPrivacy.ReadDword(null));
    }
}

public class ClipTimingTests
{
    static readonly DateTime T0 = new(2026, 11, 2, 9, 0, 0);

    [Fact]
    public void Coalesces_copies_within_window()
    {
        var c = new CopyCoalescer(TimeSpan.FromMilliseconds(300));
        Assert.True(c.ShouldAnnounce(T0));
        Assert.False(c.ShouldAnnounce(T0.AddMilliseconds(200)));
        Assert.False(c.ShouldAnnounce(T0.AddMilliseconds(450)));
        Assert.True(c.ShouldAnnounce(T0.AddMilliseconds(800)));
    }

    [Fact]
    public void Snip_hotkey_marks_next_image_once()
    {
        var d = new SnipDetector();
        Assert.False(d.IsScreenshot(T0, "chrome"));
        d.Arm(T0);
        Assert.True(d.IsScreenshot(T0.AddSeconds(4), "chrome"));
        Assert.False(d.IsScreenshot(T0.AddSeconds(5), "chrome"));
    }

    [Fact]
    public void Snip_arm_expires_and_can_be_cancelled()
    {
        var d = new SnipDetector();
        d.Arm(T0);
        Assert.False(d.IsScreenshot(T0 + SnipDetector.ArmWindow + TimeSpan.FromSeconds(1), null));
        d.Arm(T0);
        d.Disarm();
        Assert.False(d.IsScreenshot(T0.AddSeconds(1), null));
    }

    [Theory]
    [InlineData("SnippingTool", true)]
    [InlineData("ScreenClippingHost", true)]
    [InlineData("explorer", false)]
    [InlineData(null, false)]
    public void Snipping_tool_owner_is_a_screenshot(string? owner, bool expected) =>
        Assert.Equal(expected, new SnipDetector().IsScreenshot(T0, owner));

    [Fact]
    public void Same_capture_window()
    {
        Assert.True(SnipDetector.IsSameCapture(T0, T0.AddSeconds(2)));
        Assert.False(SnipDetector.IsSameCapture(T0.AddSeconds(5), T0));
    }
}
