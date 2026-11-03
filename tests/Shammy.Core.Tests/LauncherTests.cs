using Shammy.Core.Launcher;

namespace Shammy.Core.Tests;

public class AppMatcherTests
{
    static readonly AppEntry[] Apps =
    {
        new("Spotify", "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify"),
        new("Visual Studio Code", "Microsoft.VisualStudioCode"),
        new("Visual Studio 2022", "VisualStudio.2022"),
        new("Microsoft Edge", "MSEdge"),
        new("Google Chrome", "Chrome"),
        new("Notepad", "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App"),
        new("Notepad++", @"{6D809377-6AF0-444B-8957-A3773F02200E}\Notepad++\notepad++.exe"),
        new("Calculator", "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"),
        new("Windows PowerShell", "{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\\WindowsPowerShell\\v1.0\\powershell.exe"),
        new("Adobe Photoshop 2025", "Photoshop"),
        new("Settings", "windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel"),
        new("File Explorer", "Microsoft.Windows.Explorer"),
        new("Microsoft Teams", "MSTeams_8wekyb3d8bbwe!MSTeams"),
        new("OBS Studio", "OBS"),
        new("Paint", "Microsoft.Paint_8wekyb3d8bbwe!App"),
        new("Paint.NET", "paint.net"),
        new("Discord", "com.squirrel.Discord.Discord"),
        new("Resource Monitor", "perfmon.exe /res"),
        new("Microsoft License Manager", "lm"),
        new("Recovery Drive", "RecoveryDrive"),
        new("Antigravity IDE", "Google.Antigravity"),
        new("LetsView on the Web", "LetsView"),
        new("Git CMD", @"{6D809377-6AF0-444B-8957-A3773F02200E}\Git\git-cmd.exe"),
        new("Command Prompt", @"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\cmd.exe"),
    };

    [Theory]
    [InlineData("spotify", "Spotify", 100)]
    [InlineData("powerpoint", "Power Point", 95)]
    [InlineData("spot", "Spotify", 90)]
    [InlineData("vsc", "Visual Studio Code", 80)]
    [InlineData("wps", "Windows PowerShell", 80)]
    [InlineData("chrome", "Google Chrome", 70)]
    [InlineData("studio code", "Visual Studio Code", 70)]
    [InlineData("visual code", "Visual Studio Code", 65)]
    [InlineData("pad", "Notepad", 50)]
    [InlineData("xyz", "Spotify", 0)]
    public void Scores_by_kind(string query, string name, int expected)
    {
        Assert.Equal(expected, AppMatcher.Score(query, name));
    }

    [Theory]
    [InlineData("visual studio board", "Visual Studio Code")]
    [InlineData("visual studio cold", "Visual Studio Code")]
    public void Forgives_one_misheard_word(string query, string expected)
    {
        var best = AppMatcher.Best(query, Apps);
        Assert.Equal(expected, best?.App.Name);
        Assert.True(best!.Value.Score >= AppMatcher.LaunchScore);
    }

    [Theory]
    [InlineData("microsoft word")]
    [InlineData("google maps")]
    [InlineData("adobe illustrator")]
    [InlineData("my studio code thing")]
    public void Does_not_guess_from_too_little(string query)
    {
        Assert.True((AppMatcher.Best(query, Apps)?.Score ?? 0) < AppMatcher.LaunchScore);
    }

    [Fact]
    public void Kinds_are_ordered()
    {
        int exact = AppMatcher.Score("code writer pro", "Code Writer Pro");
        int prefix = AppMatcher.Score("code", "Code Writer Pro");
        int initials = AppMatcher.Score("cwp", "Code Writer Pro");
        int wordStart = AppMatcher.Score("writer", "Code Writer Pro");
        int sub = AppMatcher.Score("cdwrtr", "Code Writer Pro");
        Assert.True(exact > prefix && prefix > initials && initials > wordStart && wordStart > sub && sub > 0,
            $"{exact} {prefix} {initials} {wordStart} {sub}");
    }

