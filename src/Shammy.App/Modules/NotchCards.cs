using System.Windows;

namespace Shammy.Modules;

/// <summary>
/// A card on the open notch's home row (the v2 design): a compact view that is always 176 logical px tall and
/// <see cref="Width"/> wide, which opens in place into a 924×248 detail view with a Back button the notch adds.
/// Cards replace the old 9 tabs; modules register them with <see cref="ModuleContext.AddCard"/>.
/// </summary>
public sealed class NotchCard
{
    /// <summary>Stable id ("music", "calendar", "timer", "weather", "battery", …), also used by test pipes.</summary>
    public required string Id { get; init; }

    /// <summary>Shown as the chip label and the detail view's title.</summary>
    public required string Title { get; init; }

    /// <summary>One of the design's widths: 340 (wide), 260 (medium) or 176 (square).</summary>
    public required double Width { get; init; }

    /// <summary>The compact card, created once. The notch frames it (background, radius, border) — draw content only.</summary>
    public required FrameworkElement Compact { get; init; }

    /// <summary>Builds the 924×248 detail view; called each time the card opens, so it shows fresh data.</summary>
    public required Func<FrameworkElement> Detail { get; init; }

    /// <summary>Lower comes first on the home row.</summary>
    public int Order { get; init; } = 100;

    /// <summary>
    /// Whether the card earns a place on the home row right now (music only while something plays, a timer only
    /// while one runs…). Cards that aren't relevant are still reachable from the chips. Call
    /// <see cref="ModuleContext.CardsChanged"/> when the answer changes.
    /// </summary>
    public Func<bool> IsRelevant { get; init; } = () => true;

    /// <summary>Show a chip for this card in the top row too (for cards people open on purpose: Clipboard, Mirror…).</summary>
    public bool Chip { get; init; }
}

/// <summary>The design's colours, one accent per kind of activity (see the v2 mockup).</summary>
public static class NotchPalette
{
    public const string Music = "#FF8A3D", Calendar = "#4C9AFF", Timer = "#5AC8FA", Battery = "#34C759",
        Weather = "#FFC233", Purple = "#B48CFF", Alarm = "#FF453A",
        CardBackground = "#121214", CardStroke = "#1F1F23", Text = "#F5F5F7", Text2 = "#A1A1A6";

    public const double CardHeight = 176, DetailWidth = 924, DetailHeight = 248;
}
