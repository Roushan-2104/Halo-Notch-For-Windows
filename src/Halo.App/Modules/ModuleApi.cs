using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Halo.Core.Activities;
using Halo.Infrastructure;
using Halo.Shelf;
using Halo.Shell;
using Halo.Views;

namespace Halo.Modules;

/// <summary>
/// A self-contained feature (timers, clipboard, music commands...). Every non-abstract class that
/// implements this is found by reflection at startup and started once, so adding a feature never
/// means editing the host. Keep everything a module needs in its own folder under Modules/.
/// </summary>
internal interface IHaloModule : IDisposable
{
    string Name { get; }

    /// <summary>Called once on the UI thread after the notch and core services exist.</summary>
    void Start(ModuleContext ctx);
}

/// <summary>Text typed in the search box (tap Right Alt) or spoken (hold Right Alt), e.g. "timer 25m".</summary>
internal interface ITextCommand
{
    /// <summary>Cheap synchronous check whether the text is this command. Checked before web search.</summary>
    bool CanHandle(string text);

    Task<CommandResult> RunAsync(string text);
}

/// <summary>What the notch shows briefly after a command ran.</summary>
internal sealed record CommandResult(string Glyph, string Title, string? Detail = null, bool IsError = false);

/// <summary>
/// Content a module draws in the compact notch's two ears. Put an instance in
/// <see cref="Activity.Payload"/> with <see cref="ActivityKind.Custom"/>. Re-posting the same instance
/// keeps the built UI (update your own elements for live values); a new instance rebuilds.
/// </summary>
internal interface ICompactContent
{
    /// <summary>Fill the ears and return the width one ear needs (the pill grows to fit).</summary>
    double Build(ContentControl left, ContentControl right);

    /// <summary>Optional content for the small detached bubble when this activity is the secondary one.</summary>
    UIElement? BuildBubble() => null;
}

/// <summary>What was circled on the frozen screen.</summary>
internal sealed record SelectionInfo(string Text, byte[]? Png, BitmapSource? Image, bool IsImage);

/// <summary>Gets first look at every circle; can take over instead of a web search (e.g. a QR code).</summary>
internal interface ISelectionHandler
{
    Task<CommandResult?> TryTakeOverAsync(SelectionInfo selection);
}

/// <summary>An extra chip on the results card. <see cref="Selection"/> is null for typed/spoken searches.</summary>
internal sealed record ResultChip(string Label, Func<SelectionInfo?, bool> IsVisible, Action<SelectionInfo?, ResultView> OnClick);

internal sealed record ShelfAction(string Label, Action<ShelfItem> Run);

internal sealed record ModuleTrayItem(string Text, Action? OnClick = null, Func<bool>? IsChecked = null, Action<bool>? SetChecked = null);

