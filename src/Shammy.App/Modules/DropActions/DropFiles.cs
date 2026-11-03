using System.IO.Compression;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shammy.Core.DropActions;

namespace Shammy.Modules.DropActions;

/// <summary>The file work behind the drop targets. Thread-safe and UI-free, so it runs on a worker thread.</summary>
internal static class DropFiles
{
    /// <summary>Zips files and folders (recursively) into <paramref name="zipPath"/>; a failed zip is removed.</summary>
    public static void Zip(IReadOnlyList<string> items, string zipPath)
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            foreach (var raw in items)
            {
                var item = raw.TrimEnd('\\', '/');
                if (Directory.Exists(item))
                {
                    bool any = false;
                    foreach (var file in Directory.EnumerateFiles(item, "*", SearchOption.AllDirectories))
                    {
                        // Never zip the zip into itself (dropping a folder that the zip lands in).
                        if (string.Equals(Path.GetFullPath(file), Path.GetFullPath(zipPath), StringComparison.OrdinalIgnoreCase)) continue;
                        zip.CreateEntryFromFile(file, DropPlan.EntryName(item, file, taken), CompressionLevel.Optimal);
                        any = true;
                    }
                    if (!any) zip.CreateEntry(DropPlan.EntryName(item, item, taken) + "/");
                }
                else if (File.Exists(item))
                {
                    zip.CreateEntryFromFile(item, DropPlan.EntryName(item, item, taken), CompressionLevel.Optimal);
                }
            }
        }
        catch
        {
            TryDelete(zipPath);
            throw;
        }
    }

    /// <summary>
    /// Re-encodes an image as PNG or JPG next to it and returns the new path. Transparent pixels become white
    /// for JPG (which has no alpha), rather than the black a plain re-encode would give.
    /// </summary>
    public static string ConvertImage(string path, string extension)
    {
        var output = DropPlan.ConvertPath(path, extension, p => File.Exists(p) || Directory.Exists(p));
        BitmapSource frame;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            frame = decoder.Frames[0];
            frame.Freeze();
        }

        BitmapEncoder encoder;
        if (extension == ".jpg")
        {
            encoder = new JpegBitmapEncoder { QualityLevel = 92 };
            frame = OnWhite(frame);
        }
        else
        {
            encoder = new PngBitmapEncoder();
        }
        encoder.Frames.Add(BitmapFrame.Create(frame));
        try
        {
            using var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write);
            encoder.Save(file);
        }
        catch
        {
            TryDelete(output);
            throw;
        }
        return output;
    }

    static BitmapSource OnWhite(BitmapSource source)
    {
        var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        bgra.CopyPixels(px, stride, 0);
        bool any = false;
        for (int i = 0; i < px.Length; i += 4)
        {
            int a = px[i + 3];
            if (a == 255) continue;
            any = true;
            // Straight alpha over white: c' = c·a + 255·(1−a).
            px[i] = (byte)((px[i] * a + 255 * (255 - a)) / 255);
            px[i + 1] = (byte)((px[i + 1] * a + 255 * (255 - a)) / 255);
            px[i + 2] = (byte)((px[i + 2] * a + 255 * (255 - a)) / 255);
            px[i + 3] = 255;
        }
        if (!any && source.Format == PixelFormats.Bgr24) return source;
        var result = BitmapSource.Create(w, h, source.DpiX > 0 ? source.DpiX : 96, source.DpiY > 0 ? source.DpiY : 96, PixelFormats.Bgr32, null, px, stride);
        result.Freeze();
        return result;
    }

    static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leave it; the error is reported anyway.
        }
    }

    /// <summary>Sum of file sizes (folders counted shallowly, to stay quick during a drag).</summary>
    public static long Size(IEnumerable<string> items)
    {
        long total = 0;
        foreach (var item in items)
        {
            try
            {
                if (File.Exists(item)) total += new FileInfo(item).Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Unreadable: leave it out of the size.
            }
        }
        return total;
    }
}
