using System.Text;
using Shammy.Core.Wings;

namespace Shammy.Core.Tests;

public class WingsTests
{
    static WindowTraits App(string cls = "Chrome_WidgetWin_1") =>
        new(Visible: true, Cloaked: false, ToolWindow: false, AppWindow: false, NoActivate: false, HasOwner: false,
            TitleLength: 10, ClassName: cls, OwnProcess: false);

    // ---- Window filter -------------------------------------------------------------------------

    [Fact]
    public void Plain_visible_unowned_window_is_an_app_window() => Assert.True(WindowFilter.IsAppWindow(App()));

    [Fact]
    public void Hidden_cloaked_untitled_and_own_windows_are_left_out()
    {
        Assert.False(WindowFilter.IsAppWindow(App() with { Visible = false }));
        Assert.False(WindowFilter.IsAppWindow(App() with { Cloaked = true }));
        Assert.False(WindowFilter.IsAppWindow(App() with { TitleLength = 0 }));
        Assert.False(WindowFilter.IsAppWindow(App() with { OwnProcess = true }));
    }

    [Fact]
    public void Tool_owned_and_noactivate_windows_are_left_out_unless_forced_with_appwindow()
    {
        Assert.False(WindowFilter.IsAppWindow(App() with { ToolWindow = true }));
        Assert.False(WindowFilter.IsAppWindow(App() with { HasOwner = true }));
        Assert.False(WindowFilter.IsAppWindow(App() with { NoActivate = true }));
        Assert.True(WindowFilter.IsAppWindow(App() with { HasOwner = true, AppWindow = true }));
        Assert.True(WindowFilter.IsAppWindow(App() with { ToolWindow = true, AppWindow = true }));
    }

    [Fact]
    public void Shell_windows_are_never_apps()
    {
        Assert.False(WindowFilter.IsAppWindow(App("Shell_TrayWnd")));
        Assert.False(WindowFilter.IsAppWindow(App("Progman")));
        Assert.False(WindowFilter.IsAppWindow(App("Windows.UI.Core.CoreWindow") with { AppWindow = true }));
    }

    [Fact]
    public void Cloaked_beats_appwindow()
    {
        // Store apps leave cloaked frames around; other virtual desktops are cloaked too.
        Assert.False(WindowFilter.IsAppWindow(App("ApplicationFrameWindow") with { AppWindow = true, Cloaked = true }));
    }

    // ---- Grouping -------------------------------------------------------------------------------

    const string Chrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
    const string Code = @"C:\Users\me\AppData\Local\Programs\Microsoft VS Code\Code.exe";
    const string Notepad = @"C:\Windows\notepad.exe";

    static WindowInfo Win(long h, string? exe, long monitor = 1, string? aumid = null, long order = 0, bool flashing = false) =>
        new(h, "w" + h, aumid, exe, monitor, order == 0 ? h : order, Flashing: flashing);

    static PinnedApp Pin(string name, string? exe, string? aumid = null) =>
        new() { Name = name, ExePath = exe, Aumid = aumid, Launch = exe ?? "shell:AppsFolder\\" + aumid };

    [Fact]
    public void Pinned_running_apps_stay_on_the_left_and_are_not_repeated_on_the_right()
    {
        var pins = new[] { Pin("Chrome", Chrome), Pin("Code", Code) };
        var wins = new[] { Win(1, Chrome), Win(2, Notepad), Win(3, Chrome.ToUpperInvariant()) };
        var c = WingsModel.Build(pins, wins, monitor: 1);
        Assert.Equal(2, c.Pinned[0].Windows.Count);
        Assert.False(c.Pinned[1].IsRunning);
        var open = Assert.Single(c.Open);
        Assert.Equal(Notepad, open.Windows[0].ExePath);
    }

    [Fact]
    public void Open_apps_group_by_app_and_keep_first_seen_order()
    {
        var wins = new[] { Win(10, Notepad, order: 5), Win(11, Code, order: 2), Win(12, Notepad, order: 9) };
        var c = WingsModel.Build(Array.Empty<PinnedApp>(), wins, 1);
        Assert.Equal(2, c.Open.Count);
        Assert.Equal(Code, c.Open[0].Windows[0].ExePath);
        Assert.Equal(2, c.Open[1].Windows.Count);
    }

