using System.Windows.Threading;
using Shammy.Core.Devices;
using Shammy.Infrastructure;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace Shammy.SystemInfo;

/// <summary>A connected Bluetooth headset or speaker; <see cref="Battery"/> is null until Windows reports a level.</summary>
internal sealed record BluetoothAudioDevice(string Id, string Name, string Address, int? Battery);

/// <summary>
/// Announces when headphones/speakers connect or disconnect, the AirPods moment, and knows their battery
/// level. Only audio devices are tracked: mice and keyboards reconnect on every wake and would just be noise.
/// Battery levels are read on connect and when the System card asks; there is no background polling.
/// </summary>
internal sealed class BluetoothService : IDisposable
{
    // A headset reports its level over the hands-free link a moment after connecting; look a few times.
    static readonly TimeSpan[] ConnectReads = { TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(10) };

    readonly Dispatcher _ui;
    readonly object _gate = new();
    readonly Dictionary<string, BluetoothAudioDevice> _connected = new();
    DeviceWatcher? _watcher;
    volatile bool _enumerated;
    Task<Dictionary<string, int>>? _reading;

    public BluetoothService(Dispatcher ui) => _ui = ui;

    public event Action<string, bool>? AudioDeviceChanged;

    /// <summary>The first battery level read after a device connected (name, percent), for the connect pill.</summary>
    public event Action<string, int>? BatteryRead;

    /// <summary>A device connected or disconnected, or a battery level changed. On the UI thread.</summary>
    public event Action? Changed;

    /// <summary>Connected audio devices, oldest first. UI thread only.</summary>
    public IReadOnlyList<BluetoothAudioDevice> AudioDevices => _connected.Values.ToList();

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
        string address = "";
        try
        {
            using var device = await BluetoothDevice.FromIdAsync(info.Id);
            isAudio = device?.ClassOfDevice.MajorClass == BluetoothMajorClass.AudioVideo;
            if (device is not null) address = BluetoothBattery.AddressFromNumber(device.BluetoothAddress);
        }
        catch (Exception ex)
        {
            Log.Warn("Reading Bluetooth device failed", ex);
        }
        if (!isAudio) return;

        await _ui.InvokeAsync(() =>
        {
            _connected[info.Id] = new BluetoothAudioDevice(info.Id, info.Name, address, null);
            if (announce) AudioDeviceChanged?.Invoke(info.Name, true);
            Changed?.Invoke();
        });
        _ = ReadAfterConnectAsync(info.Id, announce);
    }

    async Task ReadAfterConnectAsync(string id, bool announce)
    {
        foreach (var delay in announce ? ConnectReads : new[] { TimeSpan.Zero })
        {
            await Task.Delay(delay).ConfigureAwait(false);
            await RefreshBatteryAsync().ConfigureAwait(false);
            var device = await _ui.InvokeAsync(() => _connected.GetValueOrDefault(id));
            if (device is null) return; // gone again
            if (device.Battery is { } level)
            {
                if (announce) await _ui.InvokeAsync(() => BatteryRead?.Invoke(device.Name, level));
                return;
            }
        }
    }

    /// <summary>Reads every connected device's level now (off the UI thread) and raises <see cref="Changed"/> if any moved.</summary>
    public async Task RefreshBatteryAsync()
    {
        Dictionary<string, int> levels;
        try
        {
            // Overlapping callers (a connect retry and the card opening) share one read.
            Task<Dictionary<string, int>> reading;
            lock (_gate)
                reading = _reading is { IsCompleted: false } r ? r : _reading = Task.Run(() => BluetoothBattery.ByAddress(BluetoothBatteryReader.ReadAll()));
            levels = await reading.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warn("Reading Bluetooth battery failed", ex);
            return;
        }
        await _ui.InvokeAsync(() =>
        {
            bool changed = false;
            foreach (var device in _connected.Values.ToList())
            {
                int? level = levels.TryGetValue(device.Address, out var l) ? l : null;
                if (level == device.Battery) continue;
                _connected[device.Id] = device with { Battery = level };
                changed = true;
            }
            if (changed) Changed?.Invoke();
        });
    }

    /// <summary>For the test pipe: every node with a level, and the connected devices it matched.</summary>
    public string Describe()
    {
        List<(string InstanceId, int Percent)> raw;
        try
        {
            raw = Task.Run(BluetoothBatteryReader.ReadAll).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            return "error: " + ex.Message;
        }
        var levels = BluetoothBattery.ByAddress(raw);
        foreach (var device in _connected.Values.ToList())
            _connected[device.Id] = device with { Battery = levels.TryGetValue(device.Address, out var l) ? l : null };
        var devices = _connected.Values.Select(d => $"{d.Name} [{d.Address}] battery={(d.Battery is { } b ? b + "%" : "-")}");
        var nodes = raw.Select(r => $"{BluetoothBattery.AddressFromInstanceId(r.InstanceId) ?? "?"}={r.Percent}%");
        return $"connected={(_connected.Count == 0 ? "-" : string.Join(", ", devices))} | nodes={(raw.Count == 0 ? "-" : string.Join(", ", nodes))}";
    }

    void OnRemoved(string id)
    {
        if (!_connected.Remove(id, out var device)) return;
        AudioDeviceChanged?.Invoke(device.Name, false);
        Changed?.Invoke();
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
