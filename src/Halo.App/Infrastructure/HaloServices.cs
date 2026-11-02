using Halo.Playback;
using Halo.Shelf;
using Halo.SystemInfo;

namespace Halo.Infrastructure;

/// <summary>The long-lived services views read from.</summary>
internal sealed record HaloServices(
    MediaService Media,
    AudioVisualizer Visualizer,
    BatteryService Battery,
    ShelfStore Shelf,
    AppSettings Settings);
