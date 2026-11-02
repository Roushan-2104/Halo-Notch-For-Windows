using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Halo.Interop;

namespace Halo.Modules.ClipboardHistory;

/// <summary>
/// Raises <see cref="Changed"/> (on the UI thread) whenever any app puts something on the clipboard.
/// Windows posts WM_CLIPBOARDUPDATE to a message-only window, so nothing polls.
/// </summary>
internal sealed class ClipboardListener : IDisposable
{
    const int WM_CLIPBOARDUPDATE = 0x031D;
    static readonly IntPtr HWND_MESSAGE = new(-3);

    readonly HwndSource _source;

    public ClipboardListener()
    {
        _source = new HwndSource(new HwndSourceParameters("HaloClipboardListener")
        {
            ParentWindow = HWND_MESSAGE,
            WindowStyle = 0,
        });
        _source.AddHook(WndProc);
        IsListening = ClipNative.AddClipboardFormatListener(_source.Handle);
    }

    public bool IsListening { get; }

    public event Action? Changed;

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE)
        {
            handled = true;
            Changed?.Invoke();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (IsListening) ClipNative.RemoveClipboardFormatListener(_source.Handle);
        _source.Dispose();
    }
}

/// <summary>Clipboard calls the shared interop class doesn't have.</summary>
internal static class ClipNative
{
    [DllImport("user32.dll", SetLastError = true)] public static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterClipboardFormat(string name);
    [DllImport("user32.dll")] static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")] static extern IntPtr GetClipboardOwner();

    static readonly Dictionary<string, uint> Formats = new();

    /// <summary>Checks a registered format without opening the clipboard (so it never contends for the lock).</summary>
    public static bool HasFormat(string name)
    {
        if (!Formats.TryGetValue(name, out var id)) Formats[name] = id = RegisterClipboardFormat(name);
        return id != 0 && IsClipboardFormatAvailable(id);
    }

    /// <summary>The process that last emptied the clipboard, or 0 if unknown.</summary>
    public static uint OwnerProcessId()
    {
        var owner = GetClipboardOwner();
        if (owner == IntPtr.Zero) return 0;
        Native.GetWindowThreadProcessId(owner, out var pid);
        return pid;
    }

    public static string? ProcessName(uint pid)
    {
        if (pid == 0) return null;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <summary>A known folder (e.g. Screenshots) or null if Windows doesn't know it.</summary>
    public static string? KnownFolder(Guid id)
    {
        // The buffer must be freed whether or not the call succeeded.
        int hr = Native.SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var ptr);
        try { return hr == 0 ? Marshal.PtrToStringUni(ptr) : null; }
        finally { if (ptr != IntPtr.Zero) Marshal.FreeCoTaskMem(ptr); }
    }
}
