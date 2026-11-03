using Shammy.Core.Cards;
using Shammy.Core.Notch;

namespace Shammy.Core.Tests;

public class BatteryLevelTests
{
    [Theory]
    [InlineData(20, true)]
    [InlineData(5, true)]
    [InlineData(21, false)]
    [InlineData(29, false)]
    public void Glance_pet_and_card_agree_on_low(int percent, bool low)
    {
        Assert.Equal(low, BatteryLevels.IsLow(percent));
        Assert.Equal(low, BuiltInGlances.Battery(true, percent, charging: false) is not null);
        Assert.Equal(low, PetMoods.Decide(new PetInputs { Now = DateTime.UtcNow, LocalNow = new DateTime(2026, 11, 4, 12, 0, 0), BatteryPercent = percent }) == PetMood.Tired);
        Assert.Equal(low, PowerText.IsLow(percent, pluggedIn: false));
    }

    [Fact]
    public void Warnings_come_at_20_10_and_5_once_each()
    {
        int last = 101;
        var warned = new List<int>();
        foreach (var p in new[] { 25, 21, 20, 19, 15, 10, 9, 6, 5, 4 })
        {
            if (BatteryLevels.WarningFor(p, last) is { } level)
            {
                warned.Add(level);
                last = level;
            }
        }
        Assert.Equal(new[] { 20, 10, 5 }, warned);
    }

    [Fact]
    public void A_big_drop_warns_once_for_the_level_it_reached()
    {
        Assert.Equal(10, BatteryLevels.WarningFor(8, 101));
        Assert.Null(BatteryLevels.WarningFor(8, 10));
        Assert.Equal(5, BatteryLevels.WarningFor(3, 20));
        Assert.Null(BatteryLevels.WarningFor(40, 101));
    }
}
