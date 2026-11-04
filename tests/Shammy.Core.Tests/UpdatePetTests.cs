using Shammy.Core.Notch;

namespace Shammy.Core.Tests;

public class UpdatePetTests
{
    static readonly PetActivityState Update = new() { Id = "update-ready", Costume = PetCostume.Update, Label = "Update", Value = "0.5.4" };
    static readonly PetActivityState Call = new() { Id = "call", Costume = PetCostume.Call };
    static readonly PetActivityState Music = new() { Id = "media", Costume = PetCostume.Music, Playing = true };

    [Fact]
    public void A_ready_update_has_a_restart_button_in_the_accent_colour()
    {
        var circle = PetActivities.Circle(Update);
        Assert.NotNull(circle);
        Assert.Equal(CircleAction.Restart, circle!.Action);
        Assert.Equal(CircleTone.Accent, circle.Tone);
        Assert.True(circle.Enabled);
        Assert.Equal("Restart to update", circle.Description);
    }

    [Fact]
    public void It_stays_on_the_stage_rather_than_being_a_passing_moment() =>
        Assert.True(PetActivities.IsOngoing(Update));

    [Fact]
    public void Clicking_the_pet_never_restarts_by_accident() =>
        Assert.False(PetActivities.BodyClickRunsActivity(Update));

    [Fact]
    public void A_call_keeps_the_pet_and_the_update_waits_as_the_dot()
    {
        var stage = PetActivities.Stage(new[] { Call, Update }, preferred: null);
        Assert.Equal("call", stage.Owner);
        Assert.Equal("update-ready", stage.Dot);
    }

    [Fact]
    public void Above_music_it_shows_the_button_and_music_is_one_click_away()
    {
        var stage = PetActivities.Stage(new[] { Update, Music }, preferred: null);
        Assert.Equal("update-ready", stage.Owner);
        Assert.Equal("media", stage.Dot);
        Assert.Equal("media", PetActivities.Stage(new[] { Update, Music }, preferred: "media").Owner);
    }
}
