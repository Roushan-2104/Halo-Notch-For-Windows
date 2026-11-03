using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shammy.Core.Wings;
using Shammy.Shelf;
using static Shammy.Modules.Wings.WingsNative;

namespace Shammy.Modules.Wings;

/// <summary>
/// Icons and names for wing items, cached per app. The shell's icon for what launches the app (a pinned
/// shortcut, the exe, or the Store app's tile) is preferred since it's crisp at any size; a window's own
/// icon is the fallback for apps the shell can't picture.
/// </summary>
internal sealed class AppVisuals
{
    const int IconPixels = 48;
    readonly Dictionary<string, ImageSource?> _icons = new();
    readonly Dictionary<string, string> _names = new();

    public void Clear()
    {
        _icons.Clear();
        _names.Clear();
    }

    public ImageSource? Icon(WingItem item)
    {
        if (_icons.TryGetValue(item.Key, out var cached)) return cached;
        ImageSource? icon = null;
        var source = item.Pin?.Launch ?? item.Windows.FirstOrDefault()?.Launch;
        if (!string.IsNullOrWhiteSpace(source)) icon = ShellIcons.Get(source, IconPixels);
        if (icon is null && item.Pin?.ExePath is { } exe) icon = ShellIcons.Get(exe, IconPixels);
        foreach (var w in item.Windows)
        {
            if (icon is not null) break;
            icon = WindowIcon(new IntPtr(w.Handle));
        }
        // Not cached while nothing was found yet: a window that just opened may not have its icon set.
        if (icon is not null || item.Windows.Count == 0) _icons[item.Key] = icon;
        return icon;
    }

    /// <summary>A small picture of one window for the window list (its own icon, else the app's).</summary>
    public ImageSource? WindowIconOrApp(WindowInfo w, WingItem item) => WindowIcon(new IntPtr(w.Handle)) ?? Icon(item);

    public string Name(WingItem item)
    {
        if (item.Pin is { } pin && pin.Name.Length > 0) return pin.Name;
        if (_names.TryGetValue(item.Key, out var cached)) return cached;
        var w = item.Windows.FirstOrDefault();
        string? name = null;
        if (w?.Launch is { } launch && launch.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
            name = DisplayName(launch);
        if (name is null && w?.ExePath is { } exe)
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(exe);
                name = string.IsNullOrWhiteSpace(info.FileDescription) ? null : info.FileDescription.Trim();
            }
            catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException)
            {
            }
            name ??= Path.GetFileNameWithoutExtension(exe);
        }
        name ??= w?.Title ?? "App";
        _names[item.Key] = name;
        return name;
    }

    /// <summary>WM_GETICON with a short timeout (a hung app mustn't hang the strip), then the class icon.</summary>
    static ImageSource? WindowIcon(IntPtr hwnd)
    {
        IntPtr icon = IntPtr.Zero;
        foreach (int kind in new[] { ICON_BIG, ICON_SMALL2 })
        {
            if (SendMessageTimeout(hwnd, WM_GETICON, new IntPtr(kind), IntPtr.Zero, SMTO_ABORTIFHUNG | SMTO_BLOCK, 60, out icon) != IntPtr.Zero && icon != IntPtr.Zero) break;
            icon = IntPtr.Zero;
        }
        if (icon == IntPtr.Zero) icon = GetClassLongPtr(hwnd, GCLP_HICON);
        if (icon == IntPtr.Zero) icon = GetClassLongPtr(hwnd, GCLP_HICONSM);
        if (icon == IntPtr.Zero) return null;
        try
        {
            // The icon belongs to the other app: copy its pixels, never destroy the handle.
            var src = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}
