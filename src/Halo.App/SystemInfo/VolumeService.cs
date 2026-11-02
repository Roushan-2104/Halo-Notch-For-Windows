using System.Windows.Threading;
using Halo.Infrastructure;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Halo.SystemInfo;

/// <summary>
/// Master volume of the default playback device. Raises <see cref="Changed"/> for any change (keys,
/// taskbar slider, other apps) and follows the default device when the user switches outputs.
/// </summary>
internal sealed class VolumeService : IMMNotificationClient, IDisposable
{
    readonly Dispatcher _ui;
    readonly MMDeviceEnumerator _enumerator = new();
    MMDevice? _device;

    public VolumeService(Dispatcher ui) => _ui = ui;

    public event Action<float, bool>? Changed;
    public event Action? DefaultDeviceChanged;

    public float Level => _device?.AudioEndpointVolume.MasterVolumeLevelScalar ?? 0;
    public bool Muted => _device?.AudioEndpointVolume.Mute ?? false;

    public void Start()
    {
        _enumerator.RegisterEndpointNotificationCallback(this);
        Bind();
    }

    void Bind()
    {
        if (_device is not null)
        {
            _device.AudioEndpointVolume.OnVolumeNotification -= OnNotify;
            _device.Dispose();
            _device = null;
        }
        try
        {
            _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _device.AudioEndpointVolume.OnVolumeNotification += OnNotify;
        }
        catch (Exception ex)
        {
            Log.Warn("No default audio output", ex);
        }
    }

    void OnNotify(AudioVolumeNotificationData d) => _ui.BeginInvoke(() => Changed?.Invoke(d.MasterVolume, d.Muted));

    /// <summary>Same behaviour as the hardware keys: 2% steps, and turning up unmutes.</summary>
    public void Step(int direction)
    {
        if (_device is null) return;
        try
        {
            var v = _device.AudioEndpointVolume;
            if (direction == 0)
            {
                v.Mute = !v.Mute;
                return;
            }
            if (v.Mute && direction > 0) v.Mute = false;
            double snapped = Math.Round(v.MasterVolumeLevelScalar * 50) / 50;
            v.MasterVolumeLevelScalar = (float)Math.Clamp(snapped + direction * 0.02, 0, 1);
        }
        catch (Exception ex)
        {
            Log.Warn("Changing volume failed", ex);
        }
    }

    public void SetLevel(float level)
    {
        if (_device is null) return;
        _device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(level, 0, 1);
    }

    void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow != DataFlow.Render || role != Role.Multimedia) return;
        _ui.BeginInvoke(() =>
        {
            Bind();
            DefaultDeviceChanged?.Invoke();
        });
    }

    void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState) { }
    void IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId) { }
    void IMMNotificationClient.OnDeviceRemoved(string deviceId) { }
    void IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    public void Dispose()
    {
        try { _enumerator.UnregisterEndpointNotificationCallback(this); } catch (Exception) { }
        if (_device is not null) _device.AudioEndpointVolume.OnVolumeNotification -= OnNotify;
        _device?.Dispose();
        _enumerator.Dispose();
    }
}
