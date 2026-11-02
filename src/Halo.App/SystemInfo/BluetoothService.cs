using System.Windows.Threading;
using Halo.Infrastructure;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace Halo.SystemInfo;

/// <summary>
/// Announces when headphones/speakers connect or disconnect, the AirPods moment. Only audio devices
/// are announced: mice and keyboards reconnect on every wake and would just be noise.
/// </summary>
internal sealed class BluetoothService : IDisposable
{
    readonly Dispatcher _ui;
    readonly Dictionary<string, string> _connected = new();
    DeviceWatcher? _watcher;
    volatile bool _enumerated;

    public BluetoothService(Dispatcher ui) => _ui = ui;

    public event Action<string, bool>? AudioDeviceChanged;

    public void Start()
    {
        try
        {
            var selector = BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected);
            _watcher = DeviceInformation.CreateWatcher(selector);
            // Devices already connected at startup arrive as Added before EnumerationCompleted; note
            // that at event time, since the class lookup below is async and finishes later.
            _watcher.Added += (sender, info) => _ = OnAddedAsync(info, announce: _enumerated);
            _watcher.Removed += (_, update) => _ui.BeginInvoke(() => OnRemoved(update.Id));
            _watcher.Updated += (_, _) => { };
            _watcher.EnumerationCompleted += (_, _) => _enumerated = true;
            _watcher.Start();
        }
        catch (Exception ex)
        {
            Log.Info($"Bluetooth unavailable ({ex.Message.Trim()})");
        }
    }

    async Task OnAddedAsync(DeviceInformation info, bool announce)
    {
        bool isAudio = false;
        try
        {
            using var device = await BluetoothDevice.FromIdAsync(info.Id);
            isAudio = device?.ClassOfDevice.MajorClass == BluetoothMajorClass.AudioVideo;
        }
        catch (Exception ex)
        {
            Log.Warn("Reading Bluetooth device failed", ex);
        }
        if (!isAudio) return;

        await _ui.InvokeAsync(() =>
        {
            _connected[info.Id] = info.Name;
            if (announce) AudioDeviceChanged?.Invoke(info.Name, true);
        });
    }

    void OnRemoved(string id)
    {
        if (!_connected.Remove(id, out var name)) return;
        AudioDeviceChanged?.Invoke(name, false);
    }

    public void Dispose()
    {
        try
        {
            if (_watcher?.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) _watcher.Stop();
        }
        catch (Exception) { }
    }
}
