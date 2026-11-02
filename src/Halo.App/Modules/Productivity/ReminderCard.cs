using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Halo.Modules.Productivity;

/// <summary>A due reminder as it drops out of the notch, laid out like a notification: bell, text, Done and Snooze.</summary>
internal sealed class ReminderCard : Grid
{
    public const double CardWidth = 460, CardHeight = 96;

    public ReminderCard(ReminderItem reminder, Action done, Action snooze)
    {
        Reminder = reminder;
        Width = CardWidth;
        Height = CardHeight;
        Background = Brushes.Transparent;

        var grid = new Grid { Margin = new Thickness(16, 12, 14, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Children.Add(grid);

        var bell = new TextBlock
        {
            Text = "\uEA8F",
            FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
            FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        bell.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        grid.Children.Add(new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(11),
            Background = Res("Fill1"),
            VerticalAlignment = VerticalAlignment.Center,
            Child = bell,
        });

        var header = new Grid();
        header.Children.Add(new TextBlock { Text = "Reminder", FontSize = 11.5, Foreground = Res("Fg2") });
        header.Children.Add(new TextBlock
        {
            Text = reminder.Due.ToString("h:mm tt", CultureInfo.InvariantCulture),
            FontSize = 11.5,
            Foreground = Res("Fg3"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 4, 0),
        });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(header);
        text.Children.Add(new TextBlock
        {
            Text = reminder.Title,
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Res("Fg"),
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 38,
            Margin = new Thickness(0, 1, 0, 0),
        });
        SetColumn(text, 2);
        grid.Children.Add(text);

        var buttons = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        buttons.Children.Add(Pill("Done", primary: true, done));
        var later = Pill("Snooze 10 min", primary: false, snooze);
        later.Margin = new Thickness(0, 6, 0, 0);
        buttons.Children.Add(later);
        SetColumn(buttons, 3);
        grid.Children.Add(buttons);
    }

    public ReminderItem Reminder { get; }

    /// <summary>A rounded text button; Borders rather than Buttons so the card never takes keyboard focus for them.</summary>
    internal static Border Pill(string label, bool primary, Action onClick)
    {
        var text = new TextBlock
        {
            Text = label,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var pill = new Border
        {
            Height = 26,
            MinWidth = 96,
            CornerRadius = new CornerRadius(13),
            Padding = new Thickness(12, 0, 12, 0),
            Cursor = Cursors.Hand,
            Child = text,
        };
        if (primary)
        {
            pill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            text.SetResourceReference(TextBlock.ForegroundProperty, "OnAccentBrush");
            pill.MouseEnter += (_, _) => pill.Opacity = 0.85;
            pill.MouseLeave += (_, _) => pill.Opacity = 1;
        }
        else
        {
            pill.Background = Res("Fill1");
            text.Foreground = Res("Fg");
            pill.MouseEnter += (_, _) => pill.Background = Res("Fill2");
            pill.MouseLeave += (_, _) => pill.Background = Res("Fill1");
        }
        pill.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            onClick();
        };
        return pill;
    }

    static Brush Res(string key) => (Brush)Application.Current.Resources[key];
}
