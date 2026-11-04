using Shammy.Core.Updates;

namespace Shammy.Core.Tests;

public class UpdateTextTests
{
    [Theory]
    [InlineData("not checked", "Check for updates")]
    [InlineData("checking", "Checking for updates…")]
    [InlineData("downloading 0.5.3 42", "Downloading 0.5.3… 42%")]
    [InlineData("ready 0.5.3", "Restart to update to 0.5.3")]
    [InlineData("up to date", "Shammy is up to date")]
    [InlineData("check failed: timed out", "Couldn’t check · try again")]
    [InlineData("something new", "Check for updates")]
    public void Tray_always_says_where_a_check_stands(string result, string label) =>
        Assert.Equal(label, UpdateText.TrayLabel(result));

    [Theory]
    [InlineData("checking", "Checking…")]
    [InlineData("downloading 0.5.3 7", "Downloading 0.5.3 · 7%")]
    [InlineData("ready 0.5.3", "Update 0.5.3 is ready")]
    [InlineData("up to date", "Up to date")]
    [InlineData("check failed: HttpRequestException", "Couldn’t check · tries again in 30 minutes")]
    [InlineData("not checked", "Checks by itself every few hours")]
    public void Settings_hint_follows_the_status(string result, string hint) =>
        Assert.Equal(hint, UpdateText.Hint(result, installed: true));

    [Fact]
    public void A_copy_that_is_not_installed_says_so_whatever_the_status() =>
        Assert.Equal("Updates come with the installed copy.", UpdateText.Hint("ready 0.5.3", installed: false));

    [Theory]
    [InlineData("checking", true)]
    [InlineData("downloading 0.5.3 42", true)]
    [InlineData("ready 0.5.3", false)]
    [InlineData("up to date", false)]
    [InlineData("check failed: timed out", false)]
    [InlineData("not checked", false)]
    public void Only_a_running_check_counts_as_busy(string result, bool busy) =>
        Assert.Equal(busy, UpdateText.IsBusy(result));

    [Fact]
    public void A_failed_check_retries_sooner_than_a_good_one()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), UpdateText.NextCheck(UpdateText.Failed("timed out")));
        Assert.Equal(TimeSpan.FromHours(4), UpdateText.NextCheck(UpdateText.UpToDate));
        Assert.Equal(TimeSpan.FromHours(4), UpdateText.NextCheck(UpdateText.Ready("0.5.3")));
    }

    [Theory]
    [InlineData(-5, "downloading 1.0.0 0")]
    [InlineData(150, "downloading 1.0.0 100")]
    public void Download_percent_stays_in_range(int percent, string expected) =>
        Assert.Equal(expected, UpdateText.Downloading("1.0.0", percent));

    [Fact]
    public void Finished_checks_say_how_they_ended()
    {
        Assert.Equal("An update is ready", UpdateText.Said(UpdateText.Ready("0.5.3")));
        Assert.Equal("You have the latest version", UpdateText.Said(UpdateText.UpToDate));
        Assert.Equal("Couldn’t check for updates", UpdateText.Said(UpdateText.Failed("timed out")));
    }
}
