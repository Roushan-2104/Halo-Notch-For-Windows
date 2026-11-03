using System.Collections.Concurrent;
using System.Windows.Media;
using System.Windows.Threading;
using Shammy.Shelf;

namespace Shammy.Modules.Palette;

/// <summary>
/// Shell icons for palette rows, read on a background STA thread (the shell's image factories want an
/// apartment, and a slow icon must never hold up a keystroke) and cached. The icons are frozen, so the
/// UI thread can use them directly.
/// </summary>
internal sealed class IconLoader : IDisposable
{
    const int Pixels = 48;
    const int MaxCached = 400;

    readonly Dispatcher _ui;
    readonly ConcurrentDictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentDictionary<string, byte> _queued = new(StringComparer.OrdinalIgnoreCase);
    readonly BlockingCollection<(string Path, bool IconOnly)> _work = new();
    readonly Thread _thread;

    public IconLoader(Dispatcher ui)
    {
        _ui = ui;
        _thread = new Thread(Loop) { IsBackground = true, Name = "Shammy palette icons", Priority = ThreadPriority.BelowNormal };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>Raised on the UI thread when an icon arrived (path, icon).</summary>
    public event Action<string, ImageSource>? Loaded;

    /// <summary>The cached icon, or null; asks for it once in the background when it isn't known yet.</summary>
    /// <param name="iconOnly">For files: the type icon, not a thumbnail (a big photo or PDF takes a while).
    /// Apps want the full image, since Store apps (shell:AppsFolder) have no plain icon to give.</param>
    public ImageSource? Get(string path, bool iconOnly)
    {
        if (_cache.TryGetValue(path, out var icon)) return icon;
        if (_queued.TryAdd(path, 0) && !_work.IsAddingCompleted) _work.Add((path, iconOnly));
        return null;
    }

    void Loop()
    {
        foreach (var (path, iconOnly) in _work.GetConsumingEnumerable())
        {
            ImageSource? icon = null;
            try { icon = ShellIcons.Get(path, Pixels, iconOnly) ?? (iconOnly ? null : ShellIcons.Get(path, Pixels, iconOnly: true)); }
            catch (Exception ex) { Infrastructure.Log.Warn("Reading a palette icon failed", ex); }
            if (_cache.Count > MaxCached) _cache.Clear();
            _cache[path] = icon;
            _queued.TryRemove(path, out _);
            if (icon is not null) _ui.BeginInvoke(() => Loaded?.Invoke(path, icon));
        }
    }

    public void Dispose() => _work.CompleteAdding();
}
