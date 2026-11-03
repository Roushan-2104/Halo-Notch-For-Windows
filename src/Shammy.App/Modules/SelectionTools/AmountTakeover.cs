using Shammy.Core.Conversions;

namespace Shammy.Modules.SelectionTools;

/// <summary>
/// Circle just a price or a measurement ("$49.99", "₹1,299", "5 ft 10 in", "350°F") and see it converted
/// instead of searched: money to rupees (or dollars if it's already rupees), units across metric/imperial.
/// </summary>
internal sealed class AmountTakeover : ISelectionHandler
{
    // A circle shouldn't hang on a slow network; without rates the normal search runs instead.
    static readonly TimeSpan RatesWait = TimeSpan.FromMilliseconds(2500);

    readonly ModuleContext _ctx;
    readonly ExchangeRates _rates;

    public AmountTakeover(ModuleContext ctx, ExchangeRates rates)
    {
        _ctx = ctx;
        _rates = rates;
    }

    public string Last { get; private set; } = "-";

    public async Task<CommandResult?> TryTakeOverAsync(SelectionInfo selection)
    {
        var result = await EvaluateAsync(selection.Text, waitForRates: false);
        if (result is not null) Last = $"{result.Title} ({result.Detail})";
        return result;
    }

    /// <summary>The conversion for a circled text, or null if it isn't just an amount. Never throws.</summary>
    public async Task<CommandResult?> EvaluateAsync(string? text, bool waitForRates)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 60) return null;
        try
        {
            if (Currency.TryReadPrice(text, out var money))
            {
                var to = Currency.DefaultTarget(money.Code);
                var pending = _rates.GetAsync();
                if (!waitForRates && await Task.WhenAny(pending, Task.Delay(RatesWait)) != pending)
                {
                    _ctx.Warn("Exchange rates too slow, searching instead");
                    return null;
                }
                var table = await pending;
                if (table is null || !Currency.TryConvert(money.Amount, money.Code, to, table.Rates, out var value)) return null;
                return new CommandResult(Glyphs.Money, Currency.Format(value, to), Currency.Format(money.Amount, money.Code));
            }
            if (Units.TryReadMeasurement(text, out var conversion))
                return new CommandResult(Glyphs.Measure, conversion.Title, conversion.Detail);
        }
        catch (Exception ex)
        {
            _ctx.Warn("Converting a circled amount failed", ex);
        }
        return null;
    }
}
