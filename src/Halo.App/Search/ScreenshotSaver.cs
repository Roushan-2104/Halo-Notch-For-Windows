using System.Windows;
using System.Windows.Media.Imaging;
using Halo.Infrastructure;
using Halo.Interop;

namespace Halo.Search;

/// <summary>
/// Saves a capture the way Windows' own Print Screen does: a PNG in Pictures\Screenshots plus a copy
/// on the clipboard. The clipboard module's screenshot catcher watches that folder, so every capture
/// gets the usual card (copy, add to shelf, drag out) without any extra wiring.
/// </summary>
internal static class ScreenshotSaver
{
    static readonly Guid ScreenshotsFolder = new("B7BEDE81-DF94-4682-A7D8-57A52620B86F");

    public static string Folder =>
        Native.KnownFolder(ScreenshotsFolder) ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");

    public static string? Save(BitmapSource image, string kind = "screenshot")
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var path = Path.Combine(Folder, $"Halo {kind} {DateTime.Now:yyyy-MM-dd HHmmss}.png");
            for (int n = 2; File.Exists(path); n++)
                path = Path.Combine(Folder, $"Halo {kind} {DateTime.Now:yyyy-MM-dd HHmmss} ({n}).png");

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var fs = File.Create(path)) encoder.Save(fs);

            try { Clipboard.SetImage(image); }
            catch (System.Runtime.InteropServices.ExternalException) { /* clipboard busy; the file is what matters */ }

            Log.Info($"Saved {kind}");
            return path;
        }
        catch (Exception ex)
        {
            Log.Warn($"Saving {kind} failed", ex);
            return null;
        }
    }
}
