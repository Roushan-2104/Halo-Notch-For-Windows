using Shammy.Core.Search;

namespace Shammy.Core.Tests;

public class SpeechFixesTests
{
    [Theory]
    [InlineData("Whether in Mumbai tomorrow.", "Weather in Mumbai tomorrow.")]
    [InlineData("whether today", "weather today")]
    [InlineData("Whether.", "Weather.")]
    [InlineData("What's the whether in Delhi?", "What's the weather in Delhi?")]
    [InlineData("tomorrow's whether", "tomorrow's weather")]
    [InlineData("How is the Whether like", "How is the Weather like")]
    [InlineData("Time or 10 minutes.", "Timer 10 minutes.")]
    [InlineData("timeout 5 minutes", "timer 5 minutes")]
    [InlineData("Time of 2 hours", "Timer 2 hours")]
    public void Fixes_weather(string spoken, string expected)
    {
        Assert.Equal(expected, SpeechFixes.Apply(spoken));
    }

    [Theory]
    [InlineData("Whether or not to buy a laptop")]
    [InlineData("whether to use python or rust")]
    [InlineData("ask him whether in fact it works")]
    [InlineData("")]
    [InlineData("time or money")]
    [InlineData("timeout error in python")]
    [InlineData("time of 5 pm meeting")]
    public void Leaves_real_whether(string spoken)
    {
        Assert.Equal(spoken, SpeechFixes.Apply(spoken));
    }
}