    [Fact]
    public void Right_wing_only_shows_this_monitors_windows_but_pins_see_every_monitor()
    {
        var pins = new[] { Pin("Chrome", Chrome) };
        var wins = new[] { Win(1, Chrome, monitor: 2), Win(2, Notepad, monitor: 2), Win(3, Code, monitor: 1) };
        var one = WingsModel.Build(pins, wins, 1);
        Assert.True(one.Pinned[0].IsRunning);
        Assert.Equal(Code, Assert.Single(one.Open).Windows[0].ExePath);
        var two = WingsModel.Build(pins, wins, 2);
        Assert.Equal(Notepad, Assert.Single(two.Open).Windows[0].ExePath);
    }

    [Fact]
    public void App_ids_split_web_apps_from_their_browser()
    {
        var pins = new[] { Pin("Chrome", Chrome, "Chrome") };
        var wins = new[] { Win(1, Chrome, aumid: "Chrome"), Win(2, Chrome, aumid: "Chrome._crx_abc") };
        var c = WingsModel.Build(pins, wins, 1);
        Assert.Single(c.Pinned[0].Windows);
        Assert.Equal("Chrome._crx_abc", Assert.Single(c.Open).Windows[0].Aumid);
    }

    [Fact]
    public void Exe_decides_when_only_one_side_has_an_app_id()
    {
        // VS Code's shortcut has an app ID; its windows don't expose one to other processes.
        var pin = Pin("Code", Code, "Microsoft.VisualStudioCode");
        Assert.True(AppIdentity.Matches(pin, Win(1, Code)));
        Assert.False(AppIdentity.Matches(Pin("x", null, "A.B_1234567890abc!App"), Win(1, Code)));
    }

    [Fact]
    public void Store_apps_match_by_app_id()
    {
        const string calc = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App";
        var pins = new[] { Pin("Calculator", null, calc) };
        var c = WingsModel.Build(pins, new[] { Win(1, @"C:\Windows\System32\ApplicationFrameHost.exe", aumid: calc) }, 1);
        Assert.True(c.Pinned[0].IsRunning);
        Assert.Empty(c.Open);
    }

    [Fact]
    public void Flashing_window_flags_its_item()
    {
        var c = WingsModel.Build(Array.Empty<PinnedApp>(), new[] { Win(1, Notepad), Win(2, Notepad, flashing: true) }, 1);
        Assert.True(c.Open[0].IsFlashing);
    }

    // ---- Pin list -------------------------------------------------------------------------------

    [Fact]
    public void Import_skips_duplicates_already_pinned_and_within_the_batch()
    {
        var pins = new List<PinnedApp> { Pin("Chrome", Chrome) };
        int added = PinList.Import(pins, new[]
        {
            Pin("Google Chrome", Chrome.ToLowerInvariant()),
            Pin("Code", Code),
            Pin("Code again", Code),
            new PinnedApp { Name = "broken", Launch = "" },
        });
        Assert.Equal(1, added);
        Assert.Equal(new[] { "Chrome", "Code" }, pins.Select(p => p.Name));
    }

    [Fact]
    public void Import_treats_same_app_id_as_one_pin_even_through_different_shortcuts()
    {
        const string calc = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App";
        var pins = new List<PinnedApp> { new() { Name = "Calculator", Aumid = calc, Launch = @"C:\pins\Calculator.lnk" } };
        Assert.Equal(0, PinList.Import(pins, new[] { Pin("Calc", null, calc) }));
    }

    [Fact]
    public void Pin_and_unpin()
    {
        var pins = new List<PinnedApp>();
        Assert.True(PinList.Pin(pins, Pin("Code", Code)));
        Assert.False(PinList.Pin(pins, Pin("Code", Code)));
        Assert.NotNull(PinList.Unpin(pins, pins[0].Key));
        Assert.Empty(pins);
        Assert.Null(PinList.Unpin(pins, "nope"));
    }

    [Fact]
    public void Move_reorders_and_clamps()
    {
        var pins = new List<PinnedApp> { Pin("a", "a.exe"), Pin("b", "b.exe"), Pin("c", "c.exe") };
        Assert.True(PinList.Move(pins, 0, 2));
        Assert.Equal("bca", string.Concat(pins.Select(p => p.Name)));
        Assert.True(PinList.Move(pins, 2, -5));
        Assert.Equal("abc", string.Concat(pins.Select(p => p.Name)));
        Assert.False(PinList.Move(pins, 1, 1));
        Assert.False(PinList.Move(pins, 7, 0));
        Assert.True(PinList.Move(pins, 0, 99));
        Assert.Equal("bca", string.Concat(pins.Select(p => p.Name)));
    }

    // ---- Taskband blob --------------------------------------------------------------------------

    static byte[] Blob(params object[] parts)
    {
        var bytes = new List<byte>();
        foreach (var p in parts)
        {
            if (p is string s) bytes.AddRange(Encoding.Unicode.GetBytes(s));
            else if (p is byte[] raw) bytes.AddRange(raw);
        }
        return bytes.ToArray();
    }