    [Fact]
    public void Subsequence_tolerates_typos_but_not_scatter()
    {
        Assert.InRange(AppMatcher.Score("spotfy", "Spotify"), AppMatcher.LaunchScore, 49);
        Assert.Equal(0, AppMatcher.Score("sy", "Spotify"));
        // Must start at a word start, and only single words are matched loosely.
        Assert.Equal(0, AppMatcher.Score("otfy", "Spotify"));
        Assert.Equal(0, AppMatcher.Score("source licenses", "Resource Monitor"));
        Assert.Equal(0, AppMatcher.Score("source licenses", "Microsoft License Manager"));
    }

    [Fact]
    public void Ties_prefer_shorter_names()
    {
        // "Paint" and "Paint.NET" are both prefix matches.
        var top = AppMatcher.Rank("pain", Apps);
        Assert.Equal(top[0].Score, top[1].Score);
        Assert.Equal("Paint", top[0].App.Name);
        Assert.Equal("Paint.NET", top[1].App.Name);
        Assert.Equal(new[] { "Notepad", "Notepad++" }, AppMatcher.Rank("notepad", Apps).Select(m => m.App.Name));
    }

    [Fact]
    public void Ranks_initials_first()
    {
        var top = AppMatcher.Rank("vsc", Apps);
        Assert.Equal("Visual Studio Code", top[0].App.Name);
        Assert.Equal("Visual Studio 2022", AppMatcher.Rank("vs2", Apps)[0].App.Name);
        Assert.True(AppMatcher.Rank("anything", Apps, 3).Count <= 3);
    }

    [Fact]
    public void Normalizes_punctuation_and_case()
    {
        Assert.Equal("notepad++ x64", AppMatcher.Normalize("  Notepad++ (x64) "));
        Assert.Equal(100, AppMatcher.Score("OBS studio", "OBS Studio"));
    }

    [Theory]
    [InlineData("open spotify", "Spotify")]
    [InlineData("Launch Visual Studio Code", "Visual Studio Code")]
    [InlineData("start vsc", "Visual Studio Code")]
    [InlineData("open the calculator app", "Calculator")]
    [InlineData("open up chrome please", "Google Chrome")]
    [InlineData("open edge", "Microsoft Edge")]
    [InlineData("open spotfy", "Spotify")]
    [InlineData("open paint.net", "Paint.NET")]
    [InlineData("open cmd", "Command Prompt")]
    [InlineData("start chrome", "Google Chrome")]
    public void Resolves_apps(string text, string app)
    {
        var plan = LaunchResolver.Resolve(text, Apps);
        Assert.NotNull(plan);
        Assert.Equal(LaunchKind.App, plan!.Request.Kind);
        Assert.Equal(app, plan.Match!.Value.App.Name);
        Assert.True(plan.CanLaunch);
    }

    [Theory]
    [InlineData("open source licenses")]
    [InlineData("how to open a pdf")]
    [InlineData("is spotify open now")]
    [InlineData("open")]
    [InlineData("open the")]
    [InlineData("open a can of worms")]
    [InlineData("start a timer for 5 minutes")]
    [InlineData("spotify")]
    [InlineData("opening hours of the louvre")]
    [InlineData("open it")]
    [InlineData("open my mind")]
    [InlineData("open up")]
    [InlineData("open ai")]
    [InlineData("start over")]
    [InlineData("start pad")]
    public void Leaves_web_questions_alone(string text)
    {
        Assert.Null(LaunchResolver.Resolve(text, Apps));
    }

    [Fact]
    public void Nothing_is_claimed_before_the_index_exists()
    {
        Assert.Null(LaunchResolver.Resolve("open spotify", Array.Empty<AppEntry>()));
        Assert.NotNull(LaunchResolver.Resolve("open github.com", Array.Empty<AppEntry>()));
    }

