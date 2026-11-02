namespace Halo.Infrastructure;

internal static class Paths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Halo");

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
}
