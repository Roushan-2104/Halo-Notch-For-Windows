using Shammy.Core.Conversions;

namespace Shammy.Modules.SelectionTools;

/// <summary>What a conversion produced, plus a page to open (translations) and what it's about, kept apart so the test pipe can dry-run.</summary>
internal sealed record ConvertOutcome(CommandResult Result, string? OpenUrl = null, string? OpenQuery = null);

/// <summary>
/// Typed or spoken conversions: units ("5 ft in cm"), money ("100 usd to inr"), time zones ("5pm ist in pst",
/// "time in tokyo") and translation ("translate good morning to hindi"). <see cref="CanHandle"/> only accepts
/// phrases that parse completely, so ordinary searches never get captured.
/// </summary>
internal sealed class ConvertCommand : ITextCommand, ICommandPreview
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
        // Translations open in the notch's results card, as the Translate chip does (not the browser).
        if (outcome.OpenUrl is { } url) return outcome.Result with { OpenInResults = new ResultsPage(new Uri(url), outcome.OpenQuery ?? text) };
        return outcome.Result;
    }

    /// <summary>The answer itself when it needs no network (units, time zones); otherwise what will happen.</summary>
    public CommandPreview? Preview(string text)
    {
        switch (Parse(text))
        {
            case TimeQuery time:
                try
                {
                    var answer = WorldClock.Answer(time, DateTime.UtcNow, TimeZoneInfo.Local);
                    return new CommandPreview(Glyphs.Clock, answer.Title, answer.Detail);
                }
                catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
                {
                    return null;
                }
            case UnitConversion units:
                return new CommandPreview(Glyphs.Measure, units.Title, units.Detail);
            case CurrencyQuery money:
                // The answer itself from the rates already cached (fetched at most an hour or two ago); Enter refreshes them.
                var from = Currency.Format(money.From.Amount, money.From.Code);
                if (_rates.Cached() is { } table
                    && Currency.TryConvert(money.From.Amount, money.From.Code, money.To, table.Rates, out var value)
                    && Currency.TryConvert(1, money.From.Code, money.To, table.Rates, out var rate))
                    return new CommandPreview(Glyphs.Money, $"{Currency.Format(value, money.To)} · {from} → {money.To}",
                        $"1 {money.From.Code} = {Currency.Format(rate, money.To)}{(table.Date.Length > 0 ? $" · rates of {table.Date}" : "")}");
                // None yet: fetch in the background (once; ExchangeRates dedups and backs off) so the next keystroke has them.
                _ = _rates.GetAsync();
                return new CommandPreview(Glyphs.Money, $"Convert {from} to {money.To}", "Live exchange rate");
            case TranslateRequest translate:
                return new CommandPreview(Glyphs.Translate, $"Translate to {translate.Language}", translate.Text);
            default:
                return null;
        }
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
                    return new ConvertOutcome(new CommandResult(Glyphs.Clock, answer.Title, answer.Detail) { CopyText = answer.Title });
                }
                catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
                {
                    _ctx.Warn("Time zone lookup failed", ex);
                    return new ConvertOutcome(new CommandResult(Glyphs.Error, "Couldn't find that time zone", ex.Message, IsError: true));
                }
            case CurrencyQuery money:
                return new ConvertOutcome(await ConvertMoneyAsync(money).ConfigureAwait(false));
            case UnitConversion units:
                return new ConvertOutcome(new CommandResult(Glyphs.Measure, units.Title, units.Detail) { CopyText = units.Title });
            case TranslateRequest translate:
                return new ConvertOutcome(new CommandResult(Glyphs.Translate, "Translating\u2026", translate.Language),
                    Translation.BuildUrl(translate.Text, translate.LanguageCode).AbsoluteUri, translate.Text);
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
        var shown = Currency.Format(value, q.To);
        return new CommandResult(Glyphs.Money, shown,
            $"{Currency.Format(q.From.Amount, q.From.Code)} \u00B7 1 {q.From.Code} = {Currency.Format(rate, q.To)}") { CopyText = shown };
    }
}
