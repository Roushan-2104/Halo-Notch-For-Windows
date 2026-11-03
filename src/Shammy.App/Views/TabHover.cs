using System.Windows;
using System.Windows.Input;

namespace Shammy.Views;

/// <summary>
/// Hover highlight for the notch's tab buttons that only real pointer movement turns on. WPF's IsMouseOver
/// alone went stale: the notch shrinks and grows under a resting pointer without a mouse-leave, so a tab
/// (the System tab, say) stayed lit when a command later opened the dashboard on another tab. The style
/// lights a tab only while it is under the pointer AND armed; a move over it arms it, leaving disarms it,
/// and the dashboard disarms every tab whenever it opens or switches tabs itself.
/// </summary>
public static class TabHover
{
    public static readonly DependencyProperty ArmedProperty =
        DependencyProperty.RegisterAttached("Armed", typeof(bool), typeof(TabHover), new PropertyMetadata(false));

    public static bool GetArmed(DependencyObject d) => (bool)d.GetValue(ArmedProperty);
    public static void SetArmed(DependencyObject d, bool value) => d.SetValue(ArmedProperty, value);

    /// <summary>Arm on movement over the tab, disarm on leaving it.</summary>
    public static void Track(FrameworkElement tab)
    {
        // WPF also raises synthetic moves at its last known pointer position after layout changes, which is
        // exactly the stale case, so the real cursor has to be over the tab too.
        tab.MouseMove += (_, _) => SetArmed(tab, CursorIsOver(tab));
        tab.MouseLeave += (_, _) => SetArmed(tab, false);
    }

    /// <summary>Clear the highlight until the pointer next moves over the tab.</summary>
    public static void Clear(DependencyObject tab) => SetArmed(tab, false);

    /// <summary>Whether the real cursor (not WPF's idea of it) is inside the element, in physical pixels.</summary>
    static bool CursorIsOver(FrameworkElement el)
    {
        if (!el.IsVisible || PresentationSource.FromVisual(el) is null || !Interop.Native.GetCursorPos(out var p)) return false;
        try
        {
            var a = el.PointToScreen(new Point(0, 0));
            var b = el.PointToScreen(new Point(el.ActualWidth, el.ActualHeight));
            return p.X >= Math.Min(a.X, b.X) && p.X < Math.Max(a.X, b.X) && p.Y >= Math.Min(a.Y, b.Y) && p.Y < Math.Max(a.Y, b.Y);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
