using System.Xml.Linq;
using Shammy.Core.Updates;

namespace Shammy.Core.Tests;

public class WhatsNewTests
{
    [Fact]
    public void A_fresh_install_shows_nothing() =>
        Assert.Empty(WhatsNew.LinesFor(null, "0.5.5", existingUser: false));

    [Fact]
    public void An_update_shows_that_versions_lines() =>
        Assert.Equal(WhatsNew.Notes["0.5.5"], WhatsNew.LinesFor("0.5.4", "0.5.5", existingUser: true));

    [Fact]
    public void Someone_updating_from_before_the_note_existed_sees_it() =>
        Assert.Equal(WhatsNew.Notes["0.5.5"], WhatsNew.LinesFor(null, "0.5.5", existingUser: true));

    [Theory]
    [InlineData("0.5.5", "0.5.5")]
    [InlineData("0.5.6", "0.5.5")]
    public void It_shows_once_and_never_on_a_downgrade(string last, string current) =>
        Assert.Empty(WhatsNew.LinesFor(last, current, existingUser: true));

    [Fact]
    public void A_version_without_lines_shows_nothing() =>
        Assert.Empty(WhatsNew.LinesFor("9.0.0", "9.0.1", existingUser: true));

    [Theory]
    [InlineData("0.5.5+23e7bb39", 0, 5, 5)]
    [InlineData("0.5.5-beta", 0, 5, 5)]
    [InlineData(" 1.2.3 ", 1, 2, 3)]
    public void Versions_parse_with_their_extras(string text, int major, int minor, int build) =>
        Assert.Equal(new Version(major, minor, build), WhatsNew.Parse(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("soon")]
    public void Nonsense_versions_show_nothing(string? current) =>
        Assert.Empty(WhatsNew.LinesFor("0.5.4", current ?? "", existingUser: true));

    [Fact]
    public void Every_note_fits_the_card()
    {
        foreach (var (version, lines) in WhatsNew.Notes)
        {
            Assert.NotNull(WhatsNew.Parse(version));
            Assert.InRange(lines.Length, 1, WhatsNew.MaxLines);
            Assert.All(lines, line => Assert.InRange(line.Length, 1, WhatsNew.MaxLineLength));
        }
    }

    /// <summary>A release can't go out silently: bumping the version without writing its lines fails here.</summary>
    [Fact]
    public void The_version_being_built_has_its_lines()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var version = XDocument.Load(Path.Combine(dir!.FullName, "Directory.Build.props")).Descendants("Version").First().Value;
        var newest = WhatsNew.Notes.Keys.Select(k => WhatsNew.Parse(k)!).Max();
        Assert.True(WhatsNew.Parse(version) <= newest, $"Add what's-new lines for {version} in WhatsNew.cs");
    }
}
