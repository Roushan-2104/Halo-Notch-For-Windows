using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Shammy.Interop;

namespace Shammy.Views;

/// <summary>
/// Keeps every context menu above the notch. A menu is its own topmost window, and the notch re-asserts itself as
/// topmost (when the foreground app changes, or when it shows over a fullscreen app), which could land it on top of
/// an open menu and leave the options unclickable. Checking only while a menu is open costs nothing otherwise.
/// </summary>
internal static class MenusAboveNotch
{
    public static void Install() =>
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler(OnOpened));

    static void OnOpened(object sender, RoutedEventArgs e)
    {
        var menu = (ContextMenu)sender;
        Raise(menu);
        var keep = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Input, (_, _) => Raise(menu), menu.Dispatcher);
        RoutedEventHandler? closed = null;
        closed = (_, _) =>
        {
            keep.Stop();
            menu.Closed -= closed;
        };
        menu.Closed += closed;
    }

    static void Raise(ContextMenu menu)
    {
        if (PresentationSource.FromVisual(menu) is HwndSource source) Native.BringToTopmost(source.Handle);
    }
}