    [Fact]
    public void Weak_matches_are_suggested_not_launched()
    {
        // "pshop" scatters over "photoshop": claimed so the user sees "Did you mean...", but not launched.
        var plan = LaunchResolver.Resolve("open pshop", Apps);
        Assert.NotNull(plan);
        Assert.False(plan!.CanLaunch);
        Assert.Equal("Adobe Photoshop 2025", plan.Match!.Value.App.Name);
    }

    static readonly AppEntry[] Office =
    {
        new("WhatsApp", "5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App"),
        new("PowerPoint", "Microsoft.Office.POWERPNT.EXE.15"),
        new("Excel", "Microsoft.Office.EXCEL.EXE.15"),
        new("WordPad", "Microsoft.Windows.WordPad"),
        new("YouTube Music", "music.youtube.com-Default"),
        new("Visual Studio Code", "Microsoft.VisualStudioCode"),
        new("Notepad", "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App"),
        new("Google Chrome", "Chrome"),
        new("Chrome Remote Desktop", "ChromeRemote"),
        new("Microsoft Edge", "MSEdge"),
        new("Spotify", "Spotify"),
        new("Command Prompt", @"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\cmd.exe"),
    };

    [Theory]
    [InlineData("open google crome", "Google Chrome")]
    [InlineData("open crome", "Google Chrome")]
    [InlineData("open chorme", "Google Chrome")]
    [InlineData("open spotfy", "Spotify")]
    [InlineData("launch whatsap", "WhatsApp")]
    [InlineData("open notpad", "Notepad")]
    [InlineData("open visul studio code", "Visual Studio Code")]
    public void Forgives_one_typo_per_word(string text, string app)
    {
        var plan = LaunchResolver.Resolve(text, Office);
        Assert.Equal(app, plan?.Match?.App.Name);
        Assert.True(plan!.CanLaunch);
    }

    [Fact]
    public void Typos_rank_below_real_matches()
    {
        int typo = AppMatcher.Score("crome", "Google Chrome");
        Assert.InRange(typo, AppMatcher.LaunchScore, 49);
        Assert.True(AppMatcher.Score("chrome", "Google Chrome") > typo);
        Assert.True(AppMatcher.Score("goo", "Google Chrome") > typo);
        // Short words are never forgiven, nor a wrong first letter.
        Assert.Equal(0, AppMatcher.Score("edg", "Microsoft Word"));
        Assert.True(AppMatcher.Score("word", "Microsoft Edge") < AppMatcher.LaunchScore);
        Assert.True(AppMatcher.Score("bpotify", "Spotify") < AppMatcher.LaunchScore);
    }

    [Theory]
    [InlineData("microsoft word")]
    [InlineData("google maps")]
    [InlineData("my thing")]
    [InlineData("chromebook")]
    [InlineData("whatever")]
    public void Typos_do_not_launch_strangers(string query)
    {
        Assert.True((AppMatcher.Best(query, Office)?.Score ?? 0) < AppMatcher.LaunchScore, AppMatcher.Best(query, Office)?.ToString());
    }

    [Theory]
    [InlineData("open my thing")]
    [InlineData("open google maps")]
    [InlineData("open microsoft word")]
    [InlineData("open word")]
    [InlineData("start over")]
    public void Typos_and_nicknames_leave_strangers_alone(string text)
    {
        var plan = LaunchResolver.Resolve(text, Office);
        // "open word" with only WordPad installed may still suggest WordPad, but never Edge or Word-by-nickname.
        Assert.True(plan is null || plan.Match?.App.Name == "WordPad", plan?.Match?.App.Name);
    }

