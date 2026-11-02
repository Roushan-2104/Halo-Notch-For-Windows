using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Halo.Infrastructure;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Halo.Notifications;

/// <summary>A toast another app posted, flattened for display.</summary>
internal sealed class HaloNotification
{
    public required uint Id { get; init; }
    public required string AppId { get; init; }
    public required string AppName { get; init; }
    public ImageSource? Icon { get; init; }
    public required string Title { get; init; }
    public required string Body { get; init; }
    public required DateTimeOffset Time { get; init; }

    public string TimeText
    {
        get
        {
            var age = DateTimeOffset.Now - Time;
            return age.TotalMinutes < 1 ? "now"
                : age.TotalHours < 1 ? $"{(int)age.TotalMinutes}m"
                : age.TotalDays < 1 ? $"{(int)age.TotalHours}h"
                : Time.LocalDateTime.ToString("d MMM");
        }
    }
}

/// <summary>
/// Reads other apps' toasts through UserNotificationListener (the API Windows uses for wearables).
/// Arrivals come from the NotificationChanged event where Windows supports it for desktop apps, with
/// polling as a fallback and safety net, so a missed event never loses a notification.
/// </summary>
internal sealed class NotificationService : IDisposable
{
    const int HistoryLimit = 40;

    readonly Dispatcher _ui;
    readonly HashSet<uint> _known = new();
    readonly Dictionary<string, ImageSource?> _icons = new();
    readonly DispatcherTimer _poll;
    UserNotificationListener? _listener;
    bool _polling;

    public NotificationService(Dispatcher ui)
    {
        _ui = ui;
        _poll = new DispatcherTimer(TimeSpan.FromMilliseconds(700), DispatcherPriority.Background, async (_, _) => await PollAsync(), ui) { IsEnabled = false };
    }

    public event Action<HaloNotification>? Arrived;

    public ObservableCollection<HaloNotification> History { get; } = new();

    public bool IsAvailable { get; private set; }
    public string? Problem { get; private set; }

    public async Task StartAsync()
    {
        try
        {
            _listener = UserNotificationListener.Current;
            var status = _listener.GetAccessStatus();
            if (status == UserNotificationListenerAccessStatus.Unspecified) status = await _listener.RequestAccessAsync();
            if (status != UserNotificationListenerAccessStatus.Allowed)
            {
                Problem = "Notification access is off (Settings › Privacy › Notifications)";
                Log.Warn($"Notification access: {status}");
                return;
            }
        }
        catch (Exception ex)
        {
            // Older Windows builds only allow this for packaged apps.
            Problem = "This Windows version only shares notifications with packaged apps";
            Log.Warn("Notification listener unavailable", ex);
            return;
        }

        IsAvailable = true;
        // Everything already in Action Center is history, not news.
        var existing = await _listener.GetNotificationsAsync(NotificationKinds.Toast).AsTask().ConfigureAwait(true);
        foreach (var n in existing.OrderByDescending(n => n.CreationTime))
        {
            _known.Add(n.Id);
            if (Convert(n) is { } h) AddToHistory(h, atEnd: true);
        }

        try
        {
            _listener.NotificationChanged += (_, e) =>
            {
                if (e.ChangeKind == UserNotificationChangedKind.Added) _ui.BeginInvoke(async () => await PollAsync());
            };
            Log.Info("Notifications: listening for events");
            // Events can still be dropped (e.g. after sleep); a slow poll catches anything missed.
            _poll.Interval = TimeSpan.FromSeconds(5);
        }
        catch (Exception)
        {
            // Desktop apps don't get this event on most Windows builds; polling every 0.7 s is cheap.
            Log.Info("Notifications: polling");
        }
        _poll.Start();
    }

    async Task PollAsync()
    {
        if (_listener is null || _polling) return;
        _polling = true;
        try
        {
            var current = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            var fresh = current.Where(n => !_known.Contains(n.Id)).OrderBy(n => n.CreationTime).ToList();
            _known.IntersectWith(current.Select(n => n.Id));
            foreach (var n in fresh)
            {
                _known.Add(n.Id);
                if (Convert(n) is not { } h) continue;
                AddToHistory(h, atEnd: false);
                Arrived?.Invoke(h);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Reading notifications failed", ex);
        }
        finally
        {
            _polling = false;
        }
    }

    HaloNotification? Convert(UserNotification n)
    {
        try
        {
            var binding = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
            var texts = binding?.GetTextElements().Select(t => t.Text?.Trim() ?? "").Where(t => t.Length > 0).ToList() ?? new List<string>();
            if (texts.Count == 0) return null;

            string appId = "", appName = "Notification";
            try
            {
                appId = n.AppInfo.AppUserModelId ?? "";
                appName = n.AppInfo.DisplayInfo.DisplayName is { Length: > 0 } name ? name : appName;
            }
            catch (Exception)
            {
                // Some tray apps post toasts without registered app info; show them anyway.
            }

            return new HaloNotification
            {
                Id = n.Id,
                AppId = appId,
                AppName = appName,
                Icon = IconFor(n, appId),
                Title = texts[0],
                Body = string.Join("\n", texts.Skip(1)),
                Time = n.CreationTime,
            };
        }
        catch (Exception ex)
        {
            Log.Warn("Unreadable notification", ex);
            return null;
        }
    }

    ImageSource? IconFor(UserNotification n, string appId)
    {
        if (_icons.TryGetValue(appId, out var cached)) return cached;
        // Desktop apps (Chrome, WhatsApp desktop installers, system toasts) have no packaged logo, but
        // the Start menu's entry for the app ID does, so ask the shell for that first.
        ImageSource? icon = appId.Length > 0 ? Shelf.ShellIcons.Get($@"shell:AppsFolder\{appId}", 64) : null;
        if (icon is not null)
        {
            _icons[appId] = icon;
            return icon;
        }
        try
        {
            var stream = n.AppInfo.DisplayInfo.GetLogo(new Windows.Foundation.Size(64, 64)).OpenReadAsync().AsTask().GetAwaiter().GetResult();
            using var s = stream.AsStreamForRead();
            var ms = new MemoryStream();
            s.CopyTo(ms);
            ms.Position = 0;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 64;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            icon = bmp;
        }
        catch (Exception)
        {
            // No icon anywhere (e.g. some system toasts); the views fall back to a bell glyph.
        }
        _icons[appId] = icon;
        return icon;
    }

    void AddToHistory(HaloNotification n, bool atEnd)
    {
        if (atEnd) History.Add(n);
        else History.Insert(0, n);
        while (History.Count > HistoryLimit) History.RemoveAt(History.Count - 1);
    }

    public void ClearHistory() => History.Clear();

    public void Dispose() => _poll.Stop();
}
