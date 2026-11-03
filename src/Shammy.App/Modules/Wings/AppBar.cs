using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Shammy.Interop;
using static Shammy.Modules.Wings.WingsNative;

namespace Shammy.Modules.Wings;

/// <summary>
/// A window registered with the shell as a top-edge app bar, so maximised windows and the work area start
/// below it. Every registration is also kept in a process-wide list, so the crash and exit paths can hand
/// the space back (ABM_REMOVE) even when the windows themselves never get to close.
/// </summary>
internal sealed class AppBar
{
    static readonly ConcurrentDictionary<IntPtr, byte> Registered = new();

    readonly IntPtr _hwnd;
    readonly uint _callback;

    public AppBar(IntPtr hwnd, uint callbackMessage)
    {
        _hwnd = hwnd;
        _callback = callbackMessage;
    }

    public bool IsRegistered { get; private set; }

    /// <summary>The rectangle the shell gave us (physical pixels).</summary>
    public Native.RECT Reserved { get; private set; }

    APPBARDATA Data() => new() { cbSize = (uint)Marshal.SizeOf<APPBARDATA>(), hWnd = _hwnd, uCallbackMessage = _callback, uEdge = ABE_TOP };

    public bool Register()
    {
        if (IsRegistered) return true;
        var abd = Data();
        IsRegistered = SHAppBarMessage(ABM_NEW, ref abd) != UIntPtr.Zero;
        if (IsRegistered) Registered[_hwnd] = 0;
        return IsRegistered;
    }

    /// <summary>
    /// Asks for a strip <paramref name="heightPx"/> tall along the top of <paramref name="monitor"/> (below a
    /// top taskbar or another app bar if there is one) and reserves it. Returns false when the shell gave us
    /// the same rectangle we already had, so a change notification we caused ourselves ends here instead
    /// of bouncing between the strips of several monitors.
    /// </summary>
    public bool Reserve(Native.RECT monitor, int heightPx)
    {
        if (!IsRegistered) return false;
        var abd = Data();
        abd.rc = new Native.RECT { Left = monitor.Left, Top = monitor.Top, Right = monitor.Right, Bottom = monitor.Top + heightPx };
        SHAppBarMessage(ABM_QUERYPOS, ref abd);
        // QUERYPOS may move the top down (past a top taskbar); keep our height from wherever it put us.
        abd.rc.Bottom = abd.rc.Top + heightPx;
        var want = abd.rc;
        if (want.Left == Reserved.Left && want.Top == Reserved.Top && want.Right == Reserved.Right && want.Bottom == Reserved.Bottom)
            return false;
        SHAppBarMessage(ABM_SETPOS, ref abd);
        Reserved = abd.rc;
        return true;
    }

    public void WindowPosChanged()
    {
        if (!IsRegistered) return;
        var abd = Data();
        SHAppBarMessage(ABM_WINDOWPOSCHANGED, ref abd);
    }

    public void Remove()
    {
        if (!IsRegistered) return;
        IsRegistered = false;
        Reserved = default;
        Registered.TryRemove(_hwnd, out _);
        var abd = Data();
        SHAppBarMessage(ABM_REMOVE, ref abd);
    }

    /// <summary>Last resort on a crash or a hard exit: give every reserved strip back to the work area.</summary>
    public static void RemoveAll()
    {
        foreach (var hwnd in Registered.Keys)
        {
            try
            {
                var abd = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>(), hWnd = hwnd };
                SHAppBarMessage(ABM_REMOVE, ref abd);
            }
            catch (Exception)
            {
                // Dying anyway; try the rest.
            }
            Registered.TryRemove(hwnd, out _);
        }
    }

    public static int Count => Registered.Count;
}
