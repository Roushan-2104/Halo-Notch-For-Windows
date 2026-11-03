using System.Runtime.InteropServices;
using Shammy.Core.Launcher;

namespace Shammy.Modules.Launcher;

/// <summary>
/// Everything in the Start menu's "All apps" list (Store apps and desktop shortcuts alike), read from the
/// shell's AppsFolder on a background STA thread at startup and every 30 minutes.
/// </summary>
internal sealed class AppIndex : IDisposable
{
    const string AppsFolder = "shell:::{4234d49b-0245-4df3-b780-3893943456e1}";
    static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(30);

    // Manuals, release notes and web links share the Start menu with apps but aren't worth launching by name.
    static readonly string[] SkipExtensions = { ".url", ".htm", ".html", ".chm", ".txt", ".pdf", ".md", ".rtf" };

    readonly ModuleContext _ctx;
    readonly Timer _timer;
    volatile Snapshot _snapshot = new(Array.Empty<AppEntry>(), null, "-");
    int _building;

    sealed record Snapshot(IReadOnlyList<AppEntry> Apps, DateTime? BuiltAt, string Source);

    public AppIndex(ModuleContext ctx)
    {
        _ctx = ctx;
        _timer = new Timer(_ => Refresh(), null, TimeSpan.FromSeconds(1), RefreshEvery);
    }

    public IReadOnlyList<AppEntry> Apps => _snapshot.Apps;
    public DateTime? BuiltAt => _snapshot.BuiltAt;
    public string Source => _snapshot.Source;
    public bool IsBuilding => Volatile.Read(ref _building) == 1;

    public void Refresh()
    {
        if (Interlocked.Exchange(ref _building, 1) == 1) return;
        // Shell.Application is apartment-threaded; an STA of our own avoids marshalling through the UI thread.
        var thread = new Thread(Build) { IsBackground = true, Name = "Shammy app index", Priority = ThreadPriority.BelowNormal };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    void Build()
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var found = new List<AppEntry>();
            string source = "apps folder";
            try { ReadAppsFolder(found); }
            catch (Exception ex) { _ctx.Warn("Reading the apps folder failed", ex); }
            if (found.Count == 0)
            {
                source = "start menu";
                ReadStartMenu(found);
            }

            var apps = found
                .Where(a => !a.Name.StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase))
                .GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            bool first = _snapshot.BuiltAt is null;
            _snapshot = new Snapshot(apps, DateTime.Now, source);
            _ctx.Info($"Indexed {apps.Count} apps from the {source} in {sw.ElapsedMilliseconds} ms");
            // Voice recognition primes itself with app names; tell it they're here.
            if (first) _ctx.VocabularyChanged();
        }
        catch (Exception ex)
        {
            _ctx.Warn("Indexing apps failed", ex);
        }
        finally
        {
            Volatile.Write(ref _building, 0);
        }
    }

    static void ReadAppsFolder(List<AppEntry> into)
    {
        var type = Type.GetTypeFromProgID("Shell.Application");
        if (type is null) return;
        dynamic shell = Activator.CreateInstance(type)!;
        try
        {
            dynamic? folder = shell.NameSpace(AppsFolder);
            if (folder is null) return;
            dynamic items = folder.Items();
            int count = items.Count;
            for (int i = 0; i < count; i++)
            {
                dynamic? item = items.Item(i);
                if (item is null) continue;
                try
                {
                    string name = item.Name ?? "";
                    string path = item.Path ?? "";
                    if (name.Length > 0 && path.Length > 0 && IsApp(path))
                        into.Add(new AppEntry(name, "shell:AppsFolder\\" + path));
                }
                finally
                {
                    Marshal.ReleaseComObject(item);
                }
            }
            Marshal.ReleaseComObject(items);
            Marshal.ReleaseComObject(folder);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }

    /// <summary>Fallback when the shell object isn't available: the .lnk files the Start menu is built from.</summary>
    static void ReadStartMenu(List<AppEntry> into)
    {
        foreach (var root in new[] { Environment.SpecialFolder.CommonPrograms, Environment.SpecialFolder.Programs })
        {
            var dir = Environment.GetFolderPath(root);
            if (dir.Length == 0 || !Directory.Exists(dir)) continue;
            try
            {
                foreach (var lnk in Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories))
                    into.Add(new AppEntry(Path.GetFileNameWithoutExtension(lnk), lnk));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    static bool IsApp(string parsingName) =>
        !parsingName.Contains("://", StringComparison.Ordinal) &&
        !SkipExtensions.Any(e => parsingName.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    public void Dispose() => _timer.Dispose();
}
