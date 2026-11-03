using Shammy.Core.Cards;

namespace Shammy.Core.Tests;

public class CardPhraseTests
{
    [Theory]
    [InlineData("calendar")]
    [InlineData("My meetings")]
    [InlineData("what's next")]
    [InlineData("Whats next?")]
    [InlineData("what’s next")]
    [InlineData("when is my next meeting")]
    [InlineData("show my calendar please")]
    public void Opens_the_calendar(string text) => Assert.True(CardPhrases.IsCalendar(text));

    [Theory]
    [InlineData("calendar app settings")]
    [InlineData("what's next in the f1 season")]
    [InlineData("next")]
    [InlineData("meetings in zoom")]
    public void Leaves_other_calendar_words_alone(string text) => Assert.False(CardPhrases.IsCalendar(text));

    [Theory]
    [InlineData("mirror")]
    [InlineData("How do I look?")]
    [InlineData("open camera")]
    [InlineData("Open the camera.")]
    public void Opens_the_mirror(string text) => Assert.True(CardPhrases.IsMirror(text));

    [Theory]
    [InlineData("open camera settings")]
    [InlineData("mirror's edge")]
    [InlineData("how do i look up a word")]
    public void Leaves_other_camera_phrases_alone(string text) => Assert.False(CardPhrases.IsMirror(text));
}
