using System.Collections.ObjectModel;
using System.Windows.Media;
using Halo.Infrastructure;

namespace Halo.Shelf;

internal sealed class ShelfItem
{
    ImageSource? _icon;

    public ShelfItem(string path) => Path = path;

    public string Path { get; }
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\')) is { Length: > 0 } n ? n : Path;
    public ImageSource? Icon => _icon ??= ShellIcons.Get(Path, 96);
}

/// <summary>
/// Files parked on the notch. Items are references to the original files, not copies, and survive
/// restarts. Entries whose file has since been deleted are dropped on load.
/// </summary>
internal sealed class ShelfStore
{
    readonly AppSettings _settings;

    public ShelfStore(AppSettings settings)
    {
        _settings = settings;
        foreach (var p in settings.Shelf.Where(Exists)) Items.Add(new ShelfItem(p));
    }

    public ObservableCollection<ShelfItem> Items { get; } = new();

    public int Add(IEnumerable<string> paths)
    {
        int added = 0;
        foreach (var p in paths)
        {
            if (!Exists(p) || Items.Any(i => string.Equals(i.Path, p, StringComparison.OrdinalIgnoreCase))) continue;
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
