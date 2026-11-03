using System.Globalization;
using Shammy.Core.PhoneSend;
using Shammy.Shelf;

namespace Shammy.Modules.PhoneSend;

/// <summary>
/// AirDrop-style transfers over the local Wi-Fi: a QR code on the notch opens a page on the phone to
/// download the file, or upload files back into Downloads. No app, no cloud, no account.
/// </summary>
internal sealed class PhoneSendModule : IShammyModule
{
    const string PhoneGlyph = "";

    ModuleContext? _ctx;
    PhoneTransfer? _transfer;

    public string Name => "phonesend";

    public void Start(ModuleContext ctx)
    {
        _ctx = ctx;
        ctx.AddShelfAction(new ShelfAction("Send to phone", item =>
        {
            if (Send(item.Path) is { } error) ctx.ShowMessage(PhoneGlyph, error, error: true);
        }));
        ctx.AddCommand(new SendCommand(this));
        // "Send to phone" on the drop zone: one file goes as is; several (or a folder) go as one zip.
        ctx.Notch.DropZone.ActionDropped += (action, files) =>
        {
            if (action == Shammy.Core.DropActions.DropAction.Phone) _ = SendDroppedAsync(files);
        };

        ctx.AddPipeCommand("phone-send", arg =>
        {
            var path = arg.Trim().Trim('"');
            if (path.Length == 0) return "error: usage phone-send <path>";
            return Send(path) is { } error ? "error: " + error : _transfer!.Url;
        });
        ctx.AddPipeCommand("phone-stop", _ =>
        {
            if (_transfer is not { IsRunning: true }) return "not running";
            _transfer.Stop();
            return "stopped";
        });
        ctx.AddPipeCommand("phone-state", _ => _transfer is { IsRunning: true } t
            ? string.Create(CultureInfo.InvariantCulture,
                $"running url={t.Url} file=\"{Path.GetFileName(t.FilePath)}\" sent={t.BytesSent}/{t.FileLength} active={t.ActiveDownloads} completed={t.CompletedDownloads} received={t.FilesReceived}")
            : "stopped");
    }

    /// <summary>Starts sharing a file (replacing any current transfer). Returns a user-facing error, or null.</summary>
    string? Send(string path)
    {
        if (_ctx is null) return "Not ready";
        _transfer?.Stop();
        _transfer = null;
        try
        {
            var transfer = PhoneTransfer.Start(_ctx, path);
            transfer.Ended += () =>
            {
                if (ReferenceEquals(_transfer, transfer)) _transfer = null;
            };
            _transfer = transfer;
            return null;
        }
        catch (PhoneSendException ex)
        {
            return ex.Message;
        }
    }

    async Task SendDroppedAsync(string[] files)
    {
        if (_ctx is null) return;
        var items = files.Where(f => File.Exists(f) || Directory.Exists(f)).ToList();
        if (items.Count == 0) return;
        string path = items[0];
        if (items.Count > 1 || Directory.Exists(path))
        {
            try
            {
                // A zip in Shammy's own temp folder: nothing appears next to the user's files.
                var dir = Path.Combine(Path.GetTempPath(), "Shammy", "phone-send");
                Directory.CreateDirectory(dir);
                _ctx.ShowMessage(PhoneGlyph, "Zipping for your phone…", seconds: 30);
                path = await Task.Run(() =>
                {
                    var zip = Shammy.Core.DropActions.DropPlan.ZipPath(items, _ => false);
                    zip = Shammy.Core.DropActions.DropPlan.Unique(dir, Path.GetFileNameWithoutExtension(zip), ".zip", File.Exists);
                    DropActions.DropFiles.Zip(items, zip);
                    return zip;
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                _ctx.Warn("Zipping for the phone failed", ex);
                _ctx.ShowMessage(PhoneGlyph, "Couldn't zip that", error: true);
                return;
            }
        }
        if (Send(path) is { } error) _ctx.ShowMessage(PhoneGlyph, error, error: true);
    }

    /// <summary>Shelf entries that are files (folders can't be sent), oldest first like the shelf.</summary>
    List<ShelfItem> ShelfFiles() =>
        _ctx?.Services.Shelf.Items.Where(i => File.Exists(i.Path)).ToList() ?? new List<ShelfItem>();

    bool CanHandle(string text)
    {
        if (!PhoneSendPhrase.TryParse(text, out var query)) return false;
        if (query is null) return true;
        // "send money to my phone" is a web search unless the shelf has something like it, or it
        // clearly names a file (then a miss is worth reporting).
        var files = ShelfFiles();
        return PhoneSendPhrase.FindItem(files.Select(f => f.Name).ToList(), query) >= 0
            || PhoneSendPhrase.LooksLikeFileName(query);
    }

    CommandResult Run(string text)
    {
        PhoneSendPhrase.TryParse(text, out var query);
        var files = ShelfFiles();
        if (files.Count == 0)
            return new CommandResult(PhoneGlyph, "Nothing on the shelf", "Drop a file on the notch first", IsError: true);
        int index = PhoneSendPhrase.FindItem(files.Select(f => f.Name).ToList(), query);
        if (index < 0)
            return new CommandResult(PhoneGlyph, "Not on the shelf", $"No shelf file matches “{query}”", IsError: true);

        var item = files[index];
        // The card waits behind this confirmation and appears when it clears.
        return Send(item.Path) is { } error
            ? new CommandResult(PhoneGlyph, error, item.Name, IsError: true)
            : new CommandResult(PhoneGlyph, "Scan to send to your phone", item.Name);
    }

    public void Dispose()
    {
        _transfer?.Dispose();
        _transfer = null;
    }

    sealed class SendCommand(PhoneSendModule module) : ITextCommand
    {
        public bool CanHandle(string text) => module.CanHandle(text);
        public Task<CommandResult> RunAsync(string text) => Task.FromResult(module.Run(text));
    }
}
