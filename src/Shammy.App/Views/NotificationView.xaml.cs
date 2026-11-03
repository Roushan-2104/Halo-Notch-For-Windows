using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shammy.Notifications;

namespace Shammy.Views;

/// <summary>
/// A notification as it drops out of the notch: the app's avatar as artwork, a tracked app name, a bold title and two
/// lines of text. Hovering holds it (the window keeps it up) and reveals Dismiss.
/// </summary>
public partial class NotificationView : UserControl
{
    ShammyNotification? _shown;

    public NotificationView()
    {
        InitializeComponent();
        MouseEnter += (_, _) => SetHeld(true);
        MouseLeave += (_, _) => SetHeld(false);
        MouseLeftButtonUp += (_, e) =>
        {
            if (_shown is not null && e.OriginalSource is not Button) Opened?.Invoke(_shown);
        };
    }

    internal event Action<ShammyNotification>? Opened;
    internal event Action? Dismissed;
    internal event Action<ShammyNotification>? MuteRequested;

    internal void Show(ShammyNotification n, int more)
    {
        _shown = n;
        AppText.Text = n.AppName;
        TimeText.Text = n.TimeText;
        TitleText.Text = n.Title;
        BodyText.Text = n.Body;
        BodyText.Visibility = n.Body.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        MoreBadge.Visibility = more > 0 ? Visibility.Visible : Visibility.Collapsed;
        MoreText.Text = $"+{more}";
        MuteItem.Header = $"Don't show from {n.AppName}";
        // Shell icons have transparent padding, so the crisp icon sits on a field blurred from its own colours.
        IconImage.Source = n.Icon;
        IconField.Source = n.Icon;
        System.Windows.Automation.AutomationProperties.SetName(this,
            n.Body.Length > 0 ? $"{n.AppName}: {n.Title}. {n.Body}" : $"{n.AppName}: {n.Title}");
        IconGlyph.Visibility = n.Icon is null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>While the pointer is over it the window holds the notification on screen: show Dismiss, and "Held" for the time.</summary>
    void SetHeld(bool held)
    {
        DismissButton.Visibility = held ? Visibility.Visible : Visibility.Hidden;
        HeldText.Visibility = held ? Visibility.Visible : Visibility.Collapsed;
        TimeText.Visibility = held ? Visibility.Collapsed : Visibility.Visible;
    }

    void OnDismiss(object sender, RoutedEventArgs e) => Dismissed?.Invoke();

    void OnMute(object sender, RoutedEventArgs e)
    {
        if (_shown is not null) MuteRequested?.Invoke(_shown);
    }

    /// <summary>"Remind me in 1 hour" from the right-click menu.</summary>
    internal event Action<ShammyNotification>? RemindRequested;

    void OnRemind(object sender, RoutedEventArgs e)
    {
        if (_shown is not null) RemindRequested?.Invoke(_shown);
    }
}