    [Fact]
    public void Taskband_order_follows_the_blob_and_keeps_unknown_names_last()
    {
        var blob = Blob(new byte[] { 1, 2, 3, 4 }, "Spotify.lnk", new byte[] { 0, 0, 7 }, "File Explorer.lnk", new byte[] { 0, 0 }, "Google Chrome.lnk");
        var order = TaskbandPins.Order(blob, new[] { "Google Chrome.lnk", "Zoom.lnk", "File Explorer.lnk", "Spotify.lnk" });
        Assert.Equal(new[] { "Spotify.lnk", "File Explorer.lnk", "Google Chrome.lnk", "Zoom.lnk" }, order);
    }

    [Fact]
    public void Taskband_order_without_a_blob_keeps_the_given_order()
    {
        Assert.Equal(new[] { "b", "a" }, TaskbandPins.Order(null, new[] { "b", "a" }));
    }

    [Fact]
    public void Store_app_ids_are_found_at_any_alignment_without_repeats()
    {
        const string calc = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App";
        const string term = "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App";
        var blob = Blob(new byte[] { 9 }, calc, new byte[] { 0, 0 }, "Google Chrome.lnk", new byte[] { 0, 0, 0 }, term, new byte[] { 0, 0 }, calc);
        Assert.Equal(new[] { calc, term }, TaskbandPins.PackagedAppIds(blob));
        Assert.Empty(TaskbandPins.PackagedAppIds(Blob("nothing here!", "C:\\foo_bar.exe")));
    }

    [Fact]
    public void Fallback_name_from_an_app_id()
    {
        Assert.Equal("Windows Calculator", TaskbandPins.FallbackName("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"));
    }

    // ---- Layout ---------------------------------------------------------------------------------

    [Fact]
    public void Strip_height_scales_with_dpi()
    {
        Assert.Equal(36, WingLayoutMath.StripHeightPx(96));
        Assert.Equal(45, WingLayoutMath.StripHeightPx(120));
        Assert.Equal(54, WingLayoutMath.StripHeightPx(144));
        Assert.Equal(1280, WingLayoutMath.ToLogical(1920, 144), 3);
    }

    [Fact]
    public void Wings_hug_the_notch_gap()
    {
        var l = WingLayoutMath.Compute(width: 1920, center: 960, gap: 480, left: 2, right: 3);
        // Left inner edge 720: two pins end there, first one furthest left.
        Assert.Equal(new[] { 720 - 68.0, 720 - 34.0 }, l.Left.ItemX);
        Assert.Equal(new[] { 1200, 1234.0, 1268 }, l.Right.ItemX);
        Assert.Null(l.Left.MoreX);
        Assert.Equal(0, l.Right.Overflow);
    }

    [Fact]
    public void Overflow_keeps_the_outer_slot_for_more()
    {
        // 1280 logical (1920 px at 150%): (640 - 240 - 8) / 34 = 11 slots per side.
        var l = WingLayoutMath.Compute(1280, 640, 480, left: 15, right: 11);
        Assert.Equal(10, l.Left.Shown);
        Assert.Equal(5, l.Left.Overflow);
        Assert.Equal(400 - 11 * 34.0, l.Left.MoreX!.Value, 6);
        Assert.Equal(400 - 10 * 34.0, l.Left.ItemX[0], 6);
        Assert.True(l.Left.ItemX.All(x => x >= 8));
        // Exactly full: no "+N".
        Assert.Equal(11, l.Right.Shown);
        Assert.Null(l.Right.MoreX);
        Assert.True(l.Right.ItemX[^1] + 34 <= 1280 - 8);
    }

    [Fact]
    public void Right_overflow_button_sits_past_the_last_icon()
    {
        var l = WingLayoutMath.Compute(1280, 640, 480, left: 0, right: 20);
        Assert.Equal(10, l.Right.Shown);
        Assert.Equal(10, l.Right.Overflow);
        Assert.Equal(880 + 10 * 34.0, l.Right.MoreX!.Value, 6);
    }

    [Fact]
    public void Off_centre_notch_and_tiny_screens()
    {
        var l = WingLayoutMath.Compute(1000, 300, 16, left: 50, right: 1);
        // (292 - 8) / 34 = 8 slots: 7 pins and "+43".
        Assert.Equal(7, l.Left.Shown);
        Assert.NotNull(l.Left.MoreX);
        Assert.Equal(43, l.Left.Overflow);
        var tiny = WingLayoutMath.Compute(400, 200, 480, left: 3, right: 3);
        Assert.Equal(0, tiny.Left.Shown);
        Assert.Equal(3, tiny.Left.Overflow);
        Assert.Null(tiny.Left.MoreX);
    }

