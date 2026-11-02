using Halo.Core.Conversions;
using Halo.Core.Launcher;

namespace Halo.Core.Tests;

/// <summary>Edge cases found in the full feature sweep.</summary>
public class MoneyEdgeTests
{
    [Theory]
    [InlineData("1.299 €", 1299, "EUR")]
    [InlineData("€1.299", 1299, "EUR")]
    [InlineData("1.299,99 €", 1299.99, "EUR")]
    [InlineData("12.500.000 ₹", 12500000, "INR")]
    [InlineData("$49.99", 49.99, "USD")]
    [InlineData("15,50 €", 15.5, "EUR")]
    [InlineData("$1,299.99", 1299.99, "USD")]
    [InlineData("rs.500", 500, "INR")]
    [InlineData("1.5 lakh rupees", 150000, "INR")]
    public void Prices_in_both_number_styles(string text, double amount, string code)
    {
        Assert.True(Currency.TryParseMoney(text, requireAmount: true, out var money), text);
        Assert.Equal(code, money.Code);
        Assert.Equal(amount, money.Amount, 6);
    }
}

public class SnippetClashTests
{
    static readonly string[] Existing = { ";date", ";time", ";shrug" };

    [Theory]
    [InlineData(";d", ";date")]
    [InlineData(";dates", ";date")]
    [InlineData(";s", ";shrug")]
    public void Prefix_triggers_clash(string trigger, string clash) => Assert.Equal(clash, SnippetText.Clash(trigger, Existing));

    [Theory]
    [InlineData(";date")]
    [InlineData(";DATE")]
    [InlineData(";sig")]
    [InlineData(";x")]
    public void Replacing_or_unrelated_triggers_dont_clash(string trigger) => Assert.Null(SnippetText.Clash(trigger, Existing));
}
