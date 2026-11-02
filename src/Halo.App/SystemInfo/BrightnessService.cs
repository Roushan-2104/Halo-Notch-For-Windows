using System.Runtime.InteropServices;
using System.Windows.Threading;
using Halo.Infrastructure;

namespace Halo.SystemInfo;

/// <summary>
/// Built-in display brightness changes, via the power-setting notification Windows raises when the
/// laptop panel's brightness changes. External monitors don't report brightness this way, so on a
/// desktop this simply never fires.
/// </summary>
/// <remarks>
/// This used to be a WMI event watcher. System.Management drags the .NET Framework's WMI glue and CRT
/// into the process, and crash dumps of a heap corruption pointed at that CRT, so it is gone.
/// </remarks>
internal sealed class BrightnessService : IDisposable
{
    // GUID_VIDEO_CURRENT_MONITOR_BRIGHTNESS: the panel's brightness in percent, sent on every change.
    static readonly Guid CurrentBrightness = new("8ffee2c6-2d01-46be-adb9-398addc5b4ff");

    const uint DEVICE_NOTIFY_CALLBACK = 2;
    const uint PBT_POWERSETTINGCHANGE = 0x8013;

    readonly Dispatcher _ui;
    readonly Callback _callback;
    GCHandle _params;
    IntPtr _registration;
    int _last = -1;

    public BrightnessService(Dispatcher ui)
    {
        _ui = ui;
        _callback = OnNotify;
    }

    public event Action<float>? Changed;

    public bool IsSupported { get; private set; }

    public void Start()
    {
        var parameters = new DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS { Callback = Marshal.GetFunctionPointerForDelegate(_callback) };
        _params = GCHandle.Alloc(parameters, GCHandleType.Pinned);
        var guid = CurrentBrightness;
        uint err = PowerSettingRegisterNotification(ref guid, DEVICE_NOTIFY_CALLBACK, _params.AddrOfPinnedObject(), out _registration);
        if (err != 0)
        {
            Log.Info($"Brightness events unavailable (error {err})");
            _params.Free();
            return;
        }
        IsSupported = true;
    }

    uint OnNotify(IntPtr context, uint type, IntPtr setting)
    {
        try
        {
            if (type != PBT_POWERSETTINGCHANGE || setting == IntPtr.Zero) return 0;
            // POWERBROADCAST_SETTING: GUID (16 bytes), DWORD DataLength, then the data (a DWORD percent).
            if (Marshal.ReadInt32(setting, 16) < 4) return 0;
            int level = Math.Clamp(Marshal.ReadInt32(setting, 20), 0, 100);
            // Registering fires once with the current value; that is not a change worth a HUD.
            int previous = Interlocked.Exchange(ref _last, level);
            if (previous < 0 || previous == level) return 0;
            _ui.BeginInvoke(() => Changed?.Invoke(level / 100f));
        }
        catch (Exception)
        {
            // Never let an exception escape into the native callback.
        }
        return 0;
    }

    public void Dispose()
    {
        if (_registration != IntPtr.Zero)
        {
            PowerSettingUnregisterNotification(_registration);
            _registration = IntPtr.Zero;
        }
        if (_params.IsAllocated) _params.Free();
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    delegate uint Callback(IntPtr context, uint type, IntPtr setting);

    [StructLayout(LayoutKind.Sequential)]
    struct DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS
    {
        public IntPtr Callback;
        public IntPtr Context;
    }

    [DllImport("powrprof.dll")]
    static extern uint PowerSettingRegisterNotification(ref Guid settingGuid, uint flags, IntPtr recipient, out IntPtr registrationHandle);

    [DllImport("powrprof.dll")]
    static extern uint PowerSettingUnregisterNotification(IntPtr registrationHandle);
}
