using System.Runtime.InteropServices;
using Shammy.Core.Devices;
using Shammy.Infrastructure;

namespace Shammy.SystemInfo;

/// <summary>
/// Reads DEVPKEY_Bluetooth_Battery from every present Bluetooth device node through SetupAPI, the same value
/// Windows Settings shows under a connected headset. One pass over the BTHENUM/BTHLE nodes takes a few
/// milliseconds; run it off the UI thread, and only when someone is looking (or a headset just connected).
/// </summary>
internal static class BluetoothBatteryReader
{
    const uint DIGCF_PRESENT = 0x2, DIGCF_ALLCLASSES = 0x4;
    static readonly IntPtr InvalidHandle = new(-1);

    // Classic headsets hang their profiles off BTHENUM (the level is on the hands-free node); LE devices off BTHLE.
    static readonly string[] Enumerators = { "BTHENUM", "BTHLE" };

    /// <summary>Every node that reports a level, as (instance id, percent).</summary>
    public static List<(string InstanceId, int Percent)> ReadAll()
    {
        var found = new List<(string, int)>();
        var key = new DEVPROPKEY { fmtid = BluetoothBattery.PropertySet, pid = BluetoothBattery.PropertyId };
        var value = new byte[8];
        var id = new char[512];
        foreach (var enumerator in Enumerators)
        {
            var set = SetupDiGetClassDevs(IntPtr.Zero, enumerator, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
            if (set == InvalidHandle || set == IntPtr.Zero) continue;
            try
            {
                var data = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
                for (uint i = 0; SetupDiEnumDeviceInfo(set, i, ref data); i++)
                {
                    if (!SetupDiGetDeviceProperty(set, ref data, ref key, out uint type, value, (uint)value.Length, out uint size, 0)) continue;
                    if (BluetoothBattery.ParsePercent(type, value.AsSpan(0, (int)Math.Min(size, (uint)value.Length))) is not { } percent) continue;
                    Array.Clear(id);
                    if (!SetupDiGetDeviceInstanceId(set, ref data, id, id.Length, out _)) continue;
                    int length = Array.IndexOf(id, '\0');
                    if (length > 0) found.Add((new string(id, 0, length), percent));
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Reading Bluetooth battery levels failed", ex);
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(set);
            }
        }
        return found;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DEVPROPKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetClassDevsW")]
    static extern IntPtr SetupDiGetClassDevs(IntPtr classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true, EntryPoint = "SetupDiGetDevicePropertyW")]
    static extern bool SetupDiGetDeviceProperty(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, ref DEVPROPKEY propertyKey,
        out uint propertyType, byte[] propertyBuffer, uint propertyBufferSize, out uint requiredSize, uint flags);

    // The buffer is UTF-16 text written by the W function: CharSet.Unicode marshals char[] as two bytes per char.
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetDeviceInstanceIdW")]
    static extern bool SetupDiGetDeviceInstanceId(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, char[] deviceInstanceId,
        int deviceInstanceIdSize, out int requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);
}
