using System.Globalization;
using System.Text.RegularExpressions;

namespace Shammy.Core.Devices;

/// <summary>
/// Reading a Bluetooth headset's battery the way Windows Settings does. The level is the device property
/// DEVPKEY_Bluetooth_Battery ({104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2, one byte 0–100) and it sits on one of
/// the headset's profile nodes (on Airdopes 311 Pro it's the "Hands-Free AG" node), not on the device node
/// that carries the friendly name. Both instance ids embed the 12-hex-digit Bluetooth address, which ties
/// them together. Earbuds report a single level here; Windows has no standard left/right/case split.
/// </summary>
public static partial class BluetoothBattery
{
    public static readonly Guid PropertySet = new("104EA319-6EE2-4701-BD47-8DDBF425BBE5");
    public const uint PropertyId = 2;

    /// <summary>DEVPROP_TYPE_BYTE: how Windows stores the level.</summary>
    public const uint TypeByte = 0x03;

    // "BTHENUM\DEV_25BF04C71669\7&…", "BTHENUM\{0000111E-…}_VID&000105D6_PID&000A\7&1DA13D4F&0&25BF04C71669_C00000000",
    // "BTHLE\DEV_D0A4CA1B2C3D\…": the address follows "DEV_" or the last "&", and ends at "_" / "\" / the end.
    [GeneratedRegex(@"(?:DEV_|&)([0-9A-F]{12})(?=_|\\|$)", RegexOptions.IgnoreCase)]
    private static partial Regex AddressPattern();

    /// <summary>The Bluetooth address in a device instance id, as 12 upper-case hex digits; null when there is none.</summary>
    public static string? AddressFromInstanceId(string? instanceId)
    {
        if (string.IsNullOrEmpty(instanceId)) return null;
        var matches = AddressPattern().Matches(instanceId);
        // The address is the last 12-hex group; "0&000000000000" (a local serial port) is not a device.
        for (int i = matches.Count - 1; i >= 0; i--)
        {
            var hex = matches[i].Groups[1].Value.ToUpperInvariant();
            if (hex != "000000000000") return hex;
        }
        return null;
    }

    /// <summary>The address WinRT reports (BluetoothDevice.BluetoothAddress) in the same 12-hex form.</summary>
    public static string AddressFromNumber(ulong address) => address.ToString("X12", CultureInfo.InvariantCulture);

    /// <summary>The level from the raw property value; null for a wrong type, an empty buffer or a value past 100.</summary>
    public static int? ParsePercent(uint type, ReadOnlySpan<byte> data)
    {
        if (data.Length < 1) return null;
        int value = type switch
        {
            TypeByte => data[0],
            0x05 or 0x07 when data.Length >= 4 => BitConverter.ToInt32(data[..4]), // DEVPROP_TYPE_INT32 / UINT32, just in case
            _ => -1,
        };
        return value is >= 0 and <= 100 ? value : null;
    }

    /// <summary>"Airdopes 311 Pro · 80%" for the connect pill; just the name when the level is unknown.</summary>
    public static string Pill(string name, int? percent) =>
        percent is { } p ? $"{name} \u00B7 {p}%" : name;

    /// <summary>
    /// Several profile nodes of one headset can carry the property; keep one level per address (the highest:
    /// a node that hasn't refreshed yet tends to report a stale lower value, never a higher one).
    /// </summary>
    public static Dictionary<string, int> ByAddress(IEnumerable<(string InstanceId, int Percent)> readings)
    {
        var levels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, percent) in readings)
        {
            if (AddressFromInstanceId(id) is not { } address) continue;
            levels[address] = levels.TryGetValue(address, out var old) ? Math.Max(old, percent) : percent;
        }
        return levels;
    }
}
