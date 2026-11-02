using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Halo.Modules.MeetingSystem;

/// <summary>
/// Mute state of the default communications microphone (the one meeting apps use). Raises
/// <see cref="Changed"/> on the UI thread for changes from anywhere (Windows, the meeting app, a
/// headset button) and follows the default device when it changes.
/// </summary>
internal sealed class MicMute : IMMNotificationClient, IDisposable
{
    readonly Dispatcher _ui;
    readonly Action<string, Exception?> _warn;
    readonly MMDeviceEnumerator _enumerator = new();
    MMDevice? _device;

    public MicMute(Dispatcher ui, Action<string, Exception?> warn)
    {
        _ui = ui;
        _warn = warn;
    }

    public event Action? Changed;

    public bool HasDevice => _device is not null;
    public string? DeviceName { get; private set; }

    public bool Muted
    {
        get
        {
            try { return _device?.AudioEndpointVolume.Mute ?? false; }
            catch (Exception) { return false; }
        }
    }

    public void Start()
    {
        try { _enumerator.RegisterEndpointNotificationCallback(this); }
        catch (Exception ex) { _warn("Microphone device notifications unavailable", ex); }
        Bind();
    }

    void Bind()
    {
        if (_device is not null)
        {
            try { _device.AudioEndpointVolume.OnVolumeNotification -= OnNotify; } catch (Exception) { }
            _device.Dispose();
            _device = null;
            DeviceName = null;
        }
        try
        {
            _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            _device.AudioEndpointVolume.OnVolumeNotification += OnNotify;
            DeviceName = _device.FriendlyName;
        }
        catch (Exception ex)
        {
            _device = null;
            _warn("No default microphone", ex);
        }
    }

    void OnNotify(AudioVolumeNotificationData d) => _ui.BeginInvoke(() => Changed?.Invoke());

    /// <summary>Sets the mute state; returns the state actually applied, or null without a microphone.</summary>
    public bool? Set(bool muted)
    {
        if (_device is null) Bind();
        if (_device is null) return null;
        try
        {
            _device.AudioEndpointVolume.Mute = muted;
            Changed?.Invoke();
            return _device.AudioEndpointVolume.Mute;
        }
        catch (Exception ex)
        {
            _warn("Changing microphone mute failed", ex);
            return null;
        }
    }

    public bool? Toggle() => Set(!Muted);

    void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow != DataFlow.Capture || role != Role.Communications) return;
        _ui.BeginInvoke(() =>
        {
            Bind();
            Changed?.Invoke();
        });
    }

    void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState) { }
    void IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId) { }
    void IMMNotificationClient.OnDeviceRemoved(string deviceId) { }
    void IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    public void Dispose()
    {
        try { _enumerator.UnregisterEndpointNotificationCallback(this); } catch (Exception) { }
        if (_device is not null)
        {
            try { _device.AudioEndpointVolume.OnVolumeNotification -= OnNotify; } catch (Exception) { }
            _device.Dispose();
        }
        _enumerator.Dispose();
    }
}
