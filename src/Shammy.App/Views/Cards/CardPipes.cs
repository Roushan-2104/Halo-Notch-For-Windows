using Shammy.Modules;

namespace Shammy.Views.Cards;

/// <summary>
/// The "card-state &lt;id&gt;" and "card-detail &lt;id&gt;" test pipes, shared by every card: each card says in one
/// line what its compact view and its detail view show, so the cards can be checked live without a screenshot.
/// </summary>
internal static class CardPipes
{
    static readonly Dictionary<string, (Func<string> Compact, Func<string> Detail)> Cards = new(StringComparer.OrdinalIgnoreCase);

    public static void Register(ModuleContext ctx, string id, Func<string> compact, Func<string> detail)
    {
        Cards[id] = (compact, detail);
        // Every card module registers the same two handlers; the last registration wins, which is the same code.
        ctx.AddPipeCommand("card-state", arg => Describe(arg, detail: false));
        ctx.AddPipeCommand("card-detail", arg => Describe(arg, detail: true));
    }

    static string Describe(string arg, bool detail)
    {
        var id = arg.Trim();
        if (!Cards.TryGetValue(id, out var card))
            return $"error: usage card-{(detail ? "detail" : "state")} <{string.Join("|", Cards.Keys.Order())}>";
        try
        {
            return detail ? card.Detail() : card.Compact();
        }
        catch (Exception ex)
        {
            return "error: " + ex.Message;
        }
    }
}