    [Theory]
    [InlineData("open vs code", "Visual Studio Code")]
    [InlineData("open vscode", "Visual Studio Code")]
    [InlineData("open code", "Visual Studio Code")]
    [InlineData("open ppt", "PowerPoint")]
    [InlineData("open power point", "PowerPoint")]
    [InlineData("open excel", "Excel")]
    [InlineData("open cmd", "Command Prompt")]
    [InlineData("open command prompt", "Command Prompt")]
    [InlineData("open wa", "WhatsApp")]
    [InlineData("open yt music", "YouTube Music")]
    [InlineData("start chrome", "Google Chrome")]
    [InlineData("open edge", "Microsoft Edge")]
    public void Resolves_nicknames(string text, string app)
    {
        var plan = LaunchResolver.Resolve(text, Office);
        Assert.Equal(app, plan?.Match?.App.Name);
        Assert.True(plan!.CanLaunch);
    }

    [Fact]
    public void Nicknames_only_name_installed_apps()
    {
        // "word" is Word when Word is installed, even with WordPad around; without Word it stays WordPad's prefix.
        var withWord = Office.Append(new AppEntry("Microsoft Word 2016", "WINWORD")).ToArray();
        Assert.Equal("Microsoft Word 2016", AppMatcher.Best("word", withWord)?.App.Name);
        Assert.Equal("WordPad", AppMatcher.Best("word", Office)?.App.Name);
        Assert.False(AppMatcher.IsNickname("ppt", "Microsoft Edge"));
        Assert.False(AppMatcher.IsNickname("word", "WordPad"));
        Assert.True(AppMatcher.IsNickname("VS Code", "Visual Studio Code"));
        Assert.False(LaunchResolver.Resolve("open ppt", Apps)?.CanLaunch ?? false);
    }

    [Theory]
    [InlineData("open github.com", "https://github.com")]
    [InlineData("Open GitHub.com/Microsoft", "https://github.com/Microsoft")]
    [InlineData("open news.ycombinator.com", "https://news.ycombinator.com")]
    [InlineData("open github dot com", "https://github.com")]
    [InlineData("open https://example.org/a?b=1", "https://example.org/a?b=1")]
    [InlineData("launch localhost.test:8080", "https://localhost.test:8080")]
    public void Resolves_domains(string text, string url)
    {
        var plan = LaunchResolver.Resolve(text, Apps);
        Assert.Equal(LaunchKind.Url, plan!.Request.Kind);
        Assert.Equal(url, plan.Request.Target);
        Assert.True(plan.CanLaunch);
    }

    [Theory]
    [InlineData("open notes.txt")]
    [InlineData("open report.pdf")]
    public void File_names_are_not_domains(string text)
    {
        Assert.NotEqual(LaunchKind.Url, LaunchResolver.Parse(text)?.Kind);
    }

    [Theory]
    [InlineData("open downloads", "downloads")]
    [InlineData("open my documents", "documents")]
    [InlineData("open the desktop folder", "desktop")]
    [InlineData("Open Pictures.", "pictures")]
    [InlineData("open music", "music")]
    [InlineData("open videos folder", "videos")]
    public void Resolves_known_folders(string text, string folder)
    {
        var plan = LaunchResolver.Resolve(text, Apps);
        Assert.Equal(new LaunchRequest(LaunchKind.Folder, folder), plan!.Request);
    }
}

public class SnippetTests
{
    static readonly string[] Triggers = { ";date", ";d", ";sig", ";shrug" };

    [Fact]
    public void Buffer_matches_longest_trigger_at_the_end()
    {
        var b = new SnippetBuffer();
        foreach (var c in "hello ;dat") b.Append(c, 0);
        Assert.Equal(";d", SnippetText.MatchTrigger("x;d", Triggers));
        Assert.Null(b.MatchTrigger(Triggers));
        b.Append('e', 10);
        Assert.Equal(";date", b.MatchTrigger(Triggers));
    }

    [Fact]
    public void Buffer_matches_case_insensitively()
    {
        Assert.Equal(";sig", SnippetText.MatchTrigger("thanks ;SIG", Triggers));
        Assert.Null(SnippetText.MatchTrigger(";sig ", Triggers));
    }