/// <summary>Everything modules register; the host and the search flow read from here.</summary>
internal sealed class ModuleRegistry
{
    public List<ITextCommand> Commands { get; } = new();
    public List<ISelectionHandler> SelectionHandlers { get; } = new();
    public List<ResultChip> ResultChips { get; } = new();
    public List<ShelfAction> ShelfActions { get; } = new();
    public List<ModuleTrayItem> TrayItems { get; } = new();
    public Dictionary<string, Func<string, string>> PipeCommands { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>A module's handle on Halo. One per module.</summary>
internal sealed class ModuleContext
{
    static readonly Lazy<HttpClient> SharedHttp = new(() =>
    {
        // Every module only fetches small JSON/RSS; cap responses so a hostile server can't balloon memory.
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 4 * 1024 * 1024 };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Halo/0.1");
        return http;
    });

    readonly ActivityManager _activities;
    readonly ModuleRegistry _registry;

    internal ModuleContext(string name, Dispatcher ui, HaloServices services, NotchWindow notch,
        ActivityManager activities, ModuleRegistry registry)
    {
        Name = name;
        Ui = ui;
        Services = services;
        Notch = notch;
        _activities = activities;
        _registry = registry;
        notch.NotchWheel += d => NotchWheel?.Invoke(d);
        notch.NotchHorizontalWheel += d => NotchHorizontalWheel?.Invoke(d);
    }

    public string Name { get; }
    public Dispatcher Ui { get; }
    public HaloServices Services { get; }
    public AppSettings Settings => Services.Settings;
    public NotchWindow Notch { get; }

    /// <summary>Shared client with a browser-like user agent and a 15 s timeout.</summary>
    public HttpClient Http => SharedHttp.Value;

    // ---- Activities (compact notch) -------------------------------------------------------------

    /// <summary>Show or update an activity. Prefix ids with your module name. Higher priority wins the pill
    /// (HUD 90, notifications 80, battery 70-75, Bluetooth 60, messages 50, downloads 40, media 20).</summary>
    public void Post(Activity activity) => _activities.Post(activity);

    public bool Remove(string id) => _activities.Remove(id);
    public Activity? Get(string id) => _activities.Get(id);

    /// <summary>A short message in the compact notch.</summary>
    public void ShowMessage(string glyph, string text, bool error = false, double seconds = 3) =>
        Post(new Activity($"{Name}.message", ActivityKind.Message, 50, new MessagePayload(glyph, text, error),
            DateTime.UtcNow.AddSeconds(seconds)));

    // ---- Registration ----------------------------------------------------------------------------

    public void AddCommand(ITextCommand command) => _registry.Commands.Add(command);
    public void AddSelectionHandler(ISelectionHandler handler) => _registry.SelectionHandlers.Add(handler);
    public void AddResultChip(ResultChip chip) => _registry.ResultChips.Add(chip);
    public void AddShelfAction(ShelfAction action) => _registry.ShelfActions.Add(action);
    public void AddTrayItem(string text, Action onClick) => _registry.TrayItems.Add(new ModuleTrayItem(text, onClick));

    public void AddTrayToggle(string text, Func<bool> isChecked, Action<bool> setChecked) =>
        _registry.TrayItems.Add(new ModuleTrayItem(text, null, isChecked, setChecked));

    /// <summary>A command for the local test pipe, e.g. "timer-test 5".</summary>
    public void AddPipeCommand(string name, Func<string, string> handler) => _registry.PipeCommands[name] = handler;

    /// <summary>A tab in the expanded notch (the card is 480×204; your panel gets about 436×150).</summary>
    public void AddTab(string glyph, string tooltip, FrameworkElement panel) => Notch.ExpandedView.AddTab(glyph, tooltip, panel);

    /// <summary>
    /// Open the expanded notch on one of your tabs. Safe to call from a command's RunAsync: the tab opens
    /// right after the confirmation card closes. With <paramref name="hold"/> (the default) it stays open
    /// until the pointer has visited and left, or 8 s if the user never comes over; no timers needed.
    /// </summary>
    public void OpenTab(FrameworkElement panel, bool hold = true) => Notch.OpenTab(panel, hold);

    // ---- Cards, glow, input ----------------------------------------------------------------------

    /// <summary>
    /// Show arbitrary content in the notch (e.g. a QR code). The notch morphs to width×height. With
    /// <paramref name="interactive"/> it takes keyboard focus and closes when you click elsewhere.
    /// </summary>
    public void ShowCard(FrameworkElement content, double width, double height, bool interactive = false) =>
        Notch.ShowCard(content, width, height, interactive);

    public void CloseCard() => Notch.CloseCard();

    /// <summary>Keep the notch's edge lit in a colour while a state lasts; null removes it.</summary>
    public void SetGlow(Color? color) => Notch.SetGlow(color);

    /// <summary>Briefly light the notch's edge in a colour (an event, not a state).</summary>
    public void FlashEdge(Color color) => Notch.FlashEdge(color);

    /// <summary>Vertical mouse wheel over the compact/idle notch (delta, 120 per notch).</summary>
    public event Action<int>? NotchWheel;

    /// <summary>Horizontal wheel / two-finger swipe over the notch (positive = right).</summary>
    public event Action<int>? NotchHorizontalWheel;

    // ---- Storage and helpers ---------------------------------------------------------------------

    /// <summary>
    /// Load this module's data (%LOCALAPPDATA%\Halo\modules\&lt;name&gt;.dat): JSON encrypted with Windows
    /// DPAPI, so only this Windows account can read it. A plain &lt;name&gt;.json from older versions is
    /// still read, and replaced by the encrypted file on the next save.
    /// </summary>
    public T Load<T>() where T : new()
    {
        try
        {
            if (File.Exists(StorePath))
            {
                var json = ProtectedData.Unprotect(File.ReadAllBytes(StorePath), Entropy, DataProtectionScope.CurrentUser);
                return JsonSerializer.Deserialize<T>(json) ?? new T();
            }
            if (File.Exists(LegacyPath)) return JsonSerializer.Deserialize<T>(File.ReadAllText(LegacyPath)) ?? new T();
        }
        catch (Exception ex) when (ex is IOException or JsonException or CryptographicException)
        {
            Log.Warn($"{Name}: data unreadable, starting fresh", ex);
        }
        return new T();
    }

    public void Save<T>(T data)
    {
        try
        {
            var tmp = StorePath + ".tmp";
            var json = JsonSerializer.SerializeToUtf8Bytes(data);
            File.WriteAllBytes(tmp, ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser));
            File.Move(tmp, StorePath, true);
            if (File.Exists(LegacyPath)) File.Delete(LegacyPath);
        }
        catch (Exception ex) when (ex is IOException or CryptographicException)
        {
            Log.Warn($"{Name}: saving data failed", ex);
        }
    }

    static readonly byte[] Entropy = "Halo module store v1"u8.ToArray();

    string StorePath => Path.Combine(StoreDir, $"{Name}.dat");
    string LegacyPath => Path.Combine(StoreDir, $"{Name}.json");

    static string StoreDir
    {
        get
        {
            var dir = Path.Combine(Paths.Root, "modules");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Open a URL, file or shell target (e.g. "shell:AppsFolder\&lt;AUMID&gt;").</summary>
    public void Open(string target, string? arguments = null)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target)
            {
                UseShellExecute = true,
                Arguments = arguments ?? "",
            });
        }
        catch (Exception ex)
        {
            Log.Warn($"{Name}: opening {target} failed", ex);
        }
    }

    public void Info(string message) => Log.Info($"[{Name}] {message}");
    public void Warn(string message, Exception? ex = null) => Log.Warn($"[{Name}] {message}", ex);
}
