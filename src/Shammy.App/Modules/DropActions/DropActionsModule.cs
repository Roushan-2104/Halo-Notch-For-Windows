using Shammy.Core.DropActions;

namespace Shammy.Modules.DropActions;

/// <summary>
/// Compress and Convert from the drop zone. Compress zips what was dropped next to the first item (never
/// overwriting) and puts the zip on the shelf; Convert turns PNG into JPG and JPG into PNG next to the
/// original, and shelves the result. Send to phone is handled by the PhoneSend module; Shelf by the shelf.
/// </summary>
internal sealed class DropActionsModule : IShammyModule
{
    const string ZipGlyph = "\uF012", ConvertGlyph = "\uE8AB";

    ModuleContext _ctx = null!;
    string _last = "none";

    public string Name => "dropactions";

    public void Start(ModuleContext ctx)
    {
        _ctx = ctx;
        ctx.Notch.DropZone.ActionDropped += (action, files) =>
        {
            if (action == DropAction.Compress) _ = CompressAsync(files);
            else if (action == DropAction.Convert) _ = ConvertAsync(files);
        };
        ctx.AddPipeCommand("drop-test", DropTest);
        ctx.AddPipeCommand("drop-state", _ => $"last={_last} hovered={ctx.Notch.DropZone.Hovered?.ToString() ?? "-"}");
    }

    async Task CompressAsync(string[] files)
    {
        var items = files.Where(f => File.Exists(f) || Directory.Exists(f)).ToList();
        if (items.Count == 0)
        {
            Report("compress: nothing to zip", ZipGlyph, "Nothing to zip", error: true);
            return;
        }
        _ctx.ShowMessage(ZipGlyph, "Zipping…", seconds: 30);
        try
        {
            var zip = await Task.Run(() =>
            {
                var path = DropPlan.ZipPath(items, p => File.Exists(p) || Directory.Exists(p));
                DropFiles.Zip(items, path);
                return path;
            });
            _ctx.Services.Shelf.Add(new[] { zip });
            Report($"compress: {zip}", ZipGlyph, $"Zipped · {Path.GetFileName(zip)}");
            _ctx.Notch.ExpandTo(shelf: true, hold: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            _ctx.Warn("Zipping dropped files failed", ex);
            Report($"compress failed: {ex.GetType().Name}", ZipGlyph,
                ex is UnauthorizedAccessException ? "Can't write a zip in that folder" : "Couldn't zip that", error: true);
        }
    }

    async Task ConvertAsync(string[] files)
    {
        var images = files.Where(f => File.Exists(f) && DropPlan.ConvertExtension(f) is not null).ToList();
        if (images.Count == 0)
        {
            Report("convert: no images", ConvertGlyph, "Convert works on images for now", error: true);
            return;
        }
        var made = new List<string>();
        int failed = 0;
        foreach (var image in images)
        {
            try
            {
                var ext = DropPlan.ConvertExtension(image)!;
                made.Add(await Task.Run(() => DropFiles.ConvertImage(image, ext)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                           or FileFormatException or ArgumentException or InvalidOperationException
                                           or System.Runtime.InteropServices.COMException)
            {
                _ctx.Warn("Converting a dropped image failed", ex);
                failed++;
            }
        }
        if (made.Count == 0)
        {
            Report($"convert failed ({failed})", ConvertGlyph, "Couldn't convert that image", error: true);
            return;
        }
        _ctx.Services.Shelf.Add(made);
        int skipped = files.Length - images.Count;
        string text = made.Count == 1 ? $"Saved {Path.GetFileName(made[0])}" : $"Converted {made.Count} images";
        if (skipped > 0 || failed > 0) text += $" · {skipped + failed} skipped";
        Report($"convert: {string.Join(" | ", made)}", ConvertGlyph, text);
        _ctx.Notch.ExpandTo(shelf: true, hold: true);
    }

    void Report(string state, string glyph, string message, bool error = false)
    {
        _last = state;
        _ctx.Info($"Drop: {state}");
        _ctx.ShowMessage(glyph, message, error, seconds: 3);
    }

    /// <summary>
    /// "drop-test compress C:\some\file.png": copies the file (or folder) into a fresh temp folder and runs the
    /// action on the copy as if it was dropped there, so nothing next to the original is touched.
    /// </summary>
    string DropTest(string arg)
    {
        var parts = arg.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || DropPlan.ParseAction(parts[0]) is not { } action)
            return "error: usage drop-test <shelf|phone|compress|convert> <path>";
        var source = parts[1].Trim().Trim('"');
        if (!File.Exists(source) && !Directory.Exists(source)) return "error: path not found";
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "shammy-drop-test", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(dir);
            var copy = Path.Combine(dir, Path.GetFileName(source.TrimEnd('\\', '/')));
            if (File.Exists(source)) File.Copy(source, copy);
            else CopyFolder(source, copy);
            var files = new[] { copy };
            if (action == DropAction.Shelf) _ctx.Services.Shelf.Add(files);
            else _ctx.Notch.DropZone.RaiseDropped(action, files);
            _last = $"{action.ToString().ToLowerInvariant()}: running";
            return $"ok {action.ToString().ToLowerInvariant()} on {copy} (see drop-state)";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "error: " + ex.Message;
        }
    }

    static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    public void Dispose()
    {
    }
}
