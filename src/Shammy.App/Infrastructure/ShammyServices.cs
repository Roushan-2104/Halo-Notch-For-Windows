using Shammy.Notifications;
using Shammy.Playback;
using Shammy.Shelf;
using Shammy.SystemInfo;

namespace Shammy.Infrastructure;

/// <summary>The long-lived services views read from.</summary>
internal sealed record ShammyServices(
    MediaService Media,
    AudioVisualizer Visualizer,
    BatteryService Battery,
    ShelfStore Shelf,
    NotificationService Notifications,
    AppSettings Settings)
{
    /// <summary>Master volume (the music card's slider); set once the host has started it, null if audio failed.</summary>
    public VolumeService? Volume { get; set; }

    /// <summary>Connected Bluetooth audio devices and their battery (the System card); null if Bluetooth is unavailable.</summary>
    public BluetoothService? Bluetooth { get; set; }
}
