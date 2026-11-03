namespace Shammy.Core.DropActions;

/// <summary>The four targets shown while a file is dragged over the notch.</summary>
public enum DropAction { Shelf, Phone, Compress, Convert }

/// <summary>Naming and format decisions for the drop actions; the app does the file work.</summary>
public static class DropPlan
{
    static readonly string[] Raster = { ".png", ".jpg", ".jpeg", ".jfif", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic", ".ico" };

    public static DropAction? ParseAction(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "shelf" => DropAction.Shelf,
        "phone" or "send" => DropAction.Phone,
        "compress" or "zip" => DropAction.Compress,
        "convert" => DropAction.Convert,
        _ => null,
    };

    public static bool IsImage(string path) => Raster.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>
    /// What Convert turns an image into: JPG becomes PNG (lossless, keeps everything), anything else becomes
    /// JPG (the "make it small enough to send" case). Null for files that aren't images.
    /// </summary>
    public static string? ConvertExtension(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (!IsImage(path)) return null;
        return ext is ".jpg" or ".jpeg" or ".jfif" ? ".png" : ".jpg";
    }

    /// <summary>
    /// The zip's name: "Report_final.zip" for one item, "Archive.zip" for several (like Explorer and Finder),
    /// next to the first item. Never overwrites: "Archive (2).zip".
    /// </summary>
    public static string ZipPath(IReadOnlyList<string> items, Func<string, bool> exists)
    {
        var first = items[0].TrimEnd('\\', '/');
        var dir = Path.GetDirectoryName(first) ?? "";
        string stem = items.Count == 1
            ? (Directory.Exists(first) || Path.GetExtension(first).Length == 0 ? Path.GetFileName(first) : Path.GetFileNameWithoutExtension(first))
            : "Archive";
        if (stem.Length == 0) stem = "Archive";
        return Unique(dir, stem, ".zip", exists);
    }

    /// <summary>"photo.png" → "photo.jpg" next to it, or "photo (2).jpg" when that exists.</summary>
    public static string ConvertPath(string path, string extension, Func<string, bool> exists) =>
        Unique(Path.GetDirectoryName(path) ?? "", Path.GetFileNameWithoutExtension(path), extension, exists);

    public static string Unique(string dir, string stem, string extension, Func<string, bool> exists)
    {
        var path = Path.Combine(dir, stem + extension);
        for (int n = 2; exists(path) && n < 10_000; n++) path = Path.Combine(dir, $"{stem} ({n}){extension}");
        return path;
    }

    /// <summary>Entry names inside the zip: a file at the root, a folder's files under the folder's name; no duplicates.</summary>
    public static string EntryName(string root, string file, ISet<string> taken)
    {
        var baseDir = Path.GetDirectoryName(root.TrimEnd('\\', '/')) ?? "";
        var rel = Path.GetRelativePath(baseDir, file).Replace('\\', '/');
        var name = rel;
        var ext = Path.GetExtension(rel);
        var stem = rel[..^ext.Length];
        for (int n = 2; !taken.Add(name); n++) name = $"{stem} ({n}){ext}";
        return name;
    }

    /// <summary>"Report_final.docx · 2.4 MB", or "3 files · 12 MB" for the chip that follows the drag.</summary>
    public static string Describe(IReadOnlyList<string> names, long totalBytes)
    {
        var size = Size(totalBytes);
        if (names.Count == 0) return "";
        var what = names.Count == 1 ? Path.GetFileName(names[0].TrimEnd('\\', '/')) : $"{names.Count} items";
        return totalBytes > 0 ? $"{what} · {size}" : what;
    }

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => FormattableString.Invariant($"{bytes} B"),
        < 1024 * 1024 => FormattableString.Invariant($"{bytes / 1024.0:0} KB"),
        < 1024L * 1024 * 1024 => FormattableString.Invariant($"{bytes / (1024.0 * 1024):0.0} MB"),
        _ => FormattableString.Invariant($"{bytes / (1024.0 * 1024 * 1024):0.0} GB"),
    };
}
