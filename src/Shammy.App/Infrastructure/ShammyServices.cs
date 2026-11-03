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
    AppSettings Settings);
