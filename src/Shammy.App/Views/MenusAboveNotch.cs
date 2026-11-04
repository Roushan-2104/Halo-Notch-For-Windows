using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Shammy.Interop;

namespace Shammy.Views;

/// <summary>
/// Keeps every context menu above the notch. A menu is its own topmost window, and the notch re-asserts itself as
/// topmost every 1.5 s (so it stays over apps that claim the top), which would land it on top of an open menu and
/// leave the options unclickable. So a menu goes on top once when it opens, and the notch holds still until it closes.
/// </summary>
internal static class MenusAboveNotch
{
    static int _open;

    /// <summary>A menu is open: the notch must not raise itself over it.</summary>
    public static bool AnyOpen => _open > 0;

    public static void Install()
    {
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler(OnOpened));
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.ClosedEvent, new RoutedEventHandler((_, _) => _open = Math.Max(0, _open - 1)));
    }

    static void OnOpened(object sender, RoutedEventArgs e)
    {
        _open++;
        if (PresentationSource.FromVisual((ContextMenu)sender) is HwndSource source) Native.BringToTopmost(source.Handle);
    }
}
