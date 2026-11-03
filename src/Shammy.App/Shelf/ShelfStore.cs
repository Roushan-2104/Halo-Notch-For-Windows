using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Threading;
using Shammy.Infrastructure;

namespace Shammy.Shelf;

internal sealed class ShelfItem : INotifyPropertyChanged
{
    ImageSource? _icon;
    bool _requested;

    public ShelfItem(string path) => Path = path;

    public string Path { get; }
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\')) is { Length: > 0 } n ? n : Path;

    /// <summary>
    /// Null until the thumbnail has been made off the UI thread; extracting hundreds of thumbnails (or one on
    /// a slow network share) inline used to freeze the notch.
    /// </summary>
    public ImageSource? Icon
    {
        get
        {
            if (_icon is null && !_requested)
            {
                _requested = true;
                IconLoader.Request(this);
            }
            return _icon;
        }
    }

    internal void SetIcon(ImageSource? icon)
    {
        _icon = icon;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>One background STA thread (shell thumbnail handlers expect one) working through icon requests.</summary>
internal static class IconLoader
{
    static readonly BlockingCollection<ShelfItem> Queue = new();
    static Dispatcher? _ui;

    public static void Request(ShelfItem item)
    {
        _ui ??= Dispatcher.CurrentDispatcher;
        EnsureThread();
        Queue.Add(item);
    }

    static Thread? _thread;

    static void EnsureThread()
    {
        if (_thread is not null) return;
        _thread = new Thread(() =>
        {
            foreach (var item in Queue.GetConsumingEnumerable())
            {
                // ShellIcons freezes what it returns, so the UI thread may use it.
                var icon = ShellIcons.Get(item.Path, 96);
                _ui?.BeginInvoke(() => item.SetIcon(icon));
            }
        })
        { IsBackground = true, Name = "Shammy shelf icons", Priority = ThreadPriority.BelowNormal };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }
}

/// <summary>
/// Files parked on the notch. Items are references to the original files, not copies, and survive
/// restarts. Entries whose file has since been deleted are dropped shortly after start.
/// </summary>
internal sealed class ShelfStore
{
    readonly AppSettings _settings;

    public ShelfStore(AppSettings settings)
    {
        _settings = settings;
        foreach (var p in settings.Shelf) Items.Add(new ShelfItem(p));
        // Checking existence can block for a network timeout (a file on an offline share), so it mustn't
        // hold up startup on the UI thread.
        var ui = Dispatcher.CurrentDispatcher;
        var paths = settings.Shelf.ToList();
        _ = Task.Run(() =>
        {
            var gone = paths.Where(p => !Exists(p)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (gone.Count == 0) return;
            ui.BeginInvoke(() =>
            {
                foreach (var item in Items.Where(i => gone.Contains(i.Path)).ToList()) Items.Remove(item);
                Persist();
            });
        });
    }

    public ObservableCollection<ShelfItem> Items { get; } = new();

    public int Add(IEnumerable<string> paths)
    {
        int added = 0;
        var known = new HashSet<string>(Items.Select(i => i.Path), StringComparer.OrdinalIgnoreCase);
        foreach (var p in paths)
        {
            // Local paths are checked (instant); network paths aren't, since a dropped file exists by
            // definition and an offline share would stall the UI. A set keeps big drops linear.
            if (!p.StartsWith(@"\\", StringComparison.Ordinal) && !Exists(p)) continue;
            if (!known.Add(p)) continue;
            Items.Add(new ShelfItem(p));
            added++;
        }
        if (added > 0) Persist();
        return added;
    }

    public void Remove(ShelfItem item)
    {
        if (Items.Remove(item)) Persist();
    }

    public void Clear()
    {
        Items.Clear();
        Persist();
    }

    void Persist()
    {
        _settings.Shelf = Items.Select(i => i.Path).ToList();
        _settings.Save();
    }

    static bool Exists(string p) => File.Exists(p) || Directory.Exists(p);
}