    [Fact]
    public void Drop_index_from_the_dragged_icon_centre()
    {
        var xs = new[] { 100.0, 134, 168 };
        Assert.Equal(0, WingLayoutMath.DropIndex(50, xs));
        Assert.Equal(0, WingLayoutMath.DropIndex(120, xs));
        Assert.Equal(1, WingLayoutMath.DropIndex(150, xs));
        Assert.Equal(2, WingLayoutMath.DropIndex(500, xs));
        Assert.Equal(0, WingLayoutMath.DropIndex(500, Array.Empty<double>()));
    }

    // ---- Clicks ---------------------------------------------------------------------------------

    [Fact]
    public void Click_on_a_pin_that_isnt_running_launches_it()
    {
        Assert.Equal(WingAction.Launch, WingClick.Decide(Array.Empty<WingWindowState>(), 5, canLaunch: true).Action);
        Assert.Equal(WingAction.None, WingClick.Decide(Array.Empty<WingWindowState>(), 5, canLaunch: false).Action);
    }

    [Fact]
    public void Click_on_a_lone_window_toggles_between_front_and_minimised()
    {
        var w = new[] { new WingWindowState(7, Minimized: false) };
        Assert.Equal((WingAction.Activate, 7L), WingClick.Decide(w, foreground: 3, canLaunch: true));
        Assert.Equal((WingAction.Minimize, 7L), WingClick.Decide(w, foreground: 7, canLaunch: true));
        // Minimised but still reported as foreground (Windows does this): bring it back.
        Assert.Equal(WingAction.Activate, WingClick.Decide(new[] { new WingWindowState(7, true) }, 7, true).Action);
    }

    [Fact]
    public void Several_windows_show_a_list()
    {
        var w = new[] { new WingWindowState(1, false), new WingWindowState(2, false) };
        Assert.Equal(WingAction.ShowList, WingClick.Decide(w, 1, true).Action);
        Assert.Equal(WingAction.Minimize, WingClick.ForWindow(w[0], 1));
        Assert.Equal(WingAction.Activate, WingClick.ForWindow(w[1], 1));
    }

    [Fact]
    public void Middle_click_starts_another_instance()
    {
        var w = new[] { new WingWindowState(1, false) };
        Assert.Equal(WingAction.Launch, WingClick.Decide(w, 1, canLaunch: true, newInstance: true).Action);
        Assert.Equal(WingAction.None, WingClick.Decide(w, 1, canLaunch: false, newInstance: true).Action);
    }

    // ---- Drag-over and default ------------------------------------------------------------------

    [Fact]
    public void Drag_over_brings_the_most_recently_used_window_else_the_newest()
    {
        Assert.Null(WingDrag.Target(Array.Empty<WindowInfo>()));
        var used = new[] { Win(1, Notepad) with { LastActive = 9 }, Win(2, Notepad) with { LastActive = 4 }, Win(3, Notepad) };
        Assert.Equal(1, WingDrag.Target(used));
        Assert.Equal(3, WingDrag.Target(new[] { Win(1, Notepad), Win(3, Notepad), Win(2, Notepad) }));
    }

    [Fact]
    public void Wings_follow_auto_hide_until_the_user_chooses()
    {
        Assert.True(WingsDefault.IsOn(null, taskbarAutoHides: true));
        Assert.False(WingsDefault.IsOn(null, taskbarAutoHides: false));
        Assert.True(WingsDefault.IsOn(true, taskbarAutoHides: false));
        Assert.False(WingsDefault.IsOn(false, taskbarAutoHides: true));
        Assert.Equal("auto(on)", WingsDefault.Describe(null, true));
        Assert.Equal("explicit(off)", WingsDefault.Describe(false, true));
    }

    // ---- Fullscreen -----------------------------------------------------------------------------

    [Fact]
    public void Fullscreen_means_covering_the_monitor_without_a_caption()
    {
        Assert.True(StripFullscreen.Covers(1920, 0, 3840, 1080, 1920, 0, 3840, 1080, false));
        Assert.False(StripFullscreen.Covers(-8, -8, 1928, 1088, 0, 0, 1920, 1080, maximizedWithCaption: true));
        Assert.False(StripFullscreen.Covers(0, 36, 1920, 1080, 0, 0, 1920, 1080, false));
        // On the other monitor.
        Assert.False(StripFullscreen.Covers(0, 0, 1920, 1080, 1920, 0, 3840, 1080, false));
    }
}
