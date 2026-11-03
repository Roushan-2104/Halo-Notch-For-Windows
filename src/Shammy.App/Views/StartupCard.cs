using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shammy.Modules.Productivity;

namespace Shammy.Views;

/// <summary>The one-time question after installing: should Shammy start with Windows? Yes / Not now.</summary>
internal sealed class StartupCard : Grid
{
    public const double CardWidth = 460, CardHeight = 96;

    public StartupCard(Action yes, Action no)
    {
        Width = CardWidth;
        Height = CardHeight;
        Background = Brushes.Transparent;

        var grid = new Grid { Margin = new Thickness(16, 12, 14, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Children.Add(grid);

        var icon = new TextBlock
        {
            Text = "", // power
            FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
            FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        grid.Children.Add(new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(11),
            Background = Res("Fill1"),
            VerticalAlignment = VerticalAlignment.Center,
            Child = icon,
        });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = "Welcome to Shammy", FontSize = 11.5, Foreground = Res("Fg2") });
        text.Children.Add(new TextBlock
        {
            Text = "Start Shammy when Windows starts?",
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Res("Fg"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 1, 0, 0),
        });
        text.Children.Add(new TextBlock { Text = "You can change this any time in the tray menu", FontSize = 11, Foreground = Res("Fg3"), Margin = new Thickness(0, 2, 0, 0) });
        SetColumn(text, 2);
        grid.Children.Add(text);

        var buttons = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        buttons.Children.Add(ReminderCard.Pill("Yes", primary: true, yes));
        var later = ReminderCard.Pill("Not now", primary: false, no);
        later.Margin = new Thickness(0, 6, 0, 0);
        buttons.Children.Add(later);
        SetColumn(buttons, 3);
        grid.Children.Add(buttons);
    }

    static Brush Res(string key) => (Brush)Application.Current.Resources[key];
}
