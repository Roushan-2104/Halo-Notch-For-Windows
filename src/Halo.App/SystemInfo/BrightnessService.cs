using System.Management;
using System.Windows.Threading;
using Halo.Infrastructure;

namespace Halo.SystemInfo;

/// <summary>
/// Built-in display brightness changes, via the WMI event laptops raise. External monitors don't report
/// brightness this way, so on a desktop this simply never fires.
/// </summary>
internal sealed class BrightnessService : IDisposable
{
    readonly Dispatcher _ui;
    ManagementEventWatcher? _watcher;

    public BrightnessService(Dispatcher ui) => _ui = ui;

    public event Action<float>? Changed;

    public bool IsSupported { get; private set; }

    public void Start()
    {
        try
        {
            var scope = new ManagementScope(@"root\WMI");
            _watcher = new ManagementEventWatcher(scope, new EventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
            _watcher.EventArrived += (_, e) =>
            {
                if (e.NewEvent.Properties["Brightness"].Value is byte level)
                    _ui.BeginInvoke(() => Changed?.Invoke(level / 100f));
            };
            _watcher.Start();
            IsSupported = true;
        }
        catch (Exception ex)
        {
            Log.Info($"Brightness events unavailable ({ex.Message.Trim()})");
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    public void Dispose()
    {
        try { _watcher?.Stop(); } catch (Exception) { }
        _watcher?.Dispose();
    }
}
