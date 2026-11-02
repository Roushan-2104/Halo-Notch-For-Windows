using Halo.Core.Conversions;

namespace Halo.Modules.SelectionTools;

/// <summary>What a conversion produced, plus a page to open (translations), kept apart so the test pipe can dry-run.</summary>
internal sealed record ConvertOutcome(CommandResult Result, string? OpenUrl = null);

/// <summary>
/// Typed or spoken conversions: units ("5 ft in cm"), money ("100 usd to inr"), time zones ("5pm ist in pst",
/// "time in tokyo") and translation ("translate good morning to hindi"). <see cref="CanHandle"/> only accepts
/// phrases that parse completely, so ordinary searches never get captured.
/// </summary>
internal sealed class ConvertCommand : ITextCommand
{
    readonly ModuleContext _ctx;
    readonly ExchangeRates _rates;

    public ConvertCommand(ModuleContext ctx, ExchangeRates rates)
    {
        _ctx = ctx;
        _rates = rates;
    }

    public bool CanHandle(string text) => Parse(text) is not null;

    public async Task<CommandResult> RunAsync(string text)
    {
        var outcome = await EvaluateAsync(text);
        if (outcome is null) return new CommandResult(Glyphs.Error, "Couldn't convert that", text, IsError: true);
        // A command can't open the results card, so translations go to the browser.
        if (outcome.OpenUrl is not null) _ctx.Open(outcome.OpenUrl);
        return outcome.Result;
    }

    static object? Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (WorldClock.TryParse(text, out var time)) return time;
        if (Currency.TryParseQuery(text, out var money)) return money;
        if (Units.TryParseQuery(text, out var units)) return units;
        if (Translation.TryParse(text, out var translate)) return translate;
        return null;
    }

    /// <summary>Works out the answer without side effects. Safe off the UI thread.</summary>
    public async Task<ConvertOutcome?> EvaluateAsync(string text)
    {
        switch (Parse(text))
        {
            case TimeQuery time:
                try
                {
                    var answer = WorldClock.Answer(time, DateTime.UtcNow, TimeZoneInfo.Local);
                    return new ConvertOutcome(new CommandResult(Glyphs.Clock, answer.Title, answer.Detail));
                }
                catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
                {
                    _ctx.Warn("Time zone lookup failed", ex);
                    return new ConvertOutcome(new CommandResult(Glyphs.Error, "Couldn't find that time zone", ex.Message, IsError: true));
                }
            case CurrencyQuery money:
                return new ConvertOutcome(await ConvertMoneyAsync(money).ConfigureAwait(false));
            case UnitConversion units:
                return new ConvertOutcome(new CommandResult(Glyphs.Measure, units.Title, units.Detail));
            case TranslateRequest translate:
                return new ConvertOutcome(new CommandResult(Glyphs.Translate, "Translating\u2026", translate.Language),
                    Translation.BuildUrl(translate.Text, translate.LanguageCode).AbsoluteUri);
            default:
                return null;
        }
    }

    async Task<CommandResult> ConvertMoneyAsync(CurrencyQuery q)
    {
        var table = await _rates.GetAsync().ConfigureAwait(false);
        if (table is null) return new CommandResult(Glyphs.Error, "Couldn't get exchange rates", "Check your connection", IsError: true);
        if (!Currency.TryConvert(q.From.Amount, q.From.Code, q.To, table.Rates, out var value)
            || !Currency.TryConvert(1, q.From.Code, q.To, table.Rates, out var rate))
            return new CommandResult(Glyphs.Error, $"No rate for {q.From.Code} to {q.To}", null, IsError: true);
        return new CommandResult(Glyphs.Money, Currency.Format(value, q.To),
            $"{Currency.Format(q.From.Amount, q.From.Code)} \u00B7 1 {q.From.Code} = {Currency.Format(rate, q.To)}");
    }
}
