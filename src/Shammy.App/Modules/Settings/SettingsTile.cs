using System.Windows;
using Shammy.Views.Cards;

namespace Shammy.Modules.Settings;

/// <summary>
/// The Settings tile (Wall.dc.html's list field with the big word "Prefs"). Settings is opened on purpose from its
/// chip, so the tile is never on the rail by itself; it exists so the card reads like every other one.
/// </summary>
internal static class SettingsTile
{
    public static FrameworkElement Build(Action open)
    {
        var tile = new WallTile { Field = Fields.List() };
        var big = CardKit.Poster("Prefs", 40);
        var label = CardKit.Label("Shammy");
        label.Margin = new Thickness(0, 6, 0, 0);
        var top = CardKit.Column(0, big, label);
        tile.Art.Children.Add(CardKit.At(top, HorizontalAlignment.Left, VerticalAlignment.Top, WallTile.Pad, WallTile.Pad));
        tile.Rest.Children.Add(WallTile.BottomLeft(CardKit.Label("Settings")));
        var words = WallTile.Words("Settings", "Hover, keys, voice, privacy", out _, out _);
        tile.Lit.Children.Add(CardKit.At(words, HorizontalAlignment.Stretch, VerticalAlignment.Bottom, WallTile.Pad, 0, WallTile.Pad, 10));
        tile.Lit.Children.Add(WallTile.OpenButton("Open settings", open));
        CardKit.Name(tile, "Settings. Open settings");
        return tile;
    }
}
