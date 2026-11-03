using System.Windows.Threading;
using Shammy.Infrastructure;
using Shammy.Interop;

namespace Shammy.SystemInfo;

/// <summary>
/// Watches the Downloads folder for browsers' in-progress files (.crdownload, .part, ...) and reports
/// progress by size and completion when the temp file is renamed to its final name.
/// </summary>
internal sealed class DownloadsService : IDisposable
{
    static readonly string[] TempExtensions = { ".crdownload", ".part", ".partial", ".opdownload", ".download" };

    readonly Dispatcher _ui;
    readonly Dictionary<string, string> _active = new(StringComparer.OrdinalIgnoreCase); // temp path -> display name
    readonly DispatcherTimer _poll;
    FileSystemWatcher? _watcher;

    public DownloadsService(Dispatcher ui)
    {
        _ui = ui;
        _poll = new DispatcherTimer(TimeSpan.FromMilliseconds(700), DispatcherPriority.Background, (_, _) => Report(), ui) { IsEnabled = false };
    }

    public string? Folder { get; private set; }

    /// <summary>(display name, bytes so far, number of active downloads); raised while any are active.</summary>
    public event Action<string, long, int>? Progress;

    public event Action<string, string>? Completed;
    public event Action? AllFinished;

    public void Start()
    {
        Folder = Native.KnownFolder(Native.DownloadsFolder) ??
                 Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (!Directory.Exists(Folder))
        {
            Log.Info("Downloads folder not found");
            return;
        }

        _watcher = new FileSystemWatcher(Folder)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size,
            InternalBufferSize = 32 * 1024,
        };
        _watcher.Created += (_, e) => _ui.BeginInvoke(() => OnCreated(e.FullPath));
        _watcher.Renamed += (_, e) => _ui.BeginInvoke(() => OnRenamed(e.OldFullPath, e.FullPath));
        _watcher.Deleted += (_, e) => _ui.BeginInvoke(() => OnDeleted(e.FullPath));
        _watcher.Error += (_, e) => Log.Warn("Downloads watcher error", e.GetException());
        _watcher.EnableRaisingEvents = true;
    }

    static bool IsTemp(string path) => TempExtensions.Any(x => path.EndsWith(x, StringComparison.OrdinalIgnoreCase));

    static string StripTemp(string path)
    {
        foreach (var x in TempExtensions)
            if (path.EndsWith(x, StringComparison.OrdinalIgnoreCase)) return path[..^x.Length];
        return path;
    }

    static string DisplayName(string tempPath)
    {
        var name = Path.GetFileName(StripTemp(tempPath));
        return name.StartsWith("Unconfirmed ", StringComparison.OrdinalIgnoreCase) ? "Download" : name;
    }

    void OnCreated(string path)
    {
        if (!IsTemp(path)) return;
        _active[path] = DisplayName(path);
        _poll.Start();
        Report();
    }

    void OnRenamed(string oldPath, string newPath)
    {
        bool wasTemp = _active.Remove(oldPath) || IsTemp(oldPath);
        if (IsTemp(newPath))
        {
            _active[newPath] = DisplayName(newPath);
            _poll.Start();
            Report();
            return;
        }
        if (wasTemp) Finish(newPath);
    }

    void OnDeleted(string path)
    {
        if (!_active.Remove(path)) return;
        // Some browsers delete the temp after writing the final file instead of renaming.
        var final = StripTemp(path);
        if (File.Exists(final)) Finish(final);
        else if (_active.Count == 0) StopIfIdle();
        else Report();
    }

    void Finish(string finalPath)
    {
        Completed?.Invoke(Path.GetFileName(finalPath), finalPath);
        if (_active.Count == 0) StopIfIdle();
        else Report();
    }

    void StopIfIdle()
    {
        _poll.Stop();
        AllFinished?.Invoke();
    }

    void Report()
    {
        // Drop entries whose temp file vanished without an event (e.g. cancelled while the watcher overflowed).
        foreach (var gone in _active.Keys.Where(p => !File.Exists(p)).ToList()) _active.Remove(gone);
        if (_active.Count == 0)
        {
            StopIfIdle();
            return;
        }
        long bytes = _active.Keys.Sum(p =>
        {
            try { return new FileInfo(p).Length; }
            catch (IOException) { return 0L; }
        });
        Progress?.Invoke(_active.Values.Last(), bytes, _active.Count);
    }

    public void Dispose()
    {
        _poll.Stop();
        _watcher?.Dispose();
    }
}