    [Fact]
    public void Buffer_keeps_the_last_40_characters()
    {
        var b = new SnippetBuffer();
        foreach (var c in new string('a', 100) + "xyz") b.Append(c, 0);
        Assert.Equal(SnippetBuffer.Capacity, b.Length);
        Assert.EndsWith("xyz", b.Text);
    }

    [Fact]
    public void Buffer_backspace_and_reset()
    {
        var b = new SnippetBuffer();
        foreach (var c in ";sigx") b.Append(c, 0);
        b.Backspace();
        Assert.Equal(";sig", b.MatchTrigger(Triggers));
        b.Reset();
        Assert.Equal("", b.Text);
        b.Backspace();
        Assert.Equal(0, b.Length);
    }

    [Fact]
    public void Buffer_resets_after_a_pause()
    {
        var b = new SnippetBuffer();
        b.Append(';', 1000);
        b.Append('s', 2000);
        b.Append('i', 5001);
        Assert.Equal("i", b.Text);
        b.Append('g', 8000);
        Assert.Equal("ig", b.Text);
    }

    [Fact]
    public void Expands_tokens()
    {
        var now = new DateTime(2026, 11, 2, 9, 5, 0);
        Assert.Equal("2026-11-02 09:05 pasted", SnippetText.Expand("{date} {TIME} {clipboard}", now, () => "pasted"));
        Assert.Equal("a\nb", SnippetText.Expand("a\r\nb", now, () => null));
        Assert.Equal("", SnippetText.Expand("{clipboard}", now, () => null));
    }

    [Fact]
    public void Clipboard_is_only_read_when_used()
    {
        bool read = false;
        SnippetText.Expand("Best regards", DateTime.Now, () => { read = true; return "x"; });
        Assert.False(read);
    }

    [Fact]
    public void Defaults_include_date_time_and_shrug()
    {
        var d = SnippetText.Defaults();
        Assert.Equal("{date}", d[";DATE"]);
        Assert.Equal("{time}", d[";time"]);
        Assert.Equal("¯\\_(ツ)_/¯", d[";shrug"]);
    }

    [Fact]
    public void Parses_add()
    {
        var c = SnippetCommands.Parse("snippet ;Sig Best regards, Roushan");
        Assert.Equal(new SnippetCommand(SnippetAction.Add, ";sig", "Best regards, Roushan"), c);
        Assert.Equal("Line one\nLine two", SnippetCommands.Parse(@"add snippet ;two Line one\nLine two")!.Expansion);
    }

    [Theory]
    [InlineData("delete snippet ;sig", ";sig")]
    [InlineData("Remove snippet sig", ";sig")]
    [InlineData("delete the snippet ;SIG", ";sig")]
    public void Parses_delete(string text, string trigger)
    {
        Assert.Equal(new SnippetCommand(SnippetAction.Delete, trigger), SnippetCommands.Parse(text));
    }

    [Theory]
    [InlineData("snippets")]
    [InlineData("Show snippets")]
    [InlineData("show my snippets")]
    [InlineData("my text snippets")]
    public void Parses_list(string text)
    {
        Assert.Equal(SnippetAction.List, SnippetCommands.Parse(text)!.Action);
    }

    [Theory]
    [InlineData("snippet sig Best regards")]
    [InlineData("snippet ;sig")]
    [InlineData("snippet ; Best regards")]
    [InlineData("code snippets for python")]
    [InlineData("what is a snippet")]
    [InlineData("snippets of conversation")]
    public void Ignores_other_text(string text)
    {
        Assert.Null(SnippetCommands.Parse(text));
    }

    [Theory]
    [InlineData(";a", true)]
    [InlineData(";email", true)]
    [InlineData(";", false)]
    [InlineData("sig", false)]
    [InlineData(";a b", false)]
    public void Validates_triggers(string trigger, bool valid)
    {
        Assert.Equal(valid, SnippetText.IsValidTrigger(trigger));
    }
}
