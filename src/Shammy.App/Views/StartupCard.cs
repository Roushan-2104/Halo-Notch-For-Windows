using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Shammy.Modules.Productivity;

namespace Shammy.Views;

/// <summary>The one-time question after installing: should Shammy start with Windows? Yes / Not now.</summary>
internal sealed class StartupCard : Grid
{
    // A little taller than a notification so the reassurance line fits under the two-line question.
    public const double CardWidth = 460, CardHeight = 112;

    public StartupCard(Action yes, Action no)
    {
        Width = CardWidth;
        Height = CardHeight;
        Background = Brushes.Transparent;

        var title = WallParts.Text("Start Shammy when Windows starts?", 14, FontWeights.Bold);
        title.TextWrapping = TextWrapping.Wrap;
        var line = WallParts.Text("You can change this any time in the tray menu", 11.5, FontWeights.Normal, "WallText2");
        line.TextWrapping = TextWrapping.Wrap;
        AutomationProperties.SetName(this, "Welcome to Shammy. " + title.Text);

        Children.Add(ReminderCard.Layout(
            WallParts.ArtTile(WallParts.Glyph("", 20), 56), // power
            "Welcome to Shammy",
            meta: null,
            title,
            line,
            WallParts.Primary("Yes", yes, 30),
            WallParts.Quiet("Not now", no, 30)));
    }
}
