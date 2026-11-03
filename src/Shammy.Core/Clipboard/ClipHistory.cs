using System.Text.Json.Serialization;

namespace Shammy.Core.Clipboard;

public enum ClipKind
{
    Text,
    Image,
    Files,
}

/// <summary>One thing that was on the clipboard. Text and file lists are persisted; images live in memory only.</summary>
public sealed class ClipEntry
{
    string? _key;

    public ClipKind Kind { get; set; }

    /// <summary>The copied text (text items only).</summary>
    public string Text { get; set; } = "";

    public string[] Files { get; set; } = Array.Empty<string>();

    /// <summary>Pixel hash (images only).</summary>
    public string Hash { get; set; } = "";

    /// <summary>Identity used to de-duplicate: the text itself, the joined paths, or the pixel hash. Derived, so not saved twice.</summary>
    [JsonIgnore]
    public string Key => _key ??= Kind switch
    {
        ClipKind.Text => "t:" + Text,
        ClipKind.Files => "f:" + string.Join("|", Files).ToLowerInvariant(),
        _ => "i:" + Hash,
    };

    public DateTime Time { get; set; }
    public bool Pinned { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>The app it was copied from, as a friendly name ("Chrome"); empty when unknown.</summary>
    public string Source { get; set; } = "";

    /// <summary>Whatever the app keeps alongside (encoded image, thumbnail); never serialized.</summary>
    [JsonIgnore]
    public object? Tag { get; set; }

    public static ClipEntry FromText(string text, DateTime time) =>
        new() { Kind = ClipKind.Text, Text = text, Time = time };

    public static ClipEntry FromFiles(IEnumerable<string> files, DateTime time) =>
        new() { Kind = ClipKind.Files, Files = files.ToArray(), Time = time };

    public static ClipEntry FromImage(string pixelHash, int width, int height, DateTime time, object? tag = null) =>
        new() { Kind = ClipKind.Image, Hash = pixelHash, Width = width, Height = height, Time = time, Tag = tag };
}

/// <summary>
/// Clipboard history: pinned entries first, then the rest newest first. Copying something already in
/// the list moves it to the top instead of duplicating it. Pinned entries are never trimmed or cleared.
/// Images are capped separately because each one holds a whole encoded picture in memory.
/// </summary>
public sealed class ClipHistory
{
    readonly List<ClipEntry> _items = new();

    public ClipHistory(int capacity = 30, int imageCapacity = 10)
    {
        Capacity = capacity;
        ImageCapacity = imageCapacity;
    }

    public int Capacity { get; }
    public int ImageCapacity { get; }
    public IReadOnlyList<ClipEntry> Items => _items;
    public int PinnedCount => _items.Count(e => e.Pinned);

    public event Action? Changed;

    /// <summary>Adds an entry (or bumps its duplicate) and returns the entry that is now in the list.</summary>
    public ClipEntry Add(ClipEntry entry)
    {
        var stored = _items.FirstOrDefault(e => e.Key == entry.Key);
        if (stored is not null)
        {
            _items.Remove(stored);
            stored.Time = entry.Time;
            stored.Tag ??= entry.Tag;
            if (entry.Source.Length > 0) stored.Source = entry.Source;
        }
        else
        {
            stored = entry;
        }
        Insert(stored);
        Trim();
        Changed?.Invoke();
        return stored;
    }

    /// <summary>Move an entry to the top of its section (e.g. after copying it back).</summary>
    public void Touch(ClipEntry entry, DateTime time)
    {
        if (!_items.Remove(entry)) return;
        entry.Time = time;
        Insert(entry);
        Changed?.Invoke();
    }

    public void SetPinned(ClipEntry entry, bool pinned)
    {
        if (entry.Pinned == pinned || !_items.Remove(entry)) return;
        entry.Pinned = pinned;
        Insert(entry);
        Trim();
        Changed?.Invoke();
    }

    public bool Remove(ClipEntry entry)
    {
        if (!_items.Remove(entry)) return false;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Removes everything that isn't pinned; returns how many went.</summary>
    public int Clear()
    {
        int removed = _items.RemoveAll(e => !e.Pinned);
        if (removed > 0) Changed?.Invoke();
        return removed;
    }

    /// <summary>Removes unpinned entries last copied before <paramref name="cutoff"/>; returns how many went.</summary>
    public int Expire(DateTime cutoff)
    {
        int removed = _items.RemoveAll(e => !e.Pinned && e.Time < cutoff);
        if (removed > 0) Changed?.Invoke();
        return removed;
    }

    /// <summary>Replace the contents with saved entries (order is rebuilt from pin state and time).</summary>
    public void Load(IEnumerable<ClipEntry> entries)
    {
        _items.Clear();
        var seen = new HashSet<string>();
        foreach (var e in entries.OrderByDescending(e => e.Pinned).ThenByDescending(e => e.Time))
            if (e.Key.Length > 2 && seen.Add(e.Key)) _items.Add(e);
        Trim();
        Changed?.Invoke();
    }

    void Insert(ClipEntry entry)
    {
        int index = entry.Pinned ? 0 : _items.Count(e => e.Pinned);
        _items.Insert(index, entry);
    }

    void Trim()
    {
        // Oldest unpinned entries go first; pinned ones may push the list over capacity, by design.
        for (int i = _items.Count - 1; i >= 0 && _items.Count > Capacity; i--)
            if (!_items[i].Pinned) _items.RemoveAt(i);

        int images = _items.Count(e => e.Kind == ClipKind.Image && !e.Pinned);
        for (int i = _items.Count - 1; i >= 0 && images > ImageCapacity; i--)
        {
            if (_items[i].Kind != ClipKind.Image || _items[i].Pinned) continue;
            _items.RemoveAt(i);
            images--;
        }
    }
}
