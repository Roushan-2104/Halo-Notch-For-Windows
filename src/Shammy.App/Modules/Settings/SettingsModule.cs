using System.Text;
using Shammy.Infrastructure;
using Shammy.Search;

namespace Shammy.Modules.Settings;

/// <summary>
/// The Settings card (a chip in the open notch) and the settings pipe commands: "settings-get [name]",
/// "settings-set &lt;name&gt; &lt;value&gt;", "settings-open", "notifications-unmute &lt;appId|all&gt;" and "mic-devices".
/// Never disabled by DisabledModules, since it's how modules are turned back on.
/// </summary>
internal sealed class SettingsModule : IShammyModule
{
    public const string ModuleName = "settings";

    ModuleContext _ctx = null!;
    SettingsCard _card = null!;

    public string Name => ModuleName;

    public void Start(ModuleContext ctx)
    {
        _ctx = ctx;
        _card = new SettingsCard(ctx.Settings, () => ctx.ModuleNames);
        ctx.AddCard(new NotchCard
        {
            Id = "settings",
            Title = "Settings",
            Width = 176,
            Order = 90,
            Chip = true,
            IsRelevant = () => false,
            Compact = _card.Compact,
            Detail = _card.BuildDetail,
        });
        // A change from the pipe shows on the card if it's open.
        ctx.Settings.Changed += OnChanged;

        ctx.AddPipeCommand("settings-get", Get);
        ctx.AddPipeCommand("settings-set", SetFromPipe);
        ctx.AddPipeCommand("settings-open", _ =>
        {
            Kit.CardKit.ShowDetailPopup(ctx, _card.BuildDetail);
            return "ok";
        });
        ctx.AddPipeCommand("notifications-unmute", Unmute);
        ctx.AddPipeCommand("notifications-muted", _ => $"muted={string.Join(",", ctx.Settings.MutedNotificationApps)}");
        ctx.AddPipeCommand("mic-devices", _ =>
        {
            var names = MicRecorder.DeviceNames();
            int chosen = Shammy.Core.Settings.DeviceMatch.Find(names, ctx.Settings.MicrophoneDeviceId);
            return $"chosen={(chosen < 0 ? "default" : chosen.ToString(System.Globalization.CultureInfo.InvariantCulture))} " +
                   string.Join(" | ", names.Select((n, i) => $"{i}:{n}"));
        });
    }

    public void Dispose()
    {
        if (_ctx is not null) _ctx.Settings.Changed -= OnChanged;
    }

    void OnChanged(string name) => _ctx.Ui.BeginInvoke(_card.Refresh);

    /// <summary>"settings-get" lists every setting as Name=value by section; "settings-get HoverSpeed" just that one.</summary>
    string Get(string arg)
    {
        var s = _ctx.Settings;
        if (arg.Trim().Length > 0)
            return SettingsCatalog.Find(arg) is { } info ? SettingsCatalog.Describe(s, info) : $"error: no setting '{arg.Trim()}'";
        var sb = new StringBuilder();
        foreach (var group in SettingsCatalog.Settings.GroupBy(i => i.Section))
            sb.Append('[').Append(group.Key).Append("] ").Append(string.Join(" ", group.Select(i => SettingsCatalog.Describe(s, i)))).Append(' ');
        return sb.ToString().TrimEnd();
    }

    /// <summary>"settings-set Use24Hour on", "settings-set HoverSpeed relaxed", "settings-set DisabledModules music,mirror".</summary>
    string SetFromPipe(string arg)
    {
        var parts = arg.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return "error: usage settings-set <name> <value>";
        if (SettingsCatalog.Find(parts[0]) is not { } info) return $"error: no setting '{parts[0]}'";
        if (SettingsCatalog.TrySet(_ctx.Settings, info, parts[1]) is { } error) return "error: " + error;
        return "ok " + SettingsCatalog.Describe(_ctx.Settings, info);
    }

    string Unmute(string arg)
    {
        var app = arg.Trim();
        if (app.Length == 0) return "error: usage notifications-unmute <appId|all>";
        var s = _ctx.Settings;
        if (app == "all")
        {
            int n = s.MutedNotificationApps.Count;
            s.MutedNotificationApps.Clear();
            s.Save();
            s.NotifyChanged(nameof(AppSettings.MutedNotificationApps));
            return $"ok unmuted {n}";
        }
        if (!s.UnmuteApp(app)) return $"error: '{app}' isn't muted";
        s.NotifyChanged(nameof(AppSettings.MutedNotificationApps));
        return "ok";
    }
}
