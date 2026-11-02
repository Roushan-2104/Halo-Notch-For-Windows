using System.Globalization;
using Halo.Core.Launcher;
using Halo.Interop;

namespace Halo.Modules.Launcher;

/// <summary>
/// Spotlight for Windows: "open spotify", "launch vsc", "open github.com", "open downloads" from the
/// type box or by voice. Only claims the text when it really names an app, a domain or a known folder.
/// </summary>
internal sealed class LauncherModule : IHaloModule, ITextCommand
{
    static readonly Dictionary<string, (Guid Id, string Title)> Folders = new()
    {
        ["downloads"] = (Native.DownloadsFolder, "Downloads"),
        ["documents"] = (new Guid("FDD39AD0-238F-46AF-ADB4-6C85480369C7"), "Documents"),
        ["desktop"] = (new Guid("B4BFCC3A-DB2C-424C-B029-7FE99A87C641"), "Desktop"),
        ["pictures"] = (new Guid("33E28130-4E1E-4676-835A-98395C3BC3BB"), "Pictures"),
        ["music"] = (new Guid("4BD8D571-6D19-48D3-BE97-422220080E43"), "Music"),
        ["videos"] = (new Guid("18989B1D-99B5-455B-841C-AB7C74E4DDFC"), "Videos"),
    };

    ModuleContext _ctx = null!;
    AppIndex _index = null!;

    public string Name => "launcher";

    public void Start(ModuleContext ctx)
    {
        _ctx = ctx;
        _index = new AppIndex(ctx);
        ctx.AddCommand(this);
        ctx.AddPipeCommand("launcher-find", Find);
        ctx.AddPipeCommand("launcher-count", arg =>
        {
            if (arg.Trim() == "refresh") _index.Refresh();
            return $"apps={_index.Apps.Count} source={_index.Source} built={_index.BuiltAt?.ToString("HH:mm:ss", CultureInfo.InvariantCulture) ?? "-"} " +
                   $"building={_index.IsBuilding}";
        });
    }

    public bool CanHandle(string text) => LaunchResolver.Resolve(text, _index.Apps) is not null;

    public Task<CommandResult> RunAsync(string text)
    {
        var plan = LaunchResolver.Resolve(text, _index.Apps);
        return Task.FromResult(plan is null ? new CommandResult("", "Nothing to open", text, IsError: true) : Launch(plan));
    }

    CommandResult Launch(LaunchPlan plan)
    {
        var request = plan.Request;
        switch (request.Kind)
        {
            case LaunchKind.Folder:
            {
                var (id, title) = Folders[request.Target];
                var path = Native.KnownFolder(id);
                if (path is null || !Directory.Exists(path))
                    return new CommandResult("", $"No {title} folder", null, IsError: true);
                _ctx.Open(path);
                return new CommandResult("", "Opening", title);
            }
            case LaunchKind.Url:
                _ctx.Open(request.Target);
                return new CommandResult("", "Opening", ShortUrl(request.Target));
            default:
            {
                var app = plan.Match!.Value.App;
                if (!plan.CanLaunch)
                    return new CommandResult("", $"No app called “{request.Target}”", $"Did you mean {app.Name}?", IsError: true);
                _ctx.Info("Launching an app");
                _ctx.Open(app.Target);
                return new CommandResult("", "Opening", app.Name);
            }
        }
    }

    static string ShortUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) ? (u.Host + u.PathAndQuery).TrimEnd('/') : url;

    /// <summary>Pipe: "launcher-find vsc" → top matches; with a verb ("open vsc") also what the command would do.</summary>
    string Find(string arg)
    {
        var apps = _index.Apps;
        var request = LaunchResolver.Parse(arg);
        var query = request?.Target ?? arg.Trim();
        var top = AppMatcher.Rank(query, apps, 3);
        var matches = top.Count == 0 ? "no matches" : string.Join(" | ", top.Select(m => $"{m.App.Name}={m.Score}"));
        if (request is null) return matches;

        var plan = LaunchResolver.Resolve(arg, apps);
        if (plan is null) return $"handle=false {matches}";
        var what = plan.Request.Kind == LaunchKind.App ? plan.Match!.Value.App.Name : plan.Request.Target;
        return $"handle=true kind={plan.Request.Kind} launch={plan.CanLaunch} target={what} | {matches}";
    }

    public void Dispose() => _index?.Dispose();
}
