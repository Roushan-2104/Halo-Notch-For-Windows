namespace Halo.Infrastructure;

internal static class Log
{
    static readonly object Gate = new();
    static string? _file;

    static string File
    {
        get
        {
            if (_file is null)
            {
                _file = Path.Combine(Paths.Logs, $"halo-{DateTime.Now:yyyyMMdd}.log");
                foreach (var old in new DirectoryInfo(Paths.Logs).GetFiles("halo-*.log").Where(f => f.LastWriteTime < DateTime.Now.AddDays(-7)))
                {
                    try { old.Delete(); } catch (IOException) { }
                }
            }
            return _file;
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message, Exception? ex = null) => Write("WARN", ex is null ? message : $"{message}: {ex}");
    public static void Error(string message, Exception? ex = null) => Write("ERROR", ex is null ? message : $"{message}: {ex}");

    static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
        lock (Gate)
        {
            try { System.IO.File.AppendAllText(File, line); }
            catch (IOException) { }
        }
#if DEBUG
        System.Diagnostics.Debug.Write(line);
#endif
    }
}
