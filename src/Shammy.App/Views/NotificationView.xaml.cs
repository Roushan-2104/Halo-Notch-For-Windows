using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shammy.Notifications;

namespace Shammy.Views;

/// <summary>A notification as it drops out of the notch: app icon, app name, title and two lines of text.</summary>
public partial class NotificationView : UserControl
{
    ShammyNotification? _shown;

    public NotificationView()
    {
        InitializeComponent();
        MouseEnter += (_, _) => DismissButton.Visibility = Visibility.Visible;
        MouseLeave += (_, _) => DismissButton.Visibility = Visibility.Hidden;
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
        // Shell icons have transparent padding, so they sit on a subtle tile rather than filling it.
        IconImage.Source = n.Icon;
        IconGlyph.Visibility = n.Icon is null ? Visibility.Visible : Visibility.Collapsed;
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
