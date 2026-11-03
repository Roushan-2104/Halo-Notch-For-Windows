namespace Shammy.Core.Activities;

public enum HudKind { Volume, Brightness }

public sealed record HudPayload(HudKind Kind, double Level, bool Muted = false);

public sealed record BatteryPayload(int Percent, bool Charging);

/// <param name="Battery">The headset's battery level once Windows has it ("Airdopes 311 Pro · 80%"); null until then.</param>
public sealed record BluetoothPayload(string Name, bool Connected, bool IsAudio, int? Battery = null);

/// <param name="Total">The expected size when known; the pill then shows a percent instead of bytes.</param>
public sealed record DownloadPayload(string Name, string? Path, long Bytes, int Count, long? Total = null);

public sealed record ProgressPayload(string Glyph, string Label, double Progress);

public sealed record MessagePayload(string Glyph, string Text, bool IsError = false);
