namespace Shammy.Core.Activities;

public enum HudKind { Volume, Brightness }

public sealed record HudPayload(HudKind Kind, double Level, bool Muted = false);

public sealed record BatteryPayload(int Percent, bool Charging);

public sealed record BluetoothPayload(string Name, bool Connected, bool IsAudio);

public sealed record DownloadPayload(string Name, string? Path, long Bytes, int Count);

public sealed record ProgressPayload(string Glyph, string Label, double Progress);

public sealed record MessagePayload(string Glyph, string Text, bool IsError = false);
