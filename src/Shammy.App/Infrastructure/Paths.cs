namespace Shammy.Infrastructure;

internal static class Paths
{
    static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string Root { get; } = ResolveRoot();

    /// <summary>Set when this start moved the data folder over from the app's old name ("Halo").</summary>
    public static string? MigratedFrom { get; private set; }

    public static string Logs => Ensure("logs");
    public static string Models => Ensure("models");
    public static string WebView => Ensure("webview");
    public static string SettingsFile => Path.Combine(Ensure(""), "settings.json");

    static string Ensure(string sub)
    {
        var dir = Path.Combine(Root, sub);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Shammy was called Halo; its settings, notes, clipboard, voice model and browser profile lived in
    /// %LOCALAPPDATA%\Halo. Move that folder over once, but only if it really is ours (another app could
    /// use a folder called "Halo"). If the move fails (something has a file open), keep using the old
    /// folder rather than starting empty.
    /// </summary>
    static string ResolveRoot()
    {
        var root = Path.Combine(Local, "Shammy");
        var old = Path.Combine(Local, "Halo");
        if (Directory.Exists(root) || !Directory.Exists(old) || !LooksLikeOurs(old)) return root;
        try
        {
            Directory.Move(old, root);
            MigratedFrom = old;
            return root;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return old;
        }
    }

    static bool LooksLikeOurs(string dir) =>
        File.Exists(Path.Combine(dir, "settings.json")) &&
        (Directory.Exists(Path.Combine(dir, "modules")) || Directory.Exists(Path.Combine(dir, "logs")));
}
